using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

/// <summary>TDD tests for TmWorkflowPropertiesPanel.</summary>
public class TmWorkflowPropertiesPanelTests : LocalizationTestBase
{
    private static readonly List<CanvasState> SampleStates =
    [
        new CanvasState { Id = "s1", Name = "Draft", Type = CanvasStateType.Initial, X = 50, Y = 100 },
        new CanvasState { Id = "s2", Name = "Review", Type = CanvasStateType.Intermediate, X = 250, Y = 100 },
        new CanvasState { Id = "s3", Name = "Approved", Type = CanvasStateType.Final, X = 450, Y = 100 },
    ];

    // ── Visibility ──

    [Fact]
    public void Panel_HiddenWithoutSelection()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>();

        cut.FindAll(".tm-wf-properties").Count.Should().Be(0);
    }

    [Fact]
    public void Panel_VisibleWhenStateSelected()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        cut.Find(".tm-wf-properties").Should().NotBeNull();
    }

    [Fact]
    public void Panel_VisibleWhenTransitionSelected()
    {
        var transition = new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" };
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedTransition, transition)
            .Add(x => x.States, SampleStates));

        cut.Find(".tm-wf-properties").Should().NotBeNull();
    }

    // ── State editing ──

    [Fact]
    public void Panel_State_ShowsNameInput()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        var input = cut.Find("input[type='text']");
        input.Should().NotBeNull();
        input.GetAttribute("value").Should().Be("Draft");
    }

    [Fact]
    public void Panel_State_ShowsTypeRadios()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        var radios = cut.FindAll("input[type='radio']");
        radios.Count.Should().Be(3); // Initial, Intermediate, Final
    }

    [Fact]
    public void Panel_State_TypeRadio_CorrectChecked()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0])); // Initial type

        var radios = cut.FindAll("input[type='radio']");
        radios[0].HasAttribute("checked").Should().BeTrue(); // Initial is checked
    }

    [Fact]
    public void Panel_State_ShowsColorSwatches()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        cut.FindAll(".tm-wf-color-swatch").Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void Panel_State_HasSaveButton()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        var saveBtn = cut.Find(".tm-wf-properties__save");
        saveBtn.Should().NotBeNull();
    }

    [Fact]
    public void Panel_State_SaveFiresOnStateChanged()
    {
        CanvasState? changed = null;
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[1]) // "Review"
            .Add(x => x.OnStateChanged, s => changed = s));

        // Change the name
        var input = cut.Find("input[type='text']");
        input.Change("Updated Name");

        // Click save
        cut.Find(".tm-wf-properties__save").Click();

        changed.Should().NotBeNull();
        changed!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public void Panel_State_ChangeType_FiresOnStateChanged()
    {
        CanvasState? changed = null;
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[1]) // Intermediate
            .Add(x => x.OnStateChanged, s => changed = s));

        // Click "Final" radio
        var radios = cut.FindAll("input[type='radio']");
        radios[2].Change(true); // Final is third

        // Click save
        cut.Find(".tm-wf-properties__save").Click();

        changed.Should().NotBeNull();
        changed!.Type.Should().Be(CanvasStateType.Final);
    }

    // ── Transition editing ──

    [Fact]
    public void Panel_Transition_ShowsLabelInput()
    {
        var transition = new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" };
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedTransition, transition)
            .Add(x => x.States, SampleStates));

        var input = cut.Find("input[type='text']");
        input.Should().NotBeNull();
        input.GetAttribute("value").Should().Be("Submit");
    }

    [Fact]
    public void Panel_Transition_ShowsFromTo()
    {
        var transition = new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" };
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedTransition, transition)
            .Add(x => x.States, SampleStates));

        // Should display "Draft → Review"
        cut.Markup.Should().Contain("Draft");
        cut.Markup.Should().Contain("Review");
    }

    [Fact]
    public void Panel_Transition_SaveFiresOnTransitionChanged()
    {
        CanvasTransition? changed = null;
        var transition = new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" };
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedTransition, transition)
            .Add(x => x.States, SampleStates)
            .Add(x => x.OnTransitionChanged, t => changed = t));

        // Change label
        var input = cut.Find("input[type='text']");
        input.Change("Review Request");

        // Click save
        cut.Find(".tm-wf-properties__save").Click();

        changed.Should().NotBeNull();
        changed!.Label.Should().Be("Review Request");
    }

    // ── Header ──

    [Fact]
    public void Panel_State_HeaderShowsState()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0]));

        var header = cut.Find(".tm-wf-properties__header");
        header.TextContent.Should().Contain("State");
    }

    [Fact]
    public void Panel_Transition_HeaderShowsTransition()
    {
        var transition = new CanvasTransition { Id = "t1", FromStateId = "s1", ToStateId = "s2", Label = "Submit" };
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedTransition, transition)
            .Add(x => x.States, SampleStates));

        var header = cut.Find(".tm-wf-properties__header");
        header.TextContent.Should().Contain("Transition");
    }

    // ── ReadOnly ──

    [Fact]
    public void Panel_ReadOnly_InputsDisabled()
    {
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[0])
            .Add(x => x.ReadOnly, true));

        var input = cut.Find("input[type='text']");
        input.HasAttribute("disabled").Should().BeTrue();
    }

    // ── Color swatch click ──

    [Fact]
    public void Panel_State_ClickColorSwatch_ChangesColor()
    {
        CanvasState? changed = null;
        var cut = RenderComponent<TmWorkflowPropertiesPanel>(p => p
            .Add(x => x.SelectedState, SampleStates[1])
            .Add(x => x.OnStateChanged, s => changed = s));

        // Click a color swatch
        var swatches = cut.FindAll(".tm-wf-color-swatch");
        swatches[0].Click();

        // Click save
        cut.Find(".tm-wf-properties__save").Click();

        changed.Should().NotBeNull();
        changed!.Color.Should().NotBeNullOrEmpty();
    }
}
