# Pin on top: a public API for host status UI (design)

> Status: **implemented in 2.12.0** (plan 4ce1b994: phase 1 design, phase 2 implementation), decision
> `TOP-LAYER-PIN` ([ADR-0006](adr/0006-top-layer-pin.md)). The inventory in §2 describes **2.11.1**, the state
> before this change. §3–§5 are the shipped contract. Demo: `/pin-on-top`.
>
> **Quick start.** For markup no component owns:
>
> ```html
> <div id="blazor-error-ui" data-tm-pin-on-top data-tm-pin-priority="200">…</div>
> <script type="module" src="_content/Tempo.Blazor/js/tm-pin-on-top.js"></script>
> ```
>
> For a component: `<TmPinnedOnTop Visible="@_offline" Priority="100" Class="my-bar">…</TmPinnedOnTop>`, or
> `TopLayerPinService.PinAsync(elementRef, new TopLayerPinOptions { Priority = 100 })` from
> `OnAfterRenderAsync` (dispose the returned `TopLayerPin` to unpin). Hide and show the element the way you
> already do, and never put `popover` on it.

## 1. Problem

Tempo keeps its own toast containers above every modal surface. The mechanism is an internal registry of
pinned roots. A host that needs ITS OWN status element above a modal cannot reach that registry: the framework
error bar `#blazor-error-ui`, a connection banner, a "session expires" strip. EmusCz therefore ships
`emus-error-ui.js`, which sets `popover="manual"` on the bar, watches the inline `display` with a
MutationObserver, skips the promotion while a `dialog:modal` is open and re-syncs on `close`.

That script still cannot join Tempo's registry. The bar sits in top-layer OPEN ORDER, so a `TmModal` opened
after the bar appeared covers it. The goal is one public, documented API that promotes any host element to the
top layer and keeps it above every later `promote()`. The API must also work for DOM that no Blazor component
renders.

## 2. Inventory of the current behaviour (2.11.1)

### 2.1 `tm-top-layer.js`, the registry

[`src/Tempo.Blazor/wwwroot/js/tm-top-layer.js`](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js)

| Lines | Symbol | Behaviour |
|---|---|---|
| [16](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L16) | `pinnedRoots` | A module-level `Set`. Insertion order is the only ordering, and nothing carries a priority. |
| [23–25](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L23-L25) | `pinRoot(root)` | Adds the root to the set. It does NOT show the root, and a second call is a no-op. |
| [32–34](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L32-L34) | `unpinRoot(root)` | Removes the root from the set. It does NOT hide the root. |
| [42–62](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L42-L62) | `reRaisePinnedRoots(except)` | Walks the set in insertion order and skips `except`. A disconnected root is deleted, not re-raised ([45–48](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L45-L48)). Only a root that is `:popover-open` is re-raised with `hidePopover()` + `showPopover()` ([52–55](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L52-L55)), because for a toast container `:popover-open` is the "has content" signal. An `InvalidStateError` is swallowed. |
| [69–79](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L69-L79) | `show(root)` | Calls `showPopover()` unless the root is already open. It swallows errors. |
| [97–103](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L97-L103) | `promote(root)` | A no-op without the Popover API ([98](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L98)). Otherwise it shows the root and then re-raises every pinned root above it. |
| [112–124](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L112-L124) | `demote(root)` | Calls `hidePopover()` when the root is open. Only roots that stay in the DOM need it. |
| [134–141](../src/Tempo.Blazor/wwwroot/js/tm-top-layer.js#L134-L141) | `raise(root)` | Re-raises the OTHER pinned roots first, then demotes and re-shows the root itself. "The pusher" therefore lands topmost. |

Callers:

- `TopLayerInterop` (C#).
- `overlay.js` imports `promote` ([`overlay.js:5`](../src/Tempo.Blazor/wwwroot/js/overlay.js#L5),
  [`:644`](../src/Tempo.Blazor/wwwroot/js/overlay.js#L644)), so a `TmOverlayPanel` re-raises pinned toasts as well.
- `tm-sheet.js` re-exports all five functions ([`tm-sheet.js:17`](../src/Tempo.Blazor/wwwroot/js/tm-sheet.js#L17)).

The node tests pin the current rules:

- [`tm-sheet.test.mjs:594–666`](../src/Tempo.Blazor/wwwroot/js/__tests__/tm-sheet.test.mjs#L594-L666): re-raise after a
  promote, skip a closed root, skip self, drop a disconnected root, the pusher lands last.
- [`overlay.test.mjs:323`](../src/Tempo.Blazor/wwwroot/js/__tests__/overlay.test.mjs#L323).

### 2.2 `TopLayerInterop`, the C# wrapper

[`src/Tempo.Blazor/Helpers/TopLayerInterop.cs`](../src/Tempo.Blazor/Helpers/TopLayerInterop.cs)

- `internal sealed` ([25](../src/Tempo.Blazor/Helpers/TopLayerInterop.cs#L25)). Hosts cannot use it.
- Each method maps to exactly one JS function: `PromoteAsync` / `RaiseAsync` / `DemoteAsync` / `PinAsync` /
  `UnpinAsync` ([41–60](../src/Tempo.Blazor/Helpers/TopLayerInterop.cs#L41-L60)).
- Every call goes through `LazyJsModule.TryInvokeVoidAsync`. The import is lazy, so the class is safe to
  construct during prerender, and every benign interop failure is swallowed. A call after dispose is a no-op
  ([62–66](../src/Tempo.Blazor/Helpers/TopLayerInterop.cs#L62-L66)).
- The modal roots use it only through `PromotedScopeGate`
  ([`PromotedScopeGate.cs:47–70`](../src/Tempo.Blazor/Helpers/PromotedScopeGate.cs#L47-L70)), which promotes first and
  then activates the focus scope. Only the two toast containers call pin/unpin.

### 2.3 `TmToastContainer`, the only real pin user (plus `TmNotificationToastContainer`)

[`src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor`](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor)

- The root always renders with `popover="manual" data-tm-inert-exempt`
  ([14](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor#L14)). The UA rule
  `[popover]:not(:popover-open) { display: none }` hides it until the first raise.
- **Pin and raise:** the first non-empty render pins and then raises. Every later render whose NEWEST toast id
  changed raises again ([257–276](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor#L257-L276)). Keying on
  the id rather than the count means a push at the `MaxVisible` cap still raises.
- **Demote when empty:** when the last toast leaves, the container demotes and THEN unpins
  ([340–353](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor#L340-L353)), so `:popover-open` is false
  whenever the container is empty.
- **Dispose:** unpins, then disposes the interop
  ([540–548](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor#L540-L548)). The root is NOT demoted here,
  because a detached popover is hidden by the browser.
- **Single flight:** the 2.11.1 fix. A render that arrives while `OnAfterRenderAsync` is awaiting JS only
  requests one more pass ([218–240](../src/Tempo.Blazor/Components/Feedback/TmToastContainer.razor#L218-L240)), and
  module fields are captured and nulled before the first await. Any new C# pin owner must keep that discipline.
- `TmNotificationToastContainer` mirrors the same pin/raise/demote/unpin sequence
  ([`TmNotificationToastContainer.razor:12`](../src/Tempo.Blazor/Components/Notifications/TmNotificationToastContainer.razor#L12),
  [154–183](../src/Tempo.Blazor/Components/Notifications/TmNotificationToastContainer.razor#L154-L183),
  [226–234](../src/Tempo.Blazor/Components/Notifications/TmNotificationToastContainer.razor#L226-L234)). It has no
  single-flight guard.
- The toast CSS resets the UA `[popover]` defaults at zero specificity
  ([`_toast.css:53–65`](../src/Tempo.Blazor/wwwroot/css/components/_toast.css#L53-L65)). The values are `inset:auto`,
  `width/height:auto`, `max-*:none`, `margin:0`, `border:none`, `padding:0`, `background:transparent`,
  `color:inherit` and `overflow:visible`. Every other promoted root has a copy of the same reset (`_modal.css`,
  `_drawer.css`, `_command-palette.css`, …).

### 2.4 The inert exemption `data-tm-inert-exempt`

[`src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js`](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js)

- When a modal trap activates, it releases its own ancestors and inerts every sibling of every ancestor up to
  `<body>` ([357–363](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js#L357-L363),
  [527–544](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js#L527-L544)).
- The walk skips `data-tm-backdrop` and `data-tm-inert-exempt`
  ([535](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js#L535),
  [508–514](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js#L508-L514)).
- The exemption works only on a sibling the walk VISITS. A pinned element nested inside a `@Body` wrapper is
  inerted together with the wrapper (see the placement rule in [overlays.md](overlays.md), rule 8).
- `exemptFocus` ([71–88](../src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js#L71-L88)) records focus that entered an exempt
  surface, so `restoreTrapFocusIfLost` can recover focus after a toast card is removed.
- Node test: [`tm-focus-trap.test.mjs:472`](../src/Tempo.Blazor/wwwroot/js/__tests__/tm-focus-trap.test.mjs#L472).

### 2.5 An open native `<dialog>.showModal()`

Tempo never calls `showModal()` itself. A host or framework dialog can, for example the Blazor reconnect modal
in the InteractiveAuto demo
([`ReconnectModal.razor.js:13`](../src/Tempo.Blazor.Demo.InteractiveAuto/Tempo.Blazor.Demo.InteractiveAuto/Components/Layout/ReconnectModal.razor.js#L13)).
What happens today:

1. `showModal()` adds the dialog to the top layer above everything already there, so a toast container that is
   already open stays BELOW the dialog and its `::backdrop`. The dialog's opening never reaches `promote()`, so
   no re-raise follows. That outcome is correct.
2. The browser makes everything outside the topmost modal dialog inert. A toast pushed while the dialog is open
   calls `raise()`, which lands the container ABOVE the dialog, but the container is inert there: the user sees
   cards that cover the dialog and cannot dismiss them. That outcome is WRONG, and it is the case this design
   changes for every pin (§4.2).
3. Any Tempo `promote()` while the dialog is open also re-raises pinned roots above the dialog, with the same
   inert result.
4. On `close` nothing is re-synced. Any pinned root ends up in whatever top-layer position it last had.

## 3. API shape (decision `TOP-LAYER-PIN`)

There is **one registry** in `tm-top-layer.js` and **one ordering rule**. Two front doors write to it:

| Front door | For | Signal |
|---|---|---|
| **Attribute** `data-tm-pin-on-top` (+ optional `data-tm-pin-priority`), scanned by the public module `tm-pin-on-top.js` with a MutationObserver | DOM that no Blazor component owns: `#blazor-error-ui`, server-rendered banners, third-party DOM | observed visibility (§4.1) |
| **C#** `TopLayerPinService` (scoped) → `TopLayerPin` handle, plus the component `TmPinnedOnTop` | Blazor components: an `ElementReference` the host already owns, or a declarative wrapper | observed visibility (§4.1) |
| internal `TopLayerInterop.PinAsync/RaiseAsync/DemoteAsync` (unchanged surface) | `TmToastContainer`, `TmNotificationToastContainer` | explicit (`:popover-open`, owner-driven) |

The owner chose the service plus the component (2026-11-15). The rejected alternatives are listed in the ADR.

## 4. Rules

### 4.1 Visibility is the signal (observed pins)

The host never calls show or hide. It hides and shows its element the way it always has (inline
`style="display:…"`, a class, the `hidden` attribute, the `Visible` parameter), and the registry follows.

- **Visible** means the element is connected, has no `hidden` attribute, and its computed `display` is not
  `none`.
- **The module owns the `popover` attribute.** An observed pin carries `popover="manual"` ONLY while it is
  promoted. Without that rule the visibility signal could not be read: on a closed popover the UA rule
  `[popover]:not(:popover-open) { display:none }` forces the computed `display` to `none`, so a closed popover
  would always look hidden. The cycle is:
  - element visible → set `popover="manual"` and promote it;
  - host hides it → `hidePopover()` and remove the attribute. The element is then a plain element again, and its
    own CSS decides what it looks like.

  If the host's markup already carries `popover`, the module leaves the attribute in place, and
  `:popover-open` becomes the signal (explicit mode).
- **Re-evaluation triggers:** a MutationObserver on `document.documentElement` with `subtree`, `childList`,
  `attributes` and `attributeFilter` set to `['style', 'class', 'hidden', 'data-tm-pin-on-top',
  'data-tm-pin-priority', 'open']`. `open` is there for native dialogs (§4.2). The `popover` attribute is NOT in
  the filter.
- **Batching:** callbacks are coalesced to one pass per animation frame.
- **InteractiveAuto handoff:** when the element is replaced, `childList` reports it. The disconnected element is
  dropped and the new `[data-tm-pin-on-top]` element is registered.
- **No observer loop:** `showPopover()` / `hidePopover()` mutate no attribute. The module's own
  `popover`-attribute writes and `data-tm-inert-exempt` writes are outside the filter, and each pass runs
  `observer.takeRecords()` after its own writes. Any feedback is therefore dropped, and a pass can never schedule
  another pass by itself.
- The module adds **`data-tm-inert-exempt`** while it holds the pin, and removes it on unpin only if the module
  added it. A pinned element therefore stays reachable under a Tempo modal trap (§2.4). The placement rule still
  applies: pin a layout-level sibling, not something nested in `@Body`.

### 4.2 Relationship to a native modal `<dialog>`

This is one rule for every pin, the toast containers included. The owner decided it on 2026-11-15.

- While any `dialog:modal` is open, **no pinned root is promoted or re-raised**. That covers the first show, a
  `raise()` and a re-raise after `promote()`. The browser has made everything outside the dialog inert, so a pin
  painted above the dialog would cover it while being impossible to interact with.
- An observed pin that becomes visible while the dialog is open stays a plain element: it has no `popover`
  attribute and paints in its own z-index band, so it renders below the dialog and its backdrop, and inert.
- An explicit pin (a toast container) that is already open stays where it is, below the dialog. A toast container
  that should first show during the dialog stays hidden until the dialog closes.
- **Close sync:** a capture-phase `close` listener on `document` catches the event even though `close` does not
  bubble. The MutationObserver's `open` attribute changes are the fallback for `dialog.open = false` and for
  removal. Either one runs a re-sync once no `dialog:modal` is left: every visible pin and every explicit pin
  that wanted to be open is promoted in registry order (§4.3). A pin never stays lost under a closed dialog.
- **Announcements:** while a native modal dialog is open, a toast container that has not been promoted
  yet is not displayed, and its live regions are not rendered either. That loses nothing compared with
  today: the browser's inert set already keeps assistive technology from reading anything outside the
  dialog. When the dialog closes, the re-sync shows the container. The phase 2 implementation must then
  re-announce the toasts that are still visible, and the phase 3 E2E covers that case.
- **Behaviour change:** a toast pushed during a native modal dialog no longer paints above it. CHANGELOG 2.12.0,
  "Breaking / Migration".
- A pin never breaks through a native dialog: it neither moves the dialog nor closes it, nor does it change the
  dialog's inert set.

### 4.3 Order between pins

- Every pin has an integer **priority**, default `0` (`data-tm-pin-priority`,
  `TopLayerPinOptions.Priority`, `TmPinnedOnTop.Priority`). A higher value paints higher. The toast containers
  use `0`, and the docs recommend `100` for a framework error bar, so it lands above toasts.
- **The order is `(priority ascending, registration order ascending)`.** Re-registering an element that is
  already pinned keeps its slot and updates its priority.
- `promote(other)` promotes `other` and then re-raises every open pin in that order. All pins end up above
  `other`, and the highest priority ends up topmost.
- `raise(pin)`, the push, re-raises in this order:
  1. the pins whose priority is ≤ that of `pin` (except `pin` itself);
  2. `pin`;
  3. the pins with a higher priority.

  Within its own priority band the pusher therefore lands on top, which is today's "the newest toast paints
  topmost", but it never lands above a higher band.
- A disconnected pin is dropped during the walk, as today.

### 4.4 UA popover defaults and the reset

- A new shared utility class resets the UA `[popover]` defaults at zero specificity:
  `:where([data-tm-pin-on-top], .tm-pin-on-top)` in a new partial `_pin-on-top.css`. The values are the
  `_toast.css` set: `inset:auto; width:auto; height:auto; max-width:none; max-height:none; margin:0;
  border:none; padding:0; background:transparent; color:inherit; overflow:visible`. The service-pinned element
  gets the class from the host (as documented), and `TmPinnedOnTop` renders it.
- **A bottom bar stays a bar.** With zero specificity, any host rule wins. `fixed bottom-0 left-0 right-0` keeps
  `bottom/left/right`, `top` resets to `auto` instead of the UA `0`, and `width:auto` with `left/right:0`
  spans the viewport. The UA rules `margin:auto` and `width:fit-content` are what would otherwise centre the bar
  and shrink it.
- `position` is not reset. The top layer computes `fixed` / `absolute` on its own, and the host keeps `fixed`.
- The reset sets no colour and adds no token. The colours stay the host's, so dark mode is not affected. Because
  the reset is `:where()`, it also applies while the element is NOT a popover. That is harmless for a
  `div`/`section`, since those defaults are already `0` / `none`.

### 4.5 No Popover API, no JS, prerender

- **Without the Popover API** (`typeof el.showPopover !== 'function'`), registering is a no-op. The module
  neither adds the `popover` attribute nor marks the element. The element paints in its own z-index band, as
  before.
- **Prerender / no JS:** `TopLayerPinService` and `TmPinnedOnTop` touch JS only from `OnAfterRenderAsync`
  through `LazyJsModule`. During prerender, and with bUnit's runtime, nothing is invoked and nothing throws. A
  server-rendered `[data-tm-pin-on-top]` element is picked up when the module starts.
- **Module start:** the attribute front door is opt-in. The host adds
  `<script type="module" src="_content/Tempo.Blazor/js/tm-pin-on-top.js"></script>`. The module starts itself
  (idempotently) on import, scans once on `DOMContentLoaded` (or immediately, if the DOM is already parsed), and
  observes from then on. `TopLayerPinService` imports the same module. Tempo does not ship a Blazor JS
  initializer (`*.lib.module.js`), so a host that uses no pins pays for no document-wide observer.
- **Dispose during an in-flight call:** `TopLayerPin.DisposeAsync` is idempotent, and so is the component's
  dispose. Dispose captures and nulls the module before its first await, the 2.11.1 `TmToastContainer` lesson,
  and swallows the benign interop exceptions. A pin request that completes after dispose unpins immediately (the
  `LazyJsModule` last-will callback).

## 5. Public contract

Everything lives in **`Tempo.Blazor`**. Nothing goes into Abstractions, because there is no UI-free contract to
share.

### 5.1 JavaScript, `_content/Tempo.Blazor/js/tm-pin-on-top.js` (public, semver-covered)

```js
export function pin(element, options = { priority: 0 })  // register / update priority; observed visibility
export function unpin(element)                           // unregister, hide, remove module-added attributes
export function refresh()                                // force one sync pass (rarely needed)
export function start()                                  // idempotent; called on import
```

`tm-top-layer.js` keeps `promote`, `demote`, `raise`, `pinRoot` and `unpinRoot` with unchanged signatures.
`pinRoot`/`unpinRoot` become the explicit-mode entry points of the same registry, with priority `0`.

### 5.2 Attribute

| Attribute | Value | Meaning |
|---|---|---|
| `data-tm-pin-on-top` | presence | Pin this element. Its visibility is observed. |
| `data-tm-pin-priority` | integer, default `0` | Order between pins; higher paints higher. |

### 5.3 C#

```csharp
namespace Tempo.Blazor.Services;

/// <summary>Pins host elements above every Tempo top-layer surface (modals, drawers, sheets, popovers)…</summary>
public sealed class TopLayerPinService : IAsyncDisposable      // TryAddScoped in AddTempoBlazor()
{
    /// <summary>Pins the element; disposing the returned handle unpins it. Call from OnAfterRenderAsync.</summary>
    public Task<TopLayerPin> PinAsync(ElementReference element, TopLayerPinOptions? options = null);
}

/// <summary>Handle of one pin. Idempotent, exception-safe dispose (also safe during prerender / after circuit loss).</summary>
public sealed class TopLayerPin : IAsyncDisposable
{
    public bool IsActive { get; }                                  // false when JS / the Popover API is unavailable or after dispose
    public Task SetPriorityAsync(int priority);
}

/// <summary>Options of a pin.</summary>
public sealed record TopLayerPinOptions
{
    /// <summary>Order between pins; higher paints higher. Defaults to 0 (the toast containers).</summary>
    public int Priority { get; init; }
}
```

```razor
@* Tempo.Blazor.Components.Feedback *@
<TmPinnedOnTop Visible="@_offline" Priority="100" Class="my-banner">…</TmPinnedOnTop>
```

`TmPinnedOnTop` takes the following parameters, each with an XML doc:

- `ChildContent`
- `Visible` (`bool`, default `true`; `false` renders the root with `hidden`)
- `Priority` (`int`, default `0`)
- `Class`
- `AdditionalAttributes` (splatted)

It renders `<div class="tm-pin-on-top …" data-tm-pin-on-top data-tm-pin-priority="…">` and pins itself through
the service after its first render. It has no user-facing text, so no localization keys are needed. It needs a
wireframe role and stencil entry (repository rules 14 and 15), which phase 2 adds.

The toast containers move to the same registry through the unchanged internal `TopLayerInterop` (explicit
mode, priority `0`). Their public surface does not change.

### 5.4 Semver

The release adds a JS module, an attribute pair, two C# classes, a record, a component and a CSS utility. All of
that is additive, so this is a **minor** release: **2.12.0**, lock-step for every packable project
(`ReleaseContractTests`). The one behaviour change is the toasts under a native modal dialog (§4.2), which the
2.12.0 "Breaking / Migration" table records. The other behaviour change is unobservable: pinned roots are
ordered by priority, and the default priority `0` reproduces today's order.

## 6. Out of scope for phase 1

Phase 1 adds no code. The implementation, the tests (bUnit, node, Playwright with RED evidence), the demo page,
the JSON documentation and the CHANGELOG entry are phases 2 and 3.
