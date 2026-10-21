using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Shared fixture for the Fáze 7 save-contract tests (result, dirty state, single-flight, ReadOnly,
/// toolbar/status). The package registers its own <c>ITmEmailLocalizer</c> over the embedded JSON
/// tables; only the core <see cref="ITmLocalizer"/> is echoed (core component chrome is irrelevant here).
/// </summary>
public abstract class EmailEditorTestBase : BunitContext
{
    protected EmailEditorTestBase()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
        // Echo the package's own localizer so the status assertions are key-based and culture-independent;
        // EditorSaveStatusLocalizationTests renders the REAL tables per culture.
        Services.AddSingleton<Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer>(new EchoEmailLocalizer());
    }

    /// <summary>A document with a single text block, so the editor has something to select and mutate.</summary>
    protected static EmailTemplateDocument DocWithTextBlock(out Guid blockId)
    {
        var document = new EmailTemplateDocument { Subject = "Hello" };
        var section = new EmailSection();
        var column = new EmailColumn();
        var block = new EmailTextBlock { Content = "Hi" };
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        blockId = block.Id;
        return document;
    }

    /// <summary>Renders the editor with a host save delegate that always answers <paramref name="result"/>.</summary>
    protected IRenderedComponent<TmEmailTemplateEditor> RenderEditor(
        EmailTemplateDocument document,
        EmailSaveResult result,
        Action<EmailSaveRequest>? onRequest = null,
        TimeProvider? timeProvider = null,
        AutoSaveOptions? autoSave = null,
        bool readOnly = false)
        => Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.OnSave, (EmailSaveRequest request) =>
            {
                onRequest?.Invoke(request);
                return Task.FromResult(result);
            });
            if (timeProvider is not null) p.Add(c => c.TimeProvider, timeProvider);
            if (autoSave is not null) p.Add(c => c.AutoSave, autoSave);
            if (readOnly) p.Add(c => c.ReadOnly, true);
        });

    /// <summary>The save badge state attribute, or <c>null</c> when no badge is rendered.</summary>
    protected static string? BadgeState(IRenderedComponent<TmEmailTemplateEditor> cut)
        => cut.FindAll("[data-tm-save-badge]").FirstOrDefault()?.GetAttribute("data-tm-save-state");

    /// <summary>The badge's visible text (label plus a host message when present).</summary>
    protected static string BadgeText(IRenderedComponent<TmEmailTemplateEditor> cut)
        => cut.Find("[data-tm-save-badge]").TextContent;

    protected sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        // Arguments are echoed too: a test can prove that a formatted value (the relative "saved" time)
        // reached the text instead of only that the key resolved.
        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }

    protected sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        // Arguments are echoed too, so a test can prove that a formatted value (a relative time) reached
        // the text instead of only that the key resolved.
        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
}
