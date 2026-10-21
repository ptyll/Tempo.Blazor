using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A review fixes on the inspector: an optional (nullable) colour can be unset and never shows a
/// false error, a required colour survives a clear, the Advanced toggle publishes a real
/// <c>aria-expanded</c> string, lengths reject a culture's thousands separator and negative numbers
/// (except letter spacing), the three typography fields are Choice editors that never swallow a stored
/// value outside their option set, Rel moved to the Advanced group, and every composite editor is named
/// through the visible label (<c>aria-labelledby</c>) with a localized unit name.
/// </summary>
public class TmEmailInspectorReviewFixTests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("cs");

    public TmEmailInspectorReviewFixTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        // The real embedded tables: these tests assert the Czech labels the user reads.
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

    private IRenderedComponent<TmEmailPropertyPanel> Panel(
        EmailTemplateDocument document, Guid? selected, bool readOnly = false)
        => Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, selected)
            .Add(c => c.ReadOnly, readOnly)
            .Add(c => c.OnChanged, () => { }));

    private static void SelectStyleTab(IRenderedComponent<TmEmailPropertyPanel> cut)
        => cut.Find("#tab-style").Click();

    // ── Optional colour (nullable) ──────────────────────────────────────────────────────────

    [Fact]
    public void Unset_section_background_renders_a_picker_without_error()
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection();
        document.Sections.Add(section);

        var cut = Panel(document, section.Id);
        SelectStyleTab(cut);

        var field = cut.Find("[data-tm-prop=\"BackgroundColor\"]");
        field.QuerySelector(".tm-color-picker").Should().NotBeNull("an unset nullable colour is a picker");
        field.QuerySelectorAll("[data-tm-field-error]").Should().BeEmpty("empty is a valid \"unset\", not an error");
        field.QuerySelector("input[aria-invalid='true']").Should().BeNull();
    }

    [Fact]
    public void Clearing_an_optional_color_unsets_it()
    {
        var document = new EmailTemplateDocument();
        var section = new EmailSection { BackgroundColor = "#112233" };
        document.Sections.Add(section);

        var cut = Panel(document, section.Id);
        SelectStyleTab(cut);

        var picker = cut.FindComponent<TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(null));

        section.BackgroundColor.Should().BeNull("a nullable colour means \"unset\" when cleared");
    }

    [Fact]
    public void Clearing_a_required_color_keeps_the_value()
    {
        var text = new EmailTextBlock { Content = "Ahoj", Color = "#112233" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var picker = cut.FindComponent<TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync(string.Empty));

        text.Color.Should().Be("#112233", "a required colour can never become empty");
    }

    [Fact]
    public void An_invalid_colour_from_the_picker_is_never_written()
    {
        var text = new EmailTextBlock { Content = "Ahoj", Color = "#112233" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var picker = cut.FindComponent<TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync("not-a-colour"));

        text.Color.Should().Be("#112233");
    }

    // ── Advanced group aria-expanded ────────────────────────────────────────────────────────

    [Fact]
    public void Advanced_toggle_publishes_the_aria_expanded_string_not_a_boolean_attribute()
    {
        var cut = Render<TmEmailObjectFields>(p => p.Add(c => c.Target, new EmailTextBlock()));

        var toggle = cut.Find("[data-tm-fields-advanced-toggle]");
        toggle.GetAttribute("aria-expanded").Should().Be("true",
            "Blazor renders a bool aria-* value as an EMPTY attribute — the string is the contract");

        toggle.Click();

        cut.Find("[data-tm-fields-advanced-toggle]").GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── Length parsing: culture, thousands separators, sign ───────────────────────────────

    [Fact]
    public void An_english_decimal_comma_is_rejected_and_the_model_is_unchanged()
    {
        using var english = CultureScope.Use("en-US");
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        cut.Find("[data-tm-prop=\"FontSize\"] input[data-tm-length-number]").Change("1,5");

        text.FontSize.Should().Be("13px", "a comma is the thousands separator in en-US, not a decimal point");
        cut.Find("[data-tm-prop=\"FontSize\"] [data-tm-field-error]").Should().NotBeNull();
    }

    [Fact]
    public void An_english_decimal_point_is_accepted()
    {
        using var english = CultureScope.Use("en-US");
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        cut.Find("[data-tm-prop=\"FontSize\"] input[data-tm-length-number]").Change("1.5");

        text.FontSize.Should().Be("1.5px");
    }

    [Fact]
    public void A_culture_thousands_separator_is_rejected_in_the_current_culture_too()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        // Czech groups thousands with a space, English with a comma; neither is a decimal point.
        cut.Find("[data-tm-prop=\"FontSize\"] input[data-tm-length-number]").Change("1 000");

        text.FontSize.Should().Be("13px");
    }

    [Fact]
    public void A_negative_length_is_rejected()
    {
        var text = new EmailTextBlock { FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        cut.Find("[data-tm-prop=\"FontSize\"] input[data-tm-length-number]").Change("-4");

        text.FontSize.Should().Be("13px", "a negative font size is not a legal CSS length");
        cut.Find("[data-tm-prop=\"FontSize\"] [data-tm-field-error]").Should().NotBeNull();
    }

    [Fact]
    public void A_negative_letter_spacing_is_allowed()
    {
        var text = new EmailTextBlock { LetterSpacing = "1px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        cut.Find("[data-tm-prop=\"LetterSpacing\"] input[data-tm-length-number]").Change("-1");

        text.LetterSpacing.Should().Be("-1px", "negative letter spacing is a legitimate tight tracking");
    }

    // ── Typography fields as Choice editors ────────────────────────────────────────────────

    [Fact]
    public void Font_style_weight_and_decoration_are_localized_choice_editors()
    {
        var text = new EmailTextBlock
        {
            Content = "Ahoj",
            FontStyle = "italic",
            FontWeight = "700",
            TextDecoration = "underline",
        };

        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var style = cut.Find("[data-tm-prop=\"FontStyle\"]");
        style.GetAttribute("data-tm-field-kind").Should().Be("Choice");
        style.QuerySelectorAll("option").Select(o => o.TextContent.Trim())
            .Should().Equal("Normální", "Kurzíva");

        var weight = cut.Find("[data-tm-prop=\"FontWeight\"]");
        weight.GetAttribute("data-tm-field-kind").Should().Be("Choice");
        weight.QuerySelectorAll("option").Select(o => o.TextContent.Trim())
            .Should().Equal("Normální", "Tučné");

        var decoration = cut.Find("[data-tm-prop=\"TextDecoration\"]");
        decoration.GetAttribute("data-tm-field-kind").Should().Be("Choice");
        decoration.QuerySelectorAll("option").Select(o => o.TextContent.Trim())
            .Should().Equal("Bez podtržení", "Podtržené");
    }

    [Fact]
    public void Changing_a_typography_choice_writes_the_raw_css_value()
    {
        var text = new EmailTextBlock { Content = "Ahoj", FontWeight = "400" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        cut.Find("[data-tm-prop=\"FontWeight\"] select").Change("700");

        text.FontWeight.Should().Be("700");
    }

    [Theory]
    [InlineData("FontWeight", "600")]
    [InlineData("FontStyle", "oblique")]
    [InlineData("TextDecoration", "line-through")]
    public void A_stored_value_outside_the_option_set_is_preserved_and_stays_editable(string property, string stored)
    {
        var text = new EmailTextBlock { Content = "Ahoj" };
        typeof(EmailTextBlock).GetProperty(property)!.SetValue(text, stored);

        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var field = cut.Find($"[data-tm-prop=\"{property}\"]");
        var input = field.QuerySelector("input");
        input.Should().NotBeNull($"{property}={stored} has no localized option, so it must stay a text field");
        input.GetAttribute("value").Should().Be(stored, "an imported value must never silently disappear");
        field.QuerySelector("[data-tm-field-error]").Should().BeNull();

        input.Change("800");
        typeof(EmailTextBlock).GetProperty(property)!.GetValue(text).Should().Be("800");
    }

    [Fact]
    public void Rel_moved_to_the_advanced_group()
    {
        var button = new EmailButtonBlock { Text = "Jdi", Href = "https://x", Rel = "noopener" };
        var cut = Panel(DocWith(button), button.Id);

        cut.FindAll("[data-tm-prop=\"Rel\"]").Should().BeEmpty("Rel is an advanced attribute, not content");

        SelectStyleTab(cut);
        cut.Find("[data-tm-prop-group-body=\"advanced\"] [data-tm-prop=\"Rel\"]").Should().NotBeNull();
    }

    // ── Accessible names of composite editors ─────────────────────────────────────────────

    [Fact]
    public void The_colour_picker_is_a_group_named_by_the_visible_label()
    {
        var text = new EmailTextBlock { Content = "Ahoj", Color = "#112233" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var label = cut.Find("[data-tm-prop=\"Color\"] .tm-email-fields__label");
        label.GetAttribute("id").Should().NotBeNullOrEmpty();

        var group = cut.Find("[data-tm-prop=\"Color\"] [role=\"group\"]");
        group.GetAttribute("aria-labelledby").Should().Be(label.GetAttribute("id"));
    }

    [Fact]
    public void The_alignment_segments_are_a_group_named_by_the_visible_label()
    {
        var text = new EmailTextBlock { Content = "Ahoj", Align = "center" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var label = cut.Find("[data-tm-prop=\"Align\"] .tm-email-fields__label");
        var group = cut.Find("[data-tm-prop=\"Align\"] [role=\"group\"]");
        group.GetAttribute("aria-labelledby").Should().Be(label.GetAttribute("id"));
    }

    [Fact]
    public void The_length_controls_are_a_group_named_by_the_label_and_the_unit_has_its_own_name()
    {
        var text = new EmailTextBlock { Content = "Ahoj", FontSize = "13px" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        var field = cut.Find("[data-tm-prop=\"FontSize\"]");
        var label = field.QuerySelector(".tm-email-fields__label")!;
        field.QuerySelector("[data-tm-length-number]")!.GetAttribute("aria-labelledby")
            .Should().Be(label.GetAttribute("id"));
        field.QuerySelector("[role=\"group\"]")!.GetAttribute("aria-labelledby")
            .Should().Be(label.GetAttribute("id"));
        field.QuerySelector("[data-tm-length-unit] select")!.GetAttribute("aria-label")
            .Should().Be("Velikost písma – jednotka", "the unit select needs a name that names the field");
    }

    [Fact]
    public void Alignment_segments_carry_an_icon_and_a_title_so_the_name_is_not_colour_only()
    {
        var text = new EmailTextBlock { Content = "Ahoj", Align = "center" };
        var cut = Panel(DocWith(text), text.Id);
        SelectStyleTab(cut);

        foreach (var option in cut.FindAll("[data-tm-prop=\"Align\"] [data-tm-align-option]"))
        {
            option.GetAttribute("title").Should().NotBeNullOrEmpty();
            option.QuerySelector("svg").Should().NotBeNull($"'{option.GetAttribute("data-tm-align-option")}' shows an icon");
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
