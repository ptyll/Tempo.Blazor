namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// A variable referenced by a template, with its dotted path and inferred kind, plus optional
/// host-supplied metadata used by the editor's variable picker.
/// </summary>
/// <param name="Path">The dotted variable path (e.g. <c>user.address.city</c>).</param>
/// <param name="Kind">Whether the variable is iterated as a collection.</param>
public sealed record TemplateVariableInfo(string Path, VariableKind Kind)
{
    /// <summary>
    /// Gets the human-readable description shown next to the token in the variable picker
    /// (e.g. "First name of the user"). Supplied by the HOST: only the host knows what its data means.
    /// Variables extracted from a document leave it <see langword="null"/>, and the picker then shows the
    /// path alone. Participates in record equality (B9).
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the optional group heading the picker sorts and labels rows under (e.g. "User", "Project").
    /// Host-supplied; extracted variables leave it <see langword="null"/> and are listed ungrouped.
    /// Participates in record equality (B9).
    /// </summary>
    public string? Group { get; init; }
}
