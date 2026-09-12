using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>Tests for TmMultiSelect&lt;TItem, TValue&gt;.</summary>
public class TmMultiSelectTests : LocalizationTestBase
{
    private static List<SelectOption<string>> FruitOptions =>
    [
        SelectOption<string>.From("apple",  "Apple"),
        SelectOption<string>.From("banana", "Banana"),
        SelectOption<string>.From("cherry", "Cherry"),
        SelectOption<string>.From("date",   "Date")
    ];

    private static Func<SelectOption<string>, string> Display => o => o.Label;
    private static Func<SelectOption<string>, string> Value => o => o.Value;

    // ── Rendering ──────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Renders_With_Placeholder_When_Empty()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Placeholder, "Pick fruits"));

        cut.Find(".tm-multiselect__placeholder").TextContent.Should().Contain("Pick fruits");
    }

    [Fact]
    public void TmMultiSelect_Renders_Label_When_Set()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Label, "Fruits"));

        cut.Find("label.tm-input-label").TextContent.Trim().Should().Be("Fruits");
    }

    [Fact]
    public void TmMultiSelect_No_Label_When_Null()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value));

        cut.FindAll("label").Should().BeEmpty();
    }

    // ── Chip Mode ──────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Chip_Mode_Shows_Chips_For_Selected_Values()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple", "cherry" }));

        var chips = cut.FindAll(".tm-multiselect__chip");
        chips.Count.Should().Be(2);
        chips[0].TextContent.Should().Contain("Apple");
        chips[1].TextContent.Should().Contain("Cherry");
    }

    [Fact]
    public void TmMultiSelect_Chip_Mode_Remove_Button_Fires_ValuesChanged()
    {
        IReadOnlyList<string>? captured = null;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple", "banana" })
            .Add(c => c.ValuesChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => captured = v)));

        cut.Find(".tm-multiselect__chip-remove").Click();

        captured.Should().NotBeNull();
        captured.Should().HaveCount(1);
        captured.Should().Contain("banana");
    }

    // ── Delimiter Mode ─────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Delimiter_Mode_Shows_Comma_Separated_Text()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Mode, MultiSelectMode.Delimiter)
            .Add(c => c.Values, new List<string> { "apple", "banana" }));

        cut.Find(".tm-multiselect__delimiter-text").TextContent.Should().Contain("Apple, Banana");
    }

    // ── CheckBox Mode ──────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_CheckBox_Mode_Shows_Selected_Count()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Mode, MultiSelectMode.CheckBox)
            .Add(c => c.Values, new List<string> { "apple", "banana", "cherry" }));

        cut.Find(".tm-multiselect__count").Should().NotBeNull();
    }

    // ── Dropdown Open / Close ──────────────────────────────────

    [Fact]
    public void TmMultiSelect_Popup_Hidden_By_Default()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value));

        cut.FindAll(".tm-multiselect__popup").Should().BeEmpty();
    }

    [Fact]
    public void TmMultiSelect_Click_Opens_Popup()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value));

        cut.Find(".tm-multiselect").Click();

        cut.FindAll(".tm-multiselect__popup").Should().NotBeEmpty();
        cut.FindAll(".tm-multiselect__option").Count.Should().Be(4);
    }

    [Fact]
    public void TmMultiSelect_Open_Fires_OnOpen_Callback()
    {
        bool opened = false;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.OnOpen, EventCallback.Factory.Create(this, () => opened = true)));

        cut.Find(".tm-multiselect").Click();

        opened.Should().BeTrue();
    }

    // ── Item Selection ─────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Click_Item_Adds_To_Values()
    {
        IReadOnlyList<string>? captured = null;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string>())
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.ValuesChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => captured = v)));

        cut.Find(".tm-multiselect").Click();
        cut.FindAll(".tm-multiselect__option")[0].Click();

        captured.Should().NotBeNull();
        captured.Should().Contain("apple");
    }

    [Fact]
    public void TmMultiSelect_Click_Selected_Item_Removes_It()
    {
        IReadOnlyList<string>? captured = null;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple", "banana" })
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.ValuesChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => captured = v)));

        cut.Find(".tm-multiselect").Click();
        cut.FindAll(".tm-multiselect__option")[0].Click(); // click Apple (already selected)

        captured.Should().NotBeNull();
        captured.Should().NotContain("apple");
        captured.Should().Contain("banana");
    }

    // ── Clear All ──────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Clear_Button_Clears_All_Values()
    {
        IReadOnlyList<string>? captured = null;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple", "banana" })
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.ValuesChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => captured = v)));

        cut.Find(".tm-multiselect__clear").Click();

        captured.Should().NotBeNull();
        captured.Should().BeEmpty();
    }

    // ── Filter ─────────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Filter_Input_Filters_Visible_Items()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.AllowFiltering, true));

        cut.Find(".tm-multiselect").Click();
        cut.FindAll(".tm-multiselect__option").Count.Should().Be(4);

        cut.Find(".tm-multiselect__filter-input").Input("app");
        cut.FindAll(".tm-multiselect__option").Count.Should().Be(1);
        cut.Find(".tm-multiselect__option-text").TextContent.Should().Contain("Apple");
    }

    // ── Select All ─────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Select_All_Selects_All_Visible_Items()
    {
        IReadOnlyList<string>? captured = null;
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string>())
            .Add(c => c.ShowSelectAll, true)
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.ValuesChanged, EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => captured = v)));

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__select-all-btn").Click();

        captured.Should().NotBeNull();
        captured.Should().HaveCount(4);
    }

    // ── Max Selection ──────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Max_Selection_Disables_Remaining_Options()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple", "banana" })
            .Add(c => c.MaxSelectionCount, 2)
            .Add(c => c.ShowCheckBox, true));

        cut.Find(".tm-multiselect").Click();

        var disabled = cut.FindAll(".tm-multiselect__option--disabled");
        disabled.Count.Should().Be(2); // cherry and date
    }

    // ── Hide Selected Items ────────────────────────────────────

    [Fact]
    public void TmMultiSelect_HideSelectedItems_Hides_Selected_From_Dropdown()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Values, new List<string> { "apple" })
            .Add(c => c.HideSelectedItems, true));

        cut.Find(".tm-multiselect").Click();

        var options = cut.FindAll(".tm-multiselect__option");
        options.Count.Should().Be(3);
        options.Select(o => o.TextContent).Should().NotContain("Apple");
    }

    // ── Disabled State ─────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Disabled_Does_Not_Open()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Disabled, true));

        cut.Find(".tm-multiselect").Click();
        cut.FindAll(".tm-multiselect__popup").Should().BeEmpty();
    }

    [Fact]
    public void TmMultiSelect_Disabled_Has_Disabled_CssClass()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Disabled, true));

        cut.Find(".tm-multiselect").ClassList.Should().Contain("tm-multiselect--disabled");
    }

    // ── Error / HelpText ───────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Error_Shows_Error_Message()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Error, "Required field"));

        cut.Find(".tm-input-error-message").TextContent.Should().Contain("Required field");
        cut.Find(".tm-multiselect").ClassList.Should().Contain("tm-multiselect--error");
    }

    [Fact]
    public void TmMultiSelect_HelpText_Shows_When_No_Error()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.HelpText, "Select one or more"));

        cut.Find(".tm-input-help-text").TextContent.Should().Contain("Select one or more");
    }

    // ── Grouping ───────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_GroupField_Renders_Group_Headers()
    {
        var items = new List<SelectOption<string>>
        {
            SelectOption<string>.From("apple",  "Apple"),
            SelectOption<string>.From("banana", "Banana"),
            SelectOption<string>.From("carrot", "Carrot"),
            SelectOption<string>.From("celery", "Celery")
        };

        // Group by first letter
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ValueField, o => o.Value)
            .Add(c => c.GroupField, o => o.Label[0].ToString()));

        cut.Find(".tm-multiselect").Click();

        var headers = cut.FindAll(".tm-multiselect__group-header");
        headers.Count.Should().Be(3); // A, B, C
    }

    // ── ShowCheckBox ───────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_ShowCheckBox_Renders_Checkboxes()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.ShowCheckBox, true));

        cut.Find(".tm-multiselect").Click();

        cut.FindAll(".tm-multiselect__option-checkbox").Count.Should().Be(4);
    }

    [Fact]
    public void TmMultiSelect_ShowCheckBox_Selected_Has_Checked_Class()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.Values, new List<string> { "apple" }));

        cut.Find(".tm-multiselect").Click();

        cut.FindAll(".tm-multiselect__option-checkbox--checked").Count.Should().Be(1);
    }

    // ── ARIA ───────────────────────────────────────────────────

    [Fact]
    public void TmMultiSelect_Has_Combobox_Role()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value));

        cut.Find("[role='combobox']").Should().NotBeNull();
        cut.Find("[role='combobox']").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void TmMultiSelect_Open_Sets_Aria_Expanded_True()
    {
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value));

        cut.Find(".tm-multiselect").Click();

        cut.Find("[role='combobox']").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("[role='listbox']").Should().NotBeNull();
    }

    // ── Keyboard activation ────────────────────────────────────────────────
    // Enter-to-toggle is intended for the filter input only (a text input
    // produces no native click). The select-all/retry controls are native
    // <button> elements inside the same popup — a container-level Enter case
    // toggled the focused option *and* the button's own click ran its action.

    [Fact]
    public void TmMultiSelect_Enter_In_FilterInput_Toggles_Focused_Option_Once()
    {
        var changes = new List<IReadOnlyList<string>>();
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.ValuesChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => changes.Add(v))));

        cut.Find(".tm-multiselect").Click();
        var input = cut.Find(".tm-multiselect__filter-input");
        input.KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        changes.Should().HaveCount(1);
        changes[0].Should().Equal("apple");
    }

    [Fact]
    public void TmMultiSelect_Enter_On_SelectAll_Button_Does_Not_Also_Toggle_Focused_Option()
    {
        var changes = new List<IReadOnlyList<string>>();
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.ShowCheckBox, true)
            .Add(c => c.ShowSelectAll, true)
            .Add(c => c.ValuesChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => changes.Add(v))));

        cut.Find(".tm-multiselect").Click();
        cut.Find(".tm-multiselect__filter-input")
            .KeyDown(new KeyboardEventArgs { Key = "ArrowDown" }); // focus option 0

        // Real sequence for Enter on a focused <button>: keydown -> click.
        var button = cut.Find(".tm-multiselect__select-all-btn");
        button.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-multiselect__select-all-btn").Click();

        changes.Should().HaveCount(1);
        changes[0].Should().Equal("apple", "banana", "cherry", "date");
    }

    // ── Trigger chrome isolation ────────────────────────────────────────────
    // The clear and chip-remove buttons sit inside the trigger, whose keydown
    // handler maps Enter/Space to ToggleDropdownAsync. Both buttons carry
    // @onkeydown:stopPropagation, so a bubbled keydown can never reach the
    // trigger handler — bUnit reports that as no reachable handler at all.

    [Fact]
    public void TmMultiSelect_Enter_On_Clear_Button_Clears_Without_Toggling_Dropdown()
    {
        // Real sequence for Enter on a focused <button>: keydown -> click.
        var changes = new List<List<string>>();
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.Values, new List<string> { "apple" })
            .Add(c => c.ValuesChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => changes.Add(v.ToList()))));

        var clear = cut.Find(".tm-multiselect__clear");
        var act = () => clear.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the clear button isolates its keydown from the trigger's Enter/Space toggle");

        cut.Find(".tm-multiselect__clear").Click();

        changes.Should().HaveCount(1);
        changes[0].Should().BeEmpty();
        cut.FindAll(".tm-multiselect__popup").Should().BeEmpty();
    }

    [Fact]
    public void TmMultiSelect_Enter_On_Chip_Remove_Button_Removes_Without_Toggling_Dropdown()
    {
        var changes = new List<List<string>>();
        var cut = Render<TmMultiSelect<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, Display)
            .Add(c => c.ValueField, Value)
            .Add(c => c.Mode, MultiSelectMode.Chip)
            .Add(c => c.Values, new List<string> { "apple", "banana" })
            .Add(c => c.ValuesChanged,
                EventCallback.Factory.Create<IReadOnlyList<string>>(this, v => changes.Add(v.ToList()))));

        var remove = cut.Find(".tm-multiselect__chip-remove");
        var act = () => remove.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>(
            "the chip-remove button isolates its keydown from the trigger's Enter/Space toggle");

        cut.Find(".tm-multiselect__chip-remove").Click();

        changes.Should().HaveCount(1);
        changes[0].Should().Equal("banana");
        cut.FindAll(".tm-multiselect__popup").Should().BeEmpty();
    }
}
