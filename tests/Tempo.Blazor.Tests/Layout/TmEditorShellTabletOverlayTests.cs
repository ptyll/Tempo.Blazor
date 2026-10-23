using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Decision EDITOR-SHELL-TABLET-PRESENTATION: <see cref="TmEditorShell.TabletPanelPresentation"/> (one value for both
/// sides). <see cref="TabletPanelPresentation.Docked"/> is today's docked tablet; <see cref="TabletPanelPresentation.Overlay"/>
/// turns the Left panel into an overlay drawer and the Right panel into a non-modal side sheet over a full-width canvas,
/// at most one open, driven by LeftOpen / RightOpen, with no rails and no resize, Escape and focus return, 44 px targets on a
/// coarse pointer and reduced motion. Desktop and mobile are untouched.
/// </summary>
public class TmEditorShellTabletOverlayTests : LocalizationTestBase
{
    private sealed class Host
    {
        public bool Left;
        public bool Right;
        public List<string> Raised = [];
    }

    private IRenderedComponent<TmEditorShell> RenderShell(
        TmLayoutMode mode,
        Host host,
        TabletPanelPresentation presentation = TabletPanelPresentation.Overlay,
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
        => Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.TabletPanelPresentation, presentation);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Toolbar, b => b.AddContent(0, "Shell toolbar"));
            p.Add(x => x.Left, b => b.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, b => b.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, b => b.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, host.Left);
            p.Add(x => x.RightOpen, host.Right);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => { host.Left = v; host.Raised.Add($"left:{v}"); }));
            p.Add(x => x.RightOpenChanged, EventCallback.Factory.Create<bool>(this, v => { host.Right = v; host.Raised.Add($"right:{v}"); }));
            extra?.Invoke(p);
        });

    private static void Sync(IRenderedComponent<TmEditorShell> cut, Host host)
        => cut.Render(p =>
        {
            p.Add(x => x.LeftOpen, host.Left);
            p.Add(x => x.RightOpen, host.Right);
        });

    // ── The parameter and its default ─────────────────────────────────────────

    [Fact]
    public void TabletPanelPresentation_DefaultsToDocked_AndTheEnumValuesAreStable()
    {
        ((int)TabletPanelPresentation.Docked).Should().Be(0);
        ((int)TabletPanelPresentation.Overlay).Should().Be(1);
        var cut = Render<TmEditorShell>(p => p.Add(x => x.LayoutMode, TmLayoutMode.Tablet));
        cut.Instance.TabletPanelPresentation.Should().Be(TabletPanelPresentation.Docked);
    }

    [Fact]
    public void Docked_Tablet_StaysExactlyToday_NoOverlay()
    {
        var host = new Host { Left = true, Right = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host, TabletPanelPresentation.Docked);

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1, "docked tablet keeps one expanded panel");
        cut.FindAll(".tm-editor-shell__rail").Should().HaveCount(1);
        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__resizer").Should().NotBeEmpty();
    }

    // ── Overlay only on the tablet layout ─────────────────────────────────────

    [Fact]
    public void Overlay_Desktop_IsUnchanged_DockedPanelsAndResizers()
    {
        var host = new Host { Left = true, Right = true };
        var cut = RenderShell(TmLayoutMode.Desktop, host);

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(2);
        cut.FindAll(".tm-editor-shell__resizer").Should().HaveCount(2);
        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty("the overlay is a tablet presentation only");
    }

    [Fact]
    public void Overlay_Mobile_IsUnchanged_TheBottomSheet()
    {
        var host = new Host { Left = true, Right = true };
        var cut = RenderShell(TmLayoutMode.Mobile, host);

        cut.FindAll(".tm-editor-shell__sheet").Should().HaveCount(1);
        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty();
    }

    // ── Left = overlay drawer, Right = non-modal side sheet ───────────────────

    [Fact]
    public void Overlay_Tablet_LeftOpen_IsAnOverlayDrawer_NamedByItsTitle()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, new Host { Left = true });

        var drawer = cut.Find(".tm-editor-shell__overlay--left");
        drawer.QuerySelector("[data-region='left']")!.TextContent.Should().Contain("Left tools");
        cut.FindAll(".tm-editor-shell__overlay--right").Should().BeEmpty();
        drawer.GetAttribute("aria-modal").Should().Be("false", "a non-modal surface: the canvas stays operable");
        var titleId = drawer.GetAttribute("aria-labelledby");
        titleId.Should().NotBeNullOrWhiteSpace();
        var title = cut.Find("#" + titleId);
        title.TextContent.Trim().Should().Be("Blocks");
        title.TagName.Should().NotBe("H2", "a host page outline must not gain a heading from the editor");
        cut.FindComponent<TmFocusScope>().Instance.Modal.Should().BeFalse();
    }

    [Fact]
    public void Overlay_Tablet_RightOpen_IsANonModalSideSheet()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, new Host { Right = true });

        var sheet = cut.Find(".tm-editor-shell__overlay--right");
        sheet.QuerySelector("[data-region='right']")!.TextContent.Should().Contain("Right props");
        sheet.GetAttribute("aria-modal").Should().Be("false");
        cut.FindAll(".tm-editor-shell__overlay--left").Should().BeEmpty();
        cut.FindAll(".tm-drawer").Should().BeEmpty("no modal TmDrawer, nothing in the top layer");
        cut.FindAll("[popover]").Should().BeEmpty();
        cut.FindAll("[inert]").Should().BeEmpty();
    }

    [Fact]
    public void Overlay_Tablet_NothingOpen_RendersOnlyTheCanvas()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, new Host());

        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty();
        cut.Find("[data-region='canvas']").TextContent.Should().Contain("Canvas body");
    }

    [Fact]
    public void Overlay_Tablet_HasNoDockedPanels_NoRails_NoStripToggles_NoResizeSeparators()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, new Host { Left = true }, extra: p =>
            p.Add(x => x.CollapsedPanels, EditorShellPanel.Right));

        cut.FindAll("aside.tm-editor-shell__panel").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__rail").Should().BeEmpty("the overlay has no rail, even for a collapsed set");
        cut.FindAll(".tm-editor-shell__panel-toggle").Should().BeEmpty("the host renders the toggles");
        cut.FindAll("[role='separator']").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__resizer").Should().BeEmpty();
    }

    [Fact]
    public void Overlay_Tablet_CanvasStaysFullWidth_ThePanelIsNotAColumnBesideIt()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, new Host { Right = true });

        var main = cut.Find("[data-region='main']");
        main.ClassList.Should().Contain("tm-editor-shell__main--overlay");
        var canvas = main.QuerySelector(":scope > .tm-editor-shell__canvas");
        canvas.Should().NotBeNull("the canvas is a direct child of main: nothing wraps or narrows it");
        canvas!.GetAttribute("style").Should().BeNull("no width is ever imposed on the canvas");
        main.QuerySelectorAll(":scope > aside").Should().BeEmpty();
    }

    [Fact]
    public void Overlay_Tablet_PanelWidth_ComesFromTheOverlayWidthToken_NotARenderedPixelValue()
    {
        var css = ThemeCss.ComponentCss("_editor-shell.css");
        css.Should().Contain("--tm-editor-shell-overlay-width");
        css.Should().Contain("60%", "a panel never covers more than 60 % of the editor");
    }

    // ── At most one open; two-way LeftOpen / RightOpen ────────────────────────

    [Fact]
    public void Overlay_BothOpen_ShowsExactlyOne_TheInspectorWins_AndTheHostIsTold()
    {
        var host = new Host { Left = true, Right = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);

        cut.FindAll(".tm-editor-shell__overlay").Should().HaveCount(1);
        cut.Find(".tm-editor-shell__overlay").ClassList.Should().Contain("tm-editor-shell__overlay--right");
        host.Raised.Should().Contain("left:False", "the panel that lost raises its own Open callback");
        host.Raised.Should().NotContain("right:False");
    }

    [Fact]
    public void Overlay_OpeningTheLeftPanel_WhileTheRightIsOpen_ClosesTheRight_AndRaisesRightOpenChanged()
    {
        var host = new Host { Right = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);
        cut.FindAll(".tm-editor-shell__overlay--right").Should().HaveCount(1);
        host.Raised.Clear();

        host.Left = true;       // the host's toggle opened the drawer
        Sync(cut, host);

        host.Raised.Should().Equal("right:False");
        host.Right.Should().BeFalse();
        Sync(cut, host);
        cut.FindAll(".tm-editor-shell__overlay").Should().HaveCount(1);
        cut.Find(".tm-editor-shell__overlay").ClassList.Should().Contain("tm-editor-shell__overlay--left");
    }

    [Fact]
    public void Overlay_OpeningTheRightPanel_WhileTheLeftIsOpen_ClosesTheLeft_AndRaisesLeftOpenChanged()
    {
        var host = new Host { Left = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);
        host.Raised.Clear();

        host.Right = true;
        Sync(cut, host);

        host.Raised.Should().Equal("left:False");
        host.Left.Should().BeFalse();
    }

    [Fact]
    public void Overlay_AHostThatIgnoresTheCallback_StillGetsOnePanel_AndNoRepeatedCallbacks()
    {
        var raised = 0;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Tablet);
            p.Add(x => x.TabletPanelPresentation, TabletPanelPresentation.Overlay);
            p.Add(x => x.Left, b => b.AddContent(0, "Left tools"));
            p.Add(x => x.Right, b => b.AddContent(0, "Right props"));
            p.Add(x => x.Canvas, b => b.AddContent(0, "Canvas body"));
            p.Add(x => x.LeftOpen, true);
            p.Add(x => x.RightOpen, true);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, _ => raised++));
        });

        cut.Render(p => p.Add(x => x.Canvas, b => b.AddContent(0, "Canvas body 2")));
        cut.Render(p => p.Add(x => x.Canvas, b => b.AddContent(0, "Canvas body 3")));

        cut.FindAll(".tm-editor-shell__overlay").Should().HaveCount(1);
        raised.Should().Be(1, "the imposed close is announced once, not on every parameter pass");
    }

    [Fact]
    public void Overlay_FollowsLeftOpenAndRightOpen_BothWays()
    {
        var host = new Host();
        var cut = RenderShell(TmLayoutMode.Tablet, host);

        host.Left = true; Sync(cut, host);
        cut.FindAll(".tm-editor-shell__overlay--left").Should().HaveCount(1);

        host.Left = false; Sync(cut, host);
        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty();

        host.Right = true; Sync(cut, host);
        cut.FindAll(".tm-editor-shell__overlay--right").Should().HaveCount(1);
        host.Raised.Should().BeEmpty("a host-driven open or close is not echoed back");
    }

    // ── Close button, Escape, focus ───────────────────────────────────────────

    [Fact]
    public void Overlay_CloseButton_IsLocalized_AndRaisesTheOpenCallbackWithFalse()
    {
        var host = new Host { Right = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);

        var close = cut.Find(".tm-editor-shell__overlay--right .tm-editor-shell__overlay-close");
        close.GetAttribute("aria-label").Should().Be("Close panel");
        close.GetAttribute("type").Should().Be("button");

        close.Click();

        host.Raised.Should().Equal("right:False");
    }

    [Fact]
    public void Overlay_LeftCloseButton_RaisesLeftOpenChanged()
    {
        var host = new Host { Left = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);

        cut.Find(".tm-editor-shell__overlay--left .tm-editor-shell__overlay-close").Click();

        host.Raised.Should().Equal("left:False");
    }

    [Fact]
    public void Overlay_Escape_ClosesTheOpenPanel_ThroughTheOpenCallback()
    {
        var host = new Host { Left = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);
        var scope = cut.FindComponent<TmFocusScope>().Instance;
        scope.CloseOnEscape.Should().BeTrue();

        cut.InvokeAsync(() => scope.HandleFocusTrapEscapeAsync()).GetAwaiter().GetResult();

        host.Raised.Should().Equal("left:False");
    }

    [Fact]
    public void Overlay_RestoresFocusToTheOpener_NotToAnExplicitTarget()
    {
        var host = new Host { Left = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);

        var scope = cut.FindComponent<TmFocusScope>().Instance;
        scope.RestoreFocus.Should().BeTrue("Escape and the × return the focus to the control that opened the panel");
        scope.RestoreFocusTarget.Should().BeNull("the opener is the target: the focus scope notes it when the panel opens");
    }

    [Fact]
    public void Overlay_OpenedFromOutsideTheCanvas_MovesFocusIntoThePanel_ButNotAtFirstRender()
    {
        var host = new Host();
        var cut = RenderShell(TmLayoutMode.Tablet, host);
        JSInterop.Invocations.Where(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase) && !i.Identifier.Contains("Trap", StringComparison.Ordinal)
            && i.Identifier != "focusWithin").Should().BeEmpty("a first render never moves focus");

        host.Left = true;
        Sync(cut, host);

        cut.WaitForAssertion(() => JSInterop.VerifyFocusAsyncInvoke());
    }

    [Fact]
    public void Overlay_AnInitiallyOpenPanel_DoesNotStealFocusAtLoad()
    {
        RenderShell(TmLayoutMode.Tablet, new Host { Right = true });

        JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus").Should().Be(0);
    }

    // ── Test ids ──────────────────────────────────────────────────────────────

    [Fact]
    public void Overlay_RootsCarryTestIds_NamespacedByTestIdPrefix()
    {
        var left = RenderShell(TmLayoutMode.Tablet, new Host { Left = true });
        left.Find(".tm-editor-shell__overlay--left").GetAttribute("data-testid").Should().Be("editor-shell-left-overlay");
        left.Find(".tm-editor-shell__overlay-close").GetAttribute("data-testid").Should().Be("editor-shell-left-overlay-close");

        var right = RenderShell(TmLayoutMode.Tablet, new Host { Right = true }, extra: p => p.Add(x => x.TestIdPrefix, "ed1"));
        right.Find(".tm-editor-shell__overlay--right").GetAttribute("data-testid").Should().Be("ed1-editor-shell-right-overlay");
        right.Find(".tm-editor-shell__overlay-close").GetAttribute("data-testid").Should().Be("ed1-editor-shell-right-overlay-close");
    }
    // ── Flip back to desktop ──────────────────────────────────────────────────

    [Fact]
    public void Overlay_TabletToDesktopFlip_DocksThePanelsAgain()
    {
        var host = new Host { Left = true, Right = true };
        var cut = RenderShell(TmLayoutMode.Tablet, host);
        cut.FindAll(".tm-editor-shell__overlay").Should().HaveCount(1);

        host.Left = true; host.Right = true;
        cut.Render(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Desktop);
            p.Add(x => x.LeftOpen, true);
            p.Add(x => x.RightOpen, true);
        });

        cut.FindAll(".tm-editor-shell__overlay").Should().BeEmpty();
        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(2);
    }

    [Fact]
    public void Overlay_NeverRaisesCollapsedPanelsChanged()
    {
        var raised = new List<EditorShellPanel>();
        RenderShell(TmLayoutMode.Tablet, new Host { Left = true, Right = true }, extra: p =>
            p.Add(x => x.CollapsedPanelsChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised.Add(v))));

        raised.Should().BeEmpty("the overlay has no rails: the docked tablet rule does not apply");
    }

    // ── CSS contract: tokens, dark mode, 44 px, reduced motion ────────────────

    [Fact]
    public void OverlayCss_UsesTokensOnly_NoHardcodedColours()
    {
        var css = ThemeCss.ComponentCss("_editor-shell.css");
        var overlayRules = ThemeCss.Rules("_editor-shell.css")
            .Where(r => r.Selector.Contains("__overlay", StringComparison.Ordinal))
            .ToList();

        overlayRules.Should().NotBeEmpty();
        foreach (var (selector, body) in overlayRules)
        {
            body.Should().NotMatchRegex("#[0-9a-fA-F]{3,8}\\b", $"{selector} must use --tm-* tokens");
            body.Should().NotMatchRegex("\\brgba?\\(", $"{selector} must use --tm-* tokens");
        }
        css.Should().Contain("var(--tm-shadow-lg)");
    }

    [Fact]
    public void OverlayCss_CoarsePointer_GivesTheCloseButtonTheTouchTarget()
    {
        var css = ThemeCss.ComponentCss("_editor-shell.css");
        var coarse = css[css.LastIndexOf("@media (pointer: coarse)", StringComparison.Ordinal)..];

        coarse.Should().Contain(".tm-editor-shell__overlay-close");
        coarse.Should().Contain("var(--tm-touch-target)");
    }

    [Fact]
    public void OverlayCss_Animates_OnlyUnderNoPreference_AndTheSubjectIsInTheReducedMotionList()
    {
        var css = ThemeCss.ComponentCss("_editor-shell.css");
        var motion = css.IndexOf("prefers-reduced-motion: no-preference", StringComparison.Ordinal);
        motion.Should().BeGreaterThan(0, "the entrance animation exists only for users who did not ask for less motion");
        css[motion..].Should().Contain(".tm-editor-shell__overlay");

        ThemeCss.StripComments(File.ReadAllText(ThemeCss.CssPath("animations.css")))
            .Should().Contain(".tm-editor-shell__overlay", "ReducedMotionSelectorTests enumerates every animated subject");
    }

    [Fact]
    public void OverlayCss_DarkMode_RidesTheSameTokens()
    {
        // The surface, border, text and shadow are theme tokens that tokens-dark.css overrides: no component dark block needed.
        var surface = ThemeCss.Rules("_editor-shell.css").First(r => r.Selector == ".tm-editor-shell__overlay").Body;
        surface.Should().Contain("var(--tm-bg-surface)").And.Contain("var(--tm-shadow-lg)");
        ThemeCss.TokenGraph(dark: true).Should().ContainKey("--tm-bg-surface").And.ContainKey("--tm-shadow-lg");
    }
}