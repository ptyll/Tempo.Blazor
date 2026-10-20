using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Extracts every template variable referenced anywhere in a document — subject, preheader, block
/// content and URLs, visibility conditions and raw block content — as a merged, de-duplicated set.
/// </summary>
public static class EmailDocumentVariableExtractor
{
    /// <summary>Returns the variables referenced across the whole document.</summary>
    public static IReadOnlyList<TemplateVariableInfo> Extract(EmailTemplateDocument document)
        => Extract(document, RegexTemplateVariableExtractor.Instance);

    /// <summary>
    /// Returns the variables referenced across the whole document, extracted with
    /// <paramref name="extractor"/>. The baseline <see cref="RegexTemplateVariableExtractor"/> keeps the
    /// picker working in a WASM editor without the Engine package;
    /// <c>ScribanTemplateVariableExtractor</c> (Engine) adds filters and loop-local variables.
    /// </summary>
    public static IReadOnlyList<TemplateVariableInfo> Extract(
        EmailTemplateDocument document, ITemplateVariableExtractor extractor)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(extractor);

        var merged = new Dictionary<string, VariableKind>(StringComparer.Ordinal);

        foreach (var text in EnumerateTemplateStrings(document))
            foreach (var info in extractor.ExtractInfos(text))
            {
                // Collection wins over scalar when the same path appears both ways.
                if (info.Kind == VariableKind.Collection || !merged.ContainsKey(info.Path))
                    merged[info.Path] = info.Kind;
            }

        return merged.Select(kv => new TemplateVariableInfo(kv.Key, kv.Value)).ToList();
    }

    private static IEnumerable<string> EnumerateTemplateStrings(EmailTemplateDocument document)
    {
        yield return document.Subject;
        if (document.Preheader is not null) yield return document.Preheader;

        foreach (var block in DocumentTree.AllBlocks(document))
        {
            // VisibleWhen is a bare Scriban expression; wrap it so its variables are extracted.
            if (!string.IsNullOrWhiteSpace(block.VisibleWhen)) yield return "{{ " + block.VisibleWhen + " }}";

            foreach (var value in EnumerateBlockTemplateStrings(block))
                yield return value;
        }
    }

    private static IEnumerable<string> EnumerateBlockTemplateStrings(EmailBlockBase block)
    {
        return block switch
        {
            EmailTextBlock t => new[] { t.Content },
            EmailButtonBlock b => EnumerateButtonStrings(b),
            EmailImageBlock i => EnumerateImageStrings(i),
            EmailRawBlock r => new[] { r.Content },
            EmailTableBlock table => EnumerateTableStrings(table),
            EmailSocialBlock social => EnumerateSocialStrings(social),
            EmailNavbarBlock navbar => EnumerateNavbarStrings(navbar),
            EmailCarouselBlock carousel => EnumerateCarouselStrings(carousel),
            EmailAccordionBlock accordion => EnumerateAccordionStrings(accordion),
            _ => Array.Empty<string>(),
        };
    }

    private static IEnumerable<string> EnumerateButtonStrings(EmailButtonBlock button)
    {
        yield return button.Text;
        if (button.Href is not null) yield return button.Href;
    }

    private static IEnumerable<string> EnumerateImageStrings(EmailImageBlock image)
    {
        yield return image.Alt;
        if (image.Href is not null) yield return image.Href;
        if (image.Src is not null) yield return image.Src;
    }

    private static IEnumerable<string> EnumerateTableStrings(EmailTableBlock table)
    {
        foreach (var row in table.Rows)
            foreach (var cell in row.Cells)
                yield return cell.Text;
    }

    private static IEnumerable<string> EnumerateSocialStrings(EmailSocialBlock social)
    {
        foreach (var e in social.Elements)
        {
            if (e.Label is not null) yield return e.Label;
            if (e.Href is not null) yield return e.Href;
        }
    }

    private static IEnumerable<string> EnumerateNavbarStrings(EmailNavbarBlock navbar)
    {
        foreach (var link in navbar.Links)
        {
            yield return link.Text;
            if (link.Href is not null) yield return link.Href;
        }
    }

    private static IEnumerable<string> EnumerateCarouselStrings(EmailCarouselBlock carousel)
    {
        foreach (var img in carousel.Images)
        {
            yield return img.Alt;
            if (img.Href is not null) yield return img.Href;
        }
    }

    private static IEnumerable<string> EnumerateAccordionStrings(EmailAccordionBlock accordion)
    {
        foreach (var item in accordion.Items)
        {
            yield return item.Title;
            yield return item.Content;
        }
    }
}
