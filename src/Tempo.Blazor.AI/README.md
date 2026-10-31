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

`AddTempoBlazorAI()` registers the core Tempo.Blazor services the AI components build on (the AI
renderer services are added here in the follow-up phases); it is idempotent, so calling it next to
`AddTempoBlazor()` (or using `AddTempoBlazorAll()`, which already includes it) is safe.

## Package contract

The bullets below describe the contract the package is being built to; phase 1 ships the
registration entry point and the stylesheet, and the components listed above arrive in the
follow-up phases of plan 7f21f26b.

- **Models and streaming contracts are planned, not shipped yet.** They will live in
  `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models` / `Tempo.Blazor.AI.Services`, Fáze 2 of the
  same plan), which has zero UI dependencies — a backend will be able to reference the message,
  delta and tool-call records without pulling in Blazor.
- **No LLM SDK, no PromptHelper.** The package is a UI over generic contracts; the host supplies
  the model call as an `IAsyncEnumerable<T>` stream.
- **Markdown will be treated as untrusted model output.** The planned `TmMarkdownView` renders
  through a Markdig pipeline with raw HTML disabled and an explicit URL-scheme allowlist; links
  carry `rel="noopener noreferrer nofollow"` and `target="_blank"`. Markdig is already the only
  third-party dependency of the package.
- **Design system only.** Styling reads `--tm-*` tokens (light and dark themes), every user-visible
  string will be localized through `ITmLocalizer` (en/cs/sk/fr built in), and every animated
  subject has a `prefers-reduced-motion` block in the package stylesheet — the phase-1 streaming
  caret partial is the first one.

## Static assets

| Asset | Path |
|-------|------|
| Stylesheet entry point | `_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` |
| Streaming primitives | `_content/Tempo.Blazor.AI/css/components/_ai-streaming.css` |

`tempo-blazor-ai.css` is an `@import` manifest — hosts reference only the entry point.

## Localization

Keys use the `Tm<Component>_*` convention and live in the core resource tables
(`src/Tempo.Blazor/Resources/TmResources[.cs|.sk|.fr].json`), resolved through the host's
`ITmLocalizer`, so an application can override the wording by registering its own localizer.

## License

MIT — see the repository license.
