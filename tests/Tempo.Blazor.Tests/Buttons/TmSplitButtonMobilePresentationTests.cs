using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Buttons;

/// <summary>
/// F11: TmSplitButton carries <c>MobilePresentation</c> / <c>SheetTitle</c> — the default stays
/// the anchored popover at every width; <c>MobilePresentation=Auto</c> presents the dropdown as a
/// content-height bottom sheet (titled) on a mobile viewport, and a forced <c>Sheet</c> renders
/// it without measuring, so bUnit can assert the sheet contract.
/// </summary>
public class TmSplitButtonMobilePresentationTests : LocalizationTestBase
{
    [Fact]
    public void SplitButton_DefaultPresentation_IsPopover_MarkupUnchanged()
    {
        var cut = RenderSplitButton();

        cut.Instance.MobilePresentation.Should().Be(PanelPresentation.Popover,
            "F3/F11: the split button opts into the sheet explicitly — the default is the popover");

        cut.Find(".tm-split-button__toggle").Click();

        var menu = cut.Find("[role='menu']");
        menu.ClassList.Should().Contain("tm-overlay-panel",
            "the default presentation renders the anchored popover element");
        // The role="menu" popover carries no other accessible name — SheetTitle names it.
        menu.GetAttribute("aria-label").Should().Be("More actions");
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty(
            "no sheet may exist at the default presentation");
        cut.FindAll(".tm-overlay-panel-sheet__title").Should().BeEmpty();
    }

    [Fact]
    public void SplitButton_MobilePresentationSheet_RendersSheetWithLocalizedDefaultTitle()
    {
        var cut = RenderSplitButton(p => p.Add(x => x.MobilePresentation, PanelPresentation.Sheet));

        cut.Find(".tm-split-button__toggle").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("More actions",
            "SheetTitle=null falls back to the localized TmSplitButton_SheetTitle default");
        // The menu role survives the popover→sheet switch on the content wrapper.
        cut.Find(".tm-overlay-panel-sheet__content[role='menu']").Should().NotBeNull();
        cut.FindAll(".tm-split-button__dropdown").Should().BeEmpty(
            "the sheet replaces the floating popover, it does not stack with it");
    }

    [Fact]
    public void SplitButton_MobilePresentationSheet_ExplicitSheetTitle_Wins()
    {
        var cut = RenderSplitButton(p => p
            .Add(x => x.MobilePresentation, PanelPresentation.Sheet)
            .Add(x => x.SheetTitle, "Save options"));

        cut.Find(".tm-split-button__toggle").Click();

        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Save options");
    }

    [Fact]
    public void SplitButton_MobilePresentationAuto_MobileViewport_PresentsAsSheet()
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
                b.OpenComponent<TmSplitButton>(0);
                b.AddAttribute(1, "Text", "Save");
                b.AddAttribute(2, "MobilePresentation", PanelPresentation.Auto);
                b.AddAttribute(3, "ChildContent", (RenderFragment)(bb =>
                    bb.AddMarkupContent(0, "<button role='menuitem'>Save as Draft</button>")));
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Find(".tm-split-button__toggle").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull(
            "Auto resolves from the viewport scope: mobile → bottom sheet");
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("More actions");
        cut.FindAll(".tm-split-button__dropdown").Should().BeEmpty();
    }

    private IRenderedComponent<TmSplitButton> RenderSplitButton(
        Action<ComponentParameterCollectionBuilder<TmSplitButton>>? configure = null)
        => Render<TmSplitButton>(p =>
        {
            p.Add(x => x.Text, "Save");
            p.AddChildContent("<button role='menuitem'>Save as Draft</button>");
            configure?.Invoke(p);
        });
}
