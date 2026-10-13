using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Actions;

/// <summary>
/// F4: the "More" menu host contract lives in ONE internal component both action surfaces compose
/// (the F5 bar and the F4 toolbar overflow) — never a second copy. These tests drive it directly with
/// a desktop viewport context (popover presentation) and forced sheet.
/// </summary>
public class ActionOverflowMenuTests : LocalizationTestBase
{
    private static TmActionItem Item(
        string id,
        bool disabled = false,
        bool danger = false,
        bool keepOpen = false,
        EventCallback? onClick = null,
        bool? checkState = null,
        bool checkMixed = false)
        => new()
        {
            Id = id, Label = id, Disabled = disabled, Danger = danger, KeepMenuOpen = keepOpen,
            OnClick = onClick ?? default, Checked = checkState, CheckedMixed = checkMixed,
        };

    private IRenderedComponent<ActionOverflowMenu> RenderMenu(
        IReadOnlyList<TmActionItem> items,
        PanelPresentation presentation = PanelPresentation.Popover,
        TmLayoutMode mode = TmLayoutMode.Desktop)
        => Render<ActionOverflowMenu>(p => p
            .Add(c => c.Items, items)
            .Add(c => c.TriggerClass, "test-trigger")
            .Add(c => c.TriggerAriaLabel, "More")
            .Add(c => c.PanelClass, "test-panel")
            .Add(c => c.MenuClass, "test-menu")
            .Add(c => c.ItemClass, "test-item")
            .Add(c => c.LabelClass, "test-label")
            .Add(c => c.CheckClass, "test-check")
            .Add(c => c.GroupClass, "test-group")
            .Add(c => c.GroupLabelClass, "test-group-label")
            .Add(c => c.SeparatorClass, "test-separator")
            .Add(c => c.MobilePresentation, presentation)
            .Add(c => c.InitialMode, mode)
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, mode)));

    [Fact]
    public void Trigger_IsAMenuButton_WithoutControlsWhileClosed()
    {
        var cut = RenderMenu([Item("a"), Item("b")]);

        var trigger = cut.Find("button.test-trigger");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
        trigger.GetAttribute("aria-label").Should().Be("More");
        trigger.HasAttribute("aria-controls").Should().BeFalse("the panel is not rendered while closed");
        trigger.GetAttribute("id").Should().NotBeNullOrEmpty("the sheet restores focus through Anchor.Id");
    }

    [Fact]
    public void Open_RendersARoleMenu_WithMenuItems_AndPointsAriaControlsAtIt()
    {
        var cut = RenderMenu([Item("a"), Item("b")]);

        cut.Find("button.test-trigger").Click();

        var menu = cut.Find("[role='menu']");
        cut.FindAll("[role='menuitem']").Select(i => i.GetAttribute("data-action-id")).Should().Equal("a", "b");
        cut.Find("button.test-trigger").GetAttribute("aria-expanded").Should().Be("true");
        cut.Find("button.test-trigger").GetAttribute("aria-controls").Should().Be(cut.Find(".test-panel").Id);
        cut.Find(".test-panel").Should().NotBeNull();
        menu.Should().NotBeNull();
    }

    [Fact]
    public void AllItemsDisabled_DisablesTheTrigger()
    {
        var cut = RenderMenu([Item("a", disabled: true), Item("b", disabled: true)]);

        cut.Find("button.test-trigger").HasAttribute("disabled").Should().BeTrue();
    }

    [Fact]
    public void DangerItem_CarriesTheDangerModifier()
    {
        var cut = RenderMenu([Item("keep"), Item("drop", danger: true)]);

        cut.Find("button.test-trigger").Click();

        cut.Find("[data-action-id='drop']").ClassList.Should().Contain("test-item--danger");
        cut.Find("[data-action-id='keep']").ClassList.Should().NotContain("test-item--danger");
    }

    [Fact]
    public void SelectingAnItem_ClosesTheMenuBeforeInvokingTheAction()
    {
        var openPanelsDuringAction = -1;
        IRenderedComponent<ActionOverflowMenu>? probe = null;
        var cut = RenderMenu([Item("a", onClick: EventCallback.Factory.Create(this, () =>
        {
            openPanelsDuringAction = probe!.FindAll("[role='menu']").Count;
        }))]);
        probe = cut;

        cut.Find("button.test-trigger").Click();
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => openPanelsDuringAction.Should().Be(0, "the menu closes first, then the action runs"));
        cut.Find("button.test-trigger").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void KeepMenuOpen_LeavesTheMenuOpenAfterTheAction()
    {
        var calls = 0;
        var cut = RenderMenu([Item("a", keepOpen: true, onClick: EventCallback.Factory.Create(this, () => { calls++; }))]);

        cut.Find("button.test-trigger").Click();
        cut.Find("[role='menuitem']").Click();

        calls.Should().Be(1);
        cut.FindAll("[role='menu']").Should().NotBeEmpty();
    }

    [Fact]
    public void Sheet_RendersTheMenuInTheSharedBottomSheet()
    {
        var cut = RenderMenu([Item("a")], PanelPresentation.Sheet, TmLayoutMode.Mobile);

        cut.Find("button.test-trigger").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find("[role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void ItemsThatEmptyWhileOpen_CloseTheMenu()
    {
        var cut = RenderMenu([Item("a")]);
        cut.Find("button.test-trigger").Click();

        cut.Render(p => p.Add(c => c.Items, new List<TmActionItem>()));

        cut.FindAll("[role='menu']").Should().BeEmpty();
        cut.FindAll("button.test-trigger").Should().BeEmpty("with nothing to offer there is no trigger");
    }

    [Fact]
    public void ReorderWhileOpen_AnItemInFrontOfTheFirstEnabledOne_DoesNotCrashTheRenderer()
    {
        // H1 (UX B1): the first enabled item used to carry the only element-reference capture, added
        // conditionally inside a keyed <button>. A reorder while open ([B] -> [A, B]) moved the capture
        // frame to a different retained element and the diff threw "Unexpected frame type during
        // RemoveOldFrame: ElementReferenceCapture" (#blazor-error-ui, dead page) - reachable by a resize
        // or rotation with the menu open.
        var cut = RenderMenu([Item("B")]);
        cut.Find("button.test-trigger").Click();

        var act = () => cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Item("A"), Item("B") }));

        act.Should().NotThrow();
        cut.FindAll("[role='menuitem']").Select(i => i.GetAttribute("data-action-id")).Should().Equal("A", "B");

        // and back, and with a disabled item taking the front: the capture never depends on position.
        cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Item("Off", disabled: true), Item("A"), Item("B") }));
        cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Item("B") }));
        cut.FindAll("[role='menuitem']").Select(i => i.GetAttribute("data-action-id")).Should().Equal("B");
    }

    [Fact]
    public void ReorderWhileOpen_ADisabledItemTakingTheFront_DoesNotCrashTheRenderer()
    {
        // The first-enabled item moves when a disabled one is inserted in front of it - the focus target
        // is picked at focus time, so the render tree never depends on which item is first.
        var cut = RenderMenu([Item("B")]);
        cut.Find("button.test-trigger").Click();

        var act = () => cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Item("Off", disabled: true), Item("A"), Item("B") }));

        act.Should().NotThrow();
        cut.FindAll("[role='menuitem']").Select(i => i.GetAttribute("data-action-id")).Should().Equal("Off", "A", "B");
    }

    // ── C2: checkable items (menuitemcheckbox) ───────────────────────────────────────────────────

    [Theory]
    [InlineData(true, false, "true", "check")]
    [InlineData(false, true, "mixed", "minus")]
    [InlineData(false, false, "false", null)]
    public void CheckedItem_RendersMenuItemCheckbox_WithCheckMarkSlot(bool state, bool mixed, string ariaChecked, string? icon)
    {
        var cut = RenderMenu([Item("grid", checkState: state, checkMixed: mixed)]);
        cut.Find("button.test-trigger").Click();

        var entry = cut.Find("[role='menu'] [role='menuitemcheckbox']");
        entry.GetAttribute("aria-checked").Should().Be(ariaChecked);
        var slot = entry.QuerySelector(".test-check");
        slot.Should().NotBeNull("the check-mark slot paints the state");
        slot!.GetAttribute("aria-hidden").Should().Be("true", "the glyph is decoration; aria-checked carries the state");
        var iconNode = slot.QuerySelector(".tm-icon");
        if (icon is null)
        {
            iconNode.Should().BeNull("an unchecked entry shows an empty slot (alignment), no glyph");
            cut.FindComponents<TmIcon>().Should().BeEmpty("an unchecked entry paints no glyph at all");
        }
        else
        {
            iconNode.Should().NotBeNull();
            // The glyph must be the STATE — Check when on, Minus when mixed — never just any icon.
            cut.FindComponents<TmIcon>().Select(c => c.Instance.Name).Should().ContainSingle().Which.Should().Be(icon);
        }
    }

    [Fact]
    public void UncheckableItem_HasNoCheckSlot()
    {
        var cut = RenderMenu([Item("plain"), Item("grid", checkState: true)]);
        cut.Find("button.test-trigger").Click();

        var plain = cut.Find("[role='menu'] [role='menuitem']");
        plain.HasAttribute("aria-checked").Should().BeFalse();
        plain.QuerySelector(".test-check").Should().BeNull();
        cut.FindAll("[role='menuitemcheckbox']").Should().ContainSingle("only the checked item changed role");
    }

    [Fact]
    public void ReorderWhileOpen_TogglingCheckedOnTheFirstItem_DoesNotCrashTheRenderer()
    {
        // H1 invariant under C2: the item frame sequence stays loop-invariant and the element-reference
        // capture stays on EVERY item (offset seq+20, last) — so a Checked flip (check-mark slot appears)
        // or a reorder while open never moves a capture between retained elements.
        var cut = RenderMenu([Item("grid", checkState: true)]);
        cut.Find("button.test-trigger").Click();
        cut.Find("[role='menuitemcheckbox']").Should().NotBeNull();

        var act = () => cut.Render(p => p.Add(c => c.Items, new List<TmActionItem>
        {
            Item("plain"),
            Item("grid", checkState: false, checkMixed: true),
            Item("off", disabled: true, checkState: false),
        }));

        act.Should().NotThrow();
        cut.FindAll("[role='menuitemcheckbox']").Select(i => i.GetAttribute("data-action-id"))
            .Should().Equal("grid", "off");
        cut.Find("[data-action-id='grid']").GetAttribute("aria-checked").Should().Be("mixed");
    }

    [Fact]
    public void Sheet_FirstEntryIsCheckbox_IsTheInitialFocusTarget()
    {
        // The sheet's initial-focus selector listed only menuitem/option, so a sheet whose first entry
        // is a checkable item focused the wrong element (the header Done fallback). The selector must
        // name menuitemcheckbox (and menuitemradio) too.
        var cut = RenderMenu([Item("grid", checkState: true), Item("a")], PanelPresentation.Sheet, TmLayoutMode.Mobile);
        cut.Find("button.test-trigger").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        var drawer = cut.FindComponent<Tempo.Blazor.Components.Layout.TmDrawer>();
        drawer.Instance.InitialFocusSelector.Should().Contain("menuitemcheckbox");
    }

    // ── C3: grouped runs (role=group sections with headers, role=separator between runs) ─────────

    private static TmActionItem Grouped(string id, string group, string? label = null, bool disabled = false)
        => new() { Id = id, Label = id, Group = group, GroupLabel = label, Disabled = disabled };

    private static IReadOnlyList<string> GroupedItemIds(AngleSharp.Dom.IElement group)
        => group.QuerySelectorAll("[role='menuitem'], [role='menuitemcheckbox']")
            .Select(i => i.GetAttribute("data-action-id")!)
            .ToList();

    [Fact]
    public void GroupedItems_RenderRoleGroup_NamedByLabel_WithAriaHiddenHeader()
    {
        var cut = RenderMenu([Grouped("undo", "edit", "Edit"), Grouped("redo", "edit", "Edit"), Item("plain")]);
        cut.Find("button.test-trigger").Click();

        var list = cut.Find(".test-menu");
        var groups = list.QuerySelectorAll("[role='group']");
        groups.Length.Should().Be(1, "the two consecutive edit items form one run");
        var group = groups[0];
        group.GetAttribute("aria-label").Should().Be("Edit", "the group's accessible name is its label");
        group.ClassList.Should().Contain("test-group");

        var header = group.QuerySelector(".test-group-label");
        header.Should().NotBeNull("the run paints a visible header");
        header!.GetAttribute("aria-hidden").Should().Be("true", "the header is decoration: the group's aria-label carries the name");
        header.TextContent.Should().Be("Edit");
        header.HasAttribute("tabindex").Should().BeFalse("a heading inside a menu is never a focusable item");
        GroupedItemIds(group).Should().Equal(new[] { "undo", "redo" }, "the run's items render inside the group, in Items order");

        // The ungrouped item is a direct child of the menu list, outside the group.
        list.Children.Where(child => child.GetAttribute("role") == "menuitem")
            .Select(i => i.GetAttribute("data-action-id")).Should().Equal("plain");
    }

    [Fact]
    public void NonContiguousRunsOfOneGroup_RenderTwoSections_InOrder_NoDuplicateKeyCrash()
    {
        var cut = RenderMenu([Grouped("a", "g1", "G1"), Item("b"), Grouped("c", "g1", "G1")]);
        cut.Find("button.test-trigger").Click();

        cut.Find(".test-menu").Children.Select(child => child.GetAttribute("role"))
            .Should().Equal("group", "separator", "menuitem", "separator", "group");

        // Re-render while open with the order swapped: two sibling sections of the same group
        // must still get distinct keys (keyed by the run's first item, not just the group).
        var act = () => cut.Render(p => p.Add(c => c.Items, new List<TmActionItem>
        {
            Grouped("c", "g1", "G1"), Item("b"), Grouped("a", "g1", "G1"),
        }));
        act.Should().NotThrow();
        cut.Find(".test-menu").Children.Select(child => child.GetAttribute("role"))
            .Should().Equal("group", "separator", "menuitem", "separator", "group");
    }

    [Fact]
    public void RunsWhoseFirstItemsShareALabelWithoutId_DoNotCollideOnSeparatorKeys()
    {
        var first = new TmActionItem { Label = "Same", Group = "g1", GroupLabel = "G1" };
        var plain = new TmActionItem { Label = "Same" };
        var second = new TmActionItem { Label = "Same", Group = "g2", GroupLabel = "G2" };

        var act = () =>
        {
            var cut = RenderMenu([first, plain, second]);
            cut.Find("button.test-trigger").Click();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void Runs_AreSeparatedByRoleSeparator_NoLeadingOrTrailingSeparator()
    {
        var cut = RenderMenu([
            Item("a"),
            Grouped("undo", "edit", "Edit"), Grouped("redo", "edit", "Edit"),
            Item("b"),
            Grouped("grid", "view", "View"),
        ]);
        cut.Find("button.test-trigger").Click();

        var list = cut.Find(".test-menu");
        var separators = list.QuerySelectorAll("[role='separator']");
        separators.Length.Should().Be(3, "one divider between each pair of the four runs, never more");
        list.FirstElementChild!.GetAttribute("role").Should().NotBe("separator", "no leading separator");
        list.LastElementChild!.GetAttribute("role").Should().NotBe("separator", "no trailing separator");
        separators[0].GetAttribute("class").Should().Be("test-separator");
        separators[0].TagName.Should().Be("DIV", "a plain divider element, not a focusable control");
        separators[0].HasAttribute("tabindex").Should().BeFalse("a separator is never a menu item");

        // The separators really sit BETWEEN the runs in DOM order.
        var roles = list.Children.Select(child => child.GetAttribute("role")).ToList();
        roles.Should().Equal(new[] { "menuitem", "separator", "group", "separator", "menuitem", "separator", "group" });
    }

    [Fact]
    public void UnlabelledRun_RendersNoGroupWrapper()
    {
        // A run with a key but no GroupLabel (the first item carries none) has no name to show: it
        // renders its items flat (as siblings), but it still counts as a run for the separators.
        var cut = RenderMenu([Grouped("a", "k"), Grouped("b", "k", "Ignored"), Item("c")]);
        cut.Find("button.test-trigger").Click();

        var list = cut.Find(".test-menu");
        list.QuerySelectorAll("[role='group']").Should().BeEmpty("an unnamed run renders no role=group");
        list.Children.Where(child => child.GetAttribute("role") == "menuitem")
            .Select(i => i.GetAttribute("data-action-id")).Should().Equal(new[] { "a", "b", "c" },
                "the unnamed run renders a and b flat; c is its own ungrouped run");
        list.QuerySelectorAll("[role='separator']").Should().ContainSingle("the unnamed run still separates it from c");
    }

    [Fact]
    public void ReorderWhileOpen_ItemMovesAcrossGroups_DoesNotCrashTheRenderer()
    {
        // H1 invariant under C3: an item that moves INTO or OUT of a group while the menu is open
        // (a resize, a rotation) changes the run structure around it. The item's own frame set and
        // its element-reference capture are identical in both shapes, and the group/separator
        // elements carry key types that never collide with an item id — so the diff matches.
        var cut = RenderMenu([Grouped("A", "g1", "G1")]);
        cut.Find("button.test-trigger").Click();
        cut.Find(".test-menu [role='group']").Should().NotBeNull();

        var act = () => cut.Render(p => p.Add(c => c.Items, new List<TmActionItem>
        {
            Item("B"),
            Grouped("A", "g2", "G2"),
            Grouped("C", "g2", "G2"),
        }));

        act.Should().NotThrow();
        var group = cut.Find(".test-menu [role='group']");
        group.GetAttribute("aria-label").Should().Be("G2");
        GroupedItemIds(group).Should().Equal("A", "C");
        cut.Find(".test-menu").Children.Where(child => child.GetAttribute("role") == "menuitem")
            .Select(i => i.GetAttribute("data-action-id")).Should().Equal("B");

        // and back to the single grouped item, plus a disabled item taking the front of the menu.
        cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Grouped("Off", "g0", "G0", disabled: true), Grouped("A", "g1", "G1") }));
        cut.FindAll(".test-menu [role='group']").Should().HaveCount(2);
        cut.Render(p => p.Add(c => c.Items, new List<TmActionItem> { Grouped("A", "g1", "G1") }));
        cut.FindAll(".test-menu [role='group']").Should().HaveCount(1);
    }

    [Fact]
    public void GroupedDisabledItem_KeepsItsState_AndTheFirstEnabledItemIsStillTheFocusTarget()
    {
        var cut = RenderMenu([Grouped("off", "edit", "Edit", disabled: true), Grouped("undo", "edit", "Edit"), Item("plain")],
            PanelPresentation.Sheet, TmLayoutMode.Mobile);
        cut.Find("button.test-trigger").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        var group = cut.Find(".test-menu [role='group']");
        GroupedItemIds(group).Should().Equal("off", "undo");
        group.QuerySelector("[data-action-id='off']")!.GetAttribute("disabled").Should().NotBeNull();

        // The pending initial focus ran exactly once (the sheet fires OnOpened without JS in bUnit):
        // only menu ITEMS carry capture frames, so a header or a separator could never be the target.
        cut.WaitForAssertion(() => JSInterop.Invocations
            .Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(1));
    }
}