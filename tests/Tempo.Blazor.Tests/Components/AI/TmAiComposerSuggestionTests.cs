using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.AI.Services;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Mention half of the composer's suggestion list (plan 7f21f26b, Fáze 6): a <c>@</c> starts a debounced,
/// cancellable query against the host's <see cref="IAiMentionProvider"/>, the list is the same combobox as the
/// slash commands, and a failure closes it with one localized announcement instead of breaking the editor.
/// </summary>
public sealed class TmAiComposerSuggestionTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(200);

    private const string InteropModulePath = TmAiComposerTests.InteropModulePath;

    private FakeTimeProvider UseFakeClock()
    {
        var clock = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(clock);
        return clock;
    }

    private void SetupInterop()
    {
        var module = JSInterop.SetupModule(InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("resize", _ => true).SetVoidResult();
        module.SetupVoid("placeSuggestions", _ => true).SetVoidResult();
        module.Setup<bool>("speechSupported", _ => true).SetResult(false);
    }

    /// <summary>A provider that records every call (query + token) and answers from a scripted delegate.</summary>
    private sealed class FakeMentionProvider : IAiMentionProvider
    {
        private readonly Func<string, CancellationToken, Task<IReadOnlyList<AiMentionItem>>> _handler;

        public FakeMentionProvider(Func<string, CancellationToken, Task<IReadOnlyList<AiMentionItem>>>? handler = null)
            => _handler = handler ?? ((_, _) => Task.FromResult<IReadOnlyList<AiMentionItem>>(
                [Mention("u1", "Alice"), Mention("u2", "Bob")]));

        public List<(string Query, CancellationToken Token)> Calls { get; } = [];

        public Task<IReadOnlyList<AiMentionItem>> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls.Add((query, cancellationToken));
            return _handler(query, cancellationToken);
        }
    }

    internal static AiMentionItem Mention(string id, string label)
        => new() { Id = id, Label = label, Kind = "user" };

    private IRenderedComponent<TmAiComposer> RenderComposer(
        IAiMentionProvider provider,
        Action<ComponentParameterCollectionBuilder<TmAiComposer>>? configure = null)
        => Render<TmAiComposer>(parameters =>
        {
            parameters.Add(component => component.MentionProvider, provider);
            configure?.Invoke(parameters);
        });

    [Fact]
    public void TypeAt_OpensTheMentionListboxAfterTheDebounce()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@al");
        cut.FindAll("[role='listbox']").Should().BeEmpty("the query is debounced — nothing is asked yet");
        provider.Calls.Should().BeEmpty();

        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(2));
        provider.Calls.Should().ContainSingle();
        provider.Calls[0].Query.Should().Be("al");
        cut.Find("[role='listbox']").GetAttribute("aria-label").Should().Be("Mentions");
        cut.Find("textarea").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void AnEmptyQuery_AfterJustTheTrigger_IsStillAsked()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.WaitForAssertion(() => provider.Calls.Should().ContainSingle());
        provider.Calls[0].Query.Should().BeEmpty("the default mention suggestions are asked for with an empty query");
    }

    [Fact]
    public void TypingMore_CancelsThePreviousQueryAndAsksOnlyTheLatest()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@a");
        provider.Calls.Should().BeEmpty();
        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.WaitForAssertion(() => provider.Calls.Should().ContainSingle());
        provider.Calls[0].Query.Should().Be("al", "a superseded query is dropped, never sent");
    }

    [Fact]
    public async Task AQueryInFlight_IsCancelledWhenTheTriggerGoesAway()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var pending = new TaskCompletionSource<IReadOnlyList<AiMentionItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeMentionProvider((_, token) =>
        {
            token.Register(() => pending.TrySetCanceled(token));
            return pending.Task;
        });
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));
        cut.WaitForAssertion(() => provider.Calls.Should().ContainSingle());
        var token = provider.Calls[0].Token;

        cut.Find("textarea").Input("plain text");
        cut.FindAll("[role='listbox']").Should().BeEmpty();
        token.IsCancellationRequested.Should().BeTrue("closing the list cancels the query it no longer needs");

        await cut.InvokeAsync(() => { });
    }

    [Fact]
    public void PickingAMention_ReplacesTheTriggerAndRecordsItOnTheSubmit()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(provider, parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("Ask @al");
        cut.InvokeAsync(() => clock.Advance(Debounce));
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(2));

        cut.Find("textarea").KeyDown("Enter");
        cut.Find("textarea").GetAttribute("value").Should().Be("Ask @Alice ");
        cut.FindAll("[role='listbox']").Should().BeEmpty();

        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Mentions.Should().ContainSingle();
        submits[0].Mentions[0].Id.Should().Be("u1");
        submits[0].Text.Should().Be("Ask @Alice", "the text is trimmed on submit");
    }

    [Fact]
    public void PickingTheSameMentionTwice_RecordsItOnce()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var submits = new List<AiComposerSubmit>();
        var cut = RenderComposer(provider, parameters => parameters
            .Add(component => component.OnSubmit, (AiComposerSubmit submit) => submits.Add(submit)));

        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(2));
        cut.Find("textarea").KeyDown("Enter");

        cut.Find("textarea").Input(cut.Find("textarea").GetAttribute("value") + "@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));
        cut.WaitForAssertion(() => cut.FindAll("[role='option']").Should().HaveCount(2));
        cut.Find("textarea").KeyDown("Enter");

        cut.Find("[data-testid='ai-composer-send']").Click();

        submits.Should().ContainSingle();
        submits[0].Mentions.Should().ContainSingle("one person mentioned twice is one mention");
    }

    [Fact]
    public void AnEmptyMentionResult_ClosesTheListbox()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider((_, _) =>
            Task.FromResult<IReadOnlyList<AiMentionItem>>(Array.Empty<AiMentionItem>()));
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@zzz");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.WaitForAssertion(() => provider.Calls.Should().ContainSingle());
        cut.FindAll("[role='listbox']").Should().BeEmpty();
        cut.Find("textarea").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void AProviderFailure_ClosesTheListboxAndAnnouncesItOnce()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider((_, _) => throw new InvalidOperationException("search down"));
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.WaitForAssertion(() => cut.Find("[data-testid='ai-composer-announcement']").TextContent
            .Trim().Should().Be("Suggestions could not be loaded."));
        cut.FindAll("[role='listbox']").Should().BeEmpty();
        cut.Find("[data-testid='ai-composer-announcement']").GetAttribute("role").Should().Be("status");
    }

    [Fact]
    public void AnAtSignInsideAWord_DoesNotOpenTheMentions()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var cut = RenderComposer(provider);

        cut.Find("textarea").Input("mail me at bob@example");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.FindAll("[role='listbox']").Should().BeEmpty("the trigger must start the token");
        provider.Calls.Should().BeEmpty();
    }

    [Fact]
    public void ATemplateWithoutAMentionProvider_NeverOpensTheMentions()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var cut = Render<TmAiComposer>();

        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        cut.FindAll("[role='listbox']").Should().BeEmpty();
    }

    [Fact]
    public void ADisabledComposer_DoesNotAskTheProvider()
    {
        SetupInterop();
        var clock = UseFakeClock();
        var provider = new FakeMentionProvider();
        var cut = RenderComposer(provider, parameters => parameters.Add(component => component.Disabled, true));

        cut.Find("textarea").Input("@al");
        cut.InvokeAsync(() => clock.Advance(Debounce));

        provider.Calls.Should().BeEmpty();
        cut.FindAll("[role='listbox']").Should().BeEmpty();
    }
}
