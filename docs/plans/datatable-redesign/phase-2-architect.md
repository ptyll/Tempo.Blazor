# Fáze 2 — kontrakt architekta

Fáze `fze-2-a11y-a-klvesnice-ovldacch-prvk-vlna-0`. Žádný vizuální redesign. Nové texty jen přes klíče en/cs/sk/fr + `MockTmLocalizer`.

## Stavy

- View panel: closed → open (`aria-expanded=true`). Escape nebo click-outside zavře a vrátí fokus na toggle.
- View dialog: closed → open na `TmModal`. Escape zavře a vrátí fokus na spouštěč. Overlay click zachovat jako dnes.
- Column picker a MVL group picker: closed → open přes `TmDropdown`/`TmPopover`. Escape a click-outside zavřou a vrátí fokus.
- Group row: expanded/collapsed. Toggle má `aria-expanded` a `aria-label` `TmDataTable_ExpandGroup` / `TmDataTable_CollapseGroup` s `{0}` = název skupiny.
- Add grouping: dropdown seznam `Groupable` sloupců volá existující `AddGroupColumn`. Respektuje `ToolbarMode`.

## Hranice

- Jedna uživatelská akce = jeden stavový přechod. Nativní tlačítko neemuluje Enter/Space podruhé (`docs/keyboard-activation-convention.md`).
- `setIndeterminate(el, bool)` se volá až v `OnAfterRenderAsync`, prerender-safe, z TmDataTable i MVL.
- Resize sloupců a `attachColumnResize` se nemění.

## Idempotence a chyby

- Opakované Escape na zavřeném panelu nic nedělá.
- `AddGroupColumn` na už přidaný sloupec je no-op.
- Chybí-li klíč, test spadne; žádný fallback na anglický hardcoded text.

## Povinné testy

`TmDataTableGroupingAccessibilityTests`, `TmViewManagerAccessibilityTests`, `TmFilterBuilderAccessibilityTests`, `TmColumnPickerTests` (Escape, click-outside, `aria-expanded`), `TmMultiViewListToolbarTests` (radiogroup, group picker Escape, select-all label), indeterminate v `OnAfterRenderAsync`, inline-edit `role=alert`.

## Rozhodnutí

1. Položky pohledů jsou `<button class="tm-view-item">`. Edit/delete zůstanou inline, bez `⋮` menu.
2. Dialog pohledů jde na `TmModal`. Vnitřní třídy `tm-view-modal` a `tm-view-modal-footer` zůstanou.
3. Čtyři ad-hoc dropdowny (view panel, column picker, MVL group picker, filter editor popup) přejdou na overlay jádra. Vlastní `.tm-overlay` ve ViewManageru zmizí.
4. Glyfy ▼/▶/× nahradí `TmIcon`. Textový obsah se tím mění — patří do Breaking.
5. Nové klíče: `TmDataTable_AddGrouping`, `TmDataTable_ExpandGroup`, `TmDataTable_CollapseGroup`, `TmDataTable_RemoveGrouping`.
