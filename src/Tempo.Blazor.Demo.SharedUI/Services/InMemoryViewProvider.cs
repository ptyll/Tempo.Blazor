using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// A view provider that keeps its views in memory. The redesign demo uses it so the saved-views
/// controls (active view name, set as default) work without the Demo API.
/// </summary>
public sealed class InMemoryViewProvider : IDataTableViewProvider
{
    private readonly List<DataTableView> _views;

    /// <summary>Creates a provider seeded with the given views.</summary>
    public InMemoryViewProvider(IEnumerable<DataTableView> views)
    {
        _views = views.ToList();
    }

    /// <inheritdoc />
    public Task<IEnumerable<DataTableView>> GetViewsAsync(
        string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default)
        => Task.FromResult<IEnumerable<DataTableView>>(_views.ToList());

    /// <inheritdoc />
    public Task<DataTableView> SaveViewAsync(
        string viewContext, DataTableView view, string? tenantId = null, string? userId = null, CancellationToken ct = default)
    {
        view.Id ??= Guid.NewGuid().ToString("N");
        var index = _views.FindIndex(existing => existing.Id == view.Id);
        if (index >= 0)
            _views[index] = view;
        else
            _views.Add(view);
        return Task.FromResult(view);
    }

    /// <inheritdoc />
    public Task DeleteViewAsync(string viewContext, string viewId, CancellationToken ct = default)
    {
        _views.RemoveAll(view => view.Id == viewId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<DataTableView?> GetDefaultViewAsync(
        string viewContext, string? tenantId = null, string? userId = null, CancellationToken ct = default)
        => Task.FromResult(_views.FirstOrDefault(view => view.IsDefault));

    /// <inheritdoc />
    public Task SetDefaultViewAsync(
        string viewContext, string viewId, string? tenantId = null, string? userId = null, CancellationToken ct = default)
    {
        foreach (var view in _views)
            view.IsDefault = view.Id == viewId;
        return Task.CompletedTask;
    }
}
