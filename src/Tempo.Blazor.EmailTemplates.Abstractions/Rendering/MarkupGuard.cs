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
    /// The elements a message never needs and a browser would run (Fáze 10 review, U1): SVG animation elements
    /// (they can set an <c>href</c> at runtime, so a value that passed the static check can still become
    /// <c>javascript:</c>), embedded documents and plugins (<c>iframe</c>, <c>frame</c>, <c>frameset</c>,
    /// <c>object</c>, <c>embed</c>, <c>applet</c>, <c>portal</c>), <c>script</c> (removed together with its
    /// content), <c>base</c> (it rewrites the resolution of EVERY relative URL in the message) and <c>form</c>
    /// (it can POST the reader's input to the sender). No "raw block may keep it" opt-in exists: raw HTML is an
    /// author's opt-in to MARKUP, not to running code.
    /// </summary>
    private static readonly string[] UnsafeElements =
    [
        "set", "animate", "animatemotion", "animatetransform", "discard",
        "script", "iframe", "frame", "frameset", "object", "embed", "applet", "base", "form", "portal",
    ];

    /// <summary>Replaces/removes refused URLs, handlers and unsafe elements in the MJML.</summary>
    public static MarkupGuardResult ApplyToMjml(string mjml) => Apply(mjml, null);

    /// <summary>Replaces/removes refused URLs, handlers and unsafe elements in the HTML.</summary>
    /// <param name="html">The compiled markup to guard.</param>
    /// <param name="permittedImports">
    /// The <c>@import</c> targets the PIPELINE itself wrote (the <c>mj-font</c> hrefs the compiler turned into an
    /// <c>@import</c> next to the <c>&lt;link&gt;</c>). Only those exact targets are exempt from the
    /// <c>@import</c> rule of <see cref="CssSafety"/>: every other one is author-controlled and is refused. A
    /// caller that guards arbitrary markup passes <see langword="null"/> and gets the strict rule.
    /// </param>
    public static MarkupGuardResult ApplyToHtml(string html, IReadOnlyCollection<string>? permittedImports = null)
        => Apply(html, permittedImports);

    /// <summary>
    /// Maps a guard failure (a regular expression that had to be aborted) to the fail-closed
    /// <see cref="RenderErrorSeverity.Error"/> diagnostic the renderer reports instead of markup it could
    /// not check.
    /// </summary>
    public static RenderError FailureToError(Exception exception)
        => new(
            $"The rendered markup could not be checked safely ({exception.GetType().Name}); it was discarded.",
            Key: RenderKeys.MarkupTooComplex);

    private static MarkupGuardResult Apply(string markup, IReadOnlyCollection<string>? permittedImports)
    {
        if (string.IsNullOrEmpty(markup))
        {
            return new MarkupGuardResult(markup ?? string.Empty, [], [], []);
        }

        var state = new State();
        var scan = HtmlTagTokenizer.Scan(markup);
        var edits = new List<Edit>();
        var skipUntil = -1;

        // The closing-tag lookup is cached per element NAME (Fáze 10 security re-review, U1): an unsafe element
        // without a closing tag used to make the scan rescan the rest of the document once per element, so
        // N unclosed "<iframe>"s cost O(N²) (320 KB took ~59 s). Tags arrive in document order, so a cached
        // position is reusable whenever it is not before the current element; a cached "not found" (-1) stays
        // valid for every later element, because later searches only cover a smaller tail of the document.
        var closeCache = new Dictionary<string, int>(StringComparer.Ordinal);

        int FindClose(string name, int from)
        {
            if (closeCache.TryGetValue(name, out var cached) && (cached < 0 || cached >= from))
            {
                return cached;
            }

            cached = HtmlTagTokenizer.FindClosingTag(markup, name, from);
            closeCache[name] = cached;
            return cached;
        }

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
                if (!tag.SelfClosing || IsRawTextElement(tag.Name) || OwnsContentEvenWhenSelfClosed(tag.Name))
                {
                    // The element is removed WITH its content: leaving a script body behind as text would only
                    // rename the payload. A raw-text element (<c>script</c>) that is never closed owns the rest
                    // of the document exactly as a browser reads it, so that whole tail goes too.
                    var closeStart = FindClose(tag.Name, tag.End + 1);
                    if (closeStart >= 0)
                    {
                        var closeEnd = markup.IndexOf('>', closeStart);
                        if (closeEnd >= 0)
                        {
                            end = closeEnd;
                        }
                    }
                    else if (IsRawTextElement(tag.Name))
                    {
                        end = markup.Length - 1;
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
                var sanitized = SanitizeStyleSheet(content, state, permittedImports);
                if (tag.ContentEnd > tag.ContentStart &&
                    !string.Equals(sanitized, content, StringComparison.Ordinal))
                {
                    edits.Add(Edit.Replace(tag.ContentStart, tag.ContentEnd - 1, sanitized));
                }
            }

            SanitizeAttributes(markup, tag, state, edits, permittedImports);
        }

        if (scan.TruncatedAt >= 0)
        {
            // An unterminated tag/comment swallows the rest of the document, exactly as a browser drops it.
            edits.Add(Edit.Remove(scan.TruncatedAt, markup.Length - 1));
        }

        return new MarkupGuardResult(
            ApplyEdits(markup, edits), state.BlockedSchemes, state.RemovedEventHandlers, state.RemovedElements);
    }

    private static void SanitizeAttributes(
        string markup,
        TagToken tag,
        State state,
        List<Edit> edits,
        IReadOnlyCollection<string>? permittedImports)
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
                var sanitized = SanitizeDeclarations(value, state, permittedImports);
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
    private static bool IsUrlAttribute(string name) => IsUrlAttributeName(name);

    /// <summary>
    /// The same answer for the emission side (<see cref="MjmlKnownAttributes"/> / <see cref="MjmlAttributeBuffer"/>):
    /// a URL-bearing attribute has to pass the scheme allow-list wherever it is written, not only when it is
    /// guarded.
    /// </summary>
    public static bool IsUrlAttributeName(string name)
        => UrlAttributeNames.Contains(name, StringComparer.OrdinalIgnoreCase) ||
            name.EndsWith(":href", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(":src", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the element owns its content as raw text (its closing tag is the only markup in it).</summary>
    private static bool IsRawTextElement(string name)
        => name is "script" or "style" or "textarea" or "title";

    /// <summary>
    /// Answers whether the element owns its content even when the author wrote the tag as self-closing (Fáze 10
    /// security re-review, U2): HTML has no self-closing syntax for a non-void element, so <c>&lt;iframe/&gt;</c>
    /// (or <c>&lt;iframe/src=…&gt;</c>, which the tokenizer also reads as self-closing) is an ordinary start tag
    /// and everything up to its closing tag is its content. Dropping only the tag would leave the payload behind
    /// as text, or leave an orphaned <c>&lt;/iframe&gt;</c>.
    /// </summary>
    private static bool OwnsContentEvenWhenSelfClosed(string name)
        => name is "iframe" or "object" or "form" or "frameset" or "applet";

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
    /// A stylesheet that still embeds a refused URL or <c>@import</c> after normalisation, or that carries a
    /// construct a browser would execute or fetch (<see cref="CssSafety"/>), is dropped entirely — a partially
    /// rewritten rule could leave a value the browser still resolves.
    /// </summary>
    private static string SanitizeStyleSheet(string css, State state, IReadOnlyCollection<string>? permittedImports)
    {
        if (HasBlockedCssReference(css, state) || !CssSafety.IsSafe(css, permittedImports))
        {
            state.RemoveElement("style");
            return string.Empty;
        }

        return css;
    }

    /// <summary>
    /// Style attribute: only the declarations that embed a refused URL or that <see cref="CssSafety"/> refuses
    /// are dropped (the rest of the attribute is the author's CSS and stays).
    /// </summary>
    private static string SanitizeDeclarations(
        string declarations,
        State state,
        IReadOnlyCollection<string>? permittedImports)
    {
        var blockedReference = HasBlockedCssReference(declarations, state);
        if (!blockedReference && CssSafety.IsSafe(declarations, permittedImports))
        {
            return declarations;
        }

        state.RemoveElement("style");

        var kept = new List<string>();
        foreach (var declaration in declarations.Split(';'))
        {
            if (!HasBlockedCssReference(declaration, state) &&
                CssSafety.IsSafe(declaration, permittedImports))
            {
                kept.Add(declaration);
            }
        }

        return kept.Count == 0 ? string.Empty : string.Join(';', kept);
    }

    /// <summary>
    /// Answers whether a stylesheet embeds a refused URL — the same rule the guard applies to a <c>style</c>
    /// element, exposed for the EMISSION side: a <c>mj-style</c> payload is written verbatim, so it must not be
    /// able to fetch or execute anything either (<see cref="CssSafety"/>).
    /// </summary>
    public static bool HasBlockedCssReference(string css) => HasBlockedCssReference(css, new State());

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
