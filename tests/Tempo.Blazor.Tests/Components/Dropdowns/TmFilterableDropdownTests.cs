using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Dropdowns;

/// <summary>TDD tests for TmFilterableDropdown&lt;TItem&gt;.</summary>
public class TmFilterableDropdownTests : LocalizationTestBase
{
    private static List<SelectOption<string>> FruitOptions =>
    [
        SelectOption<string>.From("apple",  "Apple"),
        SelectOption<string>.From("banana", "Banana"),
        SelectOption<string>.From("cherry", "Cherry")
    ];

    [Fact]
    public void TmFilterableDropdown_Renders_Trigger()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Placeholder, "Choose fruit")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_Shows_Placeholder_When_No_Value()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Placeholder, "Choose fruit")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-placeholder").TextContent.Should().Contain("Choose fruit");
    }

    [Fact]
    public void TmFilterableDropdown_Menu_Hidden_By_Default()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmFilterableDropdown_Click_Opens_Menu_With_Filter_Input()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.Find(".tm-filterable-dropdown-menu").Should().NotBeNull();
        cut.Find(".tm-filterable-dropdown-filter input").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_Open_Shows_All_Items()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.FindAll(".tm-filterable-dropdown-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmFilterableDropdown_ValueChanged_Fires_On_Item_Click()
    {
        SelectOption<string>? captured = null;
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<SelectOption<string>?>(this, v => captured = v)));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.FindAll(".tm-filterable-dropdown-item").First().Click();

        captured.Should().NotBeNull();
        captured!.Value.Should().Be("apple");
    }

    [Fact]
    public void TmFilterableDropdown_Selecting_Item_Closes_Menu()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.FindAll(".tm-filterable-dropdown-item").First().Click();

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmFilterableDropdown_ShowClearButton_Shows_Clear_When_Value_Set()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[0])
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ShowClearButton, true));

        cut.Find(".tm-filterable-dropdown-clear").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_Clear_Fires_Null_ValueChanged()
    {
        SelectOption<string>? captured = FruitOptions[0];
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[0])
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<SelectOption<string>?>(this, v => captured = v)));

        cut.Find(".tm-filterable-dropdown-clear").Click();

        captured.Should().BeNull();
    }

    // ── Accent-insensitive filtering (FormD normalization) ──────────────────

    private static List<SelectOption<string>> CityOptions =>
    [
        SelectOption<string>.From("usti",    "Ústí nad Labem"),
        SelectOption<string>.From("praha",   "Praha"),
        SelectOption<string>.From("krumlov", "Český Krumlov")
    ];

    private IRenderedComponent<TmFilterableDropdown<SelectOption<string>, string>> RenderCityDropdown(bool? accentInsensitive = null)
        => Render<TmFilterableDropdown<SelectOption<string>, string>>(p =>
        {
            p.Add(c => c.Items, CityOptions)
             .Add(c => c.DisplayField, o => o.Label);
            if (accentInsensitive is not null)
                p.Add(c => c.AccentInsensitiveFilter, accentInsensitive.Value);
        });

    [Fact]
    public void TmFilterableDropdown_Filter_Without_Diacritics_Matches_Accented_Items()
    {
        var cut = RenderCityDropdown();

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter input").Input("usti");

        var items = cut.FindAll(".tm-filterable-dropdown-item");
        items.Count.Should().Be(1);
        items[0].TextContent.Should().Contain("Ústí nad Labem");
    }

    [Fact]
    public void TmFilterableDropdown_Filter_With_Diacritics_Matches_Accented_Items()
    {
        var cut = RenderCityDropdown();

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter input").Input("Ústí");

        var items = cut.FindAll(".tm-filterable-dropdown-item");
        items.Count.Should().Be(1);
        items[0].TextContent.Should().Contain("Ústí nad Labem");
    }

    [Fact]
    public void TmFilterableDropdown_Accented_Filter_Matches_Unaccented_Items()
    {
        var cut = RenderCityDropdown();

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter input").Input("práha");

        var items = cut.FindAll(".tm-filterable-dropdown-item");
        items.Count.Should().Be(1);
        items[0].TextContent.Should().Contain("Praha");
    }

    [Fact]
    public void TmFilterableDropdown_AccentInsensitiveFilter_Disabled_Requires_Exact_Accents()
    {
        var cut = RenderCityDropdown(accentInsensitive: false);

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter input").Input("usti");

        cut.FindAll(".tm-filterable-dropdown-item").Should().BeEmpty();
        cut.Find(".tm-filterable-dropdown-empty").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_AccentInsensitiveFilter_Disabled_Still_Ignores_Case()
    {
        var cut = RenderCityDropdown(accentInsensitive: false);

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.Find(".tm-filterable-dropdown-filter input").Input("praha");

        var items = cut.FindAll(".tm-filterable-dropdown-item");
        items.Count.Should().Be(1);
        items[0].TextContent.Should().Contain("Praha");
    }

    [Fact]
    public void TmFilterableDropdown_Disabled_Does_Not_Render_The_Clear_Button()
    {
        // A disabled dropdown is read-only chrome: offering a ✕ that erases the value contradicts the disabled
        // state (and was a real data hazard — the value could be cleared from a read-only form).
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[0])
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.Disabled, true));

        cut.FindAll(".tm-filterable-dropdown-clear").Should().BeEmpty();
    }

    // ── Keyboard activation ────────────────────────────────────────────────
    // Enter-to-select is intended for the filter input only (a text input
    // produces no native click); the menu container must not also act on
    // Enter — any focusable child inside the menu (e.g. the retry <button>)
    // would otherwise double-fire.

    [Fact]
    public void TmFilterableDropdown_Enter_In_FilterInput_Selects_Focused_Item_Once()
    {
        var selections = new List<SelectOption<string>?>();
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ValueChanged,
                EventCallback.Factory.Create<SelectOption<string>?>(this, v => selections.Add(v))));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        var input = cut.Find(".tm-filterable-dropdown-filter-input");
        // Arrows bubble from the input to the menu container (focus moves).
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        selections.Should().HaveCount(1);
        selections[0].Should().Be(FruitOptions[0]);
    }

    // ── Trigger keyboard activation ─────────────────────────────────────────
    // The trigger is a non-native focusable (role="combobox"), so it needs
    // explicit Enter/Space/ArrowDown handling — the nested clear <button>
    // isolates its keydowns so Enter on it clears without toggling.

    [Fact]
    public void TmFilterableDropdown_Enter_On_Trigger_Opens_Menu()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger")
            .KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll(".tm-filterable-dropdown-menu").Should().ContainSingle();
    }

    [Fact]
    public void TmFilterableDropdown_Space_On_Trigger_Opens_Menu()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger")
            .KeyDown(new KeyboardEventArgs { Key = " " });

        cut.FindAll(".tm-filterable-dropdown-menu").Should().ContainSingle();
    }

    [Fact]
    public void TmFilterableDropdown_ArrowDown_On_Trigger_Opens_Menu()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        cut.FindAll(".tm-filterable-dropdown-menu").Should().ContainSingle();
    }

    [Fact]
    public void TmFilterableDropdown_Enter_On_Clear_Button_Clears_Without_Opening()
    {
        // Real sequence for Enter on a focused <button>: keydown -> click.
        // The clear button stops its keydown before the trigger's Enter ->
        // toggle handler; bUnit reports the boundary by throwing.
        var selections = new List<SelectOption<string>?>();
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[0])
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.ValueChanged,
                EventCallback.Factory.Create<SelectOption<string>?>(this, v => selections.Add(v))));

        var clear = cut.Find(".tm-filterable-dropdown-clear");
        var act = () => clear.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the clear button isolates its keydown from the trigger's toggle handler");

        cut.Find(".tm-filterable-dropdown-clear").Click();

        selections.Should().HaveCount(1);
        selections[0].Should().BeNull();
        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
    }

    // ── Focus management ────────────────────────────────────────────────────
    // Opening moves DOM focus into the filter input; closing destroys that
    // input. Without an explicit restore, focus falls to <body> (WCAG 2.4.3).
    // The item-select close path returns it to the trigger — Escape/outside
    // dismissal is overlay.js's job (it refocuses the anchor itself) — while
    // ElementReference.FocusAsync surfaces in bUnit's Loose JSInterop as a
    // "Blazor._internal.domWrapper.focus" invocation.

    private const string FocusInvocation = "Blazor._internal.domWrapper.focus";

    [Fact]
    public void TmFilterableDropdown_Open_Focuses_Filter_Input()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1));
    }

    [Fact]
    public async Task TmFilterableDropdown_Escape_In_Filter_Closes_Through_Js_Dismissal()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1));

        // A bubbled Escape keydown on the filter input must not close on its own: overlay.js
        // consumes Escape at the window capture phase and delivers it through
        // NotifyDismissedAsync — the menu's own Escape case was dead code (dead-branch sweep).
        cut.Find(".tm-filterable-dropdown-filter-input")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-filterable-dropdown-menu").Should().HaveCount(1,
            "a bubbled Escape keydown is not the dismissal path — overlay.js owns it");

        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
        // overlay.js itself restores focus to the anchor before notifying — the Blazor side
        // must not focus a second time (still just the one call from opening).
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1);
    }

    [Fact]
    public void TmFilterableDropdown_Enter_Select_Restores_Focus_To_Trigger()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        var input = cut.Find(".tm-filterable-dropdown-filter-input");
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(2));
    }

    [Fact]
    public void TmFilterableDropdown_Item_Click_Restores_Focus_To_Trigger()
    {
        // Clicking an option div does not move DOM focus — it stays on the
        // filter input, which is then destroyed with the popup.
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.FindAll(".tm-filterable-dropdown-item").First().Click();

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(2));
    }

    // ── JS dismissal (overlay.js) ────────────────────────────────────────────
    // overlay.js owns focus on dismissal now: Escape refocuses the anchor itself, and an outside
    // pointerdown refocuses it only when the target cannot take focus — a click into another
    // field must keep the focus it just earned (B1). The Blazor callback therefore never queues
    // _focusTriggerAfterClose for either reason.

    [Fact]
    public async Task TmFilterableDropdown_JsOutsideDismissal_Does_Not_Restore_Focus()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1));

        // Simulate overlay.js's outside-dismissal notification.
        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("outside"));

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty());
        // Still just the one focus call from opening — no trigger refocus on the Blazor side.
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1);
    }

    [Fact]
    public async Task TmFilterableDropdown_JsEscapeDismissal_Does_Not_Restore_Focus_From_Blazor()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1));

        // Escape reaches Blazor as the same NotifyDismissedAsync callback; overlay.js already
        // focused the anchor before invoking it, so the component must not focus a second time.
        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty());
        JSInterop.Invocations.Count(i => i.Identifier == FocusInvocation).Should().Be(1);
    }

    // ── Combobox/listbox semantics ──────────────────────────────────────────

    [Fact]
    public void TmFilterableDropdown_AriaLabel_Sets_Trigger_Accessible_Name()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.AriaLabel, "Fruit picker"));

        cut.Find(".tm-filterable-dropdown-trigger")
            .GetAttribute("aria-label").Should().Be("Fruit picker");
    }

    [Fact]
    public void TmFilterableDropdown_Items_Have_Option_Role_And_AriaSelected()
    {
        var cut = Render<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[1])
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        var items = cut.FindAll(".tm-filterable-dropdown-item");
        items.Should().AllSatisfy(i => i.GetAttribute("role").Should().Be("option"));
        items[1].GetAttribute("aria-selected").Should().Be("true");
        items[0].GetAttribute("aria-selected").Should().Be("false");
    }

}
