using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TDD tests for Phase 2: Interakce &amp; Data.
/// Each test is written BEFORE the corresponding implementation (Red → Green → Refactor).
/// </summary>
public class TmGanttPhase2Tests : LocalizationTestBase
{
    // ─── Sample data ────────────────────────────────────────────────

    private static IReadOnlyList<TmWorkItem> Tasks(params TmWorkItem[] tasks) => tasks;

    private static TmWorkItem MakeTask(string id, string title, string? parentId = null) => new()
    {
        Id = id, Title = title, ParentId = parentId,
        Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10)
    };

    private static IReadOnlyList<TmWorkItem> SampleHierarchy() => Tasks(
        MakeTask("1", "Root A"),
        MakeTask("2", "Child A.1", "1"),
        MakeTask("3", "Child A.2", "1"),
        MakeTask("4", "Root B"),
        MakeTask("5", "Child B.1", "4"),
        MakeTask("6", "Grandchild B.1.1", "5")
    );

    // ═══════════════════════════════════════════════════════════════
    // F2.1 – WBS Číslování
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttTaskNode_Has_WbsNumber_Property()
    {
        var node = new GanttTaskNode(MakeTask("1", "T"));
        node.WbsNumber.Should().NotBeNull();
    }

    [Fact]
    public void BuildTree_Assigns_WBS_To_Roots_And_Children()
    {
        var tree = GanttHelper.BuildTree(SampleHierarchy());

        tree[0].WbsNumber.Should().Be("1");
        tree[0].Children[0].WbsNumber.Should().Be("1.1");
        tree[0].Children[1].WbsNumber.Should().Be("1.2");
        tree[1].WbsNumber.Should().Be("2");
    }

    [Fact]
    public void BuildTree_Assigns_WBS_To_Deep_Nesting()
    {
        var tree = GanttHelper.BuildTree(SampleHierarchy());

        // Root B → Child B.1 → Grandchild B.1.1
        tree[1].Children[0].Children[0].WbsNumber.Should().Be("2.1.1");
    }

    [Fact]
    public void TmGantt_Renders_WbsNumber_In_TreeRow()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, SampleHierarchy()));

        var wbsElements = cut.FindAll("[data-testid='wbs-number']");
        wbsElements.Should().NotBeEmpty();
        wbsElements[0].TextContent.Should().Be("1");
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.2 – Inline Editace Buněk (Dvojklik)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Has_InlineEditState_Fields()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(MakeTask("1", "T"))));

        // Access internal state via FindComponent; verify no inline edit by default
        cut.FindAll("input[data-testid='inline-edit-input']").Should().BeEmpty();
    }

    [Fact]
    public void TitleCell_In_InlineEditMode_Renders_Input()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(MakeTask("1", "Title Test"))));

        var titleCell = cut.Find("[data-testid='tree-cell-title-1']");
        titleCell.DoubleClick();
        cut.WaitForState(() => cut.FindAll("input[data-testid='inline-edit-input']").Count > 0);

        var input = cut.Find("input[data-testid='inline-edit-input']");
        input.GetAttribute("value").Should().Be("Title Test");
    }

    [Fact]
    public void InlineEdit_Escape_Cancels_Without_Saving()
    {
        TmWorkItem? updated = null;
        var task = MakeTask("1", "Original");
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(task));
            p.Add(c => c.OnTaskUpdated, (TmWorkItem t) => updated = t);
        });

        var titleCell = cut.Find("[data-testid='tree-cell-title-1']");
        titleCell.DoubleClick();
        cut.WaitForState(() => cut.FindAll("input[data-testid='inline-edit-input']").Count > 0);

        cut.Find("input[data-testid='inline-edit-input']")
           .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Escape" });

        updated.Should().BeNull();
        cut.FindAll("input[data-testid='inline-edit-input']").Should().BeEmpty();
    }

    [Fact]
    public void InlineEdit_Enter_Commits_And_Fires_OnTaskUpdated()
    {
        TmWorkItem? updated = null;
        var task = MakeTask("1", "Original");
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(task));
            p.Add(c => c.OnTaskUpdated, (TmWorkItem t) => updated = t);
        });

        cut.Find("[data-testid='tree-cell-title-1']").DoubleClick();
        cut.WaitForState(() => cut.FindAll("input[data-testid='inline-edit-input']").Count > 0);

        var input = cut.Find("input[data-testid='inline-edit-input']");
        input.Change("New Title");
        input.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Enter" });

        updated.Should().NotBeNull();
        updated!.Title.Should().Be("New Title");
    }

    [Fact]
    public void InlineEdit_Blur_Commits_Change()
    {
        TmWorkItem? updated = null;
        var task = MakeTask("1", "Original");
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(task));
            p.Add(c => c.OnTaskUpdated, (TmWorkItem t) => updated = t);
        });

        cut.Find("[data-testid='tree-cell-title-1']").DoubleClick();
        cut.WaitForState(() => cut.FindAll("input[data-testid='inline-edit-input']").Count > 0);

        var input = cut.Find("input[data-testid='inline-edit-input']");
        input.Change("Blurred Title");
        input.Blur();

        updated.Should().NotBeNull();
        updated!.Title.Should().Be("Blurred Title");
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.3 – Konfigurovatelné Sloupce Tree Gridu
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttColumnKey_Enum_Has_17_Values()
    {
        var values = Enum.GetValues<GanttColumnKey>();
        values.Should().HaveCount(17);
        values.Should().Contain(GanttColumnKey.Title);
        values.Should().Contain(GanttColumnKey.Progress);
        values.Should().Contain(GanttColumnKey.WBS);
        values.Should().Contain(GanttColumnKey.Predecessor);
        values.Should().Contain(GanttColumnKey.ResourceOverload);
    }

    [Fact]
    public void GanttColumnDefinition_Has_Required_Properties()
    {
        var col = new GanttColumnDefinition { Key = GanttColumnKey.Title, Visible = true, Order = 0 };
        col.Key.Should().Be(GanttColumnKey.Title);
        col.Visible.Should().BeTrue();
        col.Order.Should().Be(0);
        col.Width.Should().BeNull();
    }

    [Fact]
    public void TmGantt_Has_Columns_Parameter_With_Defaults()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(MakeTask("1", "T"))));

        // Default columns include Title, Start, End, Progress
        cut.Instance.Columns.Should().NotBeEmpty();
        cut.Instance.Columns.Should().Contain(c => c.Key == GanttColumnKey.Title);
        cut.Instance.Columns.Should().Contain(c => c.Key == GanttColumnKey.Progress);
    }

    [Fact]
    public void Columns_Without_Progress_Does_Not_Render_Progress_Header()
    {
        var cols = new List<GanttColumnDefinition>
        {
            new() { Key = GanttColumnKey.Title, Visible = true, Order = 0 },
            new() { Key = GanttColumnKey.Start, Visible = true, Order = 1 },
            new() { Key = GanttColumnKey.End, Visible = true, Order = 2 },
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.Columns, cols);
        });

        cut.Markup.Should().NotContain("data-testid=\"col-header-progress\"");
    }

    [Fact]
    public void Columns_With_Progress_Renders_Progress_Header()
    {
        var cols = new List<GanttColumnDefinition>
        {
            new() { Key = GanttColumnKey.Title, Visible = true, Order = 0 },
            new() { Key = GanttColumnKey.Progress, Visible = true, Order = 1 },
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.Columns, cols);
        });

        cut.Find("[data-testid='col-header-progress']").Should().NotBeNull();
    }

    [Fact]
    public void OnColumnsChanged_Fires_When_Column_Visibility_Toggled()
    {
        IReadOnlyList<GanttColumnDefinition>? changedCols = null;
        var cols = new List<GanttColumnDefinition>
        {
            new() { Key = GanttColumnKey.Title, Visible = true, Order = 0 },
            new() { Key = GanttColumnKey.Progress, Visible = true, Order = 1 },
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.Columns, cols);
            p.Add(c => c.OnColumnsChanged, (IReadOnlyList<GanttColumnDefinition> c) => changedCols = c);
        });

        // Phase 4: the header checkbox is gone; the column menu (TmColumnPicker) toggles visibility.
        cut.Find("[data-testid='gantt-column-menu'] .tm-column-picker-toggle").Click();
        cut.Find(".tm-column-picker-item input[data-key='Progress']").Change(false);

        changedCols.Should().NotBeNull();
        changedCols!.First(c => c.Key == GanttColumnKey.Progress).Visible.Should().BeFalse();
    }

    [Fact]
    public void Progress_Column_Renders_MiniBar_In_Data_Cell()
    {
        var task = new TmWorkItem { Id = "1", Title = "T", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), PercentComplete = 60 };
        var cols = new List<GanttColumnDefinition>
        {
            new() { Key = GanttColumnKey.Title, Visible = true, Order = 0 },
            new() { Key = GanttColumnKey.Progress, Visible = true, Order = 1 },
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(task));
            p.Add(c => c.Columns, cols);
        });

        cut.Find(".tm-gantt__progress-mini").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.4 – Dependency Creation Drag
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Has_AllowDependencyCreation_Parameter()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.AllowDependencyCreation, true);
        });

        cut.Instance.AllowDependencyCreation.Should().BeTrue();
    }

    [Fact]
    public void Bar_Has_DepHandle_Start_And_End_Elements()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.AllowDependencyCreation, true);
        });

        cut.Find(".tm-gantt__dep-handle--start").Should().NotBeNull();
        cut.Find(".tm-gantt__dep-handle--end").Should().NotBeNull();
    }

    [Fact]
    public void OnDepDrop_Fires_OnDependencyAdded_With_FS_Dependency()
    {
        GanttDependency? added = null;
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "From"), MakeTask("2", "To")));
            p.Add(c => c.AllowDependencyCreation, true);
            p.Add(c => c.OnDependencyAdded, (GanttDependency d) => added = d);
        });

        cut.Instance.StartDepDrag("1", fromEnd: true);
        cut.Instance.OnDepDrop("2");

        added.Should().NotBeNull();
        added!.FromId.Should().Be("1");
        added.ToId.Should().Be("2");
        added.DepType.Should().Be(GanttDependencyType.FinishToStart);
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.5 – Dependency Deletion
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Click_On_Dependency_Line_Selects_It()
    {
        var dep = new GanttDependency { Id = "d1", FromId = "1", ToId = "2" };
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "A"), MakeTask("2", "B")));
            p.Add(c => c.DependencyItems, new[] { dep });
        });

        cut.Find("[data-testid='dep-line-d1']").Click();

        cut.Find("[data-testid='dep-line-d1']").ClassList.Should().Contain("tm-gantt__dep-line--selected");
    }

    [Fact]
    public void Selected_Dependency_Has_Highlighted_CSS_Class()
    {
        var dep = new GanttDependency { Id = "d1", FromId = "1", ToId = "2" };
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "A"), MakeTask("2", "B")));
            p.Add(c => c.DependencyItems, new[] { dep });
        });

        cut.Instance.SelectDependency("d1");
        cut.Render();

        cut.Find("[data-testid='dep-line-d1']").ClassList.Should().Contain("tm-gantt__dep-line--selected");
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.6 – 4 Typy Dependencies + Lag/Lead
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttDependencyType_Enum_Has_Four_Values()
    {
        var values = Enum.GetValues<GanttDependencyType>();
        values.Should().HaveCount(4);
        values.Should().Contain(GanttDependencyType.FinishToStart);
        values.Should().Contain(GanttDependencyType.StartToStart);
        values.Should().Contain(GanttDependencyType.FinishToFinish);
        values.Should().Contain(GanttDependencyType.StartToFinish);
    }

    [Fact]
    public void GanttDependency_Has_DepType_And_LagDays()
    {
        var dep = new GanttDependency();
        dep.DepType.Should().Be(GanttDependencyType.FinishToStart);
        dep.LagDays.Should().Be(0);
    }

    [Fact]
    public void BuildDependencyLines_SS_Uses_Start_Of_FromBar_To_Start_Of_ToBar()
    {
        var dep = new GanttDependency { Id = "d1", FromId = "1", ToId = "2", DepType = GanttDependencyType.StartToStart };
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(
                new TmWorkItem { Id = "1", Title = "A", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 5) },
                new TmWorkItem { Id = "2", Title = "B", Start = new DateTime(2024, 6, 3), End = new DateTime(2024, 6, 10) }
            ));
            p.Add(c => c.DependencyItems, new[] { dep });
        });

        // SS dep line should start at X1 = left of bar A (not right)
        var line = cut.Find("[data-testid='dep-line-d1']");
        var x1 = double.Parse(line.GetAttribute("x1") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);
        var x2 = double.Parse(line.GetAttribute("x2") ?? "0",
            System.Globalization.CultureInfo.InvariantCulture);

        // For SS: x1 should equal left edge of bar A
        x1.Should().BeLessThan(x2); // start of A is before start of B (approx)
    }

    [Fact]
    public void Dependency_With_Lag_Renders_Lag_Label()
    {
        var dep = new GanttDependency { Id = "d1", FromId = "1", ToId = "2", LagDays = 3 };
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(
                new TmWorkItem { Id = "1", Title = "A", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 5) },
                new TmWorkItem { Id = "2", Title = "B", Start = new DateTime(2024, 6, 8), End = new DateTime(2024, 6, 15) }
            ));
            p.Add(c => c.DependencyItems, new[] { dep });
        });

        cut.Find("[data-testid='dep-lag-d1']").TextContent.Should().Contain("3");
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.7 – Assignee Avatary na Baru + Multi-assign
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmWorkItemAssignee_Has_Required_Properties()
    {
        var a = new TmWorkItemAssignee { Id = "u1", Name = "Alice" };
        a.Id.Should().Be("u1");
        a.Name.Should().Be("Alice");
        a.AvatarUrl.Should().BeNull();
    }

    [Fact]
    public void GanttTask_Has_Assignees_Property_Default_Empty()
    {
        var task = new TmWorkItem();
        task.Assignees.Should().NotBeNull();
        task.Assignees.Should().BeEmpty();
    }

    [Fact]
    public void Bar_Renders_Up_To_3_Avatars_Then_Overflow_Badge()
    {
        var task = new TmWorkItem
        {
            Id = "1", Title = "T",
            Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10),
            Assignees = new List<TmWorkItemAssignee>
            {
                new() { Id = "u1", Name = "Alice" },
                new() { Id = "u2", Name = "Bob" },
                new() { Id = "u3", Name = "Carol" },
                new() { Id = "u4", Name = "Dave" },
            }
        };

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(task)));

        var avatars = cut.FindAll(".tm-gantt__bar-avatar");
        avatars.Should().HaveCount(3);

        cut.Find(".tm-gantt__bar-avatar-overflow").TextContent.Should().Contain("+1");
    }

    [Fact]
    public void Bar_Renders_Avatars_When_3_Or_Fewer_Assignees()
    {
        var task = new TmWorkItem
        {
            Id = "1", Title = "T",
            Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10),
            Assignees = new List<TmWorkItemAssignee>
            {
                new() { Id = "u1", Name = "Alice" },
                new() { Id = "u2", Name = "Bob" },
            }
        };

        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(task)));

        cut.FindAll(".tm-gantt__bar-avatar").Should().HaveCount(2);
        cut.FindAll(".tm-gantt__bar-avatar-overflow").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.8 – Task Panel: Chybějící Pole
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttTask_Has_EstimationHours_Property()
    {
        var task = new TmWorkItem();
        task.EstimationHours.Should().BeNull();
    }

    [Fact]
    public void GanttTask_Has_LoggedHours_Property()
    {
        var task = new TmWorkItem();
        task.LoggedHours.Should().BeNull();
    }

    [Fact]
    public void TaskPanel_Title_Is_Editable_Input()
    {
        var task = MakeTask("1", "My Task");
        var cut = Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, task);
            p.Add(c => c.AllTasks, Tasks(task));
            p.Add(c => c.Dependencies, Array.Empty<GanttDependency>());
        });

        var titleInput = cut.Find("[data-testid='task-title'] input");
        titleInput.Should().NotBeNull();
        titleInput.GetAttribute("value").Should().Be("My Task");
    }

    [Fact]
    public void TaskPanel_Renders_Duration_Field()
    {
        var task = new TmWorkItem { Id = "1", Title = "T", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10) };
        var cut = Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, task);
            p.Add(c => c.AllTasks, Tasks(task));
            p.Add(c => c.Dependencies, Array.Empty<GanttDependency>());
        });

        cut.Find("[data-testid='task-duration']").Should().NotBeNull();
    }

    [Fact]
    public void TaskPanel_Renders_Deadline_Date_Picker()
    {
        var task = MakeTask("1", "T");
        var cut = Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, task);
            p.Add(c => c.AllTasks, Tasks(task));
            p.Add(c => c.Dependencies, Array.Empty<GanttDependency>());
        });

        cut.Find("[data-testid='task-deadline']").Should().NotBeNull();
    }

    [Fact]
    public void TaskPanel_Renders_Estimation_And_TimeLog_Fields()
    {
        var task = new TmWorkItem { Id = "1", Title = "T", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), EstimationHours = 40, LoggedHours = 12 };
        var cut = Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, task);
            p.Add(c => c.AllTasks, Tasks(task));
            p.Add(c => c.Dependencies, Array.Empty<GanttDependency>());
        });

        cut.Find("[data-testid='task-estimation']").Should().NotBeNull();
        cut.Find("[data-testid='task-timelog']").Should().NotBeNull();
    }

    [Fact]
    public void TaskPanel_Renders_Color_Picker_With_12_Swatches()
    {
        var task = MakeTask("1", "T");
        var cut = Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, task);
            p.Add(c => c.AllTasks, Tasks(task));
            p.Add(c => c.Dependencies, Array.Empty<GanttDependency>());
        });

        var swatches = cut.FindAll(".tm-gantt__panel-color-swatch");
        swatches.Should().HaveCount(12);
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.9 – Zoom Slider + Dvouřádkový Header
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttZoomPreset_Enum_Has_Six_Values()
    {
        var values = Enum.GetValues<GanttZoomPreset>();
        values.Should().HaveCount(6);
        values.Should().Contain(GanttZoomPreset.Hours);
        values.Should().Contain(GanttZoomPreset.Days);
        values.Should().Contain(GanttZoomPreset.Weeks);
        values.Should().Contain(GanttZoomPreset.Months);
        values.Should().Contain(GanttZoomPreset.Quarters);
        values.Should().Contain(GanttZoomPreset.Years);
    }

    [Fact]
    public void BuildTimelineHeaderRows_Weeks_Returns_Upper_Months_Lower_Weeks()
    {
        var start = new DateTime(2024, 6, 3); // Monday
        var end = new DateTime(2024, 6, 30);
        var result = GanttHelper.BuildTimelineHeaderRows(GanttZoomPreset.Weeks, start, end, 40.0);

        result.Upper.Should().NotBeEmpty();
        result.Lower.Should().NotBeEmpty();
        (result.Upper[0].Label.Contains("Jun") || result.Upper[0].Label.Contains("June") || result.Upper[0].Label.Contains("2024")).Should().BeTrue("upper header should contain month or year info");
    }

    [Fact]
    public void BuildTimelineHeaderRows_Months_Returns_Upper_Year_Lower_Months()
    {
        var start = new DateTime(2024, 1, 1);
        var end = new DateTime(2024, 12, 31);
        var result = GanttHelper.BuildTimelineHeaderRows(GanttZoomPreset.Months, start, end, 40.0);

        result.Upper.Should().NotBeEmpty();
        result.Lower.Should().NotBeEmpty();
        result.Upper[0].Label.Should().Contain("2024");
        result.Lower.Should().HaveCount(12);
    }

    [Fact]
    public void BuildTimelineHeaderRows_Hours_Returns_Upper_Days_Lower_Hours()
    {
        var start = new DateTime(2024, 6, 3);
        var end = new DateTime(2024, 6, 5);
        var result = GanttHelper.BuildTimelineHeaderRows(GanttZoomPreset.Hours, start, end, 40.0);

        result.Upper.Should().NotBeEmpty();
        result.Lower.Should().NotBeEmpty();
        result.Lower.Should().HaveCountGreaterThan(20);
    }

    [Fact]
    public void TmGantt_Has_ZoomPreset_Parameter()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Weeks);
        });

        cut.Instance.ZoomPreset.Should().Be(GanttZoomPreset.Weeks);
    }

    [Fact]
    public void TmGantt_Renders_Zoom_Slider()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(MakeTask("1", "T"))));

        cut.Find("[data-testid='gantt-zoom-slider']").Should().NotBeNull();
    }

    [Fact]
    public void TmGantt_Renders_TwoRow_Timeline_Header()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Weeks);
        });

        cut.Find(".tm-gantt__timeline-header--upper").Should().NotBeNull();
        cut.Find(".tm-gantt__timeline-header--lower").Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.10 – Current Time Indicator
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GetCurrentTimeOffset_Returns_Positive_Offset_During_Working_Hours()
    {
        var dayStart = DateTime.Today.AddHours(8);
        var offset = GanttHelper.GetCurrentTimeOffset(dayStart, pixelPerHour: 60.0);
        // offset >= 0 (current time may be before or during working hours)
        offset.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void CurrentTimeMarker_Renders_Only_In_Hours_View()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Hours);
        });

        cut.Find("[data-testid='current-time-marker']").Should().NotBeNull();
    }

    [Fact]
    public void CurrentTimeMarker_Not_Rendered_In_Weeks_View()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.ZoomPreset, GanttZoomPreset.Weeks);
        });

        cut.FindAll("[data-testid='current-time-marker']").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.11 – Overdue Highlight Toggle
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Has_ShowOverdueHighlight_Parameter()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.ShowOverdueHighlight, true);
        });

        cut.Instance.ShowOverdueHighlight.Should().BeTrue();
    }

    [Fact]
    public void Overdue_TreeRow_Has_Overdue_CSS_Class_When_Enabled()
    {
        var overdueTask = new TmWorkItem
        {
            Id = "1", Title = "Overdue",
            Start = DateTime.Today.AddDays(-10),
            End = DateTime.Today.AddDays(-1),  // ended yesterday → overdue
            PercentComplete = 0
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(overdueTask));
            p.Add(c => c.ShowOverdueHighlight, true);
        });

        cut.Find(".tm-gantt__tree-row--overdue").Should().NotBeNull();
    }

    [Fact]
    public void Overdue_Bar_Has_Overdue_CSS_Class_When_Enabled()
    {
        var overdueTask = new TmWorkItem
        {
            Id = "1", Title = "Overdue",
            Start = DateTime.Today.AddDays(-10),
            End = DateTime.Today.AddDays(-1),
            PercentComplete = 0
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(overdueTask));
            p.Add(c => c.ShowOverdueHighlight, true);
        });

        cut.Find(".tm-gantt__bar--overdue").Should().NotBeNull();
    }

    [Fact]
    public void Overdue_Row_Not_Highlighted_When_ShowOverdueHighlight_False()
    {
        var overdueTask = new TmWorkItem
        {
            Id = "1", Title = "Overdue",
            Start = DateTime.Today.AddDays(-10),
            End = DateTime.Today.AddDays(-1),
            PercentComplete = 0
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(overdueTask));
            p.Add(c => c.ShowOverdueHighlight, false);
        });

        cut.FindAll(".tm-gantt__tree-row--overdue").Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.12 – Cascade Sort
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GanttHelper_CascadeSort_Sorts_By_Start_Per_Level()
    {
        var tasks = new List<TmWorkItem>
        {
            new() { Id = "1", Title = "Root A", Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 15) },
            new() { Id = "2", Title = "Root B", Start = new DateTime(2024, 6, 1),  End = new DateTime(2024, 6, 5) },
        };
        var roots = GanttHelper.BuildTree(tasks);
        var sorted = GanttHelper.CascadeSort(roots.ToList());

        sorted[0].Task.Title.Should().Be("Root B");
        sorted[1].Task.Title.Should().Be("Root A");
    }

    [Fact]
    public void GanttHelper_CascadeSort_Sorts_Children_Within_Parent()
    {
        var tasks = new List<TmWorkItem>
        {
            new() { Id = "1", Title = "Parent", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 30) },
            new() { Id = "2", Title = "Child Late",  Start = new DateTime(2024, 6, 15), End = new DateTime(2024, 6, 20), ParentId = "1" },
            new() { Id = "3", Title = "Child Early", Start = new DateTime(2024, 6, 5),  End = new DateTime(2024, 6, 10), ParentId = "1" },
        };
        var roots = GanttHelper.BuildTree(tasks);
        var sorted = GanttHelper.CascadeSort(roots.ToList());

        sorted[0].Children[0].Task.Title.Should().Be("Child Early");
        sorted[0].Children[1].Task.Title.Should().Be("Child Late");
    }

    [Fact]
    public void Toolbar_Has_CascadeSort_Button()
    {
        var cut = Render<TmGantt>(p => p.Add(c => c.Items, Tasks(MakeTask("1", "T"))));

        cut.Find("[data-testid='gantt-cascade-sort']").Should().NotBeNull();
    }

    [Fact]
    public void CascadeSortAsync_Fires_OnDataSorted()
    {
        IReadOnlyList<TmWorkItem>? sortedData = null;
        var tasks = new List<TmWorkItem>
        {
            new() { Id = "1", Title = "B", Start = new DateTime(2024, 6, 10), End = new DateTime(2024, 6, 15) },
            new() { Id = "2", Title = "A", Start = new DateTime(2024, 6, 1),  End = new DateTime(2024, 6, 5) },
        };

        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, tasks);
            p.Add(c => c.OnDataSorted, (IReadOnlyList<TmWorkItem> d) => sortedData = d);
        });

        cut.Find("[data-testid='gantt-cascade-sort']").Click();

        sortedData.Should().NotBeNull();
        sortedData![0].Title.Should().Be("A");
    }

    // ═══════════════════════════════════════════════════════════════
    // F2.13 – Bulk Operations
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGantt_Has_AllowBulkSelect_Parameter()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.AllowBulkSelect, true);
        });

        cut.Instance.AllowBulkSelect.Should().BeTrue();
    }

    [Fact]
    public void TreeRow_Renders_Checkbox_When_AllowBulkSelect_True()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.AllowBulkSelect, true);
        });

        cut.Find("input[data-testid='bulk-select-1']").Should().NotBeNull();
    }

    [Fact]
    public void TreeRow_Does_Not_Render_Checkbox_When_AllowBulkSelect_False()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T")));
            p.Add(c => c.AllowBulkSelect, false);
        });

        cut.FindAll("input[data-testid='bulk-select-1']").Should().BeEmpty();
    }

    [Fact]
    public void BulkUpdateArgs_Has_Required_Properties()
    {
        var args = new BulkUpdateArgs
        {
            TaskIds = new[] { "1", "2" },
            Status = TmWorkItemStatus.Done
        };

        args.TaskIds.Should().HaveCount(2);
        args.Status.Should().Be(TmWorkItemStatus.Done);
        args.Priority.Should().BeNull();
        args.Color.Should().BeNull();
        args.AssigneeIdsToAdd.Should().BeEmpty();
        args.AssigneeIdsToRemove.Should().BeEmpty();
    }

    [Fact]
    public void Bulk_Toolbar_Visible_When_Tasks_Selected()
    {
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T"), MakeTask("2", "T2")));
            p.Add(c => c.AllowBulkSelect, true);
        });

        cut.Find("input[data-testid='bulk-select-1']").Change(true);
        cut.Find("input[data-testid='bulk-select-2']").Change(true);

        cut.Find(".tm-gantt__bulk-toolbar").Should().NotBeNull();
        cut.Find(".tm-gantt__bulk-toolbar").TextContent.Should().Contain("2");
    }

    [Fact]
    public void OnBulkUpdate_Fires_When_Bulk_Status_Changed()
    {
        BulkUpdateArgs? fired = null;
        var cut = Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, Tasks(MakeTask("1", "T"), MakeTask("2", "T2")));
            p.Add(c => c.AllowBulkSelect, true);
            p.Add(c => c.OnBulkUpdate, (BulkUpdateArgs a) => fired = a);
        });

        cut.Find("input[data-testid='bulk-select-1']").Change(true);
        cut.Find("[data-testid='bulk-status-done']").Click();

        fired.Should().NotBeNull();
        fired!.TaskIds.Should().Contain("1");
        fired.Status.Should().Be(TmWorkItemStatus.Done);
    }
}
