using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
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
    public void ContextMenu_TriggerTabIndex_MinusOne_LeavesTheTabOrder_ButKeepsTheMenuClickable()
    {
        // A host whose trigger sits INSIDE another focusable element (the Kanban card owns one roving
        // stop per cell) opts out of a second tab stop; the menu stays pointer-activatable.
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Actions</span>")))
            .Add(x => x.TriggerTabIndex, -1)
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        var trigger = cut.Find(".tm-context-menu__trigger");
        trigger.GetAttribute("tabindex").Should().Be("-1");
        trigger.GetAttribute("role").Should().Be("button", "the trigger keeps its menu-button semantics");

        trigger.Click();
        cut.Find("[role='menu']").Should().NotBeNull();
    }

    [Fact]
    public void ContextMenu_TriggerTabIndex_Null_OmitsTheAttribute()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Actions</span>")))
            .Add(x => x.TriggerTabIndex, null)
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").GetAttribute("tabindex").Should().BeNull();
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

    [Fact]
    public void ContextMenu_ActivationKeys_OnNativeTriggerContent_DoNotToggleMenu()
    {
        // The @Trigger fragment may carry a native <button>. Its bubbled Enter keydown /
        // Space keyup would otherwise reach the emulating container's activation on top of
        // the button's own click — the fenced trigger-content span cuts the bubble off
        // (docs/keyboard-activation-convention.md: a keyup emulation needs a keyup fence).
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button class='inner'>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        // bUnit surfaces a fully fenced event as MissingEventHandlerException: the
        // trigger-content span cuts the bubble before it reaches the emulating
        // container's handlers — exactly the barrier the convention requires. An
        // unfenced event would reach HandleTriggerKeyDown/Up and toggle the menu.
        var inner = cut.Find(".inner");

        inner.Invoking(i => i.KeyDown(new KeyboardEventArgs { Key = "Enter" }))
            .Should().Throw<MissingEventHandlerException>(
                "a bubbled Enter keydown off a native trigger child must not fire the container's activation");
        inner.Invoking(i => i.KeyUp(new KeyboardEventArgs { Key = " " }))
            .Should().Throw<MissingEventHandlerException>(
                "a bubbled Space keyup off a native trigger child must not fire the container's activation");

        cut.FindAll("[role='menu']").Should().BeEmpty(
            "a native trigger child's activation keys must never reach the emulating container");
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

        // N319: the item closes the menu and yields a render before OnClick runs, so the
        // callback lands on a later dispatcher turn than the Click() dispatch.
        cut.WaitForAssertion(() => clicked.Should().BeTrue());
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

    /// <summary>
    /// N319: the menu must close BEFORE the item's <c>OnClick</c> runs — a handler that opens a
    /// dialog would otherwise render it under the still-open top-layer popover (and the first
    /// Escape would close the menu, not the dialog). The deterministic close (2.12.0) awaits the
    /// full state transition, so the STATE is already closed when the handler runs; the DOM
    /// render flushes with the same batch, before the next paint.
    /// </summary>
    [Fact]
    public void ContextMenuItem_Click_ClosesMenuBeforeOnClick()
    {
        var menuStateDuringHandler = (bool?)null;
        IRenderedComponent<TmContextMenu>? probe = null;
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Open</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Delete")
                .Add(x => x.OnClick, EventCallback.Factory.Create(this, () =>
                {
                    menuStateDuringHandler = probe!.Instance.IsOpen;
                }))));
        probe = cut;

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find(".tm-context-menu__item").Click();

        menuStateDuringHandler.Should().BeFalse(
            "the deterministic close has flipped the state before the item handler runs");
        cut.WaitForAssertion(() => probe!.FindAll(".tm-overlay-panel").Count.Should().Be(0));
    }

    [Fact]
    public async Task FocusTriggerAsync_FocusesTheTrigger_AndIsSafeAfterDispose()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")));

        await cut.InvokeAsync(() => cut.Instance.FocusTriggerAsync().AsTask());

        var focused = JSInterop.Invocations
            .Where(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Last().Arguments.OfType<ElementReference>().First();
        var triggerRef = (ElementReference)typeof(TmContextMenu)
            .GetField("_triggerRef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        focused.Id.Should().Be(triggerRef.Id, "FocusTriggerAsync moves focus to the menu trigger");

        await cut.InvokeAsync(() => cut.Instance.DisposeAsync().AsTask());
        await cut.InvokeAsync(() => cut.Instance.FocusTriggerAsync().AsTask());
        JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Should().Be(1, "after dispose the call is a no-op, not an exception");
    }

    [Fact]
    public void TmContextMenu_Open_RendersMenu_AndIsIdempotent()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.FindAll("[role='menu']").Should().BeEmpty();
        cut.InvokeAsync(() => cut.Instance.Open());
        cut.FindAll("[role='menu']").Should().ContainSingle();
        cut.InvokeAsync(() => cut.Instance.Open());
        cut.FindAll("[role='menu']").Should().ContainSingle("a second Open does not nest another menu");
    }

    [Fact]
    public void TmContextMenu_IsOpenChanged_RaisedFromSetOpenAndCloseMenu()
    {
        var reported = new List<bool>();
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, reported.Add))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.InvokeAsync(() => cut.Instance.Open());
        reported.Should().Equal(true);

        cut.Find(".tm-context-menu__item").Click();
        cut.WaitForAssertion(() => reported.Should().Equal(true, false));
    }

    [Fact]
    public async Task ContextMenu_Escape_RaisesIsOpenChangedFalseExactlyOnce()
    {
        var reported = new List<bool>();
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .Add(x => x.IsOpenChanged, EventCallback.Factory.Create<bool>(this, reported.Add))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        await cut.InvokeAsync(() => cut.Instance.Open());
        reported.Should().Equal([true], "Open raises the callback after the menu is open");

        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        reported.Should().Equal([true, false], "Escape raises IsOpenChanged(false) exactly once");
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public async Task TmContextMenuItem_OnClick_InvokedAfterParentCloses()
    {
        var clicked = false;
        var menuOpenWhenClicked = true;
        IRenderedComponent<TmContextMenu>? cut = null;
        cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>Open</span>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")
                .Add(x => x.OnClick, EventCallback.Factory.Create(this, () =>
                {
                    menuOpenWhenClicked = cut!.Instance.IsOpen;
                    clicked = true;
                }))));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find(".tm-context-menu__item").Click();
        await cut.InvokeAsync(() => Task.Yield());

        clicked.Should().BeTrue("closing the menu must not drop the item's OnClick");
        menuOpenWhenClicked.Should().BeFalse("the parent closes before the item handler runs");
    }

    // ── TriggerAriaLabel + AsChild (Fáze 4) ──────────────────

    [Fact]
    public void ContextMenu_TriggerAriaLabel_NamesTheTrigger()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<span>⋯</span>")))
            .Add(x => x.TriggerAriaLabel, "Row actions")
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").GetAttribute("aria-label").Should().Be("Row actions");
    }

    [Fact]
    public void ContextMenu_WithoutTriggerAriaLabel_TheTriggerCarriesNoAriaLabel()
    {
        var cut = RenderOpenMenu();

        cut.Find(".tm-context-menu__trigger").GetAttribute("aria-label").Should().BeNull();
    }

    [Fact]
    public void ContextMenu_AsChild_RendersNoEmulatingTrigger()
    {
        // The wrapper is presentational: no div[role=button] around the host's own
        // button — the nested-interactive-element defect AsChild exists to fix.
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.AsChild, true)
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button class='as-child-trigger'>⋯</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.FindAll(".tm-context-menu__trigger").Should().BeEmpty();
        cut.Find(".as-child-trigger").Should().NotBeNull();
        cut.FindAll("[role='menu']").Should().BeEmpty();
    }

    [Fact]
    public void ContextMenu_AsChild_TriggerContext_TogglesTheMenu()
    {
        var cut = Render<TmContextMenu>(p =>
        {
            p.AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit"));
            p.Add(x => x.AsChild, true);
            p.Add(x => x.Trigger, (RenderFragment)(b =>
            {
                b.OpenComponent<AsChildHost>(0);
                b.CloseComponent();
            }));
        });

        var host = cut.FindComponent<AsChildHost>();
        host.Instance.Trigger.Should().NotBeNull();
        host.Find("button.as-child-trigger").Click();

        cut.Find("[role='menu']").Should().NotBeNull();
        host.Instance.Trigger!.IsOpen.Should().BeTrue(
            "the host's button reads aria-expanded from the context");
    }

    [Fact]
    public void ContextMenu_AsChild_ItemClick_ClosesMenuAndFiresOnClickDeterministically()
    {
        var clicked = false;
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.AsChild, true)
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button class='as-child-trigger'>⋯</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Edit")
                .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicked = true))));

        cut.InvokeAsync(() => cut.Instance.Open());
        cut.Find("[role='menuitem']").Click();

        // Deterministic close: no Task.Yield race — the menu is closed and the handler
        // has run by the time the click dispatch returns.
        cut.FindAll("[role='menu']").Should().BeEmpty();
        clicked.Should().BeTrue();
    }

    [Fact]
    public void ContextMenuItem_Click_FiresOnClickSynchronouslyWithTheClose()
    {
        var clicked = false;
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Menu</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi
                .Add(x => x.Label, "Action")
                .Add(x => x.OnClick, EventCallback.Factory.Create(this, () => clicked = true))));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menuitem']").Click();

        clicked.Should().BeTrue("the deterministic close must not push OnClick to a later dispatcher turn");
    }

    [Fact]
    public void ContextMenu_AsChild_TriggerContext_IsCachedAcrossRenders()
    {
        var cut = Render<TmContextMenu>(p =>
        {
            p.AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit"));
            p.Add(x => x.AsChild, true);
            p.Add(x => x.Trigger, (RenderFragment)(b =>
            {
                b.OpenComponent<AsChildHost>(0);
                b.CloseComponent();
            }));
        });

        var host = cut.FindComponent<AsChildHost>();
        var first = host.Instance.Trigger;
        cut.Render();

        host.Instance.Trigger.Should().BeSameAs(first,
            "a new context instance per render (IsFixed=false) re-renders every consumer on every menu render");
    }

    [Fact]
    public void ContextMenu_AsChild_TriggerContext_OpenAndClose_RideTheDispatcher()
    {
        var cut = Render<TmContextMenu>(p =>
        {
            p.AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit"));
            p.Add(x => x.AsChild, true);
            p.Add(x => x.Trigger, (RenderFragment)(b =>
            {
                b.OpenComponent<AsChildHost>(0);
                b.CloseComponent();
            }));
        });

        var host = cut.FindComponent<AsChildHost>();

        host.Instance.OpenFromContext();
        cut.Find("[role='menu']").Should().NotBeNull("OpenAsync opens the menu");
        host.Instance.Trigger!.IsOpen.Should().BeTrue();

        host.Instance.CloseFromContext();
        cut.FindAll("[role='menu']").Should().BeEmpty("CloseAsync closes the menu");
        host.Instance.Trigger!.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void ContextMenu_AsChild_PanelAnchorsToTheWrapper_NotTheUnrenderedTrigger()
    {
        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.AsChild, true)
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button class='as-child-trigger'>⋯</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.InvokeAsync(() => cut.Instance.Open());

        var panel = cut.FindComponent<TmOverlayPanel>();
        var wrapperRef = (ElementReference)typeof(TmContextMenu)
            .GetField("_wrapperRef", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(cut.Instance)!;
        panel.Instance.Anchor.Id.Should().Be(wrapperRef.Id,
            "in AsChild mode the built-in trigger element is never rendered — the popover must anchor to the wrapper");
        wrapperRef.Id.Should().NotBeEmpty("a vacuous empty-vs-empty comparison proves nothing");
    }

    [Fact]
    public void ContextMenu_AsChild_ItemActivation_HandsFocusBackToTheHostTrigger()
    {
        var nav = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-menu-nav.js");
        nav.Setup<bool>("focusHostTrigger", _ => true).SetResult(true);

        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.AsChild, true)
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button class='as-child-trigger'>⋯</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.InvokeAsync(() => cut.Instance.Open());
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => nav.Invocations["focusHostTrigger"].Should().ContainSingle(
            "an item activation closes the menu and focus returns to the HOST's own trigger, not <body>"));
    }

    [Fact]
    public void ContextMenu_BuiltInTrigger_ItemActivation_DoesNotUseTheHostTriggerHelper()
    {
        var nav = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-menu-nav.js");
        nav.Setup<bool>("focusHostTrigger", _ => true).SetResult(true);

        var cut = Render<TmContextMenu>(p => p
            .Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Actions</button>")))
            .AddChildContent<TmContextMenuItem>(mi => mi.Add(x => x.Label, "Edit")));

        cut.Find(".tm-context-menu__trigger").Click();
        cut.Find("[role='menuitem']").Click();

        cut.WaitForAssertion(() => JSInterop.Invocations.Count(i => i.Identifier == "Blazor._internal.domWrapper.focus")
            .Should().BeGreaterThan(0, "the built-in trigger keeps the ElementReference focus hand-back"));
        nav.Invocations["focusHostTrigger"].Should().BeEmpty(
            "focusHostTrigger is the AsChild-only path — the built-in trigger focuses its own ref");
    }

    // ── Helper ─────────────────────────────────────────────

    /// <summary>A minimal host that binds the AsChild trigger context like a real trigger button would.</summary>
    private sealed class AsChildHost : ComponentBase
    {
        [CascadingParameter] public ContextMenuTriggerContext? Trigger { get; set; }

        public Task OpenFromContext() => Trigger!.OpenAsync();

        public Task CloseFromContext() => Trigger!.CloseAsync();

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "class", "as-child-trigger");
            builder.AddAttribute(2, "aria-haspopup", "menu");
            builder.AddAttribute(3, "aria-expanded", Trigger?.IsOpen.ToString().ToLowerInvariant());
            builder.AddAttribute(4, "onclick", EventCallback.Factory.Create(this, () => Trigger?.ToggleAsync()));
            builder.AddContent(5, "⋯");
            builder.CloseElement();
        }
    }

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
