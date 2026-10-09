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

        cut.SetParametersAndRender(p => p.Add(x => x.Open, false));

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
    public void ForcedMode_NeverMeasures_ResolvesWithoutDom()
    {
        // A forced mode must not touch JS (no observer import attempt fails the render) — the
        // panel renders entirely from the parameter.
        var cut = RenderPanel(TmLayoutMode.Tablet);

        cut.Find(".tm-drawer").GetAttribute("data-layout").Should().Be("tablet");
    }
}
