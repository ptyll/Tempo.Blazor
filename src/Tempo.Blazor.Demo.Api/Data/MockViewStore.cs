using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Data;

public class MockViewStore
{
    // Default test tenant/user IDs matching client-side values
    private const string DefaultTenantId = "CurrentTenantId";
    private const string DefaultUserId = "CurrentUserId";

    // Store views by viewContext
    private readonly Dictionary<string, List<DataTableView>> _viewsByContext = new();

    public MockViewStore()
    {
        // Initialize with default contexts
        InitializePersonsViews();
        InitializeEmployeesViews();
        InitializeTeamMembersViews();
    }

    private void InitializePersonsViews()
    {
        _viewsByContext["persons"] =
        [
            new DataTableView
            {
                Id = "v1",
                Name = "All",
                IsDefault = true,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["FirstName", "LastName", "Email", "Department", "Role", "IsActive", "HiredAt"],
                SortField = "LastName",
                SortAscending = true,
                PageSize = 25,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ModifiedAt = DateTime.UtcNow.AddDays(-10)
            },
            new DataTableView
            {
                Id = "v2",
                Name = "Active only",
                IsDefault = false,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["FirstName", "LastName", "Email", "Department", "Role"],
                SortField = "LastName",
                SortAscending = true,
                Filters =
                [
                    new FilterConfig { FieldName = "IsActive", Operator = "equals", Value = "true" }
                ],
                PageSize = 25,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                ModifiedAt = DateTime.UtcNow.AddDays(-5)
            },
            new DataTableView
            {
                Id = "v3",
                Name = "Engineers",
                IsDefault = false,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["FirstName", "LastName", "Email", "Role", "HiredAt"],
                SortField = "HiredAt",
                SortAscending = false,
                Filters =
                [
                    new FilterConfig { FieldName = "Department", Operator = "equals", Value = "Engineering" }
                ],
                PageSize = 50,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                ModifiedAt = DateTime.UtcNow.AddDays(-2)
            },
            new DataTableView
            {
                Id = "v4",
                Name = "My Personal View",
                IsDefault = false,
                Scope = ViewScope.Personal,
                TenantId = DefaultTenantId,
                VisibleColumns = ["FirstName", "LastName", "Email"],
                SortField = "FirstName",
                SortAscending = true,
                PageSize = 10,
                CreatedBy = DefaultUserId,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                ModifiedAt = DateTime.UtcNow.AddDays(-1)
            }
        ];
    }

    private List<DataTableView> GetOrCreateContext(string viewContext)
    {
        if (!_viewsByContext.TryGetValue(viewContext, out var views))
        {
            views = new List<DataTableView>();
            _viewsByContext[viewContext] = views;
        }
        return views;
    }

    private int _nextId = 5;

    public List<DataTableView> GetViews(string viewContext)
        => GetOrCreateContext(viewContext);

    public DataTableView SaveView(string viewContext, DataTableView view)
    {
        var views = GetOrCreateContext(viewContext);
        
        if (string.IsNullOrEmpty(view.Id))
        {
            // Create new view
            view.Id = $"v{_nextId++}";
            view.CreatedAt = DateTime.UtcNow;
            view.ModifiedAt = DateTime.UtcNow;
            views.Add(view);
        }
        else
        {
            var existing = views.FirstOrDefault(v => v.Id == view.Id);
            if (existing is not null)
            {
                // Update existing view
                views.Remove(existing);
                view.ModifiedAt = DateTime.UtcNow;
                views.Add(view);
            }
            else
            {
                // View with this ID doesn't exist - treat as new
                view.CreatedAt = DateTime.UtcNow;
                view.ModifiedAt = DateTime.UtcNow;
                views.Add(view);
            }
        }
        return view;
    }

    public bool DeleteView(string viewContext, string viewId)
    {
        var views = GetOrCreateContext(viewContext);
        var view = views.FirstOrDefault(v => v.Id == viewId);
        if (view is null) return false;
        views.Remove(view);
        return true;
    }

    private void InitializeEmployeesViews()
    {
        _viewsByContext["employees"] =
        [
            new DataTableView
            {
                Id = "v1",
                Name = "All Employees",
                IsDefault = true,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["Name", "Role", "Dept", "Score"],
                SortField = "Name",
                SortAscending = true,
                PageSize = 10,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ModifiedAt = DateTime.UtcNow.AddDays(-10)
            },
            new DataTableView
            {
                Id = "v2",
                Name = "High Performers",
                IsDefault = false,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["Name", "Role", "Score"],
                SortField = "Score",
                SortAscending = false,
                Filters =
                [
                    new FilterConfig { FieldName = "Score", Operator = "greaterthan", Value = "80" }
                ],
                PageSize = 10,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                ModifiedAt = DateTime.UtcNow.AddDays(-5)
            }
        ];
    }

    private void InitializeTeamMembersViews()
    {
        _viewsByContext["team-members"] =
        [
            new DataTableView
            {
                Id = "v1",
                Name = "All Members",
                IsDefault = true,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["Title", "SubTitle", "StatusLabel", "Date"],
                SortField = "Title",
                SortAscending = true,
                PageSize = 10,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                ModifiedAt = DateTime.UtcNow.AddDays(-10)
            },
            new DataTableView
            {
                Id = "v2",
                Name = "Active Only",
                IsDefault = false,
                Scope = ViewScope.Tenant,
                TenantId = DefaultTenantId,
                VisibleColumns = ["Title", "SubTitle", "StatusLabel"],
                SortField = "Title",
                SortAscending = true,
                Filters =
                [
                    new FilterConfig { FieldName = "StatusLabel", Operator = "equals", Value = "Active" }
                ],
                PageSize = 10,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow.AddDays(-5),
                ModifiedAt = DateTime.UtcNow.AddDays(-5)
            }
        ];
    }
}
