namespace Tempo.Blazor.AI.Markdown;

/// <summary>
/// Localized labels and host seams for <see cref="AiMarkdownRenderer"/>. The renderer itself never
/// invents user-visible text: every label is supplied here, which is how <c>TmMarkdownView</c> renders
/// the copy affordance and the citation chips in the host's language. A <see langword="null"/> or empty
/// label simply omits that attribute.
/// </summary>
public sealed class AiMarkdownRenderOptions
{
    /// <summary>Accessible name of the copy button of a code block that has no language.</summary>
    public string? CopyCodeLabel { get; init; }

    /// <summary>Accessible name of the copy button of a language-labelled code block; <c>{0}</c> is the language.</summary>
    public string? CopyCodeWithLanguageLabel { get; init; }

    /// <summary>Accessible name and status text after the code was copied.</summary>
    public string? CopiedLabel { get; init; }

    /// <summary>Accessible name of a citation chip; <c>{0}</c> is the citation text, e.g. <c>1</c>.</summary>
    public string? CitationLabelFormat { get; init; }

    /// <summary>
    /// Accessible name of the scrollable table region. A table wider than the conversation column scrolls
    /// inside its wrapper, so the wrapper is a keyboard focus stop and a named <c>role="region"</c>; with a
    /// <see langword="null"/> or empty label the wrapper keeps its focus stop but omits the role (an unnamed
    /// landmark cannot be identified in a screen reader's landmark list).
    /// </summary>
    public string? TableLabel { get; init; }

    /// <summary>
    /// Optional host renderer for a code block. When set, the built-in markup (including the copy
    /// affordance) is replaced by the returned HTML, which the renderer trusts verbatim — the host owns
    /// escaping and must treat its own output as trusted markup.
    /// </summary>
    /// <remarks>
    /// Escape <see cref="AiMarkdownCodeBlockContext.Code"/> (for example with
    /// <c>System.Text.Encodings.Web.HtmlEncoder.Default</c>) before it reaches the returned string, and pass
    /// a stable delegate reference (a cached field or a method group, not a lambda that builds a new closure
    /// per call) so a render-caching host can recognise it between renders: <c>TmMarkdownView</c> compares
    /// the delegate's <c>Method</c> and <c>Target</c>, not the delegate instance.
    /// </remarks>
    public Func<AiMarkdownCodeBlockContext, string>? CodeBlockRenderer { get; init; }
}
