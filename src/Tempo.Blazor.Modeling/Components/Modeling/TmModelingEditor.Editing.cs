using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The Fáze 14 editing half of <see cref="TmModelingEditor"/> (decision
/// <c>MODELING-INSPECTOR-EDITING</c>): the editor-owned <see cref="DiagramCommandStack"/> (one
/// <see cref="DiagramCommandTransaction"/> per field commit), the pending change set computed as a
/// diff against the loaded baseline, the Manual/Immediate save modes over
/// <see cref="IModelingModelWriter"/>, the conflict alert and the undo/redo surface. Without
/// <see cref="AllowModelEditing"/> (and a writer for the provider key or an
/// <see cref="OnModelChanged"/> delegate) every path here stays inert — MO2 behavior.
/// <para>
/// The pending set is a DIFF, not an accumulator: undo/redo, a discarded edit or a reload keep it
/// consistent without field-by-field bookkeeping, because it is recomputed from the live model
/// against the baseline snapshot (refreshed after every successful save / adopted model).
/// </para>
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Writers registered for the modeling providers (resolved by
    /// <see cref="IModelingModelWriter.ProviderKey"/>).</summary>
    [Inject] public IEnumerable<IModelingModelWriter> ModelWriters { get; set; } = [];

    /// <summary>
    /// Turns the inspector's editing mode on (Fáze 14). Editing is active only when a
    /// <see cref="IModelingModelWriter"/> is registered for the effective provider key OR an
    /// <see cref="OnModelChanged"/> delegate is bound — otherwise the inspector stays read-only
    /// (MO2) regardless of this flag.
    /// </summary>
    [Parameter] public bool AllowModelEditing { get; set; }

    /// <summary>How edits reach the writer: <see cref="ModelingSaveMode.Manual"/> (default)
    /// aggregates the pending change set until <see cref="SaveModelChangesAsync"/> (or the
    /// inspector's Save button) is invoked; <see cref="ModelingSaveMode.Immediate"/> calls the
    /// writer after every field commit.</summary>
    [Parameter] public ModelingSaveMode SaveMode { get; set; } = ModelingSaveMode.Manual;

    /// <summary>Raised after every committed field edit with the change set of that commit and
    /// the current model. Binding this delegate alone (without a writer) makes the editor own the
    /// persistence — the host writes the change set itself.</summary>
    [Parameter] public EventCallback<ModelingModelChangedEventArgs> OnModelChanged { get; set; }

    /// <summary>Raised after a completed save (success, conflict or failure).</summary>
    [Parameter] public EventCallback OnSaveCompleted { get; set; }

    private DiagramCommandStack? _commandStack;
    private (ModelingModelDto? Model, string? ProviderKey) _baselineKey;
    private Dictionary<string, ElementBaseline> _baselineElements = new(StringComparer.Ordinal);
    private Dictionary<string, RelationshipBaseline> _baselineRelationships = new(StringComparer.Ordinal);
    private ModelingModelSaveResult? _lastSaveResult;
    private bool _isSaving;
    private bool _discardConfirmationOpen;
    private Func<Task>? _pendingDiscardAction;
    private string? _historyAnnouncement;

    /// <summary>
    /// The edit-generation counter (review fix, phase 14): bumped by every commit, undo and redo
    /// — the save race guard compares it across the writer await, and the pending-set cache keys
    /// on it (the model is mutated IN PLACE, so a reference check cannot detect an edit).
    /// </summary>
    private int _commitVersion;

    /// <summary>Whether an Immediate-mode commit landed while a save was in flight; the save
    /// loop consumes the flag after the writer settles and runs the follow-up save.</summary>
    private bool _saveQueued;

    /// <summary>The cached pending change set and the (commit version, model, provider) key it
    /// was computed for — the diff is an O(model) pass, and the render path (pending indicator,
    /// mobile action items, save button) reads it several times per render.</summary>
    private ModelingModelChangeSet? _pendingChangeSetCache;
    private (int CommitVersion, ModelingModelDto? Model, string? ProviderKey) _pendingChangeSetCacheKey;

    /// <summary>Test seam (InternalsVisibleTo): the generated document the preview renders.</summary>
    internal DiagramDocument? TestDocument => _document;

    /// <summary>Test seam (InternalsVisibleTo): the loaded model.</summary>
    internal ModelingModelDto? TestModel => _model;

    /// <summary>Test seam (InternalsVisibleTo): the open-editor dialog document.</summary>
    internal DiagramDocument? TestOpenedEditorDocument => _openedEditorDocument;

    /// <summary>Test seam (InternalsVisibleTo): the editor-owned undo/redo stack.</summary>
    internal IDiagramCommandStack? TestStack => _commandStack;

    /// <summary>The editor-owned undo/redo stack (created lazily; cleared on model reload).</summary>
    private DiagramCommandStack CommandStack => _commandStack ??= new DiagramCommandStack();

    /// <summary>The writer registered for the effective provider key, if any.</summary>
    private IModelingModelWriter? ResolvedWriter
        => ModelWriters.FirstOrDefault(writer =>
            string.Equals(writer.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal));

    /// <summary>
    /// Whether editing is active: the host opted in AND at least one write path exists (a writer
    /// for the provider key or an <see cref="OnModelChanged"/> delegate). Without it the
    /// inspector stays read-only (MO2).
    /// </summary>
    private bool IsEditingEnabled
        => AllowModelEditing && _state == ModelingEditorState.Loaded
            && (ResolvedWriter is not null || OnModelChanged.HasDelegate);

    /// <summary>Whether unsaved edits are waiting (a Manual save, a reload or a provider switch
    /// will surface a confirmation). Public for hosts that drive a navigation guard.</summary>
    public bool HasPendingModelChanges => ComputePendingChangeSet() is { } pending
        && (pending.Elements.Count > 0 || pending.Relationships.Count > 0);

    /// <summary>The localized transaction name for element edits.</summary>
    private string EditElementTransactionName => Loc["TmModelingEditor_EditElement"];

    /// <summary>The localized transaction name for relationship edits.</summary>
    private string EditRelationshipTransactionName => Loc["TmModelingEditor_EditRelationship"];

    // ── Baseline + pending diff ─────────────────────────────────────────────

    private sealed record ElementBaseline(string Name, string Description, Dictionary<string, JsonElement> Properties);

    private sealed record RelationshipBaseline(string? Name, Dictionary<string, JsonElement> Properties);

    /// <summary>
    /// Captures the baseline snapshot the pending set is diffed against: the model AS LOADED (or
    /// as last saved/adopted). Per-element name/description/properties and per-relationship
    /// name/properties are snapshotted; the rest of the DTO is not editable through the inspector.
    /// </summary>
    private void CaptureBaseline(ModelingModelDto model)
    {
        _baselineElements = model.Elements.ToDictionary(
            element => element.Id,
            element => new ElementBaseline(
                element.Name,
                element.Description,
                new Dictionary<string, JsonElement>(element.Properties, StringComparer.Ordinal)),
            StringComparer.Ordinal);
        _baselineRelationships = model.Relationships.ToDictionary(
            relationship => relationship.Id,
            relationship => new RelationshipBaseline(
                relationship.Name,
                new Dictionary<string, JsonElement>(relationship.Properties, StringComparer.Ordinal)),
            StringComparer.Ordinal);
        _baselineKey = (model, EffectiveProviderKey);
        // A new baseline changes the diff inputs without bumping the commit version — the
        // cached pending set must go.
        _pendingChangeSetCache = null;
    }

    /// <summary>
    /// The pending change set: the diff of the LIVE model against the baseline. Returns null when
    /// nothing differs (or no baseline was captured for the current model + provider pair).
    /// Cached on the commit version + model + provider triple — a commit, an undo/redo or a
    /// baseline change invalidates it, an unrelated re-render reuses it.
    /// </summary>
    private ModelingModelChangeSet? ComputePendingChangeSet()
    {
        if (_model is null || _baselineKey.Model != _model
            || !string.Equals(_baselineKey.ProviderKey, EffectiveProviderKey, StringComparison.Ordinal))
        {
            return null;
        }

        var cacheKey = (_commitVersion, _model, EffectiveProviderKey);
        if (_pendingChangeSetCache is not null && _pendingChangeSetCacheKey.Equals(cacheKey))
        {
            return _pendingChangeSetCache;
        }

        var changeSet = BuildPendingChangeSet(_model);
        _pendingChangeSetCache = changeSet;
        _pendingChangeSetCacheKey = cacheKey;
        return changeSet;
    }

    private ModelingModelChangeSet? BuildPendingChangeSet(ModelingModelDto model)
    {
        var changeSet = new ModelingModelChangeSet
        {
            ModelId = model.Id,
            ProviderKey = EffectiveProviderKey ?? string.Empty,
            BaseVersion = model.Metadata.SourceVersion
        };

        foreach (var element in model.Elements)
        {
            if (!_baselineElements.TryGetValue(element.Id, out var baseline))
            {
                continue;
            }

            var change = new ModelingElementChange { ElementId = element.Id };
            if (!string.Equals(element.Name, baseline.Name, StringComparison.Ordinal))
            {
                change.Name = element.Name;
            }

            if (!string.Equals(element.Description, baseline.Description, StringComparison.Ordinal))
            {
                change.Description = element.Description;
            }

            var (setProperties, removedProperties) = DiffProperties(baseline.Properties, element.Properties);
            if (setProperties is not null)
            {
                change.SetProperties = setProperties;
            }

            if (removedProperties is not null)
            {
                change.RemovedProperties = removedProperties;
            }

            if (change.Name is not null || change.Description is not null
                || change.SetProperties is not null || change.RemovedProperties is not null)
            {
                changeSet.Elements.Add(change);
            }
        }

        foreach (var relationship in model.Relationships)
        {
            if (!_baselineRelationships.TryGetValue(relationship.Id, out var baseline))
            {
                continue;
            }

            var change = new ModelingRelationshipChange { RelationshipId = relationship.Id };
            if (!string.Equals(relationship.Name, baseline.Name, StringComparison.Ordinal))
            {
                change.Name = relationship.Name;
            }

            var (setProperties, removedProperties) = DiffProperties(baseline.Properties, relationship.Properties);
            if (setProperties is not null)
            {
                change.SetProperties = setProperties;
            }

            if (removedProperties is not null)
            {
                change.RemovedProperties = removedProperties;
            }

            if (change.Name is not null || change.SetProperties is not null || change.RemovedProperties is not null)
            {
                changeSet.Relationships.Add(change);
            }
        }

        return changeSet;
    }

    private static (Dictionary<string, JsonElement>? Set, List<string>? Removed) DiffProperties(
        IReadOnlyDictionary<string, JsonElement> baseline,
        IReadOnlyDictionary<string, JsonElement> current)
    {
        Dictionary<string, JsonElement>? set = null;
        List<string>? removed = null;

        foreach (var property in current)
        {
            if (!baseline.TryGetValue(property.Key, out var original) || !JsonElementEquals(original, property.Value))
            {
                set ??= new Dictionary<string, JsonElement>();
                set[property.Key] = property.Value.Clone();
            }
        }

        foreach (var key in baseline.Keys)
        {
            if (!current.ContainsKey(key))
            {
                removed ??= [];
                removed.Add(key);
            }
        }

        return (set, removed);
    }

    /// <summary>JSON-kind-preserving deep comparison (the .NET 8 multi-target has no
    /// <c>JsonElement.DeepEquals</c>).</summary>
    private static bool JsonElementEquals(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind)
        {
            return false;
        }

        return left.ValueKind switch
        {
            JsonValueKind.Object => left.EnumerateObject().SequenceEqual(
                right.EnumerateObject(),
                new JsonPropertyEqualityComparer()),
            JsonValueKind.Array => left.GetArrayLength() == right.GetArrayLength()
                && left.EnumerateArray().Zip(right.EnumerateArray(), JsonElementEquals).All(equal => equal),
            // Number/True/False/Null/String compare by their raw JSON text — GetString() would
            // throw on the non-string kinds.
            _ => string.Equals(left.GetRawText(), right.GetRawText(), StringComparison.Ordinal)
        };
    }

    private sealed class JsonPropertyEqualityComparer : IEqualityComparer<JsonProperty>
    {
        public bool Equals(JsonProperty x, JsonProperty y)
            => string.Equals(x.Name, y.Name, StringComparison.Ordinal) && JsonElementEquals(x.Value, y.Value);

        public int GetHashCode(JsonProperty obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }

    // ── Commit paths ─────────────────────────────────────────────────────────

    /// <summary>
    /// Commits one field edit: wraps the change in a ONE-command
    /// <see cref="DiagramCommandTransaction"/>, applies it to the model and the documents and
    /// (immediate mode) saves. The editor raises <see cref="OnModelChanged"/> with the change set
    /// of the commit and the current model.
    /// </summary>
    private async Task CommitElementEditAsync(ModelingElementChange change)
    {
        if (_model is null || !IsEditingEnabled)
        {
            return;
        }

        var element = _model.Elements.FirstOrDefault(item => string.Equals(item.Id, change.ElementId, StringComparison.Ordinal));
        if (element is null)
        {
            return;
        }

        CommandStack.Push(new UpdateModelingElementCommand(
            EditElementTransactionName,
            element,
            change.Name,
            change.Description,
            change.SetProperties,
            change.RemovedProperties,
            DocumentsForSync));

        // One edit generation per commit — the save race guard and the pending-set cache key on it.
        unchecked { _commitVersion++; }

        await AfterCommitAsync(new ModelingModelChangeSet
        {
            ModelId = _model.Id,
            ProviderKey = EffectiveProviderKey ?? string.Empty,
            BaseVersion = _model.Metadata.SourceVersion,
            Elements = [change]
        });
    }

    /// <summary>Commits one relationship field edit (same one-transaction rule).</summary>
    private async Task CommitRelationshipEditAsync(ModelingRelationshipChange change)
    {
        if (_model is null || !IsEditingEnabled)
        {
            return;
        }

        var relationship = _model.Relationships.FirstOrDefault(item => string.Equals(item.Id, change.RelationshipId, StringComparison.Ordinal));
        if (relationship is null)
        {
            return;
        }

        CommandStack.Push(new UpdateModelingRelationshipCommand(
            EditRelationshipTransactionName,
            relationship,
            change.Name,
            change.SetProperties,
            change.RemovedProperties,
            DocumentsForSync));

        unchecked { _commitVersion++; }

        await AfterCommitAsync(new ModelingModelChangeSet
        {
            ModelId = _model.Id,
            ProviderKey = EffectiveProviderKey ?? string.Empty,
            BaseVersion = _model.Metadata.SourceVersion,
            Relationships = [change]
        });
    }

    /// <summary>
    /// The document resolver a commit hands to its command: evaluated on EVERY Execute/Undo so
    /// the sync targets the documents that exist at THAT moment — a preview regeneration or an
    /// adopted model replaces the <see cref="DiagramDocument"/> instance, and a snapshot list
    /// taken at push time would sync a dead document (review fix, phase 14).
    /// </summary>
    private IEnumerable<DiagramDocument> DocumentsForSync()
    {
        if (_document is not null)
        {
            yield return _document;
        }

        if (_openedEditorDocument is not null && !ReferenceEquals(_openedEditorDocument, _document))
        {
            yield return _openedEditorDocument;
        }
    }

    private async Task AfterCommitAsync(ModelingModelChangeSet commitChangeSet)
    {
        // The command mutated the model and the documents in place. A render propagates the new
        // name/label into the inspector and the canvases; the TREE projection is invalidated so
        // its render gate (keyed on the Elements array identity) sees a fresh array and repaints.
        InvalidateTreeProjection();
        StateHasChanged();

        // The editor stack owns Ctrl+Z/Ctrl+Shift+Z/Ctrl+Y inside the editor root once editing
        // is live — the JS guard stops the browser's DOCUMENT-level undo from double-firing on
        // the same keystroke outside text fields (review fix, phase 14).
        if (!_undoShortcutBridgeRegistered)
        {
            _undoShortcutBridgeRegistered = true;
            await Interop.InvokeVoidSafeAsync("registerUndoShortcutBridge", _editorRoot);
        }

        await OnModelChanged.InvokeAsync(new ModelingModelChangedEventArgs
        {
            ChangeSet = commitChangeSet,
            Model = _model!
        });

        if (SaveMode == ModelingSaveMode.Immediate)
        {
            if (_isSaving)
            {
                // A save is already in flight: re-entering would no-op and drop the edit. Queue
                // it — the save loop runs the follow-up save once the writer settles.
                _saveQueued = true;
            }
            else
            {
                await SaveModelChangesAsync();
            }
        }
    }

    // ── Undo / redo ──────────────────────────────────────────────────────────

    /// <summary>Undoes the last committed edit (model + documents in one step) and re-renders.</summary>
    public Task UndoModelEditAsync()
    {
        if (_commandStack is { CanUndo: true })
        {
            var name = _commandStack.NextUndoName;
            _commandStack.Undo();
            unchecked { _commitVersion++; }
            _historyAnnouncement = string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingEditor_UndoAction"], name ?? string.Empty);
            InvalidateTreeProjection();
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    /// <summary>Redoes the last undone edit and re-renders.</summary>
    public Task RedoModelEditAsync()
    {
        if (_commandStack is { CanRedo: true })
        {
            var name = _commandStack.NextRedoName;
            _commandStack.Redo();
            unchecked { _commitVersion++; }
            _historyAnnouncement = string.Format(CultureInfo.CurrentUICulture, Loc["TmModelingEditor_RedoAction"], name ?? string.Empty);
            InvalidateTreeProjection();
            StateHasChanged();
        }

        return Task.CompletedTask;
    }

    /// <summary>Whether the editor's own stack can undo (drives the header buttons).</summary>
    public bool CanUndoModelEdits => _commandStack?.CanUndo ?? false;

    /// <summary>Whether the editor's own stack can redo.</summary>
    public bool CanRedoModelEdits => _commandStack?.CanRedo ?? false;

    /// <summary>The last history announcement for the inspector's live region (or null).</summary>
    internal string? TestHistoryAnnouncement => _historyAnnouncement;

    // ── Save + conflict ──────────────────────────────────────────────────────

    /// <summary>
    /// Saves the pending change set through the writer. In Manual mode the aggregated diff is
    /// sent once and the pending state clears on success; in Immediate mode a commit that raced
    /// an in-flight save queues itself and this method loops after the writer settles. A
    /// conflict result keeps the pending changes and shows the alert; a failed save shows the
    /// failure message. Returns the writer's result, or null when there was nothing to save / no
    /// writer is registered.
    /// <para>
    /// Race safety (review fix, phase 14): the load version, the model reference and the commit
    /// version are captured BEFORE the writer await. After it, a load or a model swap DISCARDS
    /// the result (the state it was computed from is gone); a successful save advances the
    /// baseline by applying the SENT change set onto the baseline snapshot — never the live
    /// model — and adopts the returned model only when no commit raced the save (else the live
    /// model and the undo stack stay, with the racing edits still pending).
    /// </para>
    /// </summary>
    public async Task<ModelingModelSaveResult?> SaveModelChangesAsync()
    {
        if (_isSaving || _model is null || !IsEditingEnabled)
        {
            return null;
        }

        var changeSet = ComputePendingChangeSet();
        if (changeSet is null || (changeSet.Elements.Count == 0 && changeSet.Relationships.Count == 0))
        {
            return null;
        }

        var writer = ResolvedWriter;
        if (writer is null)
        {
            // A host that only binds OnModelChanged writes the change set itself; the pending
            // state clears so the next diff starts from the delivered state. The completed
            // event still raises — the host drives its own persistence and needs the settled
            // signal (review fix, phase 14).
            CaptureBaseline(_model);
            await OnSaveCompleted.InvokeAsync();
            StateHasChanged();
            return null;
        }

        var loadVersionAtStart = _loadVersion;
        var modelAtStart = _model;
        var commitVersionAtStart = _commitVersion;

        _isSaving = true;
        StateHasChanged();
        ModelingModelSaveResult result;
        try
        {
            result = await writer.SaveChangesAsync(changeSet, CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            _isSaving = false;
            StateHasChanged();
            return null;
        }
        catch (Exception ex)
        {
            result = new ModelingModelSaveResult { Succeeded = false, Message = ex.Message };
        }

        _isSaving = false;
        var stateUnchanged = loadVersionAtStart == _loadVersion && ReferenceEquals(modelAtStart, _model);

        if (stateUnchanged)
        {
            _lastSaveResult = result;
            if (result.Succeeded)
            {
                if (result.Model is not null && commitVersionAtStart == _commitVersion)
                {
                    await AdoptModelAsync(result.Model);
                }
                else
                {
                    // The baseline advances by the SENT change set — applied onto the baseline
                    // snapshot, never the live model (a racing edit must stay pending).
                    ApplyChangeSetToBaseline(changeSet);
                }

                if (commitVersionAtStart == _commitVersion)
                {
                    _commandStack?.Clear();
                    _historyAnnouncement = Loc["TmModelingEditor_Saved"];
                }
            }

            await OnSaveCompleted.InvokeAsync();
        }

        StateHasChanged();

        if (SaveMode == ModelingSaveMode.Immediate && _saveQueued)
        {
            _saveQueued = false;
            if (stateUnchanged)
            {
                await SaveModelChangesAsync();
            }
        }

        return result;
    }

    /// <summary>
    /// Applies a SENT change set onto the BASELINE snapshot (review fix, phase 14): the baseline
    /// becomes "the state the source now has" without touching the live model, so a commit that
    /// raced the save stays pending and undoable against the advanced baseline.
    /// </summary>
    private void ApplyChangeSetToBaseline(ModelingModelChangeSet changeSet)
    {
        foreach (var elementChange in changeSet.Elements)
        {
            if (!_baselineElements.TryGetValue(elementChange.ElementId, out var elementBaseline))
            {
                continue;
            }

            var properties = new Dictionary<string, JsonElement>(elementBaseline.Properties, StringComparer.Ordinal);
            if (elementChange.SetProperties is not null)
            {
                foreach (var property in elementChange.SetProperties)
                {
                    properties[property.Key] = property.Value.Clone();
                }
            }

            if (elementChange.RemovedProperties is not null)
            {
                foreach (var key in elementChange.RemovedProperties)
                {
                    properties.Remove(key);
                }
            }

            _baselineElements[elementChange.ElementId] = new ElementBaseline(
                elementChange.Name ?? elementBaseline.Name,
                elementChange.Description ?? elementBaseline.Description,
                properties);
        }

        foreach (var relationshipChange in changeSet.Relationships)
        {
            if (!_baselineRelationships.TryGetValue(relationshipChange.RelationshipId, out var relationshipBaseline))
            {
                continue;
            }

            var properties = new Dictionary<string, JsonElement>(relationshipBaseline.Properties, StringComparer.Ordinal);
            if (relationshipChange.SetProperties is not null)
            {
                foreach (var property in relationshipChange.SetProperties)
                {
                    properties[property.Key] = property.Value.Clone();
                }
            }

            if (relationshipChange.RemovedProperties is not null)
            {
                foreach (var key in relationshipChange.RemovedProperties)
                {
                    properties.Remove(key);
                }
            }

            _baselineRelationships[relationshipChange.RelationshipId] = new RelationshipBaseline(
                relationshipChange.Name ?? relationshipBaseline.Name,
                properties);
        }

        _pendingChangeSetCache = null;
    }

    /// <summary>Replaces the loaded model with a saved/restored one: regenerates the preview,
    /// remaps the selection by Id and captures the new baseline (the pending set becomes empty).
    /// The open-editor dialog — if any — closes through the regular close path so the focus
    /// restore contract runs (never a silent null).</summary>
    private async Task AdoptModelAsync(ModelingModelDto model)
    {
        // The DTO references belong to the REPLACED model instance — remap the selection into the
        // new model by Id (null when the element/relationship is gone), or every comparison on
        // the stale references (draft seeding, the related-elements panel) goes dead.
        var selectedElementId = _selectedElement?.Id;
        var selectedRelationshipId = _selectedRelationship?.Id;
        var draggedElementId = _draggedElement?.Id;

        _model = model;
        _selectedElement = RemapElement(model, selectedElementId);
        _selectedRelationship = RemapRelationship(model, selectedRelationshipId);
        _draggedElement = RemapElement(model, draggedElementId);

        var result = GenerateDiagram(model);
        _document = result.Document;
        _issues = result.Issues;
        _completedGenerationCount++;
        InvalidateTreeProjection();
        await CloseGeneratedDiagramAsync();
        CaptureBaseline(model);
        _lastSaveResult = null;
    }

    private static ModelingElementDto? RemapElement(ModelingModelDto model, string? elementId)
        => string.IsNullOrWhiteSpace(elementId)
            ? null
            : model.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));

    private static ModelingRelationshipDto? RemapRelationship(ModelingModelDto model, string? relationshipId)
        => string.IsNullOrWhiteSpace(relationshipId)
            ? null
            : model.Relationships.FirstOrDefault(item => string.Equals(item.Id, relationshipId, StringComparison.Ordinal));

    /// <summary>Whether the document-level undo/redo shortcut guard was registered for the
    /// editor root (once per component instance; the JS side is also idempotent per element).</summary>
    private bool _undoShortcutBridgeRegistered;

    /// <summary>Whether a save is currently in flight (the saving indicator).</summary>
    private bool IsSaving => _isSaving;

    /// <summary>
    /// The editing generation the issue panels receive (review fix, phase 14): the model DTOs
    /// mutate IN PLACE, so the panels' render gates and memo keys (which compare collection
    /// references) need a counter to invalidate after a rename/undo/redo.
    /// </summary>
    private int EditContentVersion => _commitVersion;

    /// <summary>Whether the conflict alert with the reload action is showing.</summary>
    private bool ShowConflictAlert => _lastSaveResult is { IsConflict: true };

    /// <summary>Whether a failed (non-conflict) save message is showing.</summary>
    private bool ShowSaveFailure => _lastSaveResult is { Succeeded: false, IsConflict: false };

    /// <summary>The last save result the alert renders (null hides both alerts).</summary>
    internal ModelingModelSaveResult? TestLastSaveResult => _lastSaveResult;

    // ── Discard / reload guard ───────────────────────────────────────────────

    /// <summary>Handles the conflict alert's "Reload" action: asks for confirmation (the pending
    /// changes are discarded) and then reloads through the regular path. Blocked while a save is
    /// in flight — a reload would make the in-flight result stale (review fix, phase 14).</summary>
    private Task RequestConflictReloadAsync() => _isSaving
        ? Task.CompletedTask
        : RequestDiscardThenAsync(DiscardPendingEditsAndReloadAsync);

    private async Task DiscardPendingEditsAndReloadAsync()
    {
        _commandStack?.Clear();
        _lastSaveResult = null;
        _activeLoadKey = null;
        await LoadModelAsync(force: true);
        if (_model is not null)
        {
            CaptureBaseline(_model);
        }
    }

    /// <summary>
    /// Guards a destructive path with the discard confirmation when pending changes exist.
    /// <paramref name="action"/> runs only after the user confirms (or immediately when there is
    /// nothing pending).
    /// </summary>
    private Task RequestDiscardThenAsync(Func<Task> action)
    {
        if (!HasPendingModelChanges)
        {
            return action();
        }

        _pendingDiscardAction = action;
        _discardConfirmationOpen = true;
        return Task.CompletedTask;
    }

    private async Task ConfirmDiscardAsync()
    {
        _discardConfirmationOpen = false;
        var action = _pendingDiscardAction;
        _pendingDiscardAction = null;
        if (action is null)
        {
            return;
        }

        await action();
    }

    private Task CancelDiscardAsync()
    {
        _discardConfirmationOpen = false;
        _pendingDiscardAction = null;
        return Task.CompletedTask;
    }

    /// <summary>Whether the discard-confirmation modal is open.</summary>
    private bool DiscardConfirmationOpen => _discardConfirmationOpen;

    /// <summary>The inspector header's Save button (docked bands): saves the pending set.</summary>
    private async Task SaveFromInspectorAsync() => await SaveModelChangesAsync();

    /// <summary>The inspector header's Discard button: asks for confirmation, then undoes the
    /// pending transactions (the baseline diff recomputes to empty).</summary>
    private Task DiscardFromInspectorAsync() => RequestDiscardThenAsync(DiscardPendingEditsAsync);

    private Task DiscardPendingEditsAsync()
    {
        if (_commandStack is null)
        {
            return Task.CompletedTask;
        }

        // Undo until the baseline is reached — each undo is one committed field edit.
        while (_commandStack.CanUndo && HasPendingModelChanges)
        {
            _commandStack.Undo();
        }

        _commandStack.Clear();
        _lastSaveResult = null;
        InvalidateTreeProjection();
        StateHasChanged();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Drops the cached tree projection so the next render rebuilds it (a NEW array instance —
    /// the tree's render gate is keyed on the array identity, so an in-place model edit must
    /// invalidate the memoized VisibleTreeElements).
    /// </summary>
    private void InvalidateTreeProjection()
    {
        _visibleTreeElementsSource = null;
        _visibleTreeElements = [];
    }

    /// <summary>Clears the editing state on a model switch (reload, provider change): the stack
    /// and the baseline belong to the loaded model.</summary>
    private void ResetEditingState()
    {
        _commandStack?.Clear();
        _baselineKey = default;
        _baselineElements = new Dictionary<string, ElementBaseline>(StringComparer.Ordinal);
        _baselineRelationships = new Dictionary<string, RelationshipBaseline>(StringComparer.Ordinal);
        _lastSaveResult = null;
        _discardConfirmationOpen = false;
        _pendingDiscardAction = null;
        _historyAnnouncement = null;
        _saveQueued = false;
        _pendingChangeSetCache = null;
    }
}

/// <summary>Event payload of <see cref="TmModelingEditor.OnModelChanged"/>: the change set of the
/// commit plus the current model (Fáze 14).</summary>
public sealed class ModelingModelChangedEventArgs
{
    /// <summary>The committed change set (only the fields of that commit are set).</summary>
    public ModelingModelChangeSet ChangeSet { get; set; } = new();

    /// <summary>The current model after the commit.</summary>
    public ModelingModelDto Model { get; set; } = new();
}

/// <summary>How committed edits reach the <see cref="IModelingModelWriter"/> (Fáze 14).</summary>
public enum ModelingSaveMode
{
    /// <summary>Edits aggregate into the pending change set until an explicit save.</summary>
    Manual,

    /// <summary>Every field commit saves immediately through the writer.</summary>
    Immediate
}
