#!/bin/sh
set -eu

# Diffs the generated Objective-C headers (-Swift.h) between the frameworks in libs/ and another
# dd-sdk-ios release - which is exactly the delta the committed ApiDefinitions.cs must absorb on
# an upgrade, now that Objective Sharpie cannot regenerate them (its bundled clang fails on
# recent iOS SDK module maps; see GenerateBindings.sh). The headers are the source of truth the
# committed bindings were produced from, so their diff IS the porting work list.
#
# Usage:
#   ./DiffSwiftHeaders.sh 3.15.0          # download that release, diff against libs/
#   ./DiffSwiftHeaders.sh DIR_OLD DIR_NEW # diff two directories of *.xcframework
#
# The download mode also prints the archive's SHA-256 so it can be compared against the pin
# UpdateChecksums.sh records in build/checksums.txt from GitHub's own asset digest - a third
# reading of the same bytes, independent of both the API and the pinning download.

INVOKE_DIR="$(pwd)"
cd "$(dirname "$0")"
ROOT="$(cd .. && pwd)"

absolute() {
    case "$1" in
        /*) printf '%s' "$1" ;;
        *)  printf '%s' "$INVOKE_DIR/$1" ;;
    esac
}

header_of() {
    # First device slice's -Swift.h for one framework name under one root.
    find "$1" -path "*/$2.xcframework/ios-arm64*/$2.framework/Headers/$2-Swift.h" 2>/dev/null | sort | head -1
}

if [ "$#" -eq 2 ]; then
    OLD_DIR="$(absolute "$1")"
    NEW_DIR="$(absolute "$2")"
elif [ "$#" -eq 1 ]; then
    version="$1"
    case "$version" in
        *[!A-Za-z0-9._-]*)
            echo "error: invalid version '$version'" >&2
            exit 1
            ;;
    esac
    OLD_DIR="$ROOT/libs"
    WORK="$(mktemp -d)"
    trap 'rm -rf "$WORK"' EXIT
    zip="$WORK/Datadog.xcframework.zip"

    echo "==> downloading dd-sdk-ios $version"
    curl -fSL -o "$zip" \
        "https://github.com/DataDog/dd-sdk-ios/releases/download/$version/Datadog.xcframework.zip"

    echo "==> SHA-256 of this download (must match the UpdateChecksums.sh pin in build/checksums.txt):"
    shasum -a 256 "$zip" | sed "s|$WORK/||"

    NEW_DIR="$WORK/extracted"
    mkdir -p "$NEW_DIR"
    unzip -q "$zip" -d "$NEW_DIR"
else
    echo "usage: $0 <version> | <old-dir> <new-dir>" >&2
    exit 1
fi

changed=0
unchanged=0
missing=0

for framework_dir in "$OLD_DIR"/*.xcframework; do
    [ -d "$framework_dir" ] || continue
    framework="$(basename "$framework_dir" .xcframework)"

    old_header="$(header_of "$OLD_DIR" "$framework")"
    new_header="$(header_of "$NEW_DIR" "$framework")"

    if [ -z "$old_header" ]; then
        # OpenTelemetryApi ships no -Swift.h at all; that is expected, not an upgrade signal.
        echo "--- $framework: no -Swift.h in the current libs/ (expected for OpenTelemetryApi)"
        continue
    fi
    if [ -z "$new_header" ]; then
        echo "!!! $framework: present in libs/ but absent from the new release - a framework was"
        echo "    dropped or renamed upstream. FetchXcFrameworks.sh will fail loudly on this too."
        missing=$((missing + 1))
        continue
    fi

    if diff -u "$old_header" "$new_header" > "$framework.Swift.h.diff" 2>&1; then
        rm -f "$framework.Swift.h.diff"
        unchanged=$((unchanged + 1))
    else
        lines=$(wc -l < "$framework.Swift.h.diff" | tr -d ' ')
        echo "==> $framework: CHANGED - $lines diff lines in build/$framework.Swift.h.diff"
        changed=$((changed + 1))
    fi
done

echo
summary="==> $changed changed, $unchanged unchanged"
[ "$missing" -gt 0 ] && summary="$summary, $missing MISSING"
echo "$summary"
if [ "$changed" -gt 0 ]; then
    echo "    Each .diff in build/ is the porting work list for that framework's ApiDefinitions.cs."
    echo "    See docs/regenerating-bindings.md for how to read one."
fi
