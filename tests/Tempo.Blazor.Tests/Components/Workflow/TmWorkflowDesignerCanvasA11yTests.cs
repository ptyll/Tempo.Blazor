using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

/// <summary>
/// F23: the workflow canvas keyboard/a11y contract — focusable states (roving tabindex),
/// Enter/Space select exactly once, the Alt+arrow commit path announces, Delete deletes,
/// the context menu is fully localized with a keyboard-reachable Connect-to alternative,
/// the svg root is a group (never img, which hides focusable children from AT), and the
/// pinch-zoom has its single-pointer zoom buttons.
/// </summary>
public class TmWorkflowDesignerCanvasA11yTests : LocalizationTestBase
{
    private static WorkflowCanvasDefinition SimpleWorkflow => new()
    {
        States =
        [
            new CanvasState { Id = "s1", Name = "Draft", Type = CanvasStateType.Initial, X = 50, Y = 100 },
            new CanvasState { Id = "s2", Name = "Review", Type = CanvasStateType.Intermediate, X = 250, Y = 100 },
            new CanvasState { Id = "s3", Name = "Approved", Type = CanvasStateType.Final, X = 450, Y = 100 },
        ],
        Transitions = []
    };

    /// <summary>Deterministic English strings so assertions do not depend on the huge mock map.</summary>
    private void UseWorkflowLocalization()
    {
        UseCustomLocalization(new Dictionary<string, string>
        {
            ["TmWorkflowDesignerCanvas_Aria"] = "Workflow designer canvas",
            ["TmWorkflowDesignerCanvas_EditProperties"] = "EDIT-PROPS",
            ["TmWorkflowDesignerCanvas_DeleteState"] = "DELETE-STATE",
            ["TmWorkflowDesignerCanvas_DeleteTransition"] = "DELETE-TRANSITION",
            ["TmWorkflowDesignerCanvas_AddInitialState"] = "ADD-INITIAL",
            ["TmWorkflowDesignerCanvas_AddState"] = "ADD-STATE",
            ["TmWorkflowDesignerCanvas_AddFinalState"] = "ADD-FINAL",
            ["TmWorkflowDesignerCanvas_ConnectTo"] = "CONNECT-TO",
            ["TmWorkflowDesignerCanvas_StateLabel"] = "{0} ({1})",
            ["TmWorkflowDesignerCanvas_InitialState"] = "initial state",
            ["TmWorkflowDesignerCanvas_IntermediateState"] = "intermediate state",
            ["TmWorkflowDesignerCanvas_FinalState"] = "final state",
            ["TmWorkflowDesignerCanvas_StateMoved"] = "{0} moved to {1}, {2}",
            ["TmWorkflowDesignerCanvas_StateDeleted"] = "{0} deleted",
            ["TmWorkflowDesignerCanvas_ZoomIn"] = "ZOOM-IN",
            ["TmWorkflowDesignerCanvas_ZoomOut"] = "ZOOM-OUT",
        });
    }

    private IRenderedComponent<TmWorkflowDesignerCanvas> RenderCanvas(
        Action<ComponentParameterCollectionBuilder<TmWorkflowDesignerCanvas>>? configure = null,
        bool simpleWorkflow = true)
    {
        UseWorkflowLocalization();
        return Render<TmWorkflowDesignerCanvas>(p =>
        {
            if (simpleWorkflow)
            {
                p.Add(x => x.Definition, SimpleWorkflow);
            }
            configure?.Invoke(p);
        });
    }

    // ── svg role ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Svg_Role_Is_Group_Not_Img()
    {
        var cut = RenderCanvas();
        cut.Find("svg").GetAttribute("role").Should().Be("group",
            "role=img hides the focusable states from assistive technology");
    }

    // ── focusable states ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void State_Is_Focusable_Button_With_Name()
    {
        var cut = RenderCanvas();
        var states = cut.FindAll(".tm-wf-state");
        states.Count.Should().Be(3);

        // Roving tabindex: exactly one tab stop inside the group.
        states[0].GetAttribute("tabindex").Should().Be("0");
        states[1].GetAttribute("tabindex").Should().Be("-1");
        states[2].GetAttribute("tabindex").Should().Be("-1");

        states[0].GetAttribute("role").Should().Be("button");
        states[0].GetAttribute("aria-label").Should().Be("Draft (initial state)");
        states[1].GetAttribute("aria-label").Should().Be("Review (intermediate state)");
        states[2].GetAttribute("aria-label").Should().Be("Approved (final state)");
    }

    [Fact]
    public void State_Enter_Selects_Once_And_Space_On_KeyUp()
    {
        var selected = new List<CanvasState>();
        var cut = RenderCanvas(p => p.Add(x => x.OnStateSelected, s => selected.Add(s)));
        var state = cut.Find(".tm-wf-state");

        state.KeyDown("Enter");
        selected.Count.Should().Be(1, "Enter activates once on keydown");
        selected[0].Id.Should().Be("s1");

        state.KeyUp(" ");
        selected.Count.Should().Be(2, "Space activates on keyup, as a native button does");
        selected[1].Id.Should().Be("s1");

        state.KeyDown(" ");
        selected.Count.Should().Be(2, "Space keydown must not double-activate");
    }

    [Fact]
    public void Plain_Arrows_Move_The_Roving_Focus_Not_The_Node()
    {
        var cut = RenderCanvas();

        cut.FindAll(".tm-wf-state")[0].KeyDown("ArrowDown");
        cut.Render();

        var states = cut.FindAll(".tm-wf-state");
        states[0].GetAttribute("tabindex").Should().Be("-1");
        states[1].GetAttribute("tabindex").Should().Be("0", "the roving tabindex followed the arrow");
    }

    [Fact]
    public void Alt_Arrows_Are_Left_To_The_Js_Keyboard_Move()
    {
        var cut = RenderCanvas();
        // The helper (attachKeyboardMove) owns Alt+arrows; the Blazor handler must ignore them,
        // otherwise one chord would both move the node and move the focus.
        var args = new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs
        {
            Key = "ArrowRight",
            AltKey = true,
        };

        cut.FindAll(".tm-wf-state")[0].KeyDown(args);

        cut.FindAll(".tm-wf-state")[0].GetAttribute("tabindex").Should().Be("0",
            "the Alt chord never moves the roving focus");
    }

    // ── keyboard move/delete commit + announce ─────────────────────────────────────────────────

    [Fact]
    public void OnNodeKeyboardMoved_Snaps_And_Announces()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderCanvas(p => p.Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.OnNodeKeyboardMoved("s1", 55, 197);
        cut.Render();

        changed.Should().NotBeNull();
        var s1 = changed!.States.First(s => s.Id == "s1");
        s1.X.Should().Be(60, "the keyboard path snaps to the grid exactly like the drag");
        s1.Y.Should().Be(200);

        cut.Find(".tm-wf-canvas [role='status']").TextContent.Should().Contain("Draft moved to 60, 200");
    }

    [Fact]
    public void OnNodeKeyboardDeleted_Removes_State_And_Announces()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderCanvas(p => p.Add(x => x.DefinitionChanged, d => changed = d));

        cut.Instance.OnNodeKeyboardDeleted("s2");
        cut.Render();

        changed!.States.Should().NotContain(s => s.Id == "s2");
        changed!.Transitions.Should().BeEmpty("the transitions of the deleted state go with it");
        cut.Find(".tm-wf-canvas [role='status']").TextContent.Should().Contain("Review deleted");
    }

    [Fact]
    public void OnNodeKeyboardMoved_UnknownState_IsIgnored()
    {
        var cut = RenderCanvas();
        var act = () => cut.Instance.OnNodeKeyboardMoved("nope", 10, 10);
        act.Should().NotThrow();
    }

    // ── localized context menu ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ContextMenu_Is_Localized()
    {
        var cut = RenderCanvas();
        cut.Instance.OnContextMenu(250, 100, 280, 120, "s2");
        cut.Render();

        var menu = cut.Find(".tm-wf-context-menu").TextContent;
        menu.Should().Contain("EDIT-PROPS").And.Contain("DELETE-STATE");
        menu.Should().NotContain("Edit properties").And.NotContain("Delete state");
    }

    [Fact]
    public void ContextMenu_EmptyCanvas_Is_Localized()
    {
        var cut = RenderCanvas();
        cut.Instance.OnContextMenu(100, 100, 150, 200, null);
        cut.Render();

        var menu = cut.Find(".tm-wf-context-menu").TextContent;
        menu.Should().Contain("ADD-INITIAL").And.Contain("ADD-STATE").And.Contain("ADD-FINAL");
        menu.Should().NotContain("Add initial state");
    }

    [Fact]
    public void ContextMenu_Transition_Is_Localized()
    {
        var cut = RenderCanvas(p => p.Add(x => x.Definition, new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Draft", Type = CanvasStateType.Initial, X = 50, Y = 100 }],
            Transitions = [new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s1" }],
        }), simpleWorkflow: false);
        cut.Instance.OnContextMenu(150, 100, 180, 120, null, "t1");
        cut.Render();

        var menu = cut.Find(".tm-wf-context-menu").TextContent;
        menu.Should().Contain("EDIT-PROPS").And.Contain("DELETE-TRANSITION");
        menu.Should().NotContain("Delete transition");
    }

    [Fact]
    public void ContextMenu_ConnectTo_Creates_Transition()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderCanvas(p =>
        {
            p.Add(x => x.DefinitionChanged, d => changed = d);
        });
        cut.Instance.OnContextMenu(50, 100, 80, 120, "s1");
        cut.Render();

        // The non-drag alternative to dragging from a port (WCAG 2.5.7).
        var connect = cut.FindAll(".tm-wf-context-menu__item").Single(b => b.TextContent.Contains("CONNECT-TO"));
        connect.Click();
        cut.Render();

        var targets = cut.FindAll(".tm-wf-context-menu__item");
        targets.Select(b => b.TextContent.Trim()).Should().Equal(
            ["Review", "Approved"], "the menu lists the OTHER states as connection targets");

        targets.First(b => b.TextContent.Contains("Approved")).Click();

        changed.Should().NotBeNull();
        changed!.Transitions.Should().Contain(t => t.FromStateId == "s1" && t.ToStateId == "s3");
    }

    [Fact]
    public void ContextMenu_ConnectTo_SingleState_OffersNoTarget_AndCloses()
    {
        var cut = RenderCanvas(p => p.Add(x => x.Definition, new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "s1", Name = "Solo", Type = CanvasStateType.Initial, X = 50, Y = 100 }],
        }), simpleWorkflow: false);
        cut.Instance.OnContextMenu(50, 100, 80, 120, "s1");
        cut.Render();

        var menu = cut.Find(".tm-wf-context-menu").TextContent;
        menu.Should().NotContain("CONNECT-TO", "with no other state there is nothing to connect to");
        menu.Should().Contain("EDIT-PROPS").And.Contain("DELETE-STATE");
    }

    [Fact]
    public void State_ShiftF10_Opens_ContextMenu()
    {
        var cut = RenderCanvas();
        var state = cut.FindAll(".tm-wf-state")[1];

        state.KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F10", ShiftKey = true });
        cut.Render();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(1, "the menu is reachable from the keyboard");
        cut.Find(".tm-wf-context-menu").TextContent.Should().Contain("DELETE-STATE");
    }

    // ── context menu keyboard ───────────────────────────────────────────────────────────────

    [Fact]
    public void State_ShiftF10_Focuses_The_First_Menu_Item()
    {
        var cut = RenderCanvas();

        cut.FindAll(".tm-wf-state")[1].KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F10", ShiftKey = true });
        cut.Render();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(1);
        JSInterop.VerifyInvoke("tmWorkflowDesigner.focusMenu", 1);
    }

    [Fact]
    public void Menu_Escape_Closes_And_Returns_The_Focus_To_The_State()
    {
        var cut = RenderCanvas();

        // The realistic sequence: the roving focus sits on s2, Shift+F10 opens ITS menu.
        cut.FindAll(".tm-wf-state")[0].KeyDown("ArrowDown");
        cut.Render();
        cut.FindAll(".tm-wf-state").Single(s => s.GetAttribute("tabindex") == "0")
            .GetAttribute("data-state-id").Should().Be("s2");
        cut.FindAll(".tm-wf-state").Single(s => s.GetAttribute("data-state-id") == "s2")
            .KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F10", ShiftKey = true });
        cut.Render();
        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(1, "precondition: the menu is open");

        cut.FindAll(".tm-wf-context-menu__item")[0].KeyDown("Escape");
        cut.Render();

        cut.FindAll(".tm-wf-context-menu").Count.Should().Be(0, "Escape closes the menu");
        var roving = cut.FindAll(".tm-wf-state").Single(s => s.GetAttribute("tabindex") == "0");
        roving.GetAttribute("data-state-id").Should().Be("s2",
            "Escape returns the focus to the state the menu came from");
        // One focusState for the roving arrow move, one for the Escape's return to s2.
        JSInterop.VerifyInvoke("tmWorkflowDesigner.focusState", 2)
            .Last().Arguments[1].Should().Be("s2");
    }

    [Fact]
    public void Menu_ConnectTo_Switch_Focuses_The_Menu_Again()
    {
        var cut = RenderCanvas();
        cut.Instance.OnContextMenu(50, 100, 80, 120, "s1");
        cut.Render();
        JSInterop.VerifyInvoke("tmWorkflowDesigner.focusMenu", 1);

        var connect = cut.FindAll(".tm-wf-context-menu__item").Single(b => b.TextContent.Contains("CONNECT-TO"));
        connect.Click();
        cut.Render();

        // The mode switch replaced the items: the focus lands on the first target again.
        JSInterop.VerifyInvoke("tmWorkflowDesigner.focusMenu", 2);
    }

    [Fact]
    public void State_ShiftF10_Menu_Position_Comes_From_The_Measured_Rect()
    {
        // Correct under zoom and pan: the JS-measured state rect minus the container rect.
        JSInterop.Setup<double[]>("tmWorkflowDesigner.stateMenuPoint", new Bunit.InvocationMatcher(_ => true))
            .SetResult([33, 160]);
        var cut = RenderCanvas();

        cut.FindAll(".tm-wf-state")[1].KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "F10", ShiftKey = true });

        cut.WaitForAssertion(() =>
        {
            var style = cut.Find(".tm-wf-context-menu").GetAttribute("style");
            style.Should().Contain("left: 33px").And.Contain("top: 160px");
        });
    }

    // ── the roving focus survives a delete ──────────────────────────────────────────────────

    [Fact]
    public void Keyboard_Deleting_A_State_Hands_The_Roving_Focus_To_The_Neighbour()
    {
        var cut = RenderCanvas();
        cut.FindAll(".tm-wf-state")[0].KeyDown("ArrowDown"); // the roving tab stop moves to s2
        cut.Render();

        cut.Instance.OnNodeKeyboardDeleted("s2");
        cut.Render();

        var states = cut.FindAll(".tm-wf-state");
        states.Count.Should().Be(2);
        states.Count(s => s.GetAttribute("tabindex") == "0").Should().Be(1, "exactly one tab stop remains");
        states.First(s => s.GetAttribute("tabindex") == "0").GetAttribute("data-state-id")
            .Should().Be("s3", "the next state took the roving focus");
    }

    [Fact]
    public void Keyboard_Deleting_The_Last_State_Focuses_The_Zoom_Button()
    {
        var cut = RenderCanvas(p => p.Add(x => x.Definition, new WorkflowCanvasDefinition
        {
            States = [new CanvasState { Id = "only", Name = "Solo", Type = CanvasStateType.Initial, X = 50, Y = 100 }],
        }), simpleWorkflow: false);

        cut.Instance.OnNodeKeyboardDeleted("only");
        cut.Render();

        cut.FindAll(".tm-wf-state").Should().BeEmpty();
        JSInterop.VerifyInvoke("tmWorkflowDesigner.focusZoom", 1);
    }

    [Fact]
    public void Removing_The_Roving_State_Falls_Back_To_The_First_Existing_State()
    {
        var cut = RenderCanvas();
        cut.FindAll(".tm-wf-state")[0].KeyDown("ArrowDown"); // the roving tab stop moves to s2
        cut.Render();
        cut.FindAll(".tm-wf-state").Single(s => s.GetAttribute("tabindex") == "0")
            .GetAttribute("data-state-id").Should().Be("s2", "precondition");

        // The toolbox path (RemoveState): no neighbour pick — the roving id dangles instead.
        cut.Instance.RemoveState("s2");
        cut.Render();

        var roving = cut.FindAll(".tm-wf-state").Single(s => s.GetAttribute("tabindex") == "0");
        roving.GetAttribute("data-state-id")
            .Should().Be("s1", "a dangling roving id falls back to the first existing state");
    }

    // ── read-only vetoes every editing commit ───────────────────────────────────────────────

    [Fact]
    public void ReadOnly_Vetoes_Drag_KeyboardMove_KeyboardDelete_And_Transition()
    {
        WorkflowCanvasDefinition? changed = null;
        var cut = RenderCanvas(p =>
        {
            p.Add(x => x.ReadOnly, true);
            p.Add(x => x.DefinitionChanged, d => changed = d);
        });

        cut.Instance.OnNodeDragged("s1", 200, 300);
        cut.Instance.OnNodeKeyboardMoved("s1", 200, 300);
        cut.Instance.OnNodeKeyboardDeleted("s1");
        cut.Instance.OnTransitionCreated("s1", "s2");

        changed.Should().BeNull("read-only accepts no editing commit");
    }

    [Fact]
    public async Task ReadOnly_Change_Tells_The_Js_When_Initialized()
    {
        var cut = RenderCanvas();
        JSInterop.Invocations.Where(i => i.Identifier == "tmWorkflowDesigner.setReadOnly")
            .Should().BeEmpty("the initial ReadOnly already went into init");

        // OnParametersSetAsync is protected — flip the public parameter property and invoke it via
        // reflection (the same convention WireframeRulerTests uses for OnParametersSet).
        cut.Instance.ReadOnly = true;
        var method = typeof(TmWorkflowDesignerCanvas).GetMethod("OnParametersSetAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        await (Task)method.Invoke(cut.Instance, null)!;

        var invoke = JSInterop.VerifyInvoke("tmWorkflowDesigner.setReadOnly", 1).Single();
        invoke.Arguments[1].Should().Be(true, "the JS side learns the canvas went read-only");

        // A parameters pass with the SAME value must not push it again.
        await (Task)method.Invoke(cut.Instance, null)!;
        JSInterop.VerifyInvoke("tmWorkflowDesigner.setReadOnly", 1);
    }

    // ── zoom buttons (the single-pointer alternative to the pinch) ──────────────────────────────

    [Fact]
    public void Zoom_Buttons_Are_Localized_And_Change_ZoomLevel()
    {
        var cut = RenderCanvas();
        var zoomIn = cut.Find(".tm-wf-zoom-overlay button:nth-child(2)");
        zoomIn.GetAttribute("aria-label").Should().Be("ZOOM-IN");

        zoomIn.Click();

        cut.Instance.ZoomLevel.Should().BeGreaterThan(1.0);
    }
}
