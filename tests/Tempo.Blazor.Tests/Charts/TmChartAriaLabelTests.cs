using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Charts;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// Plan 7ba95ed6 phase 8 — the additive <c>TmChart.AriaLabel</c> reachable name of the plot.
/// <para>
/// The chart's SVG is a <c>role="img"</c> with a text alternative built from the localizer and the
/// chart TYPE (<c>"{0} chart"</c>), which tells a reader WHAT kind of chart it is but never WHICH
/// one. A page that shows several charts used to wrap each in a <c>&lt;figure aria-labelledby&gt;</c>
/// — a wrapper that only exists to name the plot (decision
/// FZE9-CHART-NAMED-BY-A-FIGURE-NOT-A-NEW-PARAMETER, revised here). <c>AriaLabel</c> puts that name
/// on the SVG itself, so the host drops the wrapper and the component stays the single source of the
/// accessible name.
/// </para>
/// <para>
/// The default is <c>null</c>, which keeps today's localized "Line chart" text alternative byte for
/// byte — the parameter is purely additive. An empty or whitespace-only value is treated as absent:
/// an <c>aria-label=""</c> would REMOVE the name entirely, which is worse than the generic one.
/// </para>
/// </summary>
public class TmChartAriaLabelTests : LocalizationTestBase
{
    private static ChartData LineData => new()
    {
        Labels = ["Jan", "Feb", "Mar"],
        Datasets =
        [
            new ChartDataset { Label = "Revenue", Values = [10, 20, 30], Color = "#3b82f6" }
        ]
    };

    private string Localized(string key) => Services.GetRequiredService<ITmLocalizer>()[key];

    [Fact]
    public void AriaLabel_Null_KeepsTheLocalizedTypeTextAlternative()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, LineData));

        cut.Find("svg").GetAttribute("aria-label").Should()
            .Be(string.Format(Localized("TmChart_AriaLabel"), ChartType.Line),
                "the default keeps today's text alternative — the parameter is additive");
    }

    [Fact]
    public void AriaLabel_Given_NamesThePlotWithTheHostValue()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, LineData)
            .Add(x => x.AriaLabel, "Revenue against the comparison window"));

        cut.Find("svg").GetAttribute("aria-label").Should()
            .Be("Revenue against the comparison window");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AriaLabel_Blank_FallsBackToTheTypeTextAlternative(string blank)
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, LineData)
            .Add(x => x.AriaLabel, blank));

        cut.Find("svg").GetAttribute("aria-label").Should()
            .Be(string.Format(Localized("TmChart_AriaLabel"), ChartType.Line),
                "an empty aria-label would erase the accessible name, so a blank value is treated as absent");
    }

    [Fact]
    public void AriaLabel_RendersTheSameRoleImgContract()
    {
        var cut = Render<TmChart>(p => p
            .Add(x => x.Type, ChartType.Line)
            .Add(x => x.Data, LineData)
            .Add(x => x.AriaLabel, "Revenue against the comparison window"));

        var svg = cut.Find("svg");
        svg.GetAttribute("role").Should().Be("img");
        svg.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
    }
}
