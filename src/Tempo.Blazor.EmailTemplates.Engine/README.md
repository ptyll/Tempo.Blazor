# Tempo.Blazor.EmailTemplates.Engine

The local render engine of the Tempo.Blazor email template editor — the sandboxed Scriban template engine
(member policy, per-pass HTML escaping, audit trail, limits), the Mjml.Net compiler, the AngleSharp HTML
sanitizer, the render pipeline and the local preview renderer.

The editor package (`Tempo.Blazor.EmailTemplates`) does **not** reference this one: it talks to
`ITemplateValidator`, `IHtmlContentSanitizer`, `ITemplateVariableExtractor` and `IEmailPreviewRenderer`, so a
Blazor WebAssembly editor runs without Scriban/Mjml.Net/AngleSharp and previews through a server-side
`IEmailPreviewRenderer`. Add this package when the process that renders the preview (or exports the HTML) is
the one that ships the engine.

```csharp
// server, or a WASM app that genuinely renders locally
builder.Services
    .AddTempoEmailTemplateEditor()   // UI + model, no engine
    .AddTempoEmailTemplateEngine();  // this package: Scriban + Mjml.Net + AngleSharp
```

`AddTempoEmailTemplateEngine()` also registers `ITemplateValidator` (the syntax check in
`TmEmailValidationPanel`), `IHtmlContentSanitizer` (the authoritative AngleSharp implementation replaces the
BCL `ConservativeHtmlContentSanitizer`) and `IEmailPreviewRenderer` (`LocalEmailPreviewRenderer`). The
registration is order-independent with `AddTempoEmailTemplateModel()` and idempotent.

`InternalsVisibleTo` binds this package to an exact `Tempo.Blazor.EmailTemplates.Abstractions` version
(`[2.10.0]`), because it reaches internals (`TemplateEscapeMode`, `IEscapingTemplateEngine`,
`HtmlTextEscaper`, `HtmlTagTokenizer`, `TemplateContentMasker`). Do not loosen that range: a drifted
Abstractions silently degrades the pipeline to the custom-engine fallback that skips per-pass escaping.

## What it registers

| Service | Implementation | Used by |
|---|---|---|
| `ITemplateEngine` | `ScribanTemplateEngine` | server-side render, custom pipelines |
| `ITemplateValidator` | the Scriban syntax check | `TmEmailValidationPanel` |
| `IHtmlContentSanitizer` | `AngleSharpHtmlContentSanitizer` | render pipeline, editor (replaces the BCL fallback) |
| `IEmailTemplateRenderer` | `EmailTemplateRenderer` | HTML + plain text from a document |
| `IEmailPreviewRenderer` | `LocalEmailPreviewRenderer` | `TmEmailTemplatePreview` and the HTML export |
| `IMjmlCompiler` | `MjmlNetCompiler` | MJML → HTML |

It also forwards every registered `ITemplateSecurityAuditSink` and `ITemplateFunctionProvider`, and it
validates `TemplateSecurityOptions` (loop/recursion/output/recursion/regex limits, render timeout) when the
engine is constructed — a zero or negative limit throws instead of silently meaning "unlimited".

## Documentation

- `docs/email-templates/HOSTING.md` — the hosting guide (preview seam, save contract, layout, localization)
- `docs/email-templates/TEMPLATE_SYNTAX.md` — the template language, the default HTML escaping, the
  `html.trusted` opt-out, the URL allow-list, culture and the sandbox limits
- `docs/email-templates/MIGRATION-2.9-to-2.10.md` — how 2.9.x hosts move to the three-package split
