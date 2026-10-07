using FluentAssertions;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.Tests.Layout;

/// <summary>
/// The resolution order is the layout contract: an explicit mode beats a forced ancestor, which
/// beats this component's own measurement, which beats the initial mode. Overlays resolve the same
/// way — they measure nothing, so their own measurement is absent.
/// </summary>
public class TmLayoutResolveTests
{
    [Fact]
    public void ViewportScope_BeatsANarrowContainer_ForAnOverlay()
    {
        var resolved = TmLayout.Resolve(
            TmLayoutMode.Auto,
            new TmLayoutContext(TmLayoutMode.Desktop, TmLayoutMode.Desktop),
            measured: null,
            TmLayoutMode.Mobile);

        resolved.Should().Be(TmLayoutMode.Desktop,
            "an overlay asks the viewport-scope context, not the trigger's container, so a narrow host does not turn a dialog into a sheet");
    }

    [Fact]
    public void Overlay_IgnoresTheContainersContext_AndUsesTheViewportScope()
    {
        var viewport = new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop);
        var container = new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile);

        var fromViewport = TmLayout.Resolve(TmLayoutMode.Auto, viewport, measured: null, TmLayoutMode.Mobile);
        var fromContainer = TmLayout.Resolve(TmLayoutMode.Auto, container, measured: null, TmLayoutMode.Desktop);

        fromViewport.Should().Be(TmLayoutMode.Desktop, "the viewport scope measured desktop, so an Auto overlay follows it");
        fromContainer.Should().Be(TmLayoutMode.Mobile,
            "the container context is not what an overlay passes; passing it would wrongly sheet the dialog");
        fromViewport.Should().NotBe(fromContainer, "the two contexts disagree, so the overlay must choose the viewport one");
    }

    [Fact]
    public void WithResolvedAuto_Throws()
    {
        var context = new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop);

        var act = () => context with { Resolved = TmLayoutMode.Auto };

        act.Should().Throw<ArgumentException>("a resolved layout is desktop, tablet or mobile, including through a with-expression");
    }

    [Fact]
    public void ExplicitMode_BeatsEverything()
    {
        var resolved = TmLayout.Resolve(
            TmLayoutMode.Mobile,
            new TmLayoutContext(TmLayoutMode.Desktop, TmLayoutMode.Desktop),
            TmLayoutMode.Tablet,
            TmLayoutMode.Desktop);

        resolved.Should().Be(TmLayoutMode.Mobile);
    }

    [Fact]
    public void ForcedAncestor_BeatsOwnMeasurement_WhenThisModeIsAuto()
    {
        var resolved = TmLayout.Resolve(
            TmLayoutMode.Auto,
            new TmLayoutContext(TmLayoutMode.Mobile, TmLayoutMode.Mobile),
            TmLayoutMode.Desktop,
            TmLayoutMode.Desktop);

        resolved.Should().Be(TmLayoutMode.Mobile,
            "a forced ancestor is the host's decision; measuring a popover against it would fight that decision");
    }

    [Fact]
    public void AutoAncestor_DoesNotOverrideOwnMeasurement()
    {
        var resolved = TmLayout.Resolve(
            TmLayoutMode.Auto,
            new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Desktop),
            TmLayoutMode.Tablet,
            TmLayoutMode.Desktop);

        resolved.Should().Be(TmLayoutMode.Tablet,
            "an ancestor that is itself measuring has not forced a layout");
    }

    [Fact]
    public void OwnMeasurement_BeatsInitial_WhenNothingIsForced()
    {
        var resolved = TmLayout.Resolve(TmLayoutMode.Auto, parent: null, TmLayoutMode.Mobile, TmLayoutMode.Desktop);

        resolved.Should().Be(TmLayoutMode.Mobile);
    }

    [Fact]
    public void InitialMode_IsTheFallback_BeforeAMeasurement()
    {
        var resolved = TmLayout.Resolve(TmLayoutMode.Auto, parent: null, measured: null, TmLayoutMode.Tablet);

        resolved.Should().Be(TmLayoutMode.Tablet);
    }

    [Fact]
    public void Overlay_UsesHostContext_ThenAppLevelContext_ThenInitial()
    {
        // The trigger cascades a forced context: the overlay never measures its own root.
        TmLayout.Resolve(TmLayoutMode.Auto, new TmLayoutContext(TmLayoutMode.Tablet, TmLayoutMode.Tablet), measured: null, TmLayoutMode.Desktop)
            .Should().Be(TmLayoutMode.Tablet);

        // No trigger context: fall back to the app-level observer (viewport), which is measuring.
        TmLayout.Resolve(TmLayoutMode.Auto, new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Mobile), measured: null, TmLayoutMode.Desktop)
            .Should().Be(TmLayoutMode.Mobile);

        // Nothing cascaded at all: the documented pre-measure mode.
        TmLayout.Resolve(TmLayoutMode.Auto, parent: null, measured: null, TmLayoutMode.Desktop)
            .Should().Be(TmLayoutMode.Desktop);
    }

    [Fact]
    public void Context_RejectsAnUnresolvedMode()
    {
        var act = () => new TmLayoutContext(TmLayoutMode.Auto, TmLayoutMode.Auto);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void All_IsImmutable()
    {
        TmBreakpoints.All.Should().BeAssignableTo<IReadOnlyList<int>>();
        TmBreakpoints.All.Should().Equal(640, 768, 1024, 1280);
    }
}
