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

        var close = cut.Find(".tm-command-palette-close");
        // Real browser sequence for Enter on a focused <button>:
        // keydown -> native click on the same element.
        close.KeyDown(new KeyboardEventArgs { Key = "Enter" });
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

        cut.Find(".tm-command-palette").KeyDown(new KeyboardEventArgs { Key = "Escape" });

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
