namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// The URL allowlist of the AI markdown pipeline. A model answer is untrusted, so a link target is never
/// rendered as-is: absolute <c>http</c>/<c>https</c>/<c>mailto</c> targets and same-document relative
/// references survive, everything else (any other scheme, a protocol-relative target, whitespace or
/// control-character smuggling such as <c>java\tscript:</c>) collapses to <c>#</c>. Images are stricter:
/// only absolute <c>http</c>/<c>https</c> sources are loaded.
/// </summary>
/// <remarks>
/// The check runs on a normalised copy (whitespace and control characters removed, backslashes folded to
/// forward slashes) — a browser strips exactly those characters when it parses a scheme, so allowing a
/// value whose normalised form is allowed cannot be used to smuggle a different scheme through.
/// </remarks>
internal static class AiMarkdownUrlPolicy
{
    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    private static readonly string[] AllowedImageSchemes = ["http", "https"];

    /// <summary>The replacement used for every refused link target.</summary>
    internal const string RefusedUrl = "#";

    /// <summary>Returns the href to render for a link: the original target when allowed, otherwise <c>#</c>.</summary>
    internal static string SanitizeLink(string? url)
    {
        var value = url?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return RefusedUrl;
        }

        var normalized = Normalize(value);
        if (normalized.Length == 0)
        {
            return RefusedUrl;
        }

        if (normalized.StartsWith("//", StringComparison.Ordinal))
        {
            // Protocol-relative: the scheme would be inherited from the page, so the target is not
            // verifiable against the allowlist.
            return RefusedUrl;
        }

        var scheme = ExtractScheme(normalized);
        if (scheme is null)
        {
            // A relative reference (path, query or fragment) stays on the host's own origin.
            return value;
        }

        return Array.Exists(AllowedSchemes, allowed => string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase))
            ? value
            : RefusedUrl;
    }

    /// <summary>Returns whether an image may be loaded from the target (absolute http/https only).</summary>
    internal static bool IsSafeImageUrl(string? url)
    {
        var value = url?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            return false;
        }

        var normalized = Normalize(value);
        if (normalized.Length == 0 || normalized.StartsWith("//", StringComparison.Ordinal))
        {
            return false;
        }

        var scheme = ExtractScheme(normalized);
        return scheme is not null
            && Array.Exists(AllowedImageSchemes, allowed => string.Equals(allowed, scheme, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>True when the target is a <c>cite:</c> reference, i.e. an AI citation chip.</summary>
    internal static bool TryGetCitationId(string? url, out string sourceId)
    {
        sourceId = string.Empty;
        var value = url?.Trim() ?? string.Empty;
        const string prefix = "cite:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var id = value[prefix.Length..].Trim();
        if (id.Length == 0 || Normalize(id).Length == 0)
        {
            return false;
        }

        sourceId = id;
        return true;
    }

    private static string Normalize(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (ch <= ' ' || ch == '\u007f')
            {
                continue;
            }

            builder.Append(ch == '\\' ? '/' : ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// The scheme of an absolute URL, or <see langword="null"/> for a relative reference. A colon only
    /// starts a scheme when it precedes every path, query and fragment separator and the prefix is a valid
    /// scheme token.
    /// </summary>
    private static string? ExtractScheme(string normalized)
    {
        var colon = normalized.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
        {
            return null;
        }

        var slash = normalized.IndexOfAny(['/', '?', '#']);
        if (slash >= 0 && slash < colon)
        {
            return null;
        }

        var candidate = normalized[..colon];
        if (!char.IsAsciiLetter(candidate[0]))
        {
            return null;
        }

        foreach (var ch in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('+' or '-' or '.'))
            {
                return null;
            }
        }

        return candidate;
    }
}
