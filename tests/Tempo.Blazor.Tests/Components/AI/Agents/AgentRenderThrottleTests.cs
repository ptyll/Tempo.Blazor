using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Tests.Components.AI.Agents;

/// <summary>
/// Plan d67b33af, Fáze 1 — the render side of the live-data contract: <see cref="AgentRenderThrottle"/> is the
/// shared 100 ms gate every agent visualization uses instead of re-rendering once per incoming event.
/// <para>
/// A hub that pushes ten events per second would otherwise paint ten renders per second per component, and on
/// Blazor Server every one of them is a circuit round trip. The gate is driven by an injected
/// <see cref="TimeProvider"/>, so a test (or a host with a fake clock) decides when time passes; nothing reads
/// the wall clock.
/// </para>
/// </summary>
public sealed class AgentRenderThrottleTests
{
    [Fact]
    public void TheDefaultInterval_IsOneHundredMilliseconds()
        => AgentRenderThrottle.DefaultInterval.Should().Be(TimeSpan.FromMilliseconds(100),
            "the plan's shared default: ten events per second collapse into one paint");

    [Fact]
    public void TheFirstCall_Renders()
    {
        var throttle = new AgentRenderThrottle(new FakeTimeProvider());

        throttle.ShouldRender().Should().BeTrue("the first event of a run must not be held back");
    }

    [Fact]
    public void CallsInsideTheInterval_AreSuppressed()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock);

        throttle.ShouldRender().Should().BeTrue();
        clock.Advance(TimeSpan.FromMilliseconds(99));

        throttle.ShouldRender().Should().BeFalse("99 ms is inside the 100 ms window");
    }

    [Fact]
    public void TheCallAtTheIntervalBoundary_Renders()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock);
        throttle.ShouldRender();

        clock.Advance(TimeSpan.FromMilliseconds(100));

        throttle.ShouldRender().Should().BeTrue("the window closes exactly at the interval");
    }

    [Fact]
    public void AHundredEventBurst_PaintsTenTimesPerSecond()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock);
        var renders = 0;

        for (var index = 0; index < 100; index++)
        {
            if (throttle.ShouldRender())
            {
                renders++;
            }

            clock.Advance(TimeSpan.FromMilliseconds(10));
        }

        renders.Should().Be(10,
            "the demo simulator pushes 10 events/s for two minutes — the burst has to collapse to the "
            + "documents' one paint per 100 ms");
    }

    [Fact]
    public void DelayUntilNextRender_IsZero_WhenTheStreamMayPaint()
    {
        var throttle = new AgentRenderThrottle(new FakeTimeProvider());

        throttle.DelayUntilNextRender().Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void DelayUntilNextRender_CountsDownInsideTheWindow()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock);
        throttle.ShouldRender();

        clock.Advance(TimeSpan.FromMilliseconds(30));

        throttle.DelayUntilNextRender().Should().Be(TimeSpan.FromMilliseconds(70),
            "a host scheduling a trailing render needs to know how long to wait — not merely that it must");
    }

    [Fact]
    public void DelayUntilNextRender_NeverGoesNegative()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock);
        throttle.ShouldRender();

        clock.Advance(TimeSpan.FromSeconds(5));

        throttle.DelayUntilNextRender().Should().Be(TimeSpan.Zero,
            "a clock that jumped must not produce a negative delay");
    }

    [Fact]
    public void AHostInterval_IsHonoured()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock, TimeSpan.FromSeconds(1));
        throttle.ShouldRender();

        clock.Advance(TimeSpan.FromMilliseconds(999));
        throttle.ShouldRender().Should().BeFalse();

        clock.Advance(TimeSpan.FromMilliseconds(1));
        throttle.ShouldRender().Should().BeTrue();
    }

    [Fact]
    public void AZeroInterval_RendersOnEveryCall()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock, TimeSpan.Zero);

        throttle.ShouldRender().Should().BeTrue();
        throttle.ShouldRender().Should().BeTrue("an interval of zero is how a host opts out of throttling");
    }

    [Fact]
    public void ANegativeInterval_IsTreatedAsNoThrottling()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock, TimeSpan.FromMilliseconds(-5));

        throttle.Interval.Should().Be(TimeSpan.Zero, "a negative window has no meaning");
        throttle.ShouldRender().Should().BeTrue();
        throttle.ShouldRender().Should().BeTrue();
    }

    [Fact]
    public void Reset_AllowsTheNextCallImmediately()
    {
        var clock = new FakeTimeProvider();
        var throttle = new AgentRenderThrottle(clock, TimeSpan.FromSeconds(5));
        throttle.ShouldRender();

        throttle.Reset();

        throttle.ShouldRender().Should().BeTrue(
            "a host that repainted for its own reason (a snapshot reload) must not have its next event "
            + "held back by the window the previous paint opened");
    }

    [Fact]
    public void TheClock_DefaultsToTheSystemClock()
    {
        var throttle = new AgentRenderThrottle();

        throttle.Interval.Should().Be(AgentRenderThrottle.DefaultInterval);
        throttle.ShouldRender().Should().BeTrue();
    }
}
