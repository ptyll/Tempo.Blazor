using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// The controlled cold-boot recovery of <see cref="PlaywrightTestBase.GotoAppReadyAsync"/> on the REAL WASM demo.
/// A Blazor WebAssembly boot whose framework fetch dies (a reset connection under host/CPU load) leaves the loader on
/// "Loading components..." and <c>data-blazor-ready</c> never appears - the 2026-11-20 targeted run lost
/// <c>Scheduler_Touch_SwipeFromEventCentre_Scrolls_AndDoesNotResize (768)</c> to 120 s of exactly that. The runtime
/// retries a failed download once on its own (<c>enableDownloadRetry</c>), so ONE aborted request is absorbed and
/// proves nothing: this spec cuts EVERY <c>_framework/*.wasm</c> request for as long as the first document is active
/// (the retries included), which stalls the first boot for good. One reload of the same page boots in seconds, which
/// is what the helper does once - and the spec asserts exactly that (two main-frame navigations).
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class BootRecoveryE2ETests : WasmTestBase
{
    private const string FrameworkRoute = "**/_framework/**";

    [TestMethod]
    public async Task GotoAppReady_AStalledBoot_IsRecoveredByTheOneControlledReload()
    {
        // The stalled first attempt costs the whole readiness budget, so keep it short (40 s instead of the default
        // 120 s); the reload gets the same budget, which a healthy boot (3-5 s) clears with room to spare.
        var previous = Environment.GetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable);
        Environment.SetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable, "40000");
        try
        {
            var context = await CreateContextAsync(768, 800, hasTouch: true);
            var page = await context.NewPageAsync();

            var mainFrameNavigations = 0;
            var aborted = 0;
            page.FrameNavigated += (_, frame) =>
            {
                if (frame.ParentFrame is null)
                {
                    Interlocked.Increment(ref mainFrameNavigations);
                }
            };

            // Cut every .wasm request while the FIRST document is active (navigation count == 1): the download retry
            // of the runtime meets the same abort, so the first boot cannot finish. The reload (count == 2) passes.
            await page.RouteAsync(FrameworkRoute, async route =>
            {
                if (Volatile.Read(ref mainFrameNavigations) == 1
                    && route.Request.Url.Contains(".wasm", StringComparison.OrdinalIgnoreCase))
                {
                    Interlocked.Increment(ref aborted);
                    await route.AbortAsync("connectionreset");
                    return;
                }

                await route.ContinueAsync();
            });

            await GotoAppReadyAsync(page, $"{BaseUrl}/scheduler");
            await page.UnrouteAsync(FrameworkRoute);

            Assert.IsTrue(Volatile.Read(ref aborted) >= 1, "a .wasm request of the first document was cut, so the first boot could not finish");
            Assert.AreEqual(2, Volatile.Read(ref mainFrameNavigations), "the stalled boot is recovered by exactly ONE controlled reload");
            await Assertions.Expect(page.Locator("body")).ToHaveAttributeAsync("data-blazor-ready", "true");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable, previous);
        }
    }
}