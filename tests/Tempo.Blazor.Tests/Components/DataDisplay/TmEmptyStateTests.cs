using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>TDD tests for TmEmptyState.</summary>
public class TmEmptyStateTests : LocalizationTestBase
{
    [Fact]
    public void TmEmptyState_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmEmptyState>(p => p.Add(c => c.Title, "No data"));
        cut.Find(".tm-empty-state").Should().NotBeNull();
    }

    [Fact]
    public void TmEmptyState_Renders_Title()
    {
        var cut = RenderComponent<TmEmptyState>(p => p.Add(c => c.Title, "Nothing here"));
        cut.Find(".tm-empty-state-title").TextContent.Should().Contain("Nothing here");
    }

    [Fact]
    public void TmEmptyState_Renders_Description_When_Set()
    {
        var cut = RenderComponent<TmEmptyState>(p => p
            .Add(c => c.Title, "Empty")
            .Add(c => c.Description, "Try adding some items."));

        cut.Find(".tm-empty-state-description").TextContent.Should().Contain("Try adding some items.");
    }

    [Fact]
    public void TmEmptyState_No_Description_When_Null()
    {
        var cut = RenderComponent<TmEmptyState>(p => p.Add(c => c.Title, "Empty"));
        cut.FindAll(".tm-empty-state-description").Should().BeEmpty();
    }

    [Fact]
    public void TmEmptyState_Renders_Icon_When_Set()
    {
        var cut = RenderComponent<TmEmptyState>(p => p
            .Add(c => c.Title, "Empty")
            .Add(c => c.Icon, "file"));

        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void TmEmptyState_No_Icon_When_Null()
    {
        var cut = RenderComponent<TmEmptyState>(p => p.Add(c => c.Title, "Empty"));
        cut.FindAll(".tm-icon").Should().BeEmpty();
    }

    [Fact]
    public void TmEmptyState_Action_Button_Rendered_When_ActionText_And_OnAction_Set()
    {
        var cut = RenderComponent<TmEmptyState>(p => p
            .Add(c => c.Title, "Empty")
            .Add(c => c.ActionText, "Add item")
            .Add(c => c.OnAction, EventCallback.Factory.Create(this, () => { })));

        cut.Find("button").TextContent.Should().Contain("Add item");
    }

    [Fact]
    public void TmEmptyState_No_Action_Button_When_ActionText_Null()
    {
        var cut = RenderComponent<TmEmptyState>(p => p.Add(c => c.Title, "Empty"));
        cut.FindAll("button").Should().BeEmpty();
    }
}
