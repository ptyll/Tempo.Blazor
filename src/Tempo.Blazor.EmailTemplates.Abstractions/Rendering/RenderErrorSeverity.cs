namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// How serious a <see cref="RenderError"/> is. The numeric values are part of the public contract
/// (D23) and must not be reordered — a host may persist or compare them.
/// </summary>
public enum RenderErrorSeverity
{
    /// <summary>
    /// The message could not be rendered correctly (a broken template, a broken MJML compile, a failed
    /// substitution). The render as a whole is not usable: <see cref="RenderResult.Success"/> is false.
    /// </summary>
    Error = 0,

    /// <summary>
    /// The message rendered and its HTML is complete, but a security clean-up changed it: a URL outside
    /// the scheme allow-list was replaced with <c>#</c>, or an event-handler attribute was removed from
    /// raw markup. <see cref="RenderResult.Success"/> stays true and the host decides whether to send.
    /// </summary>
    Warning = 1,
}
