using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Navigation;

/// <summary>
/// F11: TmContextMenu carries <c>MobilePresentation</c> / <c>SheetTitle</c> — the default stays
/// the anchored popover at every width; <c>MobilePresentation=Auto</c> presents the menu as a
/// content-height bottom sheet (titled) on a mobile viewport, and a forced
/// <c>Sheet</c> renders it without measuring, so bUnit can assert the sheet contract.
/// </summary>
public class TmContextMenuMobilePresentationTests : LocalizationTestBase
{
    [Fact]
    public void ContextMenu_DefaultPresentation_IsPopover_MarkupUnchanged()
    {
        var cut = RenderMenu();

        cut.Instance.MobilePresentation.Should().Be(PanelPresentation.Popover,
            "F3/F11: the context menu opts into the sheet explicitly — the default is the popover");

        var menu = cut.Find("[role='menu']");
        menu.ClassList.Should().Contain("tm-overlay-panel",
            "the default presentation renders the anchored popover element");
        // The localized default Title also names the popover (TmOverlayPanel's Title→aria-label
        // contract) — the one deliberate markup delta on the default path, same as TmDropdown.
        menu.GetAttribute("aria-label").Should().Be("Menu");
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty(
            "no sheet may exist at the default presentation");
        cut.FindAll(".tm-overlay-panel-sheet__title").Should().BeEmpty();
    }

    [Fact]
    public void ContextMenu_MobilePresentationSheet_RendersSheetWithLocalizedDefaultTitle()
    {
        var cut = RenderMenu(p => p.Add(x => x.MobilePresentation, PanelPresentation.Sheet));

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Menu",
            "SheetTitle=null falls back to the localized TmContextMenu_SheetTitle default");
        // The menu role survives the popover→sheet switch on the content wrapper
        // (axe aria-required-parent): menu ownership does not change with the presentation.
        cut.Find(".tm-overlay-panel-sheet__content[role='menu']").Should().NotBeNull();
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty(
            "the sheet replaces the floating popover, it does not stack with it");
    }

    [Fact]
    public void ContextMenu_MobilePresentationSheet_ExplicitSheetTitle_Wins()
    {
        var cut = RenderMenu(p => p
            .Add(x => x.MobilePresentation, PanelPresentation.Sheet)
            .Add(x => x.SheetTitle, "Row actions"));

        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Row actions");
    }

    [Fact]
    public void ContextMenu_MobilePresentationAuto_MobileViewport_PresentsAsSheet()
    {
        // The Auto resolution reads the TmLayoutScopes.Viewport cascade, so the component renders
        // inside a cascading mobile viewport value (docs/responsive-conventions.md).
        var viewport = new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile);

        var cut = Render(builder =>
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", viewport);
            builder.AddAttribute(3, "IsFixed", true);
            builder.AddAttribute(4, "ChildContent", (RenderFragment)(b =>
            {
                b.OpenComponent<TmContextMenu>(0);
                b.AddAttribute(1, "Trigger", (RenderFragment)(bb => bb.AddMarkupContent(0, TriggerMarkup)));
                b.AddAttribute(2, "MobilePresentation", PanelPresentation.Auto);
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        // The panel renders only while open — open the menu before asserting the presentation.
        cut.Find(".tm-context-menu__trigger").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull(
            "Auto resolves from the viewport scope: mobile → bottom sheet");
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Menu");
        cut.FindAll(".tm-overlay-panel").Should().BeEmpty();
    }

    private IRenderedComponent<TmContextMenu> RenderMenu(
        Action<ComponentParameterCollectionBuilder<TmContextMenu>>? configure = null)
    {
        var cut = Render<TmContextMenu>(p =>
        {
            p.Add(x => x.Trigger, (RenderFragment)(b => b.AddMarkupContent(0, TriggerMarkup)));
            configure?.Invoke(p);
        });
        // The panel renders only while open — the presentation contract is asserted on an OPEN menu.
        cut.Find(".tm-context-menu__trigger").Click();
        return cut;
    }

    private const string TriggerMarkup = "<button>Actions</button>";
}
