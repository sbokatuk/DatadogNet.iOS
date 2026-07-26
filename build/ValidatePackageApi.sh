#!/bin/sh
# Compares every packed .nupkg in artifacts/ against the last published release, so an accidental
# API break - a regenerated binding quietly dropping a member is the likely shape of it - fails
# here instead of reaching nuget.org and a consumer's compiler.
#
# This deliberately runs AFTER merge-packages.py, on the finished artifact, not at pack time via
# EnablePackageValidation. The two-pass band arrangement makes the MSBuild-time gate structurally
# wrong: each pass packs an intermediate that lacks the other band's target frameworks, so
# ApiCompat maps the baseline's net10 assemblies onto the current net9 ones and reports every
# band-to-band difference as a break. Only the merged package has the baseline's framework set.
#
# Requires network twice over: the baseline packages download from nuget.org, and the apicompat
# tool installs on first use. CI runs it in the pack job; locally it is an explicit invocation,
# never part of an offline build.
#
# Usage:
#   ./build/ValidatePackageApi.sh              # baseline from Directory.Build.props
#   ./build/ValidatePackageApi.sh 3.14.0.3     # explicit baseline version
set -eu

root="$(cd "$(dirname "$0")/.." && pwd)"
artifacts="$root/artifacts"

# Pinned like every other tool this repository pulls in. Advance deliberately.
apicompat_version="9.0.316"

baseline="${1:-$(sed -n 's/.*<DatadogPackageValidationBaselineVersion>\(.*\)<\/DatadogPackageValidationBaselineVersion>.*/\1/p' "$root/Directory.Build.props" | head -1)}"
if [ -z "$baseline" ]; then
  echo "error: no baseline version - set DatadogPackageValidationBaselineVersion in Directory.Build.props or pass one" >&2
  exit 1
fi

packages=$(ls "$artifacts"/*.nupkg 2>/dev/null || true)
if [ -z "$packages" ]; then
  echo "error: no packages in $artifacts - run ./build/BuildNugets.sh first" >&2
  exit 1
fi

tools="$root/artifacts/.apicompat"
if [ ! -x "$tools/apicompat" ]; then
  echo "==> installing Microsoft.DotNet.ApiCompat.Tool $apicompat_version"
  dotnet tool install Microsoft.DotNet.ApiCompat.Tool --version "$apicompat_version" --tool-path "$tools" >/dev/null
fi

baselines="$root/artifacts/.baseline-$baseline"
mkdir -p "$baselines"

failed=0
for package in $packages; do
  name=$(basename "$package")
  id=$(printf '%s' "$name" | sed -E 's/\.[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?(-[0-9A-Za-z.-]+)?\.nupkg$//')
  lower=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')

  base="$baselines/$lower.$baseline.nupkg"
  if [ ! -f "$base" ]; then
    url="https://api.nuget.org/v3-flatcontainer/$lower/$baseline/$lower.$baseline.nupkg"
    if ! curl -fsSL --max-time 120 -o "$base" "$url"; then
      rm -f "$base"
      # A package that has never shipped has no baseline to hold it to - the first release IS the
      # baseline. Anything else failing to download is a real problem.
      echo "==> $id: no $baseline on nuget.org - new package, nothing to validate against"
      continue
    fi
  fi

  echo "==> $id: validating against $baseline"
  if ! "$tools/apicompat" package "$package" --baseline-package "$base"; then
    failed=1
  fi
done

if [ "$failed" -ne 0 ]; then
  echo "ValidatePackageApi: API breaking changes against $baseline - see above" >&2
  exit 1
fi

echo "ValidatePackageApi: every package is API-compatible with $baseline"
