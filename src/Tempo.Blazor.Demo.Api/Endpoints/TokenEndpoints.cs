using Tempo.Blazor.Demo.Api.Data;
using Tempo.Blazor.Demo.Shared;

namespace Tempo.Blazor.Demo.Api.Endpoints;

/// <summary>
/// API endpoints for template token search.
/// </summary>
public static class TokenEndpoints
{
    /// <summary>
    /// Maps token-related endpoints.
    /// </summary>
    public static IEndpointRouteBuilder MapTokenEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tokens")
            .WithTags("Tokens");

        // GET /api/tokens/search?q={query} - Search tokens
        group.MapGet("/search", (MockTokenStore store, string? q, int? limit) =>
        {
            var results = store.SearchTokens(q ?? string.Empty, limit ?? 20);
            return Results.Ok(results);
        })
        .WithName("SearchTokens");

        // GET /api/tokens - Get all tokens
        group.MapGet("/", (MockTokenStore store) =>
        {
            return Results.Ok(store.Tokens);
        })
        .WithName("GetAllTokens");

        // POST /api/tokens - Create a new token
        group.MapPost("/", (MockTokenStore store, TokenDto token) =>
        {
            var created = store.AddToken(token.Key, token.DisplayName, token.Description, token.Category);
            return Results.Created($"/api/tokens/{created.Key}", created);
        })
        .WithName("CreateToken");

        return app;
    }
}
