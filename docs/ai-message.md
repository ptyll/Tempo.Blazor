# AI message (`TmAiMessage`)

`TmAiMessage` renders one turn of an AI conversation — the user's prompt or the model's answer — and
owns everything around the answer text: the avatar slot, the ordered parts, citation chips with a
source preview, the source cards, the follow-up chips and the action toolbar (copy, regenerate with a
version switcher, thumbs up/down with an optional reason, read aloud).

The answer text itself is never rendered by this component: it goes through `TmMarkdownView`, i.e.
through the safe Markdig pipeline documented in [docs/ai-markdown.md](ai-markdown.md). The token,
model and message records live in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models`), so a backend
references them without Blazor or Markdig.

```razor
@using Tempo.Blazor.Components.AI

<TmAiMessage Message="@answer"
             OnRegenerate="@(() => conversation.RegenerateAsync(answer.Id))"
             OnFeedback="@(feedback => client.RateAsync(answer.Id, feedback))"
             OnFollowUp="@(text => composer.SubmitAsync(text))"
             OnRetry="@(() => conversation.RetryAsync(answer.Id))"
             ActiveVersionIndexChanged="@(index => answer.ActiveVersionIndex = index)" />
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Message` | `AiMessage?` | `null` | The turn to render. A null value renders an empty shell instead of throwing, so a host can place the component before the first token arrives. |
| `ShowAvatar` | `bool` | `true` | Render the avatar slot. The slot is decorative (`aria-hidden`). |
| `AvatarTemplate` | `RenderFragment?` | `null` | Replaces the default role glyph inside the avatar slot. |
| `ShowActions` | `bool` | `true` | Render the action toolbar. The bar itself is offered only for a finished turn (completed, stopped or failed). |
| `ActionsTemplate` | `RenderFragment?` | `null` | Replaces the whole built-in bar with host actions (the host then owns the keyboard model). |
| `ShowSources` | `bool` | `true` | Render the cited sources as cards, in part order. |
| `ShowFollowUps` | `bool` | `true` | Render `Message.FollowUps` as chips. |
| `ReadAloud` | `bool` | `false` | Offer the "Read aloud" action (feature-detected `speechSynthesis`; hidden when the browser has none). |
| `OnRegenerate` | `EventCallback` | — | Raised when the reader asks for another generation. The action renders only when a handler is bound. |
| `OnFeedback` | `EventCallback<AiFeedback>` | — | Raised with the rating; a negative rating is raised again with the optional reason. The thumbs render only when a handler is bound. |
| `OnFollowUp` | `EventCallback<string>` | — | Raised with the suggestion text of an activated chip. |
| `OnCitationClick` | `EventCallback<string>` | — | Raised with the source id of an activated citation chip, after the built-in preview opened. |
| `OnRetry` | `EventCallback` | — | Raised when the reader retries a failed generation. |
| `ActiveVersionIndexChanged` | `EventCallback<int>` | — | Raised with the target zero-based version index. The component never mutates the model. |
| `Class` | `string?` | `null` | Extra CSS class(es) on the root `.tm-ai-message`. |
| `DataTestId` / `TestIdPrefix` | `string?` | `null` | Inherited from `TmComponentBase`; the default root test id is `ai-message`. |

The root element carries `data-role` (`user`, `assistant`, `system`, `tool`) and `data-status`
(`pending`, `streaming`, `completed`, `stopped`, `failed`), which is the stable hook for a host's own
styling and for E2E.

## Versions (regenerate)

A message keeps the live answer in `AiMessage.Parts` and the superseded generations in
`AiMessage.Versions`; `AiMessage.VersionCount` is therefore `Versions.Count + 1` and
`AiMessage.ActiveVersionIndex` selects the rendered one (the last index is the live parts). The
component renders the "2/3" switcher whenever more than one version exists. The switcher shows the
chosen version immediately (from `Versions`, without waiting for a host round-trip) and *raises* the
target index; the model stays the source of truth, so a host that writes `ActiveVersionIndex` itself
wins over the reader's last step:

```csharp
answer.Versions.Add(new AiMessageVersion(answer.Parts, answer.CreatedAt, answer.Model));
answer.Parts.Clear();            // the host starts the new stream
answer.ActiveVersionIndex = answer.VersionCount - 1;
```

An index outside the valid range is clamped, so a host that swaps the list and the index in two
renders never throws.

## Feedback

`AiFeedback` is `{ Rating, Reason?, CreatedAt }` with `Rating` one of `None`, `Positive` or
`Negative`. The thumbs render `aria-pressed` from `Message.Feedback`, falling back to the rating the
component just raised (so the control is honest even before a host round-trip).

- Thumbs up raises `OnFeedback` immediately.
- Thumbs down raises `OnFeedback` immediately **and** opens a dialog with an optional reason; submitting
  the reason raises `OnFeedback` a second time with `Reason` filled. Cancelling (or Escape, or a click
  outside) keeps the rating. An explanation is never required.

**The second call REPLACES the first, it is not a second vote.** A thumbs-down therefore produces two
`OnFeedback` invocations for one rating, and a host that aggregates feedback must key on the message id
(or `AiMessage.Id`) and let the later `AiFeedback` overwrite the earlier one — counting every invocation
would double-count every explained downvote. Feeding the value straight into `Message.Feedback` is
already correct: the assignment is idempotent.

## Citations and sources

The answer's `[1](cite:<sourceId>)` links arrive as citation chips from the markdown pipeline. Activating
a chip resolves the source through `AiMessage.FindSource(id)` in the **active** version, opens the
source preview dialog (title, domain, snippet, and an "Open source" link) and raises `OnCitationClick`
so a host can react as well. `AiSourcesPart` entries render as numbered source cards in part order.

Both the card link and the preview link pass through the package's URL allowlist
(`AiMarkdownUrlPolicy`): a refused scheme is rendered as plain text rather than a dead link, and every
surviving link gets `target="_blank"` with `rel="noopener noreferrer nofollow"`.

## Lifecycle states

| Status | Rendering |
|--------|-----------|
| `Pending`, `Streaming` | The body is `aria-busy="true"`, the last text part carries the package's `.tm-ai-streaming` caret, an empty streaming message shows the localized "Generating an answer" line, and the action bar is not offered (there is nothing to copy or rate yet). |
| `Completed` | The action bar with copy, regenerate, read aloud and the thumbs. |
| `Stopped` | The answer stays, plus the localized "Generation stopped" label. The transition from streaming is announced through the message's live region (a reader waiting on the answer is not touching the page). No retry (the reader stopped it on purpose). |
| `Failed` | `role="alert"` error block with `Message.Error` (or the localized fallback) and a Retry action when `OnRetry` is bound. The action bar stays next to it: Retry lives in the error block, and a partial answer is still worth copying. |

## Accessibility

- **Toolbar model.** The action bar is `role="toolbar"` with `aria-label`, `aria-orientation` and a
  **roving tabindex**: exactly one control is the tab stop, Arrow Left/Right (and Up/Down) move it with
  wrapping, Home/End jump to the ends, and focus follows the tab stop through
  `ElementReference.FocusAsync`. No second JS gesture module is involved — the pattern
  `TmEmailVariablePicker` uses. The version switcher is a separate `role="group"` with native buttons
  (Tab reaches it), and the prompt's keyboard model is not claimed for a host `ActionsTemplate`.
- **Copy** announces through a sibling `role="status"` live region ("Copied") and never renames the
  button; the clipboard write is best effort — a refused write leaves the control in its resting state.
- **Popovers** are the core `TmOverlayPanel` (top layer, `role="dialog"` with an accessible name,
  Escape and outside-click dismissal, `ConstrainHeight` so a long snippet stays reachable). They are
  siblings of the toolbar, never children, so a keydown inside the reason textarea never travels through
  the toolbar's roving handler.
- **Focus order.** Both popovers take focus into themselves through `TmOverlayPanel.OnOpened` (the only
  hook that runs after the panel is displayed — a focus move against a still-hidden popover is dropped):
  the source preview opens on its Close button, the reason form on its textarea (and it is a *modal*
  dialog — `TrapFocus="true"`, so Tab cannot walk out of the form into the page). Closing hands focus
  back where the reader came from: the reason form restores the thumbs-down button (and moves the roving
  tab stop with it, so the next Tab continues from there), and the preview returns to the citation chip
  that opened it — the chip lives in the markdown module's HTML, so the component asks the module to
  focus it by `data-source-id`. An **outside click deliberately does not** pull focus back: the reader
  clicked somewhere else and undoing that click is worse than a focus that follows it.
- **Touch** — on a coarse pointer every action, version button, preview close, chip and feedback button
  reaches the 44 px floor (`--tm-touch-target`).
- **Forced colors** — the avatar, cards, error block, chips and the user bubble opt out of the automatic
  adjustment and ask for `Canvas`/`CanvasText`.
- **Reduced motion** — this component adds no animation; the streaming caret is the package-level
  primitive that already carries its own `prefers-reduced-motion` block.

## Interactive state does not leak

A different `Message` instance resets every interaction state (preview, reason draft, copied flag,
speech). The copy confirmation also resets itself after two seconds so the control cannot claim a state
the reader has moved past.

## Static assets and interop

| Asset | Path |
|-------|------|
| Styles | `_content/Tempo.Blazor.AI/css/components/_ai-message.css` (imported by `tempo-blazor-ai.css`) |
| Interop | `_content/Tempo.Blazor.AI/js/ai-message.js` |

The module exposes `copyText(text)`, `isSpeechSupported()`, `speak(text, lang, dotNetRef)`,
`stopSpeaking()` and `focusCitation(root, sourceId)` (the focus return to a citation chip). It is a plain
ES module imported through `IJSObjectReference` — no `<script>` tag, no `eval`, no `unsafe-inline` in a
host CSP. The end of an utterance is reported back to the component (`OnSpeechEndedAsync`), so the "Stop
reading" toggle returns to "Read aloud" by itself. A browser without clipboard access or without speech
synthesis simply gets a quieter component, never a dead button.

## Plain-text projection

`AiMessageText.ToPlainText(markdown)` flattens an answer for the clipboard and for speech: markdown
markers are dropped while the block structure survives (one line per paragraph, list item, code line and
table row, blank lines between blocks). Link and image targets never travel — only their label (or alt
text). The walk runs over the same safe pipeline as the renderer, so raw HTML stays literal text.
