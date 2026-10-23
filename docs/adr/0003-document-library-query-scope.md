# ADR-0003: Explicitní rozsah dotazu do knihovny dokumentů (`DOCLIB-QUERY-SCOPE`)

- **Status:** Přijato
- **Datum:** 2026-10-23
- **Kontext plánu:** Plán „Uzavření remaining tasků" (643bba06), Fáze 3 (carry-over minors ve Fázi 4)
- **Kód rozhodnutí:** `DOCLIB-QUERY-SCOPE`

## Kontext

`DocumentLibraryQuery.FolderPath = null` byl dvojznačný: kontrakt říká „kořen", ale uzel „Recent" a vyhledávání
v `TmDocumentOpenDialog` posílaly `null` ve významu „celá knihovna". Poskytovatel, který `null` četl jako kořen,
proto vracel pro Recent jen kořenové dokumenty.

## Rozhodnutí

1. Do `Tempo.Blazor.Abstractions` přibyl `enum DocumentLibraryScope { Folder = 0, Subtree = 1, Library = 2 }`
   a `DocumentLibraryQuery.Scope` (výchozí `Folder`).
   - `Folder` — přímý obsah `FolderPath` (dnešní chování, `null`/`"/"` je kořen),
   - `Subtree` — složka a všechny podsložky,
   - `Library` — celá knihovna daného `Kind`, `FolderPath` se ignoruje.
2. `ITempoDocumentLibraryProvider` a `DocumentOpenResult` se **nemění**. Poskytovatel, který širší rozsahy neumí,
   je smí degradovat na `Folder` — volající je proto chápe jako požadavek, ne garanci.
3. `TmDocumentOpenDialog` pro Recent a vyhledávání posílá `Scope = Library` (s `FolderPath = null`).
4. **Legacy pravidlo (zachováno záměrně):** dotaz s `Scope = Folder`, který nese `Search`, smí prohledat celou
   knihovnu — tak se chovaly referenční úložiště (`DocumentLibraryStore`, testovací `InMemoryDocumentLibraryProvider`)
   před zavedením rozsahu. Volající, kteří chtějí vyhledávání napříč knihovnou, mají uvádět `Library`.
   Obě úložiště normalizují `FolderPath` stejně (prázdné = `"/"`, koncové lomítko se ořezává).
5. REST `GET /api/document-library/{kind}/browse?scope=` přijímá jména (bez ohledu na velikost písmen) i čísla
   definovaných hodnot; **nedefinovaná hodnota (`99`, `-1`, překlep) vrací `400 Bad Request`**, aby se tiše
   nechovala jako `Folder`.

## Důsledky

- Aditivní změna API; vlastní poskytovatelé, kteří `Scope` ignorují, fungují jako dřív.
- Poskytovatel, který četl `FolderPath = null` jako „kdekoliv", dál funguje; poskytovatel, který ho četl jako
  „kořen", ukáže Recent jen z kořene, dokud `Scope` nezačne respektovat (viz CHANGELOG → Breaking / Migration).

## Zamítnuté alternativy

- Nový parametr `bool Recursive` — nevyjádří „celá knihovna bez ohledu na `FolderPath`".
- Zpřísnit `Folder + Search` na „jen složka" — rozbilo by stávající klienty, kteří vyhledávali přes `FolderPath = null`.
