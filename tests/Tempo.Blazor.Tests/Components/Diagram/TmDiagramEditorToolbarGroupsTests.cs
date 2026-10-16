using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// Freezes the public toolbar contract of <see cref="TmDiagramEditor"/> (DiagramEditor redesign,
/// phase 1): every toolbar group carries a stable <c>data-tm-group</c> attribute from a fixed set,
/// the <see cref="TmDiagramEditor.ToolbarPreset"/>/<see cref="TmDiagramEditor.ToolbarGroups"/>
/// parameters decide group visibility in C# (never CSS), and the <c>HeaderStart</c> /
/// <c>HeaderActions</c> slots render host content. The contract is public API and must survive the
/// phase-4 toolbar regrouping unchanged.
/// </summary>
public class TmDiagramEditorToolbarGroupsTests : LocalizationTestBase
{
    /// <summary>The frozen <c>data-tm-group</c> contract, in the classic (Full preset) DOM order.</summary>
    public static readonly string[] ContractGroups =
    [
        "history", "clipboard", "tools", "format", "insert", "arrange", "search", "panels",
        "zoom", "view", "canvas-size", "layout", "export", "templates", "import", "readonly",
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
    public void FullPreset_RendersEveryGroupInTheClassicOrder()
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

        // The viewer keeps the zoom readout and the fit-to-view command.
        cut.Find(".tm-diagram-editor__toolbar [data-tm-group='zoom'] .tm-diagram-editor__zoom-label")
            .Should().NotBeNull();
    }

    [Fact]
    public void FullPreset_IsTheDefault_AndKeepsTheReadOnlyToggle()
    {
        using var cut = Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

        cut.Instance.ToolbarPreset.Should().Be(DiagramToolbarPreset.Full);
        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group='readonly']").Should().HaveCount(1);
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

        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group='templates']").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group='import']").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group='readonly']").Should().BeEmpty();
        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group='zoom']").Should().HaveCount(1);
    }

    [Fact]
    public void HeaderStart_RendersBeforeTheDocumentTitle()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.HeaderStart, "<span class=\"hdr-start-sentinel\">Start</span>"));

        var sentinel = cut.Find(".tm-diagram-editor__header-start .hdr-start-sentinel");
        sentinel.TextContent.Should().Be("Start");

        var firstRow = cut.FindAll(".tm-diagram-editor__toolbar-row")[0];
        var childClasses = firstRow.Children.Select(c => c.ClassName ?? string.Empty).ToList();
        childClasses.FindIndex(c => c.Contains("tm-diagram-editor__header-start"))
            .Should().BeLessThan(childClasses.FindIndex(c => c.Contains("tm-diagram-editor__title")),
                "HeaderStart opens the first toolbar row, before the built-in title input");
    }

    [Fact]
    public void HeaderActions_RenderAfterTheBuiltInGroups()
    {
        using var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.ReadOnly, false)
            .Add(e => e.HeaderActions, "<span class=\"hdr-actions-sentinel\">Actions</span>"));

        cut.Find(".tm-diagram-editor__header-actions .hdr-actions-sentinel").TextContent
            .Should().Be("Actions");

        var lastRow = cut.FindAll(".tm-diagram-editor__toolbar-row")[^1];
        var childClasses = lastRow.Children.Select(c => c.ClassName ?? string.Empty).ToList();
        childClasses.FindIndex(c => c.Contains("tm-diagram-editor__header-actions"))
            .Should().BeGreaterThan(childClasses.FindIndex(c => c.Contains("readonly")),
                "HeaderActions close the toolbar, after the built-in groups");
    }

    private IRenderedComponent<TmDiagramEditor> RenderDefaultEditor() =>
        Render<TmDiagramEditor>(p => p.Add(e => e.ReadOnly, false));

    private static List<string> ToolbarGroupNames(IRenderedComponent<TmDiagramEditor> cut) =>
        cut.FindAll(".tm-diagram-editor__toolbar [data-tm-group]")
            .Select(g => g.GetAttribute("data-tm-group")!)
            .ToList();
}
