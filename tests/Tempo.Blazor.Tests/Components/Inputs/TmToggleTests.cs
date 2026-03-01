using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmToggle.</summary>
public class TmToggleTests : LocalizationTestBase
{
    [Fact]
    public void TmToggle_Renders_Checkbox_Input()
    {
        var cut = RenderComponent<TmToggle>();
        cut.Find("input[type='checkbox']").Should().NotBeNull();
    }

    [Fact]
    public void TmToggle_Has_Wrapper_CssClass()
    {
        var cut = RenderComponent<TmToggle>();
        cut.Find(".tm-toggle-wrapper").Should().NotBeNull();
    }

    [Fact]
    public void TmToggle_Checked_Adds_Checked_CssClass()
    {
        var cut = RenderComponent<TmToggle>(p => p.Add(c => c.Value, true));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().Contain("tm-toggle-checked");
    }

    [Fact]
    public void TmToggle_Unchecked_Does_Not_Have_Checked_CssClass()
    {
        var cut = RenderComponent<TmToggle>(p => p.Add(c => c.Value, false));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().NotContain("tm-toggle-checked");
    }

    [Fact]
    public void TmToggle_Disabled_Adds_Disabled_CssClass()
    {
        var cut = RenderComponent<TmToggle>(p => p.Add(c => c.Disabled, true));
        cut.Find(".tm-toggle-wrapper").ClassList.Should().Contain("tm-toggle-disabled");
    }

    [Fact]
    public void TmToggle_Label_Renders_Label_Text()
    {
        var cut = RenderComponent<TmToggle>(p => p.Add(c => c.Label, "Dark mode"));
        cut.Find(".tm-toggle-label-text").TextContent.Should().Contain("Dark mode");
    }

    [Fact]
    public void TmToggle_No_Label_Text_When_Null()
    {
        var cut = RenderComponent<TmToggle>();
        cut.FindAll(".tm-toggle-label-text").Should().BeEmpty();
    }

    [Fact]
    public void TmToggle_ValueChanged_Fires_On_Change()
    {
        bool? captured = null;
        var cut = RenderComponent<TmToggle>(p => p
            .Add(c => c.Value, false)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<bool>(this, v => captured = v)));

        cut.Find("input[type='checkbox']").Change(true);

        captured.Should().BeTrue();
    }
}
