# Tempo.Blazor NuGet Package Split Migration

Date: 2026-06-23

Tempo.Blazor now uses a lean core package plus feature packages for the largest editors and dependency-heavy component groups. Existing applications that want the previous all-in behavior can reference `Tempo.Blazor.All`.

## Package Choices

| Package | Use When |
| --- | --- |
| `Tempo.Blazor` | Core UI components, services, tokens, base CSS, and lightweight JS interop. |
| `Tempo.Blazor.All` | Compatibility package for apps that want core plus the split feature packages. |
| `Tempo.Blazor.PdfViewer` | `TmPdfViewer` and PDF.js assets. |
| `Tempo.Blazor.Codes` | `TmQRCode`, `TmBarcode`, `QRCoder`, and `ZXing.Net`. |
| `Tempo.Blazor.AI` | AI conversation UI (Markdig); contracts in Tempo.Blazor.Abstractions. |
| `Tempo.Blazor.DiagramEditor` | Diagram editor, stencil/template services, layout JS, and diagram assets. |
| `Tempo.Blazor.Wireframe` | Wireframe editor, registry services, schema, and wireframe assets. |
| `Tempo.Blazor.Modeling` | Modeling editor and modeling services built on `Tempo.Blazor.DiagramEditor`. |
| `Tempo.Blazor.Spreadsheet` | Spreadsheet editor, XLSX import/export, and spreadsheet assets. |
| `Tempo.Blazor.GanttXlsx` | Optional Gantt XLSX import/export helpers. |
| `Tempo.Blazor.DocumentEditor` | Document editor UI, canvas runtime, clipboard normalization, and document assets. |
| `Tempo.Blazor.NotionEditor` | Notion-style page/block/database editor and integrated Tempo embed blocks. |
| `Tempo.Blazor.Signing` | Signing workflow components, PDF template designer, document page viewer overlays, audit trail, and signing assets. |

## Fast Compatibility Path

Replace a single old `Tempo.Blazor` all-in dependency with:

```bash
dotnet add package Tempo.Blazor.All
```

Register all split services:

```csharp
using Tempo.Blazor.Configuration;

builder.Services.AddTempoBlazorAll();
```

Continue to include the static assets for the feature pages you actually render. `Tempo.Blazor.All` brings package dependencies and DI registration, but host pages still control which CSS and scripts are loaded.

## Lean Explicit Path

Install only the feature packages used by the app:

```bash
dotnet add package Tempo.Blazor
dotnet add package Tempo.Blazor.PdfViewer
dotnet add package Tempo.Blazor.DiagramEditor
dotnet add package Tempo.Blazor.Wireframe
dotnet add package Tempo.Blazor.Spreadsheet
dotnet add package Tempo.Blazor.DocumentEditor
dotnet add package Tempo.Blazor.NotionEditor
dotnet add package Tempo.Blazor.Signing
```

Register only the matching services:

```csharp
builder.Services.AddTempoBlazor();
builder.Services.AddTempoBlazorPdfViewer();
builder.Services.AddTempoBlazorDiagramEditor();
builder.Services.AddTempoBlazorWireframe();
builder.Services.AddTempoBlazorSpreadsheet();
builder.Services.AddTempoBlazorDocumentEditor();
builder.Services.AddTempoBlazorNotionEditor();
builder.Services.AddTempoBlazorSigning();
```

## Asset Path Changes

| Old Path | New Path |
| --- | --- |
| `_content/Tempo.Blazor/js/pdf-viewer.js` | `_content/Tempo.Blazor.PdfViewer/js/pdf-viewer.js` |
| `_content/Tempo.Blazor/js/diagram-editor.js` | `_content/Tempo.Blazor.DiagramEditor/js/diagram-editor.js` |
| `_content/Tempo.Blazor/js/dagre.min.js` | `_content/Tempo.Blazor.DiagramEditor/js/dagre.min.js` |
| `_content/Tempo.Blazor/js/wireframe-designer.js` | `_content/Tempo.Blazor.Wireframe/js/wireframe-designer.js` |
| `_content/Tempo.Blazor/js/spreadsheet.js` | `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet.js` |
| `_content/Tempo.Blazor/js/spreadsheet-canvas.js` | `_content/Tempo.Blazor.Spreadsheet/js/spreadsheet-canvas.js` |
| `_content/Tempo.Blazor/js/document-editor/**` | `_content/Tempo.Blazor.DocumentEditor/js/document-editor/**` |
| `_content/Tempo.Blazor/js/document-editor-canvas/**` | `_content/Tempo.Blazor.DocumentEditor/js/document-editor-canvas/**` |
| `_content/Tempo.Blazor/js/notion-editor.js` | `_content/Tempo.Blazor.NotionEditor/js/notion-editor.js` |
| `_content/Tempo.Blazor/js/pdf-template-designer.js` | `_content/Tempo.Blazor.Signing/js/pdf-template-designer.js` |

Feature CSS entry points:

```html
<link href="_content/Tempo.Blazor/css/tempo-blazor.bundled.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Wireframe/css/tempo-blazor-wireframe.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Spreadsheet/css/tempo-blazor-spreadsheet.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.DocumentEditor/css/tempo-blazor-document-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.NotionEditor/css/tempo-blazor-notion-editor.css" rel="stylesheet" />
<link href="_content/Tempo.Blazor.Signing/css/tempo-blazor-signing.css" rel="stylesheet" />
```

`TmPdfTemplateDesigner` imports `_content/Tempo.Blazor.Signing/js/pdf-template-designer.js` as an ES module. Apps normally do not need a separate `<script>` tag for it, but CSP rules and static-asset allowlists must allow the new path.

## Signing Migration Notes

Signing workflows moved from core `Tempo.Blazor` into `Tempo.Blazor.Signing`. Add the new package and call `builder.Services.AddTempoBlazorSigning()` when using components from `Tempo.Blazor.Components.Signing`, including `TmPdfTemplateDesigner`, `TmSigningFormRunner`, `TmDocumentPageViewer`, `TmSigningFieldOverlay`, `TmAuditTrailViewer`, `TmShareLinkPanel`, and the signing step components.

The public component namespace remains `Tempo.Blazor.Components.Signing`, so Razor `@using` lines do not change. The breaking part is package and asset ownership: applications that previously got these components from `Tempo.Blazor` must reference `Tempo.Blazor.Signing`, include `_content/Tempo.Blazor.Signing/css/tempo-blazor-signing.css`, and allow the new PDF template designer module path.

For the first extraction iteration, `TmSignature`, `TmSignatureCapture`, `_signature-capture.css`, and `_content/Tempo.Blazor/js/signature-capture.js` remain in core `Tempo.Blazor`. Signing model/contracts under `Tempo.Blazor.Abstractions` also remain where they are.

## Release Notes

This is a breaking package layout change for apps that relied on large editors being inside `Tempo.Blazor`.

Core `Tempo.Blazor` no longer ships PDF viewer, QR/barcode, Diagram, Wireframe, Modeling, Spreadsheet, Gantt XLSX helpers, DocumentEditor, NotionEditor, or Signing workflow implementation/assets. Add the corresponding feature package or use `Tempo.Blazor.All`.

Known follow-up items:

- `TmSignature` and `TmSignatureCapture` remain in core for now; a later phase can decide whether moving them is worth a separate breaking change.
- Feature localization resources still live in core resources until the planned composite/resource-contributor localizer work.
- `Tempo.Blazor.NotionEditor` currently includes integrated Tempo blocks and therefore depends on PDF, Diagram, Wireframe, Spreadsheet, and Signing feature packages. A future `Tempo.Blazor.NotionEditor.TempoBlocks` split can make the base Notion package smaller.
