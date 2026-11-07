using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// The "Open in editor" dialog half of <see cref="TmModelingEditor"/> (Fáze 4 G6 + final review):
/// the stable <c>data-modeling-focus</c> hooks, the focus-return selector list, the dialog title
/// fallback and the open/close paths. Kept beside the main code-behind to respect the 1200-line
/// source ceiling. Single focus-restore owner (final review): the editor's focusWithin close
/// path — the TmModal's own restore is disabled on the dialog.
/// </summary>
public partial class TmModelingEditor
{
    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay trigger.</summary>
    internal const string OpenInEditorFocusSelector = "[data-modeling-focus='open-in-editor']";

    /// <summary>Stable, test-id-independent focus hook of the open-editor overlay close button.</summary>
    internal const string OpenEditorCloseFocusSelector = "[data-modeling-focus='open-editor-close']";

    /// <summary>Stable, test-id-independent focus hook of the mobile Inspector panel (the aside
    /// an issue/edge tap moves focus onto after switching the tab, Fáze 7 re-review).</summary>
    internal const string InspectorPanelFocusSelector = "[data-modeling-focus='inspector-panel']";

    /// <summary>Stable, test-id-independent focus hook of the docked inspector's Properties TAB
    /// button (phase 13 review): a Relations-tab row activation returns the inspector to the
    /// Properties tab and moves DOM focus onto that tab — the button carries the hook in the
    /// docked panel and in the tablet inspector sheet alike.</summary>
    internal const string InspectorPropertiesTabFocusSelector = "[data-modeling-focus='inspector-tab-properties']";

    /// <summary>Stable, test-id-independent focus hook of the Relations panel root (phase 13
    /// review): the mobile element hop re-renders the section in place and focuses it, because
    /// the activated row may have unmounted with the previous selection.</summary>
    internal const string RelatedPanelFocusSelector = "[data-modeling-focus='related-panel']";

    /// <summary>Focus-return trigger of the compact header "⋯" (the mobile open-editor entry).</summary>
    internal const string MobileHeaderTriggerSelector = ".tm-modeling-editor__more .tm-dropdown-trigger";

    /// <summary>Focus-return trigger of the mobile action bar's "More" overflow.</summary>
    internal const string MobileActionBarTriggerSelector = ".tm-mobile-action-bar__more";

    /// <summary>
    /// The surface that opened the current open-editor dialog: the desktop preview trigger by
    /// default, the mobile action-bar "More" or the compact header "⋯" when opened from there
    /// (Fáze 7 review — focus returns to the ACTUAL trigger, with the
    /// <see cref="OpenInEditorFocusSelector"/> host div kept as the fallback part of the list).
    /// </summary>
    private string _openEditorFocusReturnSelector = OpenInEditorFocusSelector;

    /// <summary>
    /// The focus-return selector list: the actual trigger FIRST, then the host-div proxy. A
    /// single-part list (the desktop path) is passed through unchanged so the established
    /// contract stays byte-equal.
    /// </summary>
    private string OpenEditorFocusReturnSelector => _openEditorFocusReturnSelector == OpenInEditorFocusSelector
        ? OpenInEditorFocusSelector
        : $"{_openEditorFocusReturnSelector}, {OpenInEditorFocusSelector}";

    /// <summary>
    /// Snapshot of the focus-return list for the PENDING restore. CloseGeneratedDiagramAsync
    /// arms the restore AND resets <see cref="_openEditorFocusReturnSelector"/> to the default in
    /// the same call (Fáze 7 re-review minor), so the actual trigger list must be captured first —
    /// otherwise the reset would make every close restore the default instead of the trigger that
    /// opened the dialog.
    /// </summary>
    private string? _pendingFocusReturnSelector;

    /// <summary>
    /// Title of the open-editor dialog: the document's title, falling back to the localized
    /// "Open in editor" label when the document carries none (the pre-redesign default fell
    /// back to "Preview" — Fáze 4 renames the fallback to the dialog's own label).
    /// Test seam (InternalsVisibleTo): the modal header renders exactly this value.
    /// </summary>
    internal string OpenEditorDialogTitle
        => string.IsNullOrWhiteSpace(_openedEditorDocument?.Title)
            ? Loc["TmModelingDiagramPreview_OpenInEditor"]
            : _openedEditorDocument.Title!;

    /// <summary>
    /// The dialog DE's history binding (review A5): bound ONLY while the editor owns the semantic
    /// stack. A read-only preview keeps the diagram editor's own undo for its session-only moves —
    /// routing those to an empty semantic stack would silently break them. Built once per instance
    /// (the embedded editor must not re-render on every parent pass).
    /// </summary>
    private EventCallback<bool>? _dialogHistoryCallback;

    private EventCallback<bool> DialogHistoryRequestedCallback
        => CanEditModelStructure || CanEditModelLayout
            ? _dialogHistoryCallback ??= EventCallback.Factory.Create<bool>(this, HandleDialogHistoryRequestedAsync)
            : default;


    // ── Chrome helpers moved out of the markup partial (1200-line ceiling) ──

    /// <summary>The discard-confirmation modal's attributes.</summary>
    private Dictionary<string, object> BuildDiscardModalAttributes() => new(StringComparer.Ordinal)
    {
        ["data-testid"] = TestId("modeling-discard-changes-modal")
    };

    /// <summary>
    /// How many element / relationship / layout entries are pending (the discard modal's neutral
    /// count AND the public observable a host renders — phase 3 review, U6). Counts the structure
    /// changes (added/removed elements and relationships, parent moves) as well as the view changes
    /// (node and connection upserts/removals), so a layout-only edit is visible too.
    /// </summary>
    private int CountPendingChanges()
    {
        var pending = ComputePendingChangeSet();
        return pending is null
            ? 0
            : pending.Elements.Count + pending.Relationships.Count
                + pending.AddedElements.Count + pending.AddedRelationships.Count
                + pending.RemovedElementIds.Count + pending.RemovedRelationshipIds.Count
                + pending.ViewChanges.Sum(view => view.Nodes.Count + view.Connections.Count
                    + view.RemovedNodeElementIds.Count + view.RemovedConnectionRelationshipIds.Count);
    }

    /// <summary>
    /// The dialog DE's toolbar group selection: the FULL preset minus History while the editor owns
    /// the only undo stack; null (the DE default) for a read-only preview. Review U8: while the host
    /// persists LAYOUT, the DE's own Layout (browser dagre) menu is removed too — the editor's Arrange
    /// action in the dialog header is the one deterministic arrangement.
    /// </summary>
    private Tempo.Blazor.Components.Diagram.DiagramToolbarGroups? ResolveDialogToolbarGroups()
    {
        if (!CanEditModelStructure && !CanEditModelLayout)
        {
            return null;
        }

        var groups = Tempo.Blazor.Components.Diagram.DiagramToolbarGroups.All
            & ~Tempo.Blazor.Components.Diagram.DiagramToolbarGroups.History;
        if (CanEditModelLayout)
        {
            groups &= ~Tempo.Blazor.Components.Diagram.DiagramToolbarGroups.Layout;
        }

        return groups;
    }

    private async Task OpenGeneratedDiagramAsync(DiagramDocument document)
    {
        _openedEditorDocument = document;
        _shouldFocusOpenEditorCloseButton = true;
        await OnOpenInEditor.InvokeAsync(document);
    }

    /// <summary>
    /// The "Open in editor" document mutation (phase 3): the dialog and the preview share ONE
    /// document, so the structural part of every mutation (palette insert, drawn relationship,
    /// deletion) commits to the semantic model exactly as it does from the preview.
    /// </summary>
    private async Task UpdateOpenedEditorDocumentAsync(DiagramDocument document)
    {
        _openedEditorDocument = document;
        _document = document;
        await SyncStructureFromDocumentAsync(document);
    }

    private Task CloseGeneratedDiagramAsync()
    {
        _openedEditorDocument = null;
        _shouldRestoreOpenButtonFocus = true;
        // Capture the ACTUAL trigger list before the reset below — the restore runs after the
        // next render and must return to the trigger that OPENED this dialog (Fáze 7 re-review).
        _pendingFocusReturnSelector = OpenEditorFocusReturnSelector;
        // Fáze 7 re-review (minor): the mobile trigger selectors describe a CLOSED context (the
        // header "⋯" and the bar's More). Reset to the default so a NEXT dialog opened by the
        // desktop preview trigger cannot inherit a stale mobile return target.
        _openEditorFocusReturnSelector = OpenInEditorFocusSelector;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves DOM focus to the element matching <paramref name="selector"/> INSIDE the editor's
    /// ROOT section (the single dialog instance, the open-editor overlay close button, and every
    /// focus-return host live inside it in every layout). Progressive enhancement: every failure
    /// is swallowed and the editor keeps working without it.
    /// </summary>
    private async Task FocusWithinAsync(string selector)
    {
        await Interop.InvokeVoidSafeAsync("focusWithin", _editorRoot, selector);
    }

    /// <summary>
    /// Whether the DOCUMENT's active element sits inside the editor root and matches
    /// <paramref name="selector"/> — the flip-time probe (see the resolved-layout-changed
    /// handler). Progressive enhancement: false without the module.
    /// </summary>
    private Task<bool> IsFocusWithinAsync(string selector)
        => Interop.InvokeSafeAsync<bool>("isFocusWithin", _editorRoot, selector);

}
