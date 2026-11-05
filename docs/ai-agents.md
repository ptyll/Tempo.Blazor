# Agent visualizations — the shared contract

The agent surfaces of `Tempo.Blazor.AI` render a multi-agent run for a human: who took part, what each agent did,
what was handed over, what broke and what is waiting for a decision. They share one identity model, one live-data
contract and one accessibility contract, and this page documents those three common layers.

Everything here is **generic**. The library never references a specific orchestration backend: a host maps its own
DTOs onto the models below and the components stay transport-agnostic.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmAgentBadge Agent="planner" Status="planner.Status" />
```

## Where the types live

| Layer | Package | Namespace |
|---|---|---|
| Models and the live-data contract | `Tempo.Blazor.Abstractions` | `Tempo.Blazor.AI.Models` |
| Components and their presentation mapping | `Tempo.Blazor.AI` | `Tempo.Blazor.Components.AI` |

The split is deliberate: `Tempo.Blazor.Abstractions` has no Blazor dependency, so a backend, a worker or an API
project models the same run without pulling a renderer in. The physical folder is
`src/Tempo.Blazor.Abstractions/AI/Agents/`.

## The agent identity: `AgentRef`

```csharp
var planner = new AgentRef("Planner", "agent-planner")
{
    Role = "Orchestrator",
    ColorIndex = 0,
    AvatarUrl = "https://example.test/planner.png",
};
```

| Member | Meaning |
|---|---|
| `Id` | Stable identity — the `@key` of every agent surface and the value a selection reports. A blank id is generated at construction, so two agents with the same name stay two agents. |
| `Name` | The display name. Empty when the host supplied none; a component renders its own localized fallback instead of an empty gap. |
| `Role` | What the agent is for ("Orchestrator", "Reviewer", "Worker"), in the host's own words. Null renders no role line. |
| `AvatarUrl` | An absolute URL of the agent's picture. Null renders the initials. The picture is host data, so a component loads it only when it is an absolute `http`/`https` source (the package's image allowlist, the same rule the markdown pipeline applies to images) and falls back to the initials for anything else. |
| `ColorIndex` | The slot in the agent palette. Any integer wraps into `0…7` on assignment. |
| `Initials` | The one or two letters the avatar paints without a picture: "Data analyst" → `DA`, "Bedřich Novák" → `BN`, a single word → its first letter, no letter at all → `?`. |
| `ColorToken` | The custom property the slot names, e.g. `--tm-agent-color-3`. |

`AgentRef` is a record and a **value**: a host recomposes it with `agent with { … }` and no component mutates it. It
carries no activity — the same agent can be idle in one lane and running in another, so state belongs to a block, a
node or a tile.

### The agent palette

Eight distinct slots, so two agents in one visualization can never be confused, plus the ink painted on them:

| Token | Light | Dark |
|---|---|---|
| `--tm-agent-color-0` | blue-700 | blue-300 |
| `--tm-agent-color-1` | violet-700 | violet-300 |
| `--tm-agent-color-2` | pink-700 | pink-300 |
| `--tm-agent-color-3` | red-700 | red-300 |
| `--tm-agent-color-4` | amber-700 | amber-300 |
| `--tm-agent-color-5` | emerald-700 | emerald-300 |
| `--tm-agent-color-6` | teal-700 | teal-300 |
| `--tm-agent-color-7` | slate-700 | slate-300 |
| `--tm-agent-color-ink` | `--tm-text-inverse` (white) | `--tm-text-inverse` (near-black) |

The light theme uses the 700-tones under white initials, the dark theme the 300-tones under the dark ink: both pairs
hold WCAG AA (**4.5 : 1**) for the initials, measured in both themes by `AgentColorTokenTests` — the palette is not a
set of decorative hues. A host that repoints the palette takes that obligation over with it; a component never paints
a fill or an ink of its own.

## The live-data contract: snapshot + events

A live surface takes a **snapshot** (parameters) and, optionally, **incremental events** with a monotonic
`Sequence`. The component owns its view of the data; the host owns the transport. Nothing in the library subscribes to
a hub, starts a timer or knows what an event means.

### What a snapshot is

The complete state at one moment — every lane, block, node, tile or row the surface draws, each with its identity, its
state and its numbers. A component renders exactly what it was handed: a deep link, a reload and a "repair after a
gap" all produce a new snapshot and the surface simply draws it. The host passes the sequence the snapshot includes
alongside it, because that number is what the event stream re-bases on.

### What an event is

One applied change with a sequence number that grows by one per change. The event carries whatever the surface needs
(a block opened, a step changed state, an edge was called, a memory item was forgotten); the library reads exactly one
thing out of it — the sequence — and lets the host's own reducer apply it.

`AgentEventStream<TEvent>` enforces the whole contract:

```csharp
private AgentEventStream<RunEvent> _events = new(evt => evt.Sequence);

// Initial load, reload, or the repair after a gap:
_events = new AgentEventStream<RunEvent>(evt => evt.Sequence, snapshot.Sequence);
Snapshot = snapshot;

// A hub callback and a one-second poll both land here:
var applied = _events.Apply(batch);
if (applied.Count > 0)
{
    Snapshot = Snapshot.Apply(applied);   // the host's own reducer
}

_events.GapDetected += missing => _ = ReloadSnapshotAsync(missing);
```

| Rule | Behaviour |
|---|---|
| **Ordered** | A batch is sorted by the sequence selector, so a merged page or a re-delivered page never applies a block before the event that opened it. |
| **Idempotent** | An event whose sequence was already applied is dropped. Applying the same batch twice changes nothing — which matters because a hub genuinely redelivers after a reconnect. |
| **Contiguous** | A hole is never papered over. When a sequence is missing the **whole batch** is rejected, `HasGap` turns true and `GapDetected` is raised once with the first sequence the stream does not have. The applied tail is therefore always a contiguous run. |
| **No baseline needed** | A fresh stream accepts any first sequence (0, 1, or whatever number a restarted backend begins at) and reports no hole against a baseline that does not exist. |
| **Thread-safe** | A hub callback and a polling loop may feed one stream: no event is applied twice and no batch is spliced into another. The one callback that runs **outside** the gate is `GapDetected`, so a handler may wait for another thread to repair the stream instead of deadlocking against the applying one. |
| **Transport-neutral** | Nothing reads the clock, awaits, or inspects the event beyond its sequence. |

### Repairing a gap

A gap means the component is missing a change it can never reconstruct. The documented repair is a reload:

1. Load a new snapshot from the host's API.
2. Call `ResetToSnapshot(snapshot.Sequence)` (or construct a new stream with that baseline).
3. The component draws the snapshot and the stream is contiguous again.

`ResetToSnapshot` may also move the baseline **backwards**, which is how a snapshot taken before the last events
replays them. A batch that applies nothing (a pure redelivery) does not clear a gap — only a repair does.

`GapDetected` is raised on the applying thread **after** the stream's gate is released, and `HasGap` is already true
when the handler starts. A handler is therefore free to call `ResetToSnapshot`, `Apply` or `LastSequence`, including
from a thread it hands the work to and waits for: the lock is never held across your callback. The state the cue
describes is not pinned, though — another feed may have repaired the hole while the handler ran.

### Throttling the paint, never the state

An event must not become a render. `AgentRenderThrottle` is the shared gate: at most one paint per **100 ms**, on an
injected `TimeProvider`.

```csharp
private readonly AgentRenderThrottle _throttle = new(Clock);

private void OnEvent(RunEvent evt)
{
    Snapshot = Snapshot.Apply(evt);          // every event is absorbed
    if (_throttle.ShouldRender())            // at most ten paints per second
    {
        StateHasChanged();
    }
}
```

- `ShouldRender()` is a window opener — ask once per event, paint when the answer is true.
- `DelayUntilNextRender()` is for a host that schedules a trailing paint (`TimeSpan.Zero` means now; the value never
  goes negative).
- `Reset()` is for a host that repainted for its own reason (a snapshot reload), so its next event is not held back.
- `TimeSpan.Zero` turns throttling off entirely.
- One `TimeProvider` drives both the gate and any live clock the surface shows, so a test with `FakeTimeProvider`
  controls both.
- The throttle is **not thread-safe** (unlike `AgentEventStream`): one timestamp is read and written with no lock, so
  an instance belongs to the component that owns it and is called from that component's renderer context (inside
  `InvokeAsync` on Blazor Server, on the dispatcher in WASM) — which is where a render is decided anyway.

### Mapping a typical backend

The library's models are deliberately smaller than any real backend's. A mapping for a hub-plus-poll orchestration
service:

| Backend concept | Library model | Notes |
|---|---|---|
| `SessionParticipantDto { ParticipantId, RoleKey, DisplayName, Model }` | `AgentRef(id: ParticipantId, name: DisplayName) { Role = RoleKey, ColorIndex = stableIndex }` | Derive `ColorIndex` from a stable ordering (the participant's index in the session), not from a hash: eight slots reused per session are what keeps two agents distinguishable. |
| `Lifecycle` / turn state (`queued`, `leased`, `waiting-for-approval`, `done`, `failed`, `cancelled`) | `AgentActivityStatus` | The one place to collapse a backend's finer vocabulary; anything the mapping does not know renders as an unknown state (no chip) rather than a guess. |
| `MessageAppended { Sequence }`, `TurnChanged { Sequence }` | the `TEvent` sequence | One sequence per applied change. If two hub messages describe one change, bump the sequence once. |
| `GetSessionSnapshotAsync()` | the snapshot parameter + its sequence | The snapshot's sequence is what `ResetToSnapshot` receives. |
| A 1 s poll that returns the tail since a sequence | `AgentEventStream.Apply(batch)` | Idempotent by construction, so the poll may overlap the hub safely. |
| A hosted timer / scheduled run | inject `TimeProvider` (a fake in tests) | Never `DateTimeOffset.UtcNow` in a component. |

## `TmAgentBadge` — the shared identity line

`TmAgentBadge` draws one agent: initials (or the picture) on the palette colour, the name, and the state as an icon,
a **word** and a tone. Every agent visualization uses it, so an agent looks the same in a lane header, a graph node
and a presence roster.

```razor
<TmAgentBadge Agent="planner" Status="AgentActivityStatus.Running" />

<TmAgentBadge Agent="reviewer"
              ShowName="false"
              Size="AgentBadgeSize.Sm"
              Status="AgentActivityStatus.WaitingForApproval" />
```

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `Agent` | `AgentRef?` | `null` | The agent. Null renders nothing — a surface that has not loaded its snapshot paints no anonymous avatar. |
| `Status` | `AgentActivityStatus?` | `null` | What the agent is doing. Null renders no chip (which is not the same as `Idle`); an unknown value renders no chip either. |
| `ShowName` | `bool` | `true` | Render the name next to the avatar. With `false` the badge becomes one labelled graphic. |
| `ShowStatus` | `bool` | `true` | Render the state chip when `Status` is set. |
| `Size` | `AgentBadgeSize` | `Md` | `Sm` (20 px avatar), `Md` (24 px) or `Lg` (32 px). |
| `Class` | `string?` | `null` | Extra root classes. |
| `AdditionalAttributes` | `Dictionary<string, object>?` | `null` | Extra root attributes. |
| `DataTestId` / `TestIdPrefix` | from `TmComponentBase` | — | Standard test-id namespacing (root id `agent-badge`). |

### The state vocabulary

`AgentActivityStatus`: `Idle`, `Queued`, `Running`, `WaitingForApproval`, `Succeeded`, `Failed`, `Cancelled`.

Each state is painted three ways at once — a tone, an icon and a localized word — because a colour alone is not
information a colour-blind or screen-reader reader can use (WCAG 1.4.1). The tone slugs are stable CSS hooks
(`.tm-agent-badge__status--running`, `--approval`, `--succeeded`, …) and the words come from `TmAgentStatus_*`
resources, so a lane, a tile and a roster cannot disagree.

The badge's own contract:

- **No live region.** A badge carries no `aria-live` and no `role="status"`. The surface that owns the run has
  exactly one announcement channel; a badge inside it must never become a second one.
- **No motion.** No transition, no animation; the state is readable without either, and the package's
  reduced-motion enumeration is unchanged. A future animated subject adds its own `prefers-reduced-motion` block.
- **Named once.** With the name visible there is no `aria-label` — the visible text is the accessible name
  (WCAG 2.5.3). With `ShowName="false"` the composed avatar becomes one `role="img"` graphic named by the agent and
  its state, because an `aria-label` on a role-less `<span>` is ignored.
- **Not interactive.** No tab stop, no button, no drag surface — so WCAG 2.5.7 and the 44 px coarse-pointer target
  are the host's obligation when it wraps a badge in its own control.
- **Picture allowlist.** `AvatarUrl` is host data, so it is loaded only when it is an absolute `http`/`https` source
  (the package's image allowlist). A refused target — another scheme, a protocol-relative path, a relative one, a
  `data:` URI — renders the initials instead, never a live source and never a broken image.
- **High contrast.** Under `forced-colors` the avatar opts out of the system palette (`forced-color-adjust: none`) so
  the eight agent fills stay distinguishable, and keeps a `CanvasText` border for the circle's edge.

## The accessibility contract of an agent visualization

The family is built against one accessibility contract, because every surface renders the same run:

- **A text or table alternative, reachable by keyboard.** A graphical surface (lanes, a graph, a conflict matrix) is
  never the only way to read the data: the same snapshot has a list or table presentation behind a switcher, and the
  switcher is a real radio group or tab list.
- **Keyboard first.** Every focusable block, node or tile carries a localized `aria-label` (agent, activity, time,
  state) and answers the arrow/Home/End keys a reader expects from the pattern it declares in ARIA.
- **One live region.** The owning surface announces what changed — a selection, a completed step, a conflict, a
  filtering result. The pieces it is built from (a badge, an icon, a chip) announce nothing, so no change is spoken
  twice.
- **No colour-only information.** A state, a conflict or an overload is always an icon AND a word AND a tone.
- **Reduced motion.** Pulses and flows are the exception, not the default: each one is declared with its own
  `prefers-reduced-motion` block that removes the movement and keeps the highlight.
- **Non-drag alternatives.** A drag (a task onto an agent, a run into another window) always has a menu or keyboard
  path that produces exactly the same command (WCAG 2.5.7).
- **Focus management.** A surface that opens a detail panel or a sheet moves the focus into it, returns it to the
  control that opened it on close, and never leaves the focus on a control that disappeared.

## The run in time: `TmAgentLanes`

`TmAgentLanes` draws the same run as lanes on one shared time axis — a block per piece of work, the review verdict a
block carries, handoff arrows between the lanes and the current moment as a vertical line. It is the first surface
built on the live contract above: the host passes a snapshot plus an event tail, the component applies the tail through
`AgentEventStream` (reporting a hole with `OnGapDetected`), throttles the paint with `AgentRenderThrottle` and keeps the
timeline from being the only way to read the data (a table view carries the same blocks and handoffs). Its full
contract — models, layout rules, zoom, accessibility, CSS — is [agent-lanes.md](agent-lanes.md).

## The network: `TmAgentGraph`

`TmAgentGraph` draws the SAME agents as a network: a box per agent, tool, resource and human in the column its kind
belongs to, a curve per connection whose thickness is how often it was called, and a short pulse on the curve every
time a new `AgentGraphCall` arrives. Its layout is pure C# (`AgentGraphLayout`, deterministic and unit-tested), the
canvas is an SVG `role="group"` whose nodes are native buttons (one roving tab stop, ARROW keys walking the real
connections), and the View switcher carries every connection and every node as plain tables. Its full contract —
models, layout rules, pulses, accessibility, CSS and the package's JS module — is [agent-graph.md](agent-graph.md).

## Related pages

- [TmAgentGraph](agent-graph.md) — the same run as a network of agents, tools and resources.
- [TmAgentLanes](agent-lanes.md), [TmAgentPlan](agent-plan.md), [TmAgentRunTree](agent-run-tree.md), [TmAgentMonitor](agent-monitor.md),
  [TmPermissionRequest](permission-request.md) — the run-side surfaces that these visualizations compose with.
- [TmTraceWaterfall](trace-waterfall.md) — the span view of one agent's work.
- [TmModelPicker](model-picker.md) — which model an agent runs on.
