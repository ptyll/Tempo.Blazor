using System.Globalization;
using System.Text;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// The rendered-data signature of one block — the "version" <see cref="TmEmailCanvasBlock"/> compares in
/// its <c>ShouldRender</c> (Fáze 9A performance requirement).
/// <para>
/// The editor mutates the model IN PLACE (the inspector writes a property on the live block and then
/// commits), so neither object identity nor a document-wide revision can tell "which block changed": the
/// first is always the same object, the second re-renders all of them. The signature is the exact set of
/// values the renderer paints, serialized into one string, so two renders are comparable in O(block data)
/// — no DOM diff, no render tree, no sanitizer work. With 200 blocks that comparison is tens of
/// microseconds, while rebuilding one block's subtree is orders of magnitude more expensive.
/// </para>
/// <para>
/// It is deliberately a full string and not a hash: a hash collision would silently show stale data on the
/// canvas, and the cost of the full value is bounded by the block's own text size.
/// </para>
/// </summary>
internal static class EmailBlockSignature
{
    /// <summary>The signature of <paramref name="block"/> (nested blocks of a wrapper/group included).</summary>
    public static string Of(EmailBlockBase block)
    {
        var builder = new StringBuilder(256);
        AppendBlock(builder, block);
        return builder.ToString();
    }

    private static void AppendBlock(StringBuilder builder, EmailBlockBase block)
    {
        AppendCommon(builder, block);
        switch (block)
        {
            case EmailTextBlock text:
                Append(builder, text.Content, text.Color, text.FontFamily, text.FontSize, text.FontStyle,
                    text.FontWeight, text.LineHeight, text.LetterSpacing, text.Height, text.TextDecoration,
                    text.TextTransform, text.Align);
                break;
            case EmailButtonBlock button:
                Append(builder, button.Text, button.Href, button.BackgroundColor, button.Color,
                    button.FontFamily, button.FontSize, button.FontStyle, button.FontWeight, button.LineHeight,
                    button.LetterSpacing, button.TextAlign, button.TextDecoration, button.TextTransform,
                    button.Align, button.VerticalAlign, button.Border, button.BorderRadius, button.InnerPadding,
                    button.Width, button.Height);
                break;
            case EmailImageBlock image:
                Append(builder, image.Src, image.Alt, image.Href, image.Title, image.Align, image.Width,
                    image.Height, image.Border, image.BorderRadius);
                break;
            case EmailDividerBlock divider:
                Append(builder, divider.BorderColor, divider.BorderStyle, divider.BorderWidth, divider.Width,
                    divider.Align);
                break;
            case EmailSpacerBlock spacer:
                Append(builder, spacer.Height);
                break;
            case EmailRawBlock raw:
                Append(builder, raw.Content);
                break;
            case EmailTableBlock table:
                Append(builder, table.Align, table.Border, table.CellPadding, table.CellSpacing, table.Color,
                    table.FontFamily, table.FontSize, table.LineHeight, table.TableLayout, table.Width);
                foreach (var row in table.Rows)
                {
                    builder.Append("|r").Append(row.IsHeader ? '1' : '0');
                    foreach (var cell in row.Cells)
                    {
                        Append(builder, cell.Text, cell.Align, cell.Href, cell.BackgroundColor);
                        builder.Append(':').Append(cell.ColSpan).Append('/').Append(cell.RowSpan);
                    }
                }

                break;
            case EmailSocialBlock social:
                Append(builder, social.Mode, social.Align, social.IconSize, social.BorderRadius, social.Color,
                    social.FontSize, social.FontFamily, social.LineHeight, social.TextPadding);
                foreach (var element in social.Elements)
                {
                    Append(builder, element.Name, element.Href, element.Src, element.Alt, element.Label,
                        element.BackgroundColor);
                }

                break;
            case EmailHeroBlock hero:
                Append(builder, hero.Mode, hero.Height, hero.BackgroundColor, hero.BackgroundUrl,
                    hero.BackgroundWidth, hero.BackgroundHeight, hero.BackgroundPosition, hero.VerticalAlign);
                foreach (var child in hero.Blocks)
                {
                    builder.Append('{');
                    AppendBlock(builder, child);
                    builder.Append('}');
                }

                break;
            case EmailNavbarBlock navbar:
                Append(builder, navbar.Align, navbar.BaseUrl, navbar.Hamburger);
                foreach (var link in navbar.Links)
                {
                    Append(builder, link.Text, link.Href, link.Color, link.FontFamily, link.FontSize,
                        link.FontWeight, link.LineHeight, link.TextDecoration, link.TextTransform, link.Padding);
                }

                break;
            case EmailCarouselBlock carousel:
                Append(builder, carousel.Align, carousel.BorderRadius, carousel.IconWidth, carousel.Thumbnails,
                    carousel.TbBorderRadius);
                foreach (var image in carousel.Images)
                {
                    Append(builder, image.Src, image.Alt, image.Href, image.Title, image.ThumbnailsSrc);
                }

                break;
            case EmailAccordionBlock accordion:
                Append(builder, accordion.Border, accordion.IconAlign, accordion.IconPosition, accordion.IconHeight,
                    accordion.IconWidth, accordion.IconWrappedUrl, accordion.IconUnwrappedUrl, accordion.FontFamily);
                foreach (var item in accordion.Items)
                {
                    Append(builder, item.Title, item.BackgroundColor, item.TitleColor);
                }

                break;
            case EmailWrapperBlock wrapper:
                Append(builder, wrapper.BackgroundColor, wrapper.BackgroundUrl, wrapper.Border,
                    wrapper.BorderRadius, wrapper.TextAlign, wrapper.FullWidth.ToString());
                foreach (var section in wrapper.Sections)
                {
                    AppendSection(builder, section);
                }

                break;
            case EmailGroupBlock group:
                Append(builder, group.BackgroundColor, group.Direction, group.VerticalAlign, group.Width);
                foreach (var column in group.Columns)
                {
                    AppendColumn(builder, column);
                }

                break;
        }
    }

    private static void AppendSection(StringBuilder builder, EmailSection section)
    {
        builder.Append("|s");
        Append(builder, section.BackgroundColor, section.BackgroundUrl, section.Border, section.BorderRadius,
            section.Direction, section.Padding, section.TextAlign);
        foreach (var column in section.Columns)
        {
            AppendColumn(builder, column);
        }
    }

    private static void AppendColumn(StringBuilder builder, EmailColumn column)
    {
        builder.Append("|c");
        Append(builder, column.Width, column.VerticalAlign, column.BackgroundColor, column.Border,
            column.BorderRadius, column.Padding);
        foreach (var block in column.Blocks)
        {
            builder.Append('{');
            AppendBlock(builder, block);
            builder.Append('}');
        }
    }

    private static void AppendCommon(StringBuilder builder, EmailBlockBase block)
    {
        builder.Append(block.Type.ToString());
        Append(builder, block.Id.ToString("N"), block.ContainerBackgroundColor, block.Padding,
            block.PaddingTop, block.PaddingRight, block.PaddingBottom, block.PaddingLeft);
    }

    /// <summary>
    /// Appends the painted values, each as <c>U+001F{length}:{value}</c>. The LENGTH comes first (review fix):
    /// a value that itself contains the separator (an import or a JSON payload can carry U+001F) would
    /// otherwise shift the boundary and let two different blocks produce the same signature, which the render
    /// gate reads as "nothing changed" and leaves stale content on the canvas. <c>-1</c> marks a null value,
    /// so null and the empty string stay distinguishable.
    /// </summary>
    private static void Append(StringBuilder builder, params string?[] values)
    {
        foreach (var value in values)
        {
            builder.Append('\u001f').Append(value?.Length ?? -1).Append(':').Append(value);
        }
    }

    /// <summary>Formats a width (a percentage, a length) for the signature without culture drift.</summary>
    internal static string Invariant(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
