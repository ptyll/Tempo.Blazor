using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Interfaces;
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
        var cut = RenderComponent<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, false)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette").Should().BeEmpty();
    }

    [Fact]
    public void TmCommandPalette_Shows_When_Open()
    {
        var cut = RenderComponent<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette").Should().NotBeEmpty();
    }

    [Fact]
    public void TmCommandPalette_Shows_Actions_List()
    {
        var cut = RenderComponent<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions()));

        cut.FindAll(".tm-command-palette-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmCommandPalette_Filter_Narrows_Results()
    {
        var cut = RenderComponent<TmCommandPalette>(p => p
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
        var cut = RenderComponent<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, new[] { action }));

        cut.Find(".tm-command-palette-item").Click();

        executed.Should().BeTrue();
    }

    [Fact]
    public void TmCommandPalette_Close_Button_Fires_IsOpenChanged()
    {
        var isOpen = true;
        var cut = RenderComponent<TmCommandPalette>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Actions, MakeActions())
            .Add(c => c.IsOpenChanged,
                EventCallback.Factory.Create<bool>(this, v => isOpen = v)));

        cut.Find(".tm-command-palette-close").Click();

        isOpen.Should().BeFalse();
    }
}
