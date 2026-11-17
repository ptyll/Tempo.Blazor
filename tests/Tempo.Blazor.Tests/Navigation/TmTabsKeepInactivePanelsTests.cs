using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// TDD tests for the 2.12.0 additive TmTabs parameters: <c>KeepInactivePanels</c>,
/// <c>IdPrefix</c>, <c>Overflow</c> (TabsOverflow) and <c>StickyHeader</c>.
/// All four are opt-in — every test below also pins that the DEFAULT render keeps the
/// pre-2.12 markup contract (single rendered panel, global tab-{id} ids, scroll overflow,
/// no sticky positioning).
/// </summary>
public class TmTabsKeepInactivePanelsTests : LocalizationTestBase
{
    private static RenderFragment ThreePanels(string prefix = "") => builder =>
    {
        var titles = new[] { "First", "Second", "Third" };
        for (var i = 0; i < 3; i++)
        {
            var index = i;
            builder.OpenComponent<TmTabPanel>(0);
            builder.AddAttribute(1, nameof(TmTabPanel.Id), $"{prefix}tab{i + 1}");
            builder.AddAttribute(2, nameof(TmTabPanel.Title), titles[index]);
            builder.AddAttribute(3, nameof(TmTabPanel.ChildContent), (RenderFragment)(b =>
                b.AddContent(0, $"Content {index + 1}")));
            builder.CloseComponent();
        }
    };

    private IRenderedComponent<TmTabs> Render(string activeId, Action<ComponentParameterCollectionBuilder<TmTabs>>? configure = null)
    {
        return Render<TmTabs>(p =>
        {
            p.Add(x => x.ActiveTabId, activeId);
            p.Add(x => x.ChildContent, ThreePanels());
            configure?.Invoke(p);
        });
    }

    // ── KeepInactivePanels ──────────────────────────────────

    [Fact]
    public void Default_KeepsRenderingOnlyTheActivePanel()
    {
        var cut = Render("tab2");

        cut.FindAll("[role='tabpanel']").Should().HaveCount(1);
    }

    [Fact]
    public void KeepInactivePanels_RendersEveryPanel_WithInactiveHidden()
    {
        var cut = Render("tab2", p => p.Add(x => x.KeepInactivePanels, true));

        var panels = cut.FindAll("[role='tabpanel']");
        panels.Should().HaveCount(3);
        panels[0].GetAttribute("hidden").Should().NotBeNull();
        panels[1].GetAttribute("hidden").Should().BeNull();
        panels[2].GetAttribute("hidden").Should().NotBeNull();
        panels[1].TextContent.Should().Contain("Content 2");
        panels[2].TextContent.Should().Contain("Content 3");
    }

    [Fact]
    public void KeepInactivePanels_ClickAnotherTab_SwapstheHiddenFlags()
    {
        var cut = Render<BoundTabsWrapper>(p => p.Add(x => x.KeepInactivePanels, true));

        cut.FindAll("[role='tab']")[1].Click();

        var panels = cut.FindAll("[role='tabpanel']");
        panels[0].GetAttribute("hidden").Should().NotBeNull();
        panels[1].GetAttribute("hidden").Should().BeNull();
    }

    // ── aria-controls only on RENDERED panels ───────────────

    [Fact]
    public void Default_OnlyTheActiveTabCarriesAriaControls()
    {
        var cut = Render("tab2");

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("aria-controls").Should().BeNull();
        tabs[1].GetAttribute("aria-controls").Should().Be("tabpanel-tab2");
        tabs[2].GetAttribute("aria-controls").Should().BeNull();
    }

    [Fact]
    public void KeepInactivePanels_EveryTabCarriesAriaControls()
    {
        var cut = Render("tab1", p => p.Add(x => x.KeepInactivePanels, true));

        var tabs = cut.FindAll("[role='tab']");
        tabs[0].GetAttribute("aria-controls").Should().Be("tabpanel-tab1");
        tabs[1].GetAttribute("aria-controls").Should().Be("tabpanel-tab2");
        tabs[2].GetAttribute("aria-controls").Should().Be("tabpanel-tab3");
    }

    // ── IdPrefix ────────────────────────────────────────────

    [Fact]
    public void Default_KeepsTheGlobalTabIdSchema()
    {
        var cut = Render("tab1");

        cut.Find("[role='tab']").GetAttribute("id").Should().Be("tab-tab1");
        cut.Find("[role='tabpanel']").GetAttribute("id").Should().Be("tabpanel-tab1");
        cut.Find("[role='tabpanel']").GetAttribute("aria-labelledby").Should().Be("tab-tab1");
    }

    [Fact]
    public void IdPrefix_ScopesBothIdsAndTheReferences()
    {
        var cut = Render("tab1", p => p.Add(x => x.IdPrefix, "rail"));

        var tab = cut.Find("[role='tab']");
        tab.GetAttribute("id").Should().Be("rail-tab-tab1");
        tab.GetAttribute("aria-controls").Should().Be("rail-tabpanel-tab1");
        var panel = cut.Find("[role='tabpanel']");
        panel.GetAttribute("id").Should().Be("rail-tabpanel-tab1");
        panel.GetAttribute("aria-labelledby").Should().Be("rail-tab-tab1");
    }

    // ── TabsOverflow ────────────────────────────────────────

    [Fact]
    public void Default_OverflowsByScroll_WithoutTheWrapClass()
    {
        var cut = Render("tab1");

        cut.Find(".tm-tabs").GetAttribute("class").Should().NotContain("tm-tabs--wrap");
    }

    [Fact]
    public void LegacyWrapParameter_StillAddsTheWrapClass()
    {
        var cut = Render("tab1", p => p.Add(x => x.Wrap, true));

        cut.Find(".tm-tabs").GetAttribute("class").Should().Contain("tm-tabs--wrap");
    }

    [Fact]
    public void OverflowWrap_AddsTheWrapClass()
    {
        var cut = Render("tab1", p => p.Add(x => x.Overflow, TabsOverflow.Wrap));

        cut.Find(".tm-tabs").GetAttribute("class").Should().Contain("tm-tabs--wrap");
    }

    [Fact]
    public void OverflowScroll_MatchesTheDefaultLayout_NoWrapClass()
    {
        var cut = Render("tab1", p => p.Add(x => x.Overflow, TabsOverflow.Scroll));

        var cls = cut.Find(".tm-tabs").GetAttribute("class");
        cls.Should().Contain("tm-tabs--line");
        cls.Should().NotContain("tm-tabs--wrap");
    }

    [Fact]
    public void OverflowWrap_WinsOverTheLegacyWrapFlag_WithoutLosingIt()
    {
        var cut = Render("tab1", p =>
        {
            p.Add(x => x.Wrap, true);
            p.Add(x => x.Overflow, TabsOverflow.Wrap);
        });

        cut.Find(".tm-tabs").GetAttribute("class").Should().Contain("tm-tabs--wrap");
    }

    // ── StickyHeader ────────────────────────────────────────

    [Fact]
    public void Default_RendersNoStickyClass()
    {
        var cut = Render("tab1");

        cut.Find(".tm-tabs").GetAttribute("class").Should().NotContain("tm-tabs--sticky");
    }

    [Fact]
    public void StickyHeader_AddsTheStickyClass()
    {
        var cut = Render("tab1", p => p.Add(x => x.StickyHeader, true));

        cut.Find(".tm-tabs").GetAttribute("class").Should().Contain("tm-tabs--sticky");
    }

    [Fact]
    public void StickyHeader_WithSlots_SticksTheRowNotJustTheStrip()
    {
        var cut = Render<TmTabs>(p =>
        {
            p.Add(x => x.ActiveTabId, "tab1");
            p.Add(x => x.StickyHeader, true);
            p.Add(x => x.ChildContent, ThreePanels());
            p.Add(x => x.HeaderLeading, builder => builder.AddMarkupContent(0, "<span>lead</span>"));
        });

        cut.Find(".tm-tabs__header-row").Should().NotBeNull();
        cut.Find(".tm-tabs").GetAttribute("class").Should().Contain("tm-tabs--sticky");
    }
}

/// <summary>
/// A minimal HOST that owns <c>ActiveTabId</c> (the two-way binding), so a click really moves the
/// selection through the full controlled round-trip.
/// </summary>
internal sealed class BoundTabsWrapper : ComponentBase
{
    [Parameter] public bool KeepInactivePanels { get; set; }

    private string _active = "tab1";

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<TmTabs>(0);
        builder.AddAttribute(1, nameof(TmTabs.ActiveTabId), _active);
        builder.AddAttribute(2, nameof(TmTabs.ActiveTabIdChanged),
            EventCallback.Factory.Create<string>(this, v => { _active = v; StateHasChanged(); }));
        builder.AddAttribute(3, nameof(TmTabs.KeepInactivePanels), KeepInactivePanels);
        builder.AddAttribute(4, nameof(TmTabs.ChildContent), (RenderFragment)(panels =>
        {
            var titles = new[] { "First", "Second", "Third" };
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                panels.OpenComponent<TmTabPanel>(0);
                panels.AddAttribute(1, nameof(TmTabPanel.Id), $"tab{index + 1}");
                panels.AddAttribute(2, nameof(TmTabPanel.Title), titles[index]);
                panels.AddAttribute(3, nameof(TmTabPanel.ChildContent), (RenderFragment)(b =>
                    b.AddContent(0, $"Content {index + 1}")));
                panels.CloseComponent();
            }
        }));
        builder.CloseComponent();
    }
}
