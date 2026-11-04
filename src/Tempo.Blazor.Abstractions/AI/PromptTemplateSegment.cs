namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One run of an assembled prompt: literal text, or a substituted variable. The live preview renders
/// <see cref="Text"/> and marks the variable runs, so what the reader previews and what "Use" sends are the same
/// string, produced by the same pass.
/// </summary>
public sealed record PromptTemplateSegment
{
    /// <summary>Creates a literal segment. Use <see cref="Variable"/> for a variable run.</summary>
    /// <param name="text">The literal text. Null is stored as an empty string.</param>
    public PromptTemplateSegment(string text)
    {
        Text = text ?? string.Empty;
    }

    /// <summary>The rendered text of this run: literal text, the substituted value, or the literal placeholder
    /// <c>{{name}}</c> when the variable has no value.</summary>
    public string Text { get; init; }

    /// <summary>The variable this run stands for, or null for literal text (including an escaped
    /// <c>{{</c>).</summary>
    public string? VariableName { get; init; }

    /// <summary>Whether this run is a variable (a placeholder the template declares).</summary>
    public bool IsVariable => VariableName is not null;

    /// <summary>
    /// Whether the variable run has NO value — the run then renders the literal placeholder. An optional variable
    /// left empty is reported the same way as a required one: the preview shows the hole, and only the required
    /// case is also a validation error.
    /// </summary>
    public bool IsEmpty { get; init; }

    /// <summary>Creates a variable segment.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="text">The substituted value, or the literal placeholder when there is no value.</param>
    /// <param name="isEmpty">Whether the variable has no value.</param>
    /// <returns>The segment.</returns>
    public static PromptTemplateSegment Variable(string name, string text, bool isEmpty)
        => new(text) { VariableName = name, IsEmpty = isEmpty };
}
