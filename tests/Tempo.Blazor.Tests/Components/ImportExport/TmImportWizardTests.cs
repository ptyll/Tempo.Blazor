using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.ImportExport;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.ImportExport;

/// <summary>TDD tests for TmImportWizard.</summary>
public class TmImportWizardTests : LocalizationTestBase
{
    [Fact]
    public void TmImportWizard_Renders_Container()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>Content 1</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Content 2</p>")));

        cut.Find(".tm-import-wizard").Should().NotBeNull();
    }

    [Fact]
    public void TmImportWizard_Shows_Step_Indicator()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ShowStepIndicator, true)
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>Content 1</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Content 2</p>")));

        cut.Find(".tm-stepper").Should().NotBeNull();
    }

    [Fact]
    public void TmImportWizard_Hides_Step_Indicator_When_Disabled()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ShowStepIndicator, false)
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>Content 1</p>")));

        cut.FindAll(".tm-stepper").Count.Should().Be(0);
    }

    [Fact]
    public void TmImportWizard_Shows_Active_Step_Content()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First content</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second content</p>")));

        cut.Find(".tm-import-wizard-content").InnerHtml.Should().Contain("First content");
        cut.Find(".tm-import-wizard-content").InnerHtml.Should().NotContain("Second content");
    }

    [Fact]
    public void TmImportWizard_Next_Moves_To_Next_Step()
    {
        int activeStep = 0;
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 0)
            .Add(c => c.ActiveStepChanged, EventCallback.Factory.Create<int>(this, v => activeStep = v))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.Find("[data-testid='wizard-next'] button").Click();

        activeStep.Should().Be(1);
    }

    [Fact]
    public void TmImportWizard_Back_Moves_To_Previous_Step()
    {
        int activeStep = 1;
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 1)
            .Add(c => c.ActiveStepChanged, EventCallback.Factory.Create<int>(this, v => activeStep = v))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.Find("[data-testid='wizard-back'] button").Click();

        activeStep.Should().Be(0);
    }

    [Fact]
    public void TmImportWizard_Back_Hidden_On_First_Step()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 0)
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.FindAll("[data-testid='wizard-back']").Count.Should().Be(0);
    }

    [Fact]
    public void TmImportWizard_Shows_Complete_On_Last_Step()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 1)
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.FindAll("[data-testid='wizard-complete']").Count.Should().Be(1);
        cut.FindAll("[data-testid='wizard-next']").Count.Should().Be(0);
    }

    [Fact]
    public void TmImportWizard_Complete_Fires_OnComplete()
    {
        bool completed = false;
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 1)
            .Add(c => c.OnComplete, EventCallback.Factory.Create(this, () => completed = true))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.Find("[data-testid='wizard-complete'] button").Click();

        completed.Should().BeTrue();
    }

    [Fact]
    public void TmImportWizard_Cancel_Fires_OnCancel()
    {
        bool cancelled = false;
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>")));

        cut.Find("[data-testid='wizard-cancel'] button").Click();

        cancelled.Should().BeTrue();
    }

    [Fact]
    public void TmImportWizard_Custom_Button_Texts()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.ActiveStep, 0)
            .Add(c => c.NextText, "Forward")
            .Add(c => c.CancelText, "Abort")
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>"))
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 2")
                .AddChildContent("<p>Second</p>")));

        cut.Find("[data-testid='wizard-next']").TextContent.Should().Contain("Forward");
        cut.Find("[data-testid='wizard-cancel']").TextContent.Should().Contain("Abort");
    }

    [Fact]
    public void TmImportWizard_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmImportWizard>(p => p
            .Add(c => c.Class, "my-wizard")
            .AddChildContent<TmImportWizardStep>(sp => sp
                .Add(s => s.Title, "Step 1")
                .AddChildContent("<p>First</p>")));

        cut.Find(".tm-import-wizard").ClassList.Should().Contain("my-wizard");
    }
}
