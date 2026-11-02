using FluentAssertions;
using System.Globalization;
using Tempo.Blazor.Components.DataDisplay;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>
/// Unit tests of the pure descriptor that translates a .NET numeric format string into the
/// <c>Intl.NumberFormat</c> options the <c>tm-animated-number.js</c> module needs for its
/// intermediate frames (plan ff043b2e, phase 1). No DOM, no interop: the mapping is a pure
/// function so every branch is testable.
/// </summary>
public class AnimatedNumberFormatTests
{
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    [Theory]
    [InlineData("N0", 0, 0)]
    [InlineData("N1", 1, 1)]
    [InlineData("N3", 3, 3)]
    [InlineData("n2", 2, 2)]
    [InlineData("F1", 1, 1)]
    [InlineData("f4", 4, 4)]
    public void StandardNumericFormats_CarryTheirFractionDigits(string format, int min, int max)
    {
        var descriptor = AnimatedNumberFormat.Describe(format, English);

        descriptor.MinimumFractionDigits.Should().Be(min);
        descriptor.MaximumFractionDigits.Should().Be(max);
        descriptor.Style.Should().Be("decimal");
        descriptor.Locale.Should().Be("en-US");
    }

    [Fact]
    public void StandardFormatWithoutDigits_KeepsTheDotNetDefaultOfTwo()
    {
        var descriptor = AnimatedNumberFormat.Describe("N", English);

        descriptor.MaximumFractionDigits.Should().Be(2, ".NET's N without a precision means two decimals");
    }

    /// <summary>
    /// <c>P</c> multiplies by 100 in .NET and in <c>Intl.NumberFormat</c> alike, so the descriptor
    /// only has to pass the style through — the two stay in step.
    /// </summary>
    [Theory]
    [InlineData("P0", 0)]
    [InlineData("P1", 1)]
    public void PercentFormats_UseThePercentStyle(string format, int digits)
    {
        var descriptor = AnimatedNumberFormat.Describe(format, English);

        descriptor.Style.Should().Be("percent");
        descriptor.MaximumFractionDigits.Should().Be(digits);
    }

    [Fact]
    public void CurrencyFormats_TakeTheCulturesCurrencyDecimals()
    {
        AnimatedNumberFormat.Describe("C2", Czech).MaximumFractionDigits.Should().Be(2);
        AnimatedNumberFormat.Describe("C0", Czech).MaximumFractionDigits.Should().Be(0);
        AnimatedNumberFormat.Describe("C", Czech).MaximumFractionDigits.Should()
            .Be(Czech.NumberFormat.CurrencyDecimalDigits);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.0", 1)]
    [InlineData("0.00", 2)]
    [InlineData("#,##0.0", 1)]
    [InlineData("#,##0", 0)]
    public void CustomZeroAndHashFormats_CountTheirDecimals(string format, int digits)
    {
        var descriptor = AnimatedNumberFormat.Describe(format, English);

        descriptor.MaximumFractionDigits.Should().Be(digits);
        descriptor.Style.Should().Be("decimal");
    }

    /// <summary>Formats the module cannot mirror keep the plain decimal shape; the FINAL frame is exact.</summary>
    [Theory]
    [InlineData("E2")]
    [InlineData("G")]
    [InlineData("X")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UnsupportedOrMissingFormats_FallBackToWholeNumbers(string? format)
    {
        var descriptor = AnimatedNumberFormat.Describe(format, English);

        descriptor.Style.Should().Be("decimal");
        descriptor.MaximumFractionDigits.Should().Be(0);
    }
}
