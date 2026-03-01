using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

/// <summary>TDD tests for TmWorkflowDesignerCanvas.</summary>
public class TmWorkflowDesignerCanvasTests : LocalizationTestBase
{
    public TmWorkflowDesignerCanvasTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static WorkflowCanvasDefinition SimpleWorkflow => new()
    {
        States =
        [
            new CanvasState { Id = "s1", Name = "Draft", Type = CanvasStateType.Initial, X = 50, Y = 100 },
            new CanvasState { Id = "s2", Name = "Review", Type = CanvasStateType.Intermediate, X = 250, Y = 100 },
            new CanvasState { Id = "s3", Name = "Approved", Type = CanvasStateType.Final, X = 450, Y = 100 },
        ],
        Transitions =
        [
            new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" },
            new CanvasTransition { Id = "t2", FromStateId = "s2", ToStateId = "s3", Label = "Approve" },
        ]
    };

    // ── Render basics ──

    [Fact]
    public void Canvas_Renders_SvgElement()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Find(".tm-wf-canvas").Should().NotBeNull();
        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void Canvas_EmptyDefinition_RendersSvg()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, new WorkflowCanvasDefinition()));

        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void Canvas_ShowGrid_RendersGridPattern()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ShowGrid, true));

        cut.Find("pattern").Should().NotBeNull();
    }

    [Fact]
    public void Canvas_HideGrid_NoPattern()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ShowGrid, false));

        cut.FindAll("pattern").Count.Should().Be(0);
    }

    // ── State nodes ──

    [Fact]
    public void Canvas_RendersStateNodes()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-state").Count.Should().Be(3);
    }

    [Fact]
    public void Canvas_StateLabels_Shown()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Markup.Should().Contain("Draft");
        cut.Markup.Should().Contain("Review");
        cut.Markup.Should().Contain("Approved");
    }

    [Fact]
    public void Canvas_StateType_CssClasses()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-state--initial").Count.Should().Be(1);
        cut.FindAll(".tm-wf-state--intermediate").Count.Should().Be(1);
        cut.FindAll(".tm-wf-state--final").Count.Should().Be(1);
    }

    [Fact]
    public void Canvas_StateColor_Applied()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Colored", X = 50, Y = 50, Color = "#ef4444" }]
        };

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def));

        var stateEl = cut.Find(".tm-wf-state");
        // Color should be present in the rendered markup (either fill or style)
        cut.Markup.Should().Contain("#ef4444");
    }

    // ── Transitions ──

    [Fact]
    public void Canvas_RendersTransitionPaths()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-transition").Count.Should().Be(2);
    }

    [Fact]
    public void Canvas_TransitionLabels_Shown()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Markup.Should().Contain("Submit");
        cut.Markup.Should().Contain("Approve");
    }

    [Fact]
    public void Canvas_ArrowMarker_Defined()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Find("marker").Should().NotBeNull();
    }

    // ── Selection ──

    [Fact]
    public void Canvas_ClickState_FiresOnStateSelected()
    {
        CanvasState? selected = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.OnStateSelected, s => selected = s));

        cut.FindAll(".tm-wf-state")[0].Click();

        selected.Should().NotBeNull();
        selected!.Name.Should().Be("Draft");
    }

    [Fact]
    public void Canvas_SelectedStateId_Highlights()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.SelectedStateId, "s2"));

        cut.FindAll(".tm-wf-state--selected").Count.Should().Be(1);
    }

    [Fact]
    public void Canvas_ClickTransition_FiresOnTransitionSelected()
    {
        CanvasTransition? selected = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.OnTransitionSelected, t => selected = t));

        cut.FindAll(".tm-wf-transition")[0].Click();

        selected.Should().NotBeNull();
        selected!.Label.Should().Be("Submit");
    }

    [Fact]
    public void Canvas_SelectedTransitionId_Highlights()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.SelectedTransitionId, "t1"));

        cut.FindAll(".tm-wf-transition--selected").Count.Should().Be(1);
    }

    // ── ReadOnly ──

    [Fact]
    public void Canvas_ReadOnly_HasClass()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ReadOnly, true));

        cut.Find(".tm-wf-canvas").ClassList.Should().Contain("tm-wf-canvas--readonly");
    }

    // ── Custom class ──

    [Fact]
    public void Canvas_CustomClass()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.Class, "my-workflow"));

        cut.Find(".tm-wf-canvas").ClassList.Should().Contain("my-workflow");
    }

    // ── State data attributes (for JS interop) ──

    [Fact]
    public void Canvas_StateNodes_HaveDataStateId()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        var states = cut.FindAll("[data-state-id]");
        states.Count.Should().Be(3);
        states[0].GetAttribute("data-state-id").Should().Be("s1");
        states[1].GetAttribute("data-state-id").Should().Be("s2");
    }

    [Fact]
    public void Canvas_Transitions_HaveDataTransitionId()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        var transitions = cut.FindAll("[data-transition-id]");
        transitions.Count.Should().Be(2);
        transitions[0].GetAttribute("data-transition-id").Should().Be("t1");
    }

    // ── Connection ports ──

    [Fact]
    public void Canvas_States_HaveConnectionPorts()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        // Each non-readonly state should have a port element
        cut.FindAll("[data-port]").Count.Should().Be(3);
    }

    [Fact]
    public void Canvas_ReadOnly_NoPorts()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ReadOnly, true));

        cut.FindAll("[data-port]").Count.Should().Be(0);
    }

    // ── Drag node (JSInvokable) ──

    [Fact]
    public void Canvas_OnNodeDragged_UpdatesPosition()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Test", X = 50, Y = 100 }]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Simulate JS calling OnNodeDragged
        cut.Instance.OnNodeDragged("s1", 200, 300);

        changed.Should().NotBeNull();
        changed!.States[0].X.Should().Be(200);
        changed!.States[0].Y.Should().Be(300);
    }

    [Fact]
    public void Canvas_OnNodeDragged_SnapToGrid()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Test", X = 50, Y = 100 }]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.GridSize, 20)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Position 37, 43 should snap to 40, 40
        cut.Instance.OnNodeDragged("s1", 37, 43);

        changed.Should().NotBeNull();
        changed!.States[0].X.Should().Be(40);
        changed!.States[0].Y.Should().Be(40);
    }

    [Fact]
    public void Canvas_OnNodeDragged_UnknownState_NoChange()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Test", X = 50, Y = 100 }]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.OnNodeDragged("unknown", 200, 300);

        changed.Should().BeNull();
    }

    // ── Add / Remove states ──

    [Fact]
    public void Canvas_AddState_IncreasesCount()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Draft", X = 50, Y = 100 }]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.AddState(CanvasStateType.Intermediate, 200, 200);

        changed.Should().NotBeNull();
        changed!.States.Count.Should().Be(2);
        changed!.States[1].Type.Should().Be(CanvasStateType.Intermediate);
        changed!.States[1].X.Should().Be(200);
        changed!.States[1].Y.Should().Be(200);
    }

    [Fact]
    public void Canvas_RemoveState_RemovesStateAndTransitions()
    {
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Remove "s2" (Review) — should also remove transitions t1 and t2
        cut.Instance.RemoveState("s2");

        changed.Should().NotBeNull();
        changed!.States.Count.Should().Be(2);
        changed!.States.Should().NotContain(s => s.Id == "s2");
        changed!.Transitions.Count.Should().Be(0); // both t1 and t2 connected to s2
    }

    [Fact]
    public void Canvas_RemoveTransition_RemovesOnly()
    {
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.RemoveTransition("t1");

        changed.Should().NotBeNull();
        changed!.Transitions.Count.Should().Be(1);
        changed!.Transitions[0].Id.Should().Be("t2");
        changed!.States.Count.Should().Be(3); // states untouched
    }

    // ── Transition creation ──

    [Fact]
    public void Canvas_OnTransitionCreated_AddsTransition()
    {
        var def = new WorkflowCanvasDefinition
        {
            States =
            [
                new CanvasState { Id = "s1", Name = "A", X = 50, Y = 50 },
                new CanvasState { Id = "s2", Name = "B", X = 250, Y = 50 }
            ]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.OnTransitionCreated("s1", "s2");

        changed.Should().NotBeNull();
        changed!.Transitions.Count.Should().Be(1);
        changed!.Transitions[0].FromStateId.Should().Be("s1");
        changed!.Transitions[0].ToStateId.Should().Be("s2");
    }

    [Fact]
    public void Canvas_OnTransitionCreated_DuplicateRejected()
    {
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // t1 already connects s1 → s2
        cut.Instance.OnTransitionCreated("s1", "s2");

        changed.Should().BeNull();
    }

    [Fact]
    public void Canvas_OnTransitionCreated_SelfLoopRejected()
    {
        var def = new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "A", X = 50, Y = 50 }]
        };
        WorkflowCanvasDefinition? changed = null;

        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, def)
            .Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.OnTransitionCreated("s1", "s1");

        changed.Should().BeNull();
    }

    // ── Zoom ──

    [Fact]
    public void Canvas_ZoomLevel_Default()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Instance.ZoomLevel.Should().Be(1.0);
    }

    [Fact]
    public void Canvas_OnZoomChanged_FiresEvent()
    {
        double? reportedZoom = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.OnZoomLevelChanged, z => reportedZoom = z));

        // Simulate JS calling OnZoomChanged
        cut.Instance.OnZoomChanged(1.5);

        reportedZoom.Should().Be(1.5);
        cut.Instance.ZoomLevel.Should().Be(1.5);
    }

    [Fact]
    public void Canvas_SetZoom_UpdatesLevel()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Instance.SetZoom(2.0);

        cut.Instance.ZoomLevel.Should().Be(2.0);
    }

    // ── Context menu ──

    [Fact]
    public void Canvas_ContextMenu_HiddenByDefault()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(0);
    }

    [Fact]
    public void Canvas_ContextMenu_ShownOnRightClick()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        // Simulate JS calling OnContextMenu on empty canvas
        cut.Instance.OnContextMenu(100, 100, 150, 200, null);
        cut.Render();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(1);
    }

    [Fact]
    public void Canvas_ContextMenu_EmptyCanvas_ShowsAddOptions()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Instance.OnContextMenu(100, 100, 150, 200, null);
        cut.Render();

        var items = cut.FindAll(".tm-wf-context-menu__item");
        items.Count.Should().Be(3); // initial, intermediate, final
    }

    [Fact]
    public void Canvas_ContextMenu_OnState_ShowsEditDelete()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Instance.OnContextMenu(50, 100, 80, 120, "s1");
        cut.Render();

        var items = cut.FindAll(".tm-wf-context-menu__item");
        items.Count.Should().Be(2); // edit properties, delete
        cut.FindAll(".tm-wf-context-menu__item--danger").Count.Should().Be(1);
    }

    [Fact]
    public void Canvas_ContextMenu_AddState_CreatesAtPosition()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Open context menu at SVG position (200, 300)
        cut.Instance.OnContextMenu(200, 300, 150, 200, null);
        cut.Render();

        // Click "Add state" (second item = intermediate)
        cut.FindAll(".tm-wf-context-menu__item")[1].Click();

        changed.Should().NotBeNull();
        changed!.States.Count.Should().Be(4); // 3 original + 1 new
    }

    [Fact]
    public void Canvas_ContextMenu_DeleteState_RemovesState()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Open context menu on state s2
        cut.Instance.OnContextMenu(250, 100, 280, 120, "s2");
        cut.Render();

        // Click "Delete state" (the danger item)
        cut.Find(".tm-wf-context-menu__item--danger").Click();

        changed.Should().NotBeNull();
        changed!.States.Should().NotContain(s => s.Id == "s2");
    }

    [Fact]
    public void Canvas_ContextMenu_ReadOnly_NotShown()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ReadOnly, true));

        cut.Instance.OnContextMenu(100, 100, 150, 200, null);
        cut.Render();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(0);
    }

    [Fact]
    public void Canvas_ContextMenu_ClickCanvas_ClosesMenu()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Instance.OnContextMenu(100, 100, 150, 200, null);
        cut.Render();
        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(1);

        // Click on canvas to close
        cut.Find(".tm-wf-canvas").Click();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(0);
    }

    [Fact]
    public void Canvas_ContextMenu_OnTransition_ShowsEditDelete()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        // Right-click on transition t1
        cut.Instance.OnContextMenu(150, 100, 180, 120, null, "t1");
        cut.Render();

        var items = cut.FindAll(".tm-wf-context-menu__item");
        items.Count.Should().Be(2); // edit properties, delete transition
        cut.FindAll(".tm-wf-context-menu__item--danger").Count.Should().Be(1);
    }

    [Fact]
    public void Canvas_ContextMenu_DeleteTransition_RemovesTransition()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.DefinitionChanged, d => changed = d));

        // Right-click on transition t1
        cut.Instance.OnContextMenu(150, 100, 180, 120, null, "t1");
        cut.Render();

        // Click "Delete transition" (the danger item)
        cut.Find(".tm-wf-context-menu__item--danger").Click();

        changed.Should().NotBeNull();
        changed!.Transitions.Should().NotContain(t => t.Id == "t1");
        changed!.States.Count.Should().Be(3); // states untouched
    }

    [Fact]
    public void Canvas_ContextMenu_SelectTransition_FiresEvent()
    {
        CanvasTransition? selected = null;
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.OnTransitionSelected, t => selected = t));

        // Right-click on transition t1
        cut.Instance.OnContextMenu(150, 100, 180, 120, null, "t1");
        cut.Render();

        // Click "Edit properties"
        cut.FindAll(".tm-wf-context-menu__item")[0].Click();

        selected.Should().NotBeNull();
        selected!.Id.Should().Be("t1");
    }

    // ── Wrapper layout ──

    [Fact]
    public void Canvas_HasDesignerWrapper()
    {
        var cut = RenderComponent<TmWorkflowDesignerCanvas>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Find(".tm-wf-designer").Should().NotBeNull();
    }

    // ── Model extensions ──

    [Fact]
    public void CanvasState_HasIconAndSystemName()
    {
        var state = new CanvasState
        {
            Id = "s1", Name = "Test", Icon = "mail", SystemName = "delivered"
        };

        state.Icon.Should().Be("mail");
        state.SystemName.Should().Be("delivered");
    }
}
