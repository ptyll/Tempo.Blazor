using System.Text.RegularExpressions;
using FluentAssertions;
using FluentAssertions.Execution;
using Tempo.Blazor.Tests.Packaging;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// N328 — a component API change must carry its JSON documentation in the SAME commit.
/// <c>ComponentDocumentationFreshnessTests</c> compares the CURRENT source against the CURRENT
/// JSON and cannot see WHEN either side moved: a commit that adds <c>[Parameter] Foo</c> while a
/// follow-up commit regenerates the JSON leaves the docs green on every commit after the first,
/// but the first commit published an API no documentation described. The calibration proof is in
/// history: <c>ee1ec3df</c> added <c>TmDataTableColumn.SortLabel</c> and regenerated only the
/// repo-root <c>tempo-blazor*.json</c> aggregates — <c>JsonDocumentation/**</c> untouched — so a
/// consumer reading the docs tree found no <c>SortLabel</c>; <c>56fa6f04</c> mass-backfilled
/// <c>JsonDocumentation/**</c> in the same commit and must read clean.
/// <para>
/// Detection is deliberately line-based, not syntax-perfect: a <c>[Parameter</c> attribute line
/// or a changed public property declaration inside <c>src/**/Components/**/Tm*.razor{,.cs}</c>
/// counts as an API change; a matching <c>JsonDocumentation/**</c> path in the same commit is the
/// required documentation. A markup-only or internal-logic change does not flag, which is the
/// precision this guard trades for being cheap enough to run per-commit.
/// </para>
/// </summary>
public sealed class ComponentChangeCarriesJsonTests
{
    /// <summary>One file inside a commit: the repo-relative path plus the lines its diff added and removed.</summary>
    internal sealed record ChangedFile(
        string Path, IReadOnlyList<string> AddedLines, IReadOnlyList<string> RemovedLines);

    /// <summary>
    /// One commit: every path it touched (for the <c>JsonDocumentation/**</c> question) and the
    /// component files with their added/removed lines (for the API question).
    /// </summary>
    internal sealed record CommitChange(
        string Sha, IReadOnlyList<string> AllPaths, IReadOnlyList<ChangedFile> ComponentFiles);

    /// <summary>
    /// Commits whose same-commit violation is already healed by a later commit in the same
    /// release window — frozen at this entry; a healed grandfather may only leave by being
    /// re-measured, never joined by a new offender.
    /// <list type="bullet">
    /// <item><c>3980af1d</c> — the TDD red commit added <c>LayoutContent</c>/<c>IsViewportScope</c>
    ///   to <c>TmLayoutObserver</c>; the JSON documentation landed one commit later in
    ///   <c>ca00f94d</c>. The docs are current; the same-commit contract was still broken.</item>
    /// <item><c>631d92f8</c> — an F2-phase overlay commit (authored on the pre-merge main before
    ///   this N328 gate existed there) added <c>TmViewportProbe</c>'s
    ///   <c>[Parameter] OnMeasured</c> without a <c>JsonDocumentation/**</c> path; the probe
    ///   component was removed entirely by later F2 work, so no documentation gap ships. The
    ///   merge that brought the gate in is the first time the commit could be measured.</item>
    /// <item><c>80a3221a</c>, <c>bd49e089</c>, <c>2dcaea5d</c>, <c>5f5f2879</c> — F2-phase
    ///   bottom-sheet/focus-scope commits, likewise authored before the N328 gate existed on
    ///   main and first measured by the merge that brought the gate in. They opened
    ///   <c>TmFocusScope</c>/<c>TmSheetHandle</c>/<c>TmDrawer</c> API surface whose
    ///   <c>JsonDocumentation/**</c> overlays landed in later commits of the same window
    ///   (both components are documented today; the same-commit contract was the broken part).</item>
    /// </list>
    /// </summary>
    private static readonly HashSet<string> GrandfatheredViolations = new(StringComparer.Ordinal)
    {
        "3980af1d127c9c7c43dd84fb4f44c6164ee9a1c2",
        "631d92f86e59d503f02efa82c9886ebfb9fc6e27",
        "80a3221a7eba44cfce5ec3973afab4bf47b17480",
        "bd49e08990a77702a31019c93101f5ad6c7d8d26",
        "2dcaea5d90e2a28a42665197ee9d00b9402499b0",
        "5f5f2879117fb258ad0ef6e98971bbfe1f361a62",
    };

    /// <summary>
    /// A changed line that opens, closes or renames a parameter: the <c>[Parameter]</c> attribute
    /// itself (<c>[Parameter(</c> argumented forms included), or a public property declaration —
    /// the line the attribute sits on. PascalCase name required and no <c>(</c> before the brace
    /// so methods, constructors and event delegates cannot impersonate a property; the type/keyword
    /// exclusions keep <c>public class/static/event</c> declarations out.
    /// </summary>
    private static readonly Regex PublicPropertyDeclaration = new(
        @"^public\s+(?!static\b|class\b|const\b|delegate\b|enum\b|interface\b|record\b|event\b|struct\b|extern\b|implicit\b|explicit\b|operator\b)[^()\n]+?\s+[A-Z]\w*\s*\{",
        RegexOptions.Compiled);

    private static bool IsComponentApiFile(string path) =>
        path.StartsWith("src/", StringComparison.Ordinal)
        && path.Contains("/Components/", StringComparison.Ordinal)
        && !path.StartsWith("src/Tempo.Blazor.Demo", StringComparison.Ordinal)
        && Path.GetFileName(path).StartsWith("Tm", StringComparison.Ordinal)
        && (path.EndsWith(".razor", StringComparison.Ordinal)
            || path.EndsWith(".razor.cs", StringComparison.Ordinal));

    private static bool IsParameterAffectingLine(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("[Parameter", StringComparison.Ordinal)
            || (!trimmed.Contains('(') && PublicPropertyDeclaration.IsMatch(trimmed));
    }

    /// <summary>
    /// The pure evaluator the mutation tests drive: commits whose component API changed without
    /// a <c>JsonDocumentation/**</c> path in the same commit, reported as sha + offending files.
    /// Deliberately NOT churn-aware: a standalone <c>[Parameter]</c> attribute line moving from
    /// one property to another normalizes to identical text and must still flag — EOL churn is
    /// cancelled at the git layer instead (<c>--ignore-cr-at-eol</c> in
    /// <see cref="ChangeForCommit"/>), never by comparing line texts here.
    /// </summary>
    internal static IReadOnlyList<string> Evaluate(IEnumerable<CommitChange> commits)
    {
        var findings = new List<string>();
        foreach (CommitChange commit in commits)
        {
            var apiFiles = commit.ComponentFiles
                .Where(file => IsComponentApiFile(file.Path))
                .Where(file => file.AddedLines.Concat(file.RemovedLines).Any(IsParameterAffectingLine))
                .Select(file => file.Path)
                .ToList();
            if (apiFiles.Count == 0)
            {
                continue;
            }

            if (commit.AllPaths.Any(path => path.StartsWith(
                    "JsonDocumentation/", StringComparison.Ordinal)))
            {
                continue;
            }

            if (GrandfatheredViolations.Contains(commit.Sha))
            {
                continue;
            }

            findings.Add($"{commit.Sha}: {string.Join(", ", apiFiles)}");
        }

        return findings;
    }

    /// <summary>
    /// Builds the evaluator's view of one real commit from the object store. The diff is read
    /// with <c>--ignore-cr-at-eol</c>: a CRLF↔LF normalization rewrites every line of a file
    /// without changing a single byte of content, and git would show each line as removed+added;
    /// the flag hides exactly that carry-return-at-EOL class of churn, so a pure line-ending
    /// commit produces no diff at all. This is the ONLY churn handling — the evaluator itself
    /// compares no line texts, so a standalone <c>[Parameter]</c> attribute line moved between
    /// two properties (identical text on both sides) still flags.
    /// </summary>
    private static CommitChange ChangeForCommit(string sha)
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        var (namesExit, namesOut, namesErr) = FullCloneFactAttribute.RunGit(
            root, "show", "--format=", "--name-only", "--first-parent", sha);
        namesExit.Should().Be(0, $"git show --name-only must succeed for {sha} ({namesErr})");
        var paths = namesOut.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(path => path.Trim())
            .ToList();

        var files = new List<ChangedFile>();
        foreach (string componentPath in paths.Where(IsComponentApiFile))
        {
            var (diffExit, diffOut, diffErr) = FullCloneFactAttribute.RunGit(
                root, "show", "--format=", "--unified=0", "--ignore-cr-at-eol",
                "--first-parent", sha, "--", componentPath);
            diffExit.Should().Be(0, $"git show --unified=0 must succeed for {sha} ({diffErr})");
            var added = new List<string>();
            var removed = new List<string>();
            foreach (string line in diffOut.Split('\n'))
            {
                if (line.StartsWith("+++", StringComparison.Ordinal)
                    || line.StartsWith("---", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith('+'))
                {
                    added.Add(line[1..]);
                }
                else if (line.StartsWith('-'))
                {
                    removed.Add(line[1..]);
                }
            }

            files.Add(new ChangedFile(componentPath, added, removed));
        }

        return new CommitChange(sha, paths, files);
    }

    // ── Mutation cells (pure — no git) ─────────────────────────────────────────────

    /// <summary>The mutation the guard exists to catch: a parameter added with no JSON in the commit.</summary>
    [Fact]
    public void ParameterChange_WithoutJsonDocumentation_IsAFinding()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000001",
            [
                "src/Tempo.Blazor/Components/DataTable/TmDataTableColumn.razor",
                "tests/Tempo.Blazor.Tests/DataTable/XTests.cs",
            ],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Components/DataTable/TmDataTableColumn.razor",
                    ["/// <summary>Accessible name override.</summary>",
                     "[Parameter] public string? SortLabel { get; set; }"],
                    []),
            ]);

        Evaluate([commit]).Should().ContainSingle(
            finding => finding.Contains("deadbeef", StringComparison.Ordinal),
            "a [Parameter] addition with no JsonDocumentation/** path in the same commit "
            + "is exactly the hole N328 closes");
    }

    /// <summary>The control: the identical change carrying its documentation is clean.</summary>
    [Fact]
    public void ParameterChange_WithJsonDocumentation_IsClean()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000002",
            [
                "src/Tempo.Blazor/Components/DataTable/TmDataTableColumn.razor",
                "JsonDocumentation/Components/Data-Table/TmDataTableColumn.json",
            ],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Components/DataTable/TmDataTableColumn.razor",
                    ["[Parameter] public string? SortLabel { get; set; }"],
                    []),
            ]);

        Evaluate([commit]).Should().BeEmpty(
            "the same-commit rule is satisfied by ANY JsonDocumentation/** path — the freshness "
            + "guard measures per-parameter coverage on HEAD, this guard measures the boundary");
    }

    /// <summary>A markup-only component change must not flag — the guard is not a churn alarm.</summary>
    [Fact]
    public void MarkupOnlyChange_IsClean()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000003",
            ["src/Tempo.Blazor/Components/Buttons/TmButton.razor"],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Components/Buttons/TmButton.razor",
                    ["<span class=\"tm-btn-inner\">@ChildContent</span>"],
                    ["<span>@ChildContent</span>"]),
            ]);

        Evaluate([commit]).Should().BeEmpty();
    }

    /// <summary>
    /// The hole cancelling symmetric line pairs opened (review round 4, W1): moving a standalone
    /// <c>[Parameter]</c> attribute line from property A to property B is a diff of exactly
    /// <c>-    [Parameter]</c> / <c>+    [Parameter]</c> — identical text on both sides — and must
    /// STILL flag: the attribute changed which property it decorates. The evaluator is pure, so
    /// this cell pins the behaviour without git; the git layer keeps CRLF churn out via
    /// <c>--ignore-cr-at-eol</c> instead (measured on real history by
    /// <see cref="EndOfLineNormalizationCommits_AreClean"/>).
    /// </summary>
    [Fact]
    public void StandaloneParameterAttributeMove_BetweenProperties_IsAFinding()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000006",
            ["src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor.cs"],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor.cs",
                    [
                        "    /// <summary>Visual cap.</summary>",
                        "    [Parameter]",
                        "    public int MaxToasts { get; set; }",
                    ],
                    [
                        "    /// <summary>Visual cap.</summary>",
                        "    [Parameter]",
                        "    public int MaxVisible { get; set; }",
                    ]),
            ]);

        Evaluate([commit]).Should().ContainSingle(
            "the removed and added [Parameter] lines are textually identical, yet the attribute "
            + "moved from MaxVisible to MaxToasts — an API change with no JsonDocumentation/** "
            + "path in the commit is exactly the hole text-cancelling must never reopen");
    }

    /// <summary>
    /// The git layer's half of the W1 contract: commits whose whole content change is a CRLF/LF
    /// normalization produce NO diff under <c>--ignore-cr-at-eol</c>, so the evaluator has nothing
    /// to flag. <c>3f5d7f5f</c> restored LF in the round-3-touched promoted component files and
    /// <c>975a75be</c> stored CRLF in them — both touched component files with zero content delta.
    /// </summary>
    [FullCloneFact]
    public void EndOfLineNormalizationCommits_AreClean()
    {
        CommitChange lfRestore = ChangeForCommit(
            "3f5d7f5f3a98e16168544927b1157021004cd555");
        CommitChange crlfStore = ChangeForCommit(
            "975a75be9481d59dfeb945d554a27f558bdd71c6");

        using (new AssertionScope())
        {
            Evaluate([lfRestore]).Should().BeEmpty(
                "3f5d7f5f only restored LF line endings — --ignore-cr-at-eol leaves no diff, so "
                + "no parameter-affecting line can be read out of it");
            Evaluate([crlfStore]).Should().BeEmpty(
                "975a75be only stored CRLF line endings — the same flag hides the churn");
        }
    }

    /// <summary>
    /// A renamed parameter is remove+add declaration lines with no attribute delta — the property
    /// declaration arm must catch what the attribute arm cannot.
    /// </summary>
    [Fact]
    public void ParameterRename_IsCaughtByTheDeclarationArm()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000004",
            ["src/Tempo.Blazor/Components/Inputs/TmTextInput.razor.cs"],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Components/Inputs/TmTextInput.razor.cs",
                    ["    [Parameter] public string? PlaceholderText { get; set; }"],
                    ["    [Parameter] public string? Placeholder { get; set; }"]),
            ]);

        Evaluate([commit]).Should().ContainSingle();
    }

    /// <summary>A non-component public property change must not flag — scope is Tm* Components only.</summary>
    [Fact]
    public void PublicPropertyOutsideComponents_IsClean()
    {
        var commit = new CommitChange(
            "deadbeef00000000000000000000000000000005",
            ["src/Tempo.Blazor/Services/ThemeService.cs"],
            [
                new ChangedFile(
                    "src/Tempo.Blazor/Services/ThemeService.cs",
                    ["public string CurrentTheme { get; set; } = \"light\";"],
                    []),
            ]);

        Evaluate([commit]).Should().BeEmpty();
    }

    // ── Live measurement over the real object store ──────────────────────────────

    /// <summary>
    /// The calibration arm per the plan contract: <c>ee1ec3df</c> (SortLabel landed while
    /// <c>JsonDocumentation/**</c> stayed untouched — the commit regenerated only the repo-root
    /// aggregates, which is exactly the shape this guard refuses) must be detected, and
    /// <c>56fa6f04</c> (mass backfill carrying its JSON in the same commit) must read clean.
    /// </summary>
    [FullCloneFact]
    public void CalibrationCommits_ResolveExactlyAsRecorded()
    {
        CommitChange offending = ChangeForCommit(
            "ee1ec3df4a527d154562141584a57e31ecf1f8e6");
        CommitChange clean = ChangeForCommit(
            "56fa6f0426f99a15c6e6fae9ed1ffd72a6900b49");

        using (new AssertionScope())
        {
            Evaluate([offending]).Should().ContainSingle(
                finding => finding.StartsWith(offending.Sha, StringComparison.Ordinal),
                "ee1ec3df added SortLabel with JSON regenerated only at the repo root — "
                + "JsonDocumentation/** untouched is precisely the detected shape");
            Evaluate([clean]).Should().BeEmpty(
                "56fa6f04 backfilled JsonDocumentation/** in the same commit — clean by contract");
        }
    }

    /// <summary>
    /// The live sweep: every commit after the most recent <c>v*</c> tag is measured; when the tag
    /// is brand new and the range is empty, the last 50 commits stand in. A shallow checkout can
    /// neither name the tag boundary nor diff across it — under
    /// <c>TEMPO_REQUIRE_FULL_CLONE=1</c> the attribute's throw already defeats the skip, and this
    /// check converts that run into the named <c>unmeasurable:shallow-clone</c> failure instead
    /// of an incidental git error.
    /// </summary>
    [FullCloneFact]
    public void ComponentApiChangesSinceLastTag_CarryJsonDocumentation()
    {
        string root = ReleaseScriptInputReadTests.FindRepoRoot();
        var (_, shallow, _) = FullCloneFactAttribute.RunGit(
            root, "rev-parse", "--is-shallow-repository");
        shallow.Should().NotBe("true",
            "unmeasurable:shallow-clone — the tag boundary and its diffs need the full object "
            + "store (this arm only runs when TEMPO_REQUIRE_FULL_CLONE defeats the skip)");

        var (tagExit, tag, _) = FullCloneFactAttribute.RunGit(
            root, "describe", "--tags", "--abbrev=0", "--match", "v*");

        List<string> shas;
        if (tagExit == 0 && tag.Length > 0)
        {
            var (_, logOut, _) = FullCloneFactAttribute.RunGit(
                root, "log", "--format=%H", $"{tag}..HEAD");
            shas = [.. logOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())];
        }
        else
        {
            shas = [];
        }

        if (shas.Count == 0)
        {
            var (_, logOut, _) = FullCloneFactAttribute.RunGit(
                root, "log", "--format=%H", "-n", "50");
            shas = [.. logOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim())];
        }

        shas.Should().NotBeEmpty(
            "unmeasurable:no-commits — the sweep must measure commits, not an empty window");

        var findings = Evaluate(shas.Select(ChangeForCommit)).ToList();
        findings.Should().BeEmpty(
            "every commit that opened, closed or renamed a component [Parameter] since the last "
            + "tag must carry JsonDocumentation/** in the same commit — findings: {0}",
            string.Join(" | ", findings));
    }
}
