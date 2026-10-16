# Fáze 3 — kontrakt: opt-in ARIA grid

## Stavy

- `KeyboardNavigation = Rows` (default): dnešní chování. Řádek je fokusovatelný, Enter na keydown a Space na keyup volají `OnRowClick`. Žádné roving tabindex na buňkách. Markup `role="grid"` se nemění.
- `KeyboardNavigation = Grid`: právě jedna buňka má `tabindex="0"`, ostatní `-1`. Šipky, Home/End, Ctrl+Home/End, PageUp/PageDown posouvají aktivní buňku. Enter na buňce = `OnRowClick` jednou (guard `!Repeat`). Space na řádku se selekcí přepne výběr a neaktivuje řádek podruhé.
- Group řádek v Grid: šipka vpravo rozbalí, vlevo sbalí. Enter/F2 vstoupí do inline editu, Escape vrátí fokus na buňku.
- MVL tabulka zůstává na Rows. Nemá parametr `KeyboardNavigation`.

## Hranice a idempotence

- Enum `DataTableKeyboardNavigation { Rows, Grid }` v `DataTableEnums.cs`. Parametr s XML dokumentací, default `Rows`.
- Větev `Rows` se nesmí změnit. `TmDataTablePinFocusTests` a `TmDataTableKeyboardSortTests` musí projít beze změny asercí.
- Posun aktivní buňky mimo načtené okno ve virtualizaci zavolá stejné načtení jako scroll a `data-table.js` scrollne buňku do view. Opakovaný posun na stejnou buňku je no-op.
- Nativní tlačítko/odkaz v `CellTemplate` má `@onkeydown:stopPropagation`. Enter na buňce bez interaktivního potomka aktivuje řádek. Enter na buňce s potomkem přesune fokus dovnitř; Escape vrátí fokus na buňku.

## Chyby

- Neznámá klávesa se ignoruje. Žádný nový error code.
- Virtualizace bez JS: posun stavu proběhne, scroll do view se přeskočí (prerender-safe).

## Testy

- `TmDataTableGridKeyboardTests`: Rows beze změny tabindexů; Grid jedna buňka `tabindex=0`; šipky a Home/End; Enter jednou; Space přepne výběr; group expand/collapse; aria-rowcount/rowindex/colindex.
- E2E `DataTableGridKeyboardE2ETests` jen pokud bUnit projde a demo stránka existuje. Screenshoty light/dark nejsou vizuální redesign; fokus ring zůstává stávající.

## MVL

`TmMultiViewList` nemá `KeyboardNavigation` a zůstává u řádkové navigace. Grid se na ni v této fázi neportuje.

## Rozhodnutí

1. Default je `Rows`. `Grid` bude default až v příští major. CHANGELOG Added + Upcoming/Migration s ukázkou `KeyboardNavigation="DataTableKeyboardNavigation.Rows"`.
2. `aria-roledescription` se nepřidává, dokud `role="grid"` stačí. Lokalizovat jen text zkratek pro demo help, pokud demo help tuto fázi mění. Jinak stačí CHANGELOG.
3. Implementace Grid nesmí zvětšit `TmDataTable.razor.cs` o víc než malý dispatch. Nová logika patří do `TmDataTable.GridKeyboard.cs`.
