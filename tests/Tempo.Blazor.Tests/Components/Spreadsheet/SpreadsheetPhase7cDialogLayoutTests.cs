using System.Linq;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Dialogs;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// Redesign phase 7c — the 320 px dialog pass and the Sort phone toolbar: the Name Manager card's
/// "Refers to" line WRAPS (two lines, then clamped) instead of being squeezed out, its name stays a
/// single ellipsised line, the scope chip moves under the name on the narrowest width, the Sort
/// toolbar turns into the reference's two columns on a phone and stacks below 320, Format Cells keeps
/// its phase-4 category select above the options, and the data-validation dialog keeps the Medium size
/// its 640 px overlay breakpoint was measured against.
/// </summary>
public sealed class SpreadsheetPhase7cDialogLayoutTests : LocalizationTestBase
{
    private const string LongRefersTo = "=Budget_2026_Consolidated!$B$2:$B$16";
    private const string LongName = "ConsolidatedRevenueForecast2026";

    private static string SpreadsheetCss()
        => ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Spreadsheet", "wwwroot", "css", "components", "_spreadsheet.css")));

    private static string SortScopedCss()
        => ThemeCss.StripComments(File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor.Spreadsheet", "Components", "Spreadsheet", "Dialogs",
            "TmSpreadsheetSortDialog.razor.css")));

    private static SpreadsheetWorkbook LongNameWorkbook()
    {
        var workbook = new SpreadsheetWorkbook();
        workbook.NamedRanges.Add(new SpreadsheetNamedRange
        {
            Name = LongName,
            RefersTo = LongRefersTo,
            Comment = "Rolled up budget"
        });
        return workbook;
    }

    // ── Name Manager at 320 px ───────────────────────────────────────────────

    [Fact]
    public void NameManager_Mobile_CardRefersTo_WrapsInsteadOfTruncating()
    {
        var cut = Render<TmSpreadsheetNameManagerDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Workbook, LongNameWorkbook()));

        var refers = cut.Find(".tm-spreadsheet-name-manager__card-refers");
        refers.TextContent.Trim().Should().Be(LongRefersTo);
        refers.GetAttribute("title").Should().Be(LongRefersTo,
            "a clamped two-line reference must still be readable in full from the title");

        refers.HasAttribute("aria-label").Should().BeFalse(
            "aria-label is prohibited on a generic div (ARIA in HTML) — the title carries the full value");

        var name = cut.Find(".tm-spreadsheet-name-manager__card-name");
        name.GetAttribute("title").Should().Be(LongName);

        var css = SpreadsheetCss();
        css.Should().MatchRegex(@"\.tm-spreadsheet-name-manager__card-refers\s*\{[^}]*overflow-wrap:\s*anywhere",
            "the reference formula breaks anywhere instead of overflowing the card");
        css.Should().MatchRegex(@"\.tm-spreadsheet-name-manager__card-refers\s*\{[^}]*-webkit-line-clamp:\s*2",
            "at most two lines, so one long formula cannot push the rest of the card away");
        css.Should().MatchRegex(@"\.tm-spreadsheet-name-manager__card-name\s*\{[^}]*text-overflow:\s*ellipsis",
            "the name stays ONE ellipsised line");
        css.Should().MatchRegex(@"\.tm-spreadsheet-name-manager__card-name\s*\{[^}]*white-space:\s*nowrap");
    }

    [Fact]
    public void NameManager_Mobile_ScopeChip_MovesUnderTheNameAt320()
    {
        var cut = Render<TmSpreadsheetNameManagerDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Workbook, LongNameWorkbook()));

        cut.Find(".tm-spreadsheet-name-manager__chip").Should().NotBeNull(
            "the scope still renders as the reference's chip");

        var css = SpreadsheetCss();
        css.Should().Contain(".tm-modal-overlay.tm-spreadsheet-name-manager",
            "the 22rem threshold is measured on the DIALOG's own width (320 = 20rem), not on the body padding");
        css.Should().MatchRegex(@"@container\s*\(width\s*<\s*22rem\)",
            "range-syntax container query — never @media (max-width)");
        css.Should().NotContain("@media (max-width");
        css.Should().MatchRegex(
                @"@container\s*\(width\s*<\s*22rem\)\s*\{[\s\S]*?\.tm-spreadsheet-name-manager__toolbar\s*\{[^}]*grid-template-columns:\s*repeat\(3,\s*minmax\(0,\s*1fr\)\)",
            "at 320 the New / Edit / Delete actions sit on ONE row of three equal tracks");
        css.Should().MatchRegex(
                @"@container\s*\(width\s*<\s*22rem\)\s*\{[\s\S]*?\.tm-spreadsheet-name-manager__filter\s*\{[^}]*grid-column:\s*1\s*/\s*-1",
            "the filter takes the full row under the three actions");
    }

    // ── Sort on a phone ─────────────────────────────────────────────────────

    private IRenderedComponent<TmSpreadsheetSortDialog> SortDialog(TmLayoutMode layout)
        => Render<TmSpreadsheetSortDialog>(p => p
            .Add(c => c.Range, new SpreadsheetRange(0, 0, 9, 2))
            .Add(c => c.LayoutMode, layout));

    [Fact]
    public void SortDialog_Mobile_OptionsColumn_SitsBesideAddLevel()
    {
        var cut = SortDialog(TmLayoutMode.Mobile);

        var toolbar = cut.Find(".tm-spreadsheet-sort__toolbar");
        toolbar.Children.Should().HaveCount(2,
            "the phone toolbar is TWO columns: the Add-level action and the options column");
        toolbar.Children[0].ClassList.Should().Contain("tm-spreadsheet-sort__tool");
        toolbar.Children[1].ClassList.Should().Contain("tm-spreadsheet-sort__toggles");

        var toggles = toolbar.QuerySelectorAll(".tm-spreadsheet-sort__header-toggle");
        toggles.Should().HaveCount(2, "both checkboxes keep the core TmCheckbox root");

        cut.Find(".tm-spreadsheet-sort__body").ClassList.Should().Contain("tm-spreadsheet-sort__body--mobile",
            "the 2-column toolbar is the phone presentation, not the desktop row");

        var css = SortScopedCss();
        css.Should().MatchRegex(
                @"\.tm-spreadsheet-sort__body--mobile\s+\.tm-spreadsheet-sort__toolbar\s*\{[^}]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s+minmax\(0,\s*1fr\)",
            "the mobile toolbar is a two-column grid");
        css.Should().MatchRegex(
                @"\.tm-spreadsheet-sort__body--mobile\s+::deep\s+\.tm-spreadsheet-sort__tool\s*\{[^}]*width:\s*100%",
            "TmButton renders the <button>, so the add-level class needs the deep combinator to fill its column");
        css.Should().NotContain(".tm-spreadsheet-sort__body--mobile .tm-spreadsheet-sort__tool {",
            "a scoped rule WITHOUT ::deep on a child-rendered class is dead CSS (the root carries the child's scope)");
        css.Should().MatchRegex(
                @"\.tm-spreadsheet-sort__body--mobile\s+\.tm-spreadsheet-sort__toggles\s*\{[^}]*flex-direction:\s*column",
            "the checkboxes are STACKED in the right column, per the reference");
        css.Should().MatchRegex(@"@container\s*\(width\s*<\s*20rem\)",
            "below 320 px the options stack under the button (readability over the reference)");
    }

    [Fact]
    public void SortDialog_Desktop_KeepsTheSingleRowToolbar()
    {
        var cut = SortDialog(TmLayoutMode.Desktop);

        cut.Find(".tm-spreadsheet-sort__body").ClassList.Should().NotContain("tm-spreadsheet-sort__body--mobile",
            "the desktop row keeps Add level left and both checkboxes beside it");
    }

    // ── Format Cells at 320 px (phase 4 regression) ─────────────────────────

    [Fact]
    public void FormatCells_Mobile_320_CategorySelectAboveOptions()
    {
        var cut = Render<TmSpreadsheetFormatCellsDialog>(p => p
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Style, new SpreadsheetCellStyle()));

        cut.Find(".tm-fcd__category-select").Should().NotBeNull();
        cut.FindAll(".tm-fcd__listbox[role='listbox']").Should().BeEmpty(
            "the reference's 140 px listbox column is unusable at 320, so the category is a select");

        var html = cut.Markup;
        var selectIndex = html.IndexOf("tm-fcd__category-select", StringComparison.Ordinal);
        var optionsIndex = html.IndexOf("tm-fcd__number-options", StringComparison.Ordinal);
        optionsIndex.Should().BeGreaterThan(selectIndex, "the select sits ABOVE the options");

        var css = SpreadsheetCss();
        css.Should().MatchRegex(@"\.tm-fcd__category-select\s*\{[^}]*width:\s*100%",
            "the select spans the phone's dialog width");
        css.Should().MatchRegex(@"\.tm-fcd__number\s*\{[^}]*flex-direction:\s*column",
            "the phone number tab is a column: category, then options");
        css.Should().MatchRegex(@"\.tm-fcd__tabs\s*\{[^}]*flex-wrap:\s*wrap",
            "five tabs cannot fit one 320 px line");
    }

    // ── Data Validation keeps the measured overlay breakpoint ───────────────

    [Fact]
    public void DataValidationDialog_KeepsMediumSize_ForOverlayBreakpoint()
    {
        var cut = Render<TmSpreadsheetDataValidationDialog>(p => p
            .Add(c => c.Validation, new SpreadsheetDataValidation()));

        cut.FindComponent<TmModal>().Instance.Size.Should().Be(ModalSize.Medium,
            "the 640 px label | field breakpoint is measured on the OVERLAY; a Medium card's body is " +
            "~470 px even at a 1440 viewport, so growing the dialog to Large/XLarge would silently " +
            "change which surface the threshold describes");

        var css = SpreadsheetCss();
        css.Should().MatchRegex(@"\.tm-modal-overlay\.tm-dvd\s*\{[^}]*container-type:\s*inline-size",
            "the overlay is the query container the 640 px threshold was measured on");
        css.Should().MatchRegex(@"@container\s*\(width\s*>=\s*640px\)",
            "the label | field grid appears from 640 px of overlay width");
    }
}
