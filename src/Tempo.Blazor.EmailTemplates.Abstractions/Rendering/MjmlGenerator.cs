using System.Text;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Generates MJML markup from an <see cref="EmailTemplateDocument"/>. Default attribute values are
/// omitted for clean output, attribute values are escaped, text-block HTML is sanitized, and a
/// block's <see cref="EmailBlockBase.VisibleWhen"/> wraps it in a Scriban condition.
/// </summary>
public sealed class MjmlGenerator
{
    /// <summary>Generates MJML for the document using the given options (render-safe by default).</summary>
    public string Generate(EmailTemplateDocument document, MjmlGeneratorOptions? options = null)
    {
        options ??= MjmlGeneratorOptions.Default;
        var sb = new StringBuilder();
        var lang = string.IsNullOrEmpty(document.Language) ? null : document.Language;

        sb.Append("<mjml");
        if (lang is not null) sb.Append(" lang=\"").Append(MjmlEscape.Attribute(lang)).Append('"');
        sb.Append(">\n");

        WriteHead(sb, document, options);
        WriteBody(sb, document);

        sb.Append("</mjml>\n");
        return sb.ToString();
    }

    // ── Head ────────────────────────────────────────────────────────────────────────────────────

    private static void WriteHead(StringBuilder sb, EmailTemplateDocument document, MjmlGeneratorOptions options)
    {
        var styles = document.Styles;
        sb.Append("<mj-head>\n");

        if (!string.IsNullOrEmpty(document.Subject))
            sb.Append("<mj-title>").Append(MjmlEscape.Text(document.Subject)).Append("</mj-title>\n");
        if (!string.IsNullOrEmpty(document.Preheader))
            sb.Append("<mj-preview>").Append(MjmlEscape.Text(document.Preheader)).Append("</mj-preview>\n");
        if (!string.IsNullOrEmpty(styles.Breakpoint) && !string.Equals(styles.Breakpoint, "480px", StringComparison.Ordinal))
            sb.Append("<mj-breakpoint width=\"").Append(MjmlEscape.Attribute(styles.Breakpoint)).Append("\" />\n");

        foreach (var font in styles.Fonts)
            sb.Append("<mj-font name=\"").Append(MjmlEscape.Attribute(font.Name))
              .Append("\" href=\"").Append(MjmlEscape.Attribute(font.Href)).Append("\" />\n");

        WriteAttributes(sb, styles);

        foreach (var style in styles.Styles)
        {
            sb.Append(style.Inline ? "<mj-style inline=\"inline\">" : "<mj-style>");
            sb.Append(style.Css).Append("</mj-style>\n");
        }

        if (options.EmitHtmlAttributes && styles.HtmlAttributes.Count > 0)
            WriteHtmlAttributes(sb, styles);

        sb.Append("</mj-head>\n");
    }

    private static void WriteAttributes(StringBuilder sb, TemplateStyles styles)
    {
        // Apply the global font-family as an mj-all default unless the cascade already sets one.
        var all = new Dictionary<string, string>(styles.Attributes.All, StringComparer.Ordinal);
        if (!all.ContainsKey("font-family") && !string.IsNullOrEmpty(styles.FontFamily))
            all["font-family"] = styles.FontFamily;

        if (all.Count == 0 && styles.Attributes.PerTag.Count == 0 && styles.Attributes.Classes.Count == 0)
            return;

        sb.Append("<mj-attributes>\n");
        // NOTE (E0.9): mj-attributes children MUST use explicit closing tags; self-closing breaks Mjml.Net.
        if (all.Count > 0)
            sb.Append("<mj-all").Append(Pairs(all)).Append("></mj-all>\n");
        foreach (var (tag, attrs) in styles.Attributes.PerTag)
            sb.Append('<').Append(tag).Append(Pairs(attrs)).Append("></").Append(tag).Append(">\n");
        foreach (var (name, attrs) in styles.Attributes.Classes)
            sb.Append("<mj-class name=\"").Append(MjmlEscape.Attribute(name)).Append('"')
              .Append(Pairs(attrs)).Append("></mj-class>\n");
        sb.Append("</mj-attributes>\n");
    }

    private static void WriteHtmlAttributes(StringBuilder sb, TemplateStyles styles)
    {
        sb.Append("<mj-html-attributes>\n");
        foreach (var selector in styles.HtmlAttributes)
        {
            sb.Append("<mj-selector path=\"").Append(MjmlEscape.Attribute(selector.Path)).Append("\">\n");
            foreach (var (name, value) in selector.Attributes)
                sb.Append("<mj-html-attribute name=\"").Append(MjmlEscape.Attribute(name)).Append("\">")
                  .Append(MjmlEscape.Text(value)).Append("</mj-html-attribute>\n");
            sb.Append("</mj-selector>\n");
        }
        sb.Append("</mj-html-attributes>\n");
    }

    private static string Pairs(Dictionary<string, string> attrs)
    {
        var sb = new StringBuilder();
        foreach (var (name, value) in attrs)
            sb.Append(' ').Append(name).Append("=\"").Append(MjmlEscape.Attribute(value)).Append('"');
        return sb.ToString();
    }

    // ── Body / layout ─────────────────────────────────────────────────────────────────────────

    private static void WriteBody(StringBuilder sb, EmailTemplateDocument document)
    {
        var width = document.Styles.ContentWidth;
        sb.Append("<mj-body");
        if (!string.IsNullOrEmpty(width)) sb.Append(" width=\"").Append(MjmlEscape.Attribute(width)).Append('"');
        sb.Append(" background-color=\"").Append(MjmlEscape.Attribute(document.Styles.BackgroundColor)).Append("\">\n");

        foreach (var section in document.Sections)
            WriteSection(sb, section);

        sb.Append("</mj-body>\n");
    }

    private static void WriteSection(StringBuilder sb, EmailSection section)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("background-color", section.BackgroundColor)
            .Optional("background-url", section.BackgroundUrl)
            .Optional("background-position", section.BackgroundPosition)
            .Optional("background-repeat", section.BackgroundRepeat)
            .Optional("background-size", section.BackgroundSize)
            .Optional("border", section.Border)
            .Optional("border-radius", section.BorderRadius)
            .Defaulted("direction", section.Direction, "ltr")
            .Flag("full-width", section.FullWidth)
            .Defaulted("padding", section.Padding, "20px 0")
            .Defaulted("text-align", section.TextAlign, "center")
            .Common(section.CssClass, section.MjClasses, section.ExtraAttributes);

        sb.Append("<mj-section").Append(a).Append(">\n");
        foreach (var column in section.Columns)
            WriteColumn(sb, column);
        sb.Append("</mj-section>\n");
    }

    private static void WriteColumn(StringBuilder sb, EmailColumn column)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("width", column.Width)
            .Defaulted("vertical-align", column.VerticalAlign, "top")
            .Optional("background-color", column.BackgroundColor)
            .Optional("border", column.Border)
            .Optional("border-radius", column.BorderRadius)
            .Optional("padding", column.Padding)
            .Common(column.CssClass, column.MjClasses, column.ExtraAttributes);

        sb.Append("<mj-column").Append(a).Append(">\n");
        foreach (var block in column.Blocks)
            WriteBlock(sb, block);
        sb.Append("</mj-column>\n");
    }

    // ── Blocks ──────────────────────────────────────────────────────────────────────────────────

    private static void WriteBlock(StringBuilder sb, EmailBlockBase block)
    {
        var hasCondition = !string.IsNullOrEmpty(block.VisibleWhen);
        if (hasCondition) sb.Append("{{ if ").Append(block.VisibleWhen).Append(" }}\n");

        switch (block)
        {
            case EmailTextBlock t: WriteText(sb, t); break;
            case EmailButtonBlock b: WriteButton(sb, b); break;
            case EmailImageBlock i: WriteImage(sb, i); break;
            case EmailDividerBlock d: WriteDivider(sb, d); break;
            case EmailSpacerBlock s: WriteSpacer(sb, s); break;
            case EmailRawBlock r: WriteRaw(sb, r); break;
            case EmailTableBlock tb: WriteTable(sb, tb); break;
            case EmailSocialBlock so: WriteSocial(sb, so); break;
            case EmailNavbarBlock nv: WriteNavbar(sb, nv); break;
            case EmailCarouselBlock c: WriteCarousel(sb, c); break;
            case EmailAccordionBlock ac: WriteAccordion(sb, ac); break;
            case EmailHeroBlock h: WriteHero(sb, h); break;
            case EmailWrapperBlock w: WriteWrapper(sb, w); break;
            case EmailGroupBlock g: WriteGroup(sb, g); break;
        }

        if (hasCondition) sb.Append("{{ end }}\n");
    }

    private static void WriteText(StringBuilder sb, EmailTextBlock t)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("color", t.Color, "#000000")
            .Defaulted("font-family", t.FontFamily, "Ubuntu, Helvetica, Arial, sans-serif")
            .Defaulted("font-size", t.FontSize, "13px")
            .Optional("font-style", t.FontStyle)
            .Optional("font-weight", t.FontWeight)
            .Defaulted("line-height", t.LineHeight, "1")
            .Optional("letter-spacing", t.LetterSpacing)
            .Optional("height", t.Height)
            .Optional("text-decoration", t.TextDecoration)
            .Optional("text-transform", t.TextTransform)
            .Defaulted("align", t.Align, "left")
            .BlockCommon(t, "10px 25px");
        sb.Append("<mj-text").Append(a).Append('>').Append(HtmlContentSanitizer.SanitizeTemplateContent(t.Content)).Append("</mj-text>\n");
    }

    private static void WriteButton(StringBuilder sb, EmailButtonBlock b)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("href", b.Href)
            .Optional("rel", b.Rel)
            .Defaulted("target", b.Target, "_blank")
            .Defaulted("background-color", b.BackgroundColor, "#414141")
            .Defaulted("color", b.Color, "#ffffff")
            .Optional("font-family", b.FontFamily)
            .Defaulted("font-size", b.FontSize, "13px")
            .Optional("font-style", b.FontStyle)
            .Defaulted("font-weight", b.FontWeight, "normal")
            .Defaulted("line-height", b.LineHeight, "120%")
            .Optional("letter-spacing", b.LetterSpacing)
            .Defaulted("text-align", b.TextAlign, "center")
            .Defaulted("text-decoration", b.TextDecoration, "none")
            .Optional("text-transform", b.TextTransform)
            .Defaulted("align", b.Align, "center")
            .Defaulted("vertical-align", b.VerticalAlign, "middle")
            .Defaulted("border", b.Border, "none")
            .Defaulted("border-radius", b.BorderRadius, "3px")
            .Defaulted("inner-padding", b.InnerPadding, "10px 25px")
            .Optional("width", b.Width)
            .Optional("height", b.Height)
            .BlockCommon(b, "10px 25px");
        sb.Append("<mj-button").Append(a).Append('>').Append(MjmlEscape.Text(b.Text)).Append("</mj-button>\n");
    }

    private static void WriteImage(StringBuilder sb, EmailImageBlock i)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("src", i.Src)
            .Raw("alt", i.Alt)
            .Optional("href", i.Href)
            .Optional("rel", i.Rel)
            .Defaulted("target", i.Target, "_blank")
            .Optional("title", i.Title)
            .Defaulted("align", i.Align, "center")
            .Optional("width", i.Width)
            .Optional("height", i.Height)
            .Defaulted("border", i.Border, "0")
            .Optional("border-radius", i.BorderRadius)
            .Optional("fluid-on-mobile", i.FluidOnMobile)
            .BlockCommon(i, "10px 25px");
        sb.Append("<mj-image").Append(a).Append(" />\n");
    }

    private static void WriteDivider(StringBuilder sb, EmailDividerBlock d)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("border-color", d.BorderColor, "#000000")
            .Defaulted("border-style", d.BorderStyle, "solid")
            .Defaulted("border-width", d.BorderWidth, "4px")
            .Defaulted("width", d.Width, "100%")
            .Defaulted("align", d.Align, "center")
            .BlockCommon(d, "10px 25px");
        sb.Append("<mj-divider").Append(a).Append(" />\n");
    }

    private static void WriteSpacer(StringBuilder sb, EmailSpacerBlock s)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("height", s.Height, "20px")
            .BlockCommon(s, null);
        sb.Append("<mj-spacer").Append(a).Append(" />\n");
    }

    private static void WriteRaw(StringBuilder sb, EmailRawBlock r)
        => sb.Append("<mj-raw>").Append(r.Content).Append("</mj-raw>\n");

    private static void WriteTable(StringBuilder sb, EmailTableBlock t)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("align", t.Align, "left")
            .Optional("border", t.Border)
            .Defaulted("cellpadding", t.CellPadding, "0")
            .Defaulted("cellspacing", t.CellSpacing, "0")
            .Defaulted("color", t.Color, "#000000")
            .Defaulted("font-family", t.FontFamily, "Ubuntu, Helvetica, Arial, sans-serif")
            .Defaulted("font-size", t.FontSize, "13px")
            .Defaulted("line-height", t.LineHeight, "22px")
            .Defaulted("table-layout", t.TableLayout, "auto")
            .Defaulted("width", t.Width, "100%")
            .BlockCommon(t, "10px 25px");
        sb.Append("<mj-table").Append(a).Append(">\n");
        foreach (var row in t.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row.Cells)
            {
                var tag = row.IsHeader ? "th" : "td";
                sb.Append('<').Append(tag);
                if (!string.IsNullOrEmpty(cell.Align)) sb.Append(" align=\"").Append(MjmlEscape.Attribute(cell.Align)).Append('"');
                if (cell.ColSpan is > 1) sb.Append(" colspan=\"").Append(cell.ColSpan).Append('"');
                if (cell.RowSpan is > 1) sb.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
                sb.Append('>').Append(MjmlEscape.Text(cell.Text)).Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>\n");
        }
        sb.Append("</mj-table>\n");
    }

    private static void WriteSocial(StringBuilder sb, EmailSocialBlock s)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("mode", s.Mode, "horizontal")
            .Defaulted("align", s.Align, "center")
            .Defaulted("icon-size", s.IconSize, "20px")
            .Defaulted("border-radius", s.BorderRadius, "3px")
            .Defaulted("color", s.Color, "#333333")
            .Defaulted("font-size", s.FontSize, "13px")
            .Optional("font-family", s.FontFamily)
            .Defaulted("line-height", s.LineHeight, "22px")
            .Defaulted("text-padding", s.TextPadding, "4px 4px 4px 0")
            .Defaulted("text-decoration", s.TextDecoration, "none")
            .BlockCommon(s, "10px 25px");
        sb.Append("<mj-social").Append(a).Append(">\n");
        foreach (var e in s.Elements)
        {
            var ea = new MjmlAttributeBuffer()
                .Optional("name", e.Name)
                .Optional("href", e.Href)
                .Optional("src", e.Src)
                .Raw("alt", e.Alt)
                .Defaulted("target", e.Target, "_blank")
                .Optional("background-color", e.BackgroundColor);
            sb.Append("<mj-social-element").Append(ea).Append('>')
              .Append(MjmlEscape.Text(e.Label ?? string.Empty)).Append("</mj-social-element>\n");
        }
        sb.Append("</mj-social>\n");
    }

    private static void WriteNavbar(StringBuilder sb, EmailNavbarBlock n)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("align", n.Align, "center")
            .Optional("base-url", n.BaseUrl)
            .Optional("hamburger", n.Hamburger)
            .BlockCommon(n, null);
        sb.Append("<mj-navbar").Append(a).Append(">\n");
        foreach (var link in n.Links)
        {
            var la = new MjmlAttributeBuffer()
                .Optional("href", link.Href)
                .Optional("rel", link.Rel)
                .Defaulted("target", link.Target, "_blank")
                .Defaulted("color", link.Color, "#000000")
                .Optional("font-family", link.FontFamily)
                .Defaulted("font-size", link.FontSize, "13px")
                .Defaulted("font-weight", link.FontWeight, "normal")
                .Defaulted("line-height", link.LineHeight, "22px")
                .Defaulted("text-decoration", link.TextDecoration, "none")
                .Defaulted("text-transform", link.TextTransform, "uppercase")
                .Defaulted("padding", link.Padding, "15px 10px");
            sb.Append("<mj-navbar-link").Append(la).Append('>')
              .Append(MjmlEscape.Text(link.Text)).Append("</mj-navbar-link>\n");
        }
        sb.Append("</mj-navbar>\n");
    }

    private static void WriteCarousel(StringBuilder sb, EmailCarouselBlock c)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("align", c.Align, "center")
            .Defaulted("border-radius", c.BorderRadius, "6px")
            .Defaulted("icon-width", c.IconWidth, "44px")
            .Optional("left-icon", c.LeftIcon)
            .Optional("right-icon", c.RightIcon)
            .Defaulted("thumbnails", c.Thumbnails, "visible")
            .Defaulted("tb-border-radius", c.TbBorderRadius, "6px")
            .BlockCommon(c, null);
        sb.Append("<mj-carousel").Append(a).Append(">\n");
        foreach (var img in c.Images)
        {
            var ia = new MjmlAttributeBuffer()
                .Optional("src", img.Src)
                .Raw("alt", img.Alt)
                .Optional("href", img.Href)
                .Optional("rel", img.Rel)
                .Defaulted("target", img.Target, "_blank")
                .Optional("title", img.Title)
                .Optional("thumbnails-src", img.ThumbnailsSrc);
            sb.Append("<mj-carousel-image").Append(ia).Append(" />\n");
        }
        sb.Append("</mj-carousel>\n");
    }

    private static void WriteAccordion(StringBuilder sb, EmailAccordionBlock ac)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("border", ac.Border, "2px solid black")
            .Defaulted("icon-align", ac.IconAlign, "middle")
            .Defaulted("icon-position", ac.IconPosition, "right")
            .Defaulted("icon-height", ac.IconHeight, "32px")
            .Defaulted("icon-width", ac.IconWidth, "32px")
            .Optional("icon-wrapped-url", ac.IconWrappedUrl)
            .Optional("icon-unwrapped-url", ac.IconUnwrappedUrl)
            .Optional("font-family", ac.FontFamily)
            .BlockCommon(ac, null);
        sb.Append("<mj-accordion").Append(a).Append(">\n");
        foreach (var item in ac.Items)
        {
            var ia = new MjmlAttributeBuffer().Optional("background-color", item.BackgroundColor);
            sb.Append("<mj-accordion-element").Append(ia).Append(">\n");
            var ta = new MjmlAttributeBuffer().Optional("color", item.TitleColor);
            sb.Append("<mj-accordion-title").Append(ta).Append('>')
              .Append(MjmlEscape.Text(item.Title)).Append("</mj-accordion-title>\n");
            sb.Append("<mj-accordion-text>").Append(HtmlContentSanitizer.SanitizeTemplateContent(item.Content)).Append("</mj-accordion-text>\n");
            sb.Append("</mj-accordion-element>\n");
        }
        sb.Append("</mj-accordion>\n");
    }

    private static void WriteHero(StringBuilder sb, EmailHeroBlock h)
    {
        var a = new MjmlAttributeBuffer()
            .Defaulted("mode", h.Mode, "fluid-height")
            .Defaulted("height", h.Height, "0px")
            .Defaulted("background-color", h.BackgroundColor, "#ffffff")
            .Optional("background-url", h.BackgroundUrl)
            .Optional("background-width", h.BackgroundWidth)
            .Optional("background-height", h.BackgroundHeight)
            .Defaulted("background-position", h.BackgroundPosition, "center center")
            .Defaulted("vertical-align", h.VerticalAlign, "top")
            .BlockCommon(h, "0px");
        sb.Append("<mj-hero").Append(a).Append(">\n");
        foreach (var block in h.Blocks)
            WriteBlock(sb, block);
        sb.Append("</mj-hero>\n");
    }

    private static void WriteWrapper(StringBuilder sb, EmailWrapperBlock w)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("background-color", w.BackgroundColor)
            .Optional("background-url", w.BackgroundUrl)
            .Optional("border", w.Border)
            .Optional("border-radius", w.BorderRadius)
            .Defaulted("text-align", w.TextAlign, "center")
            .Flag("full-width", w.FullWidth)
            .BlockCommon(w, null);
        sb.Append("<mj-wrapper").Append(a).Append(">\n");
        foreach (var section in w.Sections)
            WriteSection(sb, section);
        sb.Append("</mj-wrapper>\n");
    }

    private static void WriteGroup(StringBuilder sb, EmailGroupBlock g)
    {
        var a = new MjmlAttributeBuffer()
            .Optional("background-color", g.BackgroundColor)
            .Defaulted("direction", g.Direction, "ltr")
            .Defaulted("vertical-align", g.VerticalAlign, "top")
            .Optional("width", g.Width)
            .BlockCommon(g, null);
        sb.Append("<mj-group").Append(a).Append(">\n");
        foreach (var column in g.Columns)
            WriteColumn(sb, column);
        sb.Append("</mj-group>\n");
    }
}
