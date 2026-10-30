using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetSheetTabsTests : LocalizationTestBase
{
    [Fact]
    public void Render_MultipleSheets_DisplaysTabs()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        var tabs = cut.FindAll(".tm-spreadsheet-sheet-tab");
        tabs.Count.Should().Be(2);
        tabs[0].TextContent.Should().Contain("Sheet1");
        tabs[1].TextContent.Should().Contain("Sheet2");
    }

    [Fact]
    public void Render_ActiveSheet_HasActiveClass()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 1));

        var tabs = cut.FindAll(".tm-spreadsheet-sheet-tab");
        tabs[0].ClassList.Should().NotContain("tm-spreadsheet-sheet-tab--active");
        tabs[1].ClassList.Should().Contain("tm-spreadsheet-sheet-tab--active");
    }

    [Fact]
    public void Click_Tab_FiresActiveSheetChanged()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };
        int? receivedIndex = null;

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0)
            .Add(p => p.OnActiveSheetChanged, EventCallback.Factory.Create<int>(this, i => receivedIndex = i)));

        var tabs = cut.FindAll(".tm-spreadsheet-sheet-tab");
        tabs[1].Click();

        receivedIndex.Should().Be(1);
    }

    [Fact]
    public void AddButton_Click_FiresAddSheetRequested()
    {
        var sheets = new List<SpreadsheetSheet> { new() { Name = "Sheet1" } };
        bool fired = false;

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0)
            .Add(p => p.OnAddSheetRequested, EventCallback.Factory.Create(this, () => fired = true)));

        var addBtn = cut.Find(".tm-spreadsheet-sheet-tabs__add");
        addBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void CloseButton_Click_FiresDeleteSheetRequested()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };
        int? receivedIndex = null;

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0)
            .Add(p => p.OnDeleteSheetRequested, EventCallback.Factory.Create<int>(this, i => receivedIndex = i)));

        var closeBtn = cut.Find(".tm-spreadsheet-sheet-tab__close");
        closeBtn.Click();

        receivedIndex.Should().Be(0);
    }

    [Fact]
    public void SingleSheet_HidesCloseButton()
    {
        var sheets = new List<SpreadsheetSheet> { new() { Name = "Sheet1" } };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        cut.FindAll(".tm-spreadsheet-sheet-tab__close").Count.Should().Be(0);
    }

    [Fact]
    public void RightClick_Tab_OpensContextMenu()
    {
        var sheets = new List<SpreadsheetSheet> { new() { Name = "Sheet1" } };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        var tab = cut.Find(".tm-spreadsheet-sheet-tab");
        tab.ContextMenu();

        cut.FindAll(".tm-spreadsheet-sheet-tabs__context-menu").Count.Should().Be(1);
        cut.FindAll(".tm-spreadsheet-sheet-tabs__context-item").Count.Should().Be(2);
    }

    [Fact]
    public void ContextMenu_Rename_FiresRenameSheetRequested()
    {
        var sheets = new List<SpreadsheetSheet> { new() { Name = "Sheet1" } };
        (int Index, string NewName)? received = null;

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0)
            .Add(p => p.OnRenameSheetRequested, EventCallback.Factory.Create<(int, string)>(this, v => received = v)));

        var tab = cut.Find(".tm-spreadsheet-sheet-tab");
        tab.ContextMenu();

        // Enter rename mode
        var renameItem = cut.FindAll(".tm-spreadsheet-sheet-tabs__context-item")
            .First(e => e.TextContent.Contains("Rename"));
        renameItem.Click();

        // Input should be visible
        cut.FindAll(".tm-spreadsheet-sheet-tab__input").Count.Should().Be(1);

        var input = cut.Find(".tm-spreadsheet-sheet-tab__input");
        input.Input("NewName");
        input.KeyDown("Enter");

        received.Should().NotBeNull();
        received!.Value.Index.Should().Be(0);
        received!.Value.NewName.Should().Be("NewName");
    }

    // ── Phase 5: tab close buttons, underline and overflow arrows ──

    [Fact]
    public void SheetTabs_ShowCloseButtonsFalse_HidesX_DeleteViaContextMenu()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };
        int? deleted = null;

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0)
            .Add(p => p.ShowTabCloseButtons, false)
            .Add(p => p.OnDeleteSheetRequested, EventCallback.Factory.Create<int>(this, i => deleted = i)));

        cut.FindAll(".tm-spreadsheet-sheet-tab__close").Should().BeEmpty(
            "the host asked for the clean tab look (close lives in the context menu)");

        cut.FindAll(".tm-spreadsheet-sheet-tab")[1].ContextMenu();
        cut.FindAll(".tm-spreadsheet-sheet-tabs__context-item")
            .First(i => i.TextContent.Contains("Delete"))
            .Click();

        deleted.Should().Be(1, "deleting from the context menu must keep working without the X buttons");
    }

    [Fact]
    public void SheetTabs_ShowCloseButtons_DefaultsToTrue()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        cut.FindAll(".tm-spreadsheet-sheet-tab__close").Count.Should().Be(2);
    }

    [Fact]
    public void SheetTabs_ActiveTab_CarriesTheUnderlineContract()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 1));

        var active = cut.FindAll(".tm-spreadsheet-sheet-tab")[1];
        active.ClassList.Should().Contain("tm-spreadsheet-sheet-tab--active");
        active.QuerySelector(".tm-spreadsheet-sheet-tab__label").Should().NotBeNull();
    }

    [Fact]
    public void SheetTabs_ScrollArrows_AppearOnOverflow_AndScrollTheStrip()
    {
        var sheets = Enumerable.Range(1, 12).Select(i => new SpreadsheetSheet { Name = $"Sheet{i}" }).ToList();

        JSInterop.Setup<bool>("tmSpreadsheetSheetTabs.hasOverflow", _ => true).SetResult(true);
        var scroll = JSInterop.SetupVoid("tmSpreadsheetSheetTabs.scrollTabs", _ => true);
        scroll.SetVoidResult();

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-spreadsheet-sheet-tabs__scroll-button").Count.Should().Be(2));

        var next = cut.FindAll(".tm-spreadsheet-sheet-tabs__scroll-button")
            .First(b => b.GetAttribute("data-direction") == "right");
        next.GetAttribute("aria-label").Should().Be("Scroll sheets right");
        next.Click();

        cut.WaitForAssertion(() => scroll.Invocations.Count.Should().BeGreaterThan(0));
    }

    [Fact]
    public void SheetTabs_ScrollArrows_HiddenWithoutOverflow()
    {
        var sheets = new List<SpreadsheetSheet>
        {
            new() { Name = "Sheet1" },
            new() { Name = "Sheet2" },
        };

        JSInterop.Setup<bool>("tmSpreadsheetSheetTabs.hasOverflow", _ => true).SetResult(false);

        var cut = Render<TmSpreadsheetSheetTabs>(parameters => parameters
            .Add(p => p.Sheets, sheets)
            .Add(p => p.ActiveIndex, 0));

        cut.FindAll(".tm-spreadsheet-sheet-tabs__scroll-button").Should().BeEmpty();
    }
}
