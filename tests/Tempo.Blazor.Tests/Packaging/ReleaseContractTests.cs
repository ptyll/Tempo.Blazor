using FluentAssertions.Execution;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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
    /// The commit a package records as its origin must be the commit it was built from.
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
    /// WHY THIS GUARD READS THE SCRIPT AND THE PACKAGES, AND NOT ONLY THE PACKAGES: a unit test cannot
    /// observe "the commit at the moment of packing", and the staging directory is usually empty on a
    /// developer machine, so a packages-only assertion would pass vacuously exactly when it matters least
    /// and would give a false sense of coverage. The always-running half is therefore the CONTRACT — the
    /// pack script must pass the commit in, refuse to pack a tree no commit describes, and verify the
    /// stamp back out of the produced bytes. The second half checks any package that happens to be
    /// staged, which is what turns a stale local pack red.
    /// </para>
    /// <para>
    /// WHY THE DIRTY-TREE CLAUSE IS ASSERTED AS SCRIPT TEXT AND NOT MEASURED: the equality this test
    /// checks below is a TAUTOLOGY over a dirty tree, and so are the pack script's own two halves —
    /// all three read the same HEAD, so all three agree by construction while the packed bytes come
    /// from source no commit contains. This was not silence but an active false confirmation: on
    /// 2026-08-18 the staging directory held 26 <c>Tempo.*.2.8.18.nupkg</c> stamped
    /// <c>commit="d49ede02…"</c> (2.8.17) and the loop below certified every one of them, because HEAD
    /// really was d49ede02 while the 2.8.18 content sat uncommitted. This test cannot escape that on
    /// its own: <see cref="ReadGitHead"/> deliberately does not shell out to git, so it can read which
    /// commit HEAD points at but never whether the tree matched it at pack time. The script's
    /// <c>git status --porcelain</c> refusal is therefore the only place the loop can be broken, and
    /// the assertions below exist so that deleting it is a red test rather than a silent regression.
    /// WHAT THEY DO NOT PROVE, stated so nobody reads more into a green: they prove the clause is
    /// PRESENT in the file, not that it runs, not that it is reachable, and not that its exit code is
    /// honoured. Only running the script over a dirty tree proves that, and a unit test has no packing
    /// run to observe.
    /// </para>
    /// </summary>
    [Fact]
    public void PackedPackages_RecordTheCommitTheyWereBuiltFrom()
    {
        var repositoryRoot = FindRepoRoot();
        var packScript = File.ReadAllText(Path.Combine(repositoryRoot, "eng", "pack-nuget-packages.sh"));
        var announced = ReadAnnouncedVersion(repositoryRoot);

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

            // THE DIRTY-TREE CLAUSE IS ASSERTED AGAINST THE SCRIPT'S CODE, NOT ITS FULL TEXT. All three
            // needles below also occur in the comment block that explains the clause — deliberately, it
            // is the record of why the clause exists — so asserting over the whole file would let
            // "delete the code, keep the prose" stay green, and prose is exactly what survives a hasty
            // revert. Stripping comment lines makes the mutation that matters red. The three assertions
            // above keep reading the whole file: their needles are code-only today (measured), and
            // widening this phase into them would edit guards it was not scoped to.
            var packScriptCode = string.Join(
                '\n',
                packScript.Split('\n').Where(line => !line.TrimStart().StartsWith('#')));

            packScriptCode.Should().Contain(
                "git status --porcelain",
                "over a dirty tree there is no commit whose tree equals the bytes being packed, so the "
                + "stamp, the script's own read-back and the assertion below all agree by construction "
                + "while labelling the packages with a commit that does not contain their source; the "
                + "pack has to inspect the working tree, which is the one thing this test cannot do");

            packScriptCode.Should().Contain(
                "ALLOW_DIRTY_PACK",
                "the refusal needs a named, explicit escape or the next person under time pressure "
                + "deletes the check instead; an opt-out nobody can spell is an opt-out that becomes a "
                + "reverted guard");

            packScriptCode.Should().Contain(
                "-dirty",
                "the escape must not restore the lie it exists around: a package packed off uncommitted "
                + "source has to say so in its own stamp rather than borrow its parent commit's good "
                + "name, and a '-dirty' stamp can never equal a commit id, so the loop below fails it "
                + "if such a package is ever staged for a release");

            // The staged packages, when there are any. `packages/` is gitignored and normally absent, so
            // this half is evidence when it exists and silent when it does not — the assertions above are
            // what run unconditionally.
            var staging = Path.Combine(repositoryRoot, "packages");
            if (!Directory.Exists(staging))
            {
                return;
            }

            var head = ReadGitHead(repositoryRoot);
            if (head is null)
            {
                return;
            }

            // ONLY THE PACKAGES THAT CLAIM TO BE THIS RELEASE. The staging directory is not cleaned
            // between releases by anything except the pack script itself, so it accumulates older
            // versions — measured here: 26 leftovers from 2.8.7 next to a stray 2.1.1. Those were built
            // from the commit they say they were, and demanding they match today's HEAD would make the
            // guard permanently red for a reason that has nothing to do with the release being shipped.
            // The invariant is "a package that claims version X was built from the commit that IS
            // version X", so the population is the packages carrying the announced version.
            var releaseSuffix = "." + announced + ".nupkg";

            foreach (var package in Directory.EnumerateFiles(staging, "*.nupkg")
                         .Where(path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal))
                         .Where(path => path.EndsWith(releaseSuffix, StringComparison.Ordinal))
                         .OrderBy(path => path, StringComparer.Ordinal))
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(package);
                var nuspecEntry = archive.Entries.FirstOrDefault(
                    entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
                if (nuspecEntry is null)
                {
                    continue;
                }

                using var stream = nuspecEntry.Open();
                var stamped = XDocument.Load(stream)
                    .Descendants()
                    .FirstOrDefault(element => element.Name.LocalName == "repository")
                    ?.Attribute("commit")?.Value;

                stamped.Should().Be(
                    head,
                    $"{Path.GetFileName(package)} must record the commit it was built from; a package "
                    + "labelled with an older commit sends the next auditor to a tree that does not "
                    + "contain the change it ships");
            }
        }
    }

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
