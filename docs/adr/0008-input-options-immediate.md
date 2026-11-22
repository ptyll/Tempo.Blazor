# ADR-0008: `TmInputOptions.ImmediateByDefault` — commit textových vstupů při `input` (`INPUT-OPTIONS-IMMEDIATE`)

- **Status:** Navrženo (čeká na schválení vlastníka)
- **Datum:** 2026-11-22
- **Kontext plánu:** Tempo.Blazor 2.13 Fáze 7a (dluhy z 2.12), plán 1c95309c, subtasky A–E
- **Kód rozhodnutí:** `INPUT-OPTIONS-IMMEDIATE`
- **Návrhový dokument:** [docs/proposals/input-options-immediate.md](../proposals/input-options-immediate.md)
- **Cílový release:** 2.13.0 (všechny změny veřejného API jsou aditivní; žádný Breaking / Migration)

## Kontext

`TmTextArea.Immediate` je `bool` (default `false`): `ValueChanged` na `change`/`blur`, při `true` na `input`.
`TmTextInput` parametr `Immediate` nemá a volá `ValueChanged` na `input` i na `change`. Host, který ukládá
formulář zkratkou dřív, než pole ztratí fokus (EmusCz `oprisk-save-shortcut.js` ř. 17–23), musí před uložením
vynutit commit. Cíl: host jednou řekne, že se textové vstupy commitují při každém `input`, bez Breaking a bez
změny výchozích hodnot. EmusCz se v 2.13 nemění.

## Rozhodnutí (návrh ke schválení)

1. **Tvar je kombinace.** DI (`AddTempoBlazor` / `Configure<TmInputOptions>`) je globální výchozí.
   `TmInputOptionsProvider` (`CascadingValue`, `IsFixed="true"`) je lokální přepsání na úrovni formuláře
   nebo sekce a nezasáhne komponenty mimo strom. Samotná kaskáda nestačí aplikaci typu EmusCz; samotné DI
   nedává přepsání formuláře, což je hlavní use case.
2. **Priorita resolve:** explicitní parametr komponenty → kaskádované `TmInputOptions` → DI → fallback
   komponenty (`TmTextArea` = `false`, `TmTextInput` = `true`). Jediná kopie logiky je
   `TmInputOptionsResolver.IsImmediate`.
3. **Umístění typu:** `src/Tempo.Blazor/Services/TmInputOptions.cs` vedle `ToastOptions`. Typ je POCO bez UI,
   ale provider je komponenta a resolver patří ke vstupům; Abstractions by byl overkill.
4. **`TmTextArea.Immediate` zůstává `bool`.** Změna na `bool?` je zdrojově nekompatibilní pro C# čtení
   (`if (ta.Immediate)`) a mění binární signaturu — Breaking, které 2.13 zakazuje. Aditivní
   `bool? ImmediateMode` je explicitní přepsání včetně vynuceného `false`. `Immediate="true"` vždy
   commituje na input. Default / `Immediate="false"` options nepřekáží (jinak by globální zapnutí
   u žádné dnešní TextArea nefungovalo). Vynucené blur-commit při `ImmediateByDefault=true` je jen
   `ImmediateMode="false"`.
5. **`TmTextInput` dostane aditivní `bool? Immediate`.** `null` nebo `true` = dnešní chování (každý vstup);
   `false` = jen change/blur. Options ho mění jen při výslovném `ImmediateByDefault = false`.
6. **Dotčené vstupy jsou jen `TmTextArea` a `TmTextInput`.**
   - `TmNumberInput` má jen `@onchange` — nerozlišuje input a change.
   - `TmSearchInput` má vlastní `DebounceMs`; Immediate by s ním kolidoval.
   - `TmDecimalInput` sice input a change rozlišuje (`_liveText` vs commit na change), ale commit při
     psaní rozbije parsování a formátování podle kultury.
   - Ostatní vstupy ImmediateByDefault nedostanou.

## Důsledky

- Bez options je DOM i počet `ValueChanged` 1:1 jako dnes.
- Stávající bezparametrická `AddTempoBlazor()` se nemění.
- bUnit bez registrace `IOptions<TmInputOptions>` nesmí spadnout (volitelný inject).
- Kroky C–E (implementace, testy, dokumentace) **nezačínají**, dokud vlastník toto ADR neschválí.
- Navazující EmusCz plán odstraní obchvat `oprisk-save-shortcut.js` až na vydané 2.13.

## Zamítnuté alternativy

- **Jen CascadingValue:** chybí globální výchozí pro EmusCz.
- **Jen DI:** chybí přepsání na úrovni formuláře.
- **`bool? Immediate` na `TmTextArea`:** Breaking (C# čtení + binární signatura), i kdyby Razor přiřazení
  zůstalo kompatibilní.
- **Řídit `TmNumberInput` / `TmSearchInput` / `TmDecimalInput`:** buď nemají co přepínat, nebo by Immediate
  rozbil debounce / kulturní parse.
