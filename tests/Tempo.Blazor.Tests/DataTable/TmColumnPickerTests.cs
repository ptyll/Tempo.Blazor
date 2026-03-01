using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmColumnPickerTests : LocalizationTestBase
{
    private static IReadOnlyList<ColumnVisibilityItem> AllVisible =>
    [
        new("Name",   "Name",       IsVisible: true,  IsHideable: true),
        new("Role",   "Role",       IsVisible: true,  IsHideable: true),
        new("Dept",   "Department", IsVisible: true,  IsHideable: true),
        new("Score",  "Score",      IsVisible: false, IsHideable: true),
        new("Id",     "ID",         IsVisible: true,  IsHideable: false), // not hideable
    ];

    private IRenderedComponent<TmColumnPicker> OpenPicker(Action<ComponentParameterCollectionBuilder<TmColumnPicker>>? extra = null)
    {
        var cut = RenderComponent<TmColumnPicker>(p =>
        {
            p.Add(c => c.Columns, AllVisible);
            extra?.Invoke(p);
        });
        // Open the dropdown
        cut.Find(".tm-column-picker-toggle").Click();
        return cut;
    }

    [Fact]
    public void ColumnPicker_ShowsAllHideableColumns()
    {
        var cut = OpenPicker();

        // 4 hideable columns (Id is not hideable and should not appear)
        cut.FindAll(".tm-column-picker-item").Count.Should().Be(4);
    }

    [Fact]
    public void ColumnPicker_Toggle_HidesColumn()
    {
        string? toggledKey = null;
        var cut = OpenPicker(p =>
            p.Add(c => c.OnToggleColumn,
                EventCallback.Factory.Create<string>(this, k => toggledKey = k)));

        // Click the checkbox for "Name" (currently visible → will hide)
        var nameCheckbox = cut.FindAll(".tm-column-picker-item input[type='checkbox']").First();
        nameCheckbox.Change(false);

        toggledKey.Should().Be("Name");
    }

    [Fact]
    public void ColumnPicker_Toggle_ShowsColumn()
    {
        string? toggledKey = null;
        var cut = OpenPicker(p =>
            p.Add(c => c.OnToggleColumn,
                EventCallback.Factory.Create<string>(this, k => toggledKey = k)));

        // "Score" is IsVisible=false → toggling it should show it
        var scoreCheckbox = cut.FindAll(".tm-column-picker-item input[type='checkbox']")
            .First(cb => cb.GetAttribute("data-key") == "Score");
        scoreCheckbox.Change(true);

        toggledKey.Should().Be("Score");
    }

    [Fact]
    public void ColumnPicker_ResetToDefault_FiresOnReset()
    {
        bool resetFired = false;
        var cut = OpenPicker(p =>
            p.Add(c => c.OnReset,
                EventCallback.Factory.Create(this, () => resetFired = true)));

        cut.Find(".tm-column-picker-reset").Click();

        resetFired.Should().BeTrue();
    }

    [Fact]
    public void ColumnPicker_ClosesWhenClickingOutside()
    {
        var cut = OpenPicker();
        // Picker panel is open
        cut.FindAll(".tm-column-picker-panel").Should().NotBeEmpty();

        // Click toggle again to close
        cut.Find(".tm-column-picker-toggle").Click();
        cut.FindAll(".tm-column-picker-panel").Should().BeEmpty();
    }
}
