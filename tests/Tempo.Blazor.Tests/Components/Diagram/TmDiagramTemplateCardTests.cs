using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Diagram;

namespace Tempo.Blazor.Tests.Components.Diagram;

public class TmDiagramTemplateCardTests : DiagramTestBase
{
    [Fact]
    public void Renders_Template_Name_And_Tags()
    {
        var template = new DiagramTemplate
        {
            Id = "t1",
            Name = "Flowchart",
            Category = "General",
            Tags = ["process", "basic"]
        };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        cut.Find(".tm-diagram-template-card__name").TextContent.Should().Be("Flowchart");
        var tags = cut.FindAll(".tm-diagram-template-card__tag");
        tags.Should().HaveCount(2);
        tags[0].TextContent.Should().Be("process");
        tags[1].TextContent.Should().Be("basic");
    }

    [Fact]
    public void Renders_Placeholder_When_No_Thumbnail()
    {
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        cut.Find(".tm-diagram-template-card__placeholder").TextContent.Should().Be("B");
    }

    [Fact]
    public void Renders_Thumbnail_Image_When_Url_Provided()
    {
        var template = new DiagramTemplate
        {
            Id = "t1",
            Name = "Flowchart",
            Category = "General",
            ThumbnailUrl = "thumb.png"
        };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.BaseUri, "https://example.com/")
            .Add(p => p.Selected, false));

        var img = cut.Find("img");
        img.GetAttribute("src").Should().Be("https://example.com/thumb.png");
    }

    [Fact]
    public void Selected_State_Applies_Css_Class()
    {
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, true));

        cut.Find(".tm-diagram-template-card--selected").Should().NotBeNull();
    }

    [Fact]
    public void Click_Invokes_OnClick()
    {
        var clicked = false;
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false)
            .Add(p => p.OnClick, () => clicked = true));

        cut.Find(".tm-diagram-template-card").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public async Task EnterKey_And_Space_SelectCard()
    {
        var clicks = 0;
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false)
            .Add(p => p.OnClick, () => clicks++));

        var card = cut.Find(".tm-diagram-template-card");

        // Enter activates on keydown (guarded against auto-repeat)
        await cut.InvokeAsync(() => card.KeyDown("Enter"));
        clicks.Should().Be(1, "Enter must select the card exactly once");

        // A held Enter key auto-repeats and must not re-select
        await cut.InvokeAsync(() => card.KeyDown("Enter", true));
        clicks.Should().Be(1, "auto-repeat Enter must not re-select the card");

        // Space emulates the native button split: activation on keyup
        await cut.InvokeAsync(() =>
        {
            card.KeyDown(" ");
            card.KeyUp(" ");
        });
        clicks.Should().Be(2, "Space must select the card exactly once per press");

        // The div role=button is not native: there must be no separate click invocation
        // stacked behind the emulated key activation (exactly-one-activation rule).
    }

    [Fact]
    public void AriaPressed_RendersTrueFalseLowercase()
    {
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var unselected = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));
        var selected = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, true));

        unselected.Find(".tm-diagram-template-card").GetAttribute("aria-pressed").Should().Be("false");
        selected.Find(".tm-diagram-template-card").GetAttribute("aria-pressed").Should().Be("true");
    }
}
