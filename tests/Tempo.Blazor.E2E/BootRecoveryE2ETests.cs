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

    [TestMethod]
    public async Task NavigateAppReady_WithoutADocumentLoad_FailsInsteadOfPassingOnTheOldDocument()
    {
        // The trigger does nothing, so the old document is still there: the helper must say so (after the readiness
        // budget, which this spec keeps short) instead of returning on the old page's data-blazor-ready.
        var page = await OpenAppPageAsync($"{BaseUrl}/scheduler", new BrowserNewContextOptions { Locale = "en-US" });

        await WithReadyBudgetAsync(3_000, async () =>
        {
            var exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => NavigateAppReadyAsync(page, () => Task.CompletedTask));

            StringAssert.Contains(exception.Message, "did not load a new document");
        });
    }

    [TestMethod]
    public async Task OpenAppPage_RunsBeforeNavigateBeforeTheFirstNavigation_AndLeavesTheContextToCleanup()
    {
        var beforeNavigateRan = false;
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/scheduler",
            new BrowserNewContextOptions { Locale = "en-US" },
            context =>
            {
                beforeNavigateRan = true;
                return context.AddInitScriptAsync("window.__tmBeforeNavigateRan = true;");
            });

        Assert.IsTrue(beforeNavigateRan, "the beforeNavigate callback ran");
        Assert.IsTrue(
            await page.EvaluateAsync<bool>("() => window.__tmBeforeNavigateRan === true"),
            "an init script added in beforeNavigate is part of the FIRST document: the callback ran before the first navigation");
        await Assertions.Expect(page.Locator("body")).ToHaveAttributeAsync("data-blazor-ready", "true");

        // The context is registered for cleanup (CreateContextAsync), so the test cleanup closes it.
        Assert.IsFalse(page.IsClosed, "the page is open while the test runs");
        await BaseTestCleanup();
        Assert.IsTrue(page.IsClosed, "the context OpenAppPageAsync created is closed by the test cleanup");
    }

    [TestMethod]
    public async Task GotoWithoutBootRecovery_ABootThatNeverFinishes_IsNotRecovered()
    {
        // Every .wasm request is cut for the whole test, so the boot never finishes. GotoAppReadyAsync would reload
        // once after the readiness budget; the boot-subject helper must NOT: one navigation, no data-blazor-ready.
        await WithReadyBudgetAsync(6_000, async () =>
        {
            var context = await CreateContextAsync(new BrowserNewContextOptions { Locale = "en-US" }, trace: false);
            var page = await context.NewPageAsync();

            var mainFrameNavigations = 0;
            page.FrameNavigated += (_, frame) =>
            {
                if (frame.ParentFrame is null)
                {
                    Interlocked.Increment(ref mainFrameNavigations);
                }
            };
            await page.RouteAsync(FrameworkRoute, route =>
                route.Request.Url.Contains(".wasm", StringComparison.OrdinalIgnoreCase)
                    ? route.AbortAsync("connectionreset")
                    : route.ContinueAsync());

            await GotoWithoutBootRecoveryAsync(
                page, $"{BaseUrl}/scheduler", "the spec proves the helper does not recover a boot", WaitUntilState.DOMContentLoaded);
            await page.WaitForTimeoutAsync(8_000); // longer than the 6 s budget a recovering helper would have used

            Assert.AreEqual(1, Volatile.Read(ref mainFrameNavigations), "one navigation and no controlled reload");
            Assert.IsFalse(
                await page.EvaluateAsync<bool>("() => document.body !== null && document.body.hasAttribute('data-blazor-ready')"),
                "the cut boot never reports ready");
        });
    }

    private static async Task WithReadyBudgetAsync(int milliseconds, Func<Task> body)
    {
        var previous = Environment.GetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable);
        Environment.SetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable, milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            await body();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ReadinessTimeouts.EnvironmentVariable, previous);
        }
    }
}

/// <summary>
/// <see cref="PlaywrightTestBase.NavigateAppReadyAsync"/> on the Blazor Server demo, whose host renders
/// <c>&lt;html lang&gt;</c> from the request culture: the language switcher force-loads the page, so the new document
/// is visible as a second main-frame document request AND a changed <c>html[lang]</c>.
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class BootRecoveryServerE2ETests : ServerTestBase
{
    [TestMethod]
    public async Task NavigateAppReady_ALanguageSwitch_WaitsForTheNewDocument()
    {
        var mainFrameNavigations = 0;
        var page = await OpenAppPageAsync(
            $"{BaseUrl}/",
            new BrowserNewContextOptions { Locale = "en-US" },
            context =>
            {
                context.Page += (_, newPage) =>
                {
                    newPage.Request += (_, request) =>
                    {
                        if (request.IsNavigationRequest && request.Frame.ParentFrame is null)
                        {
                            Interlocked.Increment(ref mainFrameNavigations);
                        }
                    };
                };
                return Task.CompletedTask;
            });
        Assert.AreEqual("en", await page.Locator("html").GetAttributeAsync("lang"));
        Assert.AreEqual(1, Volatile.Read(ref mainFrameNavigations), "the first boot is one document request");

        // The click is deferred inside the page, so the trigger returns BEFORE the navigation starts (a Playwright
        // click would itself wait for the navigation it causes and hide a gate that passes on the old document).
        await NavigateAppReadyAsync(page, () => page.EvaluateAsync(
            "() => { setTimeout(() => document.querySelector(\"[data-testid='language-switcher-cs']\").click(), 1500); }"));

        // Asserted straight after the helper returned: a gate that passed on the OLD document would still see
        // one document request and lang="en".
        Assert.AreEqual(2, Volatile.Read(ref mainFrameNavigations), "the switch is exactly one more main-frame document request");
        Assert.AreEqual("cs", await page.Locator("html").GetAttributeAsync("lang"), "the helper returned on the NEW document");
        await Assertions.Expect(page.Locator("body")).ToHaveAttributeAsync("data-blazor-ready", "true");
    }
}