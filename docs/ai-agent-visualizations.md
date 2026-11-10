# /ai-agent-visualizations — one simulated run, every agent surface

The `/ai-agent-visualizations` demo page renders **one** multi-agent run with **every** visualization surface of
`Tempo.Blazor.AI` at the same time. Where `/ai-agents` walks a scenario end to end (a plan, its run, the model picker,
the context window and the prompt library), this page is the reference for the visualization layer itself: the same
run in time, in a network, split into work, retried, handed over, delegated, scheduled, merged and finally overlaid on
a document.

The page lives in `src/Tempo.Blazor.Demo.SharedUI/Pages/AiAgentVisualizationsPage.razor` and owns its strings in the
demo resource tables (`DemoResources.json`, `.cs`, `.sk`, `.fr`). Every component it renders is generic: nothing in
the library knows this page exists.

## The simulator: a host, not a component

`AiAgentRunSimulator` (`src/Tempo.Blazor.Demo.SharedUI/Services/AiAgentRunSimulator.cs`) plays the part a real
orchestration backend would play. It is a host in the strict sense of the [live-data
contract](ai-agents.md#the-live-data-contract-snapshot--events):

- It owns the **change log**. One applied change is one `AiAgentRunEvent` with a `Sequence` that grows by exactly one,
  so the sequence is contiguous — the property `AgentEventStream<T>` treats as its definition of a delivered stream.
- It owns the **reducer**. Each event folds into an `AiAgentRunState` (lane blocks, handoffs and the fan-out queue); the
  page hands that snapshot to the components, which never mutate it.
- It owns the **clock**. A timer created through the injected `TimeProvider` applies one event per interval: **ten
  events per second at 1× speed**, five at 0,5× and twenty at 2×. A test replaces the clock with
  `FakeTimeProvider` and walks the whole run deterministically.
- It never reaches into the library. No component polls, subscribes or starts a timer of its own.

The four-beat cycle of the generator is fixed and documented in the class: a block **opens** on an agent's lane, a tool
is **called** along a graph edge, the oldest open block **completes**, and the housekeeping beat **retires** the oldest
finished block once the lane reached its cap. Every completion also advances the forty-unit fan-out queue by one unit,
which starts the next queued unit or retries a failed one with an incremented attempt.

### The controls

| Control | Behaviour |
|---|---|
| **Play / Pause** | Starts and stops the generator. The label names the action the press performs, the state is announced in the page's one polite live region, and the focus stays on the button. |
| **Speed** | A `TmSegmentedControl` over `0,5×`, `1×` and `2×` — 5, 10 and 20 events per second. Changing it while running rebuilds the timer on the new period. |
| **Reset** | Returns the run to its opening snapshot (six finished blocks, one running, the seeded queue), stops the generator and restarts the change log. |
| **Read-outs** | State, applied events, events per second, sequence, open blocks and finished units, as visible text. They are **not** a live region: reading every simulated event aloud would drown the rest of the page. |
| **Last event** | One sentence per applied change ("Event 12: block completed on Planner."), as visible text. |

### Bounded state, on purpose

A surface that runs for minutes must not grow. The simulator caps the lane at twelve blocks and the handoff row at
eight links, and it **loops** the forty fan-out units back into the queue instead of appending new ones, so two minutes
at ten events per second cost the same memory as the first second. The performance spec
(`AiAgentVisualizationsPerformanceE2ETests`, category `Performance`) holds the page at that rate for two minutes and
asserts the event rate, that the UI still answers a press, and that the JS heap plateaus.

### The paint gate (and why the live surfaces are their own component)

The simulator is a host, so it applies every event it generates; only the PAINT is throttled, through
`AgentRenderThrottle` with a **200 ms** window, and a tick that falls inside the window is folded into the trailing paint
instead of being dropped.

The window is not a guess. The first version painted the whole page — thirteen surfaces, two hundred rows — once per
100 ms, and the performance spec measured the consequence: the WASM renderer needed roughly 200 ms per paint, so only
**44 of the 100 events** a ten-second window asked for ever reached the state. The fix was structural, not a bigger
window: the simulator and the three surfaces whose data it changes (lanes, graph, fan-out) moved into
`AiAgentRunLiveSurfaces`, and the ten decision surfaces below no longer re-render for a tick they do not care about.
With that split the same spec measures the full ten events per second, a flat JS heap and a press still answered in
under three seconds after two minutes of streaming.

The state is never throttled — only the paint.

### Two threads on Server

`AiAgentRunSimulator` is reached from two threads: its timer callback runs on a thread-pool thread, while Play, Pause,
the speed, Reset and Retry arrive on the circuit's UI thread. Every mutable field therefore sits behind ONE gate — the
reducer and the readers hold it, the `Ticked` event is raised after it is released (so a subscriber can call back into
the simulator without deadlocking the timer), and a tick that arrives after a Pause or a Dispose is dropped. The timer
callback never throws: a failing subscriber is recorded on `LastTickError` instead, because an unhandled exception on a
thread-pool thread would take the Server demo process down.

On the component side every tick hops to the Blazor dispatcher first (`AiAgentRunLiveSurfaces.OnTicked`), so the paint
throttle, the pending snapshot and the trailing paint timer are dispatcher-owned state only, and `Paint` runs there
directly. A trailing paint that fires after the component is disposed is a no-op.

WASM is single-threaded, which is why the browser lanes never saw this; the `AiAgentRunSimulatorTests` facts drive
`Step`, `Reset` and `Retry` from several threads at once instead.

## The surfaces on the page

| Section | Surface | What is live |
|---|---|---|
| Lanes | `TmAgentLanes` | Four agents, the seeded and the simulated blocks, handoff arrows, an approval marker, the running block and the "now" line. |
| Graph | `TmAgentGraph` | The human, the four agents, the ledger and two tools. **This is the one surface fed the incremental path**: each tool call travels as its own `AgentGraphCall` and pulses its edge. |
| Fan-out | `TmFanOutGrid` | Forty units over four workers, with a failure that can be retried and a queue that loops. |
| Parallel attempts | `TmParallelAttempts` | Three scored drafts of one task; the page records only the confirmed winner. |
| Handoff chain | `TmAgentHandoff` | Three handoffs with what they passed, what they dropped and which one is still waiting. |
| Fallback chain | `TmFallbackChain` | The recorded run (flow or table) and the editor over the same chain, with the resolution policy. |
| Autonomy | `TmAutonomyDial` | Four grades, a ceiling of two and two enforced locks; a change above the last grade asks first. |
| Memory | `TmMemoryPanel` | Twenty facts in four categories, an inline edit, pause/resume, a review decision, and forget with an undo window. |
| Delegate board | `TmDelegateBoard` | Eight tasks, three agents, the Assign menu, the automatic pass and the refusal of a full agent. |
| Scheduled runs | `TmScheduledRuns` | A week of runs over six windows, the capacity bar, the overload, and a move by drag or by menu. |
| Conflict radar | `TmConflictRadar` | Three agents over twenty-five files with **four** real conflicts; the merge waits for the last decision. The page binds BOTH pieces of reader state the surface can persist (`@bind-Resolutions`, `@bind-CollapsedDirectories`), so a fold survives the section's round trip through its table presentation, and the presentation itself is measured: the 1440 px page gets the table (with its scroll shadow), a 375 px one a card per file with the agents' changes under it. |
| Presence overlay | `TmAgentPresenceOverlay` | LIVE: `AiAgentPresenceLiveSurface` owns a marker stream (its own simulator) and a 200 ms paint gate, so four cursors follow the simulated run — anchors and activities move — while the page's own run stays paused and its counters stay at zero. A tick repaints the section and nothing else, which is why the section can afford to be live at all. It renders `AnnounceChanges="false"` (the page owns the one live region; a region reading a simulated cursor around would drown it) and keeps its table alternative over the same snapshot. The document's risk paragraph justifies its LAST line, so that anchor sits at the content's inline end and the label is stacked under the line — the placement edge case is on screen, next to three labels that stay beside their point. |

## Accessibility contract

Every graphical surface has a text or table alternative a keyboard reaches, and nothing is announced twice.

- **The components with their own switcher** carry theirs: the lanes render two tables (blocks and handoffs), the graph
  renders edges and nodes as tables, the handoff chain renders its steps as a table, the fallback chain has its Flow /
  Table switch, and the scheduled runs have Week / Day / Table.
- **The page adds a table presentation** for the fan-out grid (forty rows), the parallel attempts (three rows), the
  memory panel (twenty rows), the delegate board (eight rows), the conflict radar (every change plus the four
  decisions) and the presence roster (four rows) — the last one inside the presence section's own child component, which
  keeps the live snapshot and therefore re-renders without the page. Switching replaces the graphical surface instead of hiding it, so the
  same data is never in the accessibility tree twice. Each switch is a plain action **button**: its label names the
  presentation the next press shows ("Show the table view" ↔ "Show the visual view") and it deliberately carries **no
  `aria-pressed`**, because a changing name and a pressed state would tell a screen reader two contradictory things
  ("Show the visual view, pressed" while the table is on screen — WCAG 4.1.2; the APG keeps a toggle button's name
  fixed).
- **One polite live region per surface, and one for the page.** The page's region announces the simulator's own
  controls (play, pause, speed, reset). Reader actions — a forgotten fact, a settled conflict, an assigned card, a moved
  run — are announced by the component that owns them, because only that component knows the sentence.
- **Keyboard paths exist for every pointer path:** the lanes' blocks are native buttons, the graph is a roving tabindex
  with the arrow keys, the delegate card's drag has its Assign menu and the automatic pass (WCAG 2.5.7), the fallback
  editor reorders with Enter, the arrows and Escape, the conflicts are native radio groups, and the dial answers the
  arrows, Home and End.
- **Focus is never lost:** a cancelled autonomy change and the confirmation both hand the focus back to the slider, and
  the simulator controls keep it where the reader pressed.
- **Reduced motion** is honoured by the components (the presence cursor jumps instead of travelling), and the page adds
  no animation of its own.
- **Coarse pointers** get a 44 px target on the page's own action buttons, and every section stays inside a 375 px
  phone without widening the page. The conflict section is the one that changes SHAPE there: a card per file (never a
  table scrolled sideways), every one of them inside the container, and the presence section's cursor labels are
  measured so a label sits beside its point while there is room and is stacked under the anchored line otherwise — it
  never covers the words it points at.

## How the page is tested

- **bUnit** (`AiAgentVisualizationsPageTests`, `AiAgentRunSimulatorTests`, rendering the page and its live-surfaces child) on a `FakeTimeProvider`: the generator's
  rate, sequence and bounded state; play, pause, speed, reset; each table alternative; forget and undo; settling every
  conflict and merging; the lanes and graph selections; the dial's keys; the six view switches' absence of
  `aria-pressed`; and the simulator's thread safety (concurrent `Step`, `Reset` and `Retry`, a throwing subscriber, a
  stale tick after `Dispose`).
- **Playwright on the WASM host** (`AiAgentVisualizationsE2ETests`): the same paths in a real browser, including the
  cross-list pointer drag on the delegate board, followed by a scoped axe scan of every section.
- **The narrow-container lane** (`AiAgentVisualizationsPhoneE2ETests`) measures GEOMETRY: the 375 px cards stack the
  agents' changes under their file and nothing overflows the surface, the section or the page; the desktop table's
  declared row count is the number of rows it renders and its wrapper paints the scroll shadow; a folded directory
  survives the section's switch to the table and back; a presence label stays inside the content box and clear of every
  text line of the paragraph it points at (at 375 px at least one cursor is STACKED, because a full line leaves no room
  beside it); and the live cursors move while the page's run stays paused at zero events. Its shots land in the same
  folder (`ai-viz-conflict-cards-phone-375`, `ai-viz-conflict-folded-*`, `ai-viz-presence-labels-*`,
  `ai-viz-presence-live-*`).
- **The Server smoke** renders the page and runs the same scoped axe scans.
- **The visual sweep** (`AiAgentVisualizationsVisualE2ETests`) shoots every section at 1440 px in light and dark, the
  phone sweep at 375 px with the width and coarse-target assertions, the table alternatives on the phone, and the
  reduced-motion presentation. Reference screenshots land in
  `tests/Tempo.Blazor.E2E/TestResults/ai-agent-visualizations`.
- **The performance lane** runs the two-minute budget spec described above; it is excluded from the full E2E lane by its
  `Performance` category.
