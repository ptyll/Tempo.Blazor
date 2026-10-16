using System.Linq;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Models;
using Xunit;

namespace Tempo.Blazor.Tests.Diagram;

public class TmDiagramEditorPageTabsTests : DiagramTestBase
{
    [Fact]
    public void Renders_PageTabs_ForMultiPageDocument()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages.Add(new DiagramPage { Name = "Page 2" });

        var cut = Render<TmDiagramEditor>(
            parameters => parameters.Add(p => p.Document, doc));

        var tabs = cut.FindAll(".tm-diagram-editor__page-tab");
        tabs.Count.Should().Be(2);
        tabs[0].TextContent.Should().Contain("Page 1");
        tabs[1].TextContent.Should().Contain("Page 2");
    }

    [Fact]
    public void Click_PageTab_SwitchesActivePage()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages[0].Nodes.Add(new DiagramNode { Id = "n1", StencilId = "rect", X = 10, Y = 10, W = 100, H = 100 });
        doc.Pages.Add(new DiagramPage { Name = "Page 2" });
        doc.Pages[1].Nodes.Add(new DiagramNode { Id = "n2", StencilId = "rect", X = 20, Y = 20, W = 100, H = 100 });

        var cut = Render<TmDiagramEditor>(
            parameters => parameters.Add(p => p.Document, doc));

        var tabs = cut.FindAll(".tm-diagram-editor__page-tab");
        tabs[0].ClassList.Should().Contain("tm-diagram-editor__page-tab--active");
        tabs[1].ClassList.Should().NotContain("tm-diagram-editor__page-tab--active");

        tabs[1].Click();

        cut.WaitForAssertion(() =>
        {
            var updatedTabs = cut.FindAll(".tm-diagram-editor__page-tab");
            updatedTabs[0].ClassList.Should().NotContain("tm-diagram-editor__page-tab--active");
            updatedTabs[1].ClassList.Should().Contain("tm-diagram-editor__page-tab--active");
        });
    }

    [Fact]
    public void Click_ClosePageButton_RemovesPage()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages.Add(new DiagramPage { Name = "Page 2" });
        doc.Pages.Add(new DiagramPage { Name = "Page 3" });

        var cut = Render<TmDiagramEditor>(
            parameters => parameters.Add(p => p.Document, doc));

        var tabs = cut.FindAll(".tm-diagram-editor__page-tab");
        tabs.Count.Should().Be(3);

        // Close the middle tab (Page 2)
        var closeButtons = tabs[1].QuerySelectorAll(".tm-diagram-editor__page-tab-close");
        // The close action is the last button in the tab (the duplicate button comes first)
        var closeBtn = closeButtons.LastOrDefault();
        closeBtn.Should().NotBeNull();
        closeBtn!.Click();

        cut.WaitForAssertion(() =>
        {
            var updatedTabs = cut.FindAll(".tm-diagram-editor__page-tab");
            updatedTabs.Count.Should().Be(2);
            updatedTabs[0].TextContent.Should().Contain("Page 1");
            updatedTabs[1].TextContent.Should().Contain("Page 3");
        });
    }

    [Fact]
    public void Click_AddPageButton_CreatesNewPage()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();

        var cut = Render<TmDiagramEditor>(
            parameters => parameters.Add(p => p.Document, doc));

        var addBtn = cut.Find(".tm-diagram-editor__page-tab-add");
        addBtn.Click();

        cut.WaitForAssertion(() =>
        {
            var updatedTabs = cut.FindAll(".tm-diagram-editor__page-tab");
            updatedTabs.Count.Should().Be(2);
            updatedTabs[1].TextContent.Should().Contain("Page 2");
        });
    }

    [Fact]
    public void ActivePage_UndoRedo_IsolatedBetweenPages()
    {
        var doc = new DiagramDocument();
        doc.EnsurePages();
        doc.Pages.Add(new DiagramPage { Name = "Page 2" });

        var cut = Render<TmDiagramEditor>(
            parameters => parameters.Add(p => p.Document, doc));

        // Page 1: add a node
        var node1 = new DiagramNode { Id = "n1", StencilId = "rect", X = 0, Y = 0, W = 100, H = 100 };
        doc.Pages[0].Nodes.Add(node1);
        cut.Render();

        // Switch to Page 2
        var tabs = cut.FindAll(".tm-diagram-editor__page-tab");
        tabs[1].Click();
        cut.WaitForAssertion(() =>
        {
            var updatedTabs = cut.FindAll(".tm-diagram-editor__page-tab");
            updatedTabs[1].ClassList.Should().Contain("tm-diagram-editor__page-tab--active");
        });

        // Undo on Page 2 should do nothing (no edits on Page 2)
        var undoBtn = cut.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label")?.Contains("Undo") == true);
        undoBtn.Should().NotBeNull();
        undoBtn!.IsDisabled().Should().BeTrue();

        // Redo should also do nothing
        var redoBtn = cut.FindAll("button").FirstOrDefault(b => b.GetAttribute("aria-label")?.Contains("Redo") == true);
        redoBtn.Should().NotBeNull();
        redoBtn!.IsDisabled().Should().BeTrue();
    }
}
