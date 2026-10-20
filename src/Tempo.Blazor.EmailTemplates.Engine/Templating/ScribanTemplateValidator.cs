using Microsoft.Extensions.Logging;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// The authoritative <see cref="ITemplateValidator"/>: validates a template with the same sandbox scanner
/// and Scriban parser the render uses, so "valid" here means "this engine can render it" — a template that
/// reaches for a forbidden member fails validation before it is ever rendered.
/// </summary>
/// <remarks>
/// Registered by <c>AddTempoEmailTemplateEngine()</c>, which replaces nothing in the model registration
/// (the model deliberately has no syntax validator — without this type the editor's validation panel
/// degrades to a localized explanation instead of pretending the template is fine).
/// </remarks>
public sealed class ScribanTemplateValidator : ITemplateValidator
{
    private readonly ScribanTemplateEngine _engine;

    /// <summary>Initializes the validator over a sandboxed engine with the given limits.</summary>
    /// <param name="options">Sandbox limits; validated defaults are used when omitted.</param>
    /// <param name="auditSinks">Optional audit sinks (a refused construct can be reported).</param>
    /// <param name="timeProvider">Clock used for the audit timestamps; <see cref="TimeProvider.System"/> by default.</param>
    /// <param name="logger">Logger for swallowed audit-sink failures.</param>
    public ScribanTemplateValidator(
        TemplateSecurityOptions? options = null,
        IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null,
        TimeProvider? timeProvider = null,
        ILogger<ScribanTemplateEngine>? logger = null)
    {
        _engine = new ScribanTemplateEngine(options, auditSinks, timeProvider, functions: null, logger: logger);
    }

    /// <inheritdoc />
    public TemplateValidationResult Validate(string template) => _engine.Validate(template);
}
