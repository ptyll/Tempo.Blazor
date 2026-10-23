# Hosting the Tempo.Blazor email template editor (2.10.0)

Everything a consuming application has to do: which package to reference where, how to register the
services, how to render the preview on the server, how to save, how to guard the leave, how to localize,
and which limits apply. The template *language* (Scriban variables, filters, escaping, URL schemes) lives
in [TEMPLATE_SYNTAX.md](TEMPLATE_SYNTAX.md); the migration from 2.9.x, with a before/after for every
breaking change, is in [MIGRATION-2.9-to-2.10.md](MIGRATION-2.9-to-2.10.md).

Reference host: the demo applications in this repository —
`src/Tempo.Blazor.Demo` (WebAssembly, previews through the API),
`src/Tempo.Blazor.Demo.Api` (owns the engine and the store),
`src/Tempo.Blazor.Demo.Server` and `src/Tempo.Blazor.Demo.InteractiveAuto`.

---

## 1. Packages: three, and the split is load-bearing

| Package | Reference it from | What it brings |
|---|---|---|
| `Tempo.Blazor.EmailTemplates` | the UI project (including a Blazor WebAssembly client) | `TmEmailTemplateEditor`, `TmEmailTemplatePreview`, the dialogs, the model helpers, the JSON resource tables. **No** Scriban, Mjml.Net or AngleSharp. |
| `Tempo.Blazor.EmailTemplates.Abstractions` | shared contracts: models, DTOs, `IEmailPreviewRenderer`, `IEmailTemplateStore`, `IEmailSender` | no Blazor dependency; safe for an API or a background worker |
| `Tempo.Blazor.EmailTemplates.Engine` | the process that actually renders (your API, your worker) | Scriban, Mjml.Net, AngleSharp, the sandbox, the local preview renderer |

The editor never references the Engine, so a WebAssembly host does **not** download a template engine.
The preview and the HTML export go through `IEmailPreviewRenderer`, one interface, and the host decides
whether that is a local call or an HTTP round-trip. `Tempo.Blazor.All` does not include either email
package (adding the Engine to the metapackage would put Mjml.Net into every consumer).

The engine's NuGet dependency on `Abstractions` is pinned to an **exact** version (`[2.10.0]`), because
the two assemblies share `internal` types through `InternalsVisibleTo`. Do not loosen it.

---

## 2. Registration

There are exactly two methods, and they answer two different questions.

```csharp
// ── UI process (server, or WebAssembly) ────────────────────────────────────────────────────────────
builder.Services.AddTempoEmailTemplateEditor();   // UI + model. No engine, no Scriban, no Mjml.Net.
builder.Services.AddSingleton<IEmailPreviewRenderer, ApiEmailPreviewRenderer>();  // WASM host only

// ── rendering process (API / worker) ──────────────────────────────────────────────────────────────
builder.Services.AddTempoEmailTemplateEditor();   // or AddTempoEmailTemplateModel() when there is no UI
builder.Services.AddTempoEmailTemplateEngine();   // Scriban + Mjml.Net + AngleSharp + local preview
```

* `AddTempoEmailTemplateEditor(Action<EmailTemplateEditorOptions>? configure = null)` registers the UI,
  the block registry, the MJML generator/importer, the request validators, variable extraction, the
  conservative sanitizer and the editor's localization. It does **not** register a renderer and does not
  call `AddLocalization()`. Options: `configure(o => o.Security.LoopLimit = 1000)` configures the shared
  sandbox limits (see §9).
* `AddTempoEmailTemplateEngine(Action<TemplateSecurityOptions>? configure = null)` registers the sandboxed
  Scriban engine, the Mjml.Net compiler, the AngleSharp sanitizer (`IHtmlContentSanitizer`), the render
  pipeline and `LocalEmailPreviewRenderer` (which also implements `IEmailPreviewRenderer`). It also
  registers `ITemplateValidator` and forwards every `ITemplateSecurityAuditSink` and
  `ITemplateFunctionProvider` you registered.
* The two are **order independent** and idempotent; both write to the same `TemplateSecurityOptions`
  instance.
* `AddTempoEmailTemplateModel()` is the no-UI half (validators, generators, variable extraction) for a
  process that only renders.
* `AddTempoEmailTemplates(Action<TemplateSecurityOptions>?)` still compiles, is marked `[Obsolete]` and
  now merely delegates to `AddTempoEmailTemplateEditor()`. It no longer registers the engine — that is
  breaking change B7.

### The WASM pattern (the reference host)

```csharp
// src/Tempo.Blazor.Demo/Program.cs — the editor runs in the browser, the render does not
builder.Services.AddTempoEmailTemplateEditor();
builder.Services.AddScoped<IEmailPreviewRenderer, ApiEmailPreviewRenderer>();  // POSTs to the demo API
```

```csharp
// src/Tempo.Blazor.Demo.Api/Program.cs — the engine lives with the data
builder.Services.AddTempoEmailTemplateEngine();   // implies the model half; no UI
builder.Services.AddSingleton<IEmailTemplateStore, DemoEmailTemplateStore>();
builder.Services.AddSingleton<IEmailSender, DemoEmailSender>();
```

A missing `IEmailPreviewRenderer` is *not* an empty iframe: the preview renders
`EmailPreviewStatus.Unavailable` with a localized message that names the missing registration and offers a
retry button.

---

## 3. The preview on the server: `IEmailPreviewRenderer`

```csharp
public interface IEmailPreviewRenderer
{
    Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default);
}
```

`EmailPreviewRequest` carries the document, `VariablesJson`, `SubstituteVariables` and `CultureName`.
The contract the editor relies on:

| Situation | Return |
|---|---|
| rendered | `new EmailPreviewResult(html, text, subject, preheader, errors, EmailPreviewStatus.Rendered)` |
| the author's template is invalid | `EmailPreviewStatus.RenderFailed` + `RenderError[]` — **do not throw** |
| transport/server down, retry may help | `EmailPreviewStatus.Unavailable` + errors |
| cancelled | throw `OperationCanceledException` |

The editor cancels the previous request whenever a newer one starts, so a stale answer can never overwrite
a newer preview. The returned HTML is treated as **untrusted even when your own server produced it**: it
only ever reaches a `sandbox=""` iframe's `srcdoc`.

A minimal HTTP implementation:

```csharp
sealed class ApiEmailPreviewRenderer(HttpClient http) : IEmailPreviewRenderer
{
    public async Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("/api/email-templates/preview", request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return EmailPreviewResult.Unavailable();          // retryable
            }

            var body = await response.Content.ReadFromJsonAsync<RenderPreviewResponse>(cancellationToken);
            return body is null
                ? EmailPreviewResult.Unavailable()
                : new EmailPreviewResult(
                    body.Html ?? string.Empty, body.Text ?? string.Empty, body.Subject ?? string.Empty,
                    body.Preheader, MapErrors(body.Errors), body.Status);
        }
        catch (HttpRequestException)
        {
            return EmailPreviewResult.Unavailable();
        }
    }
}
```

The HTML export in the export dialog uses the same interface (`Export → HTML tab`), so an export in a
WebAssembly host is a server render as well — no second seam.

---

## 4. Saving: the host reports the outcome

```csharp
[Parameter] public Func<EmailSaveRequest, Task<EmailSaveResult>>? OnSave { get; set; }
```

```csharp
async Task<EmailSaveResult> SaveAsync(EmailSaveRequest request)
{
    try
    {
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
```

* `request.Document` is a **deep copy** taken when the request started, so the host may serialize it while
  the user keeps typing.
* `request.Trigger` is `Manual` (Save button), `Shortcut` (Ctrl/⌘+S), `AutoSave` (the debounced
  `AutoSaveOptions`) or `Programmatic` (`editor.SaveAsync()`). There is no separate `OnAutoSave` callback
  any more (B1).
* `request.Revision` is the editor's monotonic document revision the snapshot was taken at — the value a
  host echoes in its ETag/`If-Match`.
* `request.CancellationToken` is cancelled when the editor is disposed. Pass it to your I/O.
* Only `Saved()` clears `IsDirty` and shows "Saved". `Failed(message)` keeps the document dirty and shows
  the message; `Conflict(message)` additionally pauses auto-save until the next successful non-auto save or
  a new `Document` instance.
* Saving is **single-flight**: one request at a time, the button keeps focus and announces
  `aria-disabled` instead of being natively disabled, and a trigger accepted mid-flight merges into one
  follow-up save.
* The editor never reports success on its own. `TmEmailTemplateEditor.SaveAsync()` is public and returns
  `EmailSaveResult`, which is what a leave-guard needs.

### Resolving a conflict

Only the host can, because only the host owns the revision:

* **Reload** — deserialize the current server state into a **new** `EmailTemplateDocument` and pass it as
  `Document`. A new reference resets the baseline (clean), the pending conflict/failure state, the
  auto-save pause and the undo history.
* **Force save** — answer the next `Manual`/`Shortcut`/`Programmatic` request with `Saved()`.
* **Mutating the existing instance in place resets nothing** — the editor sees the same reference and
  treats it as an edit.

---

## 5. Dirty guard: `TmNavigationGuard`

```razor
<TmEmailTemplateEditor @ref="editor" @bind-Document="document"
                       OnSave="SaveAsync"
                       IsDirtyChanged="dirty => _dirty = dirty" />

<TmNavigationGuard IsDirty="_dirty"
                   OnSaveAndLeave="@(async () => editor is not null && (await editor.SaveAsync()).IsSaved)" />

@code {
    TmEmailTemplateEditor editor = default!;
    bool _dirty;   // the guard cannot read editor.IsDirty: markup is only re-read on the next render
}
```

Pass the host's **own** copy of the dirty flag (from `IsDirtyChanged`), never `editor.IsDirty` directly:
the component's parameter is re-read only when the parent renders, so the guard would compare a stale value.

`OnSaveAndLeave` must return `true` **only when the work is really persisted** — on `false` (or a thrown
exception) the guard closes the dialog, forgets the blocked destination and raises
`OnSaveAndLeaveFailed`, which the host wires to its own error surface.

The dialog's texts are localized by the guard itself since 2.10.0
(`TmNavigationGuard_ConfirmTitle`, `_ConfirmMessage`, `_Leave`, `_Stay`, `_SaveAndLeave`), so a Czech app
shows Czech without passing anything. The parameters (`ConfirmTitle`, `ConfirmMessage`,
`ConfirmLeaveText`, `CancelText`, `SaveAndLeaveText`) are `string?` and override the resource value.

---

## 6. `ReadOnly`

```razor
<TmEmailTemplateEditor ReadOnly="hostPinnedReadOnly" ... />
```

`ReadOnly` blocks every editing path: the toolbox, canvas drag & drop, the inspector (including its
sub-editors), import, undo/redo and the mutating keyboard shortcuts. Preview, validation and export stay
available, the save chrome is replaced by a `[data-tm-readonly-badge]` badge, no Save button is rendered,
and `SaveAsync()` answers `Failed` **without** calling the host.

The component never assigns `ReadOnly` itself; it is a host decision (a permission, a locked revision, an
archived template). If you want to combine a host pin with an internal toggle, keep the toggle in your own
state and pass the conjunction.

---

## 7. `ItemEditingMode` — inline or dialog

```razor
<TmEmailTemplateEditor ItemEditingMode="EmailItemEditingMode.Dialog" ... />
```

| Value | Behaviour |
|---|---|
| `Inline` (default, the 2.9.x behaviour) | every field of every list/table item is rendered inline in the inspector |
| `Dialog` | the inspector shows a compact list (preview + edit / remove / move) and an item's fields open in a modal **over a copy**: Save applies the copy as one undo step, Cancel/Escape/overlay-click leave the document untouched, and focus returns to the button that opened the dialog |

`Dialog` is what a narrow inspector or a touch device needs; the table cell editor has its own dialog in
both modes.

---

## 8. Appearance and responsiveness

The editor renders through the core `TmEditorShell` (Slots: Header / Toolbar / Left + LeftRail / Canvas /
Right / StatusBar / MobileActions). It has no local layout of its own — decisions EM2(a) and EM3 of the
2.10.0 plan.

| Parameter | Type | Default | Meaning |
|---|---|---|---|
| `LayoutMode` | `TmLayoutMode` | `Auto` | `Auto` measures the container and picks desktop/tablet/mobile; `Desktop`/`Tablet`/`Mobile` pin it (E2E and screenshots use this) |
| `InitialMode` | `TmLayoutMode` | `Desktop` | the assumed layout for the **first** render, before a measurement exists |
| `ResolvedLayoutChanged` | `EventCallback<TmLayoutMode>` | — | fires with the layout the editor actually resolved |
| `Breakpoints` | `TmLayoutBreakpoints?` | `null` | container widths (default 768 / 1200) |
| `MobilePanelPresentation` | `MobilePanelPresentation` | `Sheet` | below 768 px: `Sheet` = a non-modal bottom sheet with Bloky / Vlastnosti; `Tabs` = a Bloky / Plátno / Vlastnosti tablist replacing the sheet |
| `ActiveMobilePanel` / `ActiveMobilePanelChanged` | `EditorShellPanel` | `None` | which mobile surface is open; two-way bindable, and a one-way host value is adopted when it changes |
| `ToolboxCollapsed` / `ToolboxCollapsedChanged` | `bool` | `false` | the palette as an icon rail (also what a tablet's railed panel reports) |
| `ShowSaveButton` | `bool` | `true` | render the Save button |
| `ShowSaveStatus` | `bool` | `true` | render the save-status badge (dirty / saving / saved / failed / conflict) |
| `FooterActions` | `RenderFragment?` | `null` | host actions in the status-bar area |
| `ToolbarStart` / `ToolbarEnd` | `RenderFragment?` | `null` | host slots in the command bar; on a phone `ToolbarStart` moves into the header and `ToolbarEnd` into the "⋮" overflow |
| `CanvasImageLoading` | `CanvasImageLoading` | `Load` | whether the WYSIWYG canvas loads images (`Load`) or paints a placeholder (`Placeholder`) |
| `Variables` | `IReadOnlyList<TemplateVariableInfo>` | empty | the host's variable catalogue: `Path`, `Kind`, and since 2.10.0 `Description` and `Group`, which the picker shows next to the token |
| `ImportOptions` | `EmailImportOptions?` | `null` | the import limits (see §10) |

Layout vocabulary: **desktop** ≥ 1200 px keeps the docked palette + inspector; **tablet** 768–1199 px keeps
both panels but rails at most one of them; **mobile** < 768 px keeps the canvas and moves the panels into
the sheet (or the tabs).

---

## 9. Sandbox, audit and limits

`TemplateSecurityOptions` is one instance shared by the editor and the engine:

| Limit | Default | Protects |
|---|---|---|
| `LoopLimit` | 5000 | `for` iterations |
| `RecursiveLimit` | 100 | recursive includes/partials |
| `MaxOutputLength` | 5 000 000 chars | output size |
| `LimitToString` | 1 048 576 chars | a single `object → string` conversion |
| `ObjectRecursionLimit` | 20 | depth of that conversion |
| `Timeout` | 5 s | one render |
| `RegexTimeOut` | 1 s | catastrophic backtracking in `regex` filters |
| `StrictVariables` | `false` | whether an unknown variable is an error |
| `FunctionNamespaces` | empty | extra namespaces a template may call |

Every limit is validated when the engine is constructed and at `AddTempoEmailTemplateEngine`: a zero or
negative value (which Scriban would read as "unlimited") throws `ArgumentOutOfRangeException`.

### Audit sink

```csharp
sealed class SecurityLog(ILogger<SecurityLog> logger) : ITemplateSecurityAuditSink
{
    public void OnEvent(TemplateSecurityEvent e) =>
        logger.LogWarning("template {Hash}: {Kind} {Detail}", e.TemplateHash, e.Kind, e.Detail);
}

builder.Services.AddSingleton<ITemplateSecurityAuditSink, SecurityLog>();
```

`TemplateSecurityEvent.Kind` is `BlockedMember`, `BlockedNamespace`, `BlockedFunction`,
`BlockedUrlScheme`, `LimitExceeded` or `Timeout`. `Detail` names only the refused member/namespace/function
— never a model value. `TemplateHash` is the SHA-256 of the template, `TemplateExcerpt` its first 200
characters (authored PII: apply your own retention). Audit never breaks a render — a sink that throws is
logged and swallowed. The `BlockedUrlScheme` kind is what tells you a template tried `javascript:`.

### Host functions and culture

```csharp
sealed class CurrencyFunctions : ITemplateFunctionProvider
{
    public string Namespace => "app";
    public IReadOnlyDictionary<string, Delegate> Functions { get; } = new Dictionary<string, Delegate>
    {
        ["money"] = (decimal value, string culture) => value.ToString("N2", CultureInfo.GetCultureInfo(culture)),
    };
}

builder.Services.AddSingleton<ITemplateFunctionProvider, CurrencyFunctions>();
```

Templates call them as `{{ order.total | app.money "cs-CZ" }}`. A provider cannot override a Scriban
builtin namespace or another provider (that throws), its delegates are type-checked, and a result runs
through the same sandbox and the same automatic HTML escaping as any other expression. Register it as a
**singleton** — the engine snapshots the functions once when it is constructed.

The render culture comes from `TemplateRenderOptions.Culture` (or `UseDocumentLanguageCulture`), and
`TmEmailTemplatePreview.CultureName` passes it from the UI. Formatting therefore follows the recipient's
culture, not the server's ambient one.

---

## 10. Limits of an import

`EmailImportOptions` is applied by the editor's import dialog, by the MJML importer, by the JSON reader and
(string overload) by the server-side `EmailTemplateMapper`:

| Property | Default | Applies to |
|---|---|---|
| `MaxInputBytes` | 5 MB (5 242 880) | pasted/uploaded text and file, measured as UTF-8 bytes before parsing |
| `MaxSections` | 200 | document sections |
| `MaxBlocks` | 2000 | blocks, all sections together |
| `MaxNestingDepth` | 8 | nesting of containers |

`MaxInputBytes` stays at 5 MB on purpose: tightening it would be a breaking change for an existing host
(B14). A host that needs a different limit passes its own `EmailImportOptions` to the editor's
`ImportOptions` parameter.

---

## 11. Localization

The email packages ship their own strings as embedded JSON — `Resources/TmEmailResources[.cs|.sk|.fr].json`
in `Tempo.Blazor.EmailTemplates` — resolved through a **closed** `IStringLocalizer<TmEmailResources>`
registration (`JsonStringLocalizer<TmEmailResources>`, public since 2.10.0, D25) and exposed to the
components as `ITmEmailLocalizer`.

Consequences of the closed registration:

* `AddTempoEmailTemplateEditor()` does **not** call `AddLocalization()` and registers no
  `IStringLocalizerFactory`; a host that replaces the whole localization pipeline still gets real text.
* `TmEmailResources*.resx` and `EmailTemplateValidationResources*.resx` are gone (B6). An `.resx` override
  copied into a host no longer has any effect.
* To change the wording, register your own implementation — the `TryAddSingleton` registrations leave a
  host registration in place:

```csharp
public sealed class MyEmailLocalizer : ITmEmailLocalizer
{
    // delegate to the shipped localizer, override what you need
}

builder.Services.AddSingleton<ITmEmailLocalizer, MyEmailLocalizer>();
```

  or, for the whole table, `AddSingleton<IStringLocalizer<TmEmailResources>, MyLocalizer>()`. The
  `TmEmailResourcesParityTests` guard (in the repository, not in the package) fails when a key the
  packages hand to the localizer is missing in any of the four tables, so a new key can never ship
  untranslated.

The dialog, the inspector, the validation panel and the import/export dialogs all read their text from
the same tables; the only language the *host* supplies is `CultureName` on the preview.

---

## 12. Stable DOM hooks

Hosts and E2E select by attribute, never by position:

| Hook | Where |
|---|---|
| `[data-tm-email-editor]` | the editor root |
| `[data-tm-group="history\|review\|file\|help"]` | command-bar groups |
| `[data-tm-save]`, `[data-tm-save-badge]` (`data-tm-save-state="dirty\|saving\|saved\|failed\|conflict"`), `[data-tm-save-alert]`, `[data-tm-save-retry]`, `[data-tm-save-announcer]`, `[data-tm-readonly-badge]` | save chrome |
| `[data-tm-mjml-btn]` | the "Export" command (the hook kept its historical name) |
| `[data-tm-block-id]`, `[data-tm-block-body]`, `[data-tm-block-kind]` | the canvas block wrapper and its painted body |
| `[data-tm-nested-block]` | a nested (non-selectable) block inside another block's body |
| `[data-tm-import-warnings]` | the import dialog's warning list |
| `[data-tm-preview]`, `[data-tm-preview-state="…"]`, `[data-tm-preview-toolbar]`, `[data-tm-preview-device-group]`, `[data-tm-preview-substitution-group]` | the preview stage and its toolbar |

The command bar's groups are private `RenderFragment`s inside the editor, so a later phase can move one
into an overflow menu without breaking a host.

---

## 13. Checklist for a new host

1. Reference `Tempo.Blazor.EmailTemplates` in the UI project; add `Tempo.Blazor.EmailTemplates.Engine`
   **only** in the process that renders.
2. Call `AddTempoEmailTemplateEditor()` (and `AddTempoEmailTemplateEngine()` in the rendering process).
3. Register `IEmailPreviewRenderer` in every process that shows a preview — including WebAssembly.
4. Implement `OnSave` returning `Saved()` / `Failed()` / `Conflict()`, and pass `request.CancellationToken`
   to your I/O.
5. Wire `TmNavigationGuard` with your own dirty flag and `editor.SaveAsync()`.
6. Decide `ReadOnly`, `ItemEditingMode` and `LayoutMode` (leave `Auto` in production, pin for tests).
7. Supply `Variables` with `Description` and `Group` so the picker can explain each token.
8. Set `EmailImportOptions` if 5 MB / 200 sections / 2000 blocks / depth 8 do not fit your content.
9. Add an `ITemplateSecurityAuditSink` if you want refused sandbox operations in your log.
10. Serve the package's static assets: `tm-email-save-shortcut.js`, `tm-email-variable-insert.js`,
    `tm-email-panel-focus.js` are loaded as ES modules from `_content/Tempo.Blazor.EmailTemplates/`.
