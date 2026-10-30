using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Spreadsheet.Models;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Ribbon-style toolbar for the spreadsheet: one tab strip (Home, Insert, Data, View and the legacy
/// File tab) followed by a row of named groups whose commands are large (icon above the label) or
/// small (icon and label on one line). Every icon is an <c>TmIcon</c>, never a text glyph.
/// </summary>
public partial class TmSpreadsheetToolbar
{
    static TmSpreadsheetToolbar() => SpreadsheetIcons.EnsureRegistered();

    private static readonly List<SelectOption<string>> _fontOptions =
    [
        new("Arial", "Arial"),
        new("Calibri", "Calibri"),
        new("Segoe UI", "Segoe UI"),
        new("Times New Roman", "Times New Roman"),
        new("Courier New", "Courier New"),
    ];

    private static readonly List<SelectOption<string>> _fontSizeOptions =
    [
        new("8", "8"),
        new("9", "9"),
        new("10", "10"),
        new("11", "11"),
        new("12", "12"),
        new("14", "14"),
        new("16", "16"),
        new("18", "18"),
        new("20", "20"),
        new("24", "24"),
        new("28", "28"),
        new("32", "32"),
        new("36", "36"),
    ];

    private static readonly List<SelectOption<string>> _numberFormatOptions =
    [
        new("General", "General"),
        new("0", "Number"),
        new("0.00", "Number (2 dec)"),
        new("#,##0", "Number (sep)"),
        new("#,##0.00", "Number (sep, 2 dec)"),
        new("$#,##0.00", "Currency"),
        new("0%", "Percentage"),
        new("0.00%", "Percentage (2 dec)"),
        new("yyyy-MM-dd", "Date"),
        new("HH:mm", "Time"),
        new("@", "Text"),
    ];

    private static readonly string[] _allTabs = ["Home", "Insert", "Data", "View", "File"];

    /// <summary>
    /// Currently active toolbar tab. Two-way bindable through <see cref="ActiveTabChanged"/>; without a
    /// binding the toolbar switches the tab internally (the legacy behaviour).
    /// </summary>
    [Parameter] public string ActiveTab { get; set; } = "Home";

    /// <summary>Raised with the selected tab id when the user changes the ribbon tab.</summary>
    [Parameter] public EventCallback<string> ActiveTabChanged { get; set; }

    /// <summary>
    /// Whether the legacy File tab is offered. Hosts that render the document header (which owns the
    /// Open / Download actions) pass <c>false</c>.
    /// </summary>
    [Parameter] public bool ShowFileTab { get; set; } = true;

    /// <summary>
    /// The layout the ribbon renders in. <see cref="TmLayoutMode.Auto"/> resolves from the app-level
    /// VIEWPORT scope (the standalone toolbar case); the hosting <see cref="TmSpreadsheet"/> always passes
    /// its own resolved container layout, so an embedded spreadsheet follows its container, never the page.
    /// Desktop keeps the phase-5 tab strip; tablet renders two compact rows and mobile a single one
    /// (see <see cref="SpreadsheetToolbarLayout"/>).
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The viewport's layout — only read when <see cref="LayoutMode"/> is <see cref="TmLayoutMode.Auto"/>.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] public TmLayoutContext? Viewport { get; set; }

    /// <summary>Whether undo is available.</summary>
    [Parameter] public bool CanUndo { get; set; }

    /// <summary>Whether redo is available.</summary>
    [Parameter] public bool CanRedo { get; set; }

    /// <summary>Currently selected font family.</summary>
    [Parameter] public string? SelectedFontFamily { get; set; } = "Arial";

    /// <summary>Currently selected font size.</summary>
    [Parameter] public string? SelectedFontSize { get; set; } = "11";

    /// <summary>Whether the current selection is bold.</summary>
    [Parameter] public bool IsBold { get; set; }

    /// <summary>Whether the current selection is italic.</summary>
    [Parameter] public bool IsItalic { get; set; }

    /// <summary>Whether the current selection is underlined.</summary>
    [Parameter] public bool IsUnderline { get; set; }

    /// <summary>Whether the current selection has strikethrough.</summary>
    [Parameter] public bool IsStrikeThrough { get; set; }

    /// <summary>Whether Format Painter is currently active.</summary>
    [Parameter] public bool IsFormatPainterActive { get; set; }

    /// <summary>Current text color (CSS color value).</summary>
    [Parameter] public string? TextColor { get; set; }

    /// <summary>Current background color (CSS color value).</summary>
    [Parameter] public string? BackgroundColor { get; set; }

    /// <summary>Current horizontal alignment.</summary>
    [Parameter] public string? SelectedHorizontalAlign { get; set; } = "left";

    /// <summary>
    /// Current vertical alignment of the cell content: <c>top</c>, <c>middle</c> or <c>bottom</c>.
    /// Null means the default (bottom) is not marked as active.
    /// </summary>
    [Parameter] public string? SelectedVerticalAlign { get; set; }

    /// <summary>Currently selected number format.</summary>
    [Parameter] public string? SelectedNumberFormat { get; set; } = "General";

    /// <summary>Whether the current selection uses a percentage format.</summary>
    [Parameter] public bool IsPercentageFormat { get; set; }

    /// <summary>Whether the current selection uses a thousands-separator format.</summary>
    [Parameter] public bool IsThousandsFormat { get; set; }

    /// <summary>Whether the current selection covers a merged cell range.</summary>
    [Parameter] public bool IsMergeCellsActive { get; set; }

    /// <summary>Whether grid lines are currently visible.</summary>
    [Parameter] public bool ShowGridLines { get; set; } = true;

    /// <summary>Whether the auto-filter is currently enabled on the active sheet.</summary>
    [Parameter] public bool IsFilterActive { get; set; }

    /// <summary>Called when the Filter toggle button is clicked.</summary>
    [Parameter] public EventCallback OnToggleFilter { get; set; }

    /// <summary>Called when the Sort Ascending button is clicked.</summary>
    [Parameter] public EventCallback OnSortAscending { get; set; }

    /// <summary>Called when the Sort Descending button is clicked.</summary>
    [Parameter] public EventCallback OnSortDescending { get; set; }

    /// <summary>Called when the Custom Sort button is clicked.</summary>
    [Parameter] public EventCallback OnCustomSort { get; set; }

    /// <summary>Called when the Remove Duplicates button is clicked.</summary>
    [Parameter] public EventCallback OnRemoveDuplicates { get; set; }

    /// <summary>Called when the Text to Columns button is clicked.</summary>
    [Parameter] public EventCallback OnTextToColumns { get; set; }

    /// <summary>Called when the Paste Special button is clicked.</summary>
    [Parameter] public EventCallback OnPasteSpecial { get; set; }

    /// <summary>Called when the Data Validation dialog button is clicked.</summary>
    [Parameter] public EventCallback OnDataValidation { get; set; }

    /// <summary>Called when the Circle Invalid Data button is clicked.</summary>
    [Parameter] public EventCallback OnCircleInvalidData { get; set; }

    /// <summary>Called when the Clear Validation Circles button is clicked.</summary>
    [Parameter] public EventCallback OnClearValidationCircles { get; set; }

    /// <summary>Called when the Name Manager button is clicked.</summary>
    [Parameter] public EventCallback OnNameManager { get; set; }

    /// <summary>Custom tools to inject into the toolbar.</summary>
    [Parameter] public List<SpreadsheetCustomTool>? CustomTools { get; set; }

    /// <summary>Called when the Format Painter button is single-clicked.</summary>
    [Parameter] public EventCallback OnFormatPainterClick { get; set; }

    /// <summary>Called when the Format Painter button is double-clicked (sticky mode).</summary>
    [Parameter] public EventCallback OnFormatPainterDoubleClick { get; set; }

    /// <summary>Called when the Undo button is clicked.</summary>
    [Parameter] public EventCallback OnUndo { get; set; }

    /// <summary>Called when the Redo button is clicked.</summary>
    [Parameter] public EventCallback OnRedo { get; set; }

    /// <summary>Called when the Copy button is clicked.</summary>
    [Parameter] public EventCallback OnCopy { get; set; }

    /// <summary>Called when the Cut button is clicked.</summary>
    [Parameter] public EventCallback OnCut { get; set; }

    /// <summary>Called when the Paste button is clicked.</summary>
    [Parameter] public EventCallback OnPaste { get; set; }

    /// <summary>Called when the Insert Row button is clicked.</summary>
    [Parameter] public EventCallback OnInsertRow { get; set; }

    /// <summary>Called when the Delete Row button is clicked.</summary>
    [Parameter] public EventCallback OnDeleteRow { get; set; }

    /// <summary>Called when the Insert Column button is clicked.</summary>
    [Parameter] public EventCallback OnInsertColumn { get; set; }

    /// <summary>Called when the Delete Column button is clicked.</summary>
    [Parameter] public EventCallback OnDeleteColumn { get; set; }

    /// <summary>Called when the font family changes.</summary>
    [Parameter] public EventCallback<string?> OnFontFamilyChanged { get; set; }

    /// <summary>Called when the font size changes.</summary>
    [Parameter] public EventCallback<string?> OnFontSizeChanged { get; set; }

    /// <summary>Called when the Bold toggle is clicked.</summary>
    [Parameter] public EventCallback OnBoldToggle { get; set; }

    /// <summary>Called when the Italic toggle is clicked.</summary>
    [Parameter] public EventCallback OnItalicToggle { get; set; }

    /// <summary>Called when the Underline toggle is clicked.</summary>
    [Parameter] public EventCallback OnUnderlineToggle { get; set; }

    /// <summary>Called when the StrikeThrough toggle is clicked.</summary>
    [Parameter] public EventCallback OnStrikeThroughToggle { get; set; }

    /// <summary>Called when the Increase Indent button is clicked.</summary>
    [Parameter] public EventCallback OnIndentIncrease { get; set; }

    /// <summary>Called when the Decrease Indent button is clicked.</summary>
    [Parameter] public EventCallback OnIndentDecrease { get; set; }

    /// <summary>Called when the Format Cells button/dialog is requested.</summary>
    [Parameter] public EventCallback OnFormatCells { get; set; }

    /// <summary>Called when the text color is selected from the color picker.</summary>
    [Parameter] public EventCallback<string?> OnTextColorChanged { get; set; }

    /// <summary>Called when the background color is selected from the color picker.</summary>
    [Parameter] public EventCallback<string?> OnBackgroundColorChanged { get; set; }

    /// <summary>Called when the horizontal alignment changes.</summary>
    [Parameter] public EventCallback<string?> OnAlignChanged { get; set; }

    /// <summary>
    /// Called when the vertical alignment changes. The value is <c>top</c>, <c>middle</c> or
    /// <c>bottom</c>, matching the spreadsheet's <c>SpreadsheetVerticalAlign</c> names.
    /// </summary>
    [Parameter] public EventCallback<string?> OnVerticalAlignChanged { get; set; }

    /// <summary>Called when the number format changes.</summary>
    [Parameter] public EventCallback<string?> OnNumberFormatChanged { get; set; }

    /// <summary>Called when the Increase Decimals button is clicked.</summary>
    [Parameter] public EventCallback OnIncreaseDecimals { get; set; }

    /// <summary>Called when the Decrease Decimals button is clicked.</summary>
    [Parameter] public EventCallback OnDecreaseDecimals { get; set; }

    /// <summary>Called when the Percentage (%) button is clicked.</summary>
    [Parameter] public EventCallback OnPercentageFormat { get; set; }

    /// <summary>Called when the Thousands separator (,) button is clicked.</summary>
    [Parameter] public EventCallback OnThousandsFormat { get; set; }

    /// <summary>Called when the Open button is clicked.</summary>
    [Parameter] public EventCallback OnOpen { get; set; }

    /// <summary>Called when the Download button is clicked.</summary>
    [Parameter] public EventCallback OnDownload { get; set; }

    /// <summary>Called when the Insert Link button is clicked.</summary>
    [Parameter] public EventCallback OnInsertLink { get; set; }

    /// <summary>Called when the Insert Image button is clicked.</summary>
    [Parameter] public EventCallback OnInsertImage { get; set; }

    /// <summary>Called when the Merge Cells button is clicked.</summary>
    [Parameter] public EventCallback OnMergeCells { get; set; }

    /// <summary>Called when the Toggle Grid Lines button is clicked.</summary>
    [Parameter] public EventCallback OnToggleGridLines { get; set; }

    /// <summary>Called when a custom tool is clicked.</summary>
    [Parameter] public EventCallback<SpreadsheetCustomTool> OnCustomToolClick { get; set; }

    /// <summary>Called when a border preset is selected from the border picker dropdown.</summary>
    [Parameter] public EventCallback<BorderPreset> OnBorderPresetChanged { get; set; }

    /// <summary>Called when the "More Borders..." option is chosen (opens Format Cells on Border tab).</summary>
    [Parameter] public EventCallback OnOpenBorderDialog { get; set; }

    // Dropdown states
    private bool _isBorderDropdownOpen;
    private bool _isDeleteMenuOpen;
    private bool _isTextColorOpen;
    private bool _isBackgroundColorOpen;
    private BorderPreset _lastBorderPreset = BorderPreset.AllBorders;

    // Menu anchors (the core TmOverlayPanel places the popup against them, in the top layer).
    private ElementReference _textColorTriggerRef;
    private ElementReference _backgroundColorTriggerRef;
    private ElementReference _borderTriggerRef;
    private ElementReference _deleteTriggerRef;

    // The active tab is internal state: the ActiveTab parameter is the host's value, never assigned by
    // the component (that would fight a two-way binding). It is re-synced from the parameter only when
    // the host actually changes it.
    private string _activeTab = "Home";
    private string? _syncedActiveTabParameter;

    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    private IReadOnlyList<string> VisibleTabs => ShowFileTab ? _allTabs : _allTabs[..^1];

    private bool HasTextColor => !string.IsNullOrEmpty(TextColor);
    private bool HasBackgroundColor => !string.IsNullOrEmpty(BackgroundColor);

    private string TabId(string tab) => $"tm-spreadsheet-tab-{tab.ToLowerInvariant()}-{_idSuffix}";

    private string PanelId(string tab) => $"tm-spreadsheet-panel-{tab.ToLowerInvariant()}-{_idSuffix}";

    protected override void OnParametersSet()
    {
        if (!string.Equals(_syncedActiveTabParameter, ActiveTab, StringComparison.Ordinal))
        {
            _syncedActiveTabParameter = ActiveTab;
            _activeTab = string.IsNullOrEmpty(ActiveTab) ? "Home" : ActiveTab;
        }

        // A tab that is not offered any more (File with ShowFileTab=false, or an unknown id) must not
        // keep rendering a panel without its tab.
        if (!VisibleTabs.Contains(_activeTab, StringComparer.Ordinal))
        {
            _activeTab = "Home";
        }
    }

    private async Task SelectTabAsync(string tab)
    {
        if (string.Equals(_activeTab, tab, StringComparison.Ordinal))
        {
            return;
        }

        _activeTab = tab;
        if (ActiveTabChanged.HasDelegate)
        {
            await ActiveTabChanged.InvokeAsync(tab);
        }
    }

    /// <summary>Ribbon tab keyboard contract: ArrowLeft/Right cycle, Home/End jump, focus follows.</summary>
    private async Task HandleTabListKeyDownAsync(KeyboardEventArgs e)
    {
        var tabs = VisibleTabs;
        var index = tabs.ToList().IndexOf(_activeTab);
        if (index < 0)
        {
            index = 0;
        }

        var next = e.Key switch
        {
            "ArrowRight" => (index + 1) % tabs.Count,
            "ArrowLeft" => (index - 1 + tabs.Count) % tabs.Count,
            "Home" => 0,
            "End" => tabs.Count - 1,
            _ => -1
        };

        if (next < 0)
        {
            return;
        }

        var target = tabs[next];
        await SelectTabAsync(target);
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetToolbar.focusTab", TabId(target));
        }
        catch
        {
            // No script / prerender: the tab still activates, only the focus move is skipped.
        }
    }

    private Task SetTextColorOpenAsync(bool open)
    {
        _isTextColorOpen = open;
        return Task.CompletedTask;
    }

    private Task SetBackgroundColorOpenAsync(bool open)
    {
        _isBackgroundColorOpen = open;
        return Task.CompletedTask;
    }

    private Task SetBorderPickerOpenAsync(bool open)
    {
        _isBorderDropdownOpen = open;
        return Task.CompletedTask;
    }

    private Task SetDeleteMenuOpenAsync(bool open)
    {
        _isDeleteMenuOpen = open;
        return Task.CompletedTask;
    }

    private async Task AlignLeft() => await OnAlignChanged.InvokeAsync("left");
    private async Task AlignCenter() => await OnAlignChanged.InvokeAsync("center");
    private async Task AlignRight() => await OnAlignChanged.InvokeAsync("right");

    private async Task AlignVertical(string align) => await OnVerticalAlignChanged.InvokeAsync(align);

    private async Task ApplyBorderPreset(BorderPreset preset)
    {
        _lastBorderPreset = preset;
        _isBorderDropdownOpen = false;
        await OnBorderPresetChanged.InvokeAsync(preset);
    }

    private async Task ApplyLastBorderPreset()
    {
        await OnBorderPresetChanged.InvokeAsync(_lastBorderPreset);
    }

    private async Task OpenBorderDialog()
    {
        _isBorderDropdownOpen = false;
        await OnOpenBorderDialog.InvokeAsync();
    }

    private async Task OnDeleteRowFromMenu()
    {
        _isDeleteMenuOpen = false;
        await OnDeleteRow.InvokeAsync();
    }

    private async Task OnDeleteColumnFromMenu()
    {
        _isDeleteMenuOpen = false;
        await OnDeleteColumn.InvokeAsync();
    }

    private async Task OnTextColorSelected(string? color)
    {
        _isTextColorOpen = false;
        await OnTextColorChanged.InvokeAsync(color);
    }

    private async Task OnBackgroundColorSelected(string? color)
    {
        _isBackgroundColorOpen = false;
        await OnBackgroundColorChanged.InvokeAsync(color);
    }

    /// <summary>Escape closes the text-colour picker (the browser path also closes it through overlay.js).</summary>
    private void HandleTextColorKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _isTextColorOpen = false;
        }
    }

    /// <summary>Escape closes the fill-colour picker.</summary>
    private void HandleBackgroundColorKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _isBackgroundColorOpen = false;
        }
    }

    /// <summary>Escape closes the border picker (one activation per press, per the keyboard convention).</summary>
    private void HandleBorderPickerKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _isBorderDropdownOpen = false;
        }
    }

    /// <summary>Escape closes the delete split menu.</summary>
    private void HandleDeleteMenuKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape")
        {
            _isDeleteMenuOpen = false;
        }
    }

    private static string GetBorderPresetIconName(BorderPreset preset) => preset switch
    {
        BorderPreset.None => "tm-ss-border-none",
        BorderPreset.AllBorders => "tm-ss-border-all",
        BorderPreset.OutsideBorders => "tm-ss-border-outside",
        BorderPreset.ThickBox => "tm-ss-border-thick",
        BorderPreset.BottomBorder => "tm-ss-border-bottom",
        BorderPreset.ThickBottom => "tm-ss-border-thick-bottom",
        BorderPreset.DoubleBottom => "tm-ss-border-double-bottom",
        BorderPreset.TopBorder => "tm-ss-border-top",
        BorderPreset.LeftBorder => "tm-ss-border-left",
        BorderPreset.RightBorder => "tm-ss-border-right",
        BorderPreset.TopAndThickBottom => "tm-ss-border-top-thick-bottom",
        _ => "tm-ss-border-all"
    };

    private static string GetActiveClass(bool isActive)
    {
        return isActive ? "tm-spreadsheet-toolbar__button--active" : string.Empty;
    }

    private IReadOnlyList<SpreadsheetCustomTool> GetCustomTools(string tab)
    {
        if (CustomTools is null)
        {
            return Array.Empty<SpreadsheetCustomTool>();
        }

        return CustomTools
            .Where(tool => string.Equals(tool.Tab, tab, StringComparison.OrdinalIgnoreCase))
            .OrderBy(tool => tool.Order)
            .ToArray();
    }
}
