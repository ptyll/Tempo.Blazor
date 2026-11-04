namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One reusable prompt: the body (a text with <c>{{variable}}</c> placeholders), the metadata that lets a reader
/// pick it out of a library, and — optionally — the explicit declaration of its variables.
/// <para>
/// The record is the host's data. It carries no culture, no provider and no evaluation: the body is assembled by
/// <see cref="PromptTemplateParser"/> through plain text substitution, and every value the reader types is text
/// too. A template is picked by <see cref="Id"/>, so the id has to be stable and unique inside the library.
/// </para>
/// <para>
/// <see cref="Variables"/> is the declaration that WINS. When it is null or empty the variables are derived from
/// the body's placeholders (name as label, required, single line); when it is set, only the declared variables
/// exist and a placeholder it does not name stays literal text — see
/// <see cref="PromptTemplateParser.ResolveVariables(PromptTemplate)"/>.
/// </para>
/// </summary>
public sealed record PromptTemplate
{
    /// <summary>Creates a template without a body; assign <see cref="Body"/> through an object initialiser.</summary>
    /// <param name="id">Stable identity inside the library, used for selection and for the <c>@key</c> of the
    /// list row. Null or whitespace generates one.</param>
    /// <param name="title">The title a reader picks the template by. Null is stored as an empty string.</param>
    public PromptTemplate(string id, string title)
        : this(id, title, null)
    {
    }

    /// <summary>Creates a template with its body.</summary>
    /// <param name="id">Stable identity inside the library. Null or whitespace generates one.</param>
    /// <param name="title">The title a reader picks the template by.</param>
    /// <param name="body">The prompt body, with <c>{{variable}}</c> placeholders. Null is stored as an empty
    /// string.</param>
    public PromptTemplate(string id, string title, string? body)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("n") : id!;
        Title = title ?? string.Empty;
        Body = body ?? string.Empty;
    }

    /// <summary>Stable identity inside the library. Never empty: a missing id is generated at construction, so
    /// a template always has a key and a host always has something to persist.</summary>
    public string Id { get; init; }

    /// <summary>The title a reader picks the template by. Rendered as text.</summary>
    public string Title { get; init; }

    /// <summary>
    /// The category the library groups the template under — the host's own vocabulary, rendered verbatim
    /// (the component never translates a host's category and never invents one). Null (or whitespace) puts the
    /// template in the library's "Uncategorised" group, which only renders the name when the library has at
    /// least one such template.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>What the template is for, rendered under its title in the list and in the detail. Null renders no
    /// description.</summary>
    public string? Description { get; init; }

    /// <summary>The prompt body: text with <c>{{variable}}</c> placeholders. <c>{{{{</c> and <c>}}}}</c> are the
    /// escaped braces, so a body can show a literal placeholder.</summary>
    public string Body { get; init; }

    /// <summary>
    /// The explicit variable declaration, or null/empty to derive one from the body. When set it is
    /// authoritative: the label, description, default, required flag, kind and options of a declared variable are
    /// what the reader sees, and a placeholder the declaration does not name is literal text.
    /// </summary>
    public IReadOnlyList<PromptVariable>? Variables { get; init; }
}
