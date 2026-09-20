using System.Text.Json;
using System.Text.Json.Nodes;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Mcp.Wireframe;

/// <summary>Outcome of applying a granular operation batch to a wireframe document.</summary>
public sealed record WireframeOperationResult(
    bool Success,
    IReadOnlyList<string> Errors,
    int Applied,
    IReadOnlyList<string> CreatedIds,
    IReadOnlyList<WireframeLintWarning> Warnings)
{
    public IReadOnlyDictionary<string, IReadOnlyList<string>> RegionMap { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
}

/// <summary>
/// Applies a granular operation batch (add/update/remove elements, connectors and pages, set title
/// and canvas size) to a wireframe document. Operations are applied in order; the first failure
/// aborts the batch (callers apply to a copy and only persist on success).
/// </summary>
public static class WireframeOperationEngine
{
    private const double DefaultLayoutGap = 8;

    private static readonly HashSet<string> LayoutParams = new(StringComparer.Ordinal)
    {
        "op", "pageId", "children", "ids", "gap", "padding", "columns",
        "direction", "align", "wrap", "margin", "x", "y", "w", "h", "type", "role"
    };

    public static WireframeOperationResult Apply(
        WireframeDocument document,
        string operationsJson)
        => Apply(document, operationsJson, registry: null, scope: null);

    public static WireframeOperationResult Apply(
        WireframeDocument document,
        string operationsJson,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope = null)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(operationsJson);
        }
        catch (JsonException ex)
        {
            return new WireframeOperationResult(false, [$"operations: invalid JSON ({ex.Message})."], 0, [], []);
        }

        if (parsed is not JsonArray ops)
        {
            return new WireframeOperationResult(false, ["operations: expected a JSON array of operations."], 0, [], []);
        }

        var created = new List<string>();
        var warnings = new List<WireframeLintWarning>();
        var regionMap = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < ops.Count; i++)
        {
            if (ops[i] is not JsonObject op)
            {
                return Fail(i, "operation must be an object.", created, warnings, regionMap);
            }

            var name = op["op"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return Fail(i, "missing 'op' discriminator.", created, warnings, regionMap);
            }

            var error = name switch
            {
                "setTitle" => SetTitle(document, op),
                "addPage" => AddPage(document, op, created),
                "updatePage" => UpdatePage(document, op),
                "removePage" => RemovePage(document, op),
                "setCanvasSize" => SetCanvasSize(document, op),
                "addElement" => AddElement(document, op, created, warnings, registry, scope),
                "updateElement" => UpdateElement(document, op, warnings, registry, scope),
                "removeElement" => RemoveElement(document, op),
                "scaffold" => Scaffold(document, op, created, warnings, regionMap, registry, scope),
                "stack" or "row" or "grid" => Layout(document, op, name, created, warnings, registry, scope),
                "addConnector" => AddConnector(document, op, created),
                "updateConnector" => UpdateConnector(document, op),
                "removeConnector" => RemoveConnector(document, op),
                _ => $"unknown op '{name}'."
            };

            if (error is not null)
            {
                return Fail(i, error, created, warnings, regionMap);
            }
        }

        return new WireframeOperationResult(true, [], ops.Count, created, warnings)
        {
            RegionMap = FreezeRegionMap(regionMap)
        };

        static WireframeOperationResult Fail(
            int index,
            string message,
            List<string> created,
            List<WireframeLintWarning> warnings,
            Dictionary<string, List<string>> regionMap)
            => new(false, [$"operations[{index}]: {message}"], 0, created, warnings)
            {
                RegionMap = FreezeRegionMap(regionMap)
            };
    }

    // ── Page resolution ──────────────────────────────────────────────────────────

    private static WireframePage? ResolvePage(WireframeDocument document, JsonObject op, out string? error)
    {
        error = null;
        var pageId = TryString(op, "pageId");
        if (string.IsNullOrEmpty(pageId))
        {
            document.EnsureActivePage();
            return document.ActivePage;
        }

        var page = document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null)
        {
            error = $"page '{pageId}' not found.";
        }
        return page;
    }

    // ── Operations ───────────────────────────────────────────────────────────────

    private static string? SetTitle(WireframeDocument document, JsonObject op)
    {
        var title = op["title"]?.GetValue<string>();
        if (title is null)
        {
            return "setTitle requires 'title'.";
        }
        document.Title = title;
        return null;
    }

    private static string? AddPage(WireframeDocument document, JsonObject op, List<string> created)
    {
        var page = new WireframePage();
        if (op["name"]?.GetValue<string>() is { } name) page.Name = name;
        if (TryDouble(op, "width", out var w)) page.Width = w;
        if (TryDouble(op, "height", out var h)) page.Height = h;
        document.Pages.Add(page);
        created.Add(page.Id);
        return null;
    }

    private static string? UpdatePage(WireframeDocument document, JsonObject op)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        if (op["name"]?.GetValue<string>() is { } name) page.Name = name;
        if (TryDouble(op, "width", out var w)) page.Width = w;
        if (TryDouble(op, "height", out var h)) page.Height = h;
        return null;
    }

    private static string? RemovePage(WireframeDocument document, JsonObject op)
    {
        var pageId = op["pageId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pageId)) return "removePage requires 'pageId'.";
        var page = document.Pages.FirstOrDefault(p => p.Id == pageId);
        if (page is null) return $"page '{pageId}' not found.";
        document.Pages.Remove(page);
        return null;
    }

    private static string? SetCanvasSize(WireframeDocument document, JsonObject op)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        if (TryDouble(op, "width", out var w)) page.Width = w;
        if (TryDouble(op, "height", out var h)) page.Height = h;
        return null;
    }

    private static string? AddElement(
        WireframeDocument document,
        JsonObject op,
        List<string> created,
        List<WireframeLintWarning> warnings,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";

        var padding = TryDouble(op, "padding", out var pad) ? pad : 0;
        var errorMessage = CreateElement(
            document,
            page,
            op,
            registry,
            scope,
            page.Width - 2 * padding,
            page.Height - 2 * padding,
            "addElement",
            warnings,
            out var el);
        if (errorMessage is not null) return errorMessage;
        if (el is null) return "addElement requires 'type' or 'role'.";

        errorMessage = ApplyRelativeAnchor(page, el, op);
        if (errorMessage is not null) return errorMessage;

        page.Elements.Add(el);
        created.Add(el.Id);
        return null;
    }

    private static string? Layout(
        WireframeDocument document,
        JsonObject op,
        string kind,
        List<string> created,
        List<WireframeLintWarning> warnings,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope)
    {
        foreach (var (key, _) in op)
        {
            if (!LayoutParams.Contains(key))
            {
                return $"{kind}: unknown layout param '{key}'.";
            }
        }

        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";

        var gap = TryDouble(op, "gap", out var g) ? g : DefaultLayoutGap;
        var padding = TryDouble(op, "padding", out var p) ? p : 0;
        var x = TryDouble(op, "x", out var originX) ? originX : padding;
        var y = TryDouble(op, "y", out var originY) ? originY : padding;
        var layoutWidth = Math.Max(0, (TryDouble(op, "w", out var explicitW) ? explicitW : page.Width) - 2 * padding);
        var layoutHeight = Math.Max(0, (TryDouble(op, "h", out var explicitH) ? explicitH : page.Height) - 2 * padding);
        var columns = ResolveLayoutColumns(kind, op);

        var items = new List<WireframeElement>();
        var newElements = new List<WireframeElement>();
        error = ResolveLayoutIds(page, op, kind, items);
        if (error is not null) return error;

        error = ResolveLayoutChildren(
            document,
            page,
            op,
            kind,
            registry,
            scope,
            layoutWidth,
            layoutHeight,
            items,
            newElements,
            warnings);
        if (error is not null) return error;

        PlaceLayoutItems(kind, items, columns, x, y, gap, layoutWidth, IsTrue(op, "wrap"));

        page.Elements.AddRange(newElements);
        created.AddRange(newElements.Select(e => e.Id));
        return null;
    }

    private static string? UpdateElement(
        WireframeDocument document,
        JsonObject op,
        List<WireframeLintWarning> warnings,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        var id = op["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return "updateElement requires 'id'.";
        var el = page.Elements.FirstOrDefault(e => e.Id == id);
        if (el is null) return $"element '{id}' not found.";

        if (op["type"]?.GetValue<string>() is { Length: > 0 } type) el.Type = type;
        if (TryDouble(op, "x", out var x)) el.X = x;
        if (TryDouble(op, "y", out var y)) el.Y = y;
        if (TryDouble(op, "w", out var w)) el.W = w;
        if (TryDouble(op, "h", out var h)) el.H = h;
        ApplyProps(el, op["props"]);
        var schema = registry?.GetSchema(el.Type, scope, EffectiveTargetPackIds(document, page));
        if (schema is not null)
        {
            warnings.AddRange(WireframePropLinter.LintAndNormalize(el, schema));
        }
        return null;
    }

    private static string? RemoveElement(WireframeDocument document, JsonObject op)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        var id = op["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return "removeElement requires 'id'.";
        var removed = page.Elements.RemoveAll(e => e.Id == id);
        return removed == 0 ? $"element '{id}' not found." : null;
    }

    private static string? Scaffold(
        WireframeDocument document,
        JsonObject op,
        List<string> created,
        List<WireframeLintWarning> warnings,
        Dictionary<string, List<string>> regionMap,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope)
    {
        if (registry is null)
        {
            return "scaffold requires a schema registry.";
        }

        var archetype = TryString(op, "archetype");
        if (string.IsNullOrWhiteSpace(archetype))
        {
            return "scaffold requires 'archetype'.";
        }

        if (!WireframeArchetypes.TrySlots(archetype, out var slots))
        {
            return $"unknown scaffold archetype '{archetype}'.";
        }

        RemoveBlankDefaultPage(document);

        var normalizedName = NormalizeArchetypeName(archetype);
        var desktop = CreateScaffoldPage($"{normalizedName} Desktop", WireframeArchetypes.DesktopWidth, WireframeArchetypes.DesktopHeight);
        var mobile = CreateScaffoldPage($"{normalizedName} Mobile", WireframeArchetypes.MobileWidth, WireframeArchetypes.MobileHeight);
        document.Pages.Add(desktop);
        document.Pages.Add(mobile);
        created.Add(desktop.Id);
        created.Add(mobile.Id);

        if (string.IsNullOrWhiteSpace(document.ActivePageId)
            || document.Pages.All(page => page.Id != document.ActivePageId))
        {
            document.ActivePageId = desktop.Id;
        }

        SeedScaffoldSlots(document, desktop, slots, registry, scope, mobile: false, created, warnings, regionMap);
        SeedScaffoldSlots(document, mobile, slots, registry, scope, mobile: true, created, warnings, regionMap);
        return null;
    }

    private static string? AddConnector(WireframeDocument document, JsonObject op, List<string> created)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        var from = op["fromId"]?.GetValue<string>();
        var to = op["toId"]?.GetValue<string>();
        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to)) return "addConnector requires 'fromId' and 'toId'.";
        var c = new WireframeConnector { FromId = from, ToId = to };
        if (op["label"]?.GetValue<string>() is { } label) c.Label = label;
        page.Connectors.Add(c);
        created.Add(c.Id);
        return null;
    }

    private static string? UpdateConnector(WireframeDocument document, JsonObject op)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        var id = op["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return "updateConnector requires 'id'.";
        var c = page.Connectors.FirstOrDefault(x => x.Id == id);
        if (c is null) return $"connector '{id}' not found.";
        if (op["fromId"]?.GetValue<string>() is { Length: > 0 } from) c.FromId = from;
        if (op["toId"]?.GetValue<string>() is { Length: > 0 } to) c.ToId = to;
        if (op.ContainsKey("label")) c.Label = op["label"]?.GetValue<string>();
        return null;
    }

    private static string? RemoveConnector(WireframeDocument document, JsonObject op)
    {
        var page = ResolvePage(document, op, out var error);
        if (page is null) return error ?? "page not found.";
        var id = op["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return "removeConnector requires 'id'.";
        var removed = page.Connectors.RemoveAll(x => x.Id == id);
        return removed == 0 ? $"connector '{id}' not found." : null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static void ApplyProps(WireframeElement el, JsonNode? props)
    {
        if (props is not JsonObject obj) return;
        foreach (var (key, value) in obj)
        {
            el.Props[key] = value is null
                ? JsonSerializer.SerializeToElement<object?>(null)
                : JsonSerializer.Deserialize<JsonElement>(value.ToJsonString());
        }
    }

    private static WireframePage CreateScaffoldPage(string name, double width, double height)
    {
        var page = new WireframePage
        {
            Name = name,
            Width = width,
            Height = height
        };
        page.EnsureDefaultLayer();
        return page;
    }

    private static void SeedScaffoldSlots(
        WireframeDocument document,
        WireframePage page,
        IReadOnlyList<WireframeArchetypes.SlotSpec> slots,
        WireframeSchemaRegistry registry,
        WireframeComponentScope? scope,
        bool mobile,
        List<string> created,
        List<WireframeLintWarning> warnings,
        Dictionary<string, List<string>> regionMap)
    {
        var cursorY = 24d;
        foreach (var slot in slots)
        {
            var schema = registry.GetSchema(slot.Type, scope, EffectiveTargetPackIds(document, page));
            if (schema is null)
            {
                warnings.Add(new WireframeLintWarning(
                    slot.Region,
                    "scaffold-missing-schema",
                    $"scaffold skipped region '{slot.Region}' because component type '{slot.Type}' is not available."));
                continue;
            }

            var element = new WireframeElement
            {
                Type = schema.Type,
                X = mobile ? 24 : slot.X,
                Y = mobile ? cursorY : slot.Y,
                W = schema.DefaultWidth,
                H = schema.DefaultHeight
            };
            SeedScaffoldDefaultProps(element, schema);

            page.Elements.Add(element);
            created.Add(element.Id);
            AddRegion(regionMap, slot.Region, element.Id);

            if (mobile)
            {
                cursorY += schema.DefaultHeight + 24;
            }
        }
    }

    private static void SeedScaffoldDefaultProps(WireframeElement element, WireframeComponentSchema schema)
    {
        foreach (var prop in schema.Props)
        {
            if (prop.Default is null || element.Props.ContainsKey(prop.Name))
            {
                continue;
            }

            element.Props[prop.Name] = prop.Default is JsonElement json
                ? json.Clone()
                : JsonSerializer.SerializeToElement(prop.Default, prop.Default.GetType());
        }
    }

    private static void AddRegion(
        Dictionary<string, List<string>> regionMap,
        string region,
        string elementId)
    {
        if (!regionMap.TryGetValue(region, out var ids))
        {
            ids = [];
            regionMap[region] = ids;
        }

        ids.Add(elementId);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> FreezeRegionMap(
        Dictionary<string, List<string>> regionMap)
        => regionMap.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)pair.Value.ToArray(),
            StringComparer.Ordinal);

    private static void RemoveBlankDefaultPage(WireframeDocument document)
    {
        if (document.Pages.Count != 1)
        {
            return;
        }

        var page = document.Pages[0];
        if (page.Elements.Count == 0
            && page.Connectors.Count == 0
            && string.Equals(page.Name, "Page 1", StringComparison.Ordinal))
        {
            document.Pages.Clear();
            document.ActivePageId = null;
        }
    }

    private static string NormalizeArchetypeName(string archetype)
    {
        var value = archetype.Trim().ToLowerInvariant();
        return value.Length == 0
            ? "Scaffold"
            : char.ToUpperInvariant(value[0]) + value[1..];
    }

    private static bool TryDouble(JsonObject op, string key, out double value)
    {
        value = 0;
        if (op[key] is JsonValue v && v.TryGetValue<double>(out var d))
        {
            value = d;
            return true;
        }
        return false;
    }

    private static string? TryString(JsonObject op, string key)
        => op.TryGetPropertyValue(key, out var node) && node is not null
            ? node.GetValue<string>()
            : null;

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsTrue(JsonObject op, string key)
        => op[key] is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    private static WireframeLintWarning CreateAmbiguousRoleWarning(
        string elementId,
        string role,
        IReadOnlyList<WireframeComponentSchema> candidates)
    {
        var selected = candidates[0].Type;
        var alternatives = string.Join(", ", candidates.Skip(1).Select(candidate => candidate.Type).Take(5));
        var suffix = candidates.Count > 6 ? ", ..." : string.Empty;
        return new WireframeLintWarning(
            elementId,
            "role-ambiguous",
            $"role '{role}' resolved to '{selected}' but also matched: {alternatives}{suffix}.");
    }

    private static string DescribeTargetPacks(IReadOnlyList<string>? targetPackIds)
        => targetPackIds is null || targetPackIds.Count == 0
            ? "all visible packs"
            : string.Join(", ", targetPackIds);

    private static string? CreateElement(
        WireframeDocument document,
        WireframePage page,
        JsonObject op,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope,
        double fillWidth,
        double fillHeight,
        string operationName,
        List<WireframeLintWarning> warnings,
        out WireframeElement? element)
    {
        element = null;
        var targetPackIds = EffectiveTargetPackIds(document, page);
        var type = NormalizeOptional(TryString(op, "type"));
        var role = NormalizeOptional(TryString(op, "role"));
        WireframeComponentSchema? schema;
        IReadOnlyList<WireframeComponentSchema> roleCandidates = [];

        if (!string.IsNullOrWhiteSpace(role))
        {
            if (registry is null)
            {
                return $"{operationName} requires a schema registry to resolve role '{role}'.";
            }

            roleCandidates = registry.ResolveByRole(role, scope, targetPackIds);
            if (roleCandidates.Count == 0)
            {
                return $"no component maps role '{role}' in target packs ({DescribeTargetPacks(targetPackIds)}); add a schema role mapping to close this gap.";
            }

            schema = roleCandidates[0];
            type = schema.Type;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(type)) return $"{operationName} requires 'type' or 'role'.";

            schema = registry?.GetSchema(type, scope, targetPackIds);
            if (registry is not null && schema is null)
            {
                // Mirror the validation engine: a near-miss type gets a did-you-mean hint so an agent
                // can fix the op without a schema round-trip.
                var suggestion = WireframeCatalog.SuggestType(registry, type, scope, targetPackIds);
                return suggestion is null
                    ? $"component type '{type}' is not available in target packs ({DescribeTargetPacks(targetPackIds)})."
                    : $"component type '{type}' is not available in target packs. Did you mean '{suggestion}'?";
            }
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            return $"{operationName} resolved an empty component type.";
        }

        var el = new WireframeElement { Type = type, Role = role };
        if (TryString(op, "id") is { Length: > 0 } id) el.Id = id;
        if (roleCandidates.Count > 1)
        {
            warnings.Add(CreateAmbiguousRoleWarning(el.Id, role!, roleCandidates));
        }

        if (TryDouble(op, "x", out var x)) el.X = x;
        if (TryDouble(op, "y", out var y)) el.Y = y;
        ApplyProps(el, op["props"]);
        if (schema is not null)
        {
            warnings.AddRange(WireframePropLinter.LintAndNormalize(el, schema));
        }

        var hasExplicitW = TryResolveDimension(op, "w", schema, registry is not null, fillWidth, width: true, out var w);
        var hasExplicitH = TryResolveDimension(op, "h", schema, registry is not null, fillHeight, width: false, out var h);
        if (hasExplicitW) el.W = w;
        if (hasExplicitH) el.H = h;
        if (schema is not null)
        {
            SeedMissingSize(el, schema, hasExplicitW, hasExplicitH);
        }

        element = el;
        return null;
    }

    private static bool TryResolveDimension(
        JsonObject op,
        string key,
        WireframeComponentSchema? schema,
        bool sentinelsEnabled,
        double fillValue,
        bool width,
        out double value)
    {
        if (TryDouble(op, key, out value))
        {
            return true;
        }

        value = 0;
        if (!sentinelsEnabled || op[key] is not JsonValue v || !v.TryGetValue<string>(out var text))
        {
            return false;
        }

        if (string.Equals(text, "fill", StringComparison.OrdinalIgnoreCase))
        {
            value = Math.Max(0, fillValue);
            return true;
        }

        if (string.Equals(text, "auto", StringComparison.OrdinalIgnoreCase) && schema is not null)
        {
            value = width ? schema.DefaultWidth : schema.DefaultHeight;
            return true;
        }

        return false;
    }

    private static string? ApplyRelativeAnchor(WireframePage page, WireframeElement element, JsonObject op)
    {
        var margin = TryDouble(op, "margin", out var m) ? m : DefaultLayoutGap;

        if (TryString(op, "below") is { Length: > 0 } belowId)
        {
            var reference = page.Elements.FirstOrDefault(e => e.Id == belowId);
            if (reference is null) return $"reference element '{belowId}' not found.";
            element.X = reference.X;
            element.Y = reference.Y + reference.H + margin;
        }

        if (TryString(op, "rightOf") is { Length: > 0 } rightOfId)
        {
            var reference = page.Elements.FirstOrDefault(e => e.Id == rightOfId);
            if (reference is null) return $"reference element '{rightOfId}' not found.";
            element.X = reference.X + reference.W + margin;
            element.Y = reference.Y;
        }

        return null;
    }

    private static int ResolveLayoutColumns(string kind, JsonObject op)
    {
        if (kind == "stack")
        {
            return 1;
        }

        if (kind == "row")
        {
            return int.MaxValue;
        }

        return TryDouble(op, "columns", out var c)
            ? Math.Max(1, (int)c)
            : 1;
    }

    private static string? ResolveLayoutIds(
        WireframePage page,
        JsonObject op,
        string kind,
        List<WireframeElement> items)
    {
        if (!op.TryGetPropertyValue("ids", out var idsNode) || idsNode is null)
        {
            return null;
        }

        if (idsNode is not JsonArray ids)
        {
            return $"{kind}: 'ids' must be an array.";
        }

        foreach (var idNode in ids)
        {
            if (idNode is not JsonValue idValue
                || !idValue.TryGetValue<string>(out var id)
                || string.IsNullOrWhiteSpace(id))
            {
                return $"{kind}: 'ids' entries must be non-empty strings.";
            }

            var element = page.Elements.FirstOrDefault(e => e.Id == id);
            if (element is null)
            {
                return $"{kind}: element '{id}' not found.";
            }

            items.Add(element);
        }

        return null;
    }

    private static string? ResolveLayoutChildren(
        WireframeDocument document,
        WireframePage page,
        JsonObject op,
        string kind,
        WireframeSchemaRegistry? registry,
        WireframeComponentScope? scope,
        double fillWidth,
        double fillHeight,
        List<WireframeElement> items,
        List<WireframeElement> newElements,
        List<WireframeLintWarning> warnings)
    {
        if (!op.TryGetPropertyValue("children", out var childrenNode) || childrenNode is null)
        {
            return null;
        }

        if (childrenNode is not JsonArray children)
        {
            return $"{kind}: 'children' must be an array.";
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is not JsonObject child)
            {
                return $"{kind}: children[{i}] must be an object.";
            }

            var error = CreateElement(
                document,
                page,
                child,
                registry,
                scope,
                fillWidth,
                fillHeight,
                $"{kind} child",
                warnings,
                out var element);
            if (error is not null) return error;
            if (element is null) return $"{kind}: children[{i}] must define a type or role.";

            items.Add(element);
            newElements.Add(element);
        }

        return null;
    }

    private static void PlaceLayoutItems(
        string kind,
        IReadOnlyList<WireframeElement> items,
        int columns,
        double x,
        double y,
        double gap,
        double layoutWidth,
        bool wrap)
    {
        if (items.Count == 0)
        {
            return;
        }

        if (kind == "row")
        {
            PlaceRow(items, x, y, gap, layoutWidth, wrap);
            return;
        }

        PlaceGrid(items, Math.Min(Math.Max(1, columns), items.Count), x, y, gap);
    }

    private static void PlaceRow(
        IReadOnlyList<WireframeElement> items,
        double x,
        double y,
        double gap,
        double layoutWidth,
        bool wrap)
    {
        var cursorX = x;
        var cursorY = y;
        var rowHeight = 0d;
        var maxX = x + Math.Max(0, layoutWidth);

        foreach (var item in items)
        {
            if (wrap && cursorX > x && cursorX + item.W > maxX)
            {
                cursorX = x;
                cursorY += rowHeight + gap;
                rowHeight = 0;
            }

            item.X = cursorX;
            item.Y = cursorY;
            cursorX += item.W + gap;
            rowHeight = Math.Max(rowHeight, item.H);
        }
    }

    private static void PlaceGrid(
        IReadOnlyList<WireframeElement> items,
        int columns,
        double x,
        double y,
        double gap)
    {
        var rows = (int)Math.Ceiling(items.Count / (double)columns);
        var columnWidths = new double[columns];
        var rowHeights = new double[rows];

        for (var i = 0; i < items.Count; i++)
        {
            var column = i % columns;
            var row = i / columns;
            columnWidths[column] = Math.Max(columnWidths[column], items[i].W);
            rowHeights[row] = Math.Max(rowHeights[row], items[i].H);
        }

        var columnX = new double[columns];
        var rowY = new double[rows];
        columnX[0] = x;
        rowY[0] = y;

        for (var column = 1; column < columns; column++)
        {
            columnX[column] = columnX[column - 1] + columnWidths[column - 1] + gap;
        }

        for (var row = 1; row < rows; row++)
        {
            rowY[row] = rowY[row - 1] + rowHeights[row - 1] + gap;
        }

        for (var i = 0; i < items.Count; i++)
        {
            items[i].X = columnX[i % columns];
            items[i].Y = rowY[i / columns];
        }
    }

    private static IReadOnlyList<string>? EffectiveTargetPackIds(WireframeDocument document, WireframePage page)
        => page.TargetPackIds ?? document.TargetPackIds;

    private static void SeedMissingSize(
        WireframeElement element,
        WireframeComponentSchema schema,
        bool hasExplicitW,
        bool hasExplicitH)
    {
        if (hasExplicitW && hasExplicitH)
            return;

        var (seedW, seedH) = ResolveSeedSize(element, schema);
        if (!hasExplicitW) element.W = seedW;
        if (!hasExplicitH) element.H = seedH;
    }

    private static (double W, double H) ResolveSeedSize(WireframeElement element, WireframeComponentSchema schema)
    {
        var sizeValue = ResolveSizePropValue(element, schema);
        if (!string.IsNullOrWhiteSpace(sizeValue)
            && schema.SizePresets is not null
            && schema.SizePresets.TryGetValue(sizeValue, out var preset))
        {
            return preset;
        }

        return (schema.DefaultWidth, schema.DefaultHeight);
    }

    private static string? ResolveSizePropValue(WireframeElement element, WireframeComponentSchema schema)
    {
        if (element.Props.TryGetValue("size", out var sizeProp))
        {
            return sizeProp.ValueKind == JsonValueKind.String
                ? sizeProp.GetString()
                : sizeProp.ToString();
        }

        var sizeDef = schema.Props.FirstOrDefault(p =>
            string.Equals(p.Name, "size", StringComparison.Ordinal));
        return sizeDef?.Default switch
        {
            null => null,
            string value => value,
            JsonElement json when json.ValueKind == JsonValueKind.String => json.GetString(),
            JsonElement json => json.ToString(),
            _ => sizeDef.Default.ToString()
        };
    }
}
