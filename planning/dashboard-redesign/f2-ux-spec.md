# F2 — UX specifikace: chrome `TmDashboard` a karta widgetu (desktop)

Plán: *Tempo.Blazor — Redesign: TmDashboard* (`d254e3dd-5198-4c2d-8c35-08df2362aa32`), fáze
`fze-2-chrome-dashboardu-a-karta-widgetu-desktop`. Výchozí stav: commit `3c2fcc53` (F1 hotová —
přepínač na `TmOverlayPanel` s `role="menu"`, selektor na `TmModal`, lokalizace, `aria-*`).

Reference (jediný zdroj vizuálu pro F2):

- `output/imagegen/tempo-reimagined/TmDashboard/dashboard--desktop.png` — hlavička, karty, plocha.
- `output/imagegen/tempo-reimagined/TmDashboard/dashboard--tablet.png` — **jen hlavička karty**
  (podtitul, akce hostitele v hlavičce karty); hlavička dashboardu na tabletu patří do F3.

Rozsah F2: desktop (≥ 1024 px kontejneru, `data-layout="desktop"`) — hlavička dashboardu, plocha,
karta widgetu, edit režim (vizuál), stavy. **Mřížka se nemění** (umístění `--tm-w-*`, sloupce,
container query, drag/resize, `dashboard.js`). Tablet/mobil jsou níže jen jako poznámky pro F3.

Konvence: identifikátory anglicky, CSS výhradně `--tm-*` tokeny (žádný hex, `rgba()` ani fallback
v `var()`), nové třídy musí mít pravidlo v `_dashboard.css` (`MarkupClassCoverageTests`).

---

## 1. Rozbor reference (měřeno na 1536 × 1024, ≈ 1:1 vůči 1440 viewportu)

| Prvek | Reference | Rozhodnutí F2 |
|---|---|---|
| Obal komponenty | žádná bílá karta, off-white plocha | `Appearance=Plain` (default): kořen bez karty, pozadí `--tm-bg-workspace` |
| Okraj plochy | ~34 px od hrany | padding kořene `--tm-space-6` (24 px) — hostitel obvykle přidává vlastní okraj |
| Hlavička | jedna řádka, výška ~46 px, bez čáry pod ní | `flex`, `flex-wrap: wrap`, bez `border-bottom`, bez `padding-bottom` |
| Titul | „Projekt Aurora“, ~28 px bold | `h2`, `--tm-font-size-2xl` (24 px), `--tm-font-weight-bold` (viz §8 G-4) |
| Přepínač | select-like, ~308 × 46 px, ikona mřížky + název + šipka, světlý rámeček | nativní `<button class="tm-dashboard-switcher">`, výška `--tm-input-height-md` |
| Tužka vedle „Upravit“ | outlined ikonové tlačítko | **NEimplementuje se** (vymyšleno generátorem; přejmenování zůstává v edit režimu) |
| „Upravit“ | primární, indigo | `tm-btn tm-btn-primary`, ikona `edit` + text |
| Mezera hlavička → karty | ~28 px | `gap` kořene `--tm-space-6` |
| Mezera mezi kartami | ~17 px | beze změny (`GridConfig.Gap` = 16 px) |
| Karta | bílá, radius ~12 px, jemný border, téměř bez stínu | `--tm-bg-surface`, `--tm-radius-card`, 1 px `--tm-border-color`, `--tm-shadow-card` |
| Hlavička karty | bez šedého pruhu a bez čáry | transparentní, bez `border-bottom` |
| Ikonová dlaždice | 40–42 px, zaoblený čtverec, pastel kategorie | 40 px (`--tm-space-10`), `--tm-radius-control`, `color-mix` tint |
| Titul karty | ~20 px semibold | `--tm-font-size-lg` (18 px), `--tm-font-weight-semibold` |
| Chevron sbalení | vpravo, bez rámečku, ~32 px | `.tm-widget-btn` 32 px (`--tm-input-height-sm`) |
| Odsazení obsahu | ~20–24 px | horizontálně `--tm-space-5`, dole `--tm-space-5` |
| Tablet — hlavička karty | titul + podtitul („Aktuální stav a klíčové metriky“), vpravo badge / odkaz / select | `ShowWidgetDescriptions` + slot `WidgetHeaderActions` |

Obsah karet (prstenec, seznam úkolů, graf, aktivita) je **demo obsah** (`WidgetTemplate`), ne
komponenta (G12).

---

## 2. Layout — DOM a regiony

### 2.1 Kořen

```
TmLayoutObserver.tm-dashboard-layout
└─ div.tm-dashboard[.tm-dashboard--edit][.tm-dashboard--framed][.tm-dashboard--{mode}]  data-layout
   ├─ div.tm-dashboard-toolbar                       (hlavička; nevykreslí se při ShowHeader=false v view režimu)
   ├─ div.tm-dashboard-grid-container > div.tm-dashboard-grid   (beze změny)
   ├─ div.tm-dashboard-empty                          (prázdný stav)
   └─ TmMobileActionBar                               (beze změny, jen mobil + edit)
```

| Vlastnost | `Plain` (default) | `Card` (`.tm-dashboard--framed`) |
|---|---|---|
| `background` | `var(--tm-bg-workspace)` | `var(--tm-bg-surface)` |
| `border-radius` | — (0) | `var(--tm-radius-xl)` (dnešní hodnota) |
| `padding` | `var(--tm-space-6)` | `var(--tm-space-6)` |
| `gap` | `var(--tm-space-6)` | `var(--tm-space-6)` |
| `min-height` | ponechat dnešní `600px` (viz G-6) | totéž |

- Nový enum `DashboardAppearance { Plain, Card }` (namespace `Tempo.Blazor.Components.Dashboard`),
  parametr `Appearance` default `Plain`. `Card` přidá třídu **`tm-dashboard--framed`**
  (test `Dashboard_Appearance_DefaultPlain_CardAddsFramedClass`).
- Edit režim už **nemění** pozadí kořene ani nepřidává rámeček (odstranit ř. 16–19).
- Mobil (`data-layout="mobile"`) — padding `--tm-space-3` zůstává (ř. 52–54).

### 2.2 Hlavička dashboardu — view režim (desktop)

```
┌ .tm-dashboard-toolbar (flex, wrap, align-items:center, justify-content:space-between, gap space-4) ─────────────┐
│ .tm-dashboard-toolbar-left (flex, align-items:center, gap space-4, min-width:0, flex:1 1 auto)                    │
│   [HeaderStart]  .tm-dashboard-heading( h2.tm-dashboard-title / p.tm-dashboard-subtitle )  .tm-dashboard-views>   │
│                                                                           button.tm-dashboard-switcher           │
│ .tm-dashboard-toolbar-right (flex, align-items:center, gap space-2, flex:0 0 auto, margin-inline-start:auto)      │
│   .tm-dashboard-header-actions[HeaderActions]   button.tm-btn.tm-btn-primary „Upravit“                           │
└──────────────────────────────────────────────────────────────────────────────────────────────────────────────────┘
```

- **`.tm-dashboard-heading`**: `display:flex; flex-direction:column; gap: var(--tm-space-0-5); min-width:0`.
- **`h2.tm-dashboard-title`**: text `Title ?? _currentDashboard?.Name ?? Loc["TmDashboard_DefaultName"]`;
  `--tm-font-size-2xl`, `--tm-font-weight-bold`, `--tm-line-height-tight`, `--tm-text-primary`,
  `margin:0`, `overflow-wrap:anywhere` (dnešní pravidlo ř. 43–50, jen změna velikosti).
- **Duplicita názvu** (rozhodnutí): pokud `Title` je `null` **a** přepínač se vykresluje
  (`DashboardProvider` není null), h2 dostane navíc třídu `tm-sr-only` — název je vidět jen
  v přepínači, heading zůstává pro čtečky a pro existující testy (`.tm-dashboard-title`
  `TextContent`). S `Title` je h2 viditelný. Bez provideru (bez přepínače) je h2 vždy viditelný.
- **`p.tm-dashboard-subtitle`** (jen když `Subtitle` není prázdný): `--tm-font-size-sm`,
  `--tm-text-secondary`, `--tm-line-height-normal`, `margin:0`. Podtitul se zobrazí jen u
  viditelného h2 (je-li h2 `tm-sr-only`, podtitul zůstane viditelný samostatně).
- **`.tm-dashboard-header-start`** (wrapper `HeaderStart`): `display:flex; align-items:center;
  gap: var(--tm-space-2)`; vykreslit jen když je fragment zadán.
- **`.tm-dashboard-header-actions`** (wrapper `HeaderActions`): `display:flex; align-items:center;
  gap: var(--tm-space-2)`; **jen ve view režimu**, DOM pořadí **před** „Upravit“
  (test `Dashboard_HeaderActions_RenderedBeforeEditButton_InViewMode`).
- „Upravit“: zůstává nativní `<button>` (potřebuje `@ref _editButton` pro návrat fokusu; `TmButton`
  `ElementReference` nevystavuje), třídy **`tm-btn tm-btn-primary`** (dnes `tm-btn-secondary`),
  ikona `IconNames.Edit` `IconSize.Sm` + `<span>` text `TmDashboard_Edit`. Zrušit `title`
  (duplikuje viditelný text). Renderovat jen při `AllowEdit` (dnes se renderuje vždy — `EnterEditMode`
  pak tiše nic neudělá; viz §5.9).
- `ShowHeader=false`: ve **view** režimu se celý `.tm-dashboard-toolbar` nevykreslí. V **edit**
  režimu se toolbar vykreslí vždy (jinak by nešlo uložit/zrušit — do edit režimu se lze dostat
  přes CTA prázdného stavu). Test `Dashboard_ShowHeaderFalse_HidesHeader` = view režim.
- `ShowTitle`: **ponechat bez `[Obsolete]`** — má jinou sémantiku než `ShowHeader` (skryje jen h2
  + podtitul, přepínač a akce zůstanou; demo ho dnes používá). Zapsat do CHANGELOG jako
  rozhodnutí. `ShowTitle=false` ⇒ h2 ani podtitul se nevykreslí vůbec.

### 2.3 Přepínač nástěnek (`.tm-dashboard-switcher`)

Markup (wrapper `div.tm-dashboard-views` zůstává — testy hledají `.tm-dashboard-views button`):

```
<button type="button" class="tm-dashboard-switcher" id=_dashboardsButtonId @ref=_dashboardsButton
        aria-haspopup="menu" aria-expanded="true|false"
        aria-label="@string.Format(Loc["TmDashboard_SwitcherAriaLabel"], currentName)">
  <TmIcon Name="IconNames.Grid" Size="IconSize.Sm" class="tm-dashboard-switcher__icon" />
  <span class="tm-dashboard-switcher__label">@currentName</span>
  <TmIcon Name="IconNames.ChevronDown" Size="IconSize.Sm" class="tm-dashboard-switcher__chevron" />
</button>
```

`currentName = _currentDashboard?.Name ?? Loc["TmDashboard_DefaultName"]`.

| Vlastnost | Hodnota |
|---|---|
| `display` | `inline-flex; align-items:center; gap: var(--tm-space-2)` |
| výška | `height: var(--tm-input-height-md)` (38 px; stejná jako „Upravit“) |
| šířka | `min-inline-size: 14rem; max-inline-size: 20rem` (literál, viz G-3) |
| padding | `0 var(--tm-space-3)` |
| pozadí / rámeček | `var(--tm-bg-surface)`, `1px solid var(--tm-border-color)` |
| radius | `var(--tm-radius-control)` |
| písmo | `font: inherit; --tm-font-size-sm; --tm-font-weight-medium; color: var(--tm-text-primary)` |
| `__label` | `flex:1; min-width:0; text-align:start; white-space:nowrap; overflow:hidden; text-overflow:ellipsis` |
| `__icon`, `__chevron` | `color: var(--tm-text-secondary); flex-shrink:0` |
| `__chevron` při `[aria-expanded="true"]` | `transform: rotate(180deg); transition: transform var(--tm-transition-fast)` |

Menu (`TmOverlayPanel`) se **nemění** kromě:

- `Align="OverlayAlign.Start"` (přepínač je teď vlevo; dnes `End`) a `MatchAnchorWidth="true"`
  (menu ≥ šířka triggeru; `.tm-dashboard-menu { min-width: 200px }` ponechat).
- Výchozí nástěnka: místo samotné hvězdy textový štítek `span.tm-dashboard-menu-default`
  s `Loc["TmDashboard_Default"]` („Výchozí“) + `TmIcon star` `aria-hidden`; styl:
  `--tm-font-size-xs`, `--tm-font-weight-medium`, `--tm-text-secondary`, `padding: 0 var(--tm-space-2)`.
  `role="img"`/`aria-label` na stávajícím `.tm-dashboard-menu-badge` odpadá (text je viditelný).
  *(Vysvětlení „proč nejde smazat“ je F7.)*
- `.tm-dashboard-menu` stín přepnout na `var(--tm-shadow-popover)`, radius `var(--tm-radius-card)`.

### 2.4 Hlavička dashboardu — edit režim (desktop)

```
[HeaderStart] h2(Title ?? Name)+rename | badge „Režim úprav“  ……  [＋ Přidat widget] [Uložit změny] [Zrušit]
```

- Přepínač, `Subtitle` a `HeaderActions` se v edit režimu **nevykreslují** (jako dnes přepínač).
- h2 je v edit režimu **vždy viditelný** (žádné `tm-sr-only`) — uživatel vidí, co přejmenovává.
  Má-li hostitel `Title`, vedle h2 se zobrazí `span.tm-dashboard-name` s názvem nástěnky
  (`--tm-font-size-sm`, `--tm-text-secondary`) a teprve za ním tužka přejmenování — tužka tak vždy
  sousedí s textem, který mění. Bez `Title` zůstává dnešní `h2 + tužka`.
- Inline editace názvu (`form.tm-dashboard-name-edit`) beze změny chování; `.tm-dashboard-name-input input`
  přepnout na `--tm-font-size-2xl` (soulad s titulem).
- Badge `.tm-dashboard-edit-badge`: ponechat tvar; text barvou `var(--tm-color-primary-text)`
  (místo `--tm-color-primary` — vyšší kontrast na `--tm-color-primary-subtle` v obou tématech).
- Tlačítka vpravo (doporučení — jeden primární prvek na region): „Uložit změny“ `tm-btn-primary`,
  „Přidat widget“ `tm-btn-secondary`, „Zrušit“ `tm-btn-ghost`. Pokud se implementátor rozhodne
  ponechat dnešní variantu (Přidat = primary, Uložit = secondary), zapsat jako vědomou odchylku;
  při změně → CHANGELOG Migration (vizuál) a ověřit, že žádný test nehledá `.tm-btn-primary` u Přidat.

### 2.5 Plocha a edit režim

- **View**: plocha = pozadí kořene (`--tm-bg-workspace` / `--tm-bg-surface` u `Card`).
- **Edit** — „jemné zvýraznění“ místo dashed rámečku:
  - mřížka v pozadí (`.tm-dashboard-grid-bg`, jen desktop — dnešní podmínka `mode.IsDesktop`):
    `.tm-dashboard-grid-col` →
    `background-color: var(--tm-color-primary-subtle)`;
    `border: 1px dashed var(--tm-color-primary-soft-border)`;
    řádkové linky `repeating-linear-gradient(… var(--tm-color-border-subtle) …)` **bez** `rgba`
    fallbacků (ř. 144, 147); `border-radius: var(--tm-radius-control)`.
    Odstranit pravidlo s `rgba(99, 102, 241, 0.03)` (ř. 151–153).
  - karta widgetu v edit režimu: `border-color: var(--tm-color-primary-soft-border)`;
    `.tm-dashboard--edit .tm-widget-header { cursor: grab }` (dnes má hlavička `cursor:grab` i ve
    view režimu — opravit, ve view `cursor:auto`).
  - badge „Režim úprav“ v hlavičce (viz 2.4).

### 2.6 Karta widgetu

```
div.tm-widget  (flex column; surface; 1px border; radius-card; shadow-card; overflow hidden)
├─ div.tm-widget-header  (flex, align-items:center, gap space-3, padding space-4 space-5)
│  ├─ div.tm-widget-header-left (flex, align-items:center, gap space-3, flex:1, min-width:0)
│  │  ├─ span.tm-widget-drag-handle            (jen edit + CanDrag; beze změny)
│  │  ├─ span.tm-widget-icon.tm-dashboard-category--{id} aria-hidden   (ShowWidgetIcons)
│  │  │    └─ TmIcon Name=definition.Icon Size=Md
│  │  └─ div.tm-widget-heading (flex column, gap space-0-5, min-width:0)
│  │       ├─ h3.tm-widget-title id="tm-widget-title-{InstanceId}"
│  │       └─ p.tm-widget-description            (ShowWidgetDescriptions && Description)
│  └─ div.tm-widget-header-actions (flex, align-items:center, gap space-1, flex-shrink:0)
│       ├─ div.tm-widget-host-actions [WidgetHeaderActions(widget)]   (jen view)
│       ├─ button.tm-widget-btn  (collapse; jen view && CanCollapse)
│       └─ button.tm-widget-btn.tm-widget-btn--danger (remove; jen edit)
└─ div.tm-widget-content id=… (padding 0 space-5 space-5; overflow auto)
```

| Prvek | Tokeny / hodnoty |
|---|---|
| `.tm-widget` | `background: var(--tm-bg-surface)`; `border: 1px solid var(--tm-border-color)`; `border-radius: var(--tm-radius-card)` (dnes `--tm-radius-lg`, ř. 162); `box-shadow: var(--tm-shadow-card)` (dnes `--tm-shadow-sm`) |
| `.tm-widget:hover` | **ve view režimu žádná změna stínu** (karta není klikací; dnešní `hover → shadow-md` odstranit); v edit režimu `border-color: var(--tm-color-primary)` |
| `.tm-widget-header` | `background: transparent`; **bez** `border-bottom`; `padding: var(--tm-space-4) var(--tm-space-5)` |
| `.tm-widget-icon` | `inline-size/block-size: var(--tm-space-10)` (40 px); `flex-shrink:0`; `display:inline-flex; align-items:center; justify-content:center`; `border-radius: var(--tm-radius-control)`; `background: color-mix(in srgb, var(--category-color, var(--tm-category-fallback)) var(--tm-dashboard-category-tint), var(--tm-bg-surface))`; `color: var(--category-color, var(--tm-category-fallback))` |
| `--tm-dashboard-category-tint` | komponentová proměnná na `.tm-dashboard`: `12%`; v dark (`[data-theme="dark"] .tm-dashboard, .tm-dark .tm-dashboard`) `20%` (viz G-2) |
| `.tm-widget-title` | `h3`, `margin:0`; `--tm-font-size-lg`; `--tm-font-weight-semibold`; `--tm-line-height-tight`; `--tm-text-primary`; 1 řádek s ellipsis (dnešní ř. 224–231, jen velikost) |
| `.tm-widget-description` | `margin:0`; `--tm-font-size-sm`; `--tm-text-secondary`; `--tm-line-height-normal`; 1 řádek s ellipsis |
| `.tm-widget-btn` | `inline-size/block-size: var(--tm-input-height-sm)` (32 px, místo `1.75rem`); `border-radius: var(--tm-radius-control)`; `color: var(--tm-text-secondary)`; transition jen `background-color, color` (ne `all`) |
| `.tm-widget-host-actions` | `display:flex; align-items:center; gap: var(--tm-space-2)`; `margin-inline-end: var(--tm-space-1)` |
| `.tm-widget-content` | `padding: 0 var(--tm-space-5) var(--tm-space-5)` (dnes `space-4` dokola) |
| sbalená karta | jen hlavička (symetrický padding `space-4` nahoře i dole); `grid-row` se v F2 **nemění** (uvolnění místa = F3) |

Ikona a barva:

- Definice: `WidgetRegistry?.GetWidget(widget.WidgetId)`. Ikona = `definition.Icon`; pokud je
  definice `null` nebo `Icon` prázdný → `IconNames.Box` a třída se nepřidá (barva padne na
  `--tm-category-fallback`). Neregistrovaný název ikony vyřeší `TmIcon` svým `tm-icon-unknown`
  (prázdná dlaždice s tintem — přijatelné).
- Barva kategorie přes **sdílený helper** `DashboardCategoryStyles` (internal static, přesun z
  `TmWidgetSelector.razor` ř. 197–213): `GetColorClass(categoryId)` → `tm-dashboard-category--{id}`
  pro vestavěné, `""` pro vlastní; `GetColorStyle(categoryId, categories)` → `--category-color: {Color}`
  pro vlastní, jinak `""`. Používá ho `TmWidgetSelector` (karta i sidebar) **i** ikona widgetu
  (test `DashboardCategoryStyles_SharedBySelectorAndWidget`). Pravidla `.tm-dashboard-category--*`
  (ř. 451–458) beze změny.
- `ShowWidgetIcons` default `true` (Migration), `false` ⇒ `.tm-widget-icon` se nevykreslí.
- `ShowWidgetDescriptions` default `false`; `true` ⇒ `p.tm-widget-description` z
  `definition.Description` (prázdný popis ⇒ nic).
- `WidgetHeaderActions` (`RenderFragment<WidgetInstance>?`): jen ve **view** režimu, DOM pořadí
  před chevronem (test `Widget_HeaderActions_RenderedBeforeCollapse_HiddenInEditMode`). Obsah
  (badge, odkaz „Zobrazit všechny ›“, `TmSelect` období) dodává hostitel; komponenta nastaví jen
  rozvržení. Hostitel odpovídá za přístupná jména svých prvků.
- `CanCollapse(widget)`: dnes vždy `true` (ř. 944–947). Model `WidgetDefinition` vlastnost
  `CanCollapse`/`IsCollapsible` **nemá** — viz G-7: doporučení přidat do Abstractions
  `public bool IsCollapsible { get; set; } = true;` a `CanCollapse => definition?.IsCollapsible ?? true`.

---

## 3. Mapování tokenů (souhrn)

| Účel | Token | Default světlý / indigo / dark |
|---|---|---|
| Plocha | `--tm-bg-workspace` | bílá / `#f7f8fc` / `--tm-bg-page` (`#0f172a`), indigo dark `#0b1120` |
| Karta, trigger | `--tm-bg-surface` | bílá / bílá / `#1e293b` |
| Rámeček karty a triggeru | `--tm-border-color` | gray-200 |
| Hover rámečku triggeru | `--tm-border-hover` | gray-300 |
| Rádius karty / Card obalu | `--tm-radius-card` / `--tm-radius-xl` | 8 px / 12 px (indigo) |
| Rádius triggeru, dlaždice, ikon. tlačítek | `--tm-radius-control` | 6 px / 8 px (indigo) |
| Stín karty / menu | `--tm-shadow-card` / `--tm-shadow-popover` | md / lg |
| Fokus | `--tm-focus-ring` | 2 px mezera + 2 px primary |
| Titul dashboardu | `--tm-font-size-2xl`, `--tm-font-weight-bold` | 24 px / 700 |
| Titul karty | `--tm-font-size-lg`, `--tm-font-weight-semibold` | 18 px / 600 |
| Podtitul, popis, trigger | `--tm-font-size-sm` | 14 px |
| Sekundární text, ikony | `--tm-text-secondary` | gray-600 / `#cbd5e1` |
| Výška triggeru a tlačítek hlavičky | `--tm-input-height-md` | 38 px |
| Ikonové tlačítko karty | `--tm-input-height-sm` | 32 px |
| Dlaždice | `--tm-space-10` | 40 px |
| Barva kategorie | `--tm-category-1…8`, `--tm-category-fallback` | dark má vlastní 400 kroky |
| Edit mřížka | `--tm-color-primary-subtle`, `--tm-color-primary-soft-border`, `--tm-color-border-subtle` | — |
| Edit badge | `--tm-color-primary-subtle` + `--tm-color-primary-text` | — |
| Mezery | `--tm-space-0-5/1/2/3/4/5/6` | 2/4/8/12/16/20/24 px |
| Přechody | `--tm-transition-fast` | 100 ms |

Hardcody k odstranění v `_dashboard.css`: ř. 152 `rgba(99,102,241,0.03)`; fallbacky `rgba(…)`
v ř. 144 a 147; `1.75rem` v ř. 243–244 (→ `--tm-input-height-sm`); `transition: all` (ř. 251).

---

## 4. Komponenty k použití

| Potřeba | Komponenta / prvek | Poznámka |
|---|---|---|
| Ikony (mřížka, šipka, tužka, chevron, koš, kategorie) | `TmIcon` + `IconNames.Grid/ChevronDown/Edit/ChevronUp/Trash/Box` | `aria-hidden` dává `TmIcon` sám |
| Menu přepínače | `TmOverlayPanel` (stávající z F1) | `Role="menu"`, `MatchAnchorWidth`, `Align=Start` |
| „Upravit“, tlačítka edit režimu | nativní `<button class="tm-btn …">` | kvůli `@ref`/`FocusAsync`; `TmButton` nevystavuje `ElementReference` |
| Footer dialogů | `TmButton` | beze změny |
| Visually hidden h2 | utilita `.tm-sr-only` (`base.css` ř. 32) | — |
| Prázdný stav | ponechat vlastní `.tm-dashboard-empty` | `TmEmptyState` až případně F8 (DOM selektory) |
| Načítání (volitelné) | `TmSkeleton` | viz §5.7 |
| Demo obsah | `TmGauge`, `TmChart`, `TmStatCard`, `TmBadge`, `TmAvatar` | jen demo (G12) |

---

## 5. Stavy

### 5.1 Default (view)
Viz §2.2 a §2.6. Přepínač ukazuje název aktuální nástěnky, „Upravit“ primární.

### 5.2 Hover
- Trigger: `border-color: var(--tm-border-hover)`; `background: var(--tm-bg-surface-secondary)`.
- „Upravit“ a `tm-btn-*`: z `_button.css` (beze změny).
- `.tm-widget-btn:hover`: `background: var(--tm-bg-muted)`; `color: var(--tm-text-primary)`
  (`--danger`: `--tm-color-danger-subtle` / `--tm-color-danger`) — beze změny.
- Karta: ve view režimu žádný hover efekt; v edit režimu `border-color: var(--tm-color-primary)`.
- Hover pravidla obalit `@media (hover: hover)` není nutné (žádný stav se neodhaluje jen hoverem).

### 5.3 Focus-visible
- Trigger, `.tm-widget-btn`, `.tm-widget-drag-handle` (až bude button v F4):
  `outline: 2px solid transparent; box-shadow: var(--tm-focus-ring);` (transparentní outline
  zachová viditelnost ve Windows High Contrast). Globální `:focus-visible` z `base.css` se tím
  pro tyto prvky nahradí — sjednocené s `.tm-btn:focus-visible`.
- Karta (`.tm-widget`) není fokusovatelná; `:focus-within` nepřidává vizuál (resize úchyty už
  na `focus-within` reagují — ponechat).

### 5.4 Active / pressed / open
- Trigger `[aria-expanded="true"]`: `border-color: var(--tm-color-primary)`, chevron otočený o 180°.
- Trigger `:active`: `background: var(--tm-bg-muted)`.

### 5.5 Edit režim
Viz §2.4 a §2.5. Kontrola: žádný `2px dashed` na kořeni; badge „Režim úprav“ viditelný;
mřížka pozadí jen na desktopu.

### 5.6 Prázdný stav (žádné widgety)
- Hlavička se vykresluje normálně (titul, přepínač, „Upravit“).
- `.tm-dashboard-empty` leží na ploše; dlaždice `.tm-dashboard-empty-icon` `80px` → ponechat,
  pozadí `--tm-bg-muted`, radius `--tm-radius-card` (dnes `--tm-radius-xl`).
- CTA „Přidat widgety“ ve view režimu je druhý primární prvek vedle „Upravit“ → změnit na
  `tm-btn-secondary`? **Ne** — prázdný stav je hlavní úloha obrazovky; ponechat primary, vědomá
  výjimka. CTA renderovat jen při `AllowEdit`.

### 5.7 Loading (doporučené, nízké riziko)
Dnes se před načtením dat (`_dataLoaded == false`) krátce zobrazí prázdný stav („Zatím žádné
widgety“) — falešná informace. Doporučení: dokud `!_dataLoaded`, místo prázdného stavu vykreslit
2 karty `TmSkeleton` (výška `calc(4 * var(--row-height))`) a na `.tm-dashboard-grid-container`
`aria-busy="true"`; trigger zobrazí `TmDashboard_DefaultName`. Pokud se nestihne, zapsat do backlogu.

### 5.8 Error
Komponenta dnes výjimky provideru nezachytává. **Mimo rozsah F2** — zapsat do backlogu
(`TmAlert` nad mřížkou + „Zkusit znovu“).

### 5.9 `AllowEdit=false`
„Upravit“ ani CTA prázdného stavu se nevykreslí; `HeaderActions` zůstávají. Mění se jen
render (`EnterEditMode` už dnes nic neudělá) → CHANGELOG (Fixed).

### 5.10 Collapsed
- Chevron `chevron-down` (sbaleno) / `chevron-up` (rozbaleno); `aria-expanded` a `aria-controls`
  z F1 beze změny.
- Sbalená karta: jen hlavička, obsah nevykreslen. Výška slotu v mřížce se v F2 nemění (F3).

### 5.11 Dark mode a téma indigo
- Všechny barvy přes tokeny → dark/indigo automaticky. Ověřit screenshoty:
  light/dark × default/indigo (matice v §9).
- Tint dlaždice v dark `20 %` (§2.6): ikona `--tm-category-N` (400 kroky) na
  `color-mix(… 20 %, #1e293b)` — ikona je dekorativní (`aria-hidden`), 1.4.11 se nevztahuje;
  přesto vizuálně ověřit čitelnost u `--tm-category-7` (šedá) a `--tm-category-3` (amber) ve
  světlém režimu — při kontrastu < 2:1 vůči tintu použít pro ikonu
  `color-mix(in srgb, var(--category-color) 75%, var(--tm-text-primary))`.
- Default téma: `--tm-bg-workspace` = bílá ⇒ `Plain` dashboard je bílý a karty se oddělují jen
  rámečkem + stínem — záměr G4, neměnit.

### 5.12 Reduced motion / forced colors
- `@media (prefers-reduced-motion: reduce)`: přidat `.tm-dashboard-switcher__chevron` a
  `.tm-widget-btn` do stávajícího bloku `transition: none` (ř. 904–911).
- Forced colors: dlaždice ztratí pozadí (OK); trigger si drží `border` (systémová barva).

---

## 6. Fokusové pořadí a ARIA

### 6.1 Pořadí Tab — view
1. obsah `HeaderStart` (hostitel)
2. přepínač `.tm-dashboard-switcher`
3. obsah `HeaderActions`
4. „Upravit“
5. pro každý widget v **DOM pořadí**: obsah `WidgetHeaderActions` → chevron sbalení → fokusovatelný
   obsah widgetu.

⚠ DOM pořadí widgetů = pořadí v `_widgets` (pořadí přidání), ne vizuální `(Y, X)` → porušení
WCAG 2.4.3 už dnes. Oprava (render v pořadí `BuildOrder()`) patří do F3 („pořadí podle `(Y,X)`“);
pokud ji F2 udělá dřív, umístění na desktopu se nezmění (explicitní `grid-row/column`), ale
`@key` musí zůstat `InstanceId`.

### 6.2 Pořadí Tab — edit
`HeaderStart` → tužka přejmenování → „Přidat widget“ → „Uložit změny“ → „Zrušit“ → per widget:
(drag handle — až F4) → „Odebrat widget“.

**Fokus při vstupu do edit režimu**: dnes tlačítko „Upravit“ zmizí a fokus padne na `<body>`.
F2 doplní: `EnterEditMode` nastaví příznak a `OnAfterRenderAsync` přesune fokus na „Přidat widget“
(stejný mechanismus jako `_focusAddWidgetButtonAfterCreate`; na mobilu tlačítko není → tichý catch).
Návrh testu: `Dashboard_EnterEditMode_MovesFocusToAddWidget`. Výstup z edit režimu už vrací fokus
na přepínač / „Upravit“ (F1).

### 6.3 ARIA

| Prvek | Atributy |
|---|---|
| Kořen | volitelně `role="region"` + `aria-labelledby` = id h2 (doporučeno; jedna oblast na stránku) |
| h2 `.tm-dashboard-title` | `id="tm-dashboard-title-{guid}"`; při `tm-sr-only` dál v DOM |
| Přepínač | `aria-haspopup="menu"`, `aria-expanded`, `id`, `aria-label = string.Format(Loc["TmDashboard_SwitcherAriaLabel"], currentName)` — **jméno musí obsahovat viditelný text** (WCAG 2.5.3 Label in Name); samotné „Nástěnky“ z textu plánu by viditelný název přebilo, proto formátovaný klíč. Odstranit `title`. |
| Menu | beze změny z F1 (`role="menu"`, `aria-labelledby` = trigger, `menuitem`, `aria-current`) |
| Výchozí štítek v menu | prostý text, žádné `role="img"` |
| `HeaderActions`, `WidgetHeaderActions` | bez obalové role (žádný `toolbar` — jinak by bylo nutné šipkové ovládání) |
| h3 `.tm-widget-title` | `id="tm-widget-title-{InstanceId}"` |
| Dlaždice ikony | `aria-hidden="true"` |
| Popis widgetu | `id="tm-widget-desc-{InstanceId}"`; neodkazovat (čte se v přirozeném pořadí za h3) |
| Chevron sbalení | `aria-expanded`, `aria-controls` (F1) + **`aria-describedby` = id h3** → „Sbalit, Úkoly“ (rozliší N stejných tlačítek, test F1 na `aria-label="Sbalit"` zůstane zelený) |
| „Odebrat widget“ | totéž `aria-describedby` = id h3 |
| Edit badge | prostý text; volitelně `role="status"` není nutný (změnu režimu ohlásí přesun fokusu) |

---

## 7. Tablet a mobil (poznámky pro F3 — v F2 neimplementovat, ale nerozbít)

- **Tablet (`dashboard--tablet.png`)**: hlavička ve dvou řádcích — přepínač nahoře (label
  „Nástěnky“/název), pod ním titul + podtitul, „Upravit“ vpravo na úrovni titulu. Struktura z
  §2.2 to umožní přeuspořádáním přes `order`/`flex-basis` pod `data-layout="tablet"` — proto
  jsou přepínač a heading samostatné flex položky (ne vnořené).
- Karty na tabletu: ikona v referenci kruhová ~56–64 px — **vědomá odchylka**, držíme 40 px
  zaoblený čtverec na všech šířkách (konzistence s `TmWidgetSelector` 56 px v F6 je jiný kontext).
- **Mobil**: titul/podtitul nahoře, přepínač + „Upravit“ pod ním jako tlačítka ≥
  `--tm-touch-target`; `flex-wrap` z §2.2 je předpoklad. Spodní lišta ve view režimu se nedělá.
- `@media (pointer: coarse)`: `.tm-widget-btn` už roste na `--tm-touch-target` (ř. 888–893);
  přidat `.tm-dashboard-switcher { min-block-size: var(--tm-touch-target) }` je F3.
- `WidgetHeaderActions` na úzké kartě: obal `.tm-widget-host-actions` musí mít `min-width:0` a
  obsah se smí zalomit / skrýt hostitelem; F3 rozhodne o přetečení.

---

## 8. Mezery a otázky (gaps)

| # | Mezera | Doporučení |
|---|---|---|
| G-1 | Plán chce `aria-label` = „Nástěnky“, ale trigger nově ukazuje název → konflikt s WCAG 2.5.3 | nový klíč `TmDashboard_SwitcherAriaLabel` („Nástěnky: {0}“), viz §6.3 |
| G-2 | Chybí sdílený token pro „subtle tint kategorie“ (F2 dlaždice 40 px, F6 dlaždice 56 px) | komponentová proměnná `--tm-dashboard-category-tint` (12 % / dark 20 %) v `_dashboard.css`; kandidát na globální token v core follow-up plánu |
| G-3 | Žádný token pro šířku select-like triggeru | literály `14rem`/`20rem` s komentářem (audit hlídá jen nedefinované tokeny) |
| G-4 | Titul v referenci ~28–30 px (`3xl`), knihovna volí `2xl` | `2xl` — komponenta je vnořená pod `h1` stránky hostitele; vědomá odchylka |
| G-5 | `--tm-shadow-card` = `md`, reference „téměř bez stínu“ | držet token (plán to vyžaduje); případné změkčení přes téma indigo (`--tm-shadow-card`) mimo F2 |
| G-6 | `min-height: 600px` na kořeni (hardcode) | ponechat v F2 (chování plochy), zapsat do backlogu |
| G-7 | `WidgetDefinition` nemá `IsCollapsible`; plán chce „`CanCollapse` respektuje definici“ | přidat `IsCollapsible` (default `true`) do `Tempo.Blazor.Abstractions` — aditivní, CHANGELOG Added |
| G-8 | Lokalizace: plán uvádí en/cs/fr, `TmResourceCompletenessTests` vyžaduje i **sk** | přidat i `TmResources.sk.json` |
| G-9 | Reference „Výchozí dashboard“ vs. DA4 („nástěnka“) | cs texty „Výchozí nástěnka“ / „Výchozí“ |
| G-10 | Loading/error stavy chybí | loading volitelně v F2 (§5.7), error do backlogu |
| G-11 | DOM pořadí widgetů ≠ vizuální pořadí | F3 (§6.1) |

### Nové lokalizační klíče

| Klíč | en | cs | sk | fr |
|---|---|---|---|---|
| `TmDashboard_Default` | Default | Výchozí | Predvolená | Par défaut |
| `TmDashboard_SwitcherAriaLabel` | Dashboards: {0} | Nástěnky: {0} | Nástenky: {0} | Tableaux de bord : {0} |

(+ `MockTmLocalizer` en/cs.)

---

## 9. Ověření (vazba na úkoly fáze)

bUnit (RED dle plánu) — upřesnění selektorů:

- `Dashboard_Switcher_ShowsCurrentDashboardName_WithAriaHaspopup`: `.tm-dashboard-switcher__label`
  text = název; `aria-haspopup="menu"`; `aria-label` obsahuje název.
- `Dashboard_Title_OverridesDashboardName`: `Title="X"` ⇒ `.tm-dashboard-title` = „X“, bez `tm-sr-only`.
- `Dashboard_Subtitle_RendersWhenSet`: `.tm-dashboard-subtitle`.
- `Dashboard_HeaderActions_RenderedBeforeEditButton_InViewMode`: index v `.tm-dashboard-toolbar-right`.
- `Dashboard_ShowHeaderFalse_HidesHeader`: žádný `.tm-dashboard-toolbar` ve view režimu.
- `Dashboard_EditButton_IsPrimary`: `[data-testid$='dashboard-edit']` má `tm-btn-primary`.
- `Dashboard_Appearance_DefaultPlain_CardAddsFramedClass`: `tm-dashboard--framed` jen u `Card`.
- `Widget_Header_RendersIconTileFromDefinition_WithCategoryClass`: `.tm-widget-icon.tm-dashboard-category--analytics`.
- `Widget_Header_IconHidden_WhenShowWidgetIconsFalse`, `Widget_Header_DescriptionShown_WhenShowWidgetDescriptions`.
- `Widget_HeaderActions_RenderedBeforeCollapse_HiddenInEditMode`: `.tm-widget-host-actions` před `.tm-widget-btn`.
- `DashboardCategoryStyles_SharedBySelectorAndWidget`.
- Navíc doporučené: `Dashboard_EnterEditMode_MovesFocusToAddWidget`,
  `Dashboard_TitleNullWithSwitcher_TitleIsVisuallyHidden`, `Widget_CollapseButton_DescribedByTitle`.

CSS testy: `.tm-widget` → `--tm-radius-card` a `--tm-shadow-card`; `.tm-dashboard` →
`--tm-bg-workspace`; `.tm-dashboard-toolbar` bez `border-bottom`; `.tm-widget-header` bez
`border-bottom` a bez `--tm-bg-surface-secondary`; `.tm-dashboard--edit` bez `dashed`;
`_dashboard.css` neobsahuje `rgba(`.

Screenshoty `tests/Tempo.Blazor.E2E/__screenshots__/dashboard/` (1440 px): view, edit, otevřený
přepínač × light/dark × default/indigo = 12 snímků. Checklist UX review:

1. Žádná obalová karta (Plain), žádná čára pod hlavičkou.
2. Přepínač vlevo za titulem, ukazuje název, ikona mřížky + šipka; menu zarovnané vlevo, ≥ šířka triggeru.
3. „Upravit“ primární; žádná samostatná tužka ve view režimu.
4. Karta: radius dle tématu (8/12 px), bez šedého pruhu, dlaždice 40 px s tintem kategorie, titul 18 px semibold, chevron bez rámečku.
5. Edit: jemná mřížka v pozadí, badge „Režim úprav“, bez dashed rámečku kořene.
6. Dark: tinty dlaždic čitelné, rámečky karet viditelné na `--tm-bg-workspace`.
7. Fokusový prstenec viditelný na triggeru, „Upravit“ a chevronech.
