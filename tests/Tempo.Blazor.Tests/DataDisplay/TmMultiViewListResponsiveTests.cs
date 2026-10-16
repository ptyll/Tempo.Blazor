using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListResponsiveTests : LocalizationTestBase
{
    [Fact]
    public void ResponsiveDefaultsToFalseAndDoesNotAddTableScrollClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p.Add(c => c.Items, new[] { "item" }));

        cut.Find(".tm-mvl-table").ParentElement!.ClassList.Should().NotContain("tm-mvl-table-scroll");
    }

    [Fact]
    public void ResponsiveAddsTableScrollClass()
    {
        var cut = Render<TmMultiViewList<string>>(p => p
            .Add(c => c.Items, new[] { "item" })
            .Add(c => c.Responsive, true));

        cut.Find(".tm-mvl-table").ParentElement!.ClassList.Should().Contain("tm-mvl-table-scroll");
    }
}
