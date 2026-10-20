namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// The rendered preview, or the reason it is missing (§3.4/D17). <see cref="Html"/>, <see cref="Text"/>
/// and <see cref="Subject"/> are <c>""</c> — never <see langword="null"/> — when the status is
/// <see cref="EmailPreviewStatus.RenderFailed"/> or <see cref="EmailPreviewStatus.Unavailable"/>, so a
/// host view never has to nil-check them.
/// </summary>
/// <param name="Html">The rendered HTML, sandboxed by the caller (never trusted, see D17).</param>
/// <param name="Text">The plain-text version.</param>
/// <param name="Subject">The substituted subject.</param>
/// <param name="Preheader">The substituted preheader, when the document has one.</param>
/// <param name="Errors">
/// Render diagnostics. Warnings (a blocked URL, a removed event handler) keep the status
/// <see cref="EmailPreviewStatus.Rendered"/> — the HTML is complete (D23).
/// </param>
/// <param name="Status">Where the preview came from.</param>
public sealed record EmailPreviewResult(
    string Html,
    string Text,
    string Subject,
    string? Preheader,
    IReadOnlyList<RenderError> Errors,
    EmailPreviewStatus Status)
{
    /// <summary>
    /// A result that carries nothing because the renderer could not be reached — the HTML, text and
    /// subject are empty strings, never null.
    /// </summary>
    public static EmailPreviewResult Unavailable(IReadOnlyList<RenderError>? errors = null)
        => new(string.Empty, string.Empty, string.Empty, null, errors ?? [], EmailPreviewStatus.Unavailable);
}
