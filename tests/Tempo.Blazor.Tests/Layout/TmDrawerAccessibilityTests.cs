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
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Test Drawer")
            .AddChildContent("Body content"));

        var dialog = cut.Find("div[role='dialog']");
        var labelledBy = dialog.GetAttribute("aria-labelledby");
        labelledBy.Should().StartWith("tm-drawer-title",
            "the drawer names itself from its title, with a unique suffix so two open drawers do not share an id");

        var heading = cut.Find($"h2#{labelledBy}");
        heading.Should().NotBeNull();
        heading.TextContent.Should().Contain("Test Drawer");
    }

    [Fact]
    public void Drawer_NoAriaLabelledBy_WhenHeaderContentUsed()
    {
        var cut = Render<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.HeaderContent, (RenderFragment)(b =>
                b.AddMarkupContent(0, "<span>Custom Header</span>")))
            .AddChildContent("Body content"));

        var dialog = cut.Find("div[role='dialog']");
        dialog.HasAttribute("aria-labelledby").Should().BeFalse();
    }
}
