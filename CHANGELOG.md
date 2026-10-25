# Changelog

## 2.10.0 (unreleased)

> 2.10.0 also ships every 2.9.1 entry below; when upgrading from 2.9.0 read both Breaking / Migration
> sections.
>
> Email template editor plan (`planning/tm-email-template-editor-2.10.md`). The full breaking-change
> summary is **B1–B17** in §3.11 of that document; a task-oriented before/after for every item is
> `docs/email-templates/MIGRATION-2.9-to-2.10.md` and the hosting contract is
> `docs/email-templates/HOSTING.md`.

### Added

- **`TmScheduler.HighlightWeekends` and `TmScheduler.AllDayEventStyle`.** Both are opt-in and default to the previous markup. `HighlightWeekends` (default `false`) adds `tm-scheduler-month-day--weekend` on a Saturday or Sunday month cell and `tm-scheduler-timegrid-col--weekend` on the matching day or week column; the wash is `--tm-scheduler-weekend-bg`. `AllDayEventStyle` is `Solid` (the default, the historical solid fill) or `Tinted`, which adds `tm-scheduler-event--tinted` and paints the shared tinted surface with `--tm-text-primary`, so the text stays at or above 4.5:1 on both themes. Today's time-grid column now carries `tm-scheduler-timegrid-col--today`, and a slot that does not start on the hour carries `tm-scheduler-timegrid-slot--half` (a dashed line). The day header is one culture-formatted line (`SchedulerRange.FormatColumnHeader` returns the weekday and the date: ("Tue", "5/12") in en-US, ("Út", "12.05") in cs-CZ), split into the existing weekday and date spans so a host selector still matches. Timed events share one surface, `.tm-scheduler-event-surface`, painted from `--tm-scheduler-event-tint` (14%), `--tm-scheduler-event-border-tint` (28%) and `--tm-scheduler-event-bar-width`.
- **`TmScheduler` agenda empty state.** An agenda range with no events now renders the shared `TmEmptyState` (calendar icon, `TmScheduler_AgendaEmptyTitle` "Nothing scheduled", `TmScheduler_AgendaEmptyDescription` "This range has no events.") inside the existing `.tm-scheduler-agenda-empty`. New resource keys, all four languages: `TmScheduler_AgendaEmptyTitle`, `TmScheduler_AgendaEmptyDescription`.
- **`TmSchedulerMonthView.CurrentDateChanged` and readable text on a light event colour.** PageUp/PageDown inside the month grid raise `CurrentDateChanged` with the target month's date; `TmScheduler` binds it to its own `CurrentDateChanged`, so a host that binds `@bind-CurrentDate` gets month paging from the keyboard. A host that renders `TmSchedulerMonthView` directly and ignores the callback keeps the current month, and the roving stop clamps to the nearest day the grid already shows. A consumer-supplied event colour keeps its solid fill. Before, white text was painted on every fill, which failed WCAG AA on the lighter colours (amber `#f59e0b` at 2.14:1, green `#10b981` at 2.53:1). Now the component sets `--event-text-color: var(--tm-scheduler-event-fg-on-light)` on that event when white text would fall below 4.5:1, so the text turns dark while the fill stays. The token is black in both themes: gray-900 flips to white in the dark theme, and a mid colour such as violet `#8b5cf6` clears 4.5:1 on black (4.96:1) but on neither white (4.23:1) nor `#111827` (4.19:1). A colour that clears white better than black keeps the theme's white text. A host that wants white text on its own light colour forces it back:
  ```css
  .tm-scheduler { --event-text-color: var(--tm-color-white); }
  ```
- **E-mail editor UX closing items (phase 8 of the follow-up plan).** The phone palette packs four tiles per row (three on a 320 px phone), so the
  whole first group clears the sheet's default snap in the demo at 390 px. The inline table grid shows a short decorative "Header" caption under each
  row switch and drops it through a named container query (`tm-email-table`, 24 rem) in a narrow inspector or sheet; the switch keeps its full
  `aria-label` (new resource key `EmailEditor_Table_RowHeaderShort`). The import dialog's structure preview is virtualized (`Virtualize`, fixed 28 px rows,
  `aria-level` / `aria-posinset` / `aria-setsize` per row, focusable scroll list) and lists EVERY block instead of stopping at 100; the state card counts are
  unchanged. Demo: the editor page ends above the fold at 1440x900 (its status bar and "Saved" badge are visible), the send page's form is
  `align-self: start`, and the editor page's chrome (switch-bar labels, select options, "Host: unsaved changes") comes from `DemoResources` in en/cs/sk/fr.
- **Wireframe stencils and roles for the e-mail components (decision EMAIL-WIREFRAME-ROLES).** `TmEmailTemplateEditor` (role `email-template-editor`),
  `TmEmailCanvasBlock` (role `email-block`), `TmEmailItemList` (`list-view`) and `TmEmailItemDialog` (`dialog`) are placeable stencils with a schema, a role and a
  declarative entry in the built-in pack; every other public e-mail component is editor chrome that resolves a role through the vocabulary by its type name.
- **`TmEmailVariablePicker.Filter` / `FilterChanged` (`@bind-Filter`) and an editor-owned inspector state.** The variable picker's search text is
  bindable; without a binding it behaves as before. `TmEmailPropertyPanel` takes the new optional `State` (`EmailInspectorState`, supplied by
  `TmEmailTemplateEditor`), which now also remembers the OPEN picker and its filter, so a desktop <-> tablet layout flip (which re-creates the
  inspector) no longer closes the picker and loses what was typed. The filter resets when the picker closes, not on a flip. The editor itself got no new
  parameter. Purely additive (decision EMAIL-VARIABLE-PICKER-FILTER).
- **`TmEmailTemplatePreview.DocumentRevision` (`long?`, optional).** A cheap change signature: when supplied, the preview decides whether to render
  again from the document reference and the revision, instead of serializing the whole document on every parent re-render. The editor passes its own
  revision; a host that does not supply it keeps the content comparison. Additive. Contract (decision EMAIL-PREVIEW-DOCUMENT-REVISION): the signature is
  (document reference, revision); an in-place edit that is not announced by a new revision is not a change, and an undo that restores earlier content
  still bumps the revision, so it costs one extra render of the same content.
- **Editor commit cost no longer grows with a serialize-per-edit.** `EditorHistoryService` stores immutable JSON snapshots with a monotonic
  `Version`; `IsDirty` is an integer version check with a string-compare fallback (no serialization per edit beyond the one history snapshot), and a commit is one
  serialization instead of a deep copy plus a second serialization (about 3x less model work at 3/52/200 blocks, measured in
  `planning/tm-email-template-editor-2.10.md`, 9.2). Behaviour is unchanged: undo onto the saved state is clean, an edit during an in-flight save stays
  dirty, a coalesced edit after a save is dirty. The snapshot no longer touches the live document (`EmailTemplateSerializer.SerializeContent` leaves out
  `UpdatedAt` itself). Measured keystroke latency on a 200-block document: in-process bUnit (Release) p95 44-61 ms for commit + render under a test
  renderer that repaints the whole tree; in the Blazor Server demo (browser `input` to the painted canvas block, 50 keystrokes) p50 8.5 ms, p95 13-22 ms
  on loopback, i.e. under the 100 ms target (new perf-lane spec `EmailEditorKeystrokePerformanceE2ETests`, bUnit `Category=Perf`; tables in the plan, 9.2).
- **`EmailTemplateSerializer.SerializeContent(EmailTemplateDocument)`.** `Serialize` without the `UpdatedAt` timestamp, without ever writing to the
  document. Additive.

- **Template sandbox audit trail (`Tempo.Blazor.EmailTemplates.Abstractions`).**
  `ITemplateSecurityAuditSink` + `TemplateSecurityEvent` (`Kind`, `Detail`, `TemplateHash`,
  `TemplateExcerpt`, `Timestamp`) report every refused sandbox operation:
  `BlockedMember`, `BlockedNamespace`, `BlockedFunction`, `BlockedUrlScheme`, `LimitExceeded`,
  `Timeout`. `BlockedUrlScheme` is emitted from the URL allow-list (B3); the enum value predates that
  pipeline so the audit contract stays stable. Register zero to many sinks in DI
  (`AddTempoEmailTemplateEngine` forwards all
  `IEnumerable<ITemplateSecurityAuditSink>`) or pass them to the `ScribanTemplateEngine`
  constructor. `Detail` names only the refused member/namespace/function — never a model value —
  `TemplateHash` is lowercase hexadecimal SHA-256 of the template, `TemplateExcerpt` is its first
  200 characters (may contain authored PII, so apply your own retention) and `Timestamp` comes from
  `TimeProvider`. Audit never breaks a render: a sink that throws is logged and swallowed.

  ```csharp
  sealed class SecurityLog(ILogger<SecurityLog> logger) : ITemplateSecurityAuditSink
  {
      public void OnEvent(TemplateSecurityEvent e) =>
          logger.LogWarning("template {Hash}: {Kind} {Detail}", e.TemplateHash, e.Kind, e.Detail);
  }

  services.AddSingleton<ITemplateSecurityAuditSink, SecurityLog>();
  services.AddTempoEmailTemplateEngine();   // picks every registered sink up automatically
  ```

- **`TemplateSecurityOptions` hardening limits.** `RegexTimeOut` (default 1 s, protects the `regex`
  filters against catastrophic backtracking), `ObjectRecursionLimit` (default 20, depth of the
  object→text conversion) and `LimitToString` (default 1,048,576 chars, single string conversion)
  join the existing `LoopLimit`, `RecursiveLimit`, `MaxOutputLength` and `Timeout`. Every limit is
  validated at engine construction and at `AddTempoEmailTemplateEngine`, so a zero/negative value
  (which Scriban would read as "unlimited") throws `ArgumentOutOfRangeException` instead of silently
  disabling a guard.

  ```csharp
  services.AddTempoEmailTemplateEngine(o => o.RegexTimeOut = TimeSpan.FromMilliseconds(200));
  ```

- **Dependency bumps.** `Scriban` 7.2.4 → 7.5.1 (sandbox `MemberFilter`, `RegexTimeOut`,
  `ObjectRecursionLimit`, `LimitToString`), `Mjml.Net` 4.11.0 → 4.15.0, `AngleSharp` 1.5.2 → 1.8.5
  (the HTML sanitizer parses untrusted markup, so it stays on the newest parser). The MJML golden
  documents are byte-identical after the bump. The test projects that pin AngleSharp transitively
  (bunit) moved to the same 1.8.5.

  Note on the `AngleSharp` bump: the previously pinned `1.5.2` was **not** affected by
  [GHSA-pgww-w46g-26qg](https://github.com/advisories/GHSA-pgww-w46g-26qg) (CVE-2026-54570, an mXSS
  via `annotation-xml` integration points) — that advisory covers all versions before `1.5.0`. The
  `NU1902` warnings the pins actually silenced came from `bunit` 2.7.2 pulling `AngleSharp` `1.4.0`
  into the UI test projects. `1.8.5` is simply the newest stable parser line for the sanitizer.

- **HTML escaping of variable values + safe URLs (`Tempo.Blazor.EmailTemplates.Abstractions`).** The
  output of every expression in the HTML/MJML body is HTML-encoded, so a value can never inject a tag,
  an attribute or a script. The subject, preheader and plain-text version stay raw (they are text), the
  literal template text is untouched and filters still see the raw value (`{{ name | string.size }}`
  measures the data, not the entities). The single opt-out is the new filter
  **`{{ value | html.trusted }}`**, whose output is always run through the HTML sanitizer first, so the
  tag/attribute allow-list keeps applying. `html.escape` now returns already-encoded markup and is
  therefore never encoded twice. See [docs/email-templates/TEMPLATE_SYNTAX.md](docs/email-templates/TEMPLATE_SYNTAX.md).

  ```csharp
  var result = await renderer.RenderAsync(document, new { name = "<script>alert(1)</script>" });
  // result.Html      → …&lt;script&gt;alert(1)&lt;/script&gt;…
  // result.TextVersion → <script>alert(1)</script>   (plain text pass is not escaped)
  ```

- **URL scheme allow-list.** Every URL is checked **after substitution**, both in the substituted MJML
  (before compilation, so a refused `background-url` never reaches the CSS/VML the compiler emits) and
  in the compiled HTML (covers raw blocks and host-set HTML attributes). `http`, `https`, `mailto`,
  `tel`, relative paths and `#fragment` pass; anything else — including `data:`, `vbscript:`, `file:`,
  `blob:` and unknown schemes, and obfuscations such as `java\tscript:` — is replaced with `#`, reported
  as a `RenderErrorSeverity.Warning` (`RenderResult.Success` stays `true`, `RenderResult.HasWarnings`
  turns `true`) and audited as `TemplateSecurityEventKind.BlockedUrlScheme`. The allow-list is a fixed
  security rule; a host cannot extend it. `HtmlContentSanitizer` now uses the same
  `UrlSchemePolicy` (so `tel:` links in rich text are kept, and character-smuggled schemes are refused).

  ```csharp
  var result = await renderer.RenderAsync(document, new { url = "javascript:alert(1)" });
  // result.Html    → <a href="#" …>
  // result.Errors  → [ RenderError { Key = RenderKeys.BlockedUrlScheme, … } ]
  ```

- **The markup guard is a linear tokenizer, and unchecked markup is never returned.** The tag scan is now
  a single-pass WHATWG-style tokenizer (`HtmlTagTokenizer`) instead of a backtracking regular expression:
  a document made of unterminated tags used to cost minutes (10 KB ≈ 2.4 s, 40 KB ≈ 36 s — quadratic),
  it is now linear (1 MB in well under a second). Every remaining regular expression inside the guard
  carries a 2 s match budget; a guard that cannot finish produces `RenderKeys.MarkupTooComplex`
  (`render.markup.too_complex`, `RenderErrorSeverity.Error`) and an empty `RenderResult.Html`.
  **Behaviour change:** `RenderResult.Html` is now `""` whenever an `Error`-severity diagnostic comes out
  of the compile or markup phase (a broken MJML structure, an unchecked body) — a host never receives a
  half-built or unchecked body. Warnings keep the complete HTML.

- **The markup tokenizer never skips content a browser may parse as tags.** Abruptly closed comments
  (`<!-->`, `<!--->`, `--!>`) and raw-text elements inside foreign content (`<svg><style>`,
  `<svg><title>`) are classic mutation-XSS vectors: the guard now scans comment payloads and the content
  of `style`/`script`/`textarea`/`title` for start tags instead of trusting the browser's raw-text state,
  while a `<!`/`<?`/`</` construct is skipped only to its first `>`. Outlook conditional comments
  (`<!--[if mso]>…<![endif]-->`) and their VML payload are scanned the same way — a legitimate
  `background-url` is untouched, so the compiled output does not change.

- **The markup guard drops markup that can rewrite a link at runtime.** SVG animation elements
  (`set`, `animate`, `animateMotion`, `animateTransform`, `discard`) and every
  `<meta http-equiv="refresh">` are removed with a warning (`RenderKeys.UnsafeElementRemoved` =
  `render.markup.unsafe_element_removed`, audited as `BlockedMember`) — a browser would run their
  `href`/`to`/`from`/`by` values after the static check. CSS is validated on a normalised copy
  (comments removed, `:`-style escapes decoded), `image-set(…)` and `@import` targets are checked like
  `url(…)`, and a stylesheet that still embeds a refused target is dropped. The tokenizer also removes the
  legacy false positives: `<img alt="a onerror=b">` and `title="href=javascript:x"` are left untouched,
  `data-onclick` survives.

- **`RenderError.Severity` (Error / Warning).** A security clean-up is a warning, not a failure:
  `RenderError` gained `RenderErrorSeverity Severity` (last optional parameter, default
  `RenderErrorSeverity.Error`), `RenderResult.Success` now means "no *error*" (`Errors.All(e => e.Severity
  != Error)`), and `RenderResult.HasWarnings` reports warnings. A blocked URL and a removed event handler
  are warnings — the HTML is complete and `Success` stays `true`, so `POST /api/email-templates/{id}/send`
  no longer answers `422` for a message that only had a link replaced with `#`; `TmEmailTemplatePreview`
  lists errors and warnings separately. Decision `EMAIL-RENDER-ERROR-SEVERITY` (D23).

- **Event-handler attributes are removed from rendered markup.** Escaping alone cannot neutralise
  `<div onclick="f('{{ x }}')">`: the browser entity-decodes the attribute value *before* it runs it as
  script. The markup guard now drops every `on*` attribute and `srcdoc` from raw markup (MJML and HTML
  pass), reports a warning (`RenderKeys.EventHandlerRemoved` = `render.markup.event_handler_removed`) and
  audits `BlockedMember` with the attribute name. `html.trusted` markup is serialized with a quote-safe
  formatter, so a value inside an attribute cannot close it — `html.trusted` belongs in element content,
  not in an attribute.

- **The URL guard also covers `srcset`, `ping`, `data`, `lowsrc`, `dynsrc`, `meta http-equiv="refresh"`
  and CSS `url(…)` inside `<style>`** (including `EmailStyle.Css`). Character references of any form
  (`&#58`, `&#0000000058;`, `&#x6A`, `&colon;`) are decoded before the scheme is judged, and a
  protocol-relative / UNC reference (`//host`, `\\host\share`, `/\host`) is refused as
  `network-path` — a "relative" link must not resolve a remote authority (SMB leak).

- **`RenderError.Key`.** A render diagnostic can now name a localization key
  (`RenderKeys.BlockedUrlScheme` = `render.url.blocked_scheme`) so a host — and
  `TmEmailTemplatePreview`, which resolves it through `ITmEmailLocalizer` — can present a localized
  message. Diagnostics that come straight from the MJML compiler keep `Key = null`.

- **`TemplateRenderOptions`.** Per-render options type threaded through
  `ITemplateEngine.Render(…, TemplateRenderOptions?)` and
  `IEmailTemplateRenderer.RenderAsync(…, TemplateRenderOptions?, CancellationToken)`. It is the
  extension point for render-scoped configuration (the culture of formatters); it deliberately has no
  escaping switch, and the renderer chooses the escaping mode per pass.

- **Audit sinks cover the URL allow-list.** `EmailTemplateRenderer` takes optional
  `IEnumerable<ITemplateSecurityAuditSink>`, `TimeProvider` and `ILogger<EmailTemplateRenderer>`
  parameters (all appended, source compatible); `AddTempoEmailTemplateEngine` forwards every registered
  sink, so `BlockedUrlScheme` events need no extra wiring. The audit detail is the scheme name only —
  never the URL.

- **Formatter culture per render (`TemplateRenderOptions.Culture`).** `TemplateRenderOptions` gained
  `CultureInfo? Culture` and `bool UseDocumentLanguageCulture`. A render now always uses an explicit
  culture: the one the host passes, else the culture of `EmailTemplateDocument.Language` when
  `UseDocumentLanguageCulture` is set, else `CultureInfo.InvariantCulture`. The renderer resolves it
  **once** and applies it to every pass (subject, preheader, body, text version), so
  `{{ price | math.format "N2" }}` renders `1 234,50` for `cs-CZ` and `{{ price }}` follows the same
  rule. An explicit `Culture` wins over the document language; an unknown document language falls back
  to the invariant culture instead of failing the render. The invariant default keeps output
  deterministic — a Czech server and an English server produce the same bytes unless the host asks
  otherwise.

  ```csharp
  await renderer.RenderAsync(document, model, new TemplateRenderOptions { Culture = new CultureInfo("cs-CZ") });
  // or: new TemplateRenderOptions { UseDocumentLanguageCulture = true }   → document.Language decides
  ```

- **Host function registry (`ITemplateFunctionProvider`).** An application can add its own template
  filters — a currency formatter, a product-name helper — without forking the engine. A provider
  exposes one namespace plus a dictionary of synchronous delegates, and the engine registers them as
  `{{ value | app.format_currency }}`. Registration is validated when `ITemplateEngine` is resolved:
  namespace `^[a-z][a-z0-9_]*$`, never a Scriban builtin namespace (`string`, `array`, `object`,
  `math`, `date`, `html`, `regex`, `timespan`, …), never a namespace another provider already owns,
  function names `^[a-z][a-z0-9_]*$`, synchronous delegates only and contract-typed parameters/return
  values (`string`, `bool`, numeric, `decimal`, `DateTime`, `DateTimeOffset`,
  `IEnumerable<T>` of those) — everything else throws `ArgumentException` at startup. A function's
  arguments run under the same member filter and limits as any other expression, its result is
  re-checked on every call (`Type`, delegates, forbidden namespaces and the internal
  `TrustedHtml`/`EscapedHtml` markers fail the render closed) and it is HTML-escaped exactly like any
  other expression output. Provider namespaces are also added to
  `TemplateSecurityOptions.FunctionNamespaces` (deep-copied by `Clone`), so a host-side template
  validator can tell a host function namespace from a data path.

  ```csharp
  services.AddSingleton<ITemplateFunctionProvider, CurrencyFunctions>();   // Namespace = "app"
  services.AddTempoEmailTemplateEngine();                                  // → {{ total | app.format_currency }}
  ```

  A provider must be a **singleton** (the registry snapshots it once; a scoped/transient registration is
  refused with `InvalidOperationException`), its namespace must not be a Scriban keyword (`for`, `this`,
  `if`, `end`, `null`, `…`), an enumerable result is materialized into a script array of scalars (so a host
  type that happens to be enumerable cannot be read through the template, and the enumeration is bounded by
  `LoopLimit`), and an exception thrown by a delegate is logged and surfaced as
  `Template function 'app.x' failed.` — never with the host exception's message (it could carry a path, a
  connection string or a value).

- **`EmailTemplateDocument.SchemaVersion` + `EmailTemplateSerializer.ReadSchemaVersion`.** The persisted
  document carries a schema version (`public static int CurrentSchemaVersion => 1`, a property, not a
  `const`). `Serialize` always stamps the current version, `Deserialize` migrates an older document to
  it (a document without the property is version 1) and a document written by a **newer** schema
  throws `EmailTemplateSerializationException` with `Key = SerializationKeys.UnsupportedSchemaVersion`
  instead of being silently mis-read. `ReadSchemaVersion(json)` reads just the header, which is what a
  host needs to decide about a migration or to show a friendly error. A non-numeric version is
  `SerializationKeys.InvalidSchemaVersion`.

- **Import limits (`EmailImportOptions`) and the reusable `EmailDocumentLimitsValidator`.** The MJML
  importer and the JSON deserializer now bound their input: `MaxInputBytes` (default **5 MB**, measured
  as a UTF-8 byte count — today's hard-coded behaviour, so this is additive, B14),
  `MaxSections` (200), `MaxBlocks` (2000) and `MaxNestingDepth` (8). Exceeding a limit is a **fatal**
  `ImportMessage` with a localization key (`import.input_too_large`,
  `validation.limits.sections|blocks|nesting`) and `ImportResult.Document` is `null` — never an
  exception. `Deserialize(json, EmailImportOptions)` applies `MaxNestingDepth` as a JSON-parser depth
  bound (a per-call copy of `Options`, the static instance stays untouched); the bound is derived from
  the model nesting depth (a block nesting level costs several JSON levels), so a document within the
  model limit always parses and a hostile payload still cannot nest without bound. The same limits are
  available server-side through `EmailDocumentLimitsValidator` before a document is saved.

  ```csharp
  var options = new EmailImportOptions { MaxInputBytes = 10 * 1024 * 1024 };
  var result = new MjmlImporter().Import(mjml, includeResolver: null, options: options);
  var violations = new EmailDocumentLimitsValidator(options).Validate(document);   // [] when it fits
  ```

- **New public `Tempo.Blazor.EmailTemplates.Abstractions.Localization.JsonStringLocalizer<T>`.** The
  core's JSON localizer lives in `Tempo.Blazor` and is `internal`, and this Abstractions package must not
  reference the Blazor package, so the implementation is published here for the email packages (and for a
  host that wants the same behaviour for its own marker type). It is `sealed` — compose it, do not derive.
  Register the resource type as a **closed** generic so it wins over an open-generic `IStringLocalizer<>`:

  ```csharp
  services.TryAddSingleton<IStringLocalizer<TmEmailResources>,
      JsonStringLocalizer<TmEmailResources>>();
  ```

- **New package `Tempo.Blazor.EmailTemplates.Engine` — the local render engine, split out of
  Abstractions (B7, decision `EMAIL-ENGINE-PACKAGE-SPLIT`).** `Tempo.Blazor.EmailTemplates.Abstractions`
  no longer carries `Scriban`, `Mjml.Net` or `AngleSharp`: the sandboxed Scriban engine, the Mjml.Net
  compiler, the AngleSharp HTML sanitizer, the render pipeline and the local preview renderer live in the
  new package, whose public types **keep their `…Abstractions.*` namespaces** (D21) — adding it is a
  `PackageReference` plus one call, never a `using` rewrite. The editor
  (`Tempo.Blazor.EmailTemplates`) references no engine at all and runs in Blazor WebAssembly.

  ```csharp
  // server (or any process that renders locally)
  builder.Services
      .AddTempoEmailTemplateEditor()    // UI + model: registry, generators, validators, import
      .AddTempoEmailTemplateEngine();   // Scriban + Mjml.Net + AngleSharp, local preview renderer

  // Blazor WebAssembly: the editor only, preview rendered by the host's server
  builder.Services.AddTempoEmailTemplateEditor();
  builder.Services.AddScoped<IEmailPreviewRenderer, MyApiPreviewRenderer>();
  ```

  Registrations are split and **order-independent**: `AddTempoEmailTemplateModel()` (Abstractions) installs
  the baseline with `TryAdd` — the BCL `ConservativeHtmlContentSanitizer`,
  `RegexTemplateVariableExtractor`, one shared `TemplateSecurityOptions` instance, the request validators
  with their closed JSON localizer and `MjmlGenerator` — and `AddTempoEmailTemplateEngine()` **replaces**
  the engine services (`AngleSharpHtmlContentSanitizer`, `ScribanTemplateVariableExtractor`,
  `ScribanTemplateValidator`, `MjmlNetCompiler`, `EmailTemplateRenderer`, `LocalEmailPreviewRenderer`).
  `AddTempoEmailTemplateEditor()` registers UI + model only, and `AddTempoEmailTemplates()` is the
  `[Obsolete]` shortcut that delegates to it. A CI-guarded test (`PackageBoundaryTests`) fails if
  Abstractions ever references Scriban/Mjml.Net/AngleSharp again, if the editor package references the
  engine, or if the engine's exact `[$(Version)]` pin on Abstractions drifts.

- **Replaceable preview renderer `IEmailPreviewRenderer` + `EmailPreviewRequest` / `EmailPreviewResult` /
  `EmailPreviewStatus` (`Tempo.Blazor.EmailTemplates.Abstractions.Rendering`, contract §3.4).** The editor
  gets its preview and HTML export through one seam, so the render may run on the server:

  ```csharp
  public interface IEmailPreviewRenderer
  {
      Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default);
  }

  var request = new EmailPreviewRequest
  {
      Document = document,                 // travels as EmailTemplateSerializer JSON
      VariablesJson = sampleDataJson,
      SubstituteVariables = false,         // false keeps the {{ }} tokens (HTML export)
      CultureName = "cs-CZ",
  };
  ```

  `EmailPreviewResult` never returns `null` strings: a render error is `RenderFailed` with empty
  html/text/subject, a template *warning* (blocked URL, removed event handler) keeps `Rendered` with the
  complete HTML (D23), and a missing registration, a network error or a server failure is `Unavailable`
  with a retry. `LocalEmailPreviewRenderer` (Engine) is the default; a host that renders elsewhere
  registers its own. The preview cancels the previous render whenever a new one starts, so a stale answer
  can never overwrite a newer one, and the HTML is untrusted even when the host's own server produced it —
  it only ever lands in a `<iframe sandbox="" srcdoc>` (no `allow-scripts`, no `allow-same-origin`).

- **`TmEmailTemplatePreview` redesign: device switch, substitution switch, sample-data editor and host
  actions.** The toolbar is now three pill tab strips (Desktop / Tablet / Mobile with `monitor`, `tablet`
  and `smartphone` icons; "With sample data" / "Without substitution"; HTML / plain text) plus a
  "Refresh preview" action. The scene shows the email as paper on `--tm-color-surface-secondary` with
  `--tm-shadow-md`, centred, at the document's own content width (Desktop), 768 px (Tablet) or 390 px
  (Mobile) — the widths live in `EmailPreviewMetrics`. The sample-data editor uses `TmCodeEditor`
  (`Language="json"`, `ValidateJson`) and sits behind an "Edit data" button instead of a `<details>`
  element. New parameters: `VariablesJsonChanged`, `InitialDevice` (`EmailPreviewDevice`),
  `ShowSampleDataEditor`, `ShowViewToggle`, `FooterActions`, `CultureName`.

- **`ITemplateValidator` + `ITemplateVariableExtractor` (+ their Engine implementations).** The editor's
  validation panel no longer needs `ITemplateEngine`: it takes `ITemplateValidator` and, with no validator
  registered, shows a localized explanation that names the missing registration
  ("register `ITemplateValidator`, e.g. `Tempo.Blazor.EmailTemplates.Engine`") while the structural
  findings keep working. The variable picker and sample data fall back to
  `RegexTemplateVariableExtractor` (pure BCL) and are replaced by `ScribanTemplateVariableExtractor` when
  the engine is registered. `EmailDocumentVariableExtractor.Extract(document, ITemplateVariableExtractor)`
  is the new overload; the existing `Extract(document)` keeps the baseline behaviour.

- **Editor save contract: the host reports the outcome, the editor owns `IsDirty`/`IsSaving` (§3.2,
  D1/D2/D16).** `TmEmailTemplateEditor` asks the host through `OnSave` with an `EmailSaveRequest`
  (deep-copy `Document` snapshot, `Trigger` = `Manual` | `Shortcut` | `AutoSave` | `Programmatic`, the
  editor `Revision` the snapshot was taken at, and a `CancellationToken` cancelled when the editor is
  disposed) and shows **exactly** what the host answered: `EmailSaveResult.Saved()` clears the dirty flag
  and the badge says "Saved <relative time>", `Failed(message)` keeps the document dirty and shows the
  host's reason, `Conflict(message)` does the same **and pauses auto-save** until a successful
  **non-auto-save** save (`Manual`, `Shortcut` or `Programmatic` — `Ctrl/⌘+S` lifts the pause too) or a
  fresh `Document` instance. An exception from the handler is a failed save
  plus a log entry — never a crash and never "Saved". `IsDirty`/`IsDirtyChanged` (only fires on a
  transition), `IsSaving`, and the public `SaveAsync()` (wire it into
  `TmNavigationGuard.OnSaveAndLeave`) complete the host surface. Saving is **single-flight**: at most one
  host request runs, the Save button stays focusable (it announces `aria-disabled` instead of a native
  `disabled`, which would drop a keyboard user's focus; the click is merged), and a trigger accepted
  mid-flight merges into exactly one follow-up save (only while the document is still dirty) whose result
  answers the merged caller. An outcome that arrives after the host replaced `Document` answers only its
  caller — it never clears the new document's dirty flag or repaints its badge.

  ```razor
  <TmEmailTemplateEditor @ref="_editor" @bind-Document="_document" OnSave="SaveAsync"
                         IsDirtyChanged="d => _dirty = d" />

  @* the host mirrors IsDirtyChanged; reading _editor.IsDirty in markup would be stale at guard time *@
  <TmNavigationGuard IsDirty="_dirty"
                     OnSaveAndLeave="@(async () => _editor is not null && (await _editor.SaveAsync()).IsSaved)" />

  @code {
      async Task<EmailSaveResult> SaveAsync(EmailSaveRequest request)
      {
          try
          {
              // a 409 means a newer revision exists — the host owns the ETag/revision
              await Api.SaveAsync(request.Document, request.Revision, request.CancellationToken);
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

- **Save-status badge, grouped command bar, host toolbar slots and `ReadOnly` (`TmEmailTemplateEditor`).**
  `ShowSaveStatus` (default `true`) renders a badge with an icon for "Unsaved changes", "Saving…",
  "Saved <relative time>" (refreshed every minute from `TimeProvider`, the phrase from the core
  `ITmRelativeTimeFormatter`, so the plural form is the language's own), "Save failed" and
  "Version conflict" (the last two coloured through the status tokens and told apart from "Unsaved
  changes" by a stronger border and an alert-triangle icon), coloured through the status tokens.
  `ShowSaveButton` (default `true`) switches the Save button off, `ToolbarStart`
  and `ToolbarEnd` are `RenderFragment` slots for the host's own actions (back, title, "Send test email"),
  and the bar itself is grouped by task — History (Undo/Redo, which spell their labels out at a container
  width ≥ 1200 px and stay icon + `aria-label` below that) | Review (Preview, Validate) | File (Import,
  Export) | Help; every action carries `aria-keyshortcuts` and a `title`.
  A failed or conflicted save also renders a full-width `TmAlert` under the command bar
  (`[data-tm-save-alert]`, `role="alert"`) with the host's complete message, a localized hint and a
  "Try again" action (`[data-tm-save-retry]`, a `Manual` save) — the pill badge stays short and truncates
  with its text in `title`; the badge itself is **no longer a live region**: one visually hidden
  `role="status"`/`aria-live="polite"` announcer (`[data-tm-save-announcer]`) speaks a confirmed
  manual/shortcut/programmatic save and stays silent for auto-save, "Saving…" and the minute tick
  (Failed/Conflict are announced by the alert, so nothing is announced twice).
  `ReadOnly` disables **every** editing path — the toolbox tiles, canvas drag & drop and
  inline block/section actions, the inspector's controls (a native `disabled` fieldset **and** a per-method
  `if (ReadOnly) return;` in every sub-editor, because a disabled control still delivers a synthesised
  change), import, undo/redo and the document-mutating shortcuts — while preview, validation and export
  keep working; the save chrome is replaced by a read-only badge with a lock icon
  (`[data-tm-readonly-badge]`), no Save button is rendered and unsaved changes stay as secondary text.
  `SaveAsync()` answers `Failed` without calling the host. The component never changes the flag itself, and
  `Ctrl+S` / `Cmd+S` save through `OnSave` with `Trigger = Shortcut` (the browser's own "save page" default
  is cancelled for that chord only, by the package's `tm-email-save-shortcut.js` module, which guards each
  editor element separately so two editors on one page do not disarm each other). New keys in all
  four tables (`TmEmailResources{,.cs,.sk,.fr}.json`): the five save states, the retry action, the
  conflict/failure hints, the read-only badge, the save tooltip, the shortcut categories and the
  image-upload errors (`EmailEditor_Relative_*` was removed — the core formatter supplies the phrase).

- **`ImageUploadOptions` — client-side limits checked before the host's upload handler
  (`TmEmailTemplateEditor.ImageUpload`, `TmEmailPropertyPanel.ImageUpload`).** `Accept` (the `accept`
  attribute syntax, default `image/*`) and `MaxBytes` (default 5 MB) are validated **before**
  `OnImageUpload` runs; a rejected file, a handler exception and an empty URL all produce a localized error
  in the panel and leave the block's `Src` untouched.

- **Inspector metadata types (additive).** `PropertyFieldPlacement`
  (`Content`/`Style`/`Padding`/`Advanced`: which tab and which collapsible group a field belongs to),
  `PropertyChoice` (`Value` + `LabelKey` of one `Choice`/`Alignment` option), and `EmailInspectorState`
  (the state one editor remembers: the folded groups **per block type** plus `LastFocusedField`, the target
  of the caret-less variable insertion). `PropertyFieldKind` gained `Color`, `Alignment`, `Choice` and
  `Length`; `PropertyField` gained `Owner`, `Placement`, `Choices`, `SupportsTemplate`, `LabelKey`
  (`EmailEditor_Field_{Owner}_{Property}`), `SharedLabelKey` (`EmailEditor_Field_{Property}`),
  `IsNullable()` (the nullable annotation decides whether an empty value means "unset"),
  `AllowsNegative` (only letter spacing) and `SetChoice`/`TryGetLength`/`SetLength`; the alias `GetChoice`
  and the static `PropertyReflection.IsRequiredLength` were removed while the API is still unreleased —
  ask the field instead of a helper (contract §3.10). `PropertyReflection` gained
  `GetFields(target, placement)`, `OwnerOf`, `TryParseLength`, `FormatLength` and `IsValidColor`.
  `EmailInspectorState` is public because it is the type of a parameter of the public
  `TmEmailObjectFields`, not because a host is expected to build one — a standalone instance keeps its
  own state, so existing markup needs no change.
  Also additive: `LayoutSeverityRank.Of` (the one sanctioned display order of `LayoutSeverity`),
  `EmailDocumentValidator.MaxPreheaderLength` and `DocumentValidationKeys.PreheaderTooLong` (the threshold
  the recommending rule measures and names), `TmEmailTemplateToolbox.Collapsed`/`CollapsedChanged`/
  `ShowCollapseToggle`, and `TmEmailPropertyPanel.OnDuplicate`/`OnDelete`. Nothing on the document model or
  the save contract changed.

- **`TmEditorShell.MobileActionsMaxVisible` (jádro, aditivní).** How many of the shell's
  `MobileActions` stay on the mobile action bar before the rest overflow into the bar's own "More"
  menu. Default `3` — the bar's own default — so a host that never sets it keeps exactly the
  earlier behaviour; a value below `1` is rejected with `ArgumentOutOfRangeException` (a bar with
  no action would be an unreachable set of commands). The email template editor sets it to `4` so
  its D20 actions (Zpět / Znovu / Náhled / Validovat) sit on the bar instead of behind "More".

- **Dialog item editing + table-cell link/background (`Tempo.Blazor.EmailTemplates`).**
  `EmailItemEditingMode { Inline, Dialog }` and `[Parameter] ItemEditingMode` on
  `TmEmailTemplateEditor` and `TmEmailPropertyPanel` (default `Inline` = the 2.9.x behaviour). In
  `Dialog`, the social / navbar / carousel / accordion block renders a compact list
  (`TmEmailItemList`: preview + move up / move down / edit / remove) and Edit opens
  `TmEmailItemDialog` over a **copy** of the item: Save applies it as one change (one undo step),
  Cancel / Escape / an overlay click change nothing, and the core `TmModal` (a full-height bottom
  sheet with the sticky footer on a phone) restores focus to the opening button. The dialog
  validates through `Tempo.Blazor.FluentValidation` with the editor's own localizer
  (`EmailSocialElementValidator`, `EmailNavbarLinkValidator`, `EmailCarouselImageValidator`,
  `EmailAccordionItemValidator`, `EmailTableCellValidator`): a blocked URL scheme, a missing required
  text/source/alt/title, an unparsable span or a colour outside the allow-list is reported **at the
  field** (`aria-invalid` + `aria-describedby`, no pre-flagging of an untouched form) and Save — which
  is always enabled — applies nothing and moves the focus to the first invalid field; only failures
  without a field reach the dialog's `role="alert"` summary. A new item is a copy too: Add opens the
  dialog and the item is inserted only on Save, so Cancel leaves the document and the undo history
  alone. The list names its item in every row action, announces a removal in a live region and shows an
  empty state. The accordion content and a text block's content are sanitized with the pipeline's own
  `IHtmlContentSanitizer` **before** the rich editor hands them to `innerHTML` (fail-closed: with no
  sanitizer the editor receives nothing, never the raw markup).

- **`EmailTableCell.Href` + `EmailTableCell.BackgroundColor` and the policies behind them
  (`Tempo.Blazor.EmailTemplates.Abstractions`, additive — `SchemaVersion` stays `1`).**
  `Href` is emitted as `<a href>` around the cell text, `BackgroundColor` as the `bgcolor`
  attribute. Both follow the same URL rules (B3): `EmailUrlPolicy` (the public view of the one URL
  allow-list — `http`/`https`/`mailto`/`tel`, a relative path without an authority, `#`; anything
  else becomes `#` with a `render.url.blocked_scheme` warning and a `BlockedUrlScheme` audit event)
  and `EmailColorPolicy` (`#rgb`, `#rrggbb`, or a named colour from a fixed safe set — an invalid
  value is **never written**, the attribute is omitted and the render reports
  `RenderKeys.InvalidTableCellColor`, a warning). `MjmlGenerator.Generate(document, options,
  diagnostics)` is the new additive overload that collects those warnings; `MjmlImporter` reads the
  anchor and `bgcolor` back and `TextVersionGenerator` writes a linked cell as `text (url)`. The
  generator replaces a blocked cell link with `#` on its own, so the MJML export that runs **without**
  the Engine package (WASM) cannot hand out a live `javascript:`/`data:` link either. On import a cell
  whose `<a>` is not its whole content keeps its full text and no link, an invalid `bgcolor` is dropped
  and a blocked link is kept for editing — both reported as `ImportKeys.InvalidTableColor` /
  `ImportKeys.BlockedTableLink` warnings. Colours of MJML **attributes** (navbar link, accordion
  item/title, social element) follow the inspector's `PropertyReflection.IsValidColor` (hex, `rgb()`/
  `hsl()`, keyword); `EmailColorPolicy` stays the narrower allow-list of the cell `bgcolor`, and
  `TextVersionGenerator` never prints a blocked link.

- **Import dialog rebuilt (`TmEmailImportDialog`).** Two modes (MJML / JSON) as a core `TmTabs`
  strip, a `TmFileDropZone` that names the supported formats and the limit, and a `TmCodeEditor`
  instead of a bare `<textarea>`. The limits come from the new `ImportOptions`
  (`EmailImportOptions`): `MaxInputBytes` (measured in UTF-8 bytes, default 5 MB — the value the
  dialog used to hard-code) bounds both the pasted text and the read file, and the section/block/depth
  limits go to `MjmlImporter` and to `EmailTemplateSerializer.Deserialize(json, options)`. A parsed
  input shows a state card (success / warning / error, with an icon and the section, block and warning
  counts) that expands into the localized warning list, plus a section → column → block structure
  preview with the localized block names. Every failure is a localized sentence keyed by
  `SerializationKeys.*` / `ImportKeys.*` — the exception message never reaches the UI — and a failed
  parse keeps both the pasted input and the document. The footer carries Zrušit / Importovat with the
  primary action disabled until the input parses, and the document is replaced only by the caller's
  `OnImport` (one undo step in the editor).
- **Export dialog rebuilt (`TmEmailExportDialog`).** Three modes (MJML / JSON / HTML) as a core
  `TmTabs` strip with a read-only `TmCodeEditor` per mode (correct Prism language), an icon copy
  button in the code corner with a polite live-region confirmation, and a footer with Zavřít plus a
  primary “Stáhnout {MJML|JSON|HTML}”. MJML is generated with `MjmlGeneratorOptions.ForExport`
  (lossless, including `mj-html-attributes`); JSON is the canonical content JSON; HTML goes through
  `IEmailPreviewRenderer` with `SubstituteVariables = false`, so the exported file keeps the
  `{{ … }}` tokens, with explicit loading / unavailable / failed states. The download uses the
  existing core helper `tmDataTable.downloadFile` with the slugged file name
  (`{slug(name)|email-template}.{mjml|json|html}`) and the right MIME type. Generation happens on
  OPEN and on a TAB CHANGE only — the old dialog regenerated on every parent render
  (`OnParametersSet`). The dialog title is “Export” (EM5).
- **Validation dialog (`TmEmailValidationPanel`).** A per-severity count summary (errors / warnings /
  suggestions) above findings that are real `<button>` elements (keyboard operable; the old
  `<li @onclick>` broke the keyboard-activation convention) with a severity icon, a TITLE, a
  DESCRIPTION and a “Go to” action that jumps to the block. Each rule key now has
  `{rule}_Title` / `{rule}_Description` in all four tables (`ValidationKeyShape.TitleOf` /
  `DescriptionOf`), the document is validated when the dialog OPENS (not on every parent render), the
  host's `ImportOptions` limits are part of the findings, and the dialog has a Zavřít footer.
- **`TmEmailTemplateEditor.ImportOptions`** (`EmailImportOptions?`, additive): the editor forwards the
  same limits to the import dialog and the validation panel, so “what the editor accepts” and “what the
  server accepts” cannot drift apart.
- **The public surface of the import/export/validation dialogs, confirmed by the user (§3.8/§3.10 of the
  contract).** `ValidationKeyShape` (`TitleSuffix`/`DescriptionSuffix`/`TitleOf`/`DescriptionOf`) is the
  one place that composes a rule's `_Title`/`_Description` keys; `ImportKeys.BlockedLink`,
  `ImportKeys.InvalidColor` and `ImportKeys.DroppedAttribute` are the import-side audit keys (an
  unusable link, a colour the message cannot carry, an attribute the pipeline may never write — the
  detail is the tag and the name, never the value); `EmailTemplateMapper.ToDocument(contentJson,
  EmailImportOptions?)` plus the `ApplyCreate`/`ApplyUpdate` overloads let the SERVER enforce the same
  limits as the import, and `ReportEmailTemplateGalleryStore(EmailImportOptions?)` applies them to the
  report-gallery store; `TmEmailValidationPanel.ImportOptions` puts the same limits into the findings;
  `TmEmailExportDialog.LayoutMode` / `TmEmailImportDialog.LayoutMode` take the editor's already resolved
  layout. All of it is additive — the parameterless members stay.
- **`TmCopyButton.OnCopied` and `TmCodeEditor.CopyButtonCopied` (core, additive).** The copy button
  now tells its host when the text really reached the clipboard (never when the clipboard refuses the
  write), so a read-only surface can confirm the copy in its OWN live region instead of hanging a
  handler on the box around it — a click in the box is not a copy.
- **Copy / retry / large-export affordances of the export dialog.** The copy
  confirmation comes from `TmCopyButton.OnCopied` and is reset before it is set, so a SECOND copy is
  announced again; the HTML tab explains a renderer that is not configured
  (`EmailEditor_Export_HtmlNotConfiguredHelp`) or unreachable (`EmailEditor_Export_HtmlUnreachableHelp`)
  and the unreachable case offers a real retry (`data-tm-export-retry`); an export over 200 000
  characters is still complete but renders without syntax highlighting, with a line saying so
  (`EmailEditor_Export_LargeNoHighlight`). Each format's content lives in the `TmTabPanel` it belongs
  to, so the strip's `aria-controls` points at a panel that exists.
- **Import dialog: the mode follows the file/content.** The drop zone accepts
  `.mjml,.html,.json,.txt` on BOTH tabs, a `.json` file switches the mode, and a pasted document that
  starts with `{` is parsed as JSON instead of failing as MJML. The pasted text is measured against
  `EmailImportOptions.MaxInputBytes` BEFORE parsing (`EmailEditor_Import_InputTooLarge`, with the
  actual limit), and the state card carries the replace note (`EmailEditor_Import_ReplaceNote`) so the
  destructive step is stated before the user presses Import.
- **Validation panel: the good outcome and the quiet counts.** “No issues found” is a
  success card with the check icon and the success text token, a severity whose count is zero drops its
  severity colour (`tm-email-validation__count--zero`), the “syntax was not checked” note sits under the
  findings it qualifies, and “Go to” on a DOCUMENT finding opens the e-mail settings (nothing selected)
  instead of closing the dialog onto an unchanged canvas.
- **`RenderKeys.InvalidAttributeValue` / `ImportKeys.DroppedAttribute` (additive).** An attribute value
  the message cannot carry is now reported when the generator drops it, and an imported attribute the
  pipeline may never write is warned about at import (`import.dropped_attribute`) instead of living in
  the model as a value every export silently refuses.
- **Shortcuts help subtitle.** The editor passes
  `TmKeyboardShortcutsHelp.Description` (“Shortcuts work with an external keyboard.”) and lists ↑ / ↓
  as two separate key alternatives instead of one chord (F8 API).
- **Table grid: accessible, sectioned, scrollable (`TmEmailTableEditor`).** The grid is now a
  `<table>` with a `<caption>`, a localized `aria-label` on every cell ("Row {0}, column {1}"), a
  pinned control column (`position: sticky`), a visually distinct header row, a "Header" section with
  the "First row is a header" toggle, a "Rows and columns" section, and an "Edit cell" button on every
  cell that opens `TmEmailTableCellDialog` (text, link, colspan, rowspan, alignment, background, the
  row's header flag, plus a mini table preview with the edited cell highlighted) — in both item
  editing modes. The block ships its own scoped `TmEmailTableEditor.razor.css` (design tokens only),
  the actions wrap into a grid at a narrow width and the icon controls are at least 32 px
  (`--tm-touch-target` on a coarse pointer).


### Changed

- **Closing of the editor-redesign follow-up plan (phase 9): committed visual baselines and lane documentation.** The Wireframe phase 5-7 sets
  (`__screenshots__/wireframe/phase5|6|7`), the `TmDocumentOpenDialog` mobile set (`__screenshots__/document-library/mobile`) and the dark 390 / 1024 e-mail
  import and validation dialogs (`f10-import-dark-*`, `f10-validation-dark-*`, test `F10_14_Dark_Dialogs_AtPhoneAndTablet_StayReadableAndInView`) are now
  tracked, each directory names its producing test in a `README.md`. They are rewritten only with `TM_E2E_UPDATE_BASELINE=1`
  (see `docs/e2e-test-lanes.md`, "Updating committed baselines"). No product behaviour changed.
- **E-mail import dialog: the structure preview is a flat virtualized list (phase 8; changed during the 2.10 cycle, so not a breaking change against a released version).** Impact: (1) the nested `<ul>` per section / column is gone — every
  section, column and block is now one `<div class="tm-email-import__tree-row" role="listitem">` (`data-tm-import-section|column|block` unchanged) inside
  `<div role="list" data-tm-import-tree>` (no `<ul>`/`<li>` any more; the structure section is named through `aria-labelledby`, not a hard-coded label); (2) the 100-row cap and its note are gone: `[data-tm-import-structure-more]` and the resource key `EmailEditor_Import_TreeTruncated`
  were removed, and a host that overrides `ITmEmailLocalizer` can drop that key; (3) only a window of rows is in the DOM, so a spec that counted every
  `[data-tm-import-block]` must read the complete count from `[data-tm-import-tree][data-tm-import-rows]` (or the state card's `data-tm-import-blocks`).
  Before: `cut.FindAll("[data-tm-import-block]").Count == blocks` (capped at 100). After: `tree.GetAttribute("data-tm-import-rows") == blocks + sections + columns`, and
  scroll `[data-tm-import-tree]` to bring a later row into the DOM.
- **The MJML HEAD is emitted through the same gate as the body (security blocker).**
  `mj-attributes` carries a TAG NAME and attribute NAMES, `mj-font` a URL and `mj-html-attributes` a raw
  name and value — all of them used to be written verbatim, so `<mj-all x" onclick="alert(2)="1">`, a
  per-component default keyed `mj-text><script>…`, an `mj-html-attribute name="onclick"` or an
  `mj-font href="javascript:…"` produced a live handler, a script element or a live link in the export.
  The head now goes through the same rules the body uses: a per-component default's tag must be a known
  `mj-*` component (`MjmlKnownAttributes.PerTagTargets`), every attribute name has to be one that tag
  accepts (plus inert `data-*`), a colour has to be a colour (`CssColourValue`), a URL goes through
  `EmailUrlPolicy` and an HTML attribute name may not be `style`, `srcdoc` or an `on*` handler. The
  export therefore always stays well-formed XML.
- **An imported attribute is re-emitted only when this pipeline may write it (security 2).** The DENY
  filter (reject `on*`/`style`) is replaced by the ALLOW-list `MjmlKnownAttributes.For(tag)` — a deny
  filter is one invented name away from a live handler
  (`srcdoc`, `formaction`, `xlink:href`). The value is judged by the kind of the attribute (URL →
  scheme allow-list, colour → `CssColourValue`, style-bound → `CssStyleValue`, anything else → a value
  that may not open a CSS declaration: no `; { } < > " ' \`, no `url(`, no `expression(`), a duplicate
  attribute is emitted once (two of the same name made the export unparsable and let the last one win),
  and the importer drops the ones it cannot keep WITH a warning. `css-class`/`mj-class` keep only safe
  class tokens.
- **Style-bound attribute values are validated at emission (security 3).** `padding`
  (and its sides), `border*`, `border-radius`, `font-family`, `font-size`, `width`, `height`,
  `line-height`, `letter-spacing` and the rest of `MjmlKnownAttributes`' style-bound set are copied into
  the element's inline `style` by the MJML compiler, so `"0;background:url(https://tracker)"` used to
  become a tracking pixel of its own. `CssStyleValue` allows only the characters a length, number,
  percentage, hex colour, keyword or font stack needs — a value outside it is dropped and reported
  (`RenderKeys.InvalidAttributeValue`), never "cleaned up". `container-background-color` is a colour
  (`MjmlAttributeBuffer.Colour`) and a carousel's `thumbnails-src` a URL (`MjmlAttributeBuffer.Url`).
- **A `<mj-style>` payload may not carry script-in-CSS constructs (security 6).** The
  payload used to lose only `<`; `CssSafety` now refuses the whole block when the stylesheet contains
  `expression(`, `behavior:`, `-moz-binding`, `@import` or a `url(…)`/`image-set(…)` target with a
  refused scheme (normalised first, so a comment or a CSS escape cannot hide it). Legitimate CSS —
  including an `https` background image — is unchanged.
- **The MJML generator now fail-closes every link and filters imported attributes (export
  hardening).** `MjmlGenerator` emits `href` / `src` / `background-url` through
  `EmailUrlPolicy` (`MjmlAttributeBuffer.Url`), so a blocked scheme (`javascript:`, `data:`,
  `vbscript:`, …) becomes `#` in the generated MJML — the export path runs without `MarkupGuard`, and
  it must not hand out a live link. The renderer keeps warning and auditing the same scheme:
  `ModelLinkScanner` now scans EVERY model link (button, image, table cell, navbar, social, carousel,
  hero/wrapper/section background, `mj-font`), not just table cells, so `RenderKeys.BlockedUrlScheme`
  (a warning) is unchanged. Attributes preserved from an import (`ExtraAttributes`) are filtered:
  the name must be a plain lower-case attribute name (never `on*`, never `style`) and a URL-bearing
  name goes through the same allow-list — previously an imported `onclick` or
  `href="javascript:…"` was re-emitted verbatim into the export. `css-class` / `mj-class` keep only
  safe class tokens. A `<mj-style>` CSS payload has every `<` removed (CSS never contains one), which
  closes a `</style><script>` injection into the compiled HTML; a `<mj-raw>` payload can no longer
  close its own element early.
- **Headings `h1`–`h6` are allow-listed by the inline-HTML sanitizer (D28).**
  `HtmlSanitizerPolicy.AllowedTags` — the ONE source both the AngleSharp and the BCL conservative sanitizer
  read — now contains `h1`…`h6`. They are FORMATTING elements exactly like `b`/`i`: both implementations
  emit the tag and its text and drop **every** attribute, so `onclick`, `style` and `class` still never
  survive.

  ```text
  before                                     after
  <h1>Ahoj</h1>   → escaped text (the heading  <h1>Ahoj</h1>   → the heading survives
                    tag was not allow-listed)
  <h1 onclick=x style="background:url(…)">Ahoj</h1> → <h1>Ahoj</h1> (both sanitizers)
  <h2><img src=x onerror=alert(1)></h2>             → <h2>&lt;img …&gt;</h2> (inert, heading kept)
  ```

  Why: a mail author writes typography with headings, and the WYSIWYG canvas (and the export) showed the
  tags as literal text. The XSS parity suite of both sanitizers carries the heading vectors, and
  `HtmlSanitizerPolicy` stays the single source of the allow-lists (a host still cannot extend it).

- **`TmEditorShell` raises `ResolvedLayoutChanged` BEFORE `CollapsedPanelsChanged` (jádro, behaviour).**
  Both callbacks describe the same layout transition (a desktop→tablet flip rails one panel through the
  tablet one-expanded-panel rule), but the collapse one used to arrive first — so a host that maps
  `CollapsedPanels` onto its own state (the e-mail editor's `ToolboxCollapsed`, D10) could not tell a rail
  the LAYOUT imposed from a collapse the user asked for, and adopted the rail as a preference (measured:
  a Mobile→Tablet flip pinned the palette to its rail for ever).

  ```text
  before — a host sees the collapse before it knows the layout changed
    CollapsedPanelsChanged(Left)     ← the tablet rule railed the palette
    ResolvedLayoutChanged(Tablet)

  after  — the layout first, then the collapse it caused
    ResolvedLayoutChanged(Tablet)
    CollapsedPanelsChanged(Left)
  ```

  The same pass still raises both, so nothing needs to be deferred: a handler can compare the layout
  generation it just recorded with the one the collapse arrives in and treat a same-generation raise as a
  layout state. A user collapse (the panel toggle, the strip) is unchanged — one `CollapsedPanelsChanged`
  and no layout callback.

- **The shell's sheet / Tabs tab is finger-sized under a coarse pointer (jádro, aditivní CSS).**
  `@media (pointer: coarse) { .tm-editor-shell__tab { min-block-size: var(--tm-touch-target) } }` in
  `_editor-shell.css`: Bloky | Vlastnosti (and Bloky | Plátno | Vlastnosti) are primary navigation on a
  phone, and a 30 px tab measured on a touch device is not a target. The visual label is unchanged; only
  the hit area grows.

- **The collapsed panel's rail is a named group.** A collapsed docked panel renders
  `<div class="tm-editor-shell__rail" role="group" aria-label="…">` (the panel's own title, `LeftTitle` /
  `RightTitle` or their localized default), so the strip of block icons it may carry is announced as the
  palette's group instead of a run of unnamed buttons.

- **The table grid's DOM and styling.** `TmEmailTableEditor` gained a `<caption>`, a
  header section, per-cell edit buttons and a scoped stylesheet; the cell input keeps
  `[data-tm-table-cell]` and the row controls keep `[data-tm-table-row-remove]` /
  `[data-tm-table-add-row]` / `[data-tm-table-add-col]` / `[data-tm-table-remove-col]`. A host that
  styled the old bare `<table>` needs to target the new BEM classes (see Breaking / Migration).

- **The editor canvas is WYSIWYG (B16).** `TmEmailTemplateCanvas` paints each block the way it will
  look in the inbox instead of printing "type + 80 characters of text": a text block renders its
  sanitized inline HTML, a button its pill with background/border radius/alignment, an image a real
  `<img>` with `src`/`alt`/`width`, a divider its rule, a spacer its height, a table a header row and its
  cells (a cell link marked as a link), social its elements, a navbar its links, a hero its background and
  nested content, a carousel its first image plus `1 / N`, an accordion its titles and a wrapper/group a
  frame with its nested sections/columns. Sections paint `BackgroundColor`/`Padding`/border/alignment and
  columns their width ratio, and the "paper" is as wide as `Document.Styles.ContentWidth` (600 px when
  unset). The block renderer is its own component (`TmEmailCanvasBlock`) that decides on its own whether
  to repaint (`ShouldRender` over the block's rendered-data signature), so a keystroke repaints one block
  instead of the document. Security is unchanged in direction and stricter in effect: block text goes
  through `IHtmlContentSanitizer` and loses every attribute, `mj-raw` is NEVER rendered as markup (an
  escaped placeholder with the first characters), template tokens are chips rather than substituted
  values, every `Src`/`Href`/`BackgroundUrl` passes the URL allow-list, and **no link on the canvas is
  navigable** — a click selects the block.

- **The canvas no longer renders a block's structural label (B16, breaking DOM).** The
  `.tm-email-canvas-block__kind` and `.tm-email-canvas-block__detail` spans are gone (the email itself is
  the content now) and the block's floating action bar gained a drag grip
  (`[data-tm-block-grip]`); the block wrapper keeps `[data-tm-block-id]`, `role="listitem"`, its
  `aria-label` (the localized block type), `aria-selected`, `is-selected` and every
  `[data-tm-block-action]`. On a coarse pointer the actions of the SELECTED block stay visible (no hover
  to reveal them) and their targets are `--tm-touch-target` (44 px). See Breaking / Migration.

- **`CanvasImageLoading` on `TmEmailTemplateEditor` / `TmEmailTemplateCanvas` (additive).** The
  canvas is the one place where a template's remote images become requests from the AUTHOR's browser (an
  imported template can carry a per-recipient tracking pixel). `CanvasImageLoading.Load` (default) keeps
  the faithful preview; `CanvasImageLoading.Placeholder` issues no request at all and paints a framed
  placeholder with the image's alt text.

  ```csharp
  <TmEmailTemplateEditor Document="_document" OnSave="SaveAsync"
                        CanvasImageLoading="CanvasImageLoading.Placeholder" />
  ```

- **`TmEmailCanvasBlock` + `CanvasImageLoading` as the public canvas surface (additive).**
  `TmEmailCanvasBlock` (`Block`, `CanvasImageLoading`, `Nested`) is the block renderer a
  host may now mount directly — e.g. a print/PDF host that wants the canvas approximation of one block —
  and it is `public` because Razor cannot open an `internal` component from markup (`RZ10012` is an error
  in this repo). `Nested` is for CONTENT inside a wrapper/hero/group frame: a nested block carries
  `[data-tm-nested-block]` instead of `[data-tm-block-body]`, so it is never a selection target. The enum
  `CanvasImageLoading { Load = 0, Placeholder = 1 }` (default `Load`) is unchanged in meaning.

  ```razor
  <TmEmailCanvasBlock Block="@block" CanvasImageLoading="CanvasImageLoading.Placeholder" />
  ```

- **`--tm-paper-danger-ink` (additive token).** The paper family (`--tm-paper*`) is theme-invariant
  by design, so it needs a danger ink that does NOT flip in dark mode: `--tm-color-danger-text` becomes
  `#fca5a5` there (≈2.3:1 on the white paper). `--tm-paper-danger-ink: #991b1b` (8.3:1) is declared next to
  `--tm-paper-ink` and, like the rest of the family, deliberately not re-declared in `tokens-dark.css`.
  The canvas paints a refused link with it. A host that rebrands the paper palette should define it too.

- **`TmNavigationGuard`'s dialog texts now resolve from the application localization (core, additive).**
  The five text parameters (`ConfirmTitle`, `ConfirmMessage`, `ConfirmLeaveText`, `CancelText`,
  `SaveAndLeaveText`) became `string?` with a `null` default and fall back to
  `TmNavigationGuard_ConfirmTitle` / `_ConfirmMessage` / `_Leave` / `_Stay` / `_SaveAndLeave` in all four
  `TmResources` tables. A Czech/Slovak/French application that renders the guard without passing every
  string therefore shows its own language instead of English — previously the ONLY way to translate the
  dialog was to re-type the copy the library already ships. The English values are byte-identical to the
  2.9.x defaults, and an explicit parameter still wins, so no host changes and no selector moves. This is
  the last hardcoded English copy an e-mail editor host had to override.


### Added

- **`TmWireframeEditor.ExportHttpClientName`: send the PNG / PDF export to an API on another origin.** A WebAssembly host next to a separate
  API (the demo) answered `405` for `api/wireframe/export/png|pdf` because the editor posted to its own origin. The new optional parameter
  names an `IHttpClientFactory` client (like `TmDiagramEditor.ExportHttpClientName`); empty keeps the injected `HttpClient`, and a missing
  factory or a client without a base address falls back to it. The `/wireframe-editor` demo names `DemoApi` in every editor, and now fills the
  full viewport width from 1400 px up (the layout's `max-w-7xl` cap is lifted), so the editor is the desktop layout at 1440.
- **`TmWireframeEditor`: review fixes of the phone (G2) layout.** Deselecting quick actions (Delete / Lock) hand the focus to the action bar
  instead of dropping it on `<body>`; a flip desktop <-> phone closes an open context menu and resets the connector tool (the script now also
  reports that fallback to .NET); the "opened by selection" flag is cleared once the user picks a tab or drags the sheet; the context action
  sheet rows are finger-sized with a fine pointer too; the zoom pill respects `env(safe-area-inset-right)`; `.tm-wd-editor__header-stack` is
  scoped to the phone layout; the canvas render snapshot includes `TestIdPrefix`.
- **`TmEditorShell.TabletPanelPresentation` (`Docked` | `Overlay`): one shared tablet overlay for the editors.**
  `TabletPanelPresentation` (`Tempo.Blazor.Components.Layout`, next to `MobilePanelPresentation`; `Docked = 0`, `Overlay = 1`; decision
  `EDITOR-SHELL-TABLET-PRESENTATION`) is a new shell parameter, one value for both sides, **default `Docked` = today's
  behaviour** (nothing changes for `TmDiagramEditor`, Modeling or the e-mail editor). With `Overlay`, on a tablet layout only,
  the Left panel is an overlay drawer and the Right panel a non-modal side sheet over a full-width canvas; at most one is open
  (opening one closes the other and raises its `LeftOpenChanged` / `RightOpenChanged` with `false`), they follow the existing
  two-way `LeftOpen` / `RightOpen`, have no rail and no resize separator, close with a localized × (`TmEditorShell_ClosePanel`,
  en/cs/sk/fr) or Escape and return the focus to the opener, the × is 44 px on `(pointer: coarse)` and the entrance fade is
  `prefers-reduced-motion: no-preference` only. Width is the `--tm-editor-shell-overlay-width` token (default `20rem`, at most
  60 % of the editor); `LeftOverlayTestId`, `RightOverlayTestId` and `OverlayCloseTestId` name the test ids (rendered verbatim). `LeftPanelId` / `RightPanelId` are public so a host toggle can set `aria-controls` next to `aria-expanded`; the overlay title is a level-2 heading. Documented in
  [docs/editor-shell.md](docs/editor-shell.md#tablet-overlay-tabletpanelpresentationoverlay).

  ```razor
  <TmEditorShell TabletPanelPresentation="TabletPanelPresentation.Overlay"
                 @bind-LeftOpen="toolboxOpen" @bind-RightOpen="inspectorOpen" …>
  ```
- **`TmToolbarFileButton`: a toolbar button that opens a file chooser and can leave the bar into "More".**
  Import JSON (and any "attach a file" command) used to be a `<label class="tm-toolbar-btn">` around an `InputFile`
  inside a `TmToolbarItem`; such an item has no menu entry that can open the chooser, so it had to stay `Pinned` and
  an earlier attempt to make it `Secondary` left the toolbar unstable. The new core component
  (`Tempo.Blazor.Components.Toolbar`, decision `TOOLBAR-FILE-BUTTON`) takes `Icon`, `Text`, `Tooltip`, `Accept`,
  `Multiple`, `OnChange` (`EventCallback<InputFileChangeEventArgs>`), `Priority`, `Disabled`, `Class`, splatted
  attributes and the `TmComponentBase` test ids. On the bar it IS a `TmToolbarButton` (same classes, labels, priority,
  rank, roving tab stop); in "More" it is an ordinary entry that opens the SAME hidden `InputFile`. The input is a
  sibling of the button, outside the collapse wrapper, so a collapse or expand never remounts it; a native
  capture-phase `click` listener (one per page) calls `input.click()` inside the browser's own click, so WebKit keeps
  the user activation; the button is a native `<button>` with no key emulation. A cancelled chooser raises no
  `OnChange`; the input is cleared before every open so the same file can be chosen twice. It is a wireframe component
  (role `toolbar-button`, stencil in `tempo.stencil.json`) with a JSON overlay and a `/toolbar-forms` demo card.
  Documented in [docs/toolbar.md](docs/toolbar.md) ("File buttons"), including the stability analysis.

  ```razor
  <TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Editor tools">
      <TmToolbarButton Icon="@IconNames.Save" Text="Save" />
      <TmToolbarFileButton Icon="@IconNames.Upload" Text="Import JSON" Accept=".json"
                           Priority="ToolbarButtonPriority.Secondary" OnChange="@ImportAsync" />
  </TmToolbar>
  ```
- **Explicit query scope for the document library (`DocumentLibraryScope`, `DocumentLibraryQuery.Scope`).**
  `DocumentLibraryQuery.FolderPath = null` was ambiguous: the contract says "the root", yet the open
  dialog's Recent node and its search also sent `null` to mean "the whole library". The new
  `DocumentLibraryScope { Folder = 0, Subtree = 1, Library = 2 }` (namespace `Tempo.Blazor.DocumentLibrary`,
  Abstractions) states the reach explicitly: `Folder` (default — the direct content of `FolderPath`, exactly
  today's behaviour), `Subtree` (the folder and all of its sub-folders) or `Library` (the whole library,
  `FolderPath` ignored). `ITempoDocumentLibraryProvider` and `DocumentOpenResult` are unchanged; a provider that
  cannot honour the wider scopes may degrade `Subtree` / `Library` to `Folder`. The demo `DocumentLibraryStore`,
  the Demo.Api `browse` endpoint (`?scope=Subtree|Library`) and the demo `ApiTempoDocumentLibraryProvider`
  implement all three values. An undefined `?scope=` value (`99`, a typo) is a `400 Bad Request`. Decision `DOCLIB-QUERY-SCOPE` (see [docs/adr/0003-document-library-query-scope.md](docs/adr/0003-document-library-query-scope.md)).

  ```csharp
  await provider.BrowseAsync(new DocumentLibraryQuery
  {
      Kind = TempoDocumentKind.Wireframe,
      Scope = DocumentLibraryScope.Library,      // every folder; FolderPath is ignored
      SortField = DocumentLibrarySortField.Modified,
      Descending = true
  });
  ```

  The open dialog now asks for the whole library with `Scope = Library` (and `FolderPath = null`) for the Recent node
  and for a search. This is additive, not breaking: a custom `ITempoDocumentLibraryProvider` that ignores `Scope`
  still receives `FolderPath = null` and behaves exactly as before (a provider without support may degrade to
  `Folder`), and a provider that treated `null` as "everywhere" keeps working. A provider that read `null` as "the root"
  shows only root documents under Recent until it honours the scope. Migration — honour the new scope on a
  folder-aware store:

  ```csharp
  // before: Recent returned the root documents only (null == root)
  docs = docs.Where(d => d.FolderPath == (string.IsNullOrEmpty(q.FolderPath) ? "/" : q.FolderPath));

  // after: Library reaches every folder, Subtree the folder and its descendants, Folder is unchanged
  docs = q.Scope switch
  {
      DocumentLibraryScope.Library => docs,
      DocumentLibraryScope.Subtree => docs.Where(d => d.FolderPath == root || d.FolderPath.StartsWith(root + "/")),
      _                            => docs.Where(d => d.FolderPath == folder)
  };
  ```

  The reference stores keep the legacy rule that a `Folder`-scope query WITH a `Search` may span the whole library
  (existing clients are unaffected); callers wanting a library-wide search should state `Library`.

- **`TmDocumentOpenDialog` is a wireframe component.** The built-in stencil pack and `BuiltInComponentSchemas`
  list `TmDocumentOpenDialog` (category Files, roles `dialog` and `list-view`, prop `showRecent`), so the editor
  palette and the MCP catalogue (`wireframe_list_components`) can place it.
- **TmPdfAnnotator: annotation tools on every page of Continuous mode (FZE-14).** The PD1 pin is
  lifted: `ViewMode` (two-way, default `PdfViewMode.SinglePage` — additive) and
  `ShowViewModeToggle` (default `false`) now pass through to the embedded viewer, so hosts can
  run the annotator in `PdfViewMode.Continuous` and offer the single/continuous segmented control
  in the merged toolbar. In Continuous the annotator renders its marker layer and interaction
  surface PER PAGE through the viewer's FZE-13 `PageOverlayContent` slots (one geometry pipeline,
  no second pinch/drag — the shared `pointer-gesture.js` keeps owning two fingers): a stamp, a
  comment, a highlight and every ink point normalize against the slot's page box and anchor on
  THE page whose surface received the event (the annotator's current page is navigation state
  only), an ink stroke stays confined to the page where it started — a stroke dragged across the
  boundary is clipped there, and `touch-action: none` stays on the Draw surface only so the other
  tools keep the native pan/pinch — drafts and composers stay bound to the draft page while its
  slot is scrolled away and back, and selecting a thread in the panel scrolls to its anchor
  through the shared `TmPdfViewer.ScrollToAnchorAsync(page, y)` path (new public API) instead of
  leaving Continuous. Export/print are view-mode independent and cover every page, and the G2
  mobile rules (Browse + Add comment only) hold in Continuous. `TmRedactionLayer` explicitly
  pins its own viewer to `SinglePage` with no toggle (redaction measures against THE canvas).
  The per-page surface carries `pdf-annotator-surface` + `data-page` (see Breaking / Migration).

- **TmPdfViewer: annotations in Continuous mode — PD1 superseded (FZE-13).** The viewer now renders
  annotation markers and selection-driven threads on every page of the continuous column, not just in
  single-page mode. The continuous scroll wrap hosts the JS-owned pages container (unchanged lazy
  render: IntersectionObserver + serial queue, canvas release, the 12-painted-page limit) plus a
  Blazor-owned overlay-slot container; `pdf-viewer.js` reports the page boxes of the pages inside the
  ±50% viewport band through the new `OnContinuousPageGeometry` interop callback (deduped, after every
  build, in-place zoom, rotation, resize and band change), and Blazor mounts one absolutely positioned
  slot per reported page — a 120-page document with a thread on every page keeps the Blazor diff
  bounded to the mounted band. Text selection in Continuous resolves the page from the wrap containing
  the anchor (`enableSelection` grew a continuous branch), normalizes the rects against that page's box
  and clamps a cross-page selection to the anchor page, so `OnTextSelected` and the sticky CTA create
  the thread on the correct page. Selecting a thread in Continuous no longer switches to SinglePage —
  it scrolls (`tmPdfViewer.scrollToPage` + new `tmPdfViewer.scrollToAnchor`, centering the anchor's Y)
  and highlights the marker once the slot mounts. New templated parameter `PageOverlayContent`
  (`RenderFragment<PdfPageOverlayContext>` — `PageNumber`, `IsCurrentPage`, `Width`, `Height`,
  `Rotation`) renders a host overlay per page in BOTH view modes (single-page: the current page only,
  riding the canvas wrap under the new `pdf-page-overlay` test id). The removed
  `TmPdfViewer_AnnotationsSinglePageOnly` hint is gone; see Breaking / Migration.

- **TmPdfViewer & TmPdfAnnotator: the "All (N) | Unresolved (M)" thread filter (FZE-12).** A new
  shared `TmPdfThreadFilter` card (PD4, next to `TmPdfThreadList`) renders as the segmented
  control in BOTH panels' headers — the docked desktop/tablet chrome and the sheet presentations
  alike. The segments are native radios sharing one group name (the
  `TmRadioGroup Appearance=Segmented` model: the browser owns the arrow keys, one activation per
  key press, no emulation), carry live counts from the `Threads` set — `Loc.Plural` families
  `TmPdfThread_FilterAll` / `TmPdfThread_FilterUnresolved` — and map onto the panels' unchanged
  `ShowResolved`/`ShowResolvedChanged` contract (All ↔ `true`; the viewer's
  `ShowResolvedAnnotations` keeps working). The Unresolved filter has its own localized empty
  state (`TmPdfThread_EmptyUnresolved`, "No unresolved threads") when it hides every thread.
  New test ids: `pdf-annotation-filter` / `pdf-annotator-filter` with `-all` / `-unresolved`
  segments (see Breaking / Migration for the checkbox they replaced). The counts recompute on
  every render, so a resolve/reopen mutation updates them immediately.

- **TmPdfAnnotator: runtime color picker for new annotations (FZE-12).** The toolbar's color
  chip became a `pdf-annotator-color-trigger` BUTTON (`aria-haspopup="dialog"`,
  `aria-expanded`, accessible name = localized `TmPdfAnnotator_ColorLabel` + the current
  color's name) that opens the core `TmColorPalette` in an overlay panel — an anchored popover
  on desktop/tablet (44px targets on a coarse pointer) and the F3 bottom sheet on a mobile
  layout, where a color tile rides the Browse/Add-comment palette row. New parameters:
  `AnnotationColorChanged` (two-way to the existing `AnnotationColor`), `ColorPalette`
  (default `PdfAnnotationColorHelper.DefaultPalette` — the helper stays the palette's owner),
  `ColorNames` (default the localized `TmPdfAnnotator_ColorName_*` names of the default
  palette) and `AllowColorPicker` (default `true`; `false` restores the pre-2.9.1 static
  chip + label indicator). The picked color applies ONLY to annotations created afterwards
  (`CreateStampAsync`, `CreateFromDraftAsync`, the draft/ink `DraftColor`) — existing threads
  are never recolored (`IPdfAnnotationProvider` has no color update and Abstractions is
  unchanged). A "Reset to author color" action (`TmPdfAnnotator_ResetColor`) inside the
  palette returns new annotations to the per-author/role resolution; the selection survives
  tool switches and is dropped when a new `Url` loads. Swatch colors render as inline
  `style` from the data palette (allow-list) — never CSS hard-coding.

- **TmPdfAnnotator: per-session undo/redo for annotations (FZE-15).** Every mutation of the
  annotator — create (comment, highlight, stamp, ink), reply, edit, resolve, reopen and delete —
  now flows through a package-internal `PdfAnnotationHistory` command stack (100 steps, one per
  annotator instance, cleared when `Url`/`DocumentId` changes; the redo tail is dropped by every
  new action) and is undoable/redoable with the new public `UndoAsync()` / `RedoAsync()` /
  `CanUndo` / `CanRedo`, the `AllowUndo` parameter (default `true`) and the `OnHistoryChanged`
  callback — without any change to `IPdfAnnotationProvider`. Undoing a delete re-creates the
  thread with the same anchor, kind, color, ink and stamp and replays its replies (new ids are
  remapped across the stack); a step whose thread another user changed or deleted in the
  meantime is SKIPPED with a localized hint (the command is dropped, the stack stays consistent),
  and any other provider failure leaves the stack untouched. Undo/redo select the affected
  thread and, in Continuous, scroll to its anchor. The toolbar gains a `history` group (the
  frozen DiagramEditor group name) BEFORE the tools — undo-2/redo-2 icons, `aria-keyshortcuts`,
  an action-naming tooltip ("Undo: Delete comment") and an `aria-live` announcement after each
  step; on a phone the pair rides the shared "More" menu. Keyboard: Ctrl/⌘+Z and Ctrl/⌘+Shift+Z
  (or Ctrl+Y) on the annotator root (`!e.Repeat`, inert while an editable inside the annotator
  has the focus — the native text undo wins there). Drafts never enter the stack: in a Draw
  draft Ctrl+Z removes the last stroke.

### Fixed

- **`TmScheduler`: keyboard and ARIA in every view (B24).** The month grid is an ARIA grid (`role="grid"` / `row` / `columnheader` / `gridcell`, labelled `TmScheduler_CalendarGrid`) with one roving tab stop that opens on the displayed date: arrows move a day or a week, Home/End the row, PageUp/PageDown the month (raising the new `CurrentDateChanged`, which `TmScheduler` binds to its own `CurrentDateChanged`). Time-grid and timeline slots share one roving tab stop instead of one per slot (336 in a week); Up/Down move a slot and Left/Right a column (a resource column in the time grid, a resource row in the timeline), and Enter raises `OnSlotClick` once. A modified arrow (Alt, Ctrl, Meta, Shift) is left to the browser. Events are `role="button"` with an accessible name worded in the culture's own time format (`TmScheduler_EventAriaLabel` "{title}, {start} to {end}", all-day `TmScheduler_AllDayEventAriaLabel`) and activate on Enter (keydown, repeat-guarded) and Space (keyup); they are tab stops only inside the active cell, column or row. The context menu comes from the native `contextmenu` event (right click and Shift+F10), so the coordinates are the element's. The toolbar view switcher is a `radiogroup` of `radio`s with one roving stop and the Print button sits beside it, the title is `aria-live="polite"` and `aria-atomic="true"`, and the agenda is a `role="list"` (only when it has events) whose items each hold a `role="group"` labelled by the date. Every focusable carries a `:focus-visible` ring from `--tm-focus-ring`; a slot's ring is inset so it stays inside the cell. A consumer-supplied event colour keeps its solid fill, and the text switches to the dark `--tm-scheduler-event-fg-on-light` token when white text would fall below 4.5:1 (see Added for the before/after). An `EventTemplate` must not contain its own focusable element: the wrapper is already the button, and the template's time is announced from a screen-reader prefix. New resource keys, all four languages: `TmScheduler_EventAriaLabel`, `TmScheduler_AllDayEventAriaLabel`, `TmScheduler_MultiDayEventAriaLabel`, `TmScheduler_SlotAriaLabel`, `TmScheduler_DayCellAria` (a plural family), `TmScheduler_GridKeyboardHint`, `TmScheduler_ViewSwitcher`, `TmScheduler_CalendarGrid`, `TmScheduler_TimelineGrid`, `TmScheduler_AgendaList`.
- **`TmScheduler`: `EventTemplate` renders in every view, and `FirstDayOfWeek` drives the month grid (B14/B24).** The custom event template previously reached only the month view; the day and week time grid (timed events and the all-day row), the timeline and the agenda now render it too, keeping their positioning wrapper and resize handles. `FirstDayOfWeek` (previously only the week columns) now also sets the month grid's first column, the loaded week range and the week title. See Breaking / Migration below for the range, navigation and "+N more" changes that ship with this fix.
- **Wireframe mobile (G2) review debt (plan 643bba06, carry-forward 439e11a6).** (1) Closing the mobile sheet returns the focus to the action-bar tile that opened it (it used to land on the first tile); a selection-opened sheet and a tile that moved into "More" still fall back to the first tile. (2) A selection that peeks the sheet is announced politely (new resource key `TmWireframe_AnnounceSelectionPeek`, en / cs / sk / fr). (3) The sheet's scroll reset moved from `tmWireframeDesigner.resetMobileSheetScroll` into `TmEditorShell` (`tm-editor-shell.js resetSheetScroll`); `tmWireframeDesigner.focusFirstMobileAction(root)` became `focusMobileAction(root, actionId)` - both are internal helpers of the packaged script. (4) The hold release-click guard no longer swallows a keyboard or assistive-technology click (`detail === 0`). (5) **Escape typed inside the mobile sheet closed nothing**: the canvas key handler consumed it (clear selection + `preventDefault`) before the sheet's focus trap, so a keyboard user could not dismiss the sheet; keys inside `.tm-editor-shell__sheet` now belong to the sheet. (6) The canvas' `ContentVersion` follows the new public `WireframeCommandStack.Revision` plus one `MarkContentChanged()` helper instead of six hand-bumped increments. Covered by `TmWireframeEditorMobileG2Tests`, the wireframe / overlay node lanes and the landscape (667x375, 844x390), keyboard-only and axe specs of `WireframePhase7MobileE2ETests`. No parameter changed.
- **`TmPdfViewer` stylesheet: the floating search card and the continuous overlay-slot rules are back (found by the plan-643bba06 phase-9 full E2E lane).**
  A merge (`bb606ca8`) had reverted `TmPdfViewer.razor.css` to an older copy, so the FZE-11 search card (`.tm-pdf-viewer__search-card`, its docked-panel
  offset and the wraps' `scroll-padding-top`), the FZE-13 `position: relative` of `.tm-pdf-viewer__continuous-wrap` (the annotation slots drifted by the
  wrap's offset), the continuous text-layer span rules and the `--tm-pdf-comments-panel-width` knob were missing: the search card rendered as an unstyled
  full-height block and 11 `PdfViewer_Continuous_*` / `PdfAnnotator_Continuous_*` E2E specs were red. The same stale copy also lacked the view-mode segment rules (`.tm-pdf-viewer ::deep .tm-pdf-viewer__view-mode` and its `.tm-radio-group-options` / `.tm-radio-segment` rules, written with `::deep` because the segmented control is a child component) and `.tm-pdf-viewer__search-count--empty`, and still carried the unused `.tm-pdf-viewer__annotations-hint` rule, which is removed. Restored from `48af2368`; no API change. `TmPdfViewerCssTests` now guards the search card, its docked-panel offset, `--tm-pdf-comments-panel-width`, the `position: relative` of the continuous wrap and the continuous text-layer span rule.
- **E-mail editor: `DocumentTree` traversals are iterative and visit every node once.** The recursive walkers (`CollectBlockLists`, `ReassignIds` and the
  other tree walks) overflowed the stack on a deeply nested document; they now use an explicit stack, are cycle-safe (visit-once) and are covered by
  deep-tree tests. The public `DocumentTree` methods document this behaviour in their `<remarks>`.
- **`TmDocumentOpenDialog` review debt (Recent / search scope, collapsed tree, contrast, footer, keyboard).**
  * Recent and the search without a folder send `Scope = Library` instead of `FolderPath = null`; the demo store
    therefore lists documents of NESTED folders in Recent (it used to list the root only).
  * Collapsing a folder hands `aria-selected` to the nearest VISIBLE ancestor of the active folder (pinned by
    bUnit and E2E: exactly one selected row, and it is the tree's tab stop).
  * Ink on the primary-subtle wash (active tree node, active view toggle, the document thumbnail chip) reads
    `--tm-color-primary-text`; `--tm-color-primary` measured 4.19:1 in the indigo dark theme. `TokenContrastTests`
    now holds both pairs to AA in default / indigo, light / dark.
  * At 1440 px the footer hint and "New folder" no longer wrap: the Link / Copy cards are a grid (radio + title on
    the first row, the hint on the full card width below), so "Insert a copy" and its hint stay on their own lines.
  * The on-screen keyboard lift ignores a pinch-zoomed page (the visual viewport shrinks without any keyboard) and
    only hides the Link / Copy cards for a hidden gap of keyboard size (>= 120 px), not for a sliding browser toolbar.
  * The row-menu sheet title ("More actions for {name}") wraps a long document name instead of pushing Done off the
    screen, and `FullscreenChromePx` (112 px hysteresis) is derived from `_modal.css` in a test so a spacing change in
    the modal cannot reintroduce the presentation flap.
- **Phase 2 test-suite stabilisation: culture fallback, security-patched packages and test-only fixes.**
  * **Unknown culture names fall back on every platform (`LocalEmailPreviewRenderer`, `EmailTemplateRenderer`).**
    `CultureInfo.GetCultureInfo(name)` on Windows NLS synthesizes a *custom* culture for any well-formed
    name (`"not-a-culture"` came back with the machine's number format), so the documented "unknown culture →
    renderer default" fallback only fired on ICU platforms. Both call sites now pass `predefinedOnly: true`,
    which makes an unknown preview `CultureName` / document `Language` fall back to the invariant culture
    everywhere. No API change; a host that relied on a Windows-only synthesized custom culture must pass a
    real culture name.
  * **NuGetAudit NU1902/NU1903/NU1904 closed.** `Microsoft.AspNetCore.SignalR.Client` (Collaboration,
    Demo.SharedUI) 10.0.5 → 10.0.13 (SignalR.Common GHSA-334f-vvp4-7g6p),
    `Microsoft.AspNetCore.Components.WebAssembly.Server` (Demo.InteractiveAuto, ReportServer.Web) 10.0.3 →
    10.0.13 (GHSA-g27p-qf6j-c96c, GHSA-qjpx-jqm5-h2c4) and `System.Security.Cryptography.Pkcs` pinned to
    10.0.13 in Demo.Api, ReportServer.Api and the Demo.Api tests (transitive through MailKit /
    Microsoft.Data.SqlClient; GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7). A consumer of `Tempo.Blazor.Collaboration`
    now resolves SignalR.Client 10.0.13 or newer.
  * **`TmPdfAnnotator` mobile-sheet auto-close debounce runs on `TimeProvider`.** Behaviour is unchanged
    (150 ms, `TimeProvider.System` when none is registered); tests drive it with a `FakeTimeProvider`.
  * **`DefaultMarkupSnapshotTests` compare void elements in one canonical spelling.** The 13 red scenarios
    were a serializer difference (bUnit 2.x / AngleSharp writes `<input ...></input>`, the goldens carry
    `<input ... />`); the markup itself is unchanged, so no golden file was regenerated.


- **Review fix (code + architecture): the canvas token mask, the image gate and the render gate.**
  Four defects the WYSIWYG canvas shipped with:
  * **A template token inside an attribute destroyed the tag around it.** Tokens are now masked
    POSITIONALLY (`U+E000{index}U+E001`) BEFORE the markup is reduced to the allow-listed tag stream, and
    restored to a chip in the same pass — so `<a href="{{ unsubscribe_url }}">Unsubscribe</a>` renders the
    inert `span.tm-email-block__link` with "Unsubscribe" instead of leaking `&lt;a` into the text, and
    `<span style="color:{{ c }}">x</span>` keeps its element (the attribute is dropped as a whole). A
    forged private-use placeholder (a character reference the sanitizer decoded) becomes `U+FFFD` and can
    never be mistaken for a mask. A token is also encoded exactly once (`HtmlEncode(HtmlDecode(token))`),
    so `{{ a &gt; b }}` no longer reads `&amp;gt;`.
  * **Any allow-listed URL could become an `<img src>`.** The canvas accepted `<c>mailto:</c>`, `tel:` and a
    RELATIVE path from the model, so an imported/JSON value such as `/api/logout` became a GET against the
    editor's own origin. The new `EmailCanvasValue.ImageUrl` is stricter than `Url`: an absolute
    `http`/`https` URL without a template token, or nothing. A refused value paints a placeholder with its
    reason — the new `EmailEditor_Canvas_ImageNotLoadable` ("This image can't be shown on the canvas (use a
    full https:// address)") for "not loadable", the existing `EmailEditor_Canvas_ImageBlocked` for a
    refused scheme — and a painted image is now `referrerpolicy="no-referrer" loading="lazy"`.
  * **The render gate was empty until the first re-render**, so the first parent re-render rebuilt EVERY
    block (measured: the first keystroke repainted all 52 blocks). The gate is primed in `OnInitialized`,
    which also makes the internal `RenderCount` mean "paints including the initial one".
  * **The block signature could be ambiguous:** values were joined with `U+001F` and no length, so a value
    containing the separator let two different blocks produce the same signature (the gate then showed
    stale content). Each value is now prefixed with its length (`-1` for null).
  * Smaller ones: `EmailCanvasValue.FontFamily` requires BALANCED quotes (an unbalanced `"` escaped the
    declaration it was interpolated into); `@key` on the section/column/block loops keeps a renderer instance
    with its block when items are reordered or removed; a refused hero/section
    background and a refused button/navbar/cell link are MARKED (`data-tm-image-blocked`,
    `data-tm-link-blocked` / `data-tm-cell-link-blocked` plus an alert icon and a `sr-only` reason) instead
    of silently missing; the "Empty block – add content in Properties" marker makes an empty text/button/
    list block visible and selectable; the image placeholder is styled through `::deep` (a
    `RenderTreeBuilder` fragment carries no scope attribute, so it painted as run-together text).

- **Review fix (UX): typing into the inspector no longer reorders the characters.** `TmRichEditorSimple`
  (core) re-synced the whole editor on EVERY parent re-render: it read the contenteditable and, when the
  HTML differed from the parameter, wrote the parameter back with `setHtml` — which moves the caret to the
  start, so the next character landed in front of the previous one (" EDIT" became "TIDE"). The component
  now remembers the value it emitted (`_lastEmitted`) and treats the host's echo of it as "no external
  change" (no DOM read, no write), while an external change (undo, a host write) still reaches the editor.
  `TmEmailPropertyPanel` no longer re-sanitizes the rich-text value on every render either: it sanitizes
  when the editor OPENS on a block and when the model changes externally (the F9 stored-XSS guard), and
  hands the author's own emission back verbatim. No public API changed.

- **Review fix (UX): canvas contrast, focus and the desktop action bar.** The canvas re-points the chrome
  tokens it inherits (`--tm-color-text`, `--tm-color-text-muted`, `--tm-color-surface-secondary`,
  `--tm-border-color-strong`, `--tm-color-primary*`) at their paper values on `.tm-email-canvas-doc`, so a
  dark chrome no longer paints a dark-theme token chip or placeholder on the WHITE paper (measured 2.1:1 →
  ≥4.5:1). A block reached with Tab but not selected shows a dashed primary ring (`:focus-visible:not(.is-selected)`);
  a focused action bar stays visible (`:focus-within`); the remove-column button reveals itself on focus; the
  desktop bar floats ABOVE the block instead of covering its first line; the coarse-pointer grip is hidden
  (HTML5 drag does not run on touch); the token chip is `0.85em` so it does not tower over body text; the
  document's `BackgroundColor`/`FontFamily` are painted on the paper; and the empty-column/raw-block texts
  now say what to do (`EmailEditor_Canvas_EmptyColumn`, `EmailEditor_Canvas_RawBlock`).

- **Inspector: an optional colour can be unset; the required one is never cleared.** A
  nullable colour property (a section's `BackgroundColor`, say) now renders the picker for an EMPTY value
  instead of a text field with a false "enter a color" error, the picker offers its clear button
  (`ShowClearButton` follows `PropertyField.IsNullable()`) and clearing it stores `null`. Clearing a
  required colour (`EmailTextBlock.Color`) keeps the stored value, and an invalid value from the picker is
  never written. Lengths parse with `AllowDecimalPoint | AllowLeadingSign` in the current culture and then
  invariant — a thousands separator (en-US „1,5“) is rejected instead of silently becoming 15, negative
  numbers are refused everywhere except letter spacing, and `font-style`/`font-weight`/`text-decoration`
  became localized Choice editors while a stored value outside the option set (`600`, `oblique`) stays an
  editable text field instead of disappearing. `Rel` moved to the Advanced group.

- **Inspector CSS actually applies now.** The field markup is built with
  `RenderTreeBuilder`, so its elements never carry the component's CSS-isolation scope attribute — every
  `.tm-email-fields__*` rule silently matched nothing (the label typography and the number + unit row
  measured as if unstyled). All rules are now `.tm-email-fields ::deep .tm-email-fields__*`, labels share
  one typography (`--tm-font-size-sm`, `--tm-font-weight-medium`, `--tm-text-primary`), the unit sits beside
  the number, and the Delete action of the inspector header stays danger-tinted.

- **Validation panel: severity is spoken, findings are operable, Info/Warning meet contrast.**
  Every finding carries a screen-reader-only severity word
  (`EmailEditor_Validation_Severity_{Error|Warning|Info}` — Error/Warning/Suggestion), a finding that
  points at a node is a real `<button class="tm-email-validation__link">` (the row keeps its pointer
  handler), and the Info/Warning icon and strip use the accessible `--tm-color-info-strong` /
  `--tm-color-warning-strong` tokens instead of the pale fill colours (~2.3:1 measured).

- **Import dialog: the preheader warning names the limit, not „{0}“.** Import warnings
  were rendered with the one-argument localizer indexer, so a rule that carries its threshold printed a
  literal placeholder. Both the validation panel and the import dialog now resolve such keys through
  `ValidationMessageText.Of` (internal), the dialog shows the warnings at all
  (`[data-tm-import-warnings]`), and a message with a detail formats that detail.

- **Variable picker: keyboard, diacritics, width and a non-button trigger.** The search
  folds diacritics through the shared `EmailSearchText.Fold` („krestni“ finds „Křestní jméno“, „uzivatel“
  finds the group), an empty result says `EmailEditor_Variables_NoMatch` with the searched term, the search
  takes focus when the picker opens, ArrowDown reaches the first row and ArrowUp/ArrowDown walk a roving
  tabindex (one Tab stop; `<TmSearchInput>` gained `FocusAsync` and an `OnKeyDown` callback, additively).
  Rows are a `1fr auto` grid (`code badge` / `desc desc`) with a wrapping token and the token colour from
  `--tm-color-text`, the panel is `min(26rem, 90vw)` wide, and the "Insert variable" affordance is a styled
  span inside the popover's own trigger — never a nested `<button>`.

- **Deleting a block announces itself and keeps focus.** The editor's live region says
  `EmailEditor_Announce_BlockDeleted` (with the undo chord) and focus moves to the neighbouring block, or
  to the canvas root when the column is now empty, instead of falling to `<body>` with the removed header.

- **Palette: the Spacer and Social tiles have icons, the search works read-only, and the
  title pairs with the arrow.** The registry named two icons the core set does not ship (`move-vertical`,
  `share-2`), so those tiles rendered an empty box; they now use `between-horizontal-start` and `share`.
  Searching is reading, not editing, so it stays enabled in `ReadOnly` (the tiles stay inert), the palette
  header carries its title plus the collapse arrow on one row (`ShowCollapseToggle=false` for a host that
  owns the shell chrome), and the editor no longer renders a second panel header above it.

- **A long preheader is measured in graphemes of the source template.** `EmailDocumentValidator` counted
  UTF-16 code units, so a preheader of 100 skin-toned emoji (200 code units) was flagged as too long.
  `StringInfo.LengthInTextElements` now counts what the inbox shows.

- **Every key the packages hand to the localizer now resolves (en / cs / sk / fr) — previously the bare
  key was rendered.** The editor showed the *key* of the diagnostics the import and validation work
  introduced: the import messages
  (`import.empty`, `import.parse_error`, `import.not_mjml`, `import.unknown_element`,
  `import.wrapper_flattened`, `import.element_wrapped`, `import.include_unresolved`,
  `import.input_too_large`), the document limits (`validation.limits.sections|blocks|nesting`) and the
  serializer failures (`serialization.malformed`, `serialization.schema_version.invalid`,
  `serialization.schema_version.unsupported`, `serialization.too_deeply_nested`,
  `serialization.input_too_large`) had no resource entry in any language. They do now
  (`TmEmailResources*.json`, en/cs/sk/fr), and `TmEmailResourcesParityTests` walks every public `*Keys`
  constant in the Abstractions assembly and fails if any of them resolves to nothing — so a new key cannot
  ship untranslated again.

- **JSON documentation: parameter and member descriptions shifted by one position are repaired across all
  packages, and `validate --fail-on-drift` now fails on a description borrowed from a sibling.** The
  committed overlays were produced by an older parse that paired the XML-doc summaries in **declaration**
  order against parameters emitted **alphabetically**, so an entry carried its neighbour's sentence — and
  where the old parse had dropped a `<see cref="…"/>`, a hole-punched fragment of it
  (`TmEmailImportDialog.OnClose` carried `OnImport`'s, `TmTreeListColumn.Editable` carried
  `CellTemplate`'s). `PackageDocumentationGenerator` detects both forms (an entry whose description is a
  sibling's freshly parsed description, or that description with characters deleted) and repairs it from
  the source; `validate --fail-on-drift` reports any that remain. **Description-only change** — no
  parameter, type or default moved — and the same regeneration catches up the missing
  `DataTableExportScope` overlay (public types had been added without one, which made
  `validate --fail-on-drift` fail).

- **Scriban code inside block content renders again (regression from 2.9.0).** `MjmlGenerator` sanitized
  the authored content of `mj-text`/`mj-accordion-text` *before* substitution, and the sanitizer's
  quote-safe formatter entity-encoded the code itself, so `{{ price | math.format "N2" }}`,
  `{{ text | string.truncate 5 "..." }}`, `{{ if price > 100 }}` and any `&&` produced
  `Invalid token found &` and an empty `RenderResult.Html`. Code spans (`{{ … }}`, and `{% … %}` when it
  carries no markup) are now masked, the rest of the content is sanitized, and the spans are restored
  verbatim (`HtmlContentSanitizer.SanitizeTemplateContent`); everything inside a span is still escaped by
  the render pipeline and the whole substituted MJML/HTML is still checked by `MarkupGuard`, so a value in
  an attribute cannot break out and markup cannot be smuggled through a span.

  ```text
  before  {{ price | math.format "N2" }}   → RenderResult.Html == "" (error: Invalid token found `&`)
  after   {{ price | math.format "N2" }}   → 1,234.50   (cs-CZ: 1 234,50)
  ```

- **A deeply nested document can no longer crash the process.** The MJML importer measures the element
  depth iteratively right after parsing (a 20 000× nested `mj-hero`/`mj-group` payload used to overflow the
  mapping recursion), threads the model depth through the mapping as a second guard, refuses
  self-/mutually-recursive `mj-include` references (a path stack plus an include depth limit of 8) and the
  JSON reader/writer bounds depth from the document's own nesting. `EmailDocumentLimitsValidator` and the
  schema-version code walk the block tree iteratively too, and `Deserialize(json, options)` also enforces
  `MaxInputBytes` and the section/block limits (throwing `EmailTemplateSerializationException` with the
  localization key, never a silent accept). The JSON depth bound is derived from the model nesting limit
  (`14 + 6 × MaxNestingDepth`, decision `EMAIL-IMPORT-JSON-DEPTH`); a plain `Deserialize(json)` without
  options keeps `max(64, …)`, i.e. it is never stricter than `System.Text.Json`'s own default.

- **Responsive editor chrome on the core `TmEditorShell` (D20).** `TmEmailTemplateEditor` renders
  through `TmEditorShell` (Header / Toolbar / Left + LeftRail / Canvas / Right / StatusBar /
  MobileActions): desktop ≥ 1200 px and tablet 768–1199 px keep the docked palette + inspector (the
  shell rails one of them on a tablet, `F6-TABLET-DOCKED-PANELS`), and below 768 px the canvas keeps the
  height while a non-modal bottom sheet carries Bloky | Vlastnosti (`MobilePanelPresentation.Sheet`,
  default) or a Bloky | Plátno | Vlastnosti tablist replaces it (`Tabs`). Mobile chrome follows D20: the
  **Save button and the host's `ToolbarStart` sit in the header**, `ToolbarEnd` and the
  Import/Export/Validate/Shortcuts long tail move into the header's „⋮“ overflow, and the action bar
  carries only Undo / Redo / Preview / Validate. Selecting a block switches the sheet to Vlastnosti;
  inserting one collapses the sheet and moves the focus (and the scroll) to the new block. The palette's
  collapsed state is an icon RAIL whose tiles are `--tm-touch-target`-sized, the failed/conflict alert is
  readable in the status bar (no more hovering a truncated pill), and the editor keeps the command row
  as a core `TmToolbar Overflow="Menu"` with a `Priority` per action. Save sits in the toolbar's
  `Actions` area, which never overflows (`flex-shrink: 0`), so the primary action can never be pushed
  into "More" — it needs no `Pinned` priority and keeps its `data-tm-save` hook.


- **Review fix — the mobile/tablet chrome is readable and operable at 390 px and 320 px.** Seven
  defects found in review, all measured in a browser:

  * The **save state is readable again**: below the desktop layout the pill kept only a 24 px severity
    glyph, so "unsaved changes" and "saved 2 min ago" looked identical. It now keeps its
    `EmailEditor_SaveStatus_*` label (ellipsised) and drops only the host's free-text reason, which the
    full-width failed/conflict alert already carries.
  * The **"⋮" trigger has an accessible name** (`EmailEditor_Mobile_MoreActions`, visually hidden inside
    the trigger): an icon-only popover trigger was announced as "button".
  * The **⋮ body carries no Preview** any more (it is one of the four action-bar commands, D20), and its
    groups are separated by a token border: Import/Export · Validate · Shortcuts · the host's
    `ToolbarEnd`.
  * **Save and the ⋮ are pinned to the right** of the compact header (`margin-inline-start: auto`), and
    without a host `ToolbarStart` the header now shows the **document's name** (one ellipsised line,
    `Document.Name`) instead of nothing.
  * **A tablet's railed inspector is no longer a dead end**: the shell's `RightRail` carries a
    finger-sized button (`IconNames.Sliders`, `aria-label` `EmailEditor_Panel_Properties`) that expands
    the properties panel. Because a tablet expands at most one panel, that is a USER collapse of the
    palette, so it does report `ToolboxCollapsedChanged` (U1).
  * **Touch targets**: Save and every ⋮ row are `--tm-touch-target` tall under a coarse pointer, and the
    rows are full-width, left-aligned menu items (`inline-size: 100%`, `justify-content: flex-start`).
  * **The palette's own title and the inspector's panel header are hidden on a phone**: the sheet's tab
    already names the panel, so the properties tab showed three titles for one surface. The docked
    layouts keep both.

- **Review fix — inserting on a phone always reveals the new content, and "add section" focuses it.**
  In the `Tabs` presentation there is no sheet to close, so "add block" used to leave the user on the
  palette tab while the block landed on a canvas they could not see; it now switches to the CANVAS tab
  (`ActiveMobilePanel = EditorShellPanel.None`). `TmEmailTemplateCanvas` keeps section element references
  too (`_sectionRefs`), so `FocusAsync` after "add section" lands on the NEW SECTION instead of falling
  back to the canvas root, and the editor's focus helper swallows `TaskCanceledException` (a cancelled
  render mid-focus must not tear down the insert that armed it).

- **Review fix — a one-way `ActiveMobilePanel` is adopted when the parameter changes.** The editor kept
  its own shadow (`_activeMobilePanel`) and only the host's FIRST value ever won: a host that drives the
  mobile tab without binding `ActiveMobilePanelChanged` could not move the sheet again. It now adopts the
  parameter whenever it differs from the last value seen (the shell's own `_activeParamSeen` pattern).

- **Email import / export / validation dialogs.** The import dialog no longer hard-codes a
  5 MB limit, no longer shows a raw `ex.Message` for broken JSON, and no longer shows the parse and
  warning lists only as a count; the export dialog no longer regenerates the whole MJML on every
  parent render, no longer offers a bare read-only `<textarea>` without copy or download, and no
  longer lacks a footer; the validation panel no longer renders findings as `<li @onclick>` (not
  keyboard operable) and no longer revalidates every template-bearing string on every parent render.
- **Server-side template JSON applies the import limits.**
  `EmailTemplateMapper.ToDocument(contentJson, EmailImportOptions?)` (plus the `ApplyCreate` /
  `ApplyUpdate` overloads) makes the server path enforce the same limits as the editor, and
  `Demo.Api`'s email-template endpoints answer `422` ProblemDetails whose `errors` values are
  LOCALIZATION KEYS (`SerializationKeys.InputTooLarge` / `Malformed` / `InvalidSchemaVersion` /
  `UnsupportedSchemaVersion`, `validation.limits.*`) instead of a `400` with an English sentence or an
  unhandled `500`. `ReportEmailTemplateGalleryStore` applies the same limits, so “what passes the
  import passes the server” holds for JSON sent by a client.

### Breaking / Migration

- **`TmScheduler` time grid scrolls as one element.** Before, the header and the all-day row sat above `.tm-scheduler-timegrid-scroll`, and only the body scrolled. Now the header and the all-day row sit in one sticky `.tm-scheduler-timegrid-head` (`top: 0`) inside that scroller (`data-tm-scroll="timegrid"`), with the body below it. A host that set `scrollTop` on the body must set it on `.tm-scheduler-timegrid-scroll`. The day track stays `height: 72rem`.
- **`TmScheduler` agenda rows are a date column plus a four-column event grid, and the month rows stretch.** Before, an agenda row was a flex line and a month row was only as tall as its events, so a short month left empty space under the grid. Now `.tm-scheduler-agenda-event` is `grid-template-columns: 7rem 1fr 2fr` inside a card on `--tm-bg-workspace` with `--tm-radius-card`, and `.tm-scheduler-month-row` is `flex: 1`, so the rows share the height of a bounded host. The group wrapper (`.tm-scheduler-agenda-group-body`) is `grid-template-columns: 8rem minmax(0, 1fr)`, and the event is `auto minmax(7rem, max-content) minmax(0, 1fr) minmax(0, 2fr)` (dot, time, title, description). The card (surface background, border, `--tm-radius-card`) is the `role="list"` element; the outer agenda keeps the workspace background. A host that sized an agenda row with flex children should switch to grid areas. The empty state stays `TmEmptyState` with the existing `TmScheduler_AgendaEmptyTitle` as its title. Timed events no longer set `--event-text-color`; that hook applies to a solid all-day event only. To paint a timed event solid again, drop `tm-scheduler-event-surface` in a host rule and set the background to `var(--event-color)`. Timeline events are two lines (time, then title) instead of one truncated line.
- **`TmScheduler` desktop look: events are tinted cards, headers are sentence case, and the toolbar uses icons.**
  Before, a timed event was a flat tint with a 3px stripe and no border, weekday headers were uppercased, the previous/next controls were the characters ‹ ›, Print was a text button, and the active view was a solid primary fill. Now every timed event (month, time grid, timeline) is a card: a 14% tint, a 1px tinted border, a `--tm-space-1` left stripe, `--tm-radius-sm` and `--tm-shadow-xs`. Weekday headers and the agenda weekday render in sentence case. Previous and Next are chevron icons and Print is a printer icon; both keep their accessible names and the `scheduler-prev` / `scheduler-next` / `scheduler-print` test ids. The view switcher is a segmented control on `--tm-bg-muted` whose active segment is a raised surface, not a primary fill. All-day events stay a solid fill (the contrast rule is unchanged). The classes `.tm-scheduler-month-*`, `.tm-scheduler-agenda-date-*` and the test ids are unchanged, so a selector on them still matches; what changes is the paint.
  ```css
  /* before: a flat chip, and a host colouring it had to fight a 3px stripe */
  .tm-scheduler-month-event { border-left: 3px solid var(--event-color); border-radius: 2px; }

  /* after: the card already carries the stripe, the radius and the shadow */
  .my-event { box-shadow: none; } /* drop the card shadow if a host skin needs the flat chip back */
  ```
  ```razor
  <!-- before -->
  <button data-testid="scheduler-prev" aria-label="Previous">‹</button>
  <button data-testid="scheduler-print">Print</button>

  <!-- after: the same test ids and the same accessible name, an icon instead of the character -->
  <button data-testid="scheduler-prev" aria-label="Previous"><TmIcon Name="chevron-left" /></button>
  <button data-testid="scheduler-print" aria-label="Print"><TmIcon Name="printer" /></button>
  ```
- **`TmScheduler` agenda item: `div.tm-scheduler-agenda-event` is now a `<button>`.**
  Before, an agenda row was a clickable `div`, so it had no keyboard activation and host CSS qualified with `div` matched it. Now it is a `<button type="button" class="tm-scheduler-agenda-event">` with an accessible name, so Enter and Space activate it once natively. A `div.tm-scheduler-agenda-event` selector matches nothing — drop the element qualifier and keep the class. The default button chrome (border, background, font) is reset, so the row looks as before. The Gantt calendar mode embeds the scheduler, so its agenda rows change the same way.
  ```razor
  <!-- before -->
  <div class="tm-scheduler-agenda-event">…</div>

  <!-- after -->
  <button type="button" class="tm-scheduler-agenda-event">…</button>
  ```
- **`TmSchedulerMonthView`: the month grid gained a row wrapper.**
  The day cells are now wrapped in `div.tm-scheduler-month-row` (one per week) with a `div.tm-scheduler-month-header-row` for the weekday headers, so a `:nth-child` selector that counted flat cells no longer lands on the same cell — select by `.tm-scheduler-month-day` or `[data-date]`. The timeline rows moved into `div.tm-scheduler-timeline-body` (`role="grid"`), so a selector that assumed the rows were direct children of `.tm-scheduler-timeline` needs the extra step. The print rule that kept a week together now targets `.tm-scheduler-month-row`.
- **`TmScheduler`: Timeline is a single day, Agenda navigates by `AgendaDays`, and "Today" follows the display timezone (B14/B24).**
  Before, the Timeline view drew one day but loaded and navigated a week (prev/next moved ±7 days, the title showed the week range), the Agenda view rendered 30 days but loaded and navigated a calendar month (prev/next moved ±1 month, the title showed the month name), and the Today button used `DateTime.Today` whenever the display zone happened to equal the machine zone. Now the Timeline range is `[day, day+1)`, prev/next move one day and the title is the culture's long date; the Agenda range is `[CurrentDate, CurrentDate + AgendaDays)` (the new `AgendaDays` parameter, default 30; a non-positive value is treated as 1), prev/next move by `AgendaDays` and the title shows the rendered range in the current culture ("15. května – 13. června 2025" in Czech); Today is today in the scheduler timezone taken from the injected `TimeProvider` (a `FakeTimeProvider` in tests). The month query now covers exactly the rendered six-week grid (June 2026 ends on 13 July, not a week past the month). Impact: a host whose `IScheduleDataProvider` assumed the Timeline week window gets a one-day query.
  ```razor
  <!-- before: Timeline loaded and navigated a week -->
  <TmScheduler View="TmScheduleViewType.Timeline" @bind-CurrentDate="_date" DataProvider="_provider" />

  <!-- after: keep the old week window by using the Week view -->
  <TmScheduler View="TmScheduleViewType.Week" @bind-CurrentDate="_date" DataProvider="_provider" />

  <!-- or widen the query yourself, ignoring the one-day range the scheduler asks for -->
  public Task<IReadOnlyList<TmScheduleEvent>> GetEventsAsync(TmScheduleQuery query, CancellationToken ct)
      => LoadAsync(query.RangeStart.AddDays(-3), query.RangeEnd.AddDays(3), ct);
  ```
- **`TmScheduler`: `FirstDayOfWeek` now drives the month grid, the week range and the week title together.**
  Before, `FirstDayOfWeek` only moved the week view's columns; the month grid, the loaded week range and the week title stayed Monday-first whatever the value. Now one value drives all three, and values outside 0–6 wrap (`8` is Monday, `-1` is Saturday). Impact: a host that passed `FirstDayOfWeek="0"` to shift only the week columns now also gets a Sunday-first month grid, a Sunday-start loaded range and a Sunday-start title. Pass `1` to keep the previous Monday behaviour.
  ```razor
  <!-- before: 0 moved the week columns only; the month grid stayed Monday-first -->
  <TmScheduler FirstDayOfWeek="0" View="TmScheduleViewType.Month" />

  <!-- after: pass 1 to keep the Monday grid, range and title -->
  <TmScheduler FirstDayOfWeek="1" View="TmScheduleViewType.Month" />
  ```
- **`TmScheduler`: "+N more" is a native `<button>` and opens the Day view unless `OnMoreClick` is handled.**
  Before, the overflow indicator was a non-interactive `div.tm-scheduler-month-more`, so a click fell through to the day cell and host CSS qualified with `div` matched it. Now it is a `<button type="button" class="tm-scheduler-month-more">` with its own accessible name ("{count} more events on {date} – open day view"), so a `div.tm-scheduler-month-more` selector matches nothing — drop the element qualifier and keep the class. It raises the new `OnMoreClick` with the day's date; without a delegate the scheduler opens that day in the Day view, raising `CurrentDateChanged` before `ViewChanged`. The fallback only takes effect when the host binds both `@bind-View` and `@bind-CurrentDate`; a host that sets `View` without `ViewChanged` stays on the month. The cap comes from the new `MaxVisibleEvents` parameter (default 3, previously hardcoded). Impact: a host that handled `OnSlotClick` and treated the overflow click as a day click no longer receives it.
  ```razor
  <!-- before: the overflow click arrived as OnSlotClick, and host CSS targeted the div -->
  <TmScheduler View="TmScheduleViewType.Month" OnSlotClick="OpenDay" />

  <!-- after: the Day fallback needs both bindings; or handle the overflow yourself -->
  <TmScheduler @bind-View="_view" @bind-CurrentDate="_date" MaxVisibleEvents="3" />
  <TmScheduler View="TmScheduleViewType.Month" OnMoreClick="ShowOverflow" />
  ```
- **`TmScheduler`: `EventTemplate` now renders in the Day, Week, Timeline and Agenda views too.**
  Before, the template replaced only the month cell's content; the other views kept their built-in time and title. Now the same template renders inside every view, and the positioning wrapper and resize handles stay. Impact: a template written for the wide month cell also renders inside the narrow time-grid bars and the timeline row, where a long layout is clipped. Branch on the bound view inside the template.
  ```razor
  <!-- before: one template, rendered only in the month view -->
  <TmScheduler EventTemplate="e => @<span>@e.Title</span>" />

  <!-- after: the template runs in every view, so keep the compact views compact -->
  <TmScheduler @bind-View="_view" EventTemplate="Event" />
  @code {
      RenderFragment<TmScheduleEvent> Event => e => @<span>@(_view == TmScheduleViewType.Month ? e.Title : e.Start.ToString("HH:mm"))</span>;
  }
  ```
- **`TmGantt` calendar mode inherits the scheduler range, title and overflow behaviour.**
  The Gantt calendar mode embeds `TmScheduler`, so it now loads exactly the rendered month grid, titles the range it renders, and its "+N more" opens that day. A host that read the Gantt calendar's query range or styled `div.tm-scheduler-month-more` needs the same migration as `TmScheduler`.
- **`TmSchedulerToolbar`: `RangeStart` and `RangeEnd` are additive, and the Agenda title is the rendered range.**
  The toolbar never had `AgendaDays` or `FirstDayOfWeek`; nothing is removed. `RangeStart` (inclusive) and `RangeEnd` (exclusive) are new, and `TmScheduler` fills them from the one range it loads, so the title and the query cannot drift. The Agenda title used to be the month name; it is now the range that is rendered ("15. května – 13. června 2025" in Czech). A host that renders the toolbar standalone and omits the range gets the scheduler's own default: a Monday-first week, or 30 agenda days. Pass the range to title anything else.
  ```razor
  <!-- before: the Agenda title was the month name, whatever the rendered range -->
  <TmSchedulerToolbar View="TmScheduleViewType.Agenda" CurrentDate="_date" />

  <!-- after: the title covers [RangeStart, RangeEnd - 1]; omit both for the Monday-week / 30-day default -->
  <TmSchedulerToolbar View="TmScheduleViewType.Agenda" CurrentDate="_date"
                      RangeStart="_date" RangeEnd="_date.AddDays(30)" />
  ```
- **Report Server: the email Engine, `ReportEmailDeliveryService` and `ReportScheduleWorker` moved from `AddCommonServices` to the server host
  (phase 7, decision D11 now holds for the Report Server).** `Tempo.ReportServer.Web.Client` (the WebAssembly leg of the InteractiveAuto
  portal, `IsPackable=false`) no longer references `Tempo.Blazor.EmailTemplates.Engine`, so Scriban, Mjml.Net and AngleSharp are no longer part
  of the browser download. `ReportEmailDeliveryService` and `ReportScheduleWorker` moved to `Tempo.ReportServer.Web` (same namespace
  `Tempo.ReportServer.Web.Services`); the new `IReportScheduleRunner` seam (model assembly) is what `SchedulesPage` uses for the demo "Run now"
  button, which is therefore **only offered where the runner can run** (decision REPORTSERVER-ENGINE-SERVER-ONLY): `IReportScheduleRunner` gained
  `CanRun`; `AddCommonServices` registers the null-object `UnavailableReportScheduleRunner` (`CanRun = false`), and `AddReportServerEmailDelivery()`
  replaces it with the real worker (`CanRun = true`) whatever the call order, so the WebAssembly leg shows no button instead of failing on click. `PackageBoundaryTests` lost its single documented exception: no WebAssembly closure may reach the Engine.
  Impact: a host that called `AddCommonServices` and relied on it registering the Engine / delivery / worker must register them itself; a
  test fixture that resolved `ReportScheduleWorker` needs `AddReportServerEmailDelivery()`.
  ```csharp
  // before - Program.cs of the server AND of the WebAssembly leg got the engine through AddCommonServices
  builder.Services.AddCommonServices(builder.Configuration);

  // after - AddCommonServices is client-safe; the server host adds the rendering half
  builder.Services.AddCommonServices(builder.Configuration);
  builder.Services.AddReportServerEmailDelivery();   // Tempo.ReportServer.Web only, never the WASM Program.cs
  ```
- **`TmWireframeEditor` / `TmWireframeDesignerCanvas`: the canvas now starts FITTED (`InitialZoom`, default `Fit`).** Before, the
  page was drawn by the svg's own `meet` scale, so a page larger than the canvas showed its edge-to-edge scale with no margin, a 1440 px page
  on a ~600 px canvas sat at ~30 % with the page touching the canvas border, and a page that did not fit left the zoom read-out reporting a
  false value. The view is now laid out by script on the first render, when the `Document` is replaced and when the active page (or its
  size) changes: `Fit` shows the whole page with a 40 px padding and **never enlarges past 100 %** (a page that fits stays at exactly 100 %
  and is centred); a zoom the user chose (buttons, pinch, Ctrl+wheel) is no longer overwritten by anything automatic, a container resize
  included, and the toolbar read-out always shows the real scale. Impact: screenshots / E2E that assumed the old edge-to-edge `viewBox`
  (`0 0 W H`) now see a padded view box; a host that wants the previous look sets `ActualSize` (100 %, the page starts at its top-left
  corner when it is larger than the canvas).
  ```razor
  <!-- before: the svg's own meet fit -->
  <TmWireframeEditor Document="@doc" />
  <!-- after: Fit is the default; ActualSize is the closest to the old behaviour -->
  <TmWireframeEditor Document="@doc" InitialZoom="WireframeInitialZoom.ActualSize" />
  ```
  The exported SVG / PNG / PDF is unaffected: `exportSvg` now writes the PAGE (`viewBox="0 0 W H"`, explicit `width` / `height`, no live
  sizing style) whatever the live view is, so a fitted, panned or zoomed canvas never leaks into a file.
- **`TmWireframeEditor`: stencils follow a theme (`StencilTheme`, `WireframeDocument.TargetTheme` is now honoured).** The built-in pack
  gains the themes `indigo` and `indigo-dark` (next to `dark`); `TmWireframeEditor`, `TmWireframeDesignerCanvas`, `TmWireframeToolbox` and
  `WireframePageSvgOptions` take `StencilTheme`. Resolution: the parameter, the active page `TargetTheme`, the document `TargetTheme`, the default
  tokens - **a document that already carries a `TargetTheme` (it was stored but never read) is now drawn with that theme.** An unknown name is the
  default tokens plus one log warning; `light` / `default` are the default tokens. Documents without `TargetTheme` render exactly as before.
  `Tempo.Blazor.Abstractions.Wireframe.Export.WireframeExportRequest` gains an optional `Theme`; the demo API logs it and its preview endpoints
  accept `?theme=`. Migration: nothing to do unless a stored document has a `TargetTheme` you do not want applied - clear it or pass
  `StencilTheme="light"`.
- **`TmEditorShell`: new `OverlayHeadingLevel` parameter (default `2`)** - the `aria-level` of the tablet overlay panel titles. The panel toggles of the
  wireframe editor and the editor-shell demo carry `aria-expanded` only (no `aria-pressed`); the open look is a class.
- **Wireframe stencil themes are bounded.** A theme name longer than 64 characters or outside `[A-Za-z0-9._-]` is the default tokens (no warning, nothing
  stored; `WireframeStencilThemeName.IsValid`), the once-per-name warning set is capped at 64 names and the demo API answers `400` for such a name
  (`?theme=`, export `Theme`). The export request now carries the resolved canonical theme (or `null`), not the raw parameter.
- **`TmEditorShell`: the mobile sheet's canvas reserve is capped at 60 % of the stage.** The padding the shell leaves under the canvas while the
  sheet is open (`.tm-editor-shell__stage--sheet-open .tm-editor-shell__canvas`) used to be able to equal the whole stage on a short (landscape
  phone) stage, leaving the canvas no height; it is now at most `--tm-editor-shell-sheet-reserve-max` (new token, default `0.6`) of the stage + the usual gap. The wireframe editor no longer
  carries its own copy of the formula (it only removes the padding where no sheet can be open). Hosts that overrode `--tm-editor-shell-sheet-reserve`
  above 0.6 get 60 % unless they raise `--tm-editor-shell-sheet-reserve-max`.
- **`TmWireframeEditor`: the tablet sheets are the core shell's overlay (`.tm-wd-sheet` is gone).** The left drawer and the
  inspector side sheet are now rendered by `TmEditorShell` with `TabletPanelPresentation.Overlay` instead of by the editor
  inside its canvas region. Impact: (1) the classes `.tm-wd-sheet`, `.tm-wd-sheet--left`, `.tm-wd-sheet--right`,
  `.tm-wd-sheet__header`, `.tm-wd-sheet__title`, `.tm-wd-sheet__close` and `.tm-wd-sheet__body` no longer exist - use
  `.tm-editor-shell__overlay`, `.tm-editor-shell__overlay--left|right`, `.tm-editor-shell__overlay-header`,
  `.tm-editor-shell__overlay-title`, `.tm-editor-shell__overlay-close` and `.tm-editor-shell__overlay-body`; (2) the sheet is now a
  child of the shell's `main` region (a sibling of the canvas) instead of the canvas region, so a descendant selector such as
  `.tm-wd-editor__canvas-region .tm-wd-sheet` must be rewritten; (3) the focus returns to the control that OPENED the sheet (the
  toolbar toggle, or the canvas when a selection opened the inspector) after Escape / ×, no longer always to the canvas wrapper;
  (4) the keyframes `tm-wd-sheet-in` are renamed `tm-wd-fade-in` (the mobile zoom overlay and quick actions still use them; nothing sheet-specific is left in the name). The `data-testid`s
  `wd-left-drawer`, `wd-inspector-sheet`, `wd-sheet-close`, `wd-toggle-left` and `wd-toggle-inspector`, the 44 px coarse-pointer
  targets, `--tm-wd-sheet-w` and the one-sheet-at-a-time behaviour are unchanged.
  ```css
  /* before */ .my-host .tm-wd-sheet--right { box-shadow: none; }
  /* after  */ .my-host .tm-wd-editor .tm-editor-shell__overlay--right { box-shadow: none; }
  ```
  ```csharp
  // before
  await editor.Locator(".tm-wd-sheet").WaitForAsync(new() { State = WaitForSelectorState.Detached });
  // after (or keep using the data-testids, which did not change)
  await editor.Locator(".tm-editor-shell__overlay").WaitForAsync(new() { State = WaitForSelectorState.Detached });
  ```
- **`TmWireframeEditor`: Import JSON is a `Secondary` file button and moves into "More" first.** The *Import JSON* control used to be
  a `Pinned` `TmToolbarItem` holding `<label class="tm-toolbar-btn tm-wd-editor__import">` + `<input class="tm-wd-editor__file-input">`;
  it is now a `TmToolbarFileButton Priority="Secondary"`. Impact: (1) on a toolbar without room the command leaves the bar
  BEFORE the arrange actions (it is the last Secondary item) and is then reachable only through the "More" menu;
  (2) `[data-testid='wd-import']` is now the visible `<button>` (it was the `<label>`) and the file input is
  `[data-testid='wd-import-input']` (it was a descendant of `wd-import`); (3) the classes `.tm-wd-editor__import` and
  `.tm-wd-editor__file-input` no longer exist. The `data-testid`, `data-tm-group="file"` and the Export button are unchanged.
  ```csharp
  // before: the input was inside the labelled test id
  await editor.Locator("[data-testid='wd-import'] input[type=file]").SetInputFilesAsync(path);

  // after: the input has its own test id (works whether the command is on the bar or in More - the input is always rendered)
  await editor.Locator("[data-testid='wd-import-input']").SetInputFilesAsync(path);
  // or drive the real UI: open More, click "Import JSON" and answer the file chooser
  ```
  ```css
  /* before */ .my-host .tm-wd-editor__import { min-width: 9rem; }
  /* after  */ .my-host .tm-wd-editor [data-testid="wd-import"] { min-width: 9rem; }
  ```
- **`TmDocumentOpenDialog` footer cards are a grid and the accent ink moved to `--tm-color-primary-text`.** DOM / CSS impact only:
  `.tm-dod-mode-body` is `display: contents` and `.tm-dod-mode-card` is a two-column grid (the `.tm-dod-*` class names, the radio
  group and the `data-testid`s are unchanged). A host stylesheet that targeted `.tm-dod-mode-body` as a flex column, or that read
  `color: var(--tm-color-primary)` from `.tm-dod-tree-node--active` / `.tm-dod-view--active`, should style the card itself and read
  `--tm-color-primary-text`:
  ```css
  /* before */ .my-host .tm-dod-mode-body { display: flex; flex-direction: column; }
  /* after  */ .my-host .tm-dod-mode-card { row-gap: var(--tm-space-1); }
  ```
- **No implicit Google Fonts import in rendered HTML.** `MjmlNetCompiler` turns off
  Mjml.Net's built-in font table, so a render no longer emits `<link>`/`@import` for Ubuntu (or any other
  font the document did not declare), and the markup guard drops every `@import` that is not the `href` of a
  document `mj-font`. Impact: a template that relied on the implicit Ubuntu webfont now renders with the
  next family of its `font-family` stack (Helvetica/Arial by default). Migration — declare the webfont
  explicitly:
  ```xml
  <!-- before: implicit, nothing in the template -->
  <!-- after -->
  <mj-head><mj-font name="Ubuntu" href="https://fonts.googleapis.com/css?family=Ubuntu" /></mj-head>
  ```
  (in the model: add an `EmailFont { Name = "Ubuntu", Href = "https://fonts.googleapis.com/css?family=Ubuntu" }`
  to `EmailTemplateDocument.Styles.Fonts`). Inline `style`/`<style>` content carrying `expression(`, `behavior:`,
  `-moz-binding` or a foreign `@import` is now dropped with a `RenderKeys.UnsafeElementRemoved` warning.
- **A raw block is an opt-in to raw MARKUP, not to markup that RUNS (B17).** The render
  pipeline's markup guard now ALWAYS removes `script`, `iframe`, `frame`, `frameset`, `object`, `embed`,
  `applet`, `base`, `form` and `portal` — a raw-text element (`script`) together with its content, up to
  its closing tag — with a `RenderKeys.UnsafeElementRemoved` warning and a `BlockedMember` audit event.
  Inbox previews and webmail open the message in a document that runs script, so “the author pasted it
  into a raw block” was never a reason to hand out a live `<script>`; there is no opt-in flag.

  | Před (before) | Po (after) |
  | --- | --- |
  | `<mj-raw><script>init()</script></mj-raw>` rendered a live script element | the element and its body are removed; the rest of the message renders, the render reports a warning and the audit sink sees `BlockedMember`/`script` |
  | an embedded document (`<iframe>`, `<object>`, `<embed>`, `<applet>`, `<portal>`) survived a render | it is removed, and `<base>`/`<form>` no longer rewrite the message's URLs or POST anywhere |

  An author who needs interactive content in an e-mail is out of scope for a message body: link to a
  hosted page instead.
- **An imported attribute outside its tag's allow-list is dropped at import (security 2),
  which makes a foreign template's round-trip lossy — with a warning.** This is the
  deliberate cost of replacing the deny filter with an allow-list (`MjmlKnownAttributes`).

  | Před (before) | Po (after) |
  | --- | --- |
  | `<mj-text some-future-attr="x">` kept the attribute in `ExtraAttributes` and re-emitted it | the attribute is dropped from the model and reported as `ImportKeys.DroppedAttribute` (`import.dropped_attribute`, detail `mj-text some-future-attr`); `data-*` and every known MJML attribute still survive |
  | `<mj-text onclick="alert(1)">` was re-emitted verbatim | dropped at import and never emitted (the generator refuses it too) |
- **The canvas renders the E-MAIL, not a structural list (B16, breaking DOM + behaviour).** The
  canvas used to draw every block as a card with a type label and up to 80 characters of stripped text
  (`.tm-email-canvas-block__kind` / `.tm-email-canvas-block__detail`), with a button that looked like a
  chip and an image that looked like a caption. It now paints the block's real approximation of the MJML
  output, inside a "paper" that is as wide as the document says.

  ```text
  before                                    after
  ┌────────────────────────────┐           ┌──────────────────────────────────────┐
  │ TEXT                       │           │  Hello world                         │
  │ Hello world                │           │  [ Get started ]   ← pill, its own   │
  │ BUTTON                     │           │                       colour/radius  │
  │ Get started                │           │  ─────────────────────────────       │
  │ IMAGE                      │           │  ▣ Company logo (img src/alt/width)  │
  │ Company logo               │           │  {{ user.first_name }} ← token chip  │
  └────────────────────────────┘           └──────────────────────────────────────┘
  ```

  What a host has to migrate:
  * **CSS: `__kind` / `__detail` are gone — target the block BODY.** The structural label spans no longer
    exist; the block's content is a `[data-tm-block-body]` element inside the `[data-tm-block-id]` wrapper,
    and the body declares what it painted in `[data-tm-block-kind]`.
    ```css
    /* before */ .tm-email-canvas-block__kind { … }  .tm-email-canvas-block__detail { … }
    /* after  */ [data-tm-block-body][data-tm-block-kind="text"] { … }
                 [data-tm-block-body][data-tm-block-kind="button"] .tm-email-block__button { … }
    ```
    The content itself is the `tm-email-block*` BEM family (styles live in the package's scoped
    `TmEmailCanvasBlock.razor.css`).
  * **Tests: replace the removed selector with `[data-tm-block-body]`.** A spec that asserted the old
    preview line (`.tm-email-canvas-block__detail`) now asserts the rendered body:
    ```csharp
    // before
    page.Locator(".tm-email-canvas-block__detail").First
    // after
    page.Locator("[data-tm-block-body]").First
    ```
  * **The block's `aria-label` now carries CONTENT, not just the type.** It is the localized block type
    plus a ≤60-character plain-text preview (`EmailEditor_Canvas_BlockLabel`, `{0}: {1}`; an empty block
    uses `EmailEditor_Canvas_BlockLabelEmpty`, `{0} (empty)`), e.g. `Text: Welcome, {{ first_name }}`. A
    selector that matched the exact type name (`aria-label='Table'`) must move to
    `[data-tm-block-kind='table']` or to a `starts-with` match. Tokens stay as written, tags are stripped.
  * **`aria-selected` → `aria-current="true"`, and the tab order is roving.** The items are
    `role="list"`/`listitem`, where `aria-selected` is not a supported state (a screen reader ignored it).
    Exactly ONE item carries `tabindex="0"` (the selected block, or the first block when nothing is
    selected); every other item is `tabindex="-1"` and Arrow keys move selection AND focus. A spec that
    asserted `[data-tm-block-id][aria-selected='true']` must use `[aria-current='true']`.
  * **New `[data-tm-block-empty]` marker and `--tm-paper-danger-ink`.** An empty text/button/navbar/
    social/table/carousel/accordion block no longer renders nothing (an invisible, unselectable hole): it
    paints a dashed "Empty block – add content in Properties" line. A refused link is marked with
    `data-tm-link-blocked` / `data-tm-cell-link-blocked` plus an alert icon and a wavy underline, and a
    refused hero/section background with `data-tm-image-blocked`. The paper-family token
    `--tm-paper-danger-ink` (`#991b1b`, theme-invariant like the other `--tm-paper*` values) is what the
    refused link paints — a host that redefines the paper palette should define it too.
  * The floating action bar keeps `[data-tm-block-action]` (`up` / `down` / `duplicate` / `delete`) and
    gained a decorative grip (`[data-tm-block-grip]`); the block wrapper itself stays `draggable`, so a
    drag started anywhere on the block still works (`EmailEditorDragDropE2ETests` unchanged). On a desktop
    the bar floats ABOVE the block instead of over its first line.
  * Text blocks NO LONGER show their source. An author who needs the HTML uses the inspector's rich-text
    editor; the canvas shows the result. `mj-raw` blocks show an escaped placeholder, never their markup
    (this was already true of the export and the preview, and can never change).
  * `Document.Styles.ContentWidth` decides the paper width, so a wide template (700 px) no longer renders
    at the old fixed 640 px; `Document.Styles.BackgroundColor`/`FontFamily` are painted on the paper.
  * **On a coarse pointer (a phone, a tablet) the block's action strip is IN FLOW and appears only on the
    SELECTED block** — never on hover, and never as an overlay. At the finger-sized 44 px targets the old
    overlay covered the middle of a small block, so the first tap of a selection landed on "move up"
    instead of selecting (measured: the editor's own F8 mobile specs could no longer open the inspector).
    A touch user taps to select, then acts; the strip pushes the block content down by one row. The drag
    grip is hidden there on purpose: HTML5 drag-and-drop does not run on a touch screen.

- **`TmEditorShell.ResolvedLayoutChanged` now fires before `CollapsedPanelsChanged` (event ORDER, not the
  signatures).** Both callbacks keep their types and both still fire in the same pass; only the order
  changed. A host that reacts to the collapse by adopting it into its own state and also reads the layout
  must rely on the new order.

  ```razor
  @* before — the collapse arrived first, so this handler adopted a layout-imposed rail as a preference *@
  <TmEditorShell CollapsedPanelsChanged="panels => toolboxCollapsed = panels.HasFlag(EditorShellPanel.Left)" />

  @* after — the layout is current when the collapse arrives; compare the layout generation *@
  <TmEditorShell ResolvedLayoutChanged="mode => layoutGeneration++"
                 CollapsedPanelsChanged="panels => AdoptIfUserCollapse(panels, layoutGeneration)" />
  ```

  A host that only forwards `CollapsedPanels` two-way (`@bind-CollapsedPanels`) needs no change. Migration:
  if your handler assumed the collapse arrived before the layout notification, move that work into the
  collapse handler and read the layout from the parameter it was last given (the shell has already
  resolved it).


- **`TmEditorShell`'s layout-imposed rail no longer raises `ToolboxCollapsedChanged` (e-mail editor,
  U1/D27).** A tablet layout expands at most one panel, so the shell rails the other one on its own. That
  is a LAYOUT STATE, not a collapsed preference: it is neither adopted into `ToolboxCollapsed` nor
  reported through `ToolboxCollapsedChanged` any more. Before, the editor adopted and reported it, so a
  host that bound the parameter two-way saw the palette pinned to its rail for ever (the Mobile→Tablet
  flip raised `Left` before the editor knew the layout had changed).

  ```razor
  @* before — a layout flip reported the rail as a preference *@
  <TmEmailTemplateEditor @bind-ToolboxCollapsed="collapsed" />   @* collapsed became true on a tablet *@

  @* after — only a USER collapse/expand reports; a tablet auto-rail is ignored *@
  <TmEmailTemplateEditor @bind-ToolboxCollapsed="collapsed" />   @* a tablet rail leaves collapsed alone *@
  ```

  Migration: nothing to change in the markup. A host that used the callback to mirror "is the palette
  visible right now" must read the shell's own layout (`data-layout` / the editor's
  `ResolvedLayoutChanged`) instead — on a tablet the palette may be railed while `ToolboxCollapsed` stays
  `false`. The reverse direction is unchanged: `ToolboxCollapsed="true"` still forces the rail everywhere.

- **`TmEmailTemplateEditor.OnSave` is a `Func<EmailSaveRequest, Task<EmailSaveResult>>?` and `OnAutoSave` is
  gone (B1).** The old `EventCallback<EmailTemplateDocument> OnSave` could not report an outcome, so the
  editor was free to look successful while the host had failed; auto-save had a second, separate callback
  with the same blind spot. There is now **one** handler that always answers, and the dirty state, the
  status badge and the `IsDirty` guard all derive from that answer.

  ```razor
  @* before — two callbacks, no outcome, "Saved" was the editor's own guess *@
  <TmEmailTemplateEditor @bind-Document="document"
                         OnSave="SaveAsync"            @* EventCallback<EmailTemplateDocument> *@
                         AutoSave="autoSave"
                         OnAutoSave="SaveAsync" />     @* EventCallback<EmailTemplateDocument> *@

  @code {
      async Task SaveAsync(EmailTemplateDocument document)
      {
          await Api.UpdateAsync(Id, ToRequest(document));   // a failure shows up nowhere
      }
  }
  ```

  ```razor
  @* after — one handler that returns what really happened *@
  <TmEmailTemplateEditor @ref="editor" @bind-Document="document"
                         OnSave="SaveAsync"
                         AutoSave="autoSave"
                         IsDirtyChanged="d => dirty = d" />

  @code {
      TmEmailTemplateEditor editor = default!;

      async Task<EmailSaveResult> SaveAsync(EmailSaveRequest request)
      {
          // request.Trigger tells Manual / Shortcut / AutoSave / Programmatic apart,
          // request.Revision is the editor revision the snapshot was taken at,
          // request.CancellationToken is cancelled when the editor is disposed.
          try
          {
              await Api.UpdateAsync(Id, ToRequest(request.Document), request.CancellationToken);
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

  Migration checklist: (1) change the handler's signature to
  `Task<EmailSaveResult> Handler(EmailSaveRequest request)` and read the document from
  `request.Document`; (2) delete the `OnAutoSave` handler — auto-save now calls the same `OnSave` with
  `Trigger = AutoSave`; (3) return `Saved()` / `Failed(message)` / `Conflict(message)` instead of silently
  completing or throwing; (4) if you drove the editor from outside, call `SaveAsync()` (it used to be a
  private method) and read `.IsSaved` for a leave-guard. **Extra breaking detail:** `IsDirty` is owned by
  the editor (`public bool IsDirty { get; private set; }`), so `@bind-IsDirty` does not exist — observe
  `IsDirtyChanged` or read the `@ref` instance (D2); `ReadOnly`, `ToolbarStart`, `ToolbarEnd`,
  `ShowSaveButton`, `ShowSaveStatus` and `ImageUpload` are additive. The toolbar's "MJML" button now reads
  **Export** (B10) while keeping `data-tm-mjml-btn`, and its status text moved from a plain span into the
  save badge (`data-tm-save-badge`, `data-tm-save-state`, `aria-live="polite"`).

- **The local render engine moved into the new package `Tempo.Blazor.EmailTemplates.Engine` (B7).**
  `AddTempoEmailTemplateEngine()` now lives in that assembly (namespace
  `Tempo.Blazor.EmailTemplates.Abstractions`, D21), `Tempo.Blazor.EmailTemplates.Abstractions` no longer
  depends on Scriban / Mjml.Net / AngleSharp, and the editor package references no engine. Public types
  that moved: `ScribanTemplateEngine`, `ObjectToScriptObjectConverter`, `MjmlNetCompiler`,
  `EmailTemplateRenderer`, `AngleSharpHtmlContentSanitizer` (formerly the static
  `HtmlContentSanitizer`), `ScribanTemplateVariableExtractor` (formerly `TemplateVariableExtractor`) and
  `ScribanTemplateValidator`. `HtmlContentSanitizer.Sanitize` (static) is now
  `IHtmlContentSanitizer.Sanitize` (instance, resolved from DI; `ConservativeHtmlContentSanitizer` is the
  baseline in Abstractions, `AngleSharpHtmlContentSanitizer` the engine implementation).

  ```text
  before  Tempo.Blazor.EmailTemplates.Abstractions → Scriban + Mjml.Net + AngleSharp (transitively into WASM)
  after   Tempo.Blazor.EmailTemplates.Abstractions → BCL + FluentValidation + Microsoft.Extensions.*
          Tempo.Blazor.EmailTemplates.Engine      → Scriban + Mjml.Net + AngleSharp
  ```

  ```csharp
  // before — one call, engine included
  builder.Services.AddTempoEmailTemplates();

  // after — the editor, plus the engine only where a local render is wanted
  builder.Services.AddTempoEmailTemplateEditor();
  builder.Services.AddTempoEmailTemplateEngine();          // PackageReference Tempo.Blazor.EmailTemplates.Engine
  ```

  `AddTempoEmailTemplates()` still compiles — it is `[Obsolete]` and delegates to
  `AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security))`, but it **does not register the
  engine any more**: without an `IEmailPreviewRenderer` the preview shows an `Unavailable` state that
  names the missing registration instead of an empty iframe. Migration for a host that rendered locally:
  add the `PackageReference` and call `AddTempoEmailTemplateEngine()`. Migration for a WebAssembly host:
  keep `AddTempoEmailTemplateEditor()` only and register a server-side `IEmailPreviewRenderer` (and,
  optionally, a server-side `ITemplateValidator`). The engine's dependency on Abstractions is pinned to
  the exact version (`[2.10.0]` at release) because it reaches internal members through
  `InternalsVisibleTo`.

- **`TmEmailTemplatePreview` now injects `IEmailPreviewRenderer` instead of `IEmailTemplateRenderer` (B2).**
  A host that decorated `IEmailTemplateRenderer` keeps that decoration for a *local* render
  (`AddTempoEmailTemplateEngine()` wraps it in `LocalEmailPreviewRenderer`), but the component itself only
  talks to the preview seam.

  ```csharp
  // before — the component resolved the render pipeline directly
  services.AddSingleton<IEmailTemplateRenderer>(sp => new MyRenderer(…));

  // after — register the preview seam (or the engine, which registers LocalEmailPreviewRenderer)
  services.AddSingleton<IEmailPreviewRenderer>(sp => new MyPreviewRenderer(…));
  ```

- **Sample-data editor and device/view switches changed DOM inside `TmEmailTemplatePreview`.** The device
  and view switches are `TmTabs Variant="Pill"` strips, so their buttons are `#tab-preview-device-desktop`,
  `#tab-preview-device-tablet`, `#tab-preview-device-mobile`, `#tab-preview-substitute-on|off` and
  `#tab-preview-view-html|text` (the old `data-tm-preview-device` / `data-tm-preview-view` attributes are
  gone) and the sample-data textarea is a `TmCodeEditor` behind the new `[data-tm-preview-edit-data]`
  button. `data-tm-preview-frame`, `data-tm-preview-text`, `data-tm-preview-errors`,
  `data-tm-preview-warnings` and the root `data-tm-preview` are unchanged; the unavailable state adds
  `[data-tm-preview-unavailable]` and `[data-tm-preview-retry]`.

- **Variable values are HTML-escaped in the body (B3).** The body used to substitute model values into
  the MJML *after* the content sanitizer had run, so `{{ name }}` carrying `<script>` (or any markup)
  reached the message verbatim. It is now encoded expression by expression.

  ```text
  before  {{ description }}                    data: <b>Hi</b>   →  <b>Hi</b>       (markup applied)
  after   {{ description }}                                       →  &lt;b&gt;Hi&lt;/b&gt;  (literal text)
          {{ description | html.trusted }}                        →  <b>Hi</b>       (sanitized markup)
  ```

  Migration: add `| html.trusted` **only** to values your application produces as trusted HTML (a rich
  text field). A plain text field must stay without it. Subject, preheader and the text version are
  unchanged. Policies that relied on the previous injection are the reason for the change (XSS).

- **URLs with a non-allow-listed scheme are replaced with `#` (B3).** `javascript:`, `data:`,
  `vbscript:`, `file:`, `blob:` and unknown schemes — and the same schemes smuggled with embedded tabs
  or newlines — no longer reach the message, from a template expression or from a block model value
  (`EmailButtonBlock.Href`, `EmailImageBlock.Src`/`Href`, `EmailNavbarLink.Href`,
  `EmailSocialElement.Href`, `EmailCarouselImage.Src`/`Href`, `EmailHeroBlock.BackgroundUrl`,
  `EmailWrapperBlock.BackgroundUrl`, `EmailSection.BackgroundUrl`). Each refused scheme adds one
  warning `RenderError` (`RenderKeys.BlockedUrlScheme`) and one `BlockedUrlScheme` audit event, so a
  template that used to render a `data:` image now renders `src="#"` and reports the warning
  (`RenderResult.Success` becomes `false` — a host that must not send a broken link can act on it). The
  allow-list cannot be extended by a host; `http`, `https`, `mailto`, `tel`, relative paths and
  `#fragment` are unaffected.

- **`ITemplateEngine.Render` and `IEmailTemplateRenderer.RenderAsync` have one signature with optional
  parameters (B4).** Source compatible, **binary incompatible** (a library compiled against the old
  signature must be recompiled); no default interface method is provided.

  ```csharp
  // before
  interface ITemplateEngine { Result<string> Render(string template, object? model); }
  interface IEmailTemplateRenderer
  {
      Task<RenderResult> RenderAsync(EmailTemplateDocument document, object? model = null,
          CancellationToken cancellationToken = default);
  }

  // after
  interface ITemplateEngine
  {
      Result<string> Render(string template, object? model, TemplateRenderOptions? options = null);
  }
  interface IEmailTemplateRenderer
  {
      Task<RenderResult> RenderAsync(EmailTemplateDocument document, object? model = null,
          TemplateRenderOptions? options = null, CancellationToken cancellationToken = default);
  }
  ```

  Migration: a caller that passed the cancellation token **positionally** must name it, because the
  third argument is now the options:

  ```csharp
  // before
  await renderer.RenderAsync(document, model, ct);
  // after
  await renderer.RenderAsync(document, model, cancellationToken: ct);
  ```

- **`RenderError` grew two optional parameters — breaking for positional deconstruction.** `Key` and
  `Severity` were appended to the positional record, so its deconstructor has five
  components instead of three and the type is **binary incompatible – recompile**. A host that only reads
  the properties is source compatible; a host that deconstructed the record is not.

  ```csharp
  // before
  var (message, line, column) = error;
  // after
  var message = error.Message;
  var line = error.Line;
  var column = error.Column;
  // and, when only a failure matters:
  if (error.Severity == RenderErrorSeverity.Error) { /* … */ }
  ```

  `RenderErrorDto` mirrors the new shape (`Key`, `Severity` appended, source compatible, **binary
  incompatible – recompile**).

- **`EmailTemplateRenderer` constructor gained optional parameters — binary incompatible.** The signature
  is now `(ITemplateEngine, MjmlGenerator, IMjmlCompiler, TextVersionGenerator,
  IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null, TimeProvider? timeProvider = null,
  ILogger<EmailTemplateRenderer>? logger = null)`; the four-argument call keeps compiling, a compiled
  assembly must be recompiled. A custom `ITemplateEngine` (anything other than the Tempo
  `ScribanTemplateEngine`) is accepted but logs one warning at construction: it cannot be asked for a raw
  pass, so the host is responsible for escaping (D24).

- **A protocol-relative URL is no longer a valid "relative" link.** `//cdn.example.com/x` (and the UNC
  forms `\\host\share`, `/\host`) used to pass the allow-list as a relative path; it now resolves a
  remote authority (SMB/NTLM leak in a mail client) and is replaced with `#`. Migration: write the scheme
  explicitly — `https://cdn.example.com/x`.

- **`ScribanTemplateEngine`: one public constructor.** The two public overloads
  (`(options)` and `(options, auditSinks)`) are merged into
  `ScribanTemplateEngine(TemplateSecurityOptions? options = null, IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null)`.
  Source-compatible (existing `new ScribanTemplateEngine()`, `(options)` and `(options, sinks)` calls
  keep compiling) but **binary-incompatible**: a library compiled against the removed one-parameter
  overload must be recompiled. The same constructor also takes `functionProviders` — no
  additional overload is added.

- **The render time budget is enforced even when a template writes no output.** The context now arms
  Scriban's `CancellationToken` with `TemplateSecurityOptions.Timeout` (and clamps `RegexTimeOut` to
  it), so a loop whose body only assigns values is aborted at the budget instead of running to
  completion. A cancelled render reports `Timeout` / `render time budget` — a template that used to
  grind for minutes now fails at `Timeout`.

- **A refused sandbox operation is now a render error, not an empty string.** Before, a template that
  reached for reflection (`{{ t.full_name }}`, `{{ user.get_type }}`), a system namespace
  (`{{ @System.Diagnostics.Process.Start('cmd') }}`, `{{ info.length }}` on a `System.IO` value), an
  environment/file root (`{{ env.PATH }}`, `{{ file.read 'x' }}`), an evaluating builtin
  (`{{ "1 + 2" | object.eval }}`, `object.eval_template`) or `include` rendered nothing for that
  expression and returned success. It now fails the render and reports an audit event, so a host can
  never mistake a blocked template for one that rendered no data.

  ```text
  before: Render("{{ user.get_type }}", model) → Success("")          // silent
  after:  Render("{{ user.get_type }}", model) → Failure("Template is not allowed to access member 'get_type'.")
  ```

- **The template sandbox member policy is fixed, not configurable.** There is no
  `AllowReflection` switch: members declared on `System.Type`, `MemberInfo`, `Assembly`, `Module` and
  on any type from `System.Reflection`, `System.IO`, `System.Diagnostics`, `System.Net`,
  `System.Runtime*` and `System.Threading` are refused, as are reflection member names (`GetType`,
  `GetMembers`, `Assembly`, `TypeHandle`, `UnderlyingSystemType`, `InvokeMember`, `DynamicInvoke`, …)
  on any type. A model that legitimately needs such a value must project it into a plain DTO or a
  dictionary before handing it to the renderer.

  ```text
  before: new { Kind = typeof(Order) } shipped to the template → template could read .full_name / .assembly
  after:  same model → Render fails with BlockedNamespace (System.Type); expose a string instead
  ```

- **A chained access through a null/undefined target renders empty instead of failing.** PostrionCore
  parity for legacy templates: `{{ user.name }}` with a model that has no `user` renders `""`
  (previously "Cannot get the member user.name for a null object."). Set
  `TemplateSecurityOptions.StrictVariables = true` to keep the strict behaviour for undefined
  *variables*.

- **A POCO model no longer exposes its public static methods to templates.** Model import now imports
  readable fields and properties only (and runs the same member policy), so a template cannot call a
  static helper on the model type. Migration: hand the value in explicitly if a template really needs
  it.

- **The serialized document now carries `schemaVersion` (B8 — output change, not an API break).** The
  JSON written by `EmailTemplateSerializer` / `EmailTemplateMapper.ToContentJson` has one extra
  property and the version is always the current one, whatever the instance says.

  ```text
  before: {"id":"…","name":"…","subject":"…","sections":[…]}
  after:  {"schemaVersion":1,"id":"…","name":"…","subject":"…","sections":[…]}
  ```

  Nothing needs to be migrated: a stored document without the property is read as version 1 and
  migrated on the next load, and unknown properties were already ignored. A payload from a **newer**
  schema is refused (`EmailTemplateSerializationException` with `SerializationKeys.UnsupportedSchemaVersion`)
  rather than parsed on a guess.

- **`EmailImportOptions.MaxInputBytes` stays at 5 MB on purpose (B14 — explicitly *not* a breaking
  change).** The import dialog's historical 5 MB limit is now the default of a configurable option, so
  existing imports keep working; raise or lower it per call. The limit is measured in **UTF-8 bytes**,
  so multi-byte content is judged by its transported size.

- **B15 — the API-extraction changes are ADDITIVE overloads, not a rewrite.** No migration is required:
  the 2.9.x signatures remain and keep their meaning, and the new ones only add an injectable seam.

  | 2.9.x (still compiles) | 2.10.0 (new overload) |
  | --- | --- |
  | `new MjmlGenerator()` | `new MjmlGenerator(IHtmlContentSanitizer sanitizer)` |
  | `EmailDocumentVariableExtractor.Extract(document)` | `EmailDocumentVariableExtractor.Extract(document, ITemplateVariableExtractor extractor)` |
  | `MjmlImporter.Import(mjml, includeResolver)` | `MjmlImporter.Import(mjml, includeResolver, EmailImportOptions options)` |
  | `EmailTemplateSerializer.Deserialize(json)` | `EmailTemplateSerializer.Deserialize(json, EmailImportOptions options)` |

- **The email packages' strings are embedded JSON and `AddTempoEmailTemplates()` no longer calls
  `AddLocalization()` (B6).** `TmEmailResources*.resx` and `EmailTemplateValidationResources*.resx` are
  gone; the editor and the request validators resolve their strings from flat `Resources/*.json` tables
  embedded in the package assembly (en / cs / **sk** / fr), registered as a CLOSED
  `IStringLocalizer<TmEmailResources>` / `IStringLocalizer<EmailTemplateValidationResources>` over
  `Tempo.Blazor.EmailTemplates.Abstractions.Localization.JsonStringLocalizer<T>` — the same scheme the
  Tempo core uses for `TmResources*.json`.

  ```text
  before: AddTempoEmailTemplates() → services.AddLocalization()
                                     IStringLocalizer<TmEmailResources> → host's IStringLocalizerFactory
                                     → resx ResourceManager
  after:  AddTempoEmailTemplates() → no IStringLocalizerFactory registration at all
                                     IStringLocalizer<TmEmailResources> → embedded JSON in the package
  ```

  ```csharp
  // before — the package registered the localization pipeline for the whole app
  builder.Services.AddTempoEmailTemplates();

  // after — a host that relied on that must register it itself (other components still need it);
  // the email editor works either way, because its strings no longer go through a factory
  builder.Services.AddLocalization();
  builder.Services.AddTempoEmailTemplates();
  ```

  Impact and migration:
  * A host that **relied on `AddTempoEmailTemplates()` to register the localization pipeline** must call
    `builder.Services.AddLocalization()` itself (or `AddJsonLocalization()` / its own factory). The editor
    no longer needs it, but other components still do.
  * A host that **replaces `IStringLocalizerFactory`** (PostrionCore's `AddJsonLocalization()` calls
    `RemoveAll<IStringLocalizerFactory>()`) used to see raw keys (`EmailEditor_Toolbar_Save`) in the
    editor and raw keys as validation messages; both now render real text in all four languages. No
    migration needed — this is the bug the change closes.
  * A host that **overrode the editor's strings** keeps its strings: a registration made *after* the call
    wins (last registration wins), and one made *before* it is kept because the package uses `TryAdd`.
    A fork that edited the `.resx` files must move its strings into
    `Resources/TmEmailResources.<culture>.json`.
  * Slovak (`sk`) now ships for both tables; `cs`/`sk`/`fr` are guaranteed to carry exactly the English
    key set with identical `{0}` placeholders (`TmEmailResourcesParityTests`).

- **Inspector field labels are localized keys, not `Humanize(property.Name)` (B11).** Every field of
  `TmEmailPropertyPanel` now resolves its label from the JSON tables: the owner-specific key
  `EmailEditor_Field_{Owner}_{Property}` first (e.g. `EmailEditor_Field_Button_Text` → „Text tlačítka“),
  then the shared `EmailEditor_Field_{Property}` for a field whose meaning does not depend on the owner
  (e.g. `EmailEditor_Field_FontSize`). A key that does not resolve renders as the key, never as an English
  name derived from the property, and `TmEmailFieldLocalizationParityTests` proves every field of every
  registered block — plus the document, section, column, document styles and the list-item types — has a
  key in all four tables.

  ```text
  before: BackgroundColor → "Background color"   (derived from the property name, English only)
  after:  BackgroundColor → "Barva pozadí" (cs) / "Farbe des Hintergrunds"… (the shipped table)
  ```

  Migration: a host (or a host test) that selected inspector fields by their **English visible label**
  must use the stable `[data-tm-prop="<Property>"]` attribute instead (unchanged) or the translated text.
  Nothing on the document model or the save contract changed.

- **The inspector is split into Obsah/Styl tabs and its field surface moved (B11).** The
  property panel now renders a `TmTabs` strip (`#tab-content` / `#tab-style`) whose Content tab holds
  texts, URLs, alt texts and the item editors, and whose Style tab holds colours, fonts, alignment,
  dimensions, the collapsible „Odsazení“ (spacing) and „Pokročilé“ (advanced) groups, `mj-class` and the
  extra attributes. Consequences:
  * `TmEmailObjectFields` gained `Placement` (Content/Style/Spacing/Advanced), `Variables` and `State`;
    `Primary` (ordering hint) and `Exclude` keep working, so a host that renders the field list directly
    only has to add `Placement` when it wants the tab split. The editor kind, the tab and the default order
    now come from the single `PropertyReflection` table instead of a per-call property list.
  * Fields that used to be found without interaction now live in the Style tab, so a selector such as
    `[data-tm-prop="BackgroundColor"] input` only matches after the tab switch. `data-tm-prop`,
    `data-tm-list-*`, `data-tm-kv-*`, `data-tm-head`, `data-tm-mjclasses`, `data-tm-extra`,
    `data-tm-rich-content`, `data-tm-table-*` and the panel root `data-tm-properties` are unchanged.
  * The panel's „Advanced“ `<details>` became two real disclosure buttons (`[data-tm-prop-group-toggle]`
    with `aria-expanded`, remembered per block type for the editor's lifetime), because a `<details>` cannot
    remember its state per selection.
  * `TmEmailPropertyPanel` gained `OnDuplicate` / `OnDelete` (rendered only when the host handles them; the
    editor wires them to one undo step each) and `TmEmailTemplateEditor` gained `ToolboxCollapsed` /
    `ToolboxCollapsedChanged` (D10) — the editor maps the same parameter onto
    `TmEditorShell.CollapsedPanels`.

- **Typed inspector editors: a stored value the editor refuses is now visible (B11).** A colour
  is edited with `TmColorPicker` (swatch + hex), alignment with a `[data-tm-align-option]` toggle group
  (`aria-pressed`), enum-like MJML attributes (`Mode`, `BorderStyle`, `IconPosition`, `Direction`,
  `VerticalAlign`, `Target`) with a `TmSelect`, and a length as number + unit. A stored value that is not
  a valid colour/length (garbage from an import) is shown in a plain text field with a localized
  `[data-tm-field-error]` and is **not** written back until it validates, so the document never receives a
  half-typed value. A host that relied on the old free-text field for these properties now gets the
  validated editor — the stored string format (`13px`, `#1a1a1a`, `left`) is unchanged.

- **The blocks palette gained search, wrapping tiles and a collapse toggle.** The toolbox
  renders a search field (`[data-tm-toolbox-search]`, placeholder „Hledat bloky…“) that filters tiles by
  their **localized** name with diacritics and case folded, a localized empty state
  (`[data-tm-toolbox-empty]`), Escape clears the filter, tiles show the icon above a wrapping label whose
  full name is in `title`, and `Collapsed` / `CollapsedChanged` fold the palette away. `data-tm-block`,
  `data-tm-preset` and `data-tm-toolbox-list` are preserved; the component root is
  `[data-tm-toolbox-root]` (the editor's panel keeps `data-tm-toolbox`).

- **Email import / export / validation dialogs: the DOM and two parameters changed (B12/B13; the public
  API stays additive).** Migration for a host test that selected the old markup:

  | Před (before) | Po (after) |
  | --- | --- |
  | `<textarea data-tm-import-input>` | `[data-tm-import-input]` root of a `TmCodeEditor`; the editable element is `#tm-import-input` (the textarea) |
  | `<textarea data-tm-export-mjml>` / `data-tm-export-json` | `[data-tm-export-mjml]` / `[data-tm-export-json]` code editor roots; the text is the textarea's `value` (read it via `#tm-export-mjml-code`) |
  | `<button data-tm-export-mode="json">` / `data-tm-import-mode` | core `TmTabs` strip: `#tab-export-json`, `#tab-export-mjml`, `#tab-export-html`, `#tab-import-json`, `#tab-import-mjml` |
  | validation finding: `<li @onclick>` with a nested button | the finding itself is a `<button data-tm-validation-message>` (or a `<div>` when there is nowhere to jump to), with `[data-tm-validation-title]`, `[data-tm-validation-description]` and `[data-tm-validation-action]` |
  | `[data-tm-import-warnings]` list | `[data-tm-import-warnings]` is the disclosure list; the count moved to `[data-tm-import-warning-count]` on the state card |

  New additive parameters: `TmEmailTemplateEditor.ImportOptions`, `TmEmailImportDialog.ImportOptions`,
  `TmEmailValidationPanel.ImportOptions` and `TmEmailExportDialog.LayoutMode` /
  `TmEmailImportDialog.LayoutMode` (the editor passes the layout it already resolved, so a phone gets the
  core sheet from F2). The export dialog also needs the core `tmDataTable.downloadFile` helper on the host
  page (`<script src="_content/Tempo.Blazor/js/data-table.js">`), exactly like the core data components.

- **`LayoutSeverity.Info` is a new severity and the preheader has the first recommending rule (B5, D9).**
  `Warning = 0` and `Error = 1` keep their numbers; `Info = 2` is appended, so a `switch` over the enum
  without a `default` arm is the only source-breaking case. Display order comes from
  `LayoutSeverityRank.Of` (Error → Warning → Info), never from the numeric value. One rule uses it: a
  preheader longer than `EmailDocumentValidator.MaxPreheaderLength` (100) characters is reported as
  `validation.preheader.too_long` with `Info` severity, and the validation dialog now renders an icon per
  finding (`[data-tm-validation-icon]`) so severity is not colour-only.

  | Před (before) | Po (after) |
  | --- | --- |
  | `switch (severity) { case LayoutSeverity.Warning: … case LayoutSeverity.Error: … }` — exhaustive, compiled | add a `default:` arm (or a `case LayoutSeverity.Info:`), otherwise the switch is a compile error |
  | `severity > LayoutSeverity.Warning` was the "is an error" test | unchanged — `Info = 2` is ABOVE `Error`, so order by `LayoutSeverityRank.Of(severity)`, never by the numeric value |

- **`TemplateVariableInfo.Description` / `Group` are part of the record's equality (B9).** Host-supplied
  metadata for the variable picker (the picker searches both, and shows the description next to the
  token); variables extracted from a document keep them `null`. A host that compared `TemplateVariableInfo`
  instances or used them as dictionary keys sees a different `Equals`/`GetHashCode` than before — the
  compiler-visible shape is unchanged.

  | Před (before) | Po (after) |
  | --- | --- |
  | `new TemplateVariableInfo("order.total", VariableKind.Text)` — two members, equality over both | the positional members are unchanged; `Description` and `Group` default to `null` and now take part in `Equals`/`GetHashCode` |
  | two instances differing only in host metadata compared equal | they compare unequal; a dictionary/lookup keyed by `TemplateVariableInfo` must include the metadata (or key by `Path`) |

- **`TmEmailTemplateEditor` renders the core `TmEditorShell` instead of its own `TmSplitter` 20/60/20.**
  The editor used to lay itself out: a `TmSplitter` with a 20 % palette, a 60 % canvas and a 20 %
  inspector, plus its own `@container` width steps. None of that could work on a phone (three panes at
  390 px — the F7 screenshot 40b), and a second layout implementation would drift from the core shell
  every other editor now uses. The editor now owns **no** layout: the root element is a thin wrapper
  (`[data-tm-email-editor]`, the keyboard scope) around `TmEditorShell`, which resolves desktop / tablet /
  mobile from the editor's **own container** (`Breakpoints` default `new TmLayoutBreakpoints(768, 1200)`)
  and renders docked panels (desktop and tablet, at most one expanded), a bottom sheet with a
  Bloky | Vlastnosti strip (mobile default) or a Bloky | Plátno | Vlastnosti tablist.

  ```text
  before (DOM)                                  after (DOM)
  [data-tm-email-editor]                        [data-tm-email-editor]
    .tm-email-editor__toolbar                     .tm-editor-shell[data-layout=…]
    .tm-splitter                                  .tm-editor-shell__header   ← mobile only
      .tm-email-editor__body                        .tm-editor-shell__toolbar  ← desktop + tablet
        section[data-tm-toolbox]   (20 %)           .tm-editor-shell__main
        section[data-tm-canvas]    (60 %)             aside[data-region=left]    → [data-tm-toolbox]
        section[data-tm-properties](20 %)             div[data-region=canvas]    → [data-tm-canvas]
                                                      aside[data-region=right]   → [data-tm-properties]
                                                    .tm-editor-shell__status   → [data-tm-save-badge]
                                                    .tm-mobile-action-bar      ← mobile only
  ```

  Migration — the stable selectors are unchanged, the wrapper classes and the layout are not:

  | F7 selector | F8 replacement |
  |---|---|
  | `.tm-splitter` | *(gone — the shell is not a splitter)* |
  | `.tm-email-editor__body` | `.tm-editor-shell__main` |
  | `.tm-email-editor__toolbar` | still on the command row (now the core `TmToolbar` root) |
  | `[data-tm-toolbox]`, `[data-tm-canvas]`, `[data-tm-properties]` | unchanged (inside `data-region=left/canvas/right`) |
  | `[data-tm-save]` | unchanged; on a **mobile** layout it lives in `[data-region=header]` |
  | `[data-tm-save-badge]` | unchanged; **moved** from the command row to `[data-region=status-bar]` |
  | `[data-tm-save-alert]`, `[data-tm-save-retry]` | unchanged; also in `[data-region=status-bar]` |
  | `[data-tm-toolbar-start]`, `[data-tm-toolbar-end]` | unchanged; on mobile `ToolbarEnd` renders inside the header's `[data-tm-mobile-overflow-panel]` |
  | `[data-tm-group]` (`history`, `review`, `file`, `help`) | unchanged (now on core `TmToolbarGroup`s; the groups' separator spans `.tm-email-editor__toolbar-sep` are gone) |
  | `.tm-email-editor__btn-label` | `.tm-toolbar-btn-text` (the core toolbar hides labels below its own label breakpoint) |
  | `[data-tm-toolbox-collapse]` | *(gone — the shell's `button.tm-editor-shell__panel-toggle--left` is the one control; `ShowCollapseToggle="false"`)* |
  | `[data-tm-toolbox-list]` when `ToolboxCollapsed` | *(gone — a collapsed panel is the shell's `[data-region=rail-left]` rail, not a hidden list)* |

  A host stylesheet that keyed on the editor's own width had to change too: the email stylesheet no
  longer contains any `@container` or `@media (…-width)` rule. Layout-dependent rules key on the shell's
  attribute instead — `.tm-editor-shell[data-layout="mobile"] [data-tm-save-badge]`, and the values are
  `mobile` | `tablet` | `desktop`:

  ```css
  /* before */
  @container tm-email-editor (max-width: 900px) { .tm-email-editor__save-label { display: none; } }

  /* after */
  .tm-editor-shell[data-layout="mobile"] .tm-email-editor__save-label,
  .tm-editor-shell[data-layout="tablet"] .tm-email-editor__save-label { display: none; }
  ```

- **New editor parameters `LayoutMode` / `InitialMode` / `ResolvedLayoutChanged` / `Breakpoints` /
  `MobilePanelPresentation` / `ActiveMobilePanel(Changed)` (all additive, §3.3).** They are forwarded to
  the shell unchanged — the editor keeps no breakpoint logic of its own.

  ```razor
  @* before — no layout API at all (the editor was always 20/60/20) *@
  <TmEmailTemplateEditor Document="document" OnSave="SaveAsync" />

  @* after — Auto measures the editor's container; force a mode in a demo or a test *@
  <TmEmailTemplateEditor Document="document" OnSave="SaveAsync"
                         LayoutMode="TmLayoutMode.Auto"
                         MobilePanelPresentation="MobilePanelPresentation.Sheet"
                         ResolvedLayoutChanged="mode => layout = mode" />
  ```

  `ToolboxCollapsed` / `ToolboxCollapsedChanged` stay the public API for the palette (D10) and are now
  the mapping onto `TmEditorShell.CollapsedPanels` (`EditorShellPanel.Left`); a collapse the shell
  imposes itself (the tablet one-expanded-panel rule) raises `ToolboxCollapsedChanged` too.

- **`TmEmailTableEditor` markup and stylesheet.** The grid is no longer a bare `<table>`:
  it is a `<table>` with a `<caption>`, a sticky first `<th scope="row">` control column, one
  "Edit cell" action per `<td>`, a header section and a rows-and-columns section, all inside
  `.tm-email-table-editor__scroll`. The package now ships `.razor.css` scoped styles for it, so the
  grid no longer inherits a host's `table` rules.

  ```css
  /* before — a host could style the bare table of the inspector */
  .tm-email-properties table td input { … }

  /* after — target the grid's own BEM classes (the data attributes are unchanged) */
  .tm-email-table-editor__cell, .tm-email-table-editor__rowctl { … }
  ```

  Migration: a host that styled the old bare table should target `.tm-email-table-editor__*`, and a
  host that counted `td` children to locate a cell should use the stable
  `[data-tm-table-cell]` / `[data-tm-table-cell-edit]` / `[data-tm-table-row]` attributes. The item
  editing default stays `EmailItemEditingMode.Inline`, so nothing else about the inspector changes.



## 2.9.1 (not released separately — shipped in 2.10.0)

### Fixed

- **Wireframe redesign phase 10 clean-up (plan ce19aa8c).** The Blob URL of a wireframe download is revoked after 60 s instead of 1 s (iOS Safari opens the blob in a new tab asynchronously); `TmWireframeDesignerCanvas`' parent-render snapshot now includes the page width / height (a `ResizeCanvasCommand` mutates them in place) and documents that every render-affecting parameter belongs in it; deleting a selected connector closes the sheet that its selection opened on a phone (`MobileSelectionCleared`); `AddElementsCommand.Undo` removes only the elements its own `Execute` inserted; duplicating a grouped selection re-points the copied members to the copied container; the mobile Lock quick action drops `aria-pressed` (its label already flips Lock / Unlock); the phone sheet-open canvas padding lives inside the `< 768px` container query and the floating zoom pill never exceeds its canvas (`max-inline-size`, `nowrap`).
- **Wireframe middle-button pan survived Escape and destroy.** Escape or tearing the editor down in the middle of a middle-button pan left the viewBox moved, the pointer captured and the move/up listeners attached; `_cancelMiddlePan` now rolls it back (the one gesture outside `pointer-drag.js`, documented in `docs/touch-interaction.md`).
- **A cancelled wireframe drag called .NET after the editor was destroyed.** `_bodyCancel` skips (and swallows a rejected) `OnDragStarted([])` once the instance is destroyed.
- **Keys typed in a wireframe side sheet reached the canvas.** The canvas shortcut guard ignores events from `.tm-wd-sheet` like it does for the layers and pages panels (arrow nudge, Delete and Escape no longer act on the selection).
- **Test guard:** every `invokeMethodAsync` name in `wireframe-designer.js` must exist as a `[JSInvokable]` on `TmWireframeDesignerCanvas` (`WireframeJsInvokableContractTests`).

- **Wireframe toolbox insertion could land outside the page.** The insert-at-centre origin was clamped only to
  non-negative coordinates, so after panning away from the page a click inserted an element far outside it. The
  origin is now clamped into the page (`0 ≤ x ≤ pageWidth − w`, `0 ≤ y ≤ pageHeight − h`) and snapped to the grid only
  while snapping is on (`GridSize > 0`).

- **Wireframe SVG/PNG/PDF export carried the editing chrome of a selected connector.** `exportSvg` stripped the
  selection, rubber band and handles but not the connector waypoint circles (`.tm-wd-connector__waypoint`), the
  alignment guides or the connector preview, so exporting while a connector was selected put blue handles into the
  file. They are removed now, and every token-painted canvas node is baked to explicit colours (`data-wd-paint`).

- **Space on a focused `TmDocumentOpenDialog` row or grid card scrolled the list.** The rows and cards are
  `div[tabindex=0]` whose Space activation runs on keyup; the browser's default scroll on Space keydown is now
  cancelled by a scoped listener (`TmDocumentOpenDialog.razor.js`) that fires only when the keydown target IS a row or
  a card - never for an input/button inside the dialog and never for Tab (no blanket `preventDefault`,
  `docs/keyboard-activation-convention.md`).
- **Wireframe "Export PDF" opened the dialog with PNG selected.** `TmWireframeEditor` set its internal
  `_exportDialogFormat` but never handed it to `TmWireframeExportDialog`. The dialog has a new
  `InitialFormat` parameter (`"png"` default, `"pdf"`) that the editor now passes, so the *Export PDF* menu
  item preselects PDF. Guarded by `TmWireframeEditorBehaviorTests` and the `ExportPdf_PreselectsPdf` E2E.

- **`TmWireframeExportDialog` kept the previous file name after re-opening.** Only format, scale and
  background were reset on close; the file name was filled once and then stuck. Every closed→open
  transition now restores file name (from `DefaultFileName`), format (from `InitialFormat`), scale and
  background.

- **Wireframe toolbox items ignored a click or tap, and Enter/Space dropped every element at (100, 100).**
  The item had no `@onclick`, so the only non-drag way in was the keyboard, and it always inserted at a fixed
  point. Click, tap and Enter/Space (once per gesture - Enter on keydown without auto-repeat, Space on keyup,
  per `docs/keyboard-activation-convention.md`) now raise `OnComponentActivated`, and `TmWireframeEditor`
  inserts the element centred in the visible canvas area (snapped to the grid, undoable, selected). This is the
  non-drag alternative to HTML5 drag-and-drop (WCAG 2.5.7).

- **`TmWireframeLayersPanel` announced its default active layer fire-and-forget from `OnParametersSet`.**
  The report is now awaited from `OnAfterRenderAsync` (once per default layer), so a failing parent handler
  surfaces instead of being swallowed and the parent no longer re-renders in the middle of a parameter pass.
  All `[Parameter]`s of the panel carry XML docs.

- **Wireframe keyboard focus was invisible on the layers panel and the page tabs.** The focus-visible block
  now also covers `.tm-wd-layers__*` controls and `.tm-wd-editor__page-tab*` (same focus colour as the rest of
  the editor).

- **`TmDocumentOpenDialog` accessibility (additive, no class or layout change).** Grid cards are now focusable
  (`tabindex="0"`, `role="option"` inside a `role="listbox"`, `aria-selected`): Enter opens, Space selects; list
  rows use the same Enter/Space contract (Space on keyup). Sortable column headers expose
  `aria-sort` (`ascending` / `descending` / `none`) through a `columnheader` wrapper (`display: contents`, so the
  grid layout is unchanged), and folder tree items carry `aria-level` and `aria-selected`.
- **French filter result preview claimed whole millions wrong.** The `TmFilterBuilder_ResultPreview_Many`
  template was "`{0:N0}` de millions d'enregistrements correspondants", but the `_Many` form is picked for
  whole millions and the number is formatted in full, so a count of 2 000 000 read "2 000 000 de millions
  d'enregistrements correspondants". The template is now "`{0:N0}` d'enregistrements correspondants", like
  every other French `_Many` family. Guarded by a bUnit test.

- **`TmDataTable` client-side result preview counted an edited filter twice.** The table copies the applied
  builder filters into its own column-filter map; the in-memory count then applied the map *and* the builder's
  filters, so editing an applied filter (Status=A becomes Status=B) counted "A AND B" — usually 0. The count now
  merges both filter sources with the same rule the data-provider path already used: the builder's filter wins
  on its column. Guarded by bUnit (`FilterResultPreviewEditTests`).

- **The filter result preview kept a stale count when the search, column filters or the data changed.**
  `TmFilterBuilder` only recounted when `ActiveFilters`, `ResultCountProvider` or `ShowResultPreview` changed,
  but `TmDataTable` and `TmMultiViewList` count with inputs the builder cannot see. Both hosts now pass a new
  `ResultCountKey` token (below) so the preview recounts with the table.

### Added

- **TmPdfViewer: counter-clockwise rotation (FZE-11).** `RotateCounterClockwiseAsync()` rotates
  0° → 270° → 180° → 90° — the exact inverse of `RotateAsync()`, which keeps its clockwise
  behavior. `AllowRotation` now renders both directions in the `data-tm-group="rotate"` group
  (`rotate-ccw` and `rotate-cw` icons, localized tooltips `TmPdfViewer_RotateCounterClockwise` /
  `TmPdfViewer_RotateClockwise`); on a mobile layout both are "More"-menu overflow entries.
  Continuous mode rebuilds the placeholders and preserves the scroll position through the same
  path as the clockwise rotation, and the text layer follows PDF.js' `data-main-rotation`.

- **TmPdfViewer: the document header's default subtitle can show the file size (FZE-11).** With
  `ShowHeader` (and no explicit `Subtitle`), the subtitle becomes "Pages: N · 1.4 MB" — the byte
  size arrives from PDF.js `getDownloadInfo` through the new `OnDocumentLengthKnown` interop
  callback and is formatted by a package-internal `PdfFileSizeFormatter` (current culture digits,
  localized `TmPdfViewer_FileSize_B/_KB/_MB/_GB` units). `ShowFileSize` (default `true`) turns the
  append-off, and a host-supplied `FileSize` (nullable `long`) wins over the JS-reported length;
  when the size is unknown (a stream without a length) the subtitle keeps its pages-only form.

- **TmPdfViewer: floating search card (FZE-11).** The new `SearchPresentation` parameter
  (`PdfSearchPresentation`) defaults to `Floating` on desktop and tablet: a `role="search"` card
  anchored top-right over the page body — outside the scroll area, never covering the toolbar,
  painted above the docked panels. The card carries the input, the "N of M" counter, prev/next,
  clear and close; Enter walks forward, Shift+Enter backward, and Escape (or ✕) closes the card
  and returns focus to the toolbar's search toggle. The card STARTS CLOSED — `ShowSearch="true"`
  alone does not open it at load (the `Inline` bar and a mobile layout keep opening at load).
  The `pdf-search-input/-count/-prev/-next/
  -clear/-close` test ids are unchanged and `pdf-search-card` is new. `Inline` renders the
  pre-2.9.1 bar unchanged, and a mobile layout keeps the sheet-style bar in both presentations
  (see Breaking / Migration for the default change).

- **PdfViewer & Annotator redesign — plan summary (phases 1–10).** `Tempo.Blazor.PdfViewer`
  (`TmPdfViewer`, `TmPdfAnnotator`, the two thread panels and the annotation layers) was rebuilt
  against the reference mockups: wave-0 bug/a11y fixes independent of the redesign (phase 1 —
  JS document state keyed per root element instead of per canvas, visible loading/error states,
  HiDPI with a DPR cap of 2, per-instance listeners, thumbnail reuse of the open document, aria
  wiring), tokenization from the removed undefined aliases to the canonical core tokens so the
  palette, the indigo theme and dark mode propagate (phase 2), the grouped toolbar with an
  editable page input, zoom presets with Fit width/Fit page, the optional document header and
  collapsible thumbnails (phase 3), the shared thread cards for both panels with comment editing
  and a panel close (phase 4, PD3/PD4), the annotator on ONE toolbar with pass-through viewer
  parameters (phase 5), the full continuous mode — lazy render queue, canvas release outside the
  viewport band, scroll-tracked page, per-page text layer and search (phase 6; PD1 keeps
  annotations and `OverlayContent` single-page only), the labelled and responsive viewer for
  tablet and mobile on container queries + `TmLayoutMode` + the core sheets (phase 7), the
  annotator's tablet/mobile G2 presentation (phase 8) and touch — pinch zoom, touch text
  selection, pen priority, relative time and plural keys (phase 9) on the shared core gesture
  helpers. The full contract — fit modes, header/slots, toolbar `data-tm-group` vocabulary,
  responsive behavior, touch and the migration notes — is documented in
  [docs/pdf-viewer.md](docs/pdf-viewer.md); every breaking DOM/CSS change is listed under
  Breaking / Migration below.

- **TmPdfAnnotator: the annotator and its panel on tablet and mobile (FZE-8).** The annotator now
  resolves its layout from its own container through the embedded `TmEditorShell` (new
  `LayoutMode` parameter, `TmLayoutMode`, default `Auto` — a forced value pins the layout so
  tests and hosts can render a mode without a DOM) and adapts like the tablet/mobile references:
  **desktop** keeps the merged toolbar and the docked thread panel; **tablet** renders the tool
  labels under the icons (new `ToolbarLabels` parameter, `ToolbarLabels`, default `Icons` — the
  demo pages use `Auto`; the value is passed through to the embedded viewer's toolbar), meets the
  44px touch target on a coarse pointer for every annotator control (including the export/print
  buttons and the toolbar pills), keeps the thumbnails collapsible (they start collapsed, as the
  viewer does) and hosts the thread panel as the shell's docked side region; **mobile** is the
  G2 presentation — a horizontally scrollable tool-palette tile row with exactly *Browse* and
  *Add comment* (highlight, stamp and draw are hidden and an active edit mode falls back to
  *Browse*, with a localized hint that the editing tools need a larger screen), the F5
  `TmMobileActionBar` ("Export PDF | Export flattened | Print" with the threads entry in the
  shared "More" menu) and the thread panel as a non-modal bottom sheet (peek/half/full) opened
  from a thread marker, from "Comments" or from a comment draft, with the draft composer and the
  reply/resolve buttons sticky at the sheet bottom in one shared row (phase 8 review 12). Paging
  and zoom on a phone come
  from the compact viewer toolbar (F7). `TmPdfAnnotatorPanel` gains `ShowChrome` (default `true`);
  the sheet presentation passes `false` so the sheet header owns the title and the close button.
  New localization keys: `TmPdfAnnotator_OpenThreads`, `TmPdfAnnotator_EditOnLargerScreen`
  (en/cs/sk/fr + mock).

  Phase 8 review round: the mobile sheet starts CLOSED at load (and on every switch from
  desktop/tablet to mobile, unless the host explicitly supplies `ShowPanel`); after a sheet
  close focus returns to the element that opened it (marker / Comments tile / draft) through
  `tmPdfAnnotator.rememberFocus`/`focusOpener`, falling back to the first action-bar tile;
  a sheet opened from a marker or a draft lands on the HALF snap (0.7) with the composer
  pinned to the sheet bottom (review 12: the reply input and its Resolve secondary must sit
  fully inside the sheet's visible band — the peek's band was too short and pushed the
  reply/resolve row below the fold), the selected card `scrollIntoView({block:'end'})`-ed (the
  sticky
  reply composer rides at the card's bottom, so the bottom alignment lands it on the sheet's
  visible band) and the draft textarea / reply input focused; the sheet header is not sticky
  (the drawer body owns the scrolling) and the composer actions share one row instead of the
  stacked full-width column; the shell collapse chevron is
  wired two-way
  (`CollapsedPanels` — a collapse of the right panel closes it, the reopen toggle restores);
  `TmPdfViewer` gains `ShowMobileActionBar` (default `true`) plus public
  `OpenThumbnailsSheetAsync`/`OpenSearchAsync`, and the annotator folds the viewer's Pages and
  Search entries into its own bar (exactly one bar on a phone); the annotator tools render as
  their own full-width labelled row above the canvas on tablet (never inside the narrow viewer
  toolbar overflow, the color chip never wraps as a stray dot; review 12: the labelled pill
  track wraps within the row and the color label stays visible in it at narrow panes);
  `ToolbarLabels` follows the
  viewer's rule (labels under the icons on tablet, inline on desktop); the thumbnails reuse the
  viewer's guarded tablet default (collapsed unless the host binds them); the panel header ✕,
  the "Show resolved" target, the card toggles and the palette tiles meet the 44px target on a
  coarse pointer (both palette tiles stay fully visible at 320px); and editing a comment focuses
  `pdf-annotator-edit-input`, returning focus to the comment's ⋯ trigger on save/cancel.

- **TmPdfViewer: full continuous mode — lazy rendering, scroll tracking and per-page text/search.**
  `renderAllPages` now builds one placeholder wrap per page (correct aspect ratio, resolved from
  the real page boxes), paints pages lazily through an IntersectionObserver and a serial render
  queue (`pdf-viewer-continuous.mjs`, node-tested) so a 100+ page document never blocks the UI,
  and releases canvases scrolled far outside the viewport (limit 12 painted pages). Scrolling
  reports the visible page through the new `[JSInvokable] OnVisiblePageChanged` — updating the
  two-way `Page` binding, the toolbar input and the active thumbnail — while prev/next, the page
  input, thumbnails and search matches call `scrollToPage` instead of rendering into an invisible
  single-page canvas. The text layer and search highlights render per page inside each page wrap,
  the active match scrolls its page into view, and zoom/rotation/view-mode switches preserve the
  scroll position through a page-relative anchor. The demo loads a local 120-page fixture
  (`_content/Tempo.Blazor.Demo.SharedUI/sample-data/tempo-pdf-viewer-120p.pdf`, shipped by the
  shared demo UI package) instead of an external URL.
  New localization keys: `TmPdfViewer_AnnotationsSinglePageOnly`, `TmPdfViewer_PageOfTotal`
  (en/cs/sk/fr + mock).

- **TmPdfViewer: continuous mode review pass — switches, release band, scale updates and A11y.**
  A Continuous → SinglePage switch (toolbar toggle or thread select) now renders and re-arms the
  selection/overlay setup only after the fresh single-page DOM exists (`_singlePagePending`) —
  previously the JS ran against the torn-down continuous DOM. Canvas release honors the same
  margin-extended band as the IntersectionObserver (`±50 %` viewport) and a scroll tick re-queues
  unpainted visible pages; leaving the band drops a page from the render queue. Released pages
  also clear their text/search layers, and the active search match scrolls with `nearest` (and
  not at all when already visible) so the page indicator no longer regresses to N−1. A scale-only
  change in Continuous mode updates the placeholders in place (placeholders resized, painted
  canvases dropped and re-queued, scroll anchor re-applied, pageSizes cached per document +
  rotation) instead of rebuilding the DOM; the fit recompute is debounced ~100 ms in Continuous.
  A11y: each continuous page wrap is a `role="group"` labelled from the localized
  `TmPdfViewer_PageOfTotal` template with `aria-current="page"` tracking the visible page, the
  canvas is `role="img"` (or `aria-hidden` under a text layer), and the wrap itself is a
  keyboard-reachable `role="region"`. The demo pages use a tinted workspace
  (`--tm-bg-muted`) with `--tm-bg-surface` page surfaces and a reduced-motion-aware skeleton
  shimmer for unpainted pages.

- **TmPdfViewer: annotations and `OverlayContent` are single-page only (PD1).** In continuous mode
  the annotation overlay and `OverlayContent` are not rendered; the thread panel stays readable
  and shows a localized hint (`TmPdfViewer_AnnotationsSinglePageOnly`), and selecting a thread
  switches the viewer back to `SinglePage` on the thread's page (`ViewModeChanged` +
  navigation). `TmRedactionLayer` keeps its viewer pinned to SinglePage (default `ViewMode`) and
  is unaffected. `TmPdfAnnotator` already pins SinglePage.

- **TmPdfViewer: `PdfZoomMode` (`Custom` | `FitWidth` | `FitPage`) with `ZoomMode`/`ZoomModeChanged`
  (two-way, default `Custom`).** A fit mode lets JavaScript compute the scale from the container
  size (`tmPdfViewer.fitScale`) and a ResizeObserver keeps recomputing it on resize — fit-width
  keeps working after a panel or window resize. A fit scale MAY fall outside the zoom steps
  (50–200 %) — the exact container ratio beats the steps — it is clamped to the absolute 0.1–5
  bounds in `pdf-viewer.js` and re-guarded in `OnFitScaleComputed`. The zoom ± buttons and every
  preset switch the mode back to `Custom`; the `Auto` value arrives with a later phase.

- **TmPdfViewer: zoom presets dropdown.** The zoom label is now a `TmDropdown` with the presets
  50/75/100/125/150/200 % plus "Fit width" and "Fit page"; the trigger shows the active fit mode
  or the current percentage and carries `TmPdfViewer_ZoomPresets` as its accessible name.

- **TmPdfViewer: document header (`ShowHeader`, `Title`, `Subtitle`, slots `HeaderStart` and
  `HeaderActions`, default off).** With the header on, the title renders above a subtitle that
  defaults to a neutral page count ("Pages: N") once the document is loaded, and the Open and
  Download links move from the toolbar into the header (host actions slot in after them).

- **TmPdfViewer: `ShowOpenLink` (default `true`).** Hides the "Open in new tab" link without
  touching `AllowDownload`.

- **TmPdfViewer: collapsible thumbnails.** The sidebar has a "Thumbnails" header with a collapse
  button; `ThumbnailsCollapsed`/`ThumbnailsCollapsedChanged` is two-way, the toolbar gains a
  "Pages" toggle, and a collapsed rail keeps one expand affordance. The toolbar also carries
  `ToolbarStart`/`ToolbarEnd` slots so hosts (the annotator, phase 5) can compose their own
  groups, and the comments toggle shows the open-thread count ("Comments (N)").

- **TmDropdown: `AriaLabel` parameter.** Names the trigger button for assistive tech when the
  visible label is only the current value (a zoom level); when null the visible text stays the
  accessible name.

- **TmPdfViewer: `PdfZoomMode` schema props in the wireframe stencil.** The `TmPdfViewer`
  stencil schema gains `showThumbnails`, `viewMode` (`single` | `continuous`), `showHeader`,
  `title` and `zoomMode` (`custom` | `fit-width` | `fit-page`); the `pdf-viewer` role is
  unchanged.

- **TmPdfViewer/TmPdfAnnotator: shared thread cards for both annotation panels.** The
  `TmPdfAnnotationThreadPanel` and `TmPdfAnnotatorPanel` now compose three new components —
  `TmPdfThreadCard` (card header with colored badge, plain-case kind label, preview,
  "author · time", "Str. N" page reference, neutral reply count, success-subtle "Resolved" chip
  and chevron over the expandable detail), `TmPdfCommentItem` (TmAvatar with initials or the
  author's image, body with an "(Edited)" marker, and a "⋯" `TmDropdown` menu with Edit and
  Delete in place of the inline link) and `TmPdfComposer` (the textarea + 32px-minimum actions
  row; the reply composer is sticky at the bottom of the expanded thread and supports
  Ctrl+Enter). Both panels keep their public parameters and `data-testid` contract; the cards
  are white surfaces on the muted panel background with `--tm-radius-card`.

- **TmPdfViewer/TmPdfAnnotator: comment editing and panel close.** Both panels gain
  `OnEditRequested` (`EventCallback<DocumentCommentEditRequest>`); the viewer and the annotator
  persist the edit through `IPdfAnnotationProvider.EditAsync` and refresh the threads. The
  panels gain `OnCloseRequested` — a ✕ in the panel header; the viewer then hides the
  annotations panel and the annotator raises `ShowPanelChanged` (two-way `ShowPanel`). The
  panel width keeps flowing through the `--tm-pdf-panel-width` token (280px viewer /
  300px annotator).

- **TmPdfViewer/TmPdfAnnotator: the thread cards moved into one shared `TmPdfThreadList`.**
  Both panels now delegate their card loop to a public `TmPdfThreadList` (XML-documented, JSON
  overlay + bundle) that owns the `TmPdfThreadCard` rendering — `@key="thread.Id"` per card,
  `@key="comment.Id"` per comment — plus the quoted text and the sticky reply composer. The
  panels keep only their header (title, resolved filter, ✕) and their own new-thread/draft
  prompt; they pass the localized reply labels and decide the kind-caption policy through
  `HideCommentKind` (the viewer passes `true`, so plain comment threads render no kind label;
  the annotator keeps captions for every kind). The shared text helpers (`Truncate`,
  `Preview`, `ThreadNumber`) and the single `FormatTime` live in one internal helper used by
  the card and the comment item alike, and the card/comment test ids keep flowing from
  `TestIdBase` (the panels now pass `TestId("pdf-annotation")` / `TestId("pdf-annotator")`,
  so a host `TestIdPrefix` is respected).

- **TmPdfAnnotator: the ✕ keeps working without a `ShowPanel` binding, and a toolbar toggle
  reopens the panel.** The annotator now tracks the panel visibility internally (synced from
  `ShowPanel` only when the parameter value changes), so an unbound host can still close the
  panel via the ✕; `pdf-annotator-panel-toggle` in the toolbar flips it back and mirrors the
  state through `aria-pressed` (localized label `TmPdfAnnotator_PanelToggle`).

- **TmDropdown: `ShowChevron` (default `true`).** Compact icon-only triggers can drop the
  trailing chevron; the PDF comment "⋯" menu uses it to stay a compact square
  (`min-width`/`min-height: var(--tm-space-8)`) that never stretches the comment card.

- **TmPdfViewer/TmPdfAnnotator: comment head and card chrome polish.** The comment head splits
  into two rows — author + ⋯ menu on row 1 (the author shrinks with `flex: 0 1 auto` but never
  below `min-width: 4ch`), time + "(Edited)" on row 2; a changed `Comment.Id` closes a stale
  inline edit. A selected thread card is marked by `border-color: var(--tm-color-primary)` and
  a `var(--tm-color-primary-subtle)` header background — the focus ring stays reserved for
  `:focus-visible`; the reply count moved top-right of the card header (the meta row keeps
  "author · time · page ref", so the page ref and the reply count are no longer glued
  together); and the composer textarea carries its localized placeholder as `aria-label`.

- **TmPdfAnnotator: pass-through parameters for the viewer chrome.** The annotator gains
  `ShowThumbnails`, `ShowSearch`, `AllowRotation`, `ThumbnailsCollapsed`/`ThumbnailsCollapsedChanged`
  (two-way, unbound-host safe), `ZoomMode`/`ZoomModeChanged` (two-way) and `ShowOpenLink`
  (default `true`) — all forwarded to the embedded viewer — plus an optional header row via
  `ShowHeader`, `Title`, `Subtitle` and the `HeaderStart`/`HeaderActions` slots (the export
  group renders as secondary header buttons; host actions slot in after it). The annotator
  always pins the viewer to single-page mode (PD1) and never offers the continuous toggle.
  New localization keys: `TmPdfAnnotator_ColorLabel`, `TmPdfAnnotator_TogglePanel`,
  `TmPdfAnnotator_ExportGroup`, `TmPdfAnnotator_ToolsGroup` (en/cs/sk/fr + mock). The wireframe
  `TmPdfAnnotator` stencil schema gains `showThumbnails`, `showSearch`, `showHeader` and `title`.

- **TmPdfViewer: responsive viewer for tablet and mobile (FZE-7).** The viewer now resolves its
  layout from its own container (new `LayoutMode` parameter, `TmLayoutMode`, default `Auto` —
  a forced value pins the layout so tests and hosts can render a mode without a DOM) and adapts
  like the tablet/mobile references: **desktop** keeps the docked chrome; **tablet** (640-1023)
  starts with the thumbnails collapsed (the "Pages" toggle reopens them), renders the comments
  panel as the one `TmSidePanel` side sheet (closed by default, restoring focus to the toolbar
  toggle) and drops the toolbar labels below the icons; **mobile** (<640) gets a compact
  single-row toolbar + "More" (Rotate, Open, Download and the continuous toggle live only
  there, as `OverflowOnly` priorities), an in-flow `TmMobileActionBar`
  ("Pages | Hledat | Komentare (N)"), the thumbnails and comments as bottom sheets, the zoom
  presets as a sheet (`MobilePresentation=Auto` of the core dropdown) and a full-width search
  row. The default comments-panel visibility follows the layout (desktop open, tablet and
  mobile closed). New `ToolbarLabels` parameter (`Auto` | `Icons` | `IconsWithText`, default
  `Icons` = the previous icon-only toolbar; the demo pages use `Auto`); on a tablet layout the
  labels render below the icons. `.tm-pdf-viewer` is now a named query container
  (`container-type: inline-size`) and its presentation (page count, dividers, wrapping, search
  row) adapts to the viewer's own width; every viewer-owned control meets the
  `--tm-touch-target` (44 px) minimum on a `pointer: coarse` device. `Height="100%"` and
  `100dvh` now resolve through the flex chain (`min-height: 0`) of the wrapping layout host.
  New localization keys: `TmPdfViewer_OpenPages`, `TmPdfViewer_OpenComments` (en/cs/sk/fr +
  mock); the "More" trigger keeps the core `Tm_More`. `TmPdfAnnotationThreadPanel` gains
  `ShowChrome` (default `true`); the sheet presentations pass `false` so the sheet header owns
  the title and the close button. The wireframe `TmPdfViewer` stencil gains the `auto` zoom
  mode (now the default).

- **TmPdfViewer review fixes (FZE-7 follow-up).**

  - **Panel defaults now apply under `LayoutMode=Auto`.** A measured layout flip re-applies
    the panel defaults — thumbnails re-collapse on tablet, the comments panel closes off
    desktop — until the user has touched the panels themselves (an internal flag, reset per
    document load); a user's own choice is never overwritten, and reopening the thumbnails
    resets the JS thumbnail render cache. Covered by bUnit tests that drive the wrapping
    observer's `OnLayoutModeChanged("tablet")`.
  - **A forced cascaded ancestor now decides the layout.** `TmPdfViewer` takes a
    `[CascadingParameter] TmLayoutContext? Parent` and resolves its layout with the shared
    `TmLayout.Resolve(LayoutMode, Parent, measured, Desktop)` (the same pattern as
    `TmEditorShell`): an explicit `LayoutMode` wins, then a forced ancestor, then the viewer's
    own measurement. A viewer nested in a forced-`Mobile` observer renders the mobile chrome.
  - **The comments toggle id is a per-instance GUID** (it was a shared constant), used for the
    side sheet's `RestoreFocusTargetId` — two viewers on one page no longer share a DOM id.
  - **`downloadUrl` is hardened.** The decision logic moved into a pure
    `pdf-viewer-download.mjs` (node-tested via `npm run test:pdf-viewer`): the URL is resolved
    against `document.baseURI`, only `http:`/`https:`/`blob:` are allowed, same-origin and
    `blob:` URLs take the anchor + `download` path, and any other origin opens a new tab
    (`noopener,noreferrer`) instead of downloading — a cross-origin `download` attribute is
    ignored by browsers and must never turn into a navigation.
  - **Stable `data-tm-group` groups.** The "Pages" toggle (`panels`), the view-mode segment
    (`view`) and the trailing file links + host `ToolbarEnd` (`export`) are `TmToolbarGroup`s
    now; the full frozen group vocabulary is documented in [docs/toolbar.md](docs/toolbar.md)
    (the PDF viewer adds `navigation`, `rotate`, `comments` to the DiagramEditor set).
  - **TmPdfAnnotator.** `ZoomMode` defaults to `Auto` (consistent with the viewer); the
    `ToolbarStart` tools and the panel toggle are core `TmToolbarButton`s with priorities
    (Primary mode pills, Pinned panel toggle), so they overflow into the merged toolbar's
    "More" menu. All test ids survive. The annotator groups carry `data-tm-group`
    (`tools`, `export`).
  - **Mobile presentation.** The compact row keeps the prev chevron + page input + next chevron
    on one line (`flex-wrap: nowrap`), shows the compact `1/120` count, shortens the zoom
    trigger to the bare `%` and fits a 320 px viewport in a single row; the mobile search bar
    no longer wraps (input `flex: 1; min-width: 0`) and always offers a localized ✕ close; the
    mobile action bar renders a tile only for an enabled feature (Pages/Search/Comments) and
    disappears entirely when none is enabled. Under `pointer: coarse` the page input, the
    search input, the file links, the composer actions, the comment ⋯ menu and the panel close
    meet the `--tm-touch-target` minimum; the sheet-presented comments panel is `width: 100%`
    with no left border and its title shows the live comment count. The redaction stage claims
    the full width of the column below 900 px (`align-items: stretch` + `width: 100%`).
  - **Evidence.** The demo page gains a forced-tablet section (`LayoutMode="TmLayoutMode.Tablet"`)
    and the E2E lane screenshots the tablet toolbar, the open comments side sheet and the mobile
    "More" menu (`pdf-viewer/mobile-more-menu.png`); the 44 px touch-target test also covers
    `input`, `[role=button]` and `a`.

  New localization keys: `TmPdfViewer_CloseSearch` (en/cs/sk/fr + mock).

- **TmPdfViewer + TmPdfAnnotator: touch interaction (FZE-9).** The viewer now supports touch
  end to end through the shared core gesture helpers (never a package-local copy):
  - **Pinch-zoom & two-finger pan.** `pdf-viewer.js` attaches the core sibling
    `pointer-gesture.js` (`attachPinch`, imported relatively across the split-package static
    asset boundary) to the active scroll wrap. The live gesture is a CSS transform around the
    fingers' start centre; on release the transform is cleared, the two-finger pan becomes
    scroll and the committed absolute scale (clamped to 0.1–5.0) arrives at the new
    `[JSInvokable] OnPinchZoomEnded`, which hands the zoom mode to the user
    (`ZoomMode=Custom`, detaching any active fit observer) and re-renders once. The zoom
    dropdown and the ± buttons remain the WCAG 2.5.7 non-drag alternative, and ctrl+wheel
    (trackpad pinch) zooms around the cursor through the same commit path. The wraps claim
    `touch-action: pan-x pan-y` so one finger still scrolls while the JS pinch owns the zoom.
  - **Touch text selection.** The selection reports on `selectionchange` + `pointerup`
    (previously `mouseup`, which never fires for a touch drag) and the text layer paints its
    text with `color: transparent` instead of an element opacity, so the native selection
    highlight and its touch handles render at full strength (the highlight color is the
    scoped `::selection` rule). New sticky CTA on tablet and mobile:
    a pending selection pins an in-flow bar ("Comment on selected text" + a dismiss) that
    opens the comments sheet, where the docked desktop panel's prompt would be unreachable —
    `TmPdfViewer_DismissSelection` is new (en/cs/sk/fr + mock).
  - **The shared pinch helper grew one opt-out.** `attachPinch` gains
    `captureSinglePointer` (default `true`, the previous behaviour): a host whose gesture
    surface is a DESCENDANT listener inside the pinch element (the annotator's ink surface)
    passes `false`, so a lone finger keeps reaching the surface's own pointer events and the
    capture happens once the pinch actually starts (a document-level capture listener prunes a
    finger that ended elsewhere). See docs/touch-interaction.md.
  - **Drawing vs. gestures.** In the annotator's Draw mode one finger or the pen draws and
    two fingers hand over to the pinch (the in-progress stroke is cancelled on the second
    finger — the same event the pinch's `onStart` fires on — and only the stroke's own
    pointer may extend or commit it); a `pen` landing replaces a live finger stroke (the pen
    has priority). **Review fix:** `touch-action: none` moved from the active annotator
    surface to the Draw modifier only — an active Comment/Stamp surface used to block
    one-finger scrolling and the pinch on a phone. The tablet-only sticky "Highlight" CTA
    (`TmPdfAnnotator_HighlightSelection`, new) surfaces a touch selection's highlight draft
    when the panel is closed or out of reach.

- **TmPdfViewer + TmPdfAnnotator: relative time and natural plurals (FZE-9).** The annotation
  cards' timestamps are now relative ("5 min ago", "yesterday at 16:20"…) through the shared
  `ITmRelativeTimeFormatter` (`TmRelativeTime.Resolve`) — the absolute time rides in the
  `<time datetime>` machine-readable instant and the `title` tooltip. The counted texts became
  `Loc.Plural` families: `TmPdfThread_ReplyCount` ("2 replies" / cs "2 odpovědi" / …),
  `TmPdfViewer_CommentsCount` (the toolbar toggle's live count) and
  `TmPdfAnnotator_DraftStrokes` (en/cs/sk/fr + mock, with the forms each language's grammar
  needs per docs/localization-plurals.md).

### Breaking / Migration

- **TmPdfAnnotator: the toolbar gains a `history` group BEFORE the tools (FZE-15).** The merged
  annotator toolbar now renders the undo/redo pair as a `data-tm-group="history"` group ahead of
  the `tools` pills, so the tab order and every positional CSS/E2E selector that assumed the
  tools were the group's first children shift by one group. Key hosts off `data-tm-group`
  instead:

```csharp
// BEFORE (2.9.0) — the tools group is the first annotator group in the toolbar
var first = toolbar.QuerySelector("[data-tm-group='tools']");

// AFTER (2.9.1) — history (undo/redo) rides before the tools; select by group name
var history = toolbar.QuerySelector("[data-tm-group='history']");
var tools = toolbar.QuerySelector("[data-tm-group='tools']");
```

- **TmPdfViewer: annotations are no longer single-page only (PD1 superseded, FZE-13).** The
  `pdf-annotations-single-page-hint` note (localized `TmPdfViewer_AnnotationsSinglePageOnly`) is
  gone — continuous mode now renders the per-page markers, and selecting a thread from the panel
  scrolls to its anchor instead of raising `ViewModeChanged(SinglePage)`. Hosts that detected the
  mode switch (or keyed CSS/E2E off the hint's `.tm-pdf-viewer__annotations-hint` row) should drop
  that path; the `.tm-pdf-viewer__continuous-wrap` DOM gained a JS-owned
  `.tm-pdf-viewer__continuous-pages` child (the wraps moved inside it) and a Blazor-owned
  `.tm-pdf-viewer__continuous-overlays` sibling, so `:scope > .tm-pdf-viewer__page` selectors must
  become `.tm-pdf-viewer__page` descendant lookups. `OverlayContent` keeps its single-page-only
  contract (it is aligned to THE canvas and never renders in Continuous) — hosts needing per-page
  content in either mode move to the new templated `PageOverlayContent`.

```razor
@* BEFORE (2.9.0) — annotations forced the viewer back to SinglePage *@
<TmPdfViewer Url="@url" EnableAnnotations="true" ViewMode="PdfViewMode.Continuous"
             ViewModeChanged="mode => wasSwitched = mode == PdfViewMode.SinglePage" />

@* AFTER (2.9.1) — the thread select scrolls and stays Continuous; per-page host overlays
   go through the templated slot (context: PageNumber, IsCurrentPage, Width, Height, Rotation) *@
<TmPdfViewer Url="@url" EnableAnnotations="true" ViewMode="PdfViewMode.Continuous">
    <PageOverlayContent Context="page">
        <div style="position:absolute; inset:0; pointer-events:auto;">
            My page @page.PageNumber overlay (@page.Width×@page.Height, current: @page.IsCurrentPage)
        </div>
    </PageOverlayContent>
</TmPdfViewer>
```

- **TmPdfAnnotator: the interaction surface is per page in Continuous (FZE-14).** In
  `PdfViewMode.SinglePage` nothing changes — one `pdf-annotator-surface` over the canvas, one
  `tm-pdf-annotator-layer`, exactly as before. With `ViewMode="PdfViewMode.Continuous"` the
  annotator renders a layer + surface pair PER MOUNTED PAGE SLOT instead of a single
  viewport-aligned pair: each surface keeps the `pdf-annotator-surface` test id and gains a
  `data-page` attribute, and each pair rides the viewer's
  `.tm-pdf-viewer__page-overlay-slot[data-page="N"]` (FZE-13) inside a new
  `.tm-pdf-annotator__page-surface-host` wrapper (fills the slot, `pointer-events: none`).
  Hosts that located THE annotator surface as `.tm-pdf-annotator__surface` in Continuous must
  scope the lookup to the page slot (`.tm-pdf-viewer__page-overlay-slot[data-page='4']
  [data-testid='pdf-annotator-surface']`); E2E selectors keyed on a single surface break the
  same way. Single-page CSS/hosts are untouched — the default `ViewMode` is additive.

```razor
@* BEFORE (2.9.0) — the annotator pinned the viewer to SinglePage: ONE surface *@
<TmPdfAnnotator Url="@url" />
@* DOM: .tm-pdf-annotator .tm-pdf-annotator__surface  (one, page-agnostic) *@

@* AFTER (2.9.1) — Continuous is opt-in via ViewMode; the tools work per page *@
<TmPdfAnnotator Url="@url" ViewMode="PdfViewMode.Continuous" ShowViewModeToggle="true" />
@* DOM: .tm-pdf-viewer__page-overlay-slot[data-page] >
        .tm-pdf-annotator__page-surface-host > .tm-pdf-annotator-layer + .tm-pdf-annotator__surface[data-page] *@
```

- **TmPdfViewer + TmPdfAnnotator: the "Show resolved" checkbox became the segmented
  "All | Unresolved" filter (FZE-12).** The `pdf-annotation-show-resolved` /
  `pdf-annotator-show-resolved` checkbox (`label.tm-pdf-*-panel__filter` wrapping an
  `<input type="checkbox">`) is gone. Both panels now render the shared `TmPdfThreadFilter`:
  a `role="radiogroup"` with the test id `pdf-annotation-filter` / `pdf-annotator-filter` and
  two native-radio segments `…-filter-all` / `…-filter-unresolved` (`.tm-pdf-thread-filter`
  BEM block), whose labels carry the live counts ("All (4)" / "Unresolved (3)"). The panel
  API is unchanged — `ShowResolved`/`ShowResolvedChanged` map All ↔ `true`,
  Unresolved ↔ `false`, and the viewer's `ShowResolvedAnnotations` still seeds the state — so
  only host CSS/E2E keyed off the old DOM needs migrating. The removed localization keys are
  `TmPdfViewer_ShowResolved` and `TmPdfAnnotator_ShowResolved` (replaced by the plural
  families `TmPdfThread_FilterAll` / `TmPdfThread_FilterUnresolved`, the group label
  `TmPdfThread_FilterLabel` and the `TmPdfThread_EmptyUnresolved` empty state — all four
  languages ship them); hosts that overrode the old keys should move the overrides to the
  new families.

```razor
@* BEFORE (2.9.0) — a checkbox in the panel header *@
<label class="tm-pdf-annotation-panel__filter">
    <input type="checkbox" data-testid="pdf-annotation-show-resolved" checked="@ShowResolved"
           @onchange="ToggleResolvedAsync" />
    @Loc["TmPdfViewer_ShowResolved"]
</label>

@* AFTER (2.9.1) — the shared segmented filter; the panel renders it for you *@
<TmPdfThreadFilter Threads="@Threads"
                   ShowResolved="@ShowResolved"
                   ShowResolvedChanged="@ShowResolvedChanged"
                   TestIdBase="pdf-annotation" />
@* DOM: [data-testid='pdf-annotation-filter'] role=radiogroup
        [data-testid='pdf-annotation-filter-all']        (checked when ShowResolved)
        [data-testid='pdf-annotation-filter-unresolved']  (checked when !ShowResolved) *@
```

- **TmPdfAnnotator: the toolbar color chip became a color-picker trigger button (FZE-12).**
  The static `<span class="tm-pdf-annotator__color-chip"> + <span
  class="tm-pdf-annotator__color-label">` pair is now wrapped by a
  `<button class="tm-pdf-annotator__color-trigger" aria-haspopup="dialog" aria-expanded=…>`
  that opens the palette (pressing it again / Escape closes; the reset action inside is
  `pdf-annotator-color-reset`). The chip keeps its class and its `aria-hidden`, the label
  keeps its class and test id, and `AllowColorPicker="false"` restores the exact pre-2.9.1
  static pair (no button) for hosts that embed their own picker. A host styling the chip or
  the label now has to survive the extra `button` ancestor (e.g. scope with
  `.tm-pdf-annotator__color-trigger .tm-pdf-annotator__color-chip`); a host that positioned
  elements against the old inline pair should target the trigger button instead. On a mobile
  layout the new `pdf-annotator-color-tile` rides the G2 palette row (a third tile) and opens
  the palette as the F3 bottom sheet.

- **TmPdfViewer: the search UI defaults to the Floating card (FZE-11).** The new
  `SearchPresentation` parameter defaults to `PdfSearchPresentation.Floating`: on desktop and
  tablet the search UI renders as a card anchored top-right over the page body
  (`data-testid="pdf-search-card"`, `role="search"`) instead of the full-width
  `.tm-pdf-viewer__search-bar` row under the toolbar. The card STARTS CLOSED — `ShowSearch="true"`
  alone no longer opens it at load; hosts open it via the toolbar's search toggle (or
  `OpenSearchAsync()`), and E2E/bUnit flows that typed into `pdf-search-input` right after load
  must click `pdf-search-toggle` first. `SearchPresentation="Inline"` (the pre-2.9.1 in-flow
  bar) and a mobile layout keep the bar open at load. A host that keyed off the bar's markup
  (or its `.tm-pdf-viewer__search-input` row layout) should either opt back with
  `SearchPresentation="PdfSearchPresentation.Inline"` — which renders the pre-2.9.1 markup
  unchanged — or restyle against the card (`.tm-pdf-viewer__search-card`). Mobile layouts are
  unaffected: the sheet-style bar stays in both presentations.

```razor
@* BEFORE (2.9.0) — the search UI was a full-width bar under the toolbar, open at load *@
<TmPdfViewer Url="@url" ShowSearch="true" />

@* AFTER (2.9.1) — default Floating card over the page body; starts CLOSED, the
   toolbar toggle (or OpenSearchAsync) opens it *@
<TmPdfViewer Url="@url" ShowSearch="true" />

@* AFTER — opt back into the pre-2.9.1 in-flow bar (open at load) *@
<TmPdfViewer Url="@url" ShowSearch="true" SearchPresentation="PdfSearchPresentation.Inline" />
```

- **TmPdfViewer: the clockwise rotate tooltip is now `TmPdfViewer_RotateClockwise` (FZE-11).**
  `AllowRotation` renders two direction buttons, so the single "Rotate" label
  (`TmPdfViewer_Rotate`, kept as the group's accessible name) was split into
  `TmPdfViewer_RotateClockwise` and `TmPdfViewer_RotateCounterClockwise`. Hosts that override
  the old key for custom wording must add the two new keys (all four languages ship them);
  the `rotate-cw` button no longer reads `TmPdfViewer_Rotate`.

- **TmPdfViewer: the header subtitle appends the file size by default (FZE-11).** With
  `ShowHeader` and no explicit `Subtitle` the subtitle is no longer the neutral "Pages: N" —
  once the document length is known it reads "Pages: N · 1.4 MB". Hosts asserting the exact
  subtitle text (unit tests, E2E snapshots) either turn the append off or assert the extended
  form; an explicit `Subtitle` is never augmented and a stream without a known length keeps
  the pages-only form.

```razor
@* BEFORE (2.9.0) — the subtitle stayed "Pages: N" forever *@
<TmPdfViewer Url="@url" ShowHeader="true" Title="Contract" />

@* AFTER (2.9.1) — "Pages: N · 1.4 MB" once the size is known *@
<TmPdfViewer Url="@url" ShowHeader="true" Title="Contract" />

@* AFTER — restore the pages-only subtitle *@
<TmPdfViewer Url="@url" ShowHeader="true" Title="Contract" ShowFileSize="false" />
```

- **TmPdfViewer + TmPdfAnnotator: the annotation-card time stamps and counted texts changed
  format (FZE-9).** `TmPdfThread_ReplyCount` is no longer the neutral `"Replies: {0}"`
  (cs `"Odpovědi: {0}"`) but a `Loc.Plural` family — English `"{0} reply"`/`"{0} replies"`,
  Czech `"{0} odpověď"`/`"{0} odpovědi"`/`"{0} odpovědí"` — and the same happened to
  `TmPdfViewer_CommentsCount` and `TmPdfAnnotator_DraftStrokes`. The card/comment time stamps
  render RELATIVE text ("5 min ago") with the absolute time in the `<time datetime>`
  attribute and the `title` tooltip (previously an absolute "g" string in the text, no
  tooltip). Hosts that override these keys must provide the plural forms their language needs
  (see docs/localization-plurals.md) and that expect the absolute text in the element body
  should read `title` (or their own formatter) instead.

```razor
@* BEFORE — absolute text, neutral count *@
<span class="tm-pdf-comment-item__time">@PdfAnnotationTextHelper.FormatTime(Comment.CreatedAt)</span>
<span class="tm-pdf-thread-card__replies">@Loc["TmPdfThread_ReplyCount", ReplyCount]</span>

@* AFTER — relative stamp + absolute tooltip, natural plural *@
<time class="tm-pdf-comment-item__time" datetime="@stamp" title="@absolute">@relative</time>
<span class="tm-pdf-thread-card__replies">@Loc.Plural("TmPdfThread_ReplyCount", ReplyCount)</span>
```

- **TmPdfAnnotator: the interaction surface no longer claims `touch-action: none` outside Draw
  mode (FZE-9 review fix).** `.tm-pdf-annotator__surface--active` (Comment/Stamp) keeps only
  `pointer-events: auto` + the crosshair cursor, so one-finger scrolling and the pinch work
  over the page on a phone; the `touch-action: none` claim moved to
  `.tm-pdf-annotator__surface--draw`, where the pointer events own the gesture. A host that
  styled the active modifier expecting the old behavior should target the `--draw` modifier.

- **TmPdfViewer: the scroll wraps now claim `touch-action: pan-x pan-y` (FZE-9).** One finger
  still scrolls the pages, but the browser's native page pinch over the canvas is blocked so
  the shared JS pinch owns the zoom. A host overlaying its own gesture surface on the canvas
  wrap should mirror that value (or go tighter, like the annotator's Draw mode).

- **TmPdfAnnotator: the DOM and layout moved onto `TmEditorShell`, and a mobile layout offers
  light edits only (G2).** The annotator root is still `.tm-pdf-annotator` with the
  `pdf-annotator` test id and keeps every `pdf-annotator-*` test id, but the two-column
  `.tm-pdf-annotator__body` wrapper is gone: the viewer now lives in the shell's
  `data-region="canvas"` region and the thread panel in `data-region="right"` (a docked aside
  with a resize separator on desktop/tablet, a non-modal bottom sheet on mobile — where the
  panel renders with `ShowChrome=false`). The annotator root owns the height contract
  (`Height`, default "600px") and the embedded viewer flexes inside it; a host that relied on
  the annotator growing past `Height` for the compact fallback row now gets the row inside the
  same height. On a mobile layout the highlight/stamp/draw tools are not rendered (an active
  edit mode falls back to Browse), the tool palette is the G2 tile row, the export/print
  commands live in the F5 action bar and the merged toolbar gets nothing injected. Migration:
  hosts styling `.tm-pdf-annotator__body` or positioning against it should target
  `[data-region='canvas']` / `[data-region='right']` instead; a host that needs the full tool
  set on a phone pins `LayoutMode="TmLayoutMode.Desktop"`; the mobile sheet starts CLOSED at
  load and on every desktop/tablet→mobile switch, so a host that wants the sheet open on a
  phone explicitly supplies `ShowPanel="true"` (the desktop default keeps the docked panel
  open) — and on a tablet the annotator renders its tools as a full-width labelled row instead
  of merging them into the viewer toolbar.

- **TmPdfViewer: the toolbar DOM is now a core `TmToolbar`, and `ZoomMode` defaults to `Auto`
  (fit-width on mobile).** Two behavior changes in one migration:

  1. **Toolbar DOM.** The hand-rolled `role="toolbar"` row was replaced by the core
     `TmToolbar` (Overflow=Menu with priority-based "More"). The toolbar root keeps the
     `.tm-pdf-viewer__toolbar` class, `role="toolbar"`, the `PDF controls` aria-label and the
     `pdf-viewer-toolbar` test id; the buttons keep `.tm-pdf-viewer__btn` plus their
     `aria-label`s, the page input/count and the search bar keep their classes and test ids,
     and the groups keep `role="group"` + their localized labels. What changed: buttons now
     render as `.tm-toolbar-btn` (with the compact 28 px chrome layered on top), group
     wrappers carry `data-tm-group` (`navigation`, `zoom`, `rotate`, `search`, `comments`),
     the trailing file links live in the toolbar's actions section instead of a
     `.tm-pdf-viewer__toolbar-trailing` wrapper, and the separators are core
     `.tm-toolbar-divider` elements. Hosts styling the removed internals must move:
     `.tm-pdf-viewer__group` → `.tm-toolbar-group`, `.tm-pdf-viewer__toolbar-trailing` →
     `.tm-pdf-viewer__toolbar ::deep .tm-toolbar-actions`, `.tm-pdf-viewer__separator` →
     `.tm-toolbar-divider`.

  ```razor
  @* BEFORE — hand-rolled row, always-icon buttons, Scale-driven zoom *@
  <TmPdfViewer Url="@url" ZoomMode="PdfZoomMode.Custom" />

  @* AFTER — same look as before: icon-only toolbar, scale kept at every width *@
  <TmPdfViewer Url="@url" ZoomMode="PdfZoomMode.Custom" ToolbarLabels="ToolbarLabels.Icons" />
  ```

  2. **`ZoomMode` default is `Auto`.** On a mobile layout the viewer now computes the scale so
     the page fills the container width (and keeps recomputing on resize); on desktop and
     tablet the bound `Scale` is kept unchanged. Zoom buttons/presets still hand control back
     to the user (Auto demotes to `Custom`). **To restore the previous behavior at every
     width, set `ZoomMode="PdfZoomMode.Custom"`.**

  - **Panels move off the bar on tablet/mobile.** On a tablet layout the comments panel is a
    `TmSidePanel` side sheet closed by default and the thumbnails start collapsed; on mobile
    the thumbnails and the comments panel are bottom sheets and the "Pages | Hledat |
    Komentáře (N)" `TmMobileActionBar` drives them. Hosts that relied on the comments panel
    being open at load on a narrow container should open it themselves
    (`LayoutMode="TmLayoutMode.Desktop"` pins the docked chrome).

- **TmPdfViewer: annotation overlays in Continuous mode are gone (PD1).** Previously a host that
  enabled annotations and switched the viewer to `Continuous` rendered the annotation overlay and
  `OverlayContent` over the (never painted) continuous DOM — markers and custom overlays were
  misaligned and unusable. Now the overlay and `OverlayContent` render in `SinglePage` only: the
  comment thread panel stays readable in Continuous (with a localized hint), and selecting a
  thread returns the viewer to `SinglePage` on the thread's page. **Text-selection annotation
  creation is disabled in Continuous mode too** — the selection handler reads the single-page
  canvas and the annotation overlay does not exist there, so there is nothing to attach a draft
  to; the panel, replies, resolve/delete and the thread list all keep working. Hosts that need
  overlay- or selection-driven annotation creation should keep `ViewMode="SinglePage"` (as
  `TmPdfAnnotator` and `TmRedactionLayer` do) or react to `ViewModeChanged`:

  ```razor
  @* BEFORE — the overlay/selection silently broke in Continuous *@
  <TmPdfViewer Url="@url"
               EnableAnnotations="true"
               ShowViewModeToggle="true"
               ViewMode="@mode" />

  @* AFTER — keep the annotating viewer pinned to SinglePage … *@
  <TmPdfViewer Url="@url"
               EnableAnnotations="true"
               ViewMode="PdfViewMode.SinglePage" />

  @* AFTER — … or switch back on demand (e.g. a "Review" toggle) *@
  <TmPdfViewer Url="@url"
               EnableAnnotations="true"
               ShowViewModeToggle="true"
               ViewMode="@_reviewMode"
               ViewModeChanged="@(m => _reviewMode = m)" />
  @* The viewer itself already returns to SinglePage when a comment thread is selected. *@
  ```

- **TmPdfAnnotator: the annotator has ONE toolbar now — its groups are merged into the embedded
  viewer's toolbar, and the export group moved into an optional header row.** Previously the
  annotator rendered its own disconnected bar (mode pills, color chip, export buttons, panel
  toggle) above the viewer's toolbar; now `TmPdfAnnotator` composes its tool group into the
  viewer toolbar through the `ToolbarStart` slot (before the navigation/zoom groups) and its
  export group + panel toggle through `ToolbarEnd` (right-aligned after them), so navigation,
  zoom, rotation, thumbnails and search all live in the same row. The annotator's own
  `.tm-pdf-annotator__toolbar` row renders only in the compatibility case
  (`ShowViewerToolbar="false"`, documented below, or no document yet). The export group is a
  labelled `role="group"` of secondary buttons: it sits at the toolbar end by default and moves
  into the header row when `ShowHeader="true"` (after the Open link, before the host's
  `HeaderActions`). The active tool is now a tonal pill (`--tm-color-primary-subtle` +
  primary content) instead of the filled primary button, the color indicator carries a visible
  localized label (`TmPdfAnnotator_ColorLabel`), the tool group and the toggle use the new
  `TmPdfAnnotator_ToolsGroup` / `_TogglePanel` labels, and the mode buttons shrank to the
  viewer's 28 px chrome. **Every `data-testid` `pdf-annotator-*` is unchanged** (mode pills,
  stamp select, export, export-flat, print, panel-toggle, surface), so E2E selectors keep
  working; `pdf-annotator-toolbar` now names only the compact fallback row, and the new
  `pdf-annotator-tools` / `pdf-annotator-export-group` / `pdf-annotator-color-label` ids are
  available for hosts. Hosts styling the old internals must move:

  | Old class | New class |
  |---|---|
  | `.tm-pdf-annotator__toolbar` (annotator bar) | the viewer's `.tm-pdf-viewer__toolbar` (the annotator row is the fallback only) |
  | `.tm-pdf-annotator__modes` (group label) | `.tm-pdf-annotator__tools` (labelled group wrapping modes + stamp picker + color) |
  | `.tm-pdf-annotator__mode--active` (filled primary) | same class, tonal pill (`--tm-color-primary-subtle`) |
  | `.tm-pdf-annotator__color-chip` (chip only) | `.tm-pdf-annotator__color` wrapper: chip + `.tm-pdf-annotator__color-label` |
  | `.tm-pdf-annotator__spacer` | gone — right alignment comes from `.tm-pdf-annotator__toolbar-end` (`margin-inline-start: auto`) |

  ```html
  <!-- BEFORE (two disconnected bars) -->
  <div class="tm-pdf-annotator__toolbar" role="group">
      <div class="tm-pdf-annotator__modes">…mode pills…</div>
      <span class="tm-pdf-annotator__color-chip"></span>
      <div class="tm-pdf-annotator__spacer"></div>
      <button class="tm-pdf-annotator__action">Export PDF</button> …
      <button class="tm-pdf-annotator__action tm-pdf-annotator__panel-toggle">…</button>
  </div>
  <div class="tm-pdf-viewer__toolbar">…pagination + zoom…</div>

  <!-- AFTER (one toolbar; annotator groups merged via viewer slots) -->
  <div class="tm-pdf-viewer__toolbar" role="toolbar">
      <div class="tm-pdf-annotator__tools" role="group" aria-label="Annotation tools">
          …mode pills (tonal active)…
          <div class="tm-pdf-annotator__color"><span class="tm-pdf-annotator__color-chip"></span>
              <span class="tm-pdf-annotator__color-label">Your annotation color</span></div>
      </div>
      …viewer navigation / zoom / rotation groups…
      <div class="tm-pdf-annotator__toolbar-end">
          <div class="tm-pdf-annotator__export-group" role="group" aria-label="Export">
              <button class="tm-pdf-annotator__action tm-pdf-annotator__action--secondary">…</button> …
          </div>
          <button class="tm-pdf-annotator__action tm-pdf-annotator__panel-toggle" aria-pressed="true">…</button>
      </div>
  </div>
  ```

  **Migration:** `ShowViewerToolbar` keeps its name but now means "merge into the viewer's
  single toolbar" (default `true`); with `false` the viewer chrome is hidden and the annotator
  renders its compact fallback row — exactly one toolbar in both cases, never two.

- **TmPdfAnnotator: localization keys renamed — `TmPdfAnnotator_PanelToggle` →
  `TmPdfAnnotator_TogglePanel` and `TmPdfAnnotator_YourColor` → `TmPdfAnnotator_ColorLabel`.**
  The old keys were leftovers that no component used (the annotator's panel toggle speaks
  `TmPdfAnnotator_TogglePanel`, the color indicator's visible label is
  `TmPdfAnnotator_ColorLabel`); the dead entries are removed from all four resource tables
  (en/cs/sk/fr) and from `MockTmLocalizer`. Hosts that supply custom localizer tables must drop
  the old keys. The color chip itself is now `aria-hidden="true"` (no `role`/`aria-label`) —
  the visible `.tm-pdf-annotator__color-label` span is the indicator's accessible name.

- **TmPdfAnnotationThreadPanel / TmPdfAnnotatorPanel internal DOM replaced by the shared cards
  (TmPdfThreadCard + TmPdfCommentItem + TmPdfComposer).** The panels' own item/thread classes
  are gone; hosts styling the internals must move to the new shared classes:

  | Old class | New class |
  |---|---|
  | `.tm-pdf-annotation-panel__item` / `.tm-pdf-annotator-panel__item` | `li.tm-pdf-thread-card` |
  | `…__item--selected` | `.tm-pdf-thread-card--selected` |
  | `…__item--resolved` | `.tm-pdf-thread-card--resolved` |
  | `…__thread-head` | `.tm-pdf-thread-card__header` |
  | `…__badge` | `.tm-pdf-thread-card__badge` |
  | `…__kind` (annotator) | `.tm-pdf-thread-card__kind` (normal casing) |
  | `…__preview` | `.tm-pdf-thread-card__preview` |
  | `…__status` (plain uppercase text) | `.tm-pdf-thread-card__chip.tm-pdf-thread-card__chip--success` |
  | `…__detail` | `.tm-pdf-thread-card__detail` |
  | `…__list` / `…__empty` | `ul.tm-pdf-thread-list` / `.tm-pdf-thread-list__empty` (the shared `TmPdfThreadList`) |
  | `…__quote` (inside a thread) | `.tm-pdf-thread-list__quote` (panel prompts keep `…panel__quote`) |
  | `…__comment` | `.tm-pdf-comment-item` |
  | `…__comment-head` / `__author` / `__time` | `.tm-pdf-comment-item__head` / `__author` / `__time` |
  | `…__comment-body` | `.tm-pdf-comment-item__body` |
  | `…__link` (inline delete link) | `.tm-pdf-comment-item__menu .tm-dropdown-item` (⋯ menu: Edit + Delete) |
  | `…__input` | `.tm-pdf-composer__input` |
  | `…__actions` | `.tm-pdf-composer__actions` |
  | `…__btn` / `__btn--primary` / `__btn--ghost` | `.tm-pdf-composer__btn` / `__btn--primary` / `__btn--ghost` |
  | — (new) | `.tm-pdf-thread-card__top`, `__spacer`, `__chevron`, `__meta`, `__author`, `__sep`, `__time`, `__page`, `__replies`; `.tm-pdf-comment-item__edited`, `__edit-input`, `__edit-actions`, `__meta`, `__menu`; `.tm-pdf-composer--sticky`; panels: `…__close`; annotator: `…__panel-toggle` |

  ```html
  <!-- BEFORE (annotation panel, simplified) -->
  <li class="tm-pdf-annotation-panel__item tm-pdf-annotation-panel__item--selected">
      <button class="tm-pdf-annotation-panel__thread-head">
          <span class="tm-pdf-annotation-panel__badge">1</span>
          <span class="tm-pdf-annotation-panel__preview">Body preview…</span>
          <span class="tm-pdf-annotation-panel__status">RESOLVED</span>
      </button>
      <div class="tm-pdf-annotation-panel__detail">
          <div class="tm-pdf-annotation-panel__comment">
              <div class="tm-pdf-annotation-panel__comment-head">…</div>
              <button class="tm-pdf-annotation-panel__link">Delete</button>
          </div>
      </div>
  </li>

  <!-- AFTER -->
  <li class="tm-pdf-thread-card tm-pdf-thread-card--selected">
      <button class="tm-pdf-thread-card__header" aria-expanded="true" aria-controls="…">
          <span class="tm-pdf-thread-card__top">
              <span class="tm-pdf-thread-card__badge" style="background:#2563eb">1</span>
              <span class="tm-pdf-thread-card__kind">Comment</span>
              <span class="tm-pdf-thread-card__spacer"></span>
              <span class="tm-pdf-thread-card__chip tm-pdf-thread-card__chip--success">Resolved</span>
              <span class="tm-pdf-thread-card__chevron">…</span>
          </span>
          <span class="tm-pdf-thread-card__preview">Body preview…</span>
          <span class="tm-pdf-thread-card__meta">
              <span class="tm-pdf-thread-card__author">Alena</span>
              <span class="tm-pdf-thread-card__sep">·</span>
              <span class="tm-pdf-thread-card__time">6.6.2025 9:00</span>
              <span class="tm-pdf-thread-card__page">Str. 3</span>
              <span class="tm-pdf-thread-card__replies">Odpovědi: 2</span>
          </span>
      </button>
      <div class="tm-pdf-thread-card__detail">
          <div class="tm-pdf-comment-item">…<div class="tm-pdf-comment-item__menu">⋯ Edit/Delete</div></div>
          <div class="tm-pdf-composer tm-pdf-composer--sticky">…</div>
      </div>
  </li>
  ```

  The panel-level classes (`tm-pdf-annotation-panel__header/-title/-filter/-new/-prompt/-quote/-empty/-list`,
  `tm-pdf-annotator-panel__*` equivalents, plus the new `__close`) are unchanged; the panel width
  still resolves through `--tm-pdf-panel-width`. Delete moved from an inline link into the comment's
  "⋯" menu, so automation must open the menu (`[data-testid='…-comment'] .tm-dropdown-trigger`)
  before clicking `[data-testid='…-delete']`; the resolved chip text is no longer uppercased.

- **TmPdfViewer toolbar DOM restructured (desktop reference).** The toolbar root is now
  `role="toolbar"` with labelled `role="group"` sections (Navigation | Zoom | Rotate | Mode ||
  Search | Comments) instead of one flat `role="group"` bar:

  ```html
  <!-- BEFORE -->
  <div class="tm-pdf-viewer__toolbar" role="group" aria-label="PDF controls">
      <div class="tm-pdf-viewer__pagination">…<span class="tm-pdf-viewer__page-label">Page 1 of 10</span>…</div>
      <div class="tm-pdf-viewer__zoom">…<span class="tm-pdf-viewer__zoom-label">100%</span>…</div>
      <div class="tm-pdf-viewer__view-mode"><button aria-pressed="true">…</button>…</div>
  </div>

  <!-- AFTER -->
  <div class="tm-pdf-viewer__toolbar" role="toolbar" aria-label="PDF controls">
      <div class="tm-pdf-viewer__group" role="group" aria-label="Navigation">
          …<input class="tm-pdf-viewer__page-input" type="number" min="1" max="10" value="1" />
            <span class="tm-pdf-viewer__page-count">/ 10</span>…
      </div>
      <div class="tm-pdf-viewer__group" role="group" aria-label="Zoom">
          …<div class="tm-pdf-viewer__zoom"><button class="tm-dropdown-trigger">100%</button>…</div>…
      </div>
      <div class="tm-pdf-viewer__view-mode" role="radiogroup" aria-label="View mode">
          <input type="radio" checked />…  <!-- native radios: arrows are the browser's -->
      </div>
  </div>
  ```

  Style against `.tm-pdf-viewer__page-input`, `.tm-pdf-viewer__page-count`, the
  `.tm-pdf-viewer__zoom .tm-dropdown-trigger` and the view-mode radiogroup — `.tm-pdf-viewer__page-label`
  and `.tm-pdf-viewer__zoom-label` are gone. The thumbnails sidebar now renders its own header and
  list (`__thumbnails-header` / `__thumbnails-list`).

- **TmPdfViewer: "Download" is the primary action, "Open" the secondary one.** Both were equal
  links; the download link now paints the primary fill (`tm-pdf-viewer__link--primary`) and the
  open link keeps a bordered secondary look (`tm-pdf-viewer__link--secondary`). With
  `ShowHeader="true"` both move into the document header and the toolbar renders none.
- **Wireframe editor + document-open dialog redesign: plan summary (ce19aa8c, phases 1-10).** `TmWireframeEditor` now renders through the core `TmEditorShell` and resolves desktop / tablet / mobile from its own container (`data-editor-layout`, 768 / 1200): a labelled `TmToolbar` with the frozen `data-tm-group` set, a left panel with Components / Pages / Layers tabs (`PageNavigation=Panel` is opt-in), device presets and a Name field in the inspector (`WireframeElement.Name`, additive in Abstractions, schema and MCP), Pointer Events on the shared `pointer-drag.js` / `pointer-gesture.js` helpers (finger, pen, pinch, long-press menu, gesture rollback), tablet sheets and a phone "viewing + light edits" mode (action bar, bottom sheets, action-sheet context menu, export sheet), `ReadOnly` / `ShowRulers` / `ShowGrid` never assigned by the component. The shared core `TmDocumentOpenDialog` became an XLarge modal with a WAI-ARIA tree, breadcrumb, radio-card Link / Copy choice, opt-in `ShowRecent` / `LayoutMode`, and a fullscreen drill-down panel below 640 px; its public API, `DocumentOpenResult` and `ITempoDocumentLibraryProvider` are unchanged. Every breaking change of the plan is listed under Breaking / Migration with a before / after; the contracts are in [docs/wireframe-editor.md](docs/wireframe-editor.md) ("Reference at a glance"), [docs/touch-interaction.md](docs/touch-interaction.md) and the AGENTS.md "Wireframe editor chrome" section.
- **`TmDocumentOpenDialog` mobile layout: a fullscreen panel with drill-down folders (wireframe redesign plan ce19aa8c, phase 9, WF4).** In a dialog body narrower than
  640 px - or with `LayoutMode="TmLayoutMode.Mobile"` - the dialog renders `TmModal` with `MobilePresentation=Fullscreen` and an inline footer that never leaves the screen:
  a sticky head (search at full width; a back button, the breadcrumb and the list / grid switch; the inline new-folder, rename and delete-confirm bars, usable at 320 px),
  ONE scrolling region and a sticky footer. There is **no tree**: the subfolders of the current folder (taken from the folder tree the dialog already holds - no provider
  member is added) are `button.tm-dod-folder-row` rows with a chevron, a tap drills in, the back button steps one level up and a crumb jumps to that level (focus follows: to the
  back button after a drill, to the folder row that was left after a step back). Documents are two-line rows (`.tm-dod-row-name`; `.tm-dod-row-meta` = `date · author`) with a
  thumbnail rendered from `PreviewSvg` through the same path as the grid (the file icon when there is none; no new `MarkupString` source) and a per-row **more menu**
  (`.tm-dod-row-menu`, a `TmDropdown` presenting as a bottom sheet titled "More actions for {name}") with Rename / Delete that reuse the desktop flow. The footer holds the Link / Copy
  choice as compact radio cards (`.tm-dod-mode--compact`, the hint stays for assistive technology) above a `TmMobileActionBar` (New folder / Rename / Delete, gated by the provider's
  capabilities) beside the primary **Open**; the header close button is the Cancel. With the on-screen keyboard open the footer stays inside the visible viewport (the overlay is padded by the
  hidden gap, the Link / Copy cards step aside). `DocumentOpenResult`, `ITempoDocumentLibraryProvider` and every public parameter are unchanged; `LayoutMode=Mobile` is now a layout of its own.
  New localization keys (en / cs / sk / fr): `TmDocumentOpenDialog_Back`, `_FolderRow`, `_FolderKind`, `_More`, `_ActionsLabel`. Demo `/document-open-dialog` gains a *Mobile* layout option and wraps its controls on a phone.
- **`TmDocumentOpenDialog` redesign: opt-in "Recent" node and a testable `LayoutMode` (wireframe redesign plan ce19aa8c, phase 8, WF4).** Two new
  opt-in parameters, no `ITempoDocumentLibraryProvider` / `DocumentOpenResult` change. `ShowRecent` (default `false`) adds a virtual **Recent** node above the
  folder tree; selecting it issues `BrowseAsync` with `FolderPath = null`, `SortField = Modified`, `Descending = true`, `Take = PageSize` (the sort buttons are
  disabled on that node, the previous sort returns when you leave it). **Which documents a `null` folder returns is the provider's decision** - the
  `DocumentLibraryQuery.FolderPath` contract says "null or `/` lists the root", so a provider that wants a library-wide recent list must treat `null` as "everywhere"
  (the demo `DocumentLibraryStore` follows the contract and returns the root documents only; it was left unchanged). `LayoutMode` (`TmLayoutMode`, default `Auto`)
  measures the dialog body (a `TmLayoutObserver` with its own 640 / 768 thresholds) and, below 768 px of body width or when forced to `Tablet` / `Mobile`, renders
  the narrow tree and the footer buttons on their own row; `Desktop` forces the wide layout. A forced mode never imports the observer script, so a test renders
  either layout without a DOM. The tree, the list rows and the grid cards are now keyboard-operable as roving tab stops (see Breaking / Migration), the sorted column
  header shows a direction indicator (`.tm-dod-sort-indicator`, `data-direction`), the breadcrumb moved into the right pane with a home icon and `›` separators, and
  the grid listbox has its own label key `TmDocumentOpenDialog_DocumentsLabel` instead of reusing the dialog title. New localization keys (en / cs / sk / fr):
  `_Recent`, `_Home`, `_Expand`, `_Collapse`, `_SortAscending`, `_SortDescending`, `_FoldersLabel`, `_DocumentsLabel`, `_ModeLabel`. The colocated module gained
  `focusRoving` / `getGridColumns` (node tests in `tm-dod-space-guard.test.mjs`). Demo `/document-open-dialog` has "Show Recent" and layout switches.

- **Wireframe editor mobile layout: viewing + light edits (redesign plan ce19aa8c, phase 7, decision G2).** In an editor container below 768 px
  (`data-editor-layout="mobile"`) `TmWireframeEditor` renders the core `TmEditorShell` mobile presentation: a compact header (document title plus
  `HeaderStart` / `HeaderActions`) with a **select / pan / undo / redo / More** tool row, a canvas that takes the whole width (the canvas measured 0 px
  wide on a phone before), a **floating zoom** (`- | % | + | fit`, a row over the canvas, outside the svg), a **`TmMobileActionBar`** (**Add** | **Pages & layers**
  | **Export** | **More**) and non-modal bottom sheets (peek / half / full) for the component palette, the **Properties** inspector and a **Pages | Layers**
  segment (the shell's one `Extra` tab). Selecting an element peeks the Properties sheet with its type and the quick actions **Duplicate / Lock / Delete**
  (Name and every property are one swipe up); tap-insert, undo / redo, layer visibility and lock, page switch, device presets (More), read-only and the grid
  toggle, JSON / SVG export and the PNG / PDF export dialog stay. Resize / rotate handles, the rubber band, the connector tool, align / distribute,
  grouping, rulers, the minimap and JSON import are **tablet and up** (the canvas runs with `LightEditing`; a one-finger drag on the empty canvas pans, a tap
  on it deselects). The long-press menu is the shared action sheet (`TmWireframeContextMenu.Presentation=Sheet`), and `TmWireframeExportDialog` is a bottom
  sheet with an inline, sticky footer (`MobilePresentation`, default `Auto`). New: `TmWireframeEditor.MobileActions` (host actions of the bar; there is no
  built-in Save or Open), `TmWireframeDesignerCanvas.LightEditing` / `ContentVersion`, `WireframeContextMenuPresentation`, `AddElementsCommand`. Opening, switching
  and closing a sheet does not re-render the canvas (a parent-render gate keyed on `ContentVersion`; `MutationObserver` E2E). Localised en / cs / sk / fr
  (`TmWireframe_Mobile*`, `TmWireframe_QuickActionsAriaLabel`, `TmWireframe_ZoomControls`, `TmWireframe_ActionsSheetTitle`). See
  [docs/wireframe-editor.md](docs/wireframe-editor.md#mobile-layout-phase-7).
- **Wireframe export download through a Blob URL.** `tmWireframeDesigner.downloadFile` saves through `URL.createObjectURL` on the hidden anchor (a multi-MB base64
  `data:` URL is dropped by iOS Safari and some Chromium builds); the base64 `data:` URL stays as the fallback. iOS Safari has no real download: the file opens in a
  new tab and is saved from the share sheet (documented).

- **Wireframe editor tablet layout (redesign plan ce19aa8c, phase 6).** In an editor container of 768-1199 px (`data-editor-layout="tablet"`)
  the command row is **two `TmToolbar`s** split by `data-tm-group` (row 1: tools, history, arrange, grouping, alignment, snapping and
  the format painter; row 2: the Components / Properties toggles, zoom, Export and Import, plus the document title), the left panel
  (Components / Pages / Layers) is an **overlay drawer** and the inspector a **non-modal side sheet with a close ×** - at most one of
  them open. Selecting an element opens the inspector, inserting a component closes the drawer, and Escape closes the sheet that holds
  the focus and returns it to the canvas (`TmFocusScope.RestoreFocusTarget`). The minimap is hidden on a tablet, the toolbox is a single
  column of named rows (`TmWireframeToolbox.SingleColumn`, `tm-wd-toolbox--single-column`), and on `(pointer: coarse)` the toolbar
  buttons, page tabs, toolbox rows, sheet close and title reach `--tm-touch-target` (44 px). Opening or closing a sheet re-measures the
  canvas through `tmWireframeDesigner.relayout` without re-rendering it. New stable hooks: `data-testid` `wd-toggle-left`,
  `wd-toggle-inspector`, `wd-left-drawer`, `wd-inspector-sheet`, `wd-sheet-close`, `wd-toolbar-row-1|2`. New keys
  `TmWireframe_ToolbarViewAriaLabel`, `TmWireframe_SheetLeftTitle`, `TmWireframe_SheetInspectorTitle`, `TmWireframe_SheetClose` (en/cs/sk/fr).
- **Wireframe context menu from the keyboard.** Shift+F10 / the ContextMenu key open the menu at the selected element (or the canvas menu when nothing is selected); the menu focuses its first enabled item, Arrow Up/Down/Home/End move between items, and Escape / choosing an item returns the focus to the canvas.
- **Wireframe gesture-rollback announcement.** A move, resize, rotate or waypoint drag cancelled by Escape, `pointercancel` or a second finger is announced in a polite live region (`TmWireframeCanvas_GestureCancelled`, en/cs/sk/fr).
- **Wireframe long-press cue.** A ring grows at the finger during the 500 ms touch hold that opens the context menu (static under `prefers-reduced-motion`).
- **`TmWireframeDesignerCanvas.NotifyViewportChangedAsync()` and `WrapElement`**, plus the script's `relayout`: the canvas re-fits (pristine view) or keeps centre and zoom (moved view) when its container resizes, and follows size changes through a `ResizeObserver`.

- **Wireframe canvas: touch and pen (redesign plan ce19aa8c, phase 5).** The canvas now works with a finger and a pen on a tablet (full
  editing from the tablet up; the mobile layout stays viewing plus light edits). One finger or pen selects, drags (single, multi, groups),
  resizes through the eight handles, rotates, rubber-bands, draws connectors with the connector tool and drags connector waypoints;
  **two fingers pinch-zoom and pan** (zoom clamped to 0.15x-5x, the toolbar read-out follows); a **500 ms hold opens the context menu**
  beside the finger; a **double-tap** is a double-click (adds a connector waypoint). On a coarse pointer the selection handles are painted
  1.5x larger and every handle gets an invisible hit area of at least 24 screen px (`--tm-wd-handle-hit`, `--tm-touch-target` = 44px on a
  coarse pointer), the connector waypoint gets a finger-sized hit circle, and `.tm-wd-canvas__svg` claims the touch (`touch-action: none`).
  Every gesture runs on the shared core helpers - `pointer-drag.js` (`attach`, incl. the long-press) and `pointer-gesture.js`
  (`attachPinch`) - loaded from `wireframe-designer.js`; the package only adds `wireframe-gestures.mjs` (pure maths: double-tap, coarse
  handle sizes, pinch to viewBox, nudge step). A mouse is unchanged (HTML5 toolbox drag-and-drop, right-click menu, middle-button pan,
  Space+drag, wheel); Escape, a `pointercancel`, a second finger, a read-only flip and disposal roll a gesture back and commit nothing.
  Non-drag alternatives (WCAG 2.5.7): arrow keys nudge the selection (Shift = one grid cell; read-only and locked elements are skipped),
  the inspector X/Y/W/H fields resize, the zoom buttons zoom, a toolbox tap / Enter / Space inserts. New: `TmWireframeDesignerCanvas.OnZoomChangedEvent`,
  `TmWireframePagesPanel.ShowTitle`, `--tm-wd-handle-hit`, `npm run test:wireframe`.
- **Wireframe phase 4 review fixes.** `RenameElementCommand` and `ResizeCanvasCommand` remember the page they were created on, so Undo / Redo
  still reach it after a page switch; undoing a canvas resize now tells the canvas script the size changed back; the inspector explains a
  rejected canvas size (`TmWireframeProps_CanvasSizeRange`, `role="alert"`, `aria-invalid`) instead of ignoring it; the Pages tab no longer
  repeats the word "Pages" (`TmWireframePagesPanel.ShowTitle`, default `true`); an empty selection shows the hint without an empty
  "Selection" collapsible; the toolbox grid
  uses equal-width cells with an ellipsis label; the toolbar wrapper no longer repeats `role="group"` / `aria-label`; the initial zoom
  read-out shows the real fitted scale of a page wider than the viewport.
- **Wireframe editor restructured onto `TmEditorShell` (redesign plan ce19aa8c, phase 4; WF1, WF2, WF3).** The desktop editor follows the
  reference hierarchy: an opt-in header (`ShowHeader`, `HeaderStart`, `HeaderActions`, `Title` — the host brings its own Open/Save), ONE
  `TmToolbar` command row (named groups, per-button `Priority`, `Labels=Auto`, `Overflow=Menu`, Export as the pinned primary action), a left
  panel with *Components* / *Pages* / *Layers* tabs (`TmWireframeSidePanel`, internal; tabs mount lazily so a tab switch never re-renders the
  canvas or the toolbox), the canvas and an inspector that, with nothing selected, offers **Canvas** (device presets Desktop, Laptop, Tablet,
  iPhone 14, Android, Custom over the undoable `ResizeCanvasCommand`), **Page** and **Grid**. New parameters: `PageNavigation`
  (`Tabs` default | `Panel`), `LayoutMode`, `LayoutBreakpoints` (mobile < 640, desktop ≥ 860), `ShowHeader`, `HeaderStart`, `HeaderActions`,
  `Title`, `ShowGridChanged`, `GridSizeChanged`. New public components/types: `TmWireframePagesPanel`, `WireframePageNavigation`,
  `RenameElementCommand`. Mobile/tablet presentation of the shell is the next plan phase; the desktop layout is complete.
- **Wireframe element names (WF3).** `WireframeElement.Name`, the undoable `RenameElementCommand`, the inspector's *Name* field and the canvas
  `aria-label` / `<title>` tooltip; the MCP tools set and return `name`.
- **Wireframe layers panel: row menu, non-drag reorder and real semantics (redesign plan ce19aa8c, phase 3).**
  `TmWireframeLayersPanel` can now be driven entirely without a mouse and without dragging (WCAG 2.5.7):
  every row has a "⋯" menu (core `TmContextMenu`: **Rename layer**, **Move up**, **Move down**, **Delete layer**;
  the first row cannot move up, the last cannot move down, the default layer has no Delete), **F2** renames the row
  from any of its controls, and **Alt+↑ / Alt+↓** reorder it. Reordering, rename, visibility, lock, delete and
  *Move to layer* all go through the editor's undoable command stack (`ReorderLayersCommand`, `RenameLayerCommand`, …),
  so undo/redo works for every path, and a polite live region announces moves and deletions. The drag-and-drop
  drop now paints a drop indicator line (before/after the hovered row) and clears it on drop and `dragend`.
  The active layer is a labelled `radiogroup` of `role="radio"` buttons (Arrow keys roam and activate, one tab
  stop); the eye and the lock are toggle buttons with `aria-pressed`. New parameter
  **`ShowCollapseToggle`** (`bool`, default `true` = today's standalone look): a host that owns the collapse or the
  surrounding panel passes `false` and gets the panel without its own tab, never collapsed, filling its host.
  Look: lighter, per the reference — sentence-case 14px title (no uppercase 11px caption), 36px rows, 32px icon
  buttons (44px = `--tm-touch-target` on `(pointer: coarse)`), the active row tinted with
  `--tm-color-primary-subtle`, the trash quiet on a fine pointer (shown on row hover, `:focus-within` and on the active row) and always shown
  where there is no hover; in the editor's narrow column on a coarse pointer it steps aside for the row menu's Delete
  (five 44px targets do not fit 288px), and the grip is hidden on touch (HTML5 drag does not run there).
  Every key typed inside the panel stays in the panel (the canvas shortcuts - nudge, Delete, Escape - ignore it), keys inside
  an open row menu do not reach the row shortcuts, the default layer (lowest-ordered, moves on reorder) is marked with
  `aria-description`, and moves are announced with their new position.
  New localization keys `TmWireframeLayers_*` (en/cs/sk/fr). Guarded by `TmWireframeLayersPanelTests` (bUnit) and
  `WireframeLayersPanelE2ETests` / `Layers_KeyboardReorder` / `Layers_RowMenuRename` (E2E, incl. axe).
- **`TmFilterBuilder.ResultCountKey`** (`object?`, default `null`). Changing this value re-runs the count; hosts
  pass a token covering inputs outside `ActiveFilters` — search text, column filters, new items or a reloaded
  data provider. `TmDataTable` and `TmMultiViewList` pass an internal version counter they bump on those changes,
  so the preview count always matches what the table shows. No default behaviour change for hosts that do not set it.

- **Default (Plain) `TmDataTable` group zone and export menu lost their pre-redesign CSS.** The opt-in gating
  (`Appearance=Card`) had moved the markup but the stylesheet still styled the default: the group chip rendered
  as the redesign's subtle pill instead of the original solid primary chip, Expand all / Collapse all had no
  styling at all (raw browser buttons), and export items were wrapping flex rows instead of single-line
  `white-space: nowrap` items. The original rules are back as the defaults (tokens instead of the old hex
  values); the redesign styling is scoped to the Card surface via `.tm-data-table-group-zone--card` and the new
  `tm-data-table__export-item--rich` modifier that the Card export items carry. Guarded by bUnit
  (`TmDataTablePhase3OptInTests`) and the `DefaultAppearance_GroupZoneAndExport_KeepTheOriginalLook` E2E.

- **`TmMultiViewList` grouped-card header blended into the card body.** `.tm-mvl-group-header` painted the same
  surface as `.tm-mvl-group-card` and left a margin gap inside the bordered card. Inside a group card the header
  now uses `var(--tm-surface-secondary)` with a hairline bottom border and no gap. Guarded by the
  `GroupedCardHeader_DiffersFromBody_LightAndDark` E2E (light and dark).

- **`TmDataTable` UX defects from the phase 13 screenshot review.** RTL pinned headers reserve the pin glyph (logical padding), long filter placeholders and Scroll cells end in an ellipsis, filter and views sheets use 44px targets and show one Done, and the cards layout drops the "Drag column headers here to group" hint.

- **`TmDataTable` column menu opens with Alt+ArrowDown in a real browser.** The chord was announced (`aria-keyshortcuts`) and
  opened the menu, but its browser default scrolled the page, the header left the viewport and the overlay dismissed itself
  as `anchor-hidden` about 150 ms later (`M` was unaffected). `data-table.js` now cancels the default for that chord on a
  header that announces it (`tmDataTable.attachColumnMenuKeys`, attached only while `ColumnMenuMode` is `Menu`).
  Found by the phase 13 keyboard E2E; the bUnit test sent the key straight to the header and could not see it.

- **Grid keyboard: F2 / Enter on an editable cell moves focus into the editor.** The editing row renders plain cells, not
  grid cells, so the cell that held focus vanished and focus fell to the page: Escape (answered on the editing row) never
  reached the table and a keyboard user could not cancel. `KeyboardNavigation=Grid` now focuses the first editor control
  (`tmDataTable.focusRowEditor`) and, when the edit is saved, returns focus to the roving cell. `Rows` mode is unchanged.

- **TmOverlayPanel no longer takes a focus-restore id.** `RestoreFocusTargetId` was removed.
  Escape returns focus to the anchor inside `overlay.js` (`anchor.focus({ preventScroll: true })`)
  when focus would otherwise die. An outside click does not: the user clicked somewhere else.
  Behaviour change: an outside click on dead space now leaves focus on `<body>` instead of
  restoring it to the anchor; Escape still returns focus to the anchor. Migration (before/after)
  is documented in the Breaking / Migration entry "`TmOverlayPanel.RestoreFocusTargetId` was removed".

- **TmFilterBuilder focuses its own first field option.** Opening the picker no longer runs
  `document.querySelector` through `eval`, so a second builder on the page focuses its own option.
  Escape inside an open nested date picker closes that picker and leaves the filter editor open.

- **TmMultiViewList view switcher is a radiogroup.** The group is named "View"
  (`TmMvl_ViewSwitcher`). Exactly one radio is tabbable; ArrowLeft/Right/Up/Down change the view and move
  focus, and those arrows no longer scroll the page.

- **TmDataTable grid keyboard.** `KeyboardNavigation="Grid"` now keeps exactly one roving tabindex
  across selection, detail, grouping, virtualization, editing and an empty table. Enter activates
  the row once (cell key events no longer also reach the row), Space toggles selection without
  activating, Home/End include the checkbox and expander columns, and focus follows the active cell.
  Grid mode `aria-rowcount` now includes the header rows (the column header, and the filter row
  when one renders), so the last body's `aria-rowindex` is never past the announced count.
  Collapsing a group, hiding a column, or pressing End on the edit-action column keeps that
  single tab stop; controls inside a cell (expander, checkbox, edit action, group toggle) are
  `tabindex="-1"` so Tab leaves the grid instead of stepping into them.

### Added

- **Demo: table/list redesign opt-ins.** `/data-table` gains a Redesign (opt-in) playground (every opt-in behind a control that starts at its default, fixed dates, row-activation counter), a 24rem Mobile cards container, a standalone `TmViewManager` with Set as default and `TmFilterBuilder` in `Popover`/`Auto` with `ResultCountProvider`; `/multi-view-list` gains a matching playground. Wireframe stencils for `TmDataTable`, `TmMultiViewList`, `TmViewManager` and `TmFilterBuilder` expose the same opt-ins as props (`appearance`, `mobileLayout`, `showItemCount`, `viewSwitcherLabels`, `columnMenu`, `exportSelected`, `presentation`, `open`). No default changes.

- **Public tokens `--tm-color-primary-solid` and `--tm-color-on-primary-fixed`.**
  `--tm-color-primary-solid` is `var(--tm-color-primary-600)` in both themes (white on it is 5.17:1;
  the dark `--tm-color-primary` step is primary-400, and white on that is 2.54:1).
  `--tm-color-on-primary-fixed` is `var(--tm-color-white)` in both themes. Both are declared in
  `tokens.css` and re-declared in `tokens-dark.css`, so a wrapper with `data-theme="dark"` does
  not inherit the finished light colour. The active page button is the first consumer.

- **`TmViewManager.MobilePresentation`** (`PanelPresentation`, default `Popover`). The Views panel was the one toolbar overlay that
  never offered the mobile sheet: on a 390px viewport it stayed an anchored popover. `Auto` turns it into a full-width
  bottom sheet on a mobile viewport. `TmDataTable` passes `Auto` when `MobileLayout` is not `None` and
  `TmMultiViewList` when `Responsive` is on, like the export menu and group picker already do; a table or list without
  those opt-ins keeps the popover. Found by the phase 13 overlay E2E.

- **Narrow-table toolbar wraps.** The right toolbar group (Choose columns, Views, Sort, Filters, Export) is no longer
  clipped at 390px: below the table's container breakpoint (and below the 768px viewport breakpoint for a plain table)
  `.tm-data-table-toolbar-right` gets `flex-wrap: wrap`. No change at wider widths.

- **`TmViewManager.Compact`** (bool, default `false`). When true, the Views toggle adds
  `tm-btn-sm` so it lines up with the other card-toolbar triggers. `TmDataTable` passes it when
  `Appearance` is `Card`. The plain toggle is unchanged.

- **Remaining opt-in presentation parameters.** All default `false` or to today's presentation.
  `TmMultiViewList.TableCellLayout.Stacked` drops the subtitle column and renders its value under
  the title (inner `.tm-mvl-cell-stack`; the `td` stays a table cell); `Inline` is unchanged. `TmFilterBuilder.Presentation` of
  `Popover` or `Auto` opens the field picker and editor in a `TmOverlayPanel` anchored to Add
  filter — `Auto` becomes a sheet on a narrow viewport, `Popover` stays a popover; `Inline`
  (including PickersPage) stays in the document flow. `AllowSetDefault` shows "Set {name} as
  default", persists through `IDataTableViewProvider.SetDefaultViewAsync`, reloads and then raises
  `OnSetDefault`; the default view is `aria-current` with a visually hidden "Default view".
  `ShowActiveViewName` puts the active view's name on the toggle (truncated at 16rem, full name in
  `title`). `EditActionsMode.Footer` moves Save and Cancel into a following row whose cell spans
  every rendered column; `Text` labels the in-row buttons. `GroupHeaderTemplate` and
  `StatusVariantField` were already wired; their docs now say what they do. See
  `docs/data-table-redesign.md`.

- **Filter result preview.** `TmDataTable` and `TmMultiViewList` take `ShowFilterResultPreview`
  (default `false`). When the inline filter builder renders, it shows how many records match the
  current filters, including a valid filter still being edited: a `DataProvider` is asked with
  `PageSize` 1 and the preview reads `TotalCount`, while in-memory data is counted with the
  component's own filter rules. A standalone `TmFilterBuilder` takes `ShowResultPreview` plus
  `ResultCountProvider` (`Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>>`) directly.
  Changes are debounced (300 ms) into one call, the text is pluralized (`TmFilterBuilder_ResultPreview`)
  and announced in a `role="status"` region with `aria-live="polite"`. The region is in the document
  as soon as both are set. While the first count is in flight the region shows a spinner and
  "Counting…" (`TmFilterBuilder_ResultPreviewLoading`); a later refresh keeps the previous count and
  sets `aria-busy`. A null count or a thrown exception leaves the region empty. The old static
  "Preview" placeholder is gone — see Breaking / Migration.

- **TmDataTable `AllowExportSelected`.** Opt-in export scope. With `Selectable="true"` the export
  menu offers "All rows" (every row matching the current filter and sort) and "Selected rows (N)"
  (the checked rows, sorted like the current view, with any row a later filter hid appended in
  the order it was checked). The selected choice is `aria-disabled` while nothing is checked — still
  focusable, so its description "Select rows to export them" is announced — and picking a scope
  leaves the menu open. `ExportAsync(format)` still
  exports every matching row; `ExportAsync(format, DataTableExportScope.Selected)` exports the
  selection, and `OnExportCompleted` reports the scope on `DataTableExportResult.Scope` (default
  `All`). A custom `IDataTableExporter` takes the scope too: the new
  `ExportAsync(exporter, DataTableExportScope, string?)` exports the requested rows through any
  format (JSON, PDF, …) with the same download plumbing; `ExportAsync(exporter)` keeps exporting
  all rows. The default menu is unchanged.

- **TmDataTable `ColumnMenuMode.Menu`.** Opt-in kebab menu in each data column header
  (`aria-label` "Column options: {column}", `aria-haspopup="menu"`, `aria-expanded`). Items follow
  the column: Sort ascending / Sort descending and Clear sort (`Sortable`, clear only while the
  column is sorted), Pin left / Pin right or Unpin, Group by this column (`Groupable`), Hide column
  (`Hideable`, omitted for the last visible column) and Filter (`Filterable`, focuses that
  column's filter control). `M` and `Alt+ArrowDown` (announced in `aria-keyshortcuts`) open it from
  the focused header, `Escape` closes it, and the kebab
  is `tabindex="-1"`. The expander, checkbox and edit-action columns get no menu, and the toolbar
  gains no button, so `ToolbarMode="ContentOnly"` hides the toolbar but keeps the kebab. `ShowColumnMenu="false"`
  hides the kebab. `None` (the default) is unchanged — the pin toggle and its `P` shortcut stay. See
  `docs/data-table-redesign.md`.

- **TmMultiViewList `ItemHref`.** With `CardActivation="StretchedLink"` a non-empty href renders the
  card title as an anchor that covers the card; without one the title is a button that raises
  `OnItemClick`. The card itself is no longer a tab stop. `Button` (the default) is unchanged.

- **Wireframe:** the `TmDiagramEditor` stencil now mirrors the redesigned editor shell — header,
  command row, shapes palette, Properties|Layers inspector with docked minimap and status bar on
  desktop; collapsed palette rail and inspector drawer on tablet; compact header and
  Select | Shapes | Properties | More action bar on mobile (Pan lives in More). New stencil props
  `toolbarPreset`
  (`full` | `viewer`, maps to `DiagramToolbarPreset`), `readOnly` and `showMinimap`; the existing
  `layoutMode` prop (`auto` | `desktop` | `tablet` | `mobile`) now selects the variant (`auto`
  always renders the desktop variant, whatever the element width). Existing wireframe documents
  with a `TmDiagramEditor` element render the new desktop shell (exported PNG/SVG change).

- **Core tokens: `--tm-paper`, `--tm-paper-ink`, `--tm-paper-ink-muted`, `--tm-paper-border`,
  `--tm-paper-backdrop`.** Theme-invariant colours for content that represents a printed page
  (diagram pages today; document pages and report previews can adopt them). They are LITERAL
  light values declared only in `tokens.css` — deliberately NOT re-declared in `tokens-dark.css`,
  because absence under the dark selector is what keeps the paper white in every theme; hosts
  may rebrand them by targeting `:root`, `[data-theme="dark"]` and `.tm-dark` together.

- **TmEditorShell: `ShowMobilePanelsToggle` parameter (default `true`).** The labelled
  closed-state bar naming the hidden panels renders only while the parameter is true: a host
  that renders its own mobile action bar (the F5 `TmMobileActionBar`, whose tiles reopen the
  panels) sets it to `false`, giving the mobile panels sheet exactly ONE reopen path and
  landing focus on the host's bar after the sheet closes (instead of the hidden shell toggle).

- **TmDiagramToolbox: `CollapseCategoriesExceptFirst` parameter.** The mobile Shapes sheet
  starts with only the first (General/basic) category expanded; the user's own expand/collapse
  choices win afterwards.

- **DiagramEditor review fixes (phase 6/7 mobile follow-up, plan cbb1964a fze-6).** Mobile
  layout state: the editor keeps a per-session mobile snapshot (sheet + minimap collapse)
  initialised to the X1 defaults, saved on every mobile exit and applied on every mobile entry —
  mobile → desktop → mobile keeps the state the user left behind, while the desktop state in
  between is untouched; `ApplyResolvedLayout` runs the exit branches before the entry branches,
  so desktop → mobile → tablet ends with the palette railed and the inspector expanded.
  `TmDiagramEditor.Tablet.razor.cs` writes the inspector flags only through
  `SetInspectorOpen`. Minimap: the mobile default-hide now applies ONLY while the "⋯" toggle
  exists (`MobileMinimapToggleAvailable = ShowToolbar && ShowMinimap && (Panels || View
  group)`), so a `ToolbarGroups` selection without Panels/View keeps the overlay visible and a
  Viewer preset with `ShowMinimap` keeps the map reachable; the viewport drag freezes the
  pointerdown-time bounds/scale (render + client→svg mapping), captures the pointer
  (`tmDiagramMinimap.capturePointer`) instead of ending on pointerleave, and clamps to the
  frozen bounds; hidden layers' nodes/edges are excluded from the map and edge waypoints /
  free ends / label anchors are included in the framed region; the collapse button moved into
  a header strip (small title + button) that never covers the map, with state-specific
  labels (`TmDiagramEditor_HideMinimap` / `ShowMinimap`), `aria-expanded` +
  `aria-controls` on both collapse and reopen, and focus management (collapse → reopen button
  on tablet / "⋯" trigger on mobile; reopen → collapse button). The mobile "⋯" menu closes
  after Search / Minimap and refocuses its trigger; the action bar's first tile mirrors the
  tool mode (Pan tile while panning, tapping returns to Select; Pan stays in "More" while
  Select is active); the Shapes / Properties tiles are checked while the sheet is open on
  their panel and tapping the active tile closes the sheet; the floating zoom pill reads
  − | % | + | fit; the search panel's options row (all-pages + regex) wraps as one unit, the
  regex toggle carries the `TmDiagram_UseRegex` accessible name and the checkbox is finger-sized
  on a coarse pointer; the sticky Delete in the long-press actions sheet never lets items show
  through below it (the drawer body's bottom padding gives way, the danger item carries the
  safe-area inset); a selection with the sheet already open on the Layers tab keeps the tab and
  snap (it switches to Properties only from a closed sheet or the Shapes tab); the shell
  opening the Shapes side on mobile also opens the inspector, so all three sheet tabs exist;
  when the shell's own panels toggle is hidden, the sheet's close hands focus to the action
  bar (`tmDiagramEditor.focusFirstAction`). `TmEditorShell.ExtraOpen` now also requires the
  resolved Mobile layout (the Extra tab is mobile-only); the closed-state toggle label names
  the Extra panel too; `revealRectAboveSheet` waits for the sheet panel's snap animations
  (`getAnimations().finished`, or computes the band top from the stage bottom − panel
  offsetHeight), refuses to pan during a drag/pan/rubber-band/pinch and resolves false on a
  ~500 ms timeout instead of hanging. The mobile "⋯" groups, the zoom pill and the minimap
  overlay markup moved to `TmDiagramEditor.MobileChrome.razor.cs` (`.razor` length budget).

- **DiagramEditor: public `DiagramImportException` / `DiagramImportError` for CSV/SQL import
  failures.** `CsvParser`, the CSV generators and `SqlParser` throw the typed
  `DiagramImportException` (deriving from `InvalidOperationException`, so existing catch blocks
  keep working); the `DiagramImportError` enum pins explicit values (`Unknown = 0`, then
  `CsvEmpty`, `CsvNoData`, `CsvMinColumns`, `MappingMissing`, `ColumnNotFound`,
  `InputTooComplex`) and the dialogs map it to localized texts — `Exception.Message` stays an
  English developer sentence and is never shown to end users. `SqlParser.Parse` enforces an
  overall ~1 s deadline (Stopwatch, checked between matches and column lines; internal
  overload takes a `TimeSpan` budget for tests) and a 512 KB input cap, `CsvParser.Parse`
  shares the 512 KB cap.

- **DiagramEditor: template thumbnail generation API.** The public `DiagramThumbnailOptions`
  type (`Tempo.Blazor.Abstractions`, init-only setters, `Width`/`Height` defaulting to
  300×200) and the `DiagramThumbnailSvgGenerator.Generate(document, options)` overload let
  hosts render fitted 16:10 SVG thumbnails of any `DiagramDocument`; `Generate` validates the
  viewport (`ArgumentOutOfRangeException` for non-positive `Width`/`Height`).

- **DiagramEditor review fixes (phase 4 follow-up).** Connect arrows are finger-reachable on a
  coarse pointer: the decorator pane renders per node, in paint order, the visible
  resize/rotate handles (`g.tm-diagram-node-handles`), then the finger-sized port hit discs
  (`g.tm-diagram-port-hits`), then the quick-connect arrow buttons in their own group
  (`g.tm-diagram-connect-arrows`) — the rule is **buttons > gesture hit areas > visual
  handles**. The arrows get a 44 px hit area on coarse pointers (`--tm-diagram-connect-hit`,
  a `::before` inset) and the wrap offsets outward by `handle-hit/2 + gap` (the N arrow stays
  centred horizontally and shifts straight up by the offset plus the rotate-hit size, so its
  press area clears the rotate handle's hit circle); the JS transform sync keeps every
  `[data-node-id]` group glued to the node during drags and hides the port hits and arrows
  while a resize runs. The SQL/CSV import dialogs separate the debounced preview error
  (polite `role=status` region, textarea `aria-describedby` + `aria-invalid`) from the
  import-click failure (`TmAlert role=alert` only after Import); the CSV wording for
  no-data/min-columns waits until the pasted text has two lines, a vanished mapped column is
  cleared with a polite explanation (mappings survive a transient failed parse), and empty
  mappings auto-map to headers matching the semantic field name or localized label
  (fold-compare). The connect dialog owns its placeholder key, offers a "Search all shapes"
  escape hatch outside the live region when the narrowed palette has no match, moves focus
  into the grid on ArrowDown from search (`FocusAsync(preventScroll: true)` + scrollIntoView
  `nearest`), clears its tile references when hidden, takes the column count from a single
  source (`--tm-connect-cols`) and opens in the editor's resolved layout (no grid→list flash
  on mobile). Template thumbnails draw no labels on a transparent 16:10 viewport fitted to
  the node bounds (`xMidYMid meet`), return no thumbnail for node-less documents (the card
  falls back to the category icon), respect an explicit `thumbnailUrl` from `index.json` and
  load through `Lazy<Task>` caches; the built-in templates resolve by exact embedded resource
  name (csproj `LogicalName`) and `DiagramTemplateRegistry.InitializeAsync` is thread-safe
  (parallel callers await one load); a faulted cached load resets under its lock so the next
  call retries instead of replaying the exception forever (the same for each provider's
  `Lazy<Task>` catalog cache). `TmDiagramEditor` recents adopt host values only when
  the host reference changes (a stale re-render can no longer clobber a just-added recent),
  and the preview debounce is shared through an internal `DiagramPreviewDebouncer`. New keys:
  `TmDiagramConnectDialog_SearchPlaceholder`, `TmDiagramConnectDialog_SearchAllShapes`,
  `TmDiagramCsvImportDialog_ErrorTooLarge` (en/cs/sk/fr).

- **DiagramEditor tablet: finger edge-drawing discoverability (phase 3 review fixes).** The
  selected node's ports render invisible finger-sized hit circles (`.tm-diagram-port-hit`, same
  `data-port-id`, sized by `--tm-diagram-handle-hit`) AFTER the transform-handles group, so the
  port drag claims the press where they paint; the resize/rotate sessions veto through
  `engine.touchPortAt` (memoized per event), so only one session claims a pointer. The hit
  circles are pointer-active only on a coarse pointer — on desktop they stay inert (no shadow
  over the mid-edge resize handles or the small visible ports). The occlusion rule is explicit:
  a port only wins when the raw press target is the port/hit circle or an edge corridor over it —
  a port buried under another control is covered. Connection points get no hit circle: a
  stencil's connection points sit exactly on the resize handles (the default rectangle set is
  4 corners + 4 midpoints + centre) — they stay reachable via the visible circle or an edge
  corridor only. On a coarse pointer the n/e/s/w mid-edge resize hits do not render
  (`data-has-ports` scope: only where the node renders port hits) and only the selected node's
  ports show — no sticky hover (a `(hover: none)` query, so a hybrid laptop keeps its hover). Mobile (`ShowConnectArrows="false"`) renders no ports, connection points or hit circles
  at all: no invisible dead zone on the touch surface.

- **CI: `js-node-tests.yml` runs the Node lanes (`test:overlay`, `test:diagram-editor`,
  `test:reporting-modules`, `test:document-editor`) on Node 24 for every push and pull request.**

- **DiagramEditor tablet: drag a finger or a pen from a port or connection point to draw an edge
  (same commit path as the mouse, `_commitEdgeDraw`).** Mobile keeps the connect arrow / Connect
  dialog.

- **DiagramEditor redesign — plan summary (phases 1–7).** `TmDiagramEditor` was rebuilt on the
  core responsive primitives (`TmEditorShell`, `TmToolbar Overflow=Menu`, `TmOverlayPanel`,
  `TmModal.MobilePresentation`, `TmMobileActionBar`, `pointer-drag.js`/`pointer-gesture.js`)
  following the reference mockups: an explicit toolbar contract (`ToolbarPreset` /
  `ToolbarGroups` / frozen `data-tm-group` values, phase 1), wave-0 bug/localization/a11y fixes
  (phase 2), a tokenized visual layer aligned with the mockups (phase 3), the desktop restructure
  onto header + one command row + status bar + tabbed inspector (phase 4, DI2/DI3), dialogs on
  `TmModal` with the narrowed `TmDiagramConnectDialog` connect picker (phase 5, DI4), tablet with
  touch gestures and the floating minimap (phase 6) and mobile viewing + light edits below 768px
  (phase 7, G2 — resize/rotate, edges, layout and import stay tablet+). Host actions render
  through the `HeaderStart` / `HeaderActions` slots (G9). The toolbar contract, `InspectorLayout`,
  `MinimapPlacement` and the responsive scope are documented in
  [docs/diagram-editor.md](docs/diagram-editor.md); every breaking DOM/CSS change is listed under
  Breaking / Migration below.

- **`TmDiagramEditor` mobile viewing and light edits (DiagramEditor redesign phase 7, G2).** Below
  the 768px container band the editor renders the shell's mobile presentation: a non-modal bottom
  sheet (peek/half/full snap points) holding the Shapes palette and the Properties | Layers
  inspector, the core F5 action bar (Select | Pan | Properties tiles with an export-only "More"
  overflow), a floating zoom pill over the canvas corner, the floating minimap, and the long-press
  context menu as a shared F3 bottom sheet. Light edits on mobile: selection, text and style in
  the properties, layer visibility/lock/active layer and "move to layer", delete, undo/redo and
  tap-insert from the palette; resize/rotate, edge drawing, align/distribute, layout and import
  stay tablet+. New `TmDiagramCanvas` parameters `ShowTransformHandles` and `ShowConnectArrows`
  (both default `true`) let any host drop the reshaping or edge-drawing affordances where the form
  factor forbids them. A selection opens the sheet peeked on the properties panel; a host-made
  viewer — pinned `ReadOnly="true"` or a toolbar group set without editing groups (e.g. the
  `Viewer` preset) — renders no action bar or panels sheet; the user's own Read-only toggle keeps
  them (read-only panels).
  Localization: `TmDiagramEditor_ShapesTab`, `TmDiagramEditor_ActionsSheetTitle` and
  `TmDiagramEditor_ZoomControls` (en/cs/sk/fr).

- **`TmDiagramConnectDialog` (DiagramEditor redesign phase 5, DI4).** The connect picker is its
  own component: the source node's stencil set as the default palette, search over the registry,
  "Show all" opt-in, recently-used-first ordering, roving arrow-key tile grid (no preselection),
  localized stencil names, a mobile bottom sheet with plain rows and a Close footer button, and
  focus return to the canvas. `TmDiagramEditor` hosts it and passes the source stencil plus the
  canvas restore target. Dialogs of the editor now carry the foundation presentation contract:
  `FooterLayout=Inline`, `TmModal.Subtitle` (CSV/SQL), `XLarge` template gallery with search and
  category chips in one row, double-click/Enter create on template cards, two-column CSV mapping
  rows with a sticky-header pinned-first-column preview, the SQL summary box with icon stat chips
  and localized built-in template/category names via `TmDiagramTemplate_*` /
  `TmDiagramTemplateCategory_*` keys (en/cs/sk/fr).

- **`TmDiagramEditor` desktop restructure (DiagramEditor redesign phase 4, DI2/DI3).** The editor
  renders through the core `TmEditorShell`: a header (page tabs on top per DI3, document title,
  named panel toggles with visible labels, "⋯" view-settings menu), a single command row built on
  the core `TmToolbar` (`Overflow=Menu`, `Priority`-driven overflow, visible labels below icons,
  labelled `TmToolbarGroup`s carrying the frozen `data-tm-group` contract), a status bar with the
  zoom controls and the Read-only toggle, a collapsible minimap card, and an inspector presenting
  "Properties | Layers" tabs (new `InspectorLayout` parameter, `Stacked` opt-in) with a dedicated
  Actions section. Docked panels resize/collapse through the shell. New parameters:
  `InspectorLayout`, `ShowStatusBar`, `ShowTitle`.

- **`TmDiagramEditor` explicit toolbar contract (DiagramEditor redesign phase 1).** Every toolbar
  group now carries a stable `data-tm-group` attribute from the frozen set `history | clipboard |
  tools | format | insert | arrange | search | panels | zoom | view | canvas-size | layout | export
  | templates | import | readonly`, and visibility is decided in C# through the new `ToolbarPreset`
  (`DiagramToolbarPreset.Full` default — today's look; `Viewer` renders only zoom, view, search and
  export) and `ToolbarGroups` (flags, `null` = by preset) parameters. `HeaderStart`/`HeaderActions`
  render slots open the first/last toolbar row to host actions. The contract is public API: hosts
  (Modeling) key off `data-tm-group`/presets instead of positional CSS selectors on the toolbar's
  internal DOM. `IsReadOnly` exposes the effective read-only state (see Breaking / Migration).
  The `Viewer` preset still renders the document-title input (disabled while read-only — the
  document name is a header element, not a toolbar group, so it intentionally stays outside the
  group contract), while the search control received the standard toolbar-group chrome. The
  Modeling preview hides it with `ShowTitle="false"`.

- **Opt-in table/list redesign controls.** Added `ColumnMenuMode`, `EditActionsMode`, `GroupHeaderTemplate`, `AllowExportSelected` (it filters the exported rows to the checked selection; see Breaking / Migration), `ShowActiveViewName`, `AllowSetDefault`, `OnSetDefault`, `ShowResultPreview`, `TableCellLayout`, `StatusVariantField`, and `CardActivation`. Defaults preserve existing presentation.

- **Phase 7 responsive overlays and filter summary.** The `TmDataTable` export dropdown uses automatic mobile presentation when `MobileLayout` is enabled and stays a popover when it is `None`; `TmMultiViewList` grouping dropdown uses automatic presentation only when `Responsive` is enabled. `ShowFilterSummary` adds an opt-in localized active-filter count and clear control (default false).

- **Mobile data layouts.** `TmDataTable.MobileLayout` opts into `Scroll` or `Cards` while `None` preserves the existing table. `TmDataTableColumn.MobilePinned` pins a mobile scroll column; `CardRole` controls card presentation. `TmMultiViewList.Responsive` enables its responsive table behavior. `CardMode` remains an alias and now also emits the cards class when `MobileLayout` is unset. `MobileLayout=Cards` adds a sort dropdown and a filter sheet to the toolbar (`tm-data-table-mobile-only`, hidden until the table's own container is below 640px): the sort lists each sortable column ascending and descending with the active choice checked, and the filter sheet labels every filterable column. `TmMultiViewList` grows the same toolbar (`tm-mvl-cards-toolbar`) for its card and list views when `Responsive` is set.

- **`TmMultiViewList.ViewSwitcherLabels`.** Default `IconsOnly` keeps today's icon buttons.
  `IconsAndText` adds the localized Table, Card and List names. The radiogroup contract is unchanged.

- **`TmDataTable.Appearance` and `TmFilterBuilder.Presentation`.** Both default to today's
  look: `Plain` adds no card class, `Inline` keeps the filter editor in the flow (PickersPage
  included). Opt in with `Card` or `Popover`. `Appearance=Card` is the opt-in for the redesigned
  export item (icon, short label and format description), the "Add grouping" dropdown, the group
  bar prefix and the no-pager summary `TmDataTable_TotalItems` (`{0} record` / `{0} records`).
  The default no-pager summary stays `TmDataTable_ShowingItems`. Keep a custom line with
  `PaginationInfoTemplate`.

- **`TmDataTable.KeyboardNavigation`.** Opt-in `Grid` uses roving tabindex: one cell is
  tabbable, arrows / Home / End / PageUp / PageDown move it, Enter activates the row once.
  Default stays `Rows` (today's Tab-per-row). **Upcoming / Migration:** `Grid` becomes the
  default in the next major. Keep today's behavior with
  `KeyboardNavigation="DataTableKeyboardNavigation.Rows"`. `TmMultiViewList` stays on row
  navigation and has no grid parameter.

- **`TmDashboard` documentation.** The redesigned dashboard (and widget selector) is now
  documented in [docs/dashboard.md](docs/dashboard.md): the chrome parameters
  (`Appearance`, `Title`/`Subtitle`/`HeaderStart`/`HeaderActions`/`ShowHeader`,
  `ShowWidgetIcons`, `ShowWidgetDescriptions`, `WidgetHeaderActions`), `CloseOnSelect`,
  the keyboard alternative and non-drag reordering, and the `dashboard.js` /
  `pointer-drag.js` script requirements.

- **`TmDashboard` post-review hardening and UX polish (the redesign plan's last two remaining
  tasks).**

  - Widget selector chips (mobile) are native `aria-pressed` toggle buttons — the same contract
    as the desktop category sidebar — instead of `role=radio` buttons without the arrow-key
    roving tabindex a radiogroup demands (hardening).
  - `dashboard.js` `scrollWidgetIntoView` escapes the instance id through `CSS.escape` before it
    joins the attribute selector (with the `tm-focus-trap.js` stub-DOM fallback), and documents
    why the drop preview stays a JS-appended node: a Blazor-owned preview would need a JS→.NET
    round-trip per pointermove and would re-render under an active pointer (hardening).
  - Leaving edit mode via "Save changes" or "Cancel" now resets the keyboard grab state, so a
    grab held during the exit can no longer paint the grabbed style in view mode (hardening).
  - Adding a widget whose definition is wider than the grid (host typo, wider saved
    `GridConfig`) clamps the instance to the grid's column count in
    `FindNonOverlappingPosition` — the same clamp the resize and the drop commit already apply;
    the clamp also covers the empty-dashboard early return (hardening).
  - Widget titles clamp at TWO lines before the ellipsis: a 3-column KPI card at 1440 px
    squeezed "Revenue KPI" to "Revenue …" although the header had room for a second line (UX).
  - Chart axis/value/label text inside widget cards repaints at 20 viewBox units (≈ 10 px in
    typical slots) — the fixed 600×400 chart viewBox scaled the 11-unit ticks to ≈ 6 px, which
    was illegible. The override is scoped to `.tm-widget` and tunable through
    `--tm-widget-chart-font-size`; standalone charts keep their tuned size (UX).
  - The dashboard create/rename dialogs' disabled primary button (Create/Apply) swaps the global
    0.5 opacity veil (white label at ≈ 1.4:1) for a legible muted state: soft primary fill with
    the `--tm-color-primary-text` ink (UX).
  - The widget selector's mobile sheet stacks its footer — hint above, full-width Done below —
    so the hint no longer crowds the button (UX).
  - Verified the indigo variant reaches the dashboard accents: the theme-matrix E2E now pins the
    themed primary on the dashboard's Edit button in all four light/dark × default/indigo
    combinations. (The residual dark-indigo ≈ dark-default impression is by design: dark paints
    the pale `-400` primary step in both themes, and the categorical tile/chart colours are
    theme-independent on purpose.)
  - The f6 tablet captures are named for their real 900×768 viewport (`tablet-900-*.png`, was
    `tablet-1024-*.png` — 1024 itself sits on desktop), and the selector E2E waits out the
    overlay's 0.3 s fade before shooting: a mid-fade screenshot read as "the desktop dialog
    lacks the tablet/mobile scrim", while the scrim and blur are identical at every width (now
    asserted in the desktop test).

- **`TmDashboard` short labels on the mobile edit-mode action bar (Y11 pattern).** The bar
  tiles carry `TmDashboard_AddWidgetShort` / `TmDashboard_RenameShort` ("Add" / "Rename",
  "Přidat" / "Přejmenovat", "Pridať" / "Premenovať", "Ajouter" / "Renommer") — at 320 px the
  full "Ajouter un widget" / "Renommer le tableau de bord" labels needed three lines and were
  cut by the two-line clamp; the desktop toolbar keeps the full wording. New localization keys
  in all four resource tables plus `MockTmLocalizer`.

- **`TmDashboard` F7b — the redesign plan's remaining tasks resolved.**

  - Loading state (F2 UX §5.7): while the provider's first load is in flight the grid renders
    two card-shaped `TmSkeleton` placeholders (`.tm-dashboard-skeleton-card`, 4 default rows
    tall) with `aria-busy="true"` on the grid container and a localized sr-only "Loading"
    status — the empty state (which would falsely claim there are no widgets) renders only
    after the load answered.
  - Error state (F2 UX §5.8): `IDashboardProvider` failures on the load path (dashboard list,
    default dashboard, switcher selection) and the save path no longer crash the host. A soft
    danger `TmAlert` renders above the grid (`tm-dashboard-error`) with a localized message and
    a "Try again" action that re-runs the failed operation; a failed save keeps edit mode open.
    New localization keys `TmDashboard_Loading`, `TmDashboard_LoadError`, `TmDashboard_SaveError`
    and `TmDashboard_Retry` in all four resource tables plus `MockTmLocalizer`.
  - Mobile rename: the compact edit mode's action bar gains a Rename entry (Add, Save, Rename
    on the bar, Cancel overflows into the More sheet as before) that opens a rename dialog —
    the desktop toolbar hosting the inline editor is hidden on a mobile layout, so rename was
    unreachable there. The dialog's form is the Enter-to-apply contract, Escape/backdrop cancel.
  - New token `--tm-dashboard-min-height: 37.5rem` (`tokens.css`): the G-6 follow-up — the
    dashboard root's `min-height: 600px` literal is now `var(--tm-dashboard-min-height)` so
    hosts can raise or drop the workspace floor.
  - Category tile ink (F2 UX §5.11): the decorative widget icon tiles (dashboard and widget
    selector) now paint their icon in
    `color-mix(in srgb, var(--category-color) 75%, var(--tm-text-primary))`, so amber on its
    12% tint stays readable in light mode (was ≈ 2.1:1) without changing the tile hue.
  - Narrow-card widget header: an `@container tm-dashboard (width < 768px)` rule wraps
    `.tm-widget-header-left` and drops `.tm-widget-move-group` onto its own row in compact edit
    mode so the title regains the full width at 390px cards (markup unchanged).
  - The CSS bundle was regenerated. DOM/tab order of the widgets already followed the visual
    (Y, X) order since F3 (`WidgetLayout`), closing the last F2 coordination note.

- **`TmDashboard` Fáze 7 — dialogs and the dashboard switcher on mobile (dashboard redesign
  plan, phase 7), riding the F2/F3 foundation primitives.**

  - The dashboard switcher menu (a `TmOverlayPanel`, the `TmDropdown` pattern) switched from
    `MobilePresentation=Popover` to `MobilePresentation=Auto`: it stays the anchored popover on
    desktop/tablet and presents as a bottom sheet titled with the localized "Dashboards" on a
    phone. Focus returns to the switcher trigger on every close path (Done, Escape, backdrop).
  - The delete-dashboard confirmation passes `TmDialog.Icon="trash-2"` (the destructive glyph)
    and keeps `MobilePresentation=Auto` + `FooterLayout=Stacked`, so on the mobile sheet Delete
    paints above Cancel (column-reverse) with ≥ 44 px targets.
  - The switcher menu's default-dashboard row now explains WHY it cannot be deleted: a muted
    full-width hint line (`span.tm-dashboard-menu-default-hint`, localized) under the "Default"
    label instead of only the star marker. The hint is the row's LAST child (outside the inline
    actions span, with `flex-wrap: wrap` on the row), so it wraps under the name instead of
    squeezing it to zero width, and the select `menuitem` references it via `aria-describedby`
    so screen readers moving through `role=menu` hear it. The 48px coarse-pointer footer
    target uses `var(--tm-space-12)` instead of a raw `3rem`.
  - New localization keys `TmDashboard_NameExample` ("e.g. Team overview" / "např. Týmový
    přehled" / "napr. Tímový prehľad" / "p. ex. Vue d'équipe") and `TmDashboard_DefaultCannotDelete`
    in all four resource tables plus `MockTmLocalizer`.
  - The create dialog's name placeholder is now a concrete example (`TmDashboard_NameExample`)
    instead of a repeat of the field label; its Cancel button is the outlined secondary variant
    (foundation footer convention: Cancel secondary, Create primary; the sheet splits the inline
    footer 50/50 with ≥ 48 px targets). The rename inline edit keeps the original
    `TmDashboard_NamePlaceholder` key.
  - New CSS class `tm-dashboard-menu-default-hint` in `_dashboard.css` (tokens only); the CSS
    bundle was regenerated. The scoped 48px footer rule registers the deliberate
    `.tm-btn _dashboard.css|_modal.css` pair in `UnconstrainedClassOwnershipTests`.

- **`TmDashboard` F2 chrome redesign — the dashboard is redesigned around the reference
  `dashboard--desktop.png`, and every part of that redesign is parameterized** (dashboard
  redesign plan, phase F2). New parameters, all additive:

  - `Appearance` (`DashboardAppearance.Plain | Card`, default `Plain`) — the chrome of the root
    surface (see Breaking / Migration below; `Card` restores the pre-2.9.1 wrapping surface card
    via the new `tm-dashboard--framed` class).
  - `Title` — heading text overriding the current dashboard's name in view and edit mode.
    Without a `Title` (and with an `IDashboardProvider`) the view-mode heading is visually
    hidden (`tm-sr-only`), because the switcher trigger now shows the dashboard's name; the `h2`
    stays in the DOM for screen readers and existing `.tm-dashboard-title` selectors.
  - `Subtitle` — secondary line under the heading in view mode.
  - `HeaderStart` — host content at the start of the header (breadcrumbs, back link).
  - `HeaderActions` — host actions at the end of the view-mode header, rendered BEFORE the
    Edit button so DOM order = tab order; not rendered in edit mode.
  - `ShowHeader` (default `true`) — hides the whole toolbar in view mode; edit mode always
    renders the toolbar (it carries Save/Cancel).
  - `ShowWidgetIcons` (default `true`) — the widget header renders a 40px category icon tile
    from `WidgetDefinition.Icon`, tinted with the category colour over the surface through
    `color-mix` and the new global `--tm-category-tint` token (light 12%, dark 20%).
  - `ShowWidgetDescriptions` (default `false`) — renders `WidgetDefinition.Description` as a
    secondary line under the widget title.
  - `WidgetHeaderActions` (`RenderFragment<WidgetInstance>?`) — a host slot in every widget
    header (badge, "view all" link, period select), rendered before the collapse chevron in
    view mode only; the host owns its content's accessible names.

- **`WidgetDefinition.IsCollapsible` (default `true`)** in `Tempo.Blazor.Abstractions` — an
  additive model property. `TmDashboard`'s collapse chevron now respects it: a widget registered
  with `IsCollapsible = false` renders no collapse control.

- New localization keys `TmDashboard_Default` ("Default" / "Výchozí" / "Predvolená" / "Par
  défaut") and `TmDashboard_SwitcherAriaLabel` ("Dashboards: {0}" / "Nástěnky: {0}" /
  "Nástenky: {0}" / "Tableaux de bord : {0}") in all four resource tables plus `MockTmLocalizer`.

- The dashboard switcher menu's default-dashboard marker is now a visible text label
  (`span.tm-dashboard-menu-default`, star icon + localized "Default") instead of a bare starred
  icon announced through `role="img"`; the menu aligns to the START of the trigger and spans at
  least its width (`MatchAnchorWidth`).

- Entering edit mode moves focus to the "Add widget" button (previously focus fell to `<body>`
  when the Edit trigger left the DOM); on a mobile layout the move is skipped silently because
  the actions live in the mobile action bar.

- **`TmDashboard` F4 — reordering without drag: compact edit mode and a keyboard alternative for
  move and resize (dashboard redesign plan, phase F4; WCAG 2.1.1 / 2.5.7).**

  - **Compact edit mode** (`tm-dashboard--compact-edit`, tablet portrait and mobile): the drag
    handle and the resize handles do not render; each widget card grows **Move up / Move down**
    icon buttons (`tm-widget-move-btn--up` / `--down`, ≥ 44 px targets, named for the widget they
    act on through `aria-describedby` pointing at the card title) next to the existing Remove
    button. Both moves swap the card's `(X, Y)` with its previous/next neighbour in the linear
    `(Y, X)` order (DA2) and push overlaps when `GridConfig.AllowOverlap` is false — the
    `WidgetInstance` model objects are reused, only their coordinates change. A freshly added
    widget is briefly highlighted (`tm-widget--highlight`, guarded under
    `prefers-reduced-motion`) and scrolled into view — on a 390 px layout the new card can land
    below the fold, so `dashboard.js` gained a `tempoDashboard.scrollWidgetIntoView` helper the
    component calls after the render that added the widget.
  - **Keyboard grab mode (desktop)**: the drag handle (`span.tm-widget-drag-handle`) is now a
    real `<button aria-pressed>` — Tab-reachable, with `aria-describedby` pointing at a shared
    sr-only instruction line. Enter/Space (native activation) grab and drop; the arrow keys move
    the grabbed widget one cell (clamped to the grid); Shift+arrow resizes one step, clamped to
    `WidgetDefinition.MinWidth/MaxWidth/MinHeight/MaxHeight` (0 = unlimited; the grid width is
    the width ceiling); Escape cancels and restores the pre-grab geometry. The entire move and
    resize logic lives in C# — `dashboard.js` still supplies only raw mouse drag/resize cells.
  - **Live announcements**: every grab, cell move, resize step, drop and cancel is announced
    through a polite live region (`tm-dashboard-live-region`), e.g. "Widget: column 3, row 2,
    4 × 3". New localization keys `TmDashboard_MoveUp`, `TmDashboard_MoveDown`,
    `TmDashboard_DragHandleInstructions`, `TmDashboard_PositionAnnouncement`,
    `TmDashboard_Grabbed` and `TmDashboard_MoveCancelled` in all four resource tables plus
    `MockTmLocalizer`.

- **`TmWidgetSelector` F6 redesign — desktop, tablet and the mobile sheet (dashboard redesign
  plan, phase F6), following `widget-selector--{desktop,tablet,mobile}.png`.**

  - New parameter **`CloseOnSelect` (default `true`)** on `TmWidgetSelector` and pass-through on
    `TmDashboard` (DA1). With `false` the selector stays open: the added card flips to a green
    "✓ Přidáno" badge, a `AllowMultiple` widget grows an added counter
    (`TmWidgetSelector_AddedCount`, "Added ({0})"), a polite live region announces the addition,
    and the dashboard still highlights/scrolls to the newest widget once the selector closes.
  - New parameter **`LayoutMode` (`TmLayoutMode`, default `Auto`)** on `TmWidgetSelector` — a
    forced value pins the presentation (including the mobile chip row and the bottom sheet) for
    tests and embedded hosts.
  - Mobile: the category sidebar is replaced by a horizontally scrollable **chip radio group**
    (`role="radiogroup"`, chips with counts, `TmWidgetSelector_Categories` as the group label);
    cards stack in one column with a ≥ 44 px plus in the right corner; the selector presents as
    the F2 bottom sheet with a sticky footer and safe-area padding.
  - Tablet keeps the sidebar and goes to two card columns. All selector breakpoints follow the
    resolved `data-layout` (layout/container), never a viewport media query.
  - Visual refresh: 56 px pastel category tiles (shared `DashboardCategoryStyles` `color-mix`
    tint, contrast-safe in dark mode), `--tm-radius-card` card corners, plain-text category
    counts, spaced size label ("4 × 3", no monospace), permanently visible plus indicator
    (40–44 px under `pointer: coarse`), primary "Hotovo" footer button.
  - New localization keys `TmWidgetSelector_AddedCount`, `TmWidgetSelector_AddWidgetAria`
    ("Přidat {0}") and `TmWidgetSelector_Categories` in all four resource tables plus
    `MockTmLocalizer`.

- **Core: `TmContextMenu.FocusTriggerAsync()`.** The core context menu now exposes a public
  `ValueTask FocusTriggerAsync()` that moves focus to the menu trigger (`preventScroll`) and treats
  a trigger that already left the DOM as a no-op instead of throwing. Hosts call it after an action
  replaced or removed the row the trigger lived on — the diagram layers panel uses it to focus the
  moved row's "⋯" trigger after a menu-driven reorder and the neighbour's trigger after a delete,
  paths where the built-in close-focus-restore cannot run.

- **Core tokens: `--tm-data-table-cell-py` / `--tm-data-table-cell-px` are defined in
  `tokens.css`.** `_data-table.css` already read them with `var(--tm-space-3)` /
  `var(--tm-space-4)` fallbacks; the tokens are now declared with exactly those values, so
  nothing renders differently, but hosts can tune `TmDataTable` cell density by overriding the
  two tokens on `:root` (or on a wrapper). The strict CSS token audit no longer counts them as
  undefined.

- **TmEditorShell: optional `Extra` / `ExtraTitle` slot** — a third tab of the mobile panels
  sheet after `Right` (opens and closes with the right panel, selected through
  `ActiveMobilePanel = EditorShellPanel.Extra`; Sheet presentation only, ignored on
  desktop/tablet and by the Tabs presentation). New enum value `EditorShellPanel.Extra`.
  Backward compatible: without `Extra` the sheet is unchanged. `TmDiagramEditor` uses it for the
  flat mobile Shapes | Properties | Layers tabs (X2).

- **TmDiagramPropertiesPanel / TmDiagramLayersPanel / TmDiagramToolbox: new `ShowTitle`
  parameter (default `true`).** The editor passes `false` where a tab or sheet tab already names
  the panel (no duplicate heading inside the mobile sheet); standalone hosts are unaffected (U4).

- **TmDiagramEditor: new `MobileSheetInitiallyOpen` parameter (default `false`)** — the opt-out
  that restores the pre-2.9.1 mobile open-on-load sheet (see Breaking / Migration).

- **`TmDropdown.OutlineTrigger`.** Renders the trigger as a small outlined button
  (`tm-btn tm-btn-outline tm-btn-sm`) instead of the default stealth trigger — the data-table
  toolbar's export dropdown uses it so the trigger reads as a button inside the row. The small
  size applies to every `OutlineTrigger` consumer, not only Card mode. See Breaking / Migration.

- **`TmSigningFormRunner` autosave debounce and `TmModelingEditor` loading delays run on an
  injectable `TimeProvider`.** `AddTempoBlazorSigning` / `AddTempoBlazorModeling` register
  `TimeProvider.System` with `TryAddSingleton` (a host that pinned its own clock keeps it), so a
  consumer or test drives the delays through a `FakeTimeProvider` instead of sleeping; without a
  registration both components fall back to `TimeProvider.System`.

- **`TmActivityComments` can now report its draft state to the host page — the hook behind
  dirty-guard dialogs.** A host that must ask "discard your changes?" before a refresh/retry
  could not see a half-written comment: the editor state is private and the existing events fire
  only after the comment is submitted. Four consumer-facing additive members close the gap (the capability
  was driven by a concrete consumer — an outage-management consumer page, whose guard needs
  to see the draft before reloading):
  the new `TmCommentDraftState` record in `Tempo.Blazor.Abstractions` (`IsEditorOpen`,
  `EditingCommentId`, `HasUnsavedContent` — flags only, never the raw draft text), the new
  `[Parameter] EventCallback<TmCommentDraftState> OnDraftChanged`, which fires ONLY when the state
  value actually changes (value-equality dedup — never once per keystroke, so wiring the callback
  does not re-render the parent while the user types), the new `public bool HasUnsavedDraft`
  property for polling the current state at any time (e.g. from a refresh/retry guard), and the
  new `public Task DiscardDraftAsync()` for the consumer path after the user confirmed the
  discard in the host's OWN dialog — it closes the open editor (new comment or inline edit),
  clears its content and is a deliberate no-op when nothing is open. Semantics: a new comment
  counts as unsaved once its content is not effectively empty; an edit of an existing comment
  counts as unsaved only once its content differs from the comment's original body — opening an
  edit and leaving it untouched is not a draft. Consumers compiling against the previous version
  behave identically without wiring `OnDraftChanged`; the only behaviour adjustments are the three
  fixes recorded under Fixed above (starting an edit closes the new-comment editor; the Add button
  is hidden while an edit is open; effectively-empty markup is not submitted as a new comment).
  Test seam: new `SetEditorContentForTestAsync(string)` (awaits the draft notification); the
  existing void `SetEditorContentForTest` keeps its signature and now also raises
  `OnDraftChanged` (fire-and-forget). No new user-facing strings were added.

- **`TmFocusScope.NoteOpenerOnMount`** (advanced, default `false`): set when the scope's `Active`
  flips true only after its content has mounted (e.g. behind a promote/gate), so the opener is
  noted before content autofocus — a surface whose gate releases `Active` after its content child
  has already autofocus-focussed a field then restores the focus to the trigger on close instead
  of to that dying field. The note is sent once per open and never suppresses the trap itself: a
  scope that is `Active` on the same first parameter set (an inline drawer, a plain scope)
  activates as usual. The library's gated owners (`TmModal`, `TmDialog`, `TmDrawer`,
  `TmLightbox`, `TmCommandPalette`, `TmKeyboardShortcutsHelp`, `TmGanttImportDialog`) set this
  themselves; hosts rendering `TmFocusScope` behind their own mount-then-activate gate can opt in.

- **`TmSelect.PlaceholderSelectable`** (bool, default `false`): when `true` (and `Placeholder` is
  set), the placeholder renders as a selectable — not disabled — `<option value="">` mapped to
  `default(TValue)` (typically `null` for nullable types), giving users a one-click "clear the
  selection" affordance without adding a null entry to `Options` themselves. The default `false`
  keeps the placeholder disabled exactly as before. Two documented caveats: a selectable
  placeholder is a real empty choice, not a name for the control — it is no longer used as the
  `<select>` aria-label, so set `Label` to keep an accessible name; and with `Required="true"`
  picking it submits the empty value, which native validation counts as missing.

- **`TmWorkflowDesignerCanvas` works on touch, and every drag has a keyboard alternative.** The
  private document mouse listeners are gone; `workflow-designer.js` now runs three delegated
  `pointer-drag.js` sessions plus `pointer-gesture.js`: states drag with a finger immediately (a
  canvas owns its surface, no long-press — a tap still selects), the empty canvas pans with one
  finger, two fingers pinch-zoom around their centre (the first finger's drag hands over and rolls
  back the moment the second finger lands, committing nothing), ctrl+wheel zooms around the cursor,
  and a node dragged against the viewport edge auto-scrolls the page (`createAutoScroller`), with
  the node re-positioned under the stationary finger on every applied frame. Escape, pointercancel
  and a mid-drag unmount restore the start transform/viewBox VERBATIM and commit nothing. For the
  keyboard: states are `role="button"` tab stops with a roving tabindex inside the canvas group —
  Tab reaches them, the arrows move the focus, Enter selects (Space on keyup, exactly once),
  Delete removes the focused state, and **Alt+arrows move the node one grid step through the same
  commit path as the drag**, announced on a `role="status"` live region ("Draft moved to 60, 200").
  Shift+F10 (or the ContextMenu key) opens the context menu on a focused state — a right-click
  opens the same menu and cancels the gesture in flight — whose new
  **"Connect to…"** entry lists the other states — the non-drag alternative to dragging from a
  port — and the new overlay **+ / − zoom buttons** are the single-pointer alternative to the
  pinch (WCAG 2.5.7). The menu opens with the focus on its first item (the arrows/Home/End rove
  through it via `tm-menu-nav.js`), Escape closes it and returns the focus to the state, and the
  Connect-to switch keeps the focus inside the menu. Keyboard Delete removes the focused state and
  hands the roving focus to the neighbouring state (the zoom buttons when the last one goes). The
  port drop is hit-tested with `document.elementFromPoint`, so the
  transition lands on the state under the finger even though the pointer was captured by the port.
  Localization keys `TmWorkflowDesignerCanvas_EditProperties/_DeleteState/_DeleteTransition/
  _AddInitialState/_AddState/_AddFinalState/_ConnectTo/_StateLabel/_InitialState/
  _IntermediateState/_FinalState/_StateMoved/_StateDeleted/_ZoomIn/_ZoomOut` (en/cs/fr) replace
  the hardcoded menu strings.

- **`pointer-gesture.js` — the shared multi-pointer module: `attachPinch` (two-finger pinch/pan)
  and `createAutoScroller` (edge auto-scroll), with ctrl+wheel zoom.** Multi-pointer gestures now
  have the same single home as drags. `attachPinch` tracks the first two touch pointers by id
  (capture per finger, a third finger ignored), starts on the second finger, reports the scale
  relative to the gesture start (clamped to `minScale`/`maxScale`) with the finger centre and its
  pan, and commits exactly once when the count drops below two; `pointercancel`, `Escape`
  (document capture phase, `stopPropagation` — a lone finger leaves the key to the surrounding
  dialog) and `detach` roll back through `onCancel` and never commit. ctrl+wheel (trackpad pinch)
  zooms around the cursor with `scale = Math.exp(-deltaY / 100)` — a non-passive listener,
  `preventDefault` only while ctrl is held, one `onEnd` debounced 150 ms after the last tick.
  `createAutoScroller` is the spreadsheet-canvas edge auto-scroll made generic: within 36 px of a
  container edge it scrolls up to 28 px per frame, driven by `update(x, y)` from a drag's `onMove`
  and stopped from `onRelease`, with cumulative `scrolledX`/`scrolledY` and an `onScroll` hook that
  re-hit-tests the drop target. `pointer-drag.js` stays single-pointer: the second finger never
  breaks an active drag by itself — the host hands over by calling `drag.cancel('pinch')` from the
  pinch's `onStart`. A pinch must always ship a single-pointer alternative (zoom buttons,
  WCAG 2.5.7); see "Multi-pointer gestures" in `docs/touch-interaction.md`. Not breaking; no
  consumer yet — the workflow designer adopts it in its own plan.

- **`attachKeyboardMove` accepts `{ shift: true }` and reports `step.shift` for Alt+Shift+Arrow
  (the keyboard resize alternative, e.g. growing a Scheduler event).** Without the option
  Alt+Shift+Arrow stays ignored and unprevented; Ctrl/Meta chords stay ignored either way; the
  plain Alt+Arrow step reports `shift: false`. Not breaking.

- **`GanttHelper.GetTodayOffset(timelineStart, pixelPerDay, today)` overload** — pass the
  user-zone today so a custom today line lands where the host's zone says "now" is; the existing
  two-argument call keeps using the machine's `DateTime.Today`.

- **User time zone plumbing: `ITmUserTimeZone` (default since this release, see Breaking /
  Migration above).** The scoped `ITmUserTimeZone` (one browser read per circuit, shared by every
  consumer) and the private `TmUserTimeProvider` clock over it power relative time, the Scheduler's
  `ResolveTimeZone()` fallback and the Gantt's "today". Never replace the app-wide
  `TimeProvider`: Kestrel and cookie authentication are singletons that consume it, so a scoped
  `TimeProvider` fails the host under `ValidateScopes`. On WebAssembly `ITmUserTimeZone.Zone` is the
  runtime's local zone immediately (it already is the browser's zone; `InvariantTimezone` must stay
  false, the default). On Blazor Server the library reads
  `Intl.DateTimeFormat().resolvedOptions().timeZone` once per circuit after the first interactive
  render (new `tm-user-zone.js` module), resolves it with `TimeZoneInfo.FindSystemTimeZoneById` and
  falls back to UTC for an unknown id; during prerender the zone stays UTC/unresolved. The
  relative-time components (`TmTimeline`, `TmActivityTimeline`, `TmActivityComments`,
  `TmNotificationBell`) re-render once the zone arrives, and on Server those four trigger the
  browser probe from their first interactive render — a page with none of them on screen must call
  `ITmUserTimeZone.SetZone` to get a resolved zone. `SetZone` also lets a host profile setting win
  over the browser value. Nothing ticks the stamps — the host re-renders as
  before. The manual
  registration snippet in `docs/localization-plurals.md` (scoped formatter over your own lazy
  `TimeProvider`) stays valid for hosts that prefer to own the plumbing.

- **`TmEditorShell.MobileSheetSnapPoints` (C8).** The mobile panels sheet no longer hard-wires its
  half/full snap pair: a host passes the fractions of the shell's stage the sheet snaps to
  (ascending, `0–1`; `MobileSheetSnapIndex` indexes the list, 0 = the lowest). Invalid values are
  dropped and the list is sorted and deduplicated with the `TmDrawer` rules; an empty result falls
  back to the default `[0.5, 1.0]`. The lowest snap also drives the canvas reserve, so the covered
  fraction stays scrollable into view with custom snaps. On a short stage the 14rem floor
  (`--tm-editor-shell-sheet-min-block`, overridable) now applies to the whole sheet panel — header,
  tabs and body — capped at the stage height: at 390×520 the half snap used to leave ~40px of body.

- **Toolbar non-button items: `TmToolbarItem` (F14/C1).** A toolbar can now hold controls that are
  not buttons — a `TmSelect`, `TmColorPicker`, `TmToggle`, a custom dropdown — and they leave the
  bar exactly like a `TmToolbarButton`. The `TmToolbarItem` wrapper is the measured item
  (`data-tm-toolbar-item` + `data-rank` + `data-pin`, one item in `tm-toolbar.js` even with nested
  content): it collapses whole (out of flow, invisible, `inert`), so its width stays measurable and
  widening brings it back. In the "More" menu an item is offered as **plain menu entries — never a
  free template** (a select inside `role="menu"` is invalid ARIA): either one entry
  (`MenuLabel` + `OnMenuInvoke`, with optional `MenuIcon` and `MenuKeepOpen` — typically opening the
  full control in a dialog or sheet while the menu stays open under it), or a labelled group via
  `MenuItems` (the entries render as one `role="group"` section named by `MenuLabel`, checkable
  entries included, in the written order). `Priority` is the same overflow rank a button has;
  `OverflowOnly` renders only the hidden position marker and lives only in "More"; an item collapses
  only when it offers something in "More" — `MenuItems` with entries, or a non-empty `MenuLabel`
  bound to an `OnMenuInvoke` handler; without either it has no menu representation and never leaves
  the bar (it behaves as `Pinned`). `Disabled` disables the "More" entry/entries. A `TmToolbarButton` nested in
  an item never registers twice. Note for hosts: `MenuItems` is compared **structurally** when the
  toolbar decides whether to recompute — a fresh-but-equal list per render is not a change, so
  rebuild it whenever the rendered state changes (a reference comparison would loop the recompute).
  The entries need unique `Id`s (or unique labels — the label is the fallback key the menu focuses
  by).

  ```razor
  <TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Text tools">
      <TmToolbarItem MenuLabel="Font size" Priority="ToolbarButtonPriority.Secondary" MenuItems="@_sizes">
          <TmSelect TValue="int" @bind-Value="_fontSize" Options="@_sizeOptions" />
      </TmToolbarItem>
      <TmToolbarItem MenuLabel="Text colour" OnMenuInvoke="@OpenColourDialog" MenuKeepOpen="true">
          <TmColorPicker @bind-Value="_colour" />
      </TmToolbarItem>
  </TmToolbar>
  ```

  A text-entry control (a native `select`/`input`) inside an item stays **its own Tab stop** — the
  toolbar's arrow keys visit the item's first non-text-entry focusable instead. Internally the
  toolbar now ranks any `IToolbarOverflowItem` (`TmToolbarButton` unchanged in behaviour).

- **`TmToolbarButton.PreserveFocus` (F14/C4-a).** When `true`, pressing the button with a mouse, pen
  or finger does not move focus to it — the `pointerdown`/`mousedown` defaults are prevented — so an
  editor's text selection (and caret) survives a formatting command. Keyboard activation (Enter/
  Space) and the "More" menu entry are unchanged. This is the core contract the DocumentEditor and
  DiagramEditor toolbars need for their formatting commands; a host-splatted `aria-pressed` and all
  other button parameters compose with it. Default `false` (existing buttons keep focus, nothing to
  migrate).

- **Toolbar groups: `TmToolbarGroup`, and group headings in the "More" menu.** Buttons can now be
  grouped into a `TmToolbarGroup`:

  ```razor
  <TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Diagram tools">
      <TmToolbarGroup Label="Edit">
          <TmToolbarButton Icon="@IconNames.Undo" Text="Undo" />
          <TmToolbarButton Icon="@IconNames.Redo" Text="Redo" Priority="ToolbarButtonPriority.Secondary" />
      </TmToolbarGroup>
      <TmToolbarDivider />
      <TmToolbarButton Icon="@IconNames.Save" Text="Save" Priority="ToolbarButtonPriority.Pinned" />
  </TmToolbar>
  ```

  On the bar the group is the layout wrapper the measurement flattens (`data-tm-toolbar-group`):
  its buttons take room individually and still collapse on their own, exactly like the raw
  `role="group"` wrapper this replaces. With `Label` set, the wrapper is a real `role="group"` with
  an accessible name; without one it stays a layout-only wrapper with no role. When the group's
  buttons collapse into the "More" menu (of the toolbar or the mobile action bar), they keep the
  group: each contiguous run of items of one group renders as a `role="group"` section named by the
  group's label, with a visible but `aria-hidden` header, and adjacent runs are divided by a
  `role="separator"` (never leading, never trailing). The written order is the menu order — grouping
  never reorders, so a group interrupted by an ungrouped button renders as two runs around it.
  The arrow keys, Home/End and typeahead cross the sections without stopping on a heading or a
  divider, and the initial focus stays on the first enabled menu item. The shared action model grew
  the same concept for hand-built lists: `TmActionItem.Group` (the stable run key) and
  `TmActionItem.GroupLabel` (the section's name, read from the first item of a run). Renaming a
  group re-labels its open menu section immediately. The wireframe editor gained a matching
  "Toolbar Group" stencil (a dashed group frame with the label).

- **Toolbar and action-surface toggle state (F12/C2): `TmToolbarButton.Pressed` and
  `TmActionItem.Checked`.** A toolbar button can now be a toggle: `Pressed` (`bool?`, default
  `null` = a plain button with no `aria-pressed`; `true`/`false` render `aria-pressed` and the
  pressed look — subtle primary fill, primary ink and an inset primary ring that clears 3:1 in
  both themes) and `PressedMixed` (`bool`, renders `aria-pressed="mixed"` for a toggle whose
  state is `false` — e.g. Bold over a partly bold selection; mixed paints ring-only, a
  transparent background with the same inset ring, so a partly-on control reads apart from a
  pressed one — and hover restyles neither state like a plain control). Both states survive
  forced colors (Windows High Contrast): pressed paints the `SelectedItem` fill with
  `SelectedItemText` ink, mixed keeps the ring-only distinction (`Canvas` background, the
  `SelectedItem` ring). The host owns the state and flips
  it in `OnClick`. When a pressed button collapses into the toolbar's "More" menu, its entry is a
  `role="menuitemcheckbox"` with the matching `aria-checked` and a check-mark slot (Check when
  on, Minus when mixed, empty when off) instead of a plain `menuitem` — activating it still
  closes the menu (the close-before-invoke contract) and toggles. In a menu that offers
  checkable entries, plain entries are indented by the check-mark slot, so every label starts at
  the same left edge. The same model is exposed by
  the shared action model: `TmActionItem.Checked` (`bool?`) and `CheckedMixed` (`bool`) render
  `TmMobileActionBar` tiles with `aria-pressed` and checkable entries in its "More" menu. Radio
  modes (`menuitemradio`) are deliberately not part of this. The sheet's initial-focus selector
  now names `menuitemcheckbox`/`menuitemradio` beside `menuitem`/`option`, so a sheet whose
  first entry is checkable focuses the entry, not the header Done. The wireframe
  `TmToolbarButton` stencil gained a `pressed` prop that draws the highlighted button.

  ```razor
  @* before: a host could only splat the attribute — the bar button announced it,
     the "More" menu entry stayed a plain menuitem *@
  <TmToolbarButton Icon="@IconNames.Hand" Text="Pan"
                   aria-pressed="@(_panPressed ? "true" : "false")" />

  @* after: the parameter drives the bar button AND the "More" menu entry *@
  <TmToolbarButton Icon="@IconNames.Hand" Text="Pan"
                   Pressed="@_panPressed" OnClick="@(() => _panPressed = !_panPressed)" />
  ```

- **`TmContextMenu`, `TmSplitButton` and `TmNotificationBell` opt into the shared mobile bottom
  sheet (F11).** All three render `TmOverlayPanel` and kept the anchored popover at every width;
  each now carries two additive parameters (default behaviour is unchanged, nothing to migrate):
  `MobilePresentation` (`PanelPresentation`, default `Popover` — `Auto` presents the shared
  content-height bottom sheet on a mobile viewport and the anchored popover otherwise, a forced
  value renders that presentation without measuring) and `SheetTitle` (`string?`, names the sheet
  header; `null` falls back to new localized defaults `TmContextMenu_SheetTitle` ("Menu" /
  "Nabídka" / "Menu"), `TmSplitButton_SheetTitle` ("More actions" / "Další akce" / "Autres
  actions") and `TmNotificationBell_SheetTitle` ("Notifications" / "Oznámení" / "Notifications")).
  There is no sheet logic in the components — the same `TmDrawer Position=Bottom` primitive every
  anchored panel composes (title + Done in the header, Escape/backdrop/swipe dismiss, focus back
  on the trigger). Sheet-mode details: the menus' `role="menu"` moves onto the sheet content
  wrapper, menu items become full-width rows flush with the sheet header and 44px tap targets on
  coarse pointers, and the bell's in-panel header title is dropped inside the sheet (it would
  duplicate the sheet header) while its filter/mark-all actions stay reachable and the popover's
  400px list cap is not inherited by the content-height sheet. The popover presentation keeps its
  markup — the bell stays named by its visible in-panel title, and the two menus gain only the
  `aria-label` that `TmOverlayPanel` already derives from `Title` (the same contract
  `TmDropdown` has had since F3).

  ```razor
  @* opt-in: bottom sheet on a phone, popover on desktop *@
  <TmContextMenu MobilePresentation="PanelPresentation.Auto" SheetTitle="Row actions">…</TmContextMenu>
  <TmSplitButton Text="Save" MobilePresentation="PanelPresentation.Auto">…</TmSplitButton>
  <TmNotificationBell MobilePresentation="PanelPresentation.Auto" />
  ```

- **`docs/redesign-foundation.md` maps the redesign foundation.** One page links the
  contracts this release introduced (tokens, layout, sheet, focus scope, overlays,
  toolbar overflow, the mobile action bar, the editor shell, pointer drag, plurals
  and relative time) instead of restating them, and lists the follow-ups that are
  deliberately not in this release.

- **Additive core extensions (F8).** Every parameter defaults off. The shortcuts dialog's title gains a
  stable `id` and the dialog an `aria-labelledby` pointing at it, the palette's result list gains an
  `aria-label`, and the copy button's accessible name is localized — accessibility fixes, documented
  under Changed above; everything else keeps its markup.
  `TmKeyboardShortcutsHelp.Description` renders a subtitle under the title, wrapped with the title in a
  heading group that exists only when a description is set. `TmKeyboardShortcut` gains an optional
  `KeySeparator` (default `+`), so a pair like "Arrow Up / Arrow Down" stays two keys instead of being
  split into a chord; a separator other than `+` renders the localized "or" between the keys.
  `TmCopyButton.ShowLabel` renders the localized Copy/Copied text beside the icon (both labels share
  one grid cell, so the button keeps its width; a polite status announces "Copied"); the accessible
  name stays "Copy to clipboard". `TmCommandPalette.GroupByCategory` renders each category as a
  labelled `role="group"` (the divider is a CSS border), and `SplitShortcuts` renders one `kbd` per key. `TmSignatureCapture.ClearButtonPlacement`
  (`Actions|Overlay`, default `Actions`) moves the same clear button into the top-end corner of the
  surface. A segmented control is a convention, not a component
  ([docs/segmented-control.md](docs/segmented-control.md)): content switching uses `TmTabs Variant=Pill`,
  a value uses `TmRadioGroup.Appearance` (`Radio|Segmented`, default `Radio`). Seventeen lucide names
  the redesign plans share (`tablet`, `panel-left`, `group`, `magnet`, `scan`, `corner-up-left`, …)
  now render as built-in icons.

- **`pointer-drag.js` — the one shared pointer-drag helper** (`_content/Tempo.Blazor/js/pointer-drag.js`,
  F7): `attach(el, { threshold, longPressMs, selector, onStart, onMove, onEnd, onCancel, onRelease })`
  with pointer capture by `pointerId`, a movement threshold, touch long-press that keeps scrolling
  working, cancel on Escape / `pointercancel` / `lostpointercapture` / a missed mouse-up / `detach()`,
  `touch-action` guidance (`.tm-touch-drag-handle`, `.tm-pointer-dragging`), one-shot click
  suppression, `prefers-reduced-motion` reported to every callback, and per-instance state so several
  components drag at once. `tm-editor-shell.js` (the F6 panel resizer) and `scheduler.js` (event resize,
  B13: touch and pen now work, handles grow on coarse pointers) run on it. Every adopter must ship a
  non-drag alternative (WCAG 2.5.7, `Alt`+arrows): see [docs/touch-interaction.md](docs/touch-interaction.md).
- **`Loc.Plural("Key", n)`** — grammatical plurals from CLDR rules (`TmPluralRules`: cs/sk
  one-few-many-other, pl, fr one-many-other, en one-other) over `Key_One/_Few/_Many/_Other`, then
  `Key_Other`, then the plain key — and the plain key is used only when no family exists anywhere.
  `ITmLocalizer.Culture` and `ITmLocalizer.TryGetString` are default interface members, so an existing
  implementation still *compiles*. It does not keep working unchanged when it delegates: a class that
  forwards the indexer to the built-in localizer and overrides only the plain key is bypassed, because
  `TryGetString("Key_One")` reads the shipped family through that indexer. Such a localizer must
  override the family keys `_One/_Other`, plus `_Few`/`_Many` where its language needs them. A
  null `Culture` (what a mocking proxy returns for the unimplemented member) falls back to
  `CurrentUICulture` instead of throwing. A host class that already declares a public `Culture` or
  `TryGetString` now implicitly implements the new members.
- **`ITmRelativeTimeFormatter`** (Abstractions) with the default `TmRelativeTimeFormatter`
  (`Tm_Time_*` in en/cs/fr, injectable `TimeProvider`, explicit time zone): "právě teď", "před 5 min",
  "včera v 16:20", "zítra v 9:00", "za 5 dní", an absolute date from a week on (B19).
  `TmRelativeTime.Resolve` is public: every component — including split packages, which call
  `AddTempoBlazor()` and cannot see internals — resolves the formatter through this one helper
  (registered `ITmRelativeTimeFormatter`, else the default over the component's localizer and clock)
  instead of copying the fallback.
- **Demo: new `/localization` page** with a count stepper and presets (0/1/2/5/21/22/25/1000000) that
  shows every shipped plural family, the culture and the CLDR category for the count, and a
  relative-time stamp; the demo language switcher now offers FR alongside EN/CS.

- **Overflow toolbar (F4).** `TmToolbar.Overflow` (`None|Menu`), `Labels`
  (`Auto|Icons|IconsWithText`), `OverflowLabel`, `AriaLabel` and `OverflowPresentation`;
  `TmToolbarButton.Priority` (`Primary|Secondary|OverflowOnly`) and `LabelPosition`
  (`Inline|Below|Hidden`). **`ToolbarButtonPriority.Pinned`** (appended as the last member, so existing values keep
  their numbers) never moves into More — it maps to `ActionOverflow.Never` and is measured as fixed width (the
  trailing Save / primary call to action; the demo ribbon pins Save). A layout wrapper marked
  `data-tm-toolbar-group` (or `role="group"`) keeps its buttons individually collapsible; the order of record is the
  DOM order `tm-toolbar.js` reports (`OnFitChanged(maxVisible, orderedIds)`); a divider that separates nothing is
  hidden (`data-tm-divider-redundant`). The buttons that do not fit move into a More menu — the same shared
  menu host as the F5 action bar, a bottom sheet on a phone — lowest priority first, `OverflowOnly`
  always there; render order stays the written order. `tm-toolbar.js` measures how many buttons
  fit (rAF-coalesced, signature dedup) and generalises the DocumentEditor's `toolbar-overflow.mjs`
  (the editor migrates in its own plan). See [docs/toolbar.md](docs/toolbar.md).
- **`TmActionItem.Overflow`** (`Auto|Never|Always`) pins an action to the bar or to the More
  menu independently of `Priority`, and `TmActionItem.Danger` tints its menu entry; the shared
  `ActionOverflowLayout.Partition` honours the pins for both the action bar and the toolbar.
- **Menu keyboard for every `role=menu` surface.** `tm-menu-nav.js`, attached by
  `TmOverlayPanel` to each `Role="menu"` panel (popover or sheet): ArrowUp/ArrowDown/Home/End
  roving (native `disabled` and hidden items are skipped; see the Breaking / Migration entry on
  `aria-disabled` items), typeahead (diacritic- and case-insensitive) and a roving tabindex —
  `TmDropdown`, `TmSplitButton`, `TmContextMenu` and both overflow menus get the keys from the panel. A menu
  opened from the keyboard also needs its first item focused: `TmDropdown`, `TmSplitButton` and `TmContextMenu`
  each wire `OnOpened` → `focusFirst` through the shared `MenuNavInterop`, and a `TmContextMenu` item activation
  returns focus to its trigger. Tab out of an open popover menu closes it (`aria-expanded` follows).
- `TmToolbarButton` with `LabelPosition="Below"` stacks the label under the icon; `Hidden`
  omits the text span and keeps the text as the accessible name.
- `PanelPresentation` enum and `MobilePresentation` on `TmOverlayPanel`, `TmDropdown` and
  `TmPopover`, with `LayoutMode` / `InitialMode` / `ResolvedLayoutChanged` forwarded by the two
  components. `TmOverlayPanel.TrapFocus` for dialog popups that must enforce their modality, and
  `Title` for the sheet header. `TmDrawer.AriaLabel` names a drawer whose header is rendered
  through `HeaderContent`.
- `overlay.js` positions panels against the *visible* viewport: `positionFloating` +
  `readViewport` read `visualViewport`, so an on-screen keyboard opening flips/clamps an open panel
  into view, and `visualViewport` resize/scroll re-place open panels.
- `docs/overlays.md`: when to use popover vs modal vs sheet, and the migration path for packages
  carrying local clamp/focus-trap copies (NotionEditor's ~5 clamp copies, DocumentEditor's four
  focus traps).
- Semantic tokens for the redesign, in both `tokens.css` and `tokens-dark.css`: `--tm-bg-workspace`,
  `--tm-radius-card`, `--tm-radius-control`, `--tm-shadow-card`, `--tm-shadow-popover`,
  `--tm-touch-target`, `--tm-focus-ring`, `--tm-border-color-strong`, the `--tm-status-*` chip
  pairs and the `--tm-priority-*` glyphs. Existing components do not reference them yet, so the
  default appearance is unchanged.
- Opt-in indigo theme (`theme-indigo.css`, activated with `data-tm-theme="indigo"`). The blue
  scale stays the default. The demo exposes a switch that persists the choice in `localStorage`.
- Shared keys `Tm_Ok`, `Tm_Done` and `Tm_More` in en/cs/fr.
- `TmFocusScope`, the public focus-trap root. `TmSheetHandle`, the shared grabber.
  `MobilePresentation`, `FooterLayout` and `DialogLayout`. `InitialFocusTargetId` on the scope.
  [docs/bottom-sheet.md](docs/bottom-sheet.md).
- `scripts/audit-css-strict.mjs` with a shrink-only baseline, gated by `CssTokenAuditTests`, and
  `ScopedCssOwnershipTests` with its own shrink-only baseline. The audit now also flags the named
  colours `white` and `black`, and judges white-on-primary per rule rather than per line. See
  `docs/css-token-aliases.md` and `docs/scoped-css-ownership.md`.
- `TmOverlayPanel.AriaLabel` / `TmOverlayPanel.AriaLabelledBy` parameters for giving the panel an
  accessible name. Every `Role="dialog"` panel ships one; `AriaLabelledBy` is preferred when the
  panel renders a visible title.
- `OverlayPanelRules_Inventory_*` tests pin the exact `.tm-overlay-panel` rule set of the bundled
  CSS (the `:where()` reset, the fallback closed-state hide, the `:where()` open display) so a
  new or re-weighted global rule on the shared element fails as a finding (CF03c).
- `TmOverlayPanel.SheetDoneEnabled` gates the sheet header's Done action (sheet mode only): a
  consumer mid-task keeps Done disabled until its input is complete — the date-range picker
  disables it until BOTH dates are picked.
- `TmOverlayPanel.OnOpened` fires after the panel is actually displayed — the popover's
  `showPopover()` has run, or the opening sheet render batch completed — so consumers that move
  focus (a filter input) no longer land on a hidden element.
- `InitialFocusSelector` on `TmFocusScope` and `TmDrawer` resolves the initial-focus target
  inside the scope from a CSS selector (e.g. the first menuitem of a menu sheet); an explicit
  `InitialFocusTarget`/`InitialFocusTargetId` still wins.
- `TmMobileActionBar` and the shared `TmActionItem` model (F5): a component-scoped mobile action
  bar rendering up to `MaxVisible` actions with the overflow in a "More" menu that composes
  `TmOverlayPanel` (anchored popover on desktop, the shared bottom sheet on a mobile viewport —
  never a local copy of sheet/gesture/focus logic). `Placement` defaults to `StickyContainer`
  (`position: sticky` inside the component's own container — never `position: fixed` like
  `TmBottomNavigation`, which stays application chrome); `FixedViewport` is a deliberate opt-in
  anchoring to the viewport with the bottom safe area, and `Inline` renders the bar statically.
  With the default `Auto` visibility the bar exists only below the mobile breakpoint, resolved
  from the container (or from the viewport scope for `FixedViewport`); `LayoutMode` forces a mode
  for hosts and bUnit tests. The `ChildContent` region renders above the bar at every layout —
  only the bar hides above the breakpoint — and, unlike `TmToolbar`'s free-form fragments, it is
  the content the bar belongs to: the bar reserves nothing for it except the FixedViewport
  placement, where the region's bottom padding equals the bar height (the published
  `--tm-mobile-action-bar-reserve-block-size` token) so nothing is covered at 320 or 390.
  Bar actions are the bar's own tile buttons (icon over a two-line label), not `TmButton`, and
  `Priority` is only an overflow rank — the bar and the menu render in `Items` order. The public
  `CloseMoreAsync()` lets a host close the "More" menu after a confirmed `KeepMenuOpen` action.
  `docs/responsive-conventions.md` documents the
  bottom-navigation-vs-action-bar split; the demo page is `/mobile-action-bar`; the wireframe
  stencil registers the `action-bar` role. Reference usage: `TmDashboard` edit mode on mobile
  renders Add/Save/Cancel through the bar instead of the wrapping toolbar.

- `TmEditorShell` and `TmSidePanel` (F6, `Tempo.Blazor.Components.Layout`): the responsive frame for
  canvas editors and a standalone inspector. `TmEditorShell` exposes the slots `Header`, `Toolbar`,
  `Left`, `Canvas`, `Right`, `StatusBar`, `MobileActions` (`IReadOnlyList<TmActionItem>`, rendered
  by `TmMobileActionBar`) and the optional rail slots `LeftRail` / `RightRail`. It resolves its own
  layout from its container (`LayoutMode` / `InitialMode` / `ResolvedLayoutChanged`) with
  per-instance thresholds (`Breakpoints`, a `TmLayoutBreakpoints(sm, lg)`; the same optional
  parameter exists on `TmLayoutObserver`). **Desktop** and **tablet** are docked panels with a strip
  toggle each (hide / collapse to a 44px rail / expand, `aria-expanded`, `aria-controls` only while
  the controlled panel is rendered); on tablet **at most one panel is expanded** and there is no
  modal. **Mobile** is the canvas plus an inline, non-modal bottom sheet (snaps 50% / 100%) with a
  Left/Right tab strip when two panels are open, or `MobilePanelPresentation.Tabs` — a full-region
  `Left | Canvas | Right` `tablist` (`CanvasTitle`). `ActiveMobilePanel` and `MobileSheetSnapIndex`
  are two-way so a host can switch to the properties on a block selection and collapse the sheet on
  an "add block" action; the Toolbar slot is not rendered on mobile when `MobileActions` is set.
  `LeftOpen`, `RightOpen`, `CollapsedPanels` (`[Flags] EditorShellPanel`, now with `None = 0`) and
  `LeftWidth` / `RightWidth` are two-way. Each expanded docked panel has a resize **separator**
  (`role="separator"`, pointer drag + arrow keys, `MinLeftWidth` / `MaxLeftWidth` /
  `MinRightWidth` / `MaxRightWidth`, never below `MinCanvasWidth`); `PersistWidthsKey` persists only
  user-resized widths to `localStorage` (validated, an explicit host width wins). Focus returns to a
  replaced trigger only when focus was lost. `TmSidePanel` is the docked-or-sheet inspector
  (`SidePanelPresentation` `Auto | Docked | Sheet`: docked when its container resolves Desktop, a
  modal sheet otherwise whose side/bottom geometry follows the viewport scope; `Open` two-way,
  `Title`, `HeaderActions`, `FooterContent`, `Side` (`SidePanelSide.Left | Right`), `Width`,
  `RestoreFocusTargetId`). Both compose `TmDrawer` — no local sheet, gesture or focus logic.
  `docs/editor-shell.md` documents the pattern (incl. the e-mail editor mapping) for the
  DiagramEditor, Wireframe, Signing designer, PdfAnnotator, Modeling and e-mail editors; the demo
  page is `/editor-shell`; the wireframe stencil registers the `editor-shell` and `side-panel` roles.
- `tm-sheet.js` accepts an explicit basis element and an explicit track-viewport flag for an inline
  sheet, the browser top-layer promotion helpers moved to `tm-top-layer.js` (the `tm-sheet.js`
  exports re-export them, so every existing caller keeps working), and the `TmDrawer` and
  `TmModal` bodies now become a keyboard-reachable scroll region while they overflow (see
  **Changed**).
- `TmLightbox.RestoreFocusTargetId` (`string?`, default `null`): the id of the element focus
  returns to when the lightbox closes; null restores the element that held focus when it opened.
  `TmImageGallery` passes the thumbnail of the image on screen, so Escape after browsing lands on
  the image the user is looking at — in every browser, including the ones that never focus a
  clicked button.

- **Slovak (sk) ships as a built-in language for every component.** `TmResources.sk.json` — all
  5,606 English keys in natural Slovak (not a Czech copy-paste; a test rejects Czech-only
  ě/ř/ů characters) plus the Slovak `_Few`/`_Many` forms of all 16 plural families — is embedded
  into `Tempo.Blazor.dll` next to en/cs/fr and read by the same `JsonStringLocalizer` naming
  convention, so `CultureInfo("sk")` (or `sk-SK`, which falls back to its language) localizes
  every component under Blazor Server and WebAssembly alike. Counted texts agree with the count
  through the Slovak one/few/many/other rules `TmPluralRules` already carried ("1 nález",
  "2–4 nálezy", "5+ nálezov"; "1 čakajúca zmena", "2–4 čakajúce zmeny", "5+ čakajúcich zmien"),
  and the relative-time stamps read "pred 2 dňami", "o 5 dní". The demo language switcher gained
  an SK button; E2E (`LocalizationPluralsE2ETests`) verifies Slovak rows on the `/localization`
  page, and bUnit covers the Slovak table via `JsonStringLocalizerTests`/`PluralLocalizerTests`.
  The new `TmResourceCompletenessTests` guard enforces en→cs/sk/fr key parity with identical
  placeholders;
  `docs/localization-plurals.md`, README and AGENTS.md document the fourth language, and the
  `ITmLocalizer` JSON documentation now shows the decorator pattern for rewording shipped
  translations in an application (registered after `AddTempoBlazor()`).

### Fixed

- **Scriban code inside block content renders again (regression from 2.9.0).** `MjmlGenerator` sanitized
  the authored content of `mj-text`/`mj-accordion-text` *before* substitution, and the sanitizer's
  quote-safe formatter entity-encoded the code itself, so `{{ price | math.format "N2" }}`,
  `{{ text | string.truncate 5 "..." }}`, `{{ if price > 100 }}` and any `&&` produced
  `Invalid token found &` and an empty `RenderResult.Html`. Code spans (`{{ … }}`, and `{% … %}` when it
  carries no markup) are now masked, the rest of the content is sanitized, and the spans are restored
  verbatim (`HtmlContentSanitizer.SanitizeTemplateContent`); everything inside a span is still escaped by
  the render pipeline and the whole substituted MJML/HTML is still checked by `MarkupGuard`, so a value in
  an attribute cannot break out and markup cannot be smuggled through a span.

  ```text
  before  {{ price | math.format "N2" }}   → RenderResult.Html == "" (error: Invalid token found `&`)
  after   {{ price | math.format "N2" }}   → 1,234.50   (cs-CZ: 1 234,50)
  ```

- **A deeply nested document can no longer crash the process.** The MJML importer measures the element
  depth iteratively right after parsing (a 20 000× nested `mj-hero`/`mj-group` payload used to overflow the
  mapping recursion), threads the model depth through the mapping as a second guard, refuses
  self-/mutually-recursive `mj-include` references (a path stack plus an include depth limit of 8) and the
  JSON reader/writer bounds depth from the document's own nesting. `EmailDocumentLimitsValidator` and the
  schema-version code walk the block tree iteratively too, and `Deserialize(json, options)` also enforces
  `MaxInputBytes` and the section/block limits (throwing `EmailTemplateSerializationException` with the
  localization key, never a silent accept). The JSON depth bound is derived from the model nesting limit
  (`14 + 6 × MaxNestingDepth`, decision `EMAIL-IMPORT-JSON-DEPTH`); a plain `Deserialize(json)` without
  options keeps `max(64, …)`, i.e. it is never stricter than `System.Text.Json`'s own default.

### Breaking / Migration

- **PDF components no longer read the undefined `--tm-primary`/`--tm-border`/`--tm-*-dark`
  aliases — they read the core tokens.** `TmPdfViewer`, `TmPdfAnnotator`, `TmPdfAnnotatorPanel`,
  `TmPdfAnnotationThreadPanel`, `TmPdfAnnotationLayer`, `TmPdfAnnotatorLayer` and
  `TmRedactionLayer` now paint with the canonical tokens (`--tm-color-primary`,
  `--tm-border-color`, `--tm-color-surface-elevated`, `--tm-color-surface-hover`,
  `--tm-text-primary/-secondary`, `--tm-color-on-primary`, `--tm-radius-card/-control`,
  `--tm-color-warning`, `--tm-bg-workspace`, `--tm-shadow-card`, `--tm-focus-ring`), so the
  palette, the indigo theme and dark mode propagate into them — the manual
  `[data-theme="dark"]`/`.tm-dark` blocks and the JS inline colors/shadows/radii of thumbnails
  and continuous pages are gone (thumbnails/labels/pages now carry the
  `tm-pdf-thumbnail`/`tm-pdf-thumbnail--active`/`tm-pdf-thumbnail__label`/`tm-pdf-viewer__page`
  classes styled under `::deep`). Hosts that re-skinned the PDF components by overriding the old
  undefined aliases must override the canonical tokens instead (see docs/css-token-aliases.md —
  an override of a `var()`-valued token has to target `:root`, `[data-theme="dark"]` AND
  `.tm-dark` together):
  ```css
  /* before: an alias that was never defined — the components rendered their own hex fallbacks
     and every theme change was invisible */
  :root { --tm-primary: #7c3aed; --tm-hover: #ede9fe; }
  /* after: the tokens the PDF components actually read, overridden for all three theme
     selectors so a dark region sees it too */
  :root, [data-theme="dark"], .tm-dark {
      --tm-color-primary: #7c3aed;
      --tm-color-surface-hover: #ede9fe;
  }
  ```
  The per-component layout knobs stay component-local: `--tm-pdf-page-shadow` and
  `--tm-pdf-panel-width` are defined on each component root (viewer thumbnails `140px`,
  annotator panel `300px`, thread panel `280px`), never as global aliases.
- **`TmDocumentOpenDialog` is a fullscreen, drill-down panel below 640 px (redesign plan ce19aa8c, phase 9) - it used to be the desktop dialog squeezed into the width.**
  Impact: the public API (`Provider`, `Kind`, `Open`, `OnSelected`, `ShowModeToggle`, `DefaultMode`, `ShowRecent`, `LayoutMode`, ...), `DocumentOpenResult` and `ITempoDocumentLibraryProvider`
  are unchanged, and so are desktop and tablet (including a *forced* `Tablet` layout on a phone viewport: a layout you force always wins). What changes is the DOM of the **mobile** layout - a
  dialog body below 640 px (a phone, or a narrow host container) or `LayoutMode="Mobile"`, which until now reused the tablet rules: (1) the modal root is `.tm-modal--fullscreen-presentation` (was a
  `.tm-modal--sheet` bottom sheet on a phone viewport and an XLarge dialog in a narrow container) and the host wrapper `.tm-dod-host` (`display: contents`) exists; (2) **there is no
  `.tm-dod-tree` / `[role=tree]` / `.tm-dod-tree-node`** - a test or a host that entered a folder by clicking a tree item must click `.tm-dod-folder-row[data-path$="/Designs"]` (a native `button`,
  one tap drills in) and go back with `.tm-dod-back` or a crumb; (3) document rows are two lines (`.tm-dod-row-name` / `.tm-dod-row-meta` / `.tm-dod-row-preview`) - the list header
  and the `.tm-dod-cell-*` cells are not rendered, and the grid is two columns; (4) **Rename, Delete and New folder are `TmMobileActionBar` tiles** in the footer
  (`[data-action-id="rename"]` / `delete` / `new-folder`, inside `.tm-modal-footer`) instead of `.tm-dod-rename` / `.tm-dod-delete` / `.tm-dod-new-folder` toolbar buttons - the inline bars
  (`.tm-dod-new-folder-bar`, `.tm-dod-rename-bar`, `.tm-dod-delete-confirm`) are unchanged but sit in the sticky head above the list and the first two gain a Cancel (`.tm-dod-inline-cancel`); (5) **there is
  no `.tm-dod-cancel` button** in the mobile footer (the header close button cancels and raises `OnCancelled`); `.tm-dod-open` and the Link / Copy cards (`.tm-dod-mode-link` / `.tm-dod-mode-copy`,
  now `.tm-dod-mode--compact`) stay; (6) a **double click / double tap on a row or card selects only** on mobile (a drill-down tap followed by a quick tap on the row that lands at the same spot would
  have opened a document the user only meant to select) - Open is the button, or Enter on the focused row; desktop and tablet still open on a double click; (7) the Auto measurement uses a wider
  mobile threshold (640 + 112 px) while the fullscreen panel is up, so one viewport width stays mobile in both presentations instead of flapping between the dialog and the panel; with an
  app-level `TmLayoutObserver IsViewportScope` the first frame is already the mobile layout.
  Before / after:
  ```csharp
  // before: a phone opened the desktop dialog squeezed into the width - the tree was the way into a folder
  await page.Locator(".tm-dod-tree-node", new() { HasTextString = "Designs" }).ClickAsync();
  await page.ClickAsync(".tm-dod-rename");                  // toolbar button, disabled until a row is selected
  await page.ClickAsync(".tm-dod-cancel");

  // after: layout-aware - a drill-down row and the footer action bar on mobile, the tree and the toolbar elsewhere
  await page.Locator(".tm-dod-tree-node, .tm-dod-folder-row", new() { HasTextString = "Designs" }).First.ClickAsync();
  await page.ClickAsync(".tm-dod-rename, [data-action-id=rename]");
  await page.ClickAsync(".tm-modal-close");                 // the mobile Cancel (desktop / tablet keep .tm-dod-cancel too)
  ```
  ```razor
  @* unchanged: the same markup renders the dialog on a desktop and the fullscreen panel on a phone; pin either with LayoutMode *@
  <TmDocumentOpenDialog Provider="library" Kind="TempoDocumentKind.Wireframe" @bind-Open="open" OnSelected="Picked" />
  <TmDocumentOpenDialog Provider="library" Kind="TempoDocumentKind.Wireframe" @bind-Open="open" LayoutMode="TmLayoutMode.Mobile" OnSelected="Picked" />
  ```
  The three Notion blocks (wireframe, diagram, spreadsheet) host the dialog with `ShowModeToggle=true` and need no change: the Link / Copy choice stays, as compact cards above the action bar.
- **`TmDocumentOpenDialog` is a wider modal with a collapsible tree, always-visible actions and radio-card modes (redesign plan ce19aa8c, phase 8).**
  Impact: the public API (`Provider`, `Kind`, `Open`, `OnSelected`, `ShowModeToggle`, `DefaultMode`, ...), `DocumentOpenResult` and every `.tm-dod-*` selector the
  Notion / Diagram / Spreadsheet blocks and the E2E suites use stay, but the DOM and look changed:
  (1) the modal is `ModalSize.XLarge` (900 px, was `Large` 680 px); (2) **Rename and Delete are always rendered** (when the provider advertises the capability)
  and are `disabled` until a document is selected - before they appeared only with a selection, so a host stylesheet or test that expected them to be *absent*
  must now check `disabled`; (3) the **folder tree is collapsible**: rows are `div[role=treeitem]` (not `<button>`), carry `aria-expanded` on folders with children,
  a mouse-only `.tm-dod-tree-toggle` chevron (a fenced, `aria-hidden` non-focusable `<span>`; a click returns focus to the row) and are ONE tab stop named by their label (`aria-labelledby`, plus `aria-setsize` / `aria-posinset`) with the WAI-ARIA arrow keys (Left/Right collapse / expand / parent / first child, Up/Down, Home/End,
  Enter and Space navigate); the inline `padding-left` is now `padding-inline-start: calc(...)`; (4) list rows, grid cards and tree rows use a **roving
  `tabindex`** (`0` on one, `-1` on the rest, the arrow keys move focus) instead of `tabindex="0"` on every row - a test that asserted `tabindex="0"` on a
  specific row must assert "exactly one `0`" or focus the row; (5) the **link / copy choice is a `role="radiogroup"` of radio cards** (`.tm-dod-mode-card`,
  title + its own hint) instead of two inline native radios sharing one hint - `.tm-dod-mode-link` / `.tm-dod-mode-copy` are still the native `input[type=radio]`
  (so `.Change(true)` / `Check()` keep working) and the hint class `.tm-dod-mode-hint` now exists once per card; the radio `name` is per-instance; (6) the breadcrumb is
  inside the new `.tm-dod-main` right pane, its first crumb is a home **icon** button (`aria-label` "Home", no longer the text "All documents") and the separator is
  `›` (was `/`); the last crumb is `aria-current="page"` and not a button; (7) the list header is sentence case (no `text-transform: uppercase`), list rows carry `aria-selected` and `role="gridcell"` cells, and the footer
  buttons live in `.tm-dod-footer-actions` (the `.tm-dod-footer-spacer` element is gone); (8) the dialog root has a new `.tm-dod` class and `data-layout`
  (the footer carries it too), toolbar buttons render icon + label, and **until the mobile phase the `Mobile` layout (and a phone viewport, where `TmModal` becomes a
  sheet) uses the same compact rules as `Tablet`**; the arrow keys / Home / End on a tree item, row or card no longer scroll the container (the colocated scroll guard
  cancels them, Alt / Ctrl / Meta / Shift combinations excluded); New folder is disabled on the Recent node, a search replaces the Recent node, and the Open result carries the
  document name after a rename.
  Before / after:
  ```csharp
  // before: Rename / Delete absent until a row is selected, tabindex="0" on every row
  cut.FindAll(".tm-dod-rename").Should().BeEmpty();
  cut.FindAll(".tm-dod-row").Should().OnlyContain(r => r.GetAttribute("tabindex") == "0");

  // after: always rendered but disabled, exactly one row in the tab order
  cut.Find(".tm-dod-rename").HasAttribute("disabled").Should().BeTrue();
  cut.FindAll(".tm-dod-row").Count(r => r.GetAttribute("tabindex") == "0").Should().Be(1);
  ```
  ```razor
  @* new, opt-in: the Recent node and a pinned layout *@
  <TmDocumentOpenDialog Provider="library" Kind="TempoDocumentKind.Wireframe" @bind-Open="open"
                        ShowRecent="true" LayoutMode="TmLayoutMode.Auto" OnSelected="Picked" />
  ```

- **The wireframe editor is a different, lighter editor below 768 px, and its export dialog is a bottom sheet there (redesign plan ce19aa8c, phase 7).**
  Impact: in an editor container narrower than 768 px (a phone, or a narrow column) `TmWireframeEditor` no longer shows the desktop chrome squeezed into the width
  (the fixed-width toolbox and properties columns left the canvas 0 px wide). It shows the mobile layout described above: the command row is replaced by the tool row and
  the `TmMobileActionBar`, the toolbox / inspector / pages / layers are bottom sheets, the document title is always shown in the header, the page tabs and the rulers /
  minimap / import file picker are not rendered, and the canvas drops resize / rotate handles, the rubber band and the connector tool (`LightEditing`). The
  `.tm-wd-editor__toolbar`, `[data-testid="wd-toggle-left"]` / `wd-toggle-inspector` (tablet) and the `Export` / `Import` toolbar items do not exist in that layout; the
  long-press menu is `.tm-overlay-panel-sheet` instead of `.tm-wd-editor__context-menu`; `TmWireframeExportDialog` renders `TmModal` with the `Sheet` presentation
  (`.tm-modal--sheet`) on a mobile viewport. Tablet (768-1199) and desktop (>= 1200) are unchanged.
  Before / after: a host that needs the old behaviour on a narrow container pins the layout (`LayoutMode="TmLayoutMode.Tablet"`), a host that embeds the editor in a
  < 768 px column and wants a "Save" supplies it through the new slot, and a host that pinned the export dialog's presentation sets it explicitly:
  ```razor
  @* before: the editor was squeezed on a phone, a "Save" had to live outside it *@
  <TmWireframeEditor @bind-Document="doc" />

  @* after: a mobile action for the host, and the layout can still be pinned *@
  <TmWireframeEditor @bind-Document="doc" LayoutMode="TmLayoutMode.Auto">
      <HeaderActions><button @onclick="Open">Open…</button></HeaderActions>
  </TmWireframeEditor>
  <TmWireframeEditor @bind-Document="doc" MobileActions="@(new[] { new TmActionItem { Id = "save", Label = "Save", Overflow = ActionOverflow.Never, OnClick = save } })" />

  @* the export dialog: Auto (default) = sheet on a phone, dialog elsewhere; force one *@
  <TmWireframeExportDialog Show="open" MobilePresentation="MobilePresentation.Dialog" OnExport="Export" />
  ```
  The duplicate of the context menu / the mobile quick action is now ONE undoable step (`AddElementsCommand`); before, `Duplicate` added the copies behind the command stack.

- **The wireframe editor chooses its panels by the width of its own CONTAINER, and the fixed panel widths are gone (redesign plan ce19aa8c, phase 6).**
  Impact: `TmWireframeEditor` resolves `mobile < 768`, `tablet 768-1199`, `desktop >= 1200` from its own box (the pair `TmDiagramEditor` and `docs/editor-shell.md` use for editors; the plan's 1279 would make the demo, whose page is capped at `max-w-7xl`, tablet at every viewport) (it is a CSS size container,
  `container-name: tm-wd-editor`), not from the viewport and no longer with the phase-4 pair 640 / 860. Between 768 and 1199 px the docked left
  panel and inspector are replaced by an overlay drawer and a side sheet (one at a time), the command row becomes two toolbars, the minimap is
  hidden and the toolbox is a single column; the `--tm-wd-toolbox-w` (14rem) and `--tm-wd-props-w` (18rem) custom properties are removed -
  panel widths come from `TmEditorShell` (`LeftWidth` 260px / `RightWidth` 300px, resizable) and the tablet sheets from `--tm-wd-sheet-w`
  (20rem, capped at 60% of the canvas). The editor needs about 1200 px of container to dock both panels: the demo at a 1440 viewport has its sidebar (about 1080 px editor), so it shows the tablet layout there unless the sidebar is hidden.
  Before: `.my-editor .tm-wd-editor { --tm-wd-props-w: 22rem; }` and a layout that docked both panels from 860 px.
  After: the width of a docked panel is the shell's (resizable by the user, 260 / 300 px by default) - there is no CSS variable to override;
  to keep the phase-4 behaviour for a host layout pin it with `<TmWireframeEditor LayoutBreakpoints="new TmLayoutBreakpoints(640, 860)" />`
  (or force `LayoutMode="TmLayoutMode.Desktop"`). Selectors: tests that clicked the docked toolbox tabs on a 768-1279 px editor must open the drawer first
  (`[data-testid='wd-toggle-left']`, then `[data-testid='wd-left-drawer'] ...`); every `.tm-wd-editor__*` selector is unchanged.

- **The wireframe canvas listens to Pointer Events instead of mouse events (redesign plan ce19aa8c, phase 5).**
  Impact: `wireframe-designer.js` no longer registers `mousedown` / `mousemove` / `mouseup` on the canvas `<svg>` or the document; the
  same gestures now arrive as `pointerdown` / `pointermove` / `pointerup` / `pointercancel` (a mouse, a finger and a pen all produce them),
  the context-menu dismiss listens to `pointerdown` instead of `mousedown`, and the `<svg>` carries `touch-action: none`. The script
  also loads three ES modules from next to it (`../../Tempo.Blazor/js/pointer-drag.js`, `pointer-gesture.js` and its own
  `wireframe-gestures.mjs`), so the host must serve `_content/Tempo.Blazor/js/` and `_content/Tempo.Blazor.Wireframe/js/` (every
  default static-web-asset host does) and a strict CSP that allows `wireframe-designer.js` must allow those modules too.
  The `[JSInvokable]` surface of `TmWireframeDesignerCanvas` and every Blazor parameter are unchanged. A host that attached its own
  `mousedown` listener to the canvas to observe clicks must switch to `pointerdown` (pointer events also fire for touch); a test that
  drives the canvas with `dispatchEvent(new MouseEvent('mousedown', ...))` must dispatch `PointerEvent`s, or use Playwright's real
  mouse / touch input, which produces both.
  Before:

  ```js
  document.querySelector('.tm-wd-canvas__svg').addEventListener('mousedown', e => track(e.clientX, e.clientY));
  ```

  After:

  ```js
  document.querySelector('.tm-wd-canvas__svg').addEventListener('pointerdown', e => track(e.clientX, e.clientY));
  ```

  Also: a rotated element now keeps its rotation while it is dragged or nudged (the live preview used to drop it until Blazor
  re-rendered), and the selection handles are drawn 1.5x larger with extra invisible hit rects on a coarse pointer
  (`[data-handle-hit]`, `.tm-wd-connector__waypoint-hit`); a stylesheet or test that counted `[data-handle]` elements must expect the
  extra hit rects there (they carry the same `data-handle` key).

- **The `data-tm-group` set of the wireframe toolbar is frozen (phase 4 review).** Hosts and tests key off the attribute, never off the
  position of a group: `tools`, `history`, `arrange`, `group`, `align`, `snap`, `format`, `view`, `zoom`, `file`. A group may
  move between the bar and the "More" menu as the width changes; the name set only ever grows by an additive, documented entry.
  Before: `.tm-wd-editor__toolbar > div:nth-child(3)` (a positional selector on the toolbar's internal DOM).
  After: `.tm-wd-editor [data-tm-group='arrange']` (the group may sit in the bar or in the "More" menu - open it first when the width is narrow).
- **`TmWireframeEditor` renders through the core `TmEditorShell`: new DOM, one `TmToolbar` command row, named groups and visible labels (redesign plan ce19aa8c, phase 4).**
  Impact: a test, script or stylesheet that targeted the old structure must move. The editor was a flat column of
  `.tm-wd-editor__toolbar` (≈35 hand-written `.tm-wd-editor__btn` buttons and four hand-rolled dropdowns), the page tabs, and a
  `.tm-wd-editor__body` holding `__toolbox-wrap`, `__canvas-area` and `__props-wrap` (properties **and** layers stacked). It is
  now a `TmEditorShell` (`data-region="header|toolbar|main|left|canvas|right"`) whose command row is ONE core `TmToolbar`
  (`Overflow=Menu`, `Labels=Auto`) with named, labelled groups (`data-tm-group` = `tools`, `history`, `arrange`, `group`,
  `align`, `snap`, `format`, `view`, `zoom`, `file`, each `role="group"` with an `aria-label`), a `Priority` on every button
  (the lowest priority leaves the bar first, into the "More" menu), and a pinned **Export** primary button. The classes
  `.tm-wd-editor`, `__title`, `__canvas-wrap`, `__canvas-area`, `__page-tab*`, `__zoom-label`, `__minimap-wrap`, `__error-banner`, the
  whole `.tm-wd-toolbox__*`, `.tm-wd-props__*`, `.tm-wd-layers__*` families and `data-el-id` / `.tm-wd-canvas__svg` are unchanged.
  Removed: `.tm-wd-editor__body`, `__toolbox-wrap`, `__props-wrap`, `__btn` / `__btn--icon` / `__btn--active` / `__btn--text` on the
  **toolbar** (the buttons are `.tm-toolbar-btn`; the classes stay for the inspector's buttons), `__dropdown*`,
  `__canvas-size-input`, `__btn--canvas-size`, `__toolbar-sep`. Before / after:
  ```csharp
  // before: ToolbarSelector = ".tm-wd-editor__toolbar button[aria-label='Undo']", Export = ".tm-wd-editor__dropdown-wrap:has-text('Export')"
  page.Locator(".tm-wd-editor__btn[aria-label*='Lock' i]");            // every command was a bar button
  page.Locator(".tm-wd-editor__dropdown-item", new() { HasText = "PNG" });
  // after: toolbar buttons are core toolbar buttons, menus are TmDropdown items with data-testid
  page.Locator(".tm-toolbar button.tm-toolbar-btn[aria-label^='Lock']");   // or the entry in the "More" menu when the bar is narrow
  page.Locator("[data-testid='wd-export'] button.tm-dropdown-trigger");    // Export (pinned primary button)
  page.Locator("[data-testid='wd-export-png']");                           // Export ▸ PNG
  ```
  Migrate: use `data-testid`s (`wd-export`, `wd-export-json|svg|png|pdf`, `wd-align`, `wd-distribute`, `wd-import`, `wd-device-preset`,
  `wd-canvas-width|height|apply`, `wd-page-name`, `wd-grid-show|size`, `wd-element-name`, `wd-delete-element`, `wd-page-add`) and the
  `data-tm-group` groups instead of positional selectors; a command that is not on the bar is in the "More" menu
  (`button.tm-toolbar-more`), where its entry carries the visible label.
- **Wireframe editor: format painter, Read-only, rulers and the panel toggles moved into the toolbar's "More" menu; canvas size moved to the inspector.**
  Impact: the Copy style / Paste style / Paste size buttons, the Read-only/Edit toggle, the rulers toggle and the toolbox /
  properties toggles are no longer on the bar. They are checkable or plain entries of "More" (`role="menuitemcheckbox"` for Read-only,
  Show rulers, Components panel, Properties panel — the host-pinned `ReadOnly="true"` still hides the Read-only entry).
  The format painter also stays in the inspector's *Format painter* section. The `1280×800` canvas-size button with its inline
  inputs is gone: with an empty selection the inspector's **Canvas** section has the device preset, width/height and **Apply**.
  Before: `button[aria-label='Switch to read-only mode']`, `button[aria-label='Show rulers']`, `.tm-wd-editor__btn--canvas-size`.
  After: open `button.tm-toolbar-more` and click the `Read-only` / `Show rulers` entry; use `[data-testid='wd-canvas-width']` etc.
- **Wireframe editor: `ShowToolbox` / `ShowPropertiesPanel` now drive the shell panels (kept as parameters, now aliases).**
  `ShowToolbox` = the *Components* tab, `ShowPropertiesPanel` = the right inspector **and** the *Layers* tab (the layers panel
  moved from the bottom of the right column into the left panel, rendered with `ShowCollapseToggle="false"`). Both parameters keep
  their names and defaults; collapsing a panel is now shell state (the strip toggles and the More-menu entries). With both off the
  left panel is not rendered. `TmWireframeLayersPanel` stays public and unchanged outside the editor.
  Before: `<TmWireframeEditor ShowPropertiesPanel="false" />` hid the inspector and the layers. After: the same markup hides both
  (the Layers tab with it); `ShowToolbox="false"` alone keeps the Layers tab.
- **Wireframe editor: the page tabs are one of two presentations (`PageNavigation`), the default is unchanged.**
  `PageNavigation="Tabs"` (default) keeps the horizontal tabs and the *Components* / *Layers* left tabs. `PageNavigation="Panel"`
  adds a *Pages* tab (the new public `TmWireframePagesPanel`: numbered list, "⋯" Rename / Duplicate / Move up / Move down / Delete,
  Alt+↑/↓, drag, "+ Add page") and drops the horizontal tabs. Page operations stay non-undoable. The demo page switched to `Panel`
  and moved its "Open…" button into the new `HeaderActions` slot. Migrate: nothing, unless you want the panel:
  ```razor
  <TmWireframeEditor @bind-Document="doc" PageNavigation="WireframePageNavigation.Panel">
      <HeaderActions><button @onclick="OpenAsync">Open…</button></HeaderActions>
  </TmWireframeEditor>
  ```
- **Wireframe editor: `ShowGrid` / `GridSize` are no longer assigned by the component and the grid is edited in the inspector.**
  Same contract as `ReadOnly` / `ShowRulers`: the inspector's *Grid* section keeps an internal state and raises the new
  `ShowGridChanged` / `GridSizeChanged` (`@bind-ShowGrid`, `@bind-GridSize`); a changed parameter is adopted again. The context menu's
  *Hide/Show grid* goes through the same path (before it assigned the parameter). Hosts that only set the initial value are unaffected.
  Before: `<TmWireframeEditor ShowGrid="true" />` and the user's grid toggle overwrote the host's parameter value (the host never saw it).
  After: `<TmWireframeEditor @bind-ShowGrid="grid" @bind-GridSize="gridSize" />` to track the user's choice; without a binding the host value is only the initial state.
- **Wireframe editor: the left toolbox is a 3-column grid of icon tiles.** `WireframeComponentDef.Icon` is rendered as a `TmIcon`
  (`.tm-wd-toolbox__item-icon`); the mini stencil SVG (`.tm-wd-toolbox__item-preview-paper`) remains only as the fallback when the
  icon is missing or not drawable. Search and filters are unchanged. A selector on `.tm-wd-toolbox__item-preview-paper` must accept
  tiles without it.
  Before: `page.Locator('.tm-wd-toolbox__item-preview-paper').First` (every row had a preview).
  After: `page.Locator('.tm-wd-toolbox__item[data-component-type="TmButton"]')` (key off the item, not its preview; the icon is `.tm-wd-toolbox__item-icon`).
- **Wireframe inspector with an empty selection shows Canvas / Page / Grid instead of a hint (additive).**
  Device presets (Desktop 1440×900, Laptop 1280×800, Tablet 1024×768, iPhone 14 390×844, Android 360×800, Custom) push the undoable
  `ResizeCanvasCommand`; the Page section renames the active page; with a selection, *Element* has a **Name** field and a **Delete element**
  button. New `TmWireframePropertiesPanel` parameters: `ShowGrid` / `ShowGridChanged`, `GridSize` / `GridSizeChanged`, `OnCanvasResized`,
  `OnDelete` (all optional; without `OnDelete` the panel pushes `RemoveElementsCommand` itself).
- **New `WireframeElement.Name` (additive, `string?`).** An optional human-readable element name: serialised as `name` (omitted when
  null, so existing documents are byte-identical), shown in the inspector, used as the canvas element's `aria-label` and tooltip, set and
  returned by the MCP wireframe tools (`name` on `addElement` / `updateElement` / the author payload, in `wireframe_get_document`, the
  implementation brief and the authoring guide) and described in `wireframe-document.schema.json`. A strict consumer of the
  JSON schema that rejected unknown element properties must accept `name`.
  Before: `{ "id": "a1", "type": "TmButton", "x": 10 }`. After: `{ "id": "a1", "type": "TmButton", "name": "Submit", "x": 10 }` (`name` is optional; existing documents need no migration).
- **`TmWireframeLayersPanel` DOM: the active layer is a `role="radio"` button inside a `radiogroup`, and the
  visibility/lock buttons are toggles.** Impact: a test, script or stylesheet that relied on the old structure
  must move. The list was `role="list"` of `role="listitem"` rows whose "active" control was a plain `<button>`
  with a decorative `<span>`; the eye and lock buttons flipped their `aria-label` ("Hide layer" / "Show layer")
  and carried no pressed state. The `.tm-wd-layers`, `__item`, `__add-btn`, `__active`, `__radio`,
  `__radio--checked`, `__visibility`, `__lock`, `__delete`, `__name` and `__move-section` classes are unchanged
  (the E2E selectors keep working); the row is now also `--active` / `--hidden` / `--drag-over` / `--dragging`.
  Before:
  ```html
  <div class="tm-wd-layers__list" role="list">
    <div class="tm-wd-layers__item" role="listitem" data-layer-id="l1">
      <button class="tm-wd-layers__active" aria-label="Set as active layer"><span class="tm-wd-layers__radio tm-wd-layers__radio--checked"></span></button>
      <button class="tm-wd-layers__visibility" aria-label="Hide layer">…</button>
  ```
  After:
  ```html
  <div class="tm-wd-layers__list" role="radiogroup" aria-label="Active layer">
    <div class="tm-wd-layers__item tm-wd-layers__item--active" data-layer-id="l1">
      <button class="tm-wd-layers__active" role="radio" aria-checked="true" aria-label="Set layer Content as active"><span class="tm-wd-layers__radio tm-wd-layers__radio--checked" aria-hidden="true"></span></button>
      <button class="tm-wd-layers__visibility" aria-pressed="true" aria-label="Show layer Content">…</button>
  ```
  Migrate: select the active layer with `[role="radio"][aria-checked="true"]` (or the unchanged
  `.tm-wd-layers__radio--checked`), the visibility state with `aria-pressed` on `.tm-wd-layers__visibility`
  (`true` = visible) and the lock with `aria-pressed` on `.tm-wd-layers__lock` (`true` = locked) instead of
  comparing the label text. The layer title is now sentence case (`text-transform: none`, 14px); a stylesheet that
  matched the uppercase caption should drop that assumption. `data-collapsed` is now always rendered as the string
  `"True"` / `"False"`, which is what the stylesheet matches.
- **Wireframe editor: `--tm-wd-*` are now aliases of the core `--tm-*` tokens; a consumer palette override moves to
  `--tm-*`.** `_wireframe-editor.css` no longer ships a palette of its own (`--tm-wd-accent: #3b82f6`,
  `--tm-wd-border: #d1d5db`, …). Every `--tm-wd-*` is `var(--tm-…)` (`--tm-wd-accent` = `--tm-color-primary`,
  `--tm-wd-border` = `--tm-border-color`, `--tm-wd-bg-surface` = `--tm-bg-surface`, the workspace behind the page =
  `--tm-bg-workspace`, radii = `--tm-radius-control` / `--tm-radius-card`, font = `--tm-font-sans`). Dark mode and the
  opt-in indigo theme therefore reach the editor with no editor-specific CSS. The aliases are re-declared on every
  editor surface root, so a `[data-theme="dark"]` / `.tm-dark` region nested below `<html>` resolves them too.
  Impact: a host that re-coloured the editor by overriding `--tm-wd-*` on `:root` still works for the properties it
  names (the aliases stay overridable), but the supported route is the core token, and a `--tm-wd-*` override placed
  on `:root` no longer wins over the surface-root declaration for the editor's own subtree.
  Before: `:root { --tm-wd-accent: #7c3aed; --tm-wd-border: #cbd5e1; }`
  After: `:root { --tm-color-primary: #7c3aed; --tm-border-color: #cbd5e1; }` (or, for one editor only,
  `.my-editor .tm-wd-editor { --tm-wd-accent: #7c3aed; }`).
  The wireframe PAGE is paper: `--tm-wd-canvas-bg` aliases the theme-invariant `--tm-paper` (like the diagram
  editor's page), because the stencil components are drawn light-on-paper; the SVG/PNG/PDF export therefore looks
  the same in every theme (`exportSvg` bakes the computed paints into explicit colours).

- **Wireframe editor: the dark-mode hack was removed.** `[data-theme="dark"] .tm-wd-editor { --tm-wd-…: #1e293b … }`
  and `[data-theme="dark"] .tm-wd-canvas__svg rect[fill="white"] { fill: #1e293b }` are gone - the page is no longer
  inverted (it is paper) and the chrome follows `tokens-dark.css`.
  Before: a dark theme turned every `<rect fill="white">` of the canvas SVG slate.
  After: the canvas SVG keeps explicit paper paints; override `--tm-wd-canvas-bg` (not an attribute selector) to
  change the page colour, e.g. `.tm-dark .tm-wd-editor { --tm-wd-canvas-bg: var(--tm-bg-surface); }`.

- **Wireframe editor toolbar: emoji and Unicode glyphs became `TmIcon`; icon buttons are 32 px.** The ↖ ✋ → ↩ ↪ ⬇ ⬆
  🔒 📁 📂 ⇹ ⇔ 🧲 📏 🎨 🖌️ 📐 − + ✓ ✕ ◧ ◨ ▾ glyphs inside `.tm-wd-editor__btn` (and the properties panel's align / z-order
  buttons, the toolbox search clear, the page-tab close/add) render an `svg.tm-icon` instead. Toolbar buttons are
  `var(--tm-space-8)` (32 px, was 26 px). Class names, `aria-label`s and `title`s are unchanged; the six align buttons
  of the properties panel gained an `aria-label`.
  Impact: selectors or tests that matched the glyph text (`button:has-text("✕")`) must match the accessible name.
  Before: `page.Locator("button:has-text('✓')")`
  After: `page.GetByRole(AriaRole.Button, new() { Name = "Apply" })`

- **`TmDocumentOpenDialog` no longer falls back to indigo `#4f46e5`; its colour comes from `--tm-color-primary`.**
  The stylesheet used invented aliases with hex fallbacks (`var(--tm-primary, #4f46e5)`, `--tm-primary-soft`,
  `--tm-hover-bg`, `--tm-text-color`, `--tm-danger`, `--tm-surface-2`); none of those tokens exist, so the fallbacks
  always painted. They are replaced by `--tm-color-primary`, `--tm-color-primary-subtle`, `--tm-color-surface-hover`,
  `--tm-text-primary`, `--tm-color-danger(-subtle)` and `--tm-bg-surface-secondary`.
  Before: the selected folder, list toggle and breadcrumb were indigo in every theme (and dark mode kept light fills).
  After: they use the library's primary colour - blue by default, indigo with `data-tm-theme="indigo"`, and the dark
  variant in dark mode. Hosts that want the old indigo set `data-tm-theme="indigo"` on `<html>` (or override
  `--tm-color-primary`). A selected row/card now carries a primary marker (inset bar / ring), not only a faint tint.

- **`TmDocumentOpenDialog` column headers are the grid items themselves.** The `role="columnheader"` wrappers no
  longer use `style="display:contents"` (a `display: contents` element can disappear from the accessibility tree);
  `role="columnheader"` and `aria-sort` sit on `div.tm-dod-colhead`, a direct child of `.tm-dod-list-head`.
  Impact: `.tm-dod-col-name` / `.tm-dod-col-modified` keep their classes and their parent still carries
  `aria-sort`; only an inline-style assertion on the wrapper would notice.
  Before: `<div role="columnheader" aria-sort="ascending" style="display:contents"><button class="tm-dod-col-name">`
  After: `<div class="tm-dod-colhead" role="columnheader" aria-sort="ascending"><button class="tm-dod-col-name">`

- **`TmWireframeEditor` read-only now also mutes the toolbox, and the toolbox label says "click or drag".**
  `TmWireframeToolbox` has a new `ReadOnly` parameter (the editor passes its effective state): items become
  `aria-disabled="true"`, `draggable="false"`, muted (`tm-wd-toolbox__item--disabled`) and never raise
  `OnComponentActivated`. The `TmWireframeToolbox_DragOntoCanvas` text changed from "Drag {0} onto canvas" to
  "Add {0} to canvas (or drag)" in all four resource files; hosts that override the key keep their text.
- **`TmWireframeEditor` no longer assigns `ReadOnly` / `ShowRulers`, and its Read-only toggle cannot unlock a
  host-pinned `ReadOnly="true"`.** The built-in Read-only/Edit button and the rulers button used to write
  `ReadOnly = !ReadOnly` / `ShowRulers = !ShowRulers` into the component's own `[Parameter]`s, which the host
  could neither observe nor keep - and a viewer rendered with `ReadOnly="true"` could be switched to edit mode
  by any user. They now drive internal state (`IsReadOnly` = `ReadOnly || user toggle`) and raise the new
  `ReadOnlyChanged` / `ShowRulersChanged` callbacks. With `ReadOnly="true"` and no `ReadOnlyChanged` bound the
  toggle is not rendered. `IsReadOnly` is passed to the canvas, properties panel, layers panel and context menu.
  Before: `<TmWireframeEditor @bind-Document="doc" ReadOnly="true" />` showed a toggle that unlocked editing.
  After: that editor is a pinned viewer. To let the user toggle and to track the state, bind it:
  `<TmWireframeEditor @bind-Document="doc" @bind-ReadOnly="readOnly" @bind-ShowRulers="showRulers" />`.
  Hosts that read the parameter back from the component instance after a user toggle must switch to the
  callbacks (or `editor.IsReadOnly`).
- **`tm-menu-nav` roving now includes `aria-disabled="true"` items.** Arrow keys, Home/End and
  typeahead reach an item that is only `aria-disabled` (not natively `disabled`), so its
  explanation stays reachable for a screen reader. Only items with the native `disabled`
  attribute or `hidden` are skipped. This module serves every `role=menu` surface
  (`TmDropdown`, `TmSplitButton`, `TmContextMenu`, overflow menus), not just the data table.
  The initial focus (`focusFirst`) still skips an `aria-disabled` first item and falls back to
  the first item only when every item is `aria-disabled`.
  Before: `aria-disabled="true"` items were skipped by arrow/Home/End/typeahead roving and by the initial focus.
  After: the menu opens on the first item that is not disabled in either way, and arrow keys
  stop on the `aria-disabled` item too.
  Migration: set the native `disabled` (`TmDropdownItem.Disabled`) on items that must stay out
  of the keyboard order entirely; keep `aria-disabled` only for focusable-but-unavailable
  choices that carry an explanation.

- **`TmDropdown` outline trigger is now small.** `OutlineTrigger="true"` adds `tm-btn-sm` for
  every consumer, not only a Card-mode data table.
  Before: `class="tm-dropdown-trigger tm-btn tm-btn-outline"`
  After: `class="tm-dropdown-trigger tm-btn tm-btn-outline tm-btn-sm"`

- **`ToolbarMode="SearchOnly"` hides the filter summary and the clear-all buttons.** Search-only
  owns the search box and nothing else. `TmDataTable` no longer renders
  `.tm-data-table-filter-summary` or `.tm-col-filter-clear-all` in that mode, and
  `TmMultiViewList` passes `ShowClearAll="false"` to its filter builder (the inline one and the
  cards sheet).
  Before: `ShowClearAll="true"` and the summary rendered whenever filters were active.
  After: `ShowClearAll="@(!IsContentToolbarMode && !IsSearchToolbarMode)"`, and the summary
  requires `ShowFilterSummary && !IsContentToolbarMode && !IsSearchToolbarMode`.

- **`TmMultiViewList` avatar column leads the row and turns itself on for initials.** It is now
  the first column. `AvatarFallback="Initials"` adds it to the visible set on init, and a missing
  image renders `<span class="tm-mvl-avatar-initials" aria-hidden="true">` instead of an empty cell.
  Before: the avatar column followed the date column and rendered only `<img alt="">` when a URL
  was set.
  After: `@if (_showAvatarColumn) { <th>…</th> }` is the first header, and the cell is
  `<td>@RenderAvatar(item)</td>`.

- **The active page button uses two new tokens.** `.tm-page-btn-active` no longer paints with the
  theme-flipping primary step.
  Before: `background-color: var(--tm-color-primary); color: var(--tm-text-inverse);`
  After: `background-color: var(--tm-color-primary-solid); color: var(--tm-color-on-primary-fixed);`
  The new tokens are listed under Added.

- **Dropdown-item hover and the filter-row header use the surface token.**
  Before: `background-color: var(--tm-color-gray-50);` on `.tm-dropdown-item:hover` and
  `.tm-filter-row th`.
  After: `background-color: var(--tm-bg-surface-secondary);` on both. A pinned header uses the
  same token (`.tm-data-table thead th.tm-col-pinned`), matching `.tm-data-table thead tr`.

- **`TmMultiViewList` item count is a live region.** `.tm-mvl-item-count` now carries
  `role="status"` and `aria-live="polite"`. The data-table filter summary does not: the result
  count is already announced by `.tm-data-table-live-count`, and a second polite region on the
  same filter change spoke twice.
  Before: `<span class="tm-mvl-item-count">`
  After: `<span class="tm-mvl-item-count" role="status" aria-live="polite">`

- **Card toolbar sort and filter buttons are small outline buttons.** Their own padding, border
  and background were removed; the button classes supply them.
  Before: `<button class="tm-mvl-cards-toolbar__sort">` with
  `padding: var(--tm-space-1) var(--tm-space-3); border: 1px solid var(--tm-border-color); background: var(--tm-bg-surface);`
  on `.tm-mvl-cards-toolbar__sort`, `.tm-mvl-cards-toolbar__filter` and
  `.tm-data-table-cards-toolbar__filter`.
  After: `<button class="tm-btn tm-btn-outline tm-btn-sm tm-mvl-cards-toolbar__sort">` (the same
  three classes on the list filter button and `.tm-data-table-cards-toolbar__filter`), and those
  rules no longer set padding, border or background. `TmViewManager.Compact` (see Added) puts the
  same classes on the Views toggle when the table appearance is Card.

- **`TmPagination.Compact` no longer hides the page buttons.** It used to replace the page numbers
  with "Page X of Y". It now only adds that label and keeps previous, next and the page buttons.
  Before: `Compact="true"` rendered the label and no `.tm-page-btn`.
  After: `Compact="true"` renders the label and the page buttons. A narrow data table hides its
  page numbers through `.tm-data-table-pager--responsive` (set when `MobileLayout` is not `None`),
  not through `Compact`.

- **The default-view marker in `TmViewManager` is now named.** The decorative `★` stays, with
  `aria-hidden="true"`, and a visually hidden "Default view" (`TmViewManager_DefaultView`) names it
  for assistive technology. A custom `ITmLocalizer` must supply that key. <!-- snapshot:dt-viewmanager -->

- **`TmFilterBuilder.ShowResultPreview` no longer shows a static "Preview".** Before this version,
  `ShowResultPreview="true"` rendered the literal preview placeholder even with no count source.
  It now renders a live count, and only when `ResultCountProvider` is also set. A custom
  `ITmLocalizer` must supply the plural forms of `TmFilterBuilder_ResultPreview` (`{0}`); the key
  is no longer a single static string.

  ```razor
  @* before: showed "Preview" *@
  <TmFilterBuilder ShowResultPreview="true" />

  @* after: pass a count function as well *@
  <TmFilterBuilder ShowResultPreview="true" ResultCountProvider="CountAsync" />
  ```

- **`TmDataTable` export menu on phones: sheet → popover.** With the default
  `MobileLayout = DataTableMobileLayout.None` the export dropdown now opens as an anchored
  popover on a phone instead of the bottom sheet. Hosts that relied on the sheet opt back by
  setting `MobileLayout` (`Scroll` or `Cards`), which keeps the automatic mobile presentation
  (`PanelPresentation.Auto` → sheet under the phone breakpoint); the items stay ≥ 44 px tall
  in both presentations.

- **TmDiagramEditor (mobile): the panels sheet starts closed and the minimap starts hidden.** On a
  mobile layout the canvas gets the whole stage on first render; the action bar's new *Shapes* tile,
  the *Properties* tile or a node selection opens the sheet, and the minimap is toggled from the
  header "⋯" menu. The action bar now reads Select | Shapes | Properties; *Pan* moved into *More*.
  The sheet tabs are flat — Shapes | Properties | Layers (no nested inspector tabs on mobile).
  Restore the old open-on-load sheet with `MobileSheetInitiallyOpen="true"`:
  ```razor
  @* before (2.9.1 preview): sheet peeked on load *@  <TmDiagramEditor Document="doc" />
  @* after, same look as before *@                    <TmDiagramEditor Document="doc" MobileSheetInitiallyOpen="true" />
  ```

- **DiagramEditor – `TmDiagramLayersPanel` row menu now uses the core `TmContextMenu`.**
  `.tm-diagram-layers__row-menu` is now the `TmContextMenu` wrapper. The trigger is a
  `div[role="button"]` carrying the core `.tm-context-menu__trigger` class (the inner
  `.tm-diagram-layers__row-menu-trigger` span remains, so clicks on it still bubble) — selectors
  like `button.tm-diagram-layers__row-menu-trigger` no longer match anything. The trigger's
  accessible name comes from the inner sr-only "Actions for layer {name}" span, and in
  `TmDiagramEditor` the panel root's `data-collapsed` is always `False` (the editor owns the
  collapse through its own chrome — key off that, not off the panel attribute). The item classes
  (`…row-menu-item--rename/--lock/--up/--down/--delete`, new `--move-selected`) are kept, but they
  now ride the core `tm-context-menu__item` markup, and a "Move selected nodes here" entry appears
  when nodes are selected. In read-only the menu is not rendered at all. Before:
  `.tm-diagram-layers__row-menu { … }` styled the popup. After: style the core menu
  (`.tm-context-menu`); host CSS targeting the old popup classes must move to the core classes.

- **DiagramEditor – `TmDiagramLayersPanel` accessibility tree is a list, not a radiogroup.**
  The layer list is a `ul`/`li` list (was `div role="radiogroup"`), and the active-layer control is
  a toggle button with `aria-pressed` (was `role="radio"` + `aria-checked`). Class hooks are
  unchanged. Before: `[role='radio'][aria-checked='true']`. After:
  `.tm-diagram-layers__active[aria-pressed='true']`.

- **DiagramEditor – `TmDiagramLayersPanel` row: the inline lock and delete buttons are removed in
  every mode.** Lock/Unlock and Delete live in the row's ⋯ menu, and a locked layer shows a
  non-interactive `.tm-diagram-layers__locked` indicator. The hooks `.tm-diagram-layers__lock` /
  `.tm-diagram-layers__delete` are gone. New parameter `ShowCollapseToggle` (default `true`,
  standalone rendering unchanged); `TmDiagramEditor` passes `false`, so the embedded panel fills
  the inspector and has no `>` tab.

- **DiagramEditor – recently used shapes are per editor.** The connect dialog no longer shares one
  process-wide list (on Blazor Server it leaked picks between users). New optional
  `TmDiagramEditor.RecentStencilIds` / `RecentStencilIdsChanged` let a host persist them:
  before – nothing to do (global list); after –
  `<TmDiagramEditor @bind-RecentStencilIds="_userRecents" />` to keep recents per user.

- **DiagramEditor – SQL/CSV import preview is debounced (300 ms).** The live preview no longer
  parses on every keystroke. Consumer bUnit tests that type into `TmDiagramSqlImportDialog` /
  `TmDiagramCsvImportDialog` and assert the preview immediately must register a `FakeTimeProvider`
  and advance it: before – `textarea.Input(sql); cut.Find(".tm-diagram-sql-import__preview");`
  after – `Services.AddSingleton<TimeProvider>(time); textarea.Input(sql);
  time.Advance(TimeSpan.FromMilliseconds(301)); await cut.InvokeAsync(() => {})`. The resource keys
  `TmDiagramSqlImportDialog_ErrorParse` and `TmDiagramCsvImportDialog_ErrorParse` are deprecated
  (no longer read) and will be removed in the next minor release.

- **DiagramEditor – template gallery is one flat grid and the built-in catalog loads again.** The
  per-category `<h4 class="tm-diagram-template-gallery__section-title">` headings are gone (category
  chips and the per-card category stay); the embedded built-in templates (Blank, Basic Flowchart,
  UML Class, ER, Network) were silently missing because of a resource-name bug and now appear in
  every host, with generated thumbnails also for the ArchiMate/BPMN/C4/UML 2.5 templates. Before –
  selectors on `.tm-diagram-template-gallery__section(-title)`; after – use
  `.tm-diagram-template-card__category`. Hosts that register their own "blank" template: the
  registry dedupes by id with provider priority.

- **`TmDiagramCanvas` context-menu callbacks gained a trailing `string Origin` element.**
  The pointer type that opened the menu (`"mouse"` or `"touch"`) now rides with every context-menu
  event — a touch-originated menu anchors offset from the finger instead of under it. Migration:
  widen the handler tuple on all four callbacks (the new element arrives last):
  `EventCallback<(string NodeId, double ScreenX, double ScreenY)>` →
  `EventCallback<(string NodeId, double ScreenX, double ScreenY, string Origin)>` (`OnNodeContextMenu`),
  `EventCallback<(string EdgeId, double ScreenX, double ScreenY)>` →
  `EventCallback<(string EdgeId, double ScreenX, double ScreenY, string Origin)>` (`OnEdgeContextMenu`),
  `EventCallback<(string NodeId, int Row, int Column, double ScreenX, double ScreenY)>` →
  `EventCallback<(string NodeId, int Row, int Column, double ScreenX, double ScreenY, string Origin)>` (`OnTableCellContextMenu`),
  `EventCallback<(double CanvasX, double CanvasY, double ScreenX, double ScreenY)>` →
  `EventCallback<(double CanvasX, double CanvasY, double ScreenX, double ScreenY, string Origin)>` (`OnCanvasContextMenu`).

- **`TmDiagramEditor` read-only: nodes are selectable for inspection, and a finger always pans.** In the effective read-only state a mouse click or a finger tap selects a node (Ctrl/Cmd+click toggles) — a tap on an edge selects the edge through the engine (`selectEdgeForInspection`) — so the read-only properties panel shows a plain summary (shape name, text/data fields, position/size, fill/stroke swatches; label, routing, source/target for edges); nothing can be moved, resized, rotated or connected. The Select tool stays enabled and `KeyV` keeps working in read-only (only the edge shortcuts stay blocked, and entering read-only disarms an armed edge tool); the read-only toggle keeps the constant "Read-only" label with only the icon swapping. A host read-only flip reaches the JS instance in the same parameter pass even while a JS drag latches the render gate, closes the connect dialog and any open inline editor (edge label, stencil sections, table cells) without saving, and a mouse press focuses the canvas so keyboard panning keeps working. A finger that lands on a node or an edge pans the canvas in read-only; in edit mode a finger drag that starts on an edge now pans too (a tap still selects the edge). Toolbox tiles carry a localized read-only hint (`TmDiagramToolbox_ReadOnlyHint`) as their title and a single visually-hidden `aria-describedby` description. Migration: hosts that relied on read-only clicks never changing the selection must ignore `OnSelectedIdsChanged` while read-only.

- **`TmDiagramToolbox` keyboard: Space inserts on keyup, Enter ignores auto-repeat; new `ReadOnly` parameter.** Follows docs/keyboard-activation-convention.md: Space now activates on keyup (was keydown) and a held Enter inserts exactly one shape. With `ReadOnly="true"` (the editor passes its effective read-only state) tiles stay visible and focusable but are `aria-disabled`, not draggable and insert nothing. Migration: tests that pressed Space with `KeyDown(" ")` alone must dispatch `KeyDown(" ")` + `KeyUp(" ")`.

- **Data-table defaults stay `Plain`.** `TmDataTable.Appearance` remains `Plain`, `TmFilterBuilder.Presentation` remains `Inline`, `MobileLayout` remains `None`, `KeyboardNavigation` remains `Rows`, and `ShowFilterSummary` remains `false`. Grid ARIA (`aria-rowcount`, `aria-colcount`, `role="row"`, `role="gridcell"`) renders only when `KeyboardNavigation=Grid`. The redesigned group bar, export item and no-pager "N records" summary render only when `Appearance=Card`. `CardMode` keeps `tm-data-table-wrapper--card` and `data-label`; it no longer emits `--cards`, `tm-data-table-cell--card` or forces `EditActionsMode.Text`. Those belong to `MobileLayout=Cards`.
- **Approved accessibility markup, recorded against the pre-redesign snapshots.**
  - `TmColumnPicker` fills `span.tm-icon-columns` with a `TmIcon` instead of an empty span. <!-- snapshot:dt-basic --> <!-- snapshot:dt-cardmode --> <!-- snapshot:dt-filters --> <!-- snapshot:dt-grouping --> <!-- snapshot:dt-viewmanager --> <!-- snapshot:dt-paged --> <!-- snapshot:dt-selectable --> <!-- snapshot:dt-export --> <!-- snapshot:dt-virtualized -->
    Before: `<span class="tm-icon-columns"></span>`
    After: `<span class="tm-icon-columns"><svg class="tm-icon">…</svg></span>`
  - `TmViewManager` items are buttons inside `.tm-view-item-row`, the panel is a `TmOverlayPanel` (the `.tm-overlay` backdrop is gone) and the toggle is `tm-btn-outline` with `aria-expanded` / `aria-haspopup="dialog"`. <!-- snapshot:dt-viewmanager -->
    Before: `<button class="tm-btn tm-btn-secondary tm-view-manager-toggle">`
    After: `<button class="tm-btn tm-btn-outline tm-view-manager-toggle" aria-expanded="true" aria-haspopup="dialog">`
    View item before/after (exact markup from `git show 259f900a:src/Tempo.Blazor/Components/DataTable/TmViewManager.razor`):
    Before:
    ```html
    <div class="tm-view-item tm-view-item--active" @onclick="() => ApplyViewAsync(view)">
      <span class="tm-view-item-name" title="@view.Name">@view.Name</span>
    </div>
    ```
    After:
    ```html
    <div class="tm-view-item-row">
      <button type="button" class="tm-view-item tm-view-item--active" aria-current="true" @onclick="() => ApplyViewAsync(view)">
        <span class="tm-view-item-name" title="@view.Name">@view.Name</span>
      </button>
    </div>
    ```
    Backdrop before/after (exact markup from the same commit):
    Before:
    ```html
    <div class="tm-overlay" @onclick="CloseDropdown"></div>
    ```
    After: no backdrop element — the panel renders as a `TmOverlayPanel` in the browser top layer (click-outside closing is handled by the overlay panel).
  - `TmViewManager` announces a completed Set-as-default in a visually-hidden polite live region inside the panel, and each view row carries a stable id (`{toggle-id}-view-{view-id}`) used to restore focus after the row list re-renders. <!-- snapshot:dt-viewmanager -->
    Before: `<div class="tm-view-manager-panel"><div class="tm-view-section">` and `<button type="button" class="tm-view-item">`
    After: `<div class="tm-view-manager-panel"><div class="tm-sr-only" role="status" aria-live="polite"></div><div class="tm-view-section">` and `<button type="button" id="…-view-…" class="tm-view-item">`
  - Group toggles and the group-chip remove control use `TmIcon` plus `aria-expanded` / `aria-label` instead of `▼`, `▶` and `×`. <!-- snapshot:dt-grouping --> <!-- snapshot:mvl-grouped -->
    Before: `<button class="tm-data-table-group-chip-remove">×</button>`
    After: `<button class="tm-data-table-group-chip-remove" aria-label="Remove grouping by Role"><svg class="tm-icon">…</svg></button>`
  - A paged `TmDataTable` states "Page X of Y" in a `span.tm-data-table-page-label` inside `.tm-pagination-center`. The label is hidden above the mobile breakpoint and revealed by the table's own container query, so the page buttons are never removed. <!-- snapshot:dt-paged -->
  - `TmPagination` previous and next buttons use `TmIcon` chevrons instead of `‹` / `›`. <!-- snapshot:pagination-default --> <!-- snapshot:dt-paged -->
    Before: `<button aria-label="Previous">‹</button>`
    After: `<button aria-label="Previous"><svg class="tm-icon">…</svg></button>`
  - `TmColumnFilter` names the input with the column title (`Filter: {0}`) when the column has one. <!-- snapshot:dt-filters -->
    Before: `<input placeholder="Filter" aria-label="Filter" />`
    After: `<input placeholder="Filter: Name" aria-label="Filter: Name" />`
  - `TmDataTable` announces the result count in a visually-hidden polite live region (`div.tm-data-table-live-count`), so a filter or page change is spoken without moving focus. <!-- snapshot:dt-basic --> <!-- snapshot:dt-cardmode --> <!-- snapshot:dt-filters --> <!-- snapshot:dt-grouping --> <!-- snapshot:dt-viewmanager --> <!-- snapshot:dt-paged --> <!-- snapshot:dt-selectable --> <!-- snapshot:dt-export --> <!-- snapshot:dt-virtualized -->
  - `TmDataTable` with `ShowFilterSummary="true"` makes the active-filter count a polite live region (`span[role="status"][aria-live="polite"]`) and always renders a visually-hidden twin region (`div.tm-data-table-filter-summary-live`) while filters are allowed, so the first filter change is announced too. No golden snapshot renders the summary (it needs the opt-in `ShowFilterSummary`), so the golden files are unchanged.
    Before: `<div class="tm-data-table-filter-summary"><span>Filters: 1</span>…</div>` (only while a filter was already active; not announced)
    After: `<div class="tm-data-table-filter-summary"><span role="status" aria-live="polite">Filters: 1</span>…</div>` plus `<div class="tm-data-table-filter-summary-live tm-sr-only" role="status" aria-live="polite"></div>` rendered from the start.
  - A void checkbox renders its closing tag. <!-- snapshot:dt-selectable -->
    Before: `<input type="checkbox" /></th>`
    After: `<input type="checkbox"></input></th>`
  - `TmMultiViewList` status text is a `TmBadge`, the view switcher is a `radiogroup` named "View" (not "Table view") with one roving `tabindex`, and the group picker button exposes `aria-expanded` / `aria-haspopup`. <!-- snapshot:mvl-table --> <!-- snapshot:mvl-card --> <!-- snapshot:mvl-list --> <!-- snapshot:mvl-grouped -->
    Before: `<span class="tm-mvl-status">Active</span>`
    After: `<span class="tm-badge tm-mvl-status">Active</span>`
  - View switcher before/after (exact markup from `git show 259f900a:src/Tempo.Blazor/Components/DataDisplay/TmMultiViewList.razor`):
    Before:
    ```html
    <div class="tm-mvl-switcher">
      <button type="button" class="tm-mvl-switch-table tm-mvl-switch--active" aria-label="Table view" @onclick="…"><span>☐</span></button>
    ```
    After:
    ```html
    <div class="tm-mvl-switcher" role="radiogroup" aria-label="View">
      <button type="button" class="tm-mvl-switch-table tm-mvl-switch--active" role="radio" aria-checked="true" aria-label="Table view" tabindex="0" @onclick="…" @onkeydown="…"><span>☐</span></button>
    ```
    The group label changed from the per-button "Table view" phrasing to one group label "View" (`TmMvl_ViewSwitcher`), and exactly one button carries `tabindex="0"` (roving tabindex).

- **`ExportSelectedOnly` was renamed to `AllowExportSelected`.** <!-- snapshot:dt-export -->
  Before:
  ```razor
  <TmDataTable Data="_items" Selectable="true" ExportSelectedOnly="true" … />
  ```
  After:
  ```razor
  <TmDataTable Data="_items" Selectable="true" AllowExportSelected="true" … />
  ```
  Set the new parameter (and keep the table `Selectable`) to export the checked rows only; the export menu then offers the scope choice, and `DataTableExportResult.Scope` reports which one ran.

- **`TmOverlayPanel.RestoreFocusTargetId` was removed.** Focus returns to the anchor; do not pass the old parameter.
  Impact: an outside click on dead space now leaves focus where the user clicked (on `<body>`) instead of
  restoring it to the anchor; Escape still returns focus to the anchor. The same change is described in the
  Fixed entry of this version (see "TmOverlayPanel no longer takes a focus-restore id").
  Before:
  ```razor
  <TmButton @ref="_trigger">Options</TmButton>
  <TmOverlayPanel Anchor="@_trigger" RestoreFocusTargetId="options-trigger" … />
  ```
  After:
  ```razor
  <TmButton @ref="_trigger">Options</TmButton>
  <TmOverlayPanel Anchor="@_trigger" … />
  ```

- **`TmDiagramEditor` mobile presentation (DiagramEditor redesign phase 7, G2): a container below
  768px now renders the dedicated mobile layout, replacing the old `max-width: 640px` viewport
  stack (toolbox fixed to 280px above the canvas, the inspector hidden).** The mobile layout is
  container-driven (`data-editor-layout="mobile"` mirrors the resolved shell layout) and renders
  through the core shell: a compact header (document title, icon-only Undo/Redo, the "⋯"
  view-settings menu — search and the Read-only toggle moved into that menu, the docked-panel
  toggles are gone because the panels live in the sheet), horizontally scrollable page tabs, the
  full-area canvas with a floating zoom pill (`+ / % / − / fit`,
  `.tm-diagram-editor__zoom-overlay`) and the floating minimap, a **non-modal bottom sheet**
  (peek/half/full snaps) presenting Shapes | Inspector (the inspector keeps its Properties |
  Layers tabs), and the core F5 action bar **Select | Pan | Properties | More**. The "More" menu
  carries the export commands only — layout and import stay tablet+; the long-press context menu
  presents as the shared F3 bottom sheet (`.tm-diagram-editor__context-sheet`) instead of the
  `position:fixed` menu, without the align/distribute (node menu) and auto-layout (canvas menu)
  entries; resize/rotate handles and quick-connect arrows are not rendered
  (`TmDiagramCanvas.ShowTransformHandles` / `ShowConnectArrows` new, default `true`). A host-made
  viewer — pinned `ReadOnly="true"` or a toolbar group set without editing groups (e.g. the
  `Viewer` preset) — renders no action bar or panels sheet on mobile — only the floating zoom and
  minimap; the user's own Read-only toggle keeps them (read-only panels).

  DOM/CSS impact and migration:

  ```html
  <!-- before (<640px viewport): viewport media query; toolbox 280px, inspector display:none -->
  <div class="tm-diagram-editor__toolbox-wrap">…</div>
  <!-- .tm-diagram-editor__right-stack hidden by @media (max-width: 640px) -->

  <!-- after (<768px container): the shell's mobile presentation -->
  <div class="tm-diagram-editor" data-editor-layout="mobile">
    <div class="tm-diagram-editor__zoom-overlay">…</div>
    <div class="tm-diagram-editor__minimap-overlay">…</div>
    <div class="tm-editor-shell__stage tm-editor-shell__stage--sheet-open">
      <div class="tm-editor-shell__sheet tm-drawer--inline tm-drawer--bottom">…</div>
    </div>
    <div class="tm-mobile-action-bar" data-layout="mobile">…</div>
  </div>
  ```

  Host CSS keyed on `@media (max-width: 640px)` around the editor must branch on
  `data-editor-layout='mobile'` instead (a narrow editor inside a wide window keeps the desktop
  presentation). The old viewport media block in the package CSS was replaced by
  `[data-editor-layout='mobile']` rules with the same toolbox tightening. Performance note: the
  sheet interactions (open, tab switch, snap) do not re-render `TmDiagramCanvas` — the editor
  marks those passes chrome-only and the canvas skips them (its render gate never skips a
  content-affecting pass).

- **`TmDiagramEditor` tablet and touch layout (DiagramEditor redesign phase 6, DI3): the editor
  now resolves its layout from its own container width, and a tablet container changes the panel
  chrome.** New parameters: `LayoutMode` (`Auto` default — the editor measures itself; forced
  `Tablet`/`Desktop`/`Mobile` pins the presentation, e.g. for tests), `LayoutBreakpoints` (null =
  the editor's own band — tablet from 768px, desktop from 1200px; pass
  `new TmLayoutBreakpoints(640, 1024)` to follow the shared `TmBreakpoints` instead) and
  `MinimapPlacement` (`Auto` default — the right-column minimap card on desktop, a floating
  overlay over the canvas corner from tablet down; `Panel`/`Overlay` force the choice).

  From tablet down (container 768–1199 with the defaults) the shell keeps **at most one docked
  panel expanded** — expanding the palette rails the inspector and vice versa — and the palette
  renders as a 44px rail by default (a labelled grid button beside the shell's chevron expands
  it; expanded, it renders as a grid of tiles). Selecting a node raises the inspector back up.

  DOM/CSS impact and migration:

  ```html
  <!-- 2.9.0: minimap wrapped in the right column -->
  <div class="tm-diagram-editor__minimap-wrap">…</div>

  <!-- 2.9.1 desktop (MinimapPlacement=Auto/Panel): docked card -->
  <div class="tm-diagram-editor__minimap-card">…</div>

  <!-- 2.9.1 from tablet down: the palette is a rail, the minimap floats over the canvas -->
  <div class="tm-editor-shell__rail tm-editor-shell__rail--left" data-region="rail-left">…</div>
  <div class="tm-diagram-editor__minimap-overlay">…</div>
  ```

  Host CSS keyed on `.tm-diagram-editor__minimap-wrap` (2.9.0) must move to
  `.tm-diagram-editor__minimap-card` **and** `.tm-diagram-editor__minimap-overlay` (or pin
  `MinimapPlacement="DiagramMinimapPlacement.Panel"` to keep a single root).

  The editor root now carries `data-editor-layout="desktop|tablet|mobile"` (mirrors the resolved
  shell layout) — branch host CSS on that attribute; the editor root is a named inline-size
  container (`tm-diagram-editor`), so `@container tm-diagram-editor (…)` with the shared
  TmBreakpoints literals works too. Whether that container (or the mobile shell stage, a
  `container-type: size` box) becomes the containing block of `position: fixed` descendants
  differs between engines, so the engine measures it at runtime: the context menu and the
  table-inserter popover are translated and clamped into the editor box when the editor is the
  containing block, and clamped to the viewport otherwise. Hosts that repositioned them must
  not assume viewport coordinates — read the element's `getBoundingClientRect()` instead.

- **`TmDiagramEditor` markup classes removed by the redesign (2.9.0 → 2.9.1).** Host CSS or
  tests keyed on these 2.9.0 classes must migrate:

  | 2.9.0 selector | 2.9.1 replacement |
  |---|---|
  | `.tm-toolbar-btn--active` on toggle buttons (tools Select/Pan/Edge, panel toggles, grid/snap/page/rulers, Read-only) | `[aria-pressed="true"]` on the same button (`button[aria-pressed='true']` scoped to `[data-tm-group=…]`) |
  | `.tm-diagram-editor__body` (the toolbox / canvas / right-column row) | gone — the shell regions: `.tm-editor-shell__main`, `[data-region="canvas"]`, `[data-region="left"]` / `[data-region="right"]` |
  | `.tm-diagram-editor__minimap-wrap` | `.tm-diagram-editor__minimap-card` (docked) or `.tm-diagram-editor__minimap-overlay` (floating) — see `MinimapPlacement` |
  | `.tm-diagram-editor__toolbar-spacer` | gone — the command row is a core `TmToolbar`; header items are laid out by `.tm-diagram-editor__header-bar` |
  | `.tm-diagram-insert-dropdown-wrapper` | gone — the Insert command is a `TmToolbarItem` inside `[data-tm-group='insert']` |
  | `.tm-diagram-template-card__tags` | `.tm-diagram-template-card__meta` (row) containing one `.tm-diagram-template-card__tag` per tag |
  | `.tm-diagram-template-gallery__header` | `.tm-diagram-template-gallery__toolbar` (see the gallery entry above) |
  | Enter on a `TmDiagramTemplateCard` selected it | Enter (and double-click) raise `OnActivateRequest` = create; Space/click still select (see the card entry above) |
  | inline `.tm-alert.tm-alert--danger` + `.tm-pivot-btn` dismiss for import/export errors | core `TmAlert` (`Severity=Error`, `Dismissable`); the `tm-alert` classes are still rendered by the component, the dismiss button is TmAlert's own |

  ```css
  /* before (2.9.0) */
  .tm-diagram-editor__toolbar-group .tm-toolbar-btn--active { … }
  /* after (2.9.1) */
  .tm-diagram-editor [data-tm-group] button[aria-pressed="true"] { … }
  ```

- **`TmDiagramCanvas` resize/rotate handles start from Pointer Events with enlarged invisible hit
  areas (phase 6).** Each visible 8×8 handle square is now decorative (`pointer-events: none`) and
  is covered by an invisible hit rect/circle (`.tm-diagram-resize-handle-hit` /
  `.tm-diagram-rotate-handle-hit`) that carries `@onpointerdown`. The hit size is the CSS variable
  `--tm-diagram-handle-hit` on `.tm-diagram-node-handles` — `24px` by default, `44px` on a coarse
  pointer — in SVG user units, so it scales with canvas zoom. Mouse behaviour is unchanged (the C#
  resize machine and `startRotate` keep running for the mouse); touch/pen resize and rotation are
  driven by the new `diagram-editor-touch.js` over the core `pointer-drag.js` and report through
  the new `OnResizeStarted`/`OnResizeMoved`/`OnResizeEnded`/`OnResizeCancelled` interop points.
  Host CSS that attached its own handlers to `rect.tm-diagram-resize-handle` must move to the
  hit rects (or the handles group).

  ```css
  /* before: the visible square carried the gesture */
  .tm-diagram-resize-handle { pointer-events: auto; }
  /* after: the invisible hit area carries it, sized by the variable */
  .tm-diagram-node-handles { --tm-diagram-handle-hit: 24px; }
  @media (pointer: coarse) { .tm-diagram-node-handles { --tm-diagram-handle-hit: 44px; } }
  ```

- **`TmDiagramCanvas` touch stack rewritten on the shared pointer helpers (phase 6).** The
  hand-rolled `touchstart/touchmove/touchend` handlers, the custom pinch and the custom 600ms
  long-press are gone; every touch gesture is a session of the core `pointer-drag.js` /
  `pointer-gesture.js` in the new `diagram-editor-touch.js` module. Behaviour notes for hosts:
  two-finger pinch now also **pans** (previously it only zoomed) and both gestures work while
  read-only; the long-press context menu opens when the 600ms hold completes (moving the finger
  first drags the node and never opens the menu — before, moving cancelled the menu but the drag
  state had already been armed at `touchstart`); a tap on an edge hit path now selects the edge so
  tablets can edit it. Plain-wheel zoom is unchanged (`attachPinch`'s ctrl+wheel stays off). The
  engine gained `tmDiagramEditor.relayout(container)` — after host chrome around the canvas
  changes, it re-measures and reports `OnViewportChanged`/`OnZoomChanged` without a canvas
  re-render (the editor calls it when a docked panel expands, collapses or resizes).

- **`TmDiagramToolbox` stencil tiles insert on tap/click (phase 6).** A tap (or a desktop click —
  a drag is never the only way to insert, WCAG 2.5.7) on a stencil tile inserts it at the visible
  viewport centre through the same path the keyboard uses; tapping an edge stencil arms the edge
  tool instead of inserting. Drag-and-drop keeps working unchanged. `TmDiagramMinimap` is driven
  by Pointer Events (`onpointerdown/move/up`) instead of mouse events and its SVG carries
  `touch-action: none`, so the viewport rect can be dragged with a finger.

- **`TmDiagramLayersPanel` row "⋯" menu (phase 6).** Every layer row gained a "⋯" trigger
  (`.tm-diagram-layers__row-menu-trigger`) with Rename / Move up / Move down / Delete — the
  non-drag alternative to the layer drag-and-drop; Move pushes the same undoable
  `ReorderLayersCommand` the drop does. On `(hover: none)` pointers the delete affordance is
  always visible and on `(pointer: coarse)` the row controls grow to the 44px touch target.

- **`TmDiagramEditor` desktop restructure (DiagramEditor redesign phase 4): the two toolbar rows
  became header + single command row + status bar, and the inspector grew "Properties | Layers"
  tabs.** The editor now renders through the core `TmEditorShell` (header / toolbar / left /
  canvas / right / status-bar regions). What moved:

  | Group (`data-tm-group`) | Before (2.9.1 phase 1) | After (phase 4) |
  |---|---|---|
  | `history` `clipboard` `tools` `format` `insert` `arrange` `layout` `export` `import` `templates` | toolbar row 1 / row 2 | one command row (core `TmToolbar`, Priority overflow into its "More" menu, labels below icons) |
  | `search` | toolbar row 1 | header, named "Search" toggle |
  | `panels` | toolbar row 1 | header, named toggles (Tools / Properties / Layers / Minimap) |
  | `view` `canvas-size` | toolbar row 2 | header "⋯" menu (`.tm-diagram-editor__view-menu-panel`, rendered hidden in the DOM when closed) |
  | `zoom` | toolbar row 2 | status bar (`.tm-diagram-editor__statusbar`), `.tm-diagram-editor__zoom-label` preserved |
  | `readonly` | toolbar row 2 | status bar |

  DOM/CSS impact and migration:

  ```html
  <!-- before: groups lived inside the toolbar rows -->
  <div class="tm-diagram-editor__toolbar">
    <div class="tm-diagram-editor__toolbar-row">…[data-tm-group=search]…</div>
    <div class="tm-diagram-editor__toolbar-row">…[data-tm-group=zoom]…</div>
  </div>

  <!-- after: header bar + command toolbar + status bar -->
  <div class="tm-diagram-editor__header-bar">…[data-tm-group=search][data-tm-group=panels]…⋯</div>
  <div class="tm-diagram-editor__toolbar">…command groups only…</div>
  <div class="tm-diagram-editor__statusbar">…[data-tm-group=zoom][data-tm-group=readonly]…</div>
  ```

  Host CSS keyed on `.tm-diagram-editor__toolbar-row`, on the position of a group inside the
  toolbar, or on `.tm-diagram-editor__toolbar [data-tm-group='zoom']` must move to the new
  regions — query `[data-tm-group]` from the editor root (`[data-testid='diagram-editor']
  [data-tm-group]`), never positionally. `flex-wrap: wrap` on the old toolbar rows is gone:
  overflow is the core `TmToolbar` "More" menu (`data-tm-toolbar-item`), so a command that
  collapsed can be `inert` until it is opened from the menu. `__toolbar-row` no longer exists;
  `__props-wrap`, `__layers-wrap`, `__right-stack` and `__right-wrap` remain as aliases inside
  the inspector, and `__toolbox-wrap`/`__minimap-card` wrap the docked panels. The toolbar
  toggle buttons keep their tooltips ("Toggle toolbox", "Toggle inspector",
  "Toggle minimap"; see the Inspector toggle entry), so attribute selectors keep working; the new
  visible labels are `TmDiagramEditor_Panel*` keys. The Properties/Layers panels now render behind
  inspector tabs (`TmTabs`) — an inactive tab's panel is not in the DOM; opt out with the new
  `InspectorLayout="DiagramInspectorLayout.Stacked"`. With only one panel available
  (`ShowPropertiesPanel`/`ShowLayersPanel`), it renders alone in `__right-wrap` without the tab
  strip.

  `ShowToolbar="false"` keeps its pre-redesign meaning: it hides every control that used to live
  in the toolbar — now the header bar's built-in controls (title, search/panel toggles, "⋯" menu),
  the command row and the status bar (and on mobile the zoom pill and the action bar). Page tabs,
  panels, canvas, minimap and the `HeaderStart`/`HeaderActions` host slots stay. This restores
  2.9.0 behaviour (not a new break).

  The Properties panel's order/grouping/collapse/lock actions moved into a dedicated
  **Actions** section (with a Delete button raised through `OnDeleteRequested`). The collapsed
  Import group offers SQL/CSV entries in "More"; the JSON file picker stays on the bar item
  (a browser file dialog cannot be opened from a menu entry), and the Import group is pinned to
  the bar (`Priority=Pinned`) at every toolbar width. New parameters: `InspectorLayout`
  (`Tabs` default / `Stacked` opt-in), `ShowStatusBar` (default `true`), `ShowTitle`
  (default `true` — hide the document title through the API, never through CSS).

- **TmDiagramEditor header: the *Properties* and *Layers* toggles were merged into one *Inspector* toggle.** It shows/hides the whole inspector (on tablet it rails it); the inspector's Properties | Layers tabs choose the content. Below 1024px of *editor* width (a container query on the editor root, not the whole tablet band) the header toggles are icon-only — the visible name stays as `aria-label`, the tooltip in `title`. The accessible names "Toggle properties panel" and "Toggle layers" are gone — migrate selectors to the new stable test ids:
  ```text
  before: button[title='Toggle layers'], button[title='Toggle properties panel'], GetByRole(Button, "Toggle layers")
  after:  [data-testid='diagram-editor-toggle-inspector']   (also: diagram-editor-toggle-toolbox, diagram-editor-toggle-minimap)
  ```

- **`TmDiagramEditor`: the built-in Read-only/Edit toggle no longer overwrites the `[Parameter]
  ReadOnly` and disappears while the host pins read-only (DI1).**
  Previously clicking the toolbar's Read-only/Edit toggle assigned the `ReadOnly` parameter itself, so
  a host that pinned `ReadOnly="true"` (for example a read-only preview surface) could be unlocked
  from the UI — the parameter was silently rewritten to `false`. The toggle now flips an internal
  user state instead, exposed by the new public `IsReadOnly` property (`ReadOnly || user-toggle`),
  and every editing path (toolbar, keyboard shortcuts, context menu, import, templates, paste, canvas
  interop) is gated on that effective state. The toggle is not rendered at all while the host sets
  `ReadOnly="true"` or when the `readonly` toolbar group is disabled.

  ```razor
  @* before: the toggle overwrote the parameter — a pinned read-only viewer could unlock itself *@
  <TmDiagramEditor Document="@document" ReadOnly="true" />
  @* clicking the toolbar "Edit" button set ReadOnly=false on the component *@

  @* after: the host's ReadOnly=true cannot be overridden — the toggle is not even rendered *@
  <TmDiagramEditor Document="@document" ReadOnly="true" />

  @* hosts that want a user-facing lock keep the default ReadOnly="false"; the toggle works as
     before, never writes back to the parameter, and the effective state is readable: *@
  <TmDiagramEditor @ref="editor" Document="@document" />
  @code { var locked = editor.IsReadOnly; }
  ```

  The same release introduces the stable toolbar contract for nested hosts: `ToolbarPreset`
  (`Full` is the default and keeps today's look; `Viewer` renders only zoom, view, search and
  export), `ToolbarGroups` (flags that override the preset), the `HeaderStart`/`HeaderActions`
  slots, and a `data-tm-group` attribute on every toolbar group. Model it into your CSS/anchors —
  never against the toolbar's internal DOM. The Modeling diagram preview now uses
  `ToolbarPreset="Viewer"` instead of hiding editor controls with positional `nth-of-type` CSS
  selectors.

- **`TmDiagramEditor` connect modal became the `TmDiagramConnectDialog` component and defaults to
  the source node's stencil set (DI4).** Previously the "choose a shape to connect" modal listed
  every node stencil in the library (~150 tiles at once) with no search. The new dialog offers by
  default only the node stencils of the source node's own set (declared `SetId`, else the id's
  family prefix — a UML node offers UML shapes, a `general.rectangle` offers the 12 "general"
  shapes), with a search field (reusing `DiagramStencilRegistry.Search`), a "Show all" toggle as
  the explicit opt-in to the whole node library, recently-used tiles floated first, a roving
  arrow-key grid (no tile preselected) and a mobile bottom-sheet presentation with a Close footer
  button; edge stencils stay excluded everywhere. On a phone the tiles present as plain rows
  (no chevrons). Focus returns to the canvas through the modal's public focus scope
  (`RestoreFocusTarget`).

  ```html
  <!-- before: .tm-diagram-connect-modal__grid held EVERY node stencil, desktop tiles only -->
  <div class="tm-diagram-connect-modal__grid"> … ~150 tiles … </div>

  <!-- after: toolbar (search + "Show all") and the scoped palette; list rows below 640px,
       a .tm-diagram-connect-modal__close footer button on the mobile sheet -->
  <div class="tm-diagram-connect-modal__toolbar">…search…show-all…</div>
  <div class="tm-diagram-connect-modal__grid">…palette tiles…</div>
  ```

  Host CSS keyed on the bare `__grid` showing the whole library, or asserting
  `items.Count == all node stencils`, must opt into the full list first (click
  `.tm-diagram-connect-modal__show-all`) or scope to the palette. The old
  `TmDiagramEditor_SelectStencilTitle` resource key is reused unchanged.

- **`TmDiagramTemplateCard` keyboard contract: Enter (and double-click) now CREATE, Space keeps
  the selection tap.** The gallery mockup's shortcut is create-without-detour: `TmDiagramTemplateCard`
  gained an `OnActivateRequest` callback raised by double-click and by the Enter keydown
  (repeat-guarded); single click and Space still raise `OnClick` (select). Host code that relied
  on Enter only selecting must handle `OnActivateRequest` (or ignore it) — `TmDiagramTemplateGallery`
  wires it to create immediately. Cards also render the localized category chip
  (`.tm-diagram-template-card__category`) next to the tags and the whole meta row is clamped to
  two lines.

- **`TmDiagramTemplateGallery` and the import dialogs are localized through Id-derived keys and
  present through the foundation `TmModal` contract.** Template names resolve via
  `TmDiagramTemplate_<id with - → _>` and categories via
  `TmDiagramTemplateCategory_<name>` (fallback: the raw values — the `DiagramTemplate` model is
  unchanged), so a Czech gallery shows "Prázdný diagram" / "Obecné" instead of "Blank Diagram" /
  "General". The gallery dialog is `Size=XLarge` with the search field and the category chips in
  one toolbar row (`.tm-diagram-template-gallery__toolbar` replaces `__header`), the CSV mapping
  form is a two-column label|select row list (the type and layout selects span the full dialog
  width), the CSV preview pins its header row and first column, and all four dialogs pass
  `FooterLayout=Inline` plus the viewport-driven mobile presentation (bottom sheet under 640px).
  Host CSS on `.tm-diagram-template-gallery__header` or the stacked CSV mapping rows must move to
  the new structure.

- **The duplicate page-tab button has its own `tm-diagram-editor__page-tab-duplicate` class
  (DiagramEditor redesign phase 2).** The duplicate action previously reused the close button's
  class, so CSS targeting `.tm-diagram-editor__page-tab-close` silently styled both buttons; the
  icons also moved from raw glyphs to `TmIcon`.

  ```razor
  @* before: duplicate carried the close-button class *@
  <button class="tm-diagram-editor__page-tab-close">⧉</button>

  @* after: each action has its own class (close keeps the danger-red hover) *@
  <button class="tm-diagram-editor__page-tab-duplicate"><TmIcon Name="files" /></button>
  <button class="tm-diagram-editor__page-tab-close"><TmIcon Name="x" /></button>
  ```

  **Migration:** host CSS that wanted to catch both buttons keeps working unchanged; CSS that
  meant to style only the close button must not rely on `__page-tab-close` matching duplicate
  anymore — target both classes explicitly.

- **Column filter accessible name and the view-scope radio group name changed when a caption is set.**
  A `TmColumnFilter` (and the inline filter `TmDataTable` renders) that receives `ColumnTitle`
  announces `Filter: {column}` (`TmDataTable_FilterColumn`) instead of the generic `Filter`.
  Without `ColumnTitle` the label is unchanged.

  ```html
  <!-- before -->
  <input placeholder="Filter" aria-label="Filter" />
  <input type="radio" name="viewScope" />

  <!-- after -->
  <input placeholder="Filter: Name" aria-label="Filter: Name" />
  <input type="radio" name="tm-view-scope-{instanceId}" />
  ```

  A selector `[name=viewScope]` no longer matches. `TmDataTableColumn.FilterOperator` is additive
  and defaults to null, which still sends `contains` — server providers are unchanged until a
  column opts in.

- **`TmDashboard` widget move/resize no longer uses HTML5 drag-and-drop or mouse events — the
  gesture is the shared `pointer-drag.js` helper (Pointer Events: mouse, touch and pen).**
  Previously moving a widget only worked with a mouse (HTML5 `dragstart`/`dragover`/`drop`) and
  resizing only with mouse events; a finger did nothing. Now a finger, a pen and the mouse all
  drag and resize through the same core helper the Scheduler uses, Escape/pointercancel roll the
  widget back, and the drop preview follows the pointer. Host impact:

  ```html
  <!-- before: nothing else needed — dashboard.js attached its own dragstart/mouse handlers -->
  <script src="_content/Tempo.Blazor/js/dashboard.js"></script>

  <!-- after: the same single script tag; pointer-drag.js is NOT a new script tag —
       dashboard.js loads it with a dynamic import() from its own URL (same folder) -->
  <script src="_content/Tempo.Blazor/js/dashboard.js"></script>
  ```

  Hosts synthesizing the old events in tests must dispatch Pointer Events instead
  (`pointerdown`/`pointermove`/`pointerup` on the `.tm-widget-drag-handle` /
  `.tm-widget-resize-handle`, bubbling to the grid). The widget header no longer renders
  `draggable`, and the Razor surface carries no `@ondragstart/@ondragend/@onmousedown` handlers;
  the resize clamps through the new `data-min-w/data-max-w/data-min-h/data-max-h` attributes the
  component renders from the `WidgetDefinition` (grid cells; max `0` = unlimited). The JSInvokable
  contract (`OnGridPositionChanged`/`OnWidgetResized`), `window.tempoDashboard.init/destroy` and
  the keyboard grab mode are unchanged. `touch-action: none` now lives on the handles only, so
  the page keeps scrolling everywhere else.

- **`TmDashboard`'s drag handle is now a `<button>` (F4 keyboard alternative), not a
  `<span>`.** The handle keeps the class `tm-widget-drag-handle` and the grip icon, but hosts
  styling or scripting the element must account for the element change:

  ```css
  /* before (2.9.0) — handle was a span */
  span.tm-widget-drag-handle { display: block; }

  /* after (2.9.1) — handle is a button: the native reset is built in; keep host overrides on
     the class, not on the span element selector */
  .tm-widget-drag-handle { display: inline-flex; }
  ```

  ```js
  // before: querySelector('span.tm-widget-drag-handle')  → null now
  // after:  querySelector('button.tm-widget-drag-handle')
  ```

- **`TmWidgetSelector` card visuals changed (F6): the plus indicator is always visible and an
  already-added single-instance card is no longer dimmed.** Previously the 24 px plus floated in
  at `opacity: 0` until hover/focus (invisible on touch), and a non-addable card wore a 0.5
  opacity veil. Now the plus is a permanently painted 32 px ringed circle (≥ 44 px target under
  `pointer: coarse` and on the mobile layout), the added state is a green "✓ Přidáno" badge
  instead of the veil, and the icon tile is a 56 px pastel `color-mix` of the category colour.
  Hosts overriding `.tm-widget-card-add` / `.tm-widget-card--disabled` opacity rules must drop
  them:

  ```css
  /* before (2.9.0) */
  .tm-widget-card-add { opacity: 0; }
  .tm-widget-card--disabled { opacity: 0.5; }

  /* after (2.9.1) — visibility is the default; the badge carries the added state */
  .tm-widget-card-add { /* always painted */ }
  .tm-widget-card--disabled { cursor: default; }
  ```

  The selector's responsive block also moved from the `@media (width < 768px)` viewport query to
  `data-layout` attribute rules on `.tm-widget-selector-modal` (mobile hides the sidebar for the
  chip radio group; tablet keeps the sidebar with two card columns) — hosts keying CSS on the old
  media query should key on `data-layout` instead.

- **`TmDashboard` default appearance changed from a wrapping card to `Plain`.** The component no
  longer paints a white rounded card around itself: the root sits directly on the workspace
  background (`--tm-bg-workspace`) and the widget cards are the only surfaces — the reference
  look. Hosts that embed the dashboard on a contrasting page and want the old framing opt back
  in explicitly:

  ```razor
  @* before (2.9.0) — the component rendered its own surface card *@
  <TmDashboard DashboardId="overview" />

  @* after (2.9.1) — Plain is the default; opt back into the card *@
  <TmDashboard DashboardId="overview" Appearance="DashboardAppearance.Card" />
  ```

- **`AllowEdit=false` no longer renders the dashboard-edit button nor the empty state's
  "Add widgets" CTA.** Previously the button (and CTA) rendered but entering edit mode did
  nothing — a dead control advertising a disabled capability. Hosts relying on the button's
  presence for layout or `data-testid='dashboard-edit'` selectors under `AllowEdit=false` must
  drop the expectation; the header keeps the host's `HeaderActions` in view mode.

- **The widget title is now a real heading: `h3.tm-widget-title` with a stable `id`
  (`tm-widget-title-{InstanceId}`), sized `--tm-font-size-lg`.** 2.9.0 rendered a plain
  `span.tm-widget-title`. Host CSS or tests keying on the `span` element must key on the class
  (or `h3`) instead; the id is what the collapse/remove controls reference through
  `aria-describedby`.

- **`TmDashboard` widget icon tiles are rendered by default (`ShowWidgetIcons=true`).** A host
  that wants the pre-2.9.1 bare widget headers turns them off:

  ```razor
  @* before: widget headers showed only the title *@
  <TmDashboard />

  @* after: same look as 2.9.0 *@
  <TmDashboard ShowWidgetIcons="false" />
  ```

- **The view-mode "Edit" button is primary (`tm-btn-primary`) and lost its `title` attribute**
  (the visible text is the accessible name). Host CSS or tests keying on `tm-btn-secondary` for
  `[data-testid='dashboard-edit']` must key on `tm-btn-primary` (or drop the variant selector).

- **Edit-mode toolbar button hierarchy swapped: "Save changes" is now primary, "Add widget"
  secondary, "Cancel" ghost** (one primary element per region). Tests keying on
  `tm-btn-primary` for the add-widget button must switch to the save button or the test id
  `dashboard-add-widget`.

- **Removed chrome:** the header toolbar lost its `border-bottom` underline and bottom padding;
  the widget header lost its grey band (`--tm-bg-surface-secondary`) and `border-bottom`; edit
  mode no longer draws a `2px dashed` frame around the root (the subtle background grid +
  "Edit Mode" badge signal edit mode instead); widget cards no longer lift their shadow on
  hover (the card is not clickable); the `.tm-dashboard-menu-badge` class is gone (replaced by
  `.tm-dashboard-menu-default` with a visible label). Host CSS overriding those rules needs to
  target the new structure.

- **`ShowTitle` stays and is intentionally NOT obsoleted**: it hides the heading (and subtitle)
  while keeping the switcher and header actions — a different concern than `ShowHeader`. Hosts
  using `ShowTitle="false"` keep working unchanged.

- **The dashboard switcher trigger is a select-like control (`button.tm-dashboard-switcher`)**
  showing the current dashboard's name instead of the word "Dashboards", with the formatted
  accessible name `TmDashboard_SwitcherAriaLabel` ("Dashboards: {0}") so the accessible name
  contains the visible text (WCAG 2.5.3).

- **`TmDashboard` F3 — widget DOM order, released height after collapse, tablet/mobile header
  and touch targets (dashboard redesign plan, phase F3).** The tablet (`data-layout="tablet"`)
  and mobile (`data-layout="mobile"`) layouts become usable at 1024/768/390/320px:

  - **Widget DOM order = visual (row, then column) order in every mode.** The grid renders its
    widgets sorted by `(Y, X, InstanceId)` — the same key as `--tm-w-order`, which is still
    emitted — so Tab and screen readers follow the eye (WCAG 2.4.3/1.3.2). On desktop the
    explicit placement is unchanged; only the DOM order differs.
  - **A collapsed or minimized card releases its height.** Desktop keeps the saved `Y` slot
    reserved and only shrinks the card to its header; tablet and mobile also drop the row span,
    so the stack closes up. `WidgetInstance.Height` is untouched — expanding restores the slot.
  - **Tablet rows are content-driven**: `grid-auto-rows: auto` with the card's desktop slot
    height as `min-height` (the new component variable `--tm-w-min-block`, inner gaps included).
    Narrower cards no longer clip. Mobile cards use the same min-height (previously the inner
    gaps were missing, so cards were `(H-1) × gap` shorter than their desktop slot).
  - **The view-mode header stacks below desktop** when a visible heading renders (the new
    C#-computed modifier classes `tm-dashboard-toolbar--with-heading` /
    `tm-dashboard-toolbar--with-actions`): tablet puts HeaderStart + the switcher on row 1 and
    title/subtitle + actions on row 2; mobile puts HeaderStart + title on row 1, the switcher
    and Edit on row 2 (actions move to row 3 when `HeaderActions` renders). The sr-only heading
    and `ShowTitle=false` keep the single-row header. On a mobile layout the switcher, the
    header buttons and the widget chevrons meet `--tm-touch-target` (44px) regardless of the
    pointer hardware, the switcher drops its 14–20rem width floor, and the inline rename input
    fills the available width.
  - **The root is a named region**: `role="region"` with `aria-labelledby` pointing at the
    heading (visible or `tm-sr-only`), falling back to `aria-label` when no heading renders —
    several dashboards on one page stay distinguishable.

  Migration for each visible behavior change:

  - **Widget DOM order.** 2.9.0 rendered widgets in the order they were added and used CSS
    `order` to draw them correctly; Tab order and screen readers ignored that CSS. Host tests or
    CSS selecting widgets by DOM position (`:nth-child`, sibling combinators) now see the
    `(Y, X, InstanceId)` order instead of the insertion order — select by `data-instance-id`
    (or another stable attribute) instead.
  - **Collapse / minimize.** The old `tm-widget--minimized` rule forced `grid-row: span 1` via
    `!important`, which also reset `grid-row-start` — a minimized desktop widget silently lost
    its saved `Y` and jumped to the first free row — and no mode released a collapsed card's
    height. Remove host overrides of `tm-widget--collapsed` / `tm-widget--minimized` sizing and
    let the component size the card to its header (desktop keeps the slot, tablet/mobile
    release it).
  - **Compact-mode heights.** Host CSS that expected the fixed `span H` rows on tablet, or the
    gap-less mobile `min-height`, must switch to overriding `--tm-w-min-block` (or
    `--tm-w-rows` / `--row-height`) instead of the row spans.
  - **Stacked header.** Hosts that pinned the mobile toolbar-right to its own full-width row
    (`flex-basis: 100%`) or relied on the switcher's 14–20rem floor below desktop must drop
    those overrides — the switcher now spans the free space and must shrink to fit 320px next
    to the Edit button.
  - **Root region semantics.** Host tests asserting the root has no role, or asserting the exact
    attribute list of `.tm-dashboard`, must accept the always-named `role="region"`.
  - **Documented (not a change):** inside tablet, the six-to-two column switch follows the grid
    container (`@container tm-dashboard (width < 768px)`), which is ~48px narrower than the
    dashboard root — the effective root width for the two-column step is ~816px, not 768px.

- **Draft-guard surface for every comment composer the host cannot observe — `TmChat`,
  `TmNotionPageCommentPanel`, `TmNotionBlockCommentPanel`, `TmNotionTextCommentPanel`,
  `TmNotionPageCommentSection`, `TmDocumentCommentComposer`, `TmDocumentCommentThread`,
  `TmDocumentCommentRail`, `TmDocumentCommentThreadPanel` and `TmDocumentPageViewer` now expose
  `OnDraftChanged`, `HasUnsavedDraft` and `DiscardDraftAsync`**, mirroring the additive pattern
  `TmActivityComments` introduced earlier in 2.9.1. All of these components keep their composer
  text in private fields (or child composers the host does not bind), so a host could never tell
  that the user had typed a message or comment that a reload, a navigation or a panel close would
  silently throw away. The new surface follows one contract across the family:

  - `[Parameter] EventCallback<…> OnDraftChanged` fires ONLY when the draft-state record actually
    changes (value-equality dedup with the last state assigned before invoking) — never on every
    keystroke. Three records carry the flags (never the draft text itself), all in
    `Tempo.Blazor.Abstractions`: **`TmChatDraftState`** (`HasComposerDraft`, `HasThreadReplyDraft`,
    `EditingMessageId`, aggregate `HasUnsavedContent`) for `TmChat`, in the
    `Tempo.Blazor.Models` namespace next to the other chat models; **`TmNotionCommentDraftState`**
    (`HasNewCommentDraft`, `HasThreadReplyDraft`, `HasInlineReplyDraft`, `EditingEntryId`,
    aggregate `HasUnsavedContent`) for the three Notion panels and the section pass-through, in
    the `Tempo.Blazor.NotionEditor.Models` namespace next to the other Notion models; and
    **`TmCommentDraftState`** (`IsEditorOpen`, `EditingCommentId`, `HasUnsavedContent`) for
    `TmActivityComments` and the DocumentEditor and Signing components. All three implement
    `ITmDraftState` (see below).
  - `HasUnsavedDraft` polls the same aggregate flag live from the current composer state (not from
    the last notification), for hosts that prefer polling over push.
  - `DiscardDraftAsync` clears every draft slot (composer text, open reply, open edit) after the
    consumer's OWN confirmation dialog — no component shows a confirmation itself; that remains
    deliberately the consumer's responsibility. All of them dispatch through `InvokeAsync` and are
    no-ops (no notification) when no draft exists.
  - **`ITmDraftState`** (`HasUnsavedContent`) and **`ITmDraftGuard`** (`HasUnsavedDraft` +
    `DiscardDraftAsync`) in `Tempo.Blazor.Abstractions.Shared` codify the contract:
    `ITmDraftState` is implemented by all three records, `ITmDraftGuard` by every component with
    the surface (`TmActivityComments`, `TmChat`, the three Notion panels plus
    `TmNotionPageCommentSection`, `TmDocumentCommentComposer`, `TmDocumentCommentThread`,
    `TmDocumentCommentRail`, `TmDocumentCommentThreadPanel` and `TmDocumentPageViewer`), so a
    generic host page can hold `ITmDraftGuard?` / `ITmDraftState?` without caring which concrete
    component it renders.

  Per-component semantics worth knowing: `TmChat` reports the main composer, the open thread
  panel's reply input and message edits (an edit opened without changes is NOT an unsaved draft —
  the flag rises once the text differs from the message as it was when the edit started;
  a thread panel that unmounts parameter-driven — its root message left `Messages` — clears its
  slot and reports the cleared state); `DiscardDraftAsync` clears the text slots but leaves the
  thread panel open (its visibility is the host's `ActiveThreadRootId` business). The Notion
  panels treat the inline reply's auto-inserted quote the same way — content up to the auto-quote
  is not user input — and hiding a panel (`Visible` → `false`) clears every draft slot and
  reports the cleared state, so a host should check `HasUnsavedDraft` before hiding.
  `TmNotionPageCommentSection` is a pure pass-through (`@ref` + delegation) of the embedded panel.
  In the DocumentEditor stack the leaf `TmDocumentCommentComposer` carries the surface and both
  hosts aggregate it: `TmDocumentCommentThread` over its reply/edit composers (each slot gated by
  its mount condition, so a resolved thread or a withdrawn `CanComment` reports the closed, empty
  state instead of a stale composer's text) and `TmDocumentCommentRail` over everything it
  renders — its own new-comment composer AND every rendered `TmDocumentCommentThread`: the rail
  merges the threads' reported states (IsEditorOpen = new composer or any thread editor,
  EditingCommentId = the first thread's editing id, HasUnsavedContent = any slot), prunes
  threads the host removes, and `DiscardDraftAsync` reaches all of them. In Signing,
  `TmDocumentCommentThreadPanel` binds its reply/edit composers internally (the reply text is
  cleared whenever the reply composer unmounts — selection change, clear selection, edit start —
  and the aggregate counts it only while the reply composer is mounted), and
  `TmDocumentPageViewer` aggregates its own new-thread draft panel with the embedded thread
  panel (states flow in through the panel's OnDraftChanged callback, both contributions are
  gated by `CommentsEnabled && CommentThreadPanelTemplate is null`, so a host-owned panel
  template or a comment disable reports the closed state; `EditingCommentId` comes from the
  panel; the viewer's own draft always creates a NEW thread).

  Standalone editors that already expose their text through `Value`/`ValueChanged` binding —
  `TmRichEditorFull`, `TmRichEditorSimple` and the Signing `TmCommentComposer` — deliberately stay
  unchanged: a host that binds them already sees every character.

- **`TmDiagramLayersPanel` styles live in exactly one stylesheet now (DiagramEditor redesign
  phase 3).** The panel's rules were duplicated: a scoped `TmDiagramLayersPanel.razor.css` AND a
  legacy global copy inside `Tempo.Blazor.DiagramEditor`'s `_diagram-editor.css`. The global copy
  is deleted (together with its dead rules for markup that no longer exists:
  `.tm-diagram-layers__selected-section`, `__select`, `__actions`, `__action-btn`, `__icon-btn`,
  `__label`); the scoped component stylesheet is the single source
  (`TmDiagramLayersPanelTests.SingleStyleSource` pins it). No class that any markup, test or
  consumer still uses was removed — the live class names are unchanged, so most hosts need no
  action. Two consequences to migrate for:

  ```css
  /* before — two sources; the global copy could be "edited" with no effect
     (the scoped copy won on specificity) */
  wwwroot/css/components/_diagram-editor.css   .tm-diagram-layers__item { ... }   /* deleted */
  Components/Diagram/TmDiagramLayersPanel.razor.css .tm-diagram-layers__item { ... }  /* the source */

  /* after — override the scoped source (or ::deep from a parent's razor.css);
     a host that linked ONLY the package's global stylesheet and styled
     .tm-diagram-layers* must load the RCL scoped bundle ({App}.styles.css),
     which the demos already do */
  ```

  ```text
  # dead class names, removed without alias (no markup/test/consumer referenced them):
  #   .tm-diagram-layers__selected-section / __select / __actions / __action-btn
  #   .tm-diagram-layers__icon-btn / __label
  ```

- **`TmDiagramTemplateCard` is a vertical card (DiagramEditor redesign phase 3, mockup).** The
  card now stacks the 16:10 preview ABOVE the name/tags (`flex-direction: column` with
  `aspect-ratio: 16 / 10` on `.tm-diagram-template-card__thumb`); the DOM order
  (thumb → info) is unchanged, so selectors keep matching. A host that laid the card out as a
  horizontal row from CSS must set `flex-direction: row` on `.tm-diagram-template-card` again
  and give `.tm-diagram-template-card__thumb` an explicit width.

- **`TmDiagramEditor`/dialogs: component-level dark overrides removed (DiagramEditor redesign
  phase 3).** Every `[data-theme="dark"]` / `.tm-dark` block and every hex/rgba literal in the
  DiagramEditor package's CSS was replaced by semantic `--tm-*` tokens (the strict token audit
  of the package went from 168 baseline entries to 0). Dark mode now follows `tokens-dark.css`
  exclusively; a host that RE-Pointed one of the removed component dark values must override the
  semantic token instead (e.g. `--tm-color-surface-elevated`), not the component class.

- **`TmWidgetSelector` renders on `TmModal` — its private overlay markup is gone.** The selector
  no longer renders `.tm-widget-selector-overlay` (its own `position: fixed` root) or a private
  header/close button; the modal owns the overlay, the panel chrome, the title and the close
  control. The dialog surface is now `.tm-modal-overlay > .tm-modal-container > .tm-modal`
  (`.tm-focus-scope`, `role="dialog"`), and the familiar `.tm-widget-selector-modal` class moved
  INSIDE it onto the content root (search + category/grid area). Hosts (or test suites) that
  queried the old DOM must migrate, before/after:

  ```css
  /* before */
  .tm-widget-selector-overlay { /* the fixed overlay root, z-index var(--tm-z-modal) */ }
  .tm-widget-selector-header  { /* title + subtitle + close button */ }
  .tm-widget-selector-close   { /* the × button */ }

  /* after — styled by the library's _modal.css; style the content root instead */
  .tm-modal-overlay { }              /* the overlay (was .tm-widget-selector-overlay) */
  .tm-modal-close   { }              /* the × button (aria-label "Close") */
  .tm-widget-selector-modal { }      /* now the content root INSIDE .tm-modal-body */
  ```

  ```csharp
  // Playwright/bUnit selectors, before → after:
  //   .tm-widget-selector-overlay        → .tm-modal-overlay  (or .tm-widget-selector-modal)
  //   .tm-widget-selector-close          → .tm-modal-close
  //   .tm-widget-selector-modal h3       → .tm-modal-title
  ```

  The `.tm-widget-selector-modal` contract class is unchanged in name — only its position in the
  DOM moved one level down (inside the modal body), and `.tm-widget-selector-category` /
  `.tm-widget-card-icon` are untouched.

- **The `TmDashboard` switcher menu is an overlay-panel ARIA menu of native buttons.** The menu
  renders through `TmOverlayPanel` (popover="manual", positioned by `overlay.js`) — hosts that
  styled `.tm-dashboard-menu` as an absolutely-positioned dropdown (`position: absolute;
  top: calc(100% …)`) must drop those declarations, exactly like the `TmDropdown` migration; the
  class stays on the panel root. The old `div.tm-overlay` click-away backdrop behind it is gone
  (the panel dismisses on outside pointerdown itself). Each dashboard row changed from a
  `div[role="menuitem"]` with emulated Enter/Space to a `div[role="none"]` wrapper around a
  native `button[role="menuitem"].tm-dashboard-menu-item__select` plus sibling native action
  buttons (`button[role="menuitem"].tm-dashboard-menu-action[-danger]`) whose labels now name the
  dashboard — the new `TmDashboard_DeleteNamed` ("Delete dashboard {0}") and
  `TmDashboard_SetAsDefaultNamed` ("Set {0} as default") keys replace the bare
  `TmDashboard_Delete`/`TmDashboard_SetAsDefault` labels on those buttons. The current dashboard's
  select entry carries `aria-current="true"`. The rename save icon now uses
  `TmDashboard_ApplyName` ("Apply name") instead of `TmDashboard_SaveChanges`, and the inline
  rename is a `<form>` (Enter applies, Escape cancels). Tests that dispatched
  `KeyDown("Enter")`/`KeyUp(" ")` at a `.tm-dashboard-menu-item` row must instead click the native
  select button (or press keys in a real browser), and `.tm-dashboard-menu-item` rows no longer
  carry `tabindex`/`role="menuitem"` themselves — the entries are the buttons inside. The widget
  cards in `TmWidgetSelector` changed from `div.tm-widget-card` to `button.tm-widget-card` with
  span children (`.tm-widget-card-title`, `.tm-widget-card-desc` replace the inner `h4`/`p`, the
  icon/info/meta/add wrappers are spans) and render on a `ModalSize.XLarge` modal whose height is
  the `--tm-widget-selector-height` token (`min(60vh, 35rem)`, applied as a `max-height`) —
  `.tm-widget-card` queries keep working, but anything that asserted `TagName == "div"` (or relied
  on the div not being focusable, or selected `.tm-widget-card-info h4`) must move to the button.

- **The 267 demo-only localization keys are no longer in the `Tempo.Blazor` resource tables.** An
  application that read any of them through `ITmLocalizer` now gets the raw key — copy the strings
  into your own decorator (see the demo-keys entry under `Changed` below).

- **An overlay close now leaves the focus where a live element OUTSIDE the closing surface
  already holds it, and an explicit restore target INSIDE the surface is skipped.** Previously
  `TmModal`/`TmDialog`/`TmDrawer`/`TmFocusScope` always ran their restore on close: when an app's
  OK handler had settled the focus on a post-close action button before the close render, the
  restore yanked it back to the opener (or, worse, onto a captured element inside the overlay,
  which then fell to `<body>` when the render removed it). And a `RestoreFocusTarget` that lay
  inside the closing surface was focused anyway — handing focus to an element the close was about
  to remove. Now the restore runs only while the closing surface still holds the focus (or focus
  fell to `<body>`), and every restore candidate — opener or explicit target — must be connected
  and OUTSIDE the surface. Apps that depended on the close re-focusing the opener after they had
  placed focus elsewhere must migrate, before/after:

  ```razor
  <!-- before: the close stole the focus back from the "Created" button -->
  <TmModal OnOk="() => { show = false; createdButton.FocusAsync(); }" />

  <!-- after: unchanged code, new behaviour — the focus placed on the live "Created" button
       (after the close render) holds; the close restores the opener only when the surface
       still owned the focus -->
  <TmModal OnOk="() => { show = false; _focusCreatedPending = true; }" />
  @code {
      protected override async Task OnAfterRenderAsync(bool firstRender)
      {
          if (_focusCreatedPending) { _focusCreatedPending = false; await _createdButton.FocusAsync(); }
      }
  }
  ```

  (A focus issued synchronously inside the OK handler — BEFORE the close render — was and is
  dropped by the browser, because the open modal holds the page `inert`; the pending-flag pattern
  above is the supported way and works on both render modes.)

  The second half: a `RestoreFocusTarget`/`RestoreFocusTargetId` that resolves to an element
  inside the closing surface (or a disconnected one) no longer receives the focus — the restore
  falls back to the opener, so a misconfigured target cannot strand the focus on a dying element.

- **`TmColorPalette`'s accessibility tree moved from an invalid grid to a radiogroup of radios.**
  The old markup put `role="grid"` on the palette and `role="gridcell"` + `aria-selected` on the
  swatch buttons — an ARIA grid needs `role="row"` wrappers, so screen readers never exposed a
  usable grid, and `aria-selected` on a non-list option carried no checked semantics. The grid
  is now `role="radiogroup"` (same localized `aria-label`) and each swatch is `role="radio"`
  with `aria-checked` instead of `aria-selected`; the roving tabindex, the arrow/Home/End focus
  movement and the native-button Enter/Space activation are unchanged. Hosts querying the
  palette's roles or the selected swatch must migrate, before/after:

  ```html
  <!-- before -->
  <div class="tm-color-palette-grid" role="grid" aria-label="Select color">
      <button type="button" role="gridcell" aria-selected="true" …></button>
  </div>

  <!-- after -->
  <div class="tm-color-palette-grid" role="radiogroup" aria-label="Select color">
      <button type="button" role="radio" aria-checked="true" aria-label="Select color Red" …></button>
  </div>
  ```

  The generic per-swatch `aria-label` is gone with it: unnamed colors announce
  "Select color {value}", and `ColorNames` (see Accessibility) replaces the value with a real
  name.

- **`TmNotificationBell` entries are native `<button type="button">` elements, not
  `div @onclick` rows.** A `div` with a click handler is a keyboard trap — a keyboard user could
  open the dropdown but never activate a notification (WCAG 2.1.1). Each entry is now a real
  button (the browser delivers the activation; no emulated key handlers per
  [docs/keyboard-activation-convention.md](docs/keyboard-activation-convention.md)), it holds no
  nested interactive element, and its inner wrappers (`__avatar`, `__content`) became `<span>`s
  because a button's content model is phrasing content. The entry keeps its previous styling
  (the UA button chrome is reset in `_notification-bell.css`, a focus-visible ring was added).
  DOM, before/after:

  ```html
  <!-- before -->
  <div class="tm-notification-bell__item">
      <div class="tm-notification-bell__avatar">…</div>
      <div class="tm-notification-bell__content">…</div>
  </div>

  <!-- after -->
  <button type="button" class="tm-notification-bell__item">
      <span class="tm-notification-bell__avatar">…</span>
      <span class="tm-notification-bell__content">…</span>
  </button>
  ```

  Consumers styling the entries over the classes keep working (all selectors are class-based);
  consumers selecting the entries by tag (`div.tm-notification-bell__item`) must select
  `button.tm-notification-bell__item` instead.

- **`TmWorkflowDesignerCanvas`'s svg root is `role="group"`, not `role="img"`** — `img` tells
  assistive technology the subtree is a picture, which hid the now-focusable state nodes from it
  entirely. The accessible name (the canvas aria-label) is unchanged. DOM, before/after:

  ```html
  <!-- before: the states inside were invisible to assistive technology -->
  <svg viewBox="0 0 800 500" aria-label="Workflow designer canvas" role="img">…</svg>

  <!-- after: a labelled group whose state nodes are focusable buttons -->
  <svg viewBox="0 0 800 500" aria-label="Workflow designer canvas" role="group">…</svg>
  ```

  Two more behaviour changes ride along: **plain wheel no longer zooms the canvas** — ctrl+wheel
  (trackpad pinch) does, exactly like the diagram editor, and plain wheel scrolls the page again;
  and **a click that follows a real node drag no longer also selects the node** (a tap still
  selects — the drag gesture swallows only the click that terminates a drag).

- **`workflow-designer.js` now imports `pointer-drag.js` and `pointer-gesture.js`.** A `script-src`
  policy that allows `workflow-designer.js` must also allow the two helpers it loads with a dynamic
  `import()` — same `_content/Tempo.Blazor/js/` origin, so the host-wide nonce or hash that covers
  `scheduler.js` covers all of them; a per-file hash must list `pointer-drag.js` and
  `pointer-gesture.js` too. In exchange the designer no longer carries its own mouse gesture.

- **The rich text editor dialogs (link, image, table, video, find & replace) are now `TmModal`s.**
  They used to paint their own `position: fixed` overlay at `z-index: 1040` with no `role`,
  `aria-modal`, Escape handling or focus trap — opened while a promoted drawer, sheet or modal was
  up they could paint underneath it. Now each dialog renders the core `TmModal` primitive: the
  browser top layer, `role="dialog"` named by its title (`aria-labelledby`), Escape and the
  backdrop dismiss it, the shared focus trap holds Tab inside, focus returns to the editor surface
  on every close path, and the caret/selection that opened the dialog is replayed before the
  insertion (link/image/table/video) or on plain close (find & replace). The dialog chrome
  (header with the `h5` title, the `×` close button, the panel box) is the standard modal chrome
  now. Consumers who positioned or targeted the removed self-overlay parts must target the modal
  parts instead:

  ```razor
  @* before: dialog rendered its own fixed overlay *@
  <div class="tm-rte-link-dialog">
      <div class="tm-rte-link-backdrop"></div>
      <div class="tm-rte-link-content">
          <div class="tm-rte-link-header"><h5>Insert Link</h5><button class="tm-rte-btn-close">×</button></div>
          <div class="tm-rte-link-body">…</div>
          <div class="tm-rte-link-footer">…</div>
      </div>
  </div>

  @* after: the same form content on the TmModal chrome; the dialog root class rides on the
     overlay root, body content goes straight into the modal body *@
  <div class="tm-modal-overlay tm-rte-link-dialog tm-rte-dialog" popover="manual">…</div>
  ```

  The `.tm-rte-link/image/table/video/findreplace-backdrop`, `-content`, `-header`, `-body`,
  `-footer` and `.tm-rte-btn-close` classes no longer exist; the inputs (`.tm-rte-link-url-input`
  …), the size grid and the result badge keep their classes. Each dialog also grew one optional
  parameter: `RestoreFocusTargetId` — the id of the element (the editor surface) focus returns to
  when the dialog closes; the rich editors pass their editor automatically. Two more breaking
  details: the footer buttons are `TmButton`s now (Ghost Cancel + Primary/Secondary action, the
  `tm-rte-btn-cancel`/`tm-rte-btn-insert`/`tm-rte-btn-replace`/`tm-rte-btn-replaceall` hook classes
  stay), and `FindReplaceDialog.OnReplace`/`OnReplaceAll` changed from `EventCallback` to
  `Func<(string Find, string Replace), Task<int>>?` — the handler must return the real replacement
  count (from `tmRichEditor.findAndReplace`) so the dialog can report it; `OnFind` was removed.

- **`TmSplitterPane`'s resizer is a keyboard-operable separator driven by the new `tm-splitter.js`
  module (no more `eval`).** The strip renders `role="separator"` with `tabindex="0"`,
  `aria-orientation` (a horizontal splitter's separator is a vertical strip and reports
  `vertical`), `aria-valuenow` with `aria-valuetext` (`"200 px"` — the pane is measured in the DOM
  when its `Size` is not a plain pixel length, so the value is never missing),
  `aria-valuemin` (always emitted; `0` without a parseable `MinSize`), `aria-valuemax` (always
  emitted; the splitter's measured main-axis length without a parseable `MaxSize`) and a
  localized `aria-label` (`TmSplitter_Resize`). Arrows resize by 16 px (Shift by 64 px, along the
  splitter's axis only, inverted in RTL exactly like the pointer drag) and `Home`/`End` jump to a
  bound — `0` and the measured splitter length when no pixel `MinSize`/`MaxSize` parses — the
  WCAG 2.5.7 non-drag alternative. The collapse button (localized now,
  `TmSplitter_Collapse`/`TmSplitter_Expand`) is the strip's SIBLING inside a
  `.tm-splitter__gutter` wrapper, never a child of `role="separator"`. The drag itself
  moved out of Blazor pointer handlers (which re-rendered on every move) and out of the `eval`'d
  `window.TempoSplitter` shim into `tm-splitter.js` over the shared `pointer-drag.js` helper: the
  pane follows the pointer as an inline `flex` with no re-render per move and exactly one commit
  per gesture; Escape, pointercancel and a mid-drag detach restore the pane's exact start style
  (the drag's `flex` shorthand would otherwise leave the authored inline `flex-basis` destroyed);
  the horizontal delta inverts in RTL; and under `(pointer: coarse)` the strip's hit area widens
  to a full `--tm-touch-target` (the collapse button grows to it too). There is deliberately no
  `SizeChanged` event. Consumer-facing DOM, before/after:

  ```razor
  @* before: a bare div with Blazor pointer handlers, no role, not focusable *@
  <div class="tm-splitter__resizer">
      <button type="button" class="tm-splitter__collapse-btn">…</button>
  </div>

  @* after: a gutter wrapping the separator tab stop (keys resize) and, as its sibling, the
     collapse button with its own accessible name — never inside the separator *@
  <div class="tm-splitter__gutter">
      <div class="tm-splitter__resizer" role="separator" tabindex="0"
           aria-orientation="vertical" aria-valuenow="200" aria-valuetext="200 px"
           aria-valuemin="100" aria-valuemax="800" aria-label="Resize panes"></div>
      <button type="button" class="tm-splitter__collapse-btn" aria-label="Collapse pane">…</button>
  </div>
  ```

  **CSP:** a `script-src` policy must allow `tm-splitter.js` next to `pointer-drag.js` (same
  `_content/Tempo.Blazor/js/` origin, so the host-wide nonce or hash that covers `scheduler.js`
  covers both; a per-file hash must list the module too). In exchange the splitter — and the range
  slider below — no longer need `unsafe-eval`.

- **`TmRangeSlider` drags natively: two stacked `input[type=range]`, no JS and no `eval`.** The
  `window.TempoSlider` shim (an `eval`'d pointer gesture that leaked its window listeners, never
  released capture and died on touch, because the container had no `touch-action`) is gone, and the
  shipped `wwwroot/js/slider.js` static asset is **deleted** — no host page ever loaded it, and a
  host that did would have silently degraded the eval-free component. Each thumb now drags itself
  through CSS: the input swallows pointer events, the thumb pseudo-elements answer them, and on
  `(pointer: coarse)` the thumbs grow to `--tm-touch-target`. Both thumbs carry localized
  accessible names (`TmRangeSlider_Start` / `TmRangeSlider_End`, prefixed `{Label} – ` when a
  `Label` is set), and the root gains `tm-range-slider--start-on-top` whenever the start thumb is
  in the upper half of the range — parked on or a step from the end thumb, it would otherwise be
  trapped beneath it — so the start thumb stays draggable. A gesture that runs past a bound is
  clamped on the INPUT itself (the raw value renders first, then the clamped rewrite forces the
  thumb back inside the range), and the fill uses `inset-inline-start`, so the range mirrors in
  RTL. Behaviour change: clicking the empty track no longer jumps the nearest thumb — grab a
  thumb, or focus it and use the arrows. DOM, before/after:

  ```razor
  @* before: unnamed thumbs; a JS gesture owned the container *@
  <input type="range" class="tm-range-slider__input tm-range-slider__input--start"
         min="0" max="100" />

  @* after: named thumbs that drag themselves; the container has no listeners *@
  <input type="range" class="tm-range-slider__input tm-range-slider__input--start"
         min="0" max="100" aria-label="Price – Minimum" />
  ```

- **Relative time (and the Scheduler/Gantt "today") is judged in the USER's time zone by default;
  `AddTempoUserTimeZone()` is an obsolete no-op.** An earlier 2.9.1 preview added
  `ITmUserTimeZone` + `AddTempoUserTimeZone()` as an OPT-IN (2.9.0 never shipped it); this release
  flips the default on, so the call is gone — it is kept as an `[Obsolete]` no-op so code written
  against it compiles. `AddTempoBlazor()` registers the relative-time formatter over a private clock
  whose `LocalTimeZone` reads `ITmUserTimeZone.Zone` — the browser's zone on Blazor Server (UTC
  until the browser probe reports after the first interactive render, then
  `Intl.DateTimeFormat().resolvedOptions().timeZone`), the runtime's zone on WebAssembly. The day
  boundary and the clock time inside "yesterday at 4:20 PM" follow the browser, not the server.
  `TmScheduler.ResolveTimeZone()` (when the host set no `TimeZoneId`) and the Gantt's "today" (the
  today marker, new-task defaults, the overdue highlight, the embedded calendar) reuse the same
  `ITmUserTimeZone`, so Scheduler/Gantt and the stamps agree; the Scheduler and the Gantt now also
  trigger the browser probe from their first interactive render and re-render when the zone arrives.
  The app-wide `TimeProvider` is still never replaced (Kestrel and cookie authentication consume
  it), and a host profile setting still wins via `ITmUserTimeZone.SetZone`.
  **Breaking vs 2.9.0** (2.9.0 had no opt-in, so this is the real before/after):

  ```csharp
  // before (2.9.0): the SERVER's zone decided the day boundaries
  builder.Services.AddTempoBlazor();
  // formatter ran over TimeProvider.System → days judged in the server zone;
  // TmScheduler.ResolveTimeZone() with no TimeZoneId → TimeZoneInfo.Local (the server zone)

  // after (2.9.1 default): the USER's zone decides
  builder.Services.AddTempoBlazor();
  // formatter runs over the browser probe → UTC until it reports, then the browser zone;
  // TmScheduler.ResolveTimeZone() with no TimeZoneId → ITmUserTimeZone.Zone (UTC until the probe)
  ```

  A host that wants the 2.9.0 server-zone behaviour (or its own zone plumbing) registers a scoped
  `ITmRelativeTimeFormatter` over the clock of its choice after `AddTempoBlazor()` and passes an
  explicit `TimeZoneId` to its `TmScheduler` instances:

  ```csharp
  // keep the 2.9.0 server-zone behaviour
  builder.Services.AddTempoBlazor();
  builder.Services.AddScoped<ITmRelativeTimeFormatter>(sp => new TmRelativeTimeFormatter(
      sp.GetRequiredService<ITmLocalizer>(), TimeProvider.System));
  // <TmScheduler TimeZoneId="Central Europe Standard Time" … /> — the display zone, explicitly
  ```

  See [docs/localization-plurals.md](docs/localization-plurals.md) for the full plumbing notes
  (why the app-wide `TimeProvider` must stay untouched, the probe timing, host overrides). A host
  with no `IJSRuntime` at all (a pure backend resolving the formatter) gets a formatter over a
  machine-local/resolved zone service — the same day boundary the plain formatter had — and never
  faults on the missing interop.

- **The deprecated `TmTimeline_*` keys are removed from the resources.** `TmTimeline_JustNow`,
  `TmTimeline_MinutesAgo`, `TmTimeline_HoursAgo`, `TmTimeline_DaysAgo` and `TmTimeline_DateFormat`
  were deprecated earlier in this release (the library stopped reading them); they are now gone from
  `TmResources.json`, `TmResources.cs.json` and `TmResources.fr.json` as well, so a host that still
  overrides them no longer shadows anything. **Migration:** override the `Tm_Time_*` family instead
  (or register your own `ITmRelativeTimeFormatter`):

  ```csharp
  // before
  services.AddSingleton<ITmLocalizer, MyLocalizer>();   // overriding "TmTimeline_MinutesAgo"
  // after
  services.AddSingleton<ITmLocalizer, MyLocalizer>();   // overriding "Tm_Time_MinutesAgo"
  ```

### Upcoming

- **`TmDataTable.KeyboardNavigation` becomes `Grid` by default in the next major release.** Keep today's behavior by explicitly setting `KeyboardNavigation="DataTableKeyboardNavigation.Rows"`.

### Changed

- **DiagramEditor: inspector page orientation is a labelled Portrait/Landscape segmented control**
  with `aria-pressed`.

- **DiagramEditor: the status-bar mode control keeps a constant visible label** — *Read-only*
  (`TmDiagramEditor_ReadOnly`) — in both states; the state travels on `aria-pressed`, the
  lock/pencil icon and the tooltip wording (`TmDiagramEditor_Editing` / `TmDiagramEditor_ReadOnly`),
  and the accessible name repeats the visible label (WCAG 2.5.3). The same shape applies to the
  mobile "⋯" menu toggle.

- **DiagramEditor: UML stencils (`uml.*`) grow to fit their content** (rounded up to the 10-unit
  grid, never shrunk) when inserted from the palette, through the Connect dialog or when a document
  is created from a template. The growth is content-driven only: a stencil whose data carries no
  list section (attributes/methods/values — activity start/end, note, actor, …) keeps its stencil
  default height, and the content sum has no minimum-height floor beyond that default. Connect
  placement and swimlane drop detection use the fitted height, so a class-like stencil that grows
  (e.g. abstract-class 140→150) is centred and dropped into the cell its body actually overlaps.
  Stencil content taller than its node keeps its first section visible
  and clips at the bottom (`justify-content: safe center`) instead of being clipped at both edges.
  The property-edit resize path (editing attributes/methods inline) shares the same content
  measurement: `DiagramUmlAutoHeight` exposes one `ContentHeight(stencilId, data)` used by both
  `Fit` and `Compute`, so an inline edit grows the node exactly like insertion — grid-rounded,
  never below the node's current (default) height, no 140/120/100 stencil floor. Demo UML sample:
  Customer class height corrected.

- **DiagramEditor: canvas-scoped paper/ink tokens.** `--tm-diagram-paper` and
  `--tm-diagram-node-ink` are declared on `.tm-diagram-canvas`, so a host rebrand (e.g. sepia
  paper) targets `.tm-diagram-canvas` or deeper — no `!important`, no shallower ancestor. Edge
  labels reuse the paper for their halo pill. `exportViewportAsSvg` copies the computed
  fill/stroke/color/background of the live page, backdrop and edge-label text onto the serialized
  clone, AND sets the computed `--tm-diagram-node-ink` / `--tm-diagram-paper` values on the clone
  root, so every `var()` inside the exported tree (node bodies, table cells, labels) resolves to
  the paints the user actually saw (including host overrides). New package tokens:
  `--tm-diagram-swimlane-header` (1.875rem = 30px, declared on `.tm-diagram-canvas`) drives the
  swimlane header band so the painted header stays in step with
  `DiagramNodeSwimlaneData.HeaderSize = 30`; `--tm-diagram-statusbar-btn` (1.875rem) keeps the
  status-bar buttons at the same 30px band; `--tm-diagram-chip-font` (0.6875rem = 11px, declared
  on `.tm-diagram-editor`) is the package chip scale for the page-tab close button and the
  template-card category/tags.

- **TmDiagramMinimap frames the diagram content instead of the whole page (X4).** The map's viewBox
  is now the union of all node boxes and the current viewport, padded by 10 % (never smaller than a
  fifth of the page), and the viewport rectangle draws as a transparent outline *under* the node
  boxes — it no longer hides the content it frames in light themes. Taps and drags map through the
  fitted origin, so navigation always matches what is shown. The width follows the editor layout
  (112 / 160 / 200 px for mobile / tablet / desktop); the mobile CSS override of
  `.tm-diagram-minimap__svg` width was removed — hosts must not resize the minimap svg with CSS (the
  tap mapping is computed from the `Width` parameter). The tablet/mobile overlay collapses in place
  (`.tm-diagram-editor__minimap-overlay-collapse`) and reopens from the floating
  `.tm-diagram-editor__minimap-overlay-reopen` button.

- **TmDiagramEditor (mobile): selecting a node pans the canvas so the selection stays visible above
  the peeked panels sheet (U3).** Tap-insert lands in the band above the sheet again — the JS helper
  returned an object the .NET side could not read, so the band measurement silently fell back to the
  viewport centre; `tmDiagramEditor.visibleBandCentre` now returns a `[x, y]` array.

- **TmDiagramEditor (mobile): the floating zoom overlay reads horizontally** (+ | % | − | fit along
  the bottom edge) instead of the 176 px vertical pill that covered a third of the narrow canvas.

- **DiagramEditor zoom: the zoom buttons now step through 15 %…500 % (added 15 %, 400 %, 500 %),
  the same range as wheel, pinch, `zoomTo` and fit-to-view.** At a limit, a pinch or a wheel tick
  clamps exactly to the limit instead of freezing just before it, and the two-finger pan keeps
  working.

- **DiagramEditor touch: a 600 ms long-press opens the context menu on edges and on the empty
  canvas too (it was node-only, so iOS had no edge/canvas menu), and the native touch
  `contextmenu` is swallowed, so the menu opens exactly once. Pen keeps the native press-and-hold
  menu.**

- **`TmDiagramEditor` visual layer aligned with the redesign mockups — tokenized, no structure
  change (DiagramEditor redesign phase 3).** Paint-only pass over the package's CSS plus small
  markup touches; the toolbar keeps its `data-tm-group` contract, no group is re-ordered (that is
  phase 4) and no toolbar function changed:

  - The editor chrome (root, page-tabs strip, toolbar) paints the themed workspace tone
    (`--tm-bg-workspace`, F0) instead of a hardcoded surface, so the indigo theme and dark mode
    recolor it without component overrides.
  - Toolbar groups are FLAT (no boxed button groups); the cluster separators and the 40px row
    height carry the structure, buttons/controls use `--tm-radius-control` and the active tool
    paints `--tm-color-primary-subtle` + primary ink (same convention as the core
    `aria-pressed` toolbar button).
  - Page tabs mark the ACTIVE page with a primary underline instead of a raised boxed tab.
  - The stencil palette reads like the mockup: sentence-case category headers with the chevron
    on the RIGHT (markup order swapped inside the button; `aria-expanded` unchanged) and
    card-like rows for the stencils.
  - Inspector property sections use `--tm-radius-card`; the Fill/Stroke fields show the current
    hex value as text next to the swatch (`.tm-diagram-properties__color-value`, mockup
    "Vzhled" section).
  - The SQL/CSV import dialogs paint the live parse result as a summary box with icon stats
    (`.tm-diagram-{sql,csv}-import__stat`), the CSV preview table has a sticky `thead`, and the
    template gallery cards are vertical (see Breaking / Migration).
  - Everything paints through semantic tokens; translucent washes derive from tokens via
    `color-mix()`. The package's strict token-audit baseline dropped from 168 entries to 0 and
    `DiagramResponsiveScreenshotTests` gained a 1440×900 light/dark × default/indigo matrix
    (editor, layers panel and the four dialogs) whose dark captures assert that the editor
    surfaces resolve to dark backgrounds with readable text and that no white island leaks
    into the chrome (computed-style checks), so a component-level light colour leaking into
    dark mode fails the test.

- **The demo-only localization keys moved out of the `Tempo.Blazor` package into the demo
  applications.** All 267 keys that only demo pages use — `TmDemo_*` and the `TmToolbarDemo_*`,
  `TmEditorShellDemo_*`, `TmDiagramDemo_*`, `TmMobileActionBarDemo_*`, `TmChartDemo_*`,
  `TmResponsiveDemo_*`, `TmSankeyChartDemo_*`, `TmDataTableDemo_*`, `TmSidePanelDemo_*`,
  `TmCalendarHeatmapDemo_*`, `TmRichEditorDemo_*` families, plus the lone
  `TmModelingEditor_DemoError` (en/cs/sk/fr alike) — used to ship in
  the library's embedded `TmResources` tables, inflating every consuming application's bundle with
  ~1,000 strings no component ever reads. They now live in the demo's own embedded
  `DemoResources.{json,cs,sk,fr}.json` (`Tempo.Blazor.Demo.SharedUI/Resources`), served by the new
  `DemoTmLocalizer` (`ITmLocalizer` decorator: demo keys from the demo tables, everything else
  falls through to the built-in `TmResources`), registered in all three demo hosts — WASM, Server
  and both InteractiveAuto sides — via `builder.Services.AddTempoDemoLocalizer()` right after
  `AddTempoBlazor()`. Demo pages inject the same `ITmLocalizer` as before and needed no changes;
  no library component changed; the 267 demo keys are no longer in the Tempo.Blazor resource
  tables — an application that read any of them through `ITmLocalizer` now gets the raw key and
  should copy the strings into its own decorator. `DemoResourceCompletenessTests` guards the
  new tables (four-language parity, placeholder parity, every demo key visibly demo-named, and the
  key sets of the two tables disjoint), and `MockTmLocalizer` no longer carries the moved keys.

- **A `TmMobileActionBar` in-flow bar is one label line shorter (C11).** Tiles of the default
  `StickyContainer` and `Inline` bars floor at ONE label line (a ≈61px bar instead of 71px with
  one-line labels); a wrapping label grows the bar and `align-items: stretch` keeps every tile
  equal. `Placement="FixedViewport"` keeps the two-line floor its published
  `--tm-mobile-action-bar-reserve-block-size` reserve derives from, so the reserve still equals
  the real bar box.

- **The `TmMobileActionBar` tile icon is a token-driven chip box (C13, off by default).**
  `.tm-mobile-action-bar__icon` sizes itself from the new `--tm-mobile-action-bar-icon-box`
  (default `1rem`, the `IconSize.Sm` icon — no visual change) and paints
  `--tm-mobile-action-bar-icon-chip-bg` (default `transparent`) with `--tm-mobile-action-bar-icon-chip-fg`
  ink (default `currentColor`). The tile floor, the bar height and the FixedViewport reserve
  derive the icon term from that token, so a host that turns the chip on grows them together and
  the reserve stays exact — but only while the chip tokens are set on `:root`, because the
  published reserve is evaluated with the `:root` values. A scoped opt-in (a class on one bar)
  must re-declare `--tm-mobile-action-bar-reserve-block-size` in that scope as
  `calc(var(--tm-mobile-action-bar-height) + env(safe-area-inset-bottom, 0px))`; the demo's
  `FixedViewport` tile opts in that way as the working example.

- **The `TmSignatureCapture` overlay clear becomes a compact icon-only tile below 640px of the
  component's own width.** The overlay button (a labelled one covered ≈14% of the pad at 320,
  where no stroke can start under it or pass beneath it) shrinks to exactly the 44px touch
  target at `--tm-space-1` insets and now shows the ✕ icon followed by the label (the icon is
  new — the button was text-only before). Below 640px of the component's own width the label is
  visually hidden but kept in the DOM with the full screen-reader clip (`clip-path: inset(50%)`
  alongside `clip`), so the accessible name is unchanged at every width; the overlay root pins
  `inline-size: 100%` so the container query measures the pad's real width, and the compact pad
  grows to 2.5 touch targets so the corner tile stays well under its ~10% ceiling of the pad.
  At a container 640px or wider the button shows icon + label as before.

- **Escape during a bottom-sheet drag cancels the drag first (internal change, no API surface).**
  The sheet gesture (`tm-sheet.js`) now runs on the shared `pointer-drag.js` helper instead of its
  own pointer handlers: the flick/snap/`SwipeToDismiss` behaviour is unchanged, but Escape mid-drag
  rolls the inline height back and restores the snap C# owns without closing the sheet (the Escape
  is consumed by the drag; a second Escape closes the sheet), a second finger can no longer hijack
  an active drag, and `lostpointercapture` / a missed mouse-up cancel silently.

- **`TmCalendarView` colours come from `--tm-calendar-view-*` tokens.** The today background
  and ink, the event fill/border/ink and the highlighted-date dot read six new tokens
  declared in `tokens.css` with their dark pairs in `tokens-dark.css`; the component's two
  `[data-theme="dark"]` dark blocks and ten hex fallbacks are gone. A host that restyled the
  dark calendar view must override the tokens instead of `[data-theme="dark"]
  .tm-calendar-view__event`. The highlighted-date dot moves `primary-400` → `primary-500`
  in light: 400 measured 2.54:1 (indigo 2.98:1) on the light surface, under the 3:1 floor
  for a non-text indicator; the dark dot stays `primary-400`. The selected day now paints
  `--tm-cal-selected-bg`/`--tm-cal-selected-fg` (same values, no white-on-primary literal).

- **`TmLightbox` chrome comes from `--tm-lightbox-*` tokens.** The backdrop scrim, the round
  control fills (idle + hover), the control ink and the loading-line ink read five new
  `tokens.css` tokens, replacing the last colour literals in `_lightbox.css` (the recorded
  CSS-audit debt: the `rgba(0,0,0,.85)` backdrop, the `rgba(0,0,0,.5/.8)` button fills, the
  white ink and the `rgba(255,255,255,.7)` loading text). Rendered values are unchanged —
  the scrim stays near-black by design (an image reads against an almost-black field; it is
  deliberately deeper than the `--tm-bg-overlay` TmModal dimmer, same pure-black hue), and
  the chrome ink stays white in BOTH themes, because the controls ride the scrim over an
  arbitrary image where a themed ink would flip dark and sink into its own fill. Worst-case
  contrast (a pure-white image under the whole stack) is pinned by `TokenContrastTests`:
  ink ≥ 3:1 on the idle and hover fills (icons), loading ≥ 4.5:1 on the scrim (text). A host
  can now retheme the lightbox (e.g. a lighter scrim) by overriding the tokens.
  The 390/1440 light/dark presentation was re-evaluated with screenshots over landscape,
  portrait and panorama sources, with new geometric E2E assertions for the invariants
  (image inside the viewport, close/delete never covering the image, arrows overlaying only
  the image edges on phones and sitting strictly beside it on desktop, the counter pill
  inside the viewport).

- **On a tablet layout an expanded `TmEditorShell` panel now shrinks so the canvas keeps
  `MinCanvasWidth` (C9).** Previously `MinCanvasWidth` only bounded user resizes: at 768 with the
  inspector expanded the canvas could measure ~278px. Now the expanded panel gives up the measured
  canvas deficit below `MinCanvasWidth` (clamped to `MinLeftWidth` / `MinRightWidth`), converging
  in one extra render and relaxing back towards the host width when the container grows. The fit
  stops at the panel minimum: at 768 the default 320px inspector parks at `MinRightWidth` (240) and
  the canvas keeps ~358px — everything the parked minimum allows — so it can still land below
  `MinCanvasWidth` when even the panel minimum does not leave it (freeing more width means
  collapsing the other panel to its rail). The fit width is a layout decision, not a user choice:
  it is **not** persisted under `PersistWidthsKey` and **not** reported through
  `LeftWidthChanged` / `RightWidthChanged`; `aria-valuenow` on the separator reports the rendered
  width (before: the parameter width). Visual change at 768: the inspector renders at ~240–300px
  instead of 320px.

- **The shell's mobile panels sheet full snap now reaches the top of the stage.** The sheet was
  clamped to the drawer's default `MaxHeight` (0.85), so the "full" snap covered 85% of the stage;
  as an inline sheet it now passes `MaxHeight="1"` and the full snap covers the whole stage.

- **A `TmDropdown` or `TmSplitButton` inside a `TmToolbar` is now one stop of the toolbar's roving
  tab stop instead of its own Tab stop (F14/C6).** The trigger (and, for a split button, the primary
  action too) carries `data-tm-toolbar-control` automatically (through the cascade) when it sits in
  a toolbar, so ArrowLeft/ArrowRight/Home/End reach it and Tab walks the toolbar as one stop — two
  arrow stops for a split button, still one Tab stop. Before, a dropdown in a toolbar was a second
  Tab stop the arrow keys never reached — a host had to discover and mark the hook by hand. A
  text-entry control (a native `select`/`input`) keeps its own Tab stop by design.

- **A `TmToolbarButton` with `aria-pressed="true"` (including one splatted by a host through
  `AdditionalAttributes`) now paints the pressed look** — subtle primary fill, primary ink and an
  inset primary ring. Previously the attribute was announced by assistive technology but painted
  nothing (F12/C2). Existing toolbars without `aria-pressed` are unchanged.

- **Editor caret/selection survives the rich-editor dialogs.** Opening a link/image/table/video/
  find dialog bookmarks the caret and replays it when the dialog closes — before the insertion on
  confirm, alone on cancel. The bookmark in `richEditor.js` is a NON-MUTATING Range record now (a
  `WeakMap` keyed by the editor element, the same shape `markdownEditor.js` already shipped): the
  old implementation inserted a hidden `#tm-cursor-marker` span into the edited document — it
  leaked into `getHtml()`/saved `Value`, and restoring collapsed every selection to a caret.
  `getHtml` strips any legacy marker left by an older build. `TmMarkdownEditor`'s bookmark helpers
  in `markdownEditor.js` are real Range bookmarks too (they were no-op stubs, so a dialog
  round-trip used to lose the caret entirely there).

  The replay happens strictly AFTER the dialog unmounts (close-then-insert): the dialog's focus
  trap owns inert/focus while it is rendered, so the confirm/cancel handlers only close the dialog
  and stash the pending action; `OnAfterRenderAsync` replays the bookmark and runs the insert
  (passing the editor element to `insertLink`/`insertImage`/`insertTable`/`insertVideo`) once the
  render without the dialog completed. While a dialog is open the editors still skip their
  `getHtml`/`setHtml` value sync — `TmMarkdownEditor` got the same guard it was missing.

- **Enter submits the rich-editor dialogs; find & replace reports real counts.** Every dialog's
  fields now sit in a real `<form>` (novalidate): the primary footer button is its `type="submit"`
  (owned via the `form` attribute), Cancel is `type="button"`, and the submit handler re-validates —
  an invalid URL shows the error instead of confirming. Footers use `TmButton` (Ghost Cancel,
  Primary submit) instead of raw `tm-btn-sm` buttons. Find & replace is anchored to the TOP of the
  viewport and immune to overlay clicks; Enter runs Replace (single) — Replace All is the only
  primary action and is click-only. `OnReplace`/`OnReplaceAll` return the REAL replacement count
  from `tmRichEditor.findAndReplace` (the unused `OnFind` parameter was removed); the result line
  reports the true count, and 0 renders the new localized "No matches found" message
  (`TmRichEditor_FindReplace_NoMatches`). The replace itself operates on text nodes only — it can
  no longer match inside tags or attribute values and corrupt the markup. Known limitation:
  matches are per text node; text split across inline formatting (e.g. `foo<b>bar</b>`) is not
  matched. The table dialog's grid
  preview is `aria-hidden` (pointer-only) with the chosen size announced via `aria-live="polite"`,
  and the URL placeholders are localized keys
  (`TmRichEditor_LinkDialog_UrlPlaceholder`, `TmRichEditor_ImageDialog_UrlPlaceholder`,
  `TmRichEditor_VideoDialog_UrlPlaceholder`).

- **Anchored panels present as a bottom sheet on mobile only where a component opts in.**
  `MobilePresentation` (new `PanelPresentation` enum: `Popover | Sheet | Auto`) follows the same
  viewport contract `TmModal`/`TmDialog` already ship, but `TmOverlayPanel` now **defaults to
  `Popover`** — the anchored popover at every width. `TmDropdown` and `TmPopover` keep
  `MobilePresentation="Auto"` (content-height bottom sheet below 640px, the anchored popover
  otherwise, with the trigger text as the sheet title), and `TmColumnPicker`, `TmDatePicker`,
  `TmDateRangePicker` and `TmDateTimePicker` opt into `Auto` explicitly. Every other anchored
  surface — `TmEntityPicker`, `TmQueryInput`, `TmMultiSelect`, `TmFilterableDropdown`,
  `TmTagPicker`, `TmMultiColumnComboBox`, `TmColorPicker`, `TmSplitButton`, `TmContextMenu`,
  `TmNotificationBell` and the Notion mention/AI/comment-mention menus — renders the popover on
  phones too: their anchor keeps focus (a typeahead, a caret menu, an inline toolbar) and a modal
  sheet would steal it. A host that wants the sheet for its own panel passes
  `MobilePresentation="Auto"`; anchoring options (`Placement`, `Align`, `Offset`,
  `MatchAnchorWidth`, `ConstrainHeight`) apply to the popover only. In sheet mode the panel
  renders a `TmDrawer Position=Bottom` with a header (`Title`, falling back to `AriaLabel` and a
  localized generic "Menu") and a **Done** action instead of the floating popover; selectors that
  assume the consumer panel class (e.g. `.tm-dropdown-menu`) on mobile must target
  `.tm-overlay-panel-sheet` (or the unchanged trigger) instead. See
  [docs/overlays.md](docs/overlays.md).

- **`TmToolbar` is now a real toolbar: `toolbar` role + roving tabindex, a measuring container,
  and `Labels="Auto"` by default.** The root gains the `toolbar` role with an accessible name
  (`AriaLabel`, else `Title`, else a localized "Toolbar"); its buttons become **one tab stop**
  (ArrowLeft/ArrowRight, Home, End move between them; Tab leaves the toolbar); and the root is a
  layout container (`container-type: inline-size; container-name: tm-toolbar; position: relative`)
  because the labels and the overflow resolve from the toolbar's own width. The default
  `Labels="Auto"` drops the text of **icon + text buttons** when the toolbar is narrower than 640px
  (the text stays the `aria-label`); text-only buttons keep their text. Pass
  `Labels="ToolbarLabels.IconsWithText"` for the old always-visible text. Nothing collapses unless
  you opt in with `Overflow="ToolbarOverflow.Menu"`. A host that styled `.tm-toolbar` with its
  own `position` / `container-type` or relied on every button being in the tab order must adjust.
  A `TmToolbarButton` used **outside** a `TmToolbar` (`TmDiagramEditor`,
  `TmModelingDiagramPreview`) is unchanged: the toolbar-scoped rules below (`flex-shrink: 0`, the 44px
  coarse-pointer target) apply to `.tm-toolbar .tm-toolbar-btn` only.

  **Zero intrinsic width.** Because the root is now a size container (`container-type: inline-size`) it has no
  intrinsic inline size: in a shrink-to-fit context (a flex item without basis/grow in a `space-between` row, an
  `inline-block`, a float, an absolutely positioned box, an `auto` grid column, `fit-content`) it collapses to zero
  width. *Before:* the toolbar sized itself to its buttons. *After:* give the host room —
  `.page-header > .tm-toolbar { flex: 1 1 auto; min-width: 0; }` (or `width: 100%`). Inside a toolbar
  `.tm-toolbar-btn` is `flex-shrink: 0` (a button keeps its natural width and leaves the bar into More). See
  [docs/toolbar.md](docs/toolbar.md#layout-contract-breaking-since-f4).

  ```razor
  @* before: every button was a tab stop, text always visible *@
  <TmToolbar Title="Orders">
      <TmToolbarButton Icon="@IconNames.Plus" Text="New" />
  </TmToolbar>

  @* after: same markup keeps working (one tab stop, labels drop below 640px of toolbar width);
     opt in to the old always-visible text and to overflow explicitly *@
  <TmToolbar Title="Orders" Labels="ToolbarLabels.IconsWithText" Overflow="ToolbarOverflow.Menu">
      <TmToolbarButton Icon="@IconNames.Plus" Text="New" />
      <TmToolbarButton Icon="@IconNames.Trash" Text="Delete" Priority="ToolbarButtonPriority.OverflowOnly" />
  </TmToolbar>
  ```
- **`TmDatePicker`, `TmDateRangePicker` and `TmDateTimePicker` popup panels are now
  `TmFocusScope` roots** (`TrapFocus="true"`): the calendar popup traps Tab, marks the background
  `inert` and re-claims `aria-modal="true"` — the modality the `role="dialog"` popup always
  promised. The popup element carries the scope's classes (`tm-focus-scope`); its consumer class
  (e.g. `tm-date-picker-popup`) moved onto the same root element, so panel styling is unaffected,
  but markup snapshots of the popup wrapper change. Escape keeps closing the popup and returning
  focus to the trigger.

- **`TmColumnPicker` renders its panel through `TmOverlayPanel`.** The panel now lives in the
  browser top layer (no ancestor `overflow` can clip it), closes on Escape and outside pointerdown,
  gains `aria-expanded`/`aria-haspopup` on the toggle, and presents as a bottom sheet on mobile.
  Its panel CSS no longer declares `position`/`top`/`right`/`z-index` — a host that restyled those
  properties must key on the panel's surface classes instead.

- **`TmButton` wraps its content in `span.tm-btn-label`, and the span is geometry-neutral by
  default.** The span exists so a context that shrinks the button (a 320px sheet footer) can
  truncate the text with an ellipsis instead of overflowing the panel. It renders only when there
  is content and ships as `display: contents`, so a button lays out exactly as if its content were
  a direct child. Only the sheet-footer rule in `_modal.css` opts into truncation: it blockifies
  the span (`display: block; min-width: 0`) next to the existing `overflow/ellipsis`. A host that
  wants the same in its own shrinking container targets the span, never restyles `.tm-btn`:

  ```html
  <!-- before -->
  <button class="tm-btn tm-btn-primary tm-btn-md">Save</button>
  <!-- after -->
  <button class="tm-btn tm-btn-primary tm-btn-md"><span class="tm-btn-label">Save</span></button>
  ```

  Consumer markup snapshots and selectors that assume the content is a direct child of the button
  are affected: a child selector `button > svg` still matches the `Icon` parameter's icon (rendered
  directly in the button, `TmButton.razor`), but no longer an icon placed in the button's content,
  which now sits inside `.tm-btn-label`; `svg + *` now matches the span, not the text — and styling
  that relied on bare text inside `.tm-btn` must key on `.tm-btn-label` instead.

- **`ModalPosition.Bottom` anchors the panel flush with the bottom edge.** It used to sit a margin
  above the edge (`align-self: flex-end` plus the overlay padding), so a bottom modal read as a
  floating card. It now carries `tm-modal--bottom-anchored` and sits flush, with no gap below it.
  A host that relied on the gap must add its own margin.

- **`TmDrawer` with `Position="Bottom"` is a sheet, and a non-modal drawer renders inside its
  container.** A bottom drawer anchors to the bottom edge, caps at 85% of the visible viewport,
  renders a drag handle and snaps between half and full height. A host that used a bottom drawer
  as a full-height panel sets `SnapPoints` to `1` and `ShowHandle="false"`. `Modal="false"`
  drops the overlay and positions the drawer absolutely inside a positioned container, so the page
  behind it stays usable; a modal drawer stays `position: fixed`. Below 640px a side drawer's
  `Width` is overridden to full width through `--tm-drawer-width`.

- **A modal or dialog footer no longer stacks its buttons below 640px.** The old rule turned every
  footer into a `column-reverse` stack on a narrow viewport, which pushed the confirm action below
  the fold. Buttons stay side by side at every width unless the host sets `FooterLayout="Stacked"`.
  A host that relied on the stack must set that parameter.

- **`TmModal` and `TmDialog` present as a bottom sheet on a mobile viewport.** `MobilePresentation`
  defaults to `Auto`, which follows the viewport scope: a sheet below 640px, the centered dialog
  otherwise. A host that wants the centered dialog at every width sets
  `MobilePresentation="Dialog"`. An app should place `<TmLayoutObserver IsViewportScope="true"
  IsContainer="false">` as its outermost layout element. Without it, an open overlay measures its
  own viewport through an internal probe, so the sheet still appears, but the first frame can flash. See
  [docs/responsive-conventions.md](docs/responsive-conventions.md).

- **`TmModal`, `TmDialog` and `TmDrawer` render a `TmFocusScope` root.** The dialog role, `tabindex`
  and `data-testid` now sit on `div.tm-focus-scope`. Escape is a document keydown handled by the
  topmost overlay only. A modal marks the page behind it `inert` and locks document scroll. A
  drawer title id is unique per instance. The focus ring on a sheet handle paints the bar only.

- **`TmGanttImportDialog`'s dialog is a `TmFocusScope` root too**, so its Tab cycle, Escape and the
  inert background behave like every other overlay, and its Jira form now renders real
  `TmFormField` labels (the file omitted the namespace, so the tag compiled to an unknown element
  and the three labels vanished).

- **The date-picker popups no longer emit `aria-modal="true"`.** `TmDatePicker`, `TmDateRangePicker`
  and `TmDateTimePicker` keep `role="dialog"`, but nothing inerted the page behind the popup, so the
  attribute announced a modality the panel does not have. Moving the popups onto `TmFocusScope` is
  deferred to the F3 phase.

- **A drawer backdrop is a child of the focus-scope root, and it carries `data-tm-backdrop`.** It
  used to be a sibling that preceded the root. The trap marks every sibling `inert`, so that
  sibling swallowed the click that should close the drawer.

  ```html
  <!-- before: the overlay is outside the trapped root, so the trap marks it inert -->
  <div class="tm-drawer__overlay"></div>
  <div class="tm-focus-scope tm-drawer" role="dialog">…</div>
  <!-- after: the overlay is inside the root, and the marker is the only backdrop contract -->
  <div class="tm-focus-scope tm-drawer" role="dialog">
    <div class="tm-drawer__overlay" data-tm-backdrop></div>
    …
  </div>
  ```

  A backdrop without `data-tm-backdrop` is inert. A class name (`.tm-drawer__overlay`,
  `.tm-command-palette-backdrop`) is not recognised. `TmCommandPalette` and `TmKeyboardShortcutsHelp`
  mark their backdrops the same way.

- **A drawer panel stacks above its backdrop, for every position.** The overlay is `position: fixed`
  inside the root, so it painted over the static panel and every click in a left or right drawer hit
  the backdrop and closed it. `.tm-drawer__panel` is now `position: relative; z-index: 1` and
  `.tm-drawer > .tm-drawer__overlay` is `z-index: 0`. Both live in the root's stacking context, so the
  panel wins without a calc against the backdrop token. Below 640px a side panel is full width, so
  there is no backdrop point to click; close it from its own button.

- **`.tm-modal` and `.tm-dialog` end at `transform: none`.** The visible state used to be
  `transform: scale(1)`, which is still a containing block. A sheet opened inside that modal then
  anchored to the modal instead of the viewport. Host CSS that keyed off the identity transform
  must accept `none`.

- **`.tm-modal-container` no longer receives pointer events.** It fills the overlay, so a click on
  the dimmed area used to land on the container and never reached the handler — `CloseOnOverlayClick`
  already defaulted to `true`, but the click physically could not arrive, so dimmed-area closing did
  not work. The container is `pointer-events: none`; the scope (`> .tm-focus-scope`) takes its own
  clicks back. A host that wants clicks on the dimmed area ignored sets `CloseOnOverlayClick="false"`.

- **A modal or dialog sheet sizes to its content.** It used to snap. `TmModal` and `TmDialog` now
  render `tm-sheet--content` and pass no snap points, so the panel is `height: auto` under
  `MaxHeight`. A host that wants snaps composes `TmDrawer`.

- **`TmDrawer.SnapIndex` is two-way.** The drawer adopts a change of the parameter, and a gesture
  reports the snap it settled on through `SnapIndexChanged`. A parent re-render that passes the
  same index does not reset a snap the user just dragged to. `--tm-sheet-height` is written only by
  Blazor; the gesture must not delete it. Snap points are clamped to `MaxHeight` and de-duplicated
  before the gesture sees them, so a snap of `1` under a cap of `0.85` is one snap, not two. A
  one-snap sheet dismisses only past a quarter of the height it started at, or on a downward flick.
  An upward drag never dismisses.

- **An inline sheet is a fraction of its host.** `Modal="false"` renders `position: absolute;
  inset: 0` inside a positioned host, measures snaps against that host, and does not track the
  keyboard. The panel height is `calc(var(--tm-sheet-height) * 100%)` of that host, not of the
  viewport, so a host that is not positioned lets the sheet escape it. Escape closes an inline
  sheet only when focus is inside it. The root uses `--tm-z-inline-sheet` (10), under the modal band.

- **`TmLightbox`, `TmKeyboardShortcutsHelp` and `TmCommandPalette` render a `TmFocusScope` root.**
  Tab stays inside, the dialog role and `tabindex` sit on `div.tm-focus-scope`, and Escape is the
  scope's document keydown. `TmLightbox` no longer also handles Escape itself, which closed it
  twice. A test that looked for `role="dialog"` on `.tm-lightbox` or `.tm-keyboard-shortcuts` finds
  it on the scope.

- **`TmSheetHandle.SnapValue` drives the value text.** The handle announces `TmSheet_SnapPercent`
  (`{0} %`). `TmSheet_SnapIndex` is no longer the announcement.

- **An overlay with no viewport scope measures the viewport itself through `ViewportProbe`.** The
  probe is an internal component the overlay opens from C#: Razor cannot bind a tag to a non-public
  component, and with RZ10012 suppressed the `<ViewportProbe />` tag silently compiled to a literal
  HTML element that never measured — so no-scope overlays stayed on their initial mode and the
  missing-scope hint never logged. RZ10012 is no longer suppressed. (An intermediate
  `.tm-layout--fallback` stylesheet class shipped in this cycle but nothing ever rendered it; it is
  deleted.)

- **Side-drawer shadows use a token.** The panel shadow is `var(--tm-shadow-xl)`, not a hardcoded
  value. The modal sheet media query is `width < 640px`: a viewport of exactly 640px is no longer
  inside the sheet rule.

- **`TmViewManager` ids are per instance.** The toggle used to carry no id at all and the name field
  only `TmTextInput`'s random default, so nothing named which manager's toggle to restore or which
  field to focus — a page with three managers restored focus to the first. Each instance now
  suffixes real ids (`id` + `data-tm-id`) for the toggle and the name field, the label points at the
  field, the modal's initial focus is that instance's name field, and Escape restores the instance's
  toggle.

- **`TmDashboard` closes its menu when a delete dialog opens.** The menu no longer stays open
  behind the dialog.

- **Bottom-sheet dismiss gestures are measured, not positional.** A snapped sheet dismisses only
  below the lowest snap by 0.15, or on a downward flick — and a flick moves ONE snap in its
  direction from the snap the drag started on, so the ordinary "back to half" swipe from full
  (a 300px drag over 300ms, about 1 px/ms) steps down to half instead of closing the sheet; only a
  release faster than about 2 px/ms closes it from any snap, and an upward flick steps one snap up.
  A one-snap sheet dismisses past a quarter of the height it started at, or on a downward flick. A
  content sheet dismisses past a quarter of the height it started at, or on a flick. The flick is
  the travel over the last 80ms of the drag, not the gap between the last move and `pointerup` (a
  real release lands on that last move, so a velocity taken from it alone is always 0). A tap never
  dismisses, and an upward drag never dismisses.

- **Sheets grab through the shared `TmSheetHandle` (`.tm-sheet__handle`), named by the new `TmSheet_*`
  resource keys.** There were no grabber classes or sheet keys before, so nothing to migrate — a
  host that hand-rolled its own grabber can compose the same component. A `TmModal` or `TmDialog`
  sheet renders the grabber only while the sheet can be dismissed (`CloseOnOverlayClick`), so it
  never advertises a swipe that does nothing.

- **Document scroll lock is `html.tm-scroll-lock`.** A modal overlay did not lock document scroll
  before; the lock is new. It is a ref count: it stays while any modal trap is open and leaves when
  the last one closes, so two open modals do not leave the class behind.

- **A desktop sheet caps at 48rem.** `[data-layout="desktop"]` bottom drawers and modal sheets set
  `max-width: 48rem` and center. They used to be full-bleed at every width. A host that stretched a
  desktop sheet to the viewport must override that cap.

- **`TmViewManager` no longer renders `.tm-view-modal`.** The create/edit form is a `TmModal`: the
  title and close button are the modal's, and Cancel/Create sit in its footer. Host CSS that
  targeted `.tm-view-modal`, `.tm-view-modal-overlay` or `.tm-view-modal-footer` no longer matches.

- **`TmDashboard` widget placement is no longer an inline `grid-column`.** A widget used to carry
  `style="grid-column: X / span W; grid-row: Y / span H"`. It now carries the placement as custom
  properties (`--tm-w-x`, `--tm-w-span`, `--tm-w-y`, `--tm-w-rows`, `--tm-w-order`) and the grid
  stylesheet turns them into columns. The desktop result is the same 12-column grid. Below 1024px
  of the dashboard's own width the grid becomes six columns, two below 768px, and one below 640px — a widget
  wider than half the desktop grid spans the full row in the two-column layout.

  ```css
  /* before: an inline style, so no container query could restack the widget */
  .tm-widget { grid-column: 1 / span 8; }
  /* after: the stylesheet reads the variables */
  .tm-widget { grid-column: var(--tm-w-x) / span var(--tm-w-span); }
  ```

  A host rule that targeted the inline `grid-column` must target the variables or the
  `.tm-widget--wide` class instead. The old `@media (max-width: 560px)` stacking rule is gone;
  the restack follows the dashboard container, so a narrow dashboard on a wide page stacks too.

  The dashboard root is no longer the outermost element. `TmDashboard` now renders inside a
  `TmLayoutObserver`, so a selector that assumed `.tm-dashboard` was the component root must
  step through `.tm-layout.tm-dashboard-layout` first. That wrapper is `IsContainer="false"`, so it
  carries `tm-layout--no-container` and the grid's own `container-name: tm-dashboard` stays the one
  the queries match.

- **`.tm-hide-mobile` hides below 768px and `.tm-hide-desktop` hides from 768px up.** Both used to
  be off by a pixel (`max-width: 768px` hides 768, `min-width: 769px` hides from 769), so 768px was
  hidden by neither. The conditions are now half-open and complementary: `(width < 768px)` and
  `(width >= 768px)`. A viewport of exactly 768px that previously showed `.tm-hide-desktop` content
  now hides it.

- **Structural layout follows `data-layout`, not a container query.** `TmDashboard` renders
  `data-layout="desktop|tablet|mobile"` from the resolved mode, and the grid reads that: 12 columns
  on desktop, 6 on tablet, 1 on mobile. The two-column step is a container query inside tablet,
  below 768px of the dashboard's own container. Forcing `LayoutMode="Mobile"` now stacks the grid;
  previously a forced mode only changed the reported attribute.

- **Drag and resize are desktop only.** Below the desktop layout the grid has restacked, so the
  drag handle and the resize handles are not rendered. Add and remove stay available.

- **`.tm-btn-icon` is a square driven by the size modifier.** It used to fix the width at the md
  height. It is now `width: auto; aspect-ratio: 1`, so `tm-btn-sm` is 32×32 and `tm-btn-lg` follows
  the lg height. A coarse pointer still grows it to `--tm-touch-target`.

- **Safe-area helpers add to padding instead of replacing it.** `.tm-safe-area-bottom` and
  `.tm-safe-area-top` use `calc(var(--tm-safe-area-base, var(--tm-space-2)) + env(safe-area-inset-*, 0px))`.
  Set `--tm-safe-area-base` to the component's own padding. The inset is zero unless the host page
  sets `viewport-fit=cover`.

- **`TmLayoutObserver.LayoutModeChanged` is now `ResolvedLayoutChanged`.** The callback reports the
  resolved mode, so pairing it with `LayoutMode` in a two-way bind would pin that mode. `TmDashboard`
  renames its callback the same way. An owner that must branch its own markup uses the new
  `LayoutContent` (`RenderFragment<TmLayoutContext>`) instead of keeping a copy of the mode; setting
  both `ChildContent` and `LayoutContent` throws. `TmDashboard` also forwards `InitialMode`.
  `IsViewportScope` also cascades the context under `TmLayoutScopes.Viewport`, which a surface
  positioned against the viewport reads; an in-container surface reads the unnamed context. The
  no-container modifier
  is `tm-layout--no-container` (it was `tm-layout--shrink`).

- **`TmLayoutObserver` gains `IsContainer` and `InitialMode`.** `IsContainer` defaults to true;
  shrink-to-fit hosts such as a popover pass false so the root does not establish a containment
  context. `InitialMode` defaults to Desktop and is the mode rendered before the first measurement
  (static SSR, prerender, the first frame). `ChildContent` stays a `RenderFragment`; the resolved
  layout is cascaded as `TmLayoutContext`, so a child reads it with `[CascadingParameter]`.

- **`TmDialog` default button texts now come from the localizer.** `OkButtonText` and
  `CancelButtonText` used to default to the English literals `"OK"` and `"Cancel"`, so a Czech
  UI that did not pass its own text rendered "Cancel". Both parameters are now nullable and fall
  back to `Loc["Tm_Ok"]` / `Loc["Tm_Cancel"]` when no value is passed. An explicit value still
  wins, so callers that already pass their own text see no change.

  ```razor
  @* before: rendered "Cancel" in every culture *@
  <TmDialog Type="DialogType.Confirm" Show="true" Title="Smazat" />
  @* after: renders "Zrušit" under cs, "Annuler" under fr, "Cancel" under en *@
  <TmDialog Type="DialogType.Confirm" Show="true" Title="Smazat" />
  ```

  A caller that relied on the English literal while running under another culture must now pass
  `CancelButtonText="Cancel"` explicitly.

- **`TmDialog.PromptPlaceholder`, `TmModal.OkButtonText` and `TmModal.CancelButtonText` follow the
  same rule.** Each was a non-nullable string defaulting to an English literal. Each is now
  nullable and falls back to the localizer (`TmDialog_PromptPlaceholder`, `Tm_Ok`, `Tm_Cancel`).

  ```razor
  @* before: the prompt input showed "Enter value..." in every culture *@
  <TmDialog Type="DialogType.Prompt" Show="true" Title="Název" />
  @* after: "Zadejte hodnotu…" under cs, "Saisissez une valeur…" under fr *@
  <TmDialog Type="DialogType.Prompt" Show="true" Title="Název" />
  ```

- **`TmModal` no longer renders an empty footer.** The footer bar appears only when a `Footer`
  fragment is passed or `ShowDefaultFooterButtons` is set. A modal that relied on the empty bar's
  border must pass its own footer.

- **Core controls adopt the two-layer focus ring.** These rules used to paint
  `box-shadow: var(--tm-shadow-focus)`, a single soft glow, and now paint `box-shadow:
  var(--tm-focus-ring)`, the two-layer ring that holds 3:1 against the surface in both themes:
  `.tm-btn:focus-visible`, `.tm-btn.tm-btn-warning:focus-visible`, `.tm-input:focus` (which also
  covers `.tm-textarea`, since it compounds on `.tm-input`), `.tm-select:focus`,
  `.tm-search-input:focus`, `.tm-search-clear:focus-visible`, and the checkbox, radio and toggle
  `focus-visible` rules.

  Invalid and valid states get their own rings instead of the glow or a hardcoded green:
  `.tm-btn-danger:focus-visible`, `.tm-input-error:focus`, `.tm-select-error:focus`,
  `.tm-number-input--error:focus-within` and `.tm-decimal-input__control--error:focus-within` paint
  `var(--tm-focus-ring-danger)`; `.tm-input-valid:focus` paints `var(--tm-focus-ring-success)`. The
  success ring uses `--tm-color-success-strong`, because `--tm-color-success` (`#22c55e`) measures
  2.28:1 on white, under the 3:1 floor. The glow tokens are unchanged and still available.

  ```css
  /* before */
  .tm-btn:focus-visible { box-shadow: var(--tm-shadow-focus); }
  /* after */
  .tm-btn:focus-visible { box-shadow: var(--tm-focus-ring); }
  ```

  To restore the old glow everywhere, override the ring token on all three selectors a theme block
  uses. A consumer override of a `var()`-valued token has to target the same three selectors,
  because a custom property substitutes its `var()` where it is declared and a dark region does not
  see an override that lives only on `:root`:

  ```css
  :root, [data-theme="dark"], .tm-dark { --tm-focus-ring: var(--tm-shadow-focus); }
  ```

- **The rest of the core focus rules paint `--tm-focus-ring` too.** The date picker and
  date-range triggers, the multiselect (root, filter input, confirm button and open state),
  the filterable dropdown filter input, the entity picker and masked text box inputs, the
  time input, the pagination page-size and column-filter inputs, the code/markdown/rich-text
  editors and their find/replace, image, link and video dialog inputs, the pivot field
  editor inputs and the import-wizard map select used to paint `var(--tm-shadow-focus)` and
  now paint `var(--tm-focus-ring)`. Focus rules that removed the outline for the ring keep a
  transparent 2px outline (`outline: 2px solid transparent`), and so do the focus rules
  migrated earlier (`.tm-btn`, `.tm-input`, `.tm-select`, `.tm-search-input`, the editor-shell
  panel toggle/resizer/tab, the search clear button, the overlay-panel Done button and the
  side-panel close button): under Windows High Contrast (`forced-colors: active`) the UA
  forces `box-shadow: none`, and the transparent outline is then painted in the system
  colour, so focus stays visible. The calendar heatmap cell (12px, 4px gap) cannot host the
  4px ring — it drops the glow and keeps its 2px outline as the only indicator. The same
  restore override applies.

- **In the dark theme the in-range days of a date-range picker fill with `primary-900`.**
  `--tm-cal-range-bg` used to keep the light `primary-100` in the dark theme, so dark day
  text sat on a light fill at 1.1:1 (indigo 1.12:1). `primary-900` holds 9.45:1 (indigo
  10.43:1) for `--tm-text-primary` and is already the dark convention for a primary-tinted
  fill (`--tm-control-hover-fill`). Light is unchanged.

- **A scheduler event without its own colour now fills with `--tm-scheduler-event-bg`.** In the dark
  theme that token is `primary-600`, where it used to be `primary-400`. White event text holds AA on
  `primary-600` and did not on `primary-400` (2.54:1 default, 2.98:1 indigo). Light is unchanged,
  because `primary-600` was already the light fill. An event that sets `Color` is unaffected.

- **The calendar picker's selected day fills with `--tm-cal-selected-bg`, now `primary-600`.** It was
  `var(--tm-color-primary)`, whose dark step is `primary-400`, and white day text on that falls short
  of 3:1. `primary-600` holds AA in both themes. The `--tm-cal-*` tokens moved from
  `_picker-shared.css` into `tokens.css`, and the unused `--tm-cal-today-border` is removed. A
  stylesheet that referenced `--tm-cal-today-border` must drop it.

- **`--tm-priority-critical` is now `--tm-priority-highest`, and `--tm-priority-lowest` is new.**
  The suffix matches the lower-cased `TmWorkItemPriority` member. A stylesheet referencing
  `--tm-priority-critical` must switch to `--tm-priority-highest`.

- **Every modal viewport-anchored overlay root lives in the browser top layer.** A modal
  `TmDrawer` at any position, the `TmModal`/`TmDialog` overlay root, the `TmCommandPalette`
  backdrop, the `TmKeyboardShortcutsHelp` overlay, the `TmLightbox` focus-scope root, the
  `TmGanttImportDialog` overlay and both toast containers (`TmToastContainer`,
  `TmNotificationToastContainer`) carry `popover="manual"` and promote themselves at open
  through the shared promote helper, so the top-layer order equals the open order (a dialog
  opened from inside a sheet paints above it; a toast pushed while a sheet is open lands above
  it). The toast containers are pinned on top: they re-raise on every push (including at the
  `MaxVisible` cap) and after every promotion of another surface, and they are exempt from the
  modal trap's inert background, so a toast on screen stays visible, reachable and announced.
  Host chrome that relied on a z-index band above modals (its own bar at a z-index higher than
  `--tm-z-modal` painting over an open modal) now paints UNDER every open modal surface — only
  another promoted surface can cover one. The z-index bands in `tokens.css` order only
  non-promoted surfaces and the no-Popover-API fallback. Non-modal anchored popovers
  (`TmOverlayPanel`) were already top-layer and are unaffected. Extension-package dialogs (rich
  text editor, Notion, Spreadsheet, DocumentEditor, Signing, the dashboard widget-selector
  layer) are recorded exceptions — they still paint in their z-index band; see
  [docs/overlays.md](docs/overlays.md) rule 9. See [docs/overlays.md](docs/overlays.md).

- **`TmToastContainer` is `IAsyncDisposable` now (the public `Dispose` is gone).** Blazor's
  renderer disposes `IAsyncDisposable` components without any host change; a host that called
  `Dispose()` explicitly must drop the call. Same for `TmNotificationToastContainer`.

- **`TmDashboard` edit mode on mobile renders its actions through `TmMobileActionBar` instead of
  the wrapping toolbar.** Below the mobile breakpoint the toolbar's Add/Save/Cancel buttons are
  hidden by CSS in edit mode (the bar owns them; view mode keeps its Edit entry point), and
  the bar markup (`.tm-mobile-action-bar`) appears as the last child of the dashboard root. The
  desktop toolbar is untouched above the breakpoint. Note: in mobile edit mode the
  `data-testid="dashboard-add-widget"` hook lives on the hidden desktop toolbar — the bar's Add
  Widget action carries `data-action-id="add-widget"` instead. A host that restyled the mobile
  edit toolbar should target the bar classes:

  ```html
  <!-- before (mobile edit mode) -->
  <div class="tm-dashboard-toolbar-right">
    <button class="tm-btn tm-btn-primary">…Add Widget</button>
    <button class="tm-btn tm-btn-secondary">…Save Changes</button>
    <button class="tm-btn tm-btn-ghost">…Cancel</button>
  </div>
  <!-- after (mobile edit mode) -->
  <div class="tm-mobile-action-bar tm-mobile-action-bar--sticky tm-mobile-action-bar--bare" data-layout="mobile">
    <div class="tm-mobile-action-bar__bar tm-mobile-action-bar__bar--sticky" role="group" …>…</div>
  </div>
  ```

- **Counted texts are grammatical now: `Loc.Plural`, and the B16 stop-gap wording is gone.** Czech
  counts used to read "Čekající změny: 3" (F0 neutral wording) or, before that, the wrong "1 čekajících
  změn". Counted resources are now plural families (`Key_One` / `Key_Few` / `Key_Many` / `Key_Other`,
  resolved by `TmPluralRules` from the localizer's culture): "1 čekající změna", "2 čekající změny",
  "5 čekajících změn". The affected keys no longer exist as a single plain key —
  `TmDocumentEditor_ReviewSummaryRevisions`, `…ReviewSummaryComments`, `TmDocumentEditor_CommentCount`,
  `TmModelingIssuePanel_Count`, `TmModelingModelTree_Count`, `TmWireframeProps_ElementsCount`,
  `TmNotificationBell_NewCount`, `TmNotionPageComment_BadgeLabel`,
  `TmNotionPageHeader_UnresolvedComments`, `TmScreeningResultPanel_PendingCount`,
  `TmChat_ReplyCount`, `TmAuditLogViewer_EventsCount`, `TmDataImport_MoreErrors` — and
  `TmDocumentEditor_ReviewSummaryText` is now a two-slot joiner `"{0}, {1}"` over the new
  `TmDocumentEditor_ReviewSummaryOpenComments_*` family. **Migration:** a host that *replaces*
  `ITmLocalizer` and overrides these keys must override the family keys (`_One` / `_Other`, plus
  `_Few` / `_Many` where its language needs them). Overriding only the plain key is **not** enough
  for the documented delegating pattern (`PartialOverrideLocalizer` / `MyPartialLocalizer`): the
  default `TryGetString` reads `Key_One` through the indexer, the inner localizer returns the shipped
  family, and the plain-key override is never consulted. The plain key is used only when no family
  exists anywhere. A host class that already has its own public `Culture` or `TryGetString` now
  implicitly implements the new interface members — a name match, not a behaviour change, but it will
  not compile if the signature differs.

  ```csharp
  // before - a delegating localizer overriding only the plain key. Plural never sees it:
  // TryGetString("TmModelingIssuePanel_Count_One") reads the shipped family through the indexer.
  public sealed class PartialOverrideLocalizer(ITmLocalizer inner) : ITmLocalizer
  {
      public System.Globalization.CultureInfo Culture { get; } = System.Globalization.CultureInfo.GetCultureInfo("cs");
      public string this[string key] => key == "TmModelingIssuePanel_Count" ? "Nálezy: {0}" : inner[key];
      public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
  }
  // after - override the family. _Few/_Many too, where the language needs them (Czech does).
  public sealed class PartialOverrideLocalizer(ITmLocalizer inner) : ITmLocalizer
  {
      private static readonly Dictionary<string, string> Mine = new()
      {
          ["TmModelingIssuePanel_Count_One"]   = "{0} nález",
          ["TmModelingIssuePanel_Count_Few"]   = "{0} nálezy",
          ["TmModelingIssuePanel_Count_Many"]  = "{0} nálezu",
          ["TmModelingIssuePanel_Count_Other"] = "{0} nálezů",
      };
      public System.Globalization.CultureInfo Culture { get; } = System.Globalization.CultureInfo.GetCultureInfo("cs");
      public string this[string key] => Mine.TryGetValue(key, out var mine) ? mine : inner[key];
      public string this[string key, params object[] arguments] => string.Format(Culture, this[key], arguments);
  }
  ```

  See [docs/localization-plurals.md](docs/localization-plurals.md).

- **Relative time is one shared formatter; the `TmTimeline_*Ago` keys are no longer read.**
  `TmTimeline`, `TmActivityTimeline` and `TmActivityComments` ask `ITmRelativeTimeFormatter`
  (registered by `AddTempoBlazor()`), which also says "yesterday at 4:20 PM", handles the future
  ("in 3 h", "tomorrow at 9:00") and prints an absolute date from a week on. The strings are
  `Tm_Time_*`; `TmTimeline_JustNow`, `TmTimeline_MinutesAgo`, `TmTimeline_HoursAgo`,
  `TmTimeline_DaysAgo` and `TmTimeline_DateFormat` are not used by the library any more — and they
  are now removed from the resources entirely (see the removal entry under Breaking / Migration
  above). **Migration:** override
  `Tm_Time_*` (or register your own `ITmRelativeTimeFormatter`) instead of `TmTimeline_*Ago`:

  ```csharp
  // before
  services.AddSingleton<ITmLocalizer, MyLocalizer>();   // overriding "TmTimeline_MinutesAgo"
  // after
  services.AddSingleton<ITmLocalizer, MyLocalizer>();   // overriding "Tm_Time_MinutesAgo"
  services.AddScoped<ITmRelativeTimeFormatter, MyFormatter>();   // scoped, like the built-in one
  ```

  Timestamps older than a week now show the absolute date in the culture format (`Tm_Time_DateFormat`)
  instead of `TmTimeline_DateFormat`; a timestamp between one and six days old reads "2 days ago"
  (it used to be a date after 24 hours). `TmActivityTimeline` and `TmActivityComments` now show that
  absolute date after 7 days; before, both kept saying "N days ago" forever.

  The formatter is registered **scoped** (it reads the scope's `ITmLocalizer` and `TimeProvider`).
  Calendar days are judged in `TimeProvider.LocalTimeZone`. On Server and prerender that zone is the
  server's, so the host registers a scoped `ITmRelativeTimeFormatter` over a user `TimeProvider` that
  reads the zone lazily (user decision Q1=A). Never replace the app-wide `TimeProvider`: the host's
  singletons consume it, and a scoped replacement fails startup under `ValidateScopes`. A shared
  `ITmUserTimeZone` is a recorded follow-up, not a component parameter.

- **`scheduler.js` resizes with Pointer Events and loads `pointer-drag.js`.** The document-level
  `mousedown`/`mousemove`/`mouseup` listeners are replaced by the shared helper, so a finger or pen can
  resize an event (B13) and Escape or a browser takeover (`pointercancel`) rolls the resize back without
  calling .NET. `window.TempoBlazor.Scheduler.init/registerInstance/unregisterInstance/destroy` and the
  `NotifyResizeComplete` callback are unchanged. **Migration:** the script now `import()`s
  `pointer-drag.js` from the folder `scheduler.js` itself was served from, so both files must be
  reachable from the same `_content/Tempo.Blazor/js/` path (no change for the standard static-asset
  setup). Before, scheduler.js was a classic script and needed nothing else; after, a bundler or a Content-Security-Policy that restricts `script-src` must allow pointer-drag.js the same way it allows
  `scheduler.js`. `window.TempoBlazor.Scheduler.ready` is a new promise that resolves when the helper
  is attached.

- **`TmKeyboardShortcutsHelp` names its dialog.** Before, the dialog root carried `role="dialog"` and
  no accessible name, and the title was a bare `<h2 class="tm-keyboard-shortcuts-title">` directly
  inside `.tm-keyboard-shortcuts-header`. After, the title carries a stable `id` and the dialog's
  `aria-labelledby` points at it. The element order is unchanged when `Description` is null: the `h2`
  stays a direct child of the header. A description adds a `.tm-keyboard-shortcuts-heading` wrapper
  around the title and the subtitle.

- **`TmCopyButton` localizes its accessible name.** Before, the button's `aria-label` was the fixed
  English "Copy to clipboard", and it never changed after a copy. After, it reads
  `TmCopyButton_CopyToClipboard` ("Copy to clipboard" / "Kopírovat do schránky" / "Copier dans le
  presse-papiers") and switches to `TmCopyButton_Copied` ("Copied") once the copy succeeds, so the
  announced name follows the visible state.

- **`TmCommandPalette` names its result list.** Before, `<ul class="tm-command-palette-list" role="listbox">`
  had no accessible name (axe `aria-input-field-name`). After, it carries
  `aria-label="@Loc["TmCommandPalette_Aria"]"` ("Command palette" / "Paleta příkazů" / "Palette de
  commandes"). No other default markup of the palette changes.

- **`TmImageGallery` items are `<button type="button" class="tm-gallery-item">` (was a `div`
  with `role="button"`/`tabindex="0"`).** A native button activates itself — Enter and Space
  work in every browser through the browser's own click, no emulated key handlers — and gives
  the lightbox's focus restore something focusable to return to. Items carry per-instance ids
  (`tm-gallery-{uid}-item-{index}`), the accessible name is the image `Title` or a localized
  "Open image {0} of {1}", the `<img>` is `alt=""`, and the visible title is
  `<span class="tm-gallery-item-title">` (was a `div`).

  ```razor
  @* before *@
  <div class="tm-gallery-item" tabindex="0" role="button">…</div>
  @* after *@
  <button type="button" class="tm-gallery-item" id="tm-gallery-…-item-0">…</button>
  ```

  **Migration:** host CSS that styled `div.tm-gallery-item` or relied on block children must
  target the class (the library's own reset turns the UA button face back into the card);
  selectors or tests keyed on `div.tm-gallery-item` / `[role=button]` must target
  `button.tm-gallery-item`. Closing the lightbox now returns focus to the thumbnail of the
  image on screen (after browsing, that is the image being shown, not the original opener) —
  see `TmLightbox.RestoreFocusTargetId` under Added. The wireframe gallery stencil is
  unchanged (same visual).

- **The demo API allows any loopback origin in Development.** The CORS default policy no longer
  hard-codes `localhost:5010`/`localhost:7106`: the launch-profile origins stay allowed as hard
  defaults in `DemoCorsPolicy.DefaultOrigins`, `Cors:AllowedOrigins` (env
  `Cors__AllowedOrigins__0=…`) is purely additive on top of them (`appsettings.json` ships an
  empty list; configured entries are trimmed and a trailing slash is stripped), and in the
  Development environment any http(s) origin on a loopback host (`localhost`, `127.0.0.1`,
  `[::1]`) on ANY port is accepted — so a demo/E2E WASM host started on a nonstandard port
  (e.g. 7116) can call the API without editing it. Production answers only the defaults plus the
  configured list. Credentials stay enabled for the SignalR WebSocket handshake
  (`SetIsOriginAllowed` echoes the origin; the policy lives in `DemoCorsPolicy`).
- **The repository enforces LF.** `.gitattributes` now carries `* text=auto eol=lf` (every text
  file is LF in the index and in every working copy, whatever `core.autocrlf` says) with
  `eng/release-evidence/** -text` kept byte-verbatim. Before, a Windows checkout made under
  `core.autocrlf=true` produced CRLF working copies that broke the workflow-reading packaging
  guards and the frozen `TmTabs` markup baseline; those tests now also normalise CRLF→LF at the
  reader, so they pass on any checkout. Existing Windows working copies keep their CRLF files
  until refreshed: re-clone, or — on a clean tree — plain `git checkout -- .` re-checks out every
  text file as LF (the index is already LF, so no renormalization commit is needed).

- **`TmDropdown`, `TmSplitButton` and `TmContextMenu` now move focus to their first enabled item when the menu
  opens** (`tm-menu-nav.js` `focusFirst`, once per open, best-effort without JS). *Before:* focus stayed on the
  trigger and the roving keys — which listen on the menu — were unreachable from the keyboard until a Tab. *After:*
  Enter/Space on the trigger lands on the first item; Escape/selection return focus to the trigger. Tests that
  asserted the trigger keeps focus after opening must adjust.
- **`TmDrawer` and `TmModal` bodies that overflow gain a tab stop.** Both now run the shared
  `syncScrollRegion` (`tm-focus-trap.js`), exactly like `TmDialog` already did: while the body
  overflows it becomes `role="region"` with `tabindex="0"` (named by the title when there is one) so
  a keyboard user can scroll it; a body that fits grows no extra tab stop and the region never takes
  initial focus. *Before:* the overflowing body was not focusable, so keyboard users could not scroll
  it and Tab went straight from the header to the first control. *After:* one extra Tab stop on the
  scroller, only while it overflows. Tests that count tab stops inside a long drawer or modal body,
  or that snapshot the body element, will see the `tabindex` / `role` attributes.

- Czech count strings that used one plural form for every number (`{0} položek`, `{0} komentářů`,
  `{0} čekajících změn`, `{0} řádků`) now use a neutral phrasing (`Počet: {0}`, `Komentáře: {0}`,
  `Čekající změny: {0}`), so a count of one no longer reads as a plural. (a stop-gap step, superseded by the `Loc.Plural` families in the counted-texts entry above)
- Czech count strings for a further 30 keys now use the same neutral phrasing (`Nálezy: {0}`,
  `Stránky: {0}`, `Prvky: {0}`, and the rest listed in the review of B16). `Tm_More` is `Více`.
- Scheduler event text on a consumer-supplied fill uses `--tm-scheduler-event-fg`, and the
  time-grid event wash mixes the event colour into `--tm-bg-surface` instead of white. The
  filter-builder apply button uses `--tm-color-on-primary`. The data-table card mode drops its
  hardcoded white and gray fallbacks, and the
  dashboard widget-selector overlay uses `--tm-bg-overlay` and sits in the `--tm-z-modal` band
  instead of the dropdown band (`z-index: 1000`), so it paints above an open drawer.

- **Documentation — the generated JSON documentation now lists expression-bodied properties as
  `Property` members and includes positional record parameters.** The doc generator's member
  scanner only recognised `{ get;` properties and read every other line up to its first `=`, so
  members like `public bool IsMobile => …;` were mis-kinded as `"kind": "Field"` (272 member
  entries flip from `Field` to `Property` in the regenerated `tempo-blazor-all.json`) and
  everything whose body wrapped — `=> Resolved switch …`, `{ get => …; init => …; }` — was
  missing entirely, as were the generated properties of positional records (`TmLayoutContext`
  listed 3 of its 6 members). The bundles are regenerated; consumers that filtered members by
  `kind == "Field"` must match on `"Property"` instead, and types modelled as positional records
  gained member entries. Overlay member descriptions take precedence over generated ones — the
  overlays under `JsonDocumentation/` stay the editable source, so regeneration no longer blanks
  a hand-written member description. Declarations whose signature wraps across lines, and
  tuple-typed expression-bodied properties, are not yet extracted.

- **`TmCommandPalette` is a real combobox, its options carry stable ids, and a click on the
  backdrop closes it.** The search input is now the `role="combobox"` owning the result listbox:
  it gained `aria-controls` (pointing at the listbox id — which also takes the scrolling list out
  of axe's `scrollable-region-focusable` matcher), `aria-expanded`, `aria-autocomplete="list"` and
  `aria-activedescendant`, which follows the ArrowUp/ArrowDown highlight so a screen reader
  announces the row the arrows moved to. Every `role="option"` row now renders a stable `id`.
  Grouped markup changed: `group` is not an allowed role for `li` (axe `aria-allowed-role`), so
  each category is now a presentational row hosting a named group div — selectors keyed on
  `li.tm-command-palette-group` must move to `div.tm-command-palette-group` (the divider between
  categories stays a CSS border, now on `.tm-command-palette-group-item +
  .tm-command-palette-group-item`):

  ```html
  <!-- before -->
  <ul role="listbox" class="tm-command-palette-list">
    <li class="tm-command-palette-group" role="group" aria-labelledby="…">…</li>
  </ul>

  <!-- after -->
  <ul role="listbox" class="tm-command-palette-list" id="…">
    <li role="none" class="tm-command-palette-group-item">
      <div class="tm-command-palette-group" role="group" aria-labelledby="…">…</div>
    </li>
  </ul>
  ```

  The backdrop also became a click-to-dismiss surface — a click outside the panel closes the
  palette (no opt-out parameter, matching `TmModal` and `TmKeyboardShortcutsHelp`); the dismissal
  requires the press to START on the backdrop, so a drag-select that begins on the input and ends
  over the backdrop (only the click lands there) keeps the palette open; clicks inside the panel
  are fenced off and never reach the backdrop's handler. Grouped lists no longer render a group
  for a null/empty category — a `role=group` must be named, so those options render without the
  wrapper — and the combobox drops `aria-controls` when the listbox is gone (no results).
  Consumers that asserted "the palette stays open after any click" must adjust.

- **The lightbox stylesheet lost its never-rendered legacy section.** `_lightbox.css` carried a
  first block for `.tm-lightbox-overlay`, `-container`, `-nav`, `-image-wrapper`, `-image` and
  `-error` — classes no component has ever rendered — plus a 768px media block and legacy rules
  that leaked onto the live `-close`/`-prev`/`-next`/`-counter` classes through the shared names
  (the counter pill's ≤768px `bottom: 0.5rem` offset included). The pill was re-homed into the
  live section with its styling tokenized. Only a host that rendered its OWN markup with those
  legacy classes loses the styles.

### Fixed

- **TmPdfViewer: starting directly in `ViewMode=Continuous` no longer renders an empty area.** The
  PDF.js document state in `pdf-viewer.js` was keyed by the single-page canvas `WeakMap` entry —
  an element that is never rendered in continuous mode — so `init` bailed out before the document
  loaded and `OnPdfLoaded` never fired. All interop functions are now keyed by the viewer's root
  element, and the loading (`role="status"`, `TmPdfViewer_Loading`) and load-error (`TmAlert`
  above the `<embed>` fallback) states that were computed but never rendered are visible. Page
  canvases (single page, continuous pages and thumbnails) render at the device pixel ratio
  (backing store × DPR, capped at 2; CSS size stays the viewport size) — overlays, search
  highlights and annotations work in normalized 0..1 coordinates and stay aligned. Thumbnails
  reuse the already-open PDF.js document instead of a second `getDocument(url)`, the text
  selection `mouseup` listener is per instance and unregistered in `destroy` (no more leaks with
  multiple viewers on one page), icon buttons carry `title` tooltips, view-mode buttons expose
  `aria-pressed`, the thumbnails sidebar is a labelled region, thread headers in both comment
  panels expose `aria-expanded` + `aria-controls` with a stable detail id, the annotator color
  indicator is announced instead of `aria-hidden`, and selecting a thread anchored on another
  page navigates the viewer (and the annotator) to that page.

- **`TmOverlayPanel` dismissal focus restore no longer runs through `eval`.** The restore rides the
  overlay.js module export `focusById(id, { preventScroll: true })` — the id travels as a typed JS
  argument instead of an executed script string — with `preventScroll` still guaranteeing that
  Escape/outside-pointer dismissal leaves the page scrolled where the user left it.

- **DiagramEditor: edge labels and cardinalities use the theme-invariant diagram ink on a paper
  halo** (`.tm-diagram-edge-label__text`, painted via the new `--tm-paper*` tokens), so they no
  longer collide with the edge stroke or disappear on the white page in dark themes. Host CSS on
  `.tm-diagram-edge-label { color }` keeps working (inherited by the new span).

- **DiagramEditor honours `prefers-reduced-motion`.** The search-match pulse, hover lifts/scales,
  layers-panel width and chevron transitions are suppressed by an explicit enumeration block in
  `_diagram-editor.css` (the layers panel owns its rule in its scoped stylesheet), guarded for
  split packages by `ReducedMotionSelectorTests` against a frozen uncovered-subject baseline.

- **TmDiagramEditor context menu / mobile actions sheet: *Paste style* no longer shows the desktop
  shortcut in its label; *Delete* stays visible at the bottom of the mobile actions sheet** (sticky
  last row, U5/X7).

- **TmDiagramSearchPanel wraps on narrow editors (320 px) and its icon buttons meet the 44 px touch
  target on coarse pointers (U11).** The non-wrapping 200 px input row (~800 px of chrome) clipped
  "All pages", the regex toggle and the nav buttons at 320 px.

- **Core `pointer-gesture.js`: a failed capture re-assert cancels the pinch instead of committing a
  half-captured gesture.** `capture()` now reports success; when the re-assert after `onStart`
  throws (an engine that fires `lostpointercapture` synchronously on release leaves the pointer
  uncapturable) the pinch cancels itself with `lostpointercapture`. After `onStart` the pinch owns
  both fingers' capture — a host that must keep a finger elsewhere cancels the pinch from `onStart`.
  Documented in `docs/touch-interaction.md` ("Coexistence with `attach()`").

- **`TmDiagramEditor` touch review batch (phase 3 review fixes).** The context menu dismisses on a
  `pointerdown` capture anywhere outside (a touch tap on the canvas closes it, as does starting a
  pan, a pinch or an edge draw). The release-click guard is armed per hold and scoped to this
  editor's menu (`closest('.tm-diagram-editor')`), so a long hold no longer swallows unrelated
  clicks and the lift click of an unrelated tap passes. Touch contacts are tracked as a
  `pointerId` set (lifts on the document, `lostpointercapture`, mouse/pen clears) and the native
  touch `contextmenu` is only swallowed while the touch stack is attached. The touch-press ring
  grows under the finger after ~150 ms (static under `prefers-reduced-motion`) with a
  `navigator.vibrate(10)` tick on open. Zoom limits are a single source on every path: `zoomToRect`
  clamps through `_clampScale` (the ad-hoc 0.25 floor is gone), the empty-document `fitToView`
  branch clamps, and a wheel tick already outside the range only moves toward the range — never
  jumps; the zoom-in/zoom-out buttons (mobile overlay pill and status bar) disable at
  `DiagramZoomLimits.MaxScale`/`MinScale`. `init()` destroys a stale instance registered for the
  same container id first, and the context menu/table-inserter containing block is probed from the
  popover's own parent.

- **`TmDiagramEditor` context menu is finger-friendly and keyboard-operable on a tablet.** Opened
  by a touch long-press it carries `data-anchor="touch"` (the pointer type rides
  `touchOpenContextMenu` → the C# handler) and parks above the finger, flipping below at the top
  edge, offset `x + 16`, then clamped; on a coarse pointer the items are at least
  `--tm-touch-target` tall. After placement the first enabled item is focused (no page scroll) and
  ArrowUp/ArrowDown/Home/End rove between the native `role="menuitem"` buttons (no Enter/Space
  emulation); closing returns the focus to the canvas.

- **Core `pointer-gesture.js`: a pinch whose host cancels a pointer-drag attached to the SAME
  element in `onStart` no longer rolls itself back (`lostpointercapture`) when the first finger
  moved before the second landed.** Fixes the two-finger zoom from an empty canvas in
  `TmDiagramEditor` and `TmWorkflowDesignerCanvas`. No API change.

- **`TmDiagramEditor` context menu: coordinates render culture-invariant (`cs`/`sk`/`fr` produced
  `left:123,5px`), and re-opening at the same point no longer shifts the menu by the editor
  offset.** The anchored menu now carries its viewport point in `data-screen-x`/`data-screen-y`
  (invariant format) and the JS placement re-reads those attributes on every open instead of its
  own `style.left`/`style.top`, resets a stale `max-height` cap and clamps through the same
  measured containing block as the table-inserter popover.

- **`TmDiagramEditor` search: Replace / Replace all respect read-only and notify the host.** In the effective read-only state the search panel is search-only (new `TmDiagramSearchPanel.ReadOnly`; Ctrl+H opens plain search). A plain (non-regex) query is replaced literally (`a.b` no longer matches `axb`), `$` in a plain replacement is literal text (`$0x` replaces to `$0x`, not group 0 + `x`), and every replacement raises `DocumentChanged`. The find/replace inputs no longer show the literal texts "Query"/"ReplaceQuery"/"_regexError", an invalid regex is announced through a localized `role="alert"` message tied to the query input (`aria-invalid` / `aria-describedby`), the result counter is a polite live region, and the layers panel marks the active layer again.

- **`TmDropdown` disabled trigger renders as a real disabled control.** `Disabled` now puts the native `disabled` attribute on the trigger button (and omits `aria-expanded`, which is meaningless while disabled), so the browser blocks the click in every context; the wrapper already carried the `tm-dropdown-disabled` class and now also greys the trigger out via `--tm-opacity-disabled`. Becoming disabled while the menu is open now closes the menu (`OnParametersSet`), so a disabled dropdown can no longer leave an open menu behind that its dead trigger could never close.

- **`TmDiagramEditor` context menu stays inside the editor box (DiagramEditor redesign phase 8).**
  The editor is an inline-size container, so the fixed-position context menu resolves against the
  EDITOR box while `openContextMenu` clamped it to the VIEWPORT: a menu clamped to the viewport
  top translated to a negative editor-box top and painted above the editor's clip — on a hosted
  editor shorter than the window (the Modeling preview, any split layout) the edge context menu's
  upper half, including the routing previews, was unreachable. `diagram-editor.js` now detects the
  effective containing block at runtime (a zero-size fixed probe), caps the menu's height to the
  editor box and clamps the positioned box into it; under plain viewport semantics it clamps to
  the viewport. Not breaking; no markup or class changes.

- **`TmDiagramEditor`: pressing a port that carries an attached edge starts edge drawing again
  (DiagramEditor redesign phase 8).** Edges paint over nodes, so the fat invisible
  `.tm-diagram-edge-hit-path` is the TOP element at a port's position; the whole-edge drag branch
  (added after the port branch in the phase-5/6 edge work) consumed the press whenever the
  pointer rested on an edge corridor — drawing a new edge FROM a port of an already-connected
  node was impossible (the Workflow-phase "drop a dependency stencil, drag port to port" flow
  hung). `_onMouseDown` now yields the whole-edge drag to a port or connection point found under
  the pointer (x-ray via `document.elementsFromPoint`), and the port branch resolves the port
  through the same stack when the hit path owns the event. Whole-edge dragging still works from
  anywhere on the corridor away from the ports.

- **DiagramEditor wave 0 fixes: SQL/CSV import dialogs, layers panel, template gallery,
  minimap tokens, editor icons (DiagramEditor redesign phase 2).**
  The SQL import dialog now shows a live preview ("Tables: N / Relations: N") while typing
  (`@bind-Value:after` on an `Immediate` textarea) and disables **Importovat** while the parsed
  DDL has zero tables. Layout direction choices are localized (reusing
  `TmDiagram_LayoutHierarchicalTopDown` / `…LeftRight`). The CSV import dialog localizes the
  diagram-type options (`TmDiagramCsvImportDialog_Type_*`) and the semantic mapping fields
  (`TmDiagramCsvImportDialog_Field_*`), and parses the preview on every keystroke. Both dialogs
  give their fields per-instance ids (`sql-ddl-{id}`, `csv-data-{id}`, `diagram-type-{id}`,
  `csv-map-{field}-{id}`) with matching `label for` — two dialogs on one page no longer share
  ids. A new layer is named through `TmDiagramLayers_NewLayerName` ("New layer {0}") instead of a
  hardcoded "Layer {n}".

  `TmDiagramLayersPanel` derives from `TmComponentBase`, so it now accepts `DataTestId` /
  `TestIdPrefix` and renders `data-testid="diagram-layers"` by default; every parameter has an
  XML doc. The active row carries `tm-diagram-layers__item--active`, the drop target
  `--drag-over`; the layer selector is a `role="radiogroup"` with `role="radio"` +
  `aria-checked` buttons; the layer name is focusable (`tabindex="0"`, `role="button"`) and
  rename starts from the keyboard (Enter / F2 / Space, one activation per press per
  docs/keyboard-activation-convention.md). Template gallery filter chips are `type="button"`
  with `aria-pressed`, and `TmDiagramTemplateCard` renders lowercase `aria-pressed` values
  (`"true"`/`"false"`, previously invalid `"True"`/`"False"`).

  The page-tab buttons replaced the raw `⧉` / `✕` / `+` glyphs with `TmIcon`, and the duplicate
  action uses its own `tm-diagram-editor__page-tab-duplicate` class (the shared
  `__page-tab-close` class no longer matches the duplicate button — see migration below).
  Duplicate toolbar icons were disambiguated: Duplicate uses the two-pages glyph, Paste style
  uses `IconNames.Paintbrush`, Rulers use `IconNames.Ruler`, and vertical align items no longer
  reuse the horizontal align icons. Import/export error banners render as a real `TmAlert`
  (`tm-diagram-editor__banner`) instead of a hand-rolled `tm-pivot-btn` + inline-style banner.
  The minimap viewport rectangle uses `--tm-color-primary` / `--tm-color-primary-subtle`
  instead of hardcoded `#3b82f6` / `rgba(59,130,246,0.08)`. Missing keys
  `TmDiagramEditor_PasteHere` and `TmDiagramCanvas_AriaLabel` were added to all four resource
  tables (they rendered as raw keys in the UI), and the Czech toolbox placeholder is now
  "Hledat tvary…".

  ```razor
  @* before: duplicate page button carried the close-button class *@
  <button class="tm-diagram-editor__page-tab-close" ...>⧉</button>
  @* CSS targeting .tm-diagram-editor__page-tab-close also styled duplicate *@

  @* after: the duplicate action has its own class (close keeps the danger-red hover) *@
  <button class="tm-diagram-editor__page-tab-duplicate" ...><TmIcon Name="files" /></button>
  <button class="tm-diagram-editor__page-tab-close" ...><TmIcon Name="x" /></button>
  ```

- **`TmDataTable` / `TmViewManager` / `TmMultiViewList` / `TmFilterBuilder` wave 0 accessibility.**
  Group toggles announce expand/collapse (`TmDataTable_ExpandGroup` / `TmDataTable_CollapseGroup`)
  and use a chevron icon instead of a glyph. Group chips remove with a labeled icon button.
  `+ Group` is a `TmDropdown` of groupable columns and calls `AddGroupColumn` without a drag.
  The view panel is a `TmOverlayPanel` (no private `.tm-overlay`); view rows are buttons; edit
  and delete stay inline with an accessible name. The multi-view switcher is a radiogroup with
  arrow keys. The group picker is an overlay. Filter add opens the field picker and Escape
  returns focus to Add filter. Select-all sets `indeterminate` from `OnAfterRenderAsync` via
  `tmDataTable.setIndeterminate`. Inline edit errors use `role="alert"`.

- **`TmViewManager` / `TmDataTable` / `TmMultiViewList` wave 0 (no visual redesign).**
  Saving the current view names the copy with `TmViewManager_CopySuffix` (`(copy)` / `(kopie)` /
  `(kópia)` / `(copie)`) instead of a hardcoded `(Copy)`. The visibility radios use a per-instance
  `tm-view-scope-{id}` name, so two managers on one page no longer share a group. Empty view
  sections use the `tm-view-empty` class instead of an inline style. Integrated tables and
  multi-view lists pass groupable columns into the view dialog, so the Grouping section shows
  when a column or `GroupableFields` entry exists. `TmColumnFilter` accepts an additive
  `ColumnTitle`, a controlled `Value`, and `FilterOperator` (null keeps `contains`); the clear
  button is also on select filters, and `TmDataTable` feeds the input from `_activeFilters`, so
  Clear filters empties the inputs. A multi-view card keeps its avatar when selection is on, and
  a view that turns the Avatar column on renders that column in the table (empty cell when the
  URL is missing). Orphan keys `TmDataTable_SaveView`, `TmDataTable_ViewName` and
  `TmDataTable_DefaultView` stay: they are public override keys, not unused leftovers.

- **`TmDashboard`: every remaining user-facing string is localized and cancelling an unsaved new
  dashboard restores the previous one.** Seven hardcoded strings moved onto resource keys
  (`TmDashboard_NamePlaceholder`, `TmDashboard_DefaultName`, `TmDashboard_NewDashboardName`,
  `TmDashboard_UnknownWidget`, `TmDashboard_WidgetPlaceholder`, `TmDashboard_Expand`,
  `TmDashboard_Collapse`), so the widget title/content placeholder, the collapse control, the
  name editor's placeholder and the empty-dashboard name all follow the UI culture (previously a
  Czech UI still showed "Dashboard", "New Dashboard", "Unknown Widget", "Widget: …",
  "Expand"/"Collapse"). The Czech terminology is unified on „nástěnka" across every `TmDashboard_*`
  and `TmWidgetSelector_*` key (previously mixed: „Moje dashboady", „Vytvořit nový dashboard",
  „Přejmenovat nástěnku"). Additionally, creating a dashboard through the dialog now remembers the
  dashboard on screen: **Cancel in edit mode brings the previous dashboard (with its widgets)
  back without touching the provider** — previously the cancel left the empty, unsaved new
  dashboard displayed with no way back. Saving ("Uložit změny") commits the new dashboard and
  clears the restore. The "Create new dashboard" dialog also submits on Enter (the field sits in
  a `<form>`, so implicit submission runs the same handler as the footer button) and auto-focuses
  the name field on open.

- **`TmDashboard` delete-confirmation buttons stay in the UI language.** The dialog now passes the
  localized `CancelButtonText` explicitly alongside `OkButtonText`, so a host with a custom
  `ITmLocalizer` sees „Zrušit"/"Cancel" on both footer buttons, not a default English label.

- **Signing `TmDocumentCommentThreadPanel`: the reply composer is now bound internally and the
  reply text is cleared when the thread selection changes** (no visible behaviour change
  otherwise). The composer previously seeded from a parameter without a binding, so a reply draft
  typed for one thread could resurface when another thread was selected; the panel now owns the
  text and resets it whenever the reply composer unmounts — selection change, clear selection or
  starting an edit — which also keeps the draft-guard surface honest (the reply slot counts only
  while the reply composer is mounted).

- **`TmActivityComments`: starting an edit now closes the new-comment editor (before, it reopened
  empty after the edit ended).** Both editors share the same draft content, so `StartEdit` now
  clears `_showEditor` — only one draft can be open at a time — and the cancel paths
  (`CancelEdit`/`CancelEditor`) reset `_editorOriginalContent` together with the rest of the
  editor state, so no stale edit baseline survives between drafts.

- **`TmActivityComments`: the Add comment button is hidden while an existing comment is being
  edited; previously clicking it silently discarded the edit draft.** The button rendered
  whenever no new-comment editor was open — including during an inline edit — so clicking it
  cleared the shared `_editorContent`/`_editorOriginalContent` without opening any editor and the
  edit draft was gone. The button is now hidden while `_editingCommentId` is set, and
  `ShowEditor` ends any active edit first (the mirror of `StartEdit` closing the new-comment
  editor) before opening the new-comment editor, bridging the two draft states with exactly one
  deduped `OnDraftChanged` notification.

- **`TmActivityComments`: a new comment consisting only of empty editor markup (`<br>`,
  `<div><br></div>`, `<p><br></p>`) is no longer submitted — the submit guard now uses the same
  effectively-empty check as the draft flag (the edit-submit guard is unchanged).** The
  new-comment submit invoked `OnCommentAdded` whenever the editor content was not
  `IsNullOrWhiteSpace`, so clicking Submit with the placeholder-visible markup a browser leaves
  behind after emptying the editor submitted that markup as a "comment" — and the dirty-guard
  panel reported nothing unsaved, because the draft flag already uses the effectively-empty
  check. Submitting such markup is now a no-op: nothing fires and the editor stays open. The
  edit-submit guard keeps its plain `IsNullOrWhiteSpace` check, so editing behaviour is
  unchanged.

- **Six `TmDiagramEditor_*` toolbar keys ship in English instead of raw key names, and the
  `Tm_Time_TimeFormat` clock-format resource exists in all four languages.** `TmDiagramEditor`
  used `TmDiagramEditor_ShowGrid` / `HideGrid` / `ShowPageView` / `HidePageView` / `EnableSnap` /
  `DisableSnap` (toolbar tooltips and the context menu), but only the Czech table contained them —
  English, Slovak and French users saw the raw resource keys. All four tables now carry the six
  keys. `Tm_Time_TimeFormat` (the clock pattern inside "yesterday at …") existed only in the Czech
  and French tables; the English table now ships `"t"` — the .NET short-time pattern, byte-identical
  to the previous fallback, so no English output changes (`en-US` stays 12-hour, `en-GB` stays
  24-hour) — and Slovak ships `"H:mm"` (see docs/localization-plurals.md).

- **A modal/dialog close returns the focus to its opener — pointer or keyboard, first open or
  later — and can no longer land on an element inside the closing overlay.** The focus trap
  (`TmModal`, `TmDialog`, `TmDrawer`, `TmFocusScope` — the shared `tm-focus-trap.js` module)
  captured its return target only when the lazily imported module finished loading. A form modal
  whose content autofocuses its first field during the open therefore captured that field as
  "the opener": closing the dialog focused the dying field, and the focus fell to `<body>` the
  moment the overlay left the DOM. When the OK handler settled the focus on its post-close action
  button before the close render ran, the unconditional restore also yanked it back to the dying
  overlay. The contract is now:

  - A gated owner's scope notes the opener in its FIRST parameter-set phase — the mount render,
    before the overlay's content can autofocus (`NoteOpenerOnMount`); TmModal, TmDialog, TmDrawer,
    TmLightbox, TmCommandPalette, TmKeyboardShortcutsHelp and TmGanttImportDialog do, so closing
    a form modal that autofocuses its field returns the focus to the trigger on every open, on
    both render modes.
  - Every gated owner pre-imports the module on its first render while still closed
    (`ensureLoaded`), so even the first open of a browser session opens with the opener records
    (the pointerdown record, the focus history) already in place — previously that open loaded
    the module with it and its close landed on `<body>`.
  - A capture that lies INSIDE the surface is never used as the return target — restoring to it
    would focus an element the close is about to remove. The module keeps a short focus history
    (and the last document-level pointerdown, now expiring after ~2 s and cleared by a keydown,
    recording nothing when the press had no focusable ancestor), so a surface whose capture was
    poisoned still restores to the element the user actually engaged — clicked OR focused (the
    history covers keyboard-opened surfaces, which no pointerdown can name).
  - A deactivation restores focus only while its own surface still holds it (or the focus fell to
    `<body>`): a focus the app placed on a live element outside the overlay is never stolen. This
    guard now covers modal surfaces as well (it previously existed only for non-modal inline
    sheets), and a restore target that is disconnected or inside the closing surface is skipped
    (an explicit `RestoreFocusTarget`/`RestoreFocusTargetId` inside the overlay degrades to the
    opener instead of stranding the focus). The only `<body>` fallback left is the honest one: no
    live candidate outside the surface existed (for example the opener was removed from the DOM
    while the surface was open).

  An app focus issued while the modal is still open is dropped by the browser anyway (the open
  modal holds the page `inert`): settle the post-close focus AFTER the overlay is gone, which is
  what the old `requestAnimationFrame` workarounds effectively did and can now be removed.

- **`TmSelect` with a nullable `TValue` now really displays and selects the empty (null) option.**
  A null-valued `SelectOption<TValue>` (e.g. a "Root (no parent)" entry in `Options`) rendered as a
  value-less `<option>` — whose value silently became its text content — and a bound `Value` of
  `null` never pinned the preselection, so the dropdown showed the first option by accident and
  picking the null entry round-tripped through the label text. Null-valued options now render as
  real `<option value="">` elements (marked `selected` when the bound value is null), and the
  `<select>` pins the preselection with `value=""` — for nullable value types (`decimal?`, `int?`,
  `Guid?`, `enum?`, …) whenever an empty-valued option exists, and for `TmSelect<string>` when
  `Options` contains a null-valued entry — so the displayed selection finally matches the model.
  Nullable selects with a `Placeholder` now also pin `value=""` (visually identical — the
  placeholder was already the selected option); nullable selects with neither a placeholder nor an
  empty-valued option keep the old no-attribute behaviour, since a literal `value=""` would leave
  nothing selected. A null-valued and an `""`-valued `string` option share the single empty wire
  value and must not be mixed. Selecting the empty option fires `ValueChanged(null)` and the
  value → null → value switch repeats indefinitely. **Required caveat:** with `Required="true"`,
  picking the empty/null option (or a selectable placeholder) submits the empty value, so native
  validation counts the field as missing — that is the intended "please choose" behaviour, not a
  regression.

- **The `TmWorkflowDesignerCanvas` context menu is localized.** Its items ("Edit properties",
  "Delete state", "Delete transition", "Add initial state", "Add state", "Add final state") were
  hardcoded English inside the component; they now use the `TmWorkflowDesignerCanvas_*` resource
  keys in all three languages (en/cs/fr) and answer to `ITmLocalizer`, like every other
  user-visible string in the library.

- **Cancelling a `TmDataTable` column resize no longer saves the width, and the drag runs on the
  shared pointer-drag helper.** The resize gesture moved out of `data-table.js`'s own document
  listeners into `attachColumnResize(root, dotNetRef)` over `pointer-drag.js`: the start width is
  MEASURED from the `<th>` (a column without a pixel width used to jump to 160 on the first move),
  the listeners are attached once per table on the first render (the old press-then-`startColumnResize`
  round trip lost the first moves on Server), `pointercancel` / Escape / a lost capture / a
  mid-drag unmount restore the start width and commit NOTHING — they used to commit like a real
  release — and the pointer is captured by id, so a second finger can neither move nor end the drag
  and the drag follows the pointer outside the table. `window.tmDataTable.startColumnResize` stays
  exported (deprecated) for hosts that still call it. The gesture is delegated
  (`.tm-col-resize-handle`), so `touch-action: none` sits on the handle only in CSS and the table
  around it keeps scrolling; `document.body` is no longer touched (`user-select` is the helper's
  `.tm-pointer-dragging` class). The horizontal delta inverts in RTL, read live from the computed
  direction. The old pointer-press handler is gone from the markup.

- **`TmColorGradient` drags on the shared pointer-drag helper, with pointer capture and real
  element maths.** The six Blazor pointer handlers are replaced by the new
  `_content/Tempo.Blazor/js/tm-color-gradient.js` ES module: the position is a fraction of the
  element's real bounding box (the old code divided the event offset by hardcoded 200×150, so the
  value jumped when the pointer crossed the thumb child and broke at any other size), the pointer
  is captured (dragging off the element keeps tracking instead of stopping silently), the preview
  streams through rAF-throttled `OnGradientDrag` calls, and the release commits exactly once
  through `OnGradientCommit`. Escape, `pointercancel` and a mid-drag unmount report
  `OnGradientCancel` and restore the state the press started from — nothing is committed on a
  cancelled gesture. A tap (press + release without moving) sets the colour through the same
  commit. The RGBA number inputs remain the non-drag alternative.

  **CSP:** `data-table.js` now dynamically imports `pointer-drag.js`, and the new
  `tm-color-gradient.js` imports it statically — a `script-src` policy that allows those scripts
  must also allow the helper (same `_content/Tempo.Blazor/js/` origin, so the host-wide nonce or
  hash that covers `scheduler.js` covers all of them; a per-file hash must list `pointer-drag.js`
  too). In exchange neither component carries its own pointer gesture any more.

- **A cancelled `TmColorGradient` gesture restores the exact colour it started from, and ten
  1° hue steps no longer drift.** Two lossy conversions compounded: the cancel path re-derived
  R/G/B from the restored HSV — `#3B82F6` came back as `#3A82F6` — so the snapshot now captures
  R/G/B and restores them verbatim (only the gradient's hue colour is re-derived); and
  `OnParametersSet` re-parsed the value the component itself had just committed on every bind
  round trip, while `ColorHelper.HsvToRgb` truncated each channel a unit low — together a
  keyboard-stepped hue crept up ~0.2° per step. The component now remembers its last emitted
  value and skips the echo re-parse (case-insensitive), and `HsvToRgb` rounds instead of
  truncates, so RGB → HSV → RGB returns to its input.

- **A cancelled `TmDataTable` resize on a column without a width no longer leaves one behind.**
  The cancel used to replay the press-time start measurement through the live `OnColumnResized`
  path, which converted an auto-layout column's measured width into a user-set one — the
  cancelled drag persisted something after all. The component now snapshots the column's state
  before the FIRST live update of a gesture and `OnColumnResizeCancelled` (new `[JSInvokable]`)
  restores the snapshot width or removes the width entry entirely; the commit clears the
  snapshot, so a stray late cancel cannot roll a committed width back.

- **A user-set `TmDataTable` column width now survives the auto table layout.** The table renders
  `table-layout: auto`, where `width` is a suggestion the algorithm may shrink when columns
  compete for space — a +48px touch drag at 768px rendered ~1px wider. A user-set pixel width is
  now repeated as `min-width` (the column's own `MinWidth`, if any, keeps priority), and the
  keyboard resize measures the real rendered `<th>` through the new
  `tmDataTable.measureColumn(table, key)` instead of starting from the 160 default. Under
  `(pointer: coarse)` the resize handle widens to a 24px target, the last column's handle stays
  inside the table edge, and the pin button slides inward so the two targets do not overlap.

- **`TmSidePanel` without a viewport scope now opens the sheet the viewport measured (C7).** With
  no `TmLayoutScopes.Viewport` cascade the container used to decide the sheet geometry
  (side vs bottom) entirely: a narrow container on a desktop viewport opened a bottom sheet, a
  desktop container on a phone opened a side sheet. The panel's own sheet now reads the position
  from its drawer's internal viewport probe (through `ResolvedLayoutChanged` + `InitialMode`; the
  container is only the first-frame guess), so a 500px column at 1440 opens a right side sheet and
  a phone viewport opens a bottom sheet. A host with a viewport scope, or a forced `LayoutMode`,
  is unchanged.

- **The toolbar's fit measurement no longer ignores a group wrapper's margins and its own gap
  (F14/C5).** `tm-toolbar.js` counted only a flattened wrapper's padding and border, so a wrapper
  with a horizontal margin — or a column gap wider than the bar's — over-committed the row and the
  next control could overlap the pinned one (the I6 bug class). Now the margin counts as fixed width
  and a wider gap is reserved per adjacency between the wrapper's direct items, conservatively (as
  if all of them stayed); a narrower gap was already over-reserved by the bar gap and changes
  nothing, and the start/actions containers share the bar gap (their margins are not double-counted).

- **A `TmDialog` prompt no longer submits on an IME's commit-Enter.** Typing with an input-method
  editor (Chinese, Japanese, Korean…) commits the composed text with Enter; that press neither
  arms the prompt submit nor fires it, so committing composed text no longer submits the dialog
  mid-composition. The guard reads `KeyboardEventArgs.IsComposing`, which does not exist in the
  .NET 8 framework assemblies — there the guard is a no-op and the commit-Enter submits as before
  (accepted: the flag is not available). Safari's keyCode-229 commit-Enter (`isComposing=false`)
  is not detectable through Blazor on any target and is likewise accepted.

- **The dialog/modal/drawer scroll region stays correct for its whole open life.** Three defects
  fixed together in `ScrollRegionInterop` and `tm-focus-trap.js syncScrollRegion`: (1) a close
  that landed while the lazy module import was still in flight was dropped by an `!_attached`
  early return, leaving the `ResizeObserver` attached to the removed node — attaches are now
  generation-checked, so a detach wins over an in-flight attach and the stale attach stops itself
  unless a newer attach owns the region (both share one region id, so a blind stop would tear the
  newer region down; the module import is cached, so a re-attach never imports twice); (2) content
  growing INSIDE the scroller (rows appended under a `max-height` cap) never resized the scroller
  and was never re-evaluated — the children are now observed too and a childList MutationObserver
  re-observes added children and re-runs the overflow decision, coalesced into one frame; (3) a
  title that rendered or changed after the attach never named the region — `TmModal`, `TmDialog`
  and `TmDrawer` re-point it through the new `updateScrollRegionLabel` whenever the computed
  `aria-labelledby`/`aria-label` changes while attached — including an update that lands while the
  attach is still in flight, reconciled once the attach completes — so the region's name always
  tracks the title. The wrapper no longer swallows `NullReferenceException`.

- **Overlay focus traps activate only after their root is promoted to the top layer.**
  `TmModal`, `TmDialog`, every modal `TmDrawer`, `TmLightbox`, `TmCommandPalette`,
  `TmKeyboardShortcutsHelp` and `TmGanttImportDialog` activated their `TmFocusScope` on the same
  render that inserted the `popover="manual"` root — before `showPopover` ran. An unpromoted
  popover root is `display:none` by the UA rule `[popover]:not(:popover-open)`, so whenever a root
  lost its own author `display` (a consumer class or stylesheet), the trap's initial-focus move
  landed on `<body>` and the page behind the overlay was inert. All seven now go through a shared
  promote gate that promotes the root first and only then activates the trap; a promote that
  completes after its owner closed is dropped, so closing during the in-flight promotion can no
  longer make the *next* open activate before its own promotion. `role`/`aria-modal` now appear one
  render after the overlay's first paint (selectors on the rendered markup are unchanged once the
  overlay is open). In the same pass the overflow scroll region of a dialog/modal/sheet body can
  never take the initial focus: `tm-focus-trap.js` marks an overflowing scroller with
  `data-tm-scroll-region` and both the initial-focus fallback and the toast focus recovery skip it
  (a Custom `TmDialog`'s first control used to lose the focus to its own scroller whenever the
  content overflowed).

- **F6 focus-trap follow-up: phone sheet Tab cycle, modal Escape ownership, toast focus recovery.**
  Three focus-trap defects fixed in `tm-focus-trap.js` and its consumers. (1) The bottom-sheet More
  menu is a roving group (item `tabindex="-1"`), and the trap's Tab cycle counted those items — Tab
  on the real last tab stop was "not last", the browser's native Tab skipped the `-1` items, found
  nothing tabbable left (the page is inert) and focus dropped to `<body>`, out of the sheet. The
  cycle now runs over **tab stops only** (a negative tabindex is not one), and Tab from a focused
  `-1` item walks the next/previous tab stop in DOM order; this covers every modal trap with a
  roving group (menus in sheets, a `TmToolbar` roving group inside `TmModal`/`TmDrawer`). (2) A
  modal dialog's Escape was gated on registration order alone (`isTopmost`): a non-modal inline
  sheet activated after the dialog blocked the dialog's Escape entirely — nothing closed. The
  modal gate is now `modalOwnsEscape`, which ignores later **non-modal** traps that do not hold
  focus or cannot close at all (no Escape handler); an inline sheet nested in the dialog that holds
  focus still closes first, and a dialog over a dialog still closes only the top one. (3) A toast
  dismissed under an open sheet or dialog — by click, by keyboard or by expiry while focused —
  removed the focused dismiss button and stranded focus on `<body>` (the next Tab landed on the
  sheet's drag handle). The trap now remembers the element last focused inside it (`focusin`) and
  both `TmToastContainer` and `TmNotificationToastContainer` ask it to recover the focus whenever
  the set of visible toasts drops an id — not only when the count shrinks, so a replacement at the
  `MaxVisible` cap, a notification queue refill and an expire+push in one render are covered too.
  `restoreTrapFocusIfLost` moves focus only when a toast actually held it (a capture-phase
  `focusin` records the last focus inside an inert-exempt container and the recovery requires that
  element to be gone): **timer expiry moves focus only when the toast held it** — a toast expiring
  while focus sat in the sheet (screen-reader browse mode, a mouse click) changes nothing, per
  WCAG 3.2.1/APG. The recovery never steals from a live element, prefers tab stops over roving
  `-1` items, and the modal Tab cycle now skips unchecked radios of a same-name group that has a
  checked member (one tab stop per radio group, APG).

- **A toast stays above any popup opened after it.** `overlay.js` promoted every
  `TmOverlayPanel` popover (date pickers, dropdowns, menus, the notification bell) by
  calling `showPopover()` directly, so a toast already on screen slipped behind it. It
  now promotes through the shared top-layer helper, which re-raises pinned toasts; the
  set of promoted surfaces is unchanged.

- **F6 review round 2.** `TmEditorShell`: the tablet rail the shell imposes itself can be expanded
  even when the host binds no `CollapsedPanels`; with `Breakpoints` the internal `TmMobileActionBar`
  follows the shell's resolved mode (the actions stay reachable on a custom-threshold mobile shell),
  and `TmLayoutObserver` rejects `Breakpoints` on the viewport-scope observer; Escape closes the
  inline sheet that holds focus (non-modal focus traps close by focus ownership, not registration
  order — two inline sheets on one page); focus no longer drops to `<body>` when the shell reopens
  the sheet from its closed bar, when a host switches the Tabs panel, or when a desktop → tablet
  flip rails the focused panel; restored widths are reported through `LeftWidthChanged` /
  `RightWidthChanged`, a late or changed `PersistWidthsKey` loads, resizing back to the host width
  clears the stored override, `aria-valuemax` is the reachable width and the resizer hit area is
  wider under a coarse pointer; the resizer releases its pointer capture by pointer id and a
  mid-drag detach restores the live width; an overflowing inline sheet body is named by its
  `aria-label`; closing one of two sheet panels no longer remounts the other's content and a host
  close that moves the sheet tab raises `ActiveMobilePanelChanged`; the `TmMobileActionBar` "More"
  trigger omits `aria-controls` while its menu panel is not rendered.
- **F3 review round 1.** `TmOverlayPanel` sheets keep `Role`/`Id`/`AdditionalAttributes` on a
  content wrapper (menu/listbox ownership and `aria-controls` survive the popover→sheet switch);
  menus and listboxes opened as sheets take initial focus on the first item, not Done;
  `TmDropdown` returns focus to the trigger after a keyboard selection; trap-focus popups
  activate only after `showPopover()`, so a cold Enter-open no longer drops focus to `<body>`;
  a modal sheet's root is promoted to the browser top layer (sticky app bars and transformed
  ancestors can no longer confine or cover it); the date-range sheet stacks its months, shows a
  single Done and keeps it disabled until both dates are picked; overlay.js translates anchor
  rects into visible-viewport space (a panned-down visible anchor is no longer dismissed as
  hidden); `Title` also names the popover; Escape inside a sheet raises `OnDismissed("escape")`;
  the swipe-enabled sheet shows its grabber; `TmPopover`'s arrow no longer paints inside a sheet.

- **F3 review round 2.** Every modal viewport-anchored overlay root is promoted to the browser
  top layer — a modal `TmDrawer` at any position, the `TmModal`/`TmDialog` overlay root and the
  `TmToastContainer` (re-raised on each push) — so the top-layer order equals the open order: a
  dialog opened from inside a bottom sheet no longer paints invisible under it, and a toast
  pushed while a sheet is open lands above it. Each promoted root resets the UA `[popover]`
  paint (Canvas background, CanvasText color, `overflow:auto`), so the page behind a sheet stays
  dimmed instead of turning into a flat grey wall and dark body text keeps the themed colour.
  An invalid `TmFocusScope.InitialFocusSelector` no longer throws after the inert/scroll lock
  landed. The restored `@media (width < 768px)` `.tm-modal` margin is asserted on the real
  element again (the round-1 adaptation measured a margin that came from an unrelated rule).

- **F3 review round 3.** A toast that is already on screen is no longer covered by a drawer,
  sheet or dialog opened after it — the toast containers are pinned on top and re-raise after
  every later promotion, and a push at the `MaxVisible` cap (visible count unchanged) or an
  expire+push in one render re-raises too. `TmCommandPalette`, `TmKeyboardShortcutsHelp`,
  `TmLightbox`, `TmGanttImportDialog` and `TmNotificationToastContainer` join the promoted roots
  (each resets the UA popover paint), so no focused modal surface paints invisible under another
  promoted one anymore. For `TmGanttImportDialog` that reset — and the whole Gantt dialog style
  block — lives in `TmGantt.razor.css`, whose scope attribute the sub-component dialogs never
  carried; review round 4 re-wrote the block as `::deep` rules so the promoted import overlay
  paints the full-screen scrim (not the UA popover box) and the export/import dialogs are styled
  at every width. A click on a toast's dismiss button while a sheet or modal is open
  dismisses the toast instead of passing through to the backdrop and closing the surface (the
  toast containers are exempt from the focus trap's inert background), and `role=alert` toasts
  keep being announced. The demo no longer renders two `TmToastContainer`s sharing one
  `ToastService` on the feedback page (every toast painted twice).

- **Round-6 overlay fixes.** Opening a `TmDialog` Prompt with Enter no longer submits it at once.
  The opener activates on its Enter keydown and the per-type initial focus lands on the input while
  the key is still down, so the submit that rode on a bare Enter keyup received the opener's
  trailing keyup and fired the `DefaultValue` before the user ever saw the dialog. The submit is
  now armed by the input's own Enter keydown and fires on its keyup, so a full press submits
  exactly once, a keyup the input never saw go down does nothing, auto-repeat cannot double it,
  and the arm resets when the dialog reopens. (A bare keydown submit was probed and rejected:
  closing between keydown and keyup lets Chromium run the keydown's default action against the
  re-focused opener and re-open the dialog with the default value.) The prompt input's focus ring
  is no longer clipped by the scrolling content container — the input wrapper reserves one spacing
  step of padding for the 4px ring. Below 640px a dialog presented as a sheet now uses the modal
  sheet's 16px inline padding instead of 24px, so 36-character FR/CS footer labels fit at 320px
  exactly as they do in the modal.

- **Round-5 overlay fixes.** A `TmDrawer` with `SwipeToDismiss="false"` no longer dismisses on any
  fast downward flick, and a net-upward drag whose tail flicks downward no longer closes the sheet:
  every dismiss honours the flag, and the net drag direction — not the tail velocity — picks which
  way a flick steps. Every `TmDialog` type now names an explicit initial-focus target (prompt → the
  input, confirm → Cancel, alert → OK), and the scroller `div.tm-dialog-content` is a
  keyboard-reachable region only while it actually overflows, so a short dialog grows no extra tab
  stop and the title block no longer takes focus. A sheet footer no longer cuts ordinary labels in
  half with a forced 50/50 split: buttons that do not fit side by side wrap to their own full-width
  rows first, and only a label wider than the whole row truncates. The footer buttons carry their
  label as a `title` only in the sheet presentation, so desktop dialogs stopped growing an "OK"
  tooltip.

- **A sheet backdrop stays clickable, and a nested dialog no longer strands the page (F2 review).**
  `tm-focus-trap` keeps a per-trap set of the elements it marked `inert`, skips a backdrop marked
  `data-tm-backdrop`, and re-applies the inert set of the trap that remains when the topmost one
  closes. A class name is not a backdrop. The scroll lock is a ref count, so two open modals no
  longer leave `html.tm-scroll-lock` behind. A sheet gesture writes only an inline height and
  transition, and clears those two on release, cancel and a vetoed dismiss. It does not touch
  `--tm-sheet-height`: that variable is Blazor-owned, and deleting it collapsed a snap whose index
  had not changed. Flick velocity is the travel over the last 80ms, so a `pointerup` that lands on
  the last move still dismisses. The keyboard gap pads the sheet overlay, so a dialog sheet keeps
  its own safe-area padding. `TmDialog` awaits its result callbacks, and `TmCommandPalette` lets the
  focus scope own Escape instead of closing twice.

- **`ConstrainHeight` panels can now grow back after the room returns (N318).** `overlay.js`
  used to read `getComputedStyle(panel).maxHeight` on every placement pass — which resolves the
  panel's own *inline* `max-height` written by the previous pass. Once the available room
  shrank (scrolling, a mobile keyboard opening), the cap could only ever get smaller until the
  panel was closed. The stylesheet cap is now captured once per entry in `open()`
  (`entry.cssMaxHeight`), and `place()` clears the inline `max-height`/`overflow-y` before
  measuring, so `offsetHeight` — and therefore the flip decision — sees the panel's natural
  height again. Affects every `ConstrainHeight` consumer: TmFilterableDropdown, TmEntityPicker,
  TmQueryInput, TmMultiSelect, TmTagPicker and TmMultiColumnComboBox.

- **Overlay menus now close BEFORE their item handler runs (N319).** `TmDropdown.SelectItemAsync`,
  `TmContextMenuItem.HandleClick` and the `TmEntityPicker` create-new path used to invoke the
  item's `OnSelect`/`OnClick`/`DataProvider.CreateAsync` first and only close the menu afterwards.
  An async handler that opened a dialog (confirmation, create-form) therefore rendered it *under*
  the still-open top-layer popover — and the first Escape peeled the stale menu instead of the
  dialog. All three now close synchronously (and yield a render) before the handler runs; any
  click inside a `TmSplitButton` menu also closes it. No z-index or top-layer changes to
  TmModal/TmDialog.

- **`MatchAnchorWidth` now respects the panel's own `min-width` (N324).** `overlay.js` used to
  run the placement math against `anchorRect.width` and only then write `style.width` — a panel
  whose stylesheet `min-width` exceeds the anchor (TmMultiColumnComboBox's 320px dropdown under
  a 240px trigger) was positioned as if it were anchor-wide and its min-width-clamped box
  overhung the viewport's right edge. The width write now happens before `offsetWidth` is
  measured, so align, shift and the viewport clamp all use the *rendered* width.

- **TmNotionNotificationCenter's panel width rule now actually applies (N325).** It sat in the
  component's scoped `.razor.css`, but the panel element is rendered by `TmOverlayPanel` and
  carries that component's scope attribute — the scoped selector could never match, so the
  panel silently fell back to the global `.tm-notification-bell__dropdown` (360px instead of
  the declared `min(24rem, 100vw − 1.5rem)`). The rule moved to the NotionEditor global
  stylesheet (`_notion-notifications.css`, the N197 pattern) as `width: 24rem;
  max-width: calc(100vw − 1.5rem)`, and a new source sweep
  (`OverlayPanelScopedCssTests`) fails closed on any scoped rule targeting a class passed to
  `TmOverlayPanel` without `::deep`.

- **A panel whose anchor scrolled out of the viewport no longer traps focus (N329).** `overlay.js`
  used to park the panel `visibility:hidden` and leave it open — focus inside (e.g. the
  TmFilterableDropdown filter input) kept accepting keystrokes on an invisible surface. Now, when
  the hide condition coincides with focus inside, the panel is dismissed through the normal
  accepted-dismissal path (`OnDismissed` reports the new `"anchor-hidden"` reason) and focus
  returns to the anchor. A hidden panel that does NOT hold focus keeps the old
  park-and-reappear behaviour unchanged.

- **Every `Role="dialog"` overlay panel now exposes an accessible name (N326).** `TmOverlayPanel`
  gained `AriaLabel`/`AriaLabelledBy`; the seven dialog-role consumers ship names — both
  notification centers point `aria-labelledby` at their visible title, the date pickers point at
  the field label (falling back to a localized `aria-label` when `Label` is unset), and
  TmColorPicker/TmMultiColumnComboBox use localized `aria-label`s. A new source sweep
  (`TmOverlayPanelDialogNamingTests`) enumerates every `<TmOverlayPanel Role="dialog">` under
  `src/`, verifies the rendered panel carries `aria-label` or an `aria-labelledby` that resolves
  to a present element while open, and fails closed on unnamed sites — including sites it cannot
  render (`unmeasurable:`).

- **All NotionEditor floating surfaces now ride the `--tm-z-*` token bands (CF10n).** The
  z-band sweep in `TmNotionFloatingZBandTests` used to cover only `UI/**/*.razor.css`; widened
  to the whole `Components/NotionEditor` tree it exposed bare band-level levels elsewhere —
  Share/Restrictions dialogs (10030/10020), db record detail and template editor and
  import/export overlays (1200/1210), page-tree and database context menus (1000/299-300),
  page settings menu (9999) and the three TempoBlocks edit modals (1000). Dialogs and
  full-screen take-overs now sit on `var(--tm-z-modal)` (backdrops `calc(... - 1)`), anchored
  and context menus on `var(--tm-z-dropdown)`/`var(--tm-z-popover)`, and two in-flow overlays
  that only needed local stacking dropped under 100. Any future bare `z-index: NNN` or
  `+ 9000`-style arithmetic escape anywhere in the NotionEditor fails the sweep.

- **Notion AI menu, mention menu, and comment-mention dropdown now render in the browser top
  layer (CF18-7).** All three used to be `position:fixed` surfaces on `z-index:1030` — below
  the `--tm-z-modal` band — so any of them opened against a `TmModal` painted under the modal
  overlay and was unreachable. They are `TmOverlayPanel` popovers now: `TmNotionAiMenu` and
  `TmNotionMentionMenu` anchor to a zero-size virtual element pinned at the existing
  `Top`/`Left` coordinates, and `TmCommentMentionInput` anchors its dropdown to the textarea
  itself. `overlay.js` supplies flip/shift viewport clamping (replacing
  `adjustSlashMenuPosition`), outside-pointerdown dismissal (replacing the transparent
  backdrop) and Escape handling routed through `IsOpenChanged` into the existing `OnClosed`
  callbacks — arrow navigation, Enter selection, selection callbacks and the mention-trigger
  cancellation are unchanged. Panel-root styles moved to the new global
  `_notion-floating-menus.css` (the N325 scoped-CSS rule applies: the panel element carries
  `TmOverlayPanel`'s scope attribute, so scoped selectors could never match it). The z-band
  sweep gained a fail-closed theory asserting these surfaces declare no `z-index` of their
  own.

- **A degenerate viewport pass no longer dismisses an open overlay panel.** Chromium applies
  a 1×1 device-metrics emulation mid-way through a full-page screenshot capture (Playwright
  `fullPage: true`), and the resulting resize pass used to see every anchor as out-of-view —
  for a panel holding focus that meant an instant `anchor-hidden` dismissal and unmount.
  `place()` now early-returns when `innerWidth`/`innerHeight` is below 2: the layout math is
  meaningless at that size, and the next real resize pass re-runs it anyway. The migration
  E2E surfaced this inside `NotionMentionMenuE2ETests`' baseline captures.

- **TmValidatedField's green frame now tracks the field, not the context (N320).** The
  `_validationRan` flag used to be set by any `OnValidationStateChanged` — including a pass
  triggered by editing a *different* field — so a never-touched field painted `tm-input-valid`
  on the strength of its neighbour's validation. The state is now field-scoped: the field's own
  `OnFieldChanged` or a whole-form `OnValidationRequested` marks a pass as covering it, and only
  then does `OnValidationStateChanged` mark it validated (the notify event still just re-renders;
  it never marks on its own). Editing field A therefore leaves untouched field B neutral, while
  `EditContext.Validate()`/form submit still paints every field. An EditContext swap resets both
  flags and moves all three subscriptions to the new context; Dispose unsubscribes all three.

- **TmNavigationGuard's dialog can no longer be dismissed during an in-flight save (N321).**
  Escape used to reach `HandleDialogResultAsync(false)` through `TmDialog`'s default
  `CloseOnEscape="true"` — it dismissed the dialog the save still reports through and fired
  `OnCancel`, while the save's eventual success re-issued the navigation the user had just
  "stayed" from. Both guard variants now pass `CloseOnEscape="@(!_saving)"`
  (`CloseOnOverlayClick` stays false), and `HandleDialogResultAsync` early-returns while
  `_saving` as a second line of defense for any path that skips the disabled/Escape gates.
  A successful save still re-issues the blocked navigation unchanged.

- **A pending debounce can no longer reopen a dismissed dropdown (N323).** `TmQueryInput`'s
  close path bumped `_requestVersion` but left the `System.Timers.Timer` armed — type, Escape
  inside the debounce window, and the pending timer still fired `LoadSuggestionsAsync`, which
  stamps its own fresh version and re-set `_isOpen`, reopening the dropdown the user just
  closed. `CloseDropdown()` now stops and disposes the timer alongside the version bump.
  `TmEntityPicker` had the same hole on the other side of the mechanism: `SetShowDropdown(false)`
  (overlay.js Escape / outside pointerdown) flipped only the flag while the awaited
  `Task.Delay(Debounce, _debounceToken.Token)` kept running into a search that re-set
  `_showDropdown`. Closing now cancels `_debounceToken`, and the delayed blur-close cancels it
  too.

- **Track-changes toggle no longer double-pushes the canvas engine (CF09b2).**
  `ToggleTrackChanges` sent `setTrackChangesEnabled(CanvasEngineTracksChanges)` to the engine
  but never updated `_appliedCanvasEngineTrackChanges` — the next `OnParametersSetAsync` read
  the SAME value as a delta and pushed it again, so every user toggle cost two interop calls
  (and any param-pass observer saw a phantom "change"). The toggle now records the applied
  flag alongside `_trackChangesEnabled`, whether or not the host is mounted (a host mounting
  later boots with the same expression).

- **`TmNotionBlockContextMenu` submenus no longer strand keyboard focus (CF18).** Enter/Space
  on a submenu trigger is a native button click arriving with `MouseEventArgs.Detail == 0` —
  it opened the panel but never set `_pendingSubFocus`, so focus stayed on the trigger while
  ArrowRight did both (an APG asymmetry). The click path now detects `Detail == 0` and queues
  the same first-item focus; pointer clicks (`Detail >= 1`) are untouched. Separately, each
  `.tm-notion-ctx__sub` wrapper tracks `focusin`/`focusout`: `mouseleave` no longer unmounts a
  submenu that still holds keyboard focus (focus fell to `<body>`), and a sibling's
  `mouseenter` hands focus back to the focused submenu's trigger before the swap.

- **The Notion menus' focus re-assert loop no longer fights a deliberate focus move (CF18-3).**
  `tmNotionEditor.focusMenuInput` re-focuses the just-opened search input for ~10 animation
  frames because the same keystroke queues a Blazor render that can refocus the block
  editable afterwards. The loop never checked what the user did in between: a Tab (or any
  focus move to a real element) inside that window was yanked back to the menu input up to
  nine more times. `tryFocus` now stops on attempts after the first when
  `document.activeElement` is a real non-contenteditable element that is neither the target
  nor `body` — the initial claim is unchanged, and the block-editable refocus the loop
  exists to outlast still loses the race. Affects `TmNotionSlashMenu`,
  `TmNotionMentionMenu`, and `TmNotionTokenDropdown`.

- **The InteractiveAuto demo now proves the WebAssembly renderer actually claims the page
  (CF18-4).** The shared demo layout stamps `data-render-mode` from `RendererInfo.Name` —
  `Static` under prerender, `Server` while only the SignalR renderer is attached, and
  `WebAssembly` only when the WASM renderer owns the layout. The new E2E test models the
  real .NET auto-mode contract instead of asserting a live swap that `blazor.web.js`
  deliberately never performs: on the first visit the `onWebAssemblyFailedToLoadQuickly`
  latch assigns auto components to the Server renderer and there is no migration once a
  renderer claims a component; only after the WASM runtime finishes booting does the
  framework persist `blazor-resource-hash:<assembly>` to `localStorage`, and on subsequent
  navigations the matching hash skips the latch so auto descriptors stay unassigned until
  the WASM renderer claims them. The test waits for the persisted hash (deterministic
  proof a WASM boot fully completed — Debug payloads at the auto throttle of one download
  at a time take tens of seconds), reloads, and asserts the marker reaches `WebAssembly`.
  Earlier RED runs that observed `Server` for 90+ seconds were this designed latch, not a
  hosting defect: `dotnetReady` resolves, all ~300 runtime assets fetch 200, and the
  second navigation does hydrate on `WebAssembly`.

- **`TmFilterableDropdown` wires its combobox/listbox ARIA relationships (CF18-5a).**
  The closed trigger no longer emits `aria-controls` (it pointed at a popup that does not
  exist); once open it names the `TmOverlayPanel`'s generated id. The filter input —
  the element that actually holds focus — gets `aria-controls` for the popup and
  `aria-activedescendant` tracking the ArrowUp/ArrowDown highlight through stable
  per-option ids (`<popup-id>-opt-<index>`). Because ARIA `listbox` may own only
  `option` children, the popup keeps the stable id and `role="listbox"` moves to an
  inner wrapper around the options alone; the filter input and the loading/error/empty
  messages live outside it as siblings, and the messages now announce themselves via
  `role="status"`/`role="alert"`. The trigger and the listbox both carry accessible
  names — the trigger falls back to its visible placeholder/value text when
  `AriaLabel` is unset, and the listbox uses the new `TmFilterableDropdown_Options`
  resource. Verified by two bUnit tests plus an axe-core E2E scan of the open dropdown
  on the demo page (0 critical/serious violations).

- **`TmMultiViewList` list view exposes listbox semantics (CF18-5b).** The `<ul>` is
  now `role="listbox"` with `aria-multiselectable` bound to `AllowSelection` and an
  accessible name from the new `TmMvl_ListOptions` resource; each `<li>` is
  `role="option"` with `aria-selected`. Custom status-badge colors now pair the
  background with a luminance-picked ink (`--tm-text-primary` on light fills,
  `--tm-text-inverse` on dark) instead of the muted-secondary default that failed
  WCAG AA on saturated colors — mid-luminance fills remain the caller's choice.
  The demo page's palette moves to darker shades so the badges meet 4.5:1. Verified
  by a bUnit selection-state test plus an axe-core E2E scan of the list view
  (0 critical/serious violations).

- **`TmSankeyChart` sequence literals are monotonic again (CF18-6).** `BuildLinks`
  and `BuildNodes` emitted the `onkeyup` attribute with sequence number `100`
  between `14`/`15` and `13`/`14`, so every later literal sat out of order and the
  renderer's frame diff could pair unrelated attributes. The numbers now run
  strictly ascending inside each `Build*` method, and a source-level test scans
  every `Build*` member for the integer first arguments of `builder.*` calls and
  fails on any non-monotonic step — including a self-check that flags the
  `100`-between-`14`/`15` shape this fix removes.

- **Notion editor no longer loses keystrokes typed during the Enter-split
  round-trip (CF29).** Pressing Enter creates the next block asynchronously, and
  characters typed in the gap used to land in the old editable — where they were
  either overwritten by the `setHtml(before)` rewrite or captured by the blur
  save and committed as stale content. `notion-editor.js` now arms a pending-split
  buffer on Enter: a capture-phase `beforeinput` listener intercepts `insertText`
  aimed at the source block (matched by `data-block-id` so a recreated editable
  still resolves), `focusAtStart`/`focus`/`focusAtEnd`/`focusAtOffset` replay the
  buffered text into the block the caret actually lands in, queued Enters replay
  as further splits, and a bounded retry window plus a 1.5 s give-up flush cover
  slow landings. Two component-side guards close the revert paths: re-init after
  a `Content`-instance swap consults the JS `isEditableDirty` witness before
  overwriting `innerHTML`, and a stale same-instance render (queued before the
  model update) can no longer push a superseded `Html` back into the DOM —
  `_lastPropHtml` tracks the value the current content instance carried when it
  was consumed, so only a genuinely advanced prop may overwrite live edits.
  Covered by `FastTypingAfterEnter_LandsEveryCharacterInTheNewBlock` (Playwright,
  6× CPU throttling): the source block keeps `abc` and the new block receives
  `xyz`.

- **New ratchet: undocumented JSON-doc parameters can no longer grow (CF06e1).**
  `UndescribedParametersRatchetTests` counts `parameters[].description` entries
  that are missing or whitespace across `JsonDocumentation/**/*.json` and fails
  above a constant frozen at the measured value (882 of 4453 parameters on
  ratchet day); filling in descriptions must lower the constant, never raise it.
  A mutation fact proves the same counting and ceiling logic over synthetic JSON.

- **Release lane: an unreachable nuget.org feed now fails the provenance guard
  instead of skipping it (N276).** Both publish workflows set
  `TEMPO_REQUIRE_FEED=1` on every `dotnet test` step; with the variable set, the
  `FeedReachableFact` probe refuses to answer, the test runs, and its own
  `survey.Unreachable` assertion reports `unmeasured:feed-unreachable` as a
  failure. Without the variable the behaviour is unchanged — local and ordinary
  CI runs keep their skip-on-outage. Guarded by
  `ReleaseGateFilterTests.EveryPublishTestStep_SetsTempoRequireFeed` (step-, job-
  and workflow-level env reach) plus three `DecideSkip` unit cells.

- **Release evidence: `total` counts distinct test names, and duplicate keys
  refuse (CF15).** The counting convention is now written into
  `eng/verify-release-evidence.sh`'s header and `docs/e2e-test-lanes.md`:
  `total`/`passed`/`failed`/`skipped` count DISTINCT test names in the TRX —
  data-row instances sharing a name count once — not the number of `testId`
  elements the file happens to carry, so the consistency sum compares like
  with like (CF15a). The verifier rejects a second occurrence of any required
  flat-JSON key rather than trusting that two copies of the number agree
  (CF15b). And the lanes doc now records that `hostRestarts: 0` is vacuous
  under `TM_E2E_SELF_HOST=false` — the counter only measures hosts the suite
  itself started, so external-host evidence carries the watch-log requirement
  instead (CF15c).

- **Release evidence: eight fail-closed holes in the gate are closed (CF19).**
  `eng/verify-release-evidence.sh` now diffs with `--no-renames`, so a
  `git mv src/X docs/X` can no longer read as a prose-only change (CF19a);
  `artifactsPath` must live inside the committed `eng/release-evidence/` store —
  an outside path or a `..` traversal refuses (CF19b); a workflow guard pins the
  verifier invocation inside the publish job, not merely somewhere in the file
  (CF19c); a new `selfHost` key declares whether `hostRestarts` was measured —
  `selfHost: false` requires the external `host-watch.log` in the artifacts dir,
  and a missing or non-boolean value refuses (CF19d). Supporting fixes:
  `HostRestartLog` increments its counter before the JSONL write so a failed
  append can no longer hide a resurrection from the gate (CF19e);
  `SqlServerCacheFixture` detects CREATE DATABASE denied by the canonical
  262 number instead of a broad `permission`/`denied` substring (CF19f); the
  three byte-identical `TestAssemblyInit` copies are now ONE linked file under
  `tests/Shared/` (CF19g); and the 1200-line source sweep enumerates TRACKED
  files via `git ls-files`, so untracked `artifacts/`/`TestResults/` output can
  no longer move the count (CF19h). Covered by seven new verifier/fixture arms
  over real worktrees and evidence fixtures.

- **New guard: a component API change must carry `JsonDocumentation/**` in
  the same commit (N328).** `ComponentChangeCarriesJsonTests` evaluates every
  commit after the most recent tag (or the last 50 when the tag is fresh):
  a `[Parameter]` attribute line or changed public property declaration in
  `src/**/Components/**/Tm*.razor{,.cs}` without a `JsonDocumentation/**`
  path in the same commit is a finding — the freshness guard on HEAD cannot
  see WHEN either side moved, only that they currently agree. Calibrated on
  history: `ee1ec3df` is detected (SortLabel shipped with JSON regenerated
  only at the repo root), `56fa6f04` is clean (mass backfill carried its
  docs), and `3980af1d` is the single frozen grandfather — its JSON landed
  one commit later in `ca00f94d`. Shallow clones fail as
  `unmeasurable:shallow-clone` under `TEMPO_REQUIRE_FULL_CLONE`.

- **The Notion aggregate session can no longer deadlock on its own
  overlapping saves.** `NotionEditorAggregateSession.ApplyAsync` cloned the
  current snapshot per call, so two mutations that overlapped across a
  network await both carried the same base concurrency token — the second
  save returned 409, parked `_pendingMutation`, and every later mutation
  was refused with `editor_conflict_pending` until manual resolution. The
  deterministic symptom was `/wireframe` + Enter inserting nothing: the
  blur-save and the conversion save raced, one lost, and the menu's
  conversion was blocked by the self-inflicted conflict
  (`DocLib2/DocLib4/Mcp4/DocLibShot2` E2E). `LoadAsync`, `ApplyAsync` and
  `ReapplyAsync` now run one-at-a-time through `_mutationGate`, so queued
  mutations build on the freshly committed token; genuine cross-actor
  conflicts still produce the pending-conflict resolve flow. Regression
  coverage: `OverlappingApplyAsync_QueuesSecondMutationBehindInFlightSave`.

- **The Notion page-settings menu is clickable again.** CF10n migrated
  `.tm-npsm` from `z-index: 9999` to the popover band
  (`var(--tm-z-popover, 1030)`) but left the menu's click-outside backdrop
  as an inline `z-index: 9998` — a literal the band sweep could not see.
  The full-screen backdrop then painted *above* the 1030 panel and ate
  every pointer event, so no export/import/comment item could be clicked
  (`CF28`, `PageComment_MarkAllAsRead_SettingsMenu`, `CF25`/`CF26`
  import-export E2E). The backdrop is now the scoped
  `.tm-npsm__backdrop` class at `calc(var(--tm-z-popover, 1030) - 1)`,
  one step under the panel like every other anchored surface. Regression
  coverage: `OpenMenu_ClickOutsideBackdrop_UsesScopedClassNotInlineZIndex`
  plus `NotionEditorRazor_BackdropLayers_CarryNoInlineZIndex`, which scans
  the whole NotionEditor tree so the next inline band value cannot hide in
  markup again.

- `TmImageGallery` thumbnails are keyboard-operable native `<button type="button">` items (per
  [docs/keyboard-activation-convention.md](docs/keyboard-activation-convention.md): the browser
  delivers the activation, no emulated key handlers), carry an accessible name, and the lightbox
  returns focus to the thumbnail of the image on screen when it closes.

- **The command palette's keyboard highlight is visible in every theme.** The focused row painted
  `--tm-color-gray-50`, which in dark mode IS `--tm-bg-surface` (1:1 — the highlight vanished
  exactly where keyboard users need it) and about 1.04:1 on white in light. The row now paints
  `--tm-bg-muted` plus a 2px `--tm-color-primary` outline, the `TmMultiSelect` focused-option
  precedent; a `TokenContrastTests` entry holds the outline at ≥3:1 (WCAG 1.4.11 non-text) against
  the palette surface in light, dark, indigo and indigo-dark.

- **A More menu opened mid-scroll lands its initial focus when the anchor comes back.** A
  popover that opened while its trigger was scrolled out of the viewport was parked
  `visibility:hidden` by `overlay.js`, so the post-open `focus()` on the first enabled
  `menuitem` silently no-op'd — and the pending-focus flag was consumed on that first attempt,
  leaving keyboard users on `<body>` even after the anchor scrolled back into view (the More
  menu of `TmMobileActionBar` / the `TmToolbar` overflow, `TmDropdown`, `TmSplitButton`,
  `TmContextMenu` — every anchored panel that moves initial focus from `OnOpened`). `overlay.js`
  now reports a parked panel's first real visibility exactly once per open
  (`NotifyPanelVisibleAsync`), `TmOverlayPanel` raises `OnOpened` again, and the pending initial
  focus lands for real. A panel that opened on-screen never re-fires, and a report that lands
  after the panel closed is ignored. Regression coverage: the `overlay-lifecycle.test.mjs`
  park/first-visibility cases plus `PanelVisibleAfterParkedOpen_RunsThePendingInitialFocusAgain`.

- **A click inside the keyboard-shortcuts dialog no longer closes it.** `TmKeyboardShortcutsHelp`
  carried `@onclick:stopPropagation` on the `<TmFocusScope>` component tag — routed through a
  component's parameters the renderer treats it as a plain splatted attribute and ignores it (a
  runtime no-op), so the fence never existed and any click inside the dialog bubbled to the
  backdrop's close handler. The fence is now a real element wrapper around the scope — the
  `TmCommandPalette` pattern from phase 7 — so clicks inside stay off the backdrop while the
  close button and the Escape path keep working. The backdrop close also tracks the PRESS origin
  (m7): a drag that starts inside the panel and releases over the backdrop delivers only the
  click there, so the dialog closes only when the press started on the backdrop itself.
  Browser-proofed by `Shortcuts_ClickInsideTheDialog_DoesNotClose_BackdropClickDoes`.

- **`TmLightbox` mirrors in RTL.** The strip used to ignore the writing direction: in RTL the
  chevrons still pointed physically and ArrowLeft/Right kept their LTR mapping. The chevron
  glyphs now flip (`scaleX(-1)`) and the arrow keys swap with the reading direction (ArrowLeft is
  next, ArrowRight is previous), read live from the computed direction through the shared
  `tm-rtl.js` helper — "ltr" wherever JS is unavailable. Below 640px a panorama's content row
  also floors at one touch target: on a shorter row the centred arrows reached up into the
  close/delete band (-3rem) and overlapped the delete button. Guarded by the lightbox CSS guard
  tests and `TmLightboxRtlTests`.

- **The `TmLightbox` delete button clears the navigation arrows below 640px.** The delete used to
  sit in the top-end chrome band (top: -3rem, beside the close) — exactly the corner the
  overlaid arrows' upper ends reach on a short (panorama) row. It now sits in the footer flex
  row under the counter pill (still a 44px touch target), and the footer drops by one
  `--tm-space-2`, so the arrows keep ≥ 8px of real clearance from it at every image height.
  Desktop keeps the delete beside the close. Proven by
  `Gallery_Lightbox_390_DeleteMode_ArrowsStayClearOfTheDeleteButton` with the demo's Delete Mode
  on (next in LTR, prev in RTL).

- **The chevron mirror keys off the element's own resolved direction.** The RTL rule matched a
  hardcoded ancestor `[dir="rtl"]` attribute and missed hosts that flip direction through CSS;
  `.tm-lightbox-prev:dir(rtl) svg, .tm-lightbox-next:dir(rtl) svg` reads the same live resolved
  direction the `tm-rtl.js` probe reports, so the glyphs and the arrow-key semantics cannot
  disagree (m4).

- **`overlay.js` re-runs a parked panel's pending initial focus only when focus is free, and
  stale reports are dropped.** The first-visibility report now fires only when focus went
  nowhere (the parked panel's focus move dropped it to `<body>`) or still sits on the anchor
  trigger — a user who tabbed elsewhere while the panel was parked is not yanked back (the flag
  is still consumed). Each open carries a sequence number that rides on the report, and
  `TmOverlayPanel.NotifyPanelVisibleAsync` compares it against its own open sequence, so a
  report already in flight while the panel was closed and reopened can never fire the NEW open's
  `OnOpened` (m1/m2).

- **One `directionOf` for every RTL-aware component.** `tm-splitter.js` and `data-table.js` no
  longer carry private copies of the writing-direction probe: both reuse the shared
  `js/tm-rtl.js` helper (the splitter imports and re-exports it, the classic-script data table
  loads it by URL like `pointer-drag.js`), so the ltr fallback contract cannot drift. The
  `TmLightbox` also reads the direction once per open (on the opening render) instead of on
  every arrow-key press (m3).

- **The Czech table covers every English key again.** 53 keys had shipped English-only under cs —
  the `TmViewManager_*` view CRUD/scopes/visibility set (21 keys), the Notion block context menu
  (`TmNotionBlockContextMenu_Color*` and `_TurnInto*`, 22 keys), `TmNotionBlock_Copied` /
  `_CopyCode` / `_DragHandle` / `_HeadingPlaceholder` / `_MediaUrlPlaceholder` / `_Unsupported`,
  the Spreadsheet slash-menu entry, `TmNotionPage_NoComments` and `TmDataTable_CurrentViewName` —
  and now carry Czech translations. A new `TmResourceCompletenessTests` guard keeps all four
  tables honest from now on: every English key must exist in cs/sk/fr with the same `{0}`-style
  placeholders, the Slovak table must read as Slovak (no Czech-only ě/ř/ů), and every plural
  family must provide the forms each language's grammar needs.

- **The French comment-rail counter keeps its total.** `TmDocumentEditor_CommentCountVisible` had
  shipped as "{0} commentaires visibles" — the English string's `{1}` (the total comment count)
  was dropped, so the document editor's comment rail could not say how many comments exist. The
  value is now "{0} sur {1} commentaires"; the placeholder parity half of the new
  `TmResourceCompletenessTests` guard catches exactly this class of drift — a translation losing a
  `{0}`-style placeholder — in every shipped table.

### Accessibility

- **TmDiagramEditor: the header *Tools* toggle reports `aria-pressed="true"` while the palette is
  expanded, like the other panel toggles (it was inverted).** On tablet the toggle and the rail
  button collapse the palette to its rail instead of hiding it.

- **`TmWidgetSelector` is a real modal surface and the `TmDashboard` switcher is a WAI-ARIA menu.**
  The widget selector previously rendered its own `position: fixed` overlay — no `role="dialog"`,
  no `aria-modal`, no focus trap, no Escape handling and an unnamed close control. It now renders
  on `TmModal` (extra-large): `role="dialog"` + `aria-modal` + `aria-labelledby` named by the
  visible title, the shared focus trap with Escape close and scroll lock, a named close button,
  and the bottom-sheet presentation on a narrow viewport. Inside it, widget cards are native
  `<button>` elements whose children are phrasing content (the description span is referenced by
  `aria-describedby`, so the announced name stays the widget's name and the "3×2" size token is
  `aria-hidden`), a card that cannot be added again stays focusable and announces
  `aria-disabled="true"` instead of disappearing from the tab order, and the category filters are
  toggle buttons with `aria-pressed`. The dashboard switcher renders through `TmOverlayPanel` —
  a top-layer popover positioned by `overlay.js`, which owns the Escape/outside/Tab-out dismissal
  and the focus restore to the trigger, and attaches the shared `tm-menu-nav` module to the
  `role="menu"` panel, so the roving arrow keys, Home/End and the typeahead are actually wired;
  opening hands focus to the first item. Each dashboard row is a `role="none"` wrapper around a
  native `role="menuitem"` select button (Enter/Space activate natively — exactly one activation
  per press) and sibling `role="menuitem"` action buttons carrying the dashboard's NAME
  ("Delete dashboard {0}" / "Set {0} as default"); the current dashboard is marked with
  `aria-current`, the default-star badge carries the localized "Default dashboard" text through
  `role="img"` + `aria-label`, and every icon-only button in the dashboard toolbar/widget headers
  (apply/cancel rename, rename, remove widget, collapse/expand) has an `aria-label`; the collapse
  control also exposes `aria-expanded` + `aria-controls` pointing at the widget content region it
  toggles. Focus is never stranded: creating a dashboard moves focus to the "Add widget" button,
  leaving edit mode (save or cancel) returns to the switcher trigger, and the inline rename is a
  `<form>` whose Escape cancels and whose save/cancel return focus to the rename button.

- **The input-family error messages can opt out of the assertive `role="alert"` announcement.**
  Every input component with an `Error` parameter — `TmTextInput`, `TmTextArea`, `TmSelect`,
  `TmNumberInput`, `TmDecimalInput`, `TmMultiSelect`, `TmEntityPicker`, `TmCodeEditor` and
  `TmExpressionEditor` — announced its own error through `role="alert"` unconditionally. When the
  host also moves focus to the invalid field after a failed submit (e.g. a modal form whose submit
  focuses the first invalid field), the screen reader read the error twice: once from the live
  region, once through `aria-describedby` on the focused control. The new `AnnounceError`
  parameter (default `true`, so the current output is unchanged) drops only the `role` attribute
  when set to `false` — the message still renders, keeps its error styling and stays referenced
  by `aria-describedby`/`aria-invalid`:

  ```razor
  <!-- default: role="alert" announces the error the moment it appears -->
  <TmTextInput @bind-Value="model.Name" Label="Name" Error="@_nameError" />

  <!-- the host focuses the field itself, aria-describedby reads the error: no live region -->
  <TmTextInput @bind-Value="model.Amount" Label="Amount" Error="@_amountError" AnnounceError="false" />
  ```

  **Scope:** the opt-out covers only the input's own `Error` message. `TmFormValidationMessage` and
  the range pickers' own field errors (`TmTimeRangePicker`, `TmDateTimeRangePicker` invalid-range
  messages) keep their `role="alert"`, so EditContext-driven and cross-field validation keep
  announcing; `TmFormField` now exposes its own `AnnounceError` with identical semantics (see
  below), so a host that opts out mutes the wrapper and the input independently.
  **Only safe when the same render that shows the error also moves focus to this control.** Keep
  the default for validation on blur or on input, async/server validation, or when focus stays on
  the submit button — otherwise the error is not announced at all. With several invalid fields,
  pair it with a summary (e.g. `TmValidationSummary`) or focus the first invalid field.

- **`TmFormField` errors get a stable id and the same `AnnounceError` opt-out, and the id reaches
  the wrapped control's `aria-describedby`.** The field-level error `<p>` (from `ErrorMessage` or
  EditContext validation) had no id, so a screen reader that focus-navigates into the wrapped input
  read only the control's own message — a field-level EditContext error was announced once by the
  live region and then unreachable from the control. The error paragraph now renders
  `id="{Id}-field-error"` (new optional `Id` parameter, defaulting to a per-instance generated id;
  the suffix deliberately differs from the inputs' `{Id}-error` so a field and its wrapped input
  can share the same `Id` without the two error ids colliding), exposed as `TmFormField.ErrorId`.
  While the paragraph renders, the id is cascaded to the wrapped content (`TmFormFieldErrorId`) —
  the cascade reaches every Tm input inside the field's content, and each input with no own `Error`
  — the nine inputs above plus `TmRadioGroup` — adds it to its own `aria-describedby` (after the
  input's help text), so the field-level message is readable from the control in every state:

  ```razor
  <TmFormField Label="Email" Id="email" ValidationFor="@(() => model.Email)" For="email">
      <TmTextInput id="email" @bind-Value="model.Email" />
  </TmFormField>
  <!-- on a validation error the input renders aria-describedby="email-field-error" pointing at the field's error <p> -->
  ```

  The field also accepts `AnnounceError` (default `true`) with exactly the semantics of the inputs:
  `false` drops only the `role="alert"` attribute — the message still renders, keeps its error
  styling and its stable id, and stays cascaded and referenced. The default DOM is unchanged except
  for the added `id` attribute (frozen by full-markup snapshots in
  `TmFormFieldErrorAnnouncementTests`).

- **`TmRadioGroup` errors are announced, linked and marked invalid like the input family.** The
  error `<span>` under the radiogroup rendered as plain styled text — no `role`, no id, no
  `aria-invalid`, no `aria-describedby` — so a screen reader never announced it and focus on the
  group read nothing. It now renders `role="alert"` with a stable id
  (`tm-radio-error-{guid}`, exposed as `TmRadioGroup.ErrorId`), the radiogroup container links it
  through `aria-describedby` and sets `aria-invalid="true"` while the error shows, and the new
  `AnnounceError` parameter (default `true`) drops only the `role` attribute under the same
  conditions as the inputs. Inside a `TmFormField` whose field-level error is showing, the group
  additionally describes that message through the cascaded `TmFormFieldErrorId` when it has no own
  `Error`.

- **A `TmNotificationBell` unread entry announces its state.** The blue dot was `aria-hidden`
  decoration, so a read and an unread entry were indistinguishable to a screen reader. Unread
  entries now carry a localized screen-reader-only text alternative (`tm-sr-only`,
  `TmNotificationBell_Unread` — en/cs/fr); read entries announce nothing (m6).

- **The `TmNotificationBell` focus indicator draws inside the entry row.** The rows sit flush
  with the panel edge and the dropdown clips overflow, so the old outside box-shadow ring was
  invisible on the first and last row — exactly where a keyboard user lands. The
  `:focus-visible` indicator is now an inset 2px `--tm-color-primary` outline (offset -2px,
  `box-shadow: none`), guarded by `NotificationBellCssGuardTests` (M1).

- **The `TmWorkflowDesignerCanvas` states are named, focusable buttons on a labelled canvas.** The
  svg root dropped `role="img"` for `role="group"` (see Breaking/Migration), every state node is a
  `role="button"` with a localized `aria-label` (`TmWorkflowDesignerCanvas_StateLabel` —
  "{name} ({type})", the type word localized too) and a roving tabindex (one tab stop for the
  group; the arrows move the focus, their page-scroll default is cancelled in `workflow-designer.js`).
  The `:focus-visible` ring works on the SVG shapes, with a `forced-colors` variant; the temporary
  transition line lost its hardcoded hex for the `--tm-color-primary` token (Highlight under
  forced colors), and the connection ports are always visible with a ≥24 px coarse-pointer target
  (touch has no hover to reveal them).

- **The `TmDataTable` resize handle is a keyboard-operable separator.** The handle renders
  `role="separator"` with `tabindex="0"`, `aria-orientation="vertical"`, always-emitted
  `aria-valuenow` / `aria-valuemin` (60) / `aria-valuemax` (1200, raised to track the width when
  a column grows past it) and a localized
  `aria-label` (`TmDataTable_ResizeColumn` — "Resize column {0}"). Arrows resize by 8 px
  (Shift by 32 px, inverted in RTL like the drag), `Home` jumps to the minimum, `End` to the
  announced maximum — every key commits through `SetColumnWidthAsync`, the single persisting path
  (WCAG 2.5.7 non-drag alternative). The keys that would scroll the table are cancelled in
  `data-table.js`, not with a Blazor `:preventDefault`; the handle's keydown and click are fenced
  from the header's sort handler. Under `(pointer: coarse)` the handle widens to a real target,
  and it paints the shared focus ring with a `forced-colors` variant.

- **The three `TmColorGradient` sliders are keyboard-operable.** The saturation/value area, the
  hue track and the alpha track are `tabindex="0"` sliders with arrows (1 % / 1° per step,
  Shift ×10; the area's arrows move saturation horizontally and brightness vertically),
  `Home`/`End` at the bounds, and a localized `aria-valuetext` on the area
  (`TmColorGradient_AreaValue` — "Saturation {0}%, brightness {1}%"); every key commits through
  `ValueChanged`, the same path as the RGBA inputs, and the hue slider announces its own
  localized `aria-valuetext` (`TmColorGradient_HueValue` — "{0}°"). The axis keys' scroll default is cancelled in
  `tm-color-gradient.js`. The sliders paint the shared focus ring (with a `forced-colors`
  variant) and the thumbs grow on a coarse pointer.

- **Every `TmColorPalette` swatch announces its own color.** Every swatch and the grid carried
  the same generic `aria-label` "Select color", and the color value was never passed to the
  localizer, so a screen reader read an indistinguishable "Select color, button" for all 64
  default swatches (WCAG 1.1.1/4.1.2). The grid is now a labelled `role="radiogroup"` and each
  swatch is a native `<button role="radio">` with `aria-checked` for the selection, the existing
  roving tabindex, and a per-swatch accessible name: the new optional
  `ColorNames` parameter (`IReadOnlyDictionary<string, string>`, keyed by the color string as it
  appears in `Colors`, matched case-insensitively — a key spelled exactly like the palette color
  wins over a case-variant duplicate) names its swatch with the localized
  `TmColorPalette_SelectColorNamed` template ("Select color {0}" / "Vybrat barvu {0}" /
  "Sélectionner la couleur {0}", en/cs/fr), and a color without a — non-empty — entry falls back
  to announcing its raw value ("Select color #2E7D32"), so the swatches are always tellable
  apart even without `ColorNames`. The name lookup is cached per `ColorNames`/`Colors` reference
  in `OnParametersSet` instead of a linear scan per swatch per render. Custom `ITmLocalizer`
  implementations that predate the `TmColorPalette_SelectColorNamed` key stay supported: when
  the template is missing the label falls back to the plain `TmColorPalette_SelectColor` text
  plus the name/value ("Select color #FF0000") instead of a raw `[key]` placeholder.
  The `TmFlatColorPicker` / `TmColorPicker` palettes inherit the fix through their embedded
  `TmColorPalette`, and can now be named directly through their new `PaletteColorNames`
  parameter (see below).

- **`TmColorPalette` keyboard navigation moves focus only — selection follows Enter/Space.**
  The arrow keys, `Home` and `End` move the roving tabindex without committing a selection
  (selection does not follow focus), so keyboard users can explore a palette embedded in a
  `TmFlatColorPicker` / `TmColorPicker` without committing a color or closing the picker on
  every stroke; Enter/Space select through the native button activation. The keyboard-focus
  state now also clears on `focusout` (the ring used to survive focus having left the palette,
  and the stale roving tabindex blocked `OnParametersSet` from returning it to the selected
  swatch). Documented in the class XML docs and the component JSON documentation.

- **The `TmColorPalette` focus ring no longer paints onto neighbouring swatches, and survives
  forced-colors mode.** The old indicator was a 2px `--tm-color-primary` outline with a 2px
  offset — outward, exactly across the 2px grid gap onto the adjacent swatch, where a primary
  ring over a blue swatch measured ~1:1 (WCAG 1.4.11). The ring is now two-tone and inward:
  `outline: 2px solid var(--tm-color-primary); outline-offset: -2px;` plus an inset
  `var(--tm-surface)` band (`box-shadow: inset 0 0 0 4px var(--tm-surface)`) that separates the
  ring from the swatch fill — independent of the neighbours, ≥3:1 in both themes
  (`ColorPaletteFocusRingContrastTests`). A focused-and-selected swatch keeps its selection
  border under the inward ring, so the two states stay distinguishable. Under
  `(forced-colors: active)` the swatches opt out of the system override
  (`forced-color-adjust: none`) so the authored colors stay visible and the selection is marked
  with a 3px `Highlight` outline (system colors are allowed there).

- **`TmFlatColorPicker` and `TmColorPicker` accept `PaletteColorNames`.** Hosts of the embedded
  palettes previously had no way to name their colors — a screen reader read raw hex values
  unless they switched to a standalone `TmColorPalette`. The new optional
  `PaletteColorNames` (`IReadOnlyDictionary<string, string>`) is forwarded to the embedded
  `TmColorPalette`'s `ColorNames` exactly like `PaletteColors`/`PaletteColumns`, so
  `TmColorPicker` → `TmFlatColorPicker` → `TmColorPalette` all announce the localized
  "Select color {name}" (see `TmColorPalette.ColorNames` for the matching and fallback rules).
- **Card-mode `TmDataTable` cells wrap their content.** A card-mode cell is a
  flex row of `label ::before + content`, but the content side had no line
  breaking, so a single unbroken token (an id, a hash, a long name without
  spaces) overflowed the card edge — measured on a consuming app's
  delegation list at 5–24px past the card border on a 390px viewport. The
  cell now declares `overflow-wrap: anywhere` (which inherits into the
  anonymous flex item holding bare text) and direct element children get
  `min-width: 0` so they may shrink below their min-content width. The rule
  lives inside the card-mode media query only; desktop/table rendering is
  unchanged. Verified end-to-end in the consuming app's full-run gate
  (`r6r7-final`) via its `delegation-list` UX audit.

### Erratum 2.9.0

Two changes shipped in 2.9.0 under routine fix/feature sections but are
retroactively **Breaking / Migration** — each can silently lose a host
override, and each was discovered only when a downstream application hit it.
They are recorded here, in the release that publishes the correction notice,
rather than retro-edited into the released 2.9.0 section.

- **Button and input modifier selectors moved from (0,1,0) to (0,2,0)
  (`c67cd2cc`).** The bare-modifier tie — `.tm-btn-sm` at (0,1,0) competing
  with `.tm-btn` at (0,1,0) on import order — was fixed by compounding the
  modifiers onto their base: `.tm-btn.tm-btn-{xs,sm,md,lg}`,
  `.tm-btn.tm-btn-primary`, `.tm-btn.tm-btn-secondary`,
  `.tm-btn.tm-btn-outline-secondary`, `.tm-btn.tm-btn-ghost` and the
  `.tm-input.tm-*` input modifiers now sit at specificity (0,2,0). A host
  stylesheet that overrode a Tempo button or input modifier through a
  single-class selector (`.my-btn { height: … }`, still (0,1,0)) used to win
  or tie and now silently loses — nothing at build or test time flags it.
  **Migration:** override with an equally compound selector
  (`.tm-btn.my-btn`), or prefer the documented `--tm-*` design tokens
  (e.g. `--tm-input-height-md`), which change the declaration instead of
  fighting the selector.

- **Migrated floating panels position inline in the browser top layer
  (`7196e8b2`).** The migrated set — `TmPopover`, `TmDropdown`,
  `TmFilterableDropdown`, `TmContextMenu`, `TmSplitButton`, `TmDatePicker`,
  `TmDateRangePicker`, `TmDateTimePicker`, `TmMultiSelect`, `TmTagPicker`,
  `TmEntityPicker`, `TmNotificationBell`, `TmColorPicker`,
  `TmMultiColumnComboBox`, `TmQueryInput`, plus the panels inside
  `TmDataTable` — no longer positions panels through the stylesheet cascade
  alone: `overlay.js` writes `top`/`left`/`width` inline (inline style beats
  any non-`!important` rule), and the `:where()` panel reset carries
  specificity (0,0,0). A host rule that positioned, sized or offset a
  dropdown, calendar or menu panel can now conflict with the inline values
  or stop applying without any error. **Migration:** position through the
  component parameters — `Placement`, `Align`, `Offset`, `ViewportMargin`,
  `MatchAnchorWidth`, `ConstrainHeight` — not through host CSS targeting the
  panel's box.

### Release checklist

Every implementation step of the 2.9.1 development line, mapped to the commit
that landed it. Steps whose deliverable is a measurement or a ledger row rather
than a code change name the evidence instead of a hash.

| Step | Item | Commit |
|------|------|--------|
| announce | `2.9.1 (unreleased)` announced across the packable set | `7d7823ae` |
| 16.1 | N318 — `ConstrainHeight` cap grows back with the room | `4af89a0c` |
| 16.2 | N319 — menus close before invoking the item handler | `c2b2dcf6` |
| 16.3 | N324 — `MatchAnchorWidth` respects the panel `min-width` | `927e1e20` |
| 16.4 | N325 — `TmNotionNotificationCenter` width rule applies | `e32ea9fa` |
| 16.5 | N326 — `Role="dialog"` panels expose an accessible name | `844cb2b0` |
| 16.6 | N329 — hidden panel releases focus | `d96ee971` |
| 16.7 | CF03c — `.tm-overlay-panel` bundle rule inventory pinned | `2d25ff1b` |
| 16.8 | CF10n — Notion floating surfaces on `--tm-z-*` bands | `c27b2399` |
| 16.9 | CF18-7 — AI/mention menus in the top layer (+ the degenerate-viewport dismissal fix) | `a145bdf0` |
| 17.1 | N320 — `TmValidatedField` green state tracks the field | `7e7246ac` |
| 17.2 | N321 — `TmNavigationGuard` dialog inert during an in-flight save | `879c85e8` |
| 17.3 | N323 — dismissal cancels the armed debounce | `aa5bc776` |
| 17.4 | CF09b2 — track-changes toggle syncs the canvas engine flag | `54587a7e`, `e9729a1f` |
| 17.5 | CF18-1/CF18-2 — submenu focus on Enter/Space and mouseleave | `282ac3ca` |
| 17.6 | CF18-3 — `focusMenuInput` yields to deliberate focus moves | `e2b15022` |
| 17.7 | CF18-4 — InteractiveAuto E2E proves the WASM renderer | `1eb10ceb` |
| 17.8 | CF18-5a/CF18-5b — combobox/listbox ARIA wiring | `7fa79e52` |
| 17.9 | CF18-6 — monotonic RenderTreeBuilder sequence numbers | `7dc81fa1` |
| 17.10 | CF29 + ddfb:N273 — keystrokes buffered across the Enter split | `99870388` |
| 17.11 | CF06e1 — undocumented-parameter ratchet | `88e9b003` |
| 17.12 | CF06e2/F13 — `TmDataTable.razor.cs` file budget | verified unchanged at 2418 lines under the list frozen by `29172da2` |
| 18.1 | N276 — `TEMPO_REQUIRE_FEED` fails the feed guard | `43d28201` |
| 18.2 | CF15a/CF15b/CF15c — evidence totals, duplicate keys, vacuous restarts | `27975543` |
| 18.3 | CF19a–h — eight fail-closed gate holes closed | `2eede79c` |
| 18.4 | N327 — retroactive 2.9.0 erratum | `1dcf2c5f` |
| 18.5 | N328 — same-commit JSON documentation guard | `c4471191` |
| 19-fix | card-mode `TmDataTable` cell wrap (consuming-app gate finding `r6r7-final`) | `f5703a01` |

## 2.9.0 - 2026-09-27

The number is 2.9.0 rather than 2.8.27 because this release carries a breaking change
(`TmStatCard.SubValueColor` no longer accepts CSS class names — see **Breaking / Migration**
below) on top of the fixes. What was announced as 2.8.27 was test-surface and documentation
hardening only — no shipped-component changes since 2.8.26 — and moves under this heading
unchanged. The number had to open anyway: `v2.8.26` is a spent tag naming `ae72ffe0`, and the
announce commit is conventionally the first commit after the tag — these four landed without it,
which is exactly the red
`AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked` is written to refuse.

### Fixed

- **Scriban code inside block content renders again (regression from 2.9.0).** `MjmlGenerator` sanitized
  the authored content of `mj-text`/`mj-accordion-text` *before* substitution, and the sanitizer's
  quote-safe formatter entity-encoded the code itself, so `{{ price | math.format "N2" }}`,
  `{{ text | string.truncate 5 "..." }}`, `{{ if price > 100 }}` and any `&&` produced
  `Invalid token found &` and an empty `RenderResult.Html`. Code spans (`{{ … }}`, and `{% … %}` when it
  carries no markup) are now masked, the rest of the content is sanitized, and the spans are restored
  verbatim (`HtmlContentSanitizer.SanitizeTemplateContent`); everything inside a span is still escaped by
  the render pipeline and the whole substituted MJML/HTML is still checked by `MarkupGuard`, so a value in
  an attribute cannot break out and markup cannot be smuggled through a span.

  ```text
  before  {{ price | math.format "N2" }}   → RenderResult.Html == "" (error: Invalid token found `&`)
  after   {{ price | math.format "N2" }}   → 1,234.50   (cs-CZ: 1 234,50)
  ```

- **A deeply nested document can no longer crash the process.** The MJML importer measures the element
  depth iteratively right after parsing (a 20 000× nested `mj-hero`/`mj-group` payload used to overflow the
  mapping recursion), threads the model depth through the mapping as a second guard, refuses
  self-/mutually-recursive `mj-include` references (a path stack plus an include depth limit of 8) and the
  JSON reader/writer bounds depth from the document's own nesting. `EmailDocumentLimitsValidator` and the
  schema-version code walk the block tree iteratively too, and `Deserialize(json, options)` also enforces
  `MaxInputBytes` and the section/block limits (throwing `EmailTemplateSerializationException` with the
  localization key, never a silent accept). The JSON depth bound is derived from the model nesting limit
  (`14 + 6 × MaxNestingDepth`, decision `EMAIL-IMPORT-JSON-DEPTH`); a plain `Deserialize(json)` without
  options keeps `max(64, …)`, i.e. it is never stricter than `System.Text.Json`'s own default.

### Breaking / Migration

- **`TmStatCard.SubValueColor` must now be a CSS colour, not a class name.** Landed as **`0655b971`**.
  The value is written
  verbatim into an inline `style="--tm-stat-subvalue-color: …"`, so until now *any* string reached
  the attribute: a class name such as `text-green-600` produced a declaration no browser could
  resolve (the subvalue silently kept its default colour), and a payload containing `;` could smuggle
  further declarations into the element. The parameter is now validated before it is emitted —
  accepted values are `var(--…)` references, hex literals (`#rgb`, `#rgba`, `#rrggbb`,
  `#rrggbbaa`), `rgb(…)`/`rgba(…)`, `hsl(…)`/`hsla(…)`, `oklch(…)`, `color-mix(…)` and the CSS named
  colours. Anything else — including every class name — emits **no** `style` and logs a warning
  through the optionally injected `ILoggerFactory` ("SubValueColor expects a CSS color (2.9.0
  breaking change: CSS class names are no longer accepted)"). Migration is mechanical:

  ```razor
  @* before (dead since 2.8.26, warned since 2.9.0) *@
  <TmStatCard SubValueColor="text-green-600" … />
  @* after *@
  <TmStatCard SubValueColor="var(--tm-color-success-text)" … />
  @* or a literal *@
  <TmStatCard SubValueColor="#16a34a" … />
  ```

- **The `prefers-reduced-motion` reset no longer reaches host-page elements.** The
  `@media (prefers-reduced-motion: reduce)` block in `animations.css` used to target
  `* / *::before / *::after` — every element in the document, owned or not. Worse than the
  over-reach, it declared only *durations*: with `transition-property: all` and an initial
  `transition-duration` of `0s`, raising the `0.01ms` value one day would have started
  *manufacturing* transitions on every property of every element — more motion for the group
  that asked for less. The block now enumerates the Tempo-owned classes that actually carry
  `transition`/`animation` (364 selectors, regenerated by grep over the library stylesheets and
  pinned by `ReducedMotionSelectorTests`, so a new animated class without an entry fails the
  suite). The `0.01ms` duration is deliberate and unchanged — it still fires
  `transitionend`/`animationend` for JS that awaits them. **Migration:** if a host page relied on
  Tempo's stylesheet to neuter *its own* animations under reduced motion, it must ship that reset
  itself — a component library touching elements it does not own was the defect, not the feature.
  The universal `box-sizing` reset in `base.css` stays: it is the page-author idiom every CSS
  framework ships, and Tempo's own markup contains bare elements that would lose it under a
  narrowed selector.

- **`TmScrollSpyNav.AutoSelectFirstItem` is removed.** The parameter existed so a page could keep
  *nothing* current until the user acts — a workaround for scroll-spy silently never firing on shells
  whose content scrolls in its own `overflow-y` column (no window scroll event). That defect has a
  real fix since 2.8.x (`ScrollContainerSelector` points the observer at the actual scroller), and the
  application's gap register records the parameter as *unused API*: nobody opted out, because the
  working observation behaviour made the stale-highlight problem disappear on its own. The component
  now always treats the first visible item as current until a click or a scroll names another — the
  released default. **Migration:** delete the argument. If you set it to `false` because
  `aria-current` went stale on a long page, the stale highlight was the window-listener defect —
  enable `EnableScrollSpy` with `ScrollContainerSelector` aimed at the column that actually scrolls.
  If a page genuinely wants no current section ever, that is the case the register deferred: feed
  `ActiveId` from your own state instead of relying on the removed switch.

- **`TmOverlayPanel`'s content mounts only while the panel is open.** Before the Popover-API
  migration `ChildContent` stayed in the DOM, hidden by CSS, whenever `IsOpen` was `false`; it
  is now rendered inside an `@if (IsVisible)` block (`TmOverlayPanel.razor`), so a closed
  panel's children are fully disposed and re-created on the next open. Every consumer built on
  `TmOverlayPanel` — `TmDropdown`, `TmPopover`, `TmFilterableDropdown`, `TmDatePicker` and the
  rest of the 15 — shares this. Migration: a stateful child that must survive close/reopen
  (a form draft, a scroll position) needs to persist that state OUTSIDE the panel (parent
  component field, not local `@code` state of the child), and code that queried for the panel's
  content while closed (bUnit `cut.Find` against a hidden node, direct DOM selectors) must
  instead assert absence when closed and presence only after opening.

- **`TmColorPicker` no longer exposes `CloseFromGlobalAsync`/`CloseFromGlobalEscapeAsync`,
  does not implement `IAsyncDisposable`, and `color-picker.js` no longer exposes
  `window.tmColorPicker.registerEscape`/`adjustDropdownPosition`/`unregister`.** The dropdown
  is a `TmOverlayPanel` now — dismissal (Escape, outside pointerdown), placement, flip/shift
  and scroll/resize tracking all live in the shared `overlay.js`, which owns disposal too.
  `color-picker.js` keeps only `focusPaletteSwatch`, the palette keyboard-navigation helper
  `TmColorPalette` still calls. Migration: any code calling the removed JSInvokable methods or
  `window.tmColorPicker.*` global functions directly should remove that call — `TmOverlayPanel`
  already handles the same dismissal contract.

- **`TmNavigationGuard.OnSaveAndLeave` returning `false` now closes the dialog and forgets the
  blocked destination.** The signature is unchanged (`Func<Task<bool>>?`), but the failure
  contract changed: until now a failed save left the dialog open with `_pendingTargetLocation`
  retained, so a second click on "Save and leave" retried against the original target. The
  guard cannot know what failed or where the host wants focus, so per the
  DEC-SAVE-AND-LEAVE-FAILURE decision it now closes its own dialog, keeps navigation cancelled,
  clears the blocked destination, and raises the new `OnSaveAndLeaveFailed` callback (see
  **Added**). **Migration:** if a host relied on the same-dialog retry (nothing else did — the
  destination was never exposed), treat every leave attempt as fresh: wire
  `OnSaveAndLeaveFailed` to focus the form's own error surface (first invalid field or an
  `aria-live` error region); the user's next navigation attempt re-opens a fresh dialog for
  wherever they are actually going then.

### Added

- **`TmDataTable.ShowResultSummary` (`bool?`).** The item count used to exist only inside the
  paging footer, so a table without a pager — `ShowPagination="false"`, grouping, a single page,
  a non-paging `ScrollMode` — printed nothing, and a reader (or a screen reader) could not tell
  the table ended rather than truncated. `null` (the default) keeps that behaviour byte-for-byte;
  `true` renders the `pagination-summary` line whenever the pager is not already printing it,
  reporting the honest full range (`1–N of N`, since without a slicing pager every row is on the
  page); `false` suppresses the summary everywhere, including the footer's Summary slot.
  `PaginationInfoTemplate` is honoured in the standalone line.

- **`FormActionBarPosition.FloatingBottomFromMd` — the responsive placement the application gap
  register (#13) asked the library to own.** `FloatingBottom` pins the bar to the viewport at
  every width: on a phone it clips the viewport height it shares with the content (twice over
  once the actions wrap into the `column` layout) and `position: fixed` keeps it painted over
  the open mobile navigation, which only the `--tm-form-action-bar-z-index` z-ordering could
  soften — a deliberate cover, not a mode switch. A host could not express "static below,
  floating above" itself: the floating rule lives in `TmFormActionBar.razor.css` behind the
  `[b-*]` scope attribute, so an application media query would have had to out-specify that and
  re-verify the override after every upgrade. `FloatingBottomFromMd` renders
  `tm-form-action-bar--floating-bottom-md`, which carries the floating contract verbatim inside
  `@media (min-width: 768px)` — the same edge the column wrap already uses — while below the
  boundary the bar is plain document flow. The reserve follows *with it* and on the *same*
  number: inside the existing `@media (max-width: 767.98px)` in `tokens.css`, the new
  `:root:has(.tm-form-action-bar--floating-bottom-md)` gate collapses
  `--tm-form-action-bar-reserve-block-size` to `0`, so a host that already pads its page by the
  token stops reserving the moment the bar stops overlaying — no dead space to measure, no
  second breakpoint for two repositories to keep in step. `FloatingBottom` keeps its
  every-width contract untouched. The mode rides on the demo's `/toolbar-forms` page and is
  measured in a real browser by `FormActionBarResponsiveE2ETests` at 390×844 (static + zero
  reserve) and 1440×900 (fixed + positive reserve + content clear of the bar).

- **The sort button's multi-sort gesture is discoverable.** Shift+Enter (and Shift+click) has
  appended the column to the sort chain for a while — the state icon showed the result and the
  `aria-label` named the next action, but the *modifier* existed only for the user who already
  knew it. `.tm-th-sort` now carries a localized `title` (new key `TmDataTable_MultiSortHint` in
  all three resource sets), which paints a tooltip for the pointer user and lands in the
  accessible description for a control whose name comes from `aria-label`.

- **`TmOverlayPanel` — a shared floating-layer primitive built on the HTML Popover API.** Every
  floating surface in the library (dropdown, date-picker calendar, context menu, notification
  bell, color picker, query suggestions…) used to re-implement the same four problems — where to
  put the box, when to flip it, how to keep it glued to its anchor while scrolls run, and how to
  dismiss it — and each re-implementation lost against a modal's `overflow`, a transformed
  ancestor, or a sibling stacking context somewhere. `TmOverlayPanel` renders `popover="manual"`
  and `showPopover()` puts the element into the browser **top layer**: it paints above every
  z-index stacking context (structurally, not via a bigger `z-index`) and its containing block is
  the viewport, so no ancestor can clip it or shift its coordinates. The collocated
  `wwwroot/js/overlay.js` engine positions the panel in viewport coordinates with
  `Placement` (`Top`/`Bottom`/`Left`/`Right`), `Align` (`Start`/`Center`/`End`), `Offset`,
  `ViewportMargin`, `Flip` (moves to the opposite side only when the preferred side lacks room
  *and* the other side is strictly better), `Shift` (cross-axis clamp inside the margin),
  `MatchAnchorWidth` and `ConstrainHeight`; it repositions on window *and* ancestor scroll and on
  resize through a shared `requestAnimationFrame` pass, dismisses on `Escape` (returning focus to
  the trigger) and on outside pointer-down, and cleans every listener on close/dispose. Where the
  Popover API is missing, the same engine falls back to `position: fixed` and walks ancestors to
  find the transformed containing block, mirroring the old per-component math. The resolved side
  is exposed on `data-tm-placement` so consumers (e.g. `TmPopover`'s arrow) can react. All
  fifteen floating components now delegate to it (see **Fixed** below), and the `/overlay` demo
  page exercises placements, overflow escape, modal stacking and the viewport-edge flip.

- **`TmNavigationGuard.OnSaveAndLeaveFailed` (`EventCallback`).** Raised after a failed
  `OnSaveAndLeave` once the guard's dialog has closed (DEC-SAVE-AND-LEAVE-FAILURE). The guard
  deliberately does not pick a focus target or an announcement — it does not know the host's
  error surface — so the host wires this callback to move focus to its own error area or first
  invalid field and announce it (e.g. `aria-live`). Navigation stays cancelled and the blocked
  destination is cleared: a retry means the host triggers a fresh navigation attempt, not a
  second click in the same dialog.

### Fixed

- **`TmSelect<TValue>` now round-trips numeric value types.** `HandleChangeAsync` previously
  parsed only `string`, `int`, `Guid` and enum back from the raw `<select>` change event —
  `TmSelect<short?>`, `TmSelect<byte>`, `TmSelect<decimal>` and every other numeric `TValue`
  (`short`, `ushort`, `long`, `ulong`, `uint`, `sbyte`, `float`, `double`, plus their nullable
  forms) silently fell through to `default`, so a selected option was reported back as `null`/`0`.
  Both directions are fixed and kept symmetric: `HandleChangeAsync` now parses every integral type
  with `NumberStyles.Integer` and every floating type (`decimal`/`double`/`float`) with
  `NumberStyles.Float`, both against `CultureInfo.InvariantCulture` (the existing `int` branch was
  switched to the same invariant overload); and the rendered `<option value>` /
  `<select value>` (`FormatOptionValue`, used by both `SelectedValueString` and the options
  `@foreach`) formats any `IFormattable` value with `ToString(null, CultureInfo.InvariantCulture)`
  instead of the ambient-culture default `ToString()`. Without the render half of this fix, a
  decimal/double option value would have rendered with the current culture's decimal separator
  (e.g. Czech `1,5`) while the parser only ever accepted the invariant `1.5` — a value the browser
  round-trips back to the parser verbatim, so render and parse must agree. `string`/`Guid`/enum
  behaviour is unchanged.

  **Impact on `ChildContent` consumers.** This invariant-culture change only covers the `Options`
  parameter path (`FormatOptionValue`); a hand-written `<option value="@x">` inside `ChildContent`
  still renders through Blazor's own ambient-culture `ToString()`, unaffected by this fix — only the
  *parse* side (`HandleChangeAsync`) changed. For `int` (already supported before this release), that
  is a behaviour change for cultures whose `NegativeSign` is U+2212 MINUS SIGN rather than the ASCII
  hyphen (e.g. `sv-SE`, `nb-NO`, `fi-FI`): a negative `ChildContent` option used to round-trip only
  because the old parser also read the ambient culture and accepted `−`; the new invariant parser
  only accepts ASCII `-`. The newly-supported numeric types (`short`, `ushort`, `long`, `ulong`,
  `uint`, `byte`, `sbyte`, `decimal`, `double`, `float`) have no such prior behaviour to preserve —
  they always fell through to `default` via `ChildContent` before this release — but the same rule
  now applies to them too. Any `ChildContent` author relying on ambient-culture parsing for a numeric
  `TValue` must write the option value explicitly in the invariant form, e.g.
  `value="@x.ToString(CultureInfo.InvariantCulture)"`.

- **`TmSpreadsheet` (canvas JS engine): key commands run against the selection at their key press.**
  The canvas applies selection changes locally and only queues them for .NET (one frame, then a
  debounced command-log batch; cell edits ride a 120 ms commit batch), but copy/cut, Paste Special,
  Delete, undo/redo and the formatting shortcuts were sent to .NET immediately. A fast Shift+Arrow →
  Ctrl+C therefore filled the internal clipboard from the PREVIOUS selection, and Paste Special (which
  reads that clipboard) pasted the old, smaller range while the system clipboard held the new one; the
  same race made Delete or Ctrl+B act on the old range. Each key command now first queues a
  still-pending selection and edits and delivers exactly the commands queued up to its key press;
  keyboard selection changes and cell edits queued after the press (a further Shift+Arrow during the
  round-trip) are held back until that key command has been dispatched, so they cannot leak into it —
  also when several key commands follow each other. Known limitation: pointer input (cell click,
  double-click, context menu — `OnCanvasCellPointer` / `OnCanvasPointer` / `OnCanvasDoubleClick` /
  `OnCanvasContextMenu`) calls .NET directly and does not pass through this barrier, so a mouse
  selection made while a key command is still waiting can still reach .NET before it. Key commands are sent in key-press order, each as soon as the previous one has
  been dispatched (not completed), so a key command whose .NET call never returns (e.g. during a
  reconnect) does not block the keys after it; waiting for the pending state itself is capped at 2 s.
  A settled selection is not re-sent (no extra round-trip and no spurious `ActiveCellChanged` per
  key). Guarded by the E2E tests `PasteSpecial_CopyInTheSameTaskAsTheSelectionChange_CopiesTheExtendedRange`,
  `PasteSpecial_SelectionChangedAfterTheCopy_DoesNotLeakIntoTheCopiedRange`,
  `PasteSpecial_CopyQueuedBehindASlowKeyCommand_KeepsItsOwnSelection`,
  `KeyCommand_AfterAKeyCommandThatNeverCompletes_StillReachesDotNet`,
  `Delete_InTheSameTaskAsTheSelectionChange_ClearsTheExtendedRange` and
  `KeyCommand_WithASettledSelection_DoesNotResendTheSelection`.

- **`TmOverlayPanel` post-migration rework (Fáze 18 review).** One Escape gesture no longer
  closes two layers: `overlay.js` consumes the keydown (preventDefault +
  stopImmediatePropagation) once it has actually dismissed a panel and swallows the matching
  keyup at the window, so the keyup-driven `TmModal`/`TmDialog` underneath survives — and the
  suppressor disarms on blur so a lost keyup cannot eat the next Escape. Outside dismissal now
  distinguishes targets: a pointerdown into a focusable element keeps the focus the browser just
  gave it (the trigger-refocus regression that stole focus from the clicked field is gone),
  while a click on dead space still returns focus to the anchor once the doomed panel is gone.
  Placement measures the panel's layout box (`offsetWidth`/`offsetHeight`) instead of the
  transform-scaled bounding rect, so `TmPopover`'s `scale(0.95)` opening animation can no longer
  bake a 5 % measurement error into the resolved coordinates, and a panel whose anchor scrolled
  fully out of the viewport parks `visibility:hidden` instead of clamping to an edge. Escape
  focus restore now lands on real focusables: `TmNotificationBell`, `TmSplitButton`,
  `TmTagPicker` and `TmMultiColumnComboBox` anchor their panels to the trigger control rather
  than the unfocusable wrapper (Align.End/Start geometry preserved). `TmSplitButton`'s primary
  click closes an open menu, `TmMultiColumnComboBox`'s panel carries `role="dialog"` (the grid
  role belongs to the inner table), and `TmPopover`'s `div[role=button]` trigger is
  keyboard-reachable (`tabindex="0"` + Enter/Space emulation, with the consumer-content keydown
  fence the convention requires).

- **`TmGanttImportDialog`'s file chooser is keyboard-operable.** The upload affordance was a
  `<label for>` styled as a button pointing at a `display:none` `InputFile` — pointer users could
  click it, but a `<label>` can never receive keyboard focus, so Enter/Space had no path to the
  chooser at all. The label is now a real `TmButton` (Secondary, Sm, localized
  `TmGantt_ChooseFile`) with a NATIVE click listener wired by `registerFilePickerTrigger` in the
  collocated `TmGanttImportDialog.razor.js` module, which calls `input.click()` on the hidden
  `InputFile` inside the browser's own click event. The input stays in the DOM visually hidden (sr-only geometry, not
  `display:none`) with `aria-hidden` + `tabindex="-1"` so it never reaches the a11y tree or the
  tab order. bUnit verifies the module invocation and that no `<label>` remains; Playwright tabs
  to the button and opens the chooser with both Enter and Space via `WaitForFileChooserAsync`,
  with a focus screenshot under `__screenshots__/`.

- **Every picker label is now programmatically associated with its control.** All six
  `.tm-picker-label` pickers previously rendered a bare `<label>` with neither `for` nor a wrapped
  control — it named nothing, and assistive tech never announced it. Trigger pickers
  (`TmDatePicker`, `TmDateRangePicker`, `TmDateTimePicker`'s date field) now render
  `<label id for="{triggerId}">`, the trigger carries `aria-labelledby="{labelId} {valueId}"`
  so its announced name is the label followed by the shown value or placeholder, and clicking the
  label natively focuses and opens the trigger. Composite pickers (`TmTimePicker`,
  `TmTimeRangePicker`, `TmDateTimeRangePicker`, `TmDateTimePicker`'s time section) have no single
  labelable element, so their `for` targets the first segment (`TmTimeInput` gained an `InputId`
  parameter for this; `TmDateTimePicker` gained `TriggerId` so a parent label can reach the
  nested trigger) and the input body is a `role="group"` named by `aria-labelledby` — the W3C
  pattern for multi-field controls. `aria-*` entries in `AdditionalAttributes` are now splatted
  onto the trigger (or the named group) instead of the wrapper div; `data-*` and other attributes
  stay on the root, where test ids live. bUnit covers the `for`/`aria-labelledby` wiring for all
  six pickers and the attribute split; axe-core on `/pickers` asserts zero `button-name`
  violations and a real-browser test clicks the label and reads `document.activeElement`.

- **The five rich-text-editor dialogs now share one form-group stylesheet.** The scaffolding class
  `.tm-rte-form-group` was declared bare in both `_link-dialog.css` (space-4 group offset, space-2
  label offset) and `_image-dialog.css` (space-3 / space-1), so the manifest's import order — not
  anyone's intent — silently picked the image values for all five dialogs, and the link dialog's
  declared spacing never painted. The shared rules now live once in the new
  `_rte-dialog-shared.css`, scoped under the `tm-rte-dialog` host class every dialog root carries
  next to its own. The label offset is a single `--tm-space-1` for all five — the value users
  already saw (image's declaration won every tie) and the same label→control gap the design system
  uses in `.tm-form-field` and `.tm-input-wrapper`, so the change renders zero new pixels. The
  group offset keeps the painted status quo as well: the shared default is `--tm-space-3`, while
  the link dialog keeps its deliberate `--tm-space-4` through its own scoped rule, imported after
  the shared sheet so it still wins inside its own dialog. `CssComputedStyleRegressionTests`
  resolves the label margin in both dialogs to the same token and pins the two group offsets;
  the `UnconstrainedClassOwnershipTests` inventory lost its two `tm-rte-form-group`/`label` rows
  because the collision no longer exists.

- **`TmValidatedField` forwards `Required` to the inner input and only paints "valid" after a real
  validation pass.** Two halves of the same parametric bridge (application gap register #10):
  `Required` used to draw only the asterisk on the label and never reached the `TmTextInput`, so
  the `<input>` lacked `aria-required` even though the standalone input has emitted it since
  2.8.24 — it is now forwarded (`required` + `aria-required="true"`). And `IsValid` used to mean
  "non-empty and error-free", so a field the user merely typed into got the green
  `tm-input-valid` frame and check icon without anyone having validated it. It now reports valid
  only once the `EditContext` ran a validation pass (`OnValidationRequested` or
  `OnValidationStateChanged`) with no messages for the field; before that — and always without an
  `EditContext` — the field stays neutral. bUnit covers the `aria-required` forwarding, the
  absent green frame on an unvalidated field, and the icon after `EditContext.Validate()`.

- **`.tm-btn-default` draws its border from `--tm-border-color-control`.** The same defect class
  2.8.17 fixed on `.tm-btn-outline-secondary`: the decorative `--tm-border-color` measured
  1,24:1 light / 1,41:1 dark against `--tm-bg-surface`, under the 3:1 WCAG 1.4.11 asks of a
  control boundary. The control token measures 4,83:1 / 5,71:1 — verified by a new
  `TokenContrastTests` case that reads the declared value out of `_button.css` and measures it in
  both themes (application gap register #11).

- **The declarations 2.8.26 deleted as "dead" are back on the owning classes (`eef9ba93`).** The
  erratum in the 2.8.26 section tells the whole story: five commits — **`9e9e4a54`**,
  **`cfd691d5`**, **`5ab7fe83`**, **`8125ac19`**, **`a013d24c`** — removed rules whose selectors
  were dead but whose declarations still painted pixels through descendant selectors, import
  order and unique properties. What is restored is the *rendered* value: the declarations the
  dead rules had been winning with are moved onto the owning classes in the owning stylesheets,
  so the computed style is again the style 2.8.25 painted — without re-introducing a single dead
  selector. The regression surface is guarded two ways: `CssComputedStyleRegressionTests` resolves the cascade
  through `CssCascade` over `tempo-blazor.bundled.css` for every restored property, and a
  Playwright computed-style test reads the same pixels in a real browser — Chromium and Firefox
  both, because a computed style that only exists in one engine is not a restored style.

- **Every `TmDataTable` sort button announces an accessible name that tracks the sort cycle
  (`ee1ec3df`, reworked `ec990b9c`).** Until now the `<button class="tm-th-sort">` emitted `aria-label` only when the
  column used a `HeaderTemplate` — and that label was a static "Sort ascending", so a column
  whose next press sorts descending announced the wrong action, and an untitled templated header
  got no name at all. The label is now unconditional markup of the form
  "Sort by {header} — {next action}": the header is the column `Title`, then the new
  `TmDataTableColumn.SortLabel` parameter (a consumer's replacement for a caption that is not
  plain text — a templated header with neither is a developer error that throws in DEBUG and
  falls back to a localized "Column {n}" in RELEASE), and the action is the state the NEXT
  activation reaches — ascending / descending / cleared — following the same tri-state cycle the
  click walks. Activation is unchanged: the button still has no `@onclick`, Enter/Space/click all
  take the single native path, and `aria-keyshortcuts="P"` is still advertised exactly when the
  column menu exists. Component tests cover every state and fallback; the E2E pair reads the
  resolved AX-tree name, Enter and Shift+Enter multi-sort, header drag into the group zone and
  the hover/`:focus-visible` paint in Chromium and Firefox — the Shift+Enter run caught and fixed
  a real engine difference, where Firefox ships a button's keyboard-synthesized click with the
  modifiers cleared.

- **`TmGanttImportDialog`'s upload label now emits real button classes (`f72e1fb8`).** The label —
  the only visible upload element in the import dialog, its `InputFile` is `display:none` —
  carried `tm-button` / `tm-button--ghost` / `tm-button--sm`, three class names no stylesheet in
  the library declares, so the upload affordance rendered unstyled. It now emits
  `tm-btn tm-btn-ghost tm-btn-sm`, which `_button.css` actually styles. The fix landed in the
  post-tag window without a changelog line; it is declared here.

- **`TmTimeline`'s content line-height changed from 1.75 to 1.5 in 2.8.26 (`b8413898`).** The
  same scoping fix the D17 items needed — `_activity-timeline.css`'s `.tm-timeline-content` was
  scoped under `.tm-timeline-item` — had an undeclared visual consequence on a *different*
  component: the activity component's relaxed line-height had been silently styling TmTimeline's
  content too, and once the selector stopped matching, TmTimeline's own `--tm-line-height-normal`
  finally painted. The new value is the authored one; the regression is that nobody declared the
  shift at release time — declared now, and the same erratum class as the deletions above:
  a selector shared across components is a shared stylesheet, not a private one.

- **`ToastService` and `TmSearchInput` now run their timers on an injectable `TimeProvider`
  (`aee98c13`).** `ToastService` takes `TimeProvider?` as an optional constructor argument — DI injects a
  registered provider, otherwise it falls back to `TimeProvider.System` — and `TmSearchInput`
  resolves the same service with the same fallback. Both swapped their ad-hoc timers for
  `TimeProvider.CreateTimer` (`ITimer`), so a consumer that registers a fake clock gets fully
  deterministic auto-dismiss and debounce. The change exists for the tests, not for its own
  sake: the auto-dismiss and debounce-cancellation assertions used to read state *before* the
  deadline had provably passed — `PendingAutoDismissCount` reads 0 while a still-armed timer can
  still fire, and `HasPendingDebounce` reads false while a fired callback's renderer hop is still
  queued. Under a FakeTimeProvider the barrier is `Advance(deadline + 1ms)` instead: a timer that
  survives cancellation fires synchronously *inside the test*, so a missing `Dispose` or a
  dropped `ReferenceEquals` guard turns `changeCount` to 2 (or delivers the stale value) before
  the assertion runs — the exact regressions the wall-clock reads could let through.

- **Variant and modifier classes no longer depend on stylesheet order (`c67cd2cc`, Fáze 18.1).**
  The application triage measured modifier rules (`tm-btn-primary`, `tm-btn-sm`,
  `tm-input-with-left-icon`…) tying their base class at (0,1,0) — the manifest's import order,
  not specificity, decided the winner, so a reorder or a late-loaded sheet silently flipped
  rendered declarations. Every measured pair is now a compound selector
  (`.tm-btn.tm-btn-primary` at (0,2,0) beats a bare `.tm-btn` regardless of order; same for the
  input icon/validation modifiers and `TmScrollSpyNav`'s `--active`), with declarations
  byte-identical. The rule "a variant may never tie its base" is pinned by
  `UnconstrainedClassOwnershipTests` over a frozen pair list with mutation Facts, and
  `CssComputedStyleRegressionTests` resolves the shipped bundle through `CssCascade` with
  reversed source order to prove the winner survives a reorder. **Migration note:** hand-written
  markup carrying a bare modifier (`class="tm-btn-primary"`) without its base class no longer
  receives styling — every modifier now requires the base (`tm-btn tm-btn-primary`) on the same
  element.

- **Floating panels can no longer be clipped, displaced, or painted over (Fáze 18.2).** The
  date-picker-inside-a-modal defect was the visible tip: the calendar rendered as an absolutely
  positioned descendant of the modal body, so `overflow-y:auto` clipped it below the fold, the
  modal's `transform: scale(...)` entry made it the panel's containing block (wrong coordinate
  origin), and none of it followed the trigger when the modal body scrolled. The same defect
  class lived in every component that floated markup — each with its own Escape/outside-click/
  reposition code. All of them now render through `TmOverlayPanel` (see **Added** above): the
  panel lives in the browser top layer where no ancestor `overflow`/`transform`/stacking context
  can reach it, the shared engine flips it above the trigger when the space below runs out,
  shifts it inside the viewport margin, and re-anchors it on every scroll — window or inner —
  and resize. Migrated: `TmPopover`, `TmDropdown`, `TmFilterableDropdown`, `TmContextMenu`,
  `TmSplitButton`, `TmDatePicker`, `TmDateRangePicker`, `TmDateTimePicker`, `TmMultiSelect`,
  `TmTagPicker`, `TmEntityPicker`, `TmNotificationBell`, `TmColorPicker`,
  `TmMultiColumnComboBox`, `TmQueryInput`, and `TmDataTable`'s floating surfaces. Behavior that
  was already correct is preserved verbatim — dropdown width matching, `listbox`/`dialog` roles,
  per-component keyboard maps, and `TmColorPicker`'s pending-value-until-apply semantics. The
  pure-CSS hover surfaces (`TmMenu` submenus, `TmTooltip`) and the RTE mention/token completers
  were intentionally not migrated: their trigger/anchoring model differs enough that the move is
  a redesign, not a panel swap. **Declared residual gap:** a `TmMenu` submenu inside an
  overcrowded (vertically scrolling) `TmSidebar` can still be clipped by the scroll container —
  that is the submenu redesign the exclusion records, not a defect this release claims to fix.
  bUnit covers the primitive and every migrated component (9990
  tests), `overlay.test.mjs` covers the placement math DOM-free (16 tests), and
  `OverlayPanelE2ETests` measures the real browser in Chromium *and* Firefox: popover state,
  overflow escape, inner-scroll tracking, the edge flip, painting above a modal, and a date
  picker flipping inside a scrolling modal body and tracking its trigger while it scrolls.

- **Overlay cascade, lifecycle and dismissal hardening (Fáze 20B review — N196–N198,
  N163–N165, N167–N169, N172).** The `_overlay-panel.css` reset silently owned every migrated
  panel: imported last at the same specificity as the consumers' own classes, it painted over
  their backgrounds, borders and padding, and its `--open` `display:block` beat the
  `display:flex` layouts (N196). The reset now lives inside `:where()` — zero specificity, so a
  consumer declaration always wins — while the closed-state `display:none` deliberately stays at
  full specificity to keep fallback panels hidden, and `OverlayPanelComputedStyleRegressionTests`
  resolves the shipped bundle through `CssCascade` to prove it. `TmMultiColumnComboBox`'s
  dropdown root is rendered by `TmOverlayPanel` outside the component's CSS-isolation scope, so
  its scoped rule never matched (N197) — that one rule moved to the new global
  `_multi-column-combobox.css` while the in-scope child rules stayed scoped. Lifecycle races are
  serialized behind a per-instance `SemaphoreSlim`: overlapping `OnAfterRenderAsync` runs can no
  longer both observe the panel closed and double-`open()`, a render resuming after disposal
  cannot import and leak a module, and `DisposeAsync` releases the module in a `finally` even
  when `close()` throws a non-disconnect `JSException` (N198, N172). In `overlay.js`, `open()`
  on an orphaned tracked entry now re-observes the *new* panel element and reinserts the key at
  the top of the stacking order instead of watching a dead node (N164); a vetoed dismissal —
  `NotifyDismissedAsync` answering `false` because a controlled consumer kept `IsOpen` — re-arms
  `entry.dismissed` so the next Escape is not silently swallowed, with the same re-arm on
  callback failure (N167); and the Escape-keyup suppressor that shields `TmModal`/`TmDialog`
  gained a one-second timeout, so a keyup the browser swallows natively can no longer eat the
  next unrelated Escape (N163). `TmEntityPicker` and `TmQueryInput` anchor wrappers carry
  `tabindex="-1"` — programmatically focusable for dismissal focus-restore without entering the
  tab order (N168) — and the dead wrapper-level Escape handlers in `TmPopover`, `TmContextMenu`
  and `TmFilterableDropdown`'s trigger were deleted: `overlay.js` consumes Escape at window
  capture, so those branches could never fire (N169). E2E now covers the no-Popover-API fallback
  (`delete HTMLElement.prototype.showPopover` before page scripts — fixed positioning, the
  transformed-modal containing-block walk, Escape) and the suppressor timeout end to end (N165,
  N163).

- **`TmDataTable`: the sort button's accessible name predicted the wrong action during
  multi-sort (N202).** With two or more active sort descriptors the `aria-label`/`title` still
  answered with the single-column tri-state cycle ("Sort descending" → "Clear sort"), but the
  click actually resets to a single ascending sort — the announcement promised a state the
  activation never reaches. The name now says "Sort ascending" whenever more than one
  descriptor is active; the tri-state cycle is only announced when the column is the sole
  sort. `SortByAsync` itself is unchanged — only the prediction was stale.

- **`TmDataTable`: Shift+Enter on a pin-only header no longer leaks into the next sortable
  column's click (N203).** The keydown that synthesizes a click on a sortable header records
  its Shift state so the keyboard-originated click (which Firefox ships with every modifier
  cleared) can apply multi-sort. That flag used to be armed for ANY header keydown — including
  a pin-only, non-sortable header whose keydown never produces a click — and stayed set until
  an unrelated later click on a different sortable column inherited it, silently turning a
  plain activation into a multi-sort. The flag now arms only on a sortable header's activation
  keys, so only the keydown that will actually produce the consuming click can seed it.

- **`TmGanttImportDialog`: the file chooser no longer crosses a JS-interop boundary inside the
  user gesture (N204).** "Choose file" used to run `input.click()` from an `@onclick` handler
  through a `JS.InvokeAsync` round-trip — fine in WASM, but in Blazor Server the SignalR
  hop severs the user-activation chain, and WebKit ignores `input.click()` issued outside a
  native event handler even with the hop removed. The collocated module now registers a NATIVE
  click listener on the trigger button ahead of the first click
  (`registerFilePickerTrigger`), so `input.click()` runs synchronously inside the browser's own
  click event for pointer, Enter and Space activation alike. Registration is re-attempted on
  EVERY render while the dialog is open — closing the dialog and switching tabs both destroy the
  file area's DOM, and a C# "registered once" flag can never see the fresh element — while the
  `dataset.tmFilePickerRegistered` marker in JS dedupes repeat calls on the same button.

- **Accessibility and component-residue review wave (Fáze 20D review — N147–N153,
  N159, N161–N162, N188–N192).** Picker requiredness is back inside what ARIA 1.2
  permits: `aria-required` left the button triggers (`TmDatePicker`,
  `TmDateTimePicker`) where it was never a permitted attribute — requiredness
  stays on the required-label marker and form validation (N147) — and
  `TmTimePicker` dropped the inert `data-required` marker plus an `aria-disabled`
  that only meant "out of range": its label gained the required marker and every
  segment now announces `aria-invalid` through the new `TmTimeInput.Invalid`
  parameter (N148). `TmTimeRangePicker`'s From/To sections are named
  `role="group"` sub-groups like `TmDateTimeRangePicker`'s (N149), and the docs
  now say plainly that a consumer `aria-label` loses to the component's
  `aria-labelledby` — set `Label` instead (N159). `.tm-btn-secondary`'s
  decorative border measured 1,24:1/1,41:1 and now uses the control-border token
  (N150). `TmValidatedField` publishes `IsValid` only after a completed
  validation pass — the in-flight render can no longer flash green before an
  async validator answers — and re-subscribes when the host swaps the cascaded
  `EditContext` instead of listening to a dead context forever (N151, N152).
  `TmGanttImportDialog` is a real modal: `role="dialog"` + `aria-modal` +
  `aria-labelledby`, a localized close-button name, `role="alert"` import
  errors, a persistent `aria-live` filename region (a conditionally-mounted live
  region drops its first announcement) and document-level Escape-to-close
  (N153). A stale `TmSplitButton` comment now describes the `_toggleRef` anchor
  geometry it actually uses (N161). Keyboard emulation follows the native
  contract — Enter on keydown guarded by `!e.Repeat`, Space on keyup only, with
  a keyup fence so a bubbled Space off a native child cannot double-activate —
  and the dead bubbling Escape branches `overlay.js` could never reach were
  deleted (N162, N169 follow-up). `TmNotionBlockContextMenu` submenu triggers
  are real `<button>`s with `aria-haspopup="menu"`/`aria-expanded`; Escape
  collapses the open submenu and returns focus to its trigger while ArrowLeft is
  the submenu back gesture (N188). `TmNotionNotificationCenter`'s panel migrated
  onto `TmOverlayPanel` (N189). The nested `<main>` landmarks inside
  `TmModelingEditor`, `TmNotionPageHistory`, `TmReportDesigner` and
  `TmPdfTemplateDesigner` became named `<section>` regions, and
  `LandmarkOwnershipTests` sweeps every shipped package fail-closed so a
  component can never emit `<main` again (N190). The Notion floating UI —
  mention menu, inline toolbar, block type switcher — moved off hardcoded
  `9997–9999` z-indexes onto the shared `--tm-z-popover` band (N191). And
  `TmDocumentEditor` pushes runtime track-changes flips — SuggestOnly
  permission, a suggestion provider appearing or leaving, `TrackChangesEnabled`
  toggling — through `setTrackChangesEnabled` instead of leaving the canvas
  engine on its mount-time value while the toolbar reads the new one (N192).
  Three 20C review carry-forwards closed in the same wave: `TmDataTable` emits a
  directional `aria-sort` on the primary sort descriptor only and the recorded
  activation-key Shift state expires after one second on the injected
  `TimeProvider`; a thrown `OnSaveAndLeave` now runs the same failure path a
  `false` result runs (dialog closes, `OnSaveAndLeaveFailed` fires) before the
  exception propagates, and every `TmNavigationGuard` dialog action — not only
  Save — is disabled while a save is in flight; and the documentation parity
  guard's ancestor attribution is verified, closing the hole that let
  `TmNotionDbCellBase`'s seven shared cell parameters be attributed to a page
  that never named them.

- **Second accessibility/component-residue wave (Fáze 20D carry-forwards, closed in 20E).**
  `TmNotionBlockContextMenu`'s submenu triggers now answer the APG **ArrowRight** gesture —
  Enter and click already opened the submenu, Tab walked its items, but the arrow gesture a
  menu-button user reaches for did nothing; it opens the panel and lands focus on its first
  item (`tmNotionEditor.focusFirstMenuItem`), ArrowLeft/Escape still collapse back to the
  trigger (`0916707f`). `TmContextMenu`'s `Trigger` fragment is fenced the way
  `TmPopover.TriggerContent` already was (`d335c307`): a native control inside the trigger
  content used to fire the emulating `div[role=button]`'s toggle on top of its own click —
  the span now carries matching `@onkeydown`/`@onkeyup` barriers (the keyup one matters:
  Space activates on the release). `TmQueryInput`'s overlay dismissal no longer loses an
  asynchronous race (`8f31e911`): `SetOpen(false)` previously flipped only `_isOpen`, so a
  late provider response could reopen the dropdown after dismissal — the path now runs the
  same `CloseDropdown()` every other close uses, bumping the request version, clearing
  suggestions and the loading/error flags. `TmValidatedField` drops `_validationRan` when
  the cascaded `EditContext` instance is swapped (`f2c3fa6b`): the flag had measured a pass
  on the DISCARDED context, and carrying it over painted `tm-input-valid` on a context that
  never validated — N151's defect in mirror image. `TmDocumentCanvasEngineHost` re-asserts
  `trackChanges.enabled` and `reviewDisplayMode` against the freshly mounted engine
  (`64f57f35`): a flip landing while the awaited mount was in flight was silently dropped
  (`_handle` still null) and the engine's `updateOptions()` ignores both flags, so the stale
  mount-time value would have persisted for the life of the mount; both re-asserts are
  idempotent and run once per mount. `TmGanttImportDialog` no longer interpolates raw
  `Exception.Message` into user-facing errors (`aa941a54`) — failures are mapped onto
  localized categories (auth, network, file, generic, missing-xlsx-package). And the
  keyboard-activation convention reached its remaining outliers (`29dde88e`): the
  `TmColorPicker` and `TmFilterableDropdown` triggers, `TmDataTable` data rows (flat,
  virtualized and grouped-leaf alike), `TmMultiViewList` rows/cards/items, and
  `TmSankeyChart`'s focusable nodes/links all fire Enter on **keydown** guarded by
  `!e.Repeat` and Space on **keyup** only — the held-Enter re-toggle and the keydown-timed
  Space are gone; every fenced native child (dropdown clear button, row checkboxes,
  expander and edit-start buttons, view-list selection cells) gained the matching
  `@onkeyup:stopPropagation` barrier the convention now requires, because a keydown-only
  fence still leaks the Space release into the container's activation. The same wave swept
  the remaining floating surfaces onto the shared z-token bands (`cd627154` — the
  Notion `UI/*.razor.css` off the `9997–9999` literals and every other hardcoded literal
  the sweep found, and `LandmarkOwnershipTests` now asserts the element population it
  audited, not just "no `<main>`") and deduplicated `overlay.js`'s focus-restore
  verdict into one `focusWentNowhere` helper (`c56a04f0`) so the two copy-pasted
  four-node walks can never diverge on what counts as "focus escaped".

### Changed — token defaults

- **`--tm-color-danger` moves one step darker in the light theme** (`#ef4444` → `#dc2626`,
  hover `#dc2626` → `#b91c1c`). Tempo paints *white* ink on this fill (`.tm-btn-danger`,
  `.tm-badge-danger.tm-badge-filled`) and red-500 measured **3,76:1** — under the 4,5:1 WCAG
  asks of text. The new steps measure **4,83:1** and **6,47:1**. The dark theme is deliberately
  untouched: it keeps the light #f87171 under `--tm-text-inverse` ink (~7,5:1), which a
  darkening would have sunk. `--tm-shadow-focus-danger` derives from the token now
  (`rgb(from var(--tm-color-danger) r g b / 0.3)`), so it follows the new value — and a
  consumer's rebrand — instead of freezing the old one.
  (`TokenContrastTests` measures the real pair in each theme.)

- **Filled success/warning/info badges fill from new `--tm-color-*-strong` tokens**
  (`#15803d` / `#b45309` / `#0e7490` — the 700-steps). White ink on the semantic accents
  measured **2,28 / 2,15 / 2,43:1** in light — all under AA; on the strong steps it measures
  **5,02 / 5,02 / 5,36:1**. These are *separate* tokens rather than darker semantic ones
  because `--tm-color-warning` also fills `.tm-btn-warning` under *dark* text, where the same
  darkening would drop that pair *below* AA — the application's own analysis caught that
  blast radius. Dark re-points the strong tokens to the brightened accents, where the badge
  wears `--tm-text-inverse` ink. If your theme relied on `--tm-color-success` reaching the
  filled badge, repoint `--tm-color-success-strong` alongside it.
  (`FilledBadges_KeepAaContrast_InBothThemes` reads the rules out of `_badge.css`.)

- **The alpha-composited primary tokens derive from the scale now** instead of shipping Tempo
  blue literals: `--tm-shadow-focus` is `rgb(from var(--tm-color-primary-500) r g b / 0.4)`
  (dark: primary-400 at 0.5), dark `--tm-color-primary-subtle` is primary-500 at 0.15, dark
  `--tm-color-primary-soft-border` is primary-400 at 0.3. Default rendering is byte-identical
  to the literals they replace; what changed is that repointing `--tm-color-primary-500` (or
  `-400` in dark) repaints the ring — proven at the graph level in tests and in a real browser
  by `FocusTokenDerivationE2ETests`. `--tm-color-primary-500-rgb` stays a manual triplet —
  CSS cannot extract channels from a colour — with a new guard pinning that it names the
  channels of the step it shadows in each theme.

### Release gate

- **NuGet publication now requires a human gate (`8b6a8545`, N206,
  DEC-TEMPO-NUGET-ENVIRONMENT).** Both publish workflows — `publish-nuget.yml` and
  `publish-nuget-org.yml` — run their `publish` jobs inside the GitHub Environment
  `nuget-org`, so `dotnet nuget push` cannot execute without an environment reviewer's
  approval. `PublishEnvironmentGateTests` parses both workflow YAMLs fail-closed: a publish
  job without the environment, or an environment name drifted off `nuget-org`, fails the
  suite rather than the release.

- **Release evidence is a machine-checked artifact, not a promise (`3772ff70`, N207,
  DEC-TEMPO-RELEASE-GATE).** `eng/release-evidence/e2e-full-run.json` records the full-lane
  run that justifies a release: the commit it ran against, who verified it and when, the
  run name, the passed/failed/skipped totals, the serial-residual counts, the wall clock
  and the artifact path. `eng/verify-release-evidence.sh`
  validates the schema and applies the recorded staleness scope
  (DEC-TEMPO-RELEASE-EVIDENCE-SCOPE, owner decision F14): the evidence is stale when ANY
  commit after `evidence.commit` touched a path outside the allowed list — `*.md`, `docs/`,
  `eng/release-evidence/`, `.github/` — so any `src/` or `tests/` change, bUnit included,
  forces a re-measurement. The publish workflow runs the verifier before the
  pack step, so a release on untested code fails closed in CI, not at NuGet.
  `ReleaseEvidenceTests` proves the JSON parses, the verifier accepts the committed file and
  rejects tampered staleness windows.

- **`fetch-depth: 0` is enforced where it is load-bearing (`e029dd31`, N208).** The
  changelog-commit guard and the version/tag guard both need history; a shallow clone turns
  them vacuous. `TEMPO_REQUIRE_FULL_CLONE=1` (set by the workflows) makes the
  `FullCloneFactAttribute` fail on a shallow checkout instead of skipping — the attribute's
  skip path stays for developer machines where a full clone is optional, but CI can no
  longer silently downgrade a git-dependent guard into a skip.

### Tests & docs

- **`107ebfda`** (freshness guard reworked **`ec990b9c`**) — `CssCascade` stops flattening stylesheets. The regression model now parses the
  sheet structurally instead of matching flat rules: `@media` conditions are evaluated against a
  `MediaContext` (a width-dependent condition with no supplied width is undecidable, not false),
  `@layer` order participates in precedence with unlayered rules beating layered ones, `@import`
  resolves through an injected resolver, statement-form at-rules are recognised before the next
  block, CSS nesting and `&` expand as a selector cross-product, and anything the model cannot
  express — an unsupported combinator, an unknown media feature — is reported fail-closed as
  `Unmodelled` rather than silently treated as a non-match. The same commit wires
  `GeneratedDocumentationMatchesCommittedTests`: it runs the real JSON generator in-process into
  a temporary directory, compares the emitted file set and byte content against the committed
  `tempo-blazor*.json` (line-ending normalisation only, first differing line reported), and every
  configured input path of the generator now throws on missing or malformed input instead of
  falling back to empty.

- **Every commit id this changelog prints inside backticks must resolve.** The new
  `ChangelogReferencesExistingCommitsTests` extracts each `` `hex` `` / **`hex`** token from this
  file and runs `git cat-file -e <id>^{commit}` against it — a reference that points at nothing
  is exactly the dishonesty this release was cut to remove, so the file can no longer print one.
  Parenthesised ids are untouched: the 2.8.26 convention paragraph declares them development-line
  record ids, not commit claims, and several correctly resolve to no commit.

- **That guard is no longer blind in CI, and no longer red where it cannot see.** Both publish
  workflows' checkouts now fetch `fetch-depth: 0` — the default depth 1 carried no commit beyond
  HEAD, so `git cat-file -e` read exit 128 on every id this file cites and turned the
  `build-and-test` gate red over the clone, not the changelog. The same fetch carries every tag,
  which also makes `AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked` live on
  branch pushes rather than vacuous. In a shallow clone, a tree without `.git`, or on a machine
  without `git`, the test reports a named skip (`FullCloneFactAttribute`) instead of a red about
  the clone; on a full clone a 128 is still a failure, never a skip.

- **`a85cbd1e`** — production code changed for the tests' sake, declared: `ToastService` exposes
  `internal PendingAutoDismissCount` (and serialises Show/Remove/Clear through a lock) so an
  armed or un-cancelled auto-dismiss timer is visible the moment the call returns;
  `TmSearchInput._debounce` is now `volatile` behind `internal HasPendingDebounce`, so "the
  pending debounce must not deliver" waits until the timer is actually disarmed; and `TmMap`
  counts scheduled debounce→load chains as `internal PendingDataRequests`, so "no further
  provider call" waits until every armed, cancelled or in-flight chain has settled. Each replaced
  a fixed-margin wall-clock read a loaded machine could outrun in both directions.

- **`6b953cda`** — the bUnit default wait budget moved assembly-wide from 1 s to 10 s: the waits
  were correct and the machine was slow, so the margin moved rather than the assertions. Whether
  the wider budget retires the flake class is the measurement step 19.2 exists to report.

- **`b2aedb6e`** — the sweep treats a dirty starting tree as a failed run rather than reclassifying
  the outcome DISABLED.
- **`f3d3c136`** — the wwwroot provenance denominator subtracts csproj `Pack="false"` globs, so the
  count it divides by is the population the pack actually sees.
- **`4f0afe7a`**, **`d6eb7a55`** — documentation: the descendant-selector scope limit and the known
  RTE label residual are declared; the pack-script comment names the renamed
  `MarkupClassCoverageTests`.

- **E2E host lifecycle and assertion honesty (Fáze 20E review — N193, N209–N211).**
  `PlaywrightTestBase` can no longer resurrect a dead host silently or pass a test against a
  503 (`29f89b91`): demo hosts are probed on `/health` through a pooled `HttpClient` instead
  of a per-test socket, a dead host is restarted once with every restart appended to
  `host-restarts.jsonl` (the lane report surfaces the count — a host that needed rescuing is
  a finding, not a detail), and the boot wait is stateful rather than a fixed sleep.
  `InteractiveAutoTests` now proves hydration instead of markup (`f67e292d`): a
  server-prerendered page that never wires its interactive circuit renders the same DOM, so
  the tests assert the behaviour only a live circuit produces. And
  `DocumentEditorCanvasUxFixE2ETests` retries the canvas-context evaluation only on the first
  probe where a lost context is legitimate, with a navigation counter proving the retry did
  not smuggle in a reload (`6ec8e6f2`).

- **E2E release-gate filters see every spelling, bounded (`6c424d4c`, N184, N186, N212).**
  The filters that decide which tests belong to a release-gate lane previously matched one
  casing/spelling each, so a renamed category silently shrank the lane; the regexes now
  cover every observed spelling and an upper bound on matched tests fails the guard rather
  than letting a runaway pattern pass as "everything ran".

- **Test SQL databases can no longer wipe foreign data (`5714f0ae`, N213).** When the
  fixtures run in external mode (a developer-provided server instead of a Testcontainers
  instance) they create per-run isolated database names and the cleanup path only ever drops
  a database it created — an external server's pre-existing data is never in the drop set.

- **Fixture and sweep hardening (Fáze 20E review — N214, N174–N179).** The heap-leak E2E
  fails hard and measures the real heap (`673ad040`): `Assert.IsTrue` instead of a soft
  warning, a scalar `evaluate` so the returned number is the heap figure itself, Chromium
  launched with `--enable-precise-memory-info` plus a warm-up pass, so the threshold compares
  settled memory rather than GC timing noise. The bUnit wait-budget env var rejects
  `NaN`/`Infinity` as documented (`bec86fde`, N174). The MsSql fixtures stop leaking their
  connection string, pool their admin connections correctly and die honestly
  (`b541e252`): `ToString()` redacts credentials, the `ClearPool` admin connection lives
  inside a `using`, the container image is digest-pinned, and a fixture whose init fails is
  dropped from the pool instead of handed to the next test (N175, N176, N178, N179).

- **Named limits, counted populations (Fáze 20E review — N180–N183, N141, N142, N170,
  N146, N158, N270, F13).** `008cdae0` documents the limits the suite already accepted —
  the xUnit2 bridge surface, the identity seeds, the two-lane cost model — and gives the
  satellite test project its own `TestAssemblyInit` so the wait-budget env var is honoured
  there too. The theme-token sweep's thresholds are named constants with the skipped-selector
  count reported in the assertion message, so a shrink shows up as a number, not a silent
  pass (`735f09c5`). Orphaned regression screenshots whose owning test no longer exists were
  deleted, with the keep-or-delete convention and the reduced-motion limit documented
  (`40b52101`). And no source file may exceed 1200 lines: `29172da2` freezes the list of
  47 legacy offenders so the ceiling ratchets — a 48th file over the line fails the suite.

## 2.8.26 - 2026-09-13

The promised content of this release is EXACTLY the D17 register items (all four groups) plus D2 and
D3 — nothing more, nothing less. The number is 2.8.26 rather than 2.8.25 because 2.8.25 shipped on
2026-09-12 with a different subject (the Enter/Space emulation removal), and a published number is
spent: `AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked` refuses to re-announce a
tag that names another commit, `AnnouncedVersion_OnTheFeed_CarriesWhatThisTreeBuilds` refuses to call
another tree's artefact this tree's release, and the cure both guards prescribe is the next free
number.

Convention for the parenthesised ids throughout this section: they name the original
development-line record each item was tracked under (the D17 register entries and their siblings),
not a commit of this repository — the landed commits are listed by `git log`, most of them
carrying the record id in the subject line.

### Erratum (corrected in 2.9.0)

Five commits of this release deleted declarations described as "dead" duplicates of an owning
class, and the description was wrong: the rules were dead as *whole selectors* — no element
carried the bare class — but their declarations still reached rendered pixels through
**descendant selectors** (`#page .tm-modal-header` …), **import order** between stylesheets, and
**unique declarations** the owning rule never wrote. The five commits are **`9e9e4a54`**
(`.tm-modal-header`), **`cfd691d5`** (`.tm-modal-close`), **`5ab7fe83`** (`.tm-modal` and its
mobile margin), **`8125ac19`** (`.tm-filter-chip button`) and **`a013d24c`**
(the mention-dropdown rules). The measured consequence in the shipped 2.8.26 bundle: the modal
header lost its `justify-content: space-between` and vertical padding, the modal-close and
filter-chip-remove buttons rendered at different padding and font size than 2.8.25 painted, the
mention dropdown lost its vertical padding, and a `.tm-modal` below 768 px lost the margin a
stray `@media` rule in `_dashboard.css` had been the only declaration of. **`eef9ba93`** in 2.9.0
restores the effective values on the owning classes in the owning stylesheets — the dead
selectors are not re-introduced — and the release adds a computed-style regression suite plus a
descendant-selector sweep so the same deletion cannot pass review a second time.

Visual changes not declared at release time — see 2.9.0: beyond the deletions above, this release
also silently changed `TmTimeline`'s content line-height from 1.75 to 1.5 (**`b8413898`** scoped
`_activity-timeline.css`'s `.tm-timeline-content` under `.tm-timeline-item`, which stopped the
activity component's relaxed value from painting TmTimeline and let TmTimeline's own
`--tm-line-height-normal` through).

### Fixed (D17 — the CSS contract items)

- **`.tm-stat-subvalue` could not be coloured by `SubValueColor` (ed256446).** The subvalue painted
  `var(--tm-text-secondary)` unconditionally, so the documented parameter emitted a class no
  consumer rule could out-rank. The declaration is now
  `color: var(--tm-stat-subvalue-color, var(--tm-text-secondary))` and the component emits
  `style="--tm-stat-subvalue-color: …"` when the parameter is set. Guarded by a bUnit test that
  resolves the cascade through `CssCascade` over `tempo-blazor.bundled.css`.

- **`.tm-btn` carried no size of its own (2bed637e).** Height and horizontal padding lived only on
  the `tm-btn-{xs,sm,md,lg}` modifiers, so any button missing the modifier — including markup that
  sets `.tm-btn` directly — rendered at content height. The base `.tm-btn` now carries the medium
  box outright: `height: var(--tm-input-height-md)` and `padding: 0 var(--tm-space-4)`. It is
  `height`, not `min-height`, on purpose — a `min-height` would clamp `.tm-btn-xs`/`.tm-btn-sm` up
  to the medium size without editing a byte of them, while `height` stays overrideable by source
  order; the four size modifiers are unchanged.

- **The dark theme had no `--tm-text-placeholder` (910c84fe).** `tokens.css` defines it as an alias
  of `--tm-text-tertiary`, but `tokens-dark.css` never overrode it, so dark-mode placeholders
  inherited whatever the light token resolved to. The dark token is now declared with a measured
  ≥ 4.5:1 contrast against `--tm-bg-surface`, and the pair is pinned by a fail-closed text×surface
  contrast test.

- **The class-contract sweeps were blind to two whole packages and to `@media` (a3169f68,
  4fefa6de).** `OrphanClassCssContractTests` hand-listed eleven classes; it is replaced by
  `MarkupClassCoverageTests`, which derives the population from the markup itself. The sweeps now
  cover Signing and NotionEditor stylesheets, and `CssCascade` models `@media` blocks instead of
  flattening them.

### Fixed (D2 — the sortable table header)

- **`TmDataTable` sortable headers are real `<button>` elements (4cbb713b).** The `<th>` used to own
  `tabindex`, `@onkeydown` and the click handler, which made Space scroll the page instead of
  sorting and left the sort affordance unreachable to anything that queries for activatable
  elements. Sortable columns now render `<button type="button" class="tm-th-sort">` inside the
  `<th>`; Enter and Space both sort, the header template barrier still shields nested controls,
  `aria-sort` is emitted only on sortable headers, and the pin button stays out of the tab order.

### Fixed (D3 — class ownership)

- **All fifteen unconstrained class-ownership pairs are resolved (7833ae30).** Every pair in
  `UnconstrainedClassOwnershipTests.RecordedCollisions` — the six `.tm-modal*` duplicates between
  `_dashboard.css` and `_modal.css`, `.tm-filter-chip`, `.tm-form-field`, the four `.tm-rte-*`
  classes and the three `.tm-timeline-*` classes — got its own fix and its own commit, and the
  frozen list is empty.

### Release gate

- **Provenance now asks about every package this repository publishes (67883259, 9480b1fd).** The
  feed guard surveyed only `Tempo.Blazor`; it now iterates all 26 ids of `eng/nuget-packages.txt`,
  registers `lib/**` as the non-reproducible package content it cannot compare, decides each
  package's reachability at discovery time, and confirms the announced version's exact `.nupkg`
  URL with a HEAD request so a network outage cannot dress itself as a version check.

- **Seven timing-based waits in the release gate are state-based (b93a1bd1, 2f46eed2; landed
  a85cbd1e).** Toast timers, search-input debounce, map clustering callbacks, Redis polling, two
  cache TTL waits and the image inspector's render polling each name their barrier and wait on the
  state it produces. Three further flake fixes landed beyond the seven: the mini-toolbar bold
  assert now waits for the dispatcher hop (f7e5ed48), the text-color toolbar test's pull stub
  agrees with the pushed colour (6a4e3911), and `TestAssemblyInit` raises bUnit's
  `DefaultWaitTimeout` from 1 s to 10 s assembly-wide (6b953cda) — the documented knob for
  contended or slower hardware; the barriers themselves remain state-based, only the patience
  grew.

- **Three stale claims are corrected, and the toolbar guard now reads `COMPONENTS.md`
  (ef87ac57).** 2.8.24's tail still described publication as the owner's pending manual step while
  the package already sat on the feed; both 2.8.16 sections called 2.8.15 "the published package"
  though the artefact was packed and audited but never pushed; and `COMPONENTS.md` claimed
  `tm-form-action-bar` keeps `role="toolbar"` where the component renders `role="group"`.
  `ToolbarRoleGuardTests` now scans `COMPONENTS.md` too, so restoring the stale text goes red.

- **Baseline PNGs moved out of the repository (D4).** Generators under `TM_WRITE_BASELINES=1` write
  to `artifacts/baseline/`; the index holds none; `BaselineWriteSweep` fails closed if a baseline
  PNG is ever staged under `__baseline__` again.

- **Component JSON documentation has a freshness guard.** `ComponentDocumentationFreshnessTests`
  reflects over every `Tm*.razor` carrying `[Parameter]`, requires a matching
  `JsonDocumentation` overlay entry, and compares parameter sets on both layers — overlays may not
  name a parameter the component lacks, and the committed `tempo-blazor*.json` bundles must cover
  every settable parameter and name none that do not exist. Previously undocumented components are
  a frozen, itemized set (currently empty). Writing the guard exposed two generator bugs that had
  silently shaped the shipped docs: consecutive `[Parameter]` declaration lines were absorbed as
  continuation attributes so a run emitted only its last parameter, and the `{name}*.cs`
  code-behind glob folded neighbouring components' parameters into `TmGantt`'s entry.

### Expected post-publication shape

`[Provenance] 2.8.26 -> verified` is the line the feed guard will print on the first gate run AFTER
the owner's manual `dotnet nuget push`. Until that run the feed answer for 2.8.26 is `unpublished`,
which is the legal pre-publication state — recorded here as a pending item, not as a measurement.

## 2.8.25 - 2026-09-12

Every Enter and Space on a native activatable element ran its action twice — once from the browser's
own sequence (keydown→click for Enter, keyup→click for Space) and once from the component's
`@onkeydown` handler emulating the same click. This release removes the emulation wherever the
element activates natively — 43 component files across five packages — and where a non-native
container legitimately keeps a keyboard contract, the handler now lives on the element that owns it,
fenced off from native children by a propagation barrier. The removal also moves Space activation
from keydown to keyup, so read the consumer notes at the bottom before upgrading a test suite.

### Fixed

- **One press, one activation.** `TmButton`, the `TmAccordionItem` header, `TmToggle`,
  `TmColorPalette` swatches, `TmModelingModelTree` nodes, and the items of
  `TmDocumentCommandPalette`, `TmDocumentToolbarOverflowMenu`, `TmDocumentAutocompleteMenu` and
  `TmNotionBlockTypeSwitcher` render native `<button>`/`<input>` markup and *additionally* invoked
  the click action from `keydown`. Enter therefore fired the action twice per press, and Space fired
  once on keydown (the emulation) and again on keyup (the native click). In containers that restore
  focus to the trigger on close, the second invocation landed on the re-focused trigger and
  re-opened the panel the key had just closed. The handlers are gone; the browser's sequence is now
  the only one. Emulation on genuinely non-native elements — the `TmContextMenu` trigger, the
  `TmMultiViewList` rows/cards, the command-palette search input — is required and stays.

- **Container Enter handlers no longer act on top of a focused child's own click.** A root-level
  `@onkeydown` that treats Enter as confirm receives every bubbled keydown — including the one whose
  native click is about to do something else, so a prev/next/sort/cancel button ran its own action
  AND the container's. The pattern repeated across `TmDocumentFindPanel`,
  `TmSpreadsheetFindReplaceDialog`, `TmSpreadsheetFilterDropdown` (Enter→ApplyValues),
  `TmNotionPageSearch`, `TmNotionLabelEditor`, `TmCommandPalette`, `TmNotionStatusPicker`, the three
  Notion comment panels (page/block/text) and three spreadsheet dialogs (FormatCells, NamedRangeEdit,
  Hyperlink). Navigation keys moved onto the text inputs that own them; the inputs that lost the
  bubbled route to Escape got a local one; and in the comment panels the action buttons, reaction
  groups and edit/reply wraps are isolated from the entry's Enter→reply handler.

- **Native children nested in a keyboard-handling container are fenced off.**
  `@onkeydown:stopPropagation` barriers now sit on the clear and chip-remove buttons inside
  `TmMultiSelect`'s trigger and on the clear button inside `TmFilterableDropdown`'s trigger (Space on
  them used to toggle the dropdown on top of the removal), on the
  selection checkboxes and row-expander button of `TmDataTable` and `TmMultiViewList`, and on the
  toolbar/breadcrumb/sidebar/overlay chrome of `TmFileManager` and `TmDocumentManager`, where a
  bubbled Enter could reach the grid's open/delete handler.

- **`TmDataTable` grouped leaf rows are reachable by keyboard, and their checkbox no longer fires
  the row click.** `RenderGroupRows` emitted leaf `<tr>`s with `onclick` but no `tabindex` or
  `onkeydown`, and the grouped selection checkbox carried no barrier — clicking it toggled selection
  AND fired `OnRowClick`. The rows now carry the ungrouped contract — `tabindex="0"`, Enter/Space →
  `OnRowClick` — and the checkbox stops keydown and click propagation through
  `AddEventStopPropagationAttribute`, the same frames the Razor compiler emits for
  `@on…:stopPropagation`, because a plain attribute name inside a `RenderTreeBuilder` is not picked
  up as a barrier.

- **Closing a popup no longer drops focus on `<body>`.** `TmFilterableDropdown` and `TmMultiSelect`
  focus the filter input on open, so every close destroyed the focused element — a WCAG 2.4.3
  focus-order violation. The `@ref`'d trigger now receives `FocusAsync(preventScroll: true)` on
  every close path where focus could have been inside the popup — Escape, item select, confirm,
  toggle-close — the pattern `TmColorPicker` already used. The reference `TmMultiSelect` captured
  for that input is also actually focused on open; it was captured and never used.

- **`TmMultiSelect` with `AllowFiltering=false` can now be driven from the keyboard.** With no
  filter input there was no element to carry Enter, so the options were mouse-only. In that
  configuration the combobox keeps DOM focus and now owns the navigation itself: ArrowUp/ArrowDown
  move the highlight, Enter/Space toggle it, and `aria-activedescendant` on the trigger — or on the
  filter input when filtering is on — names the `{Id}-opt-{i}` option.

### Added

- **`TmFilterableDropdown.AriaLabel`, a keyboard-operable trigger, and listbox semantics.** The
  trigger is a non-native focusable (`role="combobox"`), which activates only when the library says
  so: it now handles Enter, Space and ArrowDown to open. Items carry `role="option"` with
  `aria-selected` reflecting the selected value. `AriaLabel` gives the combobox an accessible-name
  override for the cases where the visible text does not describe the field.

### Notes for consumers — test-suite breaking surface

- **Space activation moved from `keydown` to `keyup`** — the native timing the emulation had been
  shadowing. A test that dispatches `KeyDown(" ")` alone and asserts activation must now dispatch
  the real sequence (`keydown` → `keyup` → `click`, or simply `Click`); under the old code that test
  was asserting the defect's extra invocation. Real browsers always produced both halves, so only
  simulations see a change. The Enter shape is the same one event earlier: a `KeyDown("Enter")`-only
  bUnit assertion was exercising the *removed emulation*, not the native path — real browsers click
  on `keydown`, and bUnit never produces that click at all (the caveat the coverage bullet below
  spells out), so the emulation was the only thing a lone `keydown` could have activated.
- **Keyboard callbacks consumers attached still fire — they no longer activate.** A consumer's
  `@onkeydown` on a `TmButton` or a menu item still observes the event; the component no longer
  calls the click action from it. Code that dispatched `keydown` alone to *reach* an action is the
  breaking surface of this release.
- **Newly fenced children also cut off ancestor keydown handlers.** A consumer's own `@onkeydown`
  placed on a wrapper ABOVE the barriered elements — the `TmMultiSelect` clear and chip-remove
  buttons, the `TmFilterableDropdown` clear button, `TmDataTable`/`TmMultiViewList` selection
  checkboxes and row expanders,
  `TmFileManager`/`TmDocumentManager` chrome — no longer observes keydowns that originate on those
  children: the `@onkeydown:stopPropagation` barrier stops the bubble before it reaches the wrapper.
  That is the intended effect — the ancestor handler was precisely the collision being fixed — but a
  wrapper handler that counted on seeing those keys has to move onto the child or below the barrier.
- **Markup assertions observe new rendered attributes.** bUnit/markup tests that assert exact
  attributes will see: `tabindex="0"` on grouped `TmDataTable` leaf rows — which also changes the
  production tab order, since those rows are now in the tab sequence — `role="option"` with
  `aria-selected` on `TmFilterableDropdown` items alongside its trigger's existing
  `role="combobox"`/`aria-haspopup="listbox"`/`aria-expanded` and new `aria-label` when `AriaLabel`
  is set, and `{Id}-opt-{i}` option ids plus `aria-activedescendant` on `TmMultiSelect` (on the
  trigger when `AllowFiltering=false`, on the filter input when filtering).
- **Focus now returns to the trigger when a `TmFilterableDropdown`/`TmMultiSelect` popup closes.**
  Tests asserting `document.activeElement` after Escape, or counting `FocusAsync` interop calls (in
  bUnit's Loose mode the call is observable as `Blazor._internal.domWrapper.focus`), observe the
  change.
- The regression coverage — 90 new cases across 30 test files — dispatches the real browser order,
  `keydown` → `click` for Enter and `keydown` → `keyup` → `click`/`change` for Space, and asserts
  exactly-once activation. That is the only order in which the removed emulation can be told apart
  from the fix, because bUnit does not synthesize the native keyboard→click sequence.

**This number was raised because the content changed** (`DEC-TEMPO-RELEASE-GATE` point 10), and it
is a patch number because the change is a defect correction plus one additive parameter — the same
shape 2.8.13 shipped under, which added six parameters and stayed a patch. Publication of 2.8.25
remains the repository owner's manual step (`DEC-TEMPO-287-NUGET-DELIVERY`), and the order is the
2.1.0 one: **merge to `main` first, tag `v2.8.25` at the commit the packages were packed from, then
pack and push** — `VERSION=2.8.25 eng/pack-nuget-packages.sh`, then
`dotnet nuget push packages/*.2.8.25.nupkg`. The `commit` stamp inside each nuspec names that commit,
so it has to be one consumers can fetch: an artefact stamped with a commit that never lands on `main`
is a label pointing nowhere.
**The double-activation defect is live on the feed under 2.8.24.**

## 2.8.24 - 2026-08-27

The barrier 2.8.23 put around a consumer's `HeaderTemplate` cost the next keystroke. This release
fixes that and rewrites the release gate from an absence check to a provenance check.

### Fixed

- **After a key was intercepted in a `HeaderTemplate`, the user's next genuine press on the header
  did nothing.** 2.8.23 stopped the bubbling with a flag: the barrier set it, the header's own
  handler cleared it. But the header's handler is exactly what an intercepted key never reaches, so
  the flag survived the event that set it and was spent clearing on the NEXT press. Type a filter in
  the header, move to the header, press `P` or `Enter` — nothing; press again — it works. Measured:
  `[tm-col-sortable]` after the first press, `[tm-col-sortable, tm-col-pinned, tm-col-pinned-left]`
  after the second. The comment above that code claimed the opposite property.

  The barrier is now `@onkeydown:stopPropagation="true"` — stateless, so it has no next event to
  spend. **The reason 2.8.23 gave for not using it was wrong and was itself a measurement error:**
  the string `stopPropagation` does not occur in `bunit.dll` (0 hits, ascii and utf-16, all four
  TFMs) and that was read as "bUnit does not implement the flags". The name is composed at runtime,
  so the grep measured the wrong thing. A controlled experiment over three trees differing only in
  one attribute name settles it — `__internal_stopPropagation_onkeydown` stops the bubble,
  `__internal_bogus_onkeydown` and `data-nothing` do not. Five tests now cover the barrier: two that
  the shortcut does not fire from inside the template, two that the next key on the header still
  works, one that the header itself still answers while a template is present.

- **The release gate asked the wrong question, and had begun reporting failure over releases that
  arrived correctly.** See `DEC-TEMPO-RELEASE-GATE` points 6–10, which carry the original wording of
  the test verbatim along with why it no longer holds. The old guard refused a version number that
  was already on the feed. That is a PROXY for the harm — a consumer resolving the announced number
  to bytes other than the ones this tree builds — and the proxy came apart from the harm: 2.8.22 and
  2.8.23 are both published, both are byte-identical to this repository, and the guard was red over
  both. A gate that goes red over a successful delivery teaches people to bump past it.

  The question is now a whitelist: **what lies under the announced number on the feed is what this
  tree builds.** It reports three states rather than two — `unpublished` (absent; legal, green, and
  deliberately not called verified, because nothing has been delivered), `verified` (present and
  matching, which is strictly stronger evidence than the old guard could produce), and red (present
  and different). A feed that does not answer is a fourth, `unmeasured:feed-unreachable`, counted as
  its own state and folded into neither.

  It does not compare the `.nupkg` as a file — a zip carries timestamps, entry order and a signature
  block, so archive equality is not a well-defined question. It compares content items, over a
  denominator derived from the source rather than chosen: every file under
  `src/Tempo.Blazor/wwwroot`, which the SDK packs to `staticwebassets/<relative path>`. Measured on
  2.8.23: **168 files in the tree, 168 in the package, 168 byte-identical.** The count carries a
  floor so the sweep cannot shrink to whichever file somebody once cared about, and package entries
  with no counterpart in the tree — the generated scoped-CSS bundle and four colocated `.razor.js`,
  five on 2.8.23 — are reported rather than dropped.

  When bytes differ the release is refused, and the message forces the author to name which of two
  mechanisms it is, because the cures differ: the repository moved after the publish (cure: bump), or
  the published artefact did not come from the tagged tree (a supply-chain finding, which must not be
  disposed of by bumping — the push is a manual step and this is the only instrument that can say
  anything about it).

- **A guard that was green whatever the CSS said.** `TheRestingStatesOf2821_WereIndistinguishable`
  composited the same token expression twice and compared the results, so it passed independently of
  the code — a record wearing the shape of a pledge. It now runs the real cascade resolver over the
  2.8.21 rules quoted verbatim from `git show v2.8.21:…/_data-table.css` and asks it who wins; if the
  resolver stopped honouring specificity, it goes red. A second case measures the reading the
  register mistook for the sorted state — the same icon on a header under the pointer, where
  `.tm-col-sortable:hover` (0,2,0) does beat the base rule — reproducing 1,3158:1 light and 1,2156:1
  dark from the declarations rather than from a note.

**This number was raised because the content changed, not because a gate was red**
(`DEC-TEMPO-RELEASE-GATE` point 10). 2.8.23 stays published and is green under provenance.
**2.8.24 is on nuget.org** — the publication was performed by the repository owner as the named
manual step (`DEC-TEMPO-287-NUGET-DELIVERY`), so the keystroke defect above is fixed on the feed
under 2.8.24. (Corrected in 2.8.26: this paragraph previously said publication was still pending,
which was true only when it was written.)

## 2.8.23 - 2026-08-26

Three things 2.8.22 got wrong about itself, and one regression it introduced. **2.8.22 is on
nuget.org** (measured: the flat container lists 107 versions ending 2.8.22) and its
`tempo-blazor.bundled.css` matches this repository byte for byte, so the fixes it carried ARE
delivered — this release is what has to follow them.

### Fixed

- **A single-character shortcut fired from inside a consumer's `HeaderTemplate`.** 2.8.22 gave the
  column header `P` for the pin, and `keydown` bubbles: a filter box a consumer puts in the header
  passed every keystroke up to the header's handler, so typing "prague" pinned and unpinned the
  column three times, and Enter re-sorted the table under the user. WCAG 2.1.4 (level A) permits a
  single-character shortcut only when it can be turned off, remapped, or is active solely while the
  component has focus — and focus on a DESCENDANT is not the third exception. `Enter` had the same
  shape and is older than the pin. The template now renders inside a `display: contents` barrier that
  stops **keydown** only: clicking a templated header has always sorted and still does.

- **The `.tm-btn` sweep's denominator was right for its question and wrong for the sentence written
  about it.** The changelog of 2.8.22 said the sweep covered the whole `components/` directory "so
  the next one fails wherever it is written". It sweeps the directory for `.tm-btn*` and for nothing
  else, and the number that sentence was written from was 61 while the directory holds **139**.
  `UnconstrainedClassOwnershipTests` now asks the general question — which classes are declared
  unconstrained in more than one component stylesheet AND disagree about a property — and finds
  **fifteen further pairs**, each with its own row rather than a count. Two of them are the same
  defect as `.tm-btn` with the same consequence: `.tm-filter-chip` (`_data-table.css` at manifest
  line 79 against `_filter-chip.css` at 110, disagreeing about `color`, `border-radius`, `font-size`
  and five more) and the six `.tm-modal*` classes (`_dashboard.css` at 164 against `_modal.css` at
  165, disagreeing about `max-width`, `padding`, `z-index`, `align-items` and more). They are
  recorded, not fixed: repairing them is a broad visual change across modal, scheduler, timeline,
  rich-text and form layout, and this release's subject is not that. The guard fails on a sixteenth.

- **`CssCascade` claimed fail-closed and was not.** Two mutations stayed green. A selector containing
  `[` or `#` anywhere was classified as a decided non-match, so `[data-theme="dark"] .tm-sort-icon
  { color: … }` — an idiom the bundle already uses — would have silently overridden a measured colour;
  it is now reported as unreadable whenever the subject could be the element under test. And the
  opacity guard read only `_data-table.css` rules whose selector mentioned `tm-sort-`, so
  `.tm-data-table thead th { opacity: .4 }` restored the ORIGINAL defect one level up and passed.
  Nested opacity multiplies, so the population is the whole ancestor chain: `CssCascade` now models
  pseudo-elements and `EffectiveOpacity` returns the product from the table down to the `::after`
  that draws the arrow.

### Corrected

- **The sorted column was not 1,32:1 different from an unsorted one. It was 1,00:1.** That figure,
  carried in the register since Fáze 14, compares an idle header with a HOVERED one. A sorted header
  under no pointer painted the *identical* colour to an unsorted one, because `.tm-col-sorted-asc`
  (0,1,0) lost to `.tm-data-table th` (0,1,1) and contributed nothing at all. The harm is in the
  RESTING state and the recorded number described a transient one, which also means the UX review of
  the consuming application was right about the resting state and the probe was measuring something
  else. 2.8.22's fix is unaffected — 3,67:1 light and 4,34:1 dark are measured between the two
  resting states — but the size of the defect it repaired was understated.

- **The 2.8.22 docstrings said the application probe "read the SORTED one".** It read the icon in the
  state it had just clicked into, which is a `:hover`/`:focus-visible` state that happens to occur on
  a sorted header — not the sorted state, which was invisible. Two paragraphs of the same file
  disagreed about this. Corrected, because that text is what the next phase reads as the explanation.

**nuget.org publication remains the repository owner's manual step** (`DEC-TEMPO-287-NUGET-DELIVERY`)
— `VERSION=2.8.23 eng/pack-nuget-packages.sh` then `dotnet nuget push packages/*.2.8.23.nupkg`. What
changed since 2.8.22 is that this step is no longer a blocker for consuming the button and sort-state
fixes: those are on the feed under 2.8.22 already.

## 2.8.22 - 2026-08-26

Three defects, one release. Every one of them was invisible to a markup assertion and to a
screenshot, because in all three cases the class was on the element, the rule was in the file, and
something else decided what the user saw.

### Fixed

- **A component stylesheet may no longer redefine a library-wide button class.** `_pivot-table.css`
  declared a second, GLOBAL `.tm-btn` — its own `gap`, `padding`, `font-size` and the shorthand
  `border: 1px solid transparent` — and `css/tempo-blazor.css` imports it at line 82 while
  `_button.css` is at line 33. Both selectors are (0,1,0), so source order decided, and in the
  shipped bundle the two rules stood at line 190 (`.tm-btn-outline-secondary`) and line 1344 (the
  duplicate). Consequence measured on a consumer of 2.8.16 AND 2.8.21: every `ButtonVariant.Outline*`
  rendered with `border-color: rgba(0,0,0,0)` in both themes — a button that is only text. The token
  fix of 2.8.17 (`--tm-border-color` → `--tm-border-color-control`, 1,24:1 → 4,83:1) had been landing
  on a property that a few hundred lines later was written back to transparent, which is why two
  releases of it changed nothing. The block is now `.tm-pivot-btn` and the 14 buttons that used it —
  `TmPivotFieldPanel`, `TmDocumentEditor`, `TmDiagramEditor`, `TmNotionTemplateButtonBlock` and the
  demo notification page — carry the new name. No public API changed. `OutlineSecondary` now measures
  **4,83:1 light / 5,71:1 dark** against the surface.

  Two things came out of the sweep that the report did not contain. `.tm-btn--primary` was referenced
  by three of those buttons and declared NOWHERE, so they rendered as a bare base with a transparent
  border; it exists now as `.tm-pivot-btn--primary`. And `_data-table.css` carried the SAME defect on
  a second class: a global `.tm-btn-link` with `border: none` and `font-size: inherit`, imported at
  line 79, which took the 1px box and the size-class font size away from every `ButtonVariant.Link`
  in the library. The standalone reset those bare link buttons genuinely need is now its own class,
  `.tm-link-button`, in `_button.css`. `PivotButtonScopeTests` sweeps the whole `components/`
  directory rather than a list — but only for the `.tm-btn*` names, which is a complete answer to a
  narrower question than this paragraph originally claimed. See 2.8.23.

- **The sorted column is distinguishable again.** The difference between a sorted and an unsorted
  header was **1,32:1 light and 1,22:1 dark**, against the 3:1 of WCAG 2.2 SC 1.4.11 — a user could
  not tell what the table was ordered by. Two dead rules, and neither matched the hypotheses that had
  been written down. `.tm-col-sorted-asc { color: … }` is (0,1,0) against the (0,1,1) of
  `.tm-data-table th { color: … }`, so the sorted state contributed nothing at any source order; and
  `opacity: 1` on `.tm-sort-icon.tm-sort-asc::after` was inert, because nested opacity MULTIPLIES —
  1 inside a box at `opacity: .4` still paints at 0.4, in both states. What the earlier probe had
  actually recorded was the header's `:hover`/`:focus-visible` colour, which is (0,2,0) and does beat
  the base rule: composite `--tm-text-secondary` at 0.4 over the header background and you get
  `rgb(179,184,190)` light / `rgb(90,99,115)` dark to the pixel, composite `--tm-text-primary` the
  same way and you get `rgb(156,160,166)` / `rgb(105,112,125)`.

  The indicator now takes two tokens on the SPAN itself — `--tm-sort-indicator-idle` and
  `--tm-sort-indicator-active` — where no inherited header colour can reach it, and no opacity at
  all. Measured, both themes: idle **4,63:1** light / **3,75:1** dark against the header background,
  active **16,98:1** / **16,30:1**, and the state change **3,67:1** / **4,34:1**. The sorted label
  rule was raised to `.tm-data-table th.tm-col-sorted-asc` so that it, too, applies.

- **Column headers stopped lying to a screen reader and stopped costing two Tab presses each.**
  `aria-sort="none"` was rendered on non-sortable headers as well — on an ACTIONS column that
  announces a sort affordance which does not exist, because ARIA reserves the attribute for a column
  that participates in sorting. It is now absent there. The pin toggle was a real `<button>` in every
  visible header, so six columns cost eleven Tab stops and five of them painted nothing until hover;
  it is `tabindex="-1"` now and its header answers **P** instead, advertised through
  `aria-keyshortcuts`. A header that offers only the pin became a focus stop so the function stayed
  reachable, and a header that offers nothing at all still is not one.

  Space is deliberately still NOT an activation key, and that is the decision of 2.8.9 unchanged: on
  a plain `<th tabindex="0">` Space is the browser's "scroll one screen", and Blazor binds
  `:preventDefault` when the handler is registered rather than per event, so it cannot be cancelled
  for one key without cancelling Tab and trapping the keyboard. Enter satisfies WCAG 2.1.1 and
  `columnheader` has no Space activation to begin with.

### Measurement

- `ThemeCss.Composite` is the one place this suite composites a colour over a background, and
  `SortIndicatorContrastTests.TheTwoRecordedReadings` pins the four pixel values and the four ratios
  it reproduces. The apparent disagreement between the two reviews of the sort indicator — 1,91:1 /
  2,95:1 against 2,52:1 / 3,58:1, recorded as "probably a different reference background" — was not
  methodological: one read the UNSORTED icon and the other read the icon in the state it had just
  clicked into. Same background, same compositing, different question.
- `CssCascade` resolves specificity and source order over any stylesheet and is FAIL-CLOSED: a
  selector it cannot model, whose subject could be the element under test, is reported as unmodelled
  and fails the caller instead of being counted as "does not match". `TmDataTableAlignmentTests` was
  the first, private implementation of that shape and now delegates to it, so the extraction is
  proved by guards that already existed.

**nuget.org publication is NOT part of this release commit.** It is the named manual step of the
repository owner (`ptyll`), per `DEC-TEMPO-287-NUGET-DELIVERY`: `dotnet nuget push` over the packed
`packages/*.2.8.22.nupkg`. 2.8.17 was tagged and never reached the feed precisely because that step
was assumed rather than named, and two phases were rewritten because of it.

## 2.8.21 - 2026-08-22

Release tooling only. **No component changed.** 2.8.20 is already on nuget.org (immutable); this
number exists so the feed guard can be green over a number that is still free.

### Fixed (release evidence)

- **The three text-gates on the release path compared their clauses, not their inputs.** A mutation
  that left the comparison in place and hardcoded the read (`announced="$version"` instead of `sed`
  over CHANGELOG.md; `published=$total` instead of classifying `dotnet nuget push` output; a fake
  200 body instead of `curl` of the flat container) stayed green on every existing needle. Each
  script now has a needle over the READ itself: the mutation is applied in
  `ReleaseScriptInputReadTests` and must turn that member red, while the unmutated script stays
  green.

- **The push classifier is bound to SDK wording, and that is now a named property of the step.**
  PUBLISHED = `Your package was pushed.` / `Created http`; SKIPPED = `already exists at feed` /
  `Conflict http`; anything else is UNRESOLVED (red). Measured over SDK 10.0.111. A future SDK that
  rewords those sentences fails the release loudly rather than shipping a false green.
  `eng/verify-push-classifier.sh` re-measures the wording against a local 201/409 endpoint (python
  `http.server`, `NuGet.Config` with `allowInsecureConnections=true`) and is called from both
  publish workflows after pack, before the real push.

- **`Resolves_neutral_english_value` reads the neutral table through an empty culture name, not
  `en`.** Pinning `en` would have stayed green and silently changed subject the day
  `TmResources.en.json` appeared. `Shell_RendersEnglishNavigation_ByDefault` still renders inside
  `UseUiCulture("")`.

## 2.8.20 - 2026-08-21

Release tooling only. **No component changed**, so a consumer that is happy on 2.8.19 gains nothing by
upgrading. This number exists because the previous one was minted twice and nothing in the repository
could say so.

### Fixed (release evidence)

- **The version being announced was never compared against the tag store.** `v2.8.19` resolves to
  `714093ce` and the published `tempo.blazor.2.8.19.nupkg` records that same commit in its own nuspec;
  every one of the 26 packages staged locally under that number records `d1c8e776`. Two commits behind
  one version number, with the first artefact already immutable on nuget.org (published
  2026-08-19T06:11:27Z, measured as HTTP 200 on the flat container with 2.8.18 as the positive control).
  Both existing guards were green throughout and were right to be: one compares every packable csproj
  against the changelog, the other compares each staged nuspec against `HEAD`. Neither pair answers
  "is this number still free".

  `ReleaseContractTests.AnnouncedVersion_IsEitherUntagged_OrItsTagNamesTheCommitBeingPacked` compares
  that third pair and does it unconditionally: `v<announced>` either does not exist, or resolves to the
  commit being packed. There is no arm in which the comparison is reported to a reader instead of
  enforced.

  It does not rest on `git diff --name-only v2.8.19 HEAD -- src/` being empty, which it is. That
  implication has not been measured in either direction here — the two packages carrying 2.8.19 do not
  share a commit, so `src/` was never the only thing that differed between them — and the guard needs
  neither direction, because it compares a tag against a commit and never opens a package.

  What it does not see, since the gap is real and cheap to over-read: tags this ref store does not
  have. `actions/checkout@v4` fetches at depth 1 without tags, so on a push to `main` there is nothing
  to find and the check passes without having looked. Every run therefore prints how many tags were
  visible, so a zero says so instead of hiding inside a green. It says nothing at all about nuget.org:
  a number can be published without any tag ever existing, and only a feed lookup answers that.

- **Release gate is the CI filter, not `dotnet test TempoBlazor.slnx`.** Demo.Api including the two
  smtp4dev tests is in the gate (CI starts `rnwood/smtp4dev`; `Smtp4DevHost` starts the container
  locally). Named exceptions: `Tempo.Blazor.E2E` (measured 1220/124/79 of 1423 in 12 h 8 min —
  not green, not a gate) and `Tempo.ReportServer.Api.Tests.MsSql` (27/177, SQL Server missing;
  do not fix). `ReleaseGateFilterTests` keeps the two publish workflows on one filter.

### Internal

- **`ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom` was two guards in one `[Fact]`,
  and one of them is empty most of the time.** The script-text contract now lives in
  `PackScript_PassesTheCommitIn_RefusesADirtyTree_AndVerifiesTheStampBackOut` and runs unconditionally.
  The staged-package half keeps the old name, because that is the half `eng/pack-nuget-packages.sh`
  points at for the stamp comparison and the name had to stay true there.

  An empty staged population is now reported as a **skip** rather than a pass. The size of that change,
  said plainly: the old shape asserted nothing false. It wrote the population size to test output and
  passed, so "nothing was checked" and "26 packages were checked" left the same green — the loss was
  evidence, not truth, which makes this a weaker instance of vacuous green than one that certifies
  something. Deleting the reporting line left every assertion green, so the report was a claim about
  the run that the run did not enforce; the outcome carries it now.

  The skip is decided at discovery by `StagedPackagesFactAttribute`, and that is not a preference:
  xUnit v2 has no runtime skip. `Assert.Skip` is absent from `xunit.assert 2.9.3`'s `Assert`, and the
  `$XunitDynamicSkip$` protocol its `SkipException` speaks has zero occurrences in
  `xunit.execution.dotnet.dll` of the same version — measured with the tool that finds `SkipReason`
  there five times, so the zero is a reading rather than a silence. The body's first assertion is a
  tripwire and not the rule: swap the attribute back to `[Fact]` and the empty population makes it red,
  so the two reachable shapes are skip and red and the pass over nothing is gone from both.

  A second door to the same silent pass is closed in the same change: a candidate whose archive holds
  no `.nuspec` was counted and skipped by the loop, so a non-empty population made only of such files
  ran zero assertions and passed with `nuspec-inspected=0`. The tripwire cannot see that — it is about
  an EMPTY population — so `withoutNuspec` is now asserted to be zero.

  The 2.8.19 note below names the old test for the script-text assertions that have since moved into
  the new one. It is left as written — it was true when it shipped, and rewriting released notes is a
  worse habit than a superseded sentence.

## 2.8.19 - 2026-08-19

Release tooling only. **No component changed**, so a consumer that is happy on 2.8.18 gains nothing by
upgrading — this release exists so that the *next* one can be trusted.

### Fixed (release evidence)

- **`eng/pack-nuget-packages.sh` packed a dirty tree and certified the result.** The commit stamped
  into every nuspec came from `git rev-parse HEAD`; the script then re-opened the packages it had just
  produced and compared the stamp against that same value, and `ReleaseContractTests` compared it
  against `HEAD` a third time. Over a **dirty** tree all three are the same number, so the equality
  held **by construction** while the packed bytes came from source that no commit contains. This was
  not an uncovered case, it was an active false confirmation: measured on 2026-08-18, `packages/` held
  26 `Tempo.*.2.8.18.nupkg` stamped `commit="d49ede02…"` — which is the 2.8.17 commit — and all three
  checks reported them good, because HEAD really was d49ede02 while the 2.8.18 content sat uncommitted
  in the working tree. Those 26 packages then went red on their own the moment the bump was committed
  and HEAD moved: same bytes, same stamp, opposite verdict. A guard whose answer depends on when you
  ask it is not measuring the packages.

  The pack now **refuses a dirty tree** rather than labelling it. `ALLOW_DIRTY_PACK=1` is the explicit
  escape for a deliberate local experiment, and it does not restore the lie — the stamp then ends in
  `-dirty`, so a package built off uncommitted source says so in its own nuspec instead of borrowing
  its parent commit's good name. Such a package must never be published or copied into a consumed
  feed, and it cannot pass unnoticed: `-dirty` equals no commit id, so the test fails it. Nothing
  changes for CI, which checks out the tag into a clean tree — the blind spot was always the
  **local** pack, and a local pack is how consuming repositories fill their own NuGet feeds.

  Why a dirty-check and not a better stamp: the defect is not *what* is read but that the label is
  verified against the source it was minted from. Any replacement inside that loop — SourceLink, a
  content hash, another way of reading `HEAD` — only moves the tautology. The only thing that breaks
  it is refusing the situation in which a truthful answer does not exist.

### Internal

- `ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom` now also asserts that the
  script *contains* the refusal, using the same read-the-script technique it already applies to
  `-p:RepositoryCommit=` and `git rev-parse HEAD`. It has to be a text assertion: `ReadGitHead()`
  deliberately never shells out to git, so the test can read which commit `HEAD` names but never
  whether the tree matched it at pack time — it cannot measure the state the refusal exists for. The
  needles are matched against the script's **code** with comment lines stripped, because the same
  words appear in the comment block that explains the clause, and "delete the code, keep the prose"
  is what a hasty revert leaves behind. Stated plainly so a green is not over-read: this proves the
  clause is present, not that it runs, not that its exit code is honoured. Only running the script
  over a dirty tree proves that.

## 2.8.18 - 2026-08-18

One behaviour change in `TmDataTable`, and it is a change rather than an addition: **`ShowPagination="false"`
now means "do not paginate", not "paginate and hide the controls".** Read the breaking-change note before
upgrading — no API was added, so nothing about the parameter list tells you this happened.

### Changed — BREAKING for client-side tables with the pager hidden

- **`ShowPagination="false"` no longer slices the `Items` collection.** `ShowPagination` gated only the
  render of the pager (`TmDataTable.razor:469`); the slice in `RefreshClientItems()` ran regardless, so a
  table handed 200 rows with the pager hidden rendered rows 1–25 and offered no element that reached rows
  26–200. The summary that would have said "showing 1–25 of 200" sits inside the same `@if` as the pager,
  so it did not lie — it was simply absent, and the failure mode was silence. Slicing is now derived
  from the pager: no pager, no slice. The client-side table renders every item it was handed.

  **What changes for you:** a consumer that set `ShowPagination="false"` on a collection longer than the
  effective page size will now see the whole collection instead of its first page. That is the point — the
  rows were already handed to the component and were already unreachable — but a page that relied on the
  slice as a display limit has to limit the DATA instead, at the source, and say so. Doing it in the grid is
  what made the limit invisible. `ScrollMode="Virtualized"` is not the substitute: it renders through
  `<Virtualize>` into a fixed-height container and turns inline editing off, so it is a different layout,
  not "show everything".

  **What does not change:** server-side paging through `DataProvider` is untouched — the slice this removes
  lives only in the `Items` path. A table with the pager visible still pages exactly as before
  (`DataTable_Pagination_ShowsOnlyFirstPageRows` measures that arm, and inverting the new condition turns 34
  tests red). No parameter was added: an opt-out would have been a third independent switch beside
  `ShowPagination` and `ScrollMode` with nothing keeping the three consistent, which is the shape that
  produced this defect one level up.

## 2.8.17 - 2026-08-15

The rest of the host application's gap register that did not fit in 2.8.16. Released **as one
tag** — a register that names a version and then ships half of it stops being read. The list was
frozen before the first library change: orphan classes that the rendered-DOM probe actually
saw, the leftover `role="toolbar"` promises, and the outline button whose border token had been
measured under 3:1. Universal selectors stay a 2.9.0 target. The `dismiss` class on the Blazor
WASM error UI is a host hook, not a Tempo class.

### Fixed

- **Six class names Tempo rendered with no rule.** `tm-pagination-size` is the page-size wrapper
  (its label already had a rule). `tm-pagination-disabled` is the disabled-pager modifier —
  `TmPagination` already put it on the root when `Disabled` is true, but five guard runs never
  rendered that state, so the missing paint was a claim about the stylesheet, not a finding on a
  page. It now dims the pager and the component disables every interactive child, not only
  swallows the click. `tm-avatar-fallback` fills the initials. Every `AvatarColor` modifier
  (`gray` through `pink`) has a token-backed colour. `tm-avatar-2xl` is the box `AvatarSize.Xxl`
  already emitted; CSS only had `.tm-avatar-xxl`. `tm-input-search` is the search field's own
  chrome (`appearance: none`); `.tm-search-input` is a different class on different markup.
  Guarded by `OrphanClassCssContractTests` (source + shipped bundle, mutation both ways).

- **Twenty-five leftover `role="toolbar"` attributes in 22 files.** Same defect 2.8.16 closed on
  `TmFormActionBar`: a toolbar is a promise of one tab stop and arrow-key movement. None of the
  eight files that mention `tabindex` implement toolbar roving — ribbon tabs, canvas roots and
  comment panes are a different pattern. They are now `role="group"` and keep their accessible
  name. Guarded by `ToolbarRoleGuardTests` (empty set in `src/`, scanner mutation on attribute
  vs comment).

- **`ButtonVariant.OutlineSecondary` used the decorative border token.** `--tm-border-color`
  is 1.24:1 on white and 1.41:1 on the dark surface — below WCAG 1.4.11's 3:1. The control
  token is 4.83:1 / 5.76:1. The outline button now takes `--tm-border-color-control`.

### Not in this release

- `tm-pagination-controls` / `-prev` / `-next` remain semantic hooks beside the painted
  `tm-pagination-btn`. `tm-scroll-spy-nav` is a BEM root. `dismiss` belongs to
  `blazor.webassembly.js`. Universal `*` selectors stay 2.9.0.

## 2.8.16 - 2026-08-12

Nine entries from a host application's gap register, released **as one tag**. The register's own rule is
that a version it promises must arrive whole: a register that names a version and then ships half of it
stops being read. Six of the nine are about a form's action bar, its navigation guard and the picker
that sits above them, and they share one defect class — **an affordance with no mechanism**: a marker,
a role or a parameter that states an intent the rendering, the keyboard or the save path never honours.

### Known issue in the 2.8.15 artefact — read this before auditing that release

**The `2.8.15` artefact that was packed carries `repository commit="efb00b89"`, which is the commit
of `2.8.14`.** (Corrected in 2.8.26: the heading said "the published 2.8.15 package", but the flat
container has never listed 2.8.15 — the artefact was packed and audited, never pushed. The stale
label described below is therefore a property of the staged file, not of anything the feed serves.)
`2.8.16` is the first release packed by the fixed script, and its label was verified against the bytes
during the pack, not only in a test.

### Changed (source-breaking, one parameter)

- **`TmNavigationGuard.OnSaveAndLeave` is now `Func<Task<bool>>?` instead of `EventCallback`, and a
  failed save no longer discards the work.** The guard used to navigate as soon as the callback
  completed, whatever it did — so a save that failed on an HTTP error or on server-side validation threw
  the changes away. That is precisely the loss the guard exists to prevent, delivered by the button that
  promises to prevent it, which is why the only host that needed this API deliberately did not use it.
  A delegate with no result cannot say "the save failed", so the type had to change; leaving a
  data-losing API alive under a familiar name would be worse than making callers pick a return value.
  Return `true` and the guard re-issues the blocked navigation, `false` and it navigates nowhere and
  leaves the dialog open, so the user keeps both the changes and the choice — including the original
  destination, so a retry that succeeds still goes where they were going. A second click while the save
  is in flight is swallowed rather than duplicated.

  Not inferred from `IsDirty` after the callback, though that was the cheaper option: the parameter is
  pushed by the parent's render, which has not necessarily happened when the awaited callback returns,
  so the guard would be reading a value that is stale for reasons it cannot see. The host knows whether
  the save succeeded; now it says so.

  Guarded by `SaveAndLeave_FailedSave_StaysPut_AndKeepsTheDialogOpen`,
  `SaveAndLeave_RetryAfterFailure_LeavesForTheOriginalDestination` and
  `SaveAndLeave_SecondClickDuringSave_DoesNotStartASecondSave`.

- **`TmStatCard` rejects `SubValueColor` without `SubValue`** with an `InvalidOperationException` naming
  the card, instead of ignoring it. The sub-value span is rendered only when `SubValue` is non-empty, so
  a colour on its own is a stated intent that rendering silently denies — the first instance of it lived
  through eight call sites and several months, because a parameter that does nothing says nothing.
  The invariant is in the COMPONENT, not in a markup scanner, and the placement is the point: it covers
  splatted `@attributes`, `DynamicComponent` and consumers outside this repository, and it fails at the
  moment of misuse rather than at the next audit. A static scan of `.razor` markup is a legitimate second
  line for a release gate, but it cannot see those paths and must carry its own denominator.

### Fixed

- **`TmUserPicker`'s result list painted below sticky chrome.** It sat on `--tm-z-dropdown` (1000) while
  a floating `TmFormActionBar` sits on `--tm-z-sticky` (1020), so a list opened near the bottom of a page
  ended up UNDER the bar and its items were unreachable by mouse — keyboard selection still worked, which
  is what makes this easy to miss. Now on `--tm-z-popover`, the same fix `_filterable-dropdown.css` already
  carries for the same defect. `FloatingMenu` widens the exposure, because a fixed list reaches the bottom
  of the viewport.

- **The picked user's chip had no accessible name.** Once a user is selected the search input disappears,
  and with it the `<label for=…>` association — a screen reader then read the person's name with no hint
  of WHICH field held it. The chip now borrows the same label through `aria-labelledby`, carried by
  `role="group"`: a plain container cannot take an accessible name, and `group` promises no keyboard
  mechanism, unlike the `toolbar` role this release removes from `TmFormActionBar`. No label, no role —
  a role whose only job is to carry a name is an empty promise when there is no name to carry.

- **"Loading…" and "No results found" were never announced.** Both were plain elements that appeared and
  disappeared with the search. The fix is not an `aria-live` attribute on the message — a live region that
  enters the DOM together with its own text is not reliably announced, because the screen reader was not
  watching that node when the text arrived. There is now ONE permanent `role="status"` region, rendered
  even when it has nothing to say; only the messages inside it are conditional. Empty it has no box of its
  own. The transient error keeps its own `role="alert"` region: it is an interruption, not a progress
  report, and it carries a retry button.

- **`TmFormActionBar` coloured the generic `Status` slot as SUCCESS.** `.tm-form-action-bar__status` hard-coded
  `color: var(--tm-color-success-text)` although the parameter is called `Status`, not `SuccessStatus`, so an
  error message next to the save button would have been GREEN — the colour contradicting the text. Severity is
  now STATED via `StatusSeverity` (default `None` = inherit) and never derived, and every severity has both a
  modifier and its own colour, because a parameter whose values do not change anything is the same defect one
  level up.

- **`TmFormActionBar` no longer claims `role="toolbar"`.** The role promises a single tab stop and arrow-key
  movement between the actions; the bar never had roving tabindex, so a screen-reader user heard "toolbar",
  pressed an arrow and nothing happened. It is a `group` with the same `aria-label` now — honest for two or
  three buttons, and it promises nothing that would have to be delivered. **This class is NOT closed:**
  `role="toolbar"` without roving tabindex is still on ~25 other components (`TmBulkActionBar`,
  `TmAuditLogViewer`, `TmLedgerGrid`, `TmReportViewer`, the editors). Recorded, not fixed — the bar is where
  it surfaced, because Phase 3.5 moved a page's PRIMARY action into it, but the mechanism is shared.

### Added

- **`--tm-form-action-bar-z-index`, defaulting to `--tm-z-sticky`.** Overriding `--tm-z-sticky` was not a
  substitute: it moves EVERY sticky element, so a host that needed the bar above its mobile menu had to lift
  its own sidebar, scrim and header instead.

- **`--tm-form-action-bar-reserve-block-size`, desktop and below 768 px.** `position: fixed` contributes
  nothing to the flow, so a page under the bar must leave room or the bar covers the end of the form — and the
  only party that knows the bar's height is this library, since it is made of its own `--tm-space-*`, the
  `__inner` border and the button height. Hosts were GUESSING it; in one application six pages guessed
  independently. The value is DERIVED from the same tokens the bar is built from, never written in pixels: a
  magic number would drift silently the moment any input changed. Its reference content is ONE row of actions —
  how many action groups reach a phone is something only the host knows, and that limit is spelled out in the
  token itself, together with the arithmetic.

### Known issue in the packed 2.8.15 artefact — read this before auditing that release

**The `2.8.15` artefact that was packed carries `repository commit="efb00b89"`, which is the commit
of `2.8.14`.** (Corrected in 2.8.26: the heading called it "the published 2.8.15 package", but the
flat container has never listed 2.8.15 — it was packed and its label inspected, never pushed.) The
artefact's CONTENT was correct — it contained the `width: auto` fix described under 2.8.15 — only
the provenance label was one release behind. An auditor who checks out the labelled commit finds
the fix missing and concludes the release did not ship it, which is why this is written down
rather than left as a curiosity.

Cause: `eng/pack-nuget-packages.sh` packed with `--no-build`, and `dotnet pack --no-build` inherits
`SourceRevisionId` from `obj/`, where it was stamped by the LAST build — one commit back.

Fixed for every future release in `7dcdce52`: the pack script passes
`-p:RepositoryCommit=$(git rev-parse HEAD)` and, after packing, reads the label back **out of the
produced bytes** and refuses to publish a mismatch. Guarded both ways by
`ReleaseContractTests.PackedPackages_RecordTheCommitTheyWereBuiltFrom`.

The already-packed `2.8.15` was deliberately **not** repackaged: republishing the same version with
different bytes is worse than the wrong label, because it breaks the one property a version number
has. It was corrected by the next release. **When 2.8.16 shipped, the label was verified to match its
own commit** — the first live proof the guard works during a real pack, not only in a test.

## 2.8.15 - 2026-08-10

One fix, and it is a P1: since 2.8.13 a host that set the floating action bar's start inset got a bar
that hung off the right edge of the screen, with its primary button unreachable by mouse.

### Fixed

- **`TmFormActionBar` in `FloatingBottom` never reset the base `width: 100%`, so the two insets and the
  width formed an over-constrained box.** For a `position: fixed` element the containing block is the
  viewport, so `width: 100%` resolves to the full window width. Combined with
  `inset-inline-start` + `inset-inline-end` that is three constraints for one axis, and CSS resolves it
  in LTR by dropping the END inset. A host setting
  `--tm-form-action-bar-inset-inline-start: 18.5rem` on a 1440px window therefore got a bar laid out at
  296…1736px — overflowing the viewport by exactly the start inset. Because `__end` right-aligns the
  actions, the primary button landed around x 1620…1700, entirely off-screen.

  This was worse than a cosmetic overflow: a fixed element contributes nothing to the document's
  scrollable area, so no horizontal scrollbar appeared and the button could not be reached by mouse at
  all. Keyboard users could Tab to it, but the focus ring was painted outside the window.

  The floating variant now sets `width: auto` and lets both insets decide the box, which is what the
  inset variables promised in 2.8.13. `2.8.13` and `2.8.14` both ship the defect; hosts that never set
  the inset were unaffected, because `left: 0; width: 100%` happens to equal the viewport.

  Guarded by `FormActionBarCss_FloatingBottom_ResetsBaseWidthSoBothInsetsDecideTheBox`. The existing
  inset test could not have caught it — it only ever asserted the START inset, so it kept confirming
  that the bar began in the right place while saying nothing about where it ended.

## 2.8.14 - 2026-08-09

Four review findings against 2.8.13, three of them in the floating layer that shipped the day before.
Nothing here changes an API; it makes 2.8.13 behave the way its own documentation already claimed.

### Fixed

- **`TmUserPicker`'s floating layer never actually released itself.** The tracking entry was keyed by
  the menu element and released by passing that `ElementReference` back to script. Blazor resolves an
  `ElementReference` through a document query, and a closing list is out of the DOM by the time the
  release runs — so the query returned null, `release` returned immediately, and the entry plus the
  shared scroll/resize listeners stayed behind. It was self-healing (the next scroll dropped it through
  the `isConnected` guard) but the code comment and the unit test both described something that never
  happened in a browser. The map is now keyed by a string the component owns (`{Id}-results`), released
  by that same string, and the test asserts the argument rather than merely the call.
- **Switching `FloatingMenu` off while a list was open left the layer attached.** `OnAfterRenderAsync`
  returned early on `FloatingMenu`, so the release branch was unreachable and the script went on writing
  inline `left`/`top`/`width` onto a list that was no longer floating. The guard now sits inside the
  anchor condition, leaving the release path open.
- **The floating variant silently lost the list's height cap.** `.tm-user-picker__results` caps at
  `max-height: 240px`, but the script overwrites `max-height` on every placement and wrote the whole
  available space, so a floating list could be far taller than the same list not floating. It is now
  clamped to the stylesheet's 240px, with a unit test tying the constant to the CSS so the two cannot
  drift. Note the asymmetry, which is deliberate: only the ceiling can be clamped. Forcing a *floor*
  into `max-height` would not create room — it would push the list out of its containing block, where
  the modal clips it, which is the exact symptom the floating layer exists to remove. Too little room on
  both sides is a layout problem in that dialog, and the list's honest response is to scroll within what
  is left.

### Changed

- **`.tm-form-action-bar--floating-bottom` uses the logical inset properties.** The custom properties
  were named `--tm-form-action-bar-inset-inline-start` / `-end` but were applied to physical `left` /
  `right`, so the names were only true in left-to-right text. In LTR nothing changes; in RTL the start
  inset now follows the writing direction, as the name always promised.

## 2.8.13 - 2026-08-09

Six gaps that consuming applications had been papering over in their own code. Every one of them is a
place where the library made the application reach around it — with a CSS override that has to win a
specificity fight, with an `@key` remount, or with a marker the component would not render. Everything
here is additive; no released member changed shape or meaning.

### Added

- **`--tm-form-action-bar-inset-inline-start` / `--tm-form-action-bar-inset-inline-end` — the floating
  bar can be told about the shell's chrome.** `.tm-form-action-bar--floating-bottom` pinned `left: 0`,
  so in an application with a fixed side navigation the bar ran underneath it. The only way out was to
  override `left` from application CSS — and because Tempo ships as *isolated* CSS, that rule carries a
  `[b-…]` scope attribute and therefore a specificity of at least (0,2,0), while the application's
  stylesheet is loaded *before* the scoped bundle. A tie goes to Tempo, so the obvious override is a
  dead rule that looks identical to a working one on a screenshot. Both insets now read a custom
  property, defaulting to `0` — the released full-bleed behaviour — so the host sets a variable instead
  of fighting the cascade.
- **`TmScrollSpyNav.ScrollContainerSelector` — scroll-spy on the element that actually scrolls.** The
  listener went on `window`, which is right only when the document itself scrolls. An application shell
  that puts its content in an `overflow-y: auto` column raises the scroll event on that column and never
  on the window, so `EnableScrollSpy` did nothing at all and did it silently. The selector names that
  column and `ScrollOffset` is then measured from the top of the container rather than the viewport. A
  selector that matches nothing falls back to the window rather than going quiet.
- **`TmScrollSpyNav.AutoSelectFirstItem` — the option to have nothing current.** `OnParametersSet`
  unconditionally made the first visible item active, so with scroll-spy off `aria-current="true"` sat
  on the first section forever and went stale as the reader scrolled. Setting it to `false` leaves every
  item non-current until a click or scroll-spy selects one. This has to be a parameter of its own: the
  workaround it replaces — passing `ActiveId=""` — hands the component an id no section has.
- **`TmUserPicker.Id` and `Required`, plus `aria-controls` and `aria-activedescendant`.** The label was
  a bare `<label>` with no `for` and the input had no `id`, so clicking the label did nothing and no
  assistive technology tied the two together. There was no way to mark the field required, so consumers
  reproduced the asterisk with their own CSS. And `role="combobox"` shipped without either of the two
  attributes that let a screen reader announce the option the arrow keys are moving over — focus stays
  in the input, so without them the highlight is invisible to a reader. The listbox is now
  `{Id}-results` and each option `{Id}-option-{i}`, both referenced from the input. `for` is omitted
  while a user is selected, because the input is not on screen then and a dangling `for` is worse than
  none.
- **`TmUserPicker.FloatingMenu` — the results list can leave the flow.** The list is `position:
  absolute`, so an ancestor with `overflow: auto` — a modal body, a scrolling form column — clipped it
  and scrolled it away from its input. Opting in makes it `position: fixed` and has
  `TmUserPicker.razor.js` anchor it to the input on every scroll (captured, so inner scrollers count)
  and resize, flipping it above the input and clamping its height when there is not enough room below.
  It is off by default: it costs a module import, and a picker that is not inside a scroll container
  does not need it.
  - This is a floating layer, **not** a DOM portal, and the difference is visible in one case: an
    ancestor with a `transform`, `filter`, `perspective`, `contain`, or a `will-change` naming them
    becomes the containing block for a fixed descendant. `.tm-modal` is one, because it animates in with
    a transform. Inside such an ancestor the list is bounded by that box instead of by the viewport,
    which is why the available space is measured against it and the list flips rather than overflowing.
    The clipping and the scroll-away — the actual reported failure — are gone either way.
  - The scroll and resize listeners are shared by every picker on the page and are unbound as soon as
    the last open list closes.
- **`TmNumberInput.Required` and `Id`, and `TmToggle.Required`.** A form mixing these with
  `TmTextInput`/`TmSelect`/`TmTextArea` marked some of its mandatory fields and, with no parameter to
  reach for, silently not the others. `TmNumberInput` renders the same `tm-input-label-required`
  asterisk plus `required`/`aria-required`, and its label finally has a `for` — an asterisk on a label
  that names nothing is decoration. `TmNumberInput.Id` also drives the error and help ids
  (`{Id}-error`, `{Id}-help`), which were previously generated and unaddressable.
  - `TmToggle.Required` deliberately does **not** emit the native `required` attribute. On a checkbox
    that means "must be checked", which is right for an "I agree" box — `TmCheckbox` does emit it — and
    wrong for a switch whose off state is a legitimate answer: the form would refuse to submit. It marks
    the visible label and sets `aria-required` on the input; the value is the form's to validate.

### Notes

- `TmUserPicker` now implements `IAsyncDisposable` instead of `IDisposable`, so it can release the
  floating layer's module. Blazor disposes components through whichever interface they implement; no
  consumer calls `Dispose()` on a component directly.

## 2.8.12 - 2026-08-08

Two gaps in the public API, not cosmetics: in both cases a consuming application could see the right
thing on screen and still had no supported way to ask for it. Everything here is additive — no released
member changed shape or meaning.

### Added

- **`TmDataTable<TItem>.PageSize` / `PageSizeChanged` / `public ChangePageSizeAsync(int)` — a way to
  change the page size of a table that is already mounted.** `DefaultPageSize` was read exactly once, in
  `OnInitializedAsync`, and the only member that could change the size afterwards was `private`. A page
  with its own page-size control therefore had to remount the table through `@key` — which resets the
  page size, and along with it the scroll position, the focus, the selection, the sort and every expanded
  row. Both new routes resize in place.
  - `PageSize` is the **controlled** counterpart of `DefaultPageSize`: nullable, and when supplied it wins
    — including in the provider's *first* query, because it is applied in `OnInitializedAsync` rather than
    afterwards, so a server-side table is not made to fetch the default page size first. Leaving it null
    keeps the released `DefaultPageSize` behaviour exactly as it was.
  - `PageSizeChanged` enables `@bind-PageSize`. It fires for the built-in dropdown, for
    `ChangePageSizeAsync`, for an applied saved view, and when an `IDataTableDataProvider<TItem>` answers
    with a page size other than the one asked for — a provider-imposed size wins, and a bound host that
    never heard about it would be describing a table that no longer exists. The remembered parameter value
    is updated **before** the callback is invoked, so the value coming straight back in as a parameter is
    not mistaken for a new host-driven change and does not issue a second query.
  - Changing the size **returns to page one**, deliberately: page *N* denotes a different slice of the
    data at a different size and at a larger size may not exist at all. This is what the built-in dropdown
    has always done, so the public routes and the built-in control cannot disagree.
  - A size of zero or less is rejected with `ArgumentOutOfRangeException` rather than silently producing
    an empty table (`Take(0)`).
  - **Controlled without a binding re-syncs instead of drifting.** If `PageSize` is supplied but
    `PageSizeChanged` is not, a change made through the dropdown, `ChangePageSizeAsync` or a saved view
    can never reach the host, so the next parameter set snaps the table back to `PageSize`. Documentation
    alone would not have prevented the two numbers from disagreeing silently. A page size imposed by the
    *provider* deliberately does not arm this: the provider would answer the re-synced query with the same
    imposed size again, costing one query and one jump back to page one per parent render.
- **`TmToggle.AriaLabel` / `AriaLabelledBy` / `Id` — an accessible name without visible text.** `Label`
  filled the visible `<span>` **and** the input's `aria-label` at once, and `AdditionalAttributes` splat
  onto the wrapper `<div>`, so an `aria-label` supplied from outside named the wrapper and not the switch.
  A page whose channel labels live next to the toggle — because the element carrying `data-testid` has to
  contain the switch *alone*, or a click in its middle would miss the control — therefore shipped a switch
  that a screen reader announced with no name at all.
  - All three land on the inner `<input type="checkbox" role="switch">`, never on the wrapper. `AriaLabel`
    takes precedence over `Label` for the accessible name and renders no visible text; `AriaLabelledBy`
    points at a label the page already renders; `Id` lets a host-owned `<label for="…">` reach the switch.
  - `Label` still sets `aria-label` on the input, and `AdditionalAttributes` still splat onto the wrapper,
    so `data-testid` keeps addressing the same element as before.

### Fixed

- **`TmDataTable<TItem>` applied a superseded provider response over a newer one.** The pager, the
  filters, sorting, search and the page size each start a load without awaiting the one in flight, and
  `LoadFromProviderAsync` applied whatever arrived last to `_displayedItems`, `_totalCount`,
  `_currentPage`, `_pageSize` and `_totalPages` — so a slow earlier query could overwrite a faster later
  one, and the new `PageSizeChanged` would have reported the stale size to the host on top of that. Each
  load now carries a generation id and applies its result, clears the loading flag and reports the page
  size only while it is still the newest query.
- **A saved view with `PageSize = 0` produced an empty table.** `ApplyViewAsync` accepted any non-null
  value; it now ignores a stored size that is not greater than zero, matching the validation on the new
  public routes.

## 2.8.11 - 2026-08-08

One defect, in one component, that turned out not to be a convenience problem. `TmSearchInput`
delivered every edit to `ValueChanged` **twice**, and with `DebounceMs` set the two deliveries were
`DebounceMs` apart — which is a broken parameter contract, not merely a redundant call.

### Fixed

- **`TmSearchInput` raised `ValueChanged` twice for a single user edit.** The `<input>` is bound to
  `ValueChanged` on **both** `@oninput` and `@onchange`. `HandleInputAsync` armed the debounce timer,
  but `HandleChangeAsync` fired immediately and never cancelled that timer, so one edit followed by a
  blur (or Enter, or autofill, or the browser's native clear cross) produced two deliveries
  `DebounceMs` apart. In a consuming application this issued two concurrent GETs for one keystroke
  sequence and made an end-to-end test flaky: the waiter was satisfied by the *second* request while
  the first was still in flight.
  - The component now remembers the value it last handed to `ValueChanged` in a private field and
    suppresses a delivery that would repeat it. The field is written on **every** dispatch path — both
    branches of `HandleInputAsync`, the timer's `Elapsed` callback, `HandleChangeAsync`, and
    `ClearValueAsync` — and only ever from the renderer's synchronization context; `Elapsed` runs on a
    thread-pool thread, so it hops through `InvokeAsync` before touching it.
  - The suppression deliberately does **not** compare against the `Value` parameter. `Value` is
    optional, and a consumer may bind `ValueChanged` alone and never supply it, leaving `Value` at
    `string.Empty` forever — a `Value`-based comparison would therefore fail to suppress the duplicate
    on exactly the bindings that produced the flake. A regression test covers the no-`Value` binding
    specifically.
  - `@onchange` **stays bound**. Dropping it would have removed the duplicate at the cost of browser
    autofill and the native clear cross of `<input type="search">`, neither of which necessarily raises
    an `input` event this component sees. `HandleChangeAsync` now cancels the pending debounce instead,
    so the edit is delivered once, immediately.
  - `ClearValueAsync` also cancels the pending debounce. Without it, a timer still carrying the old
    text would fire after the click and silently undo the clear. The clear button dispatches
    unconditionally: it is an explicit user action, and a consumer may have set `Value` externally
    since the last dispatch.
  - Consecutive identical values are now collapsed on the undebounced path too (`DebounceMs = 0` had
    the same double-delivery on blur). Consumers that counted on receiving the same search string
    twice in a row will see one call; no consumer in the library did.
  - **Suppressing a repeat does not outlive the consumer resetting `Value` itself.** Remembering the
    last dispatched value alone would have swallowed a legitimate search: after a "clear filters"
    button or a restored saved search writes `Value` from outside, the box no longer holds what was
    delivered, and retyping that same text would never have reached the consumer. `OnParametersSet`
    therefore forgets the remembered value when `Value` *changes* to anything other than what was
    last dispatched. It keys on the change, never on what `Value` currently is — a consumer that
    binds only `ValueChanged` leaves `Value` at `string.Empty` permanently, so it never changes and
    the check stays inert exactly where the suppression must keep working. A parent re-rendering
    mid-search (results arriving, a spinner toggling) is covered by its own regression test.

## 2.8.10 - 2026-08-08

Three defects that all trace back to the same element — the column pin toggle — plus the keyboard
contract 2.8.9 got half right. Making the sortable header focusable in 2.8.9 was correct, but it sent
keyboard users into a header nobody had ever been able to reach, and what was waiting there had been
broken since the pin toggle shipped.

### Fixed (accessibility)

- **Five of the nine Tab stops in a header painted nothing (WCAG 2.4.7 Focus Visible).** `ShowColumnMenu`
  defaults to `true`, so a pin `<button>` is rendered for **every visible column**, sortable or not, with
  no `tabindex="-1"` — it is a native focus stop. The only rule that made it visible was
  `.tm-data-table th:hover .tm-col-pin-btn`; the stylesheet contained no `:focus` or `:focus-within` rule
  for it at all. A five-column header is therefore nine Tab stops, five of which a keyboard user cannot
  see. It is now revealed by `th:focus-within` and by its own `:focus-visible`, next to the existing
  `:hover` — not instead of it.
  - The button deliberately **keeps** its place in the tab order. Adding `tabindex="-1"` would have
    removed the five invisible stops by making the control keyboard-inaccessible, trading a 2.4.7 failure
    for a 2.1.1 one. Whether a grid should expose one stop per header at all (roving `tabindex`, arrow-key
    navigation, one stop for the whole table) is a redesign of the keyboard model, not a fix.
- **Tempo now ships its own focus ring for every element the table makes focusable** — the sortable
  `<th>`, the pin button, and `tbody tr` (rows are `tabindex="0"` unconditionally). 2.8.9 turned the
  header into a control but left its focus indicator to whatever the consuming application declared
  globally. That turned out not to be a theoretical gap: measured on a live page in an application that
  *does* declare `*:focus-visible { outline: 2px solid … }`, a focused sortable header still computed
  `outline-width: 0px` with `outline-color: currentcolor` — the initial values — while an `<a>` on the
  same page got the full 2px ring. Adding the rule below raised the same element to
  `2px solid rgb(79, 70, 229)`. Whatever suppresses the global rule inside the table, a component that
  turns an element into a control cannot depend on the host to make focus visible. `.tm-col-sortable`
  also answers `:focus-visible` with the same emphasis it already gave `:hover`.
- **Space on a sortable header sorted the table *and* scrolled the page away from the result.** The
  header is a `<th tabindex="0">`, not a `<button>`, so Space keeps its browser meaning there — scroll one
  screen — and accepting it as a sort key added to that default instead of replacing it. **Space no longer
  sorts; Enter does**, with Shift still mirroring the multi-sort modifier.
  - The scroll cannot be suppressed from Blazor. `@onkeydown:preventDefault` binds when the handler is
    registered, not per event, so it cannot look at the key; a static `true` would cancel the default
    action of **Tab** as well and trap the keyboard (WCAG 2.1.2) — strictly worse than the annoyance it
    fixes. Doing it from JavaScript would make a keyboard path depend on an interop module the consumer
    has to reference. Enter alone also matches the convention: Space activates a `button`, and this
    element is a `columnheader` inside a `grid`, where Space carries no activation meaning.
  - **This is a behaviour change from 2.8.9.** A host that documented Space as a sort key needs to say
    Enter instead.

### Fixed

- **`Align=Right` still left the header short — the 2.8.9 fix landed on the specificity half only.** With
  the cascade repaired, `text-align: right` reached the `<th>`, but the pin button sat **after** the label
  and reserved `1.1rem + 0.25rem = 21.6px` of inline space permanently, because `opacity: 0` paints
  nothing yet still takes its width. The browser was therefore right-aligning "label + invisible button",
  so the label stopped ~22px short of the content edge its own cells sat flush against — measured on a
  1440px page: cells ended at x=1398, the header text at x=1375. Left-aligned columns never showed it,
  because the slack drained to the right; it appeared on exactly the columns the 2.8.9 fix was made for.
  The pin is now positioned absolutely at the inline end of its header, so it reserves nothing, clears the
  resize handle, and reads as an overlay rather than colliding with the tail of a long label.

### Internal

- `TmDataTablePinFocusTests` (7) resolves the cascade out of the shipped `_data-table.css` for a modelled
  interaction state (header hovered / focus-within, pin hovered / focused / pinned) and asserts the
  `opacity` and `position` that actually win on specificity-then-source-order — the same technique as
  `TmDataTableAlignmentTests`, because both defects are invisible in the markup. Asserted in **both**
  directions on purpose: a permanently visible pin would satisfy "visible on focus" while being a worse
  component, so `PinButton_StaysHidden_WhenNothingIsHoveringOrFocusingTheHeader` holds the other end.
- `TmDataTableKeyboardSortTests.Space_SortsAscending` is **replaced** by
  `Space_DoesNotSort_SoItKeepsScrollingThePage`. The assertion is reversed deliberately: the old one
  encoded the defect.
- Not done, and recorded rather than dropped: `HandleRowKeyDownAsync` accepts Space for row activation and
  has the identical scroll-plus-activate problem. It is left alone in this release because row activation
  is bound to consumer navigation far more widely than header sorting, so changing it is a wider
  behavioural decision than this patch should make on its own.

## 2.8.9 - 2026-08-07

### Added

- **`TmDataTable.DefaultSortColumn` / `DefaultSortDirection`** — the order a table is in *before* the
  user touches a header. Sorting is tri-state (ascending → descending → none) and always started at
  "none", so a page whose list had previously arrived pre-sorted had no way to say so: not through a
  parameter, and not through `LayoutStore`, which persists column widths and pin state only. The only
  remaining route was to fake a header click during startup, and that is a *different* state — it
  leaves the cycle one step along, so the user's next real click clears the sort instead of reversing
  it. The parameters take a column key (`PropertyName`, falling back to `Title`) and a
  `DataTableSortDirection`, and both defaults are unchanged behaviour: with `DefaultSortColumn` null
  the table still starts unsorted.
  - The default is seeded during initialization **before** the first data load, so a server-side
    `IDataTableDataProvider` receives it as `SortColumn`/`SortDescending` in its very first query. A
    table that sorted only after fetching would otherwise show page one of the *provider's* order
    re-sorted in place — the wrong rows entirely, not merely the wrong order.
  - It is a starting state, not a floor. Clicking cycles on from it (from `Ascending`, the first click
    on that same column sorts descending and the second clears the sort), applying a saved view
    replaces it, and an unknown key simply leaves the table unsorted rather than throwing.
  - The column does not have to be `Sortable`. A non-sortable column still orders the data, it just
    does not advertise `aria-sort` or respond to clicks — which is how you express "this list has a
    fixed order the user cannot change".
- **`TmPagination.ShowInfo`** (default `true`) — whether the pager renders its own "X–Y of Z" label.
  Standalone pagers keep it; a host that already states the range beside the pager turns it off.
- **`TmDataTable.ResetPageAsync()`** — the public way back to page one, for a host that narrows the
  result set itself. A page that owns its filter surface (`ToolbarMode=ContentOnly`, feeding the table
  pre-filtered `Items` or a `DataProvider`) hands over an already-narrowed list, and the table cannot
  tell a new query from the same list arriving again: `RefreshClientItems` only clamps the current page
  **down** to the new last page. So searching while on page 3 left the user on page 3 — reading the tail
  of the new results — and the reset built into the table's own search box is never reached, because the
  host did the searching. There was no public member that could fix it either: `GoToPageAsync` is
  private, and handing the table `SearchText` to trigger its reset has side effects (it switches on the
  built-in client-side filtering over the host's server-side filter).
  - Deliberately **not** automatic on an `Items` reference change: a table re-polling the same list on a
    timer would then yank a reading user back to page 1 on every refresh. The host knows which change is
    a new query; the table does not.

### Fixed (accessibility)

- **Sorting was mouse-only (WCAG 2.1.1 Keyboard).** The click handler sits on the `<th>`, which is a
  plain element — no `tabindex`, no key handler — so a keyboard user could not sort a table at all. The
  sortable header is now a focus stop and answers **Enter** and **Space**, with **Shift** mirroring the
  multi-sort modifier of Shift-click; `aria-sort` already carried the state. The target stays the `<th>`
  itself rather than becoming a `<button>`: the header also hosts the consumer's `HeaderTemplate`, the
  pin toggle and the resize handle, so a wrapping button would nest interactive elements. Non-sortable
  headers are not focus stops. The pin toggle now stops keydown propagation, so operating it with the
  keyboard does not also sort the column. The global `:focus-visible` ring covers the new focus stop.

### Fixed

- **`TmDataTable` printed the item count twice.** The paging footer rendered the range in the table's
  own summary (`pagination-summary`) *and* again inside the embedded `TmPagination`
  (`pagination-info`), side by side, so every paginated table showed "Showing 1–10 of 22   1–10 of 22".
  2.8.8 flagged this as knowingly not done, because removing either one is a visual change to a
  shipped component; it is done now, and the consumer picks which one via the new
  **`TmDataTable.PaginationInfoPlacement`**:
  - `Summary` (**default**) — the table's summary on the left of the footer. This is the placement
    that honours `PaginationInfoTemplate` and the `TmDataTable_ShowingItems` resource, so the wording
    a host had already customized is the wording that survives.
  - `Pagination` — the range moves into the pager itself (`TmDataTable_Pagination` resource), matching
    how a standalone `TmPagination` looks.
  - `None` — page controls only.

  Exactly one of the two `data-testid`s now exists in the DOM at a time, which is also what makes the
  duplication impossible to reintroduce silently.
- **The same duplication in `TmMultiViewList`**, which embeds `TmPagination` under its own identical
  summary. It gets the same `PaginationInfoPlacement` parameter with the same default. Its per-group
  mini-pagers are untouched: they have no sibling summary, so they keep stating their own range.
- **`TmDataTableColumn.Align` did nothing inside a data table.** The markup was never at fault — the
  table has always put `tm-text-center` / `tm-text-right` on both the `th` and its `td`s. The
  stylesheet discarded them: `.tm-data-table th, .tm-data-table td { text-align: left }` is a class
  plus a type (specificity 0-1-1) while the helpers `.tm-text-right` / `.tm-text-center` are a bare
  class (0-1-0), so the base rule won every column regardless of source order. Applications did not
  notice in the body because a right-aligned cell usually holds its own flex container that aligns
  itself; a header is bare text, so an actions column read `Align="Right"` and rendered its cells
  right but its **header left**. The helpers are now re-stated at the table's own specificity, so
  `Align` reaches the header and the cells together.

### Internal

- 42 new bUnit test cases: `TmDataTableDefaultSortTests` (13, including the two server-side cases that
  assert the default reaches the *first* provider query), `TmDataTableAlignmentTests` (9 cases from 5
  methods), `TmDataTableKeyboardSortTests` (8), `TmDataTableResetPageTests` (5),
  `TmDataTablePaginationTests` (+5), `TmPaginationTests` (+2). The `DataTable` namespace goes from 246
  to 288 tests.
- `TmDataTableResetPageTests` pins the regression itself, not only the fix: one test asserts that
  without `ResetPageAsync` a narrowed result set leaves the user on the clamped last page, so the fix is
  measured against the behaviour that was actually there.
- The alignment fix is CSS-only, so a class assertion could not have caught it — the class was always
  present. `TmDataTableAlignmentTests` therefore resolves the cascade out of the shipped
  `_data-table.css`: it parses the rules, matches them against a modelled
  `<table class="tm-data-table"> … <th class="tm-text-right">` chain, and asserts the `text-align`
  that actually wins on specificity-then-source-order. Mutation-checked: deleting the four-line fix
  from the stylesheet turns **exactly** the four cascade assertions red, and they report `left` — the
  real defect — while the class-list assertions stay green, which is precisely the blind spot they had.
- `JsonDocumentation` updated for the MCP server: `DefaultSortColumn`, `DefaultSortDirection` and
  `PaginationInfoPlacement` on `TmDataTable`, `ShowInfo` on `TmPagination`, `PaginationInfoPlacement`
  on `TmMultiViewList`.
- Not done, and unchanged from 2.8.8: `TmMultiViewList` and `TmTreeList` still do not derive from
  `TmComponentBase`, so their embedded pagers have no `TestIdPrefix` to forward and emit the bare
  `pagination-*` ids.

## 2.8.8 - 2026-08-07

### Added

- **`TmPagination` is addressable from the outside.** It had no test hook of any kind: the only way
  to reach the prev/next buttons, a specific page button or the item-range text was to select on the
  presentational CSS classes (`.tm-pagination-next`, `.tm-page-btn:nth-child(n)`), which makes a
  consumer's suite break on any styling refactor and gives it no way to tell two pagers apart. The
  component now derives from `TmComponentBase` and renders the library's `data-testid` convention:
  `pagination` on the root, `pagination-info` on the item-range text, `pagination-prev` /
  `pagination-next` on the step buttons, `pagination-page-{n}` on each page button, and
  `pagination-page-size` on the page size `<select>`. `TestIdPrefix` namespaces all of them
  (`users-pagination-next`), `DataTestId` overrides the root id only. With neither set the bare ids
  are used, so this is additive markup — no existing selector, class or style changes.
- **`TmDataTable` propagates the hooks into its built-in pagination.** This was the actual blocker:
  the table splats `AdditionalAttributes` onto its own wrapper but passed nothing to the embedded
  `TmPagination`, so a consumer that had migrated its own pager to `TmDataTable` lost every handle on
  the paging controls. The table's `TestIdPrefix` is now forwarded to the pagination, and the paging
  footer carries `pagination-container` (the footer row) and `pagination-summary` (the table's own
  "showing X–Y of Z" text, which is a *different* element from the pagination's `pagination-info`).
- **`TmDataTable.PaginationAttributes`** — a `Dictionary<string, object>?` splatted onto the built-in
  pagination's root element, for host-owned attributes that are not test ids (analytics ids, extra
  ARIA).
- **`TmDataTable.PaginationInfoTemplate`** — replaces the built-in `TmDataTable_ShowingItems` summary
  with host markup. The context is the new `DataTablePaginationInfo` record (`CurrentPage`,
  `TotalPages`, `PageSize`, `TotalCount`, `StartItem`, `EndItem`), so an application migrating its own
  table to `TmDataTable` can keep its established wording — e.g. "Page 1 of 3 (22 records)" — instead
  of being forced onto the library's phrasing. The resource key stays the default when the template is
  null; no localization keys were added or changed in this release.
- **The active page button is marked `aria-current="page"`.** It previously carried only the
  `tm-page-btn-active` class, so assistive technology had no way to announce which page is current,
  and a test had to assert on a styling class to find it.

### Internal

- Per-group mini-pagers (server-side group paging) get `TestIdPrefix` = `{table prefix-}group-{group
  key}`, so several group pagers on one table stay individually targetable instead of all answering
  to `pagination-next`.
- 11 new bUnit tests (`TmPaginationTests`, `TmDataTablePaginationTests`). Verified: the whole
  `Tempo.Blazor.Tests` suite is green at **9272/9272** on net10.0 with the change in.
- Mutation-checked, because "the ids are there" is easy to assert vacuously: removing the
  `TestIdPrefix="@TestIdPrefix"` propagation from `TmDataTable.razor` turns **exactly one** test red
  (`DataTable_TestIdPrefix_IsPropagatedIntoTheBuiltInPagination`) and leaves the other 245 DataTable
  tests green — the prefixed-id assertions are the only thing holding that wire, and they do hold it.
- Not done, and it is a real gap: `TmMultiViewList` and `TmTreeList` also embed `TmPagination` but do
  not derive from `TmComponentBase`, so they have no `TestIdPrefix` to forward. Their pagers emit the
  bare `pagination-*` ids. A page that puts one of them next to a `TmDataTable` must set
  `TestIdPrefix` on the table, otherwise the two pagers answer to the same ids.
- Also not done: inside a `TmDataTable` the item range is rendered **twice** — once by the table
  (`pagination-summary`) and once by `TmPagination` itself (`pagination-info`). That predates this
  release; removing either one is a visual change to a shipped component and did not belong in a
  test-hook release. The two ids at least tell them apart now.

## 2.8.7 - 2026-08-02

### Fixed

- **`TmTabs` had half a roving tabindex.** `ArrowRight`/`ArrowLeft` moved the *selection* —
  `aria-selected` and the `tabindex` 0/-1 pair — but never moved the **DOM focus**, so after a key
  press the browser focus was still sitting on the tab the user had just left, on an element that
  now carried `tabindex="-1"`. Measured before the fix on the demo strip:
  `selected=tab-details, activeElement=tab-overview`. Consequences for a keyboard user: the focus
  ring and the screen-reader cursor stay behind, the next arrow key is dispatched from the wrong
  element (so two `ArrowRight`s advance **one** tab, not two), and the next `Tab` leaves the strip
  from the wrong place. The tab buttons now carry an element handle and the newly selected one is
  focused after the render that re-assigns the `tabindex` values.
- **`Home` and `End` were not handled at all** — the key handler had a bare `else return` after the
  two arrows. They now jump to the first/last *selectable* tab, scanning inwards from the edge so a
  disabled first/last tab is stepped over rather than swallowing the key, which is the same rule the
  arrows already followed.
- **A REFUSED CLICK left the focus ring on the refused tab.** The browser focuses a clicked button
  before any component code runs, so on a strip whose consumer rejects the change the ring stayed on
  a tab that is neither `aria-selected` nor in the tab order — measured:
  `activeElement=#tab-veto-second, selected=[tab-veto-first], tabindex0=[tab-veto-first]`. Same
  divergence as the keyboard one, reached through the pointer. `SelectTab` now claims the same focus
  move the keys do, which is a no-op when the change is accepted and pulls the ring back when it is
  not. Without this the release's own invariant would have been true of the keyboard only.

### Internal

Test counts below are exact: **45** bUnit tests cover the component (25 `TmTabsTests` + 18
`TmTabsHeaderSlotsTests` + 2 `TmTabsAccessibilityTests`; `--filter ~TmTabs` reports 46 because it
also matches one method in `Wireframe.TempoPackStructurePhase10Tests`), and
`TmTabsKeyboardFocusE2ETests` has **8** `[TestMethod]`s.

- New `TmTabsKeyboardFocusE2ETests` measures the contract at `document.activeElement` in a real
  browser and sends its keys through `IKeyboard` (i.e. to whatever holds focus), never at a located
  element. This closes a whole class of vacuously-green tests: *every* assertion phrased over the
  rendered DOM stays green when the focus move is reverted, because the selection really does move.
- Mutation results, all measured against the shipped implementation:
  - removing the focus move entirely — **45/45 bUnit green**, **7 of 8** e2e red, each on the focus
    assertion with the selection assertion already passed;
  - leaving `Home`/`End` unhandled — **4** bUnit red, **2** e2e red, all on the selection floor;
  - focusing the tab the interaction *requested* instead of the one the consumer confirmed —
    **45/45 bUnit green**, **2** e2e red (both veto arms);
  - reverting only the click's focus claim — **45/45 bUnit green**, **1** e2e red (the click veto
    arm), reproducing `selected=#tab-veto-first activeElement=#tab-veto-second`.
- `HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport` is **not** a focus discriminator and
  must not be counted as one: it is the single method that stays green when the focus move is
  removed, because the browser's own scroll-into-view reaches the rectangle it asserts. It
  discriminates on key handling and on the scroll bound only.
- `TmTabsHeaderSlotsTests` keeps its frozen pre-slot markup hash. The `@ref` needed for focus makes
  the renderer stamp an element-reference marker on each tab button (measured: 1458 bytes vs the
  frozen 1269, i.e. exactly 3 × 63 characters and nothing else); its value is a fresh GUID on every
  render, so it is normalised away before hashing — with the removal **count** asserted, so a
  normaliser that swallowed a second change could not hand back a green hash.
- `TmTabsHeaderSlotsTests` keeps its frozen pre-slot markup hash. The `@ref` needed for focus makes
  the renderer stamp an element-reference marker on each tab button (measured: 1458 bytes vs the
  frozen 1269, i.e. exactly 3 × 63 characters and nothing else); its value is a fresh GUID on every
  render, so it is normalised away before hashing — with the removal **count** asserted, so a
  normaliser that swallowed a second change could not hand back a green hash.

### Known gaps

Both are deliberate, and neither is a regression — before this release the arrow keys moved no
focus at all and `Home`/`End` did nothing.

- The arrow keys are **not mirrored in RTL**: the handler compares physical key names, so in a
  right-to-left strip `ArrowRight` moves towards the end of the panel list rather than towards the
  tab drawn to its right. Left out because nothing hands a Tempo Blazor component a direction to
  mirror against. Population: **3090** own `.razor`/`.cs`/`.css` files under `src/` (excluding
  `obj/`, `bin/`, `wwwroot/lib/`); needles `dir="rtl"`, `[dir=`, `:dir(`, `direction: rtl`, `IsRtl`
  → **0** hits. That population deliberately does **not** cover the 530 `.mjs` files under `src/`,
  and RTL machinery does live there, *inside* a Blazor package: the document editor ships a Unicode
  bidi resolver (`Tempo.Blazor.DocumentEditor/wwwroot/js/document-editor/layout/bidi.mjs`,
  `core-engine/bidi-line.mjs`) and stamps `dir="rtl"` on spans it renders
  (`render/atomic-renderer.mjs:306`). It is the editor laying out its **own** canvas text, not a
  direction context another component can read — 16 `.mjs` files match `rtl|RightToLeft|bidi` and
  none of them is reachable from a tab strip. (The reporting engine's `ReportTextDirection` is a
  separate stack again, and its `HbDirection` is an alias for `HarfBuzzSharp.Direction`, not a
  product type.) A lone direction-aware component would have nothing to test it against.
- **`Home`/`End` do not suppress the browser's default scroll.** On a long page the viewport jumps
  before the focus call brings the strip back. Bounded by
  `HomeScrollsThePage_ButLeavesTheFocusedTabInsideTheViewport`, which reproduces the **settled**
  scroll position (`2437`) and the focused tab's rectangle (`top=429`/`bottom=471` in a 900px
  viewport); the positions on the way there vary run to run (`346 → 1788` and `285 → 1872` were both
  observed) and are logged, never asserted. So the strip is never lost, but the scroll is visible.
  Suppressing it means `@onkeydown:preventDefault`, which *does* accept a bool expression — **6**
  places in Tempo use that form (`TmRichEditorSimple`, `TmMarkdownEditor`, `TmRichEditorFull`,
  `TmSigningFieldOverlay`, and `TmDocumentPageViewer` twice) out of **187** `:preventDefault=`
  directives under `src/`, the other **181** being the literal `"true"` — but the expression is
  evaluated at **render** time and so cannot
  depend on which key was pressed. It would have to apply to every keydown on the tablist,
  swallowing `Tab` and trapping focus inside the strip: a worse defect than the one it fixes.

### Contract

- **`TmTabs` is a controlled component, and the focus now obeys that — on both input paths.** A key
  or a click *asks* through `ActiveTabIdChanged`; the focus target is read back off `ActiveTabId`
  **after** the render, never from the tab the interaction requested. A consumer that ignores or
  vetoes the change therefore keeps both its selection and its focus ring on the same tab. Focusing
  the requested tab would have re-created the exact focus/selection divergence this release removes,
  mirrored. Pinned by `AVetoedChange_LeavesTheFocusOnTheStillSelectedTab` (keyboard) and
  `AVetoedCLICK_PullsTheFocusBackToTheStillSelectedTab` (pointer) against a demo strip whose handler
  rejects everything, each with the accepting strip driven by the same interaction first as a
  liveness floor — under the "no focus at all" mutation it is that floor that fails, so a dead page
  cannot pass either arm vacuously.
  The bound: the claim is resolved on the first render after the interaction and not carried
  further. A consumer that resolves the change asynchronously past that render still gets a focus
  move, resolved against the selection as it stood then — i.e. onto the tab that is still selected,
  a no-op rather than "nothing happens"; when its update lands, the selection moves and the focus
  does not follow.

## 2.8.6 - 2026-08-01

### Fixed

- **The CSS bundler was destroying every `calc()` that adds.** `BundleCss` stripped the whitespace
  around `+` as if it were only a sibling combinator, so `calc(100% + 8px)` shipped as
  `calc(100%+8px)` — invalid per css-values-3 §8.1, which makes the browser drop the **whole**
  declaration. Measured in the committed bundle: **30 such operators across 29 `calc()`
  expressions**, and **0** in the 975 `calc()` expressions of the sources — the defect was made
  entirely by the minifier. The worst of them was `.tm-data-table-scroll`, where the negative margin
  survived but the matching width compensation did not, so **every data table overflowed its
  container**; the rest were dropped `top:`/offset declarations on tooltips, popovers, dropdowns and
  the date picker, which fall back to `auto` and land at the static position instead of the intended
  offset. `+` and `~` are no longer stripped (leaving the space around a combinator is valid CSS and
  costs ~96 bytes); `>` still is, because it is not a `calc()` operator.
- The bundle went unmeasured for a structural reason, now closed: the demo app links the **source**
  `tempo-blazor.css` through its `@import` graph, so no e2e run ever loaded
  `tempo-blazor.bundled.css` — the file the documentation tells consumers to reference. New
  `CssBundleCalcWhitespaceTests` asserts on the **bundle**, tokenising each `calc()` rather than
  pattern-matching it, so `--tm-space-1-5`, `env(safe-area-inset-bottom)`, `calc(-1 * x)` and `1e-5`
  are not mistaken for operators (18 such control samples are pinned alongside 14 defective ones).

### Changed

- `--tm-tab-row-height` is derived with `calc()` again instead of the four per-variant literals it
  carried while `calc()` was broken in the bundle. The literals only held while the root font size,
  the tab padding and the line box all stayed at their defaults: with `:root { font-size: 20px }` a
  Line row measures `2×12.5 + 25 + 2 = 52px`, while the literal still claimed 42px — a centred slot
  then sits 5px off the row. `.tm-tab` now reads its padding and line box from
  `--tm-tab-padding-y`/`-x` and `--tm-tab-line-box`, which is also what the row height is computed
  from, so the two cannot drift apart. Because var() substitution happens at computed-value time on
  the `.tm-tabs` element itself, the token now has a **single** declaration and the variants
  re-declare only the inputs — replacing four same-specificity literals whose winner was decided by
  their order in the file. Rendered geometry is unchanged: `TmTabsHeaderSlotGeometryE2ETests`
  measures slot boxes of 42/38px (Line above/below the 640px breakpoint), 44px (Pill) and 37px
  (Enclosed) with a **0.00px** alignment delta in all 12 measurements.

### Internal

- **An ordinary `dotnet test` can no longer overwrite a committed screenshot.** Two separate routes
  reached them and only one was obvious. The *generators* — classes that exist to rewrite the PNGs —
  were held back by nothing but a doc comment, and three of the seven (`DebtTokenBaselineScreenshots`,
  `ThemeTokenBaselineScreenshots`, `SpreadsheetPhase6BaselineScreenshots`) did not even carry the
  `BaselineGeneration` category. They now inherit `BaselineGeneratorTestBase` and skip unless
  `TM_WRITE_BASELINES` is `1`/`true`/`yes`.
- The larger route was *ordinary* tests writing baselines as a side effect: `NotionE2ETestBase` and
  four Notion test classes each carried a byte-identical private path calculation that wrote
  straight into `__baseline__/notion/`, reaching **765 committed PNGs — 66% of every tracked PNG in
  the repository — from 276 call sites**. A single ordinary test rewrote 12 of them. These are not
  skipped, because they assert real behaviour; their destination is REDIRECTED instead, through
  `BaselineOutput`, exactly as `Demo__DiagramsDbPath` redirects the demo database.
- Holding both routes is `BaselineWriteSweep`, whose predicate is the **write target** rather than a
  class name: it hashes every tracked PNG under the screenshot roots in `[AssemblyInitialize]` and
  fails the run from `[AssemblyCleanup]` if any changed without the opt-in. A guard keyed on naming
  could not see any of those five Notion classes; a guard keyed on what the run touched cannot be
  defeated by renaming. `BaselineGeneratorGateTests` remains as the fast, browser-free check on the
  naming convention itself.
- The demo API no longer writes into the committed `src/Tempo.Blazor.Demo.Api/diagrams.db`: the path
  is overridable through `Demo__DiagramsDbPath`, which the Playwright host launcher points at a
  per-run temp directory. Booting the demo from a test run previously left that file modified plus
  an untracked `-wal`/`-shm` pair.

## 2.8.5 - 2026-08-01

### Added

- **`TmTabs` gains `HeaderLeading` and `HeaderTrailing` slots.** Content supplied to either slot is
  rendered beside the tab strip as a **sibling** of the `role="tablist"` element, never inside it:
  the strip runs a roving tabindex (arrow keys, `tabindex` 0/-1, `aria-selected`), so an element
  that can never be selected would both misreport the strip's contract to assistive technology and
  become arrow-key reachable. The slots therefore add nothing the roving focus walks over.
- The wrapping `.tm-tabs__header-row` flex row is emitted **only** when at least one slot is
  supplied. With neither slot the rendered markup is byte-identical to the pre-slot component
  (measured, not asserted by eye: `TmTabsHeaderSlotsTests` compares a sha256 of the rendered markup
  against a hash frozen before the slots existed), so the strip stays a direct child of `.tm-tabs`
  and every consumer selector shaped `.tm-tabs > .tm-tabs__header` keeps matching.
- Consumers with a **sticky** tab strip must stick the row, not the header — pinning
  `.tm-tabs__header` alone leaves the slot content free to scroll out from beside it. The Line and
  Enclosed variants move their underline onto the row when it exists, so the rule spans the slots
  instead of stopping at the edge of the strip.
- The slots work with `Wrap="true"`. A wrapped strip is a multi-row band, so two things follow and
  both are enforced: the row drops its bottom border (every wrapped row already draws its own
  baseline — keeping it left a rule stranded 41px below the active indicator and a doubled line
  under the last row), and the slots are aligned to the **first** row of the band rather than
  centred over the whole of it.
- The Pill variant centres its header row instead of stretching it. Line and Enclosed stretch so
  their tabs meet the underline drawn on the row; Pill has no underline, and stretching let a 60px
  slot drag the grey tray from 44px to 60px and the pills from 36px to 52px.
- Layout of the slots is guarded by `TmTabsHeaderSlotGeometryE2ETests`, which measures bounding
  boxes in a real browser on both sides of the 640px breakpoint. It takes no screenshots, so the
  nightly baseline lane neither feeds nor gates it.

## 2.8.4 - 2026-07-29

### Added

- **Durable MCP idempotency boundary for Notion authoring.** `INotionIdempotentAggregateProvider`
  lets a host commit the complete response receipt of `notion_apply_block_operations` in the same
  transaction as the aggregate writes the request performs, so a host restart between the write and
  the receipt can no longer produce a second application of the same logical request. Hosts that do
  not implement it keep using the in-process receipt store unchanged. This shipped as 2.7.1 on a
  branch that never reached `main`; it is carried here so the line is single again.
- **`notion_apply_block_operations` accepts `scopeAppId`.** A stateless MCP call carries no ambient
  application scope, so a caller whose credentials reach more than one application could not use the
  tool at all against a host that scopes data per application — every call failed before touching
  the data, while `notion_create_page` and `notion_list_pages` had taken `scopeAppId` since 2.7. The
  value reaches the host as `NotionIdempotentExecutionRequest.ScopeAppId` and stays OUT of the
  request hash: it says where the write lands, not what it writes, and receipts are already scoped
  per application, so an unchanged single-application caller keeps replaying its existing receipts.

## 2.8.3 - 2026-07-26

### Fixed

- Headless document layout now preserves nested inline drawing payloads and emits their image
  display commands, so drawn signatures and other inline images render in exported PDFs with their
  requested geometry.

## 2.8.1 - 2026-07-26

### Fixed

- `DocumentTokenHelper.ExtractTokens` now traverses tables, nested block content controls and inline
  content controls, so headless document assembly discovers the same tokens as visual previews.
- Document-assembly conditions now interpret raw `true` and `false` token values as booleans instead
  of treating every non-empty string as true.
- `DocumentTemplatePreviewService` now uses the public recursive token helper, removing a divergent
  private extraction implementation.

## 2.8.0 - 2026-07-25

### Both dark-theme switches, hover you can actually see, and ink that survives the dark fill

> **What this section covers, and what is missing from this file.** The entries below describe the
> changes of THIS release only. **Versions 2.6.0, 2.6.1 and 2.7.0 were tagged and released but have
> no entry in this changelog** — nobody wrote one, and this release does not reconstruct them. So the
> step from `## 2.8.0` straight to `## 2.5.5` below is a *gap in the document*, not a renumbering:
> upgrading from 2.5.x picks up three undocumented releases as well as everything listed here. Read
> `git log v2.5.5..v2.8.0` for those. The version number is 2.8.0 (not 2.5.6) because it has to be
> higher than the already-published 2.7.0, and a minor because a new public token is added.

### Changed — behaviour

**`--tm-color-on-primary` is no longer white; it follows the theme.** It resolves through
`--tm-text-inverse`, so it stays white in light but becomes the dark ink `#0f172a` in the dark theme —
and with it every "ink on a primary fill" in the library. This is a visible change for any consumer
who is on the dark theme, and a *breaking* one for anyone who built their own rule on the assumption
that the token is white, or who hardcoded `white` next to a primary fill; such a rule now paints
white ink on a light `primary-400` box at 2.54:1 while the rest of the library flips to dark ink.
`--tm-color-primary-contrast` and `--tm-control-glyph-color` become aliases of it, so the three names
are one source instead of three copies that drifted apart.

The reason is a contrast failure, not a preference: the dark theme repoints `--tm-color-primary` to
the lighter `primary-400`, on which white text measures **2.54:1** — under the 4.5:1 SC 1.4.3 asks of
body text. Dark ink on it is **7.02:1**. `.tm-btn-primary` already had that fix in a per-component
dark block; that block is gone now, because a shadowed patch left behind is what the next reader
takes for the rule. The flip reaches every site at once and through **both** theming APIs: the filled
primary button, the `TmMultiSelect` confirm button and option tick, the `TmCheckbox` tick, the
`TmChat` outgoing bubble / send / edit-save buttons, the NotionEditor primary buttons and audit
panel, the Spreadsheet resize dialog and the DocumentEditor toolbar. Light is untouched by
construction — `--tm-text-inverse` is white there.

`.tm-btn-danger` keeps its own dark rule: its fill is `--tm-color-danger`, not primary, and there is
no `--tm-color-on-danger` yet. The collaborative cursor label was DISCONNECTED from this token
instead — it paints on `--tm-wysiwyg-remote-cursor-color`, a per-user runtime colour, so it takes a
paired `--tm-wysiwyg-remote-cursor-ink` and keeps the theme-independent white it was built on.

### Added

- **`--tm-control-hover-fill`** — the tint a selection control takes while hovered and unselected
  (`primary-100` light, `primary-900` dark). It resolves from the primary scale, so a consumer that
  repoints that scale gets a matching tint automatically and must NOT redeclare it per accent.

### Fixed

**The appearance of several components changes in the DARK theme only.** The light theme is unchanged
by construction: every new value resolves to exactly what it resolved to before in light.

- **`.tm-dark` now really is equal to `[data-theme="dark"]`.** Both are documented as public switches,
  but 93 dark rules across 15 stylesheets listed only the attribute — a consumer toggling the theme
  with the CLASS kept light colours in `TmButton`, `TmChat`, `TmDataTable`, `TmScheduler`, `TmStepper`,
  `TmValidationSummary`, `TmAIPrompt`, the PDF viewer and the diagram editor. The drift also ran the
  other way: 34 rules in the six NotionEditor database views were written class-first and never
  reached an attribute consumer. Both directions are fixed and both are now guarded by
  `DarkThemeSelectorParityTests`, which sweeps every shipped stylesheet (scoped `.razor.css`
  included) rather than the files that happened to be broken.
- **`TmCheckbox` / `TmRadio` hover is now an AREA change, not a hue change.** Strengthening the
  resting outline in 2.5.4 had a side effect: hover re-coloured that same outline from `gray-500` to
  `primary-600`, two near-isoluminant tones, so the pointer feedback measured **1.07:1 in light and
  1.01:1 in dark** — 1.00 in greyscale, i.e. nothing for a reader who does not separate those hues.
  And it could not be fixed by picking a better outline colour: the outline has to stay above 3:1
  against the surface in BOTH states, which leaves no pair of outline colours that also differ enough
  from each other. So the outline **grows from 1.5px to 2.5px** on hover. The ring it gains replaces
  box fill with the outline colour — **5.17:1 light / 5.75:1 dark** against what it covered, over the
  3:1 that SC 1.4.11 asks of the information identifying a component's state. No new colour, no
  layout shift (`box-sizing: border-box` with a fixed 1rem box), and it survives greyscale and
  colour-vision deficiency.
  The new `--tm-control-hover-fill` adds a tint on top; that is cosmetic, not the affordance.
  `border-width` joined both controls' `transition`, and `TmRadio` also gained the `background-color`
  it was missing — without them the ring would snap while the tint faded, and the radio would have
  flickered where the checkbox faded.
  Both declarations are scoped to `:not(:checked)` so a selected control keeps its fill, its glyph
  and its ring. The **mixed** checkbox needed separate handling, because it is not `checked` at all —
  `Indeterminate` renders `checked="false"` and marks the box with a class — so that guard does not
  reach it and the pale tint would have covered the mixed fill: the dash would have dropped from
  5.17:1 to 1.22:1 in light and 7.02:1 to 1.72:1 in dark, leaving a hovered mixed box looking exactly
  like a hovered empty one. A rule next to the mixed fill takes the fill back on hover. It does not
  reset the thicker ring, and that is deliberate: on a filled box the outline and the fill are the
  same token, so the ring paints primary over primary at 1.00:1 — the growing ring is the affordance
  of the UNCHECKED state, which is also why the checked box is excluded from it. `TmRadio` has no
  third state and needs nothing extra.
  A ring is used rather than an inset `box-shadow` on purpose: `:focus-visible` already owns
  `box-shadow` and shadows do not accumulate across rules, so hover+focus together would have
  silently dropped one of them.
- **`TmMultiSelect` confirm button.** Its label was a hardcoded `white`, bypassing the token graph
  entirely, and its hover repainted `primary-600` — which IS `--tm-color-primary` in light, so the
  button had **no hover feedback at all** in the light theme, and in dark it moved to a darker shade,
  the wrong way. It now takes `--tm-color-on-primary` and `--tm-color-primary-hover`: 5.17:1 → 6.70:1
  in light, 7.02:1 → 9.90:1 in dark.
- **`TmValidationSummary` no longer prints the same message twice.** `GetValidationMessages()` walks
  the store per FIELD, so one sentence attached to several fields — a cross-field rule, or a server
  response merged into the live store — was listed once per field. The summary is a per-FORM list;
  the messages are de-duplicated with first-seen order preserved, so reading and screen-reader order
  are unchanged.
- **New guard `DesignTokenDocumentationTests`** compares the token table published in
  `JsonDocumentation/gettingStarted.json` against `tokens.css`. Nothing had ever compared them and
  four values had drifted: the documentation still described `--tm-color-primary` as `primary-500`
  (it is `-600`), the hover/active aliases one step too light, and the old system font stack. A
  consumer following the documentation would have computed contrast against the wrong shade.
- `SelectionControlContrastTests` now matches selectors EXACTLY instead of by fragment. The previous
  matcher took the first rule sharing a fragment, so two of its measurements were reading the right
  declaration only by rule ordering.

## 2.5.5 - 2026-07-22

### TmCodeEditor — leaving the editor with Tab, and wrapping long lines

The editor swallowed every Tab and never wrapped. That is right for source code, but wrong for an
editor placed among ordinary form fields (a Gherkin step, a note): keyboard users could not reach
the next control, and a long sentence scrolled sideways instead of wrapping. Two opt-in parameters,
both defaulting to today's behaviour, so upgrading changes nothing until you ask for it.

- **New parameter `TrapTab` (default `true`)** — keeps Tab/Shift+Tab inside the editor as
  indent/outdent. It stays `true` on purpose: existing hosts use the component as a source editor,
  and flipping the default would silently change their keyboard behaviour on upgrade. Set it to
  `false` and Tab moves focus to the next control (no `preventDefault`), at the cost of losing
  keyboard indentation. The flag reaches the script through `tmCodeEditor.init` and can be changed
  later through the new `tmCodeEditor.setTrapTab`; a caller that omits it keeps trapping Tab.
- **New parameter `Wrap` (default `false`)** — wraps long lines instead of scrolling horizontally.
  It adds the `tm-code-editor--wrap` modifier, which switches the textarea AND the highlight
  overlay to `white-space: pre-wrap` together (wrapping only one of them would drift the highlight
  against the caret), breaks over-long tokens and drops the now-pointless horizontal scroll.
- The wireframe stencil exposes `wrap` as a component prop, so a wireframe can state whether an
  editor wraps prose or scrolls code sideways.

## 2.5.4 - 2026-07-22

### Selection controls — non-text contrast (WCAG 2.2 SC 1.4.11) in both themes

**The appearance of three components changes**: `TmCheckbox`, `TmRadio`/`TmRadioGroup` and the
`TmMultiSelect` option checkbox. A selection control carries its whole state in two graphical
objects — the outline when nothing is selected, the glyph on the filled box when something is —
and both were below the 3:1 minimum. Measured from the token graph, before → after. The outline
numbers are identical for all three — they shared the same declaration. The glyph rows apply to the
checkbox and the multiselect option; a radio marks its state with a filled dot, not a glyph, and
that dot is unchanged (5.17:1 light / 5.75:1 dark against the box):

| | before | after |
|---|---|---|
| outline vs. its own surface (dark) | 1.41:1 | **5.71:1** |
| outline vs. its own surface (light) | 1.24:1 | **4.83:1** |
| outline on `--tm-bg-muted` (dark / light) | 1.00:1 / 1.13:1 | **4.04:1 / 4.39:1** |
| glyph on the filled box (dark) | 2.54:1 | **7.02:1** |
| glyph on the filled box (light) | 5.17:1 | 5.17:1 (unchanged) |
| indeterminate dash (light / dark) | 1.00:1 / 14.63:1 on an unfilled box | **5.17:1 / 7.02:1** |

- **New token `--tm-border-color-control`** (`--tm-color-gray-500` light, `--tm-color-gray-400`
  dark) — the boundary of a control whose STATE that boundary conveys. `--tm-border-color` keeps
  its meaning as the decorative divider tone and is unchanged, so nothing outside these three
  controls moves. Both use sites carry the fallback `var(--tm-border-color-control,
  var(--tm-border-color))`, so a stale token file cannot turn the whole `border` shorthand invalid
  and leave the control with no outline at all.
- **New token `--tm-control-glyph-color`** (white light, `--tm-text-inverse` dark) — the glyph on a
  filled selection control. On dark the fill is the lighter `primary-400`, on which white is
  2.54:1; dark ink on it is 7.02:1, the rule the filled primary button and the filled badge already
  follow. Flipping it in `tokens-dark.css` covers both theming APIs (`[data-theme="dark"]` and
  `.tm-dark`) and all three components at once. The multiselect option checkbox previously
  hardcoded `color: white`, which bypassed the token graph entirely.
- **`TmCheckbox` indeterminate state fixed.** The mixed state was styled through
  `.tm-checkbox-input:indeterminate`, a pseudo-class that only matches when something sets the
  input's `indeterminate` DOM property — nothing ever did. The box therefore stayed unfilled and
  the white dash was invisible in the light theme (1.00:1). The fill is now keyed off a class the
  component renders, and the input carries `aria-checked="mixed"`, without which the state was
  indistinguishable from unchecked for assistive technology.
- Guarded by `SelectionControlContrastTests`, which resolves the `var()` chains out of the token
  files and recomputes the ratios for all three controls, so reintroducing a failing colour further
  up the graph fails the build too.
- **Known gap, not covered here**: these ratios are Tempo's own tokens. An application that
  repoints the primary scale (per-accent themes) has to re-measure the glyph against ITS fills —
  the dark glyph token is what makes that possible, but no test in this repo can see those tokens.

## 2.5.1 - 2026-07-19

### Applier convergence follow-up: `table.cell.text` + content-control children

- **`setBlockAttribute table.cell.text` now converges across runtimes** — the JS
  collaboration applier (`transform.mjs`) implements the cell-targeting
  semantics (resolve the TABLE block with a fallback without the cell
  preference, replace the first cell paragraph's runs, convert a non-paragraph
  first block, create a paragraph in an empty cell). Both runtimes create the
  empty-cell paragraph with the DETERMINISTIC id `{cellId}-text` — the previous
  random C# Guid was itself a cross-replica divergence. Pinned by the
  convergence fixture (replace + empty-cell create) and unit tests on both
  sides; `document_editor_set_table_cell_text` edits now co-edit live.
- **Blocks inside content controls are operation-addressable** — both appliers
  descend `ContentControlBlockContent.Blocks` like table cells (keeping the
  enclosing cell context for the `TableCellId` preference), so agents can
  fine-edit conditional chains and repeating sections
  (insert/replace/delete_text, format_range, set_heading, insert_token,
  delete/move/update_block) without whole-control replaces. The JS `moveBlock`
  was aligned: without an explicit cell id a nested block stays in its SOURCE
  container (previously it was re-homed to the body).
  `document_editor_describe_document` now reports
  `operationAddressable: true` for content-control children; only
  header/footer blocks remain non-addressable. Convergence fixture extended
  with in-control text/mark edits; addressing, operation-semantics, coverage
  and tool-catalog docs updated.

## 2.5.0 - 2026-07-19

### Document MCP tools — semantic editing compiled to operations + visual previews

`Tempo.Blazor.Mcp` now ships a complete agent-facing document tool suite
(~19 new tools next to the existing low-level ones; catalog with the addressing
and concurrency contract in `docs/document-mcp-tools.md`, guarded by
`DocumentMcpToolsDocumentationDriftTests`):

- **Introspection** — `document_editor_describe_document` (blocks with stable
  semantic addresses per `docs/document-mcp-addressing.md`, truncated text,
  tables with cell ids, tokens, content controls, headers/footers,
  `contentDigest` SHA-256 fingerprint) and `document_template_describe`
  (tokens, conditional chains, repeating sections).
- **Semantic text edits** — `insert_text`/`replace_text`/`delete_text`/
  `format_range` (17 marks)/`set_heading`/`set_paragraph_properties` with
  plain-text offset/length addressing, compiled into canonical
  insertText/deleteText/mark/setBlockAttribute operations and applied through
  the same convergence-tested pipeline as `document_editor_apply_operations`.
- **Blocks & tables** — `insert_block` (body order-value / cell index),
  `delete_block`, `move_block`, `update_block`, `set_table_cell_text`.
- **Authoring** — `document_editor_create`, `document_editor_import`
  (markdown/HTML text, DOCX/ODT base64; content replacement under the
  concurrency token) and `document_editor_export` (markdown/HTML verification
  channel, DOCX/ODT packages). `Tempo.Blazor.DocumentFormats` now multi-targets
  net8.0/net9.0/net10.0 (additive) so the MCP package can reference it.
- **Templates & assembly** — `insert_token` (incl. computed expressions and
  optional token-provider key validation), `wrap_conditional` (IF/ELSEIF/ELSE
  content-control chains + branch/expression updates),
  `insert_repeating_section` and `document_assemble_render` (token values with
  rows → PNG/PDF with IF/ELSE evaluation, repeat expansion and computed
  expressions). The JS collaboration applier now normalizes content-control
  persistence payloads (previously degraded to paragraphs) and the C#↔JS
  convergence fixture covers conditional/repeat authoring operations incl.
  `tmAssembly` metadata.
- **Visual feedback** — `document_render_preview` (per-page base64 PNGs, page
  selection, dpi, caps) and `document_render_pdf` (DocumentPdfExportOptions
  passthrough incl. forensic watermark) over the headless runtime with a
  configurable font catalog (`AddTempoDocumentEditorMcpRendering`: explicit
  faces, aliases, system Arial/DejaVu fallback, ImageResolver seam) — fail
  closed with agent-friendly font diagnostics.
- **Diff & redline** — `document_editor_diff_versions` (structured diff with
  word-level segments) and `document_editor_export_redline` (DOCX with real
  `w:ins`/`w:del` tracked changes or PDF with review markup).
- **Live co-editing bridge (opt-in)** —
  `AddTempoDocumentEditorMcpCollaboration`: semantic writes publish their
  operation batches to the host collaboration stream (named agent participant
  with presence color, backplane envelopes with `SourceInstanceId`); fail-open.
  The demo Api forwards publishes to the SignalR document groups, so open
  TmDocumentEditor sessions see agent edits live
  (`scripts/e2e-document-mcp-live-coedit.mjs`).
- Demo Api: `/mcp` endpoint now really serves the document tools
  (`IDocumentEditorProvider` registration), `/api/document-editor/mcp-agent-demo`
  runs a one-shot agent orchestration (create → edits → preview), and the
  contract-demo seed page breaks carry proper `PageBreakBlockContent`.

## 2.4.0 - 2026-07-19

### Headless document runtime — Phase 0: embedded headless layout bundle

- `Tempo.Blazor.DocumentFormats` now embeds the canvas editor's layout chain
  (`buildLayoutSnapshotExport` → `buildDisplayList` → `layoutCanvasDocument` →
  `translateDisplayListToLayoutSnapshot`, incl. line breaker, paragraph engine and the
  injectable font-metrics service) as a single ESM artifact
  (`HeadlessLayout/tempo-document-headless-layout.bundle.mjs`), accessible via the new
  `TempoDocumentHeadlessLayoutBundle` class. Server-side layout hosts evaluate this script to
  produce the exact layout snapshot the browser editor exports — WYSIWYG parity by construction.
- New build tooling: `npm run build:document-editor` builds the bundle
  (`scripts/build-document-editor.mjs`); `--check` is a drift gate wired into the
  `Tempo.Blazor.DocumentFormats` MSBuild pre-build and the Node test lane, so a stale embedded
  artifact fails the build. Node guard tests keep browser globals (`document`/`window`/
  `OffscreenCanvas`) out of the bundle outside the font-metrics safe fallback.

### Headless document runtime — Phase 1: font-accurate metrics from SkiaSharp

- New `TempoFontAdvanceTableExtractor` (+ `TempoFontAdvanceFace`) in
  `Tempo.Blazor.DocumentFormats.HeadlessLayout`: reads glyph advance widths and vertical metrics
  from the SAME `ReportPdfFontFace` bytes the PDF renderer embeds (SKTypeface/SKFont, unhinted
  linear metrics in font units) and serializes them into a compact JSON table for the JS side;
  thread-safe lazy cache per font face, Latin + Czech/Central European coverage by default.
- New JS module `document-editor/layout/font-advance-metrics.mjs`
  (`createAdvanceFontMetricsService`, `parseFontAdvanceTable`, `createFontAdvanceMeasureContext`,
  all exported from the headless bundle): measures text by summing advances (+ letter spacing,
  character scale, zoom) through the production font-metrics service, with face resolution
  mirroring the PDF renderer's font catalog and synthetic fallback + diagnostics for unknown
  families/glyphs. Plugs into pagination's `ensureMeasurementService` seam as a full service or a
  `{ measureRun }` partial.
- JS↔C# parity is pinned by a committed fixture
  (`font-advance-parity-fixture.json`, regenerable via `TEMPO_REGENERATE_FONT_PARITY_FIXTURE=1`):
  the Node lane replays Czech-diacritics and letter-spacing samples through the real JS measurer
  and asserts bit-identical widths; per-glyph advances equal `SKFont.MeasureText` exactly.

### Headless document runtime — Phase 2: `ITempoDocumentLayoutService` + Jint host

- New `ITempoDocumentLayoutService` in `Tempo.Blazor.DocumentFormats.HeadlessLayout`:
  `GenerateLayoutSnapshotJson(document, pageSetup?, fonts, reviewDisplayMode)` lays out a
  `DocumentEditorDocument` server-side with the exact canvas layout chain and returns the schema
  v1 snapshot JSON (`DocumentPdfExportRequest.LayoutSnapshotJson` contract). Register with
  `services.AddTempoDocumentLayout()`.
- `JintDocumentLayoutEngine` hosts the embedded bundle in pooled Jint engines (thread-safe,
  bounded by concurrency — no engine allocation per call; `CreatedEngineCount` diagnostics).
  Data-in/data-out JSON seam (`generateHeadlessLayoutSnapshotJson` in the bundle, also exported
  for Node consumers) — no .NET↔JS callbacks per glyph. Fail-closed with diagnostics: missing
  fonts, unknown font families and unmeasurable glyphs throw `TempoDocumentLayoutException`
  (`UnknownFontFamilies`, `MissingGlyphs`) instead of silently degrading to synthetic metrics.
  Page-setup override (size/orientation/margins in points) applies document-wide incl. sections;
  redaction-marked runs are destroyed in the snapshot; redline modes supported.
- Measured 2026-07-19 (Jint 4.13, .NET 10, Debug): ≈ 2.2 s cold (engine + bundle evaluation) and
  ≈ 0.9 s warm for a 54-page document (≈ 17 ms/page); 369-page stress run 5.4 s cold / 3.8 s
  warm. Perf gate budgets: 15 s cold / 6 s warm at 21+ pages.

### Headless document runtime — Phase 3: headless ↔ browser export parity

- Committed 21-page parity pair (`headless-parity-document.json` + request + snapshot fixtures,
  regenerable via `TEMPO_REGENERATE_HEADLESS_PARITY_FIXTURE=1`): the Jint-hosted layout of the
  committed Czech contract document matches the browser-generated
  `layout-snapshot-parity-fixture.json` in page count (21) and page geometry (< 1 pt — the only
  difference is the canvas engine's rounded 794×1123 px A4 default vs the exact
  595.276 pt × 96⁄72), reproduces the committed headless snapshot byte-for-byte, and replaying
  the identical request through the bundle in Node (V8) yields a DEEPLY EQUAL snapshot — layout
  does not depend on the hosting JS engine. Headless snapshot → `TempoDocumentPdfRenderer` PDF
  keeps page count and block positions within 1 pt.
- `Demo.Api` `DemoDocumentPdfExportProvider`: the legacy text-only PDF stub is deleted. Every
  export flows through the production WYSIWYG renderer — snapshot-less requests (GET exports,
  headless API clients) are laid out server-side via `ITempoDocumentLayoutService` with the new
  `DemoDocumentExportFontCatalog` (system Arial/DejaVu faces aliased as `Aptos` for the demo
  theme), the same faces the PDF embeds.
- `JintDocumentLayoutEngine.BuildRequestJson` is now public — the exact JS-seam payload, used by
  the cross-runtime parity test and available for diagnostics.
- New E2E (`DocumentEditorHeadlessExportParityE2ETests`): the same document exported through the
  browser path (live canvas snapshot) and the server path (headless GET export) agrees on
  pagination and text layer; both PDFs open in TmPdfViewer (screenshots); empty-document server
  export yields a valid single-page PDF.

### Headless document runtime — Phase 4: `TempoDocumentService` facade + PNG page previews

- New `ITempoDocumentService`/`TempoDocumentService` facade in
  `Tempo.Blazor.DocumentFormats.HeadlessLayout`: `RenderPdfAsync(template/document +
  tokenValues, options)` = `DocumentAssemblyService.Assemble` (IF/ELSE chains, repeating
  sections, computed expressions) → headless layout → `TempoDocumentPdfRenderer`, with
  watermark + forensic watermark passthrough and an injectable `TimeProvider` clock
  (deterministic `TODAY()`/`DATEADD` and forensic timestamps). Returns
  `TempoDocumentPdfResult` (PDF, page count, layout snapshot JSON, stamped forensic time).
- `RenderPageImagesAsync` rasters every laid-out page to PNG at a parametrizable DPI
  (`TempoDocumentPageImage`); `ReportPdfRenderer` gained an additive
  `RenderPagePng(page, options, scale)` overload. Register everything with
  `services.AddTempoDocumentServices()`.
- Demo.Api: new `POST /api/document-editor/assembly/render` — the demo assembly contract
  template + a dataset (scalar values + repeating item rows) → PDF or per-page PNG previews,
  rendered purely server-side. E2E proves two datasets flip the IF/ELSE branch and compute
  their totals, with PNG preview screenshots for UX review; COMPONENTS.md gained a
  "Headless dokumentový runtime" section.

### Headless document runtime — follow-ups

- Server-side image resolution: `TempoDocumentRenderRequest.ImageResolver`
  (`TempoDocumentImageSourceResolver` + `TempoDocumentImageReference`) resolves asset-backed and
  URL-based image sources to embeddable data URIs before layout — headless exports embed real
  images instead of placeholder rects. `DemoDocumentPdfExportProvider` resolves demo-store
  assets this way (`/XObject` embedded in headless PDFs); host-relative URLs remain
  unresolvable server-side by design.
- Applier convergence completed: body-level `insertBlock`/`moveBlock` now share ORDER-VALUE
  semantics with deterministic tie-breaks on both runtimes (JS previously spliced by index —
  fractional/large C# order values landed wrong), the JS applier normalizes persistence-shaped
  block payloads (`content.$type`/`inlines`) into canvas blocks on apply, and nested moves stay
  in their source cell. The convergence fixture now covers insert/update/move incl. cell
  containers and persistence payloads.
- Skia-derived parity fixtures are platform-aware: byte-exact on Windows (the generator
  platform), structural with tight tolerances elsewhere — Skia's scaler backend (FreeType vs
  DirectWrite) differs by ~1e-5 font units per advance, which broke byte comparisons on Linux
  CI.

### Headless document runtime — Phase 5: server-side operation applier

- Coverage audit of the canonical operation model across the C# applier
  (`DocumentOperationApplier`), the JS collaboration applier (`transform.mjs`) and the conflict
  resolver — table + findings in `docs/document-operation-applier-coverage.md`.
- `DocumentOperationApplier` now resolves operation targets inside table cells exactly like the
  JS applier (deep search with `TableCellId` as the container preference): text, mark, block,
  attribute and update operations work on nested blocks; block insert/move inside cells is
  index-based like JS; `table.cell.text` keeps its historical table-targeting semantics.
- Fixed the JS collab applier to split runs at mark-range boundaries (previously a
  partial-range mark bolded whole runs) — mirrors the C# and engine semantics.
- New cross-runtime convergence property tests: seeded operation batches applied by the C#
  applier produce a committed content signature
  (`operation-convergence-fixture.json`, regenerable via
  `TEMPO_REGENERATE_OPERATION_CONVERGENCE_FIXTURE=1`) that the JS applier reproduces deeply
  equal in the Node lane. Known divergences (body-level `moveBlock` order-vs-index semantics,
  `insertBlock`/`updateBlock` payload shapes) are documented and carried forward to the MCP
  tooling plan.

## 2.3.9 - 2026-07-19

### Document editor — canvas command layer completed (TmDocumentEditor)

Every command id routed from the C# UI into the canvas engine is now actually handled — the
C#↔engine command contract test runs with no allowlist. Fixed silent no-op toolbar/ribbon actions:

- **Fullscreen, header/footer toggles, insert table, delete table/page-break, table & cell
  properties mutations, protection** (plan phases 1–8): registered as real engine commands with
  full undo/redo semantics; document protection is enforced by the engine (restricted regions veto
  inline/paragraph edits).
- **Insert-ribbon token menu** (phase 9): the token button opened nothing (it routed an
  unregistered `openTokenMenu` command). It now opens a Blazor token panel (searchable, provider
  driven); picking a token inserts a first-class token run at the caret through the new
  `insertToken` engine command — rendered as its display name, exposed to assistive tech through
  the accessibility mirror, undoable as one transaction and persistent across save/reload.
- **Table properties, cell properties, replace image, set image link** (phase 10): these ribbon/
  command-palette entries routed engine commands that never existed. They now open the Properties
  side panel (the panel issues the real `setTableProperties`/`setCellProperties`/`setImageUrl`
  mutations), mirroring the table context menu. The command palette additionally syncs the live
  canvas table/image selection before computing command availability.
- **Engine fix:** commands that insert runs now follow the copy-on-write layout contract all the
  way up (new model object, cloned block, section block-list swap) — previously an inserted token
  updated the model but the canvas never repainted until reload.

## 2.3.8 - 2026-07-17

### Fixes

- Increased the dense-line marker spacing threshold from ~9 to ~24 SVG units: at ~9 units the thinned markers still touched each other (12-unit visual diameter), keeping the beaded look on very dense series (e.g. 360 monthly values). Markers now sit clearly ON the line with visible line segments between them.

## 2.3.7 - 2026-07-17

### Dense line series readability (TmChart)

- Line series (both `Line` charts and combo overlays on `Bar` charts) now thin their point markers when values are packed tighter than ~9 SVG units apart — overlapping white-stroked circles previously made a dense line (e.g. 360 monthly values) look dotted. The polyline itself always renders complete; sparse series keep a marker on every value.

## 2.3.6 - 2026-07-17

### Negative values on Bar and Line charts (TmChart)

- `Bar` charts (including combo overlays) and `Line` charts now support negative values through a signed value domain: when any visible value is negative, the Y axis extends below zero with an emphasized zero axis (parity with Area charts), bars grow downward from the zero baseline, and line/overlay points plot below the axis. Charts with only non-negative values render exactly as before (0-based scale). Previously a negative value produced an invalid negative-height bar or a point outside the plot area.

## 2.3.5 - 2026-07-17

### Combo charts (TmChart)

- Added `ChartDataset.RenderAs` (`ChartDatasetRenderAs.Default | Bar | Line`): on a `ChartType.Bar` chart, datasets marked `Line` render as a line overlay (polyline + clickable points) over the bars, centered on each category and sharing the bars' Y scale — bars for periodic flows, lines for cumulative values in one plot. Default keeps the chart's own type, so existing charts are unaffected; on non-Bar charts the override is ignored.
- Bar charts with more than 24 categories now thin their X-axis labels (every n-th label, at most ~12) so dense categorical axes stay readable; charts with up to 24 categories keep every label.

### Fixes

- Fixed `TmLightbox` stacking: the root `.tm-lightbox` used a hardcoded `z-index: 1000`, which painted the close/prev/next buttons underneath sticky chrome such as `TmTopBar` (`--tm-z-sticky` 1020). It now uses the overlay tier (`var(--tm-z-overlay, 1060)`), consistent with `.tm-lightbox-overlay`.

## 2.3.4 - 2026-07-17

### Accent-insensitive filtering (TmFilterableDropdown / TmMultiColumnComboBox)

- Client-side filtering in `TmFilterableDropdown` and `TmMultiColumnComboBox` is now accent-insensitive by default: both the filter term and the item text are normalized to Unicode FormD and combining diacritical marks are stripped before the contains comparison, so e.g. "usti" matches "Ústí nad Labem" and "práha" matches "Praha".
- Added an `AccentInsensitiveFilter` parameter (default `true`) to both components to opt back into accent-sensitive (but still case-insensitive) filtering.
- The change is match-superset only: every item that matched before still matches; accent-mismatched items are newly included. Server-side `DataProvider` filtering is unaffected (the provider owns its own matching).

## 2.3.0-preview.1 - Unreleased

- Added `ButtonVariant.OutlineSecondary`, `ButtonVariant.Warning`, and `ButtonVariant.OutlineWarning` to `TmButton`.
- Added `RowAttributes` to `TmDataTable` for applying row-level HTML attributes consistently across non-virtualized, virtualized, and grouped data rows.
- `DocumentEditorSnapshotCommand` restores the historical defensive-clone contract by default; added an opt-in `assumeOwnership` constructor parameter (default `false`) for callers that hand over dedicated snapshots and want to skip the two O(document) copies. Existing external code compiles and behaves as in 2.0.x.
- `DocumentEditorCommandRegistry.Register` now invalidates the refresh signature gate, so commands registered after the first `RefreshAllAsync` receive their state on the next refresh even when the command context is unchanged.
- TmDocumentEditor toolbar wave: native selects openable by mouse (removed `preventDefault`), 31 missing built-in icons added to `TmIcon` (+ `IconNames` constants and `DocumentToolbarItem.Options`/`DocumentToolbarRenderContext.CommandState` for declarative renderers), ribbon CSS consolidated into `_document-editor-toolbar.css`, 21 additional commands registered in the command registry with a unified enabled/visibility fallback.
- TmDocumentEditor ribbon overflow is now live: a new `toolbar-overflow.mjs` measurement controller (ResizeObserver + scroll + tab-switch mutations) reports off-screen `[data-command]` items through the existing `SetOverflowingAsync` contract, so the More menu finally appears on narrow windows; the toolbar loads the module itself — no host-app setup needed. Fixed the overflow search box opening pre-filled with a literal `_overflowSearchQuery`.
- `DocumentToolbarButtonRenderer` now honors `DocumentToolbarRenderContext.Execute` (click) and `CommandState.IsEnabled` (disabled), matching the toggle/select/color renderers; added the `/document-toolbar-renderers` demo page showcasing the declarative toolbar extension API.
- Added `TmNavigationGuard`, an unsaved-work navigation guard that gates internal router navigation with a `TmDialog` confirmation and arms a browser `beforeunload` prompt for tab close/refresh. Exposes `Suppress()` for post-commit programmatic navigation.
- Added `TmFormActionBar`, a sticky/floating action bar for long forms with `Status`/`PrimaryActions`/`SecondaryActions`/`DangerActions` slots, `Static`/`StickyTop`/`FloatingBottom` positions (`FormActionBarPosition`), and a functional `ShowOnScroll` reveal (real passive scroll listener, not a no-op hook).
- Added `TmScrollSpyNav`, a sectional in-page navigation component with an optional passive-scroll spy (`EnableScrollSpy`), `SideRail`/`Breadcrumb` variants (`ScrollSpyNavVariant`), a minimal generic `ScrollSpyNavItem` record, and an `ItemTemplate` slot for host-supplied enrichment. Active items expose both `aria-current="true"` and `data-active`.
- Added `TmUserPicker<TUser>`, a generic entity/user picker with debounced cancellable search, pointer-down selection, keyboard navigation, and explicit three-state (`TmPickerFetchState.Ok`/`Empty`/`Transient`) fetch rendering so a real search/resolve failure is never shown as a silent "no results". Plain async `SearchProvider`/`ResolveProvider` callers; no built-in retry loop.
- Added a typed `Required` parameter to `TmSelect`, `TmCheckbox`, and `TmRadioGroup`, matching the existing `TmTextInput`/`TmMultiSelect` pattern. When set, it renders the required marker (`tm-input-label-required` label class → asterisk) and sets `aria-required="true"` on the actual control — the `<select>`, the checkbox `<input>`, and the `role="radiogroup"` element respectively — instead of the wrapper, so it is exposed to assistive tech. `AdditionalAttributes` splat behavior is unchanged. Also advertised `required` in the built-in wireframe schemas for Checkbox and Radio Group.
- Extended the visible required marker (`tm-input-label-required` label asterisk) and `aria-required` to the remaining label-owning inputs so all label-bearing field types are consistent: `TmTextInput` and `TmDecimalInput` now add the marker class to their label (both already set `aria-required` on the control); `TmTextArea` and `TmDecimalInput` gained a typed `Required` parameter that drives the marker plus native `required`/`aria-required` on the `<textarea>`/`<input>`; and `TmDatePicker`/`TmDateTimePicker`'s previously-declared-but-unused `Required` is now wired to the marker and `aria-required` on the trigger button. `AdditionalAttributes` splat behavior is unchanged.

## 2.2.0 - 2026-07-06

### Data component chrome and filtering (TmDataTable / TmMultiViewList)

- Added `ShowToolbar` parameter to `TmDataTable` and `TmMultiViewList` to explicitly suppress the built-in toolbar.
- Added `ShowViewManager` parameter to `TmDataTable` and `TmMultiViewList` to control rendering of the saved-views picker independently.
- Added `SearchText` / `SearchTextChanged` two-way binding so the surrounding page can own the search state.
- Added `ToolbarMode` (`DataToolbarMode.Full`, `SearchOnly`, `ActionsOnly`, `ContentOnly`) as a higher-level API for common toolbar presets.
  - `Full` keeps the existing behavior (respects individual `Show*` flags).
  - `SearchOnly` renders only the global search input.
  - `ActionsOnly` renders only chrome actions (column picker / view switcher / view manager).
  - `ContentOnly` hides all toolbar chrome and the external filter builder, leaving a clean data surface for page-owned filtering.
- `ToolbarMode=ContentOnly` and `ShowExternalFilterBuilder=false` prevent duplicate filtering UI when the owning page already provides filters or saved views.
- Empty toolbars are no longer rendered when no control would be visible.

### Migration notes

- Existing code continues to compile and run unchanged; all new parameters have backward-compatible defaults.
- If your page already has its own filter toolbar, switch the data component to `ToolbarMode="DataToolbarMode.ContentOnly"` and bind `Items` to your pre-filtered collection.
- If you want saved views without the inline filter builder, keep `ToolbarMode="DataToolbarMode.Full"` and set `ShowExternalFilterBuilder="false"`.
- See `docs/data-component-chrome-migration.md` for a full migration guide and PromptHelper-specific replacement instructions.

## 2.1.0 - 2026-07-04

- Added the UI role vocabulary model for wireframe authoring, including built-in role synonyms and app-scoped role resolution.
- Added role-aware wireframe authoring through MCP operations and `wireframe_author_document`, with advisory warnings for role gaps, ambiguous matches, enum normalization, off-canvas placement, text overflow, required content, and layout issues.
- Added compact/filterable `wireframe_get_authoring_guide` output with category, type, role, target pack, app scope, skip, and take filters.
- Added container-aware wireframe linting through `isContainer`, so expected containment does not appear as sibling overlap.
- Added `WireframeThumbnailRenderer` in `Tempo.Blazor.Wireframe` and moved the Demo API document-library preview generation onto the package renderer.
- Updated the wireframe document schema and JSON documentation for document version 2.1, element roles, component roles, container metadata, MCP authoring, and thumbnails.
- Bumped published package metadata to `2.1.0` and aligned release workflows to derive manual/CI versions from the core package version.

Release follow-up after review: commit the prepared changes, merge to main, tag `v2.1.0`, push, then verify NuGet.org publication with a clean-project package-install smoke.
