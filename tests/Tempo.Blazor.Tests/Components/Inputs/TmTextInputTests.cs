using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmTextInput.</summary>
public class TmTextInputTests : LocalizationTestBase
{
    [Fact]
    public void TmTextInput_Renders_Input_Element()
    {
        var cut = Render<TmTextInput>();
        cut.Find("input").Should().NotBeNull();
    }

    [Fact]
    public void TmTextInput_Has_Base_CssClass()
    {
        var cut = Render<TmTextInput>();
        cut.Find("input").ClassList.Should().Contain("tm-input");
    }

    [Fact]
    public void TmTextInput_Default_Type_Is_Text()
    {
        var cut = Render<TmTextInput>();
        cut.Find("input").GetAttribute("type").Should().Be("text");
    }

    [Fact]
    public void TmTextInput_Custom_Type_Applied()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Type, "email"));
        cut.Find("input").GetAttribute("type").Should().Be("email");
    }

    [Fact]
    public void TmTextInput_Label_Renders_Label_Element()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Label, "Email"));
        cut.Find("label").TextContent.Trim().Should().Be("Email");
    }

    [Fact]
    public void TmTextInput_No_Label_When_Null()
    {
        var cut = Render<TmTextInput>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_Label_For_Matches_Input_Id()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "my-input")
            .Add(c => c.Label, "Name"));

        cut.Find("label").GetAttribute("for").Should().Be("my-input");
        cut.Find("input").GetAttribute("id").Should().Be("my-input");
    }

    [Fact]
    public void TmTextInput_Placeholder_Applied()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Placeholder, "Enter value"));
        cut.Find("input").GetAttribute("placeholder").Should().Be("Enter value");
    }

    [Fact]
    public void TmTextInput_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Disabled, true));
        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmTextInput_ReadOnly_Sets_Readonly_Attribute()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.ReadOnly, true));
        cut.Find("input").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void TmTextInput_Error_Adds_Error_CssClass()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Error, "Required"));
        cut.Find("input").ClassList.Should().Contain("tm-input-error");
    }

    [Fact]
    public void TmTextInput_Error_Shows_Error_Message()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Error, "Required field"));
        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Required field");
    }

    [Fact]
    public void TmTextInput_HelpText_Shows_When_No_Error()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.HelpText, "Max 100 chars"));
        cut.Find("[data-testid='help-text']").TextContent.Should().Contain("Max 100 chars");
    }

    [Fact]
    public void TmTextInput_HelpText_Hidden_When_Error_Present()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Error, "Required")
            .Add(c => c.HelpText, "Max 100 chars"));

        cut.FindAll("[data-testid='help-text']").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_LeftIcon_Renders_TmIcon()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.LeftIcon, "search"));
        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    /// <summary>
    /// Fáze 18.1 fixes the measured `.tm-input|.tm-input-with-left-icon` cascade tie with the
    /// compound <c>.tm-input.tm-input-with-left-icon</c> — a selector that only reaches an input
    /// carrying BOTH classes. This pins the markup contract it relies on.
    /// </summary>
    [Fact]
    public void TmTextInput_LeftIcon_CarriesBaseAndModifier_OnTheSameInput()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.LeftIcon, "search"));

        cut.Find("input").ClassList.Should()
            .Contain("tm-input")
            .And.Contain("tm-input-with-left-icon");
    }

    [Fact]
    public void TmTextInput_NoLeftIcon_OmitsTheModifier()
    {
        var cut = Render<TmTextInput>();

        var classes = cut.Find("input").ClassList;
        classes.Should().Contain("tm-input").And.NotContain("tm-input-with-left-icon");
    }

    [Fact]
    public void TmTextInput_No_Icon_By_Default()
    {
        var cut = Render<TmTextInput>();
        cut.FindAll(".tm-icon").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("input").Change("hello");

        captured.Should().Be("hello");
    }

    // ── K8 accessibility ─────────────────────────────────────────

    [Fact]
    public void TmTextInput_Required_SetsAriaRequired()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Required, true));
        cut.Find("input").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmTextInput_NoValidationState_HasNoAriaInvalid()
    {
        var cut = Render<TmTextInput>();
        cut.Find("input").HasAttribute("aria-invalid").Should().BeFalse();
    }

    [Fact]
    public void TmTextInput_Error_SetsAriaInvalidAndDescribedByErrorId()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "email")
            .Add(c => c.Error, "Required"));

        var input = cut.Find("input");
        input.GetAttribute("aria-invalid").Should().Be("true");
        input.GetAttribute("aria-describedby").Should().Be("email-error");

        var err = cut.Find("[data-testid='error-message']");
        err.GetAttribute("id").Should().Be("email-error");
        err.GetAttribute("role").Should().Be("alert");
    }

    [Fact]
    public void TmTextInput_HelpTextOnly_DescribedByHelpId()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "phone")
            .Add(c => c.HelpText, "Digits only"));

        cut.Find("input").GetAttribute("aria-describedby").Should().Be("phone-help");
        cut.Find("[data-testid='help-text']").GetAttribute("id").Should().Be("phone-help");
    }

    [Fact]
    public void TmTextInput_IsValidFalse_SetsAriaInvalid()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.IsValid, false));
        cut.Find("input").GetAttribute("aria-invalid").Should().Be("true");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void TmTextInput_Required_SetsAriaRequiredOnInput()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Required, true));
        cut.Find("input").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmTextInput_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Label, "Name")
            .Add(c => c.Required, true));
        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void TmTextInput_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmTextInput>(p => p.Add(c => c.Label, "Name"));
        cut.Find("input").HasAttribute("aria-required").Should().BeFalse();
        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
    }
}
