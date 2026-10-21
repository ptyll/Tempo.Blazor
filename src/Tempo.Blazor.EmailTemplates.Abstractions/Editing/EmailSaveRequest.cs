using Tempo.Blazor.EmailTemplates.Abstractions.Model;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Editing;

/// <summary>
/// One save request. <see cref="Document"/> is a deep copy (snapshot) taken at request time, so the host
/// may serialize or persist it asynchronously while the user keeps editing; <see cref="Revision"/> is the
/// editor's monotonic document revision the snapshot was taken at.
/// </summary>
/// <param name="Document">The document snapshot to persist.</param>
/// <param name="Trigger">What asked for this save.</param>
/// <param name="Revision">The editor revision the snapshot was taken at.</param>
/// <param name="CancellationToken">
/// Cancelled when the editor is disposed, so the host can abandon its own I/O. Pass it to your store/HTTP
/// calls instead of ignoring it.
/// </param>
public sealed record EmailSaveRequest(
    EmailTemplateDocument Document,
    EmailSaveTrigger Trigger,
    long Revision,
    CancellationToken CancellationToken);
