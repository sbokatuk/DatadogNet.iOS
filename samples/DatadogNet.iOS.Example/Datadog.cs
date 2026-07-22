using DatadogCrashReporting;
using DatadogObjc;

namespace DatadogNetExample;

/// <summary>
/// All the Datadog setup for this app, in one place.
/// </summary>
/// <remarks>
/// This is the shape most apps end up with: one method called once at startup that configures the
/// SDK and turns on the features the app uses. Everything after that goes through
/// <see cref="Rum"/> and <see cref="Logger"/>.
/// </remarks>
public static class Datadog
{
    /// <summary>
    /// Replace these with your own values from the Datadog UI. The placeholders let the sample run
    /// and report exactly the same way as a configured app; the events simply never arrive.
    /// </summary>
    private const string ClientToken = "<CLIENT_TOKEN>";

    private const string RumApplicationId = "<RUM_APPLICATION_ID>";

    private static DDLogger? logger;

    /// <summary>The RUM monitor, for reporting views, actions and errors.</summary>
    public static DDRUMMonitor Rum => DDRUMMonitor.Shared;

    /// <summary>The app's logger.</summary>
    public static DDLogger Logger =>
        logger ?? throw new InvalidOperationException($"Call {nameof(Initialize)} first.");

    /// <summary>Whether real credentials were supplied, so events actually reach Datadog.</summary>
    public static bool IsConfigured => !ClientToken.StartsWith('<');

    /// <summary>Configures the SDK and enables RUM, Logs, Trace, Session Replay and crash reporting.</summary>
    public static void Initialize()
    {
        var configuration = new DDConfiguration(clientToken: ClientToken, env: "sample")
        {
            Service = "datadognet-ios-example",
            // Pick the site your Datadog organisation is on - Us1, Us3, Us5, Eu1, Ap1 or Us1_fed.
            // Sending to the wrong one is the most common reason events never show up.
            Site = DDSite.Us1,
        };

        // Consent must be set before, or as part of, initialization. Pending collects events but
        // holds them on the device until consent is granted or refused, which is what a GDPR
        // prompt-on-first-launch flow wants.
        DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted);

        // Logs SDK problems to the Xcode console. Worth leaving on while integrating - it is how
        // you find out that, say, the client token is wrong.
        DDDatadog.VerbosityLevel = DDSDKVerbosityLevel.Warn;

        EnableRum();
        EnableLogs();
        EnableTrace();
        EnableSessionReplay();

        // Crash reporting is enabled after the SDK is initialized, never before: it attaches to
        // the RUM and Logs features to report the crash on the next launch.
        DDCrashReporter.Enable();
    }

    private static void EnableRum()
    {
        var rum = new DDRUMConfiguration(applicationID: RumApplicationId)
        {
            // Percentage of sessions kept. 100 while developing; a real app usually samples down.
            SessionSampleRate = 100,
            // Detects rage taps, dead clicks and error taps.
            TrackFrustrations = true,
            TrackBackgroundEvents = true,

            // Automatic UIKit instrumentation. MAUI renders through UIKit, so these report views
            // and taps without any per-page code - the manual StartView calls in MainPage are
            // there to show the explicit API, not because they are required.
            UiKitViewsPredicate = new DDDefaultUIKitRUMViewsPredicate(),
            UiKitActionsPredicate = new DDDefaultUIKitRUMActionsPredicate(),
        };

        DDRUM.EnableWith(rum);
    }

    private static void EnableLogs()
    {
        // The convenience form from this repository's additions. The raw binding equivalent is
        // DDLogs.EnableWith (new DDLogsConfiguration (null)).
        DDLogs.Enable();

        logger = DDLogger.Create(
            name: "example",
            networkInfoEnabled: true,
            // Correlates each log with the RUM view and span active when it was written, so a log
            // line can be opened straight from the session replay.
            bundleWithRumEnabled: true,
            bundleWithTraceEnabled: true,
            printLogsToConsole: true);
    }

    private static void EnableTrace()
    {
        DDTrace.EnableWith(new DDTraceConfiguration
        {
            SampleRate = 100,
            NetworkInfoEnabled = true,
            BundleWithRumEnabled = true,
        });
    }

    private static void EnableSessionReplay()
    {
        // Session Replay requires RUM, so it is enabled last.
        //
        // MaskAll is the default here on purpose. Replay records the screen, and the privacy level
        // decides what is redacted before anything leaves the device: MaskUserInput hides only what
        // the user typed, while Mask also hides all text and images. Loosen this deliberately, not
        // by accident.
        // DDSessionReplay exists in two namespaces: this one, from DatadogNet.Objc.iOS, and
        // another in DatadogNet.SessionReplay.iOS. They are not the same class - each wraps a
        // different native type (_TtC11DatadogObjc15DDSessionReplay against
        // _TtC20DatadogSessionReplay15DDSessionReplay) - but they do the same thing. Prefer the
        // DatadogObjc one, as here, so a single `using DatadogObjc;` covers the whole SDK;
        // importing both namespaces makes the name ambiguous and needs an alias to resolve.
        DDSessionReplay.EnableWith(new DDSessionReplayConfiguration(replaySampleRate: 100)
        {
            DefaultPrivacyLevel = DDSessionReplayConfigurationPrivacyLevel.Mask,
        });
    }
}
