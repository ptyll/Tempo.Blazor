using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>TDD tests for TmPopover.</summary>
public class TmPopoverTests : LocalizationTestBase
{
    [Fact]
    public void Popover_RendersTrigger()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddContent(0, "<button>Open</button>"))
            .AddChildContent("Popover body"));

        cut.Markup.Should().Contain("Open");
    }

    [Fact]
    public void Popover_ClosedByDefault()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddContent(0, "<button>Open</button>"))
            .AddChildContent("Popover body"));

        cut.FindAll(".tm-popover__body").Should().BeEmpty();
    }

    [Fact]
    public void Popover_ClickTrigger_Opens()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button class='trigger-btn'>Open</button>"))
            .AddChildContent("Popover body"));

        cut.Find(".tm-popover__trigger").Click();

        cut.FindAll(".tm-popover__body").Should().HaveCount(1);
    }

    [Fact]
    public void Popover_ClickTrigger_ShowsContent()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("<p>Rich content here</p>"));

        cut.Find(".tm-popover__trigger").Click();

        cut.Find(".tm-popover__body").InnerHtml.Should().Contain("Rich content here");
    }

    [Fact]
    public async Task Popover_EscapeCloses()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.FindAll(".tm-popover__body").Should().HaveCount(1);

        // overlay.js consumes Escape in the window capture phase in a real browser — the
        // component's own dismissal path is this JSInvokable callback, not a keydown on the
        // wrapper (N169 removed the dead HandleKeyDown branch — CloseOnEscape is always true,
        // so it could never fire).
        var overlay = cut.FindComponent<TmOverlayPanel>();
        await cut.InvokeAsync(() => overlay.Instance.NotifyDismissedAsync("escape"));

        cut.FindAll(".tm-popover__body").Should().BeEmpty();
    }

    [Theory]
    [InlineData(PopoverPosition.Top, "tm-popover--top")]
    [InlineData(PopoverPosition.Bottom, "tm-popover--bottom")]
    [InlineData(PopoverPosition.Left, "tm-popover--left")]
    [InlineData(PopoverPosition.Right, "tm-popover--right")]
    public void Popover_Position_AppliesCss(PopoverPosition pos, string expected)
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.Position, pos)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover").ClassList.Should().Contain(expected);
    }

    [Fact]
    public void Popover_AriaExpanded_WhenOpen()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        var trigger = cut.Find(".tm-popover__trigger");
        trigger.GetAttribute("aria-expanded").Should().Be("false");

        trigger.Click();
        trigger.GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Popover_ShowArrowFalse_HidesArrow()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.ShowArrow, false)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.FindAll(".tm-popover__arrow").Should().BeEmpty();
    }

    [Fact]
    public void Popover_ShowArrowTrue_HasArrow()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.ShowArrow, true)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.Find(".tm-popover__arrow").Should().NotBeNull();
    }

    [Fact]
    public void Popover_ControlledMode_RespectsIsOpen()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.FindAll(".tm-popover__body").Should().HaveCount(1);
    }

    // ── Keyboard activation (non-native focusable) ──────────────────────────
    // The trigger is a div[role=button] — tabindex makes it reachable and the
    // Enter/Space emulation replaces the native click it can never produce.

    [Fact]
    public void Popover_Trigger_Is_Keyboard_Reachable()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        var trigger = cut.Find(".tm-popover__trigger");
        trigger.GetAttribute("role").Should().Be("button");
        trigger.GetAttribute("tabindex").Should().Be("0");
    }

    [Fact]
    public void Popover_Trigger_Enter_Toggles()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.FindAll(".tm-popover__body").Should().HaveCount(1);
    }

    [Fact]
    public void Popover_Trigger_Space_Toggles()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        // Space activates on KEYUP — the native <button> contract the emulation mirrors (N162):
        // firing on keydown would re-toggle on every held-key auto-repeat.
        cut.Find(".tm-popover__trigger").KeyUp(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-popover__body").Should().HaveCount(1);
    }

    [Fact]
    public void Popover_Trigger_SpaceKeydown_DoesNotToggle()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        // A Space keydown must NOT activate: held Space auto-repeats keydowns, so a keydown-fired
        // toggle could open AND close within one press (N162). Only the release counts.
        cut.Find(".tm-popover__trigger").KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-popover__body").Should().BeEmpty();
    }

    [Fact]
    public void Popover_Trigger_RepeatedEnterKeydown_TogglesOnlyOnce()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        // An auto-repeat Enter keydown (Repeat=true without a preceding non-repeat press — the
        // edge case) must not toggle: the native button's repeated keydown clicks are bounded by
        // the press, not by the repeat stream (N162).
        cut.Find(".tm-popover__trigger").KeyDown(new KeyboardEventArgs { Key = "Enter", Repeat = true });
        cut.FindAll(".tm-popover__body").Should().BeEmpty();
    }

    [Fact]
    public void Popover_Trigger_Native_Child_Keydown_Is_Fenced()
    {
        // A native <button> inside TriggerContent produces its own click — the keydown must
        // never reach the emulating trigger handler on top of it (the fence makes the keydown
        // unhandled, surfacing as MissingEventHandlerException in bUnit).
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button class='inner-btn'>Open</button>"))
            .AddChildContent("Content"));

        var inner = cut.Find(".inner-btn");
        var act = () => inner.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        act.Should().Throw<MissingEventHandlerException>();
    }

    [Fact]
    public void Popover_Trigger_Native_Child_Keyup_Is_Fenced()
    {
        // Space activates on keyup now (N162), so the fence must cover keyup too: a bubbled
        // Space keyup off a native <button> inside TriggerContent would toggle the popover on
        // top of the button's own click — the same double-activation the keydown fence kills.
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button class='inner-btn'>Open</button>"))
            .AddChildContent("Content"));

        var inner = cut.Find(".inner-btn");
        var act = () => inner.KeyUp(new KeyboardEventArgs { Key = " " });
        act.Should().Throw<MissingEventHandlerException>();
    }

    [Fact]
    public void Popover_CustomClass_IsApplied()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.Class, "my-pop")
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover").ClassList.Should().Contain("my-pop");
    }
}
