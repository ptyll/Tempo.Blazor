using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>TDD tests for TmSection.</summary>
public class TmSectionTests : LocalizationTestBase
{
    [Fact]
    public void TmSection_Renders_With_Base_Class()
    {
        var cut = RenderComponent<TmSection>(p => p
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section").Should().NotBeNull();
    }

    [Fact]
    public void TmSection_Renders_Title()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "My Section")
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-title").TextContent.Should().Contain("My Section");
    }

    [Fact]
    public void TmSection_Hides_Header_When_No_Title()
    {
        var cut = RenderComponent<TmSection>(p => p
            .AddChildContent("<p>Content</p>"));

        cut.FindAll(".tm-section-header").Count.Should().Be(0);
    }

    [Fact]
    public void TmSection_Renders_ChildContent()
    {
        var cut = RenderComponent<TmSection>(p => p
            .AddChildContent("<p>Hello World</p>"));

        cut.Find(".tm-section-content").TextContent.Should().Contain("Hello World");
    }

    [Fact]
    public void TmSection_Renders_Icon()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Settings")
            .Add(c => c.Icon, "settings")
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-header-icon").Should().NotBeNull();
    }

    [Fact]
    public void TmSection_Renders_HeaderActions()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.HeaderActions, builder => builder.AddContent(0, "Action"))
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-actions").TextContent.Should().Contain("Action");
    }

    [Fact]
    public void TmSection_Collapsible_Has_Class()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, true)
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section").ClassList.Should().Contain("tm-section--collapsible");
    }

    [Fact]
    public void TmSection_Collapsed_Hides_Content()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, true)
            .Add(c => c.Collapsed, true)
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section").ClassList.Should().Contain("tm-section--collapsed");
        cut.FindAll(".tm-section-content").Count.Should().Be(0);
    }

    [Fact]
    public void TmSection_Click_Header_Toggles_Collapse()
    {
        var collapsed = false;
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, true)
            .Add(c => c.Collapsed, false)
            .Add(c => c.CollapsedChanged, EventCallback.Factory.Create<bool>(this, v => collapsed = v))
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-header").Click();

        collapsed.Should().BeTrue();
    }

    [Fact]
    public void TmSection_NonCollapsible_Click_Does_Nothing()
    {
        var collapsed = false;
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, false)
            .Add(c => c.CollapsedChanged, EventCallback.Factory.Create<bool>(this, v => collapsed = v))
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-header").Click();

        collapsed.Should().BeFalse();
    }

    [Fact]
    public void TmSection_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Class, "my-custom-class")
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section").ClassList.Should().Contain("my-custom-class");
    }

    [Fact]
    public void TmSection_Collapsible_Header_Has_AriaExpanded()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, true)
            .Add(c => c.Collapsed, false)
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-header").GetAttribute("aria-expanded")
            .Should().Be("true");
    }

    [Fact]
    public void TmSection_Collapsed_Header_AriaExpanded_False()
    {
        var cut = RenderComponent<TmSection>(p => p
            .Add(c => c.Title, "Title")
            .Add(c => c.Collapsible, true)
            .Add(c => c.Collapsed, true)
            .AddChildContent("<p>Content</p>"));

        cut.Find(".tm-section-header").GetAttribute("aria-expanded")
            .Should().Be("false");
    }
}
