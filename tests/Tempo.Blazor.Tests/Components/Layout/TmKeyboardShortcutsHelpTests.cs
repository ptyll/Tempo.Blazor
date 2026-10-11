using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
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
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, false));

        cut.FindAll(".tm-keyboard-shortcuts-overlay").Count.Should().Be(0);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_When_Visible()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.Find(".tm-keyboard-shortcuts-overlay").Should().NotBeNull();
        cut.Find(".tm-keyboard-shortcuts-modal").Should().NotBeNull();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Renders_Categories()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-category").Count.Should().Be(2);
        cut.FindAll(".tm-keyboard-shortcuts-category-title")[0].TextContent.Should().Contain("Navigation");
        cut.FindAll(".tm-keyboard-shortcuts-category-title")[1].TextContent.Should().Contain("Actions");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Renders_Shortcuts()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-item").Count.Should().Be(3);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_Keys_In_Kbd()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        var keys = cut.FindAll("kbd");
        keys.Count.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Shows_Description()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-description")[0].TextContent.Should().Contain("Next step");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Overlay_Click_Calls_OnClose()
    {
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
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
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        // Escape is the focus trap's document listener. bUnit has no document, so the test invokes the
        // same callback the trap would.
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        closed.Should().BeTrue();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Custom_Title()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.Title, "Shortcuts"));

        cut.Find(".tm-keyboard-shortcuts-title").TextContent.Should().Contain("Shortcuts");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_EmptyTitle_FallsBackToTheLocalizedDefault()
    {
        // L13: an empty title is not a name. aria-labelledby must point at the localized default,
        // not at a heading that renders nothing.
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.Title, ""));

        var dialog = cut.Find(".tm-keyboard-shortcuts-modal");
        var heading = cut.Find("#" + dialog.GetAttribute("aria-labelledby"));
        heading.TextContent.Trim().Should().Be("Keyboard Shortcuts");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Has_Dialog_Role()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.Find(".tm-keyboard-shortcuts-modal").GetAttribute("role").Should().Be("dialog");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Default_HasNoSubtitle_AndSplitsOnPlus()
    {
        // Frozen default: no Description parameter means no subtitle, and "Ctrl+N" stays a chord
        // of two keys. A later opt-in must not change this markup.
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-subtitle").Should().BeEmpty();
        cut.FindAll(".tm-keyboard-shortcuts-item")[0].QuerySelectorAll("kbd").Count.Should().Be(2);

        // L8: the heading wrapper exists only to hold a subtitle. With no description the title is a
        // direct child of the header, which is the historical shape.
        var header = cut.Find(".tm-keyboard-shortcuts-header");
        header.QuerySelector(":scope > .tm-keyboard-shortcuts-heading").Should().BeNull();
        header.QuerySelector(":scope > .tm-keyboard-shortcuts-title").Should().NotBeNull();
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_Description_RendersSubtitle_AndLabelsTheDialog()
    {
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.Description, "Shortcuts work with an external keyboard"));

        cut.Find(".tm-keyboard-shortcuts-subtitle").TextContent
            .Should().Contain("Shortcuts work with an external keyboard");

        var titleId = cut.Find(".tm-keyboard-shortcuts-title").GetAttribute("id");
        titleId.Should().NotBeNullOrEmpty();
        cut.Find(".tm-keyboard-shortcuts-modal").GetAttribute("aria-labelledby").Should().Be(titleId);
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_AlternativeSeparator_DoesNotRenderAChord()
    {
        // "Arrow Up / Arrow Down" is two alternative keys, not a chord. A separator other than '+'
        // must keep each alternative as its own key and must not split the words on the spaces.
        var categories = new[]
        {
            new TmShortcutCategory("Navigation",
            [
                new TmKeyboardShortcut("Arrow Up / Arrow Down", "Move", " / "),
            ]),
        };

        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, categories)
            .Add(c => c.IsVisible, true));

        var keys = cut.FindAll("kbd").Select(k => k.TextContent.Trim()).ToArray();
        keys.Should().Equal("Arrow Up", "Arrow Down");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_AlternativeSeparator_RendersALocalizedJoinerBetweenKeys()
    {
        // Review L1: "Arrow Up / Arrow Down" rendered two bare <kbd> chips, indistinguishable from
        // the chord "Arrow Up"+"Arrow Down". A separator other than '+' must put a screen-reader-read,
        // localized joiner between the keys.
        var categories = new[]
        {
            new TmShortcutCategory("Navigation",
            [
                new TmKeyboardShortcut("Arrow Up / Arrow Down", "Move", " / "),
            ]),
        };

        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, categories)
            .Add(c => c.IsVisible, true));

        var joiner = cut.Find(".tm-keyboard-shortcuts-or");
        joiner.TextContent.Trim().Should().Be("or");

        var key = cut.Find(".tm-keyboard-shortcuts-key");
        var children = key.ChildNodes
            .OfType<AngleSharp.Dom.IElement>()
            .ToList();
        children.Select(n => n.NodeName.ToLowerInvariant()).Should().Equal("kbd", "span", "kbd");
        children[1].ClassName.Should().Contain("tm-keyboard-shortcuts-or");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_DefaultSeparator_RendersNoJoiner()
    {
        // The '+' default must keep the frozen chord markup exactly: bare <kbd> chips, no joiner.
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        cut.FindAll(".tm-keyboard-shortcuts-or").Should().BeEmpty();
        cut.FindAll(".tm-keyboard-shortcuts-item")[0].QuerySelectorAll("kbd").Count.Should().Be(2);
    }
}
