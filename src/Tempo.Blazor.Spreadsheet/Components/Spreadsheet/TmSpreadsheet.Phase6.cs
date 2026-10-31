using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Spreadsheet.Enums;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Phase 6 — the responsive chrome. The layout is decided by the spreadsheet's OWN container (a container
/// query for the styles, the core <see cref="Layout.TmLayoutObserver"/> for the markup), never by the
/// viewport: a 390px Notion block inside a 1440px page is a phone.
/// <para>
/// Mobile is the G2 contract — viewing plus basic editing: the compact header keeps the title and the
/// undo/redo pair, one formatting row keeps the basic marks, the sheet tabs sit above the core
/// <c>TmMobileActionBar</c> (Insert / Format / Insert image / More), the status bar is gone (its
/// aggregations and zoom move into "More") and the grid fills the container height. The advanced data tools
/// are simply not offered below the tablet breakpoint.
/// </para>
/// </summary>
public partial class TmSpreadsheet
{
    /// <summary>The resolved layout of the last render. Reconciled in render (like every container-query
    /// consumer in this repo): the observer's callback alone can miss the first measurement of a page that
    /// mounts inside a narrow container.</summary>
    private TmLayoutMode _layout = TmLayoutMode.Desktop;

    /// <summary>
    /// The layout currently rendered, never <see cref="TmLayoutMode.Auto"/>. A forced <see cref="LayoutMode"/>
    /// wins immediately — the root element's attributes are written BEFORE <c>LayoutContent</c> runs, so they
    /// cannot wait for the observer's cascade and must read the effective value.
    /// </summary>
    internal TmLayoutMode ResolvedLayout => LayoutMode == TmLayoutMode.Auto ? _layout : LayoutMode;

    /// <summary>True on the phone chrome (the G2 narrowing).</summary>
    private bool IsMobileLayout => ResolvedLayout == TmLayoutMode.Mobile;

    /// <summary>True on the two-row tablet chrome.</summary>
    private bool IsTabletLayout => ResolvedLayout == TmLayoutMode.Tablet;

    /// <summary>
    /// SP3: only the mobile layout fills its container. The desktop/tablet chrome keeps the fixed
    /// <see cref="Height"/> (600px by default), because a desktop host usually lays the sheet into a card with
    /// its own height.
    /// </summary>
    private string EffectiveHeight => IsMobileLayout ? "100%" : Height ?? "600px";

    /// <summary>The CSS width (unchanged — the height is the mode-dependent one).</summary>
    private string EffectiveWidth => Width ?? "100%";

    /// <summary>
    /// The density handed to the grid. An explicit <see cref="Density"/> always wins; on the mobile layout the
    /// <see cref="SpreadsheetDensity.Auto"/> default becomes <see cref="SpreadsheetDensity.Comfortable"/>,
    /// because a phone is a coarse pointer by definition and the reference draws the taller rows.
    /// </summary>
    private SpreadsheetDensity EffectiveDensity => Density switch
    {
        SpreadsheetDensity.Auto when IsMobileLayout => SpreadsheetDensity.Comfortable,
        _ => Density,
    };

    /// <summary>
    /// Breaks the layout down to the render branches. Called from <c>LayoutContent</c> on every render so a
    /// page that mounts inside a narrow container flips on its FIRST frame instead of waiting for the
    /// observer callback.
    /// </summary>
    private void ApplyLayout(TmLayoutMode resolved)
    {
        if (_layout == resolved)
        {
            return;
        }

        _layout = resolved;
    }

    /// <summary>
    /// The observer's measurement (Auto only). The render-time <see cref="ApplyLayout"/> is the primary
    /// reconciliation point; this handler covers a resize WITHIN a mounted spreadsheet, where no render of the
    /// owner would otherwise follow.
    /// </summary>
    private void OnResolvedLayoutChanged(TmLayoutMode resolved)
    {
        if (_layout == resolved)
        {
            return;
        }

        _layout = resolved;
        StateHasChanged();
    }

    // ── Mobile action bar ─────────────────────────────────────────────────────

    /// <summary>
    /// The F5 action bar's items for the mobile layout: Insert / Format / Insert image stay on the bar, the
    /// sheet and data commands plus the zoom and the status-bar aggregations fill the "More" menu (grouped —
    /// <see cref="TmActionItem.GroupLabel"/> appears as the section header).
    /// </summary>
    private IReadOnlyList<TmActionItem> BuildMobileActions()
    {
        var actions = new List<TmActionItem>
        {
            new()
            {
                Id = "insert",
                Label = Loc["TmSpreadsheet_Mobile_Insert"],
                Icon = "plus",
                OnClick = EventCallback.Factory.Create(this, InsertRow),
            },
            new()
            {
                Id = "format",
                Label = Loc["TmSpreadsheet_Mobile_Format"],
                Icon = "type",
                OnClick = EventCallback.Factory.Create(this, ShowFormatCellsDialog),
            },
            new()
            {
                Id = "insert-image",
                Label = Loc["TmSpreadsheet_Mobile_Image"],
                Icon = "image",
                OnClick = EventCallback.Factory.Create(this, ShowInsertImageDialog),
            },

            // ── Sheet (grouped) ──
            new()
            {
                Id = "add-sheet",
                Label = Loc["TmSpreadsheet_AddSheet"],
                Icon = "plus",
                Group = "sheet",
                GroupLabel = Loc["TmSpreadsheet_Group_Sheet"],
                KeepMenuOpen = true,
                OnClick = EventCallback.Factory.Create(this, AddNewSheet),
            },

            // ── Data (grouped) — the basic filter/sort part of G2 ──
            new()
            {
                Id = "filter",
                Label = Loc["TmSpreadsheet_Data_Filter"],
                Icon = "filter",
                Group = "data",
                GroupLabel = Loc["TmSpreadsheet_Group_Data"],
                Checked = IsAutoFilterActive,
                OnClick = EventCallback.Factory.Create(this, ToggleAutoFilter),
            },
            new()
            {
                Id = "sort-asc",
                Label = Loc["TmSpreadsheet_Data_SortAsc"],
                Icon = "arrow-up",
                Group = "data",
                GroupLabel = Loc["TmSpreadsheet_Group_Data"],
                OnClick = EventCallback.Factory.Create(this, SortSelectionAscending),
            },
            new()
            {
                Id = "sort-desc",
                Label = Loc["TmSpreadsheet_Data_SortDesc"],
                Icon = "arrow-down",
                Group = "data",
                GroupLabel = Loc["TmSpreadsheet_Group_Data"],
                OnClick = EventCallback.Factory.Create(this, SortSelectionDescending),
            },

            // ── Zoom (grouped) — the status bar's zoom controls, which the phone chrome hides ──
            new()
            {
                Id = "zoom-out",
                Label = Loc["TmSpreadsheet_ZoomOut"],
                Icon = "minus",
                Group = "zoom",
                GroupLabel = Loc["TmSpreadsheet_Group_Zoom"],
                Disabled = ZoomPercent <= 50,
                OnClick = EventCallback.Factory.Create(this, () => OnZoomStep(-1)),
            },
            new()
            {
                Id = "zoom-in",
                Label = Loc["TmSpreadsheet_ZoomIn"],
                Icon = "plus",
                Group = "zoom",
                GroupLabel = Loc["TmSpreadsheet_Group_Zoom"],
                Disabled = ZoomPercent >= 200,
                OnClick = EventCallback.Factory.Create(this, () => OnZoomStep(1)),
            },
            new()
            {
                Id = "zoom-reset",
                Label = Loc["TmSpreadsheet_ZoomReset"],
                Icon = "refresh",
                Group = "zoom",
                GroupLabel = Loc["TmSpreadsheet_Group_Zoom"],
                OnClick = EventCallback.Factory.Create(this, ResetZoom),
            },
            new()
            {
                // The current zoom as its own inert row, so the reset action's label stays a clean verb.
                Id = "zoom-level",
                Label = $"{ZoomPercent} %",
                Group = "zoom",
                GroupLabel = Loc["TmSpreadsheet_Group_Zoom"],
                Disabled = true,
            },
        };

        AppendSummaryActions(actions);
        return actions;
    }

    /// <summary>
    /// The status-bar aggregations, re-offered as inert "More" rows under a "Summary" header (the phone
    /// chrome has no status bar). They are information, not actions, hence <c>Disabled</c>.
    /// </summary>
    private void AppendSummaryActions(List<TmActionItem> actions)
    {
        var aggregation = StatusAggregation;
        var summary = Loc["TmSpreadsheet_Status_Summary"];

        void Add(string id, string label, string value)
            => actions.Add(new TmActionItem
            {
                Id = id,
                Label = $"{label}: {value}",
                Group = "summary",
                GroupLabel = summary,
                Disabled = true,
            });

        Add("summary-count", Loc["TmSpreadsheet_Status_Count"], aggregation.Count.ToString(CultureInfo.CurrentCulture));
        if (aggregation.HasNumbers)
        {
            Add("summary-sum", Loc["TmSpreadsheet_Status_Sum"], FormatAggregate(aggregation.Sum));
            Add("summary-average", Loc["TmSpreadsheet_Status_Average"], FormatAggregate(aggregation.Average));
            Add("summary-min", Loc["TmSpreadsheet_Status_Min"], FormatAggregate(aggregation.Min));
            Add("summary-max", Loc["TmSpreadsheet_Status_Max"], FormatAggregate(aggregation.Max));
        }
    }

    private static string FormatAggregate(double? value)
        => (value ?? 0).ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>The zoom as a rounded percentage (the mobile "More" entries and the status bar share it).</summary>
    private int ZoomPercent => (int)Math.Round(_zoom * 100);

    // ── Context menus (rendered by TmSpreadsheetContextMenu, a sibling of the layout container) ──

    private TmSpreadsheetSheetTabs? _sheetTabs;
    private SpreadsheetContextMenuRequest? _gridContextMenu;
    private SpreadsheetContextMenuRequest? _sheetTabContextMenu;
    private int _sheetTabContextMenuIndex = -1;
    private (int Row, int Column) _gridContextMenuIndex = (-1, -1);
    private SpreadsheetResizeTarget _resizeTarget;
    private int _resizeIndex = -1;
    private string _resizeInputValue = string.Empty;

    /// <summary>
    /// True while one of the host's OWN surfaces owns the focus: an open context menu (the anchored desktop
    /// menu or the phone sheet) or the host-owned resize dialog. The grid's canvased focus restore is then
    /// suppressed — the canvas activation that opened the surface arms it, and the grid's
    /// <c>OnAfterRenderAsync</c> runs in the same batch as the surface's, so its <c>isFocusOutside</c>
    /// probe answers stale and the <c>FocusAsync</c> hop lands ~25 ms AFTER the menu focused its first
    /// entry, killing the menu's ArrowDown / Home / End walk (phase 7d).
    /// </summary>
    private bool IsHostSurfaceFocused => _gridContextMenu is not null
        || _sheetTabContextMenu is not null
        || _showResizeDialog;

    /// <summary>Which model size the host-owned resize dialog edits.</summary>
    private enum SpreadsheetResizeTarget
    {
        None,
        Column,
        Row,
    }

    /// <summary>Opens the cell menu for <paramref name="request"/> (raised by the grid).</summary>
    private Task OpenGridContextMenu(SpreadsheetContextMenuRequest request)
    {
        _sheetTabContextMenu = null;
        _gridContextMenu = request;
        _gridContextMenuIndex = (request.Row, request.Column);
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>Opens the sheet-tab menu for <paramref name="request"/> (raised by the tabs).</summary>
    private Task OpenSheetTabContextMenu(SpreadsheetContextMenuRequest request)
    {
        _gridContextMenu = null;
        _sheetTabContextMenu = request;
        _sheetTabContextMenuIndex = request.SheetIndex;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private void CloseGridContextMenu()
    {
        if (_gridContextMenu is null)
        {
            return;
        }

        _gridContextMenu = null;
        StateHasChanged();
    }

    private void CloseSheetTabContextMenu()
    {
        if (_sheetTabContextMenu is null)
        {
            return;
        }

        // The index is deliberately NOT cleared here: the menu closes BEFORE its action runs, and the action
        // reads the index. OnSheetTabContextMenuSelect clears it after use.
        _sheetTabContextMenu = null;
        StateHasChanged();
    }

    /// <summary>
    /// The cell menu's entries (labels are localized here, ids are the host's dispatch keys).
    /// A READ-ONLY grid (an Embedded Notion block, a read-only host) offers only the observation and
    /// layout commands (Format Cells, Copy, column width / row height): Cut, Paste, Fill down/right,
    /// row/column insert/delete and every Clear are gone — a menu entry that would mutate the workbook must
    /// not exist when the grid cannot be edited. The grid itself never opens the menu in read-only mode;
    /// this is the HOST-side guard for the path that renders the menu outside the grid.
    /// </summary>
    private IReadOnlyList<SpreadsheetContextMenuItem> GridContextMenuItems
    {
        get
        {
            var readOnly = Mode == SpreadsheetMode.Embedded;
            List<SpreadsheetContextMenuItem> items =
            [
                new() { Id = "format-cells", Label = Loc["TmSpreadsheet_FormatCells"] },
                new() { Id = "copy", Label = Loc["TmSpreadsheet_Copy"], DividerBefore = true },
            ];

            if (!readOnly)
            {
                items.Add(new() { Id = "cut", Label = Loc["TmSpreadsheet_Cut"] });
                items.Add(new() { Id = "paste", Label = Loc["TmSpreadsheet_Paste"] });
                // The fill-handle drag's WCAG 2.5.7 alternative (phase 7): the same AutoFillCommand, one tap.
                items.Add(new() { Id = "fill-down", Label = Loc["TmSpreadsheet_FillDown"] });
                items.Add(new() { Id = "fill-right", Label = Loc["TmSpreadsheet_FillRight"] });
                items.Add(new() { Id = "insert-row", Label = Loc["TmSpreadsheet_InsertRow"], DividerBefore = true });
                items.Add(new() { Id = "delete-row", Label = Loc["TmSpreadsheet_DeleteRow"], Danger = true });
                items.Add(new() { Id = "insert-column", Label = Loc["TmSpreadsheet_InsertColumn"] });
                items.Add(new() { Id = "delete-column", Label = Loc["TmSpreadsheet_DeleteColumn"], Danger = true });
            }

            // Column width / row height are RENDER hints a read-only viewer may still want, and they are
            // pure layout on an Embedded block, so they stay. Clear/format writes do not.
            items.Add(new() { Id = "column-width", Label = Loc["TmSpreadsheet_SetColumnWidth"], DividerBefore = true });
            items.Add(new() { Id = "row-height", Label = Loc["TmSpreadsheet_SetRowHeight"] });
            if (!readOnly)
            {
                items.Add(new() { Id = "clear-formatting", Label = Loc["TmSpreadsheet_ClearFormatting"], DividerBefore = true });
                items.Add(new() { Id = "clear-content", Label = Loc["TmSpreadsheet_ClearContent"] });
                items.Add(new() { Id = "clear-all", Label = Loc["TmSpreadsheet_ClearAll"], Danger = true });
            }

            return items;
        }
    }

    /// <summary>The sheet-tab menu's entries.</summary>
    private IReadOnlyList<SpreadsheetContextMenuItem> SheetTabContextMenuItems =>
    [
        new() { Id = "rename-sheet", Label = Loc["TmSpreadsheet_RenameSheet"] },
        new() { Id = "delete-sheet", Label = Loc["TmSpreadsheet_DeleteSheet"], Danger = true },
    ];

    /// <summary>Runs the selected cell-menu entry. One dispatch point: an id with no case fails a test, not the user.</summary>
    private Task OnGridContextMenuSelect(string id) => id switch
    {
        "format-cells" => CloseAndRunAsync(ShowFormatCellsDialog),
        "copy" => CloseAndRunAsync(() => Copy()),
        "cut" => CloseAndRunAsync(() => Cut()),
        "paste" => CloseAndRunAsync(() => Paste()),
        "fill-down" => CloseAndRunAsync(() => OnGridFillRequested(SpreadsheetFillDirection.Down)),
        "fill-right" => CloseAndRunAsync(() => OnGridFillRequested(SpreadsheetFillDirection.Right)),
        "insert-row" => CloseAndRunAsync(InsertRow),
        "delete-row" => CloseAndRunAsync(DeleteRow),
        "insert-column" => CloseAndRunAsync(InsertColumn),
        "delete-column" => CloseAndRunAsync(DeleteColumn),
        "column-width" => CloseAndRunAsync(() => OpenResizeDialog(SpreadsheetResizeTarget.Column, _gridContextMenuIndex.Column)),
        "row-height" => CloseAndRunAsync(() => OpenResizeDialog(SpreadsheetResizeTarget.Row, _gridContextMenuIndex.Row)),
        "clear-formatting" => CloseAndRunAsync(ClearFormatting),
        "clear-content" => CloseAndRunAsync(ClearContent),
        "clear-all" => CloseAndRunAsync(ClearAll),
        _ => Task.CompletedTask,
    };

    /// <summary>Runs the selected sheet-tab entry.</summary>
    private Task OnSheetTabContextMenuSelect(string id)
    {
        // The menu closes BEFORE the action runs (OnClose), so the request is already gone here: the index is
        // captured when the menu OPENS.
        var index = _sheetTabContextMenuIndex;
        CloseSheetTabContextMenu();

        var result = id switch
        {
            "rename-sheet" => RunOnTabsAsync(tabs => tabs.RequestRename(index)),
            "delete-sheet" => RunOnTabsAsync(tabs => tabs.RequestDelete(index)),
            _ => Task.CompletedTask,
        };
        _sheetTabContextMenuIndex = -1;
        return result;
    }

    private Task RunOnTabsAsync(Action<TmSpreadsheetSheetTabs> action)
    {
        if (_sheetTabs is not null)
        {
            action(_sheetTabs);
        }

        return Task.CompletedTask;
    }

    /// <summary>Closes the menu BEFORE the command runs (the menu must not sit above the dialog it opens).</summary>
    private Task CloseAndRunAsync(Action? action)
    {
        // State first, then the action: the menu is gone from the tree before the dialog it opens renders,
        // and the whole transition happens inside one dispatch (no Task.Yield — a test's click is synchronous).
        CloseGridContextMenu();
        action?.Invoke();
        return Task.CompletedTask;
    }

    /// <summary>The async form of <see cref="CloseAndRunAsync(Action?)"/>.</summary>
    private Task CloseAndRunAsync(Func<Task> action)
    {
        CloseGridContextMenu();
        return action();
    }

    // ── Resize dialogs (host-owned: TmModal surfaces must not live inside the layout container) ──

    private bool _showResizeDialog;

    private string ResizeInputDomId => $"tm-spreadsheet-resize-input-{_resizeTarget.ToString().ToLowerInvariant()}-{_resizeIndex}";

    /// <summary>
    /// Opens the column-width / row-height dialog for <paramref name="index"/>. The dialog edits the MODEL size
    /// (a stored width/height or the sheet default) and applies through the SAME resize path the canvas drag
    /// uses (<see cref="OnColumnResizeRequested"/>/<see cref="OnRowResizeRequested"/>), which converts rendered
    /// pixels to model units — the dialog itself never deals with zoom or density.
    /// </summary>
    private void OpenResizeDialog(SpreadsheetResizeTarget target, int index)
    {
        if (_workbook.ActiveSheet is not { } sheet || index < 0)
        {
            return;
        }

        _resizeTarget = target;
        _resizeIndex = index;
        _resizeInputValue = target switch
        {
            SpreadsheetResizeTarget.Column => ((int)Math.Round(
                sheet.Columns.TryGetValue(index, out var column) && column.Width is { } stored ? stored : ColumnWidth)).ToString(),
            _ => ((int)Math.Round(
                sheet.Rows.TryGetValue(index, out var row) && row.Height is { } storedHeight ? storedHeight : RowHeight)).ToString(),
        };
        _showResizeDialog = true;
        StateHasChanged();
    }

    private void CloseResizeDialog()
    {
        _showResizeDialog = false;
        _resizeTarget = SpreadsheetResizeTarget.None;
        _resizeIndex = -1;
        StateHasChanged();
    }

    private async Task ApplyResizeDialogAsync()
    {
        if (!double.TryParse(_resizeInputValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            CloseResizeDialog();
            return;
        }

        var target = _resizeTarget;
        var index = _resizeIndex;
        _showResizeDialog = false;
        _resizeTarget = SpreadsheetResizeTarget.None;
        _resizeIndex = -1;

        if (target == SpreadsheetResizeTarget.Column)
        {
            await OnColumnResizeRequested((index, Math.Max(16, value)));
        }
        else if (target == SpreadsheetResizeTarget.Row)
        {
            await OnRowResizeRequested((index, Math.Max(8, value)));
        }

        StateHasChanged();
    }

    /// <summary>Enter commits the typed value (the field is the dialog's only control).</summary>
    private async Task OnResizeInputKeyDownAsync(KeyboardEventArgs e)
    {
        if (e.Key == "Enter")
        {
            await ApplyResizeDialogAsync();
        }
    }

    private string ResizeDialogTitle => _resizeTarget == SpreadsheetResizeTarget.Column
        ? Loc["TmSpreadsheet_SetColumnWidth"]
        : Loc["TmSpreadsheet_SetRowHeight"];

    /// <summary>True while a context sheet owns the phone layout (its drawer reports a close).</summary>
    private void OnGridContextSheetOpenChanged(bool open)
    {
        if (!open)
        {
            CloseGridContextMenu();
            RestoreGridFocus();
        }
    }

    private void OnSheetTabSheetOpenChanged(bool open)
    {
        if (!open)
        {
            CloseSheetTabContextMenu();
            RestoreGridFocus();
        }
    }

    private void RestoreGridFocus()
    {
        if (string.IsNullOrEmpty(GridRootDomId))
        {
            return;
        }

        _ = InvokeAsync(async () =>
        {
            try
            {
                await JS.InvokeVoidAsync("tmSpreadsheetToolbar.focusTab", GridRootDomId);
            }
            catch
            {
                // No script (prerender/bUnit): the close itself is what matters.
            }
        });
    }

    private bool IsMobileBarSuppressed =>
        _gridContextMenu is not null || (_filterDropdownColumn is not null && IsMobileLayout);

    private string MobileBarClass => IsMobileBarSuppressed
        ? "tm-spreadsheet__mobile-bar tm-spreadsheet__mobile-bar--suppressed"
        : "tm-spreadsheet__mobile-bar";

    // ── Mobile sheet surfaces ─────────────────────────────────────────────────

    /// <summary>The layout handed to a dialog/overlay that presents as a sheet on a phone.</summary>
    private TmLayoutMode DialogLayout => ResolvedLayout;

    /// <summary>
    /// Whether the compact mobile header has anything to render: the host's title or one of its two slots.
    /// The undo/redo pair lives in the mobile formatting row, not in this header, so an empty header can be
    /// dropped without losing functionality.
    /// </summary>
    private bool HasMobileHeaderContent
        => !string.IsNullOrWhiteSpace(Title) || HeaderStart is not null || HeaderActions is not null;

    /// <summary>Machine-readable layout name for the root's <c>data-layout</c> and its modifier class.</summary>
    private string LayoutName => ResolvedLayout switch
    {
        TmLayoutMode.Mobile => "mobile",
        TmLayoutMode.Tablet => "tablet",
        _ => "desktop",
    };
}
