using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Modeling;

/// <summary>
/// Validates a whole <see cref="ModelingModelDto"/> against structural integrity and notation
/// relationship rules, producing <see cref="ModelingIssueDto"/> diagnostics (the same shape the
/// modeling editor surfaces).
/// </summary>
internal static class ModelingValidationEngine
{
    public static List<ModelingIssueDto> Validate(
        ModelingModelDto model,
        IModelingRelationshipRulesProvider? relationshipRules)
    {
        var issues = new List<ModelingIssueDto>();
        var elementsById = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);

        foreach (var element in model.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id))
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "validation", "An element has an empty id.",
                    "Assign a unique id to every element."));
                continue;
            }
            if (!elementsById.TryAdd(element.Id, element))
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "validation", $"Duplicate element id '{element.Id}'.",
                    "Element ids must be unique within the model.", sourceElementId: element.Id));
            }
        }

        var seenRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var notationKey = model.Notation;

        ValidateHierarchy(model, elementsById, issues);

        foreach (var relationship in model.Relationships)
        {
            if (!string.IsNullOrEmpty(relationship.Id) && !seenRelationshipIds.Add(relationship.Id))
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "validation", $"Duplicate relationship id '{relationship.Id}'.",
                    "Relationship ids must be unique within the model.", sourceRelationshipId: relationship.Id));
            }

            var hasSource = elementsById.TryGetValue(relationship.SourceElementId, out var source);
            var hasTarget = elementsById.TryGetValue(relationship.TargetElementId, out var target);
            if (!hasSource || !hasTarget)
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "validation",
                    $"Relationship '{relationship.Id}' references a missing {(hasSource ? "target" : "source")} element.",
                    "Point the relationship at existing elements, or remove it.",
                    sourceRelationshipId: relationship.Id));
                continue;
            }

            if (relationshipRules is null)
            {
                continue;
            }

            var effectiveNotation = !string.IsNullOrWhiteSpace(notationKey) ? notationKey : source!.Notation;
            var ruleResult = relationshipRules.ValidateRelationship(new ModelingRelationshipRuleContext
            {
                NotationKey = effectiveNotation,
                Relationship = relationship,
                SourceElement = source!,
                TargetElement = target!,
                ElementsById = elementsById
            });

            if (!ruleResult.IsValid)
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "validation",
                    string.IsNullOrWhiteSpace(ruleResult.Message)
                        ? $"Relationship '{relationship.RelationshipType}' from '{source!.SemanticType}' to '{target!.SemanticType}' is not valid for notation '{effectiveNotation}'."
                        : ruleResult.Message,
                    ruleResult.SuggestedFix,
                    sourceRelationshipId: relationship.Id));
            }
        }

        return issues;
    }

    /// <summary>Phase 12 hierarchy validation (category <c>hierarchy</c>): a ParentId that does
    /// not resolve to another element, and parent cycles — every cycle member is reported
    /// (deterministically), an element merely HANGING OFF a cycle is not.</summary>
    private static void ValidateHierarchy(
        ModelingModelDto model,
        Dictionary<string, ModelingElementDto> elementsById,
        List<ModelingIssueDto> issues)
    {
        var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            if (string.IsNullOrWhiteSpace(element.Id) || string.IsNullOrWhiteSpace(element.ParentId))
            {
                continue;
            }

            if (element.ParentId == element.Id)
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "hierarchy",
                    $"Element '{element.Id}' is its own parent (cycle).",
                    "Break the ParentId cycle so every element resolves to a root.",
                    sourceElementId: element.Id));
                continue;
            }

            if (!elementsById.ContainsKey(element.ParentId))
            {
                issues.Add(Issue(ModelingIssueSeverity.Error, "hierarchy",
                    $"Element '{element.Id}' references a missing parent '{element.ParentId}'.",
                    "Point ParentId at an existing element, or clear it to render the element at the root.",
                    sourceElementId: element.Id));
                continue;
            }

            parentOf[element.Id] = element.ParentId;
        }

        var inCycle = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in parentOf.Keys)
        {
            var current = parentOf[id];
            while (parentOf.TryGetValue(current, out var parent) && inCycle.Add(current))
            {
                if (current == id)
                {
                    // Walking up returned to the START element: it sits inside the cycle.
                    issues.Add(Issue(ModelingIssueSeverity.Error, "hierarchy",
                        $"Element '{id}' participates in a parent cycle.",
                        "Break the ParentId cycle so every element resolves to a root.",
                        sourceElementId: id));
                    break;
                }

                current = parent;
            }

            inCycle.Clear();
        }
    }

    private static ModelingIssueDto Issue(
        ModelingIssueSeverity severity,
        string category,
        string message,
        string suggestedFix,
        string sourceElementId = "",
        string sourceRelationshipId = "")
        => new()
        {
            Id = Guid.NewGuid().ToString("n"),
            Severity = severity,
            Category = category,
            Message = message,
            SuggestedFix = suggestedFix ?? string.Empty,
            SourceElementId = sourceElementId,
            SourceRelationshipId = sourceRelationshipId
        };
}
