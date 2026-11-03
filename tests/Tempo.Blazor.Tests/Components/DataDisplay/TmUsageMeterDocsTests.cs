using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Plan ff043b2e phase 7: what shipped is documented and styled. The doc, the changelog and the agent
/// guide describe the behaviour of <c>TmUsageMeter</c> (the meter value contract, the states, the
/// over-limit reading, the category table, the single threshold live region, the edge cases), the
/// stylesheet is imported into the entry point AND the committed bundle (or a host reading the bundle
/// would never see the component), and every class the markup or the code-behind emits has a rule.
/// </summary>
public sealed class TmUsageMeterDocsTests
{
    private static string Read(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ThemeCss.RepositoryRoot().FullName }.Concat(path).ToArray()));

    [Fact]
    public void Doc_DocumentsTheApi()
    {
        var doc = Read("docs", "usage-meter.md");

        foreach (var term in new[]
        {
            "TmUsageMeter", "UsageCategory", "Used", "Limit", "Unit", "ValueFormat", "ValueFormatter",
            "Culture", "WarningThreshold", "CriticalThreshold", "ResetsAt", "DailyUsage", "Categories",
            "Compact", "Title", "HeadingLevel", "AriaLabel", "NearLimitContent", "Class", "DataTestId",
            "TestIdPrefix",
            "role=\"meter\"", "aria-valuemin", "aria-valuemax", "aria-valuenow", "aria-valuetext",
            "largest remainder", "Other", "tm-usage-meter__fill", "tm-usage-meter__table",
        })
        {
            doc.Should().Contain(term, $"docs/usage-meter.md must document {term}");
        }
    }

    [Fact]
    public void Doc_DocumentsTheAccessibilityContract()
    {
        var doc = Read("docs", "usage-meter.md");

        doc.Should().Contain("polite, atomic");
        doc.Should().Contain("threshold transition only");
        doc.Should().Contain("not interactive");
        doc.Should().Contain("forced-color-adjust: none");
        doc.Should().Contain("prefers-reduced-motion: reduce");
        doc.Should().Contain("icon, a text");
        doc.Should().Contain("(pointer: coarse)");
    }

    [Fact]
    public void Doc_DocumentsTheEdgeCases()
    {
        var doc = Read("docs", "usage-meter.md");

        doc.Should().Contain("UNKNOWN");
        doc.Should().Contain("Counted as zero");
        doc.Should().Contain("Falls back to `N0`");
        doc.Should().Contain("Skipped");
    }

    [Fact]
    public void Changelog_RecordsTheComponentUnderTheCurrentHeading()
    {
        var changelog = Read("CHANGELOG.md");

        var heading = changelog.IndexOf("## 2.10.0", StringComparison.Ordinal);
        var entry = changelog.IndexOf("**New `TmUsageMeter`**", StringComparison.Ordinal);

        heading.Should().BeGreaterThanOrEqualTo(0);
        entry.Should().BeGreaterThan(heading, "the entry belongs under the unreleased 2.10.0 heading");
        changelog.Should().Contain("docs/usage-meter.md");
    }

    [Fact]
    public void AgentGuide_ListsTheComponentInItsCategory()
    {
        var guide = Read("AGENTS.md");

        guide.Should().Contain("`TmUsageMeter`");
        guide.Should().Contain("docs/usage-meter.md");
    }

    [Fact]
    public void Stylesheet_IsImportedAndBundled()
    {
        var entry = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.css");
        var bundle = Read("src", "Tempo.Blazor", "wwwroot", "css", "tempo-blazor.bundled.css");

        entry.Should().Contain("@import \"components/_usage-meter.css\";");
        bundle.Should().Contain(".tm-usage-meter__track", "a host reading the bundle must get the component");
        bundle.Should().Contain(".tm-usage-meter__table");
    }

    [Fact]
    public void Stylesheet_DefinesEveryClassTheMarkupEmits()
    {
        var css = ThemeCss.ComponentCss("_usage-meter.css");

        foreach (var name in new[]
        {
            "tm-usage-meter", "tm-usage-meter--compact", "tm-usage-meter--warning",
            "tm-usage-meter--critical", "tm-usage-meter__title", "tm-usage-meter__track",
            "tm-usage-meter__fill", "tm-usage-meter__segment", "tm-usage-meter__segment--tone-1",
            "tm-usage-meter__segment--tone-6", "tm-usage-meter__segment--remainder",
            "tm-usage-meter__readout", "tm-usage-meter__amount", "tm-usage-meter__percent",
            "tm-usage-meter__state", "tm-usage-meter__state-text", "tm-usage-meter__over",
            "tm-usage-meter__over-icon", "tm-usage-meter__reset", "tm-usage-meter__reset-icon",
            "tm-usage-meter__daily", "tm-usage-meter__sparkline", "tm-usage-meter__cta",
            "tm-usage-meter__table", "tm-usage-meter__row", "tm-usage-meter__row--other",
            "tm-usage-meter__label", "tm-usage-meter__label-inner", "tm-usage-meter__swatch",
            "tm-usage-meter__swatch--tone-1", "tm-usage-meter__swatch--tone-6",
            "tm-usage-meter__swatch--remainder", "tm-usage-meter__value", "tm-usage-meter__share",
        })
        {
            css.Should().Contain("." + name, $"the stylesheet must style .{name}");
        }
    }

    [Fact]
    public void Stylesheet_HasNoRawPaletteLiteralAndNoDarkBlock()
    {
        var css = ThemeCss.ComponentCss("_usage-meter.css");

        css.Should().NotContain("[data-theme", "the dark theme re-points the tokens, it is not a component block");
        css.Should().NotContain(".tm-dark");
        css.Should().NotMatch(@"#[0-9a-fA-F]{3,8}\b");
        css.Should().NotContain("rgba(");
        css.Should().NotContain("rgb(");
    }

    [Fact]
    public void ReducedMotion_KeepsTheEnumeratedFill()
    {
        var animations = Read("src", "Tempo.Blazor", "wwwroot", "css", "animations.css");

        animations.Should().Contain(".tm-usage-meter__fill,",
            "the fill's width transition is an animated subject, so the reduced-motion block clamps it");
    }

    [Fact]
    public void JsonDocumentation_CoversTheComponentAndItsModel()
    {
        var component = Read("JsonDocumentation", "Components", "TmUsageMeter.json");
        var model = Read("JsonDocumentation", "Components", "Components", "UsageCategory.json");

        component.Should().Contain("\"itemName\": \"TmUsageMeter\"");
        component.Should().Contain("\"NearLimitContent\"");
        model.Should().Contain("\"itemName\": \"UsageCategory\"");
        model.Should().Contain("\"Key\"");
    }
}
