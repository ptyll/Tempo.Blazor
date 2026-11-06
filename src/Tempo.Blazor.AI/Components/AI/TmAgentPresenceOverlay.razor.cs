using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Tempo.Blazor.AI.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.AI;

/// <summary>
/// Collaborators' cursors over ANY host content: a labelled pointer per present agent (or person), anchored either to an
/// element the host marked with <c>data-presence-anchor</c> or to a fraction of the content box.
/// <para>
/// The layer is absolutely positioned OVER <see cref="ChildContent"/> and never changes it: the host's markup renders
/// exactly as it would without the overlay, so a document, a wireframe canvas or a form keeps its own behaviour. The
/// position of an ANCHORED marker is measured by the package's ES module (it watches the content and every anchor with a
/// <c>ResizeObserver</c> and the content with a <c>MutationObserver</c>, throttling the work) and written into two CSS
/// custom properties, so a cursor that moves never re-renders the component or the host's content. A POINT marker is
/// painted from the host's own <see cref="PresenceMarker.X"/>/<see cref="PresenceMarker.Y"/> fractions.
/// </para>
/// <para>
/// Accessibility: the visual layer is decorative — it is <c>aria-hidden</c> and never enters the tab order, because a
/// pointer that takes focus would fight the editor underneath. The same presence is available as text through
/// <see cref="ShowRoster"/> (a list of the present agents and what they are doing), and exactly ONE polite live region
/// announces a join, a change of activity and a leave, with an identical state never announced twice.
/// </para>
/// <para>
/// Motion: a cursor that moves between two measured positions is animated by the stylesheet, and its reduced-motion block
/// turns the movement into a jump — the module itself never animates, so there is one motion source only.
/// </para>
/// </summary>
public partial class TmAgentPresenceOverlay : TmComponentBase, IAsyncDisposable
{
    private readonly string _id = $"tm-ai-presence-{Guid.NewGuid():n}"[..25];

    private readonly Dictionary<string, PresenceMarker> _seen = new(StringComparer.Ordinal);

    private IReadOnlyList<PresenceMarker> _markers = [];
    private AgentPresenceInterop? _interop;
    private ElementReference _root;
    private ElementReference _content;
    private bool _attached;
    private bool _disposed;

    private string _announcement = string.Empty;

    /// <summary>The runtime the measuring module is imported from.</summary>
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ───────────────────────────────────────────────────────────

    /// <summary>
    /// The cursors to paint, in an order the host controls (the component keeps it). Null or empty renders an empty
    /// layer and — with <see cref="ShowRoster"/> — the localized "no one is present" line.
    /// </summary>
    [Parameter] public IReadOnlyList<PresenceMarker>? Markers { get; set; }

    /// <summary>The content the cursors are painted over. It renders untouched; the layer is absolutely positioned on
    /// top of it and never intercepts the pointer.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>
    /// Whether the roster is rendered — the TEXT alternative of the cursor layer, listing every present agent and what
    /// they are doing. Default <c>false</c>, because the visual layer is what most hosts want and the live region
    /// already announces changes.
    /// </summary>
    [Parameter] public bool ShowRoster { get; set; }

    /// <summary>The roster's visible title and its accessible name. Null uses the localized default ("Present now").</summary>
    [Parameter] public string? RosterLabel { get; set; }

    /// <summary>
    /// Whether joins, activity changes and leaves are announced through the polite live region. Default <c>true</c>. A
    /// host that owns its own announcement channel turns this off; the component then renders NO live region at all
    /// rather than a second, competing one.
    /// </summary>
    [Parameter] public bool AnnounceChanges { get; set; } = true;

    /// <summary>Additional CSS class(es) for the root element.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Additional HTML attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    // ── Derived view ─────────────────────────────────────────────────────────

    /// <summary>The markers actually painted, in the host's order.</summary>
    internal IReadOnlyList<PresenceMarker> VisibleMarkers => _markers;

    /// <summary>The one polite live region's current text.</summary>
    internal string Announcement => _announcement;

    /// <summary>The roster's settled title.</summary>
    internal string EffectiveRosterLabel
        => string.IsNullOrWhiteSpace(RosterLabel) ? Loc["TmAgentPresenceOverlay_RosterTitle"] : RosterLabel!;

    private string RosterTitleId => _id + "-roster";

    private string RootClass
    {
        get
        {
            var root = "tm-ai-presence";
            return string.IsNullOrWhiteSpace(Class) ? root : root + " " + Class;
        }
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        _markers = Markers is null
            ? []
            : [.. Markers.Where(marker => marker is not null)];

        if (AnnounceChanges)
        {
            AnnounceChangesSinceLastRender();
        }
        else
        {
            _seen.Clear();
            foreach (var marker in _markers)
            {
                _seen[marker.Id] = marker;
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed)
        {
            return;
        }

        _interop ??= new AgentPresenceInterop(JS);

        if (!_attached)
        {
            if (_root.Id is null || _content.Id is null)
            {
                return;
            }

            _attached = true;
            await _interop.AttachAsync(_root, _content);

            // A failed import (a transient interop failure) is retried on the next render instead of leaving the
            // anchored markers unplaced for the component's whole lifetime.
            _attached = _interop.IsAttached;
            return;
        }

        // A render may have moved, added or replaced an anchor; the module throttles the actual measurement.
        await _interop.RefreshAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_interop is { } interop)
        {
            _interop = null;
            await interop.DisposeAsync();
        }
    }

    // ── Presentation helpers ─────────────────────────────────────────────────

    /// <summary>The name the label shows: the host's name or the localized fallback.</summary>
    /// <param name="agent">The agent to name.</param>
    /// <returns>The name.</returns>
    internal string AgentName(AgentRef agent) => agent.HasName ? agent.Name : Loc["TmAgentBadge_NameFallback"];

    /// <summary>
    /// What the label says about the activity: the host's own sentence when it supplied one, otherwise the localized
    /// word for the enum.
    /// </summary>
    /// <param name="marker">The marker to describe.</param>
    /// <returns>The activity text.</returns>
    internal string ActivityText(PresenceMarker marker)
        => marker.HasLabel
            ? marker.Label!
            : Loc[ActivityKey(marker.Activity)];

    /// <summary>The stable slug of an activity, used as the <c>data-activity</c> hook instead of a localized word.</summary>
    /// <param name="activity">The activity to name.</param>
    /// <returns><c>viewing</c>, <c>typing</c>, <c>editing</c> or <c>commenting</c>.</returns>
    internal static string ActivitySlug(PresenceActivity activity) => activity switch
    {
        PresenceActivity.Typing => "typing",
        PresenceActivity.Editing => "editing",
        PresenceActivity.Commenting => "commenting",
        _ => "viewing",
    };

    /// <summary>The positioned style of one marker: the palette colour plus the host's own fractions.</summary>
    /// <param name="marker">The marker to paint.</param>
    /// <returns>The inline custom properties.</returns>
    internal string MarkerStyle(PresenceMarker marker)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"--tm-presence-color: var({marker.Agent.ColorToken}); --tm-presence-x: {Percent(marker.X)}; --tm-presence-y: {Percent(marker.Y)}");

    private static string ActivityKey(PresenceActivity activity) => activity switch
    {
        PresenceActivity.Typing => "TmAgentPresenceOverlay_ActivityTyping",
        PresenceActivity.Editing => "TmAgentPresenceOverlay_ActivityEditing",
        PresenceActivity.Commenting => "TmAgentPresenceOverlay_ActivityCommenting",
        _ => "TmAgentPresenceOverlay_ActivityViewing",
    };

    private static string Percent(double fraction)
        => (fraction * 100d).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Announces the most significant change since the previous render and records the whole current state, so a state
    /// that did not change is never announced twice. Several simultaneous changes produce one sentence (the first in the
    /// host's order) — the roster carries the full state, and a live region that read every change would be noise.
    /// </summary>
    private void AnnounceChangesSinceLastRender()
    {
        string? message = null;
        var previous = new Dictionary<string, PresenceMarker>(_seen, StringComparer.Ordinal);
        _seen.Clear();

        foreach (var marker in _markers)
        {
            _seen[marker.Id] = marker;
            if (message is not null)
            {
                continue;
            }

            if (!previous.TryGetValue(marker.Id, out var seen))
            {
                message = JoinedSentence(marker);
            }
            else if (!string.Equals(seen.Agent.Name, marker.Agent.Name, StringComparison.Ordinal))
            {
                // A different name is a different reading of WHO is present, not of what they are doing.
                message = JoinedSentence(marker);
            }
            else if (seen.Activity != marker.Activity)
            {
                // "Ana is now Commenting." reads as a CHANGE; "Ana is Commenting." reads as a join.
                message = ActivityChangedSentence(marker);
            }
        }

        if (message is null)
        {
            foreach (var (id, seen) in previous)
            {
                if (!_seen.ContainsKey(id))
                {
                    message = Loc["TmAgentPresenceOverlay_AnnounceLeft", AgentName(seen.Agent)];
                    break;
                }
            }
        }

        if (message is not null)
        {
            _announcement = message;
        }
    }

    private string JoinedSentence(PresenceMarker marker)
        => Loc["TmAgentPresenceOverlay_AnnouncePresence", AgentName(marker.Agent), ActivityText(marker)];

    private string ActivityChangedSentence(PresenceMarker marker)
        => Loc["TmAgentPresenceOverlay_AnnounceActivity", AgentName(marker.Agent), ActivityText(marker)];
}
