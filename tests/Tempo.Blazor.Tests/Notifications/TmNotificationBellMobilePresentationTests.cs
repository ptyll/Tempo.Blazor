using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Abstractions.Shared;
using Tempo.Blazor.Components.Notifications;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Services;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Notifications;

/// <summary>
/// F11: TmNotificationBell carries <c>MobilePresentation</c> / <c>SheetTitle</c> — the default
/// stays the anchored popover at every width; <c>MobilePresentation=Auto</c> presents the
/// notification panel as a content-height bottom sheet (titled) on a mobile viewport, and a
/// forced <c>Sheet</c> renders it without measuring, so bUnit can assert the sheet contract.
/// </summary>
public class TmNotificationBellMobilePresentationTests : LocalizationTestBase
{
    private readonly InMemoryNotificationStore _store = new();

    public TmNotificationBellMobilePresentationTests()
    {
        Services.AddSingleton<ITmNotificationService>(_store);
        Services.AddSingleton<NavigationManager>(new FakeNavManager());
    }

    [Fact]
    public void Bell_DefaultPresentation_IsPopover_MarkupUnchanged()
    {
        var cut = RenderBell();

        cut.Instance.MobilePresentation.Should().Be(PanelPresentation.Popover,
            "F3/F11: the bell opts into the sheet explicitly — the default is the popover");

        cut.Find(".tm-notification-bell__button").Click();

        var panel = cut.Find(".tm-notification-bell__dropdown");
        panel.ClassList.Should().Contain("tm-overlay-panel",
            "the default presentation renders the anchored popover element");
        // N326: the popover dialog stays named by the visible in-panel title.
        var labelledBy = panel.GetAttribute("aria-labelledby");
        labelledBy.Should().NotBeNull();
        cut.Find($"#{labelledBy}").TextContent.Should().Be("Notifications");
        // The name comes from the in-panel title alone — the panel carries no competing label.
        panel.GetAttribute("aria-label").Should().BeNull();
        cut.FindAll(".tm-overlay-panel-sheet").Should().BeEmpty(
            "no sheet may exist at the default presentation");
    }

    [Fact]
    public void Bell_MobilePresentationSheet_RendersSheetWithLocalizedDefaultTitle()
    {
        var cut = RenderBell(p => p.Add(x => x.MobilePresentation, PanelPresentation.Sheet));

        cut.Find(".tm-notification-bell__button").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull();
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Notifications",
            "SheetTitle=null falls back to the localized TmNotificationBell_SheetTitle default");
        // The bell's own filter/mark-all actions stay reachable inside the sheet body.
        cut.Find(".tm-overlay-panel-sheet__content .tm-notification-bell__filter").Should().NotBeNull();
        cut.FindAll(".tm-notification-bell__dropdown").Should().BeEmpty(
            "the sheet replaces the floating popover, it does not stack with it");
    }

    [Fact]
    public void Bell_MobilePresentationSheet_ExplicitSheetTitle_Wins()
    {
        var cut = RenderBell(p => p
            .Add(x => x.MobilePresentation, PanelPresentation.Sheet)
            .Add(x => x.SheetTitle, "Activity"));

        cut.Find(".tm-notification-bell__button").Click();

        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Activity");
    }

    [Fact]
    public void Bell_MobilePresentationAuto_MobileViewport_PresentsAsSheet()
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
                b.OpenComponent<TmNotificationBell>(0);
                b.AddAttribute(1, "MobilePresentation", PanelPresentation.Auto);
                b.CloseComponent();
            }));
            builder.CloseComponent();
        });

        cut.Find(".tm-notification-bell__button").Click();

        cut.Find(".tm-overlay-panel-sheet").Should().NotBeNull(
            "Auto resolves from the viewport scope: mobile → bottom sheet");
        cut.Find(".tm-overlay-panel-sheet__title").TextContent.Should().Be("Notifications");
        cut.FindAll(".tm-notification-bell__dropdown").Should().BeEmpty();
    }

    private IRenderedComponent<TmNotificationBell> RenderBell(
        Action<ComponentParameterCollectionBuilder<TmNotificationBell>>? configure = null)
        => Render<TmNotificationBell>(p => configure?.Invoke(p));

    private sealed class FakeNavManager : NavigationManager
    {
        public FakeNavManager()
        {
            Initialize("https://localhost/", "https://localhost/");
        }

        protected override void NavigateToCore(string uri, bool forceLoad)
        {
            Uri = ToAbsoluteUri(uri).ToString();
        }
    }
}
