#!/bin/sh
# Records the SHA-256 pin for one dd-sdk-ios release in build/checksums.txt, anchored to the
# digest GitHub itself publishes for the release asset rather than to whatever a first download
# happened to contain.
#
# The releases API reports each asset's digest ("sha256:<hex>"), computed by GitHub at upload
# time. Pinning that value closes the trust-on-first-use hole the old "download it and record
# what you got" instructions had: a machine whose download is tampered with - or an asset
# replaced before anyone here pinned it - would have minted the poisoned hash as the new truth.
# The asset is still downloaded and hashed by this script, so the two sources must agree before
# anything is recorded; a download that does not match the published digest is exactly the event
# the pins exist to catch, and stops this script loudly.
#
# Run it after bumping DatadogNativeVersion (build/BumpNativeVersion.sh does), then diff the
# result - a pin that CHANGED for an unchanged version means the release asset was replaced in
# place, and wants investigating rather than committing.
#
# Usage:
#   ./UpdateChecksums.sh          # version from Directory.Build.props
#   ./UpdateChecksums.sh 3.15.0   # pin a different release, e.g. ahead of a bump
#
# FetchXcFrameworks.sh verifies every download against the recorded pin, and cross-checks the
# pin against the live digest whenever the API is reachable.
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
props="$root/Directory.Build.props"
out="$root/build/checksums.txt"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

version="${1:-}"
if [ -z "$version" ]; then
    version=$(sed -n 's:.*<DatadogNativeVersion>\(.*\)</DatadogNativeVersion>.*:\1:p' "$props" | head -1)
fi
if [ -z "$version" ]; then
    echo "error: could not read DatadogNativeVersion from $props" >&2
    exit 1
fi

# Interpolated into URLs and matched against pin lines, so reject anything exotic up front.
case "$version" in
    *[!A-Za-z0-9._-]*)
        echo "error: invalid version '$version'" >&2
        exit 1
        ;;
esac

echo "==> asking the GitHub releases API for the $version asset digest"
digest=$(curl -fsSL "https://api.github.com/repos/DataDog/dd-sdk-ios/releases/tags/$version" | python3 -c '
import json
import sys

release = json.load(sys.stdin)
for asset in release.get("assets", []):
    if asset.get("name") == "Datadog.xcframework.zip":
        digest = asset.get("digest") or ""
        if digest.startswith("sha256:"):
            print(digest[len("sha256:"):].lower())
        break
')
if [ -z "$digest" ]; then
    echo "error: the releases API served no sha256 digest for Datadog.xcframework.zip $version" >&2
    echo "       (release missing, asset renamed, or GitHub stopped publishing digests)" >&2
    exit 1
fi
case "$digest" in
    *[!0-9a-f]*)
        echo "error: '$digest' does not look like a SHA-256" >&2
        exit 1
        ;;
esac

# Download the same asset and require it to hash to the published digest: two independent paths
# to one value - what GitHub says it stored, and what it actually serves today.
echo "==> downloading Datadog.xcframework.zip $version to verify the digest against real bytes"
archive="$work/Datadog.xcframework.zip"
curl -fSL -o "$archive" \
    "https://github.com/DataDog/dd-sdk-ios/releases/download/$version/Datadog.xcframework.zip"
if command -v shasum >/dev/null 2>&1; then
    actual=$(shasum -a 256 "$archive" | cut -d' ' -f1)
else
    actual=$(sha256sum "$archive" | cut -d' ' -f1)
fi
if [ "$actual" != "$digest" ]; then
    echo "error: the downloaded archive does not match the digest GitHub publishes for it" >&2
    echo "  published $digest" >&2
    echo "  download  $actual" >&2
    echo "  Nothing was recorded. Either the download was corrupted or tampered with in transit," >&2
    echo "  or the asset was replaced mid-request; retry, and investigate upstream if it persists." >&2
    exit 1
fi

previous=$(sed -n "s/^$version[[:space:]]\{1,\}\([0-9a-f]\{64\}\).*/\1/p" "$out" | head -1)
if [ -n "$previous" ] && [ "$previous" != "$digest" ]; then
    # Same version, different hash: for an asset that is supposed to be immutable, this is the
    # replacement event the pins exist to catch. Recording it is a deliberate act - the diff of
    # checksums.txt is the audit trail - but it must never happen silently.
    echo "WARNING: the pin for $version is changing" >&2
    echo "  was $previous" >&2
    echo "  now $digest" >&2
    echo "  The release asset was replaced upstream. Diff the frameworks before committing this." >&2
fi

# The whole file is rewritten - canonical header, then the pins in their existing order with this
# version replaced in place or appended - so the instructions above the pins cannot drift from
# what this script actually does.
pins="$work/pins"
awk -v version="$version" -v digest="$digest" '
    /^#/ || /^[[:space:]]*$/ { next }
    $1 == version { print version, digest; replaced = 1; next }
    { print }
    END { if (!replaced) print version, digest }
' "$out" > "$pins"

{
    echo "# SHA-256 of each dd-sdk-ios release's Datadog.xcframework.zip."
    echo "#"
    echo "# Upstream publishes no checksum manifest of its own, but the GitHub releases API reports"
    echo "# the digest it computed for every asset at upload time. build/UpdateChecksums.sh records"
    echo "# that digest here - after downloading the asset and requiring the bytes to hash to the"
    echo "# same value - so the pin is anchored to what GitHub stored, not to whatever one machine's"
    echo "# first download happened to contain. FetchXcFrameworks.sh refuses an archive that does"
    echo "# not match, and cross-checks the pin against the live digest when the API is reachable."
    echo "#"
    echo "# That makes this file the trust anchor for ~116 MB of native code that gets linked into"
    echo "# consumers' apps: a substituted or truncated archive fails the fetch instead of being"
    echo "# bound and published. It also makes the build reproducible, since a GitHub release asset"
    echo "# can be replaced in place without the tag changing."
    echo "#"
    echo "# To add or re-pin a version:"
    echo "#"
    echo "#   ./build/UpdateChecksums.sh 3.15.0"
    echo "#"
    echo "# Format: <dd-sdk-ios version> <sha256>"
    echo ""
    cat "$pins"
} > "$out.tmp" && mv "$out.tmp" "$out"

if [ "$previous" = "$digest" ]; then
    echo "==> $version was already pinned at this digest; verified and unchanged"
else
    echo "==> pinned $version $digest"
fi
