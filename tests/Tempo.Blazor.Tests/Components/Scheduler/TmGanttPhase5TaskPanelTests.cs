using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Models;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Scheduler;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Scheduler;

/// <summary>
/// TmGantt redesign, phase 5: the task panel as an inspector - status / priority / read-only assignees,
/// grouped sections, an Actions section, library numeric inputs, attachment cards and a sticky footer
/// (Cancel | Save). Written BEFORE the implementation.
/// </summary>
public class TmGanttPhase5TaskPanelTests : LocalizationTestBase
{
    private static TmWorkItem Task(string id = "1", string title = "Design") => new()
    {
        Id = id,
        Title = title,
        Start = new DateTime(2024, 6, 1),
        End = new DateTime(2024, 6, 10),
        PercentComplete = 50,
    };

    private IRenderedComponent<TmGanttTaskPanel> RenderPanel(TmWorkItem? task = null, Action<ComponentParameterCollectionBuilder<TmGanttTaskPanel>>? configure = null)
    {
        var item = task ?? Task();
        return Render<TmGanttTaskPanel>(p =>
        {
            p.Add(c => c.Task, item);
            p.Add(c => c.AllTasks, new List<TmWorkItem> { item });
            configure?.Invoke(p);
        });
    }

    // ── Status / priority / assignees ─────────────────────────────────

    [Fact]
    public void TmGanttTaskPanel_Status_Select_Is_A_Draft_Raised_On_Save()
    {
        var raised = new List<(string TaskId, TmWorkItemStatus Status)>();
        TmWorkItem? saved = null;
        var cut = RenderPanel(configure: p => p
            .Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, a => raised.Add(a)))
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => saved = t)));

        cut.Find("[data-testid='task-status'] select").Change(nameof(TmWorkItemStatus.InProgress));

        raised.Should().BeEmpty("like the priority, the status stays in the draft until Save");
        saved.Should().BeNull();

        cut.Find("[data-testid='task-save']").Click();

        raised.Should().ContainSingle().Which.Should().Be(("1", TmWorkItemStatus.InProgress));
        saved!.Status.Should().Be(TmWorkItemStatus.InProgress, "Save keeps the chosen status instead of copying the old one");
    }

    [Fact]
    public void TmGanttTaskPanel_Save_Without_A_Status_Change_Raises_No_OnStatusChanged()
    {
        var raised = 0;
        var cut = RenderPanel(configure: p => p
            .Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, _ => raised++)));

        cut.Find("[data-testid='task-save']").Click();

        raised.Should().Be(0);
    }

    [Fact]
    public void TmGanttTaskPanel_Cancel_Reverts_The_Status_Draft()
    {
        var raised = 0;
        TmWorkItem? saved = null;
        var cut = RenderPanel(configure: p => p
            .Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, _ => raised++))
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => saved = t)));

        cut.Find("[data-testid='task-status'] select").Change(nameof(TmWorkItemStatus.Done));
        cut.Find("[data-testid='task-cancel']").Click();

        cut.Find("[data-testid='task-status'] select option[selected]").GetAttribute("value").Should().Be(nameof(TmWorkItemStatus.Open));
        cut.Find("[data-testid='task-save']").Click();

        raised.Should().Be(0, "the cancelled draft was discarded");
        saved!.Status.Should().Be(TmWorkItemStatus.Open);
    }

    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(3L * 1024 * 1024, "3.0 MB")]
    public void TmGanttTaskPanel_Attachment_Size_Is_Localized_English(long bytes, string expected)
    {
        var task = Task();
        task.Attachments = [new TmAttachment { Id = "a1", FileName = "f.bin", Url = "https://example.test/f", SizeBytes = bytes }];

        RenderPanel(task).Find(".tm-gantt-task-panel__attachment-meta").TextContent.Trim().Should().StartWith(expected);
    }

    [Fact]
    public void TmGanttTaskPanel_Attachment_Size_Uses_The_Localized_Unit_Keys()
    {
        var czech = BuildCzechLocalizer();
        czech["TmGanttTaskPanel_SizeKB", "2,0"].Should().Be("2,0 KB");
        var task = Task();
        task.Attachments = [new TmAttachment { Id = "a1", FileName = "f.bin", Url = "https://example.test/f", SizeBytes = 2048 }];
        var custom = new Dictionary<string, string>
        {
            ["TmGanttTaskPanel_SizeBytes"] = "{0} o",
            ["TmGanttTaskPanel_SizeKB"] = "{0} Ko",
            ["TmGanttTaskPanel_SizeMB"] = "{0} Mo",
        };
        UseCustomLocalization(custom);

        RenderPanel(task).Find(".tm-gantt-task-panel__attachment-meta").TextContent.Trim().Should().StartWith("2.0 Ko");
    }
    [Fact]
    public void TmGanttTaskPanel_Status_Select_Lists_Every_Status_With_Localized_Text()
    {
        var cut = RenderPanel();

        cut.FindAll("[data-testid='task-status'] option").Select(o => o.TextContent.Trim())
            .Should().Equal("Open", "In Progress", "Done", "Closed");
    }

    [Fact]
    public void TmGanttTaskPanel_Priority_Select()
    {
        TmWorkItem? saved = null;
        var cut = RenderPanel(configure: p => p
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => saved = t)));

        cut.Find("[data-testid='task-priority'] select").Change(nameof(TmWorkItemPriority.High));
        cut.Find("[data-testid='task-save']").Click();

        saved!.Priority.Should().Be(TmWorkItemPriority.High);
    }

    [Fact]
    public void TmGanttTaskPanel_Assignees_ReadOnly_Avatars()
    {
        var task = Task();
        task.Assignees =
        [
            new TmWorkItemAssignee { Id = "u1", Name = "Alice Novak" },
            new TmWorkItemAssignee { Id = "u2", Name = "Bob Stone" },
        ];
        var cut = RenderPanel(task);

        var section = cut.Find("[data-testid='task-assignees']");
        section.QuerySelectorAll(".tm-avatar").Length.Should().Be(2, "one avatar per assignee");
        section.TextContent.Should().Contain("Alice Novak").And.Contain("Bob Stone");
        section.QuerySelectorAll("input, select, textarea, button").Length.Should().Be(0, "assignees are display-only here");
    }

    [Fact]
    public void TmGanttTaskPanel_Assignees_Empty_Shows_Neutral_Text()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='task-assignees']").TextContent.Should().Contain("No assignees");
    }

    // ── Footer and actions ────────────────────────────────────────────

    [Fact]
    public void TmGanttTaskPanel_Actions_Footer_Outside_Scroll()
    {
        var cut = RenderPanel();

        cut.FindAll(".tm-gantt-task-panel > .tm-gantt-task-panel__actions").Count.Should().Be(1, "the footer is a direct child of the panel");
        cut.FindAll(".tm-gantt-task-panel__scroll .tm-gantt-task-panel__actions").Should().BeEmpty("the footer never scrolls with the fields");

        var buttons = cut.FindAll(".tm-gantt-task-panel__actions button");
        buttons.Select(b => b.GetAttribute("data-testid")).Should().Equal(
            ["task-cancel", "task-save"], "Cancel first, the primary action last (right)");
        buttons[1].ClassList.Should().Contain("tm-btn-primary");
        buttons[0].ClassList.Should().NotContain("tm-btn-primary");
    }

    [Fact]
    public void TmGanttTaskPanel_Actions_Section_MarkDone_AddAbove_AddBelow_AddChild_Duplicate_Delete()
    {
        var raised = new List<GanttTaskAction>();
        var cut = RenderPanel(configure: p => p
            .Add(c => c.OnAction, EventCallback.Factory.Create<GanttTaskAction>(this, a => raised.Add(a))));

        cut.FindAll(".tm-gantt-task-panel__scroll [data-testid='task-section-actions']").Count.Should().Be(1);

        var map = new (string Id, GanttTaskAction Action)[]
        {
            ("task-action-mark-done", GanttTaskAction.MarkDone),
            ("task-action-add-above", GanttTaskAction.AddAbove),
            ("task-action-add-below", GanttTaskAction.AddBelow),
            ("task-action-add-child", GanttTaskAction.AddChild),
            ("task-action-duplicate", GanttTaskAction.Duplicate),
            ("task-action-delete", GanttTaskAction.Delete),
        };
        foreach (var (id, _) in map) cut.Find($"[data-testid='{id}']").Click();

        raised.Should().Equal(map.Select(m => m.Action));
    }

    [Fact]
    public void TmGanttTaskPanel_Actions_Section_Is_Hidden_Without_A_Handler()
    {
        var cut = RenderPanel();

        cut.FindAll("[data-testid='task-section-actions']").Should().BeEmpty("an action without a handler is dead UI");
    }

    // ── Library inputs ────────────────────────────────────────────────

    [Fact]
    public void TmGanttTaskPanel_Uses_Library_Numeric_Inputs_For_Estimate_And_Logged()
    {
        // TmNumberInput is integer-only; hours carry halves (step 0.5), so the decimal sibling of the same
        // family (TmDecimalInput) replaces the raw <input type=number class=tm-input>.
        var task = Task();
        task.EstimationHours = 12.5;
        task.LoggedHours = 3;
        TmWorkItem? saved = null;
        var cut = RenderPanel(task, p => p
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => saved = t)));

        foreach (var id in new[] { "task-estimation", "task-timelog" })
        {
            var field = cut.Find($"[data-testid='{id}']");
            field.QuerySelectorAll("input.tm-input").Length.Should().Be(0, $"{id} no longer uses a raw input");
            field.QuerySelectorAll(".tm-decimal-input").Length.Should().Be(1, $"{id} uses the library numeric input");
        }

        cut.Find("[data-testid='task-estimation'] input").GetAttribute("value").Should().Be("12.5");
        cut.Find("[data-testid='task-estimation'] input").Change("7,5");
        cut.Find("[data-testid='task-save']").Click();

        saved!.EstimationHours.Should().Be(7.5);
        saved.LoggedHours.Should().Be(3);
    }

    [Fact]
    public void TmGanttTaskPanel_Percent_Has_Percent_Suffix()
    {
        var cut = RenderPanel();

        cut.Find("[data-testid='task-percent'] .tm-number-input__suffix").TextContent.Trim().Should().Be("%");
    }

    [Fact]
    public void TmGanttTaskPanel_Duration_ReadOnly_Input()
    {
        var cut = RenderPanel();

        var input = cut.Find("[data-testid='task-duration'] input");
        input.HasAttribute("readonly").Should().BeTrue();
        input.GetAttribute("value").Should().Be("9 days");
    }

    // ── Attachments ───────────────────────────────────────────────────

    [Fact]
    public void TmGanttTaskPanel_Attachments_As_Cards()
    {
        var task = Task();
        task.Attachments =
        [
            new TmAttachment { Id = "a1", FileName = "spec.pdf", Url = "https://example.test/spec.pdf", SizeBytes = 2048, UploadedAt = new DateTimeOffset(2024, 4, 12, 9, 0, 0, TimeSpan.Zero) },
            new TmAttachment { Id = "a2", FileName = "notes.txt", Url = "https://example.test/notes.txt" },
        ];
        var cut = RenderPanel(task);

        var cards = cut.FindAll(".tm-gantt-task-panel__attachment-card");
        cards.Count.Should().Be(2);
        cards[0].QuerySelector(".tm-gantt-task-panel__attachment-icon svg").Should().NotBeNull();
        cards[0].QuerySelector("a.tm-gantt-task-panel__attachment-link")!.TextContent.Trim().Should().Be("spec.pdf");
        cards[0].QuerySelector(".tm-gantt-task-panel__attachment-meta")!.TextContent.Should().Contain("2.0 KB");
        cards[1].QuerySelector(".tm-gantt-task-panel__attachment-meta")!.TextContent.Should().NotContain("KB").And.NotContain("MB",
            "a size of 0 means the model carries none - only the name is shown");
        cut.Find("[data-testid='attachment-remove-a1']").Should().NotBeNull();
    }

    // ── Sections ──────────────────────────────────────────────────────

    private static readonly string[] SectionOrder =
    [
        "plan", "progress", "structure", "appearance", "description", "attachments", "comments", "budget", "timelog", "dependencies",
    ];

    [Fact]
    public void TmGanttTaskPanel_Sections_Grouped()
    {
        var cut = RenderPanel(configure: p => p.Add(c => c.OnAction, EventCallback.Factory.Create<GanttTaskAction>(this, _ => { })));

        var sections = cut.FindAll(".tm-gantt-task-panel__scroll > section.tm-gantt-task-panel__group");
        sections.Select(s => s.GetAttribute("data-testid")).Should().Equal(
            SectionOrder.Select(s => "task-section-" + s).Append("task-section-actions"));
        sections.Should().OnlyContain(s => s.QuerySelector("h3.tm-gantt-task-panel__group-title") != null, "every group has a heading");

        cut.FindAll("h3.tm-gantt-task-panel__group-title").Select(h => h.TextContent.Trim()).Should().Equal(
            "Plan", "Progress", "Structure", "Appearance", "Description", "Attachments", "Comments", "Budget & Cost", "Time Log", "Dependencies", "Actions");
    }

    [Theory]
    [InlineData("plan", new[] { "task-title", "task-start", "task-end", "task-duration", "task-deadline" })]
    [InlineData("progress", new[] { "task-status", "task-priority", "task-percent", "task-estimation", "task-timelog", "task-assignees" })]
    [InlineData("structure", new[] { "task-milestone", "task-parent" })]
    [InlineData("appearance", new[] { "task-color-picker" })]
    [InlineData("description", new[] { "task-description" })]
    [InlineData("attachments", new[] { "attachment-list", "attachment-upload" })]
    [InlineData("comments", new[] { "comment-list" })]
    [InlineData("budget", new[] { "task-budget-section", "task-budget", "task-actual-cost" })]
    [InlineData("timelog", new[] { "task-timelog-section", "task-timelog-total" })]
    [InlineData("dependencies", new[] { "task-dependencies", "task-add-dep" })]
    public void TmGanttTaskPanel_Section_Keeps_Its_Fields(string section, string[] testIds)
    {
        var cut = RenderPanel();

        var group = cut.Find($"[data-testid='task-section-{section}']");
        foreach (var id in testIds)
        {
            group.QuerySelector($"[data-testid='{id}']").Should().NotBeNull($"'{id}' stays in the {section} section - nothing was deleted");
        }
    }

    [Fact]
    public void TmGanttTaskPanel_ShowsOverload_And_BaselineDeviation_ReadOnly()
    {
        var cut = RenderPanel(configure: p => p
            .Add(c => c.IsOverloaded, true)
            .Add(c => c.BaselineTask, new GanttBaselineTask("1", new DateTime(2024, 5, 30), new DateTime(2024, 6, 12))));

        var overload = cut.Find("[data-testid='task-overload']");
        overload.TextContent.Should().Contain("Overloaded");
        overload.QuerySelectorAll("input, select, button").Length.Should().Be(0);

        var deviation = cut.Find("[data-testid='task-baseline-deviation']");
        deviation.TextContent.Should().Contain("Start +2 d").And.Contain("End -2 d");
        deviation.QuerySelectorAll("input, select, button").Length.Should().Be(0);
    }

    [Fact]
    public void TmGanttTaskPanel_Hides_Overload_And_Baseline_When_There_Is_Nothing_To_Show()
    {
        var cut = RenderPanel();

        cut.FindAll("[data-testid='task-overload']").Should().BeEmpty();
        cut.FindAll("[data-testid='task-baseline-deviation']").Should().BeEmpty();
    }

    [Fact]
    public void TmGanttTaskPanel_Renders_The_Optional_Breadcrumb_In_The_Header()
    {
        var cut = RenderPanel(configure: p => p.Add(c => c.Breadcrumb, (RenderFragment)(b => b.AddMarkupContent(0, "<span class=\"crumb\">Project / Design</span>"))));

        cut.Find(".tm-gantt-task-panel__header .crumb").TextContent.Should().Be("Project / Design");
    }

    // ── Swatches (stylesheet) ─────────────────────────────────────────

    /// <summary>
    /// UX review finding (1440 shot): a four-column Plan row left each date input ~85px, clipping "07.10.2026"
    /// to "07.10."; the Percent stepper squeezed its number to nothing. Dates get three columns at most and the
    /// percent field spans two so its value stays readable in the 420px panel.
    /// </summary>
    [Fact]
    public void TmGanttTaskPanel_Date_Row_Has_Three_Columns_And_Percent_Keeps_Room()
    {
        const string file = "TmGanttTaskPanel.razor.css";
        GanttCss.TryProperty(file, ".tm-gantt-task-panel__grid--four", "grid-template-columns").Should().BeNull("no four-column row in a 420px panel");
        GanttCss.Property(file, ".tm-gantt-task-panel__grid", "grid-template-columns").Should().Contain("repeat(3");

        var cut = RenderPanel();
        cut.Find("[data-testid='task-percent']").ParentElement!.ClassList.Should().Contain("tm-gantt-task-panel__cell--span-2");
        cut.Find("[data-testid='task-deadline']").ParentElement!.ClassList.Should().Contain("tm-gantt-task-panel__cell--span-2");
    }
    [Fact]
    public void TmGanttTaskPanel_Swatches_Are_32px_With_A_Ring_When_Selected()
    {
        const string file = "TmGanttTaskPanel.razor.css";
        GanttCss.Property(file, ".tm-gantt__panel-color-swatch", "width").Should().Be("var(--tm-space-8)");
        GanttCss.Property(file, ".tm-gantt__panel-color-swatch", "height").Should().Be("var(--tm-space-8)");
        GanttCss.Property(file, ".tm-gantt__panel-color-swatch--selected", "box-shadow").Should().Contain("var(--tm-color-primary)");
    }

    // ── TmGantt wiring ────────────────────────────────────────────────

    private IRenderedComponent<TmGantt> RenderGantt(Action<ComponentParameterCollectionBuilder<TmGantt>>? configure = null)
    {
        var first = Task("1", "Design");
        var second = Task("2", "Build");
        second.Start = new DateTime(2024, 6, 11);
        second.End = new DateTime(2024, 6, 20);
        return Render<TmGantt>(p =>
        {
            p.Add(c => c.Items, new List<TmWorkItem> { first, second });
            p.Add(c => c.SelectedTask, first);
            p.Add(c => c.ShowTaskPanel, true);
            configure?.Invoke(p);
        });
    }

    [Fact]
    public void TmGantt_TaskPanel_Status_Is_Applied_And_Raised_On_Save()
    {
        (string TaskId, TmWorkItemStatus Status)? raised = null;
        var cut = RenderGantt(p => p.Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, a => raised = a)));

        cut.Find("[data-testid='task-status'] select").Change(nameof(TmWorkItemStatus.InProgress));
        raised.Should().BeNull("the status is a draft until Save");
        cut.Instance.SelectedTask!.Status.Should().Be(TmWorkItemStatus.Open);

        cut.Find("[data-testid='task-save']").Click();

        raised.Should().NotBeNull();
        raised!.Value.Should().Be(("1", TmWorkItemStatus.InProgress));
        cut.Instance.SelectedTask!.Status.Should().Be(TmWorkItemStatus.InProgress, "the board and the panel change a status the same way");
    }

    [Fact]
    public void TmGantt_TaskPanel_Delete_Asks_For_Confirmation_Before_Removing()
    {
        var removed = new List<string>();
        var cut = RenderGantt(p => p.Add(c => c.OnTaskRemoved, EventCallback.Factory.Create<string>(this, id => removed.Add(id))));

        cut.FindAll(".tm-dialog").Should().BeEmpty();
        cut.Find("[data-testid='task-action-delete']").Click();

        removed.Should().BeEmpty("nothing is removed before the confirmation");
        cut.Find(".tm-dialog-title").TextContent.Trim().Should().Be("Delete this task?");
        cut.Find(".tm-dialog-message").TextContent.Should().Contain("Design");

        cut.Find(".tm-dialog-btn-cancel").Click();
        removed.Should().BeEmpty();
        cut.FindAll(".tm-dialog").Should().BeEmpty("cancel dismisses the dialog");
        cut.Instance.SelectedTask.Should().NotBeNull("a cancelled delete keeps the selection");

        cut.Find("[data-testid='task-action-delete']").Click();
        cut.Find(".tm-dialog-btn-ok").Click();

        removed.Should().ContainSingle().Which.Should().Be("1");
        cut.FindAll(".tm-dialog").Should().BeEmpty();
        cut.Instance.SelectedTask.Should().BeNull();
    }
    [Fact]
    public void TmGantt_TaskPanel_Actions_Reuse_The_Context_Menu_Handlers()
    {
        var inserted = new List<GanttTaskInsertedArgs>();
        var added = new List<TmWorkItem>();
        var removed = new List<string>();
        var updated = new List<TmWorkItem>();
        var statuses = new List<(string, TmWorkItemStatus)>();
        var cut = RenderGantt(p => p
            .Add(c => c.OnTaskInserted, EventCallback.Factory.Create<GanttTaskInsertedArgs>(this, a => inserted.Add(a)))
            .Add(c => c.OnTaskAdded, EventCallback.Factory.Create<TmWorkItem>(this, t => added.Add(t)))
            .Add(c => c.OnTaskRemoved, EventCallback.Factory.Create<string>(this, id => removed.Add(id)))
            .Add(c => c.OnTaskUpdated, EventCallback.Factory.Create<TmWorkItem>(this, t => updated.Add(t)))
            .Add(c => c.OnStatusChanged, EventCallback.Factory.Create<(string, TmWorkItemStatus)>(this, a => statuses.Add(a))));

        cut.Find("[data-testid='task-action-add-below']").Click();
        inserted.Should().ContainSingle().Which.Should().Match<GanttTaskInsertedArgs>(a => a.Position == GanttInsertPosition.Below && a.ReferenceTaskId == "1");

        // the new task became the selection; go back to the first one through the panel's own host state
        cut.Render(p => p.Add(c => c.SelectedTask, cut.Instance.Items[0]));
        cut.Find("[data-testid='task-action-add-child']").Click();
        inserted.Last().Position.Should().Be(GanttInsertPosition.Child);

        cut.Render(p => p.Add(c => c.SelectedTask, cut.Instance.Items[0]));
        cut.Find("[data-testid='task-action-add-above']").Click();
        inserted.Last().Position.Should().Be(GanttInsertPosition.Above);

        cut.Render(p => p.Add(c => c.SelectedTask, cut.Instance.Items[0]));
        cut.Find("[data-testid='task-action-duplicate']").Click();
        added.Last().Title.Should().Be("Design (copy)", "the duplicate is a new task named after the original");
        added.Last().Id.Should().NotBe("1");
        added.Last().Start.Should().Be(new DateTime(2024, 6, 1));
        added.Last().PercentComplete.Should().Be(0);

        cut.Render(p => p.Add(c => c.SelectedTask, cut.Instance.Items[0]));
        cut.Find("[data-testid='task-action-mark-done']").Click();
        statuses.Should().Contain(("1", TmWorkItemStatus.Done));
        updated.Should().Contain(t => t.Id == "1" && t.PercentComplete == 100 && t.Status == TmWorkItemStatus.Done);

        cut.Render(p => p.Add(c => c.SelectedTask, cut.Instance.Items[0]));
        cut.Find("[data-testid='task-action-delete']").Click();
        removed.Should().BeEmpty("the delete is confirmed first");
        cut.Find(".tm-dialog-btn-ok").Click();
        removed.Should().Contain("1");
    }

    [Fact]
    public void TmGantt_ContextMenu_Duplicate_Creates_A_Copy()
    {
        var added = new List<TmWorkItem>();
        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, new List<TmWorkItem> { Task("1", "Design") })
            .Add(c => c.ViewSettings, new GanttViewSettings { ShowAdvancedContextButtons = true })
            .Add(c => c.OnTaskAdded, EventCallback.Factory.Create<TmWorkItem>(this, t => added.Add(t))));

        cut.Find(".tm-gantt__tree-row").ContextMenu();
        cut.FindAll(".tm-gantt__context-menu-item").Single(b => b.TextContent.Contains("Duplicate")).Click();

        added.Should().ContainSingle().Which.Title.Should().Be("Design (copy)");
    }

    [Fact]
    public void TmGantt_TaskPanel_Gets_Overload_And_Baseline_From_The_Gantt()
    {
        // two one-day tasks for the same person on the same Monday: 2 x 8h against an 8h day -> over capacity
        var first = Task("1", "Design");
        first.Start = first.End = new DateTime(2024, 6, 3);
        first.Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "Alice" }];
        var second = Task("2", "Build");
        second.Start = second.End = new DateTime(2024, 6, 3);
        second.Assignees = [new TmWorkItemAssignee { Id = "u1", Name = "Alice" }];

        var cut = Render<TmGantt>(p => p
            .Add(c => c.Items, new List<TmWorkItem> { first, second })
            .Add(c => c.SelectedTask, first)
            .Add(c => c.ShowTaskPanel, true)
            .Add(c => c.ShowWorkloadPanel, true)
            .Add(c => c.Baselines, new[] { new GanttBaseline { Id = "b1", Name = "B", Tasks = [new GanttBaselineTask("1", new DateTime(2024, 6, 1), new DateTime(2024, 6, 3))] } })
            .Add(c => c.ActiveBaselineId, "b1"));

        cut.Find("[data-testid='task-overload']").Should().NotBeNull();
        cut.Find("[data-testid='task-baseline-deviation']").TextContent.Should().Contain("Start +2 d");
    }

    [Fact]
    public void TmGanttIcons_Wallet_And_Flag_Resolve_As_Built_Ins()
    {
        foreach (var name in new[] { IconNames.Wallet, IconNames.Flag })
        {
            Render<TmIcon>(p => p.Add(c => c.Name, name)).FindAll(".tm-icon-unknown").Should().BeEmpty(name);
        }
    }
}
