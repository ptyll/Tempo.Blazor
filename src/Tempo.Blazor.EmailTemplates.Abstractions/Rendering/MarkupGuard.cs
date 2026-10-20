using System.Text;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Markup guard of the render pipeline (§3.7a, D15), applied after substitution in two places: to the
/// substituted MJML before it is compiled, and to the compiled HTML. It enforces the URL scheme
/// allow-list (<see cref="UrlSchemePolicy"/>), removes event-handler attributes, and drops markup that
/// can rewrite a link at runtime (SVG animation elements, meta refresh).
/// </summary>
/// <remarks>
/// <para>
/// The scan is a linear tokenizer (<see cref="HtmlTagTokenizer"/>), not a regular expression: escaping
/// alone cannot neutralise an <c>on*</c> attribute (a browser entity-decodes its value before running it
/// as script), and a backtracking tag pattern was quadratic on adversarial input. An unterminated tag at
/// the end of input stops the scan and the rest of the document is discarded, exactly as a browser drops
/// such a tag.
/// </para>
/// <para>
/// The MJML pass is what keeps a refused value away from the compiler entirely — the compiler copies
/// <c>background-url</c> into a CSS <c>url(…)</c>, a <c>background</c> attribute and a VML
/// <c>&lt;v:fill src&gt;</c> that ends up inside an Outlook conditional comment. The HTML pass is the
/// second line of defence covering what the compiler, a raw block or a host-set HTML attribute can still
/// emit. Both passes rewrite in place instead of re-serializing the document, so an ordinary render keeps
/// the compiler's exact byte output.
/// </para>
/// </remarks>
internal static class MarkupGuard
{
    /// <summary>Attributes whose whole value is a single URL.</summary>
    private static readonly string[] UrlAttributeNames =
    [
        "href",
        "src",
        "background",
        "background-url",
        "poster",
        "base-url",
        "thumbnails-src",
        "left-icon",
        "right-icon",
        "icon-wrapped-url",
        "icon-unwrapped-url",
        "action",
        "formaction",
        "data",
        "lowsrc",
        "dynsrc",
    ];

    /// <summary>
    /// SVG animation elements: they can set an <c>href</c> at runtime, so a value that passed the static
    /// check can still become <c>javascript:</c> when the animation runs. E-mail has no use for them.
    /// </summary>
    private static readonly string[] UnsafeElements =
        ["set", "animate", "animatemotion", "animatetransform", "discard"];

    /// <summary>Replaces/removes refused URLs, handlers and unsafe elements in the MJML.</summary>
    public static MarkupGuardResult ApplyToMjml(string mjml) => Apply(mjml);

    /// <summary>Replaces/removes refused URLs, handlers and unsafe elements in the HTML.</summary>
    public static MarkupGuardResult ApplyToHtml(string html) => Apply(html);

    /// <summary>
    /// Maps a guard failure (a regular expression that had to be aborted) to the fail-closed
    /// <see cref="RenderErrorSeverity.Error"/> diagnostic the renderer reports instead of markup it could
    /// not check.
    /// </summary>
    public static RenderError FailureToError(Exception exception)
        => new(
            $"The rendered markup could not be checked safely ({exception.GetType().Name}); it was discarded.",
            Key: RenderKeys.MarkupTooComplex);

    private static MarkupGuardResult Apply(string markup)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return new MarkupGuardResult(markup ?? string.Empty, [], [], []);
        }

        var state = new State();
        var scan = HtmlTagTokenizer.Scan(markup);
        var edits = new List<Edit>();
        var skipUntil = -1;

        foreach (var tag in scan.Tokens)
        {
            if (tag.Start < skipUntil)
            {
                // Inside a removed element.
                continue;
            }

            if (Array.IndexOf(UnsafeElements, tag.Name) >= 0)
            {
                state.RemoveElement(tag.Name);
                var end = tag.End;
                if (!tag.SelfClosing && !tag.HasRawTextContent)
                {
                    var closeStart = HtmlTagTokenizer.FindClosingTag(markup, tag.Name, tag.End + 1);
                    if (closeStart >= 0)
                    {
                        var closeEnd = markup.IndexOf('>', closeStart);
                        if (closeEnd >= 0)
                        {
                            end = closeEnd;
                        }
                    }
                }

                edits.Add(Edit.Remove(tag.Start, end));
                skipUntil = end + 1;
                continue;
            }

            if (tag.Name == "meta" && IsMetaRefresh(markup, tag))
            {
                state.RemoveElement("meta-refresh");
                edits.Add(Edit.Remove(tag.Start, tag.End));
                continue;
            }

            if (tag.HasRawTextContent && tag.Name == "style")
            {
                var content = markup[tag.ContentStart..tag.ContentEnd];
                var sanitized = SanitizeStyleSheet(content, state);
                if (tag.ContentEnd > tag.ContentStart &&
                    !string.Equals(sanitized, content, StringComparison.Ordinal))
                {
                    edits.Add(Edit.Replace(tag.ContentStart, tag.ContentEnd - 1, sanitized));
                }
            }

            SanitizeAttributes(markup, tag, state, edits);
        }

        if (scan.TruncatedAt >= 0)
        {
            // An unterminated tag/comment swallows the rest of the document, exactly as a browser drops it.
            edits.Add(Edit.Remove(scan.TruncatedAt, markup.Length - 1));
        }

        return new MarkupGuardResult(
            ApplyEdits(markup, edits), state.BlockedSchemes, state.RemovedEventHandlers, state.RemovedElements);
    }

    private static void SanitizeAttributes(string markup, TagToken tag, State state, List<Edit> edits)
    {
        foreach (var attr in tag.Attrs)
        {
            var name = markup[attr.NameStart..attr.NameEnd];
            var value = attr.HasValue ? markup[attr.ValueStart..attr.ValueEnd] : null;

            if (IsEventHandler(name))
            {
                state.RemoveHandler(name);
                var removalStart = attr.NameStart;
                while (removalStart > tag.Start && char.IsWhiteSpace(markup[removalStart - 1]))
                {
                    removalStart--;
                }

                edits.Add(Edit.Remove(removalStart, attr.InclusiveEnd));
                continue;
            }

            if (value is null)
            {
                continue;
            }

            if (IsUrlAttribute(name))
            {
                var scheme = UrlSchemePolicy.FindBlockedScheme(value);
                if (scheme is not null)
                {
                    edits.Add(Edit.Replace(attr.NameStart, attr.InclusiveEnd, Rewrite(name, state.Block(scheme))));
                }

                continue;
            }

            if (name.Equals("srcset", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var candidate in value.Split(','))
                {
                    var scheme = UrlSchemePolicy.FindBlockedScheme(FirstToken(candidate));
                    if (scheme is not null)
                    {
                        edits.Add(Edit.Replace(attr.NameStart, attr.InclusiveEnd, Rewrite(name, state.Block(scheme))));
                        break;
                    }
                }

                continue;
            }

            if (name.Equals("ping", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var target in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    var scheme = UrlSchemePolicy.FindBlockedScheme(target);
                    if (scheme is not null)
                    {
                        edits.Add(Edit.Replace(attr.NameStart, attr.InclusiveEnd, Rewrite(name, state.Block(scheme))));
                        break;
                    }
                }

                continue;
            }

            if (name.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                var sanitized = SanitizeDeclarations(value, state);
                if (!string.Equals(sanitized, value, StringComparison.Ordinal))
                {
                    edits.Add(Edit.Replace(attr.NameStart, attr.InclusiveEnd, Rewrite(name, sanitized)));
                }
            }
        }
    }

    /// <summary>
    /// Answers whether the attribute holds a URL. Namespaced forms count too: <c>xlink:href</c>
    /// (SVG) is a link the same way <c>href</c> is.
    /// </summary>
    private static bool IsUrlAttribute(string name)
        => UrlAttributeNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
            name.EndsWith(":href", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(":src", StringComparison.OrdinalIgnoreCase);

    /// <summary>An event handler is any attribute whose name starts with <c>on</c>, plus <c>srcdoc</c>.</summary>
    private static bool IsEventHandler(string name)
        => name.Equals("srcdoc", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("on", StringComparison.OrdinalIgnoreCase);

    private static bool IsMetaRefresh(string markup, TagToken tag)
    {
        foreach (var attr in tag.Attrs)
        {
            var name = markup[attr.NameStart..attr.NameEnd];
            if (!name.Equals("http-equiv", StringComparison.OrdinalIgnoreCase) || !attr.HasValue)
            {
                continue;
            }

            if (markup[attr.ValueStart..attr.ValueEnd].Trim().Equals("refresh", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A stylesheet that still embeds a refused URL or <c>@import</c> after normalisation is dropped
    /// entirely — a partially rewritten rule could leave a value the browser still resolves.
    /// </summary>
    private static string SanitizeStyleSheet(string css, State state)
        => HasBlockedCssReference(css, state) ? string.Empty : css;

    /// <summary>Style attribute: only the declarations that embed a refused URL are dropped.</summary>
    private static string SanitizeDeclarations(string declarations, State state)
    {
        if (!HasBlockedCssReference(declarations, state))
        {
            return declarations;
        }

        var kept = new List<string>();
        foreach (var declaration in declarations.Split(';'))
        {
            if (!HasBlockedCssReference(declaration, state))
            {
                kept.Add(declaration);
            }
        }

        return kept.Count == 0 ? string.Empty : string.Join(';', kept);
    }

    /// <summary>
    /// Answers whether a CSS fragment embeds a refused URL: <c>url(…)</c> and <c>image-set(…)</c> targets
    /// and an <c>@import</c> target (either <c>url(…)</c> or a bare string). The fragment is normalised
    /// first, so comments and CSS escapes cannot hide the scheme.
    /// </summary>
    private static bool HasBlockedCssReference(string css, State state)
    {
        var normalized = CssNormalizer.Normalize(css);
        var blocked = false;

        foreach (var target in EnumerateCssFunctionTargets(normalized, "url("))
        {
            blocked |= BlockIfRefused(target, state);
        }

        foreach (var target in EnumerateCssFunctionTargets(normalized, "image-set("))
        {
            foreach (var candidate in target.Split(','))
            {
                blocked |= BlockIfRefused(FirstToken(candidate).Trim('"', '\''), state);
            }
        }

        blocked |= BlockImportTargets(normalized, state);
        return blocked;
    }

    private static bool BlockImportTargets(string css, State state)
    {
        var blocked = false;
        var index = 0;
        while (true)
        {
            var at = css.IndexOf("@import", index, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                return blocked;
            }

            var after = at + "@import".Length;
            var end = css.IndexOfAny([';', '}'], after);
            var statement = end < 0 ? css[after..] : css[after..end];
            index = end < 0 ? css.Length : end + 1;

            var target = ExtractImportTarget(statement);
            if (target is not null)
            {
                blocked |= BlockIfRefused(target, state);
            }
        }
    }

    private static string? ExtractImportTarget(string statement)
    {
        var trimmed = statement.TrimStart();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed[0] is '"' or '\'')
        {
            var quote = trimmed[0];
            var close = trimmed.IndexOf(quote, 1);
            return close > 1 ? trimmed[1..close] : null;
        }

        if (trimmed.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var close = trimmed.IndexOf(')');
            if (close > 4)
            {
                return trimmed[4..close].Trim().Trim('"', '\'');
            }
        }

        var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 ? trimmed[..space] : trimmed;
    }

    private static IEnumerable<string> EnumerateCssFunctionTargets(string css, string function)
    {
        var searchFrom = 0;
        while (true)
        {
            var start = css.IndexOf(function, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                yield break;
            }

            var contentStart = start + function.Length;
            var depth = 1;
            var index = contentStart;
            while (index < css.Length && depth > 0)
            {
                if (css[index] == '(')
                {
                    depth++;
                }
                else if (css[index] == ')')
                {
                    depth--;
                }

                index++;
            }

            if (depth != 0)
            {
                yield break;
            }

            var close = index - 1;
            var inner = css[contentStart..close].Trim();
            if (inner.Length > 1 && (inner[0] is '"' or '\'') && inner[^1] == inner[0])
            {
                inner = inner[1..^1];
            }

            yield return inner;
            searchFrom = close + 1;
        }
    }

    private static bool BlockIfRefused(string target, State state)
    {
        var scheme = UrlSchemePolicy.FindBlockedScheme(target);
        if (scheme is null)
        {
            return false;
        }

        state.Block(scheme);
        return true;
    }

    private static string FirstToken(string value)
    {
        var trimmed = value.Trim();
        for (var i = 0; i < trimmed.Length; i++)
        {
            if (char.IsWhiteSpace(trimmed[i]))
            {
                return trimmed[..i];
            }
        }

        return trimmed;
    }

    /// <summary>Re-emits an attribute with a double-quoted, escape-safe value.</summary>
    private static string Rewrite(string name, string value)
        => string.Concat(name, "=\"", value.Replace("\"", "&quot;", StringComparison.Ordinal), "\"");

    private static string ApplyEdits(string markup, List<Edit> edits)
    {
        if (edits.Count == 0)
        {
            return markup;
        }

        var builder = new StringBuilder(markup.Length);
        var position = 0;
        foreach (var edit in edits)
        {
            if (edit.Start < position || edit.End < edit.Start)
            {
                // Defensive: overlapping edits cannot be applied twice.
                continue;
            }

            builder.Append(markup, position, edit.Start - position);
            if (edit.Text.Length > 0)
            {
                builder.Append(edit.Text);
            }

            position = edit.End + 1;
        }

        builder.Append(markup, position, markup.Length - position);
        return builder.ToString();
    }

    /// <summary>One in-place rewrite: <c>[Start, End]</c> inclusive, replaced by <paramref name="Text"/>.</summary>
    private readonly record struct Edit(int Start, int End, string Text)
    {
        public static Edit Remove(int start, int end) => new(start, end, string.Empty);

        public static Edit Replace(int start, int end, string text) => new(start, end, text);
    }

    /// <summary>Collects what was refused while one pass runs.</summary>
    private sealed class State
    {
        public List<string> BlockedSchemes { get; } = [];

        public List<string> RemovedEventHandlers { get; } = [];

        public List<string> RemovedElements { get; } = [];

        public string Block(string scheme)
        {
            if (!BlockedSchemes.Contains(scheme, StringComparer.Ordinal))
            {
                BlockedSchemes.Add(scheme);
            }

            return UrlSchemePolicy.BlockedUrlPlaceholder;
        }

        public void RemoveHandler(string name)
        {
            if (!RemovedEventHandlers.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                RemovedEventHandlers.Add(name.ToLowerInvariant());
            }
        }

        public void RemoveElement(string name)
        {
            if (!RemovedElements.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                RemovedElements.Add(name.ToLowerInvariant());
            }
        }
    }
}
