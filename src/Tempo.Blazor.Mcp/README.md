# Tempo.Blazor.Mcp

[Model Context Protocol](https://modelcontextprotocol.io/) tools that let an LLM design and read
Tempo.Blazor documents programmatically. The package includes tool sets for wireframes, diagrams,
DocumentEditor/NotionEditor content, and Tempo Reporting definitions.

For reporting, an agent can list stored reports, create a starter definition, apply structured
element operations, validate the definition against the runtime schema, and render a deterministic
PNG preview.

The package ships the tool implementations and their dependencies only — it is **transport- and
host-agnostic**. The host application owns the MCP server, the storage, and the wiring.

## Installation

```
dotnet add package Tempo.Blazor.Mcp
dotnet add package ModelContextProtocol.AspNetCore   # the host's transport + hosting
```

`Tempo.Blazor.Mcp` references `ModelContextProtocol.Core` (the `[McpServerTool]` attributes) and
`Tempo.Blazor.Abstractions`. The host adds `ModelContextProtocol.AspNetCore` (or any other
transport) to actually serve the tools.

## Hosting

```csharp
// 1. Supply the storage the tools read and write through (host-specific implementations).
builder.Services.AddSingleton<ITempoDocumentLibraryProvider, MyDocumentLibraryProvider>();
builder.Services.AddSingleton<IWireframeDocumentProvider, MyWireframeDocumentProvider>();
builder.Services.AddSingleton<IReportDefinitionStore, MyReportDefinitionStore>();
builder.Services.AddSingleton<IReportDataProvider, MyReportDataProvider>(); // optional; fallback returns one empty row

// 2. Register the tools' own dependencies.
builder.Services.AddTempoBlazorMcpTools();

// 3. Map the tools onto an MCP server.
builder.Services.AddMcpServer()
    .WithHttpTransport()                                            // streamable HTTP (stateful)
    .WithToolsFromAssembly(typeof(TempoWireframeMcp).Assembly);

var app = builder.Build();
app.MapMcp("/mcp");
```

> **Use `WithToolsFromAssembly`, not `WithTools(TempoWireframeMcp.ToolTypes)`.** The assembly scan
> advertises the `tools` capability in the initialize handshake; registering by type list does not,
> and clients then see *"Method tools/list not available"*. `ToolTypes` remains public for hosts
> with a custom registration path, but the assembly form is the supported default.

> **Keep the HTTP transport stateful** (do not pass `Stateless = true`). The streamable-HTTP
> handshake requires a session: the client sends `initialize`, the server returns an
> `Mcp-Session-Id` header, the client replies with a `notifications/initialized` notification and
> echoes that header on every subsequent call.

### Surfacing failures as tool results

The tools return their own `{ "success": false, ... }` envelopes for validation, not-found and
conflict cases (see [Result contract](#result-contract)). To convert *unexpected* exceptions —
and the `TempoDocumentConflictException` thrown by a concurrency-aware store — into the same
envelope instead of a JSON-RPC protocol error, add a call-tool filter:

```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithRequestFilters(filters => filters.AddCallToolFilter(next => async (ctx, ct) =>
    {
        try { return await next(ctx, ct); }
        catch (TempoDocumentConflictException ex)
        {
            return new CallToolResult { IsError = false, Content =
                [new TextContentBlock { Text = McpToolResults.Failure(McpToolResults.Conflict, ex.Message) }] };
        }
        catch (Exception ex)
        {
            return new CallToolResult { IsError = false, Content =
                [new TextContentBlock { Text = McpToolResults.Failure(McpToolResults.Error, ex.Message) }] };
        }
    }))
    .WithToolsFromAssembly(typeof(TempoWireframeMcp).Assembly);
```

## Implementing storage

The tools depend on two abstractions from `Tempo.Blazor.Abstractions`; the host implements both
over its own persistence (database, file store, in-memory, …):

| Abstraction | Responsibility |
|-------------|----------------|
| `ITempoDocumentLibraryProvider` | Browse / list metadata: folder tree, paged listing, single-entry lookup (`GetEntryAsync` returns `ModifiedAt` and the preview), plus optional create-folder / rename / delete advertised via `Capabilities`. |
| `IWireframeDocumentProvider` | Load and save the wireframe **payload** by id. Loading returns the document JSON; saving persists a built/validated design. |

**Optimistic concurrency contract.** `wireframe_get_document` returns `modifiedAt`, an opaque
write token. The write tools (`wireframe_apply_operations`, `wireframe_author_document`,
`wireframe_replace_document`) accept an optional `expectedModifiedAt`. A concurrency-aware store
should compare it against the current `ModifiedAt` (with ~1 ms tolerance for round-trip precision)
and throw `TempoDocumentConflictException` on mismatch — the filter above turns that into a
`conflict` result so the LLM can re-read and retry. Omitting `expectedModifiedAt` performs a
last-writer-wins save.

**Change notification (optional).** If the host also wires `Tempo.Blazor.Collaboration`, the
*store* (not the MCP tools) publishes an `ITempoDocumentChangePublisher` change after a successful
save. Open editors and embedded NotionEditor blocks subscribed via `ITempoDocumentChangeNotifier`
then refresh live. The tools deliberately do **not** publish, to avoid a double refresh.

## Tools

All tools take and return JSON strings. Successful responses merge their data at the top level
alongside `"success": true`; failures use the [result contract](#result-contract).

| Tool | Purpose |
|------|---------|
| `wireframe_list_components` | List placeable components (`compact=true` first to keep the response small; filter by `category`, `scopeAppId`, and `targetPackIds`). Results are paged (`hasMore`/`nextSkip`, default take 200). With `scopeAppId`/`targetPackIds`, app-scoped types are listed first. |
| `wireframe_get_component_schema` | Full property contract for one type (dimensions + every prop with type/default/allowed values). Suggests a correction when the type or known role synonym is misspelled. |
| `wireframe_get_authoring_guide` | Compact or full AI guide for canvas conventions, sizing, layouts, roles, and prop vocabulary; filter by `category`, `types`, `roles`, `scopeAppId`, `targetPackIds`, `skip`, and `take`. |
| `wireframe_list_documents` | List stored wireframes (id, name, folder, last-modified); filter by `folderPath` or `search`. |
| `wireframe_get_document` | Get one document: `modifiedAt` (the concurrency token) + the full document JSON. |
| `wireframe_create_document` | Create a new empty wireframe with a title; returns its id and `modifiedAt`. |
| `wireframe_validate_document` | Validate document JSON against the schema; returns `valid` + precise `validationErrors`. |
| `wireframe_apply_operations` | Apply an ordered batch of edit ops and save. Validated before persistence — nothing is saved if invalid. |
| `wireframe_author_document` | Author a full document in one call: resolves element `role` values to concrete component types, normalizes enum casing, clamps off-canvas elements, validates, saves, and returns warnings. |
| `wireframe_replace_document` | Replace the whole document with provided JSON and save (validated first; returns advisory warnings and supports `strict`). |
| `wireframe_get_implementation_brief` | Deterministic brief: each page's regions (header/sidebar/content/footer inferred from geometry), components used with counts, and navigation flows from connectors. |

Role-aware authoring lets agents describe intent with stable role slugs such as `search-input`,
`data-table`, or `otp-input` before selecting a concrete component. Built-in and app-scoped
schemas advertise `roles`; container-like schemas also advertise `isContainer`, which keeps
lint warnings focused on sibling overlap instead of expected containment. Unknown role mappings,
ambiguous matches, prop issues, enum normalization, off-canvas clamping, text overflow, and empty
required content are returned in `warnings`. Use `strict=true` when prop/enum warnings should
reject a write instead of saving a valid advisory result.

### Reporting tools

Reporting tools use `IReportDefinitionStore` for metadata/revisions. `AddTempoReportingMcpTools`
also registers preview fallbacks for `IReportDataProvider`, `ITextMeasurer`, and `ReportPdfRenderer`;
hosts should replace the data provider with their real report data source when previews need live
data.

| Tool | Purpose |
|------|---------|
| `list_reports` | List report definitions in a tenant/folder with optional search and paging. |
| `get_report_definition` | Load report metadata, a revision token, and canonical definition JSON. |
| `create_report` | Create a stored report from supplied JSON or from an AI-friendly starter definition. |
| `update_report_elements` | Apply ordered JSON operations (`setName`, `setDescription`, `setPageSetup`, `setBandHeight`, `clearBand`, `addElement`, `updateElement`, `replaceElement`, `removeElement`) and save a new revision. |
| `validate_report` | Validate a report definition and optionally return the prompt schema for all element types. |
| `render_report_preview` | Render a stored report or supplied definition JSON to a PNG preview (`contentType`, `base64`, page dimensions, page count). |

The prompt schema documents every runtime report element discriminator: `textBox`, `image`,
`shape`, `line`, `table`, `chart`, and `subReport`.

### Operations (`wireframe_apply_operations`)

`operationsJson` is a JSON array; each item carries an `op` discriminator:

`setTitle` · `addPage` · `updatePage` · `removePage` · `setCanvasSize` ·
`addElement` · `updateElement` · `removeElement` ·
`addConnector` · `updateConnector` · `removeConnector`

The whole batch is validated against the component schema **before** anything is saved, so a
partially-applied design is never persisted.

## Tool flow for an LLM

A typical design session:

1. **Discover** — `wireframe_get_authoring_guide` with `compact=true` and focused filters for
   roles/categories, or `wireframe_list_components` (compact) to see what is placeable, then
   `wireframe_get_component_schema` for the exact props of the types you'll use.
2. **Create** — `wireframe_create_document` → keep the returned `id`.
3. **Build** — either `wireframe_apply_operations` with `setCanvasSize` + role- or type-based
   `addElement`/`addConnector` ops, or `wireframe_author_document` for a single-shot
   whole-document draft with role resolution, enum normalization, clamping and warnings.
4. **Verify** — `wireframe_get_document` → `wireframe_validate_document` to confirm `valid: true`.
5. **Hand off** — `wireframe_get_implementation_brief` to turn the design into a structured brief
   (regions, components, flows) for building the real page.

For concurrent edits, pass the `modifiedAt` from step 4 as `expectedModifiedAt` on the next write
and handle a `conflict` result by re-reading.

### Reporting flow for an LLM

1. **Discover** — `list_reports` to find existing definitions, or start with `create_report`.
2. **Build** — `update_report_elements` with a compact operation array using element payloads with
   the report JSON discriminator `type`.
3. **Verify** — `get_report_definition` and `validate_report` (`includeSchema=true` when the agent
   needs the element contract).
4. **Preview** — `render_report_preview` and inspect the returned PNG bytes before handing the
   definition to a user.

## Result contract

```jsonc
// success — data merged at the top level
{ "success": true, "id": "…", "applied": 6 }

// failure
{ "success": false, "error": "validation_failed", "message": "…",
  "validationErrors": ["Unknown component type 'TmButtonX'. Did you mean 'TmButton'?"] }
```

Error codes: `not_found`, `validation_failed`, `conflict`, `error`. Build these envelopes with the
`McpToolResults` helpers (`Success`, `Failure`).

## Extending to other document kinds

The package is laid out by area (`Wireframe/…`, `Diagram/…`, `DocumentEditor/…`, `Notion/…`,
`Reporting/…`) so new document kinds can be added under their own namespaces against either the
shared document-library abstractions or a domain-specific store.
