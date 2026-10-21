using System.Globalization;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The client-side checks of an image upload, shared by the inspector (Fáze 7) and the carousel item dialog
/// (Fáze 9) so both refuse exactly the same files <b>before</b> the host's upload handler runs. A second copy
/// of this logic is how one path ends up accepting a 10 MB PDF while the other rejects it.
/// </summary>
internal static class ImageUploadValidation
{
    /// <summary>Matches a MIME type against the <c>accept</c> attribute syntax (comma separated, <c>*</c> wildcards).</summary>
    public static bool IsAcceptedType(string? contentType, string? accept)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return false;
        if (string.IsNullOrWhiteSpace(accept)) return true;

        foreach (var candidate in accept.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (candidate is "*" or "*/*") return true;
            var slash = candidate.IndexOf('/');
            if (slash < 0)
            {
                // A bare extension list (".png,.jpg") — compare the file's type family loosely.
                if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return true;
                continue;
            }

            var type = candidate[..slash];
            var subtype = candidate[(slash + 1)..];
            if (!string.Equals(type, "*", StringComparison.OrdinalIgnoreCase)
                && !contentType.StartsWith(type + "/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (subtype is "*" || contentType.EndsWith("/" + subtype, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>Formats a byte limit for a localized message ("2 MB", "512 KB"), current culture.</summary>
    public static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} MB", bytes / (1024d * 1024d))
            : string.Format(CultureInfo.CurrentCulture, "{0:0.#} KB", bytes / 1024d);
}
