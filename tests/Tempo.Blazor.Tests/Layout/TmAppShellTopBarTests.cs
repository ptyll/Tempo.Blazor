using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// Fáze 10c (2.12.0): the shell's own top bar can be limited to the compact layouts
/// (<see cref="TmAppShell.TopBarVisibility"/>) so a desktop with the brand in the sidebar header needs
/// no host CSS that hides the frame's header, and the built-in <c>TmTopBar</c> receives a host brand
/// slot (<see cref="TmAppShell.BrandContent"/>) so a logo needs no custom <c>TopBar</c>. Both are
/// additive: the defaults render exactly what the shell always rendered.
/// </summary>
public class TmAppShellTopBarTests : LocalizationTestBase
{
    private IRenderedComponent<TmAppShell> RenderShell(
        TmLayoutMode mode,
        Action<ComponentParameterCollectionBuilder<TmAppShell>>? extra = null)
        => Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, mode);
            p.Add(x => x.Title, "Acme");
            p.Add(x => x.NavItems, TmAppShellTests.DefaultItems);
            extra?.Invoke(p);
        });

    // ── TopBarVisibility ────────────────────────────────────────────────────

    [Fact]
    public void TopBarVisibility_DefaultsToAlways()
    {
        new TmAppShell().TopBarVisibility.Should().Be(AppShellTopBarVisibility.Always);
        ((int)AppShellTopBarVisibility.Always).Should().Be(0, "the enum is append-only and Always is today's behaviour");
    }

    [Theory]
    [InlineData(TmLayoutMode.Desktop)]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public void Always_RendersTheBarOnEveryLayout(TmLayoutMode mode)
    {
        var cut = RenderShell(mode, p => p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.Always));

        cut.FindAll("header.tm-app-shell__topbar").Should().HaveCount(1);
        cut.Find("header").TextContent.Should().Contain("Acme");
    }

    [Fact]
    public void CompactOnly_OnDesktop_RendersNoHeaderLandmarkAtAll()
    {
        var cut = RenderShell(TmLayoutMode.Desktop,
            p => p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.CompactOnly));

        cut.FindAll("header").Should().BeEmpty("the bar is not hidden by CSS — it is not in the DOM, so no empty banner survives");
        cut.FindAll("[data-region='topbar']").Should().BeEmpty();
        cut.FindAll(".tm-topbar").Should().BeEmpty();
        cut.Find("[data-region='sidebar']").Should().NotBeNull("only the bar is conditional; the docked navigation column stays");
        cut.FindAll("main").Should().HaveCount(1);
    }

    [Theory]
    [InlineData(TmLayoutMode.Tablet)]
    [InlineData(TmLayoutMode.Mobile)]
    public void CompactOnly_OnTabletAndPhone_KeepsTheBar(TmLayoutMode mode)
    {
        var cut = RenderShell(mode, p => p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.CompactOnly));

        cut.FindAll("header.tm-app-shell__topbar").Should().HaveCount(1);
        cut.Find("header").TextContent.Should().Contain("Acme");
    }

    [Fact]
    public void CompactOnly_OnPhone_StillRendersTheHamburger()
    {
        var cut = RenderShell(TmLayoutMode.Mobile,
            p => p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.CompactOnly));

        cut.Find("[data-testid='topbar-menu-button']").GetAttribute("aria-label")
            .Should().Be("Navigation menu", "a phone must keep its only way into the navigation");
    }

    [Fact]
    public void CompactOnly_AlsoHidesAHostTopBarSlot_OnDesktop()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.CompactOnly);
            p.Add(x => x.TopBar, b => b.AddMarkupContent(0, "<span id='custom-bar'>Custom</span>"));
        });

        cut.FindAll("#custom-bar").Should().BeEmpty("the visibility rule governs the whole bar region, built-in or slot");
        cut.FindAll("header").Should().BeEmpty();
    }

    [Fact]
    public void CompactOnly_FollowsTheResolvedLayout_WhenItChanges()
    {
        var cut = RenderShell(TmLayoutMode.Desktop,
            p => p.Add(x => x.TopBarVisibility, AppShellTopBarVisibility.CompactOnly));
        cut.FindAll("header").Should().BeEmpty();

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Tablet));

        cut.FindAll("header.tm-app-shell__topbar").Should().HaveCount(1);

        cut.Render(p => p.Add(x => x.LayoutMode, TmLayoutMode.Desktop));

        cut.FindAll("header").Should().BeEmpty();
    }

    // ── BrandContent ────────────────────────────────────────────────────────

    private static RenderFragment Logo => b => b.AddMarkupContent(0, "<img class='host-logo' alt='Acme logo' src='/logo.svg' />");

    [Fact]
    public void BrandContent_RendersInTheBuiltInTopBarsBrandSlot()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p => p.Add(x => x.BrandContent, Logo));

        var brand = cut.Find("header .tm-topbar-brand");
        brand.QuerySelector("img.host-logo").Should().NotBeNull("the slot reaches the composed TmTopBar's brand area");
        brand.TextContent.Should().NotContain("Acme", "TmTopBar prefers the brand content over the plain title text");
    }

    [Fact]
    public void BrandContent_AloneIsEnoughForABar_WithoutATitle()
    {
        var cut = Render<TmAppShell>(p =>
        {
            p.Add(x => x.LayoutMode, TmLayoutMode.Desktop);
            p.Add(x => x.BrandContent, Logo);
        });

        cut.FindAll("header").Should().HaveCount(1, "a brand slot is bar content: without a bar there would be nowhere to render it");
        cut.Find("header img.host-logo").Should().NotBeNull();
    }

    [Fact]
    public void WithoutBrandContent_TheTitleStillRenders()
    {
        var cut = RenderShell(TmLayoutMode.Desktop);

        cut.Find(".tm-topbar-brand-title").TextContent.Should().Be("Acme");
    }

    [Fact]
    public void BrandContent_IsIgnoredByAHostTopBarSlot()
    {
        var cut = RenderShell(TmLayoutMode.Desktop, p =>
        {
            p.Add(x => x.BrandContent, Logo);
            p.Add(x => x.TopBar, b => b.AddMarkupContent(0, "<span id='custom-bar'>Custom</span>"));
        });

        cut.FindAll("img.host-logo").Should().BeEmpty("a host TopBar replaces the built-in bar entirely, brand slot included");
        cut.Find("#custom-bar").Should().NotBeNull();
    }
}
