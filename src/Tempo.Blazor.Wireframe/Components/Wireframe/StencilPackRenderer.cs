using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.Logging;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Components.Wireframe.Stencil;

namespace Tempo.Blazor.Components.Wireframe;

internal static partial class StencilPackRenderer
{
    private const int MaxDepth = 16;

    private static readonly IImmutableSet<string> EmptyRefPath = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    private static readonly AsyncLocal<RenderContext?> CurrentRenderContext = new();

    public static void Render(
        StencilComponent spec,
        WireframeElement el,
        StencilTokenScope tokens,
        RenderTreeBuilder builder)
        => Render(spec, el, tokens, builder, logger: null);

    internal static void Render(
        StencilComponent spec,
        WireframeElement el,
        StencilTokenScope tokens,
        RenderTreeBuilder builder,
        ILogger? logger)
        => Render(spec, el, tokens, builder, composition: null, logger);

    internal static void Render(
        StencilComponent spec,
        WireframeElement el,
        StencilTokenScope tokens,
        RenderTreeBuilder builder,
        StencilCompositionScope? composition,
        ILogger? logger)
    {
        if (spec.Render is null)
            return;

        tokens ??= StencilTokenScope.Empty;
        var previousContext = CurrentRenderContext.Value;
        var renderContext = ResolveRenderContext(composition);
        CurrentRenderContext.Value = renderContext;

        try
        {
            var props = el.Props.ToDictionary(
                static x => x.Key,
                static x => (object?)x.Value,
                StringComparer.Ordinal);
            var context = new StencilEvalContext(props, el.W, el.H, 0, tokens.CreateResolver());
            var state = new RendererState(context, new StencilEvaluator(), tokens.PackIcons, logger, renderContext);

            if (spec.Resize == StencilResize.NineSlice && spec.Slice is not null)
            {
                RenderNineSlice(spec, state, builder);
                return;
            }

            RenderNode(spec.Render, state, builder);
        }
        finally
        {
            CurrentRenderContext.Value = previousContext;
        }
    }

    private static void RenderNode(RenderNode node, StencilEvalContext ctx, RenderTreeBuilder b)
        => RenderNode(
            node,
            new RendererState(
                ctx,
                new StencilEvaluator(),
                StencilTokenScope.Empty.PackIcons,
                null,
                ResolveRenderContext(composition: null)),
            b);

    private static void RenderNode(RenderNode? node, RendererState state, RenderTreeBuilder b)
    {
        if (node is null || !ShouldRender(node, state))
            return;

        switch (node.Kind)
        {
            case RenderNodeKind.Rect:
                RenderRect(node, state, b);
                break;
            case RenderNodeKind.Text:
                RenderText(node, state, b);
                break;
            case RenderNodeKind.Line:
                RenderLine(node, state, b);
                break;
            case RenderNodeKind.Path:
                RenderPath(node, state, b);
                break;
            case RenderNodeKind.Icon:
                RenderIcon(node, state, b);
                break;
            case RenderNodeKind.Spinner:
                RenderSpinner(node, state, b);
                break;
            case RenderNodeKind.Image:
                RenderImage(node, state, b);
                break;
            case RenderNodeKind.Svg:
                RenderRawSvg(node, state, b);
                break;
            case RenderNodeKind.Group:
                RenderGroup(node, state, b);
                break;
            case RenderNodeKind.Stack:
            case RenderNodeKind.Row:
            case RenderNodeKind.Grid:
                RenderLayoutContainer(node, state, b);
                break;
            case RenderNodeKind.Repeat:
                RenderRepeat(node, state, b);
                break;
            case RenderNodeKind.Component:
                RenderComponent(node, state, b);
                break;
            case RenderNodeKind.Part:
                RenderPart(node, state, b);
                break;
            default:
                break;
        }
    }

    private static bool ShouldRender(RenderNode node, RendererState state)
        => string.IsNullOrWhiteSpace(node.When) || state.Evaluate(node.When).AsBool();

    private static void RenderRect(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);
        var w = state.Number(node, "w", state.Context.SizeW);
        var h = state.Number(node, "h", state.Context.SizeH);
        var fill = SafeAttribute(state.Text(node, "fill", WireframeSvg.Fill), WireframeSvg.Fill);
        var stroke = SafeAttribute(state.Text(node, "stroke", WireframeSvg.Border), WireframeSvg.Border);
        var rx = state.Number(node, "rx", 4);
        var strokeWidth = state.Number(node, "strokeWidth", 1);

        var resize = state.Text(node, "resize", string.Empty);
        if ((resize.Equals("nineSlice", StringComparison.OrdinalIgnoreCase)
             || resize.Equals("9slice", StringComparison.OrdinalIgnoreCase))
            && TryReadSlice(node, out var slice))
        {
            _ = slice;
            WireframeSvg.Markup(b, WireframeSvg.Rect(x, y, w, h, fill, stroke, rx, strokeWidth));
            return;
        }

        WireframeSvg.Markup(b, WireframeSvg.Rect(x, y, w, h, fill, stroke, rx, strokeWidth));
    }

    private static void RenderText(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var content = ResolveContent(node, state);
        if (state.Text(node, "transform", string.Empty).Equals("uppercase", StringComparison.OrdinalIgnoreCase))
            content = content.ToUpperInvariant();

        var fontSize = state.Number(node, "fontSize", 11);
        var boxW = state.Number(node, "w", state.Context.SizeW);
        if (state.Bool(node, "ellipsis", false))
            content = ApplyEllipsis(content, boxW, fontSize);

        var align = state.Text(node, "align", "start");
        var valign = state.Text(node, "valign", "middle");
        var anchor = TextAnchor(align);
        var baseline = DominantBaseline(valign);
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);

        if (node.Attributes.ContainsKey("w"))
        {
            if (anchor == "middle")
                x += boxW / 2;
            else if (anchor == "end")
                x += boxW;
        }

        if (node.Attributes.ContainsKey("h"))
        {
            var boxH = state.Number(node, "h", state.Context.SizeH);
            if (baseline == "middle")
                y += boxH / 2;
            else if (baseline == "text-after-edge")
                y += boxH;
        }

        var fill = SafeAttribute(state.Text(node, "fill", WireframeSvg.ColorText), WireframeSvg.ColorText);
        var fontWeight = SafeAttribute(state.Text(node, "fontWeight", "normal"), "normal");

        WireframeSvg.Markup(b, WireframeSvg.Text(content, x, y, fontSize, fill, anchor, fontWeight, baseline));
    }

    private static void RenderLine(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var x1 = state.Number(node, "x1", state.Number(node, "x", 0));
        var y1 = state.Number(node, "y1", state.Number(node, "y", 0));
        var x2 = state.Number(node, "x2", x1 + state.Number(node, "w", state.Context.SizeW));
        var y2 = state.Number(node, "y2", y1);
        var stroke = SafeAttribute(state.Text(node, "stroke", WireframeSvg.Border), WireframeSvg.Border);

        if (NearlyEqual(y1, y2))
        {
            WireframeSvg.Markup(b, WireframeSvg.HLine(x1, x2, y1, stroke));
            return;
        }

        if (NearlyEqual(x1, x2))
        {
            WireframeSvg.Markup(b, WireframeSvg.VLine(x1, y1, y2, stroke));
            return;
        }

        WireframeSvg.Markup(b,
            $"<line x1='{WireframeSvg.F(x1)}' y1='{WireframeSvg.F(y1)}' x2='{WireframeSvg.F(x2)}' y2='{WireframeSvg.F(y2)}' stroke='{stroke}' stroke-width='1'></line>");
    }

    private static void RenderPath(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var d = SafeAttribute(state.Text(node, "d", string.Empty), string.Empty);
        if (d.Length == 0)
            return;

        var fill = SafeAttribute(state.Text(node, "fill", "none"), "none");
        var stroke = SafeAttribute(state.Text(node, "stroke", WireframeSvg.Border), WireframeSvg.Border);
        var strokeWidth = state.Number(node, "strokeWidth", 1);
        WireframeSvg.Markup(b,
            $"<path d='{d}' fill='{fill}' stroke='{stroke}' stroke-width='{WireframeSvg.F(strokeWidth)}'></path>");
    }

    private static void RenderIcon(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var name = state.Text(node, "name", ResolveContent(node, state));
        var size = state.Number(node, "size", 12);
        var x = state.Number(node, "x", Math.Max(0, (state.Context.SizeW - size) / 2));
        var y = state.Number(node, "y", Math.Max(0, (state.Context.SizeH - size) / 2));
        var cx = state.Number(node, "cx", x + size / 2);
        var cy = state.Number(node, "cy", y + size / 2);
        var fill = SafeAttribute(state.Text(node, "fill", WireframeSvg.ColorText), WireframeSvg.ColorText);

        if (state.PackIcons.TryGetValue(name, out var pathData))
        {
            var d = SafeAttribute(pathData, string.Empty);
            if (d.Length > 0)
            {
                var scale = size / 24d;
                WireframeSvg.Markup(b,
                    $"<path d='{d}' fill='{fill}' transform='translate({WireframeSvg.F(x)} {WireframeSvg.F(y)}) scale({WireframeSvg.F(scale)})'></path>");
                return;
            }
        }

        var registered = IconRegistry.Resolve(name);
        if (!string.IsNullOrWhiteSpace(registered))
        {
            var sanitized = SanitizeRawSvg(registered);
            if (sanitized.Length > 0)
            {
                var scale = size / 24d;
                WireframeSvg.Markup(b,
                    $"<g transform='translate({WireframeSvg.F(x)} {WireframeSvg.F(y)}) scale({WireframeSvg.F(scale)})' fill='{fill}'>{sanitized}</g>");
                return;
            }
        }

        WireframeSvg.Markup(b, WireframeSvg.Icon(name, cx, cy, size));
    }

    private static void RenderSpinner(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var fallbackSize = Math.Min(
            state.Context.SizeW > 0 ? state.Context.SizeW : 16,
            state.Context.SizeH > 0 ? state.Context.SizeH : 16);
        var size = state.Number(node, "size", Math.Min(16, fallbackSize));
        var cx = state.Number(node, "cx", state.Number(node, "x", state.Context.SizeW / 2));
        var cy = state.Number(node, "cy", state.Number(node, "y", state.Context.SizeH / 2));

        WireframeSvg.Markup(b, $"<g data-stencil-kind='spinner'>{WireframeSvg.Icon("spinner", cx, cy, size)}</g>");
    }

    private static void RenderImage(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);
        var w = state.Number(node, "w", state.Context.SizeW);
        var h = state.Number(node, "h", state.Context.SizeH);
        var src = state.Text(node, "src", ResolveContent(node, state));

        if (IsSafeDataUrl(src))
        {
            var href = EscapeAttribute(src);
            WireframeSvg.Markup(b,
                $"<image x='{WireframeSvg.F(x)}' y='{WireframeSvg.F(y)}' width='{WireframeSvg.F(w)}' height='{WireframeSvg.F(h)}' href='{href}' preserveAspectRatio='xMidYMid meet'></image>");
            return;
        }

        WireframeSvg.Markup(b, WireframeSvg.Icon("image", x + w / 2, y + h / 2, Math.Min(w, h)));
    }

    private static void RenderRawSvg(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var raw = ResolveContent(node, state);
        var sanitized = SanitizeRawSvg(raw);
        if (sanitized.Length > 0)
            WireframeSvg.Markup(b, sanitized);
    }

    private static void RenderGroup(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);
        var transform = state.Text(node, "transform", string.Empty);
        var opacityAttr = node.Attributes.ContainsKey("opacity")
            ? WireframeSvg.F(state.Number(node, "opacity", 1))
            : null;
        var safeTransform = ContainsDangerousMarkup(transform) ? string.Empty : transform;
        var transformAttr = safeTransform.Length == 0
            ? $"translate({WireframeSvg.F(x)},{WireframeSvg.F(y)})"
            : $"translate({WireframeSvg.F(x)},{WireframeSvg.F(y)}) {safeTransform}";

        WireframeSvg.OpenGroup(b, transformAttr, opacityAttr);
        try
        {
            RenderChildren(node.Children, state, b);
        }
        finally
        {
            WireframeSvg.CloseGroup(b);
        }
    }

    private static void RenderLayoutContainer(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var childSizes = node.Children.Select(child => NodeSize(child, state)).ToArray();
        var rects = StencilLayoutEngine.LayoutChildren(node, state.Context.SizeW, state.Context.SizeH, childSizes);

        for (var i = 0; i < node.Children.Count && i < rects.Count; i++)
        {
            var child = node.Children[i];
            var rect = StencilLayoutEngine.HasAnchor(child)
                ? StencilLayoutEngine.ApplyAnchors(child, state.Context.SizeW, state.Context.SizeH)
                : rects[i];
            RenderChildAt(child, rect, state, b);
        }
    }

    private static void RenderRepeat(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var hasItems = TryResolveRepeatItems(node, state, out var repeatItems);
        var count = hasItems
            ? repeatItems.Count
            : Math.Max(0, (int)Math.Round(state.Number(node, "count", 0)));
        var max = Math.Max(0, (int)Math.Round(state.Number(node, "max", count)));
        var rendered = Math.Min(count, max);
        if (count > max)
            state.Logger?.LogWarning(
                "Stencil repeat capped from {Count} to {Max}; dropped {Dropped} items.",
                count,
                max,
                count - max);

        var template = node.Node ?? node.Children.FirstOrDefault();
        if (template is null)
            return;

        var direction = state.Text(node, "direction", "column");
        var gap = state.Number(node, "gap", 0);
        var columns = Math.Max(0, (int)Math.Round(state.Number(node, "columns", 0)));
        var baseX = state.Number(node, "x", 0);
        var baseY = state.Number(node, "y", 0);
        var size = NodeSize(template, state);
        for (var i = 0; i < rendered; i++)
        {
            var rect = columns > 0
                ? new LayoutRect(
                    baseX + (i % columns) * (size.W + gap),
                    baseY + (i / columns) * (size.H + gap),
                    size.W,
                    size.H)
                : direction.Equals("row", StringComparison.OrdinalIgnoreCase)
                ? new LayoutRect(baseX + i * (size.W + gap), baseY, size.W, size.H)
                : new LayoutRect(baseX, baseY + i * (size.H + gap), size.W, size.H);
            var asName = state.Text(node, "as", node.As ?? string.Empty);
            var itemState = hasItems
                ? state.WithRepeat(i, asName, repeatItems[i])
                : state.WithRepeat(i, asName);
            RenderChildAt(template, rect, itemState, b);
        }
    }

    private static bool TryResolveRepeatItems(
        RenderNode node,
        RendererState state,
        out IReadOnlyList<object?> items)
    {
        items = [];
        if (string.IsNullOrWhiteSpace(node.Prop))
            return false;

        var propName = state.Evaluate(node.Prop).AsString();
        if (string.IsNullOrWhiteSpace(propName)
            || !state.Context.Props.TryGetValue(propName, out var value))
        {
            return false;
        }

        return TryConvertRepeatItems(value, out items);
    }

    private static bool TryConvertRepeatItems(object? value, out IReadOnlyList<object?> items)
    {
        items = [];
        switch (value)
        {
            case null:
                return false;
            case JsonElement { ValueKind: JsonValueKind.Array } array:
                items = [.. array.EnumerateArray().Select(static item => (object?)item.Clone())];
                return true;
            case JsonElement { ValueKind: JsonValueKind.Object } obj:
                items = [.. obj.EnumerateObject().Select(static property => (object?)property.Value.Clone())];
                return true;
            case IEnumerable<string> strings:
                items = [.. strings.Select(static item => (object?)item)];
                return true;
            case IEnumerable<object?> objects when value is not string:
                items = [.. objects];
                return true;
            default:
                return false;
        }
    }

    private static void RenderComponent(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var type = ResolveReference(node, state, "ref", "type");
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);
        var fallbackSize = NodeSize(node, state);
        if (string.IsNullOrWhiteSpace(type))
        {
            RenderPlaceholderAt(b, x, y, fallbackSize.W, fallbackSize.H, "Missing component");
            return;
        }

        var composition = state.RenderContext.Scope;
        var def = composition?.Registry?.GetDef(type, composition.ComponentScope);
        if (def is null)
        {
            state.Logger?.LogWarning("Stencil component reference '{Type}' could not be resolved.", type);
            RenderPlaceholderAt(b, x, y, fallbackSize.W, fallbackSize.H, "Missing component");
            return;
        }

        var childProps = EvaluateProps(node.Props, state);
        var (childW, childH) = ResolveComponentSize(def, childProps);
        var targetPack = composition!.ResolvePackForType(type);
        if (!IsTargetCompatible(type, composition, targetPack))
        {
            state.Logger?.LogWarning(
                "Stencil component reference '{Type}' is not compatible with current target.", type);
            RenderPlaceholderAt(b, x, y, childW, childH, "Incompatible");
            return;
        }

        var guardKey = "component:" + def.Type;
        if (!state.RenderContext.CanDescend(guardKey))
        {
            state.Logger?.LogWarning("Stencil component reference '{Type}' was blocked by recursion guard.", type);
            RenderPlaceholderAt(b, x, y, childW, childH, "Recursive");
            return;
        }

        var childComposition = composition.WithCurrentPack(targetPack ?? composition.CurrentPack);
        var childContext = state.RenderContext.Descend(guardKey, childComposition);
        var child = new WireframeElement
        {
            Type = def.Type,
            W = childW,
            H = childH,
            Props = childProps
        };

        WireframeSvg.OpenGroup(b, $"translate({WireframeSvg.F(x)},{WireframeSvg.F(y)})");
        try
        {
            RenderWithContext(childContext, () => def.RenderSvg(child, b));
        }
        catch (Exception ex)
        {
            state.Logger?.LogWarning(ex, "Stencil component reference '{Type}' failed to render.", type);
            RenderPlaceholder(b, childW, childH, "Render error");
        }
        finally
        {
            WireframeSvg.CloseGroup(b);
        }
    }

    private static void RenderPart(RenderNode node, RendererState state, RenderTreeBuilder b)
    {
        var name = ResolveReference(node, state, "name", "ref");
        var x = state.Number(node, "x", 0);
        var y = state.Number(node, "y", 0);
        var size = NodeSize(node, state);
        if (string.IsNullOrWhiteSpace(name))
        {
            RenderPlaceholderAt(b, x, y, size.W, size.H, "Missing part");
            return;
        }

        var pack = state.RenderContext.Scope?.CurrentPack;
        if (pack is null || !pack.Parts.TryGetValue(name, out var part))
        {
            state.Logger?.LogWarning("Stencil part reference '{Name}' could not be resolved.", name);
            RenderPlaceholderAt(b, x, y, size.W, size.H, "Missing part");
            return;
        }

        var guardKey = "part:" + name;
        if (!state.RenderContext.CanDescend(guardKey))
        {
            state.Logger?.LogWarning("Stencil part reference '{Name}' was blocked by recursion guard.", name);
            RenderPlaceholderAt(b, x, y, size.W, size.H, "Recursive");
            return;
        }

        var childContext = state.RenderContext.Descend(guardKey, state.RenderContext.Scope);
        WireframeSvg.OpenGroup(b, $"translate({WireframeSvg.F(x)},{WireframeSvg.F(y)})");
        try
        {
            RenderWithContext(childContext, () => RenderNode(part, state.WithRenderContext(childContext), b));
        }
        catch (Exception ex)
        {
            state.Logger?.LogWarning(ex, "Stencil part reference '{Name}' failed to render.", name);
            RenderPlaceholder(b, size.W, size.H, "Part error");
        }
        finally
        {
            WireframeSvg.CloseGroup(b);
        }
    }

    private static void RenderNineSlice(StencilComponent spec, RendererState state, RenderTreeBuilder b)
    {
        var slice = new SliceInsets(spec.Slice!.Left, spec.Slice.Top, spec.Slice.Right, spec.Slice.Bottom);
        var fill = spec.Render is null
            ? WireframeSvg.Fill
            : SafeAttribute(state.Text(spec.Render, "fill", WireframeSvg.Fill), WireframeSvg.Fill);
        var stroke = spec.Render is null
            ? WireframeSvg.Border
            : SafeAttribute(state.Text(spec.Render, "stroke", WireframeSvg.Border), WireframeSvg.Border);

        foreach (var segment in StencilLayoutEngine.NineSlice(state.Context.SizeW, state.Context.SizeH, slice))
        {
            var r = segment.Rect;
            if (r.W <= 0 || r.H <= 0)
                continue;

            WireframeSvg.Markup(b, WireframeSvg.Rect(r.X, r.Y, r.W, r.H, fill, stroke, 0));
        }
    }

    private static void RenderChildren(IReadOnlyList<RenderNode> children, RendererState state, RenderTreeBuilder b)
    {
        foreach (var child in children)
        {
            if (StencilLayoutEngine.HasAnchor(child))
            {
                RenderChildAt(child, StencilLayoutEngine.ApplyAnchors(child, state.Context.SizeW, state.Context.SizeH), state, b);
                continue;
            }

            RenderNode(child, state, b);
        }
    }

    private static void RenderChildAt(RenderNode child, LayoutRect rect, RendererState state, RenderTreeBuilder b)
    {
        WireframeSvg.OpenGroup(b, $"translate({WireframeSvg.F(rect.X)},{WireframeSvg.F(rect.Y)})");
        try
        {
            RenderNode(child, state.WithSize(rect.W, rect.H), b);
        }
        finally
        {
            WireframeSvg.CloseGroup(b);
        }
    }

    private static LayoutRect NodeSize(RenderNode node, RendererState state)
    {
        if (node.Kind == RenderNodeKind.Component
            && TryResolveComponentSize(node, state, out var componentSize))
        {
            return componentSize;
        }

        var w = state.Number(node, "w", state.Number(node, "width", state.Context.SizeW));
        var h = state.Number(node, "h", state.Number(node, "height", state.Context.SizeH));
        return new LayoutRect(0, 0, w, h);
    }

    private static bool TryResolveComponentSize(RenderNode node, RendererState state, out LayoutRect size)
    {
        size = default;
        var type = ResolveReference(node, state, "ref", "type");
        if (string.IsNullOrWhiteSpace(type))
            return false;

        var composition = state.RenderContext.Scope;
        var def = composition?.Registry?.GetDef(type, composition.ComponentScope);
        if (def is null)
            return false;

        var props = EvaluateProps(node.Props, state);
        var (w, h) = ResolveComponentSize(def, props);
        size = new LayoutRect(0, 0, w, h);
        return true;
    }

    private static bool TryReadSlice(RenderNode node, out SliceInsets slice)
    {
        slice = default;
        if (!node.Attributes.TryGetValue("slice", out var value))
            return false;

        if (value is JsonElement { ValueKind: JsonValueKind.Object } element)
        {
            return TryGetDouble(element, "left", out var left)
                   && TryGetDouble(element, "top", out var top)
                   && TryGetDouble(element, "right", out var right)
                   && TryGetDouble(element, "bottom", out var bottom)
                   && Assign(left, top, right, bottom, out slice);
        }

        if (value is IReadOnlyDictionary<string, object?> dictionary)
        {
            return TryGetDictionaryDouble(dictionary, "left", out var left)
                   && TryGetDictionaryDouble(dictionary, "top", out var top)
                   && TryGetDictionaryDouble(dictionary, "right", out var right)
                   && TryGetDictionaryDouble(dictionary, "bottom", out var bottom)
                   && Assign(left, top, right, bottom, out slice);
        }

        return false;

        static bool Assign(double left, double top, double right, double bottom, out SliceInsets slice)
        {
            slice = new SliceInsets(left, top, right, bottom);
            return true;
        }

        static bool TryGetDouble(JsonElement element, string property, out double value)
        {
            value = 0;
            return element.TryGetProperty(property, out var prop)
                   && new StencilValue(prop).IsNumeric(out value);
        }

        static bool TryGetDictionaryDouble(
            IReadOnlyDictionary<string, object?> dictionary,
            string property,
            out double value)
        {
            value = 0;
            return dictionary.TryGetValue(property, out var raw)
                   && new StencilValue(raw).IsNumeric(out value);
        }
    }

    private static string ResolveReference(RenderNode node, RendererState state, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (node.Attributes.TryGetValue(key, out var value))
                return state.Evaluate(value).AsString().Trim();
        }

        return ResolveContent(node, state).Trim();
    }

    private static Dictionary<string, JsonElement> EvaluateProps(
        IReadOnlyDictionary<string, object?> props,
        RendererState state)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (key, value) in props)
        {
            var evaluated = ShouldEvaluateProp(value)
                ? state.Evaluate(value).Raw
                : value;
            result[key] = ToJsonElement(evaluated);
        }

        return result;
    }

    private static bool ShouldEvaluateProp(object? value)
        => value is null
           || value is string
           || value is JsonElement
           || value is IFormattable
           || value is bool;

    private static JsonElement ToJsonElement(object? value)
    {
        if (value is JsonElement element)
            return element.Clone();

        try
        {
            return JsonSerializer.SerializeToElement(value);
        }
        catch (NotSupportedException)
        {
            return JsonSerializer.SerializeToElement(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }

    private static (double W, double H) ResolveComponentSize(
        WireframeComponentDef def,
        IReadOnlyDictionary<string, JsonElement> props)
    {
        if (def.SizePresets is not null
            && props.TryGetValue("size", out var sizeProp)
            && sizeProp.ValueKind == JsonValueKind.String
            && def.SizePresets.TryGetValue(sizeProp.GetString() ?? string.Empty, out var preset))
        {
            return preset;
        }

        return (def.DefaultWidth, def.DefaultHeight);
    }

    private static bool IsTargetCompatible(
        string type,
        StencilCompositionScope composition,
        StencilPack? targetPack)
    {
        var currentPack = composition.CurrentPack;
        var targetNamespace = StencilCompositionScope.GetNamespace(type);
        if (string.IsNullOrWhiteSpace(targetNamespace)
            || currentPack is null
            || string.Equals(currentPack.Namespace, targetNamespace, StringComparison.Ordinal)
            || type.StartsWith("tempo:", StringComparison.Ordinal))
        {
            return true;
        }

        if (currentPack.Target is null || targetPack?.Target is null)
            return true;

        return string.Equals(currentPack.Target.Framework, targetPack.Target.Framework, StringComparison.OrdinalIgnoreCase)
               && string.Equals(currentPack.Target.Library, targetPack.Target.Library, StringComparison.OrdinalIgnoreCase);
    }

    private static void RenderPlaceholderAt(
        RenderTreeBuilder b,
        double x,
        double y,
        double w,
        double h,
        string label)
    {
        WireframeSvg.OpenGroup(b, $"translate({WireframeSvg.F(x)},{WireframeSvg.F(y)})");
        try
        {
            RenderPlaceholder(b, w, h, label);
        }
        finally
        {
            WireframeSvg.CloseGroup(b);
        }
    }

    private static void RenderPlaceholder(RenderTreeBuilder b, double w, double h, string label)
    {
        var safeW = Math.Max(1, w);
        var safeH = Math.Max(1, h);
        WireframeSvg.Markup(b, WireframeSvg.DashedRect(safeW, safeH));
        WireframeSvg.Markup(b, WireframeSvg.TextCentred(label, safeW, safeH, 10, WireframeSvg.ColorMuted));
    }

    private static RenderContext ResolveRenderContext(StencilCompositionScope? composition)
    {
        var active = CurrentRenderContext.Value;
        if (active is null)
            return new RenderContext(composition, 0, EmptyRefPath);

        return composition is null ? active : active with { Scope = composition };
    }

    private static void RenderWithContext(RenderContext context, Action render)
    {
        var previous = CurrentRenderContext.Value;
        CurrentRenderContext.Value = context;
        try
        {
            render();
        }
        finally
        {
            CurrentRenderContext.Value = previous;
        }
    }

    private static string ResolveContent(RenderNode node, RendererState state)
    {
        if (!string.IsNullOrEmpty(node.Text))
            return state.Evaluate(node.Text).AsString();

        if (!string.IsNullOrEmpty(node.Value))
            return state.Evaluate(node.Value).AsString();

        if (node.Attributes.TryGetValue("content", out var content))
            return state.Evaluate(content).AsString();

        if (node.Attributes.TryGetValue("value", out var value))
            return state.Evaluate(value).AsString();

        if (node.Attributes.TryGetValue("contentSlot", out var slot))
        {
            var slotName = state.Evaluate(slot).AsString();
            if (state.Context.Props.TryGetValue(slotName, out var slotValue))
                return new StencilValue(slotValue).AsString();
        }

        return string.Empty;
    }

    private static string ApplyEllipsis(string content, double width, double fontSize)
    {
        if (string.IsNullOrEmpty(content) || width <= 0 || fontSize <= 0)
            return content;

        var maxChars = (int)Math.Floor(width / (fontSize * 0.55));
        if (content.Length <= maxChars)
            return content;

        return maxChars <= 1 ? "…" : content[..(maxChars - 1)] + "…";
    }

    private static string TextAnchor(string align)
        => align.Trim().ToLowerInvariant() switch
        {
            "center" or "middle" => "middle",
            "right" or "end" => "end",
            _ => "start"
        };

    private static string DominantBaseline(string valign)
        => valign.Trim().ToLowerInvariant() switch
        {
            "top" or "start" => "hanging",
            "bottom" or "end" => "text-after-edge",
            _ => "middle"
        };

    private static bool NearlyEqual(double left, double right)
        => Math.Abs(left - right) < 0.001;

    private sealed record RenderContext(
        StencilCompositionScope? Scope,
        int Depth,
        IImmutableSet<string> RefPath)
    {
        public bool CanDescend(string key)
            => Depth < MaxDepth && !RefPath.Contains(key);

        public RenderContext Descend(string key, StencilCompositionScope? scope)
            => new(scope ?? Scope, Depth + 1, RefPath.Add(key));
    }

    private sealed class RendererState(
        StencilEvalContext context,
        StencilEvaluator evaluator,
        IReadOnlyDictionary<string, string> packIcons,
        ILogger? logger,
        RenderContext renderContext)
    {
        public StencilEvalContext Context { get; } = context;

        public IReadOnlyDictionary<string, string> PackIcons { get; } = packIcons;

        public ILogger? Logger { get; } = logger;

        public RenderContext RenderContext { get; } = renderContext;

        public RendererState WithSize(double width, double height)
            => new(Context with { SizeW = width, SizeH = height }, evaluator, PackIcons, Logger, RenderContext);

        public RendererState WithRenderContext(RenderContext context)
            => new(Context, evaluator, PackIcons, Logger, context);

        public RendererState WithRepeat(int repeatIndex, string asName)
            => WithRepeat(repeatIndex, asName, repeatIndex);

        public RendererState WithRepeat(int repeatIndex, string asName, object? value)
        {
            var props = new Dictionary<string, object?>(Context.Props, StringComparer.Ordinal);
            if (!string.IsNullOrWhiteSpace(asName))
                props[asName] = value;

            return new RendererState(
                Context with { Props = props, RepeatIndex = repeatIndex },
                evaluator,
                PackIcons,
                Logger,
                RenderContext);
        }

        public StencilValue Evaluate(object? value)
            => evaluator.Evaluate(AttributeText(value), Context);

        public string Text(RenderNode node, string key, string fallback)
            => node.Attributes.TryGetValue(key, out var value)
                ? Evaluate(value).AsString()
                : fallback;

        public double Number(RenderNode node, string key, double fallback)
            => node.Attributes.TryGetValue(key, out var value)
                ? Evaluate(value).AsDouble()
                : fallback;

        public bool Bool(RenderNode node, string key, bool fallback)
            => node.Attributes.TryGetValue(key, out var value)
                ? Evaluate(value).AsBool()
                : fallback;

        private static string AttributeText(object? value)
        {
            return value switch
            {
                null => string.Empty,
                string text => text,
                JsonElement element => element.ValueKind == JsonValueKind.String
                    ? element.GetString() ?? string.Empty
                    : element.GetRawText(),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            };
        }
    }
}
