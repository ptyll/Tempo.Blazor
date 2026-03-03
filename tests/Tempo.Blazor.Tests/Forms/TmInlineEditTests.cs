using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Forms;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Forms;

/// <summary>Tests for TmInlineEdit component.</summary>
public class TmInlineEditTests : LocalizationTestBase
{
    [Fact]
    public void InlineEdit_DisplayMode_ShowsValue()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Test Value"));

        cut.Find(".tm-inline-edit").Should().NotBeNull();
        cut.Find(".tm-inline-edit-display").TextContent.Should().Contain("Test Value");
        cut.FindAll("input.tm-inline-edit-input").Count.Should().Be(0);
    }

    [Fact]
    public void InlineEdit_ClickValue_EntersEditMode()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Hello"));

        cut.Find(".tm-inline-edit-display").Click();

        cut.FindAll(".tm-inline-edit-display").Count.Should().Be(0);
        cut.Find("input.tm-inline-edit-input").Should().NotBeNull();
    }

    [Fact]
    public void InlineEdit_EnterKey_SavesValue()
    {
        string? saved = null;
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Original")
            .Add(c => c.OnSave, EventCallback.Factory.Create<string>(this, v => saved = v)));

        cut.Find(".tm-inline-edit-display").Click();
        cut.Find("input.tm-inline-edit-input").Input("Updated");
        cut.Find("input.tm-inline-edit-input").KeyUp(new KeyboardEventArgs { Key = "Enter" });

        saved.Should().Be("Updated");
        cut.FindAll("input.tm-inline-edit-input").Count.Should().Be(0);
    }

    [Fact]
    public void InlineEdit_EscapeKey_CancelsEdit()
    {
        string? saved = null;
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Original")
            .Add(c => c.OnSave, EventCallback.Factory.Create<string>(this, v => saved = v)));

        cut.Find(".tm-inline-edit-display").Click();
        cut.Find("input.tm-inline-edit-input").Input("Changed");
        cut.Find("input.tm-inline-edit-input").KeyUp(new KeyboardEventArgs { Key = "Escape" });

        saved.Should().BeNull();
        cut.Find(".tm-inline-edit-display").TextContent.Should().Contain("Original");
    }

    [Fact]
    public void InlineEdit_Validation_ShowsError()
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
    public void InlineEdit_Disabled_CannotEdit()
    {
        var cut = RenderComponent<TmInlineEdit>(p => p
            .Add(c => c.Value, "Read Only")
            .Add(c => c.Disabled, true));

        cut.Find(".tm-inline-edit").ClassList.Should().Contain("tm-inline-edit--disabled");
        cut.Find(".tm-inline-edit-display").Click();

        cut.FindAll("input.tm-inline-edit-input").Count.Should().Be(0);
        cut.Find(".tm-inline-edit-display").Should().NotBeNull();
    }
}
