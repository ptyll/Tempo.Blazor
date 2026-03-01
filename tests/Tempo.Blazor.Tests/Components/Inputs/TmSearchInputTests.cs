using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmSearchInput.</summary>
public class TmSearchInputTests : LocalizationTestBase
{
    [Fact]
    public void TmSearchInput_Renders_Search_Input()
    {
        var cut = RenderComponent<TmSearchInput>();
        cut.Find("input[type='search']").Should().NotBeNull();
    }

    [Fact]
    public void TmSearchInput_Has_Search_Icon()
    {
        var cut = RenderComponent<TmSearchInput>();
        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void TmSearchInput_Default_Placeholder()
    {
        var cut = RenderComponent<TmSearchInput>();
        cut.Find("input").GetAttribute("placeholder").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void TmSearchInput_Custom_Placeholder()
    {
        var cut = RenderComponent<TmSearchInput>(p => p.Add(c => c.Placeholder, "Find users..."));
        cut.Find("input").GetAttribute("placeholder").Should().Be("Find users...");
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Hidden_When_Value_Empty()
    {
        var cut = RenderComponent<TmSearchInput>(p => p.Add(c => c.Value, ""));
        cut.FindAll(".tm-search-clear").Should().BeEmpty();
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Shown_When_Value_Set()
    {
        var cut = RenderComponent<TmSearchInput>(p => p.Add(c => c.Value, "hello"));
        cut.Find(".tm-search-clear").Should().NotBeNull();
    }

    [Fact]
    public void TmSearchInput_Clear_Button_Fires_Empty_String()
    {
        string? captured = null;
        var cut = RenderComponent<TmSearchInput>(p => p
            .Add(c => c.Value, "hello")
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find(".tm-search-clear").Click();

        captured.Should().Be("");
    }

    [Fact]
    public void TmSearchInput_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmSearchInput>(p => p.Add(c => c.Disabled, true));
        cut.Find("input").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSearchInput_ValueChanged_Fires_On_Input()
    {
        string? captured = null;
        var cut = RenderComponent<TmSearchInput>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string>(this, v => captured = v)));

        cut.Find("input").Input("test");

        captured.Should().Be("test");
    }
}
