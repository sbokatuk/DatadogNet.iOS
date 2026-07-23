# DatadogFlagsObjc — a prototype, not yet shipped

`DatadogFlags` is one of two frameworks Datadog ships as **Swift only**: 15 public Swift types and
zero Objective-C ones. Nothing in this repository can bind it, because there is no Objective-C
surface to bind — the API leans on generics (`FlagDetails<T>`) and enums with associated values
(`AnyValue`), neither of which Swift projects into Objective-C.

This directory is the answer to "can we write that projection ourselves". It is a hand-written
`@objc` wrapper around the Swift API, and **it compiles against the real 3.14.0 framework with zero
warnings**:

```bash
xcrun swiftc -emit-module -emit-objc-header \
    -emit-objc-header-path DatadogFlagsObjc-Swift.h \
    -module-name DatadogFlagsObjc \
    -target arm64-apple-ios12.2 \
    -sdk "$(xcrun --sdk iphoneos --show-sdk-path)" \
    -F <directory of Datadog .frameworks> \
    DatadogFlagsObjc.swift
```

The `-Swift.h` that comes out is exactly what Objective Sharpie consumes, so the rest of the path to
a NuGet is the ordinary one this repository already runs for every other package.

## Status

**Prototype.** It compiles and the shape is settled; it has never been run. Not built, not packaged,
not bound, not referenced by anything. Nothing in `src/` knows it exists.

What remains before it could ship:

- an xcframework build producing **both** the `ios-arm64` and `ios-arm64_x86_64-simulator` slices,
  with `-enable-library-evolution`, linking the Datadog frameworks rather than embedding them
- a `DatadogNet.Flags.iOS` binding project with `ApiDefinitions.cs` over the generated header
- a device check that evaluates a flag and asserts what came back
- an `IFeatureFlags` in the façade — where **Android would be the platform with no implementation**,
  since dd-sdk-android 3.12.1 has no flags module at all

See [`docs/swift-interop-plan.md`](../../../DatadogNet/docs/swift-interop-plan.md) in the façade
repository for the full design, including why the same approach is worth doing for `DatadogProfiling`
and is **not** worth doing for OpenTelemetry.

## The risk, stated plainly

This wrapper is ours, and Datadog is under no obligation to keep `FlagsClientProtocol` source-stable
between releases. When they change it, this stops compiling — at our build time, which is the good
failure. That is the standing cost of the feature, and it should be a conscious one.
