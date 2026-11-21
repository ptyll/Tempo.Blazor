using System.Globalization;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

[TestClass]
public class SignatureCaptureE2ETests : WasmTestBase
{
    [TestMethod]
    [Description("Signature capture draw mode captures a mouse-drawn signature")]
    public async Task SignatureCapture_Draw_CapturesSignature()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        await DrawSignatureAsync(page, "[data-testid='signature-draw-capture']");

        await Expect(page.Locator("[data-testid='signature-draw-value']")).ToContainTextAsync("Value captured");
    }

    [TestMethod]
    [Description("Signature capture keeps the current stroke active when the pointer leaves and returns")]
    public async Task SignatureCapture_Draw_ContinuesAfterPointerLeavesAndReturns()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        await DrawSignatureAcrossCanvasEdgeAsync(page, "[data-testid='signature-draw-capture']");

        await Expect(page.Locator("[data-testid='signature-draw-value']")).ToContainTextAsync("Value captured");
    }

    [TestMethod]
    [Description("Signature capture maps pointer coordinates into SVG viewBox coordinates when the canvas is scaled")]
    public async Task SignatureCapture_Draw_TracksCursorWhenCanvasIsScaled()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var root = page.Locator("[data-testid='signature-draw-capture']").First;
        await root.ScrollIntoViewIfNeededAsync();
        await root.EvaluateAsync("element => { element.style.width = '280px'; element.style.maxWidth = '280px'; }");

        var canvas = root.Locator(".tm-signature-capture__canvas").First;
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");

        var startX = box!.X + box.Width * 0.72;
        var startY = box.Y + box.Height * 0.34;
        var expected = await canvas.EvaluateAsync<SvgPoint>(
            @"(element, point) => {
                const svgPoint = element.createSVGPoint();
                svgPoint.x = point.x;
                svgPoint.y = point.y;
                const transformed = svgPoint.matrixTransform(element.getScreenCTM().inverse());
                return { x: transformed.x, y: transformed.y };
            }",
            new { x = startX, y = startY });

        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(startX + 18), (float)(startY + 8), new MouseMoveOptions { Steps = 2 });
        await page.Mouse.UpAsync();

        await Assertions.Expect(canvas.Locator("polyline").First).ToBeVisibleAsync();
        var firstPoint = ParseFirstPoint(await canvas.Locator("polyline").First.GetAttributeAsync("points"));
        Assert.AreEqual(expected.X, firstPoint.X, 1.0, "The drawn stroke should start under the cursor horizontally.");
        Assert.AreEqual(expected.Y, firstPoint.Y, 1.0, "The drawn stroke should start under the cursor vertically.");
    }

    [TestMethod]
    [Description("Signature capture clear button clears a drawn signature")]
    public async Task SignatureCapture_Clear_ClearsSignature()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        await DrawSignatureAsync(page, "[data-testid='signature-draw-capture']");
        await Expect(page.Locator("[data-testid='signature-draw-value']")).ToContainTextAsync("Value captured");

        await page.Locator("[data-testid='signature-draw-capture'] .tm-signature-capture__clear").ClickAsync();

        await Expect(page.Locator("[data-testid='signature-draw-value']")).ToContainTextAsync("No value");
    }

    [TestMethod]
    [Description("Signature capture typed mode captures typed signature text")]
    public async Task SignatureCapture_Typed_CapturesTypedSignature()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        var input = page.Locator("[data-testid='signature-typed-capture'] .tm-signature-capture__typed-input").First;
        await input.ScrollIntoViewIfNeededAsync();
        await input.FillAsync("Alex Johnson");
        await input.PressAsync("Tab");

        await Expect(page.Locator("[data-testid='signature-typed-value']")).ToContainTextAsync("Typed value captured");
        await Expect(page.Locator("[data-testid='signature-typed-capture'] .tm-signature-capture__typed-preview")).ToContainTextAsync("Alex Johnson");
    }

    [TestMethod]
    [Description("Signature capture stays usable on mobile viewport")]
    public async Task SignatureCapture_MobileViewport_CapturesPointerSignature()
    {
        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetViewportSizeAsync(390, 844);

        await GotoAppReadyAsync(page, $"{BaseUrl}/signing-components");

        await DrawSignatureAsync(page, "[data-testid='signature-draw-capture']");

        var box = await page.Locator("[data-testid='signature-draw-capture'] .tm-signature-capture__canvas").BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box on mobile.");
        Assert.IsTrue(box!.Width > 250, "Signature canvas should remain wide enough on mobile.");
        Assert.IsTrue(box.Height > 100, "Signature canvas should remain tall enough on mobile.");
        await Expect(page.Locator("[data-testid='signature-draw-value']")).ToContainTextAsync("Value captured");
    }

    private static async Task DrawSignatureAsync(IPage page, string rootSelector)
    {
        var canvas = page.Locator($"{rootSelector} .tm-signature-capture__canvas").First;
        await canvas.ScrollIntoViewIfNeededAsync();
        await canvas.EvaluateAsync("element => element.scrollIntoView({ block: 'center', inline: 'center' })");
        await page.WaitForTimeoutAsync(100);
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");
        Assert.IsTrue(
            double.IsFinite(box!.X) && double.IsFinite(box.Y) && double.IsFinite(box.Width) && double.IsFinite(box.Height),
            $"Signature canvas should have finite bounds, but was x={box.X}, y={box.Y}, width={box.Width}, height={box.Height}.");
        Assert.IsTrue(
            Math.Abs(box.X) < 10000 && Math.Abs(box.Y) < 10000 && box.Width < 10000 && box.Height < 10000,
            $"Signature canvas should have usable bounds, but was x={box.X}, y={box.Y}, width={box.Width}, height={box.Height}.");

        var startX = box.X + box.Width * 0.18;
        var startY = box.Y + box.Height * 0.18;
        var hitTarget = await page.EvaluateAsync<string>(
            "point => document.elementFromPoint(point.x, point.y)?.outerHTML.slice(0, 200) || ''",
            new { x = startX, y = startY });
        Assert.IsTrue(hitTarget.Contains("tm-signature-capture__canvas"), $"Mouse start should hit signature canvas, but hit: {hitTarget}");
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.38), (float)(box.Y + box.Height * 0.28), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.62), (float)(box.Y + box.Height * 0.20), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.82), (float)(box.Y + box.Height * 0.32), new MouseMoveOptions { Steps = 4 });
        await page.Mouse.UpAsync();
    }

    private static async Task DrawSignatureAcrossCanvasEdgeAsync(IPage page, string rootSelector)
    {
        var canvas = page.Locator($"{rootSelector} .tm-signature-capture__canvas").First;
        await canvas.ScrollIntoViewIfNeededAsync();
        await canvas.EvaluateAsync("element => element.scrollIntoView({ block: 'center', inline: 'center' })");
        await page.WaitForTimeoutAsync(100);
        var box = await canvas.BoundingBoxAsync();
        Assert.IsNotNull(box, "Signature canvas should have a bounding box.");

        var startX = box!.X + box.Width * 0.18;
        var startY = box.Y + box.Height * 0.25;
        await page.Mouse.MoveAsync((float)startX, (float)startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.34), (float)(box.Y + box.Height * 0.34), new MouseMoveOptions { Steps = 3 });
        await page.Mouse.MoveAsync((float)(box.X - 24), (float)(box.Y + box.Height * 0.52), new MouseMoveOptions { Steps = 3 });
        await page.Mouse.MoveAsync((float)(box.X + box.Width * 0.58), (float)(box.Y + box.Height * 0.62), new MouseMoveOptions { Steps = 3 });

        await page.WaitForFunctionAsync(
            @"selector => {
                const polyline = document.querySelector(selector);
                const points = polyline?.getAttribute('points') || '';
                return points.trim().split(/\s+/).filter(Boolean).length >= 3;
            }",
            $"{rootSelector} .tm-signature-capture__canvas polyline");

        await page.Mouse.UpAsync();
    }

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private static SvgPoint ParseFirstPoint(string? points)
    {
        Assert.IsFalse(string.IsNullOrWhiteSpace(points), "Polyline points should be rendered.");
        var first = points!.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        var coordinates = first.Split(',', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, coordinates.Length, "First polyline point should contain x and y coordinates.");
        return new SvgPoint
        {
            X = double.Parse(coordinates[0], CultureInfo.InvariantCulture),
            Y = double.Parse(coordinates[1], CultureInfo.InvariantCulture)
        };
    }

    private sealed class SvgPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }
}
