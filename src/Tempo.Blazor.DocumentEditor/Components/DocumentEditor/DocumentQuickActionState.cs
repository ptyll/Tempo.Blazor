using Tempo.Blazor.Localization;
using Tempo.Blazor.Components.DocumentEditor.Registry;

namespace Tempo.Blazor.Components.DocumentEditor;

/// <summary>
/// The enablement and the tooltip of the three quick actions (Save, Undo, Redo). The editor renders them in exactly
/// ONE place - the document header, or the ribbon when <c>ShowHeader</c> is false - and both places ask this type, so
/// the two surfaces can never disagree about when an action is available or what its tooltip says.
/// </summary>
internal readonly record struct DocumentQuickActionState(bool CanSave, bool CanUndo, bool CanRedo)
{
    /// <summary>
    /// Resolves the three actions. An attached command registry is the source of truth for Save; Undo and Redo are
    /// available when the host reports a step or the registry says so. Read-only disables Undo and Redo outright.
    /// </summary>
    public static DocumentQuickActionState Resolve(
        DocumentEditorCommandRegistry? registry,
        bool readOnly,
        bool isSaving,
        bool canUndo,
        bool canRedo) => new(
            registry?.GetState("save")?.IsEnabled ?? (!readOnly && !isSaving),
            !readOnly && (canUndo || (registry?.GetState("undo")?.IsEnabled ?? false)),
            !readOnly && (canRedo || (registry?.GetState("redo")?.IsEnabled ?? false)));

    /// <summary>
    /// The Save tooltip: the label, or the label with the reason it is unavailable. <paramref name="canSave"/> is the RESOLVED
    /// enablement (<see cref="CanSave"/>): a registry that enables Save while the host flags a save in flight keeps the plain label.
    /// </summary>
    public static string SaveTitle(ITmLocalizer loc, bool readOnly, bool isSaving, bool canSave = true)
    {
        var label = loc["TmDocumentEditor_Save"];
        if (readOnly)
        {
            return DisabledTitle(loc, label, loc["TmDocumentEditor_CommandDisabledReadOnly"]);
        }

        return isSaving && !canSave ? DisabledTitle(loc, label, loc["TmDocumentEditor_CommandDisabledBusy"]) : label;
    }

    /// <summary>The Undo tooltip: the label plus the description of the step it would undo.</summary>
    public static string UndoTitle(ITmLocalizer loc, bool readOnly, bool canUndo, string? description)
        => UndoRedoTitle(loc, loc["TmDocumentEditor_Undo"], readOnly, canUndo, description);

    /// <summary>The Redo tooltip: the label plus the description of the step it would redo.</summary>
    public static string RedoTitle(ITmLocalizer loc, bool readOnly, bool canRedo, string? description)
        => UndoRedoTitle(loc, loc["TmDocumentEditor_Redo"], readOnly, canRedo, description);

    private static string UndoRedoTitle(ITmLocalizer loc, string label, bool readOnly, bool canExecute, string? description)
    {
        if (readOnly)
        {
            return DisabledTitle(loc, label, loc["TmDocumentEditor_CommandDisabledReadOnly"]);
        }

        if (!canExecute)
        {
            return DisabledTitle(loc, label, loc["TmDocumentEditor_CommandDisabledUnavailable"]);
        }

        return string.IsNullOrWhiteSpace(description) ? label : loc["TmDocumentEditor_CommandTitleFormat", label, description];
    }

    private static string DisabledTitle(ITmLocalizer loc, string label, string reason) => loc["TmDocumentEditor_CommandTitleFormat", label, reason];
}
