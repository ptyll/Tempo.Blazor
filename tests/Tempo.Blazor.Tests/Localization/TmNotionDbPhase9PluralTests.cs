using FluentAssertions;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>Phase 9: Czech 1/2/5 plural forms for the calendar overflow and the timeline footer.</summary>
public class TmNotionDbPhase9PluralTests
{
    [Theory]
    [InlineData(1, "1 další")]
    [InlineData(2, "2 další")]
    [InlineData(5, "5 dalších")]
    public void CalendarMore_Czech_1_2_5(int count, string expected)
    {
        var localizer = LocalizationTestBase.BuildCzechLocalizer();

        localizer.Plural("TmNotionDbCalendarView_More", count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 záznam")]
    [InlineData(2, "2 záznamy")]
    [InlineData(5, "5 záznamů")]
    public void TimelineRecordCount_Czech_1_2_5(int count, string expected)
    {
        var localizer = LocalizationTestBase.BuildCzechLocalizer();

        localizer.Plural("TmNotionDbTimelineView_RecordCount", count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "1 komentář")]
    [InlineData(2, "2 komentáře")]
    [InlineData(5, "5 komentářů")]
    public void CommentCount_Czech_1_2_5(int count, string expected)
    {
        var localizer = LocalizationTestBase.BuildCzechLocalizer();

        localizer.Plural("TmNotionBlockComment_RepliesCount", count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Přesunout stránku (1) do…")]
    [InlineData(2, "Přesunout stránky (2) do…")]
    [InlineData(5, "Přesunout stránek (5) do…")]
    public void BulkMoveTitle_Czech_1_2_5(int count, string expected)
    {
        var localizer = LocalizationTestBase.BuildCzechLocalizer();

        localizer.Plural("TmNotionPageTree_BulkMoveTitle", count).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Kopírovat stránku (1) do…")]
    [InlineData(2, "Kopírovat stránky (2) do…")]
    [InlineData(5, "Kopírovat stránek (5) do…")]
    public void BulkCopyTitle_Czech_1_2_5(int count, string expected)
    {
        var localizer = LocalizationTestBase.BuildCzechLocalizer();

        localizer.Plural("TmNotionPageTree_BulkCopyTitle", count).Should().Be(expected);
    }
}
