# Modeling redesign — Fáze 1: UX review baseline screenshotů (bez oprav)

> Plán `Tempo.Blazor — Redesign: Modeling` (7480f23e), Fáze 1 `fze-1-baseline-csp-a-a11y-opravy-nezvisl-na-redesignu`.
> Úkol: „UX review baseline screenshotů proti `modeling-editor--desktop.png`, `modeling-issue-panel--desktop.png`,
> `modeling-source-panel--desktop.png` — sepsat tabulku mezer pro Fáze 2–7 (bez oprav).“

Baseline screenshoty (stav „před“) jsou v `tests/Tempo.Blazor.E2E/__screenshots__/modeling/`
(`default-*`, `issues-mixed-*`, `stale-source-*`, `open-editor-dialog-*`, `empty-*`, `error-*`
pro 1440×900, 1024×768 dotyk, 390×844, 320×640). Reference jsou v
`output/imagegen/tempo-reimagined/Tempo.Blazor.Modeling/`.

Tato fáze záměrně NEMĚNÍ vzhled ani rozvržení (G3: jen CSP, a11y, scoped CSS, dokumentace).
Následující tabulka je pracovní podklad pro Fáze 2–7 — **žádná z položek se v Fázi 1 neopravuje**.

## Tabulek mezer (baseline vs. reference)

| # | Obrazovka | Rozdíl baseline vs. reference | Kam patří (fáze) |
|---|-----------|-------------------------------|------------------|
| 1 | editor (desktop) | Hlavička: stav je prostý text v titulkové skupině; reference má stav jako badge vedle titulu, odlišený barvou podle stavu | Fáze 2 (vizuální vrstva) |
| 2 | editor (desktop) | Panely (strom/náhled/inspektor) nemají kartový vzhled (radius, stín, `--tm-bg-workspace`) ani sjednocené hlavičky panelů | Fáze 2 |
| 3 | editor (desktop) | Stavový pruh bez ikony stavu a bez vizuálních oddělovačů; počty jsou holé `<span>`y | Fáze 2 |
| 4 | editor (desktop) | Chipy Notace/Pohled/Poskytovatel v hlavičce duplicují selecty; reference má jen select Poskytovatel + Notace/Pohled jako pole, bez chipů | Fáze 3 (MO3) |
| 5 | editor (desktop) | Nálezy jsou vpravo pod Zdrojem a Inspektorem (stohovaný pravý sloupec); reference je má jako spodní sbalitelný panel pod náhledem | Fáze 5 (MO1) |
| 6 | editor (desktop) | Zdroj je samostatný panel nahoře vpravo; reference ho má jako záložku inspektoru (Vlastnosti \| Zdroj) | Fáze 5 (MO1) |
| 7 | editor (desktop) | Stavové značky prázdného/chybového stavu jsou textové glyfy „0“/„!“ místo ikon | Fáze 2 |
| 8 | editor (tablet/mobil) | Není žádný tablet ani mobilní režim: 3sloupcové rozvržení se jen zúží, chybí karty panelů, akční lišta, kompaktní hlavička (G2) | Fáze 6 + 7 |
| 9 | open-in-editor (desktop) | Overlay je absolutní uvnitř boxu editoru: bez scrimu, bez focus trapu, titul = „Náhled“ místo názvu dokumentu; reference je viewport modal ~90vw × 90vh | Fáze 4 |
| 10 | issue-panel (desktop) | Závažnosti jsou barevné okraje + textový glyf „!“/„i“; reference má ikonu v tónovaném kruhu, barevný label závažnosti a kartu s `--tm-radius-card` | Fáze 2 |
| 11 | issue-panel (desktop) | Doporučená oprava je holý text; reference má tónovaný box s ikonou a prefixem „Doporučení:“ | Fáze 2 |
| 12 | issue-panel (desktop) | Hlavička panelu: počet je text `{0} nálezů` — reference formulace „Nálezy: {0}“ (v Fázi 1 už neutralizováno v lokalizaci, DOM hlavičky se nemění) | Fáze 2 (domácí úprava hlavičky) |
| 13 | issue-panel (desktop) | Navigovatelné nálezy nemají chevron; reference naznačuje navigaci šipkou | Fáze 2 |
| 14 | source-panel (desktop) | Badge čerstvosti je text; reference má ikonový badge (`AlertTriangle`/`CheckCircle`) a duplicitní podtitul nemá | Fáze 2 |
| 15 | source-panel (desktop) | „Načíst model“ je vlastní tlačítko bez ikony Refresh a bez loading stavu | Fáze 2 |
| 16 | source-panel (desktop) | Metadata nejsou 2sloupcový `dl` s oddělovači a ikonami řádků; lokální přebití `--tm-color-primary-700` v CSS | Fáze 2 |
| 17 | strom (všechny) | Skupiny nejsou sbalitelné disclosure s počtem (`aria-expanded`); úchyt je textový glyf „::“ místo ikony; chybí ikony podle sémantického typu | Fáze 2 (MO4) |
| 18 | náhled (všechny) | Drop-zóna gradientu 8px/16px a 28rem jsou hardcodované hodnoty místo tokenů | Fáze 2 |

## Poznámky

- Body 1–8, 10–18 se řeší ve Fázích 2–3 (vizuální vrstva, hlavička, MO3/MO4), 5–6 ve Fázi 5 (MO1),
  9 ve Fázi 4 (dialog) a 8 ve Fázích 6–7 (tablet/mobil). Položky [E] z analýzy (app shell, editovatelný
  inspektor, „Externí odkazy“) se ignorují — nejsou součástí komponenty.
- Screenshoty `issues-mixed-*` obsahují nový zdrojově prázdný nález (Fáze 1 a11y oprava), takže
  baseline zachycuje i neinteraktivní variantu řádku nálezu.
