namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What a section of the context window holds. The kind selects the section's TONE (its own
/// <c>--tm-context-usage-*</c> knob on the component root) and nothing else — the label stays the host's word
/// for the section, so a host is free to call the <see cref="Files"/> section "Attachments".
/// <para>
/// <see cref="Other"/> is the default, so a section that names no kind still paints a stable, distinct tone
/// instead of falling through to a missing one.
/// </para>
/// </summary>
public enum ContextUsageKind
{
    /// <summary>A section the host did not classify (the default).</summary>
    Other = 0,

    /// <summary>The system prompt and the standing instructions of the conversation.</summary>
    System = 1,

    /// <summary>The tool / function definitions offered to the model.</summary>
    Tools = 2,

    /// <summary>Long-term memory, retrieved facts or a project knowledge base.</summary>
    Memory = 3,

    /// <summary>Attached files, documents or images.</summary>
    Files = 4,

    /// <summary>The conversation so far (the reader's turns and the model's answers).</summary>
    Chat = 5,

    /// <summary>Tool results and other returned payloads.</summary>
    Results = 6,
}
