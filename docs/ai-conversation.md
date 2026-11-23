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
| `MessagePendingText` | `string?` | `null` | **2.12.0** Text of the placeholder a turn with no content yet renders while it waits for its first part (`Pending`). Flows to the built-in `TmAiMessage` as its `PendingText`; a host with its own `MessageTemplate` passes `PendingText` itself. `null` renders no placeholder (the pre-2.12 behaviour; a localized default is planned for 3.0). |
| `Generating` | `bool` | `false` | **2.12.0** Whether the host's OWN generation — one that does not travel through `StreamAsync` — is running. The component treats the window exactly like a stream: `IsGenerating` follows (the root's `data-generating`, the log's `aria-busy`, the built-in Stop pill), the true→false edge announces the completed answer when the last turn is `Completed`, and `StopAsync` raises `OnStop` so the host can cancel its own work. A host that streams through `StreamAsync` binds nothing — the component drives the state itself and the edge stays silent, so a completion is never announced twice. |
| `CompletionAnnouncement` | `Func<AiMessage, string?>?` | `null` | **2.13.0** Host formatter for the completion note spoken into the transcript live region. Invoked on the true→false generating edge and at the end of `StreamAsync`, only for the last `Completed` turn. A null return (or a null parameter) keeps `TmAiConversation_Completed`. Stopped and Failed turns never call it. |
| `Suggestions` | `IReadOnlyList<string>?` | `null` | Prompts offered by the empty state. They render only while the conversation has no turn **and** `OnSuggestion` is bound. |
| `SuggestionsLayout` | `AiSuggestionsLayout` | `Wrap` | **2.13.0** How the empty-state chips sit. `Wrap` is today's wrapping, centred row. `ScrollRow` is a nowrap horizontal scroller that reveals a partially visible focused chip without moving the transcript (inner focus ring, `outline-offset: -4px`). `Auto` is ScrollRow under 640 px via a container query on `.tm-ai-conversation`; desktop Auto stays Wrap. |
| `OnSuggestion` | `EventCallback<string>` | — | Raised with the text of an activated suggestion prompt. |
| `OnStop` | `EventCallback` | — | Raised once after a running generation was stopped (by the built-in control or by a host calling `StopAsync`). The component cancels the token itself, so a handler must not call `StopAsync` again — it is a no-op, so it cannot loop. During a host-driven window (`Generating` set, no stream) the raised stop asks the host to cancel its own work, once per window. |
| `RenderWindowSize` | `int?` | `null` | How many of the newest turns are rendered; `null` renders every turn. Values below 10 are raised to 10. |
| `RenderWindowStep` | `int` | `50` | How many turns one "Load older messages" activation adds. Raised to at least 10. |
| `RenderThrottle` | `TimeSpan` | `50 ms` | Minimum time between two renders of a streamed answer. Zero or negative renders every applied delta. |
| `ShowStop` | `bool` | `true` | Offer the built-in "Stop generating" control while a stream or a `Generating` window runs — and only while it can act: an uncancelled stream, or a window whose one stop has not been delivered yet. A host with its own stop control passes `false` and calls `StopAsync`. |
| `AutoScroll` | `bool` | `true` | Follow new content and offer the "New messages" pill. `false` imports no browser module at all — the host owns the scrolling surface. |
| `Height` | `string?` | `null` | CSS length of the transcript (`"32rem"`, `"100%"`). `null` uses the stylesheet's height. |
| `Class` | `string?` | `null` | Extra CSS class(es) on the root `.tm-ai-conversation`. |
| `DataTestId` / `TestIdPrefix` | `string?` | `null` | Inherited from `TmComponentBase`; the default root test id is `ai-conversation`. Internal ids are `ai-conversation-log`, `-empty`, `-suggestion`, `-load-older`, `-new-messages`, `-stop`, `-announcement`. |

The root carries `data-generating` (`true`/`false`), which is the stable hook for a host's own
styling and for E2E.

Public members: `IsGenerating`, `StreamAsync(AiMessage, IAsyncEnumerable<AiStreamDelta>,
CancellationToken)`, `StopAsync()`, `AnnounceAsync(string text)` (**2.13.0** — speaks into the
transcript live region as a new keyed node so an identical wording is re-announced), `ScrollToBottomAsync()`, `ScrollToMessageAsync(string messageId)`
(which answers whether the named turn actually moved), `ExpandRenderWindowToAsync(string messageId)`
(which grows a render window so the named turn is in the DOM, without scrolling), plus the two
`[JSInvokable]` scroll reports `OnScrollStateAsync` and `OnContentResizedAsync`.

## Host-driven generation (`Generating`)

A host whose answer does not arrive as an `IAsyncEnumerable<AiStreamDelta>` — its own HTTP call, its
own agent loop — binds `Generating` instead of templating its busy state around the component
(the workaround the EmusCz panel used to do itself: `setBusy`, `SyncBusyAsync` and a hidden
completion note). While the window is open the component behaves exactly as if its own stream ran:

- `IsGenerating` is true, so `data-generating`, `aria-busy` on the transcript and the built-in
  "Stop generating" pill all follow.
- `StopAsync()` (or a click on the pill) raises `OnStop` — there is no token to cancel, so the event is
  the notification that asks the host to stop its own work. It is raised **once per window**: a handler
  that calls `StopAsync` again while the window is still open cannot loop.
- The **true→false edge announces the completed answer** when the last turn is `Completed` — the same
  keyed "Answer complete" note a stream speaks, so a screen reader hears the completion no matter which
  half of the package drove the generation. A window whose answer already arrived through
  `StreamAsync` stays silent (the stream announced it), so a host that binds both — a composer busy
  flag AND the stream — hears "Answer complete" exactly once.

A `StreamAsync` call inside an open window is allowed: the busy state stays on through both halves and
the stream owns the announcement.

One flag drives both halves of the package — the composer's send/stop surface and the conversation's
busy window are two views of the same request:

```razor
@* Set on submit, cleared when the answer arrived (or was stopped). The composer disables its send
   control and offers Stop; the conversation opens the busy window — the same busy state, wait line,
   Stop pill and completion announcement a streamed answer gets. *@
<TmAiConversation @ref="conversation"
                 Messages="@messages"
                 Generating="@isBusy"
                 OnStop="@CancelRequestAsync"
                 Height="34rem" />
<TmAiComposer IsGenerating="@isBusy"
             OnSubmit="@(async submit => await SendAsync(submit.Text))"
             OnStop="@CancelRequestAsync" />

@code {
    private bool isBusy;

    private async Task CancelRequestAsync()
    {
        // The one stop of the request: cancel your own work, mark the turn Stopped, clear the flag.
        isBusy = false;
    }
}
```

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
- `OnContentResizedAsync(bool)` when the transcript's geometry changes size — the **content** (a
  streamed token, an image that finished loading, an expanded panel) and, since 2.12.0, **the log box
  itself** (the host resized the side panel, a layout split moved, the host's own height binding
  changed). One ResizeObserver watches both surfaces; the BOX half reports only when the box's own
  **height** changed — a width-only resize (a scrollbar appearing, a panel dragged wider) reflows the
  same number of lines and never disturbs the reader. When the box SHRINKS, `scrollTop` stays where it
  is and the bottom moves away (a scroll handler cannot see that), so a reader who is stuck to the
  bottom is re-aligned by the report: it carries the position the reader had **before** the change, the
  component follows the new bottom, and the re-asserting jump runs **without** animation. A detached
  reader is never dragged and never pill-ed. The position is carried **before** the change for the same
  reason the content report is: a reader sitting at the bottom leaves the bottom band the instant the
  geometry moves, so a measurement taken after the change would answer "detached" for the reader who is
  watching the answer arrive.

New content is followed **only while the reader is at the bottom** (a 48 px tolerance, so sub-pixel
rounding does not read as "scrolled up"). A reader who scrolled back gets the "New messages" pill
instead of a moving viewport, and is scrolled only when they press it. The pill floats over the
scroller, so an appearing control never reflows the text being read.

The host can drive the same scrolling from its own state: the public `ScrollToBottomAsync()` performs
the same jump the built-in pill performs — it dismisses the "New messages" pill and re-arms the follow,
so the next appended turn is followed again — and `ScrollToMessageAsync(messageId)` brings the turn with
`data-message-id` into view and answers **whether the turn actually moved** (`false` for an empty id,
without the module, for an id that is in neither the host list nor the transcript, or on an interop
failure). When a render window is in effect and the named turn sits above it, the window grows first
(stepping back from the current start by `RenderWindowStep`, the same grid as "Load older messages")
so the row is in the DOM before the module is asked to scroll; `ExpandRenderWindowToAsync(messageId)`
is the same growth without the scroll (it answers whether the id is in the host list). The scroll is
measured against the transcript itself (minus the log's `padding-top`), so the page the component sits
on never moves, and marked as the module's own scroll, so not one animation frame is reported as the
reader detaching. Unless the named turn IS the newest one, the jump also detaches the follow: the next
appended turn pops the "New messages" pill instead of being followed, and the mark's release reports the
honest landing position to .NET when it differs from the detached baseline (a jump to the newest turn
reports "at the bottom" again).

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
resets it to the new tail. A host jump to a turn above the window (`ScrollToMessageAsync` /
`ExpandRenderWindowToAsync`) expands the same way: it steps back from the current start onto the
"Load older" grid, the first rendered row is kept, a jump that lands at index 0 hands focus to the
transcript (the "Load older" control has left the DOM), and a jump that is not the newest turn
detaches the follow. The default is `null`: no window, every turn rendered.

## Accessibility

- **One live region.** The transcript is `role="log"` with `aria-live="polite"`,
  `aria-atomic="false"` and `aria-relevant="additions"`. Reducing the relevance to *additions* is the
  point: a log with the default `additions text` announces every streamed token, so a screen reader
  would read the answer character by character. Inserted turns and the completion note are what gets
  announced; the text of a reply is read on demand.
- **Completion is announced once.** The note ("Answer complete") is a node keyed by an announcement
  sequence, so a second completion replaces the node instead of silently changing the text of an
  existing one (a live region ignores an unchanged text node); a stopped or failed stream announces
  nothing extra. A host-driven window (`Generating`) announces on its true→false edge under the same
  rule — and stays silent when the answer already arrived through `StreamAsync`.
- **`aria-busy`** is set on the transcript while a generation runs — a stream or a `Generating` window —
  so assistive technology waits for the answer instead of narrating the intermediate renders.
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

## Demo

The `/ai-conversation` demo page (`src/Tempo.Blazor.Demo.SharedUI/Pages/AiConversationPage.razor`) runs the
whole contract without a model: a local `IAsyncEnumerable<AiStreamDelta>` generator produces the reasoning
steps, one successful and one failing tool call (the failed card offers Retry), a markdown table, a code
block, two cited sources and the completion, and the page attaches the per-turn actions through the
conversation's `MessageTemplate` (regenerate with a version switcher, feedback, answer retry, tool-call retry,
citation preview, follow-up chips). Its composer is wired with three slash commands, an in-memory mention
provider and attachment limits, and the page reports what a submit carried
(`data-testid="ai-demo-submission"`). The page binds the two 2.12.0 host-facing knobs: `Generating` rides the page's own busy flag,
and the
template's `TmAiMessage.PendingText` names the wait line of a turn that has not started (the page owns a
`MessageTemplate`, so the pending text rides the template — the conversation-level `MessagePendingText`
pass-through is the path for hosts without one). The host-driven controls live on the conversation's own
card, above the transcript: a "Simulate a busy answer" button runs a full window with NO stream (busy
flag, wait line, one completion announcement), the built-in Stop pill raises the page's `OnStop`, which
cancels the window's delay (it runs on the injected `TimeProvider`), marks the answer `Stopped` and
closes the flag, and a "scroll to the first turn" button exercises `ScrollToMessageAsync`. Below the
conversation
the page renders the same components on their own — every tool-call state plus a grouped run, the thinking
pill in both states, an opt-in pending placeholder sample, the reasoning panel collapsed and expanded, and a
hostile markdown sample that neutralises raw HTML and `javascript:` / `data:` / `vbscript:` links.

The page is also the E2E subject: `tests/Tempo.Blazor.E2E/AI/AiConversationE2ETests.cs` (WASM, plus a Server
smoke) drives the stream, Stop, the citation preview with its focus return, the composer's `/` combobox and
Enter/Shift+Enter contract, the Auto suggestion row at 375 px (Tab to the third chip leaves the log unmoved)
and the sticky composer clearing a 56 px `--tm-ai-composer-bottom-clearance` bar, and axe-core scans of the empty and the finished state; light/dark desktop and
375 px screenshots land in `tests/Tempo.Blazor.E2E/__screenshots__/ai-conversation/`. The bUnit half is
`tests/Tempo.Blazor.Tests/Components/AI/AiConversationDemoPageTests.cs`. The demo binds `SuggestionsLayout="Auto"`.

The demo strings are `TmDemo_AiConversation_*` keys in `DemoResources{,.cs,.sk,.fr}.json`. The page's LABELS —
including the two tool-call display names and the slash-command titles, which a reader sees in the tool cards and
in the `/` list — resolve through `ITmLocalizer`; the sample CONTENT (the simulated answer, the standalone
gallery's sample calls, the command prompts and the JSON payloads) stays in English, like the sample data of
every other demo page.

## Localization

Keys in `TmResources{,.cs,.sk,.fr}.json`: `TmAiConversation_Transcript` (the log's accessible name),
`_EmptyTitle`, `_EmptyDescription`, `_Suggestions`, `_LoadOlder`, `_NewMessages`, `_Stop`, `_Completed`.
The pending placeholder has NO localized key in 2.12.0 by design — it renders only when the host names it
through `MessagePendingText`/`PendingText` (decision `EMUSCZ-2.12-BREAKING-POINTS` (a)); the localized
default is planned for 3.0 as a deliberate visual change.

## Assets

- `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` imports
  `components/_ai-conversation.css` (tokens only — no colour literal, no raw palette step, no
  `[data-theme]` block).
- `_content/Tempo.Blazor.AI/js/ai-conversation.js` — the ES module: `attach`, `detach`,
  `isNearBottom`, `scrollToBottom`, `scrollToMessage`, `revealFocusedSuggestion` (**2.13.0** — scrolls
  only the suggestion row by the overflow of a focused chip; `behavior: 'auto'` under reduced motion;
  never writes the transcript's `scrollTop`). Every entry point is null-safe (a component that
  lost its root element gets a quiet `false`, never a throw), and a host CSP needs no `unsafe-inline`
  for it. Its Node tests live in `wwwroot/js/__tests__/ai-conversation.test.mjs` (`npm run test:ai`) and
  cover the own-scroll suppression, the gesture / `scrollend` / fallback release, the box-own
  ResizeObserver (the log box resizing keeps a bottom-riding reader pinned), the
  `scrollToMessage` contract and the suggestion-row reveal. Host tokens: `--tm-ai-conversation-empty-padding-block`
  (fallback `--tm-space-8`).
