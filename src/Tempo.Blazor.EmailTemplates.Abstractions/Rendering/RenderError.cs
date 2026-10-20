namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>An error or warning produced while rendering a template.</summary>
/// <param name="Message">Human-readable description of the problem (English fallback).</param>
/// <param name="Line">1-based source line, when known.</param>
/// <param name="Column">1-based source column, when known.</param>
/// <param name="Key">
/// Localization key of the diagnostic (for example <see cref="RenderKeys.BlockedUrlScheme"/>), so a host
/// can present a localized message; <see langword="null"/> for messages that come straight from the MJML
/// compiler.
/// </param>
/// <param name="Severity">
/// Whether the render failed (<see cref="RenderErrorSeverity.Error"/>, the default) or completed with a
/// security clean-up (<see cref="RenderErrorSeverity.Warning"/>).
/// </param>
public sealed record RenderError(
    string Message,
    int? Line = null,
    int? Column = null,
    string? Key = null,
    RenderErrorSeverity Severity = RenderErrorSeverity.Error);
