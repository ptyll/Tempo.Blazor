using System.Globalization;
using FluentAssertions;
using Tempo.Blazor.AI.Formatting;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.AI;

/// <summary>
/// Pins the two formatting helpers behind the Fáze 4 components: the JSON pretty-printer (a tool call's
/// raw model output is shown indented and never dropped when it is not valid JSON) and the duration
/// formatter (the "4,2 s" of the Czech summary and the "4.2 s" of the English one come from the same
/// code, and a long run reads as minutes rather than as 415 seconds).
/// </summary>
public sealed class AiToolCallFormattingTests
{
    [Fact]
    public void APrettyPrintableDocument_IsIndentedWithTwoSpaces()
    {
        AiJsonPrettyPrinter.TryFormat("""{"query":"refunds","limit":5}""", out var formatted)
            .Should().BeTrue();

        formatted.Should().Be("{\n  \"query\": \"refunds\",\n  \"limit\": 5\n}");
    }

    [Fact]
    public void NestedArraysAndObjects_KeepTheirNesting()
    {
        AiJsonPrettyPrinter.TryFormat("""{"a":[1,{"b":true}]}""", out var formatted).Should().BeTrue();

        formatted.Should().Contain("\n  \"a\": [");
        formatted.Should().Contain("\n    1,");
        formatted.Should().Contain("\n    {");
        formatted.Should().Contain("\n      \"b\": true");
    }

    [Fact]
    public void AJsonScalar_IsAlreadyReadable()
    {
        AiJsonPrettyPrinter.TryFormat("42", out var formatted).Should().BeTrue();
        formatted.Should().Be("42");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json { at all")]
    [InlineData("""{"unterminated": """)]
    public void InvalidOrMissingJson_ReportsFailure_SoTheCallerKeepsTheRawText(string payload)
    {
        AiJsonPrettyPrinter.TryFormat(payload, out var formatted).Should().BeFalse();
        formatted.Should().BeEmpty();
    }

    [Fact]
    public void Null_ReportsFailure()
    {
        AiJsonPrettyPrinter.TryFormat(null, out _).Should().BeFalse();
    }

    [Fact]
    public void ASingleLinePayload_KeepsTheTrailingCommaToleranceOfTheModel()
    {
        AiJsonPrettyPrinter.TryFormat("""{"a":1,}""", out var formatted).Should().BeTrue();
        formatted.Should().Contain("\"a\": 1");
    }

    [Fact]
    public void ShortDurations_ReadAsSecondsWithOneDecimal_InTheLocalizerCulture()
    {
        var english = Localizer("en");
        var czech = Localizer("cs");

        AiDurationFormatter.Format(TimeSpan.FromSeconds(4.2), english).Should().Be("4.2 s");
        AiDurationFormatter.Format(TimeSpan.FromSeconds(4.2), czech).Should().Be("4,2 s");
    }

    [Fact]
    public void LongerDurations_ReadAsMinutesAndSeconds()
    {
        AiDurationFormatter.Format(TimeSpan.FromSeconds(75), Localizer("en")).Should().Be("1 min 15 s");
    }

    [Fact]
    public void VeryLongDurations_ReadAsHoursAndMinutes()
    {
        AiDurationFormatter.Format(TimeSpan.FromSeconds(3_900), Localizer("en")).Should().Be("1 h 5 min");
    }

    [Fact]
    public void TheDurationIsNeverNegative()
    {
        AiDurationFormatter.Format(TimeSpan.FromSeconds(-5), Localizer("en")).Should().Be("0.0 s");
    }

    private static ITmLocalizer Localizer(string culture) => new MockTmLocalizer(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["TmAiDuration_Seconds"] = "{0} s",
            ["TmAiDuration_Minutes"] = "{0} min {1} s",
            ["TmAiDuration_Hours"] = "{0} h {1} min",
        },
        CultureInfo.GetCultureInfo(culture));
}
