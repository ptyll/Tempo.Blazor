using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;
using Tempo.Blazor.Tests.Icons;

namespace Tempo.Blazor.Tests.Wireframe;

[Collection(IconRegistryCollection.Name)]
public class StencilPackRendererTests : IDisposable
{
    public void Dispose()
    {
        IconRegistry.Reset();
    }

    [Fact]
    public async Task Text_RendersBoundContentFromProp()
    {
        var svg = await RenderAsync(
            Node(RenderNodeKind.Text, ("content", "{label}"), ("x", 4), ("y", 12), ("fontSize", 12)),
            Element(("label", "Ulozit")));

        svg.Should().Contain(">Ulozit<");
    }

    [Fact]
    public async Task Text_Ellipsis_TruncatesLongContentWithEllipsis()
    {
        const string label = "A very long label that should not fit";

        var svg = await RenderAsync(
            Node(
                RenderNodeKind.Text,
                ("content", "{label}"),
                ("x", 0),
                ("y", 12),
                ("w", 36),
                ("fontSize", 10),
                ("ellipsis", true)),
            Element(("label", label)));

        svg.Should().Contain("…");
        svg.Should().NotContain(label);
    }

    [Fact]
    public async Task Text_TransformUppercase_UppercasesContent()
    {
        var svg = await RenderAsync(
            Node(RenderNodeKind.Text, ("content", "{label}"), ("transform", "uppercase"), ("x", 4), ("y", 12)),
            Element(("label", "ulozit")));

        svg.Should().Contain("ULOZIT");
    }

    [Fact]
    public async Task Rect_FillFromMapVariant()
    {
        const string fill = "$map{variant, primary: #3b82f6, default: #f3f4f6}";
        var node = Node(RenderNodeKind.Rect, ("x", 0), ("y", 0), ("w", 80), ("h", 28), ("fill", fill));

        var primary = await RenderAsync(node, Element(("variant", "primary")));
        var fallback = await RenderAsync(node, Element());

        primary.Should().Contain("fill='#3b82f6'");
        fallback.Should().Contain("fill='#f3f4f6'");
    }

    [Fact]
    public async Task Icon_EmitsPathFromPackAtlas()
    {
        var tokens = new StencilTokenScope(packIcons: new Dictionary<string, string>
        {
            ["star"] = "M1 1 L2 2"
        });

        var svg = await RenderAsync(
            Node(RenderNodeKind.Icon, ("name", "star"), ("x", 0), ("y", 0), ("size", 24)),
            Element(),
            tokens);

        svg.Should().Contain("M1 1 L2 2");
    }

    [Fact]
    public async Task Icon_FallsBackToIconRegistry()
    {
        IconRegistry.Register("registry-star", "<path d=\"M3 3 L4 4\"></path>");

        var svg = await RenderAsync(
            Node(RenderNodeKind.Icon, ("name", "registry-star"), ("x", 0), ("y", 0), ("size", 24)),
            Element());

        svg.Should().Contain("M3 3 L4 4");
    }

    [Fact]
    public async Task Line_And_Path_Render()
    {
        var lineSvg = await RenderAsync(
            Node(RenderNodeKind.Line, ("x1", 0), ("y1", 8), ("x2", "{size.w}"), ("y2", 8), ("stroke", "#111827")),
            SizedElement(w: 64, h: 24));

        var pathSvg = await RenderAsync(
            Node(RenderNodeKind.Path, ("d", "{pathD}"), ("fill", "none"), ("stroke", "#111827")),
            Element(("pathD", "M0 0 L10 10")));

        lineSvg.Should().Contain("<line");
        pathSvg.Should().Contain("<path");
        pathSvg.Should().Contain("d='M0 0 L10 10'");
    }

    [Fact]
    public async Task Spinner_Renders()
    {
        var svg = await RenderAsync(Node(RenderNodeKind.Spinner, ("size", 16)), SizedElement(w: 32, h: 32));

        svg.Should().Contain("<circle");
        svg.Should().Contain("stroke='#3b82f6'");
    }

    [Fact]
    public async Task Image_DataUrl_Renders()
    {
        var svg = await RenderAsync(
            Node(RenderNodeKind.Image, ("src", "data:image/png;base64,AAAA"), ("x", 0), ("y", 0), ("w", 20), ("h", 20)),
            Element());

        svg.Should().Contain("<image");
        svg.Should().Contain("data:image/png;base64,AAAA");
    }

    [Fact]
    public async Task Image_NonDataUrl_RendersPlaceholder()
    {
        var svg = await RenderAsync(
            Node(RenderNodeKind.Image, ("src", "https://example.test/image.png"), ("x", 0), ("y", 0), ("w", 20), ("h", 20)),
            Element());

        svg.Should().NotContain("<image");
        svg.Should().Contain("<rect");
    }

    [Fact]
    public async Task Image_DataTextHtml_RendersPlaceholder()
    {
        var svg = await RenderAsync(
            Node(RenderNodeKind.Image, ("src", "data:text/html,<script>alert(1)</script>"), ("x", 0), ("y", 0), ("w", 20), ("h", 20)),
            Element());

        svg.Should().NotContain("<image");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
    }

    [Fact]
    public async Task RawSvg_IsSanitized()
    {
        const string raw = """
            <script>alert(1)</script>
            <rect onerror="alert(1)" fill="url(javascript:alert(1))"></rect>
            <foreignObject><div>bad</div></foreignObject>
            """;

        var svg = await RenderAsync(Node(RenderNodeKind.Svg, ("content", raw)), Element());

        svg.Should().Contain("<rect");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Theory]
    [InlineData("<img src=x/onerror=alert(1)>", "onerror=")]
    [InlineData("<svg/onload=alert(1)>", "onload=")]
    [InlineData("<scr<script>ipt>alert(1)", "<script")]
    [InlineData("<foreign<foreignObject>Object>x", "<foreignObject")]
    [InlineData("<ScRiPt>alert(1)</ScRiPt><rect></rect>", "<script")]
    [InlineData("<rect fill=\"java&#x73;cript:alert(1)\"></rect>", "javascript:")]
    public async Task RawSvg_AdversarialInputs_AreSanitized(string raw, string forbidden)
    {
        var svg = await RenderAsync(Node(RenderNodeKind.Svg, ("content", raw)), Element());

        svg.Should().NotContainEquivalentOf(forbidden);
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("onload=");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Fact]
    public async Task IconRegistryContent_IsSanitized()
    {
        IconRegistry.Register(
            "unsafe-registry",
            "<path d=\"M0 0 L1 1\"></path><rect onerror=\"alert(1)\"></rect><script>alert(1)</script>");

        var svg = await RenderAsync(
            Node(RenderNodeKind.Icon, ("name", "unsafe-registry"), ("x", 0), ("y", 0), ("size", 24)),
            Element());

        svg.Should().Contain("M0 0 L1 1");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("onerror=");
        svg.Should().NotContainEquivalentOf("javascript:");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Fact]
    public async Task ContainerNodes_AreSkipped_NoThrow()
    {
        var spec = Component(new RenderNode
        {
            Kind = RenderNodeKind.Stack,
            Children =
            [
                new RenderNode
                {
                    Kind = RenderNodeKind.Repeat,
                    Node = Node(RenderNodeKind.Text, ("content", "ShouldNotRender"))
                }
            ]
        });

        var act = async () => await RenderAsync(spec, Element());

        var svg = await act.Should().NotThrowAsync();
        svg.Subject.Should().NotContain("ShouldNotRender");
    }

    [Fact]
    public async Task Render_IsDeterministic_ForSameInput()
    {
        var node = Node(RenderNodeKind.Text, ("content", "{label}"), ("x", 4), ("y", 12));
        var element = Element(("label", "Save"));

        var first = await RenderAsync(node, element);
        var second = await RenderAsync(node, element);

        second.Should().Be(first);
    }

    private static RenderNode Node(RenderNodeKind kind, params (string Key, object? Value)[] attributes)
        => new()
        {
            Kind = kind,
            Attributes = attributes.ToDictionary(x => x.Key, x => x.Value)
        };

    private static StencilComponent Component(RenderNode render)
        => new()
        {
            Type = "test:Component",
            DisplayName = "Test Component",
            Category = "Tests",
            DefaultSize = new StencilSize(120, 36),
            Render = render
        };

    private static WireframeElement Element(params (string Key, object? Value)[] props)
        => SizedElement(120, 36, props);

    private static WireframeElement SizedElement(double w = 120, double h = 36, params (string Key, object? Value)[] props)
    {
        var element = new WireframeElement { Type = "test:Component", W = w, H = h };
        foreach (var (key, value) in props)
            element.Props[key] = JsonSerializer.SerializeToElement(value);
        return element;
    }

    private static Task<string> RenderAsync(
        RenderNode node,
        WireframeElement element,
        StencilTokenScope? tokens = null)
        => RenderAsync(Component(node), element, tokens);

    private static async Task<string> RenderAsync(
        StencilComponent component,
        WireframeElement element,
        StencilTokenScope? tokens = null)
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
                builder.AddAttribute(2, "viewBox", $"0 0 {element.W} {element.H}");
                StencilPackRenderer.Render(component, element, tokens ?? StencilTokenScope.Empty, builder);
                builder.CloseElement();
            };

            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?> { ["Content"] = fragment });
            var output = await htmlRenderer.RenderComponentAsync<FragmentHost>(parameters);
            return output.ToHtmlString();
        });
    }

    private sealed class FragmentHost : ComponentBase
    {
        [Parameter] public RenderFragment? Content { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
            => Content?.Invoke(builder);
    }
}
