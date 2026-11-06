using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.Modeling;

/// <summary>Describes a modeling notation profile and the semantic keys it supports.</summary>
public interface IModelingNotationProfile
{
    /// <summary>Unique notation key, for example bpmn or archimate.</summary>
    string NotationKey { get; }

    /// <summary>Human-readable display name.</summary>
    string DisplayName { get; }

    /// <summary>Semantic element types supported by this notation profile.</summary>
    IReadOnlyCollection<string> SupportedElementTypes { get; }

    /// <summary>Semantic relationship types supported by this notation profile.</summary>
    IReadOnlyCollection<string> SupportedRelationshipTypes { get; }

    /// <summary>Viewpoint keys supported by this notation profile.</summary>
    IReadOnlyCollection<string> SupportedViewpointKeys { get; }

    /// <summary>
    /// Semantic element types that CONTAIN other elements (BPMN <c>Pool</c>/<c>Lane</c>/
    /// <c>SubProcess</c>/<c>EventSubProcess</c>). The editor derives the "insert into a container",
    /// the drop target's <c>ParentId</c> and the container candidates of its dialogs from THIS
    /// profile capability (phase 3 review, A6) — never from a hard-coded list in a component, so a
    /// custom notation declares its own containers. Empty (the default) = no containment, which is
    /// the pre-3.0 behavior for every profile that does not override it.
    /// </summary>
    IReadOnlyCollection<string> ContainerElementTypes => [];

    /// <summary>Whether missing node stencil mappings should skip elements instead of using a fallback stencil.</summary>
    bool EnforcesStrictStencilMapping => false;
}

/// <summary>Looks up modeling notation profiles by notation key.</summary>
public interface IModelingNotationProfileProvider
{
    /// <summary>Returns the matching profile, or null when the notation is unknown.</summary>
    /// <param name="notationKey">Notation key to look up.</param>
    IModelingNotationProfile? GetProfile(string notationKey);
}

/// <summary>Validates semantic relationships for a notation.</summary>
public interface IModelingRelationshipRulesProvider
{
    /// <summary>Returns whether the relationship is valid for the supplied notation and element types.</summary>
    bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType);

    /// <summary>Validates a concrete relationship with access to source-backed model element context.</summary>
    ModelingRelationshipRuleResult ValidateRelationship(ModelingRelationshipRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

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
}

/// <summary>Notation-specific relationship rules advertised by notation key.</summary>
public interface IModelingNotationRelationshipRulesProvider : IModelingRelationshipRulesProvider
{
    /// <summary>Notation keys handled by this rules provider.</summary>
    IReadOnlyCollection<string> NotationKeys { get; }
}

/// <summary>Context supplied to notation-specific relationship validation rules.</summary>
public sealed class ModelingRelationshipRuleContext
{
    /// <summary>Notation key used for this relationship validation.</summary>
    public required string NotationKey { get; init; }

    /// <summary>Optional notation-specific viewpoint key used for this validation.</summary>
    public string ViewpointKey { get; init; } = string.Empty;

    /// <summary>Relationship being validated.</summary>
    public required ModelingRelationshipDto Relationship { get; init; }

    /// <summary>Source model element.</summary>
    public required ModelingElementDto SourceElement { get; init; }

    /// <summary>Target model element.</summary>
    public required ModelingElementDto TargetElement { get; init; }

    /// <summary>All model elements keyed by element id.</summary>
    public required IReadOnlyDictionary<string, ModelingElementDto> ElementsById { get; init; }
}

/// <summary>Result of notation-specific relationship rule validation.</summary>
public sealed class ModelingRelationshipRuleResult
{
    private ModelingRelationshipRuleResult(bool isValid, string message, string suggestedFix)
    {
        IsValid = isValid;
        Message = message;
        SuggestedFix = suggestedFix;
    }

    /// <summary>Reusable valid result.</summary>
    public static ModelingRelationshipRuleResult Valid { get; } = new(true, string.Empty, string.Empty);

    /// <summary>Whether the relationship is valid.</summary>
    public bool IsValid { get; }

    /// <summary>User-facing issue message when invalid.</summary>
    public string Message { get; }

    /// <summary>Suggested fix when invalid.</summary>
    public string SuggestedFix { get; }

    /// <summary>Creates an invalid validation result.</summary>
    public static ModelingRelationshipRuleResult Invalid(string message, string suggestedFix)
        => new(false, message ?? string.Empty, suggestedFix ?? string.Empty);
}

/// <summary>Validates whether a semantic element type is allowed in a viewpoint.</summary>
public interface IModelingViewpointRulesProvider
{
    /// <summary>Returns whether the element type is allowed in the supplied notation viewpoint.</summary>
    bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType);

    /// <summary>Validates a concrete element against a viewpoint and returns optional advisory issues.</summary>
    ModelingViewpointRuleResult ValidateElementViewpoint(ModelingViewpointRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.ViewpointKey)
            || IsElementAllowedInViewpoint(context.NotationKey, context.ViewpointKey, context.Element.SemanticType))
        {
            return ModelingViewpointRuleResult.Allowed;
        }

        return ModelingViewpointRuleResult.Disallowed(
            $"Element type '{context.Element.SemanticType}' is not allowed in viewpoint '{context.ViewpointKey}'.",
            "Switch to a viewpoint that includes this element type, or remove the element from the selected view.");
    }
}

/// <summary>Notation-specific viewpoint rules advertised by notation key.</summary>
public interface IModelingNotationViewpointRulesProvider : IModelingViewpointRulesProvider
{
    /// <summary>Notation keys handled by this viewpoint rules provider.</summary>
    IReadOnlyCollection<string> NotationKeys { get; }
}

/// <summary>Context supplied to viewpoint validation rules.</summary>
public sealed class ModelingViewpointRuleContext
{
    /// <summary>Notation key used for this element validation.</summary>
    public required string NotationKey { get; init; }

    /// <summary>Selected viewpoint key.</summary>
    public string ViewpointKey { get; init; } = string.Empty;

    /// <summary>Element being validated.</summary>
    public required ModelingElementDto Element { get; init; }
}

/// <summary>Result of notation-specific viewpoint validation.</summary>
public sealed class ModelingViewpointRuleResult
{
    private ModelingViewpointRuleResult(bool isAllowed, bool hasIssue, ModelingIssueSeverity severity, string message, string suggestedFix)
    {
        IsAllowed = isAllowed;
        HasIssue = hasIssue;
        Severity = severity;
        Message = message;
        SuggestedFix = suggestedFix;
    }

    /// <summary>Reusable allowed result with no issue.</summary>
    public static ModelingViewpointRuleResult Allowed { get; } = new(true, false, ModelingIssueSeverity.Info, string.Empty, string.Empty);

    /// <summary>Whether the element can be rendered in the viewpoint.</summary>
    public bool IsAllowed { get; }

    /// <summary>Whether an issue should be surfaced to the user.</summary>
    public bool HasIssue { get; }

    /// <summary>Issue severity when <see cref="HasIssue"/> is true.</summary>
    public ModelingIssueSeverity Severity { get; }

    /// <summary>User-facing issue message.</summary>
    public string Message { get; }

    /// <summary>Suggested fix for the issue.</summary>
    public string SuggestedFix { get; }

    /// <summary>Creates an advisory warning while still allowing the element.</summary>
    public static ModelingViewpointRuleResult Warning(string message, string suggestedFix)
        => new(true, true, ModelingIssueSeverity.Warning, message ?? string.Empty, suggestedFix ?? string.Empty);

    /// <summary>Creates a disallowed result that should skip the element.</summary>
    public static ModelingViewpointRuleResult Disallowed(string message, string suggestedFix)
        => new(false, true, ModelingIssueSeverity.Warning, message ?? string.Empty, suggestedFix ?? string.Empty);
}

/// <summary>Maps semantic modeling types to diagram stencil identifiers.</summary>
public interface IModelingStencilMapper
{
    /// <summary>Returns the node stencil id for a semantic element type, or null when no mapping exists.</summary>
    string? GetStencilId(string notationKey, string semanticType);

    /// <summary>Returns the edge stencil id for a semantic relationship type, or null when no mapping exists.</summary>
    string? GetEdgeStencilId(string notationKey, string relationshipType);
}

/// <summary>DI-backed registry of modeling notation profiles.</summary>
public sealed class ModelingNotationProfileRegistry : IModelingNotationProfileProvider
{
    private readonly Dictionary<string, IModelingNotationProfile> _profiles;

    /// <summary>Creates a registry from profiles registered in DI.</summary>
    /// <param name="profiles">Profiles discovered from dependency injection.</param>
    /// <param name="logger">Optional logger used for duplicate and invalid profile warnings.</param>
    public ModelingNotationProfileRegistry(
        IEnumerable<IModelingNotationProfile>? profiles,
        ILogger<ModelingNotationProfileRegistry>? logger = null)
    {
        _profiles = new Dictionary<string, IModelingNotationProfile>(StringComparer.OrdinalIgnoreCase);

        foreach (var profile in profiles ?? [])
        {
            RegisterProfile(profile, logger);
        }
    }

    /// <inheritdoc />
    public IModelingNotationProfile? GetProfile(string notationKey)
    {
        if (string.IsNullOrWhiteSpace(notationKey))
            return null;

        return _profiles.TryGetValue(notationKey.Trim(), out var profile)
            ? profile
            : null;
    }

    /// <summary>Returns all registered profiles in display-name order.</summary>
    public IReadOnlyCollection<IModelingNotationProfile> GetAll()
        => _profiles.Values
            .OrderBy(profile => profile.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.NotationKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>Total number of registered notation profiles.</summary>
    public int Count => _profiles.Count;

    private void RegisterProfile(IModelingNotationProfile profile, ILogger<ModelingNotationProfileRegistry>? logger)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var notationKey = profile.NotationKey?.Trim();
        if (string.IsNullOrWhiteSpace(notationKey))
        {
            logger?.LogWarning("Skipping modeling notation profile with an empty notation key.");
            return;
        }

        if (_profiles.ContainsKey(notationKey))
        {
            logger?.LogWarning(
                "Duplicate modeling notation profile key '{NotationKey}' was ignored. The first registered profile is used.",
                notationKey);
            return;
        }

        _profiles[notationKey] = profile;
    }
}

/// <summary>Default relationship rules provider backed by notation profile capabilities.</summary>
public sealed class ModelingRelationshipRulesProvider : IModelingRelationshipRulesProvider
{
    private readonly IModelingNotationProfileProvider _profiles;

    /// <summary>Creates a relationship rules provider.</summary>
    /// <param name="profiles">Notation profile lookup service.</param>
    public ModelingRelationshipRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    /// <inheritdoc />
    public bool IsValidRelationship(string notationKey, string sourceType, string targetType, string relationshipType)
    {
        if (string.IsNullOrWhiteSpace(notationKey)
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
            && Contains(profile.SupportedRelationshipTypes, relationshipType);
    }

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;
}

/// <summary>Default viewpoint rules provider backed by notation profile capabilities.</summary>
public sealed class ModelingViewpointRulesProvider : IModelingViewpointRulesProvider
{
    private readonly IModelingNotationProfileProvider _profiles;

    /// <summary>Creates a viewpoint rules provider.</summary>
    /// <param name="profiles">Notation profile lookup service.</param>
    public ModelingViewpointRulesProvider(IModelingNotationProfileProvider profiles)
    {
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
    }

    /// <inheritdoc />
    public bool IsElementAllowedInViewpoint(string notationKey, string viewpointKey, string semanticType)
    {
        if (string.IsNullOrWhiteSpace(notationKey)
            || string.IsNullOrWhiteSpace(viewpointKey)
            || string.IsNullOrWhiteSpace(semanticType))
        {
            return false;
        }

        var profile = _profiles.GetProfile(notationKey);
        return profile is not null
            && Contains(profile.SupportedViewpointKeys, viewpointKey)
            && Contains(profile.SupportedElementTypes, semanticType);
    }

    private static bool Contains(IEnumerable<string>? values, string value)
        => values?.Any(candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase)) == true;
}
