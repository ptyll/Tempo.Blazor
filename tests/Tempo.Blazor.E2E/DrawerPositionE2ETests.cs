using FluentAssertions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// TG-006: a MODAL <c>TmDrawer</c> root is a top-layer popover pinned to <c>inset: 0</c>, so it
/// stretches across the whole viewport while the overlay stays <c>position: fixed</c> (out of
/// flow). The panel is then the only flex item and, without an end alignment, a
/// <c>Position=Right</c> drawer stood at the LEFT window edge (LTR, ≥ 641 px). These tests measure
/// the panel's bounding box against the viewport at several widths, in light and dark, in LTR and
/// RTL, with custom and overflowing widths, for the inline (non-modal) variant, opened from a
/// modal in the top layer, and they guard the overlay click and the focus trap/restore.
///
/// Positions are PHYSICAL: the panel's right edge belongs at the window's right edge in BOTH LTR
/// and RTL (the slide keyframes travel physical axes too).
///
/// Screenshots land in <c>tests/Tempo.Blazor.E2E/TestResults/drawer-position/</c>.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public class DrawerPositionE2ETests : WasmTestBase
{
    private const string FeedbackPage = "/feedback";

    // NOTE: Playwright's EvaluateAsync deserializer matches the JS keys case-insensitively against
    // the C# property NAMES and ignores [JsonPropertyName] — the properties must be named exactly
    // like the JS keys (Left, Right, Width, Vw, …) or they deserialize to 0.
    private sealed class PanelBox
    {
        public double Left { get; set; }
        public double Right { get; set; }
        public double Width { get; set; }
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Vw { get; set; }
        public double Vh { get; set; }
    }

    private async Task<IPage> OpenFeedbackPageAsync(int width = 1440, int height = 900)
    {
        var context = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            Locale = "en-US",
            IgnoreHTTPSErrors = true,
            AcceptDownloads = true
        });
        RegisterContext(context);
        var page = await context.NewPageAsync();
        await GotoAppReadyAsync(page, $"{BaseUrl}{FeedbackPage}");
        return page;
    }

    private static async Task<PanelBox> MeasurePanelAsync(IPage page, string panelSelector = ".tm-drawer__panel")
    {
        var box = await page.EvaluateAsync<PanelBox>(
            """
            (selector) => {
                const p = document.querySelector(selector);
                if (!p) return null;
                const r = p.getBoundingClientRect();
                return { left: r.left, right: r.right, width: r.width, top: r.top, bottom: r.bottom,
                         vw: window.innerWidth, vh: window.innerHeight };
            }
            """, panelSelector);
        return box ?? throw new InvalidOperationException($"no panel matched '{panelSelector}'");
    }

    private static async Task OpenRightDrawerAsync(IPage page)
    {
        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await WaitForLayoutToSettleAsync(page, ".tm-drawer__panel");
    }

    private static async Task OpenLeftDrawerAsync(IPage page)
    {
        await page.Locator("[data-testid='open-left-drawer']").ClickAsync();
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await WaitForLayoutToSettleAsync(page, ".tm-drawer__panel");
    }

    private async Task<string> SaveScreenshotAsync(IPage page, string name)
    {
        var directory = Path.Combine(FindRepoRoot(), "TestResults", "drawer-position");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{name}.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Type = ScreenshotType.Png, Path = path, Timeout = 60_000 });
        TestContext.AddResultFile(path);
        return path;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "TempoBlazor.slnx")))
            dir = dir.Parent;
        return dir!.FullName;
    }

    // ── Core position: right panel at the right edge, left at the left edge ──

    [TestMethod]
    [Description("TG-006: at 1440 px light, the right drawer's right edge sits at the window's right edge")]
    public async Task RightDrawer_1440_Light_PanelRightEdge_AtWindowRightEdge()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await OpenRightDrawerAsync(page);

        var box = await MeasurePanelAsync(page);
        box.Right.Should().BeApproximately(box.Vw, 2,
            "the right drawer's right edge must touch the window's right edge");

        await SaveScreenshotAsync(page, "right-1440-light");
    }

    [TestMethod]
    [Description("TG-006: at 1440 px dark, the right drawer still stands at the right edge")]
    public async Task RightDrawer_1440_Dark_PanelRightEdge_AtWindowRightEdge()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await ToggleDarkModeAsync(page);
        await OpenRightDrawerAsync(page);

        var box = await MeasurePanelAsync(page);
        box.Right.Should().BeApproximately(box.Vw, 2,
            "the right drawer's right edge must touch the window's right edge in dark mode too");

        await SaveScreenshotAsync(page, "right-1440-dark");
    }

    [TestMethod]
    [Description("TG-006: the left drawer's left edge sits at the window's left edge (regression)")]
    public async Task LeftDrawer_1440_Light_PanelLeftEdge_AtWindowLeftEdge()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await OpenLeftDrawerAsync(page);

        var box = await MeasurePanelAsync(page);
        box.Left.Should().BeApproximately(0, 2, "the left drawer's left edge must touch the window's left edge");

        await SaveScreenshotAsync(page, "left-1440-light");
    }

    // ── Widths 768 (tablet) and 390 (phone) ──────────────────────────────────

    [TestMethod]
    [Description("TG-006: at 768 px the right drawer stands at the right edge")]
    public async Task RightDrawer_768_PanelRightEdge_AtWindowRightEdge()
    {
        var page = await OpenFeedbackPageAsync(768, 1024);
        await OpenRightDrawerAsync(page);

        var box = await MeasurePanelAsync(page);
        box.Right.Should().BeApproximately(box.Vw, 2, "the right drawer must stand at the right edge at 768 px");

        await SaveScreenshotAsync(page, "right-768-light");
    }

    [TestMethod]
    [Description("TG-006: at 390 px the panel is full-width and still anchored to the right edge")]
    public async Task RightDrawer_390_PanelIsFullWidth_AndAnchoredRight()
    {
        var page = await OpenFeedbackPageAsync(390, 844);
        await OpenRightDrawerAsync(page);

        var box = await MeasurePanelAsync(page);
        box.Right.Should().BeApproximately(box.Vw, 2, "the full-width panel's right edge must stay at the window's right edge");
        box.Width.Should().BeApproximately(box.Vw, 2, "below 640 px the panel fills the viewport width");

        await SaveScreenshotAsync(page, "right-390-light");
    }

    // ── RTL: positions are physical — the same window edges ─────────────────

    [TestMethod]
    [Description("TG-006: in RTL the right drawer keeps its right edge at the window's right edge and the left drawer its left edge")]
    public async Task Rtl_RightDrawerRightEdge_And_LeftDrawerLeftEdge_AtWindowEdges()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await page.EvaluateAsync("() => document.documentElement.setAttribute('dir', 'rtl')");

        await OpenRightDrawerAsync(page);
        var right = await MeasurePanelAsync(page);
        right.Right.Should().BeApproximately(right.Vw, 2,
            "Position=Right is physical: in RTL the panel's right edge must still touch the window's right edge");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        await OpenLeftDrawerAsync(page);
        var left = await MeasurePanelAsync(page);
        left.Left.Should().BeApproximately(0, 2,
            "Position=Left is physical: in RTL the panel's left edge must still touch the window's left edge");

        await SaveScreenshotAsync(page, "rtl-left-drawer-1440");
    }

    // ── Custom width and width larger than the window ───────────────────────

    [TestMethod]
    [Description("TG-006: a custom 600 px drawer keeps its right edge at the window's right edge")]
    public async Task RightDrawer_CustomWidth600_PanelRightEdge_AtWindowRightEdge()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        var widthSelect = page.Locator("[data-testid='right-drawer-width'] select");
        await widthSelect.SelectOptionAsync(new SelectOptionValue { Value = "600px" });

        await OpenRightDrawerAsync(page);
        var box = await MeasurePanelAsync(page);
        box.Width.Should().BeApproximately(600, 2, "the custom --tm-drawer-width must size the panel");
        box.Right.Should().BeApproximately(box.Vw, 2, "a 600 px panel must still stand flush to the right edge");

        await SaveScreenshotAsync(page, "right-1440-width-600");
    }

    [TestMethod]
    [Description("TG-006: a drawer wider than the window clamps to the viewport and stays right-anchored")]
    public async Task RightDrawer_WiderThanWindow_ClampsToViewport_AndStaysRightAnchored()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        var widthSelect = page.Locator("[data-testid='right-drawer-width'] select");
        await widthSelect.SelectOptionAsync(new SelectOptionValue { Label = "Wider than window" });

        await OpenRightDrawerAsync(page);
        var box = await MeasurePanelAsync(page);
        box.Width.Should().BeApproximately(box.Vw, 2, "max-width: 100vw must clamp a panel wider than the window");
        box.Right.Should().BeApproximately(box.Vw, 2, "the clamped panel must still stand flush to the right edge");

        await SaveScreenshotAsync(page, "right-1440-width-overflow");
    }

    // ── Inline (non-modal) drawer in a container ────────────────────────────

    [TestMethod]
    [Description("TG-006: a Modal=false right drawer anchors to its container's right edge, not the window's")]
    public async Task InlineRightDrawer_AnchorsToContainerRightEdge()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        var host = page.Locator("[data-testid='inline-drawer-host']");
        await host.ScrollIntoViewIfNeededAsync();
        await host.Locator("[data-testid='open-inline-right-drawer']").ClickAsync();
        await page.Locator("[data-testid='inline-drawer-host'] .tm-drawer__panel")
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await WaitForLayoutToSettleAsync(page, "[data-testid='inline-drawer-host'] .tm-drawer__panel");

        var hostBox = await host.BoundingBoxAsync();
        hostBox.Should().NotBeNull();
        var panel = await MeasurePanelAsync(page, "[data-testid='inline-drawer-host'] .tm-drawer__panel");
        panel.Right.Should().BeApproximately(hostBox!.X + hostBox.Width, 2,
            "an inline (non-modal) drawer carries no popover and anchors to its container's right edge");
        panel.Right.Should().BeLessThan(hostBox.X + hostBox.Width + 2);

        await SaveScreenshotAsync(page, "inline-right-drawer-container");
    }

    // ── Top layer: a drawer opened from a modal ─────────────────────────────

    [TestMethod]
    [Description("TG-006: a drawer opened from inside a TmModal promotes above it and stands at the right edge")]
    public async Task Drawer_OpenedFromModal_StandsAtRightEdge_AboveTheModal()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await page.Locator("[data-testid='open-drawer-launchpad']").ClickAsync();
        await page.Locator("[data-testid='launchpad-open-right-drawer']").ClickAsync();
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        await WaitForLayoutToSettleAsync(page, ".tm-drawer__panel");

        var box = await MeasurePanelAsync(page);
        box.Right.Should().BeApproximately(box.Vw, 2,
            "the drawer opened from the modal must still stand at the right edge");

        var panelOnTop = await page.EvaluateAsync<bool>(
            """
            () => {
                const panel = document.querySelector('.tm-drawer__panel');
                if (!panel) return false;
                const top = document.elementFromPoint(panel.getBoundingClientRect().left + 20,
                                                      panel.getBoundingClientRect().top + 20);
                return !!top && !!top.closest('.tm-drawer');
            }
            """);
        panelOnTop.Should().BeTrue("the promoted drawer must paint (and hit-test) above the modal beneath it");

        await SaveScreenshotAsync(page, "drawer-from-modal-top-layer");
    }

    // ── Overlay click left of the panel ─────────────────────────────────────

    [TestMethod]
    [Description("TG-006: clicking the overlay left of the right panel closes the drawer (the stretched root must not swallow it)")]
    public async Task OverlayClick_LeftOfRightPanel_ClosesDrawer()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await OpenRightDrawerAsync(page);

        // With a 420 px panel at the right, x=700 lands on the overlay in the fixed layout
        // (and on the overlay in the old buggy left-aligned layout too, where the panel ends at 420).
        await page.Mouse.ClickAsync(700, 450);
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });
    }

    // ── Focus trap and focus restore ────────────────────────────────────────

    [TestMethod]
    [Description("TG-006: focus moves into the opened drawer and returns to the trigger after Escape")]
    public async Task Focus_MovesIntoDrawer_AndReturnsToTrigger_OnClose()
    {
        var page = await OpenFeedbackPageAsync(1440, 900);
        await OpenRightDrawerAsync(page);

        var activeInDrawer = await page.EvaluateAsync<bool>(
            "() => !!document.activeElement && !!document.activeElement.closest('.tm-drawer')");
        activeInDrawer.Should().BeTrue("the modal drawer's focus trap moves focus into the panel");

        await page.Keyboard.PressAsync("Escape");
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Detached });

        var restored = await page.EvaluateAsync<string?>(
            "() => document.activeElement?.getAttribute('data-testid') ?? document.activeElement?.id ?? ''");
        restored.Should().Be("open-right-drawer", "closing hands the focus back to the opener");
    }
}

/// <summary>
/// TG-006 smoke against the Blazor Server demo (:7107): the SignalR round trip renders the same
/// promoted popover drawer, so the panel must stand at the right edge there too.
/// </summary>
[TestClass]
[TestCategory("Server")]
[TestCategory("Smoke")]
public class DrawerPositionServerE2ETests : ServerTestBase
{
    [TestMethod]
    [Description("TG-006 (Server smoke): the right drawer's right edge sits at the window's right edge")]
    public async Task RightDrawer_Server_PanelRightEdge_AtWindowRightEdge()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1440, 900);
        await GotoAppReadyAsync(page, $"{BaseUrl}/feedback");

        await page.Locator("[data-testid='open-right-drawer']").ClickAsync();
        await page.Locator(".tm-drawer__panel").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });
        // The slide-in keyframe animation still carries translateX(100%) for its first frames —
        // a geometry read before it settles measures the panel one width past the edge.
        await WaitForLayoutToSettleAsync(page, ".tm-drawer__panel");

        var box = await page.EvaluateAsync<JsonBox>("""
            () => {
                const r = document.querySelector('.tm-drawer__panel').getBoundingClientRect();
                return { right: r.right, vw: window.innerWidth };
            }
            """);
        box.Right.Should().BeApproximately(box.Vw, 2,
            "the right drawer must stand at the right edge on the Server demo too");
    }

    private sealed class JsonBox
    {
        public double Right { get; set; }
        public double Vw { get; set; }
    }
}
