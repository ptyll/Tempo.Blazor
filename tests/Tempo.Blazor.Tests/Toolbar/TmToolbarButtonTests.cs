using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

public class TmToolbarButtonTests : LocalizationTestBase
{
    [Fact]
    public void ToolbarButton_RendersText()
    {
        var cut = RenderComponent<TmToolbarButton>(p => p.Add(c => c.Text, "Export"));

        cut.Find(".tm-toolbar-btn").TextContent.Trim().Should().Contain("Export");
    }

    [Fact]
    public void ToolbarButton_WithIcon_RendersIcon()
    {
        var cut = RenderComponent<TmToolbarButton>(p => p
            .Add(c => c.Text, "Download")
            .Add(c => c.Icon, "download"));

        cut.FindAll(".tm-icon").Should().NotBeEmpty();
    }

    [Fact]
    public void ToolbarButton_IconOnly_NoText()
    {
        var cut = RenderComponent<TmToolbarButton>(p => p
            .Add(c => c.Icon, "download")
            .Add(c => c.Tooltip, "Download"));

        cut.FindAll(".tm-toolbar-btn-text").Should().BeEmpty();
    }

    [Fact]
    public void ToolbarButton_Click_FiresCallback()
    {
        var clicked = false;
        var cut = RenderComponent<TmToolbarButton>(p => p
            .Add(c => c.Text,    "Click me")
            .Add(c => c.OnClick, () => clicked = true));

        cut.Find(".tm-toolbar-btn").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void ToolbarButton_Disabled_IsDisabled()
    {
        var cut = RenderComponent<TmToolbarButton>(p => p
            .Add(c => c.Text,     "Disabled")
            .Add(c => c.Disabled, true));

        cut.Find("button[disabled]").Should().NotBeNull();
    }

    [Fact]
    public void ToolbarButton_Tooltip_RenderedAsTitle()
    {
        var cut = RenderComponent<TmToolbarButton>(p => p
            .Add(c => c.Icon,    "info")
            .Add(c => c.Tooltip, "More info"));

        cut.Find(".tm-toolbar-btn").GetAttribute("title").Should().Be("More info");
    }
}
