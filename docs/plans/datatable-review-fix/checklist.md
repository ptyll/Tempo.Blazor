# Kontrolní seznam nálezů — plán 6166573c (datatable-review-fix)

Každý nález = opravující commit fáze + test, který ho prokazuje. Commity: f1=`090c59e1`, f2=`5ee289a8`,
f3=`ddadecc5`, f4=`145ebee3`, f5=`e41ee1e3`, f6=`b6356559`, f7=`89ecf5a3`, f8=`9c259307`, f9=`7735e035`,
f10=`acd59dc3`, f11=`b956d72c`, f12=`4c52cac6`, f13=`6aa72d32`.

| # | Nález | Commit | Prokazující test |
|---|---|---|---|
| 1 | Grid Enter/Space dvojí aktivace | f4 `145ebee3` | `TmDataTableGridKeyboardTests` + E2E `DataTableGridKeyboardE2ETests` (jedna aktivace na stisk) |
| 2 | Grid fokus se nepřesouvá, chybí preventDefault | f4 `145ebee3` | `TmDataTableGridKeyboardTests` (`JSInterop.VerifyInvoke("tmDataTable.focusGridCell")`), JS `attachGridKeys` krytý `TmDataTableGridKeyboardTests.Grid_*` a node-testy E2E |
| 3 | Grid bez tab stopu (Selectable/HasDetail, clamp) | f4 `145ebee3` | `TmDataTableGridKeyboardTests.Grid_SelectableAndDetail_HasExactlyOneTabStop_DefaultingToFirstDataColumn`, `Grid_PageShrink_KeepsExactlyOneTabStop`, `Grid_End_WithEditActions_KeepsExactlyOneTabStop` |
| 4 | Grid ve virtualizaci/seskupení/editaci, aria-rowcount/rowindex | f4 `145ebee3`, f3 | `TmDataTableGridKeyboardTests.Grid_Virtualized_CellsAreGridCells`, `Grid_CollapseGroup_KeepsExactlyOneTabStop`, `Grid_InnerControls_AreNotTabStops` |
| 5 | Výchozí markup změněn, chybí snapshot testy | f2 `5ee289a8`, f3 `ddadecc5` | `DefaultMarkupSnapshotTests` (golden soubory generované z `259f900a` přes worktree) |
| 6 | CHANGELOG Breaking/Migration neúplný | f3 `ddadecc5` | `ApprovedSnapshots_AreListedInChangelog` + ruční revize CHANGELOG sekce Breaking / Migration |
| 7 | Smazané MVL toolbar testy, oslabená group zone aserce | f1 `090c59e1` | `TmMultiViewListToolbarTests` (obnovené testy), `TmDataTableGroupingTests.DataTable_ShowGrouping_RendersDropZone` (asercí `.tm-data-table-group-zone` Count=1) |
| 8 | eval v TmOverlayPanel a TmFilterBuilder, globální querySelector | f5 `e41ee1e3` | `TmOverlayPanelEvalGuardTests` (grep zákaz `eval`/globální querySelector v JS) + `TmFilterBuilderFocusTests` |
| 9 | Duplicitní overlay TmDataTableColumn, nekonzistentní umístění overlayů | f1 `090c59e1` | `ComponentDocumentationFreshnessTests` + nový test duplicity overlayů (fázová podúloha 1.4) |
| 10 | 9 padajících ochranných testů | f1 `090c59e1` | ochranná sada: `TmResourceCompletenessTests`, `ReducedMotionSelectorTests`, `DesignTokenDefinitionTests`, `CssTokenAuditTests`, `MarkupClassCoverageTests`, `TmOverlayPanelDialogNamingTests`, `OverlayPanelComputedStyleRegressionTests`, `ComponentChangeCarriesJsonTests` |
| 11 | Bundled CSS neaktuální | f1 `090c59e1` | `CssBundleFreshnessTests` (bundle obsahuje `tm-mvl-group-card`, `tm-filter-builder--popover`, `tm-data-table-group-chevron`) |
| 12 | ExportSelectedOnly bez implementace | f7 `89ecf5a3` | `TmDataTableExportTests` (sekce Selected-only: `ExportScope_AllowExportSelectedFalse_HasNoScopeSection` a další) |
| 13 | ShowResultPreview statický text | f8 `9c259307` | `TmFilterBuilderResultPreviewTests` + `FilterResultPreviewAutoWireTests` |
| 14 | ColumnMenuMode.Menu prázdné tlačítko | f6 `b6356559` | `TmDataTableColumnMenuTests`, `TmDataTableColumnMenuKeyGuardTests` |
| 15 | TableCellLayout.Stacked bez CSS | f9 `7735e035` | `TmMultiViewListPhase9Tests` (Stacked markup + `_multi-view-list.css` pravidlo) |
| 16 | CardActivation.StretchedLink 0×0, dvě tab zastávky, dvojí aktivace | f5 `e41ee1e3` | `TmMultiViewListStretchedLinkTests` |
| 17 | FilterBuilderPresentation.Auto mrtvá hodnota, Popover vlastní CSS | f9 `7735e035` | `TmFilterBuilderPresentationTests` (Auto/Popover přes TmOverlayPanel) |
| 18 | AllowSetDefault obchází provider, hvězdička se neaktualizuje | f9 `7735e035` | `TmViewManagerPhase9Tests` |
| 19 | Container queries neexistují, viewport media query | f10 `acd59dc3` | `TmBreakpointsTests.ContainerQueries_UseRangeSyntax_AndOnlyTheFourConstants` + CSS `@container` v `_data-table.css` |
| 20 | Compact pager na desktopu, malé dotykové cíle | f10 `acd59dc3` | `TmDataTableMobileModeTests`, `TmPaginationTests` (pager podle kontejneru, `--tm-touch-target`) |
| 21 | Cards skrývají řazení/filtry, neviditelné fokus zastávky | f10 `acd59dc3` | `TmDataTableMobileFiltersTests`, `TmDataTableMobileLayoutTests` |
| 22 | Panely ViewManager/MVL group s absolutní pozicí v top layer | f10 `acd59dc3`, f5 | `TmDataTableMobileOverlayTests`, `TmViewManagerMobilePresentationTests` (TmOverlayPanel sheets) |
| 23 | Magické 44 px, nepoužitý token, MVL nth-child(2) | f10 `acd59dc3` | `DesignTokenDefinitionTests` + `TmMultiViewListResponsiveTests` |
| 24 | Tablet 1024 překrytí sloupců | f10/f13 | E2E `DataTableMobileE2ETests` (bounding boxy se nepřekrývají na 1024) |
| 25 | RTL (left/text-align left) | f10 `acd59dc3` | `CssTokenAuditTests` (logické vlastnosti) + E2E RTL edge-case screenshot testy |
| 26 | R18: ShowItemCount/summary/column menu mimo ToolbarMode | f11 `b956d72c` | `TmDataTableToolbarTests` (ToolbarMode filtry), `TmMultiViewListToolbarTests` |
| 27 | setIndeterminate každý render, SHA-256 id, LINQ per row | f11 `b956d72c` | `TmDataTablePhase11Tests` |
| 28 | Plurály TotalItems/ActiveFilters/ItemCount | f11 `b956d72c` | `TmResourceCompletenessTests` (plurálové rodiny) + `TmDataTablePaginationTests` |
| 29 | aria-live u počtů, ItemCount = velikost stránky | f11 `b956d72c` | `TmDataTablePhase11Tests`, `TmMultiViewListPhase11Tests` |
| 30 | Dva zapisovatelé _activeFilters | f11 `b956d72c` | `TmDataTablePhase11Tests` (jeden authoritativní zápis, parametr set) |
| 31 | TmColumnPicker.Open mutuje dítě | f11 `b956d72c` | `TmColumnPickerTests` |
| 32 | MVL radiogroup label, roving, fokus | f5 `e41ee1e3` | `TmMultiViewListViewSwitcherTests` |
| 33 | Set default aria-label bez názvu, ★ bez textu, aktivní pohled jen barvou | f9/f11 | `TmViewManagerPhase9Tests` (aria-label, textový doprovod ★) |
| 34 | Save current = SaveChanges text | f11 `b956d72c` | `TmViewManagerTests` / lokalizační klíče (en/cs/sk/fr) |
| 35 | Dark kontrast pageru, focus-visible exportu a grid buněk, checkboxy | f11 `b956d72c` | CSS review + `OverlayPanelComputedStyleRegressionTests`; kontrast doložen v commit zprávě fáze 11 (primary-solid) |
| 36 | Toolbar tlačítka nekonzistentní | f11 `b956d72c` | `TmDataTableToolbarOutlineTests` |
| 37 | MVL avatar sloupec za datem, prázdné td | f11 `b956d72c` | `TmMultiViewListPhase11Tests` |
| 38 | Hlavička skupiny v kartách splývá s tělem, chevron stav | f3 `ddadecc5` | `TmDataTableGroupingAccessibilityTests` (chevron `--open`, aria-expanded), CSS `--card-surface` pozadí |
| 39 | Escape z vnořeného date pickeru | f5 `e41ee1e3` | `TmDataTableMobileOverlayTests` / E2E Escape close (TmOverlayPanel) |
| 40 | ShowActiveViewName bez truncation | f9 `7735e035` | `TmViewManagerTests` (CSS truncation) |
| 41 | Hex/px fallbacky, nedeklarované komponentové tokeny | f1 `090c59e1` | `CssTokenAuditTests`, `DesignTokenDefinitionTests` |
| 42 | DataTableEnums doc chyby | f1 `090c59e1` | `ComponentDocumentationFreshnessTests` (bundly) + XML doc revize |
| 43 | Demo stránky chybí | f12 `4c52cac6` | `DataTableDemoPageTests` |
| 44 | Wireframe stencily | f12 `4c52cac6` | `StencilPackRegistryParityTests` |
| 45 | E2E chybí, screenshot testy bez asercí, orphan/stale PNG | f13 `6aa72d32` | `DataTableRedesignBaselineScreenshots`, `DataTableGridKeyboardE2ETests`, `DataTableMobileE2ETests` atd. (asercemi, orphan úklid v commitu) |
| 46 | cs kultura smoke | f13 `6aa72d32` | E2E cs-culture smoke spec (české texty + plurály) |
| 47 | Rozhodnutí a nedodělky nejsou v PromptHelper kanálech | fáze 14 (tento commit) | `upsert_decision` ×6 (`dt-*`), `plan_report_remaining`, ResultSummary |

Stav: všech 47 nálezů má commit a prokazující test (fáze 14 doplňuje položku 47 zápisem do PromptHelperu).
