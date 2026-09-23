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
# THE STALENESS BOUND — owner decision 2026-09-23, policy (B) "zúžená": the bound breaks ONLY on
# changes under
#   src/                       (code compiled into the packages)
#   tests/Tempo.Blazor.E2E/    (the suite the evidence claims ran green)
#   .github/workflows/         (the publish path itself)
#   eng/*.sh                   (the release scripts — excluding eng/release-evidence/, which is the
#                              evidence store itself and legitimately changes when a run is recorded)
# Unit test projects outside E2E, docs, CHANGELOG.md, planning/ and everything else are EXEMPT:
# they cannot change the outcome of the E2E suite or of the publish step, and forcing a 4h+ re-run
# over them would teach the gate to be bypassed. If the policy ever flips to (A) "přísná" — any
# change outside docs/*.md/planning/eng/release-evidence/ breaks the bound — replace
# STALE_BREAKING_PATTERN with an allowlist and refuse any path NOT matching it.
STALE_BREAKING_PATTERN='^(src/|tests/Tempo\.Blazor\.E2E/|\.github/workflows/|eng/[^/]*\.sh$)'

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

while IFS= read -r path; do
  [[ -z "$path" ]] && continue
  if grep -Eq "$STALE_BREAKING_PATTERN" <<<"$path"; then
    refuse "staleness bound broken: '$path' changed between the verified run ($commit) and HEAD — it can alter the E2E outcome or the publish itself, so the evidence must be re-measured (owner policy B)."
  fi
done < <(git diff --name-only "$commit" HEAD)

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
