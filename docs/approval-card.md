# TmApprovalCard

A request for a human decision: heading, description, a risk badge, a host-supplied preview and
key/value details, a deadline countdown, and Approve / Reject actions with an optional note. A
decided card collapses into one row that names who decided and when, and — while an undo window is
open — offers Undo.

Plan: `10b3966f` (human-in-the-loop, phase 2). Package: `Tempo.Blazor` (core).
Models: `Tempo.Blazor.Abstractions.Models.Approvals`.

```razor
@using Tempo.Blazor.Components.Approvals
@using Tempo.Blazor.Abstractions.Models.Approvals

<TmApprovalCard Title="Release the invoice"
                Description="The requester wants the invoice sent today."
                Risk="ApprovalRisk.High"
                Details="@details"
                PreviewContent="@preview"
                ExpiresAt="@deadline"
                RequireNoteOnReject="true"
                UndoWindow="TimeSpan.FromSeconds(30)"
                OnDecision="@SubmitAsync"
                OnUndo="@WithdrawAsync"
                @bind-State="state" />
```

## The card is a controlled component

`State` is the request's state and `StateChanged` reports every transition — a decision, the
automatic expiry and Undo. The card raises `StateChanged` and then keeps its own state until the
host passes a different `State` value; when a host reuses ONE card instance for a DIFFERENT request,
render it with `@key="requestId"` so Blazor recreates the component (or set `State="Pending"`
explicitly, which resets the collapsed row).

`OnDecision` is `Func<ApprovalDecision, Task>`. The card stays `Pending` while it runs (both actions
disabled, root `aria-busy="true"`), and leaves `Pending` **only** when it completes without throwing.
A throw shows a localized `TmAlert` and leaves the request decidable, so a failed save never looks
like a decision. The one exception is time: if the deadline passed while the save was running, the
failed decision still expires the card — a request that ran out of time is not decidable, and the
card announces the expiry instead of asking the user to retry something that can no longer succeed.

**Stamp the decision on the server.** The card builds the `ApprovalDecision` and leaves `Pending`
only after the callback succeeded, but the timestamp it puts into `DecidedAt` is `DecidedAt ??` the
card's own clock — the browser clock in a WASM host. A host that stores the decision stamps the
server time itself (either set the `DecidedAt` parameter or use
`ApprovalDecision.Approve(decidedBy, DateTimeOffset.UtcNow)` of its own instead of the argument), so
the persisted "when" cannot be moved by a client clock.

## States

| State | Card |
|---|---|
| `Pending` | The full request: heading, risk, description, preview, details, note field, countdown, actions. |
| `Approved` / `Rejected` | Collapsed row: `Approved — {who}, {when}`, the decider's note, Undo while the window is open. |
| `Expired` | Collapsed row reading "Expired"; the actions are disabled (rendered but `disabled`). Reached from `ExpiresAt` or from a host-supplied `State`. |
| `Cancelled` | Collapsed row reading "Cancelled"; no Undo. |

The time-driven `Pending → Expired` transition raises `StateChanged(Expired)` once, so a host that
persists the state learns about it.

## Countdown

`ExpiresAt` renders the remaining time as `mm:ss` — or `h:mm:ss` once an hour or more remains — in a
`<time datetime="…">` element. The value is **not** a live region (`aria-live="off"`): a ticking
clock must not be announced every second. Instead:

- one minute before the deadline the card announces it once through its single live region,
- when the deadline passes it announces the expiry and disables the actions.

The countdown reads as ordinary text (`--tm-text-primary`) and takes the danger colour only once the
deadline has passed (`.tm-approval-card__deadline--expired .tm-approval-card__countdown`). Red has to
keep meaning "this is urgent": a card with hours left must not look like an expired one, and the
one-minute warning must stay the only early signal. The shipped bundle is guarded from the other
side by `TmApprovalCardCssTests`.

The one-second timer (`ApprovalCountdownTicker`) is armed **only** while there is something to count
(a pending deadline or an open undo window) and disarmed the moment there is not, so a decided card
never re-renders. It reads the injected `TimeProvider` (`TimeProvider.System` when the host registers
none), which is also what makes `Microsoft.Extensions.Time.Testing.FakeTimeProvider` work in tests.

## Undo window

`UndoWindow` starts at the moment the decision was raised and offers Undo in the collapsed row until
it elapses. Undo is offered only when a handler is wired (`OnUndo` or `Undone`) — a window with no
handler would revert the local state without persisting anything. The handler runs first and, when
it succeeds, the card returns to `Pending`, raises `StateChanged(Pending)` and moves focus back to
Approve; a throwing handler keeps the decision and shows the localized error.

## Keyboard

| Keys | Action |
|---|---|
| `Ctrl+Enter` / `⌘+Enter` | Approve |
| `Ctrl+Backspace` / `⌘+Backspace` | Reject |

The shortcuts are advertised through `aria-keyshortcuts` on the matching button, fire only while
focus is inside the card, are ignored while a decision is in flight or the card is decided, and
**never** fire from inside the note field: the textarea fences its own keys with
`@onkeydown:stopPropagation` (so a decision is never raised from a text field), and `Escape` inside
it clears the draft note. `EnableShortcuts="false"` removes both the handlers and the
`aria-keyshortcuts` declarations.

## Note validation

The note field is offered when `AllowNote` (default `true`) or `RequireNoteOnReject` is set. With
`RequireNoteOnReject="true"` a reject with a blank note does not raise `OnDecision`: the field gets
`aria-invalid="true"` and `aria-describedby` pointing at the localized error, focus moves to the
field, and the reason is announced in the card's live region. The first non-blank keystroke clears
the invalid state (and `Escape` clears the draft).

## Accessibility contract

- **One live region.** The card owns exactly one `role="status" aria-live="polite" aria-atomic="true"`
  element; every announcement (decision, expiry, expiry warning, refused rejection, error, undo)
  goes through it. The `TmAlert` error is rendered with `role="group"` on purpose — the region is the
  only announcement channel, so nothing is announced twice.
- **Risk is words first.** The risk badge always carries localized text ("High risk"), the accent
  border is the second channel only (WCAG 1.4.1).
- **Naming.** The group is named with `aria-label` (`AriaLabel`, else `Title`, else a localized
  "Approval request") rather than `aria-labelledby`, because the labelled target could end up in the
  collapsed half — a dangling reference.
- **Focus management.** A decision collapses the half that held focus, so focus moves to the
  surviving Undo action, or to the card root (`tabindex="-1"`) when there is no Undo. Undo moves it
  back to Approve; a refused rejection moves it to the note field. Focus moves use
  `FocusAsync(preventScroll: true)` — no extra JS module.
- **Focus visible.** `:focus-visible` uses `--tm-focus-ring` (2 px offset-capable ring) and, under
  `forced-colors: active`, an explicit `outline: 2px solid Highlight` on every focus stop — the UA
  forces `box-shadow: none`, and the card deliberately opts out of nothing, so the system palette
  keeps painting.
- **Touch.** On `(pointer: coarse)` both actions and Undo grow to the shared `--tm-touch-target`
  (44 px) floor.
- **Reduced motion.** The collapse transition and the busy spinner are covered by the reduce block
  (`animations.css` enumerates `.tm-approval-card`, `.tm-approval-card__collapse` and
  `.tm-approval-card__spinner`).

## Collapse without JS

Both halves of the card (`__body` and `__summary`) stay mounted inside a grid wrapper
(`.tm-approval-card__collapse`) whose `grid-template-rows` animates from `1fr` to `0fr`. The
inactive half is `inert` and `visibility: hidden`, so it leaves the tab order and the accessibility
tree while the height still animates out. No JavaScript, no layout measurement, and the reduced
motion block turns the transition off.

## Parameters

| Parameter | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | `null` | Heading; also the group's accessible name. |
| `Description` | `string?` | `null` | What is being approved. |
| `Risk` | `ApprovalRisk` | `Low` | Localized badge + accent. |
| `PreviewContent` | `RenderFragment?` | `null` | Host preview of the subject. |
| `Details` | `IReadOnlyList<KeyValuePair<string,string>>?` | `null` | Rendered as a `<dl>`. |
| `ExpiresAt` | `DateTimeOffset?` | `null` | Deadline; drives the countdown and `Expired`. |
| `RequireNoteOnReject` | `bool` | `false` | Refuses a rejection without a note. |
| `AllowNote` | `bool` | `true` | Offers the optional note field. |
| `State` / `StateChanged` | `ApprovalState` / `EventCallback<ApprovalState>` | `Pending` | Controlled state. |
| `DecidedBy` / `DecidedAt` / `DecisionNote` | `string?` / `DateTimeOffset?` / `string?` | `null` | Collapsed-row content; falls back to the decision itself. |
| `OnDecision` | `Func<ApprovalDecision, Task>?` | `null` | Persists the decision. |
| `UndoWindow` | `TimeSpan?` | `null` | How long Undo is offered. |
| `OnUndo` / `Undone` | `Func<Task>?` / `EventCallback` | `null` | Undo handling. |
| `EnableShortcuts` | `bool` | `true` | Ctrl/Cmd shortcut pair. |
| `Compact` | `bool` | `false` | Single row: no description, preview or details. |
| `Class` / `AriaLabel` | `string?` | `null` | Extra classes / group name override. |

Public members: `ApproveAsync()`, `RejectAsync()`, `UndoAsync()`, `CanDecide`, `Announcement` (the
last value written to the live region).

## Models

```csharp
public enum ApprovalRisk { Low, Medium, High, Critical }
public enum ApprovalState { Pending, Approved, Rejected, Expired, Cancelled }

var decision = ApprovalDecision.Approve(decidedBy: "Jane Doe", decidedAt: DateTimeOffset.UtcNow);
// or: new ApprovalDecision(ApprovalState.Rejected, note: "Duplicate of INV-2");
// decision.IsApproved, decision.Note, decision.DecidedBy, decision.DecidedAt
```

`ApprovalDecision` refuses any state that is not `Approved` or `Rejected` with
`ArgumentOutOfRangeException` — a request that nobody decided is not a decision.

## CSS classes

Root `tm-approval-card` with the modifiers `--compact`, `--busy`, `--collapsed`, the state modifier
(`--pending`, `--approved`, `--rejected`, `--expired`, `--cancelled`) and the risk modifier
(`--risk-low`, `--risk-medium`, `--risk-high`, `--risk-critical`); parts `__header`, `__heading`,
`__title`, `__risk`, `__deadline` (+ `--expired`), `__deadline-label`, `__countdown`,
`__description`, `__preview`, `__details`, `__detail`, `__detail-term`, `__detail-value`, `__note`,
`__note-label`, `__note-input`, `__note-error`, `__alert`, `__actions`, `__action`, `__spinner`,
`__collapse`, `__body`, `__body-inner`, `__summary`, `__summary-inner`, `__summary-icon`,
`__summary-title`, `__summary-text`, `__summary-note`, `__undo`. Every style reads semantic
`--tm-*` tokens, so dark mode and a host's token overrides follow without a `[data-theme]` block.

## Test hooks

`data-testid` (prefixable through `TestIdPrefix`): `approval-card` (root, also carries
`data-state`), `approval-title`, `approval-risk`, `approval-description`, `approval-preview`,
`approval-details`, `approval-countdown`, `approval-note`, `approval-note-error`,
`approval-actions`, `approval-approve`, `approval-reject`, `approval-error`, `approval-summary-text`,
`approval-summary-note`, `approval-undo`, `approval-announcements`.

## Wireframe

The card is a placeable stencil: role `approval-card` (built-in vocabulary), category `Feedback`,
props `title`, `description`, `risk` (`low` / `medium` / `high` / `critical`) and `showCountdown`,
rendered declaratively (no native renderer). Contract test:
`tests/Tempo.Blazor.Tests/Wireframe/ApprovalCardStencilTests.cs`.

## Localization keys

`TmApprovalCard_AriaLabel`, `_RiskLow`, `_RiskMedium`, `_RiskHigh`, `_RiskCritical`, `_Approve`,
`_Reject`, `_NoteLabel`, `_NotePlaceholder`, `_NoteRequired`, `_ExpiresIn`, `_Expired`,
`_ExpiringSoon`, `_ExpiredAnnouncement`, `_ApprovedSummary`, `_RejectedSummary`,
`_CancelledSummary`, `_Undo`, `_UndoLabel`, `_UndoAnnouncement`, `_UnknownDecider`,
`_ApproveAnnouncement`, `_RejectAnnouncement`, `_Error`, `_DetailsLabel`.

## Demo and tests

The demo page is `/human-in-the-loop` (demo `HumanInTheLoopPage`): an invoice release with a half-hour
deadline, a 30-second Undo window and the keyboard path. Component contract:
`tests/Tempo.Blazor.Tests/Approvals/TmApprovalCardTests.cs`; demo contract:
`HumanInTheLoopDemoPageTests`; browser lane: `HumanInTheLoopE2ETests` (WASM).
