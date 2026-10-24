# Tempo.Blazor 2.10.0 — produkční e-mailový editor a engine: inventura mezer a kontrakt hostitele

Dokument fáze 1 plánu **`da0a2531-780c-4588-8c31-08df2362aa32`** („Tempo.Blazor 2.10.0 — produkční e-mailový
editor a engine pro PostrionCore“), slug fáze `fze-1-vchoz-stav-verze-a-kontrakt-hostitele`, GUID
`a15f88f6-77bb-4e39-7ebd-08df235dd165`.

| | |
|---|---|
| Větev / HEAD | `redesign2` / `11ae3552` (`Merge remote-tracking branch 'origin/main' into redesign2`) |
| Výchozí tag | `v2.9.0` → commit `05fcb4f9` (annotated tag object `8b2427c4`), `HEAD` = `v2.9.0-567-g11ae3552` |
| Balíčky | `Tempo.Blazor.EmailTemplates` (verze v csproj `2.9.1`), `Tempo.Blazor.EmailTemplates.Abstractions` (verze `2.9.1`), oba `net10.0` |
| Rozsah fáze | **jen dokumentace** — žádná změna produkčního kódu, žádné nové testy |
| Podklady | `output/imagegen/tempo-reimagined/analysis/emailtemplates.md`, `…/DECISIONS.md`, `…/_email-plan-da0a2531-current.md`, `planning/tm-email-template-editor-todo-2026-06-11.md` (E0–E11 hotové, E12.1/2/4/5/6 otevřené) |

> **Závaznost:** sekce [3 — Kontrakt pro hostitele](#3-kontrakt-pro-hostitele) je kontrakt, proti kterému plánuje
> PostrionCore P06. Jakákoli pozdější odchylka v názvech/typech veřejného API se **konzultuje s uživatelem**
> a zapisuje jako decision — nikdy tichá změna.

> **Číslování fází** odpovídá MCP plánu (15 fází, včetně `Fáze 7A` a `Fáze 9A`). Původní „Fáze 10: Import a
> export dialogy“ je v plánu `Skipped` a nahradila ji „Fáze 10: Import, export a validační dialog“ — odkazy
> „Fáze 10“ v tomto dokumentu míří na novou (přejmenovanou) fázi.

> **Stav kontraktu: POTVRZEN uživatelem 2026-10-20** (rozhodnutí D1–D20, viz [5](#5-stav-potvrzení)).
> Review 2026-10-20 doplnil do kontraktu nový balíček `Tempo.Blazor.EmailTemplates.Engine`, kontrolu URL
> schémat, audit `BlockedUrlScheme`, single-flight ukládání a další — vše zapracováno v [3](#3-kontrakt-pro-hostitele).

---

## 1. Baseline (Fáze 1)

Spuštěno po projektech (nikdy celá solution paralelně — OOM), `net10.0`, Debug, pracovní strom na `11ae3552`.

| Projekt | Výsledek | Trvání |
|---|---|---|
| `dotnet test tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests` | **Failed: 0, Passed: 192, Skipped: 0, Total: 192** | 1 s |
| `dotnet test tests/Tempo.Blazor.EmailTemplates.Tests` | **Failed: 0, Passed: 103, Skipped: 0, Total: 103** | 2 s |

`git tag --sort=-v:refname | head -3` → `v2.9.0`, `v2.8.26`, `v2.8.25`.

Poznámky k baseline:

- Počty jsou měřené na **pracovním stromě HEAD**, ne na tagu `v2.9.0`; větev `redesign2` je 567 commitů před tagem.
- Verze v obou `.csproj` je `2.9.1` (ne `2.9.0`) — sjednocení na cílovou verzi je úkol Fáze 12.
- Oba balíčky cílí **jen `net10.0`** (jeden `TargetFramework`, žádný multitarget) — konzument (PostrionCore) musí být na `net10.0`.
- Untracked v stromu (`output/`, `eng/release-evidence/full-run-20261012/`) jsou nesouvisející uživatelské změny — tento plán na ně nesahá a necommituje je.
- Fáze 1 nemění žádný soubor v `src/`, `tests/` ani CSS/JSON; jediným výstupem je tento dokument.

---

## 2. Inventura mezer, které tento plán zavírá

Odkazy na řádky jsou **ověřené proti pracovnímu stromu na `11ae3552`** (plán mohl být psán proti starší verzi).
Čísla jsou 1-based, `soubor:od-do`.

### 2.1 Ukládání, stav a autorizace editoru

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.1.1 | `OnSave` je `EventCallback` **bez výsledku** — host nemůže vrátit selhání/konflikt a editor se to nedozví | `TmEmailTemplateEditor.razor:88` (`[Parameter] public EventCallback<EmailTemplateDocument> OnSave`), `:149` (`private Task SaveAsync() => OnSave.InvokeAsync(_document)`) | Fáze 7 |
| 2.1.2 | Autosave hlásí **„Uloženo“ vždy**, když host nevyhodí výjimku — i když zápis neproběhl | `TmEmailTemplateEditor.razor:356-370`, konkrétně `:362-363` (`await OnAutoSave.InvokeAsync(_document); _autoSaveStatus = Loc["EmailEditor_AutoSave_Saved"];`); chybová větev jen `catch` `:365-368` | Fáze 7 |
| 2.1.3 | Stav uložení je holý `<span>` bez `aria-live`, bez ikony/odznaku, bez relativního času | `TmEmailTemplateEditor.razor:31` | Fáze 7 |
| 2.1.4 | Editor **nemá `ReadOnly`, `IsDirty`, `IsSaving`** ani `SaveAsync()` pro hostitele | celý `@code` blok `TmEmailTemplateEditor.razor:80-374` — grep na `ReadOnly`/`IsDirty`/`IsSaving` v souboru nic nenajde (jediný výskyt „ReadOnly“ je `IReadOnlyList` na `:100`) | Fáze 7 |
| 2.1.5 | **Žádné `Ctrl+S`/`Cmd+S`** — obsluha kláves zná jen Z/Y/Shift+Z/C/X/V | `TmEmailTemplateEditor.razor:287-299` (`HandleEditorKeyDownAsync`, `switch` na `"z"/"y"/"c"/"x"/"v"`, žádné `"s"`) | Fáze 7 |
| 2.1.6 | Žádné sloty pro akce hostitele (`ToolbarStart`/`ToolbarEnd`), žádné `ShowSaveButton`/`ShowSaveStatus` | `TmEmailTemplateEditor.razor:6-32` (pevná sestava tlačítek) | Fáze 7 |
| 2.1.7 | Upload obrázku bez limitů (`Accept`/`MaxBytes`): hostitelský `OnImageUpload` dostane soubor bez validace | `TmEmailTemplateEditor.razor:103`, `TmEmailPropertyPanel.razor:146`, volání `:168-173` | Fáze 7 |
| 2.1.8 | Náhled/validace/export se počítají při každém renderu rodiče | `TmEmailExportDialog.razor:38-43`, `TmEmailValidationPanel.razor:35-49` | Fáze 10 |

### 2.2 Náhled a render

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.2.1 | `TmEmailTemplatePreview` renderuje **výhradně lokálně** přes `IEmailTemplateRenderer` (Mjml.Net v prohlížeči) | `TmEmailTemplatePreview.razor:3` (`@inject IEmailTemplateRenderer Renderer`), `:99-105` (`RenderPreviewAsync`), `:107-121` (`BuildModel`) | Fáze 6 |
| 2.2.2 | Žádný vyměnitelný renderer → serverový render (decision 3 zadavatele) nejde | v `src/` neexistuje `IEmailPreviewRenderer` ani `EmailPreviewRequest/Result` (grep = 0) | Fáze 6 |
| 2.2.3 | Zařízení náhledu je `bool _mobile` (jen Desktop/Mobile), přepínač nemá ikony ani `aria-pressed` | `TmEmailTemplatePreview.razor:8-11`, `:73` (`private bool _mobile`), `:153-157` | Fáze 6 |
| 2.2.4 | Ukázková data v holé `<textarea>` uvnitř `<details>`; žádný `VariablesJsonChanged` pro hostitele | `TmEmailTemplatePreview.razor:33-39` (`:37` textarea) | Fáze 6 |
| 2.2.5 | Bez stavů loading/unavailable/retry, bez rušení předchozího renderu (CancellationToken) | `TmEmailTemplatePreview.razor:77-97`, `:142-148` (debounce 750 ms `:64`) | Fáze 6 |
| 2.2.6 | Chybí slot patičky náhledu („Obnovit náhled“, hostitelské „Odeslat test“) a `ShowViewToggle` | `TmEmailTemplatePreview.razor:6-19` (dvě řady `TmButton`) | Fáze 6 |

### 2.3 Responzivita a rozvržení

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.3.1 | Editor **nemá žádné layoutové `@media`/`@container` pravidlo** — jediný výskyt `@media` v celém balíčku je `prefers-reduced-motion` (korekce původního znění „žádné `@media`“) | grep `@media\|@container` v `Components/*.css`: `TmEmailTemplateCanvas.razor.css:203` (`prefers-reduced-motion`), editor a toolbox 0. Layoutové pravidlo = 0 výskytů | Fáze 8 |
| 2.3.2 | Rozvržení je jen `TmSplitter` 20/60/20 % s `MinSize` 160/320/200 px → na 390 px se tři panely (min. 680 px) nevejdou | `TmEmailTemplateEditor.razor:34-64` (`:35` 20 %/160 px, `:44` 60 %/320 px, `:54` 20 %/200 px) | Fáze 8 |
| 2.3.3 | Žádný parametr rozvržení (`TmLayoutMode`), žádná mobilní prezentace panelů | `TmEmailTemplateEditor.razor:82-103` (jediné parametry dokumentu/hostitele) | Fáze 8 |
| 2.3.4 | Akce sekce/bloku na plátně jsou **jen hover/`is-selected`** → na dotyku nedosažitelné | `TmEmailTemplateCanvas.razor.css:133-138` (`:hover` + `.is-selected`), skrytí `display: none` `:119-131` | Fáze 8 |
| 2.3.5 | Panel vlastností nemá sbalitelný levý panel ani stav sbalení | `TmEmailTemplateEditor.razor:35-43` (toolbox bez `ToolboxCollapsed`) | Fáze 7A/8 |

### 2.4 Import a export

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.4.1 | Export je jen MJML/JSON v holé `<textarea readonly>` — **bez kopírování a stažení**, bez patičky | `TmEmailExportDialog.razor:11-18` (`:13` JSON, `:17` MJML), `:3` (modal bez `Footer`) | Fáze 10 |
| 2.4.2 | MJML se generuje při **každém** `OnParametersSet` rodiče, ne při otevření/změně záložky | `TmEmailExportDialog.razor:38-43` (`:41`) | Fáze 10 |
| 2.4.3 | ~~MJML generuje `new MjmlGenerator()` místo `ForExport`~~ — **korekce původního znění plánu: tvrzení je nepřesné**, `new MjmlGenerator()` je jen instance generátoru, options jsou správně `MjmlGeneratorOptions.ForExport` | `TmEmailExportDialog.razor:32` (`static readonly MjmlGenerator Generator = new()`), `:41` (`Generator.Generate(Document, MjmlGeneratorOptions.ForExport)`), `MjmlGeneratorOptions.cs:17` | — (oprava inventury) |
| 2.4.4 | Import má **natvrdo 5 MB** limit, žádné `EmailImportOptions` | `TmEmailImportDialog.razor:63` (`file.OpenReadStream(maxAllowedSize: 5 * 1024 * 1024)`) | Fáze 10 |
| 2.4.5 | Import bez patičky (chybí „Zrušit“), varování jen jako počet (nevypíšou se), JSON chyba syrové `ex.Message` | `TmEmailImportDialog.razor:32-33` (potvrzovací tlačítko v těle), `:25-30` (souhrn `:27-29`), `:82-92` (`:90` `_errors.Add(ex.Message)`) | Fáze 10 |
| 2.4.6 | Režimy MJML/JSON jako dvě `TmButton` bez `aria-pressed`, popisky „MJML“/„JSON“ natvrdo (technické zkratky — analýza je akceptuje) | `TmEmailImportDialog.razor:5-10`, `TmEmailExportDialog.razor:5-10` | Fáze 10 |

### 2.5 Editace položek bloků a model buňky tabulky

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.5.1 | Položky bloků (social/navbar/carousel/accordion) a tabulka se editují **jen inline**, žádný dialogový režim | `TmEmailPropertyPanel.razor:51-72` (`:54-55` social, `:57-59` navbar, `:61-63` carousel, `:65-67` accordion, `:69-71` tabulka přes `TmEmailTableEditor`) | Fáze 9 |
| 2.5.2 | `EmailTableCell` **nemá odkaz ani pozadí** (jen `Text`/`Align`/`ColSpan`/`RowSpan`) | `src/Tempo.Blazor.EmailTemplates.Abstractions/Model/Blocks/EmailTableCell.cs:4-17` (`:7`, `:10`, `:13`, `:16`) | Fáze 9 |
| 2.5.3 | Chybí `EmailItemEditingMode` (host nemůže zvolit dialogovou editaci) | `TmEmailTemplateEditor.razor:82-103` — žádný takový parametr | Fáze 9 |

### 2.6 Lokalizace

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.6.1 | Texty jsou v **`.resx`** (en/cs/fr), ne v embedded JSON jako jádro — hostitel, který vymění `IStringLocalizerFactory` (PostrionCore), uvidí klíče | `Resources/TmEmailResources.resx`, `TmEmailResources.cs.resx`, `TmEmailResources.fr.resx` — každý **103** klíčů; `Localization/DefaultTmEmailLocalizer.cs`, `ITmEmailLocalizer.cs:7-14` | Fáze 5 |
| 2.6.2 | `AddTempoEmailTemplates` volá `services.AddLocalization()` — registrace závisí na hostitelské lokalizaci | `ServiceCollectionExtensions.cs:20` (metoda `:17-24`) | Fáze 5 |
| 2.6.3 | Popisky polí panelu vlastností jsou **odvozené z názvu vlastnosti** (`Humanize`) → nelokalizované anglické texty (porušení konvence „no hardcoded text“) | `TmEmailObjectFields.razor:50` (použití), `:94-104` (`Humanize`) | Fáze 7A |
| 2.6.4 | Typy polí jen Text/MultilineText/Bool/Number (žádná barva/zarovnání/enum/délka) | `PropertyReflection.cs:89-96` (`switch` na `underlying`) | Fáze 7A |

### 2.7 Bezpečnost sandboxu

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.7.1 | Sandbox **nemá `MemberFilter`** (reflexe `GetType`/`Assembly` není blokovaná), žádný zákaz jmenných prostorů, žádné odstranění `object.eval`/`eval_template` | `ScribanTemplateEngine.cs:61-72` (`CreateContext`, `:63-70` jen `LoopLimit`/`RecursiveLimit`/`StrictVariables`/`EnableRelaxedMemberAccess`), `:22-42` (`Render`) | Fáze 2 |
| 2.7.2 | Chybí **audit** (`ITemplateSecurityAuditSink`) — v `src/` neexistuje | grep `ITemplateSecurityAuditSink` = 0 | Fáze 2 |
| 2.7.3 | Limity jsou jen 4+1 (`TemplateSecurityOptions.cs:7-26`: `LoopLimit`, `RecursiveLimit`, `MaxOutputLength`, `Timeout`, `StrictVariables`) — chybí `RegexTimeOut`, `ObjectRecursionLimit`, `LimitToString` | `TemplateSecurityOptions.cs:7-26` | Fáze 2 |
| 2.7.4 | **Žádné escapování hodnot proměnných** — `EmailTemplateRenderer` substítuuje do MJML až po sanitizaci šablony, takže `{{ name }}` s `<script>` projde | `EmailTemplateRenderer.cs:32-47` (pipeline `:36-44`), `:49-55` (`Substitute` bez režimu escapování) | Fáze 3 |
| 2.7.5 | Žádná kontrola URL schémat po substituci (`href`/`src`/`background-url`) | `EmailTemplateRenderer.cs:32-47` — po `_compiler.Compile` už jen `errors.AddRange` | Fáze 3 |
| 2.7.6 | Chybí kultura renderu, registr aplikačních funkcí a `SchemaVersion` | `Templating/` — grep `TemplateRenderOptions`, `ITemplateFunctionProvider`, `SchemaVersion` = 0 | Fáze 4 |

### 2.8 JSON dokumentace balíčků

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.8.1 | **Popisy parametrů jsou posunuté o jeden** — párování proběhlo v pořadí deklarace, ale parametry jsou v bundlu řazené abecedně | `tempo-blazor-emailtemplates.json`, `items[6]` (`TmEmailImportDialog`): `OnClose` = „Raised with the imported document when the user confirms.“ (patří `OnImport`), `OnImport` = „Whether the dialog is shown.“ (patří `Show`); `TmEmailExportDialog`: `Document` = „Whether the dialog is shown.“, `OnClose` = „The document to export.“; `TmEmailTemplateEditor`: `OnAutoSave` nese popis `AutoSave`, `TimeProvider` popis `OnAutoSave`, `Variables` popis `OnImageUpload`, `OnSave` popis `DocumentChanged`; `TmEmailTemplatePreview`: `TimeProvider` nese popis `VariablesJson` | Fáze 5 |

### 2.9 Mezery vizuálního redesignu (A/B/C/D z analýzy)

| # | Mezera | Dnes v kódu | Zavírá |
|---|---|---|---|
| 2.9.1 | Hardcoded barevné fallbacky ve `var(--tm-*, #…)` | `TmEmailTemplateCanvas.razor.css` 25×, `TmEmailTemplateEditor.razor.css` 9×, `TmEmailTemplateToolbox.razor.css` 8× (grep `#hex`) | Fáze 7A |
| 2.9.2 | Hlavičky panelů uppercase xs (ne běžná typografie), panely nejsou karty | `TmEmailTemplateEditor.razor.css:49-57` | Fáze 7A |
| 2.9.3 | Validace: `<li @onclick>` není fokusovatelné/klávesnicově ovladatelné (WCAG 2.1.1), nálezy bez titulku/popisu/ikony, žádná úroveň „doporučení“ (`LayoutSeverity` má jen `Warning`/`Error`) | `TmEmailValidationPanel.razor:11-20`, `Abstractions/Layout/LayoutSeverity.cs:4-11` | Fáze 10 |
| 2.9.4 | Plátno je strukturální seznam („typ + 80 znaků“), ne WYSIWYG | `TmEmailTemplateCanvas.razor` (`RenderPreview`; text se krátí na 80 znaků `:368`), celý blok kreslí jen název typu a text | Fáze 9A |
| 2.9.5 | Toolbox bez vyhledávání, dlaždice 2 sloupce s oříznutými popisky | `TmEmailTemplateToolbox.razor`, `.razor.css:17-18` | Fáze 7A |
| 2.9.6 | Proměnné jen ve `<details>` pickeru bez popisu/ukázkové hodnoty; `TemplateVariableInfo` nemá `Description` | `TmEmailPropertyPanel.razor:22-26`, `:105-109`; `Templating/TemplateVariableInfo.cs:6` | Fáze 7A |
| 2.9.7 | Žádné bottom-sheet/`MobilePresentation` chování dialogů editoru | `TmEmailTemplateEditor.razor:69-76` (`TmModal` bez `MobilePresentation`) | Fáze 8/10 |
| 2.9.8 | Demo stránky mají hardcoded anglické texty (`Back`, `Saving…`, `Template not found`) | `EmailTemplateEditorPage.razor:9` („Back“), `:22` („Template not found“), `:54` („Saving…“), `EmailTemplateSendPage.razor:10,20` | Fáze 11 (demo, ne balíček) |

> **Co se NEMĚNÍ (mimo rozsah):** MJML parita (`docs/email-templates/MJML_ATTRIBUTE_PARITY.md`),
> `MjmlImporter` round-trip, DTO/validátory požadavků, `TemplateStyles`, `TextVersionGenerator`,
> `BlockRegistry` (14 typů). Žádná z mezer výše nevyžaduje změnu v jádrovém `Tempo.Blazor` — jádro
> potřebné primitivy už dodalo (viz 3.1).

---

## 3. Kontrakt pro hostitele

Cílová veřejná plocha **2.10.0**, proti které plánuje PostrionCore P06. Každá položka má označení
**Breaking** (ano/ne) vůči stavu `v2.9.0`/`2.9.1`. Sloupec „Fáze“ říká, kde položka vznikne.
**Stav: kontrakt POTVRZEN uživatelem 2026-10-20** (viz [5](#5-stav-potvrzení)).

> **Notace:** bloky `csharp` níže jsou **kontraktní signatury**, ne kompilovatelné soubory — u metod
> popisujících chování nejsou vypsaná těla a každý `namespace` je samostatný budoucí soubor.
> Defaulty `[Parameter]` vlastností odpovídají skutečné deklaraci v komponentě.

### 3.0 Balíčky a závislosti (NOVÉ — rozhodnutí 2026-10-20)

Uživatel rozhodl **oddělit engine do nového balíčku** `Tempo.Blazor.EmailTemplates.Engine`, aby editor
fungoval ve WASM bez Scribanu, Mjml.Net a AngleSharpu. Rozdělení bylo ověřeno **podle skutečných
závislostí v kódu** (`grep using` + `PackageReference`), ne podle názvů souborů.

**Dnešní stav (ověřeno):** `Tempo.Blazor.EmailTemplates.Abstractions.csproj` referencuje
`AngleSharp 1.5.2`, `Mjml.Net 4.11.0`, `Scriban 7.2.4`, `FluentValidation 12.1.1` a
`Microsoft.Extensions.*` 10.0.0. UI balíček `Tempo.Blazor.EmailTemplates` nemá žádnou vlastní
`PackageReference` (jen projektové reference), takže do WASM dnes tahá Scriban/Mjml.Net/AngleSharp
tranzitivně přes Abstractions.

**Skuteční konzumenti externích závislostí (soubor → závislost):**

| Soubor | Závislost |
|---|---|
| `Templating/ScribanTemplateEngine.cs` | Scriban |
| `Templating/ObjectToScriptObjectConverter.cs` | Scriban |
| `Templating/SandboxedScriptOutput.cs` | Scriban |
| `Templating/TemplateVariableExtractor.cs` | Scriban (AST visitor) |
| `Rendering/MjmlNetCompiler.cs` | Mjml.Net |
| `Rendering/HtmlContentSanitizer.cs` | AngleSharp |
| `ServiceCollectionExtensions.cs` (Abstractions) | Scriban i Mjml.Net — dnes `new ScribanTemplateEngine(options)` na `:26` a registrace `MjmlNetCompiler` na `:28`; po rozdělení se celá tahle registrace přesune do Engine |

Nic jiného v Abstractions externí závislost nepotřebuje — `Rendering/MjmlGenerator.cs` **Mjml.Net
nepoužívá** (staví string; `Generate` na `:15`), `Import/MjmlImporter.cs` je čistě `System.Xml` +
`Regex` (Mjml.Net nepotřebuje), `Rendering/TextVersionGenerator.cs` je čistě BCL.

> **Korekce zadání review:** `MjmlImporter` **nepatří** do Engine — je bez externích závislostí, takže
> import dialog funguje ve WASM i bez Engine a **žádný `IEmailImportService` není potřeba**. Do Engine
> jde jen `IMjmlCompiler` implementace (`MjmlNetCompiler`).

**Nová struktura balíčků:**

| Balíček | Závislosti | Co obsahuje |
|---|---|---|
| `Tempo.Blazor.EmailTemplates.Abstractions` | BCL, `System.Text.Json`, `FluentValidation`, `Microsoft.Extensions.*` | model dokumentu, bloky, `EmailTemplateSerializer`, `MjmlGenerator` (+ options, `MjmlEscape`, `MjmlAttributeBuffer`), `TextVersionGenerator`, `MjmlImporter`, registry, layout validátory, DTO/Contracts, **všechna rozhraní** (`ITemplateEngine`, `ITemplateValidator`, `ITemplateVariableExtractor`, `ITemplateFunctionProvider`, `ITemplateSecurityAuditSink`, `IEmailTemplateRenderer`, `IMjmlCompiler`, `IEmailPreviewRenderer`, `IHtmlContentSanitizer`), `Editing/**` (ukládací kontrakt), `HtmlSanitizerPolicy` + `ConservativeHtmlContentSanitizer`, registrace modelu (`AddTempoEmailTemplateModel`) |
| `Tempo.Blazor.EmailTemplates.Engine` (**nový**) | Abstractions + `Scriban`, `Mjml.Net`, `AngleSharp` | `ScribanTemplateEngine`, `ObjectToScriptObjectConverter`, `SandboxedScriptOutput`, `ScribanTemplateVariableExtractor`, `ScribanTemplateValidator`, `MjmlNetCompiler`, `AngleSharpHtmlContentSanitizer`, `EmailTemplateRenderer`, `LocalEmailPreviewRenderer`, `AddTempoEmailTemplateEngine` |
| `Tempo.Blazor.EmailTemplates` (UI) | Abstractions + `Tempo.Blazor` + `FluentValidation` — **bez Engine** | komponenty, lokalizace, `AutoSaveOptions`, `EmailPreviewDevice`, `EmailItemEditingMode`, `ImageUploadOptions`, `EmailTemplateEditorOptions`, `AddTempoEmailTemplateEditor` / `AddTempoEmailTemplates` |

**Typ → balíček (rozhodující řádky):**

| Typ / složka | Balíček | Poznámka |
|---|---|---|
| `Model/**`, `Serialization/**`, `Registry/**`, `Layout/**`, `Validation/**`, `Dtos/**`, `Contracts/**` | Abstractions | beze změny |
| `Import/**` (`MjmlImporter`, `ImportResult`, `ImportMessage`, `ImportKeys`, `IMjmlIncludeResolver`) | Abstractions | `System.Xml`/`Regex`, **ne** Mjml.Net |
| `Rendering/MjmlGenerator`, `MjmlGeneratorOptions`, `MjmlEscape`, `MjmlAttributeBuffer`, `TextVersionGenerator`, `EmailDocumentValidator`, `DocumentValidation*`, `RenderResult`, `RenderError`, `IMjmlCompiler`, `MjmlCompileResult`, `IEmailTemplateRenderer`, `IEmailPreviewRenderer` | Abstractions | rozhraní + čisté generátory |
| `Templating/ITemplateEngine`, `ITemplateValidator`, `ITemplateVariableExtractor`, `TemplateSecurityOptions`, `TemplateValidationResult`, `TemplateError`, `Result`, `VariableKind`, `TemplateVariableInfo`, `TemplateFunctionInfo`, `SampleDataGenerator`, `EmailDocumentVariableExtractor` | Abstractions | kontrakty a čisté pomocníky |
| `Templating/ScribanTemplateEngine`, `ObjectToScriptObjectConverter`, `SandboxedScriptOutput`, `TemplateVariableExtractor` → `ScribanTemplateVariableExtractor`, `ScribanTemplateValidator` | **Engine** | Scriban |
| `Rendering/MjmlNetCompiler`, `AngleSharpHtmlContentSanitizer` | **Engine** | přímá závislost (Mjml.Net / AngleSharp) |
| `Rendering/EmailTemplateRenderer`, `LocalEmailPreviewRenderer` | **Engine** | **kvůli výchozí kompozici, bez přímé externí závislosti** — skládají Scriban engine + `MjmlNetCompiler` + `MjmlGenerator` a v Abstractions by nemohly mít funkční výchozí pipeline |
| `Rendering/HtmlSanitizerPolicy`, `ConservativeHtmlContentSanitizer`, `IHtmlContentSanitizer` | Abstractions | viz rozhodnutí D12 níže; allow-list obsahuje i `h1`–`h6` bez atributů (D28) |
| `Editing/EmailSaveRequest`, `EmailSaveResult`, `EmailSaveStatus`, `EmailSaveTrigger` | Abstractions | nové (D1) |
| `Localization/JsonStringLocalizer<T>` (Fáze 5) | Abstractions | **`public sealed`**, jen BCL + `Microsoft.Extensions.Localization.Abstractions` (žádná nová `PackageReference`); registruje ho `AddTempoEmailTemplateModel` (po Fázi 6, vedle validátorů) a dnes `AddTempoEmailTemplates` jako CLOSED `IStringLocalizer<TMarker>` — viz D25 |
| `Rendering/UrlSchemePolicy`, `Rendering/MarkupGuard`, `Rendering/MarkupGuardResult`, `Templating/HtmlTextEscaper`, `Templating/IEscapingTemplateEngine`, `Templating/TemplateEscapeMode`, `Rendering/RenderErrorSeverity`, `Rendering/RenderKeys`, `Rendering/TemplateRenderOptions` (Fáze 3) | Abstractions | čisté BCL (regex + string), žádná externí závislost; `IEscapingTemplateEngine`/`TemplateEscapeMode` jsou `internal` a Engine je uvidí přes `InternalsVisibleTo` |
| `Templating/HtmlTemplateFunctions`, `Templating/TrustedHtml`, `Templating/EscapedHtml`, `Templating/SandboxedTemplateContext` (Fáze 3) | **Engine** (dnes v Abstractions do Fáze 6) | registrují Scriban `html.trusted`/`html.escape` a escapovací hook `TemplateContext.Write` — přímá závislost na Scriban |
| …poznámka k Fázi 6 | — | **Engine musí mít na Abstractions přesnou verzi `[2.10.0]`, ne `>=`** — `InternalsVisibleTo` je vázané na silné jméno+verzi sestavení; jinak `IEscapingTemplateEngine`/`TemplateEscapeMode` v Engine přestanou být viditelné a renderer ztratí per-pass escapování (a zaloguje warning o cizím enginu, D24) |

**Dvě vazby, které rozhodnutí o rozdělení neřešilo, a jejich řešení:**

1. **`MjmlGenerator` → `HtmlContentSanitizer` (AngleSharp).** `MjmlGenerator.cs:205` a `:415` volají
   `HtmlContentSanitizer.Sanitize` na obsah textových bloků. Sanitizer tedy nemůže zůstat
   v Abstractions, ale `MjmlGenerator` tam zůstat má.
   *Zvažované varianty:* (a) přesunout i `MjmlGenerator` do Engine — to by ale rozbilo MJML export
   ve WASM (a ten dnes funguje a je to jádro editoru), (b) nechat `AngleSharp` v Abstractions —
   v rozporu s rozhodnutím, (c) **zvoleno (D12, D22)**: politika sanitizace (`HtmlSanitizerPolicy`, jediný zdroj
   allow-listů) zůstává v Abstractions, `IHtmlContentSanitizer` je rozhraní, `AngleSharpHtmlContentSanitizer`
   je v Engine (autoritativní, robustní parser) a `ConservativeHtmlContentSanitizer` (čistě BCL,
   striktnější, fail-closed) je v Abstractions jako výchozí, dokud Engine není zaregistrovaný.
   Konstruktory `MjmlGenerator` (B15): **bezparametrický `MjmlGenerator()` zůstává** (použije
   `ConservativeHtmlContentSanitizer.Instance`) a přidává se `MjmlGenerator(IHtmlContentSanitizer sanitizer)`;
   `EmailTemplateRenderer` ho bere z DI.
   **`ConservativeHtmlContentSanitizer` je tokenizer s HTML-encode, NE regex-based strip/replace.**
   Výchozí akce je zakódovat každý `<`, `>`, `&`, `"` a `'`. Znovu se vyemitují jen tagy z allow-listu
   `HtmlSanitizerPolicy` **bez atributů**; jedinou výjimkou je `a[href]` po kontrole schémat z §3.7a.
   Nikdy se nepropustí `style` ani `on*`. Neuzavřené tagy se doplní a osiřelé zavírací tagy se zakódují;
   tím je vyloučeno mimo jiné vložení `</mj-text>`, `<mj-raw>` nebo `<!--` z obsahu. Entity se dekódují
   a znovu zakódují. Neznámé nebo nevalidní vstupy degradují na escapovaný text, **nikdy na průchod**.
   Rozdíl výstupu proti AngleSharpu je povolen **jen směrem k přísnějšímu**. Paritní XSS sada obsahuje
   i mutation-XSS vektory (`<svg>`, `<math>`, `<noscript>`, `<!--`, `<![CDATA[`, nevalidní vnoření)
   a fuzz test „výstup ⊆ policy“.
   **UX poznámka:** WASM export (bez Engine) může ztratit formátování, které AngleSharp povolí — textové
   bloky se sanitizují konzervativněji; v náhledu je to vidět jako plain text, ne jako bezpečnostní chyba.
2. **Editor → extrakce proměnných (Scriban).** `TmEmailPropertyPanel.razor:150` a
   `TmEmailTemplatePreview.razor:120,125` volají `EmailDocumentVariableExtractor.Extract`, který jde
   přes `TemplateVariableExtractor` (`Templating/TemplateVariableExtractor.cs`, Scriban AST).
   *Zvoleno:* `ITemplateVariableExtractor` (Abstractions) + `RegexTemplateVariableExtractor`
   (Abstractions, čistě BCL: tokeny `{{ path }}` a `for … in <path>` jako baseline) +
   `ScribanTemplateVariableExtractor` (Engine, autoritativní — filtry, lokální proměnné smyček).
   `EmailDocumentVariableExtractor.Extract(document)` **zůstává** (baseline `RegexTemplateVariableExtractor`)
   a přidává se přetížení `Extract(document, ITemplateVariableExtractor extractor)` — skutečné overloady,
   takže stávající volání zůstávají kompilovatelná. Rozdíl mezi implementacemi se týká jen detekce
   kolekcí/filtrů v pickeru, ne bezpečnosti.

**Co ve WASM (editor bez Engine) nahradí engine funkce + čitelná degradace:**

| Funkce editoru | Zdroj bez Engine | Chování |
|---|---|---|
| MJML a JSON export | `MjmlGenerator` + `EmailTemplateSerializer` (Abstractions) | **funguje** (sanitizace přes konzervativní implementaci) |
| Import MJML i JSON | `MjmlImporter` + `EmailTemplateSerializer` (Abstractions) | **funguje** |
| Strukturální validace | `EmailDocumentValidator`, `LayoutValidator` (Abstractions) | **funguje** |
| Syntaxe šablony | `ITemplateValidator` — chybí | `TmEmailValidationPanel` zobrazí **lokalizovanou informaci** „Kontrola syntaxe vyžaduje `Tempo.Blazor.EmailTemplates.Engine` nebo hostitelskou implementaci `ITemplateValidator`“; strukturální nálezy se zobrazí dál. Žádný no-op, žádný prázdný panel. |
| Náhled / HTML export | `IEmailPreviewRenderer` — hostitel (server) | stavy `Unavailable` + retry, lokalizovaná zpráva; chybějící registrace = vysvětlující chyba, ne tichý prázdný iframe |
| Extrakce proměnných | baseline `RegexTemplateVariableExtractor` | picker a ukázková data fungují; detekce kolekcí jen z `for … in` (Engine verze je přesnější) |

**Registrace (nové rozdělení):**

```csharp
// Tempo.Blazor.EmailTemplates.Abstractions — model, registry, validátory, MjmlGenerator (BEZ enginu)
public static IServiceCollection AddTempoEmailTemplateModel(
    this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null);

// Tempo.Blazor.EmailTemplates.Engine — Scriban + Mjml.Net + AngleSharp, volá AddTempoEmailTemplateModel
public static IServiceCollection AddTempoEmailTemplateEngine(
    this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null);

// Tempo.Blazor.EmailTemplates (UI) — UI + model, BEZ enginu
public static IServiceCollection AddTempoEmailTemplateEditor(
    this IServiceCollection services, Action<EmailTemplateEditorOptions>? configure = null);

// AddTempoEmailTemplates — [Obsolete] kompatibilní zkratka, deklarace níže
```

`AddTempoEmailTemplates` zůstává jako **`[Obsolete]`** kompatibilní zkratka:

```csharp
[Obsolete("Use AddTempoEmailTemplateEditor() and, for local rendering, AddTempoEmailTemplateEngine() " +
          "from Tempo.Blazor.EmailTemplates.Engine")]
public static IServiceCollection AddTempoEmailTemplates(
    this IServiceCollection services, Action<TemplateSecurityOptions>? configure = null)
    => services.AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security));
```

Kompozice: server/plný engine `AddTempoEmailTemplateEditor().AddTempoEmailTemplateEngine()` (nebo oba
`AddTempoEmailTemplateModel()` + `AddTempoEmailTemplateEngine()`); WASM editor
`AddTempoEmailTemplateEditor()` + hostitelský `IEmailPreviewRenderer` (+ volitelně serverový
`ITemplateValidator`). `AddTempoEmailTemplates` engine **neregistruje** (UI balíček Engine nezná) a jen
deleguje na `AddTempoEmailTemplateEditor`.

**Sémantika registrací:**

- `AddTempoEmailTemplateModel` je **idempotentní** a registruje baseline přes `TryAdd*`:
  `IHtmlContentSanitizer` = `ConservativeHtmlContentSanitizer`, `ITemplateVariableExtractor` =
  `RegexTemplateVariableExtractor`, **`IStringLocalizer<EmailTemplateValidationResources>`** =
  `JsonStringLocalizer<EmailTemplateValidationResources>` (Fáze 5 — **musí žít tady, vedle validátorů,
  ne v Engine**: validátory jsou v Abstractions, takže i bez Engine musí vracet přeložený text;
  testy `HostLocalizerFactoryReplacementTests` se ve Fázi 6 přepnou z `AddTempoEmailTemplateEngine()`
  na `AddTempoEmailTemplateModel()` a přibude případ `AddTempoEmailTemplateEditor()` bez Engine →
  validátor vrací `cs` text) a **jednu instanci** `TemplateSecurityOptions` (`TryAddSingleton`;
  `configure` se aplikuje vždy na tuto jednu instanci). Dnes (do Fáze 6) tuto registraci dělá
  `AddTempoEmailTemplateEngine` v Abstractions.
- `AddTempoEmailTemplateEngine` baseline **přepíše přes `Replace`** (`AngleSharpHtmlContentSanitizer`,
  `ScribanTemplateVariableExtractor`, `ScribanTemplateValidator`, `MjmlNetCompiler`, `EmailTemplateRenderer`,
  `LocalEmailPreviewRenderer`) → **výsledek nezávisí na pořadí volání** a `configure` se vždy aplikuje
  na tu jednu instanci `TemplateSecurityOptions`.
- `EmailTemplateRenderer` bere `MjmlGenerator` **z DI**, nikdy `new MjmlGenerator(...)`.
- Test (Fáze 6): obě pořadí (`Model`→`Engine` i `Engine`→`Model`) dají v `IHtmlContentSanitizer`
  **`AngleSharpHtmlContentSanitizer`** a stejnou instanci `TemplateSecurityOptions`.

**Výjimka z pravidla „WASM bez Enginu“ (rozhodnutí moderátora, Fáze 6 review):**
`Tempo.ReportServer.Web.Client` (Blazor WebAssembly host dema report serveru, `IsPackable=false`,
nepublikovaný) **vědomě referencuje Engine ve WASM** — lokální doručení v demo plánovači
(`ReportEmailDeliveryService`/`ReportScheduleWorker` renderují e-mail přímo v klientovi, protože demo
nemá serverový render endpoint). To není rozpor s D11: D11 chrání **publikované** balíčky (UI balíček
Engine nereferencuje, WASM demo `Tempo.Blazor.Demo` ho netáhne), kdežto tohle je interní demo host.
Hlídá to `PackageBoundaryTests.ReportServerWasmClient_IsTheOnlyWasmHostWithTheEngine` (jen tento jediný
WASM host smí Engine mít) a `Target:ReportServerClient` v `plan_report_remaining` (přesun registrace
enginu na serverový hostitel, mimo 2.10.0).

**Rozhodnutí o namespacech:** přesouvané typy **zůstávají ve svých namespacech**
(`…Abstractions.Templating`, `…Abstractions.Rendering`, `…Abstractions`), mění se jen assembly/balíček.
Důvod: migrace hostitele je pak pouze „přidat `PackageReference Tempo.Blazor.EmailTemplates.Engine` +
zavolat `AddTempoEmailTemplateEngine()`“, žádný `using` (rozhodnutí **D21** v 5.2, včetně pravidla, že
v Engine nesmí vzniknout typ se stejným FQN jako typ zůstávající v Abstractions).

**Kde se rozdělení provede:** **Fáze 6** („Vyměnitelný renderer náhledu (serverový náhled) a rozdělení
registrace“) — tam už je registrace rozdělována, takže nový balíček vzniká tam (poznámka je i v §6).
Fáze 2–4 pracují ještě v Abstractions a nesmí přidat novou závislost; při přesunu ve Fázi 6 se soubory
jen přesunou do nového projektu **bez změny logiky**. Testy se rozdělí: čisté typy zůstanou
v `tests/Tempo.Blazor.EmailTemplates.Abstractions.Tests`, testy Scriban/Mjml.Net/AngleSharp typů
přejdou do nového `tests/Tempo.Blazor.EmailTemplates.Engine.Tests` (viz §6).

### 3.1 Jádrové API, na kterém kontrakt stojí (ověřeno ve stromu, dodal základový plán `faf503eb`)

Tato fáze jádro **nemění** — jen na něj mapuje e-mailový kontrakt. Ověřené typy a jejich skutečné názvy:

| Jádrový typ | Namespace | Ověřená veřejná plocha (relevantní pro editor) |
|---|---|---|
| `TmLayoutMode` | `Tempo.Blazor.Abstractions.Layout` | `Auto = 0`, `Desktop = 1`, `Tablet = 2`, `Mobile = 3` (`TmLayoutMode.cs:7-20`) |
| `TmLayoutBreakpoints` | `Tempo.Blazor.Abstractions.Layout` | `public sealed record TmLayoutBreakpoints` s explicitním ctor `(int sm, int lg)` a **vlastnostmi** `public int Sm { get; }`, `public int Lg { get; }`; `static Default` = 640/1024; `TmLayoutMode Classify(double widthPx)`; ctor validuje `0 < sm < lg` |
| `TmBreakpoints` | `Tempo.Blazor.Abstractions.Layout` | `const int Sm = 640, Md = 768, Lg = 1024, Xl = 1280` |
| `TmEditorShell` | `Tempo.Blazor.Components.Layout` | sloty `Header`, `Toolbar`, `Left`, `Canvas`, `Right`, `StatusBar`, `Extra`, `LeftRail`, `RightRail`; `LayoutMode`, `InitialMode`, `ResolvedLayoutChanged`, `Breakpoints`; `MobilePanelPresentation`, `ActiveMobilePanel(+Changed)`, `MobileSheetSnapIndex(+Changed)`, `MobileSheetSnapPoints`, `ShowMobilePanelsToggle`, `CanvasTitle`; `LeftOpen/RightOpen(+Changed)`, `CollapsedPanels(+Changed)`, `LeftTitle`/`RightTitle`/`ExtraTitle`, `LeftWidth`/`RightWidth`(+Changed), `MinLeftWidth`/`MaxLeftWidth`/`MinRightWidth`/`MaxRightWidth`, `MinCanvasWidth`, `PersistWidthsKey`, `MobileActions`, `Class` |
| `MobilePanelPresentation` | `Tempo.Blazor.Components.Layout` | `Sheet` (0, default — plátno zůstává viditelné, dolů inline ne-modální `TmDrawer` sheet; při dvou otevřených panelech tab strip uvnitř sheetu), `Tabs` (1 — full-region `role="tablist"` Left \| Canvas \| Right, bez sheetu) — viz `TmEditorShell.razor:44-70` (Tabs), `:105-125` (`TmDrawer` bottom sheet + tab strip uvnitř), `:551` (`SheetShown`) |
| `EditorShellPanel` | `Tempo.Blazor.Components.Layout` | `[Flags] None = 0, Left = 1, Right = 2, Extra = 4` |
| `MobilePresentation` | `Tempo.Blazor.Components.Feedback` | `Dialog`, `Sheet`, `Fullscreen`, `Auto` — parametr `TmModal.MobilePresentation` (default `Auto`) |
| `DrawerPosition` | `Tempo.Blazor.Components.Layout` | `Right`, `Left`, **`Bottom`** (sheet s úchytem a snapy) |
| `TmKeyboardShortcutsHelp` | `Tempo.Blazor.Components.Layout` | `Categories`, `IsVisible`, `OnClose`, `Title`, **`Description`** (podtitulek) |
| `TmNavigationGuard` | `Tempo.Blazor.Components.Navigation` | `IsDirty`, `IsDirtyCallback`, `OnSaveAndLeave` = `Func<Task<bool>>?`, `ShouldGuard`, `BeforeUnloadPrompt` |
| `TmSidePanel` / `SidePanelPresentation` | `Tempo.Blazor.Components.Layout` | `Auto`/`Docked`/`Sheet` (F6; editor ho nepoužívá, staví na `TmEditorShell`) |

> ⚠️ **Pozor na kolizi názvů:** e-mailový balíček má vlastní namespace
> `Tempo.Blazor.EmailTemplates.Abstractions.Layout` (obsahuje `LayoutSeverity`), zatímco `TmLayoutMode`
> bydlí v `Tempo.Blazor.Abstractions.Layout`. Hostitel, který importuje oba, musí `TmLayoutMode`
> kvalifikovat nebo použít alias.

> ⚠️ **Nesoulad v jádru (mimo rozsah této fáze, ale kontrakt se ho musí držet):** XML doc hodnoty
> `MobilePanelPresentation.Tabs` v `MobilePanelPresentation.cs` tvrdí „tab strip uvnitř sheetu“, ale
> implementace `TmEditorShell` (`TmEditorShell.razor:44-70`) i XML doc parametru (`:328`) dělají z `Tabs`
> **full-region tablist bez sheetu**. Kontrakt výše se drží implementace; sjednocení XML doc je úkol
> základového plánu, ne tohoto.

### 3.2 Ukládací kontrakt (`Tempo.Blazor.EmailTemplates.Abstractions.Editing` — nový namespace)

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Editing;

/// <summary>Why a save was requested.</summary>
public enum EmailSaveTrigger
{
    Manual,        // tlačítko Uložit
    Shortcut,      // Ctrl+S / Cmd+S
    AutoSave,      // debounce z AutoSaveOptions
    Programmatic   // host přes SaveAsync()
}

/// <summary>
/// One save request. Document is a deep copy (snapshot) taken at request time; Revision is the
/// editor's monotonic document revision the snapshot was taken at.
/// </summary>
public sealed record EmailSaveRequest(
    EmailTemplateDocument Document,
    EmailSaveTrigger Trigger,
    long Revision,
    CancellationToken CancellationToken);

/// <summary>Outcome reported by the host.</summary>
public enum EmailSaveStatus { Saved, Failed, Conflict }

/// <summary>The host's answer to one save request.</summary>
public sealed class EmailSaveResult
{
    public EmailSaveStatus Status { get; }
    public string? Message { get; }                        // volitelný důvod (Failed/Conflict), lokalizuje hostitel
    public bool IsSaved => Status == EmailSaveStatus.Saved;

    public static EmailSaveResult Saved();
    public static EmailSaveResult Failed(string? message = null);
    public static EmailSaveResult Conflict(string? message = null);
}
```

Veřejná plocha `TmEmailTemplateEditor` (namespace `Tempo.Blazor.EmailTemplates.Components`):

```csharp
// Ukládání (Fáze 7) — [Parameter] vlastnosti
[Parameter] public Func<EmailSaveRequest, Task<EmailSaveResult>>? OnSave { get; set; }
[Parameter] public AutoSaveOptions? AutoSave { get; set; }           // zůstává; Interval = debounce
[Parameter] public EventCallback<bool> IsDirtyChanged { get; set; }  // volá se jen při změně hodnoty
[Parameter] public bool ReadOnly { get; set; }                       // default false
[Parameter] public RenderFragment? ToolbarStart { get; set; }
[Parameter] public RenderFragment? ToolbarEnd { get; set; }
[Parameter] public bool ShowSaveButton { get; set; } = true;
[Parameter] public bool ShowSaveStatus { get; set; } = true;
[Parameter] public ImageUploadOptions? ImageUpload { get; set; }     // namespace Tempo.Blazor.EmailTemplates

// Veřejný STAV (vlastní ho editor, není to [Parameter])
public bool IsDirty { get; private set; }
public bool IsSaving { get; private set; }

// Veřejná metoda
public Task<EmailSaveResult> SaveAsync();

// Beze změny zůstává (defaulty ve skutečné deklaraci)
[Parameter] public EmailTemplateDocument? Document { get; set; }
[Parameter] public EventCallback<EmailTemplateDocument> DocumentChanged { get; set; }
[Parameter] public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
[Parameter] public IReadOnlyList<TemplateVariableInfo> Variables { get; set; } = Array.Empty<TemplateVariableInfo>();
[Parameter] public Func<IBrowserFile, Task<string>>? OnImageUpload { get; set; }
```

**Sémantika ukládání (závazná pro Fázi 7):**

- **Single-flight:** v jednom okamžiku běží nejvýš jedno volání `OnSave`.
- Trigger (Uložit / Ctrl+S / autosave) přijatý **během** ukládání se sloučí do **jednoho** následného
  uložení, je-li dokument stále dirty; `SaveAsync()` z takového volání vrátí výsledek toho sloučeného uložení.
- `IsDirty = false` se nastaví **jen** po `Saved` a **jen** když `Revision` dokumentu stále odpovídá
  revizi snapshotu (editace během ukládání zůstává dirty).
- Výjimka z `OnSave` = `Failed` + záznam do logu; text „Uloženo“ se nikdy nezobrazí bez potvrzení hostitele.
- `OnSave == null` nebo `ReadOnly` → `SaveAsync()` vrátí `Failed` **bez** volání hostitele (žádný no-op).
- Autosave s `IsDirty == false` se **nevolá**.
- `CancellationToken` v requestu se zruší při `Dispose` editoru; host ho má předat do svého I/O.
- Po `Conflict` se autosave **pozastaví** do úspěšného **ne-autosave** uložení (`Manual`, `Shortcut` i
  `Programmatic` — viz D26 v [5.2](#52-nová-rozhodnutí-z-review-2026-10-20)) nebo do předání nové
  reference `Document`.
- Nová reference `Document` (jiná instance) resetuje baseline (snapshot), `IsDirty = false` a undo historii.
- Verzování/ETag drží **hostitel** (editor posílá jen `Revision` svého lokálního stavu).

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `EmailSaveTrigger` | **ne** (nový enum) | 7 | Čtyři hodnoty výše. |
| `EmailSaveRequest` (vč. `Revision` + `CancellationToken`) | **ne** (nový záznam) | 7 | `Document` = hluboká kopie; `Revision` = revize snapshotu. |
| `EmailSaveStatus` | **ne** (nový enum) | 7 | `Saved`/`Failed`/`Conflict` (potvrzeno D1). |
| `EmailSaveResult` (+ `IsSaved`) | **ne** (nový typ) | 7 | `IsSaved` je pojmenovaná vlastnost, ne `bool IsSaved` v ctoru. |
| `OnSave` = `Func<EmailSaveRequest, Task<EmailSaveResult>>?` | **ANO** | 7 | Nahrazuje `EventCallback<EmailTemplateDocument> OnSave` (`TmEmailTemplateEditor.razor:88`). |
| `OnAutoSave` **zrušen** | **ANO** | 7 | Autosave volá `OnSave` s `Trigger = AutoSave`. |
| `AutoSave` (`AutoSaveOptions`) | ne | — | `Enabled` + `Interval` zůstávají (`AutoSaveOptions.cs:4-11`); ruší se jen callback. |
| `IsDirty` (+ `IsDirtyChanged`), `IsSaving` | ne (aditivní) | 7 | Stav vlastní editor, není `[Parameter]` → `@bind-IsDirty` nefunguje (potvrzeno D2). |
| `ReadOnly` | ne (aditivní, default `false`) | 7 | Zakáže editaci, ponechá náhled/validaci/export. |
| `SaveAsync()` | ne (aditivní) | 7 | Nahrazuje privátní `SaveAsync` (`TmEmailTemplateEditor.razor:149`) — ta se přejmenuje (např. `RequestSaveAsync`). |
| `ToolbarStart` / `ToolbarEnd` | ne (aditivní) | 7 | `RenderFragment?`; na mobilu v `Header` shellu (viz 3.3). |
| `ShowSaveButton` / `ShowSaveStatus` | ne (aditivní, default `true`) | 7 | `ShowSaveStatus` = odznak stavu uložení. |
| `ImageUploadOptions { Accept, MaxBytes }` | ne (aditivní) | 7 | Namespace `Tempo.Blazor.EmailTemplates`; validace **před** `OnImageUpload`, porušení = lokalizovaná chyba a `Src` se nezmění. |

Napojení dirty guardu (ověřená signatura `TmNavigationGuard.OnSaveAndLeave` = `Func<Task<bool>>?`).
`IsDirty` je **stav, který si hostitel drží sám** z `IsDirtyChanged` — `_editor.IsDirty` v markupu se
vyhodnotí jen při renderu, takže guard by později pracoval se zastaralou hodnotou:

```razor
<TmEmailTemplateEditor @ref="_editor" Document="_document"
                       IsDirtyChanged="d => _dirty = d" OnSave="SaveAsync" />

<TmNavigationGuard IsDirty="_dirty"
                   OnSaveAndLeave="@(async () => _editor is not null && (await _editor.SaveAsync()).IsSaved)" />
```

> **Stav po Fázi 7 (implementováno 2026-10-21).** Kontrakt výše je splněn beze změny jmen/typů:
> `Editing` namespace (`EmailSaveTrigger`, `EmailSaveRequest`, `EmailSaveStatus`, `EmailSaveResult`),
> `OnSave` = `Func<EmailSaveRequest, Task<EmailSaveResult>>?`, `OnAutoSave` zrušen (autosave volá `OnSave`
> s `Trigger = AutoSave`), `IsDirty`/`IsSaving` vlastněné editorem, `SaveAsync()` veřejné (privátní cesta
> se jmenuje `RequestSaveAsync`), `ReadOnly`, `ToolbarStart`/`ToolbarEnd`, `ShowSaveButton`/`ShowSaveStatus`,
> `ImageUploadOptions`.
> Doplňky implementace, které kontrakt nezakazuje: (a) „Uloženo {relativní čas}" je obnovováno
> jednou za minutu z `TimeProvider` a relativní frázi skládá **jádrový `ITmRelativeTimeFormatter`**
> (hostitel ho může vyměnit; formát „Uloženo {0}" zůstává textem editoru) — `EmailEditor_Relative_*`
> proto zanikly; (b) `Ctrl+S` ruší výchozí akci prohlížeče „uložit stránku" jen pro ten
> akord, přes vlastní ES modul balíčku `wwwroot/tm-email-save-shortcut.js` — modul drží listener
> **per element** (`WeakMap`), takže dva editory na stránce se navzájem neodzbrojí (repo vzor: modul
> balíčku jako `tm-email-variable-insert.js`; jádrové editory `Ctrl+S` neřeší, `@onkeydown:preventDefault`
> je render-time bool, takže by spolkl i Tab); (c) popisky Zpět/Znovu se zobrazí při šířce **kontejneru**
> ≥ 1200 px (container query na liště) — E2E to ověřuje na vykreslené šířce lišty, ne na viewportu;
> (d) `ReadOnly` v inspektoru = nativní `disabled` fieldset (display: contents, žádná změna layoutu).
> **(e) Revize po code review (2026-10-22):** `IsDirty` se počítá proti snapshotu bez `DeepClone`
> (`UpdatedAt` se dočasně vynuluje v try/finally — běží jen na dispatcheru); výsledek uložení, který
> dorazí po předání nové reference `Document`, odpovídá jen volajícímu (epoch dokumentu) a nemění
> baseline/badge/pauzu; `SaveAsync()` skáče na dispatcher přes `InvokeAsync`; fronta uložení znovu
> kontroluje `_disposed`/`ReadOnly`/`OnSave` u follow-upu a po Conflict nespouští frontu typu AutoSave;
> `Failed`/`Conflict` mají pod lištou `TmAlert` (`data-tm-save-alert`) s plnou zprávou hostitele, hintem a
> tlačítkem „Zkusit znovu" (`data-tm-save-retry`, `Trigger = Manual`), zatímco odznak zůstává krátký;
> odznak už není live region — stav oznamuje jeden `role="status"` announcer (`data-tm-save-announcer`)
> jen pro potvrzené `Manual`/`Shortcut`/`Programmatic` uložení (Failed/Conflict oznamuje alert s
> `role="alert"`, aby se neoznamovalo dvakrát); Uložit není během ukládání `disabled`, ale
> `aria-disabled` (single-flight klik sloučí), `disabled` zůstává jen v `ReadOnly`; read-only režim
> nahradí save-chrome odznakem „Jen pro čtení" (`data-tm-readonly-badge`) a tlačítko Uložit vůbec
> nevykreslí; toolbar je rozložený na privátní `RenderFragmenty` (HistoryGroup/ReviewGroup/FileGroup/
> HelpGroup/SaveButton/SaveBadge), takže F8 může přesunout skupiny do overflow menu beze změny
> `data-tm-group`/`data-tm-save`/`data-tm-save-badge`; (f) `ReadOnly` je aditivně i na sub-editorech
> (`TmEmailListEditor`, `TmEmailKeyValueEditor`, `TmEmailTableEditor`, `TmEmailHtmlAttributesEditor`,
> `TmEmailClassesEditor`, `TmEmailVariablePicker`), protože `disabled` fieldset sám o sobě nestačí —
> emulovaný event (bUnit, syntetická událost) handler spustí, takže každá mutující metoda má vlastní
> `if (ReadOnly) return;`.

### 3.3 Rozvržení a responzivita (`TmEditorShell` ze základu F6, rozhodnutí EM2(a)/EM3)

```csharp
// Typy z JÁDRA — editor je jen vystavuje, neobaluje:
using Tempo.Blazor.Abstractions.Layout;    // TmLayoutMode, TmLayoutBreakpoints
using Tempo.Blazor.Components.Layout;      // MobilePanelPresentation, EditorShellPanel

[Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;
[Parameter] public TmLayoutMode InitialMode { get; set; } = TmLayoutMode.Desktop;
[Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }
[Parameter] public TmLayoutBreakpoints? Breakpoints { get; set; }     // null = 768/1200
[Parameter] public MobilePanelPresentation MobilePanelPresentation { get; set; } = MobilePanelPresentation.Sheet;
[Parameter] public EditorShellPanel ActiveMobilePanel { get; set; }
[Parameter] public EventCallback<EditorShellPanel> ActiveMobilePanelChanged { get; set; }
[Parameter] public bool ToolboxCollapsed { get; set; }
[Parameter] public EventCallback<bool> ToolboxCollapsedChanged { get; set; }
```

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `LayoutMode` (`TmLayoutMode`) | ne (aditivní) | 8 | **Nahrazuje dřívější návrh `EmailEditorLayout`** (EM2(a); nikdy nevydáno). `Auto` měří kontejner editoru (ne viewport). |
| `InitialMode` (`TmLayoutMode`) | ne (aditivní) | 8 | První frame před měřením, default `Desktop`. |
| `ResolvedLayoutChanged` (`EventCallback<TmLayoutMode>`) | ne (aditivní) | 8 | Editor ho přeposílá ze shellu (hostitel podle něj ladí chrome). |
| `Breakpoints` (`TmLayoutBreakpoints?`) | ne (aditivní) | 8 | `null` = `new TmLayoutBreakpoints(TmBreakpoints.Md, 1200)` = 768/1200 (shodné s DiagramEditorem, potvrzeno D7). |
| `MobilePanelPresentation` | ne (aditivní, default `Sheet`) | 8 | `Sheet` = plátno + spodní sheet se záložkami Bloky / Vlastnosti (EM3 default; sheet je inline `TmDrawer` a při obou otevřených panelech má uvnitř tab strip), `Tabs` = full-region `role="tablist"` Left \| Canvas \| Right bez sheetu. |
| `ActiveMobilePanel` / `ActiveMobilePanelChanged` (`EditorShellPanel`) | ne (aditivní) | 8 | Editor na mobilu přepne na `Right` po výběru bloku; hostitel může řídit. |
| `ToolboxCollapsed` / `ToolboxCollapsedChanged` | ne (aditivní) | 8 | Ponecháno jako vlastní parametr (potvrzeno D10, zpřesněno **D27**). Mapuje se na `TmEditorShell.CollapsedPanels`. **Tabletový auto-rail je stav layoutu a `ToolboxCollapsedChanged` NEVYVOLÁ**; událost vyvolá jen uživatelské sbalení/rozbalení (toggle panelu, rozbalení inspektoru z jeho railu). Shell posílá `ResolvedLayoutChanged` **před** `CollapsedPanelsChanged` (jádro 2.10.0, U2), takže editor rozhoduje synchronně podle generace layoutu — raise ve stejné generaci jako změna layoutu = stav layoutu (nepřevzít, neoznámit). **Na mobilu se ignoruje** (tam panely řeší sheet). |

**Mapování editoru na `TmEditorShell` (1:1, žádný lokální layout wrapper):**

| Shell slot / parametr | Obsah v editoru |
|---|---|
| `Header` | **na mobilu** kompaktní hlavička: vlevo `ToolbarStart`, nebo (bez něj) název dokumentu `Document.Name`; vpravo primární Uložit + `⋮` (`margin-inline-start: auto`); `ToolbarEnd` jde do overflow `⋮` (shell na mobilu `Toolbar` vůbec nerenderuje, když jsou `MobileActions` — `TmEditorShell.razor:30`) |
| `Toolbar` | na desktopu/tabletu příkazová řada: `ToolbarStart` \| Historie (Zpět/Znovu) \| Kontrola (Náhled/Validovat) \| Soubor (Import/Export) \| Nápověda \| `ToolbarEnd` \| Uložit |
| `Left` (+ `LeftRail`) | `TmEmailTemplateToolbox` (rail = ikonová lišta při `ToolboxCollapsed`) |
| `Canvas` | `TmEmailTemplateCanvas` (od Fáze 9A WYSIWYG) |
| `Right` | `TmEmailPropertyPanel` (inspektor) |
| `RightRail` | jediné dlaždicové tlačítko (`IconNames.Sliders`, `aria-label` `EmailEditor_Panel_Properties`), které na tabletu rozbalí inspektor a zařídí tak uživatelský auto-rail palety (D27) |
| `StatusBar` | odznak stavu uložení (`ShowSaveStatus`) + stavové informace |
| `MobileActions` | **jen** Undo (Zpět) / Znovu / Náhled / Validovat, `MobileActionsMaxVisible="4"` (Uložit je v `Header`, ne tady) |
| `Extra` | **nevyužito** (e-mail má právě dva panely: Bloky a Vlastnosti) |
| `Breakpoints` | `Breakpoints ?? new TmLayoutBreakpoints(TmBreakpoints.Md, 1200)` |
| `PersistWidthsKey` | volitelné, na rozhodnutí hostitele (ne v kontraktu 2.10.0) |

### 3.4 Náhled na serveru — `IEmailPreviewRenderer` (Fáze 6)

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>Where the rendered preview came from.</summary>
public enum EmailPreviewStatus { Rendered, RenderFailed, Unavailable }

/// <summary>One preview request. SubstituteVariables=false keeps {{ }} tokens (HTML export).</summary>
public sealed record EmailPreviewRequest
{
    public required EmailTemplateDocument Document { get; init; }
    public string? VariablesJson { get; init; }
    public bool SubstituteVariables { get; init; } = true;
    /// <summary>Culture name (e.g. "cs-CZ"); null = renderer default (document language / invariant).</summary>
    public string? CultureName { get; init; }
}

/// <summary>The rendered preview, or the reason it is missing. Html/Text/Subject are never null.</summary>
public sealed record EmailPreviewResult(
    string Html,               // Failed/Unavailable => ""
    string Text,               // Failed/Unavailable => ""
    string Subject,            // Failed/Unavailable => ""
    string? Preheader,
    IReadOnlyList<RenderError> Errors,
    EmailPreviewStatus Status);

/// <summary>Replaceable preview renderer — the host may render on the server.</summary>
public interface IEmailPreviewRenderer
{
    Task<EmailPreviewResult> RenderAsync(EmailPreviewRequest request, CancellationToken cancellationToken = default);
}
```

`LocalEmailPreviewRenderer` (implementace v balíčku **Engine**, obaluje `EmailTemplateRenderer`) je
výchozí, když je Engine zaregistrovaný.

`TmEmailTemplatePreview` (namespace `Tempo.Blazor.EmailTemplates.Components`):

```csharp
[Parameter, EditorRequired] public EmailTemplateDocument Document { get; set; } = new();
[Parameter] public string? VariablesJson { get; set; }
[Parameter] public EventCallback<string> VariablesJsonChanged { get; set; }   // host si ukládá sample data
[Parameter] public EmailPreviewDevice InitialDevice { get; set; } = EmailPreviewDevice.Desktop;
[Parameter] public bool ShowSampleDataEditor { get; set; } = true;
[Parameter] public bool ShowViewToggle { get; set; } = true;                  // HTML / Text
[Parameter] public RenderFragment? FooterActions { get; set; }                // host: "Odeslat testovací e-mail"
[Parameter] public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
```

```csharp
// namespace Tempo.Blazor.EmailTemplates.Components
public enum EmailPreviewDevice { Desktop, Tablet, Mobile }
```

**Bezpečnost přenosu a chování náhledu:**

- Náhled zůstává **`<iframe sandbox="" srcdoc="…">`** i pro HTML ze serverového rendereru — **žádné
  `allow-scripts`**, žádné `allow-same-origin`. HTML z `IEmailPreviewRenderer` je **nedůvěryhodné**
  (i když ho vyrobil vlastní server) a do dokumentu se vkládá jen jako `srcdoc` sandboxovaného iframe.
- Přenos request/result mimo proces (PostrionCore HTTP endpoint): `Document` se serializuje
  **`EmailTemplateSerializer`** (kanonický JSON, polymorfní bloky řeší on, ne `System.Text.Json`),
  `VariablesJson`/`CultureName` jsou prosté stringy. Výsledek se mapuje na `RenderPreviewRequest/Response`
  (Abstractions) nebo na vlastní DTO hostitele.
- `Failed`/`Unavailable` → `Html`/`Text`/`Subject` jsou **`""`, nikdy `null`** (view nemusí nil-checkovat).
- **Varování nejsou `RenderFailed` (D23):** `EmailPreviewStatus.Rendered` se použije i tehdy, když
  `RenderResult.HasWarnings` je `true` (blokované URL, odstraněný event handler) — `Html` je kompletní a
  `Success` zůstává `true`. `RenderFailed` je vyhrazený pro `RenderErrorSeverity.Error`; varování se
  předávají v `Errors` a UI je zobrazuje odděleně (dnes `data-tm-preview-errors` vs
  `data-tm-preview-warnings`).
- Každé nové vykreslení **zruší `CancellationToken` předchozího**; starší odpověď nikdy nepřepíše novější.

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `IEmailPreviewRenderer` + `EmailPreviewRequest`/`EmailPreviewResult`/`EmailPreviewStatus` | ne (nové typy) | 6 | `Unavailable` odlišuje síť/server od chyby šablony. |
| `EmailPreviewRequest.CultureName` | ne (aditivní) | 6 | Kultura renderu na serveru. |
| `LocalEmailPreviewRenderer` | ne (nový typ) | 6 | V balíčku **Engine**; registruje ho `AddTempoEmailTemplateEngine()`. |
| `TmEmailTemplatePreview` injektuje `IEmailPreviewRenderer` místo `IEmailTemplateRenderer` | **ANO** | 6 | Migrace: host, který si přepsal `IEmailTemplateRenderer`, registruje `IEmailPreviewRenderer`. |
| `VariablesJsonChanged` | ne (aditivní) | 6 | |
| `InitialDevice` (`EmailPreviewDevice`) | ne (aditivní; dřívější plán měl `bool`, nikdy nevydáno) | 6 | **Nahrazuje** `bool _mobile` (`TmEmailTemplatePreview.razor:73`). |
| `EmailPreviewDevice` | ne (nový enum) | 6 | Desktop / Tablet / Mobile; Tablet = 768 px, Mobile = 390 px (konstanty na jednom místě). |
| `ShowSampleDataEditor` | ne (aditivní, default `true`) | 6 | Demo/send page používá `false`. |
| `ShowViewToggle` | ne (aditivní, default `true`) | 6 | Stránka odeslání přepínač HTML/Text skryje. |
| `FooterActions` | ne (aditivní) | 6 | `RenderFragment?` — editor sám nic neodesílá. |
| `EmailPreviewResult.Unavailable(IReadOnlyList<RenderError> errors)` | ne (aditivní statická továrna) | 6 | Jediný doporučený způsob, jak renderer hlásí „síť/server není k dispozici“: `Html`/`Text`/`Subject` = `""`, `Preheader` = `null`, `Status = Unavailable`. Editor z toho pozná, že má nabídnout retry (rozdíl proti `RenderFailed` = chyba šablony). |
| `TmEmailTemplatePreview.CultureName` | `[Parameter] public string? CultureName { get; set; }` | ne (aditivní) | 6 | Kultura renderu náhledu; `null` = výchozí kultura rendereru (jazyk dokumentu, jinak invariant). Předává se do `EmailPreviewRequest.CultureName`, je součástí klíče requestu. |
| `IHtmlContentSanitizer.SanitizeTemplateContent` | `string SanitizeTemplateContent(string content)` | ne (aditivní člen rozhraní implementovaného v 2.10.0) | 6 | Sanitizace textového bloku, který může obsahovat `{{ … }}`/`{% … %}`: implementace musí code spany **zachovat verbatim** (jinak se rozbije šablona) a zbytek sanitizovat stejnou politikou jako `Sanitize`. Obě implementace sdílejí `TemplateContentMasker`; podvržený placeholder (PUA zapsaná entitou) se **nikdy neodstraňuje, ale označí obsah jako neplatný** a výsledek jde fail-closed cestou (`StripPua(sanitize(content))`, U+E000/U+E001 → U+FFFD). |

### 3.5 Registrace DI (Fáze 6)

Viz kód v [3.0](#30-balíčky-a-závislosti-nové-rozhodnutí-2026-10-20). Options:

```csharp
namespace Tempo.Blazor.EmailTemplates;

/// <summary>Options of the editor-only registration.</summary>
public sealed class EmailTemplateEditorOptions
{
    /// <summary>Sandbox limity sdílené s validací a extrakcí proměnných.</summary>
    public TemplateSecurityOptions Security { get; } = new();
}
```

`FunctionNamespaces` **není** v `EmailTemplateEditorOptions` — je v `TemplateSecurityOptions`
(`ISet<string>`, viz 3.6) a registrace ho automaticky naplní z
`IEnumerable<ITemplateFunctionProvider>.Namespace`, takže ho hostitel nevyplňuje ručně a engine
i editor-only registrace se chovají stejně.

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `AddTempoEmailTemplateModel(...)` (nová, Abstractions) | ne (nová metoda) | 6 | Model + registry + validátory + `MjmlGenerator` + baseline extractor. |
| `AddTempoEmailTemplateEngine(...)` (přesun do Engine) | **ANO** | 6 | Vyžaduje `PackageReference Tempo.Blazor.EmailTemplates.Engine`. |
| `AddTempoEmailTemplateEditor(...)` | ne (nová metoda) | 6 | UI + model, **bez** enginu; chybějící `IEmailPreviewRenderer` = vysvětlující chyba v náhledu. |
| `AddTempoEmailTemplates(...)` | **ANO** (chování), ale `[Obsolete]` | 6 | Deleguje na `AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security))` a engine neregistruje; náhled bez `IEmailPreviewRenderer` = `Unavailable` s textem jmenujícím chybějící registraci. |
| `EmailTemplateEditorOptions` (`Security`) | ne (nový typ) | 6 | `FunctionNamespaces` je v `TemplateSecurityOptions` (D6) a plní se z providerů automaticky. |

`TmEmailTemplatePreview` bez registrovaného `IEmailPreviewRenderer` zobrazí stav **`Unavailable`**
s textem, který **jmenuje chybějící registraci** (např. „Náhled vyžaduje registraci
`IEmailPreviewRenderer` (server) nebo balíček `Tempo.Blazor.EmailTemplates.Engine`“) — nikdy prázdný
iframe ani tichý no-op.

**Přepnutí volajících už ve Fázi 6** (ověřené adresy; počet testovacích volání opraven proti review —
grep najde **15**, ne 4):

| Volající | Dnes | Po Fázi 6 |
|---|---|---|
| `src/Tempo.Blazor.Demo/Program.cs:125` (WASM) | `AddTempoEmailTemplates()` | `AddTempoEmailTemplateEditor()` + HTTP `IEmailPreviewRenderer` (Engine ve WASM zůstává vypnutý) |
| `tests/Tempo.Blazor.EmailTemplates.Tests/Components/*.cs` (14×) + `tests/Tempo.Blazor.Tests/EmailTemplates/EmailTemplatePagesTests.cs:28` — **celkem 15×** `Services.AddTempoEmailTemplates()` | `AddTempoEmailTemplates()` | `AddTempoEmailTemplateEditor()` + `AddTempoEmailTemplateEngine()` (testy potřebují lokální render i syntax validaci) |
| `src/Tempo.Blazor.Demo.Api/Program.cs:164` | `AddTempoEmailTemplateEngine()` | stejné volání + **nová `PackageReference Tempo.Blazor.EmailTemplates.Engine`** (Demo.Api dnes referencuje jen Abstractions) |

> **Nález k B4 (migrace pozičního argumentu):** `src/Tempo.Blazor.Demo.Api/Endpoints/EmailTemplateEndpoints.cs:58`
> a `:93` volají `renderer.RenderAsync(parsed.Document!, model, ct)` **pozičně** — třetí argument je dnes
> `CancellationToken`. Po B4 (`…, TemplateRenderOptions? options = null, CancellationToken … = default`)
> se to nepřeloží; obě volání (a každé hostitelské poziční `ct`) musí použít `cancellationToken: ct`.

### 3.6 Bezpečnost sandboxu — MemberFilter, audit a limity (Fáze 2)

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

public enum TemplateSecurityEventKind
{
    BlockedMember,
    BlockedNamespace,
    BlockedFunction,
    BlockedUrlScheme,
    LimitExceeded,
    Timeout
}

/// <summary>
/// One audit event. TemplateHash = lowercase hex SHA-256 of the template; TemplateExcerpt = first
/// 200 chars of the template (MAY CONTAIN PII authored in the template — document retention);
/// Detail never contains variable VALUES, only member/namespace/scheme names.
/// </summary>
public sealed record TemplateSecurityEvent(
    TemplateSecurityEventKind Kind,
    string Detail,
    string TemplateHash,
    string TemplateExcerpt,
    DateTimeOffset Timestamp);

/// <summary>Optional audit sink. Registered zero-to-many; never required.</summary>
public interface ITemplateSecurityAuditSink
{
    void OnEvent(TemplateSecurityEvent securityEvent);
}
```

`ScribanTemplateEngine` (v balíčku **Engine**) — konstruktor:

```csharp
public ScribanTemplateEngine(
    TemplateSecurityOptions? options = null,
    IEnumerable<ITemplateSecurityAuditSink>? auditSinks = null,
    IEnumerable<ITemplateFunctionProvider>? functionProviders = null)
```

**Závazná pravidla sandboxu:**

- **MemberFilter není konfigurovatelný hostitelem** (žádný `AllowReflection` přepínač). Blokuje členy
  deklarované na `System.Type`, `MemberInfo`, `Assembly`, `Module`, typy z namespace
  `System.Reflection`, `System.IO`, `System.Diagnostics`, `System.Runtime*`, `System.Threading` a jména
  členů `GetType`, `GetMethods`, `GetMembers`, `Assembly`, `TypeHandle`, … na jakémkoli typu.
- `object.eval` / `object.eval_template` odstraněny; `include` bez `TemplateLoader` (fail).
- Sinky: `IEnumerable<ITemplateSecurityAuditSink>`; **výjimka sinku se spolkne a zaloguje** (nikdy
  neshodí render); volání jsou **synchronní a neblokující** (žádné I/O v renderu).
- Render je **fail-closed**: `javascript:`/`data:`/`vbscript:`/`file:` URL se nahradí `#` a emituje
  `BlockedUrlScheme`; zablokovaný člen/funkce = chyba renderu, ne prázdný výstup.
- `Timestamp` plní `TimeProvider` (testovatelné; default `TimeProvider.System`).
- `TemplateSecurityOptions` rozšíření: `RegexTimeOut`, `ObjectRecursionLimit`, `LimitToString`
  (bezpečné defaulty; záporné/nulové = `ArgumentOutOfRangeException` při registraci) a
  **`ISet<string> FunctionNamespaces`** — plněné registrací z `IEnumerable<ITemplateFunctionProvider>.Namespace`;
  validace toleruje neznámé členy jen v těchto namespacech (D6).

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `ITemplateSecurityAuditSink` + `TemplateSecurityEvent`/`…Kind` (6 hodnot) | ne (nové typy) | 2 | Volitelná závislost; bez registrace se nic neděje. |
| `TemplateSecurityOptions` rozšíření | ne (aditivní vlastnosti) | 2 | |

**Security scénáře jako testy (povinné v Fázi 2):** reflexe (`GetType`/`Assembly`), zakázané
namespace, `object.eval`, `include`, limity (loop/recursion/output/timeout/regex), sink (každý `Kind`
se spy sinkem), spolknutá výjimka sinku, `BlockedUrlScheme` pro `javascript:`/`data:`, parita obou
sanitizerů (viz 3.0). Každý scénář musí **před opravou prokazatelně selhat**.

### 3.7 Funkce, kultura a escapování (Fáze 3 + 4)

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Templating;

/// <summary>A function a provider exposes, for validation and docs.</summary>
public sealed record TemplateFunctionInfo(string Name, string? Description);

/// <summary>Host-supplied template functions under one namespace (never overrides a builtin).</summary>
public interface ITemplateFunctionProvider
{
    /// <summary>Namespace the functions are imported under, e.g. "app" (^[a-z][a-z0-9_]*$).</summary>
    string Namespace { get; }

    /// <summary>
    /// Function name → delegate. Delegates are SYNCHRONOUS; names match ^[a-z][a-z0-9_]*$; parameters
    /// and return types are string/bool/numeric/decimal/DateTime/DateTimeOffset/IEnumerable of those.
    /// Results run inside the same MemberFilter and limits as everything else.
    /// </summary>
    IReadOnlyDictionary<string, Delegate> GetFunctions();

    /// <summary>Optional documentation of the exposed functions (name + description).</summary>
    IReadOnlyList<TemplateFunctionInfo> Describe();
}
```

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

/// <summary>Per-render options: culture of formatters.</summary>
public sealed class TemplateRenderOptions
{
    /// <summary>
    /// Culture of formatters. null = (UseDocumentLanguageCulture ? culture from
    /// EmailTemplateDocument.Language : CultureInfo.InvariantCulture).
    /// </summary>
    public CultureInfo? Culture { get; init; }

    /// <summary>When true and Culture is null, the document language decides the culture.</summary>
    public bool UseDocumentLanguageCulture { get; init; }
}
```

**Escapování (BREAKING, rozhodnutí zadavatele č. 2):** režim escapování určuje **výhradně renderer
podle průchodu** — MJML/HTML tělo = `Html`, subject/preheader/textová verze = `None`.
`TemplateEscapeMode` je **`internal`** a **není** součástí veřejného `TemplateRenderOptions` (žádný
hostitelský přepínač, který by šel omylem vypnout). Jediný opt-out je filtr
**`{{ value | html.trusted }}`**, jehož výstup **VŽDY** projde `IHtmlContentSanitizer.Sanitize`
(allow-list tagů se tím neruší). Zdokumentováno v `docs/email-templates/TEMPLATE_SYNTAX.md`.
Escapuje se **výstup výrazu** (ne literální text šablony a ne model předem — jinak se rozbíjí filtry).

**Jedna signatura s volitelnými parametry (BREAKING B4, bez DIM):**

```csharp
// Abstractions (rozhraní)
public interface ITemplateEngine
{
    Result<string> Render(string template, object? model, TemplateRenderOptions? options = null);
    TemplateValidationResult Validate(string template);
    IReadOnlyList<string> ExtractVariables(string template);
}

public interface IEmailTemplateRenderer
{
    Task<RenderResult> RenderAsync(
        EmailTemplateDocument document,
        object? model = null,
        TemplateRenderOptions? options = null,
        CancellationToken cancellationToken = default);
}

// Nová rozhraní (implementace v Engine) pro editor bez enginu:
public interface ITemplateValidator               // nahrazuje přímé injectování ITemplateEngine v UI
{
    TemplateValidationResult Validate(string template);
}

public interface ITemplateVariableExtractor
{
    IReadOnlyList<TemplateVariableInfo> ExtractInfos(string template);
    IReadOnlyList<string> Extract(string template);
}
```

`ITemplateEngine.Render(template, model)` zůstává volatelné díky volitelnému parametru **jen na
zdrojové úrovni** pro nové kompilace; binárně jde o **jinou signaturu** → breaking B4 (žádná default
interface metoda, rozhodnuto).

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `ITemplateFunctionProvider` (+ `Describe()`, `TemplateFunctionInfo`) | ne (nový typ) | 4 | **Žádné Scriban typy v kontraktu** (delegáty, ne `ScriptObject`); resolvuje se jako `IEnumerable<ITemplateFunctionProvider>`. |
| `TemplateRenderOptions` (jen `Culture` + `UseDocumentLanguageCulture`) | ne (nový typ) | 3/4 | `Culture = null` má definovanou sémantiku výše. |
| `TemplateEscapeMode` | **internal** | 3 | Není veřejné — viz odstavec výše. |
| `ITemplateValidator`, `ITemplateVariableExtractor` | ne (nová rozhraní) | 6 | Abstrakce pro editor bez Engine. |
| Nahrazení `ITemplateEngine.Render`/`IEmailTemplateRenderer.RenderAsync` jednou signaturou s volitelnými parametry | **ANO** (B4) | 3 | Bez DIM; v repu implementují jen Tempo typy. |
| Výchozí HTML escapování těla + filtr `{{ value \| html.trusted }}` | **ANO** (B3) | 3 | CHANGELOG „Breaking / Migration“ + `TEMPLATE_SYNTAX.md`. |

### 3.7a Kontrola URL schémat a barev (Fáze 3 + 9)

**Allow-list schémat (hostitel ho NESMÍ rozšířit):** `http`, `https`, `mailto`, `tel`, relativní cesty
(`/path`, `path`, `./path`, `../path`) a `#` (fragment).

- Kontroluje se **po substituci** v kompilovaném HTML: `href`, `src`, `background`, `background-url`
  (a `poster`, pokud se objeví), a dále **modelové odkazy**, které se do HTML dostanou:
  `EmailTableCell.Href`, `EmailButtonBlock.Href`, `EmailImageBlock.Src`/`Href`,
  `EmailNavbarLink.Href`, `EmailSocialElement.Href`, `EmailCarouselImage.Src`/`Href`,
  `EmailHeroBlock.BackgroundUrl`, `EmailWrapperBlock.BackgroundUrl`.
- Jiné schéma (`javascript:`, `data:`, `vbscript:`, `file:`, `blob:`, neznámé) → hodnota se nahradí
  **`#`**, přidá se `RenderError` s lokalizačním klíčem (warning) a zapíše se audit
  `TemplateSecurityEventKind.BlockedUrlScheme`.
- Testy: `{{ url }}` = `javascript:alert(1)` v tlačítku, obrázku, navbar odkazu, social prvku,
  hero/wrapper pozadí a v buňce tabulky; whitelist povolí `mailto:`, `tel:`, relativní i `#`.
- **`EmailTableCell.BackgroundColor`** se validuje regexem barvy (`#rgb`, `#rrggbb`, pojmenovaná barva
  z bezpečné sady); neplatná hodnota se **zahodí** (atribut se nevygeneruje, `RenderError` warning) —
  nikdy se nepropíše do `bgcolor`/`style`.

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| Allow-list URL schémat + `BlockedUrlScheme` audit + `#` fallback | **ANO** (chování; součást B3) | 3 | Bezpečnostní pravidlo, ne konfigurace. |
| `EmailTableCell.BackgroundColor` validace | ne (součást nového pole, Fáze 9) | 9 | Neplatná barva = atribut se nevygeneruje. |

**Zpřesnění po review Fáze 3 (2026-10-20):**

- **Síťová cesta / UNC je odmítnutá** (`//evil.example.com/x`, `\\evil.example.com\share`,
  `/\evil.example.com`, `\t//evil.example.com`). „Relativní" v allow-listu znamená **bez autority**; jinak
  by e-mailový klient mohl resolvovat vzdálený SMB share (NTLM leak). `UrlSchemePolicy` proto po
  normalizaci překlopí `\` na `/` a hodnotu začínající `//` odmítne s pseudo-schématem `network-path`.
  Migrace pro hostitele: protokol-relativní `//cdn/...` piš jako `https://cdn/...` (CHANGELOG
  Breaking / Migration).
- **`cid:` zůstává mimo allow-list** — vědomé rozhodnutí podle kontraktu (D15). Inline `cid:` obrázky by
  byly **budoucí změna kontraktu**, ne tichá výjimka.
- **Kontroluje se i `srcset`** (kandidáti po `,`, URL = první token), `ping` (seznam po mezerách), `data`,
  `lowsrc`, `dynsrc`, `meta http-equiv="refresh"` (`content="0;url=…"`) a CSS `url(…)` uvnitř `<style>`
  (tj. i `EmailStyle.Css`). Character reference se dekódují před posouzením schématu: numerické
  libovolné délky s volitelným středníkem (`&#58`, `&#0000000058;`) i `&#x6A`/`&colon;`.
- **Event handlery nejsou výstup šablony:** guard odstraní z raw markupu všechny `on*` atributy a
  `srcdoc` (prohlížeč dekóduje hodnotu atributu před spuštěním JS, escapování tedy nestačí), vydá
  `RenderError` Warning s klíčem `render.markup.event_handler_removed` a audit `BlockedMember` s názvem
  atributu. Nekontroluje se, že by Mjml.Net nějaké `on*` generoval — negeneruje (golden test).
- **`html.trusted` patří do obsahu elementu, ne do atributu.** Sanitizer serializuje vlastním
  `QuoteSafeMarkupFormatter` (textové uzly i hodnoty atributů kódují `& < > " '`), takže hodnota
  v atributu nemůže atribut uzavřít; značkování v atributu je i tak chyba návrhu šablony.
- **Raw blok je opt-in k MARKUPu, ne ke spuštění kódu (rozhodnutí uživatele 2026-10-22, review Fáze 10
  U1, D29).** `MarkupGuard` **vždy** odstraňuje elementy `script`, `iframe`, `frame`, `frameset`,
  `object`, `embed`, `applet`, `base`, `form` a `portal` — bez ohledu na to, odkud markup přišel (raw
  blok, text blok, host-set HTML atribut, import) a **bez opt-in flagu**. Raw-text element (`script`) se
  odstraňuje **včetně obsahu** až po uzavírací tag (neuzavřený raw-text element vlastní zbytek
  dokumentu — fail-closed jako prohlížeč). Nález = Warning `render.markup.unsafe_element_removed` +
  audit `BlockedMember` s detailem jména elementu; `RenderResult.Success` zůstává `true` (D23 — okolní
  obsah se vykreslí). Důvod: inbox preview i webmail otevírají zprávu v dokumentu, který spouští skript,
  takže „autor to vložil do raw bloku“ není důvod vydat živý `<script>`; `<base>` přepisuje rozlišení
  všech relativních URL a `<form>` umí POSTovat vstup příjemce. Interaktivní obsah v e-mailu je mimo
  rozsah — odkaz na hostovanou stránku. Breaking pro raw blok, který takový element obsahoval
  (CHANGELOG „Breaking / Migration“, B17).

**Třetí kolo review Fáze 3 (2026-10-20) — doplněno:**

- **Skenování tagů je lineární tokenizer, ne regex.** `MarkupGuard` používal backtracking pattern, který
  byl kvadratický (10 KB `<a` ≈ 2,4 s, 40 KB ≈ 36 s, 100 KB minuty). Nahrazeno `HtmlTagTokenizer`
  (single-pass stavy podle WHATWG: tag name → before-attr-name → attr name (uvozovky a `<` jsou součást
  jména) → before-value → `"…"`/`'…'`/unquoted; komentáře, `<!…>`, `</…>`, `<?…>` se přeskakují; obsah
  `style`/`script`/`textarea`/`title` se nevrací jako tagy, CSS jde přes CSS guard). Nedokončený tag na
  konci vstupu ukončí sken a zbytek se zahodí (prohlížeč ho také zahodí) — fail-closed. Zbývající regexe
  v guardu mají `matchTimeoutMilliseconds: 2000`; `RegexMatchTimeoutException` → `RenderError` Error
  s klíčem `render.markup.too_complex` a `Html = ""`.
- **`Html = ""` při Error v kompilační/markup fázi:** nedostavěný nebo nezkontrolovaný markup se
  hostiteli nikdy nepředá (náhled ukáže chybu, ne obsah). Varování HTML zachovávají.
- **SMIL a meta refresh:** elementy `set|animate|animateMotion|animateTransform|discard` a celý
  `<meta http-equiv="refresh">` se odstraňují (Warning `render.markup.unsafe_element_removed`, audit
  `BlockedMember`) — prohlížeč aplikuje jejich `href`/`to`/`from`/`by` až po statické kontrole.
- **CSS normalizace:** komentáře a CSS escapy (`\72`…) se před kontrolou dekódují; `image-set(…)` a
  `@import` (URL i řetězec) se kontrolují jako `url(…)`; stylesheet, který i po normalizaci obsahuje
  odmítnutý cíl, se zahodí.
- **Falešné pozitivy:** `<img alt="a onerror=b">`, `title="href=javascript:x"` a `data-onclick` zůstávají
  beze změny (tokenizer neplete obsah uvozovek s atributy).
- **Tokenizer nikdy nepřeskakuje obsah, který prohlížeč může parsovat jako tagy:** komentáře se ukončují
  i abruptně (`<!-->`, `<!--->`, `--!>`), takže `<!`/`<?`/`</` se přeskakuje jen po první `>` a zbytek se
  skenuje znovu; obsah `style`/`script`/`textarea`/`title` se také skenuje (uvnitř `<svg>`/`<math>` se do
  rawtextu nepřepíná — `<svg><style><img onerror=…>` a `<svg><title><a href="javascript:…">` jsou mXSS).
  Rozsah obsahu `<style>` se páruje v témže lineárním průchodu (start tag ↔ `</style>`) pro CSS guard,
  takže linearita zůstává. Falešné pozitivy uvnitř komentářů/skriptů jsou vědomě přijaté (přísnější než
  prohlížeč).

**Provedeno ve Fázi 3 (stav implementace):** allow-list je jedna interní `UrlSchemePolicy`
(`http`/`https`/`mailto`/`tel` + relativní + `#`; schéma se čte jako v prohlížeči — tab/CR/LF se
odstraní kdekoliv, C0 znaky a mezery na okrajích se ignorují), používají ji `MarkupGuard` (přejmenovaný
`UrlAllowList`, protože od review řeší i event handlery) i `HtmlContentSanitizer` (D22). Kontrola běží **dvakrát**: nad substituovaným MJML před kompilací (to pokrývá
`background-url`, které kompilátor rozkopíruje do CSS `url(…)`, atributu `background` a VML
`<v:fill src>` uvnitř Outlook conditional commentu) a nad kompilovaným HTML (pokrývá `mj-raw`,
`mj-html-attributes` a hostitelské atributy). Obě fáze přepisují jen hodnotu atributu na `#`, dokument
se nereserializuje, takže běžný render zůstává bajtově shodný. Audit jde přes
`EmailTemplateRenderer(auditSinks: …)`, `Detail` nese **jen schéma**, nikdy URL. `cid:` se podle
kontraktu nepovoluje (starší checklist fáze ho uváděl; platí kontrakt).

#### Text pro CHANGELOG 2.10.0 — „Breaking / Migration“ (B3, hotovo ve Fázi 3)

> **Hodnoty proměnných v HTML těle se escapují (B3).** Tělo dřív substituovalo hodnoty modelu do MJML
> **až po** sanitizaci obsahu, takže `{{ name }}` s `<script>` (nebo jakýmkoli markupem) prošlo do
> zprávy doslova. Nyní se escapuje **výstup každého výrazu**; literální text šablony, filtry a model
> zůstávají nedotčené (`{{ name | string.size }}` měří data, ne entity). Subject, preheader a textová
> verze se neescapují (jsou to texty). Jediný opt-out je filtr `{{ value | html.trusted }}`, jehož
> výstup **vždy** projde `HtmlContentSanitizer` (allow-list tagů a atributů platí dál);
> `html.escape` vrací už zakódovaný markup, takže se nedvojnásobí.
>
> ```text
> before  {{ description }}                     data: <b>Hi</b>   →  <b>Hi</b>        (markup se aplikoval)
> after   {{ description }}                                        →  &lt;b&gt;Hi&lt;/b&gt;   (doslovný text)
>         {{ description | html.trusted }}                         →  <b>Hi</b>        (sanitizovaný markup)
> ```
>
> Migrace: `| html.trusted` přidej **jen** k hodnotám, které aplikace produkuje jako důvěryhodné HTML
> (rich-text pole). Prosté textové pole filtr nedostane.
>
> **URL s nepovoleným schématem se nahradí `#` (B3).** `javascript:`, `data:`, `vbscript:`, `file:`,
> `blob:` a neznámá schémata — i tatáž schémata propašovaná vloženým tabulátorem/novým řádkem — se do
> zprávy nedostanou, ať přijdou z výrazu šablony nebo z modelu (`EmailButtonBlock.Href`,
> `EmailImageBlock.Src`/`Href`, `EmailNavbarLink.Href`, `EmailSocialElement.Href`,
> `EmailCarouselImage.Src`/`Href`, `EmailHeroBlock.BackgroundUrl`, `EmailWrapperBlock.BackgroundUrl`,
> `EmailSection.BackgroundUrl`). Každý výskyt přidá `RenderError` (warning,
> `RenderKeys.BlockedUrlScheme`) a audit `BlockedUrlScheme`; allow-list hostitel nerozšiřuje.
> `http`/`https`/`mailto`/`tel`, relativní cesty a `#fragment` jsou beze změny.


### 3.8 Model a limity (Fáze 4 + 9 + 10)

Následující bloky jsou tři samostatné soubory (`Model/EmailTemplateDocument.cs`,
`Serialization/EmailTemplateSerializer.cs`, `Import/EmailImportOptions.cs`):

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Model;

public sealed class EmailTemplateDocument
{
    /// <summary>Schema version written by the serializer and required by migrations.</summary>
    public static int CurrentSchemaVersion => 1;      // vlastnost, NE const

    /// <summary>Schema version of the document; missing on read = 1.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
}

namespace Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

public static class EmailTemplateSerializer
{
    // Čtení verze bez plné deserializace (pro rozhodnutí o migraci / chybovou zprávu).
    public static int ReadSchemaVersion(string json);

    public static EmailTemplateDocument Deserialize(string json);   // migruje v -> CurrentSchemaVersion
    // Nové přetížení: limity včetně MaxNestingDepth (per-call kopie Options s MaxDepth)
    public static EmailTemplateDocument Deserialize(string json, EmailImportOptions? options);
    public static string Serialize(EmailTemplateDocument document); // vždy zapisuje CurrentSchemaVersion
    // … stávající členy beze změny
}

namespace Tempo.Blazor.EmailTemplates.Abstractions.Import;

public sealed class EmailImportOptions
{
    // 5 MB = DNEŠNÍ chování, aditivní. Měří se jako UTF-8 byte count vstupu (ne počet znaků).
    public long MaxInputBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxSections { get; set; } = 200;
    public int MaxBlocks { get; set; } = 2000;
    public int MaxNestingDepth { get; set; } = 8;
}

// Import s explicitními limity (stávající volání zůstávají funkční)
public sealed class MjmlImporter
{
    // Stávající člen (includeResolver) zůstává; limity jsou nový volitelný parametr.
    public ImportResult Import(string mjml, IMjmlIncludeResolver? includeResolver = null, EmailImportOptions? options = null);
}
```

```csharp
namespace Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;

public sealed class EmailTableCell
{
    public string Text { get; set; } = string.Empty;
    public string? Align { get; set; }
    public int? ColSpan { get; set; }
    public int? RowSpan { get; set; }
    public string? Href { get; set; }               // NOVÉ (Fáze 9) — kontrolováno allow-listem 3.7a
    public string? BackgroundColor { get; set; }    // NOVÉ (Fáze 9) — validováno regexem 3.7a
}
```

> **`SchemaVersion` zůstává `1` (Fáze 9, D18 — zdůvodnění).** `Href` a `BackgroundColor` jsou
> **volitelná** pole s `null` výchozím stavem a serializér je zapisuje jen když jsou nastavená
> (`JsonIgnoreCondition.WhenWritingNull`), takže **staré JSON bez nich se načte** (oba zůstanou `null`) a
> **nové JSON načte i klient s předchozí verzí** modelu (neznámé property zahodí). Nic se nepřejmenovalo
> ani nezměnilo typ, žádná hodnota se nedopočítává — migrační krok `v → v+1` by neměl co dělat, a bump by
> jen zbytečně odmítl dokumenty, které jsou po drátě kompatibilní. Proto `CurrentSchemaVersion => 1`
> a round-trip je krytý testem (`TableCellsSecurityTests.Serializer_RoundTripsTheNewFieldsWithoutASchemaBump`,
> `Deserialize_LoadsAJsonWithoutTheNewFields`).

> **Rozhodnutí `EMAIL-IMPORT-JSON-DEPTH`** (Fáze 4, 2026-10-20): `MaxNestingDepth` počítá vnoření
> **modelu**, `JsonSerializerOptions.MaxDepth` počítá úrovně JSON. Doslovné `MaxDepth = MaxNestingDepth`
> by odmítlo každý dokument s tabulkou, proto `Deserialize(json, options)` používá per-call kopii
> `Options` s `MaxDepth = clamp(14 + 6·MaxNestingDepth, 14, 10 000)` (6 = nejdražší kontejner, wrapper)
> a prosté `Deserialize(json)` zůstává na `max(64, odvozená výchozí)`. Invarianta hlídaná testem:
> dokument, který projde `EmailDocumentLimitsValidator`, se vždy deserializuje. Budoucí migrační kroky
> schématu poběží nad `JsonNode` před typovou deserializací.

Předávání limitů do UI: **`[Parameter] public EmailImportOptions? ImportOptions { get; set; }`** na
`TmEmailTemplateEditor` **i** `TmEmailImportDialog` (`null` = výchozí `new EmailImportOptions()`);
editor ji předá dialogu. JSON deserializace použije per-call kopii `EmailTemplateSerializer.Options`
s `MaxDepth = MaxNestingDepth` (statická `Options` zůstává beze změny).

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `EmailTemplateDocument.SchemaVersion` (+ `static CurrentSchemaVersion`) | ne (aditivní pole) | 4 | Mění výstup `Serialize` (nová vlastnost); migrace `v → Current`; **vyšší než podporovaná = `EmailTemplateSerializationException`**. |
| `EmailTemplateSerializer.ReadSchemaVersion(string json)` | ne (aditivní) | 4 | Pro čitelnou chybu a rozhodnutí o migraci. |
| `EmailImportOptions` (`MaxInputBytes` = **5 MB**) | **ne** (aditivní, zachovává dnešní chování) | 4 | Dnešní natvrdo 5 MB (`TmEmailImportDialog.razor:63`) zůstává výchozím; hostitel může jen zvýšit/snížit. **Nezpřísňovat.** |
| `ImportOptions` parametr (editor + import dialog) | ne (aditivní) | 10 | `null` = default; `MaxInputBytes` se měří jako **UTF-8 byte count** vstupu. |
| `EmailTemplateSerializer.Deserialize(json, EmailImportOptions?)` | ne (nové přetížení) | 4 | `MaxNestingDepth` jde tudy (per-call kopie `Options` s `MaxDepth`); stávající `Deserialize(json)` zůstává. |
| `EmailTableCell.Href` / `BackgroundColor` | ne (aditivní pole) | 9 | Round-trip `MjmlGenerator`↔`MjmlImporter`; `TextVersionGenerator` vypíše „text (url)“; `SchemaVersion` zůstává 1. |
| `MjmlGenerator.Generate(document, options, diagnostics)` | ne (nové přetížení) | 9 | `ICollection<RenderError>?` sbírá varování, která markup guard nevidí (barva buňky mimo allow-list). Stávající `Generate(document, options)` i `Generate(document)` zůstávají beze změny. |
| `RenderKeys.InvalidTableCellColor` (`render.table.invalid_color`) | ne (aditivní konstanta) | 9 | `Warning`: buňka nesla `bgcolor` mimo allow-list, atribut se nezapsal. Zdroje v `TmEmailResources*.json`. |
| `ImportKeys.BlockedTableLink` (`import.table.blocked_link`), `ImportKeys.InvalidTableColor` (`import.table.invalid_color`) | ne (aditivní konstanty) | 9 | Import upozorní na odkaz mimo allow-list (hodnota zůstává v modelu k opravě) a na zahozenou barvu buňky. |
| `EmailUrlPolicy` (`BlockedUrlPlaceholder`, `FindBlockedScheme(string?)`, `IsAllowed(string?)`) | ne (aditivní statický typ) | 9 | Veřejný read-only pohled na **jediný** allow-list z §3.7a (`UrlSchemePolicy` zůstává `internal`); hostitel/UI vysvětlí odmítnutý odkaz **před** renderem. Nerozšiřitelný (žádná registrace, žádné rozšíření seznamu). |
| `EmailColorPolicy.IsAllowed(string?)` | ne (aditivní statický typ) | 9 | Veřejný read-only pohled na allow-list barev **atributu** `bgcolor` (`#rgb`/`#rrggbb`/pevná množina jmen). MJML atributy (navbar, akordeon, social) se validují pravidlem inspektoru `PropertyReflection.IsValidColor` (hex, `rgb()`/`hsl()`, klíčové slovo), protože se zapisují do stylu elementu, ne do atributu, který by mohl uniknout z markup. |
| `ValidationKeyShape` (`TitleSuffix` = `_Title`, `DescriptionSuffix` = `_Description`, `TitleOf(string)`, `DescriptionOf(string)`) | `public static class` — jediné místo, které skládá prezentační klíče pravidla (`validation.*_Title` / `*_Description`); suffixy záměrně NEjsou lokalizační klíče, takže je `TmEmailResourcesParityTests` nescamuje | ne (aditivní statický typ) | 10 |
| `ImportKeys.BlockedLink` (`import.blocked_link`), `ImportKeys.InvalidColor` (`import.invalid_color`), `ImportKeys.DroppedAttribute` (`import.dropped_attribute`) | `const string` — audit importu: odkaz mimo allow-list (hodnota zůstává v modelu k opravě), barva, kterou zpráva nemůže nést (detail = PROPERTY), a atribut, který pipeline nikdy nezapíše (detail = TAG + JMÉNO, nikdy hodnota) | ne (aditivní konstanty) | 10 |
| `EmailTemplateMapper.ToDocument(string contentJson, EmailImportOptions? options)` + `ApplyCreate(CreateEmailTemplateRequest, EmailImportOptions?)` + `ApplyUpdate(UpdateEmailTemplateRequest, EmailImportOptions?)` | nová přetížení vedle stávajících bezlimitních; server tak vynutí stejné limity jako import (`MaxInputBytes`, sekce/bloky, hloubka) | ne (nová přetížení) | 10 |
| `ReportEmailTemplateGalleryStore(EmailImportOptions? importOptions)` | aditivní ctor vedle bezparametrického; galerie reportů aplikuje stejné limity jako server, takže „co projde importem, projde i serverem“ platí i pro JSON z klienta | ne (aditivní ctor, bezparametrický zůstává) | 10 |

### 3.9 Editace položek bloků (Fáze 9)

```csharp
namespace Tempo.Blazor.EmailTemplates.Components;

public enum EmailItemEditingMode { Inline, Dialog }

// na TmEmailTemplateEditor i na TmEmailPropertyPanel:
[Parameter] public EmailItemEditingMode ItemEditingMode { get; set; } = EmailItemEditingMode.Inline;
```

| Položka | Breaking | Fáze | Poznámka |
|---|---|---|---|
| `EmailItemEditingMode` + `ItemEditingMode` | ne (aditivní, default `Inline` = dnešní chování) | 9 | `Dialog` = modální editor nad KOPIÍ položky s Uložit/Zrušit, jeden krok undo, fokus zpět na spouštěcí tlačítko. |
| `EmailSocialNetworks` (`Supported`, `IsSupported(string?)`) | ne (aditivní statický typ) | 9 | Názvy `mj-social-element` s vlastní ikonou Mjml.Net; „vlastní“ = povinné `Src`. |
| `EmailItemValidationKeys` (`UrlScheme`, `TextRequired`, `HrefRequired`, `SrcRequired`, `AltRequired`, `TitleRequired`, `InvalidColor`, `InvalidSpan`, `ValidatorMissing`) | ne (aditivní statický typ) | 9 | Klíče lokalizace dialogů v tabulce balíčku; `ValidatorMissing` je fail-closed stav (bez registrace validátoru nelze uložit). |
| `EmailSocialElementValidator`, `EmailNavbarLinkValidator`, `EmailCarouselImageValidator`, `EmailAccordionItemValidator`, `EmailTableCellValidator` (+ `EmailTableCellValidator.MaxSpan = 20`) | ne (aditivní veřejné typy) | 9 | **Veřejné zůstávají** (rozhodnutí `EMAIL-F9-PUBLIC-POLICIES`): kontrakt je zde jmenuje jako veřejné aditivní typy a hostitel potřebuje stejnou odpověď jako editor, aby odmítnutou položku vysvětlil **před** renderem (stejný důvod jako u veřejných politik); `AddTempoFluentValidation` je registruje přes `assembly.GetTypes()`, takže `internal` by fungovalo, ale připravilo by hostitele o tuto možnost. |
| `TmEmailItemList`, `TmEmailItemDialog`, `TmEmailTableCellDialog` | ne (aditivní veřejné komponenty) | 9 | Kompaktní seznam (`Preview`/`Block`/`OnChanged`/`ReadOnly`/`OnImageUpload`/`ImageUpload`), dialog šablona (`Show`/`Title`/`Errors`/`OnSave`/`OnCancel`/…), dialog buňky (`Block`/`RowIndex`/`CellIndex`/`Show`/`ReadOnly`/`OnSave`/`OnCancel`). `Errors` nese jen chyby **bez pole** (souhrn `role="alert"`); chyba pole se renderuje u pole (`aria-invalid` + `aria-describedby`). |
| `TmTextInput.FocusAsync(bool preventScroll = false)` (jádro, aditivní) | `ValueTask` | ne (aditivní člen) | Dialog potřebuje fokus na první neplatné pole; komponenta vlastní element, takže konzument se k němu jinak nedostane (stejný vzor jako `TmSearchInput.FocusAsync`). |

**Provedeno ve Fázi 9 (stav implementace):** `ItemEditingMode` je na `TmEmailTemplateEditor` i
`TmEmailPropertyPanel` a v režimu `Dialog` renderuje `TmEmailItemList` (kompaktní seznam s náhledem a
akcemi přesun/upravit/odebrat) + `TmEmailItemDialog` (jeden jádrový `TmModal` s
`MobilePresentation="Auto"`, tedy na mobilu sheet se sticky patičkou — **žádný lokální sheet**).
Validace jde přes `Tempo.Blazor.FluentValidation` (`EmailSocialElementValidator`,
`EmailNavbarLinkValidator`, `EmailCarouselImageValidator`, `EmailAccordionItemValidator`,
`EmailTableCellValidator`), zaregistrované v `AddTempoEmailTemplateEditor`; lokalizované klíče
`EmailEditor_Item_*`/`EmailEditor_TableCell_*`/`EmailEditor_Table_*` jsou ve všech čtyřech tabulkách.
Nové veřejné (aditivní) typy: `EmailItemEditingMode`, `EmailSocialNetworks`, `EmailItemValidationKeys`,
validátory, a v Abstractions `EmailUrlPolicy` + `EmailColorPolicy` (veřejný read-only pohled na allow-list
z §3.7a, aby UI mohlo odmítnutou hodnotu vysvětlit **před** renderem). Buňka tabulky má navíc dialog
(`TmEmailTableCellDialog`) dostupný z každé buňky mřížky v obou režimech.

**Review F9 (UX + code/arch) — čím se implementace liší od prvního provedení:** Uložit je vždy
aktivní (zakázané tlačítko nevysvětlí, co je špatně): neplatné odeslání nic neuloží, zobrazí chybu
u pole (`aria-invalid`, `aria-describedby`) a fokus na první neplatné pole; chyby se neukazují dřív
než po zásahu do pole nebo po pokusu o uložení; souhrn `role="alert"` nese jen chyby bez pole. Nová
položka je do Uložit jen kopií v dialogu (`_isNew`), takže Zrušit nemění dokument ani historii undo.
Akce řádku mají v přístupném názvu název položky (`EmailEditor_ItemList_{MoveUp,MoveDown,Edit,Remove}`
s `{0}`, fallback `EmailEditor_ItemList_Unnamed`), prázdný seznam má vlastní stav
(`EmailEditor_ItemList_Empty`) a smazání se ohlašuje v live regionu (`EmailEditor_Item_Removed`).
Obsah akordeonu i textového bloku se sanitizuje **před** předáním rich editoru (fail-closed: bez
`IHtmlContentSanitizer` prázdný řetězec, nikdy surový obsah). Připnutý sloupec mřížky je kompaktní
(přepínač jen v `aria-label`, lišta `row`, koš v `--tm-color-danger-text` — token
`--tm-color-danger-strong` v design systému neexistuje), caption je `sr-only`.

### 3.10 Veřejná plocha z vizuálního redesignu (Fáze 6/7/7A/8/10)

| Položka | Typ / signatura | Breaking | Fáze |
|---|---|---|---|
| `EmailPreviewDevice` | `enum { Desktop, Tablet, Mobile }` — nahrazuje `bool _mobile` v `InitialDevice` | ne (nikdy nevydaný `bool`) | 6 |
| `TmEmailTemplatePreview.CultureName` | `[Parameter] public string? CultureName { get; set; }` | ne (aditivní) | 6 |
| `EmailPreviewResult.Unavailable` | `public static EmailPreviewResult Unavailable(IReadOnlyList<RenderError> errors)` | ne (aditivní) | 6 |
| `IHtmlContentSanitizer.SanitizeTemplateContent` | `public string SanitizeTemplateContent(string content)` | ne (aditivní) | 6 |
| `ShowViewToggle` | `[Parameter] public bool ShowViewToggle { get; set; } = true;` | ne | 6 |
| `FooterActions` | `[Parameter] public RenderFragment? FooterActions { get; set; }` | ne | 6 |
| `TemplateVariableInfo.Description` + `Group` | `public sealed record TemplateVariableInfo(string Path, VariableKind Kind) { public string? Description { get; init; } public string? Group { get; init; } }` (bez `SampleValue`) | ne (aditivní `init` vlastnosti; **mění rovnost záznamu** — `Equals`/`GetHashCode` je zahrnují) | 7A |
| `LayoutSeverity.Info` | `enum LayoutSeverity { Warning = 0, Error = 1, Info = 2 }` — nová hodnota `Info = 2`, existující `Warning = 0` a `Error = 1` **explicitně zafixované beze změny číselných hodnot**; řazení přes `LayoutSeverityRank.Of(severity)` (`Error` → 0, `Warning` → 1, `Info` → 2), nikdy podle číselné hodnoty | **částečně ANO** (breaking jen pro exhaustivní `switch` bez `default`) | 10 |
| `LayoutMode` / `InitialMode` / `ResolvedLayoutChanged` / `Breakpoints` / `MobilePanelPresentation` | viz [3.3](#33-rozvržení-a-responzivita-tmeditorshell-ze-základu-f6-rozhodnutí-em2aem3) — mapované 1:1 na `TmEditorShell` | ne | 8 |
| `ToolboxCollapsed` / `ToolboxCollapsedChanged` | `bool` + `EventCallback<bool>` | ne | 8 |
| `ShowSaveStatus` | `[Parameter] public bool ShowSaveStatus { get; set; } = true;` | ne | 7 |
| `ReadOnly` (aditivní na sub-editorech) | `[Parameter] public bool ReadOnly { get; set; }` na `TmEmailTemplateToolbox`, `TmEmailTemplateCanvas`, `TmEmailPropertyPanel`, `TmEmailObjectFields`, `TmEmailListEditor`, `TmEmailKeyValueEditor`, `TmEmailTableEditor`, `TmEmailHtmlAttributesEditor`, `TmEmailClassesEditor`, `TmEmailVariablePicker` — každá mutující metoda má `if (ReadOnly) return;` vedle nativního `disabled` | ne (aditivní, default `false`) | 7 |
| `ImageUpload` na `TmEmailPropertyPanel` | `[Parameter] public ImageUploadOptions? ImageUpload { get; set; }` (panel validuje **před** `OnImageUpload`) | ne (aditivní) | 7 |
| `ImportOptions` | `[Parameter] public EmailImportOptions? ImportOptions { get; set; }` (editor + import dialog + `TmEmailValidationPanel`) | ne | 10 |
| `LayoutMode` na obou dialozích | `[Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;` na `TmEmailExportDialog` i `TmEmailImportDialog` — editor předává už resolvovaný režim, takže telefon dostane jádrový sheet z F2 bez druhého měření | ne (aditivní parametry) | 10 |
| `JsonStringLocalizer<T>` | `public sealed class JsonStringLocalizer<TResourceSource> : IStringLocalizer<TResourceSource>` — embedded JSON tabulky (`{Marker}.json` + `{Marker}.{culture}.json`), registruje se jako CLOSED generic | ne (aditivní, nový typ; žádný existující člen se nemění) | 5 |
| První doporučující pravidlo | `LayoutSeverity.Info` = preheader delší než 100 **grafémů** (`StringInfo.LengthInTextElements`) měřených ve **zdroji šablony včetně tokenů** — inbox zkracuje zobrazený text, takže rozhoduje počet viditelných znaků, ne UTF-16 jednotek; tokeny se počítají jako napsané (jedno pravidlo, `DocumentValidationKeys`) | ne (nové pravidlo) | 10 |
| `PropertyFieldPlacement` | `public enum PropertyFieldPlacement { Content, Style, Padding, Advanced }` — kam pole patří v inspektoru (Obsah/Styl, sbalitelná skupina Odsazení/Pokročilé) | ne (aditivní, nový typ; `PropertyField.Placement` a `TmEmailObjectFields.Placement`) | 7A |
| `PropertyChoice` | `public sealed record PropertyChoice(string Value, string LabelKey)` — jedna volba pole `Choice`/`Alignment`: uložená MJML hodnota + lokalizační klíč popisku | ne (aditivní, nový typ; `PropertyField.Choices`) | 7A |
| `PropertyFieldKind` (nové hodnoty) | `Color`, `Alignment`, `Choice`, `Length` připojené za `Text`/`MultilineText`/`Bool`/`Number` (existující hodnoty si drží čísla 0–3); mapování výhradně tabulkou `PropertyReflection` | ne (aditivní hodnoty; `switch` bez `default` je jediný zdrojový dopad) | 7A |
| `PropertyField` (rozšíření 7A) | `Owner`, `Placement`, `Choices`, `SupportsTemplate`, `AllowsNegative` (jediná výjimka: `LetterSpacing`), `LabelKey` (`EmailEditor_Field_{Owner}_{Property}`), `SharedLabelKey` (`EmailEditor_Field_{Property}`), `IsNullable()` (nullable anotace = prázdná hodnota je „unset“), `SetChoice` (neznámý token vyhodí `ArgumentException`), `TryGetLength`/`SetLength` a čtecí/zápisové `GetText`/`SetText`/`GetBool`/`SetBool`/`GetNumber`/`SetNumber` | ne (aditivní členy; `internal` ctor zůstává) | 7A |
| `PropertyReflection` (rozšíření) | `GetFields(object, PropertyFieldPlacement)`, `OwnerOf(object)`, `TryParseLength`, `FormatLength`, `IsValidColor` — jediná tabulka mapování vlastností na druh editoru; `PropertyReflection.IsRequiredLength` byl zúžením API odstraněn (ptej se `PropertyField.IsNullable()`) | ne (aditivní statické členy) | 7A |
| `EmailInspectorState` | `public sealed class EmailInspectorState` — stav inspektoru po dobu života editoru: sbalené skupiny **per typ bloku** (`IsGroupCollapsed`/`SetGroupCollapsed`/`ToggleGroup`, konstanty `PaddingGroup`/`AdvancedGroup`) a `LastFocusedField` (cíl fallbacku vkládání proměnné). Veřejný, protože je **typem parametru** veřejné komponenty `TmEmailObjectFields.State` | ne (aditivní, nový typ; samostatná instance si drží vlastní stav) | 7A |
| `TmEmailObjectFields` (7A) | `Placement` (`PropertyFieldPlacement?`), `State` (`EmailInspectorState?`), `Variables` (`IReadOnlyList<TemplateVariableInfo>`); `Primary` zůstává jen jako řadicí hint | ne (aditivní parametry) | 7A |
| `TmEmailPropertyPanel` (7A) | `OnDuplicate` / `OnDelete` (`EventCallback<Guid>`) — akce hlavičky inspektoru, renderované jen když je hostitel obsluhuje (editor je mapuje na jeden krok undo) | ne (aditivní parametry) | 7A |
| `TmEmailTemplateToolbox` (7A) | `Collapsed` / `CollapsedChanged` (`bool` + `EventCallback<bool>`) — paleta jako rail s hledáním; `ShowCollapseToggle` (`bool`, default `true`) — host, který vlastní chrome shellu (F8 mapuje stav na `TmEditorShell.CollapsedPanels`), si šipku vypne, aby stejný stav neměl dva ovládací prvky; hledání zůstává použitelné i v `ReadOnly` (filtrování není editace, dlaždice jsou inertní); `ToolboxCollapsed` na editoru zůstává API (D10) | ne (aditivní parametry) | 7A |
| `LayoutSeverityRank` | `public static class LayoutSeverityRank { public static int Of(LayoutSeverity severity) }` — `Error` → 0, `Warning` → 1, `Info` → 2; jediné povolené řazení nálezů (číselné hodnoty enumu jsou serializační kontrakt) | ne (aditivní, nový typ) | 7A |
| `DocumentValidationKeys.PreheaderTooLong` + `EmailDocumentValidator.MaxPreheaderLength` | `const string` = `validation.preheader.too_long` + `public const int MaxPreheaderLength = 100` — práh, který pravidlo měří v grafech zdrojového textu (včetně tokenů) a který zpráva jmenuje; formátuje se jedním místem (`ValidationMessageText.Of`), aby se `{0}` nikdy nevypsalo doslova | ne (aditivní konstanty) | 7A |

| `TmEmailCanvasBlock` (Fáze 9A) | `public partial class TmEmailCanvasBlock : ComponentBase` — `Block` (`EmailBlockBase`, `EditorRequired`), `CanvasImageLoading` (`CanvasImageLoading`, default `Load`), `Nested` (`bool`, default `false`). Veřejná **proto, že ji Razor neotevře z markupu, je-li `internal`** (`RZ10012` je v tomto repu chyba, viz poučení „Razor cannot open an internal component from a markup tag“); `Nested` je jen pro vnoření (wrapper/hero/group) a nese `[data-tm-nested-block]` místo `[data-tm-block-body]` | ne (aditivní veřejná komponenta) | 9A |
| `CanvasImageLoading` (Fáze 9A) | `public enum CanvasImageLoading { Load = 0, Placeholder = 1 }`, default `Load` — `Placeholder` nenačítá externí obrázky (soukromí / tracking pixel) a maluje rámeček s alt textem. Na `TmEmailTemplateEditor` i `TmEmailTemplateCanvas` | ne (aditivní, default = dnešní chování) | 9A |

### 3.11 Souhrn breaking changes (pro CHANGELOG „Breaking / Migration“ 2.10.0)

| # | Breaking | Před → Po | Fáze |
|---|---|---|---|
| B1 | **ANO** | `EventCallback<EmailTemplateDocument> OnSave` → `Func<EmailSaveRequest, Task<EmailSaveResult>>? OnSave` (request nese `Trigger`, `Revision`, `CancellationToken`); `OnAutoSave` zrušen (`Trigger = AutoSave`); `SaveAsync()` veřejné | 7 |
| B2 | **ANO** | `TmEmailTemplatePreview` injektuje `IEmailPreviewRenderer` místo `IEmailTemplateRenderer` | 6 |
| B3 | **ANO** | Hodnoty proměnných v HTML/MJML těle se ve výchozím stavu escapují (subject/preheader/text ne); jediný opt-out je filtr `{{ value \| html.trusted }}` (výstup vždy přes sanitizer); allow-list URL schémat + `#` fallback a audit `BlockedUrlScheme` | 3 |
| B4 | **ANO** | `ITemplateEngine.Render` / `IEmailTemplateRenderer.RenderAsync` mají **jednu signaturu s volitelnými parametry** (`TemplateRenderOptions?`, `CancellationToken`) — binárně jiná signatura, **bez default interface metody** | 3 |
| B5 | **ANO** (částečně) | `LayoutSeverity`: nová hodnota `Info = 2`, existující `Warning = 0` a `Error = 1` explicitně zafixované **beze změny číselných hodnot**; breaking jen pro exhaustivní `switch` bez `default` | 10 |
| B6 | **ANO** (částečně) | Lokalizace: `.resx` → embedded JSON, `AddTempoEmailTemplates` už nevolá `AddLocalization()`; **+ nový public `JsonStringLocalizer<T>`** (aditivní, D25) | 5 |
| B7 | **ANO** | **Nový balíček `Tempo.Blazor.EmailTemplates.Engine`**: `AddTempoEmailTemplateEngine` se přesunul do něj (namespace `Tempo.Blazor.EmailTemplates.Abstractions` — D21), `AddTempoEmailTemplates` je `[Obsolete]` a jen deleguje na `AddTempoEmailTemplateEditor(o => configure?.Invoke(o.Security))` (engine neregistruje; náhled bez rendereru = `Unavailable` s textem o chybějící registraci), `HtmlContentSanitizer.Sanitize` (static) → `IHtmlContentSanitizer` (`AngleSharpHtmlContentSanitizer` v Engine, konzervativní fallback v Abstractions), `TemplateVariableExtractor` → `ITemplateVariableExtractor`/`ScribanTemplateVariableExtractor`, `ITemplateEngine` v UI nahrazen `ITemplateValidator`. **Migrace:** přidat `PackageReference Tempo.Blazor.EmailTemplates.Engine` a volat `AddTempoEmailTemplateEngine()`; WASM editor místo toho dodá `IEmailPreviewRenderer` (+ volitelně serverový `ITemplateValidator`) | 6 |
| B8 | ne, ale mění výstup | `EmailTemplateDocument.SchemaVersion` v serializovaném JSON (`static CurrentSchemaVersion => 1`) | 4 |
| B9 | ne, ale mění rovnost | `TemplateVariableInfo.Description`/`Group` v `Equals`/`GetHashCode` | 7A |
| B10 | ne (chování) | Tlačítko „MJML“ → „Export“ (viditelný text; `data-tm-mjml-btn` zůstává kvůli E2E) | 7/10 |
| B11 | ne (chování) | Popisky polí panelu vlastností jsou nově lokalizované (dřív `Humanize` z názvu vlastnosti) — hostitelské testy na anglické texty musí upravit selektory | 7A |
| B12 | ne (a11y/UX) | Import/export: `TmModal.Footer` (Zrušit/Importovat, Zavřít/Stáhnout), `TmCodeEditor` místo `<textarea>`, limity z `EmailImportOptions` (výchozí 5 MB = dnešní chování) | 10 |
| B13 | ne | Validace: nálezy jako `<button>`, souhrn počtů, titulek+popis, `LayoutSeverity.Info` + pravidlo preheaderu | 10 |
| B14 | **ne** (explicitně) | `EmailImportOptions.MaxInputBytes` = **5 MB** (dnešní hodnota) — zpřísnění by bylo breaking, proto se nedělá | 4 |
| B15 | ne (aditivní) | **Skutečné overloady, stávající signatury zůstávají:** `MjmlGenerator()` (bezparametrický ctor) + nový `MjmlGenerator(IHtmlContentSanitizer sanitizer)`; `EmailDocumentVariableExtractor.Extract(document)` + nový `Extract(document, ITemplateVariableExtractor extractor)`; `MjmlImporter.Import(mjml, IMjmlIncludeResolver? includeResolver = null)` + nový `Import(mjml, IMjmlIncludeResolver? includeResolver, EmailImportOptions? options)`; `EmailTemplateSerializer.Deserialize(json)` + nový `Deserialize(json, EmailImportOptions? options)` | 3/4/6 |
| B16 | **ANO** (DOM plátna) | **WYSIWYG plátno (F9A):** `.tm-email-canvas-block__kind` / `.tm-email-canvas-block__detail` zrušeny (obsah bloku se maluje jako e-mail, ne jako „typ + text“); blok nese `[data-tm-block-body][data-tm-block-kind]` uvnitř wrapperu `[data-tm-block-id]`; `aria-selected` → `aria-current="true"` a **roving tabindex** (`0` = vybraný/první blok); `aria-label` bloku = lokalizovaný typ **+ stručný obsah** (`EmailEditor_Canvas_BlockLabel`, prázdný `EmailEditor_Canvas_BlockLabelEmpty`); akční lišta vybraného bloku je na `(pointer: coarse)` **v toku** (ne hover overlay) s `--tm-touch-target`, na desktopu plave NAD blokem; dokument maluje `Styles.BackgroundColor`/`FontFamily` na papír | 9A |
| B17 | **ANO** (raw blok, U1/D29) | **Raw blok už nepropustí spustitelný markup:** `MarkupGuard` vždy odstraňuje `script`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `base`, `form`, `portal` (raw-text element včetně obsahu) s Warning `render.markup.unsafe_element_removed` + auditem `BlockedMember`; dřív raw blok (`mj-raw`) prošel do exportu i HTML doslova. Migrace: interaktivní obsah v e-mailu nahraďte odkazem na hostovanou stránku | 10 |


**Pravidlo:** každá položka B1–B7 musí mít v `CHANGELOG.md` (sekce `## 2.10.0` → „Breaking / Migration“)
popis dopadu **a ukázku před/po** (jak migrovat). Nedokumentovaná breaking change = vadná implementace.

---

## 4. Politika breaking changes

1. **Balíčky `Tempo.Blazor.EmailTemplates`, `Tempo.Blazor.EmailTemplates.Abstractions` a nový
   `Tempo.Blazor.EmailTemplates.Engine`: breaking changes jsou POVOLENÉ.** Rozhodnutí zadavatele
   (bod 5 původních rozhodnutí): komponentu zatím žádná aplikace nepoužívá (PostrionCore P06 je gate
   `after-external-pipeline` a čeká na 2.10.0). Každá breaking change ale **musí** mít položku
   v `CHANGELOG.md`, sekce `## 2.10.0` → „Breaking / Migration“, s popisem dopadu a ukázkou před/po.
2. **Jádro `Tempo.Blazor` a ostatní balíčky tento plán NEMĚNÍ.** Potřebné jádrové primitivy
   (`TmEditorShell`, `TmLayoutMode`, `TmLayoutBreakpoints`, `MobilePanelPresentation`, `EditorShellPanel`,
   mobilní prezentace `TmModal.MobilePresentation` / `TmDrawer` (`DrawerPosition.Bottom`) / `TmSidePanel`,
   `TmKeyboardShortcutsHelp.Description`) **dodává základový plán `faf503eb-bf08-4f5f-8c33-08df2362aa32`** —
   ve stromu už jsou (viz [3.1](#31-jádrové-api-na-kterém-kontrakt-stojí-ověřeno-ve-stromu-dodal-základový-plán-faf503eb)).
   Pokud by Fáze 8/10 potřebovala jádrovou změnu, platí app-level pravidlo **G5**: breaking změny jsou povolené
   v celé aplikaci, ale vždy s CHANGELOG „Breaking / Migration“ a ukázkou před/po; preferuj aditivní řešení.
3. **Preferuj aditivní řešení.** Kde aditivní varianta neztrácí kvalitu (nový parametr s bezpečným defaultem,
   nová hodnota vedle stávající), použij ji místo změny stávajícího členu (příklady: `EmailItemEditingMode.Inline`
   zůstává default, `ReadOnly` default `false`, `EmailImportOptions.MaxInputBytes` zůstává 5 MB,
   `MjmlImporter.Import(mjml, options = null)`).
4. **Balíčková hranice je součást kontraktu.** Nový balíček `Tempo.Blazor.EmailTemplates.Engine` nesmí
   z Abstractions udělat závislost na Scriban/Mjml.Net/AngleSharp: `grep PackageReference` v Abstractions
   = jen BCL/`System.Text.Json`/`FluentValidation`/`Microsoft.Extensions.*`. UI balíček nesmí referencovat
   Engine (jinak se Mjml.Net vrátí do WASM). Hlídá test/CI kontrola (Fáze 6 a 12).
5. **Kontrakt z Fáze 1 je závazný pro PostrionCore P06** a je **potvrzen uživatelem 2026-10-20** (viz 5).
   Odchylka v názvech/typech veřejného API = konzultace s uživatelem + zápis decision (`upsert_decision`),
   nikdy tichá změna. To platí i pro typy, které v tomto dokumentu nejsou (např. `EmailDocumentLimitsValidator`).
6. **Release gate:** tag `v2.10.0` ani publikace na NuGet se neprovádí bez výslovného potvrzení uživatele
   (hard blocker, Fáze 12). Před releasem ověřit `git tag --sort=-v:refname`.

---

## 5. Stav potvrzení

> **KONTRAKT JE POTVRZEN UŽIVATELEM — 2026-10-20.** Deset otázek z první verze dokumentu je
> převedeno na rozhodnutí (5.1) a review přidal nová rozhodnutí o rozdělení balíčků (5.2).
> Fáze 2–12 se proti nim implementují; odchylka = konzultace s uživatelem + decision, nikdy tichá změna.

### 5.1 Rozhodnutí k původním otázkám (potvrzeno 2026-10-20)

| # | Otázka | Rozhodnutí |
|---|---|---|
| D1 | `EmailSaveStatus` | `Saved` / `Failed` / `Conflict` + `public bool IsSaved => Status == EmailSaveStatus.Saved;` (žádná alternativa bez enumu). |
| D2 | `IsDirty` | `public bool IsDirty { get; private set; }` **vlastněný editorem** + `[Parameter] EventCallback<bool> IsDirtyChanged`; není to `[Parameter]`, `@bind-IsDirty` se nepoužívá. |
| D3 | Namespace nových typů | `Tempo.Blazor.EmailTemplates.Abstractions.Editing`. |
| D4 | `ITemplateFunctionProvider` | `IReadOnlyDictionary<string, Delegate> GetFunctions()` + `IReadOnlyList<TemplateFunctionInfo> Describe()`; `TemplateFunctionInfo(string Name, string? Description)`; **žádné Scriban typy**; delegáty jen synchronní, parametry/návrat `string`/`bool`/číselné/`decimal`/`DateTime`/`DateTimeOffset`/`IEnumerable` těch; jména `^[a-z][a-z0-9_]*$`; resoluce `IEnumerable<ITemplateFunctionProvider>`; výsledky jdou přes stejný `MemberFilter`. |
| D5 | Overloady vs DIM | Jedna signatura s volitelnými parametry (`TemplateRenderOptions? options = null`, `CancellationToken cancellationToken = default`) — **breaking B4, bez default interface metody**. |
| D6 | `EmailTemplateEditorOptions` | `Security` (`TemplateSecurityOptions`) + `ISet<string> FunctionNamespaces`; validace toleruje neznámé členy registrovaných namespace. |
| D7 | `Breakpoints` editoru | Veřejný `[Parameter] public TmLayoutBreakpoints? Breakpoints`; `null` = 768/1200. |
| D8 | `TemplateVariableInfo` | `Description` **+ `Group`**, **bez** `SampleValue` (ukázkové hodnoty nejsou v kontraktu). |
| D9 | `LayoutSeverity.Info` | Nová hodnota **+ jedno pravidlo** (preheader delší než 100 znaků). |
| D10 | `ToolboxCollapsed` | Ponechat vlastní parametr na editoru (mapuje se na shell `CollapsedPanels`). |

### 5.2 Nová rozhodnutí z review (2026-10-20)

| # | Rozhodnutí |
|---|---|
| D11 | **Nový balíček `Tempo.Blazor.EmailTemplates.Engine`** (Scriban, Mjml.Net, AngleSharp); Abstractions bez těchto závislostí; UI referencuje jen Abstractions. Rozdělení typů a registrací v [3.0](#30-balíčky-a-závislosti-nové-rozhodnutí-2026-10-20) — breaking B7, provedeno ve **Fázi 6**. |
| D12 | **Sanitizace bez AngleSharpu v Abstractions:** `HtmlSanitizerPolicy` (jediný zdroj allow-listů, včetně nadpisů `h1`–`h6` — D28) + `IHtmlContentSanitizer` v Abstractions; `AngleSharpHtmlContentSanitizer` v Engine (autoritativní); `ConservativeHtmlContentSanitizer` (BCL, **tokenizer s HTML-encode, NIKDY regex-based strip/replace**; emituje jen allow-list tagy bez atributů, výjimka `a[href]` po kontrole schémat z §3.7a; nikdy `style`/`on*`; neznámý/nevalidní vstup = escapovaný text, nikdy průchod; rozdíl proti AngleSharpu jen k přísnějšímu) jako výchozí pro editor bez Engine; obě musí projít stejnou XSS sadou včetně mutation-XSS vektorů a fuzz „výstup ⊆ policy“ — **plná omezení viz D22**. |
| D13 | **Limity importu aditivně:** `MaxInputBytes` = **5 MB** (dnešní chování, ne breaking) + `ImportOptions` na editoru i import dialogu + overloady `Import`/`Deserialize` s `EmailImportOptions?`. |
| D14 | **Escapování:** `TemplateEscapeMode` je `internal`; veřejný `TemplateRenderOptions` má jen `Culture`/`UseDocumentLanguageCulture`; jediný opt-out je `{{ value \| html.trusted }}` s výstupem vždy přes sanitizer. |
| D15 | **URL bezpečnost:** allow-list `http`/`https`/`mailto`/`tel` + relativní + `#`; jiné schéma → `#` + `RenderError` + audit `BlockedUrlScheme`; hostitel allow-list nerozšiřuje; `EmailTableCell.BackgroundColor` validovat regexem barvy. |
| D16 | **Ukládání single-flight** se `Revision` + `CancellationToken` a `IsSaving`; `IsDirty = false` jen po `Saved` a shodné revizi; `OnSave == null`/`ReadOnly` → `Failed` bez volání hostitele; po `Conflict` autosave pozastaven; ETag drží hostitel (sémantika v [3.2](#32-ukládací-kontrakt-tempoblazoremailtemplatesabstractionsediting--nový-namespace)). |
| D17 | **Náhled:** `Failed`/`Unavailable` vrací `Html`/`Text`/`Subject` = `""` (nikdy `null`); `EmailPreviewRequest.CultureName`; iframe zůstává `sandbox="" srcdoc` **i pro serverové HTML** (žádné `allow-scripts`); nový náhled ruší předchozí `CancellationToken`. |
| D18 | **`SchemaVersion`:** `public static int CurrentSchemaVersion => 1;` (vlastnost, ne `const`); `Deserialize` migruje `v → Current`; `Serialize` zapisuje `Current`; vyšší verze = `EmailTemplateSerializationException`; `EmailTemplateSerializer.ReadSchemaVersion(string json)`. |
| D19 | **`LayoutSeverity`:** nová hodnota `Info = 2`, existující `Warning = 0` a `Error = 1` explicitně zafixované beze změny číselných hodnot; explicitní rank `LayoutSeverityRank.Of(severity)` pro řazení zobrazení; breaking jen pro exhaustivní `switch`. |
| D20 | **Mobilní chrome:** Uložit + `ToolbarStart` v `Header` (bez `ToolbarStart` název dokumentu), `ToolbarEnd` v overflow `⋮`; `MobileActions` jen Undo (Zpět) / Znovu / Náhled / Validovat (`MobileActionsMaxVisible="4"`); `ResolvedLayoutChanged` vystavený; `ToolboxCollapsed` z `CollapsedPanelsChanged` **jen pro uživatelské sbalení/rozbalení** — tabletový auto-rail je stav layoutu a událost nevyvolá (D27); na mobilu ignorováno. |
| D21 | **Namespace přesouvaných typů se NEMĚNÍ:** `…Abstractions.Templating`, `…Abstractions.Rendering` a `…Abstractions` zůstávají i po přesunu do balíčku Engine. Výslovně: `AddTempoEmailTemplateEngine` má namespace **`Tempo.Blazor.EmailTemplates.Abstractions`**, i když žije v assembly/balíčku Engine. **V Engine nesmí vzniknout typ se stejným FQN jako typ, který zůstává v Abstractions** (jinak CS0433); přesouvané typy z Abstractions mizí, nikdy se neduplikují. |
| D22 | **`ConservativeHtmlContentSanitizer` v Abstractions přijat s omezeními:** tokenizer s HTML-encode, nikdy regex strip/replace; default akce = zakódovat `<`, `>`, `&`, `"`, `'`; znovu se emitují jen allow-list tagy bez atributů, výjimka `a[href]` po kontrole schémat z §3.7a; nikdy `style` ani `on*`; neuzavřené tagy se doplní, osiřelé zavírací se zakódují (vylučuje vložení `</mj-text>`, `<mj-raw>`, `<!--`); entity se dekódují a znovu zakódují; neznámý/nevalidní vstup degraduje na escapovaný text, nikdy na průchod; rozdíl proti AngleSharpu jen k přísnějšímu; paritní XSS sada včetně mutation-XSS (`<svg>`, `<math>`, `<noscript>`, `<!--`, `<![CDATA[`, nevalidní vnoření) + fuzz „výstup ⊆ policy“. UX: WASM export může ztratit formátování, které AngleSharp povolí. |

| D23 | **Závažnost render diagnostiky (rozhodnutí uživatele 2026-10-20):** `public enum RenderErrorSeverity { Error = 0, Warning = 1 }`; `RenderError(Message, Line, Column, Key, Severity = Error)` — `Severity` je **poslední** volitelný parametr; `RenderResult.Success = Errors.All(e => e.Severity != Error)`, nově `RenderResult.HasWarnings = Errors.Any(e => e.Severity == Warning)`. Blokovaná URL i odstraněný event handler = **Warning** (HTML je kompletní, `Success` zůstává `true`), takže `POST /api/email-templates/{id}/send` na varování **neodpovídá 422** a náhled mapuje varování na `EmailPreviewStatus.Rendered` s HTML (ne `RenderFailed`). Breaking pro poziční dekonstrukci `RenderError` a binárně (CHANGELOG). |
| D24 | **Cizí `ITemplateEngine` (rozhodnutí uživatele 2026-10-20):** `EmailTemplateRenderer` s enginem, který není `IEscapingTemplateEngine`, **nevyhazuje** — v konstruktoru jednou zaloguje warning „A custom ITemplateEngine cannot guarantee per-pass HTML escaping; the host is responsible for escaping.“ a použije fallback (`ITemplateEngine.Render`). Per-pass escapování je vlastnost Tempo enginu (`ScribanTemplateEngine`); dokumentováno v XML doc, `TEMPLATE_SYNTAX.md` a CHANGELOG. |

| D26 | **`Conflict` pauza padá i po `Shortcut` uložení (rozhodnutí uživatele 2026-10-22):** po `Conflict` se autosave pozastaví do **úspěšného ne-autosave uložení** — `Manual`, `Shortcut` (Ctrl/⌘+S) i `Programmatic` (`SaveAsync()`), nebo do předání nové reference `Document`. Zdůvodnění: `Ctrl+S` je pro uživatele stejně explicitní zápis jako tlačítko Uložit; když hostitel potvrdí `Saved`, uživatel právě dokázal, že šablona lze znovu zapsat, a držet autosave dál pauznutý by znamenalo, že se další úpravy tiše neukládají. Implementace: `if (trigger is not EmailSaveTrigger.AutoSave) _autoSavePaused = false;` v `Record`, test „Conflict → Ctrl+S Saved → autosave běží". |
| D25 | **`JsonStringLocalizer<T>` zůstává `public sealed` v `Tempo.Blazor.EmailTemplates.Abstractions.Localization`** (potvrzeno uživatelem 2026-10-20). Necpi ho jako `internal` s `InternalsVisibleTo` do UI balíčku: IVT je vázané na silné jméno **a přesnou verzi** sestavení, takže každý bump verze by tiše rozbil lokalizaci editoru (stejný důvod, proč F6 pinuje Engine na `[2.10.0]`). Veřejný typ je aditivní API (B6) a hostitel ho může použít i pro vlastní markery; registruje ho `AddTempoEmailTemplateModel` (po Fázi 6) vedle validátorů a `AddTempoEmailTemplates` pro editor. |
| D27 | **Tabletový auto-rail je stav layoutu, ne preference sbalení (rozhodnutí uživatele, fáze 8 review):** když `TmEditorShell` na tabletu sám zrailuje panel (pravidlo „nejvýš jeden rozbalený“), editor tuto změnu **nepřevezme** do `ToolboxCollapsed` a **nevyvolá** `ToolboxCollapsedChanged`; událost vyvolá jen uživatelské sbalení/rozbalení (toggle panelu, rozbalení inspektoru z `RightRail`). Implementace: `TmEditorShell` posílá `ResolvedLayoutChanged` **před** `CollapsedPanelsChanged` (jádro 2.10.0), editor drží generaci layoutu (`_layoutGeneration++` v `OnResolvedLayoutChangedAsync`, v reportu `TmEmailMobileHeaderSlot` — sdílí tentýž handler — a při změně parametru `LayoutMode`) a raise ve stejné generaci jako změna layoutu vyhodnotí jako stav layoutu. Převzetí je synchronní v `OnShellCollapsedPanelsChangedAsync` (žádné odkládání do `OnAfterRender`). Důsledek pro hostitele: na tabletu může být paleta zrailená a `ToolboxCollapsed` přitom `false` — „je paleta vidět" se čte z layoutu (`data-layout` / `ResolvedLayoutChanged`), ne z tohoto parametru. |

| D29 | **Raw blok neznamená „spusť, co přišlo“ — `MarkupGuard` vždy odstraňuje nebezpečné elementy (rozhodnutí uživatele 2026-10-22, review Fáze 10 U1):** `MarkupGuard` odstraňuje `script`, `iframe`, `frame`, `frameset`, `object`, `embed`, `applet`, `base`, `form` a `portal` z KAŽDÉHO markupu, který projde renderem (raw blok, text blok, host-set HTML atribut, import), bez opt-in flagu; raw-text element (`script`) včetně obsahu až po uzavírací tag. Warning `render.markup.unsafe_element_removed` + audit `BlockedMember`; `Success` zůstává `true` (D23). Zdůvodnění: inbox preview i webmail otevírají zprávu v dokumentu, který spouští skript — „autor to vložil do raw bloku“ není důvod vydat živý `<script>`; `<base>` přepisuje rozlišení relativních URL a `<form>` umí POSTovat vstup příjemce. Interaktivní obsah v e-mailu je mimo rozsah (odkaz na hostovanou stránku). Breaking pro raw blok s takovým elementem (CHANGELOG „Breaking / Migration“). Manuální úkol f5d9f954 (raw blok `<script>` opt-in) je tím vyřešený. |
| D28 | **Nadpisy `h1`–`h6` jsou v allow-listu sanitizéru (rozhodnutí uživatele 2026-10-26, review F9A):** `HtmlSanitizerPolicy.AllowedTags` (jediný zdroj, D12) obsahuje `h1`, `h2`, `h3`, `h4`, `h5`, `h6`. Jsou to **formátovací** elementy stejně jako `b`/`i`: obě implementace (`AngleSharpHtmlContentSanitizer` i `ConservativeHtmlContentSanitizer`) je emitují **bez atributů** — `<h1 onclick=x style="…">Ahoj</h1>` → `<h1>Ahoj</h1>`, `<h2><img onerror></h2>` → `<h2>…</h2>` s escapovaným obsahem. Důvod: e-mailový autor píše typografii nadpisy a WYSIWYG plátno ji musí zobrazit; bez allow-listu se nadpis na plátně i v exportu zobrazil jako text s závorkami. Bezpečnostní důsledek žádný — atributy (`style`, `on*`, `class`) byly a zůstávají zakázané, takže nadpis nemůže přebarvit editor ani spustit skript. Paritní XSS sada obou sanitizérů (`SanitizerXssVectors.Payloads`) obsahuje vektory s nadpisy a zůstává zelená. |


### 5.3 Dříve otevřené volby (uzavřeno rozhodnutími D21/D22)

Obě implementační volby z prvního kola review jsou **potvrzené jako rozhodnutí** — namespace
přesouvaných typů (D21) a konzervativní sanitizer v Abstractions s omezeními N8 (D22). Tato sekce už
neobsahuje žádné otevřené otázky.

---

## 6. Poznámky pro následující fáze

- **Fáze 2–4** pracují ještě v Abstractions: **nesmí** přidat novou `PackageReference` (Scriban/Mjml.Net/
  AngleSharp už tam jsou jen do Fáze 6). Sandbox, escapování a kultura se implementují tam, přesun do
  Engine je mechanický (Fáze 6).
- **Fáze 5** musí opravit posunuté popisy JSON dokumentace (2.8.1) — ověřeno, že příčinou je párování popisů
  v pořadí deklarace proti abecedně řazeným parametrům; po opravě regenerovat bundle a `validate --fail-on-drift`.
- **Fáze 6** (rozdělení registrace) je fáze, kde vzniká balíček `Tempo.Blazor.EmailTemplates.Engine`
  (D11): přesun typů bez změny logiky, nové soubory `tests/Tempo.Blazor.EmailTemplates.Engine.Tests`
  pro Scriban/Mjml.Net/AngleSharp typy, `AddTempoEmailTemplateModel/Engine/Editor` (3.0), `ITemplateValidator`
  a `ITemplateVariableExtractor` + `RegexTemplateVariableExtractor`, `IHtmlContentSanitizer` +
  `ConservativeHtmlContentSanitizer`/`AngleSharpHtmlContentSanitizer` (D12), `LocalEmailPreviewRenderer`.
  CI/test musí ověřit, že Abstractions nemá Scriban/Mjml.Net/AngleSharp a UI nemá Engine.
  **Přepnutí volajících už tady:** WASM demo `src/Tempo.Blazor.Demo/Program.cs:125` →
  `AddTempoEmailTemplateEditor()` + HTTP `IEmailPreviewRenderer` (důkaz, že Engine ve WASM není potřeba);
  15 volání `Services.AddTempoEmailTemplates()` v testech → `AddTempoEmailTemplateEditor()` +
  `AddTempoEmailTemplateEngine()`; `Demo.Api/Program.cs:164` dostane `PackageReference` na Engine
  a jeho poziční `RenderAsync(document, model, ct)` (`EmailTemplateEndpoints.cs:58,93`) se přepíše na
  `cancellationToken: ct`.
- **Fáze 7A/10** musí použít `ITemplateValidator` (ne `ITemplateEngine`) a čitelně degradovat, když
  chybí (3.0) — `TmEmailValidationPanel` dnes injektuje `ITemplateEngine` (`TmEmailValidationPanel.razor:2`).
  Import dialog může zůstat na `new MjmlImporter()` (pure BCL), ale limity bere z `ImportOptions`
  (D13) — dnes natvrdo 5 MB (`TmEmailImportDialog.razor:63`).
- **Fáze 8** staví přímo na `TmEditorShell` (rozhodnutí EM2(a)) — žádný lokální `EmailEditorLayout` ani mezikrok
  s migrací; `data-tm-*` selektory musí zůstat, jinak spadnou E2E — v `TmEmailTemplateEditor.razor` je jich 13:
  `data-tm-email-editor`, `data-tm-toolbox`, `data-tm-canvas`, `data-tm-properties`, `data-tm-status`,
  `data-tm-undo`, `data-tm-redo`, `data-tm-preview-btn`, `data-tm-validate-btn`, `data-tm-import-btn`,
  `data-tm-mjml-btn`, `data-tm-help-btn`, `data-tm-save`.
- **Fáze 11** musí parametrizovat E2E viewport (dnes pevně 1440×900 v `EmailTemplateE2ETestBase.cs:37`) a přidat
  1024/390/320 px + screenshoty podle referenčních `screen-id`. (Přepnutí WASM dema na
  `AddTempoEmailTemplateEditor()` + HTTP `IEmailPreviewRenderer` patří už do Fáze 6 — viz výše.)
- **ReportServer (mimo 2.10.0):** `Tempo.ReportServer.Web.Client` dnes vědomě referencuje Engine ve WASM —
  lokální doručení v demo plánovači. Při další práci na report serveru přesunout
  `AddTempoEmailTemplateEngine` + `ReportEmailDeliveryService` + `ReportScheduleWorker` z `AddCommonServices`
  do serverového hostitele a klientovi nechat jen `AddTempoEmailTemplateModel`; pak rozšířit
  `PackageBoundaryTests`. Hlídá to `ReportServerWasmClient_IsTheOnlyWasmHostWithTheEngine` (jen tenhle
  jediný WASM host smí Engine mít).
- **Fáze 12**: release gate = základový plán `faf503eb` má Completed F0/F1/F2/F6; bez toho 2.10.0 nevychází.
  Do CHANGELOG patří i B7 s receptem na migraci balíčku.

### 6.1 Měření velikosti publikovaného WASM dema (Fáze 6, D11)

Měřeno na `src/Tempo.Blazor.Demo` (Blazor WebAssembly, `net10.0`, `dotnet publish -c Release`, výchozí
Brotli komprese; `wasm-tools` workload není nainstalovaný, takže běží „publishing without optimizations").
Čísla jsou velikosti souborů v `wwwroot/_framework` — `*.br` je to, co si prohlížeč skutečně stáhne.

| Varianta | `_framework` raw | `_framework` Brotli | Assemblies enginu |
|---|---|---|---|
| `AddTempoEmailTemplateEditor()` + HTTP `IEmailPreviewRenderer` (dnešní stav dema) | 73,22 MB | **11,90 MB** | — (žádná) |
| `AddTempoEmailTemplateEditor().AddTempoEmailTemplateEngine()` | 74,81 MB | **12,18 MB** | Scriban 147,4 KB br, Mjml.Net 69,4 KB br, `Tempo.Blazor.EmailTemplates.Engine` 34,1 KB br |

**Závěr:** oddělení balíčku ušetří **~0,28 MB Brotli (~1,6 MB raw)** na každém WASM stažení.
Scriban, Mjml.Net a `Tempo.Blazor.EmailTemplates.Engine` v editor-only buildu **prokazatelně nejsou**
(v `_framework` se nevyskytují). Trimmer je neodstraní, když je engine zaregistrovaný — kód se reálně
volá (`ITemplateEngine` → `ScribanTemplateEngine`, `IMjmlCompiler` → `MjmlNetCompiler`), takže „trimmed
away" nepřipadá v úvahu; odstranění je dosaženo **nereferencováním balíčku**, ne trimmingem.

**AngleSharp (271,5 KB br) zůstává v OBOU buildech** — ne kvůli e-mailům: `Tempo.Blazor.DocumentEditor`
referencuje `AngleSharp` 1.5.2 vlastní `PackageReference`. E-mailové balíčky ho nepřinášejí:
`Tempo.Blazor.EmailTemplates.Abstractions` na něj nemá ani `PackageReference`, ani assembly referenci
(hlídá `PackageBoundaryTests.AbstractionsAssembly_ReferencesNoEngineDependency`).

### 6.2 Měření výkonu WYSIWYG plátna (Fáze 9A)

Plátno od Fáze 9A kreslí CELÝ dokument (blok = vizuální aproximace MJML), takže každý stisk klávesy
v inspektoru prochází renderem. Aby se nepřekreslovalo 200 bloků, má renderer bloku vlastní rozhodnutí
(`TmEmailCanvasBlock.ShouldRender` → `EmailCanvasRenderGate` + `EmailBlockSignature`): podpis je
serializace PŘESNĚ těch hodnot, které renderer maluje, takže změna jednoho bloku nechá ostatní
renderery v klidu (a naopak ji nelze zmeškat — test `Every_painted_property_of_a_block_invalidates_its_own_renderer`
projde každou malovanou vlastnost jednu po druhé).

Měřeno na `net10.0`, Debug, xUnit/bUnit (`TmEmailCanvasPerformanceTests`), dokument = 200 textových bloků
v 10 sekcích, `ContentWidth` 600 px, čistý start test hostu (JIT studený):

| Operace | Naměřeno |
|---|---|
| První vykreslení 200 bloků (studený JIT) | **984 ms** |
| Další plné vykreslení 200 bloků (teplý JIT) | **108,7 ms** |
| Rozhodovací průchod 200 bloků (podpisy + gate) po jedné změně | **1,29 ms** |
| Změna jedné vlastnosti + re-render (bUnit, který překresluje celý strom) | **50,3 ms** |
| Bloků překreslených při změně jednoho bloku | **1 z 200** |

**Závěr pro Server režim:** cena změny vlastnosti = rozhodovací průchod (~1,3 ms na 200 bloků) + re-render
jednoho bloku; cíl „< 100 ms na změnu vlastnosti" je splněn s velkou rezervou i s tím, že bUnit měří
i celý diff. Skutečný důkaz produkční cesty (že se nepřekresluje celý strom) podává prohlížeč:
`EmailEditorCanvasWysiwygF9AE2ETests` nasadí `MutationObserver` na plátno, napíše do inspektoru a ověří,
že mutace DOM vznikly pouze uvnitř editovaného bloku, plus změří čas od `input` eventu do dalšího frame.

Měřeno v prohlížeči (`EmailEditorCanvasWysiwygF9AE2ETests`, WASM Debug, self-hosted demo, 4 stisky
klávesy do rich-textu vybraného bloku, `MutationObserver` na plátně):

| Scénář | Latence na stisk | Bloků překresleno |
|---|---|---|
| Seed „Welcome" (3 bloky) | ~259 ms | 1 |
| 52 bloků (vloženo paletou) | ~562 ms | **1** (mutace DOM jen uvnitř editovaného bloku) |

Růst mezi 3 a 52 bloky (~300 ms) není práce plátna: rozhodovací průchod 200 bloků stojí 1,3 ms a DOM se
mění jen v editovaném bloku. Je to cena editorova commitu na každý stisk (hluboká kopie dokumentu pro
undo + serializace pro dirty check), tedy předexistující vlastnost F7, která roste s dokumentem — kandidát
na samostatnou optimalizaci (Release WASM je řádově rychlejší; čísla výše jsou Debug build).

Poznámka k bUnit: `BunitContext.Render()` překreslí celý strom a `ShouldRender` podruhé nezavolá
(ověřeno probe komponentou v této fázi), takže DOM ani čítač v bUnit produkční optimalizaci
neprokazují — proto testy měří rozhodnutí (`ShouldRenderNow`/`EmailCanvasRenderGate`) přímo a chování
v prohlížeči hlídá E2E.

## 7. Fáze 11 — demo, E2E a vizuální převzetí

### 7.1 Screenshot sada: reference → screenshot → odchylka

Celá e-mailová sada se od Fáze 11 generuje JEDNÍM spec souborem
`tests/Tempo.Blazor.E2E/EmailTemplateReferenceShotsE2ETests.cs`, který každé referenční screen-id vyfotí na
**všech čtyřech podporovaných šířkách**: `--desktop` (1440×900), `--tablet` (1024×768), `--mobile` (390×844) a
`--mobile-320` (320×720). Tím se završil drift z F5–F10 (každá fáze fotila na tom viewportu, který zrovna
používala, a 1024/390/320 neexistovaly vůbec).

| reference (output/imagegen/…/Tempo.Blazor.EmailTemplates) | náš screenshot (__screenshots__/email-templates) | odchylka |
|---|---|---|
| `email-template-editor--desktop/tablet/mobile.png` | `email-template-editor--{desktop,tablet,mobile,mobile-320}.png` | **záměrná**: breadcrumb + název šablony + „Vše uloženo“ v hlavičce nahrazuje shell (F7/F8) — Save je primární akce v toolbaru, stav uložení je v badge/status baru shellu; horní demo přepínače jsou host chrome dema. Panel nástrojů má jiné bloky (Text/Button/Table/Navbar/Accordion/Image/Social/Carousel/Divider/Spacer/Hero/Wrapper/Group) než reference (Nadpis/Citace/Karta) — registry je daná F4/F7A. |
| `email-template-editor--toolbar_preview--*.png` | `email-template-editor--toolbar_preview--*.png` | **záměrná**: dialog nese náš toolbar (zařízení Plocha/Tablet/Mobil + HTML/Text), patička „Obnovit náhled“/„Zavřít“; reference kreslí jen přepínač zařízení. Chybí „Odeslat test“ (vymyšleno generátorem). |
| `email-template-editor--validation_title--*.png` | `email-template-editor--validation_title--*.png` | **záměrná**: závažnosti Error/Warning/Info s ikonou a počty, titulek „Validation“; reference má jen titulek a seznam. |
| `email-keyboard-shortcuts--*.png` | `email-keyboard-shortcuts--*.png` | **záměrná**: jádrový `TmKeyboardShortcutsHelp` s popisem (F10) místo jednoduchého seznamu v referenci. |
| `email-table-editor--*.png` | `email-table-editor--*.png` | **záměrná**: mřížka s připnutým řídicím sloupcem, dialog buňky (F9); reference ukazuje jen buňky bez chrome. |
| `email-variable-picker--*.png` | `email-variable-picker--*.png` | **záměrná**: chip s tokenem + popis hostitele + skupiny (F7A); na mobilu je picker obsah spodního sheetu. |
| `email-import-dialog--*.png` | `email-import-dialog--*.png` | **záměrná**: drop zóna vždy pro všechny formáty, strukturovaný náhled, limity hostitele (F10). |
| `email-export-dialog--*.png` | `email-export-dialog--*.png` | **záměrná**: záložky MJML/JSON/HTML + kopírování + stahování se slugem (F10); reference má jen MJML/JSON. |
| `email-templates-page--*.png` | `email-templates-page--*.png` | **záměrná**: miniatury e-mailů nejsou (analýza je označila jako [D] volitelné a generátorem vymyšlené); karty mají jazykový odznak, hledání, 3/2/1 sloupce, na 390/320 akce v „⋮“ menu. |
| `email-templates-page--dialog-01--*.png` | `email-templates-page--dialog-01--*.png` | **záměrná**: potvrzovací dialog s ikonou v danger kruhu; prezentace se řídí viewportem (`TmModal.MobilePresentation` = výchozí `Auto`): 1440/1024 centrovaný dialog jako v referenci, 390/320 jádrový sheet (F2). Vynucený `Sheet` (první generace) dělal z desktopové potvrzovky spodní sheet. Pozadí není master-detail (to je hostitelova metadata, ne komponenta). |
| `email-template-send-page--*.png` | `email-template-send-page--*.png` | **záměrná**: sekce Recipients/Variable values s popisky z hostitelského katalogu (`TemplateVariableInfo.Description`), náhled bez editoru ukázkových dat a bez HTML/Text přepínače; pod 1280 px jedno sloupec (formulář nad náhledem), na mobilu sticky „Send e-mail“ s důvodem, proč je disabled („Enter at least one recipient.“); stav po odeslání + odkaz na smtp4dev zůstává (E2E `09-send-success.png`). |

Odchylky, které analýza označila jako „vymyšleno generátorem“, se **nedělají**: app shell (Projekt Aurora,
uživatel), „Odeslat test“ v toolbaru, záložky Editor/MJML/Náhled nad plátnem, bloky Nadpis/Citace/Karta a
dosazené hodnoty proměnných na plátně.

### 7.2 UX nálezy z převzetí screenshotů (opraveno test-first)

| nález | důkaz | oprava |
|---|---|---|
| Karta knihovny láme čtyři akce do tří řádků (Edit sám, pak Send/Duplicate, pak Delete) | `email-templates-page--desktop.png` (první generace) | `display: contents` na obalu sekundárních akcí → akce sdílejí jeden zalamující řádek (3+1 při třísloupcové mřížce; Delete zůstává na vlastním řádku s danger inkoustem — vědomě, čtyři tlačítka s ikonami se do ~330 px karty nevejdou); na <640 px je obal skrytý a akce jdou do „⋮“ |
| „Delete“ nemá danger barvu — vypadá jako druhý ghost | `email-templates-page--desktop.png` | scoped pravidlo musí být `::deep` (třída je na rootu `TmButton`, tedy na elementu child komponenty, který nescopovaný atribut nenosí) |
| Editor v demu nepustí status bar shellu (nosič stavu uložení) do záběru | `email-template-editor--desktop.png` | **odloženo** (remaining): stránka počítá `height: calc(100vh - 70px)`, ale app shell je ~78 px + dvouřádkový demo bar, takže status bar padne ~25 px pod okraj. Oprava = `calc(100dvh - 5rem)` v `EmailTemplateEditorPage.razor.css`; odloženo, protože mění výšku stage pro všechny responzivní E2E (F8) a vyžaduje nový běh celé lane |
| Formulářová karta na send page má prázdné místo pod tlačítkem (grid stretch) | `email-template-send-page--desktop.png` | **odloženo** (remaining, kosmetika): `.tm-demo-email-send__form { align-self: start; }`; nemění chování ani dostupnost |
| Náhled na send page má vlastní přepínač „With sample data / Without substitution“ | `email-template-send-page--desktop.png` | **záměrné**: skrývá se jen editor ukázkových dat a HTML/Text přepínač (`ShowSampleDataEditor=false`, `ShowViewToggle=false`) — substituce je věc náhledu, ne duplikace formuláře |
| Titulek `h1` má focus ring (host shell) | `email-templates-page--desktop.png`, `email-template-send-page--desktop.png` | **mimo rozsah fáze** (chování app shellu dema pro klávesnicovou orientaci, stejné na ostatních demo stránkách) |

### 7.3 Deterministická e-mailová E2E lane

- **Viewport je parametr.** `EmailTemplateE2ETestBase.OpenAsync(route, width, height, pinDesktopEditor)` —
  historická konstanta 1440×900 zůstala jako výchozí, takže staré specy se nezměnily, a screenshot sada
  bootuje 1024/390/320 s vypnutým pinováním desktop layoutu (aby se ukázal skutečný úzký chrome).
- **Seed se resetuje před každým specem.** `POST /api/email-templates/reset` (Demo API, konvence
  Gantt/Notion/DocumentEditor) vrací store do seed stavu; `EmailTemplateE2ETestBase` ho volá
  v `[TestInitialize]`. Tím zmizela order-dependence, kdy spec s hostile importem (F9A) přepsal sdílený seed
  dokument a rozbil F7A_4/F10_4 podle pořadí objevování tříd.
- **F7A_6 picker — ROOT CAUSE NALEZENA (Fáze 11).** Příčina není v produktu ani v auto-save: picker
  remountuje **vlastní screenshot specu**. `SaveNamedScreenshotAsync` je FULL-PAGE capture; ten změní
  metriky stránky, editor znovu vyhodnotí layout a `TmOverlayPanel` znovu vytvoří svůj obsah — nový
  `TmEmailVariablePicker` startuje s prázdným filtrem, takže test přečte první nefiltrovaný řádek
  (`company.support_email` místo `order.total`). Repro: spec
  `EmailWorkspaceVariablePickerStabilityE2ETests` s vloženým full-page screenshotem spolehlivě shodí marker
  DOM node (`data-picker-identity` → `null`), zatímco 50 vzorků po 100 ms přes auto-save cyklus marker
  udrží (produkt i auto-save jsou v pořádku). Oprava: F7A_6 čte filtrovaný řádek PŘED screenshotem a fotí
  **viewport** (konvence F8 pro scrollující plochu editoru); stabilitu filtru hlídá
  `EmailWorkspaceVariablePickerStabilityE2ETests.VariablePicker_KeepsItsFilter_AcrossAnAutoSaveCycle`.
  Zbývá (remaining): filtr se ztratí, když editor skutečně překlopí layout (uživatel mění šířku okna) —
  oprava by vyžadovala držet stav filtru nad pickerem, tedy nové veřejné API (mimo rámec fáze).

### 7.4 Výsledek e-mailové lane (Fáze 11)

Filtr `EmailTemplate|EmailEditor|EmailWorkspace` = **113 testů** (původních 95 + 18 nových této fáze:
`EmailTemplateF11DemoE2ETests` 7, `EmailTemplateReferenceShotsE2ETests` 4, `EmailWorkspaceVariablePickerStabilityE2ETests` 1,
`EmailTemplateServerModeE2ETests` 3, `EmailTemplateInteractiveAutoModeE2ETests` 3).

| běh | výsledek | poznámka |
|---|---|---|
| 1 (před opravami) | 105/107 | F5_1 (guard blokoval jazykový přepínač) + F7A_6 (full-page screenshot) — obojí opraveno, příčiny doloženy |
| 2 | **113/113** | první čistý běh po opravách (25:40) |
| 3 | **113/113** | druhý čistý běh (26:25) |
| 4 | 110/113 | všechny tři chyby jsou TIMEOUT na start hostů (`WaitForAppReadyAsync` / `[data-tm-email-editor] to be visible`), žádná aserce; běh trval 31 min místo 26 → přetížený stroj. Zavedena centralizovaná recovery (jeden reload) do `EmailTemplateE2ETestBase.OpenAsync` i do F8 helperu |
| 5 | 105/113 | přetížený stroj (40:51): padly i Server/IA smoke testy na start hostů, API v době běhu neodpovídalo; nic z toho není aserce ani produktová chyba |
| 6 | 111/113 | F11_4 (jednorázově; samostatně 3/3 zelené) + F6_Preview_DarkMode (60s timeout na start) |

**Závěr k determinismu:** dvě po sobě jdoucí čisté běhy (113/113) po opravách; další dva běhy padly výhradně na
colds-tart/readiness timeouty self-hostovaných dem (stroj měl load average ~8 a jeden běh trval o 60 % déle),
což je známá carry-forward položka `PlaywrightTestBase.WaitForAppReadyAsync` — fáze ji zčásti řeší
(centralizovaný jednoreload v `EmailTemplateE2ETestBase.OpenAsync` a ve F8 helperu), zbytek patří F12.
Order-dependence je uzavřená resetem seedu před každým specem (dokázáno: hostile import v F9A už nerozbije F7A_4).

### 7.5 Fáze 11 review (UX ChangesRequested + code review minor) — opraveno test-first

Převzetí screenshotů a code review první generace Fáze 11 vrátily sadu nálezů. Všechny jsou opravené
(test-first: bUnit pro chování, E2E pro rozměry a screenshoty) a screenshoty jsou přegenerované jedním
během `EmailTemplateReferenceShotsE2ETests`.

| nález | důkaz (první generace) | oprava |
|---|---|---|
| Import dialog otevírá editor s doslovným textem „_input“ | `email-import-dialog--*.png`, kód `Value="_input"` | chybějící `@` u string parametru = LITERÁL. `Value="@_input"` v `TmEmailImportDialog`; stejná chyba v `TmEmailClassesEditor` (`Value="_newName"`). Zbytek výskytů `="_…"` v balíčku jsou výrazy (komponenty ve stejné assembly je Razor type-checkuje) — ověřeno vygenerovaným kódem. bUnit: editor prázdný + placeholder, po vložení obsah |
| Potvrzovací dialog knihovny byl sheet i na 1440 | `email-templates-page--dialog-01--desktop.png` | `TmModal.MobilePresentation` vynucený `Sheet` odstraněn → výchozí `Auto` (1440/1024 dialog, 390/320 sheet). E2E: `.tm-modal--sheet` na 1440 = 0, na 390 = 1 |
| Mobilní „⋮“ nezavírá panel po spuštění příkazu | `email-keyboard-shortcuts--mobile.png` (panel zůstal pod dialogem) | příkazy z overflow (Import/Export/Validate/Help) jdou přes `RunFromMobileOverflowAsync`; hostitelský `ToolbarEnd` zavírá přes bubbling. bUnit: po kliku na `[data-tm-help-btn]` panel neexistuje |
| Send page na 1024 mačkala dva sloupce | `email-template-send-page--tablet.png` | `@media (width < 1280px)`: jeden sloupec, `position: static` u náhledu; sticky Send zůstává < 640 px. E2E 1024: náhled pod formulářem, stejný levý okraj, `position: static` |
| Disabled Send bez vysvětlení | `email-template-send-page--mobile.png` | hint „Enter at least one recipient.“ (cs/sk/fr v DemoResources) pod tlačítkem; mizí po vyplnění příjemce |
| Demo stránky měly natvrdo anglické texty | `EmailTemplatesPage`, `EmailTemplateSendPage`, guard texty | klíče `TmDemo_EmailTemplates_*` do `DemoResources{,.cs,.sk,.fr}.json` + demo localizer; hlídá `DemoResourceCompletenessTests` (parita klíčů i placeholderů) a bUnit testy (echo localizer = klíč) |
| Hledání v knihovně tvrdilo „bez diakritiky“, ale porovnávalo `OrdinalIgnoreCase` | komentář vs. kód | `CompareInfo.IndexOf(…, IgnoreCase \| IgnoreNonSpace)`; bUnit „sablona“ → „Šablona“, „zakazniky“ → „zákazníky“, „DOBRY“ → „Dobrý“ |
| Menu „⋮“ šlo zavřít jen vlastními položkami | — | Escape (fokus zpět na trigger; handler na obalu, protože fokus zůstává na triggeru) + klik mimo (backdrop). bUnit obojí, E2E Escape + `document.activeElement` |
| Mezi záložkami sheetu a hledáním palety ~60 px mrtvého místa | `email-template-editor--mobile.png` | dvojí odsazení v email CSS: `panel-body` padding 0 a prázdný `toolbox__header` skrytý na mobilu (jádro nedotčeno). E2E 390: mezera záložky→hledání ≤ 48 px |
| Pickerské hledání nalepené na první skupinu | `email-variable-picker--*.png` | `margin-block-end: var(--tm-space-2)` na `TmSearchInput` pickeru |
| `EmailEditor_Validation_Subtitle` bez uvozovek kolem „Go to“ | en/cs/sk/fr tabulky | en „Select “Go to” to jump to the spot.“, cs „Tlačítkem „Přejít“…“, sk „Tlačidlom „Prejsť“…“, fr „Utilisez « Aller à »…“ |
| Render-mode E2E (Server/IA) neresetovaly seed | `EmailTemplateRenderModesE2ETests` | `[TestInitialize]` volá sdílený `EmailTemplateE2ETestBase.ResetEmailStoreCoreAsync` (nově `internal`) |

---

## 8. Fáze 12 — release 2.10.0: dokumentace, balíčky a celá testová sada

Fáze 12 je poslední fáze plánu. Neimplementuje novou funkci: uzavírá kontrakt, dokumentuje ho, sladí
verze, zabalí balíčky, ověří release gate a **jednou** pustí celou testovou sadu repa (unit testy po
projektech + celou E2E lane).

### 8.1 Verze a release gate

| kontrola | výsledek |
|---|---|
| `git tag --sort=-v:refname` (2026-10-12) | `v2.9.0` — cílová verze `2.10.0` je volná, potvrzeno zadáním plánu (jedno vydání 2.10.0 se vším) |
| `<Version>` ve všech `src/**/*.csproj` | `2.9.1` → **`2.10.0`** (27 projektů; jediný zdroj pravdy, žádný `Directory.Build.props` version) |
| `ReleaseContractTests` (csproj vs. první semver hlavička CHANGELOGu) | před fází **RED** (2.9.1 vs. 2.10.0), po synchronizaci **zelené** |
| základový plán `faf503eb-bf08-4f5f-8c33-08df2362aa32` — F0 | `fze-1-f0-smantick-tokeny-indigo-tma-psn-audit-css-isolation-test-a-rychl-opravy-jdra` — status **Completed**, reviewStage `Finalized`, commit `3189b85a` |
| — F1 | `fze-2-f1-konvence-kontejneru-dotyku-a-tmlayoutmode` — **Completed**, `Finalized`, `7f5ad9dc` |
| — F2 | `fze-3-f2-bottom-sheet-mobiln-prezentace-tmmodaltmdialog-a-veejn-focus-trap` — **Completed**, `Finalized`, `dc75a1e4` |
| — F6 | `fze-6-f6-tmeditorshell-a-tmsidepanel-kritick-pro-release-2100` — **Completed**, `Finalized`, `72a12b6b` |
| použití primitiv základu v editoru | `TmEditorShell` (`TmEmailTemplateEditor.razor`), `TmLayoutMode` (`LayoutMode`/`InitialMode`/`Breakpoints`), `TmModal.MobilePresentation` (dialogy přes `LayoutMode` shellu); žádný lokální `EmailEditorLayout` ani vlastní sheet — `Feedback/OverlayPanel` jádra |
| **release gate** | **SPLNĚN** — všechny čtyři fáze základu jsou Completed a jejich primitiva editor používá |

Tag `v2.10.0` ani publikace na NuGet **nebyly provedeny** — vyžadují výslovné potvrzení uživatele
(hard blocker, plan rule 24). Strom je ve stavu „připraveno k vydání“.

### 8.2 `dotnet pack` a obsah balíčků

Všech **27** projektů z `eng/nuget-packages.txt` zabaleno `-c Release` do `/tmp/pk-2100`
(`Tempo.Blazor.2.10.0.nupkg` … `Tempo.Blazor.Proofing.LanguageTool.2.10.0.nupkg`). Ověřeno rozbalením
(`zipfile`, čtení `.nuspec`):

| kontrola | výsledek |
|---|---|
| verze všech balíčků | `2.10.0` |
| **Engine → Abstractions** | `version="[2.10.0]"` — **přesný rozsah**, ne `>=` (carry-forward z F3/F6) |
| `Tempo.Blazor.EmailTemplates` obsahuje `__tests__` | **ne** (0 záznamů; `tm-email-save-shortcut.js`, `tm-email-variable-insert.js`, `tm-email-panel-focus.js` jsou přibaleny, `__tests__/*.test.mjs` ne) |
| `Abstractions` závislosti | FluentValidation, Microsoft.Extensions.{DependencyInjection,Localization,Logging}.Abstractions — **žádný Scriban / Mjml.Net / AngleSharp** |
| README v každém z e-mailových balíčků | ano (`README.md`) |
| XML doc | ano (`lib/net10.0/*.xml` u všech tří) |
| JSON lokalizace | `content/Resources/TmEmailResources{,.cs,.sk,.fr}.json` + `contentFiles/any/net10.0/…` v `Tempo.Blazor.EmailTemplates` |

**Nový packaging test** (carry-forward F3/F6): `PackageBoundaryTests.PackedEngineNuspec_PinsAbstractionsToTheExactVersion`
(`tests/Tempo.Blazor.EmailTemplates.Engine.Tests`) projekt skutečně zabalí a čte `.nuspec` — zdrojová
kontrola csproj nestačí, protože NuGet `Version` metadata na `ProjectReference` ignoruje.
**Červená fáze doložena:** s dočasně vypnutým targetem `PinAbstractionsToTheExactVersion`
(`AfterTargets` změněn) test spadl na
`Expected dependencies["Tempo.Blazor.EmailTemplates.Abstractions"] to be "[2.10.0]" … but "2.10.0" … differs near "2.1" (index 0)`;
po obnovení targetu zelený (15/15 v `PackageBoundaryTests`).

### 8.3 TmNavigationGuard — poslední natvrdo anglické texty v jádru (carry-forward z F11)

`TmNavigationGuard` (jádro) měl výchozí texty dialogu jako anglické konstanty na parametrech
(`ConfirmTitle = "Unsaved changes"`, …), takže aplikace v cs/sk/fr bez explicitních parametrů zobrazila
angličtinu. Fáze 11 lokalizovala jen odpovědi dema.

**Oprava (aditivní, `string?` s null default):** `ConfirmTitle ?? Loc["TmNavigationGuard_ConfirmTitle"]`,
`ConfirmMessage ?? …_ConfirmMessage`, `ConfirmLeaveText ?? …_Leave`, `CancelText ?? …_Stay`,
`SaveAndLeaveText ?? …_SaveAndLeave`. Klíče přidány do všech čtyř `TmResources*.json` a do
`MockTmLocalizer` (anglická i česká mapa); anglické texty jsou **bajtově shodné** s původními defaulty,
takže E2E, které hledá dialog podle viditelného textu („Leave“, „Stay“, „Save and leave“), zůstává platné.

**Červená fáze doložena** (`TmNavigationGuardLocalizationTests`, 6 testů, 3 selhaly před opravou):

- `Defaults_ComeFromTheConfiguredCulture_NotFromHardcodedEnglish`:
  `Expected .tm-dialog-title to be "Neuložené změny" … ↓ (actual) "Unsaved changes"`
- `Defaults_ResolveWhenTheHostPassesNull_AndNotTheKey`:
  `Bunit.ElementNotFoundException : No elements were found that matches the selector '.tm-dialog-title'`
- `SaveAndLeaveText_ResolvesFromTheCzechResources`:
  `Expected … to be equal to {"Zůstat", "Uložit a odejít", "Odejít"}, but {"Stay", "Save and leave", "Leave"} differs at index 0`

Po opravě 6/6 zelených (spolu s `TmResourceCompletenessTests` 34/34 v daném filtru).

### 8.4 Celá testová sada repa (unit testy po projektech)

Spuštěno po jednom projektu (nikdy celá solution paralelně, kvůli OOM), `LANG/LC_ALL=en_US.UTF-8`,
`-v:q`, TRX do `/tmp/tr-2100`:

| projekt | výsledek |
|---|---|
| Tempo.Blazor.EmailTemplates.Abstractions.Tests | **469/469** |
| Tempo.Blazor.EmailTemplates.Engine.Tests | **496/496** |
| Tempo.Blazor.EmailTemplates.Tests | **1246/1246** |
| Tempo.Blazor.FluentValidation.Tests | **44/44** |
| Tempo.Blazor.DocumentFormats.Tests | **365/365** |
| Tempo.Blazor.Reporting.Tests | **29/29** |
| Tempo.Reporting.Abstractions.Tests | **37/37** |
| Tempo.Reporting.Engine.Tests | **94/94** |
| Tempo.Blazor.Demo.Api.Tests | **237/237** |
| Tempo.Blazor.Mcp.Tests | **410/410** |
| Tempo.ReportServer.Api.Tests | **185/185** (+1 přeskočený) |
| Tempo.ReportServer.Web.Tests | **102/102** |
| Tempo.Blazor.Tests | **12706/12720** — 13 failů, 1 přeskočený |

**13 failů v `Tempo.Blazor.Tests` = PŘEDEXISTUJÍCÍ, ne z tohoto plánu.** Všechny jsou
`DataTable.Snapshots.DefaultMarkupSnapshotTests.DefaultMarkup_MatchesGoldenSnapshot` s datovými řádky
`dt-basic`, `dt-paged`, `dt-selectable`, `dt-virtualized`, `dt-grouping`, `dt-cardmode`, `dt-export`,
`dt-filters`, `dt-viewmanager`, `mvl-card`, `mvl-list`, `mvl-table`, `mvl-grouped`
(zpráva: „default markup … drifted from the pre-redesign snapshot“).

**Důkaz, že jsou pre-existující:** `git worktree add --detach tmp/wt-head HEAD` (HEAD `d331b5ef`, tj. stav
**před** necommitovanými změnami této fáze) a stejný filtr:

```text
cd tmp/wt-head && dotnet test tests/Tempo.Blazor.Tests/Tempo.Blazor.Tests.csproj \
    --filter "FullyQualifiedName~DefaultMarkupSnapshotTests"
→ Failed!  - Failed: 13, Passed: 3, Skipped: 0, Total: 16
```

Tedy stejných 13 scénářů padá i bez jediné změny této fáze. Patří plánu DataTable (golden snapshoty
z redesignu), ne e-mailovému plánu, a **podle zadání se cizí kód neopravuje** — zapsáno jako remaining.

Dvě položky, které F11 hlásila jako červené, jsou po této fázi zelené: `ReleaseContractTests` (opraveno
synchronizací verze na 2.10.0) a `PluralAdoptionTests` / `CultureAssignmentGuardTests` (v době běhu F12 už
neselhaly — počet failů klesl z F11 hlášených 16 na 13, všechny zbývající jsou DataTable snapshoty).

### 8.5 Celá E2E sada

`scripts/run-e2e-full.ps1` vyžaduje `pwsh`, který na tomto stroji **není**; proto byl použitý stejný
příkaz, který skript spouští, jen bez PowerShellu (docs/e2e-test-lanes.md, sekce „Full lane“):

```bash
LANG=en_US.UTF-8 LC_ALL=en_US.UTF-8 TM_E2E_TRACE_ON_FAILURE=false \
dotnet test tests/Tempo.Blazor.E2E/Tempo.Blazor.E2E.csproj \
  --logger "trx;LogFileName=e2e-full.trx" --logger "console;verbosity=minimal" \
  --results-directory /tmp/tr-2100
```

Běží se **jednou, bez filtru** — celá sada (assembly je `[assembly: DoNotParallelize]`, takže sada je
seriální; demo aplikace si testy self-hostují samy). Výsledek: **viz 8.6**.

### 8.6 Výsledek celé E2E sady

**Výsledek běhu:** `Failed: 27, Passed: 2036, Skipped: 92, Total: 2155, Duration: 9 h 9 m`
(start 2026-10-22T19:11:24Z, end 2026-10-23T04:20:54Z; `TestResults/host-restarts.jsonl` **nepřibyl** —
žádná resurrection hostů, 7 řádků v souboru je historie z 2026-10-07/08).

#### 8.6.1 Klasifikace 27 failů (izolovaný běh + srovnání s pre-plan baseline `11ae3552`)

Každý fail byl znovu spuštěn **izolovaně** (samostatný `dotnet test --filter`, self-hostované demo apps)
na stromě této fáze; ty, které padly i izolovaně, byly spuštěny na **`tmp/wt-head`** = `git worktree
add --detach tmp/wt-head HEAD` (`d331b5ef`, strom **bez** necommitovaných změn fáze 12).

> **Oprava baseline (release review F12):** `d331b5ef` je poslední commit plánu, takže obsahuje
> **všechny** commity plánu i souběžných prací — mimo jiné U3 `f989453c`. „Padá i na HEAD“ proto
> dokazuje jen „není to z necommitovaných změn fáze 12“, **ne** „není to regrese plánu“. Skutečný
> pre-plan baseline je `11ae3552` (merge `origin/main` před plánem):
> `git worktree add --detach /tmp/wt-base 11ae3552`. Proti němu byly v release review znovu ověřeny
> U18, U19 a U20 (viz tabulka níže).

| # | test | izolovaně na této fázi | na HEAD | na 11ae3552 (pre-plan) | klasifikace |
|---|---|---|---|---|---|
| 1 | `EmailTemplateF11DemoE2ETests.F11_4_LeaveGuard_SavesThePendingEdit_AndThenLeaves` | **PASS** | — | — | **flaky při zátěži** (timeout navigace, ne aserce) |
| 2 | `EmailTemplateReferenceShotsE2ETests.ReferenceShots_Tablet1024` | **PASS** | — | — | **flaky při zátěži** (klik blokován překryvem) |
| 3 | `EmailTemplatesE2ETests.E11_8_FullSendFlow_DeliversSubstitutedEmail` | **PASS** | — | — | **flaky při zátěži** (timeout na `[data-tm-send-success]`) |
| 4 | `DocumentEditorHeadlessExportParityE2ETests.BrowserAndServerExports_AgreeOnPaginationAndTextLayer_AndOpenInTmPdfViewer` | PASS | — | — | **flaky při zátěži** (dokumentovaný env fail — fontconfig) |
| 5 | `DocumentEditorLegalFilingE2ETests.LegalFiling_RendersLineNumbersAndCaseFileHeader` | PASS | — | — | **flaky při zátěži** |
| 6 | `DocumentEditorLocalizationE2ETests.CultureSwitch_RendersEditorToolbarInCzechAndEnglish` | PASS | — | — | **flaky při zátěži** |
| 7 | `DocumentEditorPageBreakE2ETests.Phase6_PageBreakContextMenu_DeleteRestoresPaginationAndPersists` | PASS | — | — | **flaky při zátěži** |
| 8 | `SpreadsheetE2ETests.CanvasJsEngine_KeyboardNavigationKeepsGridFocusAndUpdatesAccessibilityState` | PASS | — | — | **flaky při zátěži** |
| 9 | `WireframeEditorE2ETests.DeletePage_RemovesTab` | PASS | — | — | **flaky při zátěži** |
| 10 | `WireframeEditorE2ETests.DeleteSelectedElement_RemovesIt` | PASS | — | — | **flaky při zátěži** |
| 11 | `WireframeEditorE2ETests.SelectElement_PropertiesPanelShowsFields` | PASS | — | — | **flaky při zátěži** |
| 12 | `RelativeTimeE2ETests.Timeline_English_ShowsHoursAgo_NotARawKey` | PASS | — | — | **flaky při zátěži** |
| 13 | `RelativeTimeE2ETests.Timeline_Czech_And_French_UseTheirOwnWords` | PASS | — | — | **flaky při zátěži** |
| 14 | `Feedback.SheetRound4E2ETests.ViewManager_CreateModal_OpensOnThatInstanceNameField(390,2)` | PASS (390,2) | — | — | **flaky při zátěži** (řádky 1440/390,0 prošly i v plném běhu) |
| 15 | `DocumentEditorPageSearchE2ETests.PageSearch_Enter_NavigatesToPage` | PASS (1×) | PASS | — | **flaky** (stav/pořadí; 2× fail, 2× pass) |
| 16 | `DashboardF6SelectorE2ETests.Selector_Desktop1440_SidebarFilter_AddedState_AndKeyboardOnly` | FAIL | **FAIL** | — | **předexitující** — aserce `'Added (1)'` vs `Přidáno` |
| 17 | `DashboardF6SelectorE2ETests.Selector_Mobile390_Sheet_ChipFilter` | FAIL | **FAIL** | — | **předexitující** — timeout na `Hotovo` |
| 18 | `EditorShellE2ETests.Demo_320_Mobile_NoHorizontalOverflow` | FAIL | FAIL | **PASS** | **regrese testu z U3 — opraveno ve F12** — U3 `f989453c` dal na `(pointer: coarse)` tabům `min-block-size: var(--tm-touch-target)` = 44 px, test ale assertoval výšku `< 40` („tab labels must not wrap: 44,44“); F12 měří zalomení textu (`document.createRange().getClientRects().length == 1`) a výšku už jen shora (≤ 44,5 px) |
| 19 | `EditorShellE2ETests.Demo_320_Czech_TabLabelsStayOnOneLine` | FAIL | FAIL | **PASS** | **regrese testu z U3 — opraveno ve F12** — stejná aserce výšky tabu, stejná oprava (měření zalomení + strop 44,5 px) |
| 20 | `EditorShellE2ETests.Demo_390_SwipeClose_DoesNotStealFocusFromAnActionBarButton_ButEscapeReturnsItToTheToggle` | FAIL | FAIL | **FAIL** | **předexitující — ověřeno i na pre-plan baseline**: stejná `PlaywrightException: Execution context was destroyed, most likely because of a navigation` při `sheet.Locator('.tm-sheet__handle').EvaluateAsync` na HEAD i na `11ae3552`, takže to není regrese U3 ani plánu; oprava patří vlastníkovi `TmDrawer`/swipe-close, ne tomuto plánu |
| 21 | `ModelingPerformanceM26E2ETests.ModelingEditor_LcpStaysUnderBudget` | FAIL | **FAIL** | — | **předexitující** — performance budget |
| 22 | `ModelingPerformanceM26E2ETests.LargeModel_LoadAndGenerateStayWithinBudgets` | FAIL | **FAIL** | — | **předexitující** — 4202 ms vs limit 3000 ms |
| 23 | `ModelingPerformanceM26E2ETests.LargeSurfaces_ScrollWithoutJank` | FAIL | **FAIL** | — | **předexitující** |
| 24 | `ModelingPerformanceM26E2ETests.NotationSwitch_RepaintsTreeUnderBudget` | FAIL | **FAIL** | — | **předexitující** |
| 25 | `OverlayMobilePresentationE2ETests.DatePicker_At390_InModalSheet_OpensNestedSheet` | FAIL | **FAIL** | — | **předexitující** — „modal must survive the calendar's Escape“ |
| 26 | `OverlayRound2FixE2ETests.NestedSheet_FromPromotedModal_PaintsAboveTheModal` | FAIL | **FAIL** | — | **předexitující** |
| 27 | `UserTimeZoneServerE2ETests.UserTimeZone_Server_FormatsInTheBrowserZone` | FAIL | **FAIL** | — | **předexitující** |

**Závěr (po release review):** z 27 failů je **10 předexitujících** (padají i na pre-plan baseline
`11ae3552`, všechny mimo e-mailové balíčky) a **15 flaky při zátěži** plného běhu (izolovaně zelené).
**Dva faily (U18, U19) byly regrese testu z U3** — U3 `f989453c` (schválené uživatelem) dal tabům na
`(pointer: coarse)` `min-block-size: var(--tm-touch-target)` = 44 px, zatímco oba testy assertovaly
výšku `< 40`; ve F12 jsou opravené (měří se zalomení textu přes `Range.getClientRects().length == 1`,
výška se jen shora omezuje na ≤ 44,5 px) a na `11ae3552` procházejí. **Žádný fail tedy není regrese
e-mailového kódu plánu** — tři e-mailové specy (F11_4 leave guard, ReferenceShots_Tablet1024,
E11_8 send flow), tedy jediné, které
procházejí kódem změněným v této fázi, jsou izolovaně **zelené**; F11_4 navíc cvičí právě
`TmNavigationGuard` s „Save and leave“, takže lokalizace jeho textů cestu ne rozbila.

Všech 10 předexitujících failů je mimo e-mailové balíčky (Dashboard selector, `EditorShellE2ETests` U20 —
swipe-close padá i na `11ae3552`, modeling
performance, overlay sheets, user time zone) — podle zadání se cizí kód neopravuje, zapsáno jako remaining.

**Re-run třídy `EditorShellE2ETests` po opravě** (`--filter FullyQualifiedName~EditorShellE2ETests`, 42
testů, self-hostované demo apps): **41/42** — U18 i U19 zelené, jediný fail zůstává U20
`Demo_390_SwipeClose…`, který padá i na `11ae3552`.

**E-mailová lane znovu celá** (filtr `EmailTemplate|EmailEditor|EmailWorkspace`, 113 testů, stejný
příkaz jako F11):

| běh | výsledek | poznámka |
|---|---|---|
| 1 | 105/113 | 6× `Timeout 60000ms exceeded` (host readiness — `F5_1`, `F11_5`, `F11_7`, `Tablet_1024`, `Drop_ButtonBlock`, `Dark_mode_keeps_the_paper_white`) + 1× `net::ERR_NETWORK_CHANGED` (`F11_6`) + `F11_3` (čekal `.tm-modal--sheet`, našel 0) — **kontaminace ze stale hostů** předchozích izolovaných běhů (přesně ten stav, před kterým varuje docs/e2e-test-lanes.md); `TestResults/host-restarts.jsonl` přitom nevzrostl |
| 2 (čistý, po `rm -rf tests/Tempo.Blazor.E2E/TestResults`) | **113/113** | 23:36 — celá e-mailová lane zelená se změnami této fáze (včetně `F11_4` leave guard, `F11_5`, `F5_1`, `F11_3`) |

Tím je splněn i požadavek F10/F11 „před uzavřením F12 znovu spustit celý email lane“ — a to dvakrát
(plný běh sady 2036/2155 a čistý lane běh 113/113).

**Poznámka k artefaktům:** plný běh přegeneroval 339 trackovaných PNG v `__screenshots__/` (a 3 v
`screenshots/`) — jsou to **nestabilní artefakty** (obsahují náhodná GUID a relativní časy; ověřeno
porovnáním `08-send-form.png` mezi HEAD a během: jediný rozdíl je náhodné GUID příjemce, layout je
identický). Fáze 12 nemění UI, takže byly vráceny (`git checkout --`), aby diff neobsahoval 339 binárních
souborů; kdo je chce aktualizovat, ať tak učiní samostatným commitem z vlastního běhu.

## 9. Fáze 7 plánu 643bba06 — ReportServer bez enginu, náklad commitu a stav pickeru

### 9.1 ReportServer a D11

`Tempo.ReportServer.Web.Client` (WebAssembly noha InteractiveAuto portálu) už **nereferencuje Engine**:
`AddCommonServices` (běží v obou nohách) registruje jen model, úložiště a bránu `IReportScheduleRunner`;
`AddTempoEmailTemplateEngine()` + `ReportEmailDeliveryService` + `ReportScheduleWorker` registruje serverový
hostitel přes `AddReportServerEmailDelivery()` (`Tempo.ReportServer.Web`). Výjimka v `PackageBoundaryTests`
zmizela: žádný WASM hostitel nesmí mít Engine v uzávěru `ProjectReference`. Tlačítko „Run now“ se v prohlížeči
nenabízí (bez runneru není co spustit) — viz CHANGELOG „Breaking / Migration“.

### 9.2 Náklad commitu editoru

Dřív jeden commit (`CommitAsync`) dělal `DeepClone()` pro historii (serializace + deserializace) a znovu celou
serializaci kvůli `RecomputeDirty()`; undo/redo vracely `DeepClone()`. Teď:

* `EditorHistoryService` drží snímek jako **neměnný JSON řetězec** + **`Version`** (monotónní id stavu, nikdy
  se nepoužije dvakrát; undo/redo vrací verzi stavu, na který dopadly). Snímek = jedna serializace; deserializace
  se platí jen při skutečném undo/redo.
* `IsDirty` = `_history.Version != _baselineVersion` (O(1)). Baseline je verze snímku, který hostitel potvrdil
  (úprava během letícího ukládání zůstává dirty; undo na uložený stav je zase čisté).
* Koalescence zůstala (500 ms, stejný klíč): koalescovaný push je **nová verze**, takže úprava po uložení nikdy
  nevypadá jako čistá.
* Náhled (`TmEmailTemplatePreview.DocumentRevision`, volitelné, aditivní): „změnil se dokument?“ je porovnání
  reference + revize, ne serializace dokumentu při každém re-renderu rodiče. Editor předává svůj `_revision`;
  bez parametru se chová jako dřív (porovnání obsahu).

Měření (xUnit, Debug sestavení, 300 opakování, průměr jednoho commitu = náklad serializace/klonování
dokumentu s textovými bloky ≈ 70 znaků; bez vykreslení):

| Bloků | Commit před (clone + serializace) | Commit po (jedna serializace) | Poměr |
|---|---|---|---|
| 3 | 0,147 ms | 0,054 ms | 2,7× |
| 52 | 0,954 ms | 0,310 ms | 3,1× |
| 200 | 3,892 ms | 1,110 ms | 3,5× |

Celý klik „přidat blok“ včetně bUnit vykreslení (měřeno před změnou, `AddBlock` → `CommitAsync` →
`StateHasChanged`): 3 bloky ≈ 18 ms, 52 ≈ 36 ms, 200 ≈ 304 ms. U 200 bloků tedy dominuje vykreslení plátna
(`EmailCanvasRenderGate` už v F9A omezuje přestavbu na změněné bloky), ne model; cíl **< 100 ms na stisk v Server
režimu** drží datová část s rezervou o dva řády (≈ 1 ms), plátno řeší signatura bloku z F9A.

### 9.3 Stav variable pickeru přežije layout flip

Rozhodnutí `EMAIL-VARIABLE-PICKER-FILTER`: `TmEmailVariablePicker.Filter`/`FilterChanged` (`@bind-Filter`),
otevřený picker a jeho filtr žijí v `EmailInspectorState`, který vlastní **editor** (`TmEmailPropertyPanel.State`).
Reset filtru = zavření pickeru (nebo jiný uzel); layout flip, který inspektor znovu vytvoří, filtr neresetuje.
`TmEmailTemplateEditor` nedostal žádný parametr — kontrakt §3 (PostrionCore P06) beze změny.