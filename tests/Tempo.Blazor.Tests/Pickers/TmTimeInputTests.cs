using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq.Expressions;
using Bunit;
using Bunit.Rendering;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

/// <summary>
/// TDD for the TmTimeInput defect fixes (plan 1c95309c phase 3, ADR-0007 decision 7):
/// out-of-range values stay visible and invalid, HourCycle follows culture, ReadOnly /
/// AriaDescribedBy / TmFormField cascade / ValueExpression+EditContext.
/// </summary>
public class TmTimeInputTests : LocalizationTestBase
{
    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Czech = CultureInfo.GetCultureInfo("cs-CZ");

    private sealed class AppointmentModel
    {
        [Required]
        public TimeOnly? Start { get; set; }
    }

    // ── Range: no silent discard ─────────────────────────────────────────────

    [Fact]
    public void OutOfRange_Typing_KeepsTheTypedValue_AndMarksInvalid()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(10, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(12, 0))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("8");

        captured.Should().Be(new TimeOnly(8, 0),
            "an out-of-range commit must reach the host — silently dropping it was the defect");
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("08",
            "the typed hour stays on screen instead of snapping back to the previous in-range value");
        cut.Find(".tm-time-input").ClassList.Should().Contain("tm-time-input--invalid");
        cut.Find(".tm-time-seg--hours").GetAttribute("aria-invalid").Should().Be("true");
        cut.Find(".tm-time-input__error").TextContent.Should().Contain("09:00").And.Contain("12:00");
    }

    [Fact]
    public void OutOfRange_DoesNotRevertDisplay_WhenParentDoesNotEchoValue()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(10, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? _) => { }));

        cut.Find(".tm-time-seg--hours").Change("7");

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("07",
            "without a two-way bind the typed out-of-range hour must still stay — OnParametersSet must not overwrite it from the stale Value");
        cut.Find(".tm-time-seg--hours").GetAttribute("aria-invalid").Should().Be("true");
    }

    [Fact]
    public void InRange_Typing_ClearsTheInvalidState()
    {
        var value = new TimeOnly(8, 0);
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(12, 0))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v ?? value));

        cut.Find(".tm-time-input").ClassList.Should().Contain("tm-time-input--invalid");

        cut.Find(".tm-time-seg--hours").Change("10");
        cut.Render(p => p
            .Add(c => c.Value, value)
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.MaxTime, new TimeOnly(12, 0))
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => value = v ?? value));

        cut.Find(".tm-time-input").ClassList.Should().NotContain("tm-time-input--invalid");
        cut.Find(".tm-time-seg--hours").HasAttribute("aria-invalid").Should().BeFalse();
        cut.FindAll(".tm-time-input__error").Should().BeEmpty();
    }

    [Fact]
    public void HostErrorText_WinsOverTheLocalizedRangeMessage()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(8, 0))
            .Add(c => c.MinTime, new TimeOnly(9, 0))
            .Add(c => c.Invalid, true)
            .Add(c => c.ErrorText, "Opening hours only.")
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-input__error").TextContent.Should().Be("Opening hours only.");
    }

    [Fact]
    public void InvalidWithoutErrorText_RendersNoErrorElement()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Invalid, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.FindAll(".tm-time-input__error").Should().BeEmpty(
            "Invalid without ErrorText is the TmFormField-owned-message path (same as TmOtpInput)");
        cut.Find(".tm-time-seg--hours").GetAttribute("aria-invalid").Should().Be("true");
    }

    // ── Hour cycle ───────────────────────────────────────────────────────────

    [Fact]
    public void HourCycle_H23_KeepsPadded24HourHours()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
        cut.FindAll(".tm-time-input__period").Should().BeEmpty();
        cut.Find(".tm-time-seg--hours").GetAttribute("min").Should().Be("0");
        cut.Find(".tm-time-seg--hours").GetAttribute("max").Should().Be("23");
    }

    [Fact]
    public void HourCycle_H12_Renders12HourHoursAndPeriod()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H12)
            .Add(c => c.Culture, EnglishUs));

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("02");
        cut.Find(".tm-time-seg--minutes").GetAttribute("value").Should().Be("30");
        cut.Find(".tm-time-seg--hours").GetAttribute("min").Should().Be("1");
        cut.Find(".tm-time-seg--hours").GetAttribute("max").Should().Be("12");
        cut.Find(".tm-time-input__period").Should().NotBeNull();
        cut.FindAll("[data-testid='segmented-option']").Should().HaveCount(2);
        cut.Find("[data-testid='segmented-option'][data-value='True']").GetAttribute("aria-checked")
            .Should().Be("true", "14:30 is PM");
    }

    [Fact]
    public void HourCycle_Auto_enUS_Uses12HourClock()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.Auto)
            .Add(c => c.Culture, EnglishUs));

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("02");
        cut.FindAll(".tm-time-input__period").Should().HaveCount(1);
    }

    [Fact]
    public void HourCycle_Auto_cs_Uses24HourClock()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.Auto)
            .Add(c => c.Culture, Czech));

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("14");
        cut.FindAll(".tm-time-input__period").Should().BeEmpty();
    }

    [Fact]
    public void HourCycle_H12_Midnight_Shows12Am()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(0, 15))
            .Add(c => c.HourCycle, TimeHourCycle.H12));

        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("12");
        cut.Find("[data-testid='segmented-option'][data-value='False']").GetAttribute("aria-checked")
            .Should().Be("true");
    }

    [Fact]
    public void HourCycle_H12_TypingHour_KeepsPeriod()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H12)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("3");

        captured.Should().Be(new TimeOnly(15, 30));
    }

    [Fact]
    public void HourCycle_H12_TogglingPm_ConvertsTheHour()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(9, 15))
            .Add(c => c.HourCycle, TimeHourCycle.H12)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find("[data-testid='segmented-option'][data-value='True']").Click();

        captured.Should().Be(new TimeOnly(21, 15));
    }

    // ── Seconds preservation when ShowSeconds is off ──────────────────────────

    [Fact]
    public void ShowSecondsFalse_HourChange_KeepsBoundSeconds()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(10, 15, 30))
            .Add(c => c.ShowSeconds, false)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--hours").Change("11");

        captured.Should().Be(new TimeOnly(11, 15, 30),
            "hiding the seconds segment must not zero the bound seconds — HEAD kept Value.Second");
    }

    [Fact]
    public void ShowSecondsFalse_MinuteChange_KeepsBoundSeconds()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30, 45))
            .Add(c => c.ShowSeconds, false)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-seg--minutes").Change("31");

        captured.Should().Be(new TimeOnly(14, 31, 45),
            "a minute edit with ShowSeconds=false must keep 45s; ShowSeconds ? Second : 0 is the mutant");
    }

    [Fact]
    public void ShowSecondsFalse_PeriodToggle_KeepsBoundSeconds()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(9, 15, 30))
            .Add(c => c.ShowSeconds, false)
            .Add(c => c.HourCycle, TimeHourCycle.H12)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find("[data-testid='segmented-option'][data-value='True']").Click();

        captured.Should().Be(new TimeOnly(21, 15, 30),
            "AM/PM must not drop the bound seconds when the seconds segment is hidden");
    }

    [Fact]
    public void OutOfRange_ShowSeconds_FormatsBoundsWithSeconds()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(18, 0, 45))
            .Add(c => c.ShowSeconds, true)
            .Add(c => c.MinTime, new TimeOnly(9, 0, 0))
            .Add(c => c.MaxTime, new TimeOnly(18, 0, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-input__error").TextContent
            .Should().Contain("09:00:00")
            .And.Contain("18:00:30",
                "18:00:15 is valid when MaxTime is 18:00:30, so the message must not truncate to 18:00");
    }

    // ── ReadOnly / Disabled ──────────────────────────────────────────────────

    [Fact]
    public void ReadOnly_DefaultIsFalse_AndIsNeverAssignedInternally()
    {
        new TmTimeInput().ReadOnly.Should().BeFalse();
    }

    [Fact]
    public void ReadOnly_MarksSegmentsReadonly_AndBlocksCommit()
    {
        TimeOnly? captured = null;
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(10, 0))
            .Add(c => c.ReadOnly, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, (TimeOnly? v) => captured = v));

        cut.Find(".tm-time-input").ClassList.Should().Contain("tm-time-input--readonly");
        cut.Find(".tm-time-seg--hours").HasAttribute("readonly").Should().BeTrue();
        cut.Find(".tm-time-seg--hours").GetAttribute("aria-readonly").Should().Be("true");
        cut.Find(".tm-time-seg--hours").HasAttribute("disabled").Should().BeFalse(
            "ReadOnly stays focusable — Disabled is the other parameter");

        cut.Find(".tm-time-seg--hours").Change("11");
        captured.Should().BeNull("ReadOnly must not raise ValueChanged");
        cut.Find(".tm-time-seg--hours").GetAttribute("value").Should().Be("10");
    }

    [Fact]
    public void ReadOnly_HidesPeriodInteraction()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.Value, new TimeOnly(14, 30))
            .Add(c => c.HourCycle, TimeHourCycle.H12)
            .Add(c => c.ReadOnly, true));

        cut.FindAll(".tm-time-input__period [aria-disabled='true']").Should().NotBeEmpty();
    }

    // ── AriaDescribedBy + TmFormField cascade ────────────────────────────────

    [Fact]
    public void AriaDescribedBy_LandsOnEverySegment()
    {
        var cut = Render<TmTimeInput>(p => p
            .Add(c => c.AriaDescribedBy, "hint-id")
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        cut.Find(".tm-time-seg--hours").GetAttribute("aria-describedby").Should().Be("hint-id");
        cut.Find(".tm-time-seg--minutes").GetAttribute("aria-describedby").Should().Be("hint-id");
    }

    [Fact]
    public void FormField_HelpCascade_ReachesSegments()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Start")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Opening hours.")
            .AddChildContent<TmTimeInput>(child => child
                .Add(x => x.HourCycle, TimeHourCycle.H23)));

        cut.Find(".tm-time-seg--hours").GetAttribute("aria-describedby")
            .Should().Be("region-field-field-help");
    }

    [Fact]
    public void FormField_ErrorCascade_ReachesSegments()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Start")
            .Add(c => c.Id, "region-field")
            .Add(c => c.ErrorMessage, "Required.")
            .AddChildContent<TmTimeInput>(child => child
                .Add(x => x.HourCycle, TimeHourCycle.H23)));

        cut.Find(".tm-time-seg--hours").GetAttribute("aria-describedby")
            .Should().Be("region-field-field-error");
        cut.Find(".tm-time-seg--hours").GetAttribute("aria-invalid").Should().Be("true",
            "a surrounding field error marks the segments invalid even without TmTimeInput.Invalid");
    }

    [Fact]
    public void OwnError_JoinsHostAndFieldDescribedBy()
    {
        var cut = Render<TmFormField>(p => p
            .Add(c => c.Label, "Start")
            .Add(c => c.Id, "region-field")
            .Add(c => c.HelpText, "Opening hours.")
            .AddChildContent<TmTimeInput>(child => child
                .Add(x => x.Id, "start-time")
                .Add(x => x.HourCycle, TimeHourCycle.H23)
                .Add(x => x.AriaDescribedBy, "extra-hint")
                .Add(x => x.Invalid, true)
                .Add(x => x.ErrorText, "Too early.")));

        var described = cut.Find(".tm-time-seg--hours").GetAttribute("aria-describedby");
        described.Should().Contain("start-time-error");
        described.Should().Contain("region-field-field-help");
        described.Should().Contain("extra-hint");
    }

    // ── ValueExpression / EditContext ────────────────────────────────────────

    [Fact]
    public void InEditForm_NotifiesFieldChanged()
    {
        var model = new AppointmentModel { Start = new TimeOnly(10, 0) };
        var cut = RenderInEditForm(model);
        var editContext = cut.FindComponent<EditForm>().Instance.EditContext!;

        var changed = new List<string>();
        editContext.OnFieldChanged += (_, e) => changed.Add(e.FieldIdentifier.FieldName);

        cut.Find(".tm-time-seg--hours").Change("11");

        model.Start.Should().Be(new TimeOnly(11, 0));
        changed.Should().ContainSingle().Which.Should().Be(nameof(AppointmentModel.Start));
    }

    [Fact]
    public void InEditForm_OutOfRange_StillNotifiesFieldChanged()
    {
        var model = new AppointmentModel { Start = new TimeOnly(10, 0) };
        var cut = Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<DataAnnotationsValidator>(0);
                    b.CloseComponent();
                    b.OpenComponent<TmTimeInput>(1);
                    b.AddAttribute(2, nameof(TmTimeInput.HourCycle), TimeHourCycle.H23);
                    b.AddAttribute(3, nameof(TmTimeInput.MinTime), new TimeOnly(9, 0));
                    b.AddAttribute(4, nameof(TmTimeInput.MaxTime), new TimeOnly(12, 0));
                    b.AddAttribute(5, nameof(TmTimeInput.Value), model.Start);
                    b.AddAttribute(6, nameof(TmTimeInput.ValueChanged),
                        EventCallback.Factory.Create<TimeOnly?>(this, v => model.Start = v));
                    b.AddAttribute(7, nameof(TmTimeInput.ValueExpression),
                        (Expression<Func<TimeOnly?>>)(() => model.Start));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });

        var changed = new List<string>();
        cut.FindComponent<EditForm>().Instance.EditContext!.OnFieldChanged +=
            (_, e) => changed.Add(e.FieldIdentifier.FieldName);

        cut.Find(".tm-time-seg--hours").Change("8");

        model.Start.Should().Be(new TimeOnly(8, 0));
        changed.Should().ContainSingle().Which.Should().Be(nameof(AppointmentModel.Start));
    }

    private IRenderedComponent<ContainerFragment> RenderInEditForm(AppointmentModel model)
    {
        return Render(builder =>
        {
            builder.OpenComponent<EditForm>(0);
            builder.AddAttribute(1, nameof(EditForm.Model), model);
            builder.AddAttribute(2, nameof(EditForm.ChildContent), (RenderFragment<EditContext>)(_ =>
                (RenderTreeBuilder b) =>
                {
                    b.OpenComponent<DataAnnotationsValidator>(0);
                    b.CloseComponent();
                    b.OpenComponent<TmTimeInput>(1);
                    b.AddAttribute(2, nameof(TmTimeInput.HourCycle), TimeHourCycle.H23);
                    b.AddAttribute(3, nameof(TmTimeInput.Value), model.Start);
                    b.AddAttribute(4, nameof(TmTimeInput.ValueChanged),
                        EventCallback.Factory.Create<TimeOnly?>(this, v => model.Start = v));
                    b.AddAttribute(5, nameof(TmTimeInput.ValueExpression),
                        (Expression<Func<TimeOnly?>>)(() => model.Start));
                    b.CloseComponent();
                }));
            builder.CloseComponent();
        });
    }
}
