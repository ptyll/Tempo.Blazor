using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Dtos;

/// <summary>A render diagnostic projected for transport.</summary>
/// <param name="Message">The message (English fallback).</param>
/// <param name="Line">Optional source line.</param>
/// <param name="Column">Optional source column.</param>
/// <param name="Key">Optional localization key, see <see cref="Rendering.RenderKeys"/>.</param>
/// <param name="Severity">
/// Whether the render failed (<see cref="RenderErrorSeverity.Error"/>, the default) or completed with a
/// security clean-up (<see cref="RenderErrorSeverity.Warning"/>).
/// </param>
public sealed record RenderErrorDto(
    string Message,
    int? Line,
    int? Column,
    string? Key = null,
    RenderErrorSeverity Severity = RenderErrorSeverity.Error);
