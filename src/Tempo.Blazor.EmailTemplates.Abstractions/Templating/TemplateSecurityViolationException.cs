using Scriban.Syntax;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Thrown inside a render when the sandbox refuses a member, a namespace or a builtin, or when a limit
/// is hit. Rendering is fail-closed: a refused access aborts the render instead of producing an empty
/// string, so a host never mistakes a blocked template for one that rendered no data.
/// </summary>
/// <remarks>
/// Derives from <see cref="ScriptRuntimeException"/> on purpose — Scriban rewrites any other exception
/// raised while a member is resolved into a generic "Unexpected exception while accessing target
/// expression" error, which would lose the refusal classification.
/// </remarks>
internal sealed class TemplateSecurityViolationException : ScriptRuntimeException
{
    public TemplateSecurityViolationException(
        TemplateSecurityEventKind kind,
        string detail,
        Scriban.Parsing.SourceSpan span = default)
        : base(span, BuildMessage(kind, detail))
    {
        Kind = kind;
        Detail = detail;
    }

    /// <summary>Gets the classification reported to the audit sinks.</summary>
    public TemplateSecurityEventKind Kind { get; }

    /// <summary>Gets the refused member, namespace or function name (never a model value).</summary>
    public string Detail { get; }

    private static string BuildMessage(TemplateSecurityEventKind kind, string detail) => kind switch
    {
        TemplateSecurityEventKind.BlockedMember =>
            $"Template is not allowed to access member '{detail}'.",
        TemplateSecurityEventKind.BlockedNamespace =>
            $"Template is not allowed to access '{detail}'.",
        TemplateSecurityEventKind.BlockedFunction =>
            $"Template is not allowed to use '{detail}'.",
        TemplateSecurityEventKind.BlockedUrlScheme =>
            $"Template is not allowed to use URL '{detail}'.",
        TemplateSecurityEventKind.LimitExceeded =>
            $"Template exceeded the sandbox limit ({detail}).",
        TemplateSecurityEventKind.Timeout =>
            $"Template exceeded the sandbox timeout ({detail}).",
        _ => $"Template security violation ({kind}: {detail}).",
    };
}
