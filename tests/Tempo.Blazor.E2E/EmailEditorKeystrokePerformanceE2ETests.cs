using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Phase 7 evidence for the "&lt; 100 ms per keystroke in Server mode" target, browser half. A 200-block
/// template is created through the demo API, opened in the Blazor Server demo (:7107, a real SignalR circuit) and
/// 50 keystrokes are typed into the inspector's button-text field. For every keystroke the latency is
/// <c>performance.now()</c> at the <c>input</c> event (capture phase, before Blazor sees it) to the first mutation a
/// <c>MutationObserver</c> reports on the edited canvas block: browser to server, the commit (history snapshot, dirty
/// check, emission, auto-save scheduling), the render diff and the way back. The p50/p95 table goes to the test output.
/// <para>
/// A wall-clock budget, so it carries <c>TestCategory=Performance</c> and runs in the perf lane
/// (<c>scripts/run-e2e-perf.ps1 -Filter "FullyQualifiedName~EmailEditorKeystrokePerformance"</c>), not under the load of the
/// full lane (docs/e2e-test-lanes.md). The bound is the plan's target itself: p95 &lt; 100 ms.
/// </para>
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EmailEditorKeystrokePerformanceE2ETests : EmailTemplateRenderModeTestBase
{
    private const int BlockCount = 200;
    private const int Keystrokes = 50;
    private const int Warmup = 5;

    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";

    /// <summary>The canonical document JSON (camelCase, polymorphic <c>$type</c>) with <paramref name="count"/> blocks.</summary>
    private static string LargeDocumentJson(int count, out string targetId)
    {
        targetId = Guid.NewGuid().ToString();
        var sections = new JsonArray();
        var perSection = count / 10;
        for (var s = 0; s < 10; s++)
        {
            var blocks = new JsonArray();
            for (var i = 0; i < perSection; i++)
            {
                if (s == 5 && i == 10)
                {
                    blocks.Add(new JsonObject
                    {
                        ["$type"] = "button",
                        ["id"] = targetId,
                        ["text"] = "Go",
                        ["href"] = "https://example.com",
                    });
                }
                else
                {
                    blocks.Add(new JsonObject
                    {
                        ["$type"] = "text",
                        ["id"] = Guid.NewGuid().ToString(),
                        ["content"] = $"<p>Row {s}-{i} of the body copy</p>",
                    });
                }
            }

            sections.Add(new JsonObject
            {
                ["id"] = Guid.NewGuid().ToString(),
                ["columns"] = new JsonArray(new JsonObject
                {
                    ["id"] = Guid.NewGuid().ToString(),
                    ["blocks"] = blocks,
                }),
            });
        }

        return new JsonObject
        {
            ["schemaVersion"] = 1,
            ["subject"] = "Keystroke perf",
            ["sections"] = sections,
        }.ToJsonString();
    }

    private static double Percentile(List<double> sorted, double fraction)
        => sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(fraction * sorted.Count) - 1)];

    [TestMethod]
    [TestCategory("Performance")]
    public async Task Server_Keystroke_On200Blocks_P95_IsUnder100Ms()
    {
        using var api = DemoApiTestHttp.CreateClient();
        var created = await api.PostAsJsonAsync("/api/email-templates", new
        {
            name = $"Keystroke perf {Guid.NewGuid():N}"[..30],
            subject = "Keystroke perf",
            language = "en",
            contentJson = LargeDocumentJson(BlockCount, out var targetId),
        });
        created.EnsureSuccessStatusCode();
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetGuid();

        try
        {
            var context = await Browser.NewContextAsync(new BrowserNewContextOptions
            {
                ViewportSize = new ViewportSize { Width = 1440, Height = 900 },
                Locale = "en-US",
                IgnoreHTTPSErrors = true,
            });
            RegisterContext(context);
            var page = await context.NewPageAsync();
            await page.AddInitScriptAsync("try { localStorage.setItem('tm-demo-editor-layout', 'Desktop'); } catch (e) { }");
            await GotoAppReadyAsync(page, $"{BaseUrl}/email-templates/edit/{id}");
            await page.WaitForSelectorAsync("[data-tm-canvas-doc]", new() { Timeout = 60000 });
            (await page.Locator("[data-tm-block-id]").CountAsync()).Should().Be(BlockCount);

            await page.Locator($"[data-tm-block-id='{targetId}']").ClickAsync();
            var field = page.Locator("[data-tm-prop='Text'] input").First;
            await field.WaitForAsync(new() { Timeout = 15000 });
            await field.ClickAsync();
            await page.Keyboard.PressAsync("End");

            // `input` (capture) stamps the keystroke; the first mutation on the edited canvas block after it closes
            // the sample. Mutations on any other block are recorded separately: they must stay at zero.
            await page.EvaluateAsync(
                """
                (targetId) => {
                    const block = document.querySelector(`[data-tm-block-id='${targetId}']`);
                    window.__perf = { pending: [], done: [], foreign: 0 };
                    document.addEventListener('input', () => window.__perf.pending.push(performance.now()), true);
                    new MutationObserver(() => {
                        const now = performance.now();
                        while (window.__perf.pending.length) window.__perf.done.push(now - window.__perf.pending.shift());
                    }).observe(block, { childList: true, subtree: true, characterData: true, attributes: true });
                    const canvas = document.querySelector('[data-tm-canvas-doc]');
                    new MutationObserver(records => {
                        for (const r of records) {
                            const el = r.target.nodeType === 1 ? r.target : r.target.parentElement;
                            const owner = el ? el.closest('[data-tm-block-id]') : null;
                            if (owner && owner.getAttribute('data-tm-block-id') !== targetId) window.__perf.foreign++;
                        }
                    }).observe(canvas, { childList: true, subtree: true, characterData: true, attributes: true });
                }
                """,
                targetId);

            for (var i = 0; i < Warmup + Keystrokes; i++)
            {
                var before = await page.EvaluateAsync<int>("() => window.__perf.done.length");
                await page.Keyboard.PressAsync(((char)('a' + i % 26)).ToString());
                await page.WaitForFunctionAsync($"() => window.__perf.done.length > {before}", null, new() { Timeout = 15000 });
                // Let the coalescing/auto-save settle between keystrokes so each sample is an isolated one.
                await page.WaitForTimeoutAsync(120);
            }

            var all = await page.EvaluateAsync<double[]>("() => window.__perf.done");
            var foreign = await page.EvaluateAsync<int>("() => window.__perf.foreign");
            var samples = all.Skip(Warmup).OrderBy(x => x).ToList();
            samples.Should().HaveCount(Keystrokes);

            var p50 = Percentile(samples, 0.50);
            var p95 = Percentile(samples, 0.95);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"KEYSTROKE-PERF Server blocks={BlockCount} n={samples.Count} p50={p50:F1} ms p95={p95:F1} ms "
                + $"min={samples[0]:F1} ms max={samples[^1]:F1} ms foreignBlockMutations={foreign}"));

            foreign.Should().Be(0, "a keystroke repaints only the edited block");
            p95.Should().BeLessThan(100,
                string.Create(CultureInfo.InvariantCulture,
                    $"keystroke to painted canvas block on {BlockCount} blocks in Server mode (measured p50 {p50:F1} ms, p95 {p95:F1} ms)"));
        }
        finally
        {
            await api.DeleteAsync($"/api/email-templates/{id}");
        }
    }
}
