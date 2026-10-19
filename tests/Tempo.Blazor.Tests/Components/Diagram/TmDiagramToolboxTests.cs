using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void Search_IsDiacriticInsensitive()
    {
        // D7: the visible (localized) label is searchable without diacritics — "obdelnik"
        // must find the rectangle even though its raw name is the English "Rectangle".
        UseCzechLocalization();
        var registry = Services.GetRequiredService<Tempo.Blazor.Components.Diagram.Stencils.DiagramStencilRegistry>();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "general.rectangle",
            Name = "Rectangle",
            NameResourceKey = "TmDiagramEditor_Rectangle",
            Category = "General",
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal
        }, priority: 1);

        var cut = Render<TmDiagramToolbox>();

        var searchInput = cut.Find(".tm-diagram-toolbox__search input");
        searchInput.Input("obdelnik");

        var items = cut.FindAll(".tm-diagram-toolbox__item");
        items.Should().Contain(i => i.GetAttribute("data-stencil-id") == "general.rectangle",
            "the Czech label 'Obdélník' matches the unaccented query");
    }

    [Fact]
    public void ShowTitle_DefaultsToTrue()
    {
        var cut = Render<TmDiagramToolbox>();

        cut.Instance.ShowTitle.Should().BeTrue();
        cut.Find(".tm-diagram-toolbox__title").Should().NotBeNull();
    }

    [Fact]
    public void ShowTitle_False_OmitsTheHeading()
    {
        // U4: the mobile sheet tab names the palette — no duplicate heading inside.
        var cut = Render<TmDiagramToolbox>(p => p.Add(x => x.ShowTitle, false));

        cut.FindAll(".tm-diagram-toolbox__title").Should().BeEmpty();
    }

    // ── UX review: the mobile Shapes sheet starts with only General expanded ──

    [Fact]
    public void CollapseCategoriesExceptFirst_ExpandsOnlyTheFirstCategory()
    {
        var cut = Render<TmDiagramToolbox>(p => p.Add(x => x.CollapseCategoriesExceptFirst, true));

        var headers = cut.FindAll(".tm-diagram-toolbox__category-header");
        headers.Should().HaveCountGreaterThan(1, "the fixture toolbox has several categories");
        headers.Count(h => h.GetAttribute("aria-expanded") == "true").Should().Be(1,
            "only the first (General/basic) category starts expanded");
        headers[0].GetAttribute("aria-expanded").Should().Be("true");

        // The user's own choice wins afterwards: expanding a collapsed category works.
        headers[1].Click();
        cut.FindAll(".tm-diagram-toolbox__category-header")[1]
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void CollapseCategoriesExceptFirst_False_KeepsEveryCategoryExpanded()
    {
        var cut = Render<TmDiagramToolbox>();

        cut.FindAll(".tm-diagram-toolbox__category-header")
            .Should().OnlyContain(h => h.GetAttribute("aria-expanded") == "true",
                "the desktop default keeps every category expanded");
    }

    [Fact]
    public void MobileEditor_ShapesSheet_StartsWithOnlyTheGeneralCategoryExpanded()
    {
        var cut = Render<TmDiagramEditor>(p => p
            .Add(e => e.Document, new DiagramDocument())
            .Add(e => e.LayoutMode, Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Mobile));

        cut.Find(".tm-mobile-action-bar__action[data-action-id='shapes']").Click();

        var headers = cut.FindAll(".tm-editor-shell__sheet .tm-diagram-toolbox__category-header");
        headers.Should().HaveCountGreaterThan(1);
        headers.Count(h => h.GetAttribute("aria-expanded") == "true").Should().Be(1);
    }

    // ── UX review (fze-6): search expands collapsed categories, first-LIBRARY default ──

    [Fact]
    public void CollapseCategoriesExceptFirst_SearchExpandsACollapsedCategoryWithAMatch()
    {
        var cut = Render<TmDiagramToolbox>(p => p.Add(x => x.CollapseCategoriesExceptFirst, true));

        // Precondition: the Cloud stencil's Network category starts collapsed on the mobile sheet.
        cut.FindAll(".tm-diagram-toolbox__category")
            .Single(c => c.GetAttribute("data-palette-id") == "Network")
            .QuerySelector(".tm-diagram-toolbox__category-header")!
            .GetAttribute("aria-expanded").Should().Be("false");

        cut.Find(".tm-diagram-toolbox__search input").Input("Cloud");

        cut.FindAll(".tm-diagram-toolbox__item")
            .Should().ContainSingle(i => i.GetAttribute("data-stencil-id") == "network.cloud",
                "a live search renders the matching tile even though its category starts collapsed");
        cut.FindAll(".tm-diagram-toolbox__category").Should().ContainSingle(
            "only the matching category renders at all");
    }

    [Fact]
    public void CollapseCategoriesExceptFirst_KeepsEveryPaletteOfTheFirstLibraryExpanded()
    {
        // The default collapses everything OUTSIDE THE FIRST LIBRARY — a second palette of the
        // General set stays expanded too (the old per-category rule collapsed it).
        var registry = Services.GetRequiredService<Tempo.Blazor.Components.Diagram.Stencils.DiagramStencilRegistry>();
        registry.RegisterStencil(new DiagramStencil
        {
            Id = "general.extra",
            Name = "Extra",
            Category = "General",
            PaletteId = "general.more",
            Kind = DiagramStencilKind.Node,
            Origin = DiagramStencilOrigin.TempoOriginal
        }, priority: 1);

        var cut = Render<TmDiagramToolbox>(p => p.Add(x => x.CollapseCategoriesExceptFirst, true));

        cut.FindAll(".tm-diagram-toolbox__category")
            .Where(c => c.GetAttribute("data-palette-id") is "General" or "general.more")
            .Select(c => c.QuerySelector(".tm-diagram-toolbox__category-header")!.GetAttribute("aria-expanded"))
            .Should().Equal(["true", "true"], "both palettes of the General library stay expanded");
    }

    [Fact]
    public void CollapseCategoriesExceptFirst_TrueThenFalse_ReordersTheLibraries()
    {
        var cut = Render<TmDiagramToolbox>(p => p.Add(x => x.CollapseCategoriesExceptFirst, true));

        cut.FindAll(".tm-diagram-toolbox__library").First()
            .GetAttribute("data-library-id").Should().Be("General",
                "with the mobile default the General library leads");

        cut.Render(p => p.Add(x => x.CollapseCategoriesExceptFirst, false));

        cut.FindAll(".tm-diagram-toolbox__library").First()
            .GetAttribute("data-library-id").Should().NotBe("General",
                "clearing the flag re-sorts the libraries alphabetically without a search keystroke");
    }
}
