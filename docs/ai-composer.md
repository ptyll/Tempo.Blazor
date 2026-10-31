# AI composer (`TmAiComposer`)

`TmAiComposer` is the prompt composer of an AI conversation: an autosizing textarea whose Enter sends and
Shift+Enter inserts a new line, a slot that becomes Stop while a generation runs, attachments, a model
selector, an opt-in voice input and one suggestion listbox shared by the `/` slash commands and the host's
`@` mention provider.

The submit contract is the host's: the component never references an LLM SDK and never uploads a file. It
raises `OnSubmit` with an `AiComposerSubmit` and clears the draft only after that handler completed. One
submit runs at a time: while the handler is in flight the Send control is already disabled (`IsGenerating`
is a parameter and does not move until the host re-renders), so a fast second Enter or a double click is a
no-op instead of a second generation. The clear is a compare against the snapshot that was actually sent —
text typed, an attachment picked or a mention added while a slow handler ran stays in the composer for the
next prompt. The models (`AiComposerSubmit`, `AiComposerAttachment`, `AiSlashCommand`, `AiMentionItem`,
`IAiMentionProvider`) live in `Tempo.Blazor.Abstractions` (`Tempo.Blazor.AI.Models` / `.Services`), so a
backend can consume the same records without Blazor.

```razor
@using Tempo.Blazor.Components.AI

<TmAiComposer @bind-Text="draft"
              IsGenerating="@generating"
              Commands="@commands"
              MentionProvider="@mentions"
              Models="@models"
              @bind-SelectedModel="model"
              MaxAttachments="3"
              OnSubmit="@SendAsync"
              OnStop="@(() => generation?.Cancel())" />

@code {
    private string draft = string.Empty;
    private string? model = "gpt-4o";
    private bool generating;
    private CancellationTokenSource? generation;
    private readonly string[] models = ["gpt-4o", "o3-mini"];
    private readonly List<AiSlashCommand> commands =
    [
        new() { Id = "summarise", Title = "summarise", Description = "Summarise the thread" },
    ];

    private async Task SendAsync(AiComposerSubmit submit)
    {
        generation = new CancellationTokenSource();
        generating = true;
        try
        {
            // submit.Text, submit.Attachments, submit.Model, submit.Mentions, submit.Command
            await client.AskAsync(submit, generation.Token);
        }
        finally
        {
            generating = false;
        }
    }
}
```

## Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Text` / `TextChanged` | `string?` / `EventCallback<string?>` | `null` | The draft, two-way bound. The component raises the callback on every edit and with an empty string after a successful submit. A host value the component did not type replaces the draft (the list closes, the recorded command/mentions are dropped); echoing the received value is not a replacement. |
| `OnSubmit` | `EventCallback<AiComposerSubmit>` | — | The submit. Without a bound handler the Send control stays disabled — a button that raises nothing is not offered. |
| `IsGenerating` | `bool` | `false` | The slot renders **Stop** instead of Send, Enter cannot start a second generation, and the focus follows the swapped control (Stop, or the prompt when the returning Send is disabled). |
| `OnStop` | `EventCallback` | — | Raised by the Stop control. |
| `Placeholder` | `string?` | `null` | Prompt placeholder; `null` uses the localized "Ask anything…". |
| `AriaLabel` | `string?` | `null` | Accessible name of the prompt; `null` uses the localized "Message". Set it when a page has several composers. |
| `Disabled` | `bool` | `false` | Disables the prompt, the picker, the voice control and the send/stop slot. |
| `Sticky` | `bool` | `false` | Sticks the composer to the bottom at every width. The narrow layout is sticky on its own. |
| `MaxRows` | `int` | `8` | Row cap of the autosize; zero or less leaves the height uncapped (the stylesheet's `max-block-size` stays the hard cap). |
| `EnableAttachments` | `bool` | `true` | Offers the picker and the chip list. |
| `MaxAttachments` | `int` | `5` | Files per prompt. `0` refuses every file with the localized message. |
| `MaxFileSize` | `long` | `10 MB` | Size limit in bytes. `0` disables the check. |
| `Accept` | `string?` | `null` | `accept` attribute of the browser picker. |
| `Attachments` / `AttachmentsChanged` | `IReadOnlyList<AiComposerAttachment>?` / `EventCallback<...>` | `null` | Seeded chips and the accepted changes. A new id sequence replaces the chips; echoing the raised list does not. |
| `Commands` | `IReadOnlyList<AiSlashCommand>?` | `null` | Slash commands offered after `/`. |
| `MentionProvider` | `IAiMentionProvider?` | `null` | Source of the `@` mentions. Without it the trigger does nothing. |
| `MentionDebounce` | `TimeSpan` | `200 ms` | Quiet period before a mention query is sent. |
| `Models` | `IReadOnlyList<string>?` | `null` | The built-in select's options. |
| `SelectedModel` / `SelectedModelChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way bound model id, carried into `AiComposerSubmit.Model`. |
| `ModelSelectorTemplate` | `RenderFragment?` | `null` | Replaces the built-in select with the host's own picker (the host binds it to `SelectedModel`). |
| `EnableVoiceInput` | `bool` | `false` | Opt-in voice input; the control renders only when the browser exposes the Web Speech API. |
| `SpeechLanguage` | `string?` | `null` | BCP-47 recognizer language; `null` uses the document language, then `en-US`. |
| `TrackVirtualKeyboard` | `bool` | `false` | Tracks the on-screen keyboard so a sticky composer stays above it (the `TmChat.TrackVirtualKeyboard` pattern). |
| `Class` | `string?` | `null` | Extra CSS class(es) on the root `.tm-ai-composer`. |
| `DataTestId` / `TestIdPrefix` | `string?` | `null` | Inherited from `TmComponentBase`; the default root test id is `ai-composer`. |

The root carries `data-generating` (`true`/`false`) and the narrow-layout keyboard marker
`data-tm-keyboard-open` while the opt-in tracker sees an open keyboard. Internal hooks are
`data-testid`s: `ai-composer-input`, `-send`, `-stop`, `-attach`, `-voice`, `-model`, `-attachments`,
`-attachment`, `-remove-attachment`, `-suggestions`, `-option`, `-announcement`.

Public members: `SubmitAsync()` (the same path a host's own send control uses), `IsListening`,
`CurrentAttachments`, and the `[JSInvokable]` entry points `OnComposerSubmit`, `OnCaretChanged`,
`OnSpeechResult`, `OnSpeechError`, `OnSpeechEnded`.

## Keyboard and the IME guard

Enter sends, Shift+Enter inserts a new line, and Alt/Ctrl/⌘+Enter stay native. An Enter that belongs to an
IME composition never sends. That guard lives in the package's ES module `ai-composer.js`: the browser
reports `event.isComposing` (and the legacy `keyCode === 229`, which Safari sends for a commit-Enter) on
the native event, while a **net8** `KeyboardEventArgs` has no `IsComposing` at all. The C# `@onkeydown`
handler remains the pre-attach fallback — it submits only until the module attached, so Enter activates
exactly once, and it is the only owner of the combobox keys:

| Key | Open listbox | Closed listbox |
|-----|--------------|----------------|
| `ArrowDown` / `ArrowUp` | Move the active option (wraps) | Native caret movement |
| `Home` / `End` | First / last option | Native |
| `Enter` | Activate the active option — never sends | Send (the module, or the C# fallback) |
| `Escape` | Close the list, keep the draft | — |
| `Tab` | Close the list, move focus on | Native |

While the listbox is expanded the module suppresses the native newline of a plain Enter and the caret
movement of the arrow keys; the option activation itself is C#, because it changes Blazor state.

## Suggestion listbox (`/` commands, `@` mentions)

The textarea **is** the combobox: `role="combobox"`, `aria-autocomplete="list"`, `aria-expanded`,
`aria-controls` (only while the listbox is rendered — never a dangling reference) and
`aria-activedescendant`. The DOM focus never leaves the prompt, so the reader keeps the caret and a screen
reader announces the highlighted option instead of being moved into a list.

A trigger must start a whitespace-delimited token: `and/or` and `bob@example` are text. Matching is
case-insensitive (`AiSlashCommand.Title`/`Description` for commands, the provider's answer for mentions).

Mention queries are debounced (`MentionDebounce`, resolved through the injected `TimeProvider`) and
cancelled the moment the query changes, the list closes or the component is disposed; the
`CancellationToken` a host receives is that token, so a slow API call cannot repaint a stale list. The
result is also guarded by a generation counter, so a provider that ignores the token cannot resurrect a
closed list. A provider that throws closes the list and announces one localized failure — it never breaks
the editor.

Selecting a command inserts `AiSlashCommand.Prompt` (or `/{Title}`) and records it; selecting a mention
replaces the typed `@query` with `@Label` and records the item once. The recorded command travels as
`AiComposerSubmit.Command`, the mentions as `AiComposerSubmit.Mentions`.

The listbox is deliberately **not** a `TmPopover`: that component's trigger becomes the aria-owns target of
the panel, which would move the reader's focus out of the prompt and break the combobox pattern (decision
`AI-COMPOSER-SUGGESTIONS-NOT-POPOVER`). It is instead an anchored `role="listbox"` inside the composer,
placed from the caret by the module (`placeSuggestions` writes `--tm-ai-composer-caret-inline-start` /
`--tm-ai-composer-caret-block-end`, and the stylesheet falls back to the composer's corner when no caret
measurement ever arrived).

## Attachments

The picker is a native `InputFile` wrapped in a `<label>` (visually clipped but focusable, named by the
localized "Attach files"), so it works with mouse, touch and keyboard. A picked batch is validated against
`MaxAttachments` and `MaxFileSize` **before** anything reaches the host:

- an oversized file is skipped, the rest of the batch is accepted, and the localized
  "`{0}` is larger than the `{1}` limit." message is announced;
- the batch stops at `MaxAttachments` and announces the plural family "You can attach at most `{0}`
  file/files" ("`{0}` soubor / soubory / souborů"), so the noun agrees with the count;
- an accepted batch raises `AttachmentsChanged` **once** — one picker interaction is one state change.

`AiComposerAttachment.Content` carries the `IBrowserFile` untouched (an opaque `object` so the Abstractions
package needs no Blazor reference); reading the bytes, uploading and scanning them is the host's job. A
chip's Remove control is named by its file ("Remove report.pdf"), and removing it returns the focus to the
prompt. After a successful submit the composer drops exactly the chips it sent (compared by reference) and
keeps the files the reader picked while the handler ran.

## Voice input

`EnableVoiceInput` is opt-in, and the microphone control exists only where `speechSupported()` finds the
Web Speech API (`SpeechRecognition` or the prefixed `webkitSpeechRecognition`) — a browser without it never
shows a dead button. `startSpeech` wires one recognizer per composer (language from `SpeechLanguage`, the
document language, or `en-US`), lifts the draft through `OnSpeechResult` (final transcripts only, inserted
at the caret with the separating space a typist would add) and reports a failure through the live region.
The session is released by `stopSpeech` and again by `detach`, so no recognizer outlives the component;
disabling the composer or turning the opt-in off stops a running session.

## Mobile and the virtual keyboard

The narrow layout (`@media (width < 640px)`, the shared Sm breakpoint) is sticky on the composer's bottom edge; `Sticky="true"`
forces the same at any width. `TrackVirtualKeyboard` adds the `visualViewport` tracker: while an on-screen
keyboard covers part of the visual viewport the module writes the measured gap as the runtime knob
`--tm-ai-composer-keyboard-inset` (plus `data-tm-keyboard-open`) on the root, and the sticky offset adds it
— so iOS Safari, which shrinks only the visual viewport, keeps the composer above the keyboard. The knob is
never declared by CSS (its default lives in the `var()` fallback), so a host override keeps cascading. The
tracker ignores browser-chrome-sized shrinks, a zoomed page (`scale ≠ 1`) and a degenerate 1×1 viewport.

## Accessibility

- The prompt is a labelled combobox; the suggestion listbox follows the APG combobox pattern with
  `aria-activedescendant` and keeps the focus in the prompt.
- Every control is a real `<button>`/`<select>`/`<input>` (core `TmButton` for the actions) with a
  localized accessible name; icon-only controls carry their name through `aria-label`, and a repeated chip
  control names its file.
- Focus never dies on `<body>`: activating an option or removing a chip returns the focus to the prompt, and
  the send/stop swap moves the focus to the replacement control. The component probes the slot
  (`isFocusWithin`) BEFORE the swap render, so a generation started from the prompt (the Enter path) leaves
  the caret where it is, while a click on the slot follows the new button; a disabled Send cannot take the
  focus, so the prompt is the fallback (WCAG 2.4.3).
- A coarse pointer gets the 44 px floor (`--tm-touch-target`) on the actions, the picker, the chips'
  Remove and every suggestion row.
- The forced-colors block paints the surfaces with system colours and keeps the active suggestion visible
  through `Highlight`/`HighlightText`.
- The component has exactly **one** polite live region (`role="status"`): attachment limits, the voice
  state, a voice failure and a failed mention query are additions to it. The PARAGRAPH is persistent (a
  region that is re-created with its text already in place is often silent); only its keyed child span is
  replaced, so an identical wording is announced again (WCAG 4.1.3). The region is deliberately **not** the
  prompt's `aria-describedby`: a transient status must not become the prompt's permanent description.
- Nothing in the composer's stylesheet animates or transitions, so there is no reduced-motion debt; a
  suggestion list appears instantly.

## Interop module (`ai-composer.js`)

`_content/Tempo.Blazor.AI/js/ai-composer.js`, a plain ES module imported through `IJSObjectReference` (no
`<script>` tag, no eval, no inline handler — CSP-safe). Exports: `attach(textarea, dotNetRef, options)`,
`detach(textarea)`, `resize(textarea, maxRows)`, `placeSuggestions(textarea, list)`, `speechSupported()`,
`startSpeech(textarea, dotNetRef, options)`, `stopSpeech(textarea)`, `attachViewportTracker(root, options)`,
`detachViewportTracker(root)`, `isFocusWithin(root)`, `focusFirst(root, selector)` plus the pure helpers
`shouldSubmit`, `nextTextareaHeight` and `computeCaretPlacement`. Every entry point is null-safe: a circuit that dropped mid-render gets a quiet
no-op, never a throw. Node tests: `npm run test:ai`
(`src/Tempo.Blazor.AI/wwwroot/js/__tests__/ai-composer.test.mjs`).

## Stylesheet

`_content/Tempo.Blazor.AI/css/tempo-blazor-ai.css` (the `_ai-composer.css` partial). Every rule reads a
`--tm-*` token; there is no colour literal and no `[data-theme]` block — dark mode is `tokens-dark.css`'s
job. The root is a size container (`container-name: tm-ai-composer`) for its descendants.

## Localization keys

`TmAiComposer_Label`, `_Placeholder`, `_Send`, `_Stop`, `_Attach`, `_Attachments`, `_RemoveAttachment`,
`_AttachmentTooLarge`, `_Commands`, `_Mentions`, `_MentionError`, `_Model`, `_VoiceStart`, `_VoiceStop`,
`_VoiceListening`, `_VoiceError`, and the plural family `TmAiComposer_TooManyAttachments_One` / `_Few` /
`_Many` / `_Other` (`Loc.Plural`, so each language ships the forms its grammar needs) — English, Czech,
Slovak and French, plus the test localizer.
