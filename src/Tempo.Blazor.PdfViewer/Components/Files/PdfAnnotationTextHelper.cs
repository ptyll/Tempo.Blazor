using System.Globalization;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// Shared text and formatting helpers of the PDF annotation cards (PdfViewer redesign
/// phase 4). One implementation serves both annotation panels: thread previews, thread
/// numbering and quoted-text truncation live here, and <see cref="FormatTime"/> is the
/// single time formatter used by both the thread card header and the comment item.
/// </summary>
internal static class PdfAnnotationTextHelper
{
    /// <summary>Trims <paramref name="text"/> and cuts it to <paramref name="max"/> characters with an ellipsis.</summary>
    /// <param name="text">Text to truncate.</param>
    /// <param name="max">Maximum length of the returned text.</param>
    public static string Truncate(string text, int max = 120)
    {
        text = text.Trim();
        return text.Length <= max ? text : text[..max] + "…";
    }

    /// <summary>Builds the card preview: the first comment body, falling back to the quoted text or the stamp text.</summary>
    /// <param name="thread">Annotation thread to preview.</param>
    public static string Preview(DocumentCommentThread thread)
    {
        var first = thread.Comments.FirstOrDefault()?.Body;
        if (string.IsNullOrWhiteSpace(first))
        {
            first = thread.Anchor.HighlightedText ?? thread.StampText ?? string.Empty;
        }

        return Truncate(first ?? string.Empty, 60);
    }

    /// <summary>1-based position of <paramref name="thread"/> within <paramref name="threads"/> (badge number).</summary>
    /// <param name="threads">The full thread collection the list renders.</param>
    /// <param name="thread">Thread to locate.</param>
    public static int ThreadNumber(IReadOnlyList<DocumentCommentThread> threads, DocumentCommentThread thread)
    {
        for (var i = 0; i < threads.Count; i++)
        {
            if (ReferenceEquals(threads[i], thread) || string.Equals(threads[i].Id, thread.Id, StringComparison.Ordinal))
            {
                return i + 1;
            }
        }

        return 1;
    }

    /// <summary>
    /// The single time formatter of the PDF annotation cards: local time in the current
    /// culture's short date/time pattern ("g"). Used by the thread card header and the
    /// comment item so both rows render timestamps the same way (absolute until phase 9).
    /// </summary>
    /// <param name="value">Timestamp to format.</param>
    public static string FormatTime(DateTimeOffset value)
        => value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}
