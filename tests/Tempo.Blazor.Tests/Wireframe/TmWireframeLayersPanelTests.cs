using AngleSharp.Dom;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Commands;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>bUnit tests for <see cref="TmWireframeLayersPanel"/> (wireframe redesign plan, phases 1 and 3).</summary>
public class TmWireframeLayersPanelTests : LocalizationTestBase
{
    private static WireframeDocument NewDocument()
    {
        var doc = new WireframeDocument { Title = "Test" };
        doc.EnsureActivePage();
        return doc;
    }

    [Fact]
    public void ActiveLayerDefault_IsReportedAfterRender_NotDuringParametersSet()
    {
        var doc = NewDocument();
        var reported = new List<string?>();

        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, (string?)null)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        var defaultLayerId = doc.Layers.OrderBy(l => l.Order).First().Id;
        cut.WaitForAssertion(() => reported.Should().Equal(defaultLayerId));

        // The default layer is already shown as active although the parent never confirmed it.
        cut.FindAll(".tm-wd-layers__radio--checked").Should().ContainSingle();
    }

    [Fact]
    public void ActiveLayerDefault_ReportIsAwaited_SoAHandlerFailureSurfaces()
    {
        // Fire-and-forget from OnParametersSet swallowed handler exceptions; the awaited after-render
        // report surfaces them on the renderer like every other callback.
        var doc = NewDocument();

        var act = () => Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this,
                (Func<string?, Task>)(_ => throw new InvalidOperationException("parent handler failed")))));

        act.Should().Throw<Exception>().WithMessage("*parent handler failed*");
    }
    [Fact]
    public void ActiveLayerDefault_IsReportedOnlyOnce_WhenParentDoesNotFeedItBack()
    {
        var doc = NewDocument();
        var reported = new List<string?>();

        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        cut.Render(p => p.Add(c => c.SelectedElementIds, new[] { "x" }));
        cut.Render(p => p.Add(c => c.SelectedElementIds, Array.Empty<string>()));

        reported.Should().HaveCount(1);
    }

    [Fact]
    public void ActiveLayer_ProvidedByParent_IsNotOverwritten()
    {
        var doc = NewDocument();
        var reported = new List<string?>();
        var existing = doc.Layers.OrderBy(l => l.Order).First().Id;

        Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, existing)
            .Add(c => c.ActiveLayerIdChanged, EventCallback.Factory.Create<string?>(this, id => reported.Add(id))));

        reported.Should().BeEmpty();
    }

    // ── Phase 3: semantics, row menu and non-drag reorder ──────────────────────

    /// <summary>Three layers on the active page: Background (default, order 0), Content (1), Notes (2).</summary>
    private static WireframeDocument NewDocumentWithLayers()
    {
        var doc = NewDocument();
        var page = doc.ActivePage!;
        page.Layers.Clear();
        page.Layers.Add(new WireframeLayer { Name = "Background", Order = 0 });
        page.Layers.Add(new WireframeLayer { Name = "Content", Order = 1 });
        page.Layers.Add(new WireframeLayer { Name = "Notes", Order = 2 });
        page.ActiveLayerId = page.Layers[0].Id;
        return doc;
    }

    private static IReadOnlyList<string> OrderedNames(WireframeDocument doc) =>
        doc.Layers.OrderBy(l => l.Order).Select(l => l.Name).ToList();

    private static string Id(WireframeDocument doc, string name) => doc.Layers.First(l => l.Name == name).Id;

    private static IElement Row(IRenderedComponent<TmWireframeLayersPanel> cut, string layerId) =>
        cut.Find($"[data-layer-id='{layerId}']");

    private static void OpenRowMenu(IRenderedComponent<TmWireframeLayersPanel> cut, string layerId) =>
        cut.Find($"[data-layer-id='{layerId}'] .tm-wd-layers__row-menu .tm-context-menu__trigger").Click();

    private static IElement RowMenuItem(IRenderedComponent<TmWireframeLayersPanel> cut, string layerId, string kind) =>
        cut.Find($"[data-layer-id='{layerId}'] .tm-wd-layers__row-menu-item[data-action='{kind}']");

    private IRenderedComponent<TmWireframeLayersPanel> RenderPanel(
        WireframeDocument doc,
        WireframeCommandStack? stack = null,
        bool readOnly = false,
        string[]? selected = null,
        bool showCollapseToggle = true) =>
        Render<TmWireframeLayersPanel>(p =>
        {
            p.Add(c => c.Document, doc)
             .Add(c => c.ActiveLayerId, doc.ActivePage!.Layers.OrderBy(l => l.Order).First().Id)
             .Add(c => c.ReadOnly, readOnly)
             .Add(c => c.ShowCollapseToggle, showCollapseToggle);
            if (selected is not null) p.Add(c => c.SelectedElementIds, selected);
            if (stack is not null) p.AddCascadingValue(stack);
        });

    [Fact]
    public void ActiveLayer_RendersRadioWithAriaChecked_InRadioGroup()
    {
        var doc = NewDocumentWithLayers();
        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, Id(doc, "Content")));

        var group = cut.Find("[role='radiogroup']");
        group.GetAttribute("aria-label").Should().Be("Active layer", "the radio group is named");

        var radios = group.QuerySelectorAll("[role='radio']");
        radios.Should().HaveCount(3, "every layer row has exactly one radio inside the group");
        radios.Count(r => r.GetAttribute("aria-checked") == "true").Should().Be(1);
        radios.Count(r => r.GetAttribute("aria-checked") == "false").Should().Be(2);

        var checkedRadio = radios.Single(r => r.GetAttribute("aria-checked") == "true");
        checkedRadio.GetAttribute("aria-label").Should().Be("Set layer Content as active");
        checkedRadio.Closest("[data-layer-id]")!.GetAttribute("data-layer-id").Should().Be(Id(doc, "Content"));
        checkedRadio.QuerySelector(".tm-wd-layers__radio--checked").Should().NotBeNull(
            "the visual dot keeps its class (E2E selector)");

        // Clicking another radio reports the new active layer.
        string? reported = null;
        cut.Render(p => p.Add(c => c.ActiveLayerIdChanged,
            EventCallback.Factory.Create<string?>(this, id => reported = id)));
        cut.Find($"[data-layer-id='{Id(doc, "Notes")}'] [role='radio']").Click();
        reported.Should().Be(Id(doc, "Notes"));
    }

    [Fact]
    public void VisibilityAndLock_HaveAriaPressed_AndPushCommands()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var id = Id(doc, "Content");

        var visibility = Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!;
        visibility.GetAttribute("aria-pressed").Should().Be("true", "the layer is visible, so 'show' is pressed");
        visibility.GetAttribute("aria-label").Should().Be("Show layer Content");

        visibility.Click();

        doc.Layers.First(l => l.Id == id).IsVisible.Should().BeFalse();
        stack.NextUndoName.Should().Be("Toggle layer visibility");
        Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!.GetAttribute("aria-pressed").Should().Be("false");
        Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!.GetAttribute("aria-label").Should().Be("Show layer Content",
            "a toggle keeps a constant name; the pressed state carries the value");

        var @lock = Row(cut, id).QuerySelector(".tm-wd-layers__lock")!;
        @lock.GetAttribute("aria-pressed").Should().Be("false");
        @lock.GetAttribute("aria-label").Should().Be("Lock layer Content");

        @lock.Click();

        doc.Layers.First(l => l.Id == id).IsLocked.Should().BeTrue();
        stack.NextUndoName.Should().Be("Toggle layer lock");
        Row(cut, id).QuerySelector(".tm-wd-layers__lock")!.GetAttribute("aria-pressed").Should().Be("true");

        stack.Undo();
        stack.Undo();
        doc.Layers.First(l => l.Id == id).IsLocked.Should().BeFalse("undo restores the lock");
        doc.Layers.First(l => l.Id == id).IsVisible.Should().BeTrue("undo restores the visibility");
    }

    [Fact]
    public void RowMenu_Rename_CommitsRenameLayerCommand()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var id = Id(doc, "Content");

        OpenRowMenu(cut, id);
        RowMenuItem(cut, id, "rename").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-wd-layers__name-input"));
        var input = cut.Find(".tm-wd-layers__name-input");
        input.GetAttribute("aria-label").Should().Be("Rename layer", "the inline editor is named");
        input.Input("Hero");
        input.KeyDown(new KeyboardEventArgs { Key = "Enter" });

        doc.Layers.First(l => l.Id == id).Name.Should().Be("Hero");
        stack.NextUndoName.Should().Be("Rename layer");
        cut.FindAll(".tm-wd-layers__name-input").Should().BeEmpty("Enter closes the editor");

        stack.Undo();
        doc.Layers.First(l => l.Id == id).Name.Should().Be("Content", "the rename is one undo step");
    }

    [Fact]
    public void RowMenu_MoveUpDown_PushesReorderLayersCommand_Undoable()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var content = Id(doc, "Content");

        OpenRowMenu(cut, content);
        RowMenuItem(cut, content, "up").Click();
        cut.WaitForAssertion(() => OrderedNames(doc).Should().Equal(new[] { "Content", "Background", "Notes" }));
        stack.NextUndoName.Should().Be("Reorder layers");

        OpenRowMenu(cut, content);
        RowMenuItem(cut, content, "down").Click();
        cut.WaitForAssertion(() => OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" }));

        stack.Undo();
        OrderedNames(doc).Should().Equal(new[] { "Content", "Background", "Notes" }, "undo reverts the last move only");
        stack.Undo();
        OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" }, "undo reverts the first move");
        stack.Redo();
        OrderedNames(doc).Should().Equal(new[] { "Content", "Background", "Notes" }, "redo re-applies the move");

        cut.Render();
        cut.FindAll(".tm-wd-layers__name").Select(n => n.TextContent.Trim())
            .Should().Equal(new[] { "Content", "Background", "Notes" }, "the list follows the order after a re-render");
    }

    [Fact]
    public void RowMenu_FirstRowCannotMoveUp_LastRowCannotMoveDown()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);

        OpenRowMenu(cut, Id(doc, "Background"));
        RowMenuItem(cut, Id(doc, "Background"), "up").HasAttribute("disabled").Should().BeTrue();
        RowMenuItem(cut, Id(doc, "Background"), "down").HasAttribute("disabled").Should().BeFalse();
        OpenRowMenu(cut, Id(doc, "Notes"));
        RowMenuItem(cut, Id(doc, "Notes"), "down").HasAttribute("disabled").Should().BeTrue();
        RowMenuItem(cut, Id(doc, "Notes"), "up").HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void RowMenu_Trigger_HasAccessibleName()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);

        var trigger = cut.Find($"[data-layer-id='{Id(doc, "Content")}'] .tm-wd-layers__row-menu .tm-context-menu__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.TextContent.Should().Contain("Actions for layer Content",
            "the icon-only ⋯ trigger is named after its layer for screen readers");
    }

    [Fact]
    public void F2_StartsRename()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var id = Id(doc, "Content");

        // F2 is handled once on the row, so it works from any control of the row (the event bubbles to it).
        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "F2" });

        var input = cut.Find(".tm-wd-layers__name-input");
        input.GetAttribute("value").Should().Be("Content");
        Row(cut, id).QuerySelector(".tm-wd-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public void F2_FromTheVisibilityButton_StartsRenameOfThatRow()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var id = Id(doc, "Notes");

        Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!.KeyDown(new KeyboardEventArgs { Key = "F2" });

        Row(cut, id).QuerySelector(".tm-wd-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public void EnterOnName_StartsRename_ExactlyOnce_AndSpaceOnKeyUp()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var id = Id(doc, "Content");
        var name = Row(cut, id).QuerySelector(".tm-wd-layers__name")!;
        name.GetAttribute("tabindex").Should().Be("0", "the name is a keyboard stop");
        name.GetAttribute("role").Should().Be("button");

        name.KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-wd-layers__name-input").Should().BeEmpty("Space activates on keyup, like a native button");

        Row(cut, id).QuerySelector(".tm-wd-layers__name")!.KeyUp(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-wd-layers__name-input").Should().HaveCount(1);
    }

    [Fact]
    public void Escape_InRenameInput_DiscardsTheRename()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var id = Id(doc, "Content");
        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "F2" });

        var input = cut.Find(".tm-wd-layers__name-input");
        input.Input("Discarded");
        input.KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll(".tm-wd-layers__name-input").Should().BeEmpty();
        doc.Layers.First(l => l.Id == id).Name.Should().Be("Content");
        stack.CanUndo.Should().BeFalse("a cancelled rename pushes nothing");
    }

    [Fact]
    public void AltArrowDown_ReordersLayer()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var id = Id(doc, "Content");

        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        OrderedNames(doc).Should().Equal(new[] { "Background", "Notes", "Content" });
        stack.NextUndoName.Should().Be("Reorder layers");
        cut.FindAll(".tm-wd-layers__name").Select(n => n.TextContent.Trim())
            .Should().Equal(new[] { "Background", "Notes", "Content" });

        stack.Undo();
        OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" });
    }

    [Fact]
    public void AltArrowUp_ReordersLayer_AndPlainArrowsDoNothing()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);
        var id = Id(doc, "Content");

        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });
        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" }, "arrows without Alt are not ours");
        stack.CanUndo.Should().BeFalse();

        Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!.KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        OrderedNames(doc).Should().Equal(new[] { "Content", "Background", "Notes" }, "Alt+Up works from any control of the row");
    }

    [Fact]
    public void AltArrow_AtTheEdges_PushesNothing()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);

        Row(cut, Id(doc, "Background")).KeyDown(new KeyboardEventArgs { Key = "ArrowUp", AltKey = true });
        Row(cut, Id(doc, "Notes")).KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        stack.CanUndo.Should().BeFalse("moving the first row up or the last row down is a no-op, not an undo step");
        OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" });
    }

    [Fact]
    public void Move_IsAnnouncedInAPoliteLiveRegion()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);

        Row(cut, Id(doc, "Content")).KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });

        var live = cut.Find(".tm-wd-layers [role='status']");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.TextContent.Trim().Should().Be("Layer Content moved down, position 3 of 3",
            "the new position makes a repeated move a different (re-announced) text");
    }

    [Fact]
    public void RowMenu_ItemsAreFencedFromTheRowShortcuts()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var id = Id(doc, "Content");

        OpenRowMenu(cut, id);

        // bUnit cannot dispatch to a stopPropagation-only element, so assert the fence itself: every menu entry sits
        // inside a wrapper that stops keydown, so Alt+Arrow / F2 typed in the open menu never reach OnRowKeyDown.
        var fence = Row(cut, id).QuerySelector(".tm-wd-layers__menu-fence")!;
        fence.Should().NotBeNull();
        fence.Attributes.Select(a => a.Name).Should().Contain(a => a.Contains("onkeydown") && a.Contains("stoppropagation"));
        fence.QuerySelectorAll(".tm-wd-layers__row-menu-item").Length.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void LayerName_HasARenameAccessibleName_AndTheDefaultRowIsDescribed()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);

        var defaultName = Row(cut, Id(doc, "Background")).QuerySelector(".tm-wd-layers__name")!;
        defaultName.GetAttribute("aria-label").Should().Be("Rename layer Background");
        defaultName.GetAttribute("aria-description").Should().Be("Default layer");
        Row(cut, Id(doc, "Content")).QuerySelector(".tm-wd-layers__name")!.HasAttribute("aria-description").Should().BeFalse();
    }

    [Fact]
    public void ToggleTitles_MatchTheirAriaLabels()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var row = Row(cut, Id(doc, "Content"));

        foreach (var selector in new[] { ".tm-wd-layers__visibility", ".tm-wd-layers__lock" })
        {
            var button = row.QuerySelector(selector)!;
            button.GetAttribute("title").Should().Be(button.GetAttribute("aria-label"));
        }
    }

    [Fact]
    public void DragOver_RendersDropIndicator()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var dragged = Id(doc, "Background");
        var target = Id(doc, "Notes");

        Row(cut, dragged).TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, target).TriggerEvent("ondragenter", new DragEventArgs());

        var targetRow = Row(cut, target);
        targetRow.ClassList.Should().Contain("tm-wd-layers__item--drag-over", "the hovered drop target shows the indicator");
        targetRow.GetAttribute("data-drop-position").Should().Be("after",
            "dragging downwards lands after the target, so the line sits under it");
        Row(cut, dragged).ClassList.Should().Contain("tm-wd-layers__item--dragging");

        Row(cut, target).TriggerEvent("ondragleave", new DragEventArgs());
        Row(cut, target).ClassList.Should().NotContain("tm-wd-layers__item--drag-over");

        // Dragging upwards puts the line above the target.
        Row(cut, Id(doc, "Notes")).TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, Id(doc, "Background")).TriggerEvent("ondragenter", new DragEventArgs());
        Row(cut, Id(doc, "Background")).GetAttribute("data-drop-position").Should().Be("before");
    }

    [Fact]
    public void Drop_ReordersThroughTheUndoableCommand_AndClearsTheIndicator()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);

        Row(cut, Id(doc, "Background")).TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, Id(doc, "Notes")).TriggerEvent("ondragenter", new DragEventArgs());
        Row(cut, Id(doc, "Notes")).TriggerEvent("ondrop", new DragEventArgs());

        OrderedNames(doc).Should().Equal(new[] { "Content", "Notes", "Background" });
        stack.NextUndoName.Should().Be("Reorder layers");
        cut.FindAll(".tm-wd-layers__item--drag-over").Should().BeEmpty();
        cut.FindAll(".tm-wd-layers__item--dragging").Should().BeEmpty();
    }

    [Fact]
    public void DefaultLayer_CannotBeDeleted()
    {
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var defaultId = Id(doc, "Background");
        var otherId = Id(doc, "Content");

        Row(cut, defaultId).QuerySelector(".tm-wd-layers__delete").Should().BeNull("the default layer has no trash button");
        cut.FindAll($"[data-layer-id='{defaultId}'] .tm-wd-layers__row-menu-item[data-action='delete']").Should().BeEmpty(
            "nor a Delete entry in its menu");
        Row(cut, otherId).QuerySelector(".tm-wd-layers__delete").Should().NotBeNull();
        OpenRowMenu(cut, otherId);
        RowMenuItem(cut, otherId, "delete").Should().NotBeNull();

        // A single remaining layer can never be deleted either.
        var single = NewDocument();
        var singleCut = Render<TmWireframeLayersPanel>(p => p.Add(c => c.Document, single));
        singleCut.FindAll(".tm-wd-layers__delete").Should().BeEmpty();
        singleCut.FindAll(".tm-wd-layers__row-menu-item[data-action='delete']").Should().BeEmpty();
    }

    [Fact]
    public void Delete_FromTheTrashButton_And_FromTheMenu_AreUndoable()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack);

        Row(cut, Id(doc, "Notes")).QuerySelector(".tm-wd-layers__delete")!.Click();
        doc.Layers.Select(l => l.Name).Should().NotContain("Notes");
        stack.NextUndoName.Should().Be("Remove layer");

        var content = Id(doc, "Content");
        OpenRowMenu(cut, content);
        RowMenuItem(cut, content, "delete").Click();
        cut.WaitForAssertion(() => doc.Layers.Select(l => l.Name).Should().Equal("Background"));

        stack.Undo();
        stack.Undo();
        doc.Layers.Select(l => l.Name).Should().BeEquivalentTo(new[] { "Background", "Content", "Notes" });
    }

    [Fact]
    public void ReadOnly_DisablesAllMutations()
    {
        var doc = NewDocumentWithLayers();
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack, readOnly: true, selected: ["el-1"]);
        var id = Id(doc, "Content");

        cut.Find(".tm-wd-layers__add-btn").HasAttribute("disabled").Should().BeTrue();
        foreach (var selector in new[] { ".tm-wd-layers__active", ".tm-wd-layers__visibility", ".tm-wd-layers__lock", ".tm-wd-layers__delete" })
        {
            cut.FindAll(selector).Should().OnlyContain(b => b.HasAttribute("disabled"), $"{selector} is disabled when read-only");
        }

        cut.FindAll(".tm-wd-layers__row-menu").Should().BeEmpty("the row menu only carries mutations");
        cut.FindAll(".tm-wd-layers__move-section").Should().BeEmpty("no move-to-layer while read-only");
        cut.FindAll(".tm-wd-layers__item").Should().OnlyContain(r => r.GetAttribute("draggable") == "false",
            "rows are not draggable");

        // Keyboard shortcuts and double-click are inert.
        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "F2" });
        Row(cut, id).KeyDown(new KeyboardEventArgs { Key = "ArrowDown", AltKey = true });
        Row(cut, id).QuerySelector(".tm-wd-layers__name")!.DoubleClick();
        Row(cut, Id(doc, "Background")).TriggerEvent("ondragstart", new DragEventArgs());
        Row(cut, Id(doc, "Notes")).TriggerEvent("ondrop", new DragEventArgs());

        cut.FindAll(".tm-wd-layers__name-input").Should().BeEmpty();
        OrderedNames(doc).Should().Equal(new[] { "Background", "Content", "Notes" });
        stack.CanUndo.Should().BeFalse("nothing was pushed");
    }

    [Fact]
    public void MoveToLayer_VisibleOnlyWithSelection()
    {
        var doc = NewDocumentWithLayers();

        RenderPanel(doc).FindAll(".tm-wd-layers__move-section").Should().BeEmpty("nothing selected");

        var cut = RenderPanel(doc, selected: ["el-1"]);
        var section = cut.Find(".tm-wd-layers__move-section");
        section.TextContent.Should().Contain("Move to layer");
        section.QuerySelector("select")!.GetAttribute("id").Should().NotBeNullOrEmpty();
        section.QuerySelector("label")!.GetAttribute("for").Should().Be(section.QuerySelector("select")!.GetAttribute("id"),
            "the label is associated with the select");
    }

    [Fact]
    public void MoveToLayer_PushesUndoableMoveCommand()
    {
        var doc = NewDocumentWithLayers();
        var element = new WireframeElement { Id = "el-1", Type = "TmButton", LayerId = Id(doc, "Background") };
        doc.ActivePage!.Elements.Add(element);
        var stack = new WireframeCommandStack();
        var cut = RenderPanel(doc, stack, selected: ["el-1"]);

        cut.Find(".tm-wd-layers__move-section select").Change(Id(doc, "Notes"));

        element.LayerId.Should().Be(Id(doc, "Notes"));
        stack.NextUndoName.Should().Be("Move elements to layer");
        stack.Undo();
        element.LayerId.Should().Be(Id(doc, "Background"));
    }

    [Fact]
    public void CollapseToggle_Hidden_WhenHostedInPanel()
    {
        var doc = NewDocumentWithLayers();

        var standalone = RenderPanel(doc);
        standalone.FindAll(".tm-wd-layers__toggle").Should().HaveCount(1, "default = today's standalone chrome");
        standalone.Find(".tm-wd-layers").ClassList.Should().NotContain("tm-wd-layers--embedded");

        var hosted = RenderPanel(doc, showCollapseToggle: false);
        hosted.FindAll(".tm-wd-layers__toggle").Should().BeEmpty("a host that owns the collapse hides the built-in tab");
        hosted.Find(".tm-wd-layers").ClassList.Should().Contain("tm-wd-layers--embedded");
        hosted.Find(".tm-wd-layers").GetAttribute("data-collapsed").Should().NotBe("True", "an embedded panel is never collapsed");
    }

    [Fact]
    public void ActiveRow_HasActiveClass_AndHiddenLayerIsMarked()
    {
        var doc = NewDocumentWithLayers();
        doc.Layers.First(l => l.Name == "Notes").IsVisible = false;
        var cut = Render<TmWireframeLayersPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.ActiveLayerId, Id(doc, "Content")));

        Row(cut, Id(doc, "Content")).ClassList.Should().Contain("tm-wd-layers__item--active");
        Row(cut, Id(doc, "Background")).ClassList.Should().NotContain("tm-wd-layers__item--active");
        Row(cut, Id(doc, "Notes")).ClassList.Should().Contain("tm-wd-layers__item--hidden");
    }

    [Fact]
    public void RowOrder_FollowsLayerOrder()
    {
        var doc = NewDocumentWithLayers();
        doc.Layers.First(l => l.Name == "Notes").Order = -1;
        var cut = RenderPanel(doc);

        cut.FindAll(".tm-wd-layers__name").Select(n => n.TextContent.Trim())
            .Should().Equal(new[] { "Notes", "Background", "Content" });
    }

    [Fact]
    public void Czech_RowControls_AreLocalized()
    {
        UseCzechLocalization();
        var doc = NewDocumentWithLayers();
        var cut = RenderPanel(doc);
        var id = Id(doc, "Content");

        cut.Find("[role='radiogroup']").GetAttribute("aria-label").Should().Be("Aktivní vrstva");
        Row(cut, id).QuerySelector(".tm-wd-layers__visibility")!.GetAttribute("aria-label").Should().Be("Zobrazit vrstvu Content");
        Row(cut, id).QuerySelector(".tm-wd-layers__lock")!.GetAttribute("aria-label").Should().Be("Zamknout vrstvu Content");
        OpenRowMenu(cut, id);
        RowMenuItem(cut, id, "up").TextContent.Trim().Should().Be("Posunout výš");
        RowMenuItem(cut, id, "down").TextContent.Trim().Should().Be("Posunout níž");
        RowMenuItem(cut, id, "rename").TextContent.Trim().Should().Be("Přejmenovat vrstvu");
    }
}
