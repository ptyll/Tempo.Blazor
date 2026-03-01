using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

public class TmToolbarTests : LocalizationTestBase
{
    [Fact]
    public void Toolbar_RendersChildContent()
    {
        var cut = RenderComponent<TmToolbar>(p => p
            .AddChildContent("<span class='test-item'>Item</span>"));

        cut.Find(".test-item").TextContent.Should().Be("Item");
    }

    [Fact]
    public void Toolbar_WithTitle_RendersTitle()
    {
        var cut = RenderComponent<TmToolbar>(p => p.Add(c => c.Title, "My Toolbar"));

        cut.Find(".tm-toolbar-title").TextContent.Should().Be("My Toolbar");
    }

    [Fact]
    public void Toolbar_ActionsSlot_RenderedRight()
    {
        var cut = RenderComponent<TmToolbar>(p => p
            .Add(c => c.Actions, "<button class='action-btn'>Export</button>"));

        cut.Find(".tm-toolbar-actions .action-btn").Should().NotBeNull();
    }

    [Fact]
    public void Toolbar_Sticky_HasStickyClass()
    {
        var cut = RenderComponent<TmToolbar>(p => p.Add(c => c.Sticky, true));

        cut.Find(".tm-toolbar").ClassList.Should().Contain("tm-toolbar--sticky");
    }
}
