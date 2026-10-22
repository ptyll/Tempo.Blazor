using Microsoft.AspNetCore.Components;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// Fáze 9A — the WYSIWYG renderer of one email block on the editor canvas: an approximation of the MJML
/// output built from the model values (the authoritative output stays the server preview).
/// <para>
/// <b>Performance.</b> The canvas re-renders on every editor commit, so this component decides on its own
/// whether its subtree changed: <see cref="ShouldRender"/> compares the block's rendered-data signature
/// (<see cref="EmailBlockSignature"/>), the image-loading mode, the nested flag and the UI culture. A
/// property change on one block therefore re-renders one renderer, not the document.
/// </para>
/// <para>
/// <b>Security.</b> The renderer paints model data that can come from an MJML import or a JSON payload:
/// text goes through <see cref="IHtmlContentSanitizer"/> and loses every attribute, an <c>mj-raw</c> block
/// is never rendered as markup, template tokens are chips, every URL passes
/// <see cref="EmailUrlPolicy"/>, and the canvas builds no anchor, no <c>url(…)</c> and no unvalidated
/// <c>style</c> value.
/// </para>
/// </summary>
public partial class TmEmailCanvasBlock
{
    /// <summary>The block to draw.</summary>
    [Parameter, EditorRequired] public EmailBlockBase Block { get; set; } = null!;

    /// <summary>
    /// Whether the remote images of the block may be requested by the author's browser. Defaults to
    /// <see cref="Components.CanvasImageLoading.Load"/>; see <see cref="CanvasImageLoading"/>.
    /// </summary>
    [Parameter] public CanvasImageLoading CanvasImageLoading { get; set; } = CanvasImageLoading.Load;

    /// <summary>
    /// Whether the block is drawn INSIDE another block's frame (a wrapper, a hero, a group) instead of as a
    /// canvas item. Nested blocks are content: they carry no canvas selector and no drag/selection target.
    /// </summary>
    [Parameter] public bool Nested { get; set; }

    [Inject] private IServiceProvider Services { get; set; } = null!;

    private readonly EmailCanvasRenderGate _gate = new();

    /// <summary>
    /// How many times this renderer was ALLOWED to paint its subtree, the initial render included (the gate is
    /// primed in <see cref="OnInitialized"/>) — the measurement the Fáze 9A performance test reads off the
    /// public component tree, so parallel tests can never collide on a static counter.
    /// </summary>
    internal long RenderCount => _gate.Renders;

    /// <summary>The block kind as a CSS modifier and a test hook (<c>text</c>, <c>button</c>, …).</summary>
    internal string Kind => Block.Type.ToString().ToLowerInvariant();

    /// <summary>The root class list: the block kind plus the nested marker.</summary>
    private string BlockClass => Nested
        ? "tm-email-block tm-email-block--" + Kind + " tm-email-block--nested"
        : "tm-email-block tm-email-block--" + Kind;

    /// <inheritdoc />
    protected override bool ShouldRender() => ShouldRenderNow();

    /// <summary>
    /// Primes the gate with the values of the render that is ABOUT to happen (review finding 5).
    /// <c>ShouldRender</c> is not consulted for the first render, so an un-primed gate was still empty when the
    /// parent re-rendered next — it answered "everything changed" and rebuilt EVERY block on the canvas once
    /// (the first keystroke repainted the whole document). Priming also makes <see cref="RenderCount"/> honest:
    /// one initial render, one count.
    /// </summary>
    protected override void OnInitialized()
        => _gate.ShouldRender(Block, CanvasImageLoading, Nested, CultureInfo.CurrentUICulture);

    /// <summary>
    /// The re-render decision itself, exposed to the phase's tests: <see langword="true"/> when the block's
    /// painted values (or the image-loading mode / nested flag / UI culture) changed since the last render.
    /// <c>ShouldRender</c> only forwards to it, so a test can prove the decision without depending on the
    /// test renderer's treatment of <c>ShouldRender</c>.
    /// </summary>
    internal bool ShouldRenderNow()
        => _gate.ShouldRender(Block, CanvasImageLoading, Nested, CultureInfo.CurrentUICulture);

    // ── The frame (container) of every block ───────────────────────────────────────────────

    private Dictionary<string, object> BodyAttributes
    {
        get
        {
            var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["data-tm-block-kind"] = Kind,
            };
            if (Nested)
            {
                attributes["data-tm-nested-block"] = Block.Id.ToString("D");
            }
            else
            {
                attributes["data-tm-block-body"] = Block.Id.ToString("D");
            }

            return attributes;
        }
    }

    /// <summary>
    /// The declarations of the block's own box: the container background, the effective padding and the
    /// type-specific frame properties. MJML paints a block inside a container cell, so both live here.
    /// </summary>
    private string FrameStyle
    {
        get
        {
            var declarations = new List<(string Name, string? Value)>
            {
                ("background-color", EmailCanvasValue.Color(Block.ContainerBackgroundColor)),
            };

            declarations.AddRange(SplitDeclarations(EmailCanvasValue.PaddingStyle(
                Block.Padding, Block.PaddingTop, Block.PaddingRight, Block.PaddingBottom, Block.PaddingLeft)));

            switch (Block)
            {
                case EmailTextBlock text:
                    declarations.Add(("color", EmailCanvasValue.Color(text.Color)));
                    declarations.Add(("font-family", EmailCanvasValue.FontFamily(text.FontFamily)));
                    declarations.Add(("font-size", EmailCanvasValue.Length(text.FontSize)));
                    declarations.Add(("font-style", EmailCanvasValue.FontStyle(text.FontStyle)));
                    declarations.Add(("font-weight", EmailCanvasValue.FontWeight(text.FontWeight)));
                    declarations.Add(("line-height", EmailCanvasValue.LineHeight(text.LineHeight)));
                    declarations.Add(("letter-spacing", EmailCanvasValue.Length(text.LetterSpacing)));
                    declarations.Add(("text-align", EmailCanvasValue.Align(text.Align)));
                    declarations.Add(("text-decoration", EmailCanvasValue.TextDecoration(text.TextDecoration)));
                    declarations.Add(("text-transform", EmailCanvasValue.TextTransform(text.TextTransform)));
                    break;
                case EmailButtonBlock button:
                    declarations.Add(("text-align", EmailCanvasValue.Align(button.Align)));
                    break;
                case EmailImageBlock image:
                    declarations.Add(("text-align", EmailCanvasValue.Align(image.Align)));
                    break;
                case EmailDividerBlock divider:
                    declarations.Add(("text-align", EmailCanvasValue.Align(divider.Align)));
                    break;
                case EmailSocialBlock social:
                    declarations.Add(("text-align", EmailCanvasValue.Align(social.Align)));
                    break;
                case EmailNavbarBlock navbar:
                    declarations.Add(("text-align", EmailCanvasValue.Align(navbar.Align)));
                    break;
                case EmailHeroBlock hero:
                    declarations.Add(("background-color", EmailCanvasValue.Color(hero.BackgroundColor)));
                    declarations.Add(("text-align", "center"));
                    declarations.Add(("min-block-size",
                        hero.Mode == "fixed-height" ? EmailCanvasValue.Length(hero.Height) : null));
                    break;
                case EmailWrapperBlock wrapper:
                    declarations.Add(("background-color", EmailCanvasValue.Color(wrapper.BackgroundColor)));
                    declarations.Add(("text-align", EmailCanvasValue.Align(wrapper.TextAlign)));
                    declarations.Add(("border-radius", EmailCanvasValue.Length(wrapper.BorderRadius)));
                    break;
                case EmailGroupBlock group:
                    declarations.Add(("background-color", EmailCanvasValue.Color(group.BackgroundColor)));
                    break;
            }

            return EmailCanvasValue.Style(declarations.ToArray());
        }
    }

    // ── Type-specific pieces ───────────────────────────────────────────────────────────────

    private string TextMarkup(string? content) => EmailCanvasMarkup.ToMarkup(content, ResolveSanitizer());

    private IHtmlContentSanitizer? ResolveSanitizer()
        => Services.GetService(typeof(IHtmlContentSanitizer)) as IHtmlContentSanitizer;

    private string ButtonStyle(EmailButtonBlock button) => EmailCanvasValue.Style(
        ("background-color", EmailCanvasValue.Color(button.BackgroundColor)),
        ("color", EmailCanvasValue.Color(button.Color)),
        ("font-family", EmailCanvasValue.FontFamily(button.FontFamily)),
        ("font-size", EmailCanvasValue.Length(button.FontSize)),
        ("font-style", EmailCanvasValue.FontStyle(button.FontStyle)),
        ("font-weight", EmailCanvasValue.FontWeight(button.FontWeight)),
        ("line-height", EmailCanvasValue.LineHeight(button.LineHeight)),
        ("letter-spacing", EmailCanvasValue.Length(button.LetterSpacing)),
        ("text-decoration", EmailCanvasValue.TextDecoration(button.TextDecoration)),
        ("text-transform", EmailCanvasValue.TextTransform(button.TextTransform)),
        ("border", EmailCanvasValue.Border(button.Border)),
        ("border-radius", EmailCanvasValue.Length(button.BorderRadius)),
        ("padding", EmailCanvasValue.Padding(button.InnerPadding)));

    private static string ImageStyle(EmailImageBlock image) => EmailCanvasValue.Style(
        ("border", EmailCanvasValue.Border(image.Border)),
        ("border-radius", EmailCanvasValue.Length(image.BorderRadius)),
        ("inline-size", EmailCanvasValue.Length(image.Width)),
        ("block-size", EmailCanvasValue.Length(image.Height)),
        ("max-inline-size", "100%"));

    private static string DividerStyle(EmailDividerBlock divider) => EmailCanvasValue.Style(
        ("border", "0"),
        ("border-top", JoinBorder(divider.BorderWidth, divider.BorderStyle, divider.BorderColor)),
        ("inline-size", EmailCanvasValue.Length(divider.Width)),
        ("margin", "0"));

    private static string? JoinBorder(string? width, string? style, string? color)
    {
        var parts = new[]
        {
            EmailCanvasValue.Length(width),
            EmailCanvasValue.BorderStyle(style),
            EmailCanvasValue.Color(color),
        }.Where(part => part is not null).ToArray();
        return parts.Length == 3 ? EmailCanvasValue.Border(string.Join(' ', parts)) : null;
    }

    private static string TableStyle(EmailTableBlock table) => EmailCanvasValue.Style(
        ("color", EmailCanvasValue.Color(table.Color)),
        ("font-family", EmailCanvasValue.FontFamily(table.FontFamily)),
        ("font-size", EmailCanvasValue.Length(table.FontSize)),
        ("line-height", EmailCanvasValue.LineHeight(table.LineHeight)),
        ("inline-size", EmailCanvasValue.Length(table.Width)),
        ("border-collapse", "collapse"));

    private static string NavLinkStyle(EmailNavbarLink link) => EmailCanvasValue.Style(
        ("color", EmailCanvasValue.Color(link.Color)),
        ("font-family", EmailCanvasValue.FontFamily(link.FontFamily)),
        ("font-size", EmailCanvasValue.Length(link.FontSize)),
        ("font-weight", EmailCanvasValue.FontWeight(link.FontWeight)),
        ("line-height", EmailCanvasValue.LineHeight(link.LineHeight)),
        ("text-decoration", EmailCanvasValue.TextDecoration(link.TextDecoration)),
        ("text-transform", EmailCanvasValue.TextTransform(link.TextTransform)),
        ("padding", EmailCanvasValue.Padding(link.Padding)));

    /// <summary>The nested section of a wrapper: the same properties the canvas paints on a top-level one.</summary>
    private static string SectionStyle(EmailSection section) => EmailCanvasValue.Style(
        ("background-color", EmailCanvasValue.Color(section.BackgroundColor)),
        ("border", EmailCanvasValue.Border(section.Border)),
        ("border-radius", EmailCanvasValue.Length(section.BorderRadius)),
        ("text-align", EmailCanvasValue.Align(section.TextAlign)),
        ("padding", EmailCanvasValue.Padding(section.Padding)));

    /// <summary>The nested column of a group/wrapper: width ratio plus its own box.</summary>
    internal static string ColumnStyle(EmailColumn column) => EmailCanvasValue.Style(
        ("flex", ColumnFlex(column)),
        ("min-inline-size", "0"),
        ("background-color", EmailCanvasValue.Color(column.BackgroundColor)),
        ("border", EmailCanvasValue.Border(column.Border)),
        ("border-radius", EmailCanvasValue.Length(column.BorderRadius)),
        ("vertical-align", EmailCanvasValue.VerticalAlign(column.VerticalAlign)),
        ("padding", EmailCanvasValue.Padding(column.Padding)));

    /// <summary>
    /// The flex ratio of a column, derived from its declared width (<c>50%</c> → <c>50 1 0</c>) so a row
    /// always fits the paper: a fixed basis could not shrink and adding columns pushed the row out.
    /// </summary>
    internal static string ColumnFlex(EmailColumn column)
    {
        var grow = ColumnGrow(column.Width);
        return EmailCanvasValue.Invariant(grow) + " 1 0";
    }

    /// <summary>The numeric weight of a column width (<c>33.333%</c> → 33.333), 1 when unknown.</summary>
    internal static double ColumnGrow(string? width)
    {
        if (string.IsNullOrWhiteSpace(width)) return 1;
        var digits = new string(width.TakeWhile(c => char.IsDigit(c) || c is '.' or ',').ToArray())
            .Replace(',', '.');
        return double.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1;
    }

    private static string CarouselCount(EmailCarouselBlock carousel)
        => "1 / " + carousel.Images.Count.ToString(CultureInfo.InvariantCulture);

    private static string Initial(EmailSocialElement element)
    {
        var name = element.Label ?? element.Name ?? string.Empty;
        if (name.Length == 0) return "•";
        // A rune, not a char: a label that starts with an emoji must not be split into a lone surrogate.
        var first = name.EnumerateRunes().First();
        return first.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Whether the text block has nothing to paint (no text once its tags are gone). A block that renders
    /// nothing collapses to zero height, so the canvas would show a hole instead of a block the author can
    /// select and fix; <see cref="RenderEmpty"/> marks it instead.
    /// </summary>
    private static bool IsEmptyText(EmailTextBlock text)
        => string.IsNullOrWhiteSpace(
            System.Net.WebUtility.HtmlDecode(NoTags().Replace(text.Content ?? string.Empty, string.Empty)));

    /// <summary>
    /// The "this block is still empty" marker: a dashed, italic line naming what to do (review finding 10).
    /// Without it an empty text/button/list block was invisible on the paper, so the author could not tell a
    /// selected empty block from a rendering bug.
    /// </summary>
    private RenderFragment RenderEmpty() => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "tm-email-block__empty");
        builder.AddAttribute(2, "data-tm-block-empty", "true");
        builder.AddContent(3, Loc["EmailEditor_Canvas_EmptyBlock"]);
        builder.CloseElement();
    };

    /// <summary>Any HTML tag, removed to answer "is there text in here".</summary>
    [GeneratedRegex("<[^>]*>", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NoTags();

    // ── Images ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Draws a model image or, when the URL is refused, is not a loadable image, or the canvas is in
    /// <see cref="CanvasImageLoading.Placeholder"/> mode, a framed placeholder with the alt text and the
    /// reason. A refused URL never becomes a request and never reaches the DOM.
    /// </summary>
    private RenderFragment RenderImage(
        string? src, string? alt, string? width, string style, string cssClass, string? placeholderModifier = null)
        => builder =>
        {
            var url = CanvasImageLoading == CanvasImageLoading.Load ? EmailCanvasValue.ImageUrl(src) : null;
            var text = alt ?? string.Empty;
            if (url is not null)
            {
                builder.OpenElement(0, "img");
                builder.AddAttribute(1, "class", cssClass);
                builder.AddAttribute(2, "src", url);
                builder.AddAttribute(3, "alt", text);
                if (text.Length == 0)
                {
                    builder.AddAttribute(4, "aria-hidden", "true");
                }

                if (EmailCanvasValue.PixelNumber(width) is { } pixels)
                {
                    builder.AddAttribute(5, "width", pixels);
                }

                if (style.Length > 0)
                {
                    builder.AddAttribute(6, "style", style);
                }

                // The canvas is a page the AUTHOR is editing: an image host must not learn the editor URL,
                // and an off-screen image must not be fetched until the author scrolls to it.
                builder.AddAttribute(7, "referrerpolicy", "no-referrer");
                builder.AddAttribute(8, "loading", "lazy");

                builder.CloseElement();
                return;
            }

            // Three reasons, three texts. "Blocked" is a REFUSED scheme (this is what the allow-list refused,
            // and it is the one the author must act on); "not loadable" is an allow-listed value that simply
            // cannot be an image (a relative route, mailto:, tel:, an unresolved token); the rest is the
            // privacy placeholder of CanvasImageLoading.Placeholder, which issued no request on purpose.
            var hasSource = !string.IsNullOrWhiteSpace(src);
            var loading = CanvasImageLoading == CanvasImageLoading.Load;
            var blocked = loading && hasSource && EmailUrlPolicy.FindBlockedScheme(src) is not null;
            var reasonKey = blocked
                ? "EmailEditor_Canvas_ImageBlocked"
                : loading && hasSource
                    ? "EmailEditor_Canvas_ImageNotLoadable"
                    : "EmailEditor_Canvas_ImagePlaceholder";

            builder.OpenElement(10, "span");
            builder.AddAttribute(11, "class",
                "tm-email-block__image-placeholder" + placeholderModifier);
            builder.AddAttribute(12, "data-tm-image-placeholder", "true");
            if (text.Length == 0)
            {
                builder.AddAttribute(13, "aria-hidden", "true");
            }
            else
            {
                builder.AddAttribute(13, "role", "img");
                builder.AddAttribute(14, "aria-label", text);
            }

            if (blocked)
            {
                builder.AddAttribute(15, "data-tm-image-blocked", "true");
            }

            if (style.Length > 0)
            {
                builder.AddAttribute(16, "style", style);
            }

            builder.OpenElement(17, "span");
            builder.AddAttribute(18, "class", "tm-email-block__image-placeholder-alt");
            builder.AddContent(19, text);
            builder.CloseElement();
            builder.OpenElement(20, "span");
            builder.AddAttribute(21, "class", "tm-email-block__image-placeholder-reason");
            builder.AddContent(22, Loc[reasonKey]);
            builder.CloseElement();
            builder.CloseElement();
        };

    /// <summary>
    /// A blocked LINK marker: the alert icon plus a screen-reader-only explanation, so a refused
    /// <c>href</c> is visible in the body AND announced with its reason (the review's finding 18).
    /// </summary>
    private RenderFragment BlockedLinkMarker() => builder =>
    {
        builder.OpenElement(0, "span");
        builder.AddAttribute(1, "class", "tm-email-block__link-blocked-icon");
        builder.AddAttribute(2, "aria-hidden", "true");
        builder.OpenComponent<TmIcon>(3);
        builder.AddComponentParameter(4, nameof(TmIcon.Name), IconNames.AlertTriangle);
        builder.AddComponentParameter(5, nameof(TmIcon.Size), IconSize.Xs);
        builder.CloseComponent();
        builder.CloseElement();
        builder.OpenElement(6, "span");
        builder.AddAttribute(7, "class", "tm-sr-only");
        builder.AddContent(8, Loc["EmailEditor_Canvas_LinkBlocked"]);
        builder.CloseElement();
    };

    /// <summary>Whether <paramref name="href"/> is refused by the pipeline's URL allow-list.</summary>
    private static bool IsLinkBlocked(string? href)
        => !string.IsNullOrWhiteSpace(href) && EmailUrlPolicy.FindBlockedScheme(href) is not null;

    // ── Table rows ─────────────────────────────────────────────────────────────────────────

    private RenderFragment RenderTableRow(EmailTableRow row, bool header) => builder =>
    {
        builder.OpenElement(0, "tr");
        var sequence = 1;
        foreach (var cell in row.Cells)
        {
            builder.OpenElement(sequence++, header ? "th" : "td");
            if (cell.ColSpan is > 1)
            {
                builder.AddAttribute(sequence++, "colspan", cell.ColSpan.Value);
            }

            if (cell.RowSpan is > 1)
            {
                builder.AddAttribute(sequence++, "rowspan", cell.RowSpan.Value);
            }

            var style = EmailCanvasValue.Style(
                ("background-color", EmailCanvasValue.Color(cell.BackgroundColor)),
                ("text-align", EmailCanvasValue.Align(cell.Align)));
            if (style.Length > 0)
            {
                builder.AddAttribute(sequence++, "style", style);
            }

            var blocked = IsLinkBlocked(cell.Href);
            if (blocked)
            {
                // A refused cell link stays VISIBLE as refused (the author can fix it) and never becomes an
                // anchor: no href reaches the DOM and the click still selects the block.
                builder.OpenElement(sequence++, "span");
                builder.AddAttribute(sequence++, "class", "tm-email-block__cell-link tm-email-block__cell-link--blocked");
                builder.AddAttribute(sequence++, "data-tm-cell-link-blocked", "true");
                builder.AddAttribute(sequence++, "data-tm-link-blocked", "true");
                builder.AddAttribute(sequence++, "title", Loc["EmailEditor_Canvas_LinkBlocked"]);
                builder.AddContent(sequence++, BlockedLinkMarker());
                builder.AddContent(sequence++, cell.Text);
                builder.CloseElement();
            }
            else if (string.IsNullOrWhiteSpace(cell.Href))
            {
                builder.AddContent(sequence++, cell.Text);
            }
            else
            {
                // An allowed cell link is still drawn as an inert span: a click on the canvas selects the
                // block, it never navigates.
                builder.OpenElement(sequence++, "span");
                builder.AddAttribute(sequence++, "class", "tm-email-block__cell-link");
                builder.AddContent(sequence++, cell.Text);
                builder.CloseElement();
            }

            builder.CloseElement();
        }

        builder.CloseElement();
    };

    // ── Declarations plumbing ─────────────────────────────────────────────────────────────

    /// <summary>Splits a built style string back into declarations so two sources can be merged.</summary>
    private static IEnumerable<(string Name, string? Value)> SplitDeclarations(string? style)
    {
        if (string.IsNullOrEmpty(style))
        {
            yield break;
        }

        foreach (var declaration in style.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = declaration.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0) continue;
            yield return (declaration[..separator], declaration[(separator + 1)..]);
        }
    }
}
