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

public class CanvasActionsTests : BunitContext
{
    public CanvasActionsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private static EmailTemplateDocument OneColumn(params EmailBlockBase[] blocks)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        foreach (var block in blocks)
            col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    private IRenderedComponent<TmEmailTemplateCanvas> Render(EmailTemplateDocument doc)
        => Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.OnDocumentChanged, () => { }));

    [Fact]
    public void DeleteBlock_RemovesItFromModel()
    {
        var doc = OneColumn(new EmailTextBlock { Content = "a" }, new EmailButtonBlock { Text = "b" });
        var blockId = doc.Sections[0].Columns[0].Blocks[0].Id;
        var cut = Render(doc);

        cut.Find($"[data-tm-block-id=\"{blockId}\"] [data-tm-block-action=\"delete\"]").Click();

        doc.Sections[0].Columns[0].Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<EmailButtonBlock>();
    }

    [Fact]
    public void DuplicateBlock_AddsCopyAfterOriginal()
    {
        var doc = OneColumn(new EmailTextBlock { Content = "a" });
        var blockId = doc.Sections[0].Columns[0].Blocks[0].Id;
        var cut = Render(doc);

        cut.Find($"[data-tm-block-id=\"{blockId}\"] [data-tm-block-action=\"duplicate\"]").Click();

        doc.Sections[0].Columns[0].Blocks.Should().HaveCount(2);
        doc.Sections[0].Columns[0].Blocks[1].Id.Should().NotBe(blockId);
    }

    [Fact]
    public void MoveBlockDown_ReordersWithinColumn()
    {
        var a = new EmailTextBlock { Content = "a" };
        var b = new EmailTextBlock { Content = "b" };
        var doc = OneColumn(a, b);
        var cut = Render(doc);

        cut.Find($"[data-tm-block-id=\"{a.Id}\"] [data-tm-block-action=\"down\"]").Click();

        doc.Sections[0].Columns[0].Blocks[0].Should().BeSameAs(b);
        doc.Sections[0].Columns[0].Blocks[1].Should().BeSameAs(a);
    }

    [Fact]
    public void AddColumn_AddsAndRebalances()
    {
        var doc = OneColumn(new EmailTextBlock());
        var sectionId = doc.Sections[0].Id;
        var cut = Render(doc);

        cut.Find($"[data-tm-section=\"{sectionId}\"] [data-tm-section-action=\"add-column\"]").Click();

        doc.Sections[0].Columns.Should().HaveCount(2);
        doc.Sections[0].Columns.Select(c => c.Width).Should().AllBe("50%");
    }

    [Fact]
    public void DeleteSection_RemovesIt()
    {
        var doc = OneColumn(new EmailTextBlock());
        var sectionId = doc.Sections[0].Id;
        var cut = Render(doc);

        cut.Find($"[data-tm-section=\"{sectionId}\"] [data-tm-section-action=\"delete\"]").Click();

        doc.Sections.Should().BeEmpty();
    }

    [Fact]
    public void DuplicateSection_AddsCopy()
    {
        var doc = OneColumn(new EmailTextBlock());
        var sectionId = doc.Sections[0].Id;
        var cut = Render(doc);

        cut.Find($"[data-tm-section=\"{sectionId}\"] [data-tm-section-action=\"duplicate\"]").Click();

        doc.Sections.Should().HaveCount(2);
    }

    [Fact]
    public void RemoveColumn_RemovesAndRebalances()
    {
        var doc = OneColumn(new EmailTextBlock());
        // make two columns first
        doc.Sections[0].AddColumn(new EmailColumn());
        var second = doc.Sections[0].Columns[1];
        var cut = Render(doc);

        cut.Find($"[data-tm-column=\"{second.Id}\"] [data-tm-column-action=\"remove\"]").Click();

        doc.Sections[0].Columns.Should().ContainSingle();
        doc.Sections[0].Columns[0].Width.Should().Be("100%");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
