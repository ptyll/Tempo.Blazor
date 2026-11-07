using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Phase 5 (decision <c>MODELING-PROPERTY-SCHEMA</c>): the EDITOR half of the schema-driven inspector
/// — schema resolution per provider key (cached per
/// <c>(providerKey, modelId, notation, semanticType, viewpointKey, culture, schemaVersion)</c> like
/// <see cref="IModelingModelWriter"/> resolution), the whole-model validation through the SHARED
/// <see cref="ModelingPropertySchemaValidator"/>, the save gate and the issue-dock merge.
/// <para>
/// Registration is per provider key and OPTIONAL: with no
/// <see cref="IModelingPropertySchemaProvider"/> for the effective key (or a provider that answers
/// <c>null</c>) nothing here runs, the inspector keeps its 2.10.0 surface and the operation engine
/// validates no property values.
/// </para>
/// <para>
/// The refresh runs after a render whose "schema selection key" (selection identity, model, notation,
/// viewpoint, culture and the edit generation) changed — that covers a selection change, every
/// commit, undo/redo and a reload with ONE guarded hook instead of a call on each of the eight
/// selection paths. Review A6: each refresh owns a cancellation token and a GENERATION, the token is
/// passed to the provider, and the generation is re-checked after every await — an answer for a
/// selection the user already left can never overwrite the newer one.
/// </para>
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>The property schema providers registered by the host (resolved by
    /// <see cref="IModelingPropertySchemaProvider.ProviderKey"/>, exactly like the model writers).</summary>
    [Inject] public IEnumerable<IModelingPropertySchemaProvider> PropertySchemaProviders { get; set; } = [];

    /// <summary>The host lookup providers for entity-reference fields (resolved by
    /// <see cref="IModelingLookupProvider.ProviderKey"/>).</summary>
    [Inject] public IEnumerable<IModelingLookupProvider> ModelingLookupProviders { get; set; } = [];

    /// <summary>Schemas resolved so far, keyed by the documented cache key. A <c>null</c> value is a
    /// cached "this host has no schema for that pair" answer (never re-asked). A provider that THROWS
    /// is not cached (review A5) — the next refresh retries.</summary>
    private readonly Dictionary<string, ModelingPropertySchemaDto?> _propertySchemaCache = new(StringComparer.Ordinal);

    /// <summary>The schema version a provider first served, PINNED per
    /// <c>providerKey|modelId|notation|semanticType|viewpointKey</c> (review A5), so a mid-session
    /// schema deploy cannot silently re-interpret a value. Mixed viewpoints/types therefore pin their
    /// own version instead of sharing one per provider.</summary>
    private readonly Dictionary<string, string> _pinnedSchemaVersions = new(StringComparer.Ordinal);

    /// <summary>The whole-model property issues, RAW (English <c>Message</c>, validator identity) —
    /// what the inspector matches and localizes itself.</summary>
    private IReadOnlyList<ModelingIssueDto> _propertyIssues = [];

    /// <summary>The same issues prepared for the ISSUE DOCK (review U10): the coded message localized,
    /// <c>SuggestedFix</c> cleared (the host-facing DTO keeps it).</summary>
    private IReadOnlyList<ModelingIssueDto> _dockPropertyIssues = [];

    /// <summary>The schema of the CURRENT selection (null = no schema → the 2.10.0 inspector).</summary>
    private ModelingPropertySchemaDto? _selectedPropertySchema;

    /// <summary>The inputs the last refresh was computed from (the re-render guard).</summary>
    private string _propertySchemaSelectionKey = string.Empty;

    /// <summary>Review A6: cancelled on every new refresh, on a save gate and on dispose.</summary>
    private CancellationTokenSource? _schemaRefreshCts;

    /// <summary>Review A6: bumped by every refresh/gate reset; an await that comes back with a
    /// different generation is stale and assigns nothing.</summary>
    private int _schemaRefreshGeneration;

    /// <summary>The model id the cache/pins belong to (review A5: a change resets both).</summary>
    private string? _propertySchemaModelId;

    /// <summary>Test seam (InternalsVisibleTo): the schema handed to the inspector.</summary>
    internal ModelingPropertySchemaDto? TestSelectedPropertySchema => _selectedPropertySchema;

    /// <summary>Test seam (InternalsVisibleTo): the whole-model property issues.</summary>
    internal IReadOnlyList<ModelingIssueDto> TestPropertyIssues => _propertyIssues;

    /// <summary>Test seam (InternalsVisibleTo): the dock's localized copy of the same issues.</summary>
    internal IReadOnlyList<ModelingIssueDto> TestDockPropertyIssues => _dockPropertyIssues;

    /// <summary>The schema provider registered for the effective model provider key, if any.</summary>
    private IModelingPropertySchemaProvider? ResolvedPropertySchemaProvider
        => PropertySchemaProviders.FirstOrDefault(provider =>
            string.Equals(provider.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal));

    /// <summary>The lookup provider registered for the effective model provider key, if any.</summary>
    private IModelingLookupProvider? ResolvedLookupProvider
        => ModelingLookupProviders.FirstOrDefault(provider =>
            string.Equals(provider.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal));

    /// <summary>The property issues of the SELECTED element/relationship (inline field errors) plus the
    /// last save's writer issues for it (§5.4: the host may reject a reference at save time).</summary>
    private IReadOnlyList<ModelingIssueDto> SelectedPropertyIssues
    {
        get
        {
            var elementId = _selectedElement?.Id ?? string.Empty;
            var relationshipId = _selectedRelationship?.Id ?? string.Empty;
            if (elementId.Length == 0 && relationshipId.Length == 0)
            {
                return [];
            }

            var issues = new List<ModelingIssueDto>();
            foreach (var issue in _propertyIssues)
            {
                if (MatchesSelection(issue, elementId, relationshipId))
                {
                    issues.Add(issue);
                }
            }

            foreach (var issue in _lastSaveResult?.Issues ?? [])
            {
                if (MatchesSelection(issue, elementId, relationshipId))
                {
                    issues.Add(issue);
                }
            }

            return issues;
        }
    }

    private static bool MatchesSelection(ModelingIssueDto issue, string elementId, string relationshipId)
        => (elementId.Length > 0 && string.Equals(issue.SourceElementId, elementId, StringComparison.Ordinal))
           || (relationshipId.Length > 0 && string.Equals(issue.SourceRelationshipId, relationshipId, StringComparison.Ordinal));

    /// <summary>
    /// Refreshes the selected schema and the model's property issues when the selection key moved.
    /// Returns <c>true</c> when anything changed (the caller re-renders once).
    /// </summary>
    private async Task<bool> TryRefreshPropertySchemaAsync()
    {
        var provider = ResolvedPropertySchemaProvider;
        if (_model is null || provider is null)
        {
            CancelSchemaRefresh();
            var hadState = _selectedPropertySchema is not null
                || _propertyIssues.Count > 0
                || _dockPropertyIssues.Count > 0;
            _selectedPropertySchema = null;
            _propertyIssues = [];
            _dockPropertyIssues = [];
            _propertySchemaSelectionKey = string.Empty;
            return hadState;
        }

        // Review A5: a model ID change invalidates every pin and cached schema — reset before the key
        // is computed so the new model asks its own provider from scratch.
        if (!string.Equals(_propertySchemaModelId, _model.Id, StringComparison.Ordinal))
        {
            ResetPropertySchemaState();
            _propertySchemaModelId = _model.Id;
        }

        var selectionKey = string.Join(
            '|',
            provider.ProviderKey,
            _model.Id,
            EffectiveNotationKey ?? string.Empty,
            EffectiveViewpointKey ?? string.Empty,
            CultureInfo.CurrentUICulture.Name,
            _selectedElement?.Id ?? string.Empty,
            _selectedElement?.SemanticType ?? string.Empty,
            _selectedRelationship?.Id ?? string.Empty,
            _selectedRelationship?.RelationshipType ?? string.Empty,
            _completedGenerationCount.ToString(CultureInfo.InvariantCulture),
            _commitVersion.ToString(CultureInfo.InvariantCulture));

        if (string.Equals(selectionKey, _propertySchemaSelectionKey, StringComparison.Ordinal))
        {
            return false;
        }

        _propertySchemaSelectionKey = selectionKey;

        // Review A6: this refresh supersedes every in-flight one. The token reaches the provider and
        // the generation is re-checked after each await, so a late answer for a selection the user
        // already left is dropped instead of overwriting the current one.
        var token = BeginSchemaRefresh(out var generation);

        ModelingPropertySchemaDto? schema = null;
        IReadOnlyList<ModelingIssueDto> issues = [];
        IReadOnlyList<ModelingIssueDto> dockIssues = [];
        try
        {
            if (_selectedElement is { } element)
            {
                schema = await ResolvePropertySchemaAsync(element.Notation, element.SemanticType, token);
            }
            else if (_selectedRelationship is { } relationship)
            {
                schema = await ResolvePropertySchemaAsync(_model.Notation, relationship.RelationshipType, token);
            }

            if (generation != _schemaRefreshGeneration)
            {
                return false;
            }

            // A read-only host (MO2) sees the schema labels but never gains validation findings.
            if (IsEditingEnabled)
            {
                (issues, dockIssues) = await ValidateModelPropertyValuesAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            return false;
        }

        if (generation != _schemaRefreshGeneration)
        {
            return false;
        }

        _selectedPropertySchema = schema;
        _propertyIssues = issues;
        _dockPropertyIssues = dockIssues;
        return true;
    }

    /// <summary>Starts a refresh scope: cancels the previous one, bumps the generation and returns the
    /// new token.</summary>
    private CancellationToken BeginSchemaRefresh(out int generation)
    {
        CancelSchemaRefresh();
        var cts = new CancellationTokenSource();
        _schemaRefreshCts = cts;
        generation = _schemaRefreshGeneration;
        return cts.Token;
    }

    /// <summary>Cancels the in-flight refresh and bumps the generation (review A6). The save gate calls
    /// it too: a refresh that started before the save must not land on the saved state.</summary>
    private void CancelSchemaRefresh()
    {
        _schemaRefreshCts?.Cancel();
        _schemaRefreshCts?.Dispose();
        _schemaRefreshCts = null;
        unchecked { _schemaRefreshGeneration++; }
    }

    /// <summary>Resolves (and caches) the schema of one notation + semantic type.</summary>
    private async Task<ModelingPropertySchemaDto?> ResolvePropertySchemaAsync(
        string notation,
        string semanticType,
        CancellationToken cancellationToken)
    {
        var provider = ResolvedPropertySchemaProvider;
        if (provider is null || _model is null || string.IsNullOrWhiteSpace(semanticType))
        {
            return null;
        }

        var culture = CultureInfo.CurrentUICulture.Name;
        var pinKey = BuildSchemaPinKey(provider.ProviderKey, notation, semanticType);
        var pinnedVersion = _pinnedSchemaVersions.TryGetValue(pinKey, out var pinned) ? pinned : null;
        var key = BuildSchemaCacheKey(provider.ProviderKey, notation, semanticType, culture, pinnedVersion);

        if (_propertySchemaCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        ModelingPropertySchemaDto? schema;
        try
        {
            schema = await provider.GetSchemaAsync(
                new ModelingPropertySchemaRequest
                {
                    ModelId = _model.Id,
                    Notation = notation ?? string.Empty,
                    SemanticType = semanticType,
                    ViewpointKey = EffectiveViewpointKey ?? string.Empty,
                    Culture = culture,
                    SchemaVersion = pinnedVersion
                },
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // A failing host provider must never break the editor: it degrades to "no schema" for the
            // CURRENT read and is NOT cached (review A5) — the next refresh asks again.
            return null;
        }

        _propertySchemaCache[key] = schema;
        if (schema is not null && !string.IsNullOrWhiteSpace(schema.Version))
        {
            if (pinnedVersion is null)
            {
                _pinnedSchemaVersions[pinKey] = schema.Version;
            }

            // The next request carries the now-pinned version, so it must be a cache HIT — otherwise
            // every pair would be asked twice (once unpinned, once pinned).
            _propertySchemaCache[BuildSchemaCacheKey(provider.ProviderKey, notation, semanticType, culture, schema.Version)] = schema;
        }

        return schema;
    }

    /// <summary>Review A5: the version pin is per PROVIDER + MODEL + NOTATION + TYPE + VIEWPOINT, so a
    /// mixed-notation model pins each pair independently instead of freezing the provider's first
    /// answer for everything.</summary>
    private string BuildSchemaPinKey(string providerKey, string? notation, string semanticType)
        => string.Join(
            '|',
            providerKey,
            _model?.Id ?? string.Empty,
            notation ?? string.Empty,
            semanticType,
            EffectiveViewpointKey ?? string.Empty);

    /// <summary>The documented schema cache key: <c>(providerKey, modelId, notation, semanticType,
    /// viewpointKey, culture, schemaVersion)</c>.</summary>
    private string BuildSchemaCacheKey(string providerKey, string? notation, string semanticType, string culture, string? schemaVersion)
        => string.Join(
            '|',
            providerKey,
            _model?.Id ?? string.Empty,
            notation ?? string.Empty,
            semanticType,
            EffectiveViewpointKey ?? string.Empty,
            culture,
            schemaVersion ?? string.Empty);

    /// <summary>Validates EVERY element's and relationship's property values with the shared validator
    /// (the SAME rules the engine applies to an MCP batch). Only errors are kept; the RAW list feeds
    /// the inspector's inline matching while the DOCK list carries the localized text (review U10).
    /// Review A12: each issue's id gains the owning item's id (<c>code:path@ownerId</c>) so two
    /// elements that miss the same field carry DISTINCT identities.</summary>
    private async Task<(IReadOnlyList<ModelingIssueDto> Raw, IReadOnlyList<ModelingIssueDto> Dock)> ValidateModelPropertyValuesAsync(
        CancellationToken cancellationToken)
    {
        if (_model is null || ResolvedPropertySchemaProvider is null)
        {
            return ([], []);
        }

        var validator = new ModelingPropertySchemaValidator();
        var raw = new List<ModelingIssueDto>();
        var dock = new List<ModelingIssueDto>();

        foreach (var element in _model.Elements)
        {
            var schema = await ResolvePropertySchemaAsync(element.Notation, element.SemanticType, cancellationToken);
            if (schema is null)
            {
                continue;
            }

            foreach (var issue in validator.Validate(schema, element.Properties))
            {
                if (issue.Severity != ModelingIssueSeverity.Error)
                {
                    continue;
                }

                issue.SourceElementId = element.Id;
                issue.Id = StampedIssueId(issue.Id, element.Id);
                raw.Add(issue);
                dock.Add(DockIssue(issue, schema));
            }
        }

        foreach (var relationship in _model.Relationships)
        {
            var schema = await ResolvePropertySchemaAsync(_model.Notation, relationship.RelationshipType, cancellationToken);
            if (schema is null)
            {
                continue;
            }

            foreach (var issue in validator.Validate(schema, relationship.Properties))
            {
                if (issue.Severity != ModelingIssueSeverity.Error)
                {
                    continue;
                }

                issue.SourceRelationshipId = relationship.Id;
                issue.Id = StampedIssueId(issue.Id, relationship.Id);
                raw.Add(issue);
                dock.Add(DockIssue(issue, schema));
            }
        }

        // Unknown-key INFO findings never enter the dock (host-added keys are allowed by design).
        return (raw, dock);
    }

    private static string StampedIssueId(string issueId, string ownerId)
        => issueId.Contains(ModelingPropertyIssueText.OwnerSeparator, StringComparison.Ordinal)
            ? issueId
            : $"{issueId}{ModelingPropertyIssueText.OwnerSeparator}{ownerId}";

    /// <summary>Review U10: the dock row for a coded schema issue — the localized message and no
    /// suggested fix. The raw DTO keeps the English text and the label, which is what MCP and a host
    /// writer see.</summary>
    private ModelingIssueDto DockIssue(ModelingIssueDto issue, ModelingPropertySchemaDto schema)
        => new()
        {
            Id = issue.Id,
            Severity = issue.Severity,
            Category = issue.Category,
            SourceElementId = issue.SourceElementId,
            SourceRelationshipId = issue.SourceRelationshipId,
            Message = ModelingPropertyIssueText.Localize(
                Loc,
                issue,
                ModelingPropertyIssueText.FindField(schema, ModelingPropertyIssueText.PathOf(issue.Id))),
            SuggestedFix = null
        };

    /// <summary>How many ERROR property issues currently block a save.</summary>
    private int BlockingPropertyIssueCount => _propertyIssues.Count(issue => issue.Severity == ModelingIssueSeverity.Error);

    /// <summary>
    /// The save gate (§5.6, review A9): revalidates the model and refuses the save while an Error issue
    /// TOUCHES THE PENDING CHANGE SET — every key of an ADDED item, or a key of an updated item that
    /// this change set sets/removes. An error the edit does not touch (a legacy value on another
    /// element) stays visible in the dock but never blocks the save, so a host cannot trap a user in a
    /// model it can no longer save.
    /// </summary>
    private async Task<bool> HasBlockingPropertyIssuesAsync(ModelingModelChangeSet changeSet)
    {
        if (ResolvedPropertySchemaProvider is null)
        {
            return false;
        }

        // A6: the gate supersedes any refresh that started before it.
        var token = BeginSchemaRefresh(out _);
        (var issues, var dockIssues) = await ValidateModelPropertyValuesAsync(token);
        _propertyIssues = issues;
        _dockPropertyIssues = dockIssues;

        var blocking = _propertyIssues.Count(issue =>
            issue.Severity == ModelingIssueSeverity.Error && BlocksPendingChange(issue, changeSet));
        if (blocking == 0)
        {
            return false;
        }

        _historyAnnouncement = string.Format(
            CultureInfo.CurrentUICulture,
            Loc["TmModelingInspector_SaveBlocked"],
            blocking);
        StateHasChanged();
        return true;
    }

    /// <summary>Whether an issue belongs to the pending change set (review A9).</summary>
    private static bool BlocksPendingChange(ModelingIssueDto issue, ModelingModelChangeSet changeSet)
    {
        var path = ModelingPropertyIssueText.PathOf(issue.Id);

        if (!string.IsNullOrWhiteSpace(issue.SourceElementId))
        {
            if (changeSet.AddedElements.Any(added => string.Equals(added.Id, issue.SourceElementId, StringComparison.Ordinal)))
            {
                return true;
            }

            var change = changeSet.Elements.FirstOrDefault(element =>
                string.Equals(element.ElementId, issue.SourceElementId, StringComparison.Ordinal));
            return change is not null && PathIsTouched(path, change.SetProperties?.Keys, change.RemovedProperties);
        }

        if (!string.IsNullOrWhiteSpace(issue.SourceRelationshipId))
        {
            if (changeSet.AddedRelationships.Any(added => string.Equals(added.Id, issue.SourceRelationshipId, StringComparison.Ordinal)))
            {
                return true;
            }

            var change = changeSet.Relationships.FirstOrDefault(relationship =>
                string.Equals(relationship.RelationshipId, issue.SourceRelationshipId, StringComparison.Ordinal));
            return change is not null && PathIsTouched(path, change.SetProperties?.Keys, change.RemovedProperties);
        }

        return false;
    }

    private static bool PathIsTouched(
        string path,
        IEnumerable<string>? setKeys,
        IEnumerable<string>? removedKeys)
    {
        var key = ModelingPropertyIssueText.TopLevelKey(path);
        if (key.Length == 0)
        {
            return false;
        }

        return (setKeys?.Contains(key, StringComparer.Ordinal) ?? false)
               || (removedKeys?.Contains(key, StringComparer.Ordinal) ?? false);
    }

    /// <summary>Drops the resolved schemas, the issues and every in-flight refresh. Called on a
    /// provider switch (review A5), on a model id change and from <c>ResetEditingState()</c>.</summary>
    private void ResetPropertySchemaState()
    {
        CancelSchemaRefresh();
        _propertySchemaCache.Clear();
        _pinnedSchemaVersions.Clear();
        _propertyIssues = [];
        _dockPropertyIssues = [];
        _selectedPropertySchema = null;
        _propertySchemaSelectionKey = string.Empty;
        _propertySchemaModelId = null;
    }
}
