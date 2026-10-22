using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// The canvas skeleton: sections → columns → blocks with the stable <c>data-tm-*</c> selectors, selection
/// and the keyboard contract. The VISUAL per-block rendering lives in
/// <see cref="TmEmailCanvasBlockVisualTests"/>, the untrusted-content rules in
/// <see cref="TmEmailCanvasSecurityTests"/>.
/// </summary>
public class TmEmailTemplateCanvasTests : BunitContext
{
    public TmEmailTemplateCanvasTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private static EmailTemplateDocument TwoColumnsThreeBlocks()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var c1 = new EmailColumn();
        c1.Blocks.Add(new EmailTextBlock { Content = "Hello <b>world</b>" });
        c1.Blocks.Add(new EmailButtonBlock { Text = "Go" });
        var c2 = new EmailColumn();
        c2.Blocks.Add(new EmailImageBlock { Alt = "Logo" });
        section.Columns.Add(c1);
        section.Columns.Add(c2);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public void Renders_DomStructureMatchingModel()
    {
        var doc = TwoColumnsThreeBlocks();

        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc));

        cut.FindAll("[data-tm-section]").Should().HaveCount(1);
        cut.FindAll("[data-tm-column]").Should().HaveCount(2);
        cut.FindAll("[data-tm-block-id]").Should().HaveCount(3);
    }

    [Fact]
    public void TextBlock_ShowsItsRenderedHtmlContent()
    {
        // Fáze 9A: the canvas is WYSIWYG — a text block renders its sanitized inline HTML, so the bold
        // word is bold. The previous structural "type + stripped text" preview is gone (breaking, see
        // CHANGELOG 2.10.0).
        var doc = TwoColumnsThreeBlocks();
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc));

        cut.Markup.Should().Contain("<b>world</b>");
        cut.FindAll(".tm-email-canvas-block__kind").Should().BeEmpty("the type label is not drawn over the email");
        cut.FindAll(".tm-email-canvas-block__detail").Should().BeEmpty();
    }

    [Fact]
    public void EmptyDocument_ShowsEmptyState()
    {
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, new EmailTemplateDocument()));
        cut.FindAll("[data-tm-block-id]").Should().BeEmpty();
        cut.FindAll(".tm-empty-state").Should().ContainSingle();
    }

    [Fact]
    public void ClickingBlock_RaisesSelectedIdChanged()
    {
        var doc = TwoColumnsThreeBlocks();
        Guid? selected = null;
        var blockId = doc.Sections[0].Columns[0].Blocks[1].Id;

        var cut = Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedIdChanged, id => selected = id));

        cut.Find($"[data-tm-block-id=\"{blockId}\"]").Click();

        selected.Should().Be(blockId);
    }

    [Fact]
    public void SelectedBlock_GetsSelectedClassAndAria()
    {
        var doc = TwoColumnsThreeBlocks();
        var blockId = doc.Sections[0].Columns[0].Blocks[0].Id;

        var cut = Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedId, blockId));

        var el = cut.Find($"[data-tm-block-id=\"{blockId}\"]");
        el.ClassList.Should().Contain("is-selected");
        // Review 20: the items are role=list/listitem, where aria-selected is not a supported state (a screen
        // reader ignored it). aria-current is the state that is actually announced, and tabindex is roving.
        el.GetAttribute("aria-current").Should().Be("true");
        el.GetAttribute("tabindex").Should().Be("0", "the selected item is the list's one tab stop");
    }

    [Fact]
    public void OnlyOneBlock_is_in_theTabOrder_and_the_rest_are_off_it()
    {
        var doc = TwoColumnsThreeBlocks();
        var selectedId = doc.Sections[0].Columns[1].Blocks[0].Id;

        var cut = Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedId, selectedId));

        var stops = cut.FindAll("[data-tm-block-id]")
            .Where(el => el.GetAttribute("tabindex") == "0")
            .Select(el => el.GetAttribute("data-tm-block-id"))
            .ToList();
        stops.Should().Equal(new[] { selectedId.ToString() },
            "a roving tabindex keeps ONE tab stop in the list");
        cut.FindAll("[data-tm-block-id]").Should().OnlyContain(el =>
            el.GetAttribute("tabindex") == "0" || el.GetAttribute("tabindex") == "-1");
    }

    [Fact]
    public void Blocks_HaveListitemRoleAndAriaLabel()
    {
        var doc = TwoColumnsThreeBlocks();
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc));

        var first = cut.Find("[data-tm-block-id]");
        first.GetAttribute("role").Should().Be("listitem");
        first.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
        cut.Find("[data-tm-canvas-doc]").GetAttribute("role").Should().Be("list");
    }

    [Fact]
    public void Images_carry_the_alt_text_from_the_model()
    {
        var image = new EmailImageBlock { Src = "https://example.com/logo.png", Alt = "Company logo" };
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, DocWith(image)));

        cut.Find("img").GetAttribute("alt").Should().Be("Company logo");
    }

    [Fact]
    public void An_image_without_alt_is_decorative_and_hidden_from_assistive_tech()
    {
        var image = new EmailImageBlock { Src = "https://example.com/spacer.png", Alt = string.Empty };
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, DocWith(image)));

        var img = cut.Find("img");
        img.GetAttribute("alt").Should().BeEmpty();
        img.GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void The_paper_keeps_the_configured_content_width_and_never_overflows_its_panel()
    {
        var doc = DocWith(new EmailTextBlock { Content = "Hi" });
        doc.Styles.ContentWidth = "480px";

        var paper = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc)).Find("[data-tm-canvas-paper]");

        paper.GetAttribute("style").Should().Contain("480px");
        paper.GetAttribute("style").Should().Contain("max-inline-size:100%");
    }

    [Fact]
    public void ReadOnly_keeps_the_visual_body_but_drops_every_action()
    {
        var doc = TwoColumnsThreeBlocks();
        var cut = Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ReadOnly, true));

        cut.FindAll("[data-tm-block-body]").Should().HaveCount(3, "read-only still shows the email");
        cut.FindAll("[data-tm-block-action]").Should().BeEmpty();
        cut.FindAll("[data-tm-section-action]").Should().BeEmpty();
        cut.FindAll("[data-tm-block-id][draggable]").Should().BeEmpty();
    }

    private static EmailTemplateDocument DocWith(params EmailBlockBase[] blocks)
    {
        var document = new EmailTemplateDocument();
        var column = new EmailColumn();
        foreach (var block in blocks) column.Blocks.Add(block);
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
