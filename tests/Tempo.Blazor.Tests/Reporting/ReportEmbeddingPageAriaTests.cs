using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Demo.SharedUI.Services;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Reporting;

/// <summary>
/// bUnit coverage for the /report-embedding demo page's source-mode tabs (fáze 4h, plan
/// b75365fb): <c>aria-selected</c> is an ARIA STATE attribute and must carry the strings
/// "true"/"false" — a bool comparison renders the invalid empty value on the active tab and
/// drops the attribute on the inactive one.
/// </summary>
public class ReportEmbeddingPageAriaTests : LocalizationTestBase
{
    public ReportEmbeddingPageAriaTests()
    {
        Services.AddSingleton(new DemoReportEmbeddingSourceFactory());
        Services.AddHttpClient();
    }

    [Fact]
    public void ModeTabs_RenderAriaSelectedAsStateStrings_InBothStates()
    {
        var cut = Render<ReportEmbeddingPage>();

        cut.Find("[data-testid='embedding-mode-embedded']").GetAttribute("role")
            .Should().Be("tab", "the mode buttons are tabs of the source-mode tablist");
        cut.Find("[data-testid='embedding-mode-remote']").GetAttribute("role")
            .Should().Be("tab");

        cut.Find("[data-testid='embedding-mode-embedded']").GetAttribute("aria-selected")
            .Should().Be("true", "the embedded source is the default mode");
        cut.Find("[data-testid='embedding-mode-remote']").GetAttribute("aria-selected")
            .Should().Be("false");

        cut.Find("[data-testid='embedding-mode-remote']").Click();

        cut.Find("[data-testid='embedding-mode-remote']").GetAttribute("aria-selected")
            .Should().Be("true", "the clicked Remote tab is active now");
        cut.Find("[data-testid='embedding-mode-embedded']").GetAttribute("aria-selected")
            .Should().Be("false");
    }
}
