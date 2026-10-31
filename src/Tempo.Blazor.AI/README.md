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
| `TmAiMessage` | One conversation turn: the ordered parts through `TmMarkdownView`, citation chips with a source preview, source cards, follow-up chips and the action toolbar (copy as plain text, regenerate with a version switcher, thumbs up/down with an optional reason, read aloud). Contract: [docs/ai-message.md](../../docs/ai-message.md). |

```razor
@using Tempo.Blazor.Components.AI

<TmMarkdownView Markdown="@answer.Text"
                OnCitationClick="id => preview = answer.FindSource(id)" />

<TmAiMessage Message="@answer"
             OnRegenerate="@(() => conversation.RegenerateAsync(answer.Id))"
             OnFeedback="@(feedback => client.RateAsync(answer.Id, feedback))"
             ReadAloud="true" />
```

`TmAiConversation`, `TmAiComposer`, `TmToolCallCard`, `TmThinkingIndicator` and `TmReasoningPanel` arrive
in the follow-up phases of plan 7f21f26b.

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
  `prefers-reduced-motion` block in the package stylesheet — the streaming caret partial is the only
  animated subject so far (`TmAiMessage` adds none, so nothing escapes that block).

## Static assets

| Asset | Path |
|-------|------|
| Stylesheet entry point | `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` |
| Streaming primitives | `_content/Tempo.Blazor.AI/css/components/_ai-streaming.css` |
| Markdown typography | `_content/Tempo.Blazor.AI/css/components/_markdown-view.css` |
| Message layout | `_content/Tempo.Blazor.AI/css/components/_ai-message.css` |
| Markdown interop module | `_content/Tempo.Blazor.AI/js/ai-markdown.js` |
| Message interop module | `_content/Tempo.Blazor.AI/js/ai-message.js` |

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
