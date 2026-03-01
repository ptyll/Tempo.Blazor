using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Dropdowns;
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
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Placeholder, "Choose fruit")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_Shows_Placeholder_When_No_Value()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Placeholder, "Choose fruit")
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-placeholder").TextContent.Should().Contain("Choose fruit");
    }

    [Fact]
    public void TmFilterableDropdown_Menu_Hidden_By_Default()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmFilterableDropdown_Click_Opens_Menu_With_Filter_Input()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.Find(".tm-filterable-dropdown-menu").Should().NotBeNull();
        cut.Find(".tm-filterable-dropdown-filter input").Should().NotBeNull();
    }

    [Fact]
    public void TmFilterableDropdown_Open_Shows_All_Items()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();

        cut.FindAll(".tm-filterable-dropdown-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmFilterableDropdown_ValueChanged_Fires_On_Item_Click()
    {
        SelectOption<string>? captured = null;
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
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
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.DisplayField, o => o.Label));

        cut.Find(".tm-filterable-dropdown-trigger").Click();
        cut.FindAll(".tm-filterable-dropdown-item").First().Click();

        cut.FindAll(".tm-filterable-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmFilterableDropdown_ShowClearButton_Shows_Clear_When_Value_Set()
    {
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
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
        var cut = RenderComponent<TmFilterableDropdown<SelectOption<string>, string>>(p => p
            .Add(c => c.Items, FruitOptions)
            .Add(c => c.Value, FruitOptions[0])
            .Add(c => c.DisplayField, o => o.Label)
            .Add(c => c.ShowClearButton, true)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<SelectOption<string>?>(this, v => captured = v)));

        cut.Find(".tm-filterable-dropdown-clear").Click();

        captured.Should().BeNull();
    }
}
