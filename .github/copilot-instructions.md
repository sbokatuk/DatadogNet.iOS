# DatadogNet.iOS

## What this repository is

- .NET for iOS and .NET MAUI bindings over the **prebuilt** native Datadog iOS SDK
  ([`DataDog/dd-sdk-ios`](https://github.com/DataDog/dd-sdk-ios)), pinned at **3.14.0**
  (`DatadogNativeVersion` in `Directory.Build.props`). Nothing is compiled from source.
- Twelve packages. Eleven bind one framework each — `DatadogNet.<Name>.iOS` for `Core`, `RUM`,
  `Logs`, `Trace`, `SessionReplay`, `WebViewTracking`, `CrashReporting`, `Flags`, `Profiling`,
  `Internal`, `OpenTelemetryApi`. `DatadogNet.Objc.iOS` ships no assembly and no native payload: it
  is a dependency-only compatibility meta-package left behind when dd-sdk-ios 3.0 deleted the
  `DatadogObjc` framework.
- Versions are `<native>.<binding revision>`, four parts. `3.14.0.5` is dd-sdk-ios 3.14.0, binding
  revision 5; only the fourth component moves when the native binaries stay put.
- `DatadogNet.Mac` copies this repository's binding sources verbatim and the `DatadogNet` umbrella
  pins these packages, so binding fixes land **here** first.

## Build and verify

macOS only. Xcode from the **26.0** line (.NET for iOS refuses any other for `net10.0-ios26.0`),
plus the .NET 9 and .NET 10 SDKs with the `ios`/`maui-ios` workload installed per band.

```bash
./build/CheckReadmeVersions.sh   # CI's first step; fails on stale README pins
./build/FetchXcFrameworks.sh     # ~200 MB into libs/, verified against build/checksums.txt
./build/BuildNugets.sh           # packs all twelve into artifacts/ (net9 + net10 passes, merged)
dotnet test tests/DatadogNet.iOS.PackageTests
./build/ValidatePackageApi.sh    # apicompat vs DatadogPackageValidationBaselineVersion
```

- `./build/BuildNugets.sh [version] [native-version]` — pass both to build another dd-sdk-ios line,
  and fetch that native version first.
- Simulator smoke tests: `./.github/scripts/run-simulator-tests.sh 3.14.0.5 net9.0-ios18.0`.
- Sample: `dotnet build samples/DatadogNet.iOS.Example/DatadogNetExample.csproj
  -p:RuntimeIdentifier=iossimulator-arm64 -p:DatadogPackageVersion=<version>`.
- The tests and the sample restore the **packed** `.nupkg`s from `artifacts/` (the `local-artifacts`
  source in `NuGet.config`), so pack before running them. The sample is deliberately outside
  `DatadogNet.sln`.
- `ValidatePackageApi.sh` needs network and runs on the merged artifact only — never at pack time.

## Layout

| Path | What |
| --- | --- |
| `src/DatadogNet.<Name>.iOS/` | `ApiDefinitions.cs` + `StructsAndEnums.cs` (hand-maintained sources) and `Additions/` — the hand-written convenience layer in Core, RUM, Logs and Trace |
| `src/Datadog.Binding.props` | Everything the binding projects share; imported explicitly by each `.csproj` |
| `build/` | `FetchXcFrameworks.sh`, `UpdateChecksums.sh` + `checksums.txt`, `BuildNugets.sh` + `merge-packages.py`, `ValidatePackageApi.sh`, `CheckReadmeVersions.sh`, `BumpNativeVersion.sh`, `DiffSwiftHeaders.sh`, `GenerateBindings.sh` (unusable — see below), `GenerateDeviceClassAliases.sh` + `device-class-aliases/`, `check-upstream.sh` + `upstream.tsv` |
| `libs/` | Fetched xcframeworks. Gitignored; the `ValidateDatadogXcFramework` target errors when absent |
| `shims/DatadogFlagsObjc/` | Unshipped prototype: a hand-written Swift `@objc` projection of the Swift-only `DatadogFlags`. Nothing in `src/` references it |
| `tests/` | `DatadogNet.iOS.PackageTests` (xunit over `artifacts/`), `DatadogNet.iOS.DeviceTests` (simulator app). There is no unit-test tier — no platform-neutral code exists here |
| `samples/DatadogNet.iOS.Example` | MAUI app, project `DatadogNetExample.csproj` |
| `docs/` | `regenerating-bindings.md`, `release-notes/<version>.md` |

## Conventions

- Keep `RootNamespace` as the bare framework name (`DatadogCore`, `DatadogRUM`, …) so 2.x call sites
  survive; `AssemblyName`/`PackageId` stay `DatadogNet.<Name>.iOS`.
- Every shipped version needs `docs/release-notes/<four-part version>.md`. It is packed as
  `PackageReleaseNotes`, and adding the file is what triggers the release.
- British spelling in prose ("licence", "initialise"), matching the README.
- Build files and scripts explain *why* at length. Keep that style when editing them.
- Changes to bindings land here, then sync `DatadogNet.Mac`; check the `DatadogNet` umbrella before
  touching anything public.

## CI and release flow

- `pr.yml` calls `build.yml` with `verify: true` and publishes `<version>-beta.<pr>.<run>` to
  nuget.org via OIDC trusted publishing. Fork pull requests build but skip the push.
- `build.yml` runs on `macos-15` behind `.github/actions/select-xcode`: `pack` (README check →
  cached fetch of `libs/` → pack → PackageTests → ValidatePackageApi), `sample` (Debug,
  simulator), `link-release` (Release **device** link for net9 and net10, asserting a `.app` with
  AOT images), `e2e` (simulator smoke, net8 and net10 by default).
- Release: merge a pull request that **adds** `docs/release-notes/<version>.md` → `auto-release.yml`
  tags `v<version>` and dispatches `release.yml` → `guard` proves the tag is an ancestor of the
  default branch and that it agrees with `Directory.Build.props` → `build.yml` with `verify: false`
  → every package pushed in one `dotnet nuget push` (they depend on each other at exact versions, so
  a partial push leaves ids that cannot restore) → `gh release create`.
- `upstream-drift.yml` (daily) and `upstream-watch.yml` (weekly) watch dd-sdk-ios. Add components to
  `build/upstream.tsv`, not to `check-upstream.sh`.

## Testing

- After every pack: PackageTests **and** `ValidatePackageApi.sh`.
- Anything touching bindings, `Additions/`, linker flags, `device-class-aliases/` or the native
  version also needs the simulator smoke tests **and** the Release device-link check. The device
  link is the only thing that catches missing `_OBJC_CLASS_$_DD*` symbols; every simulator build
  passes regardless.
- Adding or removing a framework means updating `FRAMEWORKS` in `FetchXcFrameworks.sh`, the binding
  project, and `Packages.All` in the package tests.

## Hard rules

- Never commit xcframeworks or anything under `libs/`; the native payload is fetched and
  checksum-pinned. Never weaken, skip or hand-edit `build/checksums.txt` verification — record pins
  only through `./build/UpdateChecksums.sh`, which anchors them to GitHub's published asset digest.
- Never regenerate bindings with Objective Sharpie: its bundled clang cannot parse modern iOS SDK
  module maps. Diff the generated headers with `./build/DiffSwiftHeaders.sh <version>` and port the
  delta by hand into the committed `ApiDefinitions.cs`/`StructsAndEnums.cs`.
- Never remove or rename members of `Additions/` without checking the `DatadogNet` umbrella, which
  compiles against them — `ValidatePackageApi.sh` fails on any API break regardless.
- Bump native versions with `./build/BumpNativeVersion.sh <version>` and keep the README pins in
  step; `CheckReadmeVersions.sh` is CI's first step. Regenerate `build/device-class-aliases/` with
  `./build/GenerateDeviceClassAliases.sh` after every bump and commit the result.
- Keep `ForceLoad=True`, `SmartLink=False` and the device-class-alias linker flags and
  `*Realize.xcframework` references in `src/Datadog.Binding.props`. Removing them reintroduces
  silent crash-handler failure and device-link breaks. Do not set `IsTrimmable`.
- Release only through the workflows. Never hand-push packages, and never bypass the guard job's
  ancestry check.

## References

- dd-sdk-ios [releases](https://github.com/DataDog/dd-sdk-ios/releases) and
  [Datadog iOS docs](https://docs.datadoghq.com/real_user_monitoring/mobile_and_tv_monitoring/ios/).
- `docs/regenerating-bindings.md` — upgrade and porting procedure.
  `build/device-class-aliases/README.md` — the device symbol repair.
- Siblings: [`sbokatuk/DatadogNet`](https://github.com/sbokatuk/DatadogNet) (umbrella),
  [`sbokatuk/DatadogNet.Mac`](https://github.com/sbokatuk/DatadogNet.Mac),
  [`sbokatuk/DatadogNet.Android`](https://github.com/sbokatuk/DatadogNet.Android).

Trust these instructions and search the codebase only when something here is incomplete or wrong.
