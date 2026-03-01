using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>
/// API endpoints for user management and mentions.
/// </summary>
public static class UserEndpoints
{
    /// <summary>
    /// Maps user-related endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users")
            .WithTags("Users");

        // GET /api/users - Get all users (paginated)
        group.MapGet("/", (MockUserStore store, int? page, int? pageSize) =>
        {
            var pageNum = page ?? 1;
            var size = pageSize ?? 20;
            var users = store.Users
                .Skip((pageNum - 1) * size)
                .Take(size)
                .ToList();
            
            return Results.Ok(new
            {
                Items = users,
                Page = pageNum,
                PageSize = size,
                TotalCount = store.Users.Count
            });
        })
        .WithName("GetUsers");

        // GET /api/users/search?q={query} - Search users for mentions
        group.MapGet("/search", (MockUserStore store, string? q, int? limit) =>
        {
            var results = store.SearchUsers(q ?? string.Empty, limit ?? 10);
            return Results.Ok(results);
        })
        .WithName("SearchUsers");

        // GET /api/users/{id} - Get user by ID
        group.MapGet("/{id}", (MockUserStore store, string id) =>
        {
            var user = store.GetById(id);
            return user is not null ? Results.Ok(user) : Results.NotFound();
        })
        .WithName("GetUserById");

        // GET /api/users/me - Get current user (mock)
        group.MapGet("/me", () =>
        {
            // Mock current user - in real app this would come from claims
            return Results.Ok(new
            {
                Id = "0",
                UserName = "current.user",
                DisplayName = "Aktuální Uživatel",
                AvatarUrl = "https://api.dicebear.com/7.x/avataaars/svg?seed=current",
                Email = "current@company.cz",
                Department = "Vývoj"
            });
        })
        .WithName("GetCurrentUser");

        return app;
    }
}
