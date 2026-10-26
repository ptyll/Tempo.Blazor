using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Chat;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Chat;

/// <summary>
/// Typing indicator contract of <see cref="TmChat"/> (FZE-4): the host may replace the localized
/// text with its own label (<c>TypingLabel</c>), replace the whole indicator with its own markup
/// (<c>TypingTemplate</c>), hide the whole indicator from assistive technology
/// (<c>TypingAriaHidden</c>) and auto-hide it after a period of silence
/// (<c>TypingTimeout</c>, driven by the injected <see cref="TimeProvider"/>).
/// <para>
/// TDD note: this file ran RED without parameterizing the four new knobs (a bUnit test for a
/// parameter that does not exist yet cannot compile) and asserting the observable behaviour
/// instead — 8 failed / 3 passed, e.g. <c>TypingLabel_Func_OverridesLocalizedText</c> with
/// "Expected … 'Jana is typing…' vs 'Jana píše…'" and
/// <c>TypingTimeout_HidesIndicatorAfterExpiry</c> with the indicator still visible after
/// <c>FakeTimeProvider.Advance(31s)</c>. The <c>.Add</c> calls below are that staged red restored
/// with the implementation.
/// </para>
/// </summary>
public class TmChatTypingTests : LocalizationTestBase
{
    private static readonly ChatUser Me = new("u1", "Alice", avatar: "A");
    private static readonly ChatUser Jana = new("u2", "Jana", avatar: "J");
    private static readonly ChatUser Bob = new("u3", "Bob", avatar: "B");

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Registers a <see cref="FakeTimeProvider"/> as the component's clock. "The typing window
    /// has expired" then means <see cref="FakeTimeProvider.Advance"/> crossed it — a timer that
    /// survived disposal would fire synchronously inside Advance, inside the test.
    /// </summary>
    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    /// <summary>
    /// Drains renderer-dispatched work queued by timer callbacks (the callback hops to the
    /// renderer via InvokeAsync; awaiting a queued no-op guarantees every earlier delivery has
    /// run when the assert executes).
    /// </summary>
    private static Task DrainDispatcherAsync(IRenderedComponent<TmChat> cut)
        => cut.InvokeAsync(() => { });

    // ── TypingLabel ──────────────────────────────────────────────────────────

    [Fact]
    public void TypingLabel_Func_OverridesLocalizedText()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingLabel, users => $"{users[0].Name} píše…"));

        cut.Find(".tm-chat__typing-text").TextContent.Should().Be("Jana píše…");
    }

    [Fact]
    public void TypingLabel_Null_KeepsLocalizedText()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana }));

        cut.Find(".tm-chat__typing-text").TextContent.Should().Be("Jana is typing…");
    }

    // ── TypingTemplate ───────────────────────────────────────────────────────

    [Fact]
    public void TypingTemplate_ReplacesIndicator()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTemplate, (IReadOnlyList<ChatUser> users) => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "my-typing");
                builder.AddContent(2, $"{users[0].Name} píše…");
                builder.CloseElement();
            }));

        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("the template replaces the built-in indicator");
        cut.Find(".my-typing").TextContent.Should().Contain("Jana píše…");
    }

    // ── TypingAriaHidden ─────────────────────────────────────────────────────

    [Fact]
    public void TypingAriaHidden_True_HidesWholeIndicatorFromAT()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingAriaHidden, true));

        var indicator = cut.Find(".tm-chat__typing-indicator");
        indicator.GetAttribute("aria-hidden").Should().Be("true",
            "the WHOLE indicator is hidden from assistive technology, not just the dots");
        cut.Find(".tm-chat__typing-dots").GetAttribute("aria-hidden").Should().Be("true");
    }

    [Fact]
    public void TypingAriaHidden_Default_KeepsCurrentMarkup()
    {
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana }));

        var indicator = cut.Find(".tm-chat__typing-indicator");
        indicator.HasAttribute("aria-hidden").Should().BeFalse(
            "the default must keep today's markup: no aria-hidden on the wrapper");
        cut.Find(".tm-chat__typing-dots").GetAttribute("aria-hidden").Should().Be("true",
            "the decorative dots keep their own aria-hidden");
        cut.Find(".tm-chat__typing-text").TextContent.Should().NotBeEmpty();
    }

    // ── TypingTimeout ────────────────────────────────────────────────────────

    [Fact]
    public async Task TypingTimeout_HidesIndicatorAfterExpiry()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout));

        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull("the indicator is visible while typing");

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);

        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty(
            "the indicator expires one TypingTimeout after the last TypingUsers change");
    }

    [Fact]
    public async Task TypingTimeout_RestartsWhenTypingUsersChange()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(TimeSpan.FromSeconds(15));
        cut.Render(p => p.Add(c => c.TypingUsers, new[] { Jana, Bob }));

        time.Advance(TimeSpan.FromSeconds(16));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().NotBeEmpty(
            "only 16s of the new 30s window have passed since the users changed");

        time.Advance(TimeSpan.FromSeconds(15));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty(
            "the restarted window expired 31s after the change");
    }

    [Fact]
    public async Task TypingTimeout_Null_NeverExpires()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana }));

        time.Advance(TimeSpan.FromMinutes(10));
        await DrainDispatcherAsync(cut);

        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull(
            "no TypingTimeout (the default) means the indicator follows TypingUsers and never expires");
    }

    [Fact]
    public async Task TypingTimeout_Expiry_DoesNotMutateHostTypingUsers()
    {
        var time = UseFakeClock();
        var hostUsers = new[] { Jana }; // the host's own list instance
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, hostUsers)
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);

        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("the indicator is hidden after expiry");
        hostUsers.Should().ContainSingle("the component hides its own indicator and never mutates the host's list");
        cut.Instance.TypingIndicatorExpired.Should().BeTrue("the expiry is the component's internal state");
    }

    /// <summary>
    /// The change detector accepts a NEW LIST INSTANCE with the same content as a change (the
    /// host's signal "the typing state was refreshed"), documented on <see cref="TmChat.TypingTimeout"/>.
    /// A new instance must therefore OPEN a fresh window after an elapsed one.
    /// </summary>
    [Fact]
    public async Task TypingTimeout_NewInstanceSameContent_RestartsExpiredWindow()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("expired");

        cut.Render(p => p.Add(c => c.TypingUsers, new[] { Jana })); // new instance, same content
        cut.Instance.TypingIndicatorExpired.Should().BeFalse("a change resets the expiry");
        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull();
        cut.Instance.TypingTimerIsPending.Should().BeTrue("a fresh window armed a new timer");

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty();
    }

    /// <summary>An IDENTICAL list reference with unchanged content must NOT extend the window.</summary>
    [Fact]
    public async Task TypingTimeout_SameReference_NoRestart()
    {
        var time = UseFakeClock();
        var users = new[] { Jana };
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, users)
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(TimeSpan.FromSeconds(10));
        cut.Render(); // a parent re-render with the SAME list reference
        await DrainDispatcherAsync(cut);
        cut.Instance.TypingTimerIsPending.Should().BeTrue("the original window is still armed");

        time.Advance(TimeSpan.FromSeconds(21));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty(
            "re-renders with the same reference must not extend the 30s window");
    }

    /// <summary>
    /// The list is stored by REFERENCE, so the change detector compares the content against a
    /// value SNAPSHOT: an in-place mutation of the SAME list instance (a host that keeps one
    /// list and adds a user to it) must restart the window just like a new instance would.
    /// </summary>
    [Fact]
    public async Task TypingTimeout_SameReferenceMutatedInPlace_RestartsWindow()
    {
        var time = UseFakeClock();
        var users = new List<ChatUser> { Jana }; // the host keeps ONE list instance
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, users)
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("expired");

        users.Add(Bob); // in-place mutation — the reference never changes
        cut.Render(p => p.Add(c => c.TypingUsers, users)); // the host re-renders with the SAME instance

        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull(
            "an in-place mutation of the same list instance is detected through the content snapshot");
        cut.Instance.TypingIndicatorExpired.Should().BeFalse("the mutation reopens the window");

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("the restarted window expired again");
    }

    [Fact]
    public async Task TypingTimeout_ReappearsWhenUsersChangeAfterExpiry()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout));

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty("expired");

        cut.Render(p => p.Add(c => c.TypingUsers, new[] { Jana, Bob }));
        time.Advance(TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);

        cut.Find(".tm-chat__typing-indicator").Should().NotBeNull(
            "a new typing event opens a fresh window after an expiry");
    }

    /// <summary>
    /// The <see cref="TmChat.TypingTimeout"/> expiry applies to a <see cref="TmChat.TypingTemplate"/>
    /// TOO — an expired window renders NEITHER the built-in indicator NOR the template.
    /// </summary>
    [Fact]
    public async Task TypingTemplate_ExpiresWithTypingTimeout()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout)
            .Add(c => c.TypingTemplate, (IReadOnlyList<ChatUser> users) => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "my-typing");
                builder.AddContent(2, $"{users[0].Name} píše…");
                builder.CloseElement();
            }));

        cut.Find(".my-typing").Should().NotBeNull("the template renders while the window is open");

        time.Advance(Timeout + TimeSpan.FromSeconds(1));
        await DrainDispatcherAsync(cut);

        cut.FindAll(".my-typing").Should().BeEmpty(
            "an expired window renders neither the built-in indicator nor the template");
        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty();
    }

    /// <summary>
    /// A non-positive <see cref="TmChat.TypingTimeout"/> (zero or negative) expires IMMEDIATELY:
    /// the indicator is suppressed from the first render and no timer is ever armed.
    /// </summary>
    [Fact]
    public void TypingTimeout_NonPositive_ExpiresImmediately()
    {
        UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, TimeSpan.Zero));

        cut.FindAll(".tm-chat__typing-indicator").Should().BeEmpty(
            "a zero timeout expires immediately, so the indicator never shows");
        cut.Instance.TypingIndicatorExpired.Should().BeTrue();
        cut.Instance.TypingTimerIsPending.Should().BeFalse("immediate expiry arms no timer");
    }

    // ── Dispose ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispose_CancelsTypingTimer()
    {
        var time = UseFakeClock();
        var cut = Render<TmChat>(p => p
            .Add(c => c.CurrentUser, Me)
            .Add(c => c.TypingUsers, new[] { Jana })
            .Add(c => c.TypingTimeout, Timeout));

        cut.Instance.TypingTimerIsPending.Should().BeTrue("the expiry timer is armed while users are typing");
        await ((IAsyncDisposable)cut.Instance).DisposeAsync();
        cut.Instance.TypingTimerIsPending.Should().BeFalse("DisposeAsync must cancel the pending typing timer");

        var act = () => time.Advance(Timeout + TimeSpan.FromSeconds(1));
        act.Should().NotThrow("a cancelled timer must never fire into a disposed component");
    }
}
