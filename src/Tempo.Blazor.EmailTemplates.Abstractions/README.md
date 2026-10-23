# Tempo.Blazor.EmailTemplates.Abstractions

The contracts and the model behind the Tempo.Blazor email template editor. This package has **no Blazor
dependency** and no template engine: it is safe to reference from an API, a background worker or a shared
DTO project, and it is what a Blazor WebAssembly editor compiles against without downloading Scriban,
Mjml.Net or AngleSharp.

## What's inside

- **Block model** — strongly typed document model covering the full MJML 4 component set
  (`EmailTemplateDocument`, `EmailSection`, `EmailBlock` and its concrete types), including
  `SchemaVersion` (currently 1) and the classes/attributes model.
- **MJML generation and import** — model → MJML markup and a bidirectional, lossless MJML → model parser,
  plus the MJML attribute-parity table.
- **Rendering contracts** — `IEmailPreviewRenderer` (the one seam through which the editor gets a preview
  and an HTML export), `IEmailTemplateRenderer`, `IMjmlCompiler`, `IHtmlContentSanitizer`,
  `ITemplateValidator`, `ITemplateEngine`, `ITemplateVariableExtractor`.
- **Security policy** — `TemplateSecurityOptions` (limits), `TemplateEscapeMode`, `HtmlSanitizerPolicy`,
  `EmailUrlPolicy` (the URL scheme allow-list), `CssSafety`, `MarkupGuard` and the audit contract
  `ITemplateSecurityAuditSink` / `TemplateSecurityEvent`.
- **Host contracts** — `IEmailTemplateStore`, `IEmailSender`, `EmailMessage` and the save contract
  (`EmailSaveRequest`, `EmailSaveResult`, `EmailSaveStatus`, `EmailSaveTrigger`).
- **Localization** — the JSON string tables and `JsonStringLocalizer<T>`, so a host can replace the
  wording without touching the pipeline.
- **Import limits** — `EmailImportOptions` (5 MB, 200 sections, 2000 blocks, depth 8) enforced by the
  importer, the JSON reader and the server-side mapper.

What it deliberately does **not** contain: Scriban, Mjml.Net and AngleSharp. Those live in
`Tempo.Blazor.EmailTemplates.Engine`, which the editor package does not reference.

## Installation

```
dotnet add package Tempo.Blazor.EmailTemplates.Abstractions
```

Adding the editor package (`Tempo.Blazor.EmailTemplates`) brings this one transitively.

## Registration

```csharp
// model half only (validators, generators, variable extraction); no UI, no engine
services.AddTempoEmailTemplateModel();

// an API/worker that actually renders
services.AddTempoEmailTemplateEngine();   // lives in Tempo.Blazor.EmailTemplates.Engine
```

## Documentation

- **Hosting guide** — `docs/email-templates/HOSTING.md`: packages, registration, the preview seam, the
  save contract, the dirty guard, the audit sink, limits.
- **Template syntax** — `docs/email-templates/TEMPLATE_SYNTAX.md`: Scriban variables, filters, the default
  HTML escaping, the `html.trusted` opt-out, URL schemes, culture, the sandbox limits.
- **Migration 2.9 → 2.10** — `docs/email-templates/MIGRATION-2.9-to-2.10.md`: every breaking change with a
  before/after.
- **MJML attribute parity** — `docs/email-templates/MJML_ATTRIBUTE_PARITY.md`: which MJML 4 attribute maps
  to which model property.
