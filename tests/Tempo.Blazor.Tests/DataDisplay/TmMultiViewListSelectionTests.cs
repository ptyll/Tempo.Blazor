using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

public class TmMultiViewListSelectionTests : LocalizationTestBase
{
    private record SelItem(string Id, string Title) : IMultiViewListItem
    {
        public string?                  SubTitle    => null;
        public string?                  AvatarUrl   => null;
        public IReadOnlyList<ITag>?     Tags        => null;
        public string?                  StatusLabel => null;
        public string?                  StatusColor => null;
        public DateTimeOffset?          Date        => null;
    }

    private static IReadOnlyList<SelItem> Items(int count = 3) =>
        Enumerable.Range(1, count)
            .Select(i => new SelItem(i.ToString(), $"Item {i}"))
            .ToArray();

    // DragDropService must be registered for the component to render
    public TmMultiViewListSelectionTests()
    {
        Services.AddScoped<DragDropService>();
    }

    // ── AllowSelection=false (default) ───────────────────────────

    [Fact]
    public void AllowSelection_False_NoCheckboxes_InTableView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, false));

        cut.FindAll("input[type='checkbox']").Should().BeEmpty();
    }

    [Fact]
    public void AllowSelection_False_NoCheckboxes_InCardView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, false));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll("input[type='checkbox']").Should().BeEmpty();
    }

    [Fact]
    public void AllowSelection_False_NoCheckboxes_InListView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, false));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll("input[type='checkbox']").Should().BeEmpty();
    }

    // ── AllowSelection=true — Table view ─────────────────────────

    [Fact]
    public void AllowSelection_True_ShowsCheckboxes_InTableView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true));

        // Header checkbox + one per row
        cut.FindAll("input[type='checkbox']").Count.Should().BeGreaterThanOrEqualTo(Items().Count + 1);
    }

    [Fact]
    public void AllowSelection_CheckRow_AddsIdToSelectedIds()
    {
        HashSet<string>? captured = null;
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        // First row checkbox (index 1, index 0 is header "select all")
        cut.FindAll("input[type='checkbox']")[1].Change(true);

        captured.Should().NotBeNull();
        captured!.Should().Contain("1");
    }

    [Fact]
    public void AllowSelection_UncheckRow_RemovesIdFromSelectedIds()
    {
        var selectedIds = new HashSet<string> { "1" };
        HashSet<string>? captured = null;

        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, selectedIds)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        // Uncheck first row (already selected)
        cut.FindAll("input[type='checkbox']")[1].Change(false);

        captured.Should().NotBeNull();
        captured!.Should().NotContain("1");
    }

    [Fact]
    public void Selection_Checkbox_Space_Does_Not_Fire_OnItemClick()
    {
        // The row <tr> maps Enter/Space to OnItemClick (it is a non-native
        // focusable, so that emulation is legitimate) — but the checkbox cell
        // isolates its keydowns with @onkeydown:stopPropagation: the browser
        // turns Space on a focused checkbox into a checked flip + change event,
        // and the bubbled keydown must not also fire OnItemClick. bUnit reports
        // the boundary as "no reachable onkeydown handler" and throws.
        SelItem? clicked = null;
        HashSet<string>? captured = null;
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.OnItemClick,
                EventCallback.Factory.Create<SelItem>(this, i => clicked = i))
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        // First row checkbox (index 1, index 0 is header "select all")
        var checkbox = cut.FindAll("input[type='checkbox']")[1];
        var act = () => checkbox.KeyDown(new KeyboardEventArgs { Key = " " });

        act.Should().Throw<MissingEventHandlerException>(
            "the selection cell stops keydowns before they reach the row's Enter/Space handler");

        // The native part of the sequence still toggles the selection.
        cut.FindAll("input[type='checkbox']")[1].Change(true);
        captured.Should().NotBeNull();
        captured!.Should().Contain("1");
        clicked.Should().BeNull();
    }

    [Fact]
    public void AllowSelection_SelectAll_SelectsAllVisibleRows()
    {
        HashSet<string>? captured = null;
        var items = Items(3);
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        // Header checkbox = select all (index 0)
        cut.FindAll("input[type='checkbox']")[0].Change(true);

        captured.Should().NotBeNull();
        captured!.Should().Contain("1").And.Contain("2").And.Contain("3");
    }

    [Fact]
    public void AllowSelection_SelectAll_WhenAllSelected_DeselectsAll()
    {
        var allIds = new HashSet<string> { "1", "2", "3" };
        HashSet<string>? captured = null;

        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items(3))
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, allIds)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        // Click header checkbox again — should deselect all
        cut.FindAll("input[type='checkbox']")[0].Change(false);

        captured.Should().NotBeNull();
        captured!.Should().BeEmpty();
    }

    // ── AllowSelection=true — Card view ──────────────────────────

    [Fact]
    public void AllowSelection_True_ShowsCheckboxes_InCardView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll("input[type='checkbox']").Count.Should().Be(Items().Count);
    }

    [Fact]
    public void AllowSelection_CardCheck_AddsIdToSelectedIds()
    {
        HashSet<string>? captured = null;
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        cut.Find(".tm-mvl-switch-card").Click();
        cut.FindAll("input[type='checkbox']")[0].Change(true);

        captured.Should().NotBeNull();
        captured!.Should().Contain("1");
    }

    // ── AllowSelection=true — List view ──────────────────────────

    [Fact]
    public void AllowSelection_True_ShowsCheckboxes_InListView()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll("input[type='checkbox']").Count.Should().Be(Items().Count);
    }

    [Fact]
    public void AllowSelection_ListCheck_AddsIdToSelectedIds()
    {
        HashSet<string>? captured = null;
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIdsChanged,
                EventCallback.Factory.Create<HashSet<string>>(this, s => captured = s)));

        cut.Find(".tm-mvl-switch-list").Click();
        cut.FindAll("input[type='checkbox']")[0].Change(true);

        captured.Should().NotBeNull();
        captured!.Should().Contain("1");
    }

    // ── Selected CSS classes ──────────────────────────────────────

    [Fact]
    public void AllowSelection_SelectedRow_HasSelectedClass()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, new HashSet<string> { "1" }));

        cut.FindAll("tr.tm-mvl-row--selected").Should().HaveCount(1);
    }

    [Fact]
    public void AllowSelection_SelectedCard_HasSelectedClass()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, new HashSet<string> { "2" }));

        cut.Find(".tm-mvl-switch-card").Click();

        cut.FindAll(".tm-mvl-card--selected").Should().HaveCount(1);
    }

    [Fact]
    public void AllowSelection_SelectedListItem_HasSelectedClass()
    {
        var cut = Render<TmMultiViewList<SelItem>>(p => p
            .Add(c => c.Items, Items())
            .Add(c => c.AllowSelection, true)
            .Add(c => c.SelectedIds, new HashSet<string> { "3" }));

        cut.Find(".tm-mvl-switch-list").Click();

        cut.FindAll(".tm-mvl-list-item--selected").Should().HaveCount(1);
    }
}
