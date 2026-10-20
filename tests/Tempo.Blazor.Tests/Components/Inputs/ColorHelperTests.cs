using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Tests.Components.Inputs;

public class ColorHelperTests
{
    [Theory]
    [InlineData("#FF0000", 255, 0, 0, 1.0)]
    [InlineData("00FF00", 0, 255, 0, 1.0)]
    [InlineData("#0000FF", 0, 0, 255, 1.0)]
    [InlineData("#FF000080", 255, 0, 0, 0.502)]
    [InlineData("rgb(128, 64, 32)", 128, 64, 32, 1.0)]
    [InlineData("rgba(10, 20, 30, 0.5)", 10, 20, 30, 0.5)]
    public void Parse_ValidInput_ReturnsCorrectRgba(string input, byte r, byte g, byte b, double a)
    {
        var result = ColorHelper.Parse(input);
        result.R.Should().Be(r);
        result.G.Should().Be(g);
        result.B.Should().Be(b);
        result.A.Should().BeApproximately(a, 0.01);
    }

    [Fact]
    public void Parse_Empty_ReturnsBlack()
    {
        var result = ColorHelper.Parse("");
        result.Should().Be((0, 0, 0, 1.0));
    }

    [Theory]
    [InlineData(255, 0, 0, 1.0, "#FF0000")]
    [InlineData(0, 255, 0, 1.0, "#00FF00")]
    [InlineData(0, 0, 255, 0.5, "#0000FF80")]
    public void ToHex_ReturnsCorrectString(byte r, byte g, byte b, double a, string expected)
    {
        ColorHelper.ToHex(r, g, b, a).Should().Be(expected);
    }

    [Fact]
    public void ToRgb_ReturnsCorrectString()
    {
        ColorHelper.ToRgb(128, 64, 32).Should().Be("rgb(128, 64, 32)");
    }

    [Fact]
    public void ToRgba_ReturnsCorrectString()
    {
        ColorHelper.ToRgba(10, 20, 30, 0.5).Should().Be("rgba(10, 20, 30, 0.5)");
    }

    [Theory]
    [InlineData(255, 0, 0, 0, 1, 1)]    // Red
    [InlineData(0, 255, 0, 120, 1, 1)]  // Green
    [InlineData(0, 0, 255, 240, 1, 1)]  // Blue
    [InlineData(128, 128, 128, 0, 0, 0.502)] // Gray
    public void RgbToHsv_ReturnsCorrectHsv(byte r, byte g, byte b, double h, double s, double v)
    {
        var result = ColorHelper.RgbToHsv(r, g, b);
        result.H.Should().BeApproximately(h, 1.0);
        result.S.Should().BeApproximately(s, 0.01);
        result.V.Should().BeApproximately(v, 0.01);
    }

    [Theory]
    [InlineData(0, 1, 1, 255, 0, 0)]    // Red
    [InlineData(120, 1, 1, 0, 255, 0)]  // Green
    [InlineData(240, 1, 1, 0, 0, 255)]  // Blue
    public void HsvToRgb_ReturnsCorrectRgb(double h, double s, double v, byte r, byte g, byte b)
    {
        var result = ColorHelper.HsvToRgb(h, s, v);
        result.R.Should().Be(r);
        result.G.Should().Be(g);
        result.B.Should().Be(b);
    }

    [Fact]
    public void RgbToHsv_And_HsvToRgb_Roundtrip()
    {
        var (h, s, v) = ColorHelper.RgbToHsv(123, 200, 77);
        var (r, g, b) = ColorHelper.HsvToRgb(h, s, v);
        r.Should().Be(123);
        g.Should().Be(200);
        b.Should().Be(77);
    }

    [Fact]
    public void HsvToRgb_Rounds_DoesNot_Truncate()
    {
        // Truncation rebuilt #3B82F6 as #3A82F6: every channel a unit low, so the round trip
        // could not return to its input and a keyboard-stepped colour drifted.
        var (h, s, v) = ColorHelper.RgbToHsv(59, 130, 246);
        var (r, g, b) = ColorHelper.HsvToRgb(h, s, v);
        r.Should().Be(59);
        g.Should().Be(130);
        b.Should().Be(246);
    }
}
