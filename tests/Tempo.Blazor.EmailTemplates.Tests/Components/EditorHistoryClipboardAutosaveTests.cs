using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class EditorHistoryClipboardAutosaveTests : BunitContext
{
    public EditorHistoryClipboardAutosaveTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private static EmailTemplateDocument OneBlock(out Guid blockId)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        var block = new EmailTextBlock { Content = "a" };
        blockId = block.Id;
        col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    [Fact]
    public void Undo_AfterAddingBlock_RemovesIt()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.FindAll("[data-tm-block-id]").Should().HaveCount(1);

        cut.Find("[data-tm-undo]").Click();

        cut.FindAll("[data-tm-block-id]").Should().BeEmpty();
    }

    [Fact]
    public void Redo_ReappliesUndoneChange()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-undo]").Click();
        cut.Find("[data-tm-redo]").Click();

        cut.FindAll("[data-tm-block-id]").Should().HaveCount(1);
    }

    [Fact]
    public void CtrlZ_Undoes()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-block=\"text\"]").Click();
        cut.Find("[data-tm-email-editor]").KeyDown(new KeyboardEventArgs { Key = "z", CtrlKey = true });

        cut.FindAll("[data-tm-block-id]").Should().BeEmpty();
    }

    [Fact]
    public void CopyPaste_DuplicatesSelectedBlock()
    {
        var doc = OneBlock(out var blockId);
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find($"[data-tm-block-id=\"{blockId}\"]").Click();          // select
        var editor = cut.Find("[data-tm-email-editor]");
        editor.KeyDown(new KeyboardEventArgs { Key = "c", CtrlKey = true });
        editor.KeyDown(new KeyboardEventArgs { Key = "v", CtrlKey = true });

        cut.FindAll("[data-tm-block-id]").Should().HaveCount(2);
    }

    [Fact]
    public void AutoSave_FiresAfterDebounceInterval()
    {
        var time = new FakeTimeProvider();
        EmailTemplateDocument? saved = null;
        var doc = new EmailTemplateDocument();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.AutoSave, new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(2) })
            .Add(c => c.OnAutoSave, d => saved = d));

        cut.Find("[data-tm-block=\"text\"]").Click(); // a change schedules auto-save
        saved.Should().BeNull();

        time.Advance(TimeSpan.FromSeconds(2));

        saved.Should().NotBeNull();
    }

    [Fact]
    public void AutoSave_Disabled_DoesNotFire()
    {
        var time = new FakeTimeProvider();
        var fired = false;
        var doc = new EmailTemplateDocument();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.TimeProvider, time)
            .Add(c => c.OnAutoSave, _ => fired = true));

        cut.Find("[data-tm-block=\"text\"]").Click();
        time.Advance(TimeSpan.FromMinutes(1));

        fired.Should().BeFalse();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
