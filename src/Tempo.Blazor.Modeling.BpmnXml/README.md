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
  `IBpmnXmlExporter` plus the built-in tool dialect plugins; the DI-resolved importer/
  exporter apply them automatically (explicit `ToolExtensions` options plugins are
  consulted first, the DI fallback list fills the remaining namespaces, the merged list is
  de-duplicated by `NamespaceUri` first-wins, and the parameterless constructors stay
  plugin-free).

```csharp
var importer = new BpmnXmlImporter();
BpmnImportResult result = importer.Import(bpmnXmlStream);
if (result.HasErrors) { /* result.ErrorMessage — never throws for bad input */ }
IReadOnlyList<ModelingModelDto> models = result.Models;

// Built-in Camunda/Signavio/Bizagi dialect plugins keep tool metadata loss-free:
var pluginImporter = new BpmnXmlImporter(BpmnBuiltInToolExtensions.All);

var exporter = new BpmnXmlExporter();
// one definitions document: collaboration, participants, processes, message flows, BPMNDI
BpmnExportResult exported = exporter.Export(models,
    new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
if (exported.Success) { string xml = exported.Xml!; }
```

Tool plugins: `IBpmnToolExtension` lets a dialect map its `extensionElements` into typed
properties and restore them on export; the built-in Camunda (`camunda:*` keys,
`camundaExtensions` verbatim payloads), Signavio (`signavio:*` keys) and Bizagi
(`bizagi:*` keys) plugins ship in `BpmnToolExtensions.cs` and are registered by
`AddTempoBpmnXml()`. Unrecognized namespaces are still preserved verbatim under
`foreignExtensions` with a loss record — nothing is dropped.
