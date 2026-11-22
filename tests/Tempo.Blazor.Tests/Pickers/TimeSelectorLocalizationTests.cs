using FluentAssertions;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Localization contract of the forthcoming <c>TmTimeSelector</c> (plan 1c95309c phase 2):
/// English hour <c>aria-valuetext</c> is "hour/hours", not "o'clock", and Hours/Minutes tabs
/// stay in lockstep with the existing time-picker keys.
/// </summary>
public class TimeSelectorLocalizationTests : LocalizationTestBase
{
    [Theory]
    [InlineData(0, "0 hours")]
    [InlineData(1, "1 hour")]
    [InlineData(14, "14 hours")]
    public void HourValue_English_UsesHourNotOClock(int hour, string expected)
    {
        BuildEnglishLocalizer().Plural("TmTimeSelector_HourValue", hour).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "0 hodin")]
    [InlineData(1, "1 hodina")]
    [InlineData(2, "2 hodiny")]
    [InlineData(5, "5 hodin")]
    [InlineData(14, "14 hodin")]
    public void HourValue_Czech_UsesTheGrammaticalForm(int hour, string expected)
    {
        BuildCzechLocalizer().Plural("TmTimeSelector_HourValue", hour).Should().Be(expected);
    }

    [Fact]
    public void HoursAndMinutesTabs_MatchTheTimePickerKeys_InTheMockMaps()
    {
        var en = BuildEnglishLocalizer();
        en["TmTimeSelector_HoursTab"].Should().Be(en["TmTimePicker_Hours"]);
        en["TmTimeSelector_MinutesTab"].Should().Be(en["TmTimePicker_Minutes"]);

        var cs = BuildCzechLocalizer();
        cs["TmTimeSelector_HoursTab"].Should().Be(cs["TmTimePicker_Hours"]);
        cs["TmTimeSelector_MinutesTab"].Should().Be(cs["TmTimePicker_Minutes"]);
    }
}
