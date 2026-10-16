using Bunit;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListResponsiveTests : LocalizationTestBase
{
    [Fact]
    public void ResponsiveDefaultsToFalseAndDoesNotAddTableScrollClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new[] { "item" })
            .Add(c => c.GroupableFields, new[] { new GroupFieldDefinition<string> { FieldName = "Name", Label = "Name" } }));

        cut.Find(".tm-mvl-table").ParentElement!.ClassList.Should().NotContain("tm-mvl-table-scroll");
        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation.Should().Be(PanelPresentation.Popover);
    }

    [Fact]
    public void ResponsiveAddsTableScrollClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new[] { "item" })
            .Add(c => c.GroupableFields, new[] { new GroupFieldDefinition<string> { FieldName = "Name", Label = "Name" } })
            .Add(c => c.Responsive, true));

        cut.Find(".tm-mvl-table").ParentElement!.ClassList.Should().Contain("tm-mvl-table-scroll");
        cut.Find(".tm-mvl-title-column").Should().NotBeNull();
        cut.Find(".tm-mvl-switch--responsive").Should().NotBeNull();
        cut.FindComponent<TmOverlayPanel>().Instance.MobilePresentation.Should().Be(PanelPresentation.Auto);
    }
}
