namespace Tempo.Blazor.AI.Models;

/// <summary>
/// What a composer sends: the prompt text, the attachments the reader picked, the selected model, the
/// mentions the draft references and the slash command it started from. The record is a plain container so
/// a backend can consume it without Blazor — the composer raises it through its
/// <c>OnSubmit</c> callback and clears the draft afterwards.
/// </summary>
public sealed class AiComposerSubmit
{
    private string _text = string.Empty;

    /// <summary>The prompt text, trimmed of leading and trailing whitespace.</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>Attached files, in the order the reader added them. Never null.</summary>
    public IReadOnlyList<AiComposerAttachment> Attachments { get; set; } = Array.Empty<AiComposerAttachment>();

    /// <summary>Identifier of the model the reader selected, or <see langword="null"/> when no picker was offered.</summary>
    public string? Model { get; set; }

    /// <summary>Mentions the draft references, in insertion order. Never null.</summary>
    public IReadOnlyList<AiMentionItem> Mentions { get; set; } = Array.Empty<AiMentionItem>();

    /// <summary>
    ///The slash command the draft was started from, or <see langword="null"/> for a free-form prompt. The
    /// command stays recorded until the reader picks another one or the draft is sent.
    /// </summary>
    public AiSlashCommand? Command { get; set; }
}
