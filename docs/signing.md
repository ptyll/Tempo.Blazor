# Signing

`Tempo.Blazor.Signing` is the signing workflow package: the PDF template designer (authoring)
and the signer wizard (`TmSigningFormRunner`). Models live in `Tempo.Blazor.Abstractions` so an
API can reference them without Blazor. Register with `builder.Services.AddTempoBlazorSigning()`
and load `_content/Tempo.Blazor.Signing/css/tempo-blazor-signing.css`.

Demo pages: `/signing-components` (every surface, including the new parameters) and
`/signing-from-editor` (document editor → designer → signer preview as one workspace).

## Layout

Both surfaces measure **their own container**, never the viewport.

| Surface | Breakpoints | Desktop | Tablet | Mobile |
|---|---|---|---|---|
| `TmPdfTemplateDesigner` | 768 / 1200 (`LayoutBreakpoints`) | docked palette + inspector, one toolbar | two toolbar rows, overlay panels | viewing + light edits (G2); no drag |
| `TmSigningFormRunner` | 640 / 1024 | docked field list (≥1024) | docked field list (640–1023) | full editing (G2) in a `TmDrawer` bottom sheet (&lt;640) |

`LayoutMode` (`TmLayoutMode`, default `Auto`) pins a band for a host or a test. The runner's
bands are 640 and 1024. The obsolete `TmPdfTemplateDesigner.MobileMode` is an alias that forces
`Mobile`.

Designer toolbar groups are frozen: `pages | view | language | zoom | detect`. The designer zoom
bar reuses the viewer `zoom` group. Hosts key off `data-tm-group`, never positional selectors.
Viewer groups `pages | zoom | comments` are frozen. The designer set is frozen too.

## Steps are Tm components (SI1)

Every step input is a core component: `TmTextInput`, `TmTextArea`, `TmSelect`, `TmCheckbox`,
`TmRadioGroup`, `TmFileDropZone`. Those components render a native `<input>` / `<select>`, so the
system pickers and `type` / `inputmode` / `autocomplete` stay.

**Date exception.** `TmSigningDateStep` keeps a native `<input type="date|month|datetime-local">`
styled with the core `tm-input` classes. A calendar component would hide the platform picker.

On a mobile layout, a choice field with five or fewer options renders as radio cards.

## Signature default (SI3)

`TmSigningFormRunner.DefaultSignatureMode` (`TmSigningDefaultSignatureMode`, default `Auto`)
decides the capture mode a signature step starts in **when the template is silent**. `Auto`
resolves to Draw on a coarse pointer (`(pointer: coarse)`, probed once by `signing-runner.js`)
and to Typed otherwise. A stored `Preferences.Format` always wins.

## Active steps (SI2)

Phone OTP, attachment upload and external verification are **host-confirmed** and opt-in:

| Hook | Completes the step when |
|---|---|
| `OnSendPhoneCode` | the host accepts the send (`CancellationToken` is passed through). Sending does not complete the step. |
| `OnVerifyPhoneCode` | the host returns `true`. The typed OTP is never stored as the field value. `CancellationToken` is passed through. |
| `OnAttachmentSelected` | the host returns the stored file URL. Read the stream inside the callback — the runner does not buffer it. SignalR hosts must stay inside the hub message size limit. |
| `OnExternalStepRequested` + `StepContextProvider` | the host writes `true` into `Values`. An external step completes only on that host-written `true`. |

**Security.** Without those callbacks the old behaviour stands: a click stores `true`, an upload
stays client-side metadata, a typed phone completes the step. That value is presentation state.
It is **not** proof that a verification, a payment or an upload happened. A host that must prove
it wires the callbacks and completes the step only after its own provider answers.

## Other host knobs

- `DocumentTitle` / `DocumentHeaderActions` / `ShowDocumentToolbar` — optional document chrome (U4).
- `EnablePinchZoom` on `TmDocumentPageViewer` — opt-in for a standalone viewer. The runner always enables it.
- Role colours — `--tm-signing-role-1..6` plus `RoleColor` on the overlay. Colour 6 is `#a16207` in light and `#facc15` in dark.
- Numeric geometry — the field inspector always shows X / Y / W / H. A standalone panel defaults `ShowGeometry` to false.
- Comments — `TmDocumentCommentThreadPanel` is a `TmDrawer` sheet on a mobile layout.

## JavaScript

Signing ES modules need no `<script>` tag. The signature step is the exception: it needs

```html
<script src="_content/Tempo.Blazor/js/signature-capture.js"></script>
```

| Module | Loaded by | Needs |
|---|---|---|
| `pdf-template-designer.js` | `TmPdfTemplateDesigner` | `pdf-template-designer-pointer.js` → core `pointer-drag.js` |
| `pdf-template-designer-pointer.js` | the designer module | core `pointer-drag.js` |
| `signing-editor.js` | `TmRecipientRoleEditor`, `TmConditionBuilder`, `TmFormulaBuilder` | `focusElement`, `insertAtCursor` |
| `signing-runner.js` | `TmSigningFormRunner`, and the viewer when pinch is on | `isCoarsePointer`, `scrollToField`, `enableViewerPinch` → core `pointer-gesture.js` |

There is no `formula-builder.js`. CSP must hash the four Signing modules plus core
`pointer-drag.js`, `pointer-gesture.js` and `signature-capture.js`. None of them call `eval`.
`_content/Tempo.Blazor/js/` must be served. Node lanes: `npm run test:signing-runner`,
`npm run test:overlay`.

Dispose is `IAsyncDisposable` only. Interop catches `JSDisconnectedException`. A pointer-drag
listener is not re-attached on every `pointermove`.
