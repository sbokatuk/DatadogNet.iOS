#!/bin/sh

set -e

# Downloads the native Datadog xcframeworks the bindings are built against, into ../libs/.
#
# Upstream publishes one archive per release - Datadog.xcframework.zip - containing every
# framework the SDK is split into, already built. That replaces the Carthage step the previous
# DataDog-SDK-iOS-net repository used: there is no longer any reason to compile dd-sdk-ios,
# plcrashreporter and opentelemetry-swift from source just to bind them, and doing so made the
# output depend on whichever Xcode happened to be selected.
#
# Usage:
#   ./FetchXcFrameworks.sh          # version from Directory.Build.props
#   ./FetchXcFrameworks.sh 2.17.0   # override the version
#
# The archive also carries an arm64e twin (Datadog-with-arm64e.xcframework.zip). It is not used:
# App Store binaries are never arm64e, so the extra slice would be dead weight in every package.

cd "$(dirname "$0")"

ROOT="$(cd .. && pwd)"
PROPS="$ROOT/Directory.Build.props"
CHECKSUMS="$ROOT/build/checksums.txt"
LIBS="$ROOT/libs"
RELEASE_BASE="https://github.com/DataDog/dd-sdk-ios/releases/download"

# Read the version from Directory.Build.props rather than repeating it here: the binding projects
# resolve the xcframeworks through the same property, so a second copy that drifts out of sync
# fails the build with a confusing "file not found" on frameworks nobody downloaded.
DATADOG_VERSION="$1"
if [ -z "$DATADOG_VERSION" ]; then
    DATADOG_VERSION=$(sed -n 's:.*<DatadogNativeVersion>\(.*\)</DatadogNativeVersion>.*:\1:p' "$PROPS" | head -1)
fi

if [ -z "$DATADOG_VERSION" ]; then
    echo "error: could not read DatadogNativeVersion from $PROPS" >&2
    exit 1
fi

# The version is interpolated into a URL and a path, so reject anything exotic up front.
case "$DATADOG_VERSION" in
    *[!A-Za-z0-9._-]*)
        echo "error: invalid version '$DATADOG_VERSION'" >&2
        exit 1
        ;;
esac

# The eleven frameworks the archive ships. Listed rather than globbed so that a framework
# appearing or disappearing upstream is a loud failure here rather than a package that silently
# stops being built. 3.0 exercised exactly that: DatadogObjc was removed and its contents folded
# into the product modules, CrashReporter went away when KSCrash replaced PLCrashReporter, and
# DatadogFlags and DatadogProfiling appeared.
FRAMEWORKS="DatadogCore DatadogCrashReporting DatadogFlags DatadogInternal DatadogLogs DatadogProfiling DatadogRUM DatadogSessionReplay DatadogTrace DatadogWebViewTracking OpenTelemetryApi"

expected=$(sed -n "s/^$DATADOG_VERSION[[:space:]]\{1,\}\([0-9a-f]\{64\}\).*/\1/p" "$CHECKSUMS" | head -1)
if [ -z "$expected" ]; then
    echo "error: no SHA-256 recorded for $DATADOG_VERSION in $CHECKSUMS" >&2
    echo "       see the instructions at the top of that file for how to add one" >&2
    exit 1
fi

sha256_of() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d' ' -f1
    else
        sha256sum "$1" | cut -d' ' -f1
    fi
}

# Every framework carries iOS, tvOS and (for CrashReporter and OpenTelemetryApi) macCatalyst,
# macOS, watchOS and visionOS slices, plus a full set of dSYMs. Only the two iOS slices are
# reachable from a net*-ios binding, and the whole xcframework is zipped into each assembly's
# .resources.zip once per target framework - so with eleven packages and three target frameworks
# the difference is between packages totalling roughly 1 GB and roughly 300 MB.
#
# The slice directories cannot simply be deleted: AvailableLibraries in Info.plist would still
# advertise them, and the iOS SDK rejects an xcframework whose manifest points at a missing slice.
# So the manifest is rewritten to match. Two things make this less obvious than it looks:
#
#   1. macCatalyst reports SupportedPlatform 'ios' and is named ios-arm64_x86_64-maccatalyst. It
#      is identified by SupportedPlatformVariant, not by the platform or the directory name.
#   2. Each entry carries DebugSymbolsPath pointing at the dSYMs directory. Removing the dSYMs
#      without removing the key leaves the manifest referring to something that is gone.
#
# plistlib is stdlib, and python3 is already required by the package merge step.
strip_to_ios_slices() {
    python3 - "$1" <<'PYTHON'
import plistlib
import shutil
import sys
from pathlib import Path

root = Path(sys.argv[1])

def is_ios_device_or_simulator(library):
    """True for the plain-iOS slices, excluding the macCatalyst one.

    macCatalyst is not a platform of its own in this manifest - it is iOS with a variant - so
    filtering on SupportedPlatform alone keeps it.
    """
    if library.get("SupportedPlatform") != "ios":
        return False
    return library.get("SupportedPlatformVariant") in (None, "simulator")

for manifest in sorted(root.glob("*.xcframework/Info.plist")):
    with manifest.open("rb") as handle:
        plist = plistlib.load(handle)

    framework = manifest.parent
    libraries = plist.get("AvailableLibraries", [])
    keep = [lib for lib in libraries if is_ios_device_or_simulator(lib)]
    drop = [lib for lib in libraries if not is_ios_device_or_simulator(lib)]

    # Exactly one device and one simulator slice is what every framework here ships. Anything
    # else means upstream changed the layout, and guessing would produce a package that fails to
    # link in a consuming app rather than here.
    simulators = [lib for lib in keep if lib.get("SupportedPlatformVariant") == "simulator"]
    devices = [lib for lib in keep if lib.get("SupportedPlatformVariant") is None]
    if len(simulators) != 1 or len(devices) != 1:
        raise SystemExit(
            f"error: {framework.name} has {len(devices)} iOS device and "
            f"{len(simulators)} iOS simulator slices, expected one of each"
        )

    for library in drop:
        shutil.rmtree(framework / library["LibraryIdentifier"], ignore_errors=True)

    for library in keep:
        # dSYMs are only useful for symbolicating crashes in the native SDK itself, which is done
        # against the copies on Datadog's release page, not against a copy embedded in every
        # consuming app's package. They are roughly half the payload.
        symbols = library.pop("DebugSymbolsPath", None)
        if symbols:
            shutil.rmtree(framework / library["LibraryIdentifier"] / symbols, ignore_errors=True)

    plist["AvailableLibraries"] = keep
    with manifest.open("wb") as handle:
        plistlib.dump(plist, handle)
PYTHON
}

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

archive="$WORK_DIR/Datadog.xcframework.zip"

echo "==> fetching dd-sdk-ios $DATADOG_VERSION"
if ! curl -fsSL -o "$archive" "$RELEASE_BASE/$DATADOG_VERSION/Datadog.xcframework.zip"; then
    echo "error: could not download Datadog.xcframework.zip for $DATADOG_VERSION - does that release exist?" >&2
    exit 1
fi

actual=$(sha256_of "$archive")
if [ "$actual" != "$expected" ]; then
    echo "error: checksum mismatch for Datadog.xcframework.zip $DATADOG_VERSION" >&2
    echo "  expected $expected" >&2
    echo "  actual   $actual" >&2
    exit 1
fi

echo "==> extracting"
rm -rf "$LIBS"
mkdir -p "$LIBS"

# -q because eleven frameworks across six platforms is tens of thousands of lines of file listing.
unzip -q "$archive" -d "$WORK_DIR"

# The archive wraps everything in a Datadog.xcframework/ directory which is not itself an
# xcframework, so the frameworks are lifted out of it rather than kept nested.
for framework in $FRAMEWORKS; do
    source_path="$WORK_DIR/Datadog.xcframework/$framework.xcframework"
    if [ ! -d "$source_path" ]; then
        echo "error: $framework.xcframework is not in the $DATADOG_VERSION archive" >&2
        echo "       upstream may have added or removed a framework; update FRAMEWORKS in this script" >&2
        exit 1
    fi
    mv "$source_path" "$LIBS/"
done

# macOS archives carry AppleDouble (._*) companions and __MACOSX/ directories. Left in place they
# end up inside the packed .resources.zip, and the iOS SDK treats a stray ._Info.plist beside a
# real one as a malformed xcframework.
find "$LIBS" -name '._*' -delete
rm -rf "$LIBS/__MACOSX"

echo "==> stripping to iOS slices"
strip_to_ios_slices "$LIBS"

echo "==> done: $(du -sh "$LIBS" | cut -f1) in $LIBS"
