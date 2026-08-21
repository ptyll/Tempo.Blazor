using FluentAssertions.Execution;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit.Abstractions;

namespace Tempo.Blazor.Tests.Packaging;

/// <summary>
/// Repo-wide release invariants: what the packages ship as, and what the changelog says they ship as.
/// <para>
/// LIVES HERE, not next to a product's own tests, because the subject is the WHOLE repository. It used
/// to sit in <c>Tempo.Blazor.Mcp.Tests</c> beside three NotionEditor contract tests, which made a guard
/// over all 26 packable projects look like a NotionEditor concern and hid it from anyone touching
/// packaging. The neighbours here are the guards that already walk up to <c>TempoBlazor.slnx</c> and
/// sweep <c>src/</c> across every package rather than their own — <c>DataTestIdConventionGuardTests</c>,
/// <c>HardcodedAriaLabelGuardTests</c>, <c>DesignTokenDefinitionTests</c> — so this is the established
/// home for a repo-wide invariant, not a new pattern.
/// </para>
/// </summary>
public sealed class ReleaseContractTests
{
    /// <summary>The manifest CI publishes from — the repository's own list of what ships.</summary>
    private static readonly string[] ManifestPath = ["eng", "nuget-packages.txt"];

    /// <summary>The pack script both guards below read; named once so the two never drift apart.</summary>
    private static readonly string[] PackScriptPath = ["eng", "pack-nuget-packages.sh"];

    private readonly ITestOutputHelper _output;

    public ReleaseContractTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The packages ship in lockstep, and the number they ship under is the one the changelog announces.
    /// <para>
    /// This used to name the release it was written for (<c>…AreSynchronizedTo270</c>, asserting the literal
    /// "2.7.0"), which made every release start by editing the test that was supposed to check the release.
    /// A guard that has to be rewritten to stay green is measuring the state it found rather than the rule,
    /// and the rule here is two things neither of which mentions a number: every packable project agrees,
    /// and what they agree on is what the changelog says is being released. The second half is the defect
    /// this release ran into — a changelog entry written under 2.5.6 while the packages stood at 2.7.0,
    /// which would have published a version below the one already on the feed.
    /// </para>
    /// <para>
    /// WHAT IT READS OUT OF THE CHANGELOG, since that shape is now load-bearing: the first <c>##</c>
    /// heading whose text is a semantic version. Headings that are not versions are SKIPPED rather than
    /// failed on, so the common <c>## Unreleased</c> section can be added on top without turning this red
    /// for a reason that has nothing to do with the packages — a guard that cries at a benign edit is a
    /// guard somebody eventually weakens. The document title (a single <c>#</c>) and the subsections
    /// inside a release (<c>###</c>) do not match either. What it therefore assumes, and what would make
    /// it lie, is that no released version is listed ABOVE the one being shipped.
    /// </para>
    /// <para>
    /// THE VERSION IS READ AS FULL SemVer, suffixes included, and that is the point rather than a detail.
    /// Matching only <c>MAJOR.MINOR.PATCH</c> did not fail to read <c>## 2.9.0-beta.1</c> — it read it as
    /// "2.9.0" and TRUNCATED the suffix, so the guard compared a shortened changelog version against the
    /// full <c>2.9.0-beta.1</c> in every csproj and went red on a release that was perfectly consistent.
    /// A guard that is red for a benign reason is one somebody eventually weakens, and the weakening lands
    /// on the half that catches real mismatches.
    /// </para>
    /// <para>
    /// The suffix groups require their first character to be alphanumeric, which is what keeps the usual
    /// <c>## 2.8.0 - 2026-07-25</c> heading reading as "2.8.0": the date is separated by a SPACE, so the
    /// optional <c>-…</c> group never engages, and neither group can swallow whitespace.
    /// </para>
    /// <para>
    /// RELATION TO <c>eng/verify-nuget-package-manifest.sh</c>, which gates the same release against the
    /// same manifest: the PREDICATE and the RELATION are deliberately identical — <c>PackageId</c> present
    /// and <c>IsPackable=false</c> absent, compared as relative project PATHS. They must be, because two
    /// guards disagreeing over one manifest is not a double check but a deadlock: no manifest state
    /// satisfies both, and each points at the other.
    /// </para>
    /// <para>
    /// ONE DIMENSION IS INTENTIONALLY NOT ALIGNED: the script globs <c>find src -maxdepth 2</c>, this
    /// sweeps <c>AllDirectories</c>. The divergence is kept because it only ever runs one way — anything
    /// nested deeper is INVISIBLE to the script and VISIBLE here, so this guard is the stricter of the
    /// two and aligning them would mean loosening it. A packable project moved below depth 2 therefore
    /// fails here first, which is the correct order: the release is blocked by the guard that can see it
    /// rather than published by the one that cannot.
    /// </para>
    /// </summary>
    [Fact]
    public void PackableProjects_AgreeOnOneVersion_AndItIsTheOneTheChangelogAnnounces()
    {
        var repositoryRoot = FindRepoRoot();

        // NOTHING IS FILTERED OUT SILENTLY. This used to end in .Where(item => item.Version is not null),
        // which looks like tidying and is really a hole: a project that LOSES its <Version> stops being a
        // counterexample instead of becoming one, so deleting the element from six csproj left the guard
        // green and six packages shipped unverified. Everything a PackageId claims is packable is carried
        // through to the assertions below, nulls included, and the nulls are what gate (b) reports.
        var packableProjects = Directory.EnumerateFiles(
                Path.Combine(repositoryRoot, "src"),
                "*.csproj",
                SearchOption.AllDirectories)
            .Select(path => (Path: path, Document: XDocument.Load(path)))
            .Select(item => (
                item.Path,
                PackageId: item.Document.Descendants("PackageId")
                    .Select(element => element.Value.Trim())
                    .FirstOrDefault(),
                Version: item.Document.Descendants("Version")
                    .Select(element => element.Value.Trim())
                    .SingleOrDefault(),
                IsUnpackable: item.Document.Descendants("IsPackable")
                    .Any(element => string.Equals(
                        element.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase))))
            // THE SAME PREDICATE eng/verify-nuget-package-manifest.sh:14 USES, and it has to be, because
            // both scripts gate the same release against the same manifest. Filtering on PackageId alone
            // made the two disagree in a way no manifest could satisfy: give a csproj a PackageId AND
            // IsPackable=false and listing it turned the shell script red while omitting it turned this
            // one red — a deadlock where each guard points at the other and the release cannot move.
            // src/ holds 5 such projects today, so this is a live shape, not a hypothetical.
            .Where(item => item.PackageId is not null && !item.IsUnpackable)
            .ToList();

        // ── (a) THE DENOMINATOR: paths, not names ──────────────────────────────────────────────────
        // Compared as SETS OF RELATIVE PATHS, the same relation the shell script compares. Projecting to
        // the bare filename made the assertion weaker than its own message: "must still exist under src/"
        // was not being checked at all, and a manifest line pointing at
        // src/THIS-DIRECTORY-DOES-NOT-EXIST/Tempo.Blazor.Maps.csproj stayed GREEN because the basename
        // still matched. The path is what CI feeds to dotnet pack, so the path is what must agree.
        // A count would prove even less: swapping one package for another keeps 26 == 26.
        var manifested = File.ReadAllLines(Path.Combine([repositoryRoot, .. ManifestPath]))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(line => line.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        var discovered = packableProjects
            .Select(item => Path.GetRelativePath(repositoryRoot, item.Path).Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);

        // BOTH DIRECTIONS IN ONE SCOPE, so a single run names everything that drifted. Without the scope
        // the first assertion throws and hides the second, and a rename — the case a count cannot see at
        // all — is precisely the one that populates both sides at once.
        using (new AssertionScope())
        {
            manifested.Except(discovered).OrderBy(path => path, StringComparer.Ordinal).Should().BeEmpty(
                "every manifest entry must name a packable project that exists at that path; one listed "
                + "here and missing from the sweep is a package that moved, was renamed or was deleted "
                + "while CI still tries to push it");

            discovered.Except(manifested).OrderBy(path => path, StringComparer.Ordinal).Should().BeEmpty(
                "every packable project under src/ must be listed in the manifest at its own path; one "
                + "missing from it is a package this guard would check and CI would never publish");
        }

        // ── (b) THE CONTENT: a packable project without a version ──────────────────────────────────
        // Separate from (a) on purpose, because they fail in different worlds: (a) cannot see a project
        // that left src/ entirely (it disappears from BOTH sides only if the manifest is edited too),
        // and (b) is vacuously satisfied for exactly that project. Together they cover the move and the
        // omission; either alone leaves one of them silent.
        packableProjects
            .Where(item => item.Version is null)
            .Select(item => Path.GetRelativePath(repositoryRoot, item.Path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Should().BeEmpty(
                "a project that declares a PackageId is shipped, so it must declare the version it ships "
                + "under; without one it inherits whatever the build supplies and escapes the check below");

        var announced = ReadAnnouncedVersion(repositoryRoot);

        announced.Should().NotBeEmpty(
            "the changelog must open with the version being released, or there is nothing to check against");

        packableProjects.Should().OnlyContain(
            item => item.Version == announced,
            $"every locally packable project must ship under the version the changelog announces ({announced}); "
            + "a package numbered independently of the release notes is how a fix ships to nobody");
    }

    /// <summary>
    /// The version the changelog announces is either UNTAGGED, or its tag names the commit being packed.
    /// <para>
    /// THE DEFECT THIS EXISTS FOR, measured rather than imagined: on 2026-08-21 the changelog announced
    /// 2.8.19, <c>v2.8.19</c> pointed at <c>714093ce</c> and <c>HEAD</c> was <c>d1c8e776</c> — a second
    /// minting of a number that had already gone out. The artefact published under 2.8.19 records
    /// <c>714093ce</c> in its own nuspec; all 26 packages staged locally under the same number record
    /// <c>d1c8e776</c>. TWO COMMITS BEHIND ONE VERSION NUMBER is the entire defect: the number is the only
    /// handle a consumer has, and a reused one stops naming a tree.
    /// </para>
    /// <para>
    /// WHAT THIS DELIBERATELY DOES NOT REST ON: that identical <c>src/</c> implies an identical package.
    /// <c>git diff --name-only v2.8.19 HEAD -- src/</c> is empty here, which is what made the reuse look
    /// harmless, and the argument runs the other way round rather than being reversed — nobody has a
    /// controlled measurement of that implication, because the two packages carrying 2.8.19 do not share
    /// a commit, so <c>src/</c> was never the only thing that differed between them. The guard needs
    /// neither direction: it compares a tag against a commit and never opens a package.
    /// </para>
    /// <para>
    /// WHY THE TWO GUARDS AROUND IT DO NOT SEE IT — they compare csproj ↔ CHANGELOG and nuspec ↔ HEAD,
    /// and BOTH those pairs were perfectly consistent while the number was already taken. The pair
    /// nobody compared is the announced version against the tag of that name, which is the only one
    /// that answers "is this number still free". That is the whole reason this is a third test and not
    /// another assertion inside one of them.
    /// </para>
    /// <para>
    /// UNCONDITIONALLY ASSERTED, and the shape is deliberate: there is no state in which the comparison
    /// below is skipped. The ternary only chooses how the subject is SPELLED — "no such tag" is a value
    /// like any commit id, not a branch out of the check — so a missing tag is an accepted value rather
    /// than a silent exit. Nothing here reports a condition to a reader and leaves the enforcing to them.
    /// </para>
    /// <para>
    /// WHY THIS ONE SHELLS OUT TO GIT WHILE <see cref="ReadGitHead"/> DELIBERATELY DOES NOT. That helper
    /// exists so the staged-package half survives a container with a working tree and no git binary, and
    /// it can afford to: reading <c>.git/HEAD</c> is a file read. A TAG cannot be resolved that way here —
    /// this repository's release tags are ANNOTATED (<c>git cat-file -t v2.8.19</c> = <c>tag</c>), so
    /// <c>refs/tags/v2.8.19</c> holds a tag OBJECT id and peeling it to a commit means inflating a loose
    /// object or reading a packfile. A guard that gets that wrong reports "no such tag", which is this
    /// check's PASSING value — the failure mode of a file-only reader here is a false green, so the
    /// instrument is git itself and its absence is a red rather than a shrug.
    /// </para>
    /// <para>
    /// AND THE LOOKUP IS <c>rev-parse --verify --quiet</c> RATHER THAN <c>rev-list -n1</c>, for the same
    /// reason. <c>rev-list -n1 v2.8.20</c> exits 128 both when the tag does not exist AND when the
    /// command was not run inside a repository at all, so the two would be indistinguishable and the
    /// second would read as "the number is free". <c>rev-parse --verify --quiet</c> separates them by
    /// exit code — 0 with the commit on stdout, 1 for a ref that is not there, anything else for an
    /// instrument that did not work — and the third case is asserted, so a broken lookup is a named
    /// failure. A precondition is a red; a branch is silence.
    /// </para>
    /// <para>
    /// WHAT A GREEN DOES NOT PROVE, stated because the gap is real and cheap to over-read. It sees the
    /// tags in THIS ref store. <c>actions/checkout@v4</c> fetches at depth 1 and does not fetch tags, so
    /// on a push to main the CI run has no tags to find and this passes without having looked at
    /// anything — which is why the line printed on every run names how many tags were visible, so a zero
    /// says so instead of hiding inside a green. It also says nothing about nuget.org: a number can be
    /// published without a tag ever existing, and only a feed lookup answers that.
    /// </para>
    /// </summary>
    [Fact]
    public void AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked()
    {
        var repositoryRoot = FindRepoRoot();
        var announced = ReadAnnouncedVersion(repositoryRoot);
        var tagName = "v" + announced;

        var head = RunGit(repositoryRoot, "rev-parse", "HEAD");
        var tagLookup = RunGit(
            repositoryRoot, "rev-parse", "--verify", "--quiet", $"refs/tags/{tagName}^{{commit}}");
        var tagList = RunGit(repositoryRoot, "tag", "--list");

        // THE POPULATION, ON EVERY RUN INCLUDING A GREEN ONE. "No such tag" is this guard's passing
        // value, so "no tags at all" produces the same green as "this number is free" — the count is
        // what tells them apart, and a run in a ref store with zero tags says so in its own output
        // rather than being reconstructed later from what somebody assumes CI fetches.
        var visibleTags = tagList.ExitCode == 0
            ? tagList.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length
            : -1;

        var found = tagLookup.ExitCode == 0 ? tagLookup.StandardOutput : NoSuchTag;

        _output.WriteLine(
            $"[ReleaseContract] announced={announced} tag={tagName} tag-commit={found} "
            + $"head={(head.ExitCode == 0 ? head.StandardOutput : "(git rev-parse HEAD failed)")} "
            + $"visible-tags={visibleTags} lookup-exit={tagLookup.ExitCode}");

        using (new AssertionScope())
        {
            announced.Should().NotBeEmpty(
                "the changelog must open with the version being released, or there is no number to ask "
                + "the tag store about");

            head.ExitCode.Should().Be(
                0,
                "the commit being packed is the other half of this comparison, so a HEAD that cannot be "
                + $"read is a failed check rather than a skipped one (git said: {Describe(head)})");

            tagLookup.ExitCode.Should().BeOneOf(
                [0, 1],
                "0 means the tag resolved and 1 means it is not in this ref store; any other status is "
                + "the lookup itself failing, and letting that count as 'the tag is absent' would turn a "
                + $"broken instrument into this guard's passing answer (git said: {Describe(tagLookup)})");

            // ONE COMPARISON, NO BRANCH. The subject is what `v<announced>` names right now; the two
            // acceptable values are "nothing" and "this HEAD". Written as strings rather than as a
            // nullable commit so the failure message names the TAG, the commit it is stuck on and the
            // commit it would have to name — the three things somebody staring at a red needs, and the
            // three a bare "expected null" would not give them.
            $"{tagName} -> {found}".Should().BeOneOf(
                [$"{tagName} -> {NoSuchTag}", $"{tagName} -> {head.StandardOutput}"],
                "a released version number is spent: the tag already names a tree, and the artefact "
                + "published under it is immutable on the feed, so re-announcing that number ships "
                + "different bytes under a label that points somewhere else. Either this tag does not "
                + "exist yet, or it is this very commit being re-packed. If it names another commit, "
                + "bump the changelog and every packable csproj to the next free number — retagging "
                + "cannot reach what is already on the feed");
        }
    }

    /// <summary>
    /// The pack script passes the commit in, refuses a tree no commit describes, asks that question
    /// again AFTER packing, and reads the stamp back out of the bytes it produced.
    /// <para>
    /// THE ASYMMETRY THAT WAS THERE UNTIL 2026-08-21, and why the fourth clause exists: the refusal
    /// ran once, before the loop, and nothing asked again afterwards — while the pack is itself a
    /// writer, because <c>BundleCssFiles</c> is hooked <c>BeforeTargets=…;GenerateNuspec;Pack</c> and
    /// writes a TRACKED file. So a pack could ship bytes the stamped commit does not contain and
    /// every check here would still agree, all of them having been computed before that write. The
    /// hole had been noticed and papered over with a HAND-RUN <c>git status</c> after the pack, which
    /// is not a gate: the next pack does not inherit somebody's habit.
    /// </para>
    /// <para>
    /// THE DEFECT THIS EXISTS FOR, measured rather than imagined: the published 2.8.15 nuspec carries
    /// <c>commit="efb00b89…"</c>, which is 2.8.14 — one release behind the content it actually ships. The
    /// content was correct; the LABEL was a release stale. That is worse than an empty field, because the
    /// method this repository verifies releases by is "read the package content AND the recorded commit",
    /// so an auditor who follows the label checks out the wrong tree, does not find the fix, and reports a
    /// correct release as broken. A piece of evidence that accuses a good release is the expensive kind.
    /// </para>
    /// <para>
    /// MECHANISM: SourceLink lives in the SDK, so <c>RepositoryCommit</c> comes from
    /// <c>SourceRevisionId</c>, which <c>InitializeSourceControlInformation</c> resolves at BUILD time.
    /// <c>eng/pack-nuget-packages.sh</c> packs with <c>--no-build</c>, so it reuses whatever the previous
    /// build left in <c>obj/</c> — and an incremental build that decided nothing changed leaves the
    /// PREVIOUS commit there.
    /// </para>
    /// <para>
    /// WHY THE CONTRACT IS READ OUT OF THE SCRIPT AT ALL: a unit test cannot observe "the commit at the
    /// moment of packing". This half is therefore the always-running one and it is a TEXT assertion by
    /// necessity, not by preference — it is separated from
    /// <see cref="PackedPackages_RecordTheCommitTheyWereBuiltFrom"/> because that one measures a
    /// population that is usually empty, and one <c>[Fact]</c> holding both meant the unconditional
    /// half's green and the empty half's green were the same green.
    /// </para>
    /// <para>
    /// WHY THE DIRTY-TREE CLAUSE IS ASSERTED AS SCRIPT TEXT AND NOT MEASURED: the equality the other
    /// test checks is a TAUTOLOGY over a dirty tree, and so are the pack script's own two halves —
    /// all three read the same HEAD, so all three agree by construction while the packed bytes come
    /// from source no commit contains. This was not silence but an active false confirmation: on
    /// 2026-08-18 the staging directory held 26 <c>Tempo.*.2.8.18.nupkg</c> stamped
    /// <c>commit="d49ede02…"</c> (2.8.17) and the loop certified every one of them, because HEAD
    /// really was d49ede02 while the 2.8.18 content sat uncommitted. The tests cannot escape that on
    /// their own: <see cref="ReadGitHead"/> deliberately does not shell out to git, so it can read which
    /// commit HEAD points at but never whether the tree matched it at pack time. The script's
    /// <c>git status --porcelain</c> refusal is therefore the only place the loop can be broken, and
    /// the assertions below exist so that deleting it is a red test rather than a silent regression.
    /// WHAT THEY DO NOT PROVE, stated so nobody reads more into a green: they prove the clause is
    /// PRESENT in the file, not that it runs, not that it is reachable, and not that its exit code is
    /// honoured. Only running the script over a dirty tree proves that, and a unit test has no packing
    /// run to observe.
    /// </para>
    /// <para>
    /// THE COMMENT PROJECTION BELOW SEES ONLY WHOLE-LINE <c>#</c>, and that condition is asserted here
    /// rather than left as a remark. Stripping lines whose first non-space character is <c>#</c> leaves a
    /// TRAILING comment (<c>code # note</c>) in the projected text, so a needle that survived only inside
    /// such a comment would keep this guard green after its code was deleted. Today there is no trailing
    /// comment in the script — measured, not assumed — which is why the projection is still sound; the
    /// assertion makes that precondition SELF-REPORTING instead of an unwatched assumption, so the day
    /// somebody adds one the guard says so rather than quietly weakening. A precondition is a red; a
    /// branch is silence. <see cref="FindTrailingComments"/> decides this by OVER-APPROXIMATION: every
    /// <c>#</c> outside a whole-line comment counts until a named entry in
    /// <see cref="HashesThatAreNotComments"/> accounts for it, so the exemptions are readable in the
    /// source and a false green needs a written reason rather than a gap in a parser.
    /// </para>
    /// </summary>
    [Fact]
    public void PackScript_PassesTheCommitIn_RefusesADirtyTree_AndVerifiesTheStampBackOut()
    {
        var repositoryRoot = FindRepoRoot();
        var packScript = File.ReadAllText(Path.Combine([repositoryRoot, .. PackScriptPath]));

        using (new AssertionScope())
        {
            packScript.Should().Contain(
                "-p:RepositoryCommit=",
                "pack runs with --no-build, so the commit must be passed in explicitly; without it the "
                + "nuspec inherits whatever commit the last incremental build cached in obj/");

            packScript.Should().Contain(
                "git rev-parse HEAD",
                "the commit that is passed in has to be read from the repository at pack time, not "
                + "hardcoded or taken from an environment variable somebody can forget to update");

            packScript.Should().Contain(
                "refusing to ship them",
                "passing the flag is the fix, reading it back out of the produced nupkg is the guard; a "
                + "-p: value has already been observed losing to a cached one, so the pack must verify "
                + "the bytes it produced rather than trust that the flag took effect");

            // THE DIRTY-TREE CLAUSE IS ASSERTED AGAINST THE SCRIPT'S CODE, NOT ITS FULL TEXT. Its
            // needles also occur in the comment block that explains it — deliberately, it is the record
            // of why the clause exists — so asserting over the whole file would let "delete the code,
            // keep the prose" stay green, and prose is exactly what survives a hasty revert. Stripping
            // comment lines makes the mutation that matters red. The assertions above keep reading the
            // whole file: their needles are code-only today (measured), and widening this phase into
            // them would edit guards it was not scoped to.
            var packScriptCode = string.Join(
                '\n',
                packScript.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

            // THE PROJECTION'S PRECONDITION, ASSERTED RATHER THAN ASSUMED. The line above removes only
            // WHOLE-LINE comments, so a trailing one (`code # note`) passes straight through it. The
            // needles below would then survive inside prose after their code was deleted, which is
            // the exact mutation this projection exists to catch. The condition it depends on — "this
            // script has no trailing comments" — was previously a sentence in a review and nothing else;
            // as an assertion it announces itself the day it stops holding, instead of silently taking
            // the strength out of the checks underneath. Widening the projection to strip trailing
            // comments would need a real shell tokeniser in the strip path, so the cheaper and stricter
            // move is to forbid the shape and exempt only hashes somebody has written a reason for.
            FindTrailingComments(packScript).Should().BeEmpty(
                "the comment projection used by the assertions below only removes whole-line '#', so a "
                + "trailing comment would let 'delete the code, keep the note' stay green. If a line "
                + "listed here carries a '#' that is NOT a comment, add it to HashesThatAreNotComments "
                + "with the reason it is not one — the exemption is meant to be a written, reviewable "
                + "act; otherwise move the note onto its own line. Already exempted: "
                + DescribeCommentExemptions());

            packScriptCode.Should().Contain(
                "git status --porcelain",
                "over a dirty tree there is no commit whose tree equals the bytes being packed, so the "
                + "stamp, the script's own read-back and the assertion in the staged half all agree by "
                + "construction while labelling the packages with a commit that does not contain their "
                + "source; the pack has to inspect the working tree, which is the one thing a unit test "
                + "cannot do");

            packScriptCode.Should().Contain(
                "ALLOW_DIRTY_PACK",
                "the refusal needs a named, explicit escape or the next person under time pressure "
                + "deletes the check instead; an opt-out nobody can spell is an opt-out that becomes a "
                + "reverted guard");

            packScriptCode.Should().Contain(
                "-dirty",
                "the escape must not restore the lie it exists around: a package packed off uncommitted "
                + "source has to say so in its own stamp rather than borrow its parent commit's good "
                + "name, and a '-dirty' stamp can never equal a commit id, so the staged half fails it "
                + "if such a package is ever staged for a release");

            // THE OTHER END OF THE DIRTY-TREE QUESTION: the pack is itself a writer, so the refusal
            // above (which runs once, before the loop) cannot see what the pack does. The needle is
            // the COMPARISON, not merely a second reading of git status: a bare emptiness test after
            // the loop would fire on every ALLOW_DIRTY_PACK=1 run and be turned off, and a reading
            // nobody compares is a variable, not a gate.
            packScriptCode.Should().Contain(
                "\"$post_status\" != \"$pre_status\"",
                "dotnet pack runs BundleCssFiles, which writes the tracked file "
                + "src/Tempo.Blazor/wwwroot/css/tempo-blazor.bundled.css, so a pack can leave the "
                + "tree different from the one its commit stamp names — and every other check here "
                + "was computed before that write, so they all agree by construction. Comparing the "
                + "status after the loop against the one recorded before it asks whether the SET OF "
                + "PATHS GIT REPORTS changed while the pack ran, and it stays usable under "
                + "ALLOW_DIRTY_PACK=1 where a bare emptiness test would not. ITS REACH IS NARROWER "
                + "THAN 'did the pack change the tree': porcelain reports path STATUSES, not "
                + "content, so over a non-empty pre_status a rewrite of an ALREADY-MODIFIED file "
                + "leaves the comparison equal. Over an EMPTY pre_status — the only state whose "
                + "packages are publishable, the rest being stamped -dirty — it is complete. WHAT "
                + "THIS PROVES: the comparison is PRESENT. That it FIRES was measured by packing "
                + "over a tree whose bundle sources had been perturbed, which a unit test has no "
                + "pack run to do");

            // OVER THE SAME PROJECTION, but about the script's read-back rather than its dirty-tree
            // refusal — kept apart from the ones above so the grouping stays readable.
            packScriptCode.Should().Contain(
                "|| true)\"",
                "the read-back has to survive a nuspec carrying no commit attribute at all: grep exits 1 "
                + "there, `set -o pipefail` promotes that to the pipeline's status and `set -e` then kills "
                + "the script on the assignment itself — before the message naming the offending package "
                + "is ever printed, which is why the '${stamped:-<none>}' fallback written for exactly "
                + "that case could not be reached by it. WHAT THIS PROVES, said so nobody reads more into "
                + "a green: the tolerance is PRESENT in the file. That it RUNS was measured by executing "
                + "the read-back block over a package with no stamp, which a unit test has no pack run to "
                + "do");
        }
    }

    /// <summary>
    /// A package staged under the announced version records the commit it was built from.
    /// <para>
    /// This is the half that reads the produced bytes. <c>packages/</c> is gitignored and normally
    /// absent, and even when it exists the population is filtered to the announced version — so it is
    /// evidence when there is something to check and NOTHING when there is not. The contract half that
    /// runs on every machine is
    /// <see cref="PackScript_PassesTheCommitIn_RefusesADirtyTree_AndVerifiesTheStampBackOut"/>.
    /// </para>
    /// <para>
    /// WHY AN EMPTY POPULATION IS A SKIP AND NOT A PASS. The filter makes the empty case the NORMAL one:
    /// the first thing a release does is announce the next number, at which point nothing staged carries
    /// it any more. A pass over nothing was byte-for-byte the same green as a pass over 26 correct
    /// packages, so "the packages were checked" could never be read out of a green run. The previous
    /// treatment wrote the population size to test output on every exit, which put the evidence somewhere
    /// nothing holds it: deleting that one line — or the method behind it — left every assertion green,
    /// so the report was a claim about the run that the run did not enforce. The OUTCOME carries it now.
    /// An empty population is reported as SKIPPED, which is a third result in the .trx and in every
    /// runner, so "nothing was checked" can no longer arrive dressed as "everything was checked".
    /// </para>
    /// <para>
    /// AND THE SIZE OF THAT FIX, said plainly rather than inflated: the old shape did not state anything
    /// FALSE. It stated nothing — the loss was evidence, not truth — which makes this a weaker instance
    /// of vacuous green than the ones that certify something. It removes an outcome two different states
    /// shared. It does not make this half run any more often, it does not widen what it looks at, and a
    /// skip here is not a defect: it is the repository's ordinary state between a bump and a pack.
    /// </para>
    /// <para>
    /// THE SKIP IS DECIDED AT DISCOVERY, BY <see cref="StagedPackagesFactAttribute"/>, AND THAT IS NOT A
    /// PREFERENCE. xUnit v2 has no runtime skip at all: <c>Assert.Skip</c> is not in
    /// <c>xunit.assert 2.9.3</c>'s <c>Assert</c>, and the <c>$XunitDynamicSkip$</c> protocol its
    /// <c>SkipException</c> speaks has zero occurrences in <c>xunit.execution.dotnet.dll</c> of the same
    /// version — measured with the same tool that finds <c>SkipReason</c> there five times, so the zero
    /// is a reading and not a silence. The only skip this runner honours is the one an attribute names
    /// during discovery, so the population has to be surveyed twice: once to decide, once to assert over.
    /// <see cref="ReleaseStagingSurvey.Take"/> is that one survey, called from both places, so the two
    /// can disagree about the world changing underneath them but never about what the rule is.
    /// </para>
    /// <para>
    /// THE FIRST ASSERTION IN THE BODY IS A TRIPWIRE, NOT THE RULE — and it is the reason the skip cannot
    /// be quietly downgraded. Swap the attribute back to a plain <c>[Fact]</c> and the body starts
    /// running over the empty population, where
    /// <see cref="ReleaseStagingSurvey.NothingCouldBeOpened"/> is non-null and that assertion goes RED.
    /// So the two shapes are skip and red; the pass over nothing is not reachable from either. It also
    /// covers the case the double survey creates on its own: a <c>packages/</c> that changed between
    /// discovery and execution is a named failure rather than a loop over zero.
    /// </para>
    /// <para>
    /// THE FILTER ITSELF IS NOT THE DEFECT and is deliberately left alone — see the comment at its site.
    /// WHAT A GREEN HERE STILL DOES NOT PROVE: that the tree matched HEAD at pack time. That remains the
    /// pack script's own refusal, and <see cref="ReadGitHead"/> cannot see it.
    /// </para>
    /// </summary>
    [StagedPackagesFact]
    public void PackedPackages_RecordTheCommitTheyWereBuiltFrom()
    {
        var survey = ReleaseStagingSurvey.Take();
        var inspected = 0;
        var withoutNuspec = 0;

        using (new AssertionScope())
        {
            survey.NothingCouldBeOpened.Should().BeNull(
                "this test only runs when there is something staged to open — StagedPackagesFactAttribute "
                + "skips it otherwise. Reaching here with an empty population means either the attribute "
                + "was removed, which would turn 'nothing was checked' back into a pass, or the staging "
                + "directory changed between discovery and execution, which makes the run's population "
                + "unknown rather than empty");

            foreach (var package in survey.Candidates)
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(package);
                var nuspecEntry = archive.Entries.FirstOrDefault(
                    entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                if (nuspecEntry is null)
                {
                    withoutNuspec++;
                    continue;
                }

                inspected++;

                using var stream = nuspecEntry.Open();
                var stamped = XDocument.Load(stream)
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "repository")
                    ?.Attribute("commit")?.Value;

                stamped.Should().Be(
                    survey.Head,
                    $"{Path.GetFileName(package)} must record the commit it was built from; a package "
                    + "labelled with an older commit sends the next auditor to a tree that does not "
                    + "contain the change it ships");
            }

            // THE OTHER WAY THIS HALF COULD PASS WITHOUT CHECKING ANYTHING, closed here rather than
            // reported. A candidate whose archive carries no .nuspec is counted and skipped by the loop
            // above, so a non-empty population made entirely of such files runs ZERO assertions,
            // leaves NothingCouldBeOpened null — the tripwire is about an EMPTY population and cannot
            // see this — and passes with nuspec-inspected=0. That is the same silent pass over a
            // non-empty population this whole test was reshaped to remove, arriving through the one
            // door the skip does not cover.
            withoutNuspec.Should().Be(
                0,
                $"{survey.Candidates.Count} package(s) claim to be this release, so every one of them "
                + "must be openable and carry the nuspec its commit stamp lives in; a candidate without "
                + "one is not a package that passed the check, it is a package the check could not read");

            // INSIDE THE SCOPE, so a red run still says how big its population was: failures above are
            // collected rather than thrown. On this path the line is a convenience and not the evidence —
            // the assertions are, which is the whole difference from the shape this replaced.
            _output.WriteLine(DescribeStagedPopulation(
                survey.Announced, survey.Staged.Count, survey.Candidates.Count, inspected,
                note: $"{survey.Candidates.Count} candidate(s) checked against HEAD {survey.Head}"
                    + (withoutNuspec == 0
                        ? string.Empty
                        : $"; {withoutNuspec} carried no .nuspec and were skipped")));
        }
    }

    /// <summary>
    /// Marks the staged-package guard as SKIPPED when there is no package for it to open.
    /// <para>
    /// <c>FactAttribute.Skip</c> is virtual and xUnit v2 reads it through
    /// <c>ReflectionAttributeInfo.GetNamedArgument</c>, which calls the property getter on a real
    /// instance rather than reading the attribute blob — so an overridden getter is the supported way to
    /// decide a skip from the environment, and in this runner it is the ONLY way (see the remark on the
    /// test itself for what was measured about dynamic skip).
    /// </para>
    /// <para>
    /// A BROKEN PROBE MUST NEVER BECOME A SKIP, which is why the catch returns null rather than a reason.
    /// Null means "do not skip", so an exception in the survey hands the test to the runner, where the
    /// same exception will be thrown again inside the body and reported as a failure with its stack. The
    /// opposite treatment — skip on error — would make every future breakage in this file look like the
    /// repository's ordinary between-releases state.
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class StagedPackagesFactAttribute : FactAttribute
    {
        public override string? Skip
        {
            get
            {
                try
                {
                    return ReleaseStagingSurvey.Take().NothingCouldBeOpened;
                }
#pragma warning disable CA1031 // see the remark above: any failure here must NOT read as "skip"
                catch (Exception)
#pragma warning restore CA1031
                {
                    return null;
                }
            }

            set => base.Skip = value;
        }
    }

    /// <summary>
    /// What the staging directory holds right now, surveyed once and read by both the skip decision and
    /// the assertions. One method rather than two so the attribute and the test can never be measuring
    /// different populations by different rules.
    /// </summary>
    /// <param name="Announced">The version the changelog announces, which is what the filter is keyed on.</param>
    /// <param name="Staged">Every non-symbol package in the staging directory, whatever version it carries.</param>
    /// <param name="Candidates">The subset claiming the announced version — the population that gets opened.</param>
    /// <param name="Head">The commit the packages are compared against, or null when the layout hides it.</param>
    /// <param name="NothingCouldBeOpened">
    /// The reason there is nothing to check, already formatted as the report line, or null when there is.
    /// Null is the only value that lets the guard run.
    /// </param>
    internal sealed record ReleaseStagingSurvey(
        string Announced,
        IReadOnlyList<string> Staged,
        IReadOnlyList<string> Candidates,
        string? Head,
        string? NothingCouldBeOpened)
    {
        internal static ReleaseStagingSurvey Take()
        {
            var repositoryRoot = FindRepoRoot();
            var announced = ReadAnnouncedVersion(repositoryRoot);
            var staging = Path.Combine(repositoryRoot, "packages");
            var stagingExists = Directory.Exists(staging);

            // NON-RECURSIVE ON PURPOSE, and said out loud because a nested directory of packages is a
            // shape this staging area has held: EnumerateFiles without SearchOption.AllDirectories never
            // descends, so nothing nested has ever been part of this population. That is the right
            // reading — a nested package was staged for some other release and owes this HEAD nothing.
            var staged = stagingExists
                ? Directory.EnumerateFiles(staging, "*.nupkg")
                    .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
                    .OrderBy(path => path, StringComparer.Ordinal)
                    .ToList()
                : [];

            var head = ReadGitHead(repositoryRoot);

            // ONLY THE PACKAGES THAT CLAIM TO BE THIS RELEASE. The staging directory is not cleaned
            // between releases by anything except the pack script itself, so it accumulates packages
            // from earlier versions. Those were built from the commit they say they were, and demanding
            // they match today's HEAD would make the guard permanently red for a reason that has nothing
            // to do with the release being shipped. The invariant is "a package that claims version X was
            // built from the commit that IS version X", so the population is the packages carrying the
            // announced version — and THAT is why an empty one skips: right after a version bump this
            // filter legitimately matches nothing, and a silent empty sweep is indistinguishable from a
            // full one.
            var releaseSuffix = "." + announced + ".nupkg";

            var candidates = staged
                .Where(path => path.EndsWith(releaseSuffix, StringComparison.Ordinal))
                .ToList();

            // THE THREE WAYS THERE IS NOTHING TO OPEN, each keeping its own sentence. They are ordered
            // from the outermost cause inwards so the reason names the first thing that was missing
            // rather than the last thing that failed because of it.
            string? nothingCouldBeOpened = null;
            if (!stagingExists)
            {
                nothingCouldBeOpened = DescribeStagedPopulation(
                    announced, staged: 0, candidates: 0, inspected: 0,
                    note: $"no staging directory at '{staging}', so no package was opened");
            }
            else if (head is null)
            {
                nothingCouldBeOpened = DescribeStagedPopulation(
                    announced, staged.Count, candidates.Count, inspected: 0,
                    note: "HEAD could not be read in this layout, so no package could be compared "
                        + "against it (see ReadGitHead: guessing would fail correct packages)");
            }
            else if (candidates.Count == 0)
            {
                nothingCouldBeOpened = DescribeStagedPopulation(
                    announced, staged.Count, candidates: 0, inspected: 0,
                    note: "0 candidates — nothing staged carries the announced version; staged instead: "
                        + DescribeStagedVersions(staged));
            }

            return new ReleaseStagingSurvey(announced, staged, candidates, head, nothingCouldBeOpened);
        }
    }

    /// <summary>
    /// One line stating how many packages the staged half actually opened, used as the reason a run is
    /// SKIPPED and as the note a run that asserted writes out.
    /// <para>
    /// IT IS A STRING RATHER THAN A WRITE, and that is the difference between this and what it replaced.
    /// The old method wrote to <see cref="ITestOutputHelper"/> and returned; deleting the call left the
    /// test green, so the population report was carried by nothing. Returning the sentence lets
    /// <see cref="StagedPackagesFactAttribute"/> hand it to the runner as a SKIP REASON, where it becomes
    /// part of an outcome instead of part of a log — and an outcome is the thing a runner, a .trx and a
    /// reviewer all read without being asked to.
    /// </para>
    /// <para>
    /// WHAT IT DOES NOT PROVE, said plainly so a reader does not borrow more from it: it reports the
    /// population this run saw. It is not a claim about what CI staged, and it cannot say whether the
    /// tree matched HEAD at pack time — that remains the pack script's own refusal.
    /// </para>
    /// </summary>
    private static string DescribeStagedPopulation(
        string announced, int staged, int candidates, int inspected, string note) =>
        $"[ReleaseContract] announced={announced} staged-nupkg={staged} "
        + $"release-matching={candidates} nuspec-inspected={inspected} :: {note}";

    /// <summary>
    /// The versions actually sitting in the staging directory, as "version xN", so a reported zero names
    /// the reason it is zero instead of only its size. Deliberately derived from the filenames present at
    /// the moment of the run rather than written down anywhere: a recorded expectation here would become a
    /// second home for the release number and would be wrong one bump later.
    /// </summary>
    private static string DescribeStagedVersions(IEnumerable<string> stagedPackages)
    {
        var groups = stagedPackages
            .Select(path => Regex.Match(
                Path.GetFileName(path), @"\.(?<version>\d+\.\d+\.\d+[^\\/]*)\.nupkg$").Groups["version"].Value)
            .Where(version => version.Length > 0)
            .GroupBy(version => version, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key} x{group.Count()}")
            .ToList();

        return groups.Count == 0 ? "(no versioned .nupkg at all)" : string.Join(", ", groups);
    }

    /// <summary>
    /// Lines of a shell script that are not whole-line comments and still carry a <c>#</c> the allow-list
    /// below does not account for. Returns "line number: line" for each, so a failure names the offender.
    /// <para>
    /// THIS IS AN OVER-APPROXIMATION, AND THAT IS THE DESIGN RATHER THAN A SHORTCUT. It replaces a
    /// quote-tracking scanner that tried to decide, per line, whether a <c>#</c> really opened a comment.
    /// That scanner was wrong in the one direction an instrument must never be wrong in: a string opened
    /// on one physical line and closed on the next took its trailing comment with it into silence
    /// (<c>x='line1⏎line2' # note</c>, and the same with double quotes), and so did <c>$'…\'…' # note</c>
    /// — all three are real comments to bash and all three came back as "nothing found". A scanner that
    /// can be silently wrong is worse than a crude one, because its silence is what the assertion reads
    /// as "the precondition holds".
    /// </para>
    /// <para>
    /// SO THE ASYMMETRY IS BUILT INSTEAD OF PROMISED. Every <c>#</c> outside a whole-line comment is a
    /// candidate. The only way out is <see cref="HashesThatAreNotComments"/> — named fragments, each
    /// carrying the reason that particular <c>#</c> is not a word opener — and a line is cleared only
    /// when NOTHING with a <c>#</c> is left after those fragments are removed from it, so an allow-listed
    /// line that also grows a real trailing comment is still reported. A false RED stays possible and is
    /// the intended side: it costs a human one reading and one allow-list entry. A false GREEN now needs
    /// somebody to add a fragment with a written reason, which is a reviewed act rather than an oversight.
    /// </para>
    /// <para>
    /// WHAT WAS ACTUALLY MEASURED IN THE SCRIPT, stated narrowly because the previous version of this
    /// remark claimed more than it had looked at: the only <c>#</c> characters outside whole-line comments
    /// are the two the allow-list names. Line continuations and arithmetic expansions do occur in the
    /// file; they carry no <c>#</c>, which is why they do not appear here — not because the file lacks
    /// them.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> FindTrailingComments(string script)
    {
        var found = new List<string>();
        var lines = script.Split('\n');

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');

            // A whole-line comment is what the projection above already removes, so it is out of scope
            // here by construction rather than by exemption.
            if (line.TrimStart().StartsWith('#') || !line.Contains('#', StringComparison.Ordinal))
            {
                continue;
            }

            // REMOVED, NOT MATCHED. Testing "does the line contain an allow-listed fragment" would clear
            // the WHOLE line, so `expected_count=${#projects[@]} # note` would pass — the exemption would
            // launder a real comment sitting next to an exempt one. Deleting the fragments and asking
            // whether any '#' survives keeps the exemption scoped to the characters it was granted for.
            var residue = line;
            foreach (var (fragment, _) in HashesThatAreNotComments)
            {
                residue = residue.Replace(fragment, string.Empty, StringComparison.Ordinal);
            }

            if (residue.Contains('#', StringComparison.Ordinal))
            {
                found.Add($"{index + 1}: {line.Trim()}");
            }
        }

        return found;
    }

    /// <summary>
    /// The <c>#</c> characters in <c>eng/pack-nuget-packages.sh</c> that are not comment openers, each
    /// with the reason it is not one. Keyed on the TEXT of the fragment rather than on a line number:
    /// a list of numbers is wrong the first time anything above it is edited, and it would be wrong
    /// silently, whereas a fragment that stops occurring simply stops exempting anything.
    /// <para>
    /// This is the instrument's off-diagonal left standing in the source. A reader can see exactly what
    /// the scanner will not flag and why, without re-running a mutation — which is the difference between
    /// an exemption and a blind spot.
    /// </para>
    /// </summary>
    /// <summary>
    /// The allow-list rendered for a failure message, which is also the only reason the Reason half of
    /// each entry exists as DATA rather than as a comment: a reader who has just been handed a red gets
    /// the exemptions and their justifications in the same breath, and an entry whose reason nobody can
    /// state is visibly missing rather than quietly absent.
    /// </summary>
    private static string DescribeCommentExemptions() => string.Join(
        "; ",
        HashesThatAreNotComments.Select(entry => $"'{entry.Fragment}' — {entry.Reason}"));

    private static readonly (string Fragment, string Reason)[] HashesThatAreNotComments =
    [
        ("'^[[:space:]]*(#|$)'",
            "inside a single-quoted ERE handed to grep: it matches the MANIFEST's comment lines, so the "
            + "'#' is data the script passes on, never a word the shell opens a comment with"),
        ("${#projects[@]}",
            "'${#name[@]}' is the array-length parameter expansion; its '#' follows '{' and belongs to "
            + "the expansion syntax, so it does not open a word and cannot start a comment"),
    ];

    /// <summary>
    /// The version the changelog announces: the first <c>##</c> heading whose text is a semantic version.
    /// Shared by both guards so they can never disagree about which release is being shipped.
    /// </summary>
    private static string ReadAnnouncedVersion(string repositoryRoot) => Regex.Match(
            File.ReadAllText(Path.Combine(repositoryRoot, "CHANGELOG.md")),
            @"^##\s*(?<version>\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?(?:\+[0-9A-Za-z][0-9A-Za-z.-]*)?)",
            RegexOptions.Multiline)
        .Groups["version"].Value;

    /// <summary>
    /// How an absent tag is SPELLED in the comparison above. A named constant because it appears as the
    /// subject and as one of the accepted values, and two literals that must stay identical are one
    /// typo away from a check that can never pass.
    /// </summary>
    private const string NoSuchTag = "(no such tag)";

    /// <summary>
    /// A git invocation rendered for a failure message: the status and whatever the command said. Used
    /// only in <c>because</c> arguments, so a red reports what the instrument reported rather than
    /// leaving the reader to re-run it.
    /// </summary>
    private static string Describe((int ExitCode, string StandardOutput, string StandardError) result) =>
        $"exit {result.ExitCode}"
        + (result.StandardOutput.Length == 0 ? string.Empty : $", stdout '{result.StandardOutput}'")
        + (result.StandardError.Length == 0 ? string.Empty : $", stderr '{result.StandardError}'");

    /// <summary>
    /// Runs git in the repository and hands back status and both streams, trimmed.
    /// <para>
    /// NOTHING IS SWALLOWED HERE. A missing git binary throws out of <c>Process.Start</c> and the test
    /// errors, which is the intended outcome: this file's tag guard has no meaning without git, and a
    /// caught exception folded into "the tag is absent" would be its passing value. The status is
    /// RETURNED rather than asserted so each caller can say which statuses are meaningful for the
    /// question it is asking.
    /// </para>
    /// <para>
    /// Arguments go through <see cref="ProcessStartInfo.ArgumentList"/>, not a joined string, so a ref
    /// name is never re-parsed by a shell — the peel suffix <c>^{commit}</c> alone would be mangled by
    /// one. Both streams are read BEFORE the wait, because waiting first deadlocks as soon as either
    /// pipe fills and <c>git tag --list</c> here is already 77 lines.
    /// </para>
    /// <para>
    /// NOT A NEW PATTERN IN THIS REPOSITORY: <c>BaselineWriteSweep.TrackedScreenshotPaths</c> spawns
    /// <c>git ls-files</c> the same way, down to <c>ArgumentList</c> and the redirected streams. The
    /// difference is what a failure means — there a non-zero status throws, here it is returned, because
    /// "the ref is not present" is a legitimate answer that shares its channel with "the lookup broke".
    /// </para>
    /// </summary>
    private static (int ExitCode, string StandardOutput, string StandardError) RunGit(
        string repositoryRoot, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started to resolve the release tag.");

        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, standardOutput.Trim(), standardError.Trim());
    }

    /// <summary>
    /// Reads <c>HEAD</c> without shelling out to git, so the guard works in a container that has the
    /// working tree but no git binary. Returns <see langword="null"/> when the layout is not the plain
    /// one (a worktree, a packed ref this cannot resolve), because guessing would be worse than saying
    /// nothing: a wrong "HEAD" would fail every correctly stamped package.
    /// </summary>
    private static string? ReadGitHead(string repositoryRoot)
    {
        var gitDirectory = Path.Combine(repositoryRoot, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            return null;
        }

        var headPath = Path.Combine(gitDirectory, "HEAD");
        if (!File.Exists(headPath))
        {
            return null;
        }

        var head = File.ReadAllText(headPath).Trim();
        if (!head.StartsWith("ref:", StringComparison.Ordinal))
        {
            return head.Length == 40 ? head : null;
        }

        var referencePath = Path.Combine(
            gitDirectory,
            head[4..].Trim().Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(referencePath) ? File.ReadAllText(referencePath).Trim() : null;
    }

    /// <summary>
    /// Walks up from the test assembly to the directory holding <c>TempoBlazor.slnx</c>.
    /// <para>
    /// KNOWN DUPLICATION, stated so it is a named limitation rather than an oversight: this is one of 29
    /// hand-rolled copies of the same walk-up in the non-E2E test projects — 28 besides this one — each
    /// re-deciding what to do when the marker is not found: some throw, some return null, some walk to
    /// the filesystem root. The marker itself appears in 139 files across all test projects, 110 of them
    /// in <c>Tempo.Blazor.E2E</c>, which reaches the root through its own base class. It is NOT unified
    /// here because a
    /// shared helper would have to live in a project all of them reference, and no such project exists
    /// today; introducing one is a structural change, not the hygiene this phase is scoped to.
    /// CLOSING CONDITION, so the item is falsifiable rather than perpetual: unify when a test-support
    /// assembly referenced by every test project exists (or is created deliberately), at which point every
    /// copy moves to it in one change and this remark is deleted. Until then the risk is bounded — a wrong
    /// root cannot pass silently here, because the population gates above turn it into a named failure.
    /// </para>
    /// </summary>
    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ??
            throw new DirectoryNotFoundException(
                "Could not locate the Tempo.Blazor repository root.");
    }
}
