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
}
