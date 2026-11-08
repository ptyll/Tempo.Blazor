using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Feedback;

namespace Tempo.Blazor.Services;

/// <summary>
/// Describes a single toast notification instance.
/// </summary>
public sealed record ToastInstance
{
    /// <summary>Unique identifier for the toast.</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    /// <summary>Severity level of the toast (Info, Success, Warning, Error).</summary>
    public ToastSeverity Severity { get; init; }
    /// <summary>Message content of the toast.</summary>
    public string Message { get; init; } = string.Empty;
    /// <summary>Optional title for the toast.</summary>
    public string? Title { get; init; }
    /// <summary>
    /// Duration in milliseconds before auto-dismiss. Default is 5000ms.
    /// A value &lt;= 0 means the toast is "sticky" and will never auto-dismiss
    /// (the caller must dismiss it manually via <see cref="ToastService.Remove"/> or <see cref="ToastService.Clear"/>).
    /// </summary>
    public int Duration { get; init; } = 5000;
    /// <summary>Timestamp when the toast was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    /// <summary>
    /// Monotonic per-id version bumped on every in-place replacement (Key dedupe, Update). The
    /// container keys the progress bar on it so the CSS animation restarts with the new duration.
    /// </summary>
    public int Revision { get; init; }
    /// <summary>
    /// Optional dedupe key set through <see cref="ToastOptions.Key"/>: at most one toast with a
    /// given key exists at a time — re-showing the key replaces the toast in place.
    /// </summary>
    public string? Key { get; init; }
    /// <summary>Optional action button (e.g. Undo) rendered inside the toast.</summary>
    public ToastAction? Action { get; init; }
    /// <summary>Optional custom content rendered below the message.</summary>
    public RenderFragment? Content { get; init; }
    /// <summary>
    /// Live-region politeness of the announcement. Derived from severity at creation time
    /// (<see cref="ToastSeverity.Error"/> is <see cref="ToastPoliteness.Assertive"/>), overridable
    /// through <see cref="ToastOptions.Politeness"/>.
    /// </summary>
    public ToastPoliteness Politeness { get; init; } = ToastPoliteness.Polite;
}

/// <summary>
/// Injectable service for showing toast notifications.
/// Register as Scoped in DI. Use <see cref="TmToastContainer"/> in layout to render.
/// Toasts with <see cref="ToastInstance.Duration"/> &gt; 0 auto-dismiss on a timer driven by the
/// registered <see cref="TimeProvider"/> (or <see cref="TimeProvider.System"/> when none is
/// registered — no JS interop required, works under any Blazor render mode). A duration of
/// 0 (or negative) makes the toast sticky.
/// <para>
/// Phase fze-3 additions (all additive; the classic helpers are unchanged):
/// <see cref="Show(ToastOptions)"/> with <see cref="ToastOptions.Key"/> in-place dedupe,
/// <see cref="ShowUndo"/>, <see cref="Update"/>, <see cref="Pause"/>/<see cref="Resume"/> and
/// <see cref="PauseAll"/>/<see cref="ResumeAll"/>. Pause disarms the one-shot timer and remembers
/// the remaining time; resume re-arms a single one-shot with exactly that remainder.
/// </para>
/// </summary>
public sealed class ToastService : IDisposable
{
    private readonly List<ToastInstance> _toasts = [];
    private readonly Dictionary<string, ITimer> _autoDismissTimers = [];
    private readonly Dictionary<string, DateTimeOffset> _deadlines = [];
    private readonly Dictionary<string, TimeSpan> _pausedRemaining = [];
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>
    /// Creates a toast service whose auto-dismiss timers run on <paramref name="timeProvider"/>.
    /// When the consumer registers a <see cref="TimeProvider"/> in DI it is injected here;
    /// otherwise the default <c>null</c> falls back to <see cref="TimeProvider.System"/>.
    /// Tests pass a FakeTimeProvider so "the deadline has passed" is a test-controlled fact,
    /// not a wall-clock race.
    /// </summary>
    public ToastService(TimeProvider? timeProvider = null)
        => _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>Fired when a new toast is added.</summary>
    public event Action? OnChange;

    /// <summary>
    /// Floor (milliseconds) applied to the duration of every toast carrying an <see cref="ToastAction"/>,
    /// so an action button is never on a shorter timer than the user can react to. The container's
    /// <c>MinActionDuration</c> parameter owns the value (it pushes it here when parameters are set).
    /// Toasts without an action and sticky toasts (duration &lt;= 0) are never floored.
    /// Defaults to 10000 (10 seconds).
    /// </summary>
    public int MinActionDuration { get; set; } = 10_000;

    /// <summary>
    /// How many auto-dismiss timers are still armed. Test-visible state: the "the timer must not
    /// fire" assertions wait on THIS — a cancelled timer leaves the dictionary, a never-scheduled
    /// one never enters it — instead of sleeping a fixed wall-clock margin past the deadline.
    /// </summary>
    internal int PendingAutoDismissCount
    {
        get
        {
            lock (_lock)
                return _autoDismissTimers.Count;
        }
    }

    /// <summary>Current active toasts (read-only snapshot).</summary>
    public IReadOnlyList<ToastInstance> Toasts
    {
        get
        {
            lock (_lock)
                return _toasts.ToList();
        }
    }

    /// <summary>Ids of the toasts whose auto-dismiss timer is currently paused.</summary>
    public IReadOnlyList<string> PausedIds
    {
        get
        {
            lock (_lock)
                return _pausedRemaining.Keys.ToList();
        }
    }

    /// <summary>Show a success toast.</summary>
    public void ShowSuccess(string message, string? title = null, int duration = 5000)
        => Add(ToastSeverity.Success, message, title, duration);

    /// <summary>Show an error toast.</summary>
    public void ShowError(string message, string? title = null, int duration = 8000)
        => Add(ToastSeverity.Error, message, title, duration);

    /// <summary>Show a warning toast.</summary>
    public void ShowWarning(string message, string? title = null, int duration = 6000)
        => Add(ToastSeverity.Warning, message, title, duration);

    /// <summary>Show an info toast.</summary>
    public void ShowInfo(string message, string? title = null, int duration = 5000)
        => Add(ToastSeverity.Info, message, title, duration);

    /// <summary>
    /// Show a toast from an options object (actions, custom content, dedupe key, politeness).
    /// When <see cref="ToastOptions.Key"/> is set and a toast with the same key exists, that toast
    /// is REPLACED in place — same id, same queue position — and its auto-dismiss timer restarts.
    /// Returns the toast id (the reused id on a replacement).
    /// </summary>
    public string Show(ToastOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        string id;
        lock (_lock)
        {
            if (_disposed)
                return string.Empty;
            var replaced = false;
            id = string.Empty;
            if (options.Key is not null)
            {
                var index = _toasts.FindIndex(t => t.Key == options.Key);
                if (index >= 0)
                {
                    // Dedupe: keep identity (id) and position (CreatedAt); only content changes and
                    // the timer restarts.
                    var existing = _toasts[index];
                    var wasPaused = _pausedRemaining.ContainsKey(existing.Id);
                    id = existing.Id;
                    CancelTimerStateLocked(id);
                    _toasts[index] = CreateInstance(options, id, existing.CreatedAt) with { Revision = existing.Revision + 1 };
                    RearmAfterMutationLocked(id, options.Duration, wasPaused);
                    replaced = true;
                }
            }

            if (!replaced)
            {
                var toast = CreateInstance(options, null, _timeProvider.GetUtcNow().UtcDateTime);
                _toasts.Add(toast);
                id = toast.Id;
                if (toast.Duration > 0)
                    ArmTimerLocked(id, toast.Duration);
            }
        }
        OnChange?.Invoke();
        return id;
    }

    /// <summary>
    /// Show a toast with a built-in Undo action (localized label, 10s default duration — the
    /// <see cref="MinActionDuration"/> floor — so the user has time to react). Activating the action
    /// runs <paramref name="onUndo"/> and closes the toast.
    /// </summary>
    public string ShowUndo(string message, Func<Task> onUndo, string? title = null, int duration = 10_000)
    {
        ArgumentNullException.ThrowIfNull(onUndo);
        return Show(new ToastOptions
        {
            Message = message,
            Title = title,
            Severity = ToastSeverity.Info,
            Duration = duration,
            Action = new ToastAction { LabelKey = ToastAction.UndoLabelKey, OnClick = onUndo },
        });
    }

    /// <summary>
    /// Update an existing toast in place. Only the provided arguments change; the auto-dismiss
    /// timer restarts with the resulting <see cref="ToastInstance.Duration"/> (so an updated toast
    /// gets a fresh full reading time). No-op for an unknown id.
    /// </summary>
    public void Update(string id, string? message = null, string? title = null,
        ToastSeverity? severity = null, int? duration = null)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            var index = _toasts.FindIndex(t => t.Id == id);
            if (index < 0)
                return;
            var old = _toasts[index];
            var wasPaused = _pausedRemaining.ContainsKey(id);
            var newDuration = duration ?? old.Duration;
            CancelTimerStateLocked(id);
            _toasts[index] = old with
            {
                Message = message ?? old.Message,
                Title = title ?? old.Title,
                Severity = severity ?? old.Severity,
                Duration = newDuration,
                Revision = old.Revision + 1,
            };
            RearmAfterMutationLocked(id, newDuration, wasPaused);
        }
        OnChange?.Invoke();
    }

    /// <summary>
    /// Pause the auto-dismiss timer of one toast, remembering the remaining time. No-op for sticky
    /// toasts, already-paused toasts and unknown ids. The container calls this on hover/focus and
    /// when the document hides.
    /// </summary>
    public void Pause(string id)
    {
        var paused = false;
        lock (_lock)
        {
            if (_autoDismissTimers.Remove(id, out var timer))
            {
                timer.Dispose();
                var remaining = (_deadlines.TryGetValue(id, out var deadline) ? deadline : _timeProvider.GetUtcNow())
                    - _timeProvider.GetUtcNow();
                _pausedRemaining[id] = remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
                paused = true;
            }
        }
        if (paused)
            OnChange?.Invoke();
    }

    /// <summary>
    /// Resume a paused toast: re-arms a single one-shot timer with exactly the remaining time
    /// measured at <see cref="Pause"/>. A toast whose remaining time has already elapsed is
    /// removed. No-op for toasts that are not paused.
    /// </summary>
    public void Resume(string id)
    {
        var resumed = false;
        lock (_lock)
        {
            if (_disposed)
                return;
            if (_pausedRemaining.Remove(id, out var remaining))
            {
                _deadlines.Remove(id);
                if (remaining <= TimeSpan.Zero)
                {
                    _toasts.RemoveAll(t => t.Id == id);
                }
                else
                {
                    ArmTimerLocked(id, (int)Math.Ceiling(remaining.TotalMilliseconds));
                }
                resumed = true;
            }
        }
        if (resumed)
            OnChange?.Invoke();
    }

    /// <summary>Pause every toast that has an armed auto-dismiss timer.</summary>
    public void PauseAll()
    {
        string[] ids;
        lock (_lock)
            ids = _autoDismissTimers.Keys.ToArray();
        foreach (var id in ids)
            Pause(id);
    }

    /// <summary>Resume every paused toast.</summary>
    public void ResumeAll()
    {
        string[] ids;
        lock (_lock)
            ids = _pausedRemaining.Keys.ToArray();
        foreach (var id in ids)
            Resume(id);
    }

    /// <summary>Whether the toast's auto-dismiss timer is currently paused.</summary>
    public bool IsPaused(string id)
    {
        lock (_lock)
            return _pausedRemaining.ContainsKey(id);
    }

    /// <summary>Remove a toast by ID. Cancels its pending auto-dismiss timer, if any. Safe to call on an already-removed id.</summary>
    public void Remove(string id)
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _toasts.RemoveAll(t => t.Id == id);
            CancelTimerStateLocked(id);
        }
        OnChange?.Invoke();
    }

    /// <summary>Remove all toasts and cancel any pending auto-dismiss timers.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            _toasts.Clear();
            foreach (var timer in _autoDismissTimers.Values)
                timer.Dispose();
            _autoDismissTimers.Clear();
            _deadlines.Clear();
            _pausedRemaining.Clear();
        }
        OnChange?.Invoke();
    }

    private void Add(ToastSeverity severity, string message, string? title, int duration)
        => Show(new ToastOptions { Severity = severity, Message = message, Title = title, Duration = duration });

    private ToastInstance CreateInstance(ToastOptions options, string? id, DateTime createdAt)
        => new()
        {
            Id = id ?? Guid.NewGuid().ToString("N")[..8],
            Severity = options.Severity,
            Message = options.Message,
            Title = options.Title,
            // An action button must be reachable for at least MinActionDuration: floor the timer
            // (sticky toasts and action-less toasts are never floored).
            Duration = options.Action is not null && options.Duration > 0 && options.Duration < MinActionDuration
                ? MinActionDuration
                : options.Duration,
            CreatedAt = createdAt,
            Key = options.Key,
            Action = options.Action,
            Content = options.Content,
            Politeness = options.Politeness ?? (options.Severity == ToastSeverity.Error
                ? ToastPoliteness.Assertive
                : ToastPoliteness.Polite),
        };

    /// <summary>
    /// Timer state after an in-place content mutation (Key dedupe replacement, Update): a PAUSED
    /// toast stays paused and remembers the FULL new duration (no timer is armed — resume re-arms
    /// with a fresh reading time); a running toast gets a fresh one-shot timer.
    /// </summary>
    private void RearmAfterMutationLocked(string id, int duration, bool wasPaused)
    {
        if (wasPaused)
        {
            if (duration > 0)
                _pausedRemaining[id] = TimeSpan.FromMilliseconds(duration);
        }
        else if (duration > 0)
        {
            ArmTimerLocked(id, duration);
        }
    }

    /// <summary>Arms the one-shot auto-dismiss timer and remembers the deadline (Pause reads it).</summary>
    private void ArmTimerLocked(string id, int durationMs)
    {
        var dueTime = TimeSpan.FromMilliseconds(durationMs);
        _deadlines[id] = _timeProvider.GetUtcNow() + dueTime;
        // The callback captures THE timer it belongs to and, under the lock, removes the toast only
        // while the dictionary still maps the id to exactly this timer instance: a stale callback
        // (its timer already disposed and replaced by a re-show, Update or Pause/Resume) must not
        // remove the toast a newer timer owns. A disposed service swallows the callback entirely.
        ITimer self = null!;
        self = _timeProvider.CreateTimer(
            _ =>
            {
                lock (_lock)
                {
                    if (_disposed)
                        return;
                    if (!_autoDismissTimers.TryGetValue(id, out var current) || !ReferenceEquals(current, self))
                        return;
                    _autoDismissTimers.Remove(id);
                    _deadlines.Remove(id);
                    _toasts.RemoveAll(t => t.Id == id);
                }
                OnChange?.Invoke();
            },
            null,
            dueTime,
            Timeout.InfiniteTimeSpan);
        _autoDismissTimers[id] = self;
    }

    /// <summary>Drops every timer/deadline/pause state for an id, disposing an armed timer.</summary>
    private void CancelTimerStateLocked(string id)
    {
        if (_autoDismissTimers.Remove(id, out var timer))
            timer.Dispose();
        _deadlines.Remove(id);
        _pausedRemaining.Remove(id);
    }

    /// <summary>
    /// Disposes all pending auto-dismiss timers and detaches every subscriber (further Show/Update/
    /// Remove/Resume calls and any late timer callback are no-ops). Called automatically when the
    /// DI scope ends.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            foreach (var timer in _autoDismissTimers.Values)
                timer.Dispose();
            _autoDismissTimers.Clear();
            _deadlines.Clear();
            _pausedRemaining.Clear();
            _disposed = true;
            OnChange = null;
        }
    }
}
