# DatadogNet.iOS

[![NuGet](https://img.shields.io/nuget/v/DatadogNet.RUM.iOS?label=nuget)](https://www.nuget.org/packages/DatadogNet.RUM.iOS)
[![release](https://github.com/sbokatuk/DatadogNet.iOS/actions/workflows/release.yml/badge.svg)](https://github.com/sbokatuk/DatadogNet.iOS/actions/workflows/release.yml)
[![Targets: net8.0 | net9.0 | net10.0](https://img.shields.io/badge/targets-net8.0%20%7C%20net9.0%20%7C%20net10.0-512BD4)](#packages)
[![dd-sdk-ios 3.14.0](https://img.shields.io/badge/dd--sdk--ios-3.14.0-632CA6)](https://github.com/DataDog/dd-sdk-ios/releases/tag/3.14.0)
[![Licence: MIT AND Apache-2.0](https://img.shields.io/badge/licence-MIT%20AND%20Apache--2.0-green)](#licence)

.NET for iOS and .NET MAUI bindings for the native
[Datadog iOS SDK (`dd-sdk-ios`)](https://github.com/DataDog/dd-sdk-ios).

Enable Datadog Real User Monitoring, Logs, Trace, Session Replay, WebView tracking and crash
reporting from C#, in a `net8.0-ios`, `net9.0-ios` or `net10.0-ios` app.

```bash
dotnet add package DatadogNet.Core.iOS
dotnet add package DatadogNet.RUM.iOS
```

```csharp
using DatadogCore;
using DatadogRUM;

var configuration = new DDConfiguration(clientToken: "<CLIENT_TOKEN>", env: "production")
{
    Service = "my-app",
    Site = DDSite.Us1(),
};

DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted());
DDRUM.EnableWith(new DDRUMConfiguration(applicationID: "<RUM_APPLICATION_ID>"));
```

Built against **dd-sdk-ios 3.14.0**. Coming from the 2.x packages? See
[Migrating from 2.x](#migrating-from-2x) — the types moved namespace, so it is not a version bump
alone.

---

## Contents

- [Packages](#packages)
- [Installing](#installing)
- [Usage](#usage)
- [Convenience API](#convenience-api)
- [Migrating from 2.x](#migrating-from-2x)
- [How this repository works](#how-this-repository-works)
- [Building locally](#building-locally)
- [Upgrading the Datadog SDK](#upgrading-the-datadog-sdk)
- [Releasing](#releasing)
- [Troubleshooting](#troubleshooting)
- [Licence](#licence)

---

## Packages

Twelve packages, one per native framework, plus a compatibility meta-package. Versions are
`<dd-sdk-ios version>.<binding revision>` — `3.14.0.1` is dd-sdk-ios **3.14.0**, binding revision
**1**. The fourth component belongs to this repository and advances when the bindings or packaging
change while the native binaries stay put.

| Package | Wraps | Depends on | What it is for |
| --- | --- | --- | --- |
| **`DatadogNet.Core.iOS`** | `DatadogCore` | Internal | SDK init, consent, user/account info, URLSession instrumentation. **Always needed.** |
| **`DatadogNet.RUM.iOS`** | `DatadogRUM` | Core, Internal | Real User Monitoring — the whole `DDRUM*` API. |
| `DatadogNet.Logs.iOS` | `DatadogLogs` | Core, Internal | Log collection. |
| `DatadogNet.Trace.iOS` | `DatadogTrace` | Core, Internal, OpenTelemetryApi | Distributed tracing (APM). |
| `DatadogNet.SessionReplay.iOS` | `DatadogSessionReplay` | Core, Internal | Session Replay. Requires RUM. |
| `DatadogNet.WebViewTracking.iOS` | `DatadogWebViewTracking` | Core, Internal | Bridges RUM/Logs out of a `WKWebView`. |
| `DatadogNet.CrashReporting.iOS` | `DatadogCrashReporting` | Core, Internal | Native crash capture (KSCrash). Reports through RUM. |
| `DatadogNet.Flags.iOS` | `DatadogFlags` | Core, Internal | Feature flags. **No C# API yet** — see below. |
| `DatadogNet.Profiling.iOS` | `DatadogProfiling` | Core, Internal | Continuous profiling. **No C# API yet** — see below. |
| `DatadogNet.Internal.iOS` | `DatadogInternal` | — | Shared foundation. Pulled in transitively. |
| `DatadogNet.OpenTelemetryApi.iOS` | `OpenTelemetryApi` | — | OpenTelemetry API types for trace propagation. |
| `DatadogNet.Objc.iOS` | — (meta-package) | Core, RUM, Logs, Trace, SessionReplay | **Compatibility only.** Redirects an existing 2.x `PackageReference`. New apps ignore it. |

Reference the modules you use. A typical app takes `DatadogNet.Core.iOS` plus `DatadogNet.RUM.iOS`,
and adds `Logs`, `Trace`, `SessionReplay`, `CrashReporting` or `WebViewTracking` as needed.

> **`DatadogNet.Flags.iOS` and `DatadogNet.Profiling.iOS` expose no callable API.** Upstream ships
> these modules but has not projected them into Objective-C, so there is nothing for C# to bind.
> The packages exist so the set mirrors the SDK and so adding the API later is a version bump. There
> is no reason to reference them today.

> **The dependency graph is the real one**, read off the frameworks' Mach-O load commands.
> `DatadogInternal` is the root; every product module depends on it, and `DatadogTrace` also on
> `OpenTelemetryApi`.

**Target frameworks**: `net8.0-ios18.0`, `net9.0-ios18.0`, `net10.0-ios26.0`.
**Minimum deployment target**: iOS **12.2** — the Datadog frameworks are Swift and use the
OS-provided Swift runtime, ABI-stable from 12.2.

---

## Installing

```xml
<ItemGroup>
  <PackageReference Include="DatadogNet.Core.iOS" Version="3.14.0.1" />
  <PackageReference Include="DatadogNet.RUM.iOS" Version="3.14.0.1" />
</ItemGroup>
```

In a multi-targeted MAUI project, guard the references so an Android or Windows head does not try to
restore them:

```xml
<ItemGroup Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">
  <PackageReference Include="DatadogNet.Core.iOS" Version="3.14.0.1" />
  <PackageReference Include="DatadogNet.RUM.iOS" Version="3.14.0.1" />
</ItemGroup>
```

```xml
<SupportedOSPlatformVersion Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'ios'">12.2</SupportedOSPlatformVersion>
```

---

## Usage

The C# names are the Objective-C selectors projected into C#. Each feature is in its own namespace
now — one `using` per module. [`samples/DatadogNet.iOS.Example`](samples/DatadogNet.iOS.Example) is
a working MAUI app that does all of the below;
[`Datadog.cs`](samples/DatadogNet.iOS.Example/Datadog.cs) is the setup in one file.

### Initialize

Once, as early as possible — in MAUI, in `MauiProgram.CreateMauiApp` before the builder runs, so
crash reporting and startup metrics cover the whole launch.

```csharp
using DatadogCore;
using DatadogInternal;   // DDCoreLoggerLevel

var configuration = new DDConfiguration(clientToken: "<CLIENT_TOKEN>", env: "production")
{
    Service = "my-app",
    Site = DDSite.Us1(),   // Us1, Us3, Us5, Eu1, Ap1, Ap2, Us1_fed
};

DDDatadog.InitializeWithConfiguration(configuration, DDTrackingConsent.Granted());
DDDatadog.SetVerbosityLevel(DDCoreLoggerLevel.Warn);   // SDK problems to the Xcode console
```

`DDSite` and `DDTrackingConsent` are classes of static factory methods, so the values are called:
`DDSite.Us1()`, `DDTrackingConsent.Granted()`.

### RUM

```csharp
using DatadogRUM;

var rum = new DDRUMConfiguration(applicationID: "<RUM_APPLICATION_ID>")
{
    SessionSampleRate = 100,
    TrackFrustrations = true,
    TrackMemoryWarnings = true,   // new in 3.0
    UiKitViewsPredicate = new DDDefaultUIKitRUMViewsPredicate(),
    UiKitActionsPredicate = new DDDefaultUIKitRUMActionsPredicate(),
};

DDRUM.EnableWith(rum);
```

Manual reporting through `DDRUMMonitor.Shared()`:

```csharp
var monitor = DDRUMMonitor.Shared();

using (monitor.StartView("checkout", "Checkout"))
{
    // View-level attributes propagate to the actions, resources and errors recorded here - a 3.0
    // change that removes the need to repeat them on every call.
    monitor.AddViewAttributes(new Dictionary<string, object?> { ["cart.total"] = 42.50m });

    monitor.AddAction(DDRUMActionType.Tap, "pay");

    try { /* ... */ }
    catch (Exception exception) { monitor.AddError(exception); }
}
```

### Logs

```csharp
using DatadogLogs;

DDLogs.Enable();

var logger = DDLogger.Create(name: "app", bundleWithRumEnabled: true);

logger.Log(DDLogLevel.Info, "user signed in");
logger.Log(DDLogLevel.Error, "payment failed", exception, new Dictionary<string, object?>
{
    ["order.id"] = orderId,
});
```

Redact or drop logs before upload with an event mapper:

```csharp
var configuration = new DDLogsConfiguration(customEndpoint: null);
configuration.SetEventMapper(logEvent =>
{
    logEvent.Message = Redact(logEvent.Message);
    return logEvent;    // or return null to drop the event
});
DDLogs.EnableWith(configuration);
```

### Trace

```csharp
using DatadogTrace;

DDTrace.EnableWith(new DDTraceConfiguration
{
    SampleRate = 100,
    NetworkInfoEnabled = true,
    BundleWithRumEnabled = true,
});
```

Header writers for distributed tracing — `DDHTTPHeadersWriter` (Datadog), `DDB3HTTPHeadersWriter`
(B3) and `DDW3CHTTPHeadersWriter` (W3C `tracecontext`). None takes a sampling argument in 3.x;
sampling is derived from the RUM `session.id`.

### Network instrumentation

Automatic RUM resource collection and trace propagation, from 3.0 through the unified
`DDURLSessionInstrumentation`. This binding resolves the delegate class for you:

```csharp
using DatadogCore;

DDURLSessionInstrumentation.Enable<MySessionDelegate>();
// ...
DDURLSessionInstrumentation.Disable<MySessionDelegate>();
```

`MySessionDelegate` must be an `NSObject` implementing `INSUrlSessionDataDelegate` with a
`[Register]` attribute. Attach it to your `NSUrlSession` as usual.

### Session Replay

```csharp
using DatadogSessionReplay;

DDSessionReplay.EnableWith(new DDSessionReplayConfiguration(
    replaySampleRate: 100,
    textAndInputPrivacyLevel: DDTextAndInputPrivacyLevel.MaskAll,
    imagePrivacyLevel: DDImagePrivacyLevel.MaskAll,
    touchPrivacyLevel: DDTouchPrivacyLevel.Hide));
```

Requires RUM. The three levels are required arguments, so what gets redacted *on the device* is
always an explicit choice. A single view can override them:

```csharp
var overrides = myView.GetDdSessionReplayPrivacyOverrides();
overrides.TextAndInputPrivacy = DDTextAndInputPrivacyLevelOverride.MaskAll;
overrides.Hide = new NSNumber(true);
```

### Crash reporting

Add `DatadogNet.CrashReporting.iOS`, then enable it **after** initializing the SDK:

```csharp
using DatadogCrashReporting;

DDCrashReporter.Enable();
```

The engine is KSCrash. Crashes are reported on the next launch as **RUM errors** — enable RUM too,
or they are not delivered. Upload your app's dSYMs to Datadog for symbolication.

### User, account and consent

```csharp
DDDatadog.SetUserInfo("user-123", "Ada Lovelace", "ada@example.com");
DDDatadog.SetAccountInfo("acct-42", "Acme Corp");
DDDatadog.SetTrackingConsent(TrackingConsent.Pending);
```

---

## Convenience API

The generated binding is a faithful projection of Objective-C, which makes some things clumsy in
C#. Each product module adds a small hand-written layer over it. The generated members are all still
there; nothing is hidden or renamed.

| Instead of | Write |
| --- | --- |
| `monitor.StartViewWithKey(...)` … `StopViewWithKey(...)` matched by key | `using (monitor.StartView("k", "Name"))` |
| `new NSDictionary<NSString, NSObject>(...)` with hand-wrapped values | `new Dictionary<string, object?> { ["k"] = 42 }` on any overload below |
| `monitor.AddActionWithType(type, name, attributes)` | `monitor.AddAction(type, name, attributes?)` |
| `monitor.AddErrorWithMessage(...)` with an `NSError` you do not have | `monitor.AddError(exception)` |
| `monitor.AddViewAttributes(nsDictionary)` | `monitor.AddViewAttributes(dictionary)` / `RemoveViewAttributes(params keys)` |
| six log methods per level, plus an `NSError` you do not have | `logger.Log(level, message, exception?, attributes?)` |
| `new DDLoggerConfiguration(service, name, bool, bool, bool, float, level, bool)` | `DDLogger.Create(name: "app")` |
| `DDDatadog.SetUserInfoWithUserId(id, null, null, empty)` | `DDDatadog.SetUserInfo(id)` |
| `DDTrackingConsent.Granted()` (a factory, not an enum) | `DDDatadog.SetTrackingConsent(TrackingConsent.Granted)` |
| `DDURLSessionInstrumentation.EnableWithConfiguration(...)` with a raw `Class` handle | `DDURLSessionInstrumentation.Enable<TDelegate>()` |
| a one-element dictionary round-trip to convert one value | `DatadogAttributes.ToNSObject(value, key)` |
| `monitor.AddAttributeForKey(k, nsObject)` | `monitor.AddAttribute(k, value)` / `AddViewAttribute` / `AddFeatureFlagEvaluation` |
| `monitor.CurrentSessionIDWithCompletion(block)` | `await monitor.GetCurrentSessionIdAsync()` |
| construct a writer, pass it as the carrier, read headers back off it — once per format | `span.InjectHeaders(tracer)` |
| no way at all to read a span's ids | `span.GetTraceId(tracer)` / `span.GetSpanId(tracer)` |
| `span.SetErrorWithKind(kind, message, stack)` from an exception you must decompose | `span.SetError(exception)` |

Attribute values may be strings, any numeric type, `bool`, `DateTime`, `DateTimeOffset`, `Guid`,
enums, `NSObject`s, arrays, and nested dictionaries. Anything else throws `ArgumentException` rather
than being silently dropped. `DatadogAttributes` lives in `DatadogCore`.

### Trace ids

`GetTraceId` returns **32 lowercase hexadecimal characters** and `GetSpanId` returns **decimal**.
The asymmetry is not a choice — it is Datadog's wire format, and matching it is what makes a RUM
resource correlate with its APM trace. dd-sdk-android's own `DatadogInterceptor` writes
`_dd.trace_id` as `DatadogTraceId.toHexString()` and `_dd.span_id` as `String.valueOf(long)`.

Reading the ids takes work because `OTSpanContext` exposes none: they are recovered by injecting
into a Datadog-format writer, and the trace id arrives in two pieces — the decimal low 64 bits in
`x-datadog-trace-id`, and the high 64 as `_dd.p.tid` inside `x-datadog-tags`. Using only the former
yields a decimal string naming half of a different-looking id, which is a mistake that reached a
release of a consumer of this package before it was caught.

---

## API coverage

Measured by diffing each framework's generated `-Swift.h` against `ApiDefinitions.cs`, not estimated.

| Framework | Objective-C types | Bound |
| --- | ---: | ---: |
| `DatadogRUM` | 377 | 377 |
| `DatadogLogs` | 16 | 16 |
| `DatadogCore` | 12 | 12 |
| `DatadogTrace` | 12 | 12 |
| `DatadogSessionReplay` | 4 | 4 |
| `DatadogInternal` | 2 | 2 |
| `DatadogCrashReporting` | 1 | 1 |
| `DatadogWebViewTracking` | 1 | 1 |

Member coverage is the same story: of 61 selectors and properties on `DatadogCore`, 59 are exported,
and the two that are not are `init` and `new`, which `[DisableDefaultCtor]` removes on purpose.

**What is missing is missing upstream.** Three parts of dd-sdk-ios have no Objective-C projection at
all, so there is nothing for a binding to bind:

| | Swift types | ObjC types | Consequence |
| --- | ---: | ---: | --- |
| `DatadogFlags` | 15 | **0** | Feature Flags are unreachable from C#. The API leans on generics (`FlagDetails<T>`) and enums with associated values (`AnyValue`), neither of which Swift projects into Objective-C. |
| `DatadogProfiling` | 2 | **0** | Profiling is unreachable. |
| `OpenTelemetryApi` | — | no `-Swift.h` at all | A pure-Swift module. `DatadogNet.OpenTelemetryApi.iOS` can only ever be a link-time dependency of `DatadogNet.Trace.iOS`. |

`DatadogTrace` is additionally 24 public Swift types projected down to 12, and the casualty is
`OTelTracerProvider` — so OpenTelemetry tracing is unreachable even though OpenTracing is not.

`DatadogNet.Flags.iOS` and `DatadogNet.Profiling.iOS` therefore ship the frameworks and expose no
callable API. They exist so the SDK is mirrored and so a future projection needs no new package.

There is a way around this — a hand-written Swift `@objc` wrapper, which we can compile ourselves —
and a working prototype for Flags lives in [`shims/DatadogFlagsObjc/`](shims/DatadogFlagsObjc/).
Nothing ships yet.

---

## Migrating from 2.x

The 2.x packages (`2.30.2.1` and earlier) bound dd-sdk-ios 2.x, where every `DD*` type lived in a
single `DatadogObjc` namespace. **dd-sdk-ios 3.0 deleted that framework and moved the types into the
module each belongs to**, so migration is a code change, not a version bump.

**Namespaces.** Replace `using DatadogObjc;` with a per-feature set:

```diff
-using DatadogObjc;
+using DatadogCore;      // DDDatadog, DDConfiguration, DDSite, DDTrackingConsent
+using DatadogRUM;       // DDRUM, DDRUMMonitor, DDRUMConfiguration, RUM event models
+using DatadogLogs;      // DDLogs, DDLogger, DDLoggerConfiguration
+using DatadogTrace;     // DDTrace, DDTracer, header writers
+using DatadogInternal;  // DDCoreLoggerLevel, DDTracingHeaderType
```

**Packages.** If you referenced `DatadogNet.Objc.iOS`, it still resolves — it is now a
dependency-only meta-package pulling Core, RUM, Logs, Trace and SessionReplay — but prefer moving to
the product modules directly. `DatadogNet.CrashReporter.iOS` is retired (KSCrash replaced
PLCrashReporter and is part of `DatadogNet.CrashReporting.iOS`).

**API changes**, all upstream:

| 2.x | 3.x |
| --- | --- |
| `DDSite.Us1` (property) | `DDSite.Us1()` (factory method) |
| `DDTrackingConsent.Granted` | `DDTrackingConsent.Granted()` |
| `DatadogURLSessionDelegate` / `DDNSURLSessionDelegate` | `DDURLSessionInstrumentation.Enable<T>()` |
| `DDOTelHTTPHeadersWriter` | `DDW3CHTTPHeadersWriter` |
| `new DDHTTPHeadersWriter(samplingStrategy, injection)` | `new DDHTTPHeadersWriter(injection)` |
| `RUMView(path:)` | `RUMView(name:)` |
| crashes via Logs | crashes via RUM error tracking |

If you cannot make these changes yet, **stay on `2.30.2.1`** — it remains on nuget.org. Note that
the 2.x line receives no further upstream fixes.

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
[`merge-packages.py`](build/merge-packages.py) grafts the net10 assets and dependency groups into
the net9 packages.

### Layout

| Path | What |
| --- | --- |
| `src/DatadogNet.*.iOS/` | One binding project per framework. `ApiDefinitions.cs` and `StructsAndEnums.cs` are committed sources. |
| `src/Datadog.Binding.props` | Everything the binding projects share, so each `.csproj` is a dozen lines. |
| `src/DatadogNet.*.iOS/Additions/` | The hand-written [convenience API](#convenience-api), in Core, RUM and Logs. |
| `src/DatadogNet.Objc.iOS/` | The compatibility meta-package: dependencies, no assembly. |
| `build/` | Fetch, pack, merge and binding-regeneration scripts, plus the checksum pins. |
| `tests/DatadogNet.iOS.PackageTests/` | Asserts the shape of the packed `.nupkg`s — 127 checks. |
| `tests/DatadogNet.iOS.DeviceTests/` | An iOS app that consumes the **packed packages** and drives the real SDK on a simulator. |
| `samples/DatadogNet.iOS.Example/` | A MAUI app doing the same, the way an app would. |

The tests consume the packed `.nupkg`s from `artifacts/` rather than referencing the projects, so
they exercise what actually gets published — including the inter-package dependencies, which a
`ProjectReference` would bypass entirely.

`DatadogNet.sln` deliberately contains the binding projects and the tests but **not** the sample. A
solution-wide `Release` build would AOT-compile the MAUI app for device across both of its target
frameworks, which takes longer than everything else in the repository put together. Build the sample
directly, as the command in the next section does.

---

## Building locally

Requires macOS, Xcode, and the .NET 9 and .NET 10 SDKs with the `ios` workload.

```bash
./build/FetchXcFrameworks.sh    # ~200 MB, verified against build/checksums.txt
./build/BuildNugets.sh          # packs all twelve into artifacts/
dotnet test tests/DatadogNet.iOS.PackageTests
```

Run the on-simulator smoke tests against the packed packages:

```bash
./.github/scripts/run-simulator-tests.sh 3.14.0.1 net9.0-ios18.0
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
   v=3.15.0
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
6. Update the `dd-sdk-ios` badge at the top of this file — both its label and its release link.
   It is hardcoded, so nothing else will notice when it goes stale.

If Datadog adds or removes a framework, `FetchXcFrameworks.sh` fails loudly rather than silently
dropping a package — update the `FRAMEWORKS` list, add or remove the binding project, and update
`Packages.All` in the package tests.

> **Note.** Objective Sharpie 3.5.116 cannot currently generate these bindings — its bundled clang
> fails on recent iOS SDK module maps. The committed sources were produced by parsing the shipped
> `-Swift.h` headers directly; `GenerateBindings.sh` documents the problem and honours a `SHARPIE`
> override for when it is fixed.

---

## Releasing

Tag it. `v3.14.0.1` builds, tests, publishes every package to nuget.org via trusted publishing, and
creates a GitHub release. The tag drives which native SDK is bound, so an older line can be released
by tagging it.

Pull requests publish a `-beta.<pr>.<run>` prerelease of the whole set.

Curated notes in `docs/release-notes/<version>.md` replace the generated commit list when present.

---

## Troubleshooting

**`DDDatadog`/`DDRUMMonitor`/`DDLogger` could not be found.** The types moved namespace in 3.0. Add
the per-module `using` — see [Migrating from 2.x](#migrating-from-2x).

**Nothing appears in Datadog.** Check `Site` matches your organisation's region — the most common
cause. Set `DDDatadog.SetVerbosityLevel(DDCoreLoggerLevel.Debug)` to see the SDK's own diagnostics
in the Xcode console.

**Crashes do not show up.** Crash reports are delivered through RUM in 3.x, so RUM must be enabled
alongside Crash Reporting, and dSYMs uploaded for symbolication.

**`This version of .NET for iOS requires Xcode 26.0`.** Only affects `net10.0-ios26.0`. See
[Building locally](#building-locally).

**A framework fails to load at runtime.** Usually a partially restored package. Clear the cached
copies and restore again: `rm -rf ~/.nuget/packages/datadognet.*`.

---

## Licence

The binding code in this repository is [MIT](LICENSE). The native binaries the packages ship are
built and published by Datadog and are Apache-2.0 — which covers `dd-sdk-ios`, KSCrash and
opentelemetry-swift alike. Every package declares `MIT AND Apache-2.0` and carries both texts under
`licenses/`.
