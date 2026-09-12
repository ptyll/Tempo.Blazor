using System.Globalization;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetFilterDropdownTests : LocalizationTestBase
{
    private static SpreadsheetSheet BuildSheet()
    {
        var sheet = new SpreadsheetSheet();
        void Set(int row, string text)
            => sheet.Cells[$"A{row + 1}"] = new SpreadsheetCell { Value = text, DisplayValue = text, DataType = SpreadsheetDataType.Text };
        Set(0, "Fruit");
        Set(1, "Apple");
        Set(2, "Banana");
        Set(3, "Apple");
        return sheet;
    }

    private static SpreadsheetAutoFilter Filter() => new(new SpreadsheetRange(0, 0, 3, 0));

    [Fact]
    public void Renders_SortOptions_SearchAndValues_Localized()
    {
        var sheet = BuildSheet();
        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture));

        var markup = cut.Markup;
        markup.Should().Contain("Sort A → Z");
        markup.Should().Contain("Sort Z → A");
        markup.Should().Contain("(Select all)");
        // distinct values listed once
        markup.Should().Contain("Apple");
        markup.Should().Contain("Banana");
        cut.FindAll("input[type=checkbox]").Count.Should().BeGreaterThanOrEqualTo(3); // select-all + 2 values
    }

    [Fact]
    public void Apply_WithSubset_RaisesValuesFilter()
    {
        var sheet = BuildSheet();
        SpreadsheetColumnFilter? applied = null;
        var applyHandled = false;

        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetColumnFilter?>(this, f =>
            {
                applied = f;
                applyHandled = true;
            })));

        // Uncheck "Banana" (find its label by text)
        var bananaCheckbox = cut.FindAll(".tm-spreadsheet-filter-dropdown__check")
            .First(e => e.TextContent.Contains("Banana"))
            .QuerySelector("input")!;
        bananaCheckbox.Change(false);

        cut.Find(".tm-spreadsheet-filter-dropdown__btn--ok").Click();

        applyHandled.Should().BeTrue();
        applied.Should().NotBeNull();
        applied!.Kind.Should().Be(SpreadsheetFilterKind.Values);
        applied.AllowedValues.Should().Contain("Apple");
        applied.AllowedValues.Should().NotContain("Banana");
    }

    [Fact]
    public void SortAscending_RaisesCallbackWithColumn()
    {
        var sheet = BuildSheet();
        int? sortedColumn = null;

        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.OnSortAscending, EventCallback.Factory.Create<int>(this, c => sortedColumn = c)));

        cut.FindAll(".tm-spreadsheet-filter-dropdown__item")
            .First(e => e.TextContent.Contains("Sort A → Z"))
            .Click();

        sortedColumn.Should().Be(0);
    }

    // ── Keyboard activation ─────────────────────────────────────────────────
    // Enter->apply is scoped to the search input; the buttons are native
    // <button>s whose bubbled keydown must not also apply the filter.

    [Fact]
    public void EnterOnCancelButton_Closes_WithoutApplying()
    {
        // Real sequence for Enter on a focused <button>: keydown -> click.
        // A container-level Enter->apply would apply the filter on top of the
        // button's own close click.
        var sheet = BuildSheet();
        var applies = 0;
        var closes = 0;
        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetColumnFilter?>(this, _ => applies++))
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closes++)));

        var cancel = cut.Find(".tm-spreadsheet-filter-dropdown__btn--cancel");
        cancel.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.Find(".tm-spreadsheet-filter-dropdown__btn--cancel").Click();

        closes.Should().Be(1);
        applies.Should().Be(0);
    }

    [Fact]
    public void EnterInSearchInput_AppliesOnce()
    {
        var sheet = BuildSheet();
        var applies = 0;
        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetColumnFilter?>(this, _ => applies++)));

        cut.Find(".tm-spreadsheet-filter-dropdown__search")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        applies.Should().Be(1);
    }

    [Fact]
    public void EnterOnSortButton_DoesNotApplyFilter()
    {
        var sheet = BuildSheet();
        var applies = 0;
        int? sortedColumn = null;
        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, Filter())
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture)
            .Add(c => c.OnSortAscending, EventCallback.Factory.Create<int>(this, c => sortedColumn = c))
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetColumnFilter?>(this, _ => applies++)));

        var sort = cut.FindAll(".tm-spreadsheet-filter-dropdown__item")
            .First(e => e.TextContent.Contains("Sort A → Z"));
        sort.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-spreadsheet-filter-dropdown__item")
            .First(e => e.TextContent.Contains("Sort A → Z")).Click();

        sortedColumn.Should().Be(0);
        applies.Should().Be(0);
    }
}
