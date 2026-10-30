using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 2 (tokens and the desktop visual layer). Written BEFORE the change: every
/// test fails against the pre-phase component for the reason named in its summary.
/// </summary>
public class TmGanttPhase2VisualTests : LocalizationTestBase
{
    private static TmWorkItem Task(string id, TmWorkItemStatus status = TmWorkItemStatus.Open, int percent = 0,
        string? parentId = null, bool milestone = false) => new()
    {
        Id = id,
        Title = $"Task {id}",
        ParentId = parentId,
        Status = status,
        PercentComplete = percent,
        IsMilestone = milestone,
        Start = new DateTime(2024, 6, 3),
        End = new DateTime(2024, 6, 14),
    };

    private static List<TmWorkItem> Tasks() => [Task("1"), Task("2", percent: 40)];

    // ═══════════════════════════════════════════════════════════════
    // Segmented Day | Week | Month
    // ═══════════════════════════════════════════════════════════════

    /// <summary>The view switch is a segmented group: the active segment carries the class and aria-pressed.</summary>
    [Fact]
    public void TmGantt_ViewSwitch_Marks_Active_View()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        var group = cut.Find(".tm-gantt__segment");
        group.GetAttribute("role").Should().Be("group");
        group.GetAttribute("aria-label").Should().Be(Loc("TmGantt_ViewSwitcher"));
        group.QuerySelectorAll("button").Should().HaveCount(3, "Day | Week | Month");

        var week = cut.Find("[data-testid='gantt-view-week']");
        var day = cut.Find("[data-testid='gantt-view-day']");
        week.ClassList.Should().Contain("tm-gantt__segment-btn--active");
        week.GetAttribute("aria-pressed").Should().Be("true");
        day.ClassList.Should().NotContain("tm-gantt__segment-btn--active");
        day.GetAttribute("aria-pressed").Should().Be("false");

        day.Click();

        cut.Find("[data-testid='gantt-view-day']").ClassList.Should().Contain("tm-gantt__segment-btn--active");
        cut.Find("[data-testid='gantt-view-day']").GetAttribute("aria-pressed").Should().Be("true");
        cut.Find("[data-testid='gantt-view-week']").ClassList.Should().NotContain("tm-gantt__segment-btn--active");
        cut.FindAll(".tm-gantt__segment-btn--active").Should().ContainSingle("exactly one segment is active");
    }

    /// <summary>The primary action reads "+ Add task", not an unlabeled plus.</summary>
    [Fact]
    public void TmGantt_AddTask_Button_Has_Visible_Text()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        var add = cut.Find("[data-testid='gantt-add-task']");
        add.TextContent.Trim().Should().Be("Add task");
        add.GetAttribute("aria-label").Should().Be("Add task");
    }

    // ═══════════════════════════════════════════════════════════════
    // "Today" pill above a solid line
    // ═══════════════════════════════════════════════════════════════

    /// <summary>The marker is a visible, localized pill, not just a title tooltip on a dashed dot.</summary>
    [Fact]
    public void TmGantt_Today_Marker_Has_Visible_Label()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        var label = cut.Find("[data-testid='today-label']");
        label.TextContent.Trim().Should().Be("Today");
        label.ClassList.Should().Contain("tm-gantt__today-label");
        cut.Find("[data-testid='today-marker']").Should().NotBeNull("the line itself stays");
    }

    [Fact]
    public void TmGantt_Today_Label_Is_Localized()
    {
        UseCzechLocalization();
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Find("[data-testid='today-label']").TextContent.Trim().Should().Be("Dnes");
    }

    [Fact]
    public void TmGantt_Today_Label_Follows_ShowTodayMarker()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ViewSettings, new GanttViewSettings { ShowTodayMarker = false }));

        cut.FindAll("[data-testid='today-label']").Should().BeEmpty("no marker, no label");
        cut.FindAll("[data-testid='today-marker']").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Status chip = icon + text
    // ═══════════════════════════════════════════════════════════════

    /// <summary>A status is never colour alone: each chip carries a glyph and the localized text.</summary>
    [Theory]
    [InlineData(TmWorkItemStatus.Done, "✓", "Done", "tm-gantt__status-badge--done")]
    [InlineData(TmWorkItemStatus.InProgress, "◉", "In Progress", "tm-gantt__status-badge--inprogress")]
    [InlineData(TmWorkItemStatus.Open, "○", "Open", "tm-gantt__status-badge--open")]
    [InlineData(TmWorkItemStatus.Closed, "⊘", "Closed", "tm-gantt__status-badge--closed")]
    public void TmGantt_StatusChip_Renders_Icon_And_Text(TmWorkItemStatus status, string glyph, string text, string cssClass)
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, new List<TmWorkItem> { Task("1", status) }));

        var chip = cut.Find("[data-testid='status-badge']");
        chip.ClassList.Should().Contain(cssClass);
        var icon = chip.QuerySelector(".tm-gantt__status-icon");
        icon.Should().NotBeNull("the chip leads with a glyph");
        icon!.TextContent.Trim().Should().Be(glyph);
        icon.GetAttribute("aria-hidden").Should().Be("true", "the glyph is decorative, the text names the status");
        chip.TextContent.Replace(glyph, string.Empty).Trim().Should().Be(text);
    }

    // ═══════════════════════════════════════════════════════════════
    // Percent next to the bar (ShowProgressLabel, additive)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttViewSettings_ShowProgressLabel_Defaults_To_False()
    {
        new GanttViewSettings().ShowProgressLabel.Should().BeFalse("the label is opt-in (additive)");
    }

    [Fact]
    public void TmGantt_ProgressLabel_Hidden_By_Default()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.FindAll("[data-testid='bar-progress-label']").Should().BeEmpty();
    }

    [Fact]
    public void TmGantt_ProgressLabel_Rendered_When_ShowProgressLabel()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem>
            {
                Task("1", percent: 0),
                Task("2", percent: 40),
                Task("3", milestone: true),
            })
            .Add(x => x.ViewSettings, new GanttViewSettings { ShowProgressLabel = true }));

        var labels = cut.FindAll("[data-testid='bar-progress-label']");
        labels.Should().HaveCount(2, "one per bar, none for the milestone diamond");
        labels.Select(l => l.TextContent.Trim()).Should().Equal("0%", "40%");
        labels[0].ClassList.Should().Contain("tm-gantt__bar-progress-label");
    }

    /// <summary>
    /// The percentage used to be a child of the bar, so a Done/Closed bar (<c>opacity: .6</c>) faded it and
    /// the label's contrast was measured against a half-transparent ancestor. It is a sibling of the bar now,
    /// positioned from the bar geometry the component already computes.
    /// </summary>
    [Fact]
    public void TmGantt_ProgressLabel_Is_A_Sibling_Of_The_Bar_Not_Inside_The_Completed_Fade()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem> { Task("1", status: TmWorkItemStatus.Done, percent: 100) })
            .Add(x => x.ViewSettings, new GanttViewSettings { ShowProgressLabel = true }));

        var bar = cut.Find(".tm-gantt__bar--completed");
        var label = cut.Find("[data-testid='bar-progress-label']");

        bar.QuerySelector("[data-testid='bar-progress-label']").Should().BeNull("a faded ancestor would dim the label");
        label.Closest(".tm-gantt__bar--completed").Should().BeNull("the label is not a descendant of the completed bar");
        label.ParentElement.Should().BeSameAs(bar.ParentElement);
        label.ParentElement!.ClassList.Should().Contain("tm-gantt__task-row");

        double Px(string style, string property) => double.Parse(
            System.Text.RegularExpressions.Regex.Match(style, property + @":\s*(-?[\d.]+)px").Groups[1].Value,
            System.Globalization.CultureInfo.InvariantCulture);

        var barStyle = bar.GetAttribute("style")!;
        var expected = Px(barStyle, "left") + Px(barStyle, "width");
        Px(label.GetAttribute("style")!, "left").Should().BeApproximately(expected, 0.05,
            "the label starts where the bar ends");
    }

    [Fact]
    public void TmGantt_ProgressLabel_Steps_Past_The_Deadline_Flame_With_A_Computed_Modifier()
    {
        var late = Task("1");
        late.DueDate = new DateTime(2024, 6, 5);
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, new List<TmWorkItem> { late, Task("2") })
            .Add(x => x.ViewSettings, new GanttViewSettings { ShowProgressLabel = true }));

        var labels = cut.FindAll("[data-testid='bar-progress-label']");
        labels.Should().HaveCount(2);
        labels[0].ClassList.Should().Contain("tm-gantt__bar-progress-label--after-deadline");
        labels[1].ClassList.Should().NotContain("tm-gantt__bar-progress-label--after-deadline");
    }
    [Fact]
    public void TmGantt_ViewSettings_Menu_Toggles_ProgressLabel()
    {
        var settings = new GanttViewSettings();
        GanttViewSettings? raised = null;
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ViewSettings, settings)
            .Add(x => x.OnViewSettingsChanged, Microsoft.AspNetCore.Components.EventCallback.Factory
                .Create<GanttViewSettings>(this, s => raised = s)));

        cut.Find("[data-testid='gantt-view-settings-btn']").Click();
        cut.Find("[data-testid='vset-show-progress']").Change(true);

        raised.Should().NotBeNull();
        raised!.ShowProgressLabel.Should().BeTrue();
        cut.FindAll("[data-testid='bar-progress-label']").Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // Bar colour and the board come from tokens/classes, not hex
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Default_Bar_Color_Uses_Token_Not_Hex()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        var style = cut.Find(".tm-gantt__bar").GetAttribute("style") ?? string.Empty;
        style.Should().Contain("--tm-gantt-task-color");
        style.Should().NotMatchRegex("#[0-9a-fA-F]{3,8}", "the default fill is a token, so dark mode and the indigo theme reach it");
    }

    [Fact]
    public void TmGantt_Board_Columns_Use_Css_Classes_Not_Inline_Hex()
    {
        var tasks = new List<TmWorkItem>
        {
            Task("1", TmWorkItemStatus.Open), Task("2", TmWorkItemStatus.Open),
            Task("3", TmWorkItemStatus.InProgress), Task("4", TmWorkItemStatus.Done), Task("5", TmWorkItemStatus.Closed),
        };
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, tasks)
            .Add(x => x.View, GanttView.Board));

        var board = cut.Find("[data-testid='gantt-board-view']");
        board.QuerySelectorAll("[style*='#']").Should().BeEmpty("no inline hex colour anywhere on the board");
        board.QuerySelectorAll(".tm-kanban__header-color").Should().BeEmpty("the column colour is a class, not a style=background-color");

        foreach (var status in new[] { "open", "inprogress", "done", "closed" })
        {
            board.QuerySelectorAll($".tm-kanban__header .tm-gantt__status-badge--{status}")
                .Should().ContainSingle($"the {status} column header is the same status chip as the tree");
        }

        var counts = board.QuerySelectorAll(".tm-kanban__header .tm-kanban__count").Select(c => c.TextContent.Trim());
        counts.Should().Equal("2", "1", "1", "1");
    }

    // ═══════════════════════════════════════════════════════════════
    // Theme: data-theme on the root, tokens do the rest
    // ═══════════════════════════════════════════════════════════════

    /// <summary>Dark reuses tokens-dark.css through data-theme; the class stays as an alias and carries no palette.</summary>
    [Fact]
    public void TmGantt_Theme_Dark_Sets_DataTheme_Instead_Of_Local_Palette()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ViewSettings, new GanttViewSettings { Theme = GanttTheme.Dark }));

        var root = cut.Find(".tm-gantt");
        root.GetAttribute("data-theme").Should().Be("dark");
        root.ClassList.Should().Contain("tm-gantt--theme-dark", "the class stays as an alias");
    }

    [Fact]
    public void TmGantt_Theme_Light_Sets_DataTheme_Light()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ViewSettings, new GanttViewSettings { Theme = GanttTheme.Light }));

        var root = cut.Find(".tm-gantt");
        root.GetAttribute("data-theme").Should().Be("light");
        root.ClassList.Should().Contain("tm-gantt--theme-light");
    }

    [Fact]
    public void TmGantt_Theme_Auto_Without_ThemeService_Adds_No_DataTheme()
    {
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Find(".tm-gantt").HasAttribute("data-theme").Should().BeFalse("Auto inherits the ambient theme");
    }

    [Fact]
    public void TmGantt_Theme_Auto_Follows_The_Global_ThemeService()
    {
        var themeService = new ThemeService();
        Services.AddSingleton(themeService);
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));
        cut.Find(".tm-gantt").HasAttribute("data-theme").Should().BeFalse("the global theme is light");

        cut.InvokeAsync(() => themeService.Toggle());

        cut.Find(".tm-gantt").GetAttribute("data-theme").Should().Be("dark", "Auto respects ThemeService");

        cut.InvokeAsync(() => themeService.Toggle());

        cut.Find(".tm-gantt").HasAttribute("data-theme").Should().BeFalse();
    }

    [Fact]
    public void TmGantt_Explicit_Theme_Wins_Over_The_Global_ThemeService()
    {
        var themeService = new ThemeService();
        themeService.Toggle();
        Services.AddSingleton(themeService);

        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.ViewSettings, new GanttViewSettings { Theme = GanttTheme.Light }));

        cut.Find(".tm-gantt").GetAttribute("data-theme").Should().Be("light");
    }

    [Fact]
    public void TmGantt_Disposal_Unsubscribes_From_ThemeService()
    {
        var themeService = new ThemeService();
        Services.AddSingleton(themeService);
        var cut = Render<TmGantt>(p => p.Add(x => x.Items, Tasks()));

        cut.Dispose();

        var act = () => themeService.Toggle();
        act.Should().NotThrow("a disposed Gantt no longer listens");
    }

    // ═══════════════════════════════════════════════════════════════
    // Workload grid
    // ═══════════════════════════════════════════════════════════════

    private static TmWorkItem Assigned(string id, string assignee, DateTime start, DateTime end) => new()
    {
        Id = id,
        Title = $"Task {id}",
        Start = start,
        End = end,
        Assignees = [new TmWorkItemAssignee { Id = assignee, Name = $"User {assignee}" }],
    };

    private IRenderedComponent<TmGanttWorkloadView> RenderWorkload(params TmWorkItem[] tasks) =>
        Render<TmGanttWorkloadView>(p => p
            .Add(x => x.Tasks, tasks)
            .Add(x => x.Today, new DateTime(2024, 6, 3)));

    /// <summary>An assignee with nothing on a day shows a dash, not an empty box.</summary>
    [Fact]
    public void TmGanttWorkloadView_Empty_Cell_Renders_Dash_Placeholder()
    {
        var cut = RenderWorkload(
            Assigned("1", "a1", new DateTime(2024, 6, 3), new DateTime(2024, 6, 4)),
            Assigned("2", "a2", new DateTime(2024, 6, 5), new DateTime(2024, 6, 6)));

        var empty = cut.Find("[data-testid='workload-cell-a1-2024-06-05']");
        empty.TextContent.Should().Contain("–");
        empty.ClassList.Should().Contain("tm-gantt__workload-cell--empty");
        empty.QuerySelector(".tm-sr-only")!.TextContent.Trim().Should().Be("No allocation", "a screen reader reads words, not a dash");

        var filled = cut.Find("[data-testid='workload-cell-a1-2024-06-03']");
        filled.TextContent.Should().NotContain("–");
        filled.ClassList.Should().NotContain("tm-gantt__workload-cell--empty");
    }

    [Fact]
    public void TmGanttWorkloadView_InCapacity_And_Overloaded_Cells_Carry_Their_Classes()
    {
        var cut = RenderWorkload(
            Assigned("1", "a1", new DateTime(2024, 6, 3), new DateTime(2024, 6, 3)),
            Assigned("2", "a2", new DateTime(2024, 6, 3), new DateTime(2024, 6, 3)),
            Assigned("3", "a2", new DateTime(2024, 6, 3), new DateTime(2024, 6, 3)));

        cut.Find("[data-testid='workload-cell-a1-2024-06-03']").ClassList.Should().Contain("tm-gantt__workload-cell--ok");
        cut.Find("[data-testid='workload-cell-a2-2024-06-03']").ClassList.Should().Contain("tm-gantt__workload-cell--over");
    }

    // ═══════════════════════════════════════════════════════════════
    // UX review finding: the notification preferences leaked into the toolbar
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The notification section lived outside the <c>@if (_viewSettingsOpen)</c> block, so with
    /// <c>NotificationSettings</c> set it was rendered inline in the toolbar row at all times (a 330px
    /// tall toolbar of stacked checkboxes). It belongs inside the settings menu, like every other group.
    /// </summary>
    [Fact]
    public void TmGantt_Notification_Preferences_Render_Only_Inside_The_Open_ViewSettings_Menu()
    {
        var cut = Render<TmGantt>(p => p
            .Add(x => x.Items, Tasks())
            .Add(x => x.NotificationSettings, new TmNotificationPreferences { EmailOnAssign = true }));

        cut.FindAll("[data-testid='notification-settings-section']").Should().BeEmpty("the menu is closed");
        cut.FindAll(".tm-gantt__toolbar .tm-gantt__notification-settings").Should().BeEmpty();

        cut.Find("[data-testid='gantt-view-settings-btn']").Click();

        var section = cut.Find("[data-testid='notification-settings-section']");
        section.Closest(".tm-gantt__view-settings-menu").Should().NotBeNull("the preferences are a group of the menu");
    }

    private static string Loc(string key) => BuildEnglishLocalizer()[key];
}
