# Modeling MCP tools + headless diagram SVG export — design & catalog

Status: **approved design (Phase 1)** for plan _"Upstream II: Modeling MCP tooly + headless SVG
export DiagramEditoru"_. Target release **2.6.0** (next minor after the shipped `v2.5.5`; the
plan's original `2.2.0` is superseded). Everything here is **additive** — existing `2.x`
consumers compile and run unchanged.

This document is both the Phase 1 design record and the living tool catalog. From Phase 4 it is
guarded by a documentation-drift test (mirror of `DocumentMcpToolsDocumentationDriftTests`):
every registered modeling tool must have a `### \`name\`` section here, and no section may
describe a tool that no longer exists.

## Why (context)

Governed by **DEC-ARCH-MODELING point 5** (PromptHelper app): architecture as *data for LLM
agents*. The consumer is the PromptHelper _"Živá architektura I/II"_ plans, which need two
things this library does not yet expose:

1. **MCP access to architecture models** — agents that can list models, read the model tree and
   generated views, mutate elements/relationships under notation rules, and validate — the same
   way agents already drive the wireframe/diagram/notion editors through `Tempo.Blazor.Mcp`.
2. **Headless SVG export of diagrams** — a server-side `DiagramDocument → SVG` renderer (no
   browser) so diagrams and generated architecture views can be embedded into documentation.

Neither exists today: `Tempo.Blazor.Mcp` has Diagram/Wireframe/Notion/Reporting/DocumentEditor
suites but **no Modeling suite**, and there is **no shippable headless `IDiagramSvgRenderer`**
(full-fidelity SVG lives only in `Demo.Api`).

## Design decisions (confirmed with the user)

| # | Decision | Choice |
|---|----------|--------|
| 1 | Modeling persistence/provider model | **Mirror the diagram/wireframe suites.** Add `IModelingModelDocumentProvider` (get/save/create `ModelingModelDto` by id) + `TempoDocumentKind.Modeling`. Read tools browse via `ITempoDocumentLibraryProvider`; `modeling_apply_operations` validates then saves back through the provider. The host (PromptHelper) implements persistence. |
| 2 | Headless SVG renderer | **Promote the existing full-fidelity `DiagramExportSvgBuilder`** out of `Demo.Api` into the shippable `Tempo.Blazor.DiagramEditor` package behind a new `IDiagramSvgRenderer`, add a light/dark theme option. Browser-free, deterministic. |
| 3 | Localization of MCP messages | **Plain English literals, no resx.** Follows the established `Tempo.Blazor.Mcp` convention (audience is the LLM; the package contains zero `ITmLocalizer` usage). The plan's "localize en+cs+fr + MockTmLocalizer" subtask does **not** apply to MCP tool output. |
| 4 | Release version | **2.6.0**, tag `v2.6.0` (after explicit user confirmation, per plan rule). |

## Where the pieces live

- **New provider contract** — `src/Tempo.Blazor.Abstractions/Modeling/IModelingModelDocumentProvider.cs`
  (mirror of `src/Tempo.Blazor.Abstractions/NotionEditor/Interfaces/IDiagramDocumentProvider.cs`).
- **New document kind** — append `Modeling` to
  `src/Tempo.Blazor.Abstractions/DocumentLibrary/TempoDocumentKind.cs` (additive enum value).
- **Renderer interface + options** — `src/Tempo.Blazor.Abstractions/Diagram/Services/IDiagramSvgRenderer.cs`
  (+ `DiagramSvgRenderOptions`), alongside the existing `IDiagramExportService`.
- **Renderer implementation** — `src/Tempo.Blazor.DiagramEditor/.../DiagramSvgRenderer.cs`
  (promoted from `src/Tempo.Blazor.Demo.Api/Services/DiagramExportSvgBuilder.cs`), registered as a
  singleton in `DiagramEditorServiceCollectionExtensions.AddTempoBlazorDiagramEditor`.
- **Modeling MCP suite** — `src/Tempo.Blazor.Mcp/Modeling/*Tools.cs` + engines; registration
  `TempoModelingMcp.ToolTypes` / `AddTempoModelingMcpTools` in
  `src/Tempo.Blazor.Mcp/ServiceCollectionExtensions.cs`, wired into the aggregate
  `TempoBlazorMcp.ToolTypes` + `AddTempoBlazorMcpTools()`.
- **`diagram_render_svg`** — added to the existing `src/Tempo.Blazor.Mcp/Diagram/` suite (it is a
  diagram-level capability, not modeling-specific), backed by `IDiagramSvgRenderer`.
- **Tests** — `tests/Tempo.Blazor.Mcp.Tests` (fakes + snapshots) and `tests/Tempo.Blazor.Tests`
  (renderer golden files).

## Data model recap (existing, unchanged)

- `ModelingModelDto` (`src/Tempo.Blazor.Abstractions/Modeling/ModelingDtos.cs`): `Id`, `Title`,
  `Notation`, `SupportedNotations`, `Elements[]`, `Relationships[]`, `Views[]`, `Issues[]`,
  `Metadata`. Elements carry `Notation`/`SemanticType`/`Name`; relationships carry
  `RelationshipType` + `SourceElementId`/`TargetElementId`.
- Notation rules: `IModelingRelationshipRulesProvider.ValidateRelationship(...)`,
  `IModelingViewpointRulesProvider.ValidateElementViewpoint(...)`,
  `IModelingNotationProfileProvider` (+ built-in ArchiMate/BPMN/UML profiles &
  `ArchimateRelationshipMatrix`). Both validators return `Message` + `SuggestedFix`.
- Diagnostics: `ModelingIssueDto { Id, Severity(Info|Warning|Error), Category, SourceElementId,
  SourceRelationshipId, Message, SuggestedFix }`.
- `DiagramDocument` (`src/Tempo.Blazor.Abstractions/Diagram/Models/DiagramDocument.cs`):
  multi-page, custom `DiagramDocumentJsonConverter` — always serialize/deserialize through
  `DiagramSerializer` (as the existing diagram tools do), never reflect its properties.
- `ModelingDiagramGenerator.Generate(model, options)` projects a `ModelingModelDto` → a
  `DiagramDocument` **and** emits `ModelingIssueDto[]` as a side effect (enforcing notation +
  viewpoint rules inline). This is reused by `modeling_get_view` and `modeling_validate`.

## New host provider contract

```csharp
namespace Tempo.Blazor.Modeling;

using Tempo.Blazor.Modeling; // ModelingModelDto

public interface IModelingModelDocumentProvider
{
    Task<ModelingModelDto?> GetModelingModelDocumentAsync(Guid documentId);
    Task<ModelingModelDto> SaveModelingModelDocumentAsync(Guid documentId, ModelingModelDto model);
    Task<(Guid Id, ModelingModelDto Document)> CreateModelingModelDocumentAsync(string title);
    Task<(Guid Id, ModelingModelDto Document)> CreateModelingModelDocumentAsync(string title, string? scopeAppId)
        => CreateModelingModelDocumentAsync(title);
}
```

Exact shape of `IDiagramDocumentProvider`, retargeted to `ModelingModelDto`. The existing
read-only, source-backed `IModelingModelProvider` (editor loading path) is **left untouched**;
the MCP write path uses the new document provider. Listing goes through the shared
`ITempoDocumentLibraryProvider` with `TempoDocumentKind.Modeling`. `DocumentLibraryEntry.ModifiedAt`
is the optimistic-concurrency token (via `McpConcurrency.DateTimeConflict`), identical to the
wireframe/diagram write flow.

## Result & error conventions (shared)

Every tool returns the `McpToolResults` envelope: `{ "success": true, ... }` or
`{ "success": false, "error": "<code>", "message": "...", "validationErrors": [...] }`. Error
codes reused verbatim: `not_found`, `validation_failed`, `conflict`, `invalid_operation`,
`unsupported`, `error`. Concurrency conflicts return `conflict` and point the agent back at the
matching read tool. All strings are literal English.

## The agent loop

```
modeling_list_models                     (discover models: id, name, folder, notation)
→ modeling_get_model_tree                (elements/relationships/views + concurrencyToken)
→ modeling_list_notations                (allowed element/relationship/viewpoint types per notation)
→ modeling_apply_operations              (batch add/update/delete under notation rules, atomic)
→ modeling_validate                      (notation issues: severity/category/message/suggestedFix)
→ modeling_get_view                      (generated DiagramDocument JSON for a view/viewpoint)
→ diagram_render_svg                     (DiagramDocument JSON → SVG string for embed/preview)
```

DI setup: host calls `AddTempoModelingMcpTools()` and implements `IModelingModelDocumentProvider`
+ `ITempoDocumentLibraryProvider` (kind `Modeling`); the notation rule providers come from
`AddTempoBlazorModeling()`. `diagram_render_svg` requires `AddTempoBlazorDiagramEditor()` (registers
`IDiagramSvgRenderer`).

## Modeling tool catalog

### `modeling_list_models`
Browse stored modeling models via `ITempoDocumentLibraryProvider` (kind `Modeling`, optional
`folderPath`, `scopeAppId`). Returns entries `{ id, name, folderPath, modifiedAt }` + `totalCount`.
Read-only; no concurrency token needed to list.

### `modeling_get_model_tree`
Load a model by id and return a structured, agent-friendly tree: `notation`,
`supportedNotations`, `elements` (`id`, `notation`, `semanticType`, `name`, `description`,
`tags`), `relationships` (`id`, `relationshipType`, `sourceElementId`, `targetElementId`,
`name`), `views` (`id`, `name`, `viewpointKey`), aggregate counts, existing `issues` summary, and
`concurrencyToken` (the library `modifiedAt`). Unknown id → `not_found`.

### `modeling_get_view`
Project a model view/viewpoint to a `DiagramDocument` via `ModelingDiagramGenerator.Generate`
(`viewId` and/or `viewpointKey`; default view when omitted). Returns the `DiagramDocument` JSON
(serialized through `DiagramSerializer`) plus the generation `issues` and `concurrencyToken`. This
is the bridge into `diagram_render_svg`.

### `modeling_apply_operations`
Batch mutation of a model under an `expectedConcurrencyToken`. Operations (each `op`):
`add_element`, `update_element`, `delete_element`, `add_relationship`, `update_relationship`,
`delete_relationship`, `set_properties`.

- **add_element**: `notation`, `semanticType`, `name`, optional `id` (server-generated when
  omitted; explicit duplicate id → `validation_failed`), `description`, `parentId`, `properties`,
  `removedProperties`.
- **update_element**: `id` + any of `name`/`semanticType`/`description`/`notation`/`parentId`/
  `properties`/`removedProperties` (only supplied fields change).
- **delete_element**: `id`. Relationships referencing the element must be deleted in the **same
  batch**, otherwise the batch is rejected with a dangling-reference error (no silent cascade —
  the agent stays in control). An element that still parents other elements must be reparented or
  deleted first (`still_has_children`).
- **add_relationship** / **update_relationship**: `relationshipType`, `sourceElementId`,
  `targetElementId` (+ `name`/`properties`/`removedProperties`). Each is validated via
  `IModelingRelationshipRulesProvider.ValidateRelationship` against the notation matrix (e.g.
  ArchiMate). An invalid relationship returns the rule's `Message` + `SuggestedFix` — it is **not
  written**.
- **delete_relationship**: `id`.
- **set_properties**: `id` of an element OR a relationship (resolved by id) + a `properties`
  object, optional `removedProperties` (a JSON array of keys to remove). The same two members are
  accepted directly by the four add/update operations, so one `add_element` or `update_element`
  carries its values without a follow-up op. A malformed payload (`properties` that is not an
  object, `removedProperties` that is not an array of strings) fails the op with `property_type`
  before anything is written.

**Atomicity**: all operations are applied to a working clone and every element/relationship is
validated first; only if the *entire* batch is valid is the model saved via
`SaveModelingModelDocumentAsync`. A single invalid op fails the whole batch with
`validation_failed` and a per-op `operations[i]` locator — nothing is persisted (mirrors the
convergence-tested wireframe/diagram operation flow). Success returns the new `concurrencyToken`
and a post-apply `issues` list.

### `modeling_validate`
Run notation validation over a model (loaded by id, or a supplied `modelJson` for stateless
checks) and return `ModelingIssueDto[]` — `severity`, `category` (`validation`/`viewpoint`/
`mapping`/`view`), `message`, `suggestedFix`, and the offending element/relationship id. Reuses
the generator's rule wiring (relationship matrix + viewpoint rules); no document is rendered.

### `modeling_list_notations`
List the registered notation profiles from `IModelingNotationProfileProvider`: `notationKey`,
`displayName`, `supportedElementTypes`, `supportedRelationshipTypes`, `supportedViewpointKeys`,
`enforcesStrictStencilMapping`. Lets an agent discover the legal vocabulary before calling
`modeling_apply_operations`.

## Diagram tool catalog (addition)

### `diagram_render_svg`
`DiagramDocument` JSON → deterministic SVG string, backed by `IDiagramSvgRenderer` (headless, no
browser). Options: `theme` (`light` default | `dark`), `pageIndex`, `width`/`height`, `padding`,
`includeGrid`, `backgroundColor`. Deserialize input via `DiagramSerializer`; on malformed JSON →
`validation_failed`. Output is SVG suitable for embedding in documentation. Pairs with
`modeling_get_view` (model view → `DiagramDocument` → SVG).

## Headless renderer contract

```csharp
namespace Tempo.Blazor.Components.Diagram.Services;

public interface IDiagramSvgRenderer
{
    string RenderSvg(DiagramDocument document, DiagramSvgRenderOptions? options = null);
}

public sealed class DiagramSvgRenderOptions
{
    public DiagramSvgTheme Theme { get; init; } = DiagramSvgTheme.Light;
    public int? PageIndex { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    public double Padding { get; init; } = 20;
    public bool IncludeGrid { get; init; }
    public string? BackgroundColor { get; init; }
}

public enum DiagramSvgTheme { Light, Dark }
```

Implementation promotes the SVG-building logic (edges/nodes/ports/stencil sections, invariant-
culture `F(...)` coordinate formatting, `Escape(...)` text escaping) into the shared, browser-free
`DiagramSvgBuilder` in `Tempo.Blazor.Abstractions`, driven by a `Func<string, DiagramStencil?>`
stencil-lookup delegate. The shippable `DiagramSvgRenderer` (in `Tempo.Blazor.DiagramEditor`) binds
it to the real `DiagramStencilRegistry`; the demo export service binds the same builder to its own
stencil map (so there is now one implementation, not two). The `Dark` theme swaps background +
default node/edge palette; explicit stencil/style colours always win. Determinism (invariant
culture, nodes ordered by z-index) is a hard requirement — see golden-file tests below.

**Usage (headless export):**

```csharp
// DI (host startup): registers IDiagramSvgRenderer as a singleton.
services.AddTempoBlazorDiagramEditor();

// Anywhere a DiagramDocument is available (e.g. an MCP tool or a docs pipeline):
public string ExportDiagram(IDiagramSvgRenderer renderer, DiagramDocument document)
    => renderer.RenderSvg(document, new DiagramSvgRenderOptions
    {
        Theme = DiagramSvgTheme.Dark,   // Light (default) | Dark
        IncludeGrid = false,
        PageIndex = 0,                  // null → active page
    });
```

Sample outputs (committed golden files):
`tests/Tempo.Blazor.Tests/Diagram/DiagramSvgGolden/diagram-svg-basic-{light,dark}.svg`.

## Testing approach

- **Renderer (Phase 2)** — TDD in `tests/Tempo.Blazor.Tests`: golden-file SVG snapshots gated by
  `TEMPO_REGENERATE_DIAGRAM_SVG=1` (house convention from the DocumentFormats parity tests) +
  determinism (two renders byte-equal) + sanitizer safety (no `<script>`/`javascript:`/`onload=`)
  + edges: empty document, large graph, custom colors, dark theme. DI registration test (singleton,
  resolves & renders).
- **MCP suite (Phase 3)** — `tests/Tempo.Blazor.Mcp.Tests`: a `FakeModelingBackend` implementing
  both `ITempoDocumentLibraryProvider` and `IModelingModelDocumentProvider` (mirror
  `FakeWireframeBackend`); happy-path + edge tests (unknown model → `not_found`, stale token →
  `conflict`, invalid ArchiMate relationship → `validation_failed` with `operations[i]`, partially-
  invalid batch → atomic no-write); tool-name **snapshot** (`TempoModelingMcp` names) + append to
  the cross-package `AllToolNames_*` snapshot; registration test (`AddTempoModelingMcpTools` wiring).
- All new tests are registered/recorded via the test MCP tools per the plan's app rules.

## Worked example (agent walkthrough)

A headless run — the model lives in the host (PromptHelper implements
`IModelingModelDocumentProvider` + the library kind `Modeling`); the agent only sees JSON.

1. **Discover the vocabulary** — `modeling_list_notations` → e.g. `archimate` with its supported
   element/relationship/viewpoint types.
2. **Read the model** — `modeling_get_model_tree { modelId }` → elements, relationships and a
   `concurrencyToken`.
3. **Extend it** — `modeling_apply_operations` with the token:

   ```json
   [
     { "op": "add_element", "id": "billing", "name": "Billing", "semanticType": "ApplicationComponent", "notation": "archimate" },
     { "op": "add_relationship", "relationshipType": "Serving", "sourceElementId": "billing", "targetElementId": "customer" }
   ]
   ```

   A relationship the notation forbids (e.g. `Composition` between those types) fails the **whole
   batch** — `{"success":false,"error":"validation_failed","validationErrors":["operations[1] add_relationship: 'Composition' from 'ApplicationComponent' to 'BusinessActor' is not valid for notation 'archimate'. …"]}` —
   and nothing is saved.
4. **Verify** — `modeling_validate { modelId }` → `{"valid":true,"issues":[]}`.
5. **Render** — `modeling_get_view { modelId }` → `DiagramDocument` JSON, then
   `diagram_render_svg { documentJson, theme:"dark" }` → an embeddable SVG string.

Sample renderer output (committed): the light/dark golden files at
`tests/Tempo.Blazor.Tests/Diagram/DiagramSvgGolden/diagram-svg-basic-{light,dark}.svg` show the
exact `diagram_render_svg` output shape (nodes, edges, labels, arrow markers, theme palette). The
renderer has no UI of its own — the SVG string **is** the deliverable.

End-to-end coverage lives in `tests/Tempo.Blazor.Mcp.Tests/ModelingToolsTests.cs`,
`DiagramRenderToolsTests.cs` and `tests/Tempo.Blazor.Tests/Diagram/DiagramSvgRendererTests.cs`.

## Related references

- Existing MCP catalog & conventions: `docs/document-mcp-tools.md`
- Diagram MCP suite (pattern for `diagram_render_svg`): `src/Tempo.Blazor.Mcp/Diagram/`
- Wireframe headless SVG renderer (packaging/DI/determinism template):
  `src/Tempo.Blazor.Wireframe/Components/Wireframe/WireframeSvgRenderer.cs`
- Full-fidelity diagram SVG builder to promote:
  `src/Tempo.Blazor.Demo.Api/Services/DiagramExportSvgBuilder.cs`
- Notation rules & ArchiMate matrix:
  `src/Tempo.Blazor.Modeling/Components/Modeling/BuiltInModelingProfiles.cs`
