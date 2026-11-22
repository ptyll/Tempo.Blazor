# Návrh: `TmInputOptions.ImmediateByDefault` (commit textových vstupů při každém `input`)

> **Stav:** čeká na schválení vlastníka, viz [ADR-0008](../adr/0008-input-options-immediate.md).
> **Cílový release:** **2.13.0**. Všechny změny veřejného API jsou **aditivní**; výchozí hodnoty se nemění.
> **Rozsah:** `Tempo.Blazor` (typ, provider, resolver, `TmTextArea`, `TmTextInput`, `AddTempoBlazor`).
> EmusCz se v 2.13 **nemění** — obchvat `oprisk-save-shortcut.js` (ř. 17–23, commit na blur před
> uložením zkratkou) odpadne až v navazujícím plánu po vydání 2.13.
> **Dluh:** `5bf164f5` / původní subtask `a901d42c` z plánu `98c80bc3`, backlog `83bd9915`.

## 0. Shrnutí rozhodnutí (návrh)

| Otázka | Doporučení |
|--------|------------|
| Tvar | **Kombinace:** DI jako globální výchozí + `CascadingValue` jako lokální přepsání. |
| Priorita | explicitní parametr komponenty → kaskáda → DI → fallback komponenty. |
| Umístění typu | core `src/Tempo.Blazor/Services/TmInputOptions.cs` vedle `ToastOptions` (POCO bez UI, ale provider je komponenta a resolver patří ke vstupům). |
| `TmTextArea.Immediate` | **ponechat `bool`**, přidat aditivní `bool? ImmediateMode`. Změna na `bool?` by rozbila C# čtení (`if (ta.Immediate)`) a binární signaturu — pravidlo „žádný Breaking“ to zakazuje. |
| `TmTextInput.Immediate` | nový aditivní `bool?`; výchozí efektivní režim zůstává **true** (dnešní `oninput` + `onchange`). |
| Dotčené vstupy | jen `TmTextArea` a `TmTextInput`. `TmNumberInput`, `TmSearchInput`, `TmDecimalInput` a ostatní **ne**. |

## 1. Problém

Host (EmusCz) potřebuje, aby se hodnoty textových vstupů committovaly při každém `input` (oninput),
ne až na `change`/`blur`. Dnes:

- **`TmTextArea.Immediate`** je `bool`, default `false`: `ValueChanged` na `change`/`blur`; při `true` na `input`.
- **`TmTextInput`** parametr `Immediate` nemá a volá `ValueChanged` na `input` i na `change`
  (`HandleInputAsync` i `HandleChangeAsync`) — tedy efektivně Immediate = true.
- Host, který ukládá formulář zkratkou (Ctrl/⌘+S) dřív, než pole ztratí fokus, musí před uložením
  vynutit commit. EmusCz to obchází v `oprisk-save-shortcut.js` ř. 17–23 (blur aktivního pole).
  Obchvat odpadne, až host jednou řekne „commituj při každém vstupu“.

Cíl 2.13: host to řekne **jednou** (globálně nebo na úrovni formuláře/sekce), bez Breaking a bez
změny výchozích hodnot.

## 2. Tři tvary

| | A. Jen CascadingValue | B. Jen DI | C. Kombinace (doporučeno) |
|--|----------------------|-----------|---------------------------|
| **Tvar** | `<TmInputOptionsProvider ImmediateByDefault="true">` kaskáduje `TmInputOptions` stromem. Žádné DI. | `AddTempoBlazor(o => o.Inputs.ImmediateByDefault = true)` nebo `services.Configure<TmInputOptions>`. Globální. | DI jako globální výchozí; `TmInputOptionsProvider` jako lokální přepsání. |
| **Lokální přepsání** | Ano (formulář / sekce). Komponenty mimo strom nezasáhne. | Ne. | Ano — kaskáda přebije DI. |
| **Globální výchozí** | Ne (každý formulář zvlášť, nebo wrap celé aplikace). | Ano — EmusCz jednou v `Program.cs`. | Ano. |
| **Bez DI** | Funguje. | Ne. | Funguje (kaskáda + fallback). |
| **bUnit bez registrace** | Bezpečné. | Musí nespadnout (`IOptions<TmInputOptions>?`). | Stejně: volitelný inject. |
| **Hlavní use case** | Přepsání na úrovni formuláře. | Aplikace typu EmusCz. | Obojí. |

### 2.1 Výhody a nevýhody

| Tvar | Výhody | Nevýhody |
|------|--------|----------|
| **A. CascadingValue** | Žádné DI, lokální přepsání, nezasáhne komponenty mimo strom, bUnit bez registrace. | Host typu EmusCz musí wrapnout celou aplikaci (nebo každý formulář). Globální výchozí neexistuje. |
| **B. DI** | Jedno `Configure` v `Program.cs`. | Žádné lokální přepsání. Formulář, který chce dnešní `TmTextArea` (commit na blur), by musel nastavit parametr na každé instanci. |
| **C. Kombinace** | Globální výchozí **i** přepsání na úrovni formuláře. Priorita je jednoznačná. | Dva vstupní body (DI + provider). Resolver musí být jedna sdílená funkce, žádné kopie. |

**Doporučení: C.** Přepsání na úrovni formuláře je hlavní use case (sekce, dialog, wizard) a globální
výchozí řeší aplikaci typu EmusCz. Priorita:

1. explicitní parametr komponenty (`Immediate` / `ImmediateMode`);
2. kaskádované `TmInputOptions` (`TmInputOptionsProvider`);
3. DI (`IOptions<TmInputOptions>`);
4. fallback komponenty (`TmTextArea` = `false`, `TmTextInput` = `true`).

`TmInputOptionsResolver.IsImmediate(explicitValue, cascaded, di, fallback)` je jediná kopie logiky.

## 3. Umístění typu

`TmInputOptions` je POCO bez UI. Dvě místa:

| Umístění | Pro | Proti |
|----------|-----|-------|
| `Tempo.Blazor.Abstractions` | Zero-UI pravidlo, backend ho může číst. | Backend ImmediateByDefault nepotřebuje. Provider je komponenta. |
| `src/Tempo.Blazor/Services/` vedle `ToastOptions` | Provider je komponenta, resolver patří ke vstupům, `AddTempoBlazor` už registruje služby z tohoto adresáře. | Balíček core táhne Blazor (ale host vstupy stejně referencuje core). |

**Doporučení: core vedle `ToastOptions`.** Abstractions by byl overkill: nikdo mimo UI options
nekonzumuje.

Stávající bezparametrická `AddTempoBlazor()` zůstává. Aditivní overload
`AddTempoBlazor(Action<TempoBlazorOptions>)` nebo `services.Configure<TmInputOptions>` —
`ImmediateByDefault` je `bool?`, default `null` (= „nerozhodnuto, použij fallback komponenty“).

## 4. Kompatibilita `TmTextArea.Immediate` (`bool` → `bool?`)

Změna `[Parameter] public bool Immediate` na `bool?` je:

- **zdrojově kompatibilní** pro Razor přiřazení (`Immediate="true"` / `Immediate="false"`);
- **zdrojově nekompatibilní** pro C# kód, který vlastnost **čte** jako `bool` (`if (ta.Immediate)`,
  `bool x = ta.Immediate`);
- **binárně nekompatibilní** (změna signatury veřejného členu).

Pravidlo této fáze: **žádný Breaking / Migration v CHANGELOG 2.13.0**. Proto:

| Varianta | Verdikt |
|----------|---------|
| `bool` → `bool?` s odůvodněním „parametry komponent se nečtou“ | **Zamítnuto.** Host smí číst `cut.Instance.Immediate` v testech i v C# wrappech. Binární změna je Breaking i bez čtení. |
| Ponechat `bool Immediate` (default `false`) a přidat **`bool? ImmediateMode`** | **Doporučeno.** `Immediate = true/false` zůstává explicitní přepsání (dnešní sémantika). `ImmediateMode` je aditivní „null = z options“. Resolver: je-li `Immediate == true` **nebo** `ImmediateMode == true` → true; je-li `Immediate == true` nemá smysl s `ImmediateMode == false` — `Immediate` (bool, už nastavený) vyhrává, protože historický parametr je explicitní. Čistší pravidlo: `ImmediateMode` má přednost, když je non-null; jinak `Immediate` (bool) je explicitní true, explicitní false se bere jako „neřeknuto“, pokud host nastavil jen default. To je ale past: default `false` nejde odlišit od `Immediate="false"`. |

**Rozhodnutí o čtení `false`:** u `bool` nejde `unset` odlišit od `false`. Proto:

- **`Immediate` (`bool`, default `false`) zůstává dnešní explicitní zapnutí.** `Immediate="true"`
  vždy commituje na input, bez ohledu na options. `Immediate="false"` (a default) **není**
  přepsání — options smí zapnout immediate. Host, který chce **vynutit** blur-commit i při
  `ImmediateByDefault=true`, nastaví nové **`ImmediateMode="false"`**.
- Tím `Immediate="false"` (dnešní default i dnešní explicitní false) **nemění chování bez options**
  a zároveň nepřekáží globálnímu zapnutí. Jediný nový způsob, jak options přebít na false, je
  `ImmediateMode="false"`.
- Alternativa „`Immediate=false` je explicitní přepsání“ by znemožnila globální zapnutí u každé
  `TmTextArea`, která parametr nechala na defaultu — tedy u všech dnešních call-sitů. To by
  ImmediateByDefault pro TextArea nefungovalo.

Toto musí **výslovně schválit vlastník**. Varianta `bool? Immediate` je zamítnutá kvůli Breaking.

`TmTextInput` parametr dnes nemá. Nový `bool? Immediate`:

- `null` (default) = dnešní chování (commit na input i change), options ho mění **jen** při
  výslovném `ImmediateByDefault = false`;
- `true` = dnešní chování;
- `false` = commit jen na change/blur.

Bez options je DOM i počet `ValueChanged` 1:1.

## 5. Krok B — které vstupy se řídí

Ověřeno ve zdrojích:

| Komponenta | Dnes | Řídí se? | Důvod |
|------------|------|----------|-------|
| **`TmTextArea`** | `Immediate` bool, default false: `ValueChanged` na change/blur, při true na input. | **Ano** | Jediný dnešní přepínač input vs change. |
| **`TmTextInput`** | Žádný Immediate; `HandleInputAsync` i `HandleChangeAsync` volají `ValueChanged`. | **Ano** | Host typu EmusCz ukládá zkratkou právě textová pole; aditivní `bool? Immediate` (default null = dnešní true). |
| **`TmNumberInput`** | Jen `@onchange`. Nerozlišuje input a change. | **Ne** | Není co přepínat. Immediate by zavedl nový kanál, který dnes neexistuje. |
| **`TmSearchInput`** | Input s `DebounceMs` a change, které debounce ruší. | **Ne** | Immediate by kolidoval s debounce — dva režimy „kdy poslat hodnotu“. |
| **`TmDecimalInput`** | `HandleInput` jen ukládá `_liveText`; commit jen na change (parsování a formát podle kultury). | **Ne** | Commit při psaní rozbije mezistavy (`"1,"`, `"1.2e"`) a kulturu. |
| Ostatní vstupy (`TmSelect`, `TmCheckbox`, `TmToggle`, `TmOtpInput`, pickery, …) | Vlastní commit model. | **Ne** | ImmediateByDefault je o textovém `input` vs `change`. |

Seznam je uzavřený: 2.13 nepřidává Immediate na další komponenty. Rozšíření je samostatné rozhodnutí.

## 6. Veřejné API (po schválení, krok C)

```csharp
namespace Tempo.Blazor.Services;

/// <summary>Host-wide defaults for text inputs. Additive; null means "not set".</summary>
public sealed class TmInputOptions
{
    /// <summary>
    /// When true, text inputs commit on every input event. When false, they commit on change/blur.
    /// Null (the default) leaves each component's own fallback.
    /// </summary>
    public bool? ImmediateByDefault { get; set; }
}
```

- `TmInputOptionsProvider` (`Components/Inputs/`): `CascadingValue` `IsFixed="true"`, `ChildContent`,
  parametr `ImmediateByDefault`.
- `TmInputOptionsResolver.IsImmediate(bool? explicitValue, bool? cascaded, bool? di, bool fallback)` —
  čistá statická funkce, jediná kopie.
- `TmTextArea`: stávající `bool Immediate` + nový `bool? ImmediateMode`; `[CascadingParameter]`
  options; volitelný `[Inject] IOptions<TmInputOptions>?` (bUnit bez registrace nesmí spadnout).
- `TmTextInput`: nový `bool? Immediate` (default `null`).
- Fallback: TextArea `false`, TextInput `true`.

## 7. Testy (krok D, RED před C)

- `TmInputOptionsResolverTests` — matice `[Theory]`.
- `TmTextAreaTests` / `TmTextInputTests` — parametr × kaskáda × DI × default; regrese 1:1
  (`MarkupMatches` + počet `ValueChanged` na input i change); `EditContext.NotifyFieldChanged`
  podle efektivního režimu.
- DI registrace v `ServiceCollectionExtensionsPhase1Tests`.
- E2E `/forms` (WASM i Server) přes `CreateContextAsync` / `GotoAppReadyAsync`.

## 8. Rozhodnutí vlastníka (gate)

Kroky C–E **nezačínají**, dokud toto není schválené. Otázky:

| # | Otázka | Doporučení |
|---|--------|------------|
| 1 | Tvar | Kombinace (DI + CascadingValue). |
| 2 | Priorita | parametr → kaskáda → DI → fallback. |
| 3 | Umístění `TmInputOptions` | core `Services/` vedle `ToastOptions`. |
| 4 | `TmTextArea.Immediate` | ponechat `bool`, přidat `bool? ImmediateMode`. `Immediate="true"` vždy vyhraje; default/`false` options nepřekáží; vynucené false jen přes `ImmediateMode="false"`. |
| 5 | Dotčené vstupy | jen `TmTextArea` a `TmTextInput`. Number/Search/Decimal a ostatní ne. |

Kód rozhodnutí: `INPUT-OPTIONS-IMMEDIATE`. ADR: [0008](../adr/0008-input-options-immediate.md).
