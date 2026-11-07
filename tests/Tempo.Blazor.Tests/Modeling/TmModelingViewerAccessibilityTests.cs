using Bunit;
using Microsoft.AspNetCore.Components.Web;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 6 accessibility DoD: the viewer's text alternative is a real, keyboard-driven list; every
/// overlay has a text equivalent; one polite live region announces selection and overlay changes;
/// the print stylesheet drops the zoom chrome and keeps the list opt-in.
/// </summary>
public sealed class TmModelingViewerAccessibilityTests : LocalizationTestBase
{
    public TmModelingViewerAccessibilityTests()
    {
        Services.AddTempoBlazorModeling();
    }

    [Fact]
    public void Every_row_names_its_type_and_name_and_points_at_the_hint_and_description()
    {
        using var cut = Render();

        var row = cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']");
        row.GetAttribute("aria-label").Should().Contain("User task").And.Contain("Validate order");

        var describedBy = row.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrWhiteSpace();
        var ids = describedBy!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        ids.Should().HaveCount(2, "the row points at the shared hint and its own description");
        cut.Find($"#{ids[0]}").TextContent.Should().Contain("arrow keys");
        cut.Find($"#{ids[1]}").TextContent.Should().BeEmpty("a plain drawn row has no extra state");
    }

    [Fact]
    public void Overlay_rows_describe_their_state_for_screen_readers()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var row = cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']");
        var describedBy = row.GetAttribute("aria-describedby")!;
        var descriptionId = describedBy.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
        cut.Find($"#{descriptionId}").TextContent.Should().Contain("Changed");
        row.TextContent.Should().Contain("Changed", "the state is also visible, not only in the description");
    }

    [Fact]
    public void Removed_rows_are_listed_but_not_interactive()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var removed = cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true']");
        removed.Should().NotBeEmpty();
        removed.All(row => row.QuerySelector("button") is null).Should().BeTrue(
            "a removed row is listed for the text alternative but has nothing to activate");
        removed.Select(row => row.TextContent).Should().Contain(text => text.Contains("Removed"));
    }

    [Fact]
    public void Element_list_has_a_roving_tab_stop_and_arrow_home_end_movement()
    {
        using var cut = Render();

        var rows = cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]");
        rows[0].GetAttribute("tabindex").Should().Be("0");
        rows[1].GetAttribute("tabindex").Should().Be("-1");

        rows[0].KeyDown("ArrowDown");
        cut.Find("[data-testid='modeling-viewer-element-list'] ul").GetAttribute("data-focused-index")
            .Should().Be("1");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[1]
            .GetAttribute("tabindex").Should().Be("0");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[0]
            .GetAttribute("tabindex").Should().Be("-1");

        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[1]
            .KeyDown("End");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[^1]
            .GetAttribute("tabindex").Should().Be("0");

        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[^1]
            .KeyDown("Home");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] [data-element-id]")[0]
            .GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void Selection_is_announced_in_the_polite_live_region()
    {
        using var cut = Render();

        var live = cut.Find("[data-testid='modeling-viewer-live-region']");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.GetAttribute("aria-atomic").Should().Be("true");

        cut.Find("[data-testid='modeling-viewer-element-list'] [data-element-id='e-review']").Click();

        cut.Find("[data-testid='modeling-viewer-live-region']").TextContent
            .Should().Contain("Selected").And.Contain("Validate order");
    }

    [Fact]
    public void Overlay_changes_are_announced_once()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        cut.Find("[data-testid='modeling-viewer-live-region']").TextContent
            .Should().Contain("Diff overlay").And.Contain("3 added").And.Contain("3 removed");
    }

    [Fact]
    public void Removed_rows_are_not_tab_stops_and_End_lands_on_the_last_drawn_row()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        // The diff scenario appends three removed rows AFTER the drawn ones; they carry no button, so
        // the roving tab stop must never leave the drawn rows (review fix, C1).
        var drawnRows = cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]");
        drawnRows.Should().HaveCount(7);

        drawnRows[0].KeyDown("End");
        drawnRows = cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]");
        drawnRows[^1].GetAttribute("tabindex").Should().Be("0", "End lands on the last DRAWN row");
        drawnRows.Count(row => row.GetAttribute("tabindex") == "0").Should().Be(1,
            "exactly one row owns the tab stop");

        drawnRows[^1].KeyDown("ArrowDown");
        cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]")[^1]
            .GetAttribute("tabindex").Should().Be("0", "ArrowDown at the end stays on the last drawn row");

        cut.FindAll("[data-testid='modeling-viewer-element-list'] li[data-removed='true'] button")
            .Should().BeEmpty("a removed row has nothing to activate");
    }

    [Fact]
    public void A_selected_removed_row_never_steals_the_tab_stop()
    {
        var overlay = ModelingViewerTestModels.CreateDiffOverlay();
        overlay.RemovedElementIds = ["e-approve"];

        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, overlay)
            .Add(p => p.SelectedElementId, "e-approve")
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        var rows = cut.FindAll("[data-testid='modeling-viewer-element-list'] button[data-list-index]");
        rows.Count(row => row.GetAttribute("tabindex") == "0").Should().Be(1);
        rows[0].GetAttribute("tabindex").Should().Be("0",
            "a selected id that is not a drawn row leaves the roving index at the top");
    }

    [Fact]
    public void Overlay_legends_pair_every_swatch_with_a_word()
    {
        using var cut = Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateDiffModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.DiffOverlay, ModelingViewerTestModels.CreateDiffOverlay())
            .Add(p => p.Heatmap, ModelingViewerTestModels.CreateHeatmap())
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

        foreach (var swatch in cut.FindAll(".tm-modeling-viewer__swatch"))
        {
            swatch.GetAttribute("aria-hidden").Should().Be("true", "the colour is decorative; the word carries it");
        }

        cut.Find("[data-testid='modeling-viewer-diff-legend']").TextContent.Should().Contain("Added");
        cut.Find("[data-testid='modeling-viewer-heatmap-legend']").TextContent.Should()
            .Contain("Low").And.Contain("Moderate").And.Contain("Elevated").And.Contain("High");
    }

    [Fact]
    public void Default_region_label_and_heading_are_localized()
    {
        using var cut = Render();

        cut.Find("[data-testid='modeling-viewer']").GetAttribute("aria-label")
            .Should().Be("Modeling diagram viewer");
        cut.Find("[data-testid='modeling-viewer-element-list']").TextContent
            .Should().Contain("Elements (7)");
    }

    [Fact]
    public void Print_stylesheet_drops_zoom_chrome_and_keeps_the_list_opt_in()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Tempo.Blazor.Modeling", "Components", "Modeling", "TmModelingViewer.razor.css"));

        css.Should().Contain("@media print");
        css.Should().Contain("[data-tm-group=\"zoom\"]", "the printed output carries no zoom controls");
        css.Should().Contain("break-inside: avoid");
        css.Should().Contain("tm-modeling-viewer--print-elements", "the element list is a print opt-in");
        css.Should().Contain("display: none", "the list is hidden in print unless opted in");
    }

    private IRenderedComponent<TmModelingViewer> Render()
        => Render<TmModelingViewer>(parameters => parameters
            .Add(p => p.Model, ModelingViewerTestModels.CreateProcessModel())
            .Add(p => p.ViewId, ModelingViewerTestModels.ViewId)
            .Add(p => p.LayoutMode, TmLayoutMode.Desktop));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent!;
        }

        return directory!.FullName;
    }
}
