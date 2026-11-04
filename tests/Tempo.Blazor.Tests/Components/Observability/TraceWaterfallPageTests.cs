using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Observability;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Components;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Observability;

/// <summary>
/// Plan ff043b2e phase 9: the <c>/trace-waterfall</c> page renders one generated agent run of roughly 150
/// spans — the treegrid, the host metric columns, the span detail and the live mode. These tests are the
/// "the page exists and is wired" guard the E2E lane then drives in a browser.
/// </summary>
[Collection(HeavyDemoPageRenders.Name)]
public class TraceWaterfallPageTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    /// <summary>The span count the generator produces; the page reports it in its summary line.</summary>
    private const int ExpectedSpanCount = 152;

    /// <summary>The instant the page's clock is pinned to, so no assertion depends on the real time.</summary>
    private static readonly DateTimeOffset AnchoredAt = new(2026, 3, 10, 9, 15, 30, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(AnchoredAt);

    public TraceWaterfallPageTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();
        Services.AddSingleton<TimeProvider>(_time);

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void Page_RendersTheGeneratedRunAsATreegrid()
    {
        var cut = Render<TraceWaterfallPage>();

        var section = cut.Find("[data-testid='trace-page-run']");

        section.QuerySelectorAll("[role='treegrid']").Length.Should().Be(1);
        section.QuerySelectorAll("[role='row'][data-span-id]").Length.Should().Be(ExpectedSpanCount,
            "the whole generated run renders under the 200-row virtualization threshold");
        section.QuerySelectorAll("[data-span-id='run'] .tm-trace-waterfall__toggle").Length.Should().Be(1);
        section.QuerySelectorAll("[data-span-id='round-04-db'] .tm-trace-waterfall__status--error").Length.Should().Be(1,
            "the fourth round's DB query is the simulated timeout");
    }

    [Fact]
    public void Page_ReportsTheSpanAndErrorCounts()
    {
        var cut = Render<TraceWaterfallPage>();

        var summary = cut.Find("[data-testid='trace-page-summary']").TextContent;

        summary.Should().Contain("152", "the generated run has 152 spans");
        summary.Should().Contain("2", "the timed-out query and the failed notification are the two errors");
        cut.Markup.Should().NotContain("TmTraceWaterfallDemo_", "every label resolves through the localizer");
    }

    [Fact]
    public void Page_ShowsTheHostMetricColumns()
    {
        var cut = Render<TraceWaterfallPage>();

        var headers = cut.Find("[data-testid='trace-page-waterfall']")
            .QuerySelectorAll("[role='columnheader']")
            .Select(header => header.TextContent.Trim())
            .ToList();

        headers.Should().Contain("Tokens in").And.Contain("Tokens out").And.Contain("Cost ($)",
            "the cost column carries the currency in its translated label");
        cut.Markup.Should().NotContain("\u00a4",
            "a 'C' format on the demo's NEUTRAL culture prints the generic currency sign (\u00a4), which is "
            + "what the visual sweep caught — the money is a plain number with the unit in the label");
    }

    [Fact]
    public void Page_OpensWithTheSelectedSpanDetail()
    {
        var cut = Render<TraceWaterfallPage>();

        var detail = cut.Find("[data-testid='trace-page-run'] [data-testid='trace-page-detail']");

        detail.GetAttribute("data-span-id").Should().Be("notify", "the page opens on the failed notification");
        cut.Find("[data-testid='trace-page-detail-status']").TextContent.Should().Be("Error");
        cut.Find("[data-testid='trace-page-detail-attributes']").TextContent.Should().Contain("http.status");
        cut.Find("[data-testid='trace-page-detail-attributes']").TextContent.Should().Contain("503");
    }

    [Fact]
    public void Page_SelectingAnotherRowFollowsTheSelection()
    {
        var cut = Render<TraceWaterfallPage>();

        // Clicking the row's name area selects it (the disclosure is a separate native button).
        cut.Find("[data-span-id='sub-agent'] .tm-trace-waterfall__label").Click();

        cut.Find("[data-testid='trace-page-detail']").GetAttribute("data-span-id").Should().Be("sub-agent");
        cut.Find("[data-testid='trace-page-detail-kind']").TextContent.Should().Be("Agent");
        cut.Find("[data-testid='trace-page-detail-metrics']").TextContent.Should().Contain("Tokens in");
        cut.Find("[data-testid='trace-page-detail-metrics']").TextContent.Should().Contain("Cost ($)",
            "the detail's money row is labelled the same way as the column");
    }

    [Fact]
    public void Page_LiveModeRepublishesTheRunWithRunningSpans()
    {
        var cut = Render<TraceWaterfallPage>();

        cut.Find("[data-testid='trace-page-live-state']").GetAttribute("data-live").Should().Be("false");
        cut.Find("[data-testid='trace-page-summary']").TextContent.Should().Contain("152");

        cut.Find("[data-testid='trace-page-live']").Click();

        cut.Find("[data-testid='trace-page-live-state']").GetAttribute("data-live").Should().Be("true");
        cut.Find("[data-testid='trace-page-live']").TextContent.Trim().Should().Be("Stop live trace");
        cut.Find("[data-testid='trace-page-summary']").TextContent.Should().Contain("151",
            "the live run has not written its final transcript row yet");
        cut.FindAll("[data-span-id='answer'] .tm-trace-waterfall__status--running").Should().ContainSingle(
            "the closing answer is still streaming while the run is live");
    }

    [Fact]
    public void Page_StoppingTheLiveRunPutsTheFinishedTraceBack()
    {
        var cut = Render<TraceWaterfallPage>();

        cut.Find("[data-testid='trace-page-live']").Click();
        cut.Find("[data-testid='trace-page-live']").Click();

        cut.Find("[data-testid='trace-page-live-state']").GetAttribute("data-live").Should().Be("false");
        cut.Find("[data-testid='trace-page-summary']").TextContent.Should().Contain("152");
    }

    [Fact]
    public void Page_AnchorsTheGeneratedRunOnTheInjectedClock()
    {
        var cut = Render<TraceWaterfallPage>();

        var root = Spans(cut).Single(span => span.Id == "run");

        root.EndedAt.Should().Be(AnchoredAt,
            "the generated run ends where the injected clock says, so the demo needs no real time");
        root.StartedAt.Should().Be(AnchoredAt.AddMilliseconds(-14_800),
            "the run still covers its documented 14.8 s");
    }

    [Fact]
    public void Page_HandsTheInjectedClockToTheWaterfall()
    {
        var cut = Render<TraceWaterfallPage>();

        cut.FindComponent<TmTraceWaterfall>().Instance.TimeProvider.Should().BeSameAs(_time,
            "the live ticker runs on the page's clock, not on the system one");
    }

    [Fact]
    public void Page_MeasuresTheRunningSpanAgainstTheInjectedClock()
    {
        var cut = Render<TraceWaterfallPage>();

        cut.Find("[data-testid='trace-page-live']").Click();

        // The live run leaves the closing answer without an end, so the detail measures it to now —
        // and "now" is the pinned clock, which is what makes this number stable instead of real time.
        var answer = Spans(cut).Single(span => span.Id == "answer");
        answer.EndedAt.Should().BeNull("the streaming answer has not finished");

        var elapsed = AnchoredAt - answer.StartedAt;
        var expected = elapsed.TotalMilliseconds < 1_000
            ? elapsed.TotalMilliseconds.ToString("N0", CultureInfo.CurrentCulture) + " ms"
            : elapsed.TotalSeconds.ToString("N2", CultureInfo.CurrentCulture) + " s";

        cut.Find("[data-testid='trace-page-detail-duration']").TextContent.Trim().Should().Be(
            expected,
            "the running duration is measured to the injected clock");
    }

    /// <summary>The span list the page handed to the waterfall — its own parameters, read back.</summary>
    private static IReadOnlyList<TraceSpan> Spans(IRenderedComponent<TraceWaterfallPage> cut)
        => cut.FindComponent<TmTraceWaterfall>().Instance.Spans;
}
