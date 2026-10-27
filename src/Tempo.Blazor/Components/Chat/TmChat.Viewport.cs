using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Virtual-keyboard half of <see cref="TmChat"/> (FZE-13): the OPT-IN
/// <see cref="TrackVirtualKeyboard"/> keeps the narrow-chat sticky composer ABOVE the on-screen
/// keyboard. On iOS Safari an opening keyboard shrinks only the VISUAL viewport — the layout
/// viewport (and with it every sticky containing block) stays put, so
/// <c>position: sticky; bottom: var(--tm-chat-composer-offset)</c> alone leaves the composer
/// under the keyboard. The <c>chat.js</c> <c>attachViewportTracker(root, options)</c> measures
/// the covered gap and writes it as the RUNTIME knob <c>--tm-chat-keyboard-inset</c> into the
/// chat root's inline style (plus the <c>data-tm-keyboard-open</c> marker); the narrow-chat CSS
/// adds the knob to the sticky composer and thread reply row offsets and drops the safe-area
/// padding while the keyboard is open. The knob is never DECLARED by the library — the default
/// lives in the <c>var()</c> fallback (<c>0px</c>), the inline value is written and removed ONLY
/// by the tracker at runtime, and a host override on a wrapper or <c>:root</c> keeps cascading.
/// <para>
/// The sync rides the per-render scroller sync (<see cref="SyncScrollerAsync"/> tail — the
/// tracker needs the same module, and its pinned-scroller rescue option takes the scroller
/// handle); the handle dies with the scroller (<see cref="DisposeScrollerAsync"/> tail, reached
/// from <c>DisposeAsyncCore</c>). Everything lives in this partial so <c>TmChat.razor.cs</c>
/// stays under the 1200-line source ceiling (N270/F13).
/// </para>
/// </summary>
public partial class TmChat
{
    /// <summary>
    /// Opt-in tracking of the on-screen keyboard for the narrow sticky composer (FZE-13).
    /// Default <see langword="false"/> — today's behavior: no <c>visualViewport</c> listeners
    /// and no inline knob, nothing changes without the opt-in. When <see langword="true"/>, the
    /// <c>chat.js</c> <c>attachViewportTracker</c> writes the keyboard's covered height into the
    /// chat root's inline style as <c>--tm-chat-keyboard-inset</c> while the keyboard is open
    /// (and removes it when it closes), so the narrow-chat sticky composer and thread reply row
    /// rise above the keyboard; while the marker is set the safe-area padding is dropped, because
    /// the keyboard covers the home indicator. The stuck composer covers more of the message list
    /// than the inset whenever the chat's box extends past the fold, so the tracker ALSO measures
    /// the covered list space into the runtime <c>--tm-chat-composer-clearance</c> knob: while
    /// the marker is set the messages list reserves it as scroll room and the pinned scroller is
    /// re-landed with the last row resting exactly on the stuck composer's top edge (a reader of
    /// older history keeps their position). The tracker is what handles the keyboard on iOS
    /// Safari, which ignores <c>interactive-widget</c>: on Android WITH the recommended
    /// <c>interactive-widget=resizes-content</c> viewport meta the LAYOUT viewport shrinks
    /// instead, the measured inset comes out ≈ 0 and the tracker is harmless there; on Android
    /// WITHOUT the meta (Chrome's default <c>resizes-visual</c>) the visual viewport shrinks
    /// like on iOS and the tracker lifts the composer there too. Before the first render (and
    /// without a live JS circuit) the sync is a no-op — the narrow CSS keeps working through the
    /// knobs' <c>0px</c> fallbacks.
    /// </summary>
    [Parameter] public bool TrackVirtualKeyboard { get; set; }

    /// <summary>The <c>{ dispose }</c> handle of the chat.js viewport tracker.</summary>
    private IJSObjectReference? _viewportTracker;

    /// <summary>Whether the current tracker was attached WITH the scroller rescue option.</summary>
    private bool _viewportTrackerHasScroller;

    /// <summary>
    /// (Re)attaches the viewport tracker per the <see cref="TrackVirtualKeyboard"/> parameter.
    /// Called from the <see cref="SyncScrollerAsync"/> tail on every render: the tracker needs
    /// the same <c>chat.js</c> module, and its pinned-scroller rescue takes the scroller handle —
    /// which is settled right before, on the same pass. A parameter or scroller change disposes
    /// and re-attaches (an attach is one call per state, never per frame); a failed import or a
    /// dropped circuit leaves the knob at its fallback and retries on the next render.
    /// </summary>
    private async Task SyncViewportTrackerAsync()
    {
        if (_disposed || _module is null) return;

        var hasScroller = _scroller is not null;
        if (!TrackVirtualKeyboard || string.IsNullOrEmpty(_chatRoot.Id))
        {
            if (_viewportTracker is not null)
            {
                await DisposeViewportTrackerAsync();
            }

            return;
        }

        if (_viewportTracker is not null && _viewportTrackerHasScroller == hasScroller)
        {
            return; // the current handle already reflects this state — one attach per state
        }

        await DisposeViewportTrackerAsync();
        try
        {
            var handle = await _module.InvokeAsync<IJSObjectReference>(
                "attachViewportTracker",
                _chatRoot,
                new { scroller = _scroller });
            if (_disposed)
            {
                // The component went away while the attach was in flight — the handle would leak
                // (its listeners would outlive the root element).
                await DisposeUnreachableAsync(handle);
                return;
            }

            if (handle is null) return; // nothing came back — retry on the next render

            _viewportTracker = handle;
            _viewportTrackerHasScroller = hasScroller;
        }
        catch (Exception ex) when (ex is JSException or JSDisconnectedException or ObjectDisposedException or OperationCanceledException)
        {
            // Attach failed (dropped circuit / prerender) — the CSS knob falls back to 0px and
            // the next render retries. No C# fallback exists here (the keyboard is a JS concern).
            _viewportTracker = null;
            _viewportTrackerHasScroller = false;
        }
    }

    /// <summary>Disposes the tracker handle (part of the <see cref="DisposeScrollerAsync"/> tail).</summary>
    private async ValueTask DisposeViewportTrackerAsync()
    {
        _viewportTrackerHasScroller = false;
        var handle = _viewportTracker;
        _viewportTracker = null;
        await DisposeJsHandleAsync(handle);
    }
}
