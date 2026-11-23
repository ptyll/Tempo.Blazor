using System.Text.Json;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// DIAGNOSTIC PROBE (temporary) — reproduces the user-reported stall while HOLDING a key (auto-repeat):
/// one ~540 ms freeze ~1.4 s into the hold (video 2026-06-10 01-51-31). During auto-repeat (~30 ms/key) all
/// our debounces keep resetting, so the B7 keydown fix is not the culprit. This probe simulates the hold,
/// watches requestAnimationFrame gaps to time the stall, and runs the CDP CPU profiler over the hold so the
/// blocked-thread window can be attributed to real functions (the Phase-8 proven method — point timers lie).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorCanvasKeyHoldProbeE2ETests : WasmTestBase
{
    /// <summary>Without this, every probe run leaves 200 typed+autosaved chars in the persisted demo doc,
    /// so successive profiles measure an ever-growing document (and the autosave of the bloat) — the runs
    /// stop being comparable.</summary>
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync() => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    public async Task HoldKey_ProfileStallsDuringAutoRepeat()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor");
        await page.WaitForSelectorAsync("[data-testid='document-canvas-engine-host'][data-canvas-engine-ready='true']", new PageWaitForSelectorOptions { State = WaitForSelectorState.Attached, Timeout = 120_000 });
        await WaitForPredicateAsync(page, "() => document.querySelectorAll('[data-canvas-text-rect]').length > 0", 120_000);
        await page.WaitForTimeoutAsync(500);

        var rect = await page.Locator("[data-canvas-text-rect]").First.BoundingBoxAsync();
        await page.Mouse.ClickAsync(rect!.X + (rect.Width / 2), rect.Y + (rect.Height / 2));
        await page.WaitForTimeoutAsync(400);

        // rAF gap watchdog: records gaps > 100 ms with their timestamps.
        await page.EvaluateAsync(@"() => {
            window.__rafGaps = [];
            window.__rafStart = performance.now();
            let last = performance.now();
            const tick = () => {
                const now = performance.now();
                if (now - last > 100) { window.__rafGaps.push({ at: Math.round(now - window.__rafStart), gap: Math.round(now - last) }); }
                last = now;
                window.__rafLoop = requestAnimationFrame(tick);
            };
            window.__rafLoop = requestAnimationFrame(tick);
        }");

        var cdp = await page.Context.NewCDPSessionAsync(page);
        await cdp.SendAsync("Profiler.enable");
        await cdp.SendAsync("Profiler.setSamplingInterval", new Dictionary<string, object> { ["interval"] = 200 });
        await cdp.SendAsync("Profiler.start");

        // Simulate holding a key: auto-repeat ~33 keys/s for ~6 s (~200 keystrokes). Playwright's keyboard
        // does not auto-repeat, so dispatch individual presses at the auto-repeat cadence.
        for (var i = 0; i < 200; i++)
        {
            await page.Keyboard.PressAsync("a");
            await page.WaitForTimeoutAsync(12); // press itself has overhead; total ≈ 30 ms/key
        }

        var profile = await cdp.SendAsync("Profiler.stop");
        await page.EvaluateAsync("() => cancelAnimationFrame(window.__rafLoop)");

        var gapsJson = await page.EvaluateAsync<string>("() => JSON.stringify(window.__rafGaps)");
        TestContext.WriteLine($"RAF GAPS >100ms (at=ms from hold start): {gapsJson}");

        // Aggregate self time per function from the CDP profile.
        Assert.IsNotNull(profile, "Profiler.stop returned no profile.");
        var prof = profile.Value.GetProperty("profile");
        var nodes = prof.GetProperty("nodes").EnumerateArray().ToList();
        var samples = prof.GetProperty("samples").EnumerateArray().Select(s => s.GetInt32()).ToList();
        var deltas = prof.GetProperty("timeDeltas").EnumerateArray().Select(d => d.GetInt64()).ToList();

        var nodeById = nodes.ToDictionary(
            n => n.GetProperty("id").GetInt32(),
            n =>
            {
                var f = n.GetProperty("callFrame");
                var name = f.GetProperty("functionName").GetString();
                var url = f.GetProperty("url").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(name)) { name = "(anonymous)"; }
                var shortUrl = url.Length > 0 ? url[(url.LastIndexOf('/') + 1)..] : string.Empty;
                return $"{name} [{shortUrl}]";
            });

        var selfMicros = new Dictionary<string, long>();
        for (var i = 0; i < samples.Count && i < deltas.Count; i++)
        {
            var key = nodeById.TryGetValue(samples[i], out var n) ? n : $"node#{samples[i]}";
            selfMicros[key] = selfMicros.GetValueOrDefault(key) + deltas[i];
        }

        TestContext.WriteLine("TOP 25 SELF-TIME (ms) during key hold:");
        foreach (var (name, micros) in selfMicros.OrderByDescending(kv => kv.Value).Take(25))
        {
            TestContext.WriteLine($"  {micros / 1000.0,8:F1}  {name}");
        }

        // Caller chains: per-NODE self time (not merged by name), then walk up the parent chain so the
        // anonymous/native hot spots (e.g. visitNode with an empty URL) get attributed to real callers.
        var parentById = new Dictionary<int, int>();
        foreach (var n in nodes)
        {
            var id = n.GetProperty("id").GetInt32();
            if (n.TryGetProperty("children", out var children))
            {
                foreach (var c in children.EnumerateArray()) { parentById[c.GetInt32()] = id; }
            }
        }

        var selfByNode = new Dictionary<int, long>();
        for (var i = 0; i < samples.Count && i < deltas.Count; i++)
        {
            selfByNode[samples[i]] = selfByNode.GetValueOrDefault(samples[i]) + deltas[i];
        }

        TestContext.WriteLine("TOP 12 NODES with caller chains:");
        foreach (var (nodeId, micros) in selfByNode.OrderByDescending(kv => kv.Value).Take(12))
        {
            var chain = new List<string>();
            var cur = nodeId;
            for (var depth = 0; depth < 12 && nodeById.ContainsKey(cur); depth++)
            {
                chain.Add(nodeById[cur]);
                if (!parentById.TryGetValue(cur, out cur)) { break; }
            }

            TestContext.WriteLine($"  {micros / 1000.0,8:F1}  {string.Join(" <- ", chain)}");
        }

        // Targeted attribution: some hot names (visitNode, pf, style-store helpers) spread their self time
        // across many call sites, so per-node top lists miss them. Group their self time by caller-name chain.
        string[] targets = ["visitNode", "visitChild", "pf", "ensureStyleStore", "normalizeStyleDefinition", "clone"];
        foreach (var target in targets)
        {
            var byCaller = new Dictionary<string, long>();
            foreach (var (nodeId, micros) in selfByNode)
            {
                if (!nodeById.TryGetValue(nodeId, out var name) || !name.StartsWith(target + " ", StringComparison.Ordinal))
                {
                    continue;
                }

                var chain = new List<string>();
                var cur = nodeId;
                for (var depth = 0; depth < 6; depth++)
                {
                    if (!parentById.TryGetValue(cur, out cur) || !nodeById.TryGetValue(cur, out var pn)) { break; }
                    chain.Add(pn);
                }

                var key = string.Join(" <- ", chain);
                byCaller[key] = byCaller.GetValueOrDefault(key) + micros;
            }

            if (byCaller.Count == 0) { continue; }
            TestContext.WriteLine($"CALLERS OF {target} (ms):");
            foreach (var (chain, micros) in byCaller.OrderByDescending(kv => kv.Value).Take(5))
            {
                TestContext.WriteLine($"  {micros / 1000.0,8:F1}  <- {chain}");
            }
        }

        // Probe, not a gate: always pass; the value is in the logged output.
        Assert.IsTrue(samples.Count > 0, "Profiler captured no samples.");
    }
}
