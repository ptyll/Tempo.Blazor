using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E template gallery and screenshot coverage for phase 12 diagram templates.</summary>
[TestClass]
[TestCategory("WASM")]
public sealed class DiagramPhase12TemplateGalleryE2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("Diagram template gallery exposes UML, BPMN, and ArchiMate templates, activates cards from the keyboard, and can create a BPMN document")]
    public async Task TemplateGallery_CreatesBpmnTemplateAndCapturesScreenshots()
    {
        var page = await CreateDiagramPageAsync();

        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Templates" }).ClickAsync();
        await page.Locator(".tm-diagram-template-gallery").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        await ExpectTemplateAsync(page, "UML 2.5 Class Baseline");
        await ExpectTemplateAsync(page, "BPMN 2 Process Baseline");
        await ExpectTemplateAsync(page, "ArchiMate 3 Layered Baseline");
        await AssertNoTextOverflowAsync(page);
        await SaveStableScreenshotAsync(page, "diagram_phase12_template_gallery");

        // Keyboard activation (phase 2): the card is a div[role=button] — Enter and Space must
        // select it exactly once, without any mouse interaction.
        var bpmnCard = page.Locator(".tm-diagram-template-card").Filter(new LocatorFilterOptions { HasText = "BPMN 2 Process Baseline" });
        await bpmnCard.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await ExpectCardPressedAsync(page, bpmnCard);
        Assert.IsTrue(await IsCreateEnabledAsync(page), "Create must enable after keyboard (Enter) selection");

        var umlCard = page.Locator(".tm-diagram-template-card").Filter(new LocatorFilterOptions { HasText = "UML 2.5 Class Baseline" });
        await umlCard.FocusAsync();
        await page.Keyboard.PressAsync(" ");
        await ExpectCardPressedAsync(page, umlCard);
        Assert.IsFalse(await bpmnCard.GetAttributeAsync("aria-pressed") == "true", "selection must move to the newly activated card");
        Assert.IsTrue(await IsCreateEnabledAsync(page), "Create must stay enabled after keyboard (Space) selection");

        await page.GetByText("BPMN 2 Process Baseline", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create" }).ClickAsync();

        await page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.task.user']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await page.Locator(".tm-diagram-node[data-stencil-id='bpmn2.gateway.exclusive']").WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Attached, Timeout = 10000 });
        await AssertNoTextOverflowAsync(page);
        await AssertNodesDoNotOverlapAsync(page, "bpmn2.event.start", "bpmn2.task.user", "bpmn2.gateway.exclusive", "bpmn2.event.end");
        await SaveStableScreenshotAsync(page, "diagram_phase12_bpmn_template_created");

        TestContext.WriteLine("UX: template gallery is scannable, card activation works from the keyboard (Enter and Space), and the BPMN template creates a readable process baseline without overlapping core nodes.");
    }

    private static Task ExpectCardPressedAsync(IPage page, ILocator card)
        => Assertions.Expect(card).ToHaveAttributeAsync("aria-pressed", "true", new LocatorAssertionsToHaveAttributeOptions { Timeout = 5000 });

    private static async Task<bool> IsCreateEnabledAsync(IPage page)
    {
        var create = page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Create" });
        return await create.IsEnabledAsync();
    }

    private async Task<IPage> CreateDiagramPageAsync()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(1600, 900);
        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);
        await WaitForDiagramReadyAsync(page);
        return page;
    }

    private static async Task WaitForDiagramReadyAsync(IPage page)
    {
        await page.GetByTestId("diagram-canvas").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Visible,
            Timeout = 20000
        });

        await page.WaitForFunctionAsync(
            """
            () => {
                const canvas = document.querySelector('[data-testid="diagram-canvas"]');
                if (!canvas || !canvas.id) return false;
                const editor = window.tmDiagramEditor;
                return !!(editor && editor.instances && editor.instances.get(canvas.id));
            }
            """,
            new PageWaitForFunctionOptions { Timeout = 20000 });
    }

    private static async Task ExpectTemplateAsync(IPage page, string name)
    {
        await page.GetByText(name, new PageGetByTextOptions { Exact = true })
            .WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });
    }

    private static async Task AssertNoTextOverflowAsync(IPage page)
    {
        var overflowing = await page.EvaluateAsync<string[]>(
            """
            () => Array.from(document.querySelectorAll('[data-testid="diagram-editor"] button, [data-testid="diagram-editor"] input, .tm-diagram-template-card__name'))
                .filter(el => el.scrollWidth > el.clientWidth + 2)
                .map(el => el.textContent?.trim() || el.getAttribute('aria-label') || el.getAttribute('title') || el.tagName)
                .slice(0, 8)
            """);
        Assert.AreEqual(0, overflowing.Length, "Phase 12 template UI text should fit without clipped controls: " + string.Join(", ", overflowing));
    }

    private static async Task AssertNodesDoNotOverlapAsync(IPage page, params string[] stencilIds)
    {
        var boxes = await page.EvaluateAsync<NodeBox[]>(
            """
            stencilIds => stencilIds.map(stencilId => {
                const node = document.querySelector(`.tm-diagram-node[data-stencil-id='${stencilId}']`);
                if (!node) {
                    return { stencilId, exists: false, x: 0, y: 0, width: 0, height: 0 };
                }

                const visual = node.querySelector('.tm-diagram-node__shape') || node.querySelector('.tm-diagram-node__shape-bg') || node;
                const rect = visual.getBoundingClientRect();
                return { stencilId, exists: true, x: rect.left, y: rect.top, width: rect.width, height: rect.height };
            })
            """,
            stencilIds);

        foreach (var box in boxes)
            Assert.IsTrue(box.Exists, $"{box.StencilId} should exist in the phase 12 screenshot baseline.");

        for (var i = 0; i < boxes.Length; i++)
        {
            for (var j = i + 1; j < boxes.Length; j++)
            {
                var a = boxes[i];
                var b = boxes[j];
                var overlapX = Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X));
                var overlapY = Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y));
                Assert.IsTrue(overlapX * overlapY < 40, $"{a.StencilId} should not overlap {b.StencilId} in the phase 12 screenshot baseline.");
            }
        }
    }

    private static async Task SaveStableScreenshotAsync(IPage page, string name)
    {
        // Durable directory inside the repo — vstest wipes TestResults at the end of a run,
        // so committed evidence lives in tests/Tempo.Blazor.E2E/__screenshots__/diagram-editor/.
        var directory = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "__screenshots__", "diagram-editor");
        directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);

        var bytes = await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Type = ScreenshotType.Png,
            FullPage = true
        });
        Assert.IsTrue(bytes.Length > 20_000, $"{name} screenshot should contain a rendered editor, not a blank page.");

        var path = Path.Combine(directory, $"{name}.png");
        await File.WriteAllBytesAsync(path, bytes);
        Assert.IsTrue(File.Exists(path), $"{name} screenshot must be written to the durable directory");
    }

    private sealed class NodeBox
    {
        public string StencilId { get; set; } = string.Empty;

        public bool Exists { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }
    }
}
