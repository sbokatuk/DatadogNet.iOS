using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace DatadogNet.iOS.PackageTests;

/// <summary>
/// Asserts, at the Mach-O level, that a consuming app can link the classes these packages bind -
/// for a real device, not just the simulator.
/// </summary>
/// <remarks>
/// dd-sdk-ios builds its prebuilt device slices with a 12.0 deployment target, below which Swift
/// withholds the static <c>_OBJC_CLASS_$_&lt;Name&gt;</c> registration for classes whose metadata
/// needs runtime fix-ups. The simulator slices (built at 14.0) export those symbols, so the gap
/// is invisible to every simulator build and surfaces only as "Undefined symbols for architecture
/// arm64" in a consumer's device link - which is exactly how 3.14.0 shipped broken.
/// build/GenerateDeviceClassAliases.sh repairs each missing name with a linker alias to the
/// class's exported Swift metadata symbol, and Datadog.Binding.props ships the flags inside each
/// package. These tests hold the three parts together: the bound API, the shipped binaries, and
/// the shipped alias flags.
/// </remarks>
public class SymbolAuditTests
{
    /// <summary>
    /// The payload is identical across target frameworks (asserted by
    /// <see cref="PackageLayoutTests.Native_payload_is_the_same_across_target_frameworks"/>),
    /// so the audit runs against one of them.
    /// </summary>
    private const string PayloadTargetFramework = "net8.0-ios18.0";

    private const string ClassSymbolPrefix = "_OBJC_CLASS_$_";

    [Theory]
    [MemberData(nameof(Packages.BindingNames), MemberType = typeof(Packages))]
    public void Every_class_a_simulator_link_resolves_also_resolves_for_a_device_link(string name)
    {
        var spec = Packages.Spec(name);
        var framework = spec.Framework!;  // BindingNames excludes the meta-package

        using var package = Packages.OpenPackage(name);
        using var payload = Packages.OpenNativePayload(package, name, PayloadTargetFramework);

        var device = ExportedSymbols(payload, framework, simulator: false);
        var simulator = ExportedSymbols(payload, framework, simulator: true);
        var aliases = ShippedAliases(payload);

        // The classes the binding registers are the classes a consumer's static registrar may
        // reference; which of them it actually reaches depends on the app, so all of them must
        // resolve. "Resolvable on the simulator" is the baseline rather than "bound", because a
        // handful of bound classes have never been exported by either slice in any upstream
        // release - a pre-existing upstream condition this repository cannot repair with aliases,
        // and one no consumer can have a working dependency on.
        var unreachable = RegisteredClasses(package, name)
            .Where(cls => simulator.Contains(ClassSymbolPrefix + cls))
            .Where(cls => !device.Contains(ClassSymbolPrefix + cls))
            .Where(cls => !aliases.ContainsKey(ClassSymbolPrefix + cls))
            .OrderBy(cls => cls, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unreachable.Count == 0,
            $"{Packages.PackageId(name)} binds classes a device link cannot resolve: " +
            $"{string.Join(", ", unreachable)}. The upstream device slice does not export them " +
            "and no alias covers them - run ./build/GenerateDeviceClassAliases.sh and repack.");
    }

    [Theory]
    [MemberData(nameof(Packages.BindingNames), MemberType = typeof(Packages))]
    public void Shipped_aliases_match_the_shipped_device_slice(string name)
    {
        var spec = Packages.Spec(name);
        var framework = spec.Framework!;  // BindingNames excludes the meta-package

        using var package = Packages.OpenPackage(name);
        using var payload = Packages.OpenNativePayload(package, name, PayloadTargetFramework);

        var device = ExportedSymbols(payload, framework, simulator: false);

        foreach (var (objcName, swiftMetadata) in ShippedAliases(payload))
        {
            // An alias whose target is gone means the Swift mangled names moved - a new native
            // version was fetched without regenerating - and every consumer's device link would
            // fail on the target instead of the class.
            Assert.True(
                device.Contains(swiftMetadata),
                $"{Packages.PackageId(name)} aliases {objcName} to {swiftMetadata}, which the " +
                "device slice does not export. The aliases are stale for these binaries - " +
                "run ./build/GenerateDeviceClassAliases.sh and repack.");

            // An alias for a symbol the device slice now exports itself means upstream fixed its
            // release build; the alias would shadow the real export. Regenerating removes it.
            Assert.False(
                device.Contains(objcName),
                $"{Packages.PackageId(name)} aliases {objcName}, but the device slice now exports " +
                "it directly - run ./build/GenerateDeviceClassAliases.sh and repack.");
        }
    }

    [Theory]
    [MemberData(nameof(Packages.BindingNames), MemberType = typeof(Packages))]
    public void Packages_with_aliases_ship_the_realization_library(string name)
    {
        var spec = Packages.Spec(name);
        var framework = spec.Framework!;  // BindingNames excludes the meta-package

        using var package = Packages.OpenPackage(name);
        using var payload = Packages.OpenNativePayload(package, name, PayloadTargetFramework);

        var aliases = ShippedAliases(payload);
        var archives = payload.Entries
            .Where(entry => entry.FullName.StartsWith($"{framework}Realize.xcframework/", StringComparison.Ordinal))
            .Where(entry => entry.FullName.EndsWith(".a", StringComparison.Ordinal))
            .ToList();

        if (aliases.Count == 0)
        {
            Assert.True(
                archives.Count == 0,
                $"{Packages.PackageId(name)} ships {framework}Realize.xcframework but no aliases - " +
                "a stale companion; run ./build/GenerateDeviceClassAliases.sh and repack.");
            return;
        }

        // Aliases alone make the classes link; the metadata they point at starts out unrealized,
        // and the static registrar messages every mapped class from main() - so each package with
        // aliases must also carry the dyld-initializer archive that realizes them before main
        // (a cold message to unrealized Swift class metadata is a segfault, measured on
        // hardware). Its device slice must call exactly the metadata accessors of the aliased
        // classes, or startup either crashes or realizes the wrong set.
        var device = archives.SingleOrDefault(entry =>
            !entry.FullName.Contains("simulator", StringComparison.Ordinal));
        Assert.True(
            device is not null,
            $"{Packages.PackageId(name)} ships aliases but no device slice in " +
            $"{framework}Realize.xcframework - the aliased classes would link and then crash at " +
            "startup. Run ./build/GenerateDeviceClassAliases.sh and repack.");

        var expected = aliases.Values
            .Select(symbol => symbol[..^1] + "Ma")  // _$s…CN -> _$s…CMa
            .OrderBy(symbol => symbol, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, UndefinedSymbols(device!));
    }

    /// <summary>
    /// The Objective-C class names the binding assembly registers - the names a consuming app's
    /// static registrar can emit hard <c>_OBJC_CLASS_$_</c> references for.
    /// </summary>
    /// <remarks>
    /// Read from the compiled assembly's <c>[Register]</c> attributes rather than from
    /// ApiDefinitions.cs, so <c>Name =</c> overrides and generator behaviour are the truth being
    /// audited. <c>[Model]</c> and <c>[Protocol]</c> types are skipped: their managed classes are
    /// registrar-provided skeletons, not references into the native binary.
    /// </remarks>
    private static List<string> RegisteredClasses(ZipArchive package, string name)
    {
        using var stream = Packages.ReadEntry(
            package, $"lib/{PayloadTargetFramework}/{Packages.AssemblyName(name)}.dll");
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        buffer.Position = 0;

        using var pe = new PEReader(buffer);
        var metadata = pe.GetMetadataReader();

        var classes = new List<string>();
        foreach (var handle in metadata.TypeDefinitions)
        {
            var type = metadata.GetTypeDefinition(handle);
            if ((type.Attributes & TypeAttributes.Interface) != 0)
            {
                continue;
            }

            string? registered = null;
            var skip = false;

            foreach (var attributeHandle in type.GetCustomAttributes())
            {
                var attribute = metadata.GetCustomAttribute(attributeHandle);
                switch (AttributeName(metadata, attribute))
                {
                    case "Foundation.ModelAttribute":
                    case "Foundation.ProtocolAttribute":
                        skip = true;
                        break;

                    case "Foundation.RegisterAttribute":
                        var value = attribute.DecodeValue(AttributeTypeProvider.Instance);
                        if (value.FixedArguments.Length > 0 &&
                            value.FixedArguments[0].Value is string objcName)
                        {
                            registered = objcName;
                        }

                        if (value.NamedArguments.Any(argument =>
                                argument.Name == "SkipRegistration" && argument.Value is true))
                        {
                            skip = true;
                        }

                        break;
                }
            }

            if (registered is not null && !skip)
            {
                classes.Add(registered);
            }
        }

        return classes;
    }

    private static string? AttributeName(MetadataReader metadata, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                if (member.Parent.Kind != HandleKind.TypeReference)
                {
                    return null;
                }

                var reference = metadata.GetTypeReference((TypeReferenceHandle)member.Parent);
                return $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}";

            case HandleKind.MethodDefinition:
                var method = metadata.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                var declaring = metadata.GetTypeDefinition(method.GetDeclaringType());
                return $"{metadata.GetString(declaring.Namespace)}.{metadata.GetString(declaring.Name)}";

            default:
                return null;
        }
    }

    /// <summary>
    /// The defined external symbols of a payload slice's arm64 binary, via <c>nm</c>. The
    /// packages are produced on macOS with Xcode, so the audit running there too is not a new
    /// requirement.
    /// </summary>
    private static HashSet<string> ExportedSymbols(ZipArchive payload, string framework, bool simulator)
    {
        var slices = payload.Entries
            .Select(entry => entry.FullName.Split('/'))
            .Where(parts => parts.Length > 2 && parts[0] == $"{framework}.xcframework")
            .Select(parts => parts[1])
            .Where(Packages.IsIosSlice)
            .Distinct()
            .ToList();

        var slice = slices.SingleOrDefault(s => Packages.IsSimulatorSlice(s) == simulator);
        Assert.True(slice is not null, $"{framework}.xcframework has no {(simulator ? "simulator" : "device")} slice.");

        var binary = payload.GetEntry($"{framework}.xcframework/{slice}/{framework}.framework/{framework}");
        Assert.True(binary is not null, $"{framework}.xcframework/{slice} has no framework binary.");

        var extracted = Path.Combine(Path.GetTempPath(), $"symbol-audit-{Guid.NewGuid():N}");
        try
        {
            using (var source = binary!.Open())
            using (var destination = File.Create(extracted))
            {
                source.CopyTo(destination);
            }

            var nm = Process.Start(new ProcessStartInfo
            {
                FileName = "xcrun",
                ArgumentList = { "nm", "-arch", "arm64", "-gU", extracted },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var output = nm.StandardOutput.ReadToEnd();
            var errors = nm.StandardError.ReadToEnd();
            nm.WaitForExit();
            Assert.True(nm.ExitCode == 0, $"nm failed on {framework}/{slice}: {errors}");

            var symbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3)
                {
                    symbols.Add(parts[2]);
                }
            }

            return symbols;
        }
        finally
        {
            File.Delete(extracted);
        }
    }

    /// <summary>The undefined symbols of an archive entry's arm64 slice, via <c>nm -u</c>.</summary>
    private static List<string> UndefinedSymbols(ZipArchiveEntry archive)
    {
        var extracted = Path.Combine(Path.GetTempPath(), $"symbol-audit-{Guid.NewGuid():N}.a");
        try
        {
            using (var source = archive.Open())
            using (var destination = File.Create(extracted))
            {
                source.CopyTo(destination);
            }

            var nm = Process.Start(new ProcessStartInfo
            {
                FileName = "xcrun",
                ArgumentList = { "nm", "-arch", "arm64", "-u", extracted },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;

            var output = nm.StandardOutput.ReadToEnd();
            var errors = nm.StandardError.ReadToEnd();
            nm.WaitForExit();
            Assert.True(nm.ExitCode == 0, $"nm failed on {archive.FullName}: {errors}");

            return output.Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.EndsWith(':'))
                .OrderBy(symbol => symbol, StringComparer.Ordinal)
                .ToList();
        }
        finally
        {
            File.Delete(extracted);
        }
    }

    /// <summary>
    /// The <c>-Wl,-alias,&lt;swift metadata&gt;,&lt;objc class&gt;</c> pairs the package actually
    /// ships, read from the binding manifest inside the payload - the same place a consuming
    /// app's build reads them from.
    /// </summary>
    private static Dictionary<string, string> ShippedAliases(ZipArchive payload)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        var manifest = payload.GetEntry("manifest");
        if (manifest is null)
        {
            return aliases;
        }

        using var stream = manifest.Open();
        var document = XDocument.Load(stream);

        var flags = document.Descendants("LinkerFlags").Select(element => element.Value);
        foreach (var token in flags.SelectMany(value => value.Split(' ', StringSplitOptions.RemoveEmptyEntries)))
        {
            var parts = token.Split(',');
            if (parts is ["-Wl", "-alias", var swiftMetadata, var objcName])
            {
                aliases[objcName] = swiftMetadata;
            }
        }

        return aliases;
    }

    private sealed class AttributeTypeProvider : ICustomAttributeTypeProvider<string>
    {
        public static readonly AttributeTypeProvider Instance = new();

        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();

        public string GetSystemType() => "System.Type";

        public string GetSZArrayType(string elementType) => elementType + "[]";

        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeDefinition(handle).Name);

        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            reader.GetString(reader.GetTypeReference(handle).Name);

        public string GetTypeFromSerializedName(string name) => name;

        public PrimitiveTypeCode GetUnderlyingEnumType(string type) => PrimitiveTypeCode.Int32;

        public bool IsSystemType(string type) => type == "System.Type";
    }
}
