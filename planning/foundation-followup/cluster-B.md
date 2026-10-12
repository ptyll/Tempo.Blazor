# Cluster B: overlays, top layer, focus, menus and palette accessibility

**Read-only analysis.** Repo is at main `846bc242`; the tree is clean apart from untracked `tmp/`. No source changes or commits were made.

**Bindings honoured:** F2-SHEET-PRIMITIVE, F3-PANEL-PRESENTATION, F6-SHELL-FOCUS-RESTORE-AND-API and F10-OVERLAY-TOAST-RERAISE.

## How the facts were established

- **Unit and node tests.** `npm run test:overlay`: 325/325 pass. Targeted bUnit run (`TmCommandPaletteTests|TmGanttImportDialogTests|TmDialogPrompt*|TmLightbox*|TmKeyboardShortcutsHelp*|OverlayScrollRegionWiringTests`): 65/65 pass.
- **Live probes.** I used throwaway Playwright scripts in this folder (`probe-b.mjs`, `probe-modal.mjs`, `probe-toast.mjs`, `probe-delay*.mjs`, `probe-click.mjs`, `topmost-probe.mjs`).
  - They run against the published WASM build `tmp/f10-hosts/wasm/wwwroot` (built 14:13 today), served through a Playwright route.
  - The shared host on 7106 (pid 49420, not mine) serves JS modules with an empty MIME type, so I did not use it and did not touch it.
  - Every commit after 14:13 touches only docs, `overlay.js` (the toast re-raise) or E2E tests, so the probed behaviour equals HEAD.
- **Package inventory.** A sub-agent produced `cluster-B-packages.md`, which has the full per-package tables. Item 0e390ce4 below summarises it.

---

## 1. [c51ddf81 #1] Activate-after-promote focus race: shared helper + repeated-open E2E

**(1) Status: PARTLY DONE, and the remaining risk is latent.**

`TmGanttImportDialog` (commit `66ea3b8c`) and `TmOverlayPanel TrapFocus` (`TmOverlayPanel.razor` L87–98 and L521–528) activate after promotion. These still render `TmFocusScope Active="true"` from the first open render, before `TopLayerInterop.PromoteAsync` runs:

| Component | Scope (Active=true) | Promote |
|---|---|---|
| `TmLightbox.razor` | L14–20 (L17) | L118–132 |
| `TmCommandPalette.razor` | L15–20 (L17) | L267–282 |
| `TmKeyboardShortcutsHelp.razor` | L15–20 | L119–133 |
| `TmModal.razor` | L29–37 (L30) | L293–302 |
| `TmDialog.razor` | L29–38 (L30) | L363–372 |
| `TmDrawer.razor` (modal) | L22–34 (L23) | L349–361 |

The drawer also hosts every `TmOverlayPanel` sheet.

**Why no focus loss shows today.** The HTML UA rule `[popover]:not(:popover-open){display:none}` has zero specificity. Each of these roots declares its own `display` (`_lightbox.css` L118–123 `display:flex`, `_command-palette.css` L1–11, `_keyboard-shortcuts.css` L2–11, `_modal.css` L6–19, `_drawer.css` L15–21), and author CSS beats the UA. So a closed-but-mounted root is visible, and the trap's `focus()` lands. This matches the F2 decision text: "a closed-but-mounted popover root is NOT hidden by the UA".

The Gantt race was real because its `::deep` CSS was dead until `7ea5eaf0` (23:24), 34 minutes before the focus fix `66ea3b8c`.

**Probe evidence:**

| Probe | Result |
|---|---|
| Natural, 6 repeated opens at 1440 and 390, plus 3 fresh first opens at 390 | Palette → input, shortcuts → close button, lightbox → close button, modal → close button, dialog → Cancel, sheet → close button. **100%**. |
| `tm-top-layer.js` delayed by 1.5 s (`probe-delay3.mjs`) | Still correct; the root is not yet `:popover-open` but is visible. |
| Same delay plus injected `[popover]:not(:popover-open){display:none!important}`, i.e. a root without its own display, as a consumer `Class` override or a future CSS edit could cause (`probe-delay4.mjs`) | Palette and shortcuts: **focus = `<body>`** before and after promotion. **Gantt control**: correct. |

**Conclusion.** Correctness hinges on every root keeping an author `display`. Fix it structurally as defence in depth, with the shared helper below.

**(2) Current pattern: the reference.** `TmGanttImportDialog.razor.cs` L88–107: promote, then `_scopeActive = true; StateHasChanged();`, with markup `Active="_scopeActive"` at `.razor` L14.

**(3) Change.**

**A. New file `src/Tempo.Blazor/Helpers/PromotedScopeGate.cs`** (internal):

```csharp
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Tempo.Blazor.Helpers;

/// <summary>
/// Promotes a modal overlay root to the top layer and only THEN reports the focus scope active, so the
/// trap's initial-focus move can never run against a popover root that is still display:none
/// (F3 follow-up c51ddf81 #1; the TmGanttImportDialog / TmOverlayPanel.TrapFocus pattern, shared).
/// </summary>
internal sealed class PromotedScopeGate : IAsyncDisposable
{
    private readonly TopLayerInterop _topLayer;
    private bool _promoted;
    private int _generation;

    public PromotedScopeGate(IJSRuntime js) => _topLayer = new TopLayerInterop(js);

    /// <summary>Bind TmFocusScope.Active to this.</summary>
    public bool ScopeActive { get; private set; }

    /// <summary>
    /// Call from OnAfterRenderAsync on EVERY render. Returns true when ScopeActive just turned true;
    /// the caller must then call StateHasChanged().
    /// </summary>
    /// <param name="isOpen">The component's open flag as it was for this render.</param>
    /// <param name="stillOpen">Re-reads the open flag after the promote await (the c51ddf81 #2 guard).</param>
    /// <param name="root">The popover="manual" root; null/unset Id while it is not rendered yet.</param>
    public async Task<bool> AfterRenderAsync(bool isOpen, Func<bool> stillOpen, ElementReference? root)
    {
        if (!isOpen)
        {
            if (_promoted || ScopeActive) { _promoted = false; ScopeActive = false; _generation++; }
            return false;
        }
        if (_promoted || root is null || root.Value.Id is null) return false;
        _promoted = true;
        var generation = ++_generation;
        await _topLayer.PromoteAsync(root.Value);      // guarded: a no-op where JS/Popover API is missing
        if (generation != _generation || !stillOpen()) return false;   // closed during the await
        if (ScopeActive) return false;
        ScopeActive = true;
        return true;
    }

    public ValueTask DisposeAsync() => _topLayer.DisposeAsync();
}
```

**B. Per-component changes.** In every component below:

- Add a field `private PromotedScopeGate? _gate;`.
- Bind the scope's `Active` to `_gate?.ScopeActive == true`.
- Replace the promote block with:
  `_gate ??= new PromotedScopeGate(JS); if (await _gate.AfterRenderAsync(<open>, () => <open>, <root>)) StateHasChanged();`
- In `DisposeAsync`, replace `_topLayer.DisposeAsync()` with `if (_gate is not null) await _gate.DisposeAsync();`.
- Delete the `_promoted`/`_topLayer` fields that become unused.

| Component | `Active=` line | Promote block to replace | open | root |
|---|---|---|---|---|
| `Gallery/TmLightbox.razor` | L17 | L118–131 | `IsOpen` | `_scope?.Element` |
| `Layout/TmCommandPalette.razor` | L17 | L272–281 | `IsOpen` | `_backdrop` |
| `Layout/TmKeyboardShortcutsHelp.razor` | L15 | L123–132 | `IsVisible` | `_root` |
| `Feedback/TmModal.razor` | L30 | L293–302 | `Show` | `_overlayElement` |
| `Feedback/TmDialog.razor` | L30 | L363–372 | `Show` | `_overlayElement` |
| `Layout/TmDrawer.razor` | L23 → `Active="@(!Modal \|\| _gate?.ScopeActive == true)"` | L349–361 (keep `Modal` in the condition: `IsOpen && Modal`) | `IsOpen && Modal` | `_scope?.Element` |
| `Scheduler/TmGanttImportDialog.razor(.cs)` | `.razor` L14 → `_gate?.ScopeActive == true` | `.cs` L88–107; drop `_promoted`, `_scopeActive`, `_topLayer` | `IsOpen` | `_overlay` |

The drawer's inline (non-modal) sheet has no popover, so it stays active immediately.

> **Coupling, mandatory.** Item 3d (scroll region) must land in the same phase or earlier. With the gate, `TmDialog`/`TmModal`/`TmDrawer` attach `syncScrollRegion` before the trap activates. An overflowing body then always carries `tabindex=0` when `activate()` resolves "first focusable", and for `TmDialog Custom` the scroller `div.tm-dialog-content` precedes its children. That makes the F2 #4 race deterministic instead of occasional.

**(4) RED tests.**

- **bUnit, one per component**, named `<Component>_FocusTrap_ActivatesOnlyAfterPromotion`, in the existing test file for each (`Components/Layout/TmCommandPaletteTests.cs`, `…/TmKeyboardShortcutsHelpTests.cs`, `Gallery/TmLightbox*Tests`, `Feedback/TmModal*Tests`, `Feedback/TmDialog*Tests`, `Layout/TmDrawer*Tests`). Steps:
  1. `var top = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-top-layer.js"); var promote = top.SetupVoid("promote", _ => true);` with no result, so the call stays pending.
  2. `var trap = JSInterop.SetupModule("./_content/Tempo.Blazor/js/tm-focus-trap.js"); trap.SetupVoid("activate", _ => true).SetVoidResult();`
  3. Render open, then assert `trap.Invocations["activate"]` is empty.
  4. `promote.SetVoidResult();` then `cut.WaitForAssertion(() => trap.Invocations["activate"].Should().HaveCount(1))`.
- **Gantt (#2), `TmGanttImportDialogTests.ImportDialog_ClosedDuringPromote_ReopenActivatesOnlyAfterTheNextPromote`:**
  1. Render open with pending `promote`.
  2. `SetParametersAndRender(IsOpen=false)`, then `promote.SetVoidResult()`.
  3. Re-open with a new pending promote.
  4. Assert `activate` is not called until the second promote completes.
- **E2E, new `tests/Tempo.Blazor.E2E/Feedback/OverlayInitialFocusE2ETests.cs`** (`[TestCategory("WASM")]`). DataRows of `(route, opener, root, expectedFocus)`:
  - palette: `/layout`, `GetByRole(Button,"Open Command Palette",Exact)`, `.tm-command-palette-backdrop`, `.tm-command-palette-input`
  - shortcuts: `/layout`, testid `shortcuts-described-open`, `.tm-keyboard-shortcuts-overlay`, `.tm-keyboard-shortcuts-close`
  - lightbox: `/gallery`, `.tm-gallery-item >> nth=0`, `.tm-lightbox`, `.tm-lightbox-close`
  - modal: `/modal-dialog`, testid `open-basic-modal`, `.tm-modal-overlay`, `.tm-modal-close`
  - dialog: `/modal-dialog`, testid `open-long-dialog`, `.tm-modal-overlay`, `.tm-dialog-btn-cancel` (observed as `button.tm-btn.tm-btn-ghost`)
  - drawer: `/feedback`, testid `open-right-drawer`, `.tm-drawer--right`, `.tm-drawer__close`
  - gantt (control): `/gantt`, `button[data-testid='gantt-import-btn']`, `.tm-gantt__dialog-overlay`, `.tm-gantt__dialog-close`

  Two methods:
  - **`InitialFocus_LandsInside_OnSixRepeatedOpens(…)` at 1440×900 and 390×844 touch:** open, poll up to 1 s until `document.activeElement.matches(expected)`, assert, press Escape, wait for the root to be `Detached` or hidden. Green today; this guards against regression.
  - **`InitialFocus_SurvivesASlowPromote_WhenTheRootHasNoOwnDisplay(…)` at 1440:**
    - `context.RouteAsync("**/_content/Tempo.Blazor/js/tm-top-layer.js", async r => { await Task.Delay(800); await r.FallbackAsync(); })`;
    - after app-ready, `page.AddStyleTagAsync(new(){ Content="[popover]:not(:popover-open){display:none !important}" })`;
    - open once, wait 2 s, assert focus matches `expected`.
    - **RED today for palette and shortcuts (verified).** Expected RED for lightbox, modal, dialog and drawer, which use the same `Active=true` pattern; confirm at the RED step. Gantt is the green control.

**(5) Verification.**

```bash
dotnet test tests/Tempo.Blazor.Tests --filter "FullyQualifiedName~ActivatesOnlyAfterPromotion|FullyQualifiedName~TmGanttImportDialogTests"
dotnet test tests/Tempo.Blazor.E2E --filter "FullyQualifiedName~OverlayInitialFocusE2ETests|FullyQualifiedName~GanttDialogOverlayE2ETests|FullyQualifiedName~OverlayRound3FixE2ETests"
```

Run the full bUnit suite as well: role and `aria-modal` now appear one render later. Loose JS interop completes the promote synchronously, so the Gantt tests already prove the pattern. Wrap any failing `role="dialog"` assertion in `WaitForAssertion`.

**(6) Breaking / CHANGELOG.** Not breaking, and the markup is identical once open. Add a CHANGELOG "Fixed" line under `## 2.9.1 (unreleased)`.

**(7) Effort.** M.

**(8) Open questions.** None.

**(9) Scope.** Core follow-up plan.

---

## 2. [c51ddf81 #2] `TmGanttImportDialog` `IsOpen` guard after the await

**(1) Status: OPEN.** `TmGanttImportDialog.razor.cs` L92–101:

```csharp
await _topLayer.PromoteAsync(_overlay);
...
_scopeActive = true;
StateHasChanged();
```

There is no re-check after the await. If the dialog closes during the await, the stale `_scopeActive=true` makes the next open activate before promotion.

**(3) Change.** Solved by migrating onto `PromotedScopeGate` (item 1: `stillOpen()` plus the generation check). Done standalone, it is one line after L92: `if (!IsOpen) { _promoted = false; return; }`.

**(4) Test.** See item 1, Gantt bUnit test.

**(5) Verification.** `dotnet test tests/Tempo.Blazor.Tests --filter FullyQualifiedName~TmGanttImportDialogTests`

**(6) Breaking / CHANGELOG.** Not breaking; no CHANGELOG entry (internal).

**(7) Effort.** S.

**(9) Scope.** Core, same phase as item 1.

---

## 3. [c51ddf81 #4] Focus after dismissing a toast under a modal trap

**(1) Status: OPEN.** Reproduced with `probe-toast.mjs` on `/feedback` at 1440:
1. Open the bottom sheet, focus "push toast" and press Enter.
2. Focus the toast's dismiss button and press Enter.
3. Result: `document.activeElement = <body>`. The next Tab lands on `tm-sheet__handle`; the trap's last element is lost.

The same happens on a mouse dismiss, because the clicked button is removed.

**(2) Current state.**
- **`TmToastContainer.razor`:**
  - `Dismiss` (L125–128) only calls `ToastService.Remove(id)`.
  - `OnAfterRenderAsync` (L83–123) re-raises or demotes, but never touches focus.
  - The container carries `data-tm-inert-exempt` (L13), so it is focusable under the trap.
- **`TmNotificationToastContainer.razor`:** `RemoveToast` L107–116, `OnAfterRenderAsync` L119–154.
- **`tm-focus-trap.js`:** keeps no record of the last element focused inside a trap (`traps` entry L89: `{ element, tabHandler, escHandler, returnTarget, restoreTarget, modal }`).

**(3) Change.** Follow the F6 `focusIfLost` philosophy: never steal, only recover lost focus.

1. **`tm-focus-trap.js` `activate()`:** after L65, add a `focusin` listener on `element` that records `lastFocused = event.target`, unless the target is `element` itself. Store `lastFocused` and `focusInHandler` in the `traps` entry (L89), and remove the listener in `deactivate()` (L279–281).
2. **New export in `tm-focus-trap.js`:**

   ```js
   /**
    * Recovers focus for the topmost MODAL trap after a surface outside it (a toast) removed the
    * focused element. Acts only when focus is lost (body or a detached element): returns focus to
    * the element last focused inside the trap, else re-runs the trap's initial-focus resolution.
    * @returns {boolean} whether focus moved
    */
   export function restoreTrapFocusIfLost() {
       const active = document.activeElement;
       if (active && active !== document.body && active.isConnected !== false) return false;
       const modal = Array.from(traps.values()).filter(t => t.modal);
       if (modal.length === 0) return false;
       const top = modal[modal.length - 1];
       if (top.lastFocused && top.lastFocused.isConnected !== false && top.element.contains(top.lastFocused)
           && focusOf(top.lastFocused)) return true;
       const list = visibleFocusable(top.element);
       return focusOf(list[0] || top.element);
   }
   ```

3. **`FocusIfLostInterop.cs`:** add `public async Task<bool> RestoreTrapFocusIfLostAsync()`, with the same guarded pattern as `FocusIfLostAsync` (L26–41), calling `"restoreTrapFocusIfLost"`.
4. **`TmToastContainer.razor`:**
   - add `private int _lastCount; private FocusIfLostInterop? _focus;` (inject `JS`, already present);
   - at the end of `OnAfterRenderAsync`: `if (count < _lastCount) { _focus ??= new FocusIfLostInterop(JS); await _focus.RestoreTrapFocusIfLostAsync(); } _lastCount = count;`
   - dispose `_focus` in `DisposeAsync`.

   This covers a click dismiss, a keyboard dismiss and a timer expiry while focused.
5. **`TmNotificationToastContainer.razor`:** the same, keyed on `_toasts.Count`.

**(4) RED tests.**
- **Node tests in `__tests__/tm-focus-trap.test.mjs`:**
  - `restoreTrapFocusIfLost returns focus to the element last focused inside the topmost modal trap`: activate a modal, `dialog.dispatch('focusin', { target: input })`, set `document.activeElement = body`, call it, and assert `input.focusCalls` incremented and the result is `true`.
  - `restoreTrapFocusIfLost never steals focus from a live element`: active is a connected element; assert the result is `false` and nothing is focused.
  - `restoreTrapFocusIfLost falls back to the first focusable when the last-focused element left the DOM`.
  - `restoreTrapFocusIfLost is a no-op without a modal trap` (only a non-modal one).
- **bUnit, `Feedback/TmToastTests.cs` `ToastContainer_RemovingAToast_AsksTheTrapToRecoverLostFocus`:** set up the `tm-focus-trap.js` module with `restoreTrapFocusIfLost` returning `true`, push then remove a toast, and verify one invocation. Pushing must not invoke it.
- **E2E, `OverlayRound3FixE2ETests.ToastDismiss_ByKeyboard_UnderOpenSheet_ReturnsFocusToTheSheet`** (1440 and 390 touch):
  1. Open `[data-testid='open-bottom-sheet']`.
  2. Focus `[data-testid='sheet-push-toast']` and press Enter.
  3. Focus `.tm-toast-container:popover-open .tm-toast-dismiss` (last) and press Enter.
  4. Assert `document.activeElement` has `data-testid == 'sheet-push-toast'`.
  5. Add a mouse-dismiss variant with the same assertion.

**(5) Verification.**

```bash
npm run test:overlay
dotnet test tests/Tempo.Blazor.Tests --filter FullyQualifiedName~TmToastTests
dotnet test tests/Tempo.Blazor.E2E --filter FullyQualifiedName~OverlayRound3FixE2ETests
```

**(6) Breaking / CHANGELOG.** Not breaking. Add a "Fixed" entry.

**(7) Effort.** S–M.

**(8) Open questions.** Without any modal trap, a dismissed toast leaves focus on `<body>`, as it does today. I recommend accepting that: there is no reliable return target.

**(9) Scope.** Core.

---

## 4. [c51ddf81 #5a] Command palette: backdrop click should close it

**(1) Status: OPEN.** Probe: clicking (20, 880) on the backdrop at 1440 leaves the palette open (`paletteOpenAfterBackdropClick: 1`).

**(2) Current state.** `TmCommandPalette.razor` L14: `<div class="tm-command-palette-backdrop @Class" popover="manual" data-tm-backdrop @ref="_backdrop" @attributes="AdditionalAttributes">` has no `@onclick`. The scope at L15–20 has no stopPropagation.

**Reference pattern (verified working).** `TmKeyboardShortcutsHelp.razor` L14 `@onclick="OnClose"` on the overlay plus L20 `@onclick:stopPropagation` on the `TmFocusScope`. `probe-click.mjs` confirms that a click inside does not close it and a backdrop click does.

**(3) Change.**
1. L14: add `@onclick="CloseAsync"`. Put it before `@attributes`, so a consumer `onclick` in `AdditionalAttributes` still wins, matching today's precedence.
2. L15–20: add `@onclick:stopPropagation` to the `TmFocusScope`.

**(4) RED tests.**
- **bUnit, `TmCommandPaletteTests`:**
  - `TmCommandPalette_BackdropClick_Closes`: `cut.Find(".tm-command-palette-backdrop").Click()`, then `isOpen` is false.
  - `TmCommandPalette_ClickInsidePanel_DoesNotClose`: assert the scope root renders `blazor:onclick:stopPropagation`. In bUnit, check that clicking `.tm-command-palette-header` does not invoke `IsOpenChanged`; bUnit honours stopPropagation in event bubbling.
- **E2E, `AdditiveExtensionsE2ETests.Palette_BackdropClick_Closes_AndPanelClickDoesNot`:** on `/layout` at 1280, open, click inside the input (still open), `page.Mouse.ClickAsync(10, 700)`, then assert the `.tm-command-palette-backdrop` count is 0.

**(5) Verification.** `dotnet test … --filter FullyQualifiedName~TmCommandPaletteTests`, plus the E2E above.

**(6) Breaking / CHANGELOG.** This is a behaviour change: an outside click now closes the palette. Add a CHANGELOG "Changed" entry.

**(7) Effort.** S.

**(8) Open question.** Should there be a `CloseOnBackdropClick` parameter, default `true`? I recommend no parameter, matching `TmModal.CloseOnOverlayClick=true` and `TmKeyboardShortcutsHelp`. One residual risk: a text selection drag that starts in the input and ends over the backdrop fires a click on the common ancestor and closes the palette. `TmModal` has the same risk today. A pointerdown-origin guard is optional.

**(9) Scope.** Core.

---

## 5. [c51ddf81 #5b] + [7800d54b] Lightbox Escape focus restore

**(1) Status: OPEN.** Probe after 6× open + Escape: `activeElement = <body>`. The current E2E only asserts that focus is "not inside the lightbox" (`GalleryLightboxE2ETests.cs` L96–101).

**Root cause.** `TmImageGallery.razor` L22 `<div class="tm-gallery-item" @onclick=…>` is not focusable. At `activate()`, `document.activeElement` is `<body>`, so the trap's `returnTarget` (`tm-focus-trap.js` L45) is the body. No gallery or lightbox component plan exists in the plan list.

**(3) Change.**
1. **`TmImageGallery.razor` L22–30:** change the `div` to `<button type="button" class="tm-gallery-item" id="@ItemId(i)" aria-label="@(image.Title ?? Loc["TmImageGallery_OpenImage"])" @onclick=…>`, with the same children.
   - Add `private readonly string _uid = Guid.NewGuid().ToString("N"); private string ItemId(int i) => $"tm-gallery-{_uid}-item-{i}";`.
   - Per the keyboard convention, a native button needs no key handler.
   - Add the localization key `TmImageGallery_OpenImage` (en "Open image", cs "Otevřít obrázek", fr "Ouvrir l'image") to the three JSONs and to `MockTmLocalizer`. Do this only if no existing key fits.
2. **`_image-gallery.css` L13 `.tm-gallery-item {`:** add the button reset (`padding:0; background:none; font:inherit; color:inherit; text-align:inherit; display:block; width:100%;`), plus `.tm-gallery-item:focus-visible { outline:none; box-shadow: var(--tm-focus-ring); }`.
3. **`TmLightbox.razor`:** add a public parameter
   `/// <summary>Id of the element focus returns to when the lightbox closes (e.g. the gallery item of the current image). Null restores the opener.</summary> [Parameter] public string? RestoreFocusTargetId { get; set; }`
   and pass it to the scope at L14–20 (`RestoreFocusTargetId="@RestoreFocusTargetId"`).
4. **`TmImageGallery.razor` L34–45:** pass `RestoreFocusTargetId="@(_lightboxIndex is int ix ? ItemId(ix) : null)"`, so Next/Prev and then Escape return to the item of the image being shown. The trap resolves the id at deactivation (`tm-focus-trap.js` L380–385).
5. **Regenerate the JSON docs bundle**, because a new `[Parameter]` was added (AGENTS.md "Component JSON Documentation").

**(4) RED tests.**
- **bUnit (`Gallery` tests):**
  - `ImageGallery_Items_AreNativeButtons_WithAccessibleName`: `FindAll("button.tm-gallery-item")` count equals the image count, and each has an `aria-label`.
  - `Lightbox_PassesRestoreFocusTargetId_ToTheScope`: `data-restore-target` on `.tm-lightbox` equals the item id.
- **E2E, `GalleryLightboxE2ETests`:**
  - Replace L96–101 with: `items.First.FocusAsync(); Keyboard.Press("Enter")`, then Escape, then assert `document.activeElement === items[0]`.
  - Add `Gallery_Lightbox_NextThenEscape_RestoresFocusToTheShownImage`: open item 0 with Enter, ArrowRight, Escape, then assert focus is on item 1.

**(5) Verification.** `dotnet test … --filter "FullyQualifiedName~Gallery|FullyQualifiedName~Lightbox"`, the E2E `GalleryLightboxE2ETests`, and `dotnet test --filter ComponentDocumentationFreshnessTests` after regeneration.

**(6) Breaking / CHANGELOG.** Gallery items change from `div` to `button`, a markup change that affects consumer CSS and selectors keyed on `div.tm-gallery-item`. Add "Changed" and "Added: `TmLightbox.RestoreFocusTargetId`" entries.

**(7) Effort.** S–M.

**(8) Open question.** Should the new public parameter be added at all? I recommend yes; it is additive and mirrors `TmModal.RestoreFocusTargetId`. Without it, the button change alone fixes the Escape case in Chromium. Safari does not focus buttons on click, so the opener stays `<body>` there.

**(9) Scope.** Core follow-up. There is no gallery plan. The visual items `b32847d0` and `d49c9776` stay for a future gallery plan.

---

## 6. [08a22db5 #2] `tm-focus-trap.js`: a modal Escape gate must skip non-modal traps

**(1) Status: OPEN, reproduced.** `topmost-probe.mjs`: activate a modal dialog, then a non-modal sheet (not containing focus), focus inside the dialog, press Escape. Result: `{"isTopmostDialog":false,"calls":[]}`. Nothing closes.

**(2) Current code.**
- **The modal branch of `escHandler` (L74–77):**

  ```js
  if (modal) {
      // Only the topmost modal closes. A dialog over a dialog must not close both.
      if (!isTopmost(id)) return;
  }
  ```

- **`isTopmost` (L199–203)** counts all traps:

  ```js
  export function isTopmost(id) {
      const ids = Array.from(traps.keys());
      return ids.length > 0 && ids[ids.length - 1] === id;
  }
  ```

- **`ownsEscape` (L187–197)** handles non-modal traps: it returns false if a modal is registered after the trap.

**(3) Change.** Keep the exported `isTopmost` unchanged; it is tested at L140. Add a new function after L203:

```js
/**
 * Whether a MODAL trap owns Escape: no modal trap registered after it, and no later NON-modal trap
 * that currently holds focus (an inline sheet nested in the dialog closes first, one layer per key).
 * A later non-modal trap that does not hold focus (a layout flip mounting an inline sheet behind the
 * dialog) is skipped (F6 follow-up 08a22db5 #2).
 */
export function modalOwnsEscape(id) {
    const ids = Array.from(traps.keys());
    const at = ids.indexOf(id);
    if (at < 0) return false;
    for (let i = at + 1; i < ids.length; i++) {
        const other = traps.get(ids[i]);
        if (other.modal) return false;
        if (ownsEscape(ids[i], other.element)) return false;
    }
    return true;
}
```

Then change L76 to `if (!modalOwnsEscape(id)) return;`.

**(4) RED tests.** In `__tests__/tm-focus-trap.test.mjs`, after L849, using the existing `element`/`installDom`/`handler`/`escapeEvent` helpers:
- `a non-modal trap activated AFTER an open modal does not block the modal's Escape`: dialog (modal) then sheet (non-modal sibling), `document.activeElement = insideDialog`, Escape. Assert `dialogEscape.calls` is `['HandleFocusTrapEscapeAsync']` and `sheetEscape.calls` is `[]`. **RED today.**
- `an inline sheet nested in a modal dialog that holds focus closes alone`: dialog (modal), sheet = element(dialog) (non-modal), focus inside the sheet. Assert only the sheet's handler fires.
- `two modals plus a later non-modal: only the second modal closes`.

Also import `modalOwnsEscape` at L5–7.

**(5) Verification.** `npm run test:overlay`, plus the E2E regression `EditorShellE2ETests` and `SheetRound*E2ETests`, which use inline sheets.

**(6) Breaking / CHANGELOG.** Not breaking. Add a "Fixed" entry.

**(7) Effort.** S.

**(9) Scope.** Core.

---

## 7. [f4c27b20 #1] Phone bottom-sheet menus: Tab from the first item escapes to `<body>`

**(1) Status: OPEN, root cause proven.** `probe-b.mjs sheet` on `/toolbar-forms` at 390 touch, More menu opened with Enter:
- Item tabindexes: `["0","-1","-1","-1","-1"]` (roving, from `tm-menu-nav.js` `rove()` L97–105).
- The trap's focusable list: `Done, item0(ti=0), item1..4(ti=-1)`.
- Tab sequence: `item0 → <body> → Done → item0 → <body>`.
- Shift+Tab works: `item0 → Done → item(last) → Done`.

The same `ActionOverflowMenu` (`Actions/ActionOverflowMenu.cs` L156–194) serves `/mobile-action-bar` More, and `TmDropdown` sheets behave the same way.

**Mechanism.**
1. `tm-focus-trap.js` `FOCUSABLE` (L13–18) contains `button:not([disabled])`, so `visibleFocusable` (L30–33) includes the `tabindex="-1"` items.
2. The trap's `last` is item4. Tab on item0 is "not last", so the handler does not `preventDefault` (L60–63).
3. The browser's native Tab skips the `-1` items, finds no further tabbable element (the page is inert), and focus leaves to `<body>`.
4. The trap's keydown listener sits on the trap element (L65), so the next Tab from `<body>` is not seen. The browser picks Done.

**(3) Change in `tm-focus-trap.js`.**
1. After L33, add:

   ```js
   /** Tab stops only: a focusable with a negative tabindex (a roving menu/toolbar item) is not one. */
   function tabbable(element) {
       return visibleFocusable(element).filter(el => {
           const ti = typeof el.getAttribute === 'function' ? el.getAttribute('tabindex') : null;
           return ti === null || ti === undefined || Number(ti) >= 0;
       });
   }
   ```

2. In `tabHandler`, L52: change `const list = visibleFocusable(element);` to `const list = tabbable(element);`.
3. **Optional hardening, recommended.** If `active` is inside the element but not in `list` (focus on a `-1` item before `focusin` re-roved), `preventDefault` and focus the next or previous list member in DOM order:

   ```js
   const at = list.indexOf(active);
   if (at < 0 && element.contains(active) && active !== element && typeof active.compareDocumentPosition === 'function') {
       e.preventDefault();
       const following = list.filter(el => active.compareDocumentPosition(el) & 4);
       const preceding = list.filter(el => active.compareDocumentPosition(el) & 2);
       (e.shiftKey ? (preceding[preceding.length - 1] || last) : (following[0] || first)).focus();
       return;
   }
   ```

   Insert it before the existing `if (e.shiftKey …)` at L57.

The initial-focus fallback (L118/L143, `list[0]`) stays on `visibleFocusable`. Menu sheets choose their first item through `InitialFocusSelector`.

This fixes every modal trap containing a roving group: menus in sheets, and `TmToolbar` (`tm-toolbar.js` roving) inside `TmModal`/`TmDrawer`.

**(4) RED tests.**
- **Node, `__tests__/tm-focus-trap.test.mjs`:** `Tab from the last TAB STOP wraps to the first: tabindex=-1 roving items are not tab stops`:

  ```js
  const body = element(); const sheet = element(body);
  const done = element(sheet); const item0 = element(sheet); const item1 = element(sheet);
  item0.getAttribute = n => (n === 'tabindex' ? '0' : null);
  item1.getAttribute = n => (n === 'tabindex' ? '-1' : null);
  sheet.querySelectorAll = () => [done, item0, item1];
  installDom(body);
  activate(sheet, 'menu-sheet', null, false, null, true);
  done.focusCalls = 0; document.activeElement = item0;
  const tab = { key: 'Tab', shiftKey: false, preventDefault() { tab.prevented = true; } };
  sheet.dispatch('keydown', tab);
  assert.equal(tab.prevented, true); assert.equal(done.focusCalls, 1);
  ```

  Plus a `Shift+Tab from the first tab stop wraps to the last TAB STOP, not to a -1 item` test, and, if you take the hardening, `Tab from a focused -1 item moves to the next tab stop in DOM order` (stub `compareDocumentPosition`).
- **E2E:**
  - **`ToolbarOverflowE2ETests.Phone_390_MoreSheet_TabAndShiftTab_StayInsideTheSheet`:** use `OpenAsync(390,844,touch:true)` (L37) and `SettleAsync(page,"toolbar-overflow-300")`. Focus `button.tm-toolbar-more` and press Enter. Wait for `.tm-overlay-panel-sheet`, and wait for focus on `[role=menuitem]`. Then press Tab ×4, asserting after each press that `document.activeElement.closest('.tm-overlay-panel-sheet')` is non-null. Then press Shift+Tab ×4 with the same assertion.
  - **`MobileActionBarE2ETests.Demo_390_MoreSheet_TabStaysInsideTheSheet`:** the same on `/mobile-action-bar`, with `[data-testid='mab-auto-bar'] .tm-mobile-action-bar__more` (see L108–155 for setup).

**(5) Verification.** `npm run test:overlay`, then `dotnet test tests/Tempo.Blazor.E2E --filter "FullyQualifiedName~ToolbarOverflowE2ETests|FullyQualifiedName~MobileActionBarE2ETests|FullyQualifiedName~Sheet"`. Note `058c3487`: the MorePopover test is flaky; run the class twice.

**(6) Breaking / CHANGELOG.** Not breaking. Add a "Fixed" entry.

**(7) Effort.** S.

**(9) Scope.** Core.

---

## 8. [26e1afd5] F2 follow-ups 1–8

None of these was done by F6, F7, F8 or F10. Each was checked against HEAD.

### 8.1 `TmDialog` prompt IME guard: OPEN

**(2) Current state.** `TmDialog.razor` L517–539 (`HandlePromptKeyDown` arms on Enter; `HandlePromptKeyUp` submits when armed). There is no `IsComposing` anywhere in `src/` (grep). Note that `KeyboardEventArgs.IsComposing` does **not** exist in `Microsoft.AspNetCore.Components.Web` 8.0.12, which is referenced for the net8.0 TFM (checked the nuget XML docs). It exists in 9.x and 10.x.

**(3) Change.**
1. New `src/Tempo.Blazor/Helpers/KeyboardEventArgsExtensions.cs`:

   ```csharp
   internal static class KeyboardEventArgsExtensions
   {
       /// <summary>True while an IME composition is in progress; always false on net8.0 (the property does not exist there).</summary>
       public static bool IsImeComposing(this KeyboardEventArgs e)
   #if NET9_0_OR_GREATER
           => e.IsComposing;
   #else
           => false;
   #endif
   }
   ```

2. L519: `if (e.Key == "Enter" && !e.IsImeComposing())`. L529: `if (e.Key != "Enter" || !_promptEnterArmed || e.IsImeComposing())`.

**(4) RED test.** `tests/Tempo.Blazor.Tests/Feedback/TmDialogPromptKeyboardTests.cs`, `Prompt_EnterDuringImeComposition_DoesNotSubmit`: `KeyDown(new KeyboardEventArgs{Key="Enter", IsComposing=true})` then `KeyUp(Enter)`, then assert `OnPromptResult` was not invoked. The test project is net10-only.

**(6) CHANGELOG.** "Fixed".

**(7) Effort.** S.

**(8) Open question, minor.** On net8 the guard is a no-op. Safari's commit-Enter quirk (keyCode 229 with `isComposing=false`) is not detectable through Blazor's `KeyboardEventArgs`. I recommend accepting and documenting this.

### 8.2 `OverlayRound6E2ETests` summary: OPEN

`tests/Tempo.Blazor.E2E/Feedback/OverlayRound6E2ETests.cs` L11–13 says "the submit happens on the input's Enter keydown (guarded against auto-repeat)". The code submits on the keyup of a press whose keydown the input saw (`TmDialog.razor` L505–516).

**Change.** Replace L11–13 with: "…After the fix the prompt submits on the Enter KEYUP of a press whose keydown the input itself saw (armed on keydown), so the opener's trailing keyup arrives unarmed and is spent." Docs only. Effort S.

### 8.3 `ScrollRegionInterop`: OPEN, all four sub-points

**(2) Current state.** `Helpers/ScrollRegionInterop.cs`:
- **Detach race:** `DetachAsync` L67–72 returns early when `!_attached`. An attach still in flight (L40–56) sets `_attached=true` after a detach, which leaves a `ResizeObserver` on a removed node.
- **NRE catch:** L63 `catch (NullReferenceException) { }`.
- **No children observed:** `tm-focus-trap.js` `syncScrollRegion` L432–438 uses a `ResizeObserver` on the scroller only. Content growing inside a `max-height`-capped scroller does not resize the scroller, so it is never re-evaluated.
- **Label fixed at attach:** the callers are `TmModal` L306–316, `TmDialog` L347–357 and `TmDrawer` L366–376. A title that appears later is never wired.

**(3) Change.**
1. **C# generation guard.**
   - Add `private int _generation;`.
   - In `AttachAsync`: `var gen = ++_generation;`, and after each await, `if (_disposed || gen != _generation) { await StopOrDropAsync(module); if (_disposed) await ReleaseAsync(module); return; }`.
   - In `DetachAsync`: `_generation++; _attached = false; await StopOrDropAsync(_module);`. Drop the `!_attached` early return; stop is idempotent in JS (L443–446).
   - Delete L63.
2. **Label re-sync.**
   - **C#:** `public Task UpdateLabelAsync(string? labelledBy, string? label)` invokes `"updateScrollRegionLabel", _id, labelledBy, label` (guarded, no-op when not attached).
   - **JS:** `export function updateScrollRegionLabel(id, labelledBy, label)` updates `region.labelledBy`/`region.label` and calls `region.apply()`. Store `labelledBy`, `label` and `apply` in the map entry at L438, and have `apply` read from the entry.
   - **Callers:** keep a `string? _regionLabel` and call `UpdateLabelAsync` in `OnAfterRenderAsync` when the computed value changes while attached:
     - `TmModal`: `string.IsNullOrEmpty(Title) ? null : _titleId`
     - `TmDialog`: `ContentLabelledBy`
     - `TmDrawer`: `LabelledBy`/`AriaLabel`
3. **Children observed.** In `syncScrollRegion` (L433–438):
   - also `observer.observe(child)` for each `element.children`;
   - add `const mutations = typeof MutationObserver === 'function' ? new MutationObserver(() => { reobserveChildren(); apply(); }) : null; mutations?.observe(element, { childList: true, subtree: true, characterData: true });`;
   - store it and disconnect it in `stopScrollRegion` (L447).
4. **`data-tm-scroll-region` marker.** Covered in 8.4.

**(4) RED tests.**
- **bUnit, new `tests/Tempo.Blazor.Tests/Helpers/ScrollRegionInteropTests.cs`:**
  - `DetachDuringInFlightAttach_StopsTheRegion`: set up the module with `syncScrollRegion` pending, call `AttachAsync` without awaiting, call `DetachAsync`, complete the sync, then assert `stopScrollRegion` was invoked after `syncScrollRegion`.
  - `UpdateLabel_ForwardsTheNewTitleId`.
  - The class is internal and `InternalsVisibleTo` covers the tests project. Reuse `OverlayScrollRegionWiringTests.cs` patterns.
- **Node:**
  - `a mutation inside the scroller re-decides the region` (stub `globalThis.MutationObserver`, change `scrollHeight`, fire the callback, then assert `tabindex=0`);
  - `updateScrollRegionLabel rewires aria-labelledby`;
  - `stopScrollRegion disconnects the mutation observer`.

**(6) CHANGELOG.** "Fixed" (region name tracks the title).

**(7) Effort.** M.

### 8.4 Custom `TmDialog`: scroll region can become the initial focus — OPEN

This becomes deterministic once item 1 lands; see the coupling note there.

**(2) Current state.** `tm-focus-trap.js` L143 uses `list[0]` as the fallback. `syncScrollRegion` L416 sets `tabindex="0"` on an overflowing scroller. `TmDialog.razor` L59 `div.tm-dialog-content` precedes the custom content; `InitialFocusId` for Custom is `null` (L278–284).

**(3) Change.**
- In `syncScrollRegion`, after L410, add `element.setAttribute('data-tm-scroll-region', '');`.
- In `stopScrollRegion` L448–453, add `region.element.removeAttribute('data-tm-scroll-region');`.
- In `focusInitial` L143, replace `list[0]` with `list.find(el => !(el.hasAttribute?.('data-tm-scroll-region'))) || list[0]`. That is still "first focusable", just never the region. Use the same filter in `restoreTrapFocusIfLost` (item 3).

**(4) RED test, node.** `initial focus skips an overflowing scroll region and lands on the first control after it`: stub scroller with the `data-tm-scroll-region` attribute in `attributes` and a button; `querySelectorAll` returns `[scroller, button]`; after activate, `button.focusCalls === 1` and `scroller.focusCalls === 0`. Also add an E2E in `Feedback/DialogF2DebtE2ETests.cs`: a custom dialog with overflowing content and a button inside opens with focus on the first button, not on `.tm-dialog-content`. If the demo page has no overflowing Custom `TmDialog`, add one to `ModalDialogPage.razor` with `data-testid="open-long-custom-dialog"`.

**(7) Effort.** S.

### 8.5 CHANGELOG `TmButton` clause: OPEN

`CHANGELOG.md` L77–96 (under `## 2.9.1 (unreleased)`, "Breaking / Migration"):
- **L80:** "an icon-only button no longer grows by one gap because of an empty flex child". It compares against an unreleased intermediate state; delete the clause and keep "It renders only when there is content and ships as `display: contents`…".
- **L94:** "a child selector (`button > svg` still matches, but `svg + *` …". Replace with "a child selector `button > svg` still matches the `Icon` parameter's icon (rendered directly in the button, `TmButton.razor` L27–30 / L43–46) but no longer an icon placed in the button's content, which now sits inside `.tm-btn-label` (L37–40); `svg + *` now matches the span, not the text".

Effort S. No test.

### 8.6 Docs: OPEN, plus a 3-snap node test

- **`docs/focus-scope-migration.md` L46–47:** "Every `TmDialog` names an explicit initial-focus target … so the trap never falls back to 'first focusable'" contradicts the table row "`Custom` | unset" (L55). Change it to "Every built-in `TmDialog` type except `Custom` names an explicit initial-focus target … A `Custom` dialog falls back to the first focusable of its content — never to the overflowing scroll region (it is skipped)." The last sentence assumes 8.4 has landed.
- **`docs/bottom-sheet.md` L23–25:** "with the flag off, a downward flick from the lowest snap clamps to it and a fast flick settles there". This is wrong for three or more snaps. Verified with `settle([0.3,0.6,1], 0.9, false, 3, 1, true)`, which gives `index 1` (one step down) rather than the lowest snap. Replace with: "…with the flag off, a fast flick steps one snap down like any flick and clamps at the lowest snap; it never dismisses."
- **`tm-sheet.js` L45–46 comment:** the same correction.
- **New test in `__tests__/tm-sheet.test.mjs`** (after L535): `SwipeToDismiss=false on a 3-snap sheet: a fast downward flick from full steps ONE snap, not to the lowest`:
  - `settle([0.3,0.6,1], 0.9, false, 3, 1, true)` gives `{dismiss:false, index:1}`;
  - `settle([0.3,0.6,1], 0.55, false, 1, 0.6, true)` gives index 0;
  - `settle([0.3,0.6,1], 0.25, false, 3, 0.3, true)` gives index 0 with `dismiss:false`.

  This pins current behaviour, so it is green immediately. It is a documentation test.

Effort S.

### 8.7 Stale comments: OPEN

Line numbers have moved since the review.
- **`_modal.css` L296–297** (was 277–278): "The scroller is tabindex="0" (a keyboard-only user must be able to reach a scrollable message…" becomes "The scroller gains tabindex="0" only while it overflows (tm-focus-trap.js syncScrollRegion), so its focus has to be visible."
- **"under a suppressed RZ10012"** appears in **four** files: `TmDialog.razor` L256, `TmModal.razor` L226, `TmDrawer.razor` L251 and `TmOverlayPanel.razor` L373. RZ10012 is promoted to an error in `Directory.Build.props` L6–10. Change it to "(an internal tag compiles to a literal element — RZ10012, an error in this repo)".
- **`tests/Tempo.Blazor.Tests/Theme/UnconstrainedClassOwnershipTests.cs` L18:** "so `RecordedCollisions` now stands empty". The list at L132 has entries; rewrite as "all fifteen fixed in 2.8.26; the pairs still listed in `RecordedCollisions` are the descendant-selector findings recorded since 2.9.0."

Effort S.

### 8.8 Shared lazy JS module helper: OPEN

**(2) Current state.** Ten `internal` wrappers duplicate the import, disposed and guard boilerplate:

`Helpers/TopLayerInterop.cs:62`, `SheetInterop.cs:51`, `ScrollRegionInterop.cs:40`, `FocusTrap.cs:47`, `MenuNavInterop.cs:28`, `FocusIfLostInterop.cs:31/52/67`, `ToolbarInterop.cs:29`, `EditorShellInterop.cs:107` and `ViewportProbe.cs:44`.

They also differ inconsistently: some catch `JSException`, some catch NRE (`FocusTrap` L75, `ScrollRegionInterop` L63), and only some release a module whose import outlived disposal.

**(3) Proposed API**, new `src/Tempo.Blazor/Helpers/LazyJsModule.cs`:

```csharp
/// <summary>One lazily imported ES module per owner: single in-flight import, disposal-safe, every interop failure
/// class (JSException, JSDisconnectedException, TaskCanceledException, ObjectDisposedException,
/// InvalidOperationException) swallowed. Safe to construct during prerender.</summary>
internal sealed class LazyJsModule : IAsyncDisposable
{
    public LazyJsModule(IJSRuntime js, string modulePath);
    public bool IsDisposed { get; }
    /// <summary>The module, or null when JS is unavailable or the owner was disposed (an import that
    /// outlived disposal is released here).</summary>
    public ValueTask<IJSObjectReference?> GetAsync();
    /// <summary>False when the call could not be made or failed.</summary>
    public ValueTask<bool> TryInvokeVoidAsync(string identifier, params object?[] args);
    public ValueTask<(bool Ok, T? Value)> TryInvokeAsync<T>(string identifier, params object?[] args);
    public ValueTask DisposeAsync();
}
```

**Migration.** Each wrapper keeps its public surface and replaces `_module`/`_disposed`/try-catch with a `LazyJsModule` field. `FocusTrap` keeps its `FallbackFocusAsync` on `TryInvokeVoidAsync == false && !IsDisposed`. Module paths are unchanged, so every bUnit `JSInterop.SetupModule(path)` keeps working.

**(4) Tests.** New `tests/Tempo.Blazor.Tests/Helpers/LazyJsModuleTests.cs` covering:
- one import for two concurrent `GetAsync` calls;
- dispose during an in-flight import releases the module and returns null;
- each guarded exception yields `false`;
- double dispose is safe.

Then run the full bUnit suite, which must be unchanged.

**(6) Breaking / CHANGELOG.** Not breaking; internal. No CHANGELOG entry.

**(7) Effort.** M.

**(8) Open question.** Do it now or defer? I recommend doing it as the **last** phase of the core follow-up, as a pure refactor after the behaviour fixes. It removes the NRE catches as a side effect. Deferring it is acceptable.

**(9) Scope for 8.1–8.8.** All core.

---

## 9. [07411bd1] `positionFloating`/`place` option vocabulary

**(1) Status: OPEN.**

**(2) Current vocabularies.**
- **TmOverlayPanel vocabulary:** `offset`, `margin`, `flip`, `shift`.
  - `TmOverlayPanel.BuildOptions` (`TmOverlayPanel.razor` L463–475) sends `offset`/`margin`/`flip`/`shift`, mirroring the C# parameters `Offset`, `ViewportMargin`, `Flip`, `Shift` (L168–178).
  - `resolvePlacement` (`overlay.js` L90–96) reads `offset`/`margin`/`flip`/`shift`.
- **Alias vocabulary:** `anchorGap`, `clamp`.
  - `place()` (L305–316) translates the entry's `offset`/`shift` into `anchorGap`/`clamp`.
  - `positionFloating` (L264–273) translates them back with `options.anchorGap ?? options.offset` and `options.clamp ?? options.shift`. Its contract comment is at L200–204.
- **Callers:** only `place()` and the node tests. `overlay.test.mjs` L14/L87/L127 use `offset`/`shift`; `floatOpts` L221/L224 uses `anchorGap`/`clamp`. `tmp/f3-review-vv.mjs` is scratch.
- **Public JS API?** No. Rule 1 in `docs/overlays.md` (L144–146): "they never import overlay.js directly … today only TmOverlayPanel imports it". `positionFloating` was introduced in `a444f5fd` (2026-10-08) under `## 2.9.1 (unreleased)` (CHANGELOG L626), so it has never shipped.

**(3) Change.** Make `offset`/`margin`/`flip`/`shift` the single vocabulary.
1. `positionFloating` L267: `offset: options.offset,`. L270: `shift: options.shift,`.
2. `place()` L309: `offset: options.offset,`. L312: `shift: options.shift,`.
3. Rewrite the contract comment L200–204 to `{anchor, placement, align, offset, margin, flip, shift, matchAnchorWidth, originLeft, originTop}`, with no aliases.
4. `overlay.test.mjs` `floatOpts` L221/L224: `offset: 4`, `shift: true`.

**(4) RED test.** `positionFloating ignores the retired anchorGap/clamp aliases`: `positionFloating(panel, floatOpts({ offset: 10, anchorGap: 99 }), stubWindow())` gives `panel.style.top === '150px'` (140 + 10).

**(5) Verification.** `npm run test:overlay`.

**(6) Breaking / CHANGELOG.** Not breaking (private, unreleased). No CHANGELOG entry; CHANGELOG L626 already names only `positionFloating`.

**(7) Effort.** S.

**(8) Note for the NotionEditor plan `c8d4bcd2`.** Its text says Notion should "volat jádrový `positionFloating`" (call the core `positionFloating`) with `{margin, anchorGap}`. That would make the function a cross-package API and violates rule 1. Add a remaining-task note: Notion surfaces consume `TmOverlayPanel`; if a raw call is ever needed, use the `offset`/`shift` vocabulary.

**(9) Scope.** Core.

---

## 10. [e5f236e8] + [c51ddf81 #7] ContextMenu / SplitButton / NotificationBell on mobile — a product decision

**(1) Status: OPEN by decision.** F3-PANEL-PRESENTATION (a) says "ContextMenu/SplitButton/NotificationBell stay Popover."

**(2) Current state at 390.** All three render `TmOverlayPanel` with no `MobilePresentation`, so they get the panel default `Popover`.
- **`TmContextMenu.razor` L33–45:** `Role="menu"`, `Align=End`, panel `min-width:10rem` (`_context-menu.css` L27). The trigger is a `role=button` div holding a `Trigger` RenderFragment (L9–27). There is no text title to reuse.
- **`TmSplitButton.razor` L37–48:** `Role="menu"`. The menu has no title; the parameters are `OnClick`, `Variant`, `Size`, `Disabled`, `IsLoading`, `ChildContent` and `Class` (L63–81).
- **`TmNotificationBell.razor` L27–88:** `Role="dialog"` with its own visible header and title (`aria-labelledby`, L108). The panel is `width:360px; max-width:calc(100vw - 2rem)`, which is 358 px at 390 (`_notification-bell.css` L60–61), with `max-height:400px` (L111).
- **What all three already have:** promotion, Escape/outside dismiss, focus return, and `tm-menu-nav` roving for the two menus. Neither the bell nor the menus has a text input, so there is no typeahead anchor to protect and a sheet would be technically allowed by rule 6.

**What a sheet needs.**
- A `MobilePresentation` parameter forwarded to `TmOverlayPanel` (the `TmDropdown` precedent, L40/L78).
- A sheet title: a new `string? SheetTitle` (`Title`) parameter for ContextMenu and SplitButton, because their triggers are fragments or icons.
- For the bell, hiding its own header in sheet mode, or passing its title as `Title` and suppressing the inner header to avoid a double header.
- The bell's `max-height` dropped in a sheet.
- Initial focus on the first menuitem through `InitialFocusSelector`; this already works.
- After item 7 lands, Tab stays inside.

**Options.**

| Option | What | Cost | Risk |
|---|---|---|---|
| **A. Status quo** | Keep Popover; record it in docs | 0 | 44 px items still render in a small floating menu on a phone; inconsistent with `TmDropdown` |
| **B. Opt-in (recommended)** | Add `MobilePresentation` (default **Popover**) and `SheetTitle` to ContextMenu and SplitButton, and `MobilePresentation` to the bell (sheet uses the bell title, header hidden); demo + E2E at 390 | M | none; non-breaking and additive |
| **C. Default Auto** | Same as B, but default `Auto` like `TmDropdown` | M | visual behaviour change at mobile; needs a CHANGELOG "Changed" entry; reverses part of the F3 decision |

**Recommendation.** B: additive and consistent with the F3 decision (Auto only by deliberate opt-in), and it reuses the TmDropdown wiring. It needs a decision update (append to F3-PANEL-PRESENTATION).

**Tests for B.**
- bUnit: `MobilePresentation=Sheet` renders a `.tm-overlay-panel-sheet` titled `SheetTitle`, and `role=menu` on the content wrapper.
- E2E: 390 touch sheet open, items ≥ 44 px, Tab stays inside, Escape returns focus to the trigger.
- Regenerate the JSON docs.

**(6) CHANGELOG.** "Added" for B, "Changed" for C.

**(7) Effort.** M.

**(9) Scope.** Core follow-up, only if B or C is chosen.

---

## 11. [0e390ce4] + [c51ddf81 #10] Extension-package z-index dialogs

Full tables are in `cluster-B-packages.md`.

**(1) Status: OPEN; mostly covered by package plans.**

**(2) Key facts.**
- `TopLayerInterop` is `internal` (L22), and the only `InternalsVisibleTo` (`Tempo.Blazor.csproj` L87–91) is for tests. Packages reach the top layer only via `TmModal`, `TmDialog`, `TmOverlayPanel` or a modal `TmDrawer`. `TmFocusScope` does **not** promote.
- **RTE dialogs (core):** `Components/Activity/{Link,Image,Table,Video,FindReplace}Dialog.razor` are own-div overlays at z 1040 (`_link-dialog.css` L6/L11, and the same in the other four CSS files). They have no `role`, `aria-modal`, Escape or trap. Hosts: `TmRichEditorFull` (L57–103), `TmRichEditorSimple` (L46) and `TmMarkdownEditor` (L41–52). **No plan covers them**; there is no RTE plan among the 38 plans.
- **Dashboard widget selector (core):** `TmWidgetSelector.razor` L7–8, `_dashboard.css` L415–425 (z 1040). **Covered** by Dashboard plan `d254e3dd` Fáze 1 subtask B12 ("přestavět na TmModal", i.e. rebuild on TmModal) and Fáze 6. B12's line refs ("ř. 378–388, z-index 1000") are stale.
- **NotionEditor `c8d4bcd2`:** about 18 surfaces, covered by F4/F6/F7. Gaps:
  - the desktop comment popovers `.tm-nbcp`/`.tm-ntcp` stay non-promoted under `Auto`;
  - `TmNotionBlockContextMenu` (999/1000, `initFocusTrap`) is unnamed;
  - F7 should state that `TmNotionTempoEditorModal` wraps `TmModal`;
  - F4's "z-index:1200" text is stale (now 1040).
  - The demo hosts the editor inside a `TmModal` (`NotionEditorPage.razor` L838–881), so the exposure is real today.
- **Spreadsheet `8959eebe`:** F3 moves 14 dialogs plus InsertImage and the alerts onto `TmModal`. Gaps:
  - the column/row resize dialogs (`TmSpreadsheetCanvasGrid.razor` L80–130) are unnamed;
  - the in-cell validation dropdown `.tm-spreadsheet-vd` (`TmSpreadsheet.razor` L285–298) is in no phase.
- **DocumentEditor `146c7e1b`:** F5 moves Version, Compare and both Debug modals onto `TmModal`. Gap: `TmDocumentCommandPalette` reuses `.tm-command-palette-backdrop` without `popover`, and F5 only switches it to `TmFocusScope`, which does not promote.
- **Signing `1fba4858`:** no dialogs exist. The only fixed layer is the designer context menu (z 1090), which F8 replaces with `TmContextMenu`. Rule 9's "signing dialogs" entry is stale.

**(3) Change, core.** One phase "RTE dialogs on TmModal". For each of the five dialogs:
- render `<TmModal Show Title Size=Small MobilePresentation=Auto RestoreFocusTarget=<editor contenteditable> Footer=…>`, keeping the `tm-rte-*` content and button classes;
- delete the `position:fixed`/`z-index` overlay and backdrop rules in the five CSS files;
- restore the editor selection on close, keeping the existing selection-save JS in `richEditor.js`.

Then, in `docs/overlays.md` rule 9 (L193–200), remove "rich-text editor … dialogs" and "signing package dialogs".

**(4) Tests.**
- **bUnit:** `LinkDialog_RendersInTmModal_WithDialogRoleAndTitle` (and the same for the other four).
- **E2E:** a new `RteDialogsTopLayerE2ETests`: open the link dialog from `TmRichEditorFull` inside a `TmModal` (or a sheet) and assert `PaintsAtAsync` (the helper in `OverlayRound3FixE2ETests.cs` L58–75) puts the dialog above the host. Escape closes it, and focus returns to the editor.

**(5) Verification.** `dotnet test --filter "FullyQualifiedName~RichEditor|FullyQualifiedName~LinkDialog|FullyQualifiedName~Rte"` plus the E2E.

**(6) CHANGELOG.** "Changed": the RTE dialogs are now `TmModal`s, with role, Escape and a trap, and consumer CSS targeting `.tm-rte-*-dialog` positioning is affected.

**(7) Effort.** M (RTE). Package items are in their plans.

**(8) Open questions.**
- **Q-A.** Is the RTE migration in the core follow-up, or does it wait for a new RTE redesign plan? I recommend the core follow-up: it is an a11y defect, not just z-order.
- **Q-B.** Should packages get a public promote primitive? I recommend **no**; packages migrate onto `TmModal`/`TmDialog`/`TmOverlayPanel`, as rule 9 says.

**(9) Scope.**
- **Core plan:** the RTE dialogs and the overlays.md rule 9 text.
- **Leave to package plans,** with remaining-task notes to add:
  - **Dashboard `d254e3dd`:** fix the B12 line refs.
  - **Notion `c8d4bcd2`:** the four gaps above, plus the `positionFloating` note (item 9).
  - **Spreadsheet `8959eebe`:** the two gaps.
  - **DocumentEditor `146c7e1b`:** the command palette must be hosted in `TmModal` or replaced by core `TmCommandPalette`.
  - **Signing `1fba4858`:** nothing.

---

## 12. Command palette accessibility: [f4057a47], [ec0299b9 #1], [ec0299b9 #2]

**(1) Status: OPEN, all three, reproduced.**
- **f4057a47:** in dark mode the focused item background is `rgb(30,41,59)` on a surface of `rgb(30,41,59)`, which is **1:1**. In dark mode `--tm-color-gray-50` is `#1e293b`, the same as `--tm-bg-surface` (`tokens-dark.css` L10/L122). In light mode it is gray-50 `#f9fafb` on white, about 1.04:1, so it fails in light too. The input has no `role`, no `aria-activedescendant` and the options have no ids (probe `optionIds: ["(none)","(none)"]`).
- **ec0299b9 #2:** the default palette's listbox scrolls (`scrollHeight 430 > clientHeight 384` on `/layout`). It is not focusable and contains no focusables, so axe reports `scrollable-region-focusable` as serious.
- **ec0299b9 #1:** `TmCommandPalette.razor` L47 `<li class="tm-command-palette-group" role="group">`; `group` is not an allowed role for `li`, which triggers axe `aria-allowed-role` (minor).

**(2) Current state.**
- **`TmCommandPalette.razor`:** input L22–28; listbox `<ul class="tm-command-palette-list" role="listbox" aria-label=…>` L37; groups L47–59; options `RenderItem` L84–109 (`<li class="tm-command-palette-item …" role="option" aria-selected=…>`); keyboard L212–235.
- **`_command-palette.css`:** L112–113 `.tm-command-palette-item:hover, .tm-command-palette-item-focused { background-color: var(--tm-color-gray-50); }`; groups L78–85.

**(3) Change.**
1. **Combobox wiring (fixes aria-activedescendant).**
   - Add `private readonly string _listId = $"tm-palette-list-{Guid.NewGuid():N}";` and `private string OptionId(int i) => $"{_listId}-opt-{i}";`.
   - Input L22–28: add `role="combobox" aria-expanded="@(_filtered.Count > 0 ? "true" : "false")" aria-controls="@_listId" aria-autocomplete="list" aria-activedescendant="@(_filtered.Count > 0 ? OptionId(_highlightedIndex) : null)"`. Keep `aria-label`.
   - L37: add `id="@_listId"`. In `RenderItem` L84: add `id="@OptionId(index)"`.
   - Precedent: `TmFilterableDropdown.razor` L11/L16/L65–66/L99 and `TmMultiSelect.razor` L15/L22/L255.
2. **ec0299b9 #2 is resolved by 1.** axe's `scrollable-region-focusable` matcher excludes combobox popups (a `listbox` whose id is in a `role=combobox` element's `aria-controls`; axe 4.10.2 `scrollable-region-focusable-matches.js` → `isComboboxPopup`). Optional: keep the highlighted option in view on ArrowUp/ArrowDown with a small JS `scrollIntoView({block:'nearest'})`. There is no shared core helper today; I recommend adding it to the palette's own handler via `ElementReference.FocusAsync`-free interop, otherwise the highlight can leave the visible area. Mark as optional.
3. **Contrast (f4057a47), `_command-palette.css` L112–113**, following the `_multiselect.css` L258–262 precedent:

   ```css
   .tm-command-palette-item:hover { background-color: var(--tm-bg-muted); }
   .tm-command-palette-item-focused {
     background-color: var(--tm-bg-muted);
     outline: 2px solid var(--tm-color-primary);
     outline-offset: -2px;
   }
   ```

   Primary is #2563eb on white, 5.2:1, and #60a5fa on #334155 in dark, about 4:1. Both are ≥ 3:1 non-text contrast; the muted fill is an extra cue.
4. **Group markup (ec0299b9 #1).** L47–59 becomes `<li role="none" class="tm-command-palette-group-item"><div class="tm-command-palette-group" role="group" aria-labelledby=…>` with label span + `<ul role="none">` options `</ul></div></li>`.
   - CSS L78–85: change `.tm-command-palette-group + .tm-command-palette-group` to `.tm-command-palette-group-item + .tm-command-palette-group-item`, and `.tm-command-palette-group-item { list-style:none; }`.
   - Update the selectors in `TmCommandPaletteTests.cs` L256 and `AdditiveExtensionsE2ETests.cs` L103 from `.tm-command-palette-list > [role='group']` to `.tm-command-palette-list > li > [role='group']`.
5. **Regenerate the CSS bundle** `tempo-blazor.bundled.css` (`efdc933b`: every CSS-touching commit).

**(4) RED tests.**
- **bUnit, `TmCommandPaletteTests`:**
  - `TmCommandPalette_Input_IsACombobox_ControllingTheListbox` (`role`, `aria-controls` equals the `ul` id, `aria-expanded="true"`);
  - `TmCommandPalette_ActiveDescendant_FollowsArrowDown` (initially equals option 0's id; after `input.KeyDown(ArrowDown)` it equals option 1's id; and every `[role=option]` has a unique id);
  - `TmCommandPalette_NoResults_HasNoActiveDescendant`;
  - `TmCommandPalette_GroupByCategory_GroupIsADivInsideANoneLi`.
- **CSS test** (`tests/Tempo.Blazor.Tests/Theme/`, `RepositoryRoot()` pattern as `DesignTokenDefinitionTests.cs` L41): `CommandPaletteHighlight_DoesNotUseGray50AndHasAnOutline`.
- **E2E, `AdditiveExtensionsE2ETests`:**
  - `Palette_Default_IsAxeClean_ForScrollableRegionAndAria`: on `/layout` at 1280×720 with the default palette, `axe.run(host, { runOnly: { type: 'rule', values: ['scrollable-region-focusable','aria-allowed-role','aria-required-children','aria-required-parent','aria-valid-attr-value'] } })` returns 0 violations;
  - `Palette_Grouped_HasNoAriaAllowedRoleViolation` (the same rules on the grouped palette);
  - `Palette_DarkHighlight_IsVisible`: set `data-theme=dark` on `<html>` (as `GanttDialogOverlayE2ETests.cs` L101–107 does), open, compute the contrast of `getComputedStyle(focused).outlineColor` against `getComputedStyle(.tm-command-palette).backgroundColor`, and assert ≥ 3. Also ArrowDown, then `input.aria-activedescendant` equals the id of `.tm-command-palette-item-focused`.
  - axe is loaded from the CDN, as in the existing tests (network needed).

**(5) Verification.** `dotnet test … --filter FullyQualifiedName~TmCommandPalette`, the E2E filter `AdditiveExtensionsE2ETests`, and the CSS bundle freshness test (`CssBundle*`).

**(6) Breaking / CHANGELOG.** Markup changes: input role, option ids, group wrapper. Add a "Changed" entry noting that selectors keyed on `li.tm-command-palette-group` must move to `div.tm-command-palette-group`, plus a "Fixed" entry for contrast and aria-activedescendant.

**(7) Effort.** M.

**(8) Open question.** Fix the group role, or accept and document it? I recommend the fix: the change is small, and the minor axe finding would otherwise need an exception.

**(9) Scope.** Core.

---

## 13. [c51ddf81 #12] DataTable export rows vs the mockup

**(1) Status: OPEN; covered by the DataTable plan.**

**(2) Current state.** `TmDataTable.razor` L55–80 renders a `TmDropdown` whose two `TmDropdownItem`s are text-only ("CSV file", "Excel workbook (XLSX)"). `TmDropdownItem.razor` has no `Description` parameter. The mockup (`output/imagegen/tempo-reimagined/TmDataTable/data-table-export-menu--desktop.png`) shows rows with an icon, a title and a muted description.

**Plan coverage.** `6288f7e0` Fáze 4 quotes:
- "export položky `TmDropdownItem.Icon` … + popis (`TmDataTable_ExportCsvDescription`, `_ExportXlsxDescription`)" (export items with `TmDropdownItem.Icon` plus a description);
- the RED test "`TmDataTableExportTests` (položky CSV/XLSX mají `Icon` a popis formátu)" (the CSV/XLSX items have an `Icon` and a format description);
- a UX review against the desktop and tablet mockups.

Fáze 7 adds `MobilePresentation=Auto`.

**Gaps to add as a note on `6288f7e0` Fáze 4:**
- how the description renders: `ChildContent` markup, or a new `TmDropdownItem.Description` core API;
- the two-line row CSS;
- the shorter titles.

`TmExportOptions` (native `<select>`, `ImportExport/TmExportOptions.razor` L27–32) is not covered by any plan and has no mockup.

**(9) Scope.** DataTable plan `6288f7e0`, not core.

---

## 14. Confirmed ALREADY DONE

| Item | Proof |
|---|---|
| TmDropdown roving (c51ddf81 #6, 835acf83 #1) | `TmDropdown.razor` L38 `Role="menu"`, L34 `OnOpened="FocusFirstItemAsync"`, L165 `MenuNavInterop.FocusFirstAsync`. `TmOverlayPanel.razor` L557–560/L588–622 attaches `tm-menu-nav.js` to every `Role=menu` panel, popover and sheet. Commit `b70cda6b`. E2E `ToolbarOverflowE2ETests.Keyboard_TmDropdown_UsesTheSharedMenuKeyboard_OnDesktopAndInTheSheet` (L499, 1440 and 390). Node `tm-menu-nav.test.mjs` passes (325/325). |
| PanelPresentation rename (c51ddf81 #8a) | Resolved through the "distinct names in F6" option. `Components/Layout/SidePanelPresentation.cs` L3–8: "Distinct from the F3 `PanelPresentation`…"; `PanelPresentation` remains at `Overlay/OverlayEnums.cs` L36. No rename needed. |
| Gallery demo images (c51ddf81 #9) | `GalleryLightboxE2ETests.cs` L36–43 asserts 8 seeded local SVGs (`_content/Tempo.Blazor.Demo.SharedUI/gallery/photo-1.svg`). The probe opened the lightbox with an image. |

---

## Open questions for the user (product/API only)

| # | Question | Options | Recommendation |
|---|---|---|---|
| Q1 | ContextMenu/SplitButton/NotificationBell mobile presentation | A status quo / B opt-in `MobilePresentation` (default Popover) + `SheetTitle` / C default Auto | **B**, and update decision F3-PANEL-PRESENTATION |
| Q2 | RTE dialogs: core follow-up or a future RTE plan? | core now / defer | **core now** (it is also an a11y defect: no role, Escape or trap) |
| Q3 | Command-palette backdrop click: always close, or a parameter? | always / `CloseOnBackdropClick` (default true) | **always**, no parameter (matches TmModal and TmKeyboardShortcutsHelp) |
| Q4 | New public `TmLightbox.RestoreFocusTargetId`? | add / buttons only | **add** (additive; also covers Safari and Next/Prev) |
| Q5 | Palette group `aria-allowed-role`: fix the markup or accept? | fix / document | **fix** (small markup change, CHANGELOG "Changed") |
| Q6 | Shared `LazyJsModule` refactor now? | last phase now / defer | **last phase, now** (optional) |
| Q7 | Public promote primitive for packages? | public API / InternalsVisibleTo / none | **none**; packages use TmModal/TmDialog/TmOverlayPanel |
| Q8 | IME guard no-op on net8 (property missing)? | accept / JS-side guard | **accept**, documented |

## Summary table

| Item | Status | Effort | Breaking / CHANGELOG | Scope |
|---|---|---|---|---|
| c51ddf81 #1 activate-after-promote, `PromotedScopeGate` + E2E | PARTLY DONE (Gantt and TrapFocus done; 6 surfaces latent) | M | no / Fixed | core |
| c51ddf81 #2 Gantt IsOpen guard | OPEN | S | no / none | core (with #1) |
| c51ddf81 #4 toast dismiss → trap focus | OPEN (repro) | S–M | no / Fixed | core |
| c51ddf81 #5a palette backdrop click | OPEN (repro) | S | behaviour / Changed | core |
| c51ddf81 #5b + 7800d54b lightbox restore, gallery buttons | OPEN (repro) | S–M | markup div→button, new param / Changed + Added | core |
| 08a22db5 #2 modal Escape vs non-modal trap | OPEN (repro) | S | no / Fixed | core |
| f4c27b20 #1 sheet menu Tab escape (tabbable filter) | OPEN (root cause proven) | S | no / Fixed | core |
| 26e1afd5 (1) IME guard | OPEN | S | no / Fixed | core |
| 26e1afd5 (2) Round6 summary | OPEN | S | no | core |
| 26e1afd5 (3) ScrollRegionInterop race, children, label, NRE | OPEN | M | no / Fixed | core |
| 26e1afd5 (4) scroll-region initial-focus skip | OPEN (must ship with #1) | S | no / Fixed | core |
| 26e1afd5 (5) CHANGELOG TmButton clause | OPEN | S | docs | core |
| 26e1afd5 (6) docs + 3-snap test | OPEN | S | docs | core |
| 26e1afd5 (7) stale comments (×6 places) | OPEN | S | no | core |
| 26e1afd5 (8) `LazyJsModule` helper | OPEN | M | no (internal) | core, last phase |
| 07411bd1 vocabulary → offset/margin/flip/shift | OPEN | S | no (private, unreleased) | core (+ note on Notion plan) |
| e5f236e8 + c51ddf81 #7 Context/Split/Bell mobile | OPEN, product decision | M | Added (B) / Changed (C) | core if B/C |
| 0e390ce4 + c51ddf81 #10 package dialogs | OPEN | M (RTE) | Changed (RTE) | RTE → core; others → Notion/Spreadsheet/DocumentEditor/Dashboard plans (notes) |
| f4057a47 palette contrast + aria-activedescendant | OPEN (repro: 1:1 dark) | M (with ec0299b9) | markup / Changed + Fixed | core |
| ec0299b9 #1 li role=group | OPEN | S | markup / Changed | core |
| ec0299b9 #2 scrollable-region-focusable | OPEN (430 > 384 px scroller) | (fixed by the combobox wiring) | — | core |
| c51ddf81 #12 DataTable export rows | OPEN, covered | — | — | DataTable plan `6288f7e0` F4 (+ note) |
| TmDropdown roving / PanelPresentation / gallery images | ALREADY DONE | — | — | — |

**Suggested core-plan phase order:**
1. Focus-trap module fixes (7, 6, 8.4, 3): node RED first.
2. `PromotedScopeGate` (1, 2).
3. Palette (4, 12).
4. Gallery and lightbox (5).
5. Dialog IME and ScrollRegionInterop (8.1, 8.3).
6. RTE dialogs (11).
7. Vocabulary, docs and comments (9, 8.2, 8.5–8.7).
8. Q1 (10), if B is chosen.
9. `LazyJsModule` (8.8).
