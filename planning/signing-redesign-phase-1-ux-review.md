# Signing redesign — Fáze 1: UX review výchozího stavu (baseline)

Plan: Signing redesign (Vlna 0 / S1-0 + S2-0). Tento dokument je výstup subtasku
„UX review: porovnat baseline s předchozím stavem … a zapsat výchozí mezery proti
`pdf-template-designer--desktop.png` a `signing-form-runner--desktop.png`".

## Metoda

- **Baseline screenshoty** (před redesignem): `tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-1-baseline/`
  generuje `SigningRedesignBaselineE2ETests` (opt-in přes `TM_WRITE_BASELINES=1`) — 23 obrazovek
  × 4 viewporty (1440 / 1024 / 390 / 320) z dema `/signing-components` a `/signing-from-editor`.
  Referenční mockupy: `output/imagegen/tempo-reimagined/Tempo.Blazor.Signing/*.png`.
- **Baseline je PRAVÝ pre-change stav** (revize po reviewu fáze 1): generátor běžel v `git worktree`
  na `HEAD` (bez necommitovaných změn) a jen s nakopírovaným generátorem; dřívější demo hostitelé
  startované s `--no-build` byli zabiti (`Get-Process dotnet` + command line), takže obě běhy
  (baseline i `phase-1`) self-hostnily čerstvé buildy. Sticky lišta dema se před
  element-screenshoty skryje (`.sticky.top-0, .fixed.top-0 { visibility: hidden }` — capture
  dokumentuje komponentu, ne demo chrome), kontext 1024 má `HasTouch=true`. Post-change capture
  píše do `phase-1/` přes `TM_SIGNING_BASELINE_DIR=phase-1`.
- **Registrace regrese na 1440:** změny Fáze 1 jsou na úrovni tokenů a breakpointů:
  - tokeny `#fff`→`--tm-color-on-primary`, `--tm-color-white`→`--tm-color-on-primary`: v light
    režimu se hodnota nerozliší (`--tm-color-on-primary` = `--tm-text-inverse` = bílá); záměrná
    změna je jen dark-mode kontrast primárních tlačítek (runner „Next/Complete", markery timeline);
  - `@media`→`@container`: měřeno v Playwrightu na 1440×900 (produkt i `/signing-from-editor`
    po proběhnutí bridge): kontejner designeru i runneru je **~1030 px**, computed tracks jsou
    designer `208px 470px 320px` (3) a runner `630px 384px` (2); 3 resp. 2 tracky na 1440 hlídá
    `SigningContainerQueryE2ETests`. Pre-change viewport
    prahy designeru 1100/900 odpovídají v kontejneru ~787/644 px, proto designer používá spodní
    konvencní prahy **768/640** — prahy 1024/768 by sbalily designer už na běžných desktopech
    (kontejner ≈ 72 % viewportu v demo shellu); runner překládá 48rem→768. Při šířce kontejneru
    ≥ prahu se layout nemění — na 1440 zůstává multi-column (E2E potvrzeno);
  - barvy rolí se uplatní jen u polí s `RoleColor` (demo nyní role s barvami seeduje — záměrně);
  - nevalidní pole má nově přerušovaný 2px rámec (nekolorový příznak, WCAG 1.4.1) — záměrně;
  - hlídáno testy `SigningCssTokenHygieneTests` a E2E `SigningContainerQueryE2ETests`
    (narrow-container chování na 1440 viewportu).

## Výsledek porovnání baseline ↔ fáze 1 (92 párů PNG)

53 párů identických, 21 s rozdílem < 2 % pixelů, 18 výrazných. Vše rozděleno na záměrné změny
nebo šum capture — žádná nezamýšlená regrese:

- **1440** (slib „žádná regrese na plné šířce“): `signing-form-runner` 3,1 % (přerušovaný rámec
  nevalidních polí + role-tint překryvů), `pdf-template-designer` 1,3 % (role-tint),
  `submission-status-timeline` 0,7 % (timestampy demo dat jsou relativní ke času běhu — šum) a
  `field-editor-panel` — ten se změnil do výšky ZÁMĚRNĚ: panel v ~448px sloupci se teď přebalí
  podle KONTEJNERU na jednosloupec, kde baseline držel desktop layout podle viewportu. To je
  hlavičkové chování fáze 1 („úzký panel na široké obrazovce se sbalí jako na telefonu“),
  hlídané E2E. Ostatní 1440 capture jsou pixel-identické včetně rozměrů (designer 3 tracky,
  runner 2 tracky).
- **1024**: 7 step-shell karet (5–8 %) a 6 panelů (condition/formula builder, field editor,
  designer, recipient-role-editor, comments panel — změna výšky elementu) se přebalí: kontejnery
  mají na tomto viewportu ~720–760 px, takže prahy 768/640 zasahují; baseline je udržoval
  otevřené podle viewportu 1024. Záměrné kontejnerové chování, šum navíc dělá subpixelový
  posun po `min-inline-size: 0; inline-size: 100 %`.
- **320/390** (skutečný mobil): téměř vše identické — viewport i kontejner query zde dávají
  stejný layout; rozdíly jen role-tint / dashed rámy / timestampy.
- Seznam záměrných odchylek: role barvy (demo seeduje paletu), role-6 `#a16207`, dashed invalid
  rámec, container-driven přebalování (výše), skrytá sticky lišta dema v capturech (metodika).

## Výchozí mezery proti `pdf-template-designer--desktop.png` (referenci plní Fáze 8)

1. **Paleta polí** – dnešní paleta je seznam textových tlačítek v kartě; reference má dlaždice
   ikona + popisek (Podpis, Textové pole, Datum, …) s vlastním rámem a stavem aktivní položky a
   pomocný text „Přidat pole" pod paletou.
2. **Toolbar** – dnešní ovládání je jednazalomená řada tlačítek; reference ji groupuje (stránka
   ‹ 1/2 ›, segment Jedna stránka | Souvislé, jazyk, zoom −/100 %+, Přizpůsobit šířce/stránce,
   „Automaticky rozpoznat pole" jako zvýrazněná akce).
3. **Inspektor** – dnešní `TmSigningFieldEditorPanel` jsou ploché sekce; reference má hlavičku typu
   pole (ikona + název + popis), sekce Název / Přiřadit příjemci (s barevnou tečkou role) / Povinné
   pole / Velikost v mm, akce Kopírovat–Smazat, blok „Práce s výběrem" a nápovědu ke kontextovému
   menu, plus zavírací křížek.
4. **Pole na plátně** – reference vykresluje nad polem malý štítek s názvem („Podpis –
   Poskytovatel") a role-barevný přerušovaný rám; dnešní overlay má popisek uvnitř pole. Fáze 1
   položila základ (`RoleColor` → `--tm-signing-role-color`, `color-mix()`), vizuál štítku chybí.
5. **Scéna** – reference má stránku centrovanou na šedém pozadí workspace; dnešní canvas je
   ohraničený box s paddingem.

## Výchozí mezery proti `signing-form-runner--desktop.png` (referenci plní Fáze 3 a 5)

1. **Panel průvodce / seznam kroků** – dnešní pravý panel ukazuje jen „Krok X z Y", přepínač
   jazyka, režim přístupnosti a editor aktuálního kroku; reference má postupnou progress bar se
   stavem („3 z 3 hotovo" + fajfka) a **seznam všech kroků jako karty** (ikona, název, stránka,
   stav Vyplněno/zbývá).
2. **Akční patička** – dnešní akce jsou řada Previous | Skip | Next | Complete; reference má jednu
   výraznou primární akci („Podepsat nyní") + „Minimalizovat" a zelenou kartu „Všechna povinná pole
   jsou hotová!". (Fáze 3: akční patička a completion panel.)
3. **Chrome dokumentu** – reference dává název dokumentu, stránku 1/3 a zoom do horní lišty a
   paginaci plavou vlevo dole u plátna; dnešní `TmDocumentPageViewer` má toolbar nad dokumentem.
4. **Hotová pole** – reference kreslí skutečnou hodnotu (podpis, datum) v role-tintovaném rámečku;
   dnešní overlay ukazuje popisek + hodnotu textem. Groundwork (role barvy) je z Fáze 1.
5. **Mobil** – reference (mockupy `--mobile`) počítá s bottom sheet a sticky akční lištou; dnešní
   runner má mobilní panel sticky přes `--tm-z-sticky` (Fáze 1 oprava) — kompletní přestavba je
   Fáze 5.

## Dokumentované známé mezer chování (SI2)

Externí krok (Verification/KBA/Payment) i phone OTP a upload přílohy jsou **kosmetické** —
„klik na Spustit ověření/Zaplatit = krok hotový bez ověření“. Zdokumentováno v XML komentářích
(`TmSigningFormRunner`, `TmSigningExternalStep`, `TmSigningPhoneStep`, `TmSigningAttachmentStep`),
v JSON overlayích (`TmSigningFormRunner.json`, `TmSigningExternalStep.json` — sekce „Known
limitations / security note“) a v CHANGELOG (Known issues). Plný kontrakt aktivních kroků je
Fáze 6.
