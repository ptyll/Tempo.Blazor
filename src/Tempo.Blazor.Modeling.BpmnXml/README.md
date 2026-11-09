# Tempo.Blazor.Modeling.BpmnXml

Headless BPMN 2.0 XML converter for Tempo.Blazor Modeling. Imports BPMN 2.0 XML
documents into `ModelingModelDto` (from `Tempo.Blazor.Abstractions`) and exports them
back — deterministic in both directions (same input produces the same output, stable
element ids taken from the XML, invariant-culture numbers, LF line endings, no BOM) —
and never silently drops content: every unsupported or approximated construct is
reported in the loss report.

- Zero UI dependencies (no Blazor, no Razor, no JS interop) — usable from ASP.NET Core
  services, workers and MCP servers.
- Safe by default: DTD processing prohibited, no XML resolver, bounded input size,
  depth and element-count limits.
- Round-trip safe: `import → export → import` reproduces the model (the
  `WithHostExtensions` export profile preserves every property through the host
  extension namespace; the `Standard` profile writes only native BPMN and reports the
  rest as export losses).
- Dependency injection: `services.AddTempoBpmnXml()` registers `IBpmnXmlImporter` and
  `IBpmnXmlExporter`.

```csharp
var importer = new BpmnXmlImporter();
BpmnImportResult result = importer.Import(bpmnXmlStream);
if (result.HasErrors) { /* result.ErrorMessage — never throws for bad input */ }
IReadOnlyList<ModelingModelDto> models = result.Models;

var exporter = new BpmnXmlExporter();
// one definitions document: collaboration, participants, processes, message flows, BPMNDI
BpmnExportResult exported = exporter.Export(models,
    new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
if (exported.Success) { string xml = exported.Xml!; }
```

Tool plugins (Camunda, Signavio, Bizagi) land in the next phase of the converter plan;
`BpmnImportOptions`/`BpmnExportOptions` already carry the hooks those phases honor.
