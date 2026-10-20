# Backlog — redesign TmDashboard

Položky posunuté mimo fáze redesignu (výchozí stav: F2, fix kolo fáze 2). Odkazy vedou na
specifikace v tomto adresáři. Pořadí není prioritou; čísla § patří k `f2-ux-spec.md`.

**Stav (F7b, 2.9.1):** načítací skeleton i error stav jsou implementované (viz CHANGELOG),
token `--tm-dashboard-min-height` nahradil literal 600px, ikona dlaždice míchá inkoust
75/25 s `--tm-text-primary` a úzká karta zpřehledňuje hlavičku container query. Zbývá jen
položka „390 px“ níže? — ne, i ta je hotová (container query na kartě). Backlog je prázdný.

## Stavy komponenty

### Loading skeleton + `aria-busy` (F2 UX §5.7) — **Hotovo (F7b, 2.9.1)**

Dokud `_dataLoaded == false`, komponenta krátce zobrazila prázdný stav. Hotovo: dvě karty
`TmSkeleton` (`.tm-dashboard-skeleton-card`, `grid-row: span 4`), `aria-busy` na
`.tm-dashboard-grid-container`, sr-only status `TmDashboard_Loading`; prázdný stav se
vykresluje až po načtení. bUnit: `TmDashboardResilienceTests` (skeleton × 3, guard CSS).

### Error stav: `TmAlert` + „Zkusit znovu“ (F2 UX §5.8) — **Hotovo (F7b, 2.9.1)**

Výjimky `IDashboardProvider` se zachytávají v load i save cestách; nad mřížkou je měkký
danger `TmAlert` (`.tm-dashboard-error`) s lokalizovaným textem a akcí „Zkusit znovu“
(`RetryFailedOperation`). Nové klíče ve čtyřech tabulkách + `MockTmLocalizer`.
bUnit: `Dashboard_LoadError_…`, `Dashboard_SaveError_…` (TmDashboardResilienceTests).

## Tokeny a CSS

### `min-height: 600px` kořene → token (G-6) — **Hotovo (F7b, 2.9.1)**

Token `--tm-dashboard-min-height: 37.5rem` v `tokens.css`; `.tm-dashboard` čte
`var(--tm-dashboard-min-height)`. Demo `.rc-dashboard` záměrně přepisuje na 0 (kompaktní
ukázky), přepis zůstává. bUnit: `Dashboard_Css_MinHeightUsesTheToken_NoLiteralLeft`.

### Kontrast amber dlaždice (F2 UX §5.11) — **Hotovo (F7b, 2.9.1)**

Ikona dlaždice (i karty ve widget selectoru) se kreslí
`color-mix(in srgb, var(--category-color) 75%, var(--tm-text-primary))` — amber na tintu
12 % zůstává čitelný, hue se nemění. bUnit: `Dashboard_Css_CategoryTileInk_…`.

## F4+ (kompaktní edit režim)

### Hlavička karty na 390 px: skupina posunu na druhý řádek (container query) — **Hotovo (F7b, 2.9.1)**

`.tm-widget` je vlastní container (`tm-widget`), `@container tm-widget (width < 400px)`
přesune `.tm-widget-move-group` na samostatný řádek hlavičky (flex-wrap v
`.tm-widget-header-left`), titul dostane plnou šířku. Čistě CSS přes tokeny, markup beze změny.
bUnit: `Dashboard_Css_NarrowCard_WrapsTheMoveGroup_…`.

## F5+ (mobil)

### Přejmenování nedostupné na mobilu — **Hotovo (F7b, 2.9.1)**

Akcí „Přejmenovat“ v mobilní akční liště (Add/Save/Rename na liště, Cancel v More) se otevře
rename dialog (Enter aplikuje, Escape/backdrop ruší). bUnit:
`Dashboard_MobileEditMode_RenameIsReachable_…` (TmDashboardResilienceTests).
