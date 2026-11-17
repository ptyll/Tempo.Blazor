# EmusCz dluh 2.12 — přeměřená inventura a rozhodnutí

> Plán 98c80bc3 („Tempo.Blazor 2.12: upstream opravy a API, které odstraní obchvaty v EmusCz“),
> Fáze 1. Původní inventura vznikla proti Tempo v2.11.1 (`edb5bf3fe`) dne 2026-11-16.
> Tento dokument je výstup Fáze 1: **přeměření** každé položky proti **Tempo main**
> (HEAD `6a5e3abfa`, 2026-11-16) a návrh rozhodnutí o breaking bodech a semveru.
> Revize 2 (týž den): zpracovány recenzní nálezy N1–N12 — nový sloupec „Dostupné od“,
> opravené řádky PageSize / TmModal footer / ReadOnly / Pressed / marginu, sestup inert
> walku a Escape přejmenovány na ODLOŽENO a přepočten souhrn.

## Metoda a limity

- Tempo cesty v tabulce jsou měřeny proti **main HEAD `6a5e3abfa`** (obsahuje již
  `c3a9bdedb` — pin-on-top API plánu 4ce1b994, tedy 2.12.0 začalo vznikat).
  Každý `soubor:řádek` níže je **skutečně otevřený a dočtený** v pracovním stromě, ne přepis
  z původní inventury; po recenzi byl znovu dočten každý řádek citovaný nálezy N1–N12
  (PageSize `TmDataTable.razor.cs:1168`/`:1637-1638`/`:1670`/`:2769`, column-reverse
  `_modal.css:473-476` + `ModalEnums.cs:85` + `TmDashboardDialogsTests.cs:115-116`, inert walk
  `tm-focus-trap.js:508-513`/`:527-544`/`:327-348`, `tokens.css:447`,
  `TmAttachmentManager.razor:48-65`/`:89`, `breakpoints.css:98`, `TmModal.razor:212`,
  parametry `ReadOnly` vstupů a `Pressed` v `Components/Buttons`).
- Sloupec **„Dostupné od“** je ověřen přes `git tag --contains`: footer wrap (`d6bf1ee1f`),
  inert-exempt atribut+dokumentace (`083aefc37`), token aliasy (`335db06eb`) i CardMode
  container (`ce940ede8`) jsou ve **v2.11.0**; PageSize throw kontrakt je od **2.8.12**
  (`1b3c29611`); pin-on-top (`c3a9bdedb`) je zatím jen v **nevydané 2.12.0** (žádný tag).
- **Repo EmusCz (EmusBlazorComponents, feat/emuscz) není na tomto stroji k dispozici**
  (prohledáno `C:\work` i běžná vývojová umístění; klíčový soubor `EmusAssistantPanel.razor.cs`
  na disku neexistuje). EmusCz čísla řádků z původní inventury proto zůstávají **hypotézou** —
  každý citovaný obchvat přeměří **navazující EmusCz plán** (bývalé Fáze 9/12 tohoto plánu se
  do něj přesouvají), až bude repo dostupné. Na toto přeměření závisí i rozhodnutí o splatu
  PageSize (N1) a o odložených záměrech inert sestupu a Escape (N3).
- Stav: **OPEN** = v Tempu main stále chybí; **UPSTREAM** = v main vyřešené (přesun do
  navazujícího EmusCz plánu); **ČÁSTEČNĚ** = vyřešené jinak/částečně, zbývá jen EmusCz úklid
  nebo zúžení záměru; **ODLOŽENO** = Tempo záměr odložen decisionem
  `EMUSCZ-2.12-UPSTREAM-PRUNING` — potřeba se teprve potvrdí na HEAD EmusCz. Citovaná
  místa se proti původní inventuře v naprosté většině shodují řádek na řádek (posuny jsou
  jednotky řádků, viz sloupec „Soubor:řádek“).

## Tabulka položek (položka · soubor:řádek · stav · cíl · dostupné od)

### Fáze 2 — Tempo.Blazor.AI

| Položka | Soubor:řádek (měřeno @ 6a5e3abfa) | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `[Parameter] bool Generating`, `IsGenerating => _streamCts is not null \|\| Generating` | `src/Tempo.Blazor.AI/Components/AI/TmAiConversation.razor.cs:146` | OPEN | Fáze 2 | — |
| Stick-to-bottom i při zmenšení logu; volitelné `ScrollToBottomAsync()`/`ScrollToMessageAsync(id)` | `TmAiConversation.razor.cs:548` (jen `OnContentResizedAsync`), `wwwroot/js/ai-conversation.js:176-182` (ResizeObserver jen na obsah zpráv, ne na box logu), `:636` (`ScrollToBottomAsync` je privátní, `ScrollToMessageAsync` neexistuje) | OPEN | Fáze 2 | — |
| Placeholder `tm-ai-message__pending` i pro `Status == Pending` bez částí + `PendingText` | `TmAiMessage.razor:38-41` (`Parts.Count == 0 && IsStreaming`; řádky přesně dle inventury), `PendingText` neexistuje; průchozí parametr na `TmAiConversation` také ne — `<TmAiMessage>` se renderuje na `TmAiConversation.razor:58`; `aria-busy` dnes jen pro streaming (`TmAiMessage.razor:37`) | OPEN | Fáze 2 | — |
| Token `--tm-ai-user-bubble-bg` (primary tint + 1px `--tm-color-border-subtle`) | `Tempo.Blazor.AI/wwwroot/css/components/_ai-message.css:58` (`var(--tm-bg-surface-secondary)`), `src/Tempo.Blazor/wwwroot/css/tokens-dark.css:15` a `:17` (tmavá: `--tm-bg-page` == `--tm-bg-surface-secondary` == `#0f172a`) | OPEN | Fáze 2 | — |

### Fáze 3 — Approvals a vstupy

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| TmInlineDiff: label/`<del>`/`<ins>` bez mezer, nebo `pre-wrap` jen na `__equal/__del/__ins`; volitelně `Layout="Stacked"` | `Components/Approvals/TmInlineDiff.razor:93-102` (elementy mezi sebou mají whitespace, který `pre-wrap` vykreslí), `wwwroot/css/components/_inline-diff.css:56-60` (`pre-wrap` na `__content`); `Layout` neexistuje (komponenta nemá žádné parametry) | OPEN | Fáze 3 | — |
| TmTextArea: hodnota jako `value` atribut, ne child text | `Components/Inputs/TmTextArea.razor:24` (`@oninput="HandleOnInput">@Value</textarea>`) | OPEN | Fáze 3 | — |
| `ReadOnly` na TmTextArea/TmSelect/TmFilterableDropdown + stabilní výchozí `Id` | `TmTextArea.razor:81`, `TmSelect.razor:108` (`Id = Guid.NewGuid()[..8]`), `TmFilterableDropdown.razor:197` (`_listboxId`); **z trojice TmTextArea/TmSelect/TmFilterableDropdown `ReadOnly` nemá žádná** — param `ReadOnly` už mají i jiné vstupy: `TmTextInput.razor:133`, `TmCurrencyInput.razor:143`, `TmDecimalInput.razor:199`, `TmMaskedTextBox.razor.cs:50`, `TmCodeEditor.razor:113`, buňky `TmOtpInput.razor.cs:49` a `TmRating` | OPEN | Fáze 3 | — |
| `AriaDescribedBy` na vstupy + kaskáda z TmFormField + slot `LabelTrailing` | Param `AriaDescribedBy` už mají `TmToggle.razor:65`, `TmOtpInput.razor.cs:106`, `TmMultiSelect.razor.cs:121`, `TmMarkdownEditor.razor:117`, `TmFocusScope.razor:72`, `TmModal.razor:272` a `TmChat.razor.cs:283` (`ComposerDescribedBy`) — **z trojice `TmTextArea`/`TmSelect`/`TmFilterableDropdown` ho nemá žádná** (stejně tak `TmTextInput`/`TmNumberInput`); kaskáda z `TmFormField` nese jen `FormFieldErrorId` (`TmTextArea.razor:105-113`); `LabelTrailing` nikde | ČÁSTEČNĚ UPSTREAM (vzor existuje) | Fáze 3 (zúžit: jádrové vstupy + kaskáda + slot) | — |
| Volitelně `TmInputOptions.ImmediateByDefault` | Typ neexistuje (0 výskytů v repu) | BACKLOG (mimo 2.12) — backlog item 83bd9915-a8cf-46a4-957c-dfeba51cc209 | Fáze 3 | — |

### Fáze 4 — Tlačítka, dialogy, kontextové menu

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `ButtonVariant.Soft` + `[Parameter] bool? Pressed` (`aria-pressed`, `.tm-btn--pressed`) | `Components/Buttons/ButtonVariant.cs:4-28` (bez `Soft`); `Pressed`/`aria-pressed` ve **`Components/Buttons` 0 výskytů** — existující vzor: `TmChip.Pressed` (`TmChip.razor:96`, `aria-pressed` `:30`/`:44`) a `TmToolbarButton` (`Pressed` `:103`, `aria-pressed` `:25`) | OPEN | Fáze 4 | — |
| `TmDialog`: `CancelButtonVariant`/`OkButtonVariant` | `Components/Feedback/TmDialog.razor:107` (cancel natvrdo `ButtonVariant.Ghost`), OK varianta přes `GetOkButtonVariant()` `:500-508` (mapuje `DialogVariant`, veřejný param není); precedens konzistentního názvu: `TmModal.OkButtonVariant` (`TmModal.razor:212`) | OPEN | Fáze 4 | — |
| `TmContextMenuItem`: deterministické zavření | `Components/Navigation/TmContextMenuItem.razor:54-65` (`Parent?.CloseMenu()` + `await Task.Yield()` na `:61-62`) | OPEN | Fáze 4 | — |
| `TmContextMenu`: „as child“ režim + `TriggerAriaLabel` | `TmContextMenu.razor:8-27` (wrapper `div role="button"` + ohraničující span), `TriggerAriaLabel` neexistuje | OPEN | Fáze 4 | — |

### Fáze 5 — TmTabs

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `KeepInactivePanels`, opt-in `IdPrefix`/instanční id, `aria-controls` jen na vykreslené panely | `Components/Navigation/TmTabs.razor:18` (jen aktivní panel `tabpanel-{activePanel.Id}`), `:102` (`aria-controls` na **všech** tab tlačítkách, i na nevykreslené), globální schéma `tab-{Id}`/`tabpanel-{Id}` | OPEN | Fáze 5 | — |
| `TabsOverflow Overflow = Wrap \| Scroll` a volitelně `StickyHeader` | 0 výskytů | OPEN | Fáze 5 | — |

### Fáze 6 — Modal, focus trap, top layer

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `TmModal`: footer sloupcem v pořadí DOM, scrollovatelná patička, zalamování popisků | `Components/Feedback/ModalEnums.cs:79-87` — **`FooterLayout.Stacked` existuje, ale = `column-reverse`** (`_modal.css:473-476`, test `TmDashboardDialogsTests.cs:115-116`: cancel je první v DOM a column-reverse ho maluje nahoru), tedy **ne pořadí DOM**; summary `ModalEnums.cs:85` s realitou nesouhlasí; wrap `_modal.css:466-469`, zalamování popisků `:484-499`; **scrollovatelná patička s max-height chybí** | ČÁSTEČNĚ — stacking + wrap upstream; chybí `StackedInOrder` (pořadí DOM) a max-height/overflow patičky | **Fáze 6** + navazující EmusCz plán (DraftResumeDialog, bývalá Fáze 9) | v2.11.0 (stacking+wrap, `d6bf1ee1f`) |
| `tm-focus-trap.js markBackgroundInert`: sestoupit do sourozence s `data-tm-inert-exempt` potomkem; dokumentovat atribut | `wwwroot/js/tm-focus-trap.js:508-513` (`isInertExempt` rozpozná jen prvek, který walk navštíví — přímé dítě), `:527-544`; dokumentace veřejného API **existuje** (`docs/top-layer-pin.md` §2.4, `docs/overlays.md:221-228`, `docs/focus-scope-migration.md:54`) | **ODLOŽENO** (decision `EMUSCZ-2.12-UPSTREAM-PRUNING`) — výjimka platí jen na sourozenci, který walk navštíví; uvnitř `@Body` wrapperu se `.emuscz-toast-slot` inertuje s wrapperem (`docs/top-layer-pin.md:116-117`, `docs/overlays.md:225-228`), takže sestup pomáhá jen při umístění slotu jako **sourozence na úrovni layoutu mimo `@Body`** | Ověření podmínky: navazující EmusCz plán | v2.11.0 (atribut+dok, `083aefc37`) |
| Escape: capture fáze na kořeni dialogu / stopPropagation / `isInsideOpenLayer` | `tm-focus-trap.js:327-348` (document-level keydown, BEZ capture), ownership systém existuje (`ownsEscape` `:459`, `modalOwnsEscape` `:486`, `tmEscapeHandled` s overlay.js `:329-346`); po zavolání handleru `stopPropagation` nepřidává | **ODLOŽENO** (decision `EMUSCZ-2.12-UPSTREAM-PRUNING`) — EmusCz `watchEscape` nikdo nečetl, potřeba capture/stopPropagation je nepotvrzená | Ověření na HEAD EmusCz: navazující EmusCz plán | — |
| Pin on top | plán 4ce1b994 **dokončen a v mainu**: commit `c3a9bdedb` (decision `TOP-LAYER-PIN`, ADR-0006), `TmPinnedOnTop` + `TopLayerPinService` + `data-tm-pin-on-top` | OPRAVENO UPSTREAM — **hotové** (Fáze 6 tohoto plánu: koordinace označena za splněnou); adopce (toast slot na pin) patří do **Fáze 12**, ne do Fáze 9 | Fáze 12 (adopce) | **2.12.0, nevydaná** (`c3a9bdedb` — žádný tag) |

### Fáze 7 — Layout a zobrazení dat

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `TmSection`: `@attributes` na kořeni | `Components/Layout/TmSection.razor:4` (root bez `@attributes`), param `AdditionalAttributes` `:71` existuje nepoužitý | OPEN | Fáze 7 | — |
| `TmDrawer`: token `--tm-drawer-body-padding` | `wwwroot/css/components/_drawer.css:226-231` (`.tm-drawer__body { padding: 1.25rem; }` — řádky přesně dle inventury) | OPEN | Fáze 7 | — |
| TmDataTable (bug): záporný margin scroll boxu přetéká wrapper pod 768 px | `_data-table.css:141-147` (`.tm-data-table-scroll { margin: 0 calc(-1*var(--tm-space-4)); width: calc(100% + 2*var(--tm-space-4)); }` — **jediná definice marginu** třídy; `breakpoints.css:98` přidává pod 768 px **jen `overflow-x`**, margin nekoriguje), wrapper padding <768 `:1133-1137` (`--tm-space-2` < `--tm-space-4` ⇒ 8 px/side) | OPEN (bugfix, nebreaking) | Fáze 7 | — |
| `WrapHeaders` (tabulka) / `HeaderWrap` (sloupec) | `_data-table.css:167-174` (`.tm-data-table th { white-space: nowrap; }`) | OPEN | Fáze 7 | — |
| CardMode: `CardBreakpointSource` | **OPRAVENO UPSTREAM** redesignem: CardMode/MobileLayout přidá `tm-data-table-wrapper--container` + `container-name: tm-data-table` (`Components/DataTable/TmDataTable.razor.cs:1112-1137`, `_data-table.css:54`, komentář `:1181-1182`), karty i toolbar se řídí `@container tm-data-table` (`:1184+`) — breakpoint sleduje **vlastní šířku**, ne viewport | **OPRAVENO UPSTREAM** — subtask `CardBreakpointSource` z Fáze 7 **odstraněn** (hotovo upstream) | navazující EmusCz plán (rušení @media obchvatu, bývalá Fáze 9) | v2.11.0 (`ce940ede8`) |
| TmDataTable: výjimka providera (`OnLoadError`, `ErrorTemplate`/`ErrorText`) | 0 výskytů `OnLoadError`/`ErrorTemplate`; napadené cesty: `GetGroupedDataAsync` (`TmDataTable.razor.cs:2407`) i `GetDataAsync` (`:2426`) se stalovou ochranou `_loadGeneration` (`:2395`, kontrola `:2408`/`:2427`) | OPEN (behaviorálně breaking → **opt-in**, viz Rozhodnutí (b) — s mantinely) | Fáze 7 | — |
| `SortDescendingFirst`/`FirstSortDirection` | 0 výskytů | OPEN | Fáze 7 (implementováno jako `FirstSortDirection` na tabulce i sloupci — sloupcový knob je nullable enum, ne bool) | — |
| `PageSize`/`DefaultPageSize <= 0` | **ZMĚNĚNO UPSTREAM, ale jen z části**: `ThrowIfNotPositive` hlídá **jen controlled `PageSize`** (`TmDataTable.razor.cs:1637`, `:1670`, `:2769`; kontrakt existuje od 2.8.12, `1b3c29611`) — `DefaultPageSize` (`:1168`) se přiřadí na `:1638` **bez kontroly** → tichá prázdná tabulka / `PageSize=0` do provideru | **ČÁSTEČNĚ** — návrh „srovnat na výchozí“ se **nedělá**; do **Fáze 7** přibývá bugfix `ThrowIfNotPositive(DefaultPageSize)` když `PageSize is null` (CHANGELOG 2.12 Fixed, bUnit RED); EmusCz splat v `DocumentDataTable.razor:56-75` odstranit až po ověření, že hodnota je ≥1, jinak předat `null` | Fáze 7 (bugfix) + navazující EmusCz plán (splat, bývalá Fáze 9) | 2.8.12 (throw controlled `PageSize`, `1b3c29611`) |
| `TmChangeDiff`: `ValueOverflow` + `Compact` | `Components/DataDisplay/TmChangeDiff.razor:36,39` (jen `Changes`/`Class`) | OPEN | Fáze 7 | — |
| `TmValidationSummary`: `GroupBy`, `OnMessageClick`, `ItemTemplate` | `Components/Forms/TmValidationSummary.razor:34-46` (jen `Title`/`ShowErrorsList`/`Class`/`ManualMode`/`Show`) | OPEN | Fáze 7 | — |

### Fáze 8 — Soubory a tokeny

| Položka | Soubor:řádek | Stav | Cíl | Dostupné od |
|---|---|---|---|---|
| `TmFileDropZone`: `OnFileRemoved` / řízené `SelectedFiles` | `Components/Files/TmFileDropZone.razor:66` (jen `OnFilesSelected`) | OPEN | Fáze 8 | — |
| `TmAttachmentManager`: `ReadOnly` skryje mazání a upload | `TmAttachmentManager.razor:48-56` (delete gate jen `att.CanDelete` — `Disabled` param `:90` jde jen do drop zony `:63-65`; řádek 48 přesně dle inventury), XML doc `Disabled` `:89` tvrdí chybně „upload and delete controls are disabled“ | OPEN (breaking → **opt-in** `ReadOnly`, viz Rozhodnutí (c); **„změna typu“ v komponentě neexistuje — skryje se mazání a upload**) | Fáze 8 | — |
| `TmUploadItem`: veřejný upload controller | `TmUploadItem.cs:65` (`Cts` je **internal**), žádné veřejné `OnCancel`/`OnResume`/CancellationToken | OPEN | Fáze 8 | — |
| Tokeny: deprekace `--tm-color-primary-light`, publikovat aliasy | Aliasy **už upstream publikované**: `--tm-color-text-muted` `tokens.css:404`, `--tm-color-surface-muted` `:451`, `--tm-text-muted` `:470`, `--tm-bg-muted` `:175`, `--tm-bg-tertiary` `:420`, `--tm-bg-hover` → `--tm-color-surface-hover` (docs/css-token-aliases.md) | VĚTŠINOU OPRAVENO UPSTREAM — zbývá jen **označit `--tm-color-primary-light` (`:447`, = primary-300, tint 2.87:1) za deprecated** a doporučit `-subtle`; aliasy znovu nepublikovat | Fáze 8 (deprekace) + navazující EmusCz plán (EmusCz compat block) | v2.11.0 (aliasy, `335db06eb`) |

## Souhrn (přepočteno po recenzních opravách N1–N12)

- **33 položek přeměřeno.** Rozpad: **OPEN 24 · ČÁSTEČNĚ 4 · ODLOŽENO 2 · UPSTREAM 2 · BACKLOG 1**
  (=33). Před recenzními opravami bylo rozdělení **OPEN 25 · ČÁSTEČNĚ 3 · UPSTREAM 5**
  (=33); po opravách N1–N12 **OPEN 25 · ČÁSTEČNĚ 6 · UPSTREAM 2** a teprve oddělením
  ODLOŽENO vznikla dnešní podoba. Které položky přešly kam:
  - **UPSTREAM → ČÁSTEČNĚ (3):** TmModal footer (stacking + wrap už upstream, chybí
    `StackedInOrder` a scrollovatelná patička), PageSize (throw kontrakt hlídá jen
    controlled `PageSize`, `DefaultPageSize` zůstává nekontrolovaný → bugfix Fáze 7),
    token deprekace (aliasy už upstream, zbývá označit `--tm-color-primary-light` za
    deprecated).
  - **ČÁSTEČNĚ → ODLOŽENO (2):** inert sestup `markBackgroundInert` a Escape capture —
    nerušeny, jen odloženy decisionem `EMUSCZ-2.12-UPSTREAM-PRUNING` (podmínku ověří
    navazující EmusCz plán).
  - **OPEN → BACKLOG (1):** volitelné `TmInputOptions.ImmediateByDefault` (Fáze 3) —
    mimo 2.12, backlog item 83bd9915-a8cf-46a4-957c-dfeba51cc209.
  - **Beze změny:** OPEN 24; ČÁSTEČNĚ 1 (vzor AriaDescribedBy, Fáze 3); UPSTREAM 2
    (pin-on-top Fáze 6, CardMode container Fáze 7).
- Rozpad po fázích: **Fáze 2** — OPEN 4. **Fáze 3** — OPEN 3, ČÁSTEČNĚ 1 (AriaDescribedBy
  vzor), BACKLOG 1 (`ImmediateByDefault`). **Fáze 4** — OPEN 4. **Fáze 5** — OPEN 2.
  **Fáze 6** — ČÁSTEČNĚ 1 (footer), ODLOŽENO 2 (inert, Escape), UPSTREAM 1 (pin-on-top).
  **Fáze 7** — OPEN 8, ČÁSTEČNĚ 1 (PageSize), UPSTREAM 1 (CardMode container). **Fáze 8** —
  OPEN 3, ČÁSTEČNĚ 1 (token deprekace).
- Co se proti první revizi změnilo: TmModal footer UPSTREAM → **ČÁSTEČNĚ** (`column-reverse`
  ≠ pořadí DOM, scrollovatelná patička s max-height chybí → Fáze 6); PageSize UPSTREAM →
  **ČÁSTEČNĚ** (`DefaultPageSize` se nekontroluje → bugfix ve Fázi 7); sestup inert walku a
  Escape capture se **neruší, ale odkládají** decisionem `EMUSCZ-2.12-UPSTREAM-PRUNING`
  (podmínka se ověří v navazujícím EmusCz plánu); subtask `CardBreakpointSource` z Fáze 7
  **odstraněn** (hotovo upstream, v2.11.0); adopce pinu patří do **Fáze 12**, ne do Fáze 9.
- Nová Tempo práce, kterou opravy přinesly: bugfix `ThrowIfNotPositive(DefaultPageSize)`
  (Fáze 7), `FooterLayout.StackedInOrder` + scrollovatelná patička + oprava summary
  `FooterLayout.Stacked` (`ModalEnums.cs:85`; poznámka hlášená do remaining Fáze 6), zúžená
  deprekace tokenu a `ReadOnly` kontrakt TmAttachmentManager (Fáze 8).
- **EmusCz řádky NEJSOU přeměřeny** (repo není na stroji) — viz Metoda a limity. Vlastníkem
  EmusCz úklidu (bývalé Fáze 9/12 tohoto plánu) je **navazující EmusCz plán**; rozhodnutí o
  splatu PageSize (N1), inert sestupu a Escape (N3) závisí na přeměření EmusCz.

## Rozhodnutí: tři breaking body

Plán doporučoval „všechny tři opt-in parametrem v 2.12, default přepnout až v 3.0“. Po
přeměření platí upřesnění — **(a) a (b) jsou čistě aditivní (opt-in bez breaking), (c)
přidává nový param místo změny sémantiky existujícího** (decision
`EMUSCZ-2.12-BREAKING-POINTS`):

**(a) TmAiMessage Pending placeholder.** Placeholder přidává řádek (vizuální změna bubliny).
Rozhodnutí: v 2.12 **aditivní** — placeholder pro `Status == Pending` bez částí se renderuje
jen když host nastaví nový param `PendingText`; placeholder nese `aria-busy="true"`;
`PendingText = null` znamená dnešní chování (žádný placeholder). Host bez šablony zprávy
použije **průchozí `[Parameter] string? MessagePendingText` na `TmAiConversation`**
(`<TmAiMessage>` se renderuje na `TmAiConversation.razor:58`). Lokalizovaný default
(vlastní klíč `TmAiMessage_Pending`, případně stávající `TmAiMessage_Generating`) přibude
v 3.0 jako záměrná vizuální změna, uvedená v Breaking/Migration.

**(b) TmDataTable výjimka providera.** Rozhodnutí: v 2.12 **aditivní** — tabulka chytá
výjimku providera (ponechá předchozí řádky) **jen když** host zaregistruje `OnLoadError`
nebo dodá `ErrorTemplate`/`ErrorText`; bez delegáta se chování vůbec nemění (výjimka letí
dál, jako dnes). To dává EmusCz řešení pro `TaskManagerJobDataProvider.cs:86-110` bez
jakéhokoli breaking rizika. **Mantinely implementace:** `OperationCanceledException` se
NEchytá (nechá letět); chybu zahodit, když `generation != _loadGeneration` (zastaralá
odpověď); ošetřit OBĚ cesty — `GetGroupedDataAsync` (`TmDataTable.razor.cs:2407`) i
`GetDataAsync` (`:2426`); `_displayedItems`/`_totalCount` se ponechávají (předchozí řádky
zůstávají); Retry znovu volá `LoadFromProviderAsync`; nové Loc klíče ve všech 4 jazycích
+ zápis do `MockTmLocalizer`. V 3.0 lze zvážit defaultní zachycení (tehdy Breaking/Migration).

**(c) TmAttachmentManager `Disabled` skryje mazání.** Změna sémantiky `Disabled` by byla
breaking (host s `Disabled="true"` dnes mazání vidí a používá). Rozhodnutí: v 2.12 přidat
**nový aditivní param `ReadOnly`**: **nevykreslí delete tlačítka ani `TmFileDropZone`**,
na kořen dá třídu `tm-attachment-manager--readonly` (žádné `aria-readonly` na div).
„Změna typu“ v komponentě neexistuje — `ReadOnly` **skryje mazání a upload**. `Disabled`
zůstává „vypne jen upload drop zonu“ a jeho XML doc (`TmAttachmentManager.razor:89`) se
opraví na „disables the upload drop zone only; use ReadOnly to hide delete“; sjednocení
`Disabled`/`ReadOnly` se otevře až v 3.0.
(EmusCz `ReadOnlyAttachmentProvider` tím padne při migraci — navazující EmusCz plán.)

## Semver a CHANGELOG

- Všechny položky Fází 2–8 jsou aditivní nebo opt-in ⇒ **minor ⇒ 2.12.0**.
- Výjimka: bugfix `ThrowIfNotPositive(DefaultPageSize)` (Fáze 7) se vede jako bugfix do
  CHANGELOG 2.12 sekce **Fixed** (doplňuje validitu u existujícího throw kontraktu z
  2.8.12, `1b3c29611` — nepřidává API).
- `CHANGELOG.md` už má sekci **`## 2.12.0 (unreleased)`** (zavedl ji plán 4ce1b994) a
  `src/Tempo.Blazor/Tempo.Blazor.csproj` má `Version=2.12.0` — Fáze 1 sem nic
  nepřidává (jejím výstupem je tento dokument; CHANGELOG dostane položky až při
  implementaci Fází 2–8, breaking položky „(a) localized default v 3.0“ a
  „(c) sjednocení v 3.0“ se zapíší do 3.0 Breaking/Migration v okamžiku vydání 3.0).
- Vydání 2.12.0 nad rámec v2.11.1 zůstává vázáno na výslovný souhlas Pavla (Fáze 11).
