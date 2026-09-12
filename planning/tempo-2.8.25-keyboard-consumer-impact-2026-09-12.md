# Dopad vydání 2.8.25 (klávesová emulace) na konzumenty

Datum: 2026-09-12
Předmět: analýza repozitářů pod `/home/pavel/NetProjects`, které referencují balíčky Tempo.Blazor,
a co pro ně znamená přechod na 2.8.25 (odstranění redundantní Enter/Space emulace, scopované
container handlery, propagation bariéry, focus-restore po zavření popupu, nová klávesová cesta
v `TmMultiSelect` bez filtrování, `TmFilterableDropdown.AriaLabel`).

## Co se v 2.8.25 mění a proč na tom konzumentovi záleží

- **Odebrání double-fire je čistá oprava.** Reálný prohlížeč na Enter/Space vždy vyráběl nativní
  click; emulace v `keydown` ho spustila podruhé. Kdokoli si toho všiml jako chyby (dvojí submit,
  znovuotevřený popup, dvojí row-click) dostane opravu zdarma.
- **Space se přesunul z `keydown` na `keyup`** — nativní časování. Test, který posílá jen
  `KeyDown(" ")` a čeká aktivaci, musí poslat skutečnou posloupnost (`keydown` → `keyup` → `click`,
  nebo prostě `Click`). Stejný test pod starým kódem asertoval druhou, vadnou invokaci.
- **Konzumentem připnuté `@onkeydown` callbacky dál běží, ale už neaktivují.** Handler na
  `TmButton`/položce menu událost pořád uvidí; komponenta z něj už click action nevolá.
- **Focus se po zavření `TmFilterableDropdown`/`TmMultiSelect` vrací na trigger.** Testy nad
  `document.activeElement` nebo nad počtem `FocusAsync` interop volání to uvidí.
- **`TmMultiSelect` s `AllowFiltering=false` má nově klávesový ovládání** (šipky +
  Enter/Space na triggeri, `aria-activedescendant`). Aditivní, ale testy nad DOM strukturou
  triggeru uvidí nové atributy.
- **`TmFilterableDropdown` trigger má nově Enter/Space/ArrowDown a položky `role="option"`** +
  `aria-selected`; nový parametr `AriaLabel` je opt-in.

## Nalezení konzumenti

Grep přes `*.csproj` pod `/home/pavel/NetProjects` (vyjma obou checkoutů samotného Tempo.Blazor):
`PackageReference Include="Tempo.Blazor*"` a CPM `PackageVersion`. Žádný konzument nemá
`ProjectReference` do tohoto repozitáře.

| Repozitář | Projekty | Balíčky a verze |
|---|---|---|
| `matrika` | `src/Rodopisna.Client` | `Tempo.Blazor 2.3.9-preview` |
| `matrika-release-6c80a94` | `src/Rodopisna.Client` | `Tempo.Blazor 2.3.9-preview` (release snapshot) |
| `AiRealCrm` | `src/CRM.Web` | `Tempo.Blazor`, `.Abstractions`, `.FluentValidation` **1.1.0** |
| `PromptHelper` | `Web`, `Api`, `Infrastructure`, `Shared` | `Tempo.Blazor`, `.Abstractions`, `.FluentValidation`, `.Mcp`, `.Wireframe`, `.NotionEditor` **2.8.4** |
| `LexiQuest` | `Blazor.Client`, `Web` | `Tempo.Blazor`, `.Abstractions`, `.FluentValidation` **1.1.15** |
| `TestMasterCourse` | `Web.Shared`, `Web.Client`, `Web`, `School.Web`, `School.Web.Client`, `Web.Rcl` | `Tempo.Blazor`, `.Abstractions`, `.FluentValidation` **1.1.15** |
| `testmaster` | `src/TestMaster.Web.V2` + `tests/TestMaster.Web.V2.Tests` | `Tempo.Blazor` + `.Abstractions` **1.1.13**, `.FluentValidation` **1.1.12** |
| `advocatus` | `App.Client`, `App`, `Portal` | `Tempo.Blazor`, `.FluentValidation` přes CPM `$(TempoVersion)` = **2.8.4** (`Directory.Packages.props`) |

## Použití dotčených komponent a klávesových testů

Změřeno grepem po `*.razor` (počet souborů obsahující komponentu) a po testech posílajících
`KeyDown`/`KeyUp` na Tempo elementy.

| Repo | TmButton | TmToggle | TmAccordionItem | TmDataTable | TmMultiSelect | TmFilterableDropdown | Klávesové testy nad Tempo komponentami |
|---|---|---|---|---|---|---|---|
| matrika | 46 | 3 | 0 | 1 | 0 | 0 | žádné nalezené (jen Playwright E2E nad skutečným prohlížečem) |
| AiRealCrm | 59 | 7 | 0 | 15 | 0 | **24** | žádné nalezené |
| PromptHelper | 270 | 15 | 13 | 43 | 3 | 1 | `KeyDown` testy cílí vlastní `CommandPalette`/inputy, ne Tempo komponenty |
| LexiQuest | 30 | 0 | 0 | 0 | 0 | 0 | `KeyDown("Enter")` jen nad vlastními inputy |
| TestMasterCourse | 132 | 0 | 3 | 2 | 0 | 0 | žádné nalezené |
| testmaster | 93 | 2 | 0 | 4 | 1 | 0 | `KeyDown` testy cílí vlastní dropdown-menu/grid inputy |
| advocatus | 6 | 1 | 0 | 2 | 0 | 0 | žádné nalezené |

## Dopad a doporučený postup per repo

- **PromptHelper (2.8.4 → 2.8.25): největší exponovaná plocha.** 270 souborů s `TmButton` — za starého
  chování každý Enter/Space na tlačítku běžel dvakrát, takže přechod může *projevit* skryté dvojí
  efekty (idempotentní akce to zamaskují; nedempotentní — např. "vytvoř a přejdi" — se teď stanou
  jednoduchými). 24× `TmFilterableDropdown` u AiRealCrm a 1× tady dostávají nové `role="option"`/
  `aria-selected` atributy — testy asertující přesný markup/menu strukturu je třeba přehlédnout.
  Jejich bUnit klávesové testy míří vlastní komponenty → žádná povinná úprava testů nenalezena.
- **matrika (2.3.9-preview → 2.8.25): velký skok.** Přes preview→stable přechod se nepojí jen s
  touto fázou; klávesová stránka je pro ni čisté vylepšení (46 souborů TmButton, žádné simulační
  klávesové testy — E2E přes Playwright běží na skutečném prohlížeči, kde oprava dvojité aktivace
  platí). Snapshot `matrika-release-6c80a94` nemá smysl aktualizovat.
- **AiRealCrm (1.1.0):** 24 souborů s `TmFilterableDropdown` — nové listbox ARIA atributy a
  focus-restore se dotknou testů asertujících DOM po otevření/zavření; jinak čistá oprava.
- **testmaster / TestMasterCourse / LexiQuest (1.1.x):** povrch hlavně přes `TmButton`. Jejich
  `KeyDown` testy cílí vlastní elementy → bez povinných úprav; profitují z odstranění double-fire.
- **advocatus (2.8.4, CPM):** stačí zvednout `<TempoVersion>` v `Directory.Packages.props`; malá
  plocha (6 souborů TmButton), žádné klávesové testy.

## Shrnutí pro poznámky k vydání

Pro všechny konzumenty je adoption `dotnet add package Tempo.Blazor --version 2.8.25` (resp. bump
`TempoVersion`/`Version=`). Povinná akce existuje jen pro testovací sestavy, které simulují
keydown-only Space/Enter proti Tempo komponentám — v nalezených repozitářích se žádná taková
nevyskytuje; klávesové testy míří vlastní komponenty a inputy. Největší provozní změna je
odstranění double-fire: akce, které se dosud spustily dvakrát, poběží jednou.
