using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Pickers;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Pickers;

/// <summary>
/// Picker labels must be programmatically associated with the trigger: a bare
/// <c>&lt;label&gt;</c> with neither <c>for</c> nor a wrapped control names nothing.
/// Trigger pickers use <c>for</c> + <c>aria-labelledby="{label} {value}"</c>; composite
/// pickers (segmented time inputs) name the <c>role="group"</c> body instead.
/// </summary>
public class TmPickerLabelAccessibilityTests : LocalizationTestBase
{
    private static void AssertLabelTargets<T>(IRenderedComponent<T> root, string triggerSelector) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var label   = root.Find("label.tm-picker-label");
        var trigger = root.Find(triggerSelector);

        var labelId   = label.GetAttribute("id");
        var triggerId = trigger.GetAttribute("id");

        labelId.Should().NotBeNullOrEmpty("the label needs an id so the trigger can reference it");
        triggerId.Should().NotBeNullOrEmpty("the trigger needs an id so the label can target it");
        label.GetAttribute("for").Should().Be(triggerId, "clicking the label must focus/activate the trigger");

        var labelledBy = trigger.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty("the trigger's accessible name is label + value");
        labelledBy!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Should().Contain(labelId);
    }

    /// <summary>The trigger's announced name must include the element that shows the picked value.</summary>
    private static void AssertValueInName<T>(IRenderedComponent<T> root, string triggerSelector) where T : Microsoft.AspNetCore.Components.IComponent
    {
        var trigger    = root.Find(triggerSelector);
        var labelledBy = trigger.GetAttribute("aria-labelledby")!
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var labelledElements = labelledBy
            .Select(id => root.FindAll($"#{id}").FirstOrDefault())
            .ToList();

        labelledElements.Should().OnlyContain(e => e != null,
            "every referenced id must resolve in the document");

        labelledElements.Should().Contain(e =>
            e != null && (e.QuerySelector(".tm-picker-placeholder") != null || !string.IsNullOrWhiteSpace(e.TextContent)),
            "the shown value/placeholder must be part of the accessible name");
    }

    [Fact]
    public void DatePicker_Label_IsBound_ToTrigger()
    {
        var cut = Render<TmDatePicker>(p => p.Add(x => x.Label, "Datum"));

        AssertLabelTargets(cut, "button.tm-date-picker-trigger");
        AssertValueInName(cut, "button.tm-date-picker-trigger");
    }

    [Fact]
    public void DatePicker_WithoutLabel_NamesTriggerByValue()
    {
        var cut = Render<TmDatePicker>();

        var trigger = cut.Find("button.tm-date-picker-trigger");
        var valueId = trigger.QuerySelector(".tm-picker-placeholder")!.ParentElement!.GetAttribute("id");
        trigger.GetAttribute("aria-labelledby").Should().Be(valueId,
            "without a label the displayed value/placeholder alone names the trigger");
    }

    [Fact]
    public void DatePicker_AriaAttributes_GoToTrigger_DataAttributes_StayOnRoot()
    {
        var cut = Render<TmDatePicker>(p => p
            .Add(x => x.Label, "Datum")
            .AddUnmatched("aria-describedby", "hint-id")
            .AddUnmatched("data-testid", "my-picker"));

        cut.Find("button.tm-date-picker-trigger").GetAttribute("aria-describedby").Should().Be("hint-id");
        cut.Find(".tm-date-picker").GetAttribute("data-testid").Should().Be("my-picker",
            "test ids live on the root element");
        cut.Find(".tm-date-picker").GetAttribute("aria-describedby").Should().BeNull(
            "aria-* attributes belong on the trigger, not the wrapper");
    }

    [Fact]
    public void DateRangePicker_Label_IsBound_ToTrigger()
    {
        var cut = Render<TmDateRangePicker>(p => p.Add(x => x.Label, "Období"));

        AssertLabelTargets(cut, "button.tm-date-range-trigger");
        AssertValueInName(cut, "button.tm-date-range-trigger");
    }

    [Fact]
    public void TimePicker_Label_TargetsFirstSegment_AndNamesGroup()
    {
        var cut = Render<TmTimePicker>(p => p.Add(x => x.Label, "Čas"));

        var label = cut.Find("label.tm-picker-label");
        var group = cut.Find(".tm-time-picker-body");
        var input = cut.Find("input.tm-time-seg--hours");

        label.GetAttribute("for").Should().Be(input.GetAttribute("id"),
            "label click must focus the hours segment");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-labelledby").Should().Be(label.GetAttribute("id"),
            "the composite input is announced as a named group");
    }

    [Fact]
    public void TimeRangePicker_Label_TargetsStartInput_AndNamesGroup()
    {
        var cut = Render<TmTimeRangePicker>(p => p.Add(x => x.Label, "Směna"));

        var label = cut.Find("label.tm-picker-label");
        var group = cut.Find(".tm-time-range-body");
        var input = cut.Find("input.tm-time-seg--hours");

        label.GetAttribute("for").Should().Be(input.GetAttribute("id"));
        group.GetAttribute("aria-labelledby").Should().Be(label.GetAttribute("id"));
    }

    [Fact]
    public void DateTimePicker_Label_IsBound_ToTrigger_And_TimeLabel_NamesTimeGroup()
    {
        var cut = Render<TmDateTimePicker>(p => p
            .Add(x => x.Label, "Kdy")
            .Add(x => x.TimeLabel, "Čas"));

        AssertLabelTargets(cut, "button.tm-date-picker-trigger");

        var timeLabel = cut.Find(".tm-datetime-time-section label.tm-picker-label");
        var timeGroup = cut.Find(".tm-datetime-time-section");
        var timeInput = cut.Find(".tm-datetime-time-section input.tm-time-seg--hours");

        timeLabel.GetAttribute("for").Should().Be(timeInput.GetAttribute("id"));
        timeGroup.GetAttribute("aria-labelledby").Should().Be(timeLabel.GetAttribute("id"));
    }

    [Fact]
    public void DateTimeRangePicker_Label_TargetsStartTrigger_AndNamesGroups()
    {
        var cut = Render<TmDateTimeRangePicker>(p => p.Add(x => x.Label, "Období"));

        var label      = cut.Find("label.tm-picker-label");
        var group      = cut.Find(".tm-datetime-range-body");
        var startGroup = cut.Find(".tm-datetime-range-start");
        var startTrig  = cut.Find(".tm-datetime-range-start button.tm-date-picker-trigger");
        var endGroup   = cut.Find(".tm-datetime-range-end");

        label.GetAttribute("for").Should().Be(startTrig.GetAttribute("id"),
            "the outer label targets the start picker's trigger");
        group.GetAttribute("aria-labelledby").Should().Be(label.GetAttribute("id"));
        startGroup.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty(
            "each section is a named sub-group");
        endGroup.GetAttribute("aria-labelledby").Should().NotBeNullOrEmpty();
    }
}
