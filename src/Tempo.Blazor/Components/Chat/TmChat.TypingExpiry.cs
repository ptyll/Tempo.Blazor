using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Models;
using System.Threading;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Typing-expiry half of <see cref="TmChat"/> (FZE-4): the clock, the single-shot expiry timer
/// and the re-arm logic behind <c>TypingTimeout</c>. The clock is resolved through
/// <see cref="IServiceProvider"/> so a consumer that never registers a <see cref="TimeProvider"/>
/// still gets <see cref="TimeProvider.System"/> — tests register a FakeTimeProvider (the same
/// pattern as <c>TmSearchInput</c>'s debounce clock). Split out of <c>TmChat.razor.cs</c> to keep
/// that file under the 1200-line source ceiling (N270/F13); behavior is unchanged.
/// </summary>
public partial class TmChat
{
    // ── Typing expiry state ──

    /// <summary>
    /// Clock behind the typing-expiry timer. Resolved through <see cref="IServiceProvider"/> so a
    /// consumer that never registers a <see cref="TimeProvider"/> still gets
    /// <see cref="TimeProvider.System"/> — tests register a FakeTimeProvider (the same pattern as
    /// <c>TmSearchInput</c>'s debounce clock).
    /// </summary>
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private TimeProvider Clock
        => _clock ??= Services.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
    private TimeProvider? _clock;

    private ITimer? _typingTimer;

    /// <summary>
    /// Monotonic generation of the typing timer: bumped when a timer is armed and when one is
    /// cancelled, so a fired callback can tell whether it still owns the CURRENT window.
    /// </summary>
    private int _typingTimerGen;

    private IReadOnlyList<ChatUser>? _lastTypingUsers;
    private ChatUser[] _lastTypingSnapshot = [];
    private TimeSpan? _lastTypingTimeout;
    private bool _typingExpired;

    /// <summary>
    /// (Re)arms the typing-expiry window. A CHANGE of <see cref="TypingUsers"/> — a new list
    /// instance, a different content OR an in-place mutation of the same list instance (see
    /// <see cref="TypingTimeout"/>) — resets the expiry and opens a fresh window; a change of
    /// <see cref="TypingTimeout"/> alone rearms the pending timer (e.g. null → a duration starts
    /// measuring now) without reopening an already elapsed window. An unchanged list and timeout
    /// keep the running deadline — re-arming on every parent re-render would extend the window
    /// forever.
    /// </summary>
    private void ScheduleTypingExpiry()
    {
        // The list is stored BY REFERENCE, so an in-place mutation of the same instance is never
        // visible through it — the content comparison runs against a value SNAPSHOT of the last
        // seen list instead.
        var usersChanged = !ReferenceEquals(TypingUsers, _lastTypingUsers)
            || !TypingUsersContentEquals(TypingUsers, _lastTypingSnapshot);
        var timeoutChanged = !Nullable.Equals(TypingTimeout, _lastTypingTimeout);
        _lastTypingUsers = TypingUsers;
        _lastTypingTimeout = TypingTimeout;
        _lastTypingSnapshot = TypingUsers.ToArray();

        if (usersChanged) _typingExpired = false;
        if (!usersChanged && !timeoutChanged) return;
        RearmTypingTimer();
    }

    /// <summary>Order-sensitive per-item value comparison of two typing-user lists.</summary>
    private static bool TypingUsersContentEquals(IReadOnlyList<ChatUser> current, IReadOnlyList<ChatUser>? previous)
    {
        if (previous is null || current.Count != previous.Count) return false;
        for (var i = 0; i < current.Count; i++)
        {
            if (!current[i].Equals(previous[i])) return false;
        }
        return true;
    }

    /// <summary>Disarms any pending timer and arms a fresh single-shot one when an expiry applies.</summary>
    private void RearmTypingTimer()
    {
        CancelTypingTimer();
        if (TypingTimeout is not { } timeout || TypingUsers.Count == 0 || _typingExpired) return;
        if (timeout <= TimeSpan.Zero)
        {
            // A non-positive timeout expires IMMEDIATELY: no timer, the next render suppresses.
            _typingExpired = true;
            return;
        }

        // Single-shot timer on the injected clock. The callback closes over `timer` AND the
        // generation: the ReferenceEquals check inside can tell "the currently armed timer" apart
        // from one that was already replaced or cancelled, and the generation check invalidates a
        // callback whose window was cancelled or superseded between firing and dispatching.
        var gen = ++_typingTimerGen;
        ITimer? timer = null;
        timer = Clock.CreateTimer(_ => _ = OnTypingExpiredAsync(timer, gen), null, timeout, Timeout.InfiniteTimeSpan);
        _typingTimer = timer;
    }

    private void CancelTypingTimer()
    {
        // Bump the generation FIRST: a callback already in flight must observe a cancelled
        // window even if it races this call on another thread.
        _typingTimerGen++;
        _typingTimer?.Dispose();
        _typingTimer = null;
    }

    private async Task OnTypingExpiredAsync(ITimer? timer, int generation)
    {
        if (timer is null || _disposed) return;
        // The callback runs on a thread-pool thread (TimeProvider.System) or the test thread
        // advancing a FakeTimeProvider; hop to the renderer before touching expiry state.
        try
        {
            await InvokeAsync(() =>
            {
                if (_disposed
                    || generation != _typingTimerGen
                    || !ReferenceEquals(_typingTimer, timer))
                {
                    return Task.CompletedTask;
                }
                // Single-shot: once fired it can never expire again — disarm so
                // TypingTimerIsPending reports the truth instead of a spent timer.
                _typingTimer = null;
                timer.Dispose();
                _typingExpired = true;
                StateHasChanged();
                return Task.CompletedTask;
            });
        }
        catch (ObjectDisposedException)
        {
            // The renderer (or the whole component) was disposed between the timer firing and the
            // dispatch — the expiry no longer has anything to render into, not an error.
        }
    }
}
