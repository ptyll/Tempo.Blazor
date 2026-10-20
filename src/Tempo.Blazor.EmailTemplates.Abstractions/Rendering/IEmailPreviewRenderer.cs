namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Replaceable preview renderer (§3.4, decision D17): the one seam through which the editor gets a
/// preview and an HTML export, so the render may run ON THE SERVER while the editor itself runs in
/// Blazor WebAssembly without Scriban, Mjml.Net or AngleSharp.
/// </summary>
/// <remarks>
/// <para>
/// The default implementation is <c>LocalEmailPreviewRenderer</c> in
/// <c>Tempo.Blazor.EmailTemplates.Engine</c>, registered by <c>AddTempoEmailTemplateEngine()</c>; it
/// wraps <see cref="IEmailTemplateRenderer"/>. A host that renders elsewhere registers its own (for
/// example an HTTP client that posts <see cref="EmailPreviewRequest"/> to its API).
/// </para>
/// <para>
/// A missing registration is not a silent empty iframe: the editor shows
/// <see cref="EmailPreviewStatus.Unavailable"/> with a localized message that names the missing
/// registration and offers a retry. The returned HTML is untrusted even when the host's own server
/// produced it — the editor only ever puts it into a <c>sandbox=""</c> iframe's <c>srcdoc</c>.
/// </para>
/// </remarks>
public interface IEmailPreviewRenderer
{
    /// <summary>
    /// Renders the preview. Implementations must not throw for a template failure — that is
    /// <see cref="EmailPreviewStatus.RenderFailed"/>. A transport/server failure is
    /// <see cref="EmailPreviewStatus.Unavailable"/> so the editor can offer a retry instead of
    /// blaming the author's template. A cancelled token raises <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="request">The document, variables, substitution switch and culture.</param>
    /// <param name="cancellationToken">
    /// Cancels this render. The editor cancels the previous render whenever a newer one starts, so a
    /// stale answer can never overwrite a newer one.
    /// </param>
    Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default);
}
