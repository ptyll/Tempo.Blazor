namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Marker for a value produced by the <c>html.trusted</c> filter: the author explicitly asked for
/// markup, so it is written without encoding — but it is always passed through the
/// <see cref="Rendering.HtmlContentSanitizer"/> first, so the tag/attribute allow-list still applies.
/// </summary>
internal sealed class TrustedHtml(string value)
{
    /// <summary>Gets the raw, not yet sanitized markup.</summary>
    public string Value { get; } = value;

    /// <inheritdoc />
    public override string ToString() => Value;
}
