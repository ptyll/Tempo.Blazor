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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.Variant, variant)
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Tab")
                .AddChildContent("Content")));

        cut.Find(".tm-tabs").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Tabs_Wrap_AppliesWrapCssClassAlongsideVariant()
    {
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.Variant, TabVariant.Line)
            .Add(x => x.Wrap, true)
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Tab")
                .AddChildContent("Content")));

        cut.Find(".tm-tabs").ClassList.Should().Contain(new[] { "tm-tabs--line", "tm-tabs--wrap" });
    }

    [Fact]
    public void Tabs_WrapDefaultsOff_DoesNotApplyWrapClass()
    {
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "Tab")
                .AddChildContent("Content")));

        cut.Find(".tm-tabs").ClassList.Should().NotContain("tm-tabs--wrap");
    }

    // ── Custom class ───────────────────────────────────────

    [Fact]
    public void Tabs_CustomClass_IsApplied()
    {
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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
        var cut = Render<TmTabs>(p => p
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

    // ── Home / End ─────────────────────────────────────────
    //
    // These say only WHICH tab gets selected. That the DOM focus follows the selection — the other
    // half of a roving tabindex, and the half no bUnit assertion can see because bUnit renders a
    // DOM and never owns a focused element — is measured in TmTabsKeyboardFocusE2ETests.

    [Theory]
    [InlineData("tab3", "Home", "tab1")]
    [InlineData("tab1", "End", "tab3")]
    public void Tabs_HomeAndEnd_JumpToTheFirstAndLastTab(string startId, string key, string expectedId)
    {
        var activeId = startId;
        var cut = RenderKeyboardTabs(startId, v => activeId = v, disableLast: false);

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = key });

        activeId.Should().Be(
            expectedId,
            "{0} must jump to the {1} tab of the strip",
            key,
            key == "Home" ? "first" : "last");
    }

    [Fact]
    public void Tabs_End_StepsOverADisabledLastTab()
    {
        // Starts on tab1 so the expected outcome (tab2) is a MOVE: had End been left unhandled the
        // selection would stay on tab1 and this would be red, which is what stops "skips disabled"
        // from being satisfied by "does nothing".
        var activeId = "tab1";
        var cut = RenderKeyboardTabs("tab1", v => activeId = v, disableLast: true);

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "End" });

        activeId.Should().Be("tab2", "End must not stop on the disabled last tab, it scans inwards");
    }

    [Fact]
    public void Tabs_HomeFromADisabledFirstTab_LandsOnTheFirstSelectableTab()
    {
        var activeId = "tab3";
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, v => activeId = v))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .Add(x => x.Disabled, true)
                .AddChildContent("One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Two"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab3")
                .Add(x => x.Title, "Third")
                .AddChildContent("Three")));

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "Home" });

        activeId.Should().Be("tab2", "Home scans inwards from the edge instead of stopping on a disabled tab");
    }

    [Fact]
    public void Tabs_UnhandledKey_LeavesTheSelectionAlone()
    {
        var activeId = "tab2";
        var cut = RenderKeyboardTabs("tab2", v => activeId = v, disableLast: false);

        cut.Find("[role='tablist']").KeyDown(new KeyboardEventArgs { Key = "PageDown" });

        activeId.Should().Be("tab2", "only Arrow/Home/End belong to the tablist");
    }

    private IRenderedComponent<TmTabs> RenderKeyboardTabs(
        string activeId,
        Action<string> onChanged,
        bool disableLast)
        => Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, activeId)
            .Add(x => x.ActiveTabIdChanged, EventCallback.Factory.Create<string>(this, onChanged))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("One"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab2")
                .Add(x => x.Title, "Second")
                .AddChildContent("Two"))
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab3")
                .Add(x => x.Title, "Third")
                .Add(x => x.Disabled, disableLast)
                .AddChildContent("Three")));

    // ── Lazy rendering ─────────────────────────────────────

    [Fact]
    public void Tabs_InactivePanel_IsNotInMarkup()
    {
        var cut = RenderTabs("tab1");

        cut.Markup.Should().Contain("Content One");
        cut.Markup.Should().NotContain("Content Two");
        cut.Markup.Should().NotContain("Content Three");
    }

    [Fact]
    public void Tabs_AriaLabel_NamesTheTablist()
    {
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .Add(x => x.AriaLabel, "Schedule views")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .AddChildContent("Content One")));

        cut.Find("[role='tablist']").GetAttribute("aria-label").Should().Be("Schedule views");
    }

    [Fact]
    public void Tabs_TabAttributes_SplatOntoTheTabButton()
    {
        var cut = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .Add(x => x.TabAttributes, new Dictionary<string, object> { ["data-view"] = "Month" })
                .AddChildContent("Content One")));

        cut.Find("[role='tab']").GetAttribute("data-view").Should().Be("Month");
    }

    [Fact]
    public void Tabs_NullTabAttributes_LeaveTheButtonByteIdentical()
    {
        var withNull = Render<TmTabs>(p => p
            .Add(x => x.ActiveTabId, "tab1")
            .AddChildContent<TmTabPanel>(tp => tp
                .Add(x => x.Id, "tab1")
                .Add(x => x.Title, "First")
                .Add(x => x.TabAttributes, null)
                .AddChildContent("Content One")));
        var without = RenderTabs("tab1");

        // A null splat must not add, remove or rewrite an attribute. bUnit's handler id differs
        // per render, so the comparison is the attribute map with that id removed.
        AttributesOf(withNull.Find("[role='tab']")).Should().Equal(AttributesOf(without.FindAll("[role='tab']")[0]));
    }

    // ── Helper ─────────────────────────────────────────────

    private static Dictionary<string, string?> AttributesOf(AngleSharp.Dom.IElement element)
        => element.Attributes
            .Where(attribute => !attribute.Name.StartsWith("blazor:", StringComparison.Ordinal))
            .ToDictionary(attribute => attribute.Name, attribute => attribute.Value);

    private IRenderedComponent<TmTabs> RenderTabs(string activeId)
    {
        return Render<TmTabs>(p => p
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
