namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>
/// Classification of a <see cref="TemplateSecurityEvent"/>. The engine reports exactly these kinds and
/// never invents new ones at runtime, so a host can switch on the enum exhaustively. The numeric
/// values are part of the public contract (§3.6) and must not be reordered — a host may persist them.
/// </summary>
public enum TemplateSecurityEventKind
{
    /// <summary>
    /// A member access was refused by the sandbox member filter (for example <c>GetType</c>,
    /// <c>Assembly</c> or <c>TypeHandle</c>).
    /// </summary>
    BlockedMember = 0,

    /// <summary>
    /// A member declared on a type from a forbidden namespace was refused
    /// (<c>System.Reflection</c>, <c>System.IO</c>, <c>System.Diagnostics</c>, <c>System.Runtime*</c>,
    /// <c>System.Threading</c>, …) or the template referenced such a namespace explicitly.
    /// </summary>
    BlockedNamespace = 1,

    /// <summary>
    /// A builtin that evaluates a string as a template/script (<c>object.eval</c>,
    /// <c>object.eval_template</c>) or a file/environment/loader builtin (<c>file.*</c>, <c>env.*</c>,
    /// <c>include</c>) was refused.
    /// </summary>
    BlockedFunction = 2,

    /// <summary>
    /// A URL with a scheme outside the allow-list (<c>javascript:</c>, <c>data:</c>, <c>vbscript:</c>,
    /// <c>file:</c>, …) was replaced by <c>#</c> while rendering (emitted from the URL allow-list,
    /// phase 3).
    /// </summary>
    BlockedUrlScheme = 3,

    /// <summary>
    /// A sandbox limit was hit — loop iterations, recursion depth, output length or string conversion
    /// length.
    /// </summary>
    LimitExceeded = 4,

    /// <summary>
    /// Rendering exceeded the wall-clock <see cref="TemplateSecurityOptions.Timeout"/> or a regular
    /// expression exceeded <see cref="TemplateSecurityOptions.RegexTimeOut"/>.
    /// </summary>
    Timeout = 5,
}

/// <summary>
/// One audit record emitted by a template render. <see cref="Detail"/> names the refused member,
/// namespace, function or limit only — it never contains model values, so an audit trail cannot leak
/// the data a template was rendering.
/// </summary>
/// <param name="Kind">What the sandbox refused or aborted.</param>
/// <param name="Detail">Member/namespace/function/limit name; never a variable value.</param>
/// <param name="TemplateHash">Lowercase hexadecimal SHA-256 of the rendered template.</param>
/// <param name="TemplateExcerpt">
/// The first 200 characters of the template (may contain PII authored in the template — treat as
/// sensitive and apply your own retention policy).
/// </param>
/// <param name="Timestamp">When the event happened, taken from the engine's <see cref="TimeProvider"/>.</param>
public sealed record TemplateSecurityEvent(
    TemplateSecurityEventKind Kind,
    string Detail,
    string TemplateHash,
    string TemplateExcerpt,
    DateTimeOffset Timestamp);
