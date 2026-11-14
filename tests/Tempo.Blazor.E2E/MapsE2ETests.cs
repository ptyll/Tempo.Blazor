using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TMPO-001 E2E (WASM @ 7106): TmMap on the /maps demo page — Leaflet initialises from bundled
/// assets (no CDN), OSM attribution is visible, marker batches render on a Canvas (never DOM
/// markers), viewport/marker/map-click events reach Blazor, and the map survives marker clearing
/// and re-adding. Includes an axe-core accessibility scan.
/// Screenshots land in <c>__screenshots__/maps/</c> for UX review.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class MapsE2ETests : WasmTestBase
{
    private const string AxeCdn = "https://cdnjs.cloudflare.com/ajax/libs/axe-core/4.10.2/axe.min.js";

    private async Task<IPage> OpenMapsPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync($"{BaseUrl}/maps", new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-map .leaflet-container", new PageWaitForSelectorOptions { Timeout = 30000 });
        // Give tiles + canvas a moment to paint.
        await page.WaitForTimeoutAsync(1500);
        return page;
    }

    // ── E2E-MAP-1: mapa se inicializuje z bundlovaných assetů, attribution viditelná ──

    [TestMethod]
    public async Task MapsPage_RendersLeafletMap_WithOsmAttribution()
    {
        var page = await OpenMapsPageAsync();

        // Leaflet booted from the bundled package assets.
        var leafletScript = page.Locator("script[src*='_content/Tempo.Blazor.Maps/js/leaflet/leaflet.js']");
        Assert.AreEqual(1, await leafletScript.CountAsync(), "Bundled leaflet.js must be injected (no CDN).");

        // OSM attribution is a licensing requirement — must be visible (scoped: the page hosts three maps).
        var attribution = page.Locator("[data-testid='map'] .leaflet-control-attribution");
        await Assertions.Expect(attribution).ToBeVisibleAsync();
        StringAssert.Contains(await attribution.InnerTextAsync(), "OpenStreetMap");

        // Loading overlay is gone once the map is initialised.
        Assert.AreEqual(0, await page.Locator(".tm-map__loading").CountAsync());

        await SaveScreenshotAsync(page, "map-initial");
    }

    // ── E2E-MAP-2: markery jsou vykreslené Canvas rendererem, ne DOM markery ──

    [TestMethod]
    public async Task Markers_AreRenderedOnCanvas_NotDomMarkers()
    {
        var page = await OpenMapsPageAsync();

        // Scoped to the basic map — the clustering maps legitimately use DOM icons for cluster bubbles.
        Assert.IsTrue(await page.Locator("[data-testid='map'] canvas").CountAsync() > 0, "Canvas renderer must be present.");
        Assert.AreEqual(0, await page.Locator("[data-testid='map'] .leaflet-marker-icon").CountAsync(),
            "Batch markers must never be DOM markers.");
    }

    // ── E2E-MAP-3: klik na mapu vyvolá OnMapClick event do Blazoru ──

    [TestMethod]
    public async Task MapClick_RaisesOnMapClickEvent()
    {
        var page = await OpenMapsPageAsync();

        // Click an empty area (top-left quadrant, away from markers).
        var box = await page.Locator("[data-testid='map-container']").BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + box.Width * 0.15), (float)(box.Y + box.Height * 0.15));

        var log = page.Locator("[data-testid='maps-demo-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Map clicked", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await SaveScreenshotAsync(page, "map-click-event");
    }

    // ── E2E-MAP-4: SetViewAsync + zoomend → OnZoomChanged event ──

    [TestMethod]
    public async Task SetView_RaisesOnZoomChangedEvent()
    {
        var page = await OpenMapsPageAsync();

        await page.Locator("[data-testid='maps-demo-view-prague']").ClickAsync();

        var log = page.Locator("[data-testid='maps-demo-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Zoom changed", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        StringAssert.Contains(await log.InnerTextAsync(), "12");
        await page.WaitForTimeoutAsync(1500); // let tiles paint for the screenshot
        await SaveScreenshotAsync(page, "map-zoom-prague");
    }

    // ── E2E-MAP-5: klik na Canvas marker vyvolá OnMarkerClick s Id markeru ──

    [TestMethod]
    public async Task MarkerClick_RaisesOnMarkerClickWithMarkerId()
    {
        var page = await OpenMapsPageAsync();

        // Center the view on the Prague marker, then click the exact map center.
        await page.Locator("[data-testid='maps-demo-view-prague']").ClickAsync();
        await page.WaitForTimeoutAsync(1000);

        var box = await page.Locator("[data-testid='map-container']").BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));

        var log = page.Locator("[data-testid='maps-demo-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Marker clicked: praha", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await SaveScreenshotAsync(page, "map-marker-click");
    }

    // ── E2E-MAP-6: clear + restore markerů bez chyb (0 markerů edge case) ──

    [TestMethod]
    public async Task ClearAndRestoreMarkers_KeepsMapAlive()
    {
        var page = await OpenMapsPageAsync();

        await page.Locator("[data-testid='maps-demo-clear']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await SaveScreenshotAsync(page, "map-markers-cleared");

        await page.Locator("[data-testid='maps-demo-restore']").ClickAsync();
        await page.WaitForTimeoutAsync(500);
        await SaveScreenshotAsync(page, "map-markers-restored");

        // Map is still interactive after both operations.
        var attribution = page.Locator("[data-testid='map'] .leaflet-control-attribution");
        await Assertions.Expect(attribution).ToBeVisibleAsync();

        // A marker click still works after restore (markers were re-added).
        await page.Locator("[data-testid='maps-demo-view-prague']").ClickAsync();
        await page.WaitForTimeoutAsync(1000);
        var box = await page.Locator("[data-testid='map-container']").BoundingBoxAsync();
        Assert.IsNotNull(box);
        await page.Mouse.ClickAsync((float)(box.X + box.Width / 2), (float)(box.Y + box.Height / 2));
        var log = page.Locator("[data-testid='maps-demo-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Marker clicked: praha", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }

    // ── E2E-MAP-7: navigace pryč a zpět (dispose + re-init stejné stránky) ──

    [TestMethod]
    public async Task NavigateAwayAndBack_ReinitialisesMapWithoutErrors()
    {
        var page = await OpenMapsPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error")
            {
                consoleErrors.Add(msg.Text);
            }
        };

        await page.GotoAsync($"{BaseUrl}/buttons", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        await WaitForAppReadyAsync(page);
        await page.GotoAsync($"{BaseUrl}/maps", new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        await WaitForAppReadyAsync(page);
        await page.WaitForSelectorAsync(".tm-map .leaflet-container", new PageWaitForSelectorOptions { Timeout = 30000 });
        await page.WaitForTimeoutAsync(1000);

        var mapErrors = consoleErrors.Where(e => e.Contains("leaflet", StringComparison.OrdinalIgnoreCase)
            || e.Contains("tm-map", StringComparison.OrdinalIgnoreCase)
            || e.Contains("Tempo.Blazor.Maps", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.AreEqual(0, mapErrors.Count, $"Map re-init must not log errors: {string.Join(" | ", mapErrors)}");
        await SaveScreenshotAsync(page, "map-reinit-after-navigation");
    }

    // ── E2E-MAP-8: dark mode — mapa respektuje --tm-* tokeny ──

    [TestMethod]
    public async Task DarkMode_MapChromeFollowsTokens()
    {
        var page = await OpenMapsPageAsync();

        await page.EvaluateAsync(
            """
            () => {
                document.documentElement.setAttribute('data-theme', 'dark');
                document.documentElement.classList.add('dark', 'tm-dark');
                document.body.classList.add('dark');
                document.querySelectorAll('[data-theme]').forEach(el => {
                    el.setAttribute('data-theme', 'dark');
                    el.classList.add('dark', 'tm-dark');
                });
            }
            """);
        await page.WaitForTimeoutAsync(500);
        await SaveScreenshotAsync(page, "map-dark-mode");

        // Attribution must stay visible (OSM licensing) in dark mode too.
        var attribution = page.Locator("[data-testid='map'] .leaflet-control-attribution");
        await Assertions.Expect(attribution).ToBeVisibleAsync();
    }

    // ── E2E-MAP-9: axe-core a11y scan bez critical/serious nálezů ──

    [TestMethod]
    public async Task Accessibility_MapRegion_NoCriticalOrSeriousViolations()
    {
        var page = await OpenMapsPageAsync();
        await page.AddScriptTagAsync(new PageAddScriptTagOptions { Url = AxeCdn });

        var violations = await page.EvaluateAsync<string[]>(
            """
            async () => {
                const host = document.querySelector('.tm-map') || document.body;
                // axe samples the RESOLVED colours: a finite CSS animation/transition still running (the demo's fade-in, a colour
                // transition, the segmented indicator) paints a blended, low-contrast frame. Wait for them (looping ones,
                // like the skeleton shimmer, never finish and are not waited for).
                await Promise.all(document.getAnimations()
                    .filter(a => a.effect && a.effect.getComputedTiming().iterations !== Infinity)
                    .map(a => a.finished.catch(() => { })));
                await new Promise(r => requestAnimationFrame(() => requestAnimationFrame(r)));
                const result = await axe.run(host, {
                    runOnly: { type: 'tag', values: ['wcag2a', 'wcag2aa'] },
                    resultTypes: ['violations']
                });
                return result.violations
                    .filter(v => v.impact === 'critical' || v.impact === 'serious')
                    .map(v => `${v.impact}: ${v.id} - ${v.help} (${v.nodes.map(n => n.target.join(' ')).join('; ')})`);
            }
            """);

        Assert.AreEqual(0, violations.Length,
            $"TmMap must have no critical/serious a11y violations: {string.Join(" | ", violations)}");
    }

    // ── E2E-MAP-10: klientský clustering — markercluster ikony s počty ──────

    [TestMethod]
    public async Task ClientClustering_RendersMarkerClusterIcons()
    {
        var page = await OpenMapsPageAsync();
        var clusterMap = page.Locator("[data-testid='cluster-map']");
        await ScrollToCenterAsync(page, "cluster-map");
        await page.WaitForTimeoutAsync(1500);

        // leaflet.markercluster renders .marker-cluster DOM icons with counts.
        var clusterIcons = clusterMap.Locator(".marker-cluster");
        Assert.IsTrue(await clusterIcons.CountAsync() > 0, "markercluster icons must be rendered.");
        var firstText = await clusterIcons.First.InnerTextAsync();
        Assert.IsTrue(int.TryParse(firstText.Trim(), out var count) && count > 1,
            $"Cluster icon must show a count > 1, got '{firstText}'.");

        await SaveScreenshotAsync(page, "map-client-clustering");
    }

    // ── E2E-MAP-11: klik na klientský cluster zoomuje k jeho boundům ────────

    [TestMethod]
    public async Task ClientClusterClick_ZoomsToBounds_AndRaisesEvent()
    {
        var page = await OpenMapsPageAsync();
        var clusterMap = page.Locator("[data-testid='cluster-map']");
        await ScrollToCenterAsync(page, "cluster-map");
        await page.WaitForTimeoutAsync(1500);

        // Leaflet also lays out icons beyond the visible map area (clipped by overflow:hidden),
        // so pick an icon whose center is truly inside the map box, then click via raw mouse
        // coordinates — Playwright's click auto-scroll can park icons under the sticky topbar.
        await clusterMap.ScrollIntoViewIfNeededAsync();
        await page.WaitForTimeoutAsync(500);
        var mapBox = await clusterMap.BoundingBoxAsync();
        Assert.IsNotNull(mapBox);
        var icons = clusterMap.Locator(".marker-cluster");
        var iconCount = await icons.CountAsync();
        Assert.IsTrue(iconCount > 0, "markercluster icons must be present.");
        var clicked = false;
        for (var i = 0; i < iconCount && !clicked; i++)
        {
            var box = await icons.Nth(i).BoundingBoxAsync();
            if (box is null)
            {
                continue;
            }

            var cx = box.X + box.Width / 2;
            var cy = box.Y + box.Height / 2;
            var insideMap = cx > mapBox.X + 20 && cx < mapBox.X + mapBox.Width - 20
                && cy > mapBox.Y + 20 && cy < mapBox.Y + mapBox.Height - 20;
            if (insideMap && cy > 120 && cy < 990)
            {
                await page.Mouse.ClickAsync((float)cx, (float)cy);
                clicked = true;
            }
        }

        Assert.IsTrue(clicked, "No cluster icon was inside the visible map area.");
        await page.WaitForTimeoutAsync(1000);

        var log = page.Locator("[data-testid='maps-cluster-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Cluster clicked", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await SaveScreenshotAsync(page, "map-client-cluster-drilldown");
    }

    // ── E2E-MAP-12: provider servíruje cluster bubliny při malém zoomu ──────

    [TestMethod]
    public async Task ProviderMap_ShowsServerClusterBubbles_AtLowZoom()
    {
        var page = await OpenMapsPageAsync();
        var providerMap = page.Locator("[data-testid='provider-map']");
        await ScrollToCenterAsync(page, "provider-map");

        var bubbles = providerMap.Locator(".tm-map__cluster");
        await Assertions.Expect(bubbles.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        Assert.IsTrue(await bubbles.CountAsync() > 3, "Grid aggregation must produce multiple cluster bubbles.");

        // Bubbles carry a localized aria-label with the count.
        var aria = await bubbles.First.GetAttributeAsync("aria-label");
        Assert.IsFalse(string.IsNullOrWhiteSpace(aria), "Cluster bubble must have an aria-label.");

        // Count = 1 cell (lone point near Aš) renders as a plain marker — no bubble shows '1'.
        var bubbleTexts = await bubbles.AllInnerTextsAsync();
        Assert.IsFalse(bubbleTexts.Any(t => t.Trim() == "1"), "A Count=1 cluster must render as a plain marker, not a bubble.");

        await SaveScreenshotAsync(page, "map-server-clusters");
    }

    // ── E2E-MAP-13: hybridní přechod přes zoom práh (clusters ↔ markery) ────

    [TestMethod]
    public async Task ProviderMap_SwitchesToIndividualMarkers_AboveThreshold()
    {
        var page = await OpenMapsPageAsync();
        var providerMap = page.Locator("[data-testid='provider-map']");
        await ScrollToCenterAsync(page, "provider-map");
        await Assertions.Expect(providerMap.Locator(".tm-map__cluster").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        // Cross the zoom threshold (12) — debounced provider request swaps clusters for markers.
        await page.Locator("[data-testid='maps-provider-zoom-in']").ClickAsync();
        await Assertions.Expect(providerMap.Locator(".tm-map__cluster"))
            .ToHaveCountAsync(0, new LocatorAssertionsToHaveCountOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(1500); // tiles + canvas paint
        await SaveScreenshotAsync(page, "map-server-individual-markers");

        // And back below the threshold — bubbles return (atomic swap, no leftovers).
        await page.Locator("[data-testid='maps-provider-zoom-out']").ClickAsync();
        await Assertions.Expect(providerMap.Locator(".tm-map__cluster").First)
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await SaveScreenshotAsync(page, "map-server-clusters-back");
    }

    // ── E2E-MAP-14: klik na server-side cluster hlásí klíč a drill-down ─────

    [TestMethod]
    public async Task ServerClusterClick_ReportsKeyAndDrillsDown()
    {
        var page = await OpenMapsPageAsync();
        var providerMap = page.Locator("[data-testid='provider-map']");
        await ScrollToCenterAsync(page, "provider-map");
        var bubbles = providerMap.Locator(".tm-map__cluster");
        await Assertions.Expect(bubbles.First).ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });

        await bubbles.First.ClickAsync();

        var log = page.Locator("[data-testid='maps-provider-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Cluster clicked: key=", new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
        await page.WaitForTimeoutAsync(1500);
        await SaveScreenshotAsync(page, "map-server-cluster-drilldown");
    }

    // ── E2E-MAP-15: 10k bodů přes SetMarkersAsync — Canvas, jeden atomický swap ──

    [TestMethod]
    public async Task BatchMap_LoadsTenThousandPoints_OnCanvas()
    {
        var page = await OpenMapsPageAsync();
        await ScrollToCenterAsync(page, "maps-batch-load");
        await page.Locator("[data-testid='maps-batch-load']").ClickAsync();

        var log = page.Locator("[data-testid='maps-batch-log-entry']");
        await Assertions.Expect(log).ToContainTextAsync("Loaded 10 000 points",
            new LocatorAssertionsToContainTextOptions { Timeout = 30000 });

        // Rendered by the Canvas renderer — no DOM markers appear.
        var batchMap = page.Locator("[data-testid='batch-map']");
        Assert.IsTrue(await batchMap.Locator("canvas").CountAsync() > 0);
        Assert.AreEqual(0, await batchMap.Locator(".leaflet-marker-icon").CountAsync(),
            "10k batch must never render DOM markers.");

        await page.WaitForTimeoutAsync(1000);
        await SaveScreenshotAsync(page, "map-batch-10k");

        // Clearing keeps the map alive.
        await ScrollToCenterAsync(page, "maps-batch-clear");
        await page.Locator("[data-testid='maps-batch-clear']").ClickAsync();
        await Assertions.Expect(log).ToContainTextAsync("Cleared",
            new LocatorAssertionsToContainTextOptions { Timeout = 10000 });
    }

    // ── E2E-MAP-16: mapa ve skrytém tabu — po přepnutí správná velikost ─────

    [TestMethod]
    public async Task MapInHiddenTab_InitialisesCorrectly_AfterTabSwitch()
    {
        var page = await OpenMapsPageAsync();
        await ScrollToCenterAsync(page, "maps-tab-info-content");

        // Switch to the tab hosting the map.
        await page.Locator("button:has-text('Map'), [role='tab']:has-text('Map')").First.ClickAsync();
        var tabMap = page.Locator("[data-testid='tab-map']");
        await Assertions.Expect(tabMap.Locator(".leaflet-container"))
            .ToBeVisibleAsync(new LocatorAssertionsToBeVisibleOptions { Timeout = 15000 });
        await page.WaitForTimeoutAsync(1500); // invalidateSize + tile paint

        // After InvalidateSizeAsync the container must have a real size and painted tiles.
        var box = await tabMap.Locator(".leaflet-container").BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Width > 200 && box.Height > 200, $"map must have a real size, got {box.Width}x{box.Height}");
        Assert.IsTrue(await tabMap.Locator(".leaflet-tile").CountAsync() > 0, "tiles must be loaded after invalidateSize");

        await SaveScreenshotAsync(page, "map-in-tab");
    }

    // The demo layout has a sticky, pointer-intercepting topbar; scrolling targets to the
    // viewport center keeps them clickable.
    private static async Task ScrollToCenterAsync(IPage page, string testId)
    {
        // behavior:'instant' sidesteps the demo's smooth scrolling, which animates over time
        // and leaves bounding boxes stale while the page settles.
        await page.EvaluateAsync($"() => document.querySelector(\"[data-testid='{testId}']\")?.scrollIntoView({{ block: 'center', behavior: 'instant' }})");
        await page.WaitForTimeoutAsync(400);
    }

    private static async Task SaveScreenshotAsync(IPage page, string fileName)
    {
        var dir = Path.Combine(FindRepoRoot().FullName, "tests", "Tempo.Blazor.E2E", "__screenshots__", "maps");
        Directory.CreateDirectory(dir);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = Path.Combine(dir, $"{fileName}.png"), FullPage = true });
    }

    private static DirectoryInfo FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
            {
                return directory;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
