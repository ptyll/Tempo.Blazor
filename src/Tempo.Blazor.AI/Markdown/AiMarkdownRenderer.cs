using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Renderers.Html.Inlines;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// The safe markdown pipeline behind <c>TmMarkdownView</c>: Markdig with raw HTML disabled, an explicit
/// URL allowlist and a hardened link renderer.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is the package's single security boundary for untrusted model output, so it is built
/// explicitly instead of through <c>UseAdvancedExtensions()</c>: the extension list below is that set
/// MINUS the ones that write markup from author input — <c>UseGenericAttributes</c> (arbitrary attribute
/// names, including event handlers), <c>UseMediaLinks</c>/<c>UseDiagrams</c> (hosted embeds) and
/// <c>UseCustomContainers</c> (author-supplied class names). A new extension is a security review, not a
/// one-word change.
/// </para>
/// <para>
/// Links, images, autolinks, code blocks and tables are rendered by the package's own renderers
/// (<see cref="AiMarkdownLinkRenderer"/>, <see cref="AiMarkdownAutolinkRenderer"/>,
/// <see cref="AiMarkdownCodeBlockRenderer"/>, <see cref="AiMarkdownTableRenderer"/>); everything else
/// keeps Markdig's default renderer.
/// </para>
/// </remarks>
public class AiMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = BuildPipeline();

    /// <summary>
    /// Renders untrusted markdown to safe HTML.
    /// </summary>
    /// <param name="markdown">The markdown to render; <see langword="null"/> or whitespace renders nothing.</param>
    /// <param name="options">
    /// Localized labels and the optional code-block renderer; <see langword="null"/> renders without any
    /// user-visible label (a host consuming the renderer directly supplies its own).
    /// </param>
    /// <returns>An HTML fragment that is safe to inject into the page.</returns>
    public virtual string Render(string? markdown, AiMarkdownRenderOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var effective = options ?? new AiMarkdownRenderOptions();
        var document = Markdig.Markdown.Parse(markdown, Pipeline);

        var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = CreateRenderer(writer, effective);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    private static MarkdownPipeline BuildPipeline()
        => new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseGridTables()
            .UseEmphasisExtras()
            .UseListExtras()
            .UseTaskLists()
            .UseAutoLinks()
            .UseFootnotes()
            .UseDefinitionLists()
            .UseAbbreviations()
            .UseSmartyPants()
            .UseEmojiAndSmiley()
            .DisableHtml()
            .Build();

    private static HtmlRenderer CreateRenderer(TextWriter writer, AiMarkdownRenderOptions options)
    {
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);

        ReplaceDefault<LinkInlineRenderer>(renderer, new AiMarkdownLinkRenderer(options));
        ReplaceDefault<AutolinkInlineRenderer>(renderer, new AiMarkdownAutolinkRenderer());
        ReplaceDefault<CodeBlockRenderer>(renderer, new AiMarkdownCodeBlockRenderer(options));
        ReplaceDefault<HtmlTableRenderer>(renderer, new AiMarkdownTableRenderer(options));
        return renderer;
    }

    /// <summary>
    /// Swaps one of Markdig's default renderers for the package's hardened version while keeping its
    /// position in the renderer list (the order decides inline precedence).
    /// </summary>
    private static void ReplaceDefault<TDefault>(HtmlRenderer renderer, IMarkdownObjectRenderer replacement)
        where TDefault : IMarkdownObjectRenderer
    {
        for (var index = 0; index < renderer.ObjectRenderers.Count; index++)
        {
            if (renderer.ObjectRenderers[index] is TDefault)
            {
                renderer.ObjectRenderers.RemoveAt(index);
                renderer.ObjectRenderers.Insert(index, replacement);
                return;
            }
        }

        renderer.ObjectRenderers.Add(replacement);
    }
}

/// <summary>
/// Renders links, images and citation chips. This is where the URL policy is enforced: a refused link
/// target becomes <c>#</c>, a refused image becomes its alt text, and a <c>cite:</c> link becomes a
/// button the page turns into a citation preview.
/// </summary>
internal sealed class AiMarkdownLinkRenderer : HtmlObjectRenderer<LinkInline>
{
    private readonly AiMarkdownRenderOptions _options;

    public AiMarkdownLinkRenderer(AiMarkdownRenderOptions options) => _options = options;

    protected override void Write(HtmlRenderer renderer, LinkInline link)
    {
        if (link.IsImage)
        {
            WriteImage(renderer, link);
            return;
        }

        if (AiMarkdownUrlPolicy.TryGetCitationId(link.Url, out var sourceId))
        {
            WriteCitation(renderer, link, sourceId);
            return;
        }

        var href = AiMarkdownUrlPolicy.SanitizeLink(link.Url);
        renderer.Write("<a href=\"");
        renderer.WriteEscapeUrl(href);
        renderer.Write("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\"");
        if (!string.IsNullOrEmpty(link.Title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(link.Title);
            renderer.Write('"');
        }

        renderer.Write('>');
        renderer.WriteChildren(link);
        renderer.Write("</a>");
    }

    private static void WriteImage(HtmlRenderer renderer, LinkInline link)
    {
        var alt = AiMarkdownInlineText.Of(link);
        if (!AiMarkdownUrlPolicy.IsSafeImageUrl(link.Url))
        {
            // A refused image never becomes a request: the alt text is the whole surviving content.
            renderer.WriteEscape(alt);
            return;
        }

        renderer.Write("<img src=\"");
        renderer.WriteEscapeUrl(link.Url);
        renderer.Write("\" alt=\"");
        renderer.WriteEscape(alt);
        renderer.Write('"');
        if (!string.IsNullOrEmpty(link.Title))
        {
            renderer.Write(" title=\"");
            renderer.WriteEscape(link.Title);
            renderer.Write('"');
        }

        renderer.Write(" loading=\"lazy\" referrerpolicy=\"no-referrer\" />");
    }

    private void WriteCitation(HtmlRenderer renderer, LinkInline link, string sourceId)
    {
        var text = AiMarkdownInlineText.Of(link);
        if (text.Length == 0)
        {
            text = sourceId;
        }

        renderer.Write("<button type=\"button\" class=\"tm-markdown-view__citation\" data-source-id=\"");
        renderer.WriteEscape(sourceId);
        renderer.Write('"');
        if (!string.IsNullOrEmpty(_options.CitationLabelFormat))
        {
            renderer.Write(" aria-label=\"");
            renderer.WriteEscape(string.Format(CultureInfo.CurrentCulture, _options.CitationLabelFormat, text));
            renderer.Write('"');
        }

        renderer.Write('>');
        renderer.WriteEscape(text);
        renderer.Write("</button>");
    }
}

/// <summary>Renders an autolink (<c>&lt;https://…&gt;</c>) through the same hardening as a normal link.</summary>
internal sealed class AiMarkdownAutolinkRenderer : HtmlObjectRenderer<AutolinkInline>
{
    protected override void Write(HtmlRenderer renderer, AutolinkInline inline)
    {
        var url = inline.IsEmail ? "mailto:" + inline.Url : inline.Url;
        var href = AiMarkdownUrlPolicy.SanitizeLink(url);

        renderer.Write("<a href=\"");
        renderer.WriteEscapeUrl(href);
        renderer.Write("\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">");
        renderer.WriteEscape(inline.Url ?? string.Empty);
        renderer.Write("</a>");
    }
}

/// <summary>
/// Renders a fenced or indented code block: an escaped <c>&lt;pre&gt;&lt;code&gt;</c> plus a copy
/// affordance, unless the host took the block over through
/// <see cref="AiMarkdownRenderOptions.CodeBlockRenderer"/>.
/// </summary>
internal sealed class AiMarkdownCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
{
    private const string CopyIcon =
        "<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" "
        + "stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
        + "<rect x=\"9\" y=\"9\" width=\"13\" height=\"13\" rx=\"2\" ry=\"2\" />"
        + "<path d=\"M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1\" /></svg>";

    private const string CopiedIcon =
        "<svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" "
        + "stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">"
        + "<polyline points=\"20 6 9 17 4 12\" /></svg>";

    private readonly AiMarkdownRenderOptions _options;

    public AiMarkdownCodeBlockRenderer(AiMarkdownRenderOptions options) => _options = options;

    /// <summary>
    /// Zero-based index of the block within THIS document, handed to
    /// <see cref="AiMarkdownRenderOptions.CodeBlockRenderer"/> so a host template can key its output
    /// (highlighting, anchoring) on a stable per-block number.
    /// </summary>
    /// <remarks>
    /// The counter is an instance field on purpose: <see cref="AiMarkdownRenderer.Render"/> builds a fresh
    /// renderer (and therefore a fresh <c>AiMarkdownCodeBlockRenderer</c>) for every call, so the field is
    /// per-render state. Never hoist it to <c>static</c> and never share one instance across calls — the
    /// index would then keep counting across documents. If a future refactor reuses renderers, the counter
    /// has to move into an explicit per-render context instead.
    /// </remarks>
    private int _index = -1;

    protected override void Write(HtmlRenderer renderer, CodeBlock block)
    {
        _index++;
        var code = ExtractCode(block);
        var language = GetLanguage(block);

        if (_options.CodeBlockRenderer is { } template)
        {
            renderer.Write(template(new AiMarkdownCodeBlockContext
            {
                Index = _index,
                Code = code,
                Language = language,
            }));
            return;
        }

        renderer.EnsureLine();
        renderer.Write("<div class=\"tm-markdown-view__code\">");
        renderer.Write("<div class=\"tm-markdown-view__code-header\">");
        if (language is not null)
        {
            renderer.Write("<span class=\"tm-markdown-view__code-language\">");
            renderer.WriteEscape(language);
            renderer.Write("</span>");
        }

        WriteCopyButton(renderer, language);
        renderer.Write("</div>");
        // tabindex="0": a long line scrolls horizontally inside <pre>, so the scroller has to be
        // reachable without a mouse (WCAG 2.1.1) — same contract as the table wrapper.
        renderer.Write("<pre tabindex=\"0\"><code");
        if (language is not null)
        {
            renderer.Write(" class=\"language-");
            renderer.WriteEscape(language);
            renderer.Write('"');
        }

        renderer.Write('>');
        renderer.WriteEscape(code);
        renderer.Write("</code></pre></div>");
        renderer.EnsureLine();
    }

    private void WriteCopyButton(HtmlRenderer renderer, string? language)
    {
        var label = language is not null && !string.IsNullOrEmpty(_options.CopyCodeWithLanguageLabel)
            ? string.Format(CultureInfo.CurrentCulture, _options.CopyCodeWithLanguageLabel, language)
            : _options.CopyCodeLabel;

        renderer.Write("<button type=\"button\" class=\"tm-markdown-view__code-copy\" data-copy-code=\"\"");
        if (!string.IsNullOrEmpty(label))
        {
            renderer.Write(" aria-label=\"");
            renderer.WriteEscape(label);
            renderer.Write('"');
        }

        if (!string.IsNullOrEmpty(_options.CopiedLabel))
        {
            renderer.Write(" data-copied-label=\"");
            renderer.WriteEscape(_options.CopiedLabel);
            renderer.Write('"');
        }

        renderer.Write('>');
        renderer.Write("<span class=\"tm-markdown-view__code-copy-icon\" data-icon=\"copy\">");
        renderer.Write(CopyIcon);
        renderer.Write("</span>");
        renderer.Write("<span class=\"tm-markdown-view__code-copy-icon\" data-icon=\"copied\">");
        renderer.Write(CopiedIcon);
        renderer.Write("</span>");
        renderer.Write("</button>");
        // The live region is the button's SIBLING: a role="status" inside a <button> is both unreliable in
        // assistive technology and part of the button's accessible name. The accessible name stays the
        // action's name; the copied confirmation is announced, not renamed (see ai-markdown.js).
        renderer.Write("<span class=\"tm-sr-only\" role=\"status\"></span>");
    }

    private static string ExtractCode(CodeBlock block)
    {
        var builder = new StringBuilder();
        var lines = block.Lines;
        for (var index = 0; index < lines.Count; index++)
        {
            builder.Append(lines.Lines[index].Slice.ToString());
            builder.Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// The fence info as a safe token: only the first word, and only when it is a plain language name.
    /// Anything else is treated as "no language", so no author-controlled text reaches the class attribute.
    /// </summary>
    private static string? GetLanguage(CodeBlock block)
    {
        if (block is not FencedCodeBlock fenced || string.IsNullOrWhiteSpace(fenced.Info))
        {
            return null;
        }

        var info = fenced.Info.Trim();
        var end = 0;
        while (end < info.Length && (char.IsAsciiLetterOrDigit(info[end]) || info[end] is '+' or '-' or '#' or '.' or '_'))
        {
            end++;
        }

        return end == 0 ? null : info[..end];
    }
}

/// <summary>
/// Wraps a table in a horizontally scrollable container (a wide answer never breaks the layout) that is
/// itself a keyboard focus stop and, when a label was supplied, a named region.
/// </summary>
internal sealed class AiMarkdownTableRenderer : HtmlTableRenderer
{
    private readonly AiMarkdownRenderOptions _options;

    public AiMarkdownTableRenderer(AiMarkdownRenderOptions options) => _options = options;

    protected override void Write(HtmlRenderer renderer, Table table)
    {
        renderer.EnsureLine();
        renderer.Write("<div class=\"tm-markdown-view__table\" tabindex=\"0\"");
        if (!string.IsNullOrEmpty(_options.TableLabel))
        {
            renderer.Write(" role=\"region\" aria-label=\"");
            renderer.WriteEscape(_options.TableLabel);
            renderer.Write('"');
        }

        renderer.Write('>');
        base.Write(renderer, table);
        renderer.Write("</div>");
        renderer.EnsureLine();
    }
}

/// <summary>Extracts the plain text of an inline container — used for image alt text and citation chips.</summary>
internal static class AiMarkdownInlineText
{
    internal static string Of(ContainerInline container)
    {
        var builder = new StringBuilder();
        Append(container, builder);
        return builder.ToString();
    }

    private static void Append(ContainerInline container, StringBuilder builder)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case LineBreakInline:
                    builder.Append(' ');
                    break;
                case ContainerInline nested:
                    Append(nested, builder);
                    break;
            }
        }
    }
}
