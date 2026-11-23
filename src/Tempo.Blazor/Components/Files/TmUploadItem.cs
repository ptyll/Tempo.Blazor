using Microsoft.AspNetCore.Components.Forms;
using Tempo.Blazor.Abstractions.Shared;

namespace Tempo.Blazor.Components.Files;

/// <summary>Lifecycle state of a tracked upload shown by <see cref="TmFileUploadProgress"/>.</summary>
public enum TmUploadState
{
    /// <summary>Chunks are being transferred.</summary>
    Uploading,

    /// <summary>Transfer finished; the file is being scanned.</summary>
    Scanning,

    /// <summary>Upload (and scan, if any) finished successfully.</summary>
    Completed,

    /// <summary>Upload failed and can be resumed.</summary>
    Failed,

    /// <summary>Upload was cancelled by the user and can be resumed.</summary>
    Cancelled,

    /// <summary>The uploaded file was blocked by the scan hook.</summary>
    Blocked
}

/// <summary>
/// Observable state for a single file upload: progress, lifecycle, and the context needed to
/// cancel or resume it. Held by the file components and rendered by <see cref="TmFileUploadProgress"/>.
/// </summary>
public sealed class TmUploadItem : IDisposable
{
    /// <summary>Stable id for this upload (used as the render key).</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>Display file name.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Total file size in bytes.</summary>
    public long TotalBytes { get; set; }

    /// <summary>Bytes transferred so far.</summary>
    public long BytesTransferred { get; set; }

    /// <summary>Completion percentage (0–100).</summary>
    public int Percent { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public TmUploadState State { get; set; } = TmUploadState.Uploading;

    /// <summary>Optional detail (e.g. failure or block reason).</summary>
    public string? Message { get; set; }

    /// <summary>True while the upload can be cancelled.</summary>
    public bool CanCancel => State is TmUploadState.Uploading;

    /// <summary>True while the upload can be resumed.</summary>
    public bool CanResume => State is TmUploadState.Failed or TmUploadState.Cancelled;

    /// <summary>True once the upload has reached a terminal, dismissable state.</summary>
    public bool IsFinished => State is TmUploadState.Completed or TmUploadState.Blocked;

    // ── Public upload controller ─────────────────────────────────
    // A host that tracks uploads itself cancels and resumes through the item — one cancellation
    // token, one OnCancel/OnResume callback per item — instead of keeping a parallel CTS or a
    // tracker dictionary of its own.

    /// <summary>
    /// Starts a new upload attempt: the previous CTS is cancelled, then disposed when the
    /// fresh source replaces it, a fresh one is owned by this item, and the cancel-raised
    /// flag is reset. Hosts call this instead of assigning a CTS of their own.
    /// <see cref="Cts"/> and <see cref="Source"/> stay internal.
    /// </summary>
    /// <returns>The cancellation token of the new attempt.</returns>
    public CancellationToken BeginAttempt()
    {
        if (_cts is not null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The previous attempt was already released.
            }
        }

        Cts = new CancellationTokenSource();
        return CancellationToken;
    }

    /// <summary>
    /// Cancellation token of the in-flight upload. Cancelled by <see cref="CancelAsync"/>;
    /// <see cref="CancellationToken.None"/> while no attempt is running.
    /// </summary>
    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    /// <summary>
    /// Raised when the upload is cancelled through <see cref="CancelAsync"/>. Exactly once per
    /// cancel, even when <see cref="CancelAsync"/> is called repeatedly while cancelling.
    /// </summary>
    public Func<TmUploadItem, Task>? OnCancel { get; set; }

    /// <summary>Raised when a cancelled or failed upload is resumed through <see cref="ResumeAsync"/>.</summary>
    public Func<TmUploadItem, Task>? OnResume { get; set; }

    /// <summary>
    /// Cancels the in-flight upload: the <see cref="CancellationToken"/> is cancelled, the state
    /// moves to <see cref="TmUploadState.Cancelled"/> and <see cref="OnCancel"/> is raised once.
    /// Idempotent and a no-op for a finished/cancelled upload (<see cref="IsFinished"/>,
    /// <see cref="TmUploadState.Cancelled"/>) — their state and callbacks stay untouched.
    /// Safe to call repeatedly and without a running attempt.
    /// </summary>
    public async Task CancelAsync()
    {
        if (_cancelRaised || IsFinished || State is TmUploadState.Cancelled) return;
        _cancelRaised = true;

        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        State = TmUploadState.Cancelled;
        if (OnCancel is not null)
        {
            await OnCancel(this);
        }
    }

    /// <summary>
    /// Resumes a cancelled or failed upload: runs the component's resume action when one was
    /// registered and raises <see cref="OnResume"/>. A no-op when the upload is not in a
    /// resumable state (<see cref="CanResume"/> is false).
    /// </summary>
    public async Task ResumeAsync()
    {
        if (!CanResume) return;
        _cancelRaised = false;
        if (ResumeAction is not null)
        {
            await ResumeAction(this);
        }
        if (OnResume is not null)
        {
            await OnResume(this);
        }
    }

    // ── Cancel/resume plumbing (not for consumers) ───────────────
    internal CancellationTokenSource? Cts
    {
        get => _cts;
        set
        {
            _cts?.Dispose();
            _cts = value;
            _cancelRaised = false;
        }
    }
    private CancellationTokenSource? _cts;
    private bool _cancelRaised;
    internal int NextChunkIndex { get; set; }
    internal string? SessionId { get; set; }
    internal IBrowserFile? Source { get; set; }

    /// <summary>
    /// Host (or owning component) action that actually restarts a cancelled or failed upload.
    /// Invoked by <see cref="ResumeAsync"/> before <see cref="OnResume"/>.
    /// </summary>
    public Func<TmUploadItem, Task>? ResumeAction { get; set; }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt was already released.
        }

        _cts?.Dispose();
        _cts = null;
    }

    internal void Apply(TmUploadProgress progress)
    {
        BytesTransferred = progress.BytesTransferred;
        TotalBytes = progress.TotalBytes;
        Percent = progress.Percent;
        NextChunkIndex = progress.ChunkIndex + 1;
        if (!string.IsNullOrEmpty(progress.UploadSessionId))
        {
            SessionId = progress.UploadSessionId;
        }
    }
}
