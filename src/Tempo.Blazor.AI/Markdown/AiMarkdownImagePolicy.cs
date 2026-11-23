namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// How <see cref="AiMarkdownRenderer"/> treats an image in untrusted model markdown.
/// Default <see cref="Allow"/> keeps today's behaviour (http/https images load; everything else
/// degrades to the alt text).
/// </summary>
public enum AiMarkdownImagePolicy
{
    /// <summary>
    /// Load a safe http/https image (the default). An unsafe source still degrades to its alt text.
    /// </summary>
    Allow = 0,

    /// <summary>Drop the image entirely: no <c>&lt;img&gt;</c> and no alt text.</summary>
    Block = 1,

    /// <summary>Render the escaped alt text instead of an <c>&lt;img&gt;</c>, so nothing is fetched.</summary>
    AltText = 2,
}
