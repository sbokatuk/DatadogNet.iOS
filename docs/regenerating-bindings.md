# Regenerating the bindings

The committed `ApiDefinitions.cs`/`StructsAndEnums.cs` files were produced from the frameworks'
generated Objective-C headers (`<Framework>-Swift.h`) and then hand-corrected. This page is the
process for carrying them across a dd-sdk-ios upgrade — because the tool that would automate it
is currently broken, and pretending otherwise makes upgrades slower, not faster.

## Why Objective Sharpie is out

Objective Sharpie 3.5.116 — the newest release — bundles a clang that fails to parse recent iOS
SDK module maps, so `./build/GenerateBindings.sh` cannot produce a fresh `Binding/` tree against
any Xcode this repository builds with. The script's header records the exact failure and honours
a `SHARPIE` override for the day a fixed release appears; until then, step "regenerate and diff"
is really "diff the headers and port by hand".

## The header diff is the work list

```bash
./build/DiffSwiftHeaders.sh 3.15.0
```

downloads that release's `Datadog.xcframework.zip`, prints its SHA-256 (the value step 1 of the
upgrade guide records in `build/checksums.txt` — same bytes, one download), and writes one
`build/<Framework>.Swift.h.diff` per framework whose generated header changed. A framework with
no diff needs nothing: its committed binding is already exact.

Reading a diff:

| In the header diff | In the committed binding |
| --- | --- |
| New `@interface` / `@protocol` | New `[BaseType]` interface in `ApiDefinitions.cs`. |
| New method/property on an existing type | New `[Export]` member on the existing interface. |
| `SWIFT_ENUM` added or extended | New/extended enum in `StructsAndEnums.cs` (check backing type). |
| Removed or renamed member | Remove/rename the export — a stale export crashes at runtime when called, not at build time. |
| `_Nullable` appearing on a parameter | Add `[NullAllowed]`. The reverse — `_Nonnull` — means do **not** add it, however tempting; see the `DDLogger` comment for a case where a `[NullAllowed]` "fix" would have turned a managed exception into a native crash. |
| Type moving between frameworks | Move the interface between binding projects and fix the project references. |

The hand-applied corrections the committed files carry on top of raw generation — the fixes a
regeneration would undo — are listed in `build/GenerateBindings.sh`'s header. Check a ported diff
against that list before committing.

## After porting

1. `./build/FetchXcFrameworks.sh` (the hash recorded above makes this pass).
2. `./build/BuildNugets.sh` and both test suites — the on-simulator smoke tests are what catch a
   selector that no longer exists at runtime.
3. Re-run `./build/DiffSwiftHeaders.sh libs <extracted-new>` if anything was ported from memory:
   zero diffs against the new headers is the done condition.
4. Sync `DatadogNet.Mac`: its binding sources are enforced verbatim copies of these
   (`DatadogNet.Mac/build/SyncBindingsFromiOS.sh`, guarded by its CI).
