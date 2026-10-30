using System.Text.Json.Nodes;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>
/// Applies an ordered batch of edit operations to a <see cref="ModelingModelDto"/>, enforcing
/// notation relationship rules. The engine mutates a working copy; the caller only persists when
/// <see cref="ModelingOperationResult.Success"/> is true, so a partially-invalid batch is atomic
/// (nothing is saved).
/// </summary>
internal static class ModelingOperationEngine
{
    public static ModelingOperationResult Apply(
        ModelingModelDto model,
        string operationsJson,
        IModelingRelationshipRulesProvider? relationshipRules)
    {
        var result = new ModelingOperationResult();

        if (!McpJsonHelpers.TryParseOperationArray(operationsJson, out var operations, out var parseErrors))
        {
            result.Errors.AddRange(parseErrors);
            return result;
        }

        for (var i = 0; i < operations!.Count; i++)
        {
            if (operations[i] is not JsonObject op)
            {
                result.Errors.Add($"operations[{i}]: expected a JSON object.");
                continue;
            }

            var kind = GetString(op, "op");
            if (string.IsNullOrWhiteSpace(kind))
            {
                result.Errors.Add($"operations[{i}]: missing 'op'.");
                continue;
            }

            var errorsBefore = result.Errors.Count;
            switch (kind)
            {
                case "add_element": ApplyAddElement(model, op, i, result); break;
                case "update_element": ApplyUpdateElement(model, op, i, result); break;
                case "delete_element": ApplyDeleteElement(model, op, i, result); break;
                case "add_relationship": ApplyAddRelationship(model, op, i, result, relationshipRules); break;
                case "update_relationship": ApplyUpdateRelationship(model, op, i, result, relationshipRules); break;
                case "delete_relationship": ApplyDeleteRelationship(model, op, i, result); break;
                default:
                    result.Errors.Add($"operations[{i}]: unknown op '{kind}'.");
                    break;
            }

            if (result.Errors.Count == errorsBefore)
            {
                result.Applied++;
            }
        }

        return result;
    }

    // ── Elements ─────────────────────────────────────────────────────────────────

    private static void ApplyAddElement(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result)
    {
        var name = GetString(op, "name");
        var semanticType = GetString(op, "semanticType");
        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add($"operations[{i}] add_element: 'name' is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            result.Errors.Add($"operations[{i}] add_element: 'semanticType' is required.");
            return;
        }

        var id = GetString(op, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            id = Guid.NewGuid().ToString();
            result.CreatedIds.Add(id);
        }
        else if (model.Elements.Any(e => e.Id == id))
        {
            result.Errors.Add($"operations[{i}] add_element: an element with id '{id}' already exists.");
            return;
        }

        var parentId = GetString(op, "parentId");
        if (!ValidateParentReference(model, id, parentId, i, "add_element", result))
        {
            return;
        }

        model.Elements.Add(new ModelingElementDto
        {
            Id = id,
            Name = name,
            SemanticType = semanticType,
            Notation = GetString(op, "notation") ?? model.Notation,
            Description = GetString(op, "description") ?? string.Empty,
            ParentId = parentId ?? string.Empty
        });
    }

    private static void ApplyUpdateElement(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result)
    {
        var id = GetString(op, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            result.Errors.Add($"operations[{i}] update_element: 'id' is required.");
            return;
        }

        var element = model.Elements.FirstOrDefault(e => e.Id == id);
        if (element is null)
        {
            result.Errors.Add($"operations[{i}] update_element: element '{id}' not found.");
            return;
        }

        if (op.ContainsKey("name")) element.Name = GetString(op, "name") ?? string.Empty;
        if (op.ContainsKey("semanticType")) element.SemanticType = GetString(op, "semanticType") ?? string.Empty;
        if (op.ContainsKey("description")) element.Description = GetString(op, "description") ?? string.Empty;
        if (op.ContainsKey("notation")) element.Notation = GetString(op, "notation") ?? string.Empty;
        if (op.ContainsKey("parentId"))
        {
            // An empty string clears the parent (the element becomes a root); any other value
            // must resolve to an existing element and must not close a cycle.
            var parentId = GetString(op, "parentId") ?? string.Empty;
            if (!ValidateParentReference(model, id, parentId, i, "update_element", result))
            {
                return;
            }

            element.ParentId = parentId;
        }
    }

    /// <summary>Validates a proposed ParentId edge (phase 12): the parent must exist, must not be
    /// the element itself, and walking up its ancestor chain must never reach the element — a
    /// chain that does would close a parent cycle.</summary>
    private static bool ValidateParentReference(
        ModelingModelDto model,
        string elementId,
        string? parentId,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        if (string.IsNullOrWhiteSpace(parentId))
        {
            return true;
        }

        if (parentId == elementId)
        {
            result.Errors.Add($"operations[{index}] {opName}: element '{elementId}' cannot be its own parent (cycle).");
            return false;
        }

        if (!model.Elements.Any(e => e.Id == parentId))
        {
            result.Errors.Add($"operations[{index}] {opName}: parent element '{parentId}' not found.");
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = parentId;
        while (!string.IsNullOrEmpty(current) && seen.Add(current))
        {
            if (current == elementId)
            {
                result.Errors.Add($"operations[{index}] {opName}: setting '{parentId}' as the parent of '{elementId}' would create a parent cycle.");
                return false;
            }

            current = model.Elements.FirstOrDefault(e => e.Id == current)?.ParentId;
        }

        return true;
    }

    private static void ApplyDeleteElement(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result)
    {
        var id = GetString(op, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            result.Errors.Add($"operations[{i}] delete_element: 'id' is required.");
            return;
        }

        var element = model.Elements.FirstOrDefault(e => e.Id == id);
        if (element is null)
        {
            result.Errors.Add($"operations[{i}] delete_element: element '{id}' not found.");
            return;
        }

        // No silent cascade: the agent must delete referencing relationships in the same batch.
        var referencing = model.Relationships
            .Where(r => r.SourceElementId == id || r.TargetElementId == id)
            .Select(r => r.Id)
            .ToList();
        if (referencing.Count > 0)
        {
            result.Errors.Add(
                $"operations[{i}] delete_element: element '{id}' is still referenced by relationship(s) [{string.Join(", ", referencing)}]. Delete them in the same batch first.");
            return;
        }

        // No silent cascade either way (phase 12): an element that still parents other elements
        // must be reparented or deleted afterwards — the agent orders the batch.
        var children = model.Elements
            .Where(e => e.ParentId == id)
            .Select(e => e.Id)
            .ToList();
        if (children.Count > 0)
        {
            result.Errors.Add(
                $"operations[{i}] delete_element: element '{id}' still has child element(s) [{string.Join(", ", children)}]. Delete or reparent them in the same batch first.");
            return;
        }

        model.Elements.Remove(element);
    }

    // ── Relationships ────────────────────────────────────────────────────────────

    private static void ApplyAddRelationship(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result, IModelingRelationshipRulesProvider? rules)
    {
        var relationshipType = GetString(op, "relationshipType");
        var sourceId = GetString(op, "sourceElementId");
        var targetId = GetString(op, "targetElementId");
        if (string.IsNullOrWhiteSpace(relationshipType))
        {
            result.Errors.Add($"operations[{i}] add_relationship: 'relationshipType' is required.");
            return;
        }
        if (string.IsNullOrWhiteSpace(sourceId) || string.IsNullOrWhiteSpace(targetId))
        {
            result.Errors.Add($"operations[{i}] add_relationship: 'sourceElementId' and 'targetElementId' are required.");
            return;
        }

        var relationship = new ModelingRelationshipDto
        {
            Id = GetString(op, "id") ?? string.Empty,
            RelationshipType = relationshipType,
            SourceElementId = sourceId,
            TargetElementId = targetId,
            Name = GetString(op, "name") ?? string.Empty
        };

        if (!string.IsNullOrWhiteSpace(relationship.Id) && model.Relationships.Any(r => r.Id == relationship.Id))
        {
            result.Errors.Add($"operations[{i}] add_relationship: a relationship with id '{relationship.Id}' already exists.");
            return;
        }

        if (!ValidateRelationship(model, relationship, rules, i, "add_relationship", result))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(relationship.Id))
        {
            relationship.Id = Guid.NewGuid().ToString();
            result.CreatedIds.Add(relationship.Id);
        }
        model.Relationships.Add(relationship);
    }

    private static void ApplyUpdateRelationship(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result, IModelingRelationshipRulesProvider? rules)
    {
        var id = GetString(op, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            result.Errors.Add($"operations[{i}] update_relationship: 'id' is required.");
            return;
        }

        var relationship = model.Relationships.FirstOrDefault(r => r.Id == id);
        if (relationship is null)
        {
            result.Errors.Add($"operations[{i}] update_relationship: relationship '{id}' not found.");
            return;
        }

        // Validate against the proposed post-update state without mutating on failure.
        var proposed = new ModelingRelationshipDto
        {
            Id = relationship.Id,
            RelationshipType = op.ContainsKey("relationshipType") ? GetString(op, "relationshipType") ?? string.Empty : relationship.RelationshipType,
            SourceElementId = op.ContainsKey("sourceElementId") ? GetString(op, "sourceElementId") ?? string.Empty : relationship.SourceElementId,
            TargetElementId = op.ContainsKey("targetElementId") ? GetString(op, "targetElementId") ?? string.Empty : relationship.TargetElementId,
            Name = op.ContainsKey("name") ? GetString(op, "name") ?? string.Empty : relationship.Name
        };

        if (!ValidateRelationship(model, proposed, rules, i, "update_relationship", result))
        {
            return;
        }

        relationship.RelationshipType = proposed.RelationshipType;
        relationship.SourceElementId = proposed.SourceElementId;
        relationship.TargetElementId = proposed.TargetElementId;
        relationship.Name = proposed.Name;
    }

    private static void ApplyDeleteRelationship(ModelingModelDto model, JsonObject op, int i, ModelingOperationResult result)
    {
        var id = GetString(op, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            result.Errors.Add($"operations[{i}] delete_relationship: 'id' is required.");
            return;
        }

        var relationship = model.Relationships.FirstOrDefault(r => r.Id == id);
        if (relationship is null)
        {
            result.Errors.Add($"operations[{i}] delete_relationship: relationship '{id}' not found.");
            return;
        }

        model.Relationships.Remove(relationship);
    }

    /// <summary>Resolves endpoints and enforces the notation relationship rule. Records an error and
    /// returns false when the relationship is invalid; nothing is written by the caller in that case.</summary>
    internal static bool ValidateRelationship(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        IModelingRelationshipRulesProvider? rules,
        int index,
        string opName,
        ModelingOperationResult result)
    {
        var source = model.Elements.FirstOrDefault(e => e.Id == relationship.SourceElementId);
        var target = model.Elements.FirstOrDefault(e => e.Id == relationship.TargetElementId);
        if (source is null)
        {
            result.Errors.Add($"operations[{index}] {opName}: source element '{relationship.SourceElementId}' not found.");
            return false;
        }
        if (target is null)
        {
            result.Errors.Add($"operations[{index}] {opName}: target element '{relationship.TargetElementId}' not found.");
            return false;
        }

        if (rules is null)
        {
            return true;
        }

        var notationKey = !string.IsNullOrWhiteSpace(model.Notation) ? model.Notation : source.Notation;
        var ruleResult = rules.ValidateRelationship(new ModelingRelationshipRuleContext
        {
            NotationKey = notationKey,
            Relationship = relationship,
            SourceElement = source,
            TargetElement = target,
            ElementsById = model.Elements
                .Where(e => !string.IsNullOrEmpty(e.Id))
                .GroupBy(e => e.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal)
        });

        if (!ruleResult.IsValid)
        {
            var fix = string.IsNullOrWhiteSpace(ruleResult.SuggestedFix) ? string.Empty : $" {ruleResult.SuggestedFix}";
            result.Errors.Add(
                $"operations[{index}] {opName}: '{relationship.RelationshipType}' from '{source.SemanticType}' to '{target.SemanticType}' is not valid for notation '{notationKey}'. {ruleResult.Message}{fix}".TrimEnd());
            return false;
        }

        return true;
    }

    private static string? GetString(JsonObject op, string key)
    {
        if (!op.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }
        try { return node.GetValue<string?>(); }
        catch (InvalidOperationException) { return node.ToString(); }
        catch (FormatException) { return node.ToString(); }
    }
}

/// <summary>Outcome of a modeling operation batch.</summary>
internal sealed class ModelingOperationResult
{
    public List<string> Errors { get; } = [];
    public List<string> CreatedIds { get; } = [];
    public int Applied { get; set; }
    public bool Success => Errors.Count == 0;
}
