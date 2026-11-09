using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.WorkItems;
using Tempo.Blazor.Components.NotionEditor.UI;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

public class TmNotionMyTasksTests : LocalizationTestBase
{
    public TmNotionMyTasksTests()
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
    public void RendersTasksGroupedByDueDate()
    {
        var provider = new FakeTaskProvider(SampleTasks());

        var cut = RenderTasks(provider);

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-panel-header__title").TextContent.Should().Be("My Tasks");
            cut.FindAll(".tm-my-tasks__item").Should().HaveCount(3);
            cut.Markup.Should().Contain("Overdue");
            cut.Markup.Should().Contain("Today");
            cut.Markup.Should().Contain("Workspace Launch");
            cut.Markup.Should().Contain("Alice Johnson");
        });
    }

    [Fact]
    public void RendersEmptyStateWhenNoTasksMatchFilters()
    {
        var provider = new FakeTaskProvider([]);

        var cut = RenderTasks(provider);

        cut.WaitForAssertion(() =>
            cut.Find(".tm-my-tasks__empty").TextContent.Should().Contain("No tasks match"));
    }

    [Fact]
    public async Task CanGroupTasksByPage()
    {
        var provider = new FakeTaskProvider(SampleTasks());
        var cut = RenderTasks(provider);
        cut.WaitForAssertion(() => cut.FindAll(".tm-my-tasks__item").Should().NotBeEmpty());

        await cut.FindAll(".tm-my-tasks__filter")
            .Single(button => button.TextContent.Trim() == "Page")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            var groupTitles = cut.FindAll(".tm-my-tasks__group-title")
                .Select(title => title.TextContent)
                .ToArray();
            groupTitles.Should().Contain(title => title.Contains("Release Plan"));
            groupTitles.Should().Contain(title => title.Contains("Workspace Launch"));
        });
    }

    [Fact]
    public async Task ToggleTaskPersistsCompletionThroughProvider()
    {
        var provider = new FakeTaskProvider(SampleTasks());
        var cut = RenderTasks(provider);
        cut.WaitForAssertion(() => cut.Find("[data-task-id='task-overdue']").Should().NotBeNull());

        await cut.Find("[data-task-id='task-overdue'] .tm-my-tasks__check").ClickAsync(new MouseEventArgs());

        provider.CompletedUpdates.Should().Contain(("task-overdue", true));
        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-my-tasks__item").Select(item => item.GetAttribute("data-task-id"))
                .Should().NotContain("task-overdue"));
    }

    [Fact]
    public async Task CompletedFilter_CountMatchesVisibleCompletedTasks()
    {
        var provider = new FakeTaskProvider(SampleTasksWithCompleted());
        var cut = RenderTasks(provider);
        cut.WaitForAssertion(() => cut.FindAll(".tm-my-tasks__item").Should().HaveCount(3));

        await cut.FindAll(".tm-my-tasks__filter")
            .Single(button => button.TextContent.Trim() == "Done")
            .ClickAsync(new MouseEventArgs());

        cut.WaitForAssertion(() =>
        {
            cut.Find(".tm-notion-panel-header__subtitle").TextContent.Should().Be("1 tasks");
            cut.FindAll(".tm-my-tasks__item").Should().ContainSingle()
                .Which.GetAttribute("data-task-id").Should().Be("task-completed");
        });
    }

    [Fact]
    public async Task StatusAndDueFilters_CountMatchesVisibleTasksForEveryCombination()
    {
        var tasks = SampleFilterMatrixTasks();
        var scenarios =
            from status in new[] { "Open", "Done", "All statuses" }
            from due in new[] { "Any due date", "Overdue", "Upcoming" }
            select (Status: status, Due: due);

        foreach (var scenario in scenarios)
        {
            var cut = RenderTasks(new FakeTaskProvider(tasks));

            await ClickSegmentFilterAsync(cut, 1, scenario.Status);
            await ClickSegmentFilterAsync(cut, 2, scenario.Due);

            var expected = tasks
                .Where(task => string.Equals(AssigneeId(task), "alice", StringComparison.OrdinalIgnoreCase))
                .Where(task => MatchesStatus(task, scenario.Status))
                .Where(task => MatchesDue(task, scenario.Due))
                .Count();

            cut.WaitForAssertion(() =>
            {
                cut.Find(".tm-notion-panel-header__subtitle").TextContent.Should().Be($"{expected} tasks");
                cut.FindAll(".tm-my-tasks__item").Should().HaveCount(expected);
            });
        }
    }

    [Fact]
    public void LoadError_UsesLocalizedMessageInsteadOfProviderException()
    {
        var provider = new ThrowingTaskProvider("database-password-leaked");

        var cut = RenderTasks(provider);

        cut.WaitForAssertion(() =>
        {
            cut.Markup.Should().Contain("Tasks could not be loaded.");
            cut.Markup.Should().NotContain("database-password-leaked");
        });
    }

    [Fact]
    public async Task ClickingTaskRaisesSourceNavigation()
    {
        var provider = new FakeTaskProvider(SampleTasks());
        (string PageId, string BlockId) selected = default;

        var cut = RenderTasks(provider, EventCallback.Factory.Create<(string, string)>(this, value => selected = value));
        cut.WaitForAssertion(() => cut.Find("[data-task-id='task-today']").Should().NotBeNull());

        await cut.Find("[data-task-id='task-today'] .tm-my-tasks__body").ClickAsync(new MouseEventArgs());

        selected.Should().Be(("page-a", "block-today"));
    }

    private IRenderedComponent<TmNotionMyTasks> RenderTasks(
        ITmWorkItemProvider provider,
        EventCallback<(string, string)> navigateCallback = default)
        => Render<TmNotionMyTasks>(parameters => parameters
            .Add(component => component.WorkItemSource, provider)
            .Add(component => component.CurrentUserId, "alice")
            .Add(component => component.OnNavigateToBlock, navigateCallback));

    private static Task ClickSegmentFilterAsync(
        IRenderedComponent<TmNotionMyTasks> cut,
        int segmentIndex,
        string label)
    {
        var segment = cut.FindAll(".tm-my-tasks__segment")[segmentIndex];
        var button = segment.QuerySelectorAll("button")
            .Single(item => item.TextContent.Trim() == label);

        return button.ClickAsync(new MouseEventArgs());
    }

    private static string? AssigneeId(TmWorkItem task) => task.Assignees.FirstOrDefault()?.Id;

    private static bool MatchesStatus(TmWorkItem task, string status) => status switch
    {
        "Open" => !task.IsCompleted,
        "Done" => task.IsCompleted,
        _ => true
    };

    private static bool MatchesDue(TmWorkItem task, string due)
    {
        var today = DateTime.Today;
        return due switch
        {
            "Overdue" => task.DueDate is DateTime date && date.Date < today,
            "Upcoming" => task.DueDate is DateTime date && date.Date >= today,
            _ => true
        };
    }

    private static TmWorkItem MakeTask(
        string id, string pageId, string pageTitle, string blockId, string text,
        string? assigneeId = null, string? assigneeName = null,
        DateTime? dueDate = null, bool isCompleted = false, DateTime createdAt = default)
    {
        var assignees = new List<TmWorkItemAssignee>();
        if (!string.IsNullOrWhiteSpace(assigneeId))
            assignees.Add(new TmWorkItemAssignee { Id = assigneeId, Name = assigneeName ?? assigneeId });

        return new TmWorkItem
        {
            Id = id,
            SourceKey = "notion",
            OriginPageId = pageId,
            OriginPageTitle = pageTitle,
            OriginBlockId = blockId,
            Title = text,
            Assignees = assignees,
            DueDate = dueDate,
            IsCompleted = isCompleted,
            Status = isCompleted ? TmWorkItemStatus.Done : TmWorkItemStatus.Open,
            CreatedAt = createdAt
        };
    }

    private static IReadOnlyList<TmWorkItem> SampleTasks()
    {
        var now = DateTime.Today;
        return
        [
            MakeTask("task-overdue", "page-a", "Workspace Launch", "block-overdue", "Send launch checklist", "alice", "Alice Johnson", now.AddDays(-1), createdAt: now.AddDays(-4)),
            MakeTask("task-today", "page-a", "Workspace Launch", "block-today", "Review onboarding copy", "alice", "Alice Johnson", now, createdAt: now.AddDays(-3)),
            MakeTask("task-release", "page-b", "Release Plan", "block-release", "Confirm rollout owner", "alice", "Alice Johnson", now.AddDays(3), createdAt: now.AddDays(-2)),
            MakeTask("task-other-user", "page-c", "Partner Notes", "block-other", "Not visible in Mine scope", "bob", "Bob Stone", now, createdAt: now.AddDays(-1))
        ];
    }

    private static IReadOnlyList<TmWorkItem> SampleTasksWithCompleted()
    {
        var tasks = SampleTasks().ToList();
        tasks.Add(MakeTask("task-completed", "page-a", "Workspace Launch", "block-completed", "Archive launch notes",
            "alice", "Alice Johnson", DateTime.Today.AddDays(-2), isCompleted: true, createdAt: DateTime.Today.AddDays(-5)));
        return tasks;
    }

    private static IReadOnlyList<TmWorkItem> SampleFilterMatrixTasks()
    {
        var today = DateTime.Today;
        return
        [
            MakeTask("open-overdue", "page-a", "Planning", "block-open-overdue", "Open overdue", "alice", dueDate: today.AddDays(-2), createdAt: today.AddDays(-4)),
            MakeTask("open-today", "page-a", "Planning", "block-open-today", "Open today", "alice", dueDate: today, createdAt: today.AddDays(-3)),
            MakeTask("open-future", "page-a", "Planning", "block-open-future", "Open future", "alice", dueDate: today.AddDays(5), createdAt: today.AddDays(-2)),
            MakeTask("open-no-due", "page-a", "Planning", "block-open-no-due", "Open no due date", "alice", createdAt: today.AddDays(-1)),
            MakeTask("done-overdue", "page-b", "Release", "block-done-overdue", "Done overdue", "alice", dueDate: today.AddDays(-1), isCompleted: true, createdAt: today.AddDays(-5)),
            MakeTask("done-today", "page-b", "Release", "block-done-today", "Done today", "alice", dueDate: today, isCompleted: true, createdAt: today.AddDays(-4)),
            MakeTask("done-future", "page-b", "Release", "block-done-future", "Done future", "alice", dueDate: today.AddDays(2), isCompleted: true, createdAt: today.AddDays(-3)),
            MakeTask("other-user", "page-c", "Hidden", "block-other-user", "Other user task", "bob", dueDate: today, createdAt: today)
        ];
    }

    private sealed class FakeTaskProvider : TmWorkItemProviderBase
    {
        private readonly List<TmWorkItem> _tasks;

        public FakeTaskProvider(IEnumerable<TmWorkItem> tasks)
        {
            _tasks = tasks.Select(Clone).ToList();
        }

        public override string SourceKey => "notion";
        public override string DisplayName => "Notion tasks";
        public override TmWorkItemCapabilities Capabilities => TmWorkItemCapabilities.All;

        public List<(string TaskId, bool Completed)> CompletedUpdates { get; } = [];

        public override Task<PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
        {
            IEnumerable<TmWorkItem> filtered = _tasks;

            if (!string.IsNullOrWhiteSpace(query.AssigneeId))
                filtered = filtered.Where(task => task.Assignees.Any(a => string.Equals(a.Id, query.AssigneeId, StringComparison.OrdinalIgnoreCase)));

            if (!query.IncludeCompleted)
                filtered = filtered.Where(task => !task.IsCompleted);

            if (query.DueBefore is DateTime dueBefore)
                filtered = filtered.Where(task => task.DueDate is not null && task.DueDate.Value.Date <= dueBefore.Date);

            if (query.DueAfter is DateTime dueAfter)
                filtered = filtered.Where(task => task.DueDate is not null && task.DueDate.Value.Date >= dueAfter.Date);

            var items = filtered
                .OrderBy(task => task.DueDate ?? DateTime.MaxValue)
                .ThenBy(task => task.OriginPageTitle, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return Task.FromResult(new PagedResult<TmWorkItem>
            {
                Items = items.Skip(query.Skip).Take(query.Take).Select(Clone).ToList(),
                TotalCount = items.Count,
                Page = query.Take <= 0 ? 1 : (query.Skip / query.Take) + 1,
                PageSize = query.Take
            });
        }

        public override Task SetCompletedAsync(string id, bool completed, CancellationToken cancellationToken = default)
        {
            CompletedUpdates.Add((id, completed));
            var task = _tasks.Single(item => item.Id == id);
            task.IsCompleted = completed;
            return Task.CompletedTask;
        }

        private static TmWorkItem Clone(TmWorkItem task) => new()
        {
            Id = task.Id,
            SourceKey = task.SourceKey,
            OriginPageId = task.OriginPageId,
            OriginPageTitle = task.OriginPageTitle,
            OriginBlockId = task.OriginBlockId,
            Title = task.Title,
            Assignees = task.Assignees.Select(a => new TmWorkItemAssignee { Id = a.Id, Name = a.Name }).ToList(),
            DueDate = task.DueDate,
            IsCompleted = task.IsCompleted,
            Status = task.Status,
            Tags = task.Tags.ToList(),
            CreatedAt = task.CreatedAt
        };
    }

    private sealed class ThrowingTaskProvider : TmWorkItemProviderBase
    {
        private readonly string _message;

        public ThrowingTaskProvider(string message)
            => _message = message;

        public override string SourceKey => "notion";
        public override string DisplayName => "Notion tasks";

        public override Task<PagedResult<TmWorkItem>> SearchAsync(TmWorkItemQuery query, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(_message);

        public override Task SetCompletedAsync(string id, bool completed, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
