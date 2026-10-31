# AI conversation (`TmAiConversation`)

`TmAiConversation` is the transcript of an AI conversation plus the machinery around it: the scroll
container, the render window of a very long history, the "stop generating" affordance, the "new
messages" pill and the one polite live region of the component. The turns themselves are rendered by
`TmAiMessage` (or by the host's `MessageTemplate`), so a conversation is composed of components the
package already ships.

The delta stream is the host's: the component never references an LLM SDK. A host appends a turn to
its own `Messages` list and hands the component an `IAsyncEnumerable<AiStreamDelta>`; the tokens, the
model records and `AiMessageStreamApplier` live in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models`
/ `.Services`), so a backend can produce the stream without Blazor or Markdig.

```razor
@using Tempo.Blazor.Components.AI

<TmAiConversation @ref="conversation"
                 Messages="@messages"
                 Suggestions="@suggestions"
                 OnSuggestion="@(text => SendAsync(text))"
                 OnStop="@(() => composer.Reset())"
                 Height="32rem" />

@code {
    private TmAiConversation conversation = default!;

    private async Task SendAsync(string prompt)
    {
        messages.Add(new AiMessage { Role = AiMessageRole.User, Parts = { new AiTextPart(prompt) } });
        var answer = new AiMessage { Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
        messages.Add(answer);
        await conversation.StreamAsync(answer, client.StreamAsync(prompt));
    }
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Messages` | `IReadOnlyList<AiMessage>?` | `null` | The turns, oldest first. The host owns the list: append a turn and the component renders it. Reassigning the property to a **new list instance** counts as loading another conversation (the window snaps to the newest turns); appending to the same instance keeps the window where the reader left it. |
| `MessageTemplate` | `RenderFragment<AiMessage>?` | `null` | Replaces the built-in `TmAiMessage` of every turn, so a host attaches its own per-turn actions without the conversation re-declaring them. |
| `Suggestions` | `IReadOnlyList<string>?` | `null` | Prompts offered by the empty state. They render only while the conversation has no turn **and** `OnSuggestion` is bound. |
| `OnSuggestion` | `EventCallback<string>` | — | Raised with the text of an activated suggestion prompt. |
| `OnStop` | `EventCallback` | — | Raised once after a running generation was stopped (by the built-in control or by a host calling `StopAsync`). The component cancels the token itself, so a handler must not call `StopAsync` again — it is a no-op, so it cannot loop. |
| `RenderWindowSize` | `int?` | `null` | How many of the newest turns are rendered; `null` renders every turn. Values below 10 are raised to 10. |
| `RenderWindowStep` | `int` | `50` | How many turns one "Load older messages" activation adds. Raised to at least 10. |
| `RenderThrottle` | `TimeSpan` | `50 ms` | Minimum time between two renders of a streamed answer. Zero or negative renders every applied delta. |
| `ShowStop` | `bool` | `true` | Offer the built-in "Stop generating" control while a stream runs. A host with its own stop control passes `false` and calls `StopAsync`. |
| `AutoScroll` | `bool` | `true` | Follow new content and offer the "New messages" pill. `false` imports no browser module at all — the host owns the scrolling surface. |
| `Height` | `string?` | `null` | CSS length of the transcript (`"32rem"`, `"100%"`). `null` uses the stylesheet's height. |
| `Class` | `string?` | `null` | Extra CSS class(es) on the root `.tm-ai-conversation`. |
| `DataTestId` / `TestIdPrefix` | `string?` | `null` | Inherited from `TmComponentBase`; the default root test id is `ai-conversation`. Internal ids are `ai-conversation-log`, `-empty`, `-suggestion`, `-load-older`, `-new-messages`, `-stop`, `-announcement`. |

The root carries `data-generating` (`true`/`false`), which is the stable hook for a host's own
styling and for E2E.

Public members: `IsGenerating`, `StreamAsync(AiMessage, IAsyncEnumerable<AiStreamDelta>,
CancellationToken)`, `StopAsync()`, plus the two `[JSInvokable]` scroll reports `OnScrollStateAsync`
and `OnContentResizedAsync`.

## Streaming

`StreamAsync` folds the deltas into the message the host already appended:

- Each delta goes through `AiMessageStreamApplier`, so a text delta appends to the trailing text part,
  a tool-call delta opens/updates the call and a completed delta finishes it — the same applier a host
  would use without the component.
- **Renders are throttled** to `RenderThrottle` on the injected `TimeProvider`: a deltas arriving
  faster than the interval is folded into the message but **not painted**, because a Server circuit
  re-rendering per token is a render storm. The stream always renders once when it starts (the Stop
  affordance must not wait for the first token) and once when it ends.
- **The end of the stream is the end of the answer.** A transport that simply completes marks the
  message `Completed`; a host does not have to append a trailing `AiCompletedDelta`.
- **Stop** cancels the token handed to the host's stream, keeps the partial answer and marks it
  `Stopped`. A late token cannot resurrect a stopped answer — the loop stops applying deltas the
  moment the token is cancelled. A token the host cancels itself stops the stream the same way but
  does not raise `OnStop` (a host must not receive its own cancellation echo).
- **A throwing stream** marks the message `Failed` with the exception message and then rethrows, so the
  host's own error handling still runs.
- A second `StreamAsync` while one runs throws `InvalidOperationException` — two streams on one
  transcript would orphan the first.

The clock is resolved as `Services.GetService<TimeProvider>() ?? TimeProvider.System`; the package
never registers one, so a host (and a test with `FakeTimeProvider`) always has the last word.

## Scrolling

The component owns the scroll container, the browser owns the geometry. The package's ES module
`_content/Tempo.Blazor.AI/js/ai-conversation.js` (imported through `IJSObjectReference`, no `<script>`
tag, no `eval`) attaches to the log element and reports two things back:

- `OnScrollStateAsync(bool)` when the reader crosses the near-bottom band themselves, and
- `OnContentResizedAsync(bool)` when the transcript's **content** changes size (a streamed token, an
  image that finished loading, an expanded panel) — a scroll handler cannot see that, and it is
  exactly the case where a detached reader must not be dragged back to the bottom. It reports the
  position the reader had **before** the growth: a reader sitting at the bottom leaves the bottom band
  the instant a token makes the content taller, so a measurement taken after the growth would answer
  "detached" for the reader who is watching the answer arrive.

New content is followed **only while the reader is at the bottom** (a 48 px tolerance, so sub-pixel
rounding does not read as "scrolled up"). A reader who scrolled back gets the "New messages" pill
instead of a moving viewport, and is scrolled only when they press it. The pill floats over the
scroller, so an appearing control never reflows the text being read.

Once the module has reported a position, that report is the state of record — re-probing the geometry
on every render would be a Blazor round-trip per parent render. The probe is the fallback for a
transcript the module has not reported on yet (the freshly attached one).

**The module never reports its own scrolling.** Following the newest turn is one smooth
`scrollToBottom`, and its first frames are still far from the bottom — it starts where the reader was
while the content has already grown. Reported as a crossing, those frames would tell the component to
stop following content that is still arriving (flicker, plus the "New messages" pill for a reader who
never scrolled). The module therefore marks the scroll as its own and stays quiet until it lands;
the mark is released by `scrollend`, by the landing frame, or by a 500 ms fallback timer where
`scrollend` is not implemented. The first real gesture — `wheel`, `touchstart`, `pointerdown`,
`keydown` — releases the mark immediately, so a reader who takes the scroll over is still reported,
even in the middle of the animation. `detach` removes every listener and the pending release.

Smooth scrolling is requested only when the reader's system does not ask for reduced motion; a host
that passes `AutoScroll="false"` imports no module at all. A static rendering (prerender) has no
interop: the transcript still renders and the scroll tracking is best effort.

## Long conversations

`RenderWindowSize` keeps a very long transcript's DOM small: the component renders the contiguous
**tail** and offers "Load older messages", which grows the window upwards by `RenderWindowStep`. The
window is a plain slice of the host's list — deliberately not `<Virtualize>`, which would unmount rows
the reader is looking at and break the message anchors and focus. The rows hold the host's own
`AiMessage` instances, so a streamed turn keeps growing in place.

The window follows appended turns until the reader pulls the older ones in; after that it grows at the
bottom instead of unmounting the rows above the viewport, and survives a parameter-only re-render (the
reader's expansion is a choice, not a derived default). A shrinking list clamps it, a replaced list
resets it to the new tail. The default is `null`: no window, every turn rendered.

## Accessibility

- **One live region.** The transcript is `role="log"` with `aria-live="polite"`,
  `aria-atomic="false"` and `aria-relevant="additions"`. Reducing the relevance to *additions* is the
  point: a log with the default `additions text` announces every streamed token, so a screen reader
  would read the answer character by character. Inserted turns and the completion note are what gets
  announced; the text of a reply is read on demand.
- **Completion is announced once.** The note ("Answer complete") is a node keyed by an announcement
  sequence, so a second completion replaces the node instead of silently changing the text of an
  existing one (a live region ignores an unchanged text node); a stopped or failed stream announces
  nothing extra.
- **`aria-busy`** is set on the transcript while a generation runs, so assistive technology waits for
  the answer instead of narrating the intermediate renders.
- **Keyboard.** The transcript is `tabindex="0"` with its own `:focus-visible` ring — a scroll
  container a keyboard-only reader cannot reach locks them out of every earlier turn (WCAG 2.1.1).
  Every control is a native `<button>` (no emulated key activation, one activation per key press).
- **Focus is never dropped.** Every control the reader's own activation REMOVES hands the focus to the
  transcript instead of `<body>` (WCAG 2.4.3): "Load older messages" when the whole history is now
  rendered, the "New messages" pill when it dismisses itself, and the "Stop generating" pill when the
  generation ends. A control that is still rendered keeps the focus the reader put on it, and a host
  that stops a stream programmatically moves no focus — nothing disappeared under the reader.
- **Touch** — every pill, chip and the "Load older messages" button reaches the 44 px
  `--tm-touch-target` floor on a coarse pointer.
- **Forced colors** — the surfaces opt out of the automatic adjustment and ask for `Canvas` /
  `CanvasText`; the Stop pill keeps a `Highlight` background, so its ink is `HighlightText`, and the
  focus rings ride `Highlight` (a `Highlight`-coloured label over `Canvas` has no contrast guarantee).
  Every `:hover` rule has a counterpart inside the forced-colors block — a class plus a pseudo-class
  outranks the block's plain class selectors, so without it the theme tokens would come back under the
  pointer (an invariant `TmAiConversationCssTests` enforces over the file).
- **Reduced motion** — the stylesheet declares no animation or transition of its own (the streaming
  caret and the thinking shimmer belong to `_ai-streaming.css` / `_ai-thinking.css`), and the module's
  smooth scroll is dropped when the system asks for reduced motion.

## Localization

Keys in `TmResources{,.cs,.sk,.fr}.json`: `TmAiConversation_Transcript` (the log's accessible name),
`_EmptyTitle`, `_EmptyDescription`, `_Suggestions`, `_LoadOlder`, `_NewMessages`, `_Stop`, `_Completed`.

## Assets

- `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` imports
  `components/_ai-conversation.css` (tokens only — no colour literal, no raw palette step, no
  `[data-theme]` block).
- `_content/Tempo.Blazor.AI/js/ai-conversation.js` — the ES module: `attach`, `detach`,
  `isNearBottom`, `scrollToBottom`. Every entry point is null-safe (a component that lost its root
  element gets a quiet `false`, never a throw), and a host CSP needs no `unsafe-inline` for it. Its
  Node tests live in `wwwroot/js/__tests__/ai-conversation.test.mjs` (`npm run test:ai`) and cover the
  own-scroll suppression, the gesture / `scrollend` / fallback release and the `detach` cleanup.
