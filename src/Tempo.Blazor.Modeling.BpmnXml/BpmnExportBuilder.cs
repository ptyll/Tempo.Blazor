using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Modeling.BpmnXml;

/// <summary>
/// Walks one or more modeling models and produces a deterministic BPMN 2.0 XML document
/// (stable ordering, invariant-culture numbers, LF line endings, no BOM). Everything the model
/// carries but BPMN cannot represent lands in the export loss report instead of being dropped.
/// Split into partials to stay under the 1200-line source ceiling (N270/F13).
/// </summary>
internal sealed partial class BpmnExportBuilder
{
    private static readonly XNamespace Ns = BpmnConstants.ModelNamespace;
    private static readonly XNamespace DiNs = BpmnConstants.DiagramNamespace;
    private static readonly XNamespace DcNs = BpmnConstants.DCNamespace;
    private static readonly XNamespace DdNs = BpmnConstants.DINamespace;
    private static readonly XNamespace XsiNs = "http://www.w3.org/2001/XMLSchema-instance";

    private readonly IReadOnlyList<ModelingModelDto> _models;
    private readonly BpmnExportOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly List<BpmnExportLoss> _losses = [];

    /// <summary>Element id → element across all models; the first occurrence wins.</summary>
    private readonly Dictionary<string, ModelingElementDto> _elements = new(StringComparer.Ordinal);

    /// <summary>Element id → owning model.</summary>
    private readonly Dictionary<string, ModelingModelDto> _elementOwners = new(StringComparer.Ordinal);

    /// <summary>Relationship id → relationship across all models; the first occurrence wins.</summary>
    private readonly Dictionary<string, ModelingRelationshipDto> _relationships = new(StringComparer.Ordinal);

    /// <summary>Element children pre-indexed by <see cref="ModelingElementDto.ParentId"/> (perf, docs §5).</summary>
    private readonly Dictionary<string, List<ModelingElementDto>> _childrenByParent = new(StringComparer.Ordinal);

    /// <summary>Relationships pre-indexed by source/target element id (perf, docs §5).</summary>
    private readonly Dictionary<string, List<ModelingRelationshipDto>> _relationshipsBySource = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ModelingRelationshipDto>> _relationshipsByTarget = new(StringComparer.Ordinal);

    /// <summary>Every XML id already in use (elements, relationships) — DI ids must not collide with them.</summary>
    private readonly HashSet<string> _usedXmlIds = new(StringComparer.Ordinal);

    /// <summary>Namespace URI → prefix already declared on the root (foreign/extension payloads).</summary>
    private readonly Dictionary<string, string> _prefixByUri = new(StringComparer.Ordinal);

    private XElement _root = null!;

    private BpmnExportBuilder(IReadOnlyList<ModelingModelDto> models, BpmnExportOptions options, CancellationToken cancellationToken)
    {
        _models = models;
        _options = options;
        _cancellationToken = cancellationToken;
    }

    public static BpmnExportResult Build(IReadOnlyList<ModelingModelDto> models, BpmnExportOptions options, CancellationToken cancellationToken)
    {
        var builder = new BpmnExportBuilder(models, options, cancellationToken);
        return builder.BuildCore();
    }

    private BpmnExportResult BuildCore()
    {
        IndexElements();

        _root = CreateRoot();
        var pools = CollectPools();
        var processes = PlanProcesses(pools, out var collaborationId);
        if (collaborationId is not null && (pools.Count > 0 || CollectMessageFlows().Count > 0))
        {
            // The collaboration element is written only when it carries participants or message
            // flows — an empty <collaboration/> is never emitted (review finding 3).
            WriteCollaboration(collaborationId, pools);
        }

        foreach (var process in processes)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            WriteProcess(process);
        }

        WriteGlobalDefinitions();
        if (_options.EmitBpmndi)
        {
            WriteDiagrams(collaborationId, processes.Count > 0 ? processes[0].Id : _models[0].Id);
        }

        WriteRootHostExtensions();

        return new BpmnExportResult
        {
            Xml = Serialize(_root),
            Success = true,
            Losses = _losses.ToArray(),
        };
    }

    private void IndexElements()
    {
        foreach (var model in _models)
        {
            foreach (var element in model.Elements)
            {
                if (element.ParentId.Length > 0)
                {
                    if (!_childrenByParent.TryGetValue(element.ParentId, out var children))
                    {
                        children = [];
                        _childrenByParent[element.ParentId] = children;
                    }

                    children.Add(element);
                }

                if (!_elements.TryAdd(element.Id, element))
                {
                    // The same element instance exported twice (e.g. a shared model in the list) is
                    // not a duplicate; two distinct elements claiming one id are (finding 7).
                    if (ReferenceEquals(_elements[element.Id], element))
                    {
                        continue;
                    }

                    _losses.Add(new BpmnExportLoss
                    {
                        ElementId = element.Id,
                        Construct = element.SemanticType,
                        Reason = $"The id '{element.Id}' is used by more than one element across the exported models; only the first occurrence is exported.",
                    });
                }
                else
                {
                    _elementOwners[element.Id] = model;
                    _usedXmlIds.Add(element.Id);
                }
            }

            foreach (var relationship in model.Relationships)
            {
                if (!string.IsNullOrEmpty(relationship.SourceElementId))
                {
                    if (!_relationshipsBySource.TryGetValue(relationship.SourceElementId, out var bySource))
                    {
                        bySource = [];
                        _relationshipsBySource[relationship.SourceElementId] = bySource;
                    }

                    bySource.Add(relationship);
                }

                if (!string.IsNullOrEmpty(relationship.TargetElementId))
                {
                    if (!_relationshipsByTarget.TryGetValue(relationship.TargetElementId, out var byTarget))
                    {
                        byTarget = [];
                        _relationshipsByTarget[relationship.TargetElementId] = byTarget;
                    }

                    byTarget.Add(relationship);
                }

                if (!_relationships.TryAdd(relationship.Id, relationship))
                {
                    if (!ReferenceEquals(_relationships[relationship.Id], relationship))
                    {
                        _losses.Add(new BpmnExportLoss
                        {
                            ElementId = relationship.Id,
                            Construct = relationship.RelationshipType,
                            Reason = $"The relationship id '{relationship.Id}' is used more than once across the exported models; only the first occurrence is exported.",
                        });
                    }
                }
                else
                {
                    _usedXmlIds.Add(relationship.Id);
                }
            }
        }
    }

    private XElement CreateRoot()
    {
        var targetNamespace = _options.TargetNamespace;
        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            targetNamespace = _models[0].Metadata.SourceVersion;
        }

        if (string.IsNullOrWhiteSpace(targetNamespace))
        {
            targetNamespace = "http://tempo.blazor/bpmn";
        }

        var root = new XElement(Ns + "definitions",
            new XAttribute("xmlns", BpmnConstants.ModelNamespace),
            new XAttribute("id", "Definitions_" + _models[0].Id),
            new XAttribute("targetNamespace", targetNamespace),
            new XAttribute("exporter", "Tempo.Blazor"),
            new XAttribute("exporterVersion", ExporterVersion),
            new XAttribute(XNamespace.Xmlns + "bpmndi", BpmnConstants.DiagramNamespace),
            new XAttribute(XNamespace.Xmlns + "dc", BpmnConstants.DCNamespace),
            new XAttribute(XNamespace.Xmlns + "di", BpmnConstants.DINamespace),
            new XAttribute(XNamespace.Xmlns + "xsi", XsiNs.NamespaceName));

        _prefixByUri[BpmnConstants.DiagramNamespace] = "bpmndi";
        _prefixByUri[BpmnConstants.DCNamespace] = "dc";
        _prefixByUri[BpmnConstants.DINamespace] = "di";
        _prefixByUri[XsiNs.NamespaceName] = "xsi";
        _prefixByUri[_options.HostExtensionNamespace] = _options.HostExtensionPrefix;

        if (_options.Profile == BpmnExportProfile.WithHostExtensions)
        {
            root.Add(new XAttribute(XNamespace.Xmlns + _options.HostExtensionPrefix, _options.HostExtensionNamespace));
        }

        if (_models.Count == 1 && _models[0].Title.Length > 0)
        {
            root.Add(new XAttribute("name", _models[0].Title));
        }

        return root;
    }

    /// <summary>
    /// The version stamped into <c>exporterVersion</c>: the exporter assembly's informational
    /// version as the full SemVer incl. prerelease; the build metadata (<c>+…</c>) is cut.
    /// The publish workflows stamp a CI prerelease on top of the csproj version
    /// (<c>-ci-&lt;timestamp&gt;</c> on every main push, an optional dispatch suffix for
    /// prereleases); that label stays — a prerelease export is honestly labelled — while the
    /// commit-hash build metadata is dropped by the csproj's
    /// <c>IncludeSourceRevisionInInformationalVersion=false</c>. The committed export goldens
    /// and the modeling MCP contract snapshot normalize this attribute to a placeholder, so
    /// exports stay byte-for-byte stable across local, CI and tag builds of the same release line.
    /// </summary>
    private static string ExporterVersion =>
        StripBuildMetadata(
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                    typeof(BpmnXmlExporter).Assembly)
                ?.InformationalVersion ?? string.Empty);

    /// <summary>
    /// Cuts the SemVer build metadata (<c>+…</c>) off a version string; the prerelease label
    /// (<c>-…</c>) is kept. An empty input stays empty (never throws on a missing attribute).
    /// </summary>
    internal static string StripBuildMetadata(string version)
    {
        var metadataStart = version.IndexOf('+');
        return metadataStart >= 0 ? version[..metadataStart] : version;
    }

    /// <summary>Prefixes reserved by the exporter itself; generated prefixes never collide with them (finding 8).</summary>
    private HashSet<string> ReservedPrefixes =>
        new(StringComparer.Ordinal) { "bpmndi", "dc", "di", "xsi", "xml", "xmlns", _options.HostExtensionPrefix };

    private List<ModelingElementDto> ChildrenOf(string parentId) =>
        _childrenByParent.TryGetValue(parentId, out var children) ? children : [];

    /// <summary>A DI id derived from {id}_di that is guaranteed unique across the whole document (finding 12).</summary>
    private string DiId(string baseId)
    {
        var candidate = baseId;
        var suffix = 1;
        while (!_usedXmlIds.Add(candidate))
        {
            candidate = baseId + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
            suffix++;
        }

        return candidate;
    }

    private List<ModelingElementDto> CollectPools() =>
        _models.SelectMany(model => model.Elements)
            .Where(element => string.Equals(element.SemanticType, BpmnSemanticTypes.Pool, StringComparison.Ordinal))
            .GroupBy(element => element.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

    private bool PoolHasChildren(ModelingElementDto pool) => _childrenByParent.ContainsKey(pool.Id);

    private List<ModelingRelationshipDto> CollectMessageFlows()
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var messageFlows = new List<ModelingRelationshipDto>();
        foreach (var relationship in _models.SelectMany(model => model.Relationships))
        {
            if (!string.Equals(relationship.RelationshipType, BpmnRelationshipTypes.MessageFlow, StringComparison.Ordinal))
            {
                continue;
            }

            if (seen.Add(relationship.Id))
            {
                messageFlows.Add(relationship);
            }
        }

        return messageFlows;
    }

    /// <summary>
    /// Plans the processes: one process per model, except a single model with several populated
    /// pools (a <see cref="BpmnModelSplit.SingleWithPools"/> import) where every populated pool
    /// becomes its own process. Returns the collaboration id when a collaboration element is
    /// required (several models, any pool, or any message flow), <c>null</c> otherwise.
    /// </summary>
    private List<ProcessPlan> PlanProcesses(List<ModelingElementDto> pools, out string? collaborationId)
    {
        var messageFlows = CollectMessageFlows();
        var needsCollaboration = pools.Count > 0 || messageFlows.Count > 0;

        var plans = new List<ProcessPlan>();
        if (_models.Count == 1 && pools.Count > 0)
        {
            var model = _models[0];
            var populatedPools = pools.Where(PoolHasChildren).ToList();
            if (populatedPools.Count == 0)
            {
                plans.Add(new ProcessPlan(model.Id, model.Title, model, string.Empty, string.Empty));
            }
            else if (populatedPools.Count == 1)
            {
                // A single populated pool is the PerProcess shape: the process keeps the model id,
                // so the collaboration needs its own id to keep the document's ids unique.
                plans.Add(new ProcessPlan(model.Id, model.Title, model, populatedPools[0].Id, populatedPools[0].Id));
                if (pools.Count > 1)
                {
                    _losses.Add(new BpmnExportLoss
                    {
                        Construct = "Pool",
                        Reason = "A pool without contents cannot be matched to a process; it exports as a black-box participant.",
                        Severity = BpmnExportLossSeverity.Info,
                    });
                }
            }
            else
            {
                foreach (var pool in populatedPools)
                {
                    plans.Add(new ProcessPlan(pool.Id + "_process", pool.Name, model, pool.Id, pool.Id));
                }

                if (model.Elements.Any(IsLooseElement))
                {
                    AddLoss(null, "Element",
                        "Elements outside every pool were attached to the first pool's process.",
                        BpmnExportLossSeverity.Info);
                }
            }
        }
        else
        {
            foreach (var model in _models)
            {
                var pool = pools.FirstOrDefault(candidate => ReferenceEquals(_elementOwners.GetValueOrDefault(candidate.Id), model));
                plans.Add(new ProcessPlan(model.Id, model.Title, model, pool?.Id ?? string.Empty, pool?.Id ?? string.Empty));
            }
        }

        if (!needsCollaboration)
        {
            collaborationId = null;
            return plans;
        }

        // The collaboration id doubles as the re-imported SingleWithPools model id, so keep the
        // model id when no process claims it; otherwise mint a deterministic sibling id.
        var candidateId = _models.Count == 1 ? _models[0].Id : _models[0].Id + "_collaboration";
        collaborationId = plans.Any(plan => string.Equals(plan.Id, candidateId, StringComparison.Ordinal))
            ? candidateId + "_collaboration"
            : candidateId;
        return plans;
    }

    /// <summary>An element that belongs to no pool/lane hierarchy below the given process root.</summary>
    private bool IsLooseElement(ModelingElementDto element)
    {
        if (element.ParentId.Length == 0)
        {
            return false;
        }

        return !_elements.ContainsKey(element.ParentId);
    }

    private void WriteCollaboration(string collaborationId, List<ModelingElementDto> pools)
    {
        var collaboration = new XElement(Ns + "collaboration", new XAttribute("id", collaborationId));

        // Processes are written after the collaboration; WriteProcess patches processRef onto the
        // matching participant once the process id is known.
        foreach (var pool in pools)
        {
            var participant = new XElement(Ns + "participant",
                new XAttribute("id", pool.Id),
                new XAttribute("name", pool.Name));
            WriteCatchAllAttributes(participant, pool);
            collaboration.Add(participant);
        }

        foreach (var messageFlow in CollectMessageFlows())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var flow = new XElement(Ns + "messageFlow",
                new XAttribute("id", messageFlow.Id),
                new XAttribute("sourceRef", messageFlow.SourceElementId),
                new XAttribute("targetRef", messageFlow.TargetElementId));
            if (messageFlow.Name.Length > 0)
            {
                flow.Add(new XAttribute("name", messageFlow.Name));
            }

            WriteRelationshipCatchAllAttributes(flow, messageFlow);
            collaboration.Add(flow);
        }

        _root.Add(collaboration);
        _pendingParticipants = (collaboration, pools);
    }

    private (XElement Collaboration, List<ModelingElementDto> Pools)? _pendingParticipants;

    private void AttachProcessRef(string poolId, string processId)
    {
        if (_pendingParticipants is not { } pending)
        {
            return;
        }

        var participant = pending.Collaboration.Elements(Ns + "participant")
            .FirstOrDefault(candidate => candidate.Attribute("id")?.Value == poolId);
        participant?.Add(new XAttribute("processRef", processId));
    }

    private void AddLoss(string? elementId, string construct, string reason,
        BpmnExportLossSeverity severity = BpmnExportLossSeverity.Loss) =>
        _losses.Add(new BpmnExportLoss { ElementId = elementId, Construct = construct, Reason = reason, Severity = severity });

    private static string Serialize(XElement root)
    {
        var document = new XDocument(root);
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append(document.ToString(SaveOptions.None).Replace("\r\n", "\n", StringComparison.Ordinal));
        return builder.ToString();
    }

    private sealed record ProcessPlan(string Id, string Name, ModelingModelDto Model, string PoolId, string RootParentId);
}
