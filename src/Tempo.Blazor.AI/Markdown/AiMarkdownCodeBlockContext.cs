namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// One code block handed to <see cref="AiMarkdownRenderOptions.CodeBlockRenderer"/> — the
/// <c>CodeBlockTemplate</c> seam of <c>TmMarkdownView</c>, which lets a host replace the built-in
/// <c>&lt;pre&gt;&lt;code&gt;</c> plus copy affordance with its own (e.g. syntax-highlighted) markup.
/// </summary>
public sealed class AiMarkdownCodeBlockContext
{
    /// <summary>Zero-based index of the code block in the document.</summary>
    public int Index { get; init; }

    /// <summary>The raw code, decoded and <em>not</em> HTML-escaped; the renderer must escape it.</summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>The fence info string (<c>csharp</c>, <c>json</c>, …), or <see langword="null"/> for an indented block.</summary>
    public string? Language { get; init; }
}
