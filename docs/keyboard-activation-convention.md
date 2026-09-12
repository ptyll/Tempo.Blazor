# Keyboard activation convention (2.8.25)

Enter and Space must produce **exactly one activation** per press, no matter which element carries
the handler. The browser already activates native elements, so the library's part is: never emulate
what is native, always emulate what is not, and fence the two off from each other where they nest.

## Native elements activate natively — never emulate

`<button>`, `<a href>`, `<input type="checkbox">` and `<select>` turn keys into clicks themselves:
Enter fires `click` on **keydown**, Space fires it on **keyup**, and a checkbox's Space produces
`change` on keyup.

```razor
@* ✘ rejected — the emulated invocation plus the native click fire the action TWICE *@
<button @onclick="Save" @onkeydown="@(e => e.Key is "Enter" or " " ? Save() : Task.CompletedTask)">

@* ✔ correct — the browser delivers the click; @onkeydown stays free for other keys *@
<button @onclick="Save" @onkeydown="HandleNonActivationKeys">
```

The double-fire is not theoretical: in containers that restore focus to the trigger on close, the
second invocation landed on the re-focused trigger and re-opened the panel the key had just closed.

## Non-native focusables REQUIRE emulation

`div[role="button"]`, `tr`, `li`, `span` and SVG elements have no native activation — a
`tabindex="0"` element that answers only to the mouse is a keyboard trap. They keep their
`@onkeydown` Enter/Space → action handlers (e.g. `TmDataTable`/`TmMultiViewList` rows, the
`TmContextMenu` trigger, the `TmMultiSelect` combobox trigger when it carries no filter input).

## Fence native children inside an emulating container

When a native control sits inside a non-native element that emulates activation — a grid row with
Enter→`OnRowClick` containing a checkbox, a dropdown trigger containing a clear button — a bubbled
keydown would reach the container's handler on top of the child's own click. Fence the child:

```razor
@* ✔ the checkbox's keys never reach the row *@
<input type="checkbox" @onclick:stopPropagation="true" @onkeydown:stopPropagation="true" />
```

In `RenderTreeBuilder` code (`.razor.cs` render fragments) the same barrier is emitted via
`builder.AddEventStopPropagationAttribute(seq, "onkeydown", true)` — a plain
`AddAttribute(seq, "onkeydown:stopPropagation", true)` does **not** create a barrier; the colon
name is not picked up as one outside the Razor compiler.

A consumer's `@onkeydown` on a wrapper above a barriered child no longer sees keydowns originating
on that child — that cutoff is the intended effect, not a side effect.

## Container-level handlers belong on the owning input

An Enter handler that means "confirm what was typed" lives on the search/filter **input**, not on
the container — a container-level handler also receives bubbled keydowns from every focused child
button and double-activates on top of the child's click. Two deliberate exceptions: **Escape** is
intentionally container-level (it means "dismiss the surface", whichever child has focus), and a
"commit on Enter anywhere" contract exists only where it was designed as one (e.g. the
`TmDataTable` edit row) — not wherever a handler happened to be hoisted.

## Consumer templates self-barrier

Focusable content a consumer puts into `CellTemplate`, `EditTemplate`, `HeaderTemplate` and similar
render fragments is outside the library's reach — the library cannot fence markup it does not
render. Consumers placing inputs or buttons inside a keyboard-handling container must add
`@onkeydown:stopPropagation="true"` on that content themselves; without it the host's header/row
handler answers keys meant for the templated control.

## Focus restoration on popup close

A popup that moves focus into content it destroys on close (a filter input, a menu) leaves focus on
`<body>` — a WCAG 2.4.3 focus-order violation. Components that do this must restore focus to the
trigger: hold an `ElementReference` on the trigger, set a `_focusTriggerAfterClose` flag on every
close path where focus could have been inside the popup (Escape, item select, confirm,
toggle-close), and call `FocusAsync(preventScroll: true)` from `OnAfterRenderAsync` — the pattern
`TmColorPicker` established and 2.8.25 extended to `TmFilterableDropdown` and `TmMultiSelect`.

## bUnit testing

bUnit does **not** synthesize the native keyboard→click sequence, so tests must dispatch the real
browser order explicitly:

| Gesture | Dispatch |
|---|---|
| Enter on a native element | `KeyDown("Enter")` + `Click` |
| Space on a native element | `KeyDown(" ")` + `KeyUp(" ")` + `Click` |
| Space on a checkbox | `KeyDown(" ")` + `KeyUp(" ")` + `Change` |

Then assert the action ran **exactly once**. A lone `KeyDown` asserting activation tests the
emulation path — which is the defect shape, not the contract. On a barriered path, bUnit's
`MissingEventHandlerException` ("no handler is associated with the event") is the observable proof
that the keydown never reached the container's handler.

## Motivating change

This convention was established by the 2.8.25 release (see the `## 2.8.25` entry of
[`CHANGELOG.md`](../CHANGELOG.md)), which removed the redundant emulation across 43 component
files in five packages and added the barriers, focus restoration and input-scoped handlers above.
Coverage lives in the per-component regression tests added with it (e.g.
`tests/Tempo.Blazor.Tests/DataTable/TmDataTableGroupingTests.cs`,
`…/TmDataTableSelectionTests.cs`), each asserting exactly-once activation over the real event
order — there is no source-text sweep, so the rule is carried by these tests plus review.
