#!/usr/bin/env bash
set -euo pipefail

manifest="${PACKAGE_MANIFEST:-eng/nuget-packages.txt}"
configuration="${CONFIGURATION:-Release}"
output="${PACKAGE_OUTPUT:-./packages}"

if [[ -z "${VERSION:-}" ]]; then
  echo "VERSION environment variable must be set." >&2
  exit 1
fi

if [[ ! -f "$manifest" ]]; then
  echo "Package manifest '$manifest' was not found." >&2
  exit 1
fi

# THE COMMIT STAMPED INTO THE NUSPEC IS PASSED IN, NOT INHERITED.
#
# Measured on 2.8.15: the published nuspec carried commit="efb00b89…", which is 2.8.14 — one
# release behind the content it shipped. The content was right, the LABEL was wrong. That is a
# defect of evidence, and it is worse than a missing field: DEC-EVIDENCE-PROVENANCE tells the next
# auditor to verify a release from the package content AND the recorded commit, and whoever checks
# out efb00b89 will not find the fix there and will conclude a correct release is broken.
#
# The mechanism: SourceLink ships inside the SDK, so `RepositoryCommit` is derived from
# `SourceRevisionId`, which the `InitializeSourceControlInformation` target resolves at BUILD time.
# `dotnet pack` runs here with --no-build, so it reuses whatever the last build left in obj/ — and
# an incremental build that decided nothing changed leaves the PREVIOUS commit there. Passing the
# value explicitly removes the dependency on that cache entirely.
#
# AND THE COMMIT IS ONLY A TRUE LABEL IF THE TREE IT NAMES IS THE TREE BEING PACKED.
#
# Everything downstream compares the stamp against this one value: the read-back at the end of this
# script, and `ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom`. All of them
# derive from the same read of HEAD below, so over a DIRTY tree the equality holds BY CONSTRUCTION
# and certifies packages whose bytes came from source that no commit contains. That is not a gap in
# coverage, it is an active false confirmation: measured on 2026-08-18 the staging directory held 26
# `Tempo.*.2.8.18.nupkg` stamped `commit="d49ede02…"` — which is 2.8.17 — and every one of the three
# checks reported them good, because at that moment HEAD really was d49ede02 and the 2.8.18 content
# was sitting uncommitted in the working tree.
#
# No better stamp fixes this. The defect is not WHAT is read but that the label is verified against
# the same source it was minted from, so any replacement inside that loop (SourceLink, a content
# hash, a different way of reading HEAD) only moves the tautology. The one thing that breaks it is
# refusing the situation in which a truthful answer does not exist — which is exactly a dirty tree.
#
# ALLOW_DIRTY_PACK=1 is the escape for a deliberate local experiment, and it does NOT restore the
# lie: the stamp then carries a `-dirty` suffix, so a package built off uncommitted source says so in
# its own nuspec instead of borrowing its parent commit's good name. Such a package must never be
# published or copied into a consumed feed — `ReleaseContractTests` will fail it if it is staged,
# because `-dirty` cannot equal any commit id.
#
# ONE MEASURED CONSEQUENCE FOR THE LOCAL WORKFLOW, so nobody reads the refusal as a bug: a full
# `dotnet build -c Release --no-incremental` leaves the tree clean (measured 2026-08-19), but the TEST
# suite does not — `src/Tempo.Blazor.Demo.Api/diagrams.db` is tracked and the Demo.Api tests write to
# it. So "run the gate, then pack" refuses until that file is restored. That is the check working, not
# misfiring: those bytes really are uncommitted. The message below prints `git status` precisely so the
# offending path is named rather than guessed at. CI is unaffected — the publish job does its own
# checkout and packs after a BUILD, with no test run in between.
dirty_suffix=""
if [[ -n "$(git status --porcelain)" ]]; then
  if [[ "${ALLOW_DIRTY_PACK:-}" != "1" ]]; then
    echo "Working tree is dirty; no commit describes the bytes about to be packed." >&2
    echo "Commit or stash first, or set ALLOW_DIRTY_PACK=1 to produce packages stamped '-dirty' that must not be published." >&2
    git status --porcelain >&2
    exit 1
  fi

  echo "ALLOW_DIRTY_PACK=1 over a dirty tree: stamping the commit with a '-dirty' suffix." >&2
  dirty_suffix="-dirty"
fi

commit="$(git rev-parse HEAD)${dirty_suffix}"

mapfile -t projects < <(grep -vE '^[[:space:]]*(#|$)' "$manifest" | sed 's/[[:space:]]*$//')

mkdir -p "$output"
# This delete is load-bearing, and nothing else in the pipeline knows it.
#
# The staging directory survives between packs and `*.nupkg` is in .gitignore, so a previous run's
# packages sit there INVISIBLY — `git status` is clean with a full set of them present. Nothing about
# the FILENAME distinguishes them: measured on 2026-08-03, ./packages already held all 26
# *.2.8.7.nupkg from a pack two commits earlier, and only the CONTENT told them apart —
# `alreadyInside` 0x vs 1x in staticwebassets/js/tm-focus-trap.js, and an informational version of
# 2.8.7+7ad76259... vs 2.8.7+0ffc0248.... The same version number had been minted twice over
# different source. So this line is the only thing standing between a repack and shipping stale
# bytes under a version that claims to be fresh.
#
# Two consequences worth knowing before you change anything here:
#   * Never point PACKAGE_OUTPUT at a directory you did not create for this purpose (a consuming
#     project's local NuGet feed, say) — this deletes EVERY nupkg it finds there, not only ours.
#     Pack into staging, then copy the versioned files out.
#   * `dotnet pack` runs with --no-build, so the version lands in the DLL at BUILD time, not here.
#     Build with -p:Version=$VERSION first or the nuspec and the assembly will disagree.
find "$output" -maxdepth 1 -type f \( -name '*.nupkg' -o -name '*.snupkg' \) -delete

for project in "${projects[@]}"; do
  if [[ ! -f "$project" ]]; then
    echo "Package project '$project' from '$manifest' was not found." >&2
    exit 1
  fi

  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
    echo "::group::Packing $project"
  else
    echo "Packing $project"
  fi

  dotnet pack "$project" \
    --configuration "$configuration" \
    --no-restore \
    --no-build \
    -p:Version="$VERSION" \
    -p:RepositoryCommit="$commit" \
    --output "$output"

  if [[ -n "${GITHUB_ACTIONS:-}" ]]; then
    echo "::endgroup::"
  fi
done

actual_count=$(find "$output" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' | wc -l | tr -d ' ')
expected_count=${#projects[@]}

if [[ "$actual_count" -ne "$expected_count" ]]; then
  echo "Expected $expected_count nupkg files in '$output', but found $actual_count." >&2
  find "$output" -maxdepth 1 -type f -name '*.nupkg' -print | sort >&2
  exit 1
fi

# VERIFIED FROM THE PRODUCED BYTES, not from the fact that the flag was passed.
#
# The flag above is the fix; this is the guard, and they are separate on purpose. `-p:` on a pack
# that reuses obj/ has already been observed to lose to a cached value once — that is the whole
# reason this block exists — so the only trustworthy check is to open what came out and read it.
# `unzip -p` streams the nuspec without unpacking, so this stays cheap over 26 packages.
#
# The value is read as "anything up to the closing quote" rather than as hex, because under
# ALLOW_DIRTY_PACK=1 the stamp legitimately ends in `-dirty`. A hex-only pattern would truncate that
# suffix, the comparison below would then fail on every package, and the escape hatch would be dead
# code that nobody could exercise — which is the state in which a `-dirty` stamp silently stops being
# produced at all.
mismatched=0
while IFS= read -r package; do
  stamped="$(unzip -p "$package" '*.nuspec' 2>/dev/null \
    | grep -o 'commit="[^"]*"' | head -n 1 | sed 's/commit="//; s/"//')"
  if [[ "$stamped" != "$commit" ]]; then
    echo "Package '$package' records commit '${stamped:-<none>}' but this pack stamped '$commit'." >&2
    mismatched=$((mismatched + 1))
  fi
done < <(find "$output" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg')

if [[ "$mismatched" -ne 0 ]]; then
  echo "$mismatched package(s) carry a commit label that does not match HEAD; refusing to ship them." >&2
  exit 1
fi

echo "Packed $actual_count NuGet packages into '$output' at commit $commit."
