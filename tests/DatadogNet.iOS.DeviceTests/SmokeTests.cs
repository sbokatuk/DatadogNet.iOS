#nullable enable
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using DatadogCore;
using DatadogCrashReporting;
using DatadogInternal;
using DatadogLogs;
using DatadogRUM;
using DatadogSessionReplay;
using DatadogTrace;
using DatadogWebViewTracking;
using Foundation;
using ObjCRuntime;
using UIKit;

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
/// Nothing here reaches Datadog. The client token is fake and every feature is pointed at a custom
/// endpoint on localhost, so the SDK batches events to disk and its uploads fail locally rather
/// than sending junk to a real intake from CI.
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

    /// <summary>Counts mapper invocations, incremented from the blocks registered in <see cref="EnablesRum"/>.</summary>
    private static int viewEventsMapped;
    private static int actionEventsMapped;
    private static int logEventsMapped;

    public static SmokeTest[] All =>
    [
        new("every native framework is linked and loadable", EveryFrameworkIsLinked),
        new("initializes the SDK", InitializesTheSdk),
        new("sets verbosity, consent, user and account info", SetsSdkLevelState),
        new("enables RUM", EnablesRum),
        new("drives a RUM view, action and error", DrivesRum),
        new("propagates view-level attributes to child events", ViewAttributesPropagate),
        new("enables Logs and writes every level", EnablesLogsAndWritesEveryLevel),
        new("enables Trace", EnablesTrace),
        new("enables Session Replay", EnablesSessionReplay),
        new("applies per-view Session Replay privacy overrides", SessionReplayPrivacyOverridesApply),
        new("enables crash reporting", EnablesCrashReporting),
        new("exposes WebView tracking", ExposesWebViewTracking),
        new("instruments a URLSession delegate by type", InstrumentsUrlSessionByType),
        new("drives RUM and Logs through the ergonomic overloads", ErgonomicOverloadsWork),
        new("invokes a RUM event mapper", RumEventMapperIsInvoked),
        new("invokes a Logs event mapper and redacts a message", LogsEventMapperRedacts),
        new("stops the RUM session and the SDK instance", StopsCleanly),
    ];

    /// <summary>Every framework the packages ship. All eleven are dynamic in 3.x.</summary>
    /// <remarks>
    /// CrashReporter is absent from this list because it no longer exists: dd-sdk-ios 3.0 replaced
    /// PLCrashReporter with KSCrash, which is linked into DatadogCrashReporting rather than shipped
    /// as a framework of its own. That also removes the one static-archive special case the 2.x
    /// bindings had to carry.
    /// </remarks>
    private static readonly string[] Frameworks =
    [
        "DatadogCore", "DatadogCrashReporting", "DatadogFlags", "DatadogInternal", "DatadogLogs",
        "DatadogProfiling", "DatadogRUM", "DatadogSessionReplay", "DatadogTrace",
        "DatadogWebViewTracking", "OpenTelemetryApi",
    ];

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern uint _dyld_image_count();

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr _dyld_get_image_name(uint index);

    /// <summary>
    /// Proves each of the eleven xcframeworks actually made it into the app and was loaded.
    /// </summary>
    /// <remarks>
    /// This is the check that catches a packaging regression the compiler cannot see. A binding
    /// assembly reaches its native framework only through selector strings, so a package whose
    /// .resources.zip was empty, or whose xcframework manifest advertised a slice that had been
    /// stripped, still compiles and links - and then fails at runtime the first time a type is
    /// touched. DatadogFlags, DatadogProfiling and OpenTelemetryApi bind no managed types at all,
    /// so for those there is no C# type whose use would reveal the problem.
    /// </remarks>
    private static void EveryFrameworkIsLinked()
    {
        var images = new List<string>();
        var count = _dyld_image_count();
        for (uint i = 0; i < count; i++)
        {
            var name = Marshal.PtrToStringUTF8(_dyld_get_image_name(i));
            if (name is not null)
            {
                images.Add(name);
            }
        }

        var missing = Frameworks
            .Where(framework => !images.Any(image =>
                image.EndsWith($"/{framework}.framework/{framework}", StringComparison.Ordinal)))
            .ToList();

        Assert(
            missing.Count == 0,
            $"these frameworks were not loaded into the process: {string.Join(", ", missing)}");

        // KSCrash is statically linked into DatadogCrashReporting rather than shipped separately,
        // so it never appears as an image. Resolving one of its classes is what proves it arrived.
        Assert(
            Class.GetHandle("KSCrash") != IntPtr.Zero,
            "KSCrash did not link: the KSCrash class does not exist.");

        Report($"all {Frameworks.Length} frameworks loaded, KSCrash statically linked");
    }

    private static void InitializesTheSdk()
    {
        var configuration = new DDConfiguration(clientToken: ClientToken, env: "e2e")
        {
            Service = "datadognet-ios-devicetests",
            Site = DDSite.Us1(),
        };

        DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted());

        Assert(DDDatadog.IsInitialized(), "DDDatadog.IsInitialized was false after initialization.");
        Report($"initialized service={configuration.Service} env={configuration.Env}");
    }

    private static void SetsSdkLevelState()
    {
        DDDatadog.SetVerbosityLevel(DDCoreLoggerLevel.Debug);

        DDDatadog.SetTrackingConsent(TrackingConsent.Pending);
        DDDatadog.SetTrackingConsent(TrackingConsent.Granted);

        DDDatadog.SetUserInfo("e2e-user", "E2E User", "e2e@example.invalid");
        DDDatadog.AddUserExtraInfo(new Dictionary<string, object?> { ["origin"] = "device-tests" });

        // Account info arrived in 2.29.0 and propagates to Logs, RUM and Trace.
        DDDatadog.SetAccountInfo("acct-1", "Test Account");
        DDDatadog.ClearAccountInfo();
        DDDatadog.ClearUserInfo();

        Report("verbosity, consent, user and account info all accepted");
    }

    private static void EnablesRum()
    {
        var configuration = new DDRUMConfiguration(applicationID: RumApplicationId)
        {
            SessionSampleRate = 100,
            TrackFrustrations = true,
            TrackBackgroundEvents = true,
            // New in 3.0: reports memory warnings as RUM errors.
            TrackMemoryWarnings = true,
            CustomEndpoint = LocalEndpoint,
            UiKitViewsPredicate = new DDDefaultUIKitRUMViewsPredicate(),
            UiKitActionsPredicate = new DDDefaultUIKitRUMActionsPredicate(),
        };

        // Registered before EnableWith, which is the only point at which mappers can be set.
        configuration.SetViewEventMapper(view =>
        {
            Interlocked.Increment(ref viewEventsMapped);
            return view;
        });

        configuration.SetActionEventMapper(action =>
        {
            Interlocked.Increment(ref actionEventsMapped);
            return action;
        });

        DDRUM.EnableWith(configuration);

        Assert(DDRUMMonitor.Shared() is not null, "DDRUMMonitor.Shared was null after enabling RUM.");
        Report($"RUM enabled for application {configuration.ApplicationID}");
    }

    private static void DrivesRum()
    {
        var monitor = DDRUMMonitor.Shared();
        var attributes = DatadogAttributes.Empty;

        monitor.StartViewWithKey("e2e-view", "E2E View", attributes);
        monitor.AddActionWithType(DDRUMActionType.Tap, "e2e-action", attributes);
        monitor.AddErrorWithMessage("e2e-error", null, DDRUMErrorSource.Source, attributes);

        monitor.StartResourceWithResourceKey("e2e-resource", DDRUMMethod.Get, "https://example.invalid/thing", attributes);
        monitor.StopResourceWithResourceKey("e2e-resource", new NSNumber(200), DDRUMResourceType.Fetch, new NSNumber(0), attributes);

        monitor.StopViewWithKey("e2e-view", attributes);

        Report("started and stopped a view with an action, error and resource");
    }

    /// <summary>
    /// The view-attribute APIs added in 3.0, which are the headline RUM change of the release.
    /// </summary>
    private static void ViewAttributesPropagate()
    {
        var monitor = DDRUMMonitor.Shared();

        using (monitor.StartView("attributed-view"))
        {
            monitor.AddViewAttributeForKey("checkout.step", new NSString("payment"));
            monitor.AddViewAttributes(new Dictionary<string, object?>
            {
                ["cart.size"] = 3,
                ["cart.currency"] = "GBP",
            });

            // Recorded while the attributes are set, so upstream attaches them to this action too -
            // that propagation is the whole point of the new API.
            monitor.AddAction(DDRUMActionType.Tap, "pay");

            monitor.RemoveViewAttributeForKey("checkout.step");
            monitor.RemoveViewAttributes("cart.size", "cart.currency");
        }

        Report("view attributes added, inherited by a child action, and removed");
    }

    private static void EnablesLogsAndWritesEveryLevel()
    {
        var logsConfiguration = new DDLogsConfiguration(LocalEndpoint);
        logsConfiguration.SetEventMapper(logEvent =>
        {
            Interlocked.Increment(ref logEventsMapped);
            if (logEvent.Message == "e2e redact me")
            {
                logEvent.Message = "[redacted]";
            }

            return logEvent;
        });

        DDLogs.EnableWith(logsConfiguration);

        var logger = DDLogger.Create(name: "e2e", printLogsToConsole: false);
        Assert(logger is not null, "DDLogger.Create returned null.");

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

        // The header writers are what an app touches directly. All three lost their sampling
        // argument in 3.0 - sampling is now derived from the RUM session id so that a trace and its
        // session agree - and DDOTelHTTPHeadersWriter was replaced by DDW3CHTTPHeadersWriter.
        var datadogWriter = new DDHTTPHeadersWriter(DDTraceContextInjection.All);
        var b3Writer = new DDB3HTTPHeadersWriter(DDInjectEncoding.Multiple, DDTraceContextInjection.All);
        var w3cWriter = new DDW3CHTTPHeadersWriter(DDTraceContextInjection.All);

        Assert(datadogWriter.TraceHeaderFields is not null, "Datadog header writer produced no fields.");
        Assert(b3Writer.TraceHeaderFields is not null, "B3 header writer produced no fields.");
        Assert(w3cWriter.TraceHeaderFields is not null, "W3C header writer produced no fields.");

        Report("Trace enabled; Datadog, B3 and W3C header writers all constructed");
    }

    private static void EnablesSessionReplay()
    {
        var configuration = new DDSessionReplayConfiguration(
            replaySampleRate: 100,
            textAndInputPrivacyLevel: DDTextAndInputPrivacyLevel.MaskAll,
            imagePrivacyLevel: DDImagePrivacyLevel.MaskAll,
            touchPrivacyLevel: DDTouchPrivacyLevel.Hide)
        {
            CustomEndpoint = LocalEndpoint,
            StartRecordingImmediately = false,
        };

        DDSessionReplay.EnableWith(configuration);

        Assert(
            configuration.TextAndInputPrivacyLevel == DDTextAndInputPrivacyLevel.MaskAll,
            "textAndInputPrivacyLevel did not round-trip.");

        DDSessionReplay.StartRecording();
        DDSessionReplay.StopRecording();

        Report("Session Replay enabled with fine-grained privacy, then started and stopped");
    }

    private static void SessionReplayPrivacyOverridesApply()
    {
        var view = new UIView();
        var overrides = view.GetDdSessionReplayPrivacyOverrides();

        Assert(overrides is not null, "UIView returned no privacy overrides object.");

        overrides.TextAndInputPrivacy = DDTextAndInputPrivacyLevelOverride.MaskAll;
        overrides.ImagePrivacy = DDImagePrivacyLevelOverride.MaskAll;
        overrides.TouchPrivacy = DDTouchPrivacyLevelOverride.Hide;
        overrides.Hide = new NSNumber(true);

        var again = view.GetDdSessionReplayPrivacyOverrides();
        Assert(
            again.TextAndInputPrivacy == DDTextAndInputPrivacyLevelOverride.MaskAll,
            $"Override did not stick: {again.TextAndInputPrivacy}");
        Assert(again.Hide?.BoolValue == true, "Hide override did not stick.");

        Report("per-view privacy overrides set and read back");
    }

    private static void EnablesCrashReporting()
    {
        // Installs the KSCrash signal handlers. The app is not crashed afterwards - that would take
        // the test host with it - so this proves the framework links and the handler installs, not
        // that a report round-trips to Datadog.
        DDCrashReporter.Enable();

        Report("crash reporting enabled (KSCrash)");
    }

    private static void ExposesWebViewTracking()
    {
        // A WKWebView is not created here: instantiating one spins up the whole web content
        // process, which is slow and flaky in CI for no added coverage.
        Assert(
            Class.GetHandle(typeof(DDWebViewTracking)) != IntPtr.Zero,
            "DDWebViewTracking did not resolve to a native class.");

        Report("DDWebViewTracking is available");
    }

    /// <summary>
    /// The unified URLSession instrumentation that replaced the delegate types in 3.0.
    /// </summary>
    private static void InstrumentsUrlSessionByType()
    {
        DDURLSessionInstrumentation.Enable<E2EUrlSessionDelegate>();
        DDURLSessionInstrumentation.Disable<E2EUrlSessionDelegate>();

        // A type the Objective-C runtime has never heard of must be rejected rather than silently
        // instrumenting nothing, which is what passing IntPtr.Zero to the native API does.
        var rejected = false;
        try
        {
            DDURLSessionInstrumentation.Enable(typeof(string));
        }
        catch (ArgumentException)
        {
            rejected = true;
        }

        Assert(rejected, "An unregistered delegate type was accepted instead of throwing.");
        Report("instrumented and disabled a URLSession delegate by type");
    }

    private static void ErgonomicOverloadsWork()
    {
        var monitor = DDRUMMonitor.Shared();

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

        var logger = DDLogger.Create(name: "ergonomics", printLogsToConsole: false);
        logger.Log(DDLogLevel.Info, "ergonomic info");
        logger.Log(DDLogLevel.Error, "ergonomic error", new InvalidOperationException("boom"));

        IReadOnlyDictionary<string, object?> readOnly =
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["k"] = "v" });
        logger.Log(DDLogLevel.Warn, "ergonomic warn", new Exception("boom"), readOnly);

        Report("view scopes, attribute conversion, logger and consent helpers all behaved");
    }

    /// <summary>
    /// Checks that the RUM event mappers fire, and that an event survives the round trip back into
    /// Swift.
    /// </summary>
    /// <remarks>
    /// Mappers are how an app redacts or drops events before upload, and they are the only part of
    /// the binding that passes a managed delegate into Swift and gets a Swift object back - so if
    /// block marshalling is wrong anywhere it is wrong here, and the failure surfaces as a crash
    /// inside the SDK's event-writing path rather than at the call that registered the mapper.
    /// </remarks>
    private static void RumEventMapperIsInvoked()
    {
        // Mappers run on the SDK's own write queue, so this waits rather than asserting straight
        // away - and keeps producing events while it waits, instead of hoping the ones already
        // queued drain in time. A fixed short deadline over a single batch is what made this flaky:
        // it passed locally in milliseconds and timed out at five seconds on a loaded CI runner
        // whose simulator had taken 46 seconds just to boot.
        var elapsed = WaitForAsyncWork(
            () => viewEventsMapped > 0 && actionEventsMapped > 0,
            nudge: attempt =>
            {
                var monitor = DDRUMMonitor.Shared();
                monitor.StartViewWithKey($"mapper-nudge-{attempt}", "Mapper Nudge", DatadogAttributes.Empty);
                monitor.AddActionWithType(DDRUMActionType.Tap, $"mapper-nudge-{attempt}", DatadogAttributes.Empty);
                monitor.StopViewWithKey($"mapper-nudge-{attempt}", DatadogAttributes.Empty);
            });

        Assert(viewEventsMapped > 0, $"The view event mapper was never invoked (waited {elapsed:0.0}s).");
        Assert(actionEventsMapped > 0, $"The action event mapper was never invoked (waited {elapsed:0.0}s).");

        Report($"mappers invoked after {elapsed:0.0}s: {viewEventsMapped} view, {actionEventsMapped} action");
    }

    private static void LogsEventMapperRedacts()
    {
        var elapsed = WaitForAsyncWork(
            () => logEventsMapped > 0,
            nudge: attempt => DDLogger
                .Create(name: "redaction", printLogsToConsole: false)
                .Log(DDLogLevel.Info, attempt == 0 ? "e2e redact me" : $"e2e mapper nudge {attempt}"));

        Assert(logEventsMapped > 0, $"The Logs event mapper was never invoked (waited {elapsed:0.0}s).");
        Report($"log events mapped after {elapsed:0.0}s: {logEventsMapped}");
    }

    /// <summary>
    /// Waits for an asynchronous SDK side effect, re-driving it periodically, and returns how long
    /// it took.
    /// </summary>
    /// <param name="satisfied">Checked between attempts; the wait ends as soon as it is true.</param>
    /// <param name="nudge">
    /// Produces more of the work being waited on. Called once per second with an increasing
    /// attempt number.
    /// </param>
    /// <remarks>
    /// The generous ceiling is deliberate. Nothing here is expected to take anywhere near it - the
    /// wait ends the moment the condition holds, so a healthy run costs milliseconds - but a CI
    /// runner under contention is far slower than a developer's machine, and a mapper that never
    /// fires is a real defect worth failing on rather than a timeout worth shrugging at.
    /// </remarks>
    private static double WaitForAsyncWork(Func<bool> satisfied, Action<int> nudge)
    {
        var started = DateTime.UtcNow;
        var deadline = started.AddSeconds(30);
        var attempt = 0;

        while (!satisfied())
        {
            if (DateTime.UtcNow >= deadline)
            {
                break;
            }

            nudge(attempt++);

            // Re-checked at a finer interval than it is nudged, so a healthy run returns promptly
            // rather than always paying a full second.
            for (var i = 0; i < 10 && !satisfied(); i++)
            {
                Thread.Sleep(100);
            }
        }

        return (DateTime.UtcNow - started).TotalSeconds;
    }

    private static void StopsCleanly()
    {
        DDRUMMonitor.Shared().StopSession();
        DDDatadog.ClearAllData();
        DDDatadog.StopInstance();

        Assert(!DDDatadog.IsInitialized(), "DDDatadog.IsInitialized was still true after StopInstance.");
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

/// <summary>
/// A minimal URLSession delegate for <see cref="SmokeTests.InstrumentsUrlSessionByType"/>.
/// </summary>
/// <remarks>
/// [Register] matters: the instrumentation is installed on the Objective-C class, so the type has
/// to be visible to that runtime by name.
/// </remarks>
[Register(nameof(E2EUrlSessionDelegate))]
public sealed class E2EUrlSessionDelegate : NSObject, INSUrlSessionDataDelegate
{
}
