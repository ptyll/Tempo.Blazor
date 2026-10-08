using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// Mobile presentation of <see cref="TmDialog"/>, plus the icon and the inline layout the reference
/// mockups use. The dialog resolves its presentation from the viewport scope, exactly like the modal.
/// </summary>
public class TmDialogPresentationTests : LocalizationTestBase
{
    [Fact]
    public void Auto_WithNoViewportScope_RendersACenteredDialog()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.Message, "This cannot be undone."));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("desktop");
        overlay.ClassList.Should().Contain("tm-modal--dialog");
        cut.FindAll(".tm-viewport-probe").Should().ContainSingle(
            "with no viewport scope an Auto dialog measures the viewport itself through the internal probe");
    }

    [Fact]
    public void Auto_FollowsTheViewportScope()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile");
        overlay.ClassList.Should().Contain("tm-modal--sheet");
    }

    [Fact]
    public void Sheet_WithADismissGesture_RendersTheDecorativeHandle()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.CloseOnOverlayClick, true));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");
        cut.Find(".tm-sheet__handle").GetAttribute("aria-hidden").Should().Be("true",
            "the grabber is decorative on a dialog sheet; the buttons are the controls");
    }

    [Fact]
    public void Sheet_WithoutADismissGesture_RendersNoDeadGrabber()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.CloseOnOverlayClick, false));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--sheet");
        cut.FindAll(".tm-sheet__handle").Should().BeEmpty(
            "the grabber never dismisses when the overlay click is off, so painting it invites a dead swipe");
    }

    [Fact]
    public void LongContent_IsAKeyboardReachableScrollRegion()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.Message, new string('x', 400)));

        var content = cut.Find(".tm-dialog-content");
        content.GetAttribute("tabindex").Should().Be("0",
            "a keyboard-only user cannot scroll an unfocusable scroller (WCAG 2.1.1)");
        content.GetAttribute("role").Should().Be("region");
        content.GetAttribute("aria-labelledby").Should().Be(cut.Find(".tm-dialog-title").GetAttribute("id"),
            "the scroll region is named by its title");
    }

    [Fact]
    public void Content_WithoutATitle_ScrollsWithoutADanglingName()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Message, new string('x', 400)));

        var content = cut.Find(".tm-dialog-content");
        content.GetAttribute("tabindex").Should().Be("0");
        content.GetAttribute("role").Should().BeNull("a region without a name is noise for a reader");
        content.GetAttribute("aria-labelledby").Should().BeNull();
    }

    [Fact]
    public void ForcedPresentation_RendersThatVariant()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.MobilePresentation, MobilePresentation.Fullscreen));

        cut.Find(".tm-modal-overlay").ClassList.Should().Contain("tm-modal--fullscreen-presentation");
    }

    [Fact]
    public void Icon_RendersTheNamedIcon_AndKeepsTheDefaultWhenUnset()
    {
        var without = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Variant, DialogVariant.Success));

        var withIcon = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Icon, "check"));

        withIcon.Find(".tm-dialog-icon svg").InnerHtml.Should().NotBe(
            without.Find(".tm-dialog-icon svg").InnerHtml,
            "a named Icon replaces the variant's default icon");

        without.Find(".tm-dialog-icon svg").Should().NotBeNull(
            "an unset Icon keeps the variant's default icon");
    }

    [Theory]
    [InlineData(DialogLayout.Centered, "tm-dialog--centered")]
    [InlineData(DialogLayout.Inline, "tm-dialog--inline")]
    public void Layout_PlacesTheIconBesideOrAboveTheContent(DialogLayout layout, string expectedClass)
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Layout, layout));

        cut.Find(".tm-dialog").ClassList.Should().Contain(expectedClass);
    }

    [Fact]
    public void Dangerous_AnnouncesItselfAsAnAlertDialog()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Delete dashboard")
            .Add(d => d.IsDangerous, true));

        cut.Find(".tm-dialog").GetAttribute("role").Should().Be("alertdialog");
    }

    [Fact]
    public void InlineLayout_PutsTheFooterOnItsOwnRow()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Layout, DialogLayout.Inline));

        cut.Find(".tm-dialog").ClassList.Should().Contain("tm-dialog--inline");
        cut.Find(".tm-dialog-footer").ClassList.Should().Contain("tm-dialog-footer--own-row");
    }

    [Fact]
    public void RestoreFocusTarget_IsForwardedToTheScope()
    {
        var cut = Render<RestoreHost>();

        cut.Find(".tm-dialog").GetAttribute("data-restore-target").Should().Be("trigger");
    }

    [Fact]
    public void FooterLayout_IsAClassOnTheDialogFooter()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.FooterLayout, FooterLayout.Stacked));

        cut.Find(".tm-dialog-footer").ClassList.Should().Contain("tm-modal-footer--stacked");
        cut.Find(".tm-dialog").GetAttribute("role").Should().Be("dialog");
    }

    [Fact]
    public void ResolvedLayoutChanged_FiresOnlyOnATransition()
    {
        var seen = new List<TmLayoutMode>();
        var cut = Render<ViewportHost>(p => p
            .Add(x => x.ViewportMode, TmLayoutMode.Desktop)
            .Add(x => x.OnResolved, EventCallback.Factory.Create<TmLayoutMode>(this, seen.Add)));

        seen.Should().BeEmpty();

        cut.Render(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        seen.Should().Equal(TmLayoutMode.Mobile);
    }

    /// <summary>A dialog opened from a named trigger, so the restore target can be asserted.</summary>
    private sealed class RestoreHost : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "id", "trigger");
            builder.AddContent(2, "Open");
            builder.CloseElement();

            builder.OpenComponent<TmDialog>(3);
            builder.AddAttribute(4, "Show", true);
            builder.AddAttribute(5, "Title", "Saved");
            builder.AddAttribute(6, "RestoreFocusTargetId", "trigger");
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
                inner.AddAttribute(3, "ChildContent", (RenderFragment)(dialog =>
                {
                    dialog.OpenComponent<TmDialog>(0);
                    dialog.AddAttribute(1, "Show", true);
                    dialog.AddAttribute(2, "Title", "Delete dashboard");
                    dialog.AddAttribute(3, "ResolvedLayoutChanged", OnResolved);
                    dialog.CloseComponent();
                }));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
