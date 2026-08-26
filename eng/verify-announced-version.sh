#!/usr/bin/env bash
set -euo pipefail

# THE NUMBER BEING PUBLISHED AND THE NUMBER BEING ANNOUNCED ARE TWO DIFFERENT READS, and until this
# script existed nothing compared them.
#
# THE DEFECT THIS EXISTS FOR, measured over the two publish workflows rather than imagined:
#   * `ReleaseContractTests.PackableProjects_AgreeOnOneVersion…` clamps csproj ↔ CHANGELOG.
#   * `AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds` asks the feed about the CHANGELOG number.
#   * `eng/pack-nuget-packages.sh` asks the feed about $VERSION.
#   * the publish job's "Set version" step, on a TAG push, takes VERSION=${GITHUB_REF#refs/tags/v} —
#     that is, FROM THE TAG. On that branch it computes BASE_VERSION out of the csproj and then does
#     not use it.
# So on a tag push the two feed guards can be asked about DIFFERENT NUMBERS: one about what the
# changelog announces, one about what the tag says. Both can come back green, each about its own
# number, and the packages can go out under a third. The pair csproj ↔ CHANGELOG is clamped; the pair
# TAG ↔ CHANGELOG was clamped by NOBODY — it was covered by a procedure ("tag only after the bump"),
# and a procedure is not a gate: the next release does not inherit somebody's memory of it.
#
# WHAT THIS COMPARES, AND WHAT IT DELIBERATELY DOES NOT. It compares $VERSION — whatever the workflow
# decided to publish, by whichever branch of "Set version" — against the version CHANGELOG.md
# announces. It does NOT re-check csproj (clamped already, see above) and it does NOT ask any feed
# (two guards do that, and both would then be asking about an agreed number, which is the point).
#
# A PRERELEASE SUFFIX IS AGREEMENT, NOT DRIFT. `workflow_dispatch` with version_suffix=beta1 publishes
# `<announced>-beta1` on purpose, so "$VERSION starts with '<announced>-'" is accepted. Nothing else
# is: 2.8.21 against an announced 2.8.20 is refused, and so is 2.8.2, which no prefix rule based on
# bare string containment would have caught.
#
# THE ANNOUNCED VERSION IS READ THE SAME WAY THE SUITE READS IT — first `## <semver>` heading of
# CHANGELOG.md, same shape as `ReleaseContractTests.ReadAnnouncedVersion`. Kept in `sed` with a POSIX
# expression rather than `grep -oP`, because this repository already reads <PackageId> that way in
# eng/pack-nuget-packages.sh and because a second dialect is a second thing that can disagree.
#
# AN EMPTY READ IS A REFUSAL, NOT A PASS. If the changelog cannot be read or opens with no version
# heading, there is nothing to compare against — and "the two numbers did not differ" is exactly the
# answer an unreadable file produces. Same trap the feed probe documents: every failure mode of a
# comparison wears the shape of its passing answer unless the population is checked first.
#
# CHANGELOG_PATH exists for the same reason PACKAGE_MANIFEST and PACKAGE_OUTPUT do in the pack script:
# so the refusal can be exercised over a fixture instead of by editing the real changelog. Neither
# publish workflow sets it, and pointing it anywhere else does not weaken the gate — it only changes
# which file the answer is read from, and an unreadable one refuses.

version="${VERSION:-}"
changelog="${CHANGELOG_PATH:-CHANGELOG.md}"

# WHERE THE NUMBER CAME FROM, said out loud in every message. A mismatch is diagnosed by knowing which
# of the two reads to fix, and "2.8.20 != 2.8.19" without provenance sends the reader to the wrong file.
if [[ -n "${VERSION_SOURCE:-}" ]]; then
  version_source="$VERSION_SOURCE"
elif [[ "${GITHUB_REF:-}" == refs/tags/v* ]]; then
  version_source="the pushed tag ${GITHUB_REF}"
else
  version_source="the VERSION environment variable"
fi

if [[ -z "$version" ]]; then
  echo "VERSION environment variable must be set; this step compares the number being published against the one CHANGELOG.md announces, and there is nothing to compare an empty version with." >&2
  exit 1
fi

if [[ ! -f "$changelog" ]]; then
  echo "Changelog '$changelog' was not found, so nothing announced the version $version (from $version_source)." >&2
  echo "A missing changelog produces the same silence as an agreeing one; this refuses rather than assuming agreement." >&2
  exit 1
fi

announced="$(sed -n 's/^##[[:space:]]*\([0-9][0-9]*\.[0-9][0-9]*\.[0-9][0-9]*\(-[0-9A-Za-z][0-9A-Za-z.-]*\)\{0,1\}\(+[0-9A-Za-z][0-9A-Za-z.-]*\)\{0,1\}\).*/\1/p' "$changelog" | head -n 1)"

if [[ -z "$announced" ]]; then
  echo "'$changelog' opens with no '## <version>' heading, so nothing in this repository announces a release number to compare $version (from $version_source) against." >&2
  exit 1
fi

if [[ "$version" == "$announced" || "$version" == "$announced-"* ]]; then
  echo "[version] publishing '$version' (from $version_source); '$changelog' announces '$announced' — the two agree."
  exit 0
fi

echo "The number being published and the number being announced are not the same release." >&2
echo "  publishing: '$version' (from $version_source)" >&2
echo "  announced : '$announced' (first '## <version>' heading of $changelog)" >&2
echo "Nothing else in this pipeline compares that pair: csproj is clamped to the changelog by ReleaseContractTests, and both feed guards ask about whichever number they were handed — so without this refusal both could go green about different numbers while a third one shipped." >&2
echo "Either retag at the number the changelog announces, or bump CHANGELOG.md and every packable csproj to the number being published." >&2
exit 1
