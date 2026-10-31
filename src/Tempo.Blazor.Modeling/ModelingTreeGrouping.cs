namespace Tempo.Blazor.Modeling;

/// <summary>
/// How <c>TmModelingModelTree</c> structures its elements (Fáze 12). The default
/// <see cref="Auto"/> renders the ParentId hierarchy when at least one element resolves to a
/// valid parent and keeps today's SemanticType groups for flat models, so a model without
/// <c>ParentId</c> looks and behaves exactly as before.
/// </summary>
public enum ModelingTreeGrouping
{
    /// <summary>Flat groups keyed by <see cref="ModelingElementDto.SemanticType"/> (the pre-phase-12 view).</summary>
    SemanticType,

    /// <summary>Nested APG hierarchy built from <see cref="ModelingElementDto.ParentId"/>.</summary>
    Hierarchy,

    /// <summary><see cref="Hierarchy"/> when at least one element has a valid <c>ParentId</c>,
    /// otherwise <see cref="SemanticType"/> (the default).</summary>
    Auto
}
