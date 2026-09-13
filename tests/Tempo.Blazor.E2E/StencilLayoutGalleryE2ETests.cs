using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("Wireframe")]
public class StencilLayoutGalleryE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task StencilLayout_GalleryScreenshot()
    {
        var svgMarkup = await RenderGallerySvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().NotContainEquivalentOf("<script");
        svgMarkup.Should().NotContainEquivalentOf("<foreignObject");

        var context = await CreateContextAsync();
        var page = await context.NewPageAsync();
        await page.SetContentAsync(
            $"""
            <!doctype html>
            <html>
            <body style="margin:0;background:#f8fafc">{svgMarkup}</body>
            </html>
            """);

        var svg = page.Locator("svg").First;
        await svg.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 15000 });

        var path = BaselinePath("stencil-layout", "stencil-layout-gallery");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderGallerySvgAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
                builder.AddAttribute(2, "viewBox", "0 0 920 620");
                builder.AddAttribute(3, "width", "920");
                builder.AddAttribute(4, "height", "620");
                builder.AddMarkupContent(5, "<rect x='0' y='0' width='920' height='620' fill='#f8fafc'></rect>");

                RenderScene(builder, 30, 60, "Stack gap", Component(StackNode()), Element(150, 110));
                RenderScene(builder, 230, 60, "Row gap", Component(RowNode()), Element(180, 80));
                RenderScene(builder, 470, 60, "Grid columns", Component(GridNode()), Element(210, 110));
                RenderScene(builder, 720, 60, "Repeat cap", Component(RepeatNode()), Element(150, 70));
                RenderScene(builder, 30, 260, "9slice small", Component(NineSliceNode(), StencilResize.NineSlice, Slice()), Element(120, 70));
                RenderScene(builder, 230, 260, "9slice large", Component(NineSliceNode(), StencilResize.NineSlice, Slice()), Element(260, 70));
                RenderScene(builder, 560, 260, "Right anchor", Component(RightAnchorNode()), Element(230, 80));

                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private static void RenderScene(
        RenderTreeBuilder builder,
        double x,
        double y,
        string title,
        StencilComponent component,
        WireframeElement element)
    {
        builder.AddMarkupContent(10, $"<g transform='translate({WireframeSvg.F(x)},{WireframeSvg.F(y)})'>");
        builder.AddMarkupContent(11, WireframeSvg.Text(title, 0, -22, 12, WireframeSvg.ColorText, fontWeight: "600"));
        builder.AddMarkupContent(12, WireframeSvg.Rect(-8, -8, element.W + 16, element.H + 16, "#ffffff", "#e5e7eb", 6));
        StencilPackRenderer.Render(component, element, StencilTokenScope.Empty, builder);
        builder.AddMarkupContent(13, "</g>");
    }

    private static StencilComponent Component(
        RenderNode render,
        StencilResize resize = StencilResize.Reflow,
        StencilSlice? slice = null)
        => new()
        {
            Type = "test:Gallery",
            DisplayName = "Gallery",
            Category = "Tests",
            DefaultSize = new StencilSize(120, 60),
            Resize = resize,
            Slice = slice,
            Render = render
        };

    private static WireframeElement Element(double w, double h)
        => new() { Type = "test:Gallery", W = w, H = h };

    private static RenderNode StackNode()
        => new()
        {
            Kind = RenderNodeKind.Stack,
            Attributes = Attrs(("gap", 8), ("padding", 0)),
            Children =
            [
                Rect("#dbeafe", 120, 20),
                Rect("#bfdbfe", 120, 20),
                Rect("#93c5fd", 120, 20)
            ]
        };

    private static RenderNode RowNode()
        => new()
        {
            Kind = RenderNodeKind.Row,
            Attributes = Attrs(("direction", "row"), ("gap", 8), ("padding", 0)),
            Children =
            [
                Rect("#dcfce7", 40, 40),
                Rect("#bbf7d0", 40, 40),
                Rect("#86efac", 40, 40)
            ]
        };

    private static RenderNode GridNode()
        => new()
        {
            Kind = RenderNodeKind.Grid,
            Attributes = Attrs(("columns", 3), ("gap", 6), ("padding", 0)),
            Children =
            [
                Rect("#fef3c7", 30, 24),
                Rect("#fde68a", 30, 24),
                Rect("#fcd34d", 30, 24),
                Rect("#fbbf24", 30, 24),
                Rect("#f59e0b", 30, 24),
                Rect("#d97706", 30, 24)
            ]
        };

    private static RenderNode RepeatNode()
        => new()
        {
            Kind = RenderNodeKind.Repeat,
            Attributes = Attrs(("count", 9), ("max", 5), ("direction", "row"), ("gap", 5)),
            Node = Rect("#e9d5ff", 22, 22)
        };

    private static RenderNode NineSliceNode()
        => Rect("#f3f4f6", 120, 60);

    private static RenderNode RightAnchorNode()
        => new()
        {
            Kind = RenderNodeKind.Group,
            Children =
            [
                Rect("#e5e7eb", 230, 80),
                Node(RenderNodeKind.Rect, ("w", 46), ("h", 28), ("anchor", "right"), ("margin.right", 10), ("y", 26), ("fill", "#3b82f6"))
            ]
        };

    private static RenderNode Rect(string fill, double w, double h)
        => Node(RenderNodeKind.Rect, ("w", w), ("h", h), ("fill", fill), ("stroke", "#94a3b8"));

    private static RenderNode Node(RenderNodeKind kind, params (string Key, object? Value)[] attributes)
        => new()
        {
            Kind = kind,
            Attributes = Attrs(attributes)
        };

    private static Dictionary<string, object?> Attrs(params (string Key, object? Value)[] attributes)
        => attributes.ToDictionary(x => x.Key, x => x.Value);

    private static StencilSlice Slice()
        => new() { Left = 16, Top = 16, Right = 16, Bottom = 16 };

    private static string BaselinePath(string area, string state)
    {
        var dir = BaselineOutput.DirectoryFor(null, area);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{state}.png");
    }

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
