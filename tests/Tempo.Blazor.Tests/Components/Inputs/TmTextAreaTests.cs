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
        var cut = RenderComponent<TmTextArea>();
        cut.Find("textarea").Should().NotBeNull();
    }

    [Fact]
    public void TmTextArea_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmTextArea>();
        cut.Find("textarea").ClassList.Should().Contain("tm-input");
    }

    [Fact]
    public void TmTextArea_Default_Rows_Is_3()
    {
        var cut = RenderComponent<TmTextArea>();
        cut.Find("textarea").GetAttribute("rows").Should().Be("3");
    }

    [Fact]
    public void TmTextArea_Custom_Rows_Applied()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Rows, 6));
        cut.Find("textarea").GetAttribute("rows").Should().Be("6");
    }

    [Fact]
    public void TmTextArea_Label_Renders_Label_Element()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Label, "Description"));
        cut.Find("label").TextContent.Trim().Should().Be("Description");
    }

    [Fact]
    public void TmTextArea_No_Label_When_Null()
    {
        var cut = RenderComponent<TmTextArea>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmTextArea_Placeholder_Applied()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Placeholder, "Enter text..."));
        cut.Find("textarea").GetAttribute("placeholder").Should().Be("Enter text...");
    }

    [Fact]
    public void TmTextArea_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Disabled, true));
        cut.Find("textarea").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmTextArea_Error_Adds_Error_CssClass()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("textarea").ClassList.Should().Contain("tm-input-error");
    }

    [Fact]
    public void TmTextArea_Error_Shows_Error_Message()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.Error, "Too short"));
        cut.Find("[data-testid='error-message']").TextContent.Should().Contain("Too short");
    }

    [Fact]
    public void TmTextArea_MaxLength_Applied()
    {
        var cut = RenderComponent<TmTextArea>(p => p.Add(c => c.MaxLength, 200));
        cut.Find("textarea").GetAttribute("maxlength").Should().Be("200");
    }

    [Fact]
    public void TmTextArea_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = RenderComponent<TmTextArea>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("textarea").Change("hello world");

        captured.Should().Be("hello world");
    }
}
