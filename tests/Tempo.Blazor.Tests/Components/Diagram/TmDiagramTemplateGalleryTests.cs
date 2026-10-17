using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Tests.Diagram;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramTemplateGalleryTests : DiagramTestBase
{
    public TmDiagramTemplateGalleryTests()
    {
        Services.AddSingleton<NavigationManager>(new BunitNavigationManager());
    }

    [Fact]
    public void Renders_Categories_And_Templates()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "t2", Name = "Flowchart", Category = "Flowchart" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var sections = cut.FindAll(".tm-diagram-template-gallery__section-title");
        sections.Should().HaveCount(2);
        sections[0].TextContent.Should().Be("Flowcharts",
            "the section heading resolves through the category key scheme");
        sections[1].TextContent.Should().Be("General");
    }

    [Fact]
    public void Search_Filters_Templates_By_Name()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "t2", Name = "Flowchart", Category = "Flowchart" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var searchInput = cut.Find("input.tm-diagram-template-gallery__search");
        searchInput.Input("flow");

        var cards = cut.FindAll(".tm-diagram-template-card");
        cards.Should().ContainSingle();
        cards[0].TextContent.Should().Contain("Flowchart");
    }

    [Fact]
    public void Category_Filter_Shows_Only_Selected_Category()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "t2", Name = "Flowchart", Category = "Flowchart" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var filters = cut.FindAll(".tm-diagram-template-gallery__filter");
        filters.First(f => f.TextContent.Contains("Flowchart")).Click();

        var cards = cut.FindAll(".tm-diagram-template-card");
        cards.Should().ContainSingle();
        cards[0].TextContent.Should().Contain("Flowchart");
    }

    [Fact]
    public void Selecting_Template_Enables_Create_Button()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var button = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        button.HasAttribute("disabled").Should().BeTrue();

        cut.Find(".tm-diagram-template-card").Click();

        button = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        button.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public async Task Clicking_Create_Invokes_OnSelect_With_Selected_Template()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });

        DiagramTemplate? selected = null;
        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnSelect, t => selected = t));

        cut.Find(".tm-diagram-template-card").Click();
        var createButton = cut.FindAll("button").First(b => b.TextContent.Contains("Create"));
        await cut.InvokeAsync(() => createButton.Click());

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("t1");
    }

    [Fact]
    public async Task DoubleClickOrEnter_InvokesCreate()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });

        DiagramTemplate? created = null;
        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.OnSelect, t => created = t));

        var card = cut.Find(".tm-diagram-template-card");

        // Double-click creates immediately, without the detour through the footer button.
        await cut.InvokeAsync(() => card.DoubleClick());
        created.Should().NotBeNull("double-clicking a card must create the template");
        created!.Id.Should().Be("t1");

        // Enter on the focused card creates as well (keyboard path).
        created = null;
        await cut.InvokeAsync(() => card.KeyDown("Enter"));
        created.Should().NotBeNull("pressing Enter on a card must create the template");
        created!.Id.Should().Be("t1");
    }

    [Fact]
    public void SearchAndChipsInOneRow()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "t2", Name = "Flowchart", Category = "Flowchart" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        // Mockup: the search field and the category chips share ONE toolbar row.
        var row = cut.Find(".tm-diagram-template-gallery__toolbar");
        row.Should().NotBeNull();
        row.QuerySelector(".tm-diagram-template-gallery__search").Should().NotBeNull(
            "the search input lives in the shared toolbar row");
        row.QuerySelectorAll(".tm-diagram-template-gallery__filter").Should().NotBeEmpty(
            "the category chips live in the same toolbar row");
    }

    [Fact]
    public void BuiltInTemplateNames_AreLocalized_Cs()
    {
        UseCzechLocalization();
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        // Mirrors the built-in provider's catalog entry: the display name resolves through a
        // resource key derived from the template Id, with the raw name only as the fallback.
        registry.RegisterTemplate(new DiagramTemplate { Id = "blank", Name = "Blank Diagram", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "uml-class", Name = "UML Class Diagram", Category = "UML" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var names = cut.FindAll(".tm-diagram-template-card__name").Select(n => n.TextContent.Trim()).ToList();
        names.Should().Contain("Prázdný diagram");
        names.Should().Contain("Diagram tříd UML");
        names.Should().NotContain("Blank Diagram");
        names.Should().NotContain("UML Class Diagram");

        // Categories localize through the same by-name key scheme.
        var sections = cut.FindAll(".tm-diagram-template-gallery__section-title").Select(s => s.TextContent.Trim()).ToList();
        sections.Should().Contain("Obecné");
    }

    [Fact]
    public void MobilePresentation_ListCards()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true)
            .Add(p => p.LayoutMode, TmLayoutMode.Mobile));

        // Forced mobile layout presents the gallery as a foundation-F2 bottom sheet whose
        // body lists the templates as cards.
        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");
        cut.FindAll(".tm-diagram-template-card").Should().NotBeEmpty();
    }

    [Fact]
    public void FilterChips_HaveTypeButtonAndAriaPressed()
    {
        var registry = Services.GetRequiredService<DiagramTemplateRegistry>();
        registry.RegisterTemplate(new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" });
        registry.RegisterTemplate(new DiagramTemplate { Id = "t2", Name = "Flowchart", Category = "Flowchart" });

        var cut = Render<TmDiagramTemplateGallery>(parameters => parameters
            .Add(p => p.Show, true));

        var chips = cut.FindAll(".tm-diagram-template-gallery__filter").ToList();
        chips.Should().HaveCountGreaterThan(1);

        foreach (var chip in chips)
        {
            chip.GetAttribute("type").Should().Be("button", "filter chips must be type=button");
            chip.GetAttribute("aria-pressed").Should().BeOneOf("true", "false",
                "filter chips are toggle buttons and must expose aria-pressed");
        }

        chips.Count(c => c.GetAttribute("aria-pressed") == "true").Should().Be(1,
            "exactly the active category chip is pressed");
    }

    private sealed class BunitNavigationManager : NavigationManager
    {
        public BunitNavigationManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad) { }
    }
}
