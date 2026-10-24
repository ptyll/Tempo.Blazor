# ADR-0005: Prezentace panelů editorového rámce na tabletu (`EDITOR-SHELL-TABLET-PRESENTATION`)

- **Status:** Přijato
- **Datum:** 2026-10-24
- **Kontext plánu:** Plán „Uzavření remaining tasků" (643bba06), Fáze 5 (carry-over minors ve Fázi 6)
- **Kód rozhodnutí:** `EDITOR-SHELL-TABLET-PRESENTATION`

## Kontext

`TmEditorShell` na tabletu (768–1199 px kontejneru) vždy vykresloval ukotvené panely: nejvýše jeden rozbalený, druhý
jako úzký rail. Wireframe editor tak na tabletu nemá dost místa pro plátno, a proto si dosud držel vlastní
„tablet sheets" (`.tm-wd-sheet`) vedle shellu: vlastní focus management, Escape, tlačítko zavření a vlastní CSS.
Diagram a e-mailový editor naopak ukotvené panely na tabletu potřebují a fungují.

## Rozhodnutí

1. Nový parametr `TmEditorShell.TabletPanelPresentation` (`Docked` = 0 výchozí, `Overlay` = 1), **jedna hodnota pro obě
   strany**, platí **jen pro tabletový layout**; desktop a mobil se nemění. Výchozí `Docked` zachovává chování všech
   dosavadních verzí, takže Diagram, Modeling a e-mailový editor nepotřebují žádnou změnu.
2. `Overlay`: levý panel je překryvná zásuvka, pravý nemodální boční panel nad plátnem v plné šířce, **nejvýše jeden
   otevřený**, řízený stávajícím dvousměrným `LeftOpen` / `RightOpen`. Vítězí naposledy otevřený panel a poražený
   jednou zavolá vlastní `…OpenChanged(false)`. Žádné raily, žádné resize separátory, `CollapsedPanelsChanged` se nevolá.
3. Panel je `TmFocusScope` s `Modal=false` (žádné `TmDrawer`, žádný top layer, nic není `inert`). Scope si sám zapamatuje
   otevírající prvek, takže Escape a × vrací fokus na toggle v toolbaru. Po otevření z toolbaru se fokus přesune na ×, ale
   nikdy ne, když už je uvnitř plátna (výběr prvku panel otevřel).
4. Povrch overlaye vykresluje shell, ne editor: levý overlay stojí v markupu **před** plátnem, pravý **za** ním (pořadí
   Tab = vizuální pořadí) na pevných pozicích, takže se plátno při přepnutí strany nikdy nepřemontuje. Titulek je
   `role="heading" aria-level="2"` na `div` (hostitelské CSS pro `h2` ho nepřestyluje).
5. Hostitelský toggle dostává `aria-expanded` a `aria-controls` z veřejných `LeftPanelId` / `RightPanelId`;
   `aria-controls` jen při otevřeném panelu (zavřený overlay v DOM není).
6. Testovací identifikátory jsou přepsatelné (`LeftOverlayTestId`, `RightOverlayTestId`, `OverlayCloseTestId`) a výslovná
   hodnota se použije **doslova** (namespace si řeší hostitel); třídy `.tm-editor-shell__overlay(--left|--right)` jsou
   stabilní kontrakt. Wireframe tím zachovává `wd-left-drawer`, `wd-inspector-sheet`, `wd-sheet-close`.
7. **Domov enumu:** `TabletPanelPresentation` leží v `Tempo.Blazor.Components.Layout` vedle `MobilePanelPresentation`
   (původně navržený `Tempo.Blazor.Abstractions.Layout` byl přesunut ještě před vydáním). Abstractions obsahuje
   zero-UI kontrakty (`TmLayoutMode`, `TmLayoutBreakpoints`), kdežto oba prezentační enumy patří výhradně komponentě
   `TmEditorShell` a nikdo jiný je nepotřebuje; dvojice enumů na jednom místě je lépe objevitelná.

## Důsledky

- Aditivní změna API s výchozí hodnotou = dnešní chování; přesun enumu je bezpečný, protože balíček ještě nevyšel.
- Wireframe editor ztrácí vlastní `.tm-wd-sheet` kód (breaking pro hostitelské CSS, zapsáno v CHANGELOG).
- Diagram zůstává `Docked`. Kdyby chtěl overlay, nastaví `TabletPanelPresentation=Overlay`, naváže `LeftOpen` /
  `RightOpen` na vlastní tlačítka a zahodí práci s `CollapsedPanels`.

## Zamítnuté alternativy

- **Dva parametry (jeden na stranu):** „levý ukotvený, pravý overlay" na tabletu nedává smysl a zdvojilo by stavový prostor.
- **Modální `TmDrawer`:** zablokuje plátno (inert) a vyžaduje top layer; wireframe musí na tabletu dál editovat s otevřeným
  inspektorem.
- **Ponechat sheety v editoru:** druhá implementace focus managementu, Escape a zavření, kterou by musel kopírovat každý
  další editor.