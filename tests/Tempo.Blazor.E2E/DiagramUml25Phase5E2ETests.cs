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

    [TestMethod]
    [Description("U7 — demo UML class: header section stays inside the shape and the last list item is not clipped")]
    public async Task UmlClass_HeaderAndLastMethodVisible()
    {
        var context = await CreateContextAsync();
        await context.AddInitScriptAsync("localStorage.setItem('tm-demo-culture', 'en');");
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{BaseUrl}{DiagramEditorUrl}");
        await WaitForAppReadyAsync(page);
        await DiagramEditorE2E.PinDesktopAsync(page);

        var reportJson = await page.EvaluateAsync<string>("""
            () => {
                const shapes = Array.from(
                    document.querySelectorAll(".tm-diagram-node__shape[data-stencil-id='uml.class']"));
                return JSON.stringify(shapes.map(shape => {
                    const sb = shape.getBoundingClientRect();
                    const node = shape.closest('.tm-diagram-node');
                    const section = node ? node.querySelector('.tm-diagram-node__section') : null;
                    const items = node
                        ? Array.from(node.querySelectorAll('.tm-diagram-node__list-item'))
                        : [];
                    const last = items[items.length - 1];
                    const s = section ? section.getBoundingClientRect() : null;
                    const l = last ? last.getBoundingClientRect() : null;
                    return {
                        okTop: !!s && s.top >= sb.top - 0.5,
                        okBottom: !!l && l.bottom <= sb.bottom + 0.5,
                        sectionTop: s ? s.top : null,
                        shapeTop: sb.top,
                        lastBottom: l ? l.bottom : null,
                        shapeBottom: sb.bottom
                    };
                }));
            }
            """);
        Assert.IsNotNull(reportJson, "No uml.class shapes rendered on the demo page.");
        using var report = System.Text.Json.JsonDocument.Parse(reportJson);
        var count = 0;
        foreach (var shape in report.RootElement.EnumerateArray())
        {
            count++;
            Assert.IsTrue(shape.GetProperty("okTop").GetBoolean(),
                $"UML class header must not escape the shape top (section {shape.GetProperty("sectionTop")}, shape {shape.GetProperty("shapeTop")}).");
            Assert.IsTrue(shape.GetProperty("okBottom").GetBoolean(),
                $"UML class last list item must not be clipped at the shape bottom (last {shape.GetProperty("lastBottom")}, shape {shape.GetProperty("shapeBottom")}).");
        }
        Assert.IsTrue(count > 0, "Expected at least one uml.class shape on the demo page.");

        await TakeScreenshotAsync(page, "uml_class_header_and_last_method_visible");
    }
}
