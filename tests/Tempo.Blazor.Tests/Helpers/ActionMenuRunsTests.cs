using FluentAssertions;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Helpers;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// C3: the "More" menu renders contiguous runs of items sharing a group key as labelled
/// <c>role="group"</c> sections separated by <c>role="separator"</c>. The split NEVER reorders —
/// the render order is the Items order (the F5 rule), so two runs of the same key stay apart and
/// a group that is interrupted by an ungrouped item forms two separate runs.
/// </summary>
public class ActionMenuRunsTests
{
    private static TmActionItem Item(string id, string? group = null, string? groupLabel = null)
        => new() { Id = id, Label = id, Group = group, GroupLabel = groupLabel };

    [Fact]
    public void ConsecutiveSameKey_FormOneRun()
    {
        var runs = ActionMenuRuns.Split([Item("a", "edit", "Edit"), Item("b", "edit", "Edit"), Item("c", "edit", "Edit")]);

        runs.Should().HaveCount(1);
        runs[0].Key.Should().Be("edit");
        runs[0].Label.Should().Be("Edit");
        runs[0].Items.Select(i => i.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void NonContiguousSameKey_FormTwoRuns_OrderPreserved()
    {
        // F5: grouping never reorders - the second "edit" run stays where it was written.
        var runs = ActionMenuRuns.Split([Item("a", "edit", "Edit"), Item("b"), Item("c", "edit", "Edit")]);

        runs.Select(r => r.Key).Should().Equal("edit", null, "edit");
        runs.SelectMany(r => r.Items).Select(i => i.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void NullAndEmptyKeys_AreBothUngrouped()
    {
        var runs = ActionMenuRuns.Split([Item("a"), Item("b", ""), Item("c", "   ")]);

        runs.Should().HaveCount(1);
        runs[0].Key.Should().BeNull("null, empty and whitespace keys are all ungrouped");
        runs[0].Label.Should().BeNull();
        runs[0].Items.Select(i => i.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void ConsecutiveUngroupedItems_MergeIntoOneRun_SoNoSeparatorIsDrawnBetweenThem()
    {
        var runs = ActionMenuRuns.Split([Item("a"), Item("b"), Item("c", "edit", "Edit"), Item("d")]);

        runs.Should().HaveCount(3);
        runs[0].Key.Should().BeNull();
        runs[0].Items.Select(i => i.Id).Should().Equal("a", "b");
        runs[2].Key.Should().BeNull();
        runs[2].Items.Select(i => i.Id).Should().Equal("d");
    }

    [Fact]
    public void Label_ComesFromTheFirstItemOfTheRun()
    {
        var runs = ActionMenuRuns.Split([Item("a", "view", "View"), Item("b", "view", "Ignored")]);

        runs[0].Label.Should().Be("View");
    }

    [Fact]
    public void Label_EmptyOnTheFirstItem_RendersTheRunWithoutAGroupWrapper()
    {
        // The render contract: a run with a key but no label still forms a run (separators apply)
        // but must NOT render a role=group - an unnamed group is noise for assistive technology.
        var runs = ActionMenuRuns.Split([Item("a", "k"), Item("b", "k", "Ignored")]);

        runs[0].Key.Should().Be("k");
        runs[0].Label.Should().BeNull();
    }

    [Fact]
    public void EmptyList_SplitsToNoRuns()
    {
        ActionMenuRuns.Split([]).Should().BeEmpty();
    }

    [Fact]
    public void Items_AreTheSameReferences_OrderPreserved()
    {
        var a = Item("a", "g", "G");
        var b = Item("b");
        var runs = ActionMenuRuns.Split([a, b]);

        runs[0].Items.Should().Contain(new[] { a });
        runs[1].Items.Should().Contain(new[] { b });
    }
}
