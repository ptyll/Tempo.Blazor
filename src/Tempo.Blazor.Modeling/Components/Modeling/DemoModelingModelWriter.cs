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
    private ModelingModelDto? _persisted;

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

    /// <summary>The source version the writer pretends the backend is on. The demo save-conflict
    /// scenario raises it so the next save reports <see cref="ModelingModelSaveResult.IsConflict"/>.</summary>
    public string SourceVersion { get; set; } = "2026.06";

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
    public ModelingModelDto PersistedModel
    {
        get
        {
            lock (_gate)
            {
                return _persisted ??= ModelingOperationEngine.Clone(DemoModelingModelProvider.CreateModel());
            }
        }
    }

    /// <summary>How many change sets were persisted (demo diagnostics).</summary>
    public int SaveCount { get; private set; }

    /// <inheritdoc />
    public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.Equals(changes.ModelId, DemoModelingModelProvider.ModelIdValue, StringComparison.Ordinal))
        {
            return Task.FromResult(new ModelingModelSaveResult
            {
                Succeeded = false,
                Message = $"Unknown model '{changes.ModelId}'."
            });
        }

        lock (_gate)
        {
            var expectedVersion = ConflictArmed ? $"{SourceVersion}-next" : SourceVersion;
            ConflictArmed = false;
            if (!string.Equals(changes.BaseVersion, expectedVersion, StringComparison.Ordinal))
            {
                // Simulated concurrent write: the source moved on from the version the model was
                // loaded with. The editor shows the conflict alert with a reload action. The
                // returned model carries the CURRENT source version so the editor's next diff
                // bases its edit on the version the source is actually on (review fix, phase 14).
                var persistedNow = PersistedModel;
                persistedNow.Metadata.SourceVersion = SourceVersion;
                return Task.FromResult(new ModelingModelSaveResult
                {
                    Succeeded = false,
                    IsConflict = true,
                    Message = $"The model changed in the meantime (source version {SourceVersion}, edit was based on {changes.BaseVersion}).",
                    Model = ModelingOperationEngine.Clone(persistedNow)
                });
            }

            // The shared translator owns the application order AND the self-contradictory change
            // set check (an id in more than one of added/updated/removed applies nothing).
            var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);
            if (!translation.Success)
            {
                var sectionErrors = translation.Errors;
                return Task.FromResult(new ModelingModelSaveResult
                {
                    Succeeded = false,
                    Message = sectionErrors[0].Message,
                    Issues = [.. sectionErrors.Select(ToIssue)]
                });
            }

            var persisted = ModelingOperationEngine.Clone(PersistedModel);
            var outcome = ModelingOperationEngine.Apply(
                persisted,
                translation.Batch,
                new ModelingOperationContext
                {
                    RelationshipRules = _relationshipRules,
                    ViewpointRules = _viewpointRules,
                    Policy = ModelingOperationPolicy.Authoring
                });

            if (!outcome.Success)
            {
                // All-or-nothing: the working copy is dropped, the persisted model is untouched.
                var errors = outcome.Errors;
                return Task.FromResult(new ModelingModelSaveResult
                {
                    Succeeded = false,
                    Message = errors[0].Message,
                    Issues = [.. errors.Select(ToIssue)]
                });
            }

            SourceVersion = $"2026.06-{++SaveCount}";
            // The persisted model carries the NEW source version: the adopted result (and the
            // reloaded model) then base the next diff on the version the source moved to —
            // without it two consecutive saves would conflict on a stale version (review fix).
            persisted.Metadata.SourceVersion = SourceVersion;
            _persisted = persisted;

            return Task.FromResult(new ModelingModelSaveResult
            {
                Succeeded = true,
                Model = ModelingOperationEngine.Clone(persisted)
            });
        }
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
