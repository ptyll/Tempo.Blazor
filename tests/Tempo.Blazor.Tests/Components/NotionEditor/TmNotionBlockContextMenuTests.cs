using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.NotionEditor.Blocks;
using Tempo.Blazor.Components.NotionEditor.Services;
using Tempo.Blazor.NotionEditor.Enums;
using Tempo.Blazor.NotionEditor.Interfaces;
using Tempo.Blazor.NotionEditor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Components.NotionEditor;

/// <summary>
/// Keyboard/ARIA contract for the block context menu's three submenu triggers
/// (Turn into, Panel type, Color). They were divs with role="menuitem" — no
/// tabindex, no click, no aria-haspopup — so the Tab-based focus trap skipped
/// them and AT could not open the submenus at all (N188).
/// </summary>
public sealed class TmNotionBlockContextMenuTests : LocalizationTestBase
{
    private static readonly Guid PageId = Guid.Parse("cf130000-0000-0000-0000-0000000000a1");

    // A Callout block renders all three submenu triggers (Panel type is Callout-only).
    private static PageBlock CalloutBlock() => new()
    {
        Id      = Guid.Parse("cf130000-0000-0000-0000-0000000000b2"),
        PageId  = PageId,
        Type    = BlockType.Callout,
        Order   = 0,
        Content = new CalloutBlockContent { Html = "callout" }
    };

    private static PageBlock TextBlock() => new()
    {
        Id      = Guid.Parse("cf130000-0000-0000-0000-0000000000b3"),
        PageId  = PageId,
        Type    = BlockType.Paragraph,
        Order   = 0,
        Content = new TextBlockContent { Html = "text" }
    };

    private IRenderedComponent<CascadingValue<NotionEditorContext>> RenderMenu(
        PageBlock block,
        Action<bool>? closed = null)
    {
        var context = new NotionEditorContext();

        return Render<CascadingValue<NotionEditorContext>>(parameters => parameters
            .Add(component => component.Value, context)
            .AddChildContent<TmNotionBlockContextMenu>(child => child
                .Add(component => component.Block, block)
                .Add(component => component.OnClose, EventCallback.Factory.Create(
                    this,
                    () => closed?.Invoke(true)))));
    }

    [Fact]
    public void SubmenuTriggers_AreKeyboardFocusableButtons()
    {
        var cut = RenderMenu(CalloutBlock());

        // The focus trap enumerates button:not([disabled]) — only real <button>
        // triggers enter its Tab cycle and expose a keyboard activation path.
        cut.FindAll("button[aria-haspopup='menu']").Should().HaveCount(3);
    }

    [Fact]
    public void SubmenuTrigger_Click_OpensSubmenu_WithMenuRole()
    {
        var cut = RenderMenu(CalloutBlock());
        var turnInto = cut.FindAll("button[aria-haspopup='menu']").First();

        turnInto.GetAttribute("aria-expanded").Should().Be("false");

        turnInto.Click();

        var sub = cut.Find(".tm-notion-ctx-sub");
        sub.GetAttribute("role").Should().Be("menu");
        cut.FindAll("button[aria-haspopup='menu']").First()
            .GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void SubmenuPanel_HasMenuRole_And_Label()
    {
        var cut = RenderMenu(CalloutBlock());

        cut.FindAll("button[aria-haspopup='menu']").First().Click();

        var sub = cut.Find(".tm-notion-ctx-sub");
        sub.GetAttribute("role").Should().Be("menu");
        // aria-label recycles the trigger's own localized label.
        sub.GetAttribute("aria-label").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Escape_WithOpenSubmenu_ClosesOnlySubmenu()
    {
        var closed = false;
        var cut = RenderMenu(CalloutBlock(), v => closed = v);

        cut.FindAll("button[aria-haspopup='menu']").First().Click();
        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1);

        cut.Find(".tm-notion-ctx").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        // APG menu-button contract: Escape inside a submenu collapses only the
        // submenu and returns focus to its trigger — the parent menu stays open.
        cut.FindAll(".tm-notion-ctx-sub").Should().BeEmpty();
        closed.Should().BeFalse("Escape with an open submenu must not close the whole menu");
        cut.FindAll(".tm-notion-ctx").Should().HaveCount(1);
    }

    [Fact]
    public async Task ArrowRight_OnSubmenuTrigger_OpensSubmenu_AndFocusesFirstItem()
    {
        // 20D carry-forward (APG menu pattern): Right Arrow on a submenu trigger opens the
        // submenu AND lands focus on its first item — Enter/Space already opened via the
        // button click, Tab walked the items, but the arrow gesture was missing.
        var focusFirst = JSInterop.SetupVoid("tmNotionEditor.focusFirstMenuItem", _ => true)
            .SetVoidResult();

        var cut = RenderMenu(CalloutBlock());
        var turnInto = cut.FindAll("button[aria-haspopup='menu']").First();

        await cut.InvokeAsync(() => turnInto.KeyDown(new KeyboardEventArgs { Key = "ArrowRight" }));

        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1,
            "ArrowRight on a submenu trigger must open its submenu like Enter/Space do");
        cut.FindAll("button[aria-haspopup='menu']").First()
            .GetAttribute("aria-expanded").Should().Be("true");
        focusFirst.Invocations["tmNotionEditor.focusFirstMenuItem"].Should().HaveCount(1,
            "the APG contract lands focus on the first submenu item, not just opens the panel");
    }

    [Fact]
    public void KeyboardActivation_OnSubmenuTrigger_FocusesFirstItem()
    {
        // CF18-1: Enter/Space on a submenu trigger is a native click with Detail==0 — it opened
        // the panel but never moved focus, an APG asymmetry next to ArrowRight (which does both).
        var focusFirst = JSInterop.SetupVoid("tmNotionEditor.focusFirstMenuItem", _ => true)
            .SetVoidResult();
        var cut = RenderMenu(CalloutBlock());
        var turnInto = cut.FindAll("button[aria-haspopup='menu']").First();

        turnInto.Click(new MouseEventArgs { Detail = 0 }); // keyboard/programmatic activation

        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1);
        focusFirst.Invocations["tmNotionEditor.focusFirstMenuItem"].Should().HaveCount(1,
            "Enter/Space activation must land focus on the first submenu item like ArrowRight does");
    }

    [Fact]
    public void PointerClick_OnSubmenuTrigger_OpensSubmenu_WithoutFocusHandoff()
    {
        // CF18-1 inverse: a pointer click (Detail>=1) opens the submenu but keeps its own focus
        // model — yanking DOM focus into the panel would fight the hover-open sibling flow.
        var focusFirst = JSInterop.SetupVoid("tmNotionEditor.focusFirstMenuItem", _ => true)
            .SetVoidResult();
        var cut = RenderMenu(CalloutBlock());
        var turnInto = cut.FindAll("button[aria-haspopup='menu']").First();

        turnInto.Click(new MouseEventArgs { Detail = 1 }); // pointer click

        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1);
        focusFirst.Invocations["tmNotionEditor.focusFirstMenuItem"].Should().BeEmpty();
    }

    [Fact]
    public void MouseLeave_WhileFocusInsideSubmenu_KeepsSubmenuOpen()
    {
        // CF18-2: mouseleave fired CloseSub unconditionally — a submenu still holding keyboard
        // focus was unmounted under the caret and focus fell to <body>.
        var cut = RenderMenu(CalloutBlock());
        var wrapper = cut.FindAll(".tm-notion-ctx__sub").First();

        wrapper.QuerySelector("button[aria-haspopup='menu']")!.Click(new MouseEventArgs { Detail = 0 });
        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1);

        // Focus lands inside the wrapper (the open submenu's first item)…
        wrapper.TriggerEvent("onfocusin", new FocusEventArgs());
        // …then the pointer leaves — the submenu must stay open under the focus it still holds.
        wrapper.TriggerEvent("onmouseleave", new MouseEventArgs());

        cut.FindAll(".tm-notion-ctx-sub").Should().HaveCount(1,
            "mouseleave must not close a submenu whose wrapper still holds keyboard focus");
    }

    [Fact]
    public void SiblingMouseEnter_WhileFocusInsideSubmenu_ReturnsFocusToTriggerBeforeClosing()
    {
        // CF18-2: a hover on a sibling wrapper closes the focused submenu — without first
        // handing focus back to that submenu's trigger it would fall to <body>.
        var cut = RenderMenu(CalloutBlock());
        var wrappers = cut.FindAll(".tm-notion-ctx__sub");
        var turnInto = wrappers[0]; // Turn into
        var color = wrappers[^1];   // Color
        var turnIntoTrigger = turnInto.QuerySelector("button[aria-haspopup='menu']")!;

        turnIntoTrigger.Click(new MouseEventArgs { Detail = 0 });
        turnInto.TriggerEvent("onfocusin", new FocusEventArgs());

        color.TriggerEvent("onmouseenter", new MouseEventArgs());

        cut.FindAll("button[aria-haspopup='menu']")[0]
            .GetAttribute("aria-expanded").Should().Be("false",
                "the sibling hover still closes the previously focused submenu");
        JSInterop.Invocations.Should().Contain(i => i.Identifier == "Blazor._internal.domWrapper.focus",
            "closing a focused submenu must first hand focus back to its trigger — " +
            "otherwise keyboard focus falls to <body> (CF18-2)");
        cut.FindAll("button[aria-haspopup='menu']")[2]
            .GetAttribute("aria-expanded").Should().Be("true",
                "the hovered sibling's own submenu opened as before");
    }

    [Fact]
    public void ArrowLeft_WithOpenSubmenu_ClosesOnlySubmenu()
    {
        var closed = false;
        var cut = RenderMenu(CalloutBlock(), v => closed = v);

        cut.FindAll("button[aria-haspopup='menu']").First().Click();

        cut.Find(".tm-notion-ctx").KeyDown(new KeyboardEventArgs { Key = "ArrowLeft" });

        cut.FindAll(".tm-notion-ctx-sub").Should().BeEmpty();
        closed.Should().BeFalse();
    }

    [Fact]
    public void Escape_WithoutOpenSubmenu_ClosesMenu()
    {
        var closed = false;
        var cut = RenderMenu(TextBlock(), v => closed = v);

        cut.Find(".tm-notion-ctx").KeyDown(new KeyboardEventArgs { Key = "Escape" });

        closed.Should().BeTrue();
    }

    [Fact]
    public void ParagraphBlock_Renders_TwoSubmenuTriggers()
    {
        var cut = RenderMenu(TextBlock());

        // Turn into + Color; Panel type is Callout-only.
        cut.FindAll("button[aria-haspopup='menu']").Should().HaveCount(2);
    }
}
