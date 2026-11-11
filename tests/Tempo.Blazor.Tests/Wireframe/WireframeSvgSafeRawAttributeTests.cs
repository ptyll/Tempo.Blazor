using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// FZE-2 specs for <see cref="WireframeSvg.SafeRawAttribute"/> (values handed to AddAttribute by
/// OpenGroup) and for the region-based sequence numbering that replaced the static counter:
/// repeated renders must produce identical frames so Blazor diffs instead of rebuilding the DOM.
/// </summary>
public class WireframeSvgSafeRawAttributeTests
{
    [Theory]
    [InlineData("translate(10,20)", "translate(10,20)")]
    [InlineData("translate(0,0) scale(2)", "translate(0,0) scale(2)")]
    [InlineData("a&b", "a&b")]
    [InlineData("x<y", "fallback")]
    [InlineData("x>y", "fallback")]
    public void SafeRawAttribute_ReturnsRawValues(string value, string expected)
        => WireframeSvg.SafeRawAttribute(value, "fallback").Should().Be(expected);

    [Theory]
    [InlineData("translate(0,0)\" onload=\"alert(1)")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("scale(2) onerror=alert(1)")]
    public void SafeRawAttribute_DangerousValues_FallBack(string value)
        => WireframeSvg.SafeRawAttribute(value, "fallback").Should().Be("fallback");

    [Fact]
    public void OpenGroup_PassesTransformAndOpacityRaw_ToAddAttribute()
    {
        var builder = new RenderTreeBuilder();
        WireframeSvg.OpenGroup(builder, "translate(1,2)&", "0.5");
        WireframeSvg.Markup(builder, "<rect width='10' height='10'></rect>");
        WireframeSvg.CloseGroup(builder);

        var frames = builder.GetFrames();
        string? transform = null;
        string? opacity = null;
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "transform")
                transform = frame.AttributeValue as string;
            if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "opacity")
                opacity = frame.AttributeValue as string;
        }

        transform.Should().Be("translate(1,2)&");
        opacity.Should().Be("0.5");
    }

    [Fact]
    public void RepeatedRenders_ProduceIdenticalFrames_NoStaticSequenceCounter()
    {
        var group = new RenderNode
        {
            Kind = RenderNodeKind.Group,
            Attributes = new Dictionary<string, object?> { ["x"] = 4, ["y"] = 6 },
            Children =
            [
                new RenderNode
                {
                    Kind = RenderNodeKind.Rect,
                    Attributes = new Dictionary<string, object?> { ["w"] = 20, ["h"] = 10 }
                }
            ]
        };
        var component = new StencilComponent
        {
            Type = "test:Stable",
            DisplayName = "Stable",
            Category = "Tests",
            DefaultSize = new StencilSize(120, 36),
            Render = group
        };
        var element = new WireframeElement { Type = "test:Stable", W = 120, H = 36 };

        var first = DescribeFrames(component, element);
        var second = DescribeFrames(component, element);

        second.Should().Equal(first);
    }

    private static List<string> DescribeFrames(StencilComponent component, WireframeElement element)
    {
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "svg");
        StencilPackRenderer.Render(component, element, StencilTokenScope.Empty, builder, logger: null);
        builder.CloseElement();

        var frames = builder.GetFrames();
        var description = new List<string>(frames.Count);
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            description.Add(frame.FrameType switch
            {
                RenderTreeFrameType.Element => $"element:{frame.ElementName}:{frame.ElementSubtreeLength}",
                RenderTreeFrameType.Attribute => $"attribute:{frame.AttributeName}:{frame.AttributeValue}",
                RenderTreeFrameType.Markup => $"markup:{frame.MarkupContent}",
                _ => frame.FrameType.ToString()
            });
        }

        return description;
    }
}
