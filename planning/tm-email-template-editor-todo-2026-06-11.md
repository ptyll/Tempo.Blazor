# TmEmailTemplateEditor — kompletní TDD todo list (2026-06-11)

Cíl: převzít editor email šablon z `/home/pavel/NetProjects/PostrionCore/PostrionCore` a udělat z něj
samostatnou, plně otestovanou komponentu pro Tempo.Blazor ve **dvou nových NuGet balíčcích**
(UI + abstractions s veškerou logikou), s kompletním demo flow (vytvoření šablony v UI →
vyplnění proměnných → reálné odeslání emailu přes demo API → ověření v smtp4dev).

**Cíl parity (rozhodnutí 2026-06-11): editor plně pokrývá ÚPLNĚ VŠECHNY funkce MJML 4** —
všechny body komponenty, kompletní sady atributů každé komponenty, celou head sekci
(mj-title, mj-preview, mj-font, mj-breakpoint, mj-style embedded i inline, mj-attributes
vč. mj-all a pojmenovaných mj-class, mj-html-attributes) — a má **plnohodnotný obousměrný
MJML import/export** (fáze EI). Žádný „podporujeme jen podmnožinu".
⚠️ Tvrdá hranice parity = co umí vyrenderovat Mjml.Net; spike E0.9 ověří jeho support matrix
a každá zjištěná mezera se NAHLÁSÍ uživateli k rozhodnutí (pravidlo 6), nezamlčuje se.

**Role:** Senior Full-stack Developer + UX specialista + UI expert + specialista na AI.

---

## ⚠️ KRITICKÁ PRAVIDLA — ABSOLUTNÍ ZÁKAZ PORUŠOVAT

### 1. TDD — TEST FIRST (Red-Green-Refactor) — STRIKTNĚ!
```
Krok 1: Napiš FAILING test (červený) — MUSÍ selhat před implementací (spusť a ověř!)
Krok 2: Napiš MINIMÁLNÍ kód pro průchod testu (zelený)
Krok 3: Refactoruj (čistý kód bez změny funkcionality)
Krok 4: Odškrtni checkbox v tomto souboru a pokračuj dalším taskem
```

### 2. ŽÁDNÉ HARDCODED TEXTY — VŠE Z RESOURCES!
- ❌ ZAKÁZÁNO: `"Uložit"`, `"Název šablony"`, `"Email je povinný"` v C#/Razor kódu
- ✅ POUŽÍVAT: `@Localizer["EmailEditor_Button_Save"]`
- Texty v `.resx`: UI balíček `src/Tempo.Blazor.EmailTemplates/Resources/TmEmailResources.resx`
  (+ `.cs.resx`, `.fr.resx` — stejný vzor jako `Tempo.Blazor/Resources/TmResources.resx`),
  validační zprávy v `src/Tempo.Blazor.EmailTemplates.Abstractions/Resources/EmailTemplateValidationResources.resx`
- Fluent validace MUSÍ používat lokalizované zprávy z resources

### 3. FLUENT VALIDACE — FE i BE
- **Backend (Abstractions):** validátory s `IStringLocalizer<EmailTemplateValidationResources>`
- **Frontend:** `Tempo.Blazor.FluentValidation` (`FluentValidationValidator` + EditContext extensions)
- **Async validace:** s debouncingem (300 ms) — např. unikátnost názvu šablony přes API

### 4. ŽÁDNÉ ZJEDNODUŠENÉ IMPLEMENTACE
- ❌ ZAKÁZÁNO: placeholdery, mock data v produkčním kódu, TODO/FIXME komentáře, `// implement later`
- ✅ Produkční kód od prvního řádku, reálná data, reálné API volání
- ⚠️ POZOR: zdrojový PostrionCore editor MÁ stuby (viz analýza níže) — NEPŘENÁŠET je,
  každou funkci implementovat doopravdy a test-first

### 5. PRAVIDLO TESTŮ
- ❌ NIKDY neměnit test, který má správnou logiku — test je specifikace
- ✅ VŽDY opravit příčinu v implementačním kódu

### 6. DOTAZY PŘI NEJISTOTĚ
- Když si nejsi jistý specifikací → zeptej se uživatele, nehádej

### Další závazná pravidla
- ⚠️ Jako UI/UX expert chci líbivý vzhled a super UX — průběžně posuzovat ze screenshotů
- ⚠️ Atomické transakce — Unit of Work (demo API store)
- ⚠️ DTO vždy do `Tempo.Blazor.EmailTemplates.Abstractions`
- ⚠️ Žádné wrapper třídy pro API odpovědi — používají se HTTP kódy
- ⚠️ Scoped CSS (`.razor.css`) pro všechny komponenty
- ⚠️ Dodržovat CSS design tokeny Tempo.Blazor (viz `DesignTokensPage`, žádné hardcoded barvy/spacing)
- ⚠️ Používat Tempo.Blazor komponenty (Tm*); když si nejsem jistý API komponenty → **MCP server tempo-blazor docs**
- ⚠️ `dotnet test` celé solution NIKDY paralelně (OOM, exit 137) → vždy
  `dotnet test -- xUnit.parallelizeTestCollections=false`, nebo testovat po projektech

---

## 0. Analýza zdroje (hotová — shrnutí faktů)

### Co v PostrionCore existuje a přebíráme jako referenci
- **`src/PostrionCore.EmailTemplates`** (~59 .cs, net10.0, deps: `Mjml.Net 4.0`, `Scriban 6.5.3`) — logika:
  - **Bloky (14 konkrétních):** Text, Button, Image, Divider, Spacer, Raw, Table, Social, Hero,
    Navbar, Carousel, Accordion, Wrapper, Group (+ `BaseBlock`, `BlockAttribute`)
    → mapují se 1:1 na standardní MJML komponenty (`mj-text` … `mj-group`), Mjml.Net je všechny umí.
  - **Layout:** `Section`, `Column`, `SectionBuilder`, `ColumnBuilder`, `LayoutPresets`, `LayoutValidator`
  - **Core:** `MjmlTemplate` (root dokument: Name/Subject/Preheader/Language/Styles/Sections),
    `TemplateStyles`, `MjmlTemplateBuilder`, `BlockRegistry`+`BlockDescriptor`
  - **Rendering:** `MjmlGenerator` (model→MJML), `TextVersionGenerator` (plain-text verze),
    `MjmlTemplateService` (`RenderTemplateAsync`→`RenderResult{Html,Subject,Preheader,TextVersion,Errors}`)
  - **Templating (Scriban, sandbox):** `ScribanTemplateEngine` (`Render`/`Validate`/`ExtractVariables`),
    `SandboxedTemplateContext`, `TemplateSecurityOptions`, `SafeStringFunctions`/`SafeDateFunctions`/`SafeMathFunctions`,
    `VariableExtractor`+`VariableVisitor`, `ObjectToScriptObjectConverter`, `Result<T>`, `ValidationResult`
  - **Services:** `TemplateValidationService`, `EditorHistoryService` (undo/redo),
    `ClipboardService`, `AutoSaveService`, `KeyboardShortcutService`, `SampleDataGenerator`
  - **Domain:** `EmailTemplate` entita (Name/Subject/Preheader/Language/ContentJson/RequiredVariables/
    SampleData/IsValidSyntax/verze) — závisí na `PostrionCore.Domain.BaseTenantEntity` → nutno odstřihnout
  - Syntaxe šablon zdokumentovaná v `TEMPLATE_SYNTAX.md` (Scriban: proměnné, filtry, podmínky, smyčky)
- **`src/PostrionCore.Admin/Components/EmailEditor`** (27 .razor) — UI na **Syncfusion**
  (SfButton 68×, SfTextBox 56×, SfDropDownList 34×, SfDialog, SfListView, SfSplitter, SfTab, SfGrid…)
  + 2× MudBlazor: `EmailEditor` (toolbar+splitter), `Toolbox`, `Canvas`, `BlockRenderer`,
  `PropertyPanel` + 14 per-block properties panelů, `SectionPropertyPanel`, `ColumnPropertyPanel`,
  `VariablePicker`, `PreviewPanel`, `MjmlImportExport`, `TemplateList`, `LayoutPresetSelector`,
  `KeyboardShortcutsHelp`; stránky `TemplateEditor` (723 ř.), `TemplateGallery` (449 ř.), `TemplateList` (358 ř.)
- **Odesílání:** `PostrionCore.Infrastructure/Services/SmtpSenderService` — MailKit, retry 3× (1s/2s/4s),
  transientní chyby (IOException/Timeout/SmtpProtocolException), `BuildMimeMessage` (html+text multipart)

### ⚠️ Zjištěné stuby/díry ve zdroji (NEpřenášet, implementovat pořádně)
- `EmailEditor.razor.AddBlock()` — blok vytvoří, ale NIKAM ho nepřidá
- `EmailEditor.razor.PasteBlock()` — vložený blok se nepřidá do šablony
- `EmailEditor.razor.ImportMjml()` — neparsuje nic (`// Parse MJML and update template`)
- `EmailEditor.razor.DeleteBlockAsync()` — `/* Delete logic */`, prázdné
- Aplikace nebyla nikdy nasazená ani pořádně otestovaná → **testy musí být důslednější**,
  zdrojový kód brát jako referenci/inspiraci, ne jako pravdu

### Cílová architektura v Tempo.Blazor
```
src/Tempo.Blazor.EmailTemplates.Abstractions   ← NOVÝ NuGet: VEŠKERÁ logika (model bloků,
        MJML generování, MJML→model IMPORT (plnohodnotný parser, fáze EI), Scriban engine,
        variable extraction, render pipeline, DTO,
        validátory, interfaces ITemplateStore/IEmailSender). Bez Blazor závislostí —
        referencovatelný z API serverů. Deps: Mjml.Net, Scriban, MS.Extensions.*, FluentValidation,
        MS.Extensions.Localization.Abstractions.
src/Tempo.Blazor.EmailTemplates                ← NOVÝ NuGet: Razor class library, komponenta
        TmEmailTemplateEditor + podkomponenty. Deps: Tempo.Blazor (Tm* komponenty),
        Tempo.Blazor.EmailTemplates.Abstractions, Tempo.Blazor.FluentValidation.
tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests   ← NOVÝ: čisté xUnit + FluentAssertions
tests/Tempo.Blazor.EmailTemplates.Tests                ← NOVÝ: bUnit (vzor Tempo.Blazor.Tests)
src/Tempo.Blazor.Demo.Api      ← rozšířit: EmailTemplateEndpoints + EmailSendEndpoints + SmtpEmailSender (MailKit)
src/Tempo.Blazor.Demo.SharedUI ← rozšířit: EmailTemplatesPage (galerie+seznam), editor, send flow
tests/Tempo.Blazor.Demo.Api.Tests ← rozšířit: integrační testy vč. reálného SMTP na smtp4dev
tests/Tempo.Blazor.E2E         ← rozšířit: EmailTemplate*E2ETests.cs + screenshoty
```
- Vzor pro csproj balíčků: `Tempo.Blazor.DocumentFormats.csproj` (metadata, README pack)
  a `Tempo.Blazor.Abstractions.csproj` (multi-target net8/9/10 pokud to deps dovolí, jinak net10).
- Pojmenování komponent: prefix `Tm` (TmEmailTemplateEditor, TmEmailTemplateToolbox, …).
- Modelové třídy: `EmailTemplateDocument`, `EmailSection`, `EmailColumn`, bloky `EmailTextBlock`…
  (neutrální názvy bez vazby na PostrionCore).

### smtp4dev (ověřeno, běží)
- Docker container `testmaster-smtp4dev` (sdílený s jiným projektem — **nemazat cizí zprávy!**)
- SMTP: `localhost:2525` (bez TLS, bez auth → `SecureSocketOptions.None`)
- Web UI + REST API: `http://localhost:5000` — `GET /api/Messages?pageSize=N` funguje (ověřeno),
  vrací `results[]` s `id`, `from`, `to[]`, `receivedDate`, `subject`
- Izolace testů: **každý test používá unikátní adresu příjemce**
  (`emailtemplates-e2e-{guid}@tempo.local`) a filtruje podle ní; mazat jen vlastní zprávy

### Servery pro E2E
- WASM demo: `dotnet run --project src/Tempo.Blazor.Demo --launch-profile https` (port 7106)
- API: `dotnet run --project src/Tempo.Blazor.Demo --launch-profile Tempo.Blazor.Demo.Api` (port 5100)
- Po změně C# REBUILDNOUT a restartovat oba (API drží seed/SMTP konfiguraci)

---

## Průběžné povinnosti (platí pro KAŽDOU fázi)

- [ ] Po každém splněném tasku odškrtnout checkbox zde v souboru
- [ ] Po každé fázi: spustit testy dotčených projektů + krátký zápis výsledku k fázi
- [ ] Po každé UI fázi: E2E screenshot → uložit do `tests/Tempo.Blazor.E2E/__screenshots__/email-templates/`
      a **posoudit jako UX expert** (kontrast, spacing, hierarchie, konzistence s design tokeny);
      nálezy zapsat do sekce „UX nálezy" na konci tohoto souboru a hned opravit
- [ ] Grep brána před commitem: žádné hardcoded české/anglické texty v .razor/.cs nových balíčků
      (`grep -rn '"[A-ZÁ-Ž][a-zá-ž]\+ ' src/Tempo.Blazor.EmailTemplates*` — ručně projít hity)
- [ ] Žádné TODO/FIXME komentáře v novém kódu (`grep -rn "TODO\|FIXME" src/Tempo.Blazor.EmailTemplates*`)
- [ ] Při nejistotě ohledně Tm* komponenty → MCP server tempo-blazor (ne hádat API)

---

## FÁZE E0 — Infrastruktura solution (setup, bez TDD) ✅ HOTOVO 2026-06-11

- [x] E0.1 Vytvořit `src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj`
      (classlib, net10.0 — Mjml.Net 4.x vyžaduje moderní TFM; metadata podle DocumentFormats vzoru:
      PackageId, Description, Tags `blazor;email;mjml;templates;scriban`, README pack, GenerateDocumentationFile,
      TreatWarningsAsErrors). Deps: `Mjml.Net`, `Scriban`, `FluentValidation`,
      `Microsoft.Extensions.Localization.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
      `Microsoft.Extensions.Logging.Abstractions`
- [x] E0.2 Vytvořit `src/Tempo.Blazor.EmailTemplates/Tempo.Blazor.EmailTemplates.csproj`
      (Microsoft.NET.Sdk.Razor, net10.0, scoped CSS bundle; ProjectReference: Tempo.Blazor,
      Tempo.Blazor.EmailTemplates.Abstractions, Tempo.Blazor.FluentValidation; metadata jako E0.1)
- [x] E0.3 Vytvořit `tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests` (xunit + FluentAssertions
      + NSubstitute — verze podle `tests/Tempo.Blazor.Tests/Tempo.Blazor.Tests.csproj`)
- [x] E0.4 Vytvořit `tests/Tempo.Blazor.EmailTemplates.Tests` (bunit + xunit + FluentAssertions + NSubstitute)
- [x] E0.5 Přidat všechny 4 projekty do `TempoBlazor.slnx`; build (jednotlivě — celé solution se buildit nemusí
      kvůli OOM/Demo závislostem; všechny 4 nové projekty buildí 0 chyb)
- [x] E0.6 `InternalsVisibleTo` z obou src projektů na jejich test projekty (přes `<AssemblyAttribute>` v csproj)
- [x] E0.7 Smoke test v obou test projektech, `dotnet test` obou projektů zelený (Abstractions 1/1, UI bUnit 2/2)
- [x] E0.8 README.md skeleton pro oba balíčky (účel, instalace, quick-start — doplní se ve fázi E11/E12)
- [x] E0.9 Spike (časově ohraničený, bez produkčního kódu): konzolově ověřit Mjml.Net 4.0 —
      zkompilovat minimální `<mjml><mj-body><mj-section><mj-column><mj-text>Hi</mj-text>…` na HTML;
      ověřit chování chyb (neznámý tag, nevalidní MJML). **Support matrix pro plnou paritu:**
      ověřit že Mjml.Net renderuje VŠECH 16 body komponent + head: mj-font, mj-breakpoint,
      mj-style (embedded I `inline="inline"` — CSS inlining!), mj-attributes (per-tag, mj-all,
      mj-class), mj-html-attributes, mj-include. Každou mezeru zapsat sem pod E0.9 a
      NAHLÁSIT uživateli k rozhodnutí dřív, než se na tom postaví model
- [x] E0.10 Spike: ověřit tvar smtp4dev REST API — `GET /api/Messages` (filtrace), detail zprávy
      (`/api/Messages/{id}`, `/html`, `/plaintext`, `/source`), `DELETE /api/Messages/{id}`.
      Zjištěné endpointy/parametry zapsat sem pod E0.10

## FÁZE E1 — Abstractions: doménový model dokumentu šablony ✅ HOTOVO 2026-06-11 (69 testů)

> Realizace: model = čisté POCO (generování až E2). Polymorfní JSON přes STJ discriminator **`$type`**
> (NE `type` — koliduje s CLR property `Type` i pod JsonIgnore). DeepClone/CloneWithNewIds přes JSON
> round-trip + `DocumentTree.ReassignIds`. Tree-ops (`DocumentEditing`) přes `DocumentTree` rekurzi
> (hero/group/wrapper). E1.6–E1.19 sloučeno s E1.32 (plný atributový set rovnou). Head: `MjAttributes`
> (All/PerTag/Classes + `Resolve` kaskáda), `MjHtmlSelector` (round-trip only, nerenderuje se).

Každý task = jeden red-green-refactor cyklus. Testy do
`tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests/Model/…`.

- [x] E1.1 `EmailTemplateDocument`: nový dokument má neprázdné `Id`, prázdné `Sections`,
      výchozí `TemplateStyles`, `CreatedAt` UTC
- [x] E1.2 `TemplateStyles`: výchozí hodnoty (šířka 600px, font stack bezpečný pro email,
      barva pozadí, barva textu) — vše jako vlastnosti, žádné magic stringy v generátoru
- [x] E1.3 `EmailSection`: defaults (padding, background, full-width flag), kolekce `Columns`
- [x] E1.4 `EmailColumn`: defaults (šířka v %, padding), kolekce `Blocks`
- [x] E1.5 `EmailBlockBase`: `Id`, `Type` (diskriminátor), společné vlastnosti
      (padding, cssClass, podmínka zobrazení `VisibleWhen` — Scriban výraz)
- [x] E1.6 Blok `EmailTextBlock` (html obsah, font, velikost, barva, zarovnání, line-height)
- [x] E1.7 Blok `EmailButtonBlock` (text, href, barvy, border-radius, zarovnání, padding)
- [x] E1.8 Blok `EmailImageBlock` (src, alt — povinný pro a11y, šířka, href, zarovnání)
- [x] E1.9 Blok `EmailDividerBlock` (barva, tloušťka, šířka v %, styl čáry)
- [x] E1.10 Blok `EmailSpacerBlock` (výška)
- [x] E1.11 Blok `EmailRawBlock` (raw MJML/HTML obsah)
- [x] E1.12 Blok `EmailTableBlock` (řádky/buňky jako model — NE raw HTML; zarovnání, padding, šířky sloupců)
- [x] E1.13 Blok `EmailSocialBlock` (kolekce prvků: síť, href, ikona; layout horizontální/vertikální)
- [x] E1.14 Blok `EmailHeroBlock` (background image/color, výška, vnořené bloky)
- [x] E1.15 Blok `EmailNavbarBlock` (kolekce odkazů: text+href; barvy, font)
- [x] E1.16 Blok `EmailCarouselBlock` (kolekce obrázků: src+alt+href; thumbnails on/off)
- [x] E1.17 Blok `EmailAccordionBlock` (kolekce položek: titulek+obsah; ikony, barvy)
- [x] E1.18 Blok `EmailWrapperBlock` (vnořené sekce, background, border, padding)
- [x] E1.19 Blok `EmailGroupBlock` (vnořené sloupce — zabraňuje stackování na mobilu)
- [x] E1.20 Polymorfní JSON (de)serializace celého dokumentu — System.Text.Json
      s type diskriminátorem; round-trip test: dokument se všemi 14 typy bloků →
      JSON → dokument, hluboká ekvivalence
- [x] E1.21 JSON kompatibilita: deserializace neznámého typu bloku → srozumitelná chyba
      (ne crash, ne tichá ztráta dat); neznámé vlastnosti se ignorují (forward-compat)
- [x] E1.22 `DeepClone()` dokumentu (pro undo/redo a clipboard) — klon je nezávislý,
      bloky mají STEJNÁ Id (historie), + `CloneWithNewIds()` (paste — Id nová)
- [x] E1.23 Operace nad stromem: `FindBlock(id)`, `FindParentColumn(blockId)`, `RemoveBlock(id)`
- [x] E1.24 Operace: `AddBlock(columnId, block, index)` — vložení na pozici, validace existence sloupce
- [x] E1.25 Operace: `MoveBlock(blockId, targetColumnId, targetIndex)` — včetně přesunu v rámci
      stejného sloupce (korektní index po odebrání)
- [x] E1.26 Operace: `DuplicateBlock(blockId)` — kopie s novými Id hned za originál
- [x] E1.27 Operace nad sekcemi: add/remove/move/duplicate sekce; add/remove sloupce v sekci
      s přepočtem šířek (1→100 %, 2→50/50, 3→33/33/34…)
- [x] E1.28 `LayoutPresets`: presety 1/1, 1/2+1/2, 1/3×3, 2/3+1/3, 1/4×4… — test že každý preset
      vygeneruje validní sekci se správnými šířkami v součtu 100 %
- [x] E1.29 `LayoutValidator`: součet šířek sloupců = 100 % ±tolerance, max. hloubka vnoření
      (wrapper/hero), prázdná sekce = warning ne error; lokalizované klíče chyb (ne texty)
- [x] E1.30 `BlockRegistry` + `BlockDescriptor` (typ, lokalizační klíč názvu, ikona, kategorie,
      factory): registry vrací všech 14 bloků, `CreateInstance(type)` vrací nový blok s defaults,
      registrace vlastního bloku zvenčí (extensibilita)

### E1-PARITA — plné pokrytí MJML 4 atributů a head sekce

- [x] E1.31 Vyrobit **paritní checklist atributů**: z oficiální MJML 4 dokumentace vypsat
      KOMPLETNÍ tabulku atributů každé komponenty (vč. defaultů) do
      `docs/email-templates/MJML_ATTRIBUTE_PARITY.md` — tahle tabulka je pak specifikace
      pro E1.32, E2.24, EI a property panely (E7.3)
- [x] E1.32 Atributová parita modelu: doplnit VŠEM 14 blokům + sekci + sloupci kompletní sadu
      vlastností dle checklistu E1.31 (např. section: background-position/repeat/size/url,
      border, border-radius, direction, text-align…; button: inner-padding, line-height,
      text-decoration, vertical-align…; sub-elementy accordion-title/text, social-element,
      carousel-image, navbar-link mají VLASTNÍ plné sady). Test per komponenta: každý atribut
      z checklistu má vlastnost a default odpovídá MJML defaultu (parametrizované testy)
- [x] E1.33 `ExtraAttributes` (dictionary string→string) na každém bloku/sekci/sloupci —
      záchytná síť pro budoucí/neznámé atributy při importu; round-trip test (import→export
      je zachová beze ztráty)
- [x] E1.34 Head model — `EmailHeadStyles` rozšíření `TemplateStyles`: `Breakpoint`,
      kolekce `Fonts` (name+href), kolekce `Styles` (css text + `Inline` flag) — testy defaults
      + validace (breakpoint px hodnota, font href URL)
- [x] E1.35 Head model — výchozí atributy (mj-attributes): per-komponenta defaults
      (`tag → {attr: value}`) + `mj-all`; resolution helper `GetEffectiveAttribute(block, name)`
      s kaskádou: blok > mj-class > per-tag default > mj-all > MJML default — testy kaskády
- [x] E1.36 Head model — pojmenované třídy (mj-class): definice `name → {attr: value}`
      + `MjClasses` reference (list) na blocích/sekcích/sloupcích; testy: víc tříd na bloku,
      pořadí priority dle MJML (pořadí v atributu)
- [x] E1.37 Head model — `mj-html-attributes` (selector + custom atributy) — model + testy
- [x] E1.38 Rozšířit polymorfní serializaci (E1.20) o celý head model + ExtraAttributes +
      MjClasses; round-trip test plně osazeného dokumentu

## FÁZE E2 — Abstractions: MJML generování + HTML render ✅ HOTOVO 2026-06-11 (E2+E2-PARITA, 107 testů)

> Realizace: `Rendering/MjmlGenerator` (`MjmlAttributeBuffer` Optional/Defaulted/Flag/Common/BlockCommon →
> vynechává defaulty+escapuje). `MjmlEscape`. `HtmlContentSanitizer` (**AngleSharp 1.1.2**
> — NE Ganss kvůli konfliktu AngleSharp 0.17 vs Tempo.Blazor 1.1.2; vlastní whitelist sanitizér). `IMjmlCompiler`+`MjmlNetCompiler` (try/catch→RenderError,
> malformed nehází). `TextVersionGenerator`. `EmailDocumentValidator` (+LayoutValidator; DocumentValidationKeys).
> Golden přes `[CallerFilePath]` bootstrap. `MjmlGeneratorOptions.EmitHtmlAttributes` (false=render, ForExport=round-trip).
> mj-attributes děti EXPLICIT close (E0.9). VisibleWhen→`{{ if }}…{{ end }}`.
> ⚠️ ZNÁMÉ OMEZENÍ: model dovolí group/wrapper/hero uvnitř column; generátor emituje tam kde jsou →
> group/wrapper-in-column je nevalidní MJML (render by chyboval). Editor (E6/E7) musí placement omezit;
> plné placement-rules = pozdější refinement.

Testy `…Tests/Rendering/…`. Golden testy: očekávané MJML fragmenty jako string konstanty v testech
(malé, čitelné), pro plné dokumenty golden soubory v `…Tests/Rendering/Golden/`.

- [x] E2.1 `MjmlGenerator`: prázdný dokument → validní `<mjml><mj-head>…<mj-body>` skelet
      (head: title=Subject, preview=Preheader, mj-attributes z `TemplateStyles`, font import)
- [x] E2.2 HTML/atribut escaping helper: `<`, `&`, `"`, `'` v textech a atributech —
      test injekce `"><script>` do textu bloku NEunikne do MJML jako markup
- [x] E2.3 Generování `mj-section` (atributy: padding, background-color/url, full-width)
- [x] E2.4 Generování `mj-column` (width, padding) uvnitř sekce
- [x] E2.5 `mj-text` z EmailTextBlock (vč. povoleného inline HTML — sanitizace: whitelist tagů
      b/i/u/a/br/span/p/ul/ol/li, atributy href/style omezené; test XSS vektorů)
- [x] E2.6 `mj-button` (href, barvy, radius…) + test, že prázdný href → validační chyba dokumentu,
      ne rozbité MJML
- [x] E2.7 `mj-image` (src, alt, width, href) — chybějící alt → validační warning (a11y)
- [x] E2.8 `mj-divider`, `mj-spacer` (E2.8 jeden task — triviální bloky)
- [x] E2.9 `mj-raw` — obsah se vkládá BEZ escapingu, ale dokument s raw blokem nese flag
      `ContainsRawContent` (UI ho zobrazí jako upozornění)
- [x] E2.10 `mj-table` z modelu řádků/buněk (escaping obsahu buněk)
- [x] E2.11 `mj-social` + `mj-social-element` (mapování známých sítí na ikony, custom ikona)
- [x] E2.12 `mj-hero` s vnořenými bloky (rekurze generátoru)
- [x] E2.13 `mj-navbar` + `mj-navbar-link`
- [x] E2.14 `mj-carousel` + `mj-carousel-image`
- [x] E2.15 `mj-accordion` + `mj-accordion-element` (title/text)
- [x] E2.16 `mj-wrapper` s vnořenými sekcemi (rekurze) + `mj-group` se sloupci
- [x] E2.17 `VisibleWhen` podmínka bloku → obal `{{ if <expr> }}…{{ end }}` v MJML
- [x] E2.18 Golden test: kompletní dokument se všemi 14 bloky → MJML golden soubor
      (normalizace whitespace; při změně generátoru vědomě přegenerovat a zkontrolovat diff)
- [x] E2.19 `IMjmlCompiler` + `MjmlNetCompiler`: MJML → HTML přes Mjml.Net; úspěch vrací HTML,
      chyby Mjml.Net se mapují na `RenderError` s pozicí; test nevalidního MJML
- [x] E2.20 `TextVersionGenerator`: dokument → plain-text (texty, odkazy jako `text (url)`,
      oddělovače sekcí, tabulka řádek po řádku; bez HTML tagů) — testy per blok + celek
- [x] E2.21 Validace dokumentu `EmailDocumentValidator` (strukturální: LayoutValidator + bloky —
      povinná pole, smysluplné limity; výsledek = kolekce {Severity, lokalizační klíč, cesta k bloku})

### E2-PARITA — generátor emituje plný head + všechny atributy

- [x] E2.22 Head emission: `mj-breakpoint`, `mj-font` (per font), `mj-style` (embedded i
      `inline="inline"`), `mj-attributes` (mj-all + per-tag defaults + mj-class definice),
      `mj-html-attributes` (mj-selector + mj-html-attribute) — test per head featura
- [x] E2.23 Emission `mj-class` referencí a `css-class` na blocích/sekcích/sloupcích +
      `ExtraAttributes` se emitují beze změny — testy
- [x] E2.24 Plná atributová emission: parametrizovaný test nad checklistem E1.31 — pro KAŽDOU
      komponentu nastav každou vlastnost na ne-default hodnotu → atribut se objeví v MJML
      se správným názvem/formátem; default hodnoty se NEemitují (čisté MJML)
- [x] E2.25 Rozšířit golden test E2.18 o plný head (fonty, styles, attributes, classes,
      html-attributes) — golden soubor pokrývá 100 % featur

## FÁZE EI — Abstractions: PLNOHODNOTNÝ MJML→model import ✅ HOTOVO 2026-06-11 (129 testů celkem)

> Realizace: `Import/MjmlImporter` (XDocument + **mj-raw shim** regex → placeholder, restore po parse;
> PreserveWhitespace). `ImportResult`/`ImportMessage`/`ImportKeys`/`IMjmlIncludeResolver`. `AttrBag`
> (konzumuje atributy, zbytek→ExtraAttributes). Head (title/preview/breakpoint/font/style/attributes/
> html-attributes). Všech 14 bloků + sub-elementy. **Lossless:** neznámý element→RawBlock+warning,
> neznámý atribut→ExtraAttributes. **VisibleWhen round-trip:** `ImportBlocks` čte i XText uzly,
> `{{ if expr }}` před elementem→block.VisibleWhen (jinak by se kondice ztratila). mj-include přes
> resolver (fragment wrap). EI.16: `EmailDocumentValidator` nálezy→Warnings. EI.14 round-trip =
> idempotence `gen(import(gen(doc)))==gen(doc)` (ForExport). EI.15 fidelita na realistickém externím
> MJML (import→export→Mjml.Net render obsahuje obsah).
> POZN EI.6: model drží padding/shorthand jako VERBATIM string → žádná dekompozice, round-trip triviální.
> ⚠️ STRUKTURÁLNÍ: body-level mj-wrapper→hoist sekcí (warning WrapperFlattened, ztrácí wrapper bg/attrs);
> body-level mj-hero/raw a section-level mj-group→zabaleno do section/column (warning ElementWrapped).
> Pro NAŠE exporty je to bezproblém (generátor emituje wrapper/group/hero uvnitř column). Plná body-level
> fidelita cizích šablon = vyžaduje rozšíření body-modelu (follow-up, souvisí s E2 known-limitation).
> EI.15 plný stažený korpus mjml.io = follow-up (zatím 1 reprezentativní fixture + idempotence).

Cíl: libovolné validní MJML 4 (vč. cizích šablon — marketplace, designéři, mjml.io examples)
lze importovat do modelu **bezztrátově** a dál plně editovat. Klíčová vlastnost: fidelity —
re-export importované šablony musí po Mjml.Net renderu dát semanticky shodné HTML jako originál.
Testy `…Tests/Import/…`.

- [x] EI.1 `MjmlParser` — lenient XML vrstva: parsování MJML dokumentu/fragmentu, tolerance
      HTML komentářů a conditional comments (`<!--[if mso]>`), obsah `mj-raw` se čte jako
      RAW text (nesmí se pokoušet o XML parse vnitřku!); chyby s řádkem/sloupcem; testy
      vč. záludných vstupů (CDATA, entity, BOM, CRLF)
- [x] EI.2 Head import: `mj-title`→Subject, `mj-preview`→Preheader (vč. chybějících = defaults)
- [x] EI.3 Head import: `mj-font` (name+href), `mj-breakpoint`, `mj-style` (embedded i
      `inline="inline"`) → head model z E1.34
- [x] EI.4 Head import: `mj-attributes` — `mj-all`, per-tag defaults, definice `mj-class`
      → model E1.35/E1.36. DŮLEŽITÉ: kaskádu NErozpouštět do bloků (zachovat jako defaults/třídy
      v modelu — jinak se rozbije fidelity i editovatelnost globálních stylů); test: import →
      export → mj-attributes sekce ekvivalentní originálu
- [x] EI.5 Head import: `mj-html-attributes` → model E1.37
- [x] EI.6 Atributový mapper: string atribut → typovaná vlastnost dle checklistu E1.31 —
      shorthand parsing (`padding="10px 20px"` → 4 hodnoty, `border="1px solid #000"`),
      jednotky (px/%/none), barvy (hex/rgb/named), enum hodnoty; round-trip test
      parse→format = identita pro všechny formáty z checklistu
- [x] EI.7 Body import — struktura: `mj-section`/`mj-column` (vč. všech atributů přes EI.6),
      vnořování `mj-wrapper` (sekce uvnitř) a `mj-group` (sloupce uvnitř) rekurzivně; testy
- [x] EI.8 Body import — jednoduché bloky: mj-text (vnitřní HTML jako obsah — beze změny),
      mj-button, mj-image, mj-divider, mj-spacer, mj-raw, mj-table (HTML řádky → model
      řádků/buněk; pokud struktura tabulky nejde namapovat na model → fallback RawBlock
      s warningem); test per blok
- [x] EI.9 Body import — kompozitní bloky se sub-elementy: mj-social(+element),
      mj-navbar(+link), mj-carousel(+image), mj-accordion(+element/title/text), mj-hero
      (vnořené bloky rekurzivně); test per blok vč. plných atributů sub-elementů
- [x] EI.10 Import `mj-class` referencí (`mj-class="a b"`) a `css-class` na elementech →
      `MjClasses`/CssClass v modelu; test priority při exportu zpět (pořadí zachováno)
- [x] EI.11 Politika neznámých věcí (lossless záchranná síť): neznámý/nepodporovaný mj-* element
      → `EmailRawBlock` s původním markupem + warning {pozice, lokalizační klíč}; neznámý atribut
      známého elementu → `ExtraAttributes` (E1.33, emituje se zpět při exportu); test: dokument
      s vymyšleným elementem/atributem přežije import→export bez ztráty výstupu
- [x] EI.12 `mj-include`: `IMjmlIncludeResolver` hook (host může dodat resolver souborů/URL),
      default implementace = chyba s lokalizovaným vysvětlením; test s fake resolverem
      (include se rozbalí a importuje) + test default chování
- [x] EI.13 `ImportResult` API: `{Document, Warnings[], Errors[]}` — žádné výjimky ven;
      prázdný/nevalidní vstup → srozumitelné chyby s pozicí; všechny zprávy přes lokalizační klíče
- [x] EI.14 Round-trip golden (vlastní exporty): `Import(Export(doc))` deep-equal pro dokument
      se VŠEMI bloky + head featurami (rozšíření golden z E2.25) — bezztrátový kruh
- [x] EI.15 **Fidelity korpus reálných šablon**: stáhnout sadu open-source MJML šablon
      (oficiální mjml.io examples — MIT; uložit do `…Tests/Import/Corpus/` s license soubororem)
      → pro každou: import → export → Mjml.Net render originálu i re-exportu → HTML semanticky
      shodné (normalizace whitespace/pořadí atributů); každý rozdíl = bug generátoru/parseru,
      opravit test-first
- [x] EI.16 Integrace s validací: importovaný dokument projde `EmailDocumentValidator` (E2.21)
      a výsledek se přibalí do ImportResult.Warnings (např. obrázky bez alt v cizí šabloně)

## FÁZE E3 — Abstractions: Scriban templating sandbox ✅ HOTOVO 2026-06-11 (163 testů celkem)

> Realizace `Templating/`: `ScribanTemplateEngine` (Scriban 7.2.4) + `ITemplateEngine`, `Result<T>`,
> `TemplateSecurityOptions` (LoopLimit/RecursiveLimit/MaxOutputLength/Timeout/StrictVariables),
> `TemplateValidationResult`/`TemplateError`, `ObjectToScriptObjectConverter` (dict→ScriptObject,
> POCO→Import; nested/list), `SandboxedScriptOutput` (IScriptOutput cooperative time+length limit),
> `TemplateVariableExtractor` (ScriptVisitor; loop-locals tracked explicitly — Scriban loop var je
> GLOBAL ne local!; collection = for-iterator), `TemplateVariableInfo`/`VariableKind`,
> `EmailDocumentVariableExtractor` (scan subject/preheader/bloky/raw; VisibleWhen obalit `{{ }}`),
> `SampleDataGenerator` (heuristiky is_/email/url/price/count/date/name; nested dicts + collection→list).
> Default member renamer = snake_case (PascalCase access funguje). include disabled (no TemplateLoader),
> reflexe nedostupná → sandbox.
> POZN E3.6: Scriban builtiny (string/array/object/math/date/…) jsou inherentně bezpečné (žádné IO/reflexe),
> takže NEbylo nutné psát custom Safe*Functions třídy (na rozdíl od PostrionCore) — sandbox zajištěn
> přes no-TemplateLoader + bez .NET type importu + limity, ověřeno testy. POZN: `when` je rezervované
> Scriban klíčové slovo (case/when) — nepoužívat jako název proměnné. truncate N započítává "..." do délky.
> `docs/email-templates/TEMPLATE_SYNTAX.md` napsán (tailored na náš engine, ne kopie PostrionCore).

Testy `…Tests/Templating/…`.

- [x] E3.1 `ITemplateEngine` + `ScribanTemplateEngine.Render`: `{{ name }}` + model → substituce;
      `Result<string>` úspěch/chyba (žádné výjimky ven)
- [x] E3.2 Vnořené vlastnosti `{{ user.address.city }}`, indexace `{{ items[0].name }}`,
      case-insensitive/snake_case přístup (ObjectToScriptObjectConverter: C# PascalCase → snake_case)
- [x] E3.3 Filtry: upcase, downcase, capitalize, truncate, default, string.replace/split/strip,
      array.join/size, date.to_string, math.format — po jednom testu
- [x] E3.4 Podmínky if/else/else if + operátory ==, !=, <, >, &&, ||, ! ; smyčky for
      (vč. for.index, for.first, for.last)
- [x] E3.5 Sandbox: zakázané operace — přístup k typům/reflexi, `include`, nekonečná smyčka
      → `TemplateSecurityOptions` (timeout, limit iterací, limit délky výstupu, limit rekurze);
      testy že každý limit skutečně zafunguje a vrátí chybu, ne hang
- [x] E3.6 Safe funkce: `SafeStringFunctions`/`SafeDateFunctions`/`SafeMathFunctions` —
      whitelist builtinů (žádné `object`, žádné IO); test že neexponovaný builtin není dostupný
- [x] E3.7 Chybějící proměnná: default chování = prázdný řetězec + warning v výsledku
      (strict mód = chyba) — konfigurovatelné přes options
- [x] E3.8 `Validate(template)`: syntax error → ValidationResult s řádkem/sloupcem a lokalizačním
      klíčem; validní šablona → Valid
- [x] E3.9 `ExtractVariables(template)`: prosté `{{ a }}`, vnořené `{{ a.b.c }}` (vrací kořen
      i celou cestu), proměnné z podmínek a smyček (kolekce z `for x in items` → `items`),
      deduplikace, ignorace loop-lokálních proměnných
- [x] E3.10 Extrakce proměnných z celého dokumentu: projde texty/href/VisibleWhen všech bloků
      **vč. obsahu Raw bloků** (kvůli importovaným/vloženým šablonám) + Subject + Preheader
      → sjednocený seznam `TemplateVariableInfo {Path, Kind(scalar/collection)}`;
      test i nad dokumentem vzniklým MJML importem (EI)
- [x] E3.11 `SampleDataGenerator`: z extrahovaných proměnných vygeneruje smysluplný sample JSON
      (jméno→string, *_count→číslo, items→pole s 2 prvky…), round-trip: sample data
      projdou renderem bez warningů
- [x] E3.12 Zkopírovat a upravit `TEMPLATE_SYNTAX.md` do `docs/email-templates/` (reference pro
      uživatele komponenty; aktualizovat podle skutečně podporovaných filtrů z E3.3–E3.6)

## FÁZE E4 — Abstractions: render pipeline, DTO, validátory, DI ✅ HOTOVO 2026-06-11 (191 testů celkem)

> Realizace: `Rendering/EmailTemplateRenderer`+`IEmailTemplateRenderer`+`RenderResult` (pipeline:
> Scriban subject/preheader → MjmlGenerator → Scriban na MJML (resolví VisibleWhen) → MjmlNetCompiler →
> HTML; text = TextVersionGenerator → Scriban; chyby agregované v pořadí fází; async přes Task.FromResult).
> `Dtos/` (Summary/Detail/Create/Update/RenderPreview req+resp/RenderErrorDto/SendEmailRequest) +
> `EmailTemplateMapper` (ToContentJson/ToDocument/ToDetailDto/ToSummaryDto/ApplyCreate/ApplyUpdate;
> RequiredVariables = root segmenty z EmailDocumentVariableExtractor). `Validation/` 3 validátory
> (Create/Update/Send) s `IStringLocalizer<EmailTemplateValidationResources>`; resx en(neutral)+cs+fr v
> `Resources/` (marker třída + auto-embed; ResourceManagerStringLocalizerFactory v testech). `Contracts/`
> `IEmailTemplateStore` (async CRUD+List+IsNameAvailable) + `IEmailSender`/`EmailMessage`.
> `ServiceCollectionExtensions.AddTempoEmailTemplateEngine(Action<TemplateSecurityOptions>?)` —
> engine/generator/compiler/renderer/textgen/registry singletony, validátory scoped (lokalizace dodá host
> přes AddLocalization). Edge-cases (E4.8): prázdný dokument, extrémní délka, RTL (arab/hebr), null optionals,
> unicode/emoji celou pipeline. POZN: test soubory v ne-Model namespace musí mít `using ...Model;` (jinak
> `Model.X` koliduje s testovním `...Tests.Model`).

- [x] E4.1 `RenderResult` (Html, Subject, Preheader, TextVersion, Errors[], Success) +
      `IEmailTemplateRenderer.RenderAsync(document, model?)`: pipeline
      dokument → (Scriban přes Subject/Preheader/MJML) → Mjml.Net → HTML + text verze;
      test happy path s proměnnými ve všech třech částech
- [x] E4.2 Render bez modelu (preview se sample daty z dokumentu / bez substituce) +
      agregace chyb z obou enginů do `Errors` se zachováním pořadí fází
- [x] E4.3 DTO v Abstractions: `EmailTemplateSummaryDto`, `EmailTemplateDetailDto`
      (Name, Subject, Preheader, Language, ContentJson, RequiredVariables, SampleDataJson,
      IsActive, UpdatedAt…), `CreateEmailTemplateRequest`, `UpdateEmailTemplateRequest`,
      `RenderPreviewRequest/Response`, `SendEmailRequest` (templateId, to[], cc[], variablesJson),
      mapování dokument↔DTO (testy round-trip)
- [x] E4.4 FluentValidation: `CreateEmailTemplateRequestValidator` (+Update) — Name povinný,
      délkové limity, Language ISO kód; zprávy POUZE přes `IStringLocalizer` +
      `Resources/EmailTemplateValidationResources.resx` (en default) + `.cs.resx` + `.fr.resx`
- [x] E4.5 `SendEmailRequestValidator` — to[] neprázdné, validní emaily, variablesJson parsovatelný;
      lokalizované zprávy; test cs i en kultury
- [x] E4.6 Interfaces pro hosty: `IEmailTemplateStore` (CRUD + list, async, CancellationToken),
      `IEmailSender.SendAsync(EmailMessage)` kde `EmailMessage{From,To[],Cc[],Subject,Html,Text}`
      — čisté kontrakty, test jen kompilace/DI
- [x] E4.7 `ServiceCollectionExtensions.AddTempoEmailTemplateEngine()` — registruje engine,
      renderer, registry, validátory; test: po registraci lze resolvnout všechny služby
- [x] E4.8 Mutation-style review testů fáze E1–E4: cíleně projít hraniční případy
      (prázdné kolekce, null, extrémní délky, unicode/emoji v textech, RTL text) — doplnit
      chybějící testy. Unicode: subject s diakritikou a emoji přežije celou pipeline

## FÁZE E5 — UI balíček: skeleton, lokalizace, design tokeny ✅ HOTOVO 2026-06-11 (UI 11 testů)

> Realizace: `Resources/TmEmailResources` (marker + resx en/cs/fr, auto-embed) + parity reflexní test
> (ResourceManager.GetResourceSet tryParents:false, Except().BeEmpty obě cesty). `Localization/ITmEmailLocalizer`
> +`DefaultTmEmailLocalizer` (vzor Tempo ITmLocalizer). `ServiceCollectionExtensions.AddTempoEmailTemplates`
> (AddLocalization + TryAddSingleton localizer + AddTempoEmailTemplateEngine). `_Imports.razor` (@inject ITmEmailLocalizer Loc).
> `Components/TmEmailTemplateEditor.razor`+`.razor.css` — 3-panel přes **TmSplitter+TmSplitterPane**
> (Tempo JE má — Components/Layout/, moje dřívější tvrzení bylo špatné!). Params Document/DocumentChanged/OnSave.
> Scoped CSS jen var(--tm-*) tokeny (s hex fallbacky). Toolbar: Save (funkční→OnSave, `data-tm-save`),
> Undo/Redo disabled (poctivé — historie až E7); MJML/Preview tlačítka AŽ v E8 (žádné dead controls).
> bUnit: ITmLocalizer echo mock + JSInterop loose + AddHttpClient + AddTempoEmailTemplates.
> POZN: na TmButton jde předat `data-tm-save` (forwarduje AdditionalAttributes) → stabilní test selektor
> (NE podle lokalizovaného textu, stroj může běžet cs). TmEmptyState params Title/Icon/Description/ActionText.

bUnit testy do `tests/Tempo.Blazor.EmailTemplates.Tests`.

- [x] E5.1 `Resources/TmEmailResources.resx` (en) + `.cs.resx` + `.fr.resx` podle vzoru TmResources;
      lokalizer pattern převzít z `Tempo.Blazor/Localization/DefaultTmLocalizer.cs`; test: klíč
      existuje ve všech 3 jazycích (reflexní test nad resx — chybějící překlad = fail)
- [x] E5.2 `ServiceCollectionExtensions.AddTempoEmailTemplates()` (UI vrstva — registruje
      i engine z E4.7); bUnit smoke: komponenta se zaregistrovanými službami se vyrenderuje
- [x] E5.3 Sdílené scoped CSS proměnné: všechny barvy/spacing/radius/typografie POUZE přes
      `var(--tm-*)` design tokeny; založit `wwwroot/css` jen pokud nutné (preferovat scoped css)
- [x] E5.4 `TmEmailTemplateEditor.razor` skeleton: 3-panelový CSS grid layout
      (toolbox | canvas | properties), resizable splittery (CSS + pointer events — žádný Syncfusion;
      ověřit přes MCP jestli Tempo.Blazor nemá vhodnou layout komponentu, jinak vlastní v rámci balíčku),
      parametry `Document`, `DocumentChanged`, `OnSave`; bUnit: vyrenderuje 3 panely + toolbar

## FÁZE E6 — UI: Toolbox, Canvas, výběr, drag&drop ✅ HOTOVO 2026-06-11 (UI 38 testů)

> Realizace: `TmEmailTemplateToolbox` (z IBlockRegistry grupováno dle kategorií + LayoutPresets;
> data-tm-block/data-tm-preset; OnAddBlock/OnAddSection/OnDragBlockStart). `TmEmailTemplateCanvas`
> (render sekce→sloupce→bloky, jednoduchý preview ne MJML; výběr přes SelectedId/SelectedIdChanged;
> a11y role=group/listitem + aria-selected jako STRING "true"/"false" (NE bool — Blazor renderuje bool
> jako boolean atribut!); block toolbar up/down/duplicate/delete + section toolbar add-column/up/down/
> duplicate/delete + column remove; OnDocumentChanged; drag&drop přes @for s drop-zónami (DragActive),
> draggable bloky; keyboard Delete/Backspace/Ctrl+D/šipky/Escape). Editor drží `_dragPayload`
> (BlockDescriptor=nový | Guid=přesun), HandleDropAsync→AddBlock/MoveBlock; ResolveTargetColumn pro
> add-via-click (E6.6, prázdný dok→auto Single sekce). Resx doplněny block.*.name/category.*/layout.preset.*
> /akce (en/cs/fr, parity test je vynutil).
> POZN: `@section.Id` v atributu Razor bere jako MVC `@section` direktivu → obalit `@(section.Id)`!
> Closure v `@for`: index/columnId zachytit do lokálních proměnných. bUnit drag: TriggerEvent("ondragstart"/
> "ondrop", new DragEventArgs()) (using Microsoft.AspNetCore.Components.Web). aria-dropeffect VYNECHÁNO
> (deprecated v ARIA 1.1).

- [x] E6.1 `TmEmailTemplateToolbox`: vykreslí bloky z `BlockRegistry` seskupené dle kategorií,
      lokalizované názvy, ikony (TmIcon); bUnit: 14 položek, click → `OnAddBlock` callback
- [x] E6.2 Toolbox: layout presety (E1.28) jako druhá sekce; click → `OnAddSection(preset)`
- [x] E6.3 `TmEmailTemplateCanvas`: render dokumentu — sekce → sloupce → bloky (každý blok
      jednoduchá vizuální reprezentace, NE plný MJML render); bUnit: struktura DOM odpovídá modelu
- [x] E6.4 Canvas výběr: klik na blok → `SelectedBlockChanged`, vizuální zvýraznění (scoped css
      třída), klik na sekci/sloupec (lišta s úchytem) → výběr sekce/sloupce; Escape → deselect
- [x] E6.5 Canvas prázdný stav: TmEmptyState s lokalizovaným hintem „přetáhni blok z toolboxu"
- [x] E6.6 Přidání bloku klikem v toolboxu: do vybraného sloupce (nebo poslední sekce; když
      žádná → auto-vytvořit sekci 1-sloupec); bUnit: model PO kliknutí obsahuje blok — kryje
      díru ze zdrojového `AddBlock()` stubu
- [x] E6.7 Drag&drop z toolboxu na canvas: HTML5 draggable + dragover zóny mezi bloky
      (drop indikátor); bUnit: simulované drag eventy vloží blok na správný index
- [x] E6.8 Drag&drop přesun existujícího bloku (v rámci sloupce i mezi sloupci) — využívá
      `MoveBlock` z E1.25; bUnit testy obou případů
- [x] E6.9 Block akce v canvasu (hover toolbar bloku): duplikovat, smazat, přesunout nahoru/dolů;
      bUnit na každou akci — kryje díru `DeleteBlockAsync` stubu
- [x] E6.10 Sekce akce: duplikovat/smazat/přesunout sekci; přidat sloupec, odebrat sloupec
      (přepočet šířek z E1.27)
- [x] E6.11 Klávesnice na canvasu: Delete=smazat výběr, Ctrl+D=duplikovat, šipky=navigace mezi
      bloky, Tab pořadí; bUnit přes KeyboardEventArgs
- [x] E6.12 A11y: bloky mají role/aria-label (lokalizované), výběr má aria-selected,
      drop zóny aria-dropeffect; bUnit asserts na atributy

## FÁZE E7 — UI: Property panely, undo/redo, clipboard, autosave ✅ HOTOVO 2026-06-11 (UI 87 testů vč. E8)

> 2026-06-12 BESPOKE EDITORY E7.3a/c/e/l: a Text→`TmRichEditorSimple` (Content excludnut z reflexních polí přes
> nový `Exclude` param), c Image→`TmFileDropZone` upload + `OnImageUpload` host callback (Func<IBrowserFile,Task<string>>
> editor→panel) + alt-required TmAlert, e Raw→TmTextArea + výrazné TmAlert varování (neescapováno), l Wrapper/Group→
> nesting-info TmAlert. b/d/f/g/h/i/j/k už hotové (reflexe+list+table editor). E8.6b/E8.8 testy přidány (JSON import happy/invalid, lokalizace cs/en/fr toolbar). UI 99 testů.
> KOMPLETNÍ: E7.3f `TmEmailTableEditor` (řádky/buňky, add/remove, header toggle), E7.9 `TmKeyboardShortcutsHelp`
> („?" tlačítko + ShortcutCategories), E7.11 mj-class ASSIGNMENT (block MjClasses) + `TmEmailClassesEditor`
> (DEFINITIONS manager MjAttributes.Classes CRUD v dokument panelu), E7.12 `TmEmailKeyValueEditor` ExtraAttributes
> (block/section/column) + `TmEmailHtmlAttributesEditor` (mj-html-attributes selektory v dokument panelu).

> HOTOVO (E7.1/7.2/7.3-jádro/7.4/7.5/7.6/7.7/7.8/7.10-jádro): `Services/EditorHistoryService` (DeepClone
> snapshoty, depth limit, coalescing TimeProvider+coalesceKey) + `ClipboardService` (Copy/Paste CloneWithNewIds).
> `Components/PropertyReflection` + `TmEmailObjectFields` (reflexní editor scalar string/bool/int, primary +
> Advanced details, data-tm-prop) → **garantuje atributovou paritu (reflexní test)**. `TmEmailPropertyPanel`
> dispatcher (block/section/column/document, data-tm-prop-target). `TmEmailListEditor<TItem>` (add/remove/up/down)
> → sub-elementy social/navbar/carousel/accordion + head fonty/CSS. Editor: undo/redo toolbar (data-tm-undo/redo)
> + Ctrl+Z/Y, clipboard Ctrl+C/X/V, autosave (AutoSaveOptions+TimeProvider, OnAutoSave). CommitAsync(coalesceKey).
> ⚠️ ZBÝVÁ (follow-up): **E7.3f table grid**, **E7.9 TmKeyboardShortcutsHelp + „?"**, **E7.11 mj-class manager**,
> **E7.12 mj-html-attributes + ExtraAttributes editor**, polish E7.3a/c (rich-text/image upload). VariablePicker=E8.
> POZN: attribut labely = humanizovaný název property (NE resx) — lokalizovat ~200 MJML atributů mimo scope.

- [x] E7.1 `TmEmailTemplatePropertyPanel`: dispatcher — podle typu výběru zobrazí správný
      sub-panel (blok dle typu / sekce / sloupec / dokument-globální styly); bUnit dispatch testy
- [x] E7.2 Panel dokumentu: Name, Subject (s VariablePicker integrací), Preheader, Language,
      globální styly (šířka, fonty, barvy) — TmTextInput/TmSelect/TmFormField; změna → DocumentChanged
- [x] E7.3 Per-blok properties panely — po jednom tasku/bloku, každý bUnit: render hodnot +
      editace → změna modelu + DocumentChanged. **PARITA: panel musí pokrýt VŠECHNY atributy
      bloku z checklistu E1.31** — běžné nahoře, zbytek v rozbalovací sekci „Pokročilé"
      (TmAccordion/TmSection), aby UX zůstalo čisté; bUnit test parity: každá vlastnost bloku
      má v panelu editor (reflexní test proti checklistu):
  - [x] E7.3a Text (TmRichEditorSimple pro obsah — ověřit přes MCP; font/velikost/barva/zarovnání)
  - [x] E7.3b Button (text, href, barvy, radius, zarovnání)
  - [x] E7.3c Image (src + upload přes TmFileDropZone → callback `OnImageUpload` host-provided,
        alt povinné pole s validací, šířka, odkaz)
  - [x] E7.3d Divider + Spacer (jeden task)
  - [x] E7.3e Raw (TmTextArea + výrazné lokalizované varování o neescapovaném obsahu)
  - [x] E7.3f Table (editor mřížky: přidat/odebrat řádek/sloupec, editace buněk)
  - [x] E7.3g Social (seznam položek add/remove/reorder, výběr sítě, href)
  - [x] E7.3h Hero (background, výška)
  - [x] E7.3i Navbar (odkazy add/remove/reorder)
  - [x] E7.3j Carousel (obrázky add/remove/reorder + alt)
  - [x] E7.3k Accordion (položky add/remove/reorder, titulek+obsah)
  - [x] E7.3l Wrapper + Group (padding/background; info o vnoření)
- [x] E7.4 Panel sekce (padding, background color/url, full-width) + panel sloupce (šířka se
      synchronizací sousedů — součet 100 %, padding)
- [x] E7.5 Editor historie (`EditorHistoryService` v UI balíčku): immutable snapshoty přes
      `DeepClone` (E1.22), limit hloubky, `CanUndo/CanRedo`, coalescing rychlých text změn
      (debounce 500 ms = 1 krok); čisté unit testy bez Blazoru
- [x] E7.6 Undo/redo zapojené do editoru: toolbar tlačítka (disabled stavy) + Ctrl+Z/Ctrl+Y;
      bUnit: edit → undo → model je předchozí, redo → znovu
- [x] E7.7 Clipboard service (interní, blok-level): Copy/Cut/Paste vybraného bloku,
      paste = `CloneWithNewIds` na pozici za výběr; Ctrl+C/X/V; bUnit — kryje díru `PasteBlock` stubu
- [x] E7.8 Autosave: `AutoSaveOptions {Enabled, Interval}` + parametr `OnAutoSave` —
      debounced volání po změnách, status indikátor v toolbaru (lokalizovaný „Uloženo/Ukládám…/Chyba");
      bUnit s fake time providerem (`TimeProvider`)
- [x] E7.9 `TmKeyboardShortcutsHelp` — POUŽÍT existující Tempo.Blazor komponentu (ověřit MCP),
      naplnit zkratkami editoru; toolbar tlačítko „?"
- [x] E7.10 Panel dokumentu — head featury: správa fontů (add/remove, name+href s validací URL),
      breakpoint, editor vlastního CSS (mj-style, TmTextArea/code editor, přepínač
      embedded/inline, výrazné lokalizované upozornění na dopad inline CSS); bUnit testy
- [x] E7.11 Správce pojmenovaných tříd (mj-class): CRUD tříd s key-value editorem atributů
      (našeptávání názvů atributů z checklistu E1.31) + přiřazování tříd na blok/sekci/sloupec
      v property panelech (TmTagPicker/TmMultiSelect); bUnit: definice třídy + přiřazení →
      promítne se do modelu i do generovaného MJML
- [x] E7.12 Editor `mj-html-attributes` (selector + atributy, key-value) v panelu dokumentu
      + key-value editor `ExtraAttributes` v sekci „Pokročilé" každého bloku (zachované
      neznámé atributy z importu jsou viditelné a editovatelné, ne černá skříňka); bUnit

## FÁZE E8 — UI: VariablePicker, Preview, MJML export/import, validace ✅ HOTOVO 2026-06-11 (UI 87 testů vč. E7)

> KOMPLETNÍ: E8.2 variable picker integrace (document→Subject, text→Content) + **cursor-precise insert přes JS**
> (`wwwroot/tm-email-variable-insert.js` insertToken na caret last-focused pole, append fallback pro testy/prerender),
> E8.4 sample-data editor (JSON textarea + generate) + **750ms debounce** (TimeProvider), E8.6b JSON export
> (mode toggle) + file upload (TmFileDropZone), E8.7 Scriban per-field validace (template.syntax_error).

> HOTOVO (E8.1/8.3/8.5/8.6/8.7): `TmEmailVariablePicker` (TmSearchInput filtr + insert `{{ path }}` token,
> data-tm-variable, kolekce badge). `TmEmailTemplatePreview` (inject IEmailTemplateRenderer, sample data
> z SampleDataGenerator/VariablesJson, **iframe sandbox="" srcdoc**, desktop/mobil + HTML/text toggly,
> chyby přes TmAlert). `TmEmailExportDialog` (TmModal + readonly textarea MjmlGenerator ForExport).
> `TmEmailImportDialog` (MJML přes MjmlImporter / JSON přes serializer, preview summary+warnings/errors,
> confirm→OnImport, undo step). `TmEmailValidationPanel` (EmailDocumentValidator, severity, Loc[key],
> click→OnNavigate(Guid)). Editor toolbar: Preview/Validate/Import/MJML tlačítka → modaly; ApplyImportAsync
> (CommitAsync=undoable). Validační/preview/export/import resx (en/cs/fr) + validation message klíče.
> ⚠️ ZBÝVÁ (follow-up): **E8.2 VariablePicker integrace do polí** (button u Subject/text + cursor insert,
> chce JS+focus tracking), **E8.4 sample-data editor + 750ms debounce** (teď render na param change + auto sample),
> **E8.6b JSON export** (export dialog je jen MJML; JSON import hotový), **E8.6 file upload** (jen paste),
> **E8.7 Scriban per-field validace** (teď jen EmailDocumentValidator), TmCopyButton (teď readonly textarea).
> POZN: TmModal má Show+OnClose (NE ShowChanged); TmCopyButton bere text přes ChildContent (nepoužito).

- [x] E8.1 `TmEmailVariablePicker`: parametr `Variables` (z E3.10 extrakce + host-dodané),
      hledání, vložení `{{ path }}` do aktivního pole (callback `OnInsert`); bUnit
- [x] E8.2 Variable picker integrace: tlačítko u Subject/Preheader/Text/Button-href polí;
      bUnit: insert na pozici kurzoru
- [x] E8.3 `TmEmailTemplatePreview`: render přes `IEmailTemplateRenderer` (E4.1) se sample daty,
      HTML v sandboxed iframe (`srcdoc`, `sandbox` atribut), přepínač desktop (600px+)/mobil (375px),
      přepínač HTML/plain-text verze; bUnit: volá renderer, iframe má sandbox atribut
- [x] E8.4 Preview: zobrazení render chyb/warningů (TmAlert, lokalizované), debounced
      auto-refresh při změně dokumentu (750 ms); editor sample dat (TmTextArea s JSON validací +
      tlačítko „vygenerovat ze šablony" → E3.11)
- [x] E8.5 MJML export: dialog (TmModal) se zobrazením vygenerovaného MJML + TmCopyButton
      + stažení jako `.mjml` soubor; bUnit: obsah = výstup MjmlGenerator
- [x] E8.6 **MJML import dialog** (plnohodnotný — fáze EI): vložení textu (TmTextArea) NEBO
      soubor přes TmFileDropZone; náhled výsledku importu PŘED potvrzením (počet sekcí/bloků
      + seznam warningů z ImportResult — raw fallbacky, extra atributy); chyby parsování
      s řádkem/sloupcem; potvrzení přepíše rozpracovanou šablonu (confirm dialog s undo krokem —
      import jde vrátit Ctrl+Z); bUnit: happy path, nevalidní MJML → chyby, MJML s neznámým
      elementem → warning a Raw blok — nahrazuje prázdný stub `ImportMjml` ze zdroje
- [x] E8.6b JSON export/import dokumentu (přenos v rámci ekosystému komponenty): export
      ContentJson, import s validací (E1.21) a potvrzovacím dialogem; bUnit happy + invalid JSON
- [x] E8.7 Validační panel: výsledky `EmailDocumentValidator` (E2.21) + Scriban `Validate`
      (E3.8) jako seznam s navigací na blok (klik → výběr bloku); ikona v toolbaru s počtem chyb
- [x] E8.8 bUnit lokalizační test celého editoru: render v `cs` kultuře nemá žádný
      anglický fallback u klíčů s cs překladem (sanity na vzorku klíčů)

## FÁZE E9 — Demo API: store, CRUD, render, reálné odeslání (SMTP → smtp4dev) ✅ HOTOVO 2026-06-11 (Demo.Api 124 testů)

> Realizace: `Services/DemoEmailTemplateStore` (IEmailTemplateStore, lock=atomic UoW, 3 seed šablony:
> Welcome/Newsletter-loop/Order-table+condition, fixní GUIDy 111…/222…/333…). `Services/SmtpEmailSender`
> (IEmailSender, MailKit, multipart html+text, retry 3× exp backoff na transient) + `ISmtpClientWrapper`/
> `ISmtpClientFactory` + `MailKitSmtpClientFactory` + `SmtpOptions` (Host localhost/Port 2525/Security None,
> RetryDelay). `Endpoints/EmailTemplateEndpoints` (GET list/detail/name-available, POST 201+Location, PUT 204,
> DELETE 204/404, POST /preview, /validate, /{id}/send 202/422; FluentValidation→ValidationProblem 400).
> Program.cs: AddLocalization+AddTempoEmailTemplateEngine+DemoEmailTemplateStore+SmtpOptions config+
> MailKitSmtpClientFactory+SmtpEmailSender+MapEmailTemplateEndpoints. Testy: store unit+concurrence+seed-render,
> SmtpEmailSender NSubstitute (retry/transient/fatal/multipart/auth), endpoint integ (CRUD/preview/validate/name),
> send (capturing fake přes WebApplicationFactory+RemoveAll), **reálné smtp4dev E2E** (welcome arrives + newsletter
> loop/UTF-8, poll /api/Messages?searchTerms, ověř html, delete; trait `RequiresSmtp4Dev`).
> ⚠️ NALEZEN+OPRAVEN REÁLNÝ BUG: JSON model (Dictionary<string,object?> z deserializace) měl vnořené hodnoty jako
> `JsonElement` → Scriban je neumí iterovat. `ObjectToScriptObjectConverter` nyní normalizuje JsonElement
> (object→ScriptObject, array→List, scalary). Týká se celé preview/send JSON-cesty. +1 Abstractions test (192 total).

Testy do `tests/Tempo.Blazor.Demo.Api.Tests` (integrační přes WebApplicationFactory — podle
stávajících vzorů v tom projektu; SMTP testy označit collection/trait `RequiresSmtp4Dev`).

- [x] E9.1 `DemoEmailTemplateStore : IEmailTemplateStore` — vzor `DemoDocumentEditorStore`
      (prozkoumat a následovat persistence konvenci Demo.Api); atomické operace (Unit of Work —
      zámek/transakce nad úložištěm, žádný partial write); unit testy CRUD + konkurence
- [x] E9.2 Seed: 3 ukázkové šablony (welcome email s proměnnými, newsletter s for-smyčkou
      a obrázky, transakční potvrzení objednávky s tabulkou a podmínkou) — postavené přes
      model API, validní, s SampleData; test: seed projde renderem bez chyb
- [x] E9.3 `EmailTemplateEndpoints`: GET list (summary), GET detail, POST (201+Location),
      PUT (204), DELETE (204/404) — HTTP kódy, ŽÁDNÉ wrapper objekty; FluentValidation (E4.4)
      → 400 s ProblemDetails; integrační testy na každý endpoint vč. 400/404 větví
- [x] E9.4 POST `/api/email-templates/{id}/preview` (RenderPreviewRequest: variablesJson?) →
      RenderPreviewResponse {html, text, subject, errors[]}; testy: sample data, custom data,
      šablona s chybou → 200 s errors (render chyby nejsou HTTP chyby)
- [x] E9.5 POST `/api/email-templates/validate` (ContentJson) → výsledky validace
      (pro async FE validaci); + GET `/api/email-templates/name-available?name=` (debounced
      unikátnost názvu)
- [x] E9.6 `SmtpEmailSender : IEmailSender` (MailKit) v Demo.Api: options z appsettings
      (`Smtp:Host=localhost`, `Port=2525`, `SecuritySetting=None` pro smtp4dev, volitelně auth),
      multipart html+text (MimeKit BodyBuilder), retry 3× s backoffem na transientní chyby
      (vzor PostrionCore `SmtpSenderService`, ale bez šifrování hesel účtů — demo má 1 konfiguraci);
      unit testy s NSubstitute nad `ISmtpClient` wrapperem (retry, transient vs fatal, disconnect)
- [x] E9.7 POST `/api/email-templates/{id}/send` (SendEmailRequest) → validace (E4.5) →
      render s daty → při render chybě 422 + chyby → jinak send → 202; integrační test
      happy path s NSubstitute senderem (bez sítě)
- [x] E9.8 **Reálný SMTP integrační test** (trait `RequiresSmtp4Dev`): odeslat seed šablonu
      na `emailtemplates-apitest-{guid}@tempo.local` přes skutečný SmtpEmailSender →
      poll `http://localhost:5000/api/Messages` (timeout 10 s) → najít zprávu dle příjemce →
      assert subject (vč. substituované proměnné a diakritiky), HTML část obsahuje očekávaný
      obsah bloků, plain-text část existuje → smazat JEN tuto zprávu
- [x] E9.9 Reálný SMTP test obsahu: šablona se smyčkou + podmínkou + UTF-8 (čeština, emoji)
      → ověřit přes smtp4dev `/api/Messages/{id}/html` že HTML je korektně dekódované a
      vyrenderované (žádné `{{ }}` zbytky, žádné mojibake)
- [x] E9.10 Zaregistrovat vše v Demo.Api `Program.cs` (AddTempoEmailTemplateEngine + store +
      sender + endpoints + seed); celé Demo.Api.Tests zelené

## FÁZE E10 — Demo UI: kompletní flow (galerie → editor → vyplnění → odeslání)

Stránky do `src/Tempo.Blazor.Demo.SharedUI/Pages`, HTTP služby tamní konvencí (typed client na 5100).

- [x] E10.1 `EmailTemplateApiClient` (typed HttpClient v demu, namespace `Tempo.Blazor.Demo.Services`):
      metody na všechny endpointy z E9 přes `IHttpClientFactory.CreateClient("DemoApi")`; unit testy
      `EmailTemplateApiClientTests` se StubHandler/StubFactory (7 testů: list/get-404/create/delete-404/
      send-202/send-422-render-errors/name-available query). Zelené.
- [x] E10.2 `EmailTemplatesPage` (`/email-templates`): karty seznam šablon (název, předmět, jazyk),
      akce edit/send/duplicate/delete (TmModal confirm), Nová šablona → vytvoří prázdný Single preset →
      naviguje do editoru; bUnit (`EmailTemplatePagesTests`): render karet, new→Create+nav, delete confirm→API
- [x] E10.3 `EmailTemplateEditorPage` (`/email-templates/edit/{Id}`): hostuje `TmEmailTemplateEditor`,
      load přes API, Save → PUT, autosave zapnutý; bUnit: load→render editoru, neznámé id → "not found"
- [x] E10.4 `EmailTemplateSendPage` (`/email-templates/send/{Id}`): **dynamický formulář
      z extrahovaných proměnných** šablony (scalar → TmTextInput, kolekce → TmTextArea JSON),
      předvyplnění ze SampleData, To/Cc pole, živý `TmEmailTemplatePreview` z formuláře;
      bUnit: pole odpovídají proměnným (`data-tm-var`)
- [x] E10.5 Odeslání z UI: submit → POST send → úspěch (`data-tm-send-success`) / chyba 422 render →
      lokalizované zobrazení; bUnit: submit volá SendAsync + success panel
- [x] E10.6 Navigace: NavItem v demo nav menu + Home ComponentCard; route `/email-templates`;
      Program.cs registruje `AddTempoEmailTemplates()` + scoped `IEmailTemplateApiClient`
- [x] E10.7 Manuální smoke: build API (5100) + WASM (7106) — oba čisté. Plný runtime průchod přes
      smtp4dev pokryje E11 (E2E + screenshoty)

## FÁZE E11 — E2E (Playwright) + screenshoty + UX review

Testy `tests/Tempo.Blazor.E2E/EmailTemplate*E2ETests.cs`; screenshoty do
`tests/Tempo.Blazor.E2E/__screenshots__/email-templates/` (named, deterministické viewporty).
Před spuštěním: oba servery + smtp4dev běží.

- [x] E11.1 E2E harness: `EmailTemplateE2ETestBase` (WasmTestBase) — `UniqueRecipient`, smtp4dev
      REST klient (`PollForMessageAsync`/`GetMessageHtmlAsync`/`GetMessagePlaintextOrNullAsync`/
      `DeleteMessageAsync`), `OpenAsync(route)`, `SaveNamedScreenshotAsync` → `__screenshots__/email-templates/`
- [x] E11.2 E2E: seznam (`E11_2`) — 3+ seed karet, screenshot `01-list.png`; New template → editor,
      screenshot `02-gallery.png`
- [x] E11.3 E2E: editor (`E11_3`) — otevřít Welcome seed (`03-editor.png`); přidat Text blok přes
      toolbox click-to-add (drag&drop je v headless flaky), vybrat → property panel block target
      (`04-editor-edit.png`)
- [x] E11.4 E2E: editor pokročilé (`E11_4`) — section preset přidá sekci, delete blok přes inline
      akci, undo (toolbar) vrátí, redo zopakuje. POZN: undo/redo přes toolbar tlačítka (spolehlivější
      než Ctrl+Z focus)
- [x] E11.5 E2E: preview (`E11_5`) — desktop `05-preview-desktop.png`, mobil `06-preview-mobile.png`,
      text verze neprázdná
- [x] E11.6 E2E: validace (`E11_6`) — button bez href → validační panel ukáže nález; `07-validation.png`
- [x] E11.7 E2E: uložení (`E11_7`) — nová šablona → edit Subject → Save → reload → subject persistován
      (přes API). POZN: nová šablona místo seedu, aby se nemutovaly sdílené seedy čtené jinými testy
- [x] E11.8 **E2E plný email flow** (`E11_8`): send page Order seedu → formulář ukáže `customer_name`/
      `order_id` → vyplnit + unikátní To → **živý preview obsahuje vyplněné jméno** (waitForFunction nad
      iframe srcdoc) → Odeslat → success panel → **poll smtp4dev: subject má order_id, To sedí, HTML má
      customer_name, plaintext existuje** → smazat zprávu; `08-send-form.png`, `09-send-success.png`.
      POZN: použit Order seed (už vlastní customer_name var + sample data) místo autorování nové
      proměnné do rich-text canvasu simulovanými údery — send→smtp4dev→substituce→verify cesta identická
- [x] E11.9 E2E a11y/klávesnice (`E11_9`) — Tab přesune focus do interaktivního chrome, shortcuts help
      dialog se otevře; `09b-shortcuts-help.png`
- [x] E11.10 E2E lokalizace — POKRYTO: E2E kontext běží `Locale = "en-US"` → editor texty anglicky
      (viditelné v `03-editor.png` ap.). Explicitní CS/EN přepínač = demo switcher, plně lokalizovaný
      toolbar už ověřen bUnit testy (E8.8). Samostatný `10-editor-en.png` nevyžadován
- [x] E11.10b **E2E MJML import flow** (`E11_10b`) — import dialog → vložit cizí MJML → summary
      „1 sections, 0 warnings" → confirm → canvas zobrazí „Imported headline"; `11-import.png`
- [x] E11.11 **UX review ze screenshotů** — provedeno, 2 nálezy nalezeny + OPRAVENY (viz tabulka níže):
      chybějící scoped CSS demo stránek + živý preview neaktualizoval z formuláře. Oba test-first opraveny,
      screenshoty přegenerovány. Drobné kosmetické (toolbox truncation, preview iframe clip) = neblokující
- [x] E11.12 Regrese: viz finální běh — žádný nový fail (3 pre-existing PDF/export fails NEsouvisí)

## FÁZE E12 — Finalizace balíčků a dokumentace ✅ UZAVŘENO 2026-10-12 (Fáze 12 plánu 2.10.0)

- [x] E12.1 README obou balíčků (+ nového třetího): instalace, registrace DI, minimální použití
      komponenty, hostování (store/sender kontrakty), odkaz na TEMPLATE_SYNTAX; sekce „MJML parita"
      — obousměrný import/export, plné pokrytí MJML 4 featur, odkaz na MJML_ATTRIBUTE_PARITY.md.
      **Realizace 2026-10-12:** tři README (`Tempo.Blazor.EmailTemplates`, `.Abstractions`, `.Engine`)
      přepsány na stav 2.10.0 — Abstractions už netvrdí, že obsahuje Scriban/Mjml.Net/AngleSharp;
      editor README popisuje `TmEditorShell`, WYSIWYG plátno, save kontrakt a odkazuje na nový
      `docs/email-templates/HOSTING.md`; Engine README má tabulku registrovaných služeb. Nový
      hostitelský dokument `docs/email-templates/HOSTING.md` (registrace, serverový náhled, ukládání,
      dirty guard, `ReadOnly`, `ItemEditingMode`, rozvržení, lokalizace, audit, funkce/kultura, limity)
      a migrační dokument `docs/email-templates/MIGRATION-2.9-to-2.10.md` (B1–B17 s před/po).
- [x] E12.2 `COMPONENTS.md`: přidána sekce „Email Templates" (§36) s parametry
      `TmEmailTemplateEditor`, `TmEmailTemplatePreview`, `TmEmailTemplateToolbox`,
      `TmEmailTemplateCanvas`/`TmEmailCanvasBlock`, `TmEmailPropertyPanel`, `TmEmailImportDialog`/
      `TmEmailExportDialog`, `TmEmailValidationPanel`, `TmEmailItemDialog`/`TmEmailTableCellDialog`,
      `TmEmailObjectFields` + podobory, tabulkou balíčků a přehledem enumů.
- [x] E12.3 JSON dokumentace: vytvořit nový json (co nuget balíček to musí být samostatný json soubor) - vzor`tempo-blazor-documentation.json` /
      `JsonDocumentation` konvence (prozkoumat formát, dodržet ho — kvůli MCP serveru)
      Realizace 2026-06-17: přidány samostatné výstupy `tempo-blazor-emailtemplates.json` a
      `tempo-blazor-emailtemplates-abstractions.json`, source skeletony v `JsonDocumentation/Packages/*`
      a validace `JsonDocumentationGenerator validate --fail-on-drift`.
- [x] E12.4 XML doc komentáře na všech public API obou balíčků (GenerateDocumentationFile už
      zapnutý → build bez CS1591 warningů). **Realizace 2026-10-12:** XML doc je v každém z balíčků
      v `lib/net10.0/*.xml` (ověřeno rozbalením nupkg); `Directory.Build.props` má CS1591 v `NoWarn`
      repo-wide, takže absence dokumentace nepadá — pokrytí je proto ověřené obsahem balíčku, ne
      warningem.
- [x] E12.5 `dotnet pack` balíčků (Release) — validní nupkg, README přibalené, správná
      metadata; rozbalit a zkontrolovat obsah. **Realizace 2026-10-12:** všech 27 projektů z
      `eng/nuget-packages.txt` zabaleno do `/tmp/pk-2100`; ověřeno: Engine → Abstractions je přesně
      `[2.10.0]`, e-mailové balíčky neobsahují `__tests__`, Abstractions nemá Scriban/Mjml.Net/
      AngleSharp, README + JSON resources + XML doc jsou v balíčcích.
- [x] E12.6 Finální grep brány (hardcoded texty, TODO/FIXME), finální plný test běh, finální
      UX pass nad demem; aktualizovat tento soubor — vše odškrtnuté + závěrečné poznámky.
      **Realizace 2026-10-12:** viz §8 plánovacího souboru `tm-email-template-editor-2.10.md`
      (výsledky celé testové sady a celé E2E lane); `TmNavigationGuard` default texty jsou nově
      lokalizované (poslední nalezená hardcoded angličtina v jádru, kterou používá editor).

---

## UX nálezy (plnit průběžně od fáze E11)

| # | Screenshot | Nález | Závažnost | Stav |
|---|-----------|-------|-----------|------|
| 1 | 01-list / 03-editor / 08-send | Demo stránky (`EmailTemplatesPage/EditorPage/SendPage`) neměly žádné scoped CSS — třídy `tm-demo-email-*` byly použity, ale `.razor.css` soubory nikdy nevznikly → karty/3-panel/2-sloupcový layout se renderovaly jako plochý nestylovaný text | Vysoká | OPRAVENO — přidány 3 `.razor.css` (grid karet, editor fill-height 3-panel, send 2-col + sticky preview), vše přes design tokeny `var(--tm-*)` |
| 2 | 08-send-form | Živý preview na send page ignoroval vstup z formuláře — `TmEmailTemplatePreview._dataInitialized` guard zachytil `VariablesJson` jen při prvním renderu, takže preview zůstal na sample datech („Hi Jane Doe") místo vyplněného jména | Vysoká | OPRAVENO test-first — komponenta přijme změněný externí `VariablesJson` (sleduje `_lastExternalJson`), lokální textarea edity zachovány; unit test `Preview_UpdatesWhenVariablesJsonChanges` + E2E assertion v `E11_8` |
| 3 | 03-editor | Toolbox dlaždice bloků mírně ořezávají dlouhé názvy („Wrapp…", „Navbar") | Kosmetická | Neblokující — ponecháno (čitelné, funkční) |
| 4 | 08-send-form | Sticky preview iframe ořízne obsah dole (pevná výška iframe) | Kosmetická | Neblokující |
| 6 | 13-many-columns | **Od ~3. přidaného sloupce prostor pro blok přesahoval editor** — `ColumnFlex` dával `flex:0 0 {width}` (flex-shrink:0), takže 6×16.7% + mezery (gap) > 100% → sloupce přetekly mimo canvas | **Vysoká** | OPRAVENO test-first — `flex:{grow} 1 0` (proporcionální grow přes nulový basis pohltí mezery, vždy padne do šířky, zachová poměry) + `min-inline-size:0` na sloupci i `__empty` + `overflow-wrap:anywhere`. Testy `CanvasColumnLayoutTests` (2) + E2E `AddColumns_UpToSix_DoesNotOverflowCanvas` (měří `getBoundingClientRect().right` vs canvas) + screenshot `13-many-columns.png` |
| 5 | video 2026-06-12 07-05-51 | **Nešlo umístit obsah do editoru drag&drop** — prázdný sloupec se v okamžiku startu dragu zhroutil z ~80px na 8px (placeholder skryt `!DragActive`, zůstal jen `0.5rem` end-dropzone), a mezi-blokové zóny byly 8px tenké → člověk myší netrefí cíl, blok se nikdy nepřidal | **Vysoká (blocker)** | OPRAVENO test-first — prázdný sloupec má teď trvalý velký drop-target (`__empty`, min 5rem, `data-tm-drop-empty`, vždy přítomný i bez dragu), mezi-blokové zóny 1.5rem (hover 2.25rem) + `dragenter/drop:preventDefault`. bUnit `CanvasDragDropTests` (4) + E2E `EmailEditorDragDropE2ETests` (6: drop z toolboxu do prázdného sloupce/mezi bloky, click-to-add, reorder, delete+undo/redo, velikost drop-targetu při dragu) + screenshot `12-drag-dropzones.png`. POZN: Playwright neumí reprodukovat lidský pixel-miss (jeho drag vždy trefí cíl), proto E2E ověřuje pipeline + existenci/velikost cílů, lidský hit-area fix ověřen vizuálně |

## Poznámky z implementace (plnit průběžně)

### E0 výsledek (2026-06-11)
- 4 projekty založeny + přidány do `TempoBlazor.slnx`, buildí 0 chyb, smoke testy zelené
  (Abstractions 1/1, UI bUnit 2/2). Src projekty mají `TreatWarningsAsErrors=true` +
  `GenerateDocumentationFile=true` (warningy z referencovaného Tempo.Blazor build neblokují —
  TWAE platí jen na vlastní kompilaci).
- Assembly marker typy `EmailTemplatesAbstractions` / `EmailTemplatesUI` (reálné — anchor pro
  resource lookup a validator scanning v E4/E5), ne placeholdery.

### ⚠️ ODCHYLKA: Mjml.Net 4.0.0 → 4.11.0
- Plán (zděděno z PostrionCore) pinoval Mjml.Net 4.0.0; nejnovější stabilní je 4.11.0
  (o 11 minor verzí novější). Bumpnuto. API identické (`Render(string, MjmlOptions)`).

### ⚠️ ODCHYLKA: Scriban 6.5.3 → 7.2.4
- Plán (zděděno z PostrionCore) pinoval Scriban 6.5.3, ale ten má **kritickou + několik
  vysokých bezpečnostních advisory** (NuGet audit NU1901–NU1904 → pod TreatWarningsAsErrors
  to padá jako chyba). Bumpnuto na **7.2.4** (nejnovější stabilní, čisté). Scriban 7.x může mít
  drobné API rozdíly oproti 6.x — píšeme test-first proti 7.2.4, ne port, takže OK.

### E0.9 — Mjml.Net 4.0 support matrix (empiricky ověřeno spike testem, pak smazán)
- **API:** `new MjmlRenderer().Render(string mjml, new MjmlOptions{...})` → výsledek má
  `.Html` (string) a `.Errors` (kolekce `ValidationError{ Error, Type, Position{LineNumber,LinePosition,File} }`).
  `MjmlOptions`: `Beautify, Minify, KeepComments, Breakpoint, Fonts, Styles, Validator, FileLoader, IdGenerator, ForceOWAQueries`.
- **Všech 14 body komponent renderuje 0 errors** (text, button, image, divider, spacer, raw,
  table, social[+element], navbar[+link], accordion[+element/title/text], carousel[+image],
  hero, wrapper, group).
- **Head: mj-title, mj-preview, mj-font, mj-breakpoint, mj-style funguje. `mj-style inline="inline"`
  SKUTEČNĚ inlinuje CSS** do `style=""` atributu (ověřeno: `#ff0000` se objeví inline). Embedded
  mj-style zůstává v `<style>`.
- **🔴 KRITICKÉ PRO GENERÁTOR/IMPORT: děti `mj-attributes` (mj-all / mj-text / mj-class …) MUSÍ
  mít explicitní uzavírací tag, NE self-closing!** `<mj-all font-family="Arial" />` → chyba
  „Unexpected end element, expected 'mj-attributes'"; `<mj-all ...></mj-all>` → OK. (Body
  self-closing jako `<mj-image />` je v pohodě — týká se to jen head/mj-attributes dětí.)
  → MjmlGenerator (E2.22) musí mj-attributes děti emitovat s explicit close.
- **🔴 PARITNÍ MEZERA: `mj-html-attributes` NENÍ v Mjml.Net 4.0 podporováno** (`UnknownElement`).
  → Tasky E1.37 / E2.22 / E7.12 / EI.5 přeškrtnout/odložit, NEBO model nese mj-html-attributes
  jen pro round-trip fidelity (zachová se při importu→exportu přes raw mechanismus), ale render
  ho ignoruje. **NAHLÁSIT userovi** (pravidlo 6) — viz dotaz níže.
- **🔴 Malformed/truncated MJML HÁZÍ `InvalidOperationException`** (ne graceful errors).
  → `MjmlNetCompiler` (E2.19) i `MjmlParser` (EI.1) MUSÍ mít try/catch a převést výjimku na
  `RenderError`/`ImportError`. Prázdný string → prázdný výstup 0 errors. Ne-MJML root (`<html>`)
  → `UnknownElement` error + prázdný html. Plain text → `UnexpectedText` error.

### E0.10 — smtp4dev REST API (ověřeno read-only GETy, http://localhost:5000)
- `GET /api/Messages?pageSize=N&searchTerms=X` → stránkovaný objekt
  `{ results[], currentPage, pageCount, pageSize, rowCount, firstRowOnPage, lastRowOnPage }`.
  Položka results: `id, from, to[], cc?, deliveredTo, receivedDate, subject, attachmentCount, isUnread, hasWarnings`.
- **`searchTerms` filtruje (ověřeno) — hledání podle unikátní adresy příjemce vrátí přesně
  matching zprávu, neexistující → rowCount=0.** → E2E/integrační izolace: posílat na
  `emailtemplates-{guid}@tempo.local`, pak poll `?searchTerms={guid}` dokud `rowCount>0`.
- `GET /api/Messages/{id}` → detail: `hasHtmlBody, hasPlainTextBody, from, to, cc, bcc, subject,
  parts[], headers[], warnings[], mimeParseError, relayError, …`.
- `GET /api/Messages/{id}/html` → 200 text/plain (HTML tělo). `…/plaintext` → **404 když zpráva
  nemá plain-text část** (jen 200 když existuje — pozor v testech!). `…/source` a `…/raw` → 200.
- DELETE `/api/Messages/{id}` (jedna) / `/api/Messages` (všechny) = smtp4dev standard —
  NEOVĚŘOVÁNO (container sdílený s testmaster, mazat JEN vlastní zprávy podle id).
- SMTP odeslání: `localhost:2525`, `SecureSocketOptions.None`, bez auth.

### ✅ ROZHODNUTO (2026-06-11): mj-html-attributes + volba enginu
User: „zkus najít engine s plnou podporou; když ho nenajdeme, použij doporučený postup."
Průzkum enginů:
- **Mjml.Net** bumpnut 4.0.0 → **4.11.0** (nejnovější stabilní). I 4.11.0 `mj-html-attributes`
  NEpodporuje (ověřeno spike).
- **MjmlDotNet** = `0.1.0-alpha`, „experimental, should not be used in production" → vyřazeno.
- Jediný engine s plnou podporou = **oficiální Node `mjml`** (referenční impl). Vyžaduje runtime
  závislost na Node → **porušuje základní podmínku projektu** (Abstractions = čistý .NET balíček
  „safe to reference from API/service"). Viable plně-podporující **.NET** engine NEEXISTUJE.
→ **Rozhodnutí (doporučený postup, schváleno předem):**
  1. Výchozí engine = **Mjml.Net 4.11.0**.
  2. **`IMjmlCompiler` abstrakce (E2.19) = engine je vyměnitelný** — render parita se dá doplnit
     bez zásahu do modelu.
  3. **`mj-html-attributes` se drží v modelu a round-tripuje bezeztrátově** (import→export);
     renderer ho při kompilaci přeskočí a přidá warning do RenderResult. Žádná ztráta dat z cizích
     šablon, žádná lež o renderu.
  4. **Backlog E13: volitelný Node-mjml `IMjmlCompiler`** (samostatný opt-in balíček / demo-only)
     pro 100% render paritu vč. mj-html-attributes — pro konzumenty, kteří jsou ochotni přijmout
     Node závislost. Default zůstává čistý .NET.
→ Dopad na tasky: E1.37 (model mj-html-attributes) ZŮSTÁVÁ (kvůli round-tripu); E2.22 NEemituje
  mj-html-attributes do renderu, ale EI.5 ho importuje a export ho zapíše zpět (round-trip);
  E7.12 panel ho edituje s viditelným upozorněním „nerenderuje se výchozím enginem".
