using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Tempo.Blazor.Modeling;

/// <summary>
/// Phase 5 (decision <c>MODELING-PROPERTY-SCHEMA</c> §5.5): the ENGINE half of the shared property
/// validation — the payload composition (<c>existing + set − removed</c>), the "touched keys only for
/// an update / full set for a new or re-typed item" rule and the validator-code → <c>property_*</c>
/// mapping. INTERNAL by design: the public contract is <see cref="ModelingOperationEngine"/> plus
/// <see cref="ModelingPropertySchemaValidator"/> plus <see cref="ModelingPropertySchemaResolver"/>;
/// this helper only wires them together and keeps the engine file under the 1200-line source
/// ceiling.
/// <para>
/// Two entry points: <see cref="Validate"/> judges ONE operation's touched keys (the per-operation
/// pass) and <see cref="ValidateFullSet"/> judges a whole property set — the deferred pass the engine
/// runs AFTER the batch so a later <c>set_properties</c> can satisfy an earlier <c>add_element</c>
/// (review A4).
/// </para>
/// </summary>
internal static class ModelingPropertyPayloadValidation
{
    /// <summary>
    /// Validates ONE operation's property payload with the SHARED
    /// <see cref="ModelingPropertySchemaValidator"/> — the same rules, messages and codes the
    /// inspector and the editor's save gate use. The schema comes from
    /// <see cref="ModelingOperationContext.ResolvePropertySchema"/>; a null resolver, or a null
    /// schema for the pair, validates nothing (the 2.10.0 behavior).
    /// <para>
    /// <c>fullSet</c> composes the RESULTING property set (existing + set − removed) and validates
    /// all of it. Otherwise only the touched keys are judged, so a legacy value elsewhere in the item
    /// never blocks a targeted fix; a REMOVED key still participates (removing a required field fails
    /// with <c>property_required</c>).
    /// </para>
    /// </summary>
    public static bool Validate(
        ModelingModelDto model,
        string notation,
        string semanticType,
        JsonObject? setProperties,
        List<string>? removedProperties,
        bool fullSet,
        ModelingOperationContext context,
        int index,
        string opName,
        ModelingOperationResult result,
        IReadOnlyDictionary<string, JsonElement>? existing = null,
        string? elementId = null,
        string? relationshipId = null)
    {
        var schema = Resolve(model, notation, semanticType, context);
        if (schema is null)
        {
            return true;
        }

        // The payload the rules judge is the RESULTING property set (existing + set − removed), so a
        // cross-field rule sees real values; in the touched mode only the issues of the keys the
        // operation names are reported, so a legacy value elsewhere never blocks a targeted fix.
        var payload = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (existing is not null)
        {
            foreach (var property in existing)
            {
                payload[property.Key] = property.Value;
            }
        }

        var touched = new HashSet<string>(StringComparer.Ordinal);
        if (setProperties is not null)
        {
            foreach (var property in setProperties)
            {
                payload[property.Key] = ToElement(property.Value);
                touched.Add(property.Key);
            }
        }

        if (removedProperties is not null)
        {
            foreach (var key in removedProperties)
            {
                touched.Add(key);
                payload.Remove(key);
            }
        }

        foreach (var issue in new ModelingPropertySchemaValidator().Validate(schema, payload))
        {
            if (issue.Severity != ModelingIssueSeverity.Error)
            {
                continue;
            }

            var path = PropertyIssuePath(issue.Id) ?? string.Empty;
            if (!fullSet && !touched.Contains(TopLevelPropertyKey(path)))
            {
                continue;
            }

            Report(result, index, opName, issue, path, elementId, relationshipId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// The DEFERRED full-set pass (review A4): judges the COMPLETE property set of an added or
    /// re-typed element/relationship against its schema, after the whole batch applied — so
    /// <c>add_element</c> followed by <c>set_properties</c> in ONE batch satisfies the required
    /// fields, while an add that still misses one reports <c>property_required</c> at the ADD's index
    /// (the operation the user must fix). Returns false when an error was recorded.
    /// </summary>
    public static bool ValidateFullSet(
        ModelingModelDto model,
        string notation,
        string semanticType,
        IReadOnlyDictionary<string, JsonElement> properties,
        ModelingOperationContext context,
        int index,
        string opName,
        ModelingOperationResult result,
        string? elementId = null,
        string? relationshipId = null)
    {
        var schema = Resolve(model, notation, semanticType, context);
        if (schema is null)
        {
            return true;
        }

        foreach (var issue in new ModelingPropertySchemaValidator().Validate(schema, properties))
        {
            if (issue.Severity != ModelingIssueSeverity.Error)
            {
                continue;
            }

            Report(result, index, opName, issue, PropertyIssuePath(issue.Id) ?? string.Empty, elementId, relationshipId);
            return false;
        }

        return true;
    }

    /// <summary>The schema for one pair, through the caller's resolver and a request the ENGINE
    /// builds (§5.1): model id, notation, semantic type, the caller's viewpoint (else the target
    /// view's, else ""), the current UI culture and an unpinned version.</summary>
    private static ModelingPropertySchemaDto? Resolve(
        ModelingModelDto model,
        string notation,
        string semanticType,
        ModelingOperationContext context)
    {
        var resolve = context.ResolvePropertySchema;
        if (resolve is null || string.IsNullOrWhiteSpace(semanticType))
        {
            return null;
        }

        try
        {
            return resolve(new ModelingPropertySchemaRequest
            {
                ModelId = model.Id,
                Notation = notation ?? string.Empty,
                SemanticType = semanticType,
                ViewpointKey = context.ViewpointKey ?? model.Views.FirstOrDefault()?.ViewpointKey ?? string.Empty,
                Culture = CultureInfo.CurrentUICulture.Name,
                SchemaVersion = null
            });
        }
        catch (Exception)
        {
            // A caller's resolver must never break the batch: the pair validates nothing.
            return null;
        }
    }

    private static void Report(
        ModelingOperationResult result,
        int index,
        string opName,
        ModelingIssueDto issue,
        string path,
        string? elementId,
        string? relationshipId)
        => ModelingOperationEngine.AddError(
            result,
            index,
            opName,
            PropertyErrorCode(issue.Id),
            $"operations[{index}] {opName}: {issue.Message}",
            suggestedFix: issue.SuggestedFix,
            elementId: elementId,
            relationshipId: relationshipId,
            propertyKey: path);

    /// <summary>The validator's issue code mapped onto the documented <c>property_*</c> error codes
    /// (§6.3). A missing <c>property_length</c> code folds into <c>property_range</c> — a length is a
    /// range on the value's size.</summary>
    private static string PropertyErrorCode(string issueId)
        => IssueCode(issueId) switch
        {
            "required" => "property_required",
            "type" => "property_type",
            "range" or "length" => "property_range",
            "pattern" => "property_pattern",
            "enum" => "property_enum",
            "reference" => "property_reference",
            _ => "property_type"
        };

    /// <summary>The top-level property key of a validator path (<c>key[0].item</c> → <c>key</c>).</summary>
    private static string TopLevelPropertyKey(string path)
    {
        var cut = path.IndexOfAny(['[', '.']);
        return cut < 0 ? path : path[..cut];
    }

    private static string? PropertyIssuePath(string issueId)
    {
        var separator = issueId.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? null : issueId[(separator + 1)..];
    }

    private static string IssueCode(string issueId)
    {
        var separator = issueId.IndexOf(':', StringComparison.Ordinal);
        return separator < 0 ? issueId : issueId[..separator];
    }

    private static JsonElement ToElement(JsonNode? node)
        => JsonSerializer.SerializeToElement<JsonNode?>(node);
}
