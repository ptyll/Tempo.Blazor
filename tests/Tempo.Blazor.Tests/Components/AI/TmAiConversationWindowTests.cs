using Bunit;
using FluentAssertions;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of the transcript's RENDER WINDOW: a conversation with hundreds of turns keeps the DOM at
/// the size of the window, follows the tail while the reader is at the bottom, and lets the reader pull the
/// earlier turns in one step at a time.
/// </summary>
/// <remarks>
/// The window is a plain slice of <see cref="TmAiConversation.Messages"/> — deliberately not
/// <c>&lt;Virtualize&gt;</c>, which would unmount rows the reader is looking at and break the message
/// anchors and focus. The default is <see langword="null"/>: no window, every turn rendered, byte-identical
/// to the pre-window behaviour.
/// </remarks>
public sealed class TmAiConversationWindowTests : LocalizationTestBase
{
    private const string LoadOlder = "[data-testid='ai-conversation-load-older']";

    private static List<AiMessage> Conversation(int count) => TmAiConversationTests.Conversation(count);

    private static string[] RenderedIds(IRenderedComponent<TmAiConversation> cut)
        => cut.FindAll(".tm-ai-conversation__message")
            .Select(row => row.GetAttribute("data-message-id")!)
            .ToArray();

    private IRenderedComponent<TmAiConversation> RenderWindowed(
        List<AiMessage> messages,
        int? size,
        int step = 10)
        => Render<TmAiConversation>(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, size)
            .Add(component => component.RenderWindowStep, step));

    // ── Default: no window ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void WithoutRenderWindowSize_EveryTurnIsRendered()
    {
        var cut = RenderWindowed(Conversation(120), size: null);

        RenderedIds(cut).Should().HaveCount(120);
        cut.FindAll(LoadOlder).Should().BeEmpty();
    }

    // ── Tail window ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FirstRender_ShowsTheNewestTurns_AndOffersToLoadTheOlderOnes()
    {
        var cut = RenderWindowed(Conversation(100), size: 20);

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("m-80");
        ids[^1].Should().Be("m-99");
        cut.FindAll(LoadOlder).Should().ContainSingle().Which.TextContent.Should().Be("Load older messages");
    }

    [Fact]
    public void LoadOlder_GrowsTheWindowByTheStep()
    {
        var cut = RenderWindowed(Conversation(100), size: 20, step: 10);

        cut.Find(LoadOlder).Click();

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(30);
        ids[0].Should().Be("m-70");
    }

    [Fact]
    public void LoadOlder_AtTheTopOfTheList_RemovesTheButton()
    {
        var cut = RenderWindowed(Conversation(15), size: 10, step: 50);

        cut.Find(LoadOlder).Click();

        RenderedIds(cut).Should().HaveCount(15);
        cut.FindAll(LoadOlder).Should().BeEmpty("everything is rendered, so there is nothing older to load");
    }

    /// <summary>A parameter-only re-render (the host's own state changed) must not snap the window back.</summary>
    [Fact]
    public void ExpandedWindow_SurvivesAParameterOnlyRerender()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        RenderedIds(cut)[0].Should().Be("m-70", "the reader's expansion is a choice, not a derived default");
    }

    // ── Appends ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AppendedTurn_SlidesTheWindowWithTheTail()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20);

        messages.Add(TmAiConversationTests.Message("m-100"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20));

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("m-81");
        ids[^1].Should().Be("m-100");
    }

    /// <summary>
    /// A reader who pulled the older turns in keeps them when a new turn arrives — the window grows at the
    /// bottom instead of unmounting the rows above the viewport.
    /// </summary>
    [Fact]
    public void AppendedTurn_WhileExpanded_KeepsTheLoadedTurns()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        messages.Add(TmAiConversationTests.Message("m-100"));
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        var ids = RenderedIds(cut);
        ids[0].Should().Be("m-70");
        ids[^1].Should().Be("m-100");
        ids.Should().HaveCount(31);
    }

    /// <summary>Replacing the list (loading another conversation) resets the window to the new tail.</summary>
    [Fact]
    public void ReplacedList_ResetsTheWindowToTheTail()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20, step: 10);
        cut.Find(LoadOlder).Click();

        var replacement = Enumerable.Range(0, 60)
            .Select(index => TmAiConversationTests.Message($"other-{index}"))
            .ToList();

        cut.Render(parameters => parameters
            .Add(component => component.Messages, replacement)
            .Add(component => component.RenderWindowSize, 20)
            .Add(component => component.RenderWindowStep, 10));

        var ids = RenderedIds(cut);
        ids.Should().HaveCount(20);
        ids[0].Should().Be("other-40");
        ids[^1].Should().Be("other-59");
    }

    [Fact]
    public void ShrunkList_ClampsTheWindow()
    {
        var messages = Conversation(100);
        var cut = RenderWindowed(messages, size: 20);
        cut.Find(LoadOlder).Click();

        messages.RemoveRange(50, 50);
        cut.Render(parameters => parameters
            .Add(component => component.Messages, messages)
            .Add(component => component.RenderWindowSize, 20));

        RenderedIds(cut).Should().HaveCount(20);
        RenderedIds(cut)[^1].Should().Be("m-49");
    }

    /// <summary>A window smaller than the minimum is a footgun, not a feature.</summary>
    [Fact]
    public void RenderWindowSize_IsClampedToTheMinimum()
    {
        var cut = RenderWindowed(Conversation(100), size: 3);

        RenderedIds(cut).Should().HaveCount(10, "the window floor keeps a usable transcript");
    }

    /// <summary>The step is a real step: a zero or negative value would make the button a no-op.</summary>
    [Fact]
    public void RenderWindowStep_IsClampedToTheMinimum()
    {
        var cut = RenderWindowed(Conversation(100), size: 20, step: 0);

        cut.Find(LoadOlder).Click();

        RenderedIds(cut).Should().HaveCount(30);
    }

    /// <summary>Loading older turns must not be mistaken for new content: the viewport stays where it is.</summary>
    [Fact]
    public void LoadOlder_DoesNotScrollToTheBottom()
    {
        var module = JSInterop.SetupModule(TmAiConversationTests.InteropModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.Setup<bool>("isNearBottom", _ => true).SetResult(true);
        module.SetupVoid("scrollToBottom", _ => true).SetVoidResult();
        var cut = RenderWindowed(Conversation(100), size: 20, step: 10);
        var before = TmAiConversationTests.Count(module, "scrollToBottom");

        cut.Find(LoadOlder).Click();

        TmAiConversationTests.Count(module, "scrollToBottom").Should().Be(before);
    }
}
