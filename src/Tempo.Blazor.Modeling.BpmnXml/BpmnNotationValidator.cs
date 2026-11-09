using System.Globalization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Headless BPMN notation sanity checks run over the imported models. The converter cannot depend
/// on the internal notation catalog (Tempo.Blazor.Modeling), so the handful of structural rules
/// that matter for a usable result are validated here and attached to the models as issues.
/// </summary>
internal static class BpmnNotationValidator
{
    private static readonly string[] FlowRelationshipTypes =
    [
        BpmnRelationshipTypes.SequenceFlow,
        BpmnRelationshipTypes.ConditionalFlow,
        BpmnRelationshipTypes.DefaultFlow,
    ];

    public static void AttachValidationIssues(List<ModelingModelDto> models, List<string> duplicateElementIds)
    {
        var ordinal = 0;
        foreach (var model in models)
        {
            var elementIds = model.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            var incoming = new HashSet<string>(StringComparer.Ordinal);
            var outgoing = new HashSet<string>(StringComparer.Ordinal);
            foreach (var relationship in model.Relationships)
            {
                if (!FlowRelationshipTypes.Contains(relationship.RelationshipType, StringComparer.Ordinal))
                {
                    continue;
                }

                if (relationship.SourceElementId.Length > 0)
                {
                    outgoing.Add(relationship.SourceElementId);
                }

                if (relationship.TargetElementId.Length > 0)
                {
                    incoming.Add(relationship.TargetElementId);
                }
            }

            foreach (var relationship in model.Relationships)
            {
                if (relationship.RelationshipType == BpmnRelationshipTypes.MessageFlow
                    && relationship.Properties.ContainsKey("externalModelRef"))
                {
                    // Cross-model message flows intentionally reference an element of another model.
                    continue;
                }

                if (relationship.SourceElementId.Length > 0 && !elementIds.Contains(relationship.SourceElementId))
                {
                    AddIssue(model, ref ordinal, ModelingIssueSeverity.Error,
                        $"Relationship '{relationship.Id}' source '{relationship.SourceElementId}' does not resolve to an element of the model.",
                        sourceElementId: relationship.SourceElementId);
                }

                if (relationship.TargetElementId.Length > 0 && !elementIds.Contains(relationship.TargetElementId))
                {
                    AddIssue(model, ref ordinal, ModelingIssueSeverity.Error,
                        $"Relationship '{relationship.Id}' target '{relationship.TargetElementId}' does not resolve to an element of the model.",
                        sourceElementId: relationship.TargetElementId);
                }
            }

            foreach (var element in model.Elements)
            {
                if (element.SemanticType == BpmnSemanticTypes.BoundaryEvent
                    && element.Properties.TryGetValue("attachedToRef", out var attached)
                    && attached.ValueKind == System.Text.Json.JsonValueKind.String
                    && !elementIds.Contains(attached.GetString() ?? string.Empty))
                {
                    AddIssue(model, ref ordinal, ModelingIssueSeverity.Error,
                        $"Boundary event '{element.Id}' is attached to '{attached.GetString()}', which is not an element of the model.",
                        sourceElementId: element.Id);
                }

                if (element.SemanticType == BpmnSemanticTypes.StartEvent && incoming.Contains(element.Id))
                {
                    AddIssue(model, ref ordinal, ModelingIssueSeverity.Warning,
                        $"Start event '{element.Id}' has an incoming sequence flow; BPMN start events should not be arrived at.",
                        sourceElementId: element.Id);
                }

                if (element.SemanticType == BpmnSemanticTypes.EndEvent && outgoing.Contains(element.Id))
                {
                    AddIssue(model, ref ordinal, ModelingIssueSeverity.Warning,
                        $"End event '{element.Id}' has an outgoing sequence flow; BPMN end events should not continue the flow.",
                        sourceElementId: element.Id);
                }
            }
        }

        foreach (var duplicateId in duplicateElementIds.Distinct(StringComparer.Ordinal))
        {
            if (models.Count == 0)
            {
                continue;
            }

            var ordinalCopy = ordinal;
            AddIssue(models[0], ref ordinalCopy, ModelingIssueSeverity.Error,
                $"Element id '{duplicateId}' is declared more than once in the document.", sourceElementId: duplicateId);
            ordinal = ordinalCopy;
        }
    }

    private static void AddIssue(ModelingModelDto model, ref int ordinal, ModelingIssueSeverity severity,
        string message, string sourceElementId)
    {
        ordinal++;
        model.Issues.Add(new ModelingIssueDto
        {
            Id = "bpmn-import-rule-" + ordinal.ToString("D4", CultureInfo.InvariantCulture),
            Category = "bpmn-import",
            Severity = severity,
            Message = message,
            SourceElementId = sourceElementId,
            SourceRelationshipId = string.Empty,
            SuggestedFix = string.Empty,
        });
    }
}
