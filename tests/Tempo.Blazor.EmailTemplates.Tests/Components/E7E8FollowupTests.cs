using Tempo.Blazor.EmailTemplates.Abstractions;
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

public class E7E8FollowupTests : BunitContext
{
    public E7E8FollowupTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(EmailTemplateDocument doc, Guid? selected)
        => Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, selected).Add(c => c.OnChanged, () => { }));

    // ── E7.3f table grid ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TableBlock_AddRowAndColumn_Mutate()
    {
        var table = new EmailTableBlock();
        var doc = DocWith(table);
        var cut = Panel(doc, table.Id);

        cut.Find("[data-tm-table-add-row]").Click();   // 1 row, 1 cell
        cut.Find("[data-tm-table-add-col]").Click();   // 1 row, 2 cells

        table.Rows.Should().ContainSingle();
        table.Rows[0].Cells.Should().HaveCount(2);
    }

    [Fact]
    public void TableBlock_EditCell_UpdatesModel()
    {
        var table = new EmailTableBlock();
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell());
        table.Rows.Add(row);
        var doc = DocWith(table);
        var cut = Panel(doc, table.Id);

        cut.Find("[data-tm-table-cell] input").Change("Hello");

        table.Rows[0].Cells[0].Text.Should().Be("Hello");
    }

    // ── E7.12 ExtraAttributes ───────────────────────────────────────────────────────────────

    [Fact]
    public void Block_ExtraAttributes_AddRow_StoresAttribute()
    {
        var text = new EmailTextBlock();
        var doc = DocWith(text);
        var cut = Panel(doc, text.Id);

        cut.Find("#tab-style").Click();
        cut.Find("[data-tm-extra] [data-tm-kv-add]").Click();
        var row = cut.Find("[data-tm-extra] [data-tm-kv-row]");
        var inputs = row.QuerySelectorAll("input");
        inputs[0].Change("data-id");
        inputs[1].Change("42");

        text.ExtraAttributes.Should().ContainKey("data-id").WhoseValue.Should().Be("42");
    }

    // ── E7.11 mj-class ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Block_MjClasses_EditField_SplitsToList()
    {
        var text = new EmailTextBlock();
        var doc = DocWith(text);
        var cut = Panel(doc, text.Id);

        cut.Find("#tab-style").Click();
        cut.Find("[data-tm-classes] input").Change("promo big");

        text.MjClasses.Should().ContainInOrder("promo", "big");
    }

    // ── E8.6b JSON export ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ExportDialog_JsonMode_ShowsContentJson()
    {
        var doc = DocWith(new EmailTextBlock { Content = "x" });
        var cut = Render<TmEmailExportDialog>(p => p.Add(c => c.Show, true).Add(c => c.Document, doc));

        cut.Find("[data-tm-export-mode=\"json\"]").Click();

        cut.Find("[data-tm-export-json]").TextContent.Should().Contain("\"$type\"");
    }

    // ── E8.2 variable picker integration ────────────────────────────────────────────────────

    [Fact]
    public void DocumentPanel_VariablePicker_InsertsTokenIntoSubject()
    {
        var doc = new EmailTemplateDocument { Subject = "Hi " };
        var vars = new[] { new TemplateVariableInfo("first_name", VariableKind.Scalar) };

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.Variables, vars).Add(c => c.OnChanged, () => { }));

        cut.Find("[data-tm-variable-trigger=\"Subject\"] .tm-popover__trigger").Click();
        cut.Find("[data-tm-variable=\"first_name\"]").Click();

        doc.Subject.Should().Be("Hi {{ first_name }}");
    }

    // ── E8.7 Scriban per-field validation ───────────────────────────────────────────────────

    [Fact]
    public void ValidationPanel_FlagsScribanSyntaxError()
    {
        var text = new EmailTextBlock { Content = "{{ if broken" };
        var doc = DocWith(text);

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, doc));

        cut.FindAll("[data-tm-validation-severity=\"Error\"]").Should().NotBeEmpty();
    }

    // ── E7.9 shortcuts help ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Editor_HelpButton_ShowsShortcutsOverlay()
    {
        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, new EmailTemplateDocument()));

        cut.FindAll(".tm-keyboard-shortcuts-overlay").Should().BeEmpty();
        cut.Find("[data-tm-help-btn]").Click();
        cut.FindAll(".tm-keyboard-shortcuts-overlay").Should().ContainSingle();
    }


    [Fact]
    public void Preview_EditingSampleData_RerendersWithValues()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "Hi {{ name }}" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var cut = Render<TmEmailTemplatePreview>(p => p
            .Add(c => c.Document, doc).Add(c => c.TimeProvider, time));
        // The sample data editor is behind the "Edit data" button (not a <details> element any more).
        cut.Find("[data-tm-preview-edit-data]").Click();
        cut.Find("[data-tm-preview-data-editor] textarea").Input("{\"name\":\"Ada\"}");

        time.Advance(TimeSpan.FromMilliseconds(750)); // debounced re-render

        cut.WaitForAssertion(() => cut.Find("[data-tm-preview-frame]").GetAttribute("srcdoc").Should().Contain("Hi Ada"));
    }

    // ── E7.11 mj-class definitions manager ──────────────────────────────────────────────────

    [Fact]
    public void DocumentPanel_AddClassDefinition_StoresIt()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.OnChanged, () => { }));

        cut.Find("#tab-style").Click();
        cut.Find("[data-tm-head=\"classes\"] [data-tm-classes-editor] input").Change("cta");
        cut.Find("[data-tm-head=\"classes\"] [data-tm-class-add]").Click();

        doc.Styles.Attributes.Classes.Should().ContainKey("cta");
    }

    // ── E7.12 mj-html-attributes editor ─────────────────────────────────────────────────────

    [Fact]
    public void DocumentPanel_AddHtmlSelector_StoresIt()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.OnChanged, () => { }));

        cut.Find("#tab-style").Click();
        cut.Find("[data-tm-head=\"html-attributes\"] [data-tm-html-add]").Click();

        doc.Styles.HtmlAttributes.Should().ContainSingle();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
