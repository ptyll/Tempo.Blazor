using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Walks a safely-loaded BPMN <c>definitions</c> document and produces modeling models,
/// the loss report and validation issues. Deterministic: element order follows the XML
/// document order, ids keep their BPMN values, generated ids are stable hashes of the XML path.
/// </summary>
internal sealed partial class BpmnModelBuilder
{
    private static readonly HashSet<string> FlowNodeTypes = new(StringComparer.Ordinal)
    {
        "task", "userTask", "serviceTask", "sendTask", "receiveTask", "scriptTask",
        "businessRuleTask", "manualTask", "subProcess", "adHocSubProcess", "transaction",
        "callActivity", "startEvent", "endEvent", "intermediateCatchEvent",
        "intermediateThrowEvent", "boundaryEvent", "exclusiveGateway", "parallelGateway",
        "inclusiveGateway", "complexGateway", "eventBasedGateway",
    };

    private static readonly HashSet<string> ArtifactTypes = new(StringComparer.Ordinal)
    {
        "dataObjectReference", "dataStoreReference", "dataObject", "textAnnotation", "group",
    };

    private static readonly string[] TaskTypes =
    [
        "task", "userTask", "serviceTask", "sendTask", "receiveTask", "scriptTask",
        "businessRuleTask", "manualTask",
    ];

    private static readonly Dictionary<string, string> TaskTypeMap = new(StringComparer.Ordinal)
    {
        ["task"] = BpmnSemanticTypes.Task,
        ["userTask"] = BpmnSemanticTypes.UserTask,
        ["serviceTask"] = BpmnSemanticTypes.ServiceTask,
        ["sendTask"] = BpmnSemanticTypes.SendTask,
        ["receiveTask"] = BpmnSemanticTypes.ReceiveTask,
        ["scriptTask"] = BpmnSemanticTypes.ScriptTask,
        ["businessRuleTask"] = BpmnSemanticTypes.BusinessRuleTask,
        ["manualTask"] = BpmnSemanticTypes.ManualTask,
    };

    private static readonly Dictionary<string, string> EventTypeMap = new(StringComparer.Ordinal)
    {
        ["startEvent"] = BpmnSemanticTypes.StartEvent,
        ["endEvent"] = BpmnSemanticTypes.EndEvent,
        ["intermediateCatchEvent"] = BpmnSemanticTypes.IntermediateCatchEvent,
        ["intermediateThrowEvent"] = BpmnSemanticTypes.IntermediateThrowEvent,
        ["boundaryEvent"] = BpmnSemanticTypes.BoundaryEvent,
    };

    private static readonly Dictionary<string, string> GatewayTypeMap = new(StringComparer.Ordinal)
    {
        ["exclusiveGateway"] = BpmnSemanticTypes.ExclusiveGateway,
        ["parallelGateway"] = BpmnSemanticTypes.ParallelGateway,
        ["inclusiveGateway"] = BpmnSemanticTypes.InclusiveGateway,
        ["complexGateway"] = BpmnSemanticTypes.ComplexGateway,
        ["eventBasedGateway"] = BpmnSemanticTypes.EventBasedGateway,
    };

    private static readonly string[] GlobalDefinitionTypes =
    [
        "message", "signal", "error", "escalation", "itemDefinition", "dataStore", "category",
    ];

    private const string HostExtensionNamespace = "https://tempo.blazor/schemas/bpmn-extensions";

    private readonly BpmnImportOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly List<ModelingModelDto> _models = [];
    private readonly List<BpmnImportLoss> _losses = [];
    private readonly List<BpmnImportWarning> _warnings = [];
    private readonly List<string> _duplicateElementIds = [];

    /// <summary>Per-model set of element ids — the O(1) duplicate check backing <see cref="CreateElement"/>.</summary>
    private readonly Dictionary<ModelingModelDto, HashSet<string>> _modelElementIds = [];

    /// <summary>Element id → (owning model, element) for every created element; powers every id lookup.</summary>
    private readonly Dictionary<string, (ModelingModelDto Model, ModelingElementDto Element)> _elementIndex = new(StringComparer.Ordinal);

    /// <summary>Relationship id → (owning model, relationship) for every created relationship; powers BPMNEdge resolution.</summary>
    private readonly Dictionary<string, (ModelingModelDto Model, ModelingRelationshipDto Relationship)> _relationshipIndex = new(StringComparer.Ordinal);

    /// <summary>flow-node id → the model that owns it.</summary>
    private readonly Dictionary<string, ModelingModelDto> _nodeOwners = new(StringComparer.Ordinal);

    /// <summary>participant id → (model, pool element).</summary>
    private readonly Dictionary<string, (ModelingModelDto Model, ModelingElementDto Pool)> _poolOwners = new(StringComparer.Ordinal);

    private readonly Dictionary<string, GlobalDefinition> _globalDefinitions = new(StringComparer.Ordinal);
    private readonly HashSet<string> _usedGlobalDefinitions = new(StringComparer.Ordinal);

    /// <summary>Global definition ids (message/signal/error/escalation) referenced from each model.</summary>
    private readonly Dictionary<ModelingModelDto, HashSet<string>> _usedGlobalDefinitionsByModel = [];

    private readonly HashSet<string> _referencedDataObjects = new(StringComparer.Ordinal);

    /// <summary>dataObjectRef value → the DataObject reference element carrying it.</summary>
    private readonly Dictionary<string, ModelingElementDto> _dataObjectReferenceIndex = new(StringComparer.Ordinal);

    private readonly HashSet<string> _defaultFlowIds = new(StringComparer.Ordinal);

    /// <summary>ioSpecification dataInput/dataOutput or activity property id → (enclosing activity element id, declaration).</summary>
    private readonly Dictionary<string, (string ActivityId, XElement Declaration)> _ioResolutions = new(StringComparer.Ordinal);

    /// <summary>io/property ids that at least one association endpoint referenced.</summary>
    private readonly HashSet<string> _referencedIoEndpoints = new(StringComparer.Ordinal);

    /// <summary>Foreign extension payloads accumulated per element and namespace, written once at the end.</summary>
    private readonly Dictionary<ModelingElementDto, Dictionary<string, StringBuilder>> _foreignExtensions = [];

    private readonly Dictionary<string, string> _namespacePrefixes = new(StringComparer.Ordinal);
    private readonly XDocument _document;
    private string _targetNamespace = string.Empty;

    private BpmnModelBuilder(XDocument document, BpmnImportOptions options, CancellationToken cancellationToken)
    {
        _document = document;
        _options = options;
        _cancellationToken = cancellationToken;
    }

    public static BpmnImportResult Build(XDocument document, BpmnImportOptions options, CancellationToken cancellationToken = default)
    {
        var builder = new BpmnModelBuilder(document, options, cancellationToken);
        return builder.BuildCore();
    }

    private BpmnImportResult BuildCore()
    {
        var root = _document.Root!;
        _targetNamespace = root.Attribute("targetNamespace")?.Value ?? string.Empty;
        CollectNamespacePrefixes(root);
        CollectGlobalDefinitions(root);

        var collaboration = root.Elements().FirstOrDefault(element => BpmnImportInternals.IsLocal(element, "collaboration"));
        var processes = root.Elements().Where(element => BpmnImportInternals.IsLocal(element, "process")).ToList();

        if (collaboration is null && processes.Count == 0)
        {
            return new BpmnImportResult
            {
                Models = [],
                Losses = [],
                Warnings = [],
                HasErrors = true,
                ErrorMessage = "The BPMN document contains no process or collaboration to import.",
            };
        }

        if (_options.ModelSplit == BpmnModelSplit.SingleWithPools)
        {
            BuildSingleModel(collaboration, processes);
        }
        else
        {
            BuildPerProcessModels(collaboration, processes);
        }

        ApplyRootExtensionElements(root);
        ImportViewsAndAutoLayout(root);

        ReportUnusedGlobalDefinitions();
        ReportUnreferencedIoDeclarations();
        FlushForeignExtensions();
        AttachGlobalDefinitions();
        BpmnNotationValidator.AttachValidationIssues(_models, _duplicateElementIds);
        AttachLossIssues();

        return new BpmnImportResult
        {
            Models = _models.ToArray(),
            Losses = _losses.ToArray(),
            Warnings = _warnings.ToArray(),
            HasErrors = false,
        };
    }

    // ---------------------------------------------------------------- model scaffolding

    private ModelingModelDto CreateModel(string id, string title)
    {
        var model = new ModelingModelDto
        {
            Id = id,
            Title = title,
            Notation = BpmnConstants.Bpmn2Notation,
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "bpmn-xml",
                SourceVersion = _targetNamespace,
                LoadedAt = DateTimeOffset.UnixEpoch,
                IsFresh = true,
            },
        };
        model.SupportedNotations.Add(BpmnConstants.Bpmn2Notation);
        _models.Add(model);
        _modelElementIds[model] = new HashSet<string>(StringComparer.Ordinal);
        return model;
    }

    private void BuildPerProcessModels(XElement? collaboration, List<XElement> processes)
    {
        var modelByProcess = new Dictionary<string, ModelingModelDto>(StringComparer.Ordinal);
        foreach (var process in processes)
        {
            var id = RequiredId(process, "process");
            var model = CreateModel(id, process.Attribute("name")?.Value ?? string.Empty);
            modelByProcess[id] = model;
        }

        if (collaboration is not null)
        {
            if (_models.Count == 0)
            {
                // Collaboration without any process/participant (e.g. only message flows between
                // black-box participants declared inline): anchor everything on a collaboration model.
                CreateModel(RequiredId(collaboration, "collaboration"),
                    collaboration.Attribute("name")?.Value ?? string.Empty);
            }

            foreach (var participant in BpmnImportInternals.Children(collaboration, "participant"))
            {
                var processRef = StripRef(participant.Attribute("processRef")?.Value);
                ModelingModelDto? model = null;
                if (processRef.Length > 0)
                {
                    modelByProcess.TryGetValue(processRef, out model);
                }

                if (model is null)
                {
                    // Black-box pool (no processRef / unresolved processRef): attach to the first
                    // model so it is not lost; the loss report says why it has no contents.
                    model = _models.Count > 0 ? _models[0] : CreateModel(
                        RequiredId(collaboration, "collaboration"),
                        collaboration.Attribute("name")?.Value ?? string.Empty);
                    var blackBoxPool = CreatePool(participant, model);
                    _poolOwners[blackBoxPool.Id] = (model, blackBoxPool);
                    AddLoss(participant, "black-box pool",
                        "Participant has no resolvable processRef; the pool imports without any contents.",
                        BpmnImportLossSeverity.Info, elementId: blackBoxPool.Id);
                    continue;
                }

                var pool = CreatePool(participant, model);
                _poolOwners[pool.Id] = (model, pool);
            }
        }

        foreach (var process in processes)
        {
            var model = modelByProcess[RequiredId(process, "process")];
            var pool = collaboration is not null
                ? _poolOwners.Values.Where(entry => ReferenceEquals(entry.Model, model)).Select(entry => entry.Pool).FirstOrDefault()
                : null;
            WalkProcess(process, model, pool?.Id ?? string.Empty);
        }

        if (collaboration is not null)
        {
            foreach (var messageFlow in BpmnImportInternals.Children(collaboration, "messageFlow"))
            {
                CreateMessageFlow(messageFlow);
            }

            ReportUnsupportedCollaborationConstructs(collaboration);
        }
    }

    private void BuildSingleModel(XElement? collaboration, List<XElement> processes)
    {
        var anchor = collaboration ?? processes[0];
        var model = CreateModel(RequiredId(anchor, collaboration is not null ? "collaboration" : "process"),
            anchor.Attribute("name")?.Value ?? string.Empty);

        var participants = collaboration is not null
            ? BpmnImportInternals.Children(collaboration, "participant").ToList()
            : [];
        var poolByProcess = new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal);
        foreach (var participant in participants)
        {
            var pool = CreatePool(participant, model);
            _poolOwners[pool.Id] = (model, pool);
            var processRef = StripRef(participant.Attribute("processRef")?.Value);
            if (processRef.Length > 0)
            {
                poolByProcess[processRef] = pool;
            }
            else
            {
                AddLoss(participant, "black-box pool",
                    "Participant has no processRef; the pool imports without any contents.",
                    BpmnImportLossSeverity.Info, elementId: pool.Id);
            }
        }

        foreach (var process in processes)
        {
            var processId = RequiredId(process, "process");
            poolByProcess.TryGetValue(processId, out var pool);
            WalkProcess(process, model, pool?.Id ?? string.Empty);
        }

        if (collaboration is not null)
        {
            foreach (var messageFlow in BpmnImportInternals.Children(collaboration, "messageFlow"))
            {
                CreateMessageFlow(messageFlow);
            }

            ReportUnsupportedCollaborationConstructs(collaboration);
        }
    }

    private ModelingElementDto CreatePool(XElement participant, ModelingModelDto model)
    {
        var pool = CreateElement(participant, BpmnSemanticTypes.Pool, parentId: string.Empty, model);
        return pool;
    }

    // ---------------------------------------------------------------- process / scope walking

    private void WalkProcess(XElement container, ModelingModelDto model, string containerParentId)
    {
        if (BpmnImportInternals.IsLocal(container, "process"))
        {
            ReportProcessLevelContent(container, model);
            ApplyProcessCatchAllAttributes(container, model);
        }

        var laneOwnership = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var laneSet in BpmnImportInternals.Children(container, "laneSet"))
        {
            WalkLaneSet(laneSet, model, containerParentId, laneOwnership);
        }

        CollectDefaultFlows(container);

        foreach (var child in container.Elements())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var localName = child.Name.LocalName;
            if (localName is "laneSet" or "incoming" or "outgoing")
            {
                // laneSet is walked above; incoming/outgoing are redundant wiring fully derived
                // from the sequence flows themselves.
                continue;
            }

            if (localName is "documentation" or "extensionElements" or "ioSpecification" or "property"
                or "dataInputAssociation" or "dataOutputAssociation" or "multiInstanceLoopCharacteristics"
                or "standardLoopCharacteristics" or "completionCondition" or "resourceRole" or "auditing"
                or "monitoring" or "conditionExpression")
            {
                // Already handled per flow node (descriptive children, activity children, event
                // definitions) or deliberately reported elsewhere — never double-reported here.
                continue;
            }

            if (FlowNodeTypes.Contains(localName))
            {
                var nodeId = RequiredId(child, localName);
                var parentId = laneOwnership.TryGetValue(nodeId, out var laneId) ? laneId : containerParentId;
                CreateFlowNode(child, model, parentId);
            }
            else if (ArtifactTypes.Contains(localName))
            {
                CreateArtifact(child, model, containerParentId);
            }
            else if (localName == "association")
            {
                CreateAssociation(child, model);
            }
            else if (localName == "sequenceFlow")
            {
                CreateSequenceFlow(child, model);
            }
            else
            {
                AddLoss(child, localName, "Unsupported process-level construct; content is not preserved.");
            }
        }

        MergeReferencedDataObjects(model, container, containerParentId);
    }

    /// <summary>
    /// Process-level documentation and extensionElements have no model element to attach to (the
    /// process IS the model). Tool plugins still get their namespace routed to them; everything
    /// else is preserved verbatim in the loss report instead of being dropped silently.
    /// </summary>
    private void ReportProcessLevelContent(XElement process, ModelingModelDto model)
    {
        foreach (var child in process.Elements())
        {
            if (child.Name.LocalName == "documentation")
            {
                AddLoss(child, "documentation",
                    $"Process-level documentation is not preserved on the model; content: {BpmnImportInternals.DirectText(child)}",
                    BpmnImportLossSeverity.Info);
                continue;
            }

            if (child.Name.LocalName != "extensionElements")
            {
                continue;
            }

            foreach (var group in child.Elements().GroupBy(payload => payload.Name.NamespaceName))
            {
                var ns = group.Key;
                var plugin = _options.ToolExtensions.FirstOrDefault(extension =>
                    string.Equals(extension.NamespaceUri, ns, StringComparison.Ordinal));
                if (plugin is not null)
                {
                    plugin.OnImportElement(new BpmnToolImportContext
                    {
                        ExtensionElements = child,
                        Element = null,
                        Model = model,
                        ReportLoss = loss => _losses.Add(loss),
                    });
                    continue;
                }

                foreach (var payload in group)
                {
                    _losses.Add(new BpmnImportLoss
                    {
                        ElementId = null,
                        XmlPath = BpmnImportInternals.BuildPath(payload),
                        Construct = "extensionElements",
                        Reason = $"Process-level extension element in namespace '{ns}' has no model element to attach to; content: {payload.ToString(SaveOptions.DisableFormatting)}",
                        Severity = BpmnImportLossSeverity.Loss,
                    });
                }
            }
        }
    }

    private void WalkLaneSet(XElement laneSet, ModelingModelDto model, string parentId, Dictionary<string, string> laneOwnership)
    {
        foreach (var lane in BpmnImportInternals.Children(laneSet, "lane"))
        {
            var laneElement = CreateElement(lane, BpmnSemanticTypes.Lane, parentId, model);
            foreach (var flowNodeRef in BpmnImportInternals.Children(lane, "flowNodeRef"))
            {
                var nodeId = StripRef(flowNodeRef.Value);
                if (nodeId.Length > 0)
                {
                    laneOwnership[nodeId] = laneElement.Id;
                }
            }

            foreach (var childLaneSet in BpmnImportInternals.Children(lane, "childLaneSet"))
            {
                WalkLaneSet(childLaneSet, model, laneElement.Id, laneOwnership);
            }
        }
    }

    private void CollectDefaultFlows(XElement container)
    {
        foreach (var element in container.DescendantsAndSelf())
        {
            if (!FlowNodeTypes.Contains(element.Name.LocalName) && element.Name.LocalName != "adHocSubProcess")
            {
                continue;
            }

            var defaultValue = element.Attribute("default")?.Value;
            if (!string.IsNullOrWhiteSpace(defaultValue))
            {
                _defaultFlowIds.Add(StripRef(defaultValue));
            }
        }
    }

    // ---------------------------------------------------------------- flow nodes

    private ModelingElementDto CreateFlowNode(XElement element, ModelingModelDto model, string parentId)
    {
        var localName = element.Name.LocalName;
        string semanticType;
        if (TaskTypeMap.TryGetValue(localName, out var taskType))
        {
            semanticType = taskType;
        }
        else if (EventTypeMap.TryGetValue(localName, out var eventType))
        {
            semanticType = eventType;
        }
        else if (GatewayTypeMap.TryGetValue(localName, out var gatewayType))
        {
            semanticType = gatewayType;
        }
        else if (localName is "subProcess" or "adHocSubProcess" or "transaction")
        {
            semanticType = IsEventSubProcess(element) ? BpmnSemanticTypes.EventSubProcess : BpmnSemanticTypes.SubProcess;
        }
        else
        {
            semanticType = BpmnSemanticTypes.CallActivity;
        }

        var node = CreateElement(element, semanticType, parentId, model);
        _nodeOwners[node.Id] = model;

        HandleDescriptiveChildren(element, node, model);
        ApplyEventProperties(element, node, model);
        ApplyActivityChildren(element, node);
        ApplyExplicitMappedAttributes(element, node);

        if (localName is "subProcess" or "adHocSubProcess" or "transaction")
        {
            if (localName == "transaction")
            {
                node.Properties["transaction"] = BpmnImportInternals.JsonElementFrom(true);
                if (element.Descendants().Any(descendant =>
                        descendant.Name.LocalName is "compensateEventDefinition" ||
                        (descendant.Attribute("isForCompensation")?.Value == "true")))
                {
                    AddLoss(element, "transaction",
                        "Transaction imported as SubProcess with transaction=true; compensation protocol semantics are approximated.",
                        BpmnImportLossSeverity.Approximated, elementId: node.Id);
                }
            }

            var isAdHoc = localName == "adHocSubProcess" || element.Attribute("adHoc")?.Value == "true";
            if (isAdHoc)
            {
                AddLoss(element, "adHocSubProcess",
                    "Ad-hoc subprocess imported as SubProcess; ordering/completion semantics are not preserved.",
                    BpmnImportLossSeverity.Approximated, elementId: node.Id);
                var completionCondition = BpmnImportInternals.Child(element, "completionCondition");
                if (completionCondition is not null)
                {
                    node.Properties["completionCondition"] = BpmnImportInternals.StringElement(
                        BpmnImportInternals.DirectText(completionCondition));
                }
            }

            WalkProcess(element, model, node.Id);
        }

        return node;
    }

    private static bool IsEventSubProcess(XElement element) =>
        element.Attribute("triggeredByEvent")?.Value == "true";

    /// <summary>Documentation (first wins) and extensionElements — common to every flow node kind.</summary>
    private void HandleDescriptiveChildren(XElement element, ModelingElementDto node, ModelingModelDto model)
    {
        var documentationSeen = false;
        foreach (var child in element.Elements())
        {
            if (child.Name.LocalName == "documentation")
            {
                if (!documentationSeen)
                {
                    documentationSeen = true;
                    node.Description = BpmnImportInternals.DirectText(child);
                }
            }
            else if (child.Name.LocalName == "extensionElements")
            {
                ApplyExtensionElements(child, node, model);
            }
        }
    }

    private void ApplyEventProperties(XElement element, ModelingElementDto node, ModelingModelDto model)
    {
        var localName = element.Name.LocalName;
        var isEvent = EventTypeMap.ContainsKey(localName);
        if (!isEvent)
        {
            return;
        }

        var definitions = new List<JsonNode>();
        foreach (var definition in element.Elements()
                     .Where(child => child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal)))
        {
            if (BpmnImportInternals.IsLocal(definition, "multipleEventDefinition"))
            {
                // The strict schema form of the legacy `multiple="true"` attribute: restore the
                // same typed property + catch-all twin the attribute path produces, so both
                // spellings normalize to the same model.
                node.Properties["multiple"] = BpmnImportInternals.JsonElementFrom(true);
                node.Properties["bpmn:multiple"] = BpmnImportInternals.StringElement("true");
                continue;
            }

            definitions.Add(BuildEventDefinition(definition, model));
        }

        if (definitions.Count > 0)
        {
            node.Properties["eventDefinitions"] = BpmnImportInternals.JsonElementFrom(definitions);
        }

        if (element.Attribute("multiple")?.Value == "true")
        {
            node.Properties["multiple"] = BpmnImportInternals.JsonElementFrom(true);
        }

        if (element.Attribute("parallelMultiple")?.Value == "true")
        {
            node.Properties["parallelMultiple"] = BpmnImportInternals.JsonElementFrom(true);
        }

        if (localName == "boundaryEvent")
        {
            var attachedToRef = StripRef(element.Attribute("attachedToRef")?.Value);
            if (attachedToRef.Length > 0)
            {
                node.Properties["attachedToRef"] = BpmnImportInternals.StringElement(attachedToRef);
            }

            var cancelActivity = element.Attribute("cancelActivity")?.Value;
            if (cancelActivity is not null)
            {
                node.Properties["cancelActivity"] = BpmnImportInternals.JsonElementFrom(cancelActivity == "true");
            }
        }
    }

    private JsonObject BuildEventDefinition(XElement definition, ModelingModelDto model)
    {
        var type = definition.Name.LocalName[..^"EventDefinition".Length];
        var obj = new JsonObject { ["type"] = type };

        if (definition.Attribute("id") is { } id)
        {
            obj["id"] = id.Value;
        }

        switch (type)
        {
            case "message":
            case "signal":
            case "error":
            case "escalation":
                var refAttribute = type + "Ref";
                var reference = StripRef(definition.Attribute(refAttribute)?.Value);
                if (reference.Length > 0)
                {
                    obj["ref"] = reference;
                    if (_globalDefinitions.TryGetValue(reference, out var global))
                    {
                        MarkGlobalUsed(model, reference);
                        if (global.Name.Length > 0)
                        {
                            obj["name"] = global.Name;
                        }

                        if (global.Code.Length > 0)
                        {
                            obj["code"] = global.Code;
                        }
                    }
                }

                break;
            case "timer":
                foreach (var kind in new[] { "timeDuration", "timeDate", "timeCycle" })
                {
                    var time = BpmnImportInternals.Child(definition, kind);
                    if (time is not null)
                    {
                        obj[kind] = BpmnImportInternals.DirectText(time);
                    }
                }

                break;
            case "conditional":
                var condition = BpmnImportInternals.Child(definition, "condition");
                if (condition is not null)
                {
                    obj["condition"] = BpmnImportInternals.DirectText(condition);
                }

                break;
            case "compensate":
                var activityRef = StripRef(definition.Attribute("activityRef")?.Value);
                if (activityRef.Length > 0)
                {
                    obj["activityRef"] = activityRef;
                }

                if (definition.Attribute("waitForCompletion") is { } wait)
                {
                    obj["waitForCompletion"] = wait.Value == "true";
                }

                break;
            case "link":
                if (definition.Attribute("name") is { } linkName)
                {
                    obj["name"] = linkName.Value;
                }

                break;
        }

        return obj;
    }

    private void ApplyActivityChildren(XElement element, ModelingElementDto node)
    {
        var isActivity = TaskTypeMap.ContainsKey(element.Name.LocalName)
            || element.Name.LocalName is "subProcess" or "adHocSubProcess" or "transaction" or "callActivity";
        if (!isActivity)
        {
            return;
        }

        if (element.Attribute("isForCompensation")?.Value == "true")
        {
            node.Properties["isForCompensation"] = BpmnImportInternals.JsonElementFrom(true);
        }

        foreach (var child in element.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "ioSpecification":
                    RegisterIoSpecification(child, node);
                    break;
                case "property":
                    RegisterActivityProperty(child, node);
                    break;
                case "dataInputAssociation":
                    CreateDataAssociation(child, node, isInput: true);
                    break;
                case "dataOutputAssociation":
                    CreateDataAssociation(child, node, isInput: false);
                    break;
                case "multiInstanceLoopCharacteristics":
                    AddLoss(child, "multiInstanceLoopCharacteristics",
                        "Multi-instance loop settings are not preserved on the imported task.",
                        BpmnImportLossSeverity.Loss, elementId: node.Id);
                    break;
                case "standardLoopCharacteristics":
                    AddLoss(child, "standardLoopCharacteristics",
                        "Standard loop settings are not preserved on the imported task.",
                        BpmnImportLossSeverity.Loss, elementId: node.Id);
                    break;
                case "resourceRole":
                case "auditing":
                case "monitoring":
                    AddLoss(child, child.Name.LocalName,
                        "Unsupported activity construct; content is not preserved.",
                        BpmnImportLossSeverity.Loss, elementId: node.Id);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------- artifacts

    private void CreateArtifact(XElement element, ModelingModelDto model, string parentId)
    {
        switch (element.Name.LocalName)
        {
            case "dataObjectReference":
            {
                var node = CreateElement(element, BpmnSemanticTypes.DataObject, parentId, model);
                var dataObjectRef = StripRef(element.Attribute("dataObjectRef")?.Value);
                if (dataObjectRef.Length > 0)
                {
                    node.Properties["dataObjectRef"] = BpmnImportInternals.StringElement(dataObjectRef);
                    _referencedDataObjects.Add(dataObjectRef);
                    _dataObjectReferenceIndex[dataObjectRef] = node;
                }

                if (element.Attribute("isCollection") is { } isCollection)
                {
                    node.Properties["isCollection"] = BpmnImportInternals.JsonElementFrom(isCollection.Value == "true");
                }

                MarkItemSubjectRefUsed(element);
                break;
            }
            case "dataStoreReference":
            {
                var node = CreateElement(element, BpmnSemanticTypes.DataStore, parentId, model);
                var dataStoreRef = StripRef(element.Attribute("dataStoreRef")?.Value);
                if (dataStoreRef.Length > 0)
                {
                    node.Properties["dataStoreRef"] = BpmnImportInternals.StringElement(dataStoreRef);
                    _usedGlobalDefinitions.Add(dataStoreRef);
                    if (_globalDefinitions.TryGetValue(dataStoreRef, out var global) && global.Name.Length > 0)
                    {
                        node.Properties["dataStoreName"] = BpmnImportInternals.StringElement(global.Name);
                    }
                }

                break;
            }
            case "dataObject":
                // Referenced raw dataObjects merge into their reference element (see
                // MergeReferencedDataObjects); unreferenced ones import standalone with an info loss.
                break;
            case "textAnnotation":
            {
                var node = CreateElement(element, BpmnSemanticTypes.TextAnnotation, parentId, model);
                // Standard form keeps the body in a <text> child; the direct-text form is the
                // fallback (finding 1).
                node.Name = BpmnImportInternals.DirectText(
                    BpmnImportInternals.Child(element, "text") ?? element);
                break;
            }
            case "group":
            {
                var node = CreateElement(element, BpmnSemanticTypes.Group, parentId, model);
                var categoryValueRef = StripRef(element.Attribute("categoryValueRef")?.Value);
                if (categoryValueRef.Length > 0)
                {
                    node.Properties["categoryValueRef"] = BpmnImportInternals.StringElement(categoryValueRef);
                    if (_globalDefinitions.TryGetValue(categoryValueRef, out var global))
                    {
                        _usedGlobalDefinitions.Add(categoryValueRef);
                        if (global.Name.Length > 0)
                        {
                            node.Properties["categoryValue"] = BpmnImportInternals.StringElement(global.Name);
                        }
                    }
                }

                break;
            }
        }
    }

    private void MarkItemSubjectRefUsed(XElement element)
    {
        var itemSubjectRef = StripRef(element.Attribute("itemSubjectRef")?.Value);
        if (itemSubjectRef.Length > 0 && _globalDefinitions.ContainsKey(itemSubjectRef))
        {
            _usedGlobalDefinitions.Add(itemSubjectRef);
        }
    }

    private void MergeReferencedDataObjects(ModelingModelDto model, XElement container, string containerParentId)
    {
        foreach (var raw in BpmnImportInternals.Children(container, "dataObject"))
        {
            var rawId = raw.Attribute("id")?.Value;
            if (rawId is null || !_referencedDataObjects.Contains(rawId))
            {
                // Unreferenced dataObject: import standalone, keep isCollection, report the mapping decision.
                var node = CreateElement(raw, BpmnSemanticTypes.DataObject, containerParentId, model);
                if (raw.Attribute("isCollection") is { } isCollection)
                {
                    node.Properties["isCollection"] = BpmnImportInternals.JsonElementFrom(isCollection.Value == "true");
                }

                MarkItemSubjectRefUsed(raw);
                AddLoss(raw, "dataObject",
                    "Unreferenced dataObject imported as a standalone DataObject element.",
                    BpmnImportLossSeverity.Info, elementId: node.Id);
                continue;
            }

            // Referenced: enrich the reference element with anything only the raw declaration carries.
            if (_dataObjectReferenceIndex.TryGetValue(rawId, out var reference))
            {
                if (reference.Name.Length == 0 && raw.Attribute("name") is { } rawName)
                {
                    reference.Name = rawName.Value;
                }

                if (!reference.Properties.ContainsKey("isCollection") && raw.Attribute("isCollection") is { } isCollection)
                {
                    reference.Properties["isCollection"] = BpmnImportInternals.JsonElementFrom(isCollection.Value == "true");
                }

                MarkItemSubjectRefUsed(raw);
            }
        }
    }

    // ---------------------------------------------------------------- relationships

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



    // ---------------------------------------------------------------- issues

    private void AttachLossIssues()
    {
        var issueOrdinal = 0;
        foreach (var loss in _losses)
        {
            issueOrdinal++;
            var model = FindOwningModel(loss);
            if (model is null)
            {
                continue;
            }

            model.Issues.Add(new ModelingIssueDto
            {
                Id = "bpmn-import-loss-" + issueOrdinal.ToString("D4", System.Globalization.CultureInfo.InvariantCulture),
                Category = "bpmn-import",
                Severity = loss.Severity == BpmnImportLossSeverity.Info
                    ? ModelingIssueSeverity.Info
                    : ModelingIssueSeverity.Warning,
                Message = $"{loss.Construct}: {loss.Reason}",
                SourceElementId = loss.ElementId ?? string.Empty,
                SourceRelationshipId = string.Empty,
                SuggestedFix = string.Empty,
            });
        }
    }

    private ModelingModelDto? FindOwningModel(BpmnImportLoss loss)
    {
        if (loss.ElementId is { } elementId
            && _elementIndex.TryGetValue(elementId, out var entry))
        {
            return entry.Model;
        }

        return _models.Count > 0 ? _models[0] : null;
    }
}
