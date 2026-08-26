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

mapfile -t projects < <(grep -vE '^[[:space:]]*(#|$)' "$manifest" | sed 's/[[:space:]]*$//')

# THE NUMBER ITSELF HAS TO STILL BE FREE, and this is the last place that can refuse it.
#
# THE DEFECT THIS EXISTS FOR, measured rather than imagined: 2.8.19 was announced twice. The published
# artefact records commit 714093ce; the 26 packages staged locally under the same number record
# d1c8e776. Two commits behind one version number, and the first artefact is immutable on the feed.
# Every other check in this script and in ReleaseContractTests was green throughout and was right to
# be: they compare csproj against CHANGELOG, and each nuspec against HEAD. Neither pair answers "is
# this number still free", which is the only question a spent number fails.
#
# WHY HERE AS WELL AS IN THE SUITE. `ReleaseContractTests.AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds`
# asks the same question on every test run, which is earlier and cheaper — but it SKIPS when the feed
# does not answer, because a suite that cannot run offline is a suite that gets run less. This copy
# REFUSES instead. They are not redundant, they are the same guard with opposite failure modes, placed
# where each one's failure mode is the affordable one.
#
# AND WHY BEFORE THE DIRTY-TREE REFUSAL. A dirty tree is fixable by committing; a spent version number
# is not fixable at all. Asking the unfixable question first means the answer arrives before any work,
# and it is also the only ordering in which this check is reachable while the tree is still dirty.
#
# TAG, PUT AND AVAILABILITY ARE THREE DIFFERENT QUANTITIES. This asks the third: is the number VISIBLE
# on the feed. It says nothing about whether a tag exists, and nothing about a push that has completed
# but not yet propagated — nuget.org serves this index through a CDN, so a green here is not proof that
# no PUT has happened.
#
# HOW LONG THAT PROPAGATION TOOK, ONCE — AN OBSERVATION AND NOT A BOUND. Release 2.8.20 on 2026-08-22,
# GHA run 32557365946: the push step PUT completed at 06:48:30Z and this same flat container index
# first answered 200 for that number at 06:55:41Z, which is 431 s later. The publish job that produced
# that PUT ran 06:43:25Z to 06:48:35Z, i.e. 310 s. THE COMPARISON IS THE FINDING: 431 s is longer than
# 310 s, so the blind window outlasts the job that opens it. A release can therefore finish green while
# this check and its copy in ReleaseContractTests both still read the number as free — and in that
# window NEITHER OF THEM IS WRONG. Each answers "is the number VISIBLE as taken", and for a number
# that has been PUT but not yet propagated the truthful answer to that question really is "not
# visible". The two guards are right and the release is still unsafe, because the question they can
# answer is not the question a second publish would need answered.
# ONE SAMPLE OF A CDN IS NOT A LIMIT: nothing here sleeps or retries for those 431 s, no release may be
# scheduled against them, and the next propagation may take longer. What the number establishes is only
# that this window is neither negligible nor shorter than a publish. The endpoint stays the flat
# container for the reason given above; registration and search answer a DIFFERENT question, so moving
# there would change the quantity being measured rather than shorten the window.
#
# AND THIS IS WHERE A RE-RUN ACTUALLY LANDS, which is why the bound is repeated here instead of only
# beside the push step. The push step in both publish workflows carries the sentence "a re-run over a
# spent number is EXPECTEDLY RED and the treatment is a bump, not a re-run" — and whoever clicks
# "Re-run jobs" never reaches that sentence, because two guards refuse first. Measured over the two
# files 2026-08-23, and named BY STEP rather than by line number — line numbers move with the very
# commit that writes them, step names do not: in both publish-nuget.yml and publish-nuget-org.yml the
# `publish` job carries `needs: build-and-test`, so the suite's
# `AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds`, which runs inside the `Test` step of
# `build-and-test`, turns red and the publish job never starts; and if it did, THIS refusal fires at
# the `Build packages` step, still ahead of the push step (`Push packages to NuGet.org` there,
# `Push packages to GitHub Packages` in publish-nuget.yml). A sentence the reader cannot reach is not
# a bound, so the refusal messages below say it themselves.
# THE ONE WINDOW IN WHICH THE PUSH-STEP COPY IS THE ONE THAT FIRES is the propagation window above: for
# the first ~431 s neither feed guard can see the number, both pass truthfully, and the push is the
# only thing left to refuse. The two placements are therefore not duplicates — they cover different
# moments of the same re-run.
# AND publish-nuget.yml IS ASYMMETRIC TO THIS, stated because the ordering above would otherwise be
# read as holding for both feeds: that workflow pushes to GITHUB PACKAGES, while this refusal and the
# suite guard both ask nuget.org. A number spent only on GitHub Packages is invisible to both, so for a
# re-run of that workflow alone the push step's own accounting really is the first and only refusal —
# the copy beside it is in the right place there.
#
# THE PACKAGE ID IS READ, NOT WRITTEN. The flat container answers 404 for an id it does not know, so a
# misspelled id would report every number as free forever. It is taken from the same csproj the publish
# workflow reads the version out of, and an empty read is a refusal rather than a guess.
#
# ALLOW_UNVERIFIED_VERSION=1 is the named escape for packing with no route to the feed. It covers ONLY
# the case where the question could not be asked; a version the feed answers WITH is refused outright,
# because there is nothing to escape to.
#
# THE POPULATION IS THE WHOLE MANIFEST, NOT THE LEAD PACKAGE, and the gap was measured rather than
# imagined: `eng/nuget-packages.txt` listed 26 projects when this was written (measured 2026-08-23 with
# `/usr/bin/grep -cvE '^[[:space:]]*(#|$)' eng/nuget-packages.txt`, which is the same read the loop
# below uses to build its own list). That number is DATED on purpose: it is expected to move, the very
# next paragraph is about what happens when a 27th package is added, and nothing here depends on it —
# the loop reads the manifest rather than a count. This probe used to ask about exactly one of
# them — the id read from `src/Tempo.Blazor/Tempo.Blazor.csproj`. The very state that
# `eng/push-nuget-packages.sh` exists for, a PARTIAL release where a push died part-way through an
# alphabetical glob, is invisible to a one-id question whenever Tempo.Blazor is among the ids that did
# not get pushed. It was caught only by the push (`published != total`), i.e. after the pack and after
# 26 PUTs against the live feed, by a guard whose whole argument is "first and cheapest".
#
# WHY A 404 IS NOT FATAL FOR THE OTHER 25, settled before the loop was written because the loop opens
# this trap: a 404 on the flat container is AMBIGUOUS. It means "misspelled id" and it equally means
# "this package was never published" — and the second is the legitimate state of a newly added 27th
# package. Applying the lead id's strictness to all 26 would let the first newly added package block
# the release, and a guard that the first new package blocks is a guard somebody switches off. So the
# LEAD id keeps today's strictness — it is provably published, so a non-200 there is a typo or an
# unreachable feed — while for the remaining ids a 404 is REPORTED and skipped, and membership is
# asked only where a 200 actually arrived.
#
# WHERE THE REACH CONTROL LIVES UNDER THAT SPLIT, because the loop has none of its own: an unreachable
# feed answers non-200 for every id, and 25 silent skips read exactly like 25 free ids. The lead probe
# is what refuses that. It runs FIRST, it is strict, and the loop is entered only when it answered —
# so no run can reach the loop without the feed having answered at least once.
#
# AND THE LIMIT THAT REMAINS AFTER ALL THAT, stated rather than left to be discovered: a non-lead id
# that answers something other than 200 or 404 — a 5xx, a request that timed out — is reported and
# skipped, so a number already spent under THAT id would still get through. The lead probe proves the
# feed was reachable, not that all 26 questions were answered; the counts printed at the end of the
# loop are the record of how many were.
#
# AND WHAT THE LOOP COSTS, including the part of it that is a CEILING rather than a measurement. The
# questions are asked one after another, each with `--max-time 20`, so 25 further requests put a worst
# case of +500 s on a pack — and nothing in this script bounds that. The lead probe does not: it proves
# the feed answered ONCE, at t0, which is a statement about reachability at that instant and says
# nothing about the 25 requests that follow it. What was measured is the other end: 7.67 s and 8.33 s
# for the whole probe over two runs (2026-08-23), and a re-measure of the two shapes alone the same day
# gave 5.10 s / 3.53 s for 26 sequential requests against 2.80 s / 2.65 s for a single curl handed all
# 26 URLs. The cheaper shape is therefore worth 1.3x-1.9x here and was measured at 2.8x earlier the
# same day, which is exactly why it is filed as a conditional queue row and not taken now: the number
# is network-dependent, and the honest trigger is the manifest outgrowing 40 ids or a measured pack
# cost above 30 s, not a ratio.
#
# WHETHER THIS LOOP CAN FIRE AGAINST THE LIVE FEED TODAY, since a guard nobody has ever seen work is a
# guard nobody trusts: no, and the reason is not that the feed answers uniformly — it does not.
# Measured 2026-08-23 over all 26 ids, the answers fall into SIX different version sets (3 ids serve
# 105 versions, 19 serve 86, and four singletons serve 58 / 55 / 48 / 30). The property that actually
# holds is weaker and is the only one the conclusion needs: the union of every version any id serves is
# exactly the 105 the LEAD id serves, with 0 versions outside it. Under that SUPERSET a number spent
# anywhere is also spent on the lead, which is why the one-id question was adequate for as long as it
# was — and the property dies at the first partial publication of a non-lead id under a number the lead
# does not carry, which is the state this loop was added for. Until that happens the refusal arm is
# exercised only offline, against a stubbed feed, by
# `tests/Tempo.Blazor.Tests/Packaging/PackScriptManifestSweepTests.cs`.
package_id="$(sed -n 's/.*<PackageId>\([^<]*\)<\/PackageId>.*/\1/p' src/Tempo.Blazor/Tempo.Blazor.csproj | head -n 1 | tr '[:upper:]' '[:lower:]')"
if [[ -z "$package_id" ]]; then
  echo "src/Tempo.Blazor/Tempo.Blazor.csproj carries no <PackageId>; refusing to guess one, because a wrong id makes every version look free." >&2
  exit 1
fi

# ONE PROBE, ASKED ONCE PER ID. The three values it leaves behind are read by the lead arm below and
# by the manifest loop after it, so both ask the feed the same way and can never drift into two
# different notions of "the feed answered".
probe_feed_index() {
  feed_index="https://api.nuget.org/v3-flatcontainer/${1}/index.json"
  feed_body="$(curl -sS --max-time 20 -w '\n%{http_code}' "$feed_index" 2>/dev/null || true)"
  feed_status="$(printf '%s' "$feed_body" | tail -n 1)"
  feed_versions="$(printf '%s' "$feed_body" | sed '$d' | grep -o '"[0-9][^"]*"' | wc -l | tr -d ' ' || true)"
}

lead_answered=0
probe_feed_index "$package_id"

# THE REACH CONTROL, and it is not decoration: measured offline, the probe returns nothing and "the
# announced version is not in the list" comes out TRUE. Status and population are therefore checked
# BEFORE membership, so a probe that never arrived cannot answer this question at all.
if [[ "$feed_status" != "200" || "${feed_versions:-0}" -eq 0 ]]; then
  if [[ "${ALLOW_UNVERIFIED_VERSION:-}" != "1" ]]; then
    echo "Could not read $feed_index (http '${feed_status:-<none>}', ${feed_versions:-0} versions parsed), so nobody has checked whether $VERSION is already published." >&2
    echo "A version number that is already on the feed is spent for good, and this is the last step that can refuse it." >&2
    echo "Fix the connection, or set ALLOW_UNVERIFIED_VERSION=1 to pack without that answer." >&2
    exit 1
  fi

  echo "ALLOW_UNVERIFIED_VERSION=1: packing $VERSION without confirming it is still free on $feed_index." >&2
elif [[ "$(printf '%s' "$feed_body" | grep -cF "\"$VERSION\"" || true)" != "0" ]]; then
  echo "Version $VERSION is already published on $feed_index, under package id '$package_id'; the artefact under that number is immutable and cannot be replaced." >&2
  echo "Packing it again would ship different bytes under a label consumers have already resolved to something else." >&2
  echo "Bump CHANGELOG.md and every packable csproj to the next free number; retagging cannot reach what is already on the feed." >&2
  echo "If you got here by clicking \"Re-run jobs\" after a release that already pushed: this red is EXPECTED and is not evidence that the release failed. The number is spent, the artefacts under it are immutable, and no re-run of this run can come back green. The treatment is a bump, not a re-run." >&2
  exit 1
else
  lead_answered=1
fi

# THE SAME QUESTION OVER THE REST OF THE MANIFEST. Guarded on the lead answer for the reason in the
# block above: without that guard an unreachable feed would walk this loop reporting nothing.
if [[ "$lead_answered" == "1" ]]; then
  manifest_spent=""
  manifest_answered=0
  manifest_unpublished=0
  manifest_unanswered=0

  for project in "${projects[@]}"; do
    if [[ ! -f "$project" ]]; then
      echo "Package project '$project' from '$manifest' was not found." >&2
      exit 1
    fi

    member_id="$(sed -n 's/.*<PackageId>\([^<]*\)<\/PackageId>.*/\1/p' "$project" | head -n 1 | tr '[:upper:]' '[:lower:]')"
    if [[ -z "$member_id" || "$member_id" == "$package_id" ]]; then
      continue
    fi

    probe_feed_index "$member_id"

    if [[ "$feed_status" == "404" ]]; then
      manifest_unpublished=$((manifest_unpublished + 1))
      echo "  $member_id: 404, never published — there is no artefact under that id for $VERSION to collide with." >&2
      continue
    fi

    if [[ "$feed_status" != "200" || "${feed_versions:-0}" -eq 0 ]]; then
      manifest_unanswered=$((manifest_unanswered + 1))
      echo "  $member_id: http '${feed_status:-<none>}', ${feed_versions:-0} versions parsed — this id was NOT asked, and a number spent under it would not be seen here." >&2
      continue
    fi

    manifest_answered=$((manifest_answered + 1))
    if [[ "$(printf '%s' "$feed_body" | grep -cF "\"$VERSION\"" || true)" != "0" ]]; then
      manifest_spent="$member_id"
      break
    fi
  done

  if [[ -n "$manifest_spent" ]]; then
    echo "Version $VERSION is already published on $feed_index, under package id '$manifest_spent'; the artefact under that number is immutable and cannot be replaced." >&2
    echo "The lead id '$package_id' does not serve $VERSION, which is why asking about that one id alone reported the number free — a partially published release is exactly that shape." >&2
    echo "Bump CHANGELOG.md and every packable csproj to the next free number; retagging cannot reach what is already on the feed." >&2
    echo "If you got here by clicking \"Re-run jobs\" after a release that already pushed: this red is EXPECTED and is not evidence that the release failed. The number is spent, the artefacts under it are immutable, and no re-run of this run can come back green. The treatment is a bump, not a re-run." >&2
    exit 1
  fi

  echo "Version $VERSION is free on '$package_id' and on $manifest_answered further manifest id(s) the feed answered for; $manifest_unpublished never published, $manifest_unanswered not answered." >&2
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
# writes it too. WHEN it does not skip is a CONDITION, not a guarantee — and the one build switch this
# comment used to name as certain owed that certainty to a target that no longer exists: while
# `CleanBundledCss` was there, `--no-incremental` meant Rebuild and its Clean-time `Delete` took the
# target's declared output away, so the `Inputs`/`Outputs` up-to-date check had nothing to compare
# against. Nothing here says a build WILL rebundle; the condition is spelled out in the comment at the
# end of `src/Tempo.Blazor/Tempo.Blazor.csproj`. That does not make this check redundant, it
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
# is precisely such a file: the comment above records that a build can rewrite it too, so it can
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
