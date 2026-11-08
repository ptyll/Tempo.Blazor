# NotionEditor redesign: finální UX a a11y review (Fáze 13)

Plán `c8d4bcd2-40ae-4c22-8c3e-08df2362aa32`, subtask „Finální UX review všech screenshotů“.

- **Recenzent:** finální UX/a11y review, 2026-11-07.
- **Zdroj:** nejnovější fáze per povrch v `tests/Tempo.Blazor.E2E/__screenshots__/notion-*/phase-*/`.
- **Mockupy:** `output/imagegen/tempo-reimagined/Tempo.Blazor.NotionEditor/` (156 PNG = 52 obrazovek × D/T/M).

## Rozsah a metodika

- **Prohlédnuté screenshoty (~60):**
  - shell (1440/1024/768/390/320, light/dark, indigo z fáze 2)
  - metadata stránky a banner konfliktu
  - komentáře stránky, bloku a textu, potvrzení smazání
  - slash menu, menu bloku, type switcher, paleta barev, nastavení stránky, zkratky, tokeny
  - emoji a status picker
  - DB: tabulka, toolbar, seznam, galerie, board, kalendář, timeline, detail záznamu, editor pole, šablony
  - sdílení, omezení, historie, AI, média, edit modal diagramu
  - vyhledávání, šablony stránek, notifikace, úkoly, analytika, audit, blog, veřejná stránka, koš
- **Prohlédnuté mockupy (7):** editor D, page M, page-comment-panel D, color-picker D, db-table D, db-calendar M, db-timeline M, page-search D.
- **Placeholdery:** v sadě mockupů žádné nejsou. Všech 156 souborů má 0,9–1,5 MB.
- **Indigo:** varianta indigo existuje jen ve fázi 2 (`notion-editor/phase-2/shell-*-indigo`, `notion-database/phase-2/*-indigo`, `notion-panels/phase-2/*-indigo`). V pozdějších fázích chybí (viz F-24).
- **Zastaralé screenshoty (11-06, před commitem fáze 10):** `notion-share/phase-7/notion-share-*`, `notion-restrictions/phase-7/notion-restrictions-*`, `notion-history/phase-7/notion-history-*`, `notion-media/phase-7/media-*` a `notion-ai/phase-7/ai-<šířka>-*`. Hodnoceny s výhradou, je nutný re-capture.
- **Datum v DB (F-07):** screenshoty DB fáze 9 a dialogů fáze 4 vznikly 21:46:23. Oprava formátteru `74f857fb0` je z 21:46:29. Surové ISO datum je proto pravděpodobně už opravené a stačí re-capture.

## Tabulka nálezů

Závažnost: **B** = blocker, **M** = major, **m** = minor. Stav: **O** = otevřeno (oprava je na implementátorovi, tento review mění jen tento soubor).

| # | Záv. | Povrch / screenshot | Nález | Konkrétní oprava | Stav |
|---|---|---|---|---|---|
| F-01 | B | Prakticky všechny demo screenshoty: `notion-editor/phase-3/shell-*`, `notion-database/phase-8/*`, `notion-page/phase-5/conflict-*`, `phase-2/*-indigo` | Lišta `#blazor-error-ui` „An unhandled error has occurred.“ je viditelná v light i dark na 1440/1024/768/390/320. Zakrývá spodní ~60 px obsahu, takže na stránce s editorem padá runtime výjimka. | Najít výjimku: `ConsoleProbe` / log konzole při capture `NotionEditorDemo`. Do capture helperů Notion screenshotů přidat assert `#blazor-error-ui` hidden, stejně jako `SmokeLaneE2ETests` (ř. 124–128). Screenshoty s viditelnou lištou neakceptovat. | O |
| F-02 | B | `notion-menus/phase-6/type-switcher-1440-dark.png`, `-1024-dark` | Turn-into panel má v dark bílé pozadí a světlý text, takže položky jsou nečitelné (kontrast ≈1,1:1, fail WCAG 1.4.3). Příčina: `background: var(--tm-color-white)`. | `TmNotionBlockTypeSwitcher.razor.css:15` → `background: var(--tm-bg-elevated)`. Stejná chyba je v `TmNotionInlineToolbar.razor.css:10`. Přidat test do `NotionCssTokenTests`: povrch floating panelu nesmí používat `--tm-color-white`. | O |
| F-03 | M | `notion-page/phase-5/conflict-1440-*.png`, `conflict-390-light` | Tlačítka banneru konfliktu nemají padding, rámeček ani radius: „Reload server version“ je holý modrý obdélník a „Reapply“ je bez rámečku. Text se na desktopu láme do úzkého sloupce, protože 4sloupcový grid tlačí tlačítka vedle textu. Příčina: `.tm-notion-load-error__retry` je ve scoped `TmNotionEditor.razor.css`, který se na DOM v `TmNotionPage` neaplikuje. | V `TmNotionPage.razor.css` doplnit u `.tm-notion-page__aggregate-conflict .tm-notion-load-error__retry`: `padding: var(--tm-space-2) var(--tm-space-4)`, `border: var(--tm-border-width) solid var(--tm-border-color)`, `border-radius: var(--tm-radius-md)`. Grid změnit na `grid-template-columns: auto minmax(0,1fr)` a tlačítka dát do druhé řady (`grid-column: 2`, flex row) jako v mockupu `notion-page--mobile`. | O |
| F-04 | M | `notion-db-dialogs/phase-4/notion-db-record-detail--desktop-dark.png`, `--mobile-320-light` | Detail záznamu ukazuje prázdné Status, Priority a Tags, přestože board a galerie mají pro stejný záznam „Not Started / Low / tag-frontend“. Hodnoty Select, Status a MultiSelect se v detailu nerenderují. | Ověřit `TmNotionDbRecordDetail` → `TmNotionDbPropertyValue` pro typy Select/Status/MultiSelect (JsonElement z API). Přidat bUnit test detailu se seed hodnotami Select a MultiSelect. | O |
| F-05 | M | `notion-database/phase-8/table-1440-light.png`, `-1024-light`, `-320-dark` | V tabulce jsou sloupce Status a Priority prázdné, druhý sloupec nemá popisek hlavičky a řádky mají nekonzistentní výšku 66–88 px. Mockup `notion-db-table-view--desktop` má pilulky stavu a 46px řádky. | Stejná příčina jako F-04, ověřit `TmNotionDbCellSelect`. Výšku řádku sjednotit přes `--tm-notion-db-row-height` na `.tm-dbt__td` a odstranit vertikální padding, který roste s obsahem prázdné buňky. | O |
| F-06 | M | `notion-db-dialogs/phase-4/notion-db-record-detail--mobile-320-light.png`, `notion-db-field-editor--mobile-light.png` (390) | Footer modalu přetéká vpravo: Done/Save je uříznuté a čtyři akce (Duplicate, Delete, Close, Save) jsou v jedné řadě. | Ve footeru detailu a editoru pole na mobilu: `flex-wrap: wrap`. Primární akci dát na samostatný řádek s `width: 100%`, destruktivní (Delete) do menu „⋯“ nebo do vlastního řádku. | O |
| F-07 | M | `notion-database/phase-9/board-*`, `notion-db-record-detail--*` | Datum se zobrazuje jako surové ISO `2026-06-23T12:00:00Z`. | Pravděpodobně opraveno v `74f857fb0` (`NotionDatabaseValueFormatter.FormatDateString`). Pořídit znovu board a detail a potvrdit lokalizovaný formát (`d`). | O (ověřit) |
| F-08 | M | `notion-ai/phase-7/ai-sheet-390-light.png`, `ai-sheet-320-light` | Sheet AI má dvojitou hlavičku: sheet „AI assistant / Done“ a pod ní vnitřní „AI assistant ×“. Obsah je bez bočního paddingu (x=0), záložka „Ask“ a tlačítko „Retry“ přetékají vpravo a „Discard“ visí osamoceně. | V sheet prezentaci skrýt vnitřní hlavičku panelu AI (podle `data-presentation="sheet"`). Tělu dát `padding-inline: var(--tm-space-4)`. Záložky režimů udělat horizontálně scrollovatelné (`overflow-x: auto`) nebo s ikonami. Akce `flex-wrap: wrap` a Run/Accept na celou šířku jako sticky footer (NO1 / N11 sticky footer). | O |
| F-09 | M | `notion-search/phase-6/search-1440-*`, `-1024-*` | Vyhledávání je na desktopu fullscreen bez hlavičky aplikace a ~40 % plochy je prázdné. Mockup `notion-page-search--desktop` je centrovaný modal. Hierarchie písma je obrácená: meta (datum · autor, ~16 px) je větší než titulek (13 px bold). „Results: 20“ neodpovídá „Pages (6)“. | Desktop a tablet: `TmModal Size=Large` (ne fullscreen), `max-height: min(80dvh, …)`. Titulek `--tm-font-size-md` semibold, meta `--tm-font-size-sm` `--tm-text-secondary`. Počty sjednotit: patička ukazuje počet zobrazených výsledků. | O |
| F-10 | M | `notion-database/phase-9/timeline-1440-light.png`, `timeline-390-dark.png` | Timeline se otevře na „dnešku“ (Oct–Nov 2026), zatímco záznamy jsou v červnu, takže nejsou vidět žádné bary ani hint. Na 390 je jen prázdná mřížka bez seznamu záznamů. Mockup má sheet „Záznamy“ s položkami. „Previous period“ a „Next period“ jsou textová tlačítka zalomená do dvou řádků. | Při prvním renderu scrollovat na nejbližší záznam (nebo zobrazit hint „N záznamů mimo rozsah → Přejít“). Na mobilu renderovat seznam záznamů (`tm-dbtl__sidebar` jako list pod osou). Navigaci periody udělat ikonovou (‹ ›) s `aria-label` = dnešní texty. | O |
| F-11 | M | `notion-database/phase-9/calendar-390-light.png`, `calendar-390-day-sheet.png` | Na 390 jsou chipy událostí zkrácené na 1–2 znaky („W A.“, „Fi l…“), tedy nečitelné. Day-sheet screenshot (2 KB) ukazuje jen „+ New record“ bez záznamů dne. | Na mobilu místo textových chipů dot indikátory (`.tm-dbc__event` → tečka + počet) a tap na den otevře day-sheet se seznamem. Re-capture day-sheetu se dnem obsahujícím záznam. | O |
| F-12 | M | `notion-menus/phase-6/colour-palette-1440-light.png`, `-1024-*` | Desktop paleta má výšku jen 72 px, popisky „TEXT COLOR“ a „BACKGROUND COLOR“ překrývají swatche a ty jsou oříznuté. Mockup `notion-color-picker--desktop` má swatche „A“ s popisky (Šedá, Hnědá…). | Doplnit `padding` a `gap` mřížky (`var(--tm-space-2)`), group label jako samostatný řádek nad mřížkou, swatch min. 28 px (44 px coarse). Každý swatch musí mít `aria-label` s názvem barvy (`TmNotionColor_*`). | O |
| F-13 | M | `notion-history/phase-7/history-mobile-preview-320-light.png`, `notion-history-320-dark` (stale) | Mobilní náhled verze má Compare a Restore dvakrát: v liště nad náhledem i ve footeru. „Restore this version“ v disabled stavu vypadá jako vybledlý primár. | Ve footeru ponechat jen jednu sadu akcí (Restore primární, Compare sekundární). Horní lištu na mobilu zrušit nebo nechat jen Back. Disabled primár podle `--tm-opacity-disabled` plus `aria-disabled`. | O |
| F-14 | m | `notion-menus/phase-6/block-menu-*` | „Delete“ je první položka a dostává iniciální fokus, takže Enter po otevření menu smaže blok. | Delete přesunout na konec za separátor (Notion konvence). `InitialFocusSelector` nastavit na první nedestruktivní položku (`TmNotionBlockContextMenu.razor:14`). | O |
| F-15 | m | `notion-menus/phase-6/type-switcher-*` | Položky turn-into nemají ikony (jen text), ačkoli `SvgIcon` je v markupu. | Ověřit, že `SvgIcon` v `_groups` není prázdný (např. stejné ikony jako slash menu). | O |
| F-16 | m | `notion-editor/phase-3/shell-desktop-1440-light`, `-1024-light` | Top bar má 9 ikonových tlačítek bez textu a dvě identické ikony „book“ (Blog a Reading mode). | Blog = ikona „notebook / file-text“, Reading mode = „book-open“. Na ≥1200 px přidat text u 3–4 hlavních akcí, jak ukazuje mockup (Blog, Analytika, Režim čtení…). | O |
| F-17 | m | `notion-database/phase-8/toolbar-1440-dark` | DB toolbar je na desktopu jen ikonový (Filter/Sort/Group…), i když místa je dost. „More views ⌄“ se láme na dva řádky. | `ToolbarLabels=Auto` (labely na desktopu) jako v mockupu (Filtr / Seskupit / Seřadit). U `.tm-db__view-tabs` nastavit `white-space: nowrap`. | O |
| F-18 | m | `notion-restrictions/phase-7/notion-restrictions-1440-dark` (stale) | Popisek „Restriction mode“ je dvakrát (legend i label). | Ponechat jen `<label>`, nebo legend skrýt (`.tm-visually-hidden`). Ověřit i v novém capture. | O |
| F-19 | m | `notion-restrictions/phase-7/restrictions-cards-390-light` | Řádek User / id / View je na 390 natěsno a placeholder „User or group id“ se láme. | Pod 480 px skládat pole pod sebe (`flex-direction: column`, `width: 100%`). | O |
| F-20 | m | `notion-panels/phase-4/notion-text-comment-panel--mobile-*` | „No comments yet“ je u levé hrany (x=16) a citace je odsazená (x=28), tedy nekonzistentní inset. Plné primární tlačítko „Close“ soupeří s „Comment“. | Sjednotit inset `var(--tm-space-4)`. Close jako sekundární nebo ghost tlačítko. | O |
| F-21 | m | `notion-page-comments/phase-5/popover-1440-light` | Dvě „Reply to this“ v jednom vlákně (inline odkaz a patička). | Ponechat jen patičku, inline odkaz u posledního komentáře skrýt. | O |
| F-22 | m | `notion-pickers/phase-6/emoji-390-dark` | „Remove icon“ v řádku hledání vytlačuje kategorii za pravou hranu. | Hlavičku pickeru udělat `flex-wrap: wrap` a „Remove icon“ dát na vlastní řádek pod 480 px. | O |
| F-23 | m | `notion-templates/phase-6/templates-1440-dark`, `-320-light`, `notion-menus/phase-6/shortcuts-320-*` | „Close“ je přilepené k pravé hraně bez paddingu. Screenshoty na 320 jsou full-page a pod sheetem je vidět stránka, takže artefakt capture mate. | Footer galerie: `padding: var(--tm-space-3) var(--tm-space-4)`. Capture sheetů dělat viewport screenshotem (`FullPage=false`). | O |
| F-24 | m | Celá sada | Pokrytí indigo končí fází 2. Media, share, restrictions, history a AI desktop jsou zastaralé (11-06). | Do finálního capture (`NotionPhase*Screenshots`) přidat indigo variantu alespoň pro shell, DB tabulku, komentáře a modal. Re-capture zastaralých souborů. | O |
| F-25 | m | `notion-media/phase-7/media-1440-dark`, `media-390-dark` (stale) | Tělo upload dialogu je v dark bílé. Novější `media-coarse-390-dark` (11-07) je v pořádku. | Po re-capture potvrdit, že jde jen o zastaralý stav. | O (ověřit) |

### Co je v pořádku (shoda s mockupy a plánem)

- Mobilní sheety (More, slash „Insert block“, notifikace, komentáře stránky, sdílení, omezení, emoji) mají grabber, titulek a Done/×. Odpovídají NO1 / N5.
- Výběr verzí v historii (A/B odznaky, Inline / Side by side / Exit comparison) a legenda diffu odpovídají mockupu `notion-diff-viewer`.
- Board (sloupce s počtem, „Add card“, menu „Move to“ jako ne-drag alternativa, WCAG 2.5.7), galerie, seznam s pilulkami a chevronem.
- Komentáře: stav vlákna (Resolved, Watch, Re-open), reakce, `Ctrl+Enter` hint a potvrzení smazání jako jádrový `TmDialog` s danger akcí.
- Veřejná stránka: read-only badge, žádný sidebar a žádný pravý sloupec (záměrně podle §6).
- Panely Notifikace, Moje úkoly, Analytika, Audit a Blog jsou čitelné v light i dark.
- Edit modal diagramu je na 390 kompaktní, s Discard a Save & close.

## Záměrné odchylky od mockupů (neřešit jako vadu)

1. **App shell z referencí:** horní lišta „Tempo“, levé menu aplikace (Domů, Rychlý přístup, Databáze, Koš v hlavní ploše), spodní tab bar aplikace, „Projekt Aurora“, přepínač projektů, avatar uživatele, notifikace a vyhledávání aplikace, breadcrumbs hostitele. Komponenta není app shell, akce hostitele řeší sloty (G9, SUMMARY §6). Screenshoty ukazují demo shell Tempo.Blazor.
2. **Pravé inspektory:** „Detaily stránky“, „O stránce“, „Související stránky“, „Historie stránky“, „Štítky“ a „Sdílení“ v pravém sloupci (mockupy editor, color-picker, page-search, page-comment-panel). Podle G10 je jen prázdný slot `InspectorContent` přes `TmSidePanel`, bez obsahu.
3. **Funkce vymyšlené generátorem (SUMMARY §6 a §4.2):**
   - pozvánky spolupracovníků „+“ a avatary spolupracovníků v hlavičce
   - „Sdíleno s N členy“ / „5 členů“
   - pravý sloupec veřejné stránky (Tým, Rychlé odkazy)
   - pokročilé filtry vyhledávání (Autor, Štítek, Prostor, Vytvořeno po/před): NO2 = klient-side, rozhraní Abstractions beze změny
   - tlačítka „Otevřít / Navigovat“ na řádku výsledku
   - „Upraveno“ badge u komentáře
   - modál konfliktu (zůstává inline banner)
   - `DeletedByUserId` a koš v hlavní ploše
   - úprava aktivního veřejného odkazu: NO5 = pole zamčená, `UpdateShareAsync` se nepřidává
4. **Ilustrační obsah:** české demo texty, barvy událostí v kalendáři a timeline podle kategorie, avatary s fotkami, data 2024/2025. Komponenta kvůli ukázkovým datům nemění model.
5. **Backlog z úvodu plánu (NEDĚLÁ SE):** vše výše plus obsah inspektoru a změny rozhraní v Abstractions (search, share update, comment provider). Mobil: DB jen čtení (G2), editor = prohlížení, psaní, vložení bloku a komentáře, plná editace od tabletu.
6. **Prezentace komentářů (NO1 = Auto):** na desktopu bublina nebo popover místo modalu „Komentáře stránky“ z mockupu, na dotyku a úzké šířce dialog nebo sheet.
7. **Barevný picker:** místo modalu „Vybrat barvu“ ukotvená paleta v menu bloku (NO4 odstranil `TmNotionColorPicker`). Popisky barev jsou ale potřeba v `aria-label`, viz F-12.

## Verdikt

**ChangesRequested.**

- **Blockery:** F-01 (runtime chyba na demo stránce ve všech screenshotech) a F-02 (nečitelný turn-into a inline toolbar v dark mode).
- **Majory:** F-03 až F-13 je nutné opravit, nebo u F-07 a F-25 ověřit re-capture.
- **Minory:** F-14 až F-25 lze převést do backlogu se záznamem přes `plan_report_remaining`.
