using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 1: the documentation, the changelog and the agent guide describe the behaviour
/// that shipped — the single accessible value and the `Announce` opt-in, reduced motion and
/// retargeting, the culture-aware delta with a tone that is separate from the direction, the additive
/// stat-card parts, the whole-card target with its href allow-list, and the wireframe surface.
/// </summary>
public class TmDataDisplayPhase1DocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void AnimatedNumberDoc_DocumentsTheApi()
    {
        var doc = Read("docs", "animated-number.md");

        foreach (var term in new[]
        {
            "TmAnimatedNumber", "Value", "Format", "Culture", "Prefix", "Suffix", "Duration",
            "StartValue", "Animate", "Announce", "tm-animated-number.js", "prefersReducedMotion",
            "animate", "cancel", "requestAnimationFrame", "N0", "Intl.NumberFormat",
        })
        {
            doc.Should().Contain(term, $"docs/animated-number.md must document {term}");
        }
    }

    [Fact]
    public void AnimatedNumberDoc_DocumentsTheAccessibilityContract()
    {
        var doc = Read("docs", "animated-number.md");

        doc.Should().Contain("One node carries the value",
            "the reader must be told the value is exposed once");
        doc.Should().Contain("polite, atomic");
        doc.Should().Contain("live region");
        doc.Should().Contain("tabular-nums");
        doc.Should().Contain("prefers-reduced-motion: reduce");
        doc.Should().Contain("retargets");
    }

    [Fact]
    public void StatCardDoc_DocumentsTheKpiSurfaceAndTheAdditivityContract()
    {
        var doc = Read("docs", "stat-card.md");

        foreach (var term in new[]
        {
            "TmStatCard", "Delta", "DeltaFormat", "TrendDirection", "PositiveIsGood", "ComparisonLabel",
            "Icon", "Sparkline", "IsLoading", "NumericValue", "AnimateValue", "ValueFormat", "ValueSuffix", "Culture",
            "Href", "OnClick", "PercentPositivePattern", "TmStatCard_TrendUp", "javascript:",
            "byte-identical", "forced-colors", "Highlight", "--tm-touch-target", "stat-card",
        })
        {
            doc.Should().Contain(term, $"docs/stat-card.md must document {term}");
        }
    }

    [Fact]
    public void Changelog_RecordsBothSurfaceChanges()
    {
        var changelog = Read("CHANGELOG.md");

        changelog.Should().Contain("New `TmAnimatedNumber`");
        changelog.Should().Contain("opt-in KPI surface");
        changelog.Should().Contain("docs/animated-number.md");
        changelog.Should().Contain("docs/stat-card.md");
    }

    [Fact]
    public void AgentGuide_ListsBothComponents()
    {
        var guide = Read("AGENTS.md");

        guide.Should().Contain("TmAnimatedNumber");
        guide.Should().Contain("docs/animated-number.md");
        guide.Should().Contain("docs/stat-card.md");
    }
}
