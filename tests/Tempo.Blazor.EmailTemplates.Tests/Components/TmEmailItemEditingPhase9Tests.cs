using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9 — the dialog item-editing mode (<see cref="EmailItemEditingMode.Dialog"/>) and the table-cell
/// dialog. The list of items stays compact (preview + Upravit / Odebrat / přesun) and the editor of a copy
/// opens in a <c>TmModal</c>: cancel/Escape/overlay leave the document untouched, Save applies the copy as
/// ONE change (one undo step) and nothing invalid can be saved — a blocked URL scheme, a missing required
/// value or a colour outside the allow-list keeps the Save button disabled with a localized message.
/// <para>
/// bUnit echoes the localization keys (see <see cref="EchoEmailLocalizer"/>), so the assertions are
/// culture- and text-independent; the real four tables are covered by
/// <c>TmEmailResourcesParityTests</c> and the E2E localization spec.
/// </para>
/// </summary>
public class TmEmailItemEditingPhase9Tests : BunitContext, IDisposable
{
    public TmEmailItemEditingPhase9Tests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
        // Echo the editor's own localizer so the assertions are key-based: the REAL four tables are covered
        // by TmEmailResourcesParityTests and the E2E localization spec, not by every component test.
        Services.AddSingleton<Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer>(new EchoEmailLocalizer());
    }

    void IDisposable.Dispose() => Dispose();

    // ── Fixtures ─────────────────────────────────────────────────────────────────────────────

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj" };
        var section = new EmailSection();
        var column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private static EmailSocialBlock SocialBlock(params EmailSocialElement[] elements)
    {
        var block = new EmailSocialBlock();
        foreach (var element in elements) block.Elements.Add(element);
        return block;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(
        EmailTemplateDocument document,
        Guid selected,
        EmailItemEditingMode mode = EmailItemEditingMode.Inline,
        bool readOnly = false,
        Action? onChanged = null)
        => Render<TmEmailPropertyPanel>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.SelectedId, selected);
            p.Add(c => c.ItemEditingMode, mode);
            p.Add(c => c.ReadOnly, readOnly);
            p.Add(c => c.OnChanged, () => onChanged?.Invoke());
        });

    private IRenderedComponent<TmEmailTableEditor> TableEditor(
        EmailTableBlock block, bool readOnly = false, Action? onChanged = null)
        => Render<TmEmailTableEditor>(p =>
        {
            p.Add(c => c.Block, block);
            p.Add(c => c.ReadOnly, readOnly);
            p.Add(c => c.OnChanged, () => onChanged?.Invoke());
        });

    private static EmailTableBlock Table(int rows, int columns)
    {
        var table = new EmailTableBlock();
        for (var r = 0; r < rows; r++)
        {
            var row = new EmailTableRow { IsHeader = r == 0 };
            for (var c = 0; c < columns; c++) row.Cells.Add(new EmailTableCell { Text = $"r{r}c{c}" });
            table.Rows.Add(row);
        }

        return table;
    }

    private static IReadOnlyList<string> ErrorKeys(IRenderedComponent<IComponent> cut)
        => cut.FindAll("[data-tm-item-dialog-error]").Select(e => e.TextContent.Trim()).ToList();

    private static bool SaveDisabled(IRenderedComponent<IComponent> cut)
        => cut.Find("[data-tm-item-dialog-save]").HasAttribute("disabled");

    /// <summary>
    /// Clicks Save and answers whether the form REFUSED it (the dialog is still open). The button is always
    /// enabled (review F9), so "cannot be saved" is proven by what Save does, not by a disabled attribute.
    /// </summary>
    private static bool SaveRefused(IRenderedComponent<IComponent> cut)
    {
        cut.Find("[data-tm-item-dialog-save]").Click();
        return cut.FindAll("[data-tm-item-dialog]").Count > 0;
    }

    /// <summary>The message a text field shows itself (<c>TmTextInput.Error</c>), or null.</summary>
    private static string? FieldMessage(IRenderedComponent<IComponent> cut, string property)
        => cut.FindAll($"[data-tm-item-field='{property}'] .tm-input-error-message")
            .FirstOrDefault()?.TextContent.Trim();

    // ── The mode parameter ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Inline_mode_is_the_default_and_keeps_the_inline_item_editor()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id);

        cut.FindAll("[data-tm-list-item]").Should().ContainSingle();
        cut.FindAll("[data-tm-item-list]").Should().BeEmpty();
    }

    [Fact]
    public void Dialog_mode_renders_the_compact_item_list_with_edit_remove_and_move()
    {
        var social = SocialBlock(
            new EmailSocialElement { Name = "facebook", Href = "https://facebook.com", Label = "FB" },
            new EmailSocialElement { Name = "instagram", Href = "https://instagram.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog);

        cut.Find("[data-tm-item-list]").Should().NotBeNull();
        cut.FindAll("[data-tm-item]").Should().HaveCount(2);
        cut.FindAll("[data-tm-item-edit]").Should().HaveCount(2);
        cut.FindAll("[data-tm-item-remove]").Should().HaveCount(2);
        cut.FindAll("[data-tm-item-up]").Should().HaveCount(2);
        cut.FindAll("[data-tm-item-down]").Should().HaveCount(2);
        // The compact row shows a preview, not the full set of inputs.
        cut.FindAll("[data-tm-item-preview]").Should().HaveCount(2);
        cut.FindAll("[data-tm-item-list] input").Should().BeEmpty(
            "the dialog mode replaces the inline inputs with a preview");
    }

    [Fact]
    public void Dialog_mode_keeps_the_inline_table_grid_and_the_cell_dialog_is_reachable()
    {
        var table = Table(2, 2);
        var document = DocWith(table);

        var cut = Panel(document, table.Id, EmailItemEditingMode.Dialog);

        cut.FindAll("[data-tm-table-editor]").Should().ContainSingle();
        cut.FindAll("[data-tm-table-cell-edit]").Should().HaveCount(4);
    }

    // ── The dialog: copy semantics ───────────────────────────────────────────────────────────

    [Fact]
    public void Editing_an_item_changes_only_the_copy_until_save()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);
        var changes = 0;

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog, onChanged: () => changes++);
        cut.Find("[data-tm-item-edit]").Click();

        cut.Find("[data-tm-item-dialog]").Should().NotBeNull();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/new");

        social.Elements[0].Href.Should().Be("https://facebook.com", "the document is edited only on Save");
        changes.Should().Be(0);
    }

    [Fact]
    public void Cancel_closes_the_dialog_and_leaves_the_document_untouched()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);
        var changes = 0;

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog, onChanged: () => changes++);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/new");
        cut.Find("[data-tm-item-dialog-cancel]").Click();

        cut.FindAll("[data-tm-item-dialog]").Should().BeEmpty();
        social.Elements[0].Href.Should().Be("https://facebook.com");
        changes.Should().Be(0);
    }

    [Fact]
    public void Closing_the_modal_with_its_own_close_button_cancels()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);
        var changes = 0;

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog, onChanged: () => changes++);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/new");
        cut.Find(".tm-modal-close").Click();

        social.Elements[0].Href.Should().Be("https://facebook.com");
        changes.Should().Be(0);
    }

    [Fact]
    public void Save_applies_the_copy_as_one_change_and_closes_the_dialog()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);
        var changes = 0;

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog, onChanged: () => changes++);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/new");
        cut.Find("[data-tm-item-field=\"Label\"] input").Change("Nový");
        cut.Find("[data-tm-item-dialog-save]").Click();

        cut.FindAll("[data-tm-item-dialog]").Should().BeEmpty();
        social.Elements[0].Href.Should().Be("https://example.com/new");
        social.Elements[0].Label.Should().Be("Nový");
        changes.Should().Be(1, "Save is ONE undo step, so the panel reports one change");
    }

    [Fact]
    public void The_dialog_reopens_with_the_current_values_of_the_item()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/new");
        cut.Find("[data-tm-item-dialog-save]").Click();

        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").GetAttribute("value")
            .Should().Be("https://example.com/new");
    }

    // ── Validation ──────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,x")]
    [InlineData("//evil.example.com/x")]
    [InlineData("vbscript:msgbox(1)")]
    public void An_item_with_a_blocked_link_scheme_cannot_be_saved(string href)
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change(href);

        // The message appears at the field as soon as the value is typed (review F9).
        cut.Find("[data-tm-item-field=\"Href\"] input").HasAttribute("aria-invalid").Should().BeTrue();
        FieldMessage((IRenderedComponent<IComponent>)cut, "Href").Should().Be("EmailEditor_Item_Error_UrlScheme");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();
        social.Elements[0].Href.Should().Be("https://facebook.com", "a refused save changes nothing");
    }

    [Fact]
    public void A_custom_social_network_requires_its_own_icon_url()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse("Save is always enabled");

        // "Vlastní" (custom) has no built-in Mjml.Net icon, so Src becomes mandatory.
        cut.Find("[data-tm-item-field=\"Name\"] select").Change(string.Empty);
        // Src was not touched, so its message waits for the save attempt (review F9).
        FieldMessage((IRenderedComponent<IComponent>)cut, "Src").Should().BeNull();
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();
        FieldMessage((IRenderedComponent<IComponent>)cut, "Src").Should().Be("EmailEditor_Item_Error_SrcRequired");
        ErrorKeys(cut).Should().BeEmpty("the failure belongs to the Src field, not to the summary");

        cut.Find("[data-tm-item-field=\"Src\"] input").Change("https://example.com/icon.png");
        FieldMessage((IRenderedComponent<IComponent>)cut, "Src").Should().BeNull();
        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-item-dialog]").Should().BeEmpty();
    }

    [Fact]
    public void A_navbar_link_requires_a_text_and_a_href()
    {
        var navbar = new EmailNavbarBlock();
        navbar.Links.Add(new EmailNavbarLink { Text = "Home", Href = "https://example.com" });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse("Save is always enabled");

        cut.Find("[data-tm-item-field=\"Text\"] input").Change(string.Empty);
        FieldMessage((IRenderedComponent<IComponent>)cut, "Text").Should().Be("EmailEditor_Item_Error_TextRequired");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();

        cut.Find("[data-tm-item-field=\"Text\"] input").Change("Home");
        cut.Find("[data-tm-item-field=\"Href\"] input").Change(string.Empty);
        FieldMessage((IRenderedComponent<IComponent>)cut, "Href").Should().Be("EmailEditor_Item_Error_HrefRequired");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();
        navbar.Links[0].Href.Should().Be("https://example.com");
    }

    [Fact]
    public void A_carousel_image_requires_a_src_and_an_alt()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "https://example.com/a.png", Alt = "A" });
        var document = DocWith(carousel);

        var cut = Panel(document, carousel.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse("Save is always enabled");

        cut.Find("[data-tm-item-field=\"Alt\"] input").Change(string.Empty);
        FieldMessage((IRenderedComponent<IComponent>)cut, "Alt").Should().Be("EmailEditor_Item_Error_AltRequired");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();

        cut.Find("[data-tm-item-field=\"Alt\"] input").Change("A");
        cut.Find("[data-tm-item-field=\"Src\"] input").Change(string.Empty);
        FieldMessage((IRenderedComponent<IComponent>)cut, "Src").Should().Be("EmailEditor_Item_Error_SrcRequired");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();
        carousel.Images[0].Src.Should().Be("https://example.com/a.png");
    }

    [Fact]
    public void An_accordion_item_requires_a_title_and_sanitizes_its_content()
    {
        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem { Title = "Otázka", Content = "<p>Odpověď</p>" });
        var document = DocWith(accordion);

        var cut = Panel(document, accordion.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse("Save is always enabled");

        cut.Find("[data-tm-item-field=\"Title\"] input").Change(string.Empty);
        FieldMessage((IRenderedComponent<IComponent>)cut, "Title").Should().Be("EmailEditor_Item_Error_TitleRequired");
        SaveRefused((IRenderedComponent<IComponent>)cut).Should().BeTrue();

        cut.Find("[data-tm-item-field=\"Title\"] input").Change("Otázka");
        // The rich editor is a contenteditable surface driven by JS, so the test drives its callback: the
        // pasted payload is what the sanitizer must strip on Save.
        cut.InvokeAsync(() => cut.FindComponent<TmRichEditorSimple>().Instance.ValueChanged
            .InvokeAsync("<p>Odpověď</p><script>alert(1)</script><p onclick=\"evil()\">x</p>"));
        cut.Find("[data-tm-item-dialog-save]").Click();

        var saved = accordion.Items[0].Content;
        saved.Should().NotContain("<script");
        saved.Should().NotContain("onclick");
        saved.Should().Contain("Odpověď");
    }

    // ── The table cell dialog ───────────────────────────────────────────────────────────────

    [Fact]
    public void Edit_cell_opens_the_cell_dialog_for_the_clicked_cell()
    {
        var table = Table(2, 2);
        var document = DocWith(table);

        var cut = Panel(document, table.Id);
        cut.FindAll("[data-tm-table-cell-edit]")[2].Click();

        cut.Find("[data-tm-table-cell-dialog]").Should().NotBeNull();
        var dialog = cut.FindComponent<TmEmailTableCellDialog>();
        dialog.Instance.RowIndex.Should().Be(1);
        dialog.Instance.CellIndex.Should().Be(0);
    }

    private IRenderedComponent<TmEmailTableCellDialog> CellDialog(
        EmailTableBlock table, int row, int column, Action? onSave = null)
        => Render<TmEmailTableCellDialog>(p =>
        {
            p.Add(c => c.Block, table);
            p.Add(c => c.RowIndex, row);
            p.Add(c => c.CellIndex, column);
            p.Add(c => c.Show, true);
            p.Add(c => c.OnSave, () => onSave?.Invoke());
        });

    [Fact]
    public void The_cell_dialog_edits_text_link_spans_alignment_colour_and_the_header_row()
    {
        var table = Table(2, 2);
        var saves = 0;

        var cut = CellDialog(table, 0, 0, () => saves++);
        cut.Find("[data-tm-item-field=\"Text\"] input").Change("Docs");
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("https://example.com/docs");
        cut.Find("[data-tm-item-field=\"ColSpan\"] input").Change("2");
        cut.Find("[data-tm-item-field=\"RowSpan\"] input").Change("2");
        cut.Find("[data-tm-item-field=\"Align\"] select").Change("center");
        cut.InvokeAsync(() => cut.FindComponent<TmColorPicker>().Instance.ValueChanged.InvokeAsync("#eeeeee"));
        cut.Find("[data-tm-table-cell-header] input").Change(true);
        cut.Find("[data-tm-item-dialog-save]").Click();

        var cell = table.Rows[0].Cells[0];
        cell.Text.Should().Be("Docs");
        cell.Href.Should().Be("https://example.com/docs");
        cell.ColSpan.Should().Be(2);
        cell.RowSpan.Should().Be(2);
        cell.Align.Should().Be("center");
        cell.BackgroundColor.Should().Be("#eeeeee");
        table.Rows[0].IsHeader.Should().BeTrue();
        saves.Should().Be(1, "Save is one change (one undo step)");
    }

    [Theory]
    [InlineData("expression(alert(1))")]
    [InlineData("url(https://evil.example.com/x)")]
    [InlineData("#fff\" onmouseover=\"alert(1)")]
    [InlineData("rgb(1,2,3)")]
    public void The_cell_dialog_refuses_a_colour_outside_the_allow_list(string color)
    {
        var table = Table(1, 2);

        var cut = CellDialog(table, 0, 0);
        cut.InvokeAsync(() => cut.FindComponent<TmColorPicker>().Instance.ValueChanged.InvokeAsync(color));

        cut.FindAll("[data-tm-item-field-error]").Select(e => e.TextContent.Trim())
            .Should().Contain("EmailEditor_Item_Error_InvalidColor",
                "a colour picker cannot describe itself, so the message is printed next to it");
        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-table-cell-dialog]").Should().NotBeEmpty("an invalid colour is never applied");
        // Nothing was written to the table.
        table.Rows[0].Cells[0].BackgroundColor.Should().BeNull();
    }

    [Fact]
    public void The_cell_dialog_refuses_a_span_that_is_not_a_number()
    {
        var table = Table(1, 2);

        var cut = CellDialog(table, 0, 0);
        cut.Find("[data-tm-item-field=\"ColSpan\"] input").Change("two");

        FieldMessage((IRenderedComponent<IComponent>)cut, "ColSpan")
            .Should().Be("EmailEditor_Item_Error_InvalidSpan");
        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-table-cell-dialog]").Should().NotBeEmpty();
        table.Rows[0].Cells[0].ColSpan.Should().BeNull();
    }

    [Fact]
    public void The_cell_dialog_previews_the_whole_table_and_highlights_the_edited_cell()
    {
        var table = Table(2, 2);

        var cut = CellDialog(table, 1, 1);

        var preview = cut.Find("[data-tm-table-cell-preview]");
        preview.QuerySelectorAll("[data-tm-table-preview-cell]").Should().HaveCount(4);
        preview.QuerySelectorAll("[data-tm-table-preview-cell][data-tm-preview-active=\"true\"]")
            .Should().ContainSingle()
            .Which.GetAttribute("data-tm-table-preview-cell").Should().Be("1-1");
    }

    [Fact]
    public void The_cell_dialog_preview_follows_the_edited_text_live()
    {
        var table = Table(2, 2);

        var cut = CellDialog(table, 0, 1);
        cut.Find("[data-tm-item-field=\"Text\"] input").Change("Živě");

        cut.Find("[data-tm-table-preview-cell][data-tm-preview-active=\"true\"]").TextContent
            .Should().Be("Živě");
    }

    [Fact]
    public void The_cell_dialog_cancel_leaves_the_table_untouched()
    {
        var table = Table(2, 2);
        var document = DocWith(table);

        var cut = Panel(document, table.Id);
        cut.FindAll("[data-tm-table-cell-edit]")[1].Click();
        cut.Find("[data-tm-item-field=\"Text\"] input").Change("Změna");
        cut.Find("[data-tm-item-dialog-cancel]").Click();

        table.Rows[0].Cells[1].Text.Should().Be("r0c1");
    }

    [Fact]
    public void The_cell_dialog_refuses_a_blocked_link_scheme()
    {
        var table = Table(1, 2);
        var document = DocWith(table);

        var cut = Panel(document, table.Id);
        cut.FindAll("[data-tm-table-cell-edit]")[0].Click();
        cut.Find("[data-tm-item-field=\"Href\"] input").Change("javascript:alert(1)");

        cut.Find("[data-tm-item-field=\"Href\"] input").HasAttribute("aria-invalid").Should().BeTrue();
        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-table-cell-dialog]").Should().NotBeEmpty();
        table.Rows[0].Cells[0].Href.Should().BeNull();
    }

    // ── The inline table grid (a11y + sections) ─────────────────────────────────────────────

    [Fact]
    public void Every_cell_has_a_localized_accessible_name_and_its_own_edit_button()
    {
        var table = Table(2, 3);

        var cut = TableEditor(table);

        cut.Find("caption").TextContent.Trim().Should().Be("EmailEditor_Table_Caption");
        var cells = cut.FindAll("[data-tm-table-cell]");
        cells.Should().HaveCount(6);
        cells[0].GetAttribute("aria-label").Should().Be("EmailEditor_Table_CellLabel[1,1]");
        cells[5].GetAttribute("aria-label").Should().Be("EmailEditor_Table_CellLabel[2,3]");
        var edits = cut.FindAll("[data-tm-table-cell-edit]");
        edits.Should().HaveCount(6);
        edits[4].GetAttribute("aria-label").Should().Be("EmailEditor_Table_CellEditLabel[2,2]");
    }

    [Fact]
    public void The_header_row_is_visually_distinguished()
    {
        var table = Table(2, 2);

        var cut = TableEditor(table);

        cut.FindAll("[data-tm-table-row]")[0].ClassList.Should().Contain("tm-email-table-editor__row--header");
        cut.FindAll("[data-tm-table-row]")[1].ClassList.Should().NotContain("tm-email-table-editor__row--header");
    }

    [Fact]
    public void The_header_section_toggles_the_first_row()
    {
        var table = Table(2, 2);
        table.Rows[0].IsHeader = false;
        var changes = 0;

        var cut = TableEditor(table, onChanged: () => changes++);

        var toggle = cut.Find("[data-tm-table-first-row-header] input");
        toggle.Change(true);

        table.Rows[0].IsHeader.Should().BeTrue();
        changes.Should().Be(1);
    }

    [Fact]
    public void The_rows_and_columns_section_keeps_the_add_and_remove_actions()
    {
        var table = Table(1, 1);

        var cut = TableEditor(table);

        cut.Find("[data-tm-table-section=\"rows-columns\"]").Should().NotBeNull();
        cut.Find("[data-tm-table-add-row]").Click();
        table.Rows.Should().HaveCount(2);
        cut.Find("[data-tm-table-add-col]").Click();
        table.Rows[0].Cells.Should().HaveCount(2);
        cut.Find("[data-tm-table-remove-col]").Click();
        table.Rows[0].Cells.Should().HaveCount(1);
    }

    [Fact]
    public void Removing_a_row_keeps_the_edit_affordance_of_the_remaining_rows()
    {
        var table = Table(2, 2);

        var cut = TableEditor(table);

        cut.FindAll("[data-tm-table-row-remove]")[0].Click();

        table.Rows.Should().HaveCount(1);
        cut.FindAll("[data-tm-table-cell-edit]").Should().HaveCount(2);
    }

    // ── ReadOnly ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ReadOnly_disables_the_item_edit_affordances_and_the_cell_dialog_save()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var socialDoc = DocWith(social);

        var socialCut = Panel(socialDoc, social.Id, EmailItemEditingMode.Dialog, readOnly: true);
        socialCut.Find("[data-tm-item-edit]").HasAttribute("disabled").Should().BeTrue();
        socialCut.Find("[data-tm-item-remove]").HasAttribute("disabled").Should().BeTrue();
        socialCut.Find("[data-tm-item-up]").HasAttribute("disabled").Should().BeTrue();
        socialCut.Find("[data-tm-item-down]").HasAttribute("disabled").Should().BeTrue();

        var table = Table(1, 1);
        var tableCut = TableEditor(table, readOnly: true);
        tableCut.Find("[data-tm-table-cell-edit]").HasAttribute("disabled").Should().BeTrue();
        tableCut.Find("[data-tm-table-first-row-header] input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void ReadOnly_table_cell_inputs_stay_disabled()
    {
        var table = Table(1, 1);

        var cut = TableEditor(table, readOnly: true);

        cut.Find("[data-tm-table-cell] input").HasAttribute("disabled").Should().BeTrue();
    }

    // ── The modal is the core one (no local sheet) ───────────────────────────────────────────

    [Fact]
    public void The_item_dialog_uses_the_core_modal_with_the_mobile_sheet_presentation()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Panel(document, social.Id, EmailItemEditingMode.Dialog);
        cut.Find("[data-tm-item-edit]").Click();

        var modal = cut.FindComponents<TmModal>().Should().ContainSingle().Subject;
        modal.Instance.MobilePresentation.Should().Be(MobilePresentation.Auto,
            "a phone renders the core modal sheet, not a second local sheet implementation");
        cut.FindAll(".tm-modal-overlay").Should().ContainSingle();
        cut.FindAll(".tm-sheet").Should().BeEmpty("the dialog must not ship its own sheet markup");
    }

    // ── The editor passes the mode through ──────────────────────────────────────────────────

    [Fact]
    public void The_editor_forwards_ItemEditingMode_to_the_inspector()
    {
        var social = SocialBlock(new EmailSocialElement { Name = "facebook", Href = "https://facebook.com" });
        var document = DocWith(social);

        var cut = Render<TmEmailTemplateEditor>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.ItemEditingMode, EmailItemEditingMode.Dialog);
        });

        cut.FindComponents<TmEmailPropertyPanel>().Should().ContainSingle()
            .Which.Instance.ItemEditingMode.Should().Be(EmailItemEditingMode.Dialog);
    }

    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
}
