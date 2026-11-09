using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

public class TmGanttTaskPanelTests : LocalizationTestBase
{
    private static TmWorkItem CreateTask() => new()
    {
        Id = "1",
        Title = "Design",
        Start = new DateTime(2024, 6, 1),
        End = new DateTime(2024, 6, 10),
        PercentComplete = 50,
        ParentId = null
    };

    private static List<TmWorkItem> AllTasksWithTwo => new()
    {
        new() { Id = "1", Title = "Design", Start = new DateTime(2024, 6, 1), End = new DateTime(2024, 6, 10), PercentComplete = 50 },
        new() { Id = "2", Title = "Dev", Start = new DateTime(2024, 6, 11), End = new DateTime(2024, 6, 20), PercentComplete = 0 }
    };

    /// <summary>
    /// Panel renders all editable fields when a task is provided.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_Renders_Fields_WhenTaskProvided()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        cut.Find(".tm-gantt-task-panel").Should().NotBeNull();
        cut.Find("[data-testid='task-title']").Should().NotBeNull();
        cut.Find("[data-testid='task-start']").Should().NotBeNull();
        cut.Find("[data-testid='task-end']").Should().NotBeNull();
        cut.Find("[data-testid='task-percent']").Should().NotBeNull();
        cut.Find("[data-testid='task-milestone']").Should().NotBeNull();
    }

    /// <summary>
    /// Panel renders existing dependencies for the selected task.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_Renders_Dependencies_WhenProvided()
    {
        var deps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "2", ToId = "1", Type = 0 }
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, deps));

        cut.Find("[data-testid='task-dependencies']").Should().NotBeNull();
        cut.FindAll("[data-testid='task-dep-item']").Count.Should().Be(1);
    }

    /// <summary>
    /// Clicking the remove button and confirming invokes OnDependencyRemoved.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_RemovesDependency_OnDelete()
    {
        string? removedId = null;
        var deps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "2", ToId = "1", Type = 0 }
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, deps)
            .Add(c => c.OnDependencyRemoved, id => removedId = id));

        var removeBtn = cut.Find("button[data-testid='task-dep-remove-d1']");
        removeBtn.Click();

        var yesBtn = cut.Find("button[data-testid='task-dep-confirm-yes']");
        yesBtn.Click();

        removedId.Should().Be("d1");
    }

    /// <summary>
    /// Adding a new dependency invokes OnDependencyAdded with correct data.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_AddsDependency_OnAdd()
    {
        GanttDependency? added = null;
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, new List<GanttDependency>())
            .Add(c => c.OnDependencyAdded, d => added = d));

        // Click add dependency to show the add form
        var addBtn = cut.Find("button[data-testid='task-add-dep']");
        addBtn.Click();

        // Select "From" task
        var fromSelect = cut.Find("[data-testid='task-add-dep-from'] select");
        fromSelect.Change("2");

        // Select dependency type
        var typeSelect = cut.Find("[data-testid='task-add-dep-type'] select");
        typeSelect.Change("1"); // Start-Start

        // Confirm
        var confirmBtn = cut.Find("button[data-testid='task-add-dep-confirm']");
        confirmBtn.Click();

        added.Should().NotBeNull();
        added!.FromId.Should().Be("2");
        added.ToId.Should().Be("1");
        added.Type.Should().Be(1);
    }

    /// <summary>
    /// Adding a duplicate dependency shows an error and does not invoke OnDependencyAdded.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_AddsDependency_Duplicate_ShowsError()
    {
        GanttDependency? added = null;
        var existingDeps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "2", ToId = "1", Type = 0 }
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, existingDeps)
            .Add(c => c.OnDependencyAdded, d => added = d));

        var addBtn = cut.Find("button[data-testid='task-add-dep']");
        addBtn.Click();

        var fromSelect = cut.Find("[data-testid='task-add-dep-from'] select");
        fromSelect.Change("2");

        var typeSelect = cut.Find("[data-testid='task-add-dep-type'] select");
        typeSelect.Change("0"); // same FS type as existing

        var confirmBtn = cut.Find("button[data-testid='task-add-dep-confirm']");
        confirmBtn.Click();

        added.Should().BeNull("duplicate dependency should not be added");
        cut.Find("[data-testid='task-dependency-error']").TextContent.Should().Contain("already exists");
    }

    /// <summary>
    /// Adding a dependency that would create a cycle shows an error.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_AddsDependency_Cycle_ShowsError()
    {
        GanttDependency? added = null;
        // Task 1 depends on Task 2. Adding dependency from 1 to 2 would be fine.
        // But if Task 2 already depends on Task 1, then adding 1->2 creates a cycle.
        var existingDeps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "1", ToId = "2", Type = 0 } // 1 -> 2
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask()) // Task "1" is selected
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, existingDeps)
            .Add(c => c.OnDependencyAdded, d => added = d));

        var addBtn = cut.Find("button[data-testid='task-add-dep']");
        addBtn.Click();

        // Try to add 2 -> 1 (which would create cycle 1 -> 2 -> 1)
        var fromSelect = cut.Find("[data-testid='task-add-dep-from'] select");
        fromSelect.Change("2");

        var confirmBtn = cut.Find("button[data-testid='task-add-dep-confirm']");
        confirmBtn.Click();

        added.Should().BeNull("cyclic dependency should not be added");
        cut.Find("[data-testid='task-dependency-error']").TextContent.Should().Contain("cyclic");
    }

    /// <summary>
    /// Removing a dependency requires confirmation.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_RemovesDependency_AfterConfirm()
    {
        string? removedId = null;
        var deps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "2", ToId = "1", Type = 0 }
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, deps)
            .Add(c => c.OnDependencyRemoved, id => removedId = id));

        // Click remove → should show confirm prompt instead of removing immediately
        var removeBtn = cut.Find("button[data-testid='task-dep-remove-d1']");
        removeBtn.Click();

        removedId.Should().BeNull("should not remove before confirmation");
        cut.Find("button[data-testid='task-dep-confirm-yes']").Should().NotBeNull();

        // Confirm
        var yesBtn = cut.Find("button[data-testid='task-dep-confirm-yes']");
        yesBtn.Click();

        removedId.Should().Be("d1");
    }

    /// <summary>
    /// Cancelling dependency removal keeps the dependency.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_CancelRemove_KeepsDependency()
    {
        string? removedId = null;
        var deps = new List<GanttDependency>
        {
            new() { Id = "d1", FromId = "2", ToId = "1", Type = 0 }
        };
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, AllTasksWithTwo)
            .Add(c => c.Dependencies, deps)
            .Add(c => c.OnDependencyRemoved, id => removedId = id));

        var removeBtn = cut.Find("button[data-testid='task-dep-remove-d1']");
        removeBtn.Click();

        var noBtn = cut.Find("button[data-testid='task-dep-confirm-no']");
        noBtn.Click();

        removedId.Should().BeNull("cancel should not remove dependency");
        cut.Find("button[data-testid='task-dep-remove-d1']").Should().NotBeNull();
    }

    /// <summary>
    /// Panel does not render when no task is provided.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_DoesNotRender_WhenNoTask()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        cut.Nodes.Should().BeEmpty("panel should not render when Task is null");
    }

    /// <summary>
    /// Changing title and clicking Save invokes OnTaskUpdated with the modified task.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_UpdatesTask_OnSave()
    {
        TmWorkItem? updated = null;
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.OnTaskUpdated, t => updated = t));

        // Change title
        var titleInput = cut.Find("[data-testid='task-title'] input");
        titleInput.Input("New Title");

        // Click save
        var saveBtn = cut.Find("button[data-testid='task-save']");
        saveBtn.Click();

        updated.Should().NotBeNull("Save should invoke OnTaskUpdated");
        updated!.Title.Should().Be("New Title");
    }

    /// <summary>
    /// Validation error appears when Start date is after End date, and Save is disabled.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_Validation_StartAfterEnd_ShowsError()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        // Simulate setting End before Start via the date inputs
        // We use the date inputs directly (native HTML date inputs rendered by TmDatePicker or plain input)
        var startInput = cut.Find("[data-testid='task-start'] input");
        var endInput = cut.Find("[data-testid='task-end'] input");

        startInput.Change("2024-06-15");
        endInput.Change("2024-06-05");

        // Trigger validation by clicking save
        var saveBtn = cut.Find("button[data-testid='task-save']");
        saveBtn.Click();

        // Error message should be visible
        var error = cut.Find("[data-testid='task-validation-error']");
        error.TextContent.Should().Contain("Start", "validation error should mention start/end problem");

        // OnTaskUpdated should NOT have been invoked
        var updated = false;
        cut.Render(p => p.Add(c => c.OnTaskUpdated, _ => updated = true));
        saveBtn.Click();
        updated.Should().BeFalse("Save should not invoke OnTaskUpdated when validation fails");
    }

    /// <summary>
    /// Clicking Cancel resets the title back to the original value.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_Cancel_ResetsChanges()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        var titleInput = cut.Find("[data-testid='task-title'] input");
        titleInput.Input("Changed Title");

        var cancelBtn = cut.Find("button[data-testid='task-cancel']");
        cancelBtn.Click();

        titleInput.GetAttribute("value").Should().Be("Design", "Cancel should reset title to original");
    }

    // ═══════════════════════════════════════════════════════════════
    // UT-5.9.3 – Budget & Cost section (PANEL-5.9.1)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttTaskPanel_Renders_Budget_And_ActualCost_Fields()
    {
        var task = CreateTask();
        task.BudgetHours = 40.0;
        task.ActualCost  = 1200.50m;

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, task)
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        cut.Find("[data-testid='task-budget']").Should().NotBeNull();
        cut.Find("[data-testid='task-actual-cost']").Should().NotBeNull();
    }

    [Fact]
    public void TmGanttTaskPanel_Save_Propagates_BudgetHours_And_ActualCost()
    {
        TmWorkItem? saved = null;
        var task = CreateTask();
        task.BudgetHours = 40.0;
        task.ActualCost  = 500m;

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, task)
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => saved = t)));

        cut.Find("[data-testid='task-budget'] input").Change("80");
        cut.Find("[data-testid='task-save']").Click();

        saved.Should().NotBeNull();
        saved!.BudgetHours.Should().Be(80.0);
    }

    // ═══════════════════════════════════════════════════════════════
    // PANEL-5.12.1 + RAZOR-5.12.1 – Time log section & timer button
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TmGanttTaskPanel_Renders_TimeLog_Section()
    {
        var task = CreateTask();
        task.TimeLog.Add(new GanttTimeLogEntry
        {
            Id         = "e1",
            TaskId     = "1",
            AssigneeId = "u1",
            StartedAt  = new DateTime(2024, 1, 1, 9, 0, 0),
            StoppedAt  = new DateTime(2024, 1, 1, 11, 0, 0),
            Notes      = "Morning work"
        });

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, task)
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        cut.Find("[data-testid='task-timelog-section']").Should().NotBeNull();
        cut.FindAll("[data-testid^='timelog-entry-']").Should().HaveCount(1);
    }

    [Fact]
    public void TmGanttTaskPanel_Shows_TotalLogged_Hours()
    {
        var task = CreateTask();
        task.TimeLog.Add(new GanttTimeLogEntry
        {
            TaskId    = "1",
            StartedAt = new DateTime(2024, 1, 1, 9, 0, 0),
            StoppedAt = new DateTime(2024, 1, 1, 11, 0, 0)
        });

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, task)
            .Add(c => c.AllTasks, new List<TmWorkItem>()));

        cut.Find("[data-testid='task-timelog-total']").TextContent.Should().Contain("2");
    }

    [Fact]
    public void TmGanttTaskPanel_Shows_StartTimer_Button_When_No_Active_Timer()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.ActiveTimerTaskId, (string?)null));

        cut.Find("[data-testid='timer-start-btn']").Should().NotBeNull();
        cut.FindAll("[data-testid='timer-stop-btn']").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttTaskPanel_Shows_StopTimer_Button_When_This_Task_Is_Active()
    {
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())          // Task.Id == "1"
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.ActiveTimerTaskId, "1"));

        cut.Find("[data-testid='timer-stop-btn']").Should().NotBeNull();
        cut.FindAll("[data-testid='timer-start-btn']").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttTaskPanel_StartTimer_Fires_OnTimerStarted_With_TaskId()
    {
        string? firedId = null;
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.ActiveTimerTaskId, (string?)null)
            .Add(c => c.OnTimerStarted, EventCallback.Factory.Create<string>(this, id => firedId = id)));

        cut.Find("[data-testid='timer-start-btn']").Click();

        firedId.Should().Be("1");
    }

    [Fact]
    public void TmGanttTaskPanel_StopTimer_Fires_OnTimerStopped_With_Entry()
    {
        (string TaskId, GanttTimeLogEntry Entry)? fired = null;
        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.ActiveTimerTaskId, "1")
            .Add(c => c.OnTimerStopped, EventCallback.Factory.Create<(string, GanttTimeLogEntry)>(this, e => fired = e)));

        cut.Find("[data-testid='timer-stop-btn']").Click();

        fired.Should().NotBeNull();
        fired!.Value.TaskId.Should().Be("1");
        fired!.Value.Entry.StoppedAt.Should().NotBeNull();
    }

    /// <summary>
    /// The panel forwards the new save handlers to the embedded TmActivityComments,
    /// the same way it forwards OnCommentSubmitted (additive pass-through).
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_ForwardsCommentHandlers()
    {
        Func<TmCommentSubmission, Task<bool>> submitHandler = _ => Task.FromResult(true);
        Func<TmCommentEdit, Task<bool>> editHandler = _ => Task.FromResult(true);

        var cut = Render<TmGanttTaskPanel>(p => p
            .Add(c => c.Task, CreateTask())
            .Add(c => c.AllTasks, new List<TmWorkItem>())
            .Add(c => c.CommentSubmitHandler, submitHandler)
            .Add(c => c.CommentEditHandler, editHandler));

        var comments = cut.FindComponent<Tempo.Blazor.Components.Activity.TmActivityComments>();
        comments.Instance.CommentSubmitHandler.Should().BeSameAs(submitHandler);
        comments.Instance.CommentEditHandler.Should().BeSameAs(editHandler);
    }
}
