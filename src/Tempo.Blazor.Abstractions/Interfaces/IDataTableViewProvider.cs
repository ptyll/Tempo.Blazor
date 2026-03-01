using Tempo.Blazor.Models;

namespace Tempo.Blazor.Interfaces;

/// <summary>
/// Persistence provider for TmDataTable/TmMultiViewList saved views.
/// Views are scoped by ViewContext to support multiple tables on the same or different pages.
/// </summary>
public interface IDataTableViewProvider
{
    /// <summary>
    /// Retrieves all saved views for the specified context and user.
    /// </summary>
    /// <param name="viewContext">Unique identifier for the table/list instance (e.g., "employees", "projects")</param>
    /// <param name="tenantId">Optional tenant ID for multi-tenant scenarios</param>
    /// <param name="userId">Optional user ID for personal views</param>
    /// <param name="ct">Cancellation token</param>
    Task<IEnumerable<DataTableView>> GetViewsAsync(string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Persists a new or updated view.
    /// </summary>
    /// <param name="viewContext">Unique identifier for the table/list instance</param>
    /// <param name="view">View to save</param>
    /// <param name="tenantId">Optional tenant ID</param>
    /// <param name="userId">Optional user ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<DataTableView> SaveViewAsync(string viewContext, DataTableView view, string? tenantId = null, string? userId = null, CancellationToken ct = default);

    /// <summary>
    /// Deletes a saved view by its identifier.
    /// </summary>
    /// <param name="viewContext">Unique identifier for the table/list instance</param>
    /// <param name="viewId">View ID to delete</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteViewAsync(string viewContext, string viewId, CancellationToken ct = default);
    
    /// <summary>
    /// Gets the default view for the specified context and user.
    /// </summary>
    /// <param name="viewContext">Unique identifier for the table/list instance</param>
    /// <param name="tenantId">Optional tenant ID</param>
    /// <param name="userId">Optional user ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<DataTableView?> GetDefaultViewAsync(string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default);
    
    /// <summary>
    /// Sets a view as the default for the specified context and user.
    /// </summary>
    /// <param name="viewContext">Unique identifier for the table/list instance</param>
    /// <param name="viewId">View ID to set as default</param>
    /// <param name="tenantId">Optional tenant ID</param>
    /// <param name="userId">Optional user ID</param>
    /// <param name="ct">Cancellation token</param>
    Task SetDefaultViewAsync(string viewContext, string viewId, string? tenantId = null, string? userId = null, CancellationToken ct = default);
}
