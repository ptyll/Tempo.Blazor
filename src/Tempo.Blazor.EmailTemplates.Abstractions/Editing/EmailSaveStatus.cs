namespace Tempo.Blazor.EmailTemplates.Abstractions.Editing;

/// <summary>The outcome a host reports for one save request.</summary>
public enum EmailSaveStatus
{
    /// <summary>The host persisted the snapshot.</summary>
    Saved,

    /// <summary>The host could not persist the snapshot.</summary>
    Failed,

    /// <summary>The host refused the snapshot because its copy is newer (a stale revision).</summary>
    Conflict,
}
