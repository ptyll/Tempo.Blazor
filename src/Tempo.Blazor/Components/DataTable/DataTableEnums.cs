namespace Tempo.Blazor.Components.DataTable;

/// <summary>Filter input type for a TmDataTableColumn.</summary>
public enum FilterType { Text, Number, Date, Boolean, Select }

/// <summary>Horizontal alignment of cell content in a TmDataTableColumn.</summary>
public enum ColumnAlign { Left, Center, Right }

/// <summary>Scroll/pagination mode for data components.</summary>
public enum DataTableScrollMode
{
    /// <summary>Classic pagination with page controls.</summary>
    Pagination,

    /// <summary>Virtualized infinite scroll rendering only visible items.</summary>
    Virtualized
}
