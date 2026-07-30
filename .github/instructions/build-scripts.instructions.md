---
applyTo: "build/*.sh"
---

# Build scripts

- POSIX `sh` with `set -e` or `set -eu`. `check-upstream.sh` is the one exception: it is
  `#!/usr/bin/env bash` with `set -euo pipefail` because it uses arrays. Do not add bashisms
  anywhere else, and do not add new dependencies — these run on a bare macOS runner, with `python3`
  for the parts shell cannot do (`merge-packages.py`, the alias generator).
- The commented header **is** the documentation for each script: what it does, why it exists, and a
  `Usage:` block. Update it in the same change as the behaviour, and keep the explanations of past
  failures — they are why the code is shaped as it is.
- Read versions from `Directory.Build.props` (`DatadogNativeVersion`, `DatadogBindingRevision`,
  `DatadogPackageValidationBaselineVersion`) rather than hardcoding them, and keep the "argument
  overrides the property" pattern the existing scripts use.
- Fail loudly and early. A missing framework, an unexpected checksum or an unresolvable download is
  an error to report, never something to skip past or repair silently.
- Never weaken the SHA-256 verification in `FetchXcFrameworks.sh`/`UpdateChecksums.sh`, and never
  write a pin from a bare download — it must agree with the digest GitHub publishes for the asset.
- Scripts invoked by CI must keep working unchanged from a developer's shell: resolve paths from the
  script's own location, never from the caller's working directory.
- `DatadogNet.Mac` carries hand-synced copies of several of these scripts; keep shared behaviour and
  option names aligned rather than diverging locally.
