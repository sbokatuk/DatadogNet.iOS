using System.IO.Compression;
using System.Xml.Linq;

namespace DatadogNet.iOS.PackageTests;

/// <summary>
/// Locates the packed .nupkg files and describes what each one is supposed to contain.
/// </summary>
public static class Packages
{
    /// <summary>
    /// Every package this repository builds, with the native framework it wraps and the packages
    /// it must declare a dependency on.
    /// </summary>
    /// <remarks>
    /// The dependency column is the real native graph, read off the frameworks' Mach-O load
    /// commands, plus DatadogCore wherever a feature module needs the SDK to have been initialised.
    /// It is asserted here because getting it wrong is invisible until an app fails to link: the
    /// previous bindings declared DatadogInternal as depending on DatadogCore and DatadogTrace,
    /// when in fact both of those depend on DatadogInternal, and nothing caught it.
    /// </remarks>
    public static readonly PackageSpec[] All =
    [
        new("Internal", "DatadogInternal", []),
        new("OpenTelemetryApi", "OpenTelemetryApi", []),
        new("Core", "DatadogCore", ["Internal"]),
        new("Trace", "DatadogTrace", ["Core", "Internal", "OpenTelemetryApi"]),
        new("Logs", "DatadogLogs", ["Core", "Internal"]),
        new("RUM", "DatadogRUM", ["Core", "Internal"]),
        new("SessionReplay", "DatadogSessionReplay", ["Core", "Internal"]),
        new("WebViewTracking", "DatadogWebViewTracking", ["Core", "Internal"]),
        new("CrashReporting", "DatadogCrashReporting", ["Core", "Internal"]),
        new("Flags", "DatadogFlags", ["Core", "Internal"]),
        new("Profiling", "DatadogProfiling", ["Core", "Internal"]),

        // The compatibility meta-package. It wraps no framework and ships no assembly - it exists
        // so an app referencing DatadogNet.Objc.iOS still restores a working set now that
        // dd-sdk-ios has deleted the DatadogObjc framework and spread its types across the product
        // modules.
        new("Objc", Framework: null, ["Core", "Logs", "RUM", "SessionReplay", "Trace"]),
    ];

    /// <summary>Target frameworks every package must carry a binding assembly for.</summary>
    public static readonly string[] ExpectedTargetFrameworks =
    [
        "net8.0-ios18.0", "net9.0-ios18.0", "net10.0-ios26.0",
    ];

    /// <summary>xunit member data: one row per package.</summary>
    public static TheoryData<string> Names
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var package in All)
            {
                data.Add(package.Name);
            }

            return data;
        }
    }

    public static PackageSpec Spec(string name) =>
        All.Single(package => package.Name == name);

    /// <summary>Packages that ship a native framework, i.e. everything but the meta-package.</summary>
    public static TheoryData<string> BindingNames
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var package in All.Where(package => package.Framework is not null))
            {
                data.Add(package.Name);
            }

            return data;
        }
    }

    /// <summary>The dependency-only compatibility package, which has no assembly or payload.</summary>
    public const string MetaPackage = "Objc";

    public static string PackageId(string name) => $"DatadogNet.{name}.iOS";

    /// <summary>The assembly name, which is also the prefix of the native payload entry.</summary>
    public static string AssemblyName(string name) => PackageId(name);

    public static string ResourcesEntry(string name, string tfm) =>
        $"lib/{tfm}/{AssemblyName(name)}.resources.zip";

    /// <summary>
    /// The directory packages are read from. Overridable so the tests can run against a directory
    /// other than the repository's own artifacts/ - a CI job that downloads them, for instance.
    /// </summary>
    public static string ArtifactsDirectory =>
        Environment.GetEnvironmentVariable("DATADOG_ARTIFACTS_DIR") is { Length: > 0 } configured
            ? configured
            : Path.Combine(RepositoryRoot, "artifacts");

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                directory = directory.Parent;
            }

            return directory?.FullName
                ?? throw new InvalidOperationException("Could not locate the repository root.");
        }
    }

    public static ZipArchive OpenPackage(string name, string extension = ".nupkg")
    {
        var id = PackageId(name);
        var matches = Directory.GetFiles(ArtifactsDirectory, $"{id}.*{extension}");

        // Matching on the id prefix would also match a longer id that starts with it. None of the
        // eleven ids is a prefix of another today, but "Core"/"CrashReporter" style collisions are
        // one rename away, so the version is required to look like a version.
        var package = matches.SingleOrDefault(path =>
            Path.GetFileName(path).StartsWith($"{id}.", StringComparison.Ordinal) &&
            char.IsDigit(Path.GetFileName(path)[id.Length + 1]));

        if (package is null)
        {
            throw new FileNotFoundException(
                $"No {id}{extension} in {ArtifactsDirectory}. Run ./build/BuildNugets.sh first.");
        }

        return ZipFile.OpenRead(package);
    }

    /// <summary>Opens the compressed native payload inside a package as an archive of its own.</summary>
    public static ZipArchive OpenNativePayload(ZipArchive package, string name, string tfm)
    {
        var entry = package.GetEntry(ResourcesEntry(name, tfm))
            ?? throw new InvalidOperationException($"{PackageId(name)} has no {ResourcesEntry(name, tfm)}.");

        // Copied to memory first: ZipArchive needs a seekable stream, and the entry stream is not.
        var buffer = new MemoryStream();
        using (var stream = entry.Open())
        {
            stream.CopyTo(buffer);
        }

        buffer.Position = 0;
        return new ZipArchive(buffer, ZipArchiveMode.Read);
    }

    public static Stream ReadEntry(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidOperationException($"Archive has no entry '{path}'.");

        return entry.Open();
    }

    public static XDocument ReadNuspec(ZipArchive package, string name)
    {
        using var stream = ReadEntry(package, $"{PackageId(name)}.nuspec");
        return XDocument.Load(stream);
    }

    /// <summary>
    /// Whether a slice directory name is one of the two iOS slices the packages are meant to ship.
    /// </summary>
    /// <remarks>
    /// Checked by shape rather than against a literal list, because upstream renames these: the
    /// device slice is <c>ios-arm64_arm64e</c> in this release but plain <c>ios-arm64</c> for
    /// DatadogCrashReporting, and 3.x renamed others again. What must never appear is a tvOS,
    /// macOS, macCatalyst, watchOS or visionOS slice - and macCatalyst is the trap, since it is
    /// also named <c>ios-*</c>.
    /// </remarks>
    public static bool IsIosSlice(string slice) =>
        slice.StartsWith("ios-", StringComparison.Ordinal) &&
        !slice.Contains("maccatalyst", StringComparison.Ordinal);

    public static bool IsSimulatorSlice(string slice) =>
        slice.EndsWith("-simulator", StringComparison.Ordinal);
}

/// <summary>What one package is expected to be.</summary>
/// <param name="Name">The middle segment of the id: <c>DatadogNet.<see cref="Name"/>.iOS</c>.</param>
/// <param name="Framework">
/// The native xcframework the package ships, or <see langword="null"/> for a dependency-only
/// meta-package.
/// </param>
/// <param name="DependsOn">The <see cref="Name"/>s of the packages it must depend on.</param>
public sealed record PackageSpec(string Name, string? Framework, string[] DependsOn);
