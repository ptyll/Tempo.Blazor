using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using NSubstitute;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

public class TmDialogTests : LocalizationTestBase
{
    public TmDialogTests()
    {
        var jsRuntime = Substitute.For<IJSRuntime>();
        Services.AddSingleton(jsRuntime);
    }

    #region Rendering Tests

    [Fact]
    public void Dialog_WhenShowTrue_RendersDialog()
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Test")
            .Add(d => d.Message, "Message"));

        // Assert
        cut.Find(".tm-modal-overlay").Should().NotBeNull();
        cut.Find(".tm-dialog").Should().NotBeNull();
    }

    [Fact]
    public void Dialog_WhenShowFalse_IsHidden()
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, false)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Test")
            .Add(d => d.Message, "Message"));

        // Assert
        cut.FindAll(".tm-modal-overlay").Count.Should().Be(0);
    }

    [Fact]
    public void Dialog_DisplaysTitleAndMessage()
    {
        // Arrange
        const string title = "Alert Title";
        const string message = "This is an alert message";

        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, title)
            .Add(d => d.Message, message));

        // Assert
        cut.Find(".tm-dialog-title").TextContent.Should().Be(title);
        cut.Find(".tm-dialog-message").TextContent.Should().Be(message);
    }

    [Fact]
    public void Dialog_ShowsIcon()
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Alert")
            .Add(d => d.Message, "Message"));

        // Assert
        cut.Find(".tm-dialog-icon").Should().NotBeNull();
    }

    #endregion

    #region Type Tests

    [Theory]
    [InlineData(DialogType.Alert, "tm-dialog--alert")]
    [InlineData(DialogType.Confirm, "tm-dialog--confirm")]
    [InlineData(DialogType.Prompt, "tm-dialog--prompt")]
    public void Dialog_AppliesTypeClass(DialogType type, string expectedClass)
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, type)
            .Add(d => d.Title, "Title")
            .Add(d => d.Message, "Message"));

        // Assert
        cut.Find(".tm-dialog").ClassList.Should().Contain(expectedClass);
    }

    #endregion

    #region Variant Tests

    [Theory]
    [InlineData(DialogVariant.Info, "tm-dialog--info")]
    [InlineData(DialogVariant.Success, "tm-dialog--success")]
    [InlineData(DialogVariant.Warning, "tm-dialog--warning")]
    [InlineData(DialogVariant.Error, "tm-dialog--error")]
    public void Dialog_AppliesVariantClass(DialogVariant variant, string expectedClass)
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Variant, variant)
            .Add(d => d.Title, "Title")
            .Add(d => d.Message, "Message"));

        // Assert
        cut.Find(".tm-dialog").ClassList.Should().Contain(expectedClass);
    }

    #endregion

    #region Dangerous Actions

    [Fact]
    public void Dialog_Dangerous_ShowsDangerStyling()
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.IsDangerous, true)
            .Add(d => d.Title, "Delete?")
            .Add(d => d.Message, "This cannot be undone"));

        // Assert
        cut.Find(".tm-dialog").ClassList.Should().Contain("tm-dialog--dangerous");
    }

    #endregion

    #region Prompt Tests

    [Fact]
    public void PromptDialog_ShowsInputField()
    {
        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Prompt")
            .Add(d => d.Message, "Enter value:"));

        // Assert
        cut.Find("input.tm-dialog-input").Should().NotBeNull();
    }

    [Fact]
    public void PromptDialog_ShowsDefaultValue()
    {
        // Arrange
        const string defaultValue = "Default Text";

        // Act
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Prompt)
            .Add(d => d.Title, "Prompt")
            .Add(d => d.Message, "Enter value:")
            .Add(d => d.DefaultValue, defaultValue));

        // Assert
        cut.Find("input.tm-dialog-input").GetAttribute("value").Should().Be(defaultValue);
    }

    #endregion

    #region Footer Button Variant Tests (Fáze 4)

    [Fact]
    public void ConfirmDialog_CancelButton_DefaultsToGhost()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm"));

        cut.Find(".tm-dialog-btn-cancel").ClassList.Should().Contain("tm-btn-ghost",
            "null keeps the historical Ghost cancel button");
    }

    [Fact]
    public void ConfirmDialog_CancelButtonVariant_OverridesTheDefault()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm")
            .Add(d => d.CancelButtonVariant, ButtonVariant.Secondary));

        cut.Find(".tm-dialog-btn-cancel").ClassList.Should().Contain("tm-btn-secondary");
    }

    [Fact]
    public void ConfirmDialog_OkButtonVariant_OverridesTheVariantImpliedDefault()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.Title, "Confirm")
            .Add(d => d.OkButtonVariant, ButtonVariant.Secondary));

        cut.Find(".tm-dialog-btn-ok").ClassList.Should().Contain("tm-btn-secondary",
            "an explicit OkButtonVariant wins over the dialog-variant-implied Primary");
    }

    [Fact]
    public void DangerousConfirmDialog_OkButton_KeepsTheDangerDefault_WhenNoVariantIsPassed()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Confirm)
            .Add(d => d.IsDangerous, true)
            .Add(d => d.Title, "Delete?"));

        cut.Find(".tm-dialog-btn-ok").ClassList.Should().Contain("tm-btn-danger");
    }

    [Fact]
    public void AlertDialog_OkButtonVariant_OverridesTheDefault()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Type, DialogType.Alert)
            .Add(d => d.Title, "Alert")
            .Add(d => d.OkButtonVariant, ButtonVariant.Soft));

        cut.Find(".tm-dialog-btn-ok").ClassList.Should().Contain("tm-btn-soft");
    }

    #endregion
}
