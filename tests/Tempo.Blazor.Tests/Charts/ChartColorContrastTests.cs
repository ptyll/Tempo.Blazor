using FluentAssertions;
using Tempo.Blazor.Components.Charts;

namespace Tempo.Blazor.Tests.Charts;

/// <summary>
/// The colour reader behind the funnel's ink choice. A host's <c>BackgroundColors</c> is not always a
/// hex — <c>rgb(100% 100% 100%)</c>, <c>rgba(0, 0, 0, .5)</c> and <c>hsl(60, 100%, 90%)</c> all name a
/// real fill, and a reader that only understood <c>#rrggbb</c> would silently keep the light ink on a
/// white band. The documented fallback for everything else is the pre-2.11.0 assumption (a dark fill).
/// </summary>
public class ChartColorContrastTests
{
    [Theory]
    [InlineData("#ffffff", 255, 255, 255)]
    [InlineData("#fff", 255, 255, 255)]
    [InlineData("#ffff", 255, 255, 255)]
    [InlineData("#000000", 0, 0, 0)]
    [InlineData("#000", 0, 0, 0)]
    [InlineData("#3b82f6", 59, 130, 246)]
    [InlineData("#3b82f680", 59, 130, 246)]
    [InlineData("#ffffffff", 255, 255, 255)]
    public void Hex_Shapes_ResolveToTheirChannels(string value, double r, double g, double b)
    {
        ChartColorContrast.TryParseColour(value, out var actualR, out var actualG, out var actualB)
            .Should().BeTrue();

        (actualR, actualG, actualB).Should().Be((r, g, b));
    }

    [Theory]
    [InlineData("rgb(255, 255, 255)", 255, 255, 255)]
    [InlineData("rgb(255 255 255)", 255, 255, 255)]
    [InlineData("rgba(0, 0, 0, 0.5)", 0, 0, 0)]
    [InlineData("rgb(255 255 255 / 50%)", 255, 255, 255)]
    [InlineData("rgb(100% 100% 100%)", 255, 255, 255)]
    [InlineData("rgb(100% 0% 0%)", 255, 0, 0)]
    [InlineData("rgb(50% 50% 50%)", 127.5, 127.5, 127.5)]
    public void Rgb_Shapes_ParseNumericAndPercentageChannels(string value, double r, double g, double b)
    {
        // A percentage channel is not a 0–255 number: 100 % is 255, so the ×2.55 step is what makes a
        // percentage fill answer the same luminance verdict a hex does.
        ChartColorContrast.TryParseColour(value, out var actualR, out var actualG, out var actualB)
            .Should().BeTrue();

        (actualR, actualG, actualB).Should().Be((r, g, b));
    }

    [Theory]
    [InlineData("hsl(0, 100%, 50%)", 255, 0, 0)]
    [InlineData("hsl(0 0% 100%)", 255, 255, 255)]
    [InlineData("hsl(120, 100%, 25%)", 0, 128, 0)]
    [InlineData("hsl(240, 100%, 50%)", 0, 0, 255)]
    [InlineData("hsl(0deg, 100%, 50%)", 255, 0, 0)]
    public void Hsl_Shapes_AreConvertedToRgb(string value, double r, double g, double b)
    {
        ChartColorContrast.TryParseColour(value, out var actualR, out var actualG, out var actualB)
            .Should().BeTrue();

        (actualR, actualG, actualB).Should().Be((r, g, b));
    }

    [Theory]
    [InlineData("var(--tm-color-primary)")]
    [InlineData("red")]
    [InlineData("color-mix(in srgb, #fff, #000)")]
    [InlineData("#12")]
    [InlineData("#gggggg")]
    [InlineData("")]
    [InlineData("   ")]
    public void UnresolvableFills_ReportFalse(string value)
    {
        ChartColorContrast.TryParseColour(value, out _, out _, out _).Should().BeFalse();
        ChartColorContrast.IsLight(value).Should().BeFalse(
            "the documented fallback keeps the pre-2.11.0 assumption of a DARK brand fill, so the light "
            + "ink is used instead of guessing at the host's colour");
    }

    [Theory]
    [InlineData("#ffffff", true)]
    [InlineData("#000000", false)]
    [InlineData("rgb(100% 100% 100%)", true)]
    [InlineData("hsl(60, 100%, 90%)", true)]
    [InlineData("hsl(220, 80%, 20%)", false)]
    [InlineData("#ef4444", true)]   // luminance 0.229 — above the 0.179 crossover, the dark ink wins
    [InlineData("#3b82f6", true)]   // luminance 0.257 — the blue is light too, hence dark ink on most fills
    [InlineData("#1e3a8a", false)]  // luminance 0.051 — the deep blue keeps the light ink
    public void IsLight_FollowsTheWcagCrossover(string value, bool light)
    {
        ChartColorContrast.IsLight(value).Should().Be(light);
        ChartColorContrast.RelativeLuminance(255, 255, 255).Should().BeApproximately(1, 0.0001);
        ChartColorContrast.RelativeLuminance(0, 0, 0).Should().BeApproximately(0, 0.0001);
    }
}
