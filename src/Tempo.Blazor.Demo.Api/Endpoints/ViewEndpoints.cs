using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.Api.Endpoints;

public static class ViewEndpoints
{
    public static void MapViewEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/views/{viewContext}").WithTags("Views");

        // Get all views (optionally filtered by tenant/user)
        group.MapGet("/", (string viewContext, MockViewStore store, string? tenantId, string? userId) =>
        {
            var views = store.GetViews(viewContext).AsEnumerable();
            
            // Filter by tenant (tenant views or personal views for this user)
            if (!string.IsNullOrEmpty(tenantId))
            {
                views = views.Where(v => 
                    (v.Scope == ViewScope.Tenant && v.TenantId == tenantId) ||
                    (v.Scope == ViewScope.Personal && v.CreatedBy == userId));
            }
            else if (!string.IsNullOrEmpty(userId))
            {
                // If no tenant, show only personal views for this user + public tenant views
                views = views.Where(v => 
                    v.Scope == ViewScope.Tenant || 
                    (v.Scope == ViewScope.Personal && v.CreatedBy == userId));
            }
            
            return Results.Ok(views);
        });

        // Get default view for tenant/user
        group.MapGet("/default", (string viewContext, MockViewStore store, string? tenantId, string? userId) =>
        {
            var views = store.GetViews(viewContext).AsEnumerable();
            
            // Filter by tenant/user context
            if (!string.IsNullOrEmpty(tenantId))
            {
                views = views.Where(v => 
                    (v.Scope == ViewScope.Tenant && v.TenantId == tenantId) ||
                    (v.Scope == ViewScope.Personal && v.CreatedBy == userId));
            }
            else if (!string.IsNullOrEmpty(userId))
            {
                views = views.Where(v => 
                    v.Scope == ViewScope.Tenant || 
                    (v.Scope == ViewScope.Personal && v.CreatedBy == userId));
            }
            
            var defaultView = views.FirstOrDefault(v => v.IsDefault) ?? views.FirstOrDefault();
            
            return defaultView is null ? Results.NotFound() : Results.Ok(defaultView);
        });

        // Set a view as default
        group.MapPost("/{viewId}/default", (string viewContext, string viewId, MockViewStore store, string? tenantId, string? userId) =>
        {
            var views = store.GetViews(viewContext);
            var view = views.FirstOrDefault(v => v.Id == viewId);
            if (view is null) return Results.NotFound();
            
            // Clear existing default for this scope/tenant/user
            foreach (var v in views.Where(v => 
                v.Scope == view.Scope && 
                v.IsDefault &&
                (view.Scope == ViewScope.Tenant 
                    ? v.TenantId == view.TenantId 
                    : v.CreatedBy == view.CreatedBy)))
            {
                v.IsDefault = false;
            }
            
            view.IsDefault = true;
            return Results.Ok(view);
        });

        // Create new view
        group.MapPost("/", (string viewContext, DataTableView view, MockViewStore store, string? tenantId, string? userId, ILogger<Program> logger) =>
        {
            logger.LogInformation("POST /api/views/{ViewContext} - Name: {Name}, Id: {Id}, Scope: {Scope}", viewContext, view.Name, view.Id, view.Scope);
            
            // Set tenant/user context if provided
            if (!string.IsNullOrEmpty(tenantId))
                view.TenantId = tenantId;
            if (!string.IsNullOrEmpty(userId))
                view.CreatedBy = userId;
                
            var saved = store.SaveView(viewContext, view);
            logger.LogInformation("View saved: {Id} in context {ViewContext}, Total views: {Count}", saved.Id, viewContext, store.GetViews(viewContext).Count);
            return Results.Created($"/api/views/{viewContext}/{saved.Id}", saved);
        });

        // Update view
        group.MapPut("/{viewId}", (string viewContext, string viewId, DataTableView view, MockViewStore store, string? tenantId, string? userId, ILogger<Program> logger) =>
        {
            logger.LogInformation("PUT /api/views/{ViewContext}/{ViewId} - Name: {Name}, Scope: {Scope}", viewContext, viewId, view.Name, view.Scope);
            
            view.Id = viewId;
            
            // Update tenant/user context if provided
            if (!string.IsNullOrEmpty(tenantId))
                view.TenantId = tenantId;
            if (!string.IsNullOrEmpty(userId))
                view.CreatedBy = userId;
                
            var saved = store.SaveView(viewContext, view);
            logger.LogInformation("View saved: {Id} in context {ViewContext}, Total views: {Count}", saved.Id, viewContext, store.GetViews(viewContext).Count);
            return Results.Ok(saved);
        });

        // Delete view
        group.MapDelete("/{viewId}", (string viewContext, string viewId, MockViewStore store) =>
            store.DeleteView(viewContext, viewId) ? Results.NoContent() : Results.NotFound());
    }
}
