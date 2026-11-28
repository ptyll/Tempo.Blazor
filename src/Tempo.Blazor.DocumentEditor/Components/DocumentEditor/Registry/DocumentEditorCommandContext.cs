using Tempo.Blazor.DocumentEditor.Models;

namespace Tempo.Blazor.Components.DocumentEditor.Registry;

/// <summary>Snapshot of editor state passed to commands when computing enabled state or executing.</summary>
public sealed record DocumentEditorCommandContext
{
    /// <summary>Whether the editor is currently in read-only mode.</summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Host-controlled permissions for the current user.</summary>
    public DocumentEditorPermissions Permissions { get; init; } = new();

    /// <summary>The active WYSIWYG editing region (Body, Header, Footer, etc.).</summary>
    public string ActiveRegion { get; init; } = "Body";

    /// <summary>Current WYSIWYG selection snapshot, or <c>null</c> when no selection is active.</summary>
    public WysiwygSelectionSnapshot? SelectionSnapshot { get; init; }

    /// <summary>Formatting state resolved for the current selection.</summary>
    public WysiwygFormattingState FormattingState { get; init; } = new();

    /// <summary>Current JS-owned undo/redo stack state.</summary>
    public WysiwygUndoState UndoState { get; init; } = new();

    /// <summary>Whether a document is currently loaded in the editor.</summary>
    public bool HasDocument { get; init; }

    /// <summary>Whether the PDF export provider is available and the user may export.</summary>
    public bool CanExportPdf { get; init; }

    /// <summary>Whether the format provider supports DOCX import and the user may import.</summary>
    public bool CanImportDocx { get; init; }

    /// <summary>Whether the format provider supports DOCX export and the user may export.</summary>
    public bool CanExportDocx { get; init; }

    /// <summary>Whether a save operation is currently in progress.</summary>
    public bool IsSaving { get; init; }

    /// <summary>Whether the track-changes feature is available in the current editor state.</summary>
    public bool CanTrackChanges { get; init; }

    /// <summary>Whether adding a comment is available in the current editor state.</summary>
    public bool CanAddComment { get; init; }

    /// <summary>Whether document comparison is available.</summary>
    public bool CanCompareDocuments { get; init; }

    /// <summary>
    /// The editor is laid out as a phone (decision G2: viewing plus light edits). The registry disables the tablet-and-up commands
    /// (<see cref="DocumentEditorCommandRegistry.PhoneRestrictedCommands"/>) while it is set.
    /// </summary>
    public bool IsPhoneLayout { get; init; }

    /// <summary>Whether the document is in protected (restricted-editing) mode.</summary>
    public bool IsProtected { get; init; }

    /// <summary>
    /// When <see cref="IsProtected"/> is <c>true</c>, indicates that the caret / selection anchor
    /// is currently inside one of the document's editable markers. <c>false</c> means the caret
    /// is outside all markers, so data-affecting commands must be disabled.
    /// </summary>
    public bool IsInEditableRegion { get; init; }
}
