# Návrh: tématické výplně stencilů DiagramEditoru (Fáze 8c)

> **Stav:** k rozhodnutí (`DIAGRAM-STENCIL-THEMED-FILLS`).
> **Cílový release:** 2.13.0 (unreleased).
> **Rozsah:** `Tempo.Blazor` (tokens.css / tokens-dark.css), `Tempo.Blazor.DiagramEditor` (stencily Archimate3 / BuiltIn / Extended), `Tempo.Blazor.Abstractions` (`DiagramSvgPalette` aditivně), `Tempo.Blazor.Modeling` (`ViewerTokens`). Všechny změny jsou **aditivní**; light vzhled canvasu i SVG exportu zůstává pixelově beze změny.

## 0. Shrnutí rozhodnutí

| Otázka | Rozhodnutí |
|--------|------------|
| Kde žijí nové barvy? | Jen `tokens.css` + oba bloky `tokens-dark.css` (dark roots i light island). Žádný `[data-theme=dark]` v `_diagram-editor.css`. |
| Light hodnoty | Dnešní literální hex každé výplně / stroke / ink. Light canvas i Light SVG export jsou pixelově 2.12.0. |
| Ink na barevné výplni (CSS) | `--tm-diagram-node-ink-on-fill`: light `#4b5563` (= dnešní `--tm-paper-ink-muted`), dark `#e5e7eb`. **Ne** `#1f2937` v light (R1). |
| Ink na barevné výplni (SVG export Light) | Paletový slot `OnFillText` = `#111827` (dnešní `DefaultText`). CSS a Light export se tedy **záměrně rozcházejí** (R3). |
| Stroke na barevné výplni | Dedikovaný `--tm-diagram-stencil-stroke-on-fill` (light `#1f2937`, dark `#e5e7eb`) s vlastním mapováním v paletě. **Ne** `--tm-diagram-node-stroke` (R2: Light export by spadl na `DefaultNodeStroke` `#111827`). |
| Headless export | `DiagramSvgPalette.Tokens` (`IReadOnlyDictionary<string,string>`) mapuje `var(--token)` na konkrétní hex. Neznámý token dál padá na `SlotDefault`. |
| Hostitelské přepisy | `node.Style.Fill` / `Stroke` / `Color` a heat overlay (`var(--tm-color-heat-*)`) mají dál přednost. |
| Vlastní stencil pack s pevným hexem | Hostitel si dark řeší sám; může přejít na nové tokeny. Není breaking. |
| `--tm-diagram-node-ink` | Beze změny. Hostitelé, kteří ho přepisovali kvůli kontrastu na ArchiMate, ho mohou odstranit. |

## 1. Problém

V dark mají uzly ArchiMate / BuiltIn / Extended **světlé pevné výplně** ze stencilů (`#cfe8ff` atd.) a popisek bere `--tm-diagram-node-ink` = `--tm-canvas-paper-ink-muted` = `#a1a1aa`. Příklad: `#a1a1aa` na `#cfe8ff` (~1,7:1). Příčinou jsou stencily DiagramEditoru, ne CSS Modelingu. BPMN (TG-004) už maluje přes `--tm-diagram-paper` / `--tm-diagram-node-stroke` a v dark je v pořádku.

Backlog PH `3ea75759-…` se tady neresolvuje.

## 2. Tokeny

Každá **pevná výplň** (a u BuiltIn i barevný stroke / barevný ink sekce) dostane sémantický token. Light = dnešní hex. Dark = tmavý tón téže rodiny z palety `tokens-dark.css` (zinc / primary-900 / status-done / danger-900 …) tak, aby ink `#e5e7eb` držel ≥ 4,5:1 a stroke ≥ 3:1.

Tokeny patří do katalogu `--tm-*`. `--stencil-fill` / `--stencil-stroke` zůstávají výchozí (paper); stencily s vlastní barvou je přepisují inline přes `TmDiagramStencilShape` (`--stencil-fill:{fill}`), takže `var(--tm-archimate-…-fill)` projde.

### 2.1 Sdílené ink / stroke

| Token | Light | Dark | Kontrast light | Kontrast dark | Role |
|---|---|---|---|---|---|
| `--tm-diagram-node-ink-on-fill` | `#4b5563` | `#e5e7eb` | ≥ 5,77:1 na všech světlých výplních (R1; ~6:1 na `#cfe8ff`) | ≥ 4,5:1 na všech tmavých výplních | Text na barevné výplni (canvas). `--tm-diagram-node-ink` se **nemění**. |
| `--tm-diagram-stencil-stroke-on-fill` | `#1f2937` | `#e5e7eb` | 11–14:1 na světlých výplních; 14,7:1 na bílém papíře | ≥ 8:1 na tmavých výplních; 12:1 na `#27272a` | Obrys ArchiMate / Extended (dnes literál `#1f2937`). |

**Dvojí hodnota on-fill ink (R3, výslovně):**

| Povrch | Light | Dark |
|---|---|---|
| Canvas CSS `--tm-diagram-node-ink-on-fill` | `#4b5563` (dnešní viditelná barva popisku) | `#e5e7eb` |
| `DiagramSvgPalette.Light/Dark` slot `OnFillText` (a `Tokens["--tm-diagram-node-ink-on-fill"]`) | `#111827` (dnešní Light export, `DefaultText`) | `#e5e7eb` |

Light canvas tedy zůstane `#4b5563`, Light SVG export `#111827`. Změna exportu na `#4b5563` by porušila (d) a vyžaduje zvláštní schválení — **nenavrhujeme ji**.

### 2.2 ArchiMate 3 (`Archimate3DiagramStencilProvider`)

| Token | Light (dnes) | Dark | Rodina | Ink light (`#4b5563`) | Ink dark (`#e5e7eb`) |
|---|---|---|---|---|---|
| `--tm-archimate-business-fill` | `#fff2b8` | `#78350f` | amber-900 | 6,71:1 | 7,33:1 |
| `--tm-archimate-application-fill` | `#cfe8ff` | `#1e3a8a` | primary-900 | 5,99:1 | 8,37:1 |
| `--tm-archimate-technology-fill` | `#d9f2d0` | `#14532d` | success-900 / status-done | 6,32:1 | 7,36:1 |
| `--tm-archimate-motivation-fill` | `#eadcff` | `#4c1d95` | violet-900 | 5,82:1 | 8,85:1 |
| `--tm-archimate-strategy-fill` | `#ffe1c2` | `#9a3412` | orange-900 | 6,05:1 | 5,90:1 |
| `--tm-archimate-physical-fill` | `#d9f2d0` | `#14532d` | stejná rodina jako technology (light hex je totožný) | 6,32:1 | 7,36:1 |
| `--tm-archimate-implementation-fill` | `#ffd6e7` | `#9d174d` | pink-900 | 5,77:1 | 6,37:1 |
| `--tm-archimate-cross-fill` | `#eef2f7` | `#3f3f46` | zinc-700 | 6,72:1 | 8,44:1 |

Stroke všech elementů: `--tm-diagram-stencil-stroke-on-fill`. `TextSection.TextStyle.Color` = `var(--tm-diagram-node-ink-on-fill)`.

Physical má **vlastní název** i při stejném hexu jako technology, aby sémantika vrstvy zůstala a hostitel mohl vrstvy přebarvit zvlášť.

### 2.3 BuiltIn — rodiny, ne řádky

Brief R6: 13 **dvojic** Fill+Stroke (26 řádků), ne 13 míst. Řádek 218 (`#fef08a`) je jiný tón než `#fefce8` → vlastní yellow token.

| Token | Light | Dark | Použití (stencil id) |
|---|---|---|---|
| `--tm-diagram-stencil-yellow-fill` | `#fef08a` | `#713f12` | `general.sticky-note` |
| `--tm-diagram-stencil-yellow-stroke` | `#ca8a04` | `#facc15` | `general.sticky-note` |
| `--tm-diagram-stencil-red-fill` | `#fef2f2` | `#7f1d1d` | `project.risk`, `project.issue`, `strategy.swot-weakness` |
| `--tm-diagram-stencil-red-stroke` | `#dc2626` | `#fca5a5` | dtto |
| `--tm-diagram-stencil-red-ink` | `#991b1b` | `#fecaca` | text těch uzlů (dnešní `TextStyle.Color`) |
| `--tm-diagram-stencil-blue-fill` | `#eff6ff` | `#1e3a8a` | `project.assumption`, `ba.assumption`, `strategy.swot-opportunity` |
| `--tm-diagram-stencil-blue-stroke` | `#2563eb` | `#93c5fd` | dtto |
| `--tm-diagram-stencil-blue-ink` | `#1e40af` | `#bfdbfe` | dtto |
| `--tm-diagram-stencil-orange-fill` | `#fff7ed` | `#9a3412` | `project.constraint`, `strategy.swot-threat` |
| `--tm-diagram-stencil-orange-stroke` | `#ea580c` | `#fdba74` | dtto |
| `--tm-diagram-stencil-orange-ink` | `#9a3412` | `#fed7aa` | dtto |
| `--tm-diagram-stencil-green-fill` | `#f0fdf4` | `#14532d` | `project.budget`, `strategy.swot-strength` |
| `--tm-diagram-stencil-green-stroke` | `#16a34a` | `#86efac` | dtto |
| `--tm-diagram-stencil-green-ink` | `#166534` | `#bbf7d0` | dtto |
| `--tm-diagram-stencil-amber-fill` | `#fefce8` | `#78350f` | `ba.gap`, `strategy.bmc-value-proposition` |
| `--tm-diagram-stencil-amber-stroke` | `#ca8a04` | `#facc15` | dtto |
| `--tm-diagram-stencil-amber-ink` | `#854d0e` | `#fde68a` | dtto |

`general.sticky-note` nemá `TextStyle.Color` → dostane `--tm-diagram-node-ink-on-fill`.

**Light stroke 1.4.11 (R7, k potvrzení):** dnešní `#ca8a04` na `#fef08a` je ~2,52:1, na `#fefce8` ~2,84:1 a na bílém papíře ~2,94:1 — **pod 3:1 už v 2.12.0**. Zvednutí stroke by porušilo (d). Kontrastní test proto v **light** připouští právě tyto dvě historické dvojice (yellow/amber stroke × fill a × paper); v **dark** musí všechny stroke páry držet ≥ 3:1 (naměřeno 4,3–6,5:1). Text 4,5:1 platí bez výjimky.

### 2.4 Extended — všechny pevné výplně, nejen ř. 39

Brief jmenuje `#f8fafc` a stroke `#1f2937`. Soubor má **18 unikátních fill hexů**. Přijímací kritérium („žádný literální hex ve Fill / Stroke / `TextStyle.Color`“) pokrývá všechny. Tokeny podle rodiny; kde light hex už má BuiltIn token, Extended ho sdílí.

| Token | Light | Dark | Poznámka |
|---|---|---|---|
| `--tm-diagram-stencil-slate-fill` | `#f8fafc` | `#334155` | flowchart process (ř. 39) |
| `--tm-diagram-stencil-slate-muted-fill` | `#f1f5f9` | `#334155` | flowchart off-page |
| `--tm-diagram-stencil-cyan-fill` | `#ecfeff` | `#164e63` | flowchart terminator |
| `--tm-diagram-stencil-amber-100-fill` | `#fef3c7` | `#78350f` | ERD entity, cloud database, k8s volume |
| `--tm-diagram-stencil-amber-200-fill` | `#fde68a` | `#713f12` | ERD weak-entity |
| `--tm-diagram-stencil-lime-fill` | `#ecfccb` | `#3f6212` | ERD attribute |
| `--tm-diagram-stencil-lime-strong-fill` | `#d9f99d` | `#3f6212` | ERD key-attribute |
| `--tm-diagram-stencil-sky-fill` | `#e0f2fe` | `#0c4a6e` | C4 person, cloud compute, k8s cluster |
| `--tm-diagram-stencil-blue-100-fill` | `#dbeafe` | `#1e40af` | C4 software-system, k8s namespace |
| `--tm-diagram-stencil-green-100-fill` | `#dcfce7` | `#14532d` | C4 container, k8s pod |
| `--tm-diagram-stencil-yellow-100-fill` | `#fef9c3` | `#713f12` | C4 / cloud object-storage |
| `--tm-diagram-stencil-teal-fill` | `#ccfbf1` | `#115e59` | cloud serverless |
| `--tm-diagram-stencil-indigo-fill` | `#e0e7ff` | `#312e81` | cloud LB, k8s service |
| `--tm-diagram-stencil-indigo-50-fill` | `#eef2ff` | `#312e81` | cloud VPC, k8s ingress |
| `--tm-diagram-stencil-pink-fill` | `#fce7f3` | `#9d174d` | cloud queue |
| (reuse) `--tm-diagram-stencil-orange-fill` | `#fff7ed` | `#9a3412` | flowchart decision |
| (reuse) `--tm-diagram-stencil-blue-fill` | `#eff6ff` | `#1e3a8a` | flowchart data |
| (reuse) `--tm-diagram-stencil-amber-fill` | `#fefce8` | `#78350f` | flowchart document, k8s config |
| (reuse) `--tm-diagram-stencil-green-fill` | `#f0fdf4` | `#14532d` | C4 component, k8s deployment |

Stroke: `--tm-diagram-stencil-stroke-on-fill`. Text: `--tm-diagram-node-ink-on-fill`. Ink light na všech těchto výplních ≥ 6,07:1.

### 2.5 Allowlist výjimek (test a)

Test hlídá `Layout.Fill`, `Layout.Stroke` a `Sections[].TextStyle.Color`. Prázdný důvod v allowlistu = fail.

Navržené výjimky (jen pokud by se sken rozšířil):

| Místo | Důvod |
|---|---|
| `general.sticky-note` `IconSvg` `fill='#fef08a'` | 16px toolbox ikona, ne canvas node. Mimo sken Fill/Stroke/Color. |
| `table.basic` cell `BackgroundColor = "#f3f4f6"` | Chrome buňky tabulky, ne `Layout.Fill`. Mimo sken. |

Výchozí allowlist pro Fill/Stroke/Color je **prázdný**.

## 3. Architektonická rozhodnutí R1–R8

### R1 — Light ink se nesmí změnit

Navržený light `#1f2937` by ztmavil popisek (dnes `#4b5563`) a porušil (d). `--tm-diagram-node-ink-on-fill` light = `#4b5563`. Naměřeno ≥ 5,77:1 na všech světlých výplních včetně extra Extended. Žádný light textový pár nepadá pod 4,5:1.

### R2 — Stroke v exportu

`--tm-diagram-node-stroke` v Light paletě mapuje na `DefaultNodeStroke` = `#111827`, ne `#1f2937`. Canvas alias `--tm-canvas-paper-ink` v light **je** `#1f2937`, takže canvas sedí a export ne. Řešení: `--tm-diagram-stencil-stroke-on-fill` s vlastním slotem / záznamem v `Tokens` (Light `#1f2937`, Dark `#e5e7eb`). Týká se i BuiltIn barevných stroke (každý má vlastní token rodiny).

### R3 — Export textu

ArchiMate `TextSection` dnes nemá `TextStyle.Color` → export bere `DefaultText` (`#111827` Light / `#e5e7eb` Dark), canvas bere CSS `--tm-diagram-node-ink`. Po změně: `TextStyle.Color = "var(--tm-diagram-node-ink-on-fill)"` jen u stencilů na barevné výplni. Light paleta mapuje tento token na `#111827` (dnešní export), CSS na `#4b5563`. Viz tabulka v §2.1.

### R4 — Druhý dark blok (light island)

`tokens-dark.css` `[data-theme="light"]` (~ř. 430+) resetuje canvas/stencil tokeny na light hodnoty, jinak by tmavá výplň seděla na světlém papíře. Nové tokeny se deklarují v **obou** blocích; v islandu = light hex (stejný výraz jako `tokens.css`, protože `ThemeLightIslandTests.LightBlock_ValuesAreTheLightRootValues` vyžaduje byte-shodu s `:root`). Test (b) měří i island variantu (vlastní graf z island bloku, `ThemeCss.TokenGraph(true)` island záměrně ignoruje).

### R5 — Modeling a PaintSlot

`ModelingViewerProjection.ViewerTokens` dnes nese jen heat/diff. `ReplaceTokens` přepisuje `node.Style.*`, ne `Layout.Fill` stencilu — výplň v exportu resolvuje Builder. Nové tokeny patří do `ViewerTokens`, aby JS `resolveTokens` vrátil živé hodnoty (probe uvnitř rootu, logika beze změny). Builder zná nové názvy přes `palette.Tokens` (slovník token → hex), ne nabobtnalý switch. `StripUnresolvedTokens` sahá jen na `node.Style.*`; stencil `Layout.Fill` jako `var(--…)` nechá Builderu. Neznámý token dál `SlotDefault`.

Aditivní API palety:

```csharp
/// <summary>
/// Token name (including the leading <c>--</c>) to concrete colour for this theme.
/// Used by the headless SVG builder to resolve <c>var(--token)</c> paints the shipped
/// stencils emit. Unknown names still fall back to the paint-slot default.
/// Additive: existing palette fields keep their 2.12.0 values.
/// </summary>
public IReadOnlyDictionary<string, string> Tokens { get; init; }
    = new Dictionary<string, string>(StringComparer.Ordinal);
```

Light/Dark statické instance naplní slovník; stávající `Background`, `DefaultNodeFill`, … se nemění.

### R6 — Počty míst s hexem

BuiltIn: 13 dvojic Fill+Stroke (26 řádků) + 12 `TextStyle.Color` na týchž uzlech (kromě sticky-note). Extended: 18 unikátních fill hexů + 1 stroke. ArchiMate: 8 fill konstant (physical sdílí hex s technology) + 1 stroke v `Element()`.

### R7 — Historický light stroke yellow/amber pod 3:1

Viz §2.3. Neměnit light hex. Dark splní 3:1. Test (b) má allowlist jen pro tyto light stroke páry.

### R8 — Print

`@media print .tm-diagram-editor` (TG-004 B1) musí nové tokeny přepnout na light hodnoty. Custom property se substituuje tam, kde je deklarovaná; bez re-deklarace by tisk z dark hostitele vynesl tmavé výplně na bílý papír. Žádný `[data-theme=dark]` blok — jen print, analogicky ke stávajícím `--tm-canvas-*` / `--stencil-*`.

## 4. Implementační kontrakt

1. Tokeny v `tokens.css` (komentář kontrastu jako u `--tm-paper-ink-muted`) a v obou blocích `tokens-dark.css`.
2. Stencily: konstanty `"var(--tm-…)"` místo hexu; `TextStyle.Color` on-fill / family-ink.
3. `DiagramSvgPalette.Tokens` + `DiagramSvgBuilder.ResolveNodePaint` čte slovník před switch/SlotDefault.
4. `ViewerTokens` rozšířit o všechny nové názvy.
5. Bundle `tempo-blazor.bundled.css`, `node scripts/audit-css-tokens.mjs`.
6. Print re-pin (R8).
7. Host `node.Style.*` a heat overlay beze změny.

`modeling-editor.js` `resolveTokens` je generický podle jmen — bez změny logiky.

## 5. Testy

- (a) `DiagramStencilThemedFillTests` — žádný literální hex ve Fill/Stroke/`TextStyle.Color` u Archimate3, BuiltIn, Extended. Allowlist s neprázdným důvodem.
- (b) `DiagramStencilFillContrastTests` — páry ze stencilů (fill token × ink token sekce), light / dark / light-island, text ≥ 4,5:1, stroke ≥ 3:1 (R7 light yellow/amber).
- (c) Dark export `archimate3.application.component`: fill = dark hodnota application tokenu, text `#e5e7eb`.
- (d) Light export beze změny: fill `#cfe8ff`, stroke `#1f2937`, text `#111827`. Totéž pro `project.risk` a Extended `#f8fafc`.
- Host `node.Style.Fill` a heat `var(--tm-color-heat-*)` vyhrají.
- `ViewerTokens` obsahuje nové názvy.
- E2E axe `color-contrast` na `g.tm-diagram-node` (ArchiMate, BPMN, DiagramEditor, light i dark). Screenshoty `__screenshots__/fze-8c/`.

Mutace: vrátit `"#cfe8ff"` u application fill → (a) i (b) RED; vrátit literál stroke → (a) RED; odebrat token z Dark palety → (c) RED; zrušit deklaraci v light islandu → (b) RED.

## 6. Hostitelé (není Breaking / Migration)

- Light vzhled 2.12.0 beze změny.
- Vlastní stencil pack s pevným hexem si dark řeší sám; může přejít na tyto tokeny.
- Hostitelé, kteří kvůli kontrastu přepisovali `--tm-diagram-node-ink`, ho mohou odstranit — on-fill ink je teď na uzlu s barevnou výplní.
- `DiagramSvgPalette` jen aditivní vlastnost `Tokens`; existující defaulty se nemění.

## 7. Mimo rozsah

- Backlog PH `3ea75759-…`.
- BPMN stencily (už tokenové, TG-004).
- Změna `--tm-diagram-node-ink` / `--tm-canvas-paper-ink-muted`.
- Ikony toolboxu (`IconSvg`) a barvy buněk tabulky.
