using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Diagram;

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
        sections[0].TextContent.Should().Be("Flowchart");
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
