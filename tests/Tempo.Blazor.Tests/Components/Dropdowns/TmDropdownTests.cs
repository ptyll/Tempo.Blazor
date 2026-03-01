using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Dropdowns;

/// <summary>TDD tests for TmDropdown.</summary>
public class TmDropdownTests : LocalizationTestBase
{
    [Fact]
    public void TmDropdown_Renders_Trigger_Button()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.Find("button.tm-dropdown-trigger").Should().NotBeNull();
    }

    [Fact]
    public void TmDropdown_Trigger_Shows_Text()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Actions"));

        cut.Find("button.tm-dropdown-trigger").TextContent.Should().Contain("Actions");
    }

    [Fact]
    public void TmDropdown_Menu_Hidden_By_Default()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmDropdown_Click_Opens_Menu()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div class='item'>Item 1</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.FindAll(".tm-dropdown-menu").Should().NotBeEmpty();
    }

    [Fact]
    public void TmDropdown_Click_Again_Closes_Menu()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find("button.tm-dropdown-trigger").Click();

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmDropdown_Escape_Key_Closes_Menu()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find(".tm-dropdown-wrapper").KeyDown(Key.Escape);

        cut.FindAll(".tm-dropdown-menu").Should().BeEmpty();
    }

    [Fact]
    public void TmDropdown_Trigger_Has_Aria_Expanded_False_When_Closed()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options"));

        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void TmDropdown_Trigger_Has_Aria_Expanded_True_When_Open()
    {
        var cut = RenderComponent<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent("<div>Item</div>"));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.Find("button.tm-dropdown-trigger").GetAttribute("aria-expanded").Should().Be("true");
    }
}
