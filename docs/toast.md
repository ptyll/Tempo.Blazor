# Toasty — ToastService & TmToastContainer

Global toast notifications: the scoped `ToastService` (`Tempo.Blazor.Services`) collects
`ToastInstance` records, the layout-mounted `<TmToastContainer />`
(`Tempo.Blazor.Components.Feedback`) renders them with auto-dismiss timers, animated progress
bars, live-region announcements, actions, promise (loading→success/error) updates, a collapsed
deck presentation and swipe-to-dismiss. Demo: `/feedback` in the demo app.

Place exactly ONE container in the app layout:

```razor
<TmToastContainer Position="ToastPosition.TopRight" MaxVisible="5"
                  PauseOnHover="true" MinActionDuration="10000" />
```

## Classic helpers (unchanged)

```csharp
Toast.ShowSuccess("Record saved successfully!", "Success");
Toast.ShowError("Failed to delete record.", "Error");
Toast.ShowWarning("Your session will expire in 5 minutes.");
Toast.ShowInfo("New version available.");
Toast.Remove(id);   // dismiss one
Toast.Clear();      // dismiss all
```

## Options API: actions, Undo, custom content, dedupe key

```csharp
// General options; returns the toast id.
Toast.Show(new ToastOptions
{
    Message = "Item deleted",
    Severity = ToastSeverity.Info,
    Duration = 10_000,
    Action = new ToastAction { Label = "Undo", OnClick = OnUndoAsync },
    Content = builder => { /* RenderFragment below the message */ },
});

// Built-in Undo action (10 s default, label via the TmToast_Undo localization key).
Toast.ShowUndo("“Report.docx” deleted.", onUndo: RestoreAsync);

// Dedupe: a second Show with the same Key replaces the toast IN PLACE
// (same id, same queue position, timer restarts) — the "Saving…" → "Saved" flow.
Toast.Show(new ToastOptions { Message = $"Counted {n}×", Key = "my-counter" });
```

## Promise toasts

`PromiseAsync` runs an operation behind ONE toast: a sticky loading state (spinner, no progress
bar) followed by an in-place update to success or error. The original exception rethrows so the
caller's own error handling still runs; cancellation removes the loading toast silently.

```csharp
try
{
    await Toast.PromiseAsync(
        async ct => await SaveAsync(ct),
        new ToastPromiseOptions
        {
            LoadingMessage = "Saving…",
            SuccessMessage = _ => "Saved.",
            SuccessTitle = "Done",
            ErrorMessage = ex => $"Save failed: {ex.Message}",
            Key = "save-flow",          // a re-run replaces the pending toast
        });
}
catch (SaveException)
{
    // already surfaced through the error toast
}
```

### `ToastPromiseOptions`

| Option | Type | Default | Purpose |
|--------|------|---------|---------|
| `LoadingMessage` | `string` | `""` | Message of the sticky loading toast (spinner, no progress bar). |
| `LoadingTitle` | `string?` | `null` | Optional title of the loading toast. |
| `SuccessMessage` | `Func<object?, string>?` | `null` (keeps the loading message) | Success message builder; receives the operation result. |
| `SuccessTitle` | `string?` | `null` | Title set on the success update. |
| `SuccessDuration` | `int` (ms) | `5000` | Duration of the success phase; `<= 0` makes it sticky. |
| `ErrorMessage` | `Func<Exception, string>?` | `null` (keeps the loading message) | Error message builder; receives the thrown exception. |
| `ErrorTitle` | `string?` | `null` | Title set on the error update. |
| `ErrorDuration` | `int` (ms) | `8000` | Duration of the error phase; `<= 0` makes it sticky. |
| `Key` | `string?` | `null` | Dedupe key of the whole flow — a re-run with the same key replaces the pending toast in place instead of stacking a second one. |
| `Politeness` | `ToastPoliteness?` | `null` (derives from severity per phase — the error phase is assertive) | Live-region politeness applied to every phase. |

### Updating a toast in place

`Update` changes only the provided arguments of an existing toast; the auto-dismiss timer restarts
with the resulting duration (a fresh full reading time). No-op for an unknown id. An explicit
`politeness` overrides the instance value — severity changes do not re-derive it (the promise-toast
error flow relies on this).

```csharp
Toast.Update(id, message: "Saved.", title: "Done",
    severity: ToastSeverity.Success, duration: 5000,
    politeness: ToastPoliteness.Polite);
```

The promise flow is built on this primitive: loading → `Update(id, success…)` / `Update(id, error…)`
in place, so the toast never jumps in the stack.

## Pause & timers

- All auto-dismiss timers run on the DI-registered `TimeProvider` (fallback
  `TimeProvider.System`) — no JS interop, safe under every render mode; tests use
  `FakeTimeProvider`.
- `PauseOnHover="true"` (default): hovering or focusing a toast disarms its timer and freezes
  the progress animation (`tm-toast--paused`); leaving resumes with exactly the remaining time.
  A hidden document pauses every toast regardless.
- Toasts carrying an `Action` are floored to `MinActionDuration` (default 10 s) so the action
  stays reachable; sticky toasts (`Duration <= 0`) and action-less toasts are never floored.
- `Pause`/`Resume`/`PauseAll`/`ResumeAll`/`IsPaused` are public for host-driven pauses.

## Keyboard & gestures

- **Escape** (handled in `tm-toast.js`): dismisses the card holding focus, sets
  `e.tmEscapeHandled = true` so capture-phase consumers (dialogs, editors) do not double-handle,
  then returns focus to the element the card took it from.
- **Alt+T** focuses the newest action button (advertised via `aria-keyshortcuts="Alt+T"`,
  skipped while typing in a field).
- **Swipe-to-dismiss** (`tm-toast-swipe.js`, touch/pen only) rides the shared `pointer-drag.js`:
  a horizontal drag past 40 % of the card width (or a fast flick) dismisses; below the threshold
  the card animates back. Vertical intent is vetoed so `touch-action: pan-y` scrolling stays
  native. The close button and Escape remain the always-available alternative (WCAG 2.5.7).

## Live regions & politeness

The container owns two persistent screen-reader-only regions (a polite `role="status"` and an
assertive `role="alert"`); the cards themselves carry no live role. Announcements are
title+message, deduped per toast id so a Key replacement announces exactly once per text change.
Politeness derives from severity at creation time — **Warning and Error are assertive**, Info and
Success polite — overridable per toast through `ToastOptions.Politeness`.

## Positions & deck

`Position` supports all six corners/centres (`TopRight` default, `TopLeft`, `TopCenter`,
`BottomRight`, `BottomLeft`, `BottomCenter` — full-width without the centering offset under
640 px). `Stacked="true"` renders the collapsed deck: the newest three cards stay fully visible,
every older card collapses to a thin strip (a pure-CSS depth hint); hovering the container or
moving focus inside it expands the deck. Every toast stays in the DOM.

The container rides the browser top layer (`popover="manual"`) and re-raises itself on each
push, so a toast pushed while a sheet or dialog is open always paints above it.

## Container parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Position` | `ToastPosition` | `TopRight` | Screen position. |
| `MaxVisible` | `int` | `5` | Max cards rendered at once. |
| `PauseOnHover` | `bool` | `true` | Pause timers on hover/focus. |
| `MinActionDuration` | `int` | `10000` | Duration floor for action toasts (pushed to the service). |
| `Stacked` | `bool` | `false` | Collapsed-deck presentation. |

## CSS contract (frozen)

`tm-toast-container` (+ `--top-left` / `--top-center` / `--top-right` / `--bottom-left` /
`--bottom-center` / `--bottom-right` / `--stacked`), `tm-toast` (+ `--success` / `--info` /
`--warning` / `--error` / `--paused` / `--loading` / `--deck-collapsed` / `--swipe-return`),
`tm-toast-body`, `tm-toast-icon`, `tm-toast-spinner`, `tm-toast-content`, `tm-toast-title`,
`tm-toast-message`, `tm-toast-custom`, `tm-toast-action`, `tm-toast-dismiss`, `tm-toast-progress`,
`tm-toast-progress-bar`.
