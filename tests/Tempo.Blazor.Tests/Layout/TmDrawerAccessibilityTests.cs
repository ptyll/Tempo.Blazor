using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>Accessibility tests for TmDrawer (aria-labelledby, heading structure).</summary>
public class TmDrawerAccessibilityTests : LocalizationTestBase
{
    [Fact]
    public void Drawer_HasAriaLabelledBy_WhenTitleSet()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Test Drawer")
            .AddChildContent("Body content"));

        var dialog = cut.Find("div[role='dialog']");
        dialog.GetAttribute("aria-labelledby").Should().Be("tm-drawer-title");

        var heading = cut.Find("h2#tm-drawer-title");
        heading.Should().NotBeNull();
        heading.TextContent.Should().Contain("Test Drawer");
    }

    [Fact]
    public void Drawer_NoAriaLabelledBy_WhenHeaderContentUsed()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.HeaderContent, (RenderFragment)(b =>
                b.AddMarkupContent(0, "<span>Custom Header</span>")))
            .AddChildContent("Body content"));

        var dialog = cut.Find("div[role='dialog']");
        dialog.HasAttribute("aria-labelledby").Should().BeFalse();
    }
}
