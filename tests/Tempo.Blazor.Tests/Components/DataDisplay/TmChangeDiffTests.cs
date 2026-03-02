using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.DataDisplay;

/// <summary>TDD tests for TmChangeDiff.</summary>
public class TmChangeDiffTests : LocalizationTestBase
{
    private static TmChangeInfo[] TestChanges() =>
    [
        new("Status", "Open", "Closed"),
        new("Priority", "Low", "High"),
    ];

    [Fact]
    public void TmChangeDiff_Renders_With_Base_Class()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.Find(".tm-change-diff").Should().NotBeNull();
    }

    [Fact]
    public void TmChangeDiff_Renders_Row_Per_Change()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-row").Count.Should().Be(2);
    }

    [Fact]
    public void TmChangeDiff_Shows_Property_Name()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-property")[0].TextContent.Should().Contain("Status");
    }

    [Fact]
    public void TmChangeDiff_Shows_Old_Value()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-old")[0].TextContent.Should().Contain("Open");
    }

    [Fact]
    public void TmChangeDiff_Shows_New_Value()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-new")[0].TextContent.Should().Contain("Closed");
    }

    [Fact]
    public void TmChangeDiff_Shows_Arrow_Separator()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-arrow").Count.Should().Be(2);
    }

    [Fact]
    public void TmChangeDiff_Empty_When_No_Changes()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, Array.Empty<TmChangeInfo>()));

        cut.FindAll(".tm-change-diff-row").Count.Should().Be(0);
    }

    [Fact]
    public void TmChangeDiff_Handles_Null_Values()
    {
        var changes = new[] { new TmChangeInfo("Field", null, "New") };
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, changes));

        cut.Find(".tm-change-diff-old").TextContent.Trim().Should().Be("—");
        cut.Find(".tm-change-diff-new").TextContent.Should().Contain("New");
    }

    [Fact]
    public void TmChangeDiff_Applies_Custom_Class()
    {
        var cut = RenderComponent<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges())
            .Add(c => c.Class, "my-diff"));

        cut.Find(".tm-change-diff").ClassList.Should().Contain("my-diff");
    }
}
