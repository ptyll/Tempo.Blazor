using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Freezes the public toolbar contract of <see cref="TmDiagramEditor"/>: every toolbar group
/// carries a stable <c>data-tm-group</c> attribute from a fixed set, the
/// <see cref="TmDiagramEditor.ToolbarPreset"/>/<see cref="TmDiagramEditor.ToolbarGroups"/>
/// parameters decide group visibility in C# (never CSS), and the <c>HeaderStart</c> /
/// <c>HeaderActions</c> slots render host content.
/// </summary>
/// <remarks>
/// Phase 4 (desktop restructure) regrouped the toolbar: the command groups render in ONE
/// TmToolbar row, the search and panel toggles moved into the header, the view/canvas-size
/// groups into the header "⋯" menu and the zoom/read-only groups into the status bar. The
/// group NAMES, the flags and the <see cref="DiagramToolbarGroups.All"/> meaning are unchanged
/// public API; only the frozen ORDER below reflects the new desktop DOM order.
/// </remarks>
public class TmDiagramEditorToolbarGroupsTests : LocalizationTestBase
{
    /// <summary>The frozen <c>data-tm-group</c> contract, in the desktop (Full preset) DOM order:
    /// header groups, view-settings menu groups, command-row groups, status-bar groups.</summary>
    public static readonly string[] ContractGroups =
    [
        "search", "panels", "view", "canvas-size",
        "history", "clipboard", "tools", "format", "insert", "arrange", "layout", "export", "import", "templates",
        "zoom", "readonly",
    ];

    public TmDiagramEditorToolbarGroupsTests()
    {
        Services.GetRequiredService<DiagramStencilRegistry>()
            .RegisterProvider(new BuiltInDiagramStencilProvider());
    }

    [Fact]
    public void EveryToolbarGroup_CarriesStableDataTmGroupAttribute()
    {
        using var cut = RenderDefaultEditor();

        var names = ToolbarGroupNames(cut);
        names.Should().NotBeEmpty();
        names.Should().OnlyContain(name => ContractGroups.Contains(name));
    }

    [Fact]
    public void FullPreset_RendersEveryGroupInTheDesktopOrder()
    {
        using var cut = RenderDefaultEditor();

        ToolbarGroupNames(cut).Should().Equal(ContractGroups);
    }

    [Fact]
    public void ViewerPreset_RendersOnlyZoomViewSearchAndExport()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, true)
            .Add(e => e.ToolbarPreset, DiagramToolbarPreset.Viewer));

        var names = ToolbarGroupNames(cut);
        names.Should().BeEquivalentTo(["zoom", "view", "search", "export"]);
        names.Should().OnlyHaveUniqueItems("each group renders exactly once");

        // The viewer keeps the zoom readout — in the status bar since phase 4 — and the
        // fit-to-view command (Modeling E2E contract: .tm-diagram-editor__zoom-label).
        cut.Find(".tm-diagram-editor__statusbar [data-tm-group='zoom'] .tm-diagram-editor__zoom-label")
            .Should().NotBeNull();
    }

    [Fact]
    public void FullPreset_IsTheDefault_AndKeepsTheReadOnlyToggle()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        cut.Instance.ToolbarPreset.Should().Be(DiagramToolbarPreset.Full);
        cut.FindAll(".tm-diagram-editor__statusbar [data-tm-group='readonly']").Should().HaveCount(1);
    }

    [Fact]
    public void ToolbarGroups_FlagsOverrideThePreset()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, true)
            .Add(e => e.ToolbarPreset, DiagramToolbarPreset.Viewer)
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.Insert | DiagramToolbarGroups.Zoom));

        ToolbarGroupNames(cut).Should().BeEquivalentTo(["insert", "zoom"]);
    }

    [Fact]
    public void MissingToolbarGroup_HidesItsCommand()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.ToolbarGroups, DiagramToolbarGroups.Zoom));

        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group='templates']").Should().BeEmpty();
        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group='import']").Should().BeEmpty();
        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group='readonly']").Should().BeEmpty();
        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group='zoom']").Should().HaveCount(1);
    }

    private IRenderedComponent<TmDiagramEditor> RenderDefaultEditor() =>
        Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

    private static List<string> ToolbarGroupNames(IRenderedComponent<TmDiagramEditor> cut) =>
        cut.FindAll("[data-testid='diagram-editor'] [data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group")!)
            .ToList();
}
