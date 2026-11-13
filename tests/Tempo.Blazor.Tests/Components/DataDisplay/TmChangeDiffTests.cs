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
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.Find(".tm-change-diff").Should().NotBeNull();
    }

    [Fact]
    public void TmChangeDiff_Renders_Row_Per_Change()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-row").Count.Should().Be(2);
    }

    [Fact]
    public void TmChangeDiff_Shows_Property_Name()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-property")[0].TextContent.Should().Contain("Status");
    }

    [Fact]
    public void TmChangeDiff_Shows_Old_Value()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-old")[0].TextContent.Should().Contain("Open");
    }

    [Fact]
    public void TmChangeDiff_Shows_New_Value()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-new")[0].TextContent.Should().Contain("Closed");
    }

    [Fact]
    public void TmChangeDiff_Shows_Arrow_Separator()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-arrow").Count.Should().Be(2);
    }

    [Fact]
    public void TmChangeDiff_Empty_When_No_Changes()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, Array.Empty<TmChangeInfo>()));

        cut.FindAll(".tm-change-diff-row").Count.Should().Be(0);
    }

    [Fact]
    public void TmChangeDiff_Handles_Null_Values()
    {
        var changes = new[] { new TmChangeInfo("Field", null, "New") };
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, changes));

        // KG-008: the visible run lives in its own aria-hidden span (the screen-reader text is
        // the prefixed .tm-sr-only sibling), so the dash fallback is asserted on THAT run.
        cut.Find(".tm-change-diff-old [aria-hidden='true']").TextContent.Trim().Should().Be("—");
        cut.Find(".tm-change-diff-new [aria-hidden='true']").TextContent.Should().Contain("New");
    }

    [Fact]
    public void TmChangeDiff_Applies_Custom_Class()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges())
            .Add(c => c.Class, "my-diff"));

        cut.Find(".tm-change-diff").ClassList.Should().Contain("my-diff");
    }

    // ── KG-008 (plan 92e96b25, phase 11): screen-reader text alternative ──

    [Fact]
    public void TmChangeDiff_OldValue_Has_ScreenReaderPrefix()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        var old = cut.FindAll(".tm-change-diff-old")[0];
        old.QuerySelector(".tm-sr-only")!.TextContent.Should().Contain("Previous value: Open");
    }

    [Fact]
    public void TmChangeDiff_NewValue_Has_ScreenReaderPrefix()
    {
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        var @new = cut.FindAll(".tm-change-diff-new")[0];
        @new.QuerySelector(".tm-sr-only")!.TextContent.Should().Contain("New value: Closed");
    }

    [Fact]
    public void TmChangeDiff_VisibleValues_Are_HiddenFromScreenReaders()
    {
        // The visible run is aria-hidden so a reader hears the prefixed text ONCE
        // ("Previous value: Open → New value: Closed"), not a bare "Open" "Closed" pair.
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, TestChanges()));

        cut.FindAll(".tm-change-diff-old")[0].QuerySelector("[aria-hidden='true']")!
            .TextContent.Should().Contain("Open");
        cut.FindAll(".tm-change-diff-new")[0].QuerySelector("[aria-hidden='true']")!
            .TextContent.Should().Contain("Closed");
    }

    [Fact]
    public void TmChangeDiff_NullOldValue_ScreenReaderPrefix_Uses_TheDashFallback()
    {
        var changes = new[] { new TmChangeInfo("Field", null, "New") };
        var cut = Render<TmChangeDiff>(p => p
            .Add(c => c.Changes, changes));

        cut.Find(".tm-change-diff-old .tm-sr-only")!.TextContent.Should().Contain("Previous value: —");
    }
}
