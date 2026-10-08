using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// E2E coverage for N324 (Tempo 2.9.1): <c>MatchAnchorWidth</c> must honor the panel's own
/// min-width. The combobox panel declares <c>min-width: 320px</c>; a 240px trigger therefore
/// renders a 320px panel — and the shift clamp must use that rendered width, or the panel
/// overhangs the viewport's right edge.
/// </summary>
[TestClass]
public class MultiColumnComboBoxE2ETests : WasmTestBase
{
    private const string PageUrl = "https://localhost:7106/multi-column-combo-box";

    [TestMethod]
    public async Task MultiColumnComboBox_AtRightViewportEdge_PanelInsideViewport()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        // A narrow viewport puts the right-aligned 240px trigger's right edge against the
        // viewport edge — the only geometry where the pre-N324 math (anchor width instead of
        // the rendered min-width) could push the panel out.
        await page.SetViewportSizeAsync(460, 800);
        await page.GotoAsync(PageUrl);
        await WaitForAppReadyAsync(page);

        var host = page.Locator("[data-testid='mccb-narrow-host']");
        var trigger = host.Locator(".tm-multi-column-combo-box__trigger");
        await trigger.ClickAsync();

        var panel = host.Locator(".tm-multi-column-combo-box__dropdown.tm-overlay-panel--open");
        await panel.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible });

        var triggerBox = await trigger.BoundingBoxAsync();
        Assert.IsNotNull(triggerBox);
        Assert.IsTrue(triggerBox.Width < 300,
            $"scenario invalid: trigger must be narrower than the 320px min-width (got {triggerBox.Width})");

        var panelBox = await panel.BoundingBoxAsync();
        Assert.IsNotNull(panelBox);
        Assert.IsTrue(panelBox.Width >= 319,
            $"scenario invalid: the min-width clamp must render the panel ~320px wide (got {panelBox.Width})");
        Assert.IsTrue(panelBox.X + panelBox.Width <= 460 + 0.5,
            $"panel's right edge must stay inside the viewport (x={panelBox.X}, width={panelBox.Width})");
    }
}
