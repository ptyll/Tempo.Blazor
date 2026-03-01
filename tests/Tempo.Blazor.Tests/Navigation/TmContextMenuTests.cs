using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>TDD tests for TmContextMenu + TmContextMenuItem.</summary>
public class TmContextMenuTests : LocalizationTestBase
{
    // ── Trigger ────────────────────────────────────────────

    [Fact]
    public void ContextMenu_RendersTrigger()
    {
        var cut = RenderComponent<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Actions</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").InnerHtml.Should().Contain("Actions");
    }

    [Fact]
    public void ContextMenu_ClickTrigger_OpensMenu()
    {
        var cut = RenderComponent<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Open</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        cut.FindAll("[role='menu']").Should().BeEmpty();

        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find("[role='menu']").Should().NotBeNull();
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
        var cut = RenderComponent<TmContextMenu>(p => p
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
        var cut = RenderComponent<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.IsDivider, true)));

        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find("[role='separator']").Should().NotBeNull();
    }

    [Fact]
    public void ContextMenuItem_Dangerous_HasDangerClass()
    {
        var cut = RenderComponent<TmContextMenu>(p => p
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
        var cut = RenderComponent<TmContextMenu>(p => p
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
        var cut = RenderComponent<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menuitem']").Click();

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Escape closes ──────────────────────────────────────

    [Fact]
    public void ContextMenu_Escape_ClosesMenu()
    {
        var cut = RenderComponent<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menu']").Should().NotBeNull();

        cut.Find(".tm-context-menu-wrapper").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    // ── Helper ─────────────────────────────────────────────

    private IRenderedComponent<TmContextMenu> RenderOpenMenu()
    {
        var cut = RenderComponent<TmContextMenu>(p => p
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
