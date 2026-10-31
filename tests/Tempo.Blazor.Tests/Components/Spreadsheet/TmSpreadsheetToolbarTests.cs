using System.Text.RegularExpressions;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

public class TmSpreadsheetToolbarTests : LocalizationTestBase
{
    [Fact]
    public void Render_Default_DisplaysAllToolGroups()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        cut.FindAll(".tm-spreadsheet-toolbar__button").Count.Should().BeGreaterThan(5);
        cut.FindAll(".tm-spreadsheet-toolbar__group").Count.Should().BeGreaterThan(3);
    }

    [Fact]
    public void UndoButton_Disabled_WhenCannotUndo()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.CanUndo, false));

        var undoBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")[0];
        undoBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void UndoButton_Enabled_WhenCanUndo()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.CanUndo, true));

        var undoBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")[0];
        undoBtn.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void BoldButton_Active_WhenIsBold()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.IsBold, true));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var boldBtn = buttons.First(b => b.GetAttribute("title") == "Bold");
        boldBtn.ClassList.Should().Contain("tm-spreadsheet-toolbar__button--active");
    }

    [Fact]
    public void BoldButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnBoldToggle, EventCallback.Factory.Create(this, () => fired = true)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var boldBtn = buttons.First(b => b.GetAttribute("title") == "Bold");
        boldBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void ItalicButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnItalicToggle, EventCallback.Factory.Create(this, () => fired = true)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var italicBtn = buttons.First(b => b.GetAttribute("title") == "Italic");
        italicBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void UnderlineButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnUnderlineToggle, EventCallback.Factory.Create(this, () => fired = true)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var underlineBtn = buttons.First(b => b.GetAttribute("title") == "Underline");
        underlineBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void AlignLeftButton_Active_WhenAlignLeft()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.SelectedHorizontalAlign, "left"));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var alignBtn = buttons.First(b => b.GetAttribute("title") == "Align left");
        alignBtn.ClassList.Should().Contain("tm-spreadsheet-toolbar__button--active");
    }

    [Fact]
    public void AlignCenterButton_Click_FiresEvent()
    {
        string? align = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnAlignChanged, EventCallback.Factory.Create<string?>(this, v => align = v)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var centerBtn = buttons.First(b => b.GetAttribute("title") == "Align center");
        centerBtn.Click();

        align.Should().Be("center");
    }

    [Fact]
    public void UndoButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.CanUndo, true)
            .Add(p => p.OnUndo, EventCallback.Factory.Create(this, () => fired = true)));

        var undoBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")[0];
        undoBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void RedoButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.CanRedo, true)
            .Add(p => p.OnRedo, EventCallback.Factory.Create(this, () => fired = true)));

        var redoBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")[1];
        redoBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void FontFamily_Changed_FiresEvent()
    {
        string? font = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnFontFamilyChanged, EventCallback.Factory.Create<string?>(this, v => font = v)));

        // The TmSelect component renders a select element
        var select = cut.Find("select");
        select.Change("Segoe UI");

        font.Should().Be("Segoe UI");
    }

    [Fact]
    public void NumberFormat_Select_FiresEvent()
    {
        string? format = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnNumberFormatChanged, EventCallback.Factory.Create<string?>(this, v => format = v)));

        var selects = cut.FindAll("select");
        // Number format select is the third one (font, size, number format)
        var numberFormatSelect = selects[2];
        numberFormatSelect.Change("0.00");

        format.Should().Be("0.00");
    }

    [Fact]
    public void IncreaseDecimals_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnIncreaseDecimals, EventCallback.Factory.Create(this, () => fired = true)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var incBtn = buttons.First(b => b.GetAttribute("title") == "Increase decimal places");
        incBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void DecreaseDecimals_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnDecreaseDecimals, EventCallback.Factory.Create(this, () => fired = true)));

        var buttons = cut.FindAll(".tm-spreadsheet-toolbar__button");
        var decBtn = buttons.First(b => b.GetAttribute("title") == "Decrease decimal places");
        decBtn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void CopyButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnCopy, EventCallback.Factory.Create(this, () => fired = true)));

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Copy");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void CutButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnCut, EventCallback.Factory.Create(this, () => fired = true)));

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Cut");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void PasteButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnPaste, EventCallback.Factory.Create(this, () => fired = true)));

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Paste");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void InsertRowButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnInsertRow, EventCallback.Factory.Create(this, () => fired = true)));

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert row");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void DeleteRowButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnDeleteRow, EventCallback.Factory.Create(this, () => fired = true)));

        // The reference groups row/column deletion behind the large "Delete" split action.
        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options").Click();

        var btn = cut.FindAll("[title='Delete row']")
            .First(b => b.ClassList.Contains("tm-border-picker__item"));
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void Toolbar_DeleteSplit_OffersRowAndColumn()
    {
        bool row = false;
        bool column = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnDeleteRow, EventCallback.Factory.Create(this, () => row = true))
            .Add(p => p.OnDeleteColumn, EventCallback.Factory.Create(this, () => column = true)));

        var deleteGroup = cut.FindAll(".tm-spreadsheet-toolbar__group")
            .First(g => g.TextContent.Contains("Cells"));
        deleteGroup.QuerySelectorAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete")
            .ClassList.Should().Contain("tm-spreadsheet-toolbar__button--large");

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options").Click();

        cut.FindAll("[title='Delete column']")
            .First(b => b.ClassList.Contains("tm-border-picker__item")).Click();

        column.Should().BeTrue();
        row.Should().BeFalse();
    }

    [Fact]
    public void Toolbar_DeleteSplit_ClosesWithEscape()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options").Click();
        cut.FindAll("[title='Delete row']").Count.Should().Be(1);

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options")
            .KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tm-spreadsheet-toolbar__split-menu").Should().BeEmpty();
    }

    [Fact]
    public void InsertColumnButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnInsertColumn, EventCallback.Factory.Create(this, () => fired = true)));

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert column");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void DeleteColumnButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnDeleteColumn, EventCallback.Factory.Create(this, () => fired = true)));

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options").Click();

        var btn = cut.FindAll("[title='Delete column']")
            .First(b => b.ClassList.Contains("tm-border-picker__item"));
        btn.Click();

        fired.Should().BeTrue();
    }

    // ── Tabs ──

    [Fact]
    public void Render_DisplaysTabHeaders()
    {
        var cut = Render<TmSpreadsheetToolbar>();
        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs.Count.Should().Be(5);
        tabs[0].TextContent.Should().Contain("Home");
        tabs[1].TextContent.Should().Contain("Insert");
        tabs[2].TextContent.Should().Contain("Data");
        tabs[3].TextContent.Should().Contain("View");
        tabs[4].TextContent.Should().Contain("File");
    }

    [Fact]
    public void Click_InsertTab_SwitchesContent()
    {
        var cut = Render<TmSpreadsheetToolbar>();
        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[1].Click(); // Insert tab

        // The parameter is the host's value; the component keeps its own _activeTab and only raises
        // ActiveTabChanged.
        cut.Instance.ActiveTab.Should().Be("Home");
        cut.FindAll("[data-tm-tab='Insert'][role='tabpanel']").Count.Should().Be(1);
        var insertBtn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .FirstOrDefault(b => b.GetAttribute("title") == "Insert link");
        insertBtn.Should().NotBeNull();
    }

    [Fact]
    public void InsertLinkButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnInsertLink, EventCallback.Factory.Create(this, () => fired = true)));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[1].Click(); // Insert tab

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert link");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void InsertImageButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnInsertImage, EventCallback.Factory.Create(this, () => fired = true)));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[1].Click(); // Insert tab

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Insert image");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void MergeCellsButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnMergeCells, EventCallback.Factory.Create(this, () => fired = true)));

        // Merge cells lives in the Home tab's Alignment group since the ribbon v2.
        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Merge cells");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void ToggleGridLinesButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnToggleGridLines, EventCallback.Factory.Create(this, () => fired = true)));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[3].Click(); // View tab

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Grid lines");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void OpenButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnOpen, EventCallback.Factory.Create(this, () => fired = true)));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[4].Click(); // File tab

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Open file");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void DownloadButton_Click_FiresEvent()
    {
        bool fired = false;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnDownload, EventCallback.Factory.Create(this, () => fired = true)));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs[4].Click(); // File tab

        var btn = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Download");
        btn.Click();

        fired.Should().BeTrue();
    }

    [Fact]
    public void CustomTools_RenderInCorrectTab()
    {
        var customTools = new List<SpreadsheetCustomTool>
        {
            new() { IconName = "copy", Title = "My Tool", Tab = "Home", Order = 0 }
        };

        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.CustomTools, customTools));

        var tool = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "My Tool");
        tool.QuerySelector("svg").Should().NotBeNull("a custom tool renders its IconName through TmIcon, never as text");
        tool.TextContent.Should().Contain("My Tool");
    }

    // ── Ribbon v2 (phase 5) ──

    [Fact]
    public void Toolbar_HomeTab_RendersLabelledGroups()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        var labels = cut.FindAll(".tm-spreadsheet-toolbar__group-label")
            .Select(label => label.TextContent.Trim())
            .ToList();

        labels.Should().ContainInOrder("Clipboard", "Font", "Alignment", "Number", "Cells");
        labels.Should().Contain("Insert");
    }

    [Fact]
    public void Toolbar_Group_Labels_AreBoundToTheirGroup()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        foreach (var group in cut.FindAll(".tm-spreadsheet-toolbar__group"))
        {
            group.QuerySelector(".tm-spreadsheet-toolbar__group-label").Should().NotBeNull(
                "every ribbon group carries its visible name under the buttons");
            group.QuerySelector(".tm-spreadsheet-toolbar__group-body").Should().NotBeNull();
        }
    }

    [Fact]
    public void Toolbar_LargeButtons_ShowIconAboveText()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        foreach (var title in new[] { "Undo", "Redo", "Paste", "Insert row", "Insert column", "Delete", "Insert link", "Insert image" })
        {
            var button = cut.FindAll(".tm-spreadsheet-toolbar__button")
                .First(b => b.GetAttribute("title") == title);
            button.ClassList.Should().Contain("tm-spreadsheet-toolbar__button--large", $"{title} is a large ribbon button");
            button.QuerySelector(".tm-spreadsheet-toolbar__button-icon svg").Should().NotBeNull();
            button.QuerySelector(".tm-spreadsheet-toolbar__button-label").Should().NotBeNull();
        }
    }

    [Fact]
    public void Toolbar_SmallTextButtons_ShowIconWithText()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        foreach (var title in new[] { "Cut", "Copy" })
        {
            var button = cut.FindAll(".tm-spreadsheet-toolbar__button")
                .First(b => b.GetAttribute("title") == title);
            button.ClassList.Should().Contain("tm-spreadsheet-toolbar__button--small");
            button.QuerySelector(".tm-spreadsheet-toolbar__button-icon svg").Should().NotBeNull();
            button.QuerySelector(".tm-spreadsheet-toolbar__button-label").Should().NotBeNull();
        }
    }

    [Fact]
    public void Toolbar_NoTextGlyphIcons()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        // Open the border picker too: its glyphs were the last Unicode "icons" in the ribbon.
        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Border picker")
            .Click();

        string[] forbidden = ["←|", "|→", ".0→", ".00→", "☐", "⊞", "▣", "◼", "▬", "▰", "═", "⊟", "▭", "▏", "▕", "⚙", "▾", "▲", "▼"];
        var text = cut.Markup;
        foreach (var glyph in forbidden)
        {
            text.Should().NotContain(glyph, $"'{glyph}' is a text glyph, not an icon");
        }

        cut.FindAll(".tm-border-picker__item").Count.Should().BeGreaterThan(5);
        foreach (var item in cut.FindAll(".tm-border-picker__item"))
        {
            item.QuerySelector("svg").Should().NotBeNull("every border entry renders a registered SVG icon");
        }
    }

    [Fact]
    public void Toolbar_BorderPicker_UsesTheRegisteredPackageIcons()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        var lastBorderButton = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "All Borders");
        var allBordersIcon = lastBorderButton.InnerHtml;

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Border picker")
            .Click();

        var icons = cut.FindAll(".tm-border-picker__item svg").Count;
        icons.Should().BeGreaterThanOrEqualTo(11, "the picker offers borders + edge presets + the more-borders entry");

        cut.FindAll(".tm-border-picker__item")
            .First(b => b.TextContent.Contains("Outside"))
            .Click();

        // The main button now paints the outside-border icon and names the last used preset: the
        // package registers its own stroke icons because lucide has no cell-border glyphs.
        var updated = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Outside Borders");
        updated.InnerHtml.Should().NotBe(allBordersIcon);
    }

    [Fact]
    public void Toolbar_BorderDropdown_AppliesEachPreset()
    {
        BorderPreset? preset = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnBorderPresetChanged, EventCallback.Factory.Create<BorderPreset>(this, p => preset = p)));

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Border picker")
            .Click();

        cut.FindAll(".tm-border-picker__item")
            .First(b => b.TextContent.Contains("Outside"))
            .Click();

        preset.Should().Be(BorderPreset.OutsideBorders);
    }

    [Fact]
    public void Toolbar_VerticalAlign_RaisesCallback()
    {
        string? align = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.OnVerticalAlignChanged, EventCallback.Factory.Create<string?>(this, v => align = v)));

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Align middle")
            .Click();

        align.Should().Be("middle");
    }

    [Fact]
    public void Toolbar_VerticalAlign_ActiveState_FollowsSelection()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.SelectedVerticalAlign, "top"));

        var top = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Align top");
        top.ClassList.Should().Contain("tm-spreadsheet-toolbar__button--active");
    }

    [Fact]
    public void Toolbar_MergeCells_InAlignmentGroup()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        var alignmentGroup = cut.FindAll(".tm-spreadsheet-toolbar__group")
            .First(g => g.TextContent.Contains("Alignment"));

        alignmentGroup.QuerySelector(".tm-spreadsheet-toolbar__group-label")!.TextContent.Trim()
            .Should().Be("Alignment");
        alignmentGroup.QuerySelectorAll(".tm-spreadsheet-toolbar__button")
            .Any(b => b.GetAttribute("title") == "Merge cells")
            .Should().BeTrue("Merge cells moved from the View tab into the Home tab's Alignment group");
        cut.FindAll(".tm-spreadsheet-toolbar__group")
            .Count(g => g.QuerySelectorAll(".tm-spreadsheet-toolbar__button").Any(b => b.GetAttribute("title") == "Merge cells"))
            .Should()
            .Be(1);
    }

    [Fact]
    public void Toolbar_Tabs_AreHomeInsertDataView()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs.Count.Should().Be(5);
        tabs[0].TextContent.Should().Contain("Home");
        tabs[1].TextContent.Should().Contain("Insert");
        tabs[2].TextContent.Should().Contain("Data");
        tabs[3].TextContent.Should().Contain("View");
        tabs[4].TextContent.Should().Contain("File");
    }

    [Fact]
    public void Toolbar_Tabs_AreHomeInsertDataView_WhenHeaderFileActions()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ShowFileTab, false));

        var tabs = cut.FindAll(".tm-spreadsheet-toolbar__tab");
        tabs.Count.Should().Be(4);
        tabs.Select(t => t.TextContent.Trim()).Should().ContainInOrder("Home", "Insert", "Data", "View");
        cut.Markup.Should().NotContain(">File<");
    }

    [Fact]
    public void Toolbar_ShowFileTabTrue_KeepsLegacyFileTab()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ShowFileTab, true));

        cut.FindAll(".tm-spreadsheet-toolbar__tab").Count.Should().Be(5);
    }

    [Fact]
    public void Toolbar_ActiveTab_IsBindable()
    {
        var activeTab = typeof(TmSpreadsheetToolbar).GetProperty(nameof(TmSpreadsheetToolbar.ActiveTab))!;
        activeTab.GetCustomAttributes(typeof(ParameterAttribute), inherit: true).Should().NotBeEmpty(
            "ActiveTab is part of the public parameter surface");

        var changed = typeof(TmSpreadsheetToolbar).GetProperty(nameof(TmSpreadsheetToolbar.ActiveTabChanged))!;
        changed.GetCustomAttributes(typeof(ParameterAttribute), inherit: true).Should().NotBeEmpty();

        string? raised = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTabChanged, EventCallback.Factory.Create<string>(this, tab => raised = tab)));

        cut.FindAll(".tm-spreadsheet-toolbar__tab")
            .First(t => t.TextContent.Contains("Data"))
            .Click();

        cut.WaitForAssertion(() => raised.Should().Be("Data"));
    }

    [Fact]
    public void Toolbar_DataTab_KeepsTheCommandTitles_TheE2ESelectorsUse()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTab, "Data"));

        string[] titles = ["Filter", "Sort ascending", "Sort descending", "Custom sort…", "Remove duplicates", "Text to columns", "Paste special", "Data validation...", "Name Manager"];
        var rendered = cut.FindAll(".tm-spreadsheet-toolbar__button").Select(b => b.GetAttribute("title")).ToList();
        foreach (var title in titles)
        {
            rendered.Should().Contain(title, "the Data tab command titles are the E2E selector contract");
        }
    }

    [Fact]
    public void Toolbar_DataTab_GroupsItsCommands()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTab, "Data"));

        cut.FindAll(".tm-spreadsheet-toolbar__group-label")
            .Select(label => label.TextContent.Trim())
            .Should()
            .Contain("Data");
    }

    [Fact]
    public void Toolbar_ViewTab_ShowsGridOnly()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTab, "View"));

        var labels = cut.FindAll(".tm-spreadsheet-toolbar__group-label").Select(l => l.TextContent.Trim()).ToList();
        labels.Should().Contain("View");

        var titles = cut.FindAll(".tm-spreadsheet-toolbar__button").Select(b => b.GetAttribute("title")).ToList();
        titles.Should().Contain("Grid lines");
        titles.Should().NotContain("Merge cells", "Merge cells moved to the Home tab's Alignment group");
    }

    [Fact]
    public void Toolbar_IndentButtons_UseIcons()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        foreach (var title in new[] { "Increase Indent", "Decrease Indent" })
        {
            var button = cut.FindAll(".tm-spreadsheet-toolbar__button")
                .First(b => b.GetAttribute("title") == title);
            button.QuerySelector("svg").Should().NotBeNull();
            button.TextContent.Trim().Should().BeEmpty("an icon button carries no text glyph");
        }
    }

    [Fact]
    public void Toolbar_DecimalButtons_UseRegisteredIcons()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        foreach (var title in new[] { "Increase decimal places", "Decrease decimal places" })
        {
            var button = cut.FindAll(".tm-spreadsheet-toolbar__button")
                .First(b => b.GetAttribute("title") == title);
            button.QuerySelector("svg").Should().NotBeNull();
            button.TextContent.Trim().Should().BeEmpty();
        }
    }

    // ── Review round: stable group/tab contract, internal ActiveTab, top-layer menus ──

    private static string[] GroupsOf(IRenderedComponent<TmSpreadsheetToolbar> cut) =>
        cut.FindAll("[data-tm-group]")
            .Select(group => group.GetAttribute("data-tm-group")!)
            .Distinct()
            .ToArray();

    [Theory]
    [InlineData("Home", new[] { "clipboard", "font", "alignment", "number", "cells", "insert" })]
    [InlineData("Insert", new[] { "links" })]
    [InlineData("Data", new[] { "data" })]
    [InlineData("View", new[] { "view" })]
    [InlineData("File", new[] { "file" })]
    public void Toolbar_Groups_ExposeTheFrozenDataTmGroupContract(string tab, string[] expected)
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters.Add(p => p.ActiveTab, tab));

        GroupsOf(cut).Should().Equal(expected);
    }

    [Fact]
    public void Toolbar_CustomTools_AreTheirOwnGroup_WithDataTmGroupAndToolId()
    {
        var tools = new List<SpreadsheetCustomTool>
        {
            new() { Id = "tool-a", IconName = "copy", Title = "Tool A", Tab = "home", Order = 1 },
            new() { Id = "tool-b", IconName = "cut", Title = "Tool B", Tab = "Home", Order = 0 },
        };

        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters.Add(p => p.CustomTools, tools));

        var customGroups = cut.FindAll("[data-tm-group='custom']");
        customGroups.Count.Should().Be(2, "every custom tool gets its own group (and the Tab match is case-insensitive)");
        customGroups.Select(g => g.GetAttribute("data-tool-id")).Should().Equal("tool-b", "tool-a");
        customGroups[0].QuerySelectorAll(".tm-spreadsheet-toolbar__button[data-tool-id]").Count.Should().Be(1);
    }

    [Fact]
    public void Toolbar_TabsAndPanels_ExposeDataTmTabAndTheAriaTabsContract()
    {
        var cut = Render<TmSpreadsheetToolbar>();
        var tabs = cut.FindAll("[role='tab']");
        var panel = cut.Find("[role='tabpanel']");

        tabs.Count.Should().Be(5);
        tabs.Select(t => t.GetAttribute("data-tm-tab")).Should().Equal("Home", "Insert", "Data", "View", "File");
        panel.GetAttribute("data-tm-tab").Should().Be("Home");
        tabs[0].GetAttribute("tabindex").Should().Be("0");
        tabs.Skip(1).Select(t => t.GetAttribute("tabindex")).Should().OnlyContain(value => value == "-1",
            "roving tabindex keeps one tab stop");
        tabs[0].GetAttribute("aria-selected").Should().Be("true");

        // aria-controls/aria-labelledby wire the tab to its panel, with per-instance ids.
        tabs[0].GetAttribute("aria-controls").Should().Be(panel.Id);
        panel.GetAttribute("aria-labelledby").Should().Be(tabs[0].Id);
        var second = Render<TmSpreadsheetToolbar>().Find("[role='tab']").Id;
        tabs[0].Id.Should().NotBe(second, "tab ids are per instance");
    }

    [Fact]
    public void Toolbar_ArrowKeys_MoveTheActiveTab_AndRaiseTheCallback()
    {
        string? raised = null;
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTabChanged, EventCallback.Factory.Create<string>(this, tab => raised = tab)));

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        cut.WaitForAssertion(() => raised.Should().Be("Insert"));
        cut.Find("[data-tm-tab='Insert'][role='tabpanel']").Should().NotBeNull();

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "End" });
        cut.WaitForAssertion(() => raised.Should().Be("File"));

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "Home" });
        cut.WaitForAssertion(() => raised.Should().Be("Home"));
    }

    [Fact]
    public void Toolbar_ActiveTabParameter_IsNotAssignedByTheComponent()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters.Add(p => p.ActiveTab, "Home"));

        cut.FindAll("[role='tab']").First(t => t.GetAttribute("data-tm-tab") == "Data").Click();

        cut.Instance.ActiveTab.Should().Be("Home", "the component never writes the host parameter");
        cut.Find("[data-tm-tab='Data'][role='tabpanel']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_InvisibleFileTab_FallsBackToHome()
    {
        var cut = Render<TmSpreadsheetToolbar>(parameters => parameters
            .Add(p => p.ActiveTab, "File")
            .Add(p => p.ShowFileTab, false));

        cut.FindAll("[data-tm-tab='File']").Should().BeEmpty("there is no File tab to show the File panel");
        cut.Find("[data-tm-tab='Home'][role='tabpanel']").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_Menus_RenderAsTopLayerPanels_OutsideTheRibbonScrollBox()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        cut.FindAll(".tm-overlay-panel").Should().BeEmpty("closed menus render nothing");

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Border picker")
            .Click();

        var borderPanel = cut.Find(".tm-overlay-panel.tm-border-picker__dropdown");
        borderPanel.ClassList.Should().Contain("tm-border-picker__dropdown");
        cut.FindAll(".tm-spreadsheet-toolbar__tab-content .tm-border-picker__dropdown").Should().BeEmpty(
            "the ribbon row's overflow must never clip a menu");

        cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Delete options")
            .Click();

        cut.FindAll(".tm-overlay-panel.tm-spreadsheet-toolbar__split-menu").Count.Should().Be(1);
        cut.FindAll(".tm-spreadsheet-toolbar__tab-content .tm-spreadsheet-toolbar__split-menu").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_ColourMenus_RenderAsTopLayerPanels_AndCloseOnEscape()
    {
        var cut = Render<TmSpreadsheetToolbar>();

        var textTrigger = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Text color picker");
        textTrigger.Click();

        cut.FindAll(".tm-overlay-panel.tm-color-picker-inline__dropdown").Count.Should().Be(1);
        cut.FindAll(".tm-spreadsheet-toolbar__tab-content .tm-color-picker-inline__dropdown").Should().BeEmpty();

        textTrigger.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-overlay-panel.tm-color-picker-inline__dropdown").Should().BeEmpty();

        var fillTrigger = cut.FindAll(".tm-spreadsheet-toolbar__button")
            .First(b => b.GetAttribute("title") == "Fill color picker");
        fillTrigger.Click();
        cut.FindAll(".tm-overlay-panel.tm-color-picker-inline__dropdown").Count.Should().Be(1);
        fillTrigger.KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-overlay-panel.tm-color-picker-inline__dropdown").Should().BeEmpty();
    }

    [Fact]
    public void Toolbar_LargeButtons_LeaveRoomForTheSecondLabelLine()
    {
        // The label clamps to two lines, so the button must not fix a height that cuts the second line.
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Tempo.Blazor.Spreadsheet", "Components",
            "Spreadsheet", "TmSpreadsheetToolbar.razor.css"));
        var large = Regex.Match(css, @"\.tm-spreadsheet-toolbar__button--large\s*\{[^}]*\}", RegexOptions.None);
        large.Success.Should().BeTrue();
        large.Value.Should().Contain("height: auto");
        large.Value.Should().Contain("min-height: 56px");
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }
}
