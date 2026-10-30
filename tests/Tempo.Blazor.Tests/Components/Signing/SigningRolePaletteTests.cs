using FluentAssertions;
using Tempo.Blazor.Components.Signing;

namespace Tempo.Blazor.Tests.Components.Signing;

public class SigningRolePaletteTests
{
    [Theory]
    [InlineData(0, "#2563eb")]
    [InlineData(1, "#16a34a")]
    [InlineData(5, "#a16207")]
    [InlineData(6, "#2563eb")]
    [InlineData(-1, "#a16207")]
    public void ColorForOrder_ReturnsDefaultColourWrappingAround(int order, string expected)
    {
        SigningRolePalette.ColorForOrder(order).Should().Be(expected);
    }

    [Theory]
    [InlineData("#2563eb", "var(--tm-signing-role-1)")]
    [InlineData("#16a34a", "var(--tm-signing-role-2)")]
    [InlineData("#A16207", "var(--tm-signing-role-6)")]
    public void ToCssColor_DefaultPaletteColour_MapsToItsToken(string color, string expected)
    {
        SigningRolePalette.ToCssColor(color).Should().Be(expected);
    }

    [Theory]
    [InlineData("#ca8a04")]
    [InlineData("#CA8A04")]
    public void ToCssColor_LegacyRole6Colour_MapsToTheRole6Token(string color)
    {
        // Role 6 shipped as #ca8a04 before phase 1 darkened it to #a16207; stored templates still
        // carry the old value and must follow theme/host overrides of --tm-signing-role-6 too.
        SigningRolePalette.ToCssColor(color).Should().Be("var(--tm-signing-role-6)");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#123456")]
    [InlineData("var(--tm-signing-role-6)")]
    [InlineData("rgb(202, 138, 4)")]
    public void ToCssColor_NonDefaultColour_PassesThroughVerbatim(string? color)
    {
        SigningRolePalette.ToCssColor(color).Should().Be(color);
    }
}
