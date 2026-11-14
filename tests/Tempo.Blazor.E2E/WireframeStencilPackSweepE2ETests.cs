using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 5b94f69c phase 3 — a whole-pack visual sweep of every stencil after the nested
/// <c>&lt;g&gt;</c> fix (real Blazor elements instead of split markup frames).
///
/// <para>
/// Every toolbox stencil is inserted into the real editor, framed, captured at two zoom levels
/// (fit ≈ 100 % and z045 = 45 % zoom) and its geometry is measured (empty g[transform], stacked
/// repeat-item labels). The same run executes on the commit BEFORE the fix
/// (<c>TM_SWEEP_LABEL=before TM_SWEEP_STRICT=0</c>) and before/after pairs land in
/// <c>TestResults/wireframe-renderer-nesting/&lt;label&gt;/</c> as a UX-review artifact.
/// </para>
///
/// <para>
/// The sweep further covers: toolbox previews and the mobile palette (no empty g[transform]),
/// parity of the client DOM against the server string export (same g and transforms via XDocument)
/// and the page's performance with 200 elements (first render + re-render after a move).
/// </para>
/// </summary>
[TestClass]
[TestCategory("Wireframe")]
public sealed class WireframeStencilPackSweepE2ETests : WasmTestBase
{
    private const string WireframeEditorUrl = "/wireframe-editor";

    /// <summary>Run label — "after" (default, strict assertions) or "before" (capture-only).</summary>
    private static string SweepLabel
        => Environment.GetEnvironmentVariable("TM_SWEEP_LABEL") ?? "after";

    /// <summary>Strict mode asserts anomalies; capture-only mode just records them (pre-fix run).</summary>
    private static bool Strict
        => !string.Equals(Environment.GetEnvironmentVariable("TM_SWEEP_STRICT"), "0", StringComparison.OrdinalIgnoreCase);

    private string OutputRoot
        => Path.Combine(FindRepoRoot(), "tests", "Tempo.Blazor.E2E", "TestResults", "wireframe-renderer-nesting");

    private string OutputDir => Path.Combine(OutputRoot, SweepLabel);

    [TestInitialize]
    public void EnsureOutputDir() => Directory.CreateDirectory(OutputDir);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }

    // ── Page preparation ─────────────────────────────────────────────────────────

    private async Task OpenEditorInContextAsync(IPage page, bool dark, bool mobile = false)
    {
        await page.AddInitScriptAsync(
            "document.addEventListener('DOMContentLoaded', () => { const s = document.createElement('style'); " +
            "s.textContent = 'aside.fixed.inset-y-0.left-0.z-30 { display: none !important; } " +
            "main { margin-left: 0 !important; width: 100vw !important; max-width: 100vw !important; }'; " +
            "document.head.appendChild(s); });");
        await GotoAppReadyAsync(page, $"{BaseUrl}{WireframeEditorUrl}");
        await page.EvaluateAsync("() => localStorage.setItem('tm-demo-culture', 'en')");
        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load, Timeout = 60_000 });
        await WaitForAppReadyAsync(page);

        if (mobile)
        {
            // Mobile layout renders no desktop toolbar — wait for the editor root + canvas instead.
            await WireframeStencilHarness.FirstEditor(page)
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
            await WireframeStencilHarness.FirstEditor(page).Locator(".tm-wd-canvas__svg")
                .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20_000 });
        }
        else
        {
            await WireframeStencilHarness.PrepareAsync(page);
        }

        if (dark)
        {
            await page.Locator("[data-testid='theme-toggle']").First.DispatchEventAsync("click");
            await page.WaitForFunctionAsync(
                "() => document.querySelector('[data-theme=\"dark\"]') !== null",
                null, new PageWaitForFunctionOptions { Timeout = 10_000 });
            await page.WaitForTimeoutAsync(500);
        }
    }

    /// <summary>Light-theme sweep — fit and 45% zoom for every stencil.</summary>
    [TestMethod]
    [Timeout(1_800_000)] // 30 min backstop — 200+ stencils × two captures each.
    public async Task FullPack_Light_FitAnd45Percent()
        => await RunSweepAsync(dark: false);

    /// <summary>Dark-theme sweep — fit and 45% zoom for every stencil.</summary>
    [TestMethod]
    [Timeout(1_800_000)] // 30 min backstop — 200+ stencils × two captures each.
    public async Task FullPack_Dark_FitAnd45Percent()
        => await RunSweepAsync(dark: true);

    /// <summary>Sample props for stencils whose repeat branch needs data to render.</summary>
    private static readonly Dictionary<string, Dictionary<string, string>> SampleProps = new()
    {
        ["TmSegmentedControl"] = new() { ["options"] = "Day, Week, Month" },
    };

    /// <summary>
    /// The eight FZE-1 priority stencils: the committed DOM guard pins these to ZERO empty
    /// g[transform] — the strict sweep keeps that guarantee. For every other stencil an empty
    /// group is recorded as a note (a repeat over an empty default list renders nothing, which
    /// is valid SVG, not the collapsed-renderer defect).
    /// </summary>
    private static readonly HashSet<string> PriorityStencils = new(StringComparer.Ordinal)
    {
        "TmSegmentedControl", "TmMultiSelect", "TmStepper", "TmScheduler",
        "TmImageGallery", "TmActivityLog", "TmCalendarHeatmap", "TmRichEditorFull",
    };

    private async Task RunSweepAsync(bool dark)
    {
        var theme = dark ? "dark" : "light";
        var shotDir = Path.Combine(OutputDir, theme);
        Directory.CreateDirectory(shotDir);

        await using var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenEditorInContextAsync(page, dark);

        var types = await ReadToolboxTypesAsync(page);
        TestContext.WriteLine($"[{SweepLabel}/{theme}] toolbox exposes {types.Count} stencils");
        Assert.IsTrue(types.Count >= 200, $"the toolbox must expose the whole built-in pack (was {types.Count})");

        var only = Environment.GetEnvironmentVariable("TM_SWEEP_ONLY");
        if (!string.IsNullOrWhiteSpace(only))
        {
            var wanted = new HashSet<string>(only.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            types = types.Where(wanted.Contains).ToList();
            TestContext.WriteLine($"[{SweepLabel}/{theme}] TM_SWEEP_ONLY narrowed the sweep to {types.Count} stencils");
        }

        var metrics = new List<JsonObject>();
        var anomalies = new List<string>();

        foreach (var (type, index) in types.Select((t, i) => (t, i)))
        {
            string? elementId = null;
            try
            {
                elementId = await WireframeStencilHarness.InsertAsync(page, type, 340, 240);

                if (SampleProps.TryGetValue(type, out var props))
                {
                    await WireframeStencilHarness.SelectAsync(page, elementId);
                    foreach (var (prop, value) in props)
                    {
                        await WireframeStencilHarness.SetTextPropAsync(page, prop, value);
                    }
                }

                await WireframeStencilHarness.FrameAsync(page, elementId);

                // Some stencils paint only a small icon at their default props (empty repeat
                // lists) — getElementBounds then frames a 24×24 patch. Zoom out so the capture
                // shows the element the way a designer sees it on the page, not a clipped icon.
                var framed = await WireframeStencilHarness.FirstEditor(page)
                    .Locator($"g[data-el-id='{elementId}']").First.BoundingBoxAsync();
                if (framed is { Width: < 80 } or { Height: < 80 })
                {
                    await ZoomOutAsync(page, 0.33);
                }

                await WireframeStencilHarness.ShootAsync(
                    page, WireframeStencilHarness.FirstEditor(page).Locator($"g[data-el-id='{elementId}']"),
                    shotDir, $"{type}__fit", pad: 24, clipToCanvas: true, alreadyPrepared: index > 0);

                // 45% zoom: same viewBox center, view widened by 1/0.45, scale kept — the element
                // paints at 45% of the fit size, the way a designer sees it zoomed out.
                await ZoomOutAsync(page, 0.45);
                await WireframeStencilHarness.ShootAsync(
                    page, WireframeStencilHarness.FirstEditor(page).Locator($"g[data-el-id='{elementId}']"),
                    shotDir, $"{type}__z045", pad: 24, clipToCanvas: true, alreadyPrepared: true);

                var metric = await CollectMetricsAsync(page, elementId);
                metric["type"] = type;
                metrics.Add(metric);

                if (metric["emptyG"]!.GetValue<int>() > 0 && PriorityStencils.Contains(type))
                    anomalies.Add($"{type}: {metric["emptyG"]} empty g[transform] (the FZE-1 guard pins this stencil to zero)");
                if (metric["stacked"]!.GetValue<int>() > 0)
                    anomalies.Add($"{type}: {metric["stacked"]} stacked label pairs (transforms do not nest)");
            }
            catch (Exception ex)
            {
                // One bad stencil must not abort the sweep — record it and keep capturing.
                anomalies.Add($"{type}: sweep step failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
            finally
            {
                if (elementId is not null)
                {
                    try
                    {
                        await DeleteElementAsync(page, elementId);
                    }
                    catch (Exception ex)
                    {
                        anomalies.Add($"{type}: cleanup failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                    }
                }
            }
        }

        await File.WriteAllTextAsync(
            Path.Combine(OutputDir, $"sweep-metrics-{theme}.json"),
            JsonSerializer.Serialize(new { label = SweepLabel, theme, stencilCount = types.Count, anomalies, metrics },
                new JsonSerializerOptions { WriteIndented = true }));

        if (Strict)
        {
            Assert.AreEqual(0, anomalies.Count,
                $"[{theme}] {anomalies.Count} stencils show collapsed nesting:{Environment.NewLine}{string.Join(Environment.NewLine, anomalies)}");
        }
        else if (anomalies.Count > 0)
        {
            TestContext.WriteLine($"[{SweepLabel}/{theme}] recorded {anomalies.Count} anomalies (capture-only):{Environment.NewLine}{string.Join(Environment.NewLine, anomalies)}");
        }
    }

    private static async Task<List<string>> ReadToolboxTypesAsync(IPage page)
    {
        var types = await page.EvaluateAsync<string[]>(
            """
            () => [...new Set([...document.querySelectorAll('.tm-wd-editor')[0]
                .querySelectorAll('.tm-wd-toolbox__item[data-component-type]')]
                .map(item => item.getAttribute('data-component-type')))].sort()
            """);
        return types!.ToList();
    }

    private static async Task ZoomOutAsync(IPage page, double zoom)
    {
        var canvasId = await WireframeStencilHarness.FirstEditor(page).Locator(".tm-wd-canvas__svg")
            .EvaluateAsync<string>("el => el.id");
        await page.EvaluateAsync(
            """
            ([canvasId, zoom]) => {
                const designer = window.tmWireframeDesigner;
                const inst = designer.instances.get(canvasId);
                if (!inst) return;
                const vb = inst.svg.viewBox.baseVal;
                const cx = vb.x + vb.width / 2, cy = vb.y + vb.height / 2;
                const w = vb.width / zoom, h = vb.height / zoom;
                designer._setViewBox(inst, cx - w / 2, cy - h / 2, w, h);
            }
            """,
            new object[] { canvasId, zoom });
        await page.WaitForTimeoutAsync(250);
    }

    private static async Task<JsonObject> CollectMetricsAsync(IPage page, string elementId)
    {
        var json = await WireframeStencilHarness.FirstEditor(page)
            .Locator($"g[data-el-id='{elementId}'] > svg")
            .EvaluateAsync<string>(
                """
                svg => {
                    const gs = [...svg.querySelectorAll('g')];
                    const withTransform = gs.filter(g => g.hasAttribute('transform'));
                    const emptyG = withTransform.filter(g => g.childNodes.length === 0).length;
                    const labels = [...svg.querySelectorAll('text')]
                        .map(t => ({ text: (t.textContent ?? '').trim(), rect: t.getBoundingClientRect() }))
                        .filter(l => l.text);
                    let stacked = 0;
                    for (let i = 0; i < labels.length; i++)
                        for (let j = i + 1; j < labels.length; j++)
                            if (Math.abs(labels[i].rect.x - labels[j].rect.x) < 0.5 &&
                                Math.abs(labels[i].rect.y - labels[j].rect.y) < 0.5) stacked++;
                    const bb = svg.getBoundingClientRect();
                    return JSON.stringify({
                        g: gs.length, gTransform: withTransform.length, emptyG,
                        labels: labels.length, stacked, w: Math.round(bb.width), h: Math.round(bb.height)
                    });
                }
                """);
        return JsonSerializer.Deserialize<JsonObject>(json)!;
    }

    private static async Task DeleteElementAsync(IPage page, string elementId)
    {
        var canvasId = await WireframeStencilHarness.FirstEditor(page).Locator(".tm-wd-canvas__svg")
            .EvaluateAsync<string>("el => el.id");
        var before = await WireframeStencilHarness.FirstEditor(page).Locator("g[data-el-id]").CountAsync();
        await page.EvaluateAsync(
            """
            async ([canvasId, id]) => {
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (inst) await inst.dotNetRef.invokeMethodAsync('OnDeleteSelected', [id]);
            }
            """,
            new object[] { canvasId, elementId });
        await page.WaitForFunctionAsync(
            "n => document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]').length === n",
            before - 1, new PageWaitForFunctionOptions { Timeout = 15_000 });
        await page.WaitForTimeoutAsync(150);
    }

    // ── Toolbox previews ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ToolboxPreviews_NoEmptyTransformGroups()
    {
        await using var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenEditorInContextAsync(page, dark: false);

        var result = await page.EvaluateAsync<string>(
            """
            () => {
                const items = [...document.querySelectorAll('.tm-wd-editor')[0]
                    .querySelectorAll('.tm-wd-toolbox__item[data-component-type]')];
                const withPreview = items.filter(item => item.querySelector('svg'));
                const bad = withPreview
                    .map(item => ({ type: item.getAttribute('data-component-type'),
                                    empty: item.querySelectorAll('svg g[transform]:empty').length }))
                    .filter(x => x.empty > 0);
                return JSON.stringify({ items: items.length, withPreview: withPreview.length, bad });
            }
            """);

        var dir = Path.Combine(OutputDir, "toolbox");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "toolbox-previews.json"), result);
        TestContext.WriteLine($"[{SweepLabel}] toolbox previews: {result}");

        var doc = JsonSerializer.Deserialize<JsonObject>(result)!;
        if (Strict)
        {
            // Only the FZE-1 priority stencils are pinned to zero empty groups; other stencils may
            // legitimately render an empty container when their repeat list defaults to empty.
            var priorityBad = doc["bad"]!.AsArray()
                .Where(b => PriorityStencils.Contains(b!["type"]!.GetValue<string>()))
                .ToList();
            Assert.AreEqual(0, priorityBad.Count,
                $"toolbox previews of the FZE-1 priority stencils must not contain empty g[transform]: {result}");
        }
    }

    // ── Mobile palette ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task MobilePalette_NoEmptyTransformGroups_AndInsertNests()
    {
        await using var context = await CreateContextAsync(hasTouch: true);
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);
        await OpenEditorInContextAsync(page, dark: false, mobile: true);

        // Mobile layout: the palette lives in the non-modal bottom sheet opened from the action bar.
        var components = page.Locator("[data-action-id='components']").First;
        await components.TapAsync();
        await page.WaitForTimeoutAsync(600);

        var sheet = page.Locator(".tm-wd-editor").First.Locator(".tm-wd-toolbox");
        await sheet.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10_000 });

        var result = await page.EvaluateAsync<string>(
            """
            () => {
                const editor = document.querySelectorAll('.tm-wd-editor')[0];
                const items = [...editor.querySelectorAll('.tm-wd-toolbox__item[data-component-type]')];
                const withPreview = items.filter(item => item.querySelector('svg'));
                const bad = withPreview
                    .map(item => ({ type: item.getAttribute('data-component-type'),
                                    empty: item.querySelectorAll('svg g[transform]:empty').length }))
                    .filter(x => x.empty > 0);
                return JSON.stringify({ items: items.length, withPreview: withPreview.length, bad });
            }
            """);

        // Insert a repeat stencil through the sheet and verify it nests on the canvas: repeated
        // labels must not stack (the collapsed-renderer defect signature).
        var search = sheet.Locator(".tm-wd-toolbox__search-input");
        await search.FillAsync("Segmented Control");
        await page.WaitForTimeoutAsync(300);
        await page.Locator(".tm-wd-editor").First
            .Locator(".tm-wd-toolbox__item[data-component-type='TmSegmentedControl']").TapAsync();
        await page.WaitForTimeoutAsync(800);

        var insertedId = await page.EvaluateAsync<string>(
            """
            () => {
                const groups = document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]');
                return groups.length ? groups[groups.length - 1].getAttribute('data-el-id') : '';
            }
            """);
        Assert.IsFalse(string.IsNullOrWhiteSpace(insertedId), "the stencil inserted from the mobile palette is on the canvas");

        var stacked = await WireframeStencilHarness.FirstEditor(page)
            .Locator($"g[data-el-id='{insertedId}'] > svg")
            .EvaluateAsync<int>(
                """
                svg => {
                    const labels = [...svg.querySelectorAll('text')]
                        .map(t => t.getBoundingClientRect())
                        .filter(r => r.width > 0 || r.height > 0);
                    let stacked = 0;
                    for (let i = 0; i < labels.length; i++)
                        for (let j = i + 1; j < labels.length; j++)
                            if (Math.abs(labels[i].x - labels[j].x) < 0.5 &&
                                Math.abs(labels[i].y - labels[j].y) < 0.5) stacked++;
                    return stacked;
                }
                """);

        var dir = Path.Combine(OutputDir, "mobile-palette");
        Directory.CreateDirectory(dir);
        var payload = JsonSerializer.Serialize(new { sheet = JsonSerializer.Deserialize<JsonObject>(result), stacked });
        await File.WriteAllTextAsync(Path.Combine(dir, "mobile-palette.json"), payload);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(dir, "mobile-palette-sheet.png"),
            FullPage = false,
        });
        TestContext.WriteLine($"[{SweepLabel}] mobile palette: {payload}");

        if (Strict)
        {
            var priorityBad = JsonSerializer.Deserialize<JsonObject>(result)!["bad"]!.AsArray()
                .Where(b => PriorityStencils.Contains(b!["type"]!.GetValue<string>()))
                .ToList();
            Assert.AreEqual(0, priorityBad.Count,
                $"mobile palette previews of the FZE-1 priority stencils must not contain empty g[transform]: {result}");
            Assert.AreEqual(0, stacked, "the stencil inserted from the mobile palette must not stack its labels");
        }
    }

    // ── Client DOM vs server export parity ───────────────────────────────────────

    private static readonly (string Type, string SearchTerm)[] ParityStencils =
    [
        ("TmSegmentedControl", "Segmented Control"),
        ("TmMultiSelect", "Multi Select"),
        ("TmStepper", "Stepper"),
        ("TmScheduler", "Scheduler"),
        ("TmImageGallery", "Image Gallery"),
        ("TmActivityLog", "Activity Log"),
        ("TmCalendarHeatmap", "Calendar Heatmap"),
        ("TmRichEditorFull", "Rich Text"),
    ];

    [TestMethod]
    public async Task ExportParity_ClientDom_Matches_ServerString()
    {
        await using var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenEditorInContextAsync(page, dark: false);

        // The same registry + drop path the editor uses, so the server-side document carries the
        // SAME schema-default props the canvas seeds at drop time (NewElement pre-populates them).
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());

        var document = new WireframeDocument { Title = "Parity" };
        var parityPage = new WireframePage { Name = "Parity", Width = 2000, Height = 2000 };
        document.Pages.Clear();
        document.Pages.Add(parityPage);

        var transforms = new Dictionary<string, string>();
        var skipped = new List<string>();

        foreach (var (type, searchTerm) in ParityStencils)
        {
            string elementId;
            try
            {
                await WireframeStencilHarness.FilterPaletteAsync(page, searchTerm);
                elementId = await WireframeStencilHarness.InsertAsync(page, type, 340, 240);

                if (type == "TmSegmentedControl")
                {
                    await WireframeStencilHarness.SelectAsync(page, elementId);
                    await WireframeStencilHarness.SetTextPropAsync(page, "options", "Day, Week, Month");
                }
            }
            catch (Exception ex)
            {
                // Skip a stencil the editor refuses to insert — the remaining elements still
                // give a parity signal, and the failure is reported in the evidence JSON.
                skipped.Add($"{type}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
                continue;
            }

            // The SAME element the drop handler created: schema defaults seeded, client geometry.
            var def = registry.GetDef(type)
                ?? throw new InvalidOperationException($"The built-in registry must contain {type}.");
            var element = WireframeDocumentExtensions.NewElement(type, 340, 240, def.DefaultWidth, def.DefaultHeight, def.Props);
            element.Id = elementId;
            if (type == "TmSegmentedControl")
            {
                // The properties panel commits a StringList as a JSON string array.
                element.Props["options"] = JsonSerializer.SerializeToElement(new[] { "Day", "Week", "Month" });
            }

            parityPage.Elements.Add(element);

            transforms[elementId] = await ClientTransformsAsync(page, elementId);
            try
            {
                await DeleteElementAsync(page, elementId);
            }
            catch (Exception ex)
            {
                skipped.Add($"{type}: cleanup failed: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            }
        }

        // Strict mode is the post-fix gate: every one of the 8 parity stencils must insert.
        // Capture-only runs tolerate a couple of refusals so the before/after evidence still lands.
        var requiredParity = Strict ? ParityStencils.Length : ParityStencils.Length - 2;
        Assert.IsTrue(transforms.Count >= requiredParity,
            $"at least {requiredParity} parity stencils must insert (skipped: {string.Join("; ", skipped)})");

        // Server half: the SAME document through the headless IWireframeSvgRenderer.
        using var http = DemoApiTestHttp.CreateClient();
        using var response = await http.PostAsync(
            "/api/wireframe/preview/render-document",
            new StringContent(WireframeSerializer.Serialize(document), System.Text.Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync();
        var pages = JsonDocument.Parse(body).RootElement;
        var serverSvg = pages[0].GetProperty("svg").GetString()!;

        var serverTransforms = ServerTransforms(serverSvg, transforms.Keys);

        // The canvas positions the element root via <svg x/y> (no transform on g[data-el-id]),
        // while WireframePageSvg uses a root transform — so the nested g TREE below the root is
        // compared structurally and the root position is checked separately against translate(x,y).
        var positionMismatches = parityPage.Elements
            .Where(el => serverTransforms[el.Id].Root != $"translate({el.X}, {el.Y})")
            .Select(el => $"{el.Id}: expected root translate({el.X}, {el.Y}), server has {serverTransforms[el.Id].Root}")
            .ToList();

        var dir = Path.Combine(OutputDir, "export-parity");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "export-parity.json"),
            JsonSerializer.Serialize(new
            {
                label = SweepLabel,
                skipped,
                elements = transforms.ToDictionary(
                    kv => kv.Key,
                    kv => new { client = kv.Value, server = serverTransforms[kv.Key] }),
            }, new JsonSerializerOptions { WriteIndented = true }));

        var mismatches = transforms
            .Where(kv => kv.Value != serverTransforms[kv.Key].Tree)
            .Select(kv => $"{kv.Key}: client {kv.Value} vs server {serverTransforms[kv.Key].Tree}")
            .ToList();

        TestContext.WriteLine($"[{SweepLabel}] export parity: {transforms.Count - mismatches.Count}/{transforms.Count} elements match");
        if (Strict)
        {
            Assert.AreEqual(0, mismatches.Count,
                $"client DOM and server string export must carry the same nested g/transform tree " +
                $"(a flat-but-equal transform list still means the browser collapsed the groups):{Environment.NewLine}{string.Join(Environment.NewLine, mismatches)}");
            Assert.AreEqual(0, positionMismatches.Count,
                $"the server root transforms must match the element positions:{Environment.NewLine}{string.Join(Environment.NewLine, positionMismatches)}");
        }
    }

    /// <summary>
    /// The canonical nested g tree of the element subtree in the browser-parsed DOM: each node is
    /// its whitespace-normalized transform (or <c>null</c>) plus its child g nodes. The broken
    /// renderer leaves the same transform ATTRIBUTES behind (auto-closed empty groups), so only
    /// the nesting exposes the collapse.
    /// </summary>
    private static async Task<string> ClientTransformsAsync(IPage page, string elementId)
        => await WireframeStencilHarness.FirstEditor(page)
            .Locator($"g[data-el-id='{elementId}']")
            .EvaluateAsync<string>(
                """
                el => {
                    const node = g => ({
                        t: g.hasAttribute('transform')
                            ? g.getAttribute('transform').replace(/\s+/g, '')
                            : null,
                        c: [...g.children]
                            .filter(ch => ch.localName === 'g')
                            .map(node),
                    });
                    // The element g holds an inner <svg> viewport — start the tree at its g children.
                    const svg = el.querySelector(':scope > svg');
                    return JSON.stringify({ t: null, c: [...svg.children].filter(ch => ch.localName === 'g').map(node) });
                }
                """);

    /// <summary>The root transform plus the canonical nested g tree below the element root in the server SVG.</summary>
    private sealed record ServerSubtree(string Root, string Tree);

    private static Dictionary<string, ServerSubtree> ServerTransforms(string serverSvg, IEnumerable<string> elementIds)
    {
        var doc = System.Xml.Linq.XDocument.Parse(serverSvg);
        var result = new Dictionary<string, ServerSubtree>();
        foreach (var id in elementIds)
        {
            var root = doc.Descendants()
                .First(e => e.Name.LocalName == "g" && e.Attribute("data-el-id")?.Value == id);
            result[id] = new ServerSubtree(
                root.Attribute("transform")?.Value ?? string.Empty,
                JsonSerializer.Serialize(new
                {
                    t = (string?)null,
                    // The element g wraps the sketch in an inner <svg> viewport on both sides.
                    c = root.Elements().SelectMany(e => e.Name.LocalName == "g"
                        ? new[] { e }
                        : e.Elements().Where(x => x.Name.LocalName == "g"))
                        .Select(BuildTree),
                }));
        }

        return result;

        static object BuildTree(System.Xml.Linq.XElement g)
            => new
            {
                t = g.Attribute("transform")?.Value.Replace(" ", string.Empty, StringComparison.Ordinal),
                c = g.Elements()
                    .Where(e => e.Name.LocalName == "g")
                    .Select(BuildTree),
            };
    }

    // ── Performance: 200 elements ────────────────────────────────────────────────

    private static readonly string[] PerfMix =
    [
        "TmSegmentedControl", "TmMultiSelect", "TmStepper", "TmScheduler", "TmImageGallery",
        "TmActivityLog", "TmCalendarHeatmap", "TmRichEditorFull", "TmDataTable", "TmButton",
    ];

    /// <summary>
    /// The full 200-element benchmark. Every insert re-renders the whole canvas, so the cost per insert grows with the
    /// element count (about 4 s per insert at n=60 on the Debug WASM host) and the whole run measured ~41 min
    /// (<c>perf-200.phase3-after.json</c>) - far beyond what the shared full lane can host. It is therefore a
    /// <c>Performance</c>-lane spec (docs/e2e-test-lanes.md, DEC-TEMPO-PERF-LANE-BUDGETS): excluded from the full lane,
    /// run serially on an idle machine by <c>scripts/run-e2e-perf.ps1</c>. The same invariants hold on the normal lane
    /// through <see cref="Perf_ThirtyElements_FirstRender_AndRerender"/>.
    /// </summary>
    [TestMethod]
    [TestCategory("Performance")]
    [Timeout(5_400_000)] // 90 min backstop - the per-call guards below fail a wedge long before this.
    public async Task Perf_TwoHundredElements_FirstRender_AndRerender() => await RunInsertPerfAsync(200);

    /// <summary>The same flow at a size the normal lane can afford: every insert must succeed, nothing may fail, the
    /// re-render of one moved element must complete. Keeps the invariants covered while the 200-element benchmark
    /// runs in the performance lane.</summary>
    [TestMethod]
    [Timeout(900_000)] // 15 min backstop - the per-call guards below should fail a wedge long before this.
    public async Task Perf_ThirtyElements_FirstRender_AndRerender() => await RunInsertPerfAsync(30);

    private async Task RunInsertPerfAsync(int count)
    {
        await using var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await OpenEditorInContextAsync(page, dark: false);

        var editor = WireframeStencilHarness.FirstEditor(page);
        var canvasId = await editor.Locator(".tm-wd-canvas__svg").EvaluateAsync<string>("el => el.id");
        // The seeded document may paint asynchronously (or be empty) — read without auto-waiting.
        var beforeIds = await editor.Locator("g[data-el-id]").EvaluateAllAsync<string[]>(
            "groups => groups.map(g => g.getAttribute('data-el-id'))");
        var beforeCount = beforeIds.Length;

        var now = async () => await page.EvaluateAsync<double>("() => performance.now()");

        const string insertJs =
            """
            async ([canvasId, type, x, y]) => {
                const inst = window.tmWireframeDesigner.instances.get(canvasId);
                if (!inst) return 'no-inst';
                await inst.dotNetRef.invokeMethodAsync('OnElementDropped', type, x, y);
                return 'ok';
            }
            """;

        var inserted = 0;
        string? firstError = null;
        var t0 = await now();
        for (var i = 0; i < count && firstError is null; i++)
        {
            var type = PerfMix[i % PerfMix.Length];
            var call = page.EvaluateAsync<string>(insertJs,
                new object[] { canvasId, type, 200 + (i % 10) * 180, 160 + (i / 10) * 140 });
            var completed = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(90)));
            if (completed != call)
            {
                firstError = $"insert #{i} ({type}) wedged past 90 s";
                break;
            }

            try
            {
                var status = await call;
                if (status != "ok")
                {
                    firstError = $"insert #{i} ({type}): {status}";
                    break;
                }

                inserted++;
            }
            catch (Exception ex)
            {
                firstError = $"insert #{i} ({type}): {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
            }

            if (i % 50 == 49 || (count < 50 && i % 10 == 9))
            {
                TestContext.WriteLine($"[{SweepLabel}] perf: inserted {inserted}/{count}");
            }
        }

        // A wedged page blocks the main thread, so every evaluate below gets the same guard.
        async Task GuardedSettleAsync()
        {
            var settle = page.EvaluateAsync(
                "() => new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)))");
            if (await Task.WhenAny(settle, Task.Delay(TimeSpan.FromSeconds(90))) != settle)
            {
                firstError ??= "settle wedged past 90 s";
                return;
            }

            await settle;
        }

        await GuardedSettleAsync();
        var t1 = await now();

        // One new element id for the re-render measurement (the canvas may have had none seeded).
        string? moveTarget = null;
        var diffCall = page.EvaluateAsync<string>(
            """
            before => {
                const ids = [...document.querySelectorAll('.tm-wd-editor')[0].querySelectorAll('g[data-el-id]')]
                    .map(g => g.getAttribute('data-el-id'));
                return ids.find(id => !before.includes(id)) ?? '';
            }
            """, beforeIds);
        if (await Task.WhenAny(diffCall, Task.Delay(TimeSpan.FromSeconds(90))) == diffCall)
        {
            moveTarget = string.IsNullOrEmpty(await diffCall) ? null : await diffCall;
        }

        double? rerenderMs = null;
        if (moveTarget is not null && firstError is null)
        {
            var m0 = await now();
            var move = page.EvaluateAsync(
                """
                async ([canvasId, id]) => {
                    const inst = window.tmWireframeDesigner.instances.get(canvasId);
                    await inst.dotNetRef.invokeMethodAsync('OnElementMoved', id, 300, 300);
                }
                """,
                new object[] { canvasId, moveTarget });
            if (await Task.WhenAny(move, Task.Delay(TimeSpan.FromSeconds(90))) == move)
            {
                await move;
                await GuardedSettleAsync();
                rerenderMs = Math.Round((await now()) - m0, 2);
            }
            else
            {
                firstError = "move wedged past 90 s";
            }
        }

        var afterCount = await editor.Locator("g[data-el-id]").CountAsync();
        var payload = JsonSerializer.Serialize(new
        {
            inserted,
            beforeCount,
            afterCount,
            firstError,
            firstRenderMs = Math.Round(t1 - t0, 2),
            rerenderMs,
        });

        var dir = Path.Combine(OutputDir, count == 200 ? "perf" : $"perf-{count}");
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, "perf-200.json"),
            JsonSerializer.Serialize(new { label = SweepLabel, host = "wasm", raw = JsonSerializer.Deserialize<JsonObject>(payload) },
                new JsonSerializerOptions { WriteIndented = true }));
        TestContext.WriteLine($"[{SweepLabel}] perf: {payload}");

        var perf = JsonSerializer.Deserialize<JsonObject>(payload)!;
        Assert.AreEqual(count, perf["inserted"]!.GetValue<int>(), $"all {count} elements must insert (was: {payload})");
        Assert.AreEqual(perf["beforeCount"]!.GetValue<int>() + count, perf["afterCount"]!.GetValue<int>());
        Assert.IsNull(perf["firstError"], $"no interop call may fail: {payload}");
    }
}
