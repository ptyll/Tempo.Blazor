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
    private readonly IHtmlContentSanitizer _sanitizer;

    /// <summary>
    /// Initializes the generator with the strict BCL sanitizer
    /// (<see cref="ConservativeHtmlContentSanitizer"/>), so <c>new MjmlGenerator()</c> stays safe
    /// without any DI container and the WASM export keeps working without the Engine package.
    /// </summary>
    public MjmlGenerator()
        : this(ConservativeHtmlContentSanitizer.Instance)
    {
    }

    /// <summary>
    /// Initializes the generator with an explicit HTML sanitizer. The render pipeline takes the
    /// registered <see cref="IHtmlContentSanitizer"/> from DI (the authoritative AngleSharp implementation
    /// when the Engine package is registered), so text-block content is sanitized by the same policy the
    /// rest of the render uses.
    /// </summary>
    public MjmlGenerator(IHtmlContentSanitizer sanitizer)
    {
        ArgumentNullException.ThrowIfNull(sanitizer);
        _sanitizer = sanitizer;
    }

    /// <summary>Generates MJML for the document using the given options (render-safe by default).</summary>
    public string Generate(EmailTemplateDocument document, MjmlGeneratorOptions? options = null)
        => Generate(document, options, null);

    /// <summary>
    /// Generates MJML and reports the values it had to drop, so a render can surface them as
    /// <see cref="RenderError"/>s. Only the checks the markup guard cannot see land here: a table cell's
    /// <c>bgcolor</c> is a model colour, not a URL in the markup, so the colour allow-list
    /// (<see cref="EmailColorPolicy"/>) has to report it here. Blocked links are silent on purpose — the
    /// value already becomes <c>#</c> in the markup and the renderer audits the model link itself (§3.7a).
    /// </summary>
    /// <param name="document">The document to generate.</param>
    /// <param name="options">Generation options, <see cref="MjmlGeneratorOptions.Default"/> when null.</param>
    /// <param name="diagnostics">Optional collector for the warnings of the generation.</param>
    public string Generate(
        EmailTemplateDocument document,
        MjmlGeneratorOptions? options,
        ICollection<RenderError>? diagnostics)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= MjmlGeneratorOptions.Default;
        var sb = new StringBuilder();
        var lang = string.IsNullOrEmpty(document.Language) ? null : document.Language;

        sb.Append("<mjml");
        if (lang is not null) sb.Append(" lang=\"").Append(MjmlEscape.Attribute(lang)).Append('"');
        sb.Append(">\n");

        WriteHead(sb, document, options, diagnostics);
        WriteBody(sb, document, diagnostics);

        sb.Append("</mjml>\n");
        return sb.ToString();
    }

    // ── Head ────────────────────────────────────────────────────────────────────────────────────

    private void WriteHead(
        StringBuilder sb,
        EmailTemplateDocument document,
        MjmlGeneratorOptions options,
        ICollection<RenderError>? diagnostics)
    {
        var styles = document.Styles;
        sb.Append("<mj-head>\n");

        if (!string.IsNullOrEmpty(document.Subject))
            sb.Append("<mj-title>").Append(MjmlEscape.Text(document.Subject)).Append("</mj-title>\n");
        if (!string.IsNullOrEmpty(document.Preheader))
            sb.Append("<mj-preview>").Append(MjmlEscape.Text(document.Preheader)).Append("</mj-preview>\n");

        // Every head element below is a place a foreign template (or a crafted JSON payload) can carry a tag
        // name, an attribute name and a value, so it goes through the SAME gate as the body: a known mj-* tag,
        // an allow-listed attribute name, a colour that is a colour and a URL through the allow-list.
        if (!string.IsNullOrEmpty(styles.Breakpoint) && !string.Equals(styles.Breakpoint, "480px", StringComparison.Ordinal))
        {
            var breakpoint = new MjmlAttributeBuffer("mj-breakpoint", diagnostics)
                .Optional("width", styles.Breakpoint);
            if (breakpoint.ToString().Length > 0)
            {
                sb.Append("<mj-breakpoint").Append(breakpoint).Append(" />\n");
            }
        }

        foreach (var font in styles.Fonts)
        {
            var fontAttributes = new MjmlAttributeBuffer("mj-font", diagnostics)
                .Optional("name", font.Name)
                .Url("href", font.Href);
            // A font is an MJML font only when BOTH attributes are there (Fáze 10 security re-review, item 6): a
            // name the attribute buffer had to drop counts as missing, so checking the href alone would emit
            // <mj-font href="…" /> — an element the compiler rejects.
            var fontMarkup = fontAttributes.ToString();
            if (fontMarkup.Contains(" name=", StringComparison.Ordinal) &&
                fontMarkup.Contains(" href=", StringComparison.Ordinal))
            {
                sb.Append("<mj-font").Append(fontAttributes).Append(" />\n");
            }
        }

        WriteAttributes(sb, styles, diagnostics);

        foreach (var style in styles.Styles)
        {
            sb.Append(style.Inline ? "<mj-style inline=\"inline\">" : "<mj-style>");
            // The CSS of a style block is written verbatim (it IS the payload), so what it may never carry is
            // the closing tag that would end the block early (a CSS payload has no '<') and the constructs a
            // browser would execute or fetch (expression(), behavior:, -moz-binding, @import, a refused URL).
            sb.Append(CssPayload(style.Css)).Append("</mj-style>\n");
        }

        if (options.EmitHtmlAttributes && styles.HtmlAttributes.Count > 0)
            WriteHtmlAttributes(sb, styles, diagnostics);

        sb.Append("</mj-head>\n");
    }

    private static void WriteAttributes(
        StringBuilder sb,
        TemplateStyles styles,
        ICollection<RenderError>? diagnostics)
    {
        // Apply the global font-family as an mj-all default unless the cascade already sets one.
        var all = new Dictionary<string, string>(styles.Attributes.All, StringComparer.Ordinal);
        if (!all.ContainsKey("font-family") && !string.IsNullOrEmpty(styles.FontFamily))
            all["font-family"] = styles.FontFamily;

        // A per-component default keyed by a name that is NOT a known MJML component is a tag-name injection
        // (`mj-text><script>…`): the element would be written with whatever the key holds, so the key itself has
        // to be a component this pipeline knows.
        var perTag = styles.Attributes.PerTag
            .Where(entry => MjmlKnownAttributes.IsKnownTag(entry.Key))
            .ToList();
        var classes = styles.Attributes.Classes;

        if (all.Count == 0 && perTag.Count == 0 && classes.Count == 0)
            return;

        sb.Append("<mj-attributes>\n");
        // NOTE (E0.9): mj-attributes children MUST use explicit closing tags; self-closing breaks Mjml.Net.
        if (all.Count > 0)
            sb.Append("<mj-all").Append(Pairs("mj-all", all, diagnostics)).Append("></mj-all>\n");
        foreach (var (tag, attrs) in perTag)
            sb.Append('<').Append(tag).Append(Pairs(tag, attrs, diagnostics)).Append("></").Append(tag).Append(">\n");
        foreach (var (name, attrs) in classes)
        {
            var safeName = MjmlKnownAttributes.SafeClassList(name);
            if (string.IsNullOrEmpty(safeName)) continue;

            sb.Append("<mj-class name=\"").Append(MjmlEscape.Attribute(safeName)).Append('"')
              .Append(Pairs("mj-class", attrs, diagnostics)).Append("></mj-class>\n");
        }

        sb.Append("</mj-attributes>\n");
    }

    private static void WriteHtmlAttributes(
        StringBuilder sb,
        TemplateStyles styles,
        ICollection<RenderError>? diagnostics)
    {
        sb.Append("<mj-html-attributes>\n");
        foreach (var selector in styles.HtmlAttributes)
        {
            sb.Append("<mj-selector path=\"").Append(MjmlEscape.Attribute(selector.Path)).Append("\">\n");
            foreach (var (name, value) in selector.Attributes)
            {
                if (!MjmlKnownAttributes.IsAllowedHtmlAttribute(name))
                {
                    continue;
                }

                if (MjmlKnownAttributes.IsUrl(name))
                {
                    sb.Append("<mj-html-attribute name=\"").Append(MjmlEscape.Attribute(name)).Append("\">")
                      .Append(MjmlEscape.Text(AllowedHref(value))).Append("</mj-html-attribute>\n");
                    continue;
                }

                if (!MjmlKnownAttributes.IsSafeValue(name, value))
                {
                    diagnostics?.Add(new RenderError(
                        $"The HTML attribute '{name}' was dropped because its value is not one the message can carry.",
                        Key: RenderKeys.InvalidAttributeValue,
                        Severity: RenderErrorSeverity.Warning));
                    continue;
                }

                sb.Append("<mj-html-attribute name=\"").Append(MjmlEscape.Attribute(name)).Append("\">")
                  .Append(MjmlEscape.Text(value)).Append("</mj-html-attribute>\n");
            }

            sb.Append("</mj-selector>\n");
        }

        sb.Append("</mj-html-attributes>\n");
    }

    private static string Pairs(string tag, Dictionary<string, string> attrs, ICollection<RenderError>? diagnostics)
    {
        var buffer = new MjmlAttributeBuffer(tag, diagnostics);
        foreach (var (name, value) in attrs)
        {
            buffer.Optional(name, value);
        }

        return buffer.ToString();
    }

    // ── Body / layout ─────────────────────────────────────────────────────────────────────────

    private void WriteBody(StringBuilder sb, EmailTemplateDocument document, ICollection<RenderError>? diagnostics)
    {
        var width = document.Styles.ContentWidth;
        sb.Append("<mj-body");
        if (!string.IsNullOrEmpty(width)) sb.Append(" width=\"").Append(MjmlEscape.Attribute(width)).Append('"');
        if (CssColourValue.IsPlausible(document.Styles.BackgroundColor))
            sb.Append(" background-color=\"").Append(MjmlEscape.Attribute(document.Styles.BackgroundColor.Trim())).Append("\">\n");
        else
            sb.Append(">\n");

        foreach (var section in document.Sections)
            WriteSection(sb, section, diagnostics);

        sb.Append("</mj-body>\n");
    }

    private void WriteSection(StringBuilder sb, EmailSection section, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-section", diagnostics)
            .Colour("background-color", section.BackgroundColor)
            .Url("background-url", section.BackgroundUrl)
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
            WriteColumn(sb, column, diagnostics);
        sb.Append("</mj-section>\n");
    }

    private void WriteColumn(StringBuilder sb, EmailColumn column, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-column", diagnostics)
            .Optional("width", column.Width)
            .Defaulted("vertical-align", column.VerticalAlign, "top")
            .Colour("background-color", column.BackgroundColor)
            .Optional("border", column.Border)
            .Optional("border-radius", column.BorderRadius)
            .Optional("padding", column.Padding)
            .Common(column.CssClass, column.MjClasses, column.ExtraAttributes);

        sb.Append("<mj-column").Append(a).Append(">\n");
        foreach (var block in column.Blocks)
            WriteBlock(sb, block, diagnostics);
        sb.Append("</mj-column>\n");
    }

    // ── Blocks ──────────────────────────────────────────────────────────────────────────────────

    private void WriteBlock(StringBuilder sb, EmailBlockBase block, ICollection<RenderError>? diagnostics)
    {
        var hasCondition = !string.IsNullOrEmpty(block.VisibleWhen);
        if (hasCondition) sb.Append("{{ if ").Append(block.VisibleWhen).Append(" }}\n");

        switch (block)
        {
            case EmailTextBlock t: WriteText(sb, t, diagnostics); break;
            case EmailButtonBlock b: WriteButton(sb, b, diagnostics); break;
            case EmailImageBlock i: WriteImage(sb, i, diagnostics); break;
            case EmailDividerBlock d: WriteDivider(sb, d, diagnostics); break;
            case EmailSpacerBlock s: WriteSpacer(sb, s, diagnostics); break;
            case EmailRawBlock r: WriteRaw(sb, r, diagnostics); break;
            case EmailTableBlock tb: WriteTable(sb, tb, diagnostics); break;
            case EmailSocialBlock so: WriteSocial(sb, so, diagnostics); break;
            case EmailNavbarBlock nv: WriteNavbar(sb, nv, diagnostics); break;
            case EmailCarouselBlock c: WriteCarousel(sb, c, diagnostics); break;
            case EmailAccordionBlock ac: WriteAccordion(sb, ac, diagnostics); break;
            case EmailHeroBlock h: WriteHero(sb, h, diagnostics); break;
            case EmailWrapperBlock w: WriteWrapper(sb, w, diagnostics); break;
            case EmailGroupBlock g: WriteGroup(sb, g, diagnostics); break;
        }

        if (hasCondition) sb.Append("{{ end }}\n");
    }

    private void WriteText(StringBuilder sb, EmailTextBlock t, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-text", diagnostics)
            .Colour("color", t.Color, "#000000")
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
        sb.Append("<mj-text").Append(a).Append('>').Append(_sanitizer.SanitizeTemplateContent(t.Content)).Append("</mj-text>\n");
    }

    private void WriteButton(StringBuilder sb, EmailButtonBlock b, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-button", diagnostics)
            .Url("href", b.Href)
            .Optional("rel", b.Rel)
            .Defaulted("target", b.Target, "_blank")
            .Colour("background-color", b.BackgroundColor, "#414141")
            .Colour("color", b.Color, "#ffffff")
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

    private void WriteImage(StringBuilder sb, EmailImageBlock i, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-image", diagnostics)
            .Url("src", i.Src)
            .Raw("alt", i.Alt)
            .Url("href", i.Href)
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

    private void WriteDivider(StringBuilder sb, EmailDividerBlock d, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-divider", diagnostics)
            .Colour("border-color", d.BorderColor, "#000000")
            .Defaulted("border-style", d.BorderStyle, "solid")
            .Defaulted("border-width", d.BorderWidth, "4px")
            .Defaulted("width", d.Width, "100%")
            .Defaulted("align", d.Align, "center")
            .BlockCommon(d, "10px 25px");
        sb.Append("<mj-divider").Append(a).Append(" />\n");
    }

    private void WriteSpacer(StringBuilder sb, EmailSpacerBlock s, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-spacer", diagnostics)
            .Defaulted("height", s.Height, "20px")
            .BlockCommon(s, null);
        sb.Append("<mj-spacer").Append(a).Append(" />\n");
    }

    private void WriteRaw(StringBuilder sb, EmailRawBlock r, ICollection<RenderError>? diagnostics)
        => sb.Append("<mj-raw>").Append(KeepInside(r.Content, "mj-raw")).Append("</mj-raw>\n");

    private void WriteTable(StringBuilder sb, EmailTableBlock t, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-table", diagnostics)
            .Defaulted("align", t.Align, "left")
            .Optional("border", t.Border)
            .Defaulted("cellpadding", t.CellPadding, "0")
            .Defaulted("cellspacing", t.CellSpacing, "0")
            .Colour("color", t.Color, "#000000")
            .Defaulted("font-family", t.FontFamily, "Ubuntu, Helvetica, Arial, sans-serif")
            .Defaulted("font-size", t.FontSize, "13px")
            .Defaulted("line-height", t.LineHeight, "22px")
            .Defaulted("table-layout", t.TableLayout, "auto")
            .Defaulted("width", t.Width, "100%")
            .BlockCommon(t, "10px 25px");
        sb.Append("<mj-table").Append(a).Append(">\n");
        for (var rowIndex = 0; rowIndex < t.Rows.Count; rowIndex++)
        {
            var row = t.Rows[rowIndex];
            sb.Append("<tr>");
            foreach (var cell in row.Cells)
            {
                var tag = row.IsHeader ? "th" : "td";
                sb.Append('<').Append(tag);
                if (!string.IsNullOrEmpty(cell.Align)) sb.Append(" align=\"").Append(MjmlEscape.Attribute(cell.Align)).Append('"');
                if (cell.ColSpan is > 1) sb.Append(" colspan=\"").Append(cell.ColSpan).Append('"');
                if (cell.RowSpan is > 1) sb.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
                AppendCellBackground(sb, cell, rowIndex, diagnostics);
                sb.Append('>');

                var text = MjmlEscape.Text(cell.Text);
                if (string.IsNullOrEmpty(cell.Href))
                {
                    sb.Append(text);
                }
                else
                {
                    // The link is the cell's whole content; a blocked scheme becomes '#' exactly like the
                    // markup guard does for every other link (§3.7a), so the export of an unrendered
                    // document cannot hand out a live javascript:/data: link either.
                    sb.Append("<a href=\"")
                      .Append(MjmlEscape.Attribute(AllowedHref(cell.Href)))
                      .Append("\">").Append(text).Append("</a>");
                }

                sb.Append("</").Append(tag).Append('>');
            }
            sb.Append("</tr>\n");
        }
        sb.Append("</mj-table>\n");
    }

    /// <summary>
    /// Writes the cell's <c>bgcolor</c> only when the value is a colour of the allow-list. An invalid value
    /// is dropped and reported — it is never "cleaned up" into something that could still reach the
    /// attribute (or the <c>style</c> attribute, which this generator never writes for a cell).
    /// </summary>
    private static void AppendCellBackground(
        StringBuilder sb,
        EmailTableCell cell,
        int rowIndex,
        ICollection<RenderError>? diagnostics)
    {
        if (string.IsNullOrWhiteSpace(cell.BackgroundColor)) return;
        if (!EmailColorPolicy.IsAllowed(cell.BackgroundColor))
        {
            diagnostics?.Add(new RenderError(
                $"The background colour of the table cell in row {rowIndex + 1} is not a #rgb/#rrggbb hex "
                + "or a named colour from the safe set, so no bgcolor was written. The value itself is not "
                + "echoed: it may be crafted markup.",
                Key: RenderKeys.InvalidTableCellColor,
                Severity: RenderErrorSeverity.Warning));
            return;
        }

        // The trimmed token is what was validated, so that is what is written.
        sb.Append(" bgcolor=\"").Append(MjmlEscape.Attribute(cell.BackgroundColor.Trim())).Append('"');
    }

    /// <summary>
    /// Keeps a verbatim payload inside its element: a closing tag of the OWN element inside the payload would end
    /// the block early and turn the rest of the payload into document markup, so its <c>&lt;/</c> is escaped.
    /// Nothing else is touched — raw HTML is a payload by definition (the markup guard and the e-mail client's
    /// own sandbox are what make it inert; the export is a text file that nothing executes).
    /// </summary>
    private static string KeepInside(string content, string elementName)
        => content.Replace("</" + elementName, "<\\/" + elementName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The CSS of one <c>mj-style</c> block, made unable to leave its element. CSS never contains <c>&lt;</c>, so
    /// dropping it is lossless for real stylesheets — and it closes the one injection this payload has: a style
    /// whose text smuggles <c>&lt;/style&gt;&lt;script&gt;</c> into the HTML the compiler produces (and into the
    /// exported MJML).
    /// </summary>
    private static string CssPayload(string css)
        => CssSafety.IsSafe(css) ? css.Replace("<", string.Empty, StringComparison.Ordinal) : string.Empty;

    /// <summary>The cell link as it may be emitted: the value itself, or <c>#</c> for a blocked scheme.</summary>
    private static string AllowedHref(string href)
        => EmailUrlPolicy.IsAllowed(href) ? href : EmailUrlPolicy.BlockedUrlPlaceholder;

    private void WriteSocial(StringBuilder sb, EmailSocialBlock s, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-social", diagnostics)
            .Defaulted("mode", s.Mode, "horizontal")
            .Defaulted("align", s.Align, "center")
            .Defaulted("icon-size", s.IconSize, "20px")
            .Defaulted("border-radius", s.BorderRadius, "3px")
            .Colour("color", s.Color, "#333333")
            .Defaulted("font-size", s.FontSize, "13px")
            .Optional("font-family", s.FontFamily)
            .Defaulted("line-height", s.LineHeight, "22px")
            .Defaulted("text-padding", s.TextPadding, "4px 4px 4px 0")
            .Defaulted("text-decoration", s.TextDecoration, "none")
            .BlockCommon(s, "10px 25px");
        sb.Append("<mj-social").Append(a).Append(">\n");
        foreach (var e in s.Elements)
        {
            var ea = new MjmlAttributeBuffer("mj-social-element", diagnostics)
                .Optional("name", e.Name)
                .Url("href", e.Href)
                .Url("src", e.Src)
                .Raw("alt", e.Alt)
                .Defaulted("target", e.Target, "_blank")
                .Colour("background-color", e.BackgroundColor);
            sb.Append("<mj-social-element").Append(ea).Append('>')
              .Append(MjmlEscape.Text(e.Label ?? string.Empty)).Append("</mj-social-element>\n");
        }
        sb.Append("</mj-social>\n");
    }

    private void WriteNavbar(StringBuilder sb, EmailNavbarBlock n, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-navbar", diagnostics)
            .Defaulted("align", n.Align, "center")
            .Optional("base-url", n.BaseUrl)
            .Optional("hamburger", n.Hamburger)
            .BlockCommon(n, null);
        sb.Append("<mj-navbar").Append(a).Append(">\n");
        foreach (var link in n.Links)
        {
            var la = new MjmlAttributeBuffer("mj-navbar-link", diagnostics)
                .Url("href", link.Href)
                .Optional("rel", link.Rel)
                .Defaulted("target", link.Target, "_blank")
                .Colour("color", link.Color, "#000000")
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

    private void WriteCarousel(StringBuilder sb, EmailCarouselBlock c, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-carousel", diagnostics)
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
            var ia = new MjmlAttributeBuffer("mj-carousel-image", diagnostics)
                .Url("src", img.Src)
                .Raw("alt", img.Alt)
                .Url("href", img.Href)
                .Optional("rel", img.Rel)
                .Defaulted("target", img.Target, "_blank")
                .Optional("title", img.Title)
                .Url("thumbnails-src", img.ThumbnailsSrc);
            sb.Append("<mj-carousel-image").Append(ia).Append(" />\n");
        }
        sb.Append("</mj-carousel>\n");
    }

    private void WriteAccordion(StringBuilder sb, EmailAccordionBlock ac, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-accordion", diagnostics)
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
            var ia = new MjmlAttributeBuffer("mj-accordion-element", diagnostics).Colour("background-color", item.BackgroundColor);
            sb.Append("<mj-accordion-element").Append(ia).Append(">\n");
            var ta = new MjmlAttributeBuffer("mj-accordion-title", diagnostics).Colour("color", item.TitleColor);
            sb.Append("<mj-accordion-title").Append(ta).Append('>')
              .Append(MjmlEscape.Text(item.Title)).Append("</mj-accordion-title>\n");
            sb.Append("<mj-accordion-text>").Append(_sanitizer.SanitizeTemplateContent(item.Content)).Append("</mj-accordion-text>\n");
            sb.Append("</mj-accordion-element>\n");
        }
        sb.Append("</mj-accordion>\n");
    }

    private void WriteHero(StringBuilder sb, EmailHeroBlock h, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-hero", diagnostics)
            .Defaulted("mode", h.Mode, "fluid-height")
            .Defaulted("height", h.Height, "0px")
            .Colour("background-color", h.BackgroundColor, "#ffffff")
            .Url("background-url", h.BackgroundUrl)
            .Optional("background-width", h.BackgroundWidth)
            .Optional("background-height", h.BackgroundHeight)
            .Defaulted("background-position", h.BackgroundPosition, "center center")
            .Defaulted("vertical-align", h.VerticalAlign, "top")
            .BlockCommon(h, "0px");
        sb.Append("<mj-hero").Append(a).Append(">\n");
        foreach (var block in h.Blocks)
            WriteBlock(sb, block, diagnostics);
        sb.Append("</mj-hero>\n");
    }

    private void WriteWrapper(StringBuilder sb, EmailWrapperBlock w, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-wrapper", diagnostics)
            .Colour("background-color", w.BackgroundColor)
            .Url("background-url", w.BackgroundUrl)
            .Optional("border", w.Border)
            .Optional("border-radius", w.BorderRadius)
            .Defaulted("text-align", w.TextAlign, "center")
            .Flag("full-width", w.FullWidth)
            .BlockCommon(w, null);
        sb.Append("<mj-wrapper").Append(a).Append(">\n");
        foreach (var section in w.Sections)
            WriteSection(sb, section, diagnostics);
        sb.Append("</mj-wrapper>\n");
    }

    private void WriteGroup(StringBuilder sb, EmailGroupBlock g, ICollection<RenderError>? diagnostics)
    {
        var a = new MjmlAttributeBuffer("mj-group", diagnostics)
            .Colour("background-color", g.BackgroundColor)
            .Defaulted("direction", g.Direction, "ltr")
            .Defaulted("vertical-align", g.VerticalAlign, "top")
            .Optional("width", g.Width)
            .BlockCommon(g, null);
        sb.Append("<mj-group").Append(a).Append(">\n");
        foreach (var column in g.Columns)
            WriteColumn(sb, column, diagnostics);
        sb.Append("</mj-group>\n");
    }
}
