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

    // ── Fáze 5: optional error announcement (no double announcement) ─────────────

    /// <summary>
    /// Fáze 5 (SpravaFinanci P04-CF F6): a host that moves focus to the invalid field already gets
    /// the error read through <c>aria-describedby</c>; with <c>AnnounceError="false"</c> the message
    /// keeps rendering (styled, linked, <c>aria-invalid</c>) but WITHOUT <c>role="alert"</c>, so the
    /// screen reader does not announce the same error twice.
    /// </summary>
    [Fact]
    public void TmTextInput_AnnounceError_False_OmitsRoleAlert_KeepsAriaInvalidAndDescribedBy()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "email")
            .Add(c => c.Error, "Required")
            .Add(c => c.AnnounceError, false));

        var err = cut.Find("[data-testid='error-message']");
        err.HasAttribute("role").Should().BeFalse(
            "focus moved to the field lets aria-describedby read the error; role=alert would announce it a second time");
        err.TextContent.Should().Contain("Required", "the message itself must still render");

        var input = cut.Find("input");
        input.GetAttribute("aria-invalid").Should().Be("true");
        input.GetAttribute("aria-describedby").Should().Be("email-error");
    }

    /// <summary>Default (no parameter set) keeps the current output: the message is an assertive live region.</summary>
    [Fact]
    public void TmTextInput_AnnounceError_Default_RendersRoleAlert()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "email")
            .Add(c => c.Error, "Required"));

        cut.Find("[data-testid='error-message']").GetAttribute("role").Should().Be("alert");
    }

    /// <summary>
    /// Freezes the unchanged default error DOM (full markup): the opt-out must not shift anything
    /// for hosts that do not set <see cref="TmTextInput.AnnounceError"/>.
    /// </summary>
    [Fact]
    public void TmTextInput_Error_DefaultAnnounce_FullMarkupMatches()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "snap-err")
            .Add(c => c.Label, "Amount")
            .Add(c => c.Value, "bad")
            .Add(c => c.Error, "Enter a number"));

        cut.MarkupMatches(
            @"<div class=""tm-input-wrapper"">
                <label for=""snap-err"" class=""tm-input-label "">Amount</label>
                <div class=""tm-input-container"">
                    <input id=""snap-err"" type=""text"" value=""bad"" tabindex=""0""
                           aria-invalid=""true"" aria-describedby=""snap-err-error""
                           class=""tm-input tm-input-error"" />
                </div>
                <div id=""snap-err-error"" data-testid=""error-message"" class=""tm-input-error-message"" role=""alert"">
                    <svg class=""tm-icon tm-icon-xs tm-icon-current"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" aria-hidden=""true"" focusable=""false"">
                        <circle cx=""12"" cy=""12"" r=""10"" />
                        <line x1=""12"" y1=""8"" x2=""12"" y2=""12"" />
                        <line x1=""12"" y1=""16"" x2=""12.01"" y2=""16"" />
                    </svg>
                    <span>Enter a number</span>
                </div>
            </div>");
    }

    /// <summary>With the announcement off, the same DOM holds except the role attribute disappears.</summary>
    [Fact]
    public void TmTextInput_AnnounceError_False_FullMarkupMatchesExceptRole()
    {
        var cut = Render<TmTextInput>(p => p
            .Add(c => c.Id, "snap-mute")
            .Add(c => c.Label, "Amount")
            .Add(c => c.Value, "bad")
            .Add(c => c.Error, "Enter a number")
            .Add(c => c.AnnounceError, false));

        cut.MarkupMatches(
            @"<div class=""tm-input-wrapper"">
                <label for=""snap-mute"" class=""tm-input-label "">Amount</label>
                <div class=""tm-input-container"">
                    <input id=""snap-mute"" type=""text"" value=""bad"" tabindex=""0""
                           aria-invalid=""true"" aria-describedby=""snap-mute-error""
                           class=""tm-input tm-input-error"" />
                </div>
                <div id=""snap-mute-error"" data-testid=""error-message"" class=""tm-input-error-message"">
                    <svg class=""tm-icon tm-icon-xs tm-icon-current"" viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" aria-hidden=""true"" focusable=""false"">
                        <circle cx=""12"" cy=""12"" r=""10"" />
                        <line x1=""12"" y1=""8"" x2=""12"" y2=""12"" />
                        <line x1=""12"" y1=""16"" x2=""12.01"" y2=""16"" />
                    </svg>
                    <span>Enter a number</span>
                </div>
            </div>");
    }
}
