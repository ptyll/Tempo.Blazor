namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Optional audit sink for template sandbox events. Register zero to many implementations in DI; the
/// engine resolves them as <see cref="IEnumerable{T}"/> and reports every refused member, namespace,
/// function, URL scheme, limit and timeout. Audit is best effort: an exception thrown by a sink is
/// swallowed and logged by the engine, never surfaced to the caller or the rendered output.
/// </summary>
/// <remarks>
/// The engine is a singleton and sinks are resolved once from the root provider, so register them as
/// singletons — a scoped sink would be captured by the singleton engine and outlive its scope.
/// </remarks>
public interface ITemplateSecurityAuditSink
{
    /// <summary>Records one security event. Implementations must be fast and must not block.</summary>
    /// <param name="securityEvent">The event to record.</param>
    void OnEvent(TemplateSecurityEvent securityEvent);
}
