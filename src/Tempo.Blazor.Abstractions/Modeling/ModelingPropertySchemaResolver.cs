using System.Globalization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// Phase 5 review (A3): the ONE bridge between the ASYNC host schema provider and the SYNCHRONOUS
/// <see cref="ModelingOperationContext.ResolvePropertySchema"/> the operation engine consumes
/// (§5.5/§6.4). A caller (a host writer, the MCP tool) preloads every schema a batch may need — the
/// pairs the model itself carries plus the pairs the batch introduces, see
/// <see cref="PropertySchemaPairs"/> — and hands the resulting dictionary lookup to the engine.
/// <para>
/// Failures are contained: a provider that throws for one pair degrades that pair to "no schema"
/// (nothing is validated for it) instead of breaking the save/batch, exactly like the editor's own
/// degradation. Nothing here pins a version: the caller's own culture/viewpoint arrive in each
/// request the engine builds, and the preload asks with what it was given, so a host that pinned a
/// schema version in its provider keeps answering with the pinned one.
/// </para>
/// </summary>
public static class ModelingPropertySchemaResolver
{
    /// <summary>
    /// The <c>(notation, semanticType)</c> pairs a batch can judge: every element's pair, every
    /// relationship's pair (the relationship TYPE is the semantic type) and — when a batch is
    /// supplied — the pairs carried by the batch's operations themselves (a retype, an add with its
    /// own notation). Distinct and ordered, so the preload asks each provider once per pair.
    /// </summary>
    /// <param name="model">The model the batch applies to.</param>
    /// <param name="batch">The batch about to be applied, or null for "the model only".</param>
    public static IReadOnlyList<(string Notation, string SemanticType)> PropertySchemaPairs(
        ModelingModelDto model,
        ModelingOperationBatch? batch = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var pairs = new List<(string Notation, string SemanticType)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string? notation, string? semanticType)
        {
            if (string.IsNullOrWhiteSpace(semanticType))
            {
                return;
            }

            var pair = (notation ?? string.Empty, semanticType);
            if (seen.Add($"{pair.Item1}\u001f{pair.Item2}"))
            {
                pairs.Add(pair);
            }
        }

        foreach (var element in model.Elements)
        {
            Add(string.IsNullOrWhiteSpace(element.Notation) ? model.Notation : element.Notation, element.SemanticType);
        }

        foreach (var relationship in model.Relationships)
        {
            Add(model.Notation, relationship.RelationshipType);
        }

        foreach (var operation in batch?.Operations ?? [])
        {
            if (operation.SemanticType is { Length: > 0 } semanticType)
            {
                Add(operation.Notation ?? model.Notation, semanticType);
            }

            if (operation.RelationshipType is { Length: > 0 } relationshipType)
            {
                Add(model.Notation, relationshipType);
            }
        }

        return pairs;
    }

    /// <summary>
    /// The pairs of a JSON operation array (the <c>modeling_apply_operations</c> payload) — the MCP
    /// front-end's counterpart of the <see cref="ModelingOperationBatch"/> overload. A malformed
    /// payload yields no extra pairs: the engine reports the parse error itself, and a preload must
    /// never turn a parse failure into a schema failure.
    /// </summary>
    /// <param name="model">The model the batch applies to.</param>
    /// <param name="operationsJson">The raw JSON array of operations (may be null/blank).</param>
    public static IReadOnlyList<(string Notation, string SemanticType)> PropertySchemaPairsFromJson(
        ModelingModelDto model,
        string? operationsJson)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrWhiteSpace(operationsJson))
        {
            return PropertySchemaPairs(model);
        }

        List<ModelingOperation>? operations = null;
        try
        {
            operations = System.Text.Json.JsonSerializer.Deserialize<List<ModelingOperation>>(
                operationsJson,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
        }
        catch (System.Text.Json.JsonException)
        {
            operations = null;
        }

        return operations is null
            ? PropertySchemaPairs(model)
            : PropertySchemaPairs(model, new ModelingOperationBatch { Operations = operations });
    }

    /// <summary>
    /// Preloads every schema the model/batch can need and returns the synchronous resolver to put on
    /// <see cref="ModelingOperationContext.ResolvePropertySchema"/>. Returns <c>null</c> when no
    /// provider is registered, so a caller can assign the result directly and keep the 2.10.0
    /// behavior without branching.
    /// </summary>
    /// <param name="provider">The host provider; null yields a null resolver.</param>
    /// <param name="model">The model whose pairs are preloaded.</param>
    /// <param name="extraPairs">Extra pairs the caller knows about (e.g. a batch's target types).</param>
    /// <param name="viewpointKey">The viewpoint the caller authors under (empty when it has none).</param>
    /// <param name="culture">The culture of the labels; null = the current UI culture.</param>
    /// <param name="cancellationToken">Cancels the preload; a cancelled pair is not cached.</param>
    public static async Task<Func<ModelingPropertySchemaRequest, ModelingPropertySchemaDto?>?> PreloadAsync(
        IModelingPropertySchemaProvider? provider,
        ModelingModelDto model,
        IEnumerable<(string Notation, string SemanticType)>? extraPairs = null,
        string? viewpointKey = null,
        string? culture = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (provider is null)
        {
            return null;
        }

        var cultureName = string.IsNullOrWhiteSpace(culture) ? CultureInfo.CurrentUICulture.Name : culture!;
        var viewpoint = viewpointKey ?? string.Empty;
        var schemas = new Dictionary<string, ModelingPropertySchemaDto?>(StringComparer.Ordinal);

        var pairs = new List<(string Notation, string SemanticType)>(PropertySchemaPairs(model));
        var seen = new HashSet<string>(
            pairs.Select(pair => $"{pair.Notation}\u001f{pair.SemanticType}"),
            StringComparer.Ordinal);
        foreach (var pair in extraPairs ?? [])
        {
            if (!string.IsNullOrWhiteSpace(pair.SemanticType)
                && seen.Add($"{pair.Notation}\u001f{pair.SemanticType}"))
            {
                pairs.Add(pair);
            }
        }

        foreach (var (notation, semanticType) in pairs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = CacheKey(notation, semanticType);
            if (schemas.ContainsKey(key))
            {
                continue;
            }

            ModelingPropertySchemaDto? schema;
            try
            {
                schema = await provider.GetSchemaAsync(
                    new ModelingPropertySchemaRequest
                    {
                        ModelId = model.Id,
                        Notation = notation,
                        SemanticType = semanticType,
                        ViewpointKey = viewpoint,
                        Culture = cultureName,
                        SchemaVersion = null
                    },
                    cancellationToken);
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // A failing host provider must never break the batch: this pair validates nothing.
                schema = null;
            }

            schemas[key] = schema;
        }

        return request =>
        {
            if (request is null || string.IsNullOrWhiteSpace(request.SemanticType))
            {
                return null;
            }

            return schemas.TryGetValue(CacheKey(request.Notation, request.SemanticType), out var schema)
                ? schema
                : null;
        };
    }

    private static string CacheKey(string? notation, string? semanticType)
        => $"{notation ?? string.Empty}\u001f{semanticType ?? string.Empty}";
}
