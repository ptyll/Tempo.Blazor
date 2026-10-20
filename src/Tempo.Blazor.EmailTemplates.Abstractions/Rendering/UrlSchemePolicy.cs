using System.Text;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The one URL scheme policy of the email pipeline (§3.7a, D15). A host cannot extend it: the
/// allow-list is <c>http</c>, <c>https</c>, <c>mailto</c> and <c>tel</c>; a value without a scheme
/// (relative path, <c>./</c>, <c>../</c>, <c>#fragment</c>) passes. Everything else
/// (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>, <c>file:</c>, <c>blob:</c>, unknown) is
/// refused and replaced with <c>#</c>, and so is a network path / UNC reference
/// (<c>//host/path</c>, <c>\\host\share</c>): "relative" means no authority, so a mail client can never
/// be pointed at a remote SMB share.
/// </summary>
/// <remarks>
/// The scheme is extracted the way a browser extracts it before deciding what to run: HTML character
/// references are decoded first (numeric forms with any number of digits and an optional semicolon,
/// plus the named <c>amp</c>/<c>colon</c>/<c>tab</c>/<c>newline</c> entities — <c>&amp;#58</c> and
/// <c>&amp;colon;</c> would otherwise hide the colon), ASCII tab, CR and LF characters are removed
/// wherever they appear (browsers do the same, so <c>java\tscript:</c> is still <c>javascript:</c>) and
/// leading/trailing control characters and spaces are trimmed. A value whose prefix is not a
/// syntactically valid scheme (<c>path/to:thing</c>, <c>java script:x</c>) is a relative URL — browsers
/// do not treat it as a scheme either.
/// </remarks>
internal static class UrlSchemePolicy
{
    /// <summary>Schemes a rendered message may link to.</summary>
    public static readonly IReadOnlyList<string> AllowedSchemes = ["http", "https", "mailto", "tel"];

    /// <summary>Pseudo-scheme reported for a protocol-relative or UNC reference.</summary>
    public const string NetworkPathScheme = "network-path";

    /// <summary>The value a refused URL is replaced with (fail-closed but still a valid link).</summary>
    public const string BlockedUrlPlaceholder = "#";

    private static readonly char[] QueryOrFragmentSeparators = ['/', '?', '#'];

    /// <summary>Longest scheme name kept in an audit detail, so a crafted value cannot bloat it.</summary>
    private const int MaxSchemeLength = 32;

    /// <summary>Unicode replacement character used for an out-of-range character reference.</summary>
    private const int ReplacementCharacter = 0xFFFD;

    /// <summary>
    /// Returns the offending scheme in lower case when <paramref name="url"/> must not be used, or
    /// <see langword="null"/> when it is allowed (including empty and relative URLs).
    /// </summary>
    public static string? FindBlockedScheme(string? url)
    {
        if (string.IsNullOrEmpty(url))
        {
            return null;
        }

        var stripped = StripControlCharacters(url);
        if (stripped.Length == 0)
        {
            return null;
        }

        // A protocol-relative or UNC reference resolves to a remote authority, not to the mail's origin.
        var slashes = stripped.Replace('\\', '/');
        if (slashes.StartsWith("//", StringComparison.Ordinal))
        {
            return NetworkPathScheme;
        }

        var colon = slashes.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
        {
            return null;
        }

        // A separator before the colon means the colon belongs to a path/query/fragment, not a scheme.
        var separator = slashes.IndexOfAny(QueryOrFragmentSeparators);
        if (separator >= 0 && separator < colon)
        {
            return null;
        }

        var scheme = slashes[..colon];
        if (!IsSchemeSyntax(scheme))
        {
            // Not a scheme (e.g. "mail to:x", "a b:c"): browsers treat the value as a relative URL.
            return null;
        }

        foreach (var allowed in AllowedSchemes)
        {
            if (string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        return scheme.Length > MaxSchemeLength ? scheme[..MaxSchemeLength].ToLowerInvariant() : scheme.ToLowerInvariant();
    }

    /// <summary>Answers whether <paramref name="url"/> may be emitted into the message.</summary>
    public static bool IsAllowed(string? url) => FindBlockedScheme(url) is null;

    private static string StripControlCharacters(string value)
    {
        // Two passes on purpose (fail-closed): the first resolves &amp;, the second the reference it
        // revealed, so "javascript&amp;colon;" is judged as "javascript:" as well.
        var decoded = DecodeCharacterReferences(DecodeCharacterReferences(value));
        var builder = new StringBuilder(decoded.Length);
        foreach (var c in decoded)
        {
            // Browsers drop tab/CR/LF anywhere in a URL ("java\tscript:" still runs as javascript:).
            if (c is '\t' or '\r' or '\n')
            {
                continue;
            }

            builder.Append(c);
        }

        var text = builder.ToString();
        // …and ignore leading/trailing C0 controls and spaces, exactly like the URL parser does.
        var start = 0;
        var end = text.Length - 1;
        while (start <= end && text[start] <= ' ')
        {
            start++;
        }

        while (end >= start && text[end] <= ' ')
        {
            end--;
        }

        return start == 0 && end == text.Length - 1 ? text : text[start..(end + 1)];
    }

    /// <summary>
    /// Decodes the HTML character references that can hide a scheme: numeric references (decimal or
    /// hexadecimal, any number of digits including leading zeros, semicolon optional) and the named
    /// <c>amp</c>/<c>colon</c>/<c>tab</c>/<c>newline</c> entities. A reference that decodes to 0 or to a
    /// code point above U+10FFFF becomes U+FFFD, exactly like the browser's decoder.
    /// </summary>
    private static string DecodeCharacterReferences(string value)
    {
        if (value.IndexOf('&') < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        var index = 0;
        while (index < value.Length)
        {
            var ampersand = value.IndexOf('&', index);
            if (ampersand < 0)
            {
                builder.Append(value, index, value.Length - index);
                break;
            }

            builder.Append(value, index, ampersand - index);
            if (!TryDecodeReference(value, ampersand, out var decoded, out var consumed))
            {
                builder.Append('&');
                index = ampersand + 1;
                continue;
            }

            builder.Append(decoded);
            index = ampersand + consumed;
        }

        return builder.ToString();
    }

    /// <summary>Decodes one reference starting at <paramref name="start"/> (the <c>&amp;</c>).</summary>
    private static bool TryDecodeReference(string value, int start, out string decoded, out int consumed)
    {
        decoded = string.Empty;
        consumed = 0;

        if (start + 2 < value.Length && value[start + 1] == '#')
        {
            var index = start + 2;
            var isHex = index < value.Length && (value[index] == 'x' || value[index] == 'X');
            if (isHex)
            {
                index++;
            }

            var digitsStart = index;
            long codePoint = 0;
            var overflow = false;
            while (index < value.Length &&
                   (isHex ? char.IsAsciiHexDigit(value[index]) : char.IsAsciiDigit(value[index])))
            {
                var digit = isHex ? HexValue(value[index]) : value[index] - '0';
                if (codePoint > 0x10FFFF)
                {
                    overflow = true;
                }
                else
                {
                    codePoint = (codePoint * (isHex ? 16 : 10)) + digit;
                }

                index++;
            }

            if (index == digitsStart)
            {
                return false;
            }

            // The semicolon is optional in practice; consume it when present.
            if (index < value.Length && value[index] == ';')
            {
                index++;
            }

            var scalar = overflow || codePoint is 0 or > 0x10FFFF ? ReplacementCharacter : (int)codePoint;
            decoded = char.ConvertFromUtf32(scalar);
            consumed = index - start;
            return true;
        }

        var semicolon = value.IndexOf(';', start + 1);
        if (semicolon < 0 || semicolon - start > 12)
        {
            return false;
        }

        var name = value[(start + 1)..semicolon];
        string? replacement = null;
        if (name.Equals("amp", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "&";
        }
        else if (name.Equals("colon", StringComparison.OrdinalIgnoreCase))
        {
            replacement = ":";
        }
        else if (name.Equals("tab", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "\t";
        }
        else if (name.Equals("newline", StringComparison.OrdinalIgnoreCase))
        {
            replacement = "\n";
        }

        if (replacement is null)
        {
            return false;
        }

        decoded = replacement;
        consumed = semicolon - start + 1;
        return true;
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        _ => c - 'A' + 10,
    };

    private static bool IsSchemeSyntax(string scheme)
    {
        if (scheme.Length == 0 || !char.IsAsciiLetter(scheme[0]))
        {
            return false;
        }

        foreach (var c in scheme)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('+' or '-' or '.'))
            {
                return false;
            }
        }

        return true;
    }
}
