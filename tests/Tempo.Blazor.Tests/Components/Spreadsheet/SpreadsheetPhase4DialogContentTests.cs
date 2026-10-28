using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 4 — the CONTENT and hierarchy of every spreadsheet dialog against the reference
/// mockups: visible labels on the find/replace fields, the counter, a two-column option block, the
/// primary "Replace all", a labelled wizard stepper, cards instead of a cramped table on a phone,
/// segmented link type / And-Or pickers, the selected-column count, icons instead of Unicode glyphs,
/// and a mobile category select above the format options.
/// </summary>
public sealed class SpreadsheetPhase4DialogContentTests : LocalizationTestBase
{
    private static readonly string[] Sample = ["Jan;Novak;Praha", "Petr;Svoboda;Brno"];

    private static string PackageCss(string relativePath)
        => ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Spreadsheet", "Components", "Spreadsheet", relativePath)));

    private static string GlobalSpreadsheetCss()
        => ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "css", "components", "_spreadsheet.css")));

    // ── Find / Replace ────────────────────────────────────────────────────────

    [Fact]
    public void FindReplace_Fields_HaveVisibleLabels()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        var labels = cut.FindAll(".tm-spreadsheet-find__label");
        labels.Should().HaveCount(2, "the reference labels both fields instead of relying on a placeholder");
        labels[0].TextContent.Trim().Should().Be("Find what");
        labels[1].TextContent.Trim().Should().Be("Replace with");

        // Every label is programmatically tied to its field.
        foreach (var label in labels)
        {
            var id = label.GetAttribute("for");
            id.Should().NotBeNullOrWhiteSpace($"{label.TextContent} must point at its field");
            cut.Find($"[id='{id}']").Should().NotBeNull();
        }
    }

    [Fact]
    public void FindReplace_ReplaceAll_IsPrimary()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        var buttons = cut.FindAll(".tm-spreadsheet-find__actions button");
        var replace = buttons.First(b => b.TextContent.Contains("Replace") && !b.TextContent.Contains("all"));
        var replaceAll = buttons.First(b => b.TextContent.Contains("Replace all"));

        replaceAll.ClassList.Should().Contain("tm-btn-primary", "Replace all is the reference's primary action");
        replace.ClassList.Should().NotContain("tm-btn-primary");
        buttons.Should().Contain(b => b.TextContent.Contains("Close"),
            "the desktop panel keeps an explicit Close action next to the header X");
    }

    [Fact]
    public void FindReplace_PrevNext_HaveTextLabels()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        var previous = cut.FindAll(".tm-spreadsheet-find__btn--nav")[0];
        var next = cut.FindAll(".tm-spreadsheet-find__btn--nav")[1];

        previous.TextContent.Should().Contain("Find previous");
        next.TextContent.Should().Contain("Find next");
        previous.QuerySelectorAll("svg").Length.Should().BeGreaterThan(0, "the action carries an icon");
        next.QuerySelectorAll("svg").Length.Should().BeGreaterThan(0, "the action carries an icon");
    }

    [Fact]
    public void FindReplace_Counter_SitsOnTheFieldRow_AndOptionsAreTwoColumns()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.MatchIndex, 1)
            .Add(c => c.MatchCount, 3));

        var counter = cut.Find(".tm-spreadsheet-find__counter");
        counter.TextContent.Should().Contain("1 of 3");
        counter.Closest(".tm-spreadsheet-find__field").Should().NotBeNull(
            "the counter belongs to the Find row, like the reference's '1 z 3'");

        var css = PackageCss(Path.Combine("Dialogs", "TmSpreadsheetFindReplaceDialog.razor.css"));
        Regex.IsMatch(css, @"\.tm-spreadsheet-find__options\s*\{[^}]*grid-template-columns:\s*[^;]*\S+\s+\S+")
            .Should().BeTrue("the four options sit in two columns on the desktop layout");
    }

    [Fact]
    public void FindReplace_Options_UseTmCheckbox()
    {
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        cut.FindComponents<TmCheckbox>().Should().HaveCount(4,
            "the four search options are core checkboxes, not raw inputs");
    }

    // ── Text to Columns ──────────────────────────────────────────────────────

    [Fact]
    public void TextToColumns_Stepper_HasLabels_AndMarksCompletedSteps()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p.Add(c => c.SourceRows, Sample));

        var stepper = cut.FindComponents<TmStepper>().Single();
        stepper.Instance.Steps!.Select(s => s.Label).Should().Equal("Data type", "Delimiters", "Column format");
        cut.FindAll(".tm-stepper-item").Should().HaveCount(3);

        var steps = cut.FindAll(".tm-stepper-step");
        steps[0].GetAttribute("aria-current").Should().Be("step", "step 1 is the active step");
        steps[0].ClassList.Should().Contain("tm-stepper-step--active");

        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click();

        steps = cut.FindAll(".tm-stepper-step");
        steps[0].ClassList.Should().Contain("tm-stepper-step--completed", "step 1 is done");
        steps[0].QuerySelectorAll("svg").Length.Should().BeGreaterThan(0, "a completed step paints its check");
        steps[1].GetAttribute("aria-current").Should().Be("step");
        steps.Select(s => s.TextContent).Should().Contain(t => t.Contains("Delimiters"));
    }

    [Fact]
    public void TextToColumns_Mobile_ShowsCompactStepLabel()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p
            .Add(c => c.SourceRows, Sample)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile));

        var compact = cut.Find(".tm-spreadsheet-t2c__step-compact");
        compact.TextContent.Should().Contain("Step 1 of 3");
        compact.TextContent.Should().Contain("Data type");

        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click();

        cut.Find(".tm-spreadsheet-t2c__step-compact").TextContent.Should()
            .Contain("Step 2 of 3").And.Contain("Delimiters");
    }

    [Fact]
    public void TextToColumns_Preview_HasScrollWrapperAndStickyHeader()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p.Add(c => c.SourceRows, Sample));
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click();

        cut.Find(".tm-spreadsheet-t2c__preview-scroll").Should().NotBeNull(
            "a wide preview scrolls inside its own wrapper instead of widening the dialog");

        var css = PackageCss(Path.Combine("Dialogs", "TmSpreadsheetTextToColumnsDialog.razor.css"));
        Regex.IsMatch(css, @"\.tm-spreadsheet-t2c__preview-scroll\s*\{[^}]*overflow-x:\s*auto")
            .Should().BeTrue();
        Regex.IsMatch(css, @"\.tm-spreadsheet-t2c__preview-table\s+thead\s+th\s*\{[^}]*position:\s*sticky")
            .Should().BeTrue("the preview header stays visible while the rows scroll");
    }

    [Fact]
    public void TextToColumns_Delimiters_RenderInOneRow()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p.Add(c => c.SourceRows, Sample));
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click();

        var row = cut.Find(".tm-spreadsheet-t2c__delims");
        row.Children.Should().HaveCount(5, "four delimiters plus the 'Other' entry sit in one row");

        var css = PackageCss(Path.Combine("Dialogs", "TmSpreadsheetTextToColumnsDialog.razor.css"));
        Regex.IsMatch(css, @"\.tm-spreadsheet-t2c__delims\s*\{[^}]*display:\s*flex")
            .Should().BeTrue();
    }

    // ── Name Manager ────────────────────────────────────────────────────────

    private IRenderedComponent<TmSpreadsheetNameManagerDialog> NameManager(TmLayoutMode layout = TmLayoutMode.Desktop)
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange { Name = "Sales", RefersTo = "=Sheet1!A1:A10", Comment = "Revenue" });
        workbook.NamedRanges.Add(new SpreadsheetNamedRange
        {
            Name = "TaxRate",
            RefersTo = "=Sheet1!B1",
            Scope = NamedRangeScope.Sheet,
            SheetIndex = 0
        });
        return Render<TmSpreadsheetNameManagerDialog>(p => p
            .Add(c => c.Workbook, workbook)
            .Add(c => c.LayoutMode, layout));
    }

    [Fact]
    public void NameManager_New_IsPrimary_AndFilterUsesTmSearchInput()
    {
        var cut = NameManager();

        var newButton = cut.FindAll(".tm-spreadsheet-name-manager__toolbar button").First();
        newButton.TextContent.Should().Contain("New");
        newButton.ClassList.Should().Contain("tm-btn-primary", "New is the reference's primary toolbar action");

        var search = cut.FindComponents<TmSearchInput>().Should().ContainSingle("the filter is the core search input").Subject;
        search.Find("input").GetAttribute("type").Should().Be("search");
        search.Find("input").GetAttribute("placeholder").Should().Be("Filter names...");
    }

    [Fact]
    public void NameManager_Mobile_RendersCards()
    {
        var cut = NameManager(TmLayoutMode.Mobile);

        cut.FindAll("table").Should().BeEmpty("a five-column table is unreadable on a phone");
        var cards = cut.FindAll(".tm-spreadsheet-name-manager__card");
        cards.Should().HaveCount(2);

        cards[0].TextContent.Should().Contain("Sales");
        cards[0].TextContent.Should().Contain("=Sheet1!A1:A10");
        cards[0].QuerySelector(".tm-spreadsheet-name-manager__chip").Should().NotBeNull(
            "the scope shows as a chip under the name");
    }

    // ── Sort ────────────────────────────────────────────────────────────────

    private IRenderedComponent<TmSpreadsheetSortDialog> SortDialog(TmLayoutMode layout = TmLayoutMode.Desktop)
        => Render<TmSpreadsheetSortDialog>(p => p
            .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2))
            .Add(c => c.LayoutMode, layout));

    [Fact]
    public void SortDialog_Mobile_RendersLevelsAsCards()
    {
        var cut = SortDialog(TmLayoutMode.Mobile);

        cut.FindAll(".tm-spreadsheet-sort__table").Should().BeEmpty("the phone layout uses cards, not a table");
        cut.FindAll(".tm-spreadsheet-sort__card").Should().ContainSingle();

        var card = cut.Find(".tm-spreadsheet-sort__card");
        card.TextContent.Should().Contain("Sort by", "the first card is titled Sort by");
        card.QuerySelectorAll(".tm-spreadsheet-sort__field-label").Length.Should().Be(3,
            "every field keeps its label above it on the phone");

        cut.Find(".tm-spreadsheet-sort__tool").ClassList.Should().Contain("tm-btn-outline",
            "Add level is an outlined tonal action, not the filled primary (phase-4 review)");

        cut.Find(".tm-spreadsheet-sort__tool").Click();
        cut.FindAll(".tm-spreadsheet-sort__card").Should().HaveCount(2);

        var remove = cut.Find(".tm-spreadsheet-sort__remove");
        remove.TextContent.Should().Contain("Delete level", "the card spells the destructive action out");
        remove.QuerySelectorAll("svg").Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SortDialog_RemoveLevel_UsesTheXIcon()
    {
        var cut = SortDialog();
        cut.Find(".tm-spreadsheet-sort__tool").Click();

        var removes = cut.FindAll(".tm-spreadsheet-sort__remove");
        removes.Should().HaveCount(2);
        removes[1].QuerySelectorAll("svg").Length.Should().BeGreaterThan(0, "the row removal is an icon, not the ✕ glyph");
        cut.Markup.Should().NotContain("✕");
    }

    // ── Special paste ───────────────────────────────────────────────────────

    [Fact]
    public void SpecialPaste_Operation_RendersAsRadioGroup()
    {
        var cut = Render<TmSpreadsheetSpecialPasteDialog>();

        cut.FindAll("input[name='ps-operation']").Should().HaveCount(5,
            "the operation is a radio choice next to the paste content — no select");
        cut.FindAll(".tm-spreadsheet-pastespecial__op").Should().BeEmpty();

        var grid = cut.Find(".tm-spreadsheet-pastespecial__grid");
        grid.Children.Should().HaveCount(2, "Paste and Operation sit in two columns");
    }

    // ── Hyperlink ───────────────────────────────────────────────────────────

    [Fact]
    public void Hyperlink_LinkType_RendersSegmented_AndInputModeMatchesType()
    {
        var workbook = new SpreadsheetWorkbook();
        var cut = Render<TmSpreadsheetHyperlinkDialog>(p => p.Add(c => c.Workbook, workbook));

        var group = cut.Find("#hl-type");
        group.ClassList.Should().Contain("tm-radio-group");
        cut.FindAll("#hl-type .tm-radio-segment").Should().HaveCount(4, "the link type is a segmented control");
        cut.FindAll("#hl-type option").Should().BeEmpty();

        cut.Find("#hl-target").GetAttribute("inputmode").Should().Be("url");

        cut.FindAll("#hl-type .tm-radio-segment")[1].QuerySelector("input")!.Change(true);
        cut.Find("#hl-target").GetAttribute("inputmode").Should().Be("email");
    }

    // ── Custom filter ───────────────────────────────────────────────────────

    [Fact]
    public void CustomFilter_AndOr_RendersSegmented()
    {
        var cut = Render<TmSpreadsheetCustomFilterDialog>(p => p
            .Add(c => c.ColumnIndex, 1)
            .Add(c => c.Kind, SpreadsheetFilterKind.Number));

        cut.FindAll("input[name='join']").Should().BeEmpty("the hand-rolled radios are gone");
        cut.FindAll(".tm-spreadsheet-custom-filter__join .tm-radio-segment").Should().HaveCount(2);
        cut.FindAll(".tm-spreadsheet-custom-filter__join .tm-radio-segment")[0].TextContent.Trim().Should().Be("And");

        // Numeric columns ask for the decimal keyboard on a phone.
        cut.Find(".tm-spreadsheet-custom-filter__value").GetAttribute("inputmode").Should().Be("decimal");
    }

    // ── Remove duplicates ───────────────────────────────────────────────────

    [Fact]
    public void RemoveDuplicates_ShowsSelectedColumnCount()
    {
        var cut = Render<TmSpreadsheetRemoveDuplicatesDialog>(p => p
            .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2)));

        var count = cut.Find(".tm-spreadsheet-dedup__count");
        count.TextContent.Trim().Should().Be("Selected 3 of 3");

        cut.FindAll(".tm-spreadsheet-dedup__column input[type=checkbox]")[0].Change(false);
        cut.Find(".tm-spreadsheet-dedup__count").TextContent.Trim().Should().Be("Selected 2 of 3");

        cut.FindComponents<TmCheckbox>().Should().HaveCountGreaterThanOrEqualTo(4,
            "the column list and the two toggles are core checkboxes");
    }

    // ── Filter dropdown ─────────────────────────────────────────────────────

    [Fact]
    public void FilterDropdown_UsesTmIcons_AndHasTitle()
    {
        var sheet = new SpreadsheetSheet();
        sheet.Cells["A1"] = new SpreadsheetCell { Value = "Fruit", DisplayValue = "Fruit", DataType = SpreadsheetDataType.Text };
        sheet.Cells["A2"] = new SpreadsheetCell { Value = "Apple", DisplayValue = "Apple", DataType = SpreadsheetDataType.Text };
        var filter = new SpreadsheetAutoFilter(new SpreadsheetRange(0, 0, 2, 0));

        var cut = Render<TmSpreadsheetFilterDropdown>(p => p
            .Add(c => c.Sheet, sheet)
            .Add(c => c.Filter, filter)
            .Add(c => c.ColumnIndex, 0)
            .Add(c => c.Culture, CultureInfo.InvariantCulture));

        cut.Find(".tm-spreadsheet-filter-dropdown__title").TextContent.Trim().Should().Be("Filter and sort");

        var markup = cut.Markup;
        foreach (var glyph in new[] { "▲", "▼", "⚙", "✕", "›" })
        {
            markup.Should().NotContain(glyph, $"'{glyph}' is a glyph, not the shared icon set");
        }

        cut.FindAll(".tm-spreadsheet-filter-dropdown__item svg").Count.Should().BeGreaterThanOrEqualTo(4);
        cut.FindComponents<TmCheckbox>().Should().HaveCountGreaterThanOrEqualTo(3,
            "the value list and select-all use core checkboxes");
        cut.Find(".tm-spreadsheet-filter-dropdown__actions .tm-btn").Should().NotBeNull();
    }

    // ── Format Cells ────────────────────────────────────────────────────────

    private IRenderedComponent<TmSpreadsheetFormatCellsDialog> FormatCells(TmLayoutMode layout = TmLayoutMode.Desktop)
        => Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.Style, new SpreadsheetCellStyle())
            .Add(c => c.LayoutMode, layout));

    [Fact]
    public void FormatCells_Mobile_CategorySelect_AboveOptions()
    {
        var cut = FormatCells(TmLayoutMode.Mobile);

        var select = cut.Find(".tm-fcd__category-select");
        select.Should().NotBeNull("a 390px panel cannot show the category list beside the options");
        cut.FindAll(".tm-fcd__listbox[role='listbox']").Should().BeEmpty();

        var selectIndex = cut.Markup.IndexOf("tm-fcd__category-select", StringComparison.Ordinal);
        var optionsIndex = cut.Markup.IndexOf("tm-fcd__number-options", StringComparison.Ordinal);
        selectIndex.Should().BeGreaterThanOrEqualTo(0);
        optionsIndex.Should().BeGreaterThan(selectIndex, "the category select sits ABOVE the options");
    }

    [Fact]
    public void FormatCells_DecimalPlaces_UsesTmNumberInputStepper()
    {
        var cut = FormatCells();

        cut.FindAll(".tm-fcd__listbox-item").First(e => e.TextContent.Contains("NumCat_Number")).Click();

        var number = cut.FindComponents<TmNumberInput>().Single();
        number.Find(".tm-number-input__decrement").Should().NotBeNull("the decimal places use the core −/+ stepper");
        number.Find(".tm-number-input__increment").Should().NotBeNull();

        var before = number.Find("input").GetAttribute("value");
        number.Find(".tm-number-input__increment").Click();
        number.Find("input").GetAttribute("value").Should().NotBe(before);
    }

    [Fact]
    public void FormatCells_Preview_AlignsNumericFormatsRight()
    {
        var cut = FormatCells();

        cut.Find(".tm-fcd__preview").GetAttribute("style").Should().Contain("text-align: left");

        cut.FindAll(".tm-fcd__listbox-item").First(e => e.TextContent.Contains("NumCat_Number")).Click();
        cut.Find(".tm-fcd__preview").GetAttribute("style").Should().Contain("text-align: right",
            "a numeric format right-aligns its preview, like the cell will");
    }

    [Fact]
    public void FormatCells_ActiveTab_IsUnderlinedInPrimary()
    {
        var css = GlobalSpreadsheetCss();
        var match = Regex.Match(css, @"\.tm-fcd__tab--active\s*\{([^}]*)\}");
        match.Success.Should().BeTrue(".tm-fcd__tab--active must style the active tab");
        match.Groups[1].Value.Should().Contain("border-bottom");
        match.Groups[1].Value.Should().Contain("--tm-color-primary");
    }

    // ── Data validation ─────────────────────────────────────────────────────

    [Fact]
    public void DataValidation_Form_UsesLabelFieldGrid_AndTmInputs()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop));

        cut.Find(".tm-dvd__form").Should().NotBeNull();
        cut.FindAll(".tm-select").Should().NotBeEmpty("Allow/Data use the core select");
        cut.FindComponents<TmCheckbox>().Should().NotBeEmpty("the toggles use the core checkbox");

        // A numeric rule grows the value fields: they are the core text inputs.
        cut.Find(".tm-dvd__select select").Change("Whole");
        cut.FindComponents<TmTextInput>().Should().NotBeEmpty("the value fields use the core text input");

        var css = GlobalSpreadsheetCss();
        Regex.IsMatch(css, @"\.tm-modal-overlay\.tm-dvd\s*\{[^}]*container-type:\s*inline-size")
            .Should().BeTrue("the dialog measures its own surface, not a hardcoded viewport breakpoint");
        Regex.IsMatch(css, @"@container\s*\(width\s*>=\s*640px\)\s*\{[^}]*\.tm-dvd__field\s*\{[^}]*grid-template-columns")
            .Should().BeTrue("label | field appears once the dialog body is at least 640px wide");
    }

    // ── Validation alert card ───────────────────────────────────────────────

    [Fact]
    public void ValidationAlert_SeverityBadge_SitsLeftOfTheMessage()
    {
        var cut = Render<TmSpreadsheetValidationAlert>(p => p
            .Add(c => c.Severity, SpreadsheetValidationErrorStyle.Stop)
            .Add(c => c.Title, "Invalid value")
            .Add(c => c.Message, "Enter a number between 0 and 100."));

        var body = cut.Find(".tm-modal-overlay.tm-spreadsheet-alert .tm-modal-body");
        body.FirstElementChild!.ClassList.Should().Contain("tm-spreadsheet-alert__badge",
            "the large severity circle is the first thing in the card body");
        cut.Find(".tm-spreadsheet-alert__badge svg").Should().NotBeNull("the circle carries the severity icon");

        // The alert is a wider card than the old Small modal and drops the header divider.
        cut.Find(".tm-modal-overlay .tm-focus-scope").ClassList.Should().Contain("tm-modal--md",
            "the reference alert is a wider card than the old Small modal");
        var css = GlobalSpreadsheetCss();
        Regex.IsMatch(css, @"\.tm-modal-overlay\.tm-spreadsheet-alert\s+\.tm-modal-header\s*\{[^}]*border-bottom:\s*none")
            .Should().BeTrue("the reference alert has no divider under the title");
        Regex.IsMatch(css, @"\.tm-modal-overlay\.tm-spreadsheet-alert\s+\.tm-modal-body\s*\{[^}]*display:\s*flex")
            .Should().BeTrue("badge and message share one row");
    }

    // ── Phase-4 review regressions ───────────────────────────────────────────

    [Fact]
    public void FormatCells_MobileCategory_ApplyWritesTheNewNumberFormat()
    {
        SpreadsheetCellStyle? applied = null;
        var cut = Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.Style, new SpreadsheetCellStyle())
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.OnApply, EventCallback.Factory.Create<SpreadsheetCellStyle>(this, s => applied = s)));

        // The phone picks the category through the select (the desktop listbox is not rendered).
        cut.Find(".tm-fcd__category-select select").Change("Percentage");
        cut.Find(".tm-fcd__actions button:last-of-type").Click();

        applied.Should().NotBeNull();
        applied!.NumberFormat.Should().Be("0.00%",
            "choosing Percentage must refresh the working format, not keep the previous category's");
    }

    [Fact]
    public void FormatCells_HasNoUnicodeGlyphIcons()
    {
        var cut = Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.Style, new SpreadsheetCellStyle()));

        cut.FindAll(".tm-fcd__tab").First(e => e.TextContent.Contains("Border")).Click();

        var markup = cut.Markup;
        var offending = markup
            .Where(ch => ch is >= '\u2190' and <= '\u27BF'
                      || ch is >= '\u22A0' and <= '\u22FF'
                      || ch is >= '\u25A0' and <= '\u25FF')
            .Distinct()
            .Select(ch => $"U+{(int)ch:X4} '{ch}'")
            .ToList();
        offending.Should().BeEmpty("dialog icons are SVG, not Unicode glyphs: " + string.Join(", ", offending));
        cut.FindAll(".tm-fcd__btn svg").Count.Should().BeGreaterThanOrEqualTo(8,
            "the border presets/edges paint the registered package icons");
    }

    [Fact]
    public void FindReplace_SameQuery_DoesNotRaiseASecondSearch()
    {
        var searches = 0;
        var cut = Render<TmSpreadsheetFindReplaceDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.OnSearchRequested, EventCallback.Factory.Create<SpreadsheetSearchOptions>(this, _ => searches++)));

        // TmTextInput raises input AND change for one edit; the change event of an unchanged value must
        // not re-raise the search (it used to reset the match index to the first hit).
        cut.Find(".tm-spreadsheet-find__query").Input("Alpha");
        searches.Should().Be(1);

        cut.Find(".tm-spreadsheet-find__query").Input("Alpha");
        searches.Should().Be(1, "a repeated value is an echo of the same edit");
    }

    [Fact]
    public void TextToColumns_OverwriteWarning_IsACoreWarningAlert()
    {
        var cut = Render<TmSpreadsheetTextToColumnsDialog>(p => p.Add(c => c.SourceRows, Sample));
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // step 2
        cut.Find(".tm-spreadsheet-t2c__btn--ok").Click(); // step 3

        var alert = cut.FindComponents<TmAlert>().Single(a => a.Instance.Severity == AlertSeverity.Warning);
        alert.Find(".tm-alert--warning").Should().NotBeNull("the overwrite warning uses the shared warning palette");
        cut.Find(".tm-spreadsheet-t2c__warning").TextContent.Trim().Should().NotBeNullOrWhiteSpace(
            "the warning keeps its localized text inside the shared alert");
    }

    [Fact]
    public void PasteSpecialAndRemoveDuplicates_FocusARealControl_NotAWrapper()
    {
        var paste = Render<TmSpreadsheetSpecialPasteDialog>();
        paste.Find(".tm-focus-scope").GetAttribute("data-initial-focus-selector")
            .Should().Be("input[name='ps-content']:checked",
                "the checked paste radio is the first real focusable control; the group div is not focusable");

        var dedup = Render<TmSpreadsheetRemoveDuplicatesDialog>(p => p
            .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2)));
        dedup.Find(".tm-focus-scope").GetAttribute("data-initial-focus-selector")
            .Should().Be(".tm-spreadsheet-dedup__column input",
                "the first column checkbox, not the TmCheckbox wrapper");
    }

    [Fact]
    public void Css_NarrowLayoutRules_UseTheContainerAndComeAfterTheirBase()
    {
        var css = GlobalSpreadsheetCss();

        // The Name Manager's full-width filter override must follow the base rule, or source order
        // silently keeps the 20rem max-width.
        var baseIndex = css.IndexOf(".tm-spreadsheet-name-manager__filter {", StringComparison.Ordinal);
        var queryIndex = css.IndexOf("@container (width < 640px)", StringComparison.Ordinal);
        baseIndex.Should().BeGreaterThanOrEqualTo(0);
        queryIndex.Should().BeGreaterThan(baseIndex, "the narrow override comes after the base declaration");

        // Hyperlink: an equal-width 2x2 segment grid, written for the GLOBAL sheet (no ::deep —
        // a ::deep selector in a global stylesheet is invalid and the browser drops the rule).
        Regex.IsMatch(css, @"\.tm-spreadsheet-hyperlink__kinds \.tm-radio-group-options\s*\{[^}]*grid-template-columns:\s*repeat\(2")
            .Should().BeTrue();
        css.Should().NotContain("::deep", "a global stylesheet cannot use the scoped ::deep combinator");
        Regex.IsMatch(css, @"\.tm-spreadsheet-named-range-edit__field label,\s*\.tm-spreadsheet-hyperlink__field label\s*\{")
            .Should().BeTrue("the Named Range Edit labels share the muted label style");

        // T2C: the column-format selects wrap into an auto-fit grid.
        var t2c = PackageCss(Path.Combine("Dialogs", "TmSpreadsheetTextToColumnsDialog.razor.css"));
        Regex.IsMatch(t2c, @"\.tm-spreadsheet-t2c__formats\s*\{[^}]*grid-template-columns:\s*repeat\(auto-fit,\s*minmax\(10rem")
            .Should().BeTrue();
    }
}
