# Atomic Notion editor authoring

This contract is the required authoring path in Tempo.Blazor 2.7. See the
[2.7 migration guide](notion-2.7-migration.md) for breaking changes.

`TmNotionEditor` requires `INotionAggregateProvider` as its canonical, optimistic
persistence boundary for logical changes that span multiple blocks. This is
required for editable tables and structured multi-block paste.

```razor
<TmNotionEditor DataProvider="@pages"
                AggregateProvider="@aggregates"
                InitialPageId="@pageId" />
```

The editor loads one complete `NotionPageSnapshot` when it opens a page. A
table row/column change, merge/split, cell edit, sort, row reorder, undo, redo,
or structured paste is applied to a clone, validated with
`NotionAggregateValidator`, and sent through exactly one
`INotionAggregateProvider.SaveAsync` request. The MCP authoring tools use the
same validator.

For durable MCP replay across process restarts, hosts may additionally implement
`INotionIdempotentAggregateProvider`. The MCP engine passes the complete operation
through `ExecuteIdempotentAsync`; the provider must invoke the callback with a
transaction-bound aggregate provider and commit the callback response receipt in
the same transaction as every aggregate write. Replays and hash collisions return
before the callback loads or saves a page. Providers that implement only
`INotionAggregateProvider` retain the process-local in-memory receipt fallback.

For host and consumer tests,
`Tempo.Blazor.NotionEditor.Testing.FakeNotionAggregateProvider` provides a
reference all-or-nothing implementation. It checks every page token before
committing any page and exposes save counters. Durable-replay tests should wrap
the fake in an `INotionIdempotentAggregateProvider` implementation with the same
transactional semantics as the production store.

## Conflict behavior

Saves use the loaded snapshot's concurrency token. If the provider reports a
conflict, the editor keeps the complete local candidate visible and prevents
another mutation from being layered over it. The user can:

- reload the current server aggregate and discard the local candidate; or
- load the current server aggregate, reapply the retained logical mutation,
  validate it again, and save once against the fresh token.

Provider and validation failures are shown in the editor. Persistence
exceptions are not treated as successful saves.

## Canonical table rendering

Canonical table cells support structured inline marks and links, background
and text colors, horizontal and vertical alignment, row/column spans, preferred
cell or column widths, and per-side borders. Dynamic HTML and CSS values are
sanitized or normalized before rendering. `DisplayHtml` is a transient,
non-serialized view derived from structured inlines; canonical `Html` remains
separate.

`AggregateProvider` is required for both editable and read-only editor instances.
There is no granular or sequential-write fallback.

## DOCX and document-model table fidelity

`DocumentModelToNotionConverter` writes table rows only through canonical
`RichCells`. `NotionToDocumentModelConverter` reads the same representation and
reconstructs physical continuation cells required by DOCX vertical merges.
Across `DOCX → DocumentModel → Notion → DocumentModel → DOCX`, the conversion
preserves cell content and order, inline marks and colors, cell fills,
horizontal and vertical alignment, row/column spans, preferred widths, and
supported per-side borders.

The regression fixture
`tests/Tempo.Blazor.DocumentFormats.Tests/TestData/KR.docx` is a byte-identical,
immutable copy of the externally supplied source. Its provenance, SHA-256, and
source location are recorded in `KR.provenance.json`. Tests pin both source
tables, including the 8-column `7 × 4` merged region and the 2-column Impact
color scale, and also verify the strict MCP `createTable` payloads.

Notion has no table-level width or table-level border representation.
Unsupported cell border syntax and other non-representable table details emit
`document.table.compatibility` warnings with a precise `SourcePath`; unsupported
DOCX cell-border styles emit `docx.tableBorderUnsupported`. Callers should
surface or log these warnings instead of treating the conversion as lossless.
