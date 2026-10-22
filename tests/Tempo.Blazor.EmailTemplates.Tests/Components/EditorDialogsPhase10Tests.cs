using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 10 (editor integration): the dialogs take the resolved layout so a forced
/// <see cref="TmLayoutMode.Mobile"/> turns every one of them into the core sheet with a sticky footer (F2),
/// the shortcuts help carries its subtitle and ↑/↓ as TWO alternatives (F8), an import is ONE undo step and
/// a read-only editor can still export but never import.
/// </summary>
public class EditorDialogsPhase10Tests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("en");

    public EditorDialogsPhase10Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
        Services.AddSingleton<Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer>(new EchoEmailLocalizer());
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private static EmailTemplateDocument Doc()
    {
        var document = new EmailTemplateDocument { Subject = "Hello" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = "Hi" });
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    /// <summary>
    /// Activates an editor action on a MOBILE layout: the command row is replaced by the action bar, so the
    /// file/help commands live in the “⋮” overflow (D20) and the four bar actions are the action bar's items.
    /// </summary>
    private static void ClickMobileAction(IRenderedComponent<TmEmailTemplateEditor> cut, string selector)
    {
        if (cut.FindAll(selector).Count == 0)
        {
            cut.Find("[data-tm-mobile-overflow] .tm-popover__trigger").Click();
        }

        cut.WaitForAssertion(() => cut.FindAll(selector).Count.Should().Be(1));
        cut.Find(selector).Click();
    }

    private IRenderedComponent<TmEmailTemplateEditor> RenderEditor(
        EmailTemplateDocument document, TmLayoutMode layout = TmLayoutMode.Desktop, bool readOnly = false)
        => Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.LayoutMode, layout);
            p.Add(c => c.ReadOnly, readOnly);
            p.Add(c => c.OnSave, (Tempo.Blazor.EmailTemplates.Abstractions.Editing.EmailSaveRequest _) =>
                Task.FromResult(Tempo.Blazor.EmailTemplates.Abstractions.Editing.EmailSaveResult.Saved()));
        });

    // ── mobile presentation of every dialog (F2) ────────────────────────────────────────────────

    [Fact]
    public void ExportDialog_IsACoreSheet_OnAForcedMobileLayout()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Mobile);

        ClickMobileAction(cut, "[data-tm-mjml-btn]");

        var dialog = cut.Find("[data-tm-export]").Closest(".tm-modal-overlay");
        dialog!.ClassList.Should().Contain("tm-modal--sheet");
        cut.Find("[data-tm-export-download]").ClassList.Should().Contain("tm-btn");
    }

    [Fact]
    public void ImportDialog_IsACoreSheet_OnAForcedMobileLayout()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Mobile);

        ClickMobileAction(cut, "[data-tm-import-btn]");

        cut.Find("[data-tm-import]").Closest(".tm-modal-overlay")!.ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public void ValidationDialog_IsACoreSheet_OnAForcedMobileLayout_AndHasACloseFooter()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Mobile);

        cut.Find("[data-action-id=\"validate\"]").Click();

        cut.Find("[data-tm-validation]").Closest(".tm-modal-overlay")!.ClassList.Should().Contain("tm-modal--sheet");
        var close = cut.Find("[data-tm-validation-close]");
        close.Click();
        cut.FindAll("[data-tm-validation]").Should().BeEmpty("Close dismisses the dialog");
    }

    [Fact]
    public void PreviewDialog_IsACoreSheet_OnAForcedMobileLayout()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Mobile);

        cut.Find("[data-action-id=\"preview\"]").Click();

        cut.Find("[data-tm-preview]").Closest(".tm-modal-overlay")!.ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public void Dialogs_StayDialogs_OnAForcedDesktopLayout()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Desktop);

        cut.Find("[data-tm-mjml-btn]").Click();

        cut.Find("[data-tm-export]").Closest(".tm-modal-overlay")!.ClassList.Should().NotContain("tm-modal--sheet");
    }

    // ── shortcuts help (F8) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ShortcutsHelp_HasTheExternalKeyboardSubtitle_AndArrowKeysAsTwoAlternatives()
    {
        var cut = RenderEditor(Doc());

        cut.Find("[data-tm-help-btn]").Click();

        cut.Find(".tm-keyboard-shortcuts-subtitle").TextContent.Should().Be("EmailEditor_Shortcuts_Description");

        var keys = cut.FindAll(".tm-keyboard-shortcuts-key").Select(k => k.TextContent.Trim()).ToList();
        keys.Should().Contain(key => key.Contains('↑'), "the arrow alternatives are separate entries, not one chord");
        keys.Should().Contain(key => key.Contains('↓'));
        keys.Should().NotContain(key => key.Contains('↑') && key.Contains('↓'));
    }

    // ── import is one undo step ────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_ReplacesTheDocumentAsOneUndoStep()
    {
        var document = Doc();
        var cut = RenderEditor(document);

        cut.Find("[data-tm-import-btn]").Click();
        cut.Find("[data-tm-import-input] textarea").Input(
            "<mjml><mj-body><mj-section><mj-column><mj-text>Imported</mj-text></mj-column></mj-section></mj-body></mjml>");
        cut.Find("[data-tm-import-confirm]").Click();

        cut.Find("[data-tm-canvas]").TextContent.Should().Contain("Imported");
        cut.FindAll("[data-tm-import]").Should().BeEmpty("a confirmed import closes the dialog");

        cut.Find("[data-tm-undo]").Click();

        cut.Find("[data-tm-canvas]").TextContent.Should().Contain("Hi");
        cut.Find("[data-tm-canvas]").TextContent.Should().NotContain("Imported");
    }

    [Fact]
    public void ReadOnly_DisablesImport_ButKeepsExport()
    {
        var cut = RenderEditor(Doc(), TmLayoutMode.Desktop, readOnly: true);

        cut.Find("[data-tm-import-btn]").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-tm-mjml-btn]").HasAttribute("disabled").Should().BeFalse();

        cut.Find("[data-tm-mjml-btn]").Click();
        cut.Find("[data-tm-export]").Should().NotBeNull();
    }

    // ── “Go to” of a validation finding (UX 10) ────────────────────────────────────────────────

    [Fact]
    public void ValidationGoTo_SelectsTheBlock_AndShowsItOnTheCanvas()
    {
        // An image without alt text is a block-level Warning, so the finding has somewhere to go.
        var document = Doc();
        document.Sections[0].Columns[0].Blocks.Clear();
        var image = new EmailImageBlock { Src = "https://example.com/logo.png" };
        document.Sections[0].Columns[0].Blocks.Add(image);
        var cut = RenderEditor(document);

        cut.Find("[data-tm-validate-btn]").Click();
        var finding = cut.FindAll("[data-tm-validation-message]")
            .Single(f => f.GetAttribute("data-tm-validation-severity") == "Warning");
        finding.Click();

        cut.FindAll("[data-tm-validation]").Should().BeEmpty("the dialog closes on a jump");
        cut.Find($"[data-tm-block-id='{image.Id}']").GetAttribute("aria-current").Should().Be("true",
            "a jump that only closed the dialog was the bug");
    }

    [Fact]
    public void ValidationGoToTheDocument_SelectsTheEmailSettings_WithoutABlock()
    {
        // A document-level finding (the preheader) has no block to jump to; its target is the e-mail settings,
        // i.e. the inspector with nothing selected.
        var document = Doc();
        document.Preheader = new string('p', 140);
        var cut = RenderEditor(document);

        cut.Find("[data-tm-validate-btn]").Click();
        var info = cut.FindAll("[data-tm-validation-message]")
            .Single(f => f.GetAttribute("data-tm-validation-severity") == "Info");
        info.Click();

        cut.FindAll("[data-tm-validation]").Should().BeEmpty();
        cut.Find("[data-tm-properties] [data-tm-prop-target='document']").Should().NotBeNull(
            "the inspector shows the e-mail settings when nothing is selected");
    }

    [Fact]
    public void ValidationDialog_HasASubtitle_AndAGhostClose()
    {
        var cut = RenderEditor(Doc());

        cut.Find("[data-tm-validate-btn]").Click();

        cut.Find("[data-tm-validation-dialog] .tm-modal-subtitle").TextContent
            .Should().Be("EmailEditor_Validation_Subtitle");
        cut.Find("[data-tm-validation-close]").ClassList.Should().Contain("tm-btn-ghost");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "|" + string.Join("|", arguments);
    }
}
