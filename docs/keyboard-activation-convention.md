# Keyboard activation convention (2.8.25)

Enter and Space must produce **exactly one activation** per press, no matter which element carries
the handler. The browser already activates native elements, so the library's part is: never emulate
what is native, always emulate what is not, and fence the two off from each other where they nest.

## Native elements activate natively — never emulate

`<button>` turns keys into clicks itself: Enter fires `click` on **keydown**, Space fires it on
**keyup**. The other natively activatable elements differ per element — a checkbox's Space produces
`change` on keyup while its Enter does not activate, an `<a href>` answers Enter while Space
scrolls, a `<select>` opens — so the rule is per element, not per "activatable": never attach an
`@onkeydown` that invokes the same action the element already performs natively.

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

**Space fires on keyup, Enter on keydown — match the native split, and guard Enter's repeat.**
A `div[role="button"]`'s keydown handler must not react to Space (the browser's default action
for Space on a focused non-form element is to scroll the page, and there is no reliable,
non-blanket way to `preventDefault()` only that key — `@onkeydown:preventDefault` is a
render-time bool, not a per-key one, so a static `true` would also swallow Tab and trap focus
inside the trigger, a worse defect than the scroll). Emulate Space on **keyup** instead, exactly
where the native `<button>` fires its click, and guard Enter's keydown handler with `!e.Repeat`
so a held key does not re-toggle on every auto-repeat event. `TmPopover` and `TmContextMenu`
(2.9.0) are the reference implementation — the residual default-scroll on Space keydown is
accepted, not fixed: it is cosmetic (the panel has not opened yet, there is nothing to lose
focus of) and the alternative is worse. Where emulation moved to keyup, any existing
`@onkeydown:stopPropagation` fence around native children needs a matching
`@onkeyup:stopPropagation` — a bubbled Space keyup off a nested control would otherwise fire
the container's activation on top of the child's own.

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

The library **can** fence template output at a wrapper boundary it renders — `TmDataTable` does
exactly that for `HeaderTemplate`, wrapping it in a span carrying `SwallowTemplateKey` plus
`@onkeydown:stopPropagation` (`TmDataTable.razor:210`), so consumer content inside a column header
needs no barrier of its own. What the library cannot do is reach INSIDE a fragment — and where the
fragment renders with no such wrapper, no fence exists at all: `TmDataTable`'s `CellTemplate` /
`EditTemplate` emit straight into the `<td>` (`TmDataTable.razor:466-469`), and `TmMultiSelect`'s
`HeaderTemplate` sits unfenced inside a popup whose container-level handler answers Escape, arrows
and Backspace (`TmMultiSelect.razor:124-126`). Consumers placing focusables in those fragments must
add `@onkeydown:stopPropagation="true"` on that content themselves — otherwise the host's row or
popup handler answers keys meant for the templated control (in the MultiSelect header: arrows move
the option highlight under the user's input, Escape closes the popup, Backspace removes the last
selected value).

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
`MissingEventHandlerException` ("The element does not have an event handler for the event
'onkeydown'") is the observable proof that the keydown never reached the container's handler.

## Motivating change

This convention was established by the 2.8.25 release (see the `## 2.8.25` entry of
[`CHANGELOG.md`](../CHANGELOG.md)), which removed the redundant emulation across 43 component
files in five packages and added the barriers, focus restoration and input-scoped handlers above.
Coverage lives in the per-component regression tests added with it (e.g.
`tests/Tempo.Blazor.Tests/DataTable/TmDataTableGroupingTests.cs`,
`…/TmDataTableSelectionTests.cs`), each asserting exactly-once activation over the real event
order — there is no source-text sweep, so the rule is carried by these tests plus review.
