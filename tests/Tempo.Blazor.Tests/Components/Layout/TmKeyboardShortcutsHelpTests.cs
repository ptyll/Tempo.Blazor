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
        // m7: a real browser click on the backdrop always STARTS with a pointerdown there; the
        // flag it arms is what lets HandleBackdropClickAsync treat this as an outside click.
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-keyboard-shortcuts-overlay").TriggerEvent("onpointerdown", new PointerEventArgs());
        cut.Find(".tm-keyboard-shortcuts-overlay").Click();

        closed.Should().BeTrue("a press that starts and ends on the backdrop is an outside click");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_BackdropClickWithoutAPressOnTheBackdrop_DoesNotClose()
    {
        // m7 (review, the TmCommandPalette pattern): a drag-select that starts inside the panel and
        // ends over the backdrop delivers only the CLICK there (the common ancestor of press and
        // release) — the backdrop's pointerdown never ran, the press flag was never armed, and the
        // dialog must stay open. bUnit cannot replay the pointer sequence itself (its dispatcher
        // bubbles a panel pointerdown straight into the backdrop's arming handler), so the bare
        // click IS this scenario in bUnit; the real drag is proven in the browser by
        // Shortcuts_ClickInsideTheDialog_DoesNotClose_BackdropClickDoes.
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-keyboard-shortcuts-overlay").Click();

        closed.Should().BeFalse("the press started inside the panel, so this click is the end of " +
            "a drag-select and must not dismiss the dialog");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_ClickInsideThePanel_DoesNotClose()
    {
        // 2.9.1: the fence around the panel is a REAL element wrapper now (the phase-7 palette
        // pattern) — the old tag-level @onclick:stopPropagation on <TmFocusScope> was a runtime
        // no-op and a click anywhere inside the dialog closed it. bUnit delivers a click only to
        // a handler on the element chain, so the missing-handler exception IS the observable: the
        // content carries no close handler of its own and the backdrop's handler is out of reach.
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        var act = () => cut.Find(".tm-keyboard-shortcuts-content").Click();
        act.Should().Throw<Bunit.MissingEventHandlerException>(
            "the content owns no click handler — the click cannot have reached the backdrop's close handler");

        closed.Should().BeFalse("a click inside the dialog must not dismiss it");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_CloseButton_StillCloses()
    {
        // The positive handler inside the fence: the close button's own click closes the dialog
        // even though its click never reaches the backdrop (the wrapper stops it — the button's
        // OWN handler is what fires).
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        cut.Find(".tm-keyboard-shortcuts-close").Click();

        closed.Should().BeTrue("the header close button works inside the fence");
    }

    [Fact]
    public void TmKeyboardShortcutsHelp_PanelWrapper_IsAnElementFenceNotATagDirective()
    {
        // The fence must live on a real element: the wrapper is a direct div child of the overlay
        // and carries only the stopPropagation directive — dispatching a click ON it proves there
        // is no click handler there (a component tag-level directive would have been a splatted
        // no-op and this wrapper would not exist at all).
        bool closed = false;
        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true)
            .Add(c => c.OnClose, EventCallback.Factory.Create(this, () => closed = true)));

        var wrapper = cut.Find(".tm-keyboard-shortcuts-overlay > div");
        wrapper.Should().NotBeNull("the fence is an element wrapper (the palette pattern)");
        var act = () => wrapper.Click();
        act.Should().Throw<Bunit.MissingEventHandlerException>(
            "the wrapper carries only @onclick:stopPropagation — no handler of its own");

        closed.Should().BeFalse();
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

    // ── Activate after promote (F3 follow-up, c51ddf81 #1) ────────────────────

    [Fact]
    public void TmKeyboardShortcutsHelp_FocusTrap_ActivatesOnlyAfterPromotion()
    {
        var top = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js");
        var promote = top.SetupVoid("promote", _ => true); // no result: the call stays pending
        var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js");
        var activate = trap.SetupVoid("activate", _ => true).SetVoidResult();

        var cut = Render<TmKeyboardShortcutsHelp>(p => p
            .Add(c => c.Categories, TestCategories())
            .Add(c => c.IsVisible, true));

        activate.Invocations["activate"].Should().BeEmpty(
            "the trap must not activate while the promote interop is still pending — an unpromoted " +
            "popover root can be display:none and the initial-focus move would land on <body>");

        promote.SetVoidResult();
        cut.WaitForAssertion(() => activate.Invocations["activate"].Should().HaveCount(1, "completing the promote is what releases the trap's activation"));
    }
}
