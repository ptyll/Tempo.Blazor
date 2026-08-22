#!/usr/bin/env bash
set -euo pipefail

# A GREEN PUSH STEP USED TO MEAN NOTHING, and this script exists to make it mean "26 packages went out".
#
# THE DEFECT THIS EXISTS FOR, measured against the real client rather than imagined: both publish
# workflows pushed with `--skip-duplicate`, and with that flag `dotnet nuget push` answers a 409 with
# EXIT 0. Measured 2026-08-21 against a local endpoint returning 409 (SDK 10.0.111): the command exits
# 0 and prints "Package '…' already exists at feed '…'". So a release in which every single number was
# already spent produced exactly the same green job as one that published everything. Success of the
# PUBLICATION was not derivable from success of the JOB.
#
# WHY THE FLAG STAYS. Deleting `--skip-duplicate` is not the treatment, it is a different failure: this
# loop runs over 26 packages, and without the flag the first already-published one aborts the rest —
# trading a silent "nothing shipped" for a loud "half of it shipped", which is the worse of the two
# because it is not recoverable by re-running (the numbers that DID go out are immutable). The fix is
# therefore PER PACKAGE, not per job: every package is attempted, every one gets its own verdict, and
# only the summary over all 26 decides whether the job is red.
#
# HOW A VERDICT IS REACHED, and why it is not "read the exit code". With the flag on, exit 0 covers
# both outcomes, so the tool's own words are the only channel that separates them. Two markers are read
# for each verdict — the HTTP status word from the request trace and the sentence NuGet prints — and
# they must AGREE:
#     published : "  Created http…"   AND/OR  "Your package was pushed."
#     skipped   : "  Conflict http…"  AND/OR  "already exists at feed"
# Anything else — no marker, or both — is UNRESOLVED, and UNRESOLVED is a failure. That direction is
# deliberate: if a future NuGet version rewords those lines, this step refuses to certify what it could
# not read, instead of quietly reporting a skipped package as published. A red is fixable; a false
# green under a release is not.
#
# THIS CLASSIFIER IS BOUND TO THE WORDING OF THE SDK THAT WAS MEASURED, not to a machine-readable
# artefact. Measured SDK: 10.0.111 (the only one installed on the machine that wrote this). There is
# no per-package structured output from `dotnet nuget push` to read instead; exit 0 covers both 201
# and 409 once `--skip-duplicate` is on. A second SDK bump without re-measuring leaves the classifier
# unmeasured. The cheap check that re-measures it is `eng/verify-push-classifier.sh`: python
# http.server + NuGet.Config with allowInsecureConnections=true + --configfile, over the staged
# packages, 201 → published=$total exit 0, 409 → skipped=$total exit 1. Both publish workflows run
# it after pack and before the real push. Run it also on an SDK bump. It is the procedure that
# keeps a wording-bound classifier from drifting silently; it is not optional documentation.
#
# AND WHY THE PUSH RUNS UNDER DOTNET_CLI_UI_LANGUAGE=en. Those sentences are LOCALIZED. Measured on a
# cs_CZ machine, the same 409 prints "Balíček '…' už v kanálu '…' existuje." — an English-only reader
# would have classified it UNRESOLVED, i.e. the classifier's premise would have been inherited from
# whoever happened to run it. Pinning the tool's UI language makes that premise a property of this step.
# (The status words "Created"/"Conflict" came out English under cs_CZ as well — they are enum names,
# not resources — which is why they are read too: one marker per verdict is one wording away from
# breaking a release.)
#   NOTE, so the two do not read as contradicting each other: the release-gate TEST step in both
#   workflows deliberately runs a second lane under a NON-English locale. Opposite settings, opposite
#   reasons — there the machine's culture is the thing under test, here it is noise in an instrument.
#
# AN EMPTY STAGING DIRECTORY IS A REFUSAL. A loop over zero packages exits 0 having pushed nothing,
# which is the same green as a successful release. The pack script already refuses a count that is not
# exactly the manifest's; this refuses the case where nothing was staged at all.

source_name="${PUSH_SOURCE:-}"
api_key="${PUSH_API_KEY:-}"
output="${PACKAGE_OUTPUT:-./packages}"

if [[ -z "$source_name" ]]; then
  echo "PUSH_SOURCE environment variable must be set (the NuGet source to push to)." >&2
  exit 1
fi

if [[ -z "$api_key" ]]; then
  echo "PUSH_API_KEY environment variable must be set." >&2
  exit 1
fi

# PUSH_CONFIGFILE exists so this script can be exercised against a local endpoint (NuGet refuses plain
# HTTP sources unless a config marks them insecure). Neither publish workflow sets it.
config_args=()
if [[ -n "${PUSH_CONFIGFILE:-}" ]]; then
  config_args=(--configfile "$PUSH_CONFIGFILE")
fi

shopt -s nullglob
packages=("$output"/*.nupkg)
shopt -u nullglob

if [[ "${#packages[@]}" -eq 0 ]]; then
  echo "No .nupkg found in '$output'; a push step that pushed nothing must not report success." >&2
  exit 1
fi

published=0
skipped=0
failed=0
unresolved=0
declare -a verdicts=()

for pkg in "${packages[@]}"; do
  name="$(basename "$pkg")"
  echo "Publishing $name to '$source_name'..."

  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
    echo "::group::dotnet nuget push $name"
  fi

  # `|| rc=$?` and not `set +e`: the failure of ONE package must not end the loop, and must not be
  # swallowed either — it is carried into the verdict below.
  rc=0
  out="$(DOTNET_CLI_UI_LANGUAGE=en dotnet nuget push "$pkg" \
    --source "$source_name" \
    --api-key "$api_key" \
    "${config_args[@]}" \
    --skip-duplicate 2>&1)" || rc=$?

  echo "$out"

  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
    echo "::endgroup::"
  fi

  says_published=0
  says_skipped=0
  # Bash pattern matching rather than grep: no external tool, no locale, and no dependence on which
  # grep is on PATH — the classification is the one thing here that must not vary by environment.
  if [[ "$out" == *"Your package was pushed."* ]]; then says_published=1; fi
  if [[ "$out" == *"Created http"* ]]; then says_published=1; fi
  if [[ "$out" == *"already exists at feed"* ]]; then says_skipped=1; fi
  if [[ "$out" == *"Conflict http"* ]]; then says_skipped=1; fi

  if [[ "$rc" -ne 0 ]]; then
    verdict="FAILED (exit $rc)"
    failed=$((failed + 1))
  elif [[ "$says_published" -eq 1 && "$says_skipped" -eq 0 ]]; then
    verdict="PUBLISHED"
    published=$((published + 1))
  elif [[ "$says_skipped" -eq 1 && "$says_published" -eq 0 ]]; then
    verdict="SKIPPED (already on the feed)"
    skipped=$((skipped + 1))
  else
    verdict="UNRESOLVED (exit 0, but the output says neither 'published' nor 'already exists')"
    unresolved=$((unresolved + 1))
  fi

  echo "[push] $name: $verdict"
  verdicts+=("$name: $verdict")
done

total="${#packages[@]}"
echo "[push] published=$published skipped=$skipped failed=$failed unresolved=$unresolved of $total to '$source_name'"

if [[ "$published" -eq "$total" ]]; then
  echo "[push] every staged package was accepted by the feed under this release's number."
  exit 0
fi

echo "This release did not publish every package it staged, so a green job here would not have meant the packages are out:" >&2
for line in "${verdicts[@]}"; do
  echo "  $line" >&2
done
echo "SKIPPED means the feed already serves that id at this version — the number is spent and the artefact under it is immutable." >&2
echo "UNRESOLVED means this step could not tell publication from a skip; it refuses instead of guessing, because a guess in this direction ships nothing and says it shipped." >&2
exit 1
