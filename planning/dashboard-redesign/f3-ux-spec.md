# F3 — UX specifikace: responzivní mřížka a hlavička na tabletu a mobilu

Plán: *Tempo.Blazor — Redesign: TmDashboard* (`d254e3dd-5198-4c2d-8c35-08df2362aa32`), fáze
`fze-3-responzivn-mka-a-hlavika-na-tabletu-a-mobilu-navazuje-na-pilot-f1`. Výchozí stav: F1 + F2
commitnuté (přepínač `TmOverlayPanel`, chrome karty, `Appearance`, `--tm-w-*` proměnné).

Reference (jediný zdroj vizuálu pro F3):

- `output/imagegen/tempo-reimagined/TmDashboard/dashboard--tablet.png` — hlavička ve 2 řádcích
  (přepínač nahoře, pod ním titul + podtitul vlevo a „Upravit“ vpravo), karty 2 × 2.
- `output/imagegen/tempo-reimagined/TmDashboard/dashboard--mobile.png` — titul/podtitul, přepínač
  a „Upravit“ jako velká tlačítka, karty v 1 sloupci (ikona + titul + chevron).
  **Spodní lišta „Přidat widget / Přetáhnout / Více“ ve view režimu se NEdělá** (vymyšleno
  generátorem; edit režim už má `TmMobileActionBar` z F5 základu — beze změny).

Rozsah F3: rozvržení **tablet** (`data-layout="tablet"`) a **mobil** (`data-layout="mobile"`) —
pořadí widgetů, uvolnění místa po sbalení, výšky, hlavička, dotykové cíle, horizontální přetečení.
Desktop se mění jen tam, kde to vyžaduje pořadí v DOM (§4) a sbalení (§5). **Mechanismus F1 se
nepřepisuje** (kontejner, `TmLayoutObserver`, `--tm-w-*`, `data-layout`).

Konvence: identifikátory anglicky, CSS výhradně `--tm-*` tokeny nebo komponentové proměnné
(žádný hex/`rgba()`), **žádné `!important`** pro umístění widgetů, nové třídy musí mít pravidlo v
`_dashboard.css` (`MarkupClassCoverageTests`), BEM modifikátory `tm-dashboard-toolbar--*`.

---

## 0. Brána a inventura F1 (co už existuje — neimplementovat znovu)

Brána: plán základu `faf503eb-…` má všech 10 fází **Completed**, včetně
`fze-2-f1-konvence-kontejneru-dotyku-a-tmlayoutmode` („Fáze 2: F1 — Konvence kontejneru, dotyku a
TmLayoutMode“). Evidence = toto ID/název (ověřeno přes `plan_get_overview`).

| Mechanismus | Kde | Stav |
|---|---|---|
| Režim rozvržení | `TmLayoutObserver.tm-dashboard-layout` (`IsContainer=false`), `TmDashboard.razor` ř. 18–22; prahy `TmLayoutBreakpoints.Default` = **640 / 1024** měřené na kořeni dashboardu | hotovo |
| `data-layout` + `tm-dashboard--{mode}` na `.tm-dashboard` | `TmDashboard.razor` ř. 20–21 | hotovo |
| Parametry `LayoutMode`, `InitialMode`, `ResolvedLayoutChanged` | `TmDashboard.razor.cs` ř. 107–126 | hotovo |
| Kontejner `container-type: inline-size; container-name: tm-dashboard` na `.tm-dashboard-grid-container` | `_dashboard.css` ř. 219–227 | hotovo |
| Umístění přes proměnné `--tm-w-x/-span/-span-md/-y/-rows/-order` (žádný inline `grid-column`) | `GetWidgetStyles` `TmDashboard.razor.cs` ř. 599–609; CSS ř. 240–244 | hotovo |
| Pořadí `BuildOrder()` = `(Y, X, InstanceId)` → `--tm-w-order` + CSS `order` | `.cs` ř. 612–617 | hotovo **jen vizuálně** (DOM = pořadí přidání, viz §4) |
| Desktop 12 sloupců | CSS ř. 229–238 | hotovo |
| Tablet 6 sloupců, `@container tm-dashboard (width < 768px)` → 2 sloupce, `tm-widget--wide` = celý řádek | CSS ř. 1023–1048, `GetWidgetClasses` ř. 593 | hotovo |
| Mobil 1 sloupec, `grid-auto-rows: auto`, `min-height` z `--tm-w-rows × --row-height` | CSS ř. 1050–1060 | hotovo (bez mezer, viz §3.3) |
| Drag/resize jen desktop (`CanDrag`/`CanResize`) | `.cs` ř. 646, 654 | hotovo |
| Edit lišta na mobilu `TmMobileActionBar`, skrytí `.tm-dashboard-toolbar-right` v mobilním edit režimu | `razor` ř. 414–419, CSS ř. 183–185 | hotovo |
| `@media (pointer: coarse)`: `.tm-widget-btn`, `.tm-dashboard-menu-action`, roh `se` ≥ `--tm-touch-target`, ostatní úchyty skryté | CSS ř. 1064–1094 | hotovo částečně (chybí přepínač, `.tm-btn` hlavičky, položky menu — §7) |
| CSS testy kontejneru | `TmDashboardTests.cs` ř. 251–317 (komentář ř. 246–249 o `!important` je **zastaralý** — přepsat) | hotovo |

Chybí (= obsah F3): DOM pořadí, sbalení/minimalizace bez `!important`, výšky tabletu, hlavička
tablet/mobil, dotykové cíle hlavičky a menu, kurzor `grab` mimo desktop, přetečení na 320 px,
`role="region"` (backlog).

---

## 1. Breakpointy — co se vykreslí při jaké šířce

Šířka **W** = šířka `.tm-dashboard-layout` (kořen, ne viewport). Mřížka = W − 2 × padding kořene.

| W kořene | `data-layout` | Padding kořene | Šířka mřížky | Sloupce | Umístění |
|---|---|---|---|---|---|
| ≥ 1024 | `desktop` | `--tm-space-6` (24) | ≥ 976 | 12 (`--grid-columns`) | explicitní `X/Y` (`--tm-w-x/-y`) |
| 816 – 1023 | `tablet` | `--tm-space-6` | 768 – 975 | 6 | auto-placement v pořadí DOM, `span --tm-w-span-md` |
| 640 – 815 | `tablet` | `--tm-space-6` | 592 – 767 | 2 (`@container < 768px`) | auto-placement, `span 1` / `--wide` celý řádek |
| < 640 | `mobile` | `--tm-space-3` (12) | < 616 | 1 | auto-placement, celý řádek |

Pozn.: přechod 6 → 2 sloupce leží na **816 px kořene** (768 px mřížky), ne na 768 px kořene —
container query měří kontejner mřížky. Ponechat (rozhodnutí M-8), jen zapsat do docs/CHANGELOG.

Referenční šířky E2E (viewport, demo shell zabere část šířky — test vždy loguje `data-layout` a
šířku kontejneru): **1440** (desktop), **1024 touch** (tablet), **768** (tablet 2 sl.),
**390** a **320** (mobil); navíc `/responsive-conventions` úzký/široký hostitel.

### Pravidlo šířek na tabletu (beze změny výpočtu, jen potvrzení)

| `Width` (z 12) | 6 sloupců (`--tm-w-span-md`) | 2 sloupce |
|---|---|---|
| > 6 (`tm-widget--wide`) | 6 = celý řádek | `1 / -1` celý řádek |
| 3 – 6 | 3 = polovina | 1 = polovina |
| 1 – 2 | 2 = třetina | 1 = polovina |

Pro jiný `Grid.Columns` platí poměry `> C/2`, `≥ C/4` (dnešní `GetWidgetStyles` ř. 607).

**Žádné `grid-auto-flow: dense`** — díry v 6sloupcovém režimu (např. 3 + 6 + 3) jsou přijatelné;
`dense` by rozbilo shodu vizuálního a DOM pořadí (WCAG 1.3.2 / 2.4.3).

---

## 2. Hlavička dashboardu

### 2.1 Modifikátory toolbaru (počítá C#, ne `:has()`)

Na `div.tm-dashboard-toolbar` přidat:

| Třída | Podmínka (jen **view** režim) |
|---|---|
| `tm-dashboard-toolbar--with-heading` | vykreslí se **viditelný** h2: `ShowTitle && !(Title is null && DashboardProvider is not null)` |
| `tm-dashboard-toolbar--with-actions` | `HeaderActions is not null` |

V edit režimu se modifikátory nevykreslí (edit hlavička = flex-wrap jako desktop, §2.4).
Bez viditelného h2 (h2 je `tm-sr-only`, nebo `ShowTitle=false`) zůstává hlavička na tabletu i mobilu
**jednořádková flex** (přepínač vlevo, akce vpravo) — skládání do řádků má smysl jen s viditelným
titulem; jinak by vznikl prázdný řádek s `row-gap`.

### 2.2 Tablet — view režim (`[data-layout="tablet"]:not(.tm-dashboard--edit) .tm-dashboard-toolbar--with-heading`)

```
┌ řádek 1 ───────────────────────────────────────────────────────────┐
│ [HeaderStart]  [▦ Název nástěnky        ⌄]                         │
├ řádek 2 ───────────────────────────────────────────────────────────┤
│ h2 Titul                                       [HeaderActions] [✎ Upravit] │
│ p  Podtitul                                                        │
└────────────────────────────────────────────────────────────────────┘
```

| Prvek | CSS |
|---|---|
| `.tm-dashboard-toolbar` | `display: grid; grid-template-columns: auto minmax(0, 1fr) auto; column-gap: 0; row-gap: var(--tm-space-4); align-items: center` |
| `.tm-dashboard-toolbar-left` | `display: contents` (prostý `div` bez role — a11y bezpečné) |
| `.tm-dashboard-header-start` | `grid-row: 1; grid-column: 1; margin-inline-end: var(--tm-space-3)` |
| `.tm-dashboard-views` | `grid-row: 1; grid-column: 2 / -1; justify-self: start` |
| `.tm-dashboard-heading` | `grid-row: 2; grid-column: 1 / 3; min-width: 0` |
| `.tm-dashboard-toolbar-right` | `grid-row: 2; grid-column: 3; margin-inline-start: var(--tm-space-4)` (zrušit `auto`) |

- Mezery: `column-gap: 0` + marginy místo gapu, aby chybějící `HeaderStart` nevytvořil 16px odsazení.
- Titul `--tm-font-size-2xl` (beze změny), podtitul `--tm-font-size-sm`.
- Přepínač si drží F2 šířku `14rem–20rem` (vejde se, mřížka ≥ 592 px).
- Bez `--with-heading`: desktopová jednořádková flex hlavička (F2 §2.2) — beze změny.

### 2.3 Mobil — view režim (`[data-layout="mobile"]:not(.tm-dashboard--edit)`)

S viditelným titulem (`--with-heading`):

```
řádek 1:  [HeaderStart] h2 Titul / p Podtitul            (celá šířka)
řádek 2:  [▦ Název nástěnky …        ⌄]  [✎ Upravit]       (přepínač 1fr, Upravit auto)
          — s --with-actions: —
řádek 2:  [▦ Název nástěnky …                         ⌄]  (celá šířka)
řádek 3:                     [HeaderActions] [✎ Upravit]   (zarovnané vpravo, flex-wrap)
```

| Prvek | CSS |
|---|---|
| `.tm-dashboard-toolbar` (`--with-heading`) | `display: grid; grid-template-columns: auto minmax(0, 1fr) auto; column-gap: 0; row-gap: var(--tm-space-3); align-items: center` |
| `.tm-dashboard-toolbar-left` | `display: contents` |
| `.tm-dashboard-header-start` | `grid-row: 1; grid-column: 1; margin-inline-end: var(--tm-space-2)` |
| `.tm-dashboard-heading` | `grid-row: 1; grid-column: 2 / -1` |
| `.tm-dashboard-views` | `grid-row: 2; grid-column: 1 / 3; min-width: 0` |
| `.tm-dashboard-switcher` | `inline-size: 100%; min-inline-size: 0; max-inline-size: none` (**nutné** — F2 `min-inline-size: 14rem` = 224 px by na 320 px s „Upravit“ přetekl) |
| `.tm-dashboard-toolbar-right` | `grid-row: 2; grid-column: 3; margin-inline-start: var(--tm-space-2); flex-basis: auto` (zrušit dnešní `flex-basis: 100%` ř. 176–178 pro tento případ) |
| `--with-actions` → `.tm-dashboard-views` | `grid-column: 1 / -1` |
| `--with-actions` → `.tm-dashboard-toolbar-right` | `grid-row: 3; grid-column: 1 / -1; justify-content: flex-end; flex-wrap: wrap; margin-inline-start: 0` |
| `.tm-dashboard-title` (mobil) | `--tm-font-size-xl` (20 px) místo `2xl` — reference ~20 px, delší názvy na 320 px |

Bez viditelného titulu (h2 `tm-sr-only` / `ShowTitle=false`): toolbar zůstává flex, `toolbar-left`
`flex: 1 1 0; min-width: 0`, přepínač `inline-size: 100%; min-inline-size: 0`, `toolbar-right`
`flex: 0 0 auto` **bez** `flex-basis: 100%` → jeden řádek „přepínač + Upravit“. S `HeaderActions`
smí `toolbar-right` zalomit (`flex-wrap: wrap` na toolbaru už je).

- „Upravit“ zůstává `tm-btn-primary` (reference má outlined — vědomá odchylka, M-5).
- Výška přepínače a tlačítek hlavičky v mobilním layoutu ≥ `--tm-touch-target` vždy (§7).
- `HeaderActions` od hostitele: hostitel odpovídá za šířku; komponenta jen zalamuje (`flex-wrap`).

### 2.4 Edit režim na tabletu a mobilu

- **Tablet edit**: desktopová flex-wrap hlavička z F2 §2.4 beze změny (h2 + tužka + badge vlevo,
  Přidat / Uložit / Zrušit vpravo; při nedostatku místa se pravá skupina zalomí — E2E
  `EditMode_KeepsTheToolbarInsideTheDashboard` už hlídá).
- **Mobil edit**: `toolbar-right` skrytý (F5, beze změny), akce v `TmMobileActionBar`. Vlevo h2
  (`--tm-font-size-xl`) + `span.tm-dashboard-name` + tužka, badge se zalomí pod. Inline formulář
  přejmenování: `min-width` vstupu přesunout z viewport `@media (width < 768px)` (ř. 1015–1017) pod
  `[data-layout="mobile"]` a nastavit `flex: 1 1 0; min-inline-size: 0` (bez pevných 150 px →
  žádné přetečení na 320 px).

---

## 3. Mřížka a karty

### 3.1 Desktop — beze změny umístění

Explicitní `grid-column/grid-row` z `--tm-w-*`. Mění se jen DOM pořadí (§4) a sbalení (§5).

### 3.2 Tablet — výšky (změna)

Dnes: `grid-auto-rows: var(--row-height)` + `grid-row: auto / span var(--tm-w-rows)` → pevná výška,
obsah užší karty se ořízne/scrolluje uvnitř. Nově (doporučení M-3) stejný model jako mobil:

```css
.tm-dashboard[data-layout="tablet"] .tm-dashboard-grid { grid-auto-rows: auto; }
.tm-dashboard[data-layout="tablet"] .tm-dashboard-grid .tm-widget {
  grid-column: auto / span var(--tm-w-span-md, 3);   /* beze změny */
  grid-row: auto;
  min-height: var(--tm-w-min-block);
}
```

- Karty v jednom řádku se natáhnou na nejvyšší (`align-items: stretch` default) → stejné výšky
  ve dvojici jako v referenci; obsah se nikdy neořízne.
- Komponentová proměnná na `.tm-dashboard-grid .tm-widget`:
  `--tm-w-min-block: calc(var(--tm-w-rows, 1) * var(--row-height) + (var(--tm-w-rows, 1) - 1) * var(--grid-gap));`
  = přesně výška slotu na desktopu (včetně mezer).

### 3.3 Mobil

- 1 sloupec, `grid-column: 1 / -1; grid-row: auto` (beze změny), `min-height: var(--tm-w-min-block)`
  (sjednocení s tabletem; dnes bez mezer — rozdíl `(H−1) × 16 px`).
- Karta: hlavička `padding: var(--tm-space-4)` (místo `space-4 space-5`), obsah
  `padding: 0 var(--tm-space-4) var(--tm-space-4)`. Dlaždice ikony 40 px, titul `--tm-font-size-lg`,
  chevron vpravo — vše z F2 beze změny.
- Mezera hlavička → první karta: `gap` kořene na mobilu `--tm-space-4` (dnes `space-6`).
- `.tm-widget-host-actions` na úzké kartě: nezalamuje se, `min-width: 0` (už je); titul má
  přednost díky ellipsis. Obsah slotu je odpovědnost hostitele (zapsat do docs) — E2E na 320 px
  hlídá, že `.tm-widget-header` nepřetéká (`scrollWidth <= clientWidth`).

### 3.4 Horizontální přetečení (DoD: žádný h-scroll do 320 px)

Zdroje rizika a opatření:

| Zdroj | Opatření |
|---|---|
| Přepínač `min-inline-size: 14rem` | mobil `min-inline-size: 0` (§2.3) |
| `.tm-dashboard-name-input { min-width: 150px }` (viewport media) | §2.4 |
| `.tm-dashboard-menu { min-width: 18rem }` (288 px) | vejde se do 320 px; overlay.js clampuje — jen ověřit E2E |
| Obsah widgetu (graf) | `.tm-widget-content { overflow: auto }` už je; `.tm-widget { min-width: 0 }` přidat (grid item default `min-width: auto`) |
| `.tm-dashboard-grid-container { overflow-x: auto }` | ponechat (desktop pojistka); E2E asserty `scrollWidth == clientWidth` na `document.documentElement` **i** na `.tm-dashboard-grid-container` |

---

## 4. Pořadí widgetů = (Y, X) — DOM i vizuál

Dnes: CSS `order: var(--tm-w-order)` seřadí vizuálně, ale DOM (`@foreach (var widget in _widgets)`,
`razor` ř. 266) je v pořadí přidání → Tab a čtečka jdou jinak než oko (WCAG 2.4.3, 1.3.2) **ve všech
režimech**. CSS `order` fokusové pořadí nemění.

Specifikace (doporučení M-2):

- Render v seřazeném pořadí: `@foreach (var widget in OrderedWidgets)`, kde řazení je tentýž klíč
  jako `BuildOrder()` — `Y`, pak `X`, pak `InstanceId` (ordinal). Jeden výpočet za render; slovník
  `order` i seřazený seznam z jednoho zdroje.
- `@key="widget.InstanceId"` zůstává → Blazor DOM uzly přesouvá (fokus i stav obsahu se zachová).
- Platí na **desktopu i v kompaktních režimech** (na desktopu se vizuál nezmění — explicitní umístění).
- `--tm-w-order` dál emitovat (existující testy, pojistka pro hostitelské CSS); hodnota = index
  v seřazeném seznamu.
- Během aktivního tažení/resize (`_isDragging`, `_resizeDirection` ≠ null) se pořadí nepřepočítává
  až do dropu (žádné přesuny DOM pod prstem/myší); po `OnDragEnd`/`OnGridPositionChanged` se seřadí.
- `.tm-widget-drop-preview` zůstává za widgety (desktop only).

---

## 5. Sbalení a minimalizace — uvolnění místa bez `!important`

Model (`WidgetInstance.Height`) se **nemění** — rozbalení vrací původní výšku. `--tm-w-rows` zůstává
= `Height` (čte ho `dashboard.js` ř. 303–304 pro náhled dropu).

| Režim | Sbalený (`tm-widget--collapsed`) / minimalizovaný (`tm-widget--minimized`) widget |
|---|---|
| Desktop | slot `Y / span H` zůstává rezervovaný (žádné přeskupení ostatních), karta se zmenší na hlavičku: `align-self: start; min-height: auto` (doporučení M-1) |
| Tablet | `grid-row: auto; min-height: auto; align-self: start` → karta = výška hlavičky; řádek drží jen soused (pokud nějaký je), jinak řádek splaskne |
| Mobil | `grid-row: auto; min-height: auto` → stack se zavře, další karta hned pod hlavičkou |

CSS (selektory se stejnou specifitou jako pravidla režimu, umístěné **za** nimi — vyhrají pořadím):

```css
.tm-dashboard-grid .tm-widget--collapsed,
.tm-dashboard-grid .tm-widget--minimized { align-self: start; min-height: auto; }

.tm-dashboard[data-layout="tablet"] .tm-dashboard-grid .tm-widget--collapsed,
.tm-dashboard[data-layout="tablet"] .tm-dashboard-grid .tm-widget--minimized,
.tm-dashboard[data-layout="mobile"] .tm-dashboard-grid .tm-widget--collapsed,
.tm-dashboard[data-layout="mobile"] .tm-dashboard-grid .tm-widget--minimized {
  grid-row: auto;
  min-height: auto;
  align-self: start;
}
```

- **Odstranit** `.tm-widget--minimized { grid-row: span 1 !important; }` (ř. 310–312) — kromě
  `!important` je to i bug: `grid-row: span 1` resetuje `grid-row-start` na `auto`, takže
  minimalizovaný widget na desktopu ztratí svou pozici `Y`. `IsMinimized` = layoutově alias
  sbalení (M-7). Obsah minimalizovaného widgetu se dál renderuje (dnešní chování), jen se mění
  výška slotu — zapsat do CHANGELOG.
- `.tm-widget--collapsed { min-height: auto }` (ř. 306–308) sloučit do pravidla výše.
- Přechod výšky neanimovat (layout skok je okamžitý; `prefers-reduced-motion` netřeba řešit).
- Fokus po sbalení zůstává na chevronu (prvek se nepřemontuje díky `@key`).

---

## 6. Tokeny (mapování a mezery)

| Účel | Token / hodnota |
|---|---|
| Padding kořene desktop/tablet / mobil | `--tm-space-6` / `--tm-space-3` (beze změny) |
| Gap kořene mobil | `--tm-space-4` (nově) |
| Row-gap hlavičky tablet / mobil | `--tm-space-4` / `--tm-space-3` |
| Odsazení HeaderStart, toolbar-right | `--tm-space-3`, `--tm-space-2`, `--tm-space-4` (margin) |
| Titul tablet / mobil | `--tm-font-size-2xl` / `--tm-font-size-xl`; `--tm-font-weight-bold` |
| Padding karty mobil | `--tm-space-4` |
| Dotykový cíl | `--tm-touch-target` (2.75rem = 44 px) |
| Výška přepínače/tlačítek (jemný ukazatel) | `--tm-input-height-md` (38 px, F2) |
| Výška slotu | komponentová `--tm-w-min-block` (z `--tm-w-rows`, `--row-height`, `--grid-gap`) |
| Fokus | `--tm-focus-ring` (F2, beze změny) |

Mezery (gaps):

- **TG-1** Žádný token pro breakpointy v `@container` — literály `768px` (pravidlo
  `breakpoints.css` / `TmBreakpoints`), beze změny.
- **TG-2** `--row-height`/`--grid-gap` nejsou `--tm-*` (inline proměnné z `GridConfig`) a mají
  literální fallbacky `60px/16px` — pre-existing; nové pravidlo `--tm-w-min-block` je použije **bez**
  dalších literálů.
- **TG-3** Šířka select-like triggeru (F2 G-3) — na mobilu se min/max ruší, žádný token netřeba.
- Nové lokalizační klíče: **žádné** (region bere jméno z h2 / `HeaderTitle`, §8).

---

## 7. Dotykové cíle

| Prvek | Pravidlo |
|---|---|
| `.tm-widget-btn` | `@media (pointer: coarse)` už má ≥ 44 (ř. 1080–1085); **přidat** totéž pro `[data-layout="mobile"]` bez ohledu na ukazatel |
| `.tm-dashboard-switcher` | `(pointer: coarse)` **a** `[data-layout="mobile"]`: `min-block-size: var(--tm-touch-target)` (`height` 38 → min 44 vyhraje) |
| `.tm-dashboard-toolbar .tm-btn` (Upravit, Přidat/Uložit/Zrušit, tužka, apply/cancel přejmenování, tlačítka `HeaderActions`) | `(pointer: coarse)` **a** `[data-layout="mobile"]`: `min-block-size: var(--tm-touch-target)`; ikonová `min-inline-size` totéž (`.tm-btn-icon` už řeší `breakpoints.css`) |
| `.tm-dashboard-menu-item__select`, `.tm-dashboard-menu-item--action` | `(pointer: coarse)`: `min-block-size: var(--tm-touch-target)` |
| `.tm-dashboard-menu-action` | už je (ř. 1081) |
| `.tm-dashboard-empty .tm-btn` | `(pointer: coarse)` / mobil: `min-block-size: var(--tm-touch-target)` |
| Resize úchyty | renderují se jen na desktopu; na coarse jen `se` 44 × 44 (už je) — beze změny |
| Drag handle | jen desktop (F4 z něj udělá button) — beze změny |

- Mezi sousedními cíli ≥ `--tm-space-2` (8 px) — přepínač ↔ Upravit, chevron ↔ host akce.
- Kurzor `grab` (ř. 324–330) omezit na `.tm-dashboard--edit[data-layout="desktop"] .tm-widget-header`
  — na tabletu/mobilu tažení neexistuje.
- Žádná akce se neodhaluje jen hoverem (F2 §5.2), takže nic dalšího pro `(hover: none)`.
- Utilitu `.tm-touch-target` do markup **nepřidávat** — pravidla patří do `_dashboard.css`
  (komponentová pravidla podle konvence `breakpoints.css` ř. 65–67).

---

## 8. Fokus, DOM pořadí a ARIA

### 8.1 Pořadí Tab — view (všechny režimy; vizuální pořadí na tabletu viz pozn.)

1. `HeaderStart` (hostitel) 2. přepínač 3. `HeaderActions` 4. „Upravit“ 5. widgety **v pořadí (Y, X)**:
   `WidgetHeaderActions` → chevron → obsah.

Tablet/mobil přeskupují hlavičku jen vizuálně (grid areas), DOM se nemění. Fokusové pořadí
odpovídá vizuálnímu čtení (řádek 1 → řádek 2, zleva doprava), protože h2 není fokusovatelný a
HeaderStart je na tabletu ve stejném řádku jako přepínač a vlevo od něj. Čtečka přečte h2 před
přepínačem, i když je na tabletu vizuálně pod ním — přijatelné (nejde o významovou sekvenci).

### 8.2 Edit

Desktop/tablet beze změny (F2 §6.2). Mobil: h2 + tužka → widgety (Odebrat) → `TmMobileActionBar`
(beze změny z F5).

### 8.3 ARIA

| Prvek | Atributy |
|---|---|
| Kořen `.tm-dashboard` | `role="region"`; když se h2 renderuje: `aria-labelledby="@_titleElementId"` (i když je `tm-sr-only`); když ne (`ShowTitle=false` ve view, nebo `ShowHeader=false`): `aria-label="@HeaderTitle"`. Vždy pojmenovaný region (více dashboardů na stránce = rozlišitelné). Backlog položka F3. |
| Mřížka | žádná role (`list` by vyžadoval `listitem` obaly a nic nepřidává — h3 dávají strukturu) |
| Widget | beze změny (h3 `id`, chevron `aria-expanded` + `aria-controls` + `aria-describedby`) |
| Toolbar `display: contents` | obaly jsou role-less `div` — žádný dopad na strom přístupnosti |

---

## 9. Stavy

| Stav | Tablet | Mobil |
|---|---|---|
| Default view | §2.2, karty 2 (nebo 6) sloupce, výšky §3.2 | §2.3, 1 sloupec |
| Hover | jako F2 (žádný hover karty ve view) | — |
| Focus-visible | `--tm-focus-ring` na přepínači, tlačítkách, chevronu (F2) | totéž |
| Edit | §2.4; bez mřížky v pozadí, bez drag/resize; jen „Odebrat“ v kartě | §2.4 + `TmMobileActionBar` |
| Sbaleno | §5 | §5 |
| Prázdný | `.tm-dashboard-empty` beze změny, CTA ≥ 44 na coarse | totéž, `padding` `--tm-space-8` místo `space-12` (320 px) |
| Otevřený přepínač | menu `Popover`, `min-width 18rem`, `MatchAnchorWidth` | totéž; ověřit, že se vejde na 320 px |
| Dark / indigo | jen tokeny → automaticky; screenshot 390 dark | totéž |
| Loading / error | backlog (F2), mimo F3 | — |

---

## 10. Komponenty k použití

| Potřeba | Komponenta |
|---|---|
| Režim rozvržení | `TmLayoutObserver` (stávající, beze změny) |
| Edit akce na mobilu | `TmMobileActionBar` (stávající) |
| Menu přepínače | `TmOverlayPanel` (stávající) |
| Ikony | `TmIcon` (stávající) |
| Nic nového se nezavádí. | |

---

## 11. Ověření (vazba na úkoly fáze)

bUnit (RED, vynucený `LayoutMode`):

- `Dashboard_WidgetOrder_FollowsRowThenColumn` — widgety přidané v pořadí C(4,0), A(0,0), B(0,4)
  (X,Y) → `.tm-widget` v DOM = A, B, C (`data-instance-id`), `--tm-w-order` 1,2,3; pro Desktop i Mobile.
- `Dashboard_CollapsedWidget_ReleasesRows` — Mobile/Tablet: po kliku na chevron má widget
  `tm-widget--collapsed`, obsah nevykreslen, `--tm-w-rows` = původní `Height`; CSS test: pravidlo
  `[data-layout="mobile"|"tablet"] … .tm-widget--collapsed` obsahuje `grid-row: auto` a
  `min-height: auto`.
- `Dashboard_Mobile_HeaderStacksTitleAboveActions` — Mobile + `Title` + provider: toolbar má
  `tm-dashboard-toolbar--with-heading`; CSS test: v mobilním bloku `.tm-dashboard-heading`
  `grid-row: 1`, `.tm-dashboard-views` `grid-row: 2`.
- `Dashboard_Tablet_WideWidgetsSpanFullWidth` — Width 8 → `tm-widget--wide` + `--tm-w-span-md: 6`;
  Width 6 → `3`; Width 2 → `2`.
- Doporučené navíc: `Dashboard_Root_IsRegionLabelledByTitle`,
  `Dashboard_TitleHidden_ToolbarHasNoHeadingModifier`, `Dashboard_HeaderActions_AddsActionsModifier`.

CSS testy (`DashboardCss()`):

- `_dashboard.css` neobsahuje `!important` v pravidlech `.tm-widget*` (pozor: ř. 621–622 jsou
  selektor `TmWidgetSelector` — test cílit na `grid-row`/`grid-column` deklarace, nebo `!important`
  odstranit i tam).
- `@media (pointer: coarse)` blok obsahuje `.tm-widget-btn`, `.tm-dashboard-switcher`,
  `.tm-dashboard-toolbar .tm-btn` s `var(--tm-touch-target)`.
- `cursor: grab` jen pod `[data-layout="desktop"]`.
- Přepsat zastaralý komentář `TmDashboardTests.cs` ř. 246–249.

E2E + screenshoty (`__screenshots__/dashboard/f3/`), view i edit:

| Šířka | Asserty |
|---|---|
| 1440 | desktop beze změny (regrese F2); DOM pořadí = (Y,X) |
| 1024 touch | `data-layout` zalogovat; switcher, Upravit, chevron ≥ 44 × 44; žádný `.tm-widget-resize-*` |
| 768 | 2 sloupce; hlavička 2 řádky (přepínač `top` < h2 `top`, Upravit `top` ≈ h2 `top`) |
| 390 | 1 sloupec; `top` widgetů rostoucí v pořadí (Y,X); `scrollWidth == clientWidth`; h2 nad přepínačem; přepínač a Upravit ve stejném řádku a ≥ 44 px; sbalený widget: výška karty ≈ výška hlavičky a další karta posunutá nahoru o (původní výška − nová) |
| 320 | totéž jako 390 + fr locale (nejdelší texty) + otevřené menu přepínače uvnitř viewportu |
| hostitelé `/responsive-conventions` | úzký hostitel v 1440 viewportu = mobilní hlavička a 1 sloupec |

Checklist UX review proti `dashboard--tablet.png` / `dashboard--mobile.png`:

1. Tablet: přepínač samostatně nahoře, titul + podtitul vlevo, „Upravit“ vpravo na úrovni titulu.
2. Tablet: karty 2 × 2, stejně vysoké v řádku, nic oříznuté.
3. Mobil: titul nahoře, přepínač + Upravit jako velká tlačítka, karty přes celou šířku.
4. Mobil: žádná spodní lišta ve view režimu; v edit režimu `TmMobileActionBar`.
5. Žádný horizontální scroll na 320 px (fr).
6. Sbalená karta neuvolňuje díru na mobilu/tabletu; na desktopu slot zůstává.
7. Fokusové pořadí = vizuální (Tab z hlavičky do první karty vlevo nahoře).

CHANGELOG „Breaking / Migration“ (před/po): DOM pořadí widgetů (selektory `:nth-child` hostitelů),
mobilní/tabletové pořadí (Y,X), sbalení uvolňuje výšku v kompaktních režimech, tabletové výšky
`auto` + `min-height`, `IsMinimized` drží pozici na desktopu, nové modifikátory toolbaru,
`role="region"` na kořeni. JSON doc: žádné nové parametry (jen popis chování). Wireframe stencil:
role beze změny — ověřit, zda stencil nekóduje pořadí/rozvržení.

---

## 12. Rozhodnutí k potvrzení moderátorem

| # | Otázka | Doporučení |
|---|---|---|
| M-1 | Sbalení na **desktopu**: uvolnit řádky (přeskupit) vs. ponechat slot a zmenšit jen kartu | **Ponechat slot** (`align-self: start`); uvolnění jen tablet/mobil. Desktop má explicitní `Y`, přeskupení by měnilo uložený layout. |
| M-2 | Pořadí: seřazený **DOM** render vs. jen CSS `order` | **Seřazený DOM ve všech režimech** (opravuje WCAG 2.4.3 i na desktopu), `--tm-w-order` dál emitovat. |
| M-3 | Tablet výšky: pevné `span H` vs. `auto` + `min-height` | **`auto` + `min-height`** (`--tm-w-min-block`), stejné jako mobil. |
| M-4 | 44 px: jen `pointer: coarse` vs. i `data-layout="mobile"` | **Obojí** pro hlavičku a chevrony (plán chce tlačítka ≥ 44 na mobilu). |
| M-5 | „Upravit“ na mobilu: primary (F2) vs. outlined (reference) | **Primary** — konzistence, jeden primární prvek. |
| M-6 | `HeaderStart` na tabletu: řádek 1 s přepínačem vs. řádek 2 s titulem | **Řádek 1** (fokus = vizuální pořadí). |
| M-7 | `IsMinimized`: layoutový alias sbalení + odstranění `!important` | **Ano** (zároveň opravuje ztrátu `Y` na desktopu). |
| M-8 | Přechod 6 → 2 sloupce je na 768 px **mřížky** (≈ 816 px kořene) | **Ponechat** (mechanismus F1), zdokumentovat. |
| M-9 | Mobilní `min-height` s mezerami (sjednocení s tabletem) — karty o `(H−1)×16 px` vyšší | **Ano** (výška = desktopový slot). |
