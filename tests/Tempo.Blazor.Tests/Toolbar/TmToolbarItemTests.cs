using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Toolbar;

/// <summary>
/// F14/C1: <see cref="TmToolbarItem"/> — a NON-button control (select, colour picker, toggle, dropdown) that
/// can leave a <c>Overflow=Menu</c> bar like a <see cref="TmToolbarButton"/>: the wrapper is the measured item,
/// it collapses exactly like a button (out of flow, invisible, inert), and in "More" it is offered as plain
/// menu entries — one entry (<c>MenuLabel</c> + <c>OnMenuInvoke</c>) or a labelled group (<c>MenuItems</c>).
/// A free template inside the menu is rejected (a select in role=menu is invalid ARIA), so the markup contract
/// is what these tests pin. Like the overflow tests, the fit count is driven directly.
/// </summary>
public class TmToolbarItemTests : LocalizationTestBase
{
    private const string ToolbarModule = "./_content/Tempo.Blazor/js/tm-toolbar.js";

    private BunitJSModuleInterop SetupModule()
    {
        var module = JSInterop.SetupModule(ToolbarModule);
        module.SetupVoid("attach", _ => true).SetVoidResult();
        module.SetupVoid("detach", _ => true).SetVoidResult();
        return module;
    }

    private static RenderFragment Item(
        string menuLabel,
        ToolbarButtonPriority priority = ToolbarButtonPriority.Secondary,
        EventCallback onMenuInvoke = default,
        bool menuKeepOpen = false,
        IReadOnlyList<TmActionItem>? menuItems = null,
        bool disabled = false,
        RenderFragment? childContent = null) => builder =>
    {
        builder.OpenComponent<TmToolbarItem>(0);
        builder.AddComponentParameter(0, nameof(TmToolbarItem.MenuLabel), menuLabel);
        builder.AddComponentParameter(1, nameof(TmToolbarItem.Priority), priority);
        builder.AddComponentParameter(2, nameof(TmToolbarItem.OnMenuInvoke), onMenuInvoke);
        builder.AddComponentParameter(3, nameof(TmToolbarItem.MenuKeepOpen), menuKeepOpen);
        builder.AddComponentParameter(4, nameof(TmToolbarItem.MenuItems), menuItems);
        builder.AddComponentParameter(5, nameof(TmToolbarItem.Disabled), disabled);
        builder.AddComponentParameter(6, nameof(TmToolbarItem.ChildContent),
            childContent ?? ((RenderFragment)(inner => inner.AddMarkupContent(0, "<select aria-label='font'><option>12</option></select>"))));
        builder.CloseComponent();
    };

    private static RenderFragment ItemWithControl(RenderFragment childContent, string menuLabel,
        ToolbarButtonPriority priority = ToolbarButtonPriority.Secondary,
        IReadOnlyList<TmActionItem>? menuItems = null, EventCallback onMenuInvoke = default) => builder =>
    {
        builder.OpenComponent<TmToolbarItem>(0);
        builder.AddComponentParameter(0, nameof(TmToolbarItem.ChildContent), childContent);
        builder.AddComponentParameter(1, nameof(TmToolbarItem.MenuLabel), menuLabel);
        builder.AddComponentParameter(2, nameof(TmToolbarItem.Priority), priority);
        builder.AddComponentParameter(3, nameof(TmToolbarItem.MenuItems), menuItems);
        builder.AddComponentParameter(4, nameof(TmToolbarItem.OnMenuInvoke), onMenuInvoke);
        builder.CloseComponent();
    };

    private static RenderFragment Buttons(params (string Text, ToolbarButtonPriority Priority)[] specs) => builder =>
    {
        for (var i = 0; i < specs.Length; i++)
        {
            builder.OpenComponent<TmToolbarButton>(i);
            builder.AddComponentParameter(0, nameof(TmToolbarButton.Text), specs[i].Text);
            builder.AddComponentParameter(1, nameof(TmToolbarButton.Icon), "plus");
            builder.AddComponentParameter(2, nameof(TmToolbarButton.Priority), specs[i].Priority);
            builder.SetKey(specs[i].Text);
            builder.CloseComponent();
        }
    };

    private IRenderedComponent<TmToolbar> RenderToolbar(
        RenderFragment content,
        ToolbarOverflow overflow = ToolbarOverflow.Menu,
        Action<ComponentParameterCollectionBuilder<TmToolbar>>? configure = null)
        => Render<TmToolbar>(p =>
        {
            p.Add(c => c.Overflow, overflow)
             .Add(c => c.ChildContent, content)
             .AddCascadingValue(TmLayoutScopes.Viewport, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop));
            configure?.Invoke(p);
        });

    private static IReadOnlyList<string> MenuLabels(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[role='menu'] [role='menuitem']").Select(i => i.TextContent.Trim()).ToList();

    private static string[] ItemIdsInDomOrder(IRenderedComponent<TmToolbar> cut)
        => cut.FindAll("[data-tm-toolbar-item]").Select(i => i.GetAttribute("data-tm-toolbar-item")!).ToArray();

    private static void OpenMenu(IRenderedComponent<TmToolbar> cut) => cut.Find("button.tm-toolbar-more").Click();

    [Fact]
    public void Item_InOverflowToolbar_RendersMeasuredWrapper()
    {
        SetupModule();
        var cut = RenderToolbar(Item("Font", onMenuInvoke: EventCallback.Factory.Create(this, () => { })));

        var wrapper = cut.Find(".tm-toolbar-item[data-tm-toolbar-item]");
        wrapper.GetAttribute("data-rank").Should().Be("1", "Secondary ranks 1 like a button");
        wrapper.GetAttribute("data-pin").Should().Be("auto");
    }

    [Fact]
    public void Item_Collapsed_IsInertHidden_AndOfferedInMore()
    {
        SetupModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, Item("Font", onMenuInvoke: EventCallback.Factory.Create(this, () => { })));
        });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(1));

        var wrapper = cut.Find(".tm-toolbar-item");
        wrapper.ClassList.Should().Contain("tm-toolbar-item--collapsed");
        wrapper.HasAttribute("inert").Should().BeTrue("a collapsed copy must not be focusable or clickable");
        wrapper.GetAttribute("aria-hidden").Should().Be("true");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Font");
    }

    [Fact]
    public void Item_MenuEntry_InvokesOnMenuInvoke_Once_AndClosesTheMenu()
    {
        SetupModule();
        var invokes = 0;
        var cut = RenderToolbar(Item("Font",
            priority: ToolbarButtonPriority.OverflowOnly,
            onMenuInvoke: EventCallback.Factory.Create(this, () => invokes++)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => invokes.Should().Be(1));
        cut.WaitForAssertion(() => cut.FindAll("[role='menu']").Should().BeEmpty());
    }

    [Fact]
    public void Item_MenuKeepOpen_LeavesTheMenuOpen()
    {
        SetupModule();
        var invokes = 0;
        var cut = RenderToolbar(Item("Font",
            priority: ToolbarButtonPriority.OverflowOnly,
            menuKeepOpen: true,
            onMenuInvoke: EventCallback.Factory.Create(this, () => invokes++)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => invokes.Should().Be(1));
        cut.FindAll("[role='menu']").Should().HaveCount(1,
            "KeepMenuOpen: the surface the action opens paints above the still-open menu");
    }

    [Fact]
    public void Item_WithMenuItems_RendersOneLabelledGroup_InItemsOrder()
    {
        SetupModule();
        var sizes = new List<TmActionItem>
        {
            new() { Label = "8" },
            new() { Label = "10", Checked = true },
            new() { Label = "12" },
        };
        var cut = RenderToolbar(Item("Font size", ToolbarButtonPriority.OverflowOnly, menuItems: sizes));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);

        var group = cut.Find("[role='menu'] [role='group']");
        group.GetAttribute("aria-label").Should().Be("Font size");
        var entries = group.QuerySelectorAll("[role='menuitem'], [role='menuitemcheckbox']");
        entries.Select(e => e.TextContent.Trim()).Should().Equal(new[] { "8", "10", "12" }, "the written order");
        entries.Single(e => e.TextContent.Trim() == "10").GetAttribute("aria-checked").Should().Be("true");
    }

    [Fact]
    public void Item_WithoutMenuRepresentation_IsPinned_NeverCollapsesOrEntersTheMenu()
    {
        // An item with neither MenuLabel nor MenuItems has no menu representation: it behaves as
        // Pinned whatever its Priority says and never leaves the bar. The button next to it still
        // overflows normally at a zero fit.
        SetupModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, ItemWithControl(
                inner => inner.AddMarkupContent(0, "<button type='button'>ctl</button>"),
                menuLabel: null!,
                priority: ToolbarButtonPriority.Secondary));
        });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));

        var wrapper = cut.Find(".tm-toolbar-item");
        wrapper.GetAttribute("data-pin").Should().Be("never");

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0));

        wrapper.ClassList.Should().NotContain("tm-toolbar-item--collapsed");
        wrapper.HasAttribute("inert").Should().BeFalse();
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal(new[] { "A" }, "the button overflows; the item stays on the bar");
    }

    [Fact]
    public void Item_MenuLabelWithoutOnMenuInvoke_IsPinned_NeverLeavesTheBar()
    {
        // F14 review fix: a label alone offers nothing invocable in More. Without OnMenuInvoke
        // (and without MenuItems) the item behaves as Pinned whatever its Priority says.
        SetupModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, Item("Font", ToolbarButtonPriority.Secondary,
                childContent: inner => inner.AddMarkupContent(0, "<button type='button'>ctl</button>")));
        });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));

        var wrapper = cut.Find(".tm-toolbar-item");
        wrapper.GetAttribute("data-pin").Should().Be("never");

        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0));

        wrapper.ClassList.Should().NotContain("tm-toolbar-item--collapsed");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal(new[] { "A" }, "a MenuLabel without a handler is not a menu representation");
    }

    [Fact]
    public void Item_MenuItems_FreshButEqualListPerRender_DoesNotLoopTheToolbar()
    {
        // The change snapshot compares MenuItems STRUCTURALLY: a host that rebuilds a fresh-but-
        // equal list on every render must not be read as a change — a reference comparison would
        // recompute the toolbar (and re-render every registered button) per render, a loop. One
        // item render per host render proves no Refresh-induced second render happened.
        SetupModule();
        var itemRenders = 0;
        RenderFragment content = b => b.AddContent(0, Item("Font size",
            menuItems: new List<TmActionItem> { new() { Label = "8" }, new() { Label = "10", Checked = true } },
            childContent: inner =>
            {
                itemRenders++;
                inner.AddMarkupContent(0, "<button type='button'>ctl</button>");
            }));
        var cut = RenderToolbar(content);
        var afterInitial = itemRenders;

        for (var i = 0; i < 5; i++)
        {
            cut.Render(p => p.Add(c => c.ChildContent, content));
        }

        itemRenders.Should().Be(afterInitial + 5, "a fresh-but-equal MenuItems list is not a change");
    }

    [Fact]
    public void Item_OverflowOnly_RendersOnlyTheHiddenMarker_AndLivesInMore()
    {
        SetupModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            builder.AddContent(1, Item("Font", ToolbarButtonPriority.OverflowOnly,
                onMenuInvoke: EventCallback.Factory.Create(this, () => { })));
        });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(2));

        var marker = cut.FindAll("[data-tm-toolbar-item]").Single(e => e.HasAttribute("hidden"));
        marker.GetAttribute("data-pin").Should().Be("always");
        cut.FindAll(".tm-toolbar-item").Should().BeEmpty("no bar wrapper for an OverflowOnly item");
        cut.FindAll("button.tm-toolbar-btn").Single().TextContent.Trim().Should().Be("A");
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Font");
    }

    [Fact]
    public void Item_Standalone_AndInOverflowNoneToolbar_RendersNoHooks()
    {
        // Standalone: no toolbar, no registration. In an Overflow=None toolbar: rendered as written,
        // no data-tm-toolbar-item / rank / pin hooks (mirrors the button contract).
        var standalone = Render<TmToolbarItem>(p => p
            .Add(c => c.MenuLabel, "Font")
            .Add(c => c.ChildContent, (RenderFragment)(b => b.AddMarkupContent(0, "<button type='button'>ctl</button>"))));
        var wrapper = standalone.Find(".tm-toolbar-item");
        wrapper.HasAttribute("data-tm-toolbar-item").Should().BeFalse();
        wrapper.HasAttribute("data-rank").Should().BeFalse();
        wrapper.HasAttribute("data-pin").Should().BeFalse();

        SetupModule();
        var cut = RenderToolbar(Item("Font"), ToolbarOverflow.None);
        var none = cut.Find(".tm-toolbar-item");
        none.HasAttribute("data-tm-toolbar-item").Should().BeFalse();
        none.HasAttribute("data-rank").Should().BeFalse();
        none.HasAttribute("data-pin").Should().BeFalse();
        cut.FindAll("button.tm-toolbar-more").Should().BeEmpty();
    }

    [Fact]
    public void MixedButtonsAndItems_MenuFollowsReportedDomOrder()
    {
        // B1 is Pinned (fixed width, never in More) so the zero fit overflows B2 and the item: the
        // menu order follows the DOM order JS reported, not the registration order.
        SetupModule();
        var cut = RenderToolbar(builder =>
        {
            builder.AddContent(0, Buttons(("B1", ToolbarButtonPriority.Pinned)));
            builder.AddContent(1, Item("I1", onMenuInvoke: EventCallback.Factory.Create(this, () => { })));
            builder.AddContent(2, Buttons(("B2", ToolbarButtonPriority.Primary)));
        });
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(3));

        var ids = ItemIdsInDomOrder(cut);
        ids[0].Should().Be(cut.Find("button.tm-toolbar-btn").GetAttribute("data-tm-toolbar-item"),
            "the pinned button's marker is written first");
        cut.InvokeAsync(() => cut.Instance.OnFitChanged(0, new[] { ids[2], ids[1], ids[0] }));

        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("B2", "I1");
        cut.FindAll("button.tm-toolbar-btn:not(.tm-toolbar-item--collapsed)").Should().HaveCount(1,
            "the pinned B1 stayed on the bar");
    }

    [Fact]
    public void ToolbarButtonNestedInAnItem_DoesNotRegisterTwice()
    {
        // A button inside an item is PART of the item: the item's wrapper is the single measured
        // item; the nested button must not register with the toolbar on its own.
        SetupModule();
        var cut = RenderToolbar(ItemWithControl(
            inner =>
            {
                inner.OpenComponent<TmToolbarButton>(0);
                inner.AddComponentParameter(1, nameof(TmToolbarButton.Text), "Nested");
                inner.AddComponentParameter(2, nameof(TmToolbarButton.Icon), "plus");
                inner.CloseComponent();
            },
            menuLabel: "Font"));
        cut.WaitForAssertion(() => cut.FindAll("[data-tm-toolbar-item]").Should().HaveCount(1,
            "exactly one marker: the item wrapper, not the nested button"));
    }

    [Fact]
    public void Item_Dispose_Unregisters()
    {
        SetupModule();
        var show = true;
        RenderFragment content = builder =>
        {
            builder.AddContent(0, Buttons(("A", ToolbarButtonPriority.Primary)));
            if (show) builder.AddContent(1, Item("Font", ToolbarButtonPriority.OverflowOnly,
                onMenuInvoke: EventCallback.Factory.Create(this, () => { })));
        };
        var cut = RenderToolbar(content);
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));
        OpenMenu(cut);
        MenuLabels(cut).Should().Equal("Font");

        show = false;
        cut.Render(p => p.Add(c => c.ChildContent, content));

        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().BeEmpty());
        cut.FindAll("[role='menu']").Should().BeEmpty("the trigger is gone with nothing left to offer");
    }

    [Fact]
    public void Item_Disabled_DisablesTheMenuEntry_TheBarControlStaysTheHosts()
    {
        SetupModule();
        var invokes = 0;
        var cut = RenderToolbar(Item("Font",
            priority: ToolbarButtonPriority.OverflowOnly,
            disabled: true,
            onMenuInvoke: EventCallback.Factory.Create(this, () => invokes++)));
        cut.WaitForAssertion(() => cut.FindAll("button.tm-toolbar-more").Should().HaveCount(1));

        OpenMenu(cut);
        var entry = cut.Find("[role='menu'] [role='menuitem']");
        entry.HasAttribute("disabled").Should().BeTrue();
        entry.Click();
        invokes.Should().Be(0, "a disabled entry does not invoke");
    }
}
