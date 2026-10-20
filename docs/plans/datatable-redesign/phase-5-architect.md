# Fáze 5 — kontrakt TmMultiViewList

Závisí na fázi 4. Nezačínat commit, dokud fáze 4 nemá screenshoty.

## Stavy

- `ViewSwitcherLabels`: `IconsOnly` (default, dnešní markup) | `IconsAndText`.
- `Appearance`: sdílený `DataTableAppearance`. Default MVL je `Card` (dnešní rámeček). `Plain` rámeček i padding sundá.
- `AvatarFallback`: `None` (default, bez avataru když chybí URL) | `Initials` z titulku.
- `ShowItemCount`: default false. `true` renderuje `TmMvl_ItemCount` s `{0}`.

## Hranice

- Přepínač pohledů zůstává `role=radiogroup` z fáze 2. Texty jen při `IconsAndText`.
- Grid karet: nejvýš 3 sloupce přes `--tm-mvl-card-min`. Ve `Virtualized` režimu vynutit 1 sloupec.
- App shell, řádkové `⋯` a pole oddělení se neimplementují.

## Testy

`TmMultiViewListToolbarTests`, `TmMultiViewListAppearanceTests`, `TmMultiViewListCardTests`.
Výchozí `IconsOnly`, `Card` a `None` nesmí změnit dnešní aserce.
