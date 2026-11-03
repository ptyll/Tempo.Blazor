using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 6: the demo page renders the trace waterfall for real — a generated agent run
/// with LLM, tool, HTTP and DB spans, one failed request, a metric column row and a live toggle. The
/// E2E lane drives the same section in a browser.
/// </summary>
public class DataDisplayPageTraceSectionTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageTraceSectionTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    [Fact]
    public void TraceSection_RendersTheGeneratedRunAsATreegrid()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='trace-waterfall-section']");

        section.QuerySelectorAll("[role='treegrid']").Length.Should().Be(1);
        section.QuerySelectorAll("[role='row'][data-span-id]").Length.Should().Be(8,
            "the demo run has a root, two LLM calls, two tool calls, two HTTP leaves and a DB leaf, all expanded");
        section.QuerySelectorAll("[data-span-id='run'] .tm-trace-waterfall__toggle").Length.Should().Be(1);
        section.QuerySelectorAll("[data-span-id='search-http'] .tm-trace-waterfall__slowest").Length.Should().Be(1,
            "the 360 ms GET /search is the longest LEAF of the demo run - its parent tool call only wraps it");
    }

    [Fact]
    public void TraceSection_ShowsTheHostMetricColumns()
    {
        var cut = Render<DataDisplayPage>();

        var headers = cut.Find("[data-testid='trace-waterfall-section']")
            .QuerySelectorAll("[role='columnheader']")
            .Select(header => header.TextContent.Trim())
            .ToList();

        headers.Should().Contain("Tokens in").And.Contain("Tokens out").And.Contain("Cost");
    }

    [Fact]
    public void TraceSection_OpensWithTheSelectedSpanDetail()
    {
        var cut = Render<DataDisplayPage>();

        var panel = cut.Find("[data-testid='trace-waterfall-section'] [data-testid='trace-detail']");
        panel.TextContent.Should().Contain("compose answer", "the host template renders the span name itself");

        var detail = panel.QuerySelector("[data-testid='trace-detail-content']");
        detail.Should().NotBeNull();
        detail!.TextContent.Should().Contain("Llm", "the template also renders the kind and the status");

        cut.FindAll("[role='row'][data-span-id='answer']")[0].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void TraceSection_TheLiveToggle_ChangesItsLabel()
    {
        var cut = Render<DataDisplayPage>();

        var toggle = () => cut.Find("[data-testid='trace-live-toggle']");
        toggle().TextContent.Trim().Should().Be("Start live trace");

        toggle().Click();

        toggle().TextContent.Trim().Should().Be("Stop live trace");

        toggle().Click();
        toggle().TextContent.Trim().Should().Be("Start live trace");
    }

    [Fact]
    public void TraceSection_UsesLocalizedKeysOnly()
    {
        var cut = Render<DataDisplayPage>();

        var section = cut.Find("[data-testid='trace-waterfall-section']");

        section.TextContent.Should().NotContain("[TmDataDisplayDemo",
            "a missing demo key renders its bracket marker, so this catches an unlocalized label");
    }
}
