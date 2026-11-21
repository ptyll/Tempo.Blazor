# ADR-0007: Zadávání času na dotyku — `TmTimeSelector` a `TmTimePicker.Presentation` (`TIME-SELECTOR`)

- **Status:** Přijato
- **Datum:** 2026-11-20
- **Kontext plánu:** „Tempo: zadávání času na telefonu a vnějším displeji Z Flip 5“ Fáze 1 plánu 1c95309c (ověření na zařízení a ADR)
- **Kód rozhodnutí:** `TIME-SELECTOR`
- **Návrhový dokument:** [docs/proposals/time-input-mobile.md](../proposals/time-input-mobile.md) (srovnání vzorů, rozpočty, API, testy)
- **Cílový release:** 2.13.0 (všechny změny veřejného API jsou aditivní)

## Kontext

`TmTimePicker` je dnes `role="group"` nad `TmTimeInput`, tedy dvěma nebo třemi `<input type="number">` šířky
`2.5rem`. Žádný popup, předvolby, krok minut ani 12h režim. Na telefonu to znamená cíle pod 44 px bez pravidla
pro `(pointer: coarse)`, žádné krokování na dotyku (spinner šipky jsou skryté), tichou ztrátu hodnoty mimo
`MinTime`/`MaxTime`, hodinu vždy ve 24h a chybějící `ReadOnly`, `AriaDescribedBy` a `ValueExpression`
(nenapojí se na `EditContext` ani na `TmFormField`). `TmTimeRangePicker` a `TmDateTimePicker` skládají stejný
`TmTimeInput`, a zdědí proto všechny vady.

Nejtěžší cílový viewport je vnější displej Galaxy Z Flip 5 (Flex Window), téměř čtvercový, s jednoruční obsluhou.
Dosavadní odhad (360 × 374, lišta ~56 px, výřez kamer vlevo dole) byl z veřejných specifikací, a proto ho
Fáze 1 plánu 1c95309c nahradila měřením na zařízení.

## Měření (Z Flip 5, vnější displej, Chrome 158, Android, portrait)

Nástroj: [tools/device-probe/viewport-probe.html](../../tools/device-probe/viewport-probe.html).

| Veličina | Naměřeno |
|----------|----------|
| `devicePixelRatio` | 2,375 |
| `screen` / `screen.avail` | 400 × 442 / 400 × 354 CSS px |
| `innerWidth` / `outerWidth` | 399 / 400 CSS px |
| Výška, lišta prohlížeče viditelná | **306 CSS px** (`innerHeight` = `visualViewport` = `dvh` = `svh`) |
| Výška, lišta skrytá po scrollu | **353 CSS px** (`innerHeight` = `dvh`; `svh` zůstává 306) |
| Výška s otevřenou SW klávesnicí | **93,5 CSS px** (`visualViewport.height`); měřeno s `interactive-widget=resizes-content`, proto i `innerHeight` = 93,5. Při výchozím `resizes-visual` zůstává `innerHeight` 306. |
| `safe-area-inset-*` | 0 na všech stranách |
| Vstup | `pointer: coarse`, `hover: none` |
| Výřez kamer | **mimo viewport**: Chrome ho do viewportu nepouští, pod viewportem je černý pás ~51 CSS px (screenshot 948 × 960 device px = 399 × 404 CSS px) |
| Zaoblení rohů viewportu | mírné spodní, odhadem ≤ 12 CSS px |

Důsledky: šířka je 399 px (ne 360), výška se za běhu mění mezi 306 a 353 px (lišta se skrývá při scrollu),
výřez viewport nezasahuje a s klávesnicí zbývá jen 93 px.

## Rozhodnutí

1. **Dvě veřejné věci rozdělené podle odpovědnosti.**
   - Nová komponenta **`TmTimeSelector`** (`Tempo.Blazor.Components.Pickers`) je samotná výběrová plocha.
     Použít ji lze i inline (karta, sheet hostitele, Flex-Window aplikace, quick-create ve scheduleru).
   - **`TmTimePicker`** dostane aditivní parametr **`Presentation`** (`Segments` / `Popup` / `Auto`). Tu plochu
     skládá do `TmOverlayPanel` (popover nebo bottom sheet). Výchozí `Presentation=Segments` je dnešní DOM beze
     změny.
2. **Primární vzor je segmentový readout s velkými +/− (APG `spinbutton`) a přímým psaním**, doplněný gridem
   hodin a minut a řadou předvoleb. Layout vychází z vlastního kontejneru (`TmLayoutObserver`). Jemný krok
   „square“ měří `tm-time-selector.js` (šířka i výška) a Blazor ho zapíše jako `data-tm-time-fit`. Konvence
   nedovoluje `@container` pod 640 px. Práh `inline-size < 640 && výška < 480` je konstanta v C#. Výška se nikdy
   nebere z block-size kořene: v sheetu je to maximální výška sheetu (85 % visualViewport.height, strop --tm-sheet-viewport), ne skutečná výška, která se řídí obsahem, inline `innerHeight` (layout viewport); hostitel si fit
   může pevně nastavit přes `Fit`. Telefon na šířku (667–932 × 360–430) má šířku ≥ 640, takže do square nespadá.
   Dostane tabletový layout a viewport s šířkou ≥ 640 znamená popover, který se v nízké výšce clampuje
   (`overlay.js`).
   **Fit se při otevřené SW klávesnici nepřepočítává.** Platí poslední fit před otevřením. Klávesnice je
   samostatný příznak `data-tm-time-keyboard` a jednořádkový readout se použije jen tehdy, když je zamčený fit
   `square`. Fit se nepřepočítává ani tehdy, když je aktivní režim „Zadat z klávesnice“ (fokus v jeho poli).
   Práh je od naměřených výšek vzdálený v sheetu 480 − 300 = 180 px, inline 480 − 353 = 127 px.
3. **Zamítnuté vzory:**
   - **Wheel / drum:** konflikt scroll-snapu se scrollem stránky a swipe-to-dismiss sheetu. Setrvačnost je
     *motion*. V 306 px výšky jsou vidět ~3 řádky. Čtečky neohlásí scroll-snap sloupec jako hodnotu, takže by
     pod povrchem musel být stejně `spinbutton`. Vyžadoval by druhou implementaci gesta, kterou
     `touch-interaction.md` zakazuje.
   - **Ciferník:** kruh pro 12 cílů po 44 px potřebuje průměr ≥ 220 px a na nic dalšího nezbyde místo. Vnitřní
     kruh 24h má cíle pod 44 px. Je to primárně tažení (WCAG 2.5.7 by vyžadoval alternativu) a pro čtečky se
     stejně degraduje na seznam.
4. **Sheet vs. popover** rozhoduje **viewport** (`MobilePresentation=Auto`, `overlays.md`), velikost plochy
   rozhoduje **kontejner**.
5. **Klávesy** vlastní JS modul `tm-time-selector.js` (vzor `tm-otp-input.js`). Blazor drží model, prohlížeč
   klávesy. Žádný `@onkeydown:preventDefault` (spolkl by Tab).

### Rozbor rozpočtů podle měření (399 × 306–353)

- Sheet = 85 % z 306 = **~260 px** (z 353 je to ~300 px) proti kompaktnímu layoutu square **196 px**, rezerva
  ~64 px. Šířka readoutu je 176 + 112 + 16 + 4×8 + 24 = 360 px. Při šířce 399 zbývá rezerva 39 px, při 374 rezerva 14 px.
- Grid po tapu na segment přepne square do **kompaktní varianty s otevřeným gridem**, která se vejde do 260 px
  bez scrollu sheetu:
  - grabber se skryje a sheet se táhne za hlavičku (`tm-sheet.js` `attachGesture`);
  - readout má 44 px, mezery jsou 4 a 8 px;
  - výpočet: 44 + 4 + 44 + 8 + 140 + 12 = 252 px ≤ 260 (rezerva 8 px, bez lišty 48 px);
  - grid minut 6×2 (92 px, buňka ≥ 55 px i při šířce 374);
  - grid hodin ve 24h 8×3 (140 px, buňka ≥ 44 px při vnitřní šířce ≥ 396); pod 396 px zůstane 6×4 a scroll je
    povolený jen uvnitř gridu;
  - grid hodin ve 12h v kompaktní variantě: grid hodin 6×2 (92 px) + AM/PM 44 px + mezera 4 px = 140 px, celkem
    252 ≤ 260.
- **ShowSeconds ve fit square:** readout se zalomí do dvou řádků po 44 px: 16 + 44 + 8 + 96 + 12 + 44 + 16 =
  236 px (rezerva 24 px). `ShowGrid=Auto` se v této kombinaci vyhodnotí jako `None`, takže zůstanou jen +/−
  a předvolby. ShowGrid=Always se ShowSeconds ve square se chová jako None (304 > 260).
- Flex Window je podle prahu **vždy `square`** a přechod 306 ↔ 353 mezi `square` a `regular` nepřepíná.
- **Výřez kamer:** v Chrome je 0. `--tm-time-selector-cutout-block/inline` zůstávají s výchozí hodnotou 0 jako
  háček pro hostitele a jiné prostředí. Odsazení dostávají jen zaoblené spodní rohy (odhadem ≤ 12 CSS px). Pravidlo „nic
  interaktivního vlevo dole“ se mění na „nic interaktivního v zaoblených rozích“.

## Rozhodnutí vlastníka

Rozhodl vlastník (ptyll), 2026-11-20, otázky 1–13 návrhu. Implementace začne po vydání 2.12.0 (v rámci 2.13).

| # | Otázka | Rozhodnutí |
|---|--------|------------|
| 1 | Pojmenování | `TmTimeSelector` (plocha) + `TmTimePicker.Presentation`. |
| 2 | Default `Presentation` | Ponechat `Segments`. |
| 3 | Zavření sheetu bez Hotovo | Zahodit draft („zrušit“ jako u nativních pickerů). |
| 4 | Hodnota mimo krok | Označit jako nevalidní, model bez akce uživatele nepřepisovat. |
| 5 | Rozsah přes půlnoc | Udělat v 2.13 (`AllowOvernightRange`) a rovnou adoptovat `TmTimeRangePicker`. |
| 6 | Přetékání minut | Nepřetékat (segmenty nezávislé, platí i pro šipky). |
| 7 | Oprava `TmTimeInput` | Opravit všechny nalezené vady v 2.13 (44 px coarse, tichá ztráta mimo min/max, `HourCycle`, `ReadOnly`, `AriaDescribedBy`, `ValueExpression`). |
| 8 | +/− v tab pořadí | `tabindex=-1`, ověřit TalkBack/VoiceOver. |
| 9 | Nedávné časy | Striktně host-owned. |
| 10 | Square breakpoint | Stačí JS měření `data-tm-time-fit`. |
| 11 | Výřez kamer | Konvence + `--tm-time-selector-cutout-*` (po měření: výchozí 0, odsazení jen zaoblených rohů). |
| 12 | Sekundy na čtverci | Povolit `ShowSeconds` (readout se zalomí na dva řádky). |
| 13 | PageUp/PageDown | Hodiny ±3 h, minuty ±15 min. |

Dvě rozhodnutí přibyla po měření (tentýž den):

| # | Otázka | Rozhodnutí |
|---|--------|------------|
| 14 | Cílový prohlížeč | **Jen Chrome.** Samsung Internet se neměří ani netestuje (E2E, ruční ověření, akceptace). |
| 15 | Zadání z klávesnice ve fit `square` | **Nenabízí se „Zadat z klávesnice“**, protože s SW klávesnicí zbude 93 px. Hardwarová klávesnice a psaní do spinbuttonů fungují dál. Segment je needitovatelný prvek (`<span role="spinbutton" tabindex="0">`); pokud by implementace použila `<input>`, platí ve fit `square` na `(pointer: coarse)` `inputmode="none"`, takže tap ani fokus SW klávesnici neotevřou. Když se SW klávesnice přesto otevře (stav `keyboard=open` je pojistka pro případ, kdy fokus přesune hostitel), zobrazí se jen jednořádkový readout bez gridu: výška 44 px + padding 2 × 8 px = 60 px ≤ 79 px. Hlavička sheetu (Hotovo) může být mimo `visualViewport`. Potvrzuje se Enterem (= Hotovo) a ruší Escapem nebo swipem. Aktivní spinbutton se posune do `visualViewport` (`scrollIntoView({block:'nearest'})`). |

## Důsledky

- Nové veřejné API je aditivní, takže cílová verze je minor **2.13.0**. Nové typy: `TmTimeSelector`,
  `TimePickerPresentation`, `TimeCommitMode`, `TimeHourCycle`, `TimeSelectorGrid`, `TimeSelectorFit`,
  `TimeSegment`, `AmbiguousTimeResolution` (`Tempo.Blazor`) a `TimePreset` (`Tempo.Blazor.Abstractions`).
  Existující `TmTimePicker` / `TmTimeInput` / `TmTimeRangePicker` / `TmDateTimePicker` renderují stejný DOM,
  dokud se `Presentation` neopt-inuje.
- **E2E viewporty (Chrome):** 399 × 306 (lišta; grid hodin i minut bez scrollu, tap na segment neotevře
  klávesnici, varianta se `ShowSeconds`), 399 × 353 (bez lišty), klávesnice a) viewport 399 × 93
  (`interactive-widget=resizes-content`) a b) viewport 399 × 306 s falešným `visualViewport.height` = 93,5
  (vzor `BottomSheetE2ETests`), 390 × 844 s falešným `visualViewport` 450 (450/844 = 0,53 < 0,6 →
  `data-tm-time-keyboard="open"` se nastaví, fit zůstane zamčený `regular`, jednořádkový readout se nepoužije
  (jen při `square`) a pole „Zadat z klávesnice“ se neodpojí). U a), b) i 390 × 844 test startuje na plné výšce,
  zmenšení viewportu (`setViewportSize`) nebo podvržení `visualViewport` a událost `resize` proběhnou až po
  attach; stav vyvolá fokus na hostitelském `<input>` na demo stránce. 844 × 390 a 667 × 375 (touch; tabletový layout, popover clampnutý do
  `visualViewport`) a 374 × 374 jako dolní mez, dále 390 × 844, 768 × 1024 a 1280 × 800. Samsung Internet se
  netestuje. Ruční ověření na fyzickém Z Flip 5 (Chrome, TalkBack) je akceptační kritérium Fáze 5 plánu
  1c95309c.
- **Klávesnice ve square:** klávesnice je otevřená, když `document.activeElement` je editovatelný prvek (uvnitř plochy i mimo ni, např. pole hostitele vedle inline selektoru) a zároveň
  `visualViewport.height` < 0,6 × největší `visualViewport.height` naměřené od attach. Pravidlo funguje v obou
  režimech `interactive-widget` a nepoužívá `innerHeight`. `tm-time-selector.js` to ohlásí a Blazor zapíše
  `data-tm-time-keyboard="open"` (samostatný příznak, ne další fit). Readout v `keyboard=open` má výšku 44 px
  a padding 2 × 8 px, celkem 60 px ≤ 79 px. Hlavička sheetu (Hotovo) může být mimo `visualViewport`; potvrzuje
  se Enterem (= Hotovo) a ruší Escapem nebo swipem. Aktivní spinbutton se posune do `visualViewport`
  (`scrollIntoView({block:'nearest'})`). Přepínač `TmTimeSelector_TypeTime` se ve fit `square` nerenderuje.
  Klávesnicový režim na dotyku (`inputmode`) platí jen mimo fit `square`.
- **Fáze plánu 1c95309c:** 1 ověření a ADR, 1a E2E harness, 2 jádro, 3 `TmTimeInput`, 4 readout, 5 grid,
  předvolby a akceptace na zařízení, 6 Presentation, 7 range, 7a dluhy, 8 swipe a docs, 9 vydání.
- Layout musí snést změnu výšky o 47 px za běhu (lišta se skrývá a vrací), bez přepnutí fitu a bez ztráty
  draftu.
- Výřez se v Chrome nekompenzuje. Tokeny `--tm-time-selector-cutout-*` jsou v klidu na 0, takže nic
  neodsazují, dokud je hostitel nenastaví. Jiný prohlížeč nebo PWA se nezaručuje (rozhodnutí 14).
- Samostatná rozhodnutí z návrhu zůstávají v platnosti: nový animovaný subjekt se zapisuje do reduced-motion
  bloku `animations.css`, jeden polite live region, `ReadOnly` se nikdy nepřiřazuje uvnitř komponenty.
- Měření platí pro Z Flip 5 v Chrome 158. Jiné zařízení se skládacím nebo cover displejem rozpočty nepotvrdí,
  ale fit `square` je odvozený z měřené výšky a šířky, takže se mu přizpůsobí.

## Zamítnuté alternativy

- **Jen nový režim `TmTimePicker`:** plocha má vlastní DOM, JS modul a stav. Uvnitř pickeru by nešla použít
  inline a `TmDateTimePicker` s `TmTimeRangePicker` by ji musely obalovat přes picker s labelem a clear
  tlačítkem, které nechtějí.
- **Úplně nový picker (`TmMobileTimePicker`):** zdvojil by label, clear, `MinTime`/`MaxTime`, `Required`,
  integraci s formulářem a dokumentaci. Uživatel by volil mezi dvěma pickery se stejnou hodnotou `TimeOnly?`.
- **Wheel / drum a ciferník** jako primární vzor (viz rozhodnutí 3).
- **Výchozí `Presentation=Auto`:** změnil by DOM všech existujících call-sitů na telefonu (E2E screenshoty,
  selektory `.tm-time-seg`). Lze přehodnotit v majoru.
- **Knihovní `TmBreakpoints.Xs` + výškový krok pro všechny komponenty:** dotkl by se `TmBreakpointsTests` a
  konvence. Stačí lokální měření `data-tm-time-fit` (rozhodnutí 10).
- **Obecný token `--tm-cover-screen-cutout-*`:** měření ukázalo, že výřez do viewportu v Chrome nezasahuje.
  Zůstává lokální `--tm-time-selector-cutout-*`.
- **Režim „Zadat z klávesnice“ ve fit `square`:** s klávesnicí zbývá 93 px, pole i Hotovo by zmizely
  (rozhodnutí 15).
- **Podpora Samsung Internet:** mimo cíl, neměří se (rozhodnutí 14).
