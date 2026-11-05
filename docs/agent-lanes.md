# TmAgentLanes — a multi-agent run as lanes on one time axis

`TmAgentLanes` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) draws a multi-agent run as one **lane per
agent** on a shared time axis: a block per piece of work, the review verdict a block carries, handoff arrows between
the lanes and the current moment as a vertical line.

The shared identity model, the live-data contract (`AgentRef`, `AgentActivityStatus`, `AgentEventStream<TEvent>`,
`AgentRenderThrottle`) and the accessibility rules every agent visualization follows are documented once in
[ai-agents.md](ai-agents.md). This page is the `TmAgentLanes` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmAgentLanes Snapshot="_snapshot"
              Events="_events"
              OnGapDetected="ReloadAsync"
              @bind-SelectedBlockId="_selectedBlockId"
              Title="Release run">
    <DetailTemplate Context="block">
        <h3>@block.Label</h3>
    </DetailTemplate>
</TmAgentLanes>
```

## The models

Everything a lane surface draws lives in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models`), so a backend or a
worker can model the same run without pulling a renderer in.

| Type | What it is |
|---|---|
| `AgentLanesSnapshot` | The COMPLETE state at one moment: `Sequence`, `Agents`, `Blocks`, `Handoffs`. A value; the component never mutates it. `Apply(events)` folds an incremental tail into a NEW snapshot. |
| `AgentLaneBlock` | One piece of work by one agent: `Id`, `AgentId`, `Start`, `End?`, `Status`, `Label`, `Kind`, `Marker?`. `End == null` means *still running* — the surface draws it up to its own "now". |
| `AgentHandoffLink` | One handover: `FromBlockId` | `FromAgentId`, `ToAgentId`, `At`, `Label?`. The block anchor wins; the agent anchor is the fallback for a block the snapshot does not contain. |
| `AgentLaneBlockKind` | `Task` \| `Tool` \| `Message` \| `Review` \| `Handoff` — painted as a glyph AND a localized word. |
| `AgentLaneBlockMarker` | `Approval` \| `ChangesRequested` \| `Review` — the verdict a review left, painted as an icon AND a word. `null` means "no verdict yet", not "neutral". |
| `AgentLaneEvent` + `AgentLaneEventKind` | The incremental change: `BlockUpserted` (`Block`), `BlockRemoved` (`BlockId`), `HandoffAdded` (`Handoff`), each with a monotonic `Sequence`. |

A blank block `Id` is generated at construction (the same rule `AgentRef` follows), so a list built in a loop still
renders; a host that wants a stable selection supplies its own id. An `End` earlier than `Start` is clamped by
`AgentLaneBlock.EndAt`, so no surface can derive a negative duration.

### The live contract

`Snapshot` is the baseline; `Events` is the tail. The component feeds the batch through the shared
`AgentEventStream<AgentLaneEvent>`:

- **Ordered** — a batch is sorted by `Sequence`, so a merged page cannot apply a block before the event that opened it.
- **Idempotent** — an event whose sequence was already applied is dropped; passing the same batch twice changes
  nothing (a hub genuinely redelivers after a reconnect).
- **Contiguous** — a hole rejects the WHOLE batch, `OnGapDetected` fires once with the first sequence the stream does
  not have, and the surface announces that live updates are behind instead of painting stale data. The documented
  repair is the host's own: load a new snapshot and pass it (with its sequence).

The component re-bases its stream when the snapshot's `Sequence` changes (a reload), and keeps the view it already
has when a host rebuilds an equal snapshot (same sequence). Every accepted event is folded into that view IMMEDIATELY,
once — only the newly accepted tail is applied, never a replay of everything a long session ever sent, so a parameter
set costs the size of the batch rather than the length of the run. Only the PAINT is gated.

**Handoffs are append-only and identified by ALL their fields** (`FromBlockId`, `FromAgentId`, `ToAgentId`, `At`,
`Label`): a link that was already applied is dropped, and a host that re-sends the "same" handoff with an edited
`Label` produces a SECOND arrow. That is deliberate — a lane surface draws history, it never rewrites it — so a host
that needs an editable handoff models it as a `BlockUpserted` block (a block is identified by its `Id` alone and
replaces the previous version). The table view lists every link, so nothing is silently merged.

### Throttling the paint, never the state

`RenderThrottle` (default `null` → the shared 100 ms window, `TimeSpan.Zero` = paint everything) collapses a burst of
events into one paint. The STATE absorbs every event, so a snapshot read at any moment is complete; a held-back batch
schedules one trailing paint at the end of the window on the injected `TimeProvider`. A paint the component makes for
its own reason (a snapshot, a selection, a zoom, a live tick) opens a new window, so the burst after it is collapsed
too.

## The layout

`AgentLanesLayout` is pure C#: no stylesheet, no JavaScript and no clock of its own — the caller passes the moment to
draw for, so every geometric rule is unit-tested (`AgentLanesLayoutTests`).

- **Range and ticks.** The range is derived from the blocks (an open block reaches "now"), padded a little and then
  aligned to a "nice" step from a fixed ladder (1 s … 1 week), so a label reads `09:10` and never `09:07:23`. A very
  short run is widened symmetrically to a readable minimum; a run with no blocks still draws an axis around "now".
  The axis is drawn in the offset of the supplied `now`, and a range that crosses midnight makes every label carry
  the date.
- **Geometry is PIXELS.** `PixelsPerHour` is the horizontal scale and `RowHeight` the sub-row height, because the
  component paints blocks at absolute positions and an SVG connector has to line up with them exactly.
- **Overlaps become sub-rows.** Work that overlaps in time inside one lane is stacked instead of covering itself, and
  a row is reused as soon as it is free again.
- **Undeclared agents still get a lane.** A block naming an agent the snapshot forgot is appended after the declared
  lanes (named through the localized fallback), so a run is never silently incomplete.
- **Handoffs.** A connector starts at the handed-over block's trailing edge (or at the moment of the handoff on the
  source lane), bends at the horizontal midpoint and ends on the target lane at that moment. A link that cannot be
  resolved to two lanes is counted, never guessed.

## The component

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Snapshot` | `AgentLanesSnapshot?` | `null` | The complete state. Null renders the localized empty state. |
| `Events` | `IReadOnlyList<AgentLaneEvent>?` | `null` | The incremental tail applied on top of the snapshot. |
| `OnGapDetected` | `EventCallback<long>` | — | A stream hole; the first sequence the stream does not have. |
| `Title` / `ShowTitle` / `HeadingLevel` | `string?` / `bool` / `int` | `null` / `true` / `3` | The heading; the level is clamped to 2–6. |
| `ViewMode` / `ViewModeChanged` | `AgentLanesViewMode` | `Timeline` | `Timeline` (graphical) or `Table` (the text alternative). |
| `SelectedBlockId` / `SelectedBlockIdChanged` | `string?` | `null` | Two-way selection (`@bind-SelectedBlockId`). |
| `DetailTemplate` | `RenderFragment<AgentLaneBlock>?` | `null` | The host's detail for the selected block. |
| `ShowZoom` | `bool` | `true` | The +/−/fit controls — the WCAG 2.5.7 alternative to the gesture. |
| `PixelsPerHour` / `PixelsPerHourChanged` | `double` | `600` | The scale, clamped to `MinPixelsPerHour`–`MaxPixelsPerHour` (24–2400). |
| `MinPixelsPerHour` / `MaxPixelsPerHour` | `double` | `24` / `2400` | The allowed window. |
| `RowHeight` | `double` | `48` | One sub-row, leaving a 44 px block inside it. |
| `LabelWidth` | `double` | `176` | The sticky agent column; "fit to width" subtracts it from the room the reader has. |
| `ShowNowLine` | `bool` | `true` | The vertical "now" line. |
| `ShowHandoffs` | `bool` | `true` | The handoff arrows (the table keeps them either way). |
| `Culture` | `CultureInfo?` | `CurrentCulture` | The shape of every timestamp, never the values. |
| `TimeProvider` | `TimeProvider?` | container → system | The clock of the live line and the paint gate. |
| `RenderThrottle` | `TimeSpan?` | `null` → 100 ms | The paint-gate window; `Zero` paints every batch. |
| `Class` / `AdditionalAttributes` | — | — | Root hooks, plus the inherited `DataTestId` / `TestIdPrefix`. |

Public members: `View` (the snapshot the surface renders), `SelectedBlock` (the selected block, if still present),
`ZoomPercent`, and the methods `SelectBlockAsync(id)`, `ZoomInAsync()`, `ZoomOutAsync()`, `FitToViewAsync()`.

### Zoom

`PixelsPerHour` (default 600 — ten pixels per minute, the scale an agent run is read at) is the whole zoom: the buttons multiply/divide by 1.5 and announce the new level in the component's live
region, `FitToViewAsync` measures the scrollport (through the package's `tm-agent-lanes.js` module) and divides the
room by the run's length — keeping the current zoom where nothing can be measured, so it never jumps to a guess.

Ctrl+wheel (a trackpad pinch) and a two-finger pinch change the scale through the **core**
`_content/Tempo.Blazor/js/pointer-gesture.js` helper, imported across the static-asset boundary by
`tm-agent-lanes.js` — a second drag/pinch implementation in a split package would drift from every other surface (see
[touch-interaction.md](touch-interaction.md)). A gesture reports its relative scale per tick but hands the level to the
host once, when it ends, so a bound `PixelsPerHour` does not re-render mid-gesture. Plain wheel is left alone.

## The text alternative

`ViewMode="Table"` renders exactly the same snapshot as two plain tables — the blocks (agent, activity, kind, start,
end, state, marker) and the handoffs (from, to, at, label) — with the same row action selecting a block. A screen
reader, a keyboard-only reader or a narrow phone therefore loses no fact, and the timeline is never the only way to
read the run (WCAG 1.1.1 / 1.4.1).

## Accessibility

- **Blocks are native buttons.** Tab walks them in DOM order, Enter/Space select natively (nothing is emulated), and
  the selected one carries `aria-pressed="true"`.
- **Every block is named in full, starting with what is on screen.** The accessible name begins with the ACTIVITY the
  block shows, followed by the agent, the kind, the start, the end (or the localized "Now" for an open block), the
  state and — when present — the marker word. A voice-control user can therefore say the visible label
  (WCAG 2.5.3, label in name). Nothing is colour-only: a state and a marker are always an icon AND a word.
- **The scrollport is a tab stop** (`role="group"`, named by the localized "Agent timeline", described by the
  keyboard instructions), so the horizontal axis is reachable without a pointer.
- **ONE polite live region** announces a selection, a zoom change and a stream hole. The ticking "now" line and the
  decorative connector SVG are `aria-hidden`; the blocks are `aria-pressed`, not live; nothing is announced twice.
- **Coarse pointers** grow the zoom buttons, the table action and a very short block to the 44 px
  `--tm-touch-target`. The block's axis position and the table's exact timestamps stay authoritative.
- **Forced colors** hand every focus ring back to `Highlight`, keep a system-colour edge on each block and paint the
  picked block's ring in `Highlight`, because a tone border and a tinted fill collapse under Windows high contrast.
- **No motion.** The surface declares no transition and no animation: a live run repaints while it streams, and the
  package's reduced-motion enumeration stays exactly as it is.

## CSS

`.tm-ai-agent-lanes`, `__header`, `__title`, `__toolbar`, `__view`, `__zoom`, `__zoom-button(--fit)`, `__zoom-value`,
`__empty`, `__viewport`, `__canvas`, `__axis`, `__axis-label`, `__axis-track`, `__tick(--end)`, `__tick-label`, `__lanes`,
`__handoffs`, `__handoff`, `__arrow-head`, `__lane`, `__lane-head`, `__lane-status(--{tone})`, `__lane-status-icon`,
`__lane-status-text`, `__lane-track`, `__block`,
`__block--{idle|queued|running|approval|succeeded|failed|cancelled|open|selected}`, `__block-kind`, `__block-label`,
`__block-status`, `__block-marker`, `__block-marker-icon`, `__block-marker-text`, `__marker`, `__marker-icon`,
`__marker-text`, `__marker--{approval|changes-requested|review}`, `__now(--end)`, `__now-label`, `__tables`, `__table`,
`__table-activity`, `__table-select`, `__detail`.

Every rule reads a `--tm-*` design token (no colour literal, no raw palette step, no `[data-theme]` block). The
component-local knobs are declared on the root: `--tm-agent-lanes-label-width`, `--tm-agent-lanes-row-height` (written
inline from `LabelWidth` / `RowHeight`), `--tm-agent-lanes-axis-height`, `--tm-agent-lanes-block-fill`,
`--tm-agent-lanes-block-border`, `--tm-agent-lanes-block-tone`, `--tm-agent-lanes-handoff`, `--tm-agent-lanes-now`. A
state modifier SETS `--tm-agent-lanes-block-tone` and the base rule READS it, so source order cannot decide a colour; the lane header names the state that matters
right now on its own line (a queued block in the future never labels a finished agent as "queued").

## Mapping a typical backend

| Backend concept | Library model |
|---|---|
| `OrchestrationSessionAgentDto { ParticipantId, RoleKey, DisplayName }` | `AgentRef(id: ParticipantId, name: DisplayName) { Role = RoleKey, ColorIndex = index }` |
| `PlanPhaseDto` / a turn with `StartedAt` / `CompletedAt` | `AgentLaneBlock(id, agentId, Start) { End = CompletedAt, Status = … }` |
| `ReviewStage`, a verdict | `AgentLaneBlock.Marker` (`Approval`, `ChangesRequested`, `Review`) |
| `RoomMessageDto.Kind` (`Chat` / `Delegation` / `StatusReport`) | `AgentLaneBlockKind` (`Message`, `Handoff`, `Task`) |
| `RoomMessageDto.TargetParticipantId` | `AgentHandoffLink(ToAgentId) { FromBlockId = … }` |
| `RoomHub.MessageAppended` / a one-second poll | `AgentLaneEvent` batches through `AgentEventStream<AgentLaneEvent>` |
| A reload after a gap | a new `AgentLanesSnapshot` with the new `Sequence` |

## Related pages

- [ai-agents.md](ai-agents.md) — the shared identity, palette, live-data and accessibility contract.
- [agent-run-tree.md](agent-run-tree.md), [agent-monitor.md](agent-monitor.md) — the same run as a tree and as a
  live strip.
- [touch-interaction.md](touch-interaction.md) — the shared pointer helpers the zoom gesture imports.
- Demo: `/ai-agents` (the lanes section of the end-to-end AI scenario).
