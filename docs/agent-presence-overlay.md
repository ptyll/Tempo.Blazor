# TmAgentPresenceOverlay — where the collaborators are, over any content

`TmAgentPresenceOverlay` (package `Tempo.Blazor.AI`, namespace `Tempo.Blazor.Components.AI`) paints the collaborators'
cursors over content the HOST renders: one labelled pointer per present agent or person, anchored either to an element
the host marked with `data-presence-anchor` or to a fraction of the content box.

The layer never touches the content. It is an absolutely positioned, `aria-hidden` sibling above it with
`pointer-events: none`, so a document, a wireframe canvas or a form underneath keeps its own behaviour — and it is
never in the tab order, because a pointer that took focus would fight the editor.

```razor
<TmAgentPresenceOverlay Markers="_markers" ShowRoster="true">
    <p data-presence-anchor="intro">The quarterly plan…</p>
</TmAgentPresenceOverlay>
```

## The model

`PresenceMarker` lives in `Tempo.Blazor.Abstractions` (namespace `Tempo.Blazor.AI.Models`) and is a **record**;
`PresenceActivity` is an enum.

| `PresenceMarker` | Meaning |
|---|---|
| `Id` | Stable identity, used as the `@key`. Null or whitespace generates one, so two markers for the same agent stay two markers. |
| `Agent` | The `AgentRef` the cursor belongs to. **Required** (null throws `ArgumentNullException`); the name, the avatar and the palette colour all come from it. |
| `AnchorId` | The value of a `data-presence-anchor` attribute on an element inside the content. Null or whitespace means the marker uses `X`/`Y` instead. |
| `X`, `Y` | The position as a fraction of the content box (`0` is the inline start / top, `1` the inline end / bottom), normalised into `0…1` on assignment — a host that hands over a pixel offset by mistake still paints inside the content. Ignored when `AnchorId` is set, because a measured anchor WINS over a computed point. |
| `Activity` (`PresenceActivity`) | `Viewing`, `Typing`, `Editing`, `Commenting`. The enum carries no text: the surface renders its own localized word. |
| `Label` | The host's own sentence for the activity ("Reviewing the summary"), used INSTEAD of the localized word. Null renders the word — which is not the same as an empty label. |

Derived: `HasAnchor`, `HasPoint`, `HasLabel`, and the `Normalize(fraction)` helper.

## The surface

| Parameter | Meaning |
|---|---|
| `Markers` | `IReadOnlyList<PresenceMarker>?` — the cursors, in an order the host controls. Null or empty renders an empty layer (and, with `ShowRoster`, the localized "No one is present."). |
| `ChildContent` | The content. It renders untouched, FIRST, and the layer is positioned over it. |
| `ShowRoster` | Whether the roster — the TEXT alternative of the cursors — is rendered. Default `false`. |
| `RosterLabel` | The roster's visible title and its accessible name. Null uses the localized "Present now". |
| `AnnounceChanges` | Whether joins, activity changes and leaves are announced. Default `true`. A host that owns its own announcement channel sets `false` and the component then renders NO live region at all rather than a second, competing one. |
| `Class`, `AdditionalAttributes` | The usual chrome. |

Each marker carries `data-presence-marker`, `data-presence-anchor` (when anchored), `data-agent-id`, `data-activity` (the
stable slug) and an inline `--tm-presence-color: var(--tm-agent-color-N)` from the shared palette, so the same agent has
the same colour here as in the lanes or the graph. The label names the agent and the activity; the cursor is a decorative
SVG pointer.

## The measurement (the package's ES module)

An anchored marker is placed by `tm-agent-presence.js` (imported through `IJSObjectReference` — no `<script>` tag, no
eval):

- it finds each marker's anchor inside the content, measures both boxes and writes the offsets into the
  `--tm-presence-x` / `--tm-presence-y` custom properties, so a moving cursor **never re-renders** the component (on a
  Server circuit that would be a message per keystroke of a collaborator);
- a `ResizeObserver` watches the content and every anchor, a `MutationObserver` watches the content (a re-rendered
  anchor is found again), and window `resize`/`scroll` — including a scroll inside the content — ask for a new pass;
- the work is throttled (a rAF plus an 80 ms minimum interval), so a burst of mutations costs one measurement;
- a marker whose anchor disappeared is HIDDEN (the stylesheet reveals `[data-presence-placed="true"]` only) instead of
  being left at a stale position;
- a marker whose label would run past the content's inline end is MIRRORED (`data-presence-flip="true"`): the marker's
  inline end lands on the measured point and its label grows back INTO the content, so a cursor near the edge never
  widens the page. The layer also clips as a last guard, because a decorative layer must never move the page under the
  reader;
- `detach()` disconnects every observer, removes every listener and cancels a pending measurement; the component calls
  it from `DisposeAsync`.

A **point** marker needs no JavaScript at all: the component paints its own fractions, so the layer still works where
the module cannot be imported (prerender, a disconnected circuit, a host without the static assets).

## Motion

Movement is the ONE animation in the partial: a marker travels between measured positions (160 ms). The
`prefers-reduced-motion: reduce` block removes it, so a reader who asked for less motion gets the finished position at
once — the information (who is where and what they do) is unchanged. The module itself never animates, so there is one
motion source only.

## Accessibility

- the visual layer is `aria-hidden` and `pointer-events: none` and never enters the tab order;
- the same presence is available as text: `ShowRoster` lists every present agent with what they are doing
  (`role="group"` labelled by its visible title), and an empty roster says "No one is present." instead of rendering an
  empty list;
- exactly ONE polite live region announces a join, a change of activity and a leave. The sentences are distinguished:
  a join or a different NAME reads "{0} is {1}." while an activity change reads "{0} is now {1}." — a reader hears that
  something CHANGED rather than that the agent joined again. Several simultaneous changes produce ONE sentence (the first
  in the host's order) because a live region that read every change would be noise; an unchanged state is never
  re-announced;
- `(pointer: coarse)` raises the roster row to the 44 px `--tm-touch-target`;
- a mirrored label sits on the other side of its point; on a phone the label can then overlap the words just before the
  anchor — the roster (and one live region) carry the same presence as text, so nothing is only visual;
- `forced-colors: active` hands the cursor and the labels to `CanvasText`/`Canvas`/`Highlight` with
  `forced-color-adjust: none`, so the system palette is the source.

Guarded by `AiMarkdownViewCssTests` and `TmAgentPresenceOverlayCssTests`; the module's behaviour is pinned by
`npm run test:ai` (`tm-agent-presence.test.mjs`).

## Adopting an existing collaboration feed

The AI package must not reference any editor or transport, so a host maps its own cursor DTO onto `PresenceMarker`
itself. The demo does exactly that for the Notion editor's `CollaboratorCursor`, in
`src/Tempo.Blazor.Demo.SharedUI/Pages/PresenceMarkerAdapter.cs`: the user id becomes the agent id (so two cursors of one
user are one agent), the display name becomes the name, the user id picks a stable palette slot, and the block id
becomes the anchor value the document's elements carry.

## Localization

`TmAgentPresenceOverlay_*` in English, Czech, Slovak and French: the roster title and its empty line, the four activity
words, and the three announcement sentences (join, activity change, leave).
