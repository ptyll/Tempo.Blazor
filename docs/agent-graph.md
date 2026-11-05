# TmAgentGraph — agents and the tools they call as one network

`TmAgentGraph` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) draws agents, the tools they call, the
resources they read and the human in the loop as **one network**: a box per node, a curve per connection, and a short
pulse on the curve every time a new call arrives.

The shared identity model, the live-data contract (`AgentRef`, `AgentActivityStatus`, `AgentEventStream<TEvent>`,
`AgentRenderThrottle`) and the accessibility rules every agent visualization follows are documented once in
[ai-agents.md](ai-agents.md). This page is the `TmAgentGraph` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmAgentGraph Snapshot="_snapshot"
              Events="_calls"
              OnGapDetected="ReloadAsync"
              @bind-SelectedNodeId="_selectedNodeId"
              Title="Reconciliation run">
    <DetailTemplate Context="node">
        <h3>@node.Label</h3>
        <p>@node.Kind · @node.Status</p>
    </DetailTemplate>
</TmAgentGraph>
```

## The models

Everything a graph surface draws lives in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models`), so a backend or a
worker can model the same graph without pulling a renderer in.

| Type | What it is |
|---|---|
| `AgentGraphSnapshot` | The COMPLETE state at one moment: `Sequence`, `Nodes`, `Edges`. A value; the component never mutates it. `Apply(calls)` folds an incremental tail into a NEW snapshot. |
| `AgentGraphNode` | One node: `Id`, `Label`, `Kind`, `Status`, `Group?`, `Agent?`. An AGENT node may carry the shared `AgentRef` identity (palette slot + initials); a tool, a resource and a human carry none. |
| `AgentGraphEdge` | One connection: `Id`, `From`, `To`, `CallCount`, `LastCallAt?`, `IsActive`. `From == To` is a self loop, which is legal and drawn beside the node. |
| `AgentGraphNodeKind` | `Agent` \| `Tool` \| `Resource` \| `Human` — the kind decides the node's column, its glyph and its word. |
| `AgentGraphCall` | The incremental change: `Sequence`, `EdgeId`, `At?`, `Count` (a batched feed may report several identical calls at once). |

A blank node or edge `Id` is generated at construction (the same rule `AgentRef` follows), so a graph built in a loop
still renders; a host that wants a stable selection supplies its own id. A negative `CallCount` is clamped to zero, so
an edge only ever grows and a stroke width can never be derived from a negative number.

### The live contract

`Snapshot` is the baseline; `Events` is the tail. The component feeds the batch through the shared
`AgentEventStream<AgentGraphCall>`:

- **Ordered** — a batch is sorted by `Sequence`, so a merged page cannot apply a call before the edge it addresses.
- **Idempotent** — an event whose sequence was already applied is dropped; passing the same batch twice changes
  nothing (a hub genuinely redelivers after a reconnect).
- **Contiguous** — a hole rejects the WHOLE batch, `OnGapDetected` fires once with the first sequence the stream does
  not have, and the surface announces that live updates are behind instead of painting stale data. A gap repaints at
  once, because the reader has to be told. The documented repair is the host's own: load a new snapshot and pass it
  (with its sequence).

Every accepted call is folded into the view **immediately** — `Count` is added to the addressed edge, `LastCallAt`
moves forward (never back) and the edge turns active — and it lights the edge up for `PulseDuration`. Only the newly
accepted tail is applied, never a replay of everything a long session ever sent, so a parameter set costs the size of
the batch rather than the length of the run. Only the PAINT is gated (`RenderThrottle`, 100 ms by default).

A `PulseDuration`/`MaxPulses` pair bounds the animation: at most `MaxPulses` edges pulse at once and the NEWEST calls
win, so a fan-out burst cannot light up the whole picture. `PulseDuration` is written onto the surface root as
`--tm-agent-graph-pulse-duration`, so the stylesheet's animation length and the timer that removes the class read ONE
value — a host that changes the window never has to restyle the knob to keep the movement and the highlight in step.

## The layout

`AgentGraphLayout` is pure C#: no stylesheet, no JavaScript and no clock — every geometric rule is unit-tested
(`AgentGraphLayoutTests`) and the same snapshot always produces the same numbers, which a live surface re-running it on
every paint depends on.

- **Columns follow the kind.** `Human` asks on the left, `Agent` answers, a `Resource` follows and a `Tool` stands on
  the right. An EMPTY column is collapsed instead of leaving a gap.
- **Every edge points forward.** A longest-path refinement pushes a target right of its source, so an agent delegating
  to another agent lands one column right instead of drawing a line back across the picture. A tool feeding an agent
  pushes the AGENT right, because that is the direction the data flows.
- **Cycles are broken deterministically.** A depth-first pass in declaration order marks the edge that closes a cycle
  as a BACK edge; it is still drawn, but below the boxes instead of through them.
- **Rows are ordered by a barycenter heuristic.** Four alternating sweeps move a node toward the average position of
  its neighbours, which is what stops the curves from crossing; every step is stable, so the result is a function of
  the snapshot alone.
- **Groups stay contiguous.** Members of one `Group` are kept in one run inside their column and get ONE labelled box
  around them (the padding leaves room for the label above the first member). An ungrouped node never joins a box.
- **The thickness is a LOG scale.** `EdgeWidth(count, maxCount)` spends the width range between 1.5 px and 7 px on
  `log(1 + count) / log(1 + maxCount)`, so ten calls out of a hundred sit at half the width range instead of at a tenth
  of it: a quiet connection stays visible beside a busy one.
- **A host may pin a node.** `NodePositions` maps a node id to an `AgentGraphPosition`; a pinned node keeps its exact
  coordinate, is flagged `data-pinned` and the canvas grows so its box is never cut off. Everything else is still laid
  out around it.

## The component

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Snapshot` | `AgentGraphSnapshot?` | `null` | The complete state. Null renders the localized empty state. |
| `Events` | `IReadOnlyList<AgentGraphCall>?` | `null` | The incremental tail applied on top of the snapshot. |
| `OnGapDetected` | `EventCallback<long>` | — | A stream hole; the first sequence the stream does not have. |
| `Title` / `ShowTitle` / `HeadingLevel` | `string?` / `bool` / `int` | `null` / `true` / `3` | The heading; the level is clamped to 2–6. |
| `ViewMode` / `ViewModeChanged` | `AgentGraphViewMode` | `Graph` | `Graph` (drawn) or `Table` (the text alternative). |
| `SelectedNodeId` / `SelectedNodeIdChanged` | `string?` | `null` | Two-way selection (`@bind-SelectedNodeId`). |
| `DetailTemplate` | `RenderFragment<AgentGraphNode>?` | `null` | The host's detail for the selected node. |
| `ShowZoom` | `bool` | `true` | The +/−/fit controls — the WCAG 2.5.7 alternative to the gesture. |
| `Zoom` / `ZoomChanged` | `double` | `1` | The scale, clamped to `MinZoom`–`MaxZoom` (0.5–2.5). |
| `MinZoom` / `MaxZoom` | `double` | `0.5` / `2.5` | The allowed window. |
| `NodePositions` | `IReadOnlyDictionary<string, AgentGraphPosition>?` | `null` | The nodes the host pinned, by id. |
| `MaxPulses` | `int` | `6` | How many edges may pulse at once (the newest win). |
| `PulseDuration` | `TimeSpan` | `600 ms` | How long one pulse stays on its edge; also written onto the root as `--tm-agent-graph-pulse-duration`. |
| `Culture` | `CultureInfo?` | `CurrentCulture` | The shape of the "last call" time, never its value. |
| `TimeProvider` | `TimeProvider?` | container → system | The clock of the pulse window and the paint gate. |
| `RenderThrottle` | `TimeSpan?` | `null` → 100 ms | The paint-gate window; `Zero` paints every batch. |
| `Class` / `AdditionalAttributes` | — | — | Root hooks, plus the inherited `DataTestId` / `TestIdPrefix`. |

Public members: `View` (the snapshot the surface renders), `SelectedNode`, `ZoomPercent`, and the methods
`SelectNodeAsync(id)`, `ZoomInAsync()`, `ZoomOutAsync()`, `FitToViewAsync()`.

## The drawing

The scene is the scrollport (`div.tm-ai-agent-graph__viewport`) — the ONE labelled `role="group"`, named by the
localized "Agent graph" and described by its instructions. The `<svg>` inside it is an unlabelled container: a second
group with the same name would repeat the surface and its instructions for a screen reader, and a second tab stop would
sit in front of the graph's own.

- **Nodes are native buttons inside a `<foreignObject>`.** That is what gives a node wrapped text, the shared icon
  components, a real focus ring, native Enter/Space activation and a 44 px coarse-pointer target with no emulation
  anywhere. The roving tabindex keeps the whole graph ONE tab stop and the scrollport is `tabindex="-1"` — focusable
  programmatically (a lost selection hands the focus back to it), never a second stop in front of the nodes; the ARROW
  keys walk the real CONNECTIONS (the neighbour furthest in that direction wins, then the cross-axis distance, then the
  painting order), Home/End jump to the first and the last node.
- **Every node is named by its label, kind, state and call count.** The visible label is ellipsized to two lines, and
  the full label reaches a reader through the button's `title` and its accessible name. A tool, a resource and a human
  paint their kind glyph on the neutral avatar; an agent paints its initials on its own palette slot (`data-color`).
- **A node that needs the reader is painted, not tinted.** `WaitingForApproval` takes the attention tone on the whole
  border AND spells the state out with an icon and a word; a failed node takes the danger tone.
- **A selection highlights its neighbourhood.** The selected node keeps a primary ring, its DIRECT neighbours are
  marked, everything else is dimmed (never hidden) and the connections it touches are highlighted. The dim is a TOKEN
  change (a quieter surface and border) rather than an opacity, because a dimmed node is still an active button and its
  label has to keep its contrast; only the decorative avatar steps back.
- **The connections are decoration for a screen reader** (`aria-hidden`): the node names carry the call counts and the
  table view lists every connection with its numbers, so nothing is announced twice.

## Accessibility

- **The table is a peer, not a fallback.** `ViewMode="Table"` renders two tables — every connection (from, to, kind,
  calls, last call) and every node (name, kind, state, calls) — with the same data as the drawing, reachable by
  keyboard, and a node row selects exactly as a node does.
- **One live region.** A selection, a zoom change, a lost selection and a stream hole are announced by the surface's
  single polite `role="status"`; a node, an icon and a chip announce nothing, so no change is spoken twice.
- **Focus is managed.** A selected node the host removes clears the selection and hands the focus back to the
  scrollport instead of dropping the reader onto the document body.
- **Zoom has a non-gesture path.** The +/−/fit buttons are always there; the pinch and ctrl+wheel gesture is an
  enhancement. The page scroll an ARROW key may produce is accepted rather than suppressed — a render-time
  `@onkeydown:preventDefault` cannot be per-key and would swallow Tab and the button's own native activation (the same
  decision `TmTabs` and `TmTopBar` document).
- **Reduced motion.** The call pulse is the ONE animated subject and its `prefers-reduced-motion` block removes the
  movement while keeping a still, dashed, Highlight-toned highlight — a reader who asked for less motion still learns
  that something happened.
- **High contrast.** Under `forced-colors` the curves and group boxes are handed to `CanvasText`, the highlighted
  connection, the call pulse and the system-owned marks to `Highlight`. Selection and approval wear a `Highlight`
  **border**, while focus owns the **outline** (3 px, offset 3 px) — a focused selected node still reads as focused,
  and no state is told apart from another only by a colour. The arrow heads take `CanvasText` too, because an SVG
  `fill` is not remapped by the system palette and the direction cue would otherwise fade out of its own curve, and
  every dim is dropped (`opacity: 1`), since opacity over system colours lowers contrast unpredictably.

## Keyboard

| Key | What it does |
|---|---|
| `Tab` | Reaches the graph's ONE node stop. The View switcher and the zoom buttons are their own normal stops; the scrollport is never a stop. |
| `ArrowRight` / `ArrowLeft` / `ArrowDown` / `ArrowUp` | Moves along a CONNECTION in that direction; a node with no connection that way keeps the focus. |
| `Home` / `End` | Jumps to the first / last node by painting order. |
| `Enter` / `Space` | Activates the focused node (native button behaviour, exactly once). |

## CSS

The partial is `_ai-agent-graph.css`, imported by the package entry stylesheet. Every rule reads a `--tm-*` token (no
colour literal, no raw palette step, no `[data-theme]` block) and the component's own knobs are declared on the root:

| Knob | Default | Meaning |
|---|---|---|
| `--tm-agent-graph-node-fill` | `--tm-bg-surface` | The node's background. |
| `--tm-agent-graph-node-border` | `--tm-border-color` | The node's resting border. |
| `--tm-agent-graph-node-tone` | `--tm-border-color-strong` | The state tone a modifier SETS and the base rule READS. |
| `--tm-agent-graph-node-avatar` / `-ink` | surface-secondary / text-secondary | The neutral avatar (an agent overrides both through `data-color`). |
| `--tm-agent-graph-edge` / `-active` / `-pulse` / `-selected` | muted / primary / primary / primary-text | The connection tones. |
| `--tm-agent-graph-group-fill` / `-border` | surface-secondary / border | The group box. |
| `--tm-agent-graph-max-block-size` | `30rem` | How tall the scrollport grows before it scrolls. |
| `--tm-agent-graph-pulse-duration` | the `PulseDuration` parameter (`600ms` in the stylesheet) | How long the pulse movement lasts. The component writes the parameter's value inline, so the stylesheet's declaration is only the fallback for a host loading the partial without the component. |

The drawing is the ONE horizontal scroll owner (`overflow: auto` on the viewport), panning is native scrolling, the
group boxes and the curves never steal a click (`pointer-events: none`), and a coarse pointer grows the zoom buttons
and the table's row actions to `--tm-touch-target`.

## JavaScript

The package ships one ES module for this surface — `_content/Tempo.Blazor.AI/js/tm-agent-graph.js` — imported through
`IJSObjectReference` (no `<script>` tag, no eval):

- `attachGraphGesture(viewport, dotNetRef)` imports the CORE `pointer-gesture.js` helper across the static-asset
  boundary (`loadPinch()`), so a graph, a lane surface and the PDF viewer pinch the same way. It reports the gesture's
  start/change/end to .NET and applies the two-finger PAN to the scrollport itself: a pan is a scroll position, not
  component state.
- `measureViewport(element)` returns `[width, height]` of the content box, which is what `FitToViewAsync` derives the
  scale from; where nothing can be measured the component keeps its zoom instead of jumping to a guess.

Every call is guarded: without JavaScript (bUnit, prerender, a disconnected circuit) the zoom BUTTONS keep working and
only the gesture is missing. Node tests: `npm run test:ai`.

## Related pages

- [TmAgentLanes](agent-lanes.md) — the same run on a shared time axis.
- [ai-agents.md](ai-agents.md) — the shared identity, palette and live-data contract.
- [TmAgentBadge](ai-agents.md) — the identity line every surface composes.
