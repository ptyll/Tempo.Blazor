using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Element/relationship writing half of <see cref="BpmnExportBuilder"/>: semantic type → BPMN local
/// name, native attribute/child write-back (<c>bpmn:*</c> catch-all, event definitions, conditions,
/// data associations, lanes), host extension payloads and the tool export hooks.
/// </summary>
internal sealed partial class BpmnExportBuilder
{
    /// <summary>Property keys with a native BPMN representation — never serialized into host extensions.</summary>
    private static readonly HashSet<string> NativelyHandledProperties = new(StringComparer.Ordinal)
    {
        "attachedToRef", "cancelActivity", "eventDefinitions", "multiple", "parallelMultiple",
        "isForCompensation", "transaction", "completionCondition", "dataObjectRef", "isCollection",
        "dataStoreRef", "dataStoreName", "categoryValueRef", "categoryValue", "condition",
        "conditionLanguage", "direction", "assignments", "externalModelRef", "foreignExtensions",
        "bpmnDefinitions", "bpmn:associationDirection", "bpmn:calledElement",
        // Carried in the host payload from the typed DTO fields (finding 9), not from loose properties.
        "tags", "governance",
        // Written natively from the boolean property; the bpmn:* string twin must not double the attribute.
        "bpmn:multiple", "bpmn:parallelMultiple", "bpmn:isForCompensation",
    };

    /// <summary>
    /// Attribute names the exporter writes natively from typed properties — a <c>bpmn:*</c>
    /// catch-all key with one of these local names is skipped so the attribute is never written
    /// twice (which would throw) (finding 6).
    /// </summary>
    private static readonly HashSet<string> NativelyWrittenAttributeNames = new(StringComparer.Ordinal)
    {
        "attachedToRef", "cancelActivity", "multiple", "parallelMultiple", "isForCompensation",
        "dataObjectRef", "isCollection", "dataStoreRef", "categoryValueRef", "triggeredByEvent",
    };

    private static readonly HashSet<string> KnownEventDefinitionTypes = new(StringComparer.Ordinal)
    {
        "message", "signal", "error", "escalation", "timer", "conditional",
        "compensate", "link", "terminate", "cancel", "multiple", "parallelMultiple",
    };

    private static bool IsValidNCName(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        try
        {
            System.Xml.XmlConvert.VerifyNCName(value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Strips characters XML 1.0 cannot represent; the loss is reported as Approximated (finding 6).</summary>
    private string SanitizeXmlText(string value, string? elementId, string construct)
    {
        var invalid = 0;
        foreach (var ch in value)
        {
            if (!System.Xml.XmlConvert.IsXmlChar(ch))
            {
                invalid++;
            }
        }

        if (invalid == 0)
        {
            return value;
        }

        AddLoss(elementId, construct,
            "Characters that are not valid in XML 1.0 were stripped from the exported text.",
            BpmnExportLossSeverity.Approximated);
        return string.Concat(value.Where(System.Xml.XmlConvert.IsXmlChar));
    }

    private static readonly Dictionary<string, string> FlowNodeLocalNames = new(StringComparer.Ordinal)
    {
        [BpmnSemanticTypes.Task] = "task",
        [BpmnSemanticTypes.UserTask] = "userTask",
        [BpmnSemanticTypes.ServiceTask] = "serviceTask",
        [BpmnSemanticTypes.SendTask] = "sendTask",
        [BpmnSemanticTypes.ReceiveTask] = "receiveTask",
        [BpmnSemanticTypes.ScriptTask] = "scriptTask",
        [BpmnSemanticTypes.BusinessRuleTask] = "businessRuleTask",
        [BpmnSemanticTypes.ManualTask] = "manualTask",
        [BpmnSemanticTypes.CallActivity] = "callActivity",
        [BpmnSemanticTypes.StartEvent] = "startEvent",
        [BpmnSemanticTypes.EndEvent] = "endEvent",
        [BpmnSemanticTypes.IntermediateCatchEvent] = "intermediateCatchEvent",
        [BpmnSemanticTypes.IntermediateThrowEvent] = "intermediateThrowEvent",
        [BpmnSemanticTypes.BoundaryEvent] = "boundaryEvent",
        [BpmnSemanticTypes.ExclusiveGateway] = "exclusiveGateway",
        [BpmnSemanticTypes.ParallelGateway] = "parallelGateway",
        [BpmnSemanticTypes.InclusiveGateway] = "inclusiveGateway",
        [BpmnSemanticTypes.ComplexGateway] = "complexGateway",
        [BpmnSemanticTypes.EventBasedGateway] = "eventBasedGateway",
    };

    private void WriteProcess(ProcessPlan plan)
    {
        var process = new XElement(Ns + "process", new XAttribute("id", plan.Id));
        if (plan.Name.Length > 0)
        {
            process.Add(new XAttribute("name", SanitizeXmlText(plan.Name, null, "Process")));
        }

        WriteProcessAttributes(process, plan.Model);

        if (plan.PoolId.Length > 0)
        {
            AttachProcessRef(plan.PoolId, plan.Id);
        }

        var model = plan.Model;
        var lanes = model.Elements
            .Where(element => IsLane(element) && IsDirectChild(element, plan.RootParentId)
                && ReferenceEquals(_elements.GetValueOrDefault(element.Id), element))
            .ToList();
        if (lanes.Count > 0)
        {
            process.Add(WriteLaneSet(plan, lanes, isChildLaneSet: false));
        }

        WriteSynthesizedDataObjects(plan, process);

        // Flow nodes owned by a lane are still process-level children in BPMN — the laneSet only
        // REFERENCES them via flowNodeRef (see WriteLaneSet).
        foreach (var element in model.Elements)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (IsLane(element) || IsPool(element)
                || !ReferenceEquals(_elements.GetValueOrDefault(element.Id), element)
                || !string.Equals(FlowContainer(element), plan.RootParentId, StringComparison.Ordinal))
            {
                continue;
            }

            var node = WriteFlowElement(plan, element);
            if (node is not null)
            {
                process.Add(node);
            }
        }

        foreach (var relationship in model.Relationships)
        {
            if (!ReferenceEquals(_relationships.GetValueOrDefault(relationship.Id), relationship))
            {
                continue;
            }

            if (IsProcessLevelFlow(relationship, plan))
            {
                process.Add(WriteFlowRelationship(relationship));
            }
        }

        _root.Add(process);
    }

    /// <summary>
    /// Writes the <c>process</c> element's own attributes from the model properties the import
    /// catch-all preserved (<c>bpmn:isExecutable</c>, <c>camunda:versionTag</c>, … — finding 5).
    /// <c>isExecutable</c> always appears and defaults to <c>false</c>.
    /// </summary>
    private void WriteProcessAttributes(XElement process, ModelingModelDto model)
    {
        var isExecutableWritten = false;
        if (model.Properties.TryGetValue("bpmn:isExecutable", out var isExecutable))
        {
            process.Add(new XAttribute("isExecutable", PropertyText(isExecutable)));
            isExecutableWritten = true;
        }

        foreach (var (key, value) in model.Properties)
        {
            if (string.Equals(key, "bpmnDefinitions", StringComparison.Ordinal)
                || string.Equals(key, "bpmn:isExecutable", StringComparison.Ordinal))
            {
                continue;
            }

            if (key.StartsWith("bpmn:", StringComparison.Ordinal))
            {
                var localName = key["bpmn:".Length..];
                if (NativelyWrittenAttributeNames.Contains(localName))
                {
                    continue;
                }

                if (!IsValidNCName(localName))
                {
                    AddLoss(null, "Property",
                        $"The model property '{key}' is not a valid XML attribute name and cannot be written on the process element.");
                    continue;
                }

                process.Add(new XAttribute(localName, SanitizeXmlText(PropertyText(value), null, "Property")));
                continue;
            }

            var prefix = KeyPrefix(key);
            if (prefix is not null)
            {
                var localName = key[(prefix.Length + 1)..];
                if (!IsValidNCName(prefix) || !IsValidNCName(localName))
                {
                    AddLoss(null, "Property",
                        $"The model property '{key}' is not a valid XML attribute name and cannot be written on the process element.");
                    continue;
                }

                DeclareExtensionNamespace(prefix, ExtensionNamespaceUri(prefix));
                process.Add(new XAttribute(
                    XNamespace.Get(ExtensionNamespaceUri(prefix)) + localName,
                    SanitizeXmlText(PropertyText(value), null, "Property")));
            }

            // Plain (colon-less) model properties stay on the definitions root host payload
            // (WriteRootHostExtensions).
        }

        if (!isExecutableWritten)
        {
            process.Add(new XAttribute("isExecutable", "false"));
        }
    }

    /// <summary>
    /// One <c>&lt;dataObject id name isCollection&gt;</c> per distinct <c>dataObjectRef</c> the
    /// process's references point at, so no reference dangles (finding 4).
    /// </summary>
    private void WriteSynthesizedDataObjects(ProcessPlan plan, XElement process)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in plan.Model.Elements)
        {
            if (!string.Equals(element.SemanticType, BpmnSemanticTypes.DataObject, StringComparison.Ordinal)
                || string.Equals(element.SourceType, "dataObject", StringComparison.Ordinal)
                || !TryGetString(element.Properties, "dataObjectRef", out var reference)
                || reference.Length == 0
                || !seen.Add(reference))
            {
                continue;
            }

            var dataObject = new XElement(Ns + "dataObject", new XAttribute("id", reference));
            if (element.Name.Length > 0)
            {
                dataObject.Add(new XAttribute("name", SanitizeXmlText(element.Name, element.Id, "DataObject")));
            }

            if (element.Properties.TryGetValue("isCollection", out var isCollection)
                && isCollection.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                dataObject.Add(new XAttribute("isCollection", isCollection.GetBoolean() ? "true" : "false"));
            }

            process.Add(dataObject);
        }
    }

    private static bool IsDirectChild(ModelingElementDto element, string rootParentId) =>
        (element.ParentId.Length == 0 && rootParentId.Length == 0)
        || string.Equals(element.ParentId, rootParentId, StringComparison.Ordinal)
        // A model authored without pool hierarchy still belongs to the (first) pooled process.
        || (rootParentId.Length > 0 && element.ParentId.Length == 0);

    private static bool IsLane(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.Lane, StringComparison.Ordinal);

    private static bool IsPool(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.Pool, StringComparison.Ordinal);

    private static bool IsSubProcessLike(ModelingElementDto element) =>
        string.Equals(element.SemanticType, BpmnSemanticTypes.SubProcess, StringComparison.Ordinal)
        || string.Equals(element.SemanticType, BpmnSemanticTypes.EventSubProcess, StringComparison.Ordinal);

    private XElement WriteLaneSet(ProcessPlan plan, List<ModelingElementDto> lanes, bool isChildLaneSet)
    {
        var laneSet = new XElement(Ns + (isChildLaneSet ? "childLaneSet" : "laneSet"));
        foreach (var lane in lanes)
        {
            var laneElement = new XElement(Ns + "lane", new XAttribute("id", lane.Id));
            if (lane.Name.Length > 0)
            {
                laneElement.Add(new XAttribute("name", lane.Name));
            }

            WriteCatchAllAttributes(laneElement, lane);
            foreach (var node in ChildrenOf(lane.Id).Where(candidate =>
                         !IsLane(candidate) && ReferenceEquals(_elements.GetValueOrDefault(candidate.Id), candidate)))
            {
                laneElement.Add(new XElement(Ns + "flowNodeRef", node.Id));
            }

            var childLanes = ChildrenOf(lane.Id).Where(IsLane).ToList();
            if (childLanes.Count > 0)
            {
                laneElement.Add(WriteLaneSet(plan, childLanes, isChildLaneSet: true));
            }

            laneSet.Add(laneElement);
        }

        return laneSet;
    }

    private XElement? WriteFlowElement(ProcessPlan plan, ModelingElementDto element)
    {
        var localName = LocalNameFor(element);
        if (localName is null)
        {
            AddLoss(element.Id, element.SemanticType,
                $"The semantic type '{element.SemanticType}' has no BPMN 2.0 XML representation; the element is not exported.");
            return null;
        }

        var node = new XElement(Ns + localName, new XAttribute("id", element.Id));
        var isTextAnnotation = string.Equals(element.SemanticType, BpmnSemanticTypes.TextAnnotation, StringComparison.Ordinal);
        if (element.Name.Length > 0 && !isTextAnnotation)
        {
            node.Add(new XAttribute("name", SanitizeXmlText(element.Name, element.Id, element.SemanticType)));
        }

        WriteFlowWiring(node, element);
        WriteMappedAttributes(node, element);
        WriteDocumentation(node, element.Description, element.Id);
        WriteExtensionElements(node, element);

        if (IsEvent(element))
        {
            WriteEventDefinitions(node, element);
        }

        if (IsSubProcessLike(element))
        {
            WriteSubProcessChildren(plan, node, element);
        }

        WriteActivityChildren(plan, node, element);
        return node;
    }

    /// <summary>
    /// Camunda-style <c>&lt;incoming&gt;</c>/<c>&lt;outgoing&gt;</c> wiring on flow nodes
    /// (finding 12); sequence flows only, derived from the pre-indexed relationship maps.
    /// </summary>
    private void WriteFlowWiring(XElement node, ModelingElementDto element)
    {
        if (!FlowNodeLocalNames.ContainsKey(element.SemanticType))
        {
            return;
        }

        if (_relationshipsByTarget.TryGetValue(element.Id, out var incoming))
        {
            foreach (var relationship in incoming.Where(IsFlowWiring).OrderBy(relationship => relationship.Id, StringComparer.Ordinal))
            {
                node.Add(new XElement(Ns + "incoming", relationship.Id));
            }
        }

        if (_relationshipsBySource.TryGetValue(element.Id, out var outgoing))
        {
            foreach (var relationship in outgoing.Where(IsFlowWiring).OrderBy(relationship => relationship.Id, StringComparer.Ordinal))
            {
                node.Add(new XElement(Ns + "outgoing", relationship.Id));
            }
        }
    }

    private static bool IsFlowWiring(ModelingRelationshipDto relationship) =>
        relationship.RelationshipType is BpmnRelationshipTypes.SequenceFlow
            or BpmnRelationshipTypes.ConditionalFlow or BpmnRelationshipTypes.DefaultFlow;

    private string? LocalNameFor(ModelingElementDto element) => element.SemanticType switch
    {
        _ when FlowNodeLocalNames.TryGetValue(element.SemanticType, out var flowNode) => flowNode,
        var type when string.Equals(type, BpmnSemanticTypes.SubProcess, StringComparison.Ordinal) =>
            element.Properties.ContainsKey("transaction") ? "transaction"
                : element.Properties.ContainsKey("completionCondition") || element.Properties.ContainsKey("bpmn:ordering")
                    ? "adHocSubProcess"
                    : "subProcess",
        var type when string.Equals(type, BpmnSemanticTypes.EventSubProcess, StringComparison.Ordinal) => "subProcess",
        var type when string.Equals(type, BpmnSemanticTypes.DataObject, StringComparison.Ordinal) =>
            string.Equals(element.SourceType, "dataObject", StringComparison.Ordinal) ? "dataObject" : "dataObjectReference",
        var type when string.Equals(type, BpmnSemanticTypes.DataStore, StringComparison.Ordinal) => "dataStoreReference",
        var type when string.Equals(type, BpmnSemanticTypes.TextAnnotation, StringComparison.Ordinal) => "textAnnotation",
        var type when string.Equals(type, BpmnSemanticTypes.Group, StringComparison.Ordinal) => "group",
        _ => null,
    };

    private static bool IsEvent(ModelingElementDto element) =>
        element.SemanticType is BpmnSemanticTypes.StartEvent or BpmnSemanticTypes.EndEvent
            or BpmnSemanticTypes.IntermediateCatchEvent or BpmnSemanticTypes.IntermediateThrowEvent
            or BpmnSemanticTypes.BoundaryEvent;

    private void WriteMappedAttributes(XElement node, ModelingElementDto element)
    {
        if (string.Equals(element.SemanticType, BpmnSemanticTypes.CallActivity, StringComparison.Ordinal)
            && TryGetString(element.Properties, "bpmn:calledElement", out var calledElement))
        {
            // Written first, before the catch-all pass, so the attribute position is stable
            // across import → export → import (the explicit mapping appends it last).
            node.Add(new XAttribute("calledElement", calledElement));
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.BoundaryEvent, StringComparison.Ordinal))
        {
            if (TryGetString(element.Properties, "attachedToRef", out var attachedTo))
            {
                node.Add(new XAttribute("attachedToRef", attachedTo));
            }

            if (element.Properties.TryGetValue("cancelActivity", out var cancelActivity)
                && cancelActivity.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                node.Add(new XAttribute("cancelActivity", cancelActivity.GetBoolean() ? "true" : "false"));
            }
        }

        if (IsEvent(element))
        {
            // parallelMultiple is a schema attribute; a "multiple" trigger is expressed as a
            // <multipleEventDefinition> child (see WriteEventDefinitions) — the strict BPMN
            // schema has no `multiple` attribute (bpmn-moddle interop).
            WriteBooleanAttribute(node, element.Properties, "parallelMultiple", "bpmn:parallelMultiple");
        }

        if (element.Properties.TryGetValue("bpmn:isForCompensation", out var rawCompensation)
            && rawCompensation.ValueKind == JsonValueKind.String)
        {
            node.Add(new XAttribute("isForCompensation", rawCompensation.GetString() ?? string.Empty));
        }
        else if (element.Properties.TryGetValue("isForCompensation", out var compensation)
            && compensation.ValueKind == JsonValueKind.True)
        {
            node.Add(new XAttribute("isForCompensation", "true"));
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.DataObject, StringComparison.Ordinal))
        {
            if (!string.Equals(element.SourceType, "dataObject", StringComparison.Ordinal)
                && TryGetString(element.Properties, "dataObjectRef", out var dataObjectRef))
            {
                node.Add(new XAttribute("dataObjectRef", dataObjectRef));
            }

            // isCollection belongs on the raw dataObject declaration (and on the synthesized
            // <dataObject>, finding 4); the strict schema rejects it on dataObjectReference.
            if (string.Equals(element.SourceType, "dataObject", StringComparison.Ordinal)
                && element.Properties.TryGetValue("isCollection", out var isCollection)
                && isCollection.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                node.Add(new XAttribute("isCollection", isCollection.GetBoolean() ? "true" : "false"));
            }
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.Group, StringComparison.Ordinal)
            && TryGetString(element.Properties, "categoryValueRef", out var categoryValueRef))
        {
            node.Add(new XAttribute("categoryValueRef", categoryValueRef));
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.DataStore, StringComparison.Ordinal)
            && TryGetString(element.Properties, "dataStoreRef", out var dataStoreRef))
        {
            node.Add(new XAttribute("dataStoreRef", dataStoreRef));
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.EventSubProcess, StringComparison.Ordinal))
        {
            node.Add(new XAttribute("triggeredByEvent", "true"));
        }

        WriteCatchAllAttributes(node, element);
    }

    /// <summary>
    /// Boolean event attributes: the importer keeps both a typed property and a verbatim
    /// <c>bpmn:*</c> catch-all string — the string wins so the re-imported model keeps both.
    /// </summary>
    private static void WriteBooleanAttribute(XElement node, Dictionary<string, JsonElement> properties,
        string booleanKey, string catchAllKey)
    {
        if (TryGetString(properties, catchAllKey, out var raw))
        {
            node.Add(new XAttribute(catchAllKey["bpmn:".Length..], raw));
        }
        else if (properties.TryGetValue(booleanKey, out var value) && value.ValueKind == JsonValueKind.True)
        {
            node.Add(new XAttribute(booleanKey, "true"));
        }
    }

    private void WriteCatchAllAttributes(XElement node, ModelingElementDto element)
    {
        foreach (var (key, value) in element.Properties)
        {
            if (NativelyHandledProperties.Contains(key)
                || key.StartsWith("bpmndi:", StringComparison.Ordinal)
                || key.StartsWith(_options.HostExtensionPrefix + ":", StringComparison.Ordinal))
            {
                continue;
            }

            WritePrefixedPropertyAttribute(node, element.Id, key, value);
        }
    }

    /// <summary>
    /// Writes one <c>bpmn:*</c> / <c>{prefix}:*</c> property as an XML attribute, guarding the
    /// NCName validity (an invalid name is a reported loss instead of an exception — finding 6)
    /// and skipping the names <see cref="WriteMappedAttributes"/> already wrote natively.
    /// </summary>
    private void WritePrefixedPropertyAttribute(XElement node, string? elementId, string key, JsonElement value)
    {
        if (key.StartsWith("bpmn:", StringComparison.Ordinal))
        {
            var localName = key["bpmn:".Length..];
            if (NativelyWrittenAttributeNames.Contains(localName))
            {
                return;
            }

            if (!IsValidNCName(localName))
            {
                AddLoss(elementId, "Property",
                    $"The property '{key}' is not a valid XML attribute name and cannot be written on the element.");
                return;
            }

            // Legacy Camunda dialect attributes (unprefixed in old exports): the strict BPMN
            // schema only knows them in the Camunda namespace — write them there (declared on
            // the root) so schema-validating consumers accept the document (bpmn-moddle interop).
            if (localName is "calledElementBinding" or "calledElementVersion")
            {
                DeclareExtensionNamespace("camunda", ExtensionNamespaceUri("camunda"));
                node.Add(new XAttribute(
                    XNamespace.Get(ExtensionNamespaceUri("camunda")) + localName,
                    SanitizeXmlText(PropertyText(value), elementId, "Property")));
                return;
            }

            node.Add(new XAttribute(localName, SanitizeXmlText(PropertyText(value), elementId, "Property")));
            return;
        }

        var prefix = KeyPrefix(key);
        if (prefix is not null)
        {
            var localName = key[(prefix.Length + 1)..];
            if (!IsValidNCName(prefix) || !IsValidNCName(localName))
            {
                AddLoss(elementId, "Property",
                    $"The property '{key}' is not a valid XML attribute name and cannot be written on the element.");
                return;
            }

            DeclareExtensionNamespace(prefix, ExtensionNamespaceUri(prefix));
            node.Add(new XAttribute(
                XNamespace.Get(ExtensionNamespaceUri(prefix)) + localName,
                SanitizeXmlText(PropertyText(value), elementId, "Property")));
        }

        // A plain (colon-less) property without a native representation: WithHostExtensions
        // carries it in the host payload (see WriteExtensionElements); Standard drops it with
        // a loss recorded there.
    }

    private static string? KeyPrefix(string key)
    {
        var colon = key.IndexOf(':');
        return colon > 0 ? key[..colon] : null;
    }

    private static string PropertyText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();

    private string ExtensionNamespaceUri(string prefix) =>
        _options.ExtensionNamespaces.TryGetValue(prefix, out var uri)
            ? uri
            : "urn:tempo-blazor:bpmn:" + prefix;

    /// <summary>
    /// Declares <paramref name="uri"/> on the document root under <paramref name="prefix"/>,
    /// renaming the prefix deterministically (<c>{prefix}_1</c>, …) when it collides with a
    /// reserved prefix (<c>bpmndi</c>/<c>dc</c>/<c>di</c>/<c>xsi</c>/<c>xml</c>/host) or a
    /// namespace already declared under it (finding 8). Returns the prefix the namespace
    /// ended up with.
    /// </summary>
    private string DeclareExtensionNamespace(string prefix, string uri)
    {
        if (_prefixByUri.TryGetValue(uri, out var existing))
        {
            return existing;
        }

        var resolved = ResolvePrefixCollision(prefix);
        _prefixByUri[uri] = resolved;
        _root.Add(new XAttribute(XNamespace.Xmlns + resolved, uri));

        if (!string.Equals(resolved, prefix, StringComparison.Ordinal))
        {
            AddLoss(null, "Namespace",
                $"The attribute prefix '{prefix}' is reserved or already declared for another namespace; the URI was declared as '{resolved}' instead.",
                BpmnExportLossSeverity.Info);
        }

        if (!_options.ExtensionNamespaces.TryGetValue(prefix, out var knownUri)
            || !string.Equals(knownUri, uri, StringComparison.Ordinal))
        {
            AddLoss(null, "Namespace",
                $"No namespace URI is known for the '{prefix}' attribute prefix; declared deterministically as '{uri}'.",
                BpmnExportLossSeverity.Info);
        }

        return resolved;
    }

    private string ResolvePrefixCollision(string prefix)
    {
        if (!ReservedPrefixes.Contains(prefix) && _root.Attribute(XNamespace.Xmlns + prefix) is null)
        {
            return prefix;
        }

        for (var index = 1; ; index++)
        {
            var candidate = prefix + "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ReservedPrefixes.Contains(candidate) && _root.Attribute(XNamespace.Xmlns + candidate) is null)
            {
                return candidate;
            }
        }
    }

    private void WriteDocumentation(XElement node, string description, string elementId)
    {
        if (description.Length > 0)
        {
            node.Add(new XElement(Ns + "documentation",
                SanitizeXmlText(description, elementId, "Documentation")));
        }
    }

    // ------------------------------------------------------------------------------ extensions

    private void WriteExtensionElements(XElement node, ModelingElementDto element)
    {
        var extension = new XElement(Ns + "extensionElements");

        if (element.Properties.TryGetValue("foreignExtensions", out var foreign)
            && foreign.ValueKind == JsonValueKind.Object)
        {
            foreach (var payload in EnumerateObject(foreign).OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                if (payload.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                try
                {
                    // The importer accumulates every payload of one namespace into a single
                    // concatenated string, so parse behind a throwaway wrapper to allow siblings.
                    var wrapper = XElement.Parse("<wrapper>" + payload.Value.GetString() + "</wrapper>");
                    extension.Add(wrapper.Elements());
                }
                catch (Exception)
                {
                    AddLoss(element.Id, "foreignExtensions",
                        $"A preserved foreign extension payload of namespace '{payload.Key}' is not well-formed XML and cannot be written back.");
                }
            }
        }

        if (_options.Profile == BpmnExportProfile.WithHostExtensions)
        {
            var properties = HostExtensionProperties(element.Properties);
            if (element.Tags.Count > 0)
            {
                properties.Add(new KeyValuePair<string, JsonElement>(
                    "tags", JsonSerializer.SerializeToElement(element.Tags)));
            }

            if (HasGovernance(element.Governance))
            {
                properties.Add(new KeyValuePair<string, JsonElement>(
                    "governance", JsonSerializer.SerializeToElement(element.Governance)));
            }

            if (properties.Count > 0)
            {
                extension.Add(HostPropertiesElement(properties));
            }
        }
        else
        {
            foreach (var pair in HostExtensionProperties(element.Properties))
            {
                AddLoss(element.Id, "Property",
                    $"The property '{pair.Key}' has no BPMN 2.0 representation and the Standard profile writes no host extensions; the value is not preserved.");
            }

            if (element.Tags.Count > 0)
            {
                AddLoss(element.Id, "Tags",
                    "Element tags have no BPMN 2.0 representation and the Standard profile writes no host extensions; they are not preserved.");
            }

            if (HasGovernance(element.Governance))
            {
                AddLoss(element.Id, "Governance",
                    "Element governance metadata has no BPMN 2.0 representation and the Standard profile writes no host extensions; it is not preserved.");
            }
        }

        foreach (var plugin in _options.ToolExtensions)
        {
            plugin.OnExportElement(new BpmnToolExportContext
            {
                Element = element,
                ExtensionElements = extension,
                ReportLoss = reason => AddLoss(element.Id, plugin.NamespaceUri, reason),
            });
        }

        if (!extension.Elements().Any())
        {
            return;
        }

        // Children the hooks/payloads appended may live in their own namespaces; declare the
        // prefix mapping on the root so the re-import reproduces the property keys.
        foreach (var child in extension.DescendantsAndSelf())
        {
            if (child.Name.NamespaceName.Length > 0
                && child.Name.NamespaceName != BpmnConstants.ModelNamespace
                && child.Name.NamespaceName != _options.HostExtensionNamespace)
            {
                var uri = child.Name.NamespaceName;
                DeclareExtensionNamespace(PrefixFor(uri), uri);
            }
        }

        // Namespace declarations inside preserved payloads are dropped once the same URI is
        // declared on the document root — repeated round trips must not accumulate xmlns
        // declarations (finding 8).
        foreach (var descendant in extension.DescendantsAndSelf().ToList())
        {
            foreach (var declaration in descendant.Attributes()
                         .Where(attribute => attribute.IsNamespaceDeclaration)
                         .ToList())
            {
                var declaredAtRoot = _root.Attributes()
                    .Any(attribute => attribute.IsNamespaceDeclaration && attribute.Value == declaration.Value);
                if (declaredAtRoot)
                {
                    declaration.Remove();
                }
            }
        }

        node.Add(extension);
    }

    private string PrefixFor(string namespaceUri)
    {
        if (namespaceUri == _options.HostExtensionNamespace)
        {
            return _options.HostExtensionPrefix;
        }

        foreach (var (prefix, uri) in _options.ExtensionNamespaces)
        {
            if (string.Equals(uri, namespaceUri, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        // Deterministic generated prefix (stable FNV-1a hash — no GetHashCode), guarded against
        // reserved-prefix collisions by DeclareExtensionNamespace (finding 8).
        return BpmnImportInternals.StableId("ns", namespaceUri);
    }

    private static bool HasGovernance(ModelingGovernanceDto governance) =>
        governance.TrustLevel.Length > 0 || governance.ReviewState.Length > 0
        || governance.SyncState.Length > 0 || governance.DataSource.Length > 0;

    /// <summary>Element properties that have no native BPMN representation and go to the host payload.</summary>
    private List<KeyValuePair<string, JsonElement>> HostExtensionProperties(Dictionary<string, JsonElement> properties)
    {
        var leftover = new List<KeyValuePair<string, JsonElement>>();
        foreach (var pair in properties)
        {
            if (NativelyHandledProperties.Contains(pair.Key)
                || pair.Key.StartsWith("bpmn:", StringComparison.Ordinal)
                || pair.Key.StartsWith("bpmndi:", StringComparison.Ordinal)
                || KeyPrefix(pair.Key) is not null)
            {
                continue;
            }

            leftover.Add(pair);
        }

        return leftover.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();
    }

    private XElement HostPropertiesElement(List<KeyValuePair<string, JsonElement>> properties)
    {
        var hostNamespace = XNamespace.Get(_options.HostExtensionNamespace);
        var host = new XElement(hostNamespace + "properties");
        foreach (var (key, value) in properties)
        {
            host.Add(new XElement(hostNamespace + "property",
                new XAttribute("name", key),
                new XAttribute("value", CanonicalJson(value))));
        }

        return host;
    }

    private void WriteRootHostExtensions()
    {
        var leftover = new List<KeyValuePair<string, JsonElement>>();
        foreach (var model in _models)
        {
            foreach (var pair in model.Properties)
            {
                if (string.Equals(pair.Key, "bpmnDefinitions", StringComparison.Ordinal))
                {
                    continue;
                }

                if (pair.Key.StartsWith("bpmn:", StringComparison.Ordinal) || KeyPrefix(pair.Key) is not null)
                {
                    // Prefixed model properties (bpmn:*, camunda:*, …) are written on the model's
                    // <process> element by WriteProcessAttributes (finding 5).
                    continue;
                }

                if (_options.Profile == BpmnExportProfile.WithHostExtensions)
                {
                    leftover.Add(pair);
                }
                else
                {
                    AddLoss(null, "Property",
                        $"The model-level property '{pair.Key}' has no BPMN representation and the Standard profile writes no host extensions; the value is not preserved.");
                }
            }
        }

        if (leftover.Count == 0)
        {
            return;
        }

        var extension = new XElement(Ns + "extensionElements", HostPropertiesElement(
            leftover.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList()));
        _root.AddFirst(extension);
    }

    private static string CanonicalJson(JsonElement value)
    {
        var node = JsonNode.Parse(value.GetRawText());
        return Canonicalize(node)!.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = new JsonObject();
                foreach (var pair in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    sorted[pair.Key] = Canonicalize(pair.Value);
                }

                return sorted;
            case JsonArray array:
                var sortedArray = new JsonArray();
                foreach (var item in array)
                {
                    sortedArray.Add(Canonicalize(item));
                }

                return sortedArray;
            default:
                return node?.DeepClone();
        }
    }

    private static IEnumerable<KeyValuePair<string, JsonElement>> EnumerateObject(JsonElement element)
    {
        foreach (var property in element.EnumerateObject())
        {
            yield return new KeyValuePair<string, JsonElement>(property.Name, property.Value);
        }
    }

    private static bool TryGetString(Dictionary<string, JsonElement> properties, string key, out string value)
    {
        value = string.Empty;
        if (!properties.TryGetValue(key, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString() ?? string.Empty;
        return true;
    }

    // ------------------------------------------------------------------------------ events

    private void WriteEventDefinitions(XElement node, ModelingElementDto element)
    {
        if (element.Properties.TryGetValue("eventDefinitions", out var definitions)
            && definitions.ValueKind == JsonValueKind.Array)
        {
            foreach (var definition in definitions.EnumerateArray())
            {
                if (definition.ValueKind != JsonValueKind.Object)
                {
                    AddLoss(element.Id, "eventDefinitions",
                        "An event definition entry is not a JSON object and is not exported.",
                        BpmnExportLossSeverity.Approximated);
                    continue;
                }

                var type = GetProperty(definition, "type");
                if (type is null || !KnownEventDefinitionTypes.Contains(type))
                {
                    // A missing or unknown definition type is a reported loss — never a silent
                    // <terminateEventDefinition> (finding 6).
                    AddLoss(element.Id, "eventDefinitions",
                        $"An event definition entry {(type is null ? "carries no type" : $"has the unknown type '{type}'")} and is not exported.");
                    continue;
                }

                var eventDefinition = new XElement(Ns + type + "EventDefinition");
                if (GetProperty(definition, "id") is { } id)
                {
                    eventDefinition.Add(new XAttribute("id", id));
                }

                switch (type)
                {
                    case "message":
                    case "signal":
                    case "error":
                    case "escalation":
                        if (GetProperty(definition, "ref") is { } reference)
                        {
                            eventDefinition.Add(new XAttribute(type + "Ref", reference));
                        }

                        break;
                    case "timer":
                        foreach (var kind in new[] { "timeDuration", "timeDate", "timeCycle" })
                        {
                            if (GetProperty(definition, kind) is { } time)
                            {
                                eventDefinition.Add(new XElement(Ns + kind,
                                    new XAttribute(XsiNs + "type", "tFormalExpression"),
                                    SanitizeXmlText(time, element.Id, "Property")));
                            }
                        }

                        break;
                    case "conditional":
                        if (GetProperty(definition, "condition") is { } condition)
                        {
                            eventDefinition.Add(new XElement(Ns + "condition",
                                new XAttribute(XsiNs + "type", "tFormalExpression"),
                                SanitizeXmlText(condition, element.Id, "Property")));
                        }

                        break;
                    case "compensate":
                        if (GetProperty(definition, "activityRef") is { } activityRef)
                        {
                            eventDefinition.Add(new XAttribute("activityRef", activityRef));
                        }

                        if (definition.TryGetProperty("waitForCompletion", out var wait)
                            && wait.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            eventDefinition.Add(new XAttribute("waitForCompletion", wait.GetBoolean() ? "true" : "false"));
                        }

                        break;
                    case "link":
                        if (GetProperty(definition, "name") is { } linkName)
                        {
                            eventDefinition.Add(new XAttribute("name", linkName));
                        }

                        break;
                }

                node.Add(eventDefinition);
            }
        }

        // BPMN 2.0 has NO multipleEventDefinition construct: a "multiple" trigger is expressed by
        // carrying several event definitions (and no parallelMultiple attribute). When the flag
        // cannot be represented that way, report the approximation instead of emitting a
        // schema-invalid element (bpmn-moddle interop).
        var isMultiple = element.Properties.TryGetValue("multiple", out var multipleFlag)
                         && multipleFlag.ValueKind == JsonValueKind.True;
        if (!isMultiple && TryGetString(element.Properties, "bpmn:multiple", out var rawMultiple))
        {
            isMultiple = string.Equals(rawMultiple, "true", StringComparison.Ordinal);
        }

        if (isMultiple)
        {
            var definitionCount = node.Elements()
                .Count(child => child.Name.NamespaceName == BpmnConstants.ModelNamespace
                    && child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal));
            if (definitionCount < 2)
            {
                AddLoss(element.Id, element.SemanticType,
                    "The event is marked 'multiple' but carries fewer than two event definitions; BPMN 2.0 has no schema construct for a multiple trigger without them, so the flag is not written.",
                    BpmnExportLossSeverity.Approximated);
            }
        }
    }

    private static string? GetProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // ------------------------------------------------------------------------------ activities / subprocesses

    private void WriteSubProcessChildren(ProcessPlan plan, XElement node, ModelingElementDto element)
    {
        if (element.Properties.TryGetValue("completionCondition", out var completion)
            && completion.ValueKind == JsonValueKind.String)
        {
            node.Add(new XElement(Ns + "completionCondition",
                new XAttribute(XsiNs + "type", "tFormalExpression"),
                SanitizeXmlText(completion.GetString() ?? string.Empty, element.Id, "Property")));
        }

        var childLanes = ChildrenOf(element.Id)
            .Where(candidate => IsLane(candidate) && ReferenceEquals(_elements.GetValueOrDefault(candidate.Id), candidate))
            .ToList();
        if (childLanes.Count > 0)
        {
            node.Add(WriteLaneSet(plan, childLanes, isChildLaneSet: false));
        }

        foreach (var child in ChildrenOf(element.Id))
        {
            if (!IsLane(child) && ReferenceEquals(_elements.GetValueOrDefault(child.Id), child)
                && string.Equals(FlowContainer(child), element.Id, StringComparison.Ordinal))
            {
                var childNode = WriteFlowElement(plan, child);
                if (childNode is not null)
                {
                    node.Add(childNode);
                }
            }
        }

        foreach (var relationship in plan.Model.Relationships)
        {
            if (!ReferenceEquals(_relationships.GetValueOrDefault(relationship.Id), relationship))
            {
                continue;
            }

            if (string.Equals(FlowContainerId(relationship), element.Id, StringComparison.Ordinal))
            {
                node.Add(WriteFlowRelationship(relationship));
            }
        }
    }

    private void WriteActivityChildren(ProcessPlan plan, XElement node, ModelingElementDto element)
    {
        var dataInputs = (_relationshipsByTarget.TryGetValue(element.Id, out var incoming)
                ? incoming
                : [])
            .Where(relationship => string.Equals(relationship.RelationshipType, BpmnRelationshipTypes.DataInputAssociation, StringComparison.Ordinal))
            .GroupBy(relationship => relationship.SourceId)
            .ToList();
        foreach (var group in dataInputs)
        {
            node.Add(WriteDataAssociation(group, isInput: true));
        }

        var dataOutputs = (_relationshipsBySource.TryGetValue(element.Id, out var outgoing)
                ? outgoing
                : [])
            .Where(relationship => string.Equals(relationship.RelationshipType, BpmnRelationshipTypes.DataOutputAssociation, StringComparison.Ordinal))
            .GroupBy(relationship => relationship.SourceId)
            .ToList();
        foreach (var group in dataOutputs)
        {
            node.Add(WriteDataAssociation(group, isInput: false));
        }

        if (string.Equals(element.SemanticType, BpmnSemanticTypes.TextAnnotation, StringComparison.Ordinal)
            && element.Name.Length > 0)
        {
            node.Add(new XElement(Ns + "text", SanitizeXmlText(element.Name, element.Id, "TextAnnotation")));
        }
    }

    private XElement WriteDataAssociation(IGrouping<string, ModelingRelationshipDto> group, bool isInput)
    {
        var association = new XElement(Ns + (isInput ? "dataInputAssociation" : "dataOutputAssociation"));
        if (group.Key.Length > 0)
        {
            association.Add(new XAttribute("id", group.Key));
        }

        foreach (var source in group.Select(relationship => relationship.SourceElementId).Distinct(StringComparer.Ordinal))
        {
            association.Add(new XElement(Ns + "sourceRef", source));
        }

        foreach (var target in group.Select(relationship => relationship.TargetElementId).Distinct(StringComparer.Ordinal))
        {
            association.Add(new XElement(Ns + "targetRef", target));
        }

        var first = group.First();
        if (first.Properties.TryGetValue("assignments", out var assignments)
            && assignments.ValueKind == JsonValueKind.Array)
        {
            foreach (var assignment in assignments.EnumerateArray())
            {
                var from = GetProperty(assignment, "from") ?? string.Empty;
                var to = GetProperty(assignment, "to") ?? string.Empty;
                association.Add(new XElement(Ns + "assignment",
                    new XElement(Ns + "from", from),
                    new XElement(Ns + "to", to)));
            }
        }

        return association;
    }

    // ------------------------------------------------------------------------------ relationships

    /// <summary>
    /// The id of the subprocess element that owns a flow relationship (sequence flow / association),
    /// or empty when the relationship lives at process level. Lanes are transparent: the container
    /// is the nearest non-lane ancestor of the source element.
    /// </summary>
    /// <summary>
    /// The nearest non-lane ancestor of an element (lanes are transparent containers: their flow
    /// nodes are process-level BPMN children referenced by <c>flowNodeRef</c>), or empty string at
    /// the process root.
    /// </summary>
    private string FlowContainer(ModelingElementDto element)
    {
        var guard = 0;
        var current = element;
        while (current.ParentId.Length > 0 && _elements.TryGetValue(current.ParentId, out var parent) && guard++ < 10_000)
        {
            current = parent;
            if (!IsLane(current))
            {
                return current.Id;
            }
        }

        return string.Empty;
    }

    private string FlowContainerId(ModelingRelationshipDto relationship)
    {
        return _elements.TryGetValue(relationship.SourceElementId, out var source)
            ? FlowContainer(source)
            : string.Empty;
    }

    private bool IsProcessLevelFlow(ModelingRelationshipDto relationship, ProcessPlan plan)
    {
        var type = relationship.RelationshipType;
        if (type is BpmnRelationshipTypes.SequenceFlow or BpmnRelationshipTypes.ConditionalFlow
            or BpmnRelationshipTypes.DefaultFlow or BpmnRelationshipTypes.Association)
        {
            return string.Equals(FlowContainerId(relationship), plan.RootParentId, StringComparison.Ordinal)
                && ReferenceEquals(_elementOwners.GetValueOrDefault(relationship.SourceElementId), plan.Model);
        }

        return false;
    }

    private XElement WriteFlowRelationship(ModelingRelationshipDto relationship)
    {
        var localName = relationship.RelationshipType switch
        {
            BpmnRelationshipTypes.Association => "association",
            _ => "sequenceFlow",
        };

        var flow = new XElement(Ns + localName,
            new XAttribute("id", relationship.Id),
            new XAttribute("sourceRef", relationship.SourceElementId),
            new XAttribute("targetRef", relationship.TargetElementId));
        if (relationship.Name.Length > 0)
        {
            flow.Add(new XAttribute("name", SanitizeXmlText(relationship.Name, relationship.Id, relationship.RelationshipType)));
        }

        if (localName == "association")
        {
            // One attribute reproduces both import properties (`direction` from the mapped value,
            // `bpmn:associationDirection` from the catch-all): the string twin wins, like `multiple`.
            var direction = TryGetString(relationship.Properties, "bpmn:associationDirection", out var rawDirection)
                ? rawDirection
                : TryGetString(relationship.Properties, "direction", out var mappedDirection)
                    ? mappedDirection
                    : null;
            if (direction is not null)
            {
                flow.Add(new XAttribute("associationDirection", direction));
            }
        }

        WriteRelationshipCatchAllAttributes(flow, relationship);

        // Relationship tags ride the host payload like element tags (finding 9); the Standard
        // profile reports them as a loss.
        if (relationship.Tags.Count > 0)
        {
            if (_options.Profile == BpmnExportProfile.WithHostExtensions)
            {
                var extension = new XElement(Ns + "extensionElements", HostPropertiesElement(
                [
                    new KeyValuePair<string, JsonElement>("tags", JsonSerializer.SerializeToElement(relationship.Tags)),
                ]));
                flow.Add(extension);
            }
            else
            {
                AddLoss(relationship.Id, "Tags",
                    "Relationship tags have no BPMN 2.0 representation and the Standard profile writes no host extensions; they are not preserved.");
            }
        }

        if (relationship.RelationshipType == BpmnRelationshipTypes.ConditionalFlow
            && TryGetString(relationship.Properties, "condition", out var condition))
        {
            var expression = new XElement(Ns + "conditionExpression",
                new XAttribute(XsiNs + "type", "tFormalExpression"),
                SanitizeXmlText(condition, relationship.Id, "Property"));
            if (TryGetString(relationship.Properties, "conditionLanguage", out var language))
            {
                expression.Add(new XAttribute("language", language));
            }

            flow.Add(expression);
        }

        return flow;
    }

    private void WriteRelationshipCatchAllAttributes(XElement flow, ModelingRelationshipDto relationship)
    {
        foreach (var (key, value) in relationship.Properties)
        {
            if (key.StartsWith("bpmndi:", StringComparison.Ordinal) || NativelyHandledProperties.Contains(key))
            {
                continue;
            }

            WritePrefixedPropertyAttribute(flow, relationship.Id, key, value);
        }
    }
}
