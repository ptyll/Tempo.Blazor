using Bunit;
using Bunit.TestDoubles;
using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Feedback;

/// <summary>
/// The additive surface phase 3 of the Spreadsheet redesign needed from the core frame: a modal can BE
/// a message (<c>role="alertdialog"</c> plus the description that carries the message) and can hand the
/// focus restore back to the host. Both are opt-in; the defaults are the pre-existing behaviour, which
/// the second half of this class pins.
/// </summary>
public class TmModalAlertDialogTests : LocalizationTestBase
{
    [Fact]
    public void DialogRole_DefaultsToPlainDialog_AndRendersNoDescription()
    {
        var cut = RenderModal();

        var scope = cut.Find(".tm-modal-overlay .tm-focus-scope");
        scope.GetAttribute("role").Should().Be("dialog",
            "a normal modal stays a dialog, exactly as before the parameter existed");
        scope.HasAttribute("aria-describedby").Should().BeFalse(
            "no description is invented for a surface that never named one");
    }

    [Fact]
    public void AlertDialog_RendersTheRoleAndTheDescriptionOnTheScope()
    {
        var cut = RenderModal(p => p
            .Add(m => m.DialogRole, "alertdialog")
            .Add(m => m.AriaDescribedBy, "alert-message"));

        var scope = cut.Find(".tm-modal-overlay .tm-focus-scope");
        scope.GetAttribute("role").Should().Be("alertdialog");
        scope.GetAttribute("aria-describedby").Should().Be("alert-message");
        scope.GetAttribute("aria-modal").Should().Be("true",
            "an alertdialog is still modal — the trap contract does not change");
    }

    [Fact]
    public void RestoreFocus_DefaultsToTrue_AndRendersTheRestoreTarget()
    {
        var cut = RenderModal(p => p.Add(m => m.RestoreFocusTargetId, "grid-root"));

        var scope = cut.Find(".tm-modal-overlay .tm-focus-scope");
        scope.GetAttribute("data-restore-target").Should().Be("grid-root");
        scope.HasAttribute("data-restore-focus").Should().BeFalse(
            "the default restore path is byte-identical to the pre-existing contract");
    }

    [Fact]
    public void RestoreFocus_False_LeavesTheFocusWhereTheCloseActionPutIt()
    {
        var cut = RenderModal(p => p
            .Add(m => m.RestoreFocus, false)
            .Add(m => m.RestoreFocusTargetId, "grid-root"));

        var scope = cut.Find(".tm-modal-overlay .tm-focus-scope");
        scope.GetAttribute("data-restore-focus").Should().Be("false");
        scope.HasAttribute("data-restore-target").Should().BeFalse(
            "the host that switched the restore off owns the destination");
    }

    [Fact]
    public void AutoFullscreen_IsNotASheet_OnAMobileViewport()
    {
        var cut = Render<MobileViewportHost>();

        var overlay = cut.Find(".tm-modal-overlay");
        overlay.ClassList.Should().Contain("tm-modal--fullscreen-presentation");
        overlay.ClassList.Should().NotContain("tm-modal--sheet");
    }

    private IRenderedComponent<TmModal> RenderModal(
        Action<Bunit.ComponentParameterCollectionBuilder<TmModal>>? configure = null)
        => Render<TmModal>(p =>
        {
            p.Add(m => m.Show, true)
                .Add(m => m.Title, "Notice")
                .Add(m => m.LayoutMode, TmLayoutMode.Desktop)
                .AddChildContent("<p>Body</p>");
            configure?.Invoke(p);
        });

    /// <summary>A modal inside a forced mobile viewport scope, so <c>AutoFullscreen</c> resolves.</summary>
    private sealed class MobileViewportHost : Microsoft.AspNetCore.Components.ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent<Microsoft.AspNetCore.Components.CascadingValue<TmLayoutContext>>(0);
            builder.AddAttribute(1, "Name", TmLayoutScopes.Viewport);
            builder.AddAttribute(2, "Value", new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile));
            builder.AddAttribute(3, "ChildContent", (Microsoft.AspNetCore.Components.RenderFragment)(inner =>
            {
                inner.OpenComponent<TmModal>(0);
                inner.AddAttribute(1, "Show", true);
                inner.AddAttribute(2, "Title", "Tall");
                inner.AddAttribute(3, "MobilePresentation", MobilePresentation.AutoFullscreen);
                inner.AddAttribute(4, "ChildContent",
                    (Microsoft.AspNetCore.Components.RenderFragment)(b => b.AddMarkupContent(0, "<p>Body</p>")));
                inner.CloseComponent();
            }));
            builder.CloseComponent();
        }
    }
}
