using System.Collections.ObjectModel;
using CrashReporter;
using DatadogCrashReporting;
using DatadogInternal;
using DatadogObjc;
// DatadogObjc declares its own DDSessionReplay, DDSessionReplayConfiguration and privacy-level
// enum, wrapping different native classes from the identically named ones here
// (_TtC11DatadogObjc15DDSessionReplay against _TtC20DatadogSessionReplay15DDSessionReplay). Both
// work; importing both namespaces unqualified is what does not. These tests deliberately drive the
// DatadogSessionReplay ones, so that package's own binding is exercised rather than only the
// façade's - the sample shows the DatadogObjc route an app would normally take.
using SessionReplay = DatadogSessionReplay;
using DatadogWebViewTracking;
using Foundation;
using ObjCRuntime;

namespace DatadogNet.iOS.DeviceTests;

/// <summary>A single on-simulator check. Throws to fail.</summary>
/// <param name="Name">Human readable name, reported to stdout.</param>
/// <param name="Execute">Runs the check.</param>
public sealed record SmokeTest(string Name, Action Execute);

/// <summary>
/// End-to-end checks that only mean anything on a real device or simulator: they load the native
/// Datadog frameworks out of the packaged xcframeworks and drive the real SDK.
/// </summary>
/// <remarks>
/// Nothing here reaches Datadog. The client token is fake and every feature is pointed at a
/// custom endpoint on localhost, so the SDK batches events to disk and its uploads fail locally
/// rather than sending junk to a real intake from CI.
/// <para>
/// The checks are ordered: the SDK has to be initialised before a feature can be enabled, and a
/// feature has to be enabled before it can be driven. A failure early on therefore cascades, which
/// is the intent - the first failure is the informative one.
/// </para>
/// </remarks>
public static class SmokeTests
{
    private const string ClientToken = "fake-client-token-for-e2e-only";
    private const string RumApplicationId = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// Where the SDK is told to upload to. Nothing listens on this port; the SDK retries in the
    /// background and never throws, which is exactly the isolation wanted here.
    /// </summary>
    private static readonly NSUrl LocalEndpoint = new("http://localhost:9/");

    public static Action<string> Reporter { get; set; } = _ => { };

    private static void Report(string message) => Reporter(message);

    public static SmokeTest[] All =>
    [
        new("every native framework is linked and loadable", EveryFrameworkIsLinked),
        new("initializes the SDK", InitializesTheSdk),
        new("sets verbosity, consent, user and account info", SetsSdkLevelState),
        new("enables RUM", EnablesRum),
        new("drives a RUM view, action and error", DrivesRum),
        new("enables Logs and writes every level", EnablesLogsAndWritesEveryLevel),
        new("enables Trace", EnablesTrace),
        new("enables Session Replay", EnablesSessionReplay),
        new("enables crash reporting", EnablesCrashReporting),
        new("constructs a URLSession delegate for first-party tracing", ConstructsUrlSessionDelegate),
        new("exposes WebView tracking", ExposesWebViewTracking),
        new("exposes PLCrashReporter", ExposesCrashReporter),
        new("drives RUM and Logs through the ergonomic overloads", ErgonomicOverloadsWork),
        new("stops the RUM session and the SDK instance", StopsCleanly),
    ];

    /// <summary>
    /// Exercises the hand-written convenience layer, which the generated binding knows nothing
    /// about and which no other check would touch.
    /// </summary>
    private static void ErgonomicOverloadsWork()
    {
        var monitor = DDRUMMonitor.Shared;

        // The scope form: the view is stopped when the using block is left, whatever happens in it.
        using (var view = monitor.StartView("ergonomic-view", "Ergonomic View"))
        {
            Assert(view.Key == "ergonomic-view", $"Scope reported the wrong key: {view.Key}");

            monitor.AddAction(DDRUMActionType.Tap, "ergonomic-action", new Dictionary<string, object?>
            {
                ["string"] = "text",
                ["int"] = 42,
                ["double"] = 1.5,
                ["bool"] = true,
                ["null"] = null,
                ["date"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ["list"] = new[] { 1, 2, 3 },
                ["nested"] = new Dictionary<string, object?> { ["inner"] = "value" },
            });

            monitor.AddError(new InvalidOperationException("ergonomic failure"));
        }

        // Disposing a scope whose view was already stopped by hand must not stop a second time.
        var second = monitor.StartView("ergonomic-view-2");
        monitor.StopView("ergonomic-view-2");
        second.Dispose();

        // An attribute type with no Objective-C representation must be rejected loudly rather than
        // silently dropped, since a missing attribute is invisible until someone queries for it.
        var rejected = false;
        try
        {
            DatadogAttributes.From(new Dictionary<string, object?> { ["bad"] = new object() });
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert(rejected, "An unconvertible attribute value was accepted instead of throwing.");

        // The logger convenience form, including the exception overload that folds error.kind,
        // error.message and error.stack into the payload.
        var logger = DDLogger.Create(name: "ergonomics", printLogsToConsole: false);
        logger.Log(DDLogLevel.Info, "ergonomic info");
        logger.Log(DDLogLevel.Error, "ergonomic error", new InvalidOperationException("boom"));

        // A read-only dictionary that is deliberately not an IDictionary, which is what the naive
        // copy in the exception path used to throw on.
        IReadOnlyDictionary<string, object?> readOnly =
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["k"] = "v" });
        logger.Log(DDLogLevel.Warn, "ergonomic warn", new Exception("boom"), readOnly);

        DDDatadog.SetUserInfo("ergonomic-user");
        DDDatadog.SetTrackingConsent(TrackingConsent.Granted);

        Report("view scopes, attribute conversion, logger and consent helpers all behaved");
    }

    /// <summary>Every framework shipped as a dynamic framework, which is all but CrashReporter.</summary>
    private static readonly string[] DynamicFrameworks =
    [
        "DatadogCore", "DatadogCrashReporting", "DatadogInternal", "DatadogLogs", "DatadogObjc",
        "DatadogRUM", "DatadogSessionReplay", "DatadogTrace", "DatadogWebViewTracking",
        "OpenTelemetryApi",
    ];

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint _dyld_image_count();

    [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr _dyld_get_image_name(uint index);

    /// <summary>
    /// Proves each of the eleven xcframeworks actually made it into the app and was loaded.
    /// </summary>
    /// <remarks>
    /// This is the check that catches a packaging regression the compiler cannot see. A binding
    /// assembly reaches its native framework only through selector strings, so a package whose
    /// .resources.zip was empty, or whose xcframework manifest advertised a slice that had been
    /// stripped, still compiles and links - and then fails at runtime the first time a type is
    /// touched. Four of the eleven packages (Logs, RUM, Trace, OpenTelemetryApi) bind no managed
    /// types at all, so for those there is no C# type whose use would reveal the problem.
    /// <para>
    /// Asking dyld what is loaded covers all of them uniformly, and does not depend on guessing
    /// Swift's mangled Objective-C class names - which encode the module name and its length, and
    /// are easy to get subtly wrong.
    /// </para>
    /// </remarks>
    private static void EveryFrameworkIsLinked()
    {
        var images = new List<string>();
        var count = _dyld_image_count();
        for (uint i = 0; i < count; i++)
        {
            var name = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(_dyld_get_image_name(i));
            if (name is not null)
            {
                images.Add(name);
            }
        }

        var missing = DynamicFrameworks
            .Where(framework => !images.Any(image =>
                image.EndsWith($"/{framework}.framework/{framework}", StringComparison.Ordinal)))
            .ToList();

        Assert(
            missing.Count == 0,
            $"these frameworks were not loaded into the process: {string.Join(", ", missing)}");

        // CrashReporter is the one framework that ships as a static archive rather than a dynamic
        // framework, so it never appears as a loaded image - its code is linked straight into the
        // app binary. Resolving one of its Objective-C classes is what proves it arrived, and is
        // also what proves ForceLoad did its job: without it the linker drops the archive's
        // Objective-C metadata and this returns null.
        Assert(
            Class.GetHandle("PLCrashReporter") != IntPtr.Zero,
            "CrashReporter did not link: the PLCrashReporter class does not exist.");

        Report($"all {DynamicFrameworks.Length} dynamic frameworks loaded, CrashReporter statically linked");
    }

    private static void InitializesTheSdk()
    {
        var configuration = new DDConfiguration(clientToken: ClientToken, env: "e2e")
        {
            Service = "datadognet-ios-devicetests",
            Site = DDSite.Us1,
        };

        DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted);

        Assert(DDDatadog.IsInitialized, "DDDatadog.IsInitialized was false after initialization.");
        Report($"initialized service={configuration.Service} env={configuration.Env}");
    }

    private static void SetsSdkLevelState()
    {
        DDDatadog.VerbosityLevel = DDSDKVerbosityLevel.Debug;
        Assert(
            DDDatadog.VerbosityLevel == DDSDKVerbosityLevel.Debug,
            $"VerbosityLevel did not round-trip: {DDDatadog.VerbosityLevel}");

        DDDatadog.SetTrackingConsentWithConsent(DDTrackingConsent.Pending);
        DDDatadog.SetTrackingConsentWithConsent(DDTrackingConsent.Granted);

        DDDatadog.SetUserInfoWithId(
            "e2e-user",
            "E2E User",
            "e2e@example.invalid",
            new NSDictionary<NSString, NSObject>());

        DDDatadog.AddUserExtraInfo(
            new NSDictionary<NSString, NSObject>(new NSString("origin"), new NSString("device-tests")));

        Report("verbosity, consent and user info all accepted");
    }

    private static void EnablesRum()
    {
        var configuration = new DDRUMConfiguration(applicationID: RumApplicationId)
        {
            SessionSampleRate = 100,
            TrackFrustrations = true,
            TrackBackgroundEvents = true,
            CustomEndpoint = LocalEndpoint,
            // The default UIKit predicates are what a real app wires up, and constructing them
            // exercises two more bound types.
            UiKitViewsPredicate = new DDDefaultUIKitRUMViewsPredicate(),
            UiKitActionsPredicate = new DDDefaultUIKitRUMActionsPredicate(),
        };

        DDRUM.EnableWith(configuration);

        Assert(DDRUMMonitor.Shared is not null, "DDRUMMonitor.Shared was null after enabling RUM.");
        Report($"RUM enabled for application {configuration.ApplicationID}");
    }

    private static void DrivesRum()
    {
        var monitor = DDRUMMonitor.Shared;
        var attributes = new NSDictionary<NSString, NSObject>();

        monitor.StartViewWithKey("e2e-view", "E2E View", attributes);
        monitor.AddActionWithType(DDRUMActionType.Tap, "e2e-action", attributes);
        monitor.AddErrorWithMessage("e2e-error", null, DDRUMErrorSource.Source, attributes);
        monitor.AddTimingWithName("e2e-timing");

        // A resource is started and stopped so the resource path is exercised too - that is the
        // part of RUM most apps get through URLSession instrumentation rather than by hand.
        monitor.StartResourceWithResourceKey("e2e-resource", DDRUMMethod.Get, "https://example.invalid/thing", attributes);
        monitor.StopResourceWithResourceKey("e2e-resource", new NSNumber(200), DDRUMResourceType.Fetch, new NSNumber(0), attributes);

        monitor.StopViewWithKey("e2e-view", attributes);

        Report("started and stopped a view with an action, error, timing and resource");
    }

    private static void EnablesLogsAndWritesEveryLevel()
    {
        DDLogs.EnableWith(new DDLogsConfiguration(LocalEndpoint));

        // The designated initializer takes all eight settings; there is no parameterless form.
        var logger = DDLogger.CreateWith(new DDLoggerConfiguration(
            service: "datadognet-ios-devicetests",
            name: "e2e",
            networkInfoEnabled: true,
            bundleWithRumEnabled: true,
            bundleWithTraceEnabled: true,
            remoteSampleRate: 100,
            remoteLogThreshold: DDLogLevel.Debug,
            printLogsToConsole: false));

        Assert(logger is not null, "DDLogger.CreateWith returned null.");

        logger.Debug("e2e debug");
        logger.Info("e2e info");
        logger.Notice("e2e notice");
        logger.Warn("e2e warn");
        logger.Error("e2e error");
        logger.Critical("e2e critical");

        logger.AddTagWithKey("suite", "device-tests");
        logger.AddAttributeForKey("attempt", new NSNumber(1));
        logger.RemoveTagWithKey("suite");
        logger.RemoveAttributeForKey("attempt");

        Report("wrote six levels and round-tripped a tag and an attribute");
    }

    private static void EnablesTrace()
    {
        DDTrace.EnableWith(new DDTraceConfiguration
        {
            SampleRate = 100,
            NetworkInfoEnabled = true,
            BundleWithRumEnabled = true,
            CustomEndpoint = LocalEndpoint,
        });

        // The header writers are the part of tracing an app touches directly, and each is bound
        // from a different Swift type - DDOTelHTTPHeadersWriter in particular is the one that only
        // links if OpenTelemetryApi.xcframework made it into the package.
        var sampling = DDTraceSamplingStrategy.CustomWithSampleRate(100);

        var datadogWriter = new DDHTTPHeadersWriter(sampling, DDTraceContextInjection.All);
        var b3Writer = new DDB3HTTPHeadersWriter(sampling, DDInjectEncoding.Multiple, DDTraceContextInjection.All);
        var otelWriter = new DDOTelHTTPHeadersWriter(sampling, DDInjectEncoding.Multiple, DDTraceContextInjection.All);

        Assert(datadogWriter.TraceHeaderFields is not null, "Datadog header writer produced no fields.");
        Assert(b3Writer.TraceHeaderFields is not null, "B3 header writer produced no fields.");
        Assert(otelWriter.TraceHeaderFields is not null, "OpenTelemetry header writer produced no fields.");

        Report("Trace enabled; Datadog, B3 and OpenTelemetry header writers all constructed");
    }

    private static void EnablesSessionReplay()
    {
        var configuration = new SessionReplay.DDSessionReplayConfiguration(replaySampleRate: 100)
        {
            DefaultPrivacyLevel = SessionReplay.DDSessionReplayConfigurationPrivacyLevel.Mask,
            CustomEndpoint = LocalEndpoint,
        };

        SessionReplay.DDSessionReplay.EnableWith(configuration);

        Report($"Session Replay enabled at privacy level {configuration.DefaultPrivacyLevel}");
    }

    private static void EnablesCrashReporting()
    {
        // Enabling installs a signal handler. The app is not crashed afterwards - a crash would
        // take the test host with it - so this proves the framework links and the handler installs,
        // not that a report round-trips to Datadog.
        DDCrashReporter.Enable();

        Report("crash reporting enabled");
    }

    private static void ConstructsUrlSessionDelegate()
    {
        var hosts = new NSSet<NSString>(new NSString("example.invalid"));
        using var internalDelegate = new DatadogURLSessionDelegate(hosts);

        Assert(internalDelegate.Handle != IntPtr.Zero, "DatadogURLSessionDelegate has a null handle.");

        var headerTypes = new NSDictionary<NSString, NSSet<DDTracingHeaderType>>(
            new NSString("example.invalid"),
            new NSSet<DDTracingHeaderType>(DDTracingHeaderType.Datadog));
        using var objcDelegate = new DDNSURLSessionDelegate(headerTypes);

        Assert(objcDelegate.Handle != IntPtr.Zero, "DDNSURLSessionDelegate has a null handle.");

        Report("both URLSession delegates constructed");
    }

    private static void ExposesWebViewTracking()
    {
        // A WKWebView is not created here: instantiating one on a simulator spins up the whole web
        // content process, which is slow and flaky in CI for no added coverage. That the class
        // resolves proves the framework is linked, which is what this package contributes.
        Assert(
            Class.GetHandle(typeof(DDWebViewTracking)) != IntPtr.Zero,
            "DDWebViewTracking did not resolve to a native class.");

        Report("DDWebViewTracking is available");
    }

    private static void ExposesCrashReporter()
    {
        // PLCrashReporter is the engine underneath DatadogCrashReporting, and the one framework in
        // the set that ships as a static archive rather than a dynamic framework - so it is the one
        // most sensitive to the ForceLoad/SmartLink settings on the NativeReference.
        var configuration = new PLCrashReporterConfig(
            PLCrashReporterSignalHandlerType.Bsd,
            PLCrashReporterSymbolicationStrategy.None);

        Assert(configuration.Handle != IntPtr.Zero, "PLCrashReporterConfig has a null handle.");
        Report("PLCrashReporter is available and configurable");
    }

    private static void StopsCleanly()
    {
        DDRUMMonitor.Shared.StopSession();
        DDDatadog.ClearAllData();
        DDDatadog.StopInstance();

        Assert(!DDDatadog.IsInitialized, "DDDatadog.IsInitialized was still true after StopInstance.");
        Report("session stopped, data cleared and instance torn down");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
