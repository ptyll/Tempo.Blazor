using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Encodes every template code span of a rendered markup fragment as *text*, so an unsubstituted render
/// (the HTML export: <see cref="EmailPreviewRequest.SubstituteVariables"/> = <see langword="false"/>) can
/// keep its <c>{{ … }}</c> tokens without letting a token carry live markup.
/// </summary>
/// <remarks>
/// <para>
/// A substituted render is safe because the template engine HTML-encodes every expression result. With
/// substitution switched off there is no expression result and therefore no escaping point — the raw token
/// text reaches the compiled markup. A document whose text block reads
/// <c>{{ "&lt;script&gt;alert(1)&lt;/script&gt;" }}</c> would then emit a real <c>&lt;script&gt;</c>
/// element into the exported HTML, i.e. an export could execute in a browser or an email client's
/// renderer.
/// </para>
/// <para>
/// The fix is the documented contract of the switch: the tokens stay visible, the *markup inside them*
/// does not. Every span is passed through <see cref="HtmlTextEscaper.Encode"/>, so the exported HTML shows
/// <c>{{ &quot;&lt;script&gt;…&lt;/script&gt;&quot; }}</c> and the browser parses it as text. This is the
/// inverse of <see cref="TemplateContentMasker"/>: the masker protects the span from being encoded by the
/// sanitizer, the encoder is the one place where a span must be encoded on purpose.
/// </para>
/// </remarks>
internal static class TemplateSpanEncoder
{
    /// <summary>HTML-encodes the <c>{{ … }}</c> / <c>{% … %}</c> spans of <paramref name="markup"/>, keeping the rest.</summary>
    internal static string EncodeSpans(string markup)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return markup ?? string.Empty;
        }

        // Cheap guard: most documents have no token in a text block at all.
        if (!markup.Contains("{{", StringComparison.Ordinal) && !markup.Contains("{%", StringComparison.Ordinal))
        {
            return markup;
        }

        // The head (`mj-title`, `mj-preview`, `mj-attributes`, fonts, …) is plain text/attributes the
        // generator has ALREADY escaped (MjmlEscape.Text/Attribute). Encoding a span there again would turn
        // `{{ &quot;…&quot; }}` into `{{ &amp;quot;…&amp;quot; }}`, i.e. the export would show `&quot;` as
        // text instead of the token's own characters. Only the body carries parsed (unescaped) markup.
        var headStart = markup.IndexOf("<mj-head", StringComparison.OrdinalIgnoreCase);
        if (headStart < 0)
        {
            return EncodeBody(markup);
        }

        var headEnd = markup.IndexOf("</mj-head>", headStart, StringComparison.OrdinalIgnoreCase);
        if (headEnd < 0)
        {
            return EncodeBody(markup);
        }

        headEnd += "</mj-head>".Length;
        return string.Concat(
            markup[..headEnd],
            EncodeBody(markup[headEnd..]));
    }

    /// <summary>Encodes the spans of a markup fragment the generator did not pre-escape.</summary>
    private static string EncodeBody(string markup)
        => markup.Length == 0
            ? markup
            : TemplateSpanPattern.CodeSpan().Replace(markup, match => HtmlTextEscaper.Encode(match.Value));
}
