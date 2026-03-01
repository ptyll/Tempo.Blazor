using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.TreeView;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.TreeView;

file record TestTreeNode(
    string Id,
    string Label,
    string? Icon,
    bool IsLeaf,
    bool IsLoading,
    IReadOnlyList<ITreeNode<string>> Children) : ITreeNode<string>;

/// <summary>TDD tests for TmTreeView&lt;TKey&gt;.</summary>
public class TmTreeViewTests : LocalizationTestBase
{
    private static List<ITreeNode<string>> MakeNodes() =>
    [
        new TestTreeNode("n1", "Node 1", null, true,  false, []),
        new TestTreeNode("n2", "Node 2", null, true,  false, []),
        new TestTreeNode("n3", "Node 3", null, false, false, []),
    ];

    [Fact]
    public void TmTreeView_Renders_TreeView()
    {
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, MakeNodes()));

        cut.Find(".tm-tree-view").Should().NotBeNull();
    }

    [Fact]
    public void TmTreeView_Renders_Node_Per_Item()
    {
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, MakeNodes()));

        cut.FindAll(".tm-tree-node").Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void TmTreeView_Leaf_Has_No_Expand_Button()
    {
        var leaf = new TestTreeNode("leaf1", "Leaf", null, true, false, []);
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, new List<ITreeNode<string>> { leaf }));

        cut.FindAll(".tm-tree-expand").Should().BeEmpty();
    }

    [Fact]
    public void TmTreeView_NonLeaf_Has_Expand_Button()
    {
        var parent = new TestTreeNode("p1", "Parent", null, false, false, []);
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, new List<ITreeNode<string>> { parent }));

        cut.FindAll(".tm-tree-expand").Should().NotBeEmpty();
    }

    [Fact]
    public void TmTreeView_Click_Expand_Shows_Children()
    {
        var child  = new TestTreeNode("c1", "Child",  null, true,  false, []);
        var parent = new TestTreeNode("p1", "Parent", null, false, false,
            new List<ITreeNode<string>> { child }.AsReadOnly());
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, new List<ITreeNode<string>> { parent }));

        cut.Find(".tm-tree-expand").Click();

        cut.FindAll(".tm-tree-node").Count.Should().Be(2);
    }

    [Fact]
    public void TmTreeView_Node_Loading_Shows_Spinner()
    {
        var loading = new TestTreeNode("n1", "Loading Node", null, false, true, []);
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, new List<ITreeNode<string>> { loading }));

        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmTreeView_Click_Label_Fires_OnNodeSelect()
    {
        ITreeNode<string>? clicked = null;
        var node = new TestTreeNode("n1", "Click Me", null, true, false, []);
        var cut = RenderComponent<TmTreeView<string>>(p => p
            .Add(c => c.Nodes, new List<ITreeNode<string>> { node })
            .Add(c => c.OnNodeSelect, EventCallback.Factory.Create<ITreeNode<string>>(
                this, n => clicked = n)));

        cut.Find(".tm-tree-node-label").Click();

        clicked.Should().NotBeNull();
        clicked!.Id.Should().Be("n1");
    }
}
