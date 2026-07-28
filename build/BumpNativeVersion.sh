#!/bin/sh
# Does the mechanical half of a dd-sdk-ios upgrade, so the human half can start at the part that
# needs judgement - porting the header diffs into the committed ApiDefinitions.cs files.
#
# What it does, in order:
#
#   * Directory.Build.props: DatadogNativeVersion to the new release, DatadogBindingRevision back
#     to 1, and DatadogPackageValidationBaselineVersion to the version being left behind - the
#     last one published, which is what the next pack validates against.
#   * build/UpdateChecksums.sh: pins the new archive's SHA-256 from the digest GitHub publishes,
#     verified against a fresh download of the asset.
#   * README.md: the dd-sdk-ios badge (label, image path, release link), the "Built against"
#     prose, the install-snippet pins and the device-check example.
#   * docs/release-notes/<new version>.md: scaffolded with TODOs in the established format,
#     unless it already exists.
#   * build/CheckReadmeVersions.sh: run at the end, so a rewrite this script missed is caught
#     here rather than in CI.
#
# It then prints what remains manual: the header diff, the fetch, the porting, the builds and
# tests, the notes, the tag. See the README's "Upgrading the Datadog SDK" section.
#
# Usage:
#   ./BumpNativeVersion.sh 3.15.0
#
# If a run dies halfway (offline during the checksum pin, say), reset with
# `git checkout -- Directory.Build.props README.md build/checksums.txt` and run it again.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
props="$root/Directory.Build.props"
readme="$root/README.md"
notes_dir="$root/docs/release-notes"

new="${1:-}"
if [ -z "$new" ]; then
    echo "usage: $0 <new-dd-sdk-ios-version>   e.g. $0 3.15.0" >&2
    exit 1
fi
if ! printf '%s' "$new" | grep -qE '^[0-9]+\.[0-9]+\.[0-9]+$'; then
    echo "error: '$new' does not look like a dd-sdk-ios version (expected e.g. 3.15.0)" >&2
    exit 1
fi

prop() {
    sed -n "s/.*<$1>\(.*\)<\/$1>.*/\1/p" "$props" | head -1
}

old_native="$(prop DatadogNativeVersion)"
old_revision="$(prop DatadogBindingRevision)"
if [ -z "$old_native" ] || [ -z "$old_revision" ]; then
    echo "error: could not read DatadogNativeVersion/DatadogBindingRevision from $props" >&2
    exit 1
fi
old_package="$old_native.$old_revision"
new_package="$new.1"

if [ "$new" = "$old_native" ]; then
    echo "error: already bound to dd-sdk-ios $old_native - nothing to bump" >&2
    exit 1
fi

# The version being left behind becomes the package-validation baseline, which only works if it
# was actually published. The tag is the best offline evidence; its absence is a note rather
# than a failure, since a shallow clone has no tags at all.
if ! git -C "$root" tag -l "v$old_package" 2>/dev/null | grep -q .; then
    echo "note: no v$old_package tag here - if that version was never published, point"
    echo "      DatadogPackageValidationBaselineVersion at the newest version that was."
fi

echo "==> $props: $old_native -> $new, revision $old_revision -> 1, baseline -> $old_package"
sed \
    -e "s|<DatadogNativeVersion>$old_native</DatadogNativeVersion>|<DatadogNativeVersion>$new</DatadogNativeVersion>|" \
    -e "s|<DatadogBindingRevision>$old_revision</DatadogBindingRevision>|<DatadogBindingRevision>1</DatadogBindingRevision>|" \
    -e "s|<DatadogPackageValidationBaselineVersion>.*</DatadogPackageValidationBaselineVersion>|<DatadogPackageValidationBaselineVersion>$old_package</DatadogPackageValidationBaselineVersion>|" \
    "$props" > "$props.tmp" && mv "$props.tmp" "$props"

echo "==> pinning the $new archive checksum"
"$root/build/UpdateChecksums.sh" "$new"

echo "==> rewriting the README badge, prose and version pins"
sed \
    -e "s|\[!\[dd-sdk-ios $old_native\]|[![dd-sdk-ios $new]|" \
    -e "s|dd--sdk--ios-$old_native-|dd--sdk--ios-$new-|" \
    -e "s|dd-sdk-ios/releases/tag/$old_native|dd-sdk-ios/releases/tag/$new|" \
    -e "s|Built against \*\*dd-sdk-ios $old_native\*\*|Built against **dd-sdk-ios $new**|" \
    -e "s|Version=\"$old_package\"|Version=\"$new_package\"|g" \
    -e "s|run-simulator-tests\.sh $old_package|run-simulator-tests.sh $new_package|" \
    "$readme" > "$readme.tmp" && mv "$readme.tmp" "$readme"

notes="$notes_dir/$new_package.md"
if [ -f "$notes" ]; then
    echo "==> $notes already exists; leaving it alone"
else
    echo "==> scaffolding $notes"
    cat > "$notes" <<NOTES
## What's changed

Native upgrade: the packages now bind
[dd-sdk-ios $new](https://github.com/DataDog/dd-sdk-ios/releases/tag/$new), up from \`$old_native\`.

> **Package versions are \`<dd-sdk-ios version>.<binding revision>\`.** \`$new_package\` is dd-sdk-ios
> \`$new\`, binding revision \`1\`. The fourth component belongs to this repository and advances when
> the bindings or packaging change while the native binaries stay put.

TODO: summarise what the upgrade means for a consumer, in the format of the previous files in
this directory - consumer-visible changes first, each with why it matters, then documentation and
tests. The raw material is upstream's release notes and the \`build/*.Swift.h.diff\` files that
\`./build/DiffSwiftHeaders.sh $new\` writes.

## Upgrading from $old_package

TODO: state exactly what a consumer must change - or that it is a version bump alone.
NOTES
fi

echo "==> checking the README rewrite"
"$root/build/CheckReadmeVersions.sh"

cat <<REMAINING

==> done. What remains is the part that needs judgement:

  1. ./build/DiffSwiftHeaders.sh $new   - run BEFORE fetching, while libs/ still holds
     $old_native: writes one build/<Framework>.Swift.h.diff per framework whose generated
     header changed, which is the porting work list (docs/regenerating-bindings.md).
  2. ./build/FetchXcFrameworks.sh       - replaces libs/ with the $new frameworks.
  3. ./build/GenerateDeviceClassAliases.sh - regenerates build/device-class-aliases/ for the new
     binaries; the Swift mangled names the aliases point at change with every native release,
     and the PackageTests symbol audit fails on stale ones.
  4. Port the diffs into the committed ApiDefinitions.cs files.
  5. ./build/BuildNugets.sh, then both test suites (PackageTests, simulator).
  6. Finish docs/release-notes/$new_package.md - the TODOs mark what cannot be generated.
  7. Commit, PR, and tag v$new_package once merged (see the README's Releasing section).
REMAINING
