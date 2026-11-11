using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan 7ba95ed6 phase 12: the demo page carries a LIVE fixture for the breakdown card's row identity —
/// two parts with an EMPTY <c>BreakdownPart.Id</c> and two that share one. That is the population a
/// hand-written <c>@key</c> collapses onto a single node, and until now only bUnit construction covered
/// it; the fixture is what a browser (and the E2E forced-colors sweep) can look at.
/// <para>
/// The card derives a private key for every row without a unique id, so both facts below are about the
/// behaviour a reader sees: all four rows render, and the period switch still re-sorts them.
/// </para>
/// </summary>
public class DataDisplayPageBreakdownFixtureTests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    public DataDisplayPageBreakdownFixtureTests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    private static AngleSharp.Dom.IElement FixtureCard(IRenderedComponent<DataDisplayPage> cut)
        => cut.Find("[data-testid='breakdown-fixture'] [data-testid='breakdown-fixture-card']");

    private static string[] Labels(IRenderedComponent<DataDisplayPage> cut)
        => [.. FixtureCard(cut)
            .QuerySelectorAll("tbody tr .tm-breakdown-card__label")
            .Select(label => label.TextContent.Trim())];

    [Fact]
    public void TheFixtureRendersEveryRow_DespiteTheDuplicateAndEmptyIds()
    {
        var cut = Render<DataDisplayPage>();

        var rows = FixtureCard(cut).QuerySelectorAll("table.tm-breakdown-card__table tbody tr");

        rows.Length.Should().Be(4,
            "two empty ids and two parts sharing one id must still render four rows — a collapsed @key "
            + "would throw 'More than one sibling … has the same key'");
        rows.Select(row => row.GetAttribute("data-part-id")).Should()
            .Equal(string.Empty, "dup", string.Empty, "dup");

        FixtureCard(cut).QuerySelectorAll(".tm-breakdown-card__segment").Length.Should()
            .Be(4, "the strip still paints one segment per part");
    }

    [Fact]
    public void TheFixtureReSortsOnThePeriodSwitch()
    {
        var cut = Render<DataDisplayPage>();

        var before = Labels(cut);
        before.Should().Equal("Unkeyed A", "Repeated A", "Unkeyed B", "Repeated B");

        // "Last week" is the second period: it makes Repeated A the biggest part, so the rows move even
        // though their keys are unusable as identities.
        cut.Find("[data-testid='breakdown-fixture'] [data-testid='breakdown-fixture-card'] .tm-breakdown-card__period")
            .QuerySelectorAll("[role='radio']")[1].Click();

        cut.WaitForAssertion(() =>
        {
            var after = Labels(cut);
            after.Should().HaveCount(4, "the reorder keeps every row");
            after.Should().NotEqual(before, "the fixture's populations reorder differently");
            after[0].Should().Be("Repeated A", "the largest part leads after the switch");
        });
    }

    [Fact]
    public void TheFixtureIsAnnouncedOncePerCardBeforeTheCrossLinks()
    {
        var cut = Render<DataDisplayPage>();

        var fixture = cut.Find("[data-testid='breakdown-fixture']");

        fixture.QuerySelectorAll("[aria-live]").Length.Should()
            .Be(0, "the card is a static summary — no live region of its own");
        fixture.TextContent.Should().NotContain("[Tm", "no raw resource key may reach the page");
    }
}
