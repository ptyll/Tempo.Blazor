# Tempo.Blazor.EmailTemplates

A visual, drag-and-drop email template editor component for [Tempo.Blazor](https://github.com/ptyll/Tempo.Blazor).
Built entirely on Tempo.Blazor components — no MudBlazor or Syncfusion dependency.

## Features

- **`TmEmailTemplateEditor`** — a responsive editor built on the core `TmEditorShell`: docked
  palette + inspector on desktop and tablet, a bottom sheet (or a tablist) on a phone. Layout follows the
  container (`LayoutMode="Auto"`) or is pinned for tests and screenshots.
- **WYSIWYG canvas** — the canvas paints the e-mail itself (typography, colours, images, tables, buttons),
  not a list of block types, and the action bar of the selected block is in flow on a touch device.
- **Full MJML 4 parity** — every block, every attribute, the complete `<mj-head>` section;
  bidirectional import/export.
- **Scriban variables** — a variable picker with host-supplied descriptions and groups, sample-data
  generation, and a sandboxed engine with per-pass HTML escaping, limits and an audit trail.
- **Live preview** — sandboxed desktop / tablet / mobile preview with a plain-text view, rendered through a
  replaceable `IEmailPreviewRenderer` (so a WASM host previews through its server).
- **Save contract** — the host reports the outcome (`EmailSaveResult`), dirty state, single-flight saving,
  autosave as a trigger, conflict handling, `ReadOnly`, and a public `SaveAsync()` for a leave guard.
- Grouped command bar with host slots, save-status badge, undo/redo, copy/paste, keyboard shortcuts
  (incl. Ctrl/⌘+S), localization (EN/CS/SK/FR) and design-token theming.

## Installation

```
dotnet add package Tempo.Blazor.EmailTemplates
```

## Registration

```csharp
// the editor UI — no template engine, so this works in Blazor WebAssembly
services.AddTempoEmailTemplateEditor();

// in the process that renders (API / worker): the Engine package
services.AddTempoEmailTemplateEngine();

// in a process that does NOT render locally: supply the seam yourself
services.AddScoped<IEmailPreviewRenderer, MyApiPreviewRenderer>();
```

`AddTempoEmailTemplates()` still compiles but is `[Obsolete]` and no longer registers the engine.
The full story — preview seam, save contract, dirty guard, `ReadOnly`, `ItemEditingMode`, layout
parameters, localization replacement, audit sink, limits — is in **`docs/email-templates/HOSTING.md`**.

## Usage

```razor
<TmEmailTemplateEditor @ref="editor" @bind-Document="document" OnSave="SaveAsync" />
```

The consuming application supplies an `IEmailTemplateStore` (persistence) and an `IEmailSender`
(delivery) from `Tempo.Blazor.EmailTemplates.Abstractions`.

## Saving: the host reports the outcome

The editor never claims a save happened. `OnSave` receives an `EmailSaveRequest` — a deep-copy snapshot,
the trigger (`Manual`, `Shortcut`, `AutoSave`, `Programmatic`), the editor revision the snapshot was taken
at and a `CancellationToken` that is cancelled when the editor is disposed — and answers with
`EmailSaveResult.Saved()`, `.Failed(message)` or `.Conflict(message)`. Only `Saved()` clears `IsDirty` and
shows "Saved"; a failure keeps the document dirty and shows the host's message, and a conflict also pauses
auto-save until the next successful non-auto-save save (`Manual`, `Shortcut` or `Programmatic`) or a new
`Document` instance. Auto-save uses the same
handler with `Trigger = AutoSave` (the old `OnAutoSave` callback is gone). Saving is single-flight: one
request at a time, the Save button stays focusable but announces `aria-disabled` while a request runs (a
native `disabled` would drop a keyboard user's focus; the click is merged instead), and a trigger accepted
mid-flight merges into one follow-up save.

```razor
<TmEmailTemplateEditor @ref="editor" @bind-Document="document"
                       OnSave="SaveAsync"
                       AutoSave="autoSave"
                       IsDirtyChanged="dirty => _dirty = dirty"
                       ToolbarStart="@(_ => <span>@Name</span>)"
                       ToolbarEnd="@(_ => <TmButton OnClick="SendTestAsync">Send test</TmButton>)" />

@* leave-guard: SaveAsync() is public for exactly this. The host keeps its OWN copy of the dirty flag
   from IsDirtyChanged — `editor.IsDirty` in markup is only re-read on the next render, so passing it
   directly gives the guard a stale value. *@
<TmNavigationGuard IsDirty="_dirty"
                   OnSaveAndLeave="@(async () => editor is not null && (await editor.SaveAsync()).IsSaved)" />

@code {
    TmEmailTemplateEditor editor = default!;
    bool _dirty;

    async Task<EmailSaveResult> SaveAsync(EmailSaveRequest request)
    {
        try
        {
            // the host owns the revision/ETag: request.Revision is the editor's local revision
            await Api.UpdateAsync(Id, request.Document, request.Revision, request.CancellationToken);
            return EmailSaveResult.Saved();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return EmailSaveResult.Conflict("Someone else saved a newer revision.");
        }
        catch (Exception ex)
        {
            return EmailSaveResult.Failed(ex.Message);
        }
    }
}
```

`Ctrl+S` / `Cmd+S` save with `Trigger = Shortcut` (the browser's own "save page" default is cancelled for
that chord only, and the guard is per editor: two editors on one page do not disarm each other). `ReadOnly`
blocks every editing path — toolbox, canvas drag & drop, inspector (including its sub-editors), import,
undo/redo and the mutating shortcuts — while preview, validation and export stay available; the save chrome
is replaced by a "Read-only" badge, no Save button is rendered, and `SaveAsync()` answers `Failed` without
calling the host. `ImageUpload="new ImageUploadOptions { Accept = "image/*", MaxBytes = 2 * 1024 * 1024 }"`
validates a picked image before `OnImageUpload` runs.

### Resolving a conflict

The host owns the revision/ETag, so only the host can resolve a conflict:

- **Reload** — deserialize the current server state into a **new instance** and pass it as `Document`. That
  is the ONLY thing that resets the editor: a new reference resets the baseline (the new document is clean),
  the pending conflict/failure state, the auto-save pause and the undo history.
- **Force save** — answer the next `Manual`/`Shortcut`/`Programmatic` request with `Saved()`. The editor
  clears the pause and announces "Saved" (D26: an explicit save is proof the template can be written again).
- **Mutating the existing instance in place resets nothing** — the editor sees the same reference, so it is
  treated as an edit, not a reload. Reload means `EmailTemplateSerializer.Deserialize(json)` into a fresh
  `EmailTemplateDocument`.
- Reloading **discards the unsaved changes** in the editor. That is a user decision: ask before you replace
  the document (the `IsDirty`/`IsDirtyChanged` pair is exactly what a "Discard changes?" dialog needs).

A reset is triggered only by a reference the host has not handed over before. The editor's own
`DocumentChanged` echo (the host stores the instance and passes it back, also after `await`) is recognised
and never resets the history, and neither is a one-way host that re-renders the same instance after
`IsDirtyChanged`.

### Stable DOM hooks

Hosts and E2E select the command bar by attributes, never by position: `[data-tm-group="history|review|file|
help"]`, `[data-tm-save]`, `[data-tm-save-badge]` (`data-tm-save-state="dirty|saving|saved|failed|conflict"`),
`[data-tm-save-alert]`, `[data-tm-save-retry]`, `[data-tm-readonly-badge]`, `[data-tm-save-announcer]`. The
groups are private `RenderFragment`s inside the editor (`HistoryGroup`, `ReviewGroup`, `FileGroup`,
`HelpGroup`, `SaveButton`, `SaveBadge`), so a later phase can move them into an overflow menu or a mobile
action bar without breaking a host or a test.

## Documentation

- `docs/email-templates/HOSTING.md` — hosting, registration, preview, saving, layout, localization, limits
- `docs/email-templates/TEMPLATE_SYNTAX.md` — the template language, escaping and URL policy
- `docs/email-templates/MIGRATION-2.9-to-2.10.md` — every breaking change with a before/after
- `docs/email-templates/MJML_ATTRIBUTE_PARITY.md` — the MJML 4 attribute table

## Static assets

The package ships three ES modules under `_content/Tempo.Blazor.EmailTemplates/`:
`tm-email-save-shortcut.js` (cancels the browser's Save-page default for Ctrl/⌘+S, one listener per
editor), `tm-email-variable-insert.js` (caret insertion for the variable picker) and
`tm-email-panel-focus.js` (focus recovery after a host-owned rail button unmounts). No `<script>` tag is
needed — they are imported on demand.
