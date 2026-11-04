namespace Tempo.Blazor.AI.Models;

/// <summary>
/// How a <see cref="PromptVariable"/> is filled in: one line of text, several lines, or one value out of the
/// list the template author declared in <see cref="PromptVariable.Options"/>.
/// <para>
/// The kind selects the INPUT the prompt library renders — a text box, a text area or a select — and nothing
/// else: the parser substitutes the value as text in every kind. It never evaluates a value, so a host that
/// wants a computed default resolves it before handing the template over.
/// </para>
/// </summary>
public enum PromptVariableKind
{
    /// <summary>A single line of text. The default.</summary>
    Text = 0,

    /// <summary>Several lines of text — a brief, an example, a body of source material.</summary>
    Multiline = 1,

    /// <summary>One value out of <see cref="PromptVariable.Options"/>; a value outside the list is reported as a
    /// validation error rather than substituted silently.</summary>
    Select = 2,
}
