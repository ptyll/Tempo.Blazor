using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Phase 6 — the COMPACT ribbon: the tablet (two rows) and mobile (one row) chrome. The rows are the core
/// <see cref="TmToolbar"/> with <c>Overflow=Menu</c>, so a command that does not fit moves into "More"
/// instead of off the edge; WHICH commands a layout offers is <see cref="SpreadsheetToolbarLayout"/>'s
/// decision, not the measurement's.
/// </summary>
public partial class TmSpreadsheetToolbar
{
    /// <summary>
    /// The palette the colour commands offer in their "More" representation. The bar keeps the full
    /// <c>TmFlatColorPicker</c>; a menu entry can only be a button, so the overflow offers the eight
    /// primaries (a colour VALUE is data — it is what the user applies to a cell — never a style).
    /// </summary>
    private static readonly string[] CompactPalette =
        ["#000000", "#C00000", "#FF8C00", "#FFD400", "#00A050", "#0066CC", "#7030A0", "#808080"];

    /// <summary>The resolved layout the ribbon renders in (never <see cref="TmLayoutMode.Auto"/>).</summary>
    private TmLayoutMode ResolvedLayout => LayoutMode switch
    {
        TmLayoutMode.Auto => Viewport?.Resolved ?? TmLayoutMode.Desktop,
        var forced => forced,
    };

    /// <summary>The compact rows for the resolved layout; empty on the desktop layout (which renders the tab strip).</summary>
    private IReadOnlyList<SpreadsheetToolbarRow> CompactRows => SpreadsheetToolbarLayout.Rows(ResolvedLayout, ShowFileTab);

    /// <summary>Whether the compact presentation is in charge.</summary>
    private bool IsCompact => CompactRows.Count > 0;

    /// <summary>Machine-readable layout name for <c>data-toolbar-layout</c>.</summary>
    private string LayoutName => ResolvedLayout switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        _ => "desktop",
    };

    /// <summary>Pressed state of a toggle command, or null for a plain button.</summary>
    private bool? PressedFor(string id) => id switch
    {
        "bold" => IsBold,
        "italic" => IsItalic,
        "underline" => IsUnderline,
        "strike" => IsStrikeThrough,
        "percent" => IsPercentageFormat,
        "thousands" => IsThousandsFormat,
        "filter" => IsFilterActive,
        "grid-lines" => ShowGridLines,
        "merge-cells" => IsMergeCellsActive,
        "format-painter" => IsFormatPainterActive,
        _ => null,
    };

    /// <summary>Whether a compact command is disabled.</summary>
    private bool DisabledFor(string id) => id switch
    {
        "undo" => !CanUndo,
        "redo" => !CanRedo,
        _ => false,
    };

    /// <summary>
    /// The text a compact button carries. It is ALWAYS the full localized name — the core <c>TmToolbarButton</c>
    /// derives its "More" entry label from this text, so a terse glyph ("U") would leave an unreadable,
    /// unsearchable menu entry. On the rows that hide labels (<c>ToolbarLabels.Icons</c>) the text is not
    /// painted; on the tablet's primary row it is the visible name.
    /// </summary>
    private string CompactTextFor(SpreadsheetToolbarCommand command) => Loc[command.LabelKey];

    private static readonly List<SelectOption<string>> _alignOptions =
    [
        new("left", "Left", false, "align-left"),
        new("center", "Center", false, "align-center"),
        new("right", "Right", false, "align-right"),
    ];

    /// <summary>The value the compact select shows.</summary>
    private string? SelectValueFor(string id) => id switch
    {
        "font-family" => SelectedFontFamily,
        "font-size" => SelectedFontSize,
        "align" => SelectedHorizontalAlign ?? "left",
        _ => SelectedNumberFormat,
    };

    /// <summary>The options of the compact select.</summary>
    private IReadOnlyList<SelectOption<string>> SelectOptionsFor(string id) => id switch
    {
        "font-family" => _fontOptions,
        "font-size" => _fontSizeOptions,
        "align" => _alignOptions,
        _ => _numberFormatOptions,
    };

    /// <summary>The change callback of the compact select (the alignment control maps onto the same callback the ribbon's three buttons use).</summary>
    private EventCallback<string?> SelectChangedFor(string id) => id switch
    {
        "font-family" => OnFontFamilyChanged,
        "font-size" => OnFontSizeChanged,
        "align" => OnAlignChanged,
        _ => OnNumberFormatChanged,
    };

    /// <summary>
    /// Runs a compact command. One dispatch point keeps the model's ids and the host callbacks together —
    /// <c>TmSpreadsheetPhase6LayoutTests</c> walks every id of every row through it so an unmapped id fails
    /// the suite instead of dying silently.
    /// </summary>
    private Task DispatchAsync(string id) => id switch
    {
        "file-open" => OnOpen.InvokeAsync(),
        "file-download" => OnDownload.InvokeAsync(),
        "undo" => OnUndo.InvokeAsync(),
        "redo" => OnRedo.InvokeAsync(),
        "paste" => OnPaste.InvokeAsync(),
        "cut" => OnCut.InvokeAsync(),
        "copy" => OnCopy.InvokeAsync(),
        "format-cells" => OnFormatCells.InvokeAsync(),
        "insert-image" => OnInsertImage.InvokeAsync(),
        "insert-link" => OnInsertLink.InvokeAsync(),
        "format-painter" => OnFormatPainterClick.InvokeAsync(),
        "insert-row" => OnInsertRow.InvokeAsync(),
        "delete-row" => OnDeleteRow.InvokeAsync(),
        "insert-column" => OnInsertColumn.InvokeAsync(),
        "delete-column" => OnDeleteColumn.InvokeAsync(),
        "merge-cells" => OnMergeCells.InvokeAsync(),
        "paste-special" => OnPasteSpecial.InvokeAsync(),
        "text-to-columns" => OnTextToColumns.InvokeAsync(),
        "remove-duplicates" => OnRemoveDuplicates.InvokeAsync(),
        "data-validation" => OnDataValidation.InvokeAsync(),
        "circle-invalid" => OnCircleInvalidData.InvokeAsync(),
        "clear-circles" => OnClearValidationCircles.InvokeAsync(),
        "name-manager" => OnNameManager.InvokeAsync(),
        "grid-lines" => OnToggleGridLines.InvokeAsync(),
        "bold" => OnBoldToggle.InvokeAsync(),
        "italic" => OnItalicToggle.InvokeAsync(),
        "underline" => OnUnderlineToggle.InvokeAsync(),
        "strike" => OnStrikeThroughToggle.InvokeAsync(),
        "align-left" => OnAlignChanged.InvokeAsync("left"),
        "align-center" => OnAlignChanged.InvokeAsync("center"),
        "align-right" => OnAlignChanged.InvokeAsync("right"),
        "percent" => OnPercentageFormat.InvokeAsync(),
        "thousands" => OnThousandsFormat.InvokeAsync(),
        "decimals-increase" => OnIncreaseDecimals.InvokeAsync(),
        "decimals-decrease" => OnDecreaseDecimals.InvokeAsync(),
        "filter" => OnToggleFilter.InvokeAsync(),
        "sort-asc" => OnSortAscending.InvokeAsync(),
        "sort-desc" => OnSortDescending.InvokeAsync(),
        "custom-sort" => OnCustomSort.InvokeAsync(),
        _ => Task.CompletedTask,
    };

    /// <summary>Applies the current colour again (the compact control's main button).</summary>
    private Task ApplyColorAsync(bool isText)
        => isText ? OnTextColorChanged.InvokeAsync(TextColor) : OnBackgroundColorChanged.InvokeAsync(BackgroundColor);

    /// <summary>The "More" entries of a select command: the same options the on-bar select offers.</summary>
    private IReadOnlyList<TmActionItem> SelectMenuItems(SpreadsheetToolbarCommand command)
    {
        var options = SelectOptionsFor(command.Id);
        var selected = SelectValueFor(command.Id);
        var changed = SelectChangedFor(command.Id);

        var items = new List<TmActionItem>(options.Count);
        foreach (var option in options)
        {
            var value = option.Value;
            items.Add(new TmActionItem
            {
                Id = $"{command.Id}:{option.Value}",
                Label = option.Label,
                Checked = string.Equals(value, selected, StringComparison.Ordinal),
                OnClick = EventCallback.Factory.Create(this, () => changed.InvokeAsync(value)),
            });
        }

        return items;
    }

    /// <summary>The "More" entries of a colour command: the compact palette plus a reset.</summary>
    private IReadOnlyList<TmActionItem> ColorMenuItems(string id)
    {
        var isText = id == "text-color";
        var current = isText ? TextColor : BackgroundColor;
        var changed = isText ? OnTextColorChanged : OnBackgroundColorChanged;

        var items = new List<TmActionItem>(CompactPalette.Length + 1);
        foreach (var color in CompactPalette)
        {
            var value = color;
            items.Add(new TmActionItem
            {
                Id = $"{id}:{value.TrimStart('#')}",
                Label = value,
                Checked = string.Equals(value, current, StringComparison.OrdinalIgnoreCase),
                OnClick = EventCallback.Factory.Create(this, () => changed.InvokeAsync(value)),
            });
        }

        items.Add(new TmActionItem
        {
            Id = $"{id}:none",
            Label = Loc["TmSpreadsheet_Color_None"],
            OnClick = EventCallback.Factory.Create(this, () => changed.InvokeAsync(null)),
        });

        return items;
    }

    /// <summary>The "More" entries of the border split: every preset the on-bar picker offers.</summary>
    private IReadOnlyList<TmActionItem> BorderMenuItems
    {
        get
        {
            BorderPreset[] presets =
            [
                BorderPreset.None, BorderPreset.AllBorders, BorderPreset.OutsideBorders, BorderPreset.ThickBox,
                BorderPreset.BottomBorder, BorderPreset.ThickBottom, BorderPreset.DoubleBottom,
                BorderPreset.TopAndThickBottom, BorderPreset.TopBorder, BorderPreset.LeftBorder, BorderPreset.RightBorder,
            ];

            var items = new List<TmActionItem>(presets.Length);
            foreach (var preset in presets)
            {
                var value = preset;
                items.Add(new TmActionItem
                {
                    Id = $"borders:{value}",
                    Label = Loc[$"TmSpreadsheet_Border_{value}"],
                    Checked = _lastBorderPreset == value,
                    OnClick = EventCallback.Factory.Create(this, () => ApplyBorderPreset(value)),
                });
            }

            return items;
        }
    }
}
