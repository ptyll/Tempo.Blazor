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
        var cut = RenderComponent<TmTextInput>();
        cut.Find("input").Should().NotBeNull();
    }

    [Fact]
    public void TmTextInput_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmTextInput>();
        cut.Find("input").ClassList.Should().Contain("tm-input");
    }

    [Fact]
    public void TmTextInput_Default_Type_Is_Text()
    {
        var cut = RenderComponent<TmTextInput>();
        cut.Find("input").GetAttribute("type").Should().Be("text");
    }

    [Fact]
    public void TmTextInput_Custom_Type_Applied()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Type, "email"));
        cut.Find("input").GetAttribute("type").Should().Be("email");
    }

    [Fact]
    public void TmTextInput_Label_Renders_Label_Element()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Label, "Email"));
        cut.Find("label").TextContent.Trim().Should().Be("Email");
    }

    [Fact]
    public void TmTextInput_No_Label_When_Null()
    {
        var cut = RenderComponent<TmTextInput>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_Label_For_Matches_Input_Id()
    {
        var cut = RenderComponent<TmTextInput>(p => p
            .Add(c => c.Id, "my-input")
            .Add(c => c.Label, "Name"));

        cut.Find("label").GetAttribute("for").Should().Be("my-input");
        cut.Find("input").GetAttribute("id").Should().Be("my-input");
    }

    [Fact]
    public void TmTextInput_Placeholder_Applied()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Placeholder, "Enter value"));
        cut.Find("input").GetAttribute("placeholder").Should().Be("Enter value");
    }

    [Fact]
    public void TmTextInput_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Disabled, true));
        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmTextInput_ReadOnly_Sets_Readonly_Attribute()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.ReadOnly, true));
        cut.Find("input").HasAttribute("readonly").Should().BeTrue();
    }

    [Fact]
    public void TmTextInput_Error_Adds_Error_CssClass()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Error, "Required"));
        cut.Find("input").ClassList.Should().Contain("tm-input-error");
    }

    [Fact]
    public void TmTextInput_Error_Shows_Error_Message()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.Error, "Required field"));
        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Required field");
    }

    [Fact]
    public void TmTextInput_HelpText_Shows_When_No_Error()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.HelpText, "Max 100 chars"));
        cut.Find("[data-testid='help-text']").TextContent.Should().Contain("Max 100 chars");
    }

    [Fact]
    public void TmTextInput_HelpText_Hidden_When_Error_Present()
    {
        var cut = RenderComponent<TmTextInput>(p => p
            .Add(c => c.Error, "Required")
            .Add(c => c.HelpText, "Max 100 chars"));

        cut.FindAll("[data-testid='help-text']").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_LeftIcon_Renders_TmIcon()
    {
        var cut = RenderComponent<TmTextInput>(p => p.Add(c => c.LeftIcon, "search"));
        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void TmTextInput_No_Icon_By_Default()
    {
        var cut = RenderComponent<TmTextInput>();
        cut.FindAll(".tm-icon").Should().BeEmpty();
    }

    [Fact]
    public void TmTextInput_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = RenderComponent<TmTextInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("input").Change("hello");

        captured.Should().Be("hello");
    }
}
