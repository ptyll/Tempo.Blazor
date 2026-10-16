namespace Tempo.Blazor.Components.DataTable;

/// <summary>Filter input type for a TmDataTableColumn.</summary>
public enum FilterType
{
    /// <summary>Text-based filtering with string comparison.</summary>
    Text,
    /// <summary>Numeric filtering with number operators.</summary>
    Number,
    /// <summary>Date filtering with date range operators.</summary>
    Date,
    /// <summary>Boolean filtering with true/false options.</summary>
    Boolean,
    /// <summary>Select dropdown filtering from predefined options.</summary>
    Select
}

/// <summary>Horizontal alignment of cell content in a TmDataTableColumn.</summary>
public enum ColumnAlign
{
    /// <summary>Left-aligned content.</summary>
    Left,
    /// <summary>Center-aligned content.</summary>
    Center,
    /// <summary>Right-aligned content.</summary>
    Right
}

/// <summary>
/// Which element renders the "showing X–Y of Z" item range in a <see cref="TmDataTable{TItem}"/> paging footer.
/// </summary>
/// <remarks>
/// The footer has two components that can state the range — the table's own summary and the embedded
/// <see cref="TmPagination"/> — and showing both prints the count twice side by side. This picks one.
/// </remarks>
public enum DataTablePaginationInfoPlacement
{
    /// <summary>
    /// The table's own summary on the left of the footer. Honours
    /// <c>TmDataTable.PaginationInfoTemplate</c> and uses the <c>TmDataTable_ShowingItems</c> resource.
    /// This is the default.
    /// </summary>
    Summary,

    /// <summary>
    /// The label inside the <see cref="TmPagination"/> bar itself, using the <c>TmDataTable_Pagination</c>
    /// resource. Keeps the range attached to the page controls, matching a standalone <c>TmPagination</c>.
    /// </summary>
    Pagination,

    /// <summary>Neither — the footer shows page controls only.</summary>
    None
}

/// <summary>Scroll/pagination mode for data components.</summary>
public enum DataTableScrollMode
{
    /// <summary>Classic pagination with page controls.</summary>
    Pagination,

    /// <summary>Virtualized infinite scroll rendering only visible items.</summary>
    Virtualized
}

/// <summary>
/// High-level preset that controls which chrome elements are rendered by data components
/// such as <see cref="TmDataTable{TItem}"/> and <see cref="TmMultiViewList{TItem}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Full"/> is the default and preserves the existing boolean-parameter behavior:
/// <c>ShowSearch</c>, <c>ShowColumnPicker</c>, <c>ShowViewManager</c>, <c>ShowViewSwitcher</c>,
/// and <c>ShowExternalFilterBuilder</c> control their respective elements.
/// </para>
/// <para>
/// All other modes are higher-level presets that override those booleans for the elements
/// they affect. <c>ShowToolbar</c> still governs whether the toolbar container itself is
/// eligible to render (the container is suppressed when no visible control remains).
/// </para>
/// </remarks>
public enum DataToolbarMode
{
    /// <summary>
    /// Default mode. All toolbar chrome is rendered according to the individual
    /// <c>Show*</c> boolean parameters.
    /// </summary>
    Full,

    /// <summary>
    /// Only the global search input is shown. Column picker, view manager, view switcher,
    /// group picker, and external filter builder are hidden.
    /// </summary>
    SearchOnly,

    /// <summary>
    /// Only action controls are shown: column picker, view manager, view switcher, and
    /// group picker (where applicable). The search input and external filter builder are hidden.
    /// </summary>
    ActionsOnly,

    /// <summary>
    /// No toolbar and no external filter builder are rendered; only the data surface is shown.
    /// </summary>
    ContentOnly
}

/// <summary>
/// How <see cref="TmDataTable{TItem}"/> moves keyboard focus.
/// <see cref="Rows"/> is the default and keeps today's row Tab stops.
/// <see cref="Grid"/> is opt-in ARIA grid roving tabindex. It becomes the default in the next major.
/// </summary>
public enum DataTableKeyboardNavigation
{
    /// <summary>Each data row is a Tab stop. Enter and Space activate the row. Arrow keys do not move cells.</summary>
    Rows,

    /// <summary>One cell has tabindex 0. Arrows, Home, End and PageUp/PageDown move that cell.</summary>
    Grid
}

/// <summary>
/// Surface of a data table or multi-view list.
/// <see cref="Plain"/> is the default and keeps today's markup.
/// <see cref="Card"/> opts into the card radius, shadow and border.
/// </summary>
public enum DataTableAppearance
{
    /// <summary>Today's flat surface. No card class.</summary>
    Plain,

    /// <summary>Card surface. Adds <c>tm-data-table-wrapper--card-surface</c>.</summary>
    Card
}

/// <summary>
/// Where <see cref="Filters.TmFilterBuilder"/> opens the field picker and editor.
/// <see cref="Inline"/> is the default and keeps today's layout, including PickersPage.
/// </summary>
public enum FilterBuilderPresentation
{
    /// <summary>The picker and editor render in the document flow.</summary>
    Inline,

    /// <summary>The picker and editor render as an anchored popover.</summary>
    Popover,

    /// <summary>Same as <see cref="Inline"/> until a host opts into a breakpoint rule.</summary>
    Auto
}

/// <summary>
/// Labels on the <c>TmMultiViewList</c> view switcher.
/// <see cref="IconsOnly"/> is the default and keeps today's buttons.
/// </summary>
public enum ViewSwitcherLabels
{
    /// <summary>Icon only. The accessible name stays on <c>aria-label</c>.</summary>
    IconsOnly,

    /// <summary>Icon plus the localized view name.</summary>
    IconsAndText
}
