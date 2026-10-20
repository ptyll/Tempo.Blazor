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
}
