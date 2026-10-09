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
        new TmActionItem { Id = "save", Label = "Save", OnClick = EventCallback.Factory.Create<object>(new object(), () => { }) },
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

        cut.SetParametersAndRender(p => p.Add(x => x.LeftOpen, leftOpen));
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

        cut.SetParametersAndRender(p => p.Add(x => x.RightOpen, rightOpen));
        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--right");
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
        tablist.FindAll("[role='tab']").Should().HaveCount(2);
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
        var rail = cut.Find(".tm-editor-shell__rail--left");
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

    [Fact]
    public void Desktop_NoMobileActions_RendersNoBar()
    {
        var cut = RenderShell(TmLayoutMode.Mobile);

        cut.FindAll(".tm-mobile-action-bar__bar").Should().BeEmpty();
    }
}
