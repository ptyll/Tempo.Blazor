using System.Reflection;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Import;

/// <summary>
/// Reports the model values an import could not keep usable: a link whose scheme the pipeline refuses (kept in
/// the model so the editor can show and fix it — every emission path fail-closes it to <c>#</c>) and a colour
/// that is not a CSS colour (it is written into the message's style, so an unparsable value cannot be emitted).
/// </summary>
/// <remarks>
/// One pass over the whole document, so the answer is CONSISTENT for every block kind: earlier only a table
/// cell's <c>href</c>/<c>bgcolor</c> was audited, while a blocked button link or a broken section colour went
/// through silently. Only the SCHEME and the PROPERTY NAME are ever reported — never the value, which may carry
/// crafted markup and data.
/// </remarks>
internal static partial class ImportValueAudit
{
    /// <summary>The colour properties the MJML body components share (read by name, so a new block needs no edit here).</summary>
    private static readonly IReadOnlyDictionary<string, string> ColourProperties = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Color"] = "color",
        ["BackgroundColor"] = "background-color",
        ["BorderColor"] = "border-color",
        ["ContainerBackgroundColor"] = "container-background-color",
    };

    /// <summary>The audit findings of one imported document, links first, then colours (each deduplicated).</summary>
    public static IEnumerable<ImportMessage> Findings(EmailTemplateDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var links = new SortedSet<string>(StringComparer.Ordinal);
        var colours = new SortedSet<string>(StringComparer.Ordinal);

        void Link(string? url)
        {
            if (EmailUrlPolicy.FindBlockedScheme(url) is { } scheme) links.Add(scheme);
        }

        void Colour(string property, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !CssColourValue.IsPlausible(value))
            {
                colours.Add(property);
            }
        }

        foreach (var font in document.Styles.Fonts) Link(font.Href);
        Colour("mj-body background-color", document.Styles.BackgroundColor);

        foreach (var section in DocumentTree.AllSections(document))
        {
            Link(section.BackgroundUrl);
            Colour("mj-section background-color", section.BackgroundColor);
            foreach (var column in section.Columns)
            {
                Colour("mj-column background-color", column.BackgroundColor);
            }
        }

        foreach (var block in DocumentTree.AllBlocks(document))
        {
            var elementName = "mj-" + ElementOf(block);
            foreach (var (property, attribute) in ColourProperties)
            {
                Colour($"{elementName} {attribute}", ColourOf(block, property));
            }

            switch (block)
            {
                case EmailButtonBlock button:
                    Link(button.Href);
                    break;
                case EmailImageBlock image:
                    Link(image.Src);
                    Link(image.Href);
                    break;
                case EmailTableBlock:
                    // Cell links/colours have their own report (ImportKeys.BlockedTableLink /
                    // InvalidTableColor, with the cell reference), so they are not repeated here.
                    break;
                case EmailNavbarBlock navbar:
                    foreach (var link in navbar.Links)
                    {
                        Link(link.Href);
                        Colour("mj-navbar-link color", link.Color);
                    }

                    break;
                case EmailSocialBlock social:
                    foreach (var socialElement in social.Elements)
                    {
                        Link(socialElement.Href);
                        Link(socialElement.Src);
                        Colour("mj-social-element background-color", socialElement.BackgroundColor);
                    }

                    break;
                case EmailCarouselBlock carousel:
                    foreach (var image in carousel.Images)
                    {
                        Link(image.Src);
                        Link(image.Href);
                        Link(image.ThumbnailsSrc);
                    }

                    break;
                case EmailHeroBlock hero:
                    Link(hero.BackgroundUrl);
                    break;
                case EmailWrapperBlock wrapper:
                    Link(wrapper.BackgroundUrl);
                    break;
                case EmailAccordionBlock accordion:
                    foreach (var item in accordion.Items)
                    {
                        Colour("mj-accordion-element background-color", item.BackgroundColor);
                        Colour("mj-accordion-title color", item.TitleColor);
                    }

                    break;
            }
        }

        foreach (var scheme in links) yield return new ImportMessage(ImportKeys.BlockedLink, scheme);
        foreach (var property in colours) yield return new ImportMessage(ImportKeys.InvalidColor, property);
    }

    /// <summary>Reads one of the shared colour properties of a block; a block without it simply has no value.</summary>
    private static string? ColourOf(EmailBlockBase block, string property)
        => block.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(block) as string;

    /// <summary>The MJML element name of a block, for the property names the messages carry.</summary>
    private static string ElementOf(EmailBlockBase block) => block.Type switch
    {
        BlockType.Text => "text",
        BlockType.Button => "button",
        BlockType.Image => "image",
        BlockType.Divider => "divider",
        BlockType.Spacer => "spacer",
        BlockType.Raw => "raw",
        BlockType.Table => "table",
        BlockType.Social => "social",
        BlockType.Navbar => "navbar",
        BlockType.Carousel => "carousel",
        BlockType.Accordion => "accordion",
        BlockType.Hero => "hero",
        BlockType.Wrapper => "wrapper",
        BlockType.Group => "group",
        _ => "block",
    };

}
