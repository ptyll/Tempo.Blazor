using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>TDD tests for TmContextMenu + TmContextMenuItem.</summary>
public class TmContextMenuTests : LocalizationTestBase
{
    // ── Trigger ────────────────────────────────────────────

    [Fact]
    public void ContextMenu_RendersTrigger()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Actions</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").InnerHtml.Should().Contain("Actions");
    }

    [Fact]
    public void ContextMenu_ClickTrigger_OpensMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Open</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        cut.FindAll("[role='menu']").Should().BeEmpty();

        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find("[role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void ContextMenu_Trigger_IsKeyboardFocusable()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        var trigger = cut.Find(".tm-context-menu__trigger");
        trigger.GetAttribute("role").Should().Be("button");
        trigger.GetAttribute("tabindex").Should().Be("0");
        trigger.GetAttribute("aria-haspopup").Should().Be("menu");
        trigger.GetAttribute("aria-expanded").Should().Be("false");
    }

    [Fact]
    public void ContextMenu_Enter_OpensMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        // Enter fires on keydown — the native <button> contract the emulation mirrors.
        cut.Find(".tm-context-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "Enter" });

        cut.Find("[role='menu']").Should().NotBeNull();
        cut.Find(".tm-context-menu__trigger").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void ContextMenu_Space_OpensMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        // Space activates on KEYUP only (N162) — bUnit does not synthesize the keydown→keyup
        // chain, so both are dispatched explicitly. A keydown-fired Space toggle would flip the
        // menu open AND closed inside a single held press; the release must open it exactly once.
        var trigger = cut.Find(".tm-context-menu__trigger");
        trigger.KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll("[role='menu']").Should().BeEmpty("Space keydown must not toggle");
        trigger.KeyUp(new KeyboardEventArgs { Key = " " });

        cut.Find("[role='menu']").Should().NotBeNull();
        cut.Find(".tm-context-menu__trigger").GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void ContextMenu_RepeatedEnterKeydown_DoesNotToggle()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        // An auto-repeat Enter keydown must not toggle on its own — the guard bounds activation
        // to the real press, not to the repeat stream a held key emits (N162).
        cut.Find(".tm-context-menu__trigger").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Items ──────────────────────────────────────────────

    [Fact]
    public void ContextMenu_Items_Render()
    {
        var cut = RenderOpenMenu();

        var items = cut.FindAll("[role='menuitem']");
        items.Should().HaveCount(3);
        items[0].TextContent.Should().Contain("Edit");
        items[1].TextContent.Should().Contain("Copy");
        items[2].TextContent.Should().Contain("Delete");
    }

    [Fact]
    public void ContextMenuItem_Icon_Renders()
    {
        var cut = RenderOpenMenu();

        cut.FindAll(".tm-context-menu__item-icon").Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void ContextMenuItem_Disabled_HasDisabledAttribute()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Locked")
                .Add(x => x.Disabled, true)));

        cut.Find(".tm-context-menu__trigger").Click();

        var item = cut.Find("[role='menuitem']");
        item.ClassList.Should().Contain("tm-context-menu__item--disabled");
    }

    [Fact]
    public void ContextMenuItem_Divider_RendersAsSeparator()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.IsDivider, true)));

        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find("[role='separator']").Should().NotBeNull();
    }

    [Fact]
    public void ContextMenuItem_Dangerous_HasDangerClass()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Delete")
                .Add(x => x.IsDangerous, true)));

        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find(".tm-context-menu__item--danger").Should().NotBeNull();
    }

    // ── Click item ─────────────────────────────────────────

    [Fact]
    public void ContextMenuItem_Click_FiresOnClick()
    {
        bool clicked = false;
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")
                .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicked = true))));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menuitem']").Click();

        clicked.Should().BeTrue();
    }

    [Fact]
    public void ContextMenuItem_Click_ClosesMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menuitem']").Click();

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Escape closes ──────────────────────────────────────

    [Fact]
    public async Task ContextMenu_Escape_ClosesMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menu']").Should().NotBeNull();

        // overlay.js consumes Escape in the window capture phase in a real browser — the
        // component's dismissal path is this JSInvokable callback, not a keydown on the wrapper
        // (N169 removed the dead HandleKeyDown branch).
        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Helper ─────────────────────────────────────────────

    private IRenderedComponent<TmContextMenu> RenderOpenMenu()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Actions</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")
                .Add(x => x.Icon, "edit"))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Copy"))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Delete")
                .Add(x => x.IsDangerous, true)));

        cut.Find(".tm-context-menu__trigger").Click();
        return cut;
    }
}
