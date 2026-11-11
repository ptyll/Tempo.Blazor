using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Partial of <see cref="BpmnModelBuilder"/> — relationship creation: sequence flows, message flows and associations (data associations and IO specifications live in BpmnModelBuilder.Data.cs).</summary>
internal sealed partial class BpmnModelBuilder
{
    private void CreateSequenceFlow(XElement element, ModelingModelDto model)
    {
        var source = StripRef(element.Attribute("sourceRef")?.Value);
        var target = StripRef(element.Attribute("targetRef")?.Value);
        if (source.Length == 0 || target.Length == 0)
        {
            AddLoss(element, "sequenceFlow",
                "Sequence flow is missing its sourceRef or targetRef attribute; the flow imports with an empty endpoint.",
                BpmnImportLossSeverity.Loss);
        }

        var condition = BpmnImportInternals.Child(element, "conditionExpression");
        string relationshipType;
        if (condition is not null)
        {
            relationshipType = BpmnRelationshipTypes.ConditionalFlow;
        }
        else if (_defaultFlowIds.Contains(element.Attribute("id")?.Value ?? string.Empty))
        {
            relationshipType = BpmnRelationshipTypes.DefaultFlow;
        }
        else
        {
            relationshipType = BpmnRelationshipTypes.SequenceFlow;
        }

        var relationship = CreateRelationship(element, relationshipType, source, target, model);
        ApplyRelationshipExtensionElements(element, relationship, model);
        if (condition is not null)
        {
            relationship.Properties["condition"] = BpmnImportInternals.StringElement(BpmnImportInternals.DirectText(condition));
            if (condition.Attribute("language") is { } language)
            {
                relationship.Properties["conditionLanguage"] = BpmnImportInternals.StringElement(language.Value);
            }
        }
    }

    private void CreateAssociation(XElement element, ModelingModelDto model)
    {
        var source = StripRef(element.Attribute("sourceRef")?.Value);
        var target = StripRef(element.Attribute("targetRef")?.Value);
        var relationship = CreateRelationship(element, BpmnRelationshipTypes.Association, source, target, model);
        ApplyRelationshipExtensionElements(element, relationship, model);
        var direction = element.Attribute("associationDirection")?.Value ?? element.Attribute("direction")?.Value;
        if (!string.IsNullOrWhiteSpace(direction))
        {
            relationship.Properties["direction"] = BpmnImportInternals.StringElement(direction!);
        }
    }


    private void CreateMessageFlow(XElement element)
    {
        var source = StripRef(element.Attribute("sourceRef")?.Value);
        var target = StripRef(element.Attribute("targetRef")?.Value);
        var sourceModel = ResolveEndpointModel(source);
        var targetModel = ResolveEndpointModel(target);
        var host = sourceModel ?? targetModel ?? _models[0];

        var relationship = new ModelingRelationshipDto
        {
            Id = RequiredId(element, "messageFlow"),
            SourceId = element.Attribute("id")?.Value ?? string.Empty,
            SourceType = "messageFlow",
            SourceElementId = source,
            TargetElementId = target,
            RelationshipType = BpmnRelationshipTypes.MessageFlow,
            Name = element.Attribute("name")?.Value ?? string.Empty,
        };
        ApplyCatchAllAttributes(element, relationship);
        ApplyRelationshipExtensionElements(element, relationship, host);
        host.Relationships.Add(relationship);
        _relationshipIndex[relationship.Id] = (host, relationship);

        var messageRef = StripRef(element.Attribute("messageRef")?.Value);
        if (messageRef.Length > 0 && _globalDefinitions.ContainsKey(messageRef))
        {
            MarkGlobalUsed(host, messageRef);
        }

        if (!ReferenceEquals(sourceModel, host) && source.Length > 0)
        {
            relationship.Properties["externalModelRef"] = BpmnImportInternals.JsonElementFrom(new JsonObject
            {
                ["modelId"] = sourceModel?.Id ?? string.Empty,
                ["elementId"] = source,
            });
        }
        else if (!ReferenceEquals(targetModel, host) && target.Length > 0)
        {
            relationship.Properties["externalModelRef"] = BpmnImportInternals.JsonElementFrom(new JsonObject
            {
                ["modelId"] = targetModel?.Id ?? string.Empty,
                ["elementId"] = target,
            });
        }

        if (source.Length == 0 || target.Length == 0)
        {
            AddLoss(element, "messageFlow",
                "Message flow is missing its sourceRef or targetRef attribute; the flow imports with an empty endpoint.");
        }
    }

    private ModelingModelDto? ResolveEndpointModel(string endpointId)
    {
        if (endpointId.Length == 0)
        {
            return null;
        }

        if (_nodeOwners.TryGetValue(endpointId, out var nodeModel))
        {
            return nodeModel;
        }

        if (_poolOwners.TryGetValue(endpointId, out var pool))
        {
            return pool.Model;
        }

        return null;
    }

    private void ReportUnsupportedCollaborationConstructs(XElement collaboration)
    {
        string[] unsupported =
        [
            "choreographyTask", "subChoreography", "callChoreography", "conversation",
            "subConversation", "callConversation", "conversationLink", "correlationKey",
            "globalConversation", "partnerEntity", "partnerRole",
        ];

        foreach (var element in collaboration.Elements())
        {
            if (unsupported.Contains(element.Name.LocalName, StringComparer.Ordinal))
            {
                AddLoss(element, element.Name.LocalName,
                    "Choreography/conversation construct is not supported by the modeling notation; content is not preserved.");
            }
        }
    }
}
