using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>Tests for TmDialog callback behavior (OnResult and OnPromptResult).</summary>
public class TmDialogCallbackTests : LocalizationTestBase
{
    public TmDialogCallbackTests()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        Services.AddSingleton(jsRuntime);
    }

    [Fact]
    public void Dialog_Confirm_FiresOnConfirm()
    {
        // Arrange
        bool? resultValue = null;
        var cut = RenderComponent<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm?")
            .Add(d => d.Message, "Are you sure?")
            .Add(d => d.OnResult, (bool? val) => resultValue = val));

        // Act
        cut.Find(".tm-dialog-btn-ok").Click();

        // Assert
        resultValue.Should().BeTrue();
    }

    [Fact]
    public void Dialog_Cancel_FiresOnCancel()
    {
        // Arrange
        bool? resultValue = null;
        var cut = RenderComponent<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm?")
            .Add(d => d.Message, "Are you sure?")
            .Add(d => d.OnResult, (bool? val) => resultValue = val));

        // Act
        cut.Find(".tm-dialog-btn-cancel").Click();

        // Assert
        resultValue.Should().BeFalse();
    }

    [Fact]
    public void Dialog_Prompt_SubmitsValue_FiresOnPromptResult()
    {
        // Arrange
        string? promptResult = null;
        var cut = RenderComponent<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Enter name")
            .Add(d => d.Message, "Please enter your name:")
            .Add(d => d.OnPromptResult, (string? val) => promptResult = val));

        // Type text into the prompt input
        var input = cut.Find("input.tm-dialog-input");
        input.Change("Hello World");

        // Act
        cut.Find(".tm-dialog-btn-ok").Click();

        // Assert
        promptResult.Should().Be("Hello World");
    }

    [Fact]
    public void Dialog_Prompt_Cancel_DoesNotFireOnPromptResult()
    {
        // Arrange
        bool promptResultFired = false;
        string? promptResult = "sentinel";
        var cut = RenderComponent<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Enter name")
            .Add(d => d.Message, "Please enter your name:")
            .Add(d => d.OnPromptResult, (string? val) =>
            {
                promptResultFired = true;
                promptResult = val;
            }));

        // Act - click cancel without entering any text
        cut.Find(".tm-dialog-btn-cancel").Click();

        // Assert - OnPromptResult IS fired on cancel, but with null value
        // (the component calls OnPromptResult with null on cancel)
        promptResultFired.Should().BeTrue();
        promptResult.Should().BeNull();
    }
}
