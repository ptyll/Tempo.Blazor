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
