using Bunit;
using FluentAssertions;
using Tempo.Blazor.Demo.SharedUI.Pages.TestSuiteManager;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.TestSuiteManager;

/// <summary>
/// bUnit coverage for the /test-suite-manager demo page's mobile tree accordion toggle
/// (fáze 4h, plan b75365fb): <c>aria-expanded</c> is an ARIA STATE attribute and must carry the
/// strings "true"/"false" — a bool field renders the invalid empty value when open and drops the
/// attribute entirely when closed.
/// </summary>
public class TestSuiteManagerPageAriaTests : LocalizationTestBase
{
    [Fact]
    public void TreeToggle_RendersAriaExpandedAsStateStrings_InBothStates()
    {
        var cut = Render<TestSuiteManagerPage>();

        var toggle = cut.Find(".tsm-tree-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true", "the tree starts open");

        toggle.Click();

        cut.Find(".tsm-tree-toggle").GetAttribute("aria-expanded").Should().Be("false",
            "the closed accordion still carries the attribute — with the string \"false\"");

        cut.Find(".tsm-tree-toggle").Click();

        cut.Find(".tsm-tree-toggle").GetAttribute("aria-expanded").Should().Be("true");
    }
}
