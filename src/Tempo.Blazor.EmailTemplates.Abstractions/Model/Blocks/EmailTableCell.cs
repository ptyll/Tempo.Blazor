namespace Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

/// <summary>A single cell of an <see cref="EmailTableRow"/>.</summary>
public sealed class EmailTableCell
{
    /// <summary>Gets or sets the cell text content.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Gets or sets the cell text alignment (<c>align</c>).</summary>
    public string? Align { get; set; }

    /// <summary>Gets or sets the column span (<c>colspan</c>), when greater than one.</summary>
    public int? ColSpan { get; set; }

    /// <summary>Gets or sets the row span (<c>rowspan</c>), when greater than one.</summary>
    public int? RowSpan { get; set; }

    /// <summary>
    /// Gets or sets an optional link target of the whole cell (an <c>&lt;a href&gt;</c> around the text).
    /// The value is checked against the one URL allow-list of the pipeline (§3.7a,
    /// <see cref="Rendering.EmailUrlPolicy"/>): a value outside the allow-list is replaced with <c>#</c>
    /// in the markup and reported as a warning, never emitted as written.
    /// </summary>
    public string? Href { get; set; }

    /// <summary>
    /// Gets or sets an optional cell background colour, emitted as the <c>bgcolor</c> attribute. Only a
    /// value on the colour allow-list (<see cref="Rendering.EmailColorPolicy"/>) is written — an invalid
    /// value is dropped and reported as a warning, so a crafted string can never reach <c>bgcolor</c> or
    /// <c>style</c>.
    /// </summary>
    public string? BackgroundColor { get; set; }
}
