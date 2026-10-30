using Tempo.Blazor.Components.Spreadsheet.Enums;

namespace Tempo.Blazor.Components.Spreadsheet.Models;

/// <summary>
/// Represents the visual style of a spreadsheet cell.
/// </summary>
public sealed class SpreadsheetCellStyle
{
    /// <summary>The font family name (e.g. Arial, Calibri).</summary>
    public string FontFamily { get; set; } = "Calibri";

    /// <summary>The font size in points.</summary>
    public double FontSize { get; set; } = 11;

    /// <summary>Whether the text is bold.</summary>
    public bool Bold { get; set; }

    /// <summary>Whether the text is italic.</summary>
    public bool Italic { get; set; }

    /// <summary>Whether the text is underlined.</summary>
    public bool Underline { get; set; }

    /// <summary>Whether the text has double underline.</summary>
    public bool DoubleUnderline { get; set; }

    /// <summary>Whether the text is struck through.</summary>
    public bool StrikeThrough { get; set; }

    /// <summary>Indent level (0–15). Each level adds 12px left padding.</summary>
    public int Indent { get; set; }

    /// <summary>Text rotation in degrees (-90 to 90). 0 = normal horizontal.</summary>
    public int TextRotation { get; set; }

    /// <summary>Whether to shrink text to fit the cell width.</summary>
    public bool ShrinkToFit { get; set; }

    /// <summary>The foreground (text) color in hex format. Defaults to black.</summary>
    public string ForeColor { get; set; } = "#000000";

    /// <summary>The background fill color in hex format. Defaults to transparent.</summary>
    public string BackgroundColor { get; set; } = "transparent";

    /// <summary>The horizontal alignment of cell content.</summary>
    public SpreadsheetHorizontalAlign HorizontalAlign { get; set; } = SpreadsheetHorizontalAlign.General;

    /// <summary>The vertical alignment of cell content.</summary>
    public SpreadsheetVerticalAlign VerticalAlign { get; set; } = SpreadsheetVerticalAlign.Bottom;

    /// <summary>Whether text wraps inside the cell.</summary>
    public bool TextWrap { get; set; }

    /// <summary>The number format string (e.g. #,##0.00, yyyy-mm-dd).</summary>
    public string NumberFormat { get; set; } = "General";

    /// <summary>The border at the top edge of the cell.</summary>
    public SpreadsheetBorder BorderTop { get; set; } = new();

    /// <summary>The border at the right edge of the cell.</summary>
    public SpreadsheetBorder BorderRight { get; set; } = new();

    /// <summary>The border at the bottom edge of the cell.</summary>
    public SpreadsheetBorder BorderBottom { get; set; } = new();

    /// <summary>The border at the left edge of the cell.</summary>
    public SpreadsheetBorder BorderLeft { get; set; } = new();

    /// <summary>
    /// Copies every visual property of <paramref name="source"/> onto this style (the border objects are
    /// copied, not shared). Used by the spreadsheet's Fill down / Fill right, which follows Excel and
    /// copies the SOURCE cell's format into the filled cells, and by the style command so the copy is one
    /// undoable step.
    /// </summary>
    /// <param name="source">The style to copy from.</param>
    public void CopyFrom(SpreadsheetCellStyle source)
    {
        ArgumentNullException.ThrowIfNull(source);

        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        Bold = source.Bold;
        Italic = source.Italic;
        Underline = source.Underline;
        DoubleUnderline = source.DoubleUnderline;
        StrikeThrough = source.StrikeThrough;
        Indent = source.Indent;
        TextRotation = source.TextRotation;
        ShrinkToFit = source.ShrinkToFit;
        ForeColor = source.ForeColor;
        BackgroundColor = source.BackgroundColor;
        HorizontalAlign = source.HorizontalAlign;
        VerticalAlign = source.VerticalAlign;
        TextWrap = source.TextWrap;
        NumberFormat = source.NumberFormat;
        BorderTop = new SpreadsheetBorder(source.BorderTop.Style, source.BorderTop.Color);
        BorderRight = new SpreadsheetBorder(source.BorderRight.Style, source.BorderRight.Color);
        BorderBottom = new SpreadsheetBorder(source.BorderBottom.Style, source.BorderBottom.Color);
        BorderLeft = new SpreadsheetBorder(source.BorderLeft.Style, source.BorderLeft.Color);
    }

    /// <summary>Creates a deep copy of this style.</summary>
    public SpreadsheetCellStyle Clone() => new()
    {
        FontFamily = FontFamily,
        FontSize = FontSize,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        DoubleUnderline = DoubleUnderline,
        StrikeThrough = StrikeThrough,
        Indent = Indent,
        TextRotation = TextRotation,
        ShrinkToFit = ShrinkToFit,
        ForeColor = ForeColor,
        BackgroundColor = BackgroundColor,
        HorizontalAlign = HorizontalAlign,
        VerticalAlign = VerticalAlign,
        TextWrap = TextWrap,
        NumberFormat = NumberFormat,
        BorderTop = new SpreadsheetBorder(BorderTop.Style, BorderTop.Color),
        BorderRight = new SpreadsheetBorder(BorderRight.Style, BorderRight.Color),
        BorderBottom = new SpreadsheetBorder(BorderBottom.Style, BorderBottom.Color),
        BorderLeft = new SpreadsheetBorder(BorderLeft.Style, BorderLeft.Color)
    };
}
