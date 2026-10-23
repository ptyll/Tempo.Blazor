# ADR-0004: Tlačítko pro výběr souboru v toolbaru (`TOOLBAR-FILE-BUTTON`)

- **Status:** Přijato
- **Datum:** 2026-10-23
- **Kontext plánu:** Plán „Uzavření remaining tasků" (643bba06), Fáze 4 (carry-over minors ve Fázi 5)
- **Kód rozhodnutí:** `TOOLBAR-FILE-BUTTON`

## Kontext

„Import JSON" ve Wireframe editoru byl `<label>` kolem `InputFile` uvnitř `TmToolbarItem` s prioritou `Pinned`. Label
nemá reprezentaci v menu „More", takže tlačítko nemohlo toolbar opustit a na úzkém toolbaru zabíralo místo. Dřívější
pokus udělat z něj `Secondary` selhal v sedmi Playwright specech (`element is not stable` na `button.tm-toolbar-more`)
a příčina se nepodařilo prokázat. Otevření výběru souboru navíc musí proběhnout synchronně v uživatelském gestu
(WebKit odmítne `input.click()` po round-tripu přes Blazor).

## Rozhodnutí

1. Nová komponenta jádra `TmToolbarFileButton` (`Tempo.Blazor.Components.Toolbar`). Viditelný ovladač je skutečné
   `TmToolbarButton` (stejné třídy, popisky, `Priority`, rank, collapse, roving tab stop); nic se nepřeimplementuje.
   `Variant`, `LabelPosition` a `PreserveFocus` se předávají dál, `Class` a splatované atributy míří na tlačítko.
2. Skrytý `<input type="file">` je **sourozenec tlačítka mimo collapse wrapper** — collapse/expand tlačítka ho nikdy
   nepřemontuje. Skrývá se vizuálně (`.tm-toolbar-file-input`: 1 px, clip, `opacity:0`, `inset-*: 0`), **nikdy
   `display:none`** (WebKit).
3. Výběr otevírá **nativní capture-phase `click` listener na dokumentu** (`TmToolbarFileButton.razor.js`), sdílený
   všemi tlačítky a odebraný s posledním. Tlačítko nese `data-tm-file-trigger="{inputId}"`, záznam v „More"
   se páruje přes `data-action-id` (jen `.tm-toolbar-more__item`) registrovaný `acquire(inputId, entryId)`.
   Registrace, jejichž input už není v DOM, se při dalším `acquire` odstraní.
4. **Žádná emulace kláves** — oba ovladače jsou nativní `<button>` (viz `docs/keyboard-activation-convention.md`).
5. Zrušený dialog nevyvolá `OnChange`; input se před každým otevřením maže, takže znovu vybraný stejný soubor událost
   vyvolá. `Accept` jen filtruje dialog, hostitel validuje obsah.
6. Wireframe: Import JSON je `Secondary` (`data-testid="wd-import"` zachováno) a je `Disabled` při `IsReadOnly`.

## Důsledky

- Aditivní změna API; stávající toolbary se nemění.
- Stabilitu toolbaru drží E2E test `Wireframe_MoreTrigger_SettlesAtEveryWidth` (Smoke).
- **Nemigrováno:** import v `TmDiagramEditor` (label + `display:none` input v dropdownu) a toolbar Spreadsheetu
  (pevné id, bez overflow) — viz `docs/toolbar.md` → „Not migrated (yet)".

## Zamítnuté alternativy

- `@onclick` + `JS.InvokeVoidAsync("click")` — round-trip přeruší user activation (WebKit).
- `<label for>` v menu „More" — menu položka není label a po zavření menu by cíl zanikl.
- Vestavět výběr souboru do `TmToolbarButton` — rozšířilo by ho o koncept, který potřebuje jediný konzument.