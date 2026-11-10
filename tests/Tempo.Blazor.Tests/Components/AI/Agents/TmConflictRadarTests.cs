using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 8 — <c>TmConflictRadar</c>: the agents' branches as one file × agent grid.
/// <para>
/// These facts pin the surface's promise: a header per agent carrying its name and branch, a row per changed file with
/// the change as an icon AND a word, the rows more than one agent claimed marked as conflicts and sorted on top, a side
/// choice per conflicting file (an agent's version or a manual merge) bound through <c>@bind-Resolutions</c>, the merge
/// action enabled only once every conflict is decided, the conflicts-only filter and collapsible directories, exactly
/// ONE polite live region, and the localized empty states.
/// </para>
/// </summary>
public class TmConflictRadarTests : LocalizationTestBase
{
    private static readonly AgentRef Ana = new("Ana", "a1") { Role = "Orchestrator" };
    private static readonly AgentRef Bo = new("Bo", "a2");
    private static readonly AgentRef Cy = new("Cy", "a3");

    private static ChangedPath Change(string path, ChangeKind kind, string? previous = null)
        => new(path, kind, previous);

    private static IReadOnlyList<AgentBranchChanges> Branches() =>
    [
        new(Ana, "feat/radar",
            [
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
                Change("src/AI/ConflictMatrix.cs", ChangeKind.Added),
                Change("README.md", ChangeKind.Modified),
            ]),
        new(Bo, "feat/lanes",
            [
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Modified),
                Change("docs/conflict-radar.md", ChangeKind.Added),
            ]),
        new(Cy, "feat/rename",
            [
                Change("README.md", ChangeKind.Deleted),
                Change("src/AI/TmConflictRadar.razor", ChangeKind.Renamed, "src/AI/Radar.razor"),
            ]),
    ];

    private IRenderedComponent<TmConflictRadar> RenderRadar(
        Action<ComponentParameterCollectionBuilder<TmConflictRadar>>? configure = null,
        IReadOnlyList<AgentBranchChanges>? branches = null)
        => Render<TmConflictRadar>(p =>
        {
            p.Add(c => c.Agents, branches ?? Branches());
            configure?.Invoke(p);
        });

    /// <summary>
    /// Re-renders the SAME instance with a partial parameter view — how a host applies (or refuses) a decision list.
    /// bUnit 2 dropped <c>SetParametersAndRender</c>, so the view goes through <c>SetParametersAsync</c>.
    /// </summary>
    private static Task RerenderAsync(
        IRenderedComponent<TmConflictRadar> cut,
        string name,
        object? value)
        => cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [name] = value })));

    private static string Text(IRenderedComponent<TmConflictRadar> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    // ── the table ────────────────────────────────────────────────────────────

    [Fact]
    public void TheSurfaceIsANativeTable_WithOneHeaderPerAgentCarryingItsBranch()
    {
        var cut = RenderRadar();

        var table = cut.Find("[data-testid='conflict-radar-grid']");
        table.TagName.Should().Be("TABLE");
        table.GetAttribute("aria-label").Should().Be("Conflict radar");
        table.HasAttribute("role").Should().BeFalse(
            "a native table already exposes table semantics; role=grid would promise a roving arrow-key model this " +
            "surface does not implement, so NVDA/JAWS would swallow the table reading commands");
        table.GetAttribute("aria-rowcount").Should().Be("10",
            "the count is COMPUTED from the same filtered, collapsed view the markup paints, so it cannot go stale " +
            "with the filter or a folded directory");
        table.GetAttribute("aria-colcount").Should().Be("4", "the file column plus one column per agent");

        foreach (var role in new[] { "grid", "rowgroup", "row", "gridcell", "columnheader", "rowheader" })
        {
            cut.FindAll($"[role='{role}']").Should().BeEmpty(
                "the {0} role is what a native table element already carries — re-declaring it is what breaks the " +
                "native reading mode", role);
        }

        var headers = cut.FindAll("[data-testid='radar-agent-header']");
        headers.Should().HaveCount(3);
        headers[0].TagName.Should().Be("TH");
        headers[0].GetAttribute("scope").Should().Be("col");
        headers[0].TextContent.Should().Contain("Ana").And.Contain("feat/radar");
        headers[1].TextContent.Should().Contain("Bo").And.Contain("feat/lanes");
        headers[2].TextContent.Should().Contain("Cy").And.Contain("feat/rename");
        headers[0].GetAttribute("data-agent-id").Should().Be("a1");
    }

    [Fact]
    public void TheDirectoryHeaderHeadsItsRowGroup_AndTheFileCellHeadsItsRow()
    {
        var cut = RenderRadar();

        var directories = cut.FindAll(".tm-ai-radar__directory-cell");
        directories.Should().HaveCount(3);
        foreach (var cell in directories)
        {
            cell.TagName.Should().Be("TH");
            cell.GetAttribute("scope").Should().Be(
                "rowgroup",
                "the cell heads the group of file rows below it, not a column group");
            cell.HasAttribute("role").Should().BeFalse();
            cell.GetAttribute("colspan").Should().Be("4", "the file column plus one per agent");
        }

        var files = cut.FindAll("[data-testid='radar-file']");
        files.Should().HaveCount(4);
        foreach (var file in files)
        {
            file.TagName.Should().Be("TH");
            file.GetAttribute("scope").Should().Be("row");
            file.Id.Should().NotBeNullOrEmpty("a resolution row has to be able to point at it");
        }
    }

    [Fact]
    public void AResolutionRowPointsAtTheFileRowItSettles()
    {
        var cut = RenderRadar();
        var rows = cut.FindAll("tbody tr").ToList();

        var resolutionRows = 0;
        for (var index = 1; index < rows.Count; index++)
        {
            var row = rows[index];
            if (!row.ClassList.Contains("tm-ai-radar__row--resolution"))
            {
                continue;
            }

            resolutionRows++;
            var header = rows[index - 1].QuerySelector("[data-testid='radar-file']");
            header.Should().NotBeNull("a resolution row directly follows the file row it settles");
            row.QuerySelector("[headers]").Should().NotBeNull("an anonymous row loses the file it belongs to");
            row.QuerySelector("[headers]")!.GetAttribute("headers").Should().Be(
                header!.Id,
                "the cell names the row header above it, so a reader hears the file");
        }

        resolutionRows.Should().Be(2, "only a conflicting file grows a side choice");
    }

    [Fact]
    public void ARowPerChangedFile_CarriesItsDirectoryAndOwnName()
    {
        var cut = RenderRadar();
        var rows = cut.FindAll("[data-testid='radar-row']");

        rows.Should().HaveCount(4);
        rows.Select(row => row.GetAttribute("data-path")).Should().BeEquivalentTo(
            "README.md",
            "docs/conflict-radar.md",
            "src/AI/ConflictMatrix.cs",
            "src/AI/TmConflictRadar.razor");
    }

    [Fact]
    public void ConflictingRowsComeFirst_AndAreMarkedWithAWordNotJustAColour()
    {
        var cut = RenderRadar();
        var rows = cut.FindAll("[data-testid='radar-row']");

        rows[0].GetAttribute("data-conflict").Should().Be("true");
        rows[1].GetAttribute("data-conflict").Should().Be("true");
        rows[2].GetAttribute("data-conflict").Should().Be("false");
        rows[0].ClassList.Should().Contain("tm-ai-radar__row--conflict");
        rows[0].TextContent.Should().Contain("Conflict");
        rows[2].QuerySelector("[data-testid='radar-row-conflict']").Should().BeNull();
    }

    [Fact]
    public void ACellNamesTheChangeWithAnIconAndAWord()
    {
        var cut = RenderRadar();
        var row = cut.FindAll("[data-testid='radar-row']")
            .Single(element => element.GetAttribute("data-path") == "src/AI/TmConflictRadar.razor");

        var cells = row.QuerySelectorAll("[data-testid='radar-cell']");
        cells.Should().HaveCount(3);
        cells[0].GetAttribute("data-agent-id").Should().Be("a1");
        cells[0].GetAttribute("data-kind").Should().Be("modified");
        cells[0].TextContent.Should().Contain("Modified");
        cells[0].QuerySelector("svg, [data-testid='radar-cell-icon']").Should().NotBeNull(
            "the kind carries an icon as well as a word");
        cells[2].GetAttribute("data-kind").Should().Be("renamed");
        cells[2].TextContent.Should().Contain("Renamed").And.Contain("src/AI/Radar.razor");
    }

    [Fact]
    public void AnUntouchedFileInAColumn_IsAnEmptyCellRatherThanAChange()
    {
        var cut = RenderRadar();
        var row = cut.FindAll("[data-testid='radar-row']")
            .Single(element => element.GetAttribute("data-path") == "docs/conflict-radar.md");

        var cells = row.QuerySelectorAll("[data-testid='radar-cell']");
        cells.Should().HaveCount(3);
        cells[0].GetAttribute("data-kind").Should().Be("none");
        cells[0].TextContent.Should().Contain("No change");
        cells[1].GetAttribute("data-kind").Should().Be("added");
    }

    [Fact]
    public void TheSummaryNamesTheFilesTheAgentsAndTheConflicts()
    {
        var cut = RenderRadar();

        Text(cut, "radar-summary").Should().Be("Files: 4 · Agents: 3 · Conflicts: 2");
    }

    // ── directories ──────────────────────────────────────────────────────────

    [Fact]
    public void RowsAreGroupedByDirectory_WithARootGroupNamedAsSuch()
    {
        var cut = RenderRadar();
        var groups = cut.FindAll("[data-testid='radar-directory']");

        groups.Select(group => group.GetAttribute("data-directory")).Should().Equal(
            string.Empty, "src/AI", "docs");
        groups[0].TextContent.Should().Contain("Repository root");
        groups[1].TextContent.Should().Contain("src/AI").And.Contain("1 conflict");
    }

    [Fact]
    public void ADirectoryCollapsesAndExpands_AndSaysSo()
    {
        var cut = RenderRadar();
        var toggle = cut.FindAll("[data-testid='radar-directory-toggle']")[1];

        toggle.GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(4);

        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();

        var collapsed = cut.FindAll("[data-testid='radar-directory-toggle']")[1];
        collapsed.GetAttribute("aria-expanded").Should().Be("false");
        cut.FindAll("[data-testid='radar-row']").Select(row => row.GetAttribute("data-path"))
            .Should().NotContain("src/AI/ConflictMatrix.cs")
            .And.NotContain("src/AI/TmConflictRadar.razor");
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(2);
        cut.Find("[aria-live]").TextContent.Should().Contain("src/AI collapsed");

        cut.FindAll("[data-testid='radar-directory-toggle']")[1].Click();
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(4);
        cut.Find("[aria-live]").TextContent.Should().Contain("src/AI expanded");
    }

    // ── the conflicts-only filter ────────────────────────────────────────────

    [Fact]
    public void TheConflictsOnlyFilter_HidesEveryNonConflictingFileAndDirectory()
    {
        var cut = RenderRadar();
        var toggle = cut.Find("[data-testid='radar-conflicts-only']");

        toggle.TextContent.Should().Contain("Only conflicts");

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(true);

        cut.FindAll("[data-testid='radar-row']").Select(row => row.GetAttribute("data-path")).Should().Equal(
            "README.md", "src/AI/TmConflictRadar.razor");
        cut.FindAll("[data-testid='radar-directory']").Select(group => group.GetAttribute("data-directory"))
            .Should().Equal(string.Empty, "src/AI");
        cut.Find("[aria-live]").TextContent.Should().Contain("Showing only conflicts");

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(false);
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(4);
        cut.Find("[aria-live]").TextContent.Should().Contain("Showing every changed file");
    }

    [Fact]
    public async Task TheConflictsOnlyFilterIsTwoWay_AndAHostEchoKeepsTheReaderPick()
    {
        bool? reported = null;
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.ConflictOnly, false);
            p.Add(c => c.ConflictOnlyChanged, value => reported = value);
        });

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(true);

        reported.Should().BeTrue();
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(2);

        // A host that echoes the pick re-renders the same value: the reader's filter must not flip back.
        await RerenderAsync(cut, nameof(TmConflictRadar.ConflictOnly), true);
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(2);
        cut.Find("[data-testid='radar-conflicts-only']").ClassList.Should().Contain("tm-toggle-checked");

        // A host that MOVES the parameter back takes the surface with it.
        await RerenderAsync(cut, nameof(TmConflictRadar.ConflictOnly), false);
        cut.FindAll("[data-testid='radar-row']").Should().HaveCount(4);
    }

    // ── the side choice ──────────────────────────────────────────────────────

    [Fact]
    public void EveryConflictingFileOffersARadiogroupOfTheAgentsAndAManualMerge()
    {
        var cut = RenderRadar();
        var groups = cut.FindAll("[role='radiogroup']");

        groups.Should().HaveCount(2, "only a conflicting file needs a decision");
        groups[0].GetAttribute("aria-label").Should().Be("Resolve README.md");

        var options = groups[0].QuerySelectorAll("input[type='radio']");
        options.Should().HaveCount(3, "Ana, Cy and the manual merge — Bo never touched the file");
        options[0].GetAttribute("data-side").Should().Be("a1");
        options[1].GetAttribute("data-side").Should().Be("a3");
        options[2].GetAttribute("data-side").Should().Be("manual");
        groups[0].TextContent.Should().Contain("Ana").And.Contain("Cy").And.Contain("Merge by hand");
        groups[0].TextContent.Should().NotContain("Bo");
    }

    [Fact]
    public void ARadiogroupIsOneNativeGroupPerFile_SoTheArrowKeysWork()
    {
        var cut = RenderRadar();
        var groups = cut.FindAll("[role='radiogroup']");

        var first = groups[0].QuerySelectorAll("input[type='radio']").Select(input => input.GetAttribute("name"));
        var second = groups[1].QuerySelectorAll("input[type='radio']").Select(input => input.GetAttribute("name"));

        first.Distinct().Should().HaveCount(1, "one name per file makes the browser group the radios");
        first.First().Should().NotBe(second.First(), "two files must not share one radio group");
    }

    [Fact]
    public void PickingAnAgentsSide_RaisesTheResolutionsWithThatAgent()
    {
        IReadOnlyList<ConflictResolution>? reported = null;
        var cut = RenderRadar(p =>
            p.Add(c => c.ResolutionsChanged, value => reported = value));

        var radio = cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a1");
        radio.Change(true);

        reported.Should().NotBeNull();
        reported!.Should().HaveCount(1);
        reported[0].Path.Should().Be("README.md");
        reported[0].Kind.Should().Be(ConflictResolutionKind.Agent);
        reported[0].AgentId.Should().Be("a1");
        cut.Find("[aria-live]").TextContent.Should().Contain("README.md resolved");
    }

    [Fact]
    public void PickingTheManualMerge_RaisesAManualResolution()
    {
        IReadOnlyList<ConflictResolution>? reported = null;
        var cut = RenderRadar(p =>
            p.Add(c => c.ResolutionsChanged, value => reported = value));

        var manual = cut.FindAll("[role='radiogroup']")[1]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "manual");
        manual.Change(true);

        reported!.Should().HaveCount(1);
        reported[0].Path.Should().Be("src/AI/TmConflictRadar.razor");
        reported[0].IsManual.Should().BeTrue();
        reported[0].AgentId.Should().BeNull();
    }

    [Fact]
    public void TheHostsResolutions_CheckTheMatchingRadio()
    {
        var cut = RenderRadar(p =>
            p.Add(c => c.Resolutions, new List<ConflictResolution>
            {
                new("README.md", ConflictResolutionKind.Agent, "a3"),
                new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Manual),
            }));

        var first = cut.FindAll("[role='radiogroup']")[0].QuerySelectorAll("input[type='radio']");
        first.Single(input => input.GetAttribute("data-side") == "a3").HasAttribute("checked").Should().BeTrue();
        first.Single(input => input.GetAttribute("data-side") == "a1").HasAttribute("checked").Should().BeFalse();

        var second = cut.FindAll("[role='radiogroup']")[1].QuerySelectorAll("input[type='radio']");
        second.Single(input => input.GetAttribute("data-side") == "manual").HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public async Task AReaderPickSurvivesTheHostReRenderingTheSameResolutions()
    {
        var hostResolutions = new List<ConflictResolution>();
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.Resolutions, hostResolutions);
            p.Add(c => c.ResolutionsChanged, value => hostResolutions = [.. value]);
        });

        cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a1")
            .Change(true);

        // A host that echoes the SAME list instance cannot undo the pick.
        await RerenderAsync(cut, nameof(TmConflictRadar.Resolutions), hostResolutions);
        cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a1")
            .HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public void ChangingTheSideReplacesThePreviousDecisionForTheSameFile()
    {
        IReadOnlyList<ConflictResolution>? reported = null;
        var cut = RenderRadar(p => p.Add(c => c.ResolutionsChanged, value => reported = value));

        cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a1")
            .Change(true);
        // The re-render replaced the tree, so the group is looked up again before the second pick.
        cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a3")
            .Change(true);

        reported!.Should().HaveCount(1, "one decision per file");
        reported[0].AgentId.Should().Be("a3");
    }

    // ── the merge action ─────────────────────────────────────────────────────

    [Fact]
    public void MergeIsDisabledUntilEveryConflictIsDecided_AndTheReasonIsStated()
    {
        var cut = RenderRadar();
        var merge = cut.Find("[data-testid='radar-merge']");

        merge.HasAttribute("disabled").Should().BeTrue();
        merge.GetAttribute("aria-describedby").Should().Be(cut.Find("[data-testid='radar-merge-hint']").Id);
        Text(cut, "radar-merge-hint").Should().Contain("2").And.Contain("unresolved");

        cut.FindAll("[role='radiogroup']")[0]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "a1")
            .Change(true);

        cut.Find("[data-testid='radar-merge']").HasAttribute("disabled").Should().BeTrue();
        Text(cut, "radar-merge-hint").Should().Contain("1").And.Contain("unresolved");

        cut.FindAll("[role='radiogroup']")[1]
            .QuerySelectorAll("input[type='radio']")
            .Single(input => input.GetAttribute("data-side") == "manual")
            .Change(true);

        cut.Find("[data-testid='radar-merge']").HasAttribute("disabled").Should().BeFalse();
        Text(cut, "radar-merge-hint").Should().Be("Every conflict is resolved. Merge is ready.");
    }

    [Fact]
    public void MergeReportsEveryFileAndTheAcceptedSides()
    {
        ConflictMergeRequest? reported = null;
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.Resolutions, new List<ConflictResolution>
            {
                new("README.md", ConflictResolutionKind.Agent, "a3"),
                new("src/AI/TmConflictRadar.razor", ConflictResolutionKind.Manual),
            });
            p.Add(c => c.OnMerge, request => reported = request);
        });

        cut.Find("[data-testid='radar-merge']").Click();

        reported.Should().NotBeNull();
        reported!.Files.Should().HaveCount(4);
        reported.Files.Should().Contain("docs/conflict-radar.md");
        reported.Resolutions.Should().HaveCount(2);
        reported.ResolutionOf("README.md")!.AgentId.Should().Be("a3");
        cut.Find("[aria-live]").TextContent.Should().Contain("Merged");
    }

    [Fact]
    public void MergeIsEnabledWhenTheAgentsDoNotConflict_AndReportsNoSides()
    {
        ConflictMergeRequest? reported = null;
        var cut = RenderRadar(
            p => p.Add(c => c.OnMerge, request => reported = request),
            [
                new(Ana, "feat/a", [Change("a.cs", ChangeKind.Added)]),
                new(Bo, "feat/b", [Change("b.cs", ChangeKind.Modified)]),
            ]);

        cut.Find("[data-testid='radar-merge']").HasAttribute("disabled").Should().BeFalse();
        cut.Find("[data-testid='radar-merge']").Click();

        reported!.Files.Should().Equal("a.cs", "b.cs");
        reported.Resolutions.Should().BeEmpty();
    }

    // ── edge cases and accessibility ─────────────────────────────────────────

    [Fact]
    public void NoChangesAtAll_RendersTheLocalizedEmptyState()
    {
        var cut = RenderRadar(null, []);

        Text(cut, "radar-empty").Should().Be("No agent changed any file.");
        cut.FindAll("[data-testid='radar-row']").Should().BeEmpty();
        cut.Find("[data-testid='radar-merge']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TheFilterThatMatchesNothing_ExplainsItself()
    {
        var cut = RenderRadar(
            null,
            [
                new(Ana, "feat/a", [Change("a.cs", ChangeKind.Added)]),
            ]);

        cut.Find("[data-testid='radar-conflicts-only'] input.tm-toggle-input").Change(true);

        Text(cut, "radar-empty").Should().Be("No conflicts between the agents.");
    }

    [Fact]
    public void TheSurfaceOwnsExactlyOnePoliteLiveRegion()
    {
        var cut = RenderRadar();

        var regions = cut.FindAll("[aria-live]");
        regions.Should().HaveCount(1);
        regions[0].GetAttribute("aria-live").Should().Be("polite");
        regions[0].ClassList.Should().Contain("tm-sr-only");
    }

    [Fact]
    public void AnUnnamedBranchAndARootFileFallBackToLocalizedWords()
    {
        var cut = RenderRadar(
            null,
            [
                new(Ana, null!, [Change("a.cs", ChangeKind.Added)]),
            ]);

        cut.FindAll("[data-testid='radar-agent-header']")[0].TextContent.Should().Contain("No branch");
        cut.FindAll("[data-testid='radar-directory']")[0].TextContent.Should().Contain("Repository root");
    }

    [Fact]
    public void TheTitleAndHeadingLevelAreTheHosts()
    {
        var cut = RenderRadar(p =>
        {
            p.Add(c => c.Title, "Branch overlap");
            p.Add(c => c.HeadingLevel, 2);
        });

        cut.Find("h2").TextContent.Should().Be("Branch overlap");
    }

    [Fact]
    public void TheInstructionsAreReachableFromTheTable()
    {
        var cut = RenderRadar();
        var instructions = cut.Find("[data-testid='radar-instructions']");

        cut.Find("[data-testid='conflict-radar-grid']").GetAttribute("aria-describedby").Should().Be(instructions.Id);
        instructions.TextContent.Should().Contain("Pick the version to keep");
    }

    [Fact]
    public void EveryChangedFileOfADeletedSideStillReadsItsKind()
    {
        var cut = RenderRadar(
            null,
            [
                new(Ana, "feat/a", [Change("a.cs", ChangeKind.Deleted)]),
                new(Bo, "feat/b", [Change("a.cs", ChangeKind.Modified)]),
            ]);

        var cells = cut.FindAll("[data-testid='radar-row']")[0].QuerySelectorAll("[data-testid='radar-cell']");
        cells[0].TextContent.Should().Contain("Deleted");
        cells[1].TextContent.Should().Contain("Modified");
    }
}
