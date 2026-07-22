using Foundation;
using UIKit;

namespace DatadogNet.iOS.DeviceTests;

/// <summary>
/// Host for the on-simulator smoke tests. Runs every check on launch, reports the outcome to
/// stdout - which <c>simctl launch --console-pty</c> streams straight back to CI - and then exits
/// with a verdict line the runner script greps for.
/// </summary>
public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register(nameof(AppDelegate))]
public sealed class AppDelegate : UIApplicationDelegate
{
    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
    {
        // A window is not strictly needed for a headless run, but iOS terminates an app that never
        // presents one, which would look like a crash rather than a test failure.
        var root = new UIViewController();
        root.View!.BackgroundColor = UIColor.SystemBackground;

        Window = new UIWindow(UIScreen.MainScreen.Bounds) { RootViewController = root };
        Window.MakeKeyAndVisible();

        // The checks run on the main thread deliberately, unlike the FFmpegKit equivalent: the
        // Datadog SDK instruments UIKit, and DDRUMMonitor asserts it is reached from the main
        // thread. Nothing here blocks for long enough to trip the watchdog.
        RunAndReport();

        return true;
    }

    private static void RunAndReport()
    {
        SmokeTests.Reporter = message => Console.WriteLine($"    {message}");

        var failures = 0;

        foreach (var test in SmokeTests.All)
        {
            try
            {
                test.Execute();
                Console.WriteLine($"PASS {test.Name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.WriteLine($"FAIL {test.Name}: {exception.Message}");
            }
        }

        Console.WriteLine(failures == 0
            ? "DATADOG_E2E_DONE PASS"
            : $"DATADOG_E2E_DONE FAIL ({failures} failed)");
        Console.Out.Flush();

        // Terminate so the runner's `simctl launch --console-pty` returns instead of hanging until
        // its timeout. Exiting from an iOS app is otherwise not something to imitate.
        Environment.Exit(failures == 0 ? 0 : 1);
    }
}
