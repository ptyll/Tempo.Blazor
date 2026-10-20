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
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<RenderError>();
        var reportedSchemes = new HashSet<string>(StringComparer.Ordinal);
        var reportedHandlers = new HashSet<string>(StringComparer.Ordinal);
        var reportedElements = new HashSet<string>(StringComparer.Ordinal);

        // Subject, preheader and the text version are plain text: no escaping (§3.7).
        var subject = Substitute(document.Subject, model, options, TemplateEscapeMode.None, errors);
        var preheader = document.Preheader is null
            ? null
            : Substitute(document.Preheader, model, options, TemplateEscapeMode.None, errors);

        // The MJML body is the HTML pass: every expression result is HTML-escaped.
        var mjml = _generator.Generate(document);
        var auditor = new TemplateSecurityAuditor(mjml, _auditSinks, _timeProvider, _logger);
        var resolvedMjml = Substitute(mjml, model, options, TemplateEscapeMode.Html, errors);

        var (mjmlGuard, mjmlGuardFailed) = Guard(
            () => MarkupGuard.ApplyToMjml(resolvedMjml), errors);
        ReportGuardFindings(mjmlGuard, errors, reportedSchemes, reportedHandlers, reportedElements, auditor);

        var compiled = _compiler.Compile(mjmlGuard.Output);
        errors.AddRange(compiled.Errors);

        var (htmlGuard, htmlGuardFailed) = Guard(() => MarkupGuard.ApplyToHtml(compiled.Html), errors);
        ReportGuardFindings(htmlGuard, errors, reportedSchemes, reportedHandlers, reportedElements, auditor);

        var textVersion = Substitute(_textGenerator.Generate(document), model, options, TemplateEscapeMode.None, errors);

        // Fail-closed: markup that could not be compiled or checked is never handed to the host as HTML.
        var html = mjmlGuardFailed || htmlGuardFailed || compiled.Errors.Count > 0
            ? string.Empty
            : htmlGuard.Output;

        return Task.FromResult(new RenderResult(html, subject, preheader, textVersion, errors));
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
