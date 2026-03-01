using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Buttons;

/// <summary>
/// TDD tests for TmButton component.
/// RED phase: these tests are written before the component implementation.
/// </summary>
public class TmButtonTests : LocalizationTestBase
{
    // ─── Rendering ────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Renders_Button_Element()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click me"));

        cut.Find("button").Should().NotBeNull();
    }

    [Fact]
    public void TmButton_Renders_ChildContent()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Save"));

        cut.Find("button").TextContent.Trim().Should().Be("Save");
    }

    // ─── ButtonType ───────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Default_Type_Is_Button()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("type").Should().Be("button");
    }

    [Theory]
    [InlineData(ButtonType.Button, "button")]
    [InlineData(ButtonType.Submit, "submit")]
    [InlineData(ButtonType.Reset,  "reset")]
    public void TmButton_Renders_Correct_Html_Type(ButtonType buttonType, string expectedHtmlType)
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Type, buttonType)
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("type").Should().Be(expectedHtmlType);
    }

    // ─── Variant CSS ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ButtonVariant.Primary,   "tm-btn-primary")]
    [InlineData(ButtonVariant.Secondary, "tm-btn-secondary")]
    [InlineData(ButtonVariant.Ghost,     "tm-btn-ghost")]
    [InlineData(ButtonVariant.Danger,    "tm-btn-danger")]
    [InlineData(ButtonVariant.Outline,   "tm-btn-outline")]
    [InlineData(ButtonVariant.Link,      "tm-btn-link")]
    [InlineData(ButtonVariant.Default,   "tm-btn-default")]
    public void TmButton_Applies_Variant_CssClass(ButtonVariant variant, string expectedClass)
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Variant, variant)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmButton_Default_Variant_Is_Primary()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-primary");
    }

    // ─── Size CSS ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(ButtonSize.Xs, "tm-btn-xs")]
    [InlineData(ButtonSize.Sm, "tm-btn-sm")]
    [InlineData(ButtonSize.Md, "tm-btn-md")]
    [InlineData(ButtonSize.Lg, "tm-btn-lg")]
    public void TmButton_Applies_Size_CssClass(ButtonSize size, string expectedClass)
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Size, size)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void TmButton_Default_Size_Is_Md()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-md");
    }

    // ─── Block ────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Block_Adds_Block_CssClass()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Block, true)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn-block");
    }

    [Fact]
    public void TmButton_NonBlock_Does_Not_Add_Block_CssClass()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Block, false)
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().NotContain("tm-btn-block");
    }

    // ─── Disabled ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Disabled, true)
            .AddChildContent("Click"));

        cut.Find("button").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmButton_Disabled_Does_Not_Fire_OnClick()
    {
        var clicked = false;
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Disabled, true)
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").Click();

        clicked.Should().BeFalse();
    }

    // ─── Loading ──────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Loading_Renders_Spinner_And_Disables()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .AddChildContent("Save"));

        // Button must be disabled while loading
        cut.Find("button").HasAttribute("disabled").Should().BeTrue();
        // Spinner should be present
        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
    }

    [Fact]
    public void TmButton_Loading_Does_Not_Fire_OnClick()
    {
        var clicked = false;
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Save"));

        cut.Find("button").Click();

        clicked.Should().BeFalse();
    }

    [Fact]
    public void TmButton_Loading_Hides_Icon_And_Shows_Spinner()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.IsLoading, true)
            .Add(c => c.Icon, IconNames.Check)
            .AddChildContent("Save"));

        // Spinner shown, TmIcon not shown while loading
        cut.FindAll(".tm-spinner").Should().NotBeEmpty();
        cut.FindAll(".tm-icon").Should().BeEmpty();
    }

    // ─── Icon ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Icon_Left_Renders_Icon_Before_Content()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Icon, IconNames.Check)
            .Add(c => c.IconRight, false)
            .AddChildContent("Save"));

        var children = cut.Find("button").ChildNodes;
        // First child should be the icon (svg), then text
        children.Length.Should().BeGreaterThan(1);
    }

    [Fact]
    public void TmButton_Icon_Right_When_IconRight_True()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.Icon, IconNames.Check)
            .Add(c => c.IconRight, true)
            .AddChildContent("Next"));

        // When IconRight=true, icon SVG should be the last element
        var button = cut.Find("button");
        button.ChildNodes.Length.Should().BeGreaterThan(1);
    }

    // ─── Click ────────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Click_Fires_OnClick()
    {
        var clicked = false;
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void TmButton_Enter_Key_Fires_OnClick()
    {
        var clicked = false;
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").KeyDown(Key.Enter);

        clicked.Should().BeTrue();
    }

    [Fact]
    public void TmButton_Space_Key_Fires_OnClick()
    {
        var clicked = false;
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.OnClick, EventCallback.Factory.Create(this, () => { clicked = true; }))
            .AddChildContent("Click"));

        cut.Find("button").KeyDown(" ");

        clicked.Should().BeTrue();
    }

    // ─── TabIndex ─────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_Default_TabIndex_Is_Zero()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void TmButton_Custom_TabIndex_Is_Applied()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.TabIndex, -1)
            .AddChildContent("Click"));

        cut.Find("button").GetAttribute("tabindex").Should().Be("-1");
    }

    // ─── AriaLabel ────────────────────────────────────────────────────────────

    [Fact]
    public void TmButton_AriaLabel_Is_Applied_When_Set()
    {
        var cut = RenderComponent<TmButton>(p => p
            .Add(c => c.AriaLabel, "Delete item")
            .AddChildContent("X"));

        cut.Find("button").GetAttribute("aria-label").Should().Be("Delete item");
    }

    [Fact]
    public void TmButton_AriaLabel_Not_Rendered_When_Null()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Save"));

        cut.Find("button").HasAttribute("aria-label").Should().BeFalse();
    }

    // ─── Base CSS class always present ────────────────────────────────────────

    [Fact]
    public void TmButton_Always_Has_TmBtn_Class()
    {
        var cut = RenderComponent<TmButton>(p => p
            .AddChildContent("Click"));

        cut.Find("button").ClassList.Should().Contain("tm-btn");
    }
}
