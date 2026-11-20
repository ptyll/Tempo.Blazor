# Návrh: zadávání času na telefonu a na vnějším displeji Z Flip 5

> **Stav:** návrh k připomínkám (architektura + UX review). **Nic z toho není implementováno.**
> **Cílový release:** první minor po 2.12.0 (pracovně **2.13.0**). Do 2.12 nic z tohoto dokumentu nepatří.
> **Rozsah:** jen `Tempo.Blazor` (komponenty, CSS, JS modul, lokalizace) a `Tempo.Blazor.Abstractions`
> (modely předvoleb). Všechny změny veřejného API jsou **aditivní**.

## 0. Shrnutí rozhodnutí

| Otázka | Rozhodnutí |
|--------|------------|
| Nová komponenta, nebo režim `TmTimePicker`? | **Obojí, rozdělené podle odpovědnosti.** Nová veřejná komponenta **`TmTimeSelector`** = samotná výběrová plocha (inline použitelná kdekoli). `TmTimePicker` dostane aditivní parametr **`Presentation`**, který tu plochu skládá do `TmOverlayPanel` (popover / bottom sheet). Výchozí `Presentation=Segments` = dnešní chování beze změny. |
| Primární vzor | **Segmentovaný vstup s velkými +/− (APG `spinbutton`)** + přímé psaní, doplněný **gridem hodin/minut** a **řadou předvoleb**. |
| Zamítnuté jako primární | Wheel/drum (konflikt se scrollem, přístupnost, motion, výška 374 px ukáže ~3 řádky), ciferník (tažení, WCAG 2.5.7, malé cíle, slabá přístupnost). |
| Layout | Z vlastního kontejneru (`TmLayoutObserver`) + jemný krok „čtverec“ měřený JS modulem (šířka **i výška**), protože konvence nedovoluje `@container` pod 640 px. |
| Sheet vs. popover | Podle `overlays.md`: rozhoduje **viewport** (`MobilePresentation=Auto`), velikost plochy rozhoduje **kontejner**. |
| Klávesy | Vlastní JS modul `tm-time-selector.js` (vzor `tm-otp-input.js`): Blazor drží model, prohlížeč klávesy. |

---

## 1. Problém a cíle

### 1.1 Dnešní stav (prostudováno)

- **`TmTimePicker`** (`Components/Pickers/TmTimePicker.razor`) je `role="group"` nad **`TmTimeInput`**
  (`TmTimeInput.razor`): dva/tři `<input type="number">` šířky `2.5rem`, `font-size-sm`, padding
  `space-1`. Žádný popup, žádné předvolby, žádný krok minut, žádný 12h režim.
- UX nálezy na telefonu:
  1. **Cíle pod 44 px** — segment je ~40×28 px, `(pointer: coarse)` pravidlo v `_time-input.css` chybí
     (porušení `responsive-conventions.md` → Touch, WCAG 2.5.8 / interní minimum 44 px).
  2. **`type="number"`** otevře na Androidu numerickou klávesnici, ale na Flex Window zabere klávesnice
     většinu plochy a pole samotné zmizí pod ní; spinner šipky jsou skryté CSS, takže na dotyku
     neexistuje žádné krokování.
  3. **Tichá ztráta vstupu:** `TmTimeInput.Commit` mimo `MinTime`/`MaxTime` hodnotu **zahodí** bez
     zprávy (pole se po re-renderu vrátí). `TmTimePicker` sice umí `--invalid`, ale k němu se hodnota
     z vlastního vstupu nikdy nedostane — jen z bindingu.
  4. Hodina se nenormalizuje podle kultury (vždy `D2`, vždy 24h), `en-US` uživatel nemá AM/PM.
  5. Chybí `ReadOnly`, `AriaDescribedBy`, `ValueExpression` → nenapojí se na `EditContext`
     (`NotifyFieldChanged`) ani na kaskádu `TmFormField.ErrorIdCascadingName` /
     `DescribedByIdsCascadingName`, takže FluentValidation zpráva není přes `aria-describedby`
     přiřazena (to `TmTextInput`/`TmDecimalInput` už umí).
- **`TmTimeRangePicker`** a **`TmDateTimePicker`** skládají stejný `TmTimeInput` — zdědí všechny vady
  i budoucí opravy. `TmDateTimePicker` už ukazuje správný vzor popupu:
  `TmOverlayPanel Role="dialog" TrapFocus="true" MobilePresentation="PanelPresentation.Auto"`.
- **`TmOtpInput`** je referenční vzor pro segmentovaný vstup: JS modul vlastní klávesy, `beforeinput`,
  paste/autofill, jeden polite live region, `ReadOnly`, `AriaDescribedBy`, 44 px na coarse pointeru.
- **`TmSegmentedControl`** — roving tabindex, nativní Enter/Space, `Role=Tabs` s `aria-controls`.
  Použijeme ho pro přepínač AM/PM a pro přepínač „Hodiny | Minuty“ gridu (nic nového nepíšeme).
- **`TmOverlayPanel` / `TmDrawer` / `TmSidePanel`** — sheet = `TmDrawer Position=Bottom` (content
  height, max 85 % visual viewportu, `--tm-sheet-viewport` / `--tm-sheet-keyboard`), header s **Done**
  (`Tm_Done`) a `SheetDoneEnabled`; `TmSidePanel` je pro inspektory, **pro picker se nehodí**
  (není kotvený k triggeru) — nepoužijeme ho.

### 1.2 Cílový viewport: vnější displej Galaxy Z Flip 5 (Flex Window)

| Veličina | Hodnota | Poznámka |
|----------|---------|----------|
| Úhlopříčka | 3,4" | |
| Fyzické rozlišení | 720 × 748 px | téměř čtverec (poměr 1 : 1,04) |
| DPR | ~1,875–2 | závisí na nastavení „velikost obsahu“ |
| **CSS viewport** | **~360 × 374 až ~384 × 400** | rozsah ze zadání; budeme testovat 360×374, **374×374**, 380×400 |
| Výška pro obsah v prohlížeči | **~300–320 CSS px** | minus lišta prohlížeče (~56 px); v PWA/standalone ~350 |
| Sheet (85 % visual viewportu) | **~255–300 px** | `TmDrawer.DefaultMaxHeight = 0.85` |
| Výřez | **levý dolní roh** (dvě kamery, displej je obtéká jako „složka“) | odhad ~70 × 50 CSS px nepoužitelné plochy; `env(safe-area-inset-*)` ho spolehlivě **nehlásí** — ověřit na zařízení |
| Klávesnice | Samsung Keyboard na cover screenu zakryje ~60–70 % výšky | přímé psaní je zde nouzová cesta, ne primární |
| Ovládání | jedna ruka, palec dosáhne celé plochy; horní okraj je nejméně pohodlný | |

> Měření je z veřejných specifikací (web search v této session nebyl dostupný); **Fáze 0** ho ověří na
> fyzickém zařízení (`window.innerWidth/innerHeight`, `visualViewport`, `devicePixelRatio`, poloha výřezu).

**Rozpočet výšky pro primární úkol bez scrollu: ≤ 256 px** (nejhorší případ sheetu). Viz výpočet v 3.1.

### 1.3 Cíle

1. Nastavit čas **≤ 3 tapy** pro běžný případ (předvolba / „Teď“ / zaokrouhlený čas), **≤ 6 tapů**
   pro libovolný čas po 5 min, na ploše 374 × 374 bez scrollování.
2. Všechny cíle ≥ 44 × 44 CSS px na coarse pointeru, jednoruční ovládání, nic důležitého v levém
   dolním rohu.
3. Plně ovladatelné klávesnicí (desktop i hardwarová klávesnice na tabletu), čtečkou obrazovky
   (TalkBack, VoiceOver, NVDA) a bez tažení (WCAG 2.5.7).
4. 12/24h a oddělovač podle kultury; 4 jazyky (en, cs, sk, fr).
5. Žádná duplikace: sheet/popover = `TmOverlayPanel`, přepínače = `TmSegmentedControl`, gesta =
   `pointer-drag.js`, množné číslo = `Loc.Plural`, hodiny = `TimeProvider`.
6. Zpětná kompatibilita: existující `TmTimePicker`/`TmTimeInput`/`TmTimeRangePicker`/
   `TmDateTimePicker` call-sity renderují stejný DOM, dokud se neopt-inují.

### 1.4 Ne-cíle

- Výběr časového pásma, výběr data, výběr trvání (`TimeSpan` > 24 h).
- Úprava `TmTimeRangePicker` / `TmDateTimePicker` v první vlně (adopce je Fáze 6).
- Persistování „nedávných“ časů komponentou (data patří hostiteli — viz otevřené otázky).

---

## 2. Srovnání vzorů

Hodnocení 1–5 (5 = nejlepší). „Flex“ = plocha ~360×374 / ~260 px sheet.

| Vzor | Flex Window | Jedna ruka | Přístupnost | Rychlost | Poznámky |
|------|:-:|:-:|:-:|:-:|----------|
| **Wheel / drum** (iOS) | 2 | 4 | 1 | 3 | Ve 260 px výšky jsou vidět ~3 řádky na sloupec, setrvačnost je *motion* (reduced-motion ji musí vypnout → zbude jen skok), scroll-snap koliduje se swipe-to-dismiss sheetu a se scrollem stránky; čtečky neumí scroll-snap sloupec ohlásit jako hodnotu → musel by být stejně `spinbutton` pod povrchem. Na desktopu (myš/kolečko) nepohodlný. Vyžaduje JS fyziku = druhá implementace gesta (zakázáno v `touch-interaction.md`). |
| **Segmenty s +/−** (APG spinbutton) | **5** | **5** | **5** | 3 | Jeden řádek `[−] 14 [+] : [−] 30 [+]` se vejde do 316 px šířky a 56 px výšky. `role="spinbutton"` je nativně podporovaný vzor (šipky, Home/End, PageUp/Down, `aria-valuetext`). Pomalý pro velké skoky → doplnit gridem a podržením (auto-repeat). |
| **Ciferník** (Material) | 2 | 3 | 1 | 4 | Kruh pro 12 cílů potřebuje průměr ≥ 220 px, aby měl cíl 44 px → nezbude místo na nic dalšího; 24h dvojitý kruh má vnitřní cíle < 44 px. Primárně tažení (WCAG 2.5.7 alternativa nutná). Pro čtečky se stejně degraduje na seznam. |
| **Grid hodin / minut** | 4 | 4 | 4 | **5** | 24 hodin = mřížka 6×4 s buňkou ≥ 48 × 44 → 288 × 176 px; minuty po 5 = 4×3. Rychlé (2 tapy na čas), ale grid hodin + grid minut současně se do čtverce **nevejde** → přepínat „Hodiny | Minuty“. Roving tabindex, 2D šipky. |
| **Přímé psaní s maskou** | 2 | 2 | 4 | **5** (klávesnice) | Nejrychlejší na desktopu a s HW klávesnicí; na Flex Window klávesnice zakryje pole. Maska musí zvládnout `1430`, `14:30`, `14.30`, `14h30`, `2:30 pm`. |
| **Předvolby + „Teď“ + nedávné** | **5** | **5** | 4 | **5** | 1 tap pro nejčastější případy (schůzky, směny). Vodorovná řada chipů 44 px; musí mít text, ne jen ikonu. Neřeší libovolný čas → vždy jen doplněk. |

**Závěr:** žádný vzor sám nestačí. Doporučená kombinace:

- **Jádro (vždy):** segmentový readout se spinbuttony a +/− → přesný, přístupný, nejmenší výška.
- **Zrychlení (podle místa):** grid (telefon/tablet/desktop; na čtverci jako druhý „krok“ po tapu na
  segment), předvolby (všude, pokud je host dodá nebo zapne „Teď“).
- **Psaní:** desktop / HW klávesnice přímo do segmentů (typeahead buffer); na dotyku přepínač
  „Zadat z klávesnice“ (`inputmode="numeric"` pole s maskou).

---

## 3. Doporučený návrh

### 3.1 Architektura a rozhodnutí „nová komponenta vs. režim“

```
TmTimePicker (existující, aditivní)          TmTimeSelector (NOVÁ, veřejná)
 ├─ Presentation=Segments (default) ──► TmTimeInput (beze změny DOM)
 └─ Presentation=Popup | Auto
      ├─ trigger <button> (hodnota/placeholder)
      └─ TmOverlayPanel Role=dialog TrapFocus MobilePresentation=Auto
            └─ TmTimeSelector  ◄─────────────── lze použít i inline (karta, sheet hostitele,
                 ├─ readout: spinbuttony + +/−          Flex-Window „widget“, scheduler quick-create)
                 ├─ TmSegmentedControl (AM/PM, Hodiny|Minuty)
                 ├─ grid (hodiny / minuty po kroku)
                 └─ předvolby (chipy)
TimeOfDayFormat (internal helper, čisté C#): hour cycle, parse, format, step, min/max, DST
tm-time-selector.js: klávesy spinbuttonů, typeahead, auto-repeat, (volitelně) swipe
```

**Proč ne jen nový režim `TmTimePicker`:** výběrová plocha má vlastní DOM, JS modul, layouty a stav
(draft, aktivní segment, grid). Kdyby žila jen uvnitř pickeru, nešla by použít inline (Flex Window
„jedna obrazovka = jeden úkol“, sheet hostitele, scheduler) a `TmDateTimePicker`/`TmTimeRangePicker`
by ji musely znovu obalovat přes picker s labelem a clear tlačítkem, které nechtějí.

**Proč ne úplně nový picker (`TmMobileTimePicker`):** zdvojil by label, clear, `MinTime`/`MaxTime`,
`Required`, `HideClear`, integraci s formulářem a dokumentaci; uživatel knihovny by musel volit mezi
dvěma pickery se stejnou hodnotou `TimeOnly?`. Jeden vstupní bod pro formulář (`TmTimePicker`) + jedna
znovupoužitelná plocha (`TmTimeSelector`) = stejné dělení jako `TmDateTimePicker` → `TmCalendarGrid`.

### 3.2 Layouty podle kontejneru

Strukturální mód: `TmLayoutObserver` na kořeni `TmTimeSelector` (`data-layout` mobile/tablet/desktop).
Jemný krok **„square“** uvnitř mobile: konvence (`TmBreakpointsTests`) nepovoluje `@container`
šířku < 640, a čtverec je definovaný hlavně **výškou**. Proto ho měří `tm-time-selector.js`
(ResizeObserver na kořen + `visualViewport.height` když je uvnitř sheetu) a hlásí .NET jen při změně;
**Blazor** zapíše `data-tm-time-fit="square|regular"` (skript nikdy nepíše atributy layoutu sám — stejné
pravidlo jako `layout-observer.js`). Práh: `inline-size < 640 && dostupná výška < 480`
(konstanta v C#, ne v CSS). Bez JS (prerender) = `regular`, který na malé ploše jen scrolluje.

| Fit / mód | Kde typicky | Obsah |
|-----------|-------------|-------|
| **square** (mobile + nízká výška) | Flex Window, telefon na šířku | readout s +/−, AM/PM, 1 řada rychlých minut **nebo** předvoleb; grid až po tapu na segment (nahradí řadu) |
| **mobile regular** | běžný telefon (390×844) | readout, přepínač Hodiny\|Minuty, grid, předvolby |
| **tablet** | 640–1023 | readout + oba gridy vedle sebe, předvolby vlevo |
| **desktop** | ≥ 1024 | popover: readout + kompaktní grid + předvolby; nebo `Segments` inline s typeahead |

Prezentace pickeru (`Presentation=Auto`): **sheet na mobile viewportu**, popover jinak (viewport
rozhoduje, viz `overlays.md` pravidlo 5). Inline `TmTimeSelector` řeší jen svůj kontejner.

#### Square — Flex Window 374 × 374, sheet (~256 px)

```
┌──────────────────────────────────────┐  ← sheet top (grabber 16 px, dekorativní)
│ ▔▔▔▔                                 │
│ Začátek směny                 [Hotovo]│  44 px  (TmDrawer header, Done vpravo nahoře)
├──────────────────────────────────────┤
│                                      │
│  [ − ]  14  [ + ]  :  [ − ]  30  [ + ]│  56 px  spinbuttony, cíle 44×44, číslo 28 px
│                                      │
│        ( 07:00 ) ( 12:00 ) ( Teď ) ⟩  │  44 px  předvolby, vodorovný scroll, zarovnané
│                                      │         VPRAVO → levý dolní roh (kamery) zůstává prázdný
│ ░░░░░                                │  ← rezerva výřezu (--tm-time-selector-cutout-*)
└──────────────────────────────────────┘
 výška: 16 + 44 + 8 + 56 + 12 + 44 + 16 (padding) = 196 px  (< 256 px rozpočet, rezerva 60 px)
 šířka: 4×44 + 2×56 (hodnota) + 16 (":") + 2×8 mezery + 2×12 padding = 344 px (< 360)
```

Tap na číslo **14** (nebo **30**) přepne spodní řadu na grid daného segmentu (nahradí předvolby, výška
grid 4×3 minut = 3 × 44 + 2×4 = 140 px → celkem ~292 px; na 255 px sheetu jediná pasáž, kde se
**plocha scrolluje** — přijatelné, protože readout a Hotovo zůstanou nahoře a grid je volitelná zkratka):

```
┌──────────────────────────────────────┐
│ Začátek směny                 [Hotovo]│
│  [ − ]  14  [ + ]  :  [ − ] ▐30▌ [ + ]│  aktivní segment podtržený (ne jen barva)
│  ┌────┬────┬────┬────┐               │
│  │ 00 │ 05 │ 10 │ 15 │   ← s MinuteStep=5: 12 buněk 4×3, buňka 64×44
│  ├────┼────┼────┼────┤               │
│  │ 20 │ 25 │▐30▌│ 35 │               │
│  ├────┼────┼────┼────┤               │
│  │ 40 │ 45 │ 50 │ 55 │               │
│  └────┴────┴────┴────┘   [ ⌨ ] [ ↺ předvolby ]
└──────────────────────────────────────┘
```

12h kultura (`en-US`) na čtverci: `[−] 2 [+] : [−] 30 [+]` + pod tím `TmSegmentedControl` **AM | PM**
(44 px, `FullWidth=false`, zarovnané vpravo) — předvolby se posunou do gridu/toggle.

#### Mobile regular — telefon 390 × 844, sheet

```
┌────────────────────────────────────────┐
│ ▔▔▔▔                                   │
│ Čas schůzky                    [Hotovo]│
├────────────────────────────────────────┤
│   [ − ]   14   [ + ]  :  [ − ]  30  [ + ]│
│                                        │
│   ┌ Hodiny ┬ Minuty ┐  (TmSegmentedControl Role=Tabs)
│   ┌───┬───┬───┬───┬───┬───┐            │
│   │ 00│ 01│ 02│ 03│ 04│ 05│  grid 24 h, 6×4, buňka 52×48
│   │ 06│ 07│ 08│ 09│ 10│ 11│            │
│   │ 12│ 13│▐14▌│15│ 16│ 17│            │
│   │ 18│ 19│ 20│ 21│ 22│ 23│            │
│   └───┴───┴───┴───┴───┴───┘            │
│   Rychlá volba                          │
│   ( Teď ) ( 09:00 ) ( 13:00 ) ( 17:30 ) │
│   Nedávné:  ( 08:15 ) ( 16:45 )         │
│                          [ ⌨ Zadat ]    │
└────────────────────────────────────────┘
```

Po výběru hodiny v gridu se přepínač **automaticky** přesune na Minuty (jako Material), focus jde na
vybranou minutu; čtečka dostane jedno oznámení (viz 4.3).

#### Tablet — popover (~420 px šířka)

```
            [ 14:30  🕑 ]  ← trigger
┌──────────────────────────────────────────────┐
│ [ − ] 14 [ + ] : [ − ] 30 [ + ]              │
│ Hodiny                     Minuty            │
│ ┌──┬──┬──┬──┬──┬──┐        ┌──┬──┬──┬──┐     │
│ │00│01│02│03│04│05│        │00│05│10│15│     │
│ │..│..│..│..│..│..│        │20│25│30│35│     │
│ └──┴──┴──┴──┴──┴──┘        │40│45│50│55│     │
│ ( Teď ) ( 09:00 ) ( 13:00 )                  │
└──────────────────────────────────────────────┘
```

#### Desktop

Default zůstává inline `Segments` (dnešní DOM) — rychlé psaní. S `Presentation=Popup`:

```
Čas  [ 14 : 30 ]  [🕑]          ← segmenty + tlačítko otevřou popover (segmenty zůstávají editovatelné)
     ┌───────────────────────────┐
     │ ( Teď ) (09:00) (13:00)   │
     │ Hodiny: 6×4 grid          │
     │ Minuty: 4×3 grid          │
     └───────────────────────────┘
```

Pozn.: popover na desktopu má `TrapFocus=true` (modální dialog, stejné jako kalendář
`TmDateTimePicker`) — cena popsaná v `overlays.md` („Modality of dialog popups“) platí.

### 3.3 Interakce

| Vstup | Chování |
|-------|---------|
| **Tap +/−** | ±1 h / ±`MinuteStep` min. Bez min/max hodina **cyklí** 23→0, minuta 55→00 **nepřenáší** do hodiny (předvídatelné; viz otevřené otázky). S min/max se tlačítko na hranici **disabluje** (`disabled` + zůstává v pořadí? ne — nativní disabled; hodnota se oznámí). |
| **Podržení +/−** | auto-repeat po 400 ms, pak 8/s, po 1,5 s zrychlí na skok po 5×krok; `pointercancel`/`pointerup`/blur zastaví. Reduced motion **nemá** vliv (není to animace). |
| **Tap na číslo segmentu** | vybere segment (podtržení + `aria-pressed` ne — je to spinbutton s focusem) a ukáže jeho grid. |
| **Tap v gridu** | nastaví segment; po hodině přeskočí na minuty. Buňky mimo `MinTime`/`MaxTime` jsou `aria-disabled="true"` (zůstávají v roving pořadí, mají `aria-describedby` s důvodem). |
| **Tap předvolby** | nastaví celý čas; v sheetu s `CommitMode=OnConfirm` jen draft, s `Live` rovnou commit. |
| **Swipe** (opt-in `EnableSwipe`, Fáze 5) | svislý swipe po čísle segmentu: každých 24 px = 1 krok (nahoru = +). `pointer-drag.js`, `threshold: 6`, `touch-action: none` **jen** na čísle; `onCancel` vrátí hodnotu z začátku gesta. Swipe sheetu začíná na grabberu/headeru (`tm-sheet.js`), takže nekoliduje. Není jedinou cestou (WCAG 2.5.7: +/−). |
| **Klávesnice — spinbutton** | ↑/↓ ±krok, PageUp/PageDown ±(hodina: 3 h? minuta: 15 min — rozhodnout), Home/End = min/max segmentu, ←/→ přesun mezi segmenty, číslice = typeahead (`1`→čeká 1 s nebo druhou číslici; `3`-`9` u hodin v 24h rovnou posune na minuty), `:` `.` `h` = další segment, `a`/`p` = AM/PM, Backspace = smazat segment. `preventDefault` jen pro tyto klávesy v JS (Blazor `@onkeydown:preventDefault` je per-element, spolkl by Tab — stejný důvod jako u `attachKeyboardMove`). |
| **Klávesnice — grid** | roving tabindex, ←→↑↓ 2D, Home/End řádek, Ctrl+Home/End celý grid; Enter/Space **nativní** `<button>` (`keyboard-activation-convention.md`, žádná emulace). |
| **Enter v readoutu** | v popoveru/sheetu = Hotovo (commit + zavřít); yield jednoho tasku před zavřením (vzor `TmCommandPalette`, aby `keypress` nedopadl na obnovený trigger). |
| **Escape** | zavře popover/sheet (overlay.js / drawer), focus zpět na trigger; draft se zahodí (viz otevřené otázky č. 3). |
| **Hardwarová klávesnice na tabletu / DeX** | stejné jako desktop; `Presentation=Auto` na coarse pointeru + HW klávesnici stále otevře sheet, ale readout dostane focus, takže psaní funguje okamžitě. |
| **Přímé psaní na dotyku** | tlačítko „⌨ Zadat z klávesnice“ přepne readout na jedno `<input inputmode="numeric" autocomplete="off" enterkeyhint="done">` s maskou; Done/Enter parsuje (`TimeOfDayFormat.TryParse`), chyba = `aria-invalid` + text, **vstup se nezahazuje**. |
| **Paste** | do readoutu i do pole: `14:30`, `1430`, `14.30`, `14h30`, `2:30 PM`, `2:30p`, `14:30:15` (sekundy jen při `ShowSeconds`). |

### 3.4 Krokování minut

- `MinuteStep` (`int`, default `1`), povolené dělitele 60: 1, 2, 3, 4, 5, 6, 10, 12, 15, 20, 30;
  jiná hodnota → `ArgumentOutOfRangeException` v `OnParametersSet` (stejně jako `TmOtpInput.Length`).
- +/− a šipky krokují po `MinuteStep`; grid ukazuje násobky `GridMinuteStep` (default
  `max(MinuteStep, 5)`, takže při kroku 1 má grid 12 buněk, ne 60).
- Hodnota mimo krok (z bindingu nebo psaním 14:07 při kroku 15): zobrazí se přesně; první +/− ji
  **zarovná** na nejbližší násobek ve směru kroku (14:07 → + → 14:15, − → 14:00). `EnforceStep=true`
  zarovná i psaný vstup a hodnotu z bindingu označí jako nevalidní (otevřená otázka č. 4).
- „Teď“ se zaokrouhlí na krok **nahoru** (14:07, krok 15 → 14:15) z `TimeProvider.GetLocalNow()`.

### 3.5 12/24 h podle kultury

`HourCycle` (`TimeHourCycle`: `Auto` default, `H23`, `H12`).
`Auto` = `Culture ?? CultureInfo.CurrentCulture`; 12h právě když `DateTimeFormat.ShortTimePattern`
obsahuje `h` (ne `H`). Rozhoduje **kultura, ne jazyk**: `en-US` 12h, `en-GB` 24h, `cs`/`sk`/`fr` 24h.

- Oddělovač: `DateTimeFormat.TimeSeparator` (vizuálně, `aria-hidden`).
- AM/PM: `AMDesignator`/`PMDesignator` kultury v `TmSegmentedControl`; prázdný designator (některé
  kultury) → fallback na lokalizační klíč `TmTimeSelector_Am`/`_Pm`.
- 12h hodiny 12, 1…11; hodnota modelu je vždy `TimeOnly` (24h). Grid hodin v 12h = 12 buněk 4×3
  (vejde se i do čtverce).
- Typeahead v 12h: `1` čeká na druhou číslici (10/11/12), `2`–`9` posune na minuty.

### 3.6 Min / max a neplatné časy

- `MinTime`/`MaxTime` (stávající parametry) — inkluzivní. **Nová sémantika pro plochu:** grid buňky a
  +/− mimo rozsah disabled; psaný/vložený čas mimo rozsah se **zobrazí a označí** (`aria-invalid`,
  zpráva `TmTimeSelector_OutOfRange` „Čas musí být mezi {0} a {1}“), **necommituje se** a nezmizí.
  Tím se opraví tichá ztráta v `TmTimeInput` — ale jen v nové ploše; `Presentation=Segments` zachová
  dnešní chování (oprava `TmTimeInput` je samostatné rozhodnutí, otázka č. 7).
- Rozsah přes půlnoc (`MinTime=22:00`, `MaxTime=06:00`, noční směna): dnes `TmTimePicker` vyhodnotí
  jako „vše neplatné“. Návrh: `AllowOvernightRange` (`bool`, default `false`) → interval se čte jako
  `[22:00, 24:00) ∪ [00:00, 06:00]` (otázka č. 5).
- Minuty v hraniční hodině: při `MinTime=09:15` a hodině 9 jsou minuty 00–10 disabled; při změně hodiny
  na hranici se minuta **neposouvá automaticky**, ale hodnota se označí — posun pod rukou mate.

### 3.7 DST

`TimeOnly` nemá datum, takže DST je problém jen v kontextu konkrétního dne a pásma. Návrh (opt-in,
Fáze 4): `ReferenceDate` (`DateOnly?`) + `TimeZone` (`TimeZoneInfo?`). Když jsou oba zadané:

- **Neexistující čas** (jarní posun, `Europe/Prague` 31. 3. 02:00–02:59): buňky disabled s popisem
  `TmTimeSelector_DstGap` („V tento den čas {0}–{1} neexistuje (posun na letní čas).“), psaný čas
  označen jako nevalidní.
- **Dvojznačný čas** (podzim, 02:00–02:59 dvakrát): readout ukáže inline volbu
  `TmSegmentedControl` „Letní čas (UTC+2) | Zimní čas (UTC+1)“; výsledek se hlásí přes
  `AmbiguousOffsetChanged` (`EventCallback<TimeSpan>`), protože `TimeOnly` offset neunese.
  Default `AmbiguousTimeResolution.Earlier`.
- `TmDateTimePicker` (Fáze 6) to předá automaticky ze svého data; samotný `TmTimePicker` bez data
  DST neřeší a dokumentuje to.

### 3.8 Předvolby a nedávné

- `Presets` (`IReadOnlyList<TimePreset>?`), nový record v Abstractions
  `TimePreset(string Label, TimeOnly Time)` — vedle `DateRangePreset`. Label je od hostitele
  (lokalizuje si ho sám); `null` label → zobrazí se naformátovaný čas.
- `ShowNowPreset` (`bool`, default `false`) — chip „Teď“ (`TmTimeSelector_Now`), čas z injektovaného
  `TimeProvider` (fallback `TimeProvider.System`, vzor `DateRangePresets.Create`).
- `RecentTimes` (`IReadOnlyList<TimeOnly>?`) — host-owned; komponenta nic nepersistuje. Pomocné
  `OnCommitted` umožní hostiteli si poslední hodnotu uložit. Max 3 zobrazené, deduplikované proti
  předvolbám.
- Chipy = nativní `<button>` s textem, `aria-pressed` když odpovídají hodnotě; skupina
  `role="group" aria-label="Rychlá volba"`. Předvolba mimo min/max je `aria-disabled`.

---

## 4. Přístupnost a lokalizace

### 4.1 Role a ARIA

| Prvek | Role / atributy |
|-------|-----------------|
| kořen `TmTimeSelector` | `role="group"`, `aria-labelledby` (label pickeru / `AriaLabelledBy`) nebo `aria-label` (fallback `TmTimeSelector_AriaLabel` „Výběr času“), `aria-describedby` = `AriaDescribedBy` + kaskáda `TmFormField` (error/help) + vlastní chyba |
| segment hodin/minut/sekund | `role="spinbutton"`, `tabindex="0"`, `aria-label` (`TmTimePicker_Hours` …, existující klíče), `aria-valuenow` (invariantní číslo, v 12h 1–12), `aria-valuemin`/`max` (efektivní dle min/max), **`aria-valuetext`** lokalizovaný a s množným číslem („14 hodin“, „2 PM“, „30 minut“), `aria-invalid`, `aria-readonly` při `ReadOnly`, `aria-disabled` při `Disabled` |
| +/− | nativní `<button type="button">`, `aria-label` „Přidat hodinu“ / „Ubrat minutu“, `aria-controls` = id segmentu, `tabindex="-1"` (spinbutton je ten tab stop; tlačítka jsou pro dotyk — čtečky používají šipky na spinbuttonu; **otevřené k ověření s TalkBack**, kde swipe gesta tlačítka potřebují → alternativně ponechat v tab pořadí, otázka č. 8) |
| AM/PM | `TmSegmentedControl` `Role=RadioGroup`, `AriaLabel` „Část dne“ |
| přepínač Hodiny\|Minuty | `TmSegmentedControl` `Role=Tabs`, `PanelId` = id gridu |
| grid | `role="tabpanel"` s `aria-labelledby` tabem; uvnitř `role="radiogroup"` řádků? → **jednodušeji:** kontejner `role="radiogroup"` + nativní `<button role="radio" aria-checked>` s roving tabindex a 2D šipkami (jako `TmSegmentedControl`, jen 2D) |
| trigger pickeru | `<button aria-haspopup="dialog" aria-expanded aria-controls>`, `aria-labelledby` = label + zobrazená hodnota (vzor `TmDateTimePicker.TriggerLabelledBy`) |
| popover / sheet | `TmOverlayPanel Role="dialog" TrapFocus="true"`, `Title`/`SheetTitle` = label (pravidlo 3 — každý sheet je pojmenovaný), `SheetInitialFocusSelector` = aktivní spinbutton (ne Done) |

### 4.2 Fokus

- Otevření: fokus na spinbutton hodin (nebo naposledy upravovaný segment) přes `OnOpened`, ne na Done.
- Zavření (Hotovo, Escape, backdrop, swipe, Enter): fokus zpět na trigger (`overlays.md` pravidlo 4).
- Přepnutí Hodiny→Minuty po výběru v gridu přesune fokus do minutového gridu na vybranou buňku.
- Viditelný fokus: dvouvrstvý ring knihovny; v gridu, který scrolluje, inset outline (důvod viz
  `segmented-control.md` „Focus visibility“).

### 4.3 Live region

**Jeden** vždy renderovaný `role="status" aria-live="polite"` (vzor `TmOtpInput`), prázdný na prvním
renderu. Oznamuje jen to, co čtečka jinak neslyší:

- výsledný čas po tapu na +/−, buňku gridu, předvolbu („Nastaveno 14:30“, `TmTimeSelector_Set`),
  **debounce 500 ms** při auto-repeatu (jedno oznámení na konci série);
- chybu (mimo rozsah, DST mezera) — clear-then-set pro opakovanou stejnou chybu.
- **Neoznamuje** změny ze šipek na spinbuttonu (čtečka čte `aria-valuetext` sama → dvojí hlášení).

### 4.4 Cíle, motion, forced colors, kontrast

- Všechny interaktivní prvky ≥ `var(--tm-touch-target)` (44 px) na `(pointer: coarse)` **a** ve fit
  `square` bez ohledu na pointer (Flex Window). Desktop fine pointer: ≥ 32 px (WCAG 2.5.8 ≥ 24 px).
  Mezery mezi +/− a číslem ≥ 8 px proti mis-tapu.
- Reduced motion: žádné animované přechody gridu/indikátoru (indikátor řeší `TmSegmentedControl`);
  nový animovaný subjekt se musí zapsat do reduced-motion bloku `animations.css`
  (`ReducedMotionSelectorTests`). Wheel setrvačnost neexistuje (zamítnuto).
- Forced colors: aktivní segment = `Highlight`/`HighlightText` + podtržení 2 px `CanvasText`
  (stav nesený tvarem, ne barvou), invalid = 2 px `CanvasText` rámeček (vzor `TmOtpInput`),
  disabled buňky `GrayText`.
- Kontrast: čísla readoutu `--tm-text-primary` na `--tm-bg-surface`, disabled buňky ≥ 3:1 proti
  pozadí (nejsou „neviditelné“), aktivní stav nikdy jen barvou (WCAG 1.4.1).
- Zoom 200 % / text spacing: na čtverci readout zalomí minuty pod hodiny (flex-wrap), nic se neořízne.
- Orientace: Flex Window je fixně na výšku; telefon na šířku spadne do `square` a funguje stejně.

### 4.5 Lokalizace (en / cs / sk / fr)

Nové klíče (vše do 4 JSON + `MockTmLocalizer`; `TmResourceCompletenessTests` hlídá úplnost
a placeholdery; množná čísla podle `localization-plurals.md`):

| Klíč | en | cs | sk | fr |
|------|----|----|----|----|
| `TmTimeSelector_AriaLabel` | Time picker | Výběr času | Výber času | Sélecteur d'heure |
| `TmTimeSelector_IncreaseHour` / `_DecreaseHour` | Add an hour / Remove an hour | Přidat hodinu / Ubrat hodinu | Pridať hodinu / Ubrať hodinu | Ajouter une heure / Retirer une heure |
| `TmTimeSelector_IncreaseMinute` / `_DecreaseMinute` | Add {0} min / Remove {0} min | Přidat {0} min / Ubrat {0} min | Pridať {0} min / Ubrať {0} min | Ajouter {0} min / Retirer {0} min |
| `TmTimeSelector_HourValue_*` (plural) | {0} o'clock | {0} hodina/hodiny/hodin | {0} hodina/hodiny/hodín | {0} heure/heures |
| `TmTimeSelector_MinuteValue_*` (plural) | {0} minute(s) | {0} minuta/minuty/minut | {0} minúta/minúty/minút | {0} minute(s) |
| `TmTimeSelector_DayPeriod` | Part of day | Část dne | Časť dňa | Moment de la journée |
| `TmTimeSelector_Am` / `_Pm` (fallback) | AM / PM | dop. / odp. | dop. / odp. | AM / PM |
| `TmTimeSelector_HoursTab` / `_MinutesTab` | Hours / Minutes | (reuse `TmTimePicker_Hours`/`_Minutes`) | | |
| `TmTimeSelector_Presets` | Quick picks | Rychlá volba | Rýchla voľba | Choix rapides |
| `TmTimeSelector_Now` | Now | Teď | Teraz | Maintenant |
| `TmTimeSelector_Recent` | Recent | Nedávné | Nedávne | Récents |
| `TmTimeSelector_TypeTime` / `_UseControls` | Type the time / Use buttons | Zadat z klávesnice / Použít tlačítka | Zadať z klávesnice / Použiť tlačidlá | Saisir l'heure / Utiliser les boutons |
| `TmTimeSelector_Set` | Set to {0} | Nastaveno {0} | Nastavené {0} | Réglé sur {0} |
| `TmTimeSelector_OutOfRange` | The time must be between {0} and {1} | Čas musí být mezi {0} a {1} | Čas musí byť medzi {0} a {1} | L'heure doit être comprise entre {0} et {1} |
| `TmTimeSelector_InvalidFormat` | Enter a time such as {0} | Zadejte čas ve tvaru {0} | Zadajte čas v tvare {0} | Saisissez une heure comme {0} |
| `TmTimeSelector_DstGap` | On this day {0}–{1} does not exist (clocks go forward) | V tento den čas {0}–{1} neexistuje (posun na letní čas) | V tento deň čas {0}–{1} neexistuje (posun na letný čas) | Ce jour-là, {0}–{1} n'existe pas (passage à l'heure d'été) |
| `TmTimeSelector_DstAmbiguous` | This time occurs twice. Which one? | Tento čas nastane dvakrát. Který? | Tento čas nastane dvakrát. Ktorý? | Cette heure se produit deux fois. Laquelle ? |
| `TmTimePicker_OpenSelector` | Choose time | Vybrat čas | Vybrať čas | Choisir l'heure |
| `TmTimePicker_SheetTitle` (fallback bez labelu) | Time | Čas | Čas | Heure |

Formátování všech zobrazených časů: `TimeOnly.ToString(pattern, culture)` s patternem odvozeným
z `HourCycle` a kultury (ne `"D2"` natvrdo); `aria-valuenow` vždy invariantní. Ukázka formátu ve
`_InvalidFormat` = `ShortTimePattern` kultury aplikovaný na 14:30.

---

## 5. Návrh API (vše aditivní)

### 5.1 `TmTimeSelector` (nová, `Tempo.Blazor.Components.Pickers`)

| Parametr | Typ | Default | Popis |
|----------|-----|---------|-------|
| `Value` / `ValueChanged` | `TimeOnly?` / `EventCallback<TimeOnly?>` | `null` | Two-way. `null` = nic; readout ukáže `--`, +/− začne od „Teď“ zaokrouhleného na krok (nebo `MinTime`). |
| `ValueExpression` | `Expression<Func<TimeOnly?>>?` | `null` | Doplní `@bind-Value`; `FieldIdentifier` pro `EditContext.NotifyFieldChanged` (vzor `TmDecimalInput`). |
| `MinTime` / `MaxTime` | `TimeOnly?` | `null` | Inkluzivní hranice. |
| `AllowOvernightRange` | `bool` | `false` | `MinTime > MaxTime` = rozsah přes půlnoc. |
| `MinuteStep` | `int` | `1` | Dělitel 60. |
| `GridMinuteStep` | `int?` | `null` | `null` = `max(MinuteStep, 5)`. |
| `EnforceStep` | `bool` | `false` | Zarovnat psaný vstup na krok. |
| `ShowSeconds` | `bool` | `false` | Třetí spinbutton; ve fit `square` bez gridu sekund. |
| `HourCycle` | `TimeHourCycle` | `Auto` | `Auto` / `H23` / `H12`. |
| `Culture` | `CultureInfo?` | `null` | `null` = `CurrentCulture`. |
| `Presets` | `IReadOnlyList<TimePreset>?` | `null` | Předvolby. |
| `ShowNowPreset` | `bool` | `false` | Chip „Teď“ z `TimeProvider`. |
| `RecentTimes` | `IReadOnlyList<TimeOnly>?` | `null` | Host-owned nedávné časy. |
| `ShowGrid` | `TimeSelectorGrid` | `Auto` | `Auto` (dle layoutu) / `None` / `Always`. |
| `AllowTyping` | `bool` | `true` | Typeahead v segmentech + přepínač „Zadat z klávesnice“ na dotyku. |
| `EnableSwipe` | `bool` | `false` | Svislý swipe na čísle (Fáze 5). |
| `ReferenceDate` / `TimeZone` | `DateOnly?` / `TimeZoneInfo?` | `null` | DST kontext; obojí nutné. |
| `AmbiguousTimeResolution` | `AmbiguousTimeResolution` | `Earlier` | Výchozí volba dvojznačného času. |
| `AmbiguousOffsetChanged` | `EventCallback<TimeSpan>` | — | Zvolený offset dvojznačného času. |
| `Disabled` | `bool` | `false` | Bez tab stopů, `aria-disabled`. |
| `ReadOnly` | `bool` | `false` | Fokusovatelné, `aria-readonly`, +/−/grid/předvolby skryté (ne jen disabled — méně šumu na čtverci). **Nikdy se nepřiřazuje uvnitř komponenty.** |
| `Invalid` | `bool` | `false` | Externí chybový stav (z validace hostitele). |
| `ErrorText` | `string?` | `null` | Vlastní chyba; `Invalid` bez textu = chybu vlastní `TmFormField`. |
| `AriaLabel` / `AriaLabelledBy` / `AriaDescribedBy` | `string?` | `null` | Pojmenování/popis skupiny. |
| `LayoutMode` / `InitialMode` / `ResolvedLayoutChanged` | `TmLayoutMode` / `TmLayoutMode` / `EventCallback<TmLayoutMode>` | `Auto` / `Desktop` / — | Konvence `responsive-conventions.md` (nikdy nepárovat `LayoutMode` s `ResolvedLayoutChanged`). |
| `Fit` | `TimeSelectorFit` | `Auto` | `Auto` (měří) / `Square` / `Regular` — pin pro testy a hostitele (Flex-Window aplikace si ho může vynutit). |
| `AutoFocus` | `bool` | `false` | Fokus na první segment po prvním renderu. |
| `OnCommitted` | `EventCallback<TimeOnly?>` | — | Jednou na „dokončenou“ změnu (blur, Enter, výběr z gridu/předvolby, konec auto-repeatu) — vhodné pro nedávné časy a drahou validaci; `ValueChanged` je per-krok. |
| `Id` | `string` | generované | Kořen; `InputId` = spinbutton hodin (`{Id}-hours`) pro `<label for>`/`TmFormField.For`. |
| `Class`, `AdditionalAttributes` | | | `aria-*` → skupina, ostatní → kořen (vzor `TmTimePicker`). |

Veřejné metody: `FocusAsync()`, `FocusSegmentAsync(TimeSegment)`, `ShowErrorAsync()` (re-announce).
Kaskády: čte `TmFormField.ErrorIdCascadingName` a `DescribedByIdsCascadingName` (spojení přes
`TmDescribedBy.Join`), `EditContext` (validační zpráva → `aria-invalid`).

### 5.2 `TmTimePicker` (existující, aditivní parametry)

| Parametr | Typ | Default | Popis |
|----------|-----|---------|-------|
| `Presentation` | `TimePickerPresentation` | **`Segments`** | `Segments` = dnešní `TmTimeInput` (DOM beze změny); `Popup` = segmenty + tlačítko 🕑 otevírající `TmTimeSelector` v `TmOverlayPanel`; `Auto` = na mobile viewportu trigger-tlačítko + sheet, jinak `Popup`. |
| `CommitMode` | `TimeCommitMode` | `Auto` | `Live` (každý krok → `ValueChanged`) / `OnConfirm` (draft, Hotovo commituje) / `Auto` = sheet `OnConfirm`, popover `Live`. |
| `SheetTitle` | `string?` | `null` | Fallback `Label` → `TmTimePicker_SheetTitle`. |
| `ValueExpression` | `Expression<Func<TimeOnly?>>?` | `null` | Napojení na `EditContext` + `NotifyFieldChanged` při commitu. |
| `ReadOnly` | `bool` | `false` | Hodnota viditelná, nelze upravit, trigger neotevírá (`aria-readonly` na skupině; segmenty `readonly`). |
| `AriaDescribedBy` | `string?` | `null` | Explicitní popis; spojí se s kaskádou `TmFormField`. |
| `Id` / `InputId` | `string?` | `null` | Stabilní id (dnes jen generované). |
| `OnCommitted` | `EventCallback<TimeOnly?>` | — | Předává z `TmTimeSelector` (a z blur segmentů). |
| pass-through | | | `MinuteStep`, `GridMinuteStep`, `HourCycle`, `Culture`, `Presets`, `ShowNowPreset`, `RecentTimes`, `ShowGrid`, `EnableSwipe`, `AllowOvernightRange`, `ReferenceDate`, `TimeZone`, `LayoutMode`, `InitialMode`, `ResolvedLayoutChanged` — mají efekt jen mimo `Segments` (kromě `HourCycle`/`Culture`, viz otázka č. 7). |

Pozn.: `TimePickerPresentation.Auto` není default, protože by změnil DOM všech existujících
call-sitů na telefonu (E2E screenshoty, selektory `.tm-time-seg`). Default lze přehodnotit v major.

### 5.3 Nové typy (Abstractions / Pickers)

```csharp
// Tempo.Blazor.Abstractions/Models/TimePreset.cs
public sealed record TimePreset(string? Label, TimeOnly Time);

// Tempo.Blazor (Components.Pickers)
public enum TimePickerPresentation { Segments, Popup, Auto }
public enum TimeCommitMode { Auto, Live, OnConfirm }
public enum TimeHourCycle { Auto, H23, H12 }
public enum TimeSelectorGrid { Auto, None, Always }
public enum TimeSelectorFit { Auto, Square, Regular }
public enum TimeSegment { Hours, Minutes, Seconds, DayPeriod }
public enum AmbiguousTimeResolution { Earlier, Later }
```

`TimeOfDayFormat` (internal static, čisté C#): `ResolveHourCycle`, `Format`, `TryParse`,
`Step(TimeOnly, segment, delta, step, min, max, overnight)`, `IsInRange`, `SnapToStep`,
`GetDstGap(date, tz)`, `IsAmbiguous(date, time, tz)`.

### 5.4 Použití

```razor
@* Formulář, telefon i desktop — opt-in nové plochy *@
<TmFormField Label="Začátek směny" For="@_startId" ValidationFor="() => model.Start">
    <TmTimePicker @bind-Value="model.Start" Id="@_startId"
                  Presentation="TimePickerPresentation.Auto"
                  MinuteStep="15" ShowNowPreset="true"
                  Presets="_shiftPresets" RecentTimes="_recent"
                  OnCommitted="RememberAsync" />
</TmFormField>

@* Flex-Window aplikace: celá obrazovka = jeden úkol, inline *@
<TmTimeSelector @bind-Value="_alarm" Fit="TimeSelectorFit.Square"
                AriaLabel="Budík" MinuteStep="5" Presets="_alarmPresets" />
```

FluentValidation: žádná změna balíčku — díky `ValueExpression` + `NotifyFieldChanged` funguje
`FluentValidationValidator` stejně jako u `TmDecimalInput`; zpráva přes `TmFormField` →
`aria-describedby`. Validace se spouští v `OnCommitted`-bodě (Hotovo / blur), ne per-krok, pokud je
`CommitMode=OnConfirm`; v `Live` per `ValueChanged` (jako dnes).

---

## 6. Plán testů a fáze

### 6.1 bUnit (`tests/Tempo.Blazor.Tests/Pickers/`)

- `TimeOfDayFormatTests` (čisté C#, `[Theory]`): hour cycle pro `en-US`, `en-GB`, `cs`, `sk`, `fr`,
  `fr-CA`; parse matice (`1430`, `14:30`, `14.30`, `14h30`, `2:30 PM`, `2:30p`, `12:00 AM`→00:00,
  `24:00` odmítnout, prázdné); step + wrap + clamp; snap; overnight; DST `Europe/Prague` 2026-03-29
  a 2026-10-25 (gap / ambiguous), pásmo bez DST.
- `TmTimeSelectorTests`: role/aria (`spinbutton`, valuenow/min/max/text, plural text cs 1/2/5 h),
  +/− commit **přesně jednou** (Enter = `KeyDown`+`Click`, Space = `KeyDown`+`KeyUp`+`Click`
  podle `keyboard-activation-convention.md`), disabled na hranici, grid `aria-disabled` mimo rozsah,
  předvolby + „Teď“ s `FakeTimeProvider`, `ReadOnly` (žádné ovládací prvky, `aria-readonly`, nikdy
  nevolá `ValueChanged`), `Disabled`, `Invalid`/`ErrorText`, `AriaDescribedBy` + `TmFormField`
  kaskáda error/help, `EditContext.NotifyFieldChanged`, `MinuteStep` validace (výjimka), `Fit=Square`
  vs `Regular` markup, 12h AM/PM přes `TmSegmentedControl`, live region prázdný na prvním renderu
  a jedno oznámení po tapu.
- `TmTimePickerTests` (rozšíření): **default `Segments` renderuje identický DOM** (snapshot / selektor
  `.tm-time-seg`), `Popup`/`Auto` renderuje trigger s `aria-haspopup/expanded`, `TmOverlayPanel`
  s `Role=dialog TrapFocus`, `MobilePresentation`, `LayoutMode=Mobile` → sheet (`PanelPresentation.Sheet`
  testovatelné bez DOM), `CommitMode` draft vs live, Hotovo commituje / Escape zahazuje, `ReadOnly`
  neotevírá.
- Lokalizace: `TmResourceCompletenessTests` (automaticky), `MockTmLocalizer` doplnit en + cs.
- CSS: nové třídy a tokeny existují (vzor `TmDocumentOpenDialogCssTests`), žádná `@container`
  šířka mimo 640/768/1024/1280 (`TmBreakpointsTests`), reduced-motion selektory
  (`ReducedMotionSelectorTests`), coarse-pointer 44 px pravidlo.

### 6.2 Node (`npm run test:core-modules` — přidat soubor explicitně do `scripts/run-node-tests.mjs`)

`src/Tempo.Blazor/wwwroot/js/__tests__/tm-time-selector.test.mjs`:
typeahead buffer (24h/12h, timeout, `3`→auto-advance), `preventDefault` jen pro obsluhované klávesy
(Tab nikdy), auto-repeat (start 400 ms, akcelerace, stop na `pointercancel`/`pointerup`/`blur`/detach),
fit měření (šířka+výška → hlášení jen při změně, degenerovaný box ignorovat), swipe mapování
dy→kroky přes stub `pointer-drag.js` a rollback na cancel, idempotentní attach/detach.

### 6.3 E2E (Playwright, demo `/time-input`, WASM + Server; vybrané Smoke)

| Viewport | Zařízení | Co se ověří |
|----------|----------|-------------|
| **374 × 374**, `hasTouch`, `isMobile`, DPR 2 | Flex Window (square) | `data-tm-time-fit="square"`; sheet se vejde bez scrollu (readout + předvolby + Hotovo v `visualViewport`); každý cíl ≥ 44×44 (`boundingBox`); levý dolní roh 70×50 bez interaktivního prvku; 14:30 nastavitelné ≤ 6 tapy; grid po tapu na segment dosažitelný. |
| 360 × 374, 380 × 400 | Flex Window rozsah | totéž (hranice rozpočtu) |
| 374 × 300 | Flex Window s lištou prohlížeče | sheet 85 % → scroll jen v gridu, Hotovo viditelné |
| 390 × 844, touch | telefon | sheet, auto-přepnutí Hodiny→Minuty, klávesnicový režim (`inputmode`), `--tm-sheet-keyboard` nezakryje pole |
| 768 × 1024, touch | tablet | popover, oba gridy |
| 1280 × 800 | desktop | `Segments` default beze změny; `Popup`: klávesnice end-to-end (typeahead, šipky, Enter, Escape → fokus na trigger) |

Navíc: axe (všechny viewporty, light/dark), `emulateMedia({ reducedMotion: 'reduce' })`,
`forcedColors: 'active'` (screenshot pod `__screenshots__/time-input/`), 4 kultury (cs 24h,
en-US 12h AM/PM, sk, fr), skutečný touch drag pro swipe (Fáze 5), toast/`TmModal` stacking
(sheet otevřený uvnitř modalu zůstává v jeho trapu — `overlays.md`). Ruční ověření na fyzickém
Z Flip 5 (TalkBack, Samsung Internet + Chrome) je **akceptační kritérium** Fáze 3.

### 6.4 Fáze

| Fáze | Obsah | Výstup |
|------|-------|--------|
| **0 — Rozhodnutí** | Odpovědi na otevřené otázky, měření na zařízení (viewport, výřez, klávesnice), ADR `docs/adr/00xx-time-selector.md`. | ADR, potvrzené rozpočty |
| **1 — Jádro** | `TimeOfDayFormat`, `TimePreset`, enumy, lokalizační klíče (4 jazyky + mock), bUnit pro helper. TDD. | bez UI změn |
| **2 — `TmTimeSelector` readout** | spinbuttony, +/−, AM/PM, `tm-time-selector.js` (klávesy, typeahead, auto-repeat, fit), live region, ReadOnly/Disabled/Invalid, FormField/EditContext, CSS `_time-selector.css` (tokeny, coarse 44 px, forced colors, reduced motion), fit square/regular. | inline komponenta, demo sekce |
| **3 — Grid + předvolby** | grid hodin/minut (roving 2D), přepínač `TmSegmentedControl Role=Tabs`, předvolby/„Teď“/nedávné, layouty mobile/tablet/desktop. **E2E 374×374 + ověření na zařízení.** | plná plocha |
| **4 — `TmTimePicker` integrace** | `Presentation` Popup/Auto přes `TmOverlayPanel`, `CommitMode`, `ValueExpression`, `ReadOnly`, `AriaDescribedBy`, `Id`, DST (`ReferenceDate`/`TimeZone`), overnight. | opt-in picker |
| **5 — Swipe + leštění** | `EnableSwipe` přes `pointer-drag.js`, klávesnicový režim na dotyku, výkon (render gate při auto-repeatu), docs `docs/time-selector.md`, AGENTS.md/COMPONENTS.md, Wireframe stencil/role (`BuiltInComponentSchemas`, `time-picker` role), CHANGELOG. | release 2.13.0 |
| **6 — Adopce (samostatný plán)** | `TmDateTimePicker` (DST z data), `TmTimeRangePicker` (dva selektory, trvání), Scheduler quick-create; případná oprava tiché ztráty v `TmTimeInput`. | další minor |

---

## 7. Otevřené otázky pro vlastníka

1. **Pojmenování:** `TmTimeSelector` (plocha) + `TmTimePicker.Presentation` — souhlas? Alternativa
   `TmTimeDial` je zavádějící (žádný ciferník), `TmTimeSpinner` zužuje na spinbutton.
2. **Default `Presentation`:** ponechat `Segments` (nulová změna) až do příštího majoru, nebo už
   v 2.13 přepnout na `Auto` jen pro `(pointer: coarse)`? Návrh: ponechat `Segments`.
3. **Zavření sheetu bez Hotovo** (Escape/backdrop/swipe): zahodit draft (návrh, „zrušit“ jako u
   nativních pickerů), nebo ponechat jako `TmDateRangePicker` (partial výběr zůstane)?
4. **Hodnota mimo krok** z bindingu při `EnforceStep=true`: označit jako nevalidní, nebo tiše zarovnat
   a vyvolat `ValueChanged`? Návrh: označit, nikdy nepřepisovat model bez akce uživatele.
5. **Rozsah přes půlnoc** (`AllowOvernightRange`) — potřebujeme ho v 2.13 (směny), nebo až s
   `TmTimeRangePicker`?
6. **Přetékání minut do hodiny** (14:55 + 5 min → 15:00, nebo 14:00?): návrh **ne** přetékat (segmenty
   nezávislé jako APG/iOS); Material přetéká. Rozhodnout jednotně i pro šipky.
7. **Oprava `TmTimeInput`** (tichá ztráta mimo min/max, chybějící 44 px na coarse, `HourCycle`
   v segmentech): udělat v 2.13 jako bugfix (mění chování existujících call-sitů), nebo nechat na
   Fázi 6? Návrh: 44 px coarse-pointer pravidlo ano (čistě CSS, bezpečné), chování až s adopcí.
8. **+/− v tab pořadí?** Návrh je `tabindex=-1` (spinbutton je tab stop, méně zastávek); TalkBack
   „swipe to next“ ale fokusovatelné prvky nepotřebuje a tlačítka čte — potvrdit testem s TalkBack
   a VoiceOver na zařízení.
9. **Nedávné časy:** striktně host-owned (návrh), nebo volitelný `RecentKey` s `localStorage`?
10. **Square breakpoint:** stačí JS měření `data-tm-time-fit` (návrh), nebo zavést knihovní
    `TmBreakpoints.Xs` (např. 400 px) + výškový krok pro všechny komponenty (dotkne se
    `TmBreakpointsTests` a konvence)? Flex Window se bude týkat i dalších komponent.
11. **Výřez kamer:** Flex Window ho přes `env(safe-area-inset-*)` nehlásí. Stačí knihovní konvence
    „nic interaktivního vlevo dole ve fit square“ + custom property
    `--tm-time-selector-cutout-block/inline` (návrh), nebo chceme obecný token
    `--tm-cover-screen-cutout-*` pro celou knihovnu?
12. **Sekundy na čtverci:** povolit `ShowSeconds` ve fit square (readout pak 3×(44+56+44) = 432 px
    → zalomení na dva řádky), nebo sekundy ve square nepodporovat?
13. **PageUp/PageDown kroky:** hodiny ±3 h? minuty ±15 min (nebo ±`GridMinuteStep`×3)? APG nechává
    na autorovi.

## 8. Rozhodnutí vlastníka (ptyll, 2026-11-20)

Implementace začne hned po vydání 2.12.0 (v rámci 2.13), spolu s opravou všech nalezených vad `TmTimeInput`.

| # | Otázka | Rozhodnutí |
|---|---|---|
| 1 | Pojmenování | Souhlas: `TmTimeSelector` (plocha) + `TmTimePicker.Presentation`. |
| 2 | Default `Presentation` | Ponechat `Segments`. |
| 3 | Zavření sheetu bez Hotovo | Zahodit draft („zrušit“ jako u nativních pickerů). |
| 4 | Hodnota mimo krok | Souhlas s návrhem: označit jako nevalidní, model bez akce uživatele nepřepisovat. |
| 5 | Rozsah přes půlnoc | Udělat v 2.13 a rovnou adoptovat i `TmTimeRangePicker`. |
| 6 | Přetékání minut | Souhlas s návrhem: nepřetékat (segmenty nezávislé, platí i pro šipky). |
| 7 | Oprava `TmTimeInput` | Opravit všechny nalezené vady v 2.13 (44 px coarse, tichá ztráta mimo min/max, `HourCycle`, `ReadOnly`, `AriaDescribedBy`, `ValueExpression`). |
| 8 | +/− v tab pořadí | Souhlas s návrhem (`tabindex=-1`, ověřit TalkBack/VoiceOver). |
| 9 | Nedávné časy | Striktně host-owned. |
| 10 | Square breakpoint | Stačí JS měření `data-tm-time-fit`. |
| 11 | Výřez kamer | Souhlas s návrhem (konvence + `--tm-time-selector-cutout-*`). |
| 12 | Sekundy na čtverci | Povolit `ShowSeconds` (readout se zalomí na dva řádky). |
| 13 | PageUp/PageDown | Hodiny ±3 h, minuty ±15 min. |
