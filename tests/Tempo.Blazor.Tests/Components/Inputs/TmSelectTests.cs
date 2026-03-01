using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Inputs;

/// <summary>TDD tests for TmSelect&lt;TValue&gt;.</summary>
public class TmSelectTests : LocalizationTestBase
{
    [Fact]
    public void TmSelect_Renders_Select_Element()
    {
        var cut = RenderComponent<TmSelect<string>>();
        cut.Find("select").Should().NotBeNull();
    }

    [Fact]
    public void TmSelect_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmSelect<string>>();
        cut.Find("select").ClassList.Should().Contain("tm-select");
    }

    [Fact]
    public void TmSelect_Label_Renders_Label_Element()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Label, "Status"));
        cut.Find("label").TextContent.Trim().Should().Be("Status");
    }

    [Fact]
    public void TmSelect_No_Label_When_Null()
    {
        var cut = RenderComponent<TmSelect<string>>();
        cut.FindAll("label").Should().BeEmpty();
    }

    [Fact]
    public void TmSelect_Placeholder_Renders_Disabled_Option()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Placeholder, "Choose..."));
        var placeholderOption = cut.Find("option[disabled]");
        placeholderOption.TextContent.Should().Contain("Choose...");
    }

    [Fact]
    public void TmSelect_No_Placeholder_Option_When_Null()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p
            .AddChildContent("<option value='a'>A</option>"));
        cut.FindAll("option[disabled]").Should().BeEmpty();
    }

    [Fact]
    public void TmSelect_Disabled_Sets_Disabled_Attribute()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Disabled, true));
        cut.Find("select").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSelect_Error_Adds_Error_CssClass()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Error, "Required"));
        cut.Find("select").ClassList.Should().Contain("tm-select-error");
    }

    [Fact]
    public void TmSelect_Error_Shows_Error_Message()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Error, "Select a value"));
        cut.Find("[data-testid='select-error']").TextContent.Should().Contain("Select a value");
    }

    [Fact]
    public void TmSelect_HelpText_Shown_When_No_Error()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.HelpText, "Pick one"));
        cut.Find("[data-testid='select-help']").TextContent.Should().Contain("Pick one");
    }

    [Fact]
    public void TmSelect_ChildContent_Renders_Options()
    {
        var cut = RenderComponent<TmSelect<string>>(p => p
            .AddChildContent("<option value='a'>Alpha</option><option value='b'>Beta</option>"));
        cut.FindAll("option").Count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void TmSelect_ValueChanged_Fires_On_Change()
    {
        string? captured = null;
        var cut = RenderComponent<TmSelect<string>>(p => p
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v))
            .AddChildContent("<option value='alpha'>Alpha</option>"));

        cut.Find("select").Change("alpha");

        captured.Should().Be("alpha");
    }

    [Fact]
    public void TmSelect_Options_Renders_Option_Elements()
    {
        var options = new List<SelectOption<string>>
        {
            new("admin", "Admin"),
            new("editor", "Editor"),
            new("viewer", "Viewer"),
        };
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Options, options));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(3);
        rendered[0].TextContent.Trim().Should().Be("Admin");
        rendered[0].GetAttribute("value").Should().Be("admin");
        rendered[1].TextContent.Trim().Should().Be("Editor");
        rendered[2].TextContent.Trim().Should().Be("Viewer");
    }

    [Fact]
    public void TmSelect_Options_With_Placeholder_Renders_Placeholder_First()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "Alpha"),
            new("b", "Beta"),
        };
        var cut = RenderComponent<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.Placeholder, "Choose..."));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(3); // placeholder + 2 options
        rendered[0].TextContent.Trim().Should().Be("Choose...");
        rendered[0].HasAttribute("disabled").Should().BeTrue();
        rendered[1].TextContent.Trim().Should().Be("Alpha");
    }

    [Fact]
    public void TmSelect_Options_DisabledOption_Renders_Disabled()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "Alpha"),
            new("b", "Beta", isDisabled: true),
        };
        var cut = RenderComponent<TmSelect<string>>(p => p.Add(c => c.Options, options));

        var rendered = cut.FindAll("option");
        rendered[0].HasAttribute("disabled").Should().BeFalse();
        rendered[1].HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void TmSelect_Options_ValueChanged_Fires_On_Selection()
    {
        string? captured = null;
        var options = new List<SelectOption<string>>
        {
            new("x", "Option X"),
            new("y", "Option Y"),
        };
        var cut = RenderComponent<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .Add(c => c.ValueChanged, EventCallback.Factory.Create<string?>(this, v => captured = v)));

        cut.Find("select").Change("y");

        captured.Should().Be("y");
    }

    [Fact]
    public void TmSelect_Options_And_ChildContent_Together()
    {
        var options = new List<SelectOption<string>>
        {
            new("a", "From Options"),
        };
        var cut = RenderComponent<TmSelect<string>>(p => p
            .Add(c => c.Options, options)
            .AddChildContent("<option value='b'>From Child</option>"));

        var rendered = cut.FindAll("option");
        rendered.Count.Should().Be(2);
    }
}
