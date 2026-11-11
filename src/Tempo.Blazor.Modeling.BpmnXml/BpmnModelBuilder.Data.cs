using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Part of <see cref="BpmnModelBuilder"/> split out to stay under the 1200-line source ceiling (N270/F13).
/// </summary>
internal sealed partial class BpmnModelBuilder
{

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
