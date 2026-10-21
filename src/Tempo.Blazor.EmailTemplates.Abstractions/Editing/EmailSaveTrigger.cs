namespace Tempo.Blazor.EmailTemplates.Abstractions.Editing;

/// <summary>Why a save was requested.</summary>
public enum EmailSaveTrigger
{
    /// <summary>The user pressed the editor's Save button.</summary>
    Manual,

    /// <summary>The user pressed Ctrl+S / Cmd+S.</summary>
    Shortcut,

    /// <summary>The editor's debounced auto-save fired.</summary>
    AutoSave,

    /// <summary>The host called <c>TmEmailTemplateEditor.SaveAsync()</c> itself.</summary>
    Programmatic,
}
