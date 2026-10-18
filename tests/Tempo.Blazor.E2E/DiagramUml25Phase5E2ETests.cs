using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>E2E and screenshot checks for phase 5 UML 2.5 stencils.</summary>
[TestClass]
[TestCategory("WASM")]
public class DiagramUml25Phase5E2ETests : PlaywrightTestBase
{
    private const string DiagramEditorUrl = "/diagram-editor";

    /// <inheritdoc />
    protected override string BaseUrl => "http://localhost:5010";

    [TestMethod]
    [Description("UML 2.5 class stencil is searchable, droppable, and renders its compartments on the canvas")]
    public async Task Canvas_DropsUml25ClassStencil_WithReadableCompartments()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);

        // The docked toolbox exists only in the DESKTOP band (the demo column is tablet at
        // every default viewport, phase 6) — pin the container like a full-bleed host.
        await DiagramEditorE2E.PinDesktopAsync(page);

        var toolbox = page.Locator(".tm-diagram-toolbox");
        await toolbox.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        var search = page.Locator(".tm-diagram-toolbox__search input");
        await search.FillAsync("UML 2.5 Class");
        await page.WaitForTimeoutAsync(300);

        var umlLibrary = page.Locator("[data-library-id='uml25']");
        await umlLibrary.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var classStencil = page.Locator(".tm-diagram-toolbox__item[data-stencil-id='uml25.class']");
        await classStencil.ScrollIntoViewIfNeededAsync();
        await classStencil.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        Assert.AreEqual("node", await classStencil.GetAttributeAsync("data-stencil-kind"));
        Assert.AreEqual("0", await classStencil.GetAttributeAsync("tabindex"));
        Assert.AreEqual("button", await classStencil.GetAttributeAsync("role"));

        var canvas = page.Locator(".tm-diagram-canvas").First;
        await canvas.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 20000 });

        await page.EvaluateAsync(
            """
            () => {
                const canvas = document.querySelector('.tm-diagram-canvas');
                const rect = canvas.getBoundingClientRect();
                window.__tmDiagramDragStencil = 'uml25.class';
                canvas.dispatchEvent(new DragEvent('drop', {
                    bubbles: true,
                    cancelable: true,
                    clientX: rect.left + rect.width * 0.58,
                    clientY: rect.top + rect.height * 0.44,
                    dataTransfer: new DataTransfer()
                }));
            }
            """);

        var droppedClass = page.Locator(".tm-diagram-node[data-stencil-id='uml25.class']").Last;
        await droppedClass.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 10000 });

        var classText = await droppedClass.TextContentAsync() ?? string.Empty;
        StringAssert.Contains(classText, "ClassName");
        StringAssert.Contains(classText, "- id: Guid");
        StringAssert.Contains(classText, "+ Save(): void");

        await TakeScreenshotAsync(page, "diagram_uml25_phase5_class_drop");
    }
}
