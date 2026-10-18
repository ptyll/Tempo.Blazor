using System.Reflection;
using AngleSharp.Dom;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Diagram;
using Tempo.Blazor.Components.Diagram.Commands;
using Tempo.Blazor.Components.Diagram.Models;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Diagram;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Components.Diagram;

/// <summary>
/// bUnit tests for the layers panel: active/drag-over row classes, localized
/// new-layer names, list/toggle-button semantics, keyboard rename, the core
/// TmContextMenu row menu, focus restoration, announcements, hidden-row styling
/// and data-testid.
/// </summary>
public class TmDiagramLayersPanelTests : DiagramTestBase
{
    // TmOverlayPanel renders the items even while the popover is closed, so row-menu items are
    // always scoped to their row: [data-layer-id='…'] .tm-diagram-layers__row-menu-item--{kind}.
    private static void OpenRowMenu(IRenderedComponent<TmDiagramLayersPanel> cut, string layerId)
        => cut.Find($"[data-layer-id='{layerId}'] .tm-diagram-layers__row-menu .tm-context-menu__trigger").Click();

    private static IElement RowItem(IRenderedComponent<TmDiagramLayersPanel> cut, string layerId, string kind)
        => cut.Find($"[data-layer-id='{layerId}'] .tm-diagram-layers__row-menu-item--{kind}");

    /// <summary>The row's core TmContextMenu component, resolved through the panel's private
    /// per-row holder dictionary (the same reference the panel's focus hand-back uses).</summary>
    private static TmContextMenu RowMenu(IRenderedComponent<TmDiagramLayersPanel> cut, string layerId)
    {
        var holders = (System.Collections.IDictionary)typeof(TmDiagramLayersPanel)
            .GetField("_rowMenuRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        var holder = holders[layerId]!;
        return (TmContextMenu)holder.GetType().GetProperty("Menu")!.GetValue(holder)!;
    }

    private static ElementReference TriggerRef(TmContextMenu menu)
        => (ElementReference)typeof(TmContextMenu)
            .GetField("_triggerRef", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(menu)!;

    /// <summary>The last domWrapper.focus invocation must target the given element reference.</summary>
    private void AssertLastFocusIs(IRenderedComponent<TmDiagramLayersPanel> cut, ElementReference expected, string because)
    {
        cut.WaitForAssertion(() =>
        {
            var invocation = JSInterop.Invocations
                .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
                .Last();
            var focused = invocation.Arguments.OfType<ElementReference>().First();
            focused.Id.Should().Be(expected.Id, because);
        });
    }

    /// <summary>The last domWrapper.focus invocation must target the given layer's name span.</summary>
    private void AssertLastFocusIsLayerName(IRenderedComponent<TmDiagramLayersPanel> cut, string layerId)
    {
        var nameRefs = (Dictionary<string, ElementReference>)typeof(TmDiagramLayersPanel)
            .GetField("_nameRefs", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        AssertLastFocusIs(cut, nameRefs[layerId], "focus must land on the layer's name");
    }

    private static DiagramDocument CreateDocument()
    {
        var doc = new DiagramDocument();
        doc.Layers.Add(new DiagramLayer { Name = "Base", Order = 0 });
        doc.Layers.Add(new DiagramLayer { Name = "Annotations", Order = 1 });
        return doc;
    }

    [Fact]
    public void ActiveRow_HasActiveClass()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var activeRow = cut.Find($"[data-layer-id='{doc.Layers[1].Id}']");
        activeRow.ClassList.Should().Contain("tm-diagram-layers__item--active");

        var inactiveRow = cut.Find($"[data-layer-id='{doc.Layers[0].Id}']");
        inactiveRow.ClassList.Should().NotContain("tm-diagram-layers__item--active");
    }

    [Fact]
    public void DragOverRow_HasDragOverClass()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var rows = cut.FindAll(".tm-diagram-layers__item");
        rows[0].GetAttribute("draggable").Should().Be("true", "rows are draggable in edit mode");

        var dragged = cut.Find($"[data-layer-id='{doc.Layers[0].Id}']");
        dragged.TriggerEvent("ondragstart", new Microsoft.AspNetCore.Components.Web.DragEventArgs());

        var target = cut.Find($"[data-layer-id='{doc.Layers[1].Id}']");
        target.TriggerEvent("ondragenter", new Microsoft.AspNetCore.Components.Web.DragEventArgs());
        cut.Render();

        target.ClassList.Should().Contain("tm-diagram-layers__item--drag-over");
    }

    [Fact]
    public void NewLayer_HasLocalizedName()
    {
        UseCzechLocalization();
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        var names = cut.FindAll(".tm-diagram-layers__name").Select(n => n.TextContent.Trim()).ToList();
        names.Should().Contain("Nová vrstva 3",
            "the new layer name must come from TmDiagramLayers_NewLayerName with a {0} ordinal");
        names.Should().NotContain("Layer 3");
    }

    [Fact]
    public void ActiveSelector_IsToggleButtonPerRow_InAList()
    {
        // D8: the list is a plain ul/li list and the active-layer control is a toggle button
        // with aria-pressed — the rows carry five other controls, so the radio model did not fit.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var group = cut.Find(".tm-diagram-layers__list");
        group.TagName.Should().Be("UL");

        cut.FindAll("[role='radiogroup']").Should().BeEmpty();
        cut.FindAll("[role='radio']").Should().BeEmpty();

        var actives = cut.FindAll(".tm-diagram-layers__active");
        actives.Should().HaveCount(2);
        actives.Count(a => a.GetAttribute("aria-pressed") == "true").Should().Be(1);
        actives.Count(a => a.GetAttribute("aria-pressed") == "false").Should().Be(1);
    }

    [Fact]
    public async Task EnterKey_And_F2_StartRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        nameSpan.GetAttribute("tabindex").Should().Be("0", "the layer name must be keyboard focusable");

        await cut.InvokeAsync(() => nameSpan.KeyDown("F2"));
        cut.Render();
        cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public async Task EnterKey_StartsRename_ExactlyOnce()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));

        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();
        cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull();
    }

    [Fact]
    public async Task EnterKey_InInput_CommitsRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("Nový název");
        await cut.InvokeAsync(() => input.KeyDown("Enter"));
        cut.Render();

        doc.Layers.First(l => l.Name == "Nový název").Should().NotBeNull(
            "Enter in the rename input must commit the new name");
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "Enter in the rename input must end the edit");
        cut.FindAll(".tm-diagram-layers__name")
            .Should().Contain(n => n.TextContent == "Nový název");
    }

    [Fact]
    public async Task EscapeKey_InInput_CancelsRename()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("Jiný název");
        await cut.InvokeAsync(() => input.KeyDown("Escape"));
        cut.Render();

        doc.Layers[1].Name.Should().Be("Annotations",
            "Escape in the rename input must discard the edit");
        cut.FindAll(".tm-diagram-layers__name-input").Should().BeEmpty(
            "Escape in the rename input must end the edit");
    }

    [Fact]
    public async Task SpaceKeyUp_InInput_DoesNotStealSpace()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        var input = cut.Find(".tm-diagram-layers__name-input");
        input.Input("My Layer");

        input.Invoking(i => i.KeyUp(" "))
            .Should().Throw<Bunit.MissingEventHandlerException>(
                "the rename input must not have a Space keyup handler — spaces in the name belong to the user");
        cut.FindAll(".tm-diagram-layers__name-input").Should().HaveCount(1,
            "Space inside the input must neither end nor restart the edit");
        doc.Layers[1].Name.Should().Be("Annotations");
    }

    [Fact]
    public void MoveUpDownMenu_PushesReorderCommand()
    {
        // Non-drag alternative to the layer DnD (WCAG 2.5.7 / touch): every row's "⋯"
        // menu offers Rename, Lock, Move up, Move down, Move selected here and Delete —
        // moving pushes the same undoable ReorderLayersCommand the drop does.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        OpenRowMenu(cut, doc.Layers[1].Id);
        cut.Render();

        cut.Find($"[data-layer-id='{doc.Layers[1].Id}'] .tm-diagram-layers__row-menu").Should().NotBeNull("the row menu opens");
        RowItem(cut, doc.Layers[1].Id, "up").Click();

        // The item closes the menu before its OnClick runs (N319), so the reorder lands async.
        cut.WaitForAssertion(() =>
        {
            doc.Layers.First(l => l.Name == "Annotations").Order.Should().Be(0,
                "Move up pushes the same ReorderLayersCommand as the DnD drop");
            doc.Layers.First(l => l.Name == "Base").Order.Should().Be(1);
        });
    }

    [Fact]
    public void MoveDownMenu_PushesReorderCommand()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "down").Click();

        cut.WaitForAssertion(() =>
        {
            doc.Layers.First(l => l.Name == "Base").Order.Should().Be(1);
            doc.Layers.First(l => l.Name == "Annotations").Order.Should().Be(0);
        });
    }

    [Fact]
    public void RowMenu_OffersRenameAndDelete()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[1].Id);
        cut.Render();

        var items = cut.FindAll($"[data-layer-id='{doc.Layers[1].Id}'] .tm-diagram-layers__row-menu-item").ToList();
        items.Should().Contain(i => i.ClassList.Contains("tm-diagram-layers__row-menu-item--rename"));
        items.Should().Contain(i => i.ClassList.Contains("tm-diagram-layers__row-menu-item--lock"));
        items.Should().Contain(i => i.ClassList.Contains("tm-diagram-layers__row-menu-item--up"));
        items.Should().Contain(i => i.ClassList.Contains("tm-diagram-layers__row-menu-item--down"));
        items.Should().Contain(i => i.ClassList.Contains("tm-diagram-layers__row-menu-item--delete"));
    }

    [Fact]
    public void RowMenu_RenameEntry_StartsInlineEdit()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "rename").Click();

        cut.WaitForAssertion(() => cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull());
    }

    [Fact]
    public void RowMenu_DeleteEntry_RemovesLayer()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        OpenRowMenu(cut, doc.Layers[1].Id);
        cut.Render();
        RowItem(cut, doc.Layers[1].Id, "delete").Click();

        cut.WaitForAssertion(() => doc.Layers.Should().ContainSingle().Which.Name.Should().Be("Base"));
    }

    [Fact]
    public void RowMenu_Trigger_HasAccessibleName()
    {
        // D4: the trigger is the core TmContextMenu's div[role=button] — it owns aria-haspopup /
        // aria-expanded and the .tm-sr-only span names the menu after its layer.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var trigger = cut.Find($"[data-layer-id='{doc.Layers[0].Id}'] .tm-diagram-layers__row-menu .tm-context-menu__trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().BeOneOf("true", "false",
            "the menu trigger reports its open state (a raw bool attribute renders empty)");
        trigger.QuerySelector(".tm-sr-only")!.TextContent.Trim().Should().Be("Actions for layer Base");
    }

    [Fact]
    public void ActiveRadio_AriaLabel_ContainsLayerName()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var actives = cut.FindAll(".tm-diagram-layers__active").ToList();
        actives.Should().HaveCount(2);
        actives[0].GetAttribute("aria-label").Should().Contain("Base",
            "each active-toggle's aria-label must identify its layer");
        actives[1].GetAttribute("aria-label").Should().Contain("Annotations");
        actives[0].GetAttribute("aria-label").Should().NotBe(actives[1].GetAttribute("aria-label"));
    }

    [Fact]
    public void DataTestId_RendersOnRoot()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.DataTestId, "my-layers"));

        cut.Find("[data-testid='my-layers']").Should().NotBeNull();
    }

    [Fact]
    public void Rows_AreKeyedByLayerId_SoAMoveKeepsTheRowElement()
    {
        // D4: without @key the keyed diff reuses DOM nodes by position, so after "Move up" the
        // focused row element silently points at another layer and focus drops to <body>.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var movedId = doc.Layers[1].Id;
        OpenRowMenu(cut, movedId);
        cut.Render();
        RowItem(cut, movedId, "up").Click();

        cut.WaitForAssertion(() =>
            cut.FindAll(".tm-diagram-layers__item")[0].GetAttribute("data-layer-id").Should().Be(movedId,
                "the moved layer renders in the first row after Move up"));
    }

    [Fact]
    public void MoveUp_FocusesTheMovedLayersRowMenuTrigger()
    {
        // After a menu-driven move the focus hand-back lands on the moved row's "⋯" trigger
        // (the row element survives the reorder through @key), not on the name span.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var movedId = doc.Layers[1].Id;
        OpenRowMenu(cut, movedId);
        cut.Render();
        RowItem(cut, movedId, "up").Click();

        AssertLastFocusIs(cut, TriggerRef(RowMenu(cut, movedId)),
            "focus must land on the moved row's ⋯ trigger");
    }

    [Fact]
    public void DeleteLayer_FocusesTheNeighbourRowMenuTrigger()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var deletedId = doc.Layers[1].Id;
        var neighbourId = doc.Layers[0].Id;
        OpenRowMenu(cut, deletedId);
        cut.Render();
        RowItem(cut, deletedId, "delete").Click();

        cut.WaitForAssertion(() => doc.Layers.Should().ContainSingle());
        AssertLastFocusIs(cut, TriggerRef(RowMenu(cut, neighbourId)),
            "focus must land on the neighbour row's ⋯ trigger");
    }

    [Fact]
    public void AddLayer_FocusesTheNewLayersName()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        cut.WaitForAssertion(() => doc.Layers.Should().HaveCount(3));
        AssertLastFocusIsLayerName(cut, doc.Layers[2].Id);
    }

    [Fact]
    public void AddLayer_WithConstantStringLocalizer_TwoAddsFinish_WithUniqueNames()
    {
        // A host localizer that ignores the ordinal maps every probe to the same text — the
        // bounded probe (≤ existing.Count + 1 tries) must give up and fall back to a unique
        // short suffix, so two consecutive adds always finish and stay distinct.
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmDiagramLayers_NewLayerName"] = "Layer",
        });
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();
        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        cut.WaitForAssertion(() => doc.Layers.Should().HaveCount(4));
        doc.Layers.Select(l => l.Name).Should().OnlyHaveUniqueItems();
        doc.Layers.Should().Contain(l => l.Name != "Layer",
            "the fallback must produce a distinct name when every ordinal collides");
    }

    [Fact]
    public void ReadOnly_HidesTheRowMenu()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.ReadOnly, true));

        cut.FindAll(".tm-diagram-layers__row-menu").Should().BeEmpty(
            "read-only renders no row menu, hence no delete path");
        cut.FindAll(".tm-diagram-layers__lock, .tm-diagram-layers__delete").Should().BeEmpty(
            "read-only renders no inline lock/delete buttons either");
    }

    [Fact]
    public void RowMenu_MoveSelectedHere_MovesTheSelectedNodes()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 0, Y = 0, W = 80, H = 40, LayerId = doc.Layers[0].Id };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.SelectedIds, [node.Id]));

        OpenRowMenu(cut, doc.Layers[1].Id);
        cut.Render();
        RowItem(cut, doc.Layers[1].Id, "move-selected").Click();

        cut.WaitForAssertion(() =>
            node.LayerId.Should().Be(doc.Layers[1].Id));
    }

    [Fact]
    public void RowMenu_MoveSelectedHere_HiddenWithoutSelection()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();

        cut.FindAll(".tm-diagram-layers__row-menu-item--move-selected").Should().BeEmpty(
            "with nothing selected the row menu offers no move-selected entry");
    }

    [Fact]
    public async Task Row_IsNotDraggable_WhileRenaming()
    {
        // D8(a): draggable=true while the rename input is up starts a row drag when the user
        // only wants to select the text — the row must not be draggable during the edit.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        cut.Find(".tm-diagram-layers__name-input").Should().NotBeNull();
        cut.Find(".tm-diagram-layers__name-input").Closest(".tm-diagram-layers__item")!
            .GetAttribute("draggable").Should().Be("false");
        cut.FindAll(".tm-diagram-layers__item")[0].GetAttribute("draggable").Should().Be("true",
            "the row that is not being edited stays draggable");
    }

    [Fact]
    public void Row_IsNotDraggable_WhenReadOnly()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.ReadOnly, true));

        cut.FindAll(".tm-diagram-layers__item")
            .Should().OnlyContain(r => r.GetAttribute("draggable") == "false",
                "read-only rows must not be draggable");
    }

    [Fact]
    public async Task EnterCommit_ReturnsFocusToTheName()
    {
        // D8(b): without a focus hand-back, committing with Enter drops focus on <body>.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var layerId = doc.Layers[1].Id;
        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        cut.Find(".tm-diagram-layers__name-input").Input("Committed");
        await cut.InvokeAsync(() => cut.Find(".tm-diagram-layers__name-input").KeyDown("Enter"));
        cut.Render();

        AssertLastFocusIsLayerName(cut, layerId);
    }

    [Fact]
    public async Task EscapeCancel_ReturnsFocusToTheName()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var layerId = doc.Layers[1].Id;
        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Annotations"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        cut.Find(".tm-diagram-layers__name-input").Input("Whatever");
        await cut.InvokeAsync(() => cut.Find(".tm-diagram-layers__name-input").KeyDown("Escape"));
        cut.Render();

        doc.Layers[1].Name.Should().Be("Annotations", "Escape discards the rename");
        AssertLastFocusIsLayerName(cut, layerId);
    }

    [Fact]
    public async Task Rename_WithOnlyTrailingSpace_PushesNoCommand()
    {
        // D8(c): "Base " used to commit a RenameLayerCommand("Base") — an empty undo step.
        var stack = new DiagramCommandStack();
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .AddCascadingValue(stack));

        var nameSpan = cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Base"));
        await cut.InvokeAsync(() => nameSpan.KeyDown("Enter"));
        cut.Render();

        cut.Find(".tm-diagram-layers__name-input").Input("Base ");
        await cut.InvokeAsync(() => cut.Find(".tm-diagram-layers__name-input").KeyDown("Enter"));
        cut.Render();

        stack.CanUndo.Should().BeFalse("a trimmed no-op rename must not push an undo step");
        doc.Layers[0].Name.Should().Be("Base");
    }

    [Fact]
    public void NewLayer_NameIsUnique_AfterDelete()
    {
        // D8(e): Base + Annotations → add → add → delete "New layer 4" → add used to create
        // a duplicate "New layer 4". The next name picks the smallest free ordinal.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();
        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        var duplicate = doc.Layers.First(l => l.Name == "New layer 4");
        OpenRowMenu(cut, duplicate.Id);
        cut.Render();
        RowItem(cut, duplicate.Id, "delete").Click();

        cut.WaitForAssertion(() =>
        {
            doc.Layers.Select(l => l.Name).Should().OnlyHaveUniqueItems();
            doc.Layers.Should().NotContain(l => l.Name == "New layer 4");
        });

        cut.Find(".tm-diagram-layers__add-btn").Click();
        cut.Render();

        doc.Layers.Select(l => l.Name).Should().OnlyHaveUniqueItems();
        doc.Layers.Should().Contain(l => l.Name == "New layer 4",
            "the freed ordinal is reused — no duplicate new-layer names");
    }

    [Fact]
    public void Embedded_HasNoCollapseToggle_AndFillsWidth()
    {
        // D17: TmDiagramEditor owns the collapse — the embedded panel has no own ">" tab and
        // fills the host width (the standalone 220px panel is for lone use).
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.ShowCollapseToggle, false));

        cut.FindAll(".tm-diagram-layers__toggle").Should().BeEmpty(
            "the embedded panel renders no own collapse toggle");
        cut.Find(".tm-diagram-layers").ClassList.Should().Contain("tm-diagram-layers--embedded");
    }

    [Fact]
    public void Standalone_KeepsCollapseToggle()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find(".tm-diagram-layers__toggle").Should().NotBeNull();
        cut.Find(".tm-diagram-layers").ClassList.Should().NotContain("tm-diagram-layers--embedded");
    }

    [Fact]
    public void LockedLayer_ShowsIndicator_AndMenuOffersUnlock()
    {
        // D17/X5: the lock is a non-interactive indicator; the actual toggle lives in the ⋯ menu.
        var doc = CreateDocument();
        doc.Layers[0].IsLocked = true;
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find($"[data-layer-id='{doc.Layers[0].Id}'] .tm-diagram-layers__locked").Should().NotBeNull(
            "a locked layer shows the indicator");
        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "lock").TextContent.Trim().Should().Be("Unlock",
            "the menu entry unlocks a locked layer");
        RowItem(cut, doc.Layers[0].Id, "lock").Click();

        cut.WaitForAssertion(() => doc.Layers[0].IsLocked.Should().BeFalse());
    }

    [Fact]
    public void UnlockedLayer_HasNoLockIndicator()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.FindAll(".tm-diagram-layers__locked").Should().BeEmpty(
            "an unlocked layer shows no lock indicator");
    }

    [Fact]
    public void Row_HasNoInlineLockOrDeleteButtons()
    {
        // D17: the inline lock and delete buttons are gone — lock/unlock and delete live in ⋯.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.FindAll(".tm-diagram-layers__lock, .tm-diagram-layers__delete").Should().BeEmpty();
    }

    // ── Polite live region announcements ─────────────────────────────────────

    private static IElement StatusRegion(IRenderedComponent<TmDiagramLayersPanel> cut)
        => cut.Find(".tm-sr-only[role='status'][aria-live='polite']");

    [Fact]
    public void LiveRegion_AnnouncesVisibilityChanges()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var button = cut.Find($"[data-layer-id='{doc.Layers[0].Id}'] .tm-diagram-layers__visibility");
        button.Click();
        cut.Render();
        StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base hidden");
        doc.Layers[0].IsVisible.Should().BeFalse();

        button = cut.Find($"[data-layer-id='{doc.Layers[0].Id}'] .tm-diagram-layers__visibility");
        button.Click();
        cut.Render();
        StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base shown");
    }

    [Fact]
    public void LiveRegion_AnnouncesLockAndUnlock()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "lock").Click();
        cut.WaitForAssertion(() =>
        {
            doc.Layers[0].IsLocked.Should().BeTrue();
            StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base locked");
        });

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "lock").Click();
        cut.WaitForAssertion(() =>
        {
            doc.Layers[0].IsLocked.Should().BeFalse();
            StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base unlocked");
        });
    }

    [Fact]
    public void LiveRegion_AnnouncesMoveDownAndDelete()
    {
        // Move DOWN (not up): after a move-up the row would become the default layer, which
        // carries no delete entry.
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "down").Click();
        cut.WaitForAssertion(() =>
            StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base moved down"));

        OpenRowMenu(cut, doc.Layers[0].Id);
        cut.Render();
        RowItem(cut, doc.Layers[0].Id, "delete").Click();
        cut.WaitForAssertion(() =>
        {
            doc.Layers.Should().ContainSingle();
            StatusRegion(cut).TextContent.Trim().Should().Be("Layer Base deleted");
        });
    }

    [Fact]
    public void LiveRegion_AnnouncesNodesMovedToLayer()
    {
        var doc = CreateDocument();
        var node = new DiagramNode { Id = "n1", StencilId = "general.rectangle", X = 0, Y = 0, W = 80, H = 40, LayerId = doc.Layers[0].Id };
        doc.Nodes.Add(node);

        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id)
            .Add(x => x.SelectedIds, [node.Id]));

        OpenRowMenu(cut, doc.Layers[1].Id);
        cut.Render();
        RowItem(cut, doc.Layers[1].Id, "move-selected").Click();

        cut.WaitForAssertion(() =>
        {
            node.LayerId.Should().Be(doc.Layers[1].Id);
            StatusRegion(cut).TextContent.Trim().Should().Be("Nodes moved to layer Annotations");
        });
    }

    // ── Hidden row, list labelling, aria-current ──────────────────────────────

    [Fact]
    public void HiddenLayer_RowCarriesTheHiddenClass()
    {
        var doc = CreateDocument();
        doc.Layers[0].IsVisible = false;
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.Find($"[data-layer-id='{doc.Layers[0].Id}']")
            .ClassList.Should().Contain("tm-diagram-layers__item--hidden",
                "a hidden layer's row is muted + italic through the --hidden class");
        cut.Find($"[data-layer-id='{doc.Layers[1].Id}']")
            .ClassList.Should().NotContain("tm-diagram-layers__item--hidden");
    }

    [Fact]
    public void List_IsLabelledByThePerInstanceTitle_AndActiveRowCarriesAriaCurrent()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[1].Id));

        var titleId = cut.Find(".tm-diagram-layers__title").GetAttribute("id");
        titleId.Should().NotBeNullOrEmpty("the heading carries a per-instance id");
        cut.Find(".tm-diagram-layers__list").GetAttribute("aria-labelledby").Should().Be(titleId);

        cut.Find($"[data-layer-id='{doc.Layers[1].Id}']").GetAttribute("aria-current")
            .Should().Be("true", "the active row is the current one");
        cut.Find($"[data-layer-id='{doc.Layers[0].Id}']").HasAttribute("aria-current")
            .Should().BeFalse("inactive rows carry no aria-current");
    }

    [Fact]
    public void VisibilityButton_LabelsNameTheLayer()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        var button = cut.Find($"[data-layer-id='{doc.Layers[0].Id}'] .tm-diagram-layers__visibility");
        button.GetAttribute("aria-label").Should().Be("Hide layer Base");
        button.GetAttribute("title").Should().Be("Hide layer Base");
        button.HasAttribute("aria-pressed").Should().BeFalse("visibility is not a toggle-button");
    }

    [Fact]
    public void NameTitle_DoubleClickOrF2()
    {
        var doc = CreateDocument();
        var cut = Render<TmDiagramLayersPanel>(p => p
            .Add(x => x.Document, doc)
            .Add(x => x.ActiveLayerId, doc.Layers[0].Id));

        cut.FindAll(".tm-diagram-layers__name")
            .First(n => n.TextContent.Contains("Base"))
            .GetAttribute("title").Should().Be("Base — double-click or F2 to rename");
    }

    [Fact]
    public void SingleStyleSource()
    {        // The panel's styles lived twice: in the component's scoped stylesheet AND as a legacy
        // copy inside the package's global _diagram-editor.css. Two sources drift silently —
        // the scoped copy wins on specificity, so edits to the global one never showed up.
        // Freeze: every .tm-diagram-layers* rule must live in exactly ONE source, the component's
        // own TmDiagramLayersPanel.razor.css (P21 ownership).
        var src = Path.Combine(ThemeCss.RepositoryRoot().FullName, "src");
        var excluded = new[] { $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}" };
        var sources = Directory.EnumerateFiles(src, "*.css", SearchOption.AllDirectories)
            .Where(path => !excluded.Any(path.Contains))
            .Where(path => File.ReadAllText(path).Contains(".tm-diagram-layers"))
            .Select(Path.GetFileName)
            .ToList();

        sources.Should().ContainSingle("the layers panel styles must have exactly one CSS source")
            .Which.Should().Be("TmDiagramLayersPanel.razor.css");
    }
}
