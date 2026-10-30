using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetCustomFilterDialogTests : LocalizationTestBase
{
    [Fact]
    public void Renders_AndOr_AndOperators_Localized()
    {
        var cut = Render<TmSpreadsheetCustomFilterDialog>(p => p
            .Add(c => c.ColumnIndex, 1)
            .Add(c => c.Kind, SpreadsheetFilterKind.Number));

        var markup = cut.Markup;
        markup.Should().Contain("Custom filter");
        markup.Should().Contain("And");
        markup.Should().Contain("Or");
        markup.Should().Contain("Greater than");
        markup.Should().Contain("Between");
    }

    [Fact]
    public void Apply_NumberGreaterThan_BuildsCriteria()
    {
        SpreadsheetColumnFilter? applied = null;
        var cut = Render<TmSpreadsheetCustomFilterDialog>(p => p
            .Add(c => c.ColumnIndex, 1)
            .Add(c => c.Kind, SpreadsheetFilterKind.Number)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetColumnFilter?>(this, f => applied = f)));

        // operator defaults to first (Equals); choose GreaterThan
        var opSelect = cut.Find(".tm-spreadsheet-custom-filter__op select");
        opSelect.Change(SpreadsheetFilterOperator.GreaterThan.ToString());

        var valueInput = cut.Find(".tm-spreadsheet-custom-filter__value");
        valueInput.Input("100");

        cut.Find(".tm-spreadsheet-custom-filter__btn--ok").Click();

        applied.Should().NotBeNull();
        applied!.Kind.Should().Be(SpreadsheetFilterKind.Number);
        applied.Criteria.Should().NotBeNull();
        applied.Criteria!.Conditions.Should().ContainSingle();
        applied.Criteria.Conditions[0].Operator.Should().Be(SpreadsheetFilterOperator.GreaterThan);
        applied.Criteria.Conditions[0].Operand.Should().Be("100");
    }
}
