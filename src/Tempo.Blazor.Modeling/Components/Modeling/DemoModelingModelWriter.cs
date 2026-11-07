using System.Globalization;
using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// In-memory demo writer (Fáze 14, extended in phase 2 with the v2 change set): persists inspector
/// edits against an in-memory copy of the demo model so the demo page can exercise the full edit →
/// save → reload loop without a backend. Version checks are simulated with a configurable
/// <see cref="SourceVersion"/>, which the <c>?scenario=save-conflict</c> demo route bumps so the
/// writer reports the conflict path.
/// <para>
/// It is also the reference writer for the authoring contract: it advertises
/// <see cref="ModelingModelWriterCapabilities.All"/>, translates the change set with the shared
/// <see cref="ModelingChangeSetTranslator"/> (decision <c>MODELING-AUTHORING-CHANGESET</c>) and
/// applies the resulting batch through the shared <see cref="ModelingOperationEngine"/> under the
/// <see cref="ModelingOperationPolicy.Authoring"/> policy, so the demo exercises exactly the
/// semantics a real host writer must implement. Registered automatically by
/// <c>AddTempoBlazorModeling</c> for the <see cref="DemoModelingModelProvider"/> key, where the
/// built-in relationship/viewpoint rules providers are injected so an invalid BPMN flow fails the
/// save just like it would in the editor.
/// </para>
/// </summary>
public sealed class DemoModelingModelWriter : IModelingModelWriter
{
    private readonly object _gate = new();
    private readonly IModelingRelationshipRulesProvider? _relationshipRules;
    private readonly IModelingViewpointRulesProvider? _viewpointRules;
    private readonly Dictionary<string, ModelingModelDto> _persistedByModelId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _sourceVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _baseVersions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _saveCounts = new(StringComparer.Ordinal);

    /// <summary>Creates the demo writer.</summary>
    /// <param name="relationshipRules">Notation relationship rules handed to the engine
    /// (<see cref="ModelingOperationContext.RelationshipRules"/>); null validates references only.</param>
    /// <param name="viewpointRules">Optional viewpoint rules handed to the engine
    /// (<see cref="ModelingOperationContext.ViewpointRules"/>).</param>
    public DemoModelingModelWriter(
        IModelingRelationshipRulesProvider? relationshipRules = null,
        IModelingViewpointRulesProvider? viewpointRules = null)
    {
        _relationshipRules = relationshipRules;
        _viewpointRules = viewpointRules;
    }

    /// <inheritdoc />
    public string ProviderKey => DemoModelingModelProvider.ProviderKeyValue;

    /// <summary>The demo writer stores content, structure and layout (the whole v2 change set).</summary>
    public ModelingModelWriterCapabilities Capabilities => ModelingModelWriterCapabilities.All;

    /// <summary>The source version the writer pretends the backend is on for the DEFAULT demo model
    /// (the save-conflict scenario raises it so the next save reports
    /// <see cref="ModelingModelSaveResult.IsConflict"/>). Each demo model keeps its OWN version (and
    /// save counter): a session that saved one scenario must not make the other model conflict.
    /// </summary>
    public string SourceVersion
    {
        get => GetSourceVersion(DemoModelingModelProvider.ModelIdValue);
        set => _sourceVersions[DemoModelingModelProvider.ModelIdValue] = value ?? string.Empty;
    }

    /// <summary>Armed by the demo <c>?scenario=save-conflict</c> route: the NEXT save reports
    /// the conflict path (the source moved on from the version the editor based its edit on)
    /// and the flag disarms itself, so a later save succeeds.
    /// <para>
    /// DEMO-ONLY: a STATIC mutable flag shared by the whole process — two browser sessions (or
    /// two editors on one page) would arm each other's conflict. A real writer scopes conflict
    /// state per session/request; the demo accepts the coupling because the writer is demo
    /// infrastructure, not a contract example.
    /// </para>
    /// </summary>
    public static bool ConflictArmed { get; set; }

    /// <summary>The persisted model (the last successful save or the initial demo model).</summary>
    public ModelingModelDto PersistedModel => GetPersistedModel(DemoModelingModelProvider.ModelIdValue);

    /// <summary>The persisted copy of one demo model (a successful save or the shipped default).
    /// Phase 5: the writer serves MORE than the single default model — the step-attribute scenario
    /// keeps its own persisted copy so the two scenarios cannot leak into each other.</summary>
    public ModelingModelDto GetPersistedModel(string modelId)
    {
        lock (_gate)
        {
            if (_persistedByModelId.TryGetValue(modelId, out var persisted))
            {
                return persisted;
            }

            var initial = CreateInitialModel(modelId);
            _persistedByModelId[modelId] = initial;
            return initial;
        }
    }

    /// <summary>The shipped default of one demo model id, or <c>null</c> when the writer does not know
    /// the id.</summary>
    private static ModelingModelDto? CreateInitialModel(string modelId)
    {
        if (string.Equals(modelId, DemoModelingModelProvider.ModelIdValue, StringComparison.Ordinal))
        {
            return ModelingOperationEngine.Clone(DemoModelingModelProvider.CreateModel());
        }

        if (string.Equals(modelId, DemoModelingPropertySchemaProvider.ModelIdValue, StringComparison.Ordinal))
        {
            return ModelingOperationEngine.Clone(DemoModelingPropertySchemaProvider.CreateStepAttributesModel());
        }

        return null;
    }

    /// <summary>How many change sets the DEFAULT demo model persisted (demo diagnostics).</summary>
    public int SaveCount
    {
        get
        {
            lock (_gate)
            {
                return _saveCounts.TryGetValue(DemoModelingModelProvider.ModelIdValue, out var count) ? count : 0;
            }
        }
    }

    /// <summary>The source version the writer pretends the backend is on for one demo model: its
    /// shipped metadata version until a save bumps it (<c>&lt;base&gt;-&lt;n&gt;</c>).</summary>
    public string GetSourceVersion(string modelId)
    {
        lock (_gate)
        {
            if (_sourceVersions.TryGetValue(modelId, out var version))
            {
                return version;
            }

            var initial = CreateInitialModel(modelId)?.Metadata.SourceVersion;
            version = string.IsNullOrWhiteSpace(initial) ? "2026.06" : initial;
            _sourceVersions[modelId] = version;
            _baseVersions[modelId] = version;
            return version;
        }
    }

    /// <inheritdoc />
    public async Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (CreateInitialModel(changes.ModelId) is null)
        {
            return new ModelingModelSaveResult
            {
                Succeeded = false,
                Message = $"Unknown model '{changes.ModelId}'."
            };
        }

        // Phase 5 review (A3): preload the schemas the batch may need BEFORE the synchronous apply —
        // ModelingOperationContext.ResolvePropertySchema is a dictionary lookup built by the shared
        // resolver, so the demo save validates the SAME rules the inspector did. The pairs the BATCH
        // introduces (a brand-new task type / relationship type) are included; the model alone does
        // not carry them yet.
        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
            new DemoModelingPropertySchemaProvider(),
            GetPersistedModel(changes.ModelId),
            AddedPairs(GetPersistedModel(changes.ModelId), changes),
            GetPersistedModel(changes.ModelId).Views.FirstOrDefault()?.ViewpointKey,
            CultureInfo.CurrentUICulture.Name,
            cancellationToken);

        lock (_gate)
        {
            return SaveLocked(changes, resolver);
        }
    }

    /// <summary>The pairs only the CHANGE SET introduces (added elements / relationships).</summary>
    private static IEnumerable<(string Notation, string SemanticType)> AddedPairs(
        ModelingModelDto model,
        ModelingModelChangeSet changes)
    {
        foreach (var added in changes.AddedElements)
        {
            yield return (
                string.IsNullOrWhiteSpace(added.Notation) ? model.Notation : added.Notation,
                added.SemanticType);
        }

        foreach (var added in changes.AddedRelationships)
        {
            yield return (model.Notation, added.RelationshipType);
        }
    }

    private ModelingModelSaveResult SaveLocked(
        ModelingModelChangeSet changes,
        Func<ModelingPropertySchemaRequest, ModelingPropertySchemaDto?>? resolver)
    {
        var sourceVersion = GetSourceVersion(changes.ModelId);
        var expectedVersion = ConflictArmed ? $"{sourceVersion}-next" : sourceVersion;
        ConflictArmed = false;
        if (!string.Equals(changes.BaseVersion, expectedVersion, StringComparison.Ordinal))
        {
            // Simulated concurrent write: the source moved on from the version the model was
            // loaded with. The editor shows the conflict alert with a reload action. The
            // returned model carries the CURRENT source version so the editor's next diff
            // bases its edit on the version the source is actually on (review fix, phase 14).
            var persistedNow = GetPersistedModel(changes.ModelId);
            persistedNow.Metadata.SourceVersion = sourceVersion;
            return new ModelingModelSaveResult
            {
                Succeeded = false,
                IsConflict = true,
                Message = $"The model changed in the meantime (source version {sourceVersion}, edit was based on {changes.BaseVersion}).",
                Model = ModelingOperationEngine.Clone(persistedNow)
            };
        }

        // Phase 5 (§5.4): the host may REJECT a stored entity reference the catalogue no longer
        // knows. The rejection is an issue on the element/relationship AND the field, and the
        // value stays pending in the editor so the user can re-pick.
        if (FindRejectedReference(changes) is { } rejection)
        {
            return rejection;
        }

        // The shared translator owns the application order AND the self-contradictory change
        // set check (an id in more than one of added/updated/removed applies nothing).
        var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);
        if (!translation.Success)
        {
            var sectionErrors = translation.Errors;
            return new ModelingModelSaveResult
            {
                Succeeded = false,
                Message = sectionErrors[0].Message,
                Issues = [.. sectionErrors.Select(ToIssue)]
            };
        }

        var persisted = ModelingOperationEngine.Clone(GetPersistedModel(changes.ModelId));
        var outcome = ModelingOperationEngine.Apply(
            persisted,
            translation.Batch,
            new ModelingOperationContext
            {
                RelationshipRules = _relationshipRules,
                ViewpointRules = _viewpointRules,
                ResolvePropertySchema = resolver,
                Policy = ModelingOperationPolicy.Authoring
            });

        if (!outcome.Success)
        {
            // All-or-nothing: the working copy is dropped, the persisted model is untouched.
            var errors = outcome.Errors;
            return new ModelingModelSaveResult
            {
                Succeeded = false,
                Message = errors[0].Message,
                Issues = [.. errors.Select(ToIssue)]
            };
        }

        var baseVersion = _baseVersions.TryGetValue(changes.ModelId, out var shipped) ? shipped : sourceVersion;
        var saveCount = (_saveCounts.TryGetValue(changes.ModelId, out var saves) ? saves : 0) + 1;
        _saveCounts[changes.ModelId] = saveCount;
        var nextVersion = $"{baseVersion.Split('-')[0]}-{saveCount}";
        _sourceVersions[changes.ModelId] = nextVersion;
        // The persisted model carries the NEW source version: the adopted result (and the
        // reloaded model) then base the next diff on the version the source moved to —
        // without it two consecutive saves would conflict on a stale version (review fix).
        persisted.Metadata.SourceVersion = nextVersion;
        _persistedByModelId[changes.ModelId] = persisted;

        return new ModelingModelSaveResult
        {
            Succeeded = true,
            Model = ModelingOperationEngine.Clone(persisted)
        };
    }

    /// <summary>
    /// Phase 5 (§5.4): scans the changed property values for typed entity references and refuses the
    /// save when the demo catalogue does not know one. Returns <c>null</c> when every reference
    /// resolves (or the change set carries none).
    /// </summary>
    private static ModelingModelSaveResult? FindRejectedReference(ModelingModelChangeSet changes)
    {
        // Review A16: ADDED items carry their property values on the add itself (the change set has
        // no separate follow-up update for them), so scanning only Elements/Relationships let a
        // brand-new step keep an unknown reference. Both collections are scanned.
        foreach (var added in changes.AddedElements)
        {
            foreach (var property in added.Properties)
            {
                if (IsRejected(property.Value))
                {
                    return Rejection(property.Key, added.Id, relationshipId: string.Empty);
                }
            }
        }

        foreach (var added in changes.AddedRelationships)
        {
            foreach (var property in added.Properties)
            {
                if (IsRejected(property.Value))
                {
                    return Rejection(property.Key, elementId: string.Empty, relationshipId: added.Id);
                }
            }
        }

        foreach (var change in changes.Elements)
        {
            if (change.SetProperties is null)
            {
                continue;
            }

            foreach (var property in change.SetProperties)
            {
                if (!IsRejected(property.Value))
                {
                    continue;
                }

                return Rejection(property.Key, change.ElementId, relationshipId: string.Empty);
            }
        }

        foreach (var change in changes.Relationships)
        {
            if (change.SetProperties is null)
            {
                continue;
            }

            foreach (var property in change.SetProperties)
            {
                if (!IsRejected(property.Value))
                {
                    continue;
                }

                return Rejection(property.Key, elementId: string.Empty, relationshipId: change.RelationshipId);
            }
        }

        return null;
    }

    private static bool IsRejected(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().Any(IsRejectedReferenceObject);
        }

        return IsRejectedReferenceObject(value);
    }

    private static bool IsRejectedReferenceObject(JsonElement value)
    {
        // Phase 5 review A10: the ONE reader of the stored {type, id, display, snapshotAt} shape — the
        // validator and the inspector parse it through the same record, so the writer can never
        // disagree about what counts as a reference.
        return ModelingEntityReference.TryParse(value, out var reference)
               && !DemoModelingLookupProvider.ContainsReference(reference.Type, reference.Id);
    }

    private static ModelingModelSaveResult Rejection(string propertyKey, string elementId, string relationshipId)
    {
        var issue = new ModelingIssueDto
        {
            Id = $"demo-modeling-reference-{propertyKey}",
            Severity = ModelingIssueSeverity.Error,
            Category = "property",
            SourceElementId = elementId,
            SourceRelationshipId = relationshipId,
            SuggestedFix = propertyKey,
            Message = $"The reference stored in '{propertyKey}' no longer exists in the catalogue. Pick another one."
        };

        return new ModelingModelSaveResult
        {
            Succeeded = false,
            Message = issue.Message,
            Issues = [issue]
        };
    }

    /// <summary>
    /// Maps one engine error onto a modeling issue. The id carries BOTH positions — the issue's
    /// position in this list (<paramref name="position"/>) and the operation index — because the
    /// post-batch re-validation can report SEVERAL errors for ONE operation index, and an id built
    /// from (index, code) alone would then collide and break the issue list's identity.
    /// </summary>
    private static ModelingIssueDto ToIssue(ModelingOperationError error, int position) => new()
    {
        Id = $"demo-modeling-save-{position}-{error.Index}-{error.Code}",
        Severity = ModelingIssueSeverity.Error,
        Category = "validation",
        SourceElementId = error.ElementId ?? string.Empty,
        SourceRelationshipId = error.RelationshipId ?? string.Empty,
        Message = error.Message,
        SuggestedFix = error.SuggestedFix ?? string.Empty
    };
}
