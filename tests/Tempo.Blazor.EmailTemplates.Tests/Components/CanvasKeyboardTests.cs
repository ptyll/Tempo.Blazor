using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class CanvasKeyboardTests : BunitContext
{
    public CanvasKeyboardTests()
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

    private IRenderedComponent<TmEmailTemplateCanvas> Render(EmailTemplateDocument doc, Guid? selected, Action<Guid?> onSel)
        => Render<TmEmailTemplateCanvas>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.SelectedId, selected)
            .Add(c => c.SelectedIdChanged, id => onSel(id))
            .Add(c => c.OnDocumentChanged, () => { }));

    [Fact]
    public void DeleteKey_DeletesSelectedBlock()
    {
        var a = new EmailTextBlock { Content = "a" };
        var doc = OneColumn(a, new EmailButtonBlock { Text = "b" });
        var cut = Render(doc, a.Id, _ => { });

        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "Delete" });

        doc.Sections[0].Columns[0].Blocks.Should().ContainSingle().Which.Should().BeOfType<EmailButtonBlock>();
    }

    [Fact]
    public void CtrlD_DuplicatesSelectedBlock()
    {
        var a = new EmailTextBlock { Content = "a" };
        var doc = OneColumn(a);
        var cut = Render(doc, a.Id, _ => { });

        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "d", CtrlKey = true });

        doc.Sections[0].Columns[0].Blocks.Should().HaveCount(2);
    }

    [Fact]
    public void ArrowDown_NavigatesToNextBlock()
    {
        var a = new EmailTextBlock { Content = "a" };
        var b = new EmailTextBlock { Content = "b" };
        var doc = OneColumn(a, b);
        Guid? selected = a.Id;
        var cut = Render(doc, a.Id, id => selected = id);

        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        selected.Should().Be(b.Id);
    }

    [Fact]
    public void Escape_ClearsSelection()
    {
        var a = new EmailTextBlock { Content = "a" };
        var doc = OneColumn(a);
        Guid? selected = a.Id;
        var cut = Render(doc, a.Id, id => selected = id);

        cut.Find("[data-tm-canvas-doc]").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        selected.Should().BeNull();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
