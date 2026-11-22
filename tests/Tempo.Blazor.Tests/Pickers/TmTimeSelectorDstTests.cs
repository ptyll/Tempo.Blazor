using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Plan 1c95309c Fáze 6 — DST gap (disabled) and ambiguous offset choice.
/// Both <see cref="TmTimeSelector.ReferenceDate"/> and <see cref="TmTimeSelector.TimeZone"/>
/// must be set; Europe/Prague 2026-03-29 / 2026-10-25.
/// </summary>
public class TmTimeSelectorDstTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";

    private static TimeZoneInfo Prague()
    {
        foreach (var id in new[] { "Europe/Prague", "Central Europe Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new InvalidOperationException("Europe/Prague (or Windows CET) time zone is not available.");
    }

    private void SetupSelectorModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
    }

    [Fact]
    public void Gap_DisablesMissingHour_AndMarksBoundValueInvalid()
    {
        SetupSelectorModule();
        TimeOnly? value = new TimeOnly(2, 30);
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.ShowGrid, TimeSelectorGrid.Always)
            .Add(c => c.ReferenceDate, new DateOnly(2026, 3, 29))
            .Add(c => c.TimeZone, Prague()));

        value.Should().Be(new TimeOnly(2, 30), "a gap value from binding is not rewritten");
        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-time-selector__error").TextContent.Should().Contain("does not exist");

        var hour2 = cut.Find("[data-tm-time-hour='2']");
        hour2.GetAttribute("aria-disabled").Should().Be("true");
        hour2.GetAttribute("aria-describedby").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Ambiguous_OffersEarlierAndLater_AndRaisesOffset()
    {
        SetupSelectorModule();
        TimeSpan? offset = null;
        TimeOnly? value = new TimeOnly(2, 30);
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.ReferenceDate, new DateOnly(2026, 10, 25))
            .Add(c => c.TimeZone, Prague())
            .Add(c => c.AmbiguousOffsetChanged, (TimeSpan v) => offset = v));

        cut.Find(".tm-time-selector__dst").Should().NotBeNull();
        cut.Find(".tm-time-selector__dst").TextContent.Should().Contain("occurs twice");
        cut.FindAll(".tm-time-selector__dst input[type='radio']").Should().HaveCount(2);
        cut.Markup.Should().Contain("Summer time");
        cut.Markup.Should().Contain("Winter time");

        cut.FindAll(".tm-time-selector__dst input[type='radio']")[1].Change("true");
        offset.Should().NotBeNull();
        offset!.Value.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void WithoutTimeZone_DoesNotApplyDstRules()
    {
        SetupSelectorModule();
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, new TimeOnly(2, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.ShowGrid, TimeSelectorGrid.Always)
            .Add(c => c.ReferenceDate, new DateOnly(2026, 3, 29)));

        cut.FindAll(".tm-time-selector__error").Should().BeEmpty();
        cut.Find("[data-tm-time-hour='2']").GetAttribute("aria-disabled").Should().BeNull();
    }
}
