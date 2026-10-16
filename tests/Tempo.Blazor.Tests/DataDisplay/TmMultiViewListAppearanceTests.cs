using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListAppearanceTests : LocalizationTestBase
{
    [Fact]
    public void Default_UsesCurrentCardFrameClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p.Add(c => c.Items, new List<string>()));

        cut.Find(".tm-multi-view-list").ClassList.Should().NotContain("tm-multi-view-list--plain");
    }

    [Fact]
    public void Plain_DoesNotUseCardFrameClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new List<string>())
            .Add(c => c.Appearance, DataTableAppearance.Plain));

        cut.Find(".tm-multi-view-list").ClassList.Should().Contain("tm-multi-view-list--plain");
    }
}
