using System.Globalization;
using System.Text;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>The three export surfaces of <see cref="TmEmailExportDialog"/>.</summary>
internal enum EmailExportFormat
{
    /// <summary>Canonical MJML markup (lossless: <c>MjmlGeneratorOptions.ForExport</c>).</summary>
    Mjml,

    /// <summary>The canonical content JSON used by the API and the import dialog.</summary>
    Json,

    /// <summary>Rendered HTML from <see cref="Abstractions.Rendering.IEmailPreviewRenderer"/>.</summary>
    Html,
}

/// <summary>
/// The file identity of one export mode: extension, MIME type and the Prism language for the read-only code
/// surface, plus the slugged file name. Kept in one place so the footer, the download and the tests cannot
/// drift apart.
/// </summary>
internal static class EmailExportFile
{
    /// <summary>The file name used when the template has no usable name.</summary>
    internal const string FallbackSlug = "email-template";

    /// <summary>The extension of the format (<c>mjml</c>, <c>json</c>, <c>html</c>).</summary>
    internal static string Extension(EmailExportFormat format) => format switch
    {
        EmailExportFormat.Json => "json",
        EmailExportFormat.Html => "html",
        _ => "mjml",
    };

    /// <summary>
    /// The MIME type the browser download is labelled with. MJML has no registered type, so it is plain text;
    /// JSON and HTML use their standard types. Every value names the UTF-8 charset because the payload is
    /// written as UTF-8 base64.
    /// </summary>
    internal static string MimeType(EmailExportFormat format) => format switch
    {
        EmailExportFormat.Json => "application/json;charset=utf-8",
        EmailExportFormat.Html => "text/html;charset=utf-8",
        _ => "text/plain;charset=utf-8",
    };

    /// <summary>The Prism grammar id for the read-only code surface (MJML and HTML are both markup).</summary>
    internal static string Language(EmailExportFormat format)
        => format == EmailExportFormat.Json ? "json" : "markup";

    /// <summary>The download file name: the slugged template name plus the format's extension.</summary>
    internal static string FileName(string? name, EmailExportFormat format)
        => $"{Slug(name)}.{Extension(format)}";

    /// <summary>
    /// The template name as a file-name-safe slug: lower case, diacritics folded to ASCII, runs of anything
    /// else replaced by a single dash and capped at 60 characters. A name that yields nothing (empty, or only
    /// punctuation) falls back to <see cref="FallbackSlug"/>.
    /// </summary>
    internal static string Slug(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return FallbackSlug;

        var normalized = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);
        var lastWasDash = false;
        foreach (var ch in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;

            var c = char.ToLowerInvariant(ch);
            var isAsciiLetter = c is >= 'a' and <= 'z';
            var isDigit = c is >= '0' and <= '9';
            if (isAsciiLetter || isDigit)
            {
                if (builder.Length >= 60) break;
                builder.Append(c);
                lastWasDash = false;
            }
            else if (!lastWasDash && builder.Length > 0 && builder.Length < 60)
            {
                builder.Append('-');
                lastWasDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length == 0 ? FallbackSlug : slug;
    }
}
