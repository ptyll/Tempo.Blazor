using System.Text.Json;
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
public class StencilCompositionE2ETests : WasmTestBase
{
    [TestMethod]
    public async Task StencilComposition_CardWithBadgeScreenshot()
    {
        var svgMarkup = await RenderCompositionSvgAsync();
        svgMarkup.Should().StartWith("<svg");
        svgMarkup.Should().Contain(">Approved<");
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

        var path = BaselinePath("stencil-composition", "card-with-badge");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = path, Type = ScreenshotType.Png, FullPage = true });
        TestContext.AddResultFile(path);

        File.Exists(path).Should().BeTrue();
    }

    private static async Task<string> RenderCompositionSvgAsync()
    {
        var pack = Pack("tempo", Target(), CardComponent(), BadgeComponent());
        var registry = RegistryFor(pack);
        var component = pack.Components.Single(x => x.Type == "tempo:InvoiceCard");
        var element = Element("tempo:InvoiceCard", 360, 180, ("status", "Approved"), ("amount", "$4,820"));

        var services = new ServiceCollection();
        services.AddLogging();
        await using var htmlRenderer = new HtmlRenderer(services.BuildServiceProvider(), NullLoggerFactory.Instance);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            RenderFragment fragment = builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "xmlns", "http://www.w3.org/2000/svg");
                builder.AddAttribute(2, "viewBox", "0 0 520 260");
                builder.AddAttribute(3, "width", "520");
                builder.AddAttribute(4, "height", "260");
                builder.AddMarkupContent(5, "<rect x='0' y='0' width='520' height='260' fill='#f8fafc'></rect>");
                builder.AddMarkupContent(6, "<g transform='translate(80,40)'>");
                StencilPackRenderer.Render(
                    component,
                    element,
                    StencilTokenScope.Empty,
                    builder,
                    new StencilCompositionScope(registry, scope: null, pack, new Dictionary<string, StencilPack>
                    {
                        [pack.Namespace] = pack
                    }),
                    logger: null);
                builder.AddMarkupContent(7, "</g>");
                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private static StencilComponent CardComponent()
        => Component(
            "tempo:InvoiceCard",
            new RenderNode
            {
                Kind = RenderNodeKind.Group,
                Children =
                [
                    Node(RenderNodeKind.Rect, ("w", "{size.w}"), ("h", "{size.h}"), ("fill", "#ffffff"), ("stroke", "#cbd5e1"), ("rx", 10)),
                    Node(RenderNodeKind.Text, ("content", "Invoice summary"), ("x", 24), ("y", 34), ("fontSize", 16), ("fontWeight", "700")),
                    Node(RenderNodeKind.Text, ("content", "Amount"), ("x", 24), ("y", 82), ("fontSize", 11), ("fill", "#64748b")),
                    Node(RenderNodeKind.Text, ("content", "{amount}"), ("x", 24), ("y", 112), ("fontSize", 28), ("fontWeight", "700"), ("fill", "#0f172a")),
                    new RenderNode
                    {
                        Kind = RenderNodeKind.Component,
                        Attributes = Attrs(("ref", "tempo:StatusBadge"), ("x", 234), ("y", 26)),
                        Props = new Dictionary<string, object?>
                        {
                            ["label"] = "{status}"
                        }
                    },
                    Node(RenderNodeKind.Line, ("x1", 24), ("x2", 336), ("y1", 138), ("y2", 138), ("stroke", "#e2e8f0")),
                    Node(RenderNodeKind.Text, ("content", "Due Jul 15, 2026"), ("x", 24), ("y", 160), ("fontSize", 10), ("fill", "#64748b"))
                ]
            },
            360,
            180);

    private static StencilComponent BadgeComponent()
        => Component(
            "tempo:StatusBadge",
            new RenderNode
            {
                Kind = RenderNodeKind.Group,
                Children =
                [
                    Node(RenderNodeKind.Rect, ("w", "{size.w}"), ("h", "{size.h}"), ("fill", "#dcfce7"), ("stroke", "#86efac"), ("rx", 14)),
                    Node(RenderNodeKind.Text, ("content", "{label}"), ("x", 0), ("y", 0), ("w", "{size.w}"), ("h", "{size.h}"), ("align", "center"), ("fontSize", 12), ("fontWeight", "700"), ("fill", "#166534"))
                ]
            },
            102,
            28);

    private static StencilComponent Component(string type, RenderNode render, double width, double height)
        => new()
        {
            Type = type,
            DisplayName = type,
            Category = "E2E",
            DefaultSize = new StencilSize(width, height),
            Render = render
        };

    private static StencilPack Pack(string ns, StencilTarget target, params StencilComponent[] components)
        => new()
        {
            Format = "tempo-stencil",
            FormatVersion = 1,
            Id = ns + "-composition",
            Namespace = ns,
            Target = target,
            Components = components
        };

    private static StencilTarget Target()
        => new()
        {
            Framework = "Blazor",
            Library = "Tempo.Blazor",
            Version = "1.0.0"
        };

    private static WireframeElement Element(string type, double w, double h, params (string Key, object? Value)[] props)
    {
        var element = new WireframeElement { Type = type, W = w, H = h };
        foreach (var (key, value) in props)
            element.Props[key] = JsonSerializer.SerializeToElement(value);
        return element;
    }

    private static WireframeComponentRegistry RegistryFor(params StencilPack[] packs)
    {
        var registry = new WireframeComponentRegistry();
        var packMap = packs.ToDictionary(x => x.Namespace, StringComparer.Ordinal);
        foreach (var pack in packs)
        {
            foreach (var component in pack.Components)
            {
                var capturedPack = pack;
                var capturedComponent = component;
                registry.RegisterDefinition(new WireframeComponentDef
                {
                    Type = component.Type,
                    DisplayName = component.DisplayName,
                    Category = component.Category,
                    DefaultWidth = component.DefaultSize.Width,
                    DefaultHeight = component.DefaultSize.Height,
                    IsBuiltIn = component.Type.StartsWith("tempo:", StringComparison.Ordinal),
                    RenderSvg = (element, builder) => StencilPackRenderer.Render(
                        capturedComponent,
                        element,
                        StencilTokenScope.Empty,
                        builder,
                        new StencilCompositionScope(registry, scope: null, capturedPack, packMap),
                        logger: null)
                });
            }
        }

        return registry;
    }

    private static RenderNode Node(RenderNodeKind kind, params (string Key, object? Value)[] attributes)
        => new()
        {
            Kind = kind,
            Attributes = Attrs(attributes)
        };

    private static Dictionary<string, object?> Attrs(params (string Key, object? Value)[] attributes)
        => attributes.ToDictionary(x => x.Key, x => x.Value);

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
