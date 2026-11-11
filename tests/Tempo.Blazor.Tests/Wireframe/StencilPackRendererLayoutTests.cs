using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Tests.Wireframe;

public class StencilPackRendererLayoutTests
{
    // ── Structural frame assertions (FZE-1) ────────────────────────────────
    //
    // Container nodes (group / repeat / part / component / layout / anchor / placeholder) MUST be
    // emitted as real Blazor <g> elements so the browser nests them: an unclosed markup <g> frame
    // auto-closes per-frame and the children become siblings, collapsing every transformed sketch
    // in the editor, the toolbox preview and the client export. These tests walk the render frames
    // directly — the server-side string renderer concatenates frames, so it cannot see the defect.

    [Fact]
    public void Group_EmitsGElement_WithTransformAndOpacity_WrappingChildren()
    {
        var group = new RenderNode
        {
            Kind = RenderNodeKind.Group,
            Attributes = Attrs(("x", 10), ("y", 20), ("opacity", 0.5)),
            Children = [Node(RenderNodeKind.Rect, ("w", 20), ("h", 10), ("fill", "#22c55e"))]
        };

        var frames = RenderFrameAssertions.RenderFrames(Component(group), Element());

        var g = RenderFrameAssertions.SingleGElement(frames);
        RenderFrameAssertions.AttributesOf(frames, g).Should().Contain("transform", "translate(10,20)")
            .And.Contain("opacity", "0.5");
        RenderFrameAssertions.SubtreeContainsMarkup(frames, g, "fill='#22c55e'").Should().BeTrue(
            "the rect markup must be INSIDE the g subtree — today it lands as a sibling");
    }

    [Fact]
    public void Repeat_Row_EmitsOneGElementPerItem_WithTranslateOffsets()
    {
        var repeat = new RenderNode
        {
            Kind = RenderNodeKind.Repeat,
            Attributes = Attrs(("count", 3), ("direction", "row"), ("gap", 5)),
            Node = Node(RenderNodeKind.Rect, ("w", 20), ("h", 10))
        };

        var frames = RenderFrameAssertions.RenderFrames(Component(repeat), Element());

        var groups = RenderFrameAssertions.GElementIndexes(frames);
        groups.Should().HaveCount(3, "each repeat item must be nested in its own <g>");
        RenderFrameAssertions.AttributesOf(frames, groups[0]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(0,0)");
        RenderFrameAssertions.AttributesOf(frames, groups[1]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(25,0)");
        RenderFrameAssertions.AttributesOf(frames, groups[2]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(50,0)");
    }

    [Fact]
    public void Repeat_Columns_EmitsOneGElementPerCell()
    {
        var repeat = new RenderNode
        {
            Kind = RenderNodeKind.Repeat,
            Attributes = Attrs(("count", 4), ("columns", 2), ("gap", 6)),
            Node = Node(RenderNodeKind.Rect, ("w", 20), ("h", 10))
        };

        var frames = RenderFrameAssertions.RenderFrames(Component(repeat), Element());

        var groups = RenderFrameAssertions.GElementIndexes(frames);
        groups.Should().HaveCount(4);
        RenderFrameAssertions.AttributesOf(frames, groups[0]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(0,0)");
        RenderFrameAssertions.AttributesOf(frames, groups[1]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(26,0)");
        RenderFrameAssertions.AttributesOf(frames, groups[2]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(0,16)");
        RenderFrameAssertions.AttributesOf(frames, groups[3]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(26,16)");
    }

    [Fact]
    public void Stack_AndAnchoredChildren_EmitGElement_WithLayoutTransforms()
    {
        var stack = new RenderNode
        {
            Kind = RenderNodeKind.Stack,
            Attributes = Attrs(("gap", 8), ("padding", 0)),
            Children =
            [
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 20)),
                Node(RenderNodeKind.Rect, ("w", 40), ("h", 20), ("anchor", "right"), ("margin.right", 10))
            ]
        };

        var frames = RenderFrameAssertions.RenderFrames(Component(stack), Element(w: 200, h: 80));

        var groups = RenderFrameAssertions.GElementIndexes(frames);
        groups.Should().HaveCount(2, "layout containers position each child inside its own <g>");
        RenderFrameAssertions.AttributesOf(frames, groups[0]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(0,0)");
        RenderFrameAssertions.AttributesOf(frames, groups[1]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(150,0)");
    }

    [Fact]
    public void ComponentReference_EmitsGElement_WrappingResolvedChild()
    {
        var card = Component(
            "tempo:Card",
            new RenderNode
            {
                Kind = RenderNodeKind.Group,
                Children =
                [
                    Node(RenderNodeKind.Rect, ("w", "{size.w}"), ("h", "{size.h}")),
                    new RenderNode
                    {
                        Kind = RenderNodeKind.Component,
                        Attributes = Attrs(("ref", "tempo:Badge"), ("x", 12), ("y", 8)),
                        Props = new Dictionary<string, object?> { ["label"] = "Paid" }
                    }
                ]
            },
            200,
            80);
        var badge = Component(
            "tempo:Badge",
            Node(RenderNodeKind.Text, ("content", "{label}"), ("x", 0), ("y", 10)),
            60,
            20);
        var (registry, pack, packMap) = RegistryFor(card, badge);

        var frames = RenderFrameAssertions.RenderFrames(
            card,
            Element("tempo:Card", 200, 80),
            new StencilCompositionScope(registry, scope: null, pack, packMap));

        // The card's own root group is a <g> too after the fix — locate the component wrapper by
        // its transform and require the child render inside its subtree.
        var componentGroup = RenderFrameAssertions.FindGWithTransform(frames, "translate(12,8)");
        componentGroup.Should().BeGreaterThan(-1, "a component reference must wrap the resolved child in a <g>");
        RenderFrameAssertions.SubtreeContainsMarkup(frames, componentGroup, ">Paid<").Should().BeTrue(
            "the child render must be INSIDE the component <g>");
    }

    [Fact]
    public void MissingComponent_EmitsGElement_WrappingPlaceholder()
    {
        var card = Component(
            "tempo:Card",
            Node(RenderNodeKind.Component, ("ref", "tempo:DoesNotExist"), ("x", 4), ("y", 6)),
            200,
            80);
        var (registry, pack, packMap) = RegistryFor(card);

        var frames = RenderFrameAssertions.RenderFrames(
            card,
            Element("tempo:Card", 200, 80),
            new StencilCompositionScope(registry, scope: null, pack, packMap));

        var groups = RenderFrameAssertions.GElementIndexes(frames);
        groups.Should().ContainSingle("the placeholder must stay inside its positioning <g>");
        RenderFrameAssertions.AttributesOf(frames, groups[0]).Should().ContainKey("transform").WhoseValue.Should().Be("translate(4,6)");
        RenderFrameAssertions.SubtreeContainsMarkup(frames, groups[0], "Missing component").Should().BeTrue();
    }

    [Fact]
    public void RecursiveComponent_EmitsGElement_WrappingRecursionPlaceholder()
    {
        var self = Component(
            "tempo:Self",
            Node(RenderNodeKind.Component, ("ref", "tempo:Self")),
            120,
            36);
        var (registry, pack, packMap) = RegistryFor(self);

        var frames = RenderFrameAssertions.RenderFrames(
            self,
            Element("tempo:Self", 120, 36),
            new StencilCompositionScope(registry, scope: null, pack, packMap));

        var groups = RenderFrameAssertions.GElementIndexes(frames);
        groups.Should().ContainSingle("the recursion guard placeholder must stay inside its <g>");
        RenderFrameAssertions.SubtreeContainsMarkup(frames, groups[0], "Recursive").Should().BeTrue();
    }

    [Fact]
    public void PartReference_EmitsGElement_WrappingPartContent()
    {
        var part = Node(RenderNodeKind.Text, ("content", "{status}"), ("x", 16), ("y", 28), ("fontSize", 12));
        var card = Component(
            "tempo:Card",
            new RenderNode
            {
                Kind = RenderNodeKind.Group,
                Children =
                [
                    Node(RenderNodeKind.Rect, ("w", "{size.w}"), ("h", "{size.h}")),
                    Node(RenderNodeKind.Part, ("name", "statusLine"), ("x", 3), ("y", 5))
                ]
            },
            180,
            80);
        var (registry, pack, packMap) = RegistryFor(
            new Dictionary<string, RenderNode> { ["statusLine"] = part },
            card);

        var frames = RenderFrameAssertions.RenderFrames(
            card,
            Element("tempo:Card", 180, 80, ("status", "Draft")),
            new StencilCompositionScope(registry, scope: null, pack, packMap));

        // The card's own root group is a <g> too after the fix — locate the part wrapper by its
        // transform and require the inlined part content inside its subtree.
        var partGroup = RenderFrameAssertions.FindGWithTransform(frames, "translate(3,5)");
        partGroup.Should().BeGreaterThan(-1, "a part reference must wrap the inlined part in a <g>");
        RenderFrameAssertions.SubtreeContainsMarkup(frames, partGroup, ">Draft<").Should().BeTrue(
            "the inlined part content must be INSIDE the part <g>");
    }

    [Fact]
    public void Group_EmitsGElement_AndSkipsWhenFalseChild()
    {
        // Frame-level guard: the group is a real <g> element and the false branch emits nothing.
        var group = new RenderNode
        {
            Kind = RenderNodeKind.Group,
            Attributes = Attrs(("x", 10), ("y", 20)),
            Children =
            [
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 10), ("fill", "#22c55e")),
                new RenderNode
                {
                    Kind = RenderNodeKind.Rect,
                    When = "{false}",
                    Attributes = Attrs(("w", 20), ("h", 10), ("fill", "#ef4444"))
                }
            ]
        };

        var frames = RenderFrameAssertions.RenderFrames(Component(group), Element());

        var g = RenderFrameAssertions.SingleGElement(frames);
        RenderFrameAssertions.SubtreeContainsMarkup(frames, g, "#ef4444").Should().BeFalse();
        RenderFrameAssertions.SubtreeContainsMarkup(frames, g, "#22c55e").Should().BeTrue();
    }

    [Fact]
    public async Task Group_AppliesTransformAndOpacity_AndSkipsWhenFalseChild()
    {
        var group = new RenderNode
        {
            Kind = RenderNodeKind.Group,
            Attributes = Attrs(("x", 10), ("y", 20), ("opacity", 0.5)),
            Children =
            [
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 10), ("fill", "#22c55e")),
                new RenderNode
                {
                    Kind = RenderNodeKind.Rect,
                    When = "{false}",
                    Attributes = Attrs(("w", 20), ("h", 10), ("fill", "#ef4444"))
                }
            ]
        };

        var svg = await RenderAsync(Component(group), Element());

        svg.Should().MatchRegex(@"<g transform=['""]translate\(10,20\)['""] opacity=['""]0\.5['""]>");
        svg.Should().Contain("fill='#22c55e'");
        svg.Should().NotContain("#ef4444");
        svg.Should().NotContainEquivalentOf("<script");
        svg.Should().NotContainEquivalentOf("<foreignObject");
    }

    [Fact]
    public async Task Stack_RendersChildrenAtGapOffsets()
    {
        var stack = new RenderNode
        {
            Kind = RenderNodeKind.Stack,
            Attributes = Attrs(("gap", 8), ("padding", 0)),
            Children =
            [
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 20)),
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 20)),
                Node(RenderNodeKind.Rect, ("w", 20), ("h", 20))
            ]
        };

        var svg = await RenderAsync(Component(stack), Element());

        svg.Should().Contain("translate(0,0)");
        svg.Should().Contain("translate(0,28)");
        svg.Should().Contain("translate(0,56)");
    }

    [Fact]
    public async Task Repeat_CapEnforced_LogsDropped()
    {
        var logger = new CaptureLogger();
        var repeat = new RenderNode
        {
            Kind = RenderNodeKind.Repeat,
            Attributes = Attrs(("count", 100), ("max", 10), ("as", "i")),
            Node = Node(RenderNodeKind.Text, ("content", "{i}"), ("x", 0), ("y", 10))
        };

        var svg = await RenderAsync(Component(repeat), Element(), logger);

        Regex.Matches(svg, "<text").Count.Should().Be(10);
        logger.Messages.Should().Contain(x => x.Contains("90", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resize_9slice_DoesNotDistortCorners()
    {
        var component = new StencilComponent
        {
            Type = "test:Layout",
            DisplayName = "Layout",
            Category = "Tests",
            DefaultSize = new StencilSize(120, 36),
            Render = Node(RenderNodeKind.Rect, ("fill", "#f3f4f6"), ("stroke", "#d1d5db")),
            Resize = StencilResize.NineSlice,
            Slice = new StencilSlice { Left = 16, Top = 16, Right = 16, Bottom = 16 }
        };

        var svg = await RenderAsync(component, Element(w: 400, h: 80));

        Regex.Matches(svg, "width='16' height='16'").Count.Should().Be(4);
        svg.Should().Contain("width='368' height='16'");
    }

    [Fact]
    public async Task RightAnchoredNode_PinsToElementWidth()
    {
        var group = new RenderNode
        {
            Kind = RenderNodeKind.Group,
            Children =
            [
                Node(RenderNodeKind.Rect, ("w", 40), ("h", 20), ("anchor", "right"), ("margin.right", 10))
            ]
        };

        var svg = await RenderAsync(Component(group), Element(w: 200, h: 80));

        svg.Should().Contain("translate(150,0)");
    }

    [Fact]
    public async Task Render_IsDeterministic()
    {
        var grid = new RenderNode
        {
            Kind = RenderNodeKind.Grid,
            Attributes = Attrs(("columns", 2), ("gap", 4), ("padding", 2)),
            Children =
            [
                Node(RenderNodeKind.Rect, ("h", 12)),
                Node(RenderNodeKind.Text, ("content", "A"), ("h", 12), ("y", 6))
            ]
        };
        var component = Component(grid);
        var element = Element(w: 100, h: 60);

        var first = await RenderAsync(component, element);
        var second = await RenderAsync(component, element);

        second.Should().Be(first);
    }

    private static RenderNode Node(RenderNodeKind kind, params (string Key, object? Value)[] attributes)
        => new()
        {
            Kind = kind,
            Attributes = Attrs(attributes)
        };

    private static Dictionary<string, object?> Attrs(params (string Key, object? Value)[] attributes)
        => attributes.ToDictionary(x => x.Key, x => x.Value);

    // ── Frame-walking helpers live in RenderFrameAssertions (shared with other wireframe specs) ──

    private static (WireframeComponentRegistry Registry, StencilPack Pack, Dictionary<string, StencilPack> PackMap)
        RegistryFor(params StencilComponent[] components)
        => RegistryFor(parts: null, components);

    private static (WireframeComponentRegistry Registry, StencilPack Pack, Dictionary<string, StencilPack> PackMap)
        RegistryFor(IReadOnlyDictionary<string, RenderNode>? parts, params StencilComponent[] components)
    {
        var registry = new WireframeComponentRegistry();
        var pack = new StencilPack
        {
            Format = "tempo-stencil",
            FormatVersion = 1,
            Id = "tempo-test",
            Namespace = "tempo",
            Target = new StencilTarget { Framework = "Blazor", Library = "Tempo.Blazor", Version = "1.0.0" },
            Parts = parts ?? new Dictionary<string, RenderNode>(),
            Components = [.. components]
        };
        var packMap = new Dictionary<string, StencilPack>(StringComparer.Ordinal) { [pack.Namespace] = pack };

        foreach (var component in components)
        {
            var capturedComponent = component;
            registry.RegisterDefinition(new WireframeComponentDef
            {
                Type = capturedComponent.Type,
                DisplayName = capturedComponent.DisplayName,
                Category = capturedComponent.Category,
                DefaultWidth = capturedComponent.DefaultSize.Width,
                DefaultHeight = capturedComponent.DefaultSize.Height,
                IsBuiltIn = true,
                RenderSvg = (element, builder) => StencilPackRenderer.Render(
                    capturedComponent,
                    element,
                    StencilTokenScope.Empty,
                    builder,
                    new StencilCompositionScope(registry, scope: null, pack, packMap),
                    logger: null)
            });
        }

        return (registry, pack, packMap);
    }

    private static StencilComponent Component(RenderNode render)
        => Component("test:Layout", render, 120, 36);

    private static StencilComponent Component(string type, RenderNode render, double width, double height)
        => new()
        {
            Type = type,
            DisplayName = type,
            Category = "Tests",
            DefaultSize = new StencilSize(width, height),
            Render = render
        };

    private static WireframeElement Element(double w = 120, double h = 36, params (string Key, object? Value)[] props)
        => Element("test:Layout", w, h, props);

    private static WireframeElement Element(string type, double w, double h, params (string Key, object? Value)[] props)
    {
        var element = new WireframeElement { Type = type, W = w, H = h };
        foreach (var (key, value) in props)
            element.Props[key] = JsonSerializer.SerializeToElement(value);
        return element;
    }

    private static async Task<string> RenderAsync(
        StencilComponent component,
        WireframeElement element,
        ILogger? logger = null)
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
                StencilPackRenderer.Render(component, element, StencilTokenScope.Empty, builder, logger);
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

    private sealed class CaptureLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
}
