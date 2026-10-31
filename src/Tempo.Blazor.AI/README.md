# Tempo.Blazor.AI

AI conversation UI for Blazor (WebAssembly, Server, InteractiveAuto): safe markdown rendering,
assistant/user messages with citations, feedback and versions, a streaming conversation container,
a prompt composer and tool-call / reasoning panels.

## Installation

```bash
dotnet add package Tempo.Blazor.AI
```

```csharp
// Program.cs
builder.Services.AddTempoBlazorAI();
```

```html
<!-- index.html / _Host.cshtml / App.razor -->
<link href="_content/Tempo.Blazor/css/tempo-blazor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css" rel="stylesheet" />
```

`AddTempoBlazorAI()` calls `AddTempoBlazor()` and then registers the package's own services
(`AiMarkdownRenderer`) through `TryAdd*`; it is idempotent, so calling it next to `AddTempoBlazor()`
(or using `AddTempoBlazorAll()`, which already includes it) is safe, and a host registration always
wins.

## Components

| Component | Contract |
|-----------|----------|
| `TmMarkdownView` | Renders untrusted markdown through the safe pipeline. Parameters: `Markdown`, `CodeBlockTemplate`, `OnCitationClick`, `Class` (+ the inherited `DataTestId` / `TestIdPrefix`). |
| `TmAiMessage` | One conversation turn: the ordered parts through `TmMarkdownView`, citation chips with a source preview, source cards, follow-up chips and the action toolbar (copy as plain text, regenerate with a version switcher, thumbs up/down with an optional reason, read aloud). A reasoning part renders through `TmReasoningPanel`, consecutive tool-call parts collapse into one `TmToolCallGroup`, and a single call renders as its own `TmToolCallCard`. Contract: [docs/ai-message.md](../../docs/ai-message.md). |
| `TmToolCallCard` | One tool/function call: state glyph, tool name, localized status, live elapsed time, Copy, Retry (a failed call only) and a disclosure that pretty-prints the JSON arguments and result. A redacted call shows a localized notice and offers no Copy. |
| `TmToolCallGroup` | A run of consecutive tool calls collapsed into one summary — "3 tools · 1 failed · 4.2 s" (Czech: "3 nástroje · 1 selhal · 4,2 s") — that expands into the cards. |
| `TmThinkingIndicator` | The "working…" pill: phase label with a shimmer and a spinner, a check once the work finished. The whole pill is one `role="status"` region. |
| `TmReasoningPanel` | The reasoning disclosure: collapsed summary of what the thinking cost ("Thought for 12.0 s"), expanded steps and the inspected sources, live counter while the model is still thinking. |
| `TmAiConversation` | The transcript plus the streaming machinery: a focusable `role="log"` live region that renders the turns through `TmAiMessage` (or a host `MessageTemplate`), the empty state with suggestion prompts, an optional render window for a long history, the "New messages" and "Stop generating" pills and the throttled `StreamAsync` delta pump. Auto-scroll follows new content only while the reader is at the bottom. Contract: [docs/ai-conversation.md](../../docs/ai-conversation.md). |
| `TmAiComposer` | The prompt composer: an autosizing textarea (Enter sends, Shift+Enter is a new line, an IME composition never sends), a send slot that becomes Stop while a generation runs, attachment chips with `MaxAttachments`/`MaxFileSize` enforced against a localized message, a model selector (built-in or `ModelSelectorTemplate`), an opt-in voice input that renders only where the Web Speech API exists, and one combobox listbox shared by the `/` slash commands and the host's `@` mention provider (debounced, cancellable). `OnSubmit` carries the text, attachments, model, mentions and command; the draft is cleared only after the handler completed, one submit runs at a time (a fast second Enter is a no-op), and the post-submit clear keeps whatever the reader typed while a slow handler ran. Contract: [docs/ai-composer.md](../../docs/ai-composer.md). |

```razor
@using Tempo.Blazor.Components.AI

<TmMarkdownView Markdown="@answer.Text"
                OnCitationClick="id => preview = answer.FindSource(id)" />

<TmAiMessage Message="@answer"
             OnRegenerate="@(() => conversation.RegenerateAsync(answer.Id))"
             OnFeedback="@(feedback => client.RateAsync(answer.Id, feedback))"
             ReadAloud="true" />
```


### Tool calls and reasoning

A tool call is model output about something the agent did, so the card shows the STATE before the payload:
the status glyph and the localized status text sit on the row with the live elapsed time, and the payloads
are behind a disclosure that parses them (`JsonDocument` + an indented `Utf8JsonWriter`) — a payload that
does not parse is shown verbatim instead of being dropped. A call whose `AiToolCall.IsRedacted` flag is set
renders a localized notice (with the lock glyph) in place of the data and offers no Copy, so a redacted
payload has no path out of the page. Retry renders only for a failed call WITH a bound handler, and it
disables itself while that host task is in flight — one click means one retry.

Every action in the row names the tool it belongs to (`TmToolCallCard_CopyFor` "Copy details of {0}" and
friends), so a group's repeated buttons are distinguishable in a screen reader's list of buttons; the
disclosures carry `aria-expanded` and emit `aria-controls` only while the region they name is actually
rendered, so a collapsed row never points at a missing id.

The elapsed time is a `TimeProvider` read: the card resolves `TimeProvider` from the service provider and
falls back to `TimeProvider.System`, so tests drive it with a `FakeTimeProvider`. The timer is armed only
while the call is queued/running and disarmed the moment it finishes — a finished call shows the recorded
duration (`CompletedAt - StartedAt`), because a duration that keeps growing is an estimate, not a fact.
A failing call opens its own details, so the error is visible without a second interaction.

One live region per card (`role="status"`) announces the status transition ("Search: Done") and the copy
confirmation; the reasoning panel has its own single region for the finished reasoning. A copy
confirmation is announced through that region rather than by renaming the button, so the control keeps its
name.

### Streaming a conversation

`TmAiConversation` owns the scroll container and the render throttle; the stream is the host's
`IAsyncEnumerable<AiStreamDelta>`. Append the turn, then stream it:

```razor
<TmAiConversation @ref="conversation" Messages="@messages" Height="32rem" />
```

```csharp
var answer = new AiMessage { Role = AiMessageRole.Assistant, Status = AiMessageStatus.Pending };
messages.Add(answer);
await conversation.StreamAsync(answer, client.StreamAsync(prompt));
```

Renders are throttled through the injected `TimeProvider` (50 ms by default), so a Server circuit is
not re-rendered per token; the end of the stream completes the answer, `StopAsync` keeps the partial
answer and marks it stopped, a throwing stream marks it failed and rethrows. Auto-scroll follows the
newest content only while the reader is at the bottom — a reader who scrolled back gets a "New
messages" pill instead of a moving viewport. Long histories are windowed with `RenderWindowSize` plus
a "Load older messages" control, and one polite `role="log"` region announces inserted turns and the
completion note exactly once.

## Markdown security

A model answer is untrusted input, so `AiMarkdownRenderer` — not the host markup — decides what a
markdown document may become. The pipeline (Markdig, BSD-2-Clause, the package's only third-party
dependency):

- **never renders raw HTML**: the HTML parsers are disabled, so `<script>` arrives as literal text;
- **has an explicit URL allowlist**: `http`, `https`, `mailto` and same-document relative targets
  survive; every other scheme (`javascript:`, `data:`, `vbscript:`, `file:`), a protocol-relative
  target and whitespace/control-character smuggling (`java\tscript:`) collapses to `#`;
- **hardens every link**: `target="_blank"` with `rel="noopener noreferrer nofollow"`, including
  autolinks;
- **loads only absolute http(s) images**: a refused image degrades to its alt text, so it never
  becomes a request;
- **builds the extension set explicitly** — not through `UseAdvancedExtensions()` — so
  `UseGenericAttributes` (arbitrary attribute names, event handlers), the hosted-media embeds and
  author-supplied container classes are not part of it;
- **turns `[1](cite:<sourceId>)` into a citation chip** (`data-source-id`) instead of a link, and
  raises `OnCitationClick` with the source id;
- **escapes code blocks** and ships them as `<pre><code class="language-x">` with a copy affordance;
  the copy/copied labels come from `ITmLocalizer` via `TmMarkdownView`.

The full XSS/mutation-XSS vector set the email sanitizers are held to
(`tests/Shared/SanitizerXssVectors.cs`) is run through the markdown renderer as a regression test,
together with a deterministic fuzz pass. `docs/ai-markdown.md` documents the contract and the
renderer's extension points.

Code blocks are rendered by the pipeline rather than as Blazor components: a citation, a code fence
and a copy button can sit inside any inline/block element of a markdown document, which a Blazor
component cannot be placed into. A host that wants syntax highlighting (or any other code-block
chrome) supplies `CodeBlockTemplate`, a `Func<AiMarkdownCodeBlockContext, string>` returning its own
HTML.

## Package contract

- **Models and streaming contracts live in `Tempo.Blazor.Abstractions`**
  (`Tempo.Blazor.AI.Models` / `Tempo.Blazor.AI.Services`), which has zero UI dependencies — a backend
  can reference the message, delta, tool-call, citation, version and feedback records without pulling in
  Blazor or Markdig. `AiMessageStreamApplier` folds the delta stream into a message as pure logic; the
  caller passes the timestamp. The reader-facing state (`Versions`/`ActiveVersionIndex`, `Feedback`,
  `FollowUps`) lives on `AiMessage` too, so a host persists one record per turn.
- **No LLM SDK, no PromptHelper.** The package is a UI over generic contracts; the host supplies the
  model call as an `IAsyncEnumerable<T>` stream.
- **Design system only.** Styling reads `--tm-*` tokens (light and dark themes), every user-visible
  string is localized through `ITmLocalizer` (en/cs/sk/fr built in), and every animated subject has a
  `prefers-reduced-motion` block in the package stylesheet: the streaming caret
  (`_ai-streaming.css`) and the thinking pair — the `.tm-ai-shimmer` gradient and the spinner — in
  `_ai-thinking.css`. No other package stylesheet declares motion, which is what keeps the reduce
  sweep exact.

## Static assets

| Asset | Path |
|-------|------|
| Stylesheet entry point | `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` |
| Streaming primitives | `_content/Tempo.Blazor.AI/css/components/_ai-streaming.css` |
| Markdown typography | `_content/Tempo.Blazor.AI/css/components/_markdown-view.css` |
| Message layout | `_content/Tempo.Blazor.AI/css/components/_ai-message.css` |
| Tool call card + group | `_content/Tempo.Blazor.AI/css/components/_ai-tool-call.css` |
| Thinking indicator, reasoning panel, shimmer/spinner motion | `_content/Tempo.Blazor.AI/css/components/_ai-thinking.css` |
| Markdown interop module | `_content/Tempo.Blazor.AI/js/ai-markdown.js` |
| Message interop module | `_content/Tempo.Blazor.AI/js/ai-message.js` |
| Conversation interop module | `_content/Tempo.Blazor.AI/js/ai-conversation.js` |
| Composer interop module | `_content/Tempo.Blazor.AI/js/ai-composer.js` |

`tempo-blazor-ai.css` is an `@import` manifest — hosts reference only the entry point. The interop
module is a plain ES module imported through `IJSObjectReference`, so it needs **no** `<script>` tag
and no CSP `unsafe-inline`; it wires the citation chips (which cannot be Blazor components inside a
rendered document) and the code-block copy button to the component's root element.

`ai-message.js` is the same kind of module for the message's browser-only behaviours: the clipboard
write behind Copy, the `speechSynthesis` behind Read aloud (feature-detected — a browser without it
simply gets no button) and `focusCitation`, which returns focus to the citation chip that opened the
source preview (the chip is markdown-generated markup, so Blazor holds no reference to it).

## Localization

Keys use the `Tm<Component>_*` convention and live in the core resource tables
(`src/Tempo.Blazor/Resources/TmResources[.cs|.sk|.fr].json`), resolved through the host's
`ITmLocalizer`, so an application can override the wording by registering its own localizer.

## License

MIT — see the repository license.
