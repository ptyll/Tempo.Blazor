using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class E8ComponentsTests : BunitContext
{
    public E8ComponentsTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplates();
    }

    private static EmailTemplateDocument DocWithText(string content)
    {
        var doc = new EmailTemplateDocument { Subject = "Hi {{ name }}" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = content });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    // ── Variable picker (E8.1) ──────────────────────────────────────────────────────────────

    [Fact]
    public void VariablePicker_ListsVariables_AndInsertsToken()
    {
        string? inserted = null;
        var vars = new[]
        {
            new TemplateVariableInfo("first_name", VariableKind.Scalar),
            new TemplateVariableInfo("orders", VariableKind.Collection),
        };

        var cut = Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, vars)
            .Add(c => c.OnInsert, t => inserted = t));

        cut.FindAll("[data-tm-variable]").Should().HaveCount(2);
        cut.Find("[data-tm-variable=\"first_name\"]").Click();

        inserted.Should().Be("{{ first_name }}");
    }

    // ── Preview (E8.3) ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Preview_RendersSandboxedIframeWithContent()
    {
        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, DocWithText("Welcome aboard")));

        var frame = cut.Find("[data-tm-preview-frame]");
        frame.HasAttribute("sandbox").Should().BeTrue();
        frame.GetAttribute("srcdoc").Should().Contain("Welcome aboard");
    }

    [Fact]
    public void Preview_Warnings_AreShownSeparately_WithTheEngineFallbackText()
    {
        // The test localizer echoes the key, so a diagnostic whose key is unknown to the host must fall
        // back to the engine's English message instead of showing the raw key.
        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Sections.Add(new EmailSection
        {
            Columns =
            {
                new EmailColumn
                {
                    Blocks = { new EmailButtonBlock { Text = "Go", Href = "javascript:alert(1)" } },
                },
            },
        });

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));

        cut.FindAll("[data-tm-preview-errors]").Should().BeEmpty();
        var warnings = cut.Find("[data-tm-preview-warnings]");
        warnings.TextContent.Should().Contain("blocked URL scheme");
        warnings.TextContent.Should().NotContain("render.url.blocked_scheme");
        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().NotContain("javascript:");
    }

    [Fact]
    public void Preview_WithError_ShowsTheErrorAndNoHtml()
    {
        // A message whose markup could not be built is fail-closed (Html = ""), so the preview shows the
        // diagnostic instead of a half-built body.
        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Sections.Add(new EmailSection
        {
            Columns =
            {
                new EmailColumn { Blocks = { new EmailRawBlock { Content = "<mj-column>" } } },
            },
        });

        var cut = Render<TmEmailTemplatePreview>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-preview-errors]").TextContent.Should().NotBeNullOrWhiteSpace();
        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().BeEmpty();
    }

    [Fact]
    public void Preview_TextToggle_ShowsPlainText()
    {
        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, DocWithText("Plain content here")));

        cut.Find("[data-tm-preview-view=\"text\"]").Click();

        cut.Find("[data-tm-preview-text]").TextContent.Should().Contain("Plain content here");
    }

    [Fact]
    public void Preview_UpdatesWhenVariablesJsonChanges()
    {
        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, DocWithText("Hello {{ first_name }}"))
            .Add(c => c.VariablesJson, "{\"first_name\":\"Alice\"}"));

        cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().Contain("Alice");

        // Parent (the send form) supplies fresh values as the user types — the live
        // preview must adopt them, not stay pinned to the first snapshot.
        cut.Render(p => p.Add(c => c.VariablesJson, "{\"first_name\":\"Bob\"}"));

        var srcdoc = cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc");
        srcdoc.Should().Contain("Bob");
        srcdoc.Should().NotContain("Alice");
    }

    // ── MJML export (E8.5) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ExportDialog_ShowsGeneratedMjml()
    {
        var cut = Render<TmEmailExportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.Document, DocWithText("hello")));

        cut.Find("[data-tm-export-mjml]").TextContent.Should().Contain("<mjml");
    }

    // ── MJML import (E8.6) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void ImportDialog_ParsesMjml_AndConfirms()
    {
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.OnImport, d => imported = d));

        const string mjml = "<mjml><mj-body><mj-section><mj-column><mj-text>Imported</mj-text></mj-column></mj-section></mj-body></mjml>";
        cut.Find("[data-tm-import-input]").Input(mjml);
        cut.Find("[data-tm-import-confirm]").Click();

        imported.Should().NotBeNull();
        imported!.Sections.Should().ContainSingle();
    }

    [Fact]
    public void ImportDialog_InvalidMjml_ShowsErrors_NoConfirm()
    {
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true)
            .Add(c => c.OnImport, d => imported = d));

        cut.Find("[data-tm-import-input]").Input("<html>not mjml</html>");

        cut.FindAll("[data-tm-import-errors]").Should().ContainSingle();
        cut.Find("[data-tm-import-confirm]").Click();
        imported.Should().BeNull();
    }

    // ── Validation panel (E8.7) ─────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationPanel_ListsFindings_AndNavigates()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        var button = new EmailButtonBlock { Text = "Go" }; // no href → error
        col.Blocks.Add(button);
        section.Columns.Add(col);
        doc.Sections.Add(section);

        Guid? navigated = null;
        var cut = Render<TmEmailValidationPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.OnNavigate, id => navigated = id));

        cut.FindAll("[data-tm-validation-message]").Should().NotBeEmpty();
        cut.FindAll("[data-tm-validation-message]")
            .First(m => string.Equals(m.GetAttribute("data-tm-validation-severity"), "Error", StringComparison.Ordinal)).Click();

        navigated.Should().Be(button.Id);
    }

    [Fact]
    public void ValidationPanel_ValidDocument_ShowsOk()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "ok" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, doc));

        cut.FindAll("[data-tm-validation-ok]").Should().ContainSingle();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
