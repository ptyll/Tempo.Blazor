using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Components.Dashboard;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Dashboard;

/// <summary>
/// Fáze 1 (B12) — the widget selector is a real modal surface: it renders through
/// <see cref="TmModal"/> (dialog role, aria-modal, focus trap, Escape, scroll lock), the cards are
/// native buttons (exactly one activation per Enter/Space), categories are toggle buttons
/// (aria-pressed) and the close control is named. The test-visible class contract with the E2E
/// suite (<c>.tm-widget-selector-modal</c>) stays on the content root.
/// </summary>
public class TmWidgetSelectorTests : LocalizationTestBase
{
    private const string SingleInstanceWidgetId = "w-kpi";

    private static IWidgetRegistry CreateRegistry()
    {
        var registry = Substitute.For<IWidgetRegistry>();
        var widgets = new List<WidgetDefinition>
        {
            new()
            {
                Id = SingleInstanceWidgetId, Name = "KPI", Description = "Key metric",
                Category = WidgetCategories.Analytics, Icon = "bar-chart-2", AllowMultiple = false,
            },
            new()
            {
                Id = "w-chart", Name = "Chart", Description = "Line chart",
                Category = WidgetCategories.Charts, Icon = "pie-chart", AllowMultiple = true,
            },
        };
        registry.GetAllWidgets().Returns(widgets);
        registry.GetCategories().Returns(new List<WidgetCategory>
        {
            new() { Id = WidgetCategories.Analytics, Name = "Analytics & KPIs", Icon = "bar-chart-2", Order = 1 },
            new() { Id = WidgetCategories.Charts, Name = "Charts & Graphs", Icon = "pie-chart", Order = 2 },
        });
        registry.GetWidgetsByCategory(Arg.Any<string>())
            .Returns(ci => widgets.Where(w => w.Category == ci.Arg<string>()).ToList());
        return registry;
    }

    private IRenderedComponent<TmWidgetSelector> RenderSelector(
        List<string>? existing = null,
        List<WidgetDefinition>? selected = null,
        Action? closed = null)
        => Render<TmWidgetSelector>(p =>
        {
            p.Add(x => x.WidgetRegistry, CreateRegistry());
            p.Add(x => x.ExistingWidgets, existing ?? []);
            p.Add(x => x.OnSelect, (WidgetDefinition w) => selected?.Add(w));
            p.Add(x => x.OnClose, () => closed?.Invoke());
        });

    // ── B12: the selector is a TmModal surface ─────────────────────────────────────────

    [Fact]
    public void WidgetSelector_RendersTmModal_WithDialogRoleAndAriaModal()
    {
        var cut = RenderSelector();

        var modal = cut.Find(".tm-modal");
        modal.GetAttribute("role").Should().Be("dialog");
        modal.GetAttribute("aria-modal").Should().Be("true");

        // The dialog is named by its visible title (programmatic compare — a `#…` selector would
        // throw on a guid id starting with a digit).
        var labelledBy = modal.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNullOrEmpty();
        cut.Find(".tm-modal-title").GetAttribute("id").Should().Be(labelledBy);

        // The E2E contract class stays on the content root, the private overlay class is gone.
        cut.FindAll(".tm-widget-selector-modal").Should().HaveCount(1);
        cut.FindAll(".tm-widget-selector-overlay").Should().BeEmpty();
    }

    [Fact]
    public void WidgetSelector_Escape_InvokesOnClose()
    {
        var closed = false;
        var cut = RenderSelector(closed: () => closed = true);

        // Escape is delivered by the focus-trap module at the document level; the module calls
        // back through the scope (the same route TmModalTests uses).
        cut.InvokeAsync(() => cut.FindComponent<TmFocusScope>().Instance.HandleFocusTrapEscapeAsync());

        closed.Should().BeTrue();
    }

    [Fact]
    public void WidgetSelector_CloseButton_HasAriaLabel()
    {
        UseCzechLocalization();
        var cut = RenderSelector();

        cut.Find(".tm-modal-close").GetAttribute("aria-label").Should().Be("Zavřít");
    }

    // ── Cards are native buttons: exactly one activation per key press ─────────────────

    [Fact]
    public void WidgetSelector_Card_IsKeyboardActivatable()
    {
        var selected = new List<WidgetDefinition>();
        var cut = RenderSelector(selected: selected);

        var card = cut.Find(".tm-widget-card");
        card.TagName.Should().Be("BUTTON", "the card is a native button, not a div with click emulation");

        // Native activation: the card carries NO emulated Enter/Space handler — bUnit surfaces the
        // missing handler as the proof that the browser's own key handling stays in charge
        // (docs/keyboard-activation-convention.md, "bUnit testing").
        var keyDown = () => card.KeyDown("Enter");
        keyDown.Should().Throw<Bunit.MissingEventHandlerException>();
        var keyUp = () => card.KeyUp(" ");
        keyUp.Should().Throw<Bunit.MissingEventHandlerException>();

        // The click the browser produces from Enter/Space activates the card exactly once.
        card.Click();
        selected.Should().ContainSingle();
    }

    [Fact]
    public void WidgetSelector_Card_NotAddable_AnnouncesAriaDisabled_AndNeverSelects()
    {
        var selected = new List<WidgetDefinition>();
        var cut = RenderSelector(existing: [SingleInstanceWidgetId], selected: selected);

        // The single-instance widget is already on the dashboard: the card stays focusable but
        // announces itself as disabled and ignores activation.
        var card = cut.FindAll(".tm-widget-card")
            .Single(c => (c.GetAttribute("class") ?? "").Contains("tm-widget-card--disabled"));
        card.TagName.Should().Be("BUTTON");
        card.GetAttribute("aria-disabled").Should().Be("true");

        card.Click();
        selected.Should().BeEmpty();
    }

    [Fact]
    public void WidgetSelector_Card_IsPhrasingContent_WithTheDescriptionReferenced()
    {
        // A button must not contain headings or paragraphs: the card's children are spans, the
        // title/description blockified by CSS, and the description span is what the button's
        // aria-describedby points at (the accessible NAME stays the widget's name alone).
        var cut = RenderSelector();

        var card = cut.Find(".tm-widget-card");
        card.QuerySelector("h4, h3, p, div").Should().BeNull(
            "a button carries phrasing content only — no h4/p/div inside the card");

        var describedBy = card.GetAttribute("aria-describedby");
        describedBy.Should().NotBeNullOrEmpty();
        var desc = card.QuerySelector($"#{describedBy}");
        desc.Should().NotBeNull("aria-describedby must resolve inside the card");
        desc!.ClassList.Should().Contain("tm-widget-card-desc");
        desc.TextContent.Should().Be("Key metric");

        // The accessible name is the widget name; the size token "3×2" is decoration.
        card.TextContent.Should().Contain("KPI");
        card.QuerySelector(".tm-widget-card-size")!.GetAttribute("aria-hidden").Should().Be("true");

        // The large modal surface: the selector needs the room for sidebar + grid.
        cut.Find(".tm-modal").ClassList.Should().Contain("tm-modal--xl");
    }

    // ── Categories are toggle buttons ──────────────────────────────────────────────────

    [Fact]
    public void WidgetSelector_CategoryButton_HasAriaPressed()
    {
        var cut = RenderSelector();

        cut.FindAll(".tm-widget-selector-category").Should().HaveCountGreaterThan(1);
        cut.FindAll(".tm-widget-selector-category")[0].GetAttribute("aria-pressed")
            .Should().Be("true", "'All widgets' is selected by default");
        cut.FindAll(".tm-widget-selector-category")[1].GetAttribute("aria-pressed")
            .Should().Be("false");

        cut.FindAll(".tm-widget-selector-category")[1].Click();

        // Re-query after the re-render (the modal content rebuilds its subtree).
        cut.FindAll(".tm-widget-selector-category")[1].GetAttribute("aria-pressed").Should().Be("true");
        cut.FindAll(".tm-widget-selector-category")[0].GetAttribute("aria-pressed").Should().Be("false");
    }
}
