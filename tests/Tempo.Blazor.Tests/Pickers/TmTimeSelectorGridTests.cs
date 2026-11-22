using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// TDD for <see cref="TmTimeSelector"/> Fáze 5 (plan 1c95309c, ADR-0007): hour/minute
/// radiogroup grids, 2D roving, host-owned presets / Now / recents, square tap-to-open,
/// compact geometry and layout branching.
/// </summary>
public class TmTimeSelectorGridTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";

    private BunitJSModuleInterop SetupSelectorModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
        return module;
    }

    private IRenderedComponent<TmTimeSelector> RenderSelector(
        Action<ComponentParameterCollectionBuilder<TmTimeSelector>>? configure = null,
        TimeHourCycle hourCycle = TimeHourCycle.H23,
        TimeSelectorFit fit = TimeSelectorFit.Regular,
        TmLayoutMode layout = TmLayoutMode.Desktop)
    {
        SetupSelectorModule();
        return Render<TmTimeSelector>(p =>
        {
            p.Add(c => c.HourCycle, hourCycle);
            p.Add(c => c.LayoutMode, layout);
            p.Add(c => c.Fit, fit);
            configure?.Invoke(p);
        });
    }

    private static IElement Hours(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-tm-time-segment='hours']");

    private static IElement Minutes(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-tm-time-segment='minutes']");

    private static IElement Live(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[role='status']");

    private static IReadOnlyList<IElement> HourCells(IRenderedComponent<TmTimeSelector> cut)
        => cut.FindAll("[data-testid='time-selector-grid-hours'] [data-tm-time-grid-cell]");

    private static IReadOnlyList<IElement> MinuteCells(IRenderedComponent<TmTimeSelector> cut)
        => cut.FindAll("[data-testid='time-selector-grid-minutes'] [data-tm-time-grid-cell]");

    [Fact]
    public void RegularAuto_RendersHoursMinutesTabsAndHourRadiogroup()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            layout: TmLayoutMode.Mobile);

        var tabs = cut.Find("[data-testid='time-selector-grid-tabs'] [role='tablist']");
        tabs.Should().NotBeNull();
        tabs.GetAttribute("aria-label").Should().Be("Hours and minutes",
            "the Hours|Minutes tablist must not reuse the whole-control aria-label");
        cut.FindAll("[data-testid='time-selector-grid-tabs'] [role='tab']").Should().HaveCount(2);
        cut.Find("[data-testid='time-selector-grid-tabs'] [data-value='Hours']")
            .GetAttribute("aria-selected").Should().Be("true");

        var hours = cut.Find("[data-testid='time-selector-grid-hours']");
        hours.GetAttribute("role").Should().Be("radiogroup");
        hours.GetAttribute("data-tm-time-grid-columns").Should().Be("6");
        HourCells(cut).Should().HaveCount(24);
        HourCells(cut).Single(c => c.GetAttribute("data-tm-time-hour") == "14")
            .GetAttribute("aria-checked").Should().Be("true");
        cut.FindAll("[data-testid='time-selector-grid-minutes']").Should().BeEmpty(
            "mobile regular shows one grid at a time; Hours is the default tab");
    }

    [Fact]
    public void MinutesTab_ShowsTwelveCellsAtDefaultGridStep()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-grid-tabs'] [data-value='Minutes']").Click();

        var minutes = cut.Find("[data-testid='time-selector-grid-minutes']");
        minutes.GetAttribute("role").Should().Be("radiogroup");
        minutes.GetAttribute("data-tm-time-grid-columns").Should().Be("4");
        MinuteCells(cut).Should().HaveCount(12, "GridMinuteStep defaults to max(MinuteStep, 5) → 12 cells");
        MinuteCells(cut).Single(c => c.GetAttribute("data-tm-time-minute") == "30")
            .GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void HourCell_Click_SetsHour_SwitchesToMinutes_AndAnnounces()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-grid-hours'] [data-tm-time-hour='9']").Click();

        captured.Should().Be(new TimeOnly(9, 30));
        Live(cut).TextContent.Trim().Should().Be("Set to 09:30");
        cut.Find("[data-testid='time-selector-grid-tabs'] [data-value='Minutes']")
            .GetAttribute("aria-selected").Should().Be("true");
        MinuteCells(cut).Should().NotBeEmpty();
        MinuteCells(cut).Single(c => c.GetAttribute("aria-checked") == "true")
            .GetAttribute("data-tm-time-minute").Should().Be("30");
    }

    [Fact]
    public async Task HourCell_Click_FocusesTheSelectedMinuteCell()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            layout: TmLayoutMode.Mobile);
        const string focus = "Blazor._internal.domWrapper.focus";
        var before = JSInterop.Invocations.Count(i => i.Identifier == focus);

        cut.Find("[data-testid='time-selector-grid-hours'] [data-tm-time-hour='9']").Click();
        await cut.InvokeAsync(() => { });

        var focusedMinute = MinuteCells(cut).Single(c => c.GetAttribute("tabindex") == "0");
        focusedMinute.GetAttribute("data-tm-time-minute").Should().Be("30");
        JSInterop.Invocations.Count(i => i.Identifier == focus).Should().BeGreaterThan(before,
            "after an hour tap the hours grid is gone — focus must land on the selected minute cell");
    }

    [Fact]
    public void HourGrid_ArrowRight_MovesRovingTabindex_WithoutChangingValue()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(0, 0))
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            layout: TmLayoutMode.Mobile);

        var cells = HourCells(cut);
        cells[0].GetAttribute("tabindex").Should().Be("0");
        cells[1].GetAttribute("tabindex").Should().Be("-1");

        cells[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        captured.Should().BeNull("arrows move focus only — they never emulate Enter/Space");
        HourCells(cut)[0].GetAttribute("tabindex").Should().Be("-1");
        HourCells(cut)[1].GetAttribute("tabindex").Should().Be("0");
        HourCells(cut)[0].GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public async Task HourGrid_ArrowRight_FocusesTheNextCell()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(0, 0)),
            layout: TmLayoutMode.Mobile);
        const string focus = "Blazor._internal.domWrapper.focus";
        var before = JSInterop.Invocations.Count(i => i.Identifier == focus);

        HourCells(cut)[0].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });
        await cut.InvokeAsync(() => { });

        HourCells(cut)[1].GetAttribute("tabindex").Should().Be("0");
        JSInterop.Invocations.Count(i => i.Identifier == focus).Should().BeGreaterThan(before,
            "ArrowRight must move DOM focus via FocusAsync, not only the roving tabindex");
    }

    [Fact]
    public void HourGrid_ArrowDown_MovesByColumnCount()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(0, 0)),
            layout: TmLayoutMode.Mobile);

        HourCells(cut)[0].KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        HourCells(cut)[6].GetAttribute("tabindex").Should().Be("0",
            "6-column hour grid: ArrowDown from 00 lands on 06");
    }

    [Fact]
    public void HourGrid_NativeButton_DoesNotEmulateEnter()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            layout: TmLayoutMode.Mobile);

        HourCells(cut)[9].KeyDown(new KeyboardEventArgs { Key = "Enter" });

        captured.Should().BeNull(
            "exactly one activation per key press: the native radio button owns Enter/Space");
    }

    [Fact]
    public void OutOfRangeHour_HasAriaDisabled()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(10, 0))
                .Add(c => c.MinTime, new TimeOnly(9, 0))
                .Add(c => c.MaxTime, new TimeOnly(17, 0)),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-tm-time-hour='8']").GetAttribute("aria-disabled").Should().Be("true");
        cut.Find("[data-tm-time-hour='9']").GetAttribute("aria-disabled").Should().BeNull();
        cut.Find("[data-tm-time-hour='18']").GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void SquareAuto_HidesGridUntilSegmentTap()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            fit: TimeSelectorFit.Square);

        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-grid-tabs']").Should().BeEmpty();
        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-grid-open").Should().BeNull();
        cut.FindAll("[data-testid='time-selector-presets']").Should().NotBeEmpty(
            "square shows the preset row until a segment tap opens the grid");
    }

    [Fact]
    public void Square_TapHours_OpensHourGrid_AndHidesPresets()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            fit: TimeSelectorFit.Square);

        Hours(cut).Click();

        var root = cut.Find("[data-testid='time-selector']");
        root.ClassList.Should().Contain("tm-time-selector--grid-open");
        root.GetAttribute("data-tm-time-grid-open").Should().Be("true");
        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().NotBeEmpty();
        cut.FindAll("[data-testid='time-selector-presets']").Should().BeEmpty(
            "opening the square grid replaces the preset row");
        cut.FindAll("[data-testid='time-selector-grid-tabs']").Should().BeEmpty(
            "square has no Hours|Minutes tabs — the tapped segment chooses the grid");
    }

    [Fact]
    public async Task CompactSquare_HoursAt399_UsesEightByThree()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(399, 306));
        Hours(cut).Click();

        var hours = cut.Find("[data-testid='time-selector-grid-hours']");
        hours.GetAttribute("data-tm-time-grid-columns").Should().Be("8");
        HourCells(cut).Should().HaveCount(24);
    }

    [Fact]
    public async Task CompactSquare_InnerWidth395_UsesSixHourColumns()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(399, 306, 395));
        Hours(cut).Click();

        cut.Find("[data-testid='time-selector-grid-hours']")
            .GetAttribute("data-tm-time-grid-columns").Should().Be("6",
                "8×3 requires inner content width ≥ 396; a 399 px viewport with padding is 6×4");
    }

    [Fact]
    public async Task CompactSquare_InnerWidth396_UsesEightHourColumns()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(399, 306, 396));
        Hours(cut).Click();

        cut.Find("[data-testid='time-selector-grid-hours']")
            .GetAttribute("data-tm-time-grid-columns").Should().Be("8");
    }

    [Fact]
    public async Task CompactSquare_HoursBelow396_StaySixByFour()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(374, 374));
        Hours(cut).Click();

        cut.Find("[data-testid='time-selector-grid-hours']")
            .GetAttribute("data-tm-time-grid-columns").Should().Be("6");
    }

    [Fact]
    public void CompactSquare_Minutes_AreSixByTwo()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        Minutes(cut).Click();

        var minutes = cut.Find("[data-testid='time-selector-grid-minutes']");
        minutes.GetAttribute("data-tm-time-grid-columns").Should().Be("6");
        MinuteCells(cut).Should().HaveCount(12);
    }

    [Fact]
    public void CompactSquare_12HourHours_AreSixByTwo()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            hourCycle: TimeHourCycle.H12,
            fit: TimeSelectorFit.Square);

        Hours(cut).Click();

        var hours = cut.Find("[data-testid='time-selector-grid-hours']");
        hours.GetAttribute("data-tm-time-grid-columns").Should().Be("6");
        HourCells(cut).Should().HaveCount(12);
        HourCells(cut).Select(c => c.GetAttribute("data-tm-time-hour")).Should().Equal(
            "1", "2", "3", "4", "5", "6", "7", "8", "9", "10", "11", "12");
    }

    [Fact]
    public void Presets_RenderNativeChips_WithAriaPressedOnMatch()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(12, 0))
                .Add(c => c.Presets, new[]
                {
                    new TimePreset("Noon", new TimeOnly(12, 0)),
                    new TimePreset(null, new TimeOnly(18, 0)),
                }),
            layout: TmLayoutMode.Mobile);

        var group = cut.Find("[data-testid='time-selector-presets']");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Quick picks");

        var noon = cut.Find("[data-testid='time-selector-preset'][data-tm-time-preset='12:00']");
        noon.TagName.Should().Be("BUTTON");
        noon.GetAttribute("aria-pressed").Should().Be("true");
        noon.TextContent.Trim().Should().Be("Noon");

        var evening = cut.Find("[data-testid='time-selector-preset'][data-tm-time-preset='18:00']");
        evening.GetAttribute("aria-pressed").Should().Be("false");
        evening.TextContent.Trim().Should().Be("18:00");
    }

    [Fact]
    public void PresetClick_CommitsAndAnnounces()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v)
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-preset'][data-tm-time-preset='12:00']").Click();

        captured.Should().Be(new TimeOnly(12, 0));
        Live(cut).TextContent.Trim().Should().Be("Set to 12:00");
    }

    [Fact]
    public void OutOfRangePreset_IsAriaDisabled()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(10, 0))
                .Add(c => c.MinTime, new TimeOnly(9, 0))
                .Add(c => c.MaxTime, new TimeOnly(17, 0))
                .Add(c => c.Presets, new[] { new TimePreset("Night", new TimeOnly(22, 0)) }),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-preset'][data-tm-time-preset='22:00']")
            .GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void ShowNowPreset_UsesInjectedTimeProvider()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 7, 4, 9, 17, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);

        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.ShowNowPreset, true)
                .Add(c => c.MinuteStep, 5)
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            layout: TmLayoutMode.Mobile);

        var now = cut.Find("[data-testid='time-selector-preset-now']");
        now.TextContent.Trim().Should().Be("Now");
        now.Click();

        captured.Should().Be(new TimeOnly(9, 20),
            "Now rounds up to the minute step from the injected TimeProvider, not the machine clock");
    }

    [Fact]
    public void NowPreset_OutOfRange_IsAriaDisabled()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 7, 4, 22, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);

        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(10, 0))
                .Add(c => c.ShowNowPreset, true)
                .Add(c => c.MaxTime, new TimeOnly(17, 0)),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-preset-now']")
            .GetAttribute("aria-disabled").Should().Be("true",
                "Now must be aria-disabled when StartingTime is outside MinTime/MaxTime");
    }

    [Fact]
    public void RecentTimes_ShowsAtMostThree_DedupedAgainstPresets()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) })
                .Add(c => c.RecentTimes, new[]
                {
                    new TimeOnly(12, 0),
                    new TimeOnly(8, 15),
                    new TimeOnly(9, 45),
                    new TimeOnly(11, 0),
                    new TimeOnly(16, 0),
                }),
            layout: TmLayoutMode.Mobile);

        var recents = cut.FindAll("[data-testid='time-selector-preset-recent']");
        recents.Should().HaveCount(3);
        recents.Select(c => c.GetAttribute("data-tm-time-preset")).Should().Equal(
            "08:15", "09:45", "11:00");
        recents[0].GetAttribute("aria-label").Should().Be("Recent 08:15",
            "recent chips use TmTimeSelector_RecentValue so the time is not glued onto the adjective");
    }

    [Fact]
    public void ReadOnly_HidesGridAndPresets()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(10, 0))
                .Add(c => c.ReadOnly, true)
                .Add(c => c.ShowNowPreset, true)
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            layout: TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-presets']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-grid-tabs']").Should().BeEmpty();
    }

    [Fact]
    public void Disabled_HidesGridAndPresets()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(10, 0))
                .Add(c => c.Disabled, true)
                .Add(c => c.ShowNowPreset, true)
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            layout: TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-presets']").Should().BeEmpty();
    }

    [Fact]
    public async Task KeyboardOpen_OnSquare_HidesGridAndPresets()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.Presets, new[] { new TimePreset("Noon", new TimeOnly(12, 0)) }),
            fit: TimeSelectorFit.Square);

        Hours(cut).Click();
        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().NotBeEmpty();

        await cut.InvokeAsync(() => cut.Instance.OnKeyboardChanged(true));
        cut.Render();

        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-presets']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-increase-hours']").Should().BeEmpty();
        Hours(cut).TextContent.Trim().Should().Be("14");
    }

    [Fact]
    public void Tablet_ShowsBothGrids_WithoutTabs()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            layout: TmLayoutMode.Tablet);

        cut.FindAll("[data-testid='time-selector-grid-tabs']").Should().BeEmpty();
        HourCells(cut).Should().HaveCount(24);
        MinuteCells(cut).Should().HaveCount(12);
        cut.Find("[data-testid='time-selector']")
            .GetAttribute("data-tm-time-layout").Should().Be("tablet");
    }

    [Fact]
    public void MobileRegular_OffersTypeTime_SquareNeverDoes()
    {
        var mobile = RenderSelector(p => p.Add(c => c.AllowTyping, true),
            layout: TmLayoutMode.Mobile);
        mobile.Find("[data-testid='time-selector-type-time']").TextContent.Trim()
            .Should().Be("Type the time");

        var square = RenderSelector(p => p.Add(c => c.AllowTyping, true),
            fit: TimeSelectorFit.Square);
        square.FindAll("[data-testid='time-selector-type-time']").Should().BeEmpty();
    }

    [Fact]
    public void TypeTime_ReplacesReadoutWithNumericInput()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.AllowTyping, true)
                .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-type-time']").Click();

        var input = cut.Find("[data-testid='time-selector-type-input']");
        input.GetAttribute("inputmode").Should().Be("numeric");
        input.GetAttribute("autocomplete").Should().Be("off");
        input.GetAttribute("enterkeyhint").Should().Be("done");
        input.GetAttribute("aria-label").Should().Be("Type the time");
        cut.FindAll("[data-tm-time-segment='hours']").Should().BeEmpty();
        cut.Find("[data-testid='time-selector-use-controls']").TextContent.Trim()
            .Should().Be("Use buttons");

        input.Input("09:15");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        captured.Should().Be(new TimeOnly(9, 15));
    }

    [Fact]
    public void ShowGridNone_NeverRendersGrid()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(14, 30))
                .Add(c => c.ShowGrid, TimeSelectorGrid.None),
            layout: TmLayoutMode.Mobile);

        cut.FindAll("[data-testid='time-selector-grid-hours']").Should().BeEmpty();
        cut.FindAll("[data-testid='time-selector-grid-tabs']").Should().BeEmpty();
    }

    [Fact]
    public void GridMinuteStep_Invalid_ThrowsNamedGridMinuteStep()
    {
        SetupSelectorModule();
        var act = () => Render<TmTimeSelector>(p => p
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.GridMinuteStep, 7));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("GridMinuteStep");
    }

    [Fact]
    public void OvernightRange_HourGrid_DisablesNoon_AndEnablesTwentyThree()
    {
        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(23, 0))
                .Add(c => c.MinTime, new TimeOnly(22, 0))
                .Add(c => c.MaxTime, new TimeOnly(6, 0))
                .Add(c => c.AllowOvernightRange, true),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-tm-time-hour='12']").GetAttribute("aria-disabled").Should().Be("true");
        cut.Find("[data-tm-time-hour='23']").GetAttribute("aria-disabled").Should().BeNull();
        cut.Find("[data-tm-time-hour='0']").GetAttribute("aria-disabled").Should().BeNull();
        cut.Find("[data-tm-time-hour='6']").GetAttribute("aria-disabled").Should().BeNull();
    }

    [Fact]
    public void OvernightRange_StartingTime_DoesNotSnapToMin()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 7, 4, 12, 0, 0, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);

        var cut = RenderSelector(p => p
                .Add(c => c.Value, new TimeOnly(23, 0))
                .Add(c => c.ShowNowPreset, true)
                .Add(c => c.MinTime, new TimeOnly(22, 0))
                .Add(c => c.MaxTime, new TimeOnly(6, 0))
                .Add(c => c.AllowOvernightRange, true),
            layout: TmLayoutMode.Mobile);

        cut.Find("[data-testid='time-selector-preset-now']")
            .GetAttribute("data-tm-time-preset").Should().Be("12:00",
                "overnight StartingTime must keep the rounded now instead of snapping to MinTime");
    }
}
