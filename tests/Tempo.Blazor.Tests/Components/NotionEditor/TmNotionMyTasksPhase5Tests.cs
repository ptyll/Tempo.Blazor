using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Phase 5 (P6): "My tasks" per the reference — the four filter groups are real
/// <c>fieldset/legend</c> groups with visible labels, the completion checkbox names the task,
/// due-date groups collapse (<c>aria-expanded</c>), and each row shows the assignee avatar,
/// the page and the due date.
/// </summary>
public sealed class TmNotionMyTasksPhase5Tests : LocalizationTestBase
{
    public TmNotionMyTasksPhase5Tests()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["Tm_Close"] = "Close",
            ["Tm_Loading"] = "Loading",
            ["Tm_Retry"] = "Retry",
            ["Notion_Tasks_Title"] = "My Tasks",
            ["Notion_Tasks_Count"] = "{0} tasks",
            ["Notion_Tasks_Filters"] = "Task filters",
            ["Notion_Tasks_Filter_Assignee"] = "Assignee filter",
            ["Notion_Tasks_Filter_My"] = "Mine",
            ["Notion_Tasks_Filter_All"] = "All",
            ["Notion_Tasks_Filter_Status"] = "Status filter",
            ["Notion_Tasks_Filter_Open"] = "Open",
            ["Notion_Tasks_Filter_Completed"] = "Done",
            ["Notion_Tasks_Filter_AllStatuses"] = "All statuses",
            ["Notion_Tasks_Filter_Due"] = "Due date filter",
            ["Notion_Tasks_Filter_DueAll"] = "Any due date",
            ["Notion_Tasks_Filter_Overdue"] = "Overdue",
            ["Notion_Tasks_Filter_Upcoming"] = "Upcoming",
            ["Notion_Tasks_GroupBy"] = "Group tasks by",
            ["Notion_Tasks_GroupBy_DueDate"] = "Due date",
            ["Notion_Tasks_GroupBy_Page"] = "Page",
            ["Notion_Tasks_LoadError"] = "Tasks could not be loaded.",
            ["Notion_Tasks_Empty"] = "No tasks match the current filters.",
            ["Notion_Tasks_ToggleComplete"] = "Toggle task completion",
            ["Notion_Tasks_ToggleCompleteFor"] = "{0}",
            ["Notion_Tasks_FiltersOpen"] = "Filters ({0})",
            ["Notion_Tasks_Group_Overdue"] = "Overdue",
            ["Notion_Tasks_Group_Today"] = "Today",
            ["Notion_Tasks_Group_Tomorrow"] = "Tomorrow",
            ["Notion_Tasks_Group_Later"] = "Later",
            ["Notion_Tasks_Group_NoDueDate"] = "No due date",
            ["Notion_Tasks_Due_Today"] = "Today",
            ["Notion_Tasks_Due_Tomorrow"] = "Tomorrow",
            ["Notion_Tasks_Due_Overdue"] = "Overdue"
        });
    }

    [Fact]
    public void FilterGroups_RenderAsFieldsetWithLegend()
    {
        var cut = RenderTasks(new FakeTaskProvider(SampleTasks()));

        cut.WaitForAssertion(() =>
        {
            var legends = cut.FindAll(".tm-my-tasks__filters fieldset legend")
                .Select(legend => legend.TextContent.Trim())
                .ToArray();
            legends.Should().Equal("Assignee filter", "Status filter", "Due date filter", "Group tasks by");
        });
    }

    [Fact]
    public void TaskCheckbox_AccessibleNameContainsTaskTitle()
    {
        var cut = RenderTasks(new FakeTaskProvider(SampleTasks()));

        cut.WaitForAssertion(() =>
        {
            var check = cut.Find("[data-task-id='task-overdue'] .tm-my-tasks__check");
            check.GetAttribute("aria-label").Should().Be("Fix export bug");
        });
    }

    [Fact]
    public async Task DueDateGroup_CollapsesWithAriaExpanded()
    {
        var cut = RenderTasks(new FakeTaskProvider(SampleTasks()));
        cut.WaitForAssertion(() => cut.FindAll(".tm-my-tasks__group").Should().NotBeEmpty());

        var toggle = cut.Find(".tm-my-tasks__group-toggle");
        toggle.GetAttribute("aria-expanded").Should().Be("true");

        await toggle.ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-my-tasks__group-toggle").GetAttribute("aria-expanded").Should().Be("false");
            cut.FindAll(".tm-my-tasks__group[data-group-collapsed='true']").Should().ContainSingle();
        });
    }

    [Fact]
    public void TaskRow_RendersAssigneeAvatarPageAndDueDate()
    {
        var cut = RenderTasks(new FakeTaskProvider(SampleTasks()));

        cut.WaitForAssertion(() =>
        {
            var row = cut.Find("[data-task-id='task-overdue']");
            row.QuerySelector(".tm-my-tasks__assignee-avatar .tm-avatar").Should().NotBeNull();
            row.QuerySelector(".tm-my-tasks__assignee-avatar").TextContent.Should().Contain("Alice Johnson");
            row.QuerySelector(".tm-my-tasks__page").Should().NotBeNull();
            row.QuerySelector(".tm-my-tasks__due").Should().NotBeNull();
        });
    }

    [Fact]
    public void NarrowContainer_MovesFiltersIntoASheetTrigger()
    {
        var css = File.ReadAllText(Path.Combine(FindNotionRoot(), "UI", "TmNotionMyTasks.razor.css"));

        css.Should().Contain("@container (max-width: 640px)");
        css.Should().Contain(".tm-my-tasks__filters--inline");
        css.Should().Contain(".tm-my-tasks__compact-bar");
        var narrow = css[css.IndexOf("@container (max-width: 640px)", StringComparison.Ordinal)..];
        narrow.Should().Contain(".tm-my-tasks__filters-open");
        narrow.Should().Contain("display: flex");
        css.Should().NotContain("the filter segments stack and wrap as full rows");
    }

    private static string FindNotionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "Tempo.Blazor.NotionEditor", "Components", "NotionEditor");
    }

    private IRenderedComponent<TmNotionMyTasks> RenderTasks(ITmWorkItemProvider provider)
        => Render<TmNotionMyTasks>(parameters => parameters
            .Add(p => p.WorkItemSource, provider)
            .Add(p => p.CurrentUserId, "user-alice"));

    private static IReadOnlyList<TmWorkItem> SampleTasks()
        =>
        [
            new()
            {
                Id = "task-overdue",
                Title = "Fix export bug",
                IsCompleted = false,
                DueDate = DateTime.Today.AddDays(-1),
                OriginPageId = "page-1",
                OriginPageTitle = "Release Plan",
                Assignees = [new TmWorkItemAssignee { Id = "user-alice", Name = "Alice Johnson" }]
            }
        ];

    private sealed class FakeTaskProvider(IReadOnlyList<TmWorkItem> tasks) : TmWorkItemProviderBase
    {
        public List<(string Id, bool Completed)> CompletedUpdates { get; } = [];

        public override string SourceKey => "notion";
        public override string DisplayName => "Notion tasks";
        public override TmWorkItemCapabilities Capabilities => TmWorkItemCapabilities.All;

        public override Task<PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PagedResult<TmWorkItem>
            {
                Items = tasks.Skip(query.Skip).Take(query.Take).ToList(),
                TotalCount = tasks.Count,
                Page = 1,
                PageSize = query.Take
            });

        public override Task SetCompletedAsync(string id, bool completed, CancellationToken cancellationToken = default)
        {
            CompletedUpdates.Add((id, completed));
            return Task.CompletedTask;
        }
    }
}
