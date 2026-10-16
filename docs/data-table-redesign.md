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

`KeyboardNavigation="DataTableKeyboardNavigation.Grid"` opts into cell-based roving tabindex navigation. The current default, `Rows`, keeps one tab stop per row. Keep `Rows` explicitly when preserving today's behavior is important; Grid is planned to become the default in the next major release.

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
