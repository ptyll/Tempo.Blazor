using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmTimePickerTests : LocalizationTestBase
{
    [Fact]
    public void TimePicker_RendersHoursAndMinutes()
    {
        var cut = Render<TmTimePicker>();

        cut.FindAll(".tm-time-seg--hours").Should().HaveCount(1);
        cut.FindAll(".tm-time-seg--minutes").Should().HaveCount(1);
        cut.FindAll(".tm-time-seg--seconds").Should().BeEmpty();
    }

    [Fact]
    public void TimePicker_WithShowSeconds_RendersSecondsField()
    {
        var cut = Render<TmTimePicker>(p => p.Add(c => c.ShowSeconds, true));

        cut.FindAll(".tm-time-seg--seconds").Should().HaveCount(1);
    }

    [Fact]
    public void TimePicker_Value_Binding_Works()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        // Hours field should show 14
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
    }

    [Fact]
    public void TimePicker_InvalidTime_DoesNotUpdate()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("99");

        captured.Should().BeNull();
    }

    [Fact]
    public void TimePicker_MinTime_DisablesEarlierValues()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(8, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0)));

        // The invalid state paints the --invalid class on the wrapper. N148: aria-disabled is no
        // longer emitted (the segments stay editable, so "disabled" was the wrong signal) — the
        // selector is carried by the class only.
        cut.FindAll(".tm-time-picker--invalid, [aria-disabled='true']")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_MaxTime_DisablesLaterValues()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(18, 0)));

        // The invalid state paints the --invalid class on the wrapper. N148: aria-disabled is no
        // longer emitted (the segments stay editable, so "disabled" was the wrong signal) — the
        // selector is carried by the class only.
        cut.FindAll(".tm-time-picker--invalid, [aria-disabled='true']")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_OutOfRange_SetsAriaInvalidOnSegments()
    {
        // N148: an out-of-range value is announced on the editable segments via aria-invalid,
        // not by disabling the group (segments stay editable).
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(8, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0)));

        cut.Find(".tm-time-seg--hours").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-time-seg--minutes").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void TimePicker_InRange_HasNoAriaInvalidOnSegments()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,   new TimeOnly(10, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0)));

        cut.Find(".tm-time-seg--hours").HasAttribute("aria-invalid").Should().BeFalse();
        cut.Find(".tm-time-seg--minutes").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void TimePicker_Placeholder_UsesLocalizer()
    {
        UseCzechLocalization();
        var cut = Render<TmTimePicker>();

        // placeholder attribute on the wrapper or an aria-placeholder
        cut.Markup.Should().Contain("HH:mm");
    }

    [Fact]
    public void TimePicker_ClearButton_ClearsValue()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,        new TimeOnly(10, 0))
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public void TimePicker_HideClear_DefaultsToFalse_AndKeepsClearButton()
    {
        new TmTimePicker().HideClear.Should().BeFalse();

        var cut = Render<TmTimePicker>(p => p.Add(c => c.Value, new TimeOnly(10, 0)));

        cut.FindAll(".tm-picker-clear").Should().HaveCount(1);
    }

    [Fact]
    public void TimePicker_HideClear_True_RemovesClearButton_ButKeepsValueEditable()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,        new TimeOnly(10, 0))
            .Add(c => c.HideClear,    true)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.FindAll(".tm-picker-clear").Should().BeEmpty();
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("10");

        cut.Find(".tm-time-seg--hours").Change("11");

        captured.Should().Be(new TimeOnly(11, 0));
    }

    [Fact]
    public void TimePicker_HideClear_False_DoesNotRenderClearWithoutValueOrWhenDisabled()
    {
        Render<TmTimePicker>().FindAll(".tm-picker-clear").Should().BeEmpty();

        Render<TmTimePicker>(p => p
            .Add(c => c.Value,    new TimeOnly(10, 0))
            .Add(c => c.Disabled, true))
            .FindAll(".tm-picker-clear").Should().BeEmpty();
    }

    [Fact]
    public void TimePicker_Required_DoesNotHideClearImplicitly()
    {
        // Parity with TmDatePicker / TmDateTimePicker: Required only marks the label. Hiding the
        // clear button is the explicit HideClear opt-in, so a required field can still be emptied
        // (and then reports its own validation error) unless the host asks otherwise.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value,    new TimeOnly(10, 0))
            .Add(c => c.Required, true));

        cut.FindAll(".tm-picker-clear").Should().HaveCount(1);
    }

    [Fact]
    public void TimePicker_Disabled_RendersDisabled()
    {
        var cut = Render<TmTimePicker>(p => p.Add(c => c.Disabled, true));

        cut.FindAll("input[disabled]").Should().NotBeEmpty();
    }

    [Fact]
    public void TimePicker_Required_AddsRequiredMarkerClassToLabel()
    {
        // N148: Required is carried by the same tm-input-label-required marker class as
        // TmDatePicker — not by a data-required attribute no assistive tech consumes.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Label,    "Start")
            .Add(c => c.Required, true));

        cut.Find(".tm-picker-label").ClassList.Should().Contain("tm-input-label-required");
    }

    // ── Fáze 6: default Presentation=Segments keeps today's DOM ──────────────

    [Fact]
    public void TimePicker_Default_KeepsSegmentsDom_WithoutPopupChrome()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Label, "Meeting")
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-picker").Should().NotBeNull();
        cut.Find(".tm-time-picker-body").GetAttribute("role").Should().Be("group");
        cut.FindAll(".tm-time-seg").Should().HaveCount(2);
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
        cut.FindAll("[aria-haspopup]").Should().BeEmpty();
        cut.Markup.Should().NotContain("tm-overlay-panel");
        cut.Markup.Should().NotContain("tm-time-selector");
    }

    [Fact]
    public void TimePicker_AllowOvernightRange_ForwardsToTimeInput()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(23, 30))
            .Add(c => c.MinTime, new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(6, 0))
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.FindComponent<TmTimeInput>().Instance.AllowOvernightRange.Should().BeTrue();
        cut.FindAll(".tm-time-picker--invalid").Should().BeEmpty();
        cut.Find(".tm-time-seg--hours").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void TimePicker_Overnight_TwentyThreeThirty_IsInRange()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(23, 30))
            .Add(c => c.MinTime, new TimeOnly(22, 0))
            .Add(c => c.MaxTime, new TimeOnly(6, 0))
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.FindAll(".tm-time-picker--invalid").Should().BeEmpty(
            "IsOutOfRange must use IsInRange so 23:30 stays valid in 22:00–06:00");
    }

    // ── Fáze 4c (plan b75365fb): a locked overlay trigger is a BUTTON that cannot open anything,
    //    so it must not advertise a popup — the same contract the date pickers got in fáze 4
    //    (decision READONLY-BUTTON-TRIGGER-ARIA-DISABLED). Review round: where the segments
    //    render, the locked trigger is not rendered at all (the segments carry the value and the
    //    lock); on a phone it stays as the value's carrier, without the opening icon. ──────────

    [Fact]
    public void TimePicker_Popup_ReadOnly_RendersNoTrigger_SegmentsCarryTheLock()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.AriaDescribedBy, "lock-reason"));

        cut.FindAll(".tm-time-picker__trigger").Should().BeEmpty(
            "where the segments render, a button that opens nothing would be a dead tab stop — " +
            "the read-only segments carry the value and the lock");

        var hours = cut.Find(".tm-time-seg--hours");
        hours.HasAttribute("readonly").Should().BeTrue("the segments are the read-only surface");
        hours.GetAttribute("aria-readonly").Should().Be("true");
        hours.GetAttribute("aria-describedby").Should().Contain("lock-reason",
            "AriaDescribedBy reaches the segments, so a host can describe WHY the field is locked");
    }

    [Fact]
    public void TimePicker_Auto_ReadOnly_Desktop_RendersNoTrigger()
    {
        // On a desktop viewport Auto resolves to the popup presentation — segments render, so
        // the locked trigger is gone there too.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-time-picker__trigger").Should().BeEmpty();
        cut.FindAll(".tm-time-seg").Should().NotBeEmpty(
            "the read-only segments carry the value and the lock");
    }

    [Fact]
    public void TimePicker_Auto_Mobile_ReadOnly_TriggerCarriesTheValue_AnnouncesTheLock_NotAPopup()
    {
        // On a phone Auto hides the segments, so the locked trigger STAYS as the value's
        // carrier — without the opening icon, with the lock announced and the description linked.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.AriaDescribedBy, "lock-reason"));

        cut.FindAll(".tm-time-seg").Should().BeEmpty("Auto on a phone hides the segments");

        var trigger = cut.Find(".tm-time-picker__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("false",
            "a locked trigger has no popup to advertise");
        trigger.HasAttribute("aria-expanded").Should().BeFalse(
            "aria-expanded on a button that opens nothing announces a collapsed expandable control");
        trigger.HasAttribute("aria-controls").Should().BeFalse(
            "there is no overlay to control while the picker is locked");
        trigger.GetAttribute("aria-disabled").Should().Be("true",
            "the lock is announced as disabled — aria-readonly is not valid on role=button");
        trigger.HasAttribute("aria-readonly").Should().BeFalse(
            "aria-readonly is not a supported attribute on a native <button>");
        trigger.HasAttribute("disabled").Should().BeFalse(
            "aria-disabled names the state; the trigger stays focusable and readable");
        trigger.HasAttribute("tabindex").Should().BeFalse("the locked trigger keeps the natural tab stop");
        trigger.QuerySelector(".tm-time-picker__icon").Should().BeNull(
            "no opening icon on the locked trigger — nothing advertises a popup that can never open");
        trigger.QuerySelector(".tm-time-picker__value")!.TextContent.Should().Contain("14:30",
            "the phone trigger carries the value");
        trigger.GetAttribute("aria-describedby").Should().Contain("lock-reason",
            "AriaDescribedBy reaches the locked trigger");

        // Enter/Space on a native <button> produce a click in the browser — the guarded click is
        // the whole activation path (real Enter/Space coverage lives in the WASM E2E).
        trigger.Click();
        cut.FindAll(".tm-time-picker__popup").Should().BeEmpty();
    }

    [Fact]
    public void TimePicker_Popup_AriaDescribedBy_ReachesTheTrigger()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.AriaDescribedBy, "hint"));

        cut.Find(".tm-time-picker__trigger").GetAttribute("aria-describedby").Should().Be("hint");
    }

    [Fact]
    public void TimePicker_ReadOnly_MarksTheRootSoTheLockedTriggerPaintsTheReadOnlySurface()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.ReadOnly, true));

        cut.Find(".tm-time-picker").ClassList.Should().Contain("tm-time-picker--readonly");
    }

    [Fact]
    public void TimePicker_Popup_Editable_TriggerKeepsThePopupContract()
    {
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.Presentation, TimePickerPresentation.Popup));

        var trigger = cut.Find(".tm-time-picker__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("dialog");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-controls").Should().NotBeNullOrEmpty();
        trigger.HasAttribute("aria-disabled").Should().BeFalse();
        trigger.HasAttribute("aria-readonly").Should().BeFalse();

        trigger.Click();
        cut.Find(".tm-time-picker__trigger").GetAttribute("aria-expanded").Should().Be("true");
        cut.FindAll(".tm-time-picker__popup").Should().HaveCount(1);
    }

    [Fact]
    public void TimePicker_ReadOnly_GroupCarriesNoAriaReadonly()
    {
        // ARIA 1.2 supports aria-readonly on combobox/grid/gridcell/listbox/radiogroup/slider/
        // spinbutton/textbox — NOT on role=group (axe aria-allowed-attr flags it critical). The
        // lock is announced by the readonly segments and by aria-disabled on the overlay trigger.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.ReadOnly, true)
            .Add(c => c.Presentation, TimePickerPresentation.Popup));

        cut.Find(".tm-time-picker-body").HasAttribute("aria-readonly").Should().BeFalse();
    }

    [Fact]
    public void TimePicker_ConsumerAriaAttributes_WinOnTheGroup()
    {
        // Consumer aria-* attributes target the input group (the documented routing) and are
        // splatted AFTER the explicit attributes, so they keep winning over the defaults.
        var cut = Render<TmTimePicker>(p => p
            .Add(c => c.Label, "Meeting")
            .AddUnmatched("aria-labelledby", "host-label"));

        cut.Find(".tm-time-picker-body").GetAttribute("aria-labelledby").Should().Be("host-label",
            "a consumer attribute wins over the component default");
    }
}
