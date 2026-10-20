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
    public void TextBlock_ShowsStrippedContent()
    {
        var doc = TwoColumnsThreeBlocks();
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc));

        cut.Markup.Should().Contain("Hello world");
        cut.Markup.Should().NotContain("<b>world</b>");
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
        el.GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void Blocks_HaveListitemRoleAndAriaLabel()
    {
        var doc = TwoColumnsThreeBlocks();
        var cut = Render<TmEmailTemplateCanvas>(p => p.Add(c => c.Document, doc));

        var first = cut.Find("[data-tm-block-id]");
        first.GetAttribute("role").Should().Be("listitem");
        first.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
