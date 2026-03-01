using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

/// <summary>TDD tests for TmWorkflowMinimap.</summary>
public class TmWorkflowMinimapTests : LocalizationTestBase
{
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
            new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2" },
        ]
    };

    [Fact]
    public void Minimap_RendersContainer()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Find(".tm-wf-minimap").Should().NotBeNull();
    }

    [Fact]
    public void Minimap_RendersSvg()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.Find("svg").Should().NotBeNull();
    }

    [Fact]
    public void Minimap_RendersStateRects()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-minimap__state").Count.Should().Be(3);
    }

    [Fact]
    public void Minimap_RendersViewportRect()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.ViewBoxX, 0)
            .Add(x => x.ViewBoxY, 0)
            .Add(x => x.ViewBoxW, 800)
            .Add(x => x.ViewBoxH, 500));

        cut.FindAll(".tm-wf-minimap__viewport").Count.Should().Be(1);
    }

    [Fact]
    public void Minimap_EmptyDefinition_RendersSvg()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, new WorkflowCanvasDefinition()));

        cut.Find("svg").Should().NotBeNull();
        cut.FindAll(".tm-wf-minimap__state").Count.Should().Be(0);
    }

    [Fact]
    public void Minimap_RendersTransitionLines()
    {
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow));

        cut.FindAll(".tm-wf-minimap__transition").Count.Should().Be(1);
    }

    [Fact]
    public void Minimap_ClickFires_OnNavigate()
    {
        bool navigated = false;
        var cut = RenderComponent<TmWorkflowMinimap>(p => p
            .Add(x => x.Definition, SimpleWorkflow)
            .Add(x => x.OnNavigate, () => navigated = true));

        cut.Find("svg").Click();

        navigated.Should().BeTrue();
    }
}
