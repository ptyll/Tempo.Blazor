using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Pickers;

public class TmTimeRangePickerTests : LocalizationTestBase
{
    [Fact]
    public void TimeRangePicker_RendersFromAndToInputs()
    {
        var cut = Render<TmTimeRangePicker>();

        cut.FindAll(".tm-time-range-from").Should().HaveCount(1);
        cut.FindAll(".tm-time-range-to").Should().HaveCount(1);
    }

    [Fact]
    public void TimeRangePicker_Value_Binding_Works()
    {
        var start = new TimeOnly(8, 0);
        var end   = new TimeOnly(17, 0);
        var cut   = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Value, (start, end))
            .Add(c => c.HourCycle, TimeHourCycle.H23));

        // Start inputs
        cut.Find(".tm-time-range-from .tm-time-seg--hours").GetAttribute("value").Should().Be("08");
        cut.Find(".tm-time-range-to .tm-time-seg--hours").GetAttribute("value").Should().Be("17");
    }

    [Fact]
    public void TimeRangePicker_EndTime_CannotBeBefore_StartTime()
    {
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Value, (new TimeOnly(17, 0), new TimeOnly(8, 0))));

        cut.FindAll(".tm-time-range--invalid").Should().NotBeEmpty();
    }

    [Fact]
    public void TimeRangePicker_Swap_SwapsValues()
    {
        (TimeOnly? s, TimeOnly? e)? captured = null;
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.ShowSwapButton, true)
            .Add(c => c.Value,          (new TimeOnly(17, 0), new TimeOnly(8, 0)))
            .Add(c => c.ValueChanged,   ((TimeOnly? s, TimeOnly? e) v) => captured = v));

        cut.Find(".tm-time-range-swap-btn").Click();

        captured.Should().NotBeNull();
        captured!.Value.s.Should().Be(new TimeOnly(8, 0));
        captured.Value.e.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public void TimeRangePicker_Duration_Calculated()
    {
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.ShowDuration, true)
            .Add(c => c.Value,        (new TimeOnly(8, 0), new TimeOnly(10, 30))));

        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("2");
    }

    [Fact]
    public void TimeRangePicker_Sections_HaveDistinctAccessibleNames()
    {
        // N149: both ends of the range are named sub-groups (same pattern as
        // TmDateTimeRangePicker) so screen readers distinguish "From" from "To" instead of
        // hearing identical bare Hours/Minutes segments twice.
        var cut = Render<TmTimeRangePicker>();

        var from = cut.Find(".tm-time-range-from");
        from.GetAttribute("role").Should().Be("group");
        var fromLabelId = from.GetAttribute("aria-labelledby");
        fromLabelId.Should().NotBeNullOrEmpty();
        cut.Find($"#{fromLabelId}").TextContent.Should().Be("From");

        var to = cut.Find(".tm-time-range-to");
        to.GetAttribute("role").Should().Be("group");
        var toLabelId = to.GetAttribute("aria-labelledby");
        toLabelId.Should().NotBeNullOrEmpty();
        cut.Find($"#{toLabelId}").TextContent.Should().Be("To");

        fromLabelId.Should().NotBe(toLabelId);
    }

    [Fact]
    public void DefaultPresentation_IsSegments_AndKeepsTwoTimeInputs()
    {
        new TmTimeRangePicker().Presentation.Should().Be(TimePickerPresentation.Segments);

        var cut = Render<TmTimeRangePicker>();

        cut.FindComponents<TmTimeInput>().Should().HaveCount(2);
        cut.FindComponents<TmTimePicker>().Should().BeEmpty();
        cut.FindComponents<TmTimeSelector>().Should().BeEmpty();
        cut.Find(".tm-time-range-picker").Should().NotBeNull();
        cut.Find(".tm-time-range-body").Should().NotBeNull();
        cut.Find(".tm-time-range-from").Should().NotBeNull();
        cut.Find(".tm-time-range-to").Should().NotBeNull();
        cut.Find(".tm-time-range-sep").Should().NotBeNull();
        cut.FindComponents<TmTimeInput>()[1].Instance.InputId.Should().NotBeNullOrEmpty(
            "the end TmTimeInput must have a stable InputId");
    }

    [Fact]
    public void OvernightRange_TwentyTwoToSix_ShowsDurationWithoutError()
    {
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.ShowDuration, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Value, (new TimeOnly(22, 0), new TimeOnly(6, 0))));

        cut.FindAll(".tm-field-error").Should().BeEmpty();
        cut.FindAll(".tm-time-range--invalid").Should().BeEmpty();
        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("8h 00m");
        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("(+1 day)");
        cut.Find(".tm-time-range-picker").GetAttribute("data-tm-overnight").Should().Be("true");

        var overnight = cut.Find(".tm-time-range-overnight");
        overnight.TextContent.Should().Be("(+1 day)",
            "the overnight marker must not carry a hardcoded leading space inside the span");
        overnight.InnerHtml.Should().Be(overnight.InnerHtml.TrimStart());
    }

    [Fact]
    public void OvernightRange_Disabled_TwentyTwoToSix_IsInvalidRange()
    {
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Value, (new TimeOnly(22, 0), new TimeOnly(6, 0))));

        cut.FindAll(".tm-time-range--invalid").Should().NotBeEmpty();
        cut.Find(".tm-field-error").TextContent.Should().Be("End time must be after start time");
    }

    [Fact]
    public void Popup_RendersTwoTimePickers_WithSharedPresentationAndNamedGroups()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.CommitMode, TimeCommitMode.Live)
            .Add(c => c.MinuteStep, 15)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Value, (new TimeOnly(9, 0), new TimeOnly(17, 0))));

        var pickers = cut.FindComponents<TmTimePicker>();
        pickers.Should().HaveCount(2);
        pickers.Should().OnlyContain(p => p.Instance.Presentation == TimePickerPresentation.Popup);
        pickers.Should().OnlyContain(p => p.Instance.CommitMode == TimeCommitMode.Live);
        pickers.Should().OnlyContain(p => p.Instance.MinuteStep == 15);
        pickers.Should().OnlyContain(p => p.Instance.ReadOnly);

        var from = cut.Find(".tm-time-range-from");
        from.GetAttribute("role").Should().Be("group");
        cut.Find($"#{from.GetAttribute("aria-labelledby")}").TextContent.Should().Be("From");

        var to = cut.Find(".tm-time-range-to");
        to.GetAttribute("role").Should().Be("group");
        cut.Find($"#{to.GetAttribute("aria-labelledby")}").TextContent.Should().Be("To");
    }

    [Fact]
    public void ValueChanged_FromStartAndEnd_CarriesThePair()
    {
        (TimeOnly? Start, TimeOnly? End)? captured = null;
        var value = (Start: (TimeOnly?)new TimeOnly(9, 0), End: (TimeOnly?)new TimeOnly(17, 0));
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Value, value)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, ((TimeOnly? s, TimeOnly? e) v) =>
            {
                captured = v;
                value = v;
            }));

        cut.Find(".tm-time-range-from .tm-time-seg--hours").Change("10");
        captured.Should().NotBeNull();
        captured!.Value.Start.Should().Be(new TimeOnly(10, 0));
        captured.Value.End.Should().Be(new TimeOnly(17, 0));

        cut.Render(p => p
            .Add(c => c.Value, value)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.ValueChanged, ((TimeOnly? s, TimeOnly? e) v) =>
            {
                captured = v;
                value = v;
            }));

        cut.Find(".tm-time-range-to .tm-time-seg--hours").Change("18");
        captured!.Value.Start.Should().Be(new TimeOnly(10, 0));
        captured.Value.End.Should().Be(new TimeOnly(18, 0));
    }

    [Fact]
    public void Popup_OnConfirm_DoesNotCommitUntilDone()
    {
        SetupOverlayAndSelectorModules();
        (TimeOnly? Start, TimeOnly? End)? captured = (new TimeOnly(14, 30), new TimeOnly(17, 0));
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Popup)
            .Add(c => c.CommitMode, TimeCommitMode.OnConfirm)
            .Add(c => c.LayoutMode, TmLayoutMode.Desktop)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Value, captured.Value)
            .Add(c => c.ValueChanged, ((TimeOnly? s, TimeOnly? e) v) => captured = v));

        cut.Find(".tm-time-range-from .tm-time-picker__trigger").Click();
        cut.Find("[data-testid='time-selector-increase-hours']").Click();

        captured!.Value.Start.Should().Be(new TimeOnly(14, 30), "OnConfirm holds a draft until Done");
        captured.Value.End.Should().Be(new TimeOnly(17, 0));

        cut.Find(".tm-time-picker__done").Click();

        captured.Value.Start.Should().Be(new TimeOnly(15, 30));
        captured.Value.End.Should().Be(new TimeOnly(17, 0));
    }

    [Fact]
    public void ReadOnly_HidesSwap_AndIsNeverAssignedInternally()
    {
        new TmTimeRangePicker().ReadOnly.Should().BeFalse();

        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.ShowSwapButton, true)
            .Add(c => c.ReadOnly, true)
            .Add(c => c.Value, (new TimeOnly(9, 0), new TimeOnly(17, 0))));

        cut.FindAll(".tm-time-range-swap-btn").Should().BeEmpty();
        cut.Instance.ReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Auto_Mobile_EveryLabelFor_PointsAtAnExistingId()
    {
        SetupOverlayAndSelectorModules();
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.Presentation, TimePickerPresentation.Auto)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .Add(c => c.Label, "Shift")
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Value, (new TimeOnly(22, 0), new TimeOnly(6, 0))));

        cut.FindAll(".tm-time-seg").Should().BeEmpty("Auto on mobile hides segments, so InputId is not in the DOM");

        var labels = cut.FindAll("label[for]");
        foreach (var label in labels)
        {
            var id = label.GetAttribute("for");
            id.Should().NotBeNullOrEmpty();
            cut.FindAll($"#{id}").Should().HaveCount(1,
                $"label for=\"{id}\" must point at an existing element (axe label / broken for)");
        }
    }

    [Fact]
    public void OverlayParameterXmlDocs_NameTheOwnerAndSegmentsIgnore()
    {
        var src = File.ReadAllText(Path.Combine(
            Tempo.Blazor.Tests.Theme.ThemeCss.RepositoryRoot().FullName,
            "src", "Tempo.Blazor", "Components", "Pickers", "TmTimeRangePicker.razor"));

        src.Should().Contain("forwarded to both ends",
            "HourCycle is forwarded to TmTimePicker in Popup/Auto, not only TmTimeInput");
        src.Should().Contain("<c>TmTimeInput</c> in Segments");
        src.Should().Contain("<c>TmTimePicker</c> in Popup/Auto");
        src.Should().Contain(
            "Minute step forwarded to both ends when Presentation is Popup or Auto; ignored by Segments");
        src.Should().Contain("When true, an off-step bound value is marked invalid and never rewritten. Popup/Auto only;");
        src.Should().Contain("a sheet holds a draft until Done; a popover");
        src.Should().Contain("commits live.");
        src.Should().NotContain("commits live. Popup/Auto only; ignored by Segments",
            "CommitMode XML doc must not repeat the Popup/Auto-only sentence already on the first summary line");
    }

    [Fact]
    public void Changelog_Phase7_MentionsDurationMinutesAndAriaLabelledBy()
    {
        var changelog = File.ReadAllText(Path.Combine(
            Tempo.Blazor.Tests.Theme.ThemeCss.RepositoryRoot().FullName, "CHANGELOG.md"));
        var phase7 = changelog[..changelog.IndexOf("## 2.12.0", StringComparison.Ordinal)];

        phase7.Should().Contain("`TmTimeRangePicker_DurationMinutes`");
        phase7.Should().Contain("`\"{0}m\"`");
        phase7.Should().Contain("`TmTimePicker.AriaLabelledBy`");
    }

    [Fact]
    public void CzechCulture_LocalizesOvernightDuration()
    {
        UseCzechLocalization();
        var cut = Render<TmTimeRangePicker>(p => p
            .Add(c => c.AllowOvernightRange, true)
            .Add(c => c.ShowDuration, true)
            .Add(c => c.HourCycle, TimeHourCycle.H23)
            .Add(c => c.Value, (new TimeOnly(22, 0), new TimeOnly(6, 0))));

        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("8 h 00 min");
        cut.Find(".tm-time-range-duration").TextContent.Should().Contain("(+1 den)");
    }

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
}
