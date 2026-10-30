using System.Linq;
using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Spreadsheet;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.Components.Spreadsheet;

/// <summary>
/// The <c>data-testid</c> contract of <see cref="TmSpreadsheet"/> (redesign phase 8 review): the component
/// derives from the core <c>TmComponentBase</c>, but before this test its root carried no
/// <c>data-testid</c> at all — a host could not label the grid, and the internal header ids ignored
/// <c>TestIdPrefix</c>, so two spreadsheets on one page produced duplicate, ambiguous ids.
/// </summary>
public class TmSpreadsheetTestIdTests : LocalizationTestBase
{
    [Fact]
    public void DataTestId_ReachesTheRoot_AndLeavesTheInternalIdsBare()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.DataTestId, "host-sheet")
            .Add(c => c.ShowHeader, true));

        cut.Find(".tm-spreadsheet").GetAttribute("data-testid")
            .Should().Be("host-sheet", "DataTestId is the host's id for the component root");

        cut.FindAll("[data-testid='tm-spreadsheet-header']").Should().ContainSingle(
            "a host that sets only DataTestId keeps the bare internal ids");
    }

    [Fact]
    public void TestIdPrefix_PrefixesTheInternalHeaderIds_AndLeavesTheRootUntitled()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.TestIdPrefix, "sheet-a")
            .Add(c => c.ShowHeader, true));

        cut.FindAll("[data-testid='sheet-a-tm-spreadsheet-header']").Should().ContainSingle(
            "TestIdPrefix namespaces the document header id so two instances never collide");
        cut.FindAll("[data-testid='tm-spreadsheet-header']").Should().BeEmpty(
            "the bare id must not survive next to the prefixed one");

        // No default root id: the root stays untitled until the host sets DataTestId, so adding the
        // convention changes no existing DOM.
        cut.Find(".tm-spreadsheet").HasAttribute("data-testid")
            .Should().BeFalse("TestIdPrefix alone does not invent a root id");
    }

    [Fact]
    public void DataTestId_AndTestIdPrefix_Together_ServeTheRootAndTheInternalIds()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.DataTestId, "sheet-b")
            .Add(c => c.TestIdPrefix, "sheet-b")
            .Add(c => c.ShowHeader, true));

        cut.Find(".tm-spreadsheet").GetAttribute("data-testid").Should().Be("sheet-b");
        cut.FindAll("[data-testid='sheet-b-tm-spreadsheet-header']").Should().ContainSingle();
    }

    [Fact]
    public void MobileHeader_IsPrefixedToo()
    {
        var cut = Render<TmSpreadsheet>(p => p
            .Add(c => c.LayoutMode, Tempo.Blazor.Abstractions.Layout.TmLayoutMode.Mobile)
            .Add(c => c.Title, "Budget")
            .Add(c => c.TestIdPrefix, "phone"));

        cut.FindAll("[data-testid='phone-tm-spreadsheet-mobile-header']").Should().ContainSingle(
            "the phone compact header follows the same prefix contract (it renders only with host content)");
    }
}
