using System.ComponentModel;
using ModelContextProtocol.Server;
using Tempo.Blazor.Components.Wireframe;

namespace Tempo.Blazor.Mcp.Wireframe;

/// <summary>
/// MCP tools that let an LLM discover which Tempo.Blazor components it can place in a wireframe
/// and how to configure them.
/// </summary>
[McpServerToolType]
public static class WireframeComponentCatalogTools
{
    [McpServerTool(Name = "wireframe_list_components")]
    [Description("List the wireframe components available to place on a design. Start with compact=true to keep the response small, then call wireframe_get_component_schema for the full property contract of a chosen type. Optionally filter by category and application scope. Results are paged: when hasMore is true, call again with skip=nextSkip. With scopeAppId/targetPackIds, app-scoped components are listed first.")]
    public static string ListComponentsScoped(
        WireframeSchemaRegistry registry,
        [Description("When true, return only type/category/displayName. Recommended first call.")] bool compact = true,
        [Description("Optional category filter (e.g. 'Buttons', 'Inputs', 'Layout').")] string? category = null,
        [Description("Pagination offset.")] int skip = 0,
        [Description("Maximum number of components to return (default 200, clamped 1..1000). Results are paged: when hasMore is true, call again with skip=nextSkip.")] int take = 200,
        [Description("Optional app id. When set, return Tempo baseline components plus custom components scoped to this app only. App-scoped components are listed first.")] string? scopeAppId = null,
        [Description("Optional target pack ids from the active document. Built-in Tempo components stay visible; app-scoped components require their app:{id} pack. App-scoped components are listed first.")] IReadOnlyList<string>? targetPackIds = null)
    {
        var scope = WireframeComponentScope.FromAppId(scopeAppId);
        var available = registry.GetAll(scope, targetPackIds).ToList();
        var all = string.IsNullOrWhiteSpace(category)
            ? available
            : available.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase)).ToList();

        var scoped = !string.IsNullOrWhiteSpace(scopeAppId) || targetPackIds is { Count: > 0 };
        var ordered = scoped
            ? all.OrderBy(s => s.ScopeAppId is null ? 1 : 0).ToList()
            : all;
        var effectiveSkip = Math.Max(0, skip);
        var effectiveTake = Math.Clamp(take, 1, 1000);
        var page = ordered.Skip(effectiveSkip).Take(effectiveTake);
        var items = page.Select(s => compact ? WireframeCatalog.Compact(s) : WireframeCatalog.Full(s)).ToList();
        var returnedCount = items.Count;
        var hasMore = effectiveSkip + returnedCount < ordered.Count;
        int? nextSkip = hasMore ? effectiveSkip + returnedCount : null;

        return McpToolResults.Success(new
        {
            totalCount = ordered.Count,
            returnedCount,
            hasMore,
            nextSkip,
            categories = available
                .Select(s => s.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToList(),
            items
        });
    }

    public static string ListComponents(
        WireframeSchemaRegistry registry,
        bool compact = true,
        string? category = null,
        int skip = 0,
        int take = 200)
        => ListComponentsScoped(registry, compact, category, skip, take, scopeAppId: null);

    [McpServerTool(Name = "wireframe_get_component_schema")]
    [Description("Get the full property contract for one wireframe component type (dimensions and every prop with its type, default and allowed values). Returns not_found with a suggestion when the type is misspelled.")]
    public static string GetComponentSchemaScoped(
        WireframeSchemaRegistry registry,
        [Description("The component type id, e.g. 'TmButton' or 'app:{id}:MyCard'.")] string type,
        [Description("Optional app id used to resolve local custom type names.")] string? scopeAppId = null,
        [Description("Optional target pack ids from the active document. Built-in Tempo components stay visible; app-scoped components require their app:{id} pack.")] IReadOnlyList<string>? targetPackIds = null)
    {
        var scope = WireframeComponentScope.FromAppId(scopeAppId);
        var schema = registry.GetSchema(type, scope, targetPackIds);
        if (schema is null)
        {
            var suggestion = WireframeCatalog.SuggestType(registry, type, scope, targetPackIds);
            var message = suggestion is null
                ? $"Unknown component type '{type}'."
                : $"Unknown component type '{type}'. Did you mean '{suggestion}'?";
            return McpToolResults.Failure(McpToolResults.NotFound, message);
        }

        return McpToolResults.Success(new { component = WireframeCatalog.Full(schema) });
    }

    public static string GetComponentSchema(WireframeSchemaRegistry registry, string type)
        => GetComponentSchemaScoped(registry, type, scopeAppId: null, targetPackIds: null);
}
