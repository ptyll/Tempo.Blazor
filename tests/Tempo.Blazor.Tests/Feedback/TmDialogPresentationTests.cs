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
    }

    [Fact]
    public void Auto_FollowsTheViewportScope()
    {
        var cut = Render<ViewportHost>(p => p.Add(x => x.ViewportMode, TmLayoutMode.Mobile));

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.GetAttribute("data-layout").Should().Be("mobile");
        overlay.ClassList.Should().Contain("tm-modal--sheet");
        cut.FindAll(".tm-dialog__handle").Should().NotBeEmpty();
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
        var withIcon = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Icon, "check"));

        withIcon.Find(".tm-dialog-icon svg").ClassList.Should().Contain("tm-icon-check");

        var without = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.Variant, DialogVariant.Success));

        without.Find(".tm-dialog-icon .tm-icon").Should().NotBeNull(
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
    public void FooterLayout_IsAClassOnTheDialogFooter()
    {
        var cut = Render<TmDialog>(p => p
            .Add(d => d.Show, true)
            .Add(d => d.Title, "Saved")
            .Add(d => d.FooterLayout, FooterLayout.Stacked));

        cut.Find(".tm-dialog-footer").ClassList.Should().Contain("tm-modal-footer--stacked");
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
