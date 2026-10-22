using System.Globalization;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Default render pipeline: Scriban substitution on subject/preheader, MJML generation + Scriban
/// substitution (resolving variables, <c>VisibleWhen</c> conditions and HTML-escaping every expression
/// result) + MJML compilation for the HTML, and a substituted plain-text version. Every URL that ends
/// up in the markup is checked against the scheme allow-list after substitution, and event-handler
/// attributes of raw markup are removed. Errors and warnings from every phase are aggregated in order.
/// </summary>
/// <remarks>
/// Per-pass escaping requires an engine that implements the internal <c>IEscapingTemplateEngine</c>
/// contract (the Tempo <c>ScribanTemplateEngine</c>). A custom <see cref="ITemplateEngine"/> is used as
/// given and the renderer logs one warning at construction, because it cannot guarantee that the body is
/// escaped while the subject/preheader/text passes stay raw — escaping is then the host's responsibility.
/// </remarks>
public sealed class EmailTemplateRenderer : IEmailTemplateRenderer
{
    private readonly ITemplateEngine _engine;
    private readonly MjmlGenerator _generator;
    private readonly IMjmlCompiler _compiler;
    private readonly TextVersionGenerator _textGenerator;
    private readonly IReadOnlyList<ITemplateSecurityAuditSink> _auditSinks;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmailTemplateRenderer>? _logger;

    /// <summary>Initializes the renderer with its pipeline collaborators.</summary>
    /// <param name="engine">The template engine used for the substitution passes.</param>
    /// <param name="generator">The MJML generator.</param>
    /// <param name="compiler">The MJML compiler.</param>
    /// <param name="textGenerator">The plain-text version generator.</param>
    /// <param name="auditSinks">
    /// Optional audit sinks; they receive the <see cref="TemplateSecurityEventKind.BlockedUrlScheme"/>
    /// events of the URL allow-list. A sink that throws is logged and swallowed.
    /// </param>
    /// <param name="timeProvider">Clock used for the audit timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="logger">Logger for swallowed audit-sink failures.</param>
    public EmailTemplateRenderer(
        ITemplateEngine engine,
        MjmlGenerator generator,
        IMjmlCompiler compiler,
        TextVersionGenerator textGenerator,
        IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null,
        TimeProvider? timeProvider = null,
        ILogger<EmailTemplateRenderer>? logger = null)
    {
        _engine = engine;
        _generator = generator;
        _compiler = compiler;
        _textGenerator = textGenerator;
        _auditSinks = auditSinks?.Where(sink => sink is not null).ToArray() ?? [];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;

        // A custom engine cannot be asked for a raw pass, so the renderer cannot guarantee that the
        // subject/preheader/text passes stay unescaped and the body pass stays escaped. Say so once, at
        // construction, instead of silently rendering with a weaker contract.
        if (engine is not IEscapingTemplateEngine)
        {
            _logger?.LogWarning(
                "A custom ITemplateEngine cannot guarantee per-pass HTML escaping; the host is responsible for escaping.");
        }
    }

    /// <inheritdoc />
    public Task<RenderResult> RenderAsync(
        EmailTemplateDocument document,
        object? model = null,
        TemplateRenderOptions? options = null,
        CancellationToken cancellationToken = default)
        => RenderCore(document, model, options, substituteVariables: true, cancellationToken);

    /// <summary>
    /// Renders the document without substituting template variables, keeping every <c>{{ … }}</c> token in
    /// the output. Used by <see cref="LocalEmailPreviewRenderer"/> for the "no substitution" preview and the
    /// HTML export (<see cref="EmailPreviewRequest.SubstituteVariables"/> = <see langword="false"/>).
    /// </summary>
    /// <remarks>
    /// The markup is still generated, guarded and compiled by the same pipeline. Without substitution there
    /// is no expression result to escape, so the tokens themselves are the only untrusted text: every
    /// <c>{{ … }}</c> / <c>{% … %}</c> span is HTML-encoded by <see cref="TemplateSpanEncoder"/> before the
    /// markup is guarded. The export therefore keeps its tokens as *text* — nothing is emitted unchecked.
    /// </remarks>
    internal Task<RenderResult> RenderWithoutSubstitutionAsync(
        EmailTemplateDocument document,
        TemplateRenderOptions? options = null,
        CancellationToken cancellationToken = default)
        => RenderCore(document, null, options, substituteVariables: false, cancellationToken);

    private Task<RenderResult> RenderCore(
        EmailTemplateDocument document,
        object? model,
        TemplateRenderOptions? options,
        bool substituteVariables,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        // The formatter culture is resolved once for every pass of this render: an explicit Culture
        // wins, otherwise UseDocumentLanguageCulture takes it from the document language, otherwise the
        // engine's invariant default applies.
        options = ResolveRenderOptions(document, options);

        var errors = new List<RenderError>();
        var reportedSchemes = new HashSet<string>(StringComparer.Ordinal);
        var reportedHandlers = new HashSet<string>(StringComparer.Ordinal);
        var reportedElements = new HashSet<string>(StringComparer.Ordinal);

        // Subject, preheader and the text version are plain text: no escaping (§3.7).
        var subject = substituteVariables
            ? Substitute(document.Subject, model, options, TemplateEscapeMode.None, errors)
            : document.Subject;
        var preheader = document.Preheader is null
            ? null
            : substituteVariables
                ? Substitute(document.Preheader, model, options, TemplateEscapeMode.None, errors)
                : document.Preheader;

        // The MJML body is the HTML pass: every expression result is HTML-escaped. The generator reports
        // the values it had to drop (a table cell colour outside the allow-list), because the markup guard
        // cannot see a model colour at all.
        var generatorDiagnostics = new List<RenderError>();
        var mjml = _generator.Generate(document, MjmlGeneratorOptions.Default, generatorDiagnostics);
        errors.AddRange(generatorDiagnostics);

        var auditor = new TemplateSecurityAuditor(mjml, _auditSinks, _timeProvider, _logger);

        // A blocked table-cell link was already rewritten to '#' by the generator, so the guard below can
        // no longer recognise its scheme: the model link is warned and audited here instead (§3.7a).
        ReportModelLinkFindings(document, errors, reportedSchemes, auditor);
        var resolvedMjml = substituteVariables
            ? Substitute(mjml, model, options, TemplateEscapeMode.Html, errors)
            // No substitution: the token text is the only place markup could reach the export unchecked.
            : TemplateSpanEncoder.EncodeSpans(mjml);

        var (mjmlGuard, mjmlGuardFailed) = Guard(
            () => MarkupGuard.ApplyToMjml(resolvedMjml), errors);
        ReportGuardFindings(mjmlGuard, errors, reportedSchemes, reportedHandlers, reportedElements, auditor);

        var compiled = _compiler.Compile(mjmlGuard.Output);
        errors.AddRange(compiled.Errors);

        var (htmlGuard, htmlGuardFailed) = Guard(
            () => MarkupGuard.ApplyToHtml(compiled.Html, PermittedStyleImports(document)), errors);
        ReportGuardFindings(htmlGuard, errors, reportedSchemes, reportedHandlers, reportedElements, auditor);

        var textTemplate = _textGenerator.Generate(document);
        var textVersion = substituteVariables
            ? Substitute(textTemplate, model, options, TemplateEscapeMode.None, errors)
            : textTemplate;

        // Fail-closed: markup that could not be compiled or checked is never handed to the host as HTML.
        var html = mjmlGuardFailed || htmlGuardFailed || compiled.Errors.Count > 0
            ? string.Empty
            : htmlGuard.Output;

        return Task.FromResult(new RenderResult(html, subject, preheader, textVersion, errors));
    }

    /// <summary>
    /// Resolves the formatter culture of this render (§3.7): <see cref="TemplateRenderOptions.Culture"/>
    /// wins, then <see cref="TemplateRenderOptions.UseDocumentLanguageCulture"/> takes it from the
    /// document language, and a document without a usable language falls back to the invariant culture.
    /// </summary>
    private static TemplateRenderOptions? ResolveRenderOptions(
        EmailTemplateDocument document,
        TemplateRenderOptions? options)
    {
        if (options is null || options.Culture is not null || !options.UseDocumentLanguageCulture)
        {
            return options;
        }

        return new TemplateRenderOptions
        {
            Culture = ResolveDocumentCulture(document.Language),
            UseDocumentLanguageCulture = options.UseDocumentLanguageCulture,
        };
    }

    /// <summary>
    /// The <c>@import</c> targets the pipeline itself writes into the compiled HTML (Fáze 10 security re-review,
    /// item 4). The MJML compiler turns every <c>mj-font</c> into a <c>&lt;link&gt;</c> plus an
    /// <c>@import</c>, always from the href the generator had already passed through the URL allow-list; the
    /// guard's CSS rule refuses every OTHER <c>@import</c> (a raw block's stylesheet, an author's <c>mj-style</c>),
    /// so the pipeline's own font imports are named here explicitly — never matched by a pattern.
    /// </summary>
    private static IReadOnlyCollection<string> PermittedStyleImports(EmailTemplateDocument document)
    {
        if (document.Styles.Fonts.Count == 0)
        {
            return [];
        }

        var imports = new List<string>(document.Styles.Fonts.Count);
        foreach (var font in document.Styles.Fonts)
        {
            // The generator emits mj-font only when name AND href are usable, so a nameless entry imported
            // nothing and is not permitted either.
            if (string.IsNullOrEmpty(font.Name) || string.IsNullOrEmpty(font.Href))
            {
                continue;
            }

            imports.Add(EmailUrlPolicy.IsAllowed(font.Href) ? font.Href : EmailUrlPolicy.BlockedUrlPlaceholder);
        }

        return imports;
    }

    /// <summary>
    /// Maps a document language (<c>cs</c>, <c>en-US</c>, …) to a culture, falling back to the
    /// invariant culture for an empty or unknown code instead of failing the render.
    /// </summary>
    private static CultureInfo ResolveDocumentCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return CultureInfo.InvariantCulture;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (ArgumentException)
        {
            // CultureNotFoundException derives from ArgumentException: an unknown or malformed language
            // is not a render error, it simply means "no culture preference".
            return CultureInfo.InvariantCulture;
        }
    }

    private string Substitute(
        string template,
        object? model,
        TemplateRenderOptions? options,
        TemplateEscapeMode escapeMode,
        List<RenderError> errors)
    {
        var result = _engine is IEscapingTemplateEngine escapingEngine
            ? escapingEngine.Render(template, model, options, escapeMode)
            : _engine.Render(template, model, options);
        if (result.IsSuccess) return result.Value!;
        errors.Add(new RenderError(result.Error ?? "Template rendering failed."));
        return string.Empty;
    }

    /// <summary>
    /// Turns the guard's findings into <see cref="RenderErrorSeverity.Warning"/> diagnostics: the message
    /// stayed renderable, the clean-up just changed it, so <see cref="RenderResult.Success"/> keeps true
    /// and the host decides whether to send (D23).
    /// </summary>
    /// <summary>
    /// Runs one guard pass. A guard that cannot finish within its regex budget is an
    /// <see cref="RenderErrorSeverity.Error"/> (never a silently unchecked document) and yields empty
    /// markup, which makes the render fail closed.
    /// </summary>
    private static (MarkupGuardResult Result, bool Failed) Guard(
        Func<MarkupGuardResult> guard,
        List<RenderError> errors)
    {
        try
        {
            return (guard(), false);
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException ex)
        {
            errors.Add(MarkupGuard.FailureToError(ex));
            return (new MarkupGuardResult(string.Empty, [], [], []), true);
        }
    }

    /// <summary>
    /// Warns about (and audits) the blocked schemes of model links that the generator rewrote before the
    /// markup guard could see them. The wording and the audit detail match the guard's own finding — only
    /// the scheme is ever reported, never the URL, which may carry data.
    /// </summary>
    private static void ReportModelLinkFindings(
        EmailTemplateDocument document,
        List<RenderError> errors,
        HashSet<string> reportedSchemes,
        TemplateSecurityAuditor auditor)
    {
        foreach (var scheme in ModelLinkScanner.BlockedModelSchemes(document))
        {
            if (!reportedSchemes.Add(scheme))
            {
                continue;
            }

            errors.Add(new RenderError(
                $"A link with the blocked URL scheme '{scheme}' was replaced with '{UrlSchemePolicy.BlockedUrlPlaceholder}'.",
                Key: RenderKeys.BlockedUrlScheme,
                Severity: RenderErrorSeverity.Warning));
            auditor.Report(TemplateSecurityEventKind.BlockedUrlScheme, scheme);
        }
    }

    private static void ReportGuardFindings(
        MarkupGuardResult result,
        List<RenderError> errors,
        HashSet<string> reportedSchemes,
        HashSet<string> reportedHandlers,
        HashSet<string> reportedElements,
        TemplateSecurityAuditor auditor)
    {
        foreach (var scheme in result.BlockedSchemes)
        {
            if (!reportedSchemes.Add(scheme))
            {
                continue;
            }

            errors.Add(new RenderError(
                $"A link with the blocked URL scheme '{scheme}' was replaced with '{UrlSchemePolicy.BlockedUrlPlaceholder}'.",
                Key: RenderKeys.BlockedUrlScheme,
                Severity: RenderErrorSeverity.Warning));
            // Only the scheme is audited — never the URL, which may carry data.
            auditor.Report(TemplateSecurityEventKind.BlockedUrlScheme, scheme);
        }

        foreach (var handler in result.RemovedEventHandlers)
        {
            if (!reportedHandlers.Add(handler))
            {
                continue;
            }

            errors.Add(new RenderError(
                $"An event-handler attribute ('{handler}') was removed from the rendered markup.",
                Key: RenderKeys.EventHandlerRemoved,
                Severity: RenderErrorSeverity.Warning));
            auditor.Report(TemplateSecurityEventKind.BlockedMember, handler);
        }

        foreach (var element in result.RemovedElements)
        {
            if (!reportedElements.Add(element))
            {
                continue;
            }

            errors.Add(new RenderError(
                $"An unsafe element ('{element}') was removed from the rendered markup.",
                Key: RenderKeys.UnsafeElementRemoved,
                Severity: RenderErrorSeverity.Warning));
            auditor.Report(TemplateSecurityEventKind.BlockedMember, element);
        }
    }
}
