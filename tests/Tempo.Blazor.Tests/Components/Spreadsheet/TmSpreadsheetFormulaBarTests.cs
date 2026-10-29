using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetFormulaBarTests : LocalizationTestBase
{
    [Fact]
    public void Render_Default_DisplaysCellReferenceAndValue()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "B2")
            .Add(p => p.DisplayValue, "Hello"));

        cut.Find(".tm-spreadsheet-formula-bar__ref").GetAttribute("value").Should().Be("B2");
        cut.Find(".tm-spreadsheet-formula-bar__display").TextContent.Trim().Should().Be("Hello");
    }

    [Fact]
    public void Render_EmptyCell_ShowsPlaceholder()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, (string?)null));

        cut.Find(".tm-spreadsheet-formula-bar__display").TextContent.Trim().Should().Contain("Enter formula or value");
    }

    [Fact]
    public void Click_Display_StartsEditing()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test"));

        cut.Find(".tm-spreadsheet-formula-bar__display").Click();

        cut.Find(".tm-spreadsheet-formula-bar__input").Should().NotBeNull();
    }

    [Fact]
    public void Enter_CommitsValue()
    {
        string? committed = null;
        (int RowDelta, int ColDelta) navigation = (0, 0);
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnValueCommitted, EventCallback.Factory.Create<string?>(this, v => committed = v))
            .Add(p => p.OnCommitNavigationRequested, EventCallback.Factory.Create<(int RowDelta, int ColDelta)>(this, move => navigation = move)));

        var input = cut.Find(".tm-spreadsheet-formula-bar__input");
        input.Input("New Value");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.WaitForAssertion(() =>
        {
            committed.Should().Be("New Value");
            navigation.Should().Be((1, 0));
        });
    }

    [Fact]
    public void ShiftEnter_CommitsValueAndRequestsUpNavigation()
    {
        string? committed = null;
        (int RowDelta, int ColDelta) navigation = (0, 0);
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A2")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnValueCommitted, EventCallback.Factory.Create<string?>(this, v => committed = v))
            .Add(p => p.OnCommitNavigationRequested, EventCallback.Factory.Create<(int RowDelta, int ColDelta)>(this, move => navigation = move)));

        var input = cut.Find(".tm-spreadsheet-formula-bar__input");
        input.Input("New Value");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter", ShiftKey = true });

        cut.WaitForAssertion(() =>
        {
            committed.Should().Be("New Value");
            navigation.Should().Be((-1, 0));
        });
    }

    [Fact]
    public void Escape_CancelsEdit()
    {
        bool cancelled = false;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnEditCancelled, EventCallback.Factory.Create(this, () => cancelled = true)));

        var input = cut.Find(".tm-spreadsheet-formula-bar__input");
        input.Input("Changed");
        input.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.WaitForAssertion(() => cancelled.Should().BeTrue());
    }

    [Fact]
    public void F2_RequestsTransferToInlineEditor()
    {
        var transferRequested = false;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "=SUM(A1:B2)")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnTransferToInlineEditorRequested, EventCallback.Factory.Create(this, () => transferRequested = true)));

        var input = cut.Find(".tm-spreadsheet-formula-bar__input");
        input.KeyDown(new KeyboardEventArgs { Key = "F2" });

        cut.WaitForAssertion(() => transferRequested.Should().BeTrue());
    }

    [Fact]
    public void EditStarted_EventFires()
    {
        bool started = false;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test")
            .Add(p => p.OnEditStarted, EventCallback.Factory.Create(this, () => started = true)));

        cut.Find(".tm-spreadsheet-formula-bar__display").Click();

        started.Should().BeTrue();
    }

    [Fact]
    public void ValueChanged_EventFiresOnInput()
    {
        string? changed = null;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnValueChanged, EventCallback.Factory.Create<string?>(this, v => changed = v)));

        cut.Find(".tm-spreadsheet-formula-bar__input").Input("X");

        cut.WaitForAssertion(() => changed.Should().Be("X"));
    }

    [Fact]
    public void DisplayValueChange_DuringEditing_DoesNotOverwriteLocalFormulaSession()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "=A1+B2")
            .Add(p => p.IsEditing, true));

        var input = cut.Find(".tm-spreadsheet-formula-bar__input");
        input.Input("=SUM(C1:C3)");

        cut.Render(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "=Z9+Y8")
            .Add(p => p.IsEditing, true));

        cut.Find(".tm-spreadsheet-formula-bar__input")
            .GetAttribute("value")
            .Should()
            .Be("=SUM(C1:C3)");
    }

    [Fact]
    public void NameBox_Enter_NavigatesToRef()
    {
        string? navigated = null;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test")
            .Add(p => p.OnNavigateToRef, EventCallback.Factory.Create<string>(this, v => navigated = v)));

        var nameBox = cut.Find(".tm-spreadsheet-formula-bar__ref");
        nameBox.Input("B5");
        nameBox.KeyDown("Enter");

        navigated.Should().Be("B5");
    }

    [Fact]
    public void NameBox_Escape_RevertsToActiveCellRef()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "C3")
            .Add(p => p.DisplayValue, "Test"));

        var nameBox = cut.Find(".tm-spreadsheet-formula-bar__ref");
        nameBox.Input("Z99");
        nameBox.KeyDown("Escape");

        nameBox.GetAttribute("value").Should().Be("C3");
    }

    // ── Ribbon v2 / formula bar polish (phase 5) ──

    [Fact]
    public void FormulaBar_FxLabel_AlwaysVisible()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "D5")
            .Add(p => p.DisplayValue, "=C5-B5"));

        var fx = cut.Find(".tm-spreadsheet-formula-bar__fx");
        fx.TextContent.Trim().Should().Be("fx");

        // It is a permanent part of the bar, not an edit-mode decoration: it survives editing too.
        cut.Render(parameters => parameters
            .Add(p => p.ActiveCellRef, "D5")
            .Add(p => p.DisplayValue, "=C5-B5")
            .Add(p => p.IsEditing, true));

        cut.FindAll(".tm-spreadsheet-formula-bar__fx").Count.Should().Be(1);
    }

    [Fact]
    public void FormulaBar_ConfirmCancelButtons_CommitAndRevert()
    {
        string? committed = null;
        var cancelled = false;
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnValueCommitted, EventCallback.Factory.Create<string?>(this, v => committed = v))
            .Add(p => p.OnEditCancelled, EventCallback.Factory.Create(this, () => cancelled = true)));

        var confirm = cut.Find(".tm-spreadsheet-formula-bar__confirm");
        confirm.GetAttribute("aria-label").Should().Be("Confirm formula");
        var cancel = cut.Find(".tm-spreadsheet-formula-bar__cancel");
        cancel.GetAttribute("aria-label").Should().Be("Cancel formula");

        cut.Find(".tm-spreadsheet-formula-bar__input").Input("New Value");
        cut.Find(".tm-spreadsheet-formula-bar__confirm").Click();

        cut.WaitForAssertion(() => committed.Should().Be("New Value"));

        // A fresh edit session (the host first drops IsEditing after the commit, then re-opens it),
        // then the ✗ reverts instead of committing.
        cut.Render(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, false)
            .Add(p => p.OnEditCancelled, EventCallback.Factory.Create(this, () => cancelled = true)));
        cut.Render(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Old")
            .Add(p => p.IsEditing, true)
            .Add(p => p.OnEditCancelled, EventCallback.Factory.Create(this, () => cancelled = true)));

        cut.Find(".tm-spreadsheet-formula-bar__input").Input("Changed");
        cut.Find(".tm-spreadsheet-formula-bar__cancel").Click();
        cut.WaitForAssertion(() => cancelled.Should().BeTrue());
    }

    [Fact]
    public void FormulaBar_ConfirmCancelButtons_HiddenWhenNotEditing()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test"));

        cut.FindAll(".tm-spreadsheet-formula-bar__confirm").Should().BeEmpty();
        cut.FindAll(".tm-spreadsheet-formula-bar__cancel").Should().BeEmpty();
    }

    [Fact]
    public void FormulaBar_NameBox_ListsNamedRanges_AndNavigates()
    {
        var ranges = new List<SpreadsheetNamedRange>
        {
            new() { Name = "TaxRate", RefersTo = "=Sheet1!$B$2", Scope = NamedRangeScope.Workbook },
            new() { Name = "Total", RefersTo = "=Sheet1!$B$10", Scope = NamedRangeScope.Workbook },
        };
        string? navigated = null;

        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test")
            .Add(p => p.NamedRanges, ranges)
            .Add(p => p.OnNavigateToRef, EventCallback.Factory.Create<string>(this, v => navigated = v)));

        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Should().BeEmpty("the list is closed until the chevron is used");

        cut.Find(".tm-spreadsheet-formula-bar__ref-toggle").Click();

        var items = cut.FindAll(".tm-spreadsheet-formula-bar__ref-item");
        items.Count.Should().Be(2);
        items.Select(i => i.TextContent.Trim()).Should().ContainInOrder("TaxRate", "Total");

        items.First(i => i.TextContent.Contains("Total")).Click();

        navigated.Should().Be("Total");
        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Should().BeEmpty();
    }

    [Fact]
    public void FormulaBar_NameBox_NoNamedRanges_KeepsTheLegacyInput()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "Test"));

        cut.FindAll(".tm-spreadsheet-formula-bar__ref-toggle").Should().BeEmpty(
            "a workbook without named ranges shows no empty dropdown");
        cut.Find(".tm-spreadsheet-formula-bar__ref").Should().NotBeNull();
    }

    // ── Review round: list dismissal paths ──

    [Fact]
    public void FormulaBar_NameBox_EscapeOnTheToggle_ClosesTheList()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "x")
            .Add(p => p.NamedRanges, new List<SpreadsheetNamedRange>
            {
                new() { Name = "Total", RefersTo = "=A1" },
            }));

        var toggle = cut.Find(".tm-spreadsheet-formula-bar__ref-toggle");
        toggle.Click();
        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Count.Should().Be(1);

        toggle.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Should().BeEmpty();
    }

    [Fact]
    public void FormulaBar_NameBox_OutsideClick_ClosesTheList()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "x")
            .Add(p => p.NamedRanges, new List<SpreadsheetNamedRange>
            {
                new() { Name = "Total", RefersTo = "=A1" },
            }));

        cut.Find(".tm-spreadsheet-formula-bar__ref-toggle").Click();
        cut.Find(".tm-spreadsheet-formula-bar__ref-overlay").Click();

        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Should().BeEmpty();
    }

    [Fact]
    public void FormulaBar_NameBox_FocusOutOfTheWrapper_ClosesTheList()
    {
        var cut = Render<TmSpreadsheetFormulaBar>(parameters => parameters
            .Add(p => p.ActiveCellRef, "A1")
            .Add(p => p.DisplayValue, "x")
            .Add(p => p.NamedRanges, new List<SpreadsheetNamedRange>
            {
                new() { Name = "Total", RefersTo = "=A1" },
            }));

        cut.Find(".tm-spreadsheet-formula-bar__ref-toggle").Click();
        cut.Find(".tm-spreadsheet-formula-bar__name-box").FocusOut();

        cut.FindAll(".tm-spreadsheet-formula-bar__ref-item").Should().BeEmpty();
    }
}
