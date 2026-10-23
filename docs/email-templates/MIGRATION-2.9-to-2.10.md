# Migrating from 2.9.x to 2.10.0

Written for a host that already used `Tempo.Blazor.EmailTemplates` 2.9.x — in practice PostrionCore,
whose P06 plan ("Šablony a e-mailový editor na Tempo.Blazor.EmailTemplates") has a gate on this release.
The reference host in this repository is the demo application (`src/Tempo.Blazor.Demo`,
`src/Tempo.Blazor.Demo.Api`, `src/Tempo.Blazor.Demo.Server`, `src/Tempo.Blazor.Demo.InteractiveAuto`).

Every section here corresponds to one entry of the CHANGELOG's `## 2.10.0 → Breaking / Migration`
section; the identifiers **B1–B17** are the contract's own numbering
(`planning/tm-email-template-editor-2.10.md` §3.11). Nothing is a surprise rename: 2.10.0 is one release
that changes the packages (B7), the registration (§2), the save contract (B1), the preview seam (B2), the
escaping (B3) and the DOM of the editor (B16).

The migration order below is the order that keeps the tree compiling: packages → registration → save →
preview → templates → DOM.

---

## 0. Quick reference

| # | Breaking | What to change |
|---|---|---|
| B1 | **yes** | `OnSave` is `Func<EmailSaveRequest, Task<EmailSaveResult>>?`; `OnAutoSave` is gone; `SaveAsync()` returns the result |
| B2 | **yes** | `IEmailPreviewRenderer` is the preview seam; `TmEmailTemplatePreview` injects it |
| B3 | **yes** | variable values are HTML-escaped in the body; non-allow-listed URL schemes become `#` |
| B4 | **yes** (binary) | `ITemplateEngine.Render` / `IEmailTemplateRenderer.RenderAsync` have one signature with optional parameters |
| B5 | partly | `LayoutSeverity.Info = 2` — an exhaustive `switch` without `default` stops compiling |
| B6 | partly | strings are embedded JSON; `AddTempoEmailTemplates()` no longer calls `AddLocalization()` |
| B7 | **yes** | new package `Tempo.Blazor.EmailTemplates.Engine`; `AddTempoEmailTemplateEngine()` moved into it |
| B8 | output | serialized documents carry `schemaVersion: 1` |
| B9 | equality | `TemplateVariableInfo.Description` / `Group` participate in record equality |
| B10 | behaviour | the "MJML" button is called "Export" (`data-tm-mjml-btn` kept) |
| B11 | behaviour | inspector field labels are localized keys, not `Humanize(property.Name)` |
| B12 | a11y/UX | import/export dialogs: `TmModal.Footer`, `TmCodeEditor`, host-configured limits |
| B13 | a11y/UX | validation findings are `<button>`s, with counts and `Info` severity |
| B14 | **explicitly not** | `EmailImportOptions.MaxInputBytes` stays 5 MB |
| B15 | additive | real overloads; the 2.9.x signatures remain |
| B16 | **yes** (canvas DOM) | the WYSIWYG canvas paints the e-mail, not a "type + text" list |
| B17 | **yes** (raw block) | a raw block no longer lets executable markup through |

---

## 1. Packages (§1 of the plan, B7)

**Before** — two packages:

```xml
<PackageReference Include="Tempo.Blazor.EmailTemplates" Version="2.9.0" />
<PackageReference Include="Tempo.Blazor.EmailTemplates.Abstractions" Version="2.9.0" />
```

**After** — three, and only the rendering process gets the engine:

```xml
<!-- UI project (including a Blazor WebAssembly client) -->
<PackageReference Include="Tempo.Blazor.EmailTemplates" Version="2.10.0" />

<!-- the process that renders (API, worker) -->
<PackageReference Include="Tempo.Blazor.EmailTemplates.Engine" Version="2.10.0" />
```

`Tempo.Blazor.EmailTemplates` no longer references Scriban, Mjml.Net or AngleSharp. A WebAssembly host
that only edits does **not** download a template engine. The engine's dependency on `Abstractions` is
pinned to exactly `[2.10.0]` (the two assemblies share `internal` types through `InternalsVisibleTo`);
do not loosen that range.

`Tempo.Blazor.All` still contains neither email package — adding the editor to the metapackage would put
the editor's FluentValidation dependency into every consumer.

---

## 2. Registration (B7, B6)

**Before**:

```csharp
builder.Services.AddTempoEmailTemplates();   // registered the editor AND the local engine
```

**After** — one call for the UI, one for the renderer:

```csharp
// UI / WASM
builder.Services.AddTempoEmailTemplateEditor();
builder.Services.AddScoped<IEmailPreviewRenderer, MyApiPreviewRenderer>();

// API / worker
builder.Services.AddTempoEmailTemplateEngine();
```

`AddTempoEmailTemplates(Action<TemplateSecurityOptions>?)` still compiles (it is `[Obsolete]`) and now
delegates to `AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security))`. **It no longer registers
the engine**: a host that keeps calling it without adding the Engine package gets a preview in the
`Unavailable` state that names the missing registration instead of an empty iframe.

Three more registration facts changed:

* `AddTempoEmailTemplateEditor()` does not call `AddLocalization()` and registers no
  `IStringLocalizerFactory` (B6). The editor's own strings come from embedded JSON through a **closed**
  `IStringLocalizer<TmEmailResources>` registration, which wins over an open-generic factory.
* `ITemplateEngine` is replaced by `ITemplateValidator` in the UI (only the syntax check is a UI
  concern); the engine package registers the full `ITemplateEngine`.
* `HtmlContentSanitizer.Sanitize` (a static method in 2.9.x) is `IHtmlContentSanitizer` with
  `AngleSharpHtmlContentSanitizer` in the Engine and a conservative fallback in Abstractions.
  `TemplateVariableExtractor` (static) is `ITemplateVariableExtractor` /
  `ScribanTemplateVariableExtractor`.

---

## 3. Saving (B1)

**Before**:

```razor
<TmEmailTemplateEditor Document="document"
                       OnSave="document => SaveAsync(document)"
                       OnAutoSave="document => SaveAsync(document)" />

@code {
    async Task SaveAsync(EmailTemplateDocument document) => await Api.UpdateAsync(Id, document);
}
```

**After**:

```razor
<TmEmailTemplateEditor @bind-Document="document"
                       OnSave="SaveAsync"
                       AutoSave="new AutoSaveOptions { Enabled = true, Interval = TimeSpan.FromSeconds(3) }"
                       IsDirtyChanged="dirty => _dirty = dirty" />

@code {
    async Task<EmailSaveResult> SaveAsync(EmailSaveRequest request)
    {
        try
        {
            await Api.UpdateAsync(Id, request.Document, request.Revision, request.CancellationToken);
            return EmailSaveResult.Saved();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            return EmailSaveResult.Conflict(ex.Message);
        }
        catch (Exception ex)
        {
            return EmailSaveResult.Failed(ex.Message);
        }
    }
}
```

What to watch for:

* `request.Document` is a deep copy; you may serialize it while the user keeps typing.
* Auto-save is a **trigger**, not a callback: `request.Trigger == EmailSaveTrigger.AutoSave`. Enable it with
  the `AutoSave` parameter — `AutoSaveOptions { Enabled = true, Interval = … }`; `Enabled` defaults to
  `false`, and a `null` parameter means no auto-save at all. An
  `EmailSaveResult.Failed` keeps the document dirty; a `Conflict` also pauses auto-save until the next
  successful manual/shortcut/programmatic save or a new `Document` instance.
* The editor shows "Saved" only after `EmailSaveResult.Saved()` — it never reports success on its own.
* `SaveAsync()` is public and returns `EmailSaveResult`, which is what `TmNavigationGuard`'s
  `OnSaveAndLeave` needs:

```razor
<TmNavigationGuard IsDirty="_dirty"
                   OnSaveAndLeave="@(async () => editor is not null && (await editor.SaveAsync()).IsSaved)" />
```

Pass your own `_dirty` (from `IsDirtyChanged`), never `editor.IsDirty` directly: markup is re-read only on
the next parent render, so the guard would see a stale value.

---

## 4. Preview (B2)

**Before**: `TmEmailTemplatePreview` injected `IEmailTemplateRenderer` and rendered locally.

**After**: it injects `IEmailPreviewRenderer`, so the render may run on the server.

```csharp
// 2.9.x
public interface IEmailTemplateRenderer
{
    Task<RenderResult> RenderAsync(EmailTemplateDocument document, object? model = null, ...);
}

// 2.10.0 — the seam the editor uses
public interface IEmailPreviewRenderer
{
    Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default);
}
```

`EmailPreviewRequest` = `{ Document, VariablesJson, SubstituteVariables, CultureName }`. Return
`EmailPreviewStatus.Rendered` for a render (warnings allowed), `RenderFailed` when the *template* is
invalid, `Unavailable` when your transport/server failed (the editor then offers a retry), and throw
`OperationCanceledException` when the token fires.

Register it in every process that shows a preview, including WebAssembly:

```csharp
builder.Services.AddScoped<IEmailPreviewRenderer, HttpEmailPreviewRenderer>();   // posts to your API
```

The HTML export in the export dialog goes through the same interface.

`IEmailTemplateRenderer.RenderAsync` itself changed signature (B4): it now takes
`(document, object? model = null, TemplateRenderOptions? options = null, CancellationToken cancellationToken = default)`
and `ITemplateEngine.Render` takes `(template, object? model, TemplateRenderOptions? options = null)`.
Neither has a default interface implementation, so a compiled-against-2.9.x implementation must be
recompiled — a source change is not always needed (`Render(template, model)` still binds), but the
assembly must be rebuilt.

**New parameter** `TmEmailTemplatePreview.InitialDevice` (`EmailPreviewDevice`, default
`EmailPreviewDevice.Desktop`) — 2.9.0 had no device parameter at all, so nothing is renamed and an
existing call keeps working unchanged; a host that wants the preview to open on a phone opts in:

```razor
<!-- 2.9.0: no such parameter -->
<TmEmailTemplatePreview Document="doc" />
<!-- 2.10.0: opt into the starting device (Desktop is the default) -->
<TmEmailTemplatePreview Document="doc" InitialDevice="EmailPreviewDevice.Mobile" />
```

---

## 5. Templates: escaping and URLs (B3) — read this one twice

This is the change that can alter the output of an existing template. It is deliberate (owner decision 2)
and it is what makes it safe to hand author-supplied templates a model at all.

One change in the same area is purely additive and needs no migration: `h1`–`h6` joined the sanitizer's
allow-list (D28), so a heading an author writes is no longer flattened to text on the canvas or in the
export. The elements are emitted **without attributes** (`style`, `class` and `on*` stay refused), and
both sanitizer implementations share the one policy, so nothing becomes reachable that was not before.

### 5.1 Variable values are HTML-escaped in the body

**Before** — the value was spliced into the markup as-is:

```xml
<mj-text>Hello {{ user.name }}</mj-text>
<!-- user.name = "<b>Jan</b>"  →  Hello <b>Jan</b> -->
```

**After** — every expression in the HTML/MJML body is HTML-encoded, so a value can never inject a tag, an
attribute or a script:

```xml
<mj-text>Hello {{ user.name }}</mj-text>
<!-- user.name = "<b>Jan</b>"  →  Hello &lt;b&gt;Jan&lt;/b&gt; -->
```

The **subject, preheader and plain-text version stay raw** — they are text, not markup. The opt-out for a
value that really is trusted HTML is one explicit filter, and its output still passes the sanitizer:

```xml
<mj-text>{{ trusted_snippet | html.trusted }}</mj-text>
```

Migration guidance: replace any `{{{ value }}}`-style "triple mustache" expectation (Scriban has none) or
"print the model's HTML" pattern with `| html.trusted`, and nowhere else. `| html.escape` remains
available and is now the identity of the default behaviour.

### 5.2 URLs outside the allow-list become `#`

**Before** — `href="{{ link }}"` with `link = "javascript:alert(1)"` reached the exported HTML.

**After** — `javascript:`, `data:`, `vbscript:` and `file:` are refused in `href`/`src`/`background`/`model`
links, the value is replaced with `#`, the render reports a warning
(`RenderKeys.BlockedUrlScheme`, `render.url.blocked_scheme`) and an `ITemplateSecurityAuditSink` receives
`TemplateSecurityEventKind.BlockedUrlScheme`. Allowed: absolute `http`/`https`/`mailto`/`tel`, a relative
path, and a `#` fragment. A **protocol-relative** URL (`//cdn.example.com/x`) is no longer accepted as
"relative".

```text
before  <a href="javascript:alert(1)">x</a>   →  kept as authored
after                                          →  <a href="#">x</a> + warning + audit event
```

### 5.3 A raw block is an opt-in to raw markup, not to markup that runs (B17)

`MarkupGuard` now always removes `script`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`,
`base`, `form` and `portal` — together with their content for raw-text elements — from both the exported
MJML and the rendered HTML, reports
`render.markup.unsafe_element_removed` and audits `BlockedMember`. Migration: interactive content in an
e-mail must become a link to a hosted page. Styling stays possible (`style` attributes go through
`CssSafety`).

If you relied on the escaping lesson in reverse — "the engine will not touch my markup" — you now have
exactly one place to say so, `| html.trusted`, and one place where a template cannot go, the element
removal list above.

### 5.4 Sandbox behaviour that changed as an error, not silently

* A refused sandbox operation is a **render error**, not an empty string: `RenderResult.Errors` carries the
  violation message (`"Template is not allowed to access member 'x'."`, `… to access '…'`,
  `… to use '…'`, `… to use URL '…'`), and the audit sink receives the typed
  `TemplateSecurityEventKind.BlockedMember` / `BlockedNamespace` / `BlockedFunction` / `BlockedUrlScheme`
  with the refused name in `Detail`.
* A chained access through a null/undefined target renders empty instead of failing (`{{ a.b.c }}` with
  `a = null`).
* A POCO model no longer exposes its public **static** methods to templates.
* The render time budget is enforced even when a template writes no output.
* The member policy is fixed, not configurable.

### 5.5 No implicit Google font (Ubuntu)

**Before** — every render emitted Mjml.Net's built-in font table, so the HTML carried a `<link>` /
`@import` for the Ubuntu webfont even when the document declared no font at all.

**After** — that table is turned off: only a font the document itself declares is linked, and the markup
guard drops every `@import` that is not the `href` of a document `mj-font`. A template that relied on the
implicit Ubuntu webfont now renders with the next family of its `font-family` stack (Helvetica/Arial by
default). Migration — declare the webfont explicitly:

```xml
<!-- before: nothing declared, Ubuntu arrived implicitly -->
<!-- after -->
<mj-head><mj-font name="Ubuntu" href="https://fonts.googleapis.com/css?family=Ubuntu" /></mj-head>
```

```csharp
// the same, when the document is built in code
document.Styles.Fonts.Add(new EmailFont
{
    Name = "Ubuntu",
    Href = "https://fonts.googleapis.com/css?family=Ubuntu",
});
```

A host that wants no remote font at all simply declares none: nothing is fetched behind its back any more.

---

## 6. Model, serialization and import (B8, B14, B15)

* The serialized JSON now carries `schemaVersion: 1` (`EmailTemplateDocument.CurrentSchemaVersion`). The
  reader accepts a document without the property (it reads as 1) and refuses an unsupported version with
  `EmailTemplateSerializationException` + the localization key
  `serialization.schema_version.unsupported`. If you compare serialized documents in a test, update the
  expectation.
* `EmailImportOptions.MaxInputBytes` is still **5 MB**, `MaxSections` 200, `MaxBlocks` 2000,
  `MaxNestingDepth` 8. Tightening was explicitly rejected as a breaking change (B14). Pass your own
  `EmailImportOptions` to the editor's `ImportOptions` parameter or to
  `MjmlImporter.Import(mjml, includeResolver, options)` / `EmailTemplateSerializer.Deserialize(json, options)`
  to change them.
* The new overloads are additive (B15): `MjmlGenerator(IHtmlContentSanitizer)`,
  `EmailDocumentVariableExtractor.Extract(document, ITemplateVariableExtractor)`,
  `MjmlImporter.Import(mjml, IMjmlIncludeResolver?, EmailImportOptions?)`,
  `EmailTemplateSerializer.Deserialize(json, EmailImportOptions?)`. The 2.9.x shapes still compile.

---

## 7. The editor's DOM and behaviour (B10, B11, B12, B13, B16)

### B10 — "MJML" is called "Export"

```razor
<!-- before: the button read "MJML" -->
<!-- after: it reads "Export" (an added HTML tab, per-format copy and download) -->
```

The hook `[data-tm-mjml-btn]` is unchanged, so a selector keeps working; only visible text changed.

### B11 — inspector labels are localized, not humanized

**Before** — the property panel derived a label from the property name: `Humanize("ContentWidth")` →
"Content width", regardless of culture.

**After** — every field has a localization key in en/cs/sk/fr. A host test that matched the English
label ("Content width") must select the field's own hook instead
(`[data-tm-prop="ContentWidth"]`, plus `[data-tm-field-kind]` / `[data-tm-field-placement]`) or assert
against the localized text. The panel is also split into
Obsah/Styl tabs, and typed editors (colour, length, choice) replaced free-text fields — a stored value the
editor refuses is visible instead of silently disappearing.

### B12 / B13 — import, export and validation

The import and export dialogs gained a `TmModal.Footer` (Cancel/Import, Close/Download), `TmCodeEditor`
instead of a bare read-only `<textarea>`, host-configured limits, an always-available drop zone and a
structured preview. The validation panel renders findings as `<button>`s (keyboard operable), shows counts
per severity, and `LayoutSeverity.Info` is a new severity with the preheader length as its first rule:

```csharp
// B5 — enumerating severities
switch (finding.Severity)
{
    case LayoutSeverity.Warning: …
    case LayoutSeverity.Error:   …
    // 2.10.0 adds:
    case LayoutSeverity.Info:    …
    default:                     …   // ← without this the switch no longer compiles
}
```

### B16 — the canvas paints the e-mail

**Before** a block on the canvas was a "kind + text" list row. **After** it is the e-mail itself:

| 2.9.x | 2.10.0 |
|---|---|
| `.tm-email-canvas-block__kind`, `.tm-email-canvas-block__detail` | removed — the block's content is painted |
| — | `[data-tm-block-id]` wrapper around `[data-tm-block-body][data-tm-block-kind]` |
| `aria-selected` | `aria-current="true"` + a roving tabindex (`0` = selected/first block) |
| `aria-label` = the block type | localized type **+ a short content summary** |
| hover action bar | on `(pointer: coarse)` the action bar is **in flow** with `--tm-touch-target`; on desktop it floats above the block |

A selector written against `.tm-email-canvas-block__kind` must move to the block's own content.

### `TmEmailTemplateEditor` renders the core `TmEditorShell` (part of B16's phase)

The editor no longer has its own 20/60/20 `TmSplitter`. Its layout is the core shell's, and the layout
decisions are parameters:

```razor
<!-- before: <TmEmailTemplateEditor Document="doc" /> with a fixed three-pane layout -->
<!-- after: the same call, plus optional layout control -->
<TmEmailTemplateEditor Document="doc"
                       LayoutMode="TmLayoutMode.Auto"
                       MobilePanelPresentation="MobilePanelPresentation.Sheet" />
```

New parameters: `LayoutMode`, `InitialMode`, `ResolvedLayoutChanged`, `Breakpoints`,
`MobilePanelPresentation`, `ActiveMobilePanel`(+Changed), `ToolboxCollapsed`(+Changed), `ShowSaveStatus`,
`FooterActions`, `CanvasImageLoading`, `ItemEditingMode`, `Variables` (now with `Description`/`Group`,
B9) — all documented in [HOSTING.md](HOSTING.md) §8. The default `LayoutMode="Auto"` means an existing
host sees a responsive editor without changing a line.

---

## 8. Localization (B6)

**Before** — `.resx` files (`TmEmailResources.resx`, `EmailTemplateValidationResources.resx`) compiled into
satellite assemblies, `AddTempoEmailTemplates()` called `AddLocalization()`, and a host could override the
wording by adding its own `.resx`.

**After** — four embedded JSON tables (`Resources/TmEmailResources[.cs|.sk|.fr].json`), a closed
`IStringLocalizer<TmEmailResources>` registration and the public `JsonStringLocalizer<T>` (D25). To change
the wording, register your own localizer — a `.resx` copied into the host has no effect any more:

```csharp
builder.Services.AddSingleton<ITmEmailLocalizer, MyEmailLocalizer>();
// or replace the whole table:
builder.Services.AddSingleton<IStringLocalizer<TmEmailResources>, MyTmEmailResources>();
```

`TmEmailResourcesParityTests` guards key parity across en/cs/sk/fr, so a new key cannot ship untranslated.
Language selection follows the ambient culture (`CultureInfo.CurrentUICulture`) as before; the *render*
culture is separate (`TmEmailTemplatePreview.CultureName`).

---

## 9. Checklist

1. Add `Tempo.Blazor.EmailTemplates.Engine` to the rendering process only; bump every package to 2.10.0.
2. Replace `AddTempoEmailTemplates()` with `AddTempoEmailTemplateEditor()` (+ `AddTempoEmailTemplateEngine()`
   where you render locally).
3. Register `IEmailPreviewRenderer` in every process that shows a preview or exports HTML.
4. Rewrite `OnSave` to return `EmailSaveResult`; delete `OnAutoSave`; configure `AutoSave` options;
   pass `request.CancellationToken` to your I/O.
5. Wire `TmNavigationGuard` with your own dirty flag and `editor.SaveAsync()`.
6. Audit every template that prints a value which is meant to be markup → `| html.trusted`.
7. Audit every template that links to an author-supplied URL: it must be `http`/`https`/`mailto`/`tel`, a
   relative path or `#`.
8. Declare every webfont the template uses as a document `mj-font` (or `document.Styles.Fonts`): the
   implicit Ubuntu import is gone, so an undeclared font falls back to the `font-family` stack.
9. Update selectors: `.tm-email-canvas-block__kind`/`__detail` → `[data-tm-block-body]`; inspector labels →
   `[data-tm-field]` attributes; "MJML" button → still `[data-tm-mjml-btn]`.
10. Expect `schemaVersion: 1` in serialized documents and update any golden JSON.
11. If you had an exhaustive `switch` over `LayoutSeverity`, add the `Info` case.
12. Drop any `.resx` override; register `ITmEmailLocalizer`/`IStringLocalizer<TmEmailResources>` instead.
