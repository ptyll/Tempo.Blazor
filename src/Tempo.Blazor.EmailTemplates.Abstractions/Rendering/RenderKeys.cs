namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>Localization keys for diagnostics produced while rendering a template.</summary>
public static class RenderKeys
{
    /// <summary>
    /// A link, image or background used a URL scheme outside the allow-list and was replaced with
    /// <c>#</c> (§3.7a). Localized by the host; the resource entry lives in the editor package under
    /// the same key.
    /// </summary>
    public const string BlockedUrlScheme = "render.url.blocked_scheme";

    /// <summary>
    /// Raw markup carried an event-handler attribute (<c>on*</c>) or <c>srcdoc</c>, which was removed
    /// because a variable inside it would run as script. The editor package carries the resource entry.
    /// </summary>
    public const string EventHandlerRemoved = "render.markup.event_handler_removed";

    /// <summary>
    /// Markup that can rewrite a link at runtime was removed: an SVG animation element
    /// (<c>set</c>/<c>animate</c>/<c>animateMotion</c>/<c>animateTransform</c>/<c>discard</c>) or a
    /// <c>meta http-equiv="refresh"</c> redirect. E-mail has no legitimate use for either.
    /// </summary>
    public const string UnsafeElementRemoved = "render.markup.unsafe_element_removed";

    /// <summary>
    /// The rendered markup could not be checked within the guard's regex budget, so it was discarded and
    /// the render failed (<see cref="RenderErrorSeverity.Error"/>): unchecked markup is never returned.
    /// </summary>
    public const string MarkupTooComplex = "render.markup.too_complex";

    /// <summary>
    /// The preview renderer itself could not produce a result (a server or transport failure). The editor
    /// shows this as <see cref="EmailPreviewStatus.Unavailable"/> with a retry, not as a template error.
    /// </summary>
    public const string PreviewUnavailable = "render.preview.unavailable";

    /// <summary>
    /// No <see cref="IEmailPreviewRenderer"/> is registered, so there is nothing that could render a
    /// preview. The message names the missing registration
    /// (<c>AddTempoEmailTemplateEngine()</c> or a host <see cref="IEmailPreviewRenderer"/>).
    /// </summary>
    public const string PreviewNotRegistered = "render.preview.not_registered";
}
