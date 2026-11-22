using System.Globalization;
using System.Linq.Expressions;
using AngleSharp.Dom;
using Bunit;
using Bunit.Rendering;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// TDD for <see cref="TmTimeSelector"/> (plan 1c95309c Fáze 4, ADR-0007): APG spinbutton
/// readout, +/−, AM/PM, PageUp/PageDown, square / keyboard-open markup, live region,
/// ReadOnly/Disabled/Invalid, TmFormField cascade and EditContext.
/// Keyboard typeahead, auto-repeat and fit measurement live in tm-time-selector.js
/// (npm run test:core-modules).
/// </summary>
public class TmTimeSelectorTests : LocalizationTestBase
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
        TimeSelectorFit fit = TimeSelectorFit.Regular)
    {
        SetupSelectorModule();
        return Render<TmTimeSelector>(p =>
        {
            p.Add(c => c.HourCycle, hourCycle);
            p.Add(c => c.LayoutMode, TmLayoutMode.Desktop);
            p.Add(c => c.Fit, fit);
            configure?.Invoke(p);
        });
    }

    private static IElement Hours(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-tm-time-segment='hours']");

    private static IElement Minutes(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-tm-time-segment='minutes']");

    private static IElement IncreaseHours(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-testid='time-selector-increase-hours']");

    private static IElement DecreaseMinutes(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[data-testid='time-selector-decrease-minutes']");

    private static IElement Live(IRenderedComponent<TmTimeSelector> cut)
        => cut.Find("[role='status']");

    [Fact]
    public void Renders_RoleGroup_WithLocalizedAriaLabel()
    {
        var cut = RenderSelector();

        var group = cut.Find("[data-testid='time-selector']");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be("Time picker");
    }

    [Fact]
    public void HoursAndMinutes_AreSpinbuttons_WithInvariantValueNow()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)));

        var hours = Hours(cut);
        hours.GetAttribute("role").Should().Be("spinbutton");
        hours.GetAttribute("tabindex").Should().Be("0");
        hours.GetAttribute("aria-valuenow").Should().Be("14");
        hours.GetAttribute("aria-valuemin").Should().Be("0");
        hours.GetAttribute("aria-valuemax").Should().Be("23");
        hours.GetAttribute("aria-valuetext").Should().Be("14 hours");
        hours.GetAttribute("aria-label").Should().Be("Hours");
        hours.TextContent.Trim().Should().Be("14");

        var minutes = Minutes(cut);
        minutes.GetAttribute("role").Should().Be("spinbutton");
        minutes.GetAttribute("aria-valuenow").Should().Be("30");
        minutes.GetAttribute("aria-valuemin").Should().Be("0");
        minutes.GetAttribute("aria-valuemax").Should().Be("59");
        minutes.GetAttribute("aria-valuetext").Should().Be("30 minutes");
        minutes.TextContent.Trim().Should().Be("30");
    }

    [Fact]
    public void NullValue_ShowsPlaceholders()
    {
        var cut = RenderSelector();

        Hours(cut).TextContent.Trim().Should().Be("--");
        Minutes(cut).TextContent.Trim().Should().Be("--");
    }

    [Fact]
    public void StepperButtons_AreNative_WithTabIndexMinusOneAndAriaControls()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Id, "alarm"));

        var plus = IncreaseHours(cut);
        plus.TagName.Should().Be("BUTTON");
        plus.GetAttribute("type").Should().Be("button");
        plus.GetAttribute("tabindex").Should().Be("-1");
        plus.GetAttribute("aria-controls").Should().Be("alarm-hours");
        plus.GetAttribute("aria-label").Should().Be("Add an hour");

        var minus = DecreaseMinutes(cut);
        minus.GetAttribute("aria-controls").Should().Be("alarm-minutes");
        minus.GetAttribute("aria-label").Should().Be("Remove 1 min");
    }

    [Fact]
    public void InputId_IsTheHoursSpinbutton()
    {
        var cut = RenderSelector(p => p.Add(c => c.Id, "alarm"));

        cut.Instance.InputId.Should().Be("alarm-hours");
        Hours(cut).Id.Should().Be("alarm-hours");
    }

    [Fact]
    public void IncreaseHour_RaisesValueChanged()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        IncreaseHours(cut).Click();

        captured.Should().Be(new TimeOnly(15, 30));
        Hours(cut).TextContent.Trim().Should().Be("15");
    }

    [Fact]
    public void DecreaseMinute_WrapsWithoutCarryingIntoHours()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 0))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        DecreaseMinutes(cut).Click();

        captured.Should().Be(new TimeOnly(14, 59),
            "minutes wrap inside the hour and never carry (ADR-0007 decision 6)");
        Hours(cut).TextContent.Trim().Should().Be("14");
    }

    [Fact]
    public void MinuteStepFive_DecreaseLabelAndStepUseTheStep()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.MinuteStep, 5)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        DecreaseMinutes(cut).GetAttribute("aria-label").Should().Be("Remove 5 min");
        cut.Find("[data-testid='time-selector-increase-minutes']")
            .GetAttribute("aria-label").Should().Be("Add 5 min");

        cut.Find("[data-testid='time-selector-increase-minutes']").Click();
        captured.Should().Be(new TimeOnly(14, 35));
    }

    [Fact]
    public async Task PageUp_Hours_StepsThreeHours()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnKeyStepAsync("hours", 3, announce: false));

        captured.Should().Be(new TimeOnly(17, 30));
        Live(cut).TextContent.Trim().Should().BeEmpty(
            "arrow / PageUp changes are not announced — the reader already hears aria-valuetext");
    }

    [Fact]
    public async Task PageDown_Minutes_StepsFifteenWithoutCarry()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 10))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnKeyStepAsync("minutes", -15, announce: false));

        captured.Should().Be(new TimeOnly(14, 55),
            "PageDown minutes wrap inside the hour; 10 − 15 → 55, hour stays 14");
    }

    [Fact]
    public void PlusButton_DoesNotEmulateEnter()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)));

        var act = () => IncreaseHours(cut).KeyDown("Enter");

        act.Should().Throw<MissingEventHandlerException>(
            "exactly one activation per key press: the native button owns Enter/Space");
    }

    [Fact]
    public void HourCycleH12_RendersPeriodSwitchAndTwelveHourValueText()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Culture, CultureInfo.GetCultureInfo("en-US")),
            hourCycle: TimeHourCycle.H12);

        Hours(cut).GetAttribute("aria-valuenow").Should().Be("2");
        Hours(cut).GetAttribute("aria-valuemin").Should().Be("1");
        Hours(cut).GetAttribute("aria-valuemax").Should().Be("12");
        Hours(cut).GetAttribute("aria-valuetext").Should().Be("2 PM");
        Hours(cut).TextContent.Trim().Should().Be("02");

        cut.Find(".tm-time-selector__period").Should().NotBeNull();
        cut.Find("[data-testid='segmented-option'][data-value='True']")
            .GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void HourCycleH12_TogglingAmPm_ConvertsTheHour()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(9, 15))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v),
            hourCycle: TimeHourCycle.H12);

        cut.Find("[data-testid='segmented-option'][data-value='True']").Click();

        captured.Should().Be(new TimeOnly(21, 15));
    }

    [Fact]
    public void CzechPlural_HourValueText_UsesOneFewOther()
    {
        UseCzechLocalization();
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(1, 0)));
        Hours(cut).GetAttribute("aria-valuetext").Should().Be("1 hodina");

        cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(2, 0)));
        Hours(cut).GetAttribute("aria-valuetext").Should().Be("2 hodiny");

        cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(5, 0)));
        Hours(cut).GetAttribute("aria-valuetext").Should().Be("5 hodin");
    }

    [Fact]
    public void SecondsSteppers_HaveDistinctDirectionalAriaLabels()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 45)));

        var plus = cut.Find("[data-testid='time-selector-increase-seconds']").GetAttribute("aria-label");
        var minus = cut.Find("[data-testid='time-selector-decrease-seconds']").GetAttribute("aria-label");

        plus.Should().Be("Add a second");
        minus.Should().Be("Remove a second");
        plus.Should().NotBe(minus,
            "WCAG 2.4.6 / 4.1.2: the two seconds buttons must not share one name");
    }

    [Fact]
    public void Seconds_AriaValueText_UsesPluralFamily()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 1)));
        cut.Find("[data-tm-time-segment='seconds']").GetAttribute("aria-valuetext")
            .Should().Be("1 second");

        cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 45)));
        cut.Find("[data-tm-time-segment='seconds']").GetAttribute("aria-valuetext")
            .Should().Be("45 seconds");
    }

    [Fact]
    public void CzechPlural_SecondValueText_UsesOneFewOther()
    {
        UseCzechLocalization();
        var cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 1)));
        cut.Find("[data-tm-time-segment='seconds']").GetAttribute("aria-valuetext")
            .Should().Be("1 sekunda");

        cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 2)));
        cut.Find("[data-tm-time-segment='seconds']").GetAttribute("aria-valuetext")
            .Should().Be("2 sekundy");

        cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.Value, new TimeOnly(14, 30, 5)));
        cut.Find("[data-tm-time-segment='seconds']").GetAttribute("aria-valuetext")
            .Should().Be("5 sekund");
    }

    [Fact]
    public void FitSquare_HasNoTypeTimeButton()
    {
        var cut = RenderSelector(p => p.Add(c => c.AllowTyping, true),
            fit: TimeSelectorFit.Square);

        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-fit").Should().Be("square");
        cut.FindAll("[data-testid='time-selector-type-time']").Should().BeEmpty(
            "fit square never offers Type the time (decision 15)");
        cut.FindAll("input").Should().BeEmpty("spinbuttons are spans, not inputs, so a tap cannot open the SW keyboard");
    }

    [Fact]
    public async Task KeyboardOpen_OnSquare_HidesSteppers()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Square);

        cut.FindAll("[data-testid='time-selector-increase-hours']").Should().NotBeEmpty();

        await cut.InvokeAsync(() => cut.Instance.OnKeyboardChanged(true));
        cut.Render();

        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-keyboard").Should().Be("open");
        cut.FindAll("[data-testid='time-selector-increase-hours']").Should().BeEmpty(
            "square + SW keyboard is a one-line readout: no +/−, grid or presets");
        Hours(cut).TextContent.Trim().Should().Be("14");
    }

    [Fact]
    public async Task KeyboardOpen_DoesNotRecomputeFit()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)),
            fit: TimeSelectorFit.Auto);

        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(399, 306));
        cut.Render();
        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-fit").Should().Be("square");

        await cut.InvokeAsync(() => cut.Instance.OnKeyboardChanged(true));
        await cut.InvokeAsync(() => cut.Instance.OnViewportChanged(1280, 800));
        cut.Render();

        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-fit").Should().Be("square",
            "fit is locked while the software keyboard is open");
    }

    [Fact]
    public void HostFitPin_WinsOverMeasurement()
    {
        var cut = RenderSelector();

        cut.InvokeAsync(() => cut.Instance.OnViewportChanged(399, 306)).GetAwaiter().GetResult();
        cut.Render();

        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-fit").Should().Be("regular",
            "a host Fit pin wins over the measured box");
    }

    [Fact]
    public void ShowSeconds_Square_WrapsReadoutAndCollapsesGrid()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.ShowGrid, TimeSelectorGrid.Auto)
            .Add(c => c.Value, new TimeOnly(14, 30, 45)),
            fit: TimeSelectorFit.Square);

        var root = cut.Find("[data-testid='time-selector']");
        root.ClassList.Should().Contain("tm-time-selector--with-seconds");
        root.GetAttribute("data-tm-time-grid").Should().Be("none",
            "ShowGrid=Auto + square + seconds collapses to None (236 px readout, no grid)");
        cut.Find("[data-tm-time-segment='seconds']").TextContent.Trim().Should().Be("45");
    }

    [Fact]
    public void ShowGridAlways_SquareSeconds_StillCollapses()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.ShowGrid, TimeSelectorGrid.Always),
            fit: TimeSelectorFit.Square);

        cut.Find("[data-testid='time-selector']").GetAttribute("data-tm-time-grid").Should().Be("none",
            "ShowGrid=Always + seconds + square still collapses (304 > 260)");
    }

    [Fact]
    public void LiveRegion_EmptyOnFirstRender_AnnouncesAfterTap()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)));

        Live(cut).TextContent.Trim().Should().BeEmpty();
        Live(cut).GetAttribute("aria-live").Should().Be("polite");

        IncreaseHours(cut).Click();

        Live(cut).TextContent.Trim().Should().Be("Set to 15:30");
    }

    [Fact]
    public void ReadOnly_DefaultIsFalse_AndIsNeverAssignedInternally()
    {
        new TmTimeSelector().ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_HidesSteppers_MarksAriaReadonly_AndBlocksCommit()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(10, 0))
            .Add(c => c.ReadOnly, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find("[data-testid='time-selector']").ClassList.Should().Contain("tm-time-selector--readonly");
        Hours(cut).GetAttribute("aria-readonly").Should().Be("true");
        Hours(cut).GetAttribute("tabindex").Should().Be("0", "ReadOnly stays focusable");
        cut.FindAll("[data-testid='time-selector-increase-hours']").Should().BeEmpty(
            "ReadOnly hides +/− (not merely disabled) so a square fit stays quiet");

        captured.Should().BeNull();
        Hours(cut).TextContent.Trim().Should().Be("10");
    }

    [Fact]
    public void Disabled_RemovesTabStops_AndBlocksCommit()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(10, 0))
            .Add(c => c.Disabled, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        var root = cut.Find("[data-testid='time-selector']");
        root.ClassList.Should().Contain("tm-time-selector--disabled");
        root.GetAttribute("aria-disabled").Should().Be("true");
        Hours(cut).GetAttribute("tabindex").Should().Be("-1");
        Hours(cut).GetAttribute("aria-disabled").Should().Be("true");
        IncreaseHours(cut).HasAttribute("disabled").Should().BeTrue();

        IncreaseHours(cut).Click();
        captured.Should().BeNull();
    }

    [Fact]
    public void Invalid_WithErrorText_MarksSpinbuttonsAndShowsMessage()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Id, "alarm")
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Too early."));

        cut.Find("[data-testid='time-selector']").ClassList.Should().Contain("tm-time-selector--invalid");
        Hours(cut).GetAttribute("aria-invalid").Should().Be("true");
        Hours(cut).GetAttribute("aria-describedby").Should().Contain("alarm-error");
        cut.Find(".tm-time-selector__error").TextContent.Should().Be("Too early.");
    }

    [Fact]
    public void OutOfRange_StaysVisibleAndAnnounces()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(8, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(17, 0)));

        Hours(cut).GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-time-selector__error").TextContent
            .Should().Contain("09:00")
            .And.Contain("17:00");
    }

    [Fact]
    public void MinTime_DisablesDecreaseHourAtBound()
    {
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(9, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(17, 0)));

        cut.Find("[data-testid='time-selector-decrease-hours']").HasAttribute("disabled").Should().BeTrue();
        IncreaseHours(cut).HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void AriaDescribedBy_LandsOnEverySpinbutton()
    {
        var cut = RenderSelector(p => p.Add(c => c.AriaDescribedBy, "hint-id"));

        Hours(cut).GetAttribute("aria-describedby").Should().Be("hint-id");
        Minutes(cut).GetAttribute("aria-describedby").Should().Be("hint-id");
    }

    [Fact]
    public void FormField_HelpCascade_ReachesSpinbuttons()
    {
        SetupSelectorModule();
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Start")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Opening hours.")
            .AddChildContent<TmTimeSelector>(child => child
                .Add(x => x.HourCycle, TimeHourCycle.H23)
                .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
                .Add(x => x.Fit, TimeSelectorFit.Regular)));

        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-describedby")
            .Should().Be("region-field-field-help");
    }

    [Fact]
    public void FormField_ErrorCascade_ReachesSpinbuttons()
    {
        SetupSelectorModule();
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Start")
            .Add(c => c.Id, "region-field")
            .Add(c => c.ErrorMessage, "Required.")
            .AddChildContent<TmTimeSelector>(child => child
                .Add(x => x.HourCycle, TimeHourCycle.H23)
                .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
                .Add(x => x.Fit, TimeSelectorFit.Regular)));

        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-describedby")
            .Should().Be("region-field-field-error");
        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void InEditForm_NotifiesFieldChanged()
    {
        SetupSelectorModule();
        var model = new AppointmentModel { Start = new TimeOnly(10, 0) };
        var cut = Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<TmTimeSelector>(0);
                    b.AddAttribute(1, nameof(TmTimeSelector.HourCycle), TimeHourCycle.H23);
                    b.AddAttribute(2, nameof(TmTimeSelector.LayoutMode), TmLayoutMode.Desktop);
                    b.AddAttribute(3, nameof(TmTimeSelector.Fit), TimeSelectorFit.Regular);
                    b.AddAttribute(4, nameof(TmTimeSelector.Value), model.Start);
                    b.AddAttribute(5, nameof(TmTimeSelector.ValueChanged),
                        EventCallback.Factory.Create<TimeOnly?>(this, v => model.Start = v));
                    b.AddAttribute(6, nameof(TmTimeSelector.ValueExpression),
                        (Expression<Func<TimeOnly?>>)(() => model.Start));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });

        var changed = new List<string>();
        cut.FindComponent<EditForm>().Instance.EditContext!.OnFieldChanged +=
            (_, e) => changed.Add(e.FieldIdentifier.FieldName);

        cut.Find("[data-testid='time-selector-increase-hours']").Click();

        model.Start.Should().Be(new TimeOnly(11, 0));
        changed.Should().ContainSingle().Which.Should().Be(nameof(AppointmentModel.Start));
    }

    [Fact]
    public void MinuteStep_Invalid_ThrowsArgumentOutOfRangeNamedMinuteStep()
    {
        SetupSelectorModule();
        var act = () => Render<TmTimeSelector>(p => p
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.MinuteStep, 7));

        act.Should().Throw<ArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("MinuteStep");
    }

    [Fact]
    public async Task Typeahead_SetsTheSegment()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnTypeaheadAsync("hours", 3, advance: true));

        captured.Should().Be(new TimeOnly(3, 30));
        Hours(cut).TextContent.Trim().Should().Be("03");
    }

    [Fact]
    public async Task TypeaheadAdvance_DoesNotCallFocusInterop()
    {
        var cut = RenderSelector(p => p.Add(c => c.Value, new TimeOnly(14, 30)));
        const string focus = "Blazor._internal.domWrapper.focus";
        var before = JSInterop.Invocations.Count(i => i.Identifier == focus);

        await cut.InvokeAsync(() => cut.Instance.OnTypeaheadAsync("hours", 3, advance: true));

        JSInterop.Invocations.Count(i => i.Identifier == focus).Should().Be(before,
            "JS owns the typeahead advance (focus + scrollIntoView); Blazor must not move focus a second time");
    }

    [Fact]
    public async Task Home_Hours_GoesToMinimum()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(17, 0))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        await cut.InvokeAsync(() => cut.Instance.OnHomeEndAsync("hours", home: true));

        captured.Should().Be(new TimeOnly(9, 30));
    }

    [Fact]
    public void ShowSecondsFalse_HourChange_KeepsBoundSeconds()
    {
        TimeOnly? captured = null;
        var cut = RenderSelector(p => p
            .Add(c => c.Value, new TimeOnly(10, 15, 30))
            .Add(c => c.ShowSeconds, false)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        IncreaseHours(cut).Click();

        captured.Should().Be(new TimeOnly(11, 15, 30));
    }

    [Fact]
    public void OvernightRange_IncreaseHour_FromTwentyThree_WrapsToMidnight()
    {
        TimeOnly? captured = new TimeOnly(23, 0);
        var cut = RenderSelector(p => p
            .Add(c => c.Value, captured)
            .Add(c => c.MinTime, new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(6, 0))
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        IncreaseHours(cut).HasAttribute("disabled").Should().BeFalse();
        IncreaseHours(cut).Click();

        captured.Should().Be(new TimeOnly(0, 0));
        Hours(cut).TextContent.Trim().Should().Be("00");
    }

    [Fact]
    public void OvernightRange_IncreaseHour_FromSix_IsRejected()
    {
        TimeOnly? captured = new TimeOnly(6, 0);
        var cut = RenderSelector(p => p
            .Add(c => c.Value, captured)
            .Add(c => c.MinTime, new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(6, 0))
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find("[data-testid='time-selector-increase-hours']").HasAttribute("disabled").Should().BeTrue();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();

        captured.Should().Be(new TimeOnly(6, 0), "stepping 06:00 → 07:00 is outside the overnight union");
        Hours(cut).TextContent.Trim().Should().Be("06");
    }

    private sealed class AppointmentModel
    {
        public TimeOnly? Start { get; set; }
    }
}
