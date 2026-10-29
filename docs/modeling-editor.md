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
| `TmModelingModelTree` | The APG `role="tree"` of model elements grouped by `SemanticType`, with filter, collapse groups and the drag/insert bridge. |
| `TmModelingInspector` | Read-only inspector for the selected element/relationship (MO2 — the inspector never edits). |
| `TmModelingSourcePanel` | The "Source" inspector tab: provider/system/version/trust metadata, freshness and the "Load model" reload. |
| `TmModelingIssuePanel` | The issues list (severity, message, recommended fix, source navigation) with a collapsible dock header. |
| `TmModelingViewSelector` | The labeled Notation / Viewpoint selects of the header. |

## Responsive scope (G2)

Like the DiagramEditor and the Wireframe editor, `TmModelingEditor` resolves its presentation
from its **own container** (`TmLayoutObserver`, `data-layout` on the root — never the viewport,
`container: tm-modeling-editor / inline-size`), with the DiagramEditor pair 768 / 1200:

| Layout | Container width | What the user gets |
|---|---|---|
| **Desktop** | ≥ 1200 | The core `TmEditorShell`: Header (title + state badge, provider field/chip, `TmModelingViewSelector`, Inspector toggle), Left = model tree, Canvas = the preview with the **collapsible issues dock** under it (an **issues-empty** body drops the dock row to `auto` when the model has zero issues), Right = inspector with **Properties \| Source** tabs (only the active tab body renders), StatusBar = the status strip. The preview's embedded editor keeps the **Zoom** toolbar group only — the search + "⋯" header-bar row is gone with its groups (the DE renders no empty header bar), and the status bar keeps the zoom controls. |
| **Tablet** | 768–1199 | The shell's `TabletPanelPresentation.Overlay`: the tree is an overlay **drawer** (`modeling-tree-drawer`), the inspector a non-modal **side sheet** (`modeling-inspector-sheet`) — at most ONE open at a time, both **start closed**. The `modeling-panel-tabs` row under the header is the visibility switch (Tree/Inspector/Issues are `aria-expanded` disclosure toggles; Preview closes all) — **Issues is the fourth panel**, a non-modal side sheet (`modeling-issues-sheet`) under the same one-open rule, never a dock row under the canvas; its tab carries the count badge. 44px coarse-pointer targets; the preview canvas row keeps a `minmax(20rem, 1fr)` floor. |
| **Mobile** | < 768 | **Viewing + light edits (G2).** A `TmTabs` tablist **Preview \| Tree \| Inspector \| Issues** (the issue-count badge hides at zero) where exactly ONE panel body mounts at a time (M26 budget); a compact header (title, state badge, one "⋯" `TmDropdown` sheet: Change view / Generate diagram / Open in editor); a **status card** (`modeling-status-card`, four count tiles with short visible labels + aria-hidden type icons + last-loaded); and the F5 `TmMobileActionBar` (Reload \| Change view \| Issues, with Generate diagram + Open in editor behind More). **No drag and no tree→preview insert**: reuse is a desktop/tablet editing intent — on mobile the tree renders neither the "+" button nor the drag grip, and edits go through "Open in editor" (G2 viewing). Preview zoom is pinch-only — zoom buttons live in "Open in editor". The open-editor dialog is a **fullscreen** `TmModal` presentation handing the embedded editor a full-height body (the DE's own mobile mode drives inside). |

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
| `OpenInEditorTriggerId` | `string?` | `null` | Stable DOM id stamped on the open-editor trigger so the dialog's `TmFocusScope` can name the restore target. |
| `AllowNodeDrop` / `OnDrop` / `ActiveDraggedElement` / `OnNodeDropped` | — | — | The drop target contract. |
| `OnGenerateRequested` / `OnOpenInEditor` / `OnRelationshipSelected` | — | — | Host callbacks. |

Public methods: `InsertAtViewportCenterAsync(ModelingElementDto element)` — the non-drag insert;
creates the node at the center of the diagram viewport (`TmDiagramEditor.GetViewport()`, page
center when unknown) through the same clamp/collision path as a drop and raises the SAME
`OnNodeDropped` contract. `RefreshViewportAsync()` — final UX review M5: after the hosting
editor re-grids the canvas column (an issues-dock collapse/expand), the embedded viewer
re-measures (the DE's JS `relayout` path) so the diagram refits the new box.

### `TmModelingModelTree`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Elements` | `IReadOnlyList<ModelingElementDto>` | — | The tree source. |
| `SelectedElement` / `SelectedElementChanged` / `OnElementSelected` | — | — | Selection (an external arrival inside a collapsed group auto-reveals and scrolls to it). |
| `FilterText` / `FilterTextChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way filter. The parameter SEEDS the internal filter and is never assigned inside the component; user edits raise the callback (and clear the transient filter-collapse set). |
| `CollapsedGroups` / `CollapsedGroupsChanged` | `IReadOnlyCollection<string>?` / `EventCallback<...>` | `null` | Two-way collapsed `SemanticType` group keys (owned and bound by the editor). While a filter is active, toggling works on a transient set cleared when the filter text changes. |
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

### `TmModelingIssuePanel`

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Issues` | `IReadOnlyList<ModelingIssueDto>` | — | The issues to list. |
| `OnIssueSelected` | `EventCallback<ModelingIssueDto>` | — | Navigable issue activated (source element/relationship). |
| `Collapsed` / `CollapsedChanged` | `bool` / `EventCallback<bool>` | `false` | Two-way dock collapse. The body always renders (the `aria-controls` id is never missing); a collapsed dock hides the body with `hidden`. |
| `ShowToggle` | `bool` | `true` | The header collapse toggle (`modeling-issues-toggle`, `aria-expanded`/`aria-controls`). The editor's mobile Issues tab passes `false` and shows a plain heading. |
| `Class` | `string?` | `null` | Extra class. |

Issues without a source element/relationship render as a plain non-interactive `<div>` (same
`data-testid`, `data-severity`, `data-source-*`, same text) — never a button.

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
- **No footer** (G8): `DocumentChanged` edits propagate live, so the scrim click does NOT close
  (`CloseOnOverlayClick="false"`) — only the × close, `TmModal.OnClose` and an Escape the
  embedded editor did not consume close it.
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
  expanded — the DE exposes no "start collapsed" knob for the tablet overlay (noted for the DE
  plan).
- **Escape ordering**: the dialog's trap honors Escape on a document *bubble* keydown listener,
  so every DiagramEditor Escape consumer runs in the **capture** phase and marks a handled
  Escape (`preventDefault()` + `tmEscapeHandled`) — the open search panel, the "⋯" view menu,
  the context menu, a running gesture or a non-empty selection close/cancel inside the editor
  while the dialog stays open; an Escape NOTHING consumes keeps bubbling and closes the dialog.
  The search-close path refocuses the canvas via `TmDiagramCanvas.FocusCanvasAsync()`. Contract:
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
`modeling-issues-panel`, `modeling-panel-tab-*`, `modeling-tab-properties`, `modeling-tab-source`,
`modeling-tree-drawer`, `modeling-inspector-sheet`, `modeling-sheet-close`,
`modeling-open-diagram-editor`, `modeling-open-diagram-close`) are the contract for hosts and
E2E — never positional selectors into the `TmEditorShell` or `TmDiagramEditor` internal DOM.
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
`_CloseIssues`/`_ToggleIssues` (the tablet issues sheet), and the neutral count key
`TmModelingIssuePanel_Count` ("Issues: {0}" — a plain key, no plural family).

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
  the Height CSS-length guard), `TmModelingModelTreeTests`, `TmModelingDiagramPreviewTests`,
  `TmModelingViewSelectorTests`, `TmModelingIssuePanelTests`, `TmModelingInspectorTests`,
  `TmModelingSourcePanelTests`, `TmModelingLocalizationM15Tests` (+ the package-wide
  `ModelingNoEvalInteropTests` guard and `ModelingSemanticTypeIconsTests`).
- E2E: the `Modeling*` specs (M8–M26) cover the desktop dock, the tablet drawer/sheet rules, the
  mobile G2 band, the provider selector, tree collapse/filter, the CSP lane (`?csp=1`), the
  open-editor dialog Escape/focus contracts and the M26 performance budgets (inactive tab bodies
  never mount; the tree filter re-render budget).
- Node lane: `npm run test:diagram-editor` covers the shared engine the preview embeds.
