# Fáze 1 — kontrakt architekta (vlna 0, bez vizuální změny)

Plan `6288f7e0-f903-4fb5-8c37-08df2362aa32`. Stav kódu ověřen 2026-10-16 na `main` @ `259f900a`. Čísla řádků jsou aktuální, ne ta ze zadání plánu.

## 1. Rozsah

Jen funkční a lokalizační opravy. Žádný nový vzhled, žádné nové PNG, žádný sheet/modal/dropdown, žádné `MobileLayout` / `KeyboardNavigation` / `Appearance`.

Working tree má **cizí dashboard WIP**. Commit jen explicitní cesty této fáze. `git add -A` je zakázané. `CHANGELOG.md` je už dirty — před editací `git diff CHANGELOG.md`; do commitu nesmí spadnout dashboard odstavce.

## 2. Stavy a přechody

- **Filtr sloupce:** neřízený režim drží text v `_value` (`TmColumnFilter.razor` ř. 60). Řízený režim (`Value` != null) přepisuje `_value` z parametru při každém setu. Clear ✕ nastaví prázdný řetězec a pošle `ValueChanged` + stávající filter callback s prázdnou hodnotou (nebo odstranění filtru — stejná sémantika jako dnešní text clear).
- **Tabulka:** `_activeFilters` je zdroj pravdy. `RemoveColumnFilterAsync` (ř. 1638) a `ClearAllFiltersAsync` (ř. 1648) mažou slovník a reloadují, ale inputy to dnes nevidí, protože `TmColumnFilter` je neřízený. Po fázi tabulka předává hodnotu z `_activeFilters` do `Value`.
- **View dialog:** sekce Seskupení se renderuje jen když `AvailableGroupableColumns.Count > 0` (`TmViewManager.razor` ř. 182). Integrované `TmDataTable` / `TmMultiViewList` ten parametr dnes nepředávají — sekce je mrtvá. Předat sloupce s `Groupable=true` (DT) a `GroupableFields` (MVL).
- **Radio scope:** oba inputy mají `name="viewScope"` (ř. 145, 153). Dvě instance na stránce sdílí skupinu. Nové jméno `tm-view-scope-{_instanceId}` (`_instanceId` už je v `TmViewManager.razor.cs` ř. 63).
- **Karta MVL:** při `AllowSelection` je avatar v `else if` (ř. 1303) a nikdy se nevykreslí. Avatar a checkbox jsou nezávislé.

## 3. Transakční hranice

- Jeden clear / jeden odstraněný filtr = jedna změna `_activeFilters` + jeden reload (provider nebo in-memory). Žádný druhý reload z `@bind`.
- `FilterOperator == null` znamená dnešní `"contains"`. **Default se nemění.** Serverový provider nesmí dostat jiný operátor, dokud sloupec explicitně nenastaví `FilterOperator`.
- Save-current je jedna kopie stavu, ne mutace aktivního pohledu.

## 4. Idempotence

- `ClearAllFiltersAsync` na prázdném slovníku je no-op z pohledu dat (smí reloadovat, inputy zůstanou prázdné).
- Řízené `Value` vždy vyhraje nad vnitřním `_value`. Bez `Value` se chování text/select filtru nemění.
- Copy suffix se přidá jednou při save-current. Opakované uložení už uloženého názvu „X (kopie)“ smí dát „X (kopie) (kopie)“ — to je dnešní sémantika, jen lokalizovaná. Netvoř deduplikaci.

## 5. Neplatné vstupy

- `FilterOperator` null nebo whitespace → `"contains"`.
- Neznámý operátor: nepolykej ho potichu do contains, pokud ho sloupec explicitně nastavil. Předat dál tak, jak dnes `FilterOperatorParser` / in-memory filtr reagují; když parser neznámé zahodí, test to zaznamená a v docs napiš povolené hodnoty (`equals`, `contains`, `greaterthan`, … — ověř parser, nevymýšlej).
- Prázdný `ColumnTitle`: aria-label/placeholder spadnou na stávající klíč bez názvu sloupce (dnešní „Filtr“), ne na „Filtr: “.
- Žádné groupable sloupce: sekce Seskupení se nezobrazí (dnešní podmínka).

## 6. Povinné testy

TDD: nejdřív RED, dolož selhání, pak GREEN.

| Test | Assert |
|---|---|
| `TmViewManagerLocalizationTests` | cs kultura: texty panelu i dialogu česky (Views, PersonalViews, NoPersonalViews, CreateNew, SaveCurrent, ViewName, Visibility, Personal, Tenant, Columns, Grouping, Filters, Cancel, Create/Save). Save-current název = `"{název} (kopie)"` přes `TmViewManager_CopySuffix`, ne `"(Copy)"`. |
| `TmViewManagerTests.TwoInstances_UseDistinctRadioGroupNames` | dvě instance → dva různé `name` začínající `tm-view-scope-`. |
| `TmDataTableViewGroupingTests.ViewDialog_ShowsGroupingSection_WhenGroupableColumnExists` | tabulka se sloupcem `Groupable=true` předá `AvailableGroupableColumns` a dialog ukáže sekci. |
| `TmMultiViewListToolbarTests` (nová metoda stejného významu) | `GroupableFields` se dostanou do view dialogu. |
| `TmColumnFilterTests` | s `ColumnTitle` je aria-label i placeholder `Filtr: {title}`; řízené `Value` přepíše `_value`; bez `Value` beze změny; ✕ i u selectu. |
| `TmDataTableToolbarTests.ClearAllFilters_EmptiesColumnFilterInputs` | po clear jsou inputy prázdné. |
| `TmDataTableColumnFilterOperatorTests` | bez `FilterOperator` query i in-memory dostanou `contains`; `FilterOperator="equals"` se propíše do `DataTableQuery` i `InMemoryDataProvider`. |
| lokalizační kompletnost | nové klíče v en+cs+sk+fr (test repa hlídá i sk, ne jen fr). |

## 7. Rozhodnutí (implementátor nehádá)

1. **Avatar sloupec v tabulce MVL — VYKRESLIT.** `GetAvailableColumns` (ř. 972) už Avatar nabízí a `_visibleColumns` ho umí zapnout (ř. 928), ale `RenderTableRow` (ř. 1161–1174) pro něj nemá `<td>`. Přidat buňku `if (_visibleColumns.Contains(ColumnKeys.Avatar))` s `<img class="tm-mvl-list-avatar">` když `GetAvatarUrl` není prázdné, jinak prázdné `<td>` (kvůli colspan/zarovnání). **Nenabízet ho pryč** — uživatel ho ve view už může zapnout a dnes tiše zmizí. Zapsat `upsert_decision` kód `mvl-avatar-column-renders-in-table`.
2. **Karta + AllowSelection:** avatar není v `else if`. Checkbox řádek a avatar mohou být spolu. Když avatar není, checkbox řádek zůstane jak je.
3. **Osiřelé klíče NESMAZAT.** `TmDataTable_SaveView` je veřejný příklad v `JsonDocumentation/Abstractions/ITmLocalizer.json` a v `DocumentedTranslationOverrideLocalizerTests`. `TmDataTable_ViewName` / `TmDataTable_DefaultView` jsou shipping klíče v en/cs/sk/fr a v `MockTmLocalizer`, bez razor konzumenta. Smazání by byla nedokumentovaná breaking change. V CHANGELOG Fixed jednou větou: ponecháno, protože jde o veřejné override klíče.
4. **České klíče ViewManageru už v `TmResources.cs.json` jsou** (ř. 5000–5024, 25 klíčů). Plán tvrdil „4 z 25“ — to je zastaralé. Nepřepisuj existující překlady. Doplň jen `TmViewManager_CopySuffix`: en `(copy)`, cs `(kopie)`, sk `(kópia)`, fr `(copie)`. Stejné do `MockTmLocalizer` (en i cs mapy). Save-current v `TmViewManager.razor.cs` ř. 281: `Loc["TmViewManager_CopySuffix"]`, žádný hardcoded `"(Copy)"`.
5. **Radio name:** `name="@($"tm-view-scope-{_instanceId}")"` na obou inputech.
6. **Inline style** ř. 52 a 93 → třída `tm-view-empty` v `src/Tempo.Blazor/wwwroot/css/components/_data-table.css`. Padding `var(--tm-space-3)`, font `var(--tm-font-size-sm)`. Žádný hex/rgba. Po změně CSS regeneruj `tempo-blazor.bundled.css` stejným postupem jako repo (ne ruční rozbití dashboard diffu v tom souboru — bundled CSS je taky dirty od dashboardu; **necommituj celý bundled soubor**, pokud do něj spadne dashboard. Když bundler přepíše celý soubor, nech ho unstaged a v reportu to řekni).
7. **ColumnTitle** aditivní parametr. Klíč `TmDataTable_FilterColumn` = `"Filter: {0}"` / `"Filtr: {0}"` / `"Filter: {0}"` (sk) / `"Filtre : {0}"`. Aria-label i placeholder. Když `ColumnTitle` chybí, dnešní text bez dvojtečky.
8. **Clear ✕ i pro select** (`TmColumnFilter.razor` ř. 41 je dnes jen u textové větve — ověř a přidej i k selectu).
9. **FilterOperator** `string?` na `TmDataTableColumn`, default null. `TmColumnFilter` ho propíše místo hardcoded `"contains"` (ř. 86). Null → `"contains"`.

## 8. E2E

Jen:

- stávající `DataTableErgonomicsE2ETests`, `TmDataTableSortButtonE2ETests` musí zůstat zelené,
- nový smoke: cs kultura, texty ViewManager panelu česky na `src/Tempo.Blazor.Demo.SharedUI/Pages/DataTablePage.razor`.

Zakázáno: `DebtTokenBaselineScreenshots`, jakákoliv regenerace PNG, nový screenshot test.

## 9. Dokumentace

- JSON overlay `JsonDocumentation/Components/TmColumnFilter.json` a `TmDataTableColumn.json` (ColumnTitle, Value/ValueChanged, FilterOperator).
- `dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate` a `validate --fail-on-drift`.
- Wireframe stencil `data-table` a `view-manager` beze změny — ověř, neupravuj.
- CHANGELOG: sekce Fixed (cs copy suffix, radio name, sekce Seskupení, filtry, avatar). Breaking / Migration s před/po:
  - placeholder/aria-label filtru se **při nastaveném ColumnTitle** změní z „Filtr“ na „Filtr: {sloupec}“. Bez ColumnTitle beze změny.
  - `name="viewScope"` → `name="tm-view-scope-{id}"`. Selektor `[name=viewScope]` přestane fungovat.

## 10. Git

Commit message česky, styl repa: `fix(data-table): fáze 1 — lokalizace ViewManageru, filtry a avatar MVL`. Push až po review. Nikdy `git add -A`.
