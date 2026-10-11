# Touch interaction: the shared pointer-drag helper

Every drag in Tempo.Blazor — resizing an editor panel, resizing a scheduler event, and what the
Gantt, dashboard and designers will adopt next — runs on **one** helper:
`_content/Tempo.Blazor/js/pointer-drag.js`. A component or package must not write its own
`mousedown`/`mousemove`/`mouseup` or `touchstart` drag: it attaches this module. It is an ES module
with no dependencies and knows nothing about the DOM shape of its host.

```js
import { attach } from '_content/Tempo.Blazor/js/pointer-drag.js';

const drag = attach(handle, {
    threshold: 4,                       // px before a press becomes a drag (a tap stays a tap)
    onStart({ startX, startY }) { /* the drag began */ },
    onMove({ dx, dy, x, y }) { /* follow the pointer; dx/dy are measured from the press point */ },
    onEnd({ dx, dy }) { /* released: commit ONCE */ },
    onCancel({ reason }) { /* Escape, pointercancel, lost capture, detach: roll back, commit nothing */ },
    onRelease({ outcome }) { /* every path (tap, end, cancel): cleanup */ },
});

drag.detach();                          // on dispose; cancels a drag in progress, safe to call twice
```

A classic script (`scheduler.js`) loads it with a dynamic `import()` resolved from its own
`document.currentScript.src`; a module (`tm-editor-shell.js`) imports it statically.

## What the helper owns

| Concern | Behaviour |
|---------|-----------|
| **Pointer capture** | `setPointerCapture(pointerId)` on press and `releasePointerCapture(pointerId)` on every exit. The id is required: the argument-less call throws in a real DOM and leaks the capture. |
| **One pointer** | Only the first (primary) pointer drives a gesture. A second finger can neither move nor end it. Secondary mouse buttons never start one. |
| **Threshold** | `threshold` px (default 4) of movement before `onStart`. Below it the press is a tap: `onPress` and `onRelease({ outcome: 'tap' })` only, and its `click` is untouched. |
| **Long-press** | `longPressMs > 0` (touch only): the drag starts after the finger rests that long (`origin: 'long-press'`). Moving more than `longPressTolerance` (8 px) first is a scroll — the long-press is abandoned and the page scrolls normally. A mouse ignores `longPressMs`. |
| **Cancel** | `Escape` (while a gesture is active), `pointercancel`, `lostpointercapture`, a mouse move with no button held (a missed mouse-up), and `detach()` all call `onCancel({ reason })` — **never** `onEnd` — and restore nothing the host did not ask for. |
| **Click suppression** | The `click` that follows a real drag is swallowed once (it expires on the next task, so an unrelated later click is never eaten). `suppressClick: false` opts out. |
| **touch-action** | See below. |
| **Reduced motion** | Every callback receives `reducedMotion` (`prefers-reduced-motion: reduce`). Skip animation, snap instead of easing. |
| **Cleanup** | Listeners, the document `keydown`, the capture, the drag class and the original `touch-action` are removed on every path, even when a host callback throws. |
| **Multi-instance** | State is per `attach()` call. Two schedulers or two shells on one page never share a gesture; a registration id belongs to the host (see `tm-editor-shell.js`, which keys by a per-component-instance id). |

## Options

| Option | Default | Meaning |
|--------|---------|---------|
| `threshold` | `4` | Pixels before a press becomes a drag; `0` starts on the first move. |
| `longPressMs` | `0` | Touch long-press delay; `0` disables it. |
| `longPressTolerance` | `8` | Finger drift allowed while waiting for the long-press. |
| `selector` | none | **Delegation:** listen on a container and start only from descendants matching it (one listener for many items). Capture and the drag class go to the matched element, which is `info.target`. |
| `pointerTypes` | all | e.g. `['mouse', 'pen']` to leave touch to native scrolling or HTML5 drag-and-drop. |
| `captureOn` | `'press'` | `'start'` takes capture only once the drag begins, so a tap on a child still gets its click untouched. |
| `cancelOnEscape` | `true` | |
| `suppressClick` | `true` | |
| `dragClass` | `tm-pointer-dragging` | Class on the target while dragging; `null` disables. |
| `touchAction` | see below | Explicit `touch-action` for the element; `null` leaves it alone. |

`onPress` and `onStart` may return `false` to veto the gesture (the capture is released and nothing
else runs). Each callback receives `{ event, pointerId, pointerType, target, startX, startY, x, y,
dx, dy, origin, reducedMotion }`; `onCancel` adds `reason`, `onRelease` adds `outcome`
(`'tap' | 'end' | 'cancel' | 'aborted'`) and `started`.

## touch-action — the page must still scroll

A browser decides whether a touch pans the page **before** any script runs, so the CSS has to say so:

- **A drag handle that starts immediately** (a splitter, a resize edge) uses `touch-action: none`.
  Put `.tm-touch-drag-handle` on it, or let `attach()` set it for you — without `longPressMs` and
  without `selector` the helper sets `touch-action: none` on the element and restores the old value
  on `detach()`.
- **A long-press drag** (a draggable card in a scrolling list) must **not** set `touch-action: none`,
  or the list can no longer be scrolled by touching a card. The helper leaves it alone.
- **A delegated drag** puts `touch-action: none` only on the handle elements, in CSS.
- While a drag is *active* the helper cancels `touchmove` (registered non-passive) so iOS does not
  also scroll; while a long-press is still pending it does not.

`.tm-pointer-dragging` is `user-select: none`, so text is not selected under the finger.

## A drag is never the only way (WCAG 2.5.7)

WCAG 2.5.7 *Dragging Movements* (WCAG 2.2, level AA): anything done by dragging must also be possible with a
single pointer press, and a keyboard user cannot drag at all. **A component that adopts the helper
must ship a non-drag alternative in the same change.** The convention:

| Gesture | Keyboard alternative | Announcement |
|---------|----------------------|--------------|
| Move an item (event, card, node) | Alt+arrow (`Alt+ArrowDown`) moves it one step (slot, day, grid cell) | `aria-live="polite"` message "Moved to …" |
| Resize an item | Alt+Shift+arrow grows/shrinks by one step | "Resized to …" |
| Resize a panel (splitter) | arrows / `Home` / `End` on the `role="separator"` (`TmEditorShell`) | `aria-valuenow` |
| Cancel | `Escape` while a mouse/touch gesture is active (handled by the helper) | none needed |

The alternative reuses the commit path: `Alt`+arrow calls the same .NET method the drag's `onEnd`
does, so there is one code path to test and one place a read-only guard lives. Pointer-only
affordances (a visible `+`/`−` button, a menu command "Move to…") satisfy "single pointer" for
touch users who cannot drag precisely.

## Adopters

| Module | What it does with the helper |
|--------|------------------------------|
| `tm-editor-shell.js` | Panel resizer: `attach(handle, { threshold: 0 })`; `onMove` clamps the width and writes the CSS variable, `onEnd` commits once, `onCancel` restores the start width. The keyboard path stays in Blazor and shares the clamp vectors. |
| `scheduler.js` | Event resize (time grid and timeline) with a mouse, finger or pen: `attach(document, { selector: '.tm-scheduler-event-resize-handle', threshold: 2, longPressMs: 400 })`. The long-press applies to touch only, so a quick swipe scrolls (the coarse handle is `touch-action: pan-y` on the time grid and `pan-x` on the timeline, capped at `min(var(--tm-space-4), 25%)`, with a visible grip under `hover: none`); mouse and pen keep the 2px threshold. Hold, then drag, to resize. `window.TempoBlazor.Scheduler.init/registerInstance/unregisterInstance/destroy` and the `NotifyResizeComplete` callback are unchanged. The mouse keeps HTML5 drag-and-drop for moving the event body. |

Gantt, Dashboard, DiagramEditor, Wireframe and the Signing designer adopt it in their own plans; none
of them may add a second implementation.

## Testing

`src/Tempo.Blazor/wwwroot/js/__tests__/pointer-drag.test.mjs` drives the helper with hand-rolled
DOM stubs (`npm run test:overlay`). A node test with fake pointer events cannot prove a gesture in a
browser: drive every JS-owned gesture with a real Playwright mouse/touch drag and assert the result
(width, persisted value, notified delta).
