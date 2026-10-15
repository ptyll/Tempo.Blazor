# Backlog — redesign TmDashboard

Položky posunuté mimo fáze redesignu (výchozí stav: F2, fix kolo fáze 2). Odkazy vedou na
specifikace v tomto adresáři. Pořadí není prioritou; čísla § patří k `f2-ux-spec.md`.

## Stavy komponenty

### Loading skeleton + `aria-busy` (F2 UX §5.7)

Dokud `_dataLoaded == false`, komponenta krátce zobrazí prázdný stav („Zatím žádné widgety“) —
falešná informace. Navrhnout: místo prázdného stavu vykreslit 2 karty `TmSkeleton`
(výška `calc(4 * var(--row-height))`), na `.tm-dashboard-grid-container` nastavit
`aria-busy="true"` a přepínač zobrazí `TmDashboard_DefaultName`. Nutné rozmyslet bUnit testy
(nebude možné číst widgety dřív, než provider odpoví) a obrazovky, které provider záměrně
prázdný nemají.

### Error stav: `TmAlert` + „Zkusit znovu“ (F2 UX §5.8)

Výjimky `IDashboardProvider` (`GetDashboardsAsync` / `GetDefaultDashboardAsync` /
`GetDashboardAsync` / `SaveDashboardAsync`) nejsou zachyceny — padají celou aplikaci. Navrhnout
`try/catch` v load/save cestách, nad mřížkou `TmAlert` (Danger) s lokalizovaným textem a akcí
„Zkusit znovu“ (opakování `LoadDefaultDashboard`/`LoadDashboard`). Nové lokalizační klíče
do všech čtyř tabulek + `MockTmLocalizer`.

## Tokeny a CSS

### `min-height: 600px` kořene → token (G-6)

`.tm-dashboard` nese hardcode `min-height: 600px` (dnes s komentářem). Zavést spacing-odvozený
token (např. `--tm-dashboard-min-height`) v `tokens.css` (+ dark nepotřebuje override) a
použít ho zde i v `.rc-dashboard` demo pravidle, které hodnotu ruší. Změna čistě na token,
žádný vizuál.

### Kontrast amber dlaždice (F2 UX §5.11)

Ikona dlaždice dědí barvu kategorie (např. `--tm-category-3`, amber, ve světlém režimu). Na
tintu `12 %` může splynout pod 2:1. Ověřit `--tm-category-7` (šedá) a `--tm-category-3`
(amber) ve světlém režimu; při kontrastu < 2:1 použít pro barvu ikony dlaždice
`color-mix(in srgb, var(--category-color) 75%, var(--tm-text-primary))`. Dlaždice je
dekorativní (`aria-hidden`), 1.4.11 se nevztahuje — jde o vizuální čitelnost.

## F3 (layout, pořadí, tablet/mobil)

### `_titleElementId` není nikým odkázáno — zapojit `aria-labelledby`

Kořen drží `tm-dashboard-title-{guid}` jako `id` viditelného/skrytého h2, ale nic na něj
neodkazuje (F2 UX §6.3 to doporučuje). Ve F3 přidat kořeni `role="region"` +
`aria-labelledby="@_titleElementId"` (jedna oblast na stránku; ověřit dopad na testy, které
hledají roli/strukturu kořene, a na vnořené dashboardy).

**Hotovo (F3, 2.9.1):** kořen nese `role="region"` a `aria-labelledby="@_titleElementId"`,
když se h2 vykresluje (i jako `tm-sr-only`); bez nadpisu (ShowTitle=false ve view, ShowHeader=false)
přebírá `aria-label="@HeaderTitle"`. bUnit testy: `Dashboard_Root_IsRegionLabelledByTitle`
(TmDashboardResponsiveTests).

### Sbalení widgetu: `!important` → `--tm-w-rows`

`.tm-widget--minimized { grid-row: span 1 !important; }` (`_dashboard.css`) bije do inline
`--tm-w-rows` z `GetWidgetStyles`. Ve F3 („pořadí podle (Y,X)“ + práce s výškou slotů)
přepsat na var pravidlo (např. `grid-row: span var(--tm-w-rows-collapsed, 1)` nebo podmíněné
neposílání `--tm-w-rows`), aby `!important` zmizelo a mřížka zůstala řízená proměnnými.
Sbalená karta dnes neuvolní výšku slotu — totéž F3 (F2 UX §5.10).

**Hotovo (F3, 2.9.1):** `!important` pravidlo zmizelo; `IsMinimized` je layoutový alias sbalení
(M-7) — na desktopu karta zůstane ve slotu `Y / span H` a zmenší se na hlavičku
(`align-self: start; min-height: auto`), na tabletu/mobilu navíc `grid-row: auto` uvolní výšku.
`WidgetInstance.Height` se nemění, `--tm-w-rows` zůstává (dashboard.js čte Height pro náhled
dropu). bUnit testy: `Dashboard_CollapsedWidget_ReleasesRows`,
`Dashboard_MinimizedWidget_CarriesTheAliasClass_AndKeepsItsSlotVariables`,
`Dashboard_Css_CollapseReleasesRows_WithoutImportant` (TmDashboardResponsiveTests).

## F4+ (kompaktní edit režim)

### Hlavička karty na 390 px: skupina posunu na druhý řádek (container query)

Na 390 px drží hlavička karty dvě 44 px tlačítka posunu (nahoru/dolů), ikonu a tlačítko
Odebrat — titul se zkracuje na „Active …“ (viditelné na
`__screenshots__/dashboard/f4/mobile-390--compact-edit-after-swap.png`). Funkčně v pořádku
(titul má ellipsis a `aria-label` tlačítek nesou význam), proto jen follow-up v pozdější fázi:
v úzkém kontejneru (container query na kartě) zabalit skupinu `.tm-widget-move-group` na
samostatný druhý řádek hlavičky, aby titul dostal plnou šířku. Čistě CSS změna přes tokeny,
bez změny markupu.
