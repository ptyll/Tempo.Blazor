# Pin on top — RED evidence (plan 4ce1b994 phase 3)

Each mutation was applied alone, the PinOnTopE2ETests class was run against self-hosted HTTPS API + WASM, and the mutation was reverted.

## A — no pin (registry never re-raises pinned roots)
- mutation: `function reRaise(filter) {` → `function reRaise(filter) {
    return;` in tm-top-layer.js
- result: Neúspěšné!  - Neúspěšné:     5, Úspěšné:     4, Přeskočeno:     0, Celkem:     9, Doba trvání: 2 m 13 s - Tempo.Blazor.E2E.dll (net10.0)
- red: Banner_StaysAboveAModalOpenedAfterIt_AndIsNotInert, Banner_StaysAboveADrawerOpenedAfterIt, Banner_PaintsAboveAToastPushedAfterIt, ToastPushedBeforeTheModal_StaysAboveIt, Screenshots_PhoneAndDark

## B — no CSS reset (UA popover defaults apply to the pinned bar)
- mutation: `:where([data-tm-pin-on-top]) {` → `:where([data-tm-pin-on-top-disabled]) {` in _pin-on-top.css
- result: Úspěšné!    - Neúspěšné:     0, Úspěšné:     9, Přeskočeno:     0, Celkem:     9, Doba trvání: 2 m 31 s - Tempo.Blazor.E2E.dll (net10.0)
- red: 

## C — no native-dialog deferral (isNativeModalOpen always false)
- mutation: `export function isNativeModalOpen() {` → `export function isNativeModalOpen() {     return false;` in tm-top-layer.js
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:     8, Přeskočeno:     0, Celkem:     9, Doba trvání: 2 m 2 s - Tempo.Blazor.E2E.dll (net10.0)
- red: NativeModalDialog_DefersThePin_UntilItCloses

## D — no inert exemption (module never adds data-tm-inert-exempt)
- mutation: `if (!el.hasAttribute('data-tm-inert-exempt')) {` → `if (false) {` in tm-pin-on-top.js
- result: Neúspěšné!  - Neúspěšné:     6, Úspěšné:     3, Přeskočeno:     0, Celkem:     9, Doba trvání: 2 m 31 s - Tempo.Blazor.E2E.dll (net10.0)
- red: Banner_StaysAboveAModalOpenedAfterIt_AndIsNotInert, Banner_ShownFromInsideAnOpenModal_LandsAboveIt, Banner_StaysAboveADrawerOpenedAfterIt, Banner_PaintsAboveAToastPushedAfterIt, HidingTheBanner_DemotesIt_AndRemovesThePopoverAttribute, Screenshots_PhoneAndDark

## B' — no CSS reset, re-run with the bare-markup probe (PIN-E2E.10 added after B stayed green)
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:     0, Přeskočeno:     0, Celkem:     1, Doba trvání: 1 m 32 s - Tempo.Blazor.E2E.dll (net10.0)
- red: AttributeFrontDoor_BareMarkup_IsPinned_AndTheUaPopoverDefaultsAreReset
- message:    Assert.AreEqual selhalo. Očekáváno:<0|0|0|true|rgba(0, 0, 0, 0)|0px>. Aktuálně:<696|-696|-437|true|rgb(255, 255, 255)|437px>. without the reset the UA rules (inset:0, margin:auto, fit-content, Canvas background) turn the bar into a centred white box
- why B alone was green: the demo banner's scoped CSS sets position/offsets/background/border and Tailwind preflight zeroes border-width, so the UA defaults were masked; the probe has no host CSS.


# Node registry layer (node --test tm-top-layer / tm-pin-on-top / tm-sheet)

## node: order ignores priority (registration only)
- file: tm-top-layer.js
- ℹ fail 1
- red: pinned roots re-raise in ascending priority, so the highest priority paints topmost

## node: detached root kept in the registry
- file: tm-top-layer.js
- ℹ fail 1
- red: promote drops disconnected pinned roots from the registry

## node: observer watches the popover attribute (loop)
- file: tm-pin-on-top.js
- ℹ fail 1
- red: the observer watches the document subtree but never the popover or inert-exempt attribute

## node: no takeRecords after own writes (loop)
- file: tm-pin-on-top.js
- ℹ fail 1
- red: a pass never schedules another pass: own writes are discarded with takeRecords()

## node: no Popover API guard
- file: tm-pin-on-top.js
- ℹ fail 1
- red: without the Popover API registering is a no-op: no attribute is added, nothing throws

## node: no native-dialog deferral
- file: tm-top-layer.js
- ℹ fail 6
- red: an element shown while a native modal dialog is open stays plain and is promoted on close | the dialog open attribute is the close fallback | a native modal dialog stops every re-raise: pins are inert outside it, so they must not cover it | a raise while a native modal dialog is open is deferred and lands when the dialog closes | a deferred show dispatches tm-pin-shown once, so the owner can re-announce | a close event while ANOTHER native modal dialog is still open does not re-sync


# bUnit layer (TopLayerPinServiceTests, TmPinnedOnTopTests, TmToastTests, TmNotificationToastContainerTests)

## bUnit: service never unpins
- file: TopLayerPinService.cs
- result: Neúspěšné!  - Neúspěšné:     3, Úspěšné:    84, Přeskočeno:     0, Celkem:    87, Doba trvání: 11 s - Tempo.Blazor.Tests.dll (net10.0)
- red: DisposeAsync_UnpinsExactlyOnce_AndIsIdempotent, Dispose_Unpins, DisposeWhileTheFirstPinIsInFlight_DoesNotThrow_AndStillUnpins

## bUnit: component drops a late pin (no release after dispose race)
- file: TmPinnedOnTop.razor
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:    86, Přeskočeno:     0, Celkem:    87, Doba trvání: 5 s - Tempo.Blazor.Tests.dll (net10.0)
- red: DisposeWhileTheFirstPinIsInFlight_DoesNotThrow_AndStillUnpins

## bUnit: service ignores a dispose during the in-flight pin — EQUIVALENT MUTANT, code removed
- file: TopLayerPinService.cs
- why green: TopLayerPin.IsActive already reads the owner's disposal and UnpinAsync is a no-op on a disposed service, so the branch was redundant. It was REMOVED in phase 3 instead of kept untested; ServiceDisposedWhilePinIsInFlight_DoesNotThrow_AndTheLateHandleIsInactive still pins the behaviour.
- result: Úspěšné!    - Neúspěšné:     0, Úspěšné:    87, Přeskočeno:     0, Celkem:    87, Doba trvání: 770 ms - Tempo.Blazor.Tests.dll (net10.0)
- red: 

## bUnit: notification container without single-flight
- file: TmNotificationToastContainer.razor
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:    86, Přeskočeno:     0, Celkem:    87, Doba trvání: 5 s - Tempo.Blazor.Tests.dll (net10.0)
- red: Container_RenderWhileThePinIsInFlight_PinsAndRaisesExactlyOnce

## bUnit: OnPinShown does not re-announce
- file: TmToastContainer.razor
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:    86, Přeskočeno:     0, Celkem:    87, Doba trvání: 5 s - Tempo.Blazor.Tests.dll (net10.0)
- red: Container_OnPinShown_ReAnnouncesTheVisibleToasts

## bUnit: component pins on every render
- file: TmPinnedOnTop.razor
- result: Neúspěšné!  - Neúspěšné:     1, Úspěšné:    86, Přeskočeno:     0, Celkem:    87, Doba trvání: 5 s - Tempo.Blazor.Tests.dll (net10.0)
- red: PinsItselfOnce_AfterTheFirstRender_WithItsPriority

