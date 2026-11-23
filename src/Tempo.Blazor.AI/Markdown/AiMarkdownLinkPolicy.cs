namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// How <see cref="AiMarkdownRenderer"/> treats a hyperlink in untrusted model markdown.
/// Default <see cref="Allow"/> keeps today's hardened <c>&lt;a&gt;</c> (http/https/mailto/relative,
/// <c>target="_blank"</c>, <c>rel="noopener noreferrer nofollow"</c>). Citation chips are not links
/// and are unaffected.
/// </summary>
public enum AiMarkdownLinkPolicy
{
    /// <summary>Render a hardened <c>&lt;a&gt;</c> (the default).</summary>
    Allow = 0,

    /// <summary>Render the escaped link text with no <c>&lt;a&gt;</c>.</summary>
    TextOnly = 1,
}
