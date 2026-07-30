---
applyTo: "src/**/ApiDefinitions.cs,src/**/StructsAndEnums.cs"
---

# Binding definitions

These files are **hand-maintained**, not generated output, even though they look like it.

- Never regenerate them with Objective Sharpie. `build/GenerateBindings.sh` cannot run against any
  Xcode this repository builds with, and it writes to `Binding/` rather than over these files
  precisely so a stray run cannot overwrite the hand-applied corrections.
- Change them only to mirror a change in the frameworks' generated `<Framework>-Swift.h` headers.
  Get the work list from `./build/DiffSwiftHeaders.sh <version>` and read it with
  `docs/regenerating-bindings.md`. Do not invent, tidy or "improve" API that upstream does not
  project into Objective-C.
- Preserve the existing style: tabs, Sharpie's space before the argument list
  (`[Export ("isInitialResourceFrom:")]`), the `// @interface …` / `// @protocol …` provenance
  comment above each type, and the `partial interface I<Name> {}` stubs for protocols.
- `[NullAllowed]` tracks the header exactly: add it where a parameter gained `_Nullable`, and never
  where the header says `_Nonnull` — relaxing a `_Nonnull` turns a managed exception into a native
  crash in the bridging thunk.
- Removing or renaming an export is an API break for consumers and a runtime crash for stale call
  sites. `./build/ValidatePackageApi.sh` will fail on it; check the `DatadogNet` umbrella too.
- `DatadogFlags`, `DatadogProfiling` and `OpenTelemetryApi` have deliberately near-empty definitions
  — upstream projects no Objective-C surface. The file must still exist: a binding project without
  an `ObjcBindingApiDefinition` does not build. Keep the comment explaining why.
- `DatadogNet.Mac` holds enforced verbatim copies of these files, so keep changes portable and sync
  that repository afterwards.
