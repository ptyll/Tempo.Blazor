using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Overlay;

/// <summary>
/// F4 (carry-forward from F3/F5): <c>role="menu"</c> surfaces rove with ArrowUp/ArrowDown/Home/End
/// and typeahead. The behaviour lives in one module (<c>tm-menu-nav.js</c>) that
/// <see cref="TmOverlayPanel"/> attaches to a <c>Role="menu"</c> panel — the popover element or the
/// sheet's content wrapper — once per open, so TmDropdown, TmSplitButton, TmContextMenu and the F5/F4
/// overflow menus all get it from the single owner of the menu role. The real key presses are
/// asserted in the E2E lane; here the wiring contract.
/// </summary>
public class TmOverlayPanelMenuKeyboardTests : LocalizationTestBase
{
    private const string OverlayModule = "./_content/Tempo.Blazor/js/overlay.js";
    private const string MenuNavModule = "./_content/Tempo.Blazor/js/tm-menu-nav.js";

    private BunitJSModuleInterop SetupMenuNav()
    {
        var overlay = JSInterop.SetupModule(OverlayModule);
        overlay.SetupVoid("open", _ => true).SetVoidResult();
        overlay.SetupVoid("update", _ => true).SetVoidResult();
        overlay.SetupVoid("close", _ => true).SetVoidResult();
        var nav = JSInterop.SetupModule(MenuNavModule);
        nav.SetupVoid("attach", _ => true).SetVoidResult();
        nav.Setup<bool>("focusFirst", _ => true).SetResult(true);
        return nav;
    }

    [Fact]
    public void MenuRole_Popover_AttachesTheMenuKeyboardOncePerOpen()
    {
        var nav = SetupMenuNav();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, "menu")
            .AddChildContent("<button role='menuitem'>One</button>"));

        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().ContainSingle());

        // A re-render of the open panel must not attach again (the module is idempotent per
        // element, but the interop call itself is the cost this guards).
        cut.Render();
        cut.Render();
        nav.Invocations["attach"].Should().ContainSingle();
    }

    [Fact]
    public void MenuRole_Reopen_AttachesAgain_BecauseTheMenuElementIsNew()
    {
        var nav = SetupMenuNav();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, "menu")
            .AddChildContent("<button role='menuitem'>One</button>"));
        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().HaveCount(1));

        cut.Render(p => p.Add(c => c.IsOpen, false));
        cut.Render(p => p.Add(c => c.IsOpen, true));

        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().HaveCount(2));
    }

    [Fact]
    public void MenuRole_Sheet_AttachesToTheSheetContent()
    {
        var nav = SetupMenuNav();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, "menu")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<button role='menuitem'>One</button>"));

        cut.FindAll(".tm-overlay-panel-sheet__content[role='menu']").Should().HaveCount(1);
        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().ContainSingle());
    }

    [Theory]
    [InlineData("listbox")]
    [InlineData("dialog")]
    [InlineData(null)]
    public void OtherRoles_DoNotAttachTheMenuKeyboard(string? role)
    {
        var nav = SetupMenuNav();
        var cut = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, role)
            .Add(c => c.AriaLabel, "Panel")
            .AddChildContent("<button>One</button>"));

        cut.Render();
        nav.Invocations["attach"].Should().BeEmpty("only role=menu panels get menu keyboard behaviour");
    }

    [Fact]
    public void TmDropdown_MenuGetsTheMenuKeyboard()
    {
        var nav = SetupMenuNav();
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent<TmDropdownItem>(i => i.Add(x => x.Value, "a").AddChildContent("A")));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.WaitForAssertion(() => nav.Invocations["attach"].Should().ContainSingle());
    }

    [Fact]
    public void TmDropdown_OpenMovesFocusIntoTheMenu_OnceAfterTheOpen()
    {
        // A keyboard user opens the dropdown with Enter/Space: focus has to land on the first item or
        // the roving keys (which listen on the menu) are unreachable. The wrapper is the host element
        // the module searches - the popover and the sheet both stay inside it.
        var nav = SetupMenuNav();
        var cut = Render<TmDropdown>(p => p
            .Add(c => c.Text, "Options")
            .AddChildContent<TmDropdownItem>(i => i.Add(x => x.Value, "a").AddChildContent("A")));

        cut.Find("button.tm-dropdown-trigger").Click();

        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().ContainSingle());
        cut.Render();
        nav.Invocations["focusFirst"].Should().ContainSingle("a re-render of the open menu must not steal focus again");

        cut.Find("button.tm-dropdown-trigger").Click();
        cut.Find("button.tm-dropdown-trigger").Click();
        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().HaveCount(2));
    }

    [Fact]
    public void MenuRole_Popover_PassesTheDotNetRefAndTheAnchorSoTabOutDismisses_SheetDoesNot()
    {
        // H16c: Tab out of an open popover menu closes it. tm-menu-nav.js needs the panel's dotNetRef
        // (NotifyDismissedAsync) and the anchor to tell "focus left menu AND trigger" from "moved between them".
        var nav = SetupMenuNav();
        var popover = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, "menu")
            .AddChildContent("<button role='menuitem'>One</button>"));
        popover.WaitForAssertion(() => nav.Invocations["attach"].Should().ContainSingle());
        nav.Invocations["attach"].Single().Arguments.Should().HaveCount(3, "element, dotNetRef, anchor");
        nav.Invocations["attach"].Single().Arguments[1].Should().NotBeNull("a popover menu can be dismissed from JS");

        var sheet = Render<TmOverlayPanel>(p => p
            .Add(c => c.IsOpen, true)
            .Add(c => c.Role, "menu")
            .Add(c => c.MobilePresentation, PanelPresentation.Sheet)
            .Add(c => c.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<button role='menuitem'>One</button>"));
        sheet.WaitForAssertion(() => nav.Invocations["attach"].Should().HaveCount(2));
        nav.Invocations["attach"].Last().Arguments.Skip(1).Should().OnlyContain(a => a == null, "the sheet's own trap/Done owns focus");
    }

    [Fact]
    public void TmSplitButton_OpenMovesFocusIntoTheMenu_OnceAfterTheOpen()
    {
        var nav = SetupMenuNav();
        var cut = Render<Tempo.Blazor.Components.Buttons.TmSplitButton>(p => p
            .Add(c => c.Text, "Save")
            .AddChildContent("<button role='menuitem'>As copy</button>"));

        cut.Find(".tm-split-button__toggle").Click();

        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().ContainSingle());
        cut.Render();
        nav.Invocations["focusFirst"].Should().ContainSingle("a re-render of the open menu must not steal focus again");
    }

    [Fact]
    public void TmContextMenu_OpenMovesFocusIntoTheMenu_AndAnItemActivationReturnsFocusToTheTrigger()
    {
        var nav = SetupMenuNav();
        var cut = Render<Tempo.Blazor.Components.Navigation.TmContextMenu>(p => p
            .Add(c => c.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<Tempo.Blazor.Components.Navigation.TmContextMenuItem>(i => i.Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.WaitForAssertion(() => nav.Invocations["focusFirst"].Should().ContainSingle());

        var focusBefore = JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus");
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Should().BeGreaterThan(focusBefore, "activating an item closes the menu and focus returns to the trigger, not <body>"));
    }
}