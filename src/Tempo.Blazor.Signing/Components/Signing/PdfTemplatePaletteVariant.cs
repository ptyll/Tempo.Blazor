namespace Tempo.Blazor.Components.Signing;

/// <summary>Where a <see cref="TmPdfTemplateDesignerPalette"/> renders. One designer slot uses one variant.</summary>
public enum PdfTemplatePaletteVariant
{
    /// <summary>The docked left panel, with a heading and a help card.</summary>
    Docked,

    /// <summary>The tablet rail: icon-only square buttons in one scrolling row.</summary>
    Rail,

    /// <summary>The mobile sheet palette.</summary>
    Compact
}
