using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>Displays details for a selected modeling element or relationship.</summary>
public partial class TmModelingInspector
{
    /// <summary>Selected modeling element. Takes precedence when both element and relationship are supplied.</summary>
    [Parameter] public ModelingElementDto? Element { get; set; }

    /// <summary>Selected modeling relationship.</summary>
    [Parameter] public ModelingRelationshipDto? Relationship { get; set; }

    /// <summary>Elements used to resolve relationship source and target display names.</summary>
    [Parameter] public IReadOnlyList<ModelingElementDto> Elements { get; set; } = [];

    /// <summary>Additional CSS class applied to the inspector root.</summary>
    [Parameter] public string? Class { get; set; }

    private string RootClass => string.Join(" ", new[] { "tm-modeling-inspector", Class }.Where(value => !string.IsNullOrWhiteSpace(value)));

    private string Kind => Element is not null ? "element" : Relationship is not null ? "relationship" : "empty";

    private string KindLabel => Element is not null
        ? Loc["TmModelingInspector_Element"]
        : Relationship is not null
            ? Loc["TmModelingInspector_Relationship"]
            : Loc["TmModelingInspector_Title"];

    /// <summary>Header icon: the element's semantic-type icon, a link for relationships, the
    /// fallback shape otherwise (shared with the model tree so the same type always maps to
    /// the same icon).</summary>
    private string HeaderIcon => Element is not null
        ? ModelingSemanticTypeIcons.ForSemanticType(Element.SemanticType)
        : Relationship is not null
            ? IconNames.Link2
            : ModelingSemanticTypeIcons.Fallback;

    private string DisplayName => Element is not null
        ? Fallback(Element.Name, Loc["TmModelingModelTree_Unnamed"])
        : Relationship is not null
            ? Fallback(Relationship.Name, Relationship.Id)
            : string.Empty;

    private string DisplayType => Element is not null
        ? Fallback(Element.SemanticType, Loc["TmModelingModelTree_UnknownType"])
        : Relationship is not null
            ? Fallback(Relationship.RelationshipType, Loc["TmModelingModelTree_UnknownType"])
            : string.Empty;

    private string DisplayNotation => Element is not null
        ? Fallback(Element.Notation, Loc["TmModelingEditor_Unknown"])
        : Relationship is not null
            ? Fallback(SourceElement?.Notation, TargetElement?.Notation, Loc["TmModelingEditor_Unknown"])
            : string.Empty;

    private string DisplaySourceId => Element is not null
        ? Element.SourceId
        : Relationship?.SourceId ?? string.Empty;

    private string DisplaySourceType => Element is not null
        ? Element.SourceType
        : Relationship?.SourceType ?? string.Empty;

    private string DisplaySourcePath => Element?.SourcePath ?? string.Empty;

    private ModelingElementDto? SourceElement => Relationship is null
        ? null
        : Elements.FirstOrDefault(element => string.Equals(element.Id, Relationship.SourceElementId, StringComparison.Ordinal));

    private ModelingElementDto? TargetElement => Relationship is null
        ? null
        : Elements.FirstOrDefault(element => string.Equals(element.Id, Relationship.TargetElementId, StringComparison.Ordinal));

    private string DisplaySourceName => Fallback(SourceElement?.Name, Relationship?.SourceElementId, Loc["TmModelingEditor_Unknown"]);

    private string DisplayTargetName => Fallback(TargetElement?.Name, Relationship?.TargetElementId, Loc["TmModelingEditor_Unknown"]);

    private static string Fallback(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FormatPropertyValue(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.ToString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };

    private static string GetTrustClass(string trustLevel)
        => string.Equals(trustLevel, "low", StringComparison.OrdinalIgnoreCase)
            ? "tm-modeling-inspector__value tm-modeling-inspector__value--trust-low"
            : "tm-modeling-inspector__value";
}
