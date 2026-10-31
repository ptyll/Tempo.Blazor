namespace Tempo.Blazor.AI.Models;

/// <summary>
/// One mention a composer offers after the reader types <c>@</c> — a person, a document, a saved prompt,
/// anything the host's <c>IAiMentionProvider</c> can search. Picking it inserts <c>@{Label}</c> into the
/// draft and records the item on the next <see cref="AiComposerSubmit"/>.
/// </summary>
public sealed class AiMentionItem
{
    private string _id = string.Empty;
    private string _label = string.Empty;

    /// <summary>Stable identifier of the mentioned entity; the host resolves it on submit.</summary>
    public string Id
    {
        get => _id;
        set => _id = value ?? string.Empty;
    }

    /// <summary>Display label inserted after the <c>@</c> and shown in the suggestion list. Required.</summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? string.Empty;
    }

    /// <summary>Optional kind of the mentioned entity (<c>"user"</c>, <c>"document"</c>, …) shown as a hint.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional icon name (an <c>IconNames</c> constant or a host-registered icon).</summary>
    public string? Icon { get; set; }

    /// <summary>Optional second line rendered under the label in the suggestion list.</summary>
    public string? Description { get; set; }
}
