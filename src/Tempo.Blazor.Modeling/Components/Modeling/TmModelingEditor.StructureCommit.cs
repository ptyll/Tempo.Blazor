using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The COMMIT half of the phase-3 structure surface of <see cref="TmModelingEditor"/> — the shared
/// plumbing every structural command goes through (clone validation, one transaction, the view seed
/// of a viewless model, the change-set intent), the small lookups the commands need and the polite
/// announcements the structural live region speaks. Split from
/// <c>TmModelingEditor.Structure.cs</c> (the document diff + the insert/connect/delete gestures) to
/// respect the 1200-line source ceiling.
/// </summary>
public partial class TmModelingEditor
{
    // ── Shared commit plumbing ───────────────────────────────────────────────

    /// <summary>
    /// Validates a structural batch on a CLONE and, when the engine accepts it, pushes the commands
    /// as ONE transaction (one undo step), regenerates the documents from the model and raises the
    /// regular commit contract. A rejection never touches the model, the documents or the undo stack.
    /// <para>
    /// A viewless model gets the view seed PREPENDED to the same transaction (phase 3 review, A1):
    /// its operations enter the clone-validation batch first and its view changes join the intent,
    /// so the writer receives the complete scene (existing nodes + the inserted one) and ONE undo
    /// removes the insert AND the view it created.
    /// </para>
    /// <para>
    /// Phase 4: a LAYOUT commit passes <paramref name="regenerate"/> = <c>false</c> — the document
    /// already carries the gesture's geometry, and rebuilding it would reset the canvas zoom and
    /// selection on every move. The transaction still enters the stack (so undo/redo regenerate).
    /// </para>
    /// </summary>
    private async Task PushStructuralCommandsAsync(
        string transactionName,
        IReadOnlyList<IDiagramCommand> commands,
        ModelingOperationBatch batch,
        ModelingModelChangeSet intent,
        Action? afterCommit,
        bool seedView = true,
        bool regenerate = true)
    {
        if (_model is null)
        {
            return;
        }

        // Any other command breaks the keyboard-nudge burst (the layout path re-arms it after the
        // push when the gesture is still coalescable).
        _coalescedLayoutCommand = null;

        var seed = seedView ? CreateViewSeedCommand(transactionName) : null;
        if (seed is not null)
        {
            batch.Operations.InsertRange(0, seed.Batch.Operations);
            MergeIntentViewChange(intent, seed.Intent.ViewChanges[0]);
        }

        if (!ValidateStructureBatch(batch))
        {
            RegenerateDocumentsAfterStructureChange();
            return;
        }

        var transaction = new DiagramCommandTransaction(transactionName);
        if (seed is not null)
        {
            transaction.Add(seed);
        }

        foreach (var command in commands)
        {
            transaction.Add(command);
        }

        ClearStructureError();
        CommandStack.Push(transaction);
        _hasStructuralCommands = true;
        unchecked { _commitVersion++; }
        afterCommit?.Invoke();
        if (regenerate)
        {
            RegenerateDocumentsAfterStructureChange();
        }

        await AfterCommitAsync(intent);
    }

    /// <summary>
    /// The view seed a VIEWLESS model needs before its first structural commit (A1): one view node
    /// per rendered node plus one connection per rendered relationship edge. Null when the model
    /// already has a view (or the document has nothing to seed — the insert's own
    /// <c>set_view_node</c> then creates the default view).
    /// </summary>
    private SeedModelingViewCommand? CreateViewSeedCommand(string transactionName)
    {
        if (_model is null || _model.Views.Count > 0)
        {
            return null;
        }

        var seed = new SeedModelingViewCommand(
            transactionName,
            _model,
            CreateOperationContext(),
            _document,
            [.. _model.Relationships.Select(relationship => relationship.Id)]);
        return seed.HasOperations ? seed : null;
    }

    /// <summary>Folds the seed's view change into the intent's entry for the same view (one
    /// <c>ViewChanges</c> entry per view, seeded nodes first).</summary>
    private static void MergeIntentViewChange(ModelingModelChangeSet intent, ModelingViewChange seedChange)
    {
        var target = intent.ViewChanges.FirstOrDefault(change => string.Equals(change.ViewId, seedChange.ViewId, StringComparison.Ordinal));
        if (target is null)
        {
            intent.ViewChanges.Insert(0, seedChange);
            return;
        }

        target.Nodes.InsertRange(0, seedChange.Nodes.Where(node => target.Nodes.All(existing => !string.Equals(existing.ElementId, node.ElementId, StringComparison.Ordinal))));
        target.Connections.InsertRange(0, seedChange.Connections.Where(connection => target.Connections.All(existing => !string.Equals(existing.RelationshipId, connection.RelationshipId, StringComparison.Ordinal))));
    }

    /// <summary>Runs the batch on a CLONE; on failure it publishes the engine's first error (its
    /// message is the one <c>modeling_apply_operations</c> would return) and returns false.</summary>
    private bool ValidateStructureBatch(ModelingOperationBatch batch)
    {
        if (_model is null)
        {
            return false;
        }

        var outcome = ModelingOperationEngine.Apply(ModelingOperationEngine.Clone(_model), batch, CreateOperationContext());
        if (outcome.Success)
        {
            return true;
        }

        var error = outcome.Errors[0];
        ShowStructureError(
            Loc["TmModelingEditor_StructureRejectedTitle"],
            error.Message,
            error.SuggestedFix);
        return false;
    }

    /// <summary>
    /// Rebuilds the preview and the open-editor document from the model after a structural change
    /// (the generator is the ONE projection, so a semantic insert/delete can never leave a diagram
    /// node behind). The documents become a NEW identity — the embedded editors re-adopt them.
    /// </summary>
    internal void RegenerateDocumentsAfterStructureChange()
    {
        if (_model is null)
        {
            CaptureStructureSnapshot(_document);
            return;
        }

        var result = GenerateDiagram(_model);
        _document = result.Document;
        _issues = result.Issues;
        _completedGenerationCount++;

        if (_openedEditorDocument is not null)
        {
            _openedEditorDocument = _document;
        }

        if (_selectedElement is not null
            && _model.Elements.All(element => !ReferenceEquals(element, _selectedElement)))
        {
            _selectedElement = null;
        }

        if (_selectedRelationship is not null
            && _model.Relationships.All(relationship => !ReferenceEquals(relationship, _selectedRelationship)))
        {
            _selectedRelationship = null;
        }

        CaptureStructureSnapshot(_document);
        InvalidateTreeProjection();

        // The diagram changed shape: refit the docked preview to its box after the render, so an
        // inserted element is visible where the user is looking (the mobile band owns its own
        // floating zoom pill and keeps the user's scale).
        if (!IsMobileLayout)
        {
            _refitPreviewAfterRender = true;
        }
    }

    // ── Small helpers ────────────────────────────────────────────────────────

    private bool TryGetElement(string elementId, out ModelingElementDto? element)
    {
        element = _model?.Elements.FirstOrDefault(item => string.Equals(item.Id, elementId, StringComparison.Ordinal));
        return element is not null;
    }

    private string ResolveElementIdForNode(DiagramDocument document, string? nodeId)
    {
        if (string.IsNullOrWhiteSpace(nodeId) || _model is null)
        {
            return string.Empty;
        }

        var node = document.Nodes.FirstOrDefault(item => string.Equals(item.Id, nodeId, StringComparison.Ordinal));
        if (node?.Data is null || !node.Data.TryGetValue("modelElementId", out var value))
        {
            return string.Empty;
        }

        var elementId = value?.ToString() ?? string.Empty;
        return _model.Elements.Any(element => string.Equals(element.Id, elementId, StringComparison.Ordinal))
            ? elementId
            : string.Empty;
    }

    /// <summary>Element lookup that tolerates a malformed (duplicate-id) source model.</summary>
    private Dictionary<string, ModelingElementDto> ElementsById()
        => _model is null
            ? new Dictionary<string, ModelingElementDto>(StringComparer.Ordinal)
            : _model.Elements
                .GroupBy(element => element.Id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    private string GetElementName(string elementId)
        => _model?.Elements.FirstOrDefault(element => string.Equals(element.Id, elementId, StringComparison.Ordinal))?.Name ?? elementId;

    /// <summary>
    /// Whether the semantic type CONTAINS other elements (BPMN pool/lane/subprocess). Resolved from
    /// the notation profile's <see cref="IModelingNotationProfile.ContainerElementTypes"/> (phase 3
    /// review, A6) — the editor owns no hard-coded vocabulary, so a custom notation's containers
    /// work exactly like BPMN's.
    /// </summary>
    internal bool IsContainerSemanticType(string? semanticType)
    {
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            return false;
        }

        var profile = NotationProfiles?.GetProfile(EffectiveNotation);
        return profile is not null
            && profile.ContainerElementTypes.Any(type => string.Equals(type, semanticType.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The container candidates of the notation (elements whose type contains others).</summary>
    internal IReadOnlyList<ModelingElementDto> ContainerElements
        => _model is null
            ? []
            : [.. _model.Elements.Where(element => IsContainerSemanticType(element.SemanticType))];

    /// <summary>The localized semantic-type label (the catalog/tree fallback chain): the
    /// <c>TmModelingSemanticType_{key}</c> resource, else the humanized key — the SAME fallback the
    /// catalog palette and the tree use, so a picker option, a tree row and an announcement never
    /// disagree on one type (phase 3 review, U7).</summary>
    private string GetTypeLabel(string semanticType)
    {
        if (string.IsNullOrWhiteSpace(semanticType))
        {
            return string.Empty;
        }

        return Loc.TryGetString($"TmModelingSemanticType_{semanticType.Trim()}", out var localized) && !string.IsNullOrWhiteSpace(localized)
            ? localized
            : ModelingNotationCatalog.Humanize(semanticType.Trim());
    }

    private ModelingModelChangeSet NewIntent() => new()
    {
        ModelId = _model?.Id ?? string.Empty,
        ProviderKey = EffectiveProviderKey ?? string.Empty,
        BaseVersion = _model?.Metadata.SourceVersion ?? string.Empty
    };

    /// <summary>The name the delete announcement names: the user's selected element, else the
    /// first removed element's name (a cascade started from a relationship or an issue).</summary>
    private string ResolveDeletedPrimaryName((IReadOnlyList<string> ElementIds, IReadOnlyList<string> RelationshipIds) request)
    {
        if (_selectedElement is not null && request.ElementIds.Contains(_selectedElement.Id, StringComparer.Ordinal))
        {
            return GetElementName(_selectedElement.Id);
        }

        var first = _model?.Elements.FirstOrDefault(element => request.ElementIds.Contains(element.Id, StringComparer.Ordinal));
        return first is null ? string.Empty : GetElementName(first.Id);
    }

    /// <summary>The polite announcement of a confirmed deletion (phase 3 review, U4): the deleted
    /// element by NAME with grammatical plural counts, or the relationship's endpoints when only a
    /// relationship went away.</summary>
    private string DescribeDelete(
        (IReadOnlyList<string> ElementIds, IReadOnlyList<string> RelationshipIds) request,
        string primaryName,
        ModelingRelationshipDto? primaryRelationship)
    {
        if (request.ElementIds.Count == 0)
        {
            return primaryRelationship is null
                ? Loc.Plural("TmModelingEditor_DeleteRelCount", request.RelationshipIds.Count)
                : string.Format(
                    CultureInfo.CurrentUICulture,
                    Loc["TmModelingEditor_StructureDeletedRelationship"],
                    GetElementName(primaryRelationship.SourceElementId),
                    GetElementName(primaryRelationship.TargetElementId));
        }

        return string.Format(
            CultureInfo.CurrentUICulture,
            Loc["TmModelingEditor_StructureDeleted"],
            primaryName,
            Loc.Plural("TmModelingEditor_DeleteNestedCount", Math.Max(0, request.ElementIds.Count - 1)),
            Loc.Plural("TmModelingEditor_DeleteRelCount", request.RelationshipIds.Count));
    }

    /// <summary>The polite announcement of an insert (phase 3 review, U4): type + quoted name, plus
    /// the container it landed in when there is one.</summary>
    private string DescribeInsert(string semanticType, string? name, string? containerId)
    {
        var typeLabel = GetTypeLabel(semanticType);
        var displayName = string.IsNullOrWhiteSpace(name) ? typeLabel : name!.Trim();
        if (!string.IsNullOrWhiteSpace(containerId))
        {
            return string.Format(
                CultureInfo.CurrentUICulture,
                Loc["TmModelingEditor_StructureInsertedInto"],
                typeLabel,
                displayName,
                GetElementName(containerId!));
        }

        return string.Format(
            CultureInfo.CurrentUICulture,
            Loc["TmModelingEditor_StructureInserted"],
            typeLabel,
            displayName);
    }

    /// <summary>DOM id of the header's Delete button — the delete confirmation's fallback focus
    /// target when no following tree item exists (phase 3 review, U1).</summary>
    private readonly string _deleteHeaderButtonId = $"tm-modeling-delete-{Guid.NewGuid():N}";

    /// <summary>DOM id of one model-tree item (the focus hook the delete confirmation restores to).</summary>
    internal static string TreeItemDomId(string elementId) => $"tm-modeling-tree-item-{elementId}";

    /// <summary>Per-instance id of the "select an element first" sr-only hint the disabled
    /// Connect/Delete actions point at (review U11).</summary>
    private readonly string _selectElementFirstHintId = $"tm-modeling-select-first-{Guid.NewGuid():N}";

    /// <summary>Per-instance id of the "open a diagram first" sr-only hint the disabled Arrange
    /// action points at (phase 4 review U10).</summary>
    private readonly string _diagramNotReadyHintId = $"tm-modeling-diagram-not-ready-{Guid.NewGuid():N}";

    /// <summary>
    /// The editor root's Delete shortcut (phase 3 review, A10): on a NON-text target it asks for the
    /// same cascade confirmation the header Delete does. Text-entry targets keep the native Delete
    /// (the JS probe answers "text"), and a keystroke a nested editor already consumed (the canvas
    /// Delete, a modal) arrives with its default prevented — the delegated path skips it.
    /// </summary>
    internal async Task HandleEditorRootKeyDownAsync(KeyboardEventArgs args)
    {
        if (!CanEditModelStructure || IsMobileLayout || _model is null)
        {
            return;
        }

        if (!string.Equals(args.Key, "Delete", StringComparison.Ordinal)
            || args.CtrlKey || args.MetaKey || args.AltKey)
        {
            return;
        }

        if (await Interop.InvokeSafeAsync<bool>("isTextEntryTarget", _editorRoot))
        {
            return;
        }

        await RequestDeleteSelectedAsync();
    }

    private string InsertTransactionName => Loc["TmModelingEditor_InsertElement"];

    private string ConnectTransactionName => Loc["TmModelingEditor_ConnectTo"];

    private string DeleteTransactionName => Loc["TmModelingEditor_DeleteElement"];

    /// <summary>
    /// Publishes a polite announcement (one live region for every structural result). A REPEATED
    /// identical message still re-announces (phase 3 review, U2): a live region only speaks when its
    /// text CHANGES, so the second identical announcement carries a trailing zero-width space — the
    /// third drops it again, so every announcement differs from the previous one.
    /// </summary>
    private void Announce(string message)
    {
        _structureAnnouncement = string.Equals(_structureAnnouncement, message, StringComparison.Ordinal)
            ? message + "\u200B"
            : message;
    }

    private void ShowStructureError(string title, string? message, string? suggestedFix)
    {
        _structureErrorTitle = title;
        _structureErrorText = message;
        _structureErrorFix = suggestedFix;
        Announce(string.IsNullOrWhiteSpace(message) ? title : message!);
    }

    private void ClearStructureError()
    {
        _structureErrorTitle = null;
        _structureErrorText = null;
        _structureErrorFix = null;
    }

    private Task DismissStructureErrorAsync()
    {
        ClearStructureError();
        return Task.CompletedTask;
    }

    /// <summary>A relationship awaiting the user's type pick (the dialog state).</summary>
    private sealed record PendingRelationship(
        string SourceElementId,
        string TargetElementId,
        IReadOnlyList<string> AllowedTypes,
        bool FromConnectDialog);
}
