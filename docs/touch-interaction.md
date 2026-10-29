# Touch interaction: the shared pointer-drag helper

Rule: new drags must use pointer-drag.js (`_content/Tempo.Blazor/js/pointer-drag.js`): existing private gestures migrate in their owners' plans,
and they are not rewritten here. The helper is an ES module with no dependencies and knows nothing about
the DOM shape of its host.

The private gestures still to migrate:

| Owner | Gesture |
| --- | --- |
| `dashboard.js` | widget drag and resize |
| `TmGantt` | bar drag and resize |
| `TmSigningFieldOverlay` | field drag |

Migrated (they no longer own a private gesture): `TmDataTable` (column resize became
`data-table.js` over the helper), `TmColorGradient` (the gradient stop drag became
`tm-color-gradient.js`), `TmSplitterPane` (the `eval` drag became
`tm-splitter.js` over the helper), and `slider.js` (deleted: `TmRangeSlider` uses the standard
dual-range CSS, so each native thumb drags itself and no JS gesture exists).

Multi-pointer gestures (pinch and pan) live in the core sibling module `pointer-gesture.js`
(see "Multi-pointer gestures" below) — never an implementation inside a package.

```js
import { attach } from './pointer-drag.js';            // from another file in wwwroot/js/

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
`document.currentScript.src`; a module (`tm-editor-shell.js`) imports it statically. The specifier is
always **relative**. A bare `'_content/Tempo.Blazor/js/pointer-drag.js'` is not a module specifier: the
browser throws `TypeError` unless the host added an import map, which this library does not.

## Consuming from a split package

A module that ships in another package (`Tempo.Blazor.DiagramEditor`, `Tempo.Blazor.Signing`, …) cannot
use `./pointer-drag.js` — that resolves inside its own `_content/<Package>/js/` folder. Static web assets
of referenced packages are served as siblings, so the module walks up to the core package:

```js
import { attach } from '../../Tempo.Blazor/js/pointer-drag.js';
```

A classic script has no import path of its own. Resolve the helper from the URL the browser actually
loaded the script from, which keeps a `<base href>` and a fingerprinted `MapStaticAssets` name intact:

```js
const helperUrl = new URL('pointer-drag.js', document.currentScript.src).href;
const { attach } = await import(helperUrl);
```

`document.currentScript` is only set while the classic script evaluates synchronously, so capture it at
the top of the file, not inside a later callback. Two caveats:

- **`<base href>` and `MapStaticAssets`.** A root-absolute `'/_content/…'` URL ignores `<base href>` and
  misses the fingerprint `MapStaticAssets` appends to the file name, so the import 404s. Resolving
  against `document.currentScript.src` (or a relative specifier, for a module) follows both.
- **CSP.** A `script-src` policy that allows `scheduler.js` must also allow the module it imports. The
  helper is served from the same `_content/Tempo.Blazor/js/` origin, so a host-wide nonce or hash for
  that origin covers it; a per-file hash must list `pointer-drag.js` too.

## What the helper owns

| Concern | Behaviour |
|---------|-----------|
| **Pointer capture** | `setPointerCapture(pointerId)` on press and `releasePointerCapture(pointerId)` on every exit. The id is required: the argument-less call throws in a real DOM and leaks the capture. A new primary `pointerdown` with a different `pointerId` cancels a stale session first (`reason: 'superseded'`, never `onEnd`) — a pointer that vanished without `pointerup` would otherwise leave a gesture nothing can finish. |
| **One pointer** | Only the first (primary) pointer drives a gesture. A second finger can neither move nor end it, and neither can a non-primary button: the `isPrimary` and `button` guards run before the superseded check, so a second contact never cancels the active drag. |
| **Threshold** | `threshold` px (default 4) of movement before `onStart`. Below it the press is a tap: `onPress` and `onRelease({ outcome: 'tap' })` only, and its `click` is untouched. |
| **Long-press** | `longPressMs > 0` (touch only): the drag starts after the finger rests that long (`origin: 'long-press'`). Moving more than `longPressTolerance` (8 px) first is a scroll — the long-press is abandoned and the page scrolls normally. A mouse ignores `longPressMs`. |
| **Cancel** | `Escape` (while a gesture is active), `pointercancel`, `lostpointercapture`, a mouse move with no button held (a missed mouse-up), and `detach()` all call `onCancel({ reason })` — **never** `onEnd` — and restore nothing the host did not ask for. The `Escape` listener is registered on `document` in the **capture** phase and calls `stopPropagation`, so it runs before a dialog's own listener (or an earlier document listener) and does not also close the surrounding dialog or sheet. |
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
else runs). The helper calls `preventDefault` on the `pointerdown` of an immediate drag, and that
suppresses the browser's compatibility mouse events (`mousedown`/`mouseup`) in the matched subtree. An adopter with a
child that listens for `@onmousedown` must convert it to `@onpointerdown`, or veto the gesture in
`onPress` and handle the press itself. Each callback receives `{ event, pointerId, pointerType, target, startX, startY, x, y,
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

## Multi-pointer gestures — `pointer-gesture.js`

Two-finger pinch/pan and edge auto-scroll live in the `pointer-drag.js` sibling module
`pointer-gesture.js` (`_content/Tempo.Blazor/js/pointer-gesture.js`, ES module, no dependencies).
The same rule as for drags: a component or a package never writes its own pinch.

```js
import { attachPinch, createAutoScroller } from './pointer-gesture.js';

const pinch = attachPinch(element, {
    pointerTypes: ['touch'],         // mouse/pen cannot pinch; [] allows every pointer type
    wheel: true,                     // ctrl+wheel (trackpad pinch) zooms around the cursor
    minScale: 0, maxScale: Infinity, // clamp of the cumulative scale reported
    touchAction: 'none',             // the default; null leaves the element alone (restored on detach)
    captureSinglePointer: true,      // false leaves a LONE finger to a descendant surface's own listeners
    onStart({ centerX, centerY, distance, reducedMotion }) { /* second finger landed */ },
    onChange({ scale, centerX, centerY, dx, dy, event, reducedMotion }) { /* live zoom/pan */ },
    onEnd({ scale, centerX, centerY, dx, dy }) { /* fingers lifted: commit ONCE */ },
    onCancel({ reason }) { /* 'escape' | 'pointercancel' | 'lostpointercapture' | 'detached': roll back, commit nothing */ },
});

pinch.detach();                      // on dispose; cancels an active pinch, safe to call twice
```

| Concern | Behaviour |
|---------|-----------|
| **Two pointers** | The first two pointers of the allowed types are tracked by id: the pinch starts when the second finger lands and ends exactly once when the count drops below two. A third finger is ignored — it neither starts, moves, nor ends anything. |
| **Pointer capture** | Both fingers are captured by id (they may slide off the element mid-gesture); every exit releases by id. By default a LONE finger is captured too (`captureSinglePointer`, default true) — a descendant-surface host opts out (see Coexistence below). A `lostpointercapture` for a tracked finger — the element left the DOM, the browser took the capture back — cannot be tracked around: mid-pinch it cancels (below), with a lone finger it just forgets the finger. When the drag and the pinch share an element, the drag's release on cancel also drops the pinch's capture of that finger. The pinch re-asserts both captures right after `onStart`, so the hand-over never rolls the pinch back. |
| **Scale** | `onChange`/`onEnd` report the scale relative to the gesture start, clamped to `minScale`/`maxScale`, with the centre of the two fingers and its pan `dx`/`dy` from the start centre. |
| **Cancel** | `pointercancel` of either finger, `lostpointercapture` of a tracked finger, `Escape` and `detach()` call `onCancel({ reason })`, never `onEnd`. The `Escape` listener sits on `document` in the capture phase with `stopPropagation` (as in the drag helper): an ACTIVE pinch consumes the key, a lone finger leaves it to the surrounding dialog. |
| **ctrl+wheel** | A non-passive wheel listener; each tick reports `scale = Math.exp(-deltaY / 100)` around the cursor and calls `preventDefault` only while ctrl is held — plain wheel keeps scrolling the page. A burst is one gesture: `onStart` on the first tick, `onChange` per tick with the cumulative scale, and one `onEnd` 150 ms after the last tick (`detach()` flushes a pending one). |
| **Reduced motion** | `onStart`/`onChange` receive `reducedMotion` so the host can snap zoom steps instead of animating. |

**Coexistence with `attach()`.** `pointer-drag.js` stays single-pointer. The first finger may already
drive a drag; the drag helper's `isPrimary` guard means the second finger neither moves nor cancels
that drag — the HOST hands over, in the pinch's `onStart`, so the drag rolls back exactly once the
pinch takes over:

```js
const drag = attach(handle, { /* ... */ });
const pinch = attachPinch(handle, { onStart() { drag.cancel('pinch'); } });
```

When the drag and the pinch share an element, the drag's release on cancel also drops the pinch's
capture of that finger. The pinch re-asserts both captures right after `onStart`, so the hand-over
never rolls the pinch back. A host whose gesture surface is a DESCENDANT listener (the PdfViewer
annotator draws its ink stroke with Blazor pointer events on a surface inside the pinch element)
passes `captureSinglePointer: false`: the pinch then captures only once it actually starts, so the
lone finger's move/up keep reaching the surface, and a document-level capture listener prunes a
lone finger that ended elsewhere. After `onStart` the pinch OWNS both fingers' capture: a host that must
keep a finger elsewhere cancels the pinch from `onStart` (`pinch.cancel('host')`) instead of
relying on the re-assert. And when the re-assert itself fails (an engine that fires
`lostpointercapture` synchronously on release leaves the pointer uncapturable), the pinch can no
longer track the finger: it cancels itself with `lostpointercapture` instead of committing a
half-captured gesture.

**A pinch is never the only way (WCAG 2.5.7).** Zoom buttons (+/−) beside the canvas are the
single-pointer alternative and must ship in the same change; `onChange` already carries the scale a
button press needs, so one commit path serves both.

`createAutoScroller(container, { edge = 36, maxStep = 28, axis = 'both', onScroll })` is the edge
auto-scroll for drags, ported from `spreadsheet-canvas.js`: within `edge` px of a container edge it
scrolls `round(power * maxStep)` px per frame (0 in the middle) on a rAF loop with no listeners of
its own — the host calls `scroller.update(x, y)` from its drag's `onMove` and `scroller.stop()` from
`onRelease`. Unlike spreadsheet-canvas, the power is **capped at 1 past the edge**: a pointer dragged
beyond the edge scrolls at full `maxStep` per frame, never faster. The scroll position is read back
after each write, so `onScroll({ dx, dy })` and the accumulators carry only the **applied** delta —
the browser clamps at the scroll limits, and a frame at a limit applies nothing and stays silent.
The scroller is created once per component and reused across gestures: `stop()` ends
the loop, the next `update()` starts a new one, and the accumulation continues.
`scrolledX`/`scrolledY` accumulate everything applied since creation, so the content
delta under a stationary pointer is `dx + scrolledX`; `onScroll({ dx, dy })` fires after each
scrolled frame so the host can re-hit-test the drop target. `container = null` means the page itself
(`document.scrollingElement`), edge-tested against the **viewport** rect
(`documentElement.clientWidth`/`clientHeight`), not the document box its `getBoundingClientRect()`
returns. The scroll is raw `scrollLeft`/`scrollTop`: a host with RTL scroll
semantics applies its own logical translation. The scroller needs no alternative of its own — it
only assists an existing drag, whose alternative carries the WCAG duty.

## A drag is never the only way (WCAG 2.5.7)

WCAG 2.5.7 *Dragging Movements* (WCAG 2.2, level AA): anything done by dragging must also be possible with a
single pointer press, and a keyboard user cannot drag at all. **A component that adopts the helper
must ship a non-drag alternative in the same change.** The convention:

| Gesture | Keyboard alternative | Announcement |
|---------|----------------------|--------------|
| Move an item (event, card, node) | Alt+arrow (`Alt+ArrowDown`) moves it one step (slot, day, grid cell) | `aria-live="polite"` message "Moved to …" |
| Resize an item | Alt+Shift+arrow grows/shrinks by one step | "Resized to …" |
| Resize a panel (splitter) | arrows / `Home` / `End` on the `role="separator"` (`TmEditorShell`, `TmSplitterPane`) | `aria-valuenow` |
| Zoom a canvas (pinch) | zoom buttons (+/−) beside the canvas (`pointer-gesture.js` hosts) | none needed |
| Cancel | `Escape` while a mouse/touch gesture is active (handled by the helper) | none needed |

`Alt`+arrow is the browser's Back/Forward shortcut, so the chord is cancelled in JavaScript, not with
a Blazor `@onkeydown:preventDefault` (that would swallow Tab too). `attachKeyboardMove(el, { onStep })`
listens on the focused item and calls `preventDefault` only for `Alt`+an arrow, then reports
`{ key, x, y, shift }` (`ArrowLeft` is `{ x: -1, y: 0 }`, `shift` is `false` for the plain chord). A
plain arrow, and every other key, is left to the browser. With `{ shift: true }` the helper also
accepts `Alt`+Shift+arrow — the resize alternative, e.g. growing a Scheduler event — and reports
`shift: true`; without the option `Alt`+Shift+arrow stays ignored and unprevented. A canvas editor
that already nudges with plain arrows may keep them and not call this. The scheduler's resize
alternative is Alt+Shift+arrow on the end only (see the scheduler row below).

The alternative reuses the commit path: `Alt`+arrow calls the same .NET method the drag's `onEnd`
does, so there is one code path to test and one place a read-only guard lives. Pointer-only
affordances (a visible `+`/`−` button, a menu command "Move to…") satisfy "single pointer" for
touch users who cannot drag precisely.

## Adopters

| Module | What it does with the helper |
|--------|------------------------------|
| `tm-editor-shell.js` | Panel resizer: `attach(handle, { threshold: 0 })`; `onMove` clamps the width and writes the CSS variable, `onEnd` commits once, `onCancel` restores the start width. The keyboard path stays in Blazor and shares the clamp vectors. |
| `tm-splitter.js` | `TmSplitterPane` resizer: `attach(handle, { threshold: 0, suppressClick: false, touchAction: null })`; `onMove` clamps the pane size and writes the inline `flex` (no Blazor render per move), `onEnd` commits once through `OnSplitterResizeCommitted`, `onCancel` restores the press-time `style` attribute verbatim (the drag's `flex` shorthand would otherwise leave the authored inline `flex-basis` destroyed), and the horizontal delta inverts in RTL. A press on the strip's collapse button is vetoed so the button keeps its click. Arrows / `Home` / `End` on the `role="separator"` stay in Blazor and share the clamp. |
| `tm-sheet.js` | Bottom-sheet drag (TmDrawer/TmModal/TmDialog sheets): `attach(handle, { threshold: 6, captureOn: <grabber ? 'press' : 'start'>, touchAction: null, suppressClick: true, dragClass: null })`. The grabber captures on press (a fast drag whose first move leaves the 44px handle would otherwise never start); a header waits for the 6px arm so a close-button click stays a click. The sheet owns measuring, the inline height and the settle/dismiss decision; the velocity samples come from `onMove`'s `event.timeStamp`. |
| `scheduler.js` | Event resize and touch move. The three selectors are `.tm-scheduler-event-resize-handle` (resize, `longPressMs: 400`), `.tm-scheduler-timegrid-event[data-event-id][draggable="true"]` / `.tm-scheduler-timeline-event[data-event-id][draggable="true"]` / `.tm-scheduler-month-event[data-event-id][draggable="true"]` (touch move, `pointerTypes: ['touch']`, `longPressMs: 400`). A coarse pointer shows the 44px handles on the selected or focused event (a compact selected event hides the start grip). The time grid renders a top and a bottom handle, the timeline a left and a right handle, and the keyboard resize changes the end only. The keyboard alternative is per view: time grid Alt+Left/Right moves a day and Alt+Up/Down a slot, Alt+Shift+Up/Down resizes the end; timeline Alt+Left/Right moves a slot and Alt+Up/Down a resource, Alt+Shift+Left/Right resizes the end; month Alt+Left/Right moves a day and Alt+Up/Down a week, with no resize. A plain arrow stays the roving key, and a read-only event is not a keyboard target. Both gestures commit through the same C# path as the HTML5 drop. |
| `data-table.js` | `TmDataTable` column resize: one delegated `attach(root, { selector: '.tm-col-resize-handle', threshold: 0, suppressClick: false, touchAction: null })` per table (the classic script loads the helper with a dynamic `import()` resolved from `document.currentScript.src`). `onPress` measures the real `<th>` (and vetoes the gesture when the measure fails), `onMove` rAF-throttles live widths into `OnColumnResized`, `onEnd` flushes the final width and commits once through `OnColumnResizeCommitted`, and `onCancel` reports `OnColumnResizeCancelled` so the C# snapshot restores (or removes) the pre-gesture width — a cancelled drag persists nothing. The horizontal delta inverts in RTL. Arrows / `Home` / `End` on the `role="separator"` stay in Blazor, starting from `tmDataTable.measureColumn`; the axis keys are prevented here. |
| `tm-color-gradient.js` | `TmColorGradient` saturation/value, hue and alpha gestures: one `attach(el, { threshold: 0, suppressClick: false, touchAction: null })` per slider part (a static `import { attach } from './pointer-drag.js'`). Positions stream as fractions of the element's real bounding box through rAF-throttled `OnGradientDrag` calls, a tap commits through the same `OnGradientCommit`, and `onCancel` reports `OnGradientCancel` so the C# snapshot restores the colour the press started from. The keyboard sliders stay in Blazor; only the axis keys and `Home`/`End` are prevented here. The horizontal mapping is deliberately not RTL-inverted — the painted gradient runs left-to-right in every writing direction. |
| `workflow-designer.js` | `TmWorkflowDesignerCanvas`: three delegated `attach(svg, …)` sessions — ports (transition creation, threshold 2, the drop is hit-tested with `document.elementFromPoint` because the pointer is captured by the port), states (node drag, threshold 4, touch starts immediately — a canvas owns its surface) and the empty canvas (pan) — plus `attachPinch` (two-finger zoom + ctrl+wheel around the cursor; plain wheel stays the page's) with the pinch's `onStart` cancelling the host's active drag (`drag.cancel('pinch')`), and `createAutoScroller(null)` fed from the node drag's `onMove` (the node is re-positioned from the scrolled pointer on every applied frame). A canvas editor accepts `touch-action: none` on the svg (CSS owns it). `attachKeyboardMove(svg, { selector: '[data-state-id]' })` is the move alternative — one grid step per Alt+arrow through the SAME `OnNodeKeyboardMoved` commit path as the drag, announced on a `role=status` live region — with Delete mapping to the context menu's delete and the roving-focus arrows cancelled in JS. `role=img` on the svg became `role=group` (img hides the focusable states from assistive technology), states are `role=button` tab stops (Enter selects, Space on keyup), Shift+F10 — or a right-click, the svg `contextmenu` event, which cancels the gesture in flight — opens the context menu (focus moves to its first item, Escape closes it and returns the focus to the state) whose "Connect to…" entry is the non-drag transition alternative, and the overlay zoom buttons are the pinch alternative. The canvas is a classic script: both helpers load with a dynamic `import()` resolved from its own URL. |
| `pdf-viewer.js` | `TmPdfViewer` (FZE-9): `attachPinch` on the active scroll wrap (canvas wrap / continuous wrap) with `touchAction: null` — CSS owns `touch-action: pan-x pan-y` there, so one finger still scrolls while the core pinch owns the zoom (and blocks the native page pinch); the live gesture is a CSS transform around the fingers' start centre, and `onEnd` clears it, turns the two-finger pan into scroll and commits through `OnPinchZoomEnded` (clamped, `ZoomMode=Custom`, one re-render — the zoom dropdown is the WCAG 2.5.7 alternative). The package's split-package import walks up to the core sibling. The pinch passes `captureSinglePointer: false` because the annotator's ink surface is a descendant listener inside the wrap; the ink stroke is Blazor state, not a pointer-drag session: its own `pointerdown` handler cancels it on a second touch finger, a `pen` landing replaces a live finger stroke (the pen has priority) and a palm touch during a pen stroke is ignored. The pinch's `onStart` additionally raises `OnPinchStarted` through the viewer's `DotNetObjectReference`, and the annotator — subscribed through its viewer reference — discards the live stroke without committing it; a `pointercancel` on the surface discards it the same way. The pinch commit records a scroll anchor (the transform origin + the scale ratio) that the next `renderPage`/`updateContinuousScale` completion applies, so the point under the fingers stays put; a non-passive `touchmove` listener on the wrap (bound to the instance abort signal) keeps the native two-finger page zoom out of the gesture. The annotator's draw surface claims `touch-action: none`; the comment/stamp surfaces keep the native pan/pinch (a review fix — an active comment surface used to block both on a phone). Text selection reports on `selectionchange` + `pointerup` (there is no `mouseup` on touch) and the text layer paints its text `color: transparent` (never an element opacity, which would fade the native selection highlight) with the highlight color in the scoped `::selection` rule. FZE-14 extends the annotator surface PER PAGE: in Continuous the ink/comment/stamp surfaces mount inside each page's overlay slot (the FZE-13 geometry — no second measurement, no second gesture session), the pointer handlers normalize against that slot's box so a stroke always anchors on the page whose slot received the event, an ink stroke is confined to the page where it started (a stroke dragged across the boundary is clipped there), and the ONE shared pinch on the scroll wrap keeps owning two fingers. |
| `spreadsheet-canvas.js` | `TmSpreadsheet`'s canvas grid (FZE-7): one `attach(root, { pointerTypes: ['touch'], longPressMs: 500, threshold: 8, touchAction: null, captureOn: 'press', suppressClick: false })` session drives the tap (select) / double tap (edit, with the cell type's `inputmode`) / long-press (the cell menu, plus a 400 ms release-click guard so the lifting finger's compatibility click does not close it) and ABORTS on a threshold drag so the finger pans natively; the selection-extension and fill drags stay on the canvas's own pointerdown/move/up path, reached through the DOM touch-handle layer (`[data-touch-handle]`, `touch-action: none`, hidden on `(pointer: fine)`), and a second `attachPinch(root, { pointerTypes: ['touch'], wheel: false, touchAction: null, captureSinglePointer: false })` session commits the zoom through `OnZoomChanged` in whole 10 % steps (its `onStart` cancels the single-pointer session). CSS owns `touch-action: pan-x pan-y` on the grid. The WCAG 2.5.7 alternatives are Shift+arrow / tapping an edge drop (selection) and the cell menu's Fill down / Fill right plus Ctrl+D / Ctrl+R (fill). See [spreadsheet.md](spreadsheet.md#touch-and-pen). |
| `wireframe-designer.js` | `TmWireframeDesignerCanvas`: `attach(svg, …)` sessions for resize, rotate, connector waypoint, the body gesture (select, element drag, connector drag, connector tool, rubber-band, pan) and a touch-only hold (`longPressMs: 500`, opens the context menu offset from the finger), plus `attachPinch` (two-finger zoom + pan, `wheel: false` - the canvas keeps its own wheel/ctrl+wheel zoom) whose `onStart` cancels every single-finger session. All are attached with `touchAction: null`: CSS owns `touch-action: none` on the svg. The sessions are attached after a dynamic `import()` resolved from `document.currentScript.src` (classic script); `wireframe-gestures.mjs` holds only the pure maths (double-tap, coarse handle sizes, pinch to viewBox, nudge). The non-drag alternatives are the arrow-key nudge, the inspector X/Y/W/H fields and the zoom buttons. See [wireframe-editor.md](wireframe-editor.md#touch-and-pen-phase-5). |

Gantt, Dashboard, DiagramEditor, Wireframe, the Signing designer and the PdfViewer package adopt
it in their own plans; none
of them may add a second implementation.

## Diagram editor touch notes (occlusion, release-click guard, contact tracking)

`diagram-editor-touch.js` stacks several delegated sessions on ONE container (node drag,
long-press context menu, port edge draw, pan, resize, rotate, pinch). Two conventions keep the
stack unambiguous:

- **Occlusion.** Sessions that resolve their target through the hit stack (`engine.touchPortAt`)
  only claim a press whose RAW target is that control itself (or an edge corridor painting over
  it). A port buried under a resize handle or a node body is COVERED, not pressed — the topmost
  control owns the press, and only one session claims a pointer. Conversely, the finger-sized
  `.tm-diagram-port-hit` circles render after the transform handles on purpose: where they paint,
  the port drag wins.
- **Release-click guard.** A touch long-press opens the context menu while the finger is still
  down, so the compatibility `click` of the lifting finger would land on a menu item. The guard is
  armed per hold (set when the menu opens, disarmed on the first swallowed click or any new
  `pointerdown`) and, after the lift, swallows clicks for 350 ms — scoped to THIS editor's menu by
  comparing `closest('.tm-diagram-editor')` against the canvas's own editor root. A tap that never
  opened a menu arms nothing.
- **Contact tracking.** The stack keeps a `Set` of the touch `pointerId`s currently down (added on
  the container's capture `pointerdown`, deleted on the DOCUMENT's capture `pointerup` /
  `pointercancel` / `lostpointercapture` — a captured finger lifts off the element — and cleared
  by any mouse/pen press). The engine's `_isTouchContextMenu` consults the set so the native
  touch `contextmenu` (Android/Windows) is swallowed while the long-press session owns the menu —
  but only when the touch stack is actually attached (`inst.__touchDetach` exists).

## Testing

`src/Tempo.Blazor/wwwroot/js/__tests__/pointer-drag.test.mjs` drives the helper with hand-rolled
DOM stubs (`npm run test:overlay`), and `__tests__/pointer-gesture.test.mjs` does the same for
`pointer-gesture.js` (pinch sessions, ctrl+wheel, the auto-scroller). `captureModelElement()` in
`pointer-gesture.test.mjs` models the pending-capture rules; a fake that releases synchronously
cannot see hand-over bugs. A node test with fake pointer
events cannot prove a gesture in a
browser: drive every JS-owned gesture with a real Playwright mouse/touch drag and assert the result
(width, persisted value, notified delta).

The F7 E2E lanes write diagnostic shots under `tests/Tempo.Blazor.E2E/TestResults/` (`pointer-drag/`,
`relative-time/`). That folder is gitignored (`[Tt]est[Rr]esult*/`), so those shots are not committed
evidence and a fresh clone does not contain them. They are not a baseline. A
baseline that must survive a clone belongs under `__screenshots__/`, which these lanes deliberately
do not use.
