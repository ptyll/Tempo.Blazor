# Modeling authoring — design contract (full BPMN authoring, plan `eadbf967`)

This document is the **phase-1 output** of the plan *Tempo.Blazor Modeling — full BPMN authoring*
(`eadbf967-c7d1-437f-c06d-08df38c30cbd`): the verified current state, the designed public API
(everything PromptHelper and other hosts consume) and the decisions the later phases implement.
It is a **contract**, not implementation notes — phases 2–7 must not invent public shape outside
what is written here without superseding this document.

- Consumer that drives the requirements: the PromptHelper module *Procesní know-how*
  (app `3bc052519-a811-4c7e-b2de-d47088082b86`, decisions `DEC-PRC-001`,
  UC-PRC-002/003/008/009/012). PromptHelper hosts `TmModelingEditor` over **its own**
  `IModelingModelProvider` + `IModelingModelWriter`; everything the editor lacks is delivered
  **here**, never forked in PromptHelper.
- Target release: the first minor after 2.10.0 (`2.11.0`), shipped together with the separate
  BPMN 2.0 XML converter plan.
- Non-goals: BPMN 2.0 XML import/export (separate plan), real-time collaboration, server-side
  layout computation, an LLM inside the component packages.

Related documents: [modeling-editor.md](modeling-editor.md) (the component contract as shipped in
2.10.0), [modeling-mcp-tools.md](modeling-mcp-tools.md) (the MCP tool surface),
[diagram-editor.md](diagram-editor.md) (the embedded editor).

---

## 1. Verified current state (re-verified 2026-11)

All paths are relative to the repository root. Line references are to the tree this phase was
verified against.

### 1.1 Write contract (Fáze 14, decision `MODELING-INSPECTOR-EDITING`)

`src/Tempo.Blazor.Abstractions/Modeling/ModelingProviderContracts.cs`

- `IModelingModelWriter` (`:141`) — `ProviderKey` + `SaveChangesAsync(ModelingModelChangeSet,
  CancellationToken) → ModelingModelSaveResult`. `IModelingModelProvider` stays **read-only**; the
  editor resolves the writer **by provider key**.
- `ModelingModelChangeSet` (`:160`) — `ModelId`, `ProviderKey`, `BaseVersion` and **only**
  `Elements` / `Relationships` lists of *field changes on existing items*.
- `ModelingElementChange` (`:207`) — `Name?`, `Description?`, `SetProperties?`,
  `RemovedProperties?`. **No** semantic type, notation, `ParentId`, tags, source triple or
  governance.
- `ModelingRelationshipChange` (`:233`) — `Name?`, `SetProperties?`, `RemovedProperties?`. **No**
  re-targeting of `SourceElementId` / `TargetElementId` / `RelationshipType`.
- `ModelingModelSaveResult` (`:256`) — `Succeeded`, `IsConflict`, `Message`, `Model?`, `Issues`.
- The `null = unchanged` rule is explicit and load-bearing; the writer must distinguish
  "unchanged" from "changed to null/empty".

**Gaps for authoring:** no way to add or remove a semantic element/relationship, no way to change
`ParentId` (pool → lane), no way to persist layout (`ModelingViewDto`).

### 1.2 Editor editing surface today

`src/Tempo.Blazor.Modeling/Components/Modeling/TmModelingEditor.Editing.cs`

- Opt-in: `AllowModelEditing` **and** (a writer for the effective provider key **or** an
  `OnModelChanged` delegate) — `IsEditingEnabled` (`:105`). Without it every path here is inert
  (MO2 read-only, unchanged).
- `CaptureBaseline` (`:131`) snapshots **only** per-element `Name`/`Description`/`Properties` and
  per-relationship `Name`/`Properties`.
- `BuildPendingChangeSet` (`:178`) is a **diff against that baseline**, not an accumulator; the
  pending set is cached on `(_commitVersion, model, providerKey)`.
- Commit paths (`:322`, `:357`) push exactly one `DiagramCommandTransaction` per field commit and
  raise `OnModelChanged` with the commit's change set.
- Save (`:507`) captures load version + model reference + commit version before the writer await,
  discards a stale result, advances the baseline by the **sent** change set, adopts
  `result.Model` only when no commit raced.
- `SaveMode` — `Manual` (default, explicit save) / `Immediate` (save after every commit).

`src/Tempo.Blazor.Modeling/Components/Modeling/TmModelingInspector.razor.cs`

- `IsScalarEditable` (`:155`) = `String`, `Number`, `True`, `False`. Complex JSON (object/array)
  renders as read-only rows; `SemanticType`, `Notation`, `Source*`, `Governance` are never
  editable.
- Commits raise `OnElementEdited` / `OnRelationshipEdited` with only the changed fields.

`src/Tempo.Blazor.Modeling/Components/Modeling/Commands/`

- `UpdateModelingElementCommand` mutates the `ModelingElementDto` **in place** and mirrors
  `Data["name"]` / `Data["label"]` onto diagram nodes carrying `modelElementId`
  (`:139`), through a **document resolver** evaluated on every Execute/Undo.
- `UpdateModelingRelationshipCommand` does the same for edges by relationship id.

**Gap:** there is no `Add*`/`Remove*`/`MoveParent*`/`Update*View*` command, and nothing writes back
to `ModelingViewDto`.

### 1.3 "Open in editor" dialog — the palette inserts only a diagram node

`src/Tempo.Blazor.Modeling/Components/Modeling/TmModelingEditor.razor:947-961`

The dialog embeds `TmDiagramEditor` with `ToolbarPreset="Full"`, `ShowToolbox="true"` and the
notation palette pinned through `PreferredStencilIds="@NotationPaletteStencilIds"`. Edits flow out
through `DocumentChanged="UpdateOpenedEditorDocumentAsync"` (`TmModelingEditor.Dialog.cs:86`),
which only assigns `_openedEditorDocument` / `_document`.

Consequences (verified):

- A toolbox insert (or a drawn edge, or a node move/resize) mutates the **`DiagramDocument`**, not
  the semantic model. The `DiagramCommandStack` of the embedded editor is diagram-local.
- The **next generation** (`GenerateDiagram`, `TmModelingEditor.razor.cs:1086`) rebuilds the
  document from `ModelingModelDto` → the palette node silently disappears.
- Nothing reaches `ModelingModelChangeSet`, so a host never learns about the insert.
- `ParentId` is never assigned from the drop target, and no notation rule is consulted for a
  palette insert.

This is the single largest authoring gap and the reason phases 3–4 replace the "diagram node" insert
with a **semantic** insert.

### 1.4 `ModelingDiagramGenerator` and the view positions

`src/Tempo.Blazor.Modeling/Components/Modeling/ModelingDiagramGenerator.cs`

- `Generate` (`:50`) walks `model.Elements`, resolves a stencil (`ResolveNodeStencil`), and builds
  `DiagramNode`s. Per element it takes `X/Y` and `W/H` (when > 0) and `ParentNodeId` **from the
  selected view node**; with no view node it falls back to a deterministic grid
  (`GetGridPosition`, `:630` — 5 columns, 220×150 steps).
- `SelectView` (`:242`) returns **null when `options.ViewId` is empty** — only in that case does
  the generator ignore `model.Views` entirely.
- With a view selected, edges are iterated from `view.Connections` (in view order, unknown
  relationship ids become warnings); without a view, from `model.Relationships`.
- View connection `Waypoints` become edge `Waypoints` + `IsManuallyRouted = true` (`:325`).
- `GenerateDiagram` in the editor (`razor.cs:1086`) always passes `ResolveInitialViewId(model)`
  (`razor.cs:753`) — the viewpoint-matching view, else `Views[0]`, else null.

**Consequence:** a host that persists `ModelingViewDto` **already** gets its layout rendered — but
only for elements that carry a view node, and only because the editor picks a view. The editor
never *writes* those positions back (see 1.5), so a reload always shows the persisted (stale)
layout.

### 1.5 Layout is not persisted

- Node move/resize/waypoint drags happen inside the embedded `TmDiagramEditor`
  (`document.Nodes[i].X/Y/W/H`, `document.Edges[i].Waypoints`).
- `DocumentChanged` handlers (`UpdatePreviewDocumentAsync`,
  `TmModelingEditor.razor.cs:1003`, and `UpdateOpenedEditorDocumentAsync`,
  `TmModelingEditor.Dialog.cs:86`) assign the document; `CaptureBaseline` does not snapshot
  geometry; `BuildPendingChangeSet` never emits view changes.
- `ModelingViewDto` is therefore **read-only** in 2.10.0 — the demo writer's persisted model never
  sees an authored position.

### 1.6 MCP modeling suite

`src/Tempo.Blazor.Mcp/Modeling/`

- `ModelingOperationEngine` (`:12`, **internal static**) applies an ordered JSON batch atomically to
  a cloned `ModelingModelDto`; the caller persists only when `Success`.
  - Ops: `add_element`, `update_element`, `delete_element`, `add_relationship`,
    `update_relationship`, `delete_relationship` (`:43`).
  - `add_element` accepts `id`, `name`, `semanticType`, `notation`, `description`, `parentId`;
    **no** properties, tags, source triple, governance, view node, geometry.
  - `ParentId` validation (`:148`): parent must exist, must not be self, must not close a cycle.
  - **No silent cascade** (`:205`): deleting an element that still has referencing relationships or
    children is an error; the batch must delete/re-parent them first.
  - Relationship validation (`:337`) resolves endpoints and delegates to
    `IModelingRelationshipRulesProvider.ValidateRelationship` with the full
    `ModelingRelationshipRuleContext` (`ElementsById` included) — this is the rule path BPMN
    semantics already live on.
  - Errors are flat strings of the form `operations[i] <op>: <message>[ <suggestedFix>]`.
- Tools: `modeling_get_model_tree` (elements/relationships/views + `concurrencyToken`),
  `modeling_get_view` (delegates to a registered `IModelingDiagramProjector`),
  `modeling_apply_operations` (optimistic concurrency through
  `ITempoDocumentLibraryProvider.GetEntryAsync`, persistence through
  `IModelingModelDocumentProvider`).
- **Gap:** no view ops, no property ops (values are only merged through `update_element`'s fixed
  field list), and the operation vocabulary is a private, non-reusable implementation.

### 1.7 BPMN rules and the stencil mapper

`src/Tempo.Blazor.Modeling/Components/Modeling/BuiltInModelingProfiles.cs`

- `BpmnModelingCatalog` (`:1739`) — **27 element types** (`Task`…`TextAnnotation`, including
  `Pool`, `Lane`, `SubProcess`, `EventSubProcess`, `CallActivity`, `DataObject`, `DataStore`,
  `Group`, `TextAnnotation`) and **7 relationship types** (`SequenceFlow`, `ConditionalFlow`,
  `DefaultFlow`, `MessageFlow`, `Association`, `DataInputAssociation`, `DataOutputAssociation`).
- `BpmnRelationshipRulesProvider` (`:511`) — SequenceFlow/ConditionalFlow/DefaultFlow only between
  flow nodes and **never across Pool/SubProcess boundaries**; MessageFlow only between **different**
  pools; Association/Data* between known BPMN elements.
- `BpmnViewpointRulesProvider` (`:394`), `BpmnStencilMapper` (same file),
  `ModelingNotationCatalog` (`ModelingNotationCatalog.cs`, palette + legend, Fáze 15).
- The rules provider API is in Abstractions (`ModelingNotationProfiles.cs`), so a **shared** engine
  can call it without a Blazor dependency.

**Gap:** the editor never consults these rules (only the MCP engine and the generator do), so a
palette insert or a drag-drawn edge in the dialog can produce an invalid BPMN model.

### 1.8 Reader/viewer surface today

`TmModelingDiagramPreview` is the only read-only diagram surface: it embeds `TmDiagramEditor`
(`ToolbarPreset.Viewer`, `ReadOnly=true`) but keeps the editor's own chrome (header, edit actions,
`OpenInEditor` trigger) and lives inside the editor's layout. There is **no** standalone
read-only component, no highlight/diff overlay, no heatmap, no print/export path and no
keyboard-first element list. `IDiagramSvgRenderer`
(`src/Tempo.Blazor.Abstractions/Diagram/Services/IDiagramSvgRenderer.cs`) exists and is the
intended SVG export path (light/dark theme today).

### 1.9 Gap → phase map

| Verified gap | Phase |
|---|---|
| Change set cannot add/remove/re-parent or carry view changes | 2 |
| Operation semantics duplicated (MCP only) / unavailable to the editor | 2 |
| Writer capability unknown → the editor cannot hide unsupported actions | 2 |
| Palette insert and edge drawing create diagram-only artifacts; no notation validation | 3 |
| No layout persistence; hierarchy (pool/lane) not editable; BPMN visual conventions | 4 |
| No property schema, no host lookups, complex values read-only | 5 |
| No standalone read-only viewer (diff/heatmap/a11y/print) | 6 |
| MCP parity, docs, changelog, packaging | 7 |

---

## 2. Decisions produced by this phase

| Code | Title | Section |
|---|---|---|
| `MODELING-AUTHORING-CHANGESET` | Change set v2 — additive structural + layout changes, `null = unchanged`, one atomic batch | [§3](#3-d1--change-set-v2-modeling-authoring-changeset) |
| `MODELING-WRITER-CAPABILITIES` | Writer advertises capabilities (default interface member); the editor gates every new action on them | [§4](#4-d2--writer-capabilities-modeling-writer-capabilities) |
| `MODELING-PROPERTY-SCHEMA` | `IModelingPropertySchemaProvider` + `IModelingLookupProvider` in Abstractions, per provider key, typed entity refs | [§5](#5-d3--property-schema-and-host-lookups-modeling-property-schema) |
| `MODELING-UNIFIED-OPERATION-ENGINE` | **One** operation engine in Abstractions, used by the editor and `modeling_apply_operations`, same errors | [§6](#6-d4--unified-operation-engine-modeling-unified-operation-engine) |
| `MODELING-READONLY-VIEWER` | `TmModelingViewer` — chrome-less read-only viewer with diff/heatmap, a11y and print | [§7](#7-d5--tmmodelingviewer-modeling-readonly-viewer) |
| `MODELING-AUTHORING-COMPAT` | Everything is additive and opt-in; no host sees a behavior change without a new writer capability / schema provider | [§8](#8-breaking-changes-and-migration) |

---

## 3. D1 — Change set v2 (`MODELING-AUTHORING-CHANGESET`)

### 3.1 Shape

Extend `ModelingModelChangeSet` in `src/Tempo.Blazor.Abstractions/Modeling/ModelingProviderContracts.cs`
(new members are additive; the existing four keep their meaning):

```csharp
public sealed class ModelingModelChangeSet
{
    // existing, unchanged
    public string ModelId { get; set; }
    public string ProviderKey { get; set; }
    public string BaseVersion { get; set; }
    public List<ModelingElementChange> Elements { get; set; }            // updates of EXISTING elements
    public List<ModelingRelationshipChange> Relationships { get; set; }   // updates of EXISTING relationships

    // NEW — structure
    public List<ModelingElementAdd> AddedElements { get; set; }
    public List<ModelingRelationshipAdd> AddedRelationships { get; set; }
    public List<string> RemovedElementIds { get; set; }
    public List<string> RemovedRelationshipIds { get; set; }

    // NEW — layout (ModelingViewDto)
    public List<ModelingViewChange> ViewChanges { get; set; }

    // NEW — convenience, never serialized state ([JsonIgnore] — STJ serializes get-only properties)
    [JsonIgnore] public bool HasStructuralChanges => AddedElements.Count > 0 || AddedRelationships.Count > 0
        || RemovedElementIds.Count > 0 || RemovedRelationshipIds.Count > 0
        || Elements.Any(e => e.ParentId is not null)
        || Relationships.Any(r => r.SourceElementId is not null || r.TargetElementId is not null);
    [JsonIgnore] public bool HasViewChanges => ViewChanges.Count > 0;
}
```

```csharp
/// <summary>A NEW semantic element. The editor proposes a stable client id (the same id its
/// commands and the pending diff use); a writer may keep it or rewrite it and return the model.</summary>
public sealed class ModelingElementAdd
{
    public string Id { get; set; }                 // required, unique inside the model
    public string SemanticType { get; set; }       // required
    public string Name { get; set; }               // required
    public string Notation { get; set; }           // empty = the model's notation
    public string Description { get; set; }
    public string ParentId { get; set; }           // empty = root (pool/lane/subprocess containment)
    public Dictionary<string, JsonElement> Properties { get; set; }
}

/// <summary>A NEW semantic relationship between two elements that exist after this batch.</summary>
public sealed class ModelingRelationshipAdd
{
    public string Id { get; set; }                 // required, unique
    public string RelationshipType { get; set; }   // required
    public string SourceElementId { get; set; }
    public string TargetElementId { get; set; }
    public string Name { get; set; }
    public Dictionary<string, JsonElement> Properties { get; set; }
}

/// <summary>A layout change of ONE view. Upsert semantics per node/connection.</summary>
public sealed class ModelingViewChange
{
    public string ViewId { get; set; }             // empty = the model's default view
    public List<ModelingViewNodeChange> Nodes { get; set; }
    public List<string> RemovedNodeElementIds { get; set; }                 // view nodes removed, by ElementId
    public List<ModelingViewConnectionChange> Connections { get; set; }
    public List<string> RemovedConnectionRelationshipIds { get; set; }
}

public sealed class ModelingViewNodeChange
{
    public string ElementId { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Width { get; set; }
    public double? Height { get; set; }
    public string? ParentNodeId { get; set; }      // null = unchanged, "" = clear (root)
    // ParentNodeId holds the PARENT ELEMENT id (view nodes have no id of their own). The generator
    // maps it through its element→diagram-node map (a parent with a SourceId gets the SourceId
    // node id) — phase 4 fixes the generator, which today copies the value verbatim.
    // A node that does not exist in the view yet REQUIRES X and Y; Width/Height null = stencil default.
}

public sealed class ModelingViewConnectionChange
{
    public string RelationshipId { get; set; }
    public List<ModelingViewWaypointDto>? Waypoints { get; set; }  // null = unchanged, empty = clear
}
```

New member on `ModelingElementChange` (semantic hierarchy):

```csharp
/// <summary>New parent element id. null = unchanged, "" = make the element a root.</summary>
public string? ParentId { get; set; }
```

New members on `ModelingRelationshipChange` (re-targeting, needed when a lane move rewrites a
connection):

```csharp
public string? SourceElementId { get; set; }   // null = unchanged
public string? TargetElementId { get; set; }   // null = unchanged
```

### 3.2 Semantics

1. **`null` (and `null`-able member) always means "unchanged".** The rule that already governs
   `ModelingElementChange` extends to every new nullable member. `""` is a real value (clear the
   parent / clear a name / clear waypoints via an empty list) and must never be conflated with
   `null`.
2. **The batch is atomic.** The writer applies all-or-nothing and returns
   `Succeeded = false` + `Issues` on any validation failure. A `ModelingModelChangeSet` is one
   user-visible save, never a partial application.
3. **Application order** (fixed; the editor emits content in this order, the change-set → engine
   translation and every writer apply it in this order):
   1. `AddedElements` (list order; a parent must precede its children),
   2. `Elements` updates (including `ParentId`),
   3. `RemovedRelationshipIds`,
   4. `AddedRelationships` (list order; endpoints may be existing or just-added elements),
   5. `Relationships` updates (including re-targeting),
   6. `RemovedElementIds` (the editor lists them **descendants first**, so the engine's
      `still_has_children` check passes without a cascade),
   7. `ViewChanges`.
   Re-parents run before relationship adds because the engine validates a relationship
   **per operation** at the moment it is added/updated (2.10.0 MCP semantics, kept for parity) —
   a SequenceFlow is judged against the pool/lane structure that already includes the batch's
   moves. In `Authoring` policy the engine additionally re-validates, after the last operation,
   every relationship incident to an element whose `ParentId` changed in the batch
   (`invalid_relationship`, §9.3); `Strict` (MCP) gains the same check only in phase 7.
   An id appears in **at most one** of `Added*` / updates / `Removed*` per batch: the pending set
   is a diff against the baseline, so a delete-then-re-add of the same id is emitted as an update
   and an add-then-delete as nothing. A violating batch is rejected (`duplicate_id`).
4. **Removals are expanded by the editor, not inferred by the writer.** Deleting a `Lane` with
   tasks produces `RemovedElementIds` = the lane **and every descendant**, plus
   `RemovedRelationshipIds` = every incident relationship, plus the matching
   `ViewChanges.RemovedNodeElementIds` / `RemovedConnectionRelationshipIds`. The host therefore always sees
   the complete intent (its confirmation dialog and its audit log can show it) and a writer that
   cannot cascade is still correct. A writer **may** reject the batch (e.g. referential integrity,
   workflow semantics) with an issue on the offending id.
5. **Client ids are stable.** The editor mints ids (`Guid.NewGuid().ToString()`) before the save so
   undo/redo, the pending diff and the diagram commands already reference them; a writer that must
   own ids returns the saved `Model` from `ModelingModelSaveResult`, and the editor adopts it
   (existing `AdoptModelAsync` path). Writers are encouraged to keep the proposed id.
6. **Optimistic concurrency is unchanged** — `BaseVersion` (the loaded
   `Metadata.SourceVersion`) vs the current source version; `IsConflict` + optional `Model` keeps
   today's conflict UX.
7. **`ViewChanges` are layout, not content.** They never change semantics. Without the `Layout`
   capability the editor emits **no** geometry `ViewChanges` (moves stay session-only — today's
   behavior). A `Layout`-capable writer that still refuses a layout change returns a `Warning`
   issue (not a failure); the content part of the batch stays saved and the positions stay
   pending.
8. **Target view.** The editor always sends the **resolved** view id (`ResolveInitialViewId`), so a
   host with views gets `ViewId = <id>`. When the model has **no view**, the editor sends
   `ViewId = ""` and the writer/engine creates ONE view (`Id = "default"` unless the writer mints
   its own and returns the model, `Notation` = the model notation, `ViewpointKey` = the active
   viewpoint). Because the generator renders edges of a selected view **only** from
   `view.Connections`, the first `ViewChanges` entry for a not-yet-existing view is **seeded** by the
   editor: a node for every rendered element (at its current generated geometry) and a connection
   for every rendered relationship — otherwise every relationship without a connection would
   vanish on reload. For the same reason every `AddedRelationships` entry is paired with a
   connection upsert in the target view (§9.2). A writer without a view concept may persist
   geometry elsewhere.
9. **No new change-set members for source/governance data.** `SourceId`, `SourceType`,
   `SourcePath`, `Tags`, `Governance` of an added element are the **host's** business (a writer
   fills them); the editor never authors them.
10. **`OnModelChanged` carries the same object** (already true), so a host that writes itself gets
    the full structural set with no extra wiring.

### 3.3 JSON example (host-facing, `OnModelChanged` / MCP parity fixtures)

```json
{
  "modelId": "order-process",
  "providerKey": "prompthelper-processes",
  "baseVersion": "2026.11.3",
  "addedElements": [
    { "id": "el-1", "semanticType": "Pool", "name": "Order handling", "parentId": "" },
    { "id": "el-2", "semanticType": "Lane", "name": "Sales", "parentId": "el-1" }
  ],
  "addedRelationships": [
    { "id": "rel-1", "relationshipType": "SequenceFlow", "sourceElementId": "el-3", "targetElementId": "el-4" }
  ],
  "elements": [
    { "elementId": "el-3", "parentId": "el-2" }
  ],
  "relationships": [],
  "removedElementIds": [],
  "removedRelationshipIds": [],
  "viewChanges": [
    {
      "viewId": "view-default",
      "nodes": [
        { "elementId": "el-1", "x": 80, "y": 80, "width": 900, "height": 260 },
        { "elementId": "el-2", "parentNodeId": "el-1", "x": 110, "y": 110 }
      ],
      "removedNodeElementIds": [],
      "connections": [
        { "relationshipId": "rel-1", "waypoints": [ { "x": 420, "y": 200 } ] }
      ],
      "removedConnectionRelationshipIds": []
    }
  ]
}
```

Serialization uses the host's options; the DTO defaults must round-trip with
`System.Text.Json` (no converters required, `null`-able members omitted or `null`).
`ModelingDtoSerializationTests` gains the new types in phase 2.

---

## 4. D2 — Writer capabilities (`MODELING-WRITER-CAPABILITIES`)

```csharp
/// <summary>What an <see cref="IModelingModelWriter"/> can persist. Additive: an existing writer
/// that does not override this member is treated as <see cref="Content"/> only.</summary>
[Flags]
public enum ModelingModelWriterCapabilities
{
    None = 0,
    /// <summary>Rename, description and property values of existing items (2.10.0 behavior).</summary>
    Content = 1,
    /// <summary>Added/removed elements and relationships, parent (pool/lane) changes.</summary>
    Structure = 2,
    /// <summary>Persisted layout — view node geometry, connection waypoints, view nodes.</summary>
    Layout = 4,
    /// <summary>Content | Structure | Layout.</summary>
    All = Content | Structure | Layout
}

public interface IModelingModelWriter
{
    string ProviderKey { get; }

    /// <summary>Capabilities advertised to the editor. Default = Content (2.10.0 writers keep
    /// working, unchanged, without any source change).</summary>
    ModelingModelWriterCapabilities Capabilities => ModelingModelWriterCapabilities.Content;

    Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken);
}
```

A **default interface member** is the compatibility mechanism: adding it is source- and
binary-compatible for existing implementers, and a host that only supports content never sees
structurally impossible actions.

Editor gating (one place, `TmModelingEditor`, so no panel decides on its own):

| Editor action | Required capability | When absent |
|---|---|---|
| Inspector name/description/scalar property edit | `Content` | today's read-only inspector (MO2) |
| Schema-driven property fields, lookups | `Content` + a schema provider | today's scalar inputs; complex values stay read-only |
| Palette insert, delete, edge drawing, parent move | `Structure` | action hidden (`data-capability` attribute for tests/hosts), no silent diagram node |
| Node move/resize, waypoint drag, "Uspořádat" (auto-layout) | `Layout` | read-only geometry (today's behavior) |

Rules:

- Missing capability ⇒ the action is **not rendered** (not rendered-and-disabled), with the
  localized reason in the header hint (`TmModelingEditor_CapabilityMissing`), so a keyboard user
  never tabs onto a dead control.
- `AllowModelEditing` remains the global switch; capability flags are additive to it.
- One new editor parameter `WriterCapabilities` (`ModelingModelWriterCapabilities?`, default
  `null`). Effective capabilities = `WriterCapabilities ?? ResolvedWriter?.Capabilities ?? Content`;
  when a writer is resolved an explicit value is **intersected** with `writer.Capabilities` (a
  host can narrow, never widen, what its writer stores). An `OnModelChanged`-only host therefore
  stays content-only unless it passes e.g. `WriterCapabilities="ModelingModelWriterCapabilities.All"`
  — the editor cannot verify what such a host stores. There is no separate
  `AllowStructuralEditing` switch.
- `HasPendingModelChanges` (public) counts the new lists too (structure and view changes).

---

## 5. D3 — Property schema and host lookups (`MODELING-PROPERTY-SCHEMA`)

New Abstractions file `src/Tempo.Blazor.Abstractions/Modeling/ModelingPropertyContracts.cs`
(namespace `Tempo.Blazor.Modeling`). Everything is **optional**: with no provider registered the
inspector behaves exactly as in 2.10.0 (backward-compatibility test in phase 5).

### 5.1 Providers

```csharp
/// <summary>Provides the property schema (sections + typed fields) for a notation + semantic type.
/// Resolved by provider key, exactly like IModelingModelProvider / IModelingModelWriter.</summary>
public interface IModelingPropertySchemaProvider
{
    string ProviderKey { get; }
    Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken);
}

public sealed class ModelingPropertySchemaRequest
{
    public string ModelId { get; set; }
    public string Notation { get; set; }
    public string SemanticType { get; set; }
    public string ViewpointKey { get; set; }
    public string Culture { get; set; }
    /// <summary>Version the host previously served (null = latest). Lets the host pin a schema so
    /// a mid-session schema deploy cannot silently re-interpret a value.</summary>
    public string? SchemaVersion { get; set; }
}

/// <summary>Host lookups for entity-reference fields. One provider per provider key.</summary>
public interface IModelingLookupProvider
{
    string ProviderKey { get; }
    Task<ModelingLookupResultDto> LookupAsync(ModelingLookupRequest request, CancellationToken cancellationToken);
}
```

**Containment is a notation capability (phase 3 review A6).** `IModelingNotationProfile` gained a
DEFAULT interface member, so every existing profile keeps compiling and declaring no containment:

```csharp
public interface IModelingNotationProfile
{
    // …existing members…
    /// <summary>Semantic element types that CONTAIN other elements. Empty = no containment.</summary>
    IReadOnlyCollection<string> ContainerElementTypes => [];
}
```

The BPMN 2.0 profile overrides it with `Pool`, `Lane`, `SubProcess` and `EventSubProcess`. The
editor derives the insert dialog's container candidates, the drop target's `ParentId` and the
"inside the container's bounds" placement from THIS member — there is no hard-coded container list
in a component, so a custom notation declares its own containers.

### 5.2 Schema model

```csharp
public sealed class ModelingPropertySchemaDto
{
    public string SchemaId { get; set; }               // stable identity of this schema
    public string Version { get; set; }                // opaque, host-owned (e.g. "4")
    public string Notation { get; set; }
    public string SemanticType { get; set; }           // "" = applies to every type of the notation
    public List<ModelingPropertySectionDto> Sections { get; set; }
}

public sealed class ModelingPropertySectionDto
{
    public string Key { get; set; }
    public string Label { get; set; }
    public string? Help { get; set; }
    public int Order { get; set; }
    public bool CollapsedByDefault { get; set; }
    public List<ModelingPropertyFieldDto> Fields { get; set; }
}

public enum ModelingPropertyFieldType
{
    String, Markdown, MultilineText, Integer, Number, Boolean, Enum,
    Date, DateTime, Duration, Url,
    EntityReference, EntityReferenceList, StringList, StructuredList
}

public sealed class ModelingPropertyFieldDto
{
    public string Key { get; set; }                    // the ModelingElementDto.Properties key
    public string Label { get; set; }
    public ModelingPropertyFieldType Type { get; set; }
    public bool Required { get; set; }
    public bool ReadOnly { get; set; }
    public string? Help { get; set; }
    public string? Placeholder { get; set; }
    public string? Unit { get; set; }                  // "h", "days", "CZK" — display only
    public JsonElement? DefaultValue { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public int? MaxLength { get; set; }
    public string? Pattern { get; set; }               // anchored ECMA-ish regex, host-authored
    public List<ModelingPropertyOptionDto> Options { get; set; }   // Enum
    public ModelingLookupDescriptor? Lookup { get; set; }          // EntityReference / EntityReferenceList
    public List<ModelingPropertyFieldDto> ItemFields { get; set; } // StructuredList
    public int Order { get; set; }
}

public sealed class ModelingPropertyOptionDto { public string Value { get; set; } public string Label { get; set; } public string? Help { get; set; } public bool Disabled { get; set; } }

public sealed class ModelingLookupDescriptor
{
    public string LookupType { get; set; }             // host vocabulary: "role", "system", "document"
    public string? EntityType { get; set; }            // optional narrowing inside LookupType
    public int MinQueryLength { get; set; } = 2;
    public int PageSize { get; set; } = 20;
    public bool SearchRequired { get; set; } = true;   // false = offer the first page immediately
}
```

`StructuredList` uses `ItemFields` recursively (the "list of structured items" the plan asks for);
`EntityReferenceList` is a list of typed references, `StringList` a list of strings.

Stored JSON value per field type (what lands in `Properties[key]`, validated by §5.5): `String`,
`Markdown`, `MultilineText`, `Url` → JSON string; `Integer` / `Number` → JSON number; `Boolean` →
JSON `true`/`false`; `Enum` → JSON string = `Options.Value`; `Date` → string `yyyy-MM-dd`;
`DateTime` → ISO 8601 round-trip string with offset (`o`); `Duration` → JSON integer **minutes**;
`EntityReference` → object §5.4; `EntityReferenceList` / `StringList` / `StructuredList` → JSON
array (of §5.4 objects / strings / objects keyed by `ItemFields[].Key`).

### 5.3 Lookup contract

```csharp
public sealed class ModelingLookupRequest
{
    public string ModelId { get; set; }
    public string LookupType { get; set; }
    public string? EntityType { get; set; }
    public string SearchText { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; } = 20;
    public string Culture { get; set; }
    public string? ContinuationToken { get; set; }     // host-owned paging cursor (optional)
}

public sealed class ModelingLookupResultDto
{
    public List<ModelingLookupItemDto> Items { get; set; }
    public int TotalCount { get; set; }
    public bool HasMore { get; set; }
    public string? ContinuationToken { get; set; }
}

public sealed class ModelingLookupItemDto
{
    public string RefType { get; set; }                // usually = descriptor EntityType/LookupType
    public string RefId { get; set; }
    public string Display { get; set; }                // the label the user sees
    public string? Description { get; set; }
    public string? SecondaryText { get; set; }         // e.g. "Sales • CZ"
    public bool Disabled { get; set; }
}
```

### 5.4 How an entity-reference value is stored

`Properties[key]` holds a typed reference object (never a bare id, never HTML):

```json
{ "type": "role", "id": "role-42", "display": "Risk owner", "snapshotAt": "2026-11-06T09:24:33Z" }
```

`EntityReferenceList` holds an **array** of that shape. `display` is a snapshot so a portal can
render a value without a lookup call; the editor re-resolves the label on demand and a failed
re-resolution renders the snapshot plus a localized "reference may be stale" hint — it never
blocks the save. The **host may reject** a reference at save time (the writer returns an issue on
the field, `SourceElementId` set) — that is the contract the plan asks for.

### 5.5 Validation (shared)

New public helper in Abstractions:

```csharp
public sealed class ModelingPropertySchemaValidator
{
    /// <summary>Validates the given Properties against the schema. Returns issues (Category =
    /// "property", SourceElementId set, SuggestedFix = the field label). Unknown properties are
    /// reported as Info (host-added keys are allowed).</summary>
    public IReadOnlyList<ModelingIssueDto> Validate(
        ModelingPropertySchemaDto schema,
        IReadOnlyDictionary<string, JsonElement> properties);
}
```

Rules: `Required` (missing/empty/`null`), type match (JSON kind per field type; numbers accept
integer JSON for `Number` and reject `1.5` for `Integer`), `Min`/`Max`, `MaxLength`, `Pattern`,
`Enum` membership (`Options.Value`), `EntityReference` shape (`type`+`id` non-empty strings),
`StructuredList` item validation. The **same validator** is called by the editor (field-level
errors + save gate) and by the engine (§6) so MCP and the editor reject the identical value with
the identical issue — mandatory rule "one semantics".

### 5.6 Editor/engine integration

- Resolution: `IModelingPropertySchemaProvider` registered for `ProviderKey`; the editor resolves it
  like the writer. Cache key = `(providerKey, notation, semanticType, viewpointKey, culture,
  schemaVersion)`; a `null` schema falls back to today's behavior (scalars editable, complex
  read-only) — the backward-compatibility test.
- Render mapping (phase 5): `String` → `TmTextInput`, `MultilineText`/`Markdown` → the Tempo
  markdown editor, `Integer`/`Number` → `TmNumberInput`, `Boolean` → `TmToggle`, `Enum` →
  `TmSelect`, `Date`/`DateTime` → `TmDatePicker`/`TmDateTimePicker`, `Duration` → number + unit
  select (stored as **minutes**, integer), `Url` → `TmTextInput` with URL validation,
  `EntityReference` → `TmFilterableDropdown`/combobox with debounced async lookup,
  `EntityReferenceList` → `TmMultiSelect` (async + `AllowCreate=false`), `StringList` →
  repeated `TmTextInput` with add/remove, `StructuredList` → a repeatable nested form
  (`TmFormSection` + item actions).
- Required-field and type errors render inline (`aria-invalid` + described-by) and as
  `ModelingIssueDto` in the issues dock; saving is blocked while an `Error` issue exists
  (`SaveModelChangesAsync` returns `null` and announces the count).
- Localization: new `TmModelingInspector_*` keys in en/cs/sk/fr + `MockTmLocalizer` (phase 5 DoD).

---

## 6. D4 — Unified operation engine (`MODELING-UNIFIED-OPERATION-ENGINE`)

### 6.1 Location and shape

The engine moves from `Tempo.Blazor.Mcp.Modeling.ModelingOperationEngine` (internal) to
**`Tempo.Blazor.Abstractions`**, namespace `Tempo.Blazor.Modeling`, file
`src/Tempo.Blazor.Abstractions/Modeling/ModelingOperationEngine.cs` (+
`ModelingOperations.cs`). Abstractions already owns the DTOs, the notation rules interfaces and
`IModelingDiagramProjector`; both consumers already reference it
(`Tempo.Blazor.Mcp` → Abstractions; `Tempo.Blazor.Modeling` → `Tempo.Blazor` → Abstractions).
**No new project.** A new `Tempo.Blazor.Modeling.Abstractions` package was rejected: it would
split the DTO namespace for hosts and add a package for ~4 types.

```csharp
public sealed class ModelingOperationBatch
{
    public List<ModelingOperation> Operations { get; set; }
}

public sealed class ModelingOperationContext
{
    public IModelingRelationshipRulesProvider? RelationshipRules { get; set; }
    public IModelingViewpointRulesProvider? ViewpointRules { get; set; }
    public IModelingNotationProfileProvider? NotationProfiles { get; set; }
    /// <summary>Phase 3 (added while implementing §9.2): the notation the CALLER authors under.
    /// null = the model's own Notation (the MCP behavior). An editor whose host overrides the
    /// notation (a multi-notation model such as "mixed" rendered as "bpmn2") passes its effective
    /// key, so the picker's rule probe and the engine's verdict judge the same notation — without
    /// it the two disagree and the editor offers a connection the engine then refuses.</summary>
    public string? Notation { get; set; }
    /// <summary>Phase 3 review (A2): the viewpoint the CALLER authors under. null = the target
    /// view's ViewpointKey (Views[0] — the view authoring operations default to), else "". An
    /// editor whose host filters by viewpoint passes its effective key, so the type picker's rule
    /// probe and the engine's verdict judge the same viewpoint — a UML "Include" offered under
    /// UseCaseDiagram then also commits.</summary>
    public string? ViewpointKey { get; set; }
    /// <summary>Phase 5. The engine is synchronous, so the caller resolves schemas up front
    /// (IModelingPropertySchemaProvider is async) and passes a lookup by (notation, semanticType).
    /// Reference EXISTENCE is never checked here — that is the writer's job (§5.4).</summary>
    public Func<string, string, ModelingPropertySchemaDto?>? ResolvePropertySchema { get; set; }
    /// <summary>Editor batch (expanded cascades, geometry allowed) vs MCP batch (no cascade, no
    /// geometry unless asked).</summary>
    public ModelingOperationPolicy Policy { get; set; } = ModelingOperationPolicy.Strict;
}

public enum ModelingOperationPolicy
{
    /// <summary>MCP: no silent cascade, no view ops (until phase 7 enables them for MCP).</summary>
    Strict,
    /// <summary>Editor: view ops allowed. Removals are still never cascaded by the engine (Q3) —
    /// the caller expands them.</summary>
    Authoring
}

public sealed class ModelingOperationResult
{
    public bool Success => Errors.Count == 0;
    public int Applied { get; }
    public List<string> CreatedIds { get; }
    public List<ModelingOperationError> Errors { get; }
}

public sealed class ModelingOperationError
{
    public int Index { get; set; }              // operations[i]
    public string Op { get; set; }
    public string Code { get; set; }            // machine-readable, see 6.3
    public string Message { get; set; }         // the legacy formatted message (MCP parity)
    public string? SuggestedFix { get; set; }
    public string? ElementId { get; set; }
    public string? RelationshipId { get; set; }
    public string? PropertyKey { get; set; }
}

public static class ModelingOperationEngine
{
    /// <summary>Applies the batch IN PLACE to <paramref name="model"/> (2.10.0 MCP semantics). On
    /// failure the passed instance is in an undefined state, so atomic callers pass a
    /// <see cref="Clone"/> and swap on success. The CALLER persists: nothing here touches
    /// IModelingModelDocumentProvider or IModelingModelWriter.</summary>
    public static ModelingOperationResult Apply(
        ModelingModelDto model,
        ModelingOperationBatch batch,
        ModelingOperationContext? context = null);

    /// <summary>JSON front-end (MCP contract, kept byte-compatible).</summary>
    public static ModelingOperationResult ApplyJson(
        ModelingModelDto model,
        string operationsJson,
        ModelingOperationContext? context = null);

    /// <summary>Deep copy through System.Text.Json (the helper MCP uses today, moved here).</summary>
    public static ModelingModelDto Clone(ModelingModelDto model);

    /// <summary>Phase 3 review (A2): validates ONE relationship and returns the FIRST error, or null.
    /// The EDITOR's type picker and rejection text call this — never a hand-built rule context — so
    /// the picker can never offer a type the commit refuses ("one semantics, same errors"). The
    /// message carries the rules' own text WITHOUT the operations[i] prefix the batch path adds.</summary>
    public static ModelingOperationError? ValidateRelationship(
        ModelingModelDto model,
        ModelingRelationshipDto relationship,
        ModelingOperationContext? context = null);
}
```

The engine resolves the notation and the viewpoint ONCE, from the caller's overrides first
(`ModelingOperationContext.Notation` / `ViewpointKey`, phase 3 review A2) and the model's own
values second (`model.Notation`, the target view's `ViewpointKey`). MCP callers pass neither and
keep the 2.10.0 verdict byte-identical.

`ApplyJson` parses into `ModelingOperationBatch` and reports parse errors with the legacy wording
(`operations[i]: expected a JSON object.`, `operations[i]: missing 'op'.`). The MCP tool becomes a
~10-line caller: load model → clone → `ApplyJson` → persist or fail with
`result.Errors.Select(e => e.Message)`.

### 6.2 Operation catalog (existing ops unchanged, new ones additive)

Existing (message strings must stay byte-compatible, `ModelingOperationToolsTests` +
`DiagramOperationToolsTests` stay green; the optional `properties` / `removedProperties` members
are additive — see §8):

| `op` | Required | Optional |
|---|---|---|
| `add_element` | `name`, `semanticType` | `id`, `notation`, `description`, `parentId`, `properties`, `removedProperties` |
| `update_element` | `id` | `name`, `semanticType`, `description`, `notation`, `parentId` (`""` clears), `properties`, `removedProperties` |
| `delete_element` | `id` | — (no cascade; referencing relationships and children must go first) |
| `add_relationship` | `relationshipType`, `sourceElementId`, `targetElementId` | `id`, `name`, `properties`, `removedProperties` |
| `update_relationship` | `id` | `relationshipType`, `sourceElementId`, `targetElementId`, `name`, `properties`, `removedProperties` |
| `delete_relationship` | `id` | — |

The property payload is ONE contract on all four collection operations (`add_element`,
`update_element`, `add_relationship`, `update_relationship`) and on `set_properties`: both members
are optional, absent/`null` means "do not touch properties", `properties` is a JSON object of values
(the JSON value kind is preserved) and `removedProperties` is a JSON array of keys to remove. A
writer therefore never needs a follow-up `set_properties` op — `ModelingChangeSetTranslator` puts
the values directly on the add/update op. A MALFORMED payload (`properties` that is not an object,
`removedProperties` that is not an array of strings) fails the operation with `property_type`
BEFORE any field of the element/relationship is written.

New (phases 2, 4, 5, 7):

| `op` | Required | Optional | Policy |
|---|---|---|---|
| `set_properties` | `id` (element or relationship — resolved by id), `properties` | `removedProperties` | both |
| `set_view_node` | `elementId`, `viewId?` | `x`, `y`, `width`, `height`, `parentNodeId` (`""` clears) | `Authoring`, MCP in phase 7 |
| `remove_view_node` | `elementId`, `viewId?` | — | `Authoring` |
| `set_view_connection` | `relationshipId`, `viewId?` | `waypoints: [{x,y}]` (`[]` clears) | `Authoring` |
| `remove_view_connection` | `relationshipId`, `viewId?` | — | `Authoring` |

(`viewId` omitted/`""` = the **target view**: `Views[0]`… see §3.2 rule 8 — note this is NOT the
generator's `SelectView` meaning, where an empty id renders the grid without any view.)

Auto-layout is **not** an engine op: the layout is computed by dagre in the browser
(`TmDiagramEditor.RunLayoutAsync`) and server-side layout is a non-goal. The editor's "Uspořádat"
action runs the layout, then commits the resulting geometry as ONE transaction of `set_view_node`
/ `set_view_connection` ops (one undo step). MCP clients that want a layout send computed
positions the same way.

### 6.3 Error codes (`ModelingOperationError.Code`)

`parse` · `unknown_op` · `missing_field` · `duplicate_id` · `not_found` · `parent_cycle` ·
`parent_not_found` · `still_referenced` · `still_has_children` · `invalid_relationship` ·
`unsupported_relationship` · `viewpoint_disallowed` · `property_required` · `property_type` ·
`property_range` · `property_pattern` · `property_enum` · `property_reference` ·
`unsupported_operation` (a policy rejection, e.g. a view op in `Strict`).

The `Message` keeps the 2.10.0 format so existing MCP clients and tests keep working:

```
operations[2] add_relationship: 'SequenceFlow' from 'Task' to 'Pool' is not valid for notation 'bpmn2'. BPMN SequenceFlow can connect only BPMN flow nodes. Connect tasks, events, gateways, subprocesses, or call activities with SequenceFlow.
```

### 6.4 Parity contract (mandatory rule "one semantics, same errors")

- The **editor** never re-implements a rule: every structural/geometry commit goes through a
  `DiagramCommand` that calls `ModelingOperationEngine.Apply` on the live model (or validates the
  proposed state through the very same internal helpers the engine exposes for single operations).
- Phase 7 ships a **parity test**: the same logical batch applied through the editor path and
  through `modeling_apply_operations` yields the same model JSON and the same error list
  (`Message` + `Code`).
- Notation rules come only from `IModelingRelationshipRulesProvider` /
  `IModelingViewpointRulesProvider` (BPMN rules stay in
  `BpmnRelationshipRulesProvider`) — no ad-hoc checks in components.

---

## 7. D5 — `TmModelingViewer` (`MODELING-READONLY-VIEWER`)

New component `src/Tempo.Blazor.Modeling/Components/Modeling/TmModelingViewer.razor(.cs/.css)`,
namespace `Tempo.Blazor.Components.Modeling`. No model tree, no inspector, no editor chrome, no
writer: a **presentation** surface for portals, documentation and diffs.

### 7.1 Parameters

```csharp
[Parameter, EditorRequired] public ModelingModelDto Model { get; set; }          // semantic model (required)
[Parameter] public string? ViewId { get; set; }                                  // null = default view / grid
[Parameter] public ModelingViewerFitMode FitMode { get; set; } = ModelingViewerFitMode.Width; // Width | Page
[Parameter] public double? Zoom { get; set; }                                    // null = fit
[Parameter] public bool ShowZoomControls { get; set; } = true;
[Parameter] public IReadOnlyCollection<string>? HighlightElementIds { get; set; } // element ids (nodes) and relationship ids
[Parameter] public string? SelectedElementId { get; set; }                       // two-way
[Parameter] public EventCallback<string?> SelectedElementIdChanged { get; set; }
[Parameter] public EventCallback<ModelingViewerElementEventArgs> OnElementClick { get; set; }
[Parameter] public ModelingDiffOverlayDto? DiffOverlay { get; set; }
[Parameter] public ModelingHeatmapDto? Heatmap { get; set; }
[Parameter] public bool ShowLegend { get; set; } = true;                         // reuses ModelingNotationCatalog
[Parameter] public bool ShowElementList { get; set; } = true;                    // the keyboard/text alternative
[Parameter] public RenderFragment<ModelingViewerElementContext>? ElementListContent { get; set; } // host slot replacing the default list row
[Parameter] public string Height { get; set; } = "480px";
[Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;
[Parameter] public string? Class { get; set; }
[Parameter] public string? AriaLabel { get; set; }
```

```csharp
public enum ModelingViewerFitMode { Width, Page }

public sealed class ModelingViewerElementEventArgs
{
    public string ElementId { get; set; }            // relationship ids resolve to the edge's relationship id
    public bool IsRelationship { get; set; }
    public string SemanticType { get; set; }
    public string Name { get; set; }
}

public sealed class ModelingDiffOverlayDto
{
    public IReadOnlyCollection<string> AddedElementIds { get; set; }
    public IReadOnlyCollection<string> RemovedElementIds { get; set; }
    public IReadOnlyCollection<string> ChangedElementIds { get; set; }
    public IReadOnlyCollection<string> LayoutOnlyElementIds { get; set; }   // changed position only
    public IReadOnlyCollection<string> AddedRelationshipIds { get; set; }
    public IReadOnlyCollection<string> RemovedRelationshipIds { get; set; }
    /// <summary>Optional "before" model. Removed ids are resolved against it so the legend and the
    /// element list can NAME removed items (they are listed, never drawn).</summary>
    public ModelingModelDto? BaselineModel { get; set; }
    public string? LegendTitle { get; set; }
}

public sealed class ModelingHeatmapDto
{
    /// <summary>Normalized value 0..1 per element id (or relationship id). Missing = no tint.</summary>
    public IReadOnlyDictionary<string, double> Values { get; set; }
    public string? ValueLabel { get; set; }          // "SLA risk", formatted with the value
    public string? LegendMinLabel { get; set; }
    public string? LegendMaxLabel { get; set; }
    public string? Format { get; set; }              // composite format for the value, e.g. "{0:P0}"
}
```

### 7.2 Rendering approach (no DiagramEditor internals)

1. Project the model exactly like the editor: the injected `ModelingDiagramGenerator` instance,
   `Generate(model, new ModelingDiagramGenerationOptionsDto { ViewId = resolvedViewId,
   IncludeIssues = true })` → `DiagramDocument`.
2. Build a **presentation copy** of the document (clone by value; never mutate the caller's model
   or a cached document) and apply overlays through the public model only:
   - `DiagramNode.Style` / `DiagramEdge.Style` — `Stroke`, `Fill`, `StrokeWidth`,
     `StrokeDasharray`, `Opacity`; colors are `var(--tm-…)` token expressions, so light/dark and
     high-contrast themes work and no hard-coded color enters the package.
   - `Data["viewerState"]` (`"highlight" | "diff-added" | "diff-changed" | "diff-layout" |
     "diff-removed" | "heatmap"`) for tests/hosts and CSS hooks (`data-viewer-state`); removed
     removed items no longer exist in `Model`, so they are **not drawn**; they appear in the diff
     legend count and, when `DiffOverlay.BaselineModel` is supplied, by name in the element list
     (a "Removed" group).
   - Heatmap tint: interpolate the four-step scale
     (`--tm-color-heat-0..3` tokens, added in phase 6) → `Style.Fill` with `Opacity`.
3. Render with `TmDiagramEditor` in **viewer mode** — a small, explicit set of public parameters:
   `ReadOnly="true"`, `ToolbarPreset="Viewer"`, `ShowToolbox="false"`,
   `ShowPropertiesPanel="false"`, `ShowLayersPanel="false"`, `ShowPageTabs="false"`,
   `ShowMinimap="false"`, `ShowToolbar="@ShowZoomControls"`, `FitOnDocumentLoad`,
   `ShowMobileZoomOverlay` and `OnSelectedIdsChanged`. **No DiagramEditor change is planned:**
   highlight parameters such as `HighlightNodeIds`/`HighlightEdgeIds` are not needed because the
   viewer restyles the presentation copy instead. `DocumentChanged` is not bound (read-only).
4. Selection: `OnSelectedIdsChanged` → map diagram ids to model ids through
   `Data["modelElementId"]` / the edge id (the generator writes `modelElementId` on nodes and uses
   relationship ids as edge ids). `SelectedElementId` two-way; the presentation copy restyles the
   selected element.
5. `FitMode` → `DiagramFitMode.Width`/`Page` (the DE already distinguishes "container width only"
   from "whole page").

### 7.3 Accessibility (phase 6 DoD)

- The viewer renders a **text alternative list** (`ShowElementList`, default `true`) — a real
  `<ul>` of buttons in topological order (BFS from the start elements over relationships, ties by
  model order), each `aria-label="<type> <name>"`, with an `aria-describedby` hint. It is visible
  (not `sr-only`) on the mobile band and collapsible elsewhere; `ElementListContent` lets a host
  replace the row.
- Keyboard: the list is the tab path (arrow keys move, Enter/Space raises `OnElementClick` and sets
  `SelectedElementId`); the DE canvas remains usable with its own keyboard model, but the viewer
  never relies on it.
- Every overlay carries a text equivalent: the diff legend lists counts per state, the heatmap
  legend lists the scale plus the value of the selected element.
- Live region: a polite `aria-live` announces selection and overlay changes.

### 7.4 Print and export

- `TmModelingViewer.razor.css` gets an `@media print` block: white background, no zoom controls, no
  element list (unless `ShowElementList` and a `print` opt-in), page-break avoidance for the canvas.
- Public `Task<string> ExportSvgAsync(DiagramSvgTheme theme = DiagramSvgTheme.Light)` calls the
  injected `IDiagramSvgRenderer.RenderSvg` (already in Abstractions, registered by
  `AddTempoBlazorDiagramEditor`) on the presentation copy — no new renderer. A standalone SVG
  cannot resolve `var(--tm-…)`, so before rendering the viewer resolves every overlay token it
  used to a concrete color through the package JS module (`resolveTokens(root, names)` →
  `getComputedStyle`, under the requested theme) and writes those values into an export-only
  copy. Without JS (prerender) the export omits the overlay styling and keeps `data-viewer-state`.

### 7.5 Wireframe role/stencil (phase 6)

A `modeling-viewer` **wireframe UI role + component type** (`TmModelingViewer` with props
`modelId`, `viewId`, `fitMode`, `showLegend`) is registered through the existing wireframe
component catalog so a wireframe can reserve the viewer's place in a page layout. No runtime
coupling: the wireframe editor only needs the type/prop schema.

---

## 8. Breaking changes and migration

**No breaking change is introduced.** The whole plan is additive + opt-in (decision
`MODELING-AUTHORING-COMPAT`).

| Change | Kind | Migration |
|---|---|---|
| `ModelingModelChangeSet` gains `AddedElements`, `AddedRelationships`, `RemovedElementIds`, `RemovedRelationshipIds`, `ViewChanges`, `HasStructuralChanges`, `HasViewChanges` | additive | none — an existing writer that ignores them keeps working and the editor never sends them without the `Structure`/`Layout` capability |
| `ModelingElementChange.ParentId`, `ModelingRelationshipChange.SourceElementId/TargetElementId` | additive nullable | none (`null` = unchanged) |
| `IModelingModelWriter.Capabilities` (default member `Content`) | additive, default impl | none — no source change for existing writers; a structural host overrides it |
| `IModelingPropertySchemaProvider`, `IModelingLookupProvider`, schema DTOs, validator | new API | none — no provider = 2.10.0 behavior |
| `ModelingOperationEngine` moves `Tempo.Blazor.Mcp.Modeling` (internal) → `Tempo.Blazor.Modeling` (public, Abstractions) | internal type removed | none publicly; MCP keeps its tool output (message strings unchanged) |
| `properties` / `removedProperties` on `add_element`, `add_relationship`, `update_element`, `update_relationship` (the same payload `set_properties` takes) | additive member support — the property payload was silently IGNORED in 2.10.0 | none for a well-formed payload — it is now applied; a MALFORMED payload (`properties` not an object, `removedProperties` not an array of strings) fails the op with `property_type` instead of being skipped |
| `ModelingChangeSetTranslator.ToOperationBatch` (change set → engine batch, in the documented order, plus the `duplicate_id` self-contradiction check) | new public API (Abstractions) | none — the demo writer is its first caller; a host writer may keep its own translation |
| New MCP ops (`set_properties`, `set_view_*`) + `Capabilities`-driven policy | additive | none — existing ops/strings unchanged |
| `TmModelingViewer`, `ModelingDiffOverlayDto`, `ModelingHeatmapDto` | new component/types | none |
| Editor behavior | unchanged unless a host opts in (`AllowModelEditing` + writer capabilities + schema provider) | MO2 and 2.10.0 read-only behavior is the default and is test-guarded |

Compatibility tests that must stay green without edits: `ModelingDtoSerializationTests`
(existing types), `TmModelingInspectorTests`, `TmModelingEditorEditingTests`,
`tests/Tempo.Blazor.Mcp.Tests` modeling specs.

---

## 9. Sequences

### 9.1 Create a BPMN process from scratch (phases 3–4)

1. The palette in the open-editor dialog lists `ModelingNotationCatalog.GetPalette` entries; the
   user taps/drops one on the canvas (or uses "Insert element > type > container", the non-drag
   path). The notation palette only renders when the effective writer advertises `Structure`.
2. **As built (phase 3 review).** The embedded `TmDiagramEditor` mutates its document; the editor
   diffs it against a `StructureSnapshot` captured after the last render/generation and translates
   the STRUCTURAL part into semantic commands. The snapshot records the document page
   (`StructureSnapshot.PageId`, A3) plus node id → model element id and the edge ids:
   - a **page switch/add** inside the dialog is navigation, not a gesture — the mismatch re-captures
     the snapshot and returns (the open-editor dialog hides the page tabs while structure editing is
     on, so the gesture does not even exist there);
   - an **added node** is a palette insert (`modelElementId` absent = a fresh palette shape; a node
     carrying `modelElementId` is the session-only tree→preview reuse and never semantic);
   - a **removed node/edge** is a delete request, but only the element ids NO LIVE node still
     carries (A4 — a duplicated placement keeps its element) and only edge ids that ARE relationship
     ids;
   - anything else (a move, a label edit) stays session-only.

   The editor maps the stencil id back to `SemanticType`, resolves the container through the
   notation profile's `ContainerElementTypes` (A6) → `ParentId`, mints an element id and pushes
   **one** `DiagramCommandTransaction` ("Insert element") containing `AddModelingElementCommand`
   (plus, for a viewless model, the `SeedModelingViewCommand` below).
3. `AddModelingElementCommand` calls the engine (policy `Authoring`): semantic element + the view
   node at the drop point (view `ParentNodeId` = the container's node id, view `Width`/`Height`
   defaulted) — both in the same transaction, so one undo removes both. The non-drag insert places
   the node INSIDE the container's rendered bounds (X = container.X + 30, Y = container.Y + 12, a
   24px collision nudge clamped to those bounds; a lane spans its pool's width below the last lane)
   and GROWS the container with a second `set_view_node` in the same command when the node no longer
   fits — the growth is reported as a second view-node change and reverted by the same undo.

   Every batch is validated on a CLONE through `ModelingOperationEngine` with the editor's context
   (effective `Notation` **and** `ViewpointKey`, §6.1); the pending set diffs EVERY view (A9), so a
   save reports the layout of all of them, not just the target.
4. **Viewless model (A1).** Before the first structural commit the editor prepends
   `SeedModelingViewCommand` to the SAME transaction: one `set_view_node` per rendered document node
   (its current geometry and semantic parent) plus one `set_view_connection` per rendered edge whose
   id is a relationship id. The seed's operations join the clone-validation batch and its changes
   join the intent's `ViewChanges` for the engine-created `"default"` view; `Undo` removes exactly
   the view the seed created. The editor's `TargetViewId` is `"default"` from then on.
5. The pending diff (`BuildPendingChangeSet`) picks it up as `AddedElements` + a `ViewChanges`
   entry; the model tree, the preview and the dialog regenerate from the model.
6. Manual save → `IModelingModelWriter.SaveChangesAsync` (`Structure | Layout`); the host persists
   and returns the saved `Model`; the editor adopts it and clears the stack.
7. Without a `Structure` capability the palette in the dialog is hidden and only the semantic
   regeneration remains (today's read-only experience).

### 9.2 Draw a relationship with notation validation (phase 3)

1. Edge handle drag (or "Connect to > target") → the editor probes every candidate type through
   `ModelingOperationEngine.ValidateRelationship(model, relationship, context)` (phase 3 review A2)
   — the SAME call the commit uses, with the editor's effective notation and `ViewpointKey` — and
   renders only the accepted ones in the type picker. There is no hand-built rule context in the
   editor: the picker cannot offer a type the engine would refuse.
2. A disallowed combination is rejected **before** the commit: the editor shows the engine's own
   message + `SuggestedFix` in an alert and a live region (the connect sheet shows it inline in a
   danger card); nothing enters the undo stack.
3. On confirm: `AddModelingRelationshipCommand` (engine validation again, so MCP and editor agree)
   → `AddedRelationships` + a `ViewChanges.Connections` entry for the resolved view **always**
   (with a view selected the generator renders only `view.Connections`, so a relationship without a
   connection entry would vanish on reload; `Waypoints` stays `null` when none were drawn).
4. Delete: `DeleteModelingSelectionCommand` (renamed from `DeleteModelingElementCommand` — it
   removes relationships too) expands the cascade (children + incident relationships) and asks for
   confirmation. The confirmation names what goes away ("Delete “Sales”?" plus a grammatical
   plural summary), the confirm action is the danger variant, the body says aloud what the cascade
   counts, and focus returns to the next tree row (or the header Delete action) when it closes.
   The removal touches EVERY view (A9): each view's nodes/connections go in its own explicit
   `viewId` operation and the change set carries one `ModelingViewChange` per affected view; undo
   restores each DTO into its ORIGINAL position (A7), so the element/relationship/view-node id
   sequences are unchanged by a delete → undo pair.
5. The engine's relationship verdict uses the same `ViewpointKey` the picker's probe used (A2).

### 9.3 Move a task between lanes (phase 4)

1. Drop on another lane → the editor computes `ParentId` (semantic) and `ParentNodeId` (layout).
2. One transaction: `MoveModelingElementParentCommand` (engine `update_element parentId`) **and**
   the view node change → `Elements[].ParentId` + `ViewChanges.Nodes[].ParentNodeId` in the same
   pending set, one undo step.
3. The host validates BPMN semantics (e.g. a SequenceFlow that would now cross a pool → the engine's
   `invalid_relationship` code); a rejected batch keeps the pending edit and shows the issues.

### 9.4 Persist layout (phase 4)

1. A node move/resize/waypoint drag inside the embedded editor mutates the document.
2. After a debounce (~400 ms) the editor diffs the document geometry against the generated baseline
   and pushes `SetModelingViewNodeCommand` transactions (one per gesture, not per pointer move).
3. `ViewChanges` accumulate in the pending set; `HasViewChanges` drives the "unsaved layout"
   indicator. Without the `Layout` capability no `ViewChanges` are produced (see §3.2 rule 7).
4. Reload → the provider returns the persisted view → the generator renders the saved geometry;
   elements without a view node still fall back to the grid (auto-layout for position-less
   elements only — phase 4 requirement).

### 9.5 Edit a schema-driven property with a lookup (phase 5)

1. Selection → editor asks `IModelingPropertySchemaProvider.GetSchemaAsync` (cached per
   notation/type/culture/version) → renders sections/fields per §5.6.
2. An entity-ref field queries `IModelingLookupProvider.LookupAsync` with debounce, `MinQueryLength`
   and paging (`Skip`/`Take`/`ContinuationToken`).
3. Commit → the typed reference JSON (§5.4) goes into `SetProperties`; the schema validator runs
   first (required/type/range) and blocks the commit on `Error`.
4. The writer may reject the reference (unknown/deleted entity) → issue on the element + field;
   the value stays pending and the user can re-pick.
5. No schema registered → the inspector is exactly the 2.10.0 one (scalars only).

---

## 10. Phase → deliverable map

| Phase | Delivered against this document |
|---|---|
| 2 | §3 change set v2 + §4 capabilities + §6 engine (typed, JSON front-end, `set_properties`) moved to Abstractions; MCP refactored to a caller with byte-identical output; `DemoModelingModelWriter` extended |
| 3 | §9.1, §9.2 — semantic palette insert/delete/relationship drawing, cascade confirmation, non-drag alternatives, localization, E2E. As implemented: `ModelingOperationContext.Notation` + `ViewpointKey` (§6.1) so the editor's overrides and the engine agree; `ModelingOperationEngine.ValidateRelationship` is the ONE relationship verdict the picker and the commit share; a viewless model is seeded by `SeedModelingViewCommand` in the same transaction; the cascade delete names its target, touches every view, restores original ordering on undo and is confirmed through an `alertdialog`; the authoring sheets are an INTERNAL renderer (no public component, no JSON documentation entry); `IModelingNotationProfile.ContainerElementTypes` owns containment (§5.1) |
| 4 | §9.3, §9.4 — layout persistence, hierarchy moves, auto-layout as an undoable op, BPMN visual conventions |
| 5 | §5 + §9.5 — schema provider, lookups, shared validator, inspector rendering, demo schema |
| 6 | §7 — `TmModelingViewer`, overlays, a11y, print/SVG, wireframe role, demo page |
| 7 | §6.4 parity tests, docs updates, CHANGELOG, packaging smoke test, release |

---

## 11. Resolved questions (architect review, phase 1)

The plan's mandatory rule is "public API first in docs, confirmed with the user". The user was not
available during phase 1, so the architect review resolved every open point with the most
compatible / simplest option. The resolutions are binding for phases 2–7; a later user deviation is
recorded as a superseding decision.

- **Q1 — Engine placement → `Tempo.Blazor.Abstractions`** (namespace `Tempo.Blazor.Modeling`). No
  new package. The JSON helpers the engine needs (operation-array parsing, model clone) move with
  it as `internal` helpers; `Tempo.Blazor.Mcp` keeps its own `McpJsonHelpers` for the other tools.
- **Q2 — Capability advertisement → default interface member** `IModelingModelWriter.Capabilities`
  (default `Content`). No second writer interface.
- **Q3 — Removal cascade → the editor expands it** into the change set (and into engine
  operations). Writers and the engine never cascade silently; the engine keeps the MCP
  `still_referenced` / `still_has_children` errors in every caller.
- **Q4 — Entity-reference storage → `{type, id, display, snapshotAt}` JSON object in
  `Properties`** (array for `EntityReferenceList`). No new member on `ModelingElementDto`.
- **Q5 — Viewer rendering → restyled presentation copy over `TmDiagramEditor` viewer mode.** No
  bespoke renderer and no DiagramEditor change.
- **Q6 — Release coupling → ship together as `2.11.0`** with the BPMN 2.0 XML converter plan
  (phase 7 release gate stays "after user confirmation").

The six decisions in §2 record these answers; their `reviewDisposition` moves from `Pending` to
`Approved` when the user (or the plan owner) signs off.

---

## 12. Testing strategy (phases 2–7)

- **Unit / bUnit** (`tests/Tempo.Blazor.Tests/Modeling`): serialization round-trip of the new
  change-set types + `null = unchanged`; engine operations and every error code; capability gating
  (a content-only writer renders no structural action); schema validator cases; viewer projection,
  overlays, keyboard order and empty model.
- **MCP** (`tests/Tempo.Blazor.Mcp.Tests`): the existing modeling specs stay **unmodified** (byte
  parity), new specs for `set_properties`/view ops + parity fixtures (§6.4).
- **E2E** (`tests/Tempo.Blazor.E2E`, `Modeling*`): only the specs of the screens touched by the
  phase, plus the new `/modeling-viewer` page; screenshots (light/dark, desktop/tablet/mobile) as
  the phase DoDs require.
- **Docs/JSON overlay**: `JsonDocumentation/Packages/Tempo.Blazor.Modeling` overlay for the new
  public types (phases 2 and 6) with `validate --fail-on-drift` clean (phase 2 DoD).
- **Node lane**: unchanged (`npm run test:diagram-editor` covers the embedded engine the viewer
  reuses).
