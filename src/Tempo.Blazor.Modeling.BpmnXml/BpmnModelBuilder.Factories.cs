using System.Text.Json;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Part of <see cref="BpmnModelBuilder"/> split out to stay under the 1200-line source ceiling (N270/F13).
/// </summary>
internal sealed partial class BpmnModelBuilder
{

    // ---------------------------------------------------------------- element factories

    private ModelingElementDto CreateElement(XElement source, string semanticType, string parentId, ModelingModelDto model)
    {
        var path = BpmnImportInternals.BuildPath(source);
        var id = source.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(id))
        {
            id = BpmnImportInternals.StableId(semanticType, path);
        }
        else if (!_modelElementIds[model].Add(id))
        {
            _duplicateElementIds.Add(id);
            _warnings.Add(new BpmnImportWarning
            {
                Message = $"Element id '{id}' is used more than once in the document; the duplicate keeps the id and stays addressable by XML order.",
                XmlPath = path,
            });
        }

        var element = new ModelingElementDto
        {
            Id = id,
            SourceId = source.Attribute("id")?.Value ?? string.Empty,
            SourceType = source.Name.LocalName,
            SourcePath = path,
            Notation = BpmnConstants.Bpmn2Notation,
            SemanticType = semanticType,
            Name = source.Attribute("name")?.Value ?? string.Empty,
            ParentId = parentId,
        };

        ApplyCatchAllAttributes(source, element);
        model.Elements.Add(element);
        _elementIndex[id] = (model, element);
        return element;
    }

    private void ApplyCatchAllAttributes(XElement source, ModelingElementDto element)
    {
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (IsConsumedAttribute(source, attribute))
            {
                continue;
            }

            element.Properties[CatchAllKey(attribute)] = BpmnImportInternals.StringElement(attribute.Value);
        }
    }

    /// <summary>
    /// Relationship variant of the catch-all: endpoint attributes (sourceRef/targetRef) are
    /// consumed by the mapping; everything else (messageRef, isImmediate, foreign attributes)
    /// is preserved verbatim for round-trip.
    /// </summary>
    private void ApplyCatchAllAttributes(XElement source, ModelingRelationshipDto relationship)
    {
        foreach (var attribute in source.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.NamespaceName.Length == 0
                && attribute.Name.LocalName is "id" or "name" or "sourceRef" or "targetRef")
            {
                continue;
            }

            if (string.Equals(attribute.Name.NamespaceName, BpmnConstants.ModelNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            relationship.Properties[CatchAllKey(attribute)] = BpmnImportInternals.StringElement(attribute.Value);
        }
    }

    private string CatchAllKey(XAttribute attribute)
    {
        if (attribute.Name.NamespaceName.Length == 0)
        {
            return "bpmn:" + attribute.Name.LocalName;
        }

        // Known tool dialects normalize to their canonical lowercase prefix regardless of the
        // prefix spelling the source document declared (e.g. a "Bizagi:"-prefixed attribute becomes
        // the 'bizagi:property' key — converter design §6); unknown namespaces keep the document's
        // declared prefix when one is registered, otherwise the neutral 'bpmn:' bucket.
        var canonical = CanonicalToolPrefix(attribute.Name.NamespaceName);
        if (canonical is not null)
        {
            return canonical + ":" + attribute.Name.LocalName;
        }

        return _namespacePrefixes.TryGetValue(attribute.Name.NamespaceName, out var prefix)
            ? prefix + ":" + attribute.Name.LocalName
            : "bpmn:" + attribute.Name.LocalName;
    }

    private static string? CanonicalToolPrefix(string namespaceUri) => namespaceUri switch
    {
        BpmnCamundaToolExtension.NamespaceUriConstant => "camunda",
        BpmnSignavioToolExtension.NamespaceUriConstant => "signavio",
        BpmnBizagiToolExtension.NamespaceUriConstant => "bizagi",
        _ => null,
    };

    /// <summary>
    /// The process element IS the model: its own attributes (<c>isExecutable</c>,
    /// <c>camunda:versionTag</c>, <c>camunda:historyTimeToLive</c>, …) are preserved verbatim in
    /// the model properties via the same catch-all the elements use (finding 5), so the exporter
    /// can write them back onto <c>&lt;process&gt;</c>.
    /// </summary>
    private void ApplyProcessCatchAllAttributes(XElement process, ModelingModelDto model)
    {
        foreach (var attribute in process.Attributes())
        {
            if (attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            if (attribute.Name.NamespaceName.Length == 0
                && attribute.Name.LocalName is "id" or "name")
            {
                continue;
            }

            if (string.Equals(attribute.Name.NamespaceName, BpmnConstants.ModelNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            model.Properties[CatchAllKey(attribute)] = BpmnImportInternals.StringElement(attribute.Value);
        }
    }

    /// <summary>Attributes the element catch-all skips but whose value the mapping still preserves explicitly.</summary>
    private void ApplyExplicitMappedAttributes(XElement source, ModelingElementDto node)
    {
        switch (source.Name.LocalName)
        {
            case "callActivity":
                CopyRawAttribute(source, node, "calledElement");
                // Legacy Camunda dialect attributes (unprefixed in old exports): preserve them
                // under the Camunda namespace key — the exporter writes them back namespaced so
                // schema-validating consumers accept the document (bpmn-moddle interop).
                CopyRawAttribute(source, node, "calledElementBinding", CamundaNamespace);
                CopyRawAttribute(source, node, "calledElementVersion", CamundaNamespace);
                break;
            case "exclusiveGateway":
            case "parallelGateway":
            case "inclusiveGateway":
            case "complexGateway":
            case "eventBasedGateway":
                CopyRawAttribute(source, node, "default");
                break;
        }
    }

    private const string CamundaNamespace = "http://camunda.org/schema/1.0/bpmn";

    private static void CopyRawAttribute(XElement source, ModelingElementDto node, string localName, string? namespaceOverride = null)
    {
        if (source.Attribute(localName) is { } attribute)
        {
            var key = namespaceOverride is null
                ? "bpmn:" + localName
                : CatchAllKeyForNamespace(namespaceOverride, localName);
            node.Properties[key] = BpmnImportInternals.StringElement(attribute.Value);
        }
    }

    private static string CatchAllKeyForNamespace(string namespaceUri, string localName) =>
        namespaceUri == CamundaNamespace ? "camunda:" + localName : "bpmn:" + localName;

    private bool IsConsumedAttribute(XElement source, XAttribute attribute)
    {
        if (attribute.Name.NamespaceName.Length == 0)
        {
            if (attribute.Name.LocalName is "id" or "name")
            {
                return true;
            }

            // Attributes the mapping consumes explicitly (typed elsewhere or skipped by design) —
            // the catch-all must not duplicate them under a "bpmn:" key.
            return source.Name.LocalName switch
            {
                "boundaryEvent" => attribute.Name.LocalName is "attachedToRef" or "cancelActivity",
                "participant" => attribute.Name.LocalName is "processRef",
                "callActivity" => attribute.Name.LocalName is "calledElement" or "calledElementBinding" or "calledElementVersion",
                "dataObjectReference" => attribute.Name.LocalName is "dataObjectRef" or "isCollection",
                "dataStoreReference" => attribute.Name.LocalName is "dataStoreRef",
                "group" => attribute.Name.LocalName is "categoryValueRef",
                "subProcess" or "adHocSubProcess" or "transaction" => attribute.Name.LocalName is "triggeredByEvent" or "adHoc",
                _ => IsGatewayLocalName(source.Name.LocalName) && attribute.Name.LocalName == "default",
            };
        }

        return string.Equals(attribute.Name.NamespaceName, BpmnConstants.ModelNamespace, StringComparison.Ordinal);
    }

    private static bool IsGatewayLocalName(string localName) =>
        localName is "exclusiveGateway" or "parallelGateway" or "inclusiveGateway" or "complexGateway" or "eventBasedGateway";

    private ModelingRelationshipDto CreateRelationship(XElement source, string relationshipType, string sourceElementId,
        string targetElementId, ModelingModelDto model)
    {
        var relationship = new ModelingRelationshipDto
        {
            Id = RequiredId(source, source.Name.LocalName),
            SourceId = source.Attribute("id")?.Value ?? string.Empty,
            SourceType = source.Name.LocalName,
            SourceElementId = sourceElementId,
            TargetElementId = targetElementId,
            RelationshipType = relationshipType,
            Name = source.Attribute("name")?.Value ?? string.Empty,
        };
        ApplyCatchAllAttributes(source, relationship);
        model.Relationships.Add(relationship);
        _relationshipIndex[relationship.Id] = (model, relationship);
        return relationship;
    }

    private string RequiredId(XElement element, string fallbackType)
    {
        var id = element.Attribute("id")?.Value;
        return string.IsNullOrWhiteSpace(id)
            ? BpmnImportInternals.StableId(fallbackType, BpmnImportInternals.BuildPath(element))
            : id!;
    }

    private void AddLoss(XElement element, string construct, string reason,
        BpmnImportLossSeverity severity = BpmnImportLossSeverity.Loss, string? elementId = null)
    {
        _losses.Add(new BpmnImportLoss
        {
            ElementId = elementId ?? element.Attribute("id")?.Value,
            XmlPath = BpmnImportInternals.BuildPath(element),
            Construct = construct,
            Reason = reason,
            Severity = severity,
        });
    }

    private void CollectNamespacePrefixes(XElement root)
    {
        foreach (var attribute in root.Attributes())
        {
            if (!attribute.IsNamespaceDeclaration)
            {
                continue;
            }

            var ns = attribute.Value;
            if (string.Equals(ns, BpmnConstants.ModelNamespace, StringComparison.Ordinal)
                || string.Equals(ns, BpmnConstants.DiagramNamespace, StringComparison.Ordinal)
                || string.Equals(ns, BpmnConstants.DCNamespace, StringComparison.Ordinal)
                || string.Equals(ns, BpmnConstants.DINamespace, StringComparison.Ordinal)
                || string.Equals(ns, HostExtensionNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            _namespacePrefixes.TryAdd(ns, attribute.Name.LocalName);
        }
    }

    private static string StripRef(string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : BpmnImportInternals.StripQNamePrefix(value.Trim());
}
