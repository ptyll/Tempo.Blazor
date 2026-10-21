using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Validates a document for structural and content correctness: layout (via
/// <see cref="LayoutValidator"/>) plus per-block rules such as required button hrefs and image
/// sources/alt text. Findings carry localization keys, never localized text.
/// </summary>
public sealed class EmailDocumentValidator
{
    /// <summary>
    /// The longest preheader an inbox reliably shows before truncating it, measured in TEXT ELEMENTS
    /// (graphemes) of the SOURCE template — tokens included, because the inbox truncates the rendered text
    /// at the same visual width. A longer value is reported as a recommendation
    /// (<see cref="LayoutSeverity.Info"/>), the single recommending rule of D9.
    /// </summary>
    public const int MaxPreheaderLength = 100;

    private readonly LayoutValidator _layoutValidator = new();

    /// <summary>Validates the document and returns all findings (layout + block level).</summary>
    public IReadOnlyList<DocumentValidationMessage> Validate(EmailTemplateDocument document)
    {
        var messages = new List<DocumentValidationMessage>();

        foreach (var layout in _layoutValidator.Validate(document))
            messages.Add(new DocumentValidationMessage(layout.Severity, layout.Key, layout.Path));

        // Count graphemes, not UTF-16 code units: a skin-toned emoji is two code points but one visible
        // character, and a code-point count flagged a preheader that fits (contract §3.10).
        if (document.Preheader is { } preheader
            && new StringInfo(preheader.Trim()).LengthInTextElements > MaxPreheaderLength)
            messages.Add(new(
                LayoutSeverity.Info, DocumentValidationKeys.PreheaderTooLong, document.Id.ToString()));

        foreach (var block in DocumentTree.AllBlocks(document))
            ValidateBlock(block, messages);

        return messages;
    }

    private static void ValidateBlock(EmailBlockBase block, List<DocumentValidationMessage> messages)
    {
        switch (block)
        {
            case EmailButtonBlock button when string.IsNullOrWhiteSpace(button.Href):
                messages.Add(new(LayoutSeverity.Error, DocumentValidationKeys.ButtonHrefMissing, block.Id.ToString()));
                break;
            case EmailImageBlock image:
                if (string.IsNullOrWhiteSpace(image.Src))
                    messages.Add(new(LayoutSeverity.Error, DocumentValidationKeys.ImageSrcMissing, block.Id.ToString()));
                if (string.IsNullOrWhiteSpace(image.Alt))
                    messages.Add(new(LayoutSeverity.Warning, DocumentValidationKeys.ImageAltMissing, block.Id.ToString()));
                break;
            case EmailCarouselBlock carousel:
                foreach (var img in carousel.Images.Where(i => string.IsNullOrWhiteSpace(i.Src)))
                    messages.Add(new(LayoutSeverity.Error, DocumentValidationKeys.CarouselImageSrcMissing, block.Id.ToString()));
                break;
        }
    }
}
