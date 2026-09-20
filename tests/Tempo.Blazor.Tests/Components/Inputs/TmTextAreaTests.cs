using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmTextArea.</summary>
public class TmTextAreaTests : LocalizationTestBase
{
    [Fact]
    public void TmTextArea_Renders_Textarea_Element()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").Should().NotBeNull();
    }

    [Fact]
    public void TmTextArea_Has_Base_CssClass()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").ClassList.Should().Contain("tm-input");
    }

    /// <summary>
    /// Fáze 18.1 fixes the measured `.tm-input|.tm-textarea` cascade tie with the compound
    /// <c>.tm-input.tm-textarea</c> — a selector that only reaches a textarea carrying BOTH
    /// classes. This pins the markup contract it relies on.
    /// </summary>
    [Fact]
    public void TmTextArea_CarriesBaseAndModifier_OnTheSameElement()
    {
        var cut = Render<TmTextArea>();

        cut.Find("textarea").ClassList.Should()
            .Contain("tm-input")
            .And.Contain("tm-textarea");
    }

    [Fact]
    public void TmTextArea_Default_Rows_Is_3()
    {
        var cut = Render<TmTextArea>();
        cut.Find("textarea").GetAttribute("rows").Should().Be("3");
    }

    [Fact]
    public void TmTextArea_Custom_Rows_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Rows, 6));
        cut.Find("textarea").GetAttribute("rows").Should().Be("6");
    }

    [Fact]
    public void TmTextArea_Label_Renders_Label_Element()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Description"));
        cut.Find("label").TextContent.Trim().Should().Be("Description");
    }

    [Fact]
    public void TmTextArea_No_Label_When_Null()
    {
        var cut = Render<TmTextArea>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmTextArea_Placeholder_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Placeholder, "Enter text..."));
        cut.Find("textarea").GetAttribute("placeholder").Should().Be("Enter text...");
    }

    [Fact]
    public void TmTextArea_Disabled_Sets_Disabled_Attribute()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Disabled, true));
        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmTextArea_Error_Adds_Error_CssClass()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("textarea").ClassList.Should().Contain("tm-input-error");
    }

    [Fact]
    public void TmTextArea_Error_Shows_Error_Message()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Too short");
    }

    [Fact]
    public void TmTextArea_MaxLength_Applied()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.MaxLength, 200));
        cut.Find("textarea").GetAttribute("maxlength").Should().Be("200");
    }

    [Fact]
    public void TmTextArea_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("textarea").Change("hello world");

        captured.Should().Be("hello world");
    }

    // ── Required (accessibility) ─────────────────────────────────

    [Fact]
    public void TmTextArea_Required_SetsAriaRequiredOnTextarea()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Required, true));
        cut.Find("textarea").GetAttribute("aria-required").Should().Be("true");
    }

    [Fact]
    public void TmTextArea_Required_AddsRequiredMarkerClassToLabel()
    {
        var cut = Render<TmTextArea>(p => p
            .Add(c => c.Label, "Comment")
            .Add(c => c.Required, true));
        cut.Find("label").ClassList.Should().Contain("tm-input-label-required");
    }

    [Fact]
    public void TmTextArea_NotRequired_HasNoAriaRequiredAndNoMarker()
    {
        var cut = Render<TmTextArea>(p => p.Add(c => c.Label, "Comment"));
        cut.Find("textarea").HasAttribute("aria-required").Should().BeFalse();
        cut.Find("label").ClassList.Should().NotContain("tm-input-label-required");
    }
}
