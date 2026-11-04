using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Plan ff043b2e phase 8 — match highlighting and the "create" row in the combobox family. Both are
/// OPT-IN (<c>HighlightMatches</c> defaults to <c>false</c>, <c>AllowCreate</c> to <c>false</c>), so an
/// existing combobox renders exactly the text it rendered before. These facts pin that default first and
/// then the behaviour: the matched substring is the only thing wrapped in <c>&lt;mark&gt;</c>, the create
/// row sits LAST, it is offered only for text that matches nothing exactly, Enter picks it, and the host
/// hears about it through <c>OnCreate</c>.
/// </summary>
public class TmComboboxHighlightTests : LocalizationTestBase
{
    private static List<SelectOption<string>> Cities =>
    [
        SelectOption<string>.From("praha", "Praha"),
        SelectOption<string>.From("brno", "Brno"),
        SelectOption<string>.From("usti", "Ústí nad Labem")
    ];

    private static Func<SelectOption<string>, string> Display => o => o.Label;

    private static Func<SelectOption<string>, string> Value => o => o.Value;

    // ── TmMultiSelect ────────────────────────────────────────

    private IRenderedComponent<TmMultiSelect<SelectOption<string>, string>> RenderMultiSelect(
        bool highlightMatches = false,
        bool allowCreate = false,
        Action<string>? onCreate = null)
        => Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Cities)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.AllowFiltering, true)
            .Add(c => c.HighlightMatches, highlightMatches)
            .Add(c => c.AllowCreate, allowCreate)
            .Add(c => c.OnCreate, (string? text) => onCreate?.Invoke(text!)));

    [Fact]
    public void MultiSelect_DoesNotWrapTheMatchByDefault()
    {
        var cut = RenderMultiSelect();

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Pra");

        cut.FindAll(".tm-multiselect__option mark").Should().BeEmpty(
            "HighlightMatches is opt-in — an existing multi-select keeps painting plain text");
    }

    [Fact]
    public void MultiSelect_MarksTheMatchedSubstringWhenAsked()
    {
        var cut = RenderMultiSelect(highlightMatches: true);

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Pra");

        var mark = cut.Find(".tm-multiselect__option mark");

        mark.TextContent.Should().Be("Pra");
        cut.Find(".tm-multiselect__option-text").TextContent.Should().Be("Praha");
    }

    [Fact]
    public void MultiSelect_HighlightKeepsTheOriginalText_NotTheFoldedQuery()
    {
        var cut = RenderMultiSelect(highlightMatches: true);

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("úSTÍ");

        cut.Find(".tm-multiselect__option mark").TextContent.Should().Be("Ústí",
            "the mark carries the ORIGINAL accented text of the row, not the query as typed");
    }

    [Fact]
    public void MultiSelect_DoesNotOfferACreateRowByDefault()
    {
        var cut = RenderMultiSelect();

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Zlin");

        cut.FindAll(".tm-multiselect__option--create").Should().BeEmpty();
    }

    [Fact]
    public void MultiSelect_OffersACreateRowForTextThatMatchesNothing()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Zlin");

        var create = cut.Find(".tm-multiselect__option--create");

        create.GetAttribute("role").Should().Be("option");
        create.TextContent.Trim().Should().Be("Create \"Zlin\"");
        cut.Find("[role='listbox']").QuerySelectorAll("[role='option']").Last()
            .GetAttribute("id").Should().Be(create.GetAttribute("id"),
                "the create row is the LAST option of the same listbox, not a control outside it");
    }

    [Fact]
    public void MultiSelect_DoesNotOfferACreateRowForAnExactMatch()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Praha");

        cut.FindAll(".tm-multiselect__option--create").Should().BeEmpty(
            "a row that would duplicate an existing option is noise, not a shortcut");
    }

    [Fact]
    public void MultiSelect_DoesNotOfferACreateRowForBlankText()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();

        cut.FindAll(".tm-multiselect__option--create").Should().BeEmpty();
    }

    [Fact]
    public void MultiSelect_EnterPicksTheCreateRow()
    {
        string? created = null;
        var cut = RenderMultiSelect(allowCreate: true, onCreate: text => created = text);

        cut.Find(".tm-multiselect").Click();
        var filter = cut.Find(".tm-multiselect__filter-input");
        filter.Input("Zlin");

        // ArrowDown walks past the (empty) result list onto the create row.
        filter.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-multiselect__option--create").GetAttribute("id")
            .Should().Be(cut.Find(".tm-multiselect__filter-input").GetAttribute("aria-activedescendant"),
                "the create row is the active descendant, so a screen reader reads what Enter will do");

        cut.Find(".tm-multiselect__filter-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        created.Should().Be("Zlin");
        cut.FindAll(".tm-multiselect__option--create").Should().BeEmpty("the popup closes after creating");
    }

    [Fact]
    public void MultiSelect_TheCreateRowPaintsTheFocusedStyle_WhenArrowedOnto()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();
        var filter = cut.Find(".tm-multiselect__filter-input");
        filter.Input("Zlin");

        // ArrowDown walks past the (empty) result list onto the create row.
        filter.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.Find("[data-testid='multiselect-create-option']").ClassList.Should()
            .Contain("tm-multiselect__option--focused",
                "a sighted keyboard user has to SEE where the highlight went, exactly as on a regular option");
    }

    [Fact]
    public void MultiSelect_TheFocusedStyleMovesFromTheLastResultToTheCreateRow()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();
        var filter = cut.Find(".tm-multiselect__filter-input");

        // "Pra" matches exactly one row (Praha) and still offers the create row, so the two steps show
        // the highlight MOVING instead of being painted on the create row from the start.
        filter.Input("Pra");
        filter.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var firstStop = cut.FindAll(".tm-multiselect__option--focused");
        firstStop.Should().ContainSingle();
        firstStop[0].GetAttribute("id").Should().EndWith("-opt-0",
            "the first ArrowDown lands on the first RESULT, not on the create row");

        filter.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var secondStop = cut.FindAll(".tm-multiselect__option--focused");
        secondStop.Should().ContainSingle("exactly one option owns the visible focus");
        secondStop[0].GetAttribute("data-testid").Should().Be("multiselect-create-option");
    }

    [Fact]
    public void MultiSelect_TheCreateRowIsLocalized_AndKeepsItsPlaceholder()
    {
        var cut = RenderMultiSelect(allowCreate: true);

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input").Input("Zlin \"quoted\"");

        cut.Find(".tm-multiselect__option--create").TextContent
            .Should().Contain("Create").And.Contain("Zlin \"quoted\"");
    }

    // ── TmFilterableDropdown ─────────────────────────────────

    private IRenderedComponent<TmFilterableDropdown<SelectOption<string>, string>> RenderFilterable(
        bool highlightMatches)
        => Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, Cities)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.HighlightMatches, highlightMatches));

    [Fact]
    public void FilterableDropdown_DoesNotWrapTheMatchByDefault()
    {
        var cut = RenderFilterable(highlightMatches: false);

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter-input").Input("Br");

        cut.FindAll(".tm-filterable-dropdown-item mark").Should().BeEmpty();
    }

    [Fact]
    public void FilterableDropdown_MarksTheMatchedSubstringWhenAsked()
    {
        var cut = RenderFilterable(highlightMatches: true);

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter-input").Input("Br");

        cut.Find(".tm-filterable-dropdown-item mark").TextContent.Should().Be("Br");
    }

    // ── TmEntityPicker ───────────────────────────────────────

    private IRenderedComponent<TmEntityPicker<SelectOption<string>, string>> RenderEntityPicker(
        bool highlightMatches)
        => Render<TmEntityPicker<SelectOption<string>, string>>(p => p
            .Add(c => c.SearchProvider, _ => Task.FromResult<IEnumerable<SelectOption<string>>>(Cities))
            .Add(c => c.DisplaySelector, Display)
            .Add(c => c.ValueSelector, Value)
            .Add(c => c.MinSearchLength, 1)
            .Add(c => c.HighlightMatches, highlightMatches));

    [Fact]
    public void EntityPicker_DoesNotWrapTheMatchByDefault()
    {
        var cut = RenderEntityPicker(highlightMatches: false);

        cut.Find(".tm-entity-picker__input").Input("Pra");

        cut.WaitForAssertion(() => cut.FindAll(".tm-entity-picker__option").Should().NotBeEmpty());
        cut.FindAll(".tm-entity-picker__option mark").Should().BeEmpty();
    }

    [Fact]
    public void EntityPicker_MarksTheMatchedSubstringWhenAsked()
    {
        var cut = RenderEntityPicker(highlightMatches: true);

        cut.Find(".tm-entity-picker__input").Input("Pra");

        cut.WaitForAssertion(() => cut.FindAll(".tm-entity-picker__option mark").Should().NotBeEmpty());
        cut.Find(".tm-entity-picker__option mark").TextContent.Should().Be("Pra");
    }

    // ── TmUserPicker ─────────────────────────────────────────

    private IRenderedComponent<TmUserPicker<SelectOption<string>>> RenderUserPicker(bool highlightMatches)
    {
        var result = new TmPickerSearchResult<SelectOption<string>>(Cities, TmPickerFetchState.Ok);
        var resolve = new TmPickerResolveResult<SelectOption<string>>(Cities[0], TmPickerFetchState.Ok);

        return Render<TmUserPicker<SelectOption<string>>>(p => p
            .Add(c => c.SearchProvider,
                (_, _) => Task.FromResult(result))
            .Add(c => c.ResolveProvider,
                (_, _) => Task.FromResult(resolve))
            .Add(c => c.DisplaySelector, Display)
            .Add(c => c.ValueSelector, Value)
            .Add(c => c.MinChars, 1)
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.HighlightMatches, highlightMatches));
    }

    [Fact]
    public void UserPicker_MarksTheMatchedSubstringWhenAsked()
    {
        var cut = RenderUserPicker(highlightMatches: true);

        cut.Find(".tm-user-picker__input").Input("Pra");

        cut.WaitForAssertion(() => cut.FindAll(".tm-user-picker__result-display mark").Should().NotBeEmpty());
        cut.Find(".tm-user-picker__result-display mark").TextContent.Should().Be("Pra");
    }

    [Fact]
    public void UserPicker_DoesNotWrapTheMatchByDefault()
    {
        var cut = RenderUserPicker(highlightMatches: false);

        cut.Find(".tm-user-picker__input").Input("Pra");

        cut.WaitForAssertion(() => cut.FindAll(".tm-user-picker__result-item").Should().NotBeEmpty());
        cut.FindAll(".tm-user-picker__result-display mark").Should().BeEmpty();
    }
}
