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

        var groups = cut.FindAll(".tm-command-palette-list > [role='group']");
        groups.Should().HaveCount(2);
        foreach (var group in groups)
        {
            var label = group.QuerySelector(".tm-command-palette-group-label");
            label.Should().NotBeNull();
            group.GetAttribute("aria-labelledby").Should().Be(label!.GetAttribute("id"));
            group.QuerySelector(":scope > [role='none']").Should().NotBeNull();
        }

        groups[0].QuerySelectorAll("[role='option']").Should().HaveCount(2);
        groups[1].QuerySelectorAll("[role='option']").Should().HaveCount(1);
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
}
