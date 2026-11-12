using System.Globalization;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.AI;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// The <c>/context-usage</c> demo page: the panel at full strength with the PAGE as the owner of the snapshot, the
/// same snapshot in the other two presentations, and the edge states. These facts walk what a reader actually does
/// — expand a section, remove an entry, remove a section, compact, push the context past its budget and reset —
/// so a page that only renders the component cannot pass.
/// </summary>
public class ContextUsagePageTests : DemoPageLocalizationTestBase
{
    private IRenderedComponent<ContextUsagePage> RenderPage() => Render<ContextUsagePage>();

    private static string Text(IRenderedComponent<ContextUsagePage> cut, string testId)
        => cut.Find($"[data-testid='{testId}']").TextContent.Trim();

    private static string ScopedText(IRenderedComponent<ContextUsagePage> cut, string root, string testId)
        => cut.Find($"[data-testid='{root}'] [data-testid='{testId}']").TextContent.Trim();

    private static IElement Row(IRenderedComponent<ContextUsagePage> cut, string root, string key)
        => cut.Find($"[data-testid='{root}'] tr.tm-context-usage__row[data-section-key='{key}']");

    [Fact]
    public void ThePageRendersItsThreeSectionsWithoutRawKeys()
    {
        var cut = RenderPage();

        cut.FindAll("section.demo-section").Should().HaveCount(3);
        foreach (var section in new[]
                 {
                     "context-usage-basic-section", "context-usage-variants-section", "context-usage-edge-section",
                 })
        {
            cut.Find($"[data-testid='{section}']").TextContent.Should().NotContain("[Tm");
        }
    }

    [Fact]
    public void TheReadingStartsAtFourteenThousandOfSixteenThousandTokens()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();

        Text(cut, "context-usage-readout").Should()
            .Be("Used 14,000 of 16,000 tokens (88 %) · sections: 6");
        cut.FindAll("[data-testid='context-usage-basic'] tr.tm-context-usage__row").Should().HaveCount(6);
        Text(cut, "context-usage-log-empty").Should().Be("Nothing has been removed yet.");
    }

    [Fact]
    public void ExpandingASectionShowsItsEntries()
    {
        var cut = RenderPage();

        Row(cut, "context-usage-basic", "files").QuerySelector(".tm-context-usage__toggle")!.Click();

        var detail = cut.Find("[data-testid='context-usage-basic'] tr.tm-context-usage__detail");
        detail.QuerySelectorAll(".tm-context-usage__item-label").Select(label => label.TextContent.Trim())
            .Should().Equal("Contract.pdf", "Report.pdf");
    }

    [Fact]
    public void RemovingAnEntry_IsAppliedByThePage_AndLogged()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();
        Row(cut, "context-usage-basic", "files").QuerySelector(".tm-context-usage__toggle")!.Click();

        cut.Find("[data-testid='context-usage-basic'] tr.tm-context-usage__detail [data-item-id='contract'] .tm-context-usage__item-remove")
            .Click();

        Text(cut, "context-usage-readout").Should()
            .Be("Used 10,000 of 16,000 tokens (63 %) · sections: 6",
                "the page drops the entry and its tokens from the snapshot");
        Row(cut, "context-usage-basic", "files").QuerySelector(".tm-context-usage__tokens")!.TextContent.Trim()
            .Should().Be("1,000");
        Text(cut, "context-usage-log-entry").Should().Be("Removed Contract.pdf from Files.");
    }

    [Fact]
    public void RemovingASection_IsAppliedByThePage_AndLogged()
    {
        // The grouped-value assertion needs the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();

        Row(cut, "context-usage-basic", "tools").QuerySelector(".tm-context-usage__remove")!.Click();

        cut.FindAll("[data-testid='context-usage-basic'] tr.tm-context-usage__row").Should().HaveCount(5,
            "the page drops the whole Tools section");
        Text(cut, "context-usage-readout").Should().Be("Used 13,200 of 16,000 tokens (83 %) · sections: 5");
        Text(cut, "context-usage-log-entry").Should().Be("Removed the Tools section.");
    }

    [Fact]
    public void CompactingTheOptionalSections_ShrinksTheContext_AndDisablesTheAction()
    {
        // The grouped-value assertion needs the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();

        cut.Find("[data-testid='context-usage-basic'] [data-testid='context-usage-compact']").Click();

        Text(cut, "context-usage-readout").Should().Be("Used 4,560 of 16,000 tokens (29 %) · sections: 6");
        Text(cut, "context-usage-log-entry").Should().Be("Compacted the optional sections.");
        cut.Find("[data-testid='context-usage-basic'] [data-testid='context-usage-compact']")
            .HasAttribute("disabled").Should().BeTrue("nothing optional is left to compact");
    }

    [Fact]
    public void TheNextTurnPushesTheContextOverBudget_AndThePanelSaysSo()
    {
        // The grouped-value assertions need the suite's invariant baseline, not the ambient culture.
        using var _ = CultureScope.Use(CultureInfo.InvariantCulture);
        var cut = RenderPage();

        cut.Find("[data-testid='context-usage-turn']").Click();

        cut.WaitForAssertion(() =>
        {
            Text(cut, "context-usage-readout").Should().Be("Used 16,500 of 16,000 tokens (103 %) · sections: 6");
            cut.Find("[data-testid='context-usage-basic']").GetAttribute("data-over-budget").Should().Be("true");
            ScopedText(cut, "context-usage-basic", "context-usage-over-budget").Should()
                .Contain("Over budget by 500");
        });

        cut.Find("[data-testid='context-usage-reset']").Click();
        cut.WaitForAssertion(() => Text(cut, "context-usage-readout")
            .Should().Be("Used 14,000 of 16,000 tokens (88 %) · sections: 6"));
        cut.FindAll("[data-testid='context-usage-log-empty']").Should().HaveCount(1);
    }

    [Fact]
    public void TheThreePresentationsShareTheSnapshotAndItsActions()
    {
        var cut = RenderPage();

        cut.Find("[data-testid='context-usage-grid'] .tm-context-usage__grid").Should().NotBeNull();
        cut.Find("[data-testid='context-usage-treemap'] .tm-context-usage__treemap .tm-chart").Should().NotBeNull();

        cut.Find("[data-testid='context-usage-reset']").Click();
        cut.FindAll("[data-testid='context-usage-grid'] tr.tm-context-usage__row").Should().HaveCount(6);
        cut.FindAll("[data-testid='context-usage-treemap'] tr.tm-context-usage__row").Should().HaveCount(6);
    }

    [Fact]
    public void TheEdgeStatesSayWhatTheyAre()
    {
        var cut = RenderPage();

        ScopedText(cut, "context-usage-edge-empty", "context-usage-empty").Should()
            .Be("No context usage is reported.");
        cut.Find("[data-testid='context-usage-edge-over']").GetAttribute("data-over-budget").Should().Be("true");
        ScopedText(cut, "context-usage-edge-over", "context-usage-over-budget").Should().Contain("600",
            "8 000 of an 8 000 budget plus 600 more is named exactly");
        cut.Find("[data-testid='context-usage-edge-unknown'] [data-testid='context-usage-meter']")
            .GetAttribute("aria-valuemax").Should().Be("0");
        cut.Find("[data-testid='context-usage-edge-unknown']").GetAttribute("data-over-budget").Should().BeNull();
        cut.FindAll("[data-testid='context-usage-edge-no-cache'] [data-testid='context-usage-cache']").Should().BeEmpty();
        cut.Find("[data-testid='context-usage-edge-no-cache'] [data-testid='context-usage-meter']")
            .GetAttribute("aria-describedby").Should().BeNull("no cache badge and no over-budget line to describe");
    }
}
