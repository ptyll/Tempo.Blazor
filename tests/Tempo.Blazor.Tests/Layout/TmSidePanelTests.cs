using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// TDD tests for <see cref="TmSidePanel"/> — the standalone responsive inspector that docks
/// in-flow on a desktop container and composes <c>TmDrawer</c> (promoted, modal) as a side or
/// bottom sheet on narrower layouts.
/// </summary>
public class TmSidePanelTests : LocalizationTestBase
{
    private IRenderedComponent<TmSidePanel> RenderPanel(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmSidePanel>>? extra = null)
    {
        return Render<TmSidePanel>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Open, true);
            p.AddChildContent("Panel body");
            extra?.Invoke(p);
        });
    }

    // ── Auto presentation across the three modes ────────────────────────────

    [Fact]
    public void Auto_DockedOnDesktop_SideSheetOnTablet_BottomSheetOnMobile()
    {
        var desktop = RenderPanel(TmLayoutMode.Desktop);
        desktop.FindAll(".tm-side-panel--docked").Should().HaveCount(1);
        desktop.FindAll(".tm-drawer").Should().BeEmpty();

        var tablet = RenderPanel(TmLayoutMode.Tablet);
        tablet.FindAll(".tm-side-panel--docked").Should().BeEmpty();
        var sideSheet = tablet.Find(".tm-drawer");
        sideSheet.ClassList.Should().Contain("tm-drawer--right");
        sideSheet.ClassList.Should().Contain("tm-side-panel-sheet");

        var mobile = RenderPanel(TmLayoutMode.Mobile);
        mobile.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--bottom");
    }

    [Fact]
    public void Auto_SheetIsModal_AndPromoted()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet);

        var root = cut.Find(".tm-drawer");
        root.GetAttribute("popover").Should().Be("manual");
        root.GetAttribute("aria-modal").Should().Be("true");
    }

    // ── Forced presentations ────────────────────────────────────────────────

    [Fact]
    public void Docked_Forced_RendersDockedAtEveryWidth()
    {
        foreach (var mode in new[] { TmLayoutMode.Desktop, TmLayoutMode.Tablet, TmLayoutMode.Mobile })
        {
            var cut = RenderPanel(mode, p => p.Add(x => x.Presentation, SidePanelPresentation.Docked));
            cut.FindAll(".tm-side-panel--docked").Should().HaveCount(1, $"{mode} must stay docked");
            cut.FindAll(".tm-drawer").Should().BeEmpty();
        }
    }

    [Fact]
    public void Sheet_Forced_RendersSheetAtEveryWidth()
    {
        var desktop = RenderPanel(TmLayoutMode.Desktop, p => p.Add(x => x.Presentation, SidePanelPresentation.Sheet));
        desktop.FindAll(".tm-drawer").Should().HaveCount(1, "forced Sheet stays a sheet on desktop");
    }

    // ── Open / two-way ──────────────────────────────────────────────────────

    [Fact]
    public void Closed_RendersNothing()
    {
        var cut = Render<TmSidePanel>(p => p
            .Add(x => x.LayoutMode, TmLayoutMode.Desktop)
            .Add(x => x.Open, false)
            .AddChildContent("Panel body"));

        cut.FindAll(".tm-side-panel").Should().BeEmpty();
        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void Docked_CloseButton_FiresOpenChangedFalse()
    {
        bool? reported = null;
        var cut = RenderPanel(TmLayoutMode.Desktop, p => p
            .Add(x => x.OpenChanged, EventCallback.Factory.Create<bool>(this, v => reported = v)));

        cut.Find(".tm-side-panel__close").Click();

        reported.Should().Be(false);
    }

    [Fact]
    public void Sheet_CloseButton_FiresOpenChangedFalse()
    {
        bool? reported = null;
        var cut = RenderPanel(TmLayoutMode.Tablet, p => p
            .Add(x => x.OpenChanged, EventCallback.Factory.Create<bool>(this, v => reported = v)));

        cut.Find(".tm-drawer__close").Click();

        reported.Should().Be(false);
    }

    [Fact]
    public void Sheet_AdoptsOpenParameterChange()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet);
        cut.FindAll(".tm-drawer").Should().HaveCount(1);

        cut.Render(p => p.Add(x => x.Open, false));

        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    // ── Title and header actions ────────────────────────────────────────────

    [Fact]
    public void Title_RendersInDockedHeader()
    {
        var cut = RenderPanel(TmLayoutMode.Desktop, p => p.Add(x => x.Title, "Event detail"));

        cut.Find(".tm-side-panel__title").TextContent.Should().Contain("Event detail");
    }

    [Fact]
    public void Title_RendersInSheetHeader()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet, p => p.Add(x => x.Title, "Event detail"));

        cut.Find(".tm-drawer__title").TextContent.Should().Contain("Event detail");
    }

    [Fact]
    public void HeaderActions_RenderNextToTheCloseButton()
    {
        var cut = RenderPanel(TmLayoutMode.Desktop, p => p
            .Add(x => x.Title, "Detail")
            .Add(x => x.HeaderActions, builder =>
            {
                builder.OpenElement(0, "button");
                builder.AddAttribute(1, "class", "my-action");
                builder.AddContent(2, "Act");
                builder.CloseElement();
            }));

        cut.Find(".tm-side-panel__header-actions .my-action").Should().NotBeNull();
    }

    // ── Resolution contract ─────────────────────────────────────────────────

    [Fact]
    public void Sheet_RestoreFocusTargetId_IsForwardedToTheFocusScope()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet, p => p.Add(x => x.RestoreFocusTargetId, "open-inspector"));

        cut.Find(".tm-drawer").GetAttribute("data-restore-target").Should().Be("open-inspector");
    }

    [Fact]
    public async Task Sheet_Escape_RaisesOpenChangedFalse()
    {
        bool? last = null;
        var cut = RenderPanel(TmLayoutMode.Tablet, p =>
            p.Add(x => x.OpenChanged, EventCallback.Factory.Create<bool>(this, v => last = v)));

        var scope = cut.FindComponent<Tempo.Blazor.Components.Feedback.TmFocusScope>();
        await cut.InvokeAsync(() => scope.Instance.HandleFocusTrapEscapeAsync());

        last.Should().BeFalse("Escape funnels through the controlled OpenChanged");
    }

    [Fact]
    public void Sheet_Backdrop_RaisesOpenChangedFalse()
    {
        bool? last = null;
        var cut = RenderPanel(TmLayoutMode.Tablet, p =>
            p.Add(x => x.OpenChanged, EventCallback.Factory.Create<bool>(this, v => last = v)));

        cut.Find(".tm-drawer__overlay").Click();

        last.Should().BeFalse();
    }
    [Fact]
    public void ForcedMode_NeverMeasures_ResolvesWithoutDom()
    {
        // A forced mode must not touch JS (no observer import attempt fails the render) — the
        // panel renders entirely from the parameter.
        var cut = RenderPanel(TmLayoutMode.Tablet);

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("tablet");
    }

    // ── F4: the sheet's geometry follows the VIEWPORT, the dock-vs-sheet choice the container ──

    [Fact]
    public void Auto_MobileContainerOnDesktopViewport_RendersRightSideSheet()
    {
        var cut = Render<TmSidePanel>(p => p
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .Add(x => x.Open, true)
            .AddChildContent("Panel body"));

        cut.FindAll(".tm-side-panel--docked").Should().BeEmpty("a mobile container never docks");
        var sheet = cut.Find(".tm-drawer");
        sheet.ClassList.Should().Contain("tm-drawer--right",
            "a desktop viewport has room for a side sheet even when the container is narrow");
        sheet.ClassList.Should().NotContain("tm-drawer--bottom");
    }

    [Fact]
    public void Auto_DesktopContainerOnMobileViewport_StaysDocked_AndASheetWouldBeBottom()
    {
        var docked = Render<TmSidePanel>(p => p
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(x => x.Open, true)
            .AddChildContent("Panel body"));
        docked.FindAll(".tm-side-panel--docked").Should().HaveCount(1);

        var sheet = Render<TmSidePanel>(p => p
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(x => x.Presentation, SidePanelPresentation.Sheet)
            .Add(x => x.Open, true)
            .AddChildContent("Panel body"));
        sheet.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--bottom");
    }

    [Fact]
    public void Auto_WithoutAViewportScope_FallsBackToTheContainer()
    {
        var cut = Render<TmSidePanel>(p => p
            .AddCascadingValue(new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile))
            .Add(x => x.Open, true)
            .AddChildContent("Panel body"));

        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--bottom");
    }

    [Fact]
    public void ForcedMode_BeatsTheViewportScope_AndIsForwardedToTheDrawer()
    {
        var cut = Render<TmSidePanel>(p => p
            .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop))
            .Add(x => x.LayoutMode, TmLayoutMode.Mobile)
            .Add(x => x.Open, true)
            .AddChildContent("Panel body"));

        var sheet = cut.Find(".tm-drawer");
        sheet.ClassList.Should().Contain("tm-drawer--bottom");
        sheet.GetAttribute("data-layout").Should().Be("mobile");
    }

    // ── F7 / F14: names, Side, FooterContent, header gap ────────────────────

    [Fact]
    public void Docked_Aside_IsLabelledByItsTitle()
    {
        var cut = RenderPanel(TmLayoutMode.Desktop, p => p.Add(x => x.Title, "Event detail"));

        var aside = cut.Find("aside.tm-side-panel");
        var labelledBy = aside.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find($"#{labelledBy}").TextContent.Should().Contain("Event detail");
    }

    [Fact]
    public void Docked_WithoutTitle_GetsALocalisedDefaultName()
    {
        var cut = RenderPanel(TmLayoutMode.Desktop);

        cut.Find("aside.tm-side-panel").GetAttribute("aria-label").Should().Be("Side panel");
    }

    [Fact]
    public void Sheet_WithoutTitle_GetsALocalisedDefaultName()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet);

        cut.Find(".tm-drawer").GetAttribute("aria-label").Should().Be("Side panel");
    }

    [Fact]
    public void Side_Left_MakesTheSideSheetSlideInFromTheLeft()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet, p => p.Add(x => x.Side, SidePanelSide.Left));

        cut.Find(".tm-drawer").ClassList.Should().Contain("tm-drawer--left");
    }

    [Fact]
    public void Side_IsReflectedOnTheDockedRoot()
    {
        var left = RenderPanel(TmLayoutMode.Desktop, p => p.Add(x => x.Side, SidePanelSide.Left));
        left.Find("aside.tm-side-panel").GetAttribute("data-side").Should().Be("left");

        var right = RenderPanel(TmLayoutMode.Desktop);
        right.Find("aside.tm-side-panel").GetAttribute("data-side").Should().Be("right");
    }

    [Fact]
    public void FooterContent_RendersBelowTheBody_InBothPresentations()
    {
        RenderFragment footer = b => { b.OpenElement(0, "button"); b.AddAttribute(1, "class", "apply"); b.AddContent(2, "Apply"); b.CloseElement(); };

        var docked = RenderPanel(TmLayoutMode.Desktop, p => p.Add(x => x.FooterContent, footer));
        docked.Find("aside.tm-side-panel .tm-side-panel__footer .apply").Should().NotBeNull();

        var sheet = RenderPanel(TmLayoutMode.Tablet, p => p.Add(x => x.FooterContent, footer));
        sheet.Find(".tm-drawer .tm-drawer__footer .apply").Should().NotBeNull();
    }

    [Fact]
    public void NoFooter_RendersNoFooterRegion()
    {
        RenderPanel(TmLayoutMode.Desktop).FindAll(".tm-side-panel__footer").Should().BeEmpty();
    }

    [Fact]
    public void Sheet_HeaderActions_AreSeparatedFromTheTitle()
    {
        var cut = RenderPanel(TmLayoutMode.Tablet, p => p
            .Add(x => x.Title, "Event detail")
            .Add(x => x.HeaderActions, b => b.AddContent(0, "Inspect")));

        // The title and the actions share one flexible group so the gap is a CSS concern of the
        // side-panel stylesheet, not of the drawer's space-between header.
        cut.Find(".tm-drawer__header .tm-side-panel__header-actions").Should().NotBeNull();
        cut.Find(".tm-drawer__header .tm-drawer__title").Should().NotBeNull();
    }
}