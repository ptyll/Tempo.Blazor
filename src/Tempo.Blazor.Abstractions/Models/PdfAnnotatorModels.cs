using System.Globalization;
using System.Text;

namespace Tempo.Blazor.Abstractions.Models;

/// <summary>Kind of a PDF annotation thread created by the annotator component.</summary>
public enum DocumentAnnotationKind
{
    /// <summary>A plain comment thread anchored to a page location. Default for existing threads.</summary>
    Comment = 0,

    /// <summary>A text highlight with an optional comment thread.</summary>
    Highlight = 1,

    /// <summary>A stamp (short text badge such as APPROVED) placed on the page.</summary>
    Stamp = 2,

    /// <summary>A freehand ink drawing with an optional comment thread.</summary>
    Drawing = 3
}

/// <summary>Normalized point on a document page, expressed as fractions of the page size.</summary>
public sealed class DocumentInkPoint
{
    /// <summary>Normalized horizontal position in the page, from 0 to 1.</summary>
    public double X { get; set; }

    /// <summary>Normalized vertical position in the page, from 0 to 1.</summary>
    public double Y { get; set; }

    /// <summary>Creates a normalized point, clamping both components into the page bounds.</summary>
    /// <param name="x">Normalized horizontal position.</param>
    /// <param name="y">Normalized vertical position.</param>
    public static DocumentInkPoint Create(double x, double y)
        => new() { X = Clamp01(x), Y = Clamp01(y) };

    private static double Clamp01(double value)
        => double.IsFinite(value) ? Math.Min(Math.Max(value, 0), 1) : 0;
}

/// <summary>Single freehand stroke of a drawing annotation, in normalized page coordinates.</summary>
public sealed class DocumentInkStroke
{
    /// <summary>Ordered points of the stroke.</summary>
    public List<DocumentInkPoint> Points { get; set; } = [];

    /// <summary>Stroke thickness as a fraction of the page width. Default is 0.004.</summary>
    public double Thickness { get; set; } = 0.004;

    /// <summary>Creates a stroke from raw coordinate pairs, clamping every point into the page bounds.</summary>
    /// <param name="points">Sequence of (x, y) normalized coordinate pairs.</param>
    /// <param name="thickness">Stroke thickness as a fraction of the page width.</param>
    public static DocumentInkStroke Create(IEnumerable<(double X, double Y)> points, double thickness = 0.004)
    {
        ArgumentNullException.ThrowIfNull(points);
        return new DocumentInkStroke
        {
            Points = [.. points.Select(p => DocumentInkPoint.Create(p.X, p.Y))],
            Thickness = double.IsFinite(thickness) && thickness > 0 ? thickness : 0.004
        };
    }
}

/// <summary>
/// Resolves the display color of a PDF annotation thread: an explicit thread color wins,
/// then a per-author color, then a per-role color, and finally a deterministic palette
/// color derived from the author id.
/// </summary>
public static class PdfAnnotationColorHelper
{
    /// <summary>Default annotation palette. All colors keep AA contrast for white marker text.</summary>
    public static readonly IReadOnlyList<string> DefaultPalette =
    [
        "#2563eb", // blue 600
        "#b45309", // amber 700
        "#0e7490", // cyan 700
        "#be185d", // pink 700
        "#15803d", // green 700
        "#7c3aed", // violet 600
        "#b91c1c", // red 700
        "#475569"  // slate 600
    ];

    /// <summary>
    /// The library's default annotation color: the first entry of <see cref="DefaultPalette"/>
    /// (blue 600). Components resolve their draft color through this instead of carrying a hex
    /// literal, so the palette stays owned by the helper alone. Declared AFTER the palette —
    /// static initializers run in declaration order.
    /// </summary>
    public static readonly string DefaultColor = DefaultPalette[0];

    /// <summary>Resolves the color for an annotation thread.</summary>
    /// <param name="thread">Annotation thread.</param>
    /// <param name="authorColors">Optional per-author colors keyed by user id.</param>
    /// <param name="roleColors">Optional per-role colors keyed by role.</param>
    /// <param name="users">Optional known users used to translate an author id to a role.</param>
    /// <param name="palette">Optional palette overriding <see cref="DefaultPalette"/>.</param>
    public static string ResolveColor(
        DocumentCommentThread thread,
        IReadOnlyDictionary<string, string>? authorColors,
        IReadOnlyDictionary<string, string>? roleColors,
        IReadOnlyList<DocumentCommentUser>? users,
        IReadOnlyList<string>? palette = null)
    {
        ArgumentNullException.ThrowIfNull(thread);

        if (!string.IsNullOrWhiteSpace(thread.Color))
        {
            return thread.Color!;
        }

        var authorId = thread.Comments.FirstOrDefault()?.AuthorId ?? string.Empty;
        return ResolveForAuthor(authorId, authorColors, roleColors, users, palette);
    }

    /// <summary>Resolves the color used for a given author's annotations.</summary>
    /// <param name="authorId">Stable user identifier of the author.</param>
    /// <param name="authorColors">Optional per-author colors keyed by user id.</param>
    /// <param name="roleColors">Optional per-role colors keyed by role.</param>
    /// <param name="users">Optional known users used to translate an author id to a role.</param>
    /// <param name="palette">Optional palette overriding <see cref="DefaultPalette"/>.</param>
    public static string ResolveForAuthor(
        string authorId,
        IReadOnlyDictionary<string, string>? authorColors,
        IReadOnlyDictionary<string, string>? roleColors,
        IReadOnlyList<DocumentCommentUser>? users,
        IReadOnlyList<string>? palette = null)
    {
        authorId ??= string.Empty;

        if (authorColors is not null && authorColors.TryGetValue(authorId, out var authorColor)
            && !string.IsNullOrWhiteSpace(authorColor))
        {
            return authorColor;
        }

        if (roleColors is not null)
        {
            var role = users?.FirstOrDefault(user => string.Equals(user.UserId, authorId, StringComparison.Ordinal))?.Role;
            if (!string.IsNullOrWhiteSpace(role) && roleColors.TryGetValue(role!, out var roleColor)
                && !string.IsNullOrWhiteSpace(roleColor))
            {
                return roleColor;
            }
        }

        var colors = palette is { Count: > 0 } ? palette : DefaultPalette;
        return colors[(int)(StableHash(authorId) % (uint)colors.Count)];
    }

    // Deterministic FNV-1a hash so an author keeps the same palette color across sessions and platforms.
    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var ch in value)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return hash;
    }
}

/// <summary>
/// Builds the culture-invariant JSON payload consumed by the annotator's JavaScript
/// export and print functions. The payload carries one entry per annotation thread with
/// its page, kind, geometry, color, quoted text, ink strokes, stamp text, and comments.
/// </summary>
public static class PdfAnnotationExportPayloadBuilder
{
    /// <summary>Builds the export payload for a set of annotation threads.</summary>
    /// <param name="threads">Annotation threads to export.</param>
    /// <param name="includeResolved">Whether resolved threads are included.</param>
    /// <param name="authorColors">Optional per-author colors keyed by user id.</param>
    /// <param name="roleColors">Optional per-role colors keyed by role.</param>
    /// <param name="users">Optional known users used to translate an author id to a role.</param>
    public static string Build(
        IEnumerable<DocumentCommentThread> threads,
        bool includeResolved,
        IReadOnlyDictionary<string, string>? authorColors = null,
        IReadOnlyDictionary<string, string>? roleColors = null,
        IReadOnlyList<DocumentCommentUser>? users = null)
    {
        ArgumentNullException.ThrowIfNull(threads);

        var builder = new StringBuilder();
        builder.Append('[');
        var first = true;
        var index = 0;
        foreach (var thread in threads)
        {
            index++;
            if (!includeResolved && thread.Status == DocumentCommentThreadStatus.Resolved)
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            AppendThread(builder, thread, index, authorColors, roleColors, users);
        }

        builder.Append(']');
        return builder.ToString();
    }

    private static void AppendThread(
        StringBuilder builder,
        DocumentCommentThread thread,
        int number,
        IReadOnlyDictionary<string, string>? authorColors,
        IReadOnlyDictionary<string, string>? roleColors,
        IReadOnlyList<DocumentCommentUser>? users)
    {
        builder.Append('{');
        AppendString(builder, "id", thread.Id);
        builder.Append(',');
        AppendNumber(builder, "number", number);
        builder.Append(',');
        AppendString(builder, "kind", KindName(thread.Kind));
        builder.Append(',');
        AppendNumber(builder, "page", thread.Anchor.PageNumber);
        builder.Append(',');
        AppendString(builder, "color", PdfAnnotationColorHelper.ResolveColor(thread, authorColors, roleColors, users));
        builder.Append(',');
        AppendString(builder, "status", thread.Status == DocumentCommentThreadStatus.Resolved ? "resolved" : "open");
        builder.Append(',');

        builder.Append("\"x\":").Append(Num(thread.Anchor.X)).Append(',');
        builder.Append("\"y\":").Append(Num(thread.Anchor.Y)).Append(',');
        builder.Append("\"width\":").Append(Num(thread.Anchor.Width)).Append(',');
        builder.Append("\"height\":").Append(Num(thread.Anchor.Height)).Append(',');

        builder.Append("\"rects\":[");
        for (var i = 0; i < thread.Anchor.Rects.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var rect = thread.Anchor.Rects[i];
            builder.Append("{\"x\":").Append(Num(rect.X))
                .Append(",\"y\":").Append(Num(rect.Y))
                .Append(",\"width\":").Append(Num(rect.Width))
                .Append(",\"height\":").Append(Num(rect.Height))
                .Append('}');
        }

        builder.Append("],");

        builder.Append("\"strokes\":[");
        for (var i = 0; i < thread.InkStrokes.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var stroke = thread.InkStrokes[i];
            builder.Append("{\"thickness\":").Append(Num(stroke.Thickness)).Append(",\"points\":[");
            for (var j = 0; j < stroke.Points.Count; j++)
            {
                if (j > 0)
                {
                    builder.Append(',');
                }

                builder.Append("{\"x\":").Append(Num(stroke.Points[j].X))
                    .Append(",\"y\":").Append(Num(stroke.Points[j].Y))
                    .Append('}');
            }

            builder.Append("]}");
        }

        builder.Append("],");

        AppendString(builder, "stampText", thread.StampText);
        builder.Append(',');
        AppendString(builder, "quote", thread.Anchor.HighlightedText);
        builder.Append(',');

        builder.Append("\"comments\":[");
        for (var i = 0; i < thread.Comments.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            var comment = thread.Comments[i];
            builder.Append('{');
            AppendString(builder, "author", comment.AuthorName);
            builder.Append(',');
            AppendString(builder, "body", comment.Body);
            builder.Append(',');
            AppendString(builder, "createdAt", comment.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            builder.Append('}');
        }

        builder.Append(']');
        builder.Append('}');
    }

    private static string KindName(DocumentAnnotationKind kind)
        => kind switch
        {
            DocumentAnnotationKind.Highlight => "highlight",
            DocumentAnnotationKind.Stamp => "stamp",
            DocumentAnnotationKind.Drawing => "drawing",
            _ => "comment"
        };

    private static string Num(double value)
        => double.IsFinite(value)
            ? value.ToString("0.######", CultureInfo.InvariantCulture)
            : "0";

    private static void AppendNumber(StringBuilder builder, string name, int value)
        => builder.Append('"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendString(StringBuilder builder, string name, string? value)
    {
        builder.Append('"').Append(name).Append("\":");
        if (value is null)
        {
            builder.Append("null");
            return;
        }

        builder.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (ch < ' ')
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        builder.Append('"');
    }
}
