using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramToolboxTests : DiagramTestBase
{
    [Fact]
    public void Renders_All_Categories_By_Default()
    {
        var cut = Render<TmDiagramToolbox>();

        var categories = cut.FindAll(".tm-diagram-toolbox__category-header");
        categories.Should().NotBeEmpty();
    }

    [Fact]
    public void TapStencil_InsertsAtViewportCenter()
    {
        // Touch has no HTML5 drag: tapping a stencil tile inserts it at the visible
        // viewport centre — the same HandleQuickInsert path the keyboard uses (WCAG
        // 2.5.7: a drag is never the only way to insert).
        DiagramDocument? changed = null;
        var doc = new DiagramDocument();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.DocumentChanged, d => changed = d));

        var tile = cut.Find("[data-stencil-id='general.rectangle']");
        tile.Click();

        changed.Should().NotBeNull("tapping a stencil tile must insert a node");
        var inserted = changed!.Nodes.Last(n => n.StencilId == "general.rectangle");

        // Without a JS measure the viewport falls back to the whole page, so the
        // node centre must land on the page centre (within one 8px grid cell).
        const double grid = 8;
        (inserted.X + inserted.W / 2d).Should().BeApproximately(doc.Width / 2d, grid, "tap inserts at the viewport centre");
        (inserted.Y + inserted.H / 2d).Should().BeApproximately(doc.Height / 2d, grid, "tap inserts at the viewport centre");
    }

    [Fact]
    public void TapEdgeStencil_SwitchesToEdgeTool()
    {
        DiagramDocument? changed = null;
        var doc = new DiagramDocument();

        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, doc)
            .Add(e => e.DocumentChanged, d => changed = d));

        var edgeTile = cut.Find("[data-stencil-kind='edge']");
        edgeTile.Click();

        changed.Should().BeNull("an edge stencil tap arms the edge tool instead of mutating the document");
        doc.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void Search_Filters_Stencils_In_Realtime()
    {
        var cut = Render<TmDiagramToolbox>();

        // Type "Rectangle" into search input
        var searchInput = cut.Find(".tm-diagram-toolbox__search input");
        searchInput.Input("Rectangle");

        // Should show only matching stencils
        var items = cut.FindAll(".tm-diagram-toolbox__item");
        items.Should().Contain(i => i.TextContent.Contains("Rectangle", StringComparison.OrdinalIgnoreCase));
        items.Should().NotContain(i => i.TextContent.Contains("Cloud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_Shows_NoResults_When_No_Match()
    {
        var cut = Render<TmDiagramToolbox>();

        var searchInput = cut.Find(".tm-diagram-toolbox__search input");
        searchInput.Input("xyznonexistent");

        cut.FindAll(".tm-diagram-toolbox__no-results").Should().ContainSingle();
        cut.FindAll(".tm-diagram-toolbox__category").Should().BeEmpty();
    }

    [Fact]
    public void Search_Shows_Only_Categories_With_Matching_Stencils()
    {
        var cut = Render<TmDiagramToolbox>();

        var searchInput = cut.Find(".tm-diagram-toolbox__search input");
        searchInput.Input("Cloud");

        var categories = cut.FindAll(".tm-diagram-toolbox__category-header");
        categories.Should().ContainSingle();

        var items = cut.FindAll(".tm-diagram-toolbox__item");
        items.Should().OnlyContain(i => i.TextContent.Contains("Cloud", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Clear_Search_Restores_All_Categories()
    {
        var cut = Render<TmDiagramToolbox>();

        var initialCategories = cut.FindAll(".tm-diagram-toolbox__category-header").Count;

        var searchInput = cut.Find(".tm-diagram-toolbox__search input");
        searchInput.Input("Cloud");

        // Clear the search
        searchInput.Input(string.Empty);

        var restoredCategories = cut.FindAll(".tm-diagram-toolbox__category-header").Count;
        restoredCategories.Should().Be(initialCategories);
    }
}
