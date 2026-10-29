using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Toolbar;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// What a compact ribbon command is: how the compact toolbar row renders it.
/// </summary>
internal enum SpreadsheetToolbarCommandKind
{
    /// <summary>A plain command button (an icon, tooltip and accessible name).</summary>
    Button,

    /// <summary>A toggle button whose pressed state is the selection's (<c>aria-pressed</c>).</summary>
    Toggle,

    /// <summary>A select item — the font family / font size / number format list, rendered through a <c>TmToolbarItem</c>.</summary>
    Select,

    /// <summary>The cell-border split button with its preset menu.</summary>
    Border,

    /// <summary>A text-colour or fill-colour split button with its palette menu.</summary>
    Color,
}

/// <summary>
/// One command of a compact ribbon row: identity, priority (an overflow rank, never an order) and the
/// icon/label the toolbar renders.
/// </summary>
internal sealed record SpreadsheetToolbarCommand
{
    /// <summary>Stable command id, kebab case and never prefixed with <c>tm-</c>. Used as the render key and the dispatch key.</summary>
    public required string Id { get; init; }

    /// <summary>How the command renders.</summary>
    public SpreadsheetToolbarCommandKind Kind { get; init; } = SpreadsheetToolbarCommandKind.Button;

    /// <summary>Icon name (<c>IconNames</c> or a registered package icon). Null renders a text mark (B / I / U).</summary>
    public string? Icon { get; init; }

    /// <summary>Localization key of the visible label and the accessible name.</summary>
    public required string LabelKey { get; init; }

    /// <summary>
    /// Overflow rank — NEVER a render order (the row renders in list order). Secondary commands leave the
    /// row first, <see cref="ToolbarButtonPriority.OverflowOnly"/> commands never render on the row.
    /// </summary>
    public ToolbarButtonPriority Priority { get; init; } = ToolbarButtonPriority.Primary;

    /// <summary>
    /// The frozen <c>data-tm-group</c> id the command belongs to (the same set the ribbon's groups use:
    /// <c>clipboard | font | alignment | number | cells | insert | data | view | file | custom</c>). The compact
    /// rows render a core <c>TmToolbarGroup</c> per id, so a host keys off the SAME attribute in every layout.
    /// </summary>
    public string? GroupId { get; init; }

    /// <summary>Localization key of the group's accessible name and of its "More" section header.</summary>
    public string? GroupLabelKey { get; init; }
}

/// <summary>One rendered row of the compact ribbon.</summary>
internal sealed record SpreadsheetToolbarRow
{
    /// <summary>Stable row id (<c>primary</c>, <c>formatting</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Localization key of the row's accessible name.</summary>
    public required string LabelKey { get; init; }

    /// <summary>The commands of the row, in render order.</summary>
    public required IReadOnlyList<SpreadsheetToolbarCommand> Commands { get; init; }
}

/// <summary>
/// The compact ribbon model (redesign phase 6). The desktop ribbon is the tab strip of phase 5; the tablet
/// and mobile chrome are ONE-row / TWO-row sets of named commands instead, driven by the core overflow
/// toolbar (<c>TmToolbar Overflow=Menu</c>) so a command that does not fit moves into "More" rather than
/// off the edge. Nothing here paints: the model says WHICH commands a layout offers and how important each
/// one is, the toolbar maps them onto the core <c>TmToolbarButton</c> / <c>TmToolbarItem</c> primitives.
/// <para>
/// The mobile set is the G2 contract: <b>viewing plus basic editing</b> — a value can be selected, edited,
/// scrolled and formatted, the number format and the basic data tools are reachable; the advanced tools
/// (text to columns, remove duplicates, data validation, name manager, paste special, format painter), the
/// cell-border picker, the merge toggle, the thousands separator and the workbook open/download are NOT
/// offered at all below the tablet breakpoint (full editing starts at the tablet). The tablet keeps them,
/// in the row's "More" menu.
/// </para>
/// </summary>
internal static class SpreadsheetToolbarLayout
{
    private const string GroupClipboard = "TmSpreadsheet_Group_Clipboard";
    private const string GroupFont = "TmSpreadsheet_Group_Font";
    private const string GroupAlignment = "TmSpreadsheet_Group_Alignment";
    private const string GroupNumber = "TmSpreadsheet_Group_Number";
    private const string GroupCells = "TmSpreadsheet_Group_Cells";
    private const string GroupInsert = "TmSpreadsheet_Group_Insert";
    private const string GroupData = "TmSpreadsheet_Group_Data";
    private const string GroupView = "TmSpreadsheet_Group_View";
    private const string GroupFile = "TmSpreadsheet_Group_File";
    private const string GroupAdvanced = "TmSpreadsheet_Group_Advanced";

    /// <summary>
    /// The rows the compact ribbon renders for <paramref name="layout"/>. Desktop returns no rows — it
    /// renders the phase-5 tab strip instead.
    /// </summary>
    /// <param name="layout">The RESOLVED container layout (never <see cref="TmLayoutMode.Auto"/>; the host resolves Auto from its own container).</param>
    /// <param name="showFileCommands">Whether the workbook open/download commands are offered (the toolbar owns the file actions only while the host renders no document header).</param>
    internal static IReadOnlyList<SpreadsheetToolbarRow> Rows(TmLayoutMode layout, bool showFileCommands) => layout switch
    {
        TmLayoutMode.Tablet => TabletRows(showFileCommands),
        TmLayoutMode.Mobile => [MobileRow()],
        _ => [],
    };

    /// <summary>
    /// The tablet chrome: the reference's two rows. Row one is the workbook/object command set, row two the
    /// formatting set; both are <c>TmToolbar Overflow=Menu</c> bars, so a narrow tablet container moves the
    /// tail of a row into "More" instead of clipping it.
    /// </summary>
    private static SpreadsheetToolbarRow[] TabletRows(bool showFileCommands)
    {
        var primary = new List<SpreadsheetToolbarCommand>();
        if (showFileCommands)
        {
            // The reference's "Soubor ▾" is ONE control. A core dropdown inside a measuring toolbar item is
            // not cheap (the item must keep a More representation and the trigger must not swallow the
            // measurement), so the file surface is one visible command (open) with the download right behind
            // it in "More" — documented as a deviation in docs/spreadsheet.md.
            primary.Add(Button("file-open", "TmSpreadsheet_Open", GroupFile, icon: "folder-open"));
        }

        primary.AddRange(
        [
            Button("undo", "TmSpreadsheet_Undo", GroupClipboard, icon: "undo"),
            Button("redo", "TmSpreadsheet_Redo", GroupClipboard, icon: "redo"),
            Button("paste", "TmSpreadsheet_Paste", GroupClipboard, icon: "paste"),
            Button("cut", "TmSpreadsheet_Cut", GroupClipboard, icon: "cut"),
            Button("copy", "TmSpreadsheet_Copy", GroupClipboard, icon: "copy"),
            Button("format-cells", "TmSpreadsheet_FormatCells", GroupNumber, icon: "settings"),
            Button("insert-image", "TmSpreadsheet_InsertImage", GroupInsert, icon: "image"),
            Button("insert-link", "TmSpreadsheet_InsertLink", GroupInsert, icon: "link"),

            // Full editing starts at the tablet — these stay reachable, but only through "More".
            Button("format-painter", "TmSpreadsheet_FormatPainter", GroupClipboard, icon: "format-painter", priority: ToolbarButtonPriority.OverflowOnly),
            Button("insert-row", "TmSpreadsheet_InsertRow", GroupCells, icon: "insert-row", priority: ToolbarButtonPriority.OverflowOnly),
            Button("delete-row", "TmSpreadsheet_DeleteRow", GroupCells, icon: "delete-row", priority: ToolbarButtonPriority.OverflowOnly),
            Button("insert-column", "TmSpreadsheet_InsertColumn", GroupCells, icon: "insert-column", priority: ToolbarButtonPriority.OverflowOnly),
            Button("delete-column", "TmSpreadsheet_DeleteColumn", GroupCells, icon: "delete-column", priority: ToolbarButtonPriority.OverflowOnly),
            Button("merge-cells", "TmSpreadsheet_MergeCells", GroupAlignment, icon: "merge-cells", priority: ToolbarButtonPriority.OverflowOnly),
            Button("paste-special", "TmSpreadsheet_Data_PasteSpecial", GroupAdvanced, icon: "clipboard-list", priority: ToolbarButtonPriority.OverflowOnly),
            Button("text-to-columns", "TmSpreadsheet_Data_TextToColumns", GroupAdvanced, icon: "columns", priority: ToolbarButtonPriority.OverflowOnly),
            Button("remove-duplicates", "TmSpreadsheet_Data_RemoveDuplicates", GroupAdvanced, icon: "rows-3", priority: ToolbarButtonPriority.OverflowOnly),
            Button("data-validation", "TmSpreadsheet_Data_DataValidation", GroupAdvanced, icon: "check-square", priority: ToolbarButtonPriority.OverflowOnly),
            Button("circle-invalid", "TmSpreadsheet_Data_CircleInvalidData", GroupAdvanced, icon: "alert-circle", priority: ToolbarButtonPriority.OverflowOnly),
            Button("clear-circles", "TmSpreadsheet_Data_ClearValidationCircles", GroupAdvanced, icon: "eraser", priority: ToolbarButtonPriority.OverflowOnly),
            Button("name-manager", "TmSpreadsheet_Names_Title", GroupAdvanced, icon: "tag", priority: ToolbarButtonPriority.OverflowOnly),
            Button("grid-lines", "TmSpreadsheet_GridLines", GroupView, icon: "grid", priority: ToolbarButtonPriority.OverflowOnly),
            Button("file-download", "TmSpreadsheet_Download", GroupFile, icon: "download", priority: ToolbarButtonPriority.OverflowOnly),
        ]);

        var formatting = new List<SpreadsheetToolbarCommand>
        {
            Select("font-family", "TmSpreadsheet_FontFamily", GroupFont),
            Select("font-size", "TmSpreadsheet_FontSize", GroupFont),
            Toggle("bold", "TmSpreadsheet_Bold", GroupFont),
            Toggle("italic", "TmSpreadsheet_Italic", GroupFont),
            Toggle("underline", "TmSpreadsheet_Underline", GroupFont),
            Toggle("strike", "TmSpreadsheet_Strikethrough", GroupFont, priority: ToolbarButtonPriority.Secondary),
            new SpreadsheetToolbarCommand
            {
                Id = "text-color",
                Kind = SpreadsheetToolbarCommandKind.Color,
                LabelKey = "TmSpreadsheet_TextColor",
                GroupId = "font",
                GroupLabelKey = GroupFont,
            },
            new SpreadsheetToolbarCommand
            {
                Id = "fill-color",
                Kind = SpreadsheetToolbarCommandKind.Color,
                LabelKey = "TmSpreadsheet_BackgroundColor",
                GroupId = "font",
                GroupLabelKey = GroupFont,
            },
            Button("align-left", "TmSpreadsheet_AlignLeft", GroupAlignment, icon: "align-left"),
            Button("align-center", "TmSpreadsheet_AlignCenter", GroupAlignment, icon: "align-center"),
            Button("align-right", "TmSpreadsheet_AlignRight", GroupAlignment, icon: "align-right"),
            Select("number-format", "TmSpreadsheet_NumberFormat", GroupNumber),
            Toggle("percent", "TmSpreadsheet_PercentageFormat", GroupNumber, icon: "percent"),
            Toggle("thousands", "TmSpreadsheet_ThousandsFormat", GroupNumber, priority: ToolbarButtonPriority.Secondary),
            Button("decimals-increase", "TmSpreadsheet_IncreaseDecimals", GroupNumber, icon: "tm-ss-decimal-increase", priority: ToolbarButtonPriority.Secondary),
            Button("decimals-decrease", "TmSpreadsheet_DecreaseDecimals", GroupNumber, icon: "tm-ss-decimal-decrease", priority: ToolbarButtonPriority.Secondary),
            new SpreadsheetToolbarCommand
            {
                Id = "borders",
                Kind = SpreadsheetToolbarCommandKind.Border,
                LabelKey = "TmSpreadsheet_BorderPicker",
                GroupId = "font",
                GroupLabelKey = GroupFont,
                Priority = ToolbarButtonPriority.Secondary,
            },
            Toggle("filter", "TmSpreadsheet_Data_Filter", GroupData, icon: "filter"),
            Button("sort-asc", "TmSpreadsheet_Data_SortAsc", GroupData, icon: "arrow-up", priority: ToolbarButtonPriority.Secondary),
            Button("sort-desc", "TmSpreadsheet_Data_SortDesc", GroupData, icon: "arrow-down", priority: ToolbarButtonPriority.Secondary),
            Button("custom-sort", "TmSpreadsheet_Data_CustomSort", GroupData, icon: "list-ordered", priority: ToolbarButtonPriority.Secondary),
        };

        return
        [
            new SpreadsheetToolbarRow { Id = "primary", LabelKey = "TmSpreadsheet_CompactRow_Primary", Commands = primary },
            new SpreadsheetToolbarRow { Id = "formatting", LabelKey = "TmSpreadsheet_CompactRow_Formatting", Commands = formatting },
        ];
    }

    /// <summary>
    /// The mobile chrome: ONE basic-formatting row (G2). Everything advanced is absent from the model, so the
    /// core toolbar cannot re-offer it in "More" — that is what makes "full editing starts at the tablet" a
    /// hard contract instead of a CSS accident.
    /// </summary>
    private static SpreadsheetToolbarRow MobileRow()
    {
        // G2: viewing plus basic editing. The undo/redo pair leads the row — the compact header is only
        // rendered when the host actually gives it content (Title / HeaderStart / HeaderActions), so the
        // row must stand on its own. The alignment control is ONE entry (a dropdown), like the reference.
        var commands = new List<SpreadsheetToolbarCommand>
        {
            Button("undo", "TmSpreadsheet_Undo", GroupClipboard, icon: "undo"),
            Button("redo", "TmSpreadsheet_Redo", GroupClipboard, icon: "redo"),
            Toggle("bold", "TmSpreadsheet_Bold", GroupFont, icon: "bold"),
            Toggle("italic", "TmSpreadsheet_Italic", GroupFont, icon: "italic"),
            Toggle("underline", "TmSpreadsheet_Underline", GroupFont, icon: "underline"),
            Select("align", "TmSpreadsheet_Align", GroupAlignment),
            Select("number-format", "TmSpreadsheet_NumberFormat", GroupNumber),
            Toggle("percent", "TmSpreadsheet_PercentageFormat", GroupNumber, icon: "percent"),
            new SpreadsheetToolbarCommand
            {
                Id = "text-color",
                Kind = SpreadsheetToolbarCommandKind.Color,
                LabelKey = "TmSpreadsheet_TextColor",
                GroupId = "font",
                GroupLabelKey = GroupFont,
                Priority = ToolbarButtonPriority.Secondary,
            },
            new SpreadsheetToolbarCommand
            {
                Id = "fill-color",
                Kind = SpreadsheetToolbarCommandKind.Color,
                LabelKey = "TmSpreadsheet_BackgroundColor",
                GroupId = "font",
                GroupLabelKey = GroupFont,
                Priority = ToolbarButtonPriority.Secondary,
            },
            Button("align-left", "TmSpreadsheet_AlignLeft", GroupAlignment, icon: "align-left", priority: ToolbarButtonPriority.OverflowOnly),
            Button("align-center", "TmSpreadsheet_AlignCenter", GroupAlignment, icon: "align-center", priority: ToolbarButtonPriority.OverflowOnly),
            Button("align-right", "TmSpreadsheet_AlignRight", GroupAlignment, icon: "align-right", priority: ToolbarButtonPriority.OverflowOnly),
        };

        return new SpreadsheetToolbarRow
        {
            Id = "primary",
            LabelKey = "TmSpreadsheet_CompactRow_Formatting",
            Commands = commands,
        };
    }

    private static SpreadsheetToolbarCommand Button(
        string id,
        string labelKey,
        string groupLabelKey,
        string? icon = null,
        ToolbarButtonPriority priority = ToolbarButtonPriority.Primary) => new()
        {
            Id = id,
            Icon = icon,
            LabelKey = labelKey,
            Priority = priority,
            GroupId = GroupIdOf(groupLabelKey),
            GroupLabelKey = groupLabelKey,
        };

    private static SpreadsheetToolbarCommand Toggle(
        string id,
        string labelKey,
        string groupLabelKey,
        string? icon = null,
        ToolbarButtonPriority priority = ToolbarButtonPriority.Primary) => new()
        {
            Id = id,
            Kind = SpreadsheetToolbarCommandKind.Toggle,
            Icon = icon,
            LabelKey = labelKey,
            Priority = priority,
            GroupId = GroupIdOf(groupLabelKey),
            GroupLabelKey = groupLabelKey,
        };

    private static SpreadsheetToolbarCommand Select(string id, string labelKey, string groupLabelKey) => new()
    {
        Id = id,
        Kind = SpreadsheetToolbarCommandKind.Select,
        LabelKey = labelKey,
        GroupId = GroupIdOf(groupLabelKey),
        GroupLabelKey = groupLabelKey,
    };

    /// <summary>Maps a group's localization key to its frozen <c>data-tm-group</c> id.</summary>
    internal static string GroupIdOf(string? groupLabelKey) => groupLabelKey switch
    {
        GroupClipboard => "clipboard",
        GroupFont => "font",
        GroupAlignment => "alignment",
        GroupNumber => "number",
        GroupCells => "cells",
        GroupInsert => "insert",
        GroupData => "data",
        GroupView => "view",
        GroupFile => "file",
        _ => "custom",
    };
}
