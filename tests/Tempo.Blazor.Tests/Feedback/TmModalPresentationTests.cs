using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Mobile presentation of <see cref="TmModal"/>. The presentation is a parameter so a test can force
/// it without a DOM, and Auto follows the viewport scope rather than the container the trigger sits
/// in. These tests pin the markup contract the stylesheet keys off.
/// </summary>
public class TmModalPresentationTests : LocalizationTestBase
{
    [Theory]
    [InlineData(MobilePresentation.Dialog, "tm-modal--dialog")]
    [InlineData(MobilePresentation.Sheet, "tm-modal--sheet")]
    [InlineData(MobilePresentation.Fullscreen, "tm-modal--fullscreen-presentation")]
    public void ForcedPresentation_RendersThatVariant(MobilePresentation presentation, string expectedClass)
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, presentation)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Auto_WithNoViewportScope_RendersTheInitialMode_WhichIsADialog()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .AddChildContent("<p>Body</p>"));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("desktop");
        overlay.ClassList.Should().Contain("tm-modal--dialog");
        overlay.ClassList.Should().NotContain("tm-modal--sheet");
        cut.FindAll(".tm-layout").Should().ContainSingle(
            "with no viewport scope an Auto overlay measures its own fixed root, so the first frame is not stuck on InitialMode");
    }

    [Fact]
    public void Auto_FollowsTheViewportScope_EvenWhenTheContainerIsDesktop()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile");
        overlay.ClassList.Should().Contain("tm-modal--sheet",
            "Auto presents a sheet on a mobile viewport, regardless of the container the trigger sits in");
    }

    [Fact]
    public void Auto_PresentsADialogOnTabletAndDesktop()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Tablet));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("tablet");
        overlay.ClassList.Should().Contain("tm-modal--dialog");
    }

    [Fact]
    public void ForcedLayoutMode_RendersTheMatchingPresentationWithoutADom()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.LayoutMode, TmLayoutMode.Mobile)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public void Sheet_RendersADragHandle_AndKeepsTheFooterASiblingOfTheBody()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.MobilePresentation, MobilePresentation.Sheet)
            .Add(m => m.Footer, (RenderFragment)(b => b.AddMarkupContent(0, "<button>Save</button>")))
            .AddChildContent("<p>Body</p>"));

        var handle = cut.Find(".tm-sheet__handle");
        handle.GetAttribute("aria-hidden").Should().Be("true",
            "a modal sheet handle is decorative; the drawer handle is the one that takes keyboard focus");
        handle.GetAttribute("tabindex").Should().BeNull();
        var modal = cut.Find(".tm-modal");
        modal.InnerHtml.Should().Contain("tm-modal-body");
        modal.InnerHtml.Should().Contain("tm-modal-footer");
    }

    [Theory]
    [InlineData(FooterLayout.Inline, "tm-modal-footer--inline")]
    [InlineData(FooterLayout.Stacked, "tm-modal-footer--stacked")]
    public void FooterLayout_IsAClass_SoTheStylesheetCanKeepButtonsSideBySide(FooterLayout layout, string expectedClass)
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.FooterLayout, layout)
            .Add(m => m.ShowDefaultFooterButtons, true)
            .AddChildContent("<p>Body</p>"));

        cut.Find(".tm-modal-footer").ClassList.Should().Contain(expectedClass);
        cut.Find(".tm-modal").GetAttribute("data-restore-target").Should().BeNull();
    }

    [Fact]
    public void RestoreFocusTarget_IsForwardedToTheScope()
    {
        var cut = Render<RestoreHost>();

        cut.Find(".tm-modal").GetAttribute("data-restore-target").Should().Be("trigger");
    }

    [Fact]
    public void PositionBottom_AnchorsThePanel_WithNoGap()
    {
        var cut = Render<TmModal>(p => p
            .Add(m => m.Show, true)
            .Add(m => m.Title, "Create")
            .Add(m => m.Position, ModalPosition.Bottom)
            .AddChildContent("<p>Body</p>"));

        var container = cut.Find(".tm-modal-container");
        container.ClassList.Should().Contain("tm-modal--bottom-anchored");
        container.ClassList.Should().Contain("tm-modal--bottom-flush",
            "ModalPosition.Bottom sits flush with the viewport edge at every width, with no overlay padding and no modal margin");
    }

    [Fact]
    public void ResolvedLayoutChanged_FiresOnATransition_NotForTheInitialValue()
    {
        var seen = new List<TmLayoutMode>();
        var cut = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Desktop)
            .Add(x => x.OnResolved, EventCallback.Factory.Create<TmLayoutMode>(this, seen.Add)));

        seen.Should().BeEmpty("the initial resolved value is not a change");

        cut.Render(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        seen.Should().Equal(TmLayoutMode.Mobile);
    }

    private sealed class RestoreHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "id", "trigger");
            builder.AddContent(2, "Open");
            builder.CloseElement();

            builder.OpenComponent<TmModal>(3);
            builder.AddAttribute(4, "Show", true);
            builder.AddAttribute(5, "Title", "Create");
            builder.AddAttribute(6, "RestoreFocusTargetId", "trigger");
            builder.AddAttribute(7, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
            builder.CloseComponent();
        }
    }

    /// <summary>Cascades a desktop container context beside a named viewport context.</summary>
    private sealed class ViewportHost : ComponentBase
    {
        [Parameter] public TmLayoutMode ViewportMode { get; set; } = TmLayoutMode.Mobile;

        [Parameter] public EventCallback<TmLayoutMode> OnResolved { get; set; }

        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Desktop, TmLayoutMode.Desktop));
            builder.AddAttribute(2, "ChildContent", (RenderFragment)(inner =>
            {
                inner.OpenComponent<CascadingValue<TmLayoutContext>>(0);
                inner.AddAttribute(1, "Value", new TmLayoutContext(TmLayoutMode.Auto, ViewportMode));
                inner.AddAttribute(2, "Name", TmLayoutScopes.Viewport);
                inner.AddAttribute(3, "ChildContent", (RenderFragment)(modal =>
                {
                    modal.OpenComponent<TmModal>(0);
                    modal.AddAttribute(1, "Show", true);
                    modal.AddAttribute(2, "Title", "Create");
                    modal.AddAttribute(3, "ResolvedLayoutChanged", OnResolved);
                    modal.AddAttribute(4, "ChildContent", (RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
                    modal.CloseComponent();
                }));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
