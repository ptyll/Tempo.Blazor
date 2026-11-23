using System.Globalization;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>
/// Fáze 10a remaining b53df865 / 64d0e9a2: numeric preselection that formats differently from the
/// matching option, and round-trip coverage of the remaining TValue types.
/// </summary>
public partial class TmSelectTests
{
    [Fact]
    public void TmSelect_Decimal_PreselectsTheMatchingOptionWireValue()
    {
        var options = new List<SelectOption<decimal>> { new(1.0m, "One"), new(1.5m, "One and a half") };
        var cut = Render<TmSelect<decimal>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, 1.5000m));

        cut.Find("select").GetAttribute("value").Should().Be("1.5");
        var selected = cut.FindAll("option").Single(o => o.HasAttribute("selected"));
        selected.GetAttribute("value").Should().Be("1.5");
        selected.TextContent.Should().Be("One and a half");
    }

    [Fact]
    public void TmSelect_NullableDecimal_PreselectsTheMatchingOptionWireValue()
    {
        var options = new List<SelectOption<decimal?>> { new(1.0m, "One"), new(1.5m, "One and a half") };
        var cut = Render<TmSelect<decimal?>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, 1.5000m));

        cut.Find("select").GetAttribute("value").Should().Be("1.5");
        cut.FindAll("option").Single(o => o.HasAttribute("selected")).GetAttribute("value").Should().Be("1.5");
    }

    [Fact]
    public void TmSelect_Double_PreselectsTheMatchingOptionWireValue()
    {
        var options = new List<SelectOption<double>> { new(1.0d, "One"), new(1.5d, "One and a half") };
        var cut = Render<TmSelect<double>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Value, 1.50d));

        cut.Find("select").GetAttribute("value").Should().Be("1.5");
        cut.FindAll("option").Single(o => o.HasAttribute("selected")).GetAttribute("value").Should().Be("1.5");
    }

    [Fact]
    public void TmSelect_WithoutOptions_KeepsFormattingTheBoundValue()
    {
        var cut = Render<TmSelect<decimal>>(p => p
            .Add(c => c.Id, "tmsel-no-opts")
            .Add(c => c.Value, 1.5000m)
            .AddChildContent("<option value=\"1.5\">One and a half</option>"));

        cut.Find("select").GetAttribute("value").Should().Be("1.5000");
    }

    [Fact]
    public void TmSelect_Bool_RoundTrips() => AssertRoundTrip(true);

    [Fact]
    public void TmSelect_DateOnly_RoundTrips() => AssertRoundTrip(new DateOnly(2026, 11, 23));

    [Fact]
    public void TmSelect_TimeOnly_RoundTrips() => AssertRoundTrip(new TimeOnly(14, 30, 5, 123, 456));

    [Fact]
    public void TmSelect_DateTime_RoundTrips() =>
        AssertRoundTrip(new DateTime(2026, 11, 23, 14, 30, 5, DateTimeKind.Utc));

    [Fact]
    public void TmSelect_DateTimeOffset_RoundTrips() =>
        AssertRoundTrip(new DateTimeOffset(2026, 11, 23, 14, 30, 5, TimeSpan.FromHours(1)));

    [Fact]
    public void TmSelect_Char_RoundTrips() => AssertRoundTrip('Ž');

    [Fact]
    public void TmSelect_Nint_RoundTrips() => AssertRoundTrip((nint)42);

    [Fact]
    public void TmSelect_Nuint_RoundTrips() => AssertRoundTrip((nuint)42);

    [Fact]
    public void TmSelect_Half_RoundTrips() => AssertRoundTrip((Half)1.5f);

    [Fact]
    public void TmSelect_Int128_RoundTrips() => AssertRoundTrip((Int128)1234567890123456789);

    [Fact]
    public void TmSelect_UInt128_RoundTrips() => AssertRoundTrip((UInt128)1234567890123456789);

    [Fact]
    public void TmSelect_UnconvertibleRaw_DoesNotCallValueChanged()
    {
        var called = false;
        var options = new List<SelectOption<DateOnly>> { new(new DateOnly(2026, 11, 23), "D") };
        var cut = Render<TmSelect<DateOnly>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<DateOnly>(this, _ => called = true)));

        cut.Find("select").Change("not-a-date");

        called.Should().BeFalse("an unconvertible raw value must not raise ValueChanged");
    }
}
