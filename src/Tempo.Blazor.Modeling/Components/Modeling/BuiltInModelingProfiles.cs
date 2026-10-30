using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

internal sealed class BpmnNotationProfile : IModelingNotationProfile
{
    public const string Key = "bpmn2";

    public string NotationKey => Key;

    public string DisplayName => "BPMN 2.0";

    public IReadOnlyCollection<string> SupportedElementTypes { get; } = BpmnModelingCatalog.ElementTypes;

    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = BpmnModelingCatalog.RelationshipTypes;

    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } =
    [
        "Collaboration",
        "Process",
        "Choreography",
        "Conversation"
    ];
}

internal sealed class BpmnLegacyModelingNotationProfile : IModelingNotationProfile
{
    public string NotationKey => "bpmn";

    public string DisplayName => "BPMN Legacy";

    public IReadOnlyCollection<string> SupportedElementTypes { get; } =
    [
        "task",
        "userTask",
        "serviceTask",
        "manualTask",
        "scriptTask",
        "businessRuleTask",
        "sendTask",
        "receiveTask",
        "subprocess",
        "startEvent",
        "intermediateEvent",
        "endEvent",
        "exclusiveGateway",
        "parallelGateway"
    ];

    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } =
    [
        "sequenceFlow",
        "conditionalFlow",
        "defaultFlow",
        "messageFlow",
        "association"
    ];

    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } =
    [
        "default",
        "overview",
        "process",
        "operations"
    ];
}

internal sealed class ArchimateModelingNotationProfile : IModelingNotationProfile
{
    public string NotationKey => "archimate";

    public string DisplayName => "ArchiMate 3";

    public IReadOnlyCollection<string> SupportedElementTypes { get; } =
    [
        "businessActor",
        "businessRole",
        "businessProcess",
        "applicationComponent",
        "applicationService",
        "applicationFunction",
        "dataObject",
        "technologyNode",
        "technologyService"
    ];

    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } =
    [
        "association",
        "triggering",
        "flow",
        "access",
        "serving",
        "realization",
        "assignment",
        "aggregation",
        "composition",
        "specialization",
        "influence"
    ];

    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } =
    [
        "default",
        "overview",
        "layered",
        "application",
        "business",
        "technology"
    ];
}

internal sealed class Archimate32NotationProfile : IModelingNotationProfile
{
    public const string Key = "archimate32";

    public string NotationKey => Key;

    public string DisplayName => "ArchiMate 3.2";

    public IReadOnlyCollection<string> SupportedElementTypes { get; } = Archimate32ModelingCatalog.ElementTypes;

    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } =
    [
        "Composition",
        "Aggregation",
        "Assignment",
        "Realization",
        "Serving",
        "Access",
        "Influence",
        "Triggering",
        "Flow",
        "Specialization",
        "Association"
    ];

    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } =
    [
        "Layered",
        "Strategy",
        "Business",
        "Application",
        "Technology",
        "Physical",
        "Organization",
        "BusinessProcess",
        "ApplicationUsage",
        "ApplicationCooperation",
        "ApplicationStructure",
        "TechnologyUsage",
        "TechnologyCooperation",
        "TechnologyInfrastructure",
        "PhysicalEnvironment",
        "Motivation",
        "Implementation",
        "Migration",
        "MigrationMap",
        "StrategyEnvironment",
        "Capability",
        "ValueStream",
        "ProjectStatus",
        "LandscapeMap"
    ];

    public bool EnforcesStrictStencilMapping => true;
}

internal sealed class UmlNotationProfile : IModelingNotationProfile
{
    public const string Key = "uml25";

    public string NotationKey => Key;

    public string DisplayName => "UML 2.5";

    public IReadOnlyCollection<string> SupportedElementTypes { get; } = UmlModelingCatalog.ElementTypes;

    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } = UmlModelingCatalog.RelationshipTypes;

    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } =
    [
        "ClassDiagram",
        "PackageDiagram",
        "UseCaseDiagram",
        "ActivityDiagram",
        "StateMachineDiagram",
        "SequenceDiagram",
        "CommunicationDiagram",
        "ComponentDiagram",
        "DeploymentDiagram",
        "ObjectDiagram"
    ];
}

/// <summary>Optional Entity Relationship Diagram notation profile for consumer-registered modeling scenarios.</summary>
public sealed class ErdNotationProfile : IModelingNotationProfile
{
    /// <summary>Stable ERD notation key.</summary>
    public const string Key = "erd";

    /// <inheritdoc />
    public string NotationKey => Key;

    /// <inheritdoc />
    public string DisplayName => "ERD";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedElementTypes { get; } =
    [
        "Entity",
        "WeakEntity",
        "Attribute",
        "MultiValuedAttribute",
        "DerivedAttribute",
        "KeyAttribute",
        "RelationshipSet",
        "WeakRelationshipSet"
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedRelationshipTypes { get; } =
    [
        "OneToOne",
        "OneToMany",
        "ManyToMany",
        "Identifying",
        "NonIdentifying"
    ];

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedViewpointKeys { get; } = [];

    /// <inheritdoc />
    public bool EnforcesStrictStencilMapping => true;
}

/// <summary>ERD stencil mapper placeholder. It intentionally returns no mappings until a consumer registers ERD stencils.</summary>
public sealed class ErdStencilMapper : IModelingStencilMapper
{
    /// <inheritdoc />
    public string? GetStencilId(string notationKey, string semanticType)
        => null;

    /// <inheritdoc />
    public string? GetEdgeStencilId(string notationKey, string relationshipType)
        => null;
}

internal sealed class BuiltInModelingRelationshipRulesProvider : IModelingRelationshipRulesProvider
{
    private readonly IModelingNotationProfileProvider _profiles;
    private readonly IReadOnlyDictionary<string, IModelingNotationRelationshipRulesProvider> _notationRules;

    public BuiltInModelingRelationshipRulesProvider(
        IModelingNotationProfileProvider profiles,
        IEnumerable<IModelingNotationRelationshipRulesProvider>? notationRules = null)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _notationRules = CreateNotationRulesMap(notationRules ?? CreateDefaultNotationRules(profiles));
    }

    public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
    {
        if (TryGetNotationRules(notationKey, out var rules))
            return rules.IsValidRelationship(notationKey, sourceType, targetType, relationshipType);

        var profile = _profiles.GetProfile(notationKey);
        return profile is not null
            && Contains(profile.SupportedElementTypes, sourceType)
            && Contains(profile.SupportedElementTypes, targetType)
            && Contains(profile.SupportedRelationshipTypes, relationshipType);
    }

    public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (TryGetNotationRules(context.NotationKey, out var rules))
            return rules.ValidateRelationship(context);

        return IsValidRelationship(
            context.NotationKey,
            context.SourceElement.SemanticType,
            context.TargetElement.SemanticType,
            context.Relationship.RelationshipType)
            ? ModelingRelationshipRuleResult.Valid
            : ModelingRelationshipRuleResult.Invalid(
                "Relationship is not valid for the selected notation.",
                "Use a supported relationship type for the source and target element types.");
    }

    private bool TryGetNotationRules(string notationKey, out IModelingNotationRelationshipRulesProvider rules)
    {
        if (!string.IsNullOrWhiteSpace(notationKey)
            && _notationRules.TryGetValue(notationKey.Trim(), out rules!))
        {
            return true;
        }

        rules = null!;
        return false;
    }

    private static IReadOnlyList<IModelingNotationRelationshipRulesProvider> CreateDefaultNotationRules(IModelingNotationProfileProvider profiles) =>
    [
        new BpmnRelationshipRulesProvider(profiles),
        new UmlRelationshipRulesProvider(profiles),
        new Archimate32RelationshipRulesProvider(profiles)
    ];

    private static IReadOnlyDictionary<string, IModelingNotationRelationshipRulesProvider> CreateNotationRulesMap(IEnumerable<IModelingNotationRelationshipRulesProvider> rulesProviders)
    {
        var map = new Dictionary<string, IModelingNotationRelationshipRulesProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var rulesProvider in rulesProviders)
        {
            foreach (var notationKey in rulesProvider.NotationKeys)
            {
                if (!string.IsNullOrWhiteSpace(notationKey))
                    map.TryAdd(notationKey.Trim(), rulesProvider);
            }
        }

        return map;
    }

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;
}

internal sealed class BuiltInModelingViewpointRulesProvider : IModelingViewpointRulesProvider
{
    private readonly IModelingViewpointRulesProvider _fallback;
    private readonly IReadOnlyDictionary<string, IModelingNotationViewpointRulesProvider> _notationRules;

    public BuiltInModelingViewpointRulesProvider(
        IModelingNotationProfileProvider profiles,
        IEnumerable<IModelingNotationViewpointRulesProvider>? notationRules = null)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        _fallback = new ModelingViewpointRulesProvider(profiles);
        _notationRules = CreateNotationRulesMap(notationRules ?? CreateDefaultNotationRules(profiles));
    }

    public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
        => TryGetNotationRules(notationKey, out var rules)
            ? rules.IsElementAllowedInViewpoint(notationKey, viewpointKey, semanticType)
            : _fallback.IsElementAllowedInViewpoint(notationKey, viewpointKey, semanticType);

    public ModelingViewpointRuleResult ValidateElementViewpoint(ModelingViewpointRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return TryGetNotationRules(context.NotationKey, out var rules)
            ? rules.ValidateElementViewpoint(context)
            : _fallback.ValidateElementViewpoint(context);
    }

    private bool TryGetNotationRules(string notationKey, out IModelingNotationViewpointRulesProvider rules)
    {
        if (!string.IsNullOrWhiteSpace(notationKey)
            && _notationRules.TryGetValue(notationKey.Trim(), out rules!))
        {
            return true;
        }

        rules = null!;
        return false;
    }

    private static IReadOnlyList<IModelingNotationViewpointRulesProvider> CreateDefaultNotationRules(IModelingNotationProfileProvider profiles) =>
    [
        new BpmnViewpointRulesProvider(profiles),
        new UmlViewpointRulesProvider(profiles),
        new Archimate32ViewpointRulesProvider()
    ];

    private static IReadOnlyDictionary<string, IModelingNotationViewpointRulesProvider> CreateNotationRulesMap(IEnumerable<IModelingNotationViewpointRulesProvider> rulesProviders)
    {
        var map = new Dictionary<string, IModelingNotationViewpointRulesProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var rulesProvider in rulesProviders)
        {
            foreach (var notationKey in rulesProvider.NotationKeys)
            {
                if (!string.IsNullOrWhiteSpace(notationKey))
                    map.TryAdd(notationKey.Trim(), rulesProvider);
            }
        }

        return map;
    }
}

internal sealed class BpmnViewpointRulesProvider : IModelingNotationViewpointRulesProvider
{
    private readonly IModelingNotationProfileProvider _profiles;
    private readonly IModelingViewpointRulesProvider _fallback;

    public BpmnViewpointRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _fallback = new ModelingViewpointRulesProvider(profiles);
    }

    public IReadOnlyCollection<string> NotationKeys { get; } = [BpmnNotationProfile.Key, "bpmn"];

    public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
    {
        if (!IsBpmnNotation(notationKey) || string.IsNullOrWhiteSpace(semanticType))
            return false;

        return _fallback.IsElementAllowedInViewpoint(notationKey, viewpointKey, semanticType)
            || (IsKnownViewpoint(notationKey, viewpointKey) && IsTaskLikeSemanticType(semanticType));
    }

    public ModelingViewpointRuleResult ValidateElementViewpoint(ModelingViewpointRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsBpmnNotation(context.NotationKey))
            return ModelingViewpointRuleResult.Allowed;

        if (string.IsNullOrWhiteSpace(context.ViewpointKey)
            || _fallback.IsElementAllowedInViewpoint(context.NotationKey, context.ViewpointKey, context.Element.SemanticType))
        {
            return ModelingViewpointRuleResult.Allowed;
        }

        if (IsKnownViewpoint(context.NotationKey, context.ViewpointKey)
            && IsTaskLikeSemanticType(context.Element.SemanticType))
        {
            return ModelingViewpointRuleResult.Warning(
                $"BPMN task type '{context.Element.SemanticType}' is not part of the built-in BPMN catalog and will use a generic fallback stencil.",
                "Register a BPMN stencil mapping for this task type if it should render with a specialized symbol.");
        }

        return _fallback.ValidateElementViewpoint(context);
    }

    private static bool IsBpmnNotation(string notationKey)
        => string.Equals(notationKey?.Trim(), BpmnNotationProfile.Key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(notationKey?.Trim(), "bpmn", StringComparison.OrdinalIgnoreCase);

    private bool IsKnownViewpoint(string notationKey, string viewpointKey)
        => !string.IsNullOrWhiteSpace(viewpointKey)
            && _profiles.GetProfile(notationKey)?.SupportedViewpointKeys.Any(candidate =>
                string.Equals(candidate, viewpointKey.Trim(), StringComparison.OrdinalIgnoreCase)) == true;

    private static bool IsTaskLikeSemanticType(string semanticType)
        => semanticType.Trim().EndsWith("Task", StringComparison.OrdinalIgnoreCase);
}

internal sealed class UmlViewpointRulesProvider : IModelingNotationViewpointRulesProvider
{
    private readonly IModelingViewpointRulesProvider _fallback;

    public UmlViewpointRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _fallback = new ModelingViewpointRulesProvider(profiles);
    }

    public IReadOnlyCollection<string> NotationKeys { get; } = [UmlNotationProfile.Key];

    public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
        => _fallback.IsElementAllowedInViewpoint(notationKey, viewpointKey, semanticType);

    public ModelingViewpointRuleResult ValidateElementViewpoint(ModelingViewpointRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!string.Equals(context.NotationKey?.Trim(), UmlNotationProfile.Key, StringComparison.OrdinalIgnoreCase))
            return ModelingViewpointRuleResult.Allowed;

        if (string.Equals(context.Element.SemanticType, "Actor", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(context.ViewpointKey)
            && !string.Equals(context.ViewpointKey, "UseCaseDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ModelingViewpointRuleResult.Warning(
                "UML Actor is normally used in UseCaseDiagram viewpoint.",
                "Switch to UseCaseDiagram or replace the Actor with a viewpoint-appropriate UML element.");
        }

        return _fallback.ValidateElementViewpoint(context);
    }
}

internal sealed class Archimate32ViewpointRulesProvider : IModelingNotationViewpointRulesProvider
{
    public IReadOnlyCollection<string> NotationKeys { get; } = [Archimate32NotationProfile.Key];

    public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
    {
        if (!string.Equals(notationKey?.Trim(), Archimate32NotationProfile.Key, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(semanticType))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(viewpointKey))
            return true;

        if (IsAlwaysAllowed(semanticType))
            return true;

        return ArchimateViewpointCatalog.IsElementAllowed(viewpointKey, semanticType);
    }

    private static bool IsAlwaysAllowed(string semanticType)
        => string.Equals(semanticType.Trim(), "Junction", StringComparison.OrdinalIgnoreCase)
            || string.Equals(semanticType.Trim(), "Grouping", StringComparison.OrdinalIgnoreCase);
}

internal sealed class BpmnRelationshipRulesProvider : IModelingNotationRelationshipRulesProvider
{
    private static readonly HashSet<string> FlowNodeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Task",
        "UserTask",
        "ServiceTask",
        "SendTask",
        "ReceiveTask",
        "ScriptTask",
        "BusinessRuleTask",
        "ManualTask",
        "SubProcess",
        "EventSubProcess",
        "CallActivity",
        "StartEvent",
        "EndEvent",
        "IntermediateCatchEvent",
        "IntermediateThrowEvent",
        "BoundaryEvent",
        "ExclusiveGateway",
        "ParallelGateway",
        "InclusiveGateway",
        "ComplexGateway",
        "EventBasedGateway",
        "task",
        "userTask",
        "serviceTask",
        "sendTask",
        "receiveTask",
        "scriptTask",
        "businessRuleTask",
        "manualTask",
        "subprocess",
        "startEvent",
        "intermediateEvent",
        "endEvent",
        "exclusiveGateway",
        "parallelGateway"
    };

    private readonly IModelingNotationProfileProvider _profiles;

    public BpmnRelationshipRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public IReadOnlyCollection<string> NotationKeys { get; } = [BpmnNotationProfile.Key, "bpmn"];

    public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(notationKey)
            || string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(relationshipType))
        {
            return false;
        }

        if (!IsBpmn(notationKey))
        {
            var profile = _profiles.GetProfile(notationKey);
            return profile is not null
                && Contains(profile.SupportedElementTypes, sourceType)
                && Contains(profile.SupportedElementTypes, targetType)
                && Contains(profile.SupportedRelationshipTypes, relationshipType);
        }

        return relationshipType switch
        {
            "SequenceFlow" or "sequenceFlow" or "ConditionalFlow" or "conditionalFlow" or "DefaultFlow" or "defaultFlow"
                => FlowNodeTypes.Contains(sourceType) && FlowNodeTypes.Contains(targetType),
            "MessageFlow" or "messageFlow"
                => IsPool(sourceType) && IsPool(targetType),
            "Association" or "association" or "DataInputAssociation" or "DataOutputAssociation" or "dataAssociation"
                => IsKnownBpmnElement(sourceType) && IsKnownBpmnElement(targetType),
            _ => false
        };
    }

    public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsBpmn(context.NotationKey))
        {
            return ModelingRelationshipRuleResult.Valid;
        }

        var relationshipType = context.Relationship.RelationshipType;
        if (IsSequenceFlow(relationshipType))
        {
            if (!FlowNodeTypes.Contains(context.SourceElement.SemanticType)
                || !FlowNodeTypes.Contains(context.TargetElement.SemanticType))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "BPMN SequenceFlow can connect only BPMN flow nodes.",
                    "Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.");
            }

            var sourceContainer = ResolveBpmnContainerId(context.SourceElement, context.ElementsById);
            var targetContainer = ResolveBpmnContainerId(context.TargetElement, context.ElementsById);
            if (!string.IsNullOrWhiteSpace(sourceContainer)
                && !string.IsNullOrWhiteSpace(targetContainer)
                && !string.Equals(sourceContainer, targetContainer, StringComparison.OrdinalIgnoreCase))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "BPMN SequenceFlow cannot cross Pool or SubProcess boundaries.",
                    "Use MessageFlow between Pools or keep SequenceFlow inside the same Pool/SubProcess.");
            }

            return ModelingRelationshipRuleResult.Valid;
        }

        if (IsMessageFlow(relationshipType))
        {
            var sourcePool = ResolveBpmnPoolId(context.SourceElement, context.ElementsById);
            var targetPool = ResolveBpmnPoolId(context.TargetElement, context.ElementsById);
            if (!string.IsNullOrWhiteSpace(sourcePool)
                && !string.IsNullOrWhiteSpace(targetPool)
                && !string.Equals(sourcePool, targetPool, StringComparison.OrdinalIgnoreCase))
            {
                return ModelingRelationshipRuleResult.Valid;
            }

            return ModelingRelationshipRuleResult.Invalid(
                "BPMN MessageFlow must connect different Pools.",
                "Use SequenceFlow inside one Pool, or assign the source and target to different Pools.");
        }

        return IsValidRelationship(
            context.NotationKey,
            context.SourceElement.SemanticType,
            context.TargetElement.SemanticType,
            relationshipType)
            ? ModelingRelationshipRuleResult.Valid
            : ModelingRelationshipRuleResult.Invalid(
                $"BPMN relationship '{relationshipType}' is not supported for the selected elements.",
                "Choose a BPMN relationship type supported by this profile.");
    }

    private static bool IsSequenceFlow(string relationshipType)
        => string.Equals(relationshipType, "SequenceFlow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relationshipType, "sequenceFlow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relationshipType, "ConditionalFlow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relationshipType, "DefaultFlow", StringComparison.OrdinalIgnoreCase);

    private static bool IsMessageFlow(string relationshipType)
        => string.Equals(relationshipType, "MessageFlow", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relationshipType, "messageFlow", StringComparison.OrdinalIgnoreCase);

    private static bool IsBpmn(string notationKey)
        => string.Equals(notationKey, BpmnNotationProfile.Key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(notationKey, "bpmn", StringComparison.OrdinalIgnoreCase);

    private static bool IsKnownBpmnElement(string semanticType)
        => BpmnModelingCatalog.ElementTypes.Contains(semanticType, StringComparer.OrdinalIgnoreCase)
            || FlowNodeTypes.Contains(semanticType)
            || IsPool(semanticType)
            || string.Equals(semanticType, "Lane", StringComparison.OrdinalIgnoreCase);

    private static bool IsPool(string semanticType)
        => string.Equals(semanticType, "Pool", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;

    private static string ResolveBpmnContainerId(ModelingElementDto element, IReadOnlyDictionary<string, ModelingElementDto> elementsById)
    {
        var declared = FirstNonEmptyProperty(element, "subProcessId", "subprocessId", "poolId", "pool");
        if (!string.IsNullOrWhiteSpace(declared))
            return declared;

        var pool = ResolveBpmnPoolId(element, elementsById);
        if (!string.IsNullOrWhiteSpace(pool))
            return pool;

        // Phase 12: the semantic ParentId is the container when the model reports containment
        // through the tree edge only (no poolId/subProcessId/parentId property) — the walk
        // above already returned every Pool above the element, so its DIRECT parent is the
        // remaining container (typically the owning SubProcess).
        return element.ParentId;
    }

    private static string ResolveBpmnPoolId(ModelingElementDto element, IReadOnlyDictionary<string, ModelingElementDto> elementsById)
    {
        if (IsPool(element.SemanticType))
            return element.Id;

        var current = element;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            // Declared containment properties first; the semantic ParentId (phase 12) is the
            // fallback when the model reports the parent through the tree edge only.
            var parentId = FirstNonEmptyProperty(current, "poolId", "pool", "parentId", "containerId")
                ?? current.ParentId;
            if (string.IsNullOrWhiteSpace(parentId))
                return string.Empty;

            if (!elementsById.TryGetValue(parentId, out var parent))
                return parentId;

            if (IsPool(parent.SemanticType))
                return parent.Id;

            if (!visited.Add(parent.Id))
                return string.Empty;

            current = parent;
        }
    }

    private static string? FirstNonEmptyProperty(ModelingElementDto element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.Properties.TryGetValue(name, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }
        }

        return null;
    }
}

internal sealed class UmlRelationshipRulesProvider : IModelingNotationRelationshipRulesProvider
{
    private static readonly HashSet<string> ClassifierTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Class",
        "Interface",
        "AbstractClass",
        "Enumeration",
        "Component",
        "Node",
        "Artifact",
        "UseCase",
        "Actor",
        "State",
        "Activity",
        "Action",
        "ObjectNode",
        "Lifeline",
        "CombinedFragment",
        "Collaboration"
    };

    private readonly IModelingNotationProfileProvider _profiles;

    public UmlRelationshipRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public IReadOnlyCollection<string> NotationKeys { get; } = [UmlNotationProfile.Key];

    public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
    {
        if (!IsUml(notationKey)
            || string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(relationshipType))
        {
            return false;
        }

        var profile = _profiles.GetProfile(notationKey);
        if (profile is null
            || !Contains(profile.SupportedElementTypes, sourceType)
            || !Contains(profile.SupportedElementTypes, targetType)
            || !Contains(profile.SupportedRelationshipTypes, relationshipType))
        {
            return false;
        }

        return relationshipType switch
        {
            "Include" or "Extend" => IsUseCase(sourceType) && IsUseCase(targetType),
            "Generalization" or "Realization" => ClassifierTypes.Contains(sourceType) && ClassifierTypes.Contains(targetType),
            "Message" => IsSequenceParticipant(sourceType) && IsSequenceParticipant(targetType),
            _ => true
        };
    }

    public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsUml(context.NotationKey))
            return ModelingRelationshipRuleResult.Valid;

        if (IsIncludeOrExtend(context.Relationship.RelationshipType))
        {
            if (!string.Equals(context.ViewpointKey, "UseCaseDiagram", StringComparison.OrdinalIgnoreCase))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "UML Include and Extend relationships are allowed only in UseCaseDiagram.",
                    "Switch to UseCaseDiagram or use Dependency/Association in this viewpoint.");
            }

            if (!IsUseCase(context.SourceElement.SemanticType) || !IsUseCase(context.TargetElement.SemanticType))
            {
                return ModelingRelationshipRuleResult.Invalid(
                    "UML Include and Extend relationships must connect UseCase elements.",
                    "Connect two UseCase elements, or choose another relationship type.");
            }
        }

        if (string.Equals(context.Relationship.RelationshipType, "Generalization", StringComparison.OrdinalIgnoreCase)
            && string.Equals(context.ViewpointKey, "DeploymentDiagram", StringComparison.OrdinalIgnoreCase))
        {
            return ClassifierTypes.Contains(context.SourceElement.SemanticType)
                && ClassifierTypes.Contains(context.TargetElement.SemanticType)
                ? ModelingRelationshipRuleResult.Valid
                : ModelingRelationshipRuleResult.Invalid(
                    "UML Generalization in DeploymentDiagram can connect only classifier-like deployment elements.",
                    "Use Node, Artifact, Component, or another classifier-like UML element.");
        }

        return IsValidRelationship(
            context.NotationKey,
            context.SourceElement.SemanticType,
            context.TargetElement.SemanticType,
            context.Relationship.RelationshipType)
            ? ModelingRelationshipRuleResult.Valid
            : ModelingRelationshipRuleResult.Invalid(
                $"UML relationship '{context.Relationship.RelationshipType}' is not supported for the selected elements.",
                "Choose a UML relationship type supported by this profile and element pair.");
    }

    private static bool IsUml(string notationKey)
        => string.Equals(notationKey?.Trim(), UmlNotationProfile.Key, StringComparison.OrdinalIgnoreCase);

    private static bool IsUseCase(string semanticType)
        => string.Equals(semanticType, "UseCase", StringComparison.OrdinalIgnoreCase);

    private static bool IsIncludeOrExtend(string relationshipType)
        => string.Equals(relationshipType, "Include", StringComparison.OrdinalIgnoreCase)
            || string.Equals(relationshipType, "Extend", StringComparison.OrdinalIgnoreCase);

    private static bool IsSequenceParticipant(string semanticType)
        => string.Equals(semanticType, "Lifeline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(semanticType, "Actor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(semanticType, "ObjectNode", StringComparison.OrdinalIgnoreCase)
            || string.Equals(semanticType, "Class", StringComparison.OrdinalIgnoreCase)
            || string.Equals(semanticType, "Interface", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;
}

internal sealed class Archimate32RelationshipRulesProvider : IModelingNotationRelationshipRulesProvider
{
    private readonly IModelingNotationProfileProvider _profiles;

    public Archimate32RelationshipRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    public IReadOnlyCollection<string> NotationKeys { get; } = [Archimate32NotationProfile.Key];

    public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
    {
        if (!IsArchimate32(notationKey)
            || string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(relationshipType))
        {
            return false;
        }

        var profile = _profiles.GetProfile(notationKey);
        return profile is not null
            && Contains(profile.SupportedElementTypes, sourceType)
            && Contains(profile.SupportedElementTypes, targetType)
            && Contains(profile.SupportedRelationshipTypes, relationshipType)
            && ArchimateRelationshipMatrix.IsValid(sourceType, targetType, relationshipType);
    }

    public ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!IsArchimate32(context.NotationKey))
            return ModelingRelationshipRuleResult.Valid;

        return IsValidRelationship(
            context.NotationKey,
            context.SourceElement.SemanticType,
            context.TargetElement.SemanticType,
            context.Relationship.RelationshipType)
            ? ModelingRelationshipRuleResult.Valid
            : ModelingRelationshipRuleResult.Invalid(
                $"ArchiMate 3.2 relationship '{context.Relationship.RelationshipType}' is not valid between {context.SourceElement.SemanticType} and {context.TargetElement.SemanticType}.",
                "Choose an ArchiMate relationship allowed by the ArchiMate 3.2 relationship matrix for these element types.");
    }

    private static bool IsArchimate32(string notationKey)
        => string.Equals(notationKey?.Trim(), Archimate32NotationProfile.Key, StringComparison.OrdinalIgnoreCase);

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;
}

internal static class ArchimateRelationshipMatrix
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ElementGroups =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["All"] = Archimate32ModelingCatalog.ElementTypes,
            ["Strategy"] =
            [
                "Capability",
                "CourseOfAction",
                "Resource",
                "ValueStream"
            ],
            ["Business"] =
            [
                "BusinessActor",
                "BusinessRole",
                "BusinessCollaboration",
                "BusinessInterface",
                "BusinessProcess",
                "BusinessFunction",
                "BusinessInteraction",
                "BusinessEvent",
                "BusinessService",
                "BusinessObject",
                "Contract",
                "Representation",
                "Product"
            ],
            ["Application"] =
            [
                "ApplicationComponent",
                "ApplicationCollaboration",
                "ApplicationInterface",
                "ApplicationFunction",
                "ApplicationInteraction",
                "ApplicationProcess",
                "ApplicationEvent",
                "ApplicationService",
                "DataObject"
            ],
            ["Technology"] =
            [
                "Node",
                "Device",
                "SystemSoftware",
                "TechnologyCollaboration",
                "TechnologyInterface",
                "Path",
                "CommunicationNetwork",
                "TechnologyFunction",
                "TechnologyProcess",
                "TechnologyInteraction",
                "TechnologyEvent",
                "TechnologyService",
                "Artifact"
            ],
            ["Physical"] =
            [
                "Equipment",
                "Facility",
                "DistributionNetwork",
                "Material"
            ],
            ["Motivation"] =
            [
                "Stakeholder",
                "Driver",
                "Assessment",
                "Goal",
                "Outcome",
                "Principle",
                "Requirement",
                "Constraint",
                "Meaning",
                "Value"
            ],
            ["Implementation"] =
            [
                "WorkPackage",
                "Deliverable",
                "ImplementationEvent",
                "Plateau",
                "Gap"
            ],
            ["BusinessStructure"] = ["BusinessActor", "BusinessRole", "BusinessCollaboration"],
            ["BusinessBehavior"] = ["BusinessProcess", "BusinessFunction", "BusinessInteraction"],
            ["BusinessPassive"] = ["BusinessObject", "Contract", "Representation", "Product"],
            ["ApplicationStructure"] = ["ApplicationComponent", "ApplicationCollaboration"],
            ["ApplicationBehavior"] = ["ApplicationFunction", "ApplicationInteraction", "ApplicationProcess"],
            ["ApplicationPassive"] = ["DataObject"],
            ["TechnologyStructure"] = ["Node", "Device", "SystemSoftware", "TechnologyCollaboration", "Path", "CommunicationNetwork"],
            ["TechnologyBehavior"] = ["TechnologyFunction", "TechnologyProcess", "TechnologyInteraction"],
            ["TechnologyPassive"] = ["Artifact"],
            ["PhysicalStructure"] = ["Equipment", "Facility", "DistributionNetwork"],
            ["PhysicalPassive"] = ["Material"],
            ["Structure"] =
            [
                "BusinessActor",
                "BusinessRole",
                "BusinessCollaboration",
                "BusinessInterface",
                "ApplicationComponent",
                "ApplicationCollaboration",
                "ApplicationInterface",
                "Node",
                "Device",
                "SystemSoftware",
                "TechnologyCollaboration",
                "TechnologyInterface",
                "Path",
                "CommunicationNetwork",
                "Equipment",
                "Facility",
                "DistributionNetwork"
            ],
            ["Behavior"] =
            [
                "Capability",
                "CourseOfAction",
                "ValueStream",
                "BusinessProcess",
                "BusinessFunction",
                "BusinessInteraction",
                "BusinessEvent",
                "ApplicationFunction",
                "ApplicationInteraction",
                "ApplicationProcess",
                "ApplicationEvent",
                "TechnologyFunction",
                "TechnologyProcess",
                "TechnologyInteraction",
                "TechnologyEvent",
                "WorkPackage",
                "ImplementationEvent"
            ],
            ["Passive"] =
            [
                "Resource",
                "BusinessObject",
                "Contract",
                "Representation",
                "Product",
                "DataObject",
                "Artifact",
                "Material",
                "Deliverable",
                "Plateau",
                "Gap",
                "Meaning",
                "Value"
            ],
            ["Service"] = ["BusinessService", "ApplicationService", "TechnologyService"],
            ["Interface"] = ["BusinessInterface", "ApplicationInterface", "TechnologyInterface"],
            ["Event"] = ["BusinessEvent", "ApplicationEvent", "TechnologyEvent", "ImplementationEvent"],
            ["MotivationCore"] = ["Stakeholder", "Driver", "Assessment", "Goal", "Outcome", "Principle", "Requirement", "Constraint", "Meaning", "Value"],
            ["ImplementationCore"] = ["WorkPackage", "Deliverable", "ImplementationEvent", "Plateau", "Gap"],
            ["Grouping"] = ["Grouping"],
            ["Junction"] = ["Junction"],
            ["Location"] = ["Location"]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<MatrixRule>> Rules =
        new Dictionary<string, IReadOnlyCollection<MatrixRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Association"] = [new("All", "All")],
            ["Specialization"] = [new("All", "All", SameType: true)],
            ["Composition"] =
            [
                new("Strategy", "Strategy"),
                new("Business", "Business"),
                new("Application", "Application"),
                new("Technology", "Technology"),
                new("Physical", "Physical"),
                new("Motivation", "Motivation"),
                new("Implementation", "Implementation"),
                new("Grouping", "All")
            ],
            ["Aggregation"] =
            [
                new("Strategy", "Strategy"),
                new("Business", "Business"),
                new("Application", "Application"),
                new("Technology", "Technology"),
                new("Physical", "Physical"),
                new("Motivation", "Motivation"),
                new("Implementation", "Implementation"),
                new("Grouping", "All")
            ],
            ["Assignment"] =
            [
                new("BusinessStructure", "BusinessBehavior"),
                new("BusinessStructure", "BusinessService"),
                new("ApplicationStructure", "ApplicationBehavior"),
                new("ApplicationStructure", "ApplicationService"),
                new("TechnologyStructure", "TechnologyBehavior"),
                new("TechnologyStructure", "TechnologyService"),
                new("PhysicalStructure", "PhysicalPassive"),
                new("WorkPackage", "Deliverable"),
                new("Stakeholder", "Driver"),
                new("Stakeholder", "Goal")
            ],
            ["Realization"] =
            [
                new("Capability", "BusinessService"),
                new("BusinessBehavior", "BusinessService"),
                new("BusinessPassive", "BusinessService"),
                new("ApplicationStructure", "ApplicationService"),
                new("ApplicationBehavior", "ApplicationService"),
                new("ApplicationPassive", "ApplicationService"),
                new("TechnologyBehavior", "TechnologyService"),
                new("TechnologyPassive", "TechnologyService"),
                new("TechnologyStructure", "Artifact"),
                new("Requirement", "Goal"),
                new("Requirement", "Outcome"),
                new("Constraint", "Requirement"),
                new("Deliverable", "WorkPackage"),
                new("Plateau", "Gap"),
                new("Resource", "Capability")
            ],
            ["Serving"] =
            [
                new("BusinessService", "BusinessBehavior"),
                new("BusinessInterface", "BusinessStructure"),
                new("ApplicationService", "BusinessBehavior"),
                new("ApplicationService", "BusinessStructure"),
                new("ApplicationService", "ApplicationBehavior"),
                new("ApplicationInterface", "ApplicationStructure"),
                new("TechnologyService", "ApplicationStructure"),
                new("TechnologyService", "ApplicationBehavior"),
                new("TechnologyService", "TechnologyBehavior"),
                new("TechnologyInterface", "TechnologyStructure"),
                new("Path", "Node"),
                new("CommunicationNetwork", "Node"),
                new("Facility", "Equipment")
            ],
            ["Access"] =
            [
                new("BusinessBehavior", "BusinessPassive"),
                new("BusinessService", "BusinessPassive"),
                new("ApplicationBehavior", "ApplicationPassive"),
                new("ApplicationService", "ApplicationPassive"),
                new("TechnologyBehavior", "TechnologyPassive"),
                new("TechnologyService", "TechnologyPassive"),
                new("WorkPackage", "Deliverable")
            ],
            ["Influence"] =
            [
                new("MotivationCore", "MotivationCore"),
                new("Strategy", "MotivationCore"),
                new("MotivationCore", "Strategy"),
                new("ImplementationCore", "MotivationCore")
            ],
            ["Triggering"] =
            [
                new("BusinessBehavior", "BusinessBehavior"),
                new("BusinessEvent", "BusinessBehavior"),
                new("BusinessBehavior", "ApplicationBehavior"),
                new("ApplicationBehavior", "ApplicationBehavior"),
                new("ApplicationEvent", "ApplicationBehavior"),
                new("ApplicationBehavior", "TechnologyBehavior"),
                new("TechnologyBehavior", "TechnologyBehavior"),
                new("TechnologyEvent", "TechnologyBehavior"),
                new("ImplementationEvent", "WorkPackage"),
                new("WorkPackage", "ImplementationEvent")
            ],
            ["Flow"] =
            [
                new("BusinessBehavior", "BusinessBehavior"),
                new("BusinessBehavior", "ApplicationBehavior"),
                new("ApplicationBehavior", "BusinessBehavior"),
                new("ApplicationBehavior", "ApplicationBehavior"),
                new("ApplicationBehavior", "TechnologyBehavior"),
                new("TechnologyBehavior", "ApplicationBehavior"),
                new("TechnologyBehavior", "TechnologyBehavior"),
                new("WorkPackage", "WorkPackage")
            ]
        };

    public static bool IsValid(string sourceType, string targetType, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)
            || string.IsNullOrWhiteSpace(targetType)
            || string.IsNullOrWhiteSpace(relationshipType)
            || !ContainsGroup("All", sourceType)
            || !ContainsGroup("All", targetType)
            || !Rules.TryGetValue(relationshipType.Trim(), out var rules))
        {
            return false;
        }

        var source = sourceType.Trim();
        var target = targetType.Trim();
        return rules.Any(rule => rule.Matches(source, target));
    }

    private static bool ContainsGroup(string groupName, string semanticType)
    {
        if (ElementGroups.TryGetValue(groupName, out var group))
            return group.Contains(semanticType, StringComparer.OrdinalIgnoreCase);

        return Archimate32ModelingCatalog.ElementTypes.Contains(groupName, StringComparer.OrdinalIgnoreCase)
            && string.Equals(groupName, semanticType, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record MatrixRule(string SourceGroup, string TargetGroup, bool SameType = false)
    {
        public bool Matches(string sourceType, string targetType)
        {
            if (SameType && !string.Equals(sourceType, targetType, StringComparison.OrdinalIgnoreCase))
                return false;

            return ContainsGroup(SourceGroup, sourceType)
                && ContainsGroup(TargetGroup, targetType);
        }
    }
}

internal static class ArchimateViewpointCatalog
{
    public static readonly IReadOnlyCollection<string> Archimate32Viewpoints =
    [
        "Organization",
        "BusinessProcess",
        "ApplicationUsage",
        "ApplicationCooperation",
        "ApplicationStructure",
        "TechnologyUsage",
        "TechnologyCooperation",
        "TechnologyInfrastructure",
        "PhysicalEnvironment",
        "Implementation",
        "MigrationMap",
        "Motivation",
        "StrategyEnvironment",
        "Capability",
        "ValueStream",
        "ProjectStatus",
        "LandscapeMap"
    ];

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ElementGroups =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["All"] = Archimate32ModelingCatalog.ElementTypes,
            ["Strategy"] =
            [
                "Capability",
                "CourseOfAction",
                "Resource",
                "ValueStream"
            ],
            ["Business"] =
            [
                "BusinessActor",
                "BusinessRole",
                "BusinessCollaboration",
                "BusinessInterface",
                "BusinessProcess",
                "BusinessFunction",
                "BusinessInteraction",
                "BusinessEvent",
                "BusinessService",
                "BusinessObject",
                "Contract",
                "Representation",
                "Product"
            ],
            ["BusinessProcessCore"] =
            [
                "BusinessProcess",
                "BusinessFunction",
                "BusinessInteraction",
                "BusinessEvent",
                "BusinessService",
                "BusinessObject",
                "Contract",
                "Representation",
                "Product"
            ],
            ["OrganizationCore"] =
            [
                "BusinessActor",
                "BusinessRole",
                "BusinessCollaboration",
                "BusinessInterface",
                "ApplicationComponent",
                "ApplicationCollaboration",
                "ApplicationInterface",
                "Node",
                "Device",
                "SystemSoftware",
                "TechnologyCollaboration",
                "TechnologyInterface",
                "Equipment",
                "Facility",
                "Location"
            ],
            ["Application"] =
            [
                "ApplicationComponent",
                "ApplicationCollaboration",
                "ApplicationInterface",
                "ApplicationFunction",
                "ApplicationInteraction",
                "ApplicationProcess",
                "ApplicationEvent",
                "ApplicationService",
                "DataObject"
            ],
            ["Technology"] =
            [
                "Node",
                "Device",
                "SystemSoftware",
                "TechnologyCollaboration",
                "TechnologyInterface",
                "Path",
                "CommunicationNetwork",
                "TechnologyFunction",
                "TechnologyProcess",
                "TechnologyInteraction",
                "TechnologyEvent",
                "TechnologyService",
                "Artifact"
            ],
            ["Physical"] =
            [
                "Equipment",
                "Facility",
                "DistributionNetwork",
                "Material"
            ],
            ["Motivation"] =
            [
                "Stakeholder",
                "Driver",
                "Assessment",
                "Goal",
                "Outcome",
                "Principle",
                "Requirement",
                "Constraint",
                "Meaning",
                "Value"
            ],
            ["Implementation"] =
            [
                "WorkPackage",
                "Deliverable",
                "ImplementationEvent",
                "Plateau",
                "Gap"
            ],
            ["CapabilityView"] =
            [
                "Capability",
                "Resource",
                "CourseOfAction",
                "ValueStream",
                "BusinessActor",
                "BusinessRole"
            ],
            ["ValueStreamView"] =
            [
                "ValueStream",
                "Capability",
                "BusinessProcess",
                "BusinessFunction",
                "BusinessService",
                "Outcome",
                "Value"
            ],
            ["ProjectStatusView"] =
            [
                "WorkPackage",
                "Deliverable",
                "ImplementationEvent",
                "Plateau",
                "Gap",
                "Goal",
                "Outcome",
                "Requirement",
                "Constraint"
            ],
            ["Junction"] = ["Junction"],
            ["Grouping"] = ["Grouping"]
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> ViewpointGroups =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Organization"] = ["OrganizationCore"],
            ["BusinessProcess"] = ["BusinessProcessCore"],
            ["ApplicationUsage"] = ["Application", "Technology"],
            ["ApplicationCooperation"] = ["Application"],
            ["ApplicationStructure"] = ["Application"],
            ["TechnologyUsage"] = ["Application", "Technology"],
            ["TechnologyCooperation"] = ["Technology"],
            ["TechnologyInfrastructure"] = ["Technology"],
            ["PhysicalEnvironment"] = ["Physical", "Technology"],
            ["Implementation"] = ["Implementation"],
            ["MigrationMap"] = ["Implementation"],
            ["Motivation"] = ["Motivation"],
            ["StrategyEnvironment"] = ["Strategy", "Motivation"],
            ["Capability"] = ["CapabilityView"],
            ["ValueStream"] = ["ValueStreamView"],
            ["ProjectStatus"] = ["ProjectStatusView"],
            ["LandscapeMap"] = ["All"],
            ["Layered"] = ["All"],
            ["Strategy"] = ["Strategy"],
            ["Business"] = ["Business"],
            ["Application"] = ["Application"],
            ["Technology"] = ["Technology"],
            ["Physical"] = ["Physical"],
            ["Migration"] = ["Implementation"]
        };

    public static bool IsElementAllowed(string viewpointKey, string semanticType)
    {
        if (string.IsNullOrWhiteSpace(viewpointKey) || string.IsNullOrWhiteSpace(semanticType))
            return false;

        if (!ViewpointGroups.TryGetValue(viewpointKey.Trim(), out var groups))
            return false;

        var semantic = semanticType.Trim();
        return groups.Any(groupName => ContainsGroup(groupName, semantic));
    }

    private static bool ContainsGroup(string groupName, string semanticType)
    {
        if (ElementGroups.TryGetValue(groupName, out var group))
            return group.Contains(semanticType, StringComparer.OrdinalIgnoreCase);

        return Archimate32ModelingCatalog.ElementTypes.Contains(groupName, StringComparer.OrdinalIgnoreCase)
            && string.Equals(groupName, semanticType, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class BpmnStencilMapper : IModelingStencilMapper
{
    private static readonly Dictionary<string, string> NodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Task"] = "bpmn2.task",
        ["UserTask"] = "bpmn2.task.user",
        ["ServiceTask"] = "bpmn2.task.service",
        ["SendTask"] = "bpmn2.task.send",
        ["ReceiveTask"] = "bpmn2.task.receive",
        ["ScriptTask"] = "bpmn2.task.script",
        ["BusinessRuleTask"] = "bpmn2.task.business-rule",
        ["ManualTask"] = "bpmn2.task.manual",
        ["SubProcess"] = "bpmn2.subprocess",
        ["EventSubProcess"] = "bpmn2.subprocess",
        ["CallActivity"] = "bpmn2.subprocess.collapsed",
        ["StartEvent"] = "bpmn2.event.start",
        ["EndEvent"] = "bpmn2.event.end",
        ["IntermediateCatchEvent"] = "bpmn2.event.intermediate",
        ["IntermediateThrowEvent"] = "bpmn2.event.intermediate",
        ["BoundaryEvent"] = "bpmn2.event.non-interrupting",
        ["ExclusiveGateway"] = "bpmn2.gateway.exclusive",
        ["ParallelGateway"] = "bpmn2.gateway.parallel",
        ["InclusiveGateway"] = "bpmn2.gateway.inclusive",
        ["ComplexGateway"] = "bpmn2.gateway.complex",
        ["EventBasedGateway"] = "bpmn2.gateway.event-based",
        ["Pool"] = "bpmn2.pool",
        ["Lane"] = "bpmn2.lane",
        ["DataObject"] = "bpmn2.data-object",
        ["DataStore"] = "bpmn2.data-store",
        ["Group"] = "general.group",
        ["TextAnnotation"] = "general.text",
        ["task"] = "bpmn2.task",
        ["userTask"] = "bpmn2.task.user",
        ["serviceTask"] = "bpmn2.task.service",
        ["sendTask"] = "bpmn2.task.send",
        ["receiveTask"] = "bpmn2.task.receive",
        ["scriptTask"] = "bpmn2.task.script",
        ["businessRuleTask"] = "bpmn2.task.business-rule",
        ["manualTask"] = "bpmn2.task.manual",
        ["subprocess"] = "bpmn2.subprocess",
        ["startEvent"] = "bpmn2.event.start",
        ["intermediateEvent"] = "bpmn2.event.intermediate",
        ["endEvent"] = "bpmn2.event.end",
        ["exclusiveGateway"] = "bpmn2.gateway.exclusive",
        ["parallelGateway"] = "bpmn2.gateway.parallel"
    };

    private static readonly Dictionary<string, string> EdgeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SequenceFlow"] = "bpmn2.flow.sequence",
        ["ConditionalFlow"] = "bpmn2.flow.conditional",
        ["DefaultFlow"] = "bpmn2.flow.default",
        ["MessageFlow"] = "bpmn2.flow.message",
        ["Association"] = "bpmn2.association",
        ["DataInputAssociation"] = "bpmn2.data-association",
        ["DataOutputAssociation"] = "bpmn2.data-association",
        ["sequenceFlow"] = "bpmn2.flow.sequence",
        ["conditionalFlow"] = "bpmn2.flow.conditional",
        ["defaultFlow"] = "bpmn2.flow.default",
        ["messageFlow"] = "bpmn2.flow.message",
        ["association"] = "bpmn2.association",
        ["dataAssociation"] = "bpmn2.data-association"
    };

    public string? GetStencilId(string notationKey, string semanticType)
        => IsBpmn(notationKey) && NodeMappings.TryGetValue(semanticType.Trim(), out var stencilId) ? stencilId : null;

    public string? GetEdgeStencilId(string notationKey, string relationshipType)
        => IsBpmn(notationKey) && EdgeMappings.TryGetValue(relationshipType.Trim(), out var stencilId) ? stencilId : null;

    private static bool IsBpmn(string notationKey)
        => string.Equals(notationKey?.Trim(), BpmnNotationProfile.Key, StringComparison.OrdinalIgnoreCase)
            || string.Equals(notationKey?.Trim(), "bpmn", StringComparison.OrdinalIgnoreCase);
}

internal sealed class UmlStencilMapper : IModelingStencilMapper
{
    private static readonly Dictionary<string, string> NodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Class"] = "uml25.class",
        ["Interface"] = "uml25.interface",
        ["AbstractClass"] = "uml25.abstract-class",
        ["Enumeration"] = "uml25.enumeration",
        ["Package"] = "uml25.package",
        ["Component"] = "uml25.component",
        ["Node"] = "uml25.deployment-node",
        ["Artifact"] = "uml25.artifact",
        ["UseCase"] = "uml25.use-case",
        ["Actor"] = "uml25.actor",
        ["State"] = "uml25.activity-action",
        ["PseudoState"] = "uml25.activity-initial",
        ["Activity"] = "uml25.activity-action",
        ["Action"] = "uml25.activity-action",
        ["ObjectNode"] = "uml25.activity-object-node",
        ["Lifeline"] = "uml25.sequence-lifeline",
        ["CombinedFragment"] = "uml25.sequence-combined-fragment",
        ["Collaboration"] = "uml25.system-boundary"
    };

    private static readonly Dictionary<string, string> EdgeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Association"] = "uml25.association",
        ["DirectedAssociation"] = "uml25.directed-association",
        ["Aggregation"] = "uml25.aggregation",
        ["Composition"] = "uml25.composition",
        ["Dependency"] = "uml25.dependency",
        ["Generalization"] = "uml25.generalization",
        ["Realization"] = "uml25.realization",
        ["Include"] = "uml25.dependency",
        ["Extend"] = "uml25.dependency",
        ["Message"] = "uml25.sequence-message"
    };

    public string? GetStencilId(string notationKey, string semanticType)
        => IsUml(notationKey) && NodeMappings.TryGetValue(semanticType.Trim(), out var stencilId) ? stencilId : null;

    public string? GetEdgeStencilId(string notationKey, string relationshipType)
        => IsUml(notationKey) && EdgeMappings.TryGetValue(relationshipType.Trim(), out var stencilId) ? stencilId : null;

    private static bool IsUml(string notationKey)
        => string.Equals(notationKey?.Trim(), UmlNotationProfile.Key, StringComparison.OrdinalIgnoreCase);
}

internal sealed class Archimate32StencilMapper : IModelingStencilMapper
{
    private static readonly Dictionary<string, string> NodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Capability"] = "archimate3.strategy.capability",
        ["CourseOfAction"] = "archimate3.strategy.course-of-action",
        ["Resource"] = "archimate3.strategy.resource",
        ["ValueStream"] = "archimate3.strategy.value-stream",
        ["BusinessActor"] = "archimate3.business.actor",
        ["BusinessRole"] = "archimate3.business.role",
        ["BusinessCollaboration"] = "archimate3.business.collaboration",
        ["BusinessInterface"] = "archimate3.business.interface",
        ["BusinessProcess"] = "archimate3.business.process",
        ["BusinessFunction"] = "archimate3.business.function",
        ["BusinessInteraction"] = "archimate3.business.interaction",
        ["BusinessEvent"] = "archimate3.business.event",
        ["BusinessService"] = "archimate3.business.service",
        ["BusinessObject"] = "archimate3.business.object",
        ["Contract"] = "archimate3.business.contract",
        ["Representation"] = "archimate3.business.representation",
        ["Product"] = "archimate3.business.product",
        ["ApplicationComponent"] = "archimate3.application.component",
        ["ApplicationCollaboration"] = "archimate3.application.collaboration",
        ["ApplicationInterface"] = "archimate3.application.interface",
        ["ApplicationFunction"] = "archimate3.application.function",
        ["ApplicationInteraction"] = "archimate3.application.interaction",
        ["ApplicationProcess"] = "archimate3.application.process",
        ["ApplicationEvent"] = "archimate3.application.event",
        ["ApplicationService"] = "archimate3.application.service",
        ["DataObject"] = "archimate3.application.data-object",
        ["Node"] = "archimate3.technology.node",
        ["Device"] = "archimate3.technology.device",
        ["SystemSoftware"] = "archimate3.technology.system-software",
        ["TechnologyCollaboration"] = "archimate3.technology.collaboration",
        ["TechnologyInterface"] = "archimate3.technology.interface",
        ["Path"] = "archimate3.technology.path",
        ["CommunicationNetwork"] = "archimate3.technology.communication-network",
        ["TechnologyFunction"] = "archimate3.technology.function",
        ["TechnologyProcess"] = "archimate3.technology.process",
        ["TechnologyInteraction"] = "archimate3.technology.interaction",
        ["TechnologyEvent"] = "archimate3.technology.event",
        ["TechnologyService"] = "archimate3.technology.service",
        ["Artifact"] = "archimate3.technology.artifact",
        ["Equipment"] = "archimate3.physical.equipment",
        ["Facility"] = "archimate3.physical.facility",
        ["DistributionNetwork"] = "archimate3.physical.distribution-network",
        ["Material"] = "archimate3.physical.material",
        ["Stakeholder"] = "archimate3.motivation.stakeholder",
        ["Driver"] = "archimate3.motivation.driver",
        ["Assessment"] = "archimate3.motivation.assessment",
        ["Goal"] = "archimate3.motivation.goal",
        ["Outcome"] = "archimate3.motivation.outcome",
        ["Principle"] = "archimate3.motivation.principle",
        ["Requirement"] = "archimate3.motivation.requirement",
        ["Constraint"] = "archimate3.motivation.constraint",
        ["Meaning"] = "archimate3.motivation.meaning",
        ["Value"] = "archimate3.motivation.value",
        ["WorkPackage"] = "archimate3.implementation.work-package",
        ["Deliverable"] = "archimate3.implementation.deliverable",
        ["ImplementationEvent"] = "archimate3.implementation.event",
        ["Plateau"] = "archimate3.implementation.plateau",
        ["Gap"] = "archimate3.implementation.gap",
        ["Junction"] = "archimate3.cross.junction",
        ["Grouping"] = "archimate3.cross.grouping",
        ["Location"] = "archimate3.cross.location"
    };

    private static readonly Dictionary<string, string> EdgeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Association"] = "archimate3.relationship.association",
        ["Triggering"] = "archimate3.relationship.triggering",
        ["Flow"] = "archimate3.relationship.flow",
        ["Access"] = "archimate3.relationship.access",
        ["Serving"] = "archimate3.relationship.serving",
        ["Realization"] = "archimate3.relationship.realization",
        ["Assignment"] = "archimate3.relationship.assignment",
        ["Aggregation"] = "archimate3.relationship.aggregation",
        ["Composition"] = "archimate3.relationship.composition",
        ["Specialization"] = "archimate3.relationship.specialization",
        ["Influence"] = "archimate3.relationship.influence"
    };

    public string? GetStencilId(string notationKey, string semanticType)
        => IsArchimate32(notationKey) && NodeMappings.TryGetValue(semanticType.Trim(), out var stencilId) ? stencilId : null;

    public string? GetEdgeStencilId(string notationKey, string relationshipType)
        => IsArchimate32(notationKey) && EdgeMappings.TryGetValue(relationshipType.Trim(), out var stencilId) ? stencilId : null;

    private static bool IsArchimate32(string notationKey)
        => string.Equals(notationKey?.Trim(), Archimate32NotationProfile.Key, StringComparison.OrdinalIgnoreCase);
}

internal sealed class BuiltInModelingStencilMapper : IModelingStencilMapper
{
    private readonly BpmnStencilMapper _bpmn = new();
    private readonly UmlStencilMapper _uml = new();
    private readonly Archimate32StencilMapper _archimate32 = new();
    private readonly ErdStencilMapper _erd = new();

    private static readonly Dictionary<string, string> NodeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["archimate:businessActor"] = "archimate3.business.actor",
        ["archimate:businessRole"] = "archimate3.business.role",
        ["archimate:businessProcess"] = "archimate3.business.process",
        ["archimate:applicationComponent"] = "archimate3.application.component",
        ["archimate:applicationService"] = "archimate3.application.service",
        ["archimate:applicationFunction"] = "archimate3.application.function",
        ["archimate:dataObject"] = "archimate3.application.data-object",
        ["archimate:technologyNode"] = "archimate3.technology.node",
        ["archimate:technologyService"] = "archimate3.technology.service"
    };

    private static readonly Dictionary<string, string> EdgeMappings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["archimate:association"] = "archimate3.relationship.association",
        ["archimate:triggering"] = "archimate3.relationship.triggering",
        ["archimate:flow"] = "archimate3.relationship.flow",
        ["archimate:access"] = "archimate3.relationship.access",
        ["archimate:serving"] = "archimate3.relationship.serving",
        ["archimate:realization"] = "archimate3.relationship.realization",
        ["archimate:assignment"] = "archimate3.relationship.assignment",
        ["archimate:aggregation"] = "archimate3.relationship.aggregation",
        ["archimate:composition"] = "archimate3.relationship.composition",
        ["archimate:specialization"] = "archimate3.relationship.specialization",
        ["archimate:influence"] = "archimate3.relationship.influence"
    };

    public string? GetStencilId(string notationKey, string semanticType)
        => _bpmn.GetStencilId(notationKey, semanticType)
            ?? _uml.GetStencilId(notationKey, semanticType)
            ?? _archimate32.GetStencilId(notationKey, semanticType)
            ?? _erd.GetStencilId(notationKey, semanticType)
            ?? GetMapping(NodeMappings, notationKey, semanticType);

    public string? GetEdgeStencilId(string notationKey, string relationshipType)
        => _bpmn.GetEdgeStencilId(notationKey, relationshipType)
            ?? _uml.GetEdgeStencilId(notationKey, relationshipType)
            ?? _archimate32.GetEdgeStencilId(notationKey, relationshipType)
            ?? _erd.GetEdgeStencilId(notationKey, relationshipType)
            ?? GetMapping(EdgeMappings, notationKey, relationshipType);

    private static string? GetMapping(IReadOnlyDictionary<string, string> mappings, string notationKey, string semanticType)
    {
        if (string.IsNullOrWhiteSpace(notationKey) || string.IsNullOrWhiteSpace(semanticType))
            return null;

        return mappings.TryGetValue($"{notationKey.Trim()}:{semanticType.Trim()}", out var stencilId)
            ? stencilId
            : null;
    }
}

internal static class BpmnModelingCatalog
{
    public static readonly IReadOnlyCollection<string> ElementTypes =
    [
        "Task",
        "UserTask",
        "ServiceTask",
        "SendTask",
        "ReceiveTask",
        "ScriptTask",
        "BusinessRuleTask",
        "ManualTask",
        "SubProcess",
        "EventSubProcess",
        "CallActivity",
        "StartEvent",
        "EndEvent",
        "IntermediateCatchEvent",
        "IntermediateThrowEvent",
        "BoundaryEvent",
        "ExclusiveGateway",
        "ParallelGateway",
        "InclusiveGateway",
        "ComplexGateway",
        "EventBasedGateway",
        "Pool",
        "Lane",
        "DataObject",
        "DataStore",
        "Group",
        "TextAnnotation"
    ];

    public static readonly IReadOnlyCollection<string> RelationshipTypes =
    [
        "SequenceFlow",
        "ConditionalFlow",
        "DefaultFlow",
        "MessageFlow",
        "Association",
        "DataInputAssociation",
        "DataOutputAssociation"
    ];
}

internal static class UmlModelingCatalog
{
    public static readonly IReadOnlyCollection<string> ElementTypes =
    [
        "Class",
        "Interface",
        "AbstractClass",
        "Enumeration",
        "Package",
        "Component",
        "Node",
        "Artifact",
        "UseCase",
        "Actor",
        "State",
        "PseudoState",
        "Activity",
        "Action",
        "ObjectNode",
        "Lifeline",
        "CombinedFragment",
        "Collaboration"
    ];

    public static readonly IReadOnlyCollection<string> RelationshipTypes =
    [
        "Association",
        "DirectedAssociation",
        "Aggregation",
        "Composition",
        "Dependency",
        "Generalization",
        "Realization",
        "Include",
        "Extend",
        "Message"
    ];
}

internal static class Archimate32ModelingCatalog
{
    public static readonly IReadOnlyCollection<string> ElementTypes =
    [
        "Capability",
        "CourseOfAction",
        "Resource",
        "ValueStream",
        "BusinessActor",
        "BusinessRole",
        "BusinessCollaboration",
        "BusinessInterface",
        "BusinessProcess",
        "BusinessFunction",
        "BusinessInteraction",
        "BusinessEvent",
        "BusinessService",
        "BusinessObject",
        "Contract",
        "Representation",
        "Product",
        "ApplicationComponent",
        "ApplicationCollaboration",
        "ApplicationInterface",
        "ApplicationFunction",
        "ApplicationInteraction",
        "ApplicationProcess",
        "ApplicationEvent",
        "ApplicationService",
        "DataObject",
        "Node",
        "Device",
        "SystemSoftware",
        "TechnologyCollaboration",
        "TechnologyInterface",
        "Path",
        "CommunicationNetwork",
        "TechnologyFunction",
        "TechnologyProcess",
        "TechnologyInteraction",
        "TechnologyEvent",
        "TechnologyService",
        "Artifact",
        "Equipment",
        "Facility",
        "DistributionNetwork",
        "Material",
        "Stakeholder",
        "Driver",
        "Assessment",
        "Goal",
        "Outcome",
        "Principle",
        "Requirement",
        "Constraint",
        "Meaning",
        "Value",
        "WorkPackage",
        "Deliverable",
        "ImplementationEvent",
        "Plateau",
        "Gap",
        "Junction",
        "Grouping",
        "Location"
    ];
}
