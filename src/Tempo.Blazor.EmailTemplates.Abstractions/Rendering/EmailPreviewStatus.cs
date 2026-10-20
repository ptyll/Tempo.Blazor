namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>Where the rendered preview came from (§3.4).</summary>
public enum EmailPreviewStatus
{
    /// <summary>The template rendered. Warnings may still be present (D23).</summary>
    Rendered = 0,

    /// <summary>The template itself failed to render (<see cref="RenderErrorSeverity.Error"/>).</summary>
    RenderFailed = 1,

    /// <summary>
    /// The renderer could not be reached — no registration, a network error or a server failure. Kept
    /// apart from <see cref="RenderFailed"/> because the author can retry, not fix the template.
    /// </summary>
    Unavailable = 2,
}
