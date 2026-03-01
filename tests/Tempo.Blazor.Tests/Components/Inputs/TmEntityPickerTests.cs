using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmEntityPicker.</summary>
public class TmEntityPickerTests : LocalizationTestBase
{
    private record TestItem(int Id, string Name);

    private static readonly List<TestItem> _allItems = new()
    {
        new(1, "Alice"),
        new(2, "Bob"),
        new(3, "Charlie"),
        new(4, "Diana"),
    };

    private static Task<IEnumerable<TestItem>> SearchProvider(string query)
    {
        var results = _allItems.Where(i => i.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(results);
    }

    [Fact]
    public void EntityPicker_Renders()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name));

        cut.Find(".tm-entity-picker").Should().NotBeNull();
        cut.Find("input").Should().NotBeNull();
    }

    [Fact]
    public void EntityPicker_Label_Renders()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Label, "Select user"));

        cut.Find(".tm-input-label").TextContent.Should().Contain("Select user");
    }

    [Fact]
    public async Task EntityPicker_Search_ShowsResults()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1));

        var input = cut.Find("input");
        await input.InputAsync(new ChangeEventArgs { Value = "Ali" });

        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__option").Count > 0);
        cut.FindAll(".tm-entity-picker__option").Should().HaveCount(1);
        cut.Find(".tm-entity-picker__option").TextContent.Should().Contain("Alice");
    }

    [Fact]
    public async Task EntityPicker_SelectItem_ClosesDropdown()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Bob" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__option").Count > 0);

        cut.Find(".tm-entity-picker__option").Click();
        // Dropdown should close after selection
        cut.FindAll(".tm-entity-picker__option").Should().BeEmpty();
    }

    [Fact]
    public async Task EntityPicker_MinSearchLength_RespectsThreshold()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 3));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Al" });

        // Should NOT trigger search (min length = 3)
        cut.FindAll(".tm-entity-picker__option").Should().BeEmpty();
    }

    [Fact]
    public void EntityPicker_Disabled_DisablesInput()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Disabled, true));

        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void EntityPicker_Error_Renders()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Error, "Required field"));

        cut.Find(".tm-input-error-message").TextContent.Should().Contain("Required field");
    }

    [Fact]
    public async Task EntityPicker_NoResults_ShowsMessage()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "XYZ" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__no-results").Count > 0);

        cut.Find(".tm-entity-picker__no-results").Should().NotBeNull();
    }

    [Fact]
    public void EntityPicker_Placeholder_Renders()
    {
        var cut = RenderComponent<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Placeholder, "Search users..."));

        cut.Find("input").GetAttribute("placeholder").Should().Be("Search users...");
    }
}
