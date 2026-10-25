using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>TDD tests for TmSchedulerToolbar titles (B14: the title matches the range that is loaded).</summary>
public class TmSchedulerToolbarTests : LocalizationTestBase
{
    /// <summary>
    /// The toolbar no longer derives the range itself: it titles the range the scheduler hands it.
    /// 15 May to 14 June exclusive is "15. května – 13. června 2025".
    /// </summary>
    [Fact]
    public void Agenda_Title_Shows_Rendered_Range()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Agenda)
            .Add(c => c.CurrentDate, new DateTime(2025, 5, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 5, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 14)));

        cut.Find(".tm-scheduler-toolbar-title").TextContent
            .Should().Be("15. května – 13. června 2025");
    }

    /// <summary>
    /// A standalone toolbar that is given no range falls back to a Monday-first week of the current
    /// date, the same default the scheduler uses. 18 June 2025 is a Wednesday, so that week is
    /// [16 June, 23 June).
    /// </summary>
    [Fact]
    public void Standalone_Week_Without_Range_Shows_The_Monday_Week()
    {
        using var _ = CultureScope.Use("en-US");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 18)));

        cut.Find(".tm-scheduler-toolbar-title").TextContent.Should().Be("June 16–22, 2025");
    }

    /// <summary>Timeline title is the long day title of the range start, not a range.</summary>
    [Fact]
    public void Timeline_Title_Is_Day_Title()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Timeline)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 6, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 16)));

        cut.Find(".tm-scheduler-toolbar-title").TextContent
            .Should().Be("Neděle 15. června 2025");
    }

    /// <summary>
    /// Week title is the genitive range of the week the scheduler computed. A Sunday-first week of
    /// 15 June 2025 is [15 June, 22 June), titled "15.–21. června 2025".
    /// </summary>
    [Fact]
    public void WeekTitle_Shows_The_Given_Range()
    {
        using var _ = CultureScope.Use("cs-CZ");

        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.RangeStart, new DateTime(2025, 6, 15))
            .Add(c => c.RangeEnd, new DateTime(2025, 6, 22)));

        cut.Find(".tm-scheduler-toolbar-title").TextContent
            .Should().Be("15.–21. června 2025");
    }

    /// <summary>The view switcher is a radiogroup; the active view is the checked radio.</summary>
    [Fact]
    public void ViewSwitcher_Has_Radiogroup_And_AriaChecked()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Week)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var group = cut.Find("[role='radiogroup']");
        group.GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var radios = cut.FindAll("[role='radio']");
        radios.Count.Should().Be(5);
        radios.Count(r => r.GetAttribute("aria-checked") == "true").Should().Be(1);
        radios.Single(r => r.GetAttribute("data-view") == "Week").GetAttribute("aria-checked").Should().Be("true");
        radios.Count(r => r.GetAttribute("tabindex") == "0").Should().Be(1);
    }

    /// <summary>Previous, Today and Next each have an accessible name.</summary>
    [Fact]
    public void Nav_Buttons_Have_Accessible_Names()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        cut.Find("[data-testid='scheduler-prev']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();
        cut.Find("[data-testid='scheduler-next']").GetAttribute("aria-label").Should().NotBeNullOrWhiteSpace();

        var today = cut.Find("[data-testid='scheduler-today']");
        var name = today.GetAttribute("aria-label") ?? today.TextContent;
        name.Trim().Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>The title announces range changes through a polite live region.</summary>
    [Fact]
    public void Title_Is_AriaLive_Polite()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15)));

        var title = cut.Find(".tm-scheduler-toolbar-title");
        title.GetAttribute("aria-live").Should().Be("polite");
        title.GetAttribute("aria-atomic").Should().Be("true");
    }

    /// <summary>
    /// Print is a sibling of the radiogroup, never a radio, and the group contains only the view radios.
    /// </summary>
    [Fact]
    public void Print_Is_Outside_The_Radiogroup()
    {
        var cut = Render<TmSchedulerToolbar>(p => p
            .Add(c => c.View, TmScheduleViewType.Month)
            .Add(c => c.CurrentDate, new DateTime(2025, 6, 15))
            .Add(c => c.ShowPrint, true));

        var group = cut.Find("[role='radiogroup']");
        group.QuerySelectorAll("[role='radio']").Length.Should().Be(5);
        group.QuerySelector("[data-testid='scheduler-print']").Should().BeNull();
        cut.Find(".tm-scheduler-toolbar-views [data-testid='scheduler-print']").Should().NotBeNull();
    }
}
