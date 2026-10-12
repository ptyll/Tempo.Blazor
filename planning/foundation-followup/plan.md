# Tempo.Blazor — Redesign: Základ, dodělávky (core follow-up)

Plán dokončuje všechny otevřené úkoly plánu faf503eb („Redesign: Základ“), které nemají jiného vlastníka. Každá fáze odkazuje na přesnou sekci analýzy v `planning/foundation-followup/` — tam jsou ověřené cesty, čísla řádků, současný kód, navržená změna a testy. **Než začneš fázi, otevři a přečti uvedenou sekci analýzy celou.** Rozhodnutí uživatele: `planning/foundation-followup/user-decisions.md` (závazná). Pořadí fází = pořadí v dokumentu.

## Fáze 1: Repozitář — konce řádků, „env“ unit testy a CORS demo API

Cíl fáze: celá unit sada je zelená i na Windows s `core.autocrlf=true`, repo vynucuje LF a E2E (včetně Smoke lane) poběží proti hostu na libovolném localhost portu.

Zdroj: remaining tasks 9e46d2da, 17b6d82d, 4770bcfb, efdc933b. Analýza: `planning/foundation-followup/cluster-A.md` sekce **A1** (konce řádků), **A3** (CSS bundle guard), **A10** část „smoke CORS“ (A10b).

### Pevná rozhodnutí
- `.gitattributes`: přidej `* text=auto eol=lf` a `eng/release-evidence/** -text` (evidence logy zůstávají byte-for-byte). Existující pravidla v souboru ponech.
- Frozen baseline `TmTabsHeaderSlotsTests` je SPRÁVNÝ (po odstranění `\r` má 1269 B a zmrazený hash) — baseline NEregeneruj; test před hashováním normalizuje CRLF→LF.
- `ReleaseGateFilterTests.ReadRepoFile` (cca ř. 1583) normalizuje konce řádků (`Replace("\r\n", "\n")`) — stejně všechny ostatní čtečky workflow YAML v `tests/Tempo.Blazor.Tests/Packaging/`, které padají (FullCloneWorkflowTests, PublishEnvironmentGateTests, ReleaseEvidenceTests).
- CORS demo API: nová třída `DemoCorsPolicy` v `src/Tempo.Blazor.Demo.Api` — v Development povolí libovolný loopback origin (`SetIsOriginAllowed(o => Uri.TryCreate(o, UriKind.Absolute, out var u) && u.IsLoopback)`), jinak seznam z konfigurace `Cors:AllowedOrigins` (výchozí = dnešní 5010/7106). Přesný kód: cluster-A.md A10 (3).
- CSS bundle guard: oba publish workflow (`.github/workflows/publish-nuget.yml` i `publish-nuget-org.yml`) mají krok touch + diff čerstvosti `tempo-blazor.bundled.css`; test ověří přítomnost a pořadí kroků (A3). Lokální bundler test NEpřidávat.

### Pasti
- Renormalizaci (`git add --renormalize .`) spusť až po úpravě `.gitattributes` a jen když je `git status --porcelain` prázdný; změnit by se mělo jen ~13 evidence logů → ty díky `-text` zůstanou, takže výsledný commit má mít jen `.gitattributes` (ověř `git diff --cached --stat`).
- Po commitu renormalizace obnov pracovní kopii příkazem `git checkout -- .` (jen když je `git status --porcelain` prázdný), ať testy čtou LF soubory. NIKDY nepoužívej `git reset --hard` ani `git rm --cached -r`.

- [ ] RED: `tests/Tempo.Blazor.Tests/Packaging/ReleaseGateFilterTests.cs` → `TheWorkflowReader_SegmentsACrlfCopyLikeTheLfOriginal` a `tests/Tempo.Blazor.Tests/Navigation/TmTabsHeaderSlotsTests.cs` → `TheBaselineHash_DoesNotDependOnTheCheckoutLineEndings` (podle cluster-A.md A1 (4)); commit „test(repo): RED …“.
- [ ] Oprav čtečky souborů v Packaging testech a hashování v TmTabsHeaderSlotsTests tak, aby normalizovaly CRLF→LF (A1 (3)); spusť 10 dříve padajících testů — všechny zelené.
- [ ] Uprav `.gitattributes` (`* text=auto eol=lf`, `eng/release-evidence/** -text`), proveď renormalizaci dle Pastí, commit „chore(repo): enforce LF …“.
- [ ] RED: `tests/Tempo.Blazor.Demo.Api.Tests/CorsPolicyTests.cs` — origin `https://localhost:7116` v Development dostane `Access-Control-Allow-Origin`; `https://example.com` ne; v Production platí jen `Cors:AllowedOrigins` (A10 (4)).
- [ ] Implementuj `DemoCorsPolicy` a zapoj ji v `src/Tempo.Blazor.Demo.Api/Program.cs` místo pevného seznamu; `appsettings.json` dostane `Cors:AllowedOrigins` s dnešními dvěma originy.
- [ ] RED + fix CSS bundle guardu: testy `BothPublishWorkflows_CarryTheReleaseStepsThatOnlyExistInCi` a `TheBundleFreshnessCheck_RunsAfterTheBuild_AndTheTouchBeforeIt` (A3 (4)), pak doplň kroky do `publish-nuget-org.yml` a markery do `RequiredReleaseMarkers`.
- [ ] Spusť Smoke E2E lane proti vlastnímu WASM hostu na nestandardním portu (např. 7116): `scripts/run-e2e-smoke.ps1` nebo `dotnet test tests/Tempo.Blazor.E2E --filter "TestCategory=Smoke"` — všechny testy zelené včetně `Phase12_SaveFailure_CreatesOfflineDraftRetryAndBeforeUnloadState`.
- [ ] Aktualizuj `docs/redesign-foundation.md` (tabulka follow-upů: řádek o 10 env failures odstraň) a do CHANGELOG pod „Changed“ zapiš: demo API v Development povoluje libovolný loopback origin; repo vynucuje LF.
- [ ] Ověření: `dotnet test tests/Tempo.Blazor.Tests -f net10.0` = 0 selhání; `dotnet test tests/Tempo.Blazor.Demo.Api.Tests` zelené; `git ls-files --eol | Select-String 'i/crlf'` prázdné.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 2: Testová hygiena — registry ikon, CultureScope, EOL commit test, formátování

Cíl fáze: odstranit známé testové závody a nestabilní/vakuózní testy.

Zdroj: ec0299b9 #3, 4c92ba05 #1 #2, c51ddf81 #3, f4c27b20 #2. Analýza: cluster-A.md **A4, A5, A6, A7**.

### Pevná rozhodnutí
- `TmIconRegistryTests` a `StencilPackRendererTests` dostanou `[Collection("IconRegistry")]`; definice `[CollectionDefinition("IconRegistry", DisableParallelization = true)]` v novém souboru `tests/Tempo.Blazor.Tests/Icons/IconRegistryCollection.cs` (A5 (3)).
- Nový helper `tests/Tempo.Blazor.Tests/Localization/CultureScope.cs` (`IDisposable`, nastaví CurrentCulture+CurrentUICulture, v Dispose vrátí původní) — použij ho ve všech testech `RelativeTimeFormatterTests`, které mění kulturu (A6 (3)).
- `ComponentChangeCarriesJsonTests.EndOfLineNormalizationCommits_AreClean`: commit `975a75be` nahraď `98cc1c99` a přidej „teeth“ větev: bez `--ignore-cr-at-eol` by diff [Parameter] řádky ukázal (A4 (3)).

- [ ] RED: `RelativeTimeFormatterTests.Formatting_leaves_the_ambient_culture_as_it_found_it` (A6 (4)) — selže, protože testy kulturu nevrací.
- [ ] Přidej `CultureScope` a přepiš helpery `Formatter/Cs/En/Fr` v `RelativeTimeFormatterTests` na `using var _ = new CultureScope(...)`.
- [ ] Rozšiř `RelativeTimeRegistrationTests.DocumentedRegistrationSnippet_Compiles` o porovnání těla třídy `UserTimeProvider` (`ClassBlock`) a z `DocumentedUserZoneRegistration.cs` odstraň zakomentovanou kopii (A6 (3)).
- [ ] Přidej `IconRegistryCollection` a atributy na obě třídy; ověř 10× za sebou `dotnet test --filter "FullyQualifiedName~TmIconRegistryTests|FullyQualifiedName~StencilPackRendererTests"` bez selhání.
- [ ] Uprav `EndOfLineNormalizationCommits_AreClean` dle A4 (sha + komentář + raw-diff větev).
- [ ] `tests/Tempo.Blazor.Tests/Toolbar/TmToolbarOverflowTests.cs` (konec souboru, `}}`) rozděl na dva řádky (A7).
- [ ] Ověření: cílené filtry výše + celá unit sada zelená.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 3: JSON dokumentace — generátor členů (expression-bodied, record parametry)

Cíl fáze: generátor dokumentace správně vypisuje expression-bodied vlastnosti (jako Property, ne Field), `{ get => … }` vlastnosti a poziční parametry recordů.

Zdroj: 02dd6146. Analýza: cluster-A.md **A2** (kompletní návrh parseru, 10 testových případů).

### Pevná rozhodnutí
- Scanner je `SourceDocParser.ExtractMembers` v `JsonDocumentation/JsonDocumentationGenerator/PackageDocumentationGenerator.cs` (cca ř. 1336). Nová logika do nového partial souboru `SourceDocParser.Members.cs` vedle něj (A2 (3)).
- Změna Field→Property (~490 záznamů v bundlech) se přijímá; CHANGELOG „Changed“ (Documentation): „JSON bundly nově uvádějí expression-bodied vlastnosti jako Property a obsahují poziční parametry recordů“.

- [ ] RED: `SourceDocParserMemberTests` (10 případů dle A2 (4), včetně skutečného `TmLayoutContext` — Mode/Resolved/CssModifier musí být členy).
- [ ] Implementuj `SourceDocParser.Members.cs` (expression-bodied jedno- i víceřádkové, accessor-bodied `{ get => }`, poziční parametry recordu, vyloučení `=>` z field smyčky).
- [ ] Spusť `dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate`, zkontroluj diff bundlů (jen očekávané změny), commitni bundly (LF).
- [ ] CHANGELOG záznam (viz Pevná rozhodnutí).
- [ ] Ověření: `... validate --fail-on-drift` exit 0; `dotnet test --filter "FullyQualifiedName~SourceDocParser|FullyQualifiedName~ComponentDocumentationFreshness|FullyQualifiedName~GeneratedDocumentation"` zelené.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 4: Stabilizace E2E a pokrytí render módů

Cíl fáze: odstranit známé flaky E2E testy, opravit vakuózní asserce a doplnit E2E pro top layer na Blazor Server a InteractiveAuto a pro šířku TmCopyButton.

Zdroj: 058c3487, 835acf83 #5, 08a22db5 #1 #3, 3f1747c0, 64114d91, ec0299b9 #4. Analýza: cluster-A.md **A8, A9, A10 (A10a, A10c), A11**.

### Pevná rozhodnutí
- MobileActionBar flaky: příčinou je `html{scroll-behavior:smooth}`. Oprava testů (ne komponenty): sdílený `WaitForLayoutToSettleAsync` v `PlaywrightTestBase`, scroll `behavior:'instant'`, `focus({preventScroll:true})` po instant scrollu, `ToBeFocused` s 15 s timeoutem a diagnostikou (A8 (3)). Produktové zpevnění komponenty = follow-up (plan_report_remaining).
- EditorShell thresholds demo (`EditorShellPage.razor`): bind `LeftOpen`/`RightOpen`, `CollapsedPanels` zůstává nevázaný (regresní případ G1) (A9).
- Top-layer E2E pro Server a InteractiveAuto: nový soubor `tests/Tempo.Blazor.E2E/OverlayTopLayerRenderModesE2ETests.cs` se třídami pro Server a IA (6 testů dle A10 (4)); důvěryhodný dev certifikát NENÍ potřeba (`IgnoreHTTPSErrors`).
- `InteractiveAutoTests.Scheduler_ViewsWork` je hotový (prošel) — jen ho zařaď do ověření.

- [ ] Přidej `WaitForLayoutToSettleAsync` do `tests/Tempo.Blazor.E2E/PlaywrightTestBase.cs` a uprav `MobileActionBarE2ETests` (`Demo_1440_ForcedVariant_MorePopover_PaintsSurface_AndReturnsFocus`, `Demo_390_AutoCard_BarSticksAtViewportBottom_WhileTheCardStraddlesTheFold`) dle A8 (3).
- [ ] Ověř stabilitu: třídu `MobileActionBarE2ETests` spusť 5× za sebou — 0 selhání; zaznamenej běhy v MCP.
- [ ] RED: `EditorShellE2ETests` → `…EscapeFromInsideTheThresholdsSheet_ClosesItAndLeavesTheMainSheetOpen` (A9 (4)); pak bind `LeftOpen`/`RightOpen` v demu a uprav asserci kolem ř. 963.
- [ ] Vytvoř `OverlayTopLayerRenderModesE2ETests.cs` (Server 7107 a IA 7108; palette nad drawerem, re-raise toastu, Server reconnect, obě větve IA) dle A10 (3)/(4).
- [ ] Přidej E2E `CopyButton_ShowLabel_KeepsItsWidth_AcrossCopyAndCopied` (en + cs) a negativní kontrolu do `AdditiveExtensionsE2ETests.cs` (A11).
- [ ] plan_report_remaining: „MobileActionBar More menu: produktové zpevnění fokusu při otevření během plynulého scrollu (A8 Q5)“.
- [ ] Ověření: `MobileActionBarE2ETests`, `EditorShellE2ETests`, `OverlayTopLayerRenderModesE2ETests`, `AdditiveExtensionsE2ETests`, `InteractiveAutoTests` zelené na vlastních hostech (WASM, Server 7107, IA 7108).
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 5: Focus trap — tabbable cyklus, modalOwnsEscape, návrat fokusu po zavření toastu

Cíl fáze: Tab nikdy neuteče z bottom-sheet menu, Escape modálu nezablokuje později aktivovaný nemodální trap a zavření toastu klávesnicí vrátí fokus do trapu.

Zdroj: f4c27b20 #1, 08a22db5 #2, c51ddf81 #4. Analýza: `planning/foundation-followup/cluster-B.md` sekce **6, 7, 3**.

### Pevná rozhodnutí
- `src/Tempo.Blazor/wwwroot/js/tm-focus-trap.js`: tabHandler počítá first/last jen z **tabbable** prvků (vyřadit `tabindex="-1"`, tedy roving položky menu) — sekce 7.
- `isTopmost` u modálního trapu nahradí nová `modalOwnsEscape()`, která ignoruje NEmodální trapy aktivované později — sekce 6.
- Trap si přes `focusin` pamatuje `lastFocused`; nový export `restoreTrapFocusIfLost()` volají `TmToastContainer` i `TmNotificationToastContainer`, když klesne počet toastů — sekce 3.

- [ ] RED node: `src/Tempo.Blazor/wwwroot/js/__tests__/tm-focus-trap.test.mjs` — (a) Tab z posledního tabbable prvku v sheetu s roving menu (položky tabindex=-1) cyklí na první prvek; (b) modální trap + později aktivovaný nemodální → Escape zavře modál; (c) `restoreTrapFocusIfLost` vrátí fokus na `lastFocused`.
- [ ] Implementuj tabbable filtr v tabHandleru.
- [ ] Implementuj `modalOwnsEscape()` a nahraď volání `isTopmost` pro modální trapy.
- [ ] Implementuj sledování `lastFocused` + export `restoreTrapFocusIfLost()`; zapoj v obou toast kontejnerech (C# volání přes existující focus-trap interop).
- [ ] E2E: toolbar More a `/mobile-action-bar` More na 390 (HasTouch) — Tab z první položky zůstane v sheetu (fokus nikdy na `<body>`); toast zavřený klávesnicí uvnitř otevřeného sheetu vrátí fokus do sheetu.
- [ ] CHANGELOG „Fixed“: tři opravy focus trapu.
- [ ] Ověření: `npm run test:overlay`; E2E `ToolbarOverflowE2ETests`, `MobileActionBarE2ETests`, `OverlayMobilePresentationE2ETests`, `EditorShellE2ETests` zelené.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 6: Aktivace focus scope až po promoci do top layer
<!-- depends-on: Fáze 5 -->

Cíl fáze: žádný overlay neaktivuje TmFocusScope dřív, než je jeho kořen v top layer; Gantt import dialog má guard po awaitu; přetékající scroll region se nestane počátečním fokusem.

Zdroj: c51ddf81 #1 #2, 26e1afd5 #4. Analýza: cluster-B.md sekce **1, 2, 8.4**.

### Pevná rozhodnutí
- Nový interní helper `src/Tempo.Blazor/Helpers/PromotedScopeGate.cs` s API dle sekce 1 (AfterRenderAsync(isOpen, stillOpen, root) → `ScopeActive`). Vzor: TmGanttImportDialog.
- Použij ho v TmLightbox, TmCommandPalette, TmKeyboardShortcutsHelp, TmModal, TmDialog a modálním TmDrawer (přesné řádky v sekci 1).
- Musí jít SPOLU s 8.4: scroll region dostane `data-tm-scroll-region` a initial-focus fallback v tm-focus-trap.js ho přeskočí — jinak se závod stane deterministickým.

- [ ] RED bUnit: pro každý z 6 overlayů test, že `TmFocusScope.Active` je false, dokud neproběhne promoce (mock TopLayerInterop se zpožděním) — sekce 1 (4); RED node: initial-focus fallback přeskočí `[data-tm-scroll-region]` (8.4).
- [ ] Implementuj `PromotedScopeGate`.
- [ ] Zapoj gate do TmModal a TmDialog.
- [ ] Zapoj gate do TmDrawer (modální), TmLightbox, TmCommandPalette, TmKeyboardShortcutsHelp.
- [ ] TmGanttImportDialog: nahraď vlastní vzor helperem (tím vznikne guard `IsOpen` po awaitu, sekce 2).
- [ ] Implementuj `data-tm-scroll-region` + přeskočení v tm-focus-trap.js (8.4).
- [ ] E2E: opakované otevření (3×) každého overlaye — počáteční fokus vždy na očekávaném prvku, nikdy `<body>`.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~Modal|FullyQualifiedName~Dialog|FullyQualifiedName~Drawer|FullyQualifiedName~Lightbox|FullyQualifiedName~CommandPalette|FullyQualifiedName~KeyboardShortcuts|FullyQualifiedName~GanttImport"`; `npm run test:overlay`; overlay E2E lane.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 7: Command palette — backdrop, combobox, kontrast zvýraznění, skupiny
<!-- depends-on: Fáze 6 -->

Cíl fáze: palette se zavírá klikem na pozadí, je to správný combobox s `aria-activedescendant`, zvýraznění má kontrast i v tmavém tématu a skupiny nemají axe chybu.

Zdroj: c51ddf81 #5 (palette), f4057a47, ec0299b9 #1 #2. Analýza: cluster-B.md sekce **4, 12**.

### Pevná rozhodnutí
- Klik na backdrop palette VŽDY zavře (bez parametru); klik do panelu ne (`@onclick:stopPropagation`).
- Input = `role="combobox"` + `aria-controls` na listbox + `aria-activedescendant` na zvýrazněnou položku; každá položka má stabilní `id`. Tím zmizí i axe `scrollable-region-focusable`.
- Skupiny: `li role="none"` > `div role="group" aria-labelledby` (místo `li role="group"`).
- Zvýrazněná položka: pozadí z tokenu, které dosáhne ≥3:1 vůči povrchu v light/dark/indigo (hodnotu a token urči podle sekce 12; přidej záznam do TokenContrastTests).
- CHANGELOG „Changed“ (markup) s ukázkou před/po.

- [ ] RED bUnit: backdrop klik zavře; input má role=combobox a aria-activedescendant = id zvýrazněné položky a mění se šipkami; skupiny mají li role=none > div role=group.
- [ ] RED TokenContrastTests: kontrast zvýraznění vs povrch ≥3:1 (light, dark, indigo, indigo-dark).
- [ ] Implementuj backdrop zavření v `TmCommandPalette.razor`.
- [ ] Implementuj combobox/aria-activedescendant a id položek.
- [ ] Oprav markup skupin a CSS (oddělovač zůstává CSS border).
- [ ] Oprav CSS zvýraznění (tokeny, bez hex).
- [ ] E2E axe: výchozí i seskupená palette bez critical/serious nálezů; klik na backdrop zavře; zvýraznění viditelné v dark (screenshot 1440 + 390).
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~TmCommandPalette|FullyQualifiedName~TokenContrast"`; strict audit; E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 8: Galerie a lightbox — tlačítka, návrat fokusu, šipky na mobilu
<!-- depends-on: Fáze 6 -->

Cíl fáze: položky TmImageGallery jsou skutečná tlačítka, po zavření lightboxu se fokus vrátí na náhled zobrazeného obrázku a šipky jsou na 390 px vidět celé.

Zdroj: 7800d54b, d49c9776, c51ddf81 #5 (lightbox), 835acf83 #2, b32847d0. Analýza: `planning/foundation-followup/cluster-D.md` sekce **3, 4, 5** a cluster-B.md sekce **5**.

### Pevná rozhodnutí
- Položka galerie = `<button type="button">` s per-instance id (keyboard-activation konvence); nový parametr `TmLightbox.RestoreFocusTargetId` (`string?`, výchozí null) předávaný do TmFocusScope; galerie ho nastaví na id náhledu obrázku, který je při zavření zobrazen.
- Pod 640 px šipky lightboxu leží přes okraje obrázku (CSS v cluster-D sekce 4); odstraň mrtvé legacy CSS v `_lightbox.css` (sekce 4).
- Redesign lightboxu podle mockupu NEdělat (mockup neexistuje) — b32847d0 zaznamenej jako vyřešené rozhodnutím.
- Breaking: markup položky galerie `div` → `button` (CHANGELOG Breaking/Migration s před/po); Added: `RestoreFocusTargetId` (JSON overlay `JsonDocumentation/Components/TmLightbox.json`, stejný commit).
- Wireframe stencil: beze změny (vizuál stejný) — uveď v commitu.

- [ ] RED bUnit: položky galerie jsou `button` s unikátním id; `TmLightbox` předá `RestoreFocusTargetId` do TmFocusScope.
- [ ] Implementuj tlačítka v TmImageGallery (CSS reset tlačítka přes tokeny).
- [ ] Přidej `RestoreFocusTargetId` (XML doc + JSON overlay ve stejném commitu) a napoj galerii.
- [ ] CSS šipek pod 640 px + odstranění mrtvého CSS; přegeneruj bundle.
- [ ] E2E `GalleryLightboxE2ETests`: klávesnicí otevřít, přejít na další obrázek, Escape → fokus na náhledu zobrazeného obrázku; na 390 (HasTouch) jsou obě šipky celé ve viewportu; screenshoty 1440/390.
- [ ] CHANGELOG (Breaking/Migration + Added); regenerace JSON bundlů.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~TmImageGallery|FullyQualifiedName~TmLightbox"`; JSON validate; strict audit; E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 9: Drobnosti z F2 — IME, ScrollRegionInterop, dokumentace, positionFloating

Cíl fáze: uzavřít zbylé drobnosti fáze F2 a sjednotit slovník voleb pozicování.

Zdroj: 26e1afd5 #1 #2 #3 #5 #6 #7, 07411bd1, c51ddf81 #8. Analýza: cluster-B.md sekce **8.1, 8.2, 8.3, 8.5, 8.6, 8.7, 9**.

### Pevná rozhodnutí
- IME: Enter v TmDialog promptu nesubmituje při `IsComposing`; na net8 `IsComposing` neexistuje → podmíněný helper (`#if NET9_0_OR_GREATER`), na net8 bez efektu (přijato) — 8.1.
- ScrollRegionInterop: generation/`_wanted` flag proti detach během attach, sledování změn obsahu (MutationObserver nebo pozorování dětí), resync při změně titulku, přestat chytat NullReferenceException — 8.3.
- positionFloating: jediný slovník `offset`/`margin`/`flip`/`shift`, aliasy (`anchorGap`, `clamp` atd.) odstranit — API je privátní a nevydané, není breaking (sekce 9).

- [ ] RED bUnit: Enter během kompozice nesubmituje (net9+); RED bUnit ScrollRegionInterop: detach během zpožděného importu nenechá observer (8.3 (4)).
- [ ] Implementuj IME guard (8.1).
- [ ] Implementuj opravy ScrollRegionInterop (8.3).
- [ ] RED node + implementace sjednocení slovníku positionFloating, migrace volajících a testů (sekce 9).
- [ ] Node test sheetu se 3 snapy (flick bez SwipeToDismiss sjede o jeden snap a zastaví na nejnižším) + oprava `docs/bottom-sheet.md` a komentáře v tm-sheet.js (8.6).
- [ ] Dokumentace a komentáře: `docs/focus-scope-migration.md` (8.6), CHANGELOG TmButton věta (8.5), XML summary `OverlayRound6E2ETests` (8.2), zastaralé komentáře na všech místech ze sekce 8.7 (včetně všech 4 výskytů „suppressed RZ10012“).
- [ ] Ověření: `npm run test:overlay`; `dotnet test -f net8.0` i `-f net10.0` pro filtr `Dialog|ScrollRegion`; overlay E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 10: Dialogy rich text editoru na TmModal/TmDialog
<!-- depends-on: Fáze 6 -->

Cíl fáze: pět dialogů RTE (odkaz, obrázek, tabulka, video, hledání) je v top layer, mají roli dialog, název, Escape a focus trap.

Zdroj: 0e390ce4 (RTE část), c51ddf81 #10. Analýza: `planning/foundation-followup/cluster-B-packages.md` (sekce RTE) a cluster-B.md sekce **11**.

### Pevná rozhodnutí
- Každý dialog RTE přejde na `TmModal` (nebo `TmDialog` tam, kde jde o potvrzení) — žádný vlastní z-index, žádná vlastní promoce. Obsah a chování formulářů zachovej.
- Ostatní balíčkové dialogy (Notion, Spreadsheet, DocumentEditor, Dashboard) se zde NEdělají — patří jejich plánům.
- CHANGELOG „Changed“ s popisem DOM změny (před/po) pro RTE dialogy; aktualizuj pravidlo 9 v `docs/overlays.md` (RTE už není výjimka; Signing nemá dialogy).

- [ ] RED bUnit pro každý z 5 dialogů: renderuje TmModal/TmDialog s názvem (aria-labelledby), Escape zavře, fokus se po zavření vrátí do editoru.
- [ ] Migruj dialog odkazu a obrázku.
- [ ] Migruj dialog tabulky a videa.
- [ ] Migruj dialog hledání (fokus musí po zavření zůstat ve výběru editoru).
- [ ] Odstraň nepoužívané CSS (z-index 1040) a přegeneruj bundle.
- [ ] E2E: každý dialog otevřený z toolbaru RTE je v top layer (nad otevřeným drawerem), Escape zavře, fokus zpět v editoru; screenshot 1440 a 390.
- [ ] `docs/overlays.md` pravidlo 9 + CHANGELOG.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~RichEditor"`; strict audit; E2E RTE.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 11: ContextMenu, SplitButton, NotificationBell — volitelná mobilní prezentace

Cíl fáze: hostitel si může u TmContextMenu, TmSplitButton a TmNotificationBell zapnout spodní sheet na mobilu; výchozí chování zůstává popover.

Zdroj: e5f236e8, c51ddf81 #7. Analýza: cluster-B.md sekce **10** (varianta B).

### Pevná rozhodnutí
- Nové parametry na všech třech: `MobilePresentation` (`PanelPresentation`, výchozí `PanelPresentation.Popover`) a `SheetTitle` (`string?`; když null, použije se lokalizovaný výchozí titulek — nové klíče `TmContextMenu_SheetTitle`, `TmSplitButton_SheetTitle`, `TmNotificationBell_SheetTitle` v en/cs/fr dle sekce 10).
- Předává se do TmOverlayPanel; žádná vlastní logika sheetu.
- Aktualizuj decision F3-PANEL-PRESENTATION bod (a): „ContextMenu/SplitButton/NotificationBell výchozí Popover, opt-in MobilePresentation“.
- JSON overlay pro každou komponentu ve stejném commitu; wireframe stencil beze změny (výchozí vzhled stejný).

- [ ] RED bUnit pro každou komponentu: výchozí = popover (beze změny markupu); `MobilePresentation=Auto` na mobilu = sheet s titulkem.
- [ ] Implementuj parametry v TmContextMenu (+ JSON overlay).
- [ ] Implementuj v TmSplitButton (+ JSON overlay).
- [ ] Implementuj v TmNotificationBell (+ JSON overlay).
- [ ] Lokalizační klíče en/cs/fr + MockTmLocalizer; demo ukázka na příslušné demo stránce.
- [ ] E2E na 390 (HasTouch): s `MobilePresentation=Auto` se otevře sheet s titulkem, Escape zavře, fokus zpět na trigger; výchozí varianta zůstává popover.
- [ ] Regenerace JSON bundlů, CHANGELOG „Added“, update decision F3-PANEL-PRESENTATION.
- [ ] Ověření: cílené bUnit, JSON validate, E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 12: Toolbar — stisknutý stav a zaškrtávací položky v menu Více

Cíl fáze: `TmToolbarButton` umí stisknutý stav a v menu Více se zobrazí jako zaškrtávací položka.

Zdroj: b6c72292. Analýza: `planning/foundation-followup/cluster-C.md` sekce **C2** (kompletní API a testy).

### Pevná rozhodnutí
- `TmToolbarButton.Pressed` (`bool?`, null = není přepínač) a `PressedMixed` (`bool`) → `aria-pressed="true|false|mixed"`.
- `TmActionItem.Checked` (`bool?`) a `CheckedMixed` (`bool`) → v `ActionOverflowMenu` `role="menuitemcheckbox"` + `aria-checked`; aktivace zaškrtávací položky menu ZAVŘE (jako ostatní položky).
- Opravit selektor počátečního fokusu sheetu v `TmOverlayPanel.razor` (cca ř. 340), který zná jen `menuitem`.
- Dlaždice mobilní akční lišty dostane `aria-pressed`.
- Radio režimy (menuitemradio) se NEdělají.
- Aktualizuj decision F5-ACTION-BAR-API: „Checked/CheckedMixed dodáno“.
- Pozor na pád ElementReferenceCapture (F4 H1): sekvence v BuildMenu musí zůstat neměnné, capture pro všechny položky.

- [ ] RED bUnit dle C2 (4) (Pressed→aria-pressed, Checked→menuitemcheckbox, sheet initial focus na menuitemcheckbox, tile aria-pressed).
- [ ] Implementuj `Pressed`/`PressedMixed` na TmToolbarButton (+ JSON overlay ve stejném commitu).
- [ ] Implementuj `Checked`/`CheckedMixed` na TmActionItem a vykreslení v ActionOverflowMenu.
- [ ] Mapování Pressed→Checked v ToolbarOverflowLayout (sbalené tlačítko v menu zachová stav).
- [ ] Oprav initial-focus selektor sheetu a aria-pressed dlaždice.
- [ ] E2E: sbalené stisknuté tlačítko je v menu zaškrtnuté, Space/Enter ho přepne a zavře menu; 1440 i 390.
- [ ] Regenerace bundlů, CHANGELOG „Added“, wireframe stencil (Pressed vykreslit jako zvýrazněné tlačítko — uprav schema + tempo.stencil.json + parity test), decision update.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~Toolbar|FullyQualifiedName~ActionOverflow|FullyQualifiedName~MobileActionBar"`; `npm run test:overlay`; E2E ToolbarOverflow + MobileActionBar.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 13: Toolbar — skupiny (TmToolbarGroup) a nadpisy skupin v menu Více
<!-- depends-on: Fáze 12 -->

Cíl fáze: tlačítka lze seskupit do `TmToolbarGroup`; v menu Více se skupiny zobrazí s nadpisem a oddělovači, pořadí se nikdy nemění.

Zdroj: 4c483f49. Analýza: cluster-C.md sekce **C3**.

### Pevná rozhodnutí
- Nová komponenta `TmToolbarGroup` (`Label` string?, `ChildContent`), renderuje wrapper s `data-tm-toolbar-group` a `role="group"` + `aria-label`.
- `TmActionItem.Group` (string?) a `GroupLabel` (string?); `ActionOverflowMenu` vykreslí běhy stejné skupiny jako vnořený `role="group"` s nadpisem (`aria-labelledby`) a `role="separator"` mezi běhy (sekce C3 (3)).
- Skupiny nikdy nemění pořadí (pravidlo F5: render order = Items order).
- Nová komponenta = wireframe stencil + role (BuiltInComponentSchemas.cs, tempo.stencil.json) + JSON overlay `JsonDocumentation/Components/TmToolbarGroup.json` + demo na `/toolbar-forms`.

- [ ] RED bUnit dle C3 (4) + RED node (tm-toolbar.js měří skupinu jako dnes).
- [ ] Implementuj `TmToolbarGroup` (+ JSON overlay, stencil, role).
- [ ] Implementuj `Group`/`GroupLabel` na TmActionItem a skupinové vykreslení v ActionOverflowMenu (tm-menu-nav.js přeskočí nadpisy i oddělovače).
- [ ] Toolbar předá skupinu sbalených tlačítek do položek menu.
- [ ] Demo + E2E: sbalená skupina má v menu nadpis, šipky nadpis přeskakují, axe bez chyb v popoveru i sheetu.
- [ ] Regenerace bundlů, CHANGELOG „Added“, decision F4-TOOLBAR-OVERFLOW + F5-ACTION-BAR-API update.
- [ ] Ověření: cílené bUnit, `npm run test:overlay`, E2E ToolbarOverflow.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 14: Toolbar — ne-tlačítkové položky (TmToolbarItem), PreserveFocus a dropdowny v toolbaru
<!-- depends-on: Fáze 13 -->

Cíl fáze: do toolbaru lze dát sbalitelné ne-tlačítkové prvky (select, color picker…), tlačítka mohou nebrat fokus editoru a dropdown v toolbaru je součástí šipkové navigace.

Zdroj: bdab3cb6, f4c27b20 #3 #5, mezera PreserveFocus. Analýza: cluster-C.md sekce **C1, C4 (2) „Newly found core gaps“, C5, C6**.

### Pevná rozhodnutí
- Nová komponenta `TmToolbarItem` (přesné API v C1 (3)): `Priority`, `MenuLabel`, `MenuIcon`, `OnMenuInvoke`, `MenuKeepOpen` nebo `MenuItems` (skupina položek), `Disabled`; interně `IToolbarOverflowItem`, na který TmToolbar převede registraci. V menu Více = jedna položka nebo skupina položek — NIKDY volná šablona (select v role=menu je neplatné ARIA).
- Select/input uvnitř TmToolbarItem zůstává vlastní Tab zastávkou (není v roving množině).
- `TmToolbarButton.PreserveFocus` (`bool`, výchozí false): preventDefault na pointerdown/mousedown, aby editor neztratil výběr (C4 (2)).
- TmDropdown a TmSplitButton uvnitř TmToolbar automaticky dostanou `data-tm-toolbar-control` přes kaskádu (C6) — CHANGELOG „Changed“ (o jednu Tab zastávku méně).
- tm-toolbar.js měří i margin wrapperu skupiny a jeho gap, pokud je větší než gap toolbaru — konzervativně, nikdy nepřecení (C5).
- Nová komponenta = stencil + role + JSON overlay + demo.

- [ ] RED bUnit dle C1 (4), C6 (4), `TmToolbarButtonTests.PreserveFocus_*`; RED node dle C5 (4).
- [ ] Implementuj `IToolbarOverflowItem` a převeď na něj registraci v TmToolbar (TmToolbarButton beze změny chování).
- [ ] Implementuj `TmToolbarItem` (+ JSON overlay, stencil, role).
- [ ] Implementuj `PreserveFocus` (+ JSON overlay).
- [ ] Implementuj auto `data-tm-toolbar-control` pro TmDropdown/TmSplitButton v toolbaru.
- [ ] Implementuj měření margin/gap wrapperu v tm-toolbar.js.
- [ ] Demo (`/toolbar-forms`: select + color picker v toolbaru) + E2E: sbalení položky, výběr z menu, PreserveFocus zachová výběr v textovém poli, šipky přes dropdown.
- [ ] Regenerace bundlů, CHANGELOG, `docs/toolbar.md` (sekce core prerequisites označit jako hotové), decision F4-TOOLBAR-OVERFLOW update.
- [ ] Ověření: cílené bUnit, `npm run test:overlay`, E2E ToolbarOverflow, celá unit sada.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 15: Editor shell a side panel — snap pointy, tablet fit, side panel bez scope

Cíl fáze: mobilní sheet editor shellu má nastavitelné snap pointy a minimální výšku, na tabletu má plátno vždy aspoň MinCanvasWidth a TmSidePanel bez viewport scope bere pozici sheetu ze sondy draweru.

Zdroj: 1645e0aa, ed5296e9, 96b27129. Analýza: cluster-C.md sekce **C7, C8 (varianta A+B), C9 (varianta A)**.

### Pevná rozhodnutí
- `TmEditorShell.MobileSheetSnapPoints` (`IReadOnlyList<double>?`, výchozí null = dnešní `[0.5, 1]`) + minimální výška těla sheetu 14rem (token) — C8.
- Tablet: rozbalený panel se zúží tak, aby plátnu zůstalo `MinCanvasWidth`; tato šířka se NEukládá a NEhlásí přes `LeftWidthChanged`/`RightWidthChanged` — C9 (A). CHANGELOG „Changed“.
- TmSidePanel bez viewport scope: pozici sheetu bere z vlastní sondy draweru (`ResolvedLayoutChanged` + `InitialMode`), žádná druhá sonda — C7. Aktualizuj decision F6-SIDE-PANEL-PRESENTATION.
- Peek snap (jen hlavička) se NEdělá.

- [ ] RED bUnit dle C7 (4), C8 (4), C9 (4).
- [ ] Implementuj `MobileSheetSnapPoints` + min. výšku (+ JSON overlay).
- [ ] Implementuj tablet fit šířku (nepersistuje, nehlásí).
- [ ] Implementuj TmSidePanel fallback přes sondu draweru.
- [ ] E2E `EditorShellE2ETests`: 768 tablet — plátno ≥ 400 px s rozbaleným inspektorem; 390×520 — tělo sheetu ≥ 14rem; vlastní snap pointy; side panel bez scope na 1440 v 500px sloupci = boční sheet.
- [ ] Regenerace bundlů, CHANGELOG, `docs/editor-shell.md`, decisions F6-SIDE-PANEL-PRESENTATION a F6-SHELL-PANEL-STATES update.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~TmEditorShell|FullyQualifiedName~TmSidePanel|FullyQualifiedName~ViewportProbe"`; E2E EditorShell.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 16: Tokeny — focus ring, tokeny calendar-view, tmavé pozadí rozsahu kalendáře

Cíl fáze: žádné jádrové pravidlo nepoužívá slabý `--tm-shadow-focus`, `_calendar-view.css` nemá hex hodnoty ani vlastní dark bloky a dny v rozsahu date-range pickeru mají v tmavém tématu kontrast.

Zdroj: 8b3c2021 (jádro), 421a41ce. Analýza: cluster-D.md sekce **1a, 1b, 2**.

### Pevná rozhodnutí
- 25 jádrových pravidel (seznam souborů a řádků v 1a): 1:1 výměna `--tm-shadow-focus` → `--tm-focus-ring`; kde pravidlo má `outline: none`, nahraď `outline: 2px solid transparent` (forced-colors) — platí i pro pravidla migrovaná už v F0. Heatmap buňka (12px) dostane 2px outline místo ringu. Stavy `-danger` (invalid) a výběru zůstávají.
- Guard test `FocusRingAdoptionTests`: jádro = 0 výskytů `--tm-shadow-focus`, balíčky = seznam, který smí jen ubývat.
- `_calendar-view.css`: 10 hex fallbacků, 2 dark bloky a white-on-primary → 6 nových tokenů `--tm-calendar-view-*` v tokens.css + tokens-dark.css (hodnoty v 1b), přidat do intentionalDifferences.
- Tmavé `--tm-cal-range-bg` = `var(--tm-color-primary-900)` v tokens-dark.css (default i indigo) → 9.45:1 / 10.43:1; test `CalendarInRangeDayText_HoldsAa` (kód v sekci 2).
- Balíčková pravidla (DocumentEditor, EmailTemplates, Notion, Signing, Spreadsheet) se zde NEmění.
- CHANGELOG Breaking/Migration (vizuální: silnější focus ring, tmavé pozadí rozsahu).

- [ ] RED: `FocusRingAdoptionTests` (jádro 0) a `CalendarInRangeDayText_HoldsAa` + TokenContrast záznamy pro nové calendar-view tokeny.
- [ ] Migruj focus ring v pravidlech 1–13 ze seznamu 1a.
- [ ] Migruj focus ring v pravidlech 14–25 + heatmap výjimka + forced-colors outline u pravidel migrovaných v F0.
- [ ] Přidej 6 `--tm-calendar-view-*` tokenů a přepiš `_calendar-view.css`.
- [ ] Nastav tmavé `--tm-cal-range-bg`.
- [ ] Přegeneruj bundle; screenshoty (1440, light/dark/indigo) date-range pickeru a fokusu na 5 vzorových komponentách, read_image.
- [ ] CHANGELOG + `docs/redesign-foundation.md` (řádky follow-upů odstranit).
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~TokenContrast|FullyQualifiedName~FocusRing|FullyQualifiedName~CssTokenAudit"`; strict audit (počet porušení klesne nebo zůstane; uprav baseline jen dolů).
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 17: Lokalizace a čas — plural demo, plurály počtů, veřejný TmRelativeTime.Resolve

Cíl fáze: demo stránka ukazuje plurály s krokovačem, tři jádrové počty jsou plurálové věty, balíčky mohou použít `TmRelativeTime.Resolve` a staré klíče TmTimeline mají guard.

Zdroj: c88f6e52, 4c92ba05 #4, 449c99df (jádro), 37682aee (guard). Analýza: cluster-D.md sekce **8, 9a, 9b (jen jádrová část), 10**.

### Pevná rozhodnutí
- Nová demo stránka `/localization` (krokovač + předvolby 0/1/2/5/21/22/25, data-testid dle sekce 8); do přepínače jazyků dema přidat FR.
- Převést na `Loc.Plural`: `TmChat_ReplyCount`, `TmAuditLogViewer_EventsCount`, `TmDataImport_MoreErrors` (rodiny en/cs/fr v sekci 9a); CHANGELOG Breaking/Migration (klíče).
- `TmRelativeTime.Resolve` se stane veřejným (Abstractions/core dle 9b) s XML doc a JSON overlay.
- Klíče `TmTimeline_*Ago`/`DateFormat` se NEmažou (2.9.1 nevydaná); přidej guard test, že je knihovna nečte, a plan_report_remaining „odstranit v plánu dalšího vydání“.

- [ ] RED bUnit pro tři převedené počty (cs 1/2/5/22 — pozor: 22 = Other v češtině); RED test, že `TmRelativeTime.Resolve` je public; guard test TmTimeline klíčů.
- [ ] Převeď tři počty na Loc.Plural (resource rodiny en/cs/fr + MockTmLocalizer).
- [ ] Zveřejni `TmRelativeTime.Resolve` (+ JSON overlay, docs/localization-plurals.md).
- [ ] Vytvoř demo stránku `/localization` + odkaz v navigaci + FR v přepínači jazyků.
- [ ] E2E `LocalizationPluralsE2ETests`: tabulka hodnot pro en/cs/fr a 0/1/2/5/21/22/25 (očekávané texty ze sekce 8), screenshot 1440 a 390.
- [ ] CHANGELOG, regenerace bundlů, plan_report_remaining (odstranění klíčů v další verzi).
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~Plural|FullyQualifiedName~RelativeTime|FullyQualifiedName~Localization"`; JSON validate; E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 18: ITmUserTimeZone — sdílené časové pásmo uživatele (opt-in)
<!-- depends-on: Fáze 17 -->

Cíl fáze: knihovna umí jednou zjistit časové pásmo prohlížeče a použít ho pro relativní čas, aniž by měnila výchozí chování nebo nahrazovala app-wide TimeProvider.

Zdroj: 8d6b99e3, 4c92ba05 #3. Analýza: cluster-D.md sekce **11 (varianta B), 12**.

### Pevná rozhodnutí
- `ITmUserTimeZone` v `src/Tempo.Blazor.Abstractions/Localization/ITmUserTimeZone.cs` přesně dle sekce 11 (3): `Zone`, `IsResolved`, `event Changed`, `SetZone`.
- Interní `TmUserTimeZone` (scoped): na WASM `Zone = TimeZoneInfo.Local`, `IsResolved = true`; na Serveru JS `Intl.DateTimeFormat().resolvedOptions().timeZone` jednou za circuit po prvním interaktivním renderu (nový modul `src/Tempo.Blazor/wwwroot/js/tm-user-zone.js`, export `resolve()`), neznámé id → UTC; při prerenderu UTC/nevyřešeno.
- Interní `TmUserTimeProvider : TimeProvider` čte `LocalTimeZone` líně; NIKDY se neregistruje jako `TimeProvider`.
- `AddTempoBlazor()` registruje `ITmUserTimeZone` (TryAddScoped); formatter ho použije až po `services.AddTempoUserTimeZone()` (nahradí registraci formatteru). Výchozí chování se NEmění; přepnutí výchozího = 2.10.0 (plan_report_remaining).
- Živý refresh (ticker) se NEdělá — zdokumentovat „host překreslí“.
- Nové API: XML doc + JSON overlay (Abstractions) + CHANGELOG „Added“; aktualizuj decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME bod (3).

- [ ] RED `UserTimeZoneTests` (bUnit, FakeTimeProvider) dle sekce 11 (4), včetně „AddTempoBlazor neregistruje TimeProvider“.
- [ ] Implementuj interface (Abstractions) a `TmUserTimeZone` + `tm-user-zone.js` (+ node test).
- [ ] Implementuj `TmUserTimeProvider` a `AddTempoUserTimeZone()`.
- [ ] Relativní časové komponenty se přihlásí k `Changed` a překreslí (InvokeAsync), odhlásí v Dispose.
- [ ] Přepiš dokumentaci (`docs/localization-plurals.md`) — ruční snippet z F7 zůstává platný, nový jednořádkový způsob přes `AddTempoUserTimeZone()`; uprav dokumentační testy snippetu.
- [ ] E2E Server (7107): Playwright `TimezoneId = "Pacific/Auckland"` → relativní čas „včera v …“ v pásmu prohlížeče.
- [ ] CHANGELOG, JSON overlay/bundly, decision update, plan_report_remaining (výchozí zapnutí ve 2.10.0).
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~UserTimeZone|FullyQualifiedName~RelativeTime"`; `npm run test:overlay`; E2E Server.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 19: Pointer-drag — Alt+Shift v klávesové alternativě a tm-sheet.js na sdíleném helperu

Cíl fáze: `attachKeyboardMove` podporuje Alt+Shift+šipky (potřebuje Scheduler resize) a gesto bottom sheetu běží na `pointer-drag.js` bez regrese kontraktu F2.

Zdroj: 59e85790 (E-K), 10bcfc90 (tm-sheet). Analýza: `planning/foundation-followup/cluster-E.md` sekce **1.5, 2.6**.

### Pevná rozhodnutí
- `attachKeyboardMove(el, { onStep, shift: true })` — s volbou `shift` reaguje i na Alt+Shift+šipku a předá `shift: true` do `onStep` (sekce 1.5); výchozí chování beze změny.
- tm-sheet.js: plná migrace na `attach()` z pointer-drag.js (sekce 2.6); zachovat rychlostní flick, snapy, SwipeToDismiss a všechny testy F2-SHEET-PRIMITIVE; druhý prst už nepřevezme tažení.
- Aktualizuj decision F2-SHEET-PRIMITIVE (gesto běží na pointer-drag.js) a F7-POINTER-DRAG-PLURALS-RELATIVE-TIME (volba shift).

- [ ] RED node: `pointer-drag.test.mjs` — Alt+Shift+ArrowRight s `shift:true` volá onStep s shift; bez volby se ignoruje.
- [ ] Implementuj volbu `shift` (+ `docs/touch-interaction.md`).
- [ ] RED node `tm-sheet.test.mjs`: druhý prst během tažení nepřevezme tažení; všechny dosavadní testy sheetu zůstávají.
- [ ] Migruj gesto tm-sheet.js na `attach()` (velocity z `info.event.timeStamp`, threshold 6).
- [ ] E2E: `OverlayMobilePresentationE2ETests`, `EditorShellE2ETests` (sheet swipe, snapy, zavření) na 390 HasTouch.
- [ ] Decisions update + CHANGELOG „Changed“ (interní, bez API změny).
- [ ] Ověření: `npm run test:overlay`; E2E sheet lanes.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 20: TmSplitterPane a TmRangeSlider bez eval
<!-- depends-on: Fáze 19 -->

Cíl fáze: splitter a range slider nepoužívají `eval`, fungují na dotyku a mají klávesovou alternativu.

Zdroj: 10bcfc90 (splitter, slider). Analýza: cluster-E.md sekce **2.1, 2.2**.

### Pevná rozhodnutí
- TmSplitterPane: nový modul `src/Tempo.Blazor/wwwroot/js/tm-splitter.js` nad `attach()`; živá velikost přes inline styl v JS (žádný re-render na pohyb), commit jednou na konci; oddělovač `role="separator"`, `aria-orientation`, `aria-valuenow/min/max`, lokalizovaný `aria-label`, šipky ±16 px, Home/End; žádný nový `SizeChanged` parametr.
- TmRangeSlider: dva nativní `input type=range` stylované CSS (dual-thumb), žádný JS; `slider.js` odstranit (nikdo ho nenačítá); každý thumb má lokalizovaný accessible name (klíče en/cs/fr).
- CHANGELOG Breaking/Migration pro DOM změny obou komponent (před/po) + CSP poznámka pro tm-splitter.js.

- [ ] RED bUnit/node dle 2.1 (4) a 2.2 (4).
- [ ] Implementuj `tm-splitter.js` a přepiš TmSplitterPane (odstraň eval).
- [ ] Klávesová alternativa splitteru + lokalizace.
- [ ] Přepiš TmRangeSlider na CSS dual-thumb, odstraň `slider.js` (zkontroluj ProvenanceComparisonTests počty).
- [ ] E2E: splitter myš + dotyk (CDP) na 768/390 + klávesnice; range slider dotyk na 390 + klávesnice; screenshoty.
- [ ] CHANGELOG, JSON overlay (pokud se mění parametry), stencil kontrola.
- [ ] Ověření: cílené bUnit, `npm run test:overlay`, strict audit, E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 21: Sloupce TmDataTable a TmColorGradient na pointer-drag
<!-- depends-on: Fáze 19 -->

Cíl fáze: změna šířky sloupců tabulky a výběr v barevném gradientu běží na sdíleném helperu, zrušení tažení nic neukládá a obojí má klávesovou alternativu.

Zdroj: 10bcfc90 (DataTable, ColorGradient). Analýza: cluster-E.md sekce **2.3, 2.7**.

### Pevná rozhodnutí
- DataTable: delegovaný `attach()` v `data-table.js` (selector na úchyty), `pointercancel`/Escape šířku VRÁTÍ (nyní ji uloží — bug), úchyt `role="separator"` s šipkami/Home/End; zachovat dnešní C# callbacky/hooky.
- TmColorGradient: `attach()` s pointer capture, přepočet podle skutečné velikosti prvku (ne pevných 200×150), náhled přes rAF volání .NET; slidery fokusovatelné se šipkami; lokalizované názvy.
- CHANGELOG „Fixed“ (cancel ukládal šířku) + CSP poznámka.

- [ ] RED dle 2.3 (4) — mimo jiné pointercancel nevolá commit šířky.
- [ ] Migruj resize sloupců + klávesová alternativa.
- [ ] RED dle 2.7 (4).
- [ ] Migruj TmColorGradient + fokusovatelné slidery.
- [ ] E2E: DataTable resize myší, dotykem (768) a klávesnicí, Escape vrací; gradient dotykem na 390 a klávesnicí.
- [ ] CHANGELOG, lokalizace, stencil kontrola.
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~TmDataTable|FullyQualifiedName~ColorGradient"`; `npm run test:overlay`; E2E.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 22: pointer-gesture.js — pinch/zoom a auto-scroll
<!-- depends-on: Fáze 19 -->

Cíl fáze: jádro má jediný modul pro dvouprstý pinch/pan (včetně ctrl+kolečka) a pro automatický scroll u okraje při tažení.

Zdroj: 6de6d5b9. Analýza: cluster-E.md sekce **4.3** (přesné API, kontrakt, testy).

### Pevná rozhodnutí
- Nový modul `src/Tempo.Blazor/wwwroot/js/pointer-gesture.js` s exporty `attachPinch(element, options)` a `createAutoScroller(container, options)` přesně dle sekce 4.3 (signatury, výchozí hodnoty, cancel cesty, ctrl+wheel `scale = Math.exp(-deltaY/100)`, debounce onEnd 150 ms, preventDefault jen s ctrl).
- `pointer-drag.js` zůstává single-pointer; spolupráce: host v `onStart` pinche zavolá `drag.cancel('pinch')`.
- `createAutoScroller` je port `spreadsheet-canvas.js` (ř. 3026–3038), `scrolledX/Y` kumulativní.
- `docs/touch-interaction.md`: nová sekce „Multi-pointer gestures“ (vždy single-pointer alternativa: tlačítka zoomu).

- [ ] RED node `src/Tempo.Blazor/wwwroot/js/__tests__/pointer-gesture.test.mjs` — všech 9 případů ze sekce 4.3.
- [ ] Implementuj `attachPinch`.
- [ ] Implementuj `createAutoScroller`.
- [ ] Dokumentace + uprav ProvenanceComparisonTests počet (nový Pack=false test soubor).
- [ ] Decision F7-POINTER-DRAG-PLURALS-RELATIVE-TIME: doplň pointer-gesture.js.
- [ ] Ověření: `npm run test:overlay` (nový test běží), celá unit sada.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 23: Workflow designer — pointer-drag, pinch, auto-scroll, klávesnice, lokalizace
<!-- depends-on: Fáze 22 -->

Cíl fáze: TmWorkflowDesignerCanvas funguje na dotyku (tažení uzlů, pan, pinch zoom, auto-scroll), stavy jsou fokusovatelné s klávesovým posunem a kontextové menu je lokalizované.

Zdroj: 10bcfc90 (workflow-designer), 32e4e6c4 (jádrová část). Analýza: cluster-E.md sekce **2.5**.

### Pevná rozhodnutí
- `workflow-designer.js`: tažení uzlů a tvorba přechodů přes `attach()` (dotyk okamžitě, bez long-pressu), pan přes `attach()` na pozadí, zoom přes `attachPinch`, auto-scroll přes `createAutoScroller`.
- Uzly fokusovatelné (`tabindex`, roving), Alt+šipky posun přes `attachKeyboardMove`, Delete smaže; kořen SVG `role="group"` místo `role="img"` (CHANGELOG Breaking/Migration před/po).
- Texty kontextového menu do TmResources en/cs/fr (dnes natvrdo anglicky).
- CSP poznámka do CHANGELOG (nové importy pointer-drag.js a pointer-gesture.js).

- [ ] RED bUnit/node dle 2.5 (4) (fokusovatelné uzly, lokalizované menu, klávesový posun volá C# callback).
- [ ] Migruj tažení uzlů a přechodů na `attach()`.
- [ ] Pan + pinch zoom + auto-scroll.
- [ ] Fokus a klávesová alternativa uzlů.
- [ ] Lokalizace kontextového menu (en/cs/fr + MockTmLocalizer).
- [ ] E2E: myš 1440, dotyk (CDP, dva touchPoints pro pinch) 768 a 390, klávesnice; screenshoty.
- [ ] CHANGELOG, JSON overlay (pokud se mění parametry), stencil kontrola, `docs/touch-interaction.md` (workflow designer v tabulce adoptérů).
- [ ] Ověření: `dotnet test --filter "FullyQualifiedName~Workflow"`; `npm run test:overlay`; E2E workflow.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 24: Kosmetika — dlaždice akční lišty, viewport-fit, icon chip, kompaktní Vymazat

Cíl fáze: drobné vizuální úpravy podle review F5 a F8.

Zdroj: 835acf83 #4 #6, f1ebebef, cb3d590f. Analýza: cluster-C.md sekce **C11, C12, C13**; cluster-D.md sekce **6**.

### Pevná rozhodnutí
- Jednořádkové dlaždice akční lišty: výška dvouřádkové dlaždice jen u `Placement=FixedViewport` (C11) — CHANGELOG „Changed“.
- `viewport-fit=cover` v meta viewport všech tří demo hostů (C12).
- Icon chip na dlaždicích jen přes tokeny, výchozí vypnuto (C13 — přesný název tokenu/parametru dle sekce).
- Podpis: pod 640 px šířky kontejneru (`@container`) je Vymazat v overlay režimu kompaktní tlačítko jen s ikonou (accessible name zachován) — cluster-D sekce 6.

- [ ] RED testy dle C11 (4), C13 (4) a cluster-D 6 (4).
- [ ] Implementuj výšku dlaždic.
- [ ] Implementuj icon chip (tokeny, výchozí vypnuto) + JSON overlay, pokud vzniká parametr.
- [ ] Kompaktní Vymazat u podpisu (`@container (width < 640px)`).
- [ ] `viewport-fit=cover` v demo hostech.
- [ ] E2E/screenshoty: akční lišta 390 (sticky vs fixed), podpis 320 a 390.
- [ ] Ověření: cílené bUnit, strict audit, E2E MobileActionBar + Signature.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 25: Sdílený helper pro lazy JS moduly

Cíl fáze: interop wrappery (FocusTrap, SheetInterop, ScrollRegionInterop a další) nemají duplikovaný import/guard boilerplate.

Zdroj: 26e1afd5 #8. Analýza: cluster-B.md sekce **8.8** (návrh API `LazyJsModule`).

### Pevná rozhodnutí
- Interní `src/Tempo.Blazor/Helpers/LazyJsModule.cs` dle 8.8: líný import, guard dvojitého importu, `InvokeVoidAsync`/`InvokeAsync`, awaitovaný dispose, chytání JSDisconnectedException/TaskCanceledException/ObjectDisposedException, nic při prerenderu.
- Migrace po jednom wrapperu, každý v samostatném commitu, beze změny chování (stávající testy musí zůstat zelené). Seznam wrapperů: grepni `IJSObjectReference` v `src/Tempo.Blazor/Helpers` a `Components` (cca 10).

- [ ] RED bUnit `LazyJsModuleTests` (import jednou, dispose bezpečný, výjimky spolknuté, prerender bez volání).
- [ ] Implementuj `LazyJsModule`.
- [ ] Migruj FocusTrap interop, SheetInterop, ScrollRegionInterop.
- [ ] Migruj TopLayerInterop, ToolbarInterop, MenuNavInterop.
- [ ] Migruj zbývající wrappery (EditorShellInterop, layout observer interop a další z grepu).
- [ ] Ověření: celá unit sada; `npm run test:overlay`; overlay + toolbar + editor shell E2E lanes.
- [ ] Code review této fáze a oprava nalezeného.

## Fáze 26: Dokumentace, finální review a uzavření

Cíl fáze: dokumentace odpovídá kódu, všechny brány a E2E lanes jsou zelené a otevřené úkoly plánu faf503eb pokryté tímto plánem jsou v MCP vyřešené.

Zdroj: všechny výše. Seznam ID: `planning/foundation-followup/inventory.md`.

### Pevná rozhodnutí
- Aktualizuj `docs/redesign-foundation.md` (tabulka follow-upů: odstranit hotové, ponechat jen ty, které patří komponentovým plánům) a specializované dokumenty změněné fázemi 1–25 (overlays.md, toolbar.md, editor-shell.md, touch-interaction.md, localization-plurals.md, responsive-conventions.md, bottom-sheet.md).
- CHANGELOG: konsolidace — žádné duplicitní nadpisy v nevydané verzi.
- Ruční ověření tvrzení v dokumentaci proti kódu (grep), min. 30 tvrzení.

- [ ] Aktualizuj `docs/redesign-foundation.md` a ostatní dokumenty; ověř každé změněné tvrzení grepem.
- [ ] Konsolidace CHANGELOG.
- [ ] Plný běh bran: build, celá unit sada (0 selhání), `npm run test:overlay`, strict audit, JSON validate, E2E lanes Smoke + overlay + toolbar + editor shell + mobile action bar + gestures (vlastní hosty).
- [ ] V MCP vyřeš (plan_resolve_remaining_task, planId faf503eb-bf08-4f5f-8c33-08df2362aa32) všechny remaining tasks, které tento plán pokryl (seznam z inventory.md; u každého resolutionNote s commitem); nepokryté nech a uveď vlastníka.
- [ ] Oznam dokončení závislým plánům (DocumentEditor, DiagramEditor, Scheduler, Notion, PdfViewer, Wireframe, Gantt, e-mail) přes plan_learning_add / poznámku: které jádrové prerekvizity jsou hotové a jak je použít.
- [ ] Ověření: všechny brány zelené, výsledky s cestami k logům.
- [ ] Code review této fáze a oprava nalezeného.
