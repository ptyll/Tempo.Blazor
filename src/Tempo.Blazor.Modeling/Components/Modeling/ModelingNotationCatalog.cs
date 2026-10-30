using System.Collections.Concurrent;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Diagram.Stencils;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>One notation entry of the shared <see cref="ModelingNotationCatalog"/>: a semantic
/// type mapped to the diagram stencil that draws it.</summary>
/// <param name="SemanticType">The semantic element/relationship type (the notation profile's vocabulary).</param>
/// <param name="StencilId">Diagram stencil id the type maps to (the fallback stencil when the mapped one is unknown to the registry).</param>
/// <param name="Label">Localized label (<c>TmModelingSemanticType_{type}</c>, humanized key as fallback). Resolved per call — the entry itself is culture-invariant.</param>
/// <param name="IsRelationship">True for relationship types (edge stencils), false for element types (node stencils).</param>
/// <param name="HasMapping">False when the mapper mapped the type to a stencil the registry does not know — the entry renders the generic fallback icon.</param>
public sealed record ModelingNotationCatalogEntry(
    string SemanticType,
    string StencilId,
    string Label,
    bool IsRelationship,
    bool HasMapping);

/// <summary>
/// Fáze 15: the ONE notation catalog behind both the "Open in editor" palette (the pinned
/// stencil library) and the preview legend — the notation profile's semantic types mapped to
/// diagram stencils through the shared <see cref="IModelingStencilMapper"/>. The per-notation
/// resolution is memoized CULTURE-INVARIANT (semantic type, stencil id, relationship flag,
/// mapping flag only — labels are resolved per call so a culture switch needs no cache
/// invalidation; the stencil registry exposes no version, so staleness rides the per-call
/// document arguments). Legend entries are filtered to the types the generated document
/// actually uses: nodes through their <c>semanticType</c> data, relationships through the
/// caller-supplied model-derived set (the editor passes the relationship types whose Id is
/// among the document's edge Ids) with the connector-type detection as the fallback. Registered
/// as a singleton by <c>AddTempoBlazorModeling</c>.
/// </summary>
public sealed class ModelingNotationCatalog
{
    private readonly IModelingNotationProfileProvider _profileProvider;
    private readonly IModelingStencilMapper _stencilMapper;
    private readonly DiagramStencilRegistry _stencilRegistry;
    private readonly ConcurrentDictionary<string, IReadOnlyList<ModelingNotationCatalogEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The culture-invariant memoized entry (no label — labels resolve per call).</summary>
    private sealed record InvariantEntry(string SemanticType, string StencilId, bool IsRelationship, bool HasMapping);

    /// <summary>Creates the catalog.</summary>
    /// <param name="profileProvider">Notation profile lookup (the vocabulary of each notation).</param>
    /// <param name="stencilMapper">Semantic type → stencil mapping (the same mapper the diagram generator uses).</param>
    /// <param name="stencilRegistry">Stencil registry validating the mapped ids and edge connector types.</param>
    public ModelingNotationCatalog(
        IModelingNotationProfileProvider profileProvider,
        IModelingStencilMapper stencilMapper,
        DiagramStencilRegistry stencilRegistry)
    {
        _profileProvider = profileProvider ?? throw new ArgumentNullException(nameof(profileProvider));
        _stencilMapper = stencilMapper ?? throw new ArgumentNullException(nameof(stencilMapper));
        _stencilRegistry = stencilRegistry ?? throw new ArgumentNullException(nameof(stencilRegistry));
    }

    /// <summary>
    /// The palette entries of a notation: every ELEMENT type of its profile that the stencil
    /// mapper maps, in profile order. Types without a mapping are skipped (they cannot offer a
    /// shape); a mapping the registry does not know still lists, flagged with
    /// <see cref="ModelingNotationCatalogEntry.HasMapping"/> <c>false</c> so the caller renders
    /// the generic fallback icon. An unknown notation returns an empty list.
    /// </summary>
    public IReadOnlyList<ModelingNotationCatalogEntry> GetPalette(string notationKey, ITmLocalizer localizer)
        => Resolve(notationKey)
            .Where(entry => !entry.IsRelationship)
            .Select(entry => WithLabel(entry, localizer))
            .ToArray();

    /// <summary>
    /// The legend entries of a notation: the palette and relationship entries of the types the
    /// <paramref name="document"/> actually uses — nodes through their <c>semanticType</c> data
    /// value, relationships through <paramref name="presentRelationshipTypes"/> (the editor
    /// derives it from the MODEL: the relationship types whose Id is among the document's edge
    /// Ids). When that set is <c>null</c> (a host without the model) the presence falls back to
    /// the connector type of the mapped edge stencil; several relationship types sharing one
    /// stencil collapse into ONE row with their labels joined " / ". A null document, an unknown
    /// notation or an empty localizer result returns an empty list; the order follows the profile.
    /// </summary>
    public IReadOnlyList<ModelingNotationCatalogEntry> GetLegend(
        string notationKey,
        DiagramDocument? document,
        IReadOnlyCollection<string>? presentRelationshipTypes,
        ITmLocalizer localizer)
    {
        if (document is null || string.IsNullOrWhiteSpace(notationKey))
        {
            return [];
        }

        var entries = Resolve(notationKey);
        if (entries.Count == 0)
        {
            return [];
        }

        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var page in document.Pages)
        {
            foreach (var node in page.Nodes)
            {
                if (node.Data.TryGetValue("semanticType", out var semanticType)
                    && semanticType?.ToString() is { } nodeType)
                {
                    present.Add(nodeType);
                }
            }
        }

        var result = new List<ModelingNotationCatalogEntry>();
        foreach (var entry in entries)
        {
            if (!entry.IsRelationship && present.Contains(entry.SemanticType))
            {
                result.Add(WithLabel(entry, localizer));
            }
        }

        // Relationship rows are grouped BY STENCIL: several profile types may map to one edge
        // stencil (UML Dependency/Include/Extend), and both presence paths can then mark more
        // than one type present — the legend shows one row per stencil with the labels joined.
        var presentRelationships = presentRelationshipTypes is null
            ? DetectPresentRelationships(document, entries)
            : new HashSet<string>(presentRelationshipTypes, StringComparer.OrdinalIgnoreCase);
        foreach (var group in entries.Where(entry => entry.IsRelationship).GroupBy(entry => entry.StencilId, StringComparer.Ordinal))
        {
            var presentMembers = group
                .Where(entry => presentRelationships.Contains(entry.SemanticType))
                .ToArray();
            if (presentMembers.Length == 0)
            {
                continue;
            }

            var first = presentMembers[0];
            result.Add(first with
            {
                Label = presentMembers.Length == 1
                    ? GetLabel(first.SemanticType, localizer)
                    : string.Join(" / ", presentMembers.Select(member => GetLabel(member.SemanticType, localizer)))
            });
        }

        return result;
    }

    /// <summary>Fallback relationship presence (no model available): a semantic type counts as
    /// present when its mapped edge stencil's connector type matches an edge in the document.</summary>
    private HashSet<string> DetectPresentRelationships(DiagramDocument document, IReadOnlyList<ModelingNotationCatalogEntry> entries)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var connectorTypes = new HashSet<string>(
            document.Pages.SelectMany(page => page.Edges).Select(edge => edge.ConnectorType),
            StringComparer.Ordinal);

        foreach (var entry in entries.Where(entry => entry.IsRelationship))
        {
            var stencil = _stencilRegistry.GetStencil(entry.StencilId);
            var connectorType = stencil?.Kind == DiagramStencilKind.Edge
                ? stencil.EdgeDefaults?.ConnectorType
                : null;
            if (!string.IsNullOrWhiteSpace(connectorType) && connectorTypes.Contains(connectorType))
            {
                present.Add(entry.SemanticType);
            }
        }

        return present;
    }

    /// <summary>The memoized per-notation entries: elements first (profile order), then the
    /// profile's relationship types that the mapper maps to edge stencils.</summary>
    private IReadOnlyList<ModelingNotationCatalogEntry> Resolve(string notationKey)
    {
        if (string.IsNullOrWhiteSpace(notationKey))
        {
            return [];
        }

        return _cache.GetOrAdd(notationKey.Trim(), notation =>
        {
            var profile = _profileProvider.GetProfile(notation);
            if (profile is null)
            {
                return [];
            }

            var entries = new List<ModelingNotationCatalogEntry>();
            foreach (var elementType in profile.SupportedElementTypes)
            {
                if (CreateEntry(notation, elementType, isRelationship: false) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            foreach (var relationshipType in profile.SupportedRelationshipTypes)
            {
                if (CreateEntry(notation, relationshipType, isRelationship: true) is { } entry)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        });
    }

    private ModelingNotationCatalogEntry? CreateEntry(string notationKey, string semanticType, bool isRelationship)
    {
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            return null;
        }

        var mappedStencilId = isRelationship
            ? _stencilMapper.GetEdgeStencilId(notationKey, semanticType)
            : _stencilMapper.GetStencilId(notationKey, semanticType);

        if (string.IsNullOrWhiteSpace(mappedStencilId))
        {
            // No mapping — the type cannot offer a shape, so it is skipped.
            return null;
        }

        var stencil = _stencilRegistry.GetStencil(mappedStencilId);
        var hasMapping = stencil is not null;

        // An entry the registry does not know still lists — its StencilId stays the mapper's
        // mapping and the icon component renders the generic fallback shape for it — but the
        // entry keeps the fallback flag so hosts can tell the two apart.
        return new ModelingNotationCatalogEntry(
            semanticType,
            mappedStencilId,
            string.Empty, // Labels are culture-dependent and resolve per call.
            isRelationship,
            hasMapping);
    }

    private static ModelingNotationCatalogEntry WithLabel(ModelingNotationCatalogEntry entry, ITmLocalizer localizer)
        => entry with { Label = GetLabel(entry.SemanticType, localizer) };

    /// <summary>The localized semantic-type label (<c>TmModelingSemanticType_{type}</c>) with
    /// the tree's humanized-key fallback.</summary>
    private static string GetLabel(string semanticType, ITmLocalizer localizer)
        => localizer.TryGetString($"TmModelingSemanticType_{semanticType.Trim()}", out var localized)
            && !string.IsNullOrWhiteSpace(localized)
            ? localized
            : Humanize(semanticType);

    private static string Humanize(string semanticType)
    {
        var trimmed = semanticType.Trim();
        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        var spaced = new System.Text.StringBuilder(trimmed.Length + 8);
        for (var index = 0; index < trimmed.Length; index++)
        {
            var current = trimmed[index];
            if (index > 0 && char.IsUpper(current))
            {
                var previous = trimmed[index - 1];
                var next = index + 1 < trimmed.Length ? trimmed[index + 1] : default;
                var isBoundary = char.IsLower(previous)
                    || (char.IsUpper(previous) && next != default && char.IsLower(next));
                if (isBoundary)
                {
                    spaced.Append(' ');
                }
            }

            spaced.Append(current);
        }

        // Sentence case: capitalize the first character, lowercase the rest (the model tree's
        // humanized-group-key fallback behavior — both must agree for the same type).
        var words = spaced.ToString();
        return string.Concat(words.AsSpan(0, 1).ToString().ToUpperInvariant(), words[1..].ToLowerInvariant());
    }
}
