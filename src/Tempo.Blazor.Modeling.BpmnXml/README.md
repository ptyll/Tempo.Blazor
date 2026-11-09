# Tempo.Blazor.Modeling.BpmnXml

Headless BPMN 2.0 XML converter for Tempo.Blazor Modeling. Imports BPMN 2.0 XML
documents into `ModelingModelDto` (from `Tempo.Blazor.Abstractions`) deterministically —
same input produces the same output, with stable element ids taken from the XML —
and never silently drops content: every unsupported or approximated construct is
reported in the import loss report.

- Zero UI dependencies (no Blazor, no Razor, no JS interop) — usable from ASP.NET Core
  services, workers and MCP servers.
- Safe by default: DTD processing prohibited, no XML resolver, bounded input size,
  depth and element-count limits.
- Dependency injection: `services.AddTempoBpmnXml()` registers `IBpmnXmlImporter`.

```csharp
var importer = new BpmnXmlImporter();
BpmnImportResult result = importer.Import(bpmnXmlStream);
if (result.HasErrors) { /* result.ErrorMessage — never throws for bad input */ }
ModelingModelDto model = result.Models[0];
IReadOnlyList<BpmnImportLoss> losses = result.Losses;
```

The export side (`IBpmnXmlExporter`), BPMNDI view import and tool plugins (Camunda,
Signavio, Bizagi) land in later phases of the converter plan; `BpmnImportOptions`
already carries the knobs those phases honor.
