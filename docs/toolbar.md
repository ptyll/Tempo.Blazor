# Toolbar: priorities, overflow and labels

`TmToolbar` holds `TmToolbarButton`s, `TmToolbarFileButton`s, `TmToolbarItem`s, `TmToolbarDivider`s, `TmToolbarGroup`s and any custom content. Since F4 it can
move the buttons that do not fit into a **More** menu instead of overflowing or scrolling, and
buttons can show their text next to or under the icon.

```razor
<TmToolbar Overflow="ToolbarOverflow.Menu" Labels="ToolbarLabels.IconsWithText" AriaLabel="Diagram tools">
    <TmToolbarButton Icon="@IconNames.Plus" Text="New" LabelPosition="ToolbarLabelPosition.Below" />
    <TmToolbarButton Icon="@IconNames.Copy" Text="Copy" Priority="ToolbarButtonPriority.Secondary" />
    <TmToolbarDivider />
    <TmToolbarButton Icon="@IconNames.Trash" Text="Delete" Variant="ButtonVariant.Danger"
                     Priority="ToolbarButtonPriority.OverflowOnly" />
    <TmToolbarButton Icon="@IconNames.Check" Text="Save" Variant="ButtonVariant.Primary"
                     Priority="ToolbarButtonPriority.Pinned" />
</TmToolbar>
```

## Parameters

| Component | Parameter | Values | Default |
|-----------|-----------|--------|---------|
| `TmToolbar` | `Overflow` | `None` · `Menu` | `None` (nothing changes for existing toolbars) |
| `TmToolbar` | `Labels` | `Auto` · `Icons` · `IconsWithText` | `Auto` |
| `TmToolbar` | `OverflowLabel` | accessible name + tooltip of the More trigger | localized "More" |
| `TmToolbar` | `AriaLabel` | accessible name of the toolbar | `Title`, then a localized "Toolbar" |
| `TmToolbar` | `OverflowPresentation` | `PanelPresentation` of the More menu | `Auto` (sheet on phones) |
| `TmToolbarButton` | `Priority` | `Primary` · `Secondary` · `OverflowOnly` · `Pinned` | `Primary` |
| `TmToolbarButton` | `LabelPosition` | `Inline` · `Below` · `Hidden` | `Inline` |
| `TmToolbarButton` | `Pressed` | `bool?` — `null` = plain button, `true`/`false` = toggle | `null` |
| `TmToolbarButton` | `PressedMixed` | renders a `false` toggle as `aria-pressed="mixed"` | `false` |
| `TmToolbarButton` | `PreserveFocus` | a pointer press does not move focus to the button (an editor selection survives) | `false` |
| `TmToolbarFileButton` | `Text` / `Icon` / `Tooltip` / `Class` | the visible button (a `TmToolbarButton` underneath) | — |
| `TmToolbarFileButton` | `Variant` / `LabelPosition` / `PreserveFocus` | passed through to the inner `TmToolbarButton` (same values as above) | `Ghost` / `Inline` / `false` |
| `TmToolbarFileButton` | `Accept` / `Multiple` | the `accept` / `multiple` of the hidden file input | `null` / `false` |
| `TmToolbarFileButton` | `OnChange` | `EventCallback<InputFileChangeEventArgs>`; raised only when at least one file was chosen | — |
| `TmToolbarFileButton` | `Priority` | `Primary` · `Secondary` · `OverflowOnly` · `Pinned` — exactly the button's | `Primary` |
| `TmToolbarFileButton` | `Disabled` | disables the button, its "More" entry and the file input | `false` |
| `TmToolbarItem` | `Priority` | `Primary` · `Secondary` · `OverflowOnly` · `Pinned` | `Primary` |
| `TmToolbarItem` | `MenuLabel` / `MenuIcon` / `OnMenuInvoke` / `MenuKeepOpen` | the single "More" entry | — |
| `TmToolbarItem` | `MenuItems` | a labelled group of checkable/plain entries instead of one entry | `null` |
| `TmToolbarItem` | `Disabled` | disables the "More" entry/entries (the bar control stays the host's) | `false` |
| `TmToolbarGroup` | `Label` | accessible name on the bar and header of the group's section in More | unlabeled (no role) |

## Overflow = Menu

* **Priority is an overflow rank, not a rank number and not an order.** Buttons render in the order you
  write them, always — the menu too. `Secondary` leaves the bar before any `Primary`; of two buttons
  with the same priority the *later* one (in DOM order) leaves first. `OverflowOnly` buttons never
  appear on the bar and never count against the room; each leaves a hidden, zero-width
  `<span hidden data-tm-toolbar-item data-pin="always">` marker at its written place, so it reports its DOM
  position like every other button and keeps its written slot in the menu. `Pinned` is the opposite pin: the button
  **never** moves into More at any width (the trailing Save / primary call to action); it is measured
  as fixed width, like a title, and is not counted against the room the collapsible buttons share. The
  enum values are *not* ranks — `Pinned` is the numerically last member (it was added after
  `OverflowOnly`, so existing values keep their numbers): never compare or cast them; the toolbar maps
  them through `ToolbarOverflowLayout.Rank`. If nothing collapsible fits, the shared partition keeps
  one button on the bar (the clamp) — a pinned button satisfies it by itself.
* **One ordering rule.** Which buttons leave is `ActionOverflowLayout.Partition` — the same
  function the F5 `TmMobileActionBar` uses (through `ToolbarOverflowLayout`, which maps
  `Priority` to the rank, `OverflowOnly` to the `Always` pin and `Pinned` to the `Never` pin). The
  toolbar's measurement only decides *how many* fit.
* **The DOM order is the order of record.** `tm-toolbar.js` counts buttons in DOM order and reports
  `OnFitChanged(maxVisible, orderedIds)` — the count **and** the `data-tm-toolbar-item` ids in DOM
  order. `TmToolbar` re-sorts its registered buttons to that order before it resolves the partition, so
  the More menu order and the tie-break ("the later one leaves first") follow what the user sees, even
  for a conditional button inserted *between* existing ones after the first render. A reorder alone
  (same count) is reported too.
* **Measurement** is `tm-toolbar.js`: a `ResizeObserver` on the toolbar plus a `MutationObserver`
  (children, class/disabled/rank/pin attributes, and **character data** — a changed label re-measures),
  coalesced into one animation frame, with signature dedup — .NET is called only when the count or the
  order changes, so there is no render loop. A button that left the bar stays in the DOM *collapsed*
  (`.tm-toolbar-item--collapsed`: out of flow, invisible, `inert`, `aria-hidden`) so its width stays
  measurable; that is why widening the toolbar brings it back without a second pass. A bar that cannot
  be measured (no item in the DOM, not laid out) reports nothing rather than "0 fit". The module
  detaches itself when its root leaves the document.
* **Items at any depth, and groups.** Every `TmToolbarButton` and `TmToolbarItem` below the toolbar registers (cascade) and
  is measured wherever it sits. `.tm-toolbar-start` / `.tm-toolbar-actions` are flattened, and so is
  any wrapper that holds items: a semantic group is `<TmToolbarGroup Label="Edit">`, which renders the
  `role="group"` + accessible name + `data-tm-toolbar-group` wrapper (a plain wrapper marked
  `data-tm-toolbar-group` works as a layout-only group); the children take room individually, the
  wrapper's own horizontal padding and border count as fixed width. Since F14 its **margin and a
  column gap wider than the bar's are measured too** — conservatively: the wider gap is reserved per
  adjacency between the wrapper's direct items as if all of them stayed (never over-commits, the I6
  invariant), a narrower gap is already over-reserved by the bar gap, and the start/actions
  containers share the bar gap (their margins are not double-counted). Keep a group wrapper's gap
  equal to the bar's (the default `--tm-space-2`) unless it must be wider. Without the flattening a
  wrapped group would look like one fixed block with no items and collapse everything into More.

  ```razor
  <TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Diagram tools">
      <TmToolbarGroup Label="Edit">
          <TmToolbarButton Icon="@IconNames.Undo" Text="Undo" />
          <TmToolbarButton Icon="@IconNames.Redo" Text="Redo" Priority="ToolbarButtonPriority.Secondary" />
      </TmToolbarGroup>
      <TmToolbarDivider />
      ...
  </TmToolbar>
  ```
* **Dividers that separate nothing** — first or last among the visible content, or directly followed by
  another divider ("Pan | | Find" at 390px) — are marked `data-tm-divider-redundant` by the
  measurement pass and hidden with `visibility` (they keep their box, so the measured width and the fit
  cannot oscillate). The attribute is not observed, so marking never re-triggers a pass. A divider never flex-shrinks
  (.tm-toolbar-divider { flex-shrink: 0 }), and a divider squeezed to 0px is still counted by its CSS width and
  margins, so a narrowed row cannot over-commit.
* **Test ids on More entries.** A host `data-testid` is copied onto the More entry as `data-testid`
  only when the button has no bar copy (`Priority == OverflowOnly`). A button that still renders on
  the bar — including a collapsed Secondary, which stays in the DOM `inert` so its width can be
  measured — keeps `data-testid` on that button and exposes the same value as `data-menu-testid` on
  the menu entry. With More open, exactly one `[data-testid]` match exists. A file button's input
  keeps its `-input` suffix either way (`wd-import` stays on the bar button, `data-menu-testid="wd-import"`
  on the entry, `wd-import-input` on the input).
* **The More menu** is the shared overflow menu (`ActionOverflowMenu`, also the action bar's),
  so it honours the whole host contract of [overlays.md](overlays.md): close-before-invoke,
  first enabled item focused, `aria-expanded` accurate on every close path (a Tab out of an open
  popover menu closes it), focus back to the trigger, a disabled trigger when only disabled items
  remain. A `Danger` button's entry is tinted. **A pressed button keeps its state in More**
  (F12/C2): `Pressed` (`bool?` — `null` is a plain button with no `aria-pressed`;
  `true`/`false` render `aria-pressed` and the pressed look, a host-splatted `aria-pressed` still
  wins on the bar) and `PressedMixed` (`aria-pressed="mixed"` for a partly-on toggle) make the
  bar button a toggle and its menu entry a `role="menuitemcheckbox"` with the matching
  `aria-checked` and a check-mark slot — activating it closes the menu like every entry, and the
  host flips the state in `OnClick`. On a phone-sized viewport the menu is the shared bottom
  sheet with 44px entries; an editor/formatting toolbar sets `OverflowPresentation="Popover"` so the
  user's selection survives (see the F4 decision in overlays.md). The menu survives a reorder while
  open (resize, rotation): its items are keyed and every item carries a stable capture.
* **Groups survive into More** (F13/C3). The collapsed members of a `TmToolbarGroup` keep the group:
  each contiguous run of menu items sharing the group renders as a `role="group"` section named by
  the group's `Label` — the visible header is `aria-hidden` (the section's name is its
  `aria-label`, so no heading ever sits in the menu's accessibility tree) — and adjacent runs are
  divided by a `role="separator"`. The order is the written order, always: grouping never reorders
  (the F5 rule), so a group interrupted by an ungrouped button renders as two runs separated by the
  ungrouped entry. A run whose first item carries no label renders flat (separators only). The
  arrow keys, Home/End and typeahead cross the sections and never stop on a header or a divider
  (`tm-menu-nav.js` roves menu items only), and the initial focus is still the first **enabled**
  menu item.

## Non-button items (`TmToolbarItem`)

Since F14 a toolbar can hold **controls that are not buttons** — a `TmSelect`, `TmColorPicker`,
`TmToggle`, a custom dropdown — and they leave the bar exactly like a `TmToolbarButton`:

```razor
<TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Text tools">
    <TmToolbarItem MenuLabel="Font size" Priority="ToolbarButtonPriority.Secondary" MenuItems="@_sizes">
        <TmSelect TValue="int" @bind-Value="_fontSize" Options="@_sizeOptions" />
    </TmToolbarItem>
    <TmToolbarItem MenuLabel="Text colour" OnMenuInvoke="@OpenColourDialog" MenuKeepOpen="true">
        <TmColorPicker @bind-Value="_colour" />
    </TmToolbarItem>
    <TmToolbarButton Icon="@IconNames.Save" Text="Save" />
</TmToolbar>
```

* **The wrapper is the measured item.** `TmToolbarItem` renders a `tm-toolbar-item` wrapper carrying
  `data-tm-toolbar-item` (the item's stable id), `data-rank` and `data-pin`, exactly like a button;
  `tm-toolbar.js` measures it as ONE item (it is never a layout group, even with nested content) and
  it collapses whole — out of flow, invisible, `inert` — so its width stays measurable. An item
  collapses only when it offers something in "More": `MenuItems` with entries, or a non-empty
  `MenuLabel` bound to an `OnMenuInvoke` handler. Otherwise it has **no menu representation** and
  never leaves the bar (it behaves as `Pinned` whatever its `Priority`).
* **In "More" it is plain menu entries — never a free template.** A `RenderFragment` rendered inside
  `role="menu"` would put a `<select>` in a menu, which is invalid ARIA (a menu may own only
  menuitem*/group/separator) and could not be roved. So an item offers either **one entry**
  (`MenuLabel` + `OnMenuInvoke` + optional `MenuIcon`/`MenuKeepOpen` — typically opening the full
  control in a dialog or sheet while the menu stays open under it) or **a labelled group**
  (`MenuItems`: the entries render as one `role="group"` section named by `MenuLabel`, checkable
  entries included, in the given order). `Disabled` disables the entry/entries; the bar control's own
  disabled state stays the host's.
* **A `TmToolbarButton` nested in an item never registers twice** — it is part of the item.
* **Text-entry controls stay their own Tab stop.** A native `select`/`input` inside an item is not in
  the roving set (the arrows never reach it); the item's first **non-text-entry** focusable (or its
  host-marked `data-tm-toolbar-control`) is the one arrow stop — a leading text-entry focusable does
  not hide a later button. `MenuItems` is compared **structurally** when the toolbar decides whether
  to recompute: a fresh-but-equal list per render is not a change, so a host may rebuild it whenever
  the state it renders from changes (a reference comparison would loop the recompute). The entries
  need unique `Id`s (or unique labels — the label is the fallback key the menu focuses by).
* **OverflowOnly / Pinned** mean exactly what they mean for a button: `OverflowOnly` renders only the
  hidden zero-width position marker and lives only in "More"; `Pinned` never leaves the bar.

## File buttons (`TmToolbarFileButton`)

A button that opens the browser's file chooser — *Import JSON*, *Attach file*. It exists because the obvious markup
(a `<label class="tm-toolbar-btn">` wrapping an `InputFile`, inside a `TmToolbarItem`) cannot leave the bar: a label
has no menu representation, so the toolbar had to keep it `Pinned`.

```razor
<TmToolbar Overflow="ToolbarOverflow.Menu" AriaLabel="Editor tools">
    <TmToolbarButton Icon="@IconNames.Save" Text="Save" />
    <TmToolbarFileButton Icon="@IconNames.Upload" Text="Import JSON" Accept=".json"
                         Priority="ToolbarButtonPriority.Secondary" OnChange="@ImportAsync" />
</TmToolbar>
```

* **On the bar it is a `TmToolbarButton`** — the same `tm-toolbar-btn` classes, `Labels`, `Priority`, rank, collapse
  and roving tab stop (the component renders a real `TmToolbarButton`; nothing is re-implemented). `Class` and any
  splatted attribute (`data-testid`, `aria-*`) land on that button; `DataTestId` / `TestIdPrefix` come from
  `TmComponentBase`, the hidden input gets the button's id with an `-input` suffix.
* **In "More" it is an ordinary entry** (`Text` + `Icon`, a `menuitem`), and activating it opens the SAME hidden
  `InputFile`. A `Disabled` button offers a disabled entry and a disabled input.
* **The input is a sibling, outside the collapse wrapper.** The button collapses (out of flow, invisible, `inert`)
  when it does not fit; the `<input type="file">` next to it is never inside `.tm-toolbar-item--collapsed`, so a collapse
  or expand never remounts it (bUnit and E2E pin the node identity).
* **The chooser opens from a native listener.** `TmToolbarFileButton.razor.js` installs ONE capture-phase `click` listener
  on the document (shared by every file button, removed with the last one) and calls `input.click()` synchronously inside
  the browser's own click — a Blazor round-trip in between would sever the user activation WebKit requires. The bar
  button carries the input's id as `data-tm-file-trigger`; the "More" entry is matched by its `data-action-id`.
* **No key emulation.** Both the bar control and the menu entry are native `<button>`s — Enter and Space are the browser's
  (`docs/keyboard-activation-convention.md`). There is no `@onclick` and no `@onkeydown`.
* **Cancel and repeat.** A cancelled chooser raises no `OnChange`; the input is cleared before every open, so choosing the
  *same* file twice raises a change twice (re-importing a fixed JSON). Read the file inside the handler — the selection
  is dropped the next time the chooser opens. `Accept` only filters the chooser: validate what you read (size, JSON) and
  report an invalid file in the UI, as `TmWireframeEditor` does with its import banner.
* **Wireframe.** The component is a placeable wireframe element (`tempo.stencil.json`, role `toolbar-button`).
  `TmWireframeEditor` uses `Priority="Secondary"` for *Import JSON* (`data-testid="wd-import"` is kept on the button; the
  input is `wd-import-input`), so it is the first command to move into "More" — ahead of the arrange actions.

### Stability analysis (the earlier "element is not stable")

The earlier attempt made the label+`InputFile` item `Secondary` and Playwright failed 7 toolbar specs with
`element is not stable` on `button.tm-toolbar-more`. What was measured when this component was built (the More trigger's
position and the collapsed count sampled every 50 ms for 2.5 s after each width change, in the real editor):

* **The known mechanism of this kind in the code base is the group-box feedback loop.** `tm-toolbar.js` counts a group wrapper's
  padding and border as FIXED width; when every control of a group collapsed, dropping that padding freed a few pixels, one
  more item fit, the group re-expanded and the fit flipped forever (see the comment above `.tm-wd-editor__group` in
  `_wireframe-editor.css`, fixed in the tablet phase). It needs a group whose controls ALL collapse: the `file` group always
  keeps Export, which is `Pinned`, so Import JSON cannot trigger it.
* **The old label pattern, made `Secondary` with a menu entry, did NOT reproduce an oscillation** in the current editor (at
  most one layout change per width, nine widths from 1262 to 560 px); the new component measures the same. The previous
  attempt's exact markup is not in the history, so its failure is not reproduced; no further root cause is claimed.
* **What the design guarantees instead.** The measured item is a plain `TmToolbarButton`, whose box is identical collapsed
  and expanded, and the file input is never inside the collapse wrapper, so nothing about the input can change a measured
  width. A `TmToolbarItem` around a label has no way to name "the input to open" for its More entry, which is why the old
  Import JSON stayed `Pinned`; `TmToolbarFileButton` gives the menu entry exactly that link (`acquire(inputId, entryId)`).
* **Regression net.** `ToolbarFileButtonE2ETests.Wireframe_MoreTrigger_SettlesAtEveryWidth` asserts at most one layout
  change per width and a still trigger for the last second; the Playwright click on the trigger uses a 5 s budget in the
  import specs.

### Not migrated (yet)

* **`TmDiagramEditor` Import** is still a `<label class="tm-dropdown-item">` wrapping a `display:none` `InputFile` inside a view dropdown - not a toolbar button, so it does not use this component.
* **The Spreadsheet toolbar** keeps its own fixed-id file input and has no overflow menu, so there is nothing to move into "More".
* **`display:none` file inputs are a WebKit risk**: an input removed from rendering can refuse a programmatic `input.click()` there. `TmToolbarFileButton` therefore hides its input visually (`.tm-toolbar-file-input`: 1px, clipped, `opacity:0`) and never with `display:none`; a migration of the two hosts above has to do the same.

## PreserveFocus: buttons that do not steal the editor's focus

A formatting toolbar button normally takes focus on a pointer press, collapsing the editor's text
selection. `TmToolbarButton.PreserveFocus="true"` prevents the `pointerdown`/`mousedown` defaults,
so the selection (and the caret) survives the command; keyboard activation and the "More" entry are
unchanged. The DocumentEditor and DiagramEditor toolbars build on this for their formatting commands.

* **Focus survives a re-layout.** When the focused bar button collapses into More, focus moves to the
  More trigger (else to the roving stop) instead of dropping to `<body>`; a focus that is still valid or
  that the user moved elsewhere is left alone.
* The toolbar measures **its own width** — an editor panel in a 390px column collapses on a 1440px
  desktop.

## Layout contract (breaking since F4)

The toolbar is a **size container** (`container-type: inline-size`), which means it has **no intrinsic
inline size** of its own: in a context that sizes its children to their content — a flex item without a
basis/grow in a `space-between` row, an `inline-block`, a float, an absolutely positioned box, an `auto`
grid column, `fit-content` — it collapses to zero width. Give the host room:

```css
/* before (F3): the toolbar sized itself to its buttons */
.page-header { display: flex; justify-content: space-between; }

/* after: the toolbar fills the slack (or give it an explicit width) */
.page-header > .tm-toolbar { flex: 1 1 auto; min-width: 0; }   /* or: width: 100%; */
```

Inside a `TmToolbar`, `.tm-toolbar-btn` is `flex-shrink: 0` (a button keeps its natural width and leaves
the bar into More instead of squeezing into its touch-target `min-width`) and a coarse pointer gives
buttons and the More trigger a 44px target. Both rules are scoped to `.tm-toolbar`: a standalone
`TmToolbarButton` (`TmDiagramEditor`, `TmModelingDiagramPreview`) keeps its pre-F4 flex behaviour.

## The stable `data-tm-group` contract

A `TmToolbarGroup` may carry a host-supplied `data-tm-group` attribute (through its
`AdditionalAttributes`). The attribute is a **stable locator contract**: hosts and tests key off
`[data-tm-group='…']` scoped from the editor root — never off positional CSS selectors on the
toolbar's internal DOM. Where a group renders (start of the bar, trailing actions section, or as a
labelled section of the "More" menu) is a layout detail that may change; the vocabulary below is
frozen and may only grow.

| Value | Owner |
|-------|-------|
| `history` | TmDiagramEditor undo/redo; the PDF annotator's undo/redo pair |
| `clipboard` | TmDiagramEditor copy/paste |
| `tools` | TmDiagramEditor tool palette; the PDF annotator's mode pills (`TmPdfAnnotator`) |
| `format` | TmDiagramEditor formatting |
| `insert` | TmDiagramEditor insertion |
| `arrange` | TmDiagramEditor z-order/alignment |
| `search` | TmDiagramEditor search; the PDF viewer's search toggle |
| `panels` | TmDiagramEditor dock toggles; the PDF viewer's "Pages" (thumbnails) toggle |
| `zoom` | TmDiagramEditor zoom; the PDF viewer's zoom presets + fit modes |
| `view` | TmDiagramEditor view settings; the PDF viewer's single/continuous segmented control |
| `canvas-size` | TmDiagramEditor canvas size |
| `layout` | TmDiagramEditor auto-layout |
| `export` | TmDiagramEditor export; the PDF viewer's trailing file links + host `ToolbarEnd` (the PDF annotator's export/print group carries the same value) |
| `templates` | TmDiagramEditor stencils |
| `import` | TmDiagramEditor imports |
| `readonly` | TmDiagramEditor read-only toggle |
| `navigation` | The PDF viewer's prev / page-input / next group |
| `rotate` | The PDF viewer's rotation group |
| `comments` | The PDF viewer's annotations/comments toggle |
| `title` | TmScheduler toolbar title (the date popover trigger) |
| `primary` | TmScheduler toolbar primary action (the add button, only with an `OnAddClick` delegate) |
| `header-actions` | TmScheduler toolbar host actions, after the view switcher |
| `filter` | TmScheduler toolbar resource filter (only with `ShowResourceFilter`), after `header-actions` and before `primary`. On the compact row it is its own second row under the header, so it never squeezes `‹ title ›` |
| `overflow` | TmScheduler toolbar More menu (only when it has an entry) |

## Labels

* `Inline` (default) puts the text next to the icon; `Below` stacks it under the icon (the ribbon
  style of the tablet design); `Hidden` omits the text span entirely — the text stays the
  accessible name and the tooltip.
* `Labels="Auto"` (default) shows labels and drops them on a toolbar **narrower than `TmBreakpoints.Sm`
  (640px), measured on the toolbar** with a container query (`@container tm-toolbar (width < 640px)`).
  There is no JavaScript and no flash; the text remains the `aria-label`. This is independent of a
  shell's custom thresholds by design: a host whose shell uses other thresholds passes `Labels`
  explicitly from the cascaded layout context —

  ```razor
  <TmLayoutObserver Breakpoints="@_shellBreakpoints">
      <LayoutContent Context="layout">
          <TmToolbar Labels="@(layout.IsMobile ? ToolbarLabels.Icons : ToolbarLabels.IconsWithText)"
                     Overflow="ToolbarOverflow.Menu" AriaLabel="Tools">...</TmToolbar>
      </LayoutContent>
  </TmLayoutObserver>
  ```
* `Labels="Icons"` never shows icon-button text; `IconsWithText` always does. A button's own
  `LabelPosition="Hidden"` wins over the toolbar. A text-only button always keeps its text.

## Keyboard and accessibility

The toolbar carries the `toolbar` role and a **roving tabindex**: one tab stop, ArrowLeft/ArrowRight
(swapped in a right-to-left toolbar), Home and End move between the enabled controls that are on the
bar — collapsed copies, disabled buttons and the open More menu are skipped. Tab leaves the toolbar.
Inside the menu ArrowUp/ArrowDown/Home/End and typeahead come from `tm-menu-nav.js` (see
overlays.md). Because the toolbar ships the mechanism, `ToolbarRoleGuardTests` allow-lists exactly
this component (and asserts that `TmToolbar.razor` uses `ToolbarInterop`, whose source carries the module
path and the `attach` call); any other claim of the role without a roving mechanism still fails the guard.

**Hooks for custom content.** The roving set is `button.tm-toolbar-btn`, `button.tm-toolbar-more` and any
element marked `data-tm-toolbar-control`: put the attribute on a custom focusable to make it one stop
of the roving set instead of a stray Tab stop. A `TmDropdown` inside a
`TmToolbar` joins the roving set **automatically** (its trigger carries the attribute through the
cascade); a `TmSplitButton` marks **both** its buttons (the primary action and the menu toggle) — two
arrow stops, one Tab stop. Mark other custom focusables yourself or wrap them in a
`TmToolbarItem`. A host that renders the toolbar row inside its own wrapper marks the measured row
`data-tm-toolbar-row` (default: the `.tm-toolbar` root itself).

## Migrating from the DocumentEditor toolbar (DE-1.2)

The DocumentEditor keeps its own `ToolbarItemPriority` and `toolbar-overflow.mjs` (it measures which
ribbon commands are scrolled out of view). The core generalises that mechanism — `ToolbarButtonPriority`
and `tm-toolbar.js` (fit count + DOM order, signature dedup, rAF coalescing,
`ResizeObserver`/`MutationObserver`). The DocumentEditor migrates onto it in its own plan; nothing in the
DocumentEditor changed here.

| DocumentEditor | Core |
|----------------|------|
| `ToolbarItemPriority` | `ToolbarButtonPriority` (`Primary` / `Secondary` / `OverflowOnly`, plus `Pinned` for the trailing CTA) |
| `toolbar-overflow.mjs` scroll measurement (which commands are scrolled out of view) | `tm-toolbar.js` fit count: how many buttons FIT, reported with the DOM order |
| `TmDocumentToolbarOverflowMenu` | `ActionOverflowMenu` (the shared host, over `TmOverlayPanel Role="menu"`) |
| a menu that must not collapse the editor selection | `OverflowPresentation="PanelPresentation.Popover"` (editor/formatting toolbars; F3-PANEL-PRESENTATION) |

### Core prerequisites DE-1.2 / DiagramEditor adoption

All four were recorded as core follow-ups and are **in the core now** (2.9.1) —
nothing gates the adoption any more:

1. **A collapsible non-button item contract** — done (F14/C1): `TmToolbarItem` wraps a `Select`,
   `ColorPicker`, `GridPicker`, `Toggle` or a similar custom control so it leaves the bar like a
   button: the wrapper is the measured item, it collapses whole, and in "More" it is one entry or a
   labelled group of entries (never a free template — a select in `role="menu"` is invalid ARIA).
2. **Pressed / checked state** — done (F12/C2): `TmToolbarButton.Pressed` → `aria-pressed` →
   `TmActionItem.Checked` → `role="menuitemcheckbox"` in the menu (see "The More menu" above).
3. **Group headers and separators in `ActionOverflowMenu`** — done (F13/C3): the collapsed members of a
   `TmToolbarGroup` render as labelled `role="group"` runs with `role="separator"` dividers (see "Groups
   survive into More" above).
4. **Toolbar groups** — done (`TmToolbarGroup`, see above): grouped rows measure and collapse correctly.