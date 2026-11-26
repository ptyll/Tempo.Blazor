using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDatePickerTests : LocalizationTestBase
{
    [Fact]
    public void DatePicker_ClosedByDefault_ShowsInputOnly()
    {
        var cut = Render<TmDatePicker>();

        cut.FindAll(".tm-calendar").Should().BeEmpty();
        cut.FindAll(".tm-date-picker-trigger").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_ClickInput_OpensCalendar()
    {
        var cut = Render<TmDatePicker>();

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-calendar").Should().HaveCount(1);
    }

    // TmDatePicker renders TmCalendarGrid, which shares the .tm-calendar markup the scheduler
    // date-popover axe scan used to exclude. Same contract as TmCalendarView: not an APG grid.
    [Fact]
    public void DatePicker_OpenCalendar_Has_No_Grid_Or_Row_Role()
    {
        var cut = Render<TmDatePicker>();

        cut.Find(".tm-date-picker-trigger").Click();

        var calendar = cut.Find(".tm-calendar");
        calendar.GetAttribute("role").Should().BeNull();
        calendar.QuerySelector(".tm-cal-nav")!.GetAttribute("role").Should().BeNull();
        calendar.QuerySelector(".tm-cal-day")!.TagName.Should().Be("BUTTON");
        cut.Markup.Should().NotContain("role=\"grid\"");
        cut.Markup.Should().NotContain("role=\"row\"");
        calendar.QuerySelector(".tm-cal-header-cell")!.HasAttribute("aria-label").Should().BeFalse();
        calendar.QuerySelector(".tm-cal-day")!.GetAttribute("aria-pressed").Should().Be("false");
    }

    [Fact]
    public void DatePicker_SelectDate_ClosesCalendarAndUpdatesValue()
    {
        DateOnly? captured = null;
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.ValueChanged, (DateOnly? v) => captured = v));

        cut.Find(".tm-date-picker-trigger").Click();
        // Click on a day button that is in current month (not disabled, not other-month)
        var dayBtn = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)").First();
        dayBtn.Click();

        cut.FindAll(".tm-calendar").Should().BeEmpty();
        captured.Should().NotBeNull();
    }

    [Fact]
    public void DatePicker_Value_Binding_Works()
    {
        var date = new DateOnly(2025, 6, 15);
        var cut  = Render<TmDatePicker>(p => p.Add(c => c.Value, date));

        cut.Find(".tm-date-picker-trigger").TextContent.Should().Contain("15");
    }

    [Fact]
    public void DatePicker_MinDate_DisablesEarlierDays()
    {
        var min = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 15);
        var cut = Render<TmDatePicker>(p => p.Add(c => c.MinDate, min));

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--disabled").Should().NotBeEmpty();
    }

    [Fact]
    public void DatePicker_MaxDate_DisablesLaterDays()
    {
        var max = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 5);
        var cut = Render<TmDatePicker>(p => p.Add(c => c.MaxDate, max));

        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--disabled").Should().NotBeEmpty();
    }

    [Fact]
    public void DatePicker_Navigate_PrevMonth_ChangesDisplayMonth()
    {
        var cut = Render<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        var titleBefore = cut.Find(".tm-cal-title").TextContent;
        cut.Find(".tm-cal-prev").Click();
        var titleAfter = cut.Find(".tm-cal-title").TextContent;

        titleAfter.Should().NotBe(titleBefore);
    }

    [Fact]
    public void DatePicker_Navigate_NextMonth_ChangesDisplayMonth()
    {
        var cut = Render<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        var titleBefore = cut.Find(".tm-cal-title").TextContent;
        cut.Find(".tm-cal-next").Click();
        var titleAfter = cut.Find(".tm-cal-title").TextContent;

        titleAfter.Should().NotBe(titleBefore);
    }

    [Fact]
    public void DatePicker_Today_Highlighted()
    {
        var cut = Render<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--today").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_DayButtonsExposeIsoDateForAutomation()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var cut = Render<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();

        cut.Find($".tm-cal-day[data-date='{today:yyyy-MM-dd}']")
            .TextContent.Should().Be(today.Day.ToString());
    }

    [Fact]
    public void DatePicker_SelectedDate_Highlighted()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var cut = Render<TmDatePicker>(p => p.Add(c => c.Value, today));
        cut.Find(".tm-date-picker-trigger").Click();

        cut.FindAll(".tm-cal-day--selected").Should().HaveCount(1);
    }

    [Fact]
    public void DatePicker_ClearButton_ClearsValue()
    {
        DateOnly? captured = null;
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value,        new DateOnly(2025, 1, 1))
            .Add(c => c.ValueChanged, (DateOnly? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public async Task DatePicker_Escape_ClosesCalendar()
    {
        var cut = Render<TmDatePicker>();
        cut.Find(".tm-date-picker-trigger").Click();
        cut.FindAll(".tm-calendar").Should().HaveCount(1);

        // overlay.js consumes Escape in the window capture phase in a real browser — the
        // component's dismissal path is this JSInvokable callback, not a keydown on the
        // wrapper (dead-branch sweep, N169 follow-up).
        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-calendar").Should().BeEmpty();
    }

    [Fact]
    public void DatePicker_Format_AppliedToDisplayValue()
    {
        var date = new DateOnly(2025, 3, 5);
        var cut  = Render<TmDatePicker>(p => p
            .Add(c => c.Value,      date)
            .Add(c => c.DateFormat, "yyyy-MM-dd"));

        cut.Find(".tm-date-picker-trigger").TextContent.Trim().Should().Be("2025-03-05");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void DatePicker_Required_HasNoAriaRequiredOnTrigger()
    {
        // N147: aria-required is NOT a permitted attribute on role=button (ARIA 1.2/HTML-AAM) —
        // requiredness is carried by the tm-input-label-required marker on the label and by form
        // validation, never by an attribute on the trigger.
        var cut = Render<TmDatePicker>(p => p.Add(c => c.Required, true));
        cut.Find(".tm-date-picker-trigger").HasAttribute("aria-required").Should().BeFalse();
    }

    [Fact]
    public void DatePicker_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Required, true));
        cut.Find(".tm-picker-label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void DatePicker_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmDatePicker>(p => p.Add(c => c.Label, "Occurred"));
        cut.Find(".tm-date-picker-trigger").HasAttribute("aria-required").Should().BeFalse();
        cut.Find(".tm-picker-label").ClassList.Should().NotContain("tm-input-label-required");
    }

    // ── F3 (carried from F2): the popup traps focus through TmFocusScope — the role=dialog
    //    popup re-gains the aria-modal it lost when the false promise was removed. ────────────

    [Fact]
    public void DatePicker_OpenCalendar_PopupTrapsFocus_WithModalDialogSemantics()
    {
        var cut = Render<TmDatePicker>(p => p.Add(c => c.Label, "Occurred"));

        cut.Find(".tm-date-picker-trigger").Click();

        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        scope.Instance.Active.Should().BeTrue();
        scope.Instance.Modal.Should().BeTrue();
        var root = scope.Find(".tm-focus-scope");
        root.GetAttribute("role").Should().Be("dialog");
        root.GetAttribute("aria-modal").Should().Be("true");
        root.GetAttribute("aria-labelledby").Should().Be(cut.Find(".tm-picker-label").Id);
        // The calendar still renders inside the popup.
        root.QuerySelector(".tm-calendar").Should().NotBeNull();
    }

    [Fact]
    public void DatePicker_ReadOnly_KeepsTriggerFocusable_DoesNotOpen()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value, new DateOnly(2026, 3, 15))
            .Add(c => c.ReadOnly, true));

        var trigger = cut.Find(".tm-date-picker-trigger");
        trigger.HasAttribute("disabled").Should().BeFalse();
        trigger.HasAttribute("tabindex").Should().BeFalse("the locked trigger keeps the natural tab stop");
        cut.Find(".tm-date-picker").ClassList.Should().Contain("tm-date-picker--readonly");
        cut.FindAll(".tm-picker-clear").Should().BeEmpty();

        trigger.Click();
        cut.FindAll(".tm-calendar").Should().BeEmpty();

        trigger.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-calendar").Should().BeEmpty();

        trigger.KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-calendar").Should().BeEmpty();

        trigger.KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        cut.FindAll(".tm-calendar").Should().BeEmpty();
    }

    // ── Fáze 4 (plan b75365fb): a locked trigger is a BUTTON that cannot open anything, so it must
    //    not advertise a popup. aria-readonly is not a supported attribute on role=button (ARIA 1.2),
    //    so the lock is named by aria-disabled while the trigger stays focusable and readable. ─────

    [Fact]
    public void DatePicker_ReadOnly_TriggerAnnouncesLockedState_NotAPopup()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value, new DateOnly(2026, 3, 15))
            .Add(c => c.ReadOnly, true));

        var trigger = cut.Find(".tm-date-picker-trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("false",
            "a locked trigger has no popup to advertise");
        trigger.HasAttribute("aria-expanded").Should().BeFalse(
            "aria-expanded on a non-popup button announces a collapsed expandable control");
        trigger.GetAttribute("aria-disabled").Should().Be("true",
            "the lock is announced as disabled — aria-readonly is not valid on role=button");
        trigger.HasAttribute("aria-readonly").Should().BeFalse(
            "aria-readonly is not a supported attribute on a native <button>");
        trigger.HasAttribute("disabled").Should().BeFalse(
            "aria-disabled names the state; the trigger stays focusable and readable");
    }

    [Fact]
    public void DatePicker_Editable_TriggerKeepsThePopupContract()
    {
        var cut = Render<TmDatePicker>(p => p.Add(c => c.Value, new DateOnly(2026, 3, 15)));

        var trigger = cut.Find(".tm-date-picker-trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("true");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.HasAttribute("aria-disabled").Should().BeFalse();
        trigger.HasAttribute("aria-readonly").Should().BeFalse();

        trigger.Click();
        cut.Find(".tm-date-picker-trigger").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void DatePicker_ReadOnly_ConsumerAriaAttributes_TakePrecedence()
    {
        // EmusCz worked around the missing lock announcement by passing the attributes from the
        // consumer side; that escape hatch must keep winning over the component defaults.
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value, new DateOnly(2026, 3, 15))
            .Add(c => c.ReadOnly, true)
            .AddUnmatched("aria-haspopup", "false")
            .AddUnmatched("aria-disabled", "false")
            .AddUnmatched("aria-expanded", "false"));

        var trigger = cut.Find(".tm-date-picker-trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("false");
        trigger.GetAttribute("aria-disabled").Should().Be("false",
            "a consumer attribute wins over the component default");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void DatePicker_ReadOnly_AriaDescribedBy_StillReachesTheTrigger()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.ReadOnly, true)
            .Add(c => c.AriaDescribedBy, "lock-reason"));

        cut.Find(".tm-date-picker-trigger").GetAttribute("aria-describedby")
            .Should().Be("lock-reason", "the host can describe WHY the field is locked");
    }

    [Fact]
    public void DatePicker_LabelTrailing_RendersAtTheEndOfTheLabelRow()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.LabelTrailing, "<span id='date-hint'>ISO</span>"));

        var row = cut.Find(".tm-input-label-row");
        row.QuerySelector("label")!.TextContent.Trim().Should().Be("Occurred");
        row.QuerySelector("#date-hint")!.TextContent.Should().Be("ISO");
    }

    [Fact]
    public void DatePicker_AriaDescribedBy_JoinsFormFieldCascade()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Id, "occurred-field")
            .Add(c => c.HelpText, "When it happened.")
            .AddChildContent<TmDatePicker>(child => child
                .Add(x => x.AriaDescribedBy, "extra-hint")));

        cut.Find(".tm-date-picker-trigger").GetAttribute("aria-describedby")
            .Should().Be("occurred-field-field-help extra-hint");
    }

    [Fact]
    public void DatePicker_AllowTypedDate_DefaultFalse_AndFailedParseKeepsValue()
    {
        DateOnly? captured = new DateOnly(2026, 1, 1);
        var closed = Render<TmDatePicker>(p => p.Add(c => c.Value, captured));
        closed.FindAll(".tm-date-picker-typed").Should().BeEmpty();

        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Value, captured)
            .Add(c => c.AllowTypedDate, true)
            .Add(c => c.ValueChanged, (DateOnly? v) => captured = v));

        var typed = cut.Find(".tm-date-picker-typed");
        typed.Change("not-a-date");

        captured.Should().Be(new DateOnly(2026, 1, 1), "a failed parse must not reset Value");
        typed.GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void DatePicker_AllowTypedDate_WithLabel_UsesLabelledBy_NotAriaLabel()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.AllowTypedDate, true));

        var typed = cut.Find(".tm-date-picker-typed");
        var labelId = cut.Find("label.tm-picker-label").GetAttribute("id");
        typed.GetAttribute("aria-labelledby").Should().Be(labelId);
        typed.HasAttribute("aria-label").Should().BeFalse(
            "aria-label is only the fallback when there is no visible Label");
    }

    [Fact]
    public void DatePicker_AllowTypedDate_WithoutLabel_FallsBackToAriaLabel()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(c => c.Placeholder, "Pick a day")
            .Add(c => c.AllowTypedDate, true));

        var typed = cut.Find(".tm-date-picker-typed");
        typed.HasAttribute("aria-labelledby").Should().BeFalse();
        typed.GetAttribute("aria-label").Should().Be("Pick a day");
    }

    [Fact]
    public void DatePicker_AllowTypedDate_JoinsFormFieldDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Id, "occurred-field")
            .Add(c => c.HelpText, "When it happened.")
            .AddChildContent<TmDatePicker>(child => child
                .Add(x => x.AllowTypedDate, true)
                .Add(x => x.AriaDescribedBy, "extra-hint")));

        cut.Find(".tm-date-picker-typed").GetAttribute("aria-describedby")
            .Should().Be("occurred-field-field-help extra-hint");
    }

    [Fact]
    public void DatePicker_ReservedClearSlot_IsAlwaysPresent()
    {
        var empty = Render<TmDatePicker>();
        empty.FindAll(".tm-picker-clear-slot").Should().HaveCount(1);
        empty.FindAll(".tm-picker-clear").Should().BeEmpty();

        var filled = Render<TmDatePicker>(p => p.Add(c => c.Value, new DateOnly(2026, 3, 15)));
        filled.FindAll(".tm-picker-clear-slot").Should().HaveCount(1);
        filled.FindAll(".tm-picker-clear").Should().HaveCount(1);
    }
}
