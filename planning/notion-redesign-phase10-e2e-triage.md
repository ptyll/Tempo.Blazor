# Notion E2E lane fáze 10 — triage (notion-ph10.trx)

Zdroj: `tests/Tempo.Blazor.E2E/TestResults/notion-ph10.trx` (celkem 537, prošlo 509, selhalo 28, error 0).
Triage jen z chybových hlášek trx, bez rerunů. F11 = `fze-11-oprava-zbvajcch-chyb-shell-menu-historie-omezen-ai-a-mobil` (mimo DB),
F12 = `fze-12-oprava-zbvajcch-chyb-databze-toolbar-tabulka-board-kalend-timeline` (DB).

Souhrn tříd: produkt 22, test 3, flaky 3. Souhrn fází: F11 = 13, F12 = 15.

| Test | Třída | Příčina (jedna věta) | Fáze |
|---|---|---|---|
| `Phase6Menus_Shortcuts_AtEveryBand` | produkt | Klik na `notion-shortcuts-open` na 320 zachytí `.tm-toolbar-more` (pinned kopie pod More). | F11 |
| `CF23_PageHistoryDiffViewer_InlineSideBySideAndNoChange` | produkt | Po ukončení porovnání chybí `.tm-nph__preview` (`ExitDiff` vrací Empty bez `_selectedVersion`). | F11 |
| `Restrictions_ShowsEntriesAndInheritedBanner` | produkt | Po resize na 390 dialog nemá `.tm-nprd[data-layout='mobile']`. | F11 |
| `History_SelectCompareRestoreAndMobilePreview` | produkt | `notion-page-settings` v mobilním More na 320 není viditelný (timeout 30 s). | F11 |
| `AiSheet_FillsThePhoneViewport` | produkt | `/` v odstavci na telefonu neotevře `.tm-notion-slash` (timeout 15 s). | F11 |
| `Phase6Menus_Templates_AtEveryBand` | produkt | Po kliku na New page se galerie `.tm-ntg` vůbec neobjeví (timeout 10 s). | F11 |
| `EmojiPicker_OpensFromThePageIcon_AndSheetsOnMobile` | produkt | Klik na ikonu stránky neotevře `.tm-modal-overlay` emoji pickeru (timeout 10 s). | F11 |
| `PageHistory_ClosePanel` | produkt | Otevřený panel historie nemá `.tm-nph__close-btn` (timeout 5 s). | F11 |
| `PageHistory_RestoreVersion_AddsAsNewHead` | produkt | Restore neotevře potvrzovací `.tm-nph-confirm` (timeout 5 s). | F11 |
| `EB13_RestoreConfirmAndRestoredPageContent_AreCaptured` | produkt | Stejný chybějící `.tm-nph-confirm` při restore (timeout 5 s). | F11 |
| `PageRestrictions_AclDialogEffectivePermissionsAndEdges` | produkt | Dialog omezení nemá `.tm-nprd__primary` (timeout 30 s). | F11 |
| `MarkdownCodeBlock_EmptyAndInvalidMarkdownDoNotCrash` | produkt | Prázdný markdown vyrenderuje text spreadsheet placeholderu místo prázdného náhledu (regrese proti notion-ph6). | F11 |
| `Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` | produkt | Spreadsheet edit modal na 390 nemá `.tm-notion-spreadsheet-edit-modal__footer` (timeout 30 s). | F11 |
| `Toolbar_DoesNotOverflow_AndSearchFiltersRows` | produkt | `.tm-db__header` přetéká: scrollWidth 308 > clientWidth 290. | F12 |
| `Table_StaysReadable_AndFirstColumnSticks` | produkt | Sticky první sloupec po scrollu (left 40) je vlevo od wrapu (50). | F12 |
| `Phase4_DbImportExport` | produkt | Tlačítko `.tm-db__tool-btn[title*='Import']` se neobjeví (timeout 30 s). | F12 |
| `DbImportExport_At390_Sheet_WithMappingCards` | produkt | Stejné chybějící Import tlačítko na 390 (timeout 30 s). | F12 |
| `Phase4_DbPropertiesPopoverAndSheet` | produkt | Trigger `notion-db-properties-open` se neobjeví (timeout 30 s). | F12 |
| `DbPropertiesPanel_At390_PresentsAsBottomSheet` | produkt | Stejný chybějící `notion-db-properties-open` na 390 (timeout 30 s). | F12 |
| `EB9_DatabaseViews_EmptyOneMany_AreCaptured` | produkt | Záložka view „Calendar“ se na seedované stránce neobjeví (timeout 30 s). | F12 |
| `Phase1_CalendarOverflow` | produkt | Stejná chybějící záložka „Calendar“ (timeout 30 s). | F12 |
| `Surfaces_Screenshots` | produkt | `board-move-to-menu.png` má 1880 B, tedy pod prahem 2 KB (oříznuté/prázdné menu). | F12 |
| `MediaLibrary_Search_FiltersItems` | test | Test čeká na `.tm-media-library__search-input`, který dialog `.tm-media-dialog` nemá (timeout 30 s). | F11 |
| `MediaLibrary_LibraryTab_ShowsItems` | test | Mřížka knihovny je prázdná — seed/fixture testu nedodá žádnou položku. | F11 |
| `MediaLibrary_SelectItem_SetsImageOnBlock` | test | Stejný prázdný seed: `.tm-media-library__item` se neobjeví (timeout 5 s). | F11 |
| `Notification_Bell_ClickOpensPanel` | flaky | „Execution context was destroyed, most likely because of a navigation“ uprostřed kliku na zvonek. | F11 |
| `EB11_SidebarNavigationStates_AreCaptured` | flaky | Timeout 60 s na `.tm-notion-sidebar` během nedokončené navigace na `/notion-editor`. | F11 |
| `Timeline_DragBar_Desktop` | flaky | `WaitForFunctionAsync` (30 s) na posun `style.left` po drag — commit drag gesto nedoručil. | F12 |

## Clustery předané do F11 / F12

| Cluster | Fáze | Testy |
|---|---|---|
| Menu fáze 6 mimo známé carry-forwardy | F11 | `Phase6Menus_Templates_AtEveryBand`, `EmojiPicker_OpensFromThePageIcon_AndSheetsOnMobile` |
| Page history panel | F11 | `PageHistory_ClosePanel`, `PageHistory_RestoreVersion_AddsAsNewHead`, `EB13_RestoreConfirmAndRestoredPageContent_AreCaptured` |
| Restrictions ACL dialog | F11 | `PageRestrictions_AclDialogEffectivePermissionsAndEdges` |
| Spreadsheet edit modal na 390 | F11 | `Phase6_NotionEditModal_At390_IsMobile_AndKeepsTheFooterInView` |
| Media library (test/seed) | F11 | `MediaLibrary_Search_FiltersItems`, `MediaLibrary_LibraryTab_ShowsItems`, `MediaLibrary_SelectItem_SetsImageOnBlock` |
| Flaky shell navigace | F11 | `EB11_SidebarNavigationStates_AreCaptured` |
| DB toolbar — Import a Properties | F12 | `Phase4_DbImportExport`, `DbImportExport_At390_Sheet_WithMappingCards`, `Phase4_DbPropertiesPopoverAndSheet`, `DbPropertiesPanel_At390_PresentsAsBottomSheet` |
| DB view tab Calendar | F12 | `EB9_DatabaseViews_EmptyOneMany_AreCaptured`, `Phase1_CalendarOverflow` |

Ostatní červené testy už vlastní existující subtasky F11 (`Phase6Menus_Shortcuts_AtEveryBand`, `CF23_*`,
`Restrictions_ShowsEntriesAndInheritedBanner`, `History_SelectCompareRestoreAndMobilePreview`,
`AiSheet_FillsThePhoneViewport`, regrese `MarkdownCodeBlock_*` + `Notification_Bell_ClickOpensPanel`)
a F12 (`Toolbar_DoesNotOverflow_AndSearchFiltersRows`, `Table_StaysReadable_AndFirstColumnSticks`,
phase-9 `Timeline_DragBar_Desktop` + `Surfaces_Screenshots`, DB regrese vč. `EB9_*`).
