using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>TDD tests for TmKanbanBoard.</summary>
public class TmKanbanBoardTests : LocalizationTestBase
{
    private record KanbanTask(int Id, string Title, string Status);

    private static readonly IReadOnlyList<KanbanColumn> Columns =
    [
        new("todo", "To Do", "#3b82f6"),
        new("doing", "In Progress", "#f59e0b"),
        new("done", "Done", "#10b981"),
    ];

    private static readonly IReadOnlyList<KanbanTask> Tasks =
    [
        new(1, "Task A", "todo"),
        new(2, "Task B", "todo"),
        new(3, "Task C", "doing"),
        new(4, "Task D", "done"),
    ];

    [Fact]
    public void Kanban_Renders_Columns()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b =>
            {
                b.AddContent(0, t.Title);
            })));

        cut.Find(".tm-kanban").Should().NotBeNull();
        cut.FindAll(".tm-kanban__column").Count.Should().Be(3);
    }

    [Fact]
    public void Kanban_ColumnHeaders_ShowTitles()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        cut.Markup.Should().Contain("To Do");
        cut.Markup.Should().Contain("In Progress");
        cut.Markup.Should().Contain("Done");
    }

    [Fact]
    public void Kanban_CardsDistributed_PerColumn()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        // todo=2, doing=1, done=1
        var cards = cut.FindAll(".tm-kanban__card");
        cards.Count.Should().Be(4);
    }

    [Fact]
    public void Kanban_CardTemplate_Rendered()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b =>
            {
                b.OpenElement(0, "strong");
                b.AddContent(1, t.Title);
                b.CloseElement();
            })));

        cut.Markup.Should().Contain("<strong>Task A</strong>");
    }

    [Fact]
    public void Kanban_CardClick_FiresCallback()
    {
        KanbanTask? clicked = null;
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title)))
            .Add(x => x.OnCardClick, t => clicked = t));

        cut.FindAll(".tm-kanban__card")[0].Click();

        clicked.Should().NotBeNull();
        clicked!.Title.Should().Be("Task A");
    }

    [Fact]
    public void Kanban_EmptyColumn_ShowsEmptyMessage()
    {
        var items = new List<KanbanTask>
        {
            new(1, "Task A", "todo"),
        };

        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, items)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        // "doing" and "done" columns should show empty state
        cut.FindAll(".tm-kanban__empty").Count.Should().Be(2);
    }

    [Fact]
    public void Kanban_WipLimit_ShowsWarning()
    {
        var columnsWithLimit = new KanbanColumn[]
        {
            new("todo", "To Do", "#3b82f6", MaxItems: 1),
            new("doing", "In Progress"),
            new("done", "Done"),
        };

        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, columnsWithLimit)
            .Add(x => x.Items, Tasks) // 2 items in "todo" but limit is 1
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        // The "todo" column should have a WIP warning class
        cut.FindAll(".tm-kanban__column--over-limit").Count.Should().Be(1);
    }

    [Fact]
    public void Kanban_ColumnCount_ShowsItemCount()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        // Should show count badges
        cut.FindAll(".tm-kanban__count").Count.Should().Be(3);
    }

    [Fact]
    public void Kanban_DraggableAttribute_OnCards()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        var cards = cut.FindAll(".tm-kanban__card");
        foreach (var card in cards)
        {
            card.GetAttribute("draggable").Should().Be("true");
        }
    }

    [Fact]
    public void Kanban_CustomClass()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title)))
            .Add(x => x.Class, "my-kanban"));

        cut.Find(".tm-kanban").ClassList.Should().Contain("my-kanban");
    }

    [Fact]
    public void Kanban_ColumnColor_AppliedAsStyle()
    {
        var cut = RenderComponent<TmKanbanBoard<KanbanTask>>(p => p
            .Add(x => x.Columns, Columns)
            .Add(x => x.Items, Tasks)
            .Add(x => x.ColumnSelector, t => t.Status)
            .Add(x => x.CardTemplate, t => (RenderFragment)(b => b.AddContent(0, t.Title))));

        var firstHeader = cut.FindAll(".tm-kanban__header-color")[0];
        var style = firstHeader.GetAttribute("style") ?? "";
        style.Should().Contain("#3b82f6");
    }
}
