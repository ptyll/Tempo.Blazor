using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.E2E;

[TestClass]
[TestCategory("Wireframe")]
public class StencilComplexPhase12E2ETests : WasmTestBase
{
    [TestMethod]
    public async Task Complex_GalleryScreenshot()
    {
        var svgMarkup = await RenderGallerySvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().Contain("Revenue chart");
        svgMarkup.Should().Contain("Toolbox");
        svgMarkup.Should().Contain("AI Prompt");
        svgMarkup.Should().Contain("Documents");
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

        var path = BaselinePath("stencil-complex", "gallery");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderGallerySvgAsync()
    {
        var registry = Registry();
        var page = new WireframePage { Id = "complex", Name = "Complex", Width = 1600, Height = 1800 };

        var samples = Samples();
        var x = 24.0;
        var y = 72.0;
        var currentRowHeight = 0.0;
        const double colW = 760;
        const double minRowH = 120;
        for (var i = 0; i < samples.Length; i++)
        {
            var sample = samples[i];
            if (i > 0 && i % 2 == 0)
            {
                x = 24;
                y += Math.Max(minRowH, currentRowHeight) + 28;
                currentRowHeight = 0;
            }

            var def = registry.GetDef(sample.Type)!;
            var element = new WireframeElement
            {
                Id = "sample" + i,
                Type = sample.Type,
                X = x,
                Y = y,
                W = Math.Min(def.DefaultWidth, colW - 36),
                H = def.DefaultHeight
            };
            foreach (var (key, value) in sample.Props)
                element.SetProp(key, value);

            page.Elements.Add(element);
            currentRowHeight = Math.Max(currentRowHeight, element.H);
            x += colW;
        }
        page.Height = Math.Max(page.Height, y + Math.Max(minRowH, currentRowHeight) + 56);

        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
                builder.AddAttribute(2, "viewBox", $"0 0 1600 {page.Height}");
                builder.AddAttribute(3, "width", "1600");
                builder.AddAttribute(4, "height", WireframeSvg.F(page.Height));
                builder.AddMarkupContent(5, $"<rect x='0' y='0' width='1600' height='{WireframeSvg.F(page.Height)}' fill='#f8fafc'></rect>");
                builder.AddMarkupContent(6, WireframeSvg.Text("Phase 12 - Complex and native stencil pack", 24, 34, 18, WireframeSvg.ColorText, fontWeight: "600"));
                WireframePageSvg.BuildFragment(page, registry).Invoke(builder);
                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private static Sample[] Samples()
        =>
        [
            S("TmChart", ("title", "Revenue chart"), ("type", "line")),
            S("TmSparkline", ("type", "line"), ("color", "#16a34a")),
            S("TmGauge", ("value", 72), ("label", "Health")),
            S("TmStockChart", ("title", "ACME"), ("period", "3M")),
            S("TmWorkflowToolbox"),
            S("TmWorkflowPropertiesPanel", ("title", "Step properties"), ("nodeType", "Approval")),
            S("TmWorkflowMinimap"),
            S("TmWorkflowDesignerCanvas", ("title", "Approval flow")),
            S("TmStepper", ("activeStep", 2)),
            S("TmTimeline"),
            S("TmScheduler", ("title", "Launch schedule")),
            S("TmDashboard"),
            S("TmImageGallery"),
            S("TmActivityLog", ("itemCount", 5)),
            S("TmActivityTimeline"),
            S("TmTreeView"),
            S("TmAIPrompt", ("placeholder", "Summarize this project")),
            S("TmShareLinkPanel"),
            S("TmKanbanBoard"),
            S("TmGantt"),
            S("TmSpreadsheet"),
            S("TmPivotTable"),
            S("TmDiagramEditor", ("title", "Service map")),
            S("TmDocumentEditor", ("title", "Proposal")),
            S("TmNotionEditor", ("title", "Workspace")),
            S("TmChat"),
            S("TmGanttPortfolio"),
            S("TmTreeList"),
            S("TmModelingEditor", ("title", "Domain model")),
            S("TmFileManager", ("path", "/Documents")),
            S("TmDocumentManager"),
            S("TmNotionPage", ("title", "Project brief"))
        ];

    private static WireframeComponentRegistry Registry()
    {
        var registry = new WireframeComponentRegistry();
        registry.RegisterProvider(new BuiltInStencilPackProvider());
        return registry;
    }

    private static Sample S(string type, params (string Key, object? Value)[] props)
        => new(type, props);

    private static string BaselinePath(string area, string state)
    {
        var dir = BaselineOutput.DirectoryFor(null, area);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{state}.png");
    }

    private readonly record struct Sample(string Type, (string Key, object? Value)[] Props);

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
