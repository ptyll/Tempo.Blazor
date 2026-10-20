namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>One attribute of a scanned tag; value positions are indices into the scanned markup.</summary>
/// <param name="NameStart">Inclusive index of the first character of the attribute name.</param>
/// <param name="NameEnd">Exclusive index of the end of the attribute name.</param>
/// <param name="ValueStart">Inclusive index of the value, or <c>-1</c> when the attribute has no value.</param>
/// <param name="ValueEnd">Exclusive index of the end of the value (the closing quote is at this index).</param>
/// <param name="Quote">The quote character of a quoted value, or <c>'\0'</c> for an unquoted value.</param>
internal readonly record struct AttrToken(int NameStart, int NameEnd, int ValueStart, int ValueEnd, char Quote)
{
    /// <summary>Gets whether the attribute has a value.</summary>
    public bool HasValue => ValueStart >= 0;

    /// <summary>Gets the inclusive end of the whole attribute (its closing quote, last value char or name char).</summary>
    public int InclusiveEnd => !HasValue ? NameEnd - 1 : Quote == '\0' ? ValueEnd - 1 : ValueEnd;
}

/// <summary>
/// The result of one scan: the complete start tags plus, when the document ended inside a comment, a
/// declaration or a tag, the index of that unterminated construct (a browser drops it and everything
/// after it, so the caller discards that tail — fail-closed).
/// </summary>
/// <param name="Tokens">The complete start tags, in document order.</param>
/// <param name="TruncatedAt">Index of the unterminated construct, or <c>-1</c> when scanning reached EOF cleanly.</param>
internal readonly record struct TagScanResult(IReadOnlyList<TagToken> Tokens, int TruncatedAt);

/// <summary>One start tag found by <see cref="HtmlTagTokenizer"/>; positions are indices into the markup.</summary>
/// <param name="Start">Inclusive index of the <c>&lt;</c>.</param>
/// <param name="End">Inclusive index of the closing <c>&gt;</c>.</param>
/// <param name="Name">Lower-case tag name.</param>
/// <param name="Attrs">The attributes of the tag, in source order.</param>
/// <param name="ContentStart">Inclusive start of the raw-text content, or <c>-1</c>.</param>
/// <param name="ContentEnd">Exclusive end of the raw-text content, or <c>-1</c>.</param>
/// <param name="SelfClosing">Whether the tag ended with <c>/&gt;</c>.</param>
internal readonly record struct TagToken(
    int Start,
    int End,
    string Name,
    IReadOnlyList<AttrToken> Attrs,
    int ContentStart = -1,
    int ContentEnd = -1,
    bool SelfClosing = false)
{
    /// <summary>Gets whether the element's content is raw text (CSS, script, …) and must not be scanned.</summary>
    public bool HasRawTextContent => ContentStart >= 0;
}

/// <summary>
/// A single-pass, linear HTML tag scanner (§3.7a). It replaces a backtracking regular expression that
/// was quadratic on adversarial input (100 KB of <c>&lt;a</c> took minutes) and it follows the WHATWG
/// tokenizer states closely enough that a mismatched quote cannot hide an attribute:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>a comment, doctype, processing instruction or closing tag is skipped only up to its first
/// <c>&gt;</c>; everything after it is scanned again, so an abruptly closed comment
/// (<c>&lt;!--&gt;</c>, <c>&lt;!---&gt;</c>, <c>--!&gt;</c>) cannot hide a live tag;</item>
/// <item>raw-text content (<c>style</c>, <c>script</c>, <c>textarea</c>, <c>title</c>) is *not* skipped:
/// a browser does not switch to raw text inside SVG/MathML content, so <c>&lt;svg&gt;&lt;style&gt;</c> is
/// a classic mutation-XSS vector. The content of a <c>style</c> element is additionally reported as a
/// range so the CSS guard can check it;</item>
/// <item>inside a tag the states are tag name → before attribute name (whitespace and <c>/</c>) →
/// attribute name (everything except whitespace, <c>/</c>, <c>&gt;</c> and <c>=</c>; a quote or
/// <c>&lt;</c> is part of the name) → after name → before value → quoted value, single-quoted value or
/// unquoted value (which ends at whitespace or <c>&gt;</c>; quotes are part of it);</item>
/// <item>an unterminated tag (end of input before <c>&gt;</c>) ends the scan: the browser drops such a
/// tag, so the remaining text is not markup and is discarded by the caller (fail-closed);</item>
/// </list>
/// </remarks>
internal static class HtmlTagTokenizer
{
    /// <summary>Scans every start tag of <paramref name="markup"/> in one pass.</summary>
    public static TagScanResult Scan(string markup)
    {
        var tokens = new List<TagToken>();
        if (string.IsNullOrEmpty(markup))
        {
            return new TagScanResult(tokens, -1);
        }

        var index = 0;
        var openStyles = new List<int>();
        while (index < markup.Length)
        {
            var lt = markup.IndexOf('<', index);
            if (lt < 0)
            {
                break;
            }

            index = lt;
            var next = index + 1 < markup.Length ? markup[index + 1] : '\0';
            if (next is '!' or '?' or '/')
            {
                // A comment ends at (or after) the first '>' — WHATWG also closes it abruptly at
                // "<!-->" and "<!--->" and at "--!>" — so skipping to the first '>' never hides a tag
                // the browser would run, and everything after it is scanned again.
                var declarationEnd = markup.IndexOf('>', index + 2);
                if (declarationEnd < 0)
                {
                    return new TagScanResult(tokens, index);
                }

                if (next == '/' && openStyles.Count > 0 && IsClosingTag(markup, index, "style"))
                {
                    // Pair the <style> start tag with its content so the CSS guard sees exactly it.
                    var styleIndex = openStyles[^1];
                    openStyles.RemoveAt(openStyles.Count - 1);
                    var styleTag = tokens[styleIndex];
                    tokens[styleIndex] = styleTag with
                    {
                        ContentStart = styleTag.End + 1,
                        ContentEnd = index,
                    };
                }

                index = declarationEnd + 1;
                continue;
            }

            if (!char.IsAsciiLetter(next))
            {
                index++;
                continue;
            }

            if (!TryReadTag(markup, index, out var token, out var resume))
            {
                // EOF inside a tag: the browser drops it and everything after it.
                return new TagScanResult(tokens, index);
            }

            if (token.Name == "style" && !token.SelfClosing)
            {
                openStyles.Add(tokens.Count);
            }

            tokens.Add(token);
            index = resume;
        }

        // A <style> that is never closed owns the rest of the document (a browser treats it as CSS).
        foreach (var styleIndex in openStyles)
        {
            var styleTag = tokens[styleIndex];
            tokens[styleIndex] = styleTag with { ContentStart = styleTag.End + 1, ContentEnd = markup.Length };
        }

        return new TagScanResult(tokens, -1);
    }

    /// <summary>Answers whether the closing tag at <paramref name="index"/> closes <paramref name="name"/>.</summary>
    private static bool IsClosingTag(string markup, int index, string name)
    {
        var nameStart = index + 2;
        while (nameStart < markup.Length && char.IsWhiteSpace(markup[nameStart]))
        {
            nameStart++;
        }

        return nameStart + name.Length <= markup.Length &&
            string.Compare(markup, nameStart, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    private static bool TryReadTag(string markup, int start, out TagToken token, out int resume)
    {
        token = default;
        resume = start;

        var index = start + 1;
        var nameStart = index;
        while (index < markup.Length && IsTagNameChar(markup[index]))
        {
            index++;
        }

        var name = markup[nameStart..index].ToLowerInvariant();
        List<AttrToken>? attrs = null;
        var selfClosing = false;

        while (true)
        {
            while (index < markup.Length && char.IsWhiteSpace(markup[index]))
            {
                index++;
            }

            if (index >= markup.Length)
            {
                return false;
            }

            if (markup[index] == '>')
            {
                index++;
                break;
            }

            if (markup[index] == '/')
            {
                selfClosing = true;
                index++;
                continue;
            }

            var attrNameStart = index;
            while (index < markup.Length &&
                   !char.IsWhiteSpace(markup[index]) &&
                   markup[index] is not ('/' or '>' or '='))
            {
                index++;
            }

            if (index == attrNameStart)
            {
                // A stray character (for example '=' at the start of an attribute) — skip it.
                index++;
                continue;
            }

            var attrNameEnd = index;
            while (index < markup.Length && char.IsWhiteSpace(markup[index]))
            {
                index++;
            }

            if (index >= markup.Length)
            {
                return false;
            }

            if (markup[index] != '=')
            {
                (attrs ??= []).Add(new AttrToken(attrNameStart, attrNameEnd, -1, -1, '\0'));
                continue;
            }

            index++;
            while (index < markup.Length && char.IsWhiteSpace(markup[index]))
            {
                index++;
            }

            if (index >= markup.Length)
            {
                return false;
            }

            if (markup[index] is '"' or '\'')
            {
                var quote = markup[index];
                var valueStart = index + 1;
                var close = markup.IndexOf(quote, valueStart);
                if (close < 0)
                {
                    // Unterminated quoted value: the browser consumes the rest as the value.
                    return false;
                }

                (attrs ??= []).Add(new AttrToken(attrNameStart, attrNameEnd, valueStart, close, quote));
                index = close + 1;
                continue;
            }

            var unquotedStart = index;
            while (index < markup.Length && !char.IsWhiteSpace(markup[index]) && markup[index] != '>')
            {
                index++;
            }

            (attrs ??= []).Add(new AttrToken(attrNameStart, attrNameEnd, unquotedStart, index, '\0'));
        }

        var end = index - 1;
        token = new TagToken(start, end, name, attrs ?? (IReadOnlyList<AttrToken>)Array.Empty<AttrToken>());

        if (selfClosing)
        {
            token = token with { SelfClosing = true };
        }

        resume = index;
        return true;
    }

    /// <summary>Finds the start index of <c>&lt;/name</c> at or after <paramref name="from"/> (linear).</summary>
    public static int FindClosingTag(string markup, string name, int from)
    {
        for (var i = from; i + name.Length + 1 < markup.Length; i++)
        {
            if (markup[i] != '<' || markup[i + 1] != '/')
            {
                continue;
            }

            var nameStart = i + 2;
            while (nameStart < markup.Length && char.IsWhiteSpace(markup[nameStart]))
            {
                nameStart++;
            }

            if (nameStart + name.Length <= markup.Length &&
                string.Compare(markup, nameStart, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                var after = nameStart + name.Length;
                if (after >= markup.Length ||
                    char.IsWhiteSpace(markup[after]) ||
                    markup[after] is '>' or '/')
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static bool IsPrefix(string markup, int index, string prefix)
        => markup.Length - index >= prefix.Length &&
            string.CompareOrdinal(markup, index, prefix, 0, prefix.Length) == 0;

    private static bool IsTagNameChar(char c)
        => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or ':' or '.';
}
