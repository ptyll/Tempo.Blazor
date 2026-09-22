using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
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

    /// <summary>Provider that supports inline create-new and recent items via the K10 default-interface methods.</summary>
    private sealed class FakeEntityProvider : IDropdownDataProvider<TestItem>
    {
        public Task<DropdownDataResult<TestItem>> GetItemsAsync(DropdownSearchRequest request, CancellationToken ct = default)
            => Task.FromResult(DropdownDataResult<TestItem>.WithAllItems(_allItems));

        public Task<TestItem?> CreateAsync(string text, CancellationToken ct = default)
            => Task.FromResult<TestItem?>(new TestItem(99, text));

        public Task<IReadOnlyList<TestItem>> GetRecentAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TestItem>>(new List<TestItem> { new(3, "Charlie") });
    }

    [Fact]
    public void EntityPicker_Renders()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name));

        cut.Find(".tm-entity-picker").Should().NotBeNull();
        cut.Find("input").Should().NotBeNull();
    }

    [Fact]
    public void EntityPicker_Label_Renders()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Label, "Select user"));

        cut.Find(".tm-input-label").TextContent.Should().Contain("Select user");
    }

    [Fact]
    public async Task EntityPicker_Search_ShowsResults()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
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
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
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
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
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
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Disabled, true));

        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void EntityPicker_Error_Renders()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Error, "Required field"));

        cut.Find(".tm-input-error-message").TextContent.Should().Contain("Required field");
    }

    [Fact]
    public async Task EntityPicker_NoResults_ShowsMessage()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
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
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.Placeholder, "Search users..."));

        cut.Find("input").GetAttribute("placeholder").Should().Be("Search users...");
    }

    // ── K10: multi-select, create-new, recent ──────────────────────

    [Fact]
    public async Task EntityPicker_SingleSelect_StillRaisesValueChanged()
    {
        // Backward compatibility: the single Value/ValueChanged path is unchanged when MultiSelect is off.
        int? value = null;
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1)
            .Add(x => x.ValueChanged, v => value = v));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Bob" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__option").Count > 0);
        cut.Find(".tm-entity-picker__option").Click();

        value.Should().Be(2);
        cut.FindAll(".tm-entity-picker__chip").Should().BeEmpty();
    }

    [Fact]
    public async Task EntityPicker_MultiSelect_Selecting_RaisesSelectedValuesChanged_And_AddsChip()
    {
        IReadOnlyList<int> selected = new List<int>();
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1)
            .Add(x => x.MultiSelect, true)
            .Add(x => x.SelectedValues, selected)
            .Add(x => x.SelectedValuesChanged, v => selected = v));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Ali" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__option").Count > 0);
        cut.Find(".tm-entity-picker__option").Click();

        selected.Should().ContainSingle().Which.Should().Be(1);
        cut.Render(p => p.Add(x => x.SelectedValues, selected));
        cut.FindAll(".tm-entity-picker__chip").Should().ContainSingle();
    }

    [Fact]
    public async Task EntityPicker_MultiSelect_DeselectingSelected_RemovesValue()
    {
        IReadOnlyList<int> selected = new List<int> { 1 };
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1)
            .Add(x => x.MultiSelect, true)
            .Add(x => x.SelectedValues, selected)
            .Add(x => x.SelectedValuesChanged, v => selected = v));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Ali" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__option").Count > 0);
        cut.Find(".tm-entity-picker__option").Click(); // Alice (id 1) already selected → toggle off

        selected.Should().BeEmpty();
    }

    [Fact]
    public async Task EntityPicker_CreateNew_AddsAndSelectsItem()
    {
        int? value = null;
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.MinSearchLength, 1)
            .Add(x => x.AllowCreateNew, true)
            .Add(x => x.DataProvider, new FakeEntityProvider())
            .Add(x => x.ValueChanged, v => value = v));

        await cut.Find("input").InputAsync(new ChangeEventArgs { Value = "Zoe" });
        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__create").Count > 0);
        cut.Find(".tm-entity-picker__create").Click();

        value.Should().Be(99);
    }

    [Fact]
    public void EntityPicker_Recent_RendersItems_OnFocus()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name)
            .Add(x => x.ShowRecent, true)
            .Add(x => x.DataProvider, new FakeEntityProvider()));

        cut.Find("input").Focus();

        cut.WaitForState(() => cut.FindAll(".tm-entity-picker__recent-item").Count > 0);
        cut.Find(".tm-entity-picker__recent").TextContent.Should().Contain("Charlie");
    }

    /// <summary>
    /// N168: the overlay Anchor is the <c>.tm-entity-picker</c> wrapper — overlay.js calls
    /// <c>.focus()</c> on it for Escape/outside dismissal. Without tabindex a plain div swallows
    /// that as a no-op and focus falls to <c>&lt;body&gt;</c>. <c>-1</c> makes it
    /// script-focusable without adding a Tab stop.
    /// </summary>
    [Fact]
    public void TmEntityPicker_AnchorWrapper_IsProgrammaticallyFocusable()
    {
        var cut = Render<TmEntityPicker<TestItem, int>>(p => p
            .Add(x => x.SearchProvider, SearchProvider)
            .Add(x => x.ValueSelector, i => i.Id)
            .Add(x => x.DisplaySelector, i => i.Name));

        cut.Find(".tm-entity-picker").GetAttribute("tabindex").Should().Be("-1");
    }
}
