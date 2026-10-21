using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
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

/// <summary>
/// Fáze 7A review fixes on the variable picker: the search folds diacritics (<c>krestni</c> finds
/// „Křestní jméno“), an empty result explains itself instead of showing the generic "no variables" text,
/// the list is operable from the keyboard (focus lands in the search, ArrowDown reaches the first row,
/// ArrowUp/ArrowDown walk a roving tabindex) and the popover trigger is a styled span, never a nested
/// <c>&lt;button&gt;</c> inside the popover's own role=button trigger.
/// </summary>
public class TmEmailVariablePickerReviewFixTests : BunitContext
{
    private static readonly TemplateVariableInfo[] Variables =
    [
        new("user.first_name", VariableKind.Scalar) { Description = "Křestní jméno", Group = "Uživatel" },
        new("user.email", VariableKind.Scalar) { Description = "E-mail uživatele", Group = "Uživatel" },
        new("order.total", VariableKind.Scalar) { Description = "Celková cena", Group = "Objednávka" },
        new("order.items", VariableKind.Collection) { Description = "Položky objednávky", Group = "Objednávka" },
    ];

    public TmEmailVariablePickerReviewFixTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoLocalizer());
        Services.AddSingleton<Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer>(new EchoLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
    }

    private IRenderedComponent<TmEmailVariablePicker> Picker()
        => Render<TmEmailVariablePicker>(p => p
            .Add(c => c.Variables, Variables)
            .Add(c => c.OnInsert, (string _) => { }));

    private static IElement Search(IRenderedComponent<TmEmailVariablePicker> cut)
        => cut.Find("[data-tm-variable-picker] input[type=search]");

    private bool FocusedSomething()
        => JSInterop.Invocations.Any(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

    // ── Diacritics-insensitive search + explicit no-match text ────────────────────────────

    [Fact]
    public void Search_folds_diacritics_in_path_and_description()
    {
        var cut = Picker();

        Search(cut).Input("krestni");

        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("data-tm-variable"))
            .Should().Equal(new[] { "user.first_name" }, "a Czech user types without diacritics");
    }

    [Fact]
    public void Search_folds_diacritics_from_the_catalogue_side_too()
    {
        var cut = Picker();

        Search(cut).Input("uzivatel");

        cut.FindAll("[data-tm-variable-group]").Select(g => g.GetAttribute("data-tm-variable-group"))
            .Should().Equal("Uživatel");
    }

    [Fact]
    public void A_filter_with_no_match_explains_which_term_found_nothing()
    {
        var cut = Picker();

        Search(cut).Input("nic-takového");

        var noMatch = cut.Find("[data-tm-variable-nomatch]");
        noMatch.TextContent.Should().Contain("nic-takového", "the sentence names the searched term");
    }

    [Fact]
    public void A_picker_without_any_variable_keeps_the_generic_empty_state()
    {
        var cut = Render<TmEmailVariablePicker>(p => p.Add(c => c.Variables, Array.Empty<TemplateVariableInfo>()));

        cut.Find("[data-tm-variable-empty]").Should().NotBeNull();
        cut.FindAll("[data-tm-variable-nomatch]").Should().BeEmpty();
    }

    // ── Keyboard: focus on open, roving tabindex ─────────────────────────────────────────

    [Fact]
    public void The_search_field_takes_focus_when_the_picker_opens()
    {
        var cut = Picker();

        cut.WaitForAssertion(() => FocusedSomething().Should().BeTrue(
            "the picker is usable without a mouse the moment it opens"));
    }

    [Fact]
    public void ArrowDown_from_the_search_moves_focus_to_the_first_row()
    {
        var cut = Picker();
        cut.WaitForAssertion(() => FocusedSomething().Should().BeTrue());
        var before = JSInterop.Invocations.Count(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase));

        Search(cut).TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(invocation =>
            invocation.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase))
            .Should().BeGreaterThan(before, "ArrowDown hands the caret to the first row"));

        var rows = cut.FindAll("[data-tm-variable]");
        rows[0].GetAttribute("tabindex").Should().Be("0");
        rows[1].GetAttribute("tabindex").Should().Be("-1");
    }

    [Fact]
    public void ArrowDown_and_ArrowUp_walk_a_roving_tabindex_through_the_rows()
    {
        var cut = Picker();
        Search(cut).TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowDown" });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-variable]")[0].GetAttribute("tabindex").Should().Be("0"));

        var firstRow = cut.FindAll("[data-tm-variable]")[0];
        var firstPath = firstRow.GetAttribute("data-tm-variable")!;
        var secondPath = cut.FindAll("[data-tm-variable]")[1].GetAttribute("data-tm-variable")!;

        firstRow.TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowDown" });

        cut.WaitForAssertion(() => cut.Find($"[data-tm-variable=\"{secondPath}\"]")
            .GetAttribute("tabindex").Should().Be("0", "one row is the tab stop at a time"));
        cut.Find($"[data-tm-variable=\"{firstPath}\"]").GetAttribute("tabindex").Should().Be("-1");

        cut.Find($"[data-tm-variable=\"{secondPath}\"]")
            .TriggerEventAsync("onkeydown", new KeyboardEventArgs { Key = "ArrowUp" });

        cut.WaitForAssertion(() => cut.Find($"[data-tm-variable=\"{firstPath}\"]")
            .GetAttribute("tabindex").Should().Be("0"));
    }

    [Fact]
    public void The_first_row_is_the_initial_tab_stop()
    {
        var cut = Picker();

        cut.FindAll("[data-tm-variable]").Select(e => e.GetAttribute("tabindex"))
            .Should().Equal(new[] { "0", "-1", "-1", "-1" },
                "a single tab stop makes the long list one Tab away");
    }

    // ── Trigger markup: a styled span, not a nested button ────────────────────────────────

    [Fact]
    public void The_insert_variable_trigger_is_a_span_inside_the_popover_trigger()
    {
        var button = new EmailButtonBlock { Text = "Jdi", Href = "https://x" };
        var document = new EmailTemplateDocument { Subject = "Ahoj" };
        var column = new EmailColumn();
        column.Blocks.Add(button);
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, document)
            .Add(c => c.SelectedId, button.Id)
            .Add(c => c.OnChanged, () => { }));

        // The popover trigger is itself the activation control (role=button). A nested <button> would be a
        // button inside a button — invalid HTML, and two activations per press.
        var trigger = cut.Find("[data-tm-variable-trigger=\"Text\"] .tm-popover__trigger");
        trigger.QuerySelectorAll("button").Should().BeEmpty();
        trigger.QuerySelector(".tm-email-fields__variable-trigger").Should().NotBeNull();
    }

    private sealed class EchoLocalizer : ITmLocalizer, Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments]
            => arguments.Length == 0 ? key : key + "[" + string.Join(",", arguments) + "]";
    }
}
