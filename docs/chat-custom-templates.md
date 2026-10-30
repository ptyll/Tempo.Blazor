# Custom chats: TmChat templates, slots and `TmChatBubble`

`TmChat` renders a fixed model (`ChatMessage`) but no longer owns a fixed *look*: every part of
the UI can be replaced by a template or slot while the component keeps the machinery around it
(scrolling, threads, reactions, draft guard, the single live region, read receipts). This page is
the contract for hosts that build a **custom chat** on top of it — the shape of the feature came
from a real consumer (BeautyFans ran two hand-rolled chats because TmChat had no render
fragments); everything below ships as public, tested API.

## Why not `TmChat<TItem>` (ADR)

**Decision (closed — do not reopen):** `TmChat` stays **non-generic**. Instead of
`TmChat<TItem>` + `ItemKey`/`Timestamp`/`IsOwn`/… selectors, `ChatMessage` gains three host-owned
fields (`Kind`, `Metadata`, `Payload`) and the host maps its DTO onto `ChatMessage` once, while
`MessageTemplate` receives a non-generic `ChatMessageContext` with typed accessors
(`context.PayloadAs<T>()`, `context.GetMetadata<T>(key, fallback)`).

Rationale:

1. A generic type parameter with no inferable `TItem` breaks **every** existing `<TmChat>`,
   every `Render<TmChat>` in tests, every `@ref TmChat`, every `ITmDraftGuard` reference and the
   JSON documentation — a hard breaking change for zero gain.
2. The component needs `Id`, `Timestamp`, `Author` and `Type` internally for scroll anchors,
   announcements, read receipts, threads and the draft guard. A generic version would need six or
   more selectors (`ItemKey`, `Timestamp`, `IsOwn`, `ThreadRootId`, `Author`, `IsSystem`) — a
   strictly worse API than one DTO→`ChatMessage` mapping.
3. `Payload` + `context.PayloadAs<T>()` gives the template typed access to the host DTO without
   generics.
4. The rest of the library (`TmActivityComments`, the Notion comment blocks) is model-based too.

A generic `TmChat<TItem>` remains possible **as a separate component** over the same core — it is
a carry-forward, not part of this release. **Delivered in FZE-15** as
[`TmTypedChat<TItem>`](#the-typed-wrapper-tmtypedchartitem-fze-15): a separate component composed
OVER the non-generic `TmChat` — see the section below for the contract and the ADR.

## Mapping the host DTO onto `ChatMessage`

`TmChat` never interprets the three fields; it only carries them into the template and mirrors
`Kind` on the wrapper's `data-kind` attribute.

```csharp
private static ChatMessage ToChat(InboxMessageDto dto, ChatUser author) =>
    new(dto.Id, dto.PreviewText, author, ToMessageType(dto.Direction), dto.CreatedAt)
    {
        Kind = dto.State switch
        {
            InboxState.AiDraft => "ai-draft",   // routed to a Pending bubble
            InboxState.Queued  => "queued",     // routed to a Queued bubble
            _ => null,                          // plain message → built-in look
        },
        Metadata = new Dictionary<string, object?>
        {
            ["reason"] = dto.DraftReason,       // context.GetMetadata<string>("reason", "…")
        },
        Payload = dto,                          // context.PayloadAs<InboxMessageDto>()
    };
```

Notes:

- `Metadata` defaults to an **empty read-only dictionary**, never `null`; values must be
  in-process CLR objects (after a host-side JSON round-trip they become `JsonElement`s).
- `Payload` is `[JsonIgnore]` — `TmChat` never serializes or logs it; note that the record's
  `ToString()` includes it.
- `TmChat` mutates nothing: append a *new* `ChatMessage` instance when the DTO state changes.

## Templates and slots

| Member | Type | Replaces |
|--------|------|----------|
| `MessageTemplate` (+ `UseTemplateFor`) | `RenderFragment<ChatMessageContext>` | The message row. `context.DefaultContent` renders the built-in row (fallback for ordinary kinds). The wrapper element with `data-message-id` / `data-kind` stays built-in — scroll anchoring and read receipts are unaffected. |
| `ComposerTemplate` | `RenderFragment<ChatComposerContext>` | The whole composer. Drive it through `composer.Text` / `SetTextAsync` / `SendAsync`; register your own input with `composer.AttachAsync(element)` — or let TmChat resolve it by id with `AutoAttachComposerInput` (see below) — to get the built-in Enter/Shift+Enter + IME behavior and the post-send refocus. |
| `TypingTemplate` | `RenderFragment<IReadOnlyList<ChatUser>>` | The whole typing indicator. Must **not** contain a live region (see a11y rules). |
| `JumpToNewTemplate` | `RenderFragment<int>` | The *content* of the jump-to-new button (the count); the `<button>` and its behavior stay built-in. |
| `BannerContent` | `RenderFragment` | A strip above the message list (connection bar). Rendered raw: **no wrapper, no live region** — put `role="status"` inside the slot if it should be announced. |
| `HeaderContent` | `RenderFragment` | Very top of the component. An open thread panel overlays it. |
| `EmptyContent` | `RenderFragment` | The built-in empty state (root list only). |
| `ComposerAddon` / `ComposerFooter` | `RenderFragment` | Content above / below the built-in input (skipped entirely when `ComposerTemplate` is set). |
| `TmChatBubble` slots | `Badge`, `Meta`, `Note`, `Actions`, body | The building blocks of a custom bubble; `Locked` never renders the body. |

`UseTemplateFor` keeps the override surgical — e.g. `m => m.Kind is not null` lets ordinary
messages keep the built-in row while the host's special kinds render `TmChatBubble`s.

## The composer contract: `AttachAsync` vs. `AutoAttachComposerInput`

A `ComposerTemplate` input gets the built-in keyboard behavior (Enter submits with
`preventDefault`, Shift+Enter/Alt+Enter stay native, an IME composition session never submits,
the textarea auto-resizes up to the row cap) and becomes the refocus target of
`RefocusAfterSend` / `FocusComposerAsync` / `composer.FocusAsync()` in one of two ways:

1. **Explicit registration** — the host captures its own `ElementReference` and calls
   `composer.AttachAsync(input)` from its `OnAfterRenderAsync` once the input has mounted,
   again whenever the element is replaced, and with `composer.AttachAsync(default)` to detach.
2. **Auto-attach by id (opt-in, FZE-12)** — set `AutoAttachComposerInput="true"` and give the
   template input `id="@composer.InputId"`; TmChat resolves and attaches it itself:

   ```razor
   <TmChat Messages="_messages" CurrentUser="_user" OnSend="HandleSend"
           ComposerTemplate="AutoComposer" AutoAttachComposerInput="true" />

   @code {
       private RenderFragment<ChatComposerContext> AutoComposer => context =>
           @<textarea id="@context.InputId"
                      value="@context.Text"
                      @oninput="@(e => context.SetTextAsync(e.Value?.ToString() ?? string.Empty))" />;
   }
   ```

Rules of the auto mode:

- **Precedence: explicit > auto.** As long as a host registration exists, the auto-attached
  handle is disposed; `AttachAsync(default)` hands the control back to the auto-attach. The
  two modes never fight over Enter.
- **The lookup is scoped to TmChat's own render.** `chat.js` resolves the input with
  `root.querySelector('#' + id)` where `root` is THIS chat instance's root element — an element
  with the same id elsewhere on the page (a second chat, a hidden copy) never wins. Because
  `InputId` is per-TmChat-instance, an input inside a nested host component that re-renders
  INDEPENDENTLY of TmChat is re-captured on the NEXT TmChat render (the handle `sync()`s on
  every render: same node = no-op, replaced node = re-attach, missing node = detach). A host
  that swaps the element without TmChat re-rendering still needs the explicit
  `AttachAsync`.
- **Nothing happens before interactivity.** The auto-attach is a pure JS-module path: during
  prerender (and whenever the import failed) the template input has no keydown handling — the
  C# `@onkeydown` fallback cannot help an input TmChat did not render. Turning
  `ShowComposer` off, removing the template or the parameter disposes the handle.
- `EnterToSend` re-attaches (the option is baked into the attach call); the post-send refocus
  and the resize after a programmatic clear target the auto-attached input.

## The CSS scope pitfall

Markup passed into a `RenderFragment` carries the **host's CSS scope**, not `TmChat.razor.css`.
Two consequences:

1. Scoped classes of `TmChat.razor.css` (`.tm-chat__bubble`, `.tm-chat__meta`, …) **do not apply**
   inside a template — do not reach for them; build the row from `TmChatBubble` (whose own scoped
   stylesheet applies to its own markup, which the host *renders*, not *contains* — the component
   boundary re-scopes it correctly) or from host-owned classes.
2. Conversely, the chat's responsive rules branch on the chat's own width because the root is a
   CSS size container (`container-type: inline-size`, `container-name: tm-chat`,
   `contain-intrinsic-inline-size: auto 360px`). An **unnamed** `@container` rule inside host
   template markup therefore also resolves against the *chat*, not the viewport — usually what a
   custom row wants (the bubble behaves mobile inside a narrow chat column). Opt the whole
   container behavior out with `Class="tm-chat--no-container"` when the chat sits in a
   shrink-to-fit host that must not get a definite inline size.

## `TmChatBubble`

`TmChatBubble` is a presentation-only component for host-built rows: no domain copy (labels come
from the host), every visual driven by a `--tm-chat-bubble-*` knob, dark mode free because the
defaults reference semantic tokens that flip in `tokens-dark.css`.

| `ChatBubbleVariant` | Modifier class | Property set | Notes |
|---------------------|----------------|--------------|-------|
| `Incoming` | `tm-chat-bubble--incoming` | `--tm-chat-bubble-in-*` | received message |
| `Outgoing` | `tm-chat-bubble--outgoing` | `--tm-chat-bubble-out-*` | sent by the current user |
| `System` | `tm-chat-bubble--system` | `--tm-chat-bubble-system-*` | system event row |
| `Pill` | `tm-chat-bubble--pill` | `--tm-chat-bubble-system-*` | compact inline pill, radius always full |
| `Pending` | `tm-chat-bubble--pending` | `--tm-chat-bubble-pending-*` | dashed border (AI draft awaiting review) |
| `Queued` | `tm-chat-bubble--queued` | `--tm-chat-bubble-queued-*` | info tint (scheduled message) |
| `Discarded` | `tm-chat-bubble--discarded` | `--tm-chat-bubble-discarded-*` | body is a semantic `<del>` |
| `Locked` | `tm-chat-bubble--locked` | `--tm-chat-bubble-locked-*` | **body is never rendered** — the price goes to `Badge`, the unlock to `Actions` |

The root carries `data-variant="{variant}"` for host hooks and tests. Always give `Pending`,
`Queued`, `Discarded` and `Locked` a visible `Badge` or a `StatusLabel` (machine-readable,
visually hidden) — the surface colour alone does not name the state.

## Custom properties

The library declares **none** of these knobs — an override on a wrapper, on `:root` or on the
bubble itself always wins (and flips with the tokens in dark mode because the defaults are
semantic-token references).

| Knob | Default (fallback) | What it controls |
|------|--------------------|------------------|
| `--tm-chat-min-height` | `300px` (`0` in `tm-chat--fill`) | The chat's height floor; set `0` to let the host own the height |
| `--tm-chat-composer-offset` | `0px` | Space below the composer for host chrome (bottom navigation height); the narrow sticky composer and thread reply row rest at `bottom: var(--tm-chat-composer-offset)`, and while the keyboard-open marker is set at `bottom: max(var(--tm-chat-composer-offset), var(--tm-chat-keyboard-inset))` |
| `--tm-chat-safe-area-bottom` | `env(safe-area-inset-bottom, 0px)` | Extra padding under the sticky composer/thread row for the device safe area |
| `--tm-chat-keyboard-inset` | `0px` (RUNTIME-ONLY) | The on-screen keyboard's covered height, written into the chat root's inline style by `chat.js` while `TrackVirtualKeyboard="true"` — never declared by the library, removed again when the keyboard closes; on a narrow chat it lifts the composer (added while closed, `max()` while the open marker is set) |
| `--tm-chat-composer-clearance` | `0px` (RUNTIME-ONLY) | The list space the open-keyboard composer covers — measured by the tracker (list bottom minus stuck composer top; it exceeds the inset whenever the chat's box extends past the fold) — reserved as scroll room under the messages so the rescued last row can rest above the composer; removed again when the keyboard closes |
| `--tm-chat-bubble-in-bg` / `-in-fg` / `-in-border` | `--tm-bg-muted` / `--tm-color-text` / `transparent` | Incoming bubbles (also the built-in rows) |
| `--tm-chat-bubble-out-bg` / `-out-fg` / `-out-border` | `--tm-color-primary` / `--tm-color-primary-contrast` / `transparent` | Outgoing bubbles |
| `--tm-chat-bubble-system-bg` / `-system-fg` | `--tm-bg-muted` / `--tm-color-text-secondary` | System rows and `Pill` |
| `--tm-chat-bubble-pending-*` | `--tm-bg-surface-secondary` set + dashed `--tm-border-color-strong` | `Pending` |
| `--tm-chat-bubble-queued-*` | `--tm-color-info-*` set | `Queued` |
| `--tm-chat-bubble-discarded-*` | muted set + `--tm-border-color` | `Discarded` |
| `--tm-chat-bubble-locked-*` | `--tm-color-primary-*` set | `Locked` |
| `--tm-chat-bubble-radius` | `--tm-radius-xl` | Corner radius (`Pill` always full) |
| `--tm-chat-bubble-padding` | token-compensated for the 1px border | Bubble padding |
| `--tm-chat-bubble-max-width` | `80%` (`100%` for `Pill`) | Width cap; set on a wrapper or `:root` to tighten the whole conversation |

## Accessibility rules

- **Exactly one live region.** `TmChat` owns a single polite region (visually hidden,
  `aria-atomic="true"`, keyed span so re-announcements land without a two-phase render).
  Templates and the component's wrappers never add a live region — no `aria-live` or
  `role="status"` from any template, including `TypingTemplate`. A host status element inside
  `BannerContent` is the host's OWN region — it does not belong to the component, and the host
  makes sure it does not duplicate the chat's announcements.
- **The host supplies the words.** The library never invents sentences about messages:
  `MessageAnnouncement` / `BatchAnnouncement` produce the text, `AnnounceAsync` announces host
  actions, and with none of them the chat is silent. `AnnounceOwnMessages` (default `false`)
  decides whether the user's own messages are announced.
- **Typing is never announced.** The indicator is visual only. `TypingAriaHidden=true` hides the
  built-in indicator from AT entirely; a `TypingTemplate` sets its own visibility and must not
  contain a live region.
- **The messages container is a named `role=region` with `tabindex=0`** — keyboard-focusable
  scrollable region (WCAG 2.1), deliberately **not** `role="log"` (log implies `aria-live` and
  would make a second live region). Give `MessagesAriaLabel` a **unique label per page** when a
  page hosts several chats: the container is a named landmark and a Tab stop, so two chats
  labelled "Messages" are indistinguishable in the landmark list.
- Messages are focus targets — the wrapper gets `tabindex="-1"` when `FocusMessageAsync(id)`
  focuses it (the `chat.js` scroller applies the attribute, scrolls with `block:'nearest'` and
  honours reduced motion).
- Custom send buttons: mirror `composer.CanSend`; announce host outcomes through
  `AnnounceAsync("Draft approved")`.

## Scroll modes

`AutoScroll` (default **`WhenAtBottom`** — the one scrolling default change; the pre-2.10
`ScrollToBottomAsync` was a no-op):

| Value | Behavior |
|-------|----------|
| `WhenAtBottom` (default) | First render lands at the bottom; afterwards the list scrolls only while the user is actually at the bottom (the `chat.js` scroller reports it). Messages arriving above the reading position count behind the jump-to-new button instead of stealing the position; the user's **own** messages always scroll. A prepend of older history never scrolls — the visual position is preserved. |
| `Never` | The component makes no scroll calls at all (the pre-2.10 behavior). `ScrollToBottomAsync` / `ScrollToMessageAsync` still work when called explicitly; the jump button does not render. |
| `Always` | Pin to the bottom on every message change, regardless of the reading position. |

- **Load older:** wire `OnLoadOlder` + `HasMoreBefore`; the host prepends the page, the component
  detects the prepend (first id changed, last stayed) and keeps the visual position.
  `IsLoadingOlder` mirrors the host's in-flight state.
- **Nothing scrolls before interactivity** — during prerender the JS scroller is not attached and
  every scroll path is a no-op; the first interactive render lands at the bottom.
- **Explicit helpers are never suppressed:** `ScrollToBottomAsync()` (clears the unseen count and
  hides the jump button) and `ScrollToMessageAsync(id)` work in every mode; both are dispatcher
  safe.

## Layout notes

- `FillParent=true` makes the chat a flex child of the host layout (`tm-chat--fill`): the
  parent owns the height and the internal scroller (`min-height: 0`) absorbs the difference. The
  modifier reads `--tm-chat-min-height` with a **zero** fallback, so a floor declared on a
  wrapper or `:root` keeps applying in fill mode — set the knob to `0` when the parent should own
  the height entirely. Inside a plain block container keep the default.
- Below 640 px of the **chat's own width** the composer and the thread reply row become sticky
  (`bottom: var(--tm-chat-composer-offset)`), and the thread panel renders as a full-width
  overlay. Hosts with bottom navigation set `--tm-chat-composer-offset` to the navigation height.
- Coarse pointers get 44 px targets (send button, actions, jump button) and the composer input
  steps up to `--tm-font-size-md` (1rem) — 0.875rem body text is too small under a finger.
- **The iOS virtual keyboard (visualViewport) is a component feature now — opt-in
  `TrackVirtualKeyboard="true"` (FZE-13).** The lift applies ONLY to a narrow chat (< 640 px of
  the chat's own width — the sticky-composer breakpoint above); a desktop/tablet chat has no
  sticky composer to lift. iOS ignores `interactive-widget`: an opening keyboard there shrinks
  only the VISUAL viewport, and `position: sticky` alone still pins to the layout viewport and
  would leave the composer under the keyboard — the tracker is what handles the keyboard there.
  When the parameter is on, the `chat.js`
  `attachViewportTracker(root, { scroller })` listens to `visualViewport` `resize` **and**
  `scroll` (iOS moves the viewport on scroll instead of resizing it), coalesces both through
  `requestAnimationFrame` (one style write per frame) and — only on a CHANGE — writes the covered
  gap `round(max(0, innerHeight - (viewport.height + viewport.offsetTop)))` as the RUNTIME knob
  `--tm-chat-keyboard-inset` into the chat root's inline style, plus a
  `data-tm-keyboard-open` marker. Shrinks below 80 px (a collapsing browser toolbar) are
  ignored — `minInset` is adjustable through the JS option — and so is a ZOOM: while
  `viewport.scale` sits off 1 the tracker keeps the current state (a pinch zoom is the user
  resizing, not a keyboard), a degenerate (< 2 px) viewport measures nothing, and sub-pixel
  fractions are rounded so they cannot churn the inline style. While the keyboard is CLOSED the
  narrow-chat sticky composer and thread reply row rest at `bottom:
  calc(var(--tm-chat-composer-offset) + var(--tm-chat-keyboard-inset))` (the knob's 0px
  fallback collapses that to the plain composer offset); while the `data-tm-keyboard-open`
  marker is set they rest at `bottom: max(var(--tm-chat-composer-offset),
  var(--tm-chat-keyboard-inset))` — the keyboard covers the host's bottom navigation, so the
  LARGER of the two offsets wins instead of their sum — and the safe-area padding is dropped
  (the keyboard covers the home indicator). The knob is RUNTIME-ONLY: the library never declares
  it, the inline value is removed (not zeroed) when the keyboard closes, and a host override on a
  wrapper or `:root` keeps cascading. The stuck composer covers MORE of the message list than the
  inset whenever the chat's box extends past the fold, so the tracker ALSO measures the covered
  list space into a second runtime knob, `--tm-chat-composer-clearance` (list bottom minus stuck
  composer top, re-measured per tracked event): while the marker is set the messages list
  reserves it as scroll room (`padding-bottom`) and `scroll-padding-bottom`, so the rescued last
  row can land ABOVE the composer even when the messages themselves do not overflow the list.
  When the keyboard OPENS and the user was reading at the bottom (the scroller's `isPinned()`
  belief first, the measured `isAtBottom()` as the fallback), the scroller is re-landed so the
  last row's bottom rests exactly on the stuck composer's top edge — pure JS, no .NET round
  trip; a reader of older history keeps their position. Android: iOS ignores `interactive-widget` — the tracker is what
  handles the keyboard there. On Android WITH the recommended
  `interactive-widget=resizes-content` viewport meta (`<meta name="viewport"
  content="width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content">`)
  the LAYOUT viewport shrinks instead, the measured inset comes out ≈ 0 and the tracker never
  writes; on Android WITHOUT it (Chrome's default `resizes-visual`) the visual viewport shrinks
  like on iOS and the tracker lifts the composer too. Keep the meta regardless — it is the
  correct mobile viewport declaration.

## Long conversations: the render window (FZE-14)

A conversation with thousands of messages does not render thousands of `.tm-chat__item` rows —
with **`RenderWindowSize="N"`** (default `null` = render everything, byte-identical markup) the
component renders a contiguous `[Start, count)` slice of the root sequence: the TAIL. The window
always runs to the tail and only ever **grows at the top**, by **`RenderWindowStep`** rows
(default 50; the size clamps to a minimum of 20, the step to a minimum of 10):

- **Top edge:** reaching the top of the DOM list (the scroller's near-top report, or the built-in
  *Load older* affordance — visible while `Start > 0` **or** `HasMoreBefore`) grows the window by
  one step. The near-top report is verified against the live geometry TWICE, 300 ms apart (the
  `chat.js` scroller handle's `isNearTop()` getter): a transient scroll clamp or a mis-aimed
  smooth scroll during a huge window render holds the viewport in the top band for a fraction of
  a second, and that momentary report must not mount thousands of rows. Because rows are only
  ever ADDED above the reading position, the expansion is for the
  DOM exactly a *prepend* — the `chat.js` scroller restores the position on its snapshot anchor,
  so the FZE-6 scroll contract keeps holding untouched. The built-in *Load older* click expands
  the window the same way; the host's `OnLoadOlder` page load fires only once the window already
  covers the whole sequence (`Start == 0`) and `HasMoreBefore` is set.
- **Appends:** with the viewport pinned to the bottom the window slides with the tail (the top is
  trimmed back to `count - N`); while the user reads ABOVE the bottom nothing is trimmed — the DOM
  above the viewport must not change — and the trim happens when the scroller reports the return
  to the bottom (including behind the jump-to-new button's click — the click trims immediately,
  it does not wait for the report). The return-to-bottom trim
  reclaims only that AWAY-APPEND excess and REFRESHES the tracked keep-id onto the trimmed start,
  so a later parameter set cannot snap the window back; a deep-link or top-edge EXPANSION mounts
  its rows deliberately and drops the claim, so it survives its own restore's at-bottom report
  and persists until the next pinned append slides the window back to the tail. A row VANISHING
  from the list (the tracked window head removed) keeps the start — only a PINNED append falls
  back to the tail.
- **Host prepends** (a loaded older page) keep the previously-first rendered row rendered and
  expand one step above it.
- **Protected rows:** the row under inline edit and the root of the open thread panel are never
  trimmed out of the window.
- **Deep links:** `ScrollToMessageAsync(id)` / `FocusMessageAsync(id)` for an anchor above the
  window expand the window to include it (a render prepend), WAIT for that render to apply
  (a `TaskCompletionSource` completed by the after-render bookkeeping of a render that actually
  mounted rows; concurrent deep links SHARE that one wait, and dispose completes it too)
  and only then run the JS navigation — an anchor outside the window is still found and focused,
  and the expansion is not reclaimed by the restore's own return to the bottom.
- **The thread panel is deliberately NOT windowed** — threads are short, and the panel renders
  the root message a second time (page-wide `data-message-id` duplication is already the norm).

Everything data-driven is untouched by the window: announcements and the jump-to-new count read
`Messages`, read receipts and the draft guard run over the data, and
`ChatMessageContext.Index` / `IsFirstInGroup` are computed over the FULL sequence before the
slice — grouping does not change at the window edge.

```razor
@* A 100k-message inbox: the DOM holds the last 100 rows and grows by 50 per top-edge visit *@
<TmChat Messages="@_history" CurrentUser="@_user"
        RenderWindowSize="100" RenderWindowStep="50"
        OnLoadOlder="LoadOlderPageAsync" HasMoreBefore="_hasMore" />
```

### ADR: why not `<Virtualize>`

`<Virtualize>` was rejected (decision `TMCHAT-RENDER-WINDOW`) because its model fights this
component's contract: it is top-anchored with fixed or ESTIMATED row heights, so its spacers
guess where the content sits — and the chat list runs with `overflow-anchor: none` plus its own
anchor restore, which the spacers would fight over the same pixels; rows OUTSIDE the virtualized
window do not exist in the DOM, which breaks the `data-message-id` anchors
(`ScrollToMessageAsync`/`FocusMessageAsync` could not find them without mounting), the
`focusMessage` focus targets and any host reading rows from the DOM; and the chat needs
`@key`ed row elements that SURVIVE a prepend (a host `MessageTemplate` keeps state in them),
which a top-anchored spacer model does not promise. The render window keeps every row a real,
keyed DOM element while it is in the slice and reuses the scroller's own prepend machinery for
the growth — one mechanism, no competing layout heuristics.

## The typed wrapper: `TmTypedChat<TItem>` (FZE-15)

**Decision (ADR):** the non-generic `TmChat` stays, and the generic variant is a SEPARATE
component composed OVER it — `TmTypedChat<TItem>` renders an inner `<TmChat>` and owns nothing
but the mapping. It does not duplicate the draft guard (the inner chat registers
`ITmDraftGuard`), ships no JS of its own (the inner chat's `chat.js` handles it) and is not
`IAsyncDisposable` (it holds no timers, modules or handles — the inner chat disposes itself).
Making the CORE generic was rejected (the closed decision above); a generic WRAPPER costs
nothing and keeps every existing `<TmChat>` compiling unchanged.

The wrapper's whole API is three members plus the splat:

- `Items` (`[EditorRequired] IReadOnlyList<TItem>`) — the host's domain sequence, rendered in
  order.
- `MessageSelector` (`[EditorRequired] Func<TItem, ChatMessage>`) — maps one item to the
  message the inner chat renders. It runs in `OnParametersSet`, **once per new item reference**:
  an unchanged reference reuses the previously mapped `ChatMessage` instance (a
  `ReferenceEqualityComparer` cache keyed by the item, evicted when its item disappears), so
  Blazor's `@key`ed row matching keeps the row's component instances alive across re-renders.
  The cache saves MAPPING WORK only — the selector must depend on nothing but the item handed
  to it, and replacing an item (a different reference for the same row) or replacing the
  selector forces the affected rows to be re-mapped. The comparison is `Equals`, so a fresh
  delegate of the same instance/static method (the Razor compiler builds one per render for a
  method group like `MessageSelector="ToChatMessage"`) does not pay a re-map on every render.
  Value-type items — and a null item — have no identity and are re-mapped on every parameter
  pass. When the
  selector returns a message with a null `Payload`, the wrapper stores the ITEM as the payload —
  so even a plain (non-generic) `TmChat` `MessageTemplate` can read it back with
  `context.PayloadAs<TItem>()`; a payload the selector sets itself wins.
- `ItemTemplate` (`RenderFragment<ChatItemContext<TItem>>?`) — the typed message template.
  `ChatItemContext<TItem>` carries the `Item` (recovered from the mapped message's payload,
  exact type — no second lookup dictionary) plus the full `ChatMessageContext` and the
  `IsOwn` / `InThread` / `Index` / `IsFirstInGroup` / `DefaultContent` shortcuts. Without an
  `ItemTemplate` the built-in rows render for every item.
- `UseTemplateFor` (`Func<TItem, bool>?`) — the typed predicate with exactly the precedence
  `TmChat.UseTemplateFor` documents (null → template for every item). The predicate receives
  the item recovered from the payload. A message whose mapped payload is NOT a `TItem` — the
  selector supplied its own payload — never renders the typed template: it renders the
  BUILT-IN row, with or without a predicate (the wrapper always passes the inner
  `m => m.Payload is TItem i && (UseTemplateFor?.Invoke(i) ?? true)` when an `ItemTemplate`
  exists), so a selector-owned payload can never reach the template as `default(TItem)`.
- `AdditionalAttributes` (`[CaptureUnmatchedValues]`) — **every other `TmChat` parameter is
  forwarded by splatting**: `OnSend`, `ComposerTemplate`, `AutoAttachComposerInput`,
  `RenderWindowSize`, … pass them on `TmTypedChat` and they reach the inner chat. There is NO
  IntelliSense for the forwarded parameters and the compiler cannot check them — a typo surfaces
  as an unmatched attribute instead of a compile error. `Messages`, `MessageTemplate` and
  `UseTemplateFor` are OWNED by the wrapper: passing any of them through the splat throws an
  `ArgumentException` naming the conflicting attribute (they would silently fight the values
  the wrapper derives from `Items` + `MessageSelector`).
  `TmTypedChat` also validates every forwarded value against the `TmChat` parameter it names
  and throws a descriptive `ArgumentException` from `OnParametersSet` when the type cannot
  reach it — instead of letting the chat die on an `InvalidCastException` while rendering.
  **EventCallback pitfall:** for an attribute that matches no parameter the Razor compiler has
  no target type, so a bare method group (`OnSend="HandleSend"`) compiles to the handler's
  NAME as a plain string — the error names the attribute, the expected type and the factory
  form to write: `OnSend="@(EventCallback.Factory.Create<ChatSendRequest>(this, HandleSend))"`.
  Values whose type already matches (bools, strings, ints, enums, render fragments) pass
  through unchanged.

Public helpers delegate to the inner chat — `Chat` (the `TmChat?` instance),
`ScrollToBottomAsync()`, `ScrollToItemAsync(TItem)` / `FocusItemAsync(TItem)` (the id resolved from the
selector cache first — the host's selector does not re-run for an already-mapped item),
`ScrollToMessageAsync(string)`, `AnnounceAsync(string)`,
`FocusComposerAsync()`, `HasUnsavedDraft` and `DiscardDraftAsync()` — each a no-op (or `false`)
before the first render, never a throw.

```razor
@* A support-ticket thread built from the host's own domain record *@
<TmTypedChat Items="_ticketEvents" MessageSelector="MapTicketEvent"
             OnSend="@(EventCallback.Factory.Create<ChatSendRequest>(this, HandleSend))"
             Placeholder="Reply to the ticket thread…">
    <ItemTemplate Context="ctx">
        <TmChatBubble Variant="@(ctx.Item!.State == "open" ? ChatBubbleVariant.Pending : ChatBubbleVariant.Pill)"
                      ActorLabel="@ctx.Item.Actor"
                      Timestamp="ctx.Message.Message.Timestamp">
            <Badge><span class="ticket-badge">#@ctx.Item.Id · @ctx.Item.State</span></Badge>
            <ChildContent><span>@ctx.Item.Message</span></ChildContent>
        </TmChatBubble>
    </ItemTemplate>
</TmTypedChat>
@code {
    private sealed record SupportTicketEvent(int Id, string Actor, string Message, string State, DateTimeOffset Timestamp);

    private ChatMessage MapTicketEvent(SupportTicketEvent e) => new(
        $"ticket-{e.Id}", e.Message, _agents[e.Actor], ChatMessageType.Incoming, e.Timestamp);
}
```

Demo: the `chat-typed-section` of the `/chat` demo page; bUnit coverage in
`TmTypedChatTests`; E2E in `ChatTypedE2ETests`.
