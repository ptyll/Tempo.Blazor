using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Files;

/// <summary>
/// Shared text and formatting helpers of the PDF annotation cards (PdfViewer redesign
/// phase 4). One implementation serves both annotation panels: thread previews, thread
/// numbering and quoted-text truncation live here. (Since FZE-9 the time stamps render
/// through the shared <see cref="Tempo.Blazor.Localization.ITmRelativeTimeFormatter"/> and
/// the counted texts through <c>Loc.Plural</c>, so this class carries no formatting of its own.)
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
}
