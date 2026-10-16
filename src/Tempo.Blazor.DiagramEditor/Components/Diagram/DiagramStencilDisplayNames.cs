using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Shared display-name helpers for diagram stencils. Single source of truth for
/// resolving a stencil's localized display name (resource key with a fallback to
/// the raw <see cref="DiagramStencil.Name"/>) and for listing the stencils that
/// are connectable shapes (nodes only — edge stencils style relationships and
/// must never appear in "choose a shape" surfaces such as the connect modal).
/// </summary>
public static class DiagramStencilDisplayNames
{
    /// <summary>
    /// Returns the localized display name for <paramref name="stencil"/>:
    /// the translation of <see cref="DiagramStencil.NameResourceKey"/> when the
    /// key resolves, otherwise the stencil's raw name.
    /// </summary>
    public static string GetStencilDisplayName(DiagramStencil stencil, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(stencil);
        ArgumentNullException.ThrowIfNull(localizer);
        return GetLocalizedText(stencil.NameResourceKey, stencil.Name, localizer);
    }

    /// <summary>
    /// Resolves a localized text for <paramref name="resourceKey"/>, falling back
    /// to <paramref name="fallback"/> when the key is empty or untranslated.
    /// Untranslated is decided by <see cref="ITmLocalizer.TryGetString"/>: localizers
    /// may echo the missing key either bare (production default) or bracketed as
    /// <c>[key]</c> (test doubles) — both count as missing.
    /// </summary>
    public static string GetLocalizedText(string? resourceKey, string fallback, ITmLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        if (string.IsNullOrWhiteSpace(resourceKey))
        {
            return fallback;
        }

        return localizer.TryGetString(resourceKey, out var localized)
            ? localized
            : fallback;
    }

    /// <summary>
    /// Returns the connectable stencils from <paramref name="registry"/> — node
    /// stencils only, in the registry's display order. Edge stencils define
    /// relationship styling and are excluded from shape-picking surfaces.
    /// </summary>
    public static IEnumerable<DiagramStencil> GetConnectableStencils(DiagramStencilRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return registry.GetAll().Where(s => s.Kind == DiagramStencilKind.Node);
    }
}
