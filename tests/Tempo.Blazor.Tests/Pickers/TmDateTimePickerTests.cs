using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmDateTimePickerTests : LocalizationTestBase
{
    [Fact]
    public void DateTimePicker_RendersDateAndTimeSections()
    {
        var cut = Render<TmDateTimePicker>();

        cut.Find(".tm-date-picker-trigger").Should().NotBeNull();
        cut.FindAll(".tm-time-input").Should().HaveCount(1);
    }

    [Fact]
    public void DateTimePicker_Value_Binding_Works()
    {
        var dt  = new DateTime(2025, 6, 15, 10, 30, 0);
        var cut = Render<TmDateTimePicker>(p => p.Add(c => c.Value, dt));

        cut.Find(".tm-date-picker-trigger").TextContent.Should().Contain("15");
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("10");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
    }

    [Fact]
    public void DateTimePicker_SelectDate_KeepsExistingTime()
    {
        DateTime? captured = null;
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 14, 45, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        // Open calendar and select a day
        cut.Find(".tm-date-picker-trigger").Click();
        var day = cut.FindAll(".tm-cal-day:not(.tm-cal-day--other-month):not(.tm-cal-day--disabled)").First();
        day.Click();

        captured.Should().NotBeNull();
        captured!.Value.Hour.Should().Be(14);
        captured.Value.Minute.Should().Be(45);
    }

    [Fact]
    public void DateTimePicker_ChangeTime_KeepsExistingDate()
    {
        DateTime? captured = null;
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 14, 45, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("9");

        captured.Should().NotBeNull();
        captured!.Value.Year.Should().Be(2025);
        captured.Value.Month.Should().Be(6);
        captured.Value.Day.Should().Be(15);
    }

    [Fact]
    public void DateTimePicker_ClearButton_ClearsDateAndTime()
    {
        DateTime? captured = null;
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value,        new DateTime(2025, 6, 15, 10, 0, 0))
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-picker-clear").Click();

        captured.Should().BeNull();
    }

    [Fact]
    public void DateTimePicker_MinDateTime_Enforced()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value,    new DateTime(2025, 1, 1, 7, 0, 0))
            .Add(c => c.MinValue, new DateTime(2025, 1, 1, 9, 0, 0)));

        cut.FindAll(".tm-time-picker--invalid, .tm-datetime-picker--invalid")
           .Should().NotBeEmpty();
    }

    [Fact]
    public void DateTimePicker_MaxDateTime_Enforced()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value,    new DateTime(2025, 12, 31, 23, 0, 0))
            .Add(c => c.MaxValue, new DateTime(2025, 12, 31, 20, 0, 0)));

        cut.FindAll(".tm-time-picker--invalid, .tm-datetime-picker--invalid")
           .Should().NotBeEmpty();
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void DateTimePicker_Required_HasNoAriaRequiredOnTrigger()
    {
        // N147: aria-required is NOT a permitted attribute on role=button (ARIA 1.2/HTML-AAM) —
        // requiredness is carried by the tm-input-label-required marker on the label and by form
        // validation, never by an attribute on the trigger.
        var cut = Render<TmDateTimePicker>(p => p.Add(c => c.Required, true));
        cut.Find(".tm-date-picker-trigger").HasAttribute("aria-required").Should().BeFalse();
    }

    [Fact]
    public void DateTimePicker_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Required, true));
        cut.Find(".tm-picker-label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void DateTimePicker_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmDateTimePicker>(p => p.Add(c => c.Label, "Occurred"));
        cut.Find(".tm-date-picker-trigger").HasAttribute("aria-required").Should().BeFalse();
        cut.Find(".tm-picker-label").ClassList.Should().NotContain("tm-input-label-required");
    }

    // ── F3 (carried from F2): the popup traps focus through TmFocusScope ────────────────────

    [Fact]
    public void DateTimePicker_OpenCalendar_PopupTrapsFocus_WithModalDialogSemantics()
    {
        var cut = Render<TmDateTimePicker>(p => p.Add(c => c.Label, "Occurred"));

        cut.Find(".tm-date-picker-trigger").Click();

        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        scope.Instance.Active.Should().BeTrue();
        scope.Instance.Modal.Should().BeTrue();
        var root = scope.Find(".tm-focus-scope");
        root.GetAttribute("role").Should().Be("dialog");
        root.GetAttribute("aria-modal").Should().Be("true");
        root.GetAttribute("aria-labelledby").Should().Be(cut.Find(".tm-picker-label").Id);
        root.QuerySelector(".tm-calendar").Should().NotBeNull();
    }

    [Fact]
    public void DateTimePicker_Empty_ShowsTimePlaceholder_NotMidnight()
    {
        var cut = Render<TmDateTimePicker>();

        cut.Find(".tm-time-input").ClassList.Should().Contain("tm-time-input--empty");
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().BeNullOrEmpty();
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().BeNullOrEmpty();
        cut.FindAll(".tm-date-picker-typed").Should().BeEmpty();
        cut.FindAll(".tm-picker-clear-slot").Should().HaveCount(1);
    }

    [Fact]
    public void DateTimePicker_ReadOnly_KeepsTriggerFocusable_DoesNotOpen()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value, new DateTime(2026, 3, 15, 10, 30, 0))
            .Add(c => c.ReadOnly, true));

        var trigger = cut.Find(".tm-date-picker-trigger");
        trigger.HasAttribute("disabled").Should().BeFalse();
        trigger.GetAttribute("aria-readonly").Should().Be("true");
        cut.Find(".tm-datetime-picker").ClassList.Should().Contain("tm-datetime-picker--readonly");
        cut.FindAll(".tm-picker-clear").Should().BeEmpty();
        cut.Find(".tm-time-input").ClassList.Should().Contain("tm-time-input--readonly");

        trigger.Click();
        cut.FindAll(".tm-calendar").Should().BeEmpty();
    }

    [Fact]
    public void DateTimePicker_LabelTrailing_AndAriaDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Id, "occurred-field")
            .Add(c => c.HelpText, "When it happened.")
            .AddChildContent<TmDateTimePicker>(child => child
                .Add(x => x.Label, "Occurred")
                .Add(x => x.AriaDescribedBy, "extra-hint")
                .Add(x => x.LabelTrailing, "<span id='dt-hint'>ISO</span>")));

        cut.Find(".tm-date-picker-trigger").GetAttribute("aria-describedby")
            .Should().Be("occurred-field-field-help extra-hint");
        cut.Find("#dt-hint").TextContent.Should().Be("ISO");
    }

    [Fact]
    public void DateTimePicker_FirstDate_AppliesMidnightByDefault()
    {
        DateTime? captured = null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-date-picker-trigger").Click();
        cut.Find($".tm-cal-day[data-date='{today:yyyy-MM-dd}']").Click();

        captured.Should().Be(today.ToDateTime(TimeOnly.MinValue));
    }

    [Fact]
    public void DateTimePicker_DefaultTimeNow_UsesInjectedTimeProvider()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2031, 7, 4, 9, 17, 42, TimeSpan.Zero));
        Services.AddSingleton<TimeProvider>(clock);

        DateTime? captured = null;
        var today = DateOnly.FromDateTime(DateTime.Today);
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.DefaultTime, DateTimePickerDefaultTime.Now)
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-date-picker-trigger").Click();
        cut.Find($".tm-cal-day[data-date='{today:yyyy-MM-dd}']").Click();

        captured.Should().NotBeNull();
        TimeOnly.FromDateTime(captured!.Value).Should().Be(new TimeOnly(9, 17, 42));
    }

    [Fact]
    public void DateTimePicker_AllowTypedDate_FailedParseKeepsValue()
    {
        DateTime? captured = new DateTime(2026, 1, 1, 8, 0, 0);
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Value, captured)
            .Add(c => c.AllowTypedDate, true)
            .Add(c => c.ValueChanged, (DateTime? v) => captured = v));

        cut.Find(".tm-date-picker-typed").Change("not-a-date");

        captured.Should().Be(new DateTime(2026, 1, 1, 8, 0, 0));
        cut.Find(".tm-date-picker-typed").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-datetime-picker").ClassList.Should().Contain("tm-datetime-picker--invalid");
    }

    [Fact]
    public void DateTimePicker_AllowTypedDate_WithLabel_UsesLabelledBy_NotAriaLabel()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.AllowTypedDate, true));

        var typed = cut.Find(".tm-date-picker-typed");
        var labelId = cut.Find("label.tm-picker-label").GetAttribute("id");
        typed.GetAttribute("aria-labelledby").Should().Be(labelId);
        typed.HasAttribute("aria-label").Should().BeFalse(
            "aria-label is only the fallback when there is no visible Label");
    }

    [Fact]
    public void DateTimePicker_AllowTypedDate_JoinsFormFieldDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Occurred")
            .Add(c => c.Id, "occurred-field")
            .Add(c => c.HelpText, "When it happened.")
            .AddChildContent<TmDateTimePicker>(child => child
                .Add(x => x.Label, "Occurred")
                .Add(x => x.AllowTypedDate, true)
                .Add(x => x.AriaDescribedBy, "extra-hint")));

        cut.Find(".tm-date-picker-typed").GetAttribute("aria-describedby")
            .Should().Be("occurred-field-field-help extra-hint");
    }
}
