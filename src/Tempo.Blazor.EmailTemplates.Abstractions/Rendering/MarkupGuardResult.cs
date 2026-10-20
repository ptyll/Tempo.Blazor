namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>
/// Result of one markup-guard pass: the (possibly rewritten) markup plus what was refused, in the order
/// it was found.
/// </summary>
/// <param name="Output">The markup with refused URLs replaced, handlers and unsafe elements removed.</param>
/// <param name="BlockedSchemes">Lower-case scheme names that were refused (deduplicated).</param>
/// <param name="RemovedEventHandlers">Event-handler attribute names that were removed (deduplicated).</param>
/// <param name="RemovedElements">
/// Unsafe elements that were removed (deduplicated): an SVG animation element name, or
/// <c>meta-refresh</c>.
/// </param>
internal sealed record MarkupGuardResult(
    string Output,
    IReadOnlyList<string> BlockedSchemes,
    IReadOnlyList<string> RemovedEventHandlers,
    IReadOnlyList<string> RemovedElements);
