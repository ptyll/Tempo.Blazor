# Modeling package — `TmModelingEditor` and friends

`Tempo.Blazor.Modeling` turns a semantic model (BPMN, UML, ArchiMate, ERD profiles) into a
diagram document and previews/edits it. The package ships `TmModelingEditor` (the workspace:
model tree, diagram preview, inspector, issues dock, source panel), the supporting panels
(`TmModelingDiagramPreview`, `TmModelingModelTree`, `TmModelingInspector`,
`TmModelingSourcePanel`, `TmModelingIssuePanel`, `TmModelingViewSelector`), the notation
profiles (`BuiltInModelingProfiles`), the `ModelingDiagramGenerator` and one CSP-safe ES module
(`_content/Tempo.Blazor.Modeling/js/modeling-editor.js`).

The package embeds `TmDiagramEditor` (the preview and the "Open in editor" dialog), so a host
also needs the DiagramEditor static assets — no Modeling-specific `<script>` tag or stylesheet
exists; all Modeling styling is scoped Razor CSS:

```html
<link rel="stylesheet" href="_content/Tempo.Blazor/css/tempo-blazor.css" />
<link rel="stylesheet" href="_content/Tempo.Blazor.DiagramEditor/css/tempo-blazor-diagram-editor.css" />
<!-- + the DiagramEditor JS bundle (dagre, d3, diagram-editor.js) when the dialog/preview is used -->
```

`modeling-editor.js` is imported through `IJSObjectReference` (`./_content/Tempo.Blazor.Modeling/js/modeling-editor.js`)
— the host needs **no** `<script>` tag and the package **never** uses `eval` interop, so hosts
may ship a CSP without `unsafe-eval` (guarded by `ModelingNoEvalInteropTests`; the demo Server
host serves `/modeling-editor?csp=1` with such a header). Every interop call is progressive
enhancement: a failed import is never cached and never breaks the component.

Registration:

```csharp
builder.Services.AddTempoBlazorModeling();   // profiles, generator, demo providers
```

The demo page is `/modeling-editor` (WASM + Server + InteractiveAuto demos), with deep links
`?provider=`, `?notation=`, `?viewpoint=` and scenarios `?scenario=drag-readonly|error|preview|
performance-large|bpmn2-*|uml25-*|invalid|scenario-invalid-relationship`.

## Component map

| Component | Purpose |
|-----------|---------|
| `TmModelingEditor` | The workspace shell: header, model tree, diagram preview + issues dock, inspector, status bar. Owns loading, generation, selection and the open-editor dialog. |
| `TmModelingDiagramPreview` | The generated diagram: a read-only `TmDiagramEditor` (`ToolbarPreset.Viewer`, `ReadOnly=true`, `ShowTitle=false`) in a labelled section with the Primary "Open in editor" + Secondary "Generate diagram" actions and a drop target. |
| `TmModelingModelTree` | The APG `role="tree"` of model elements — flat `SemanticType` groups or the nested `ParentId` hierarchy (`Grouping`, Fáze 12) — with filter, collapse state and the drag/insert bridge. |
| `TmModelingInspector` | Read-only inspector for the selected element/relationship (MO2 — the inspector never edits). |
| `TmModelingRelatedElementsPanel` | The Relations tab body (Fáze 13): the selected element's outgoing/incoming relationships (type + counterpart with the type icon) and the selected relationship's source/target as navigable rows; activations reuse the editor's selection paths. |
| `TmModelingSourcePanel` | The "Source" inspector tab: provider/system/version/trust metadata, freshness and the "Load model" reload. |
| `TmModelingIssuePanel` | The issues list (severity, message, recommended fix, source navigation) with a collapsible dock header. |
| `TmModelingViewSelector` | The labeled Notation / Viewpoint selects of the header. |

## Responsive scope (G2)

Like the DiagramEditor and the Wireframe editor, `TmModelingEditor` resolves its presentation
from its **own container** (`TmLayoutObserver`, `data-layout` on the root — never the viewport,
`container: tm-modeling-editor / inline-size`), with the DiagramEditor pair 768 / 1200:

| Layout | Container width | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1200 | The core `TmEditorShell`: Header (title + state badge, provider field/chip, `TmModelingViewSelector`, Inspector toggle), Left = model tree, Canvas = the preview with the **collapsible issues dock** under it (an **issues-empty** body drops the dock row to `auto` when the model has zero issues), Right = inspector with **Properties \| Relations \| Source** tabs (Fáze 13; only the active tab body renders), StatusBar = the status strip. The preview's embedded editor keeps the **Zoom** toolbar group only — the search + "⋯" header-bar row is gone with its groups (the DE renders no empty header bar), and the status bar keeps the zoom controls. |
| **Tablet** | 768–1199 | The shell's `TabletPanelPresentation.Overlay`: the tree is an overlay **drawer** (`modeling-tree-drawer`), the inspector a non-modal **side sheet** (`modeling-inspector-sheet`) — at most ONE open at a time, both **start closed**. The `modeling-panel-tabs` row under the header is the visibility switch (Tree/Inspector/Issues are `aria-expanded` disclosure toggles; Preview closes all) — **Issues is the fourth panel**, a non-modal side sheet (`modeling-issues-sheet`) under the same one-open rule, never a dock row under the canvas; its tab carries the count badge. The sheet is positioned by the editor-owned `tm-modeling-editor__issues-sheet-host` wrapper (absolute, the canvas column's end edge, `min(20rem, 100%)`, `--tm-z-inline-sheet`; the `TmFocusScope` root is a child component and carries no scope attribute of its own) and its issue list scrolls inside. 44px coarse-pointer targets; the preview canvas row keeps a `minmax(20rem, 1fr)` floor. |
| **Mobile** | < 768 | **Viewing + light edits (G2).** A `TmTabs` tablist **Preview \| Tree \| Inspector \| Issues** (the issue-count badge hides at zero) where exactly ONE panel body mounts at a time (M26 budget); a compact header (title, state badge, one "⋯" `TmDropdown` sheet: Change view / Generate diagram / Open in editor); a **status card** (`modeling-status-card`, four count tiles with short visible labels + aria-hidden type icons + last-loaded); and the F5 `TmMobileActionBar` (Reload \| Change view \| Issues, with Generate diagram + Open in editor behind More). **No drag and no tree→preview insert**: reuse is a desktop/tablet editing intent — on mobile the tree renders neither the "+" button nor the drag grip, and edits go through "Open in editor" (G2 viewing). Preview zoom is the DE's floating pill + pinch (Fáze 9 — `ShowZoomOverlay`, fit-to-width first load); the full editor lives in "Open in editor". The open-editor dialog is a **fullscreen** `TmModal` presentation handing the embedded editor a full-height body (the DE's own mobile mode drives inside). |

`LayoutMode` (`Auto` default) pins a presentation for tests and kiosk hosts; `InitialMode`
(`Desktop`) is what an Auto editor renders before the first measurement (SSR/prerender);
`LayoutBreakpoints` overrides the 768/1200 pair per instance. The nested `TmDiagramEditor`s
(preview + dialog) are NEVER pinned to the editor's resolved mode — each measures its own
container (the dialog body uses the DE pair `new(768, 1100)` so a ~90vw body at a 1024/1180
window still resolves the DE tablet layout).

## Parameters

### `TmModelingEditor`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ProviderKey` | `string?` | `null` | Semantic model provider key; null/empty renders the empty state. |
| `ShowProviderSelector` | `bool` | `false` | Render a labeled Provider select in the header (MO3). `false` keeps the provider as a labeled value chip (host-owned). |
| `ProviderKeyChanged` | `EventCallback<string?>` | — | `@bind-ProviderKey` partner; raised when the user picks a provider (the editor reloads the model, closing the open-editor dialog via the keep-surface path). An in-editor pick is kept as an override until the host parameter itself changes; an unregistered key stays listed so a deep link never silently jumps. |
| `OnProviderChanged` | `EventCallback<string?>` | — | Raised together with `ProviderKeyChanged`. |
| `ProviderDisplayName` | `Func<string, string>?` | `null` | Display-name resolver for the select options and the chip; null (or an empty result) shows the raw key. |
| `NotationKey` / `ViewpointKey` | `string?` | `null` | Passed to the provider/generator; changes raise `OnNotationChanged` / `OnViewpointChanged`. |
| `OnDiagramGenerated` | `EventCallback<DiagramDocument>` | — | After a model is loaded and converted. |
| `OnOpenInEditor` | `EventCallback<DiagramDocument>` | — | When the user opens the generated document in the full editor. |
| `AllowNodeDrop` | `bool` | `true` | Tree→preview reuse (drop AND the non-drag insert); `false` removes the drop zone and the "+" affordance. |
| `TreeGrouping` | `ModelingTreeGrouping` | `Auto` | Fáze 12 pass-through to the model tree: `Auto` renders the `ParentId` hierarchy when at least one element resolves to a valid parent and keeps the flat SemanticType groups otherwise. |
| `OnNodeDropped` | `EventCallback<ModelingNodeDroppedEventArgs>` | — | Raised by a real drop AND by the insert alternative (same contract). |
| `LayoutMode` / `InitialMode` / `LayoutBreakpoints` | see above | `Auto` / `Desktop` / `new(768, 1200)` | Presentation resolution from the editor's own container. |
| `Height` | `string?` | `null` | The editor's block size (CSS length only). `null` keeps the stylesheet contract `min(768px, 100vh - 32px)`; a set value renders exactly that height (the token floor is dropped) and the shell fills the box. A value that is not a simple CSS length or a single `calc()` expression — anything carrying `;`/`{`/`}` — is REJECTED and falls back to the stylesheet default (final review: the inline style is never an injection vector). |
| `PersistKey` | `string?` | `null` | Overrides the localStorage key the docked shell persists panel widths under. Default derives `{TestIdPrefix}-modeling-editor` (or `modeling-editor`) so two editors on one page share widths only when they share the prefix. |
| `ModelLoaderOverride` | `Func<ModelingModelRequest, CancellationToken, Task<ModelingModelDto>>?` | `null` | Host/test loader without registering a provider. |
| `Class` | `string?` | `null` | Extra root class. |

### `TmModelingDiagramPreview`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Document` / `DocumentChanged` | `DiagramDocument?` / `EventCallback<DiagramDocument>` | `null` | The generated document; live two-way so dialog edits propagate to the preview (G8 — no Apply/Cancel footer). |
| `ShowHeader` | `bool` | `true` | The section header row (title summary + actions). The editor's mobile band passes `false` (the status card already shows the counts). |
| `ShowSummary` | `bool` | `true` | The nodes/edges subtitle line in the header. The hosting editor passes `false` (final UX review M1 — the status bar already carries the counts and the row cost the canvas height). |
| `ShowEditActions` | `bool` | `true` | Render the Primary "Open in editor" / Secondary "Generate diagram" buttons and the drop hint (only while drops are accepted). `false` when a hosting action bar owns those entries (mobile). |
| `ShowEmbeddedEditorChrome` | `bool` | `true` | Pass `false` to hide the embedded editor's toolbar (`ShowToolbar=false` through the public API — never CSS). The mobile band does this: the header bar there would hold only the "⋯" trigger while the floating zoom pill duplicates the pinch. On the docked bands the preview keeps the **Zoom** toolbar group only — the search + "⋯" header-bar row is gone with its groups (the DE renders no empty header bar). |
| `ShowZoomOverlay` | `bool` | `false` | Opt in to the embedded editor's floating mobile zoom pill (Fáze 9): passes the DE's additive `ShowMobileZoomOverlay="true"` — the pill shows on the DE's own mobile band even with the toolbar hidden — and arms `FitOnDocumentLoad="DiagramFitMode.Width"` so a first load fits the diagram to the width. The hosting editor turns this on for its mobile band; the pinch keeps working either way. The docked bands keep the DE's own status-bar zoom. |
| `OpenInEditorTriggerId` | `string?` | `null` | Stable DOM id stamped on the open-editor trigger so the dialog's `TmFocusScope` can name the restore target. |
| `AllowNodeDrop` / `OnDrop` / `ActiveDraggedElement` / `OnNodeDropped` | — | — | The drop target contract. |
| `OnGenerateRequested` / `OnOpenInEditor` / `OnRelationshipSelected` | — | — | Host callbacks. |

Public methods: `InsertAtViewportCenterAsync(ModelingElementDto element)` — the non-drag insert;
creates the node at the center of the diagram viewport (`TmDiagramEditor.GetViewport()`, page
center when unknown) through the same clamp/collision path as a drop and raises the SAME
`OnNodeDropped` contract. `RefreshViewportAsync()` — final UX review M5: after the hosting
editor re-grids the canvas column (an issues-dock collapse/expand), the embedded viewer
re-fits its content to the new box through the DE's additive `FitToViewAsync()` (a real
zoom-to-fit, the same path the document load uses), so the diagram fills the box and the zoom
report follows. The embedded editor also runs `LayoutBreakpoints = new(600, 1200)`: the docked
canvas column sits near the DE's own 768px mobile threshold, so the preview drops the mobile
threshold to 600px — a docked column always presents the stable tablet chrome (page tabs +
canvas + status-bar zoom, never a flip during a dock toggle) while a real G2 column (≤ 600px)
keeps the DE's compact mobile presentation, whose chrome the mobile band drops through
`ShowEmbeddedEditorChrome`.

Embedded-viewer knobs (Fáze 9, all through the DE's public API — never CSS on its internals):
the preview passes `ShowPageTabs="@(Document?.Pages.Count > 1)"`, so a single-page generated
document renders NO page-tabs row (a multi-page one keeps the tabs switchable), and the mobile
band's `ShowZoomOverlay` adds the DE's floating zoom pill plus the fit-to-width first load.

### `TmModelingModelTree`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Elements` | `IReadOnlyList<ModelingElementDto>` | — | The tree source. |
| `SelectedElement` / `SelectedElementChanged` / `OnElementSelected` | — | — | Selection (an external arrival inside a collapsed group auto-reveals and scrolls to it; in the hierarchy it reveals the WHOLE ancestor chain). |
| `FilterText` / `FilterTextChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way filter. The parameter SEEDS the internal filter and is never assigned inside the component; user edits raise the callback (and clear the transient filter-collapse set). |
| `CollapsedGroups` / `CollapsedGroupsChanged` | `IReadOnlyCollection<string>?` / `EventCallback<...>` | `null` | Two-way collapsed `SemanticType` group keys (owned and bound by the editor). While a filter is active, toggling works on a transient set cleared when the filter text changes. |
| `Grouping` | `ModelingTreeGrouping` | `Auto` | Fáze 12. `SemanticType` keeps the flat groups; `Hierarchy` always renders the `ParentId` nesting; `Auto` uses the hierarchy when at least one element resolves to a valid parent and the flat groups otherwise. |
| `CollapsedElementIds` / `CollapsedElementIdsChanged` | `IReadOnlyCollection<string>?` / `EventCallback<...>` | `null` | Fáze 12: two-way collapsed hierarchy nodes — element ids, a SEPARATE key space from `CollapsedGroups` (no migration). The editor owns the state across tree remounts; while a filter is active, toggles work on a transient set. |
| `AllowNodeDrop` / `OnInsertIntoPreview` | `bool` / `EventCallback<ModelingElementDto>` | `true` / — | The drag handle and the non-drag "+" alternative. The "+" button renders only when `AllowNodeDrop && OnInsertIntoPreview.HasDelegate`. |
| `OnElementDragStarted` / `OnElementDragEnded` | — | — | Drag bridge events. |
| `ShowTitle` | `bool` | `true` | The heading block. The tablet drawer passes `false` (its sheet already says "Model tree"; the search field stays and `role="tree"` is named by `aria-label`). |
| `Class` | `string?` | `null` | Extra class. |

The tree is a real APG tree: each type group is a `role="treeitem"` wrapper (`aria-level="1"`,
`aria-expanded`, `aria-setsize`/`aria-posinset`) containing a `role="group"` list of
`aria-level="2"` element treeitems. Keyboard navigation walks ONE flat visible list
(Up/Down/Home/End; Right expands a collapsed header, Left collapses; Enter/Space toggle the
emulated group treeitem — the node buttons stay native and fence their keys with
`stopPropagation`). The node treeitem advertises the insert paths with
`aria-keyshortcuts="Insert Control+Enter"` and names the "+" button per row
(`TmModelingModelTree_InsertIntoPreviewNamed`). Root counters: `data-visible-count` (matching
elements across ALL groups) and `data-rendered-count` (nodes actually in the DOM).

**Hierarchy indent step (phase-13 carry-forward).** The per-level step is a full
`--tm-space-6` (the original `--tm-space-4` read as ~9px on screen once the chevron column
ate into it); the token-driven calc lives only in `TmModelingModelTree.razor.css` and the
chevron column is untouched, so the disclosure glyphs stay aligned across levels. The
`hierarchy-*` baselines pin the geometry.

**Hierarchy view (Fáze 12).** With `Grouping=Auto` and at least one resolvable
`ModelingElementDto.ParentId`, the tree renders the model's containment instead of the flat
type groups: every element is a treeitem at its own `aria-level` with
`aria-setsize`/`aria-posinset` computed among the VISIBLE siblings, parents are disclosures
(chevron + the direct child count through the localized plural
`TmModelingModelTree_ChildCount_*`, `aria-expanded` + `aria-controls`), and the same flat
visible list drives keyboard navigation (Right expands a collapsed parent or descends, Left
collapses or moves to the nearest visible ancestor; Insert / Ctrl+Enter keep the insert paths).
Indentation rides the per-node `--tm-modeling-tree-level` custom property through
`--tm-space-*` — no hard-coded pixels. Robustness contract: an element whose `ParentId` does
not resolve to another element renders at the root; a parent cycle (and anything hanging off
one) is broken deterministically to the root — no recursion, no stack overflow, whatever the
input order; the hierarchy is built once per `Elements` reference (a dictionary of children by
parent, memoized) so a 200-element / 6-level model re-filters in well under 200 ms (M26).
The filter keeps every ancestor of a match visible and expanded while it is active, and
`data-visible-count` counts MATCHES (never dropping to zero because the only matching
subtree is collapsed) while `data-rendered-count` counts the nodes in the DOM. The diagram
generator never maps `ParentId` onto `ModelingViewNodeDto.ParentNodeId` — diagram nesting
stays owned by the view.

### `TmModelingIssuePanel`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Issues` | `IReadOnlyList<ModelingIssueDto>` | — | The issues to present. The panel renders a filtered/sorted **projection**; the collection is never mutated. Row `data-testid`s carry the ORIGINAL input index, so filter/sort state never renumbers rows. |
| `OnIssueSelected` | `EventCallback<ModelingIssueDto>` | — | Navigable issue activated (source element/relationship). |
| `Collapsed` / `CollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | Two-way dock collapse. The body always renders (the `aria-controls` id is never missing); a collapsed dock hides the body — including the filter bar — with `hidden`. |
| `ShowToggle` | `bool` | `true` | The header collapse toggle (`modeling-issues-toggle`, `aria-expanded`/`aria-controls`). The editor's mobile Issues tab passes `false` and shows a plain heading. |
| `ShowFilterBar` | `bool` | `false` | The Fáze 11 filter bar above the list: severity chips with counts, text search, sort select. Renders only when `Issues` is not empty; the hosting editor passes `true` on every band. |
| `SeverityFilter` / `SeverityFilterChanged` | `IReadOnlyCollection<ModelingIssueSeverity>?` / `EventCallback<…>` | `null` | Two-way active severity set; `null` (or empty) = all. Seeds on a new collection, never assigned internally (the `CollapsedGroups` pattern) — the editor owns the state. |
| `FilterText` / `FilterTextChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way text filter (seed/echo like `TmModelingModelTree.FilterText`, incl. echo recording so a host reset re-seeds). Matches message, category and source element/relationship NAME — case- and accent-insensitively. |
| `SortMode` / `SortModeChanged` | `ModelingIssueSortMode` / `EventCallback<ModelingIssueSortMode>` | `Input` | Two-way sort: `Input` (never re-sorts), `Severity` (Error > Warning > Info, stable within a group), `Source` (resolved source display name, unmatched ids fall back to the id, sourceless last). |
| `Elements` | `IReadOnlyList<ModelingElementDto>?` | `null` | Element list for source-name search and the `Source` sort. |
| `Relationships` | `IReadOnlyList<ModelingRelationshipDto>?` | `null` | Relationship list (as `Elements`). |
| `Class` | `string?` | `null` | Extra class. |

Root attributes: `data-issue-count` and `data-total-issue-count` (both the TOTAL) plus
`data-visible-issue-count` (the filtered count — equals the total unfiltered). While a filter
hides rows, the header badge shows the visible-of-total pair (`2 / 4` visible, localized
`TmModelingIssuePanel_CountFiltered` "Issues: {0} of {1}" on the sr-only span). When the
filters hide EVERY row the body renders the localized no-match state
(`modeling-issue-no-match`) with a "Clear filters" button (`modeling-issue-clear-filters`)
that resets both filters through the two-way bindings. The rendered list is memoized (key =
the `Issues` reference + filter + sort + culture + `Elements`/`Relationships` references;
M26: 500 issues stay inside the budget and an unrelated parent re-render renders nothing —
the panel owns a render gate like the model tree). Severity chips are NATIVE `aria-pressed`
toggle buttons (`modeling-issue-severity-chip-{error|warning|info}` — one activation per
press, keyboard reachable, 44px targets on a coarse pointer), the search field is
`modeling-issue-filter-input` and the sort select `modeling-issue-sort` (visible label).

Issues without a source element/relationship render as a plain non-interactive `<div>` (same
`data-testid`, `data-severity`, `data-source-*`, same text) — never a button.

`TmModelingEditor` owns the filter state (`TmModelingEditor.IssueFilter.cs`:
`_issueSeverityFilter` / `_issueFilterText` / `_issueSortMode`) and hands the same fields to
the desktop dock, the tablet sheet and the mobile Issues tab, so a filter survives a dock
collapse, a sheet toggle and a tab switch; the mobile tab badge still shows the TOTAL count.

### `TmModelingRelatedElementsPanel` (the Relations tab, Fáze 13)

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Element` | `ModelingElementDto?` | `null` | The selected element; renders its outgoing/incoming groups. Takes precedence when a relationship is supplied too. |
| `Relationship` | `ModelingRelationshipDto?` | `null` | The selected relationship; renders its source and target as navigable rows. |
| `Elements` | `IReadOnlyList<ModelingElementDto>` | `[]` | Counterpart resolution (display names + semantic-type icons via `ModelingSemanticTypeIcons`). |
| `Relationships` | `IReadOnlyList<ModelingRelationshipDto>` | `[]` | The model's relationships — the memoized by-element index source. |
| `OnElementSelected` / `OnRelationshipSelected` | `EventCallback<…>` | — | Row activations (the editor routes them through the same `SelectElementAsync` / `SelectRelationshipAsync` paths an issue or edge pick uses, and returns the docked inspector to the Properties tab — with DOM focus on that tab button through its `data-modeling-focus="inspector-tab-properties"` hook; on mobile the section updates in place and the focus lands on the panel root's `related-panel` hook). |
| `Class` | `string?` | `null` | Extra root class. |

The root `<section data-testid="modeling-related-panel">` reports `data-kind`
(`element` / `relationship` / `empty`), carries `tabindex="-1"` +
`data-modeling-focus="related-panel"` (the mobile element-hop focus hook — not tab-reachable)
and names itself through one sr-only `<h3>` (`TmModelingEditor_Relations`), so the group
headings hang off a real heading — the groups are plain `<div>`s, no extra landmarks. An
element view groups rows under localized Outgoing/Incoming headings with a count badge (sr-only
text through the `TmModelingRelated_RelationshipCount_*` plural family); every row is
`data-testid="modeling-related-{direction}-{relationshipId}"` with `direction` ∈
`outgoing | incoming | source | target`. Each element-view row holds TWO native buttons whose
ids carry the row's direction too — the counterpart
(`modeling-related-{direction}-counterpart-{id}`, named `TmModelingRelated_SelectElement` "{0}")
and the relationship type (`modeling-related-{direction}-relationship-{id}`, named
`TmModelingRelated_SelectRelationshipTo` "Select relationship {0} to {1}" outgoing /
`TmModelingRelated_SelectRelationshipFrom` "Select relationship {0} from {1}" incoming, {1} =
the counterpart display name) — never nested; a relationship-view row is one native button. A
counterpart that does not resolve renders the localized `TmModelingEditor_Unknown`
fallback as plain content (never a dead button), a self-reference renders safely in both
directions (and the tab badge counts both rows — one index pass, each direction separately),
the empty state is `TmModelingRelated_Empty`, rows reach 44px on a coarse pointer, rows carry
`@key` and BOTH indexes — the by-element relationship index AND the by-id element lookup — are
built ONCE per `Relationships` / `Elements` reference (guarded by
`Relationships_index_memoized` / `Element_index_memoized`).

### `TmModelingViewSelector`, `TmModelingSourcePanel`, `TmModelingInspector`

`TmModelingViewSelector` — `NotationKey`/`ViewpointKey` + `OnNotationChanged`/
`OnViewpointChanged`; labels sit ABOVE the fields (`--tm-input-height-md`), the disabled
viewpoint select's placeholder is localized. `TmModelingSourcePanel` — `Metadata`, `IsLoading`,
`OnLoadRequested` (the "Load model" button drives the editor's reload); a wide panel container
(≥ 640px) lays the metadata cells into columns. `TmModelingInspector` — read-only
`Element`/`Relationship`/`Elements` presentation (MO2).

## Non-drag alternative (WCAG 2.5.7)

Every drag affordance has a tap/keyboard path:

- The tree node row carries a sibling **"+" button** (`modeling-tree-insert-{elementId}` — a
  button must never nest inside the node button) that requests the insert. On a coarse pointer
  it is a full 44px target; on `(hover: hover) and (pointer: fine)` no column is reserved — the
  action floats on the trailing edge via the core `.tm-reveal-on-hover` pair (hidden until
  hover, always visible on `(hover: none)` and under `:focus-within`).
- The keyboard path is the node treeitem's **Insert** (or **Ctrl+Enter**) — emulated on keydown,
  `!repeat`-guarded, advertised via `aria-keyshortcuts`.
- The geometry is the HOST's: the editor routes the request to
  `TmModelingDiagramPreview.InsertAtViewportCenterAsync` (viewport center → page center
  fallback), which raises the same `OnNodeDropped`/`ModelingNodeDroppedEventArgs` contract as a
  real drop — hosts listening for tree reuse need no new wiring.
- With `AllowNodeDrop="false"` neither the drop zone nor the alternative exists
  (`?scenario=drag-readonly` demo). The alternative is a DESKTOP/TABLET affordance: the mobile
  band (G2) renders neither the drag grip nor the "+" button — G2 viewing, edits go through
  "Open in editor".

## The "Open in editor" dialog

The dialog is a viewport-level core `TmModal` (G6) — a real scrim, a `TmFocusScope` trap with an
inert background, document-level Escape, a `popover="manual"` top-layer root — never clipped to
the editor box. The modal renders ONCE, outside both layout branches (final review): a flip
across the 768 boundary re-presents the SAME `TmDialogEditor` instance (no teardown, no engine
reboot); the fullscreen-vs-dialog choice rides the TmModal `MobilePresentation` parameter.
Decisions:

- **One header** (`MODELING-OPEN-EDITOR-SINGLE-HEADER`): the `TmModal` header owns the title
  (the DOCUMENT title, falling back to the localized "Open in editor" label) plus the
  `TmModelingDiagramPreview_OpenEditorSummary` subtitle; the embedded `TmDiagramEditor` renders
  with `ShowTitle="false"` (public API, never CSS).
- **Header close (Fáze 10)**: on the docked bands (desktop/tablet) the close is a TEXT
  `TmButton` — "Close" (`TmModal_Close`), icon `x`, `ButtonVariant.Ghost` — rendered through the
  core `TmModal.HeaderActions` slot with `ShowCloseButton="false"` (the icon-only
  `.tm-modal-close` is disabled). On the mobile **fullscreen** presentation the icon-only close
  stays and no actions slot renders. The frozen `data-testid="modeling-open-diagram-close"` and
  the `data-modeling-focus="open-editor-close"` hook live on whichever close is ACTIVE, so the
  open focus and host/E2E selectors keep working on both bands (see Breaking / Migration — E2E
  consumers key off the test id, never off `.tm-modal-close`).
- **No footer** (G8): `DocumentChanged` edits propagate live, so the scrim click does NOT close
  (`CloseOnOverlayClick="false"`) — only the header close (text button or ×), `TmModal.OnClose`
  and an Escape the embedded editor did not consume close it.
- **Footprint**: ~90vw × 90vh on desktop/tablet (through `.tm-modeling-editor__open-editor-modal
  .tm-modal`); the mobile band presents **fullscreen** and hands the embedded editor a
  full-height body (`tm-modal-body` padding `0` there, `--tm-space-4` elsewhere). The footprint
  is a `@container tm-modeling-editor (width < 768px)` query — a 500px editor inside a wide
  viewport gets the compact dialog, a tablet editor keeps the big one.
- **Embedded editor API**: `ToolbarPreset="Full"`, every panel visible. The dialog body is its
  own layout scope: the embedded editor resolves **AUTO** from the dialog body with the dialog
  breakpoints **768/1100** (`OpenEditorBreakpoints`) — NOT pinned to the editor's resolved mode
  (the pin went stale the moment the editor's container and the ~90vw dialog body disagreed).
  A 1024 or 1180 tablet window renders a 922/1062px body, which resolves the DE tablet layout,
  while a desktop dialog body (≥1100px) still resolves desktop. The floating minimap starts
  **collapsed on a tablet** (Fáze 9): the dialog seeds the DE's additive
  `MinimapInitiallyCollapsed` with the editor's resolved tablet mode — the overlay covered the
  touch canvas there — and it reopens from the overlay's reopen button; a desktop dialog body
  keeps the expanded card (the seed is a `bool` — a desktop body is simply `false`).
- **Escape ordering** (confirmed Fáze 10): the dialog's trap honors Escape on a document *bubble*
  keydown listener, so every DiagramEditor Escape consumer runs in the **capture** phase and marks
  a handled Escape (`preventDefault()` + `tmEscapeHandled`). One press closes exactly ONE layer —
  the topmost one: the context menu, then the "⋯" view menu, then the search panel, then an
  inline edit of an edge/node label (the edit CANCELS — value reverted, nothing pushed to the
  undo stack — and the dialog stays open), then a running gesture or a non-empty selection; an
  Escape NOTHING consumes keeps bubbling and closes the dialog. The search-close path refocuses
  the canvas via `TmDiagramCanvas.FocusCanvasAsync()`. Contract:
  [docs/diagram-editor.md](docs/diagram-editor.md).
- **Focus return**: the editor's `focusWithin` close path is the SINGLE restore owner (final
  review) — the dialog's `TmModal` renders with `RestoreFocus="false"` (the additive parameter:
  the scope renders `data-restore-focus="false"` and the trap module skips its own restore), so
  a mobile close cannot race a restore target that does not exist there. The restore moves to
  the ACTUAL trigger (`OpenInEditorTriggerId`, hook `data-modeling-focus="open-in-editor"`) or,
  on mobile, the action-bar "More" / header "⋯" host (`tm-modeling-editor__more-host`,
  `tabindex="-1"` proxy). A next dialog opened from another trigger cannot inherit a stale
  restore selector.

## CSP-safe interop (`modeling-editor.js`)

`_content/Tempo.Blazor.Modeling/js/modeling-editor.js` is a plain ES module imported per editor
instance (`ModelingInteropModule`, disposed with the component). Exports:

| Export | Purpose |
|--------|---------|
| `registerTreeDragBridge()` | Document-level `dragstart`/`dragend` `dataTransfer` bridge for `TmModelingModelTree`. |
| `scrollTreeElementIntoView(treeRoot, elementId)` | Scroll a tree node into view (auto-reveal of an externally selected element). |
| `focusTreeElement(treeRoot, elementId)` | Tree keyboard navigation onto a node. |
| `focusTreeGroupHeader(treeRoot, groupKey)` | Tree keyboard navigation onto the APG group `treeitem` (via its `data-group-key`). |
| `focusWithin(root, selector)` | Overlay focus move + focus return in `TmModelingEditor`, scoped to the editor root. Accepts a comma-separated priority list; the hook selectors are the stable `data-modeling-focus="open-in-editor"` / `"open-editor-close"` / `"inspector-panel"` attributes — never `data-testid`. |

The tree helpers query INSIDE the caller's `treeRoot` `ElementReference`; a failed import is not
cached and every call site degrades to a no-op.

## DOM contract (stable)

The editor root `<section data-testid="modeling-editor">` carries the frozen state attributes:
`data-state`, `data-load-count`, `data-generation-count`, `data-provider`, `data-notation`,
`data-viewpoint`, `data-selected-element-id`, `data-selected-relationship-id`,
`data-element-count`, `data-relationship-count`, `data-diagram-node-count`,
`data-diagram-edge-count`, `data-active-panel` and `data-open-editor` (`"true"`/`"false"`).
Panel test ids (`modeling-model-tree-panel`, `modeling-preview-panel`, `modeling-inspector-panel`,
`modeling-issues-panel`, `modeling-panel-tab-*`, `modeling-tab-properties`,
`modeling-tab-relations`, `modeling-tab-source`, `modeling-related-panel`,
`modeling-related-{outgoing|incoming|source|target}-{relationshipId}`,
`modeling-related-{outgoing|incoming}-{counterpart|relationship}-{relationshipId}`,
`modeling-tree-drawer`, `modeling-inspector-sheet`, `modeling-sheet-close`,
`modeling-open-diagram-editor`, `modeling-open-diagram-close`) are the contract for hosts and
E2E — never positional selectors into the `TmEditorShell` or `TmDiagramEditor` internal DOM. The
docked inspector tablist has THREE tabs (Properties | Relations | Source) — key off the tab test
ids, never the index (Breaking / Migration).
Modeling CSS never touches `.tm-diagram-editor__*` internals; the nested editor is driven only
through its public contract (`ToolbarPreset`, `ShowToolbar`, `ShowTitle`).

The docked desktop shell persists user-resized panel widths under `{TestIdPrefix}-modeling-editor`
(or `PersistKey`) — the `TmEditorShell` widths contract. The editor's height contract is
`min(768px, 100vh - 32px)` unless the host passes `Height`.

## Localization

All user-facing strings resolve through `ITmLocalizer` keys in the four core tables
(`TmModelingEditor_*`, `TmModelingModelTree_*`, `TmModelingIssuePanel_*`,
`TmModelingSourcePanel_*`, `TmModelingDiagramPreview_*`, `TmModelingViewSelector_*` plus the
`TmModelingSemanticType_{key}` group labels). Notable keys: `TmModelingEditor_SelectProvider`,
`TmModelingEditor_LastLoaded`, `TmModelingEditor_Properties`, `TmModelingEditor_SourceStaleHint`,
`TmModelingIssuePanel_Collapse`/`_Expand` (fr/sk name the *issues panel*),
`TmModelingEditor_ChangeView`/`_Reload`/`_MoreOptions`, the short mobile labels
`TmModelingEditor_TreeShort`/`_ElementsShort`/`_RelationshipsShort`/`_Nodes`/`_Edges` and
`_CloseIssues`/`_ToggleIssues` (the tablet issues sheet), the neutral count key
`TmModelingIssuePanel_Count` ("Issues: {0}" — a plain key, no plural family) and the Fáze 11
filter/sort keys `TmModelingIssuePanel_FilterPlaceholder`, `_SortLabel`, `_SortInput`,
`_SortSeverity`, `_SortSource`, `_SeverityFilterLabel`, `_NoMatch`, `_ClearFilters` and
`_CountFiltered` ("Issues: {0} of {1}" / "Nálezy: {0} z {1}" — a plain key, no plural family).
Fáze 12 adds the hierarchy disclosure keys: `TmModelingModelTree_ExpandNode` / `_CollapseNode`
("{0}" = the node display name, used as the chevron `title`) and the plural family
`TmModelingModelTree_ChildCount_*` ("{0} child(ren)", spoken on a parent node's aria-label).
Fáze 13 adds the relations keys: `TmModelingEditor_Relations` (the tab title),
`TmModelingRelated_Outgoing` / `_Incoming` / `_Source` / `_Target` (group headings),
`TmModelingRelated_Empty`, `TmModelingRelated_SelectElement` / `_SelectRelationship` ({0} = the
row target) and the `TmModelingRelated_RelationshipCount_*` plural family (the sr-only count of a
group heading).

## Demo

`src/Tempo.Blazor.Demo.SharedUI/Pages/ModelingEditorPage.razor` showcases the opt-in surface:
`ShowProviderSelector="true"` + `@bind-ProviderKey` + `ProviderDisplayName` (two registered demo
providers — the default mixed model and `DemoUmlModelingModelProvider`), the `?notation=` /
`?viewpoint=` deep links, `OnDiagramGenerated`, `AllowNodeDrop` (off under
`?scenario=drag-readonly`), `ModelLoaderOverride` (error / delayed / large-model / bpmn2 / uml25
scenarios) and the demo-chrome accommodations — the demo page owns its height with a
demo-scoped rule; a real host owns the editor height through the `Height` parameter.

## Tests

- bUnit: `TmModelingEditorTests`, `TmModelingEditorDialogTests`, `TmModelingEditorTabletTests`,
  `TmModelingEditorMobileTests`, `TmModelingEditorSingleStateTests` (the retry/empty screens and
  the Height CSS-length guard), `TmModelingModelTreeTests`, `TmModelingModelTreeHierarchyTests`,
  `TmModelingRelatedElementsPanelTests` (Fáze 13 — the panel, the tab, the badge, the navigation
  and the mobile section), `TmModelingDiagramPreviewTests`,
  `TmModelingViewSelectorTests`, `TmModelingIssuePanelTests`, `TmModelingInspectorTests`,
  `TmModelingSourcePanelTests`, `TmModelingLocalizationM15Tests` (+ the package-wide
  `ModelingNoEvalInteropTests` guard and `ModelingSemanticTypeIconsTests`).
- E2E: the `Modeling*` specs (M8–M26) cover the desktop dock, the tablet drawer/sheet rules, the
  mobile G2 band, the provider selector, tree collapse/filter, the CSP lane (`?csp=1`), the
  open-editor dialog Escape/focus contracts and the M26 performance budgets (inactive tab bodies
  never mount; the tree filter re-render budget). `ModelingRelatedElementsE2ETests` (Fáze 13)
  covers the desktop Relations tab (badge + counterpart navigation), the tablet sheet tap, the
  mobile section at 320×640 without horizontal overflow and the a11y names.
- Node lane: `npm run test:diagram-editor` covers the shared engine the preview embeds.
