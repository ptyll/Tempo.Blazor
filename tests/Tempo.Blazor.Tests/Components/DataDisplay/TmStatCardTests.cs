using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>TDD tests for TmStatCard.</summary>
public class TmStatCardTests : LocalizationTestBase
{
    [Fact]
    public void TmStatCard_Has_Base_CssClass()
    {
        var cut = RenderComponent<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-card").Should().NotBeNull();
    }

    [Fact]
    public void TmStatCard_Renders_Value()
    {
        var cut = RenderComponent<TmStatCard>(p => p
            .Add(c => c.Title, "Users")
            .Add(c => c.Value, "1,234"));

        cut.Find(".tm-stat-value").TextContent.Should().Contain("1,234");
    }

    [Fact]
    public void TmStatCard_Renders_Title()
    {
        var cut = RenderComponent<TmStatCard>(p => p
            .Add(c => c.Title, "Active users")
            .Add(c => c.Value, "42"));

        cut.Find(".tm-stat-label").TextContent.Should().Contain("Active users");
    }

    [Fact]
    public void TmStatCard_Renders_SubValue_When_Set()
    {
        var cut = RenderComponent<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000")
            .Add(c => c.SubValue, "+12% this month"));

        cut.Find(".tm-stat-subvalue").TextContent.Should().Contain("+12% this month");
    }

    [Fact]
    public void TmStatCard_No_SubValue_When_Null()
    {
        var cut = RenderComponent<TmStatCard>(p => p
            .Add(c => c.Title, "Revenue")
            .Add(c => c.Value, "$5,000"));

        cut.FindAll(".tm-stat-subvalue").Should().BeEmpty();
    }
}
