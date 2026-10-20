using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Gallery;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Gallery;

/// <summary>
/// 2.9.1: the lightbox strip mirrors in RTL — the next image lies to the LEFT (the logical insets
/// already place the buttons there and the CSS flips the chevrons), so the physical arrow keys
/// swap with the reading direction, read live through the shared <c>tm-rtl.js</c> helper.
/// </summary>
public class TmLightboxRtlTests : LocalizationTestBase
{
    private const string RtlModule = "./_content/Tempo.Blazor/js/tm-rtl.js";

    private int _nextCalls;
    private int _prevCalls;

    private IRenderedComponent<TmLightbox> RenderOpen(string direction)
    {
        if (direction == "rtl")
        {
            JSInterop.SetupModule(RtlModule)
                .Setup<string>("directionOf", _ => true)
                .SetResult("rtl");
        }
        // No setup for "ltr": the loose runtime answers null and the component falls back to LTR —
        // the historical behaviour wherever JS is unavailable.

        return Render<TmLightbox>(p => p
            .Add(l => l.IsOpen, true)
            .Add(l => l.CurrentIndex, 1)
            .Add(l => l.TotalCount, 2)
            .Add(l => l.OnNext, EventCallback.Factory.Create(this, () => _nextCalls++))
            .Add(l => l.OnPrev, EventCallback.Factory.Create(this, () => _prevCalls++)));
    }

    [Fact]
    public void InLtr_ArrowRightIsNext_AndArrowLeftIsPrevious()
    {
        var cut = RenderOpen("ltr");

        cut.Find(".tm-lightbox-content").KeyDown("ArrowRight");
        _nextCalls.Should().Be(1, "the strip reads left-to-right: ArrowRight advances");
        _prevCalls.Should().Be(0);

        cut.Find(".tm-lightbox-content").KeyDown("ArrowLeft");
        _prevCalls.Should().Be(1, "ArrowLeft goes back");
        _nextCalls.Should().Be(1);
    }

    [Fact]
    public void InRtl_ThePhysicalArrowsSwap_ArrowLeftIsNext()
    {
        var cut = RenderOpen("rtl");

        cut.Find(".tm-lightbox-content").KeyDown("ArrowLeft");
        _nextCalls.Should().Be(1, "in RTL the next image lies to the LEFT — the mirrored chevron says so");

        cut.Find(".tm-lightbox-content").KeyDown("ArrowRight");
        _prevCalls.Should().Be(1, "ArrowRight goes back in RTL");
        _nextCalls.Should().Be(1);
    }

    [Fact]
    public void InRtl_TheArrowsStillRespectTheEnds()
    {
        // m3: the direction is probed ONCE on the opening render and cached for the open, so the
        // module setup must precede the render (a post-render setup would meet an already-cached
        // probe).
        JSInterop.SetupModule(RtlModule)
            .Setup<string>("directionOf", _ => true)
            .SetResult("rtl");
        var cut = Render<TmLightbox>(p => p
            .Add(l => l.IsOpen, true)
            .Add(l => l.CurrentIndex, 0)
            .Add(l => l.TotalCount, 2)
            .Add(l => l.IsFirst, true)
            .Add(l => l.OnNext, EventCallback.Factory.Create(this, () => _nextCalls++))
            .Add(l => l.OnPrev, EventCallback.Factory.Create(this, () => _prevCalls++)));

        // First image in RTL: ArrowRight is "previous" and the strip is at its start — no call.
        cut.Find(".tm-lightbox-content").KeyDown("ArrowRight");
        _prevCalls.Should().Be(0, "the first image has no previous to go to");

        cut.Find(".tm-lightbox-content").KeyDown("ArrowLeft");
        _nextCalls.Should().Be(1, "ArrowLeft is next in RTL");
    }

    [Fact]
    public void TheDirectionIsProbedOncePerOpen_NotPerKeyPress()
    {
        // m3 (review): the cached probe replaces the per-press read — two arrow presses on one
        // open must produce exactly ONE directionOf invocation.
        var probe = JSInterop.SetupModule(RtlModule)
            .Setup<string>("directionOf", _ => true)
            .SetResult("rtl");
        var cut = Render<TmLightbox>(p => p
            .Add(l => l.IsOpen, true)
            .Add(l => l.CurrentIndex, 1)
            .Add(l => l.TotalCount, 2)
            .Add(l => l.OnNext, EventCallback.Factory.Create(this, () => _nextCalls++))
            .Add(l => l.OnPrev, EventCallback.Factory.Create(this, () => _prevCalls++)));

        cut.Find(".tm-lightbox-content").KeyDown("ArrowLeft");
        cut.Find(".tm-lightbox-content").KeyDown("ArrowRight");
        _nextCalls.Should().Be(1, "ArrowLeft is next in RTL");
        _prevCalls.Should().Be(1, "ArrowRight is previous in RTL");
        probe.Invocations["directionOf"].Should().HaveCount(1,
            "the strip's writing direction cannot change while the modal is up — one probe per open, not per key press");
    }
}
