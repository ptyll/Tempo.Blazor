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
a carry-forward, not part of this release.

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
| `ComposerTemplate` | `RenderFragment<ChatComposerContext>` | The whole composer. Drive it through `composer.Text` / `SetTextAsync` / `SendAsync`; register your own input with `composer.AttachAsync(element)` to get the built-in Enter/Shift+Enter + IME behavior and the post-send refocus. |
| `TypingTemplate` | `RenderFragment<IReadOnlyList<ChatUser>>` | The whole typing indicator. Must **not** contain a live region (see a11y rules). |
| `JumpToNewTemplate` | `RenderFragment<int>` | The *content* of the jump-to-new button (the count); the `<button>` and its behavior stay built-in. |
| `BannerContent` | `RenderFragment` | A strip above the message list (connection bar). Rendered raw: **no wrapper, no live region** — put `role="status"` inside the slot if it should be announced. |
| `HeaderContent` | `RenderFragment` | Very top of the component. An open thread panel overlays it. |
| `EmptyContent` | `RenderFragment` | The built-in empty state (root list only). |
| `ComposerAddon` / `ComposerFooter` | `RenderFragment` | Content above / below the built-in input (skipped entirely when `ComposerTemplate` is set). |
| `TmChatBubble` slots | `Badge`, `Meta`, `Note`, `Actions`, body | The building blocks of a custom bubble; `Locked` never renders the body. |

`UseTemplateFor` keeps the override surgical — e.g. `m => m.Kind is not null` lets ordinary
messages keep the built-in row while the host's special kinds render `TmChatBubble`s.

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
| `--tm-chat-composer-offset` | `0px` | Space below the composer for host chrome (bottom navigation height); the narrow sticky composer and thread reply row rest at `bottom: var(--tm-chat-composer-offset)` |
| `--tm-chat-safe-area-bottom` | `env(safe-area-inset-bottom, 0px)` | Extra padding under the sticky composer/thread row for the device safe area |
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
- Keep the sticky composer above the iOS virtual keyboard (visualViewport) **host-side** — the
  component does not listen to the visual viewport (carry-forward).

## Future work (deliberately NOT implemented)

- **Auto-attach via `id=InputId`**: a `ComposerTemplate` input carrying `id="@composer.InputId"`
  could be attached automatically (instead of the host calling `AttachAsync` from
  `OnAfterRenderAsync`). `composer.InputId` is public today for labels / `aria-describedby` /
  `aria-controls`; the auto-attach hook would be a future additive change.
- Generic `TmChat<TItem>` as a **separate** component over the same core.
- Virtualization of very long conversations (`Virtualize` around the keyed rows).
- iOS `visualViewport` keyboard tracking inside the component (see Layout notes).
