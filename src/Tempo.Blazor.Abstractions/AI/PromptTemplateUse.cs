using System.Collections.ObjectModel;

namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What the prompt library hands its host when the reader presses Use: the template that was picked, the prompt
/// assembled from the values, and those effective values (the reader's input plus every default that stood in for
/// an empty field). The host stores or sends it — the component never sends anything itself.
/// </summary>
public sealed record PromptTemplateUse
{
    /// <summary>
    /// The value map of a payload that carries none. It is ONE shared instance, so it must be read-only: a host that
    /// cast the property back to a mutable dictionary would otherwise add a value that every later payload sees.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NoValues =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>The <see cref="PromptTemplate.Id"/> of the template that produced the prompt.</summary>
    public string TemplateId { get; init; } = string.Empty;

    /// <summary>The <see cref="PromptTemplate.Title"/>, so a host can label what it received without a lookup.
    /// </summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>The assembled prompt — the text the reader previewed, with every variable substituted.</summary>
    public string Prompt { get; init; } = string.Empty;

    /// <summary>The effective values by variable name: the reader's input, plus the default used for an empty
    /// field. A variable with neither is absent (and made the assembly invalid, so it never reaches a host).</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = NoValues;
}
