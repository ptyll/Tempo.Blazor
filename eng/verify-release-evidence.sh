#!/usr/bin/env bash
set -euo pipefail

# DEC-TEMPO-RELEASE-GATE, second clause, made machine-readable. The release condition is the CI
# filter AND, at the same time, an owner-verified green full-suite E2E run over the commit being
# published — or over a commit differing from it only in files that cannot change the outcome.
# Until this script existed the contract lived only in the publish workflows' comments: nothing
# read the evidence, so a tag could publish a commit no full suite ever ran over.
#
# THE EVIDENCE SHAPE is the flat eng/release-evidence/e2e-full-run.json — flat on purpose, so it
# can be read with sed/grep and no jq dependency, the same trade verify-announced-version.sh and
# eng/pack-nuget-packages.sh already make. Every required key missing is a refusal, not a default:
# an unreadable evidence file must produce red, not silence.
#
# THE STALENESS BOUND — owner decision DEC-TEMPO-RELEASE-EVIDENCE-SCOPE (ptyll, 2026-09-22, F14):
# the evidence is valid ONLY when every path changed between the run commit and the tagged commit
# stays OUTSIDE what compiles into the package. The owner's allowed list is exhaustive —
#   *.md                   (any markdown file — prose never compiles into the package)
#   docs/                  (documentation tree)
#   eng/release-evidence/  (the evidence store itself — legitimately changes when a run is recorded)
#   .github/               (CI definition — not compiled into the package)
# ANY change in src/ or tests/ — explicitly including bUnit and other non-E2E test projects —
# invalidates the evidence and the full run must be re-measured; anything else outside the
# allowed list (scripts/, eng/*.sh, package manifests, …) invalidates too, fail-closed by
# omission. The bound is an ALLOWLIST precisely so a path nobody enumerated cannot slip through:
# refuse anything NOT matching STALE_ALLOWED_PATTERN rather than enumerating what may break it.
STALE_ALLOWED_PATTERN='(\.md$|^docs/|^eng/release-evidence/|^\.github/)'

# THE GIT QUESTIONS this asks — cat-file -e and merge-base --is-ancestor — need the full ref
# store. The publish workflows check out with fetch-depth: 0 for exactly this reason; the script
# deliberately does NOT probe clone depth itself (a shallow clone would report the honest
# "commit does not exist" refusal, which is already fail-closed).
#
# RELEASE_EVIDENCE_PATH exists for the same reason CHANGELOG_PATH does in
# verify-announced-version.sh: so the refusal can be exercised over a fixture instead of editing
# the real evidence. Nothing in CI sets it.

RELEASE_EVIDENCE_PATH="${RELEASE_EVIDENCE_PATH:-eng/release-evidence/e2e-full-run.json}"

refuse() {
  echo "verify-release-evidence: $1" >&2
  exit 1
}

if [[ ! -f "$RELEASE_EVIDENCE_PATH" ]]; then
  refuse "release evidence '$RELEASE_EVIDENCE_PATH' does not exist — DEC-TEMPO-RELEASE-GATE requires a recorded owner-verified full-suite E2E run; an absent file is not a green run."
fi

# Flat-JSON field reader: '"key": "value"' or '"key": 123' — the evidence schema forbids nested
# objects, so a single sed line per key is the whole parser.
json_value() {
  sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^,\"}]*\).*/\1/p" "$RELEASE_EVIDENCE_PATH" | head -n 1
}

required_keys=(commit verifiedDate verifiedBy runName passed failed skipped total serialResidualTotal serialResidualFailed wallClock artifactsPath)
for key in "${required_keys[@]}"; do
  if [[ -z "$(json_value "$key")" ]]; then
    refuse "release evidence is missing required key '$key' — a partial record is not a recorded run."
  fi
done

commit="$(json_value commit)"
verified_by="$(json_value verifiedBy)"
passed="$(json_value passed)"
failed="$(json_value failed)"
skipped="$(json_value skipped)"
total="$(json_value total)"
serial_residual_total="$(json_value serialResidualTotal)"
serial_residual_failed="$(json_value serialResidualFailed)"

for key in passed failed skipped total serialResidualTotal serialResidualFailed; do
  value="$(json_value "$key")"
  if ! [[ "$value" =~ ^[0-9]+$ ]]; then
    refuse "release evidence key '$key' is '$value', not a non-negative integer — a count that cannot be read cannot be checked."
  fi
done

if ! git cat-file -e "${commit}^{commit}" 2>/dev/null; then
  refuse "evidence commit '$commit' does not exist in this clone — the run it claims cannot be tied to this repository."
fi

if ! git merge-base --is-ancestor "$commit" HEAD; then
  refuse "evidence commit '$commit' is not an ancestor of the HEAD being published — the green run belongs to a side line, not to this release."
fi

# THE DIFF IS CAPTURED BEFORE THE LOOP, not streamed into it: <(git diff …) runs the command in
# a process substitution whose failure nothing observes — a broken git (measured with a
# PATH-shadowed fake that exits 128 on `diff`) produced an EMPTY list, so the staleness bound
# iterated nothing and the run read as clean. In a command substitution the failure becomes this
# assignment's exit status, which `set -e` turns into a refusal — the same contract every other
# read in this script already keeps.
changed_paths="$(git diff --name-only "$commit" HEAD)"
while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  if ! grep -Eq "$STALE_ALLOWED_PATTERN" <<<"$path"; then
    refuse "staleness bound broken: '$path' changed between the verified run ($commit) and HEAD — DEC-TEMPO-RELEASE-EVIDENCE-SCOPE allows only *.md, docs/, eng/release-evidence/ and .github/ to change without re-measuring; any src/ or tests/ change (bUnit included) invalidates the evidence."
  fi
done <<<"$changed_paths"

if [[ "$serial_residual_failed" != "0" ]]; then
  refuse "the recorded run has $serial_residual_failed test(s) still red after serial re-measurement — a deterministic failure cannot ship under DEC-TEMPO-RELEASE-GATE."
fi

if (( failed > serial_residual_total )); then
  refuse "the recorded run reports $failed parallel failure(s) but only $serial_residual_total were sent to serial re-measurement — a red without a verdict is not a green run."
fi

if (( passed + failed + skipped != total )); then
  refuse "the recorded run is internally inconsistent: passed($passed) + failed($failed) + skipped($skipped) != total($total) — fix the evidence, never the arithmetic."
fi

echo "[release-evidence] commit=$commit passed=$passed failed=$failed skipped=$skipped total=$total serialResidual=$serial_residual_failed/$serial_residual_total verifiedBy=$verified_by"
exit 0
