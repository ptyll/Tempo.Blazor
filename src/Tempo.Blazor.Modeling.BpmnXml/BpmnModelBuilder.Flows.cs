using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>Partial of <see cref="BpmnModelBuilder"/> — relationship creation: sequence flows, message flows, associations and data associations plus the IO-spec/property registration they feed.</summary>
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
        var direction = element.Attribute("associationDirection")?.Value ?? element.Attribute("direction")?.Value;
        if (!string.IsNullOrWhiteSpace(direction))
        {
            relationship.Properties["direction"] = BpmnImportInternals.StringElement(direction!);
        }
    }

    private void CreateDataAssociation(XElement element, ModelingElementDto enclosing, bool isInput)
    {
        var sources = BpmnImportInternals.Children(element, "sourceRef").Select(reference => StripRef(reference.Value)).ToList();
        var targets = BpmnImportInternals.Children(element, "targetRef").Select(reference => StripRef(reference.Value)).ToList();

        if (isInput && targets.Count == 0)
        {
            targets.Add(enclosing.Id);
        }

        if (!isInput && sources.Count == 0)
        {
            sources.Add(enclosing.Id);
        }

        sources = sources.Select(reference => ResolveIoEndpoint(reference, enclosing.Id)).ToList();
        targets = targets.Select(reference => ResolveIoEndpoint(reference, enclosing.Id)).ToList();

        var assignments = BpmnImportInternals.Children(element, "assignment")
            .Select(assignment => (JsonNode?)new JsonObject
            {
                ["from"] = BpmnImportInternals.DirectText(BpmnImportInternals.Child(assignment, "from")),
                ["to"] = BpmnImportInternals.DirectText(BpmnImportInternals.Child(assignment, "to")),
            })
            .ToList();

        var pairs = new List<(string Source, string Target)>();
        if (sources.Count == 0 || targets.Count == 0)
        {
            AddLoss(element, element.Name.LocalName,
                "Data association has no resolvable endpoints; the association is not imported.",
                BpmnImportLossSeverity.Loss, elementId: enclosing.Id);
            return;
        }

        foreach (var source in sources)
        {
            foreach (var target in targets)
            {
                pairs.Add((source, target));
            }
        }

        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            var id = index == 0
                ? RequiredId(element, element.Name.LocalName)
                : RequiredId(element, element.Name.LocalName) + "_" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var relationship = new ModelingRelationshipDto
            {
                Id = id,
                SourceId = element.Attribute("id")?.Value ?? string.Empty,
                SourceType = element.Name.LocalName,
                SourceElementId = pair.Source,
                TargetElementId = pair.Target,
                RelationshipType = isInput ? BpmnRelationshipTypes.DataInputAssociation : BpmnRelationshipTypes.DataOutputAssociation,
                Name = element.Attribute("name")?.Value ?? string.Empty,
            };
            if (assignments.Count > 0)
            {
                relationship.Properties["assignments"] = BpmnImportInternals.JsonElementFrom(assignments);
            }

            ApplyCatchAllAttributes(element, relationship);
            _nodeOwners[enclosing.Id].Relationships.Add(relationship);
        }
    }

    private string ResolveIoEndpoint(string reference, string enclosingId)
    {
        if (_ioResolutions.TryGetValue(reference, out var resolution))
        {
            _referencedIoEndpoints.Add(reference);
            return resolution.ActivityId;
        }

        return reference;
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
        host.Relationships.Add(relationship);

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

    // ---------------------------------------------------------------- io declarations

    private void RegisterIoSpecification(XElement ioSpecification, ModelingElementDto enclosing)
    {
        foreach (var dataInput in BpmnImportInternals.Children(ioSpecification, "dataInput"))
        {
            if (dataInput.Attribute("id") is { } id)
            {
                _ioResolutions[id.Value] = (enclosing.Id, dataInput);
            }
        }

        foreach (var dataOutput in BpmnImportInternals.Children(ioSpecification, "dataOutput"))
        {
            if (dataOutput.Attribute("id") is { } id)
            {
                _ioResolutions[id.Value] = (enclosing.Id, dataOutput);
            }
        }
    }

    private void RegisterActivityProperty(XElement property, ModelingElementDto enclosing)
    {
        if (property.Attribute("id") is { } id)
        {
            _ioResolutions[id.Value] = (enclosing.Id, property);
        }
    }

    private void ReportUnreferencedIoDeclarations()
    {
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (endpointId, resolution) in _ioResolutions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (_referencedIoEndpoints.Contains(endpointId) || !reported.Add(resolution.ActivityId + "|" + endpointId))
            {
                continue;
            }

            var declarationKind = endpointId.StartsWith("DataInput", StringComparison.Ordinal) || endpointId.StartsWith("DataOutput", StringComparison.Ordinal)
                ? "ioSpecification entry"
                : "property";
            _losses.Add(new BpmnImportLoss
            {
                ElementId = resolution.ActivityId,
                XmlPath = BpmnImportInternals.BuildPath(resolution.Declaration),
                Construct = declarationKind,
                Reason = $"Activity {declarationKind} '{endpointId}' is declared but not referenced by any data association; the declaration is not preserved.",
                Severity = BpmnImportLossSeverity.Loss,
            });
        }
    }

}
