using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmCheckbox.</summary>
public class TmCheckboxTests : LocalizationTestBase
{
    [Fact]
    public void TmCheckbox_Renders_Checkbox_Input()
    {
        var cut = RenderComponent<TmCheckbox>();
        cut.Find("input[type='checkbox']").Should().NotBeNull();
    }

    [Fact]
    public void TmCheckbox_Has_Wrapper_CssClass()
    {
        var cut = RenderComponent<TmCheckbox>();
        cut.Find(".tm-checkbox-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void TmCheckbox_Label_Renders_Label_Text()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.Label, "Accept terms"));
        cut.Find(".tm-checkbox-text").TextContent.Should().Contain("Accept terms");
    }

    [Fact]
    public void TmCheckbox_No_Label_Text_When_Null()
    {
        var cut = RenderComponent<TmCheckbox>();
        cut.FindAll(".tm-checkbox-text").Should().BeEmpty();
    }

    [Fact]
    public void TmCheckbox_Checked_When_Value_True()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.Value, true));
        cut.Find("input[type='checkbox']").HasAttribute("checked").Should().BeTrue();
    }

    [Fact]
    public void TmCheckbox_Unchecked_When_Value_False()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.Value, false));
        cut.Find("input[type='checkbox']").HasAttribute("checked").Should().BeFalse();
    }

    [Fact]
    public void TmCheckbox_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.Disabled, true));
        cut.Find("input[type='checkbox']").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmCheckbox_Disabled_Adds_Disabled_CssClass_To_Wrapper()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.Disabled, true));
        cut.Find(".tm-checkbox-wrapper").ClassList.Should().Contain("tm-checkbox-disabled");
    }

    [Fact]
    public void TmCheckbox_HelpText_Shown()
    {
        var cut = RenderComponent<TmCheckbox>(p => p.Add(c => c.HelpText, "Optional field"));
        cut.Find("[data-testid='checkbox-help']").TextContent.Should().Contain("Optional field");
    }

    [Fact]
    public void TmCheckbox_ValueChanged_Fires_On_Change()
    {
        bool? captured = null;
        var cut = RenderComponent<TmCheckbox>(p => p
            .Add(c => c.Value, false)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<bool>(this, v => captured = v)));

        cut.Find("input[type='checkbox']").Change(true);

        captured.Should().BeTrue();
    }
}
