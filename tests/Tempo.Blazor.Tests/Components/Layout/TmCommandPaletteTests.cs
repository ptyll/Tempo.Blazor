using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

file record TestAction(
    string Id,
    string Title,
    string? Description,
    string? Icon = null,
    string? Shortcut = null,
    string? Category = null,
    Func<Task>? Execute = null) : ICommandPaletteAction
{
    Func<Task> ICommandPaletteAction.Execute => Execute ?? (() => Task.CompletedTask);
}

/// <summary>TDD tests for TmCommandPalette.</summary>
public class TmCommandPaletteTests : LocalizationTestBase
{
    private static ICommandPaletteAction[] MakeActions() =>
    [
        new TestAction("a1", "New File",      "Create a new file"),
        new TestAction("a2", "Open Project",  "Open an existing project"),
        new TestAction("a3", "New Folder",    "Create a new folder"),
    ];

    [Fact]
    public void TmCommandPalette_Hidden_When_Closed()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, false)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette").Should().BeEmpty();
    }

    [Fact]
    public void TmCommandPalette_Shows_When_Open()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette").Should().NotBeEmpty();
    }

    [Fact]
    public void TmCommandPalette_Shows_Actions_List()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmCommandPalette_Filter_Narrows_Results()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").Input("New");

        cut.FindAll(".tm-command-palette-item").Count.Should().Be(2);
    }

    [Fact]
    public void TmCommandPalette_Click_Action_Executes()
    {
        var executed = false;
        var action = new TestAction("a1", "Run Me", null,
            Execute: () => { executed = true; return Task.CompletedTask; });
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[] { action }));

        cut.Find(".tm-command-palette-item").Click();

        executed.Should().BeTrue();
    }

    [Fact]
    public void TmCommandPalette_Close_Button_Fires_IsOpenChanged()
    {
        var isOpen = true;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-close").Click();

        isOpen.Should().BeFalse();
    }

    [Fact]
    public void TmCommandPalette_Placeholder_Is_Localized()
    {
        var expected = Services.GetRequiredService<ITmLocalizer>()["TmCommandPalette_Placeholder"];

        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").GetAttribute("placeholder").Should().Be(expected);
    }

    [Fact]
    public void TmCommandPalette_Shows_Empty_State_When_No_Match()
    {
        var expected = Services.GetRequiredService<ITmLocalizer>()["TmCommandPalette_NoResults"];

        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").Input("no-such-command");

        cut.FindAll(".tm-command-palette-item").Should().BeEmpty();
        cut.Find(".tm-command-palette-empty").TextContent.Trim().Should().Be(expected);
    }

    [Fact]
    public void TmCommandPalette_ArrowDown_Moves_Highlight()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        // First item is highlighted on open.
        cut.FindAll(".tm-command-palette-item")[0].ClassList.Should().Contain("tm-command-palette-item-focused");

        cut.Find(".tm-command-palette-input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        var items = cut.FindAll(".tm-command-palette-item");
        items[0].ClassList.Should().NotContain("tm-command-palette-item-focused");
        items[1].ClassList.Should().Contain("tm-command-palette-item-focused");
    }

    [Fact]
    public void TmCommandPalette_ArrowUp_From_Top_Wraps_To_Bottom()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").KeyDown(new KeyboardEventArgs { Key = "ArrowUp" });

        var items = cut.FindAll(".tm-command-palette-item");
        items[^1].ClassList.Should().Contain("tm-command-palette-item-focused");
    }

    [Fact]
    public void TmCommandPalette_Enter_Executes_Highlighted_Action()
    {
        var executed = string.Empty;
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "First",  null, Execute: () => { executed = "First";  return Task.CompletedTask; }),
            new TestAction("a2", "Second", null, Execute: () => { executed = "Second"; return Task.CompletedTask; }),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions));

        cut.Find(".tm-command-palette-input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });
        cut.Find(".tm-command-palette-input").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        executed.Should().Be("Second");
    }

    [Fact]
    public void TmCommandPalette_Default_RendersAFlatList_AndOneShortcutKbd()
    {
        // Frozen default: categories do not insert separators, and a shortcut stays one kbd.
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Shortcut: "Ctrl+N", Category: "File"),
            new TestAction("a2", "Open", null, Shortcut: "Ctrl+O", Category: "File"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions));

        cut.FindAll(".tm-command-palette-separator").Should().BeEmpty();
        cut.FindAll(".tm-command-palette-group").Should().BeEmpty();
        cut.FindAll("kbd").Count.Should().Be(2);
        cut.Find("kbd").TextContent.Trim().Should().Be("Ctrl+N");
    }

    [Fact]
    public void TmCommandPalette_Default_ListboxIsNamed()
    {
        // Documented default-markup change (CHANGELOG Changed): the result listbox carries the
        // localized TmCommandPalette_Aria name.
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new ICommandPaletteAction[] { new TestAction("a1", "New File", null) }));

        cut.Find("ul.tm-command-palette-list[role=listbox]")
            .GetAttribute("aria-label").Should().Be(Services.GetRequiredService<ITmLocalizer>()["TmCommandPalette_Aria"]);
    }

    [Fact]
    public void TmCommandPalette_GroupByCategory_InsertsASeparatorBetweenGroups()
    {
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Category: "File"),
            new TestAction("a2", "Open", null, Category: "File"),
            new TestAction("a3", "Undo", null, Category: "Edit"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.GroupByCategory, true)
            .Add(c => c.Actions, actions));

        // The divider is a border between groups, not a separator element a listbox may not own.
        cut.FindAll(".tm-command-palette-separator").Should().BeEmpty();
        cut.FindAll(".tm-command-palette-group-label").Select(l => l.TextContent.Trim())
            .Should().Equal("File", "Edit");
        // The separator is not an option, so arrow navigation still walks the actions only.
        cut.FindAll("[role='option']").Count.Should().Be(3);
    }

    [Fact]
    public void TmCommandPalette_GroupByCategory_ExposesLabelledGroups()
    {
        // L5: a listbox must not contain role=presentation, role=separator or an unexposed label.
        // Each category is a role=group named by its label, holding a role=none list of options.
        // `group` is not an allowed role for `li` (ec0299b9 #1), so the group is a DIV inside a
        // role=none list item.
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Category: "File"),
            new TestAction("a2", "Open", null, Category: "File"),
            new TestAction("a3", "Undo", null, Category: "Edit"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.GroupByCategory, true)
            .Add(c => c.Actions, actions));

        cut.FindAll("[role='separator']").Should().BeEmpty();
        cut.FindAll("[role='presentation']").Should().BeEmpty();

        var groups = cut.FindAll(".tm-command-palette-list > li > [role='group']");
        groups.Should().HaveCount(2);
        foreach (var group in groups)
        {
            group.LocalName.Should().Be("div", "group is not an allowed role for li (ec0299b9 #1)");
            group.ParentElement!.GetAttribute("role").Should().Be("none",
                "the list item hosting a group is presentational");
            group.ParentElement!.ClassList.Should().Contain("tm-command-palette-group-item");
            var label = group.QuerySelector(".tm-command-palette-group-label");
            label.Should().NotBeNull();
            group.GetAttribute("aria-labelledby").Should().Be(label!.GetAttribute("id"));
            group.QuerySelector(":scope > [role='none']").Should().NotBeNull();
        }

        groups[0].QuerySelectorAll("[role='option']").Should().HaveCount(2);
        groups[1].QuerySelectorAll("[role='option']").Should().HaveCount(1);
    }

    [Fact]
    public void TmCommandPalette_GroupByCategory_GroupIsADivInsideANoneLi()
    {
        // ec0299b9 #1: `li[role=group]` trips axe aria-allowed-role. The group markup must be
        // li[role=none] > div[role=group][aria-labelledby] > ul[role=none] > options.
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Category: "File"),
            new TestAction("a2", "Undo", null, Category: "Edit"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.GroupByCategory, true)
            .Add(c => c.Actions, actions));

        var list = cut.Find("ul.tm-command-palette-list[role='listbox']");
        list.Children.Should().NotBeEmpty();
        foreach (var li in list.Children)
        {
            li.GetAttribute("role").Should().Be("none", "every listbox child is an option or a presentational row");
            li.QuerySelector(":scope > div[role='group']").Should().NotBeNull("the named group is a div inside the none row");
        }

        cut.FindAll("li[role='group']").Should().BeEmpty("group is not an allowed role for li");
    }

    [Fact]
    public void TmCommandPalette_GroupByCategory_UnlabeledGroup_RendersNoGroupWrapper()
    {
        // A role=group must be named; a null/empty Category would render a group with no label —
        // exactly the "unnamed group" defect. The category-less cluster's options therefore skip
        // the group wrapper and sit directly in the presentational row.
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Category: "File"),
            new TestAction("a2", "Uncategorized", null, Category: null),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.GroupByCategory, true)
            .Add(c => c.Actions, actions));

        var groups = cut.FindAll("[role='group']");
        groups.Should().ContainSingle("only the named category renders a group wrapper");
        groups.Should().OnlyContain(g => !string.IsNullOrEmpty(g.GetAttribute("aria-labelledby")),
            "every rendered group must be named");

        var list = cut.Find("ul.tm-command-palette-list[role='listbox']");
        list.Children.Should().HaveCount(2, "one row per category cluster");
        var unlabeledRow = list.Children[1];
        unlabeledRow.GetAttribute("role").Should().Be("none", "the row stays presentational");
        unlabeledRow.QuerySelector("[role='group']").Should().BeNull("the unlabeled cluster gets no group wrapper");
        unlabeledRow.QuerySelectorAll("[role='option']").Should().ContainSingle("its option still renders");
    }

    // ── Combobox wiring (cluster-B §12, f4057a47 + ec0299b9 #2) ───────────────

    [Fact]
    public void TmCommandPalette_Input_IsACombobox_ControllingTheListbox()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        var input = cut.Find(".tm-command-palette-input");
        input.GetAttribute("role").Should().Be("combobox",
            "the search input is the combobox that owns the result list");
        input.GetAttribute("aria-autocomplete").Should().Be("list",
            "filtering completes from the list of options");
        input.GetAttribute("aria-expanded").Should().Be("true",
            "results are rendered, so the listbox popup is expanded");

        var list = cut.Find("ul.tm-command-palette-list[role='listbox']");
        input.GetAttribute("aria-controls").Should().Be(list.GetAttribute("id"),
            "aria-controls must name the listbox — this is what takes it out of axe's " +
            "scrollable-region-focusable matcher (ec0299b9 #2)");
    }

    [Fact]
    public void TmCommandPalette_ActiveDescendant_FollowsArrowDown()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        var input = cut.Find(".tm-command-palette-input");
        var options = cut.FindAll("[role='option']");
        var ids = options.Select(o => o.GetAttribute("id")).ToList();
        ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id),
            "every option needs a stable id for aria-activedescendant to resolve");
        ids.Should().OnlyHaveUniqueItems("aria-activedescendant resolves by id");
        input.GetAttribute("aria-activedescendant").Should().Be(ids[0],
            "the highlight starts on the first option");

        cut.Find(".tm-command-palette-input").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        input.GetAttribute("aria-activedescendant").Should().Be(ids[1],
            "ArrowDown moved the highlight, so the combobox must move with it");
    }

    [Fact]
    public void TmCommandPalette_NoResults_HasNoActiveDescendant()
    {
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").Input("no-such-command");

        cut.Find(".tm-command-palette-input").GetAttribute("aria-activedescendant")
            .Should().BeNull("with no options there is no active descendant to point at");
    }

    [Fact]
    public void TmCommandPalette_NoResults_DropsAriaControls()
    {
        // The listbox renders only while there are results; with none, aria-controls would name an
        // id that does not exist in the document, so the combobox drops the attribute with it.
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.Find(".tm-command-palette-input").GetAttribute("aria-controls").Should().NotBeNullOrEmpty(
            "results are rendered, so the combobox names its listbox");

        cut.Find(".tm-command-palette-input").Input("no-such-command");

        cut.Find(".tm-command-palette-input").GetAttribute("aria-controls")
            .Should().BeNull("the listbox is gone, so a dangling aria-controls would be worse than none");
    }

    // ── Backdrop click closes (cluster-B §4, c51ddf81 #5a + phase-7 press guard) ──

    [Fact]
    public void TmCommandPalette_ClickInsidePanel_DoesNotClose()
    {
        var isOpen = true;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        // The backdrop owns a close handler (BackdropClick_Closes proves it fires). The element
        // fence around the panel stops clicks before they reach it — in the browser that is the
        // wrapper's @onclick:stopPropagation; bUnit does not simulate propagation at all, so the
        // observable here is that the clicked panel element carries no close handler of its own
        // and the palette state never changes. The fence itself is browser-proven by the E2E
        // Palette_BackdropClick_Closes_AndPanelClickDoesNot (input click and drag-select).
        var act = () => cut.Find(".tm-command-palette-header").Click();
        act.Should().Throw<Bunit.MissingEventHandlerException>(
            "bUnit delivers a click only to a handler on the element chain — the header owns none, " +
            "so the click cannot have reached the backdrop's close handler");

        isOpen.Should().BeTrue(
            "a click inside the panel (here: the header, not a command) must not dismiss the palette");
    }

    [Fact]
    public void TmCommandPalette_BackdropClick_Closes()
    {
        // A real browser click on the backdrop always STARTS with a pointerdown there; the flag it
        // arms is what lets HandleBackdropClickAsync treat this as an outside click.
        var isOpen = true;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-backdrop").TriggerEvent("onpointerdown", new PointerEventArgs());
        cut.Find(".tm-command-palette-backdrop").Click();

        isOpen.Should().BeFalse("a press that starts and ends on the backdrop is an outside click " +
            "and must dismiss the palette");
    }

    [Fact]
    public void TmCommandPalette_BackdropClickWithoutAPressOnTheBackdrop_DoesNotClose()
    {
        // A drag-select that starts on the input and ends over the backdrop delivers only the CLICK
        // there (the common ancestor of press and release) — the backdrop's pointerdown never ran,
        // so the press flag was never armed and the palette must stay open. bUnit cannot replay the
        // pointer sequence itself: its dispatcher bubbles a panel pointerdown straight into the
        // backdrop's arming handler (it does not honor the splatted :stopPropagation directives the
        // real renderer applies), so the bare click IS this scenario in bUnit. The real drag is
        // proven in a browser by Palette_BackdropClick_Closes_AndPanelClickDoesNot.
        var isOpen = true;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-backdrop").Click();

        isOpen.Should().BeTrue("the press started on the panel, so the click on the backdrop is the " +
            "end of a drag-select and must not dismiss the palette");
    }

    [Fact]
    public void TmCommandPalette_SplitShortcuts_RendersOneKbdPerKey()
    {
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "New File", null, Shortcut: "Ctrl+N"),
            new TestAction("a2", "Help", null, Shortcut: "?"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.SplitShortcuts, true)
            .Add(c => c.Actions, actions));

        var groups = cut.FindAll(".tm-command-palette-shortcut");
        groups[0].QuerySelectorAll("kbd").Select(k => k.TextContent.Trim()).Should().Equal("Ctrl", "N");
        groups[1].QuerySelectorAll("kbd").Select(k => k.TextContent.Trim()).Should().Equal("?");
    }

    [Fact]
    public void TmCommandPalette_SplitShortcuts_KeepsATrailingPlus()
    {
        // L9: "Ctrl++" is Ctrl and the + key. Splitting on '+' and discarding empty parts drops the
        // key itself, so a trailing separator must survive as its own chip.
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "Zoom in", null, Shortcut: "Ctrl++"),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.SplitShortcuts, true)
            .Add(c => c.Actions, actions));

        cut.FindAll(".tm-command-palette-shortcut kbd").Select(k => k.TextContent.Trim())
            .Should().Equal("Ctrl", "+");
    }

    [Fact]
    public void TmCommandPalette_Enter_On_Close_Button_Closes_Without_Executing()
    {
        // The close control is a native <button>: Enter produces a click on
        // keydown. When the container also ran the highlighted action from its
        // keydown handler, one press executed an action AND closed the palette.
        var executed = 0;
        var isOpen = true;
        var actions = new ICommandPaletteAction[]
        {
            new TestAction("a1", "First", null, Execute: () => { executed++; return Task.CompletedTask; }),
        };
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, actions)
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        // Enter on a native <button> is the button's own click. The palette no longer has a container
        // keydown handler, so there is nothing to also run the highlighted action.
        cut.Find(".tm-command-palette-close").Click();

        executed.Should().Be(0);
        isOpen.Should().BeFalse();
    }

    [Fact]
    public void TmCommandPalette_Escape_Fires_IsOpenChanged()
    {
        var isOpen = true;
        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        // Escape is the focus scope's document listener, not a container keydown: a container handler
        // on top of it closed the palette twice.
        cut.InvokeAsync(() => cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>().Instance
            .HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        isOpen.Should().BeFalse();
    }

    // Reachability zub — disposing the palette MUST tear the focus-trap down. This guards the
    // SectionNav circuit-kill class: a trap left active after the owner is gone leaks a document/element
    // listener and a stale return target. Proven with a companion mutation (removing the
    // `_focusTrap.DisposeAsync()` call in the component's DisposeAsync turns this red).
    [Fact]
    public async Task TmCommandPalette_Dispose_TearsDown_FocusTrap()
    {
        var module = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        module.SetupVoid("activate", _ => true).SetVoidResult();
        module.SetupVoid("deactivate", _ => true).SetVoidResult();

        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        // OnAfterRenderAsync activated the trap during the open render.
        module.VerifyInvoke("activate");

        // Disposing the component must reach FocusTrap.DisposeAsync → deactivate + module dispose.
        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());

        module.VerifyInvoke("deactivate");
    }

    // ── Activate after promote (F3 follow-up, c51ddf81 #1) ────────────────────

    [Fact]
    public void TmCommandPalette_FocusTrap_ActivatesOnlyAfterPromotion()
    {
        var top = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        var promote = top.SetupVoid("promote", _ => true); // no result: the call stays pending
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        activate.Invocations["activate"].Should().BeEmpty(
            "the trap must not activate while the promote interop is still pending — an unpromoted " +
            "popover root can be display:none and the initial-focus move would land on <body>");

        promote.SetVoidResult();
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1, "completing the promote is what releases the trap's activation"));
    }
}
