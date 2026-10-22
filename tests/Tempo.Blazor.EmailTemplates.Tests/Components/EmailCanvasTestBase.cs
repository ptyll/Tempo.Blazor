using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fixture of the Fáze 9A WYSIWYG canvas tests: the real package registration (the AngleSharp sanitizer,
/// so a test proves what the AUTHORITATIVE sanitizer emits) plus an echoing core localizer, so every
/// assertion is culture-independent. The document builders below cover one document per block type —
/// the phase's core requirement is that each block type has a VISUAL renderer, so each one needs its own
/// reachable shape on the canvas.
/// </summary>
public abstract class EmailCanvasTestBase : BunitContext
{
    protected EmailCanvasTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    /// <summary>A document holding the given blocks in a single column of a single section.</summary>
    protected static EmailTemplateDocument DocWith(params EmailBlockBase[] blocks)
    {
        var document = new EmailTemplateDocument();
        var column = new EmailColumn();
        foreach (var block in blocks)
        {
            column.Blocks.Add(block);
        }

        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    /// <summary>Renders the canvas for <paramref name="document"/>.</summary>
    protected IRenderedComponent<TmEmailTemplateCanvas> RenderCanvas(
        EmailTemplateDocument document,
        bool readOnly = false,
        CanvasImageLoading imageLoading = CanvasImageLoading.Load)
        => Render<TmEmailTemplateCanvas>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.ReadOnly, readOnly);
            p.Add(c => c.CanvasImageLoading, imageLoading);
        });

    /// <summary>The rendered body of the first block — the element the visual renderer owns.</summary>
    protected static AngleSharp.Dom.IElement Body(IRenderedComponent<TmEmailTemplateCanvas> cut)
        => cut.Find("[data-tm-block-body]");

    /// <summary>The rendered body of the block with <paramref name="blockId"/>.</summary>
    protected static AngleSharp.Dom.IElement Body(IRenderedComponent<TmEmailTemplateCanvas> cut, Guid blockId)
        => cut.Find($"[data-tm-block-id=\"{blockId}\"] [data-tm-block-body]");

    /// <summary>The inline style of an element, normalised so "<c>a:b</c>" and "<c>a: b</c>" both match.</summary>
    protected static string Style(AngleSharp.Dom.IElement element)
        => (element.GetAttribute("style") ?? string.Empty).Replace(" ", string.Empty);

    /// <summary>A CSS declaration assertion helper: the element's style contains <paramref name="declaration"/>.</summary>
    protected static void ShouldDeclare(AngleSharp.Dom.IElement element, string declaration)
        => Style(element).Should().Contain(declaration.Replace(" ", string.Empty));

    protected sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
}
