using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.Layout;

/// <summary>TDD tests for TmKeyboardShortcutsHelp.</summary>
public class TmKeyboardShortcutsHelpTests : LocalizationTestBase
{
    private static TmShortcutCategory[] TestCategories() =>
    [
        new("Navigation", [
            new("Ctrl+N", "Next step"),
            new("Ctrl+P", "Previous step"),
        ]),
        new("Actions", [
            new("Enter", "Submit"),
        ]),
    ];

    [Fact]
    public void TmKeyboardShortcutsHelp_Hidden_When_Not_Visible()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, false));

        cut.FindAll(".tm-keyboard-shortcuts-overlay").Count.Should().Be(0);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_When_Visible()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.Find(".tm-keyboard-shortcuts-overlay").Should().NotBeNull();
        cut.Find(".tm-keyboard-shortcuts-modal").Should().NotBeNull();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Renders_Categories()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-category").Count.Should().Be(2);
        cut.FindAll(".tm-keyboard-shortcuts-category-title")[0].TextContent.Should().Contain("Navigation");
        cut.FindAll(".tm-keyboard-shortcuts-category-title")[1].TextContent.Should().Contain("Actions");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Renders_Shortcuts()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_Keys_In_Kbd()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        var keys = cut.FindAll("kbd");
        keys.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_Description()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-description")[0].TextContent.Should().Contain("Next step");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Overlay_Click_Calls_OnClose()
    {
        bool closed = false;
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-keyboard-shortcuts-overlay").Click();

        closed.Should().BeTrue();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Escape_Calls_OnClose()
    {
        bool closed = false;
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-keyboard-shortcuts-modal").KeyUp(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Custom_Title()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.Title, "Shortcuts"));

        cut.Find(".tm-keyboard-shortcuts-title").TextContent.Should().Contain("Shortcuts");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Has_Dialog_Role()
    {
        var cut = RenderComponent<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.Find(".tm-keyboard-shortcuts-modal").GetAttribute("role").Should().Be("dialog");
    }
}
