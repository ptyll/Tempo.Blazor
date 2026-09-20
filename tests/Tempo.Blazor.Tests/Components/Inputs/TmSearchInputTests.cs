using System.Collections.Concurrent;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmSearchInput.</summary>
public class TmSearchInputTests : LocalizationTestBase
{
    /// <summary>Debounce delay used by the timing tests.</summary>
    private const int DebounceMs = 60;

    /// <summary>
    /// Registers a FakeTimeProvider as the component's debounce clock. "The debounce deadline
    /// has passed" then means <see cref="FakeTimeProvider.Advance"/> crossed it — a timer that
    /// survived cancellation fires synchronously inside Advance, inside the test, so a missing
    /// cancellation can no longer hide behind an assertion read too early.
    /// </summary>
    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider();
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    /// <summary>
    /// Drains renderer-dispatched work queued by timer callbacks. A fired debounce callback hops
    /// to the renderer via InvokeAsync; awaiting a queued no-op behind it guarantees every
    /// earlier-queued delivery has already run when the assert executes.
    /// </summary>
    private static Task DrainDispatcherAsync(IRenderedComponent<TmSearchInput> cut)
        => cut.InvokeAsync(() => { });

    [Fact]
    public void TmSearchInput_Renders_Search_Input()
    {
        var cut = Render<TmSearchInput>();
        cut.Find("input[type='search']").Should().NotBeNull();
    }

    [Fact]
    public void TmSearchInput_Has_Search_Icon()
    {
        var cut = Render<TmSearchInput>();
        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    /// <summary>
    /// The search input carries BOTH icon modifiers the measured `.tm-input|tm-input-with-*-icon`
    /// pairs are about — left for the search icon, right for the clear button — on the same element
    /// as `tm-input` (Fáze 18.1: the compound `.tm-input.tm-input-with-*-icon` only reaches markup
    /// that keeps emitting them together).
    /// </summary>
    [Fact]
    public void TmSearchInput_CarriesBaseAndBothIconModifiers_OnTheSameInput()
    {
        var cut = Render<TmSearchInput>();

        cut.Find("input").ClassList.Should()
            .Contain("tm-input")
            .And.Contain("tm-input-with-left-icon")
            .And.Contain("tm-input-with-right-icon");
    }

    [Fact]
    public void TmSearchInput_Default_Placeholder()
    {
        var cut = Render<TmSearchInput>();
        cut.Find("input").GetAttribute("placeholder").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TmSearchInput_Custom_Placeholder()
    {
        var cut = Render<TmSearchInput>(p => p.Add(c => c.Placeholder, "Find users..."));
        cut.Find("input").GetAttribute("placeholder").Should().Be("Find users...");
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Hidden_When_Value_Empty()
    {
        var cut = Render<TmSearchInput>(p => p.Add(c => c.Value, ""));
        cut.FindAll(".tm-search-clear").Should().BeEmpty();
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Shown_When_Value_Set()
    {
        var cut = Render<TmSearchInput>(p => p.Add(c => c.Value, "hello"));
        cut.Find(".tm-search-clear").Should().NotBeNull();
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Fires_Empty_String()
    {
        string? captured = null;
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.Value, "hello")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find(".tm-search-clear").Click();

        captured.Should().Be("");
    }

    [Fact]
    public void TmSearchInput_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmSearchInput>(p => p.Add(c => c.Disabled, true));
        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSearchInput_ValueChanged_Fires_On_Input()
    {
        string? captured = null;
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("input").Input("test");

        captured.Should().Be("test");
    }

    // --- Single delivery per edit -------------------------------------------------------------
    // The input element raises both `input` and `change` for one user edit. Both are bound to
    // ValueChanged, so every edit used to be delivered twice — with DebounceMs the two deliveries
    // were DebounceMs apart, which produced two concurrent searches in consuming applications.

    /// <summary>
    /// The regression case. The consumer binds ONLY ValueChanged and never supplies Value, so the
    /// duplicate cannot be suppressed by comparing against the Value parameter — it would stay
    /// string.Empty forever.
    /// </summary>
    [Fact]
    public async Task TmSearchInput_Debounced_Input_Then_Change_Delivers_Once_When_Value_Not_Supplied()
    {
        var time = UseFakeClock();
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        var input = cut.Find("input");
        input.Input("abc");   // starts the debounce timer
        input.Change("abc");  // blur/Enter for the same edit — delivers immediately

        delivered.Should().ContainSingle().Which.Should().Be("abc");
        cut.Instance.HasPendingDebounce.Should().BeFalse("`change` must disarm the pending debounce");

        // The barrier is fake time crossing the deadline: a timer `change` failed to cancel fires
        // HERE — inside the test, before the assert — and would take the delivery count to 2.
        time.Advance(TimeSpan.FromMilliseconds(DebounceMs + 1));
        await DrainDispatcherAsync(cut);
        delivered.Should().ContainSingle().Which.Should().Be("abc");
    }

    /// <summary>
    /// The sequence cancelling the debounce timer alone does NOT fix: type, pause long enough for
    /// the debounce to deliver, then blur. `change` arrives with no timer left to cancel, so the
    /// second delivery can only be suppressed by remembering what was already dispatched. The
    /// consumer again supplies no Value, so a Value-based comparison would not suppress it.
    /// </summary>
    [Fact]
    public async Task TmSearchInput_Debounce_Elapsed_Then_Change_Delivers_Once_When_Value_Not_Supplied()
    {
        var time = UseFakeClock();
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        var input = cut.Find("input");
        input.Input("abc");

        // The debounce delivers exactly when fake time crosses the deadline — not "within some
        // wall-clock timeout", so a slow CI box cannot reorder the observation.
        time.Advance(TimeSpan.FromMilliseconds(DebounceMs + 1));
        await DrainDispatcherAsync(cut);
        delivered.Should().ContainSingle().Which.Should().Be("abc"); // the debounce delivered

        input.Change("abc"); // blur for the same, unchanged text

        delivered.Should().ContainSingle().Which.Should().Be("abc");
    }

    /// <summary>Same edit, same guarantee, with Value supplied — the other five consumers.</summary>
    [Fact]
    public async Task TmSearchInput_Debounced_Input_Then_Change_Delivers_Once_When_Value_Supplied()
    {
        var time = UseFakeClock();
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.Value, string.Empty)
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        var input = cut.Find("input");
        input.Input("abc");
        input.Change("abc");

        cut.Instance.HasPendingDebounce.Should().BeFalse("`change` must disarm the pending debounce");
        time.Advance(TimeSpan.FromMilliseconds(DebounceMs + 1));
        await DrainDispatcherAsync(cut);
        delivered.Should().ContainSingle().Which.Should().Be("abc");
    }

    [Fact]
    public void TmSearchInput_Undebounced_Input_Then_Change_Delivers_Once()
    {
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        var input = cut.Find("input");
        input.Input("abc");
        input.Change("abc");

        delivered.Should().ContainSingle().Which.Should().Be("abc");
    }

    /// <summary>
    /// `change` must stay bound: browser autofill and the native search clear cross can raise it
    /// without a preceding `input` this component saw.
    /// </summary>
    [Fact]
    public void TmSearchInput_Change_Alone_Is_Delivered()
    {
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        cut.Find("input").Change("autofilled");

        delivered.Should().ContainSingle().Which.Should().Be("autofilled");
    }

    [Fact]
    public async Task TmSearchInput_Debounced_Input_Alone_Is_Delivered_After_Delay()
    {
        var time = UseFakeClock();
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        cut.Find("input").Input("abc");

        delivered.Should().BeEmpty(); // debounced, not synchronous

        // Not before the deadline, not without it: only Advance crossing the deadline delivers.
        time.Advance(TimeSpan.FromMilliseconds(DebounceMs + 1));
        await DrainDispatcherAsync(cut);
        delivered.Should().ContainSingle().Which.Should().Be("abc");
        cut.Instance.HasPendingDebounce.Should().BeFalse("a fired single-shot timer must disarm itself");
    }

    [Fact]
    public void TmSearchInput_Distinct_Values_Are_All_Delivered()
    {
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        var input = cut.Find("input");
        input.Input("a");
        input.Input("ab");
        input.Change("abc");

        delivered.Should().Equal("a", "ab", "abc");
    }

    /// <summary>
    /// Suppressing a repeated value must not outlive the consumer resetting Value itself. After an
    /// external reset (a "clear filters" button, a restored saved search) the box no longer holds
    /// what was last dispatched, so retyping that same text has to reach the consumer again.
    /// </summary>
    [Fact]
    public void TmSearchInput_External_Value_Reset_Allows_Retyping_The_Same_Text()
    {
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.Value, string.Empty)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        cut.Find("input").Change("abc");
        delivered.Should().Equal("abc");

        cut.Render(p => p.Add(c => c.Value, "abc"));        // consumer stores it
        cut.Render(p => p.Add(c => c.Value, string.Empty)); // consumer resets it itself

        cut.Find("input").Change("abc"); // user retypes the same text

        delivered.Should().Equal("abc", "abc");
    }

    /// <summary>
    /// A consumer that binds only ValueChanged re-renders while the search runs (results arrive,
    /// a spinner toggles), which sets this component's parameters again with Value still
    /// string.Empty. That must not be mistaken for an external reset — otherwise the suppression is
    /// disarmed between the input and the change of a single edit, which is the original defect.
    /// </summary>
    [Fact]
    public void TmSearchInput_Parent_Rerender_Does_Not_Disarm_Suppression_When_Value_Not_Supplied()
    {
        var delivered = new ConcurrentQueue<string>();
        var callback = EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v));
        var cut = Render<TmSearchInput>(p => p.Add(c => c.ValueChanged, callback));

        cut.Find("input").Input("abc");
        cut.Render(p => p.Add(c => c.ValueChanged, callback)); // parent re-renders, Value still absent
        cut.Find("input").Change("abc");                       // blur for the same edit

        delivered.Should().Equal("abc");
    }

    /// <summary>
    /// The consumer feeding Value back after a dispatch is the normal round trip and must NOT count
    /// as an external reset — otherwise the duplicate suppression would be disarmed on every edit.
    /// </summary>
    [Fact]
    public void TmSearchInput_Value_Fed_Back_By_Consumer_Keeps_Suppressing_The_Duplicate()
    {
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.Value, string.Empty)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        cut.Find("input").Input("abc");
        cut.Render(p => p.Add(c => c.Value, "abc")); // the @bind-Value round trip
        cut.Find("input").Change("abc");                             // blur for the same edit

        delivered.Should().Equal("abc");
    }

    /// <summary>A pending debounce carrying the old text must not overwrite an explicit clear.</summary>
    [Fact]
    public async Task TmSearchInput_Clear_Cancels_Pending_Debounce()
    {
        var time = UseFakeClock();
        var delivered = new ConcurrentQueue<string>();
        var cut = Render<TmSearchInput>(p => p
            .Add(c => c.Value, "hello")
            .Add(c => c.DebounceMs, DebounceMs)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => delivered.Enqueue(v))));

        cut.Find("input").Input("abc"); // starts the debounce timer
        cut.Find(".tm-search-clear").Click();

        // The clear delivered synchronously; the pending debounce must never deliver "abc" after it.
        delivered.Should().Equal(string.Empty);
        cut.Instance.HasPendingDebounce.Should().BeFalse("clear must disarm the pending debounce");

        // A timer the clear failed to cancel fires inside this Advance — before the assert —
        // and would append "abc" behind the empty string. No wall-clock window to hide in.
        time.Advance(TimeSpan.FromMilliseconds(DebounceMs + 1));
        await DrainDispatcherAsync(cut);
        delivered.Should().Equal(string.Empty);
    }
}
