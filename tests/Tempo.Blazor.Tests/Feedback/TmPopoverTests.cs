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
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddContent(0, "<button>Open</button>"))
            .AddChildContent("Popover body"));

        cut.Markup.Should().Contain("Open");
    }

    [Fact]
    public void Popover_ClosedByDefault()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddContent(0, "<button>Open</button>"))
            .AddChildContent("Popover body"));

        cut.FindAll(".tm-popover__body--open").Should().BeEmpty();
    }

    [Fact]
    public void Popover_ClickTrigger_Opens()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button class='trigger-btn'>Open</button>"))
            .AddChildContent("Popover body"));

        cut.Find(".tm-popover__trigger").Click();

        cut.FindAll(".tm-popover__body--open").Should().HaveCount(1);
    }

    [Fact]
    public void Popover_ClickTrigger_ShowsContent()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("<p>Rich content here</p>"));

        cut.Find(".tm-popover__trigger").Click();

        cut.Find(".tm-popover__body").InnerHtml.Should().Contain("Rich content here");
    }

    [Fact]
    public void Popover_EscapeCloses()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.FindAll(".tm-popover__body--open").Should().HaveCount(1);

        cut.Find(".tm-popover").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        cut.FindAll(".tm-popover__body--open").Should().BeEmpty();
    }

    [Theory]
    [InlineData(PopoverPosition.Top, "tm-popover--top")]
    [InlineData(PopoverPosition.Bottom, "tm-popover--bottom")]
    [InlineData(PopoverPosition.Left, "tm-popover--left")]
    [InlineData(PopoverPosition.Right, "tm-popover--right")]
    public void Popover_Position_AppliesCss(PopoverPosition pos, string expected)
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.Position, pos)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover").ClassList.Should().Contain(expected);
    }

    [Fact]
    public void Popover_AriaExpanded_WhenOpen()
    {
        var cut = RenderComponent<TmPopover>(p => p
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
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.ShowArrow, false)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.FindAll(".tm-popover__arrow").Should().BeEmpty();
    }

    [Fact]
    public void Popover_ShowArrowTrue_HasArrow()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.ShowArrow, true)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover__trigger").Click();
        cut.Find(".tm-popover__arrow").Should().NotBeNull();
    }

    [Fact]
    public void Popover_ControlledMode_RespectsIsOpen()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.IsOpen, true)
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.FindAll(".tm-popover__body--open").Should().HaveCount(1);
    }

    [Fact]
    public void Popover_CustomClass_IsApplied()
    {
        var cut = RenderComponent<TmPopover>(p => p
            .Add(x => x.Class, "my-pop")
            .Add(x => x.TriggerContent, b => b.AddMarkupContent(0, "<button>Open</button>"))
            .AddChildContent("Content"));

        cut.Find(".tm-popover").ClassList.Should().Contain("my-pop");
    }
}
