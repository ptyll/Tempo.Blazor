using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Builds and dispatches <see cref="TemplateSecurityEvent"/>s for one render. Registration is optional
/// (zero sinks = no cost beyond a few field reads); the template hash and excerpt are computed lazily
/// on the first event; a sink that throws is logged and swallowed so audit can never break a render.
/// </summary>
internal sealed class TemplateSecurityAuditor
{
    private const int ExcerptLength = 200;

    private readonly string _template;
    private readonly IReadOnlyList<ITemplateSecurityAuditSink> _sinks;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger? _logger;
    private string? _templateHash;
    private string? _templateExcerpt;

    public TemplateSecurityAuditor(
        string template,
        IReadOnlyList<ITemplateSecurityAuditSink>? sinks,
        TimeProvider? timeProvider,
        ILogger? logger)
    {
        _template = template ?? string.Empty;
        _sinks = sinks ?? [];
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger;
    }

    /// <summary>Gets a value indicating whether any sink is registered.</summary>
    public bool HasSinks => _sinks.Count > 0;

    /// <summary>Reports one refusal to every registered sink.</summary>
    public void Report(TemplateSecurityEventKind kind, string detail)
    {
        if (!HasSinks)
        {
            return;
        }

        var securityEvent = new TemplateSecurityEvent(
            kind,
            detail,
            _templateHash ??= ComputeHash(_template),
            _templateExcerpt ??= ComputeExcerpt(_template),
            _timeProvider.GetUtcNow());

        foreach (var sink in _sinks)
        {
            try
            {
                sink.OnEvent(securityEvent);
            }
            catch (Exception ex)
            {
                // Audit is best effort: a broken sink must never fail or alter a render.
                _logger?.LogWarning(
                    ex,
                    "Template security audit sink {SinkType} failed while recording {Kind}.",
                    sink.GetType().FullName,
                    kind);
            }
        }
    }

    private static string ComputeHash(string template)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(template)));

    private static string ComputeExcerpt(string template)
        => template.Length <= ExcerptLength ? template : template[..ExcerptLength];
}
