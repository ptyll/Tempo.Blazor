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

public class CanvasDragDropTests : BunitContext
{
    public CanvasDragDropTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void DragBlockFromToolbox_DropsIntoColumn_AtIndex()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "existing" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        // Begin dragging a Button from the toolbox; drop zones then appear.
        cut.Find("[data-tm-block=\"button\"]").TriggerEvent("ondragstart", new DragEventArgs());
        // Drop at index 0 of the column (before the existing block).
        cut.Find($"[data-tm-drop-col=\"{col.Id}\"][data-tm-drop-index=\"0\"]").TriggerEvent("ondrop", new DragEventArgs());

        col.Blocks.Should().HaveCount(2);
        col.Blocks[0].Should().BeOfType<EmailButtonBlock>();
        col.Blocks[1].Should().BeOfType<EmailTextBlock>();
    }

    [Fact]
    public void DragExistingBlock_BetweenColumns_MovesIt()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var c1 = new EmailColumn();
        var moving = new EmailTextBlock { Content = "move me" };
        c1.Blocks.Add(moving);
        var c2 = new EmailColumn();
        section.Columns.Add(c1);
        section.Columns.Add(c2);
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find($"[data-tm-block-id=\"{moving.Id}\"]").TriggerEvent("ondragstart", new DragEventArgs());
        cut.Find($"[data-tm-drop-col=\"{c2.Id}\"][data-tm-drop-index=\"0\"]").TriggerEvent("ondrop", new DragEventArgs());

        c1.Blocks.Should().BeEmpty();
        c2.Blocks.Should().ContainSingle().Which.Should().BeSameAs(moving);
    }

    [Fact]
    public void EmptyColumn_AlwaysExposesPersistentDropTarget_EvenWhenIdle()
    {
        // Regression: previously the empty-column drop area collapsed to a thin strip the moment
        // a drag began (the placeholder was hidden), so blocks could not be dropped by hand. The
        // empty column must present a large, persistent drop target — also when not dragging.
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        // No transient insert-between zones while idle…
        cut.FindAll(".tm-email-canvas-dropzone").Should().BeEmpty();
        // …but the empty column is itself a drop target.
        var target = cut.Find($"[data-tm-drop-empty][data-tm-drop-col=\"{col.Id}\"]");
        target.GetAttribute("data-tm-drop-index").Should().Be("0");
    }

    [Fact]
    public void DropOntoPersistentEmptyTarget_AddsBlock()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-block=\"text\"]").TriggerEvent("ondragstart", new DragEventArgs());
        cut.Find($"[data-tm-drop-empty][data-tm-drop-col=\"{col.Id}\"]").TriggerEvent("ondrop", new DragEventArgs());

        col.Blocks.Should().ContainSingle().Which.Should().BeOfType<EmailTextBlock>();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
