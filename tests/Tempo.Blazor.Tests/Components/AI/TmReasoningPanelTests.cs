using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// bUnit contract of <see cref="TmReasoningPanel"/> (plan 7f21f26b, Fáze 4): the collapsed summary
/// ("Přemýšlel {0}"), the live timer that runs while the model is still thinking, the disclosure with
/// the steps and the inspected sources, the hardened source links and the single live region that
/// announces the finished reasoning.
/// </summary>
public sealed class TmReasoningPanelTests : LocalizationTestBase
{
    private static readonly DateTimeOffset Start = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static AiReasoningPart Reasoning(double? durationMs = null, params string[] steps)
    {
        var part = new AiReasoningPart(steps.Length == 0 ? ["Checked the policy."] : steps)
        {
            DurationMs = durationMs,
        };
        return part;
    }

    private static AiSource Source(string id, string? url = null) => new()
    {
        Id = id,
        Title = $"Source {id}",
        Url = url ?? $"https://example.test/{id}",
        Domain = "example.test",
        Snippet = $"Snippet of {id}.",
    };

    private FakeTimeProvider UseFakeClock()
    {
        var time = new FakeTimeProvider(Start);
        Services.AddSingleton<TimeProvider>(time);
        return time;
    }

    private static Task DrainAsync<TComponent>(IRenderedComponent<TComponent> cut)
        where TComponent : IComponent
        => cut.InvokeAsync(() => { });

    private static string Summary(IRenderedComponent<TmReasoningPanel> cut)
        => cut.Find(".tm-ai-reasoning__summary").TextContent.Trim();

    [Fact]
    public void CollapsedByDefault_ShowsWhatTheThinkingCost()
    {
        UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning(12_000, "Checked the policy.")));

        var root = cut.Find(".tm-ai-reasoning");
        root.GetAttribute("data-testid").Should().Be("ai-reasoning");

        var toggle = cut.Find(".tm-ai-reasoning__toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        Summary(cut).Should().Be("Thought for 12.0 s");
        cut.FindAll(".tm-ai-reasoning__steps").Should().BeEmpty("the panel starts collapsed");
    }

    [Fact]
    public void Toggle_ExpandsTheSteps()
    {
        UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning(4_000, "Checked the policy.", "Compared the numbers.")));

        var toggle = cut.Find(".tm-ai-reasoning__toggle");
        toggle.GetAttribute("aria-controls").Should().BeNull(
            "the body is not rendered while the panel is collapsed, so the id would dangle");

        cut.Find(".tm-ai-reasoning__toggle").Click();

        cut.Find(".tm-ai-reasoning__toggle").GetAttribute("aria-expanded").Should().Be("true");
        var bodyId = cut.Find(".tm-ai-reasoning__toggle").GetAttribute("aria-controls");
        bodyId.Should().NotBeNullOrWhiteSpace("an open disclosure names the region it owns");
        var body = cut.Find($"[id='{bodyId}']");
        body.ClassList.Should().Contain("tm-ai-reasoning__body");
        body.QuerySelectorAll(".tm-ai-reasoning__step")
            .Select(step => step.TextContent.Trim())
            .Should().Equal("Checked the policy.", "Compared the numbers.");
    }

    [Fact]
    public void DefaultExpanded_RendersTheBodyImmediately()
    {
        UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning(4_000))
            .Add(panel => panel.DefaultExpanded, true));

        cut.FindAll(".tm-ai-reasoning__step").Should().ContainSingle();
    }

    [Fact]
    public void Sources_AreListed_AndTheirLinksAreHardened()
    {
        UseFakeClock();
        var reasoning = Reasoning(4_000);
        reasoning.AddSources([Source("ok"), Source("bad", "javascript:alert(1)")]);

        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, reasoning)
            .Add(panel => panel.DefaultExpanded, true));

        var items = cut.FindAll(".tm-ai-reasoning__source");
        items.Should().HaveCount(2);

        var safe = items[0].QuerySelector("a");
        safe.Should().NotBeNull();
        safe!.GetAttribute("href").Should().Be("https://example.test/ok");
        safe.GetAttribute("rel").Should().Contain("noopener").And.Contain("noreferrer");
        safe.GetAttribute("target").Should().Be("_blank");

        items[1].QuerySelector("a").Should().BeNull(
            "a refused scheme renders as plain text instead of a dead '#' link");
        items[1].TextContent.Should().Contain("Source bad");
    }

    [Fact]
    public void ShowSourcesFalse_OmitsTheSourceList()
    {
        UseFakeClock();
        var reasoning = Reasoning(4_000);
        reasoning.AddSources([Source("ok")]);

        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, reasoning)
            .Add(panel => panel.DefaultExpanded, true)
            .Add(panel => panel.ShowSources, false));

        cut.FindAll(".tm-ai-reasoning__sources").Should().BeEmpty();
    }

    [Fact]
    public async Task WhileRunning_TheTimerIsLive()
    {
        var time = UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning())
            .Add(panel => panel.IsRunning, true)
            .Add(panel => panel.DefaultExpanded, true));

        Summary(cut).Should().Be("Thinking for 0.0 s");
        cut.Find(".tm-ai-reasoning__summary").ClassList.Should().Contain("tm-ai-shimmer");
        cut.Find(".tm-ai-reasoning__step:last-child").ClassList.Should().Contain("tm-ai-streaming",
            "the trailing step is the one still being written");

        time.Advance(TimeSpan.FromSeconds(3));
        await DrainAsync(cut);

        Summary(cut).Should().Be("Thinking for 3.0 s");
    }

    [Fact]
    public async Task Completion_StopsTheTimer_AndAnnouncesTheResult()
    {
        var time = UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning())
            .Add(panel => panel.IsRunning, true)
            .Add(panel => panel.DefaultExpanded, true));

        cut.FindAll("[role='status']").Count.Should().Be(1, "the panel owns exactly one live region");
        cut.Find("[role='status']").TextContent.Trim().Should().BeEmpty("nothing has changed yet");

        time.Advance(TimeSpan.FromSeconds(2));
        await DrainAsync(cut);

        // The stream applier closes the SAME reasoning part; a new instance would be a different thought.
        var part = cut.Instance.Reasoning!;
        await cut.InvokeAsync(() => part.DurationMs = 2_400);
        cut.Render(parameters => parameters
            .Add(panel => panel.Reasoning, part)
            .Add(panel => panel.IsRunning, false));

        Summary(cut).Should().Be("Thought for 2.4 s");
        cut.Find(".tm-ai-reasoning__step:last-child").ClassList.Should().NotContain("tm-ai-streaming");
        cut.Find("[role='status']").TextContent.Trim().Should().Be("Thought for 2.4 s");

        time.Advance(TimeSpan.FromSeconds(10));
        await DrainAsync(cut);

        Summary(cut).Should().Be("Thought for 2.4 s", "a finished reasoning keeps its recorded duration");
    }

    [Fact]
    public void WithoutADuration_AndNotRunning_ThePanelSaysItIsThinking()
    {
        UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning()));

        Summary(cut).Should().Be("Thinking");
    }

    [Fact]
    public void NullReasoning_RendersNothing()
    {
        UseFakeClock();
        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, null));

        cut.FindAll(".tm-ai-reasoning").Should().BeEmpty();
    }

    [Fact]
    public void TheSummary_IsLocalized()
    {
        UseCzechLocalization();
        UseFakeClock();

        var cut = Render<TmReasoningPanel>(parameters => parameters
            .Add(panel => panel.Reasoning, Reasoning(12_000)));

        Summary(cut).Should().Be("Přemýšlel 12,0 s");
    }
}
