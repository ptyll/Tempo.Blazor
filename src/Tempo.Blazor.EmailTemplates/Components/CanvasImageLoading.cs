namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// How the WYSIWYG canvas treats the images a template references (Fáze 9A).
/// <para>
/// The editor canvas is the one place where a template's remote images become requests from the AUTHOR's
/// browser: an imported template can carry a per-recipient tracking pixel, and a host editing a template
/// it did not author may not want that request made from the editor at all. The default is
/// <see cref="Load"/> — the canvas is a preview and the author is writing the email, so a missing picture
/// would be the more surprising behaviour.
/// </para>
/// </summary>
public enum CanvasImageLoading
{
    /// <summary>
    /// Load the image in an <c>&lt;img&gt;</c> after the pipeline's URL allow-list accepted it (the default).
    /// The rendered email loads the same source anyway, so this is the faithful preview.
    /// </summary>
    Load = 0,

    /// <summary>
    /// Never issue a request from the editor: paint a framed placeholder with the alt text instead. Use it
    /// for privacy (no tracking pixel from an author's browser) or on a metered connection.
    /// </summary>
    Placeholder = 1,
}
