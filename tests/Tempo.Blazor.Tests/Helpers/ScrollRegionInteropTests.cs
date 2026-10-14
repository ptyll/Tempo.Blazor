using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.Helpers;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Helpers;

/// <summary>
/// cluster-B 8.3: the scroll-region wrapper must be detach-race-safe (a close that lands while the
/// lazy module import and its <c>syncScrollRegion</c> are still in flight must not leave the
/// observer behind — the old <c>!_attached</c> early return dropped that detach), a stale attach
/// must not stop a region a NEWER attach already owns (both share one region id), the module
/// import must not race or repeat, and the region's accessible name must track updates that land
/// while an attach is still in flight (<c>updateScrollRegionLabel</c>).
/// </summary>
public class ScrollRegionInteropTests : LocalizationTestBase
{
    private const string FocusTrapModule = "./_content/Tempo.Blazor/js/tm-focus-trap.js";

    [Fact]
    public async Task DetachDuringInFlightAttach_StopsTheRegion()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        var sync = module.SetupVoid("syncScrollRegion", _ => true);          // stays pending until released below
        module.SetupVoid("stopScrollRegion", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);

        var attach = interop.AttachAsync(default, "title-1");
        var detach = interop.DetachAsync();                                  // the close lands mid-attach
        sync.SetVoidResult();                                                // … the sync completes after it

        await detach;
        await attach;

        module.Invocations.Should().Contain(i => i.Identifier == "syncScrollRegion",
            "the attach reached the module");
        module.Invocations.Should().Contain(i => i.Identifier == "stopScrollRegion",
            "a detach that loses the race against an in-flight attach must still stop the region " +
            "that attach creates — the dropped detach left a ResizeObserver on the removed node");
    }

    [Fact]
    public async Task DetachThenReattachDuringInFlightAttach_NewerAttachKeepsTheRegion()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        var sync = module.SetupVoid("syncScrollRegion", _ => true);          // stays pending until released below
        module.SetupVoid("stopScrollRegion", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);

        var first = interop.AttachAsync(default, "title-1");
        await interop.DetachAsync();                                         // the close lands mid-attach
        var second = interop.AttachAsync(default, "title-1");                // the reopen re-attaches the SAME region id
        sync.SetVoidResult();                                                // … both syncs complete after it

        await second;
        await first;

        // bUnit's Invocations are NOT insertion-ordered (grouped per identifier), so the ordering
        // is asserted by counts: the only legitimate stop belongs to the detach itself. A stale
        // attach that stops unconditionally adds a SECOND stop — and since a newer attach owns the
        // shared region id, that stop would tear the newer region down.
        module.Invocations.Count(i => i.Identifier == "stopScrollRegion").Should().Be(1,
            "only the detach itself may stop the region — the stale attach must not stop the " +
            "region the newer attach already owns");
        module.Invocations.Count(i => i.Identifier == "syncScrollRegion").Should().Be(2,
            "both attaches reached the module");
        JSInterop.Invocations.Should().ContainSingle(i => i.Identifier == "import",
            "the module import is cached — the re-attach must not import again");
    }

    [Fact]
    public async Task AttachAsync_WhenTheSyncInvokeFails_IsNotAttached()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        module.SetupVoid("syncScrollRegion", _ => true).SetException(new JSException("sync failed"));
        module.SetupVoid("updateScrollRegionLabel", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);
        await interop.AttachAsync(default, "title-1");

        await interop.UpdateLabelAsync("title-2", null);

        module.Invocations.Should().Contain(i => i.Identifier == "syncScrollRegion",
            "the sync attempt reached the module");
        module.Invocations.Should().NotContain(i => i.Identifier == "updateScrollRegionLabel",
            "a failed sync never attached the region — there is no label to re-point");
    }

    [Fact]
    public async Task UpdateLabel_WhileDetached_IsANoOp()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        module.SetupVoid("updateScrollRegionLabel", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);

        await interop.UpdateLabelAsync("title-2", null);

        module.Invocations.Should().NotContain(i => i.Identifier == "updateScrollRegionLabel",
            "a label update without a live region has nothing to re-point");
    }

    [Fact]
    public async Task UpdateLabel_ForwardsTheNewTitleId()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        module.SetupVoid("syncScrollRegion", _ => true).SetVoidResult();
        module.SetupVoid("updateScrollRegionLabel", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);
        await interop.AttachAsync(default, "title-1");

        await interop.UpdateLabelAsync("title-2", null);

        var calls = module.Invocations.Where(i => i.Identifier == "updateScrollRegionLabel").ToList();
        calls.Should().HaveCount(1, "the label update forwards exactly one interop call");
        var call = calls[0];
        call.Arguments.Should().HaveCount(3);
        call.Arguments[1].Should().Be("title-2", "the region's name follows the new title id");
        call.Arguments[2].Should().BeNull();
    }

    [Fact]
    public async Task UpdateLabelDuringInFlightAttach_IsReconciledOnceAfterTheSyncCompletes()
    {
        var module = JSInterop.SetupModule(FocusTrapModule);
        var sync = module.SetupVoid("syncScrollRegion", _ => true);          // the attach stays pending
        module.SetupVoid("updateScrollRegionLabel", _ => true).SetVoidResult();

        var interop = new ScrollRegionInterop(JSInterop.JSRuntime);

        var attach = interop.AttachAsync(default, "title-1");
        await interop.UpdateLabelAsync("title-2", null);                     // lands while the sync is in flight
        sync.SetVoidResult();                                                // … the sync completes after it
        await attach;

        var calls = module.Invocations.Where(i => i.Identifier == "updateScrollRegionLabel").ToList();
        calls.Should().HaveCount(1, "the in-flight label update is reconciled exactly once after the sync");
        calls[0].Arguments.Should().HaveCount(3);
        calls[0].Arguments[1].Should().Be("title-2", "the region ends up named by the NEW values");
        calls[0].Arguments[2].Should().BeNull();
    }
}
