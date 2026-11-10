using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 10 — the two-minute budget on the SERVER host. The WASM lane measures the simulator against a
/// local render; a Server circuit pays a round trip per paint instead, so the same ten-events-per-second budget is NOT
/// implied by the WASM result: a throttling regression on the Server side shows up as a burst of events after each round
/// trip (the paint gate folding ticks it never painted) while the ten-second total still looks right.
/// <para>
/// The inherited test is the shared budget (<see cref="AiAgentVisualizationsPerformanceE2EBase"/>): ten events per
/// second per ten-second window, a press still answered in under three seconds after two minutes of streaming, and a
/// flat JS heap. The Server-specific test below samples the page ONCE PER SECOND and asserts the stream is spread across
/// the window instead of arriving in one lump — the shape a circuit round-trip regression takes, and the thing the
/// ten-second aggregate cannot see.
/// </para>
/// <para>
/// Category <c>Performance</c>: wall-clock budgets only hold on an idle machine, so both tests stay out of the full lane
/// and run from the performance lane (<c>scripts/run-e2e-perf.ps1</c>) together with the WASM measurement.
/// </para>
/// </summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("Server")]
public sealed class AiAgentVisualizationsServerPerformanceE2ETests : AiAgentVisualizationsPerformanceE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";

    /// <summary>
    /// Ten seconds of streaming, sampled every second: every sample must advance (a circuit that answered nothing until
    /// the end of the window would leave the intermediate samples flat), each one-second delta must stay inside a wide
    /// band around ten events, and the read-out must never stall for a whole second. This is what "the Server host pays
    /// round trips per paint" means in observable terms.
    /// </summary>
    [TestMethod]
    public async Task TheServerStreamIsSpreadAcrossEverySecond_NotBatchedAfterTheRoundTrip()
    {
        var page = await OpenAsync();
        await PlayAsync(page);

        var samples = new List<int>();
        for (var second = 1; second <= 10; second++)
        {
            await page.WaitForTimeoutAsync(1_000);
            samples.Add(int.Parse(await TextAsync(page, "ai-viz-events"), CultureInfo.InvariantCulture));
        }

        var deltas = samples.Zip(samples.Skip(1), (previous, current) => current - previous).ToList();

        Assert.IsTrue(deltas.All(delta => delta > 0),
            "the circuit keeps painting while the run streams, so no second is flat (deltas: "
            + string.Join(", ", deltas) + ")");
        Assert.IsTrue(deltas.All(delta => delta >= 3 && delta <= 25),
            "every one-second sample stays inside a wide band around ten events per second (deltas: "
            + string.Join(", ", deltas) + ")");
        Assert.IsTrue(samples[^1] - samples[0] >= 40,
            $"the whole ten-second window really applied the stream (from {samples[0]} to {samples[^1]})");

        // The page is still interactive mid-stream: the speed switch answers over the circuit.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await page.Locator("[data-testid='ai-viz-speed'] [data-testid='segmented-option']").Last.ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-rate']\")?.textContent?.trim() === '20'",
            new PageWaitForFunctionOptions { Timeout = 5000 });
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 3000,
            $"a speed change reaches the Server run within three seconds mid-stream ({stopwatch.ElapsedMilliseconds} ms)");

        // And the page stops when asked: a pause is not queued behind the stream.
        stopwatch.Restart();
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-state']\")?.textContent?.trim() === 'Paused'",
            new PageWaitForFunctionOptions { Timeout = 5000 });
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 3000,
            $"the pause is answered within three seconds on the Server host ({stopwatch.ElapsedMilliseconds} ms)");
    }
}

/// <summary>
/// The phase's wall-clock budget: the simulator holds ten events per second for two minutes without growing the heap
/// and without locking the UI. Excluded from the full lane by its category; run it from the performance lane with the
/// machine idle.
/// <para>
/// Plan 7ba95ed6, Fáze 10 — the budget is LANE-AGNOSTIC now. The WASM subclass below is the original measurement; the
/// Server subclass in <c>AiAgentVisualizationsE2ELanes.Performance.cs</c> holds the same rate and the same
/// responsiveness bound over a real circuit, where every applied event costs a round trip per paint instead of a local
/// render. Both classes log their per-window numbers so a red run is judged from the breakdown, never from a raised
/// threshold.
/// </para>
/// </summary>
public abstract class AiAgentVisualizationsPerformanceE2EBase : AiAgentVisualizationsPageBase
{
    [TestMethod]
    public async Task TenEventsPerSecondForTwoMinutes_KeepTheHeapFlatAndTheUiResponsive()
    {
        const int windows = 12;
        const int windowMs = 10_000;

        var page = await OpenAsync();
        await PlayAsync(page);

        var samples = new List<(int Window, long Heap, int Events)>();
        var lastEvents = int.Parse(await TextAsync(page, "ai-viz-events"), CultureInfo.InvariantCulture);

        for (var window = 1; window <= windows; window++)
        {
            await page.WaitForTimeoutAsync(windowMs);

            var heap = await page.EvaluateAsync<long>(
                "() => { if (window.gc) { window.gc(); } return performance.memory ? performance.memory.usedJSHeapSize : 0; }");
            var events = int.Parse(await TextAsync(page, "ai-viz-events"), CultureInfo.InvariantCulture);
            samples.Add((window, heap, events));

            var applied = events - lastEvents;
            lastEvents = events;

            // The rate is the budget: ten events per second, with the paint gate folding ticks instead of dropping them.
            Assert.IsTrue(applied is >= 60 and <= 130,
                $"window {window}: {applied} events applied in {windowMs / 1000}s at 1× (budget 100 ± 30)");
        }

        // The UI stays live: the pause button reacts and the read-out stops.
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await page.Locator("[data-testid='ai-viz-play']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector(\"[data-testid='ai-viz-state']\")?.textContent?.trim() === 'Paused'",
            new PageWaitForFunctionOptions { Timeout = 5000 });
        stopwatch.Stop();
        Assert.IsTrue(stopwatch.ElapsedMilliseconds < 3000,
            $"the page answers a press within three seconds after two minutes of streaming ({stopwatch.ElapsedMilliseconds} ms)");

        // The heap plateaus: after the warm-up windows, two minutes of streaming must not add megabytes.
        var warm = samples[2].Heap;
        var last = samples[^1].Heap;
        Assert.IsTrue(warm > 0 && last > 0, "the browser exposes performance.memory");
        var growthMb = (last - warm) / (1024d * 1024d);
        Assert.IsTrue(growthMb < 16d,
            $"the heap grew {growthMb:0.0} MB between window 3 and window {windows} (budget 16 MB; "
            + $"samples {string.Join(", ", samples.Select(sample => $"{sample.Window}:{sample.Heap / (1024 * 1024)}MB"))})");
    }
}

/// <summary>The WASM lane of the two-minute budget (the original measurement).</summary>
[TestClass]
[TestCategory("Performance")]
[TestCategory("WASM")]
public sealed class AiAgentVisualizationsPerformanceE2ETests : AiAgentVisualizationsPerformanceE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}
