using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>
/// TG-010 async-pipeline tests for TmCommandPalette (decision DEC-CMDPALETTE-ASYNC): the
/// SearchProvider source with debounce on a FakeTimeProvider, cancellation of superseded
/// requests, stale-response dropping, MinChars, Transient + retry, Empty, host-driven IsLoading,
/// the polite live region and highlight preservation by action Id. Local results always render
/// before async results (O3).
/// </summary>
public class TmCommandPaletteAsyncTests : LocalizationTestBase
{
    private readonly FakeTimeProvider _clock = new();

    public TmCommandPaletteAsyncTests()
    {
        Services.AddSingleton<TimeProvider>(_clock);
    }

    private sealed class ProviderCall(string query, CancellationToken token)
    {
        public string Query { get; } = query;
        public CancellationToken Token { get; } = token;
    }

    private sealed class ProviderStub
    {
        private readonly Func<string, CancellationToken, Task<CommandPaletteSearchResult>> _handler;
        public List<ProviderCall> Calls { get; } = [];

        public ProviderStub(Func<string, CancellationToken, Task<CommandPaletteSearchResult>> handler)
            => _handler = handler;

        public Func<string, CancellationToken, Task<CommandPaletteSearchResult>> Delegate =>
            (query, token) =>
            {
                Calls.Add(new ProviderCall(query, token));
                return _handler(query, token);
            };
    }

    private static CommandPaletteSearchResult Found(params ICommandPaletteAction[] items)
        => CommandPaletteSearchResult.Found(items);

    private IRenderedComponent<TmCommandPalette> RenderPalette(
        Action<ComponentParameterCollectionBuilder<TmCommandPalette>> configure)
        => Render<TmCommandPalette>(p =>
        {
            p.Add(c => c.IsOpen, true);
            configure(p);
        });

    private static void Advance(IRenderedComponent<TmCommandPalette> cut, FakeTimeProvider clock, TimeSpan by)
    {
        // The timer callback hops onto the renderer via InvokeAsync; give the dispatcher a beat
        // after advancing the fake clock so the search state is observable.
        cut.InvokeAsync(() => { clock.Advance(by); return Task.CompletedTask; }).GetAwaiter().GetResult();
    }

    // ── Debounce ─────────────────────────────────────────────────────────────

    [Fact]
    public void SearchProvider_Called_Once_After_Debounce_For_A_Keystroke_Series()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Remote"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("a");
        cut.Find(".tm-command-palette-input").Input("ab");
        cut.Find(".tm-command-palette-input").Input("abc");

        stub.Calls.Should().BeEmpty("the debounce window has not elapsed");

        Advance(cut, _clock, TimeSpan.FromMilliseconds(300));

        stub.Calls.Should().ContainSingle("a rapid series of keystrokes collapses into one request");
        stub.Calls[0].Query.Should().Be("abc", "the newest query wins");
    }

    [Fact]
    public void SearchProvider_With_DebounceMs_Zero_Called_Immediately()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Remote"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("go");

        stub.Calls.Should().ContainSingle("DebounceMs 0 fires the search without a timer");
    }

    // ── Cancellation and stale responses ─────────────────────────────────────

    [Fact]
    public void New_Keystroke_Cancels_The_Running_Request()
    {
        var gates = new Queue<TaskCompletionSource<CommandPaletteSearchResult>>();
        var stub = new ProviderStub((_, _) => gates.Dequeue().Task);
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        var first = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        gates.Enqueue(first);
        gates.Enqueue(second);

        cut.Find(".tm-command-palette-input").Input("a");
        stub.Calls.Should().ContainSingle();

        cut.Find(".tm-command-palette-input").Input("ab");
        stub.Calls.Should().HaveCount(2);
        stub.Calls[0].Token.IsCancellationRequested.Should().BeTrue(
            "superseding a request cancels its token");

        // The stale request finishes anyway (its gate released late): its result must be dropped.
        first.SetResult(Found(new CommandPaletteAction("stale", "Stale")));
        cut.WaitForAssertion(() => cut.FindAll(".tm-command-palette-item").Should().BeEmpty(),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Stale_Response_Without_Cooperative_Cancellation_Is_Dropped()
    {
        // The provider ignores the token; ordering alone (the request sequence number) must drop
        // the older response.
        var gates = new Queue<TaskCompletionSource<CommandPaletteSearchResult>>();
        var stub = new ProviderStub((_, _) => gates.Dequeue().Task);
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        var slow = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fast = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        gates.Enqueue(slow);
        gates.Enqueue(fast);

        cut.Find(".tm-command-palette-input").Input("a");
        cut.Find(".tm-command-palette-input").Input("ab");

        fast.SetResult(Found(new CommandPaletteAction("new", "Newest")));
        slow.SetResult(Found(new CommandPaletteAction("old", "Stale")));

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-item").Should().ContainSingle();
            cut.Find(".tm-command-palette-title").TextContent.Trim().Should().Be("Newest");
        }, TimeSpan.FromSeconds(2));
    }

    // ── MinChars ─────────────────────────────────────────────────────────────

    [Fact]
    public void MinChars_Suppresses_Provider_Until_The_Query_Is_Long_Enough()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Remote"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.MinChars, 3)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("ab");
        Advance(cut, _clock, TimeSpan.FromMilliseconds(400));
        stub.Calls.Should().BeEmpty("two chars is below MinChars=3");

        cut.Find(".tm-command-palette-input").Input("abc");
        Advance(cut, _clock, TimeSpan.FromMilliseconds(400));
        stub.Calls.Should().ContainSingle().Which.Query.Should().Be("abc");
    }

    [Fact]
    public void MinChars_Zero_Calls_Provider_For_The_Empty_Query()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Recent"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.MinChars, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        // Reopening resets the query to empty; with MinChars=0 the provider runs for "recent".
        cut.Find(".tm-command-palette-input").Input("x");
        Advance(cut, _clock, TimeSpan.FromMilliseconds(300));
        cut.Find(".tm-command-palette-input").Input(string.Empty);
        Advance(cut, _clock, TimeSpan.FromMilliseconds(300));

        stub.Calls.Select(c => c.Query).Should().Equal("x", string.Empty);
    }

    // ── Transient error + retry ──────────────────────────────────────────────

    [Fact]
    public void Transient_Result_Renders_Inline_Error_With_Retry()
    {
        var fail = true;
        var stub = new ProviderStub((_, _) => Task.FromResult(
            fail ? CommandPaletteSearchResult.Failed : Found(new CommandPaletteAction("ok", "Recovered"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");

        var loc = Services.GetRequiredService<ITmLocalizer>();
        cut.WaitForAssertion(() =>
        {
            var error = cut.Find(".tm-command-palette-error");
            error.GetAttribute("role").Should().Be("alert");
            error.TextContent.Should().Contain(loc["TmCommandPalette_SearchError"]);
            cut.Find(".tm-command-palette-retry").TextContent.Trim().Should().Be(loc["TmCommandPalette_Retry"]);
        }, TimeSpan.FromSeconds(2));

        fail = false;
        cut.Find(".tm-command-palette-retry").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-error").Should().BeEmpty("a successful retry clears the error");
            cut.FindAll(".tm-command-palette-item").Should().ContainSingle();
        }, TimeSpan.FromSeconds(2));
        stub.Calls.Should().HaveCount(2, "retry calls the provider again");
    }

    [Fact]
    public void Provider_Exception_Is_Treated_As_Transient()
    {
        var stub = new ProviderStub((_, _) => throw new InvalidOperationException("boom"));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");

        cut.WaitForAssertion(() => cut.Find(".tm-command-palette-error[role='alert']"),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void TransientErrorTemplate_Overrides_The_Default_Error()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(CommandPaletteSearchResult.Failed));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate)
            .Add(c => c.TransientErrorTemplate, retry => builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "class", "custom-retry");
                builder.AddAttribute(2, "onclick", EventCallback.Factory.Create(builder, retry));
                builder.AddContent(3, "custom retry");
                builder.CloseElement();
            }));

        cut.Find(".tm-command-palette-input").Input("x");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-error .tm-command-palette-retry").Should().BeEmpty();
            cut.Find(".tm-command-palette-error .custom-retry").Should().NotBeNull();
        }, TimeSpan.FromSeconds(2));
    }

    // ── Empty ────────────────────────────────────────────────────────────────

    [Fact]
    public void Empty_Result_Uses_EmptyTemplate_With_The_Query()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(CommandPaletteSearchResult.NoResults));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate)
            .Add(c => c.EmptyTemplate, query => builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "custom-empty");
                builder.AddContent(2, $"nothing for '{query}'");
                builder.CloseElement();
            }));

        cut.Find(".tm-command-palette-input").Input("zz");

        cut.WaitForAssertion(() =>
            cut.Find(".custom-empty").TextContent.Should().Be("nothing for 'zz'"), TimeSpan.FromSeconds(2));
        cut.FindAll(".tm-command-palette-empty").Should().BeEmpty();
    }

    [Fact]
    public void Empty_Result_Without_Template_Keeps_The_Localized_Default()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(CommandPaletteSearchResult.NoResults));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("zz");

        var loc = Services.GetRequiredService<ITmLocalizer>();
        cut.WaitForAssertion(() =>
            cut.Find(".tm-command-palette-empty").TextContent.Trim().Should().Be(loc["TmCommandPalette_NoResults"]),
            TimeSpan.FromSeconds(2));
    }

    // ── Loading ──────────────────────────────────────────────────────────────

    [Fact]
    public void Loading_Without_Results_Renders_Skeleton_Rows_Instead_Of_The_Empty_State()
    {
        var gate = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new ProviderStub((_, _) => gate.Task);
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-loading-row").Should().HaveCount(3);
            cut.Find(".tm-command-palette-loading").GetAttribute("aria-hidden").Should().Be("true");
            cut.FindAll(".tm-command-palette-empty").Should().BeEmpty("loading is not an empty result");
        }, TimeSpan.FromSeconds(2));

        gate.SetResult(Found(new CommandPaletteAction("done", "Done")));
        cut.WaitForAssertion(() => cut.FindAll(".tm-command-palette-item").Should().ContainSingle(),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void LoadingTemplate_Overrides_The_Default_Skeleton_Rows()
    {
        var gate = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new ProviderStub((_, _) => gate.Task);
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate)
            .Add(c => c.LoadingTemplate, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "custom-loading");
                builder.AddContent(2, "working…");
                builder.CloseElement();
            }));

        cut.Find(".tm-command-palette-input").Input("x");

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-command-palette-loading .custom-loading").Should().NotBeNull();
            cut.FindAll(".tm-command-palette-loading-row").Should().BeEmpty();
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void IsLoading_Param_Sets_AriaBusy_And_Keeps_Existing_Results_Visible()
    {
        using var cut = RenderPalette(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.Actions, new ICommandPaletteAction[] { new CommandPaletteAction("a1", "Local") }));

        cut.Find("ul.tm-command-palette-list").GetAttribute("aria-busy").Should().Be("true");
        cut.FindAll(".tm-command-palette-item").Should().ContainSingle("results stay visible while loading");
        cut.FindAll(".tm-command-palette-loading-row").Should().HaveCount(3,
            "the skeleton rows render below the kept results");
    }

    [Fact]
    public void IsLoading_Param_Without_Results_Renders_The_Skeleton()
    {
        using var cut = RenderPalette(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>()));

        cut.FindAll(".tm-command-palette-loading-row").Should().HaveCount(3);
        cut.FindAll(".tm-command-palette-empty").Should().BeEmpty();
    }

    [Fact]
    public void Not_Loading_List_Has_No_AriaBusy()
    {
        using var cut = RenderPalette(p => p
            .Add(c => c.Actions, new ICommandPaletteAction[] { new CommandPaletteAction("a1", "Local") }));

        cut.Find("ul.tm-command-palette-list").HasAttribute("aria-busy").Should().BeFalse(
            "default markup stays byte-identical: aria-busy appears only while loading");
    }

    // ── Combined results: local first (O3), grouped by Category (O6) ─────────

    [Fact]
    public void Local_Results_Render_Before_Async_Results()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(
            Found(new CommandPaletteAction("r1", "Remote", Category: "Entities"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, new ICommandPaletteAction[]
            {
                new CommandPaletteAction("l1", "Local command", Category: "Commands"),
            })
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("Local");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-title").Select(t => t.TextContent.Trim())
                .Should().Equal("Local command", "Remote");
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void GroupByCategory_Groups_Combined_Local_And_Async_Results()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(
            Found(new CommandPaletteAction("r1", "Remote", Category: "Entities"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.GroupByCategory, true)
            .Add(c => c.Actions, new ICommandPaletteAction[]
            {
                new CommandPaletteAction("l1", "Local command", Category: "Commands"),
            })
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("Local");

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-group-label").Select(l => l.TextContent.Trim())
                .Should().Equal("Commands", "Entities");
        }, TimeSpan.FromSeconds(2));
    }

    // ── Live region ──────────────────────────────────────────────────────────

    [Fact]
    public void Live_Region_Announces_Searching_Then_The_Result_Count()
    {
        var gate = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new ProviderStub((_, _) => gate.Task);
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");

        var loc = Services.GetRequiredService<ITmLocalizer>();
        cut.WaitForAssertion(() =>
        {
            var status = cut.Find("[role='status'].tm-command-palette-status");
            status.TextContent.Trim().Should().Be(loc["TmCommandPalette_Loading"]);
        }, TimeSpan.FromSeconds(2));

        gate.SetResult(Found(
            new CommandPaletteAction("r1", "One"),
            new CommandPaletteAction("r2", "Two")));
        cut.WaitForAssertion(() =>
        {
            cut.Find("[role='status'].tm-command-palette-status").TextContent.Trim()
                .Should().Be(loc["TmCommandPalette_ResultCount_Other"].Replace("{0}", "2"));
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Live_Region_Exists_And_Is_Empty_Right_After_Open()
    {
        using var cut = RenderPalette(p => p.Add(c => c.Actions, Array.Empty<ICommandPaletteAction>()));

        var status = cut.Find("[role='status'].tm-command-palette-status");
        status.GetAttribute("aria-live").Should().Be("polite");
        status.GetAttribute("aria-atomic").Should().Be("true");
        status.TextContent.Should().BeEmpty("the region is mounted empty so the first message is announced");
    }

    [Fact]
    public void Live_Region_Is_Emptied_On_Reopen()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "One"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));
        cut.Find(".tm-command-palette-input").Input("x");
        cut.WaitForAssertion(() =>
            cut.Find("[role='status'].tm-command-palette-status").TextContent.Trim().Should().NotBeEmpty(),
            TimeSpan.FromSeconds(2));

        cut.Render(p => p.Add(c => c.IsOpen, false));
        cut.Render(p => p.Add(c => c.IsOpen, true));

        cut.Find("[role='status'].tm-command-palette-status").TextContent.Should().BeEmpty();
    }

    [Fact]
    public void Retry_Hides_Alert_Shows_Skeleton_While_Pending_And_Alert_Returns_On_Second_Failure()
    {
        var calls = 0;
        TaskCompletionSource<CommandPaletteSearchResult>? pending = null;
        var stub = new ProviderStub((_, _) =>
        {
            if (++calls == 1)
            {
                return Task.FromResult(CommandPaletteSearchResult.Failed);
            }

            pending = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            return pending.Task;
        });
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");
        cut.WaitForAssertion(() => cut.Find(".tm-command-palette-error[role='alert']"), TimeSpan.FromSeconds(2));

        cut.Find(".tm-command-palette-retry").Click();

        cut.WaitForAssertion(() =>
        {
            cut.FindAll(".tm-command-palette-error").Should().BeEmpty("the alert leaves as soon as the retry starts");
            cut.FindAll(".tm-command-palette-loading").Should().ContainSingle("the skeleton shows while the retry runs");
        }, TimeSpan.FromSeconds(2));

        pending!.SetResult(CommandPaletteSearchResult.Failed);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-command-palette-error[role='alert']");
            cut.FindAll(".tm-command-palette-loading").Should().BeEmpty();
        }, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Retry_Moves_Focus_To_The_Search_Input()
    {
        var calls = 0;
        var stub = new ProviderStub((_, _) => Task.FromResult(
            ++calls == 1 ? CommandPaletteSearchResult.Failed : Found(new CommandPaletteAction("ok", "Recovered"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));
        cut.Find(".tm-command-palette-input").Input("x");
        cut.WaitForAssertion(() => cut.Find(".tm-command-palette-retry"), TimeSpan.FromSeconds(2));

        cut.Find(".tm-command-palette-retry").Click();

        cut.WaitForAssertion(() =>
            JSInterop.Invocations.Should().Contain(i => i.Identifier == "Blazor._internal.domWrapper.focus"),
            TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Whitespace_Only_Query_Does_Not_Call_Provider_With_MinChars()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Remote"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("   ");
        Advance(cut, _clock, TimeSpan.FromMilliseconds(300));

        stub.Calls.Should().BeEmpty();
    }

    [Fact]
    public void Debounce_Wait_Counts_As_Loading()
    {
        var stub = new ProviderStub((_, _) => Task.FromResult(Found(new CommandPaletteAction("r1", "Remote"))));
        using var cut = RenderPalette(p => p
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("a");

        cut.FindAll(".tm-command-palette-loading").Should().ContainSingle("the skeleton shows during the debounce wait");
    }

    // ── Highlight preservation ───────────────────────────────────────────────
    [Fact]
    public void Highlight_Follows_Action_Id_When_Async_Results_Arrive()
    {
        var responses = new Queue<CommandPaletteSearchResult>();
        responses.Enqueue(Found(new CommandPaletteAction("r1", "Remote one")));
        responses.Enqueue(Found(
            new CommandPaletteAction("l1-remote", "Local echo"),
            new CommandPaletteAction("r1", "Remote one")));
        var stub = new ProviderStub((_, _) => Task.FromResult(responses.Dequeue()));
        using var cut = RenderPalette(p => p
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, new ICommandPaletteAction[]
            {
                new CommandPaletteAction("l1", "Local command"),
            })
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("Local");
        cut.WaitForAssertion(() => cut.FindAll(".tm-command-palette-item").Should().HaveCount(2),
            TimeSpan.FromSeconds(2));

        // Highlight moves onto the async item (index 1: "Remote one").
        cut.Find(".tm-command-palette-input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "ArrowDown" });
        HighlightedTitle(cut).Should().Be("Remote one");

        // The next response contains the same action id at a different index — the highlight follows it.
        cut.Find(".tm-command-palette-input").Input("Loca");
        cut.WaitForAssertion(() => HighlightedTitle(cut).Should().Be("Remote one"),
            TimeSpan.FromSeconds(2));
    }

    private static string HighlightedTitle(IRenderedComponent<TmCommandPalette> cut)
        => cut.Find(".tm-command-palette-item-focused .tm-command-palette-title").TextContent.Trim();

    // ── Close / dispose cancels the in-flight request ────────────────────────

    [Fact]
    public async Task Enter_Executes_Highlighted_Action_And_Closes_The_Palette()
    {
        var isOpen = true;
        using var cut = RenderPalette(p => p
            .Add(c => c.Actions, new ICommandPaletteAction[] { new CommandPaletteAction("a1", "Run") })
            .Add(c => c.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });
        // Enter yields one browser task before closing (so the keypress cannot re-activate the opener).
        await Task.Run(async () => { for (var i = 0; i < 100 && isOpen; i++) await Task.Delay(10); });
        isOpen.Should().BeFalse("Enter executes the highlighted action and closes the palette");
    }

    [Fact]
    public void Closing_The_Palette_Cancels_The_InFlight_Request_And_Reopen_Resets()
    {
        var gate = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new ProviderStub((_, _) => gate.Task);
        var isOpen = true;
        using var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate)
            .Add(c => c.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-input").Input("x");
        stub.Calls.Should().ContainSingle();

        cut.Find(".tm-command-palette-close").Click();
        isOpen.Should().BeFalse();
        stub.Calls[0].Token.IsCancellationRequested.Should().BeTrue("closing cancels the running request");

        // The host re-renders with IsOpen=false (the subtree unmounts), then reopens.
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmCommandPalette.IsOpen)] = false }))).GetAwaiter().GetResult();
        cut.InvokeAsync(() => cut.Instance.SetParametersAsync(ParameterView.FromDictionary(
            new Dictionary<string, object?> { [nameof(TmCommandPalette.IsOpen)] = true }))).GetAwaiter().GetResult();
        cut.FindAll(".tm-command-palette-item").Should().BeEmpty("reopening starts with no stale results");
        cut.Find(".tm-command-palette-input").GetAttribute("value").Should().Be(string.Empty);
        stub.Calls.Should().HaveCount(1, "reopening must not replay the old request");
    }

    [Fact]
    public async Task Dispose_Cancels_The_InFlight_Request()
    {
        var gate = new TaskCompletionSource<CommandPaletteSearchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stub = new ProviderStub((_, _) => gate.Task);
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.DebounceMs, 0)
            .Add(c => c.Actions, Array.Empty<ICommandPaletteAction>())
            .Add(c => c.SearchProvider, stub.Delegate));

        cut.Find(".tm-command-palette-input").Input("x");
        stub.Calls.Should().ContainSingle();

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        stub.Calls[0].Token.IsCancellationRequested.Should().BeTrue("disposing cancels the running request");
    }
}
