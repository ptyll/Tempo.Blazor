using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A (redesign, screen <c>email-template-editor</c>): the inspector splits into localised
/// Content/Style tabs, groups the spacing and advanced fields, heads the block with Duplicate/Delete,
/// and renders typed editors (colour with a swatch + hex, alignment segments, enum selects, lengths with
/// a unit) with a localised error instead of a silent model write. Field labels come from the JSON tables
/// (<c>EmailEditor_Field_{Owner}_{Property}</c>, shared fallback <c>EmailEditor_Field_{Property}</c>) —
/// never from <c>Humanize(property.Name)</c> (B11).
/// </summary>
public class TmEmailPropertyPanelPhase7ATests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("cs");

    public TmEmailPropertyPanelPhase7ATests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block, out EmailSection section, out EmailColumn column)
    {
        var document = new EmailTemplateDocument { Subject = "Ahoj" };
        section = new EmailSection();
        column = new EmailColumn();
        column.Blocks.Add(block);
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block) => DocWith(block, out _, out _);

    private IRenderedComponent<TmEmailPropertyPanel> Panel(
        EmailTemplateDocument document,
        Guid? selected = null,
        Action<Guid>? onDuplicate = null,
        Action<Guid>? onDelete = null,
        bool readOnly = false,
        IReadOnlyList<Tempo.Blazor.EmailTemplates.Abstractions.Templating.TemplateVariableInfo>? variables = null)
        => Render<TmEmailPropertyPanel>(p =>
        {
            p.Add(c => c.Document, document);
            p.Add(c => c.SelectedId, selected);
            p.Add(c => c.OnChanged, () => { });
            p.Add(c => c.ReadOnly, readOnly);
            if (variables is not null) p.Add(c => c.Variables, variables);
            if (onDuplicate is not null) p.Add(c => c.OnDuplicate, (Guid id) => onDuplicate(id));
            if (onDelete is not null) p.Add(c => c.OnDelete, (Guid id) => onDelete(id));
        });

    private static AngleSharp.Dom.IElement? FindLabel(IRenderedComponent<TmEmailPropertyPanel> cut, string property)
        => cut.FindAll(
                $"[data-tm-prop=\"{property}\"] .tm-email-fields__label, " +
                $"[data-tm-prop=\"{property}\"] .tm-input-label, " +
                $"[data-tm-prop=\"{property}\"] .tm-select-label, " +
                $"[data-tm-prop=\"{property}\"] .tm-toggle-label-text").FirstOrDefault();

    private static void SelectTab(IRenderedComponent<TmEmailPropertyPanel> cut, string label)
        => cut.Find(label == "Styl" ? "#tab-style" : "#tab-content").Click();

    // ── Localised labels (B11, carry-forward z F5/F7) ─────────────────────────────────────

    [Fact]
    public void Document_panel_labels_the_subject_in_czech_never_the_property_name()
    {
        var cut = Panel(new EmailTemplateDocument { Subject = "Ahoj" });

        var field = cut.Find("[data-tm-prop=\"Subject\"]");
        field.QuerySelector(".tm-input-label")!.TextContent.Trim().Should().Be("Předmět");
        field.TextContent.Should().NotContain("Subject");
    }

    [Theory]
    [InlineData("Button", "Text", "Text tlačítka")]
    [InlineData("Button", "Href", "Odkaz tlačítka")]
    [InlineData("Image", "Src", "URL obrázku")]
    [InlineData("Image", "Alt", "Alternativní text obrázku")]
    [InlineData("Spacer", "Height", "Výška mezery")]
    [InlineData("Divider", "Width", "Šířka oddělovače")]
    [InlineData("Hero", "Height", "Výška hero sekce")]
    public void Owner_specific_labels_are_used_where_the_property_means_something_else(
        string owner, string property, string expected)
    {
        var block = new BlockRegistry().GetAll().Single(d => d.Id == owner.ToLowerInvariant()).Factory();
        var cut = Panel(DocWith(block), block.Id);

        var label = FindLabel(cut, property);
        if (label is null)
        {
            SelectTab(cut, "Styl");
            label = FindLabel(cut, property);
        }

        label.Should().NotBeNull($"{owner}.{property} must render a label");
        label!.TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void Shared_labels_are_used_for_fields_a_single_owner_does_not_name()
    {
        var text = new EmailTextBlock();
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"FontSize\"] .tm-email-fields__label").TextContent.Trim()
            .Should().Be("Velikost písma");
        cut.Find("[data-tm-prop=\"Color\"]").Should().NotBeNull();
    }

    // ── Content / Style tabs ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tabs_are_localized_and_the_content_tab_is_active_first()
    {
        var button = new EmailButtonBlock { Text = "Jdi", Href = "https://x" };
        var cut = Panel(DocWith(button), button.Id);

        cut.FindAll(".tm-tab").Select(tab => tab.TextContent.Trim()).Should().Equal("Obsah", "Styl");
        cut.Find("[data-tm-prop-tabs]").Should().NotBeNull();
        cut.Find("[data-tm-prop=\"Text\"]").Should().NotBeNull();
        cut.FindAll("[data-tm-prop=\"BackgroundColor\"]").Should().BeEmpty("a style field lives in the Styl tab");
    }

    [Fact]
    public void Style_tab_shows_the_style_fields_and_hides_the_content_fields()
    {
        var button = new EmailButtonBlock { Text = "Jdi", Href = "https://x" };
        var cut = Panel(DocWith(button), button.Id);

        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"Color\"]").Should().NotBeNull();
        cut.FindAll("[data-tm-prop=\"Href\"]").Should().BeEmpty();
    }

    [Fact]
    public void Padding_and_advanced_groups_are_collapsible_headers_inside_the_style_tab()
    {
        var text = new EmailTextBlock();
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var padding = cut.Find("[data-tm-prop-group-toggle=\"padding\"]");
        padding.GetAttribute("aria-expanded").Should().Be("true");
        padding.TextContent.Trim().Should().Be("Odsazení");
        cut.Find("[data-tm-prop-group-body=\"padding\"]").HasAttribute("hidden").Should().BeFalse();

        var advanced = cut.Find("[data-tm-prop-group-toggle=\"advanced\"]");
        advanced.TextContent.Trim().Should().Be("Pokročilé");
    }

    [Fact]
    public void Collapsing_the_padding_group_hides_the_body_and_is_remembered_per_block_type()
    {
        var first = new EmailTextBlock { Content = "A" };
        var second = new EmailTextBlock { Content = "B" };
        var document = DocWith(first);
        document.Sections[0].Columns[0].Blocks.Add(second);

        var cut = Panel(document, first.Id);
        SelectTab(cut, "Styl");
        cut.Find("[data-tm-prop-group-toggle=\"padding\"]").Click();

        cut.Find("[data-tm-prop-group-body=\"padding\"]").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[data-tm-prop-group-toggle=\"padding\"]").GetAttribute("aria-expanded").Should().Be("false");

        // Same block type: the inspector remembers the state for the editor's lifetime.
        cut.Render(p => p.Add(c => c.SelectedId, second.Id));
        SelectTab(cut, "Styl");
        cut.Find("[data-tm-prop-group-body=\"padding\"]").HasAttribute("hidden").Should().BeTrue();

        // A different block type starts expanded.
        var button = new EmailButtonBlock();
        document.Sections[0].Columns[0].Blocks.Add(button);
        cut.Render(p => p.Add(c => c.SelectedId, button.Id));
        SelectTab(cut, "Styl");
        cut.Find("[data-tm-prop-group-body=\"padding\"]").HasAttribute("hidden").Should().BeFalse();
    }

    // ── Header actions (reachable without hover, one undo step in the editor) ─────────────

    [Fact]
    public void Block_header_offers_duplicate_and_delete_and_reports_the_block_id()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        Guid? duplicated = null;
        Guid? deleted = null;
        var cut = Panel(DocWith(button), button.Id, id => duplicated = id, id => deleted = id);

        var header = cut.Find("[data-tm-prop-target=\"block\"]");
        header.TextContent.Should().Contain("Tlačítko");

        cut.Find("[data-tm-prop-duplicate]").Click();
        cut.Find("[data-tm-prop-delete]").Click();

        duplicated.Should().Be(button.Id);
        deleted.Should().Be(button.Id);
    }

    [Fact]
    public void Delete_is_a_danger_action()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id, _ => { }, _ => { });

        // The header action is a soft danger icon (a filled red button would shout in the panel header),
        // so the DANGER is carried by the token colour class, not by the solid variant.
        cut.Find("[data-tm-prop-delete]").ClassList.Should().Contain("tm-email-properties__delete");
        cut.Find("[data-tm-prop-delete]").GetAttribute("aria-label")
            .Should().Be("Smazat", "the icon-only action is named for screen readers (cs table)");
    }

    [Fact]
    public void Read_only_hides_the_header_actions_and_disables_every_field()
    {
        var button = new EmailButtonBlock { Text = "Jdi" };
        var cut = Panel(DocWith(button), button.Id, readOnly: true);

        cut.FindAll("[data-tm-prop-duplicate]").Should().BeEmpty();
        cut.FindAll("[data-tm-prop-delete]").Should().BeEmpty();
        cut.Find("[data-tm-properties-panel] fieldset").HasAttribute("disabled").Should().BeTrue();
    }

    // ── Typed field editors (D: colour, alignment, enum, length) ──────────────────────────

    [Fact]
    public void Color_fields_render_the_color_picker_with_a_swatch_and_the_hex_value()
    {
        var text = new EmailTextBlock { Color = "#112233" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var field = cut.Find("[data-tm-prop=\"Color\"]");
        field.GetAttribute("data-tm-field-kind").Should().Be("Color");
        field.QuerySelector(".tm-color-picker").Should().NotBeNull();
        field.TextContent.Should().Contain("#112233");
    }

    [Fact]
    public void Alignment_fields_render_segments_with_aria_pressed()
    {
        var text = new EmailTextBlock { Align = "center" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var field = cut.Find("[data-tm-prop=\"Align\"]");
        field.GetAttribute("data-tm-field-kind").Should().Be("Alignment");

        var segments = field.QuerySelectorAll("[data-tm-align-option]");
        segments.Select(s => s.GetAttribute("data-tm-align-option")).Should().Equal("left", "center", "right", "justify");
        segments.Single(s => s.GetAttribute("data-tm-align-option") == "center")
            .GetAttribute("aria-pressed").Should().Be("true");
        segments.Single(s => s.GetAttribute("data-tm-align-option") == "left")
            .GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void Clicking_an_alignment_segment_writes_the_value_and_raises_on_changed()
    {
        var text = new EmailTextBlock { Align = "left" };
        var changed = false;
        var document = DocWith(text);
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document).Add(c => c.SelectedId, text.Id)
            .Add(c => c.OnChanged, () => changed = true));
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-align-option=\"right\"]").Click();

        text.Align.Should().Be("right");
        changed.Should().BeTrue();
    }

    [Fact]
    public void Choice_fields_render_a_select_with_localized_values()
    {
        var divider = new EmailDividerBlock { BorderStyle = "dashed" };
        var cut = Panel(DocWith(divider), divider.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"BorderStyle\"]").GetAttribute("data-tm-field-kind").Should().Be("Choice");
        var select = cut.Find("[data-tm-prop=\"BorderStyle\"] select");
        select.QuerySelectorAll("option").Select(o => o.TextContent.Trim())
            .Should().Contain(["Plná", "Tečkovaná", "Čárkovaná", "Žádná"]);
        select.QuerySelector("option[selected]")!.TextContent.Trim().Should().Be("Čárkovaná");
    }

    [Fact]
    public void Changing_a_choice_writes_the_raw_mjml_value()
    {
        var divider = new EmailDividerBlock { BorderStyle = "solid" };
        var cut = Panel(DocWith(divider), divider.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"BorderStyle\"] select").Change("dotted");

        divider.BorderStyle.Should().Be("dotted");
    }

    [Fact]
    public void Length_fields_render_a_number_and_a_unit()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var field = cut.Find("[data-tm-prop=\"FontSize\"]");
        field.GetAttribute("data-tm-field-kind").Should().Be("Length");
        field.QuerySelector("input").GetAttribute("value").Should().Be("13");
        field.QuerySelector("input").GetAttribute("inputmode").Should().Be("decimal");
        field.QuerySelector("[data-tm-length-unit] select")!.GetAttribute("value").Should().Be("px");
    }

    [Fact]
    public void Editing_a_length_writes_the_composed_value()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"FontSize\"] input").Change("18");

        text.FontSize.Should().Be("18px");
    }

    [Fact]
    public void Changing_the_length_unit_rewrites_the_value()
    {
        var column = new EmailColumn { Width = "50%" };
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);

        var cut = Panel(document, column.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"Width\"] [data-tm-length-unit] select").Change("px");

        column.Width.Should().Be("50px");
    }

    [Fact]
    public void A_stored_invalid_color_shows_a_localized_error_and_is_never_written()
    {
        var text = new EmailTextBlock { Color = "not-a-color" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var field = cut.Find("[data-tm-prop=\"Color\"]");
        field.QuerySelector("[data-tm-field-error]")!.TextContent.Trim()
            .Should().Be("Zadejte barvu, například #1a1a1a.");

        field.QuerySelector("input")!.Change("still-not-a-color");
        text.Color.Should().Be("not-a-color", "an invalid value must never reach the document");

        cut.Find("[data-tm-prop=\"Color\"] input").Change("#0f0f0f");
        text.Color.Should().Be("#0f0f0f");
        cut.FindAll("[data-tm-prop=\"Color\"] [data-tm-field-error]").Should().BeEmpty();
    }

    [Fact]
    public void A_stored_invalid_length_shows_a_localized_error_and_is_never_written()
    {
        var text = new EmailTextBlock { FontSize = "weird" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        var field = cut.Find("[data-tm-prop=\"FontSize\"]");
        field.QuerySelector("[data-tm-field-error]")!.TextContent.Trim()
            .Should().Be("Zadejte délku, například 16px nebo 100 %.");

        field.QuerySelector("input")!.Change("weird2");
        text.FontSize.Should().Be("weird");

        cut.Find("[data-tm-prop=\"FontSize\"] input").Change("16px");
        text.FontSize.Should().Be("16px");
    }

    [Fact]
    public void Clearing_a_required_length_shows_the_localized_error_and_keeps_the_value()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"FontSize\"] input[data-tm-length-number]").Change(string.Empty);

        text.FontSize.Should().Be("13px", "a required length never becomes empty");
        cut.Find("[data-tm-prop=\"FontSize\"] [data-tm-field-error]").TextContent.Trim()
            .Should().Be("Zadejte délku, například 16px nebo 100 %.");
    }

    [Fact]
    public void Clearing_an_optional_length_unsets_it()
    {
        var image = new EmailImageBlock { Src = "https://x/i.png", Alt = "Logo", Width = "100px" };
        var cut = Panel(DocWith(image), image.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"Width\"] input[data-tm-length-number]").Change(string.Empty);

        image.Width.Should().BeNull("a nullable length means \"unset\"");
        cut.FindAll("[data-tm-prop=\"Width\"] [data-tm-field-error]").Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_property_stays_a_text_field()
    {
        // Border is a compound CSS shorthand ("2px solid black") the typed editors must not claim.
        var button = new EmailButtonBlock { Border = "2px solid black" };
        var cut = Panel(DocWith(button), button.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"Border\"]").GetAttribute("data-tm-field-kind").Should().Be("Text");
    }

    [Fact]
    public void Section_padding_uses_the_shared_padding_group_and_its_own_label()
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        document.Sections.Add(section);
        var cut = Panel(document, section.Id);
        SelectTab(cut, "Styl");

        cut.Find("[data-tm-prop=\"Padding\"] .tm-input-label").TextContent.Trim()
            .Should().Be("Odsazení sekce");
        cut.Find("[data-tm-prop-group=\"padding\"]").Should().NotBeNull();
    }

    // ── Variables affordance lives on the fields that support a template ──────────────────

    [Fact]
    public void Template_fields_offer_the_insert_variable_button_and_others_do_not()
    {
        var button = new EmailButtonBlock { Text = "Jdi", Href = "https://x" };
        var cut = Panel(DocWith(button), button.Id);

        cut.Find("[data-tm-variable-trigger=\"Text\"]").Should().NotBeNull();
        cut.Find("[data-tm-variable-trigger=\"Href\"]").Should().NotBeNull();
        cut.FindAll("[data-tm-variable-trigger=\"Target\"]").Should().BeEmpty();
    }

    [Fact]
    public void The_document_panel_offers_the_button_for_the_subject_and_the_preheader()
    {
        var cut = Panel(new EmailTemplateDocument());

        cut.Find("[data-tm-variable-trigger=\"Subject\"]").Should().NotBeNull();
        cut.Find("[data-tm-variable-trigger=\"Preheader\"]").Should().NotBeNull();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
