using System.Globalization;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Signing;

public partial class TmPdfTemplateDesigner
{
    private IReadOnlyList<string> GetDesignerCultures()
    {
        var cultures = new List<string>();
        AddCultures(cultures, SupportedCultures);
        AddCulture(cultures, Culture);
        AddCulture(cultures, FallbackCulture);
        return cultures;
    }

    private static void AddCultures(List<string> target, IEnumerable<string>? cultures)
    {
        if (cultures is null)
        {
            return;
        }

        foreach (var culture in cultures)
        {
            AddCulture(target, culture);
        }
    }

    private static void AddCulture(List<string> target, string? culture)
    {
        var normalized = NormalizeCulture(culture);
        if (!string.IsNullOrWhiteSpace(normalized)
            && !target.Any(item => string.Equals(item, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            target.Add(normalized);
        }
    }

    private static string? NormalizeCulture(string? culture)
    {
        var trimmed = culture?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(trimmed).Name;
        }
        catch (CultureNotFoundException)
        {
            return trimmed;
        }
    }

    private static string GetCultureLabel(string culture)
    {
        try
        {
            var cultureInfo = CultureInfo.GetCultureInfo(culture);
            return string.Create(CultureInfo.InvariantCulture, $"{cultureInfo.NativeName} ({cultureInfo.Name})");
        }
        catch (CultureNotFoundException)
        {
            return culture;
        }
    }

    private enum ContextMenuKind
    {
        Page,
        Field,
        Selection
    }

    private sealed record ContextMenuState(
        ContextMenuKind Kind,
        double X,
        double Y,
        string? FieldUuid,
        string? AttachmentUuid,
        int PageIndex,
        double? DocumentX,
        double? DocumentY);

    private sealed record AreaSnapshot(string FieldUuid, string AreaUuid, SigningFieldArea Area);

    private sealed record MoveState(double StartClientX, double StartClientY, IReadOnlyList<AreaSnapshot> Areas);

    private sealed record ResizeState(string FieldUuid, string AreaUuid, SigningFieldArea Area, SigningResizeHandle Handle, double StartClientX, double StartClientY);

    private sealed class PageSurfaceRect
    {
        public double Left { get; set; }

        public double Top { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }

    private sealed class FixedOrigin
    {
        public double Left { get; set; }

        public double Top { get; set; }
    }

    /// <summary>A long-press the pointer session wants opened as a context menu. Not part of the component contract.</summary>
    internal sealed record FieldContextMenuRequest
    {
        /// <summary>Field the finger held.</summary>
        public string? FieldUuid { get; set; }

        /// <summary>Viewport X of the finger.</summary>
        public double ClientX { get; set; }

        /// <summary>Viewport Y of the finger.</summary>
        public double ClientY { get; set; }
    }

    /// <summary>One committed move or resize reported by the pointer helper. Not part of the component contract.</summary>
    internal sealed record DesignerDragCommit
    {
        /// <summary>Field the gesture started on.</summary>
        public string? FieldUuid { get; set; }

        /// <summary>Resize handle, or null when the gesture moved the field.</summary>
        public string? Handle { get; set; }

        /// <summary>Area the resize handle belongs to. A move leaves it null and moves every area.</summary>
        public string? AreaUuid { get; set; }

        /// <summary>Horizontal delta normalized against the measured page surface.</summary>
        public double NormalizedDx { get; set; }

        /// <summary>Vertical delta normalized against the measured page surface.</summary>
        public double NormalizedDy { get; set; }
    }

    /// <summary>A palette press that ended on a page, reported by the pointer session. Not part of the component contract.</summary>
    internal sealed record PaletteDropCommit
    {
        /// <summary>Page the pointer ended on (<c>attachment:index</c>).</summary>
        public string? PageKey { get; set; }

        /// <summary>Horizontal position, 0–1 of the page surface.</summary>
        public double X { get; set; }

        /// <summary>Vertical position, 0–1 of the page surface.</summary>
        public double Y { get; set; }

        /// <summary>Field type read from the palette item. The JS session is the owner, so the drop no longer depends on a Blazor drag type.</summary>
        public string? Type { get; set; }
    }

    private sealed class DrawState
    {
        public DrawState(SigningDocumentPage page, double startX, double startY, SigningFieldArea area, bool isSelectionBox = false)
        {
            Page = page;
            StartX = startX;
            StartY = startY;
            Area = area;
            IsSelectionBox = isSelectionBox;
        }

        public SigningDocumentPage Page { get; }

        public string PageKey => GetPageKey(Page);

        public double StartX { get; }

        public double StartY { get; }

        public SigningFieldArea Area { get; set; }

        public bool IsSelectionBox { get; }
    }
}
