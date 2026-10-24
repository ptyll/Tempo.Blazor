using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Components;

// Saving: the single-flight queue, the confirmed-outcome contract, the dirty state, the save badge
// and auto-save.
public partial class TmEmailTemplateEditor
{
    // ── Saving ───────────────────────────────────────────────────────────────────────────────

    private Task OnSaveClickedAsync() => RequestSaveAsync(EmailSaveTrigger.Manual);

    /// <summary>The alert's "Try again": the user's explicit retry is a manual save, never an auto-save.</summary>
    private Task OnRetrySaveAsync() => RequestSaveAsync(EmailSaveTrigger.Manual);

    private async Task<EmailSaveResult> RequestSaveAsync(EmailSaveTrigger trigger)
    {
        if (_disposed)
        {
            return await RejectSaveAsync(Loc["EmailEditor_SaveStatus_Cancelled"], trigger);
        }

        if (ReadOnly)
        {
            return await RejectSaveAsync(Loc["EmailEditor_SaveStatus_ReadOnly"], trigger);
        }

        if (OnSave is null)
        {
            return await RejectSaveAsync(Loc["EmailEditor_SaveStatus_NoHost"], trigger);
        }

        PendingSave? job = null;
        Task<EmailSaveResult>? merged = null;
        lock (_saveSync)
        {
            if (_saveInProgress)
            {
                // Single-flight: the trigger joins ONE follow-up save, and this caller is answered by it.
                _followUpJob ??= new PendingSave(trigger);
                _followUpJob.Merge(trigger);
                merged = _followUpJob.Completion;
            }
            else
            {
                _saveInProgress = true;
                job = new PendingSave(trigger);
            }
        }

        if (merged is not null) return await merged;

        _ = PumpAsync(job!);
        return await job!.Completion;
    }

    /// <summary>
    /// The editor refuses a save without touching the host (read-only, no handler, disposed): the answer is
    /// a failed result, and the badge says why. Rendering goes through the dispatcher so a host calling
    /// <see cref="SaveAsync"/> from outside the render loop is safe.
    /// </summary>
    private async Task<EmailSaveResult> RejectSaveAsync(string message, EmailSaveTrigger trigger)
    {
        var result = Record(EmailSaveResult.Failed(message), null, null, trigger, _documentEpoch);
        await InvokeAsync(StateHasChanged);
        return result;
    }

    /// <summary>
    /// Runs the save queue: one host request at a time, then at most one merged follow-up while the document
    /// is still dirty. A follow-up that finds a clean document is answered with the previous result and no
    /// second write, and a follow-up that can no longer write (paused auto-save after a conflict, a new
    /// read-only pin, a removed handler, a disposed editor) is answered without touching the host.
    /// </summary>
    private async Task PumpAsync(PendingSave job)
    {
        while (true)
        {
            EmailSaveResult result;
            try
            {
                result = await ExecuteSaveAsync(job.Trigger);
            }
            catch (Exception ex)
            {
                LogError(ex, "The email template save pipeline failed unexpectedly.");
                result = Record(EmailSaveResult.Failed(null), null, null, job.Trigger, _documentEpoch);
            }

            job.Complete(result);

            PendingSave? next;
            bool stillDirty;
            lock (_saveSync)
            {
                next = _followUpJob;
                _followUpJob = null;
                stillDirty = IsDirty;
                if (next is null || !stillDirty)
                {
                    _saveInProgress = false;
                }
            }

            if (next is null) return;
            if (!stillDirty)
            {
                next.Complete(result);
                return;
            }

            // A conflict pauses auto-save: a queue entry that arrived as an auto-save must not turn into the
            // second write the pause exists to prevent (the user's own trigger, merged in, still may).
            if (next.Trigger == EmailSaveTrigger.AutoSave && _autoSavePaused)
            {
                lock (_saveSync) { _saveInProgress = false; }
                next.Complete(result);
                return;
            }

            // The world may have changed while the previous request was in flight: re-check every reason the
            // editor refuses a save before starting the follow-up (the badge explains which one).
            var refusal = RefusalKey();
            if (refusal is not null)
            {
                var refused = Record(EmailSaveResult.Failed(Loc[refusal]), null, null, next.Trigger, _documentEpoch);
                lock (_saveSync) { _saveInProgress = false; }
                next.Complete(refused);
                return;
            }

            job = next;
        }
    }

    /// <summary>The localization key of the reason a save may not start right now, or <see langword="null"/>.</summary>
    private string? RefusalKey()
        => _disposed ? "EmailEditor_SaveStatus_Cancelled"
        : ReadOnly ? "EmailEditor_SaveStatus_ReadOnly"
        : OnSave is null ? "EmailEditor_SaveStatus_NoHost"
        : null;

    private async Task<EmailSaveResult> ExecuteSaveAsync(EmailSaveTrigger trigger)
    {
        var revision = _revision;
        var epoch = _documentEpoch;
        var snapshot = _document.DeepClone();
        // The history version of exactly the state the host is about to receive: the baseline becomes THIS
        // version on success, so an edit made while the request is in flight (a newer version) stays dirty.
        var snapshotVersion = _history.Version;
        var snapshotContent = _history.Content;
        // Invariant the baseline relies on: EVERY change of _document is followed synchronously by a commit
        // (Push), so the history's content signature IS the document being handed to the host. A mutation
        // that skipped CommitAsync would make the baseline claim a state the host never received (silently
        // clean after Saved). Debug builds check it; Release pays nothing.
        System.Diagnostics.Debug.Assert(
            string.Equals(snapshotContent, EmailTemplateSerializer.SerializeContent(snapshot), StringComparison.Ordinal),
            "The editor history is out of step with the document at save time: a document change was not committed.");

        IsSaving = true;
        _saveErrorMessage = null;
        await InvokeAsync(StateHasChanged);

        EmailSaveResult result;
        try
        {
            EmailSaveResult? answered = null;
            try
            {
                answered = await OnSave!(new EmailSaveRequest(snapshot, trigger, revision, _disposeCts.Token));
            }
            catch (OperationCanceledException) when (_disposed)
            {
                answered = EmailSaveResult.Failed(Loc["EmailEditor_SaveStatus_Cancelled"]);
            }
            catch (Exception ex)
            {
                LogError(ex, "The host's email template save handler threw; the save is reported as failed.");
                // No detail: the badge's own label already is "Save failed", and repeating it under the label
                // reads as two different errors. The alert carries the retry affordance instead.
                answered = EmailSaveResult.Failed(null);
            }

            result = answered ?? EmailSaveResult.Failed(Loc["EmailEditor_SaveStatus_NoResult"]);
        }
        finally
        {
            IsSaving = false;
        }

        result = Record(result, snapshotVersion, snapshotContent, trigger, epoch);
        await InvokeAsync(StateHasChanged);
        return result;
    }

    /// <summary>
    /// Stores the outcome (dirty baseline, badge state, conflict pause, announcement) and returns it
    /// unchanged. An outcome that belongs to a document the host has since replaced only answers its caller.
    /// </summary>
    private EmailSaveResult Record(EmailSaveResult result, long? savedVersion, string? savedContent, EmailSaveTrigger trigger, long epoch)
    {
        if (epoch != _documentEpoch) return result;

        _lastSaveOutcome = result.Status;

        switch (result.Status)
        {
            case EmailSaveStatus.Saved when savedVersion is not null:
                // The baseline is the VERSION the host confirmed, not the live document: an edit made
                // while the save was in flight stays dirty after success.
                _baselineVersion = savedVersion.Value;
                _baselineContent = savedContent ?? _baselineContent;
                _lastSavedAt = TimeProvider.GetUtcNow();
                _saveErrorMessage = null;
                _conflictMessage = null;
                if (trigger is not EmailSaveTrigger.AutoSave)
                {
                    // Any explicit save the host confirmed resolves the conflict — including Ctrl/⌘+S: the
                    // user has just proved the template can be written again (D26).
                    _autoSavePaused = false;
                }

                // Announced only for a save the user asked for: a background auto-save must not narrate the
                // editing session, and "Saving…"/the minute tick never touch the live region at all.
                if (trigger is not EmailSaveTrigger.AutoSave)
                {
                    _announcement = Loc["EmailEditor_SaveStatus_Saved"];
                }

                EnsureSavedAgeTimer();
                break;

            case EmailSaveStatus.Conflict:
                _conflictMessage = result.Message;
                _saveErrorMessage = null;
                _autoSavePaused = true;
                // The alert (role="alert") announces this; the announcer stays quiet so it is not repeated.
                _announcement = string.Empty;
                break;

            case EmailSaveStatus.Failed:
                _saveErrorMessage = result.Message;
                _announcement = string.Empty;
                break;
        }

        RecomputeDirty();
        return result;
    }

    // ── Dirty state ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Recomputes <see cref="IsDirty"/> without serializing the document (phase 7): the history already took
    /// the one serialization of this commit. Same version = the confirmed state itself (undo/redo landing on it
    /// restores its version); a different version is still clean when the CONTENT is the confirmed content (the
    /// user typed the saved text back, or an edit changed nothing but the timestamp) - a string comparison.
    /// </summary>
    private void RecomputeDirty()
        => SetDirty(_history.Version != _baselineVersion
                    && !string.Equals(_history.Content, _baselineContent, StringComparison.Ordinal));
    private void SetDirty(bool dirty)
    {
        if (IsDirty == dirty) return;
        IsDirty = dirty;
        _ = NotifyDirtyChangedAsync(dirty);
    }

    private async Task NotifyDirtyChangedAsync(bool dirty)
    {
        if (!IsDirtyChanged.HasDelegate) return;
        try
        {
            await IsDirtyChanged.InvokeAsync(dirty);
        }
        catch (Exception ex)
        {
            LogError(ex, "The IsDirtyChanged handler threw.");
        }
    }

    // ── Save badge ──────────────────────────────────────────────────────────────────────────

    private enum EditorSaveBadge { None, Dirty, Saving, Saved, Failed, Conflict }

    private EditorSaveBadge SaveBadgeKind =>
        IsSaving ? EditorSaveBadge.Saving
        : _lastSaveOutcome == EmailSaveStatus.Conflict ? EditorSaveBadge.Conflict
        : _lastSaveOutcome == EmailSaveStatus.Failed ? EditorSaveBadge.Failed
        : IsDirty ? EditorSaveBadge.Dirty
        : _lastSavedAt is not null ? EditorSaveBadge.Saved
        : EditorSaveBadge.None;

    private string SaveBadgeStateName => SaveBadgeKind switch
    {
        EditorSaveBadge.Dirty => "dirty",
        EditorSaveBadge.Saving => "saving",
        EditorSaveBadge.Saved => "saved",
        EditorSaveBadge.Failed => "failed",
        EditorSaveBadge.Conflict => "conflict",
        _ => "none",
    };

    private string SaveBadgeIcon => SaveBadgeKind switch
    {
        EditorSaveBadge.Dirty => IconNames.Edit,
        EditorSaveBadge.Saving => IconNames.Refresh,
        EditorSaveBadge.Saved => IconNames.CheckCircle,
        EditorSaveBadge.Failed => IconNames.AlertCircle,
        EditorSaveBadge.Conflict => IconNames.AlertTriangle,
        _ => IconNames.Circle,
    };

    private string SaveBadgeLabel => SaveBadgeKind switch
    {
        EditorSaveBadge.Dirty => Loc["EmailEditor_SaveStatus_Dirty"],
        EditorSaveBadge.Saving => Loc["EmailEditor_SaveStatus_Saving"],
        EditorSaveBadge.Saved => SavedAgoText(),
        EditorSaveBadge.Failed => Loc["EmailEditor_SaveStatus_Failed"],
        EditorSaveBadge.Conflict => Loc["EmailEditor_SaveStatus_Conflict"],
        _ => string.Empty,
    };

    /// <summary>
    /// The host's reason under the badge label — <see langword="null"/> when there is none, and also when it
    /// is just the label repeated (a handler that throws is reported as <see cref="EmailSaveResult.Failed()"/>
    /// without a message precisely so the pill does not read the same sentence twice).
    /// </summary>
    private string? SaveBadgeDetail
    {
        get
        {
            var detail = SaveBadgeKind switch
            {
                EditorSaveBadge.Failed => _saveErrorMessage,
                EditorSaveBadge.Conflict => _conflictMessage,
                _ => null,
            };

            if (string.IsNullOrWhiteSpace(detail)) return null;
            return string.Equals(detail.Trim(), SaveBadgeLabel.Trim(), StringComparison.Ordinal) ? null : detail;
        }
    }

    /// <summary>The badge's tooltip: the label, plus the host's reason when it sent one.</summary>
    private string SaveBadgeTitle => SaveBadgeDetail is { } detail
        ? SaveBadgeLabel + ": " + detail
        : SaveBadgeLabel;

    /// <summary>The badge kind the full-width alert reports, or <see langword="null"/> for a quiet state.</summary>
    private EditorSaveBadge? SaveAlertKind => SaveBadgeKind is EditorSaveBadge.Failed or EditorSaveBadge.Conflict
        ? SaveBadgeKind
        : null;

    /// <summary>The host's own reason in the alert; the localized label stands in when it sent none.</summary>
    private string SaveAlertMessage => SaveBadgeKind switch
    {
        EditorSaveBadge.Conflict => string.IsNullOrWhiteSpace(_conflictMessage) ? Loc["EmailEditor_SaveStatus_Conflict"] : _conflictMessage!,
        EditorSaveBadge.Failed => string.IsNullOrWhiteSpace(_saveErrorMessage) ? Loc["EmailEditor_SaveStatus_Failed"] : _saveErrorMessage!,
        _ => string.Empty,
    };

    /// <summary>What the user can do about it — the alert must not be a diagnosis without a next step.</summary>
    private string SaveAlertHint => SaveBadgeKind == EditorSaveBadge.Conflict
        ? Loc["EmailEditor_SaveStatus_ConflictHint"]
        : Loc["EmailEditor_SaveStatus_FailedHint"];

    /// <summary>
    /// "Saved just now" / "Saved 5 min ago" — the language's own relative phrase comes from the core
    /// <see cref="ITmRelativeTimeFormatter"/> (the host may replace it, and it knows the plural forms),
    /// wrapped in the editor's "Saved {0}" pattern so the verb stays the editor's wording.
    /// </summary>
    private string SavedAgoText()
    {
        if (_lastSavedAt is not { } savedAt) return Loc["EmailEditor_SaveStatus_Saved"];

        var now = TimeProvider.GetUtcNow();
        if (now - savedAt < TimeSpan.FromMinutes(1)) return Loc["EmailEditor_SaveStatus_SavedJustNow"];

        // No core localization registered at all (a host that replaced every string): the badge still says
        // "Saved", just without a relative phrase, instead of formatting a key.
        if (RelativeTimeFormatter is not { } formatter) return Loc["EmailEditor_SaveStatus_Saved"];

        return Loc["EmailEditor_SaveStatus_SavedAgo", formatter.Format(savedAt, now)];
    }

    /// <summary>
    /// The relative-time formatter: the host's registration when it has one (<c>AddTempoBlazor</c> wires a
    /// scoped default that reads the user's time zone), otherwise the core implementation over the core
    /// localizer this editor's chrome already needs and this editor's <see cref="TimeProvider"/>.
    /// </summary>
    private ITmRelativeTimeFormatter? RelativeTimeFormatter
    {
        // Resolved lazily and cached once it resolves; a null result (no core localizer registered yet) is
        // NOT cached, so a host that registers a localizer later (tests) is picked up on the next read.
        get => _relativeTime ??= EmailRelativeTime.Resolve(Services, TimeProvider);
    }

    private void EnsureSavedAgeTimer()
    {
        if (_savedAgeTimer is not null || _disposed) return;
        _savedAgeTimer = TimeProvider.CreateTimer(_ => _ = InvokeAsync(OnSavedAgeTickAsync), null,
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private Task OnSavedAgeTickAsync()
    {
        if (_disposed || !ShowSaveStatus) return Task.CompletedTask;
        StateHasChanged();
        return Task.CompletedTask;
    }

    // ── Auto-save ───────────────────────────────────────────────────────────────────────────

    private void ScheduleAutoSave()
    {
        if (ReadOnly || _disposed) return;
        if (AutoSave is not { Enabled: true }) return;

        _autoSaveTimer?.Dispose();
        _autoSaveTimer = TimeProvider.CreateTimer(_ => _ = InvokeAsync(RunAutoSaveAsync), state: null,
            dueTime: AutoSave.Interval, period: Timeout.InfiniteTimeSpan);
    }

    private async Task RunAutoSaveAsync()
    {
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = null;

        if (_disposed || ReadOnly || _autoSavePaused) return;
        if (!IsDirty) return;
        if (OnSave is null) return;

        await RequestSaveAsync(EmailSaveTrigger.AutoSave);
    }
}
