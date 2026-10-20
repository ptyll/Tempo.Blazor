# Data-table presentation and migration

This guide describes the landed opt-in presentation and interaction parameters for `TmDataTable` and `TmMultiViewList`. Existing defaults are intentionally preserved; for new consumers, keep them unless a particular screen needs an opt-in.

## Defaults to preserve

| Component / parameter | Default | Effect |
|---|---|---|
| `TmDataTable.Appearance` | `DataTableAppearance.Plain` | No card-surface class; preserves the existing plain surface. |
| `TmFilterBuilder.Presentation` | `FilterBuilderPresentation.Inline` | Filter picker/editor remain in document flow. |
| `TmDataTable.MobileLayout` | `DataTableMobileLayout.None` | Existing table presentation; no responsive mobile layout is enabled. |
| `TmDataTable.KeyboardNavigation` | `DataTableKeyboardNavigation.Rows` | Row-based tab stops; arrows do not move between cells. |
| `TmDataTable.ShowFilterSummary` | `false` | No active-filter summary/clear control unless enabled. |
| `TmMultiViewList.ViewSwitcherLabels` | `ViewSwitcherLabels.IconsOnly` | View buttons retain icon-only visual labels (accessible names remain). |
| `TmMultiViewList.Appearance` | `DataTableAppearance.Card` | Multi-view list keeps its card surface. |
| `TmMultiViewList.AvatarFallback` | `AvatarFallback.None` | Missing avatar URLs render no fallback avatar. |
| `TmMultiViewList.ShowItemCount` | `false` | Item count is hidden unless enabled. |
| `TmMultiViewList.Responsive` | `false` | Responsive table/list behavior remains off. |
| `TmMultiViewList.TableCellLayout` | `TableCellLayout.Inline` | Title and subtitle stay in their own table columns. |
| `TmDataTable.ColumnMenuMode` | `ColumnMenuMode.None` | Header keeps the pin toggle. `Menu` replaces it with a kebab. |
| `TmDataTable.EditActionsMode` | `EditActionsMode.Icons` | Save and Cancel are icon buttons in the edited row. |
| `TmDataTable.AllowExportSelected` | `false` | The export menu lists formats only. |
| `TmDataTable.ShowActiveViewName` / `AllowSetDefault` | `false` | The view toggle reads "Views"; no Set as default action. |
| `TmDataTable.ShowFilterResultPreview` | `false` | The inline filter builder shows no match count. |
| `TmDataTable.GroupHeaderTemplate` | `null` | Group headers use the built-in title, value and count. |
| `TmMultiViewList.CardActivation` | `CardActivation.Button` | Cards are buttons. `StretchedLink` needs `ItemHref`. |
| `TmMultiViewList.ItemHref` | `null` | No stretched link is rendered. |
| `TmMultiViewList.MaxCardColumns` | `null` | The card grid is not capped. |
| `TmMultiViewList.GroupHeaderTemplate` | `null` | Group headers use the built-in markup. |
| `TmMultiViewList.StatusVariantField` | `null` | Every status badge uses the default variant. |
| `TmFilterBuilder.ShowResultPreview` | `false` | No match count. Needs `ResultCountProvider` as well. |

Do not change these defaults as part of consumer adoption. Set an opt-in only when the screen requires it.

## Responsive table layouts

`MobileLayout` selects the mobile treatment:

- `None` preserves the current table markup.
- `Scroll` enables horizontal scrolling. A column can opt into `MobilePinned`; when no visible data column is explicitly pinned, the first visible data column is pinned automatically. The option applies in scroll layout only.
- `Cards` presents each row as a card on narrow viewports. `TmDataTableColumn.CardRole` controls the card cell's semantic presentation: `Field` (default), `Title`, `Badge`, `Media`, or `Hidden` (omit the column from cards).

The legacy `CardMode` parameter is an alias for cards only when `MobileLayout` is `None`. If `MobileLayout` is explicitly `Scroll` or `Cards`, that choice takes precedence. Virtualized card rows render in a single column.

Example:

```razor
<TmDataTable TItem="Person" Items="people"
             MobileLayout="DataTableMobileLayout.Cards">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name"
                       CardRole="DataTableCardRole.Title" />
    <TmDataTableColumn TItem="Person" Title="Department" Field="p => p.Department" />
</TmDataTable>
```

## Keyboard navigation

`KeyboardNavigation="DataTableKeyboardNavigation.Grid"` opts into ARIA grid navigation with one roving tabindex. The current default, `Rows`, keeps one tab stop per row (Enter activates on keydown, Space on keyup). Keep `Rows` explicitly when preserving today's behavior is important; Grid is planned to become the default in the next major release.

Grid keys:

| Key | Behaviour |
|---|---|
| Arrow keys | Move the active cell. ArrowLeft on column 0 and ArrowRight on the last column stay put. |
| Home / End | First / last column of the current row. With Ctrl, also the first / last row. |
| PageUp / PageDown | Move 10 rows, clamped to the rendered page. They do not change the page. |
| Enter | Activates the row once (`OnRowClick`). A held key does not repeat. |
| Space | Toggles row selection when `Selectable` is set. It never activates the row. |
| F2 | Starts inline editing when the table is `Editable` and a column has an `EditTemplate`. Escape cancels and returns focus to the cell. |

The expander and the selection checkbox are columns of the grid (`role="gridcell"`, `aria-colindex`, roving tabindex). The active cell starts on the first data column; Home moves onto the checkbox or expander, and Enter or Space there is handed to that control (toggle detail / toggle selection) instead of activating the row. Cell keydown and keyup stop at the cell, so a key never also fires the row handler.

An empty grid keeps the single tab stop on the first column header (`role="columnheader"`); arrows move between headers. A grouped table makes each group header a `gridcell` with `aria-colspan` — Enter or Space toggles it, ArrowRight expands it, ArrowLeft collapses it, and ArrowUp/ArrowDown move between group rows. Data cells keep the same roving contract inside an expanded group. Virtualized mode emits `role="gridcell"` on the cells Virtualize has rendered. PageDown or End past the rendered window scrolls the container by `index * VirtualItemSize` so the row renders, then focus moves onto it. Card layout (`MobileLayout="Cards"`) omits columns whose `CardRole` is `Hidden` from `aria-colcount` and from keyboard movement. While a row is being edited the grid handler returns immediately — arrows belong to the input; Escape cancels the edit and returns focus to the cell. F2, or Enter on an editable column, starts the edit.

## Column menu

`ColumnMenuMode` defaults to `None`, which keeps today's header: the pin toggle (reached with `P`) and the resize handle. `ColumnMenuMode.Menu` replaces that pin toggle with one kebab per data column header. The expander, checkbox and edit-action columns never get one, and the toolbar gains no button.

The menu offers only what the column can do:

| Item | Shown when |
|---|---|
| Sort ascending / Sort descending | `Sortable` |
| Clear sort | `Sortable` and the column is currently sorted |
| Pin left / Pin right, or Unpin | the column is unpinned, or pinned |
| Group by this column | `ShowGrouping`, `Groupable` and not already grouped |
| Hide column | the column picker is shown, `Hideable`, and another visible column would remain |
| Filter | `Filterable` — moves focus to that column's filter control |

`M` and `Alt+ArrowDown` (announced in `aria-keyshortcuts`) open the menu of the header that has focus; `Escape` closes it. The kebab is `tabindex="-1"`, so it is not its own Tab stop. A `HeaderTemplate` stops keydown, so typing into a control inside the header never opens the menu. In grid mode both shortcuts are answered on the active header and do not move the cursor. `ShowColumnMenu="false"` hides the kebab as well as the pin toggle. The menu lives in the header, so `ToolbarMode="ContentOnly"` hides the toolbar but keeps the kebab.

```razor
<TmDataTable TItem="Person" Items="people" ColumnMenuMode="ColumnMenuMode.Menu">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" Sortable="true" />
</TmDataTable>
```

## Surface and filter presentation

`Appearance="DataTableAppearance.Card"` opts `TmDataTable` into its card surface; Plain remains the default. `TmFilterBuilder.Presentation` opts the field picker and editor out of the document flow: `Popover` opens them in a `TmOverlayPanel` anchored to Add filter and stays a popover at every width; `Auto` uses the same panel and lets it become a sheet on a narrow viewport. `Inline` (the default, including PickersPage) is unchanged.

## Export scope

`AllowExportSelected` defaults to `false`, which keeps the format-only export menu. Set it together with `Selectable="true"` and the menu gains a scope section above the formats:

- **All rows** exports every row matching the current filter and sort, paging bypassed — the same set `ExportAsync(format)` has always exported.
- **Selected rows (N)** exports the checked rows, sorted like the current view, with any checked row a later filter hid appended in the order it was checked. While nothing is checked the choice is `aria-disabled` (still focusable) and its description reads "Select rows to export them". Picking a scope leaves the menu open.

`ExportAsync(format, DataTableExportScope.Selected)` exports the selection directly, and `OnExportCompleted` reports which scope ran on `DataTableExportResult.Scope` (default `All`, so existing handlers keep compiling). The first overload of `IDataTableExporter` is unchanged.

```razor
<TmDataTable TItem="Person" Items="people" Selectable="true" ShowExport="true" AllowExportSelected="true">
    <TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" />
</TmDataTable>
```

## Filter summary

`ShowFilterSummary="true"` enables the localized active-filter count and clear control. It is off by default.

## Result preview

`ShowFilterResultPreview="true"` on `TmDataTable` or `TmMultiViewList` makes the inline filter builder show how many records match the filters currently applied, including a valid filter still being edited. It is off by default, and it only renders where the inline builder renders (a view provider and external filter definitions).

The count comes from `ResultCountProvider`, a `Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>>`. Both components build one themselves:

- with a `DataProvider`, the preview calls `GetDataAsync` with `PageSize` 1 and reads `TotalCount`, so a page of rows is never fetched just to count them;
- without one, the in-memory rows are counted with the component's own filter rules, including the search and column filters already applied.

A burst of changes waits 300 ms and collapses into one call. The text is pluralized (`TmFilterBuilder_ResultPreview`: "1 matching record" / "3 matching records", and the Czech forms for 1 / 2–4 / 5+) and announced in a `role="status"` region with `aria-live="polite"`. The region is in the document as soon as the preview and the provider are both set, so the first result is not announced into a region that was just created. While the first count is in flight the region shows a spinner and "Counting…" (`TmFilterBuilder_ResultPreviewLoading`). A later refresh keeps that number on screen and sets `aria-busy` until the new count arrives. A null count or a swallowed exception leaves the region empty, and the builder keeps working.

A standalone `TmFilterBuilder` takes the same `ResultCountProvider` directly, together with `ShowResultPreview`. `ShowResultPreview` alone no longer renders the old static "Preview" placeholder.

```razor
<TmDataTable TItem="Person" Items="people" ViewProvider="views"
             ExternalFilterDefinitions="filters" ShowFilterResultPreview="true">
    <TmDataTableColumn TItem="Person" Title="Name" PropertyName="Name" Field="p => p.Name" />
</TmDataTable>
```

## Inline editing templates

When defining an `EditTemplate` on `TmDataTableColumn`, wrap the edited control in `TmFormField` to provide consistent field layout and labeling:

```razor
<TmDataTableColumn TItem="Person" Title="Name" Field="p => p.Name" Editable="true">
    <EditTemplate Context="person">
        <TmFormField Label="Name">
            <TmTextInput @bind-Value="person.Name" />
        </TmFormField>
    </EditTemplate>
</TmDataTableColumn>
```

## Saved views

`ShowActiveViewName="true"` replaces the generic "Views" toggle label with the active view's name. A long name is truncated (`max-inline-size: 16rem`, ellipsis) and the full name stays in the toggle's `title`.

`AllowSetDefault="true"` adds a "Set {name} as default" action on every view that is not already the default. Choosing it calls `IDataTableViewProvider.SetDefaultViewAsync`, reloads the list, then raises `OnSetDefault` with the persisted view. A failed write is shown in the panel and the current default is left as it was. The default view is marked with `aria-current` and a visually hidden "Default view" (`TmViewManager_DefaultView`); the star itself is `aria-hidden`.

## Edit actions

`EditActionsMode` chooses how an edited row presents Save and Cancel:

- `Icons` (default) keeps the icon buttons in the row.
- `Text` keeps them in the row and shows the localized labels.
- `Footer` moves them into the following row. That cell spans every rendered column — the expander, the checkbox, the data columns and the edit-action column. Flex alignment lives on the inner `.tm-data-table__edit-footer-actions`, not on the `td`, so `colspan` is honored.

Mobile cards always use `Text`, whatever was set.

`GroupHeaderTemplate` replaces the built-in group header (column title, value and count) on `TmDataTable` and `TmMultiViewList`. The context is the `DataGroup<TItem>`. Omit it and the built-in header stays.

## Multi-view list options

`TmMultiViewList` retains its existing defaults: card appearance, icon-only view-switcher labels, no avatar fallback, hidden item count, and non-responsive behavior. Opt into `ViewSwitcherLabels.IconsAndText`, `AvatarFallback.Initials`, `ShowItemCount="true"`, or `Responsive="true"` only where needed. Responsive behavior is controlled by `Responsive`; no additional mobile presentation parameter is provided for `TmColumnPicker` or `TmViewManager`.

`TableCellLayout="TableCellLayout.Stacked"` drops the subtitle column in the table view and renders its value under the title in the same cell (an inner `.tm-mvl-cell-stack`, with `.tm-mvl-cell-secondary` for the subtitle). The `td` stays a table cell. `Inline` (the default) keeps the two columns. `StatusVariantField` maps an item to the status badge variant; omit it and every badge uses the default variant.

`CardActivation="CardActivation.StretchedLink"` renders each card as an inner stretched link instead of a button. It needs `ItemHref`, which returns the URL for an item; without it the card stays a button. `MaxCardColumns` caps the card grid (`--tm-mvl-card-columns-max`); `null` leaves it uncapped.

`GroupHeaderTemplate` replaces the built-in group header on both components. The context is `DataGroup<TItem>` (field name, key, display value, count, items). Omit it and the built-in header stays.

## Appearance gate

`Appearance` is the gate for the card surface, not for the other opt-ins. `DataTableAppearance.Card` adds `tm-data-table-wrapper--card-surface` (radius, shadow, border, surface background). `Plain` adds nothing, and it is the `TmDataTable` default, so an existing table does not gain a card surface by upgrading. `TmMultiViewList.Appearance` defaults to `Card` and keeps the surface it already had. Mobile layout, grid keyboard, the column menu, export scope, the filter summary and the saved-view actions all work on either surface — none of them flips `Appearance` for you.

## Where to try it

The demo pages render every opt-in behind a control that starts at the default, so the rest of the page is unchanged:

- `/data-table` — Appearance, MobileLayout, KeyboardNavigation, ColumnMenuMode, EditActionsMode, AllowExportSelected, ShowFilterSummary, ShowActiveViewName, AllowSetDefault and ShowFilterResultPreview, plus a 24rem-wide container that shows `MobileLayout="Cards"` on a desktop.
- `/multi-view-list` — ViewSwitcherLabels, AvatarFallback, TableCellLayout, CardActivation, ShowItemCount, Responsive, MaxCardColumns, ItemHref, StatusVariantField and GroupHeaderTemplate.

## Mobile sheets

Panels that open from a trigger go through `TmOverlayPanel` (see [overlays.md](overlays.md)). With `MobilePresentation.Auto` the panel becomes a content-height bottom sheet on a phone-sized viewport and stays an anchored popover elsewhere; the popover's size still follows the trigger's container.

| Surface | Mobile presentation |
|---|---|
| `TmViewManager` panel (saved views, Set as default) | `Auto` — a sheet on a mobile viewport, a popover otherwise. |
| `TmDataTable` column picker | `Auto`. |
| `TmDataTable` export menu and column menu | `Auto` when `MobileLayout` is `Scroll` or `Cards`; a popover when it is `None`. |
| `TmMultiViewList` grouping menu | `Auto` when `Responsive="true"`; a popover otherwise. The cards-sheet filter and sort panels are always `Auto`. |
| `TmFilterBuilder` field picker and editor | `Auto` for `Presentation="Auto"`, a popover at every width for `Presentation="Popover"`. `Inline` renders no panel. |

## Parameter reference

Every opt-in parameter added for the redesign, with its default. A default is the value that keeps the pre-redesign markup, so none of these needs to be set for an existing screen to render as before.

### TmDataTable

| Parameter | Type | Default |
|---|---|---|
| `Appearance` | `DataTableAppearance` | `Plain` |
| `MobileLayout` | `DataTableMobileLayout` | `None` |
| `KeyboardNavigation` | `DataTableKeyboardNavigation` | `Rows` |
| `ColumnMenuMode` | `ColumnMenuMode` | `None` |
| `ShowColumnMenu` | `bool` | `true` |
| `EditActionsMode` | `EditActionsMode` | `Icons` |
| `AllowExportSelected` | `bool` | `false` |
| `ShowFilterSummary` | `bool` | `false` |
| `ShowFilterResultPreview` | `bool` | `false` |
| `ShowActiveViewName` | `bool` | `false` |
| `AllowSetDefault` / `OnSetDefault` | `bool` / `EventCallback<DataTableView>` | `false` / — |
| `GroupHeaderTemplate` | `RenderFragment<DataGroup<TItem>>?` | `null` |
| `ToolbarMode` | `DataToolbarMode` | `Full` |
| `CardMode` (alias of `MobileLayout=Cards`) | `bool` | `false` |
| `TmDataTableColumn.MobilePinned` | `bool` | `false` |
| `TmDataTableColumn.CardRole` | `DataTableCardRole` | `Field` |

### TmMultiViewList

| Parameter | Type | Default |
|---|---|---|
| `Appearance` | `DataTableAppearance` | `Card` |
| `ViewSwitcherLabels` | `ViewSwitcherLabels` | `IconsOnly` |
| `AvatarFallback` | `AvatarFallback` | `None` |
| `ShowItemCount` | `bool` | `false` |
| `Responsive` | `bool` | `false` |
| `TableCellLayout` | `TableCellLayout` | `Inline` |
| `CardActivation` / `ItemHref` | `CardActivation` / `Func<TItem, string?>?` | `Button` / `null` |
| `MaxCardColumns` | `int?` | `null` |
| `StatusVariantField` | `Func<TItem, BadgeVariant>?` | `null` |
| `GroupHeaderTemplate` | `RenderFragment<DataGroup<TItem>>?` | `null` |
| `ShowFilterResultPreview` | `bool` | `false` |
| `ToolbarMode` | `DataToolbarMode` | `Full` |

### TmViewManager and TmFilterBuilder

| Parameter | Type | Default |
|---|---|---|
| `TmViewManager.ShowActiveViewName` | `bool` | `false` |
| `TmViewManager.AllowSetDefault` / `OnSetDefault` | `bool` / `EventCallback<DataTableView>` | `false` / — |
| `TmViewManager.Compact` | `bool` | `false` |
| `TmFilterBuilder.Presentation` | `FilterBuilderPresentation` | `Inline` |
| `TmFilterBuilder.ShowResultPreview` | `bool` | `false` |
| `TmFilterBuilder.ResultCountProvider` | `Func<IReadOnlyList<ActiveFilter>, CancellationToken, Task<int?>>?` | `null` |

The `Appearance` gate, grid keys, export scope and result preview are described in the sections above; the parameters with a landed-in-the-backlog origin (`ColumnMenuMode`, `EditActionsMode`, `GroupHeaderTemplate`, `AllowExportSelected` — formerly `ExportSelectedOnly` —, `ShowActiveViewName`, `AllowSetDefault`, `OnSetDefault`, `ShowFilterResultPreview`, `TableCellLayout`, `StatusVariantField`, `CardActivation`) are all opt-in with the defaults above.
