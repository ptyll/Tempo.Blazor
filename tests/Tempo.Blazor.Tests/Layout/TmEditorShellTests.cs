using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// TDD tests for <see cref="TmEditorShell"/> — the responsive three-region editor frame:
/// desktop and tablet are DOCKED panels (tablet: at most one expanded, no modal), mobile is the
/// canvas + an inline bottom sheet (Left/Right tabs when two panels are open) or a full-region
/// Left | Canvas | Right tablist, plus the F5 action bar. F6 review round 1, decisions Q1 and Q2.
/// </summary>
public class TmEditorShellTests : LocalizationTestBase
{
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

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

    // ── Tablet = docked panels (Q1=A): collapsed rails, at most ONE expanded, no modal ─────

    [Fact]
    public void Tablet_FirstRender_IsDockedWithNoModalSurface()
    {
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.FindAll(".tm-drawer").Should().BeEmpty("tablet never opens a modal sheet");
        cut.FindAll("[popover]").Should().BeEmpty();
        cut.FindAll("[inert]").Should().BeEmpty("nothing is inert on a first tablet render");
        cut.FindAll("[aria-modal='true']").Should().BeEmpty();
        cut.FindComponents<TmFocusScope>().Should().BeEmpty("a docked tablet owns no focus trap");
        cut.Find("[data-region='canvas']").TextContent.Should().Contain("Canvas body");
        JSInterop.Invocations.Where(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("a tablet first render must not move focus");
    }

    [Fact]
    public void Tablet_BothOpen_RendersExactlyOneExpandedPanelAndARailForTheOther()
    {
        var cut = RenderShell(TmLayoutMode.Tablet);

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1, "at most one panel is expanded");
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("right",
            "the inspector stays expanded, the toolbox is the rail (the e-mail editor mapping)");
        cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_HonoursCollapsedPanels_BothRailed()
    {
        var cut = RenderShell(TmLayoutMode.Tablet, p =>
            p.Add(x => x.CollapsedPanels, EditorShellPanel.Left | EditorShellPanel.Right));

        cut.FindAll("aside.tm-editor-shell__panel").Should().BeEmpty();
        cut.FindAll(".tm-editor-shell__rail").Should().HaveCount(2);
    }

    [Fact]
    public void Tablet_FirstRender_RaisesCollapsedPanelsChanged_ForTheRailedSide()
    {
        EditorShellPanel? raised = null;
        RenderShell(TmLayoutMode.Tablet, p => p.Add(x => x.CollapsedPanelsChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v)));

        raised.Should().Be(EditorShellPanel.Left,
            "the shell rails the second panel and tells the host, so a bound state stays truthful");
    }

    [Fact]
    public void Tablet_AtMostOneExpanded_EvenWhenTheHostIgnoresTheCallback()
    {
        // The host never applies CollapsedPanels: the render itself must still hold the invariant.
        var cut = RenderShell(TmLayoutMode.Tablet, p => p.Add(x => x.CollapsedPanelsChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, _ => { })));

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1);
        cut.Render(p => p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body 2")));
        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1);
    }

    [Fact]
    public void Tablet_ExpandingTheRail_RailsTheOtherPanel_AndRaisesTheNewCollapsedSet()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Tablet, p =>
        {
            p.Add(x => x.CollapsedPanels, EditorShellPanel.Left);
            p.Add(x => x.CollapsedPanelsChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v));
        });

        cut.Find(".tm-editor-shell__rail--left button").Click();

        raised.Should().Be(EditorShellPanel.Right, "expanding left rails right: one expanded panel at a time");
    }

    [Fact]
    public void Tablet_Unbound_ExpandingTheImposedRail_SwapsTheExpandedPanel()
    {
        // F6 r2 G1: the shell itself imposed the left rail (both panels were open) and the host binds
        // NOTHING — no CollapsedPanels, no handler. The expand button must still swap the panels.
        var cut = RenderShell(TmLayoutMode.Tablet);
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("right");

        cut.Find(".tm-editor-shell__rail--left > button").Click();

        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("left",
            "the swap applies even when the host never applies CollapsedPanelsChanged");
        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1);
        cut.Find(".tm-editor-shell__rail--right").Should().NotBeNull("the other panel is now the rail");
        cut.FindAll(".tm-editor-shell__rail--left").Should().BeEmpty();

        // ... and back: the swap is repeatable.
        cut.Find(".tm-editor-shell__rail--right > button").Click();
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("right");
        cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull();
    }

    [Fact]
    public void Tablet_Unbound_ExpandingTheRail_RaisesTheNewCollapsedSetOnce_AndSurvivesAHostRerender()
    {
        var raised = new List<EditorShellPanel>();
        var cut = RenderShell(TmLayoutMode.Tablet, p => p.Add(x => x.CollapsedPanelsChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, v => raised.Add(v))));
        raised.Clear();

        cut.Find(".tm-editor-shell__rail--left > button").Click();
        cut.Render(p => p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body 2")));

        raised.Should().Equal(new[] { EditorShellPanel.Right }, "one raise for the swap, no duplicate from the next parameter pass");
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("left",
            "an unrelated host re-render must not undo the user's swap");
    }
    [Fact]
    public void Tablet_OpeningAHiddenPanelFromItsToggle_RailsTheOther()
    {
        EditorShellPanel? raised = null;
        bool? leftOpen = null;
        var cut = RenderShell(TmLayoutMode.Tablet, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
            p.Add(x => x.CollapsedPanelsChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v));
        });

        cut.Find(".tm-editor-shell__panel-toggle--left").Click();

        leftOpen.Should().Be(true);
        raised.Should().Be(EditorShellPanel.Right, "the right panel (expanded) becomes the rail");
    }

    [Fact]
    public void Tablet_HostOpensRight_RightIsExpandedAndLeftRailed()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Tablet, p =>
        {
            p.Add(x => x.RightOpen, false);
            p.Add(x => x.CollapsedPanelsChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v));
        });
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("left");

        cut.Render(p => p.Add(x => x.RightOpen, true));

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1);
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("right");
        cut.Find(".tm-editor-shell__rail--left").Should().NotBeNull();
        raised.Should().Be(EditorShellPanel.Left);
        cut.FindAll(".tm-drawer").Should().BeEmpty("one Escape is never needed: there is no modal");
    }

    [Fact]
    public void LeftOpenRightOpen_MeanTheSameOnDesktopAndTablet()
    {
        foreach (var mode in new[] { TmLayoutMode.Desktop, TmLayoutMode.Tablet })
        {
            var cut = RenderShell(mode, p => p.Add(x => x.LeftOpen, false));

            cut.FindAll("[data-region='left']").Should().BeEmpty($"{mode}: a hidden panel renders no content");
            var toggle = cut.Find(".tm-editor-shell__panel-toggle--left");
            toggle.GetAttribute("aria-expanded").Should().Be("false", mode.ToString());
            toggle.HasAttribute("aria-controls").Should().BeFalse($"{mode}: nothing is rendered for it to control");
        }
    }

    [Fact]
    public void LayoutFlip_DesktopToTablet_KeepsTheCanvasAndTheInspector_NoModalNoInert()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
            p.Add(x => x.CollapsedPanelsChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v)));
        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(2);

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Tablet));

        cut.FindAll("aside.tm-editor-shell__panel").Should().HaveCount(1);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.FindAll("[inert]").Should().BeEmpty();
        raised.Should().Be(EditorShellPanel.Left);
        JSInterop.Invocations.Where(i => i.Identifier.Contains("focus", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty("the flip never moves focus");
    }

    // ── Rail slots (F13) ────────────────────────────────────────────────────

    [Fact]
    public void Rail_RendersTheRailSlotUnderTheExpandButton_OnlyWhileCollapsed()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.CollapsedPanels, EditorShellPanel.Left);
            p.Add(x => x.LeftRail, builder =>
            {
                builder.OpenElement(0, "span");
                builder.AddAttribute(1, "class", "my-rail-icon");
                builder.CloseElement();
            });
            p.Add(x => x.RightRail, builder => builder.AddContent(0, "right-rail-content"));
        });

        cut.Find(".tm-editor-shell__rail--left .my-rail-icon").Should().NotBeNull();
        cut.Markup.Should().NotContain("right-rail-content", "the right panel is expanded: its rail slot is not shown");

        cut.Render(p => p.Add(x => x.CollapsedPanels, EditorShellPanel.None));
        cut.FindAll(".my-rail-icon").Should().BeEmpty();
    }

    // ── aria-controls / aria-expanded correctness (F5) ──────────────────────

    [Fact]
    public void Desktop_ExpandedToggle_ControlsTheRealAside()
    {
        var cut = RenderShell(TmLayoutMode.Desktop);

        var toggle = cut.Find(".tm-editor-shell__panel-toggle--left");
        toggle.GetAttribute("aria-expanded").Should().Be("true");
        var target = cut.Find($"#{toggle.GetAttribute("aria-controls")}");
        target.TagName.Should().Be("ASIDE");
        target.GetAttribute("data-region").Should().Be("left");
        cut.FindAll("[data-panel-anchor]").Should().BeEmpty("the hidden anchor spans are gone");
        cut.FindAll("span[hidden]").Should().BeEmpty();
    }

    [Fact]
    public void Desktop_RailButton_IsCollapsedAndOmitsAriaControls()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.CollapsedPanels, EditorShellPanel.Left));

        var button = cut.Find(".tm-editor-shell__rail--left button");
        button.GetAttribute("aria-expanded").Should().Be("false", "the panel content is not shown");
        button.HasAttribute("aria-controls").Should().BeFalse("the controlled panel is not rendered");
    }

    [Fact]
    public void Tablet_DockedToggle_ControlsTheRealAside()
    {
        var cut = RenderShell(TmLayoutMode.Tablet);

        var toggle = cut.Find(".tm-editor-shell__panel-toggle--right");
        var target = cut.Find($"#{toggle.GetAttribute("aria-controls")}");
        target.TagName.Should().Be("ASIDE");
        target.GetAttribute("data-region").Should().Be("right");
    }

    [Fact]
    public void Toggles_SurviveEveryStateTransitionWithTheRightAttributes()
    {
        // F8: the toggle helper renders inside its own region; walking every state must keep the
        // attributes of one state from leaking into the next.
        var leftOpen = true;
        var collapsed = EditorShellPanel.None;
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find(".tm-editor-shell__panel-toggle--left").GetAttribute("aria-expanded").Should().Be("true");
        collapsed = EditorShellPanel.Left;
        cut.Render(p => p.Add(x => x.CollapsedPanels, collapsed));
        var rail = cut.Find(".tm-editor-shell__rail--left button");
        rail.GetAttribute("aria-expanded").Should().Be("false");
        rail.HasAttribute("aria-controls").Should().BeFalse();

        leftOpen = false;
        cut.Render(p => p.Add(x => x.LeftOpen, leftOpen));
        var show = cut.Find(".tm-editor-shell__panel-toggle--left");
        show.GetAttribute("aria-expanded").Should().Be("false");
        show.HasAttribute("aria-controls").Should().BeFalse();

        leftOpen = true;
        collapsed = EditorShellPanel.None;
        cut.Render(p => { p.Add(x => x.LeftOpen, leftOpen); p.Add(x => x.CollapsedPanels, collapsed); });
        var hide = cut.Find(".tm-editor-shell__panel-toggle--left");
        hide.GetAttribute("aria-expanded").Should().Be("true");
        cut.Find($"#{hide.GetAttribute("aria-controls")}").TagName.Should().Be("ASIDE");
    }

    [Fact]
    public void Aside_IsNamedAfterItsPanelTitle()
    {
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find("aside[data-region='left']").GetAttribute("aria-label").Should().Be("Blocks");
        cut.Find("aside[data-region='right']").GetAttribute("aria-label").Should().Be("Properties");
    }

    // ── Mobile, Sheet presentation (default): canvas + inline bottom sheet ──────

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
    public void Mobile_Sheet_IsAChildOfThePositionedStage_NotOfAnOverlayWrapper()
    {
        // F1: the wrapper that covered the stage (and ate the canvas pointer events) is gone.
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.FindAll(".tm-editor-shell__panels").Should().BeEmpty();
        cut.Find(".tm-editor-shell__sheet").ParentElement!.ClassList.Should().Contain("tm-editor-shell__stage");
        cut.Find(".tm-editor-shell__stage [data-region='canvas']").Should().NotBeNull();
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
    public void MobileSheet_TwoOpenPanels_ShowLeftRightTabsInTheSheet()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        var tablist = cut.Find(".tm-editor-shell__sheet [role='tablist']");
        var tabs = tablist.QuerySelectorAll("[role='tab']");
        tabs.Should().HaveCount(2);
        tabs[0].TextContent.Trim().Should().Be("Blocks");
        tabs[1].TextContent.Trim().Should().Be("Properties");
        tabs[0].GetAttribute("aria-selected").Should().Be("true");
        tabs[1].GetAttribute("aria-selected").Should().Be("false");

        // Each tab controls a REAL tabpanel; the inactive panel stays in the DOM (toolbox state
        // survives a tab switch) but hidden.
        foreach (var tab in tabs)
        {
            var panel = cut.Find($"#{tab.GetAttribute("aria-controls")}");
            panel.GetAttribute("role").Should().Be("tabpanel");
            panel.GetAttribute("aria-labelledby").Should().Be(tab.Id);
        }
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void MobileSheet_SelectingATab_RaisesActiveMobilePanelChanged_AndShowsThatPanel()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.ActiveMobilePanelChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v)));

        cut.FindAll("[role='tab']")[1].Click();

        raised.Should().Be(EditorShellPanel.Right);
        cut.FindAll("[role='tab']")[1].GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeTrue();
    }

    [Fact]
    public void MobileSheet_HostSetsActiveMobilePanelRight_PropertiesAreShown()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Left));
        cut.FindAll("[role='tab']")[0].GetAttribute("aria-selected").Should().Be("true");

        cut.Render(p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Right));

        cut.FindAll("[role='tab']")[1].GetAttribute("aria-selected").Should().Be("true");
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse();
    }

    [Fact]
    public void MobileSheet_OneOpenPanel_NoTablist_TitledByThatPanel()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.RightOpen, false));

        cut.FindAll(".tm-editor-shell__sheet [role='tablist']").Should().BeEmpty();
        cut.Find(".tm-editor-shell__sheet .tm-drawer__title").TextContent.Trim().Should().Be("Blocks");
        cut.Find(".tm-editor-shell__sheet").TextContent.Should().NotContain("Right props");
    }

    [Fact]
    public void MobileSheet_HasAnAccessibleName()
    {
        // F7: the inline sheet root is role=dialog; it must carry a name.
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.Find(".tm-editor-shell__sheet").GetAttribute("aria-label").Should().Be("Panels");
        cut.Find(".tm-editor-shell__sheet [role='tablist']").GetAttribute("aria-label").Should().Be("Panels");
    }

    [Fact]
    public void MobileSheet_SnapIndexIsTwoWay()
    {
        int? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.MobileSheetSnapIndex, 1);
            p.Add(x => x.MobileSheetSnapIndexChanged, EventCallback.Factory.Create<int>(this, v => raised = v));
        });
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("1",
            "the host-supplied snap is rendered");

        cut.Find(".tm-sheet__handle").KeyDown(new KeyboardEventArgs { Key = "ArrowDown" });

        raised.Should().Be(0, "a handle gesture raises the new snap to the host");
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");

        cut.Render(p => p.Add(x => x.MobileSheetSnapIndex, 0));
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");
        cut.Render(p => p.Add(x => x.MobileSheetSnapIndex, 1));
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("1",
            "a host that sets the snap (collapse/expand on a block action) is honoured");
    }

    [Fact]
    public void Mobile_ClosedSheet_RendersALabelledAffordance_WithNoAriaControls()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.LeftOpen, false);
            p.Add(x => x.RightOpen, false);
        });

        var toggle = cut.Find(".tm-editor-shell__panel-toggle--mobile");
        toggle.TextContent.Should().Contain("Blocks").And.Contain("Properties", "the closed state names what is behind it");
        toggle.GetAttribute("aria-expanded").Should().Be("false");
        toggle.HasAttribute("aria-controls").Should().BeFalse("the sheet it controls is not rendered");
    }

    // ── Mobile, Tabs presentation: Left | Canvas | Right, no sheet ──────────

    [Fact]
    public void MobileTabs_RendersAFullRegionTablist_NoSheet()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        cut.FindAll(".tm-drawer").Should().BeEmpty("the Tabs presentation has no sheet");
        var tabs = cut.Find("[role='tablist']").QuerySelectorAll("[role='tab']");
        tabs.Select(t => t.TextContent.Trim()).Should().Equal("Blocks", "Canvas", "Properties");
        tabs[1].GetAttribute("aria-selected").Should().Be("true", "the canvas is the default tab");
        cut.Find("[role='tabpanel'][data-region='canvas']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[role='tabpanel'][data-region='left']").HasAttribute("hidden").Should().BeTrue();
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeTrue();
        foreach (var tab in tabs)
        {
            cut.Find($"#{tab.GetAttribute("aria-controls")}").GetAttribute("aria-labelledby").Should().Be(tab.Id);
        }
    }

    [Fact]
    public void MobileTabs_CanvasTitle_IsCustomisable_AndLocalisedByDefault()
    {
        var custom = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs);
            p.Add(x => x.CanvasTitle, "Content");
        });
        custom.FindAll("[role='tab']")[1].TextContent.Trim().Should().Be("Content");

        var cs = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));
        cs.FindAll("[role='tab']")[1].TextContent.Trim().Should().Be("Canvas");
    }

    [Fact]
    public void MobileTabs_SelectingATab_RaisesTheCallback_AndShowsThatRegion()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs);
            p.Add(x => x.ActiveMobilePanelChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v));
        });

        cut.FindAll("[role='tab']")[2].Click();

        raised.Should().Be(EditorShellPanel.Right);
        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.Find("[role='tabpanel'][data-region='canvas']").HasAttribute("hidden").Should().BeTrue();

        cut.FindAll("[role='tab']")[1].Click();
        raised.Should().Be(EditorShellPanel.None, "the canvas tab is EditorShellPanel.None");
    }

    [Fact]
    public void MobileTabs_HostSetsActiveMobilePanel_SwitchesTheRegion()
    {
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        cut.Render(p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Right));

        cut.Find("[role='tabpanel'][data-region='right']").HasAttribute("hidden").Should().BeFalse();
        cut.FindAll("[role='tab']")[2].GetAttribute("aria-selected").Should().Be("true");
    }

    [Fact]
    public void MobileTabs_ArrowKeysRoveBetweenTabs()
    {
        EditorShellPanel? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs);
            p.Add(x => x.ActiveMobilePanelChanged, EventCallback.Factory.Create<EditorShellPanel>(this, v => raised = v));
        });

        cut.FindAll("[role='tab']")[1].KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        raised.Should().Be(EditorShellPanel.Right);
    }

    // ── Mobile chrome (F12) ─────────────────────────────────────────────────

    [Fact]
    public void Mobile_ToolbarSlot_IsHiddenWhenMobileActionsAreSet()
    {
        var withActions = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobileActions, Actions));
        withActions.FindAll("[data-region='toolbar']").Should().BeEmpty("the action bar replaces the toolbar on a phone");

        var without = RenderShell(TmLayoutMode.Mobile);
        without.Find("[data-region='toolbar']").TextContent.Should().Contain("Shell toolbar");

        var desktop = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.MobileActions, Actions));
        desktop.Find("[data-region='toolbar']").Should().NotBeNull("only a mobile layout hides the toolbar");
    }

    [Theory]
    [InlineData(MobilePanelPresentation.Sheet)]
    [InlineData(MobilePanelPresentation.Tabs)]
    public async Task CustomBreakpoints_TheMobileActionBarFollowsTheShellsResolvedMode_NotItsOwnDefaultMeasurement(MobilePanelPresentation presentation)
    {
        // F6 r2 G2: with Breakpoints(768, 1200) a 700px container is MOBILE for the shell (the Toolbar
        // slot is hidden, the bar replaces it). The internal bar must not re-measure the same
        // container with the DEFAULT thresholds (700 => tablet => bar hidden): undo/redo/preview
        // would be unreachable.
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.Breakpoints, new TmLayoutBreakpoints(768, 1200));
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.RightTitle, "Properties");
            p.Add(x => x.Toolbar, builder => builder.AddContent(0, "Shell toolbar"));
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.Right, builder => builder.AddContent(0, "Right props"));
            p.Add(x => x.MobileActions, Actions);
            p.Add(x => x.MobilePanelPresentation, presentation);
        });

        await cut.InvokeAsync(() => cut.FindComponents<TmLayoutObserver>()[0].Instance.OnLayoutModeChanged("mobile"));
        cut.Find("[data-testid='tm-editor-shell']").GetAttribute("data-layout").Should().Be("mobile");
        cut.FindAll("[data-region='toolbar']").Should().BeEmpty("the action bar replaces the toolbar on a mobile shell");

        // The bar's own observer measures the same container with the default thresholds.
        var barObserver = cut.FindComponents<TmLayoutObserver>().Single(o => o.Instance.Class?.Contains("tm-mobile-action-bar") == true);
        await cut.InvokeAsync(() => barObserver.Instance.OnLayoutModeChanged("tablet"));

        cut.FindAll(".tm-mobile-action-bar__bar").Should().HaveCount(1, "the actions must stay reachable on a mobile shell");
        cut.FindComponent<TmMobileActionBar>().Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile,
            "the shell forces its mobile-only child to the shell's resolved mode");
        cut.FindComponents<TmLayoutObserver>().Single(o => o.Instance.Class?.Contains("tm-mobile-action-bar") == true)
            .Instance.LayoutMode.Should().Be(TmLayoutMode.Mobile);
    }
    [Fact]
    public void Mobile_NoMobileActions_RendersNoBar()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }

    // ── Two-way panel state ─────────────────────────────────────────────────

    [Fact]
    public void Desktop_HiddenPanel_RendersToggleWithCollapsedStateAndNoControls()
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
        toggle.HasAttribute("aria-controls").Should().BeFalse();
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
        cut.Find(".tm-editor-shell__rail--left .tm-editor-shell__panel-toggle").Should().NotBeNull();
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

    // ── Close paths, focus restore and layout flips (the F5 pattern) ─────────

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
    public async Task Mobile_EscapeAndCloseButton_CloseThePanels_AndTheReopenToggleIsCollapsed()
    {
        var host = new Host();
        var cut = RenderControlled(TmLayoutMode.Mobile, host);

        var scope = cut.FindComponent<TmFocusScope>();
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
    public void LayoutFlip_TabletMobileTablet_NeverRaisesAModalAndKeepsTheHostsOpenState()
    {
        var host = new Host { Right = false };
        var cut = RenderControlled(TmLayoutMode.Tablet, host);
        cut.FindAll(".tm-drawer").Should().BeEmpty();
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("left");

        Sync(cut, host, TmLayoutMode.Mobile);
        Sync(cut, host, TmLayoutMode.Tablet);

        cut.FindAll(".tm-drawer").Should().BeEmpty();
        host.Left.Should().BeTrue("a flip must not close what the host opened");
        cut.Find("aside.tm-editor-shell__panel").GetAttribute("data-region").Should().Be("left");
    }

    [Fact]
    public void LayoutFlip_ResetsTheMobileSheetSnap_AndTellsTheHost()
    {
        int? raised = null;
        var cut = RenderShell(TmLayoutMode.Mobile, p =>
        {
            p.Add(x => x.MobileSheetSnapIndex, 1);
            p.Add(x => x.MobileSheetSnapIndexChanged, EventCallback.Factory.Create<int>(this, v => raised = v));
        });
        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("1");

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));
        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Mobile));

        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-snap-index").Should().Be("0");
        raised.Should().Be(0, "the host is told the snap was reset, so a bound value stays truthful");
    }

    [Fact]
    public void ProgrammaticClose_OnDesktop_RendersTheCollapsedToggle()
    {
        var host = new Host { Right = false };
        var cut = RenderControlled(TmLayoutMode.Desktop, host);

        // The host closes the panel itself — no gesture involved.
        host.Left = false;
        Sync(cut, host, TmLayoutMode.Desktop);

        cut.FindAll("[data-region='left']").Should().BeEmpty();
        cut.Find(".tm-editor-shell__panel-toggle--left").GetAttribute("aria-expanded").Should().Be("false");
    }

    // ── Focus restore never steals focus (F6) ───────────────────────────────

    [Fact]
    public void Focus_AfterAProgrammaticMobileClose_UsesFocusIfLost_NeverAnUnconditionalFocus()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        var focusIfLost = module.Setup<bool>("focusIfLost", _ => true);
        focusIfLost.SetResult(false);
        var host = new Host();
        var cut = RenderControlled(TmLayoutMode.Mobile, host);

        host.Left = false;
        host.Right = false;
        Sync(cut, host, TmLayoutMode.Mobile);

        cut.WaitForAssertion(() => focusIfLost.Invocations.Should().NotBeEmpty());
        var target = focusIfLost.Invocations.Last().Arguments[0] as string;
        target.Should().Be(cut.Find(".tm-editor-shell__panel-toggle--mobile").Id,
            "the restore targets the panels toggle, through the helper that checks focus was lost");
        JSInterop.Invocations.Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Should().BeEmpty("an unconditional element.FocusAsync would steal a focus the user placed elsewhere");
    }

    [Fact]
    public void MobileSheet_DisablesTheTrapsOwnFocusRestore_TheShellRestoresThroughFocusIfLost()
    {
        // The trap restores during disposal, BEFORE the panels toggle exists in the DOM, so it
        // falls back to whatever was focused when the sheet opened (the page heading) and then
        // blocks the shell's own focusIfLost. The shell owns the restore: one path, no race.
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.Find(".tm-editor-shell__sheet").GetAttribute("data-restore-focus").Should().Be("false");
    }
    [Fact]
    public void Focus_AfterAStripCollapse_UsesFocusIfLostOnTheRailButton()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        var focusIfLost = module.Setup<bool>("focusIfLost", _ => true);
        focusIfLost.SetResult(true);
        var collapsed = EditorShellPanel.None;
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.CollapsedPanelsChanged,
            EventCallback.Factory.Create<EditorShellPanel>(this, v => collapsed = v)));

        cut.Find(".tm-editor-shell__panel-toggle--left").Click();
        collapsed.Should().Be(EditorShellPanel.Left);
        cut.Render(p => p.Add(x => x.CollapsedPanels, collapsed));

        cut.WaitForAssertion(() => focusIfLost.Invocations.Should().NotBeEmpty());
        (focusIfLost.Invocations.Last().Arguments[0] as string).Should().Be(
            cut.Find(".tm-editor-shell__rail--left button").Id);
    }

    // ── F6 r2 G4: focus never drops to <body> when the SHELL removes the focused element ──

    private BunitJSModuleInterop FocusModule(bool focusWithin = false)
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        module.Setup<bool>("focusIfLost", _ => true).SetResult(true);
        module.Setup<bool>("focusWithin", _ => true).SetResult(focusWithin);
        return module;
    }

    private static IReadOnlyList<JSRuntimeInvocation> FocusIfLostCalls(BunitJSModuleInterop module)
        => module.Invocations.Where(i => i.Identifier == "focusIfLost").ToList();

    [Fact]
    public void Mobile_ReopeningFromTheClosedBar_MovesFocusToTheSelectedSheetTab_ThroughFocusIfLost()
    {
        var module = FocusModule();
        var host = new Host { Left = false, Right = false };
        var cut = RenderControlled(TmLayoutMode.Mobile, host);
        cut.Find(".tm-editor-shell__panel-toggle--mobile").Click();
        Sync(cut, host, TmLayoutMode.Mobile);

        var selectedTab = cut.Find(".tm-editor-shell__sheet [role='tab'][aria-selected='true']");
        cut.WaitForAssertion(() => FocusIfLostCalls(module).Should().NotBeEmpty());
        FocusIfLostCalls(module).Last().Arguments[0].Should().Be(selectedTab.Id,
            "the closed bar unmounts with focus on it: focus is lost by construction, so it moves to the selected sheet tab");
    }

    [Fact]
    public void Mobile_ReopeningWithOnePanel_MovesFocusToTheSheetHeading()
    {
        var module = FocusModule();
        var leftOpen = false;
        var cut = Render<TmEditorShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Mobile);
            p.Add(x => x.LeftTitle, "Blocks");
            p.Add(x => x.Left, builder => builder.AddContent(0, "Left tools"));
            p.Add(x => x.Canvas, builder => builder.AddContent(0, "Canvas body"));
            p.Add(x => x.LeftOpen, leftOpen);
            p.Add(x => x.LeftOpenChanged, EventCallback.Factory.Create<bool>(this, v => leftOpen = v));
        });
        cut.Find(".tm-editor-shell__panel-toggle--mobile").Click();
        cut.Render(p => p.Add(x => x.LeftOpen, leftOpen));

        var heading = cut.Find(".tm-editor-shell__sheet .tm-drawer__title");
        heading.GetAttribute("tabindex").Should().Be("-1", "a heading can take programmatic focus");
        cut.WaitForAssertion(() => FocusIfLostCalls(module).Should().NotBeEmpty());
        FocusIfLostCalls(module).Last().Arguments[0].Should().Be(heading.Id);
    }

    [Fact]
    public void Tabs_HostSetsActiveMobilePanel_FocusMovesToTheNewTab_OnlyIfItWasInTheHiddenPanelOrLost()
    {
        var module = FocusModule();
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        cut.Render(p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Right));

        cut.WaitForAssertion(() => FocusIfLostCalls(module).Should().NotBeEmpty());
        var call = FocusIfLostCalls(module).Last();
        call.Arguments[0].Should().Be(cut.FindAll("[role='tab']")[2].Id, "focus follows to the Properties tab");
        call.Arguments[1].Should().Be(cut.Find("[role='tabpanel'][data-region='canvas']").Id,
            "the container is the panel being hidden: focus elsewhere (a host button) is never stolen");
    }

    [Fact]
    public void Tabs_ClickingATab_ArmsNoFocusRestore_TheFocusIsOnTheTabAlready()
    {
        var module = FocusModule();
        var cut = RenderShell(TmLayoutMode.Mobile, p => p.Add(x => x.MobilePanelPresentation, MobilePanelPresentation.Tabs));

        cut.FindAll("[role='tab']")[2].Click();
        cut.Render(p => p.Add(x => x.ActiveMobilePanel, EditorShellPanel.Right));

        FocusIfLostCalls(module).Should().BeEmpty("a user-selected tab (and its host echo) has focus already");
    }

    [Fact]
    public void LayoutFlip_DesktopToTablet_FocusInsideThePanelThatGetsRailed_MovesToItsRailButton()
    {
        var module = FocusModule(focusWithin: true);
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Tablet));

        cut.WaitForAssertion(() => FocusIfLostCalls(module).Should().NotBeEmpty());
        FocusIfLostCalls(module).Last().Arguments[0].Should().Be(cut.Find(".tm-editor-shell__rail--left > button").Id,
            "the focused toolbox element unmounted with its panel: the rail's expand button takes focus");
    }

    [Fact]
    public void LayoutFlip_DesktopToTablet_FocusElsewhere_IsNotMoved()
    {
        var module = FocusModule();
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Tablet));

        FocusIfLostCalls(module).Should().BeEmpty("focus was not inside the panel that got railed (page load: body)");
    }
}
