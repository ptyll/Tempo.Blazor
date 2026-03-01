using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>TDD tests for TmDrawer.</summary>
public class TmDrawerTests : LocalizationTestBase
{
    // ── Open / Closed ──────────────────────────────────────

    [Fact]
    public void Drawer_WhenClosed_RendersNothing()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, false)
            .AddChildContent("Content"));

        cut.FindAll(".tm-drawer").Should().BeEmpty();
    }

    [Fact]
    public void Drawer_WhenOpen_RendersDrawer()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("Drawer content"));

        cut.Find(".tm-drawer").Should().NotBeNull();
        cut.Markup.Should().Contain("Drawer content");
    }

    // ── ARIA roles ─────────────────────────────────────────

    [Fact]
    public void Drawer_HasRoleDialog()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("Content"));

        var dialog = cut.Find("[role='dialog']");
        dialog.Should().NotBeNull();
        dialog.GetAttribute("aria-modal").Should().Be("true");
    }

    // ── Position ───────────────────────────────────────────

    [Theory]
    [InlineData(DrawerPosition.Right, "tm-drawer--right")]
    [InlineData(DrawerPosition.Left, "tm-drawer--left")]
    public void Drawer_Position_AppliesCssClass(DrawerPosition pos, string expectedClass)
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Position, pos)
            .AddChildContent("Content"));

        cut.Find(".tm-drawer").ClassList.Should().Contain(expectedClass);
    }

    // ── Title ──────────────────────────────────────────────

    [Fact]
    public void Drawer_Title_RendersInHeader()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Title, "Edit Record")
            .AddChildContent("Content"));

        cut.Find(".tm-drawer__title").TextContent.Should().Contain("Edit Record");
    }

    // ── Width ──────────────────────────────────────────────

    [Fact]
    public void Drawer_CustomWidth_AppliesStyle()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.Width, "600px")
            .AddChildContent("Content"));

        var panel = cut.Find(".tm-drawer__panel");
        panel.GetAttribute("style").Should().Contain("600px");
    }

    // ── Close button ───────────────────────────────────────

    [Fact]
    public void Drawer_CloseButton_IsShownByDefault()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("Content"));

        cut.FindAll(".tm-drawer__close").Should().HaveCount(1);
    }

    [Fact]
    public void Drawer_CloseButton_CanBeHidden()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.ShowCloseButton, false)
            .AddChildContent("Content"));

        cut.FindAll(".tm-drawer__close").Should().BeEmpty();
    }

    [Fact]
    public void Drawer_CloseButtonClick_FiresIsOpenChanged()
    {
        bool isOpen = true;
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .AddChildContent("Content"));

        cut.Find(".tm-drawer__close").Click();

        isOpen.Should().BeFalse();
    }

    // ── Overlay ────────────────────────────────────────────

    [Fact]
    public void Drawer_Overlay_IsShownByDefault()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .AddChildContent("Content"));

        cut.FindAll(".tm-drawer__overlay").Should().HaveCount(1);
    }

    [Fact]
    public void Drawer_Overlay_CanBeHidden()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.ShowOverlay, false)
            .AddChildContent("Content"));

        cut.FindAll(".tm-drawer__overlay").Should().BeEmpty();
    }

    [Fact]
    public void Drawer_OverlayClick_ClosesDrawer()
    {
        bool isOpen = true;
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .AddChildContent("Content"));

        cut.Find(".tm-drawer__overlay").Click();

        isOpen.Should().BeFalse();
    }

    [Fact]
    public void Drawer_OverlayClick_DoesNotClose_WhenDisabled()
    {
        bool isOpen = true;
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.CloseOnOverlayClick, false)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .AddChildContent("Content"));

        cut.Find(".tm-drawer__overlay").Click();

        isOpen.Should().BeTrue();
    }

    // ── Escape key ─────────────────────────────────────────

    [Fact]
    public void Drawer_EscapeKey_ClosesDrawer()
    {
        bool isOpen = true;
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, v => isOpen = v))
            .AddChildContent("Content"));

        cut.Find("[role='dialog']").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        isOpen.Should().BeFalse();
    }

    // ── Header / Footer slots ──────────────────────────────

    [Fact]
    public void Drawer_HeaderContent_RendersInHeader()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.HeaderContent, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Custom Header</span>")))
            .AddChildContent("Body content"));

        cut.Find(".tm-drawer__header").InnerHtml.Should().Contain("Custom Header");
    }

    [Fact]
    public void Drawer_FooterContent_Renders()
    {
        var cut = RenderComponent<TmDrawer>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.FooterContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Save</button>")))
            .AddChildContent("Body"));

        cut.Find(".tm-drawer__footer").InnerHtml.Should().Contain("Save");
    }
}
