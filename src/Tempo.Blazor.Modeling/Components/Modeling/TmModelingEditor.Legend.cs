using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Fáze 15: the notation palette + preview legend half of <see cref="TmModelingEditor"/>, kept
/// in its own partial so the main code-behind respects the source ceiling. The "Open in editor"
/// dialog pins the ACTIVE notation's stencils as the FIRST toolbox library (label = the
/// profile's display name, resolved through the shared <see cref="ModelingNotationCatalog"/>);
/// the preview carries the legend card; the mobile band explains the legend through a "⋯"
/// bottom sheet instead. Inserting from the palette creates ONLY a diagram node (the canvas
/// node's own <c>semanticType</c>/<c>notation</c> data) — never a semantic model element.
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>The shared notation catalog (registered by <c>AddTempoBlazorModeling</c>).
    /// Null (a host without the registration) keeps today's default toolbox and no legend.</summary>
    [Inject] public ModelingNotationCatalog? NotationCatalog { get; set; }

    /// <summary>Notation profile lookup for the pinned palette label (the profile display name).</summary>
    [Inject] public IModelingNotationProfileProvider? NotationProfiles { get; set; }

    /// <summary>
    /// Whether the preview renders the notation legend card. Default <c>true</c> — the legend is
    /// part of the editor's contract from Fáze 15 on (a host that wants the pre-Fáze-15 look
    /// passes <c>false</c>). The mobile band shows the legend as a "⋯" sheet instead of the card.
    /// </summary>
    [Parameter] public bool ShowPreviewLegend { get; set; } = true;

    // ── The dialog's pinned notation palette ────────────────────────────────

    /// <summary>
    /// The stencil ids pinned as the dialog toolbox's FIRST library: every element type of the
    /// active notation's profile that the shared catalog maps, in profile order, deduplicated
    /// (several semantic types may share one stencil). Null keeps today's default toolbox —
    /// no notation configured, no catalog, or the profile maps nothing.
    /// </summary>
    internal IReadOnlyList<string>? NotationPaletteStencilIds
    {
        get
        {
            var entries = GetNotationPaletteEntries();
            return entries is null
                ? null
                : entries.Select(entry => entry.StencilId).Distinct(StringComparer.Ordinal).ToArray();
        }
    }

    /// <summary>The pinned library label: the active notation profile's display name (null when
    /// no profile resolves — the toolbox falls back to its localized default).</summary>
    internal string? NotationPaletteLabel
        => EffectiveNotationKey is { } notation && NotationProfiles?.GetProfile(notation.Trim()) is { } profile
            ? profile.DisplayName
            : null;

    private IReadOnlyList<ModelingNotationCatalogEntry>? GetNotationPaletteEntries()
    {
        if (NotationCatalog is null || string.IsNullOrWhiteSpace(EffectiveNotationKey))
        {
            return null;
        }

        // Labels resolve per call through the editor's localizer — the catalog memoizes only
        // culture-invariant data, so a runtime culture switch re-labels without a cache reset.
        return NotationCatalog.GetPalette(EffectiveNotationKey, Loc);
    }

    // ── The preview legend entries ───────────────────────────────────────────

    /// <summary>
    /// The legend entries handed to the preview (and the mobile sheet): the shared catalog
    /// filtered to the types the CURRENT document uses. Null when the legend is off, no
    /// catalog is registered, no notation is active, or no document exists — the preview then
    /// renders no card. Computed per access (the document mutates in place — drops add nodes
    /// without replacing the instance), the per-notation resolution is memoized in the catalog.
    /// </summary>
    internal IReadOnlyList<ModelingNotationCatalogEntry>? LegendEntries
    {
        get
        {
            if (!ShowPreviewLegend
                || NotationCatalog is null
                || string.IsNullOrWhiteSpace(EffectiveNotationKey)
                || _document is null)
            {
                return null;
            }

            return NotationCatalog.GetLegend(EffectiveNotationKey, _document, ComputePresentRelationshipTypes(), Loc);
        }
    }

    /// <summary>
    /// Fáze 15 review: the relationship types the CURRENT document actually carries, derived
    /// from the MODEL — the <see cref="ModelingRelationshipDto.RelationshipType"/> of every
    /// <c>_model.Relationships</c> entry whose Id is among the document's edge Ids (the
    /// generator sets <c>edge.Id = relationship.Id</c>). This is exact where the catalog's
    /// connector-type fallback is not: several profile types (UML Dependency/Include/Extend)
    /// share one edge stencil and connector, and only the model knows which ones exist. Null
    /// while no model is loaded — the catalog then falls back to its connector detection.
    /// </summary>
    private IReadOnlyList<string>? ComputePresentRelationshipTypes()
    {
        if (_model is null || _document is null)
        {
            return null;
        }

        var edgeIds = new HashSet<string>(
            _document.Pages.SelectMany(page => page.Edges).Select(edge => edge.Id),
            StringComparer.Ordinal);

        return _model.Relationships
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Id)
                && !string.IsNullOrWhiteSpace(relationship.RelationshipType)
                && edgeIds.Contains(relationship.Id))
            .Select(relationship => relationship.RelationshipType)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // ── The legend's initial expansion ───────────────────────────────────────

    /// <summary>Fáze 15 review: the desktop node-count rule — a document with MORE than 12 nodes
    /// starts the legend collapsed (a long notation list would cover most of the canvas);
    /// smaller documents start expanded. The tablet band starts collapsed regardless.</summary>
    internal const int LegendExpandedNodeLimit = 12;

    /// <summary>Whether the preview's legend card starts expanded for the CURRENT document
    /// (desktop only consults this — the tablet band passes collapsed independently).</summary>
    internal bool LegendStartsExpanded => (_document?.Nodes.Count ?? 0) <= LegendExpandedNodeLimit;

    // ── The mobile "⋯" → Legenda bottom sheet ────────────────────────────────

    /// <summary>Whether the mobile legend sheet is open. A mobile-only surface (the compact
    /// header "⋯" is the only entry) — mirrors the "Change view" sheet state.</summary>
    private bool _legendSheetOpen;

    /// <summary>The cached sheet overlay attributes (the frozen sheet test id).</summary>
    private Dictionary<string, object>? _legendSheetAttributes;

    private Dictionary<string, object> LegendSheetAttributes => _legendSheetAttributes ??=
        new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["data-testid"] = TestId("modeling-legend-sheet")
        };

    private Task OpenLegendSheetAsync()
    {
        _legendSheetOpen = true;
        return Task.CompletedTask;
    }

    private Task CloseLegendSheetAsync()
    {
        _legendSheetOpen = false;
        return Task.CompletedTask;
    }
}
