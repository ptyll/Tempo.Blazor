using System.Text.RegularExpressions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Templates;
using Tempo.Blazor.Tests.Diagram;
using Tempo.Blazor.Tests.Theme;

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

        // Placeholder (X6): the notation icon — never a first letter.
        var placeholder = cut.Find(".tm-diagram-template-card__placeholder");
        placeholder.QuerySelector("svg").Should().NotBeNull("the placeholder paints a category icon");
        placeholder.TextContent.Trim().Should().BeEmpty("no letter text renders inside the placeholder");
    }

    [Fact]
    public void EmptyName_RendersPlaceholder_WithoutThrowing()
    {
        var template = new DiagramTemplate { Id = "no-name", Name = "", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        cut.Find(".tm-diagram-template-card__placeholder").Should().NotBeNull();
        cut.Find(".tm-diagram-template-card").GetAttribute("aria-label")
            .Should().Be("no-name", "an empty name falls back to the template id, never an index crash");
    }

    [Fact]
    public void Placeholder_UsesCategoryIcon()
    {
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "Database" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        var placeholder = cut.Find(".tm-diagram-template-card__placeholder");
        placeholder.QuerySelector("svg.tm-icon-lg").Should().NotBeNull("the category icon renders");
        placeholder.TextContent.Trim().Should().BeEmpty();
    }

    [Fact]
    public void Tags_ThatRepeatTheCategory_AreHidden()
    {
        var template = new DiagramTemplate
        {
            Id = "t1",
            Name = "ArchiMate 3 Layered Baseline",
            Category = "ArchiMate",
            Tags = ["archimate", "layers", "LAYERS"]
        };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        cut.FindAll(".tm-diagram-template-card__tag")
            .Select(t => t.TextContent.Trim())
            .Should().Equal(new[] { "layers" }, "tags that merely repeat the category are deduplicated away");
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
    public async Task EnterKey_Activates_Space_Selects()
    {
        var clicks = 0;
        var activations = 0;
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false)
            .Add(p => p.OnClick, () => clicks++)
            .Add(p => p.OnActivateRequest, () => activations++));

        var card = cut.Find(".tm-diagram-template-card");

        // Enter creates (the mockup's double-click twin) on keydown, guarded against auto-repeat
        await cut.InvokeAsync(() => card.KeyDown("Enter"));
        activations.Should().Be(1, "Enter must create exactly once");
        clicks.Should().Be(0, "Enter is the create shortcut, not the selection tap");

        // A held Enter key auto-repeats and must not re-activate
        await cut.InvokeAsync(() => card.KeyDown("Enter", true));
        activations.Should().Be(1, "auto-repeat Enter must not re-activate the card");

        // Space keeps the selection tap: activation on keyup
        await cut.InvokeAsync(() =>
        {
            card.KeyDown(" ");
            card.KeyUp(" ");
        });
        clicks.Should().Be(1, "Space must select the card exactly once per press");
        activations.Should().Be(1, "Space never creates");

        // The div role=button is not native: there must be no separate click invocation
        // stacked behind the emulated key activation (exactly-one-activation rule).
    }

    [Fact]
    public async Task DoubleClick_Activates()
    {
        var activations = 0;
        var template = new DiagramTemplate { Id = "t1", Name = "Blank", Category = "General" };

        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.OnActivateRequest, () => activations++));

        await cut.InvokeAsync(() => cut.Find(".tm-diagram-template-card").DoubleClick());
        activations.Should().Be(1, "double-clicking a card is the create shortcut");
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

    [Fact]
    public void Renders_Thumbnail_Above_Name()
    {
        const string cardCssPath = "src/Tempo.Blazor.DiagramEditor/wwwroot/css/components/_diagram-editor.css";
        var css = File.ReadAllText(Path.Combine(ThemeCss.RepositoryRoot().FullName, cardCssPath));

        var template = new DiagramTemplate { Id = "t1", Name = "Flowchart", Category = "General" };
        var cut = Render<TmDiagramTemplateCard>(parameters => parameters
            .Add(p => p.Template, template)
            .Add(p => p.Selected, false));

        // The vertical mockup card stacks the preview above the name: thumb first in the DOM...
        var card = cut.Find(".tm-diagram-template-card");
        card.Children[0].ClassList.Should().Contain("tm-diagram-template-card__thumb");
        card.Children.Skip(1).Should().Contain(c => c.ClassList.Contains("tm-diagram-template-card__info"));

        // ...and the stylesheet must lay the card out as a column (not the legacy row),
        // with a 16:10 preview area.
        var cardRule = Regex.Match(css, @"\.tm-diagram-template-card\s*\{([^}]*)\}");
        cardRule.Success.Should().BeTrue("the card rule must stay the single layout source");
        cardRule.Groups[1].Value.Should().Contain("flex-direction: column");

        var thumbRule = Regex.Match(css, @"\.tm-diagram-template-card__thumb\s*\{([^}]*)\}");
        thumbRule.Success.Should().BeTrue();
        thumbRule.Groups[1].Value.Should().Contain("aspect-ratio: 16 / 10");
    }
}
