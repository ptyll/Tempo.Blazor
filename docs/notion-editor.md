# NotionEditor package — `TmNotionEditor` and friends

`Tempo.Blazor.NotionEditor` ships the Notion-style page editor: `TmNotionEditor` (the shell),
the page, the sidebar, the block catalogue and the database views. Full parameter lists stay in
the XML docs and in [COMPONENTS.md](../COMPONENTS.md); this page is the contract a host keys off.

```html
<link rel="stylesheet" href="_content/Tempo.Blazor/css/tempo-blazor.css" />
<link rel="stylesheet" href="_content/Tempo.Blazor.NotionEditor/css/tempo-blazor-notion-editor.css" />
<script src="_content/Tempo.Blazor.NotionEditor/js/notion-editor.js"></script>
```

`notion-editor.js` is a classic script. Column resize and the timeline drag are ES modules
imported by the views themselves — no extra `<script>` tag, and no package-local copy of the
pointer helper (see [JavaScript and CSP](#javascript-and-csp)).

## Responsive scope

The editor resolves its presentation from its **own container** through the core `TmEditorShell`
(`LayoutMode`, default `Auto`). Bands: desktop ≥ 1024, tablet 640–1023, mobile < 640. The first
frame, before a measurement, is Desktop. A forced `LayoutMode` pins the band for a host or a test
and is cascaded into the page, the menus and the database blocks. The resolved band is mirrored as
`data-layout` (`desktop` / `tablet` / `mobile`) on the shell root (`data-testid` of the editor).

| Layout | Container | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1024 | Header top bar, docked page tree, the page, and the host inspector when `InspectorContent` is set. Shell panels dock next to the page. |
| **Tablet** | 640–1023 | The page tree is an overlay drawer and the inspector a side sheet (at most one open). Secondary top-bar tools fold into the toolbar "More" menu. Shell panels are side sheets. |
| **Mobile** | < 640 | Compact top bar, the page tree as a sheet, and the F5 `TmMobileActionBar` (Add block, Comments, My tasks, the rest in More). Shell panels are full-height sheets. A database that fills the page (`IsPage`) shows the mobile action bar; an inline database keeps the compact toolbar. |

**`Chrome`** (`NotionEditorChrome`, default `Full`):

- `Full` — sidebar, top bar, notification bell and the mobile action bar.
- `Minimal` — a public, read-only document. No shortcuts, bell, mode toggles or badges; the host
  page supplies the only header.
- `None` — the page only. No top bar, no sidebar chrome, no mobile action bar.

`Chrome.None`, and `Chrome.Full` with both `ShowSidebar="false"` and no `InspectorContent`, render
no mobile action bar: the bar is editor chrome, not page content.

**Mobile policy.** The phone keeps reading and light edits: add a block, comment, open My tasks,
search records already loaded, change a cell. Column drag, column resize and timeline bar drag are
not offered on a coarse pointer or a mobile layout — each has a non-drag path (below). Pin
`LayoutMode="TmLayoutMode.Tablet"` when a host needs the full editing chrome inside a narrow box.

## Shell bands

`TmEditorShell` regions, and what fills them:

| Band | Slot | Notes |
|---|---|---|
| Header | top bar | One `TmToolbar` (`Overflow=Menu`, `Labels=Auto`). Absent for `Chrome` `Minimal` / `None`, so the shell draws no empty header band. |
| Left | page tree | Drawer below 1024. Pass `ShowSidebar="false"` and the slot is null — no empty rail, no toggle. |
| Canvas | the page | Scroll root is `[data-notion-scroll-root]`. |
| Right | `InspectorContent` | Host slot through `TmSidePanel`. Null (default) renders no right region — the editor does not invent inspector content. |
| Mobile | action bar | See the frozen test ids below. |

**`ShellPanelPresentation`** (`NotionShellPanelPresentation`, default `Overlay`) decides how the
Tasks / Blog / Analytics / Audit panels present. At most one is open.

- `Overlay` — the core `TmSidePanel`: docked next to the page on desktop, a side sheet on tablet,
  a full-height sheet on mobile. The page stays mounted.
- `Main` — the panel replaces the page in the main region (the pre-phase-3 hosting).

## Overlays and sheets

Dialogs, menus and comment bubbles ride the core overlay stack. The package does not ship its own
backdrop, its own z-index band or a second drag implementation.

| Surface | Core primitive | Notes |
|---|---|---|
| Share, restrictions, history, media, AI result, Tempo edit modals | `TmModal` | Escape closes. A dirty Tempo edit modal asks before Discard or Escape. History on mobile is list, then preview, with Back. |
| Block menu, slash menu, inline toolbar | `TmOverlayPanel` | Positioned by `overlay.js`. |
| Page tree (tablet), inspector, shell panels | `TmSidePanel` / shell drawer | At most one shell panel open. |
| Mobile page tree, shell panels, database sheets | sheet presentation of the same primitives | Full height; the page tree starts closed. |

## Comment presentation

`CommentPresentation` (`NotionCommentPresentation`, default **`Auto`**) is the block and text
comment panels:

- `Auto` — anchored popover on a fine desktop pointer; the core `TmModal` (a sheet on a mobile
  layout) on a coarse pointer (`TmLayoutContext.IsCoarsePointer`) and on touch and narrow containers.
- `Popover` — the bubble at every layout (the pre-redesign behavior).
- `Dialog` — the modal at every layout.

`PageCommentsPresentation` (`NotionPageCommentsPresentation`, default `Inline`) is the page-level
panel: `Inline` keeps the collapsible panel at the bottom of the page; `Dialog` renders no inline
panel — the header badge and the Comments action open a `TmModal` with the embedded panel body.

The three panels share one entry and composer surface
(`tempo-blazor-notion-editor.css`). The panel prefix (`tm-nbcp` / `tm-ntcp` / `tm-npcp`) only
chooses which panel a rule paints; the block popover keeps its slightly larger metrics through
`--tm-notion-comment-*` on `.tm-nbcp`.

## Toolbar contract (frozen)

Hosts key off `data-tm-group` and `data-testid`, never a positional selector. Where a group renders
(bar / "More" / mobile sheet) is a layout detail.

**Top bar** (`data-tm-group`): `navigation | collaboration | tools | modes | primary`.

**Database toolbar** (`data-tm-group`): `views | tools | primary`. Each tool keeps `.tm-db__tool-btn`,
`data-tool` (`filter | sort | group | sub-items | properties | open-as-page | import-export | new |
templates`) and its `data-testid`. On a compact layout the secondary tools render only inside
"More". On mobile the view tabs are replaced by `[data-testid=notion-db-view-switch]`; past four
views the rest sit behind `[data-testid=notion-db-more-views]`.

**Frozen `data-testid`s** (editor root, `TestId` prefix applied):

| id | Where |
|---|---|
| `notion-audit-open`, `notion-blog-open`, `notion-analytics-open`, `notion-shortcuts-open` | top bar |
| `notion-share-open`, `notion-reading-toggle`, `notion-presentation-toggle`, `notion-tasks` | top bar |
| `notion-add`, `notion-comments`, `notion-tasks`, `notion-exit-reading` | mobile action bar (pinned) |
| `notion-watch-toggle`, `notion-page-settings` | mobile More |
| `notion-db-view-switch`, `notion-db-view-tab`, `notion-db-more-views`, `notion-db-search` | database toolbar |
| `notion-db-filter-open`, `notion-db-sort-open`, `notion-db-group-open`, `notion-db-properties-open`, `notion-db-new` | database tools |

The database block adds `notion-db-search-status` / `notion-db-search-loaded-only` on the search
live region. Dialogs keep their own frozen ids (`notion-share-dialog`, `notion-restrictions-dialog`,
`notion-history-restore-confirm`, `notion-tempo-modal-save`, …).

## Non-drag alternatives (WCAG 2.5.7)

Every drag has a tap or keyboard path:

- **Column resize** — Alt+Arrow on the handle (16 px step, commits on a short debounce). A coarse
  pointer hides the handle; width then changes from the field editor.
- **Column order** — header drag on a fine pointer; the Properties panel up/down buttons
  (`ReorderFieldsAsync`) everywhere else, including every coarse pointer.
- **Timeline bar** — `pointer-drag.js` (500 ms long-press on touch). Alt+Arrow moves one unit,
  Alt+Shift+Arrow resizes. On a mobile layout a tap opens the date sheet instead of dragging.
- **Page tree reorder** — the row menu and the keyboard path; the drag grip is the bonus.

## Tokens and palettes

Package tokens are `--tm-notion-*`, declared on `:root` **and** `.tm-notion-editor` so a nested
dark editor recomputes them. Palette roles, not the removed aliases:

| Role | Token shape | Use |
|---|---|---|
| Option chip | `--tm-notion-option-{color}-wash` / `-ink` / `-swatch` | Select / Status chips |
| Text colour | `--tm-notion-option-text-{color}-ink` | `[data-color]` |
| Highlight | `--tm-notion-option-text-{color}-wash` | `[data-bg]` |

`{color}` is `gray | red | orange | yellow | green | blue | purple | pink | brown`. Comment entry
metrics are `--tm-notion-comment-*` (avatar, gaps, type sizes, paddings). Do not reintroduce
`--tm-notion-option-*-bg`, `--tm-notion-color-*`, `--tm-notion-text-{color}`, `--tm-notion-bg-{color}`
or `--tm-notion-border-width` — those aliases were removed in 2.10.0.

## Database search

`ShowSearch` (default `false`) filters the records **already loaded**. It does not extend the
provider contract. When more pages exist the field warns
(`[data-testid=notion-db-search-loaded-only]`). `TmNotionEditor` passes `ShowSearch="true"` for a
database block it hosts; a host that embeds `TmNotionDatabaseBlock` directly and wants the field
passes it itself. `IsPage` (default `false`) is what shows the mobile action bar.

## JavaScript and CSP

| Asset | Kind | Role |
|---|---|---|
| `notion-editor.js` | classic script, `<script>` tag | Editor interop. Menus and the inline toolbar are positioned by `overlay.js`; this file does not clamp them. |
| `notion-db-column-resize.js` | ES module, imported by the table view | Live column width on the header cell; Blazor learns the width once, on pointerup. |
| `notion-db-timeline.js` | ES module, imported by the timeline view | Bar move and resize. No Blazor render on pointermove. |
| `pointer-drag.js` | core ES module, `_content/Tempo.Blazor/js/` | The only drag implementation. Both modules import it by rewriting their own URL (`/Tempo.Blazor.NotionEditor/` → `/Tempo.Blazor/`). |

A host must serve `_content/Tempo.Blazor/js/` next to the package assets. A per-file CSP hash must
list `pointer-drag.js` beside `notion-editor.js`, `notion-db-column-resize.js` and
`notion-db-timeline.js`. Without the helper the table keeps its mouse path, logs one error and
column resize is off; the timeline drag does not start.

Localization keys live in the core tables (`TmResources.json`, `.cs.json`, `.sk.json`, `.fr.json`),
not in a package resource. A host override replaces them through `ITmLocalizer`.

## Related

- Migration notes for 2.7: [notion-2.7-migration.md](notion-2.7-migration.md)
- Atomic editor: [notion-atomic-editor.md](notion-atomic-editor.md)
- MCP authoring: [notion-mcp-authoring.md](notion-mcp-authoring.md)
- Breaking changes of this redesign: [CHANGELOG.md](../CHANGELOG.md) 2.10.0, Breaking / Migration index
