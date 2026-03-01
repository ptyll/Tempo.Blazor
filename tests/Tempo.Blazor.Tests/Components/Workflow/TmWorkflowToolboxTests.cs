using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

/// <summary>TDD tests for TmWorkflowToolbox.</summary>
public class TmWorkflowToolboxTests : LocalizationTestBase
{
    [Fact]
    public void Toolbox_RendersContainer()
    {
        var cut = RenderComponent<TmWorkflowToolbox>();
        cut.Find(".tm-wf-toolbox").Should().NotBeNull();
    }

    [Fact]
    public void Toolbox_RendersStateButtons()
    {
        var cut = RenderComponent<TmWorkflowToolbox>();
        var buttons = cut.FindAll(".tm-wf-toolbox__btn");
        // At least 3 state type buttons (initial, intermediate, final)
        buttons.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Toolbox_ClickInitial_FiresOnAddState()
    {
        CanvasStateType? addedType = null;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.OnAddState, t => addedType = t));

        cut.FindAll(".tm-wf-toolbox__btn")[0].Click();

        addedType.Should().Be(CanvasStateType.Initial);
    }

    [Fact]
    public void Toolbox_ClickIntermediate_FiresOnAddState()
    {
        CanvasStateType? addedType = null;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.OnAddState, t => addedType = t));

        cut.FindAll(".tm-wf-toolbox__btn")[1].Click();

        addedType.Should().Be(CanvasStateType.Intermediate);
    }

    [Fact]
    public void Toolbox_ClickFinal_FiresOnAddState()
    {
        CanvasStateType? addedType = null;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.OnAddState, t => addedType = t));

        cut.FindAll(".tm-wf-toolbox__btn")[2].Click();

        addedType.Should().Be(CanvasStateType.Final);
    }

    [Fact]
    public void Toolbox_DeleteButton_Exists()
    {
        var cut = RenderComponent<TmWorkflowToolbox>();
        cut.FindAll(".tm-wf-toolbox__btn--danger").Count.Should().Be(1);
    }

    [Fact]
    public void Toolbox_DeleteButton_DisabledWithoutSelection()
    {
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.HasSelection, false));

        var deleteBtn = cut.Find(".tm-wf-toolbox__btn--danger");
        deleteBtn.HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void Toolbox_DeleteButton_EnabledWithSelection()
    {
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.HasSelection, true));

        var deleteBtn = cut.Find(".tm-wf-toolbox__btn--danger");
        deleteBtn.HasAttribute("disabled").Should().BeFalse();
    }

    [Fact]
    public void Toolbox_DeleteButton_FiresOnDeleteSelected()
    {
        bool deleted = false;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.HasSelection, true)
            .Add(x => x.OnDeleteSelected, () => deleted = true));

        cut.Find(".tm-wf-toolbox__btn--danger").Click();

        deleted.Should().BeTrue();
    }

    [Fact]
    public void Toolbox_HasZoomButtons()
    {
        var cut = RenderComponent<TmWorkflowToolbox>();
        cut.FindAll(".tm-wf-zoom-btn").Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public void Toolbox_ZoomIn_FiresOnZoom()
    {
        double? zoom = null;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.ZoomLevel, 1.0)
            .Add(x => x.OnZoomChanged, z => zoom = z));

        // Click zoom in (first zoom button)
        cut.FindAll(".tm-wf-zoom-btn")[0].Click();

        zoom.Should().NotBeNull();
        zoom.Should().BeGreaterThan(1.0);
    }

    [Fact]
    public void Toolbox_ZoomLevel_DisplaysPercentage()
    {
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.ZoomLevel, 1.5));

        cut.Find(".tm-wf-zoom-level").TextContent.Should().Contain("150");
    }

    [Fact]
    public void Toolbox_FitToView_FiresOnFitToView()
    {
        bool fitCalled = false;
        var cut = RenderComponent<TmWorkflowToolbox>(p => p
            .Add(x => x.OnFitToView, () => fitCalled = true));

        // Fit to view is the last zoom button
        var zoomBtns = cut.FindAll(".tm-wf-zoom-btn");
        zoomBtns[zoomBtns.Count - 1].Click();

        fitCalled.Should().BeTrue();
    }
}
