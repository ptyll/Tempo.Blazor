using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// TDD tests for <see cref="TmEditorShell"/> — the responsive three-region editor frame
/// (desktop columns, tablet side sheets with at most one open, mobile canvas + inline sheet
/// or tabs + the F5 action bar).
/// </summary>
public class TmEditorShellTests : LocalizationTestBase
{
    private static IReadOnlyList<TmActionItem> Actions =>
    [
        new TmActionItem { Id = "save", Label = "Save", OnClick = EventCallback.Factory.Create(new object(), () => { }) },
    ];

    private IRenderedComponent<TmEditorShell> RenderShell(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmEditorShell>>? extra = null)
    {
        return Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Header, builder => builder.AddContent(0, "Shell header"));
            p.Add(x => x.Toolbar, builder => builder.AddContent(0, "Shell toolbar"));
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.StatusBar, builder => builder.AddContent(0, "Status text"));
            extra?.Invoke(p);
        });
    }

    // ── Desktop ─────────────────────────────────────────────────────────────

    [Fact]
    public void Desktop_RendersThreeRegions()
    {
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find("[data-region='left']").TextContent.Should().Contain("Left tools");
        cut.Find("[data-region='canvas']").TextContent.Should().Contain("Canvas body");
        cut.Find("[data-region='right']").TextContent.Should().Contain("Right props");
        // No overlay surfaces on desktop.
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    // ── Tablet ──────────────────────────────────────────────────────────────

    [Fact]
    public void Tablet_RendersAtMostOneSideSheet()
    {
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.FindAll(".tm-drawer").Should().HaveCount(1, "both panels open must render exactly one side sheet");
        cut.FindAll(".tm-drawer")[0].ClassList.Should().Contain("tm-drawer--left");
        cut.Find("[data-region='canvas']").TextContent.Should().Contain("Canvas body");
    }

    [Fact]
    public void Tablet_ClosingTheActiveSideSheet_RevealsTheOther()
    {
        var leftOpen = true;
        var rightOpen = true;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Tablet);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
            p.Add(x => x.RightOpen, rightOpen);
            p.Add(x => x.RightOpenChanged, EventCallback.Factory.Create<bool>(this, v => rightOpen = v));
        });

        cut.Find(".tm-drawer--left .tm-drawer__close").Click();
        leftOpen.Should().Be(false);

        cut.Render(p => p.Add(x => x.LeftOpen, leftOpen));
        var drawer = cut.Find(".tm-drawer");
        drawer.ClassList.Should().Contain("tm-drawer--right");
    }

    [Fact]
    public void Tablet_OpeningTheSecondPanel_SwitchesTheSheet()
    {
        var leftOpen = true;
        var rightOpen = false;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Tablet);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
            p.Add(x => x.RightOpen, rightOpen);
            p.Add(x => x.RightOpenChanged, EventCallback.Factory.Create<bool>(this, v => rightOpen = v));
        });

        cut.Find(".tm-editor-shell__panel-toggle--right").Click();
        rightOpen.Should().Be(true);

        cut.Render(p => p.Add(x => x.RightOpen, rightOpen));
        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--right");
    }

    [Fact]
    public void Tablet_ClosingTheRevealedSheet_ClosesThatPanel()
    {
        // Regression: the close callback must target the sheet ACTUALLY rendered (the fallback
        // side), not the stale most-recent side — otherwise the revealed sheet can never close.
        var leftOpen = true;
        var rightOpen = true;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Tablet);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
            p.Add(x => x.RightOpen, rightOpen);
            p.Add(x => x.RightOpenChanged, EventCallback.Factory.Create<bool>(this, v => rightOpen = v));
        });

        // Close the left (active) sheet — the right one takes over.
        cut.Find(".tm-drawer--left .tm-drawer__close").Click();
        cut.Render(p => p.Add(x => x.LeftOpen, leftOpen));
        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--right");

        // Closing the revealed right sheet must close the RIGHT panel (not re-fire the left one).
        cut.Find(".tm-drawer--right .tm-drawer__close").Click();
        rightOpen.Should().Be(false);

        cut.Render(p => p.Add(x => x.RightOpen, rightOpen));
        cut.FindAll(".tm-drawer").Should().BeEmpty("both panels are closed now");
    }

    // ── Mobile ──────────────────────────────────────────────────────────────

    [Fact]
    public void Mobile_RendersCanvasPanelSheetAndActionBar()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobileActions, Actions));

        cut.Find("[data-region='canvas']").TextContent.Should().Contain("Canvas body");
        var sheet = cut.Find(".tm-editor-shell__sheet");
        sheet.ClassList.Should().Contain("tm-drawer--inline", "the mobile panel sheet is an inline (non-modal) sheet");
        sheet.ClassList.Should().Contain("tm-drawer--bottom");
        sheet.TextContent.Should().Contain("Left tools");
        sheet.TextContent.Should().Contain("Right props");
        cut.FindAll(".tm-mobile-action-bar__bar").Should().HaveCount(1);
    }

    [Fact]
    public void Mobile_NoPanelsOpen_RendersNoSheet()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.RightOpen, false);
        });

        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void Mobile_InlineSheet_IsNotPromoted()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.Find(".tm-editor-shell__sheet").GetAttribute("popover").Should().BeNull();
    }

    [Fact]
    public void MobilePanelPresentationTabs_RendersTabs()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        var tablist = cut.Find("[role='tablist']");
        tablist.QuerySelectorAll("[role='tab']").Should().HaveCount(2);
        cut.FindAll("[role='tabpanel']").Should().HaveCount(1, "only the selected panel renders");
    }

    [Fact]
    public void Mobile_Tabs_SelectingAShowsThatPanel()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        var rightTab = cut.FindAll("[role='tab']")[1];
        rightTab.GetAttribute("aria-selected").Should().Be("false");
        rightTab.Click();

        cut.FindAll("[role='tab']")[1].GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[role='tabpanel']").TextContent.Should().Contain("Right props");
    }

    // ── Two-way panel state ─────────────────────────────────────────────────

    [Fact]
    public void LeftOpenRightOpen_TwoWay()
    {
        var leftOpen = true;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Tablet);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
        });

        cut.Find(".tm-drawer__close").Click();
        leftOpen.Should().Be(false);
    }

    [Fact]
    public void Desktop_HiddenPanel_RendersToggleWithExpandedStateAndControls()
    {
        var leftOpen = false;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Desktop);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
        });

        var toggle = cut.Find(".tm-editor-shell__panel-toggle--left");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        var controls = toggle.GetAttribute("aria-controls");
        controls.Should().NotBeNullOrEmpty();
        cut.Find($"#{controls}").Should().NotBeNull();
        cut.FindAll("[data-region='left']").Should().BeEmpty();

        toggle.Click();
        leftOpen.Should().Be(true);
    }

    // ── Collapsed panels ────────────────────────────────────────────────────

    [Fact]
    public void CollapsedPanels_RendersRailInsteadOfPanel()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
            p.Add(x => x.CollapsedPanels, EditorShellPanel.Left));

        cut.FindAll("[data-region='left']").Should().BeEmpty();
        var rail = cut.Find(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle");
        rail.GetAttribute("aria-expanded").Should().Be("true");
    }

    // ── Slots ───────────────────────────────────────────────────────────────

    [Fact]
    public void Slots_RenderInDataRegions()
    {
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find("[data-region='header']").TextContent.Should().Contain("Shell header");
        cut.Find("[data-region='toolbar']").TextContent.Should().Contain("Shell toolbar");
        cut.Find("[data-region='status-bar']").TextContent.Should().Contain("Status text");
    }

    // ── Close paths, aria-expanded and layout flips (the F5 focus-restore pattern) ──

    private sealed class Host
    {
        public bool Left = true;
        public bool Right = true;
    }

    private IRenderedComponent<TmEditorShell> RenderControlled(TmLayoutMode mode, Host host, bool leftOnly = false)
    {
        return Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.LeftOpen, host.Left);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => host.Left = v));
            p.Add(x => x.RightOpen, host.Right && !leftOnly);
            p.Add(x => x.RightOpenChanged, EventCallback.Factory.Create<bool>(this, v => host.Right = v));
        });
    }

    private static void Sync(IRenderedComponent<TmEditorShell> cut, Host host, TmLayoutMode mode)
    {
        cut.Render(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.LeftOpen, host.Left);
            p.Add(x => x.RightOpen, host.Right);
        });
    }

    [Fact]
    public async Task Tablet_EscapeAndBackdrop_CloseTheSheet_AndTheToggleReadsCollapsed()
    {
        var host = new Host { Right = false };
        var cut = RenderControlled(TmLayoutMode.Tablet, host);

        // Escape arrives through the focus scope's document-level listener.
        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        host.Left.Should().BeFalse("Escape must close the sheet through the controlled callback");
        Sync(cut, host, TmLayoutMode.Tablet);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        var toggle = cut.Find(".tm-editor-shell__panel-toggle--left");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        cut.Find($"#{toggle.GetAttribute("aria-controls")}").Should().NotBeNull();

        // Reopen from the toggle, then close through the backdrop.
        toggle.Click();
        host.Left.Should().BeTrue();
        Sync(cut, host, TmLayoutMode.Tablet);
        cut.Find(".tm-drawer__overlay").Click();
        host.Left.Should().BeFalse("the backdrop is a close path too");
        Sync(cut, host, TmLayoutMode.Tablet);
        cut.Find(".tm-editor-shell__panel-toggle--left").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public async Task Mobile_EscapeAndCloseButton_CloseThePanels_AndTheReopenToggleIsCollapsed()
    {
        var host = new Host();
        var cut = RenderControlled(TmLayoutMode.Mobile, host);

        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());
        host.Left.Should().BeFalse();
        host.Right.Should().BeFalse();
        Sync(cut, host, TmLayoutMode.Mobile);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        var reopen = cut.Find(".tm-editor-shell__panel-toggle--mobile");
        reopen.GetAttribute("aria-expanded").Should().Be("false");

        reopen.Click();
        Sync(cut, host, TmLayoutMode.Mobile);
        cut.FindAll(".tm-editor-shell__panel-toggle--mobile").Should().BeEmpty("the toggle unmounts while the sheet is open");
        cut.Find(".tm-editor-shell__sheet-close").Click();
        host.Left.Should().BeFalse();
        Sync(cut, host, TmLayoutMode.Mobile);
        cut.Find(".tm-editor-shell__panel-toggle--mobile").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void LayoutFlip_MobileDesktopMobile_DoesNotReopenAnything()
    {
        var host = new Host();
        var cut = RenderControlled(TmLayoutMode.Mobile, host);
        cut.Find(".tm-editor-shell__sheet-close").Click();
        Sync(cut, host, TmLayoutMode.Mobile);
        cut.FindAll(".tm-drawer").Should().BeEmpty();

        Sync(cut, host, TmLayoutMode.Desktop);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.FindAll("[data-region='left']").Should().BeEmpty("the host closed both panels");

        Sync(cut, host, TmLayoutMode.Mobile);
        cut.FindAll(".tm-drawer").Should().BeEmpty("a layout flip must not reopen the closed panels sheet");
        cut.Find(".tm-editor-shell__panel-toggle--mobile").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void LayoutFlip_TabletMobileTablet_DoesNotReopenAClosedSheet()
    {
        var host = new Host { Right = false };
        var cut = RenderControlled(TmLayoutMode.Tablet, host);
        cut.Find(".tm-drawer__close").Click();
        Sync(cut, host, TmLayoutMode.Tablet);
        cut.FindAll(".tm-drawer").Should().BeEmpty();

        Sync(cut, host, TmLayoutMode.Mobile);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        Sync(cut, host, TmLayoutMode.Tablet);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.Find(".tm-editor-shell__panel-toggle--left").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void LayoutFlip_ResetsTheMobileSheetSnap()
    {
        var host = new Host();
        var cut = RenderControlled(TmLayoutMode.Mobile, host);
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");

        Sync(cut, host, TmLayoutMode.Desktop);
        Sync(cut, host, TmLayoutMode.Mobile);

        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");
    }

    [Fact]
    public void ProgrammaticClose_OnTablet_RendersTheCollapsedToggle()
    {
        var host = new Host { Right = false };
        var cut = RenderControlled(TmLayoutMode.Tablet, host);

        // The host closes the panel itself — no sheet gesture involved.
        host.Left = false;
        Sync(cut, host, TmLayoutMode.Tablet);

        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.Find(".tm-editor-shell__panel-toggle--left").GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void FocusRestore_EveryFocusAsyncSite_SwallowsJsExceptionToo()
    {
        // bUnit attaches no JSRuntime to element references, so FocusAsync only ever throws
        // InvalidOperationException here; the JSException half (a detached element in a real
        // browser) is pinned by this source guard plus the live-browser E2E.
        var root = AppContext.BaseDirectory;
        var dir = new DirectoryInfo(root);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        var source = File.ReadAllText(Path.Combine(dir!.FullName, "src", "Tempo.Blazor", "Components", "Layout", "TmEditorShell.razor"));

        var sites = System.Text.RegularExpressions.Regex.Matches(source, @"\.FocusAsync\(");
        sites.Count.Should().BeGreaterThan(0);
        foreach (System.Text.RegularExpressions.Match site in sites)
        {
            var window = source.Substring(site.Index, Math.Min(700, source.Length - site.Index));
            window.Should().Contain("JSException", "every best-effort focus move must swallow JSException");
        }
    }
    [Fact]
    public void Desktop_NoMobileActions_RendersNoBar()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }
}
