# AI markdown (`TmMarkdownView`)

`Tempo.Blazor.AI` renders every model answer through **one** pipeline. An answer is untrusted
output, so the renderer — not the host markup, not the component — decides what a markdown document
may become.

| Layer | Type | Responsibility |
|-------|------|----------------|
| Component | `TmMarkdownView` | Localized labels, last-input render cache, the interop module wiring, `CodeBlockTemplate` seam |
| Renderer | `AiMarkdownRenderer` (`Tempo.Blazor.AI.Markdown`) | Markdig pipeline + the package's link/image/code/table renderers + URL policy |
| Models | `AiMessage`, `AiStreamDelta`, `AiMessageStreamApplier` (`Tempo.Blazor.AI.Models` / `.Services`, in `Tempo.Blazor.Abstractions`) | What a message is; folding the delta stream into it |

## Usage

```razor
@using Tempo.Blazor.Components.AI

<TmMarkdownView Markdown="@answer.Text"
                OnCitationClick="id => preview = answer.FindSource(id)" />
```

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Markdown` | `string?` | `null` | Untrusted markdown. Whitespace renders nothing. |
| `CodeBlockTemplate` | `Func<AiMarkdownCodeBlockContext, string>?` | `null` | Takes over a code block's HTML (e.g. syntax highlighting). The returned HTML is trusted verbatim — escape `context.Code` yourself (see below). |
| `OnCitationClick` | `EventCallback<string>` | — | Raised with the source id of an activated citation chip. |
| `Class` | `string?` | `null` | Extra CSS class(es) on the root `.tm-markdown-view`. |
| `DataTestId` / `TestIdPrefix` | `string?` | `null` | Inherited from `TmComponentBase`; the default root test id is `markdown-view`. |

`AddTempoBlazorAI()` registers `AiMarkdownRenderer` through `TryAddSingleton`, so a host may replace it
with a subclass.

## `CodeBlockTemplate` — escaping and the render cache

Two rules, both enforced by the component contract:

1. **The host escapes.** `AiMarkdownCodeBlockContext.Code` is the decoded code, **not** HTML-escaped.
   The delegate's return value is written into the answer's markup verbatim, so the host owns the
   encoding:

   ```csharp
   // GOOD — the code is encoded before it touches the markup
   <TmMarkdownView Markdown="@answer"
                   CodeBlockTemplate="@RenderHighlighted" />

   private string RenderHighlighted(AiMarkdownCodeBlockContext block)
       => $"<pre class=\"hl\"><code>{HtmlEncoder.Default.Encode(block.Code)}</code></pre>";

   // BAD — a raw code block can inject markup
   private string RenderUnsafe(AiMarkdownCodeBlockContext block)
       => $"<pre><code>{block.Code}</code></pre>";
   ```

2. **The reference must be stable.** The component caches its last render, and it compares
   `CodeBlockTemplate` by the delegate's `Method` **and** `Target` — not by delegate instance. A cached
   field, a static method or a method group therefore keeps a parent re-render free, while an inline
   lambda that captures a fresh closure object changes the target and re-runs Markdig on every render:

   ```csharp
   // GOOD: one cached seam, method+target are unchanged across renders
   private readonly Func<AiMarkdownCodeBlockContext, string> _codeBlockTemplate;
   public MyAnswerView() => _codeBlockTemplate = RenderHighlighted;

   // BAD: a new closure per render -> a new Target -> the markdown pipeline runs again every time
   <TmMarkdownView CodeBlockTemplate="@(ctx => RenderHighlighted(ctx))" />
   ```

## Security contract

1. **Raw HTML is disabled.** `MarkdownPipelineBuilder.DisableHtml()` removes the HTML parsers, so
   `<script>`, `<img onerror=…>` and `<!-- -->` arrive as literal (escaped) text.
2. **The extension set is explicit.** The pipeline is built from
   `UsePipeTables`, `UseGridTables`, `UseEmphasisExtras`, `UseListExtras`, `UseTaskLists`,
   `UseAutoLinks`, `UseFootnotes`, `UseDefinitionLists`, `UseAbbreviations`, `UseSmartyPants` and
   `UseEmojiAndSmiley` — deliberately **not** `UseAdvancedExtensions()`, which would add
   `UseGenericAttributes` (arbitrary attribute names, including event handlers),
   `UseMediaLinks`/`UseDiagrams` (hosted embeds) and `UseCustomContainers` (author-supplied classes).
3. **URL allowlist** (`AiMarkdownUrlPolicy`): a link target keeps its value only when its normalised
   scheme is `http`, `https` or `mailto`, or when it is a same-document relative reference. Everything
   else — `javascript:`, `data:`, `vbscript:`, `file:`, any protocol-relative `//host` target, and
   whitespace/control-character smuggling such as `java\tscript:` — collapses to `#`. The check runs on
   a normalised copy (controls, whitespace and backslashes removed) — exactly what a browser strips
   before it parses a scheme.
4. **Every link is hardened**: `target="_blank" rel="noopener noreferrer nofollow"`, including
   autolinks (`<https://…>` and `www.…`).
5. **Images load only from absolute http(s)**: a refused image is replaced by its alt text, so it never
   becomes a request (no `data:` payload, no tracking pixel through a refused scheme).
6. **Citation chips, not links**: `[1](cite:<sourceId>)` renders as
   `<button class="tm-markdown-view__citation" data-source-id="<sourceId>">` with the localized
   accessible name; a `cite:` link without an id stays ordinary text.
7. **Code blocks are escaped** and wrapped in `<div class="tm-markdown-view__code">` with a language
   label and a copy button; the fence info is reduced to a plain `[A-Za-z0-9+#._-]` token, so no author
   text reaches the `class` attribute. The `<pre>` carries `tabindex="0"`, so a long line can be
   scrolled with the keyboard (WCAG 2.1.1).
8. **Tables** are wrapped in a horizontally scrollable `.tm-markdown-view__table` container that is a
   keyboard focus stop (`tabindex="0"`) and, when a `TableLabel` was supplied, a named
   `role="region"` (`TmMarkdownView_Table`, localized). An unnamed region is never emitted — a landmark
   a screen reader cannot identify is worse than none.

The regression suite (`tests/Tempo.Blazor.Tests/Components/AI`) runs the shared vector set
(`tests/Shared/XssPayloads.cs` — the sanitizer-free file, linked into this test project) through the
renderer as plain markdown, as a link target and as an image target, plus a deterministic fuzz pass, and
asserts attribute-level markup safety (`XssPayloads.AssertNoExecutableMarkup`). The email packages pin
the very same payload bytes through `tests/Shared/SanitizerXssVectors.cs`, which adds their
production-tokenizer assertions on top.

## Model output → message

A host streams `IAsyncEnumerable<AiStreamDelta>` and folds it with `AiMessageStreamApplier.Apply`,
which returns `true` only when the message changed (so a component can skip a render):

| Delta | Effect |
|-------|--------|
| `AiTextDelta` | Appends to the trailing text part (a new part when another kind is last) |
| `AiReasoningDelta` | Appends to the trailing reasoning step; `NewStep = true` starts another |
| `AiToolCallStartedDelta` | Adds a tool-call part (status `Running`, `StartedAt`); a re-delivered id is a no-op |
| `AiToolCallUpdatedDelta` | Updates arguments/result/name/status by id; an unknown id is created |
| `AiToolCallCompletedDelta` | Final status, result, error, `CompletedAt`; the first completion keeps its timestamp |
| `AiCitationAddedDelta` | Adds a source to the sources part, deduplicated by id; a delta without a source or with an empty id is a no-op (malformed transport input must not tear down the read loop) |
| `AiCompletedDelta` | `Completed`; a duplicate — or a completion after `Failed`/`Stopped` — is a no-op |
| `AiErrorDelta` | `Failed` + `AiMessage.Error`; ignored after `Completed` or `Stopped` |

A content delta that arrives after the message reached `Completed`, `Stopped` or `Failed` is ignored —
a late token never reopens a finished answer, and an error that arrives after the user stopped it never
turns a partial answer into a failure. The full state machine is documented on
`AiMessageStreamApplier.Apply`.

## Interop

`TmMarkdownView` imports `_content/Tempo.Blazor.AI/js/ai-markdown.js` through `IJSObjectReference` (no
`<script>` tag, no `unsafe-inline`, no `eval`) and attaches one delegated click listener to its root:

- `[data-source-id]` → `HandleCitationClickAsync(sourceId)` → `OnCitationClick`;
- `[data-copy-code]` → `navigator.clipboard.writeText(<pre><code> text)`, then `data-copied` (icon swap
  via CSS) and the localized `data-copied-label` written into the **sibling** `role="status"` live
  region for two seconds. The button's accessible name never changes: a name that flips for two seconds
  makes the control hard to find again, and the live region already announces the state.

### Decision `AI-MARKDOWN-COPY-DELEGATED`

The citation chip and the copy button are rendered as **markup** and their behaviour is delegated to
this ES module, because a Blazor component cannot sit inside a `MarkupString`: the answer is one HTML
string produced by the renderer, and a citation can land inside any inline element. The copy affordance
mirrors `TmCopyButton` at the markup level (the same two SVG icons swapped through a data attribute, a
localized accessible name) without restyling the core `.tm-copy-button`. Extracting a component per
control would mean parsing the HTML back into a render tree at every render — exactly the round-trip
the sanitizing pipeline exists to avoid. The trade-off is that the click behaviour is not a Blazor
event handler, which is why the markup contract is pinned in bUnit and this note exists.

The listener is detached on dispose. The module is imported only when the rendered document actually
contains a citation or a code block, so plain prose costs no interop call.

## Accessibility

- The copy button is a native `<button>` with a localized accessible name; the copied state is
  announced through the `role="status"` span that is its **sibling** in the code header row (a live
  region nested inside the control is unreliable and would join its accessible name).
- The copy button and a citation chip both grow to the `--tm-touch-target` floor on a coarse pointer.
- The table wrapper is a keyboard focus stop with a localized region name; the `<pre>` of a code block
  is a keyboard focus stop too.
- Every interactive element (links, chips, copy buttons, table wrapper, code `<pre>`) keeps a visible
  2px `--tm-border-color-focus` ring with a 2px offset; the code `<pre>` draws its ring inset, because
  the surrounding code card clips at its border.
- `prefers-reduced-motion`: the markdown stylesheet declares no animation; the streaming caret in
  `_ai-streaming.css` keeps its own reduce block.
