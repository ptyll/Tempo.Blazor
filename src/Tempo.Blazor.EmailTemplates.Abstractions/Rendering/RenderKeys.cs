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
    /// Markup that can rewrite a link at runtime or run code was removed: an SVG animation element
    /// (<c>set</c>/<c>animate</c>/<c>animateMotion</c>/<c>animateTransform</c>/<c>discard</c>), a
    /// <c>meta http-equiv="refresh"</c> redirect, or a <c>style</c> payload <see cref="CssSafety"/> refuses
    /// (<c>expression(</c>, <c>behavior:</c>, <c>-moz-binding</c>, <c>@import</c>). E-mail has no legitimate use
    /// for any of them.
    /// </summary>
    public const string UnsafeElementRemoved = "render.markup.unsafe_element_removed";

    /// <summary>
    /// An attribute value the message cannot carry was dropped from the generated markup: a value that would
    /// open a declaration of its own (<c>x;background:url(…)</c> in a <c>padding</c>, a colour or a font stack),
    /// which the MJML compiler copies verbatim into the element's inline <c>style</c>. The rest of the element
    /// renders normally. The attribute NAME is reported, never the value. The editor package carries the
    /// resource entry.
    /// </summary>
    public const string InvalidAttributeValue = "render.attribute.invalid_value";

    /// <summary>
    /// The rendered markup could not be checked within the guard's regex budget, so it was discarded and
    /// the render failed (<see cref="RenderErrorSeverity.Error"/>): unchecked markup is never returned.
    /// </summary>
    public const string MarkupTooComplex = "render.markup.too_complex";

    /// <summary>
    /// A table cell carried a background colour outside the colour allow-list (<c>#rgb</c>, <c>#rrggbb</c>
    /// or a named colour of <see cref="EmailColorPolicy"/>), so the <c>bgcolor</c> attribute was omitted
    /// instead of being written. Severity is <see cref="RenderErrorSeverity.Warning"/>: the rest of the
    /// message renders normally. The editor package carries the resource entry.
    /// </summary>
    public const string InvalidTableCellColor = "render.table.invalid_color";

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
