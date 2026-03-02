using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Forms;

/// <summary>TDD tests for TmInlineEdit.</summary>
public class TmInlineEditTests : LocalizationTestBase
{
    [Fact]
    public void TmInlineEdit_Renders_DisplayMode_By_Default()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello"));

        cut.Find(".tm-inline-edit").Should().NotBeNull();
        cut.Find(".tm-inline-edit-display").TextContent.Should().Contain("Hello");
        cut.FindAll(".tm-inline-edit-input").Count.Should().Be(0);
    }

    [Fact]
    public void TmInlineEdit_Shows_Placeholder_When_Empty()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "")
            .Add(c => c.Placeholder, "Click to edit"));

        var display = cut.Find(".tm-inline-edit-display");
        display.ClassList.Should().Contain("tm-inline-edit-display--placeholder");
        display.TextContent.Should().Contain("Click to edit");
    }

    [Fact]
    public void TmInlineEdit_Click_Switches_To_EditMode()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello"));

        cut.Find(".tm-inline-edit-display").Click();

        cut.FindAll(".tm-inline-edit-display").Count.Should().Be(0);
        cut.Find("input.tm-inline-edit-input").Should().NotBeNull();
    }

    [Fact]
    public void TmInlineEdit_Enter_Saves_Value()
    {
        string? saved = null;
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.OnSave, EventCallback.Factory.Create<string>(this, v => saved = v)));

        cut.Find(".tm-inline-edit-display").Click();
        cut.Find("input.tm-inline-edit-input").Input("Updated");
        cut.Find("input.tm-inline-edit-input").KeyUp(new KeyboardEventArgs { Key = "Enter" });

        saved.Should().Be("Updated");
        cut.FindAll("input.tm-inline-edit-input").Count.Should().Be(0);
    }

    [Fact]
    public void TmInlineEdit_Escape_Cancels_Edit()
    {
        string? saved = null;
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.OnSave, EventCallback.Factory.Create<string>(this, v => saved = v)));

        cut.Find(".tm-inline-edit-display").Click();
        cut.Find("input.tm-inline-edit-input").Input("Changed");
        cut.Find("input.tm-inline-edit-input").KeyUp(new KeyboardEventArgs { Key = "Escape" });

        saved.Should().BeNull();
        cut.Find(".tm-inline-edit-display").TextContent.Should().Contain("Hello");
    }

    [Fact]
    public void TmInlineEdit_Validation_Error_Prevents_Save()
    {
        string? saved = null;
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.OnSave, EventCallback.Factory.Create<string>(this, v => saved = v))
            .Add(c => c.Validate, v => string.IsNullOrWhiteSpace(v) ? "Value is required" : null));

        cut.Find(".tm-inline-edit-display").Click();
        cut.Find("input.tm-inline-edit-input").Input("");
        cut.Find("input.tm-inline-edit-input").KeyUp(new KeyboardEventArgs { Key = "Enter" });

        saved.Should().BeNull();
        cut.Find(".tm-inline-edit-error").TextContent.Should().Contain("Value is required");
        cut.Find("input.tm-inline-edit-input").ClassList.Should().Contain("tm-inline-edit-input--error");
    }

    [Fact]
    public void TmInlineEdit_Disabled_Does_Not_Switch_To_EditMode()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.Disabled, true));

        cut.Find(".tm-inline-edit-display").Click();

        cut.FindAll("input.tm-inline-edit-input").Count.Should().Be(0);
        cut.Find(".tm-inline-edit-display").Should().NotBeNull();
    }

    [Fact]
    public void TmInlineEdit_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.Class, "my-class"));

        cut.Find(".tm-inline-edit").ClassList.Should().Contain("my-class");
    }

    [Fact]
    public void TmInlineEdit_Disabled_Has_Disabled_Class()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello")
            .Add(c => c.Disabled, true));

        cut.Find(".tm-inline-edit").ClassList.Should().Contain("tm-inline-edit--disabled");
    }
}
