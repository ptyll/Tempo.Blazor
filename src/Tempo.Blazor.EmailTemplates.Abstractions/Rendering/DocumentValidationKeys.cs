namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>Localization keys for block-level document validation findings.</summary>
public static class DocumentValidationKeys
{
    /// <summary>A button has no link target.</summary>
    public const string ButtonHrefMissing = "validation.button.href_missing";

    /// <summary>An image has no source URL.</summary>
    public const string ImageSrcMissing = "validation.image.src_missing";

    /// <summary>An image has no alternative text (accessibility).</summary>
    public const string ImageAltMissing = "validation.image.alt_missing";

    /// <summary>A carousel image has no source URL.</summary>
    public const string CarouselImageSrcMissing = "validation.carousel.src_missing";

    /// <summary>The document has more sections than the configured import limit allows.</summary>
    public const string TooManySections = "validation.limits.sections";

    /// <summary>The document has more blocks than the configured import limit allows.</summary>
    public const string TooManyBlocks = "validation.limits.blocks";

    /// <summary>Blocks are nested deeper than the configured import limit allows.</summary>
    public const string TooDeeplyNested = "validation.limits.nesting";

    /// <summary>
    /// The preheader is longer than <see cref="EmailDocumentValidator.MaxPreheaderLength"/> characters, so
    /// the inbox truncates it. A recommendation (D9), never a failure.
    /// </summary>
    public const string PreheaderTooLong = "validation.preheader.too_long";
}
