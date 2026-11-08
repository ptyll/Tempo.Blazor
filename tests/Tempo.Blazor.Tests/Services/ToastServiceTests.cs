using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Tests.Services;

/// <summary>TDD tests for ToastService.</summary>
public class ToastServiceTests
{
    [Fact]
    public void ShowSuccess_AddsSuccessToast()
    {
        var svc = new ToastService();
        svc.ShowSuccess("Done!");

        svc.Toasts.Should().HaveCount(1);
        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Success);
        svc.Toasts[0].Message.Should().Be("Done!");
    }

    [Fact]
    public void ShowError_AddsErrorToast()
    {
        var svc = new ToastService();
        svc.ShowError("Failed!", "Error Title");

        svc.Toasts.Should().HaveCount(1);
        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Error);
        svc.Toasts[0].Title.Should().Be("Error Title");
    }

    [Fact]
    public void ShowWarning_AddsWarningToast()
    {
        var svc = new ToastService();
        svc.ShowWarning("Watch out");

        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Warning);
    }

    [Fact]
    public void ShowInfo_AddsInfoToast()
    {
        var svc = new ToastService();
        svc.ShowInfo("FYI", duration: 3000);

        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Info);
        svc.Toasts[0].Duration.Should().Be(3000);
    }

    [Fact]
    public void OnChange_FiresWhenToastAdded()
    {
        var svc = new ToastService();
        int callCount = 0;
        svc.OnChange += () => callCount++;

        svc.ShowSuccess("Test");

        callCount.Should().Be(1);
    }

    [Fact]
    public void Remove_RemovesToastById()
    {
        var svc = new ToastService();
        svc.ShowSuccess("One");
        svc.ShowError("Two");
        var idToRemove = svc.Toasts[0].Id;

        svc.Remove(idToRemove);

        svc.Toasts.Should().HaveCount(1);
        svc.Toasts[0].Message.Should().Be("Two");
    }

    [Fact]
    public void Clear_RemovesAllToasts()
    {
        var svc = new ToastService();
        svc.ShowSuccess("One");
        svc.ShowError("Two");
        svc.ShowWarning("Three");

        svc.Clear();

        svc.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void OnChange_FiresOnRemove()
    {
        var svc = new ToastService();
        svc.ShowSuccess("Test");
        int callCount = 0;
        svc.OnChange += () => callCount++;

        svc.Remove(svc.Toasts[0].Id);

        callCount.Should().Be(1);
    }

    // ── Auto-dismiss ──
    // These tests run on a FakeTimeProvider: "the deadline has passed" is a test-controlled fact
    // via Advance(duration + 1ms), not a wall-clock race. A timer that survives its cancellation
    // fires synchronously inside Advance — inside the test, before the assertion — so a missing
    // Dispose can no longer hide behind a read taken too early.

    [Fact]
    public void AutoDismiss_RemovesToastAfterDuration()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("Auto", duration: 50);

        svc.Toasts.Should().HaveCount(1);

        time.Advance(TimeSpan.FromMilliseconds(50 + 1));
        svc.Toasts.Should().BeEmpty("the auto-dismiss timer fires deterministically once fake time crosses the duration");
    }

    [Fact]
    public void AutoDismiss_FiresOnChange_WhenToastAutoRemoved()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("Auto", duration: 50);
        int callCount = 0;
        svc.OnChange += () => Interlocked.Increment(ref callCount);

        time.Advance(TimeSpan.FromMilliseconds(50 + 1));

        Volatile.Read(ref callCount).Should().Be(1,
            "the auto-dismiss Remove raises OnChange exactly once when the timer fires");
    }

    [Fact]
    public void AutoDismiss_DurationZeroOrLess_NeverAutoRemoves()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("Sticky", duration: 0);
        svc.ShowError("AlsoSticky", duration: -1);

        svc.PendingAutoDismissCount.Should().Be(0,
            "duration <= 0 must never schedule an auto-dismiss timer");

        // Far past every deadline this library could schedule: a wrongly armed timer (a 0ms
        // timer fires immediately, a -1ms one is still armed) shows up inside this Advance.
        time.Advance(TimeSpan.FromMinutes(10));
        svc.Toasts.Should().HaveCount(2, "duration <= 0 means the toast is sticky and must never auto-dismiss");
        svc.PendingAutoDismissCount.Should().Be(0);
    }

    [Fact]
    public void Remove_BeforeTimerFires_CancelsPendingAutoDismiss_NoDoubleRemoveOrException()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("CancelMe", duration: 150);
        var id = svc.Toasts[0].Id;

        int changeCount = 0;
        svc.OnChange += () => Interlocked.Increment(ref changeCount);

        var act = () => svc.Remove(id);
        act.Should().NotThrow();
        changeCount.Should().Be(1, "the manual Remove should raise OnChange exactly once");

        svc.PendingAutoDismissCount.Should().Be(0,
            "Remove must drop the pending auto-dismiss timer, not just the toast");

        // The regression this guards: Remove removes the timer from the dictionary but forgets
        // Dispose — PendingAutoDismissCount already reads 0, yet the still-armed timer fires at
        // its deadline and re-enters Remove, raising OnChange a second time. Advance makes that
        // fire HERE, before the count is read — the old wall-clock assertion read it too early.
        time.Advance(TimeSpan.FromMilliseconds(150 + 1));
        changeCount.Should().Be(1, "the cancelled auto-dismiss timer must not fire after its deadline");
        svc.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Clear_CancelsAllPendingAutoDismissTimers_NoExceptionsLater()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("One", duration: 100);
        svc.ShowError("Two", duration: 120);

        int changeCount = 0;
        svc.OnChange += () => Interlocked.Increment(ref changeCount);

        svc.Clear();
        changeCount.Should().Be(1);

        svc.PendingAutoDismissCount.Should().Be(0,
            "Clear() must cancel every pending auto-dismiss timer");

        // Past the later deadline: any timer Clear failed to dispose fires inside this Advance
        // and would push changeCount above 1 before it is read.
        time.Advance(TimeSpan.FromMilliseconds(120 + 1));
        changeCount.Should().Be(1, "Clear() must cancel any pending auto-dismiss timers");
        svc.Toasts.Should().BeEmpty();
    }

    // ── Phase fze-3: ToastOptions, Key dedupe, ShowUndo, Update, Pause/Resume ──

    [Fact]
    public void Show_WithOptions_ReturnsId_AndAddsToast()
    {
        var svc = new ToastService();
        var id = svc.Show(new ToastOptions { Message = "Opt", Severity = ToastSeverity.Warning, Duration = 1234 });

        id.Should().NotBeNullOrEmpty();
        svc.Toasts.Should().ContainSingle();
        svc.Toasts[0].Id.Should().Be(id);
        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Warning);
        svc.Toasts[0].Duration.Should().Be(1234);
    }

    [Fact]
    public void Show_DefaultPoliteness_DerivesFromSeverity()
    {
        var svc = new ToastService();
        svc.ShowError("e");
        svc.ShowInfo("i");

        svc.Toasts[0].Politeness.Should().Be(ToastPoliteness.Assertive,
            "an error interrupts — it is announced assertively");
        svc.Toasts[1].Politeness.Should().Be(ToastPoliteness.Polite);
    }

    [Fact]
    public void Show_WithExplicitPoliteness_WinsOverDerivation()
    {
        var svc = new ToastService();
        svc.Show(new ToastOptions { Message = "m", Severity = ToastSeverity.Error, Politeness = ToastPoliteness.Polite });

        svc.Toasts[0].Politeness.Should().Be(ToastPoliteness.Polite);
    }

    [Fact]
    public void Show_WithKey_ReplacesExistingToastInPlace_KeepingIdAndPosition()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        var first = svc.Show(new ToastOptions { Message = "A", Key = "save" });
        svc.Show(new ToastOptions { Message = "Other" });

        var replacement = svc.Show(new ToastOptions { Message = "A2", Key = "save", Severity = ToastSeverity.Success });

        replacement.Should().Be(first, "Key dedupe reuses the id so the toast keeps its identity");
        svc.Toasts.Should().HaveCount(2, "the replacement does not grow the queue");
        svc.Toasts[0].Id.Should().Be(first, "the replacement keeps the ORIGINAL list position");
        svc.Toasts[0].Message.Should().Be("A2");
        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Success);
    }

    [Fact]
    public void Show_WithKey_RestartsAutoDismissTimer()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.Show(new ToastOptions { Message = "A", Key = "k", Duration = 50 });
        time.Advance(TimeSpan.FromMilliseconds(40));

        svc.Show(new ToastOptions { Message = "A2", Key = "k", Duration = 100 });

        time.Advance(TimeSpan.FromMilliseconds(60)); // t=100: the OLD deadline (50) has passed
        svc.Toasts.Should().ContainSingle("the replaced toast must not die on the OLD deadline");
        time.Advance(TimeSpan.FromMilliseconds(41)); // t=141 > the new deadline (140)
        svc.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void ShowUndo_AddsToastWithUndoAction()
    {
        var svc = new ToastService();

        var id = svc.ShowUndo("Item deleted", () => Task.CompletedTask);

        svc.Toasts.Should().ContainSingle();
        svc.Toasts[0].Id.Should().Be(id);
        svc.Toasts[0].Action.Should().NotBeNull();
        svc.Toasts[0].Action!.LabelKey.Should().Be(ToastAction.UndoLabelKey,
            "the label resolves through ITmLocalizer at render time");
        svc.Toasts[0].Action!.OnClick.Should().NotBeNull();
        svc.Toasts[0].Duration.Should().Be(10_000,
            "a fresh Undo toast uses the default, which sits at the MinActionDuration floor");
    }

    [Fact]
    public void ShowUndo_ExplicitShortDuration_IsFlooredToMinActionDuration()
    {
        var svc = new ToastService();
        svc.ShowUndo("Item deleted", () => Task.CompletedTask, duration: 2000);

        svc.Toasts[0].Duration.Should().Be(10_000,
            "an action button must be reachable for at least MinActionDuration (default 10s)");
    }

    [Fact]
    public void Show_WithAction_FloorsDurationToMinActionDuration()
    {
        var svc = new ToastService { MinActionDuration = 4000 };

        svc.Show(new ToastOptions { Message = "m", Duration = 500, Action = new ToastAction { Label = "Undo" } });

        svc.Toasts[0].Duration.Should().Be(4000);
    }

    [Fact]
    public void Show_WithoutAction_OrSticky_IsNeverFloored()
    {
        var svc = new ToastService { MinActionDuration = 10_000 };
        svc.ShowInfo("plain", duration: 2000);
        svc.Show(new ToastOptions { Message = "sticky", Duration = 0, Action = new ToastAction { Label = "A" } });

        svc.Toasts[0].Duration.Should().Be(2000, "action-less toasts keep their exact duration");
        svc.Toasts[1].Duration.Should().Be(0, "sticky toasts never auto-dismiss, so the floor does not apply");
    }

    [Fact]
    public void Show_WithKey_BumpsRevision_SoTheProgressAnimationRestarts()
    {
        var svc = new ToastService(new FakeTimeProvider());
        var id = svc.Show(new ToastOptions { Message = "A", Key = "k" });
        svc.Toasts[0].Revision.Should().Be(0);

        svc.Show(new ToastOptions { Message = "A2", Key = "k" });

        svc.Toasts[0].Id.Should().Be(id);
        svc.Toasts[0].Revision.Should().Be(1, "an in-place replacement bumps the revision");
    }

    [Fact]
    public void Update_BumpsRevision()
    {
        var svc = new ToastService(new FakeTimeProvider());
        svc.ShowInfo("x");
        var id = svc.Toasts[0].Id;

        svc.Update(id, message: "y");

        svc.Toasts[0].Revision.Should().Be(1);
    }

    [Fact]
    public void Update_ChangesMessageTitleSeverity_AndRestartsTimer()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("old", duration: 50);
        var id = svc.Toasts[0].Id;
        time.Advance(TimeSpan.FromMilliseconds(20));

        svc.Update(id, message: "new", title: "T", severity: ToastSeverity.Error);

        svc.Toasts[0].Message.Should().Be("new");
        svc.Toasts[0].Title.Should().Be("T");
        svc.Toasts[0].Severity.Should().Be(ToastSeverity.Error);

        time.Advance(TimeSpan.FromMilliseconds(31)); // t=51: the ORIGINAL deadline (50) has passed
        svc.Toasts.Should().ContainSingle("Update restarts the auto-dismiss timer");
        time.Advance(TimeSpan.FromMilliseconds(20)); // t=71 > the restarted deadline (20 + 50)
        svc.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Update_WithDuration_ChangesDurationAndRestartsTimer()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("x", duration: 50);
        var id = svc.Toasts[0].Id;

        svc.Update(id, duration: 100);

        svc.Toasts[0].Duration.Should().Be(100);
        time.Advance(TimeSpan.FromMilliseconds(101));
        svc.Toasts.Should().BeEmpty();
    }

    [Fact]
    public void Update_UnknownId_IsANoOp()
    {
        var svc = new ToastService();
        svc.ShowInfo("x");
        int changeCount = 0;
        svc.OnChange += () => changeCount++;

        svc.Update("no-such-id", message: "y");

        svc.Toasts[0].Message.Should().Be("x");
        changeCount.Should().Be(0, "the unknown id raised no OnChange (the subscription postdates the Show)");
    }

    [Fact]
    public void Pause_DisarmsTimer_AndToastSurvivesItsDeadline()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("x", duration: 100);
        var id = svc.Toasts[0].Id;
        time.Advance(TimeSpan.FromMilliseconds(40));

        svc.Pause(id);

        svc.IsPaused(id).Should().BeTrue();
        svc.PendingAutoDismissCount.Should().Be(0, "Pause disarms the auto-dismiss timer");

        time.Advance(TimeSpan.FromMinutes(5));
        svc.Toasts.Should().ContainSingle("a paused toast never auto-dismisses");
        svc.PendingAutoDismissCount.Should().Be(0, "the pause must not leak an armed timer");
    }

    [Fact]
    public void Resume_AfterPause_PreservesTheRemainingTime()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("x", duration: 100);
        var id = svc.Toasts[0].Id;
        time.Advance(TimeSpan.FromMilliseconds(40));
        svc.Pause(id);
        time.Advance(TimeSpan.FromMinutes(5));

        svc.Resume(id);

        svc.IsPaused(id).Should().BeFalse();
        svc.PendingAutoDismissCount.Should().Be(1, "Resume re-arms a single one-shot timer");

        time.Advance(TimeSpan.FromMilliseconds(59)); // 40 + 59 = 99 < 100
        svc.Toasts.Should().ContainSingle();
        time.Advance(TimeSpan.FromMilliseconds(2));
        svc.Toasts.Should().BeEmpty("only the REMAINING 60ms counted after the resume");
    }

    [Fact]
    public void Pause_StickyToast_IsANoOp()
    {
        var svc = new ToastService(new FakeTimeProvider());
        svc.ShowInfo("sticky", duration: 0);
        var id = svc.Toasts[0].Id;

        svc.Pause(id);

        svc.IsPaused(id).Should().BeFalse("a toast without an armed timer has nothing to pause");
    }

    [Fact]
    public void PauseAll_ThenResumeAll_PreservesEachRemainingTime()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("one", duration: 100);
        svc.ShowInfo("two", duration: 200);
        time.Advance(TimeSpan.FromMilliseconds(50));

        svc.PauseAll();
        svc.PendingAutoDismissCount.Should().Be(0);
        time.Advance(TimeSpan.FromMinutes(5));
        svc.Toasts.Should().HaveCount(2);

        svc.ResumeAll();

        time.Advance(TimeSpan.FromMilliseconds(51)); // one: 50 + 51 > 100
        svc.Toasts.Should().ContainSingle();
        svc.Toasts[0].Message.Should().Be("two", "only the shorter remaining time has elapsed");
    }

    [Fact]
    public void Regression_ShowHelpers_KeepDefaultDurationsAndBehavior()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowSuccess("s");              // 5000
        svc.ShowInfo("i");                 // 5000
        svc.ShowWarning("w", "t");         // 6000
        svc.ShowError("e");                // 8000

        svc.Toasts.Select(t => t.Duration).Should().Equal(5000, 5000, 6000, 8000);
        svc.PendingAutoDismissCount.Should().Be(4);
        svc.Toasts.Should().OnlyContain(t => t.Action == null && t.Content == null && t.Key == null,
            "the classic helpers stay free of the new optional state");

        time.Advance(TimeSpan.FromMilliseconds(5001));
        svc.Toasts.Select(t => t.Message).Should().Equal("w", "e");
        time.Advance(TimeSpan.FromMilliseconds(1000)); // t=6001
        svc.Toasts.Select(t => t.Message).Should().Equal("e");
        time.Advance(TimeSpan.FromMilliseconds(2000)); // t=8001
        svc.Toasts.Should().BeEmpty();
    }

    // ── Review round: stale timer callbacks, dispose semantics, paused mutations ──

    [Fact]
    public void AutoDismiss_FireThenDedupe_StaleDeadlineDoesNotRemoveTheReplacement()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.Show(new ToastOptions { Message = "A", Key = "k", Duration = 50 });

        time.Advance(TimeSpan.FromMilliseconds(51)); // fires: the toast is gone
        svc.Toasts.Should().BeEmpty();

        svc.Show(new ToastOptions { Message = "B", Key = "k", Duration = 200 });
        time.Advance(TimeSpan.FromMilliseconds(60)); // t=111: past the ORIGINAL 50ms deadline

        svc.Toasts.Should().ContainSingle("the replacement must not die on the stale deadline");
    }

    [Fact]
    public void AutoDismiss_StaleCallbackFromASupersededTimer_DoesNotRemoveTheReplacement()
    {
        // A real System.Threading.Timer can already be queued when its replacement disposes it.
        // The manual provider lets us fire the disposed timer anyway: the ReferenceEquals guard in
        // the callback must refuse to remove the toast the NEWER timer owns.
        var timers = new ManualTimerProvider();
        var svc = new ToastService(timers);
        var first = svc.Show(new ToastOptions { Message = "A", Key = "k", Duration = 100 });
        var stale = timers.Timers.Should().ContainSingle().Subject;

        svc.Show(new ToastOptions { Message = "A2", Key = "k", Duration = 100 });
        timers.Timers.Should().HaveCount(2, "the replacement arms a fresh timer");
        var live = timers.Timers[^1];

        stale.FireAnyway();

        svc.Toasts.Should().ContainSingle();
        svc.Toasts[0].Id.Should().Be(first, "the stale callback must not remove the replaced toast");
        svc.Toasts[0].Message.Should().Be("A2");
        svc.PendingAutoDismissCount.Should().Be(1, "the live timer stays armed");

        live.Fire();

        svc.Toasts.Should().BeEmpty("the live timer still dismisses at its deadline");
    }

    [Fact]
    public void Dispose_LateTimerCallback_IsSwallowed()
    {
        var timers = new ManualTimerProvider();
        var svc = new ToastService(timers);
        svc.ShowInfo("x", duration: 100);
        var timer = timers.Timers.Should().ContainSingle().Subject;
        var changes = 0;
        svc.OnChange += () => changes++;

        svc.Dispose();
        timer.FireAnyway();

        changes.Should().Be(0, "a callback queued before disposal but fired after is a no-op");
        svc.Toasts.Should().ContainSingle();
    }

    [Fact]
    public void Show_WithKey_OnAPausedToast_StaysPausedWithTheFullNewDuration()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        var id = svc.Show(new ToastOptions { Message = "A", Key = "k", Duration = 100 });
        time.Advance(TimeSpan.FromMilliseconds(40));
        svc.Pause(id);

        svc.Show(new ToastOptions { Message = "A2", Key = "k", Duration = 100 });

        svc.IsPaused(id).Should().BeTrue("dedupe of a paused toast keeps it paused");
        svc.PendingAutoDismissCount.Should().Be(0, "no timer is armed while paused");

        time.Advance(TimeSpan.FromMinutes(5)); // far past every deadline
        svc.Toasts.Should().ContainSingle("a paused replacement never auto-dismisses");

        svc.Resume(id);
        svc.PendingAutoDismissCount.Should().Be(1, "resume re-arms with the FULL new duration");
        time.Advance(TimeSpan.FromMilliseconds(99));
        svc.Toasts.Should().ContainSingle();
        time.Advance(TimeSpan.FromMilliseconds(2));
        svc.Toasts.Should().BeEmpty("the replacement credited a fresh 100ms, not the old remainder");
    }

    [Fact]
    public void Update_OnAPausedToast_StaysPausedWithTheFullNewDuration()
    {
        var time = new FakeTimeProvider();
        var svc = new ToastService(time);
        svc.ShowInfo("x", duration: 100);
        var id = svc.Toasts[0].Id;
        time.Advance(TimeSpan.FromMilliseconds(40));
        svc.Pause(id);

        svc.Update(id, message: "y", duration: 80);

        svc.IsPaused(id).Should().BeTrue("Update of a paused toast keeps it paused");
        svc.PendingAutoDismissCount.Should().Be(0);

        svc.Resume(id);
        time.Advance(TimeSpan.FromMilliseconds(79));
        svc.Toasts.Should().ContainSingle();
        time.Advance(TimeSpan.FromMilliseconds(2));
        svc.Toasts.Should().BeEmpty("resume re-arms the FULL updated 80ms");
    }

    /// <summary>TimeProvider whose timers only fire when the test fires them manually — even when
    /// disposed (simulating a real timer whose callback was already queued).</summary>
    private sealed class ManualTimerProvider : TimeProvider
    {
        public List<ManualTimer> Timers { get; } = [];

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer : ITimer
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private bool _disposed;

        public ManualTimer(TimerCallback callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        /// <summary>Normal fire: a disposed timer does not fire.</summary>
        public void Fire()
        {
            if (!_disposed)
                _callback(_state);
        }

        /// <summary>Fire regardless of disposal — a real timer already queued at disposal still runs.</summary>
        public void FireAnyway() => _callback(_state);

        public void Dispose() => _disposed = true;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        public bool Change(TimeSpan dueTime, TimeSpan period) => !_disposed;
    }
}
