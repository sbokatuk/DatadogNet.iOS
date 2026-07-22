# DatadogNet.iOS

.NET for iOS and .NET MAUI bindings for the native
[Datadog iOS SDK (`dd-sdk-ios`)](https://github.com/DataDog/dd-sdk-ios).

Enable Datadog Real User Monitoring, Logs, Trace, Session Replay, WebView tracking and crash
reporting from C#, in a `net8.0-ios`, `net9.0-ios` or `net10.0-ios` app.

```bash
dotnet add package DatadogNet.Objc.iOS
```

```csharp
using DatadogObjc;

var configuration = new DDConfiguration(clientToken: "<CLIENT_TOKEN>", env: "production")
{
    Service = "my-app",
    Site = DDSite.Us1,
};

DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted);
DDRUM.EnableWith(new DDRUMConfiguration(applicationID: "<RUM_APPLICATION_ID>"));
```

---

## Contents

- [Packages](#packages)
- [Installing](#installing)
- [Usage](#usage)
- [Convenience API](#convenience-api)
- [API coverage](#api-coverage)
- [Migrating from `DatadogCore.iOS` / `DatadogObjc.iOS`](#migrating-from-datadogcoreios--datadogobjcios)
- [How this repository works](#how-this-repository-works)
- [Building locally](#building-locally)
- [Upgrading the Datadog SDK](#upgrading-the-datadog-sdk)
- [Releasing](#releasing)
- [Troubleshooting](#troubleshooting)
- [Licence](#licence)

---

## Packages

Eleven packages, one per native framework in the Datadog release. Versions are
`<dd-sdk-ios version>.<binding revision>` — `2.30.2.1` is dd-sdk-ios **2.30.2**, binding revision
**2**. The fourth component belongs to this repository and advances when the bindings or packaging
change while the native binaries stay put.

| Package | Wraps | Depends on | What it is for |
| --- | --- | --- | --- |
| **`DatadogNet.Objc.iOS`** | `DatadogObjc` | Core, Internal, Logs, RUM, SessionReplay, Trace | **The one you install.** The whole `DD*` API — configuration, RUM, Logs, Trace, Session Replay, tracing headers — and it pulls in every feature module. |
| `DatadogNet.Core.iOS` | `DatadogCore` | Internal | SDK initialization, consent, user and account info. |
| `DatadogNet.Internal.iOS` | `DatadogInternal` | — | Shared foundation every other module is built on. |
| `DatadogNet.Logs.iOS` | `DatadogLogs` | Core, Internal | Log collection. |
| `DatadogNet.RUM.iOS` | `DatadogRUM` | Core, Internal | Real User Monitoring. |
| `DatadogNet.Trace.iOS` | `DatadogTrace` | Core, Internal, OpenTelemetryApi | Distributed tracing (APM). |
| `DatadogNet.SessionReplay.iOS` | `DatadogSessionReplay` | Core, Internal | Session Replay. Requires RUM. |
| `DatadogNet.WebViewTracking.iOS` | `DatadogWebViewTracking` | Core, Internal | Bridges RUM/Logs out of a `WKWebView`. Opt-in. |
| `DatadogNet.CrashReporting.iOS` | `DatadogCrashReporting` | Core, Internal, CrashReporter | Native crash capture. Opt-in. |
| `DatadogNet.CrashReporter.iOS` | `CrashReporter` | — | PLCrashReporter, the engine underneath crash reporting. |
| `DatadogNet.OpenTelemetryApi.iOS` | `OpenTelemetryApi` | — | OpenTelemetry API types used by trace propagation. |

Most apps need one line:

```xml
<PackageReference Include="DatadogNet.Objc.iOS" Version="2.30.2.1" />
```

Add `DatadogNet.CrashReporting.iOS` for crash reporting and `DatadogNet.WebViewTracking.iOS` for
hybrid apps. Both are separate on purpose — crash reporting installs a signal handler, and neither
is something an app that only wanted RUM should pay for.

> **The dependency graph is the real one**, read off the frameworks' Mach-O load commands.
> `DatadogInternal` is the root: `DatadogCore` and `DatadogTrace` depend on *it*, not the other way
> round. The previous `DataDog-SDK-iOS-net` packages declared the reverse, and `DatadogTrace.iOS`
> did not declare `OpenTelemetryApi.iOS` at all — so referencing Trace alone did not link.

**Target frameworks**: `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-ios26.0`.
**Minimum deployment target**: iOS **12.2** — the Datadog frameworks are Swift and use the
OS-provided Swift runtime, which is only ABI-stable from 12.2.

---

## Installing

```xml
<ItemGroup>
  <PackageReference Include="DatadogNet.Objc.iOS" Version="2.30.2.1" />
</ItemGroup>
```

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">12.2</SupportedOSPlatformVersion>
```

The packages are iOS-only. In a multi-targeted MAUI project, guard the reference so an Android or
Windows head does not try to restore them:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <PackageReference Include="DatadogNet.Objc.iOS" Version="2.30.2.1" />
</ItemGroup>
```

---

## Usage

The C# names are the Objective-C selectors projected into C#, so
`initializeWithConfiguration:trackingConsent:` becomes
`InitializeWithConfiguration(configuration, consent)`. Everything is in the `DatadogObjc`
namespace.

[`samples/DatadogNet.iOS.Example`](samples/DatadogNet.iOS.Example) is a working MAUI app that does
all of the below; [`Datadog.cs`](samples/DatadogNet.iOS.Example/Datadog.cs) is the setup in one file.

### Initialize

Once, as early as possible. In MAUI that means `MauiProgram.CreateMauiApp` before the builder runs —
crash reporting only covers what happens after it is enabled, and startup crashes are the ones
worth catching.

```csharp
var configuration = new DDConfiguration(clientToken: "<CLIENT_TOKEN>", env: "production")
{
    Service = "my-app",
    Site = DDSite.Us1,   // Us1, Us3, Us5, Eu1, Ap1, Us1_fed
};

DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted);
DDDatadog.VerbosityLevel = DDSDKVerbosityLevel.Warn;  // SDK problems to the Xcode console
```

`DDConfiguration` also exposes `BatchSize`, `UploadFrequency`, `BatchProcessingLevel`,
`ProxyConfiguration`, `AdditionalConfiguration`, and hooks for custom encryption and server-date
providers.

### RUM

```csharp
var rum = new DDRUMConfiguration(applicationID: "<RUM_APPLICATION_ID>")
{
    SessionSampleRate = 100,
    TrackFrustrations = true,
    TrackBackgroundEvents = true,
    // Automatic UIKit instrumentation. MAUI renders through UIKit, so these report views and
    // taps with no per-page code.
    UiKitViewsPredicate = new DDDefaultUIKitRUMViewsPredicate(),
    UiKitActionsPredicate = new DDDefaultUIKitRUMActionsPredicate(),
};

DDRUM.EnableWith(rum);
```

Manual reporting through `DDRUMMonitor.Shared`:

```csharp
var monitor = DDRUMMonitor.Shared;

using (monitor.StartView("checkout", "Checkout"))
{
    monitor.AddAction(DDRUMActionType.Tap, "pay", new Dictionary<string, object?>
    {
        ["cart.total"] = 42.50m,
    });

    try { /* ... */ }
    catch (Exception exception) { monitor.AddError(exception); }
}
```

### Logs

```csharp
DDLogs.Enable();

var logger = DDLogger.Create(name: "app", bundleWithRumEnabled: true);

logger.Log(DDLogLevel.Info, "user signed in");
logger.Log(DDLogLevel.Error, "payment failed", exception, new Dictionary<string, object?>
{
    ["order.id"] = orderId,
});
```

Levels: `Debug`, `Info`, `Notice`, `Warn`, `Error`, `Critical`.

### Trace

```csharp
DDTrace.EnableWith(new DDTraceConfiguration
{
    SampleRate = 100,
    NetworkInfoEnabled = true,
    BundleWithRumEnabled = true,
});
```

For distributed tracing across your backend, attach `DDNSURLSessionDelegate` to your
`NSUrlSession`, or use the `DDRUMURLSessionTracking` / `DDTraceURLSessionTracking` first-party-host
helpers. Propagation style is chosen with `DDTracingHeaderType` — `Datadog`, `B3`, `B3multi` or
`Tracecontext`.

### Session Replay

```csharp
using DatadogSessionReplay;

DDSessionReplay.EnableWith(new DDSessionReplayConfiguration(
    replaySampleRate: 100,
    textAndInputPrivacyLevel: DDTextAndInputPrivacyLevel.MaskAll,
    imagePrivacyLevel: DDImagePrivacyLevel.MaskAll,
    touchPrivacyLevel: DDTouchPrivacyLevel.Hide));
```

Requires RUM. The three levels decide what is redacted *on the device*, before anything is
uploaded, and the initializer requires all three so the choice is never left implicit. Loosen them
deliberately.

A single view can override the session-wide levels:

```csharp
var overrides = myView.GetDdSessionReplayPrivacyOverrides();
overrides.TextAndInputPrivacy = DDTextAndInputPrivacyLevelOverride.MaskAll;
overrides.Hide = new NSNumber(true);
```

Set `StartRecordingImmediately = false` to enable the feature without recording, then call
`DDSessionReplay.StartRecording()` once the user has consented, and `StopRecording()` to pause.

### Crash reporting

Add `DatadogNet.CrashReporting.iOS`, then enable it **after** initializing the SDK:

```csharp
using DatadogCrashReporting;

DDCrashReporter.Enable();
```

Crashes are reported on the next launch, as RUM errors and logs. Upload your app's dSYMs to Datadog
for symbolication.

### WebView tracking

```csharp
using DatadogWebViewTracking;

DDWebViewTracking.EnableWithWebView(webView, new NSSet<NSString>((NSString)"example.com"), logsSampleRate: 100);
DDWebViewTracking.DisableWithWebView(webView);  // on teardown
```

### User info and consent

```csharp
DDDatadog.SetUserInfo("user-123", "Ada Lovelace", "ada@example.com");
DDDatadog.SetTrackingConsent(TrackingConsent.Pending);
```

`Pending` collects events but holds them on the device until consent is granted or refused — which
is what a prompt-on-first-launch flow wants.

---

## Convenience API

The generated binding is a faithful projection of Objective-C, which makes some things clumsy in
C#. `DatadogNet.Objc.iOS` adds a small hand-written layer over it. The generated members are all
still there; nothing is hidden or renamed.

| Instead of | Write |
| --- | --- |
| `monitor.StartViewWithKey("k", "Name", new NSDictionary<NSString, NSObject>())` … `StopViewWithKey` | `using (monitor.StartView("k", "Name"))` |
| `new NSDictionary<NSString, NSObject>(...)` with hand-wrapped `NSObject` values | `new Dictionary<string, object?> { ["k"] = 42 }` on any overload below |
| `monitor.AddActionWithType(type, name, attributes)` | `monitor.AddAction(type, name, attributes?)` |
| `monitor.AddErrorWithMessage(...)` with an `NSError` you do not have | `monitor.AddError(exception)` |
| `DDLogs.EnableWith(new DDLogsConfiguration(null))` | `DDLogs.Enable()` |
| `new DDLoggerConfiguration(service, name, bool, bool, bool, float, level, bool)` | `DDLogger.Create(name: "app")` |
| six methods per level, plus an `NSError` you do not have | `logger.Log(level, message, exception?, attributes?)` |
| `DDDatadog.SetUserInfoWithId(id, null, null, empty)` | `DDDatadog.SetUserInfo(id)` |
| `DDTrackingConsent.Granted` (a class, not an enum) | `DDDatadog.SetTrackingConsent(TrackingConsent.Granted)` |
| `DDURLSessionInstrumentation.EnableWithConfiguration(...)` with a `Class` as a raw `IntPtr` | `DDURLSessionInstrumentation.Enable<MyDelegate>()` |

The view scope is the one worth adopting everywhere: the raw API is a `StartViewWithKey` /
`StopViewWithKey` pair matched by string key, and a view left open by an early return or an
exception captures every later action and error in the session.

Attribute values may be strings, any numeric type, `bool`, `DateTime`, `DateTimeOffset`, `Guid`,
enums, `NSObject`s, arrays, and nested dictionaries. Anything else throws `ArgumentException`
rather than being silently dropped.

### Redacting events before upload

Both RUM and Logs let you rewrite or drop events on the device, before anything is uploaded. This
is the supported way to keep PII out of Datadog.

```csharp
var logs = new DDLogsConfiguration(customEndpoint: null);
logs.SetEventMapper(logEvent =>
{
    logEvent.Message = Redact(logEvent.Message);
    return logEvent;    // or return null to drop the event entirely
});
DDLogs.EnableWith(logs);
```

RUM has five equivalents — `SetViewEventMapper`, `SetActionEventMapper`, `SetResourceEventMapper`,
`SetErrorEventMapper` and `SetLongTaskEventMapper` — registered on `DDRUMConfiguration` before
`DDRUM.EnableWith`. Mappers can only be set at configuration time.

### Network instrumentation

```csharp
DDURLSessionInstrumentation.Enable<MySessionDelegate>();
```

`MySessionDelegate` must be an `NSObject` implementing `INSUrlSessionDataDelegate` and carrying a
`[Register]` attribute. The raw binding takes the delegate class as an `IntPtr`, which accepts
`IntPtr.Zero` happily and then instruments nothing; the generic form resolves the Objective-C class
and throws if it does not exist.

---

## API coverage

Every public Objective-C type Datadog declares for 2.30.2 is bound except one, and every documented
feature is reachable from C#. Measured by parsing the `-Swift.h` header each xcframework ships and
diffing it against the bound selectors — not by reading the documentation.

The unbound type is **`DDRUMLongTaskEventLongTaskScripts`**, with the `scripts` property that
returns it. It describes JavaScript long tasks, is reachable only from inside a RUM long-task event
mapper, and a native iOS app does not produce them.

Three further groups of *members* are deliberately not bound:

- **Four `DDTelemetryConfigurationEventTelemetryConfiguration` properties** named after the Session
  Replay privacy levels. These are read-only fields on a *telemetry* event, describing what the SDK
  reported about its own configuration; they are not knobs. The real ones are on
  `DDSessionReplayConfiguration`, and all of those are bound.
- **`initWithSamplingRate:` and `initWithSamplingRate:injectEncoding:` on the tracing header
  writers** — deprecated upstream, and differing from the bound `initWithSampleRate:` forms only by
  argument label, which Objective Sharpie cannot project as two separate initializers.
- **Three `URLSession` overloads on `DatadogURLSessionDelegate`** — see
  [the note in that binding](src/DatadogNet.Internal.iOS/ApiDefinitions.cs); binding them crashes
  every consuming app at startup, and they remain callable under their inherited names.

---

## Migrating from `DatadogCore.iOS` / `DatadogObjc.iOS`

The package IDs changed; **the namespaces and the API did not**. Migration is a `PackageReference`
edit — no `using` directive and no call site changes.

```diff
-<PackageReference Include="DatadogObjc.iOS" Version="2.17.0.1" />
+<PackageReference Include="DatadogNet.Objc.iOS" Version="2.30.2.1" />
```

| Old | New |
| --- | --- |
| `DatadogObjc.iOS` | `DatadogNet.Objc.iOS` |
| `DatadogCore.iOS` | `DatadogNet.Core.iOS` |
| `DatadogInternal.iOS` | `DatadogNet.Internal.iOS` |
| `DatadogLogs.iOS` | `DatadogNet.Logs.iOS` |
| `DatadogRUM.iOS` | `DatadogNet.RUM.iOS` |
| `DatadogTrace.iOS` | `DatadogNet.Trace.iOS` |
| `DatadogSessionReplay.iOS` | `DatadogNet.SessionReplay.iOS` |
| `DatadogWebViewTracking.iOS` | `DatadogNet.WebViewTracking.iOS` |
| `DatadogCrashReporting.iOS` | `DatadogNet.CrashReporting.iOS` |
| `CrashReporter.iOS` | `DatadogNet.CrashReporter.iOS` |
| `OpenTelemetryApi.iOS` | `DatadogNet.OpenTelemetryApi.iOS` |

Three things genuinely changed:

**A startup crash is fixed.** The old `DatadogInternal.iOS` bound three `URLSession` overloads that
`INSUrlSessionDataDelegate` already declares, registering each selector twice. Any app that loaded
the package died during assembly registration before running a line of its own code:

```
Could not register the selector 'URLSession:task:didFinishCollectingMetrics:' … because the
selector is already registered on the member 'DidFinishCollectingMetrics'.
```

**`CrashReporter` is versioned with everything else.** It was `1.11.2.1`, tracking PLCrashReporter
upstream; it is now `2.30.2.1` like the rest, because it ships inside the same Datadog release and
the old numbering made it impossible to tell which Datadog build a given package belonged to.

**`net7.0-ios` is gone, `net9`/`net10` are new.** The old packages targeted `net7.0-ios16.1` and
`net8.0-ios17.2`.

---

## How this repository works

```
DataDog/dd-sdk-ios release  ──►  Datadog.xcframework.zip  (one archive, eleven frameworks)
        │  build/FetchXcFrameworks.sh — download, verify SHA-256, strip to iOS slices
        ▼
   libs/<Framework>.xcframework
        │  src/DatadogNet.<Package>.iOS/ — binding project + committed ApiDefinitions.cs
        │  build/BuildNugets.sh — pack twice (net9 band, net10 band), then merge
        ▼
   artifacts/*.nupkg  ──►  nuget.org
```

There is no Carthage step and nothing is compiled from source. Datadog publishes the built
xcframeworks as a release asset, so the build downloads one archive, verifies it against a hash
pinned in [`build/checksums.txt`](build/checksums.txt), and strips it to the two iOS slices.

**Why the two-pass build.** Each .NET SDK's iOS workload supports the current target framework and
the previous one — the .NET 9 band builds net8 + net9, the .NET 10 band builds net9 + net10. No
single SDK builds all three, so `BuildNugets.sh` packs twice and
[`merge-packages.py`](build/merge-packages.py) grafts the net10 assets into the net9 packages,
carrying the dependency group across with them.

**Why the slices are stripped.** The upstream archive carries tvOS for every framework, plus
macCatalyst, macOS, watchOS and visionOS for two of them, and a full set of dSYMs. None of it is
reachable from a `net*-ios` binding, and the whole xcframework is embedded in each assembly's
payload once per target framework. Stripping takes the eleven packages from roughly a gigabyte to
about 74 MB.

### Layout

| Path | What |
| --- | --- |
| `src/DatadogNet.*.iOS/` | One binding project per framework. `ApiDefinitions.cs` and `StructsAndEnums.cs` are committed sources, not generated at build time. |
| `src/Datadog.Binding.props` | Everything the eleven projects share, so each `.csproj` is a dozen lines. |
| `src/DatadogNet.Objc.iOS/Additions/` | The hand-written [convenience API](#convenience-api). |
| `build/` | Fetch, pack, merge and binding-regeneration scripts, plus the checksum pins. |
| `tests/DatadogNet.iOS.PackageTests/` | Asserts the shape of the packed `.nupkg`s — 123 checks over layout, slices, manifests, dependencies and metadata. |
| `tests/DatadogNet.iOS.DeviceTests/` | An iOS app that consumes the **packed packages** and drives the real SDK on a simulator. |
| `samples/DatadogNet.iOS.Example/` | A MAUI app doing the same, the way an app would. |

The tests consume the packed `.nupkg`s from `artifacts/` rather than referencing the projects, so
they exercise what actually gets published — including the inter-package dependencies, which a
`ProjectReference` would bypass entirely.

`DatadogNet.sln` deliberately contains the binding projects and the tests but **not** the sample.
A solution-wide `Release` build would AOT-compile the MAUI app for device across both of its target
frameworks, which takes longer than everything else in the repository put together. Build the
sample directly, as the command in the previous section does.

---

## Building locally

Requires macOS, Xcode, and the .NET 9 and .NET 10 SDKs with the `ios` workload.

```bash
./build/FetchXcFrameworks.sh    # ~100 MB, verified against build/checksums.txt
./build/BuildNugets.sh          # packs all eleven into artifacts/
dotnet test tests/DatadogNet.iOS.PackageTests
```

Run the on-simulator smoke tests against the packed packages:

```bash
./.github/scripts/run-simulator-tests.sh 2.30.2.1 net9.0-ios18.0
```

Build and run the sample:

```bash
dotnet build samples/DatadogNet.iOS.Example/DatadogNetExample.csproj -p:RuntimeIdentifier=iossimulator-arm64
```

> Building anything that targets `net10.0-ios26.0` needs Xcode **26.0** specifically — .NET for iOS
> refuses any other version. If your default Xcode is newer, prefix the command:
> `DEVELOPER_DIR=/Applications/Xcode_26.0.1.app/Contents/Developer ...`. CI handles this with the
> [`select-xcode`](.github/actions/select-xcode/action.yml) action.

---

## Upgrading the Datadog SDK

1. Record the new archive's hash in [`build/checksums.txt`](build/checksums.txt):

   ```bash
   v=2.18.0
   curl -fsSL -O "https://github.com/DataDog/dd-sdk-ios/releases/download/$v/Datadog.xcframework.zip"
   shasum -a 256 Datadog.xcframework.zip
   ```

2. Bump `DatadogNativeVersion` in [`Directory.Build.props`](Directory.Build.props) and reset
   `DatadogBindingRevision` to `1`.
3. `./build/FetchXcFrameworks.sh`
4. `./build/GenerateBindings.sh` — writes to `Binding/`, **not** over the committed sources. Diff
   and port real changes across; the script's header lists the fixes the committed files carry that
   regenerating would otherwise undo.
5. `./build/BuildNugets.sh` and run both test suites.

If Datadog adds or removes a framework, `FetchXcFrameworks.sh` fails loudly rather than silently
dropping a package — update the `FRAMEWORKS` list, add or remove the binding project, and update
`Packages.All` in the package tests.

> **Note for 3.x.** dd-sdk-ios 3.0 removed `DatadogObjc` entirely — the `DD*` types moved into each
> feature module — dropped PLCrashReporter for KSCrash, and added `DatadogFlags` and
> `DatadogProfiling`. Moving to 3.x is a re-layout of the package set, not a version bump.

---

## Releasing

Tag it. `v2.30.2.1` builds, tests, publishes all eleven packages to nuget.org via trusted
publishing, and creates a GitHub release. The tag drives which native SDK is bound, so an older
line can be released by tagging it.

Pull requests publish a `-beta.<pr>.<run>` prerelease of the whole set.

Curated notes in `docs/release-notes/<version>.md` replace the generated commit list when present.

---

## Troubleshooting

**Nothing appears in Datadog.** Check `Site` matches your organisation's region — this is the most
common cause. Set `DDDatadog.VerbosityLevel = DDSDKVerbosityLevel.Debug` to see the SDK's own
diagnostics in the Xcode console.

**`DDSessionReplay` could not be found.** It moved out of the `DatadogObjc` namespace in
dd-sdk-ios 2.19.0 and now lives only in `DatadogSessionReplay`. Add `using DatadogSessionReplay;`.

**`This version of .NET for iOS requires Xcode 26.0`.** Only affects `net10.0-ios26.0`. See
[Building locally](#building-locally).

**Restore fails on Windows with a path-length error.** Should not happen — the native payload ships
as a single compressed file precisely to avoid it. Please file an issue.

**A framework fails to load at runtime.** Usually a partially restored package. Clear the cached
copies and restore again:
`rm -rf ~/.nuget/packages/datadognet.*`.

---

## Licence

The binding code in this repository is [MIT](LICENSE). The native binaries the packages ship are
built and published by Datadog and are Apache-2.0 — which covers `dd-sdk-ios`, PLCrashReporter and
opentelemetry-swift alike. Every package declares `MIT AND Apache-2.0` and carries both texts under
`licenses/`.
