using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.RegularExpressions;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E smoke for the accessible <c>TmColorPalette</c> on the Color Picker demo page
/// (<c>/color-picker</c>, WASM demo at 7106): the palette grid is a labelled radiogroup, every
/// swatch announces its own color (a <c>ColorNames</c> name, or the raw value), the selection is
/// <c>aria-checked</c>, the roving tabindex + arrows + Home/End + Enter work on real keys, and
/// the two-tone inward swatch focus ring clears 3:1 in the light AND the dark theme — measured
/// on the first swatch and probed on an INTERIOR swatch adjacent to the blue one, where the old
/// outward ring measured ~1:1 (the applied theme is asserted first). Screenshots are taken with
/// the sticky demo header hidden and the card centered; light/dark element screenshots land in
/// <c>tmp/e2e-color-palette/</c> for the UX review.
/// </summary>
[TestClass]
[TestCategory("Smoke")]
public class TmColorPaletteE2ETests : WasmTestBase
{
    private const string PagePath = "/color-picker";
    private const string NamedCard = "[data-testid='color-palette-named-card']";
    private const string DefaultCard = "[data-testid='color-palette-card']";
    private const string NamedValue = "[data-testid='color-palette-named-value']";
    private const string LightSurface = "rgb(255, 255, 255)";
    private const string DarkSurface = "rgb(30, 41, 59)";

    private static readonly string ScratchDirectory = Path.Combine(
        FindRepoRoot().FullName, "tmp", "e2e-color-palette");

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static DirectoryInfo FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                return current;
            }

            current = current.Parent!;
        }

        throw new DirectoryNotFoundException("Could not locate TempoBlazor.slnx.");
    }

    private async Task<IPage> OpenColorPickerPageAsync()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await page.GotoAsync($"{BaseUrl}{PagePath}",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 60000 });
        await WaitForAppReadyAsync(page);

        await page.Locator($"{NamedCard} .tm-color-palette-swatch")
            .First.WaitForAsync(new LocatorWaitForOptions { Timeout = 30000 });
        return page;
    }

    private static ILocator Swatches(ILocator card) => card.Locator(".tm-color-palette-swatch");

    [TestMethod]
    [Description("Named palette: labelled radiogroup, per-swatch names (name or value fallback), aria-checked selection")]
    public async Task Palette1_NamedSwatches_AnnounceTheirOwnNames_AndCheckSelection()
    {
        var page = await OpenColorPickerPageAsync();
        var card = page.Locator(NamedCard);
        var swatches = Swatches(card);

        // The grid is a labelled radiogroup.
        var grid = card.Locator(".tm-color-palette-grid");
        Assert.AreEqual("radiogroup", await grid.GetAttributeAsync("role"));
        Assert.AreEqual("Select color", await grid.GetAttributeAsync("aria-label"));

        // Every swatch is a radio announcing ITS OWN color: a ColorNames name for the six named
        // colors — and the announced names must be distinct (the original bug).
        var names = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var swatch = swatches.Nth(i);
            Assert.AreEqual("radio", await swatch.GetAttributeAsync("role"));
            names.Add(await swatch.GetAttributeAsync("aria-label") ?? string.Empty);
        }

        names.Should().OnlyHaveUniqueItems("a screen reader must be able to tell the swatches apart");
        names.Should().Equal(
            "Select color Crimson",
            "Select color Amber",
            "Select color Forest green",
            "Select color Ocean blue",
            "Select color Walnut brown",
            "Select color Slate gray");

        // Clicking checks the radio and updates the bound value (shown as "Name · #hex").
        Assert.AreEqual("false", await swatches.Nth(2).GetAttributeAsync("aria-checked"));
        await swatches.Nth(2).ClickAsync();
        await Expect(swatches.Nth(2)).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(page.Locator(NamedValue)).ToHaveTextAsync("Forest green · #2E7D32");
        await Expect(page.Locator(NamedValue)).ToBeVisibleAsync();
    }

    [TestMethod]
    [Description("Named palette on real keys: arrows rove the tabindex, Home/End jump at the bounds, Enter selects (one activation)")]
    public async Task Palette2_Keyboard_RovesFocus_AndSelectsOnEnter()
    {
        var page = await OpenColorPickerPageAsync();
        var swatches = Swatches(page.Locator(NamedCard));

        // No initial selection: the roving tabindex rests on the first swatch.
        Assert.AreEqual("0", await swatches.Nth(0).GetAttributeAsync("tabindex"));

        await swatches.Nth(0).FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        Assert.AreEqual("-1", await swatches.Nth(0).GetAttributeAsync("tabindex"));
        Assert.AreEqual("0", await swatches.Nth(1).GetAttributeAsync("tabindex"));

        await page.Keyboard.PressAsync("End");
        Assert.AreEqual("0", await swatches.Nth(5).GetAttributeAsync("tabindex"));

        await page.Keyboard.PressAsync("Home");
        Assert.AreEqual("0", await swatches.Nth(0).GetAttributeAsync("tabindex"));

        // Enter activates the native button exactly once → the bound value shows the color.
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(NamedValue)).ToHaveTextAsync("Crimson · #C62828");
    }

    [TestMethod]
    [Description("Default palette without ColorNames: every swatch announces its color value, never the generic label")]
    public async Task Palette3_UnnamedPalette_SwatchesAnnounceTheirValues()
    {
        var page = await OpenColorPickerPageAsync();
        var card = page.Locator(DefaultCard);
        var swatches = Swatches(card);

        var first = await swatches.Nth(0).GetAttributeAsync("aria-label");
        var second = await swatches.Nth(1).GetAttributeAsync("aria-label");

        Assert.AreEqual("Select color #000000", first);
        Assert.AreEqual("Select color #1A1A1A", second);

        // The announced names stay distinct across the first row — the generic "Select color"
        // on every swatch was the reported accessibility defect.
        var rowNames = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            rowNames.Add(await swatches.Nth(i).GetAttributeAsync("aria-label") ?? string.Empty);
        }

        rowNames.Should().OnlyHaveUniqueItems("even the fallback naming must distinguish swatches");

        // Selecting from the default palette still updates the swatch selection state.
        await swatches.Nth(10).ClickAsync();
        await Expect(swatches.Nth(10)).ToHaveAttributeAsync("aria-checked", "true");
    }

    [TestMethod]
    [Description("Swatch focus ring clears 3:1 against the palette surface in light AND dark (theme asserted first); screenshots captured")]
    public async Task Palette4_FocusRingContrast_LightAndDark_WithScreenshots()
    {
        var page = await OpenColorPickerPageAsync();
        Directory.CreateDirectory(ScratchDirectory);

        var card = page.Locator(NamedCard);
        var swatches = Swatches(card);

        // Programmatic focus fires the component's focus handler, which pins the
        // --keyboard-focus class — the same outline declaration a :focus-visible keyboard
        // focus paints. Its resolved colour is what this test measures.
        await swatches.Nth(0).FocusAsync();
        await Expect(swatches.Nth(0)).ToHaveClassAsync(new Regex("tm-color-palette-swatch--keyboard-focus"));

        // ── light ──
        var light = await MeasureFocusRingAsync(page);
        light.Surface.Should().Be(LightSurface, "the light surface token must be what the ring is measured against");
        light.OutlineStyle.Should().Be("solid");
        light.Ratio.Should().BeGreaterThanOrEqualTo(3,
            "light: {0} ring on {1} measures {2:0.00}:1 — WCAG 1.4.11 needs 3:1", light.OutlineColor, light.Surface, light.Ratio);

        await PrepareCardScreenshotAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScratchDirectory, "palette-named-light.png"),
        });
        await page.Locator(DefaultCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScratchDirectory, "palette-default-light.png"),
        });

        // ── dark ── (the layout carries two toggles, desktop + mobile — click the visible one)
        await page.Locator("[data-testid='theme-toggle']:visible").First.ClickAsync();
        await WaitForPaletteSurfaceAsync(page, DarkSurface);

        // The theme click moved focus out of the palette — and the component now CORRECTLY drops
        // the --keyboard-focus class on focusout — so re-focus before measuring the dark ring.
        await swatches.Nth(0).FocusAsync();
        await Expect(swatches.Nth(0)).ToHaveClassAsync(new Regex("tm-color-palette-swatch--keyboard-focus"));

        // Guard the theme actually applied before measuring (light pairs often also clear 3:1).
        var dark = await MeasureFocusRingAsync(page);
        dark.Surface.Should().Be(DarkSurface, "the dark surface token must be what the ring is measured against");
        dark.OutlineStyle.Should().Be("solid");
        dark.Ratio.Should().BeGreaterThanOrEqualTo(3,
            "dark: {0} ring on {1} measures {2:0.00}:1 — WCAG 1.4.11 needs 3:1", dark.OutlineColor, dark.Surface, dark.Ratio);

        await PrepareCardScreenshotAsync(page);
        await card.ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScratchDirectory, "palette-named-dark.png"),
        });
        await page.Locator(DefaultCard).ScreenshotAsync(new LocatorScreenshotOptions
        {
            Path = Path.Combine(ScratchDirectory, "palette-default-dark.png"),
        });

        foreach (var name in new[] { "palette-named-light.png", "palette-default-light.png", "palette-named-dark.png", "palette-default-dark.png" })
        {
            File.Exists(Path.Combine(ScratchDirectory, name))
                .Should().BeTrue($"the UX-review screenshot {name} must exist");
        }
    }

    [TestMethod]
    [Description("Interior swatch next to the blue one: the two-tone ring stays inside the swatch and clears 3:1")]
    public async Task Palette5_InteriorSwatchFocusRing_StaysInsideBesideTheBlueSwatch()
    {
        var page = await OpenColorPickerPageAsync();

        // Walnut brown (index 4) sits directly beside Ocean blue (index 3) in the second row of
        // the 3-column named palette — exactly where the old OUTWARD ring measured ~1:1.
        await Swatches(page.Locator(NamedCard)).Nth(4).FocusAsync();
        await Expect(Swatches(page.Locator(NamedCard)).Nth(4)).ToHaveClassAsync(new Regex("tm-color-palette-swatch--keyboard-focus"));

        var probe = await MeasureInteriorSwatchRingAsync(page);

        probe.NeighbourLabel.Should().Be(
            "Select color Ocean blue",
            "the probe must sit on the interior swatch adjacent to the blue one — that adjacency is the regression case");
        probe.OutlineOffset.Should().StartWith(
            "-",
            "the ring must run INSIDE the swatch edge (the outward offset painted across the 2px gap onto the blue neighbour)");
        probe.HasInsetSurfaceBand.Should().BeTrue(
            "the surface band must separate the ring from the swatch fill (the two-tone design)");
        probe.Ratio.Should().BeGreaterThanOrEqualTo(3,
            "interior swatch: {0} ring measures {1:0.00}:1 against its surface band — WCAG 1.4.11 needs 3:1",
            probe.RingColor, probe.Ratio);
    }

    /// <summary>Hides the sticky demo header (visibility keeps the layout) and centers the named
    /// palette card in the viewport, so element screenshots are not overpainted by the bar.</summary>
    private static async Task PrepareCardScreenshotAsync(IPage page) =>
        await page.EvaluateAsync(
            """
            () => {
                const sticky = document.querySelector("main > div.sticky");
                if (sticky) sticky.style.visibility = "hidden";
                const card = document.querySelector("[data-testid='color-palette-named-card']");
                if (card) card.scrollIntoView({ block: "center" });
            }
            """);

    /// <summary>Waits until the palette's computed surface background reaches the expected theme
    /// colour — the deterministic replacement for a fixed post-toggle sleep.</summary>
    private static async Task WaitForPaletteSurfaceAsync(IPage page, string expectedSurface) =>
        await page.WaitForFunctionAsync(
            """
            ([expected]) => {
                const palette = document.querySelector("[data-testid='color-palette-named-card'] .tm-color-palette");
                return palette && getComputedStyle(palette).backgroundColor === expected;
            }
            """,
            new[] { expectedSurface },
            new PageWaitForFunctionOptions { Timeout = 15000 });

    private static async Task<FocusRingProbe> MeasureFocusRingAsync(IPage page) =>
        await page.EvaluateAsync<FocusRingProbe>(
            """
            () => {
                const swatch = document.querySelector("[data-testid='color-palette-named-card'] .tm-color-palette-swatch");
                const card = document.querySelector("[data-testid='color-palette-named-card'] .tm-color-palette");
                if (!swatch || !card) throw new Error("palette not rendered");
                const parse = (value) => {
                    const match = value.match(/rgba?\(([^)]+)\)/);
                    if (!match) return null;
                    const parts = match[1].split(',').map(Number);
                    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
                };
                const linear = (v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                const luminance = (c) => 0.2126 * linear(c.r) + 0.7152 * linear(c.g) + 0.0722 * linear(c.b);
                const format = (c) => `rgb(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)})`;
                const swatchStyle = getComputedStyle(swatch);
                const cardStyle = getComputedStyle(card);
                let ring = parse(swatchStyle.outlineColor);
                let surface = parse(cardStyle.backgroundColor);
                if (!ring || !surface) throw new Error("unreadable colours: " + swatchStyle.outlineColor + " / " + cardStyle.backgroundColor);
                // The outline is opaque in both themes; composite defensively if a theme ever
                // makes it translucent.
                if (ring.a < 1) ring = { r: ring.r * ring.a + surface.r * (1 - ring.a), g: ring.g * ring.a + surface.g * (1 - ring.a), b: ring.b * ring.a + surface.b * (1 - ring.a), a: 1 };
                const l1 = luminance(ring), l2 = luminance(surface);
                return {
                    outlineColor: format(ring),
                    outlineStyle: swatchStyle.outlineStyle,
                    outlineWidth: swatchStyle.outlineWidth,
                    surface: format(surface),
                    ratio: (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05),
                };
            }
            """);

    /// <summary>Public parameterless type: Playwright's <c>EvaluateAsync&lt;T&gt;</c> cannot
    /// construct positional records from the deserialized JSON object.</summary>
    private sealed class FocusRingProbe
    {
        public string OutlineColor { get; set; } = string.Empty;
        public string OutlineStyle { get; set; } = string.Empty;
        public string OutlineWidth { get; set; } = string.Empty;
        public string Surface { get; set; } = string.Empty;
        public double Ratio { get; set; }
    }

    /// <summary>Public parameterless type: Playwright's <c>EvaluateAsync&lt;T&gt;</c> cannot
    /// construct positional records from the deserialized JSON object.</summary>
    private sealed class InteriorRingProbe
    {
        public string NeighbourLabel { get; set; } = string.Empty;
        public string OutlineOffset { get; set; } = string.Empty;
        public bool HasInsetSurfaceBand { get; set; }
        public string RingColor { get; set; } = string.Empty;
        public double Ratio { get; set; }
    }

    /// <summary>
    /// Probes the FOCUSED interior swatch (index 4) whose right-hand neighbour is the blue one:
    /// the ring must be offset inward with an inset surface band, and its colour must clear 3:1
    /// against that band — the two-tone design makes the measurement neighbour-independent.
    /// </summary>
    private static async Task<InteriorRingProbe> MeasureInteriorSwatchRingAsync(IPage page) =>
        await page.EvaluateAsync<InteriorRingProbe>(
            """
            () => {
                const card = document.querySelector("[data-testid='color-palette-named-card']");
                const swatches = card.querySelectorAll(".tm-color-palette-swatch");
                const focused = swatches[4];
                const neighbour = swatches[3];
                const palette = card.querySelector(".tm-color-palette");
                if (!focused || !neighbour || !palette) throw new Error("palette not rendered");
                const parse = (value) => {
                    const match = value.match(/rgba?\(([^)]+)\)/);
                    if (!match) return null;
                    const parts = match[1].split(',').map(Number);
                    return { r: parts[0], g: parts[1], b: parts[2], a: parts.length > 3 ? parts[3] : 1 };
                };
                const linear = (v) => { v /= 255; return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                const luminance = (c) => 0.2126 * linear(c.r) + 0.7152 * linear(c.g) + 0.0722 * linear(c.b);
                const format = (c) => `rgb(${Math.round(c.r)}, ${Math.round(c.g)}, ${Math.round(c.b)})`;
                const style = getComputedStyle(focused);
                const ring = parse(style.outlineColor);
                const surface = parse(getComputedStyle(palette).backgroundColor);
                if (!ring || !surface) throw new Error("unreadable colours: " + style.outlineColor);
                const opaque = ring.a < 1
                    ? { r: ring.r * ring.a + surface.r * (1 - ring.a), g: ring.g * ring.a + surface.g * (1 - ring.a), b: ring.b * ring.a + surface.b * (1 - ring.a), a: 1 }
                    : ring;
                const l1 = luminance(opaque), l2 = luminance(surface);
                return {
                    neighbourLabel: neighbour.getAttribute("aria-label"),
                    outlineOffset: style.outlineOffset,
                    hasInsetSurfaceBand: /inset/.test(style.boxShadow),
                    ringColor: format(opaque),
                    ratio: (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05),
                };
            }
            """);
}
