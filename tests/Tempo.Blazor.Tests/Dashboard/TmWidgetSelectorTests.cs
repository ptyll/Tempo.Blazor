using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using Tempo.Blazor.Abstractions.Layout;
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
                DefaultWidth = 4, DefaultHeight = 3,
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
        Action? closed = null,
        bool closeOnSelect = true,
        TmLayoutMode layoutMode = TmLayoutMode.Auto)
        => Render<TmWidgetSelector>(p =>
        {
            p.Add(x => x.WidgetRegistry, CreateRegistry());
            p.Add(x => x.ExistingWidgets, existing ?? []);
            p.Add(x => x.OnSelect, (WidgetDefinition w) => selected?.Add(w));
            p.Add(x => x.OnClose, () => closed?.Invoke());
            p.Add(x => x.CloseOnSelect, closeOnSelect);
            p.Add(x => x.LayoutMode, layoutMode);
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

    // ── Fáze 6 (DA1): CloseOnSelect, added state, size label ───────────────────────────

    [Fact]
    public void WidgetSelector_CloseOnSelectDefaultTrue_ClosesAfterAdd()
    {
        var closed = false;
        var cut = RenderSelector(closed: () => closed = true);

        cut.FindAll(".tm-widget-card").First().Click();

        closed.Should().BeTrue("CloseOnSelect defaults to true — today's close-on-add behaviour");
    }

    [Fact]
    public void WidgetSelector_CloseOnSelectFalse_StaysOpen_ShowsAddedState_AnnouncesLive()
    {
        var selected = new List<WidgetDefinition>();
        var cut = RenderSelector(selected: selected, closeOnSelect: false);

        cut.FindAll(".tm-widget-card").First().Click();

        closed_guard(cut);
        void closed_guard(IRenderedComponent<TmWidgetSelector> c)
        {
            // The selector stays open — the modal is still rendered.
            c.FindAll(".tm-modal").Should().HaveCount(1);
        }

        selected.Should().ContainSingle();
        selected[0].Id.Should().Be(SingleInstanceWidgetId);

        // The just-added single-instance card switches to the "Added" state: the green badge
        // replaces the plus indicator, and the card announces itself as not addable.
        var card = cut.FindAll(".tm-widget-card")
            .Single(c => (c.GetAttribute("aria-label") ?? "").Contains("KPI"));
        card.GetAttribute("aria-disabled").Should().Be("true");
        card.QuerySelector(".tm-widget-card-badge--added").Should().NotBeNull();
        card.QuerySelector(".tm-widget-card-add").Should().BeNull();

        // A polite live region announces the addition for screen readers.
        var live = cut.Find(".tm-widget-selector-live");
        live.GetAttribute("aria-live").Should().Be("polite");
        live.TextContent.Should().Contain("Added");
    }

    [Fact]
    public void WidgetSelector_AllowMultiple_ShowsAddedCount()
    {
        var cut = RenderSelector(closeOnSelect: false);

        // The chart widget allows multiple instances: add it twice through the session.
        cut.FindAll(".tm-widget-card").First(c => c.TextContent.Contains("Chart")).Click();
        cut.FindAll(".tm-widget-card").First(c => c.TextContent.Contains("Chart")).Click();

        var card = cut.FindAll(".tm-widget-card").First(c => c.TextContent.Contains("Chart"));
        card.QuerySelector(".tm-widget-card-badge--added")!.TextContent.Should().Contain("2");
        // A multiple-instance widget stays addable: the plus indicator remains.
        card.QuerySelector(".tm-widget-card-add").Should().NotBeNull();
    }

    [Fact]
    public void WidgetSelector_AddedSingleInstance_ShowsAddedBadge_InsteadOfPlus_AriaDisabled()
    {
        var cut = RenderSelector(existing: [SingleInstanceWidgetId]);

        var card = cut.FindAll(".tm-widget-card")
            .Single(c => (c.GetAttribute("class") ?? "").Contains("tm-widget-card--disabled"));
        card.QuerySelector(".tm-widget-card-badge--added")!.TextContent.Should().Contain("Added");
        card.QuerySelector(".tm-widget-card-add").Should().BeNull("the badge replaces the plus indicator");
        card.GetAttribute("aria-disabled").Should().Be("true");
    }

    [Fact]
    public void WidgetSelector_PlusIndicator_AlwaysRendered()
    {
        var cut = RenderSelector();

        // Every addable card carries the plus indicator in the DOM — it never depends on hover
        // (the CSS keeps it permanently visible; coarse pointers get the 44px touch target).
        var addableCards = cut.FindAll(".tm-widget-card")
            .Where(c => c.GetAttribute("aria-disabled") != "true");
        addableCards.Should().NotBeEmpty();
        addableCards.Should().OnlyContain(c => c.QuerySelector(".tm-widget-card-add") != null);
    }

    [Fact]
    public void WidgetSelector_SizeLabel_FormattedWithSpaces()
    {
        var cut = RenderSelector();

        cut.Find(".tm-widget-card-size").TextContent.Should().Be("4 × 3",
            "the grid size reads as a sentence-like token, not the cramped 4×3");
    }

    // ── Fáze 6: mobile presentation ────────────────────────────────────────────────────

    [Fact]
    public void WidgetSelector_Mobile_RendersCategoryChips_AsRadioGroup()
    {
        var cut = RenderSelector(layoutMode: TmLayoutMode.Mobile);

        // The sidebar is gone; categories render as a horizontal radio group of chips.
        cut.FindAll(".tm-widget-selector-sidebar").Should().BeEmpty();
        var group = cut.Find(".tm-widget-selector-chips");
        group.GetAttribute("role").Should().Be("radiogroup");
        group.GetAttribute("aria-label").Should().Be("Categories");

        var chips = group.QuerySelectorAll(".tm-widget-selector-chip");
        chips.Should().HaveCountGreaterThan(1);
        chips[0].GetAttribute("role").Should().Be("radio");
        chips[0].GetAttribute("aria-checked").Should().Be("true", "'All widgets' is the default chip");
        chips[1].GetAttribute("aria-checked").Should().Be("false");

        // Activating a chip filters the grid to that category.
        chips[1].Click();
        cut.FindAll(".tm-widget-selector-chip")[1].GetAttribute("aria-checked").Should().Be("true");
        cut.FindAll(".tm-widget-card").Should().OnlyContain(c => c.TextContent.Contains("KPI"),
            "the Analytics category holds only the KPI widget");
    }

    [Fact]
    public void WidgetSelector_MobilePresentation_IsSheetOnMobile()
    {
        var cut = RenderSelector(layoutMode: TmLayoutMode.Mobile);

        // The F2 foundation: a forced-mobile selector presents as a bottom sheet — the modal
        // panel carries the sheet class (tm-sheet__panel) instead of the desktop dialog chrome.
        cut.Find(".tm-modal").ClassList.Should().Contain("tm-sheet__panel");
    }

    [Fact]
    public void WidgetSelector_DoneButton_IsPrimary()
    {
        var cut = RenderSelector();

        var done = cut.Find(".tm-widget-selector-footer .tm-btn");
        done.ClassList.Should().Contain("tm-btn-primary",
            "'Done' is the primary action of the selector footer");
    }
}
