using System.Globalization;
using Microsoft.Playwright;
using static Tempo.Blazor.E2E.DocumentEditorRedesignE2EHelpers;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Redesign plan 146c7e1b, phase 2 (tokens, visual layer, shared panel building blocks): behavioural assertions.
/// Writes NO files; the screenshots are <see cref="DocumentEditorRedesignPhase2CaptureE2ETests"/> (a gated generator).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase2E2ETests : WasmTestBase
{
    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("The root and the workspace are flat and ride --tm-bg-workspace / --tm-radius-card in all four theme combinations")]
    public async Task VisualLayer_IsFlat_AndRidesTheSemanticTokens_InEveryThemeCombination()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);

        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            var paint = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const root = document.querySelector('.tm-document-editor');
                    const workspace = document.querySelector('.tm-document-editor__workspace');
                    const probe = document.createElement('div');
                    probe.style.cssText = 'background: var(--tm-bg-workspace); border-radius: var(--tm-radius-card); position: absolute; width: 1px; height: 1px;';
                    root.appendChild(probe);
                    const result = [
                        getComputedStyle(root).backgroundImage,
                        getComputedStyle(workspace).backgroundImage,
                        getComputedStyle(workspace).backgroundColor,
                        getComputedStyle(probe).backgroundColor,
                        getComputedStyle(root).borderTopLeftRadius,
                        getComputedStyle(probe).borderTopLeftRadius,
                    ];
                    probe.remove();
                    return result;
                }
                """);

            Assert.AreEqual("none", paint[0], $"{name}: the editor root paints no gradient");
            Assert.AreEqual("none", paint[1], $"{name}: the workspace paints no gradient");
            Assert.AreEqual(paint[3], paint[2], $"{name}: the workspace background is --tm-bg-workspace");
            Assert.AreEqual(paint[5], paint[4], $"{name}: the editor root radius is --tm-radius-card");

            if (indigo)
            {
                Assert.AreEqual("12px", paint[4], $"{name}: the indigo card radius is 12px");
                Assert.AreEqual(dark ? "rgb(11, 17, 32)" : "rgb(247, 248, 252)", paint[2], $"{name}: the indigo workspace token");
            }
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The side panel's active tab is a primary underline in light and dark")]
    public async Task SidePanelActiveTab_IsAPrimaryUnderline()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, false);
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });

        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            var values = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const tab = document.querySelector('.tm-document-side-panel__tab--active');
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--tm-color-primary)';
                    document.body.appendChild(probe);
                    const primary = getComputedStyle(probe).color;
                    probe.remove();
                    const cs = getComputedStyle(tab);
                    return [cs.boxShadow, primary, cs.backgroundColor];
                }
                """);
            StringAssert.Contains(values[0], values[1], $"{name}: the active tab carries a primary underline (box-shadow {values[0]})");
            Assert.AreEqual("rgba(0, 0, 0, 0)", values[2], $"{name}: the active tab is not a raised pill any more");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The panel's primary action is filled with --tm-color-primary, the secondary one is not (Accept all vs Reject all)")]
    public async Task RevisionPanel_PrimaryAction_IsFilledWithThePrimaryToken()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, false);
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-accept-all")).ToBeVisibleAsync(new() { Timeout = 15_000 });

        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            var values = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const probe = document.createElement('span');
                    probe.style.color = 'var(--tm-color-primary)';
                    document.body.appendChild(probe);
                    const primary = getComputedStyle(probe).color;
                    probe.remove();
                    const accept = document.querySelector('[data-testid="document-revision-accept-all"]');
                    const reject = document.querySelector('[data-testid="document-revision-reject-all"]');
                    return [primary, getComputedStyle(accept).backgroundColor, getComputedStyle(reject).backgroundColor,
                        accept.className, getComputedStyle(accept).borderTopLeftRadius];
                }
                """);
            Assert.AreEqual(values[0], values[1], $"{name}: Accept all is the filled primary action");
            Assert.AreNotEqual(values[0], values[2], $"{name}: Reject all is a secondary action");
            StringAssert.Contains(values[3], "tm-btn-primary");
            StringAssert.Contains(values[3], "tm-document-revision-panel__batch-action--accept", "the BEM alias stays on the element");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The search highlight classes paint the editor's highlight tokens (amber, never the blue selection tint) in light and dark")]
    public async Task SearchHighlight_Classes_PaintTheHighlightTokens()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);

        foreach (var dark in new[] { false, true })
        {
            await ApplyThemeAsync(page, dark, false);
            // The overlay element is injected the way search-overlay.mjs creates it (class + geometry only), inside
            // the canvas host the scoped ::deep rules target.
            var values = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    const make = cls => {
                        const el = document.createElement('div');
                        el.className = cls;
                        el.style.cssText = 'position:absolute;left:0;top:0;width:10px;height:10px;';
                        host.appendChild(el);
                        return el;
                    };
                    const plain = make('tm-document-canvas-search-highlight');
                    const active = make('tm-document-canvas-search-highlight tm-document-canvas-search-highlight--active');
                    const probe = document.createElement('span');
                    host.appendChild(probe);
                    probe.style.background = 'var(--tm-document-highlight-match)';
                    const matchToken = getComputedStyle(probe).backgroundColor;
                    probe.style.background = 'var(--tm-document-highlight-match-active)';
                    const activeToken = getComputedStyle(probe).backgroundColor;
                    probe.remove();
                    const result = [getComputedStyle(plain).backgroundColor, matchToken,
                        getComputedStyle(active).backgroundColor, activeToken, getComputedStyle(active).outlineStyle, getComputedStyle(active).outline + "/" + getComputedStyle(active).outlineColor];
                    plain.remove(); active.remove();
                    return result;
                }
                """);
            var name = dark ? "dark" : "light";
            Assert.AreNotEqual("rgba(0, 0, 0, 0)", values[0], $"{name}: the match rule applies (a dropped rule would leave it transparent)");
            Assert.AreEqual(values[1], values[0], $"{name}: a match paints --tm-document-highlight-match");
            Assert.AreEqual(values[3], values[2], $"{name}: the active match paints --tm-document-highlight-match-active");
            Assert.AreEqual("solid", values[4], $"{name}: the active match is outlined ({values[5]})");
            var channels = System.Text.RegularExpressions.Regex.Matches(values[2], @"[\d.]+")
                .Select(match => double.Parse(match.Value, CultureInfo.InvariantCulture)).ToArray();
            if (values[2].StartsWith("color(", StringComparison.Ordinal))
            {
                // The computed value of a color-mix() is a color(srgb r g b / a) with channels in 0..1.
                channels = channels.Select((value, index) => index < 3 ? value * 255 : value).ToArray();
            }

            Assert.IsTrue(channels[0] > channels[2] + 40, $"{name}: the active match is amber/warm, not the blue selection tint ({values[2]})");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("A real search paints exactly one active highlight over the match, aligned with the text, above the selection - also zoomed")]
    public async Task SearchHighlight_RealSearch_PaintsOneAlignedActiveHighlight_AtDefaultAndZoomedPages()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);

        foreach (var zoomSteps in new[] { 0, 4 })
        {
            for (var i = 0; i < zoomSteps; i++)
            {
                await page.Locator("[data-testid='document-canvas-engine-root']").EvaluateAsync(
                    "el => el.dispatchEvent(new WheelEvent('wheel', { deltaY: -120, ctrlKey: true, bubbles: true, cancelable: true }))");
                await page.WaitForTimeoutAsync(150);
            }

            await page.GetByTestId("document-canvas-engine-host").ClickAsync();
            await page.Keyboard.PressAsync("Control+F");
            await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
            await page.GetByTestId("document-find-input").FillAsync(string.Empty);
            await page.GetByTestId("document-find-input").FillAsync("history");
            await Assertions.Expect(page.GetByTestId("document-canvas-search-active")).ToHaveCountAsync(1, new() { Timeout = 10_000 });

            var probe = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                    const active = document.querySelector('[data-testid="document-canvas-search-active"]').getBoundingClientRect();
                    const word = document.querySelector('[data-canvas-text-rect][data-block-id="canvas-history-text"][data-canvas-start-offset="0"]').getBoundingClientRect();
                    return [Number(root.getAttribute('data-canvas-search-highlight-count')), active.left, active.width, word.left, word.width,
                        Number(root.getAttribute('data-canvas-zoom-percent'))];
                }
                """);
            Assert.IsTrue(probe[0] > 0, "the engine reports painted highlights");
            Assert.AreEqual(probe[3], probe[1], 3.0, $"zoom {probe[5]}%: the highlight starts where the word starts");
            Assert.AreEqual(probe[4], probe[2], Math.Max(3.0, probe[4] * 0.1), $"zoom {probe[5]}%: the highlight is as wide as the word");

            foreach (var dark in new[] { false, true })
            {
                await ApplyThemeAsync(page, dark, false);
                var paint = await page.EvaluateAsync<string[]>(
                    """
                    () => {
                        const active = document.querySelector('[data-testid="document-canvas-search-active"]');
                        const layer = document.querySelector('[data-testid="document-canvas-search-layer"]');
                        return [getComputedStyle(active).backgroundColor, getComputedStyle(layer).zIndex];
                    }
                    """);
                Assert.AreNotEqual("rgba(0, 0, 0, 0)", paint[0], $"zoom {probe[5]}% {(dark ? "dark" : "light")}: the active highlight is painted");
                Assert.AreEqual("13", paint[1], "the search layer sits above the selection layer");
            }

            // Follow-up (phase 3): while a search is up the active match IS the selection, so the blue selection rectangle is
            // hidden and the amber match is not muddied by a blue wash beneath it. The engine host carries the flag, a rectangle
            // painted by the engine gets the rule (a probe stands in for it: a collapsed caret has none), and closing the
            // find panel releases both.
            var flagged = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const layer = document.querySelector('[data-testid="document-canvas-search-layer"]').parentElement;
                    const probe = document.createElement('div');
                    probe.className = 'tm-document-canvas-selection-rect';
                    probe.style.cssText = 'position:absolute;left:0;top:0;width:10px;height:10px;';
                    layer.appendChild(probe);
                    const result = [host.getAttribute('data-canvas-search-active') ?? 'null', getComputedStyle(probe).visibility];
                    probe.remove();
                    return result;
                }
                """);
            Assert.AreEqual("true", flagged[0], $"zoom {probe[5]}%: the engine host is flagged while matches exist");
            Assert.AreEqual("hidden", flagged[1], $"zoom {probe[5]}%: the selection rectangle is hidden while a search is up");

            await ApplyThemeAsync(page, false, false);
            await page.GetByTestId("document-find-close").ClickAsync();
            await WaitForPredicateAsync(page, """
                () => !document.querySelector('[data-testid="document-canvas-engine-host"]').hasAttribute('data-canvas-search-active')
                """, 10_000);
            var released = await page.EvaluateAsync<string>(
                """
                () => {
                    const host = document.querySelector('[data-testid="document-canvas-engine-host"]');
                    const probe = document.createElement('div');
                    probe.className = 'tm-document-canvas-selection-rect';
                    probe.style.cssText = 'position:absolute;left:0;top:0;width:10px;height:10px;';
                    host.appendChild(probe);
                    const visibility = getComputedStyle(probe).visibility;
                    probe.remove();
                    return visibility;
                }
                """);
            Assert.AreEqual("visible", released, $"zoom {probe[5]}%: the selection rectangle returns once the search is cleared");
        }

        await page.Context.CloseAsync();
    }

    private const string LegibilityProbe = """
        (selector) => {
            const toRgba = css => {
                const ctx = document.createElement('canvas').getContext('2d', { willReadFrequently: true });
                ctx.clearRect(0, 0, 1, 1);
                ctx.fillStyle = '#000';
                ctx.fillStyle = css;
                ctx.fillRect(0, 0, 1, 1);
                const [r, g, b, a] = ctx.getImageData(0, 0, 1, 1).data;
                return { r, g, b, a: a / 255 };
            };
            const lum = c => {
                const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                return 0.2126 * f(c.r) + 0.7152 * f(c.g) + 0.0722 * f(c.b);
            };
            const button = document.querySelector(selector);
            if (!button) return { missing: true };
            const buttonColor = getComputedStyle(button).color;
            // The first opaque background up the tree (the button's own fill for a filled button).
            let bg = null;
            for (let el = button; el; el = el.parentElement) {
                const c = toRgba(getComputedStyle(el).backgroundColor);
                if (c.a > 0.99) { bg = c; break; }
            }
            bg ??= { r: 255, g: 255, b: 255, a: 1 };
            const fg = toRgba(buttonColor);
            const l1 = lum(fg), l2 = lum(bg);
            const contrast = (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
            const mismatched = Array.from(button.querySelectorAll('*'))
                .filter(el => el.textContent.trim() && el.children.length === 0 && getComputedStyle(el).color !== buttonColor)
                .map(el => el.tagName + ':' + getComputedStyle(el).color);
            return { missing: false, contrast, mismatched, buttonColor };
        }
        """;

    private static async Task AssertLegibleAsync(IPage page, string selector, string what, string theme)
    {
        var probe = await page.EvaluateAsync<System.Text.Json.JsonElement>(LegibilityProbe, selector);
        Assert.IsFalse(probe.GetProperty("missing").GetBoolean(), $"{what}: button not found ({selector})");
        var mismatched = probe.GetProperty("mismatched").EnumerateArray().Select(item => item.GetString()).ToArray();
        Assert.AreEqual(0, mismatched.Length, $"{what} [{theme}]: every text child carries the button ink, got {string.Join(", ", mismatched)}");
        var contrast = probe.GetProperty("contrast").GetDouble();
        Assert.IsTrue(contrast >= 4.5, $"{what} [{theme}]: label contrast {contrast:0.00}:1 must be at least 4.5:1");
    }

    [TestMethod]
    [Description("Primary and secondary panel actions keep their ink (no panel type-selector rule recolours a label) and clear 4.5:1 in all four theme combinations")]
    public async Task PrimaryActions_Labels_AreLegible_InEveryThemeCombination()
    {
        // Version panel (the regression: a bare "span" rule under the panel painted the label muted grey on the fill).
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false, "/document-editor");
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-versions");
        await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await AssertLegibleAsync(page, "[data-testid='document-version-create-open']", "Create version", name);
        }

        await page.Context.CloseAsync();

        // Revision panel: Accept all.
        page = await OpenAsync(RevisionsDocumentId, 1440, 900, false);
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-accept-all")).ToBeVisibleAsync(new() { Timeout = 15_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await AssertLegibleAsync(page, "[data-testid='document-revision-accept-all']", "Accept all", name);
        }

        await page.Context.CloseAsync();

        // Compare dialog and find/replace.
        page = await OpenAsync(HistoryDocumentId, 1440, 900, false);
        await page.GetByTestId("document-ribbon-tab-review").ClickAsync();
        await page.GetByTestId("document-compare-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-compare-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await AssertLegibleAsync(page, "[data-testid='document-compare-run']", "Compare", name);
        }

        await page.Context.CloseAsync();

        page = await OpenAsync(HistoryDocumentId, 1440, 900, false);
        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+H");
        await Assertions.Expect(page.GetByTestId("document-find-replace-one")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await AssertLegibleAsync(page, "[data-testid='document-find-replace-one']", "Replace", name);
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The canvas workspace paints --tm-bg-workspace and the active ribbon tab is a flat primary underline, in all four theme combinations")]
    public async Task CanvasWorkspace_AndRibbonTab_RideTheTokens()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            var v = await page.EvaluateAsync<string[]>(
                """
                () => {
                    const host = document.querySelector('.tm-document-canvas-engine-host');
                    const probe = document.createElement('span');
                    probe.style.cssText = 'background: var(--tm-bg-workspace); color: var(--tm-color-primary);';
                    document.body.appendChild(probe);
                    const cs = getComputedStyle(probe);
                    const workspace = cs.backgroundColor, primary = cs.color;
                    probe.remove();
                    const tab = document.querySelector('.tm-document-editor__ribbon-tab--active');
                    const tabCs = getComputedStyle(tab), after = getComputedStyle(tab, '::after');
                    return [getComputedStyle(host).backgroundColor, workspace, tabCs.boxShadow, tabCs.backgroundColor, after.backgroundColor, primary, tabCs.color];
                }
                """);
            Assert.AreEqual(v[1], v[0], $"{name}: the canvas workspace is --tm-bg-workspace (no primary tint)");
            Assert.AreEqual("none", v[2], $"{name}: the active ribbon tab has no shadow");
            Assert.AreEqual("rgba(0, 0, 0, 0)", v[3], $"{name}: the active ribbon tab has no fill");
            Assert.AreEqual(v[5], v[4], $"{name}: the ::after underline is the primary colour");
            Assert.AreEqual(v[5], v[6], $"{name}: the active tab ink is primary");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Revision markers paint the insert / delete / change tokens (they differ per kind) in light and dark")]
    public async Task RevisionMarkers_PaintThePerKindHighlightTokens()
    {
        var page = await OpenAsync(RevisionsDocumentId, 1440, 900, false);
        await WaitForPredicateAsync(page, "() => document.querySelectorAll('[data-canvas-revision-type]').length >= 2", 20_000);
        foreach (var dark in new[] { false, true })
        {
            await ApplyThemeAsync(page, dark, false);
            var json = await page.EvaluateAsync<string>(
                """
                () => {
                    const out = {};
                    for (const el of document.querySelectorAll('[data-canvas-revision-type]')) {
                        const type = el.getAttribute('data-canvas-revision-type');
                        const cs = getComputedStyle(el);
                        out[type] = [cs.backgroundColor + '|' + cs.backgroundImage, cs.boxShadow, cs.borderTopWidth, cs.appearance];
                    }
                    return JSON.stringify(out);
                }
                """);
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var kinds = document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray());
            var theme = dark ? "dark" : "light";
            Assert.IsTrue(kinds.Count >= 2, $"{theme}: at least two revision kinds are marked ({string.Join(",", kinds.Keys)})");
            foreach (var (kind, paint) in kinds)
            {
                Assert.AreNotEqual("rgba(0, 0, 0, 0)|none", paint[0], $"{theme}: the {kind} marker is painted");
                Assert.AreEqual("0px", paint[2], $"{theme}: the {kind} marker has no UA button border");
            }

            Assert.AreEqual(kinds.Count, kinds.Values.Select(paint => paint[0] + paint[1]).Distinct().Count(), $"{theme}: every revision kind paints differently");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Table and cell properties fit the 360px inspector without a frame and the side panel stays inside the viewport height")]
    public async Task PropertiesPanels_FitTheInspector_AndTheSidePanelIsBounded()
    {
        var page = await OpenAsync("contract-demo", 1440, 900, false, "/document-editor");
        var cell = await DocumentEditorRedesignPhase2CaptureE2ETests.ScrollToCanvasElementAsync(page, "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']");
        await page.Mouse.ClickAsync((float)cell.X, (float)cell.Y);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-cell-id') === 'contract-pricing-table-r1-item'
            """, 10_000);
        await DocumentEditorRedesignPhase2CaptureE2ETests.RunPaletteCommandAsync(page, "tableProperties");
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await DocumentEditorRedesignPhase2CaptureE2ETests.RunPaletteCommandAsync(page, "cellProperties");
        await Assertions.Expect(page.GetByTestId("document-cell-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });

        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            var v = await page.EvaluateAsync<double[]>(
                """
                () => {
                    const panel = document.querySelector('[data-testid="document-side-panel"]');
                    const body = panel.querySelector('.tm-document-side-panel__body');
                    const table = document.querySelector('[data-testid="document-table-properties-panel"]');
                    const cell = document.querySelector('[data-testid="document-cell-properties-panel"]');
                    const framed = el => (parseFloat(getComputedStyle(el).borderTopWidth) > 0 ? 1 : 0) + (getComputedStyle(el).boxShadow !== 'none' ? 1 : 0);
                    return [body.scrollWidth - body.clientWidth, panel.getBoundingClientRect().height, window.innerHeight, framed(table), framed(cell),
                        table.getBoundingClientRect().right - body.getBoundingClientRect().right];
                }
                """);
            Assert.IsTrue(v[0] <= 1, $"{name}: the panel body must not scroll horizontally (by {v[0]}px)");
            Assert.IsTrue(v[1] <= v[2], $"{name}: the side panel ({v[1]}px) fits the viewport ({v[2]}px)");
            Assert.AreEqual(0, v[3], $"{name}: the embedded table panel has no frame and no shadow");
            Assert.AreEqual(0, v[4], $"{name}: the embedded cell panel has no frame and no shadow");
            Assert.IsTrue(v[5] <= 1, $"{name}: the table panel stays inside the inspector body");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The inspector building-blocks demo card renders the header and the form pattern; the close button and the chevron work")]
    public async Task InspectorBuildingBlocksCard_RendersAndBehaves()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor", WaitUntilState.DOMContentLoaded);
        var card = page.GetByTestId("document-inspector-blocks-card");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await card.ScrollIntoViewIfNeededAsync();

        var heading = card.Locator("h2.tm-document-inspector-header__title");
        await Assertions.Expect(heading).ToHaveAttributeAsync("id", "demo-inspector-header-title");
        await Assertions.Expect(card.Locator(".tm-document-inspector-header__subtitle")).ToHaveTextAsync("evidence.png");

        var toggle = page.GetByTestId("demo-inspector-size-toggle").First;
        var open = await toggle.Locator(".tm-document-inspector-form__chevron").EvaluateAsync<string>("el => getComputedStyle(el).transform");
        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        var closed = await toggle.Locator(".tm-document-inspector-form__chevron").EvaluateAsync<string>("el => getComputedStyle(el).transform");
        Assert.AreNotEqual(open, closed, "the chevron rotates between open and closed");
        await toggle.ClickAsync();

        await page.GetByTestId("demo-inspector-close").ClickAsync();
        await Assertions.Expect(page.GetByTestId("demo-inspector-closed")).ToBeVisibleAsync();
        await context.CloseAsync();
    }

    [TestMethod]
    [Description("A debug modal section without data shows the localized 'No data' notice and keeps its test ids")]
    public async Task ClipboardDebugModal_EmptySections_ShowTheNoDataNotice()
    {
        var page = await OpenAsync(HistoryDocumentId, 1440, 900, false);
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-view-clipboard-html");
        var modal = page.GetByTestId("document-clipboard-html-debug-modal");
        await Assertions.Expect(modal).ToBeVisibleAsync(new() { Timeout = 10_000 });

        // A fresh editor has no clipboard capture: at least one section (the normalized blocks) is empty and says so,
        // and an empty section hides its monospace block instead of showing an empty box.
        var notices = modal.Locator(".tm-document-json-debug-modal__empty");
        var noticeCount = await notices.CountAsync();
        Assert.IsTrue(noticeCount >= 1, "an editor without a clipboard capture has an empty debug section");
        for (var i = 0; i < noticeCount; i++)
        {
            await Assertions.Expect(notices.Nth(i)).ToBeVisibleAsync();
            await Assertions.Expect(notices.Nth(i)).ToHaveTextAsync("No data");
            await Assertions.Expect(notices.Nth(i).Locator("xpath=preceding-sibling::pre[1]")).ToBeHiddenAsync();
        }

        foreach (var testId in new[] { "document-clipboard-html-debug-content", "document-clipboard-normalized-debug-content", "document-clipboard-warnings-debug-content" })
        {
            Assert.AreEqual(1, await page.GetByTestId(testId).CountAsync(), $"{testId} stays in the DOM");
        }

        // The JSON modal keeps its Copy button (a delegate is bound) and the shared frame stays centered at 390.
        await page.GetByTestId("document-clipboard-html-debug-close").ClickAsync();
        await ClickRibbonAsync(page, "document-view-json");
        await Assertions.Expect(page.GetByTestId("document-json-debug-copy")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Edge: the redesigned surfaces never overflow horizontally on a phone in dark indigo")]
    public async Task MainSurface_NeverOverflows_OnAPhone_InDarkIndigo()
    {
        var page = await OpenAsync(HistoryDocumentId, 390, 844, true);
        await ApplyThemeAsync(page, dark: true, indigo: true);
        var overflow = await page.EvaluateAsync<double>("() => document.documentElement.scrollWidth - document.documentElement.clientWidth");
        Assert.IsTrue(overflow <= 1, $"document must not overflow horizontally (by {overflow}px)");
        await page.Context.CloseAsync();
    }

    private async Task<IPage> OpenAsync(string documentId, int width, int height, bool touch, string route = "/canvas-engine-host")
    {
        var context = await CreateContextAsync(ContextOptions(width, height, touch));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, OpenUrl(BaseUrl, documentId, route), WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 45_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }
}

/// <summary>
/// Phase-2 "after" screenshots for the UX review: the editor surface and the panels in light/dark x default/indigo
/// at 1440. Gated by <c>TM_WRITE_BASELINES</c> like every generator; the PNGs land in
/// <c>tests/Tempo.Blazor.E2E/__screenshots__/document-editor-redesign/phase-2/</c> (override the folder name with
/// <c>TM_DOCUMENT_EDITOR_BASELINE_DIR</c>).
/// </summary>
[TestClass]
[TestCategory("DocumentEditor")]
[TestCategory("DocumentEditor:CanvasEngine")]
[DoNotParallelize]
public sealed class DocumentEditorRedesignPhase2CaptureE2ETests : BaselineGeneratorTestBase
{
    private const string ContractDocumentId = "contract-demo";

    private static string OutputDirectory
    {
        get
        {
            var target = Environment.GetEnvironmentVariable("TM_DOCUMENT_EDITOR_BASELINE_DIR");
            var name = string.IsNullOrWhiteSpace(target) ? "phase-2" : target.Trim();
            var directory = Path.Combine(
                FindRepositoryRootDirectory(),
                "tests", "Tempo.Blazor.E2E", "__screenshots__", "document-editor-redesign", name);
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    [TestInitialize]
    public Task ResetDocumentEditorDemoAsync()
        => DocumentEditorE2EReset.ResetAsync();

    [TestMethod]
    [Description("Main surface and find panel at 1440 in the four theme combinations")]
    public async Task CaptureMainSurfaceAndFindPanel()
    {
        var page = await OpenAsync(HistoryDocumentId, "/canvas-engine-host");
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await Shot(page, $"editor-{name}-1440.png");
        }

        await page.GetByTestId("document-canvas-engine-host").ClickAsync();
        await page.Keyboard.PressAsync("Control+F");
        await Assertions.Expect(page.GetByTestId("document-find-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-find-input").FillAsync("history");
        await Assertions.Expect(page.GetByTestId("document-find-count")).ToHaveTextAsync("1 of 1", new() { Timeout = 10_000 });
        await page.GetByTestId("document-find-panel").ScrollIntoViewIfNeededAsync();
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await Shot(page, $"find-panel-{name}-1440.png");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Revision panel at 1440 in the four theme combinations")]
    public async Task CaptureRevisionPanel()
    {
        var page = await OpenAsync(RevisionsDocumentId, "/canvas-engine-host");
        await page.GetByTestId("document-side-panel-tab-revisions").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-revision-item").First).ToBeVisibleAsync(new() { Timeout = 15_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await Shot(page, $"revision-panel-{name}-1440.png");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Version panel and version dialog at 1440 in the four theme combinations")]
    public async Task CaptureVersionPanel()
    {
        var page = await OpenAsync(HistoryDocumentId, "/document-editor");
        await ClickRibbonAsync(page, "document-ribbon-tab-view");
        await ClickRibbonAsync(page, "document-open-versions");
        await Assertions.Expect(page.GetByTestId("document-version-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-version-create-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-version-label").FillAsync("Phase 2");
        await page.GetByTestId("document-version-create-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-item").First).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await Shot(page, $"version-panel-{name}-1440.png");
        }

        await page.GetByTestId("document-version-create-open").ClickAsync();
        await Assertions.Expect(page.GetByTestId("document-version-dialog")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await Shot(page, "version-dialog-dark-indigo-1440.png");
        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("Image, table and cell properties panels at 1440 in the four theme combinations")]
    public async Task CapturePropertiesPanels()
    {
        var page = await OpenAsync(ContractDocumentId, "/document-editor");

        // Table + cell properties: caret into a pricing-table cell, command palette.
        var cell = await ScrollToCanvasElementAsync(page, "[data-canvas-table-cell][data-cell-id='contract-pricing-table-r1-item']");
        await page.Mouse.ClickAsync((float)cell.X, (float)cell.Y);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-root"]')
                ?.getAttribute('data-canvas-selection-cell-id') === 'contract-pricing-table-r1-item'
            """, 10_000);
        await RunPaletteCommandAsync(page, "tableProperties");
        await Assertions.Expect(page.GetByTestId("document-table-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await ShotPanelAsync(page, $"table-properties-{name}-1440.png");
        }

        await ApplyThemeAsync(page, false, false);
        await RunPaletteCommandAsync(page, "cellProperties");
        await Assertions.Expect(page.GetByTestId("document-cell-properties-panel")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await ShotPanelAsync(page, $"cell-properties-{name}-1440.png");
        }

        // Image inspector: select the inline image, then the replace-image command opens the Properties panel.
        await ApplyThemeAsync(page, false, false);
        var image = await ScrollToCanvasElementAsync(page, "[data-canvas-object][data-object-id='contract-inline-image']");
        await page.Mouse.ClickAsync((float)image.X, (float)image.Y);
        await WaitForPredicateAsync(page, """
            () => {
                const root = document.querySelector('[data-testid="document-canvas-engine-root"]');
                return root?.getAttribute('data-canvas-object-selected') === 'true'
                    && root?.getAttribute('data-canvas-object-id') === 'contract-inline-image';
            }
            """, 10_000);
        await Assertions.Expect(page.GetByTestId("document-image-inspector")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await ShotPanelAsync(page, $"image-inspector-{name}-1440.png");
        }

        await page.Context.CloseAsync();
    }

    [TestMethod]
    [Description("The inspector building-blocks demo card in the four theme combinations")]
    public async Task CaptureInspectorBuildingBlocksCard()
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, touch: false));
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}/document-editor", WaitUntilState.DOMContentLoaded);
        var card = page.GetByTestId("document-inspector-blocks-card");
        await Assertions.Expect(card).ToBeVisibleAsync(new() { Timeout = 60_000 });
        await card.ScrollIntoViewIfNeededAsync();
        foreach (var (dark, indigo, name) in ThemeMatrix)
        {
            await ApplyThemeAsync(page, dark, indigo);
            await page.WaitForTimeoutAsync(300);
            await card.ScreenshotAsync(new LocatorScreenshotOptions
            {
                Path = Path.Combine(OutputDirectory, $"inspector-building-blocks-{name}.png"),
                Type = ScreenshotType.Png
            });
        }

        await context.CloseAsync();
    }

    internal static async Task RunPaletteCommandAsync(IPage page, string command)
    {
        await page.GetByTestId("document-canvas-hidden-input").FocusAsync();
        await page.Keyboard.PressAsync("Control+Shift+P");
        await Assertions.Expect(page.GetByTestId("document-command-palette")).ToBeVisibleAsync(new() { Timeout = 10_000 });
        await page.GetByTestId("document-command-palette-search").FillAsync(command);
        var item = page.Locator($"[data-testid='document-command-palette-item'][data-command='{command}'] button");
        await Assertions.Expect(item).ToBeEnabledAsync(new() { Timeout = 10_000 });
        await item.ClickAsync();
    }

    internal static async Task<(double X, double Y)> ScrollToCanvasElementAsync(IPage page, string selector)
    {
        for (var attempt = 0; ; attempt++)
        {
            var point = await page.EvaluateAsync<double[]?>(
                """
                selector => {
                    const element = document.querySelector(selector);
                    if (!element) return null;
                    const rect = element.getBoundingClientRect();
                    return [rect.left + rect.width / 2, rect.top + rect.height / 2];
                }
                """,
                selector);
            if (point is { Length: 2 } && point[1] > 120 && point[1] < 800)
            {
                return (point[0], point[1]);
            }

            if (attempt >= 60)
            {
                Assert.Fail($"Canvas element '{selector}' did not scroll into the viewport.");
            }

            var delta = point is null ? 900 : Math.Clamp(point[1] - 450, -800, 800);
            await page.Mouse.MoveAsync(700, 450);
            await page.Mouse.WheelAsync(0, (float)delta);
            await page.WaitForTimeoutAsync(200);
        }
    }

    private async Task<IPage> OpenAsync(string documentId, string route)
    {
        var context = await CreateContextAsync(ContextOptions(1440, 900, touch: false));
        var page = await context.NewPageAsync();
        // The canvas-engine-host route takes the seeded document id; the full editor page does for the contract demo
        // document only (the history document is the page's default).
        var url = route == "/document-editor" && documentId == ContractDocumentId
            ? $"{BaseUrl}{route}?documentId={documentId}"
            : OpenUrl(BaseUrl, documentId, route);
        await GotoAppReadyAsync(page, url, WaitUntilState.DOMContentLoaded);
        await WaitForPredicateAsync(page, """
            () => document.querySelector('[data-testid="document-canvas-engine-host"][data-canvas-engine-ready="true"]')
                && document.querySelectorAll('[data-canvas-text-rect]').length >= 1
                && document.querySelector('[data-testid="document-ribbon-tab-review"]')
            """, 60_000);
        await page.WaitForTimeoutAsync(400);
        return page;
    }

    /// <summary>The side panel on its own (an element capture scrolls the panel into view by itself).</summary>
    private static async Task ShotPanelAsync(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(300);
        await page.GetByTestId("document-side-panel").ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png
        });
    }

    private static async Task Shot(IPage page, string fileName)
    {
        await page.WaitForTimeoutAsync(300);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, fileName),
            Type = ScreenshotType.Png,
            FullPage = false
        });
    }
}
