using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Import;

/// <summary>
/// Imports MJML markup into an <see cref="EmailTemplateDocument"/>. Parsing is XML-based with an
/// <c>mj-raw</c> shim (raw content may be non-well-formed HTML), tolerates comments, preserves unknown
/// elements as raw blocks and unknown attributes in <see cref="EmailBlockBase.ExtraAttributes"/>, and
/// never throws — failures (including a breach of the <see cref="EmailImportOptions"/> limits) are
/// returned as <see cref="ImportMessage"/>s.
/// </summary>
public sealed partial class MjmlImporter
{
    private const string RawTokenPrefix = "MJRAWPLACEHOLDER_";
    private const string RawTokenSuffix = "_END";

    /// <summary>Maximum include nesting; a self- or mutually-referencing include stops here.</summary>
    private const int MaxIncludeDepth = 8;

    private readonly List<ImportMessage> _warnings = new();
    private readonly List<ImportMessage> _errors = new();
    private readonly HashSet<string> _includeStack = new(StringComparer.Ordinal);
    private List<string> _rawStore = new();
    private IMjmlIncludeResolver? _includes;
    private EmailImportOptions _limits = new();
    private int _includeDepth;
    private bool _depthExceeded;

    /// <summary>Imports the given MJML markup, optionally resolving <c>mj-include</c> references.</summary>
    /// <param name="mjml">The MJML markup to import.</param>
    /// <param name="includeResolver">Optional resolver for <c>mj-include</c> references.</param>
    public ImportResult Import(string mjml, IMjmlIncludeResolver? includeResolver = null)
        => Import(mjml, includeResolver, null);

    /// <summary>
    /// Imports the given MJML markup with explicit <paramref name="options"/> limits (input size in UTF-8
    /// bytes, sections, blocks, nesting depth). Exceeding a limit is a fatal
    /// <see cref="ImportResult.Errors"/> entry with a localization key — never an exception — and the
    /// document is not produced.
    /// </summary>
    /// <param name="mjml">The MJML markup to import.</param>
    /// <param name="includeResolver">Optional resolver for <c>mj-include</c> references.</param>
    /// <param name="options">The limits to apply; <see langword="null"/> means <see cref="EmailImportOptions"/> defaults.</param>
    public ImportResult Import(string mjml, IMjmlIncludeResolver? includeResolver, EmailImportOptions? options = null)
    {
        _warnings.Clear();
        _errors.Clear();
        _includeStack.Clear();
        _includeDepth = 0;
        _depthExceeded = false;
        _rawStore = new List<string>();
        _includes = includeResolver;
        var limits = options ?? new EmailImportOptions();
        _limits = limits;

        if (string.IsNullOrWhiteSpace(mjml))
            return Fail(new ImportMessage(ImportKeys.Empty));

        // The size limit is measured in UTF-8 bytes, not characters: a multi-byte payload must not slip
        // through a character-count check (the input is transported and compiled as UTF-8).
        var inputBytes = Encoding.UTF8.GetByteCount(mjml);
        if (inputBytes > limits.MaxInputBytes)
        {
            return Fail(new ImportMessage(
                ImportKeys.InputTooLarge,
                inputBytes.ToString(CultureInfo.InvariantCulture)));
        }

        XElement root;
        try
        {
            root = XDocument.Parse(ShimRaw(mjml), LoadOptions.PreserveWhitespace).Root!;
        }
        catch (XmlException ex)
        {
            return Fail(new ImportMessage(ImportKeys.ParseError, ex.Message, ex.LineNumber, ex.LinePosition));
        }

        if (!NameIs(root, "mjml"))
            return Fail(new ImportMessage(ImportKeys.NotMjml, root.Name.LocalName));

        // A hostile attribute tree must be refused before the mapping recursion touches it: 20 000 nested
        // mj-hero/mj-group elements used to overflow the stack inside MapBlock. The element depth of a
        // legal document is 4 (mjml/body/section/column) + 3 per model nesting level (a wrapper adds
        // wrapper+section+column) plus a little room for inline markup inside a text block.
        if (MaxElementDepth(root) > ElementDepthLimit(limits))
            return Fail(new ImportMessage(DocumentValidationKeys.TooDeeplyNested, "mjml"));

        var document = new EmailTemplateDocument();
        var lang = root.Attribute("lang")?.Value;
        if (!string.IsNullOrEmpty(lang)) document.Language = lang;

        var head = root.Elements().FirstOrDefault(e => NameIs(e, "mj-head"));
        if (head is not null) ImportHead(head, document);

        var body = root.Elements().FirstOrDefault(e => NameIs(e, "mj-body"));
        if (body is not null) ImportBody(body, document);

        // Limits are checked before any content finding: a document over the limits is refused outright,
        // so its validation warnings would only be noise.
        foreach (var finding in new Rendering.EmailDocumentLimitsValidator(limits).Validate(document))
            _errors.Add(new ImportMessage(finding.Key, finding.Path));

        if (_errors.Count > 0)
            return new ImportResult { Document = null, Warnings = _warnings.ToList(), Errors = _errors.ToList() };

        // Every block kind is audited for the values the pipeline cannot emit (a blocked link scheme, an
        // unparsable colour), so the warning list is consistent across blocks — a table cell has had its own
        // report since Fáze 9 and is not repeated here.
        _warnings.AddRange(ImportValueAudit.Findings(document));

        // EI.16: surface content/layout issues found in the imported document as warnings
        // (e.g. foreign templates with images missing alt text).
        foreach (var finding in new Rendering.EmailDocumentValidator().Validate(document))
            _warnings.Add(new ImportMessage(finding.Key, finding.Path));

        return new ImportResult { Document = document, Warnings = _warnings.ToList(), Errors = _errors.ToList() };
    }

    private ImportResult Fail(ImportMessage error)
    {
        _errors.Add(error);
        return new ImportResult { Document = null, Warnings = _warnings.ToList(), Errors = _errors.ToList() };
    }

    /// <summary>
    /// The deepest element nesting a legal document can have: the MJML envelope (4) and the most
    /// expensive container per model nesting level (wrapper → section → column = 3), plus room for the
    /// inline markup a text block may carry.
    /// </summary>
    private static int ElementDepthLimit(EmailImportOptions limits)
    {
        var derived = 8L + 3L * limits.MaxNestingDepth;
        return derived >= int.MaxValue ? int.MaxValue : (int)derived;
    }

    /// <summary>Measures the element depth iteratively (no recursion: the input is untrusted).</summary>
    private static int MaxElementDepth(XElement root)
    {
        var max = 0;
        var stack = new Stack<(XElement Element, int Depth)>();
        stack.Push((root, 1));
        while (stack.Count > 0)
        {
            var (element, depth) = stack.Pop();
            if (depth > max)
            {
                max = depth;
            }

            foreach (var child in element.Elements())
            {
                stack.Push((child, depth + 1));
            }
        }

        return max;
    }

    /// <summary>Records the nesting violation once; the import then fails without producing a document.</summary>
    private void RecordDepthExceeded()
    {
        if (_depthExceeded)
        {
            return;
        }

        _depthExceeded = true;
        _errors.Add(new ImportMessage(DocumentValidationKeys.TooDeeplyNested, "mjml"));
    }

    // ── Raw shim ──────────────────────────────────────────────────────────────────────────────

    private string ShimRaw(string mjml)
        => RawRegex.Replace(mjml, m =>
        {
            _rawStore.Add(m.Groups["content"].Value);
            return m.Groups["open"].Value + RawTokenPrefix + (_rawStore.Count - 1) + RawTokenSuffix + m.Groups["close"].Value;
        });

    private string RestoreRaw(string placeholderText)
    {
        var match = RawTokenRegex.Match(placeholderText);
        return match.Success && int.TryParse(match.Groups["index"].Value, null, out var i) && i < _rawStore.Count
            ? _rawStore[i]
            : placeholderText;
    }

    // ── Head ────────────────────────────────────────────────────────────────────────────────────

    private static void ImportHead(XElement head, EmailTemplateDocument document)
    {
        var styles = document.Styles;
        foreach (var e in head.Elements())
        {
            switch (e.Name.LocalName)
            {
                case "mj-title": document.Subject = e.Value; break;
                case "mj-preview": document.Preheader = e.Value; break;
                case "mj-breakpoint":
                    var w = e.Attribute("width")?.Value;
                    if (!string.IsNullOrEmpty(w)) styles.Breakpoint = w;
                    break;
                case "mj-font":
                    styles.Fonts.Add(new EmailFont
                    {
                        Name = e.Attribute("name")?.Value ?? string.Empty,
                        Href = e.Attribute("href")?.Value ?? string.Empty,
                    });
                    break;
                case "mj-style":
                    styles.Styles.Add(new EmailStyle
                    {
                        Css = e.Value,
                Inline = string.Equals(e.Attribute("inline")?.Value, "inline", StringComparison.Ordinal),
                    });
                    break;
                case "mj-attributes": ImportAttributes(e, styles.Attributes); break;
                case "mj-html-attributes": ImportHtmlAttributes(e, styles); break;
            }
        }
    }

    private static void ImportAttributes(XElement element, MjAttributes attributes)
    {
        foreach (var child in element.Elements())
        {
            var pairs = child.Attributes()
                .Where(a => !string.Equals(a.Name.LocalName, "name", StringComparison.Ordinal))
                .ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal);

            switch (child.Name.LocalName)
            {
                case "mj-all":
                    Merge(attributes.All, pairs);
                    break;
                case "mj-class":
                    var name = child.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(name)) attributes.Classes[name] = pairs;
                    break;
                default:
                    attributes.PerTag[child.Name.LocalName] = pairs;
                    break;
            }
        }
    }

    private static void ImportHtmlAttributes(XElement element, TemplateStyles styles)
    {
        foreach (var selectorEl in element.Elements().Where(e => NameIs(e, "mj-selector")))
        {
            var selector = new MjHtmlSelector { Path = selectorEl.Attribute("path")?.Value ?? string.Empty };
            foreach (var attrEl in selectorEl.Elements().Where(e => NameIs(e, "mj-html-attribute")))
            {
                var attrName = attrEl.Attribute("name")?.Value;
                if (!string.IsNullOrEmpty(attrName)) selector.Attributes[attrName] = attrEl.Value;
            }
            styles.HtmlAttributes.Add(selector);
        }
    }

    // ── Body / layout ─────────────────────────────────────────────────────────────────────────

    private void ImportBody(XElement body, EmailTemplateDocument document)
    {
        var width = body.Attribute("width")?.Value;
        if (!string.IsNullOrEmpty(width)) document.Styles.ContentWidth = width;
        var bg = body.Attribute("background-color")?.Value;
        if (!string.IsNullOrEmpty(bg)) document.Styles.BackgroundColor = bg;

        foreach (var e in body.Elements())
            ImportBodyChild(e, document, depth: 1);
    }

    private void ImportBodyChild(XElement e, EmailTemplateDocument document, int depth)
    {
        switch (e.Name.LocalName)
        {
            case "mj-section":
                document.Sections.Add(MapSection(e, depth));
                break;
            case "mj-wrapper":
                // Body-level wrapper: hoist its inner sections (the model has no body-level wrapper).
                _warnings.Add(new ImportMessage(ImportKeys.WrapperFlattened));
                if (depth > _limits.MaxNestingDepth)
                {
                    RecordDepthExceeded();
                    break;
                }

                foreach (var inner in e.Elements().Where(c => NameIs(c, "mj-section")))
                    document.Sections.Add(MapSection(inner, depth + 1));
                break;
            case "mj-include":
                ResolveInclude(e, resolved =>
                {
                    foreach (var child in resolved.Elements())
                        ImportBodyChild(child, document, depth);
                });
                break;
            default:
                // hero / raw / unknown at body level → wrap into a section+column to fit the model.
                _warnings.Add(new ImportMessage(ImportKeys.ElementWrapped, e.Name.LocalName));
                document.Sections.Add(WrapInSection(MapBlock(e, depth)));
                break;
        }
    }

    private EmailSection MapSection(XElement e, int depth)
    {
        var section = new EmailSection();
        var a = new AttrBag(e);
        section.BackgroundColor = a.Take("background-color");
        section.BackgroundUrl = a.Take("background-url");
        section.BackgroundPosition = a.Take("background-position");
        section.BackgroundRepeat = a.Take("background-repeat");
        section.BackgroundSize = a.Take("background-size");
        section.Border = a.Take("border");
        section.BorderRadius = a.Take("border-radius");
        section.Direction = a.Take("direction") ?? section.Direction;
        section.FullWidth = a.Take("full-width") is not null;
        section.Padding = a.Take("padding") ?? section.Padding;
        section.TextAlign = a.Take("text-align") ?? section.TextAlign;
        ApplyCommon("mj-section", a, v => section.CssClass = v, section.MjClasses, section.ExtraAttributes);

        foreach (var child in e.Elements())
        {
            if (NameIs(child, "mj-column"))
                section.Columns.Add(MapColumn(child, depth));
            else if (NameIs(child, "mj-group"))
            {
                _warnings.Add(new ImportMessage(ImportKeys.ElementWrapped, "mj-group"));
                var col = new EmailColumn();
                col.Blocks.Add(MapGroup(child, depth));
                section.Columns.Add(col);
            }
            else if (!IsComment(child))
            {
                _warnings.Add(new ImportMessage(ImportKeys.ElementWrapped, child.Name.LocalName));
                var col = new EmailColumn();
                col.Blocks.Add(MapBlock(child, depth));
                section.Columns.Add(col);
            }
        }
        return section;
    }

    private EmailColumn MapColumn(XElement e, int depth)
    {
        var col = new EmailColumn();
        var a = new AttrBag(e);
        col.Width = a.Take("width");
        col.VerticalAlign = a.Take("vertical-align") ?? col.VerticalAlign;
        col.BackgroundColor = a.Take("background-color");
        col.Border = a.Take("border");
        col.BorderRadius = a.Take("border-radius");
        col.Padding = a.Take("padding");
        ApplyCommon("mj-column", a, v => col.CssClass = v, col.MjClasses, col.ExtraAttributes);

        ImportBlocks(e, col.Blocks, depth);
        return col;
    }

    /// <summary>
    /// Imports the block children of a container, reattaching any <c>{{ if expr }}…{{ end }}</c>
    /// Scriban wrapper emitted around a block as that block's <see cref="EmailBlockBase.VisibleWhen"/>.
    /// </summary>
    private void ImportBlocks(XElement parent, IList<EmailBlockBase> target, int depth)
    {
        string? pendingCondition = null;
        foreach (var node in parent.Nodes())
        {
            if (node is XText text)
            {
                var match = IfRegex.Match(text.Value);
                if (match.Success) pendingCondition = match.Groups["condition"].Value.Trim();
                continue;
            }
            if (node is XElement element)
            {
                var block = MapBlock(element, depth);
                if (pendingCondition is not null)
                {
                    block.VisibleWhen = pendingCondition;
                    pendingCondition = null;
                }
                target.Add(block);
            }
        }
    }

    private static EmailSection WrapInSection(EmailBlockBase block)
    {
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(block);
        section.Columns.Add(col);
        return section;
    }

    // ── Blocks ──────────────────────────────────────────────────────────────────────────────────

    private EmailBlockBase MapBlock(XElement e, int depth)
    {
        // Second line of defence behind the element-depth guard: the documented model limit is applied
        // while mapping, so a mapping regression cannot walk deeper than the limit.
        if (depth > _limits.MaxNestingDepth)
        {
            RecordDepthExceeded();
            return new EmailTextBlock();
        }

        return e.Name.LocalName switch
        {
            "mj-text" => MapText(e),
            "mj-button" => MapButton(e),
            "mj-image" => MapImage(e),
            "mj-divider" => MapDivider(e),
            "mj-spacer" => MapSpacer(e),
            "mj-raw" => MapRaw(e),
            "mj-table" => MapTable(e),
            "mj-social" => MapSocial(e),
            "mj-navbar" => MapNavbar(e),
            "mj-carousel" => MapCarousel(e),
            "mj-accordion" => MapAccordion(e),
            "mj-hero" => MapHero(e, depth),
            "mj-wrapper" => MapWrapper(e, depth),
            "mj-group" => MapGroup(e, depth),
            _ => MapUnknown(e),
        };
    }

    private EmailBlockBase MapUnknown(XElement e)
    {
        _warnings.Add(new ImportMessage(ImportKeys.UnknownElement, e.Name.LocalName));
        return new EmailRawBlock { Content = e.ToString(SaveOptions.DisableFormatting) };
    }

    private EmailTextBlock MapText(XElement e)
    {
        var b = new EmailTextBlock { Content = InnerMarkup(e) };
        var a = new AttrBag(e);
        b.Color = a.Take("color") ?? b.Color;
        b.FontFamily = a.Take("font-family") ?? b.FontFamily;
        b.FontSize = a.Take("font-size") ?? b.FontSize;
        b.FontStyle = a.Take("font-style");
        b.FontWeight = a.Take("font-weight");
        b.LineHeight = a.Take("line-height") ?? b.LineHeight;
        b.LetterSpacing = a.Take("letter-spacing");
        b.Height = a.Take("height");
        b.TextDecoration = a.Take("text-decoration");
        b.TextTransform = a.Take("text-transform");
        b.Align = a.Take("align") ?? b.Align;
        ApplyBlockCommon(a, b);
        return b;
    }

    private EmailButtonBlock MapButton(XElement e)
    {
        var b = new EmailButtonBlock { Text = e.Value };
        var a = new AttrBag(e);
        b.Href = a.Take("href");
        b.Rel = a.Take("rel");
        b.Target = a.Take("target") ?? b.Target;
        b.BackgroundColor = a.Take("background-color") ?? b.BackgroundColor;
        b.Color = a.Take("color") ?? b.Color;
        b.FontFamily = a.Take("font-family");
        b.FontSize = a.Take("font-size") ?? b.FontSize;
        b.FontStyle = a.Take("font-style");
        b.FontWeight = a.Take("font-weight") ?? b.FontWeight;
        b.LineHeight = a.Take("line-height") ?? b.LineHeight;
        b.LetterSpacing = a.Take("letter-spacing");
        b.TextAlign = a.Take("text-align") ?? b.TextAlign;
        b.TextDecoration = a.Take("text-decoration") ?? b.TextDecoration;
        b.TextTransform = a.Take("text-transform");
        b.Align = a.Take("align") ?? b.Align;
        b.VerticalAlign = a.Take("vertical-align") ?? b.VerticalAlign;
        b.Border = a.Take("border") ?? b.Border;
        b.BorderRadius = a.Take("border-radius") ?? b.BorderRadius;
        b.InnerPadding = a.Take("inner-padding") ?? b.InnerPadding;
        b.Width = a.Take("width");
        b.Height = a.Take("height");
        ApplyBlockCommon(a, b);
        return b;
    }

    private EmailImageBlock MapImage(XElement e)
    {
        var b = new EmailImageBlock();
        var a = new AttrBag(e);
        b.Src = a.Take("src") ?? string.Empty;
        b.Alt = a.Take("alt") ?? string.Empty;
        b.Href = a.Take("href");
        b.Rel = a.Take("rel");
        b.Target = a.Take("target") ?? b.Target;
        b.Title = a.Take("title");
        b.Align = a.Take("align") ?? b.Align;
        b.Width = a.Take("width");
        b.Height = a.Take("height");
        b.Border = a.Take("border") ?? b.Border;
        b.BorderRadius = a.Take("border-radius");
        b.FluidOnMobile = a.Take("fluid-on-mobile");
        ApplyBlockCommon(a, b);
        return b;
    }

    private EmailDividerBlock MapDivider(XElement e)
    {
        var b = new EmailDividerBlock();
        var a = new AttrBag(e);
        b.BorderColor = a.Take("border-color") ?? b.BorderColor;
        b.BorderStyle = a.Take("border-style") ?? b.BorderStyle;
        b.BorderWidth = a.Take("border-width") ?? b.BorderWidth;
        b.Width = a.Take("width") ?? b.Width;
        b.Align = a.Take("align") ?? b.Align;
        ApplyBlockCommon(a, b);
        return b;
    }

    private EmailSpacerBlock MapSpacer(XElement e)
    {
        var b = new EmailSpacerBlock();
        var a = new AttrBag(e);
        b.Height = a.Take("height") ?? b.Height;
        ApplyBlockCommon(a, b);
        return b;
    }

    private EmailRawBlock MapRaw(XElement e)
        => new() { Content = RestoreRaw(e.Value) };

    private EmailTableBlock MapTable(XElement e)
    {
        var b = new EmailTableBlock();
        var a = new AttrBag(e);
        b.Align = a.Take("align") ?? b.Align;
        b.Border = a.Take("border");
        b.CellPadding = a.Take("cellpadding") ?? b.CellPadding;
        b.CellSpacing = a.Take("cellspacing") ?? b.CellSpacing;
        b.Color = a.Take("color") ?? b.Color;
        b.FontFamily = a.Take("font-family") ?? b.FontFamily;
        b.FontSize = a.Take("font-size") ?? b.FontSize;
        b.LineHeight = a.Take("line-height") ?? b.LineHeight;
        b.TableLayout = a.Take("table-layout") ?? b.TableLayout;
        b.Width = a.Take("width") ?? b.Width;
        ApplyBlockCommon(a, b);

        foreach (var tr in e.Elements().Where(x => NameIs(x, "tr")))
        {
            var cells = tr.Elements().Where(x => x.Name.LocalName is "td" or "th").ToList();
            var row = new EmailTableRow { IsHeader = cells.Count > 0 && cells.All(c => NameIs(c, "th")) };
            for (var cellIndex = 0; cellIndex < cells.Count; cellIndex++)
            {
                var cell = cells[cellIndex];
                int? colSpan = int.TryParse(cell.Attribute("colspan")?.Value, null, out var cs) ? cs : null;
                int? rowSpan = int.TryParse(cell.Attribute("rowspan")?.Value, null, out var rs) ? rs : null;
                // A cell's link is written as an <a href> around the WHOLE text (Fáze 9), so the anchor —
                // not an attribute of the td — is where the imported href lives. Only an anchor that is the
                // cell's single content node counts as the cell link: a mixed cell (`<a>Docs</a> since
                // 2024`, or an anchor next to markup) keeps its full text and no link, so the rest of the
                // cell is never silently dropped (review F9).
                var anchor = cell.Elements().FirstOrDefault(x => NameIs(x, "a"));
                var isSoleAnchor = anchor is not null && IsSoleContent(cell, anchor);
                var href = isSoleAnchor ? anchor!.Attribute("href")?.Value : null;
                var text = isSoleAnchor ? anchor!.Value : cell.Value;

                var background = cell.Attribute("bgcolor")?.Value;
                if (!string.IsNullOrWhiteSpace(background) && !EmailColorPolicy.IsAllowed(background))
                {
                    // The message can never carry this value (the generator only writes colours of the
                    // allow-list), so it is dropped at the door instead of riding along through every edit
                    // and being reported again on every render.
                    Warn(ImportKeys.InvalidTableColor, CellReference(tr, cellIndex));
                    background = null;
                }

                if (!string.IsNullOrEmpty(href) && !EmailUrlPolicy.IsAllowed(href))
                {
                    // The value is KEPT: the editor has to show the offending link so it can be fixed, and
                    // every emission path already fail-closes it (generator → '#', text version omits it,
                    // renderer audits it) — the same treatment a hand-authored model value gets.
                    Warn(ImportKeys.BlockedTableLink, CellReference(tr, cellIndex));
                }

                row.Cells.Add(new EmailTableCell
                {
                    Text = text,
                    Align = cell.Attribute("align")?.Value,
                    ColSpan = colSpan,
                    RowSpan = rowSpan,
                    Href = href,
                    BackgroundColor = background,
                });
            }
            b.Rows.Add(row);
        }
        return b;
    }

    /// <summary>
    /// Whether <paramref name="anchor"/> is the only content node of <paramref name="cell"/> and carries
    /// nothing but text itself — i.e. whether the cell's whole content is that one link.
    /// </summary>
    private static bool IsSoleContent(XElement cell, XElement anchor)
        => cell.Nodes().All(node => ReferenceEquals(node, anchor) || IsWhitespaceText(node))
           && anchor.Nodes().All(node => node is XText or XCData);

    private static bool IsWhitespaceText(XNode node)
        => node is XText text && string.IsNullOrWhiteSpace(text.Value);

    /// <summary>A stable, value-free reference to a cell for a diagnostic ("row 2, column 3").</summary>
    private static string CellReference(XElement row, int cellIndex)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"row {row.ElementsBeforeSelf().Count(x => NameIs(x, "tr")) + 1}, column {cellIndex + 1}");

    private void Warn(string key, string detail) => _warnings.Add(new ImportMessage(key, detail));

    private EmailSocialBlock MapSocial(XElement e)
    {
        var b = new EmailSocialBlock();
        var a = new AttrBag(e);
        b.Mode = a.Take("mode") ?? b.Mode;
        b.Align = a.Take("align") ?? b.Align;
        b.IconSize = a.Take("icon-size") ?? b.IconSize;
        b.BorderRadius = a.Take("border-radius") ?? b.BorderRadius;
        b.Color = a.Take("color") ?? b.Color;
        b.FontSize = a.Take("font-size") ?? b.FontSize;
        b.FontFamily = a.Take("font-family");
        b.LineHeight = a.Take("line-height") ?? b.LineHeight;
        b.TextPadding = a.Take("text-padding") ?? b.TextPadding;
        b.TextDecoration = a.Take("text-decoration") ?? b.TextDecoration;
        ApplyBlockCommon(a, b);

        foreach (var el in e.Elements().Where(x => NameIs(x, "mj-social-element")))
        {
            b.Elements.Add(new EmailSocialElement
            {
                Name = el.Attribute("name")?.Value,
                Href = el.Attribute("href")?.Value,
                Src = el.Attribute("src")?.Value,
                Alt = el.Attribute("alt")?.Value ?? string.Empty,
                Target = el.Attribute("target")?.Value ?? "_blank",
                BackgroundColor = el.Attribute("background-color")?.Value,
                Label = string.IsNullOrEmpty(el.Value) ? null : el.Value,
            });
        }
        return b;
    }

    private EmailNavbarBlock MapNavbar(XElement e)
    {
        var b = new EmailNavbarBlock();
        var a = new AttrBag(e);
        b.Align = a.Take("align") ?? b.Align;
        b.BaseUrl = a.Take("base-url");
        b.Hamburger = a.Take("hamburger");
        ApplyBlockCommon(a, b);

        foreach (var el in e.Elements().Where(x => NameIs(x, "mj-navbar-link")))
        {
            b.Links.Add(new EmailNavbarLink
            {
                Text = el.Value,
                Href = el.Attribute("href")?.Value,
                Rel = el.Attribute("rel")?.Value,
                Target = el.Attribute("target")?.Value ?? "_blank",
                Color = el.Attribute("color")?.Value ?? "#000000",
                FontFamily = el.Attribute("font-family")?.Value,
                FontSize = el.Attribute("font-size")?.Value ?? "13px",
                FontWeight = el.Attribute("font-weight")?.Value ?? "normal",
                LineHeight = el.Attribute("line-height")?.Value ?? "22px",
                TextDecoration = el.Attribute("text-decoration")?.Value ?? "none",
                TextTransform = el.Attribute("text-transform")?.Value ?? "uppercase",
                Padding = el.Attribute("padding")?.Value ?? "15px 10px",
            });
        }
        return b;
    }

    private EmailCarouselBlock MapCarousel(XElement e)
    {
        var b = new EmailCarouselBlock();
        var a = new AttrBag(e);
        b.Align = a.Take("align") ?? b.Align;
        b.BorderRadius = a.Take("border-radius") ?? b.BorderRadius;
        b.IconWidth = a.Take("icon-width") ?? b.IconWidth;
        b.LeftIcon = a.Take("left-icon");
        b.RightIcon = a.Take("right-icon");
        b.Thumbnails = a.Take("thumbnails") ?? b.Thumbnails;
        b.TbBorderRadius = a.Take("tb-border-radius") ?? b.TbBorderRadius;
        ApplyBlockCommon(a, b);

        foreach (var el in e.Elements().Where(x => NameIs(x, "mj-carousel-image")))
        {
            b.Images.Add(new EmailCarouselImage
            {
                Src = el.Attribute("src")?.Value ?? string.Empty,
                Alt = el.Attribute("alt")?.Value ?? string.Empty,
                Href = el.Attribute("href")?.Value,
                Rel = el.Attribute("rel")?.Value,
                Target = el.Attribute("target")?.Value ?? "_blank",
                Title = el.Attribute("title")?.Value,
                ThumbnailsSrc = el.Attribute("thumbnails-src")?.Value,
            });
        }
        return b;
    }

    private EmailAccordionBlock MapAccordion(XElement e)
    {
        var b = new EmailAccordionBlock();
        var a = new AttrBag(e);
        b.Border = a.Take("border") ?? b.Border;
        b.IconAlign = a.Take("icon-align") ?? b.IconAlign;
        b.IconPosition = a.Take("icon-position") ?? b.IconPosition;
        b.IconHeight = a.Take("icon-height") ?? b.IconHeight;
        b.IconWidth = a.Take("icon-width") ?? b.IconWidth;
        b.IconWrappedUrl = a.Take("icon-wrapped-url");
        b.IconUnwrappedUrl = a.Take("icon-unwrapped-url");
        b.FontFamily = a.Take("font-family");
        ApplyBlockCommon(a, b);

        foreach (var el in e.Elements().Where(x => NameIs(x, "mj-accordion-element")))
        {
            var title = el.Elements().FirstOrDefault(x => NameIs(x, "mj-accordion-title"));
            var text = el.Elements().FirstOrDefault(x => NameIs(x, "mj-accordion-text"));
            b.Items.Add(new EmailAccordionItem
            {
                Title = title?.Value ?? string.Empty,
                Content = text is null ? string.Empty : InnerMarkup(text),
                BackgroundColor = el.Attribute("background-color")?.Value,
                TitleColor = title?.Attribute("color")?.Value,
            });
        }
        return b;
    }

    private EmailHeroBlock MapHero(XElement e, int depth)
    {
        var b = new EmailHeroBlock();
        var a = new AttrBag(e);
        b.Mode = a.Take("mode") ?? b.Mode;
        b.Height = a.Take("height") ?? b.Height;
        b.BackgroundColor = a.Take("background-color") ?? b.BackgroundColor;
        b.BackgroundUrl = a.Take("background-url");
        b.BackgroundWidth = a.Take("background-width");
        b.BackgroundHeight = a.Take("background-height");
        b.BackgroundPosition = a.Take("background-position") ?? b.BackgroundPosition;
        b.VerticalAlign = a.Take("vertical-align") ?? b.VerticalAlign;
        ApplyBlockCommon(a, b);

        ImportBlocks(e, b.Blocks, depth + 1);
        return b;
    }

    private EmailWrapperBlock MapWrapper(XElement e, int depth)
    {
        var b = new EmailWrapperBlock();
        var a = new AttrBag(e);
        b.BackgroundColor = a.Take("background-color");
        b.BackgroundUrl = a.Take("background-url");
        b.Border = a.Take("border");
        b.BorderRadius = a.Take("border-radius");
        b.TextAlign = a.Take("text-align") ?? b.TextAlign;
        b.FullWidth = a.Take("full-width") is not null;
        ApplyBlockCommon(a, b);

        foreach (var child in e.Elements().Where(c => NameIs(c, "mj-section")))
            b.Sections.Add(MapSection(child, depth + 1));
        return b;
    }

    private EmailGroupBlock MapGroup(XElement e, int depth)
    {
        var b = new EmailGroupBlock();
        var a = new AttrBag(e);
        b.BackgroundColor = a.Take("background-color");
        b.Direction = a.Take("direction") ?? b.Direction;
        b.VerticalAlign = a.Take("vertical-align") ?? b.VerticalAlign;
        b.Width = a.Take("width");
        ApplyBlockCommon(a, b);

        foreach (var child in e.Elements().Where(c => NameIs(c, "mj-column")))
            b.Columns.Add(MapColumn(child, depth + 1));
        return b;
    }

    private void ResolveInclude(XElement e, Action<XElement> onResolved)
    {
        var path = e.Attribute("path")?.Value ?? e.Attribute("src")?.Value;
        if (path is null || _includeDepth >= MaxIncludeDepth || !_includeStack.Add(path))
        {
            // Unresolvable, too deeply nested or already being expanded (a self/mutually recursive
            // include): warn instead of looping.
            _warnings.Add(new ImportMessage(ImportKeys.IncludeUnresolved, path));
            return;
        }

        try
        {
            var content = _includes?.Resolve(path);
            if (content is null)
            {
                _warnings.Add(new ImportMessage(ImportKeys.IncludeUnresolved, path));
                return;
            }

            _includeDepth++;
            try
            {
                // Wrap the fragment so multiple top-level elements parse under a single root.
                var fragment = XElement.Parse("<mj-include-fragment>" + ShimRaw(content) + "</mj-include-fragment>",
                    LoadOptions.PreserveWhitespace);
                onResolved(fragment);
            }
            finally
            {
                _includeDepth--;
            }
        }
        catch (XmlException)
        {
            _warnings.Add(new ImportMessage(ImportKeys.IncludeUnresolved, path));
        }
        finally
        {
            _includeStack.Remove(path);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private void ApplyBlockCommon(AttrBag a, EmailBlockBase block)
    {
        block.Padding = a.Take("padding") ?? block.Padding;
        block.PaddingTop = a.Take("padding-top");
        block.PaddingRight = a.Take("padding-right");
        block.PaddingBottom = a.Take("padding-bottom");
        block.PaddingLeft = a.Take("padding-left");
        block.ContainerBackgroundColor = a.Take("container-background-color");
        ApplyCommon(MjmlTagOf(block), a, v => block.CssClass = v, block.MjClasses, block.ExtraAttributes);
    }

    /// <summary>
    /// Moves <c>css-class</c>, <c>mj-class</c> and the remaining attributes onto the model. A remaining attribute
    /// is re-emitted verbatim by the generator, so it is kept only when the pipeline may actually write it: a
    /// known attribute of THIS tag (<see cref="Rendering.MjmlKnownAttributes"/>) whose value is one the message
    /// can carry. Everything else is dropped here and WARNED about
    /// (<see cref="ImportKeys.DroppedAttribute"/>, carrying the tag and the attribute name, never the value) — the
    /// alternative is a model value that the export silently refuses on every save.
    /// </summary>
    private void ApplyCommon(string tag, AttrBag a, Action<string?> setCss, IList<string> mjClasses, IDictionary<string, string> extra)
    {
        setCss(a.Take("css-class"));
        var mj = a.Take("mj-class");
        if (!string.IsNullOrEmpty(mj))
            foreach (var mjClass in mj.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                mjClasses.Add(mjClass);
        foreach (var (name, value) in a.Rest())
        {
            if (!Rendering.MjmlKnownAttributes.IsAllowed(tag, name)
                || !Rendering.MjmlKnownAttributes.IsSafeValue(name, value)
                || (Rendering.MjmlKnownAttributes.IsUrl(name) && !Rendering.EmailUrlPolicy.IsAllowed(value)))
            {
                _warnings.Add(new ImportMessage(ImportKeys.DroppedAttribute, tag + " " + name));
                continue;
            }

            extra[name] = value;
        }
    }

    /// <summary>The MJML element name of a block, for the attribute allow-list and the import diagnostics.</summary>
    private static string MjmlTagOf(EmailBlockBase block) => block.Type switch
    {
        BlockType.Text => "mj-text",
        BlockType.Button => "mj-button",
        BlockType.Image => "mj-image",
        BlockType.Divider => "mj-divider",
        BlockType.Spacer => "mj-spacer",
        BlockType.Raw => "mj-raw",
        BlockType.Table => "mj-table",
        BlockType.Social => "mj-social",
        BlockType.Navbar => "mj-navbar",
        BlockType.Carousel => "mj-carousel",
        BlockType.Accordion => "mj-accordion",
        BlockType.Hero => "mj-hero",
        BlockType.Wrapper => "mj-wrapper",
        BlockType.Group => "mj-group",
        _ => "mj-text",
    };

    private static void Merge(IDictionary<string, string> target, IReadOnlyDictionary<string, string> source)
    {
        foreach (var (k, v) in source) target[k] = v;
    }

    private static bool NameIs(XElement e, string local) => string.Equals(e.Name.LocalName, local, StringComparison.Ordinal);

    private static bool IsComment(XElement e) => false; // comments are XComment nodes, never XElement

    private static string InnerMarkup(XElement e)
        => string.Concat(e.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting))).Trim();

    [GeneratedRegex(@"(?<open><mj-raw\b[^>]*>)(?<content>.*?)(?<close></mj-raw>)", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex RawRegex { get; }

    [GeneratedRegex(RawTokenPrefix + @"(?<index>\d+)" + RawTokenSuffix, RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex RawTokenRegex { get; }

    [GeneratedRegex(@"\{\{\s*if\s+(?<condition>.+?)\s*\}\}", RegexOptions.ExplicitCapture, 1000)]
    private static partial Regex IfRegex { get; }

    /// <summary>Mutable attribute lookup that tracks which attributes have been consumed.</summary>
    private sealed class AttrBag
    {
        private readonly Dictionary<string, string> _attrs;

        public AttrBag(XElement element)
            => _attrs = element.Attributes()
                .Where(a => !a.IsNamespaceDeclaration)
                .ToDictionary(a => a.Name.LocalName, a => a.Value, StringComparer.Ordinal);

        public string? Take(string name)
        {
            if (_attrs.TryGetValue(name, out var value)) { _attrs.Remove(name); return value; }
            return null;
        }

        public IEnumerable<KeyValuePair<string, string>> Rest() => _attrs;
    }
}
