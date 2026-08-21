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
# WHAT THIS BLOCK NO LONGER SAYS, AND WHY THAT SENTENCE WAS DELETED RATHER THAN REWORDED.
#
# This block used to carry a paragraph saying that "run the suite, then pack" legitimately refuses
# because `src/Tempo.Blazor.Demo.Api/diagrams.db` is tracked and the Demo.Api tests write to it, and
# that this was the check working rather than misfiring. The refusal was honest — those bytes really
# were uncommitted — but the paragraph turned a defect into an expected outcome, and a red that has
# been explained in advance is a red nobody fixes. The write itself is what was wrong: the unit lane
# is the last one that had not redirected the database (the e2e lane sets `Demo__DiagramsDbPath`
# already), and committing the churn was ruled out by measurement, six different contents out of one
# clean base. That lane now redirects too, so the sentence has been removed rather than reworded:
# there is no longer a routine dirty state for it to describe.
#
# The message below still prints `git status`, so whatever DOES turn up is named rather than guessed
# at. CI is unaffected either way — the publish job does its own checkout and packs after a BUILD,
# with no test run in between.
dirty_suffix=""
pre_status="$(git status --porcelain)"
if [[ -n "$pre_status" ]]; then
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
#
# `|| true` IS WHAT MAKES THE `${stamped:-<none>}` BELOW REACHABLE, and it is the whole point of it.
# `grep` exits 1 when the nuspec carries no `commit="…"` at all, `set -o pipefail` promotes that to the
# pipeline's status, and `set -e` then killed the script on the assignment itself — BEFORE the message
# that names the offending package could be printed. That was never a hole: the run still failed closed
# with rc=1. It was worse in a quieter way — a fallback written for exactly this case that this case
# could not reach, i.e. source that says the script reports an unstamped package when it does not.
#
# OF THE TWO TREATMENTS THIS ONE IS CHOSEN — make the fallback reachable, rather than delete it — because
# the useful output here is the NAME of the package that is missing its stamp. A bare rc=1 names nothing,
# and this loop runs over the whole manifest, so "one of them is unstamped" is not an actionable answer.
# The refusal below still fires afterwards, so the run remains fail-closed; only the diagnosis improves.
# WHAT THE `|| true` ALSO SWALLOWS, since it must be said rather than discovered: a genuinely unreadable
# archive (`unzip` failing) now also yields an empty value instead of aborting. It is still reported by
# name, counted as mismatched, and the script still exits 1 — but under its OWN message, for the reason
# in the next paragraph.
#
# TWO DIFFERENT FAILURES, TWO DIFFERENT MESSAGES — and they are split because merging them made the
# script assert something it had not measured. "Records commit '<none>'" is a POSITIVE claim about the
# package's content: it says the nuspec was read and carried no stamp. When the archive cannot be opened
# at all, nothing about its content has been established, and reporting the same sentence sends the
# reader looking for a missing `-p:RepositoryCommit` in a package whose real problem is that it is not
# readable. The read is therefore done first, on its own, and an empty result is reported as what it is.
mismatched=0
while IFS= read -r package; do
  nuspec="$(unzip -p "$package" '*.nuspec' 2>/dev/null || true)"
  if [[ -z "$nuspec" ]]; then
    echo "Package '$package' could not be read: no .nuspec came out of the archive, so this pack cannot say which commit it records." >&2
    mismatched=$((mismatched + 1))
    continue
  fi

  stamped="$(printf '%s' "$nuspec" | grep -o 'commit="[^"]*"' | head -n 1 | sed 's/commit="//; s/"//' || true)"
  if [[ "$stamped" != "$commit" ]]; then
    echo "Package '$package' records commit '${stamped:-<none>}' but this pack stamped '$commit'." >&2
    mismatched=$((mismatched + 1))
  fi
done < <(find "$output" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg')

if [[ "$mismatched" -ne 0 ]]; then
  echo "$mismatched package(s) carry a commit label that does not match HEAD; refusing to ship them." >&2
  exit 1
fi

# THE SAME QUESTION, ASKED AGAIN AFTERWARDS — because the pack is itself a writer.
#
# The refusal at the top runs BEFORE the loop and never again, so it can only see dirt that already
# existed. `dotnet pack` builds targets of its own: `BundleCssFiles` is hooked
# `BeforeTargets="ResolveProjectStaticWebAssets;GenerateNuspec;Pack"` and writes
# `src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css`, which is TRACKED. The trigger is WIDER than
# packing, and saying "the pack writes it" understates it: any BUILD that does not skip that target
# writes it too. Measured — a plain `dotnet build --no-incremental` defeats the target's
# `Inputs`/`Outputs` up-to-date check and the bundler runs. That does not make this check redundant, it
# makes it the only one that sits where nothing else looks: a build's write lands BEFORE the refusal
# above and shows up as a red it can explain, whereas a pack's write lands after everything and shows
# up as nothing at all. A pack that regenerates
# that file with different bytes therefore ships them under a commit stamp minted from a tree that no
# longer matches — and every check in this script and in ReleaseContractTests would still agree,
# because all of them were computed before the write. That is the same tautology the dirty-tree
# refusal exists to break, arriving through the one door that refusal does not cover.
#
# The last time this was caught it was caught by a HUMAN running `git status` after the pack. A step
# somebody remembers is not a gate: the next pack does not inherit it.
#
# WHY THE DELTA AND NOT "IS IT DIRTY". Under ALLOW_DIRTY_PACK=1 the tree is legitimately dirty on the
# way in, so a bare emptiness test would fire on every deliberate local experiment and be turned off.
# Comparing against the status recorded at the top asks the question this check can actually answer:
# did the SET OF PATHS GIT REPORTS, and the status letter against each, change while this pack ran. It
# is also indifferent to WHICH file — the bundle is the known case, but nobody has measured how many
# other tracked files a build writes, and a check that has to know their names in advance is only as
# complete as that list.
#
# AND THE LIMIT THAT FOLLOWS FROM THAT, which bites in exactly the mode this shape is argued for:
# `git status --porcelain` reports PATH STATUSES, NOT CONTENT. Over a NON-EMPTY `pre_status` — that is,
# under ALLOW_DIRTY_PACK=1 — a pack that rewrites an ALREADY-MODIFIED tracked file with different bytes
# produces a character-identical `post_status`, so this check passes and reports no change. The bundle
# is precisely such a file: the comment above records that even a plain build rewrites it, so it can
# already be sitting modified when the loop starts. Over an EMPTY `pre_status` — the only state in
# which the packages produced here are publishable at all, since anything else is stamped `-dirty` —
# the check is COMPLETE, because a write to any tracked file has to move it from clean to modified and
# that is a change in the set.
#
# WHY NOT MOVE THE BUNDLE OUT OF THE TREE INSTEAD. That was the other candidate: generate into obj/
# and take it into the package from there, after which no check afterwards would be needed. It was
# rejected on measurement, not taste. Five test files read that bundle from its committed location
# (OrphanClassCssContractTests, CodeEditorWrapStylesheetTests, CssBundleCalcWhitespaceTests,
# CssBundlerInputSourceTests, TmSignatureCaptureTests), and CssBundleCalcWhitespaceTests asserts on
# the COMMITTED bundle deliberately — its own doc records that the sources were fine all along, so a
# guard over them would measure a permanently green population. Moving the file to a directory
# `Clean` deletes would take those guards with it. And it cures exactly one file while leaving the
# class open, whereas this check does not need to know the list.
#
# The packages are left where they are on failure, on purpose: they are the evidence of what was
# produced, and deleting them would leave the reader with a message and nothing to inspect.
post_status="$(git status --porcelain)"
if [[ "$post_status" != "$pre_status" ]]; then
  echo "The pack itself modified the working tree; the commit stamped into these packages no longer describes their source." >&2
  echo "Before the pack:" >&2
  echo "${pre_status:-(clean)}" >&2
  echo "After the pack:" >&2
  echo "${post_status:-(clean)}" >&2
  echo "Restore or commit the paths above, then pack again. Do not publish the packages in '$output'." >&2
  exit 1
fi

echo "Packed $actual_count NuGet packages into '$output' at commit $commit; no path changed status during this pack."
