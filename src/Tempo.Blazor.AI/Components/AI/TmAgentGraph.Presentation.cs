using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// The PRESENTATION half of <see cref="TmAgentGraph"/>: the names, tones, classes and geometry strings its markup reads,
/// plus the per-node handle that owns the element reference the focus move needs. Split out of the markup part so a
/// reviewer reads one concern at a time.
/// </summary>
public partial class TmAgentGraph
{
    // ── Presentation helpers ─────────────────────────────────────────────────

    private AgentGraphNode? FindNode(string nodeId)
        => _view.Nodes.FirstOrDefault(node => string.Equals(node.Id, nodeId, StringComparison.Ordinal));

    private string NodeName(string nodeId)
        => FindNode(nodeId) is { } node ? node.DisplayName : nodeId;

    private string KindText(AgentGraphNodeKind kind)
        => AgentGraphPresentation.IsKnownKind(kind)
            ? AgentGraphPresentation.KindText(Loc, kind)
            : Loc["TmAgentGraph_Kind_Agent"];

    private string StateText(AgentActivityStatus status)
        => AgentStatusPresentation.IsKnown(status)
            ? AgentStatusPresentation.Text(Loc, status)
            : Loc["TmAgentGraph_StateUnknown"];

    /// <summary>
    /// The agent palette slot a node paints its avatar with, or null for a tool, a resource and a human: they are not
    /// in the agent palette, so they keep the neutral avatar instead of borrowing an identity.
    /// </summary>
    private static string? ColorOf(AgentGraphNode node)
        => node.Agent is { } agent ? agent.ColorIndex.ToString(CultureInfo.InvariantCulture) : null;

    private static string? StatusSlug(AgentActivityStatus status)
        => AgentStatusPresentation.IsKnown(status) ? AgentStatusPresentation.Tone(status) : null;

    /// <summary>
    /// The number of calls on the node's connections — an edge's count belongs to BOTH of its endpoints, so a tool
    /// reports the calls it received and an agent the calls it made, from one rule. The per-connection numbers are in
    /// the table view, so no reader has to derive them from this total.
    /// </summary>
    private int CallsOf(string nodeId)
        => _view.Edges
            .Where(edge => string.Equals(edge.From, nodeId, StringComparison.Ordinal)
                           || string.Equals(edge.To, nodeId, StringComparison.Ordinal))
            .Sum(edge => edge.CallCount);

    private string NodeLabel(AgentGraphNode node)
        => node.HasLabel ? node.Label : node.DisplayName is { Length: > 0 } name ? name : KindText(node.Kind);

    private string NodeAriaLabel(AgentGraphNodePlacement placement)
        => Loc[
            "TmAgentGraph_NodeAriaLabel",
            NodeLabel(placement.Node),
            KindText(placement.Node.Kind),
            StateText(placement.Node.Status),
            CallsOf(placement.Node.Id).ToString(CultureInfo.CurrentCulture)];

    private string NodeTitle(AgentGraphNodePlacement placement)
        => string.IsNullOrWhiteSpace(placement.Node.Label)
            ? NodeLabel(placement.Node)
            : placement.Node.Label;

    private IReadOnlyList<string> LabelLines(AgentGraphNode node)
        => AgentGraphPresentation.LabelLines(NodeLabel(node), LabelCharsPerLine);

    private bool IsSelected(string nodeId) => string.Equals(_selectedNodeId, nodeId, StringComparison.Ordinal);

    private bool IsActive(AgentGraphNodePlacement placement)
        => string.Equals(_activeNodeId, placement.Node.Id, StringComparison.Ordinal);

    /// <summary>Whether the node is one connection away from the selected one.</summary>
    private bool IsNeighbor(string nodeId)
    {
        if (_selectedNodeId is not { } selected || string.Equals(selected, nodeId, StringComparison.Ordinal))
        {
            return false;
        }

        foreach (var edge in _view.Edges)
        {
            if (string.Equals(edge.From, selected, StringComparison.Ordinal)
                && string.Equals(edge.To, nodeId, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(edge.To, selected, StringComparison.Ordinal)
                && string.Equals(edge.From, nodeId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsEdgeHighlighted(AgentGraphEdge edge)
        => _selectedNodeId is { } selected
           && (string.Equals(edge.From, selected, StringComparison.Ordinal)
               || string.Equals(edge.To, selected, StringComparison.Ordinal));

    private string NodeClass(AgentGraphNodePlacement placement)
    {
        var builder = new StringBuilder("tm-ai-agent-graph__node");

        if (StatusSlug(placement.Node.Status) is { } status)
        {
            builder.Append(" tm-ai-agent-graph__node--").Append(status);
        }

        if (placement.IsPinned)
        {
            builder.Append(" tm-ai-agent-graph__node--pinned");
        }

        if (IsSelected(placement.Node.Id))
        {
            builder.Append(" tm-ai-agent-graph__node--selected");
        }
        else if (IsNeighbor(placement.Node.Id))
        {
            builder.Append(" tm-ai-agent-graph__node--neighbor");
        }
        else if (_selectedNodeId is not null)
        {
            builder.Append(" tm-ai-agent-graph__node--dimmed");
        }

        return builder.ToString();
    }

    private string EdgeClass(AgentGraphEdgePath path)
    {
        var builder = new StringBuilder("tm-ai-agent-graph__edge");

        if (path.IsBackEdge)
        {
            builder.Append(" tm-ai-agent-graph__edge--back");
        }

        if (path.Edge.IsActive)
        {
            builder.Append(" tm-ai-agent-graph__edge--active");
        }

        if (IsPulsing(path.Edge.Id))
        {
            builder.Append(" tm-ai-agent-graph__edge--pulse");
        }

        if (IsEdgeHighlighted(path.Edge))
        {
            builder.Append(" tm-ai-agent-graph__edge--highlighted");
        }
        else if (_selectedNodeId is not null)
        {
            builder.Append(" tm-ai-agent-graph__edge--dimmed");
        }

        return builder.ToString();
    }

    private string NodeTransform(AgentGraphNodePlacement placement)
        => $"translate({Number(placement.X)} {Number(placement.Y)})";

    private string LastCallText(AgentGraphEdge edge)
        => edge.LastCallAt is { } at
            ? at.ToString(FormatCulture.DateTimeFormat.ShortTimePattern, FormatCulture)
            : Loc["TmAgentGraph_Never"];

    private string Px(double value) => Number(value) + "px";

    private static string Number(double value)
        => double.IsFinite(value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : "0";

    /// <summary>
    /// Replaces the live region's text. Sticky (never cleared on a re-render, which could wipe an announcement before
    /// assistive technology read it); when two consecutive transitions would write the same sentence, one invisible
    /// marker character is toggled so the region still sees a mutation.
    /// </summary>
    private void Announce(string message)
    {
        if (string.Equals(message, _announcement, StringComparison.Ordinal))
        {
            _announcementMarker = !_announcementMarker;
            _announcement = _announcementMarker ? message + "\u200B" : message;
            return;
        }

        _announcementMarker = false;
        _announcement = message;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelTrailingPaint();
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _self?.Dispose();
        _self = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        Dispose();

        if (_interop is { } interop)
        {
            _interop = null;
            await interop.DisposeAsync();
        }
    }

    /// <summary>
    /// The pulse window written onto the root as <c>--tm-agent-graph-pulse-duration</c>: the stylesheet's animation
    /// length and the timer that removes the pulse class read ONE value, so a host that changes
    /// <see cref="PulseDuration"/> never has to restyle the knob to keep the movement and the highlight in step.
    /// </summary>
    private string PulseDurationStyle => "--tm-agent-graph-pulse-duration: " + PulseDurationValue;

    /// <summary>The parameter as a CSS time; a non-finite or negative duration becomes zero rather than invalid CSS.</summary>
    private string PulseDurationValue
    {
        get
        {
            var milliseconds = PulseDuration.TotalMilliseconds;
            if (!double.IsFinite(milliseconds) || milliseconds <= 0)
            {
                milliseconds = 0;
            }

            return milliseconds.ToString("0.##", CultureInfo.InvariantCulture) + "ms";
        }
    }

    /// <summary>
    /// The tabindex of the scrollport: a node carries the graph's ONE tab stop (a roving tabindex), so the viewport is
    /// programmatic only (<c>-1</c>). Where no node can take the focus, the viewport keeps the drawing reachable from
    /// the keyboard for native scrolling (<c>0</c>).
    /// </summary>
    private string ViewportTabIndex => Layout.IsEmpty || _handles.Count == 0 ? "0" : "-1";

    /// <summary>One node's rendered element, so the component can move the DOM focus onto it.</summary>
    private sealed class NodeHandle
    {
        /// <summary>The layout placement the group paints; refreshed in place so the element reference survives.</summary>
        public AgentGraphNodePlacement Placement { get; set; } = null!;

        /// <summary>The element reference Blazor stamps on the group.</summary>
        public ElementReference Ref { get; set; }
    }
}
