using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// <see cref="DateRangePresets.Create(TimeProvider, ITmLocalizer)"/> — the localized preset factory
/// the pickers build their default preset row from. The legacy static members read
/// <c>DateTime.Today</c> and hardcode English labels; the factory takes both facts from its
/// arguments so a test (and a server) can pin them.
/// </summary>
public class DateRangePresetsCreateTests
{
    private static readonly Dictionary<string, string> EnglishLabels = new(StringComparer.Ordinal)
    {
        ["TmDateRangePicker_Preset_Today"] = "Today",
        ["TmDateRangePicker_Preset_LastWeek"] = "Last week",
        ["TmDateRangePicker_Preset_LastMonth"] = "Last month",
        ["TmDateRangePicker_Preset_LastQuarter"] = "Last quarter",
        ["TmDateRangePicker_Preset_ThisYear"] = "This year",
    };

    private static readonly Dictionary<string, string> CzechLabels = new(StringComparer.Ordinal)
    {
        ["TmDateRangePicker_Preset_Today"] = "Dnes",
        ["TmDateRangePicker_Preset_LastWeek"] = "Minulý týden",
        ["TmDateRangePicker_Preset_LastMonth"] = "Minulý měsíc",
        ["TmDateRangePicker_Preset_LastQuarter"] = "Minulý kvartál",
        ["TmDateRangePicker_Preset_ThisYear"] = "Letos",
    };

    private static readonly Lazy<IReadOnlyList<DateRangePreset>> Presets = new(() =>
        DateRangePresets.Create(Clock(), new MockTmLocalizer(EnglishLabels)));

    /// <summary>A clock pinned to Friday 4 July 2031 — nowhere near the machine's own date.</summary>
    private static FakeTimeProvider Clock() =>
        new(new DateTimeOffset(2031, 7, 4, 9, 30, 0, TimeSpan.Zero));

    private static DateRangePreset Named(string label) =>
        Presets.Value.Single(p => p.Label == label);

    [Fact]
    public void Create_ReturnsTheFiveBuiltInWindowsInTheirHistoricalOrder()
    {
        Presets.Value.Select(p => p.Label).Should().Equal(
            "Today", "Last week", "Last month", "Last quarter", "This year");
    }

    [Fact]
    public void Create_ComputesEveryWindowFromTheClockNotFromTheMachineDate()
    {
        Named("Today").Should().Be(new DateRangePreset(
            "Today", new DateOnly(2031, 7, 4), new DateOnly(2031, 7, 4)));

        Named("Last week").Should().Be(new DateRangePreset(
            "Last week", new DateOnly(2031, 6, 23), new DateOnly(2031, 6, 29)),
            "4 July 2031 is a Friday, so last week is Monday 23 June to Sunday 29 June");

        Named("Last month").Should().Be(new DateRangePreset(
            "Last month", new DateOnly(2031, 6, 1), new DateOnly(2031, 6, 30)));

        Named("Last quarter").Should().Be(new DateRangePreset(
            "Last quarter", new DateOnly(2031, 4, 1), new DateOnly(2031, 6, 30)));

        Named("This year").Should().Be(new DateRangePreset(
            "This year", new DateOnly(2031, 1, 1), new DateOnly(2031, 7, 4)));
    }

    [Fact]
    public void Create_LooksEveryLabelUpInTheLocalizer()
    {
        var czech = DateRangePresets.Create(Clock(), new MockTmLocalizer(CzechLabels));

        czech.Select(p => p.Label).Should().Equal(
            "Dnes", "Minulý týden", "Minulý měsíc", "Minulý kvartál", "Letos");
    }

    [Fact]
    public void Create_FallsBackToTheEnglishLabelWhenTheLocalizerHasNoTranslation()
    {
        var sparse = DateRangePresets.Create(Clock(), new MockTmLocalizer(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["TmDateRangePicker_Preset_Today"] = "Aujourd'hui",
            }));

        sparse.Select(p => p.Label).Should().Equal(
            "Aujourd'hui", "Last week", "Last month", "Last quarter", "This year");
    }

    [Fact]
    public void Create_RejectsNullArguments()
    {
        var clock = Clock();
        var localizer = new MockTmLocalizer(EnglishLabels);

        FluentActions.Invoking(() => DateRangePresets.Create(null!, localizer))
            .Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => DateRangePresets.Create(clock, null!))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    /// The legacy members stay: a host that already reads <c>DateRangePresets.Today</c> keeps
    /// compiling and keeps its behaviour (the machine date, English label). They are documented as
    /// legacy, not marked <c>[Obsolete]</c>.
    /// </summary>
    [Fact]
    public void LegacyStaticMembers_KeepTheMachineDateAndTheEnglishLabel()
    {
        DateRangePresets.Today.Label.Should().Be("Today");
        DateRangePresets.Today.Start.Should().Be(DateOnly.FromDateTime(DateTime.Today));
        DateRangePresets.Today.End.Should().Be(DateOnly.FromDateTime(DateTime.Today));

        foreach (var property in typeof(DateRangePresets).GetProperties())
        {
            property.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Should().BeEmpty(
                $"{property.Name} is kept as a legacy member, not deprecated with [Obsolete]");
        }
    }
}
