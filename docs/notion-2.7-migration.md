# Migrating Notion authoring to Tempo.Blazor 2.7

Tempo.Blazor 2.7 makes complete page aggregates the only persistence boundary
for structured Notion authoring. The release deliberately has no runtime
upgrade shim for legacy table rows or partial multi-block writes.

## Breaking table-row change

`ITableRowBlockContent.Cells` and `TableRowBlockContent.Cells` were removed.
Use the canonical rich-cell collection:

```csharp
var row = new TableRowBlockContent
{
    RichCells =
    [
        new NotionTableCell
        {
            Html = "<strong>Risk</strong>",
            BackgroundColor = "#fef3c7",
            RowSpan = 1,
            ColSpan = 1
        }
    ]
};
```

Migrate persisted legacy rows before upgrading the runtime. Tempo.Blazor 2.7
does not inspect or reconstruct a `cells: string[]` payload. HTML and Markdown
importers now create `RichCells` directly, and exporters, search, page
analytics, demo data, and document conversion read only `RichCells`.

The MCP `createTable.rows[].cells` field remains valid. It represents logical
`NotionAuthoringTableCell` origins in the strict authoring wire contract; it is
not the removed `TableRowBlockContent.Cells` CLR property. Covered merge slots
are renderer details and must never be sent as additional logical cells.

Custom `INotionTableCell` implementations must also implement the 2.7 rich
contract members `Inlines`, `TextColor`, `HorizontalAlignment`,
`VerticalAlignment`, `Width`, and `Borders`. The package API-validation
suppressions enumerate only these approved Notion table breaks and the removed
flat-cell accessors across all three target frameworks.

## Atomic persistence boundary

Implement `INotionAggregateProvider` and register it with the Notion editor and
MCP host. A save must:

1. receive complete replacement snapshots for every affected page;
2. validate every snapshot before changing durable state;
3. compare every opaque `BaseConcurrencyToken`;
4. persist all pages in one transaction or persist none; and
5. return a new opaque token and digest for each committed page.

`FakeNotionAggregateProvider` in
`Tempo.Blazor.NotionEditor.Testing` is an executable reference for consumer
tests:

```csharp
var provider = new FakeNotionAggregateProvider([initialSnapshot]);
var engine = new NotionAtomicAuthoringEngine(
    provider,
    compiler,
    new InMemoryNotionIdempotencyReceiptStore());
```

The fake demonstrates multi-page all-or-nothing conflict handling, defensive
snapshot copies, deterministic `sha256:` digests, and token advancement.

`INotionAggregateProvider` alone uses the MCP engine's process-local receipt
fallback. A host that requires replay after restart must implement the optional
`INotionIdempotentAggregateProvider` contract. Its `ExecuteIdempotentAsync`
implementation must:

1. scope the key by the host tenant/application plus the supplied operation scope;
2. return the stored opaque response without invoking the callback when the key
   and canonical request hash match;
3. return `Collision` without invoking the callback when the key belongs to a
   different hash;
4. invoke the callback with an aggregate provider bound to the same transaction;
5. atomically commit every callback aggregate write and the exact response receipt;
6. roll back both writes and receipt when the callback throws or cancellation wins;
7. serialize concurrent calls for the same scoped key so the callback runs once;
8. expire receipts only after the requested retention interval.

The callback form is intentional: provider-generated concurrency tokens are part
of the final MCP response, so a receipt written before `SaveAsync` returns cannot
represent an exact replay. Retry the identical request with the same idempotency
key, but use a new key after rebuilding a request following a concurrency conflict.

`INotionBlockProvider`, the `BlockProvider` component parameter, and the demo
`/api/notion/blocks` endpoints were removed. Pass only `AggregateProvider` to
`TmNotionEditor` and `TmNotionPublicPage`. Interactive block operations are
translated by the editor into one complete aggregate save per logical change.

## MCP migration

Use this sequence:

1. call `notion_get_block_tree`;
2. retain each page's `concurrencyToken`;
3. discover exact schemas through `notion_get_block_schema`,
   `notion_get_operation_catalog`, or `notion_get_authoring_guide`;
4. submit one closed operation array to `notion_apply_block_operations`; and
5. use the returned canonical readback and new page versions.

Granular block-write tools and permissive payload aliases are not part of the
2.7 authoring contract. Unknown operation fields, missing explicit identities,
invalid merge geometry, unsafe HTML/CSS, stale tokens, and reuse of an
idempotency key with different operations fail with structured issues. Do not
add compatibility endpoints that translate partial writes into sequential
provider calls.

## Release checklist

- Update all Tempo.Blazor packages participating in the deployment to 2.7.1 when durable direct
  MCP replay is required; 2.7.0 hosts retain only the process-local fallback.
- Migrate stored flat table rows before deploying the new assemblies.
- Exercise a multi-page conflict and verify no partial page is persisted.
- Exercise an ambiguous retry and verify the provider save count remains one.
- Verify machine-readable MCP discovery output in the deployed host.
- Run the supported .NET 8, 9, and 10 build/test matrix before publishing.

Creating a Git tag and publishing packages remain explicit release-owner
actions; building and validating 2.7.1 packages does not publish them.
