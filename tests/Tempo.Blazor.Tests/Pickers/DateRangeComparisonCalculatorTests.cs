using FluentAssertions;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// The comparison-window maths of <see cref="DateRangeComparisonCalculator"/> in isolation: the
/// picker and a host backend must agree on the window to the day, so the arithmetic is a pure
/// public helper rather than component internals.
/// </summary>
public class DateRangeComparisonCalculatorTests
{
    [Fact]
    public void PreviousPeriod_IsTheSameLengthEndingTheDayBeforeStart()
    {
        var previous = DateRangeComparisonCalculator.PreviousPeriod(
            new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14));

        previous.Should().Be((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9)),
            "a five-day window is compared against the five days immediately before it");
    }

    [Fact]
    public void PreviousPeriod_OfASingleDayIsTheDayBefore()
    {
        DateRangeComparisonCalculator.PreviousPeriod(new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 1))
            .Should().Be((new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 28)));
    }

    [Fact]
    public void PreviousPeriod_SpansTheYearBoundary()
    {
        DateRangeComparisonCalculator.PreviousPeriod(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 3))
            .Should().Be((new DateOnly(2025, 12, 29), new DateOnly(2025, 12, 31)));
    }

    [Fact]
    public void PreviousPeriod_NormalizesAReversedRange()
    {
        var previous = DateRangeComparisonCalculator.PreviousPeriod(
            new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 10));

        previous.Should().Be((new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9)),
            "the caller's direction is a UI artefact — the window itself is ordered");
    }

    [Fact]
    public void PreviousYear_KeepsTheSameWindowOneYearEarlier()
    {
        DateRangeComparisonCalculator.PreviousYear(new DateOnly(2026, 10, 10), new DateOnly(2026, 10, 14))
            .Should().Be((new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 14)));
    }

    [Fact]
    public void PreviousYear_ClampsTheLeapDayToTheTwentyEighth()
    {
        DateRangeComparisonCalculator.PreviousYear(new DateOnly(2024, 2, 29), new DateOnly(2024, 2, 29))
            .Should().Be((new DateOnly(2023, 2, 28), new DateOnly(2023, 2, 28)),
                "2023 has no 29 February, so the window ends on the last day of the same month");
    }

    [Fact]
    public void PreviousYear_ClampsBothEndsOfAWholeFebruaryWindow()
    {
        DateRangeComparisonCalculator.PreviousYear(new DateOnly(2024, 2, 1), new DateOnly(2024, 2, 29))
            .Should().Be((new DateOnly(2023, 2, 1), new DateOnly(2023, 2, 28)));
    }

    [Fact]
    public void PreviousYear_NormalizesAReversedRange()
    {
        DateRangeComparisonCalculator.PreviousYear(new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 10))
            .Should().Be((new DateOnly(2025, 10, 10), new DateOnly(2025, 10, 14)));
    }
}
