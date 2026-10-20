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

`Appearance="DataTableAppearance.Card"` opts `TmDataTable` into its card surface; Plain remains the default. `TmFilterBuilder.Presentation="FilterBuilderPresentation.Popover"` opts the filter editor into an anchored popover; Inline remains the default.

## Filter summary

`ShowFilterSummary="true"` enables the localized active-filter count and clear control. It is off by default.

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

## Multi-view list options

`TmMultiViewList` retains its existing defaults: card appearance, icon-only view-switcher labels, no avatar fallback, hidden item count, and non-responsive behavior. Opt into `ViewSwitcherLabels.IconsAndText`, `AvatarFallback.Initials`, `ShowItemCount="true"`, or `Responsive="true"` only where needed. Responsive behavior is controlled by `Responsive`; no additional mobile presentation parameter is provided for `TmColumnPicker` or `TmViewManager`.
