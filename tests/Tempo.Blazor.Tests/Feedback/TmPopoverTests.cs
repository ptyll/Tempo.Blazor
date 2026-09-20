using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Feedback;
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
    public void Popover_EscapeCloses()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.FindAll(".tm-popover__body").Should().HaveCount(1);

        cut.Find(".tm-popover").KeyDown(new KeyboardEventArgs { Key = "Escape" });
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

        cut.Find(".tm-popover__trigger").KeyDown(new KeyboardEventArgs { Key = " " });
        cut.FindAll(".tm-popover__body").Should().HaveCount(1);
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
    public void Popover_CustomClass_IsApplied()
    {
        var cut = Render<TmPopover>(p => p
            .Add(x => x.Class, "my-pop")
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover").ClassList.Should().Contain("my-pop");
    }
}
