using System.Collections.Generic;
using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Chat;

/// <summary>
/// Render-window half of <see cref="TmChat"/> (FZE-14): the OPT-IN virtualization of very long
/// conversations. Instead of <c>&lt;Virtualize&gt;</c> (top-anchored, fixed/estimated row heights,
/// its spacers fight <c>overflow-anchor: none</c> + the anchor restore, and rows outside the DOM
/// would break the <c>data-message-id</c> anchors, <c>focusMessage</c> and row reading — decision
/// <c>TMCHAT-RENDER-WINDOW</c>) the component renders a contiguous <c>[Start, count)</c> slice of
/// the root sequence and grows it upward by <see cref="RenderWindowStep"/> rows. Because the
/// window only ever GROWS at the top, a window expansion is for the DOM exactly a "prepend" —
/// it reuses the existing <see cref="ScrollChangeMode.Prepend"/> mode and the
/// <c>chat.js</c> anchor restore, so the whole FZE-6 scroll contract keeps holding. The window
/// state itself is the dependency-free <see cref="ChatRenderWindow"/>; this partial carries the
/// parameters, the per-parameter-set sync and the expansion/deep-link hooks. The thread panel is
/// deliberately NOT windowed (threads are short), and <c>TmChat.razor.cs</c> stays
/// untouched under its source ceiling.
/// </summary>
public partial class TmChat
{
    // ── Parameters ──

    /// <summary>
    /// Opt-in render window over very long conversations (FZE-14). <see langword="null"/> (the
    /// default) renders EVERY root row — today's behavior, byte-identical markup. A positive
    /// value renders only the LAST <c>N</c> root rows (the window is clamped to a minimum of 20)
    /// and grows upward by <see cref="RenderWindowStep"/> rows whenever the user reaches the top
    /// edge — the expansion is applied as a render PREPEND, so the <c>chat.js</c> anchor restore
    /// keeps the reading position and the whole scroll contract (pinned tail, jump-to-new count,
    /// announcements, read receipts, draft guard) keeps working, because all of those are driven
    /// by the DATA (<c>Messages</c>), never by the DOM.
    /// <para>
    /// Rules: (a) the first render and a replaced list window the last <c>N</c> rows; (b) an
    /// append with the viewport pinned to the bottom slides the window with the tail (the top is
    /// trimmed to <c>count - N</c>); (c) an append while the user reads above the bottom trims
    /// NOTHING (the DOM above the viewport must not change) — the trim happens when the scroller
    /// reports the return to the bottom, reclaims only that AWAY-APPEND excess and refreshes the
    /// tracked keep-id onto the trimmed start (a deep-link or top-edge expansion mounts its rows
    /// deliberately and drops the claim, so it survives its own restore's at-bottom report and
    /// persists until the next pinned append); a row VANISHING from the list keeps the start
    /// (clamped) — only a pinned append falls back to the tail; the jump-to-new click trims the
    /// excess immediately (it IS a return to the bottom); (d) reaching the top edge
    /// expands the window one step — after the near-top report is verified against the live
    /// geometry twice, 300 ms apart (the <c>chat.js</c> <c>isNearTop()</c> handle getter), so a
    /// transient scroll clamp mid-churn cannot mount thousands of rows —
    /// WITHOUT calling <see cref="OnLoadOlder"/> — the host page load fires only once the window
    /// already covers the whole sequence (<c>Start == 0</c>) and <see cref="HasMoreBefore"/> is
    /// set; (e) a host prepend of older history keeps the previously-first rendered row and
    /// expands one step above it; (f) the row under edit and the opener of the active thread are
    /// never trimmed out of the window; (g) <see cref="ChatMessageContext.Index"/> and
    /// <see cref="ChatMessageContext.IsFirstInGroup"/> are computed over the FULL sequence, so
    /// grouping does not change at the window edge; (h) the built-in "load older" affordance
    /// renders while <c>Start &gt; 0</c> or <see cref="HasMoreBefore"/>. The built-in
    /// <see cref="ScrollToMessageAsync"/>/<see cref="TmChat.FocusMessageAsync"/> deep links
    /// expand the window (and wait for the render) when the anchor lies above it — CONCURRENT
    /// deep links share the one pending wait — so an anchor outside the window is still found.
    /// The thread panel is deliberately NOT windowed —
    /// threads are short and the panel renders the root message a second time.
    /// </para>
    /// </summary>
    [Parameter] public int? RenderWindowSize { get; set; }

    /// <summary>
    /// How many rows one upward expansion of the render window adds (clamped to a minimum of 10).
    /// Only meaningful together with <see cref="RenderWindowSize"/>; the expansion is a render
    /// prepend, so the scroller's anchor restore keeps the reading position across it. Default 50.
    /// </summary>
    [Parameter] public int RenderWindowStep { get; set; } = 50;

    // ── State ──

    /// <summary>The window state; <see langword="null"/> while <see cref="RenderWindowSize"/> is unset.</summary>
    private ChatRenderWindow? _window;

    /// <summary>
    /// Id of the row the window currently keeps at <see cref="ChatRenderWindow.Start"/> — the
    /// anchor the window re-locates after the host changes the list (a prepend of older history
    /// shifts every index).
    /// </summary>
    private string? _windowKeepId;

    /// <summary>
    /// Completed by <see cref="ApplyScrollAfterRenderAsync"/> once the render that applied a
    /// deep-link window expansion has run (the scroller bookkeeping included) — the deep-link
    /// helpers wait on it before their JS navigation so the anchor row exists. CONCURRENT deep
    /// links SHARE one wait (the <c>??=</c> keeps the first completion source, so every waiter
    /// completes on the one render that mounts all the anchors). Dispose completes it too, so a
    /// waiter never hangs.
    /// </summary>
    private TaskCompletionSource? _renderWindowApplied;

    /// <summary>
    /// Whether the CURRENT render ran the row loop while a deep-link wait was pending — the gate
    /// <see cref="SignalRenderWindowApplied"/> releases under. A render that never mounted rows
    /// (an emptied list) must not complete a navigation that expects the anchor to exist; only a
    /// render that actually iterated the row sequence (or dispose) may.
    /// </summary>
    private bool _renderWindowArmed;

    /// <summary>
    /// A render-window expansion (the top edge or a deep link) parked a PREPEND for the current
    /// render while <see cref="ChatAutoScroll.Never"/> is active: the mode itself is consumed
    /// silently there, but the scroller's <c>afterUpdate("prepend")</c> bookkeeping must still
    /// run so the expansion's anchor restore keeps the reading position — the expansion is a DOM
    /// prepend the component itself made, not an auto-scroll.
    /// </summary>
    private bool _pendingWindowPrepend;

    /// <summary>Test-visible state: is a render window active for this component instance?</summary>
    internal bool RenderWindowIsActive => _window is not null;

    /// <summary>Test-visible state: the window's current start index (−1 when inactive).</summary>
    internal int RenderWindowStart => _window?.Start ?? -1;

    /// <summary>The effective (clamped) size of the render window; null when inactive.</summary>
    private int? EffectiveRenderWindowSize
        => RenderWindowSize is { } requested ? Math.Max(ChatRenderWindow.MinSize, requested) : null;

    /// <summary>The effective (clamped) expansion step.</summary>
    private int EffectiveRenderWindowStep => Math.Max(ChatRenderWindow.MinStep, RenderWindowStep);

    /// <summary>
    /// The keyed root rows of the RENDER WINDOW: the contiguous <c>[Start, count)</c> slice of
    /// the root sequence. The per-row inputs (index, first-in-group) come from
    /// <see cref="RootEntries"/> — computed over the FULL sequence BEFORE the slice, so grouping
    /// does not change at the window edge (rule g). With no window (the default) this is
    /// <see cref="RootEntries"/> verbatim.
    /// </summary>
    private IEnumerable<(ChatMessage Message, int Index, bool IsFirstInGroup)> WindowedRootEntries()
    {
        var start = _window?.Start ?? 0;
        foreach (var entry in RootEntries())
        {
            // This render runs the row loop while a deep-link wait pends → it is ALLOWED to
            // release that wait once its after-render bookkeeping ran (the gate below).
            if (_renderWindowApplied is not null) _renderWindowArmed = true;
            if (entry.Index >= start) yield return entry;
        }
    }

    /// <summary>
    /// Per-parameter-set window sync — the hook <see cref="DetectScrollChange"/> calls after it
    /// classified the change (synchronously, before the trackers move). A no-op while
    /// <see cref="RenderWindowSize"/> is unset: the window stays <see langword="null"/> and every
    /// row renders (today's behavior, byte-identical markup).
    /// </summary>
    private void SyncRenderWindow(List<ChatMessage> roots, ScrollChangeMode mode)
    {
        // A fresh render re-arms the deep-link release gate only when its row loop actually runs
        // (see <see cref="WindowedRootEntries"/>) — a render that never mounts rows (an emptied
        // list) must not release a pending deep-link wait.
        _renderWindowArmed = false;

        if (EffectiveRenderWindowSize is not { } size)
        {
            // The parameter is unset (or was unset) — no window, every row renders.
            _window = null;
            _windowKeepId = null;
            return;
        }

        if (_window is null
            || _window.Size != size
            || _window.Step != EffectiveRenderWindowStep)
        {
            // First activation or a changed size/step: a fresh window — the next rules run from
            // the reset baseline (the keep-id is dropped with it).
            _window = new ChatRenderWindow(size, EffectiveRenderWindowStep);
            _windowKeepId = null;
        }

        var count = roots.Count;
        var keepIndex = IndexOfId(roots, _windowKeepId);
        var protectedIndex = ProtectedRenderWindowIndex(roots);

        if (_windowKeepId is null || mode == ScrollChangeMode.Reset || count == 0)
        {
            // (a) First sight of a list / a replaced list: the last `size` rows.
            _window.Reset(count);
        }
        else if (mode == ScrollChangeMode.Append)
        {
            // (b) pinned → slide with the tail (trim the top); (c) away → keep the window, the
            // DOM above the viewport must not change — the trim waits for the return to the
            // bottom (the OnScrollStateChanged hook).
            _window.OnAppend(count, keepIndex, pinned: _atBottom, protectedIndex);
        }
        else if (mode == ScrollChangeMode.Prepend)
        {
            // (e) A host prepend of older history: keep the previously-first rendered row and
            // expand one step above it (the rows it already rendered all survive — the window
            // always runs to the tail).
            _window.OnPrepend(count, keepIndex, protectedIndex);
        }
        else
        {
            // No boundary moved (typing-only, parameter-only, a middle edit): re-locate the
            // kept row in case indices shifted underneath the window.
            _window.Relocate(count, keepIndex, protectedIndex);
        }

        _windowKeepId = count > 0 ? roots[_window.Start].Id : null;
    }

    /// <summary>
    /// Rule (f): the smallest index that may never be trimmed out of the window — the row under
    /// edit and the root of the active thread panel. <see cref="int.MaxValue"/> when neither is
    /// present (no constraint).
    /// </summary>
    private int ProtectedRenderWindowIndex(List<ChatMessage> roots)
    {
        var protectedIndex = int.MaxValue;
        if (_editingId is not null)
        {
            var index = IndexOfId(roots, _editingId);
            if (index >= 0) protectedIndex = Math.Min(protectedIndex, index);
        }

        var threadRoot = EffectiveActiveThread;
        if (threadRoot is not null)
        {
            var index = IndexOfId(roots, threadRoot);
            if (index >= 0) protectedIndex = Math.Min(protectedIndex, index);
        }

        return protectedIndex;
    }

    /// <summary>
    /// Rule (c): the return to the bottom trims the window back to its size (the append that
    /// arrived while the user read above the bottom is no longer "above the viewport"). The trim
    /// also REFRESHES the tracked keep-id onto the new start row — otherwise the next parameter
    /// set's <see cref="SyncRenderWindow"/> would <see cref="ChatRenderWindow.Relocate"/> snap
    /// the window back to the untrimmed start and undo the trim. Returns whether the window
    /// moved, so the caller re-renders only then.
    /// </summary>
    private bool TrimRenderWindowOnReturnToBottom()
    {
        if (_window is null) return false;
        var roots = RootMessages().ToList();
        var trimmed = _window.TrimToTail(roots.Count, ProtectedRenderWindowIndex(roots));
        if (trimmed) RefreshWindowKeepId();
        return trimmed;
    }

    /// <summary>
    /// Expands the window one step UP (rule d) and parks the change as a render PREPEND so the
    /// scroller restores the reading position on its anchor. The optional focus anchor is the
    /// previously-first rendered row for a CLICK-initiated expansion (the same contract as the
    /// load-older prepend: the button is about to be aria-disabled, the row takes the focus).
    /// </summary>
    private void ExpandRenderWindowUpward(string? focusAnchorId)
    {
        if (_window is null || !_window.ExpandUpOneStep()) return;
        if (focusAnchorId is not null) _loadOlderFocusAnchorId = focusAnchorId;
        RefreshWindowKeepId();
        MergePendingMode(ScrollChangeMode.Prepend);
        // The expansion is a DOM prepend the component made itself: even under
        // <see cref="ChatAutoScroll.Never"/> the scroller's afterUpdate("prepend") bookkeeping
        // must run so the anchor restore keeps the reading position (fix: the Never branch
        // consumes the mode silently — the flag carries the prepend to it).
        _pendingWindowPrepend = true;
        StateHasChanged();
    }

    /// <summary>
    /// The first RENDERED root row id (the row at the window start) — the focus anchor of a
    /// click-initiated expansion; null without a window or an empty list.
    /// </summary>
    private string? FirstRenderedRootId()
    {
        var start = _window?.Start ?? 0;
        var roots = RootMessages().ToList();
        return start < roots.Count ? roots[start].Id : null;
    }

    /// <summary>
    /// Re-reads the id of the row the window keeps at its start (the anchor the NEXT
    /// <see cref="SyncRenderWindow"/> relocates onto). Called after every direct expansion —
    /// <see cref="ExpandRenderWindowUpward"/> and the deep-link <c>ExpandToInclude</c> — because
    /// those bypass <see cref="SyncRenderWindow"/>'s own keep-id bookkeeping; a stale id would
    /// revert the expansion on the next parameter set.
    /// </summary>
    private void RefreshWindowKeepId()
    {
        if (_window is null)
        {
            _windowKeepId = null;
            return;
        }

        var roots = RootMessages().ToList();
        _windowKeepId = _window.Start < roots.Count ? roots[_window.Start].Id : null;
    }

    /// <summary>
    /// Deep-link guard (rule: an anchor outside the window must still be found): when
    /// <paramref name="id"/> is a ROOT message above the window start, the window expands to
    /// include it (a render prepend — the scroller restores the position on its snapshot anchor)
    /// and the caller waits for the render to apply before its JS navigation. An anchor already
    /// covered by a PENDING expansion (a concurrent deep link) shares that first wait instead of
    /// parking a second completion source over it — overwriting would strand the first waiter
    /// forever. A thread reply or an unknown id changes nothing — the JS call keeps today's
    /// semantics (the thread panel is not windowed; an unknown id is simply not found).
    /// </summary>
    private async Task EnsureMessageInRenderWindowAsync(string id)
    {
        if (_window is null || _disposed) return;
        var roots = RootMessages().ToList();
        var index = IndexOfId(roots, id);
        if (index < 0) return;

        // Already inside the window: either rendered — or covered by an expansion of a
        // concurrent deep link that has not rendered yet, in which case THIS waiter shares the
        // pending wait (the row arrives with the very same render).
        if (index >= _window.Start)
        {
            if (_renderWindowApplied is { } pending) await pending.Task;
            return;
        }

        if (!_window.ExpandToInclude(index)) return;
        // The expansion bypassed SyncRenderWindow, so the tracked keep-id must move to the new
        // start row NOW — a stale id would let the next parameter set's Relocate snap the
        // window back to the pre-expansion start and unmount the anchor again.
        _windowKeepId = roots[index].Id;
        MergePendingMode(ScrollChangeMode.Prepend);
        // The expansion's anchor restore runs even under ChatAutoScroll.Never (see
        // ApplyScrollAfterRenderAsync's Never branch).
        _pendingWindowPrepend = true;

        // Wait for the render that mounts the anchor row. The wait is SHARED: several deep
        // links may expand the same render, and the ??= keeps the FIRST completion source so
        // every waiter completes on the one render that mounts all the anchors (the completion
        // is parked on the after-render scroll bookkeeping and dispose completes it too — a
        // waiter never hangs).
        var tcs = _renderWindowApplied ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StateHasChanged();
        try
        {
            await tcs.Task;
        }
        finally
        {
            if (ReferenceEquals(_renderWindowApplied, tcs)) _renderWindowApplied = null;
        }
    }

    /// <summary>
    /// Completes a pending deep-link wait — called from the after-render scroll bookkeeping
    /// (every path, including the suppressed <see cref="ChatAutoScroll.Never"/> one) and from
    /// dispose, so the waiter's JS navigation runs after the anchor exists or not at all. A
    /// RENDER releases only when the render actually ran the row loop while the wait was
    /// pending (the <see cref="_renderWindowArmed"/> gate — a render that never mounted the
    /// anchor must not release the navigation early); dispose releases UNCONDITIONALLY, because
    /// after it no render is coming that could arm the gate.
    /// </summary>
    private void SignalRenderWindowApplied(bool force = false)
    {
        var armed = _renderWindowArmed;
        _renderWindowArmed = false;
        if (!force && !armed) return;

        var tcs = _renderWindowApplied;
        _renderWindowApplied = null;
        tcs?.TrySetResult();
    }
}

/// <summary>
/// The dependency-free state of the TmChat render window (FZE-14, decision
/// <c>TMCHAT-RENDER-WINDOW</c>): pure index/step arithmetic over the root sequence — no Blazor
/// and no message types — so the window rules are unit-testable in isolation. The window is the
/// contiguous <c>[Start, Count)</c> slice of the root sequence: it always runs to the TAIL, and
/// only ever GROWS at the top, which is what makes every expansion a "prepend" for the DOM and
/// lets it reuse the chat.js anchor restore. All size/step inputs are clamped
/// (<see cref="MinSize"/>/<see cref="MinStep"/>).
/// </summary>
internal sealed class ChatRenderWindow
{
    /// <summary>Lower clamp of <c>RenderWindowSize</c>: a window smaller than 20 rows is pointless.</summary>
    public const int MinSize = 20;

    /// <summary>Lower clamp of <c>RenderWindowStep</c>: one row per expansion would re-render per scroll pixel.</summary>
    public const int MinStep = 10;

    /// <summary>Creates the window with the requested (clamped) size and step.</summary>
    public ChatRenderWindow(int requestedSize, int requestedStep)
    {
        Size = Math.Max(MinSize, requestedSize);
        Step = Math.Max(MinStep, requestedStep);
    }

    /// <summary>The clamped window size — the number of rows the window keeps at minimum.</summary>
    public int Size { get; }

    /// <summary>The clamped expansion step — the rows one upward expansion adds.</summary>
    public int Step { get; }

    /// <summary>Inclusive start of the window in the root sequence; the window runs to the tail.</summary>
    public int Start { get; private set; }

    /// <summary>The root count as of the last update (the exclusive end of the window).</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Whether the window holds rows an AWAY-append left above the size line — the only excess
    /// the rule-(c) return-to-bottom trim reclaims. A deep-link or top-edge EXPANSION mounts its
    /// rows deliberately (the anchor must stay mounted for the navigation/reading) and sets no
    /// claim, so the restore's own at-bottom report cannot undo it; the next pinned append
    /// (rule b) slides the window back to the tail regardless.
    /// </summary>
    public bool PendingTrim { get; private set; }

    /// <summary>
    /// Rule (a): the first sight of a list and a replaced list window the last
    /// <see cref="Size"/> rows (the whole list when it is shorter). The start is 0 for an empty
    /// list.
    /// </summary>
    public void Reset(int count)
    {
        Count = count;
        Start = Math.Max(0, count - Size);
        PendingTrim = false;
    }

    /// <summary>
    /// Rule (b)/(c): an append at the tail. With the viewport PINNED to the bottom the window
    /// slides with the tail — the start is trimmed to <c>count - Size</c> (never past a
    /// protected index); a vanished tracked row keeps the PINNED tail fallback too. Away from
    /// the bottom the start is RELOCATED onto the tracked row (the host may have shifted
    /// indices) and nothing is trimmed: the DOM above the viewport must not change. When the
    /// tracked row is GONE the start is KEPT (clamped into the new list, never past a protected
    /// index) — falling back to the tail would jump the reading position.
    /// </summary>
    public void OnAppend(int count, int keepIndex, bool pinned, int protectedIndex)
    {
        Count = count;
        if (pinned)
        {
            Start = Math.Min(Math.Max(0, count - Size), protectedIndex);
            PendingTrim = false;
            return;
        }

        // An append while the user reads above the bottom: nothing is trimmed now, so the
        // excess is reclaimable on the return to the bottom (rule c).
        Start = keepIndex >= 0
            ? Math.Clamp(keepIndex, 0, Math.Max(0, count - 1))
            : Math.Min(Math.Clamp(Start, 0, Math.Max(0, count - 1)), protectedIndex);
        PendingTrim = true;
    }

    /// <summary>
    /// Rule (e): the host prepended older history — <paramref name="keepIndex"/> is the new index
    /// of the row the window was keeping. The window expands one <see cref="Step"/> ABOVE that
    /// row (which keeps every previously rendered row: the slice still runs to the tail) and
    /// never trims past a protected index. A vanished tracked row keeps the current start
    /// (clamped into the new list). A prepend mounts its rows deliberately, so it drops any
    /// pending away-append claim.
    /// </summary>
    public void OnPrepend(int count, int keepIndex, int protectedIndex)
    {
        Count = count;
        Start = keepIndex >= 0
            ? Math.Min(Math.Max(0, keepIndex - Step), protectedIndex)
            : Math.Min(Math.Clamp(Start, 0, Math.Max(0, count - 1)), protectedIndex);
        PendingTrim = false;
    }

    /// <summary>
    /// No boundary moved (a typing-only or parameter-only set, a middle edit): re-locate the
    /// kept row; when it is GONE keep the start (clamped into the new list — a vanished row
    /// must not jump the reader to the tail). Never expands, never trims past a protected
    /// index.
    /// </summary>
    public void Relocate(int count, int keepIndex, int protectedIndex)
    {
        Count = count;
        Start = keepIndex >= 0
            ? Math.Min(Math.Clamp(keepIndex, 0, Math.Max(0, count - 1)), protectedIndex)
            : Math.Min(Math.Clamp(Start, 0, Math.Max(0, count - 1)), protectedIndex);
    }

    /// <summary>
    /// Rule (d): one expansion step UP — <c>Start = max(0, Start - Step)</c>. Returns whether
    /// the start moved (already at 0 → nothing to do). The expansion mounts its rows
    /// deliberately, so it drops any pending away-append claim (the restore's own at-bottom
    /// report must not reclaim the expansion).
    /// </summary>
    public bool ExpandUpOneStep()
    {
        var target = Math.Max(0, Start - Step);
        if (target == Start) return false;
        Start = target;
        PendingTrim = false;
        return true;
    }

    /// <summary>
    /// Deep-link expansion: include <paramref name="index"/> in the window. Returns whether the
    /// start moved (the row was already rendered → nothing to do). The expansion mounts its rows
    /// deliberately, so it drops any pending away-append claim.
    /// </summary>
    public bool ExpandToInclude(int index)
    {
        if (index >= Start) return false;
        Start = Math.Max(0, index);
        PendingTrim = false;
        return true;
    }

    /// <summary>
    /// Rule (c): the return to the bottom — reclaim the rows an AWAY-append left above the size
    /// line (never past a protected index). A window grown by a deep-link or top-edge expansion
    /// (no <see cref="PendingTrim"/>) is deliberately left mounted. Returns whether the start
    /// moved.
    /// </summary>
    public bool TrimToTail(int count, int protectedIndex)
    {
        if (!PendingTrim) return false;
        Count = count;
        var target = Math.Min(Math.Max(0, count - Size), protectedIndex);
        if (target <= Start)
        {
            PendingTrim = false; // nothing above the size line to reclaim
            return false;
        }

        Start = target;
        PendingTrim = false;
        return true;
    }
}
