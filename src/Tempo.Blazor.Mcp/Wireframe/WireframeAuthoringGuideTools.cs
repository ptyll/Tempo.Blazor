using System.ComponentModel;
using ModelContextProtocol.Server;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Mcp.Wireframe;

/// <summary>LLM-facing authoring guide for wireframe canvas conventions and component props.</summary>
[McpServerToolType]
public static class WireframeAuthoringGuideTools
{
    private static readonly Lazy<UiRoleVocabulary> BuiltInVocabulary =
        new(() => new UiRoleVocabulary([new BuiltInUiRoleVocabularySource()]));

    [McpServerTool(Name = "wireframe_get_authoring_guide")]
    [Description("Return the wireframe authoring guide for the current component catalog: canvas conventions, sizing and variant guidance, supported layout ops and prop vocabulary. Use compact/filter parameters to keep responses small. Pass scopeAppId to include app-scoped custom components alongside Tempo built-ins.")]
    public static string GetAuthoringGuideScoped(
        WireframeSchemaRegistry registry,
        [Description("Optional app id. When set, include Tempo baseline components plus custom components scoped to this app.")] string? scopeAppId = null,
        [Description("When true, omit propVocabulary and return compact component entries instead. Default false preserves the full guide contract.")] bool compact = false,
        [Description("Optional category filter (e.g. 'Buttons', 'Inputs', 'Layout').")] string? category = null,
        [Description("Optional component type filter. Accepts concrete type ids and app-scoped local type names when scopeAppId is set.")] IReadOnlyList<string>? types = null,
        [Description("Optional role filter. Accepts role slugs or known role synonyms, e.g. 'search-input' or 'TmSearchBox'.")] IReadOnlyList<string>? roles = null,
        [Description("Pagination offset after filters are applied.")] int skip = 0,
        [Description("Optional maximum number of filtered components to include. Omit to return all filtered components.")] int? take = null,
        [Description("Optional target pack ids from the active document. Built-in Tempo components stay visible; app-scoped components require their app:{id} pack.")] IReadOnlyList<string>? targetPackIds = null)
    {
        var scope = WireframeComponentScope.FromAppId(scopeAppId);
        var vocabulary = BuiltInVocabulary.Value;
        var available = registry.GetAll(scope, targetPackIds).ToList();
        var filtered = FilterComponents(registry, available, scope, targetPackIds, vocabulary, category, types, roles);
        var pageQuery = filtered.Skip(Math.Max(0, skip));
        if (take is not null)
        {
            pageQuery = pageQuery.Take(Math.Clamp(take.Value, 1, 1000));
        }

        var page = pageQuery.ToList();

        return McpToolResults.Success(new
        {
            canvas = new
            {
                desktopWidth = WireframeArchetypes.DesktopWidth,
                mobileWidth = WireframeArchetypes.MobileWidth,
                navbarHeight = WireframeArchetypes.NavbarHeight,
                sectionSpacing = WireframeArchetypes.SectionSpacing
            },
            sizing = new
            {
                defaultSize = "Omit w/h to use the resolved component schema defaultWidth/defaultHeight.",
                auto = "Use w or h = 'auto' in addElement/layout children to resolve the component default size.",
                fill = "Use w or h = 'fill' in addElement/layout children to consume the page or layout span after padding.",
                presets = "When a component exposes a size prop with sizePresets, that preset seeds missing w/h unless explicit dimensions are supplied."
            },
            variants = new
            {
                enums = "Enum props are case-normalized when possible; invalid enum values are returned as warnings.",
                strict = "strict=true rejects prop/enum warning batches and scaffold missing-schema warnings before saving."
            },
            layoutOps = new
            {
                operations = new[] { "stack", "row", "grid" },
                sentinels = new[] { "auto", "fill" },
                anchors = new[] { "below", "rightOf" },
                commonParams = new[] { "pageId", "children", "ids", "gap", "padding", "columns", "wrap", "margin", "x", "y", "w", "h", "role", "name" }
            },
            themes = new
            {
                stencilThemes = new[] { "indigo", "indigo-dark", "dark" },
                documentField = "WireframeDocument.targetTheme (and an optional per-page targetTheme) names the stencil theme the document is drawn with: the built-in pack ships 'indigo', 'indigo-dark' and 'dark'; 'light'/'default' (or no value) are the default tokens. An unknown name draws the default tokens (the editor logs one warning).",
                resolutionOrder = "The editor's StencilTheme parameter wins over the page targetTheme, then the document targetTheme, then the default tokens. Set targetTheme when the design is meant for an indigo or dark product; leave it empty otherwise.",
                scope = "Only stencil colours change (primary / info / surfaces / text); the page stays white paper, in the browser SVG export and in the server PNG / PDF export alike."
            },
            elementFields = new
            {
                name = "Optional human-readable element name, e.g. 'Login button'. Accepted by addElement, layout children and updateElement (null or empty clears it) and by whole-document payloads; shown in the inspector, used as the element's accessible name/tooltip and listed in the implementation brief."
            },
            archetypes = WireframeArchetypes.Names,
            totalCount = filtered.Count,
            categories = available
                .Select(s => s.Category)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order()
                .ToList(),
            roleVocabulary = vocabulary.GetAll().Select(RoleSummary).ToList(),
            components = compact ? page.Select(WireframeCatalog.Compact).ToList() : null,
            propVocabulary = compact ? null : page.Select(WireframeCatalog.Full).ToList()
        });
    }

    public static string GetAuthoringGuide(WireframeSchemaRegistry registry)
        => GetAuthoringGuideScoped(registry);

    private static List<WireframeComponentSchema> FilterComponents(
        WireframeSchemaRegistry registry,
        IReadOnlyList<WireframeComponentSchema> available,
        WireframeComponentScope? scope,
        IReadOnlyList<string>? targetPackIds,
        UiRoleVocabulary vocabulary,
        string? category,
        IReadOnlyList<string>? types,
        IReadOnlyList<string>? roles)
    {
        IEnumerable<WireframeComponentSchema> query = available;

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(s => string.Equals(s.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        var typeFilter = BuildTypeFilter(registry, scope, targetPackIds, types);
        if (typeFilter is not null)
        {
            query = query.Where(s =>
                typeFilter.Contains(s.Type)
                || typeFilter.Contains(s.LocalType ?? WireframeComponentScope.GetLocalType(s.Type)));
        }

        var roleFilter = BuildRoleFilter(vocabulary, roles);
        if (roleFilter is not null)
        {
            query = query.Where(s => s.Roles?.Any(roleFilter.Contains) == true);
        }

        return query.ToList();
    }

    private static HashSet<string>? BuildTypeFilter(
        WireframeSchemaRegistry registry,
        WireframeComponentScope? scope,
        IReadOnlyList<string>? targetPackIds,
        IReadOnlyList<string>? types)
    {
        if (types is null || types.Count == 0)
            return null;

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type))
                continue;

            var normalized = type.Trim();
            var schema = registry.GetSchema(normalized, scope, targetPackIds);
            result.Add(schema?.Type ?? normalized);
        }

        return result.Count == 0 ? null : result;
    }

    private static HashSet<string>? BuildRoleFilter(
        UiRoleVocabulary vocabulary,
        IReadOnlyList<string>? roles)
    {
        if (roles is null || roles.Count == 0)
            return null;

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            if (string.IsNullOrWhiteSpace(role))
                continue;

            var normalized = role.Trim();
            result.Add(vocabulary.Find(normalized)?.Slug ?? normalized);
        }

        return result.Count == 0 ? null : result;
    }

    private static object RoleSummary(UiRoleDefinition role) => new
    {
        slug = role.Slug,
        displayName = role.DisplayName,
        definition = role.Definition,
        synonyms = role.Synonyms
    };
}
