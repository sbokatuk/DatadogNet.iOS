#!/bin/sh

set -e

# Regenerates the Objective Sharpie binding definitions from the fetched xcframeworks, into
# ../Binding/<Framework>/.
#
# Run this after bumping DatadogNativeVersion and re-running FetchXcFrameworks.sh.
#
# Usage:
#   ./GenerateBindings.sh              # every framework
#   ./GenerateBindings.sh DatadogObjc  # just one
#
# Requires Objective Sharpie, which is not on nuget.org and has to be obtained separately:
#
#   https://aka.ms/objective-sharpie
#
# Set SHARPIE to point at the binary if it is not on PATH. The installer needs administrator
# rights, but the package can also just be expanded and run in place, which is enough:
#
#   curl -fsSL -o sharpie.pkg https://aka.ms/objective-sharpie
#   pkgutil --expand-full sharpie.pkg sharpie-x
#   export SHARPIE="$PWD/sharpie-x/Framework.pkg/Payload/Library/Frameworks/ObjectiveSharpie.framework/Versions/*/bin/sharpie"
#
# KNOWN LIMITATION: Objective Sharpie 3.5.116 (the current release) bundles a clang that cannot
# parse the module maps in recent iOS SDKs. Against the 18.5 and 26.5 SDKs it fails with
#
#     module '_stddef' requires feature 'found_incompatible_headers__check_search_paths'
#     unknown type name 'size_t'
#
# before emitting anything. It also refuses to bind a framework whose recorded DTSDKName is not
# installed ("framework requires SDK 'iphoneos18.2'"), which is normal for a downloaded binary.
# Until sharpie ships a newer clang, the 2.30.2 binding delta was produced by reading the shipped
# -Swift.h headers directly and diffing them against the previous version's - see the release
# notes. Keep this script for the day it works again.
#
# IMPORTANT: the output is a starting point, not a drop-in replacement. It is written to
# ../Binding/ deliberately, *not* over the committed ApiDefinitions.cs files, because those carry
# fixes that regenerating would silently undo. Diff the two and port the real changes across.
#
# The fixes currently carried in the committed sources, all of which sharpie will re-emit wrongly:
#
#   * DatadogInternal: sharpie binds three URLSession overloads that INSUrlSessionDataDelegate
#     already declares. Binding both registers each selector twice, and *every* app using the
#     package then dies at startup during assembly registration. They are deliberately absent from
#     the committed file - see the comment there.
#
#   * [Verify] attributes: sharpie emits these wherever it wants a human to check its guess. They
#     do not compile, so they are stripped, but each one marks a place worth reading.
#
#   * void*, CFUUIDRef* and similar C types reach C# as unusable signatures and are rewritten to
#     IntPtr / NSUuid.
#
# The previous repository automated the rewrites with a pile of sed commands (fixgen.sh) and ran
# them straight over the committed files. That is why the duplicate-selector crash survived: there
# was no diff step in which anyone would have seen it.

cd "$(dirname "$0")"

ROOT="$(cd .. && pwd)"
LIBS="$ROOT/libs"
OUTPUT="$ROOT/Binding"

SHARPIE="${SHARPIE:-sharpie}"
if ! command -v "$SHARPIE" >/dev/null 2>&1; then
    echo "error: sharpie not found - set SHARPIE, or see https://aka.ms/objective-sharpie" >&2
    exit 1
fi

if [ ! -d "$LIBS" ]; then
    echo "error: no xcframeworks in $LIBS. Run ./FetchXcFrameworks.sh first." >&2
    exit 1
fi

FRAMEWORKS="$1"
if [ -z "$FRAMEWORKS" ]; then
    FRAMEWORKS="DatadogCore DatadogCrashReporting DatadogFlags DatadogInternal DatadogLogs DatadogProfiling DatadogRUM DatadogSessionReplay DatadogTrace DatadogWebViewTracking OpenTelemetryApi"
fi

# The SDK the headers are parsed against. Nothing is compiled here, so this only has to be a
# version whose headers exist; it does not have to match what the packages target.
SDK="$(xcrun --sdk iphoneos --show-sdk-version)"

for framework in $FRAMEWORKS; do
    xcframework="$LIBS/$framework.xcframework"
    if [ ! -d "$xcframework" ]; then
        echo "error: $xcframework does not exist" >&2
        exit 1
    fi

    # The device slice, whatever it is named - upstream ships ios-arm64_arm64e for most frameworks
    # and plain ios-arm64 for others, and has renamed them between releases. The simulator slice
    # carries the same headers, so either would do; the device one is picked for determinism.
    slice="$(find "$xcframework" -maxdepth 1 -type d -name 'ios-*' ! -name '*-simulator' | head -1)"
    if [ -z "$slice" ]; then
        echo "error: $framework.xcframework has no iOS device slice" >&2
        exit 1
    fi

    target="$OUTPUT/$framework"
    rm -rf "$target"
    mkdir -p "$target"

    echo "==> binding $framework"

    # -scope keeps the output to this framework's own headers: without it, a framework that
    # imports another (all of them import DatadogInternal) has the imported API duplicated into
    # its definitions, and the same type ends up bound in several packages.
    "$SHARPIE" bind \
        --output "$target" \
        --namespace "$framework" \
        --sdk "iphoneos$SDK" \
        -scope "$slice/$framework.framework/Headers" \
        -framework "$slice/$framework.framework" \
        || echo "   (sharpie reported problems for $framework - read the output above before using it)"
done

echo
echo "==> generated into $OUTPUT"
echo "    Diff against the committed sources before copying anything across:"
echo
for framework in $FRAMEWORKS; do
    echo "      diff Binding/$framework/ApiDefinitions.cs src/DatadogNet.<Package>.iOS/ApiDefinitions.cs"
    break
done
echo
echo "    See the notes at the top of this script for the fixes the committed files carry."
