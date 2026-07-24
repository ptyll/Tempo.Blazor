# Notion MCP authoring contract

This is the Tempo.Blazor 2.7 contract. See the
[2.7 migration guide](notion-2.7-migration.md) before upgrading an existing
provider or persisted table-row payload.

This catalog documents the canonical Notion MCP surface. Read nested schemas through the discovery
tools instead of embedding complex JSON Schema types in MCP arguments: tool arguments intentionally
remain primitive strings, booleans, integers and timestamps.

## Read before write

Call `notion_get_block_tree` before an atomic edit. Retain the returned `page.id`,
`concurrencyToken` and `digest`. The block response is a recursive tree ordered by sibling `order`
and stable block id. A table embeds its persisted `tableRow` children under
`content.rows`; every row contains logical origin `cells`, including formatting and spans, and never
contains covered-cell merge markers.

Submit the token through `expectedPageVersionsJson`:

```json
[
  {
    "pageId": "11111111-1111-1111-1111-111111111111",
    "concurrencyToken": "opaque-token-from-read"
  }
]
```

## MCP tools

### `notion_list_pages`

Lists pages in the selected parent, favorite, recent, trash, label or app scope.

### `notion_get_page`

Reads page metadata through the page provider. Use `notion_get_block_tree` for canonical authoring
state and optimistic-concurrency metadata.

### `notion_create_page`

Creates a page under an optional parent page.

### `notion_update_page`

Updates page metadata.

### `notion_delete_page`

Moves a page to trash.

### `notion_restore_page`

Restores a page from trash.

### `notion_move_page`

Moves a page below another page or to the root.

### `notion_duplicate_page`

Duplicates page metadata through the page provider.

### `notion_get_block_tree`

Returns the complete canonical recursive block tree, logical table rows/cells, schema version,
opaque concurrency token, digest and provider load issues.

### `notion_apply_block_operations`

Applies one strict operation array atomically. The request needs a stable `idempotencyKey`,
`operationsJson`, and normally the latest `expectedPageVersionsJson`.
The result includes the new page versions, deterministic reference mappings,
structured issues, and canonical readback. No granular block-write alias or
sequential compatibility path is available.

### `notion_list_block_types`

Lists every canonical block type as a compact summary. Call `notion_get_block_schema` for the
complete field metadata of one selected type.

### `notion_get_block_schema`

Returns required/optional/null/default semantics, enums, nested fields, limits, styles and an
executable example for one block type. For tables, inspect both `Table` and `TableRow`.

### `notion_get_operation_catalog`

Returns the strict field vocabulary and an executable example for one operation or for all
operations.

### `notion_get_authoring_guide`

Returns machine-readable guidance for recursive children, rich tables, patch, move, concurrency and
idempotent retry.

## Atomic operations

Every operation is a closed JSON object: unknown fields are validation errors. `clientRef` is
optional and maps created, updated or deleted identifiers in the result.

#### `createBlock`

Required: `op`, `pageId`, `block`. Optional: `clientRef`, `parentBlockId`, `order`. A strict block
contains `type`, `content`, and optional recursive `children`.

#### `createBlocks`

Required: `op`, `pageId`, `blocks`. Optional: `clientRef`, `parentBlockId`, `order`. Array order is
the new sibling order.

#### `createTable`

Required: `op`, `pageId`, `columnCount`, `rows`. Optional: `clientRef`, `parentBlockId`, `order`,
`hasHeaderRow`, `hasHeaderColumn`, `columnAlignments`, `columnWidths`.

Each row has exactly one `cells` array. Cells are logical origins with `html` or authoritative
structured `inlines`, `backgroundColor`, `textColor`, horizontal/vertical alignment, `rowSpan`,
`columnSpan`, optional `width`, and per-side `borders`. Literal safe colors are accepted; `url()`,
`var()`, declaration separators and markup/CSS injection are rejected.

```json
{
  "op": "createTable",
  "clientRef": "risk-table",
  "pageId": "11111111-1111-1111-1111-111111111111",
  "columnCount": 2,
  "hasHeaderRow": true,
  "columnAlignments": ["left", "right"],
  "columnWidths": [220, 120],
  "rows": [
    {
      "cells": [
        {
          "html": "<strong>Risk</strong>",
          "backgroundColor": "#fef3c7",
          "textColor": "#111827",
          "rowSpan": 1,
          "columnSpan": 1
        },
        {
          "inlines": [
            {
              "text": "Impact",
              "bold": true,
              "textColor": "#111827"
            }
          ],
          "rowSpan": 1,
          "columnSpan": 1
        }
      ]
    }
  ]
}
```

#### `patchBlockContent`

Required: `op`, `blockId`, `patch`. Optional: `clientRef`. The object patch is merged into canonical
content while identity, page, parent and order remain unchanged.

#### `moveBlock`

Required: `op`, `blockId`, `targetPageId`, `targetOrder`. Optional: `clientRef`,
`targetParentBlockId`. The complete subtree moves as one operation, including cross-page moves.

#### `reorderBlocks`

Required: `op`, `pageId`, `orderedBlockIds`. Optional: `clientRef`, `parentBlockId`. The id list must
be the complete desired sibling set.

#### `convertBlockType`

Required: `op`, `blockId`, `newType`, `content`. Optional: `clientRef`. Content replaces the prior
canonical payload and must match the new type.

#### `deleteBlock`

Required: `op`, `blockId`. Optional: `clientRef`. Deletes the complete subtree.

#### `replaceBlocks`

Required: `op`, `pageId`, `blocks`. Optional: `clientRef`, `parentBlockId`. Replaces the complete
child set with a strict recursive block forest.

## Idempotent retry

- Generate one stable `idempotencyKey` per logical request.
- After an ambiguous transport failure, retry the identical request with the same key.
- Reusing a key with different canonical operations is rejected.
- On optimistic-concurrency conflict, re-read the page, rebuild against the new token, and submit
  with a new key.
- `INotionAggregateProvider` hosts use a process-local 24-hour receipt fallback.
- Implement `INotionIdempotentAggregateProvider` when retries must survive a process restart. Its
  callback, aggregate writes, and opaque response receipt must share one transaction; Tempo then
  replays before loading stale targets and never invokes the callback for collisions.

## Limits and safety

The machine-readable table schema is authoritative for exact limits. Current hard limits are 1,000
rows, 100 columns, 10,000 physical slots, 1,000 structured inlines per cell, 16,384 characters per
inline, 65,536 characters per cell HTML fragment, and 1,048,576 combined content characters per
table. New writes accept only the documented inline HTML profile and literal safe CSS colors;
historical data is sanitized again at conversion and render boundaries.
