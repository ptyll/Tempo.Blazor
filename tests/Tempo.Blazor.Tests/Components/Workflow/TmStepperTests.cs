using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Workflow;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Workflow;

file record TestStep(
    string Id,
    string Label,
    string? Description,
    string? Icon = null) : IStepItem;

/// <summary>TDD tests for TmStepper.</summary>
public class TmStepperTests : LocalizationTestBase
{
    private static IStepItem[] MakeSteps() =>
    [
        new TestStep("s1", "Details",  "Enter details"),
        new TestStep("s2", "Review",   "Review your input"),
        new TestStep("s3", "Confirm",  "Confirm and submit"),
    ];

    [Fact]
    public void TmStepper_Renders_Stepper()
    {
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 0));

        cut.Find(".tm-stepper").Should().NotBeNull();
    }

    [Fact]
    public void TmStepper_Renders_Step_Per_Item()
    {
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 0));

        cut.FindAll(".tm-stepper-step").Count.Should().Be(3);
    }

    [Fact]
    public void TmStepper_Active_Step_Has_Active_Class()
    {
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 1));

        // Second step (index 1) should have active class
        cut.FindAll(".tm-stepper-step")[1].ClassList
            .Should().Contain("tm-stepper-step-active");
    }

    [Fact]
    public void TmStepper_Completed_Steps_Have_Completed_Class()
    {
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 2));

        // Steps before active should be completed
        cut.FindAll(".tm-stepper-step")[0].ClassList
            .Should().Contain("tm-stepper-step-completed");
        cut.FindAll(".tm-stepper-step")[1].ClassList
            .Should().Contain("tm-stepper-step-completed");
    }

    [Fact]
    public void TmStepper_Shows_Step_Labels()
    {
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 0));

        cut.FindAll(".tm-stepper-step-label")[0].TextContent
            .Should().Contain("Details");
    }

    [Fact]
    public void TmStepper_Click_Step_Fires_OnStepClick()
    {
        var clickedIndex = -1;
        var cut = RenderComponent<TmStepper>(p => p
            .Add(c => c.Steps, MakeSteps())
            .Add(c => c.ActiveStep, 0)
            .Add(c => c.OnStepClick,
                EventCallback.Factory.Create<int>(this, i => clickedIndex = i)));

        cut.FindAll(".tm-stepper-step")[2].Click();

        clickedIndex.Should().Be(2);
    }
}
