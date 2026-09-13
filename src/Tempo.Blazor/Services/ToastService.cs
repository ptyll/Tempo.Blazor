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
}

/// <summary>
/// Injectable service for showing toast notifications.
/// Register as Scoped in DI. Use <see cref="TmToastContainer"/> in layout to render.
/// Toasts with <see cref="ToastInstance.Duration"/> &gt; 0 auto-dismiss on a server-side
/// timer (no JS interop required, works under any Blazor render mode). A duration of
/// 0 (or negative) makes the toast sticky.
/// </summary>
public sealed class ToastService : IDisposable
{
    private readonly List<ToastInstance> _toasts = [];
    private readonly Dictionary<string, Timer> _autoDismissTimers = [];
    private readonly object _lock = new();
    private bool _disposed;

    /// <summary>Fired when a new toast is added.</summary>
    public event Action? OnChange;

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

    /// <summary>Remove a toast by ID. Cancels its pending auto-dismiss timer, if any. Safe to call on an already-removed id.</summary>
    public void Remove(string id)
    {
        lock (_lock)
        {
            _toasts.RemoveAll(t => t.Id == id);
            if (_autoDismissTimers.Remove(id, out var timer))
                timer.Dispose();
        }
        OnChange?.Invoke();
    }

    /// <summary>Remove all toasts and cancel any pending auto-dismiss timers.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _toasts.Clear();
            foreach (var timer in _autoDismissTimers.Values)
                timer.Dispose();
            _autoDismissTimers.Clear();
        }
        OnChange?.Invoke();
    }

    private void Add(ToastSeverity severity, string message, string? title, int duration)
    {
        var toast = new ToastInstance
        {
            Severity = severity,
            Message = message,
            Title = title,
            Duration = duration
        };
        lock (_lock)
        {
            _toasts.Add(toast);
            if (duration > 0)
            {
                // Single-shot timer: fires once after `duration` ms, then never again.
                // Its callback runs on a thread-pool thread and re-enters via Remove(id),
                // which takes the same lock and disposes this very timer — safe per
                // System.Threading.Timer semantics (disposing from within its own callback
                // does not block or throw).
                var timer = new Timer(_ => Remove(toast.Id), null, duration, Timeout.Infinite);
                _autoDismissTimers[toast.Id] = timer;
            }
        }
        OnChange?.Invoke();
    }

    /// <summary>Disposes all pending auto-dismiss timers. Called automatically when the DI scope ends.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        lock (_lock)
        {
            foreach (var timer in _autoDismissTimers.Values)
                timer.Dispose();
            _autoDismissTimers.Clear();
            _disposed = true;
        }
    }
}
