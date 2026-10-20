using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>Renders, validates and analyzes Scriban-syntax templates within a sandbox.</summary>
public interface ITemplateEngine
{
    /// <summary>
    /// Renders a template with the given model. Never throws — failures are returned.
    /// </summary>
    /// <remarks>
    /// Tempo's <c>ScribanTemplateEngine</c> HTML-encodes the output of every expression (§3.7), with
    /// <c>html.trusted</c> as the only opt-out. A custom implementation of this interface is responsible
    /// for escaping its own output.
    /// </remarks>
    /// <param name="template">The template source.</param>
    /// <param name="model">The data the template reads.</param>
    /// <param name="options">Per-render options such as the formatter culture.</param>
    Result<string> Render(string template, object? model, TemplateRenderOptions? options = null);

    /// <summary>Validates a template's syntax without rendering it.</summary>
    TemplateValidationResult Validate(string template);

    /// <summary>Extracts the variable paths referenced by a template.</summary>
    IReadOnlyList<string> ExtractVariables(string template);
}
