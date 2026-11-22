using System.Reflection;
using System.Runtime.CompilerServices;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// Plan 1c95309c Fáze 6 — CommitMode draft/Done/discard and ReadOnly never opens.
/// </summary>
public class TmTimePickerCommitTests : LocalizationTestBase
{
    private const string OverlayModulePath = "./_content/Tempo.Blazor/js/overlay.js";
    private const string SelectorModulePath = "./_content/Tempo.Blazor/js/tm-time-selector.js";

    private void SetupOverlayAndSelectorModules()
    {
        var overlay = JSInterop.SetupModule(OverlayModulePath);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();

        var selector = JSInterop.SetupModule(SelectorModulePath);
        selector.SetupVoid("attach", _ => true).SetVoidResult();
        selector.SetupVoid("detach", _ => true).SetVoidResult();
        selector.SetupVoid("update", _ => true).SetVoidResult();
    }

    [Fact]
    public void Popup_Live_SelectorChange_RaisesValueChangedImmediately()
    {
        SetupOverlayAndSelectorModules();
        TimeOnly? captured = new TimeOnly(14, 30);
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.Value, captured)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<TimeOnly?>(this, v => captured = v))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-picker__trigger").Click();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();

        captured.Should().Be(new TimeOnly(15, 30));
    }

    [Fact]
    public void Popup_OnConfirm_KeepsDraft_UntilDone()
    {
        SetupOverlayAndSelectorModules();
        TimeOnly? captured = new TimeOnly(14, 30);
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.CommitMode, TimeCommitMode.OnConfirm)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.Value, captured)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<TimeOnly?>(this, v => captured = v))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-picker__trigger").Click();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();

        captured.Should().Be(new TimeOnly(14, 30), "OnConfirm holds a draft until Done");
        cut.FindComponent<TmTimeSelector>().Instance.Value.Should().Be(new TimeOnly(15, 30));

        cut.Find(".tm-time-picker__done").Click();

        captured.Should().Be(new TimeOnly(15, 30));
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        ConfirmingFlag(cut.Instance).Should().BeFalse(
            "popover Done must clear _confirming because the programmatic close does not raise IsOpenChanged");
    }

    [Fact]
    public void CloseWithoutCommitAsync_IsNotAnAsyncStateMachine()
    {
        var method = typeof(TmTimePicker).GetMethod(
            "CloseWithoutCommitAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task));
        method.GetCustomAttribute<AsyncStateMachineAttribute>().Should().BeNull(
            "returning Task.CompletedTask avoids an async state machine for a sync close");
    }

    [Fact]
    public void Auto_MobileSheet_DoneCommits_BackdropDiscards()
    {
        SetupOverlayAndSelectorModules();
        TimeOnly? captured = new TimeOnly(14, 30);
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);
        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmTimePicker>(0);
                b.AddAttribute(1, nameof(TmTimePicker.Value), captured);
                b.AddAttribute(2, nameof(TmTimePicker.ValueChanged),
                    EventCallback.Factory.Create<TimeOnly?>(this, v => captured = v));
                b.AddAttribute(3, nameof(TmTimePicker.Label), "Meeting");
                b.AddAttribute(4, nameof(TmTimePicker.HourCycle), TimeHourCycle.H23);
                b.AddAttribute(5, nameof(TmTimePicker.Presentation), TimePickerPresentation.Auto);
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Find(".tm-time-picker__trigger").Click();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();
        captured.Should().Be(new TimeOnly(14, 30), "sheet Auto is OnConfirm");

        cut.Find(".tm-overlay-panel-sheet__done").Click();
        captured.Should().Be(new TimeOnly(15, 30));
    }

    [Fact]
    public void Auto_MobileSheet_Backdrop_DiscardsDraft()
    {
        SetupOverlayAndSelectorModules();
        TimeOnly? captured = new TimeOnly(14, 30);
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Value, captured)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<TimeOnly?>(this, v => captured = v))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-picker__trigger").Click();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();
        captured.Should().Be(new TimeOnly(14, 30));

        cut.Find(".tm-drawer__overlay").Click();

        captured.Should().Be(new TimeOnly(14, 30), "close without Done discards the draft");
        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
    }

    [Fact]
    public void ReadOnly_DoesNotOpen()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.ReadOnly, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Label, "Meeting"));

        cut.Find(".tm-time-picker-body").GetAttribute("aria-readonly").Should().Be("true");
        cut.Find(".tm-time-picker__trigger").Click();

        cut.FindComponents<TmOverlayPanel>().Should().BeEmpty();
        cut.Find(".tm-time-picker__trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    private static bool ConfirmingFlag(TmTimePicker instance)
    {
        var field = typeof(TmTimePicker).GetField(
            "_confirming",
            BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (bool)field!.GetValue(instance)!;
    }
}
