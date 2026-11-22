using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Plan 1c95309c Fáze 6 — EnforceStep: an off-step bound value is marked invalid and never rewritten.
/// </summary>
public class TmTimeSelectorEnforceStepTests : LocalizationTestBase
{
    private const string ModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";

    private void SetupSelectorModule()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        module.SetupVoid("update", _ => true).SetVoidResult();
    }

    [Fact]
    public void OffStepBinding_MarksInvalid_DoesNotRewrite()
    {
        SetupSelectorModule();
        TimeOnly? value = new TimeOnly(14, 7);
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.EnforceStep, true));

        value.Should().Be(new TimeOnly(14, 7));
        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find("[data-tm-time-segment='minutes']").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-time-selector__error").TextContent.Should().Be("Use a 15-minute step");
        cut.Find("[data-tm-time-segment='minutes']").TextContent.Trim().Should().Be("07");
    }

    [Fact]
    public void OffStep_PlusSnapsUp_MinusSnapsDown()
    {
        SetupSelectorModule();
        TimeOnly? plus = new TimeOnly(14, 7);
        var up = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, plus)
            .Add(c => c.ValueChanged, (TimeOnly? v) => plus = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.EnforceStep, true));

        up.Find("[data-testid='time-selector-increase-minutes']").Click();
        plus.Should().Be(new TimeOnly(14, 15));

        SetupSelectorModule();
        TimeOnly? minus = new TimeOnly(14, 7);
        var down = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, minus)
            .Add(c => c.ValueChanged, (TimeOnly? v) => minus = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.EnforceStep, true));

        down.Find("[data-testid='time-selector-decrease-minutes']").Click();
        minus.Should().Be(new TimeOnly(14, 0));
    }

    [Fact]
    public async Task OffStep_TypedInput_SnapsOntoStep()
    {
        SetupSelectorModule();
        TimeOnly? value = new TimeOnly(14, 7);
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.EnforceStep, true));

        await cut.InvokeAsync(() => cut.Instance.OnTypeaheadAsync("minutes", 7, advance: true));
        value.Should().Be(new TimeOnly(14, 15));
    }

    [Fact]
    public void EnforceStepFalse_LeavesOffStepValue()
    {
        SetupSelectorModule();
        TimeOnly? value = new TimeOnly(14, 7);
        var cut = Render<TmTimeSelector>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Fit, TimeSelectorFit.Regular)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.EnforceStep, false));

        cut.FindAll(".tm-time-selector__error").Should().BeEmpty();
        cut.Find("[data-tm-time-segment='hours']").GetAttribute("aria-invalid").Should().BeNull();
        cut.Find("[data-testid='time-selector-increase-minutes']").Click();
        value.Should().Be(new TimeOnly(14, 22));
    }
}
