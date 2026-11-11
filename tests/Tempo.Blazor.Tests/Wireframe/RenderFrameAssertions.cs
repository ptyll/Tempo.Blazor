using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Tempo.Blazor.Components.Wireframe;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Tests.Wireframe;

/// <summary>
/// Shared frame-walking helpers for the FZE-1 structural specs: they walk the
/// <see cref="RenderTreeFrame"/> list a <see cref="StencilPackRenderer"/> render produced so a test
/// can assert that container nodes are emitted as REAL Blazor <c>&lt;g&gt;</c> elements (with their
/// children INSIDE the subtree) — something the server-side string renderer, which only
/// concatenates frames, can never see.
/// </summary>
internal static class RenderFrameAssertions
{
    /// <summary>Builds the full frame list of <paramref name="component"/> rendered inside an
    /// <c>&lt;svg&gt;</c> element via <see cref="StencilPackRenderer"/>.</summary>
    public static List<RenderTreeFrame> RenderFrames(
        StencilComponent component,
        WireframeElement element,
        StencilCompositionScope? composition = null)
    {
        var builder = new RenderTreeBuilder();
        builder.OpenElement(0, "svg");
        StencilPackRenderer.Render(
            component,
            element,
            StencilTokenScope.Empty,
            builder,
            composition,
            logger: null);
        builder.CloseElement();

        var range = builder.GetFrames();
        var frames = new List<RenderTreeFrame>(range.Count);
        for (var i = 0; i < range.Count; i++)
            frames.Add(range.Array[i]);

        return frames;
    }

    /// <summary>Returns the indexes of every <c>&lt;g&gt;</c> element frame, in document order.</summary>
    public static List<int> GElementIndexes(List<RenderTreeFrame> frames)
    {
        var result = new List<int>();
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].FrameType == RenderTreeFrameType.Element
                && string.Equals(frames[i].ElementName, "g", StringComparison.Ordinal))
            {
                result.Add(i);
            }
        }

        return result;
    }

    /// <summary>Asserts exactly one <c>&lt;g&gt;</c> element frame exists and returns its index.</summary>
    public static int SingleGElement(List<RenderTreeFrame> frames)
    {
        var groups = GElementIndexes(frames);
        groups.Should().HaveCount(1, "exactly one <g> element frame was expected");
        return groups[0];
    }

    /// <summary>Returns the index of the <c>&lt;g&gt;</c> whose <c>transform</c> attribute equals
    /// <paramref name="transform"/>, or -1 when there is none.</summary>
    public static int FindGWithTransform(List<RenderTreeFrame> frames, string transform)
    {
        foreach (var index in GElementIndexes(frames))
        {
            if (AttributesOf(frames, index).TryGetValue("transform", out var value)
                && string.Equals(value as string, transform, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Reads the attribute frames that belong to the element at
    /// <paramref name="elementIndex"/> itself (stopping at the first nested element frame).</summary>
    public static IReadOnlyDictionary<string, object?> AttributesOf(List<RenderTreeFrame> frames, int elementIndex)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.Ordinal);
        var end = elementIndex + frames[elementIndex].ElementSubtreeLength;
        for (var i = elementIndex + 1; i < end; i++)
        {
            if (frames[i].FrameType == RenderTreeFrameType.Element)
                break; // nested element — this element's own attributes end here

            if (frames[i].FrameType == RenderTreeFrameType.Attribute)
                attributes[frames[i].AttributeName] = frames[i].AttributeValue;
        }

        return attributes;
    }

    /// <summary>True when any markup frame inside the element's subtree contains
    /// <paramref name="substring"/> — i.e. the markup renders INSIDE the element, not as a
    /// sibling of it.</summary>
    public static bool SubtreeContainsMarkup(List<RenderTreeFrame> frames, int elementIndex, string substring)
    {
        var end = elementIndex + frames[elementIndex].ElementSubtreeLength;
        for (var i = elementIndex + 1; i < end; i++)
        {
            if (frames[i].FrameType == RenderTreeFrameType.Markup
                && frames[i].MarkupContent.Contains(substring, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
