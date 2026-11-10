using System.Globalization;
using System.Text.RegularExpressions;
using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Demo.SharedUI.Components;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan 7ba95ed6 phase 9: the demo pages cross-link each other instead of duplicating sections.
/// <c>/data-display</c> and <c>/forms</c> stay the COMPONENT-LEVEL REFERENCES (one component per
/// section, every knob exercised) while <c>/analytics-kit</c> and <c>/trace-waterfall</c> are the
/// ASSEMBLED SCENARIOS (the same components driving one reporting screen and one generated agent
/// run). A reference page names the scenario its sections are assembled into, and a scenario page
/// names the references its components are documented on — one localized label, the demo nav titles
/// as anchor text, and relative hrefs so the demo base href applies.
/// </summary>
[Collection(HeavyDemoPageRenders.Name)]
public class DemoCrossLinksPhase9Tests : DemoPageLocalizationTestBase
{
    private const string AnimatedNumberModule = "./_content/Tempo.Blazor/js/tm-animated-number.js";

    private const string ReferenceLabelKey = "TmDemo_ComponentReference";

    private const string ScenarioLabelKey = "TmDemo_FullScenario";

    public DemoCrossLinksPhase9Tests()
    {
        Services.AddSingleton<Tempo.Blazor.Services.ToastService>();
        Services.AddSingleton<TimeProvider>(
            new FakeTimeProvider(new DateTimeOffset(2026, 3, 10, 9, 15, 30, TimeSpan.Zero)));

        var module = JSInterop.SetupModule(AnimatedNumberModule);
        module.Setup<bool>("prefersReducedMotion", _ => true).SetResult(true);
        module.SetupVoid("animate", _ => true).SetVoidResult();
        module.SetupVoid("cancel", _ => true).SetVoidResult();
    }

    // ── The shared component ────────────────────────────────────────────────────

    [Fact]
    public void CrossLinks_RenderOneLocalizedLabelAndOneAnchorPerEntry()
    {
        var cut = Render<DemoCrossLinks>(parameters => parameters
            .Add(component => component.LabelKey, ReferenceLabelKey)
            .Add(component => component.Links, DemoCrossLinkSets.ComponentReference)
            .Add(component => component.DataTestId, "test-cross-links"));

        var row = cut.Find("[data-testid='test-cross-links']");

        row.QuerySelector(".demo-cross-links__label")!.TextContent.Trim().Should().Be("Component reference",
            "the label is one localized string, not a hardcoded English literal");
        row.QuerySelectorAll("a").Length.Should().Be(3, "the reference set has three pages");

        row.QuerySelectorAll("a")
            .Select(anchor => (Href: anchor.GetAttribute("href"), Text: anchor.TextContent.Trim()))
            .Should().Equal(
                ("data-display", "Data Display"),
                ("charts", "Charts"),
                ("forms", "Form Inputs"));
    }

    [Fact]
    public void CrossLinks_KeepEveryHrefRelativeSoTheDemoBaseHrefApplies()
    {
        // A root-absolute href ignores <base href> and breaks when the demo is hosted under a sub-path
        // (the same rule the /app-shell back-link follows).
        DemoCrossLinkSets.All.SelectMany(set => set.Value).Select(link => link.Href)
            .Should().OnlyContain(href => !href.StartsWith('/') && !href.Contains("://", StringComparison.Ordinal));

        DemoCrossLinkSets.All.SelectMany(set => set.Value).Select(link => link.LabelKey)
            .Should().OnlyContain(key => key.Contains("Demo", StringComparison.Ordinal),
                "demo-owned keys carry Demo in their name (the demo resource tables' own rule)");
    }

    [Fact]
    public void CrossLinks_AnchorTextIsTheDemoNavTitleOfTheTargetPage()
    {
        // "nav titles as anchor text": a reader who found the page in the navigation meets the same
        // wording on the cross-link, in every language (the nav itself is not localized).
        var nav = File.ReadAllText(Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Demo.SharedUI", "Layout", "NavMenu.razor"));

        foreach (var link in DemoCrossLinkSets.All.SelectMany(set => set.Value).Distinct())
        {
            var title = EnglishDemoTable()[link.LabelKey];
            nav.Should().Contain($"new NavItem(\"{link.Href}\", \"{title}\"",
                $"{link.LabelKey} must spell the nav title of /{link.Href} so the link is recognisable");
        }
    }

    // ── The scenario pages link back to their references ────────────────────────

    [Fact]
    public void AnalyticsKitPage_LinksToItsThreeComponentReferences()
    {
        var cut = Render<AnalyticsKitPage>();

        var row = cut.Find("[data-testid='analytics-kit-component-reference']");

        row.QuerySelector(".demo-cross-links__label")!.TextContent.Trim().Should().Be("Component reference");
        row.QuerySelectorAll("a").Select(anchor => anchor.GetAttribute("href"))
            .Should().Equal(new[] { "data-display", "charts", "forms" },
                "the reporting screen is assembled from the data-display, charts and form-input references");
        row.QuerySelectorAll("a").Select(anchor => anchor.TextContent.Trim())
            .Should().Equal(new[] { "Data Display", "Charts", "Form Inputs" },
                "the anchor text is the target page's nav title");
        // The row exposes its label KEY as a data attribute (a test/E2E hook that survives translation),
        // so the guard is the localizer's unresolved-key marker, not the key itself.
        cut.Markup.Should().NotContain("[TmDemo_",
            "every label and anchor text resolves through the demo localizer, never as an unresolved key");
    }

    [Fact]
    public void TraceWaterfallPage_LinksToTheDataDisplayReference()
    {
        var cut = Render<TraceWaterfallPage>();

        var row = cut.Find("[data-testid='trace-page-component-reference']");

        row.QuerySelector(".demo-cross-links__label")!.TextContent.Trim().Should().Be("Component reference");
        row.QuerySelectorAll("a").Select(anchor => anchor.GetAttribute("href"))
            .Should().Equal(new[] { "data-display" },
                "the waterfall is documented component-by-component on /data-display");
        row.QuerySelectorAll("a").Select(anchor => anchor.TextContent.Trim())
            .Should().Equal(new[] { "Data Display" },
                "the anchor text is the target page's nav title");
    }

    // ── The reference pages link to the scenarios that assemble them ────────────

    [Theory]
    [InlineData("data-display-kpi-scenario")]
    [InlineData("data-display-trend-scenario")]
    [InlineData("data-display-breakdown-scenario")]
    [InlineData("data-display-facet-scenario")]
    [InlineData("data-display-usage-scenario")]
    [InlineData("data-display-segmented-scenario")]
    public void DataDisplayPage_SectionLinksToTheAnalyticsKitScenario(string testId)
    {
        var cut = Render<DataDisplayPage>();

        var row = cut.Find($"[data-testid='{testId}']");

        row.QuerySelector(".demo-cross-links__label")!.TextContent.Trim().Should().Be("Full scenario");
        row.QuerySelector("a")!.GetAttribute("href").Should().Be("analytics-kit");
        row.QuerySelector("a")!.TextContent.Trim().Should().Be("Analytics Kit");
    }

    [Fact]
    public void DataDisplayPage_TraceSectionLinksToTheTraceWaterfallScenario()
    {
        var cut = Render<DataDisplayPage>();

        var row = cut.Find("[data-testid='data-display-trace-scenario']");

        row.QuerySelector(".demo-cross-links__label")!.TextContent.Trim().Should().Be("Full scenario");
        row.QuerySelector("a")!.GetAttribute("href").Should().Be("trace-waterfall");
        row.QuerySelector("a")!.TextContent.Trim().Should().Be("Trace Waterfall");
    }

    [Fact]
    public void FormsPage_MatchHighlightSectionLinksToTheAnalyticsKitScenario()
    {
        // /forms is the component-level reference for the input vocabulary and is not rendered by any
        // bUnit suite (it hosts a FluentValidation form), so the wiring is pinned at the source: the
        // section composes the SAME shared row with the same catalog set.
        var markup = ReadPage("FormsPage.razor");

        var matchHighlight = Regex.Match(markup,
            @"<section[^>]*>\s*<div[^>]*>\s*<h2[^>]*>@Loc\[""TmFormsDemo_MatchHighlight""\].*?</section>",
            RegexOptions.Singleline);

        matchHighlight.Success.Should().BeTrue("the match-highlight section still exists on /forms");
        matchHighlight.Value.Should().Contain("<DemoCrossLinks",
            "the reference section names the scenario that assembles it");
        matchHighlight.Value.Should().Contain("LabelKey=\"" + ScenarioLabelKey + "\"");
        matchHighlight.Value.Should().Contain("Links=\"DemoCrossLinkSets.AnalyticsKitScenario\"");
        matchHighlight.Value.Should().Contain("DataTestId=\"forms-match-highlight-scenario\"");
    }

    [Fact]
    public void EveryCrossLinkLabelIsTranslatedInAllFourDemoTables()
    {
        // A missing translation would render the English text on a Czech page (the demo localizer falls
        // back per table, not per key, so the completeness guard alone is not enough here).
        foreach (var language in new[] { "en", "cs", "sk", "fr" })
        {
            var table = DemoTable(language);

            foreach (var key in new[] { ReferenceLabelKey, ScenarioLabelKey }
                         .Concat(DemoCrossLinkSets.All.SelectMany(set => set.Value).Select(link => link.LabelKey)))
            {
                table.Should().ContainKey(key, $"{key} must exist in DemoResources.{language}.json");
                table[key].Should().NotBeNullOrWhiteSpace(key);
            }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string ReadPage(string fileName) => File.ReadAllText(Path.Combine(
        ThemeCss.RepositoryRoot().FullName,
        "src", "Tempo.Blazor.Demo.SharedUI", "Pages", fileName));

    private static Dictionary<string, string> DemoTable(string language)
    {
        var name = language == "en" ? "DemoResources.json" : $"DemoResources.{language}.json";
        var path = Path.Combine(
            ThemeCss.RepositoryRoot().FullName, "src", "Tempo.Blazor.Demo.SharedUI", "Resources", name);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static Dictionary<string, string> EnglishDemoTable() => DemoTable("en");
}
