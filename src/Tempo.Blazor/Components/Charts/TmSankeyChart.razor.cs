using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Tempo.Blazor.Components.Charts;

/// <summary>
/// Renders a responsive Sankey flow diagram as pure inline SVG without JavaScript dependencies.
/// </summary>
public partial class TmSankeyChart
{
    internal const double SvgWidth = 800;
    internal const double SvgHeight = 400;
    private const double LabelHorizontalPadding = 144;
    private const double CompactHorizontalPadding = 32;

    private static readonly string[] Palette =
    [
        "#3b82f6", "#ef4444", "#10b981", "#f59e0b", "#8b5cf6",
        "#ec4899", "#06b6d4", "#84cc16", "#f97316", "#6366f1",
    ];

    private SankeyLayoutResult? _layout;
    private Dictionary<string, int> _nodeIndexes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _highlightedNodeIds = new(StringComparer.Ordinal);
    private readonly HashSet<int> _highlightedLinkIndexes = [];
    private SankeyData? _highlightedData;

    /// <summary>Gets or sets the nodes and directed value flows to render.</summary>
    [Parameter, EditorRequired]
    public SankeyData Data { get; set; } = default!;

    /// <summary>Gets or sets the CSS width of the chart container.</summary>
    [Parameter]
    public string Width { get; set; } = "100%";

    /// <summary>Gets or sets the CSS height of the chart container.</summary>
    [Parameter]
    public string Height { get; set; } = "400px";

    /// <summary>Gets or sets whether node labels are rendered.</summary>
    [Parameter]
    public bool ShowLabels { get; set; } = true;

    /// <summary>Gets or sets whether node values are appended to labels.</summary>
    [Parameter]
    public bool ShowValues { get; set; } = true;

    /// <summary>Gets or sets the node width in SVG view-box units.</summary>
    [Parameter]
    public double NodeWidth { get; set; } = 16;

    /// <summary>Gets or sets the vertical gap between nodes in the same layer.</summary>
    [Parameter]
    public double NodePadding { get; set; } = 10;

    /// <summary>Gets or sets the minimum rendered link width in SVG view-box units.</summary>
    [Parameter]
    public double MinLinkWidth { get; set; } = 1;

    /// <summary>Gets or sets the base link opacity. Values outside zero to one are clamped.</summary>
    [Parameter]
    public double LinkOpacity { get; set; } = 0.4;

    /// <summary>Gets or sets whether hovering highlights connected nodes and links.</summary>
    [Parameter]
    public bool HighlightOnHover { get; set; } = true;

    /// <summary>Raised when a Sankey node is clicked.</summary>
    [Parameter]
    public EventCallback<SankeyNode> OnNodeClick { get; set; }

    /// <summary>Raised when a Sankey link is clicked.</summary>
    [Parameter]
    public EventCallback<SankeyLink> OnLinkClick { get; set; }

    /// <summary>
    /// Gets or sets an optional value formatter. The default uses the current culture and
    /// the <c>0.##</c> numeric format.
    /// </summary>
    [Parameter]
    public Func<double, string>? ValueFormatter { get; set; }

    /// <summary>Gets or sets additional CSS classes applied to the chart container.</summary>
    [Parameter]
    public string? Class { get; set; }

    private string CssClass =>
        string.IsNullOrWhiteSpace(Class) ? "tm-sankey" : $"tm-sankey {Class}";

    private string WrapperStyle => $"width:{Width};height:{Height};";

    private bool IsNoData =>
        Data is null ||
        Data.Nodes is { Count: 0 } && Data.Links is { Count: 0 } ||
        Data.Nodes is { Count: > 0 } && Data.Links is { Count: 0 };

    private string LayoutErrorText =>
        _layout?.ErrorKind == SankeyLayoutErrorKind.Cycle
            ? Loc["TmSankeyChart_CycleDetected"]
            : Loc["TmSankeyChart_InvalidData"];

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _layout = SankeyLayoutEngine.Layout(
            Data,
            SvgWidth,
            SvgHeight,
            NodeWidth,
            NodePadding,
            MinLinkWidth,
            horizontalPadding: ShowLabels ? LabelHorizontalPadding : CompactHorizontalPadding);

        _nodeIndexes = Data?.Nodes?
            .Select((node, index) => (node, index))
            .Where(item => item.node is not null && !string.IsNullOrWhiteSpace(item.node.Id))
            .GroupBy(item => item.node.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().index, StringComparer.Ordinal)
            ?? new Dictionary<string, int>(StringComparer.Ordinal);

        if (!ReferenceEquals(_highlightedData, Data) || !HighlightOnHover)
        {
            ClearHighlight();
        }

        _highlightedData = Data;
    }

    private RenderFragment ChartContent => builder =>
    {
        if (_layout is null)
        {
            return;
        }

        BuildLinks(builder, _layout.Links);
        BuildNodes(builder, _layout.Nodes);
    };

    private void BuildLinks(RenderTreeBuilder builder, IReadOnlyList<SankeyLinkLayout> links)
    {
        for (var index = 0; index < links.Count; index++)
        {
            var layout = links[index];
            var linkIndex = index;
            var sourceLabel = NodeLabel(layout.Link.SourceId);
            var targetLabel = NodeLabel(layout.Link.TargetId);

            builder.OpenElement(0, "path");
            builder.SetKey(layout.Link);
            builder.AddAttribute(1, "class", LinkCssClass(index));
            builder.AddAttribute(2, "data-link-index", index.ToString(CultureInfo.InvariantCulture));
            builder.AddAttribute(3, "data-source-id", layout.Link.SourceId);
            builder.AddAttribute(4, "data-target-id", layout.Link.TargetId);
            builder.AddAttribute(5, "d", layout.PathData);
            builder.AddAttribute(6, "fill", "none");
            builder.AddAttribute(7, "stroke", LinkColor(layout.Link));
            builder.AddAttribute(8, "stroke-width", F(layout.Width));
            builder.AddAttribute(9, "stroke-opacity", F(NormalizedLinkOpacity));
            if (OnLinkClick.HasDelegate)
            {
                builder.AddAttribute(10, "role", "button");
                builder.AddAttribute(11, "tabindex", "0");
                builder.AddAttribute(
                    12,
                    "aria-label",
                    $"{sourceLabel} → {targetLabel}: {FormatValue(layout.Link.Value)}");
                builder.AddAttribute(
                    13,
                    "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(
                        this,
                        () => HandleLinkClickAsync(layout.Link)));
                builder.AddAttribute(
                    14,
                    "onkeydown",
                    EventCallback.Factory.Create<KeyboardEventArgs>(
                        this,
                        args => HandleLinkKeyDownAsync(args, layout.Link)));
                builder.AddAttribute(
                    100,
                    "onkeyup",
                    EventCallback.Factory.Create<KeyboardEventArgs>(
                        this,
                        args => HandleLinkKeyUpAsync(args, layout.Link)));
                builder.AddAttribute(
                    15,
                    "onfocus",
                    EventCallback.Factory.Create<FocusEventArgs>(
                        this,
                        () => HighlightLink(linkIndex)));
                builder.AddAttribute(
                    16,
                    "onblur",
                    EventCallback.Factory.Create<FocusEventArgs>(this, ClearHighlight));
            }

            builder.AddAttribute(
                17,
                "onmouseover",
                EventCallback.Factory.Create<MouseEventArgs>(
                    this,
                    () => HighlightLink(linkIndex)));
            builder.AddAttribute(
                18,
                "onmouseout",
                EventCallback.Factory.Create<MouseEventArgs>(this, ClearHighlight));

            builder.OpenElement(19, "title");
            builder.AddContent(
                20,
                $"{sourceLabel} → {targetLabel}: {FormatValue(layout.Link.Value)}");
            builder.CloseElement();
            builder.CloseElement();
        }
    }

    private void BuildNodes(RenderTreeBuilder builder, IReadOnlyList<SankeyNodeLayout> nodes)
    {
        for (var index = 0; index < nodes.Count; index++)
        {
            var layout = nodes[index];
            var color = NodeColor(layout.Node.Id);

            builder.OpenElement(0, "rect");
            builder.SetKey(layout.Node);
            builder.AddAttribute(1, "class", NodeCssClass(layout.Node.Id));
            builder.AddAttribute(2, "data-node-id", layout.Node.Id);
            builder.AddAttribute(3, "x", F(layout.X));
            builder.AddAttribute(4, "y", F(layout.Y));
            builder.AddAttribute(5, "width", F(layout.Width));
            builder.AddAttribute(6, "height", F(layout.Height));
            builder.AddAttribute(7, "rx", "2");
            builder.AddAttribute(8, "fill", color);
            if (OnNodeClick.HasDelegate)
            {
                builder.AddAttribute(9, "role", "button");
                builder.AddAttribute(10, "tabindex", "0");
                builder.AddAttribute(
                    11,
                    "aria-label",
                    $"{layout.Node.Label} — {FormatValue(layout.Value)}");
                builder.AddAttribute(
                    12,
                    "onclick",
                    EventCallback.Factory.Create<MouseEventArgs>(
                        this,
                        () => HandleNodeClickAsync(layout.Node)));
                builder.AddAttribute(
                    13,
                    "onkeydown",
                    EventCallback.Factory.Create<KeyboardEventArgs>(
                        this,
                        args => HandleNodeKeyDownAsync(args, layout.Node)));
                builder.AddAttribute(
                    100,
                    "onkeyup",
                    EventCallback.Factory.Create<KeyboardEventArgs>(
                        this,
                        args => HandleNodeKeyUpAsync(args, layout.Node)));
                builder.AddAttribute(
                    14,
                    "onfocus",
                    EventCallback.Factory.Create<FocusEventArgs>(
                        this,
                        () => HighlightNode(layout.Node.Id)));
                builder.AddAttribute(
                    15,
                    "onblur",
                    EventCallback.Factory.Create<FocusEventArgs>(this, ClearHighlight));
            }

            builder.AddAttribute(
                16,
                "onmouseover",
                EventCallback.Factory.Create<MouseEventArgs>(
                    this,
                    () => HighlightNode(layout.Node.Id)));
            builder.AddAttribute(
                17,
                "onmouseout",
                EventCallback.Factory.Create<MouseEventArgs>(this, ClearHighlight));

            builder.OpenElement(18, "title");
            builder.AddContent(19, $"{layout.Node.Label} — {FormatValue(layout.Value)}");
            builder.CloseElement();
            builder.CloseElement();

            if (ShowLabels)
            {
                BuildLabel(builder, layout);
            }
        }
    }

    private void BuildLabel(RenderTreeBuilder builder, SankeyNodeLayout layout)
    {
        var firstLayer = layout.Layer == 0;
        var x = firstLayer ? layout.X - 8 : layout.X + layout.Width + 8;
        var label = ShowValues
            ? $"{layout.Node.Label} — {FormatValue(layout.Value)}"
            : layout.Node.Label;

        builder.OpenElement(0, "text");
        builder.AddAttribute(1, "class", LabelCssClass(layout.Node.Id));
        builder.AddAttribute(2, "data-node-id", layout.Node.Id);
        builder.AddAttribute(3, "x", F(x));
        builder.AddAttribute(4, "y", F(layout.Y + (layout.Height / 2)));
        builder.AddAttribute(5, "text-anchor", firstLayer ? "end" : "start");
        builder.AddAttribute(6, "dominant-baseline", "middle");
        builder.AddContent(7, label);
        builder.CloseElement();
    }

    private string NodeColor(string nodeId)
    {
        if (_nodeIndexes.TryGetValue(nodeId, out var index) &&
            !string.IsNullOrWhiteSpace(Data.Nodes[index].Color))
        {
            return Data.Nodes[index].Color!;
        }

        return Palette[
            _nodeIndexes.TryGetValue(nodeId, out index)
                ? index % Palette.Length
                : 0];
    }

    private string LinkColor(SankeyLink link) =>
        string.IsNullOrWhiteSpace(link.Color) ? NodeColor(link.SourceId) : link.Color;

    private string NodeLabel(string nodeId) =>
        _nodeIndexes.TryGetValue(nodeId, out var index)
            ? Data.Nodes[index].Label
            : nodeId;

    private string FormatValue(double value) =>
        ValueFormatter?.Invoke(value) ??
        value.ToString("0.##", CultureInfo.CurrentCulture);

    private double NormalizedLinkOpacity =>
        double.IsFinite(LinkOpacity) ? Math.Clamp(LinkOpacity, 0, 1) : 0.4;

    private bool HasHighlight =>
        _highlightedNodeIds.Count > 0 || _highlightedLinkIndexes.Count > 0;

    private string NodeCssClass(string nodeId)
    {
        var classes = "tm-sankey__node";
        if (OnNodeClick.HasDelegate)
        {
            classes += " tm-sankey__node--clickable";
        }

        if (HasHighlight)
        {
            classes += _highlightedNodeIds.Contains(nodeId)
                ? " tm-sankey__node--highlight"
                : " tm-sankey__node--dimmed";
        }

        return classes;
    }

    private string LinkCssClass(int linkIndex)
    {
        var classes = "tm-sankey__link";
        if (OnLinkClick.HasDelegate)
        {
            classes += " tm-sankey__link--clickable";
        }

        if (HasHighlight)
        {
            classes += _highlightedLinkIndexes.Contains(linkIndex)
                ? " tm-sankey__link--highlight"
                : " tm-sankey__link--dimmed";
        }

        return classes;
    }

    private string LabelCssClass(string nodeId)
    {
        var classes = "tm-sankey__label";
        if (HasHighlight)
        {
            classes += _highlightedNodeIds.Contains(nodeId)
                ? " tm-sankey__label--highlight"
                : " tm-sankey__label--dimmed";
        }

        return classes;
    }

    private Task HandleNodeClickAsync(SankeyNode node) =>
        OnNodeClick.HasDelegate ? OnNodeClick.InvokeAsync(node) : Task.CompletedTask;

    private Task HandleLinkClickAsync(SankeyLink link) =>
        OnLinkClick.HasDelegate ? OnLinkClick.InvokeAsync(link) : Task.CompletedTask;

    // The <rect>/<path> activatables are non-native focusables (role="button" + tabindex) —
    // the emulation is required and follows the native <button> split exactly
    // (docs/keyboard-activation-convention.md): Enter fires on keydown (!Repeat-guarded —
    // a held Enter's auto-repeat stream must not re-fire), Space fires on keyup only.
    private Task HandleNodeKeyDownAsync(KeyboardEventArgs args, SankeyNode node) =>
        args.Key == "Enter" && !args.Repeat ? HandleNodeClickAsync(node) : Task.CompletedTask;

    private Task HandleLinkKeyDownAsync(KeyboardEventArgs args, SankeyLink link) =>
        args.Key == "Enter" && !args.Repeat ? HandleLinkClickAsync(link) : Task.CompletedTask;

    private Task HandleNodeKeyUpAsync(KeyboardEventArgs args, SankeyNode node) =>
        args.Key == " " ? HandleNodeClickAsync(node) : Task.CompletedTask;

    private Task HandleLinkKeyUpAsync(KeyboardEventArgs args, SankeyLink link) =>
        args.Key == " " ? HandleLinkClickAsync(link) : Task.CompletedTask;

    private void HighlightNode(string nodeId)
    {
        if (!HighlightOnHover)
        {
            return;
        }

        ClearHighlight();
        _highlightedNodeIds.Add(nodeId);

        for (var index = 0; index < Data.Links.Count; index++)
        {
            var link = Data.Links[index];
            if (!string.Equals(link.SourceId, nodeId, StringComparison.Ordinal) &&
                !string.Equals(link.TargetId, nodeId, StringComparison.Ordinal))
            {
                continue;
            }

            _highlightedLinkIndexes.Add(index);
            _highlightedNodeIds.Add(link.SourceId);
            _highlightedNodeIds.Add(link.TargetId);
        }
    }

    private void HighlightLink(int linkIndex)
    {
        if (!HighlightOnHover || linkIndex < 0 || linkIndex >= Data.Links.Count)
        {
            return;
        }

        ClearHighlight();
        var link = Data.Links[linkIndex];
        _highlightedLinkIndexes.Add(linkIndex);
        _highlightedNodeIds.Add(link.SourceId);
        _highlightedNodeIds.Add(link.TargetId);
    }

    private void ClearHighlight()
    {
        _highlightedNodeIds.Clear();
        _highlightedLinkIndexes.Clear();
    }

    private static string F(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}
