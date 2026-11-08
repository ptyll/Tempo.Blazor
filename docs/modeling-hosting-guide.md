# Hosting the modeling editor with a structural writer (2.11.0)

This guide is the implementation recipe for a host that lets its users **author** a model — build a
BPMN process from scratch, draw flows, move tasks between lanes, persist layout and edit
schema-driven properties — instead of only rendering one. It complements
[modeling-authoring.md](modeling-authoring.md) (the design contract) and
[modeling-editor.md](modeling-editor.md) (the component contract); it is written against the
2.11.0 surface that ships with the full BPMN authoring plan (`eadbf967`).

The reference consumer is PromptHelper's *Procesní know-how* module. The reference implementation
is the demo writer, `DemoModelingModelWriter`
(`src/Tempo.Blazor.Modeling/Components/Modeling/DemoModelingModelWriter.cs`), and the same pattern
appears as the parity harness in `tests/Tempo.Blazor.Mcp.Tests/ModelingMcpParityTests.cs`.

---

## 1. What the host owns

| Piece | Contract | Required for |
|---|---|---|
| Read the model | `IModelingModelProvider` (or a loader delegate / `ModelLoaderOverride`) | always |
| Persist edits | `IModelingModelWriter` — **you implement this** | any editing |
| Declare what you store | `IModelingModelWriter.Capabilities` (`Content`, `Structure`, `Layout`) | every action is gated on it |
| Property schema | `IModelingPropertySchemaProvider` | schema-driven inspector fields |
| Entity lookups | `IModelingLookupProvider` | reference fields (roles, systems, catalogues) |
| MCP access (optional) | `ITempoDocumentLibraryProvider` + `IModelingModelDocumentProvider` | agents editing the model |

The editor never writes to a database itself: every structural gesture produces a
`ModelingModelChangeSet`, and a host writer applies it all-or-nothing.

## 2. Register the providers

One registration per model provider key — the editor resolves the writer, the schema provider and the
lookup provider **by the same key**:

```csharp
services.AddTempoBlazorModeling();                      // profiles, generator, notation rules

services.AddSingleton<IModelingModelProvider, MyModelProvider>();        // read path
services.AddSingleton<IModelingModelWriter, MyModelWriter>();            // write path
services.AddSingleton<IModelingPropertySchemaProvider, MyPropertySchemaProvider>();
services.AddSingleton<IModelingLookupProvider, MyLookupProvider>();

// Optional MCP surface (agents):
services.AddTempoModelingMcpTools();
services.AddSingleton<ITempoDocumentLibraryProvider, MyDocumentLibrary>();
services.AddSingleton<IModelingModelDocumentProvider, MyModelDocumentProvider>();
```

The editor enables editing only when `AllowModelEditing="true"` **and** a writer for the effective
provider key (or an `OnModelChanged` delegate) resolves. Without a capability the corresponding
action is **not rendered** — a keyboard user never tabs onto a dead control.

## 3. Advertise capabilities honestly

```csharp
public sealed class MyModelWriter : IModelingModelWriter
{
    public string ProviderKey => "my-app/processes";

    // Content only means: rename/description/properties of EXISTING items (the 2.10.0 surface).
    // Add Structure for add/remove/parent changes, Layout for persisted geometry.
    public ModelingModelWriterCapabilities Capabilities =>
        ModelingModelWriterCapabilities.Content
        | ModelingModelWriterCapabilities.Structure
        | ModelingModelWriterCapabilities.Layout;

    public async Task<ModelingModelSaveResult> SaveChangesAsync(
        ModelingModelChangeSet changes, CancellationToken cancellationToken) { /* §4 */ }
}
```

`Capabilities` is a **default interface member** (`Content`), so a 2.10.0 writer keeps compiling and
behaving unchanged. A host that only wants to edit names/schemas leaves it alone; the palette, the
edge tool, delete, the parent move and the Arrange action then stay hidden.

`WriterCapabilities` on `TmModelingEditor` can NARROW what the writer stores (an explicit value is
intersected with `writer.Capabilities`) — never widen it.

## 4. Apply one change set atomically

This is the whole save path. It is ~40 lines because the ordering and the validation already live in
`Tempo.Blazor.Abstractions` — do not re-derive them:

```csharp
public async Task<ModelingModelSaveResult> SaveChangesAsync(
    ModelingModelChangeSet changes, CancellationToken cancellationToken)
{
    // 1. OPTIMISTIC CONCURRENCY. BaseVersion is the Metadata.SourceVersion the editor loaded.
    var current = await _store.GetVersionAsync(changes.ModelId, cancellationToken);
    if (!string.Equals(changes.BaseVersion, current, StringComparison.Ordinal))
    {
        return new ModelingModelSaveResult
        {
            Succeeded = false,
            IsConflict = true,
            Message = $"The model moved from '{changes.BaseVersion}' to '{current}'.",
            Model = await _store.LoadAsync(changes.ModelId, cancellationToken)  // the reload path
        };
    }

    // 2. ONE application order (added elements → element updates → removed relationships →
    //    added relationships → relationship updates → removed elements → view changes) plus the
    //    duplicate_id self-contradiction check.
    var translation = ModelingChangeSetTranslator.ToOperationBatch(changes);
    if (!translation.Success)
    {
        return Failure(translation.Errors);
    }

    // 3. Validate against the loaded model, then apply to a CLONE — a failed batch writes nothing.
    var persisted = ModelingOperationEngine.Clone(await _store.LoadAsync(changes.ModelId, cancellationToken));

    //    The engine is synchronous; the schema provider is async, so preload every (notation, type)
    //    pair the model AND the batch can judge, including a brand-new type the model lacks.
    var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
        _schemas,
        persisted,
        ModelingPropertySchemaResolver.PropertySchemaPairs(persisted, translation.Batch),
        persisted.Views.FirstOrDefault()?.ViewpointKey,
        CultureInfo.CurrentUICulture.Name,
        cancellationToken);

    var outcome = ModelingOperationEngine.Apply(
        persisted,
        translation.Batch,
        new ModelingOperationContext
        {
            RelationshipRules = _relationshipRules,   // notation rules (BPMN, ArchiMate, UML, ERD…)
            ViewpointRules = _viewpointRules,
            ResolvePropertySchema = resolver,
            Policy = ModelingOperationPolicy.Authoring  // view ops + post-batch relationship re-check
        });

    if (!outcome.Success)
    {
        return Failure(outcome.Errors);   // nothing is persisted; the editor keeps the pending set
    }

    // 4. Commit, advance the source version and return the saved model (the editor adopts it).
    var nextVersion = await _store.SaveAsync(changes.ModelId, persisted, current, cancellationToken);
    persisted.Metadata.SourceVersion = nextVersion;

    return new ModelingModelSaveResult
    {
        Succeeded = true,
        Message = string.Empty,
        Model = ModelingOperationEngine.Clone(persisted)
    };
}
```

Rules this path encodes:

- **Removals are already expanded.** A cascade delete arrives with every descendant and every
  incident relationship in `RemovedElementIds` / `RemovedRelationshipIds` (descendants first) and
  with the matching view removals. The engine never cascades silently — it still reports
  `still_referenced` / `still_has_children` for a batch that forgot something, so a hand-built batch
  (an MCP agent's, a test's) is rejected too.
- **One id, one bucket.** An id may appear in at most one of `Added*` / updates / `Removed*` per
  batch; the translator reports `duplicate_id` otherwise and returns an EMPTY batch.
- **Layout is optional.** Without the `Layout` capability the editor emits no `ViewChanges` and every
  move stays session-only (the 2.10.0 behavior). A `Layout`-capable writer that refuses a layout
  change returns a `Warning` issue — the content part of the batch is still expected to persist.
- **Client ids are yours to keep.** The editor mints stable ids before the save (`Guid` strings); keep
  them when you can. If your backend must own ids, return the saved `Model` and the editor adopts it
  through its existing path.

## 5. Map engine errors into your issue list

Every rejection is a `ModelingOperationError` carrying a machine-readable `Code`
(`invalid_relationship`, `property_required`, `parent_cycle`, `still_has_children`, …), the
human-readable `Message` (the SAME text the MCP tool returns) and the offending id. Map them onto
`ModelingIssueDto`; the editor renders them in its issues dock and gates the next save on `Error`
severity:

```csharp
private static ModelingModelSaveResult Failure(IReadOnlyList<ModelingOperationError> errors) => new()
{
    Succeeded = false,
    Message = errors.Count > 0 ? errors[0].Message : string.Empty,
    Issues = [.. errors.Select((error, position) => new ModelingIssueDto
    {
        Id = $"{position}-{error.Index}-{error.Code}",   // stable identity for the list
        Severity = ModelingIssueSeverity.Error,
        Category = "validation",
        SourceElementId = error.ElementId ?? string.Empty,
        SourceRelationshipId = error.RelationshipId ?? string.Empty,
        Message = error.Message,
        SuggestedFix = error.SuggestedFix ?? string.Empty
    })]
};
```

A property-schema rejection is a `property_*` code whose message names the field LABEL; the
inspector's own inline errors use the localized `ModelingPropertyIssueText.Localize` text, and the
two read the same `ModelingIssueDto` (the shared validator, see §6).

## 6. Schema-driven properties and lookups

When `IModelingPropertySchemaProvider` is registered for the key, the inspector renders ordered
sections with typed fields (`String`, `MultilineText`, `Markdown`, `Integer`, `Number`, `Boolean`,
`Enum`, `Date`, `DateTime`, `Duration`, `Url`, `EntityReference`, `EntityReferenceList`,
`StringList`, `StructuredList`). Rules that matter to a writer:

- **Validate the FULL set on save.** The editor's live commands intentionally run no property
  resolver (§5.6 of the design contract): every commit already passed the inspector's gate, and
  re-validating mid-edit would flag a required field the user has not filled yet. The SAVE is where
  the whole set is judged — through `ModelingPropertySchemaResolver` above, which is the same
  `ModelingPropertySchemaValidator` the MCP tool uses. Property parity is defined there.
- **Entity references are stored as values, not ids.** `Properties[key]` holds
  `{ "type": "role", "id": "role-42", "display": "Risk owner", "snapshotAt": "…" }` (an array for
  `EntityReferenceList`), so a portal renders the label without a lookup call. Re-resolution only
  refreshes the LABEL; the stored snapshot is never rewritten by the editor.
- **You may reject a stored reference.** Return an issue on the field (`Category = "property"`,
  `SourceElementId` / `SourceRelationshipId` set, `SuggestedFix` = the field key) and keep the value
  pending in the editor; the user re-picks. A failed re-resolution in the UI is only a "may be
  stale" hint, never a blocked save.
- **Unknown keys are allowed** (reported as `Info`), so host-added metadata survives.
- **`IModelingLookupProvider`** serves the search (debounced 300 ms, per-field cancellation,
  continuation pages) and an optional `ResolveAsync` batch for label refresh; `ResolveAsync` has a
  default member returning `null`, so a search-only provider keeps compiling.

## 7. MCP parity — the same semantics for agents

`modeling_apply_operations` is the SAME engine, now under the SAME policy the editor's save uses
(phase 7): view (layout) operations are accepted and a parent change re-validates the relationships
incident to it after the batch. A host that exposes both surfaces therefore cannot drift:

- One batch applied through MCP and the equivalent change set saved through `IModelingModelWriter`
  produce the **same model JSON** and the **same error messages/codes** — pinned by
  `tests/Tempo.Blazor.Mcp.Tests/ModelingMcpParityTests.cs`.
- `modeling_get_property_schema` reads the schema with the same provider resolution and request
  shape the batch is judged with, so an agent can discover required fields before writing.
- The success payload carries the applied diff (`addedElementIds`, `removedElementIds`,
  `changedElementIds`, `layoutOnlyElementIds`, the relationship ids and the counts), computed by the
  headless `ModelingModelDiff.Compute(baseline, current)` in Abstractions — the same helper a host
  uses to feed `TmModelingViewer.DiffOverlay`.
- The MCP write path persists through `IModelingModelDocumentProvider` with the library
  `ModifiedAt` as its optimistic token; the editor's writer uses its own `BaseVersion`. Both refuse a
  stale write before anything is applied.

See [modeling-mcp-tools.md](modeling-mcp-tools.md) for the catalog and
[modeling-authoring.md](modeling-authoring.md) §6.4 for the parity contract.

## 8. Editor-side behavior you inherit for free

- **Pending set, not an accumulator.** `HasPendingModelChanges` is a diff against the loaded
  baseline; a delete-then-re-add of the same id is emitted as an update, an add-then-delete as
  nothing.
- **Discard** undoes the pending transactions; **Save** sends one `ModelingModelChangeSet`; with
  `SaveMode="Immediate"` every commit saves (each commit is its own batch).
- **A conflict** keeps the pending edits. The editor shows the conflict alert with the writer's
  message and a reload action; a returned `Model` is the model the host is actually on.
- **One undo stack** covers semantic and layout changes (one gesture = one undo step; a burst of
  keyboard nudges over the same element set merges).
- **No view yet?** The editor seeds `"default"` in the SAME transaction as the first insert
  (`SeedModelingViewCommand`) so the first authored element and its placement cannot half-persist.

## 9. Verifying your writer

Copy the shape of the MCP parity harness:

1. Seed your model, construct the change set a gesture would produce (added pool → lane → task,
   a relationship, a view node), and assert `Succeeded` plus the persisted JSON.
2. Send an invalid batch (a flow across pools, a missing required property, a stale `BaseVersion`)
   and assert the error `Code`, the untouched store and that nothing was persisted.
3. Run the same logical batch through `modeling_apply_operations` (if you expose MCP) and compare the
   two models/errors — that is the parity test the library itself runs.

`tests/Tempo.Blazor.Tests/Modeling/ModelingChangeSetTranslatorTests.cs`,
`ModelingOperationEngineTests.cs` and `ModelingPropertySchemaValidatorTests.cs` show the expected
codes for every edge; `DemoModelingModelWriterTests` is the in-repo reference writer's suite.

**Clean-host smoke (run for 2.11.0).** The release candidate was packed locally
(`dotnet pack src/Tempo.Blazor.Abstractions src/Tempo.Blazor src/Tempo.Blazor.DiagramEditor
src/Tempo.Blazor.Modeling src/Tempo.Blazor.DocumentFormats src/Tempo.Reporting.Abstractions
src/Tempo.Reporting.Engine src/Tempo.Blazor.Mcp -c Release -o artifacts/local-pack-<version>`) and
consumed from a throwaway console host outside the repository — a `NuGet.config` pointing at that
folder, `<PackageReference Include="Tempo.Blazor.Modeling" Version="…" />` +
`Tempo.Blazor.Mcp`, no project references. The host ran 23 checks and all passed:
`AddTempoBlazorModeling()` registers the real BPMN rules; its own writer (Structure + Layout) applies
a `Pool`/`Lane`/`UserTask`/`SequenceFlow` change set with view geometry; a cross-pool
`SequenceFlow` is refused with `invalid_relationship`; a missing required schema field is refused
with `property_required`; a stale `BaseVersion` is a conflict carrying the current model;
`modeling_apply_operations` accepts structural + view operations and returns the diff;
`modeling_get_property_schema` reads the host schema; and `ModelingModelDiff.Compute` runs without
the Blazor UI. Repeat this run against the packages before publishing a new version — it is the only
check that exercises the PACKAGED assemblies (the in-repo suites use project references).

## 10. Demo

`/modeling-editor` (WASM, Server, InteractiveAuto) runs this whole contract against
`DemoModelingModelWriter` with `?editing=1`; `?scenario=bpmn2-*` starts from an empty BPMN model,
`?scenario=step-attributes` exercises the property schema, and `?scenario=save-conflict` arms the
optimistic-concurrency refusal.
