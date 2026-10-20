using System.Globalization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Per-render options handed to <see cref="Templating.ITemplateEngine.Render"/> and
/// <see cref="IEmailTemplateRenderer.RenderAsync"/>. It is the extension point for render-scoped
/// configuration (the culture of formatters); it deliberately has no escaping switch: the renderer
/// decides the escaping mode per pass (§3.7, D14) and the only opt-out is the
/// <c>{{ value | html.trusted }}</c> template filter. A host therefore cannot accidentally turn
/// HTML escaping off.
/// </summary>
public sealed class TemplateRenderOptions
{
    /// <summary>
    /// Culture of the formatters (<c>math.format</c>, <c>date.to_string</c>, plain number output…).
    /// <see langword="null"/> means "decide from <see cref="UseDocumentLanguageCulture"/>": the culture
    /// of <see cref="Model.EmailTemplateDocument.Language"/> when that option is set, otherwise
    /// <see cref="CultureInfo.InvariantCulture"/> — so a render never depends on the server's thread
    /// culture.
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>
    /// When <see langword="true"/> and <see cref="Culture"/> is <see langword="null"/>, the renderer
    /// takes the culture from the rendered document's language (an unknown language falls back to
    /// <see cref="CultureInfo.InvariantCulture"/>). <see cref="Templating.ITemplateEngine.Render"/> has
    /// no document, so the engine itself falls back to the invariant culture.
    /// </summary>
    public bool UseDocumentLanguageCulture { get; init; }
}
