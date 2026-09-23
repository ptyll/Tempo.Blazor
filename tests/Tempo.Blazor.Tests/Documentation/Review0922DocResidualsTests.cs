using FluentAssertions;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Fail-closed guards for the seven documentation residuals closed in phase 20C (review
/// 2026-09-22: N143, N144, N154, N156, N157, N160, N171). Each test pins the exact explanatory
/// wording written into the owning file, so a later refactor that drops the clause fails here
/// instead of silently re-opening the finding. Substrings mirror the text actually written —
/// keep them in sync when the wording changes on purpose.
/// </summary>
public class Review0922DocResidualsTests
{
    private static string Read(params string[] parts) => File.ReadAllText(
        Path.Combine(ThemeCss.RepositoryRoot().FullName, Path.Combine(parts)));

    /// <summary>
    /// N143: the <c>#if DEBUG</c> throw in <c>SortButtonHeaderText</c> is a dev-loop net only —
    /// the doc must say a Release package never compiles it in and the positional fallback
    /// applies in published apps.
    /// </summary>
    [Fact]
    public void N143_SortButtonHeaderText_Documents_DebugOnlyThrow()
        => Read("src", "Tempo.Blazor", "Components", "DataTable", "TmDataTable.razor.cs")
            .Should().Contain("a Release NuGet package never compiles it in");

    /// <summary>
    /// N144: <c>TmStatCard.IsValidCssColor</c> accepts a deliberately narrower grammar than CSS
    /// Color 4 — the exclusion of hwb()/lab()/lch()/oklab() must be documented as a decision,
    /// not an oversight.
    /// </summary>
    [Fact]
    public void N144_SubValueColor_Documents_ExcludedColorFunctions()
        => Read("src", "Tempo.Blazor", "Components", "DataDisplay", "TmStatCard.razor.cs")
            .Should().Contain("deliberate, documented exclusion");

    /// <summary>
    /// N154: the getting-started docs must state the browser floor for <c>rgb(from …)</c> and
    /// <c>:root:has()</c> — including that a too-old browser loses the focus ring entirely.
    /// </summary>
    [Fact]
    public void N154_GettingStarted_Documents_BrowserFloor()
        => Read("JsonDocumentation", "gettingStarted.json").Should().Contain("Baseline 2024");

    /// <summary>
    /// N156: COMPONENTS.md param tables must list <c>TriggerId</c> (TmDateTimePicker) and
    /// <c>InputId</c> (TmTimeInput) — both exist in source and were missing from the prose.
    /// </summary>
    [Fact]
    public void N156_Components_Documents_TriggerIdAndInputId()
    {
        var doc = Read("COMPONENTS.md");
        doc.Should().Contain("`TriggerId`");
        doc.Should().Contain("`InputId`");
    }

    /// <summary>
    /// N157: the ShowResultSummary paragraph must spell out the two corner combinations
    /// (placement=None prints nothing; placement=Pagination keeps the pager's own ShowInfo).
    /// </summary>
    [Fact]
    public void N157_Components_Documents_ShowResultSummaryCornerCases()
    {
        var doc = Read("COMPONENTS.md");
        doc.Should().Contain("ShowResultSummary=\"false\"");
        doc.Should().Contain("ShowInfo");
    }

    /// <summary>
    /// N160: the COMPONENTS.md table of contents must list the three sections that existed in
    /// the body but were missing from the TOC.
    /// </summary>
    [Fact]
    public void N160_Components_TocListsAllThreeMissingSections()
    {
        var doc = Read("COMPONENTS.md");
        doc.Should().Contain("[Přehled enumů](#přehled-enumů)");
        doc.Should().Contain("[Design Token System](#design-token-system)");
        doc.Should().Contain("[Diagram Editor](#diagram-editor)");
    }

    /// <summary>
    /// N171: the consumer _Imports.razor sample in AGENT.md must include the Overlay namespace
    /// — TmOverlayPanel, OverlayPlacement and OverlayAlign (also used by TmDropdown) live there.
    /// </summary>
    [Fact]
    public void N171_AgentMd_Documents_OverlayUsing()
        => Read("src", "Tempo.Blazor", "AGENT.md").Should().Contain("@using Tempo.Blazor.Components.Overlay");
}
