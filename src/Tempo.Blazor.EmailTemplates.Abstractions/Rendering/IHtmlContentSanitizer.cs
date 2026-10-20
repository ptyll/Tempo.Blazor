namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Sanitizes the inline HTML a text-bearing block may carry (D12/D22). Two implementations ship:
/// <see cref="ConservativeHtmlContentSanitizer"/> in this package (pure BCL, the default when the
/// Engine package is not registered — the WASM editor) and <c>AngleSharpHtmlContentSanitizer</c> in
/// <c>Tempo.Blazor.EmailTemplates.Engine</c> (authoritative parser, registered by
/// <c>AddTempoEmailTemplateEngine</c>). Both read <see cref="HtmlSanitizerPolicy"/> and may only differ
/// towards a stricter result.
/// </summary>
public interface IHtmlContentSanitizer
{
    /// <summary>
    /// Returns a sanitized copy of <paramref name="html"/>, safe to embed in generated markup. The
    /// result is also safe inside a quoted attribute: <c>&amp;</c>, <c>&lt;</c>, <c>&gt;</c>, <c>"</c>
    /// and <c>'</c> are encoded.
    /// </summary>
    string Sanitize(string html);

    /// <summary>
    /// Sanitizes authored content that may carry Scriban code. Each <c>{{ … }}</c> / <c>{% … %}</c>
    /// span is masked before the sanitizer runs and restored verbatim afterwards, so entity-encoding the
    /// span cannot destroy the template. Content that already contains a masking placeholder — or a
    /// construct whose masking would be guesswork — falls back to <see cref="Sanitize"/> (fail closed).
    /// </summary>
    string SanitizeTemplateContent(string content);
}
