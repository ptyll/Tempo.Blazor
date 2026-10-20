# Cluster E — pointer gestures and scheduler interaction (read-only analysis)

Tree: `main @ 846bc242`. Sources read: `AGENTS.md`, `docs/touch-interaction.md`, `docs/redesign-foundation.md`,
decision `F7-POINTER-DRAG-PLURALS-RELATIVE-TIME`, completion report of plan `faf503eb`, the Scheduler plan
`09c5afa6` (phases 2, 5, 6, 7 read in full) and the phase texts of the DataTable, Dashboard, Gantt,
DiagramEditor, Wireframe, Signing, Modeling, PdfViewer, Spreadsheet, DocumentEditor and NotionEditor plans
(verbatim extraction by a sub-agent: `C:\Users\tyllp\AppData\Local\Temp\plangrep\<Plan>.md`).

Verification runs, all on this tree:
- `node --test src/Tempo.Blazor/wwwroot/js/__tests__/pointer-drag.test.mjs …/scheduler-pointer.test.mjs …/tm-sheet.test.mjs` → **120/120 pass**.
- `dotnet test tests/Tempo.Blazor.Tests -f net10.0 --filter "TmSchedulerDragTests|TmSplitterTests|TmColorGradientTests|TmRangeSliderTests|TmDataTablePinResizeTests|TmWorkflowDesignerCanvasTests"` → **92/92 pass**.
  These are the baseline tests a migration must keep green (or deliberately rewrite).

## 0. Cross-cutting facts every phase below relies on

**F0-1. The helper API is complete for single-pointer drags.** `src/Tempo.Blazor/wwwroot/js/pointer-drag.js`
(424 lines) exports `attach(el, opts)` (lines 121–424) and `attachKeyboardMove(el, opts)` (lines 61–86) plus
`prefersReducedMotion()`. Use the options that already exist: `threshold`, `longPressMs`, `selector`,
`pointerTypes`, `captureOn: 'press'|'start'`, `cancelOnEscape`, `suppressClick`, `dragClass`, `touchAction`.
The callbacks are `onPress`/`onStart`, which may veto by returning `false`, then `onMove`, `onEnd`, `onCancel`
and `onRelease`.

**F0-2. GAP: `attachKeyboardMove` cannot report Alt+Shift+Arrow.** `pointer-drag.js:66`:
```js
if (!event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
```
`docs/touch-interaction.md:135` prescribes "Alt+Shift+arrow grows/shrinks by one step". The Scheduler plan
F7 also asks for "Alt+Shift+šipky resize", but the helper drops that chord on purpose today. This is a
**core** change, and the Scheduler keyboard resize is blocked until it lands. → Core phase **E-K** below.

**F0-3. Import rule.** Use a *relative* specifier. A module in `wwwroot/js/` uses `import { attach } from './pointer-drag.js'`.
A classic script resolves the URL from `document.currentScript.src`, captured synchronously at the top of the
file; `scheduler.js:16-24` is the reference. A split package uses `'../../Tempo.Blazor/js/pointer-drag.js'`.
A bare `'_content/Tempo.Blazor/js/pointer-drag.js'` throws `TypeError` (docs/touch-interaction.md:39-42).

**F0-4. Reference adopter for a resizer** (copy this pattern):
- `src/Tempo.Blazor/wwwroot/js/tm-editor-shell.js:110-163`: `attachResize` with `threshold: 0`, live CSS write, one commit in `onEnd`, restore in `onCancel`, and a registry keyed by id.
- `src/Tempo.Blazor/Helpers/EditorShellInterop.cs`: lazy `import`, the `IsBenign` catch list, and detach on dispose.
- `TmEditorShell.razor:597-613`: the `role="separator"` markup. `TmEditorShell.razor.cs:71-84`: keyboard steps of 16 px, Shift 64 px, Home/End.

**F0-5. The click-target trap with pointer capture.** A handle that captures on press receives the
`pointerup`, so the `click` lands on the handle and not on a child button. Any handle that contains a
clickable child must either veto in `onPress` when `info.event.target.closest('<child selector>')` matches,
or use `captureOn: 'start'`. This applies to `TmSplitterPane` (collapse button) and to the `tm-sheet` header (close button).

**F0-6. `preventDefault` on `pointerdown`** (an immediate drag) suppresses the compat `mousedown`/`mouseup`
events in that subtree. Any `@onmousedown` child must become `@onpointerdown` (docs/touch-interaction.md:102-106).

**F0-7. E2E harness to reuse.** `tests/Tempo.Blazor.E2E/PointerDragE2ETests.cs`:
- context factory (lines 42-65) with `HasTouch`, `IsMobile = touch && width < 640`;
- CDP touch via `TouchAsync(session, "touchStart"|"touchMove"|"touchEnd", x, y)` (lines 408-415) and `touchCancel` (line 191);
- the 450 ms wait for a long-press (line 157);
- `ScrollOffsetsAsync` (lines 399-406) to prove the page still scrolls.

Diagnostic screenshots go to the gitignored `TestResults/pointer-drag/`. All of these tests are `[TestCategory("WASM")]` on `WasmTestBase`.

---

## 1. [59e85790] + [8b353a98] Scheduler: touch MOVE, Alt+Arrow move/resize, aria-live, live target time

### 1.1 Status: **PARTLY DONE**

Done by plan faf503eb F7, with proof:
- `scheduler.js` resize runs on the helper: `scheduler.js:51-68` (`helper.attach(document, { selector: '.tm-scheduler-event-resize-handle', threshold: 2, longPressMs: 400, … })`).
  It resolves `helperUrl` from `document.currentScript.src` (lines 16-24).
- The contract is unchanged: `window.TempoBlazor.Scheduler.init/registerInstance/unregisterInstance/destroy/cancelResize` (lines 46-273).
  `[JSInvokable] NotifyResizeComplete(string eventId, int deltaMinutes, string resizeEdge)` is at `TmSchedulerTimeGrid.razor:467-471` and `TmSchedulerTimelineView.razor:289-293`.
- Coarse handles: `_scheduler-timegrid.css:268/288` and `_scheduler-timeline.css:122/140` (`touch-action: pan-y` / `pan-x` under `(pointer:coarse)`, `min(var(--tm-space-4),25%)`).
- Node tests: `__tests__/pointer-drag.test.mjs` (attachKeyboardMove test at line 820) and `__tests__/scheduler-pointer.test.mjs` (476 lines, resize only).
- E2E: `PointerDragE2ETests.cs` has 10 scheduler tests covering mouse resize and Escape, touch at 1024/390/768, swipe-scroll, pointercancel, pen, and the HTML5-drag regression.
- Docs, CHANGELOG and AGENTS are done: `docs/touch-interaction.md`, `CHANGELOG.md:516-523` (Breaking: scheduler.js imports pointer-drag.js, CSP) and `:572` (Added), and AGENTS.md "JavaScript Interop".

**OPEN** (all verified absent):

| Gap | Evidence |
|---|---|
| Touch move | Move is HTML5 DnD only: `draggable` + `ondragstart` (`TmSchedulerTimeGrid.razor:226-227`, `TmSchedulerTimelineView.razor:58-59`, `TmSchedulerMonthView.razor:40-41`). Drop happens per slot/day: `TimeGrid.razor:202-203` → `HandleSlotDrop` 411-421; `Timeline.razor:39-40` → 225-242; `Month.razor:23-24` → `HandleDrop` 155-167. Touch never fires `dragstart` reliably. |
| Events not focusable | No `tabindex`/`role` on `.tm-scheduler-timegrid-event` (TimeGrid 221-231), `.tm-scheduler-timeline-event` (Timeline 54-63) or `.tm-scheduler-month-event` (Month 37-45). Scheduler plan **F2** adds `role=button tabindex=0`. |
| Keyboard move/resize | No keydown anywhere in Scheduler views. |
| aria-live | No live region in `TmScheduler.razor` (1-84). No `TmScheduler_EventMoved/Resized` keys in `TmResources.json` (only Agenda, AllDay, Day, Month, More, Next, NoEvents, Prev, Print, Timeline, Today, Week). |
| Live target time | `_move` (scheduler.js:160-196) only changes `style.height/top/width/left`; no label. |
| Month view has no JS registration | `TmSchedulerMonthView.razor` has no `registerInstance`/`DotNetObjectReference`, and the month events have **no `data-event-id`** (37-45). |
| Minutes per pixel assume 24 h | `scheduler.js:149` `minutesPerPixel: (24 * 60) / containerSize`. Plan F5 changes this to `data-range-*`. |
| Alt+Shift chord | Blocked by F0-2. |

### 1.2 What the Scheduler plan 09c5afa6 already covers

- **Fáze 7 "Dotykové ovládání — pilot jádrového pointer-drag.js (B13)"** (`fze-7-dotykov-ovldn-pilot-jdrovho-pointer-dragjs-b13`, depends-on Fáze 5, size L) covers *all* open items:
  - touch/pen move with long-press in Month/TimeGrid/Timeline through a shared `[JSInvokable] NotifyMoveComplete`;
  - the mouse stays on HTML5 DnD, and mouse/pointer are unified only with a recorded decision;
  - `touch-action` that keeps scrolling;
  - "Alt+šipky přesun o slot/den, Alt+Shift+šipky resize, Esc zruší", aria-live, docs;
  - bUnit `TmSchedulerDragTests.JSInvokable_Move_Complete_…`, `…Resize_Respects_ReadOnly`, `…Move_Between_Resources_In_Timeline`, `Keyboard_AltArrow_Moves_Event_By_Slot`, `Keyboard_AltShiftArrow_Resizes_Event`, `Keyboard_Move_Ignored_When_ReadOnly`;
  - node `scheduler-pointer.test.mjs` move maths;
  - E2E `SchedulerTouchE2ETests` (1024 Week+Timeline, 390 Day long-press, desktop DnD regression, keyboard);
  - localization keys `TmScheduler_MoveHint`, `TmScheduler_EventMoved` ("Přesunuto na {0}"), `TmScheduler_EventResized`;
  - Gantt tests green.
- **Fáze 2** (a11y) makes events `role=button tabindex=0` with Enter/Space. That is a hard prerequisite for Alt+Arrow, which needs a focusable event.
- **Fáze 5** changes `scheduler.js` `minutesPerPixel` (line 122 in the plan text; the real line is 149) to `data-range-start`/`data-range-minutes`, adds `ShowFullDay` and `ScrollToTime`, and keeps `NotifyResizeComplete`. The move maths must use the same range.
- **Live target time while dragging** is **not** mentioned in any Scheduler phase. It is only in the foundation remaining-task text.

**Stale text in Fáze 7** (because faf503eb F7 already shipped the helper and the resize pilot):
1. "přepis `mousedown/mousemove/mouseup` (ř. 27, 124–221) na helper" — **done**.
2. "helper načíst dynamickým `import('_content/Tempo.Blazor/js/pointer-drag.js')`" — **wrong**. That is a bare specifier, which throws (F0-3). The shipped code resolves from `document.currentScript.src`.
3. The coordination sub-bullets (b)/(c), "pointer-drag.test.mjs pokud helper vzniká zde", the CHANGELOG "pointer-drag.js pokud vzniká zde" and AGENTS.md — **done/obsolete**.
4. "resize handly 12–16 px na pointer: coarse" — done as `min(var(--tm-space-4),25%)` with a visible grip under `hover:none`.
5. "Alt+Shift+šipky resize" — needs core E-K first (F0-2).

### 1.3 Recommendation: **leave the implementation to Scheduler plan Fáze 7**, plus two small core pieces

Why it stays in the Scheduler plan: move needs focusable events (F2), the range-based minutes (F5) and the F1–F5 markup rewrites. Doing it in a core follow-up plan would collide with those phases on the same razor lines.

The core follow-up plan contributes:
- **E-K (S):** `attachKeyboardMove` option `shift: true` (section 1.5).
- Optionally **E-A (M):** a `createAutoScroller` for dragging an event to the edge of a scrolled grid (section 4).

### 1.4 Exact replacement text for Scheduler plan Fáze 7 (Czech, paste over the matching bullets)

Replace the bullet "Koordinace s F7 …" (including all sub-bullets) with:
```
- [ ] Koordinace s F7: plán základu faf503eb je Completed, helper `src/Tempo.Blazor/wwwroot/js/pointer-drag.js`
  (`attach`, `attachKeyboardMove`, `prefersReducedMotion`) a pilot resize ve `scheduler.js` už existují —
  jen je napoj. Ověř, že ve `pointer-drag.js` je volba `attachKeyboardMove(el, { shift: true })`
  (follow-up plán jádra, fáze E-K); bez ní Alt+Shift+šipky helper zahodí (`pointer-drag.js:66`) —
  v tom případě nejdřív E-K, nikdy vlastní keydown s preventDefault. Scheduler-specifický helper je zakázán.
```
Replace the TDD RED sub-bullet about `pointer-drag.test.mjs`/`scheduler-pointer.test.mjs` with:
```
  - JS testy: rozšířit existující `src/Tempo.Blazor/wwwroot/js/__tests__/scheduler-pointer.test.mjs`
    (dnes jen resize) o move: long-press 400 ms prstem na těle události → přesun, rychlý swipe = scroll
    (nic se neposílá), myš a pero na těle NEspouští pointer move (HTML5 DnD zůstává), výpočet cílového
    dne/minuty/zdroje z `data-date`/`data-resource` a `data-range-*` se snapem na `data-slot-duration`,
    Escape/pointercancel vrátí událost bez volání .NET, `NotifyMoveComplete` přesně jednou;
    attachKeyboardMove: Alt+šipka → `NotifyKeyboardStep(eventId, x, y, false)`, Alt+Shift+šipka → `…, true)`;
    běží `npm run test:overlay`.
```
Replace the bullet "`scheduler.js`:" (all four sub-bullets) with:
```
- [ ] `scheduler.js` (resize už běží na helperu, ř. 51–68; NEpřepisovat):
  - druhé `helper.attach(document, { selector: MOVE_SELECTOR, pointerTypes: ['touch'], longPressMs: 400,
    threshold: 4, onPress/onStart/onMove/onEnd/onCancel/onRelease })`, kde
    MOVE_SELECTOR = '.tm-scheduler-timegrid-event[data-event-id], .tm-scheduler-timeline-event[data-event-id],
    .tm-scheduler-month-event[data-event-id]'; `onPress` vrací false, když cíl leží v
    '.tm-scheduler-event-resize-handle' nebo `draggable !== 'true'` (read-only);
  - `attachKeyboardMove(document, { selector: MOVE_SELECTOR, shift: true, onStep })` → `NotifyKeyboardStep`;
  - obě instance se odpojují v `_detachDrag`/`destroy` (ř. 259–273) a hlídá je `_generation`;
  - helper se NEnačítá bare specifierem: `helperUrl` z `document.currentScript.src` (ř. 16–24) zůstává;
  - zachovat `window.TempoBlazor.Scheduler.init/registerInstance/unregisterInstance/destroy/cancelResize`
    a `[JSInvokable] NotifyResizeComplete(string eventId, int deltaMinutes, string resizeEdge)`
    (`TmSchedulerTimeGrid.razor` ř. 467–471, `TmSchedulerTimelineView.razor` ř. 289–293).
```
Add to the bullet "Přesun:":
```
  - Month view se dnes do JS neregistruje a události nemají `data-event-id` (`TmSchedulerMonthView.razor`
    ř. 37–45): doplnit `data-event-id`, `data-date` na `.tm-scheduler-month-day`, `@ref` + registerInstance/
    unregisterInstance jako TimeGrid (ř. 167–182, 473–486);
  - `data-date="yyyy-MM-dd"` na `.tm-scheduler-timegrid-col` / `-resource-col` (TimeGrid ř. 99, 110) a na
    kořen `.tm-scheduler-timeline`; zdroj z existujícího `data-resource`;
  - během tažení: třída `tm-scheduler-event--moving` (pointer-events: none, transform translate), živý
    štítek cílového času `.tm-scheduler-event-drag-time` („14:30–15:30“, formát HH:mm jako C# ř. 236)
    — stejný štítek i při resize; cíl přes `document.elementFromPoint`;
  - jedna C# cesta commitu `MoveEventAsync(evt, newStart, resourceId)`, kterou volají HTML5 drop
    (`HandleSlotDrop`/`HandleDrop`), `NotifyMoveComplete(string eventId, string date, int minuteOfDay,
    string? resourceId)` (minuteOfDay = -1 v Month = zachovat čas) i klávesnice; read-only guard jen tam.
```
Replace the bullet "Klávesnicová alternativa: …" with:
```
- [ ] Klávesnicová alternativa (WCAG 2.5.7, docs/touch-interaction.md tabulka): na fokusované události
  (role/tabindex z Fáze 2) Alt+šipky přesun (TimeGrid: ↑/↓ slot, ←/→ den; Timeline: ←/→ slot, ↑/↓ zdroj;
  Month: ←/→ den, ↑/↓ týden), Alt+Shift+šipky resize konce o slot (TimeGrid ↑/↓, Timeline ←/→; Month nic),
  přes `attachKeyboardMove(..., { shift: true })` → `[JSInvokable] NotifyKeyboardStep(eventId, x, y, shift)`
  → stejné `MoveEventAsync` / `HandleResizeComplete`. Každý krok se commituje hned (žádný grab mode),
  takže „Esc zruší“ platí jen pro probíhající pointer gesto (řeší helper). Po commitu fokus zůstane na
  přesunuté události (`@key="e.Id"`), oznámení `TmScheduler_EventMoved`/`TmScheduler_EventResized`
  do jednoho `<div class="tm-sr-only" role="status" aria-live="polite">` v `TmScheduler.razor`.
```
Drop from the CHANGELOG/JSON-doc/AGENTS bullets the phrase "`pointer-drag.js`, pokud vzniká zde"; it already exists.

### 1.5 Core piece E-K: `attachKeyboardMove` option `shift`

- **JS** `pointer-drag.js:58-86`:
  - Extend the JSDoc options with `shift?: boolean`.
  - Line 66 becomes:
    ```js
    if (!event.altKey || event.ctrlKey || event.metaKey) return;
    if (event.shiftKey && options.shift !== true) return;
    ```
  - Line 74 becomes `options.onStep?.({ key: event.key, x: step.x, y: step.y, shift: event.shiftKey === true, event });`.
  - Default behaviour is unchanged: without `shift: true`, Alt+Shift+Arrow is still ignored and not prevented.
- **Node RED tests** (append after `pointer-drag.test.mjs:844`):
  - `attachKeyboardMove ignores Alt+Shift+Arrow by default (not prevented, no step)`;
  - `attachKeyboardMove with shift:true reports Alt+Shift+ArrowDown as {x:0,y:1,shift:true} and prevents it`;
  - `shift:true still ignores Ctrl/Meta chords`;
  - `the plain Alt+Arrow step reports shift:false`.
- **Docs**: in `docs/touch-interaction.md:139-144`, document `shift` and `step.shift`. Also in `docs/redesign-foundation.md:213`.
- **CHANGELOG** `## 2.9.1 (unreleased)` → `### Added`: "`attachKeyboardMove` accepts `{ shift: true }` and reports `step.shift` for Alt+Shift+Arrow (resize alternative)." Not breaking.
- **Verification**: `npm run test:overlay`.
- **Effort: S.**

### 1.6 RED tests the Scheduler plan should own

The Scheduler plan already lists these; they are restated here with concrete selectors for a weak implementer.
- **bUnit** `tests/Tempo.Blazor.Tests/Components/Scheduler/TmSchedulerDragTests.cs`:
  - `TimeGrid_NotifyMoveComplete_MovesEventToDateAndMinute_AndRaisesOnEventChangedOnce` (`cut.InvokeAsync(() => cut.Instance.NotifyMoveComplete("e1","2026-10-14",600,null))` → Start 10:00, duration kept);
  - `Timeline_NotifyMoveComplete_ChangesResource`;
  - `Month_NotifyMoveComplete_KeepsTimeOfDay` (minuteOfDay -1);
  - `NotifyMoveComplete_ReadOnly_Or_EventReadOnly_IsIgnored`;
  - `NotifyKeyboardStep_AltDown_MovesOneSlot`, `…AltRight_MovesOneDay`, `…Shift_ResizesEndOneSlot`, `…ReadOnly_Ignored`;
  - `Month_Events_Have_DataEventId`, `TimeGrid_Columns_Have_DataDate`;
  - `TmScheduler_Renders_PoliteStatusRegion_And_AnnouncesMove`.
- **Node**: the extension of `scheduler-pointer.test.mjs` per the replacement text above. Reuse the `timegrid()`/`timeline()` stubs (lines 56-81). Stubs need `data-date` and an `elementFromPoint` stub on `doc`.
- **E2E** `tests/Tempo.Blazor.E2E/Scheduler/SchedulerTouchE2ETests.cs` (or extend `PointerDragE2ETests`), using demo `/scheduler` `[data-testid='scheduler-interactive']` and event `i4`:
  - `Touch_LongPressMove_Day_390` and `…_Week_768`: CDP touchStart, wait 450 ms, touchMove by 2 slots, touchEnd → `scheduler-last-action` contains the title, and the event `top` changed by 2 slots;
  - `Touch_QuickSwipeOnEventBody_StillScrolls` — already exists for resize (`Scheduler_Touch_SwipeFromEventCentre_Scrolls_AndDoesNotResize`); assert also no move;
  - `Mouse_Html5Drag_StillMoves_1440` (regression);
  - `Keyboard_AltArrowDown_MovesFocusedEvent` (`page.Keyboard.PressAsync("Alt+ArrowDown")`, assert no navigation via `page.Url` unchanged);
  - `Keyboard_AltShiftArrowDown_Resizes`;
  - `DragShowsLiveTimeLabel`.

### 1.7 Breaking / CHANGELOG
Not breaking: additive `[JSInvokable]` methods and `data-*` attributes. CHANGELOG `Added` lines: touch move, keyboard move/resize and the live region. In the views the new `data-date` attributes are additive. If `TmSchedulerMonthView` gains `IAsyncDisposable`, that is not a public-contract break.

### 1.8 Effort
- Scheduler F7 remainder: **L**, as the plan says (4–6 days).
- Core E-K: **S**.
- Plan-text update: **S**.

### 1.9 OPEN QUESTIONS
- **Q-E1 — Pen move.** Should a pen use the pointer move or HTML5 DnD? (a) `pointerTypes: ['touch']`, so pen and mouse keep HTML5 DnD as today; (b) `['touch','pen']` with `threshold: 4`, setting `draggable=false` on press. **Recommendation: (a).** Pen already fires HTML5 drag in Chromium on Windows, and (a) keeps desktop behaviour bit-identical. The Scheduler plan text says "touch/pen", so record a decision.
- **Q-E2 — Keyboard model.** (a) An immediate commit per Alt+Arrow step (docs convention, one code path); (b) a Dashboard-style grab mode (Enter grabs, arrows move, Escape cancels). **Recommendation: (a).** It matches `docs/touch-interaction.md` and `attachKeyboardMove`. The plan's "Esc zruší" then refers only to a pointer drag.
- **Q-E3 — Live time label format.** (a) `HH:mm`, identical to the C# card text (`TimeGrid.razor:236`); (b) `Intl.DateTimeFormat` with `document.documentElement.lang`. **Recommendation: (a)** now, because the card text is culture-invariant too. Change both together later.
- **Q-E4 — Live region location.** (a) One `role=status` in `TmScheduler.razor`, with views raising an internal `OnAnnounce` callback; (b) one region per view. **Recommendation: (a).** Views are swapped by `View`, so a per-view region would unmount mid-announcement.

### 1.10 Scope
**Scheduler plan 09c5afa6, Fáze 7** (after F2 and F5). The core follow-up plan takes **E-K** and the **plan-text update** (1.4) only.

---

## 2. [32e4e6c4] + [10bcfc90] Private gestures inventory

Legend: **Pkg** = NuGet package that ships the file. **Plan** = component redesign plan that owns it, and whether its text is correct.

### 2.1 `TmSplitterPane` — eval drag

1. **Status: OPEN.** There is no splitter plan in `plan_list_for_app`.
2. **Current state** (Pkg core, `Components/Layout`):
   - `TmSplitterPane.razor:9-13`: `<div @ref="_resizerRef" class="tm-splitter__resizer" @onpointerdown @onpointermove @onpointerup @onpointercancel>` contains a collapse `<button>` at lines 16-18.
   - `TmSplitterPane.razor.cs:90-128`: `HandleResizerPointerDown` runs `JSRuntime.InvokeVoidAsync("eval", "… window.TempoSplitter = { startDrag(pane, resizer, pointerId) { resizer.setPointerCapture(pointerId); return rect } }")` (lines 101-111), then `TempoSplitter.startDrag`.
   - Lines 130-140: `HandleResizerPointerMove` sets `_dragSize = $"{newSize}px"` and calls `Parent.RequestStateUpdate()` on **every move**, which is a .NET round-trip per move on Server.
   - Lines 142-145: `PointerUp` only clears the flag.
   - `TmSplitter.razor.cs:41-46`: `HandleResizerPointerDown` stores unused start coordinates (dead code).
   - CSS `_splitter.css:31-50`: 8px resizer, `touch-action: none`.
   - Problems:
     - `eval` violates CSP (`unsafe-eval`).
     - The capture is never released by id.
     - There is no Escape, `lostpointercapture` or missed-mouse-up handling.
     - MaxSize is not clamped during the drag; CSS `min/max-width` clamps visually, but `_dragSize` keeps growing.
     - Each move re-renders.
     - The collapse button click lands on the resizer once capture is taken (F0-5), and today it works only because the capture is taken asynchronously after an interop await.
   - Usages: demo `NewComponentsPage.razor:208` and `Tempo.Blazor.EmailTemplates/Components/TmEmailTemplateEditor.razor:34`.
   - Touch today: works partially (pointer events plus `touch-action:none`), but every move goes through .NET.
   - Keyboard today: **none.** The resizer is not focusable and has no role, which fails WCAG 2.1.1.
3. **Migration steps** (copy F0-4):
   - JS: new ES module `src/Tempo.Blazor/wwwroot/js/tm-splitter.js`:
     ```js
     import { attach } from './pointer-drag.js';
     const registry = new Map();
     export function clampSize(requested, min, max) { /* Math.round, then min/max when finite */ }
     export function attachResize(handle, pane, dotnet, id, { orientation, min, max }) {
       detachResize(id);
       const horizontal = orientation === 'horizontal';
       let start = 0, size = 0;
       const drag = attach(handle, {
         threshold: 0,
         dragClass: 'tm-splitter__resizer--dragging',
         suppressClick: false,
         onPress(info) {
           if (info.event?.target?.closest?.('.tm-splitter__collapse-btn')) return false;   // F0-5
           const r = pane.getBoundingClientRect(); start = size = Math.round(horizontal ? r.width : r.height);
         },
         onMove({ dx, dy }) { size = clampSize(start + (horizontal ? dx : dy), min, max); pane.style.flex = `0 0 ${size}px`; handle.setAttribute('aria-valuenow', String(size)); },
         onEnd() { if (size !== start) dotnet?.invokeMethodAsync('OnSplitterResizeCommitted', size)?.catch?.(() => {}); },
         onCancel() { pane.style.flex = ''; handle.setAttribute('aria-valuenow', String(start)); },
       });
       const onKey = (e) => { if (['ArrowLeft','ArrowRight','ArrowUp','ArrowDown','Home','End'].includes(e.key)) e.preventDefault?.(); };
       handle.addEventListener('keydown', onKey);
       registry.set(id, { handle, drag, onKey });
     }
     export function detachResize(id) { /* like tm-editor-shell.js:166-172 */ }
     export function __resetForTests() { … }
     ```
     `min`/`max` are pixels when `MinSize`/`MaxSize` are plain `NNNpx` (reuse `EditorShellResize.TryParsePx`); otherwise pass `0`/`Infinity`. The onCancel path clears the inline style so Blazor's `GetStyle()` takes effect again.
   - C#:
     - New `src/Tempo.Blazor/Helpers/SplitterInterop.cs`, a copy of `EditorShellInterop` reduced to `AttachResizeAsync`/`DetachResizeAsync`, with `ModulePath = "./_content/Tempo.Blazor/js/tm-splitter.js"`.
     - `TmSplitterPane`:
       - Implement `IAsyncDisposable` (keep `Dispose` → `RemovePane`).
       - Inject `IJSRuntime`. In `OnAfterRenderAsync`, attach when a resizer is rendered (`Parent.IsNotLastPane(this)`) and re-attach if the pane becomes or stops being last.
       - Add `[JSInvokable] public Task OnSplitterResizeCommitted(int px)` → `_dragSize = $"{px}px"; Parent.RequestStateUpdate()`.
       - Delete `HandleResizerPointerDown/Move/Up`, the `eval` block, `DomRect`, `_isDragging` and `_dragStartPos`. Delete `TmSplitter.HandleResizerPointerDown` and `_activePane/_dragStartX/_dragStartY` (internal, so not public API).
     - Markup (`TmSplitterPane.razor:9-13`):
       ```razor
       <div @ref="_resizerRef" class="tm-splitter__resizer" role="separator" tabindex="0"
            aria-orientation="@(Parent.Orientation == SplitterOrientation.Horizontal ? "vertical" : "horizontal")"
            aria-valuenow="@CurrentPx" aria-valuemin="@MinPx" aria-valuemax="@MaxPx"
            aria-label="@Loc["TmSplitter_Resize"]"
            @onkeydown="OnResizerKeyAsync">
       ```
       Omit `valuemin`/`valuemax` when they are unknown. The collapse button gets `@onkeydown:stopPropagation="true"` and `aria-label="@Loc[Collapsed ? "TmSplitter_Expand" : "TmSplitter_Collapse"]"`; today it has no accessible name.
     - Keyboard (`OnResizerKeyAsync`):
       - ArrowRight/ArrowLeft for horizontal, ArrowDown/ArrowUp for vertical: ±16 px, with Shift ±64.
       - Home → min, End → max (only when parseable).
       - The start size is measured through `SplitterInterop.MeasureAsync`, or read from `_dragSize`/`Size` px when JS is unavailable.
       - Commit through the same `OnSplitterResizeCommitted(px)`, which is the single commit path.
   - Localization: add `TmSplitter_Resize` ("Resize panes" / "Změnit velikost panelů" / "Redimensionner les panneaux"), `TmSplitter_Collapse`, `TmSplitter_Expand` to the en/cs/fr JSON and to `MockTmLocalizer`.
   - CSS `_splitter.css`:
     - `.tm-splitter__resizer:focus-visible { outline: var(--tm-focus-ring); }` — check the token name used by `_editor-shell.css`.
     - `@media (pointer: coarse) { .tm-splitter__resizer::before { content:''; position:absolute; inset: 0 calc(-1 * var(--tm-space-2)); } }` for a wider hit area.
     - Replace the hardcoded `8px`/`20px`/`2px`/`12px` with tokens where tokens exist.
     - Regenerate `tempo-blazor.bundled.css`.
   - Keyboard alternative per WCAG 2.5.7: arrows/Home/End on the `role="separator"`, plus the collapse button.
4. **RED tests:**
   - Node `src/Tempo.Blazor/wwwroot/js/__tests__/tm-splitter.test.mjs`, modelled on `tm-editor-shell.test.mjs`:
     - a mouse drag sets `pane.style.flex` live and commits once;
     - Escape/pointercancel restore without invoking;
     - a press on `.tm-splitter__collapse-btn` is vetoed, with no capture and the click untouched;
     - clamp min/max;
     - `detachResize` removes every listener;
     - two ids are independent.
   - bUnit `TmSplitterTests`: **rewrite** the four drag tests (lines 127-193) that trigger `onpointerdown`/`onpointermove`, as `OnSplitterResizeCommitted_SetsFlexBasisPx` plus:
     - `Resizer_Has_Role_Separator_Tabindex_AriaLabel`;
     - `ArrowRight_Grows_Horizontal_Pane_By16`;
     - `ShiftArrow_Grows_By64`;
     - `Vertical_Uses_ArrowDown`;
     - `Home_Clamps_To_MinSize_Px`;
     - `Collapse_Button_Has_AccessibleName`;
     - `No_Eval_Interop_Is_Used` (`JSInterop.Invocations` contains no `"eval"`).
   - E2E `tests/Tempo.Blazor.E2E/SplitterE2ETests.cs` (`/new-components`): mouse drag at 1440 changes the pane width and Escape restores it; CDP touch drag at 390 and 768 resizes immediately (threshold 0, no long-press, because `touch-action:none` is on the handle); Tab to the separator then ArrowRight×2 = +32 px; clicking the collapse button collapses.
5. **Verification:** `npm run test:overlay`; `dotnet test --filter TmSplitterTests`; the E2E above; a grep that `eval` no longer appears in `src/Tempo.Blazor/Components/Layout`; `TmEmailTemplateEditor` still renders (its bUnit tests).
6. **Breaking? No public parameter changes.** CHANGELOG:
   - `### Accessibility`: TmSplitter separator is keyboard-operable.
   - `### Fixed`: no `eval` (CSP) and no per-move re-render.
   - `### Breaking / Migration` (short): a strict CSP must allow `tm-splitter.js` and `pointer-drag.js`, as for `scheduler.js` at CHANGELOG:516-523.
7. **Effort: M.**
8. **OPEN:**
   - **Q-S1 — Live size.** (a) JS writes the inline flex, and .NET commits only at the end; (b) .NET re-renders on every move as today. **Rec (a).**
   - **Q-S2 — Should `SizeChanged` be added?** **Rec: no** in this plan; it is a new public API.
9. **Scope: core follow-up plan** (phase E-1).

### 2.2 `slider.js` / `TmRangeSlider`

1. **Status: OPEN.**
2. **Current state** (Pkg core, `Components/Inputs`). There are **two diverging copies**:
   - `src/Tempo.Blazor/wwwroot/js/slider.js:1-24` (`window.TempoSlider.initRangeSlider`) only swaps the `z-index` on `pointerdown`, and **no host page loads it**: grep of `index.html`/`_Host.cshtml`/`App.razor` finds no `slider.js`.
   - `TmRangeSlider.razor.cs:55-145`: `OnAfterRenderAsync` runs `eval` defining `window.TempoSlider` (lines 61-136), but only `if (!window.TempoSlider)`. If a host loads `slider.js`, the eval copy is skipped and the drag silently degrades.
   - The eval drag:
     - `container.addEventListener('pointerdown')` plus **window** `pointermove/pointerup/pointercancel` listeners that are **never removed**, so they leak per instance; the component has no Dispose.
     - No capture, no Escape cancel; `preventDefault` on `pointerdown`.
     - On touch the container has no `touch-action`, so the browser pans and sends `pointercancel`, and the drag dies.
   - `TmSlider` (single) is pure native `<input type=range>`, so nothing to migrate.
   - Keyboard today: both `<input type=range>` (`TmRangeSlider.razor:12-29`) are natively keyboard-operable, but they have **no accessible name**: the `<label>` at line 6 is not associated, and the inputs carry no `aria-label`.
   - CSS `_range-slider.css:37-108`: overlaid inputs with `z-index` 2/3.
3. **Migration steps** — two options; see Q-R1.
   - **(A) Recommended: remove the private gesture.** Use the standard dual-range CSS so that each native thumb drags itself, giving native touch, keyboard, pen, capture and cancel.
     - Delete the `eval` block (`TmRangeSlider.razor.cs:55-145`), `_jsInitialized`, `JSRuntime` and `_containerRef` if unused.
     - CSS `_range-slider.css`: `.tm-range-slider__input { pointer-events: none; }`, then `.tm-range-slider__input::-webkit-slider-thumb { pointer-events: auto; }` and `.tm-range-slider__input::-moz-range-thumb { pointer-events: auto; }`.
     - Keep z-index 2/3. When start == end, the end thumb sits on top; add `.tm-range-slider__input--start` z-index 4 when `_currentStartValue >= Max - Step` (a C# class modifier `tm-range-slider--start-on-top`), so a thumb at the max can still be pulled left.
     - Coarse thumbs: `@media (pointer: coarse) { thumb width/height: var(--tm-touch-target-min, 1.75rem) }` (check the token name).
     - `slider.js`: keep the file as a deprecated no-op (`window.TempoSlider = window.TempoSlider || { initRangeSlider() {} };`) for one release, because it is a shipped static asset. Note the removal in CHANGELOG.
   - **(B)** Migrate the drag to `pointer-drag.js`:
     - new ES module `tm-range-slider.js`: `attach(trackContainer, { threshold: 0, touchAction: 'pan-y' })`;
     - `onPress` picks the closer input and sets its value;
     - `onMove` sets the value and dispatches `input`;
     - `onEnd` dispatches `change`;
     - `onCancel` restores both start values and dispatches `input`;
     - an interop class with `DisposeAsync`.
     - This is more code and keeps track-click-to-jump.
   - Both options:
     - Add `aria-label="@Loc["TmRangeSlider_Start"]"`/`"TmRangeSlider_End"` (en "Minimum"/"Maximum", cs "Od"/"Do", fr "Minimum"/"Maximum") and `aria-valuetext` on both inputs.
     - Associate the label: `id` on the `<label>`, and `aria-labelledby="labelId startLabelId"`.
4. **RED tests:**
   - bUnit `TmRangeSliderTests`:
     - `Inputs_Have_Accessible_Names`;
     - `No_Eval_Interop` (`JSInterop.Invocations.Should().NotContain(i => i.Identifier == "eval")`);
     - `StartOnTop_Class_When_Start_At_Max`.
   - Static CSS test (pattern from existing CSS tests in `tests/Tempo.Blazor.Tests/Theme/`): `_range-slider.css` contains `pointer-events: none` on `.tm-range-slider__input` and `pointer-events: auto` on both thumb pseudo-elements.
   - E2E `RangeSliderE2ETests` (`/new-components`): mouse-drag the end thumb at 1440 and the value text changes; CDP touch drag of the start thumb at 390 and 768 changes the start value and the page still scrolls when swiping vertically on the track; Tab to the start thumb, ArrowRight raises it by Step; the start cannot pass the end.
5. **Verification:** the bUnit + E2E above; manual check in Firefox (moz thumb).
6. **Breaking:** visual/behaviour change only (option A loses "click on the empty track jumps the nearest thumb"). CHANGELOG `### Changed` + `### Accessibility`; `### Deprecated`: `slider.js` (no-op, removed in the next minor).
7. **Effort: S** (A) / **M** (B).
8. **OPEN — Q-R1:** (A) CSS dual-thumb, no JS gesture; (B) pointer-drag adopter; (C) leave it. **Rec (A).** It deletes a private gesture instead of re-implementing one, and fixes the listener leak and the touch failure. That fits the rule "never write a second drag implementation".
9. **Scope: core follow-up plan** (phase E-2).

### 2.3 `TmDataTable` column resize

1. **Status: OPEN.** The DataTable plan 6288f7e0 has **no column-resize text** (sub-agent full-text search). Its F2 covers only a non-drag grouping alternative.
2. **Current state** (Pkg core):
   - Markup `TmDataTable.razor:273-279`:
     ```razor
     <span class="tm-col-resize-handle" data-testid="@TestId($"resize-{col.Key}")"
           @onpointerdown:stopPropagation="true" @onpointerdown="e => StartColumnResizeAsync(col, e)"></span>
     ```
     The handle sits inside a `<th tabindex="0">` whose keydown sorts (`HandleHeaderKeyDownAsync`, `.razor.cs:2206`).
   - `TmDataTable.razor.cs:1108-1117` `StartColumnResizeAsync` → `tmDataTable.startColumnResize(dotNetRef, col.Key, e.ClientX, startWidth = GetPixelWidth(col) ?? DefaultPinnedWidth /*160*/, MinColumnWidth /*60*/)`.
     - The start width is **not measured**, so a column without a px width jumps to 160 on the first move.
     - The JS listeners attach only after a .NET round-trip, so the first moves are lost on Server.
   - `[JSInvokable] OnColumnResized(string, double)` (lines 1120-1126) and `OnColumnResizeCommitted()` → `PersistLayoutAsync` (1129-1130). Public API: `SetColumnWidthAsync(string columnKey, int width)` (1100-1106).
   - `data-table.js:9-49` (classic `window.tmDataTable`, host `<script>` at index.html:164): document `pointermove/pointerup/pointercancel`, no capture, **`pointercancel` commits** (same as up), no Escape, `document.body.style.userSelect/cursor`.
   - CSS `_data-table.css:288-297`: 6px handle, `touch-action:none`.
   - Touch today: works but the 6px target is tiny and pointercancel commits.
   - Keyboard today: **none** (WCAG 2.5.7/2.1.1).
   - Tap on the handle: there is no `@onclick:stopPropagation`, so a tap may bubble a `click` to the `<th>` and sort; verify in E2E.
3. **Migration steps:**
   - JS `data-table.js`:
     - Add at the top (synchronously) `var helperUrl = new URL('pointer-drag.js', document.currentScript.src).href` with the same try/fallback as `scheduler.js:16-24`.
     - Add `attachColumnResize(root, dotNetRef)` and `detachColumnResize(root)` (a `WeakMap` registry keyed by root). It awaits `import(helperUrl)` and calls:
       ```js
       attach(root, {
         selector: '.tm-col-resize-handle',
         threshold: 0,
         onPress(info) {
           const th = info.target.closest('th');
           s = { key: info.target.dataset.colKey, start: Math.round(th.getBoundingClientRect().width), min: +info.target.dataset.minWidth || 60, last: … };
         },
         onMove({ dx }) { /* width = max(min, round(start + dx)); rAF-throttled invoke 'OnColumnResized'(key, width) — same as today lines 15-25 */ },
         onEnd() { /* flush OnColumnResized(key, last) then OnColumnResizeCommitted — same as today lines 39-41 */ },
         onCancel() { /* cancel rAF; invoke OnColumnResized(key, start); NO commit */ },
       })
       ```
       `touch-action:none` stays on the handle in CSS (delegated drag, so the helper leaves the root alone). There is no more `body.style` mutation; `.tm-pointer-dragging` handles `user-select`.
     - Keep `startColumnResize` exported but unused, marked `/** @deprecated */`, for hosts that call it.
   - C#:
     - `OnAfterRenderAsync(firstRender)`: `JS.InvokeVoidAsync("tmDataTable.attachColumnResize", _tableRef, _dotNetRef)` with the same catch as the export call (line 299). Detach in `Dispose`, which becomes `DisposeAsync`, or fire-and-forget in `Dispose`. Render `@ref="_tableRef"` on `<table class="tm-data-table" role="grid">` (razor:152).
     - Handle markup:
       ```razor
       <span class="tm-col-resize-handle" data-testid="…" data-col-key="@col.Key" data-min-width="@MinColumnWidth"
             role="separator" aria-orientation="vertical" tabindex="0"
             aria-valuenow="@(GetPixelWidth(col) ?? DefaultPinnedWidth)" aria-valuemin="@MinColumnWidth"
             aria-label="@Loc["TmDataTable_ResizeColumn", HeaderText(col)]"
             @onclick:stopPropagation="true"
             @onkeydown="e => OnResizeHandleKeyAsync(col, e)" @onkeydown:stopPropagation="true"></span>
       ```
       Remove `@onpointerdown`, and keep `StartColumnResizeAsync` only if a test needs it; otherwise delete it (private). `HeaderText(col)` is the existing `SortButtonHeaderText(col)` (`.razor.cs:2356`). `@onkeydown:stopPropagation` fences the th's sort key per `docs/keyboard-activation-convention.md`.
     - Keyboard `OnResizeHandleKeyAsync`: ArrowLeft/ArrowRight → `SetColumnWidthAsync(col.Key, current ∓/± (Shift ? 32 : 8))`; Home → `MinColumnWidth`. ArrowLeft/Right on a focused separator would otherwise scroll the table horizontally; prevent that in JS with a `keydown` listener on `root` filtered by `.tm-col-resize-handle`, like `tm-editor-shell.js:157-160`, not with Blazor `:preventDefault`. `SetColumnWidthAsync` persists, which is the single commit path.
   - CSS:
     - `@media (pointer: coarse) { .tm-col-resize-handle { width: var(--tm-space-4); right: calc(-1 * var(--tm-space-2)); } }`;
     - `.tm-col-resize-handle:focus-visible { outline: … focus ring token }`;
     - replace `6px` with a token;
     - regenerate the bundle.
   - Localization: `TmDataTable_ResizeColumn` ("Resize column {0}" / "Změnit šířku sloupce {0}" / "Redimensionner la colonne {0}") in en/cs/fr and the mock.
4. **RED tests:**
   - Node `src/Tempo.Blazor/wwwroot/js/__tests__/data-table-resize.test.mjs` (boot pattern of `scheduler-pointer.test.mjs:91-100`, `currentScript.src` stub):
     - drag on `.tm-col-resize-handle` measures the th, invokes `OnColumnResized` live and then `OnColumnResizeCommitted` once;
     - **pointercancel does NOT commit** and restores the start width;
     - Escape restores;
     - a min clamp;
     - a press outside a handle does nothing;
     - detach removes listeners;
     - two tables are independent.
   - bUnit `TmDataTablePinResizeTests`:
     - `ResizeHandle_Is_Focusable_Separator_With_Name`;
     - `ResizeHandle_ArrowRight_Grows_By8_And_Persists`;
     - `ResizeHandle_ShiftArrowLeft_Shrinks_By32_Clamped_To_Min`;
     - `ResizeHandle_Keydown_Does_Not_Sort`;
     - `AttachColumnResize_Invoked_On_First_Render`.
     - Keep `OnColumnResized_JsCallback_AppliesWidth` (line 157).
   - E2E `DataTableColumnResizeE2ETests` (`/data-table`, `Resizable="true"` columns at `DataTablePage.razor:55-57`):
     - mouse drag at 1440 +50 px makes the th width grow ≈50 and the layout persist;
     - Escape mid-drag restores;
     - CDP touch at 390/768 resizes (handle width ≥ 16 on coarse);
     - tap on the handle does not change the sort (`aria-sort` unchanged);
     - keyboard Tab to the handle then ArrowRight×3 = +24.
5. **Verification:** `npm run test:overlay`; `dotnet test --filter "TmDataTable"`; E2E; JSON docs unaffected (no parameter change).
6. **Breaking: no.** `startColumnResize` is kept (deprecated). CHANGELOG `### Fixed` (cancel no longer commits; start width measured), `### Accessibility` (keyboard column resize), `### Breaking / Migration` note: `data-table.js` now imports `pointer-drag.js` (CSP), the same wording as the scheduler entry.
7. **Effort: M.**
8. **OPEN — Q-D1:** (a) core follow-up plan; (b) add to DataTable plan F2 ("A11y a klávesnice ovládacích prvků"). **Rec (a)**, because it is self-contained and the DataTable plan's F4+ have external-pipeline gates. Then add one line to DataTable plan F2: „Resize sloupců už běží na `pointer-drag.js` s klávesnicovým separátorem (follow-up plán jádra E-3) — neměnit, jen zachovat v redesignu (`.tm-col-resize-handle`, `data-col-key`).“
9. **Scope: core follow-up plan** (phase E-3), with a note in the DataTable plan.

### 2.4 `dashboard.js`

1. **Status: OPEN; owned by a plan.**
2. **Current state** (Pkg core):
   - `src/Tempo.Blazor/wwwroot/js/dashboard.js` (338 lines, classic `window.tempoDashboard`):
     - HTML5 DnD listeners at **lines 54-57** (`dragstart/dragover/drop/dragend`); handlers 83-163; snapping 124-126/143-145.
     - Resize with **mouse** at **lines 60-62** (`grid mousedown`, `document mousemove/mouseup`); handlers 165-280.
     - Directions 282-292; min 120×80 px hardcoded at lines 244-245; `destroy` 318-337.
   - Blazor: `TmDashboard.razor:191` `draggable`, `:196` `span.tm-widget-drag-handle`, `:236-250` eight `.tm-widget-resize-*` handles, `:484` `tempoDashboard.init`.
   - `[JSInvokable] OnGridPositionChanged(instanceId, x, y)` at `:912` and `OnWidgetResized(instanceId, w, h)` at `:931`. `CanDrag` = desktop only (`:742`).
   - Touch today: none (HTML5 DnD + mouse). Keyboard today: none.
3. **Migration:** owned by **Dashboard plan d254e3dd**:
   - **Fáze 4** "Přeuspořádání bez dragu": C# grab mode (Enter/Space grab, arrows move, Shift+arrows resize, Escape cancel), `aria-live`, layout logic in C#.
   - **Fáze 5** "Dotykový drag a resize v dashboard.js přes pointer-drag helper": replace DnD and mouse with `pointer-drag.js`, `touch-action:none` only on handles, explicit "žádný vlastní helper".
   - The plan text is correct apart from stale line numbers and a missing import rule. Proposed edit to F5's `dashboard.js` bullet (Czech):
     ```
     - [ ] `dashboard.js`: nahradit HTML5 DnD (dnes ř. 54–57) a mouse události (ř. 60–62) za jádrový
       `pointer-drag.js`. `dashboard.js` je klasický skript: helper načíst `import()` z URL odvozené od
       `document.currentScript.src` zachycené synchronně na začátku souboru (vzor `scheduler.js` ř. 16–24,
       docs/touch-interaction.md „Consuming from a split package“) — nikdy bare `'_content/…'`. Jeden
       `attach(grid, { selector: '.tm-widget-drag-handle', threshold: 4 })` a jeden
       `attach(grid, { selector: '.tm-widget-resize-handle', threshold: 0 })`; `touch-action: none` jen na
       úchytech v CSS; Escape/pointercancel = `onCancel` vrátí widget (žádný .NET call); commit jen v `onEnd`
       přes stávající `OnGridPositionChanged` / `OnWidgetResized`. Klávesnice zůstává grab mode z Fáze 4
       (ne `attachKeyboardMove` — šipky v grab mode nejsou Alt-akord).
     ```
4. **RED tests:** listed in the plan (F4 bUnit, F5 E2E). Add a node test `__tests__/dashboard-pointer.test.mjs` (boot like `scheduler-pointer.test.mjs`). There is an existing bUnit `TmDashboardTests.cs:433`.
5. **Verification:** per plan.
6. **Breaking:** the CSP note for `dashboard.js` → `pointer-drag.js` (plan F5 CHANGELOG).
7. **Effort: L** (F4 M + F5 M).
8. **OPEN:** none new. Auto-scroll for a long grid is a good first consumer of section 4's `createAutoScroller`; add it to F5 as optional.
9. **Scope: Dashboard plan F4 + F5.** Not core.

### 2.5 `workflow-designer.js` / `TmWorkflowDesignerCanvas`

1. **Status: OPEN**, with no plan: there is no workflow plan in `plan_list_for_app`.
2. **Current state** (Pkg core, `Components/Workflow`):
   - `src/Tempo.Blazor/wwwroot/js/workflow-designer.js` (350 lines, classic `window.tmWorkflowDesigner`):
     - `_attachEvents` 51-63: svg `mousedown`, **document** `mousemove/mouseup`, `wheel` (non-passive), `contextmenu`.
     - `_onMouseDown` 96-139: three modes — port `[data-port]` → transition creation; node `[data-state-id]` → node drag; else → pan.
     - `_onMouseMove` 143-176; `_onMouseUp` 180-227. Calls `OnNodeDragged(stateId, x, y)`, `OnViewBoxChanged`, `OnTransitionCreated(from, to)`.
     - Wheel zoom 231-253 has a hardcoded `origW = 800`. Temp line colour `#3b82f6` (282).
   - C#: `TmWorkflowDesignerCanvas.razor:141-166` init/destroy; `[JSInvokable]` at 170-239 (`OnNodeDragged` snaps to `GridSize`).
   - Markup: `<svg role="img">` (lines 10-16) and state `<g data-state-id … style="cursor:pointer" onclick>` (485-492) — **not focusable**. Port `<circle data-port>` (561-571).
   - The context menu has **hardcoded English** labels "Edit properties", "Delete state", "Add state", … (lines 50-88), a separate localization defect, and opens only by right-click.
   - Touch today: **none.** Mouse listeners only; no `touch-action` on the svg, so a finger scrolls the page.
   - Keyboard today: **none** for move or connect. The toolbox adds and deletes states (`TmWorkflowToolbox.razor`); the properties panel edits name/type/colour but not position or transitions.
   - Tests: `TmWorkflowDesignerCanvasTests.cs` (44 tests; `OnNodeDragged_*` at 275-330, `OnTransitionCreated_*` at 397-437).
3. **Migration steps:**
   - JS:
     - Capture `helperUrl` at the top (as `scheduler.js:16-24`).
     - `init` → `inst.ready = import(helperUrl).then(h => { … })`, honouring a generation counter so a destroy during the import wins.
     - Create three `h.attach(svg, …)` sessions:
       - (1) `{ selector: '[data-port]', threshold: 2, onPress: veto if readOnly, onStart: _createTempLine, onMove: update x2/y2, onEnd: target = document.elementFromPoint(x, y)?.closest('[data-state-id]') → OnTransitionCreated, onRelease: _removeTempLine }`;
       - (2) `{ selector: '[data-state-id]', threshold: 4, onPress: veto if readOnly or target.closest('[data-port]'), onMove: setAttribute('transform', translate(start + dx/scale)), onEnd: OnNodeDragged, onCancel: restore the start transform }`;
       - (3) `{ threshold: 4, onPress: veto if target.closest('[data-state-id],[data-port]'), onMove: pan viewBox, onEnd: OnViewBoxChanged, onCancel: restore viewBox }`. Pan on the bare svg: `touchAction` defaults to `'none'` because there is no selector and no longPress. A canvas editor accepts that the page does not scroll over the canvas, as in DiagramEditor.
     - Delete the mouse listeners and `_onMouseDown/Move/Up`.
     - Keep `wheel`, `contextmenu`, `zoomTo` and `fitToView`.
     - Convert `svg → doc` deltas with the existing `_svgPoint` (75-83).
     - Note F0-6: state `<g>` uses `onclick`, which is unaffected because `click` still fires after a tap (threshold not reached), and the helper swallows the click after a real drag, so a drag no longer also selects. That is intended; document it.
   - Keyboard (WCAG 2.1.1 + 2.5.7):
     - States: `<g … tabindex="0" role="button" aria-label="@Loc["TmWorkflowDesignerCanvas_StateLabel", state.Name, typeName]">` with `@onkeydown` Enter/Space → `SelectState` (per the keyboard-activation convention: emulate on a non-native focusable).
     - Move: `attachKeyboardMove(svg, { selector: '[data-state-id]', onStep })` → read the current translate, add `step.x/y * GridSize` (GridSize passed in `init` options) and invoke the existing `OnNodeDragged`, the single commit path.
     - Announce through `<div class="tm-sr-only" role="status" aria-live="polite">@_announcement</div>` set in `OnNodeDragged` when called from the keyboard: add an `[JSInvokable] OnNodeKeyboardMoved` wrapper that calls `OnNodeDragged` and sets `TmWorkflowDesignerCanvas_StateMoved` ("{0} moved to {1}, {2}").
     - The svg `role="img"` must become `role="application"` or `role="group"` with `aria-roledescription`; `img` hides focusable children from assistive technology. **Rec `role="group"`.**
   - Non-drag transition creation (single-pointer alternative): add a state context-menu entry "Connect to…" and a properties-panel `<select>` "Add transition to" listing the other states → `OnTransitionCreated(from, to)`. Localize the whole context menu: new keys `TmWorkflowDesignerCanvas_EditProperties`, `_DeleteState`, `_DeleteTransition`, `_AddInitialState`, `_AddState`, `_AddFinalState`, `_ConnectTo`, `_StateLabel`, `_StateMoved`. The context menu must also open from the keyboard (Shift+F10 / ContextMenu key on a focused state) via the existing `OnContextMenu` path.
   - CSS (`_workflow*.css`):
     - `@media (pointer: coarse)`: port radius ≥ 12 (visual `r` via an attribute in C#, or a transparent hit `<circle r="14">`);
     - `.tm-wf-state:focus-visible { outline: focus ring }`;
     - replace the hex `#3b82f6` in JS line 282 with `getComputedStyle(svg).getPropertyValue('--tm-color-primary')` or a class with a CSS stroke token.
4. **RED tests:**
   - Node `src/Tempo.Blazor/wwwroot/js/__tests__/workflow-designer-pointer.test.mjs`:
     - mouse/touch drag on a state commits one `OnNodeDragged` with svg-scaled coordinates;
     - Escape restores the transform without invoking;
     - port drag onto another state invokes `OnTransitionCreated` once, and onto empty canvas invokes nothing while the temp line is removed on every path;
     - empty-canvas drag pans and commits `OnViewBoxChanged` once;
     - readOnly vetoes node/port;
     - `attachKeyboardMove` Alt+ArrowRight → `OnNodeKeyboardMoved` +GridSize;
     - destroy detaches all listeners (count 0);
     - no `mousedown` listener remains.
   - bUnit:
     - `State_Is_Focusable_Button_With_Name`;
     - `State_Enter_Selects_Once`;
     - `OnNodeKeyboardMoved_Snaps_And_Announces`;
     - `ContextMenu_Is_Localized`;
     - `ContextMenu_ConnectTo_Creates_Transition`;
     - `PropertiesPanel_AddTransition_Select`;
     - `Svg_Role_Is_Not_Img`.
   - E2E `WorkflowDesignerE2ETests` (`/workflow-designer`):
     - 1440 mouse node drag moves it (transform changes, snapped);
     - Escape restores;
     - port→state drag creates a transition path;
     - CDP touch at 768: a node drag works and a one-finger empty-canvas pan changes the viewBox;
     - at 390 the same;
     - keyboard Tab to a state then Alt+ArrowRight moves one grid step, the `role=status` text updates and `page.Url` is unchanged (no Back navigation).
5. **Verification:** `npm run test:overlay`; `dotnet test --filter TmWorkflow`; E2E; axe on `/workflow-designer` with 0 serious.
6. **Breaking:**
   - No public parameter changes.
   - The svg role changes `img` → `group` (DOM).
   - A click on a state after a real drag no longer selects it.
   - The `workflow-designer.js` → `pointer-drag.js` import is a CSP note.
   - CHANGELOG: `Breaking / Migration` (role + CSP), `Added` (touch, keyboard move, Connect to), `Fixed` (localized context menu).
7. **Effort: L.** Split it: E-4a pointer+touch (M), E-4b keyboard/a11y + Connect-to + localization (M).
8. **OPEN:**
   - **Q-W1 — Should the workflow canvas be migrated in the core plan at all?** It has no redesign plan and is listed in AGENTS as a core component. (a) Yes, in the core follow-up plan; (b) create a "Redesign: Workflow designer" plan; (c) defer. **Rec (a)**, and make it the pilot consumer of `pointer-gesture.js` pinch (section 4).
   - **Q-W2 — Node drag on touch.** (a) Immediate (threshold 4, `touch-action:none` on the canvas); (b) long-press 400 ms so the page scrolls over the canvas. **Rec (a)**, consistent with DiagramEditor/Wireframe canvases.
9. **Scope: core follow-up plan** (E-4a/E-4b).

### 2.6 `tm-sheet.js` gesture

1. **Status: OPEN** (listed in the docs table; the foundation F2 wrote it before the helper existed).
2. **Current state** (Pkg core):
   - `src/Tempo.Blazor/wwwroot/js/tm-sheet.js:155-302` `attachGesture(handle, panel, snaps, swipeToDismiss, host, id, options)`:
     - own `pointerdown/move/up/cancel` on the handle (289-292);
     - capture by id on press for the grabber, or after the 6 px arm for the header (200-204, 217-233);
     - velocity sampling over 80 ms (168-176, 254-258);
     - `settle()` (30-67, exported and tested).
   - Called by `SheetInterop.cs:39-77` (`attachGesture`) from `TmDrawer`, `TmModal` and `TmDialog`.
   - Grabber: `TmSheetHandle.razor` (`role="slider"`, arrows/Home/End when not decorative).
   - CSS `_sheet.css:45` and `_drawer.css:157`: `touch-action:none`.
   - Missing vs the helper:
     - no `isPrimary` guard: a second finger's `pointerdown` resets `startY`/`pointerId` (206-215), hijacking the drag;
     - no `lostpointercapture`;
     - no missed-mouse-up check;
     - Escape during a drag goes to the dialog, which closes it;
     - no `superseded` handling.
   - Touch today: **yes** (it was built for touch). Keyboard today: TmSheetHandle slider plus Escape/Done.
   - Tests: `__tests__/tm-sheet.test.mjs` (32 KB, passes).
3. **Migration steps** — keep `settle`, `keyboardOffset`, `trackViewport`, `detach`, the deprecated `attach` exports and the `attachGesture` signature:
   - Add `import { attach as attachPointerDrag } from './pointer-drag.js';` at the top of `tm-sheet.js` (it is a module).
   - In `attachGesture`, replace lines 162-293 with one `attachPointerDrag(handle, {...})`:
     - `threshold: 6`;
     - `captureOn: handle.classList?.contains('tm-sheet__handle') ? 'press' : 'start'` (header keeps the close button clickable, F0-5);
     - `touchAction: null` (CSS owns it);
     - `suppressClick: true`;
     - `dragClass: null`;
     - `onPress({ y, event })`: measure startHeight/startFraction and reset samples with `event.timeStamp`;
     - `onStart`: `panel.style.transition = 'none'`;
     - `onMove({ y, dy, event })`: `remember(y, event.timeStamp)`; `panel.style.height = max(0, startHeight - dy) + 'px'`;
     - `onEnd({ event, y })`: the existing settle/dismiss logic (251-284), with `event.clientY` replaced by `y`;
     - `onCancel`: `resetInline()`, nothing reported;
     - `onRelease({ outcome })`: when `outcome === 'tap'` do nothing (today's "never armed" path).
   - Store `drag` in `gestures` and call `drag.detach()` in `detach(id)`.
   - Behaviour change: Escape mid-drag now cancels the drag (snap restored) and does **not** close the sheet; a second Escape closes it. Document it in `docs/bottom-sheet.md`.
4. **RED tests** (`tm-sheet.test.mjs` additions):
   - `a second finger cannot hijack an active sheet drag`;
   - `Escape during a sheet drag restores the snap and does not propagate`;
   - `lostpointercapture cancels without HandleSheetSnappedAsync`;
   - `a header tap on the close button is not captured (captureOn start)`.
   - Run the whole existing file unchanged. The stubs may need `isPrimary: true`/`button: 0` defaults and a `document` stub, because the helper reads `globalThis.document` guarded.
   - E2E: re-run `tests/Tempo.Blazor.E2E/Feedback/BottomSheetE2ETests.cs` and `SheetRound*E2ETests.cs`; add `Sheet_390_SecondFingerDoesNotHijackDrag` (CDP touch with two `touchPoints`) and `Sheet_390_EscapeMidDrag_KeepsSheetOpen`.
5. **Verification:** `npm run test:overlay`; Feedback E2E lane at 390/768/1024 touch.
6. **Breaking: no** API change. CHANGELOG `### Changed`: Escape during a sheet drag cancels the drag first.
7. **Effort: M** (risk medium: velocity/flick feel; keep the 80 ms window).
8. **OPEN — Q-T1:** (a) migrate now in the core follow-up plan; (b) leave it, since it works and is tested, and drop it from the docs list as an accepted exception; (c) fix only the `isPrimary` guard (S). **Rec (a)** as the last core phase. If plan capacity is tight, do **(c)** and record the exception in `docs/touch-interaction.md`.
9. **Scope: core follow-up plan** (E-6).

### 2.7 `TmColorGradient`

1. **Status: OPEN.** There is no colour-picker plan. Plan 684fe86d "TmSelect … TmColorPalette přístupné názvy barev" touches the palette, not the gradient.
2. **Current state** (Pkg core, `Components/Inputs`):
   - `TmColorGradient.razor:7-47`: three `role="slider"` divs (area / hue / alpha) with Blazor `@onpointerdown/@onpointermove/@onpointerup`. They have **no `tabindex`** and no keydown, so they are not keyboard-operable (WCAG 2.1.1). Hue/alpha have valuemin/max/now; the area has only `aria-valuetext`.
   - `TmColorGradient.razor.cs:124-212`:
     - **no pointer capture**, so leaving the element ends the tracking silently and the drag never "ends";
     - position from `e.OffsetX/OffsetY` relative to the event target, so over the thumb child (`.tm-color-gradient-thumb`) the offset jumps;
     - **hardcoded 200×150** (lines 149-150, 180, 210);
     - each move round-trips to .NET;
     - `ValueChanged` only on pointerup (good).
   - CSS `_color-gradient.css:20,53`: `touch-action:none`.
   - The RGBA `<input type=number>` (51-71) exist, which satisfies WCAG 2.5.7 (a non-drag alternative exists) but not a keyboard slider.
   - Usages: `TmFlatColorPicker.razor:18`, demo `/color-picker`, `/new-components`.
3. **Migration steps:**
   - JS new ES module `src/Tempo.Blazor/wwwroot/js/tm-color-gradient.js`:
     ```js
     import { attach } from './pointer-drag.js';
     const registry = new Map();
     export function attachGradient(root, dotnet, id) {
       detachGradient(id);
       const parts = [['area', '.tm-color-gradient-area'], ['hue', '.tm-color-gradient-hue-track'], ['alpha', '.tm-color-gradient-alpha-track']];
       const drags = [];
       for (const [kind, sel] of parts) {
         const el = root.querySelector(sel); if (!el) continue;
         let frame = 0, last = null;
         const frac = ({ x, y }) => { const r = el.getBoundingClientRect(); return { fx: clamp01((x - r.left) / r.width), fy: clamp01((y - r.top) / r.height) }; };
         const send = (m, p) => dotnet?.invokeMethodAsync(m, kind, p.fx, p.fy)?.catch?.(() => {});
         drags.push(attach(el, {
           threshold: 0, touchAction: null, suppressClick: false,
           onPress(i) { last = frac(i); send('OnGradientDrag', last); },
           onMove(i) { last = frac(i); if (!frame) frame = requestAnimationFrame(() => { frame = 0; send('OnGradientDrag', last); }); },
           onEnd(i) { cancelAnimationFrame(frame); frame = 0; send('OnGradientCommit', frac(i)); },
           onCancel() { cancelAnimationFrame(frame); frame = 0; dotnet?.invokeMethodAsync('OnGradientCancel')?.catch?.(() => {}); },
           onRelease({ outcome }) { if (outcome === 'tap') send('OnGradientCommit', last); },
         }));
       }
       registry.set(id, drags);
     }
     export function detachGradient(id) { (registry.get(id) ?? []).forEach(d => d.detach()); registry.delete(id); }
     ```
     A tap sets the colour (outcome `tap`), and so does a drag.
   - C#:
     - New `ColorGradientInterop` (EditorShellInterop pattern). `TmColorGradient` implements `IAsyncDisposable`, attaches in `OnAfterRenderAsync(firstRender)` (re-attach when `ShowAlpha` toggles) and gets `@ref` on the root.
     - `[JSInvokable] OnGradientDrag(string kind, double fx, double fy)`: remember the start state once per gesture, update HSV/alpha, `InvokeAsync(StateHasChanged)`, no `ValueChanged`.
     - `OnGradientCommit(kind, fx, fy)`: update, then `NotifyChangedAsync()`.
     - `OnGradientCancel()`: restore the start HSV/alpha.
     - Delete the six pointer handlers and the 200/150 constants (fractions replace them).
   - Keyboard (markup):
     - Add `tabindex="0"` and `@onkeydown` to all three sliders.
     - Area: ArrowLeft/Right saturation ±1 % (Shift ±10), ArrowUp/Down value ±1 % (Shift ±10), Home/End saturation 0/100; `aria-valuetext="@Loc["TmColorGradient_AreaValue", sat%, val%]"`.
     - Hue: ←/→ ±1° (Shift ±10), Home 0 / End 360.
     - Alpha: ±1 % (Shift ±10), Home/End.
     - Each key commits through `NotifyChangedAsync`, the single commit path.
     - The page must not scroll on arrows: add a JS keydown `preventDefault` for arrows/Home/End on these three elements, registered in `attachGradient` (as in `tm-editor-shell.js:157-160`).
   - CSS: `:focus-visible` ring on the area/tracks; coarse thumb size via a token.
   - Localization: `TmColorGradient_AreaValue` ("Saturation {0}%, brightness {1}%" / "Sytost {0} %, jas {1} %" / "Saturation {0} %, luminosité {1} %").
4. **RED tests:**
   - Node `__tests__/tm-color-gradient.test.mjs`:
     - fractions come from the bounding rect, not the offset (press on a thumb child at the rect centre gives fx 0.5);
     - drag invokes `OnGradientDrag` live and `OnGradientCommit` once;
     - Escape gives `OnGradientCancel` and no commit;
     - a tap commits once;
     - detach.
   - bUnit `TmColorGradientTests`:
     - **rewrite** `TmColorGradient_Format_Applies_To_Value` (lines 63-78, which use `hueTrack.PointerDown(OffsetX=0)`) to call `cut.InvokeAsync(() => cut.Instance.OnGradientCommit("hue", 0, 0))`;
     - add `Sliders_Are_Focusable`, `Hue_ArrowRight_Adds1_ShiftAdds10_And_Fires_ValueChanged`, `Area_ArrowUp_Raises_Value`, `Alpha_Home_Sets_Zero`, `OnGradientDrag_Does_Not_Fire_ValueChanged`, `OnGradientCancel_Restores_Start`.
   - E2E `ColorGradientE2ETests` (`/color-picker`):
     - 1440 mouse drag across the area, leaving the element mid-drag (capture keeps tracking), changes the value once;
     - CDP touch at 390/768 changes the hue;
     - keyboard Tab to hue then ArrowRight×10 changes the preview.
5. **Verification:** node + bUnit + E2E; axe on `/color-picker`.
6. **Breaking: no.** CHANGELOG `### Fixed` (thumb offset jump, fixed 200×150 maths, capture), `### Accessibility` (keyboard sliders), CSP note (`tm-color-gradient.js` → `pointer-drag.js`).
7. **Effort: M.**
8. **OPEN — Q-C1 — Live preview path.** (a) rAF-throttled `OnGradientDrag` .NET calls; (b) JS moves the thumbs directly and .NET is told only at the end. **Rec (a).** The area background and preview depend on C#-computed colours; (b) would duplicate HSV maths in JS.
9. **Scope: core follow-up plan** (E-5).

### 2.8 `TmGantt` `@onmouse*`

1. **Status: OPEN; owned by a plan.**
2. **Current state** (Pkg core, `Components/Scheduler`):
   - Timeline pan, `TmGantt.razor:786-793`: `@onmousedown/@onmousemove/@onmouseup/@onmouseleave/@onwheel`. `TmGantt.razor.cs:976-999` makes **two to three awaited JS interop calls per mousemove** (`tmGantt.getScrollLeft/setScrollLeft/setScrollTop`, `wwwroot/js/gantt.js` 6 lines). Wheel at 1018-1032.
   - Dependency "drag": `razor:951-961` `@onmousedown` → `StartDepDrag` and `@onmouseup` → `OnDepDrop` (`cs:842-866`). There is no visual line; it is press on one handle, release on another.
   - Tree reorder: HTML5 DnD `razor:585-595` → `cs:677-729`.
   - Bars: `@onmousedown:stopPropagation` (896, 906). Bar drag/resize is **not implemented** (backlog).
   - The calendar view embeds `TmScheduler` with `HideTimeGrid=true` (`razor:393-398`), so it inherits the Scheduler month move (item 1).
   - Touch today: native scroll of the timeline container only. Keyboard today: none for reorder/dependencies.
   - Tests: `TmGanttDragTests.cs` (4 tests, tree DnD).
3. **Migration:** owned by **Gantt plan cfc9ae8a**:
   - Fáze 3 splits the monolith and keeps the mouse handlers.
   - **Fáze 7** "Tablet a dotyk — … pointer events": pan + dependency drag + long-press menu on core `pointer-drag.js`, "žádný gantt-specifický helper", `touch-action: pan-x pan-y`, tree DnD off on touch, Alt+↑/↓ tree reorder.
   - Fáze 8: mobile read-mostly.
   - Proposed text addition to Gantt F7 (Czech):
     ```
     - Pan timeline přes `attach(timeline, { pointerTypes: ['mouse', 'pen'], threshold: 4 })` — prst
       scrolluje nativně (`touch-action: pan-x pan-y`), takže se pan pro touch neimplementuje; scrollLeft/Top
       psát v JS (ne awaited interop na každý pohyb jako dnes `TmGantt.razor.cs` ř. 988–997).
     - Tažení závislosti: `attach(timeline, { selector: '.tm-gantt__dep-handle', threshold: 2 })`, cíl přes
       `document.elementFromPoint(...).closest('[data-testid^=task-bar]')`, viditelná dočasná čára, commit
       stávajícím `OnDepDrop`. `gantt.js` je klasický skript → helper přes `document.currentScript.src`
       (vzor `scheduler.js` ř. 16–24).
     - Alt+↑/↓ přesun ve stromu přes `attachKeyboardMove(treeBody, { selector: '.tm-gantt__tree-row', onStep })`
       — Blazor `@onkeydown:preventDefault` nepoužívat. Ohlášení `aria-live="polite"` „Úkol X přesunut za Y“.
     - Pinch-zoom (volitelný) jen přes jádrový `pointer-gesture.js` `attachPinch` (follow-up plán jádra), jinak
       vynechat — tlačítka zoomu jsou ne-drag alternativa.
     ```
4–7. Tests, verification and breaking are per the plan: CHANGELOG "mouse → pointer handlery" plus the `pointer-drag.js` load requirement. **Effort: L.**
8. **OPEN:** none beyond Q-P (section 4).
9. **Scope: Gantt plan F3/F7/F8.** Not core.

### 2.9 `TmSigningFieldOverlay` / `TmPdfTemplateDesigner`

1. **Status: OPEN; owned by a plan.**
2. **Current state** (Pkg **Tempo.Blazor.Signing**):
   - `TmSigningFieldOverlay.razor:21-22` (`@onmousedown="HandleStartMoveAsync"`) and `:101-104` (resize handles `@onmousedown`).
   - `TmSigningFieldOverlay.razor.cs:352-364` raises **public** `OnStartMove` / `OnStartResize` with `TmSigningFieldOverlayPointerEventArgs(…, MouseEventArgs MouseEventArgs)` / `TmSigningFieldOverlayResizeEventArgs(…, MouseEventArgs)`. These public records carry `MouseEventArgs`; `PointerEventArgs : MouseEventArgs`, so passing a pointer event is source-compatible.
   - `TmPdfTemplateDesigner.razor:8-9`: root `@onmousemove/@onmouseup` with a .NET round-trip per move. `.razor.cs:465-519`: deltas divided by `DefaultPageWidth/Height` constants, which are not zoom-aware.
   - Palette HTML5 DnD (`razor:39`); a mobile "tap type then tap page" alternative exists (`razor:25`).
   - `wwwroot/js/pdf-template-designer.js` (13 lines, module).
   - Keyboard: overlay Enter/Space = click (`cs:342-350`); Delete deletes (`cs:523`). **No arrow move.**
   - Also `TmDocumentPageViewer.razor:150-153` (comment anchors `@onmouse*`) and `TmRecipientRoleEditor.razor:17` (HTML5 DnD).
3. **Migration:** owned by **Signing plan 1fba4858**:
   - **Fáze 8**: characterization E2E; "`onmouse*` → pointer events přes `pointer-drag.js` … JS-side drag a commit do .NET na `pointerup`", palette pointer drag, coarse handles, `Designer_ArrowKeys_MoveSelectedField`, numeric geometry.
   - F9: viewer comment anchors via pointer events; pinch only if core supports it.
   - F7: role editor arrows.
   - Proposed text addition to Signing F8 (Czech):
     ```
     - Import helperu z `pdf-template-designer.js` (ES modul) relativně:
       `import { attach } from '../../Tempo.Blazor/js/pointer-drag.js';` (docs/touch-interaction.md
       „Consuming from a split package“). Veřejné `TmSigningFieldOverlayPointerEventArgs`/`…ResizeEventArgs`
       ponechat (nesou `MouseEventArgs`; `PointerEventArgs` z něj dědí) — OnStartMove/OnStartResize se dál
       vyvolávají z `@onpointerdown`, samotný drag a commit běží v JS. Delty přepočítat přes skutečný
       `getBoundingClientRect()` stránky (dnes `DefaultPageWidth/Height`, `.razor.cs` ř. 469–478), jinak
       zoom rozbije přesun. Posun šipkami je plain-arrow nudge kanvasu (docs: canvas editor smí);
       Alt+šipky přes `attachKeyboardMove` nejsou nutné.
     ```
4–7. Per the plan. **Breaking:** keep the public args records (no break). **Effort: L.**
8. **OPEN:** Q-P (pinch, F9).
9. **Scope: Signing plan F8/F9.** Not core.

### 2.10 DiagramEditor `diagram-editor.js`

1. **Status: OPEN; partially owned by a plan.**
2. **Current state** (Pkg **Tempo.Blazor.DiagramEditor**, `wwwroot/js/diagram-editor.js`, 4315 lines):
   - `_attachEvents` 247-276: container `mousedown`; **document** `mousemove/mouseup` (capture phase, 272-273); `wheel`; `touchstart/touchmove/touchend/touchcancel` non-passive (264-267); document `keydown/keyup`.
   - `_onMouseDown` 555-982, `_onMouseMove` 983-1299, `_onMouseUp` 1394-1805 cover node drag, rubber-band, edge draw, waypoints and pan.
   - **Private touch stack** 1832-2049:
     - **own pinch** 1835-1848 / 1918-1945 / 1997-2006 (`e.touches` distance, clamp 0.15–5×);
     - **own long-press** 600 ms context menu 1897-1906;
     - single-finger node drag / pan.
   - `startRotate` 3624-3687 has its own document mouse listeners. Toolbox HTML5 DnD 3579-3590.
   - Keyboard: arrow nudge 2856-2890 exists and is the alternative for move. Resize/rotate have no keyboard path in JS; check the properties panel W/H.
3. **Migration:** **DiagramEditor plan 94d441f2**:
   - F6 owns resize/rotate handles (`TmDiagramCanvas.razor:614-641`) on `pointer-drag.js`, the minimap on pointer events, tap-to-insert in the palette, and the layer menu.
   - F7 needs mobile pan/pinch/long-press menu as a sheet.
   - **Gap:** node drag, pan, rubber-band, edge drawing and the private pinch/long-press are not migrated by any phase.
   - Proposed text addition to DiagramEditor F6 (Czech):
     ```
     - Celý touch stack `diagram-editor.js` (ř. 1832–2049: `touchstart/move/end`, vlastní pinch, vlastní
       long-press 600 ms) nahradit: jednoprstý drag uzlu/pan/rubber-band přes `pointer-drag.js`
       (`import` relativně `../../Tempo.Blazor/js/pointer-drag.js`; `diagram-editor.js` je klasický skript →
       URL z `document.currentScript.src`), long-press přes `longPressMs` helperu, pinch + dvouprstý pan přes
       jádrový `pointer-gesture.js` `attachPinch` (follow-up plán jádra E-P). Druhá implementace pinch/long-press
       v balíčku je zakázaná (decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME). `startRotate` (ř. 3624–3687)
       na `attach(handle, { threshold: 0 })`. Myší cesta (`_onMouseDown/Move/Up`) může zůstat, dokud
       charakterizační E2E nepotvrdí paritu — pak sjednotit (upsert_decision).
     ```
4–7. Per the plan. **Effort: XL** for full unification; **L** for touch-stack replacement only.
8. **OPEN:** Q-P (whether core pinch exists before DE F6/F7).
9. **Scope: DiagramEditor plan F6/F7**, gated on core E-P for pinch.

### 2.11 Wireframe `wireframe-designer.js`

1. **Status: OPEN; owned by a plan, but the plan text violates the decision.**
2. **Current state** (Pkg **Tempo.Blazor.Wireframe**, `wwwroot/js/wireframe-designer.js`, 1536 lines):
   - `_attachEvents` 162-186: svg `mousedown`/`mousemove`, **document** `mousemove/mouseup`, `wheel`, `dragover`, document `keydown/keyup`. Handlers `_onMouseDown` 396, `_onMouseMove` 589, `_onMouseUp` 762.
   - No touch at all. Arrow nudge 1251-1258. Toolbox HTML5 DnD 1493.
3. **Migration:** **Wireframe plan ce19aa8c Fáze 5** "Dotyk a pointer events v plátně" switches `_attachEvents` to pointer events "na threshold, capture a long-press použít jádrový `pointer-drag.js`". It **also** plans its own `wireframe-gestures.mjs` (pinch, pointer-count routing, **own long-press detector ≈500 ms**) and says „Pinch není součástí F7: implementovat ho v balíčku …“. That conflicts with decision F7 ("multi-pointer … need a core sibling module, never an implementation inside a package") and duplicates `longPressMs`.
   - Exact replacement for that F5 bullet (Czech):
     ```
     - Gesta: jednoprstý drag/resize/rotate/rubber-band/connector/waypoint přes jádrový `pointer-drag.js`
       (`import { attach } from '../../Tempo.Blazor/js/pointer-drag.js'`; `wireframe-designer.js` je klasický
       skript → URL z `document.currentScript.src`), long-press → `_onContextMenu` přes `longPressMs: 500`
       helperu (žádný vlastní detektor). Pinch-zoom a dvouprstý pan přes jádrový sibling modul
       `src/Tempo.Blazor/wwwroot/js/pointer-gesture.js` (`attachPinch`) z follow-up plánu jádra (fáze E-P) —
       Brána: ověř, že E-P je Completed; pokud ne, pinch v této fázi vynech (ne-drag alternativa = tlačítka
       zoomu) a zapiš remaining. `wireframe-gestures.mjs` smí obsahovat jen čisté funkce specifické pro
       wireframe (hit-area ×1.5 pro coarse, double-tap → dblclick), ne pinch ani long-press.
     ```
4–7. Per the plan. **Effort: L.**
8. **OPEN:** Q-P.
9. **Scope: Wireframe plan F5/F7**, gated on core E-P.

### 2.12 Not in the inventory but found (information only, no action proposed here)

The grep of `@onmousedown|@ondragstart|@onpointerdown` in core turned up these:
- HTML5 DnD in `TmKanbanBoard`, `TmMultiViewList`, `TmDockManager` (113-317), `PivotTable` (`TmPivotFieldChip`, `TmPivotFieldPanel`) and `TmTreeView`;
- mouse range select in `TmTreeList.razor:48-50`;
- Notion board/table header drag (NotionEditor plan F8/F9 owns the Timeline only);
- Modeling tree→preview drag-and-drop (Modeling F6 gives a button alternative);
- Spreadsheet column/row resize (no plan text).

These are **not** in `docs/touch-interaction.md`'s list. Recommend adding them to that table in the core plan's docs phase so the inventory is complete.

---

## 3. Summary of what the core follow-up plan should contain (cluster E)

| Phase | Content | Effort | Depends |
|---|---|---|---|
| E-K | `attachKeyboardMove({ shift: true })` + node tests + docs | S | — |
| E-1 | `TmSplitterPane` → `tm-splitter.js` on `pointer-drag.js`, `role=separator` keyboard, remove `eval` | M | — |
| E-2 | `TmRangeSlider` → CSS dual-thumb (remove `eval` drag + listener leak), accessible names; `slider.js` deprecated no-op | S | Q-R1 |
| E-3 | `TmDataTable` column resize → delegated `attach` in `data-table.js`, keyboard separator, cancel ≠ commit | M | — |
| E-4a/b | `workflow-designer.js` → pointer-drag (node / port / pan), touch; keyboard Alt+Arrow via `attachKeyboardMove`, focusable states, Connect-to, localized context menu | L | Q-W1 |
| E-5 | `TmColorGradient` → `tm-color-gradient.js` on pointer-drag, rect-based fractions, keyboard sliders | M | — |
| E-6 | `tm-sheet.js` gesture onto `pointer-drag.js` (or the minimal `isPrimary` fix) | M (S) | Q-T1 |
| E-P | `pointer-gesture.js` (`attachPinch`, `createAutoScroller`), pilot in E-4 | M | Q-P |
| E-D | Docs: `docs/touch-interaction.md` table update (done rows, new rows from 2.12), CHANGELOG, AGENTS JS-interop list (`tm-splitter.js`, `tm-color-gradient.js`, `pointer-gesture.js`), plan-text updates (1.4, 2.4, 2.8, 2.9, 2.10, 2.11) | S | all |

---

## 4. [6de6d5b9] Multi-pointer pinch/pan + auto-scroll core sibling module

### 4.1 Status: **OPEN** (nothing exists in core)

- `pointer-drag.js` is single-pointer by design (`isPrimary` guard at line 289, docs line 21-22).
- **Private implementations already exist:**
  - DiagramEditor pinch: `diagram-editor.js:1835-1848, 1918-1945, 1997-2006` (Touch Events, not Pointer Events).
  - Spreadsheet edge auto-scroll: `spreadsheet-canvas.js:2994-3062` (`updateDragAutoscroll/stopDragAutoscroll/runDragAutoscroll`, `edge = 36`, `maxStep = 28`, rAF loop).
- **Planned private ones:** Wireframe F5 `wireframe-gestures.mjs` (pinch) violates the rule.

### 4.2 Who needs it (from the plan extraction)

| Consumer | Need | Plan text |
|---|---|---|
| PdfViewer/Annotator | pinch-zoom + two-finger pan; draw with one finger | F9 "přes `pointer-drag.js` z F7 … Pokud helper pinch neumí, zapiš požadavek na základ místo lokální kopie" — **explicit request to core** |
| DiagramEditor | pinch + pan (mobile), replacing the private pinch | F7 (no owner named) |
| Wireframe | pinch + two-finger pan | F5 (plans its own — must change, 2.11) |
| Spreadsheet | pinch → `OnZoomStep`; drag auto-scroll exists privately | F7 "vlastní kopie se NEpíše" |
| Gantt | optional pinch-zoom | F7 "(volitelně)" |
| Signing viewer | pinch/pan only if core supports it, else backlog | F9 |
| Auto-scroll | Scheduler touch move (drag to the grid edge), Dashboard (long grid), DataTable column resize (horizontal edge), Gantt dependency drag, Spreadsheet (migrate its private one) | no plan mentions a core auto-scroller |

### 4.3 Design proposal

New ES module **`src/Tempo.Blazor/wwwroot/js/pointer-gesture.js`**, a sibling of `pointer-drag.js` with no dependencies. `pointer-drag.js` stays single-pointer.

```js
/**
 * Two-pointer pinch/pan. Tracks the first two primary-or-not pointers by pointerId on `element`.
 * @returns {{ detach(): void, cancel(reason?: string): void, readonly active: boolean }}
 */
export function attachPinch(element, {
    pointerTypes = ['touch'],          // pen/mouse cannot pinch
    wheel = true,                      // ctrl+wheel (trackpad pinch) → onChange with a scale factor
    minScale = 0, maxScale = Infinity, // clamp of the CUMULATIVE scale reported
    touchAction,                       // default: 'none' set on the element (restored on detach)
    onStart,   // ({ centerX, centerY, distance, reducedMotion }) — host should cancel its pointer-drag: drag.cancel('pinch')
    onChange,  // ({ scale, centerX, centerY, dx, dy, event, reducedMotion }) — scale relative to start, dx/dy = centre pan
    onEnd,     // ({ scale, centerX, centerY, dx, dy }) — commit once
    onCancel,  // ({ reason }) — 'escape' | 'pointercancel' | 'detached' — roll back
} = {}) { … }

/**
 * Edge auto-scroll while something is dragged near a scroll container edge. rAF loop; no listeners of its own.
 * @returns {{ update(clientX: number, clientY: number): void, stop(): void, readonly scrolledX: number, readonly scrolledY: number }}
 */
export function createAutoScroller(container /* Element | null = nearest scrollable ancestor */, {
    edge = 36, maxStep = 28, axis = 'both', // 'x' | 'y' | 'both'
    onScroll,                                // ({ dx, dy }) after each scrolled frame: re-hit-test the drop target
} = {}) { … }
```

Contract notes for the implementer:
- **Two pointers.** `pointerdown` records `{id, x, y}` in a Map. Pinch starts when the size reaches 2. With 3+ pointers, ignore the extras. Pinch ends when the size drops below 2 (`onEnd`). Cancel paths are `pointercancel` of either pointer, Escape on document capture phase with `stopPropagation` (as `pointer-drag.js:266-272`) and `detach()`.
- **Pointer capture** by id for both pointers; release by id on every exit.
- **Coexistence with `attach()`.** The single-finger drag already started on finger 1. The pinch's `onStart` is where the host calls `drag.cancel('pinch')`. Document that pattern and test it.
- **ctrl+wheel.** `scale = Math.exp(-deltaY / 100)`; `onStart`/`onChange`/`onEnd` are emitted in one go, debounced 150 ms for `onEnd`. Use a non-passive wheel listener and call `preventDefault` only when `ctrlKey`.
- **Reduced motion.** It is passed through and the host decides (snap zoom steps instead of continuous).
- **`createAutoScroller`.** A port of `spreadsheet-canvas.js:3026-3038` (edge power formula), generic over `scrollLeft/scrollTop`. `scrolledX/Y` is the cumulative scroll since creation, so the host computes the content delta as `dx + scrolledX`. **No change to the `attach()` API**: hosts call `scroller.update(info.x, info.y)` from `onMove` and `scroller.stop()` from `onRelease`.
- **WCAG 2.5.7.** Pinch must always have a single-pointer alternative: zoom buttons. Document it in `docs/touch-interaction.md` in a new section "Multi-pointer gestures".

RED tests (`src/Tempo.Blazor/wwwroot/js/__tests__/pointer-gesture.test.mjs`, stub style of `pointer-drag.test.mjs`):
- two touch pointers apart, then further apart → `onChange` scale > 1, centre;
- one pointer → no pinch;
- a third pointer is ignored;
- pointercancel of either pointer → `onCancel`, no `onEnd`;
- Escape cancels and stops propagation;
- mouse pointers are ignored by default;
- ctrl+wheel → a scale factor and `preventDefault` only with ctrl;
- `detach` restores touch-action and removes listeners;
- `createAutoScroller` scrolls by `round(power * maxStep)` near each edge, by nothing in the middle, `axis: 'y'` ignores x, `stop()` cancels rAF, and `scrolledY` accumulates.

E2E: CDP `Input.dispatchTouchEvent` with **two `touchPoints`** (ids 1, 2) spreading apart, on the pilot consumer (Q-P), at 768 and 390.

### 4.4 PRODUCT DECISION Q-P: build now without a consumer?

Options:
- **(A) Build `pointer-gesture.js` now in the core follow-up plan, with a real core pilot:** `TmWorkflowDesignerCanvas` pinch-zoom (E-4, it already has zoom maths in `workflow-designer.js:231-253`) and `createAutoScroller` in the DataTable column resize (E-3, horizontal edge) or the workflow node drag. Then update the DiagramEditor/Wireframe/PdfViewer/Spreadsheet/Gantt plan texts to consume it. *Pros:* one implementation before any package plan starts, and the API is validated by a real consumer and E2E. *Cons:* about M more work in the core plan, and it depends on E-4 being in scope.
- **(B) Build it in the first consumer plan that starts** (PdfViewer F9 or Wireframe F5) as a **core** module, with the same coordination clause the Scheduler F7 had for `pointer-drag.js`. *Pros:* designed against the hardest consumer (PdfViewer draw-vs-pan). *Cons:* the race the Scheduler/F7 coordination already showed. Wireframe F5 currently says "implement in package", so its text must change anyway.
- **(C) API only (no consumer)** in the core plan with node tests. *Cons:* an unvalidated API, which the rule-of-three argues against.
- **(D) Defer**, keeping the rule in the docs and only fixing the Wireframe/DiagramEditor plan texts to forbid package-local pinch.

**Recommendation: (A)** if Q-W1 = (a), i.e. the workflow designer is migrated in this plan. Otherwise **(B)**, with PdfViewer F9 as the designated builder. In both cases apply the plan-text fixes in 2.10/2.11 now, so no package writes its own pinch. `createAutoScroller` is cheap: ship it in (A), or in Scheduler F7 if Q-P resolves to (B)/(D).

- **Status:** OPEN. **Effort:** M (module + tests + pilot integration). **Breaking:** no; it is a new file (AGENTS JS-interop list + CHANGELOG `Added`).
- **Scope:** core follow-up plan (E-P), or the first consumer plan under (B).

---

## 5. Summary table

| ID | Item | Status | Pkg | Mechanism today | Touch today | Keyboard alt today | Owner (plan + phase) | Core plan? | Effort |
|---|---|---|---|---|---|---|---|---|---|
| 59e85790+8b353a98 | Scheduler move/keyboard/aria-live/live time | PARTLY DONE (resize on helper ✔; move/kbd/live ✘) | core | resize: pointer-drag; move: HTML5 DnD | resize ✔ (long-press 400), move ✘ | none | **Scheduler 09c5afa6 Fáze 7** (needs F2, F5); text update 1.4 | E-K (shift) + plan text | L (+S core) |
| 32e4e6c4/10bcfc90 | TmSplitterPane | OPEN | core | Blazor @onpointer* + `eval` capture | partial (round-trip per move) | none | none | **E-1** | M |
| ″ | slider.js / TmRangeSlider | OPEN | core | `eval` drag, leaking window listeners; slider.js unused | broken (pan → cancel) | native range keys, no names | none | **E-2** (CSS dual-thumb) | S |
| ″ | TmDataTable column resize | OPEN | core | data-table.js document pointer listeners, no capture, cancel commits | partial (6 px) | none | DataTable 6288f7e0 has no text | **E-3** (+note in DataTable F2) | M |
| ″ | dashboard.js | OPEN | core | HTML5 DnD + mouse | none | none | **Dashboard d254e3dd F4 + F5** (text fix 2.4) | no | L |
| ″ | workflow-designer.js | OPEN | core | mouse + document listeners | none | none (not focusable) | none | **E-4a/b** | L |
| ″ | tm-sheet.js | OPEN | core | own pointer impl (capture, velocity) | ✔ | handle slider + Escape | none (foundation) | **E-6** (or minimal fix) | M (S) |
| ″ | TmColorGradient | OPEN | core | Blazor @onpointer*, no capture, fixed 200×150 | partial | RGBA inputs only (sliders not focusable) | none | **E-5** | M |
| ″ | TmGantt | OPEN | core | @onmouse* pan (interop per move), mouse dep handles, HTML5 tree DnD | native scroll only | none | **Gantt cfc9ae8a F3/F7/F8** (text add 2.8) | no | L |
| ″ | TmSigningFieldOverlay / designer | OPEN | Signing | @onmousedown + root mousemove/up | tap-type alt only | Enter/Delete; no arrows | **Signing 1fba4858 F8/F9** (text add 2.9) | no | L |
| ″ | DiagramEditor | OPEN | DiagramEditor | mouse + private touch (pinch, long-press) | ✔ private | arrow nudge | **DiagramEditor 94d441f2 F6/F7** — gap: node/pan/pinch (text add 2.10) | E-P dependency | XL/L |
| ″ | Wireframe | OPEN | Wireframe | mouse + document listeners | none | arrow nudge | **Wireframe ce19aa8c F5/F7** — plans own pinch ✘ (text fix 2.11) | E-P dependency | L |
| 6de6d5b9 | pinch/pan + auto-scroll module | OPEN | core | private in DiagramEditor/Spreadsheet | — | zoom buttons | none | **E-P** (decision Q-P) | M |
| 64114d91 | InteractiveAuto Scheduler_ViewsWork | — | — | — | — | — | cluster A (skipped) | — | — |

### Open questions (with recommendation)

**Scheduler**
- **Q-E1** pen move: (a) touch only → **a**.
- **Q-E2** keyboard model: (a) immediate Alt+Arrow → **a**.
- **Q-E3** live time format HH:mm → **a**.
- **Q-E4** one live region in TmScheduler → **a**.

**Core gestures**
- **Q-S1** splitter live size: JS inline → **a**.
- **Q-S2** no `SizeChanged` → **no**.
- **Q-R1** range slider: CSS dual-thumb → **A**.
- **Q-D1** DataTable column resize in the core plan → **a**.
- **Q-W1** migrate the workflow designer in the core plan → **a**.
- **Q-W2** workflow node drag on touch: immediate → **a**.
- **Q-T1** tm-sheet: full migration → **a** (fallback c).
- **Q-C1** colour gradient live preview: rAF .NET calls → **a**.

**Product decision**
- **Q-P** pinch/auto-scroll: build now with the workflow-designer pilot → **A** (else B = PdfViewer F9 builds it as core).
