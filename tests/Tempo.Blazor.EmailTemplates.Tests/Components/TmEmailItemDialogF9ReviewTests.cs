using Bunit;
using FluentValidation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Activity;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Components;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 9 review — the fixes the code+arch and the UX review asked for, each guarded by the behaviour it
/// broke before:
/// <list type="bullet">
/// <item><b>Stored XSS</b>: the rich-text content of an accordion item and of a text block reaches
/// <c>tmRichEditor.setHtml</c> (i.e. <c>innerHTML</c>) sanitized, and fail-closed when no sanitizer is
/// registered;</item>
/// <item><b>validation at the field</b>: nothing is pre-flagged, the Save button stays enabled, a failed save
/// shows the message at the offending field (<c>aria-invalid</c> + <c>aria-describedby</c>) and moves the
/// focus there, and only field-less failures reach the role=alert summary;</item>
/// <item><b>a new item is a copy</b>: Add opens the dialog and the item is inserted only on Save, so Cancel
/// leaves the document and the undo history alone;</item>
/// <item><b>a11y</b>: every row action names its item, an empty list says so, a removal is announced, the
/// table caption is screen-reader only and the pinned column carries no visible toggle label;</item>
/// <item><b>colour policy</b>: MJML attribute colours (navbar, accordion, social) follow the inspector's rule
/// and accept <c>rgba()</c>; only the table cell's <c>bgcolor</c> uses the narrow allow-list.</item>
/// </list>
/// </summary>
public class TmEmailItemDialogF9ReviewTests : EmailEditorTestBase, IDisposable
{
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

    private static EmailNavbarBlock Navbar(params EmailNavbarLink[] links)
    {
        var block = new EmailNavbarBlock();
        foreach (var link in links) block.Links.Add(link);
        return block;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(
        EmailTemplateDocument document,
        Guid selected,
        bool readOnly = false,
        Action? onChanged = null)
        => Render<TmEmailPropertyPanel>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.SelectedId, selected);
            p.Add(c => c.ItemEditingMode, EmailItemEditingMode.Dialog);
            p.Add(c => c.ReadOnly, readOnly);
            p.Add(c => c.OnChanged, () => onChanged?.Invoke());
        });

    private IRenderedComponent<TmEmailTableEditor> TableEditor(EmailTableBlock table)
        => Render<TmEmailTableEditor>(p => p.Add(c => c.Block, table));

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

    /// <summary>The HTML the rich editor was told to write into its contenteditable surface.</summary>
    private IEnumerable<string?> RichEditorHtml()
        => JSInterop.Invocations
            .Where(i => i.Identifier == "tmRichEditor.setHtml")
            .Select(i => i.Arguments.Count > 1 ? i.Arguments[1]?.ToString() : null);

    /// <summary>
    /// The message shown inside one field. A text input renders it itself (<c>TmTextInput.Error</c>, which
    /// also feeds <c>aria-describedby</c>); a select or a colour picker gets the message printed next to it.
    /// </summary>
    private static string? FieldErrorText(IRenderedComponent<IComponent> cut, string property)
    {
        var own = cut.FindAll($"[data-tm-item-field='{property}'] .tm-input-error-message").FirstOrDefault();
        var beside = cut.FindAll($"[data-tm-item-field='{property}'] [data-tm-item-field-error]").FirstOrDefault();
        return (own ?? beside)?.TextContent.Trim();
    }

    private static bool InputInvalid(IRenderedComponent<IComponent> cut, string property)
        => cut.Find($"[data-tm-item-field='{property}'] input").HasAttribute("aria-invalid");

    private static bool SaveDisabled(IRenderedComponent<IComponent> cut)
        => cut.Find("[data-tm-item-dialog-save]").HasAttribute("disabled");

    // ── Stored XSS: the sinks of the rich editor ────────────────────────────────────────────

    [Fact]
    public void Accordion_content_is_sanitized_before_it_reaches_the_rich_editor()
    {
        const string payload = "<img src=x onerror=alert(1)>{{ name }}";
        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem { Title = "Otázka", Content = payload });
        var document = DocWith(accordion);

        var cut = Panel(document, accordion.Id);
        cut.Find("[data-tm-item-edit]").Click();

        var html = RichEditorHtml().LastOrDefault();
        html.Should().NotBeNull("the dialog renders the rich editor over the copy of the accordion content");
        html.Should().NotContain("onerror", "the item content came from an import/JSON, so it is sanitized "
            + "before the editor hands it to innerHTML");
        html.Should().Contain("{{ name }}", "a template token is content, not markup, and must survive");
    }

    [Fact]
    public void Text_block_content_is_sanitized_before_it_reaches_the_rich_editor()
    {
        const string payload = "<p>Hi</p><img src=x onerror=alert(1)>{{ name }}";
        var block = new EmailTextBlock { Content = payload };
        var document = DocWith(block);

        var cut = Panel(document, block.Id);

        var html = RichEditorHtml().LastOrDefault();
        html.Should().NotBeNull();
        html.Should().NotContain("onerror");
        html.Should().Contain("{{ name }}");
        html.Should().Contain("Hi");
    }

    [Fact]
    public void Without_a_sanitizer_the_rich_editor_receives_nothing_instead_of_the_raw_content()
    {
        // Fail-closed: an editor that cannot sanitize must not hand the raw value to innerHTML.
        Services.RemoveAll<IHtmlContentSanitizer>();

        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem
        {
            Title = "Otázka",
            Content = "<img src=x onerror=alert(1)>{{ name }}",
        });
        var document = DocWith(accordion);

        var cut = Panel(document, accordion.Id);
        cut.Find("[data-tm-item-edit]").Click();

        // The sink is emptied: the editor may not be written at all (an empty value is the DOM's initial
        // state), but nothing from the payload may ever reach innerHTML.
        RichEditorHtml().Should().NotContain(html => !string.IsNullOrEmpty(html),
            "without IHtmlContentSanitizer the sink is emptied, never the raw markup");
    }

    // ── Validation at the field ─────────────────────────────────────────────────────────────

    [Fact]
    public void A_freshly_opened_dialog_shows_no_validation_message()
    {
        // A navbar link has both Text and Href required, so the item is invalid from the start — yet nothing
        // may be flagged before the user did anything.
        var navbar = Navbar(new EmailNavbarLink { Text = string.Empty, Href = string.Empty });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);
        cut.Find("[data-tm-item-edit]").Click();

        cut.FindAll("[data-tm-item-dialog-error]").Should().BeEmpty();
        cut.FindAll("[data-tm-item-field-error]").Should().BeEmpty();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse(
            "the Save button is always enabled — a disabled button never explains what is wrong");
    }

    [Fact]
    public void A_failed_save_shows_the_errors_at_their_fields_and_keeps_the_dialog_open()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = string.Empty, Href = string.Empty });
        var document = DocWith(navbar);
        var changes = 0;

        var cut = Panel(document, navbar.Id, onChanged: () => changes++);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-dialog-save]").Click();

        cut.FindAll("[data-tm-item-dialog]").Should().NotBeEmpty("an invalid form cannot be saved");
        InputInvalid((IRenderedComponent<IComponent>)cut, "Text").Should().BeTrue();
        InputInvalid((IRenderedComponent<IComponent>)cut, "Href").Should().BeTrue();
        cut.Find("[data-tm-item-field='Href'] input").GetAttribute("aria-describedby")
            .Should().NotBeNullOrEmpty("the field message is described by the control");
        cut.FindAll("[data-tm-item-dialog-error]").Should().BeEmpty(
            "the summary is reserved for failures without a field");
        changes.Should().Be(0);
    }

    [Fact]
    public void A_blocked_link_is_reported_at_the_href_field_as_soon_as_it_is_typed()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = "Home", Href = "https://example.com" });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-field='Href'] input").Change("javascript:alert(1)");

        InputInvalid((IRenderedComponent<IComponent>)cut, "Href").Should().BeTrue();
        cut.Find("[data-tm-item-field='Href'] input").GetAttribute("aria-describedby").Should().NotBeNull();
        FieldErrorText((IRenderedComponent<IComponent>)cut, "Href")
            .Should().Be(EmailItemValidationKeys.UrlScheme);
        // The schema message is localized in the package's table, so it is the dialog that explains it.
        EmailItemValidationKeys.UrlScheme.Should().Be("EmailEditor_Item_Error_UrlScheme");
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse();

        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-item-dialog]").Should().NotBeEmpty();
        navbar.Links[0].Href.Should().Be("https://example.com", "a blocked link is never applied");
    }

    [Fact]
    public void A_failed_save_moves_the_focus_to_the_first_invalid_field()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = string.Empty, Href = string.Empty });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-dialog-save]").Click();

        JSInterop.Invocations.Should().Contain(
            invocation => invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase),
            "the first invalid field takes the focus, so the message is reached and announced");
    }

    [Fact]
    public void A_field_less_failure_reaches_the_alert_summary()
    {
        // With no validator registered nothing can be checked, so the form must not save and the reason
        // cannot belong to a field: it is the dialog's role=alert summary.
        var navbar = Navbar(new EmailNavbarLink { Text = "Home", Href = "https://example.com" });
        var document = DocWith(navbar);

        Services.RemoveAll<IValidator<EmailNavbarLink>>();

        var cut = Panel(document, navbar.Id);
        cut.Find("[data-tm-item-edit]").Click();
        cut.Find("[data-tm-item-dialog-save]").Click();

        cut.FindAll("[data-tm-item-dialog]").Should().NotBeEmpty();
        cut.Find("[data-tm-item-dialog-errors]").GetAttribute("role").Should().Be("alert");
        cut.FindAll("[data-tm-item-dialog-error]").Should().NotBeEmpty();
        cut.FindAll("[data-tm-item-field-error]").Should().BeEmpty(
            "the failure belongs to no field, so it cannot be shown at one");
    }

    [Fact]
    public void The_cell_dialog_reports_a_blocked_link_and_a_bad_span_at_their_fields()
    {
        var table = Table(1, 1);
        var cut = TableEditor(table);

        cut.Find("[data-tm-table-cell-edit]").Click();
        cut.Find("[data-tm-item-field='Href'] input").Change("javascript:alert(1)");
        cut.Find("[data-tm-item-field='ColSpan'] input").Change("nope");

        InputInvalid((IRenderedComponent<IComponent>)cut, "Href").Should().BeTrue();
        InputInvalid((IRenderedComponent<IComponent>)cut, "ColSpan").Should().BeTrue();
        SaveDisabled((IRenderedComponent<IComponent>)cut).Should().BeFalse();

        cut.Find("[data-tm-item-dialog-save]").Click();
        cut.FindAll("[data-tm-table-cell-dialog]").Should().NotBeEmpty("an invalid cell cannot be saved");
        table.Rows[0].Cells[0].Href.Should().BeNull();
    }

    // ── A new item is a copy ────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_opens_the_dialog_and_inserts_the_item_only_on_save()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = "Home", Href = "https://example.com" });
        var document = DocWith(navbar);
        var changes = 0;

        var cut = Panel(document, navbar.Id, onChanged: () => changes++);
        cut.Find("[data-tm-item-add]").Click();

        cut.Find("[data-tm-item-dialog]").Should().NotBeNull("Add opens the dialog over the new copy");
        navbar.Links.Should().HaveCount(1, "nothing is inserted before Save");
        changes.Should().Be(0);

        cut.Find("[data-tm-item-field='Text'] input").Change("Kontakt");
        cut.Find("[data-tm-item-field='Href'] input").Change("https://example.com/kontakt");
        cut.Find("[data-tm-item-dialog-save]").Click();

        navbar.Links.Should().HaveCount(2);
        navbar.Links[1].Text.Should().Be("Kontakt");
        changes.Should().Be(1, "one insertion is one undo step");
    }

    [Fact]
    public void Cancelling_a_new_item_leaves_the_block_and_the_undo_history_untouched()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = "Home", Href = "https://example.com" });
        var document = DocWith(navbar);
        var changes = 0;

        var cut = Panel(document, navbar.Id, onChanged: () => changes++);
        cut.Find("[data-tm-item-add]").Click();
        cut.Find("[data-tm-item-field='Text'] input").Change("Kontakt");
        cut.Find("[data-tm-item-dialog-cancel]").Click();

        navbar.Links.Should().HaveCount(1);
        changes.Should().Be(0, "no change reached the editor, so there is nothing to undo");
        cut.FindAll("[data-tm-item-dialog]").Should().BeEmpty();
    }

    // ── a11y: named actions, empty state, removal announcement ──────────────────────────────

    [Fact]
    public void Every_row_action_names_the_item_it_belongs_to()
    {
        var navbar = Navbar(
            new EmailNavbarLink { Text = "Domů", Href = "https://example.com" },
            new EmailNavbarLink { Text = string.Empty, Href = "https://example.com/bez" });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);

        cut.FindAll("[data-tm-item-up]")[0].GetAttribute("aria-label")
            .Should().Be("EmailEditor_ItemList_MoveUp[Domů]");
        cut.FindAll("[data-tm-item-down]")[0].GetAttribute("aria-label")
            .Should().Be("EmailEditor_ItemList_MoveDown[Domů]");
        cut.FindAll("[data-tm-item-edit]")[0].GetAttribute("aria-label")
            .Should().Be("EmailEditor_ItemList_Edit[Domů]");
        cut.FindAll("[data-tm-item-remove]")[0].GetAttribute("aria-label")
            .Should().Be("EmailEditor_ItemList_Remove[Domů]");
        // An unnamed item falls back to its POSITION, so the four actions are still distinguishable.
        cut.FindAll("[data-tm-item-remove]")[1].GetAttribute("aria-label")
            .Should().Be("EmailEditor_ItemList_Remove[EmailEditor_ItemList_Unnamed[2]]");
    }

    [Fact]
    public void An_empty_list_says_that_there_are_no_items_yet()
    {
        var navbar = Navbar();
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);

        cut.Find("[data-tm-item-list-empty]").TextContent.Trim().Should().Be("EmailEditor_ItemList_Empty");
        cut.FindAll("[data-tm-item]").Should().BeEmpty();
    }

    [Fact]
    public void Removing_an_item_is_announced_in_a_live_region()
    {
        var navbar = Navbar(new EmailNavbarLink { Text = "Domů", Href = "https://example.com" });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);

        cut.Find("[data-tm-item-removed]").GetAttribute("aria-live").Should().Be("polite");
        cut.Find("[data-tm-item-removed]").TextContent.Trim().Should().BeEmpty();

        cut.Find("[data-tm-item-remove]").Click();

        cut.Find("[data-tm-item-removed]").TextContent.Trim().Should().Be("EmailEditor_Item_Removed");
        navbar.Links.Should().BeEmpty();
    }

    [Fact]
    public void Moving_an_item_focuses_the_same_action_of_the_moved_item()
    {
        var navbar = Navbar(
            new EmailNavbarLink { Text = "A", Href = "https://example.com/a" },
            new EmailNavbarLink { Text = "B", Href = "https://example.com/b" });
        var document = DocWith(navbar);

        var cut = Panel(document, navbar.Id);
        cut.FindAll("[data-tm-item-down]")[0].Click();

        navbar.Links[0].Text.Should().Be("B");
        JSInterop.Invocations.Should().Contain(
            invocation => invocation.Identifier == "focusBySelector",
            "the moved row keeps the focus on the same action button");
    }

    // ── The table grid: compact pinned column, screen-reader caption ────────────────────────

    [Fact]
    public void The_grid_caption_is_screen_reader_only()
    {
        var table = Table(2, 2);
        var cut = TableEditor(table);

        cut.Find("caption").ClassList.Should().Contain("tm-sr-only");
        cut.Find("caption").TextContent.Trim().Should().Be("EmailEditor_Table_Caption");
    }

    [Fact]
    public void The_pinned_column_toggle_carries_its_label_in_the_accessible_name_only()
    {
        var table = Table(2, 2);
        var cut = TableEditor(table);

        var rowControl = cut.Find("[data-tm-table-rowctl]");
        rowControl.QuerySelectorAll(".tm-toggle-label-text").Should().BeEmpty(
            "a visible label would double the width of the sticky column");
        rowControl.QuerySelector("input[type='checkbox']").Should().NotBeNull();
        rowControl.QuerySelector("input[type='checkbox']")!.GetAttribute("aria-label")
            .Should().Be("EmailEditor_Table_RowHeaderLabel[1]");
    }

    // ── The colour policy split ─────────────────────────────────────────────────────────────

    [Fact]
    public void The_navbar_validator_accepts_an_rgba_colour_like_the_inspector()
    {
        var validator = new EmailNavbarLinkValidator(new EchoEmailLocalizer());

        var result = validator.Validate(new EmailNavbarLink
        {
            Text = "Domů",
            Href = "https://example.com",
            Color = "rgba(0,0,0,0.5)",
        });

        result.IsValid.Should().BeTrue(
            "an MJML attribute colour follows PropertyReflection.IsValidColor, which accepts a colour function");
    }

    [Fact]
    public void The_accordion_validator_accepts_an_hsl_colour_like_the_inspector()
    {
        var validator = new EmailAccordionItemValidator(new EchoEmailLocalizer());

        var result = validator.Validate(new EmailAccordionItem
        {
            Title = "Otázka",
            BackgroundColor = "hsl(210, 40%, 96%)",
            TitleColor = "rgb(17, 24, 39)",
        });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void The_cell_validator_still_uses_the_narrow_bgcolor_allow_list()
    {
        var validator = new EmailTableCellValidator(new EchoEmailLocalizer());

        validator.Validate(new EmailTableCell { Text = "x", BackgroundColor = "rgb(1,2,3)" })
            .IsValid.Should().BeFalse(
                "a cell bgcolor is written verbatim into an HTML attribute, so only #hex and named colours pass");
    }
}
