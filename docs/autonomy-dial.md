# TmAutonomyDial — how much the agent may do on its own

`TmAutonomyDial` (`Tempo.Blazor.Components.AI`, package `Tempo.Blazor.AI`) is the control a reader uses to say how far an
agent may go without asking: the grades the host declares, the ceiling a plan imposes and the limits somebody else
enforces. It is a CONTROL, not a read-out — the value is chosen with arrow keys, Home/End, a segmented radio group or a
pair of native step buttons.

The shared identity model and the accessibility rules every agent surface follows are documented once in
[ai-agents.md](ai-agents.md). This page is the `TmAutonomyDial` contract.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmAutonomyDial Levels="_levels"
                @bind-Value="_value"
                Locks="_locks"
                MaxAllowed="2"
                ConfirmAbove="2" />
```

## The models

| Type | What it is |
|---|---|
| `AutonomyLevel` | One stop: `Value` (the grade a host persists and a ceiling compares against), `Label`, `Description`, plus `HasLabel` / `HasDescription`. A record VALUE. |
| `AutonomyLock` | A limit the surface cannot lift: `ActionLabel`, `Source` (who enforces it), `Reason?`, plus `HasSource` / `HasReason`. |
| `AutonomyDialPresentation` | `Dial` (0, the default) \| `Segmented`. Host-owned, not two-way. |

`AutonomyDialPresentation.Dial` is the default on purpose: an unset presentation must land on the slider rather than on a
cast integer. The component normalizes an unknown value back to the dial.

### The default stops

`Levels` is optional. Null or empty renders the three localized stops every agent product starts from:

| Value | English label | What it says |
|---|---|---|
| 0 | Ask first | Every action waits for your approval. |
| 1 | Ask when risky | Risky actions wait for your approval; everything else runs on its own. |
| 2 | Act freely | The agent acts without asking and you review the result afterwards. |

The host's stops are copied, nulls are dropped, a duplicate `Value` is kept ONCE and the list is sorted by `Value`
ascending — ArrowRight always means "more autonomy". A stop the host left unlabelled prints the localized
`Level {n}` fallback, so no stop is ever blank.

## The dial

`Presentation="Dial"` (the default) renders a real `role="slider"` with ONE tab stop:

- `aria-valuemin` / `aria-valuemax` are the allowed grades (the ceiling lowers the max), `aria-valuenow` is the current
  grade as an invariant number, and `aria-valuetext` is the grade's LABEL — a screen reader hears "Ask when risky", not
  "1".
- `ArrowRight` / `ArrowUp` move one grade up, `ArrowLeft` / `ArrowDown` one down. `Home` / `End` jump to the first and
  last ALLOWED grade — `End` stops at the ceiling instead of jumping past it. A key past an end changes nothing and
  announces nothing.
- The slider's children are purely decorative (the rail, the fill and the stop dots), so the widget never owns an
  interactive descendant. The two native step buttons sit BESIDE the slider and are the pointer alternative
  (WCAG 2.5.7); at an end of the allowed range the pressed button carries `aria-disabled="true"` instead of `disabled`,
  so it keeps the focus (a natively disabled button drops it to `<body>` mid-press — WCAG 2.4.3) and `StepAsync` is the
  one guard that makes it inert.
- The heading names the slider (`aria-labelledby`) when `ShowTitle` is true; without a title the slider carries the
  localized `Autonomy level` name. The current stop's description is the slider's `aria-describedby`.

### The six keys are also the page's keys (`tm-autonomy-dial.js`)

`ArrowUp` / `ArrowDown` / `ArrowLeft` / `ArrowRight` / `Home` / `End` are the dial's own keys AND the browser's scroll
keys. Blazor cannot express "prevent the default for these six and nothing else": `@onkeydown:preventDefault` is
unconditional per element, so it would also swallow Tab, Escape and every other key the surface must keep.

The package therefore ships a tiny ES module, `_content/Tempo.Blazor.AI/js/tm-autonomy-dial.js`, imported through
`IJSObjectReference` (no `<script>` tag, no eval):

- `attachDialKeys(slider)` installs ONE `keydown` listener on the rendered slider, idempotently (a re-render that hands it
  the same element adds nothing, so `preventDefault` cannot run twice);
- the listener calls `preventDefault` for the four arrows and Home/End only, and never `stopPropagation` — Blazor still
  receives the very same event, so the component's keyboard model is untouched and the guard changes no value by itself;
- `detachDialKeys(slider)` releases it, and the component releases the guard when it is disposed;
- every interop call is guarded: where JavaScript is unavailable (prerender, a test double, a disconnected circuit) every
  key still moves the dial — only the page may scroll along with it. Nothing surfaces as a throw into the render loop.

The module is covered by node tests (`npm run test:ai`) for the key set, the idempotent attach, the detach and the
no-DOM cases.

---

`Presentation="Segmented"` swaps the slider for a `TmSegmentedControl` radio group in which every grade is its own
target. The radio group brings its own arrow/Home/End model, and a grade above the ceiling renders **disabled** so the
limit is visible in the control itself. The segmented presentation renders no slider, so it attaches no scroll guard.

## The ceiling

`MaxAllowed` is the highest `AutonomyLevel.Value` a plan allows (null allows every declared stop). The stops above it
still render — marked `data-available="false"`, dashed and dimmed, and disabled in the segmented presentation — and the
surface prints an explanation under the dial rather than silently refusing a key:

- `TmAutonomyDial_Limit` — "Levels above {ceiling label} are not available."
- `MaxAllowedReason` or the localized `TmAutonomyDial_LimitDefaultReason` — why.

A `Value` above the ceiling (or between two stops) renders the nearest allowed grade; the component never writes the
host's state behind its back, and the next change reports an allowed value.

## The enforced limits

`Locks` lists what the surface cannot change, each with its action and — when known — who enforces it and why. A lock is
a FACT, never a control: the list is not a tab stop, and the lock glyph is `aria-hidden` because the "Enforced by
{source}" sentence already carries it. A lock without a source prints no "enforced by" line instead of a line with a
blank source.

## Confirmation

`ConfirmAbove` is the grade at or above which a change asks first (null commits every change at once). A held change
renders a `role="group"` next to the dial:

- the message names the grade it is about to commit AND its description;
- the focus moves to the confirm button, so a keyboard reader can answer without hunting;
- Enter/Space on the native button commits (`ValueChanged` fires once), Escape or Cancel drops the change and the focus
  returns to the slider (WCAG 2.4.3).

The confirmation is not a live region of its own — its message is in front of the reader, and the surface already owns
ONE polite region.

## Accessibility

- `role="slider"` with an invariant `aria-valuenow` and a localized `aria-valuetext`; the grade's description is the
  slider's description.
- One tab stop for the dial, with a native step-button alternative for pointer users (WCAG 2.5.7). A step button at an
  end of the allowed range stays focusable (`aria-disabled="true"`, never `disabled`), so pressing it to the last stop
  cannot drop the reader's focus on the floor (WCAG 2.4.3 / 2.1.1).
- The confirmation promises the reader a way out (Escape / Cancel) and hands the focus back to the slider.
- ONE polite live region announces a COMMITTED change ("Autonomy level: {label}"); a repeated sentence is re-announced
  through an invisible marker. A refused key announces nothing.
- `(pointer: coarse)` raises the step buttons and both confirmation buttons to the 44 px `--tm-touch-target`.
- `forced-colors` hands the track and the lock borders to `CanvasText`, the fill and the reached dot to `Highlight`, and
  every focus ring to a 3 px `Highlight` outline.
- The only movement in the partial is the track fill's travel, and `prefers-reduced-motion: reduce` removes it.

## CSS

`.tm-ai-autonomy`, `--segmented`, `__header`, `__title`, `__readout`, `__current`, `__position`, `__description`,
`__dial`, `__control`, `__rail`, `__fill`, `__stop`, `__stop-dot`, `__stop-label`, `__step`, `__segmented`, `__limit`,
`__limit-reason`, `__locks`, `__locks-title`, `__lock`, `__lock-icon`, `__lock-action`, `__lock-source`,
`__lock-reason`, `__confirm`, `__confirm-message`, `__confirm-actions`, `__confirm-accept`, `__confirm-cancel` — tokens
only. The fill's extent is the `--tm-autonomy-progress` knob the component writes and the rule reads; the tone is
`--tm-autonomy-tone`. Guarded by `AiMarkdownViewCssTests` and `TmAutonomyDialCssTests`.

## Localization

`TmAutonomyDial_*` (title, slider label, the position format, the per-position fallback, the three default pairs, the
step-button labels, "enforced by", the limits title, the ceiling sentence and its default reason, the confirmation
message and its two actions, and the announcement) in English, Czech, Slovak and French.

## Wireframe

The `autonomy-dial` UI role (synonyms in both languages) plus the `TmAutonomyDial` schema (`title`, `levels`, `value`,
`maxAllowed`, `presentation`, `locks`) and its declarative stencil, which draws the heading, the presentation chip, the
track with its fill, one dot and label per level (a stop above the ceiling is drawn in the warning tone), the ceiling
line and one enforced limit per supplied lock with its "enforced by" line.

**Stencil check (plan 7ba95ed6, Fáze 5):** the scroll guard adds no `[Parameter]` and changes nothing a designer draws —
the stencil still draws the same track, stops and ceiling, because the guard is a browser behaviour of the RUNTIME slider
(and the wireframe sketch has no keyboard at all). The wireframe citizen of this state stays the existing `autonomy-dial`
role (`BuiltInUiRoleVocabularySource`, `BuiltInComponentSchemas`,
`TempoNativeRendererDefinitions.RenderAutonomyDial`), so the check is recorded here rather than as a new schema prop.

## Demo

`/ai-agents` → the Autonomy section: four host grades (Observe / Assist / Act / Autopilot), the plan's ceiling at Act
(Autopilot is offered but unavailable), an enforced limit from a tenant and one from a workspace administrator, and the
confirmation above Assist. The page owns the presentation and the grade and records what it received.
