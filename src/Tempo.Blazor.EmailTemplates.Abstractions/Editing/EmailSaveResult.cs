namespace Tempo.Blazor.EmailTemplates.Abstractions.Editing;

/// <summary>
/// The host's answer to one <see cref="EmailSaveRequest"/>. The editor only clears its dirty flag and
/// shows "Saved" for <see cref="EmailSaveStatus.Saved"/>; a <see cref="EmailSaveStatus.Failed"/> keeps the
/// document dirty, and a <see cref="EmailSaveStatus.Conflict"/> additionally pauses auto-save until a
/// successful manual/programmatic save or a new document reference.
/// </summary>
public sealed class EmailSaveResult
{
    private EmailSaveResult(EmailSaveStatus status, string? message)
    {
        Status = status;
        Message = message;
    }

    /// <summary>Gets the reported outcome.</summary>
    public EmailSaveStatus Status { get; }

    /// <summary>
    /// Gets the host's optional reason for a <see cref="EmailSaveStatus.Failed"/> or
    /// <see cref="EmailSaveStatus.Conflict"/> result. The host localizes it; the editor shows it verbatim
    /// next to its own localized status label.
    /// </summary>
    public string? Message { get; }

    /// <summary>Gets whether the host confirmed the write — the only state in which the editor may say "Saved".</summary>
    public bool IsSaved => Status == EmailSaveStatus.Saved;

    /// <summary>Creates the result that confirms the snapshot was persisted.</summary>
    public static EmailSaveResult Saved() => new(EmailSaveStatus.Saved, null);

    /// <summary>Creates a failure result carrying the host's optional reason.</summary>
    /// <param name="message">A localized, user-facing reason; <see langword="null"/> when the host has none.</param>
    public static EmailSaveResult Failed(string? message = null) => new(EmailSaveStatus.Failed, message);

    /// <summary>Creates a conflict result (a stale revision) carrying the host's optional reason.</summary>
    /// <param name="message">A localized, user-facing reason; <see langword="null"/> when the host has none.</param>
    public static EmailSaveResult Conflict(string? message = null) => new(EmailSaveStatus.Conflict, message);
}
