using FluentAssertions;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// Invariant wire form shared by <c>TmSelect</c> and native radio <c>value</c>.
/// Segmented <c>data-value</c> is a separate DOM contract (see
/// <c>TmSegmentedControlTests</c>) and must not silently follow this formatter
/// for existing types.
/// </summary>
public class OptionValueFormatterTests
{
    [Fact]
    public void Bool_IsLowercaseTrueFalse()
    {
        OptionValueFormatter.Format(true).Should().Be("true");
        OptionValueFormatter.Format(false).Should().Be("false");
    }

    [Fact]
    public void DateOnly_IsIsoDate()
        => OptionValueFormatter.Format(new DateOnly(2026, 3, 15)).Should().Be("2026-03-15");

    [Fact]
    public void TimeOnly_IsInvariantWithFraction()
        => OptionValueFormatter.Format(new TimeOnly(14, 30, 5)).Should().Be("14:30:05");

    [Fact]
    public void Null_IsEmpty()
        => OptionValueFormatter.Format<string>(null).Should().BeEmpty();
}
