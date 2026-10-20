using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Scriban;
using Scriban.Parsing;
using Scriban.Syntax;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// A sandboxed <see cref="ITemplateEngine"/> built on Scriban. Rendering is fail-closed: a template
/// that references reflection, a forbidden namespace or an evaluating/loader builtin fails instead of
/// rendering an empty result, and every refusal is reported to the registered
/// <see cref="ITemplateSecurityAuditSink"/>s. Work is bounded by <see cref="TemplateSecurityOptions"/>
/// (loops, recursion, output size, string conversion, regex and wall-clock time) and no .NET
/// reflection is exposed — the member policy is hard-coded and cannot be relaxed by a host.
/// <see cref="Render(string, object?, TemplateRenderOptions?)"/> HTML-encodes every expression result
/// by default; the only opt-out is the <c>html.trusted</c> filter, whose output is always sanitized.
/// </summary>
public sealed class ScribanTemplateEngine : IEscapingTemplateEngine
{
    private readonly TemplateSecurityOptions _options;
    private readonly IReadOnlyList<ITemplateSecurityAuditSink> _auditSinks;
    private readonly TemplateFunctionRegistry _functions;
    private readonly IHtmlContentSanitizer _sanitizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScribanTemplateEngine>? _logger;

    /// <summary>Initializes the engine with the given sandbox options, audit sinks and host functions.</summary>
    /// <param name="options">
    /// Sandbox limits; validated defaults are used when omitted. The engine keeps its own copy, so a
    /// later mutation of the passed instance cannot weaken a render.
    /// </param>
    /// <param name="auditSinks">
    /// Optional audit sinks. A sink that throws is logged and swallowed; it never changes the render.
    /// </param>
    /// <param name="functionProviders">
    /// Optional host function providers (<see cref="ITemplateFunctionProvider"/>). Every provider is
    /// validated when the engine is constructed — an invalid namespace or function name, a namespace
    /// colliding with a Scriban builtin or another provider, an async delegate or an unsupported
    /// delegate signature throws <see cref="ArgumentException"/> — and its namespaces are added to
    /// <see cref="TemplateSecurityOptions.FunctionNamespaces"/>.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">A limit is zero or negative.</exception>
    /// <exception cref="ArgumentException">A function provider cannot be exposed to templates.</exception>
    public ScribanTemplateEngine(
        TemplateSecurityOptions? options = null,
        IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null,
        IEnumerable<ITemplateFunctionProvider>? functionProviders = null,
        IHtmlContentSanitizer? sanitizer = null)
        : this(options, auditSinks, TimeProvider.System, TemplateFunctionRegistry.Create(functionProviders), sanitizer)
    {
    }

    internal ScribanTemplateEngine(
        TemplateSecurityOptions? options,
        IEnumerable<ITemplateSecurityAuditSink>? auditSinks,
        TimeProvider? timeProvider,
        TemplateFunctionRegistry? functions = null,
        IHtmlContentSanitizer? sanitizer = null,
        ILogger<ScribanTemplateEngine>? logger = null)
    {
        _options = (options ?? new TemplateSecurityOptions()).Clone();
        _options.Validate();
        _functions = functions ?? TemplateFunctionRegistry.Empty;
        foreach (var @namespace in _functions.NamespaceNames)
        {
            _options.FunctionNamespaces.Add(@namespace);
        }

        _sanitizer = sanitizer ?? AngleSharpHtmlContentSanitizer.Instance;
        _auditSinks = auditSinks?.Where(sink => sink is not null).ToArray() ?? [];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>
    /// Renders a template, HTML-encoding every expression result. This is the safe default: a value a
    /// template prints can never open markup. Use the <c>html.trusted</c> filter for markup a template
    /// is trusted to emit (it is sanitized, never written verbatim); the escaping mode itself is not
    /// host-configurable (§3.7, D14).
    /// </summary>
    public Result<string> Render(string template, object? model, TemplateRenderOptions? options = null)
        => Render(template, model, options, TemplateEscapeMode.Html);

    /// <summary>Renders one pipeline pass with the renderer-chosen escaping mode.</summary>
    Result<string> IEscapingTemplateEngine.Render(
        string template,
        object? model,
        TemplateRenderOptions? options,
        TemplateEscapeMode escapeMode)
        => Render(template, model, options, escapeMode);

    private Result<string> Render(
        string template,
        object? model,
        TemplateRenderOptions? options,
        TemplateEscapeMode escapeMode)
    {
        // The formatter culture is per render: an explicit Culture wins, otherwise the invariant
        // culture (deterministic output — the document language is resolved by the renderer, which is
        // the only layer that has the document).
        var culture = options?.Culture ?? CultureInfo.InvariantCulture;
        var auditor = new TemplateSecurityAuditor(template, _auditSinks, _timeProvider, _logger);

        // Fail-closed gate: refused constructs abort the render before anything is evaluated.
        var violations = SandboxTemplateScanner.Scan(template);
        if (violations.Count > 0)
        {
            foreach (var violation in violations)
            {
                auditor.Report(violation.Kind, violation.Detail);
            }

            return Result<string>.Failure(
                new TemplateSecurityViolationException(violations[0].Kind, violations[0].Detail).OriginalMessage);
        }

        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
            return Result<string>.Failure(string.Join("; ", parsed.Messages.Select(m => m.ToString())));

        try
        {
            using var context = new SandboxedTemplateContext(_options, escapeMode, _functions, culture, _sanitizer);
            context.PushGlobal(ObjectToScriptObjectConverter.ToScriptObject(model));
            var output = new SandboxedScriptOutput(_options.MaxOutputLength, _options.Timeout);
            context.PushOutput(output);

            parsed.Render(context);
            return Result<string>.Success(output.ToString());
        }
        catch (Exception ex)
        {
            if (TryClassify(ex, out var kind, out var detail))
            {
                auditor.Report(kind, detail);
            }

            return Result<string>.Failure(DescribeFailure(ex, kind, detail));
        }
    }

    /// <inheritdoc />
    public TemplateValidationResult Validate(string template)
    {
        var violations = SandboxTemplateScanner.Scan(template);
        if (violations.Count > 0)
        {
            var errors = violations
                .Select(v =>
                {
                    var (line, column) = GetPosition(template, v.Index);
                    return new TemplateError(
                        new TemplateSecurityViolationException(v.Kind, v.Detail).OriginalMessage,
                        line,
                        column);
                })
                .ToList();
            return TemplateValidationResult.Invalid(errors);
        }

        var parsed = Template.Parse(template);
        if (!parsed.HasErrors) return TemplateValidationResult.Valid;

        var parseErrors = parsed.Messages
            .Where(m => m.Type == ParserMessageType.Error)
            .Select(m => new TemplateError(m.Message, m.Span.Start.Line + 1, m.Span.Start.Column + 1))
            .ToList();
        return TemplateValidationResult.Invalid(parseErrors);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ExtractVariables(string template)
        => ScribanTemplateVariableExtractor.Instance.Extract(template);

    /// <summary>Maps a zero-based character index to the 1-based line/column pair of <see cref="TemplateError"/>.</summary>
    private static (int Line, int Column) GetPosition(string template, int index)
    {
        if (string.IsNullOrEmpty(template) || index < 0)
        {
            return (1, 1);
        }

        var line = 1;
        var lineStart = 0;
        var limit = Math.Min(index, template.Length);
        for (var i = 0; i < limit; i++)
        {
            if (template[i] == '\n')
            {
                line++;
                lineStart = i + 1;
            }
        }

        return (line, limit - lineStart + 1);
    }

    /// <summary>
    /// Classifies a failed render for the audit trail. A sandbox refusal, the render-abort
    /// cancellation and the regex timeout are found anywhere in the exception chain (Scriban may
    /// re-wrap them); Scriban's own limit errors are recognised from their messages. The wording is
    /// pinned by <c>SandboxAuditParityTests</c>, so a Scriban upgrade that changes it fails the suite
    /// instead of silently losing the audit event.
    /// </summary>
    private static bool TryClassify(Exception exception, out TemplateSecurityEventKind kind, out string detail)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TemplateSecurityViolationException violation)
            {
                kind = violation.Kind;
                detail = violation.Detail;
                return true;
            }

            if (current is RegexMatchTimeoutException)
            {
                kind = TemplateSecurityEventKind.Timeout;
                detail = "regex";
                return true;
            }

            // Scriban raises ScriptAbortException when the context's CancellationToken (armed with
            // TemplateSecurityOptions.Timeout) is cancelled on a loop step; a host cancellation may
            // surface as OperationCanceledException. Both are the render time budget.
            if (current is ScriptAbortException || current is OperationCanceledException)
            {
                kind = TemplateSecurityEventKind.Timeout;
                detail = "render time budget";
                return true;
            }

            if (current is TimeoutException)
            {
                kind = TemplateSecurityEventKind.Timeout;
                detail = "render time budget";
                return true;
            }
        }

        var message = exception.Message;
        kind = TemplateSecurityEventKind.LimitExceeded;
        if (message.Contains("Exceeding number of", StringComparison.Ordinal) ||
            message.Contains("RecursiveLimit", StringComparison.Ordinal))
        {
            detail = "recursion/iteration limit";
            return true;
        }

        if (message.Contains("LoopLimit", StringComparison.Ordinal))
        {
            detail = nameof(TemplateSecurityOptions.LoopLimit);
            return true;
        }

        if (message.Contains("OutputLimit", StringComparison.Ordinal))
        {
            detail = nameof(TemplateSecurityOptions.MaxOutputLength);
            return true;
        }

        if (message.Contains("LimitToString", StringComparison.Ordinal) ||
            message.Contains("string conversion limit", StringComparison.OrdinalIgnoreCase))
        {
            detail = nameof(TemplateSecurityOptions.LimitToString);
            return true;
        }

        detail = string.Empty;
        return false;
    }

    private static string DescribeFailure(
        Exception exception,
        TemplateSecurityEventKind kind,
        string detail)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TemplateSecurityViolationException violation)
            {
                return violation.OriginalMessage;
            }
        }

        return string.IsNullOrEmpty(detail)
            ? exception.Message
            : new TemplateSecurityViolationException(kind, detail).OriginalMessage;
    }
}
