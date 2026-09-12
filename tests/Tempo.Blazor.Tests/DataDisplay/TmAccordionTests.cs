using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.DataDisplay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataDisplay;

/// <summary>TDD tests for TmAccordion + TmAccordionItem.</summary>
public class TmAccordionTests : LocalizationTestBase
{
    [Fact]
    public void Accordion_RendersItems()
    {
        var cut = RenderAccordion();

        var headers = cut.FindAll(".tm-accordion-item__header");
        headers.Should().HaveCount(3);
        headers[0].TextContent.Should().Contain("First");
        headers[1].TextContent.Should().Contain("Second");
        headers[2].TextContent.Should().Contain("Third");
    }

    [Fact]
    public void Accordion_AllCollapsedByDefault()
    {
        var cut = RenderAccordion();

        var expandedPanels = cut.FindAll(".tm-accordion-item--expanded");
        expandedPanels.Should().BeEmpty();
    }

    [Fact]
    public void Accordion_ClickHeader_Expands()
    {
        var cut = RenderAccordion();

        cut.FindAll(".tm-accordion-item__header")[0].Click();

        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
        cut.Find(".tm-accordion-item--expanded .tm-accordion-item__body")
            .TextContent.Should().Contain("Content One");
    }

    [Fact]
    public void Accordion_ClickExpanded_Collapses()
    {
        var cut = RenderAccordion();

        var header = cut.FindAll(".tm-accordion-item__header")[0];
        header.Click(); // expand
        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);

        header.Click(); // collapse
        cut.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
    }

    [Fact]
    public void Accordion_SingleMode_ClosesOthers()
    {
        var cut = RenderAccordion(multiple: false);

        var headers = cut.FindAll(".tm-accordion-item__header");
        headers[0].Click();
        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);

        headers[1].Click();
        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
        cut.Find(".tm-accordion-item--expanded .tm-accordion-item__body")
            .TextContent.Should().Contain("Content Two");
    }

    [Fact]
    public void Accordion_MultipleMode_AllowsMultipleOpen()
    {
        var cut = RenderAccordion(multiple: true);

        var headers = cut.FindAll(".tm-accordion-item__header");
        headers[0].Click();
        headers[1].Click();

        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(2);
    }

    [Fact]
    public void Accordion_DisabledItem_CannotExpand()
    {
        var cut = Render<TmAccordion>(p => p
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Disabled")
                .Add(x => x.Disabled, true)
                .AddChildContent("Hidden")));

        cut.Find(".tm-accordion-item__header").Click();
        cut.FindAll(".tm-accordion-item--expanded").Should().BeEmpty();
    }

    [Fact]
    public void Accordion_DisabledItem_HasDisabledClass()
    {
        var cut = Render<TmAccordion>(p => p
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Disabled")
                .Add(x => x.Disabled, true)
                .AddChildContent("Hidden")));

        cut.Find(".tm-accordion-item").ClassList.Should().Contain("tm-accordion-item--disabled");
    }

    [Fact]
    public void Accordion_Subtitle_Renders()
    {
        var cut = Render<TmAccordion>(p => p
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Main")
                .Add(x => x.Subtitle, "Additional info")
                .AddChildContent("Body")));

        cut.Find(".tm-accordion-item__subtitle").TextContent.Should().Contain("Additional info");
    }

    [Fact]
    public void Accordion_AriaExpanded_ReflectsState()
    {
        var cut = RenderAccordion();

        cut.FindAll(".tm-accordion-item__header")[0].GetAttribute("aria-expanded").Should().Be("false");

        cut.FindAll(".tm-accordion-item__header")[0].Click();
        cut.FindAll(".tm-accordion-item__header")[0].GetAttribute("aria-expanded").Should().Be("true");
    }

    [Fact]
    public void Accordion_KeyboardEnter_TogglesItem_ExactlyOnce()
    {
        var cut = RenderAccordion(withKeySinks: true);

        var header = cut.FindAll(".tm-accordion-item__header")[0];
        // Native <button> sequence for Enter: keydown -> click. Emulating the
        // activation in keydown would toggle twice (expand then collapse = no-op).
        header.KeyDown(new KeyboardEventArgs { Key = "Enter" });
        header.Click();

        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
    }

    [Fact]
    public void Accordion_KeyboardSpace_TogglesItem_ExactlyOnce()
    {
        var cut = RenderAccordion(withKeySinks: true);

        var header = cut.FindAll(".tm-accordion-item__header")[0];
        // Native <button> sequence for Space: keydown -> keyup -> click.
        header.KeyDown(new KeyboardEventArgs { Key = " " });
        header.KeyUp(new KeyboardEventArgs { Key = " " });
        header.Click();

        cut.FindAll(".tm-accordion-item--expanded").Should().HaveCount(1);
    }

    [Fact]
    public void Accordion_CustomClass_IsApplied()
    {
        var cut = Render<TmAccordion>(p => p
            .Add(x => x.Class, "my-acc")
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Item")
                .AddChildContent("Body")));

        cut.Find(".tm-accordion").ClassList.Should().Contain("my-acc");
    }

    [Fact]
    public void Accordion_HasChevron()
    {
        var cut = RenderAccordion();

        cut.FindAll(".tm-accordion-item__chevron").Should().HaveCountGreaterThan(0);
    }

    // ── Helper ─────────────────────────────────────────────
    // withKeySinks attaches no-op key handlers to the accordion root so bUnit
    // can dispatch keydown/keyup on the header buttons (bUnit requires a
    // handler on the target or an ancestor; real browsers bubble to document).
    private IRenderedComponent<TmAccordion> RenderAccordion(bool multiple = false, bool withKeySinks = false)
    {
        return Render<TmAccordion>(p => p
            .Add(x => x.Multiple, multiple)
            .Add(x => x.AdditionalAttributes, withKeySinks
                ? new Dictionary<string, object>
                {
                    ["onkeydown"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }),
                    ["onkeyup"] = EventCallback.Factory.Create<KeyboardEventArgs>(this, _ => { }),
                }
                : null)
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "First")
                .AddChildContent("Content One"))
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Second")
                .AddChildContent("Content Two"))
            .AddChildContent<TmAccordionItem>(i => i
                .Add(x => x.Title, "Third")
                .AddChildContent("Content Three")));
    }
}
