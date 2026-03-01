using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>TDD tests for TmTabs + TmTabPanel.</summary>
public class TmTabsTests : LocalizationTestBase
{
    // ── Rendering ──────────────────────────────────────────

    [Fact]
    public void Tabs_RendersTabListWithRoleTablist()
    {
        var cut = RenderTabs("tab1");

        var tablist = cut.Find("[role='tablist']");
        tablist.Should().NotBeNull();
    }

    [Fact]
    public void Tabs_RendersTabHeaders()
    {
        var cut = RenderTabs("tab1");

        var tabs = cut.FindAll("[role='tab']");
        tabs.Should().HaveCount(3);
        tabs[0].TextContent.Should().Contain("First");
        tabs[1].TextContent.Should().Contain("Second");
        tabs[2].TextContent.Should().Contain("Third");
    }

    [Fact]
    public void Tabs_ActiveTab_HasAriaSelected()
    {
        var cut = RenderTabs("tab2");

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("aria-selected").Should().Be("false");
        tabs[1].GetAttribute("aria-selected").Should().Be("true");
        tabs[2].GetAttribute("aria-selected").Should().Be("false");
    }

    [Fact]
    public void Tabs_ActivePanel_HasRoleTabpanel()
    {
        var cut = RenderTabs("tab1");

        var panel = cut.Find("[role='tabpanel']");
        panel.Should().NotBeNull();
        panel.TextContent.Should().Contain("Content One");
    }

    [Fact]
    public void Tabs_OnlyActivePanel_IsRendered()
    {
        var cut = RenderTabs("tab2");

        var panels = cut.FindAll("[role='tabpanel']");
        panels.Should().HaveCount(1);
        panels[0].TextContent.Should().Contain("Content Two");
    }

    // ── Tab switching ──────────────────────────────────────

    [Fact]
    public void Tabs_ClickTab_SwitchesContent()
    {
        var activeId = "tab1";
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, v => activeId = v))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Content Two")));

        var tabs = cut.FindAll("[role='tab']");
        tabs[1].Click();

        activeId.Should().Be("tab2");
    }

    [Fact]
    public void Tabs_ClickActiveTab_DoesNothing()
    {
        int changeCount = 0;
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, _ => changeCount++))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One")));

        cut.Find("[role='tab']").Click();

        changeCount.Should().Be(0);
    }

    // ── Disabled tab ───────────────────────────────────────

    [Fact]
    public void Tabs_DisabledTab_HasAriaDisabled()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Active")
                .AddChildContent("Active"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Disabled")
                .Add(x => x.Disabled, true)
                .AddChildContent("Disabled")));

        var tabs = cut.FindAll("[role='tab']");
        tabs[1].GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void Tabs_ClickDisabledTab_DoesNotSwitch()
    {
        int changeCount = 0;
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, _ => changeCount++))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Active")
                .AddChildContent("Active"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Disabled")
                .Add(x => x.Disabled, true)
                .AddChildContent("Disabled")));

        var tabs = cut.FindAll("[role='tab']");
        tabs[1].Click();

        changeCount.Should().Be(0);
    }

    // ── Badge ──────────────────────────────────────────────

    [Fact]
    public void Tabs_Badge_RendersInHeader()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Messages")
                .Add(x => x.Badge, "5")
                .AddChildContent("Content")));

        var badge = cut.Find(".tm-tab__badge");
        badge.TextContent.Should().Contain("5");
    }

    // ── Icon ───────────────────────────────────────────────

    [Fact]
    public void Tabs_Icon_RendersInHeader()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Settings")
                .Add(x => x.Icon, "settings")
                .AddChildContent("Content")));

        var icon = cut.Find(".tm-tab__icon");
        icon.Should().NotBeNull();
    }

    // ── Variants ───────────────────────────────────────────

    [Theory]
    [InlineData(TabVariant.Line, "tm-tabs--line")]
    [InlineData(TabVariant.Pill, "tm-tabs--pill")]
    [InlineData(TabVariant.Enclosed, "tm-tabs--enclosed")]
    public void Tabs_Variant_AppliesCssClass(TabVariant variant, string expectedClass)
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.Variant, variant)
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Tab")
                .AddChildContent("Content")));

        cut.Find(".tm-tabs").ClassList.Should().Contain(expectedClass);
    }

    // ── Custom class ───────────────────────────────────────

    [Fact]
    public void Tabs_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.Class, "my-custom")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Tab")
                .AddChildContent("Content")));

        cut.Find(".tm-tabs").ClassList.Should().Contain("my-custom");
    }

    // ── Keyboard navigation ────────────────────────────────

    [Fact]
    public void Tabs_ArrowRight_MovesToNextTab()
    {
        var activeId = "tab1";
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, v => activeId = v))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Two")));

        var tablist = cut.Find("[role='tablist']");
        tablist.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" });

        activeId.Should().Be("tab2");
    }

    [Fact]
    public void Tabs_ArrowLeft_MovesToPreviousTab()
    {
        var activeId = "tab2";
        var cut = RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, v => activeId = v))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Two")));

        var tablist = cut.Find("[role='tablist']");
        tablist.KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        activeId.Should().Be("tab1");
    }

    // ── Lazy rendering ─────────────────────────────────────

    [Fact]
    public void Tabs_InactivePanel_IsNotInMarkup()
    {
        var cut = RenderTabs("tab1");

        cut.Markup.Should().Contain("Content One");
        cut.Markup.Should().NotContain("Content Two");
        cut.Markup.Should().NotContain("Content Three");
    }

    // ── Helper ─────────────────────────────────────────────

    private IRenderedComponent<TmTabs> RenderTabs(string activeId)
    {
        return RenderComponent<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Content Two"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab3")
                .Add(x => x.Title, "Third")
                .AddChildContent("Content Three")));
    }
}
