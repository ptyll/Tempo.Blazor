# ADR-0006: Veřejné API „pin on top“ pro stavové UI hostitele (`TOP-LAYER-PIN`)

- **Status:** Přijato
- **Datum:** 2026-11-15
- **Kontext plánu:** „Tempo: veřejné API pin on top pro stavové UI hostitele“ (4ce1b994), Fáze 1
- **Kód rozhodnutí:** `TOP-LAYER-PIN`
- **Návrhový dokument:** [docs/top-layer-pin.md](../top-layer-pin.md) (inventura s odkazy na řádky, pravidla, kontrakt)

## Kontext

Tempo drží své toast containery nad každým modálním povrchem pomocí interního registru připíchnutých kořenů
(`pinRoot` / `unpinRoot` / `reRaisePinnedRoots` v `tm-top-layer.js`, `internal TopLayerInterop`). Hostitel ten
registr použít nemůže. EmusCz si proto pro `#blazor-error-ui` napsal vlastní `emus-error-ui.js`. Lišta, kterou
ten skript zobrazí, ale leží v pořadí otevření, takže ji Tempo modal otevřený později přikryje. Navíc nikdo dnes
nezná nativní `<dialog showModal>`: toast pushnutý během takového dialogu se vykreslí nad ním, přitom je inert.

## Rozhodnutí

1. **Jeden registr, jedno pravidlo pořadí, dvoje vstupní dveře (varianta c).** Registr zůstává v `tm-top-layer.js`.
   - Pro cizí DOM slouží deklarativní atribut `data-tm-pin-on-top` (+ `data-tm-pin-priority`), který skenuje
     veřejný modul `tm-pin-on-top.js` s MutationObserverem.
   - Pro komponenty slouží scoped služba `TopLayerPinService.PinAsync(ElementReference, TopLayerPinOptions?)`,
     která vrací `TopLayerPin : IAsyncDisposable`. K ní patří tenký wrapper `<TmPinnedOnTop Visible Priority>`.
   - Toast containery přejdou na stejný registr přes beze změny zachovaný interní `TopLayerInterop`.
   - Variantu služba + komponenta potvrdil vlastník 2026-11-15.
2. **Signálem je viditelnost.** Prvek je viditelný, když je připojený, nemá `hidden` a jeho computed `display`
   není `none`.
   - Atribut `popover="manual"` vlastní modul a drží ho jen po dobu povýšení. Jinak by UA pravidlo
     `[popover]:not(:popover-open){display:none}` signál zakrylo.
   - Prvek, který `popover` nese už v markupu (toasty), je v explicitním režimu a jeho signálem je `:popover-open`.
   - Observer nesleduje atribut `popover`. Každý průchod po vlastních zápisech zavolá `takeRecords()` a průchody
     se slučují do jednoho na snímek, takže smyčka observeru nevznikne.
3. **Pořadí určuje explicitní priorita** (int, výchozí `0`), při shodě rozhoduje pořadí registrace. Rozhodl
   vlastník 2026-11-15.
   - `raise()` (push) umístí prvek navrch jeho prioritního pásma, ale nikdy nad vyšší pásmo.
   - Toasty mají `0`. Pro error lištu frameworku dokumentace doporučí `100`.
4. **Nativní modální `<dialog>`: jedno pravidlo pro všechny piny, toasty nevyjímaje.** Rozhodl vlastník 2026-11-15.
   - Dokud je otevřený nějaký `dialog:modal`, nic se nepovyšuje ani nepřezvedává.
   - Po `close` (capture listener na `document`, záloha přes observer na atributu `open`) se všechny piny
     synchronizují v pořadí registru.
   - Je to změna chování toastů, a proto ji CHANGELOG 2.12.0 zapíše do „Breaking / Migration“.
5. **Inert.** Modul přidá připíchnutému prvku `data-tm-inert-exempt` a odebere ho jen tehdy, když ho přidal sám.
   Pravidlo umístění (sourozenec na úrovni layoutu, ne uvnitř `@Body`) platí dál.
6. **Reset UA stylů popoveru** zajistí utilita `:where([data-tm-pin-on-top], .tm-pin-on-top)` s nulovou
   specificitou: `inset:auto`, `width/height:auto`, `margin:0`, `border:none`, `padding:0`,
   `background:transparent`, `color:inherit`, `overflow:visible`, `max-*:none`. Spodní lišta tak zůstane lištou.
   Utilita nepřidává žádnou barvu ani token.
7. **Bez Popover API** je registrace no-op a prvek se vykreslí ve svém z-index pásmu. **Při prerenderu** nic
   neběží, protože C# pracuje jen v `OnAfterRenderAsync` přes `LazyJsModule`. Modul pro atribut je opt-in
   (`<script type="module" src="_content/Tempo.Blazor/js/tm-pin-on-top.js">`). JS initializer `*.lib.module.js`
   se nepřidává.
8. **Umístění a verze:** vše patří do `Tempo.Blazor`. Služba a typy jsou v `Tempo.Blazor.Services`, komponenta
   v `Tempo.Blazor.Components.Feedback`. Abstractions zůstávají beze změny, protože nejde o UI-free kontrakt.
   Nové veřejné API je minor, takže cílová verze je **2.12.0** (lock-step).

## Důsledky

- Hostitel místo vlastního skriptu napíše `<div id="blazor-error-ui" data-tm-pin-on-top data-tm-pin-priority="100">`
  a přidá jeden `<script type="module">`. Lišta pak zůstane nad modalem otevřeným před jejím zobrazením i po něm.
- Toast pushnutý během nativního modálního dialogu se ukáže až po jeho zavření. Dnes se vykreslí nad dialogem jako
  inert.
- Fáze 2 musí převést `TmNotificationToastContainer` na stejnou single-flight disciplínu jako `TmToastContainer`
  a doplnit wireframe roli a stencil pro `TmPinnedOnTop`.

## Zamítnuté alternativy

- **(a) Jen atribut + observer:** komponenta by musela přidat atribut a doufat v observer. Neexistuje deterministický
  okamžik pinu ani dispose, takže by nebyla testovatelná v bUnitu.
- **(b) Jen C# služba / komponenta:** nepokryje DOM, který žádná komponenta nevlastní (`#blazor-error-ui`,
  banner z `blazor.web.js`), a to je právě případ EmusCz.
- **Jen pořadí registrace bez priority:** error lišta zaregistrovaná dřív než toast container by skončila pod
  toasty a hostitel by s tím nemohl nic udělat.
- **Výjimka toastů z pravidla o nativním dialogu:** dvě pravidla v jednom registru a dál viditelné, ale
  neovladatelné karty nad dialogem.
- **Automatický start přes Blazor JS initializer:** každá aplikace by platila za observer nad celým dokumentem,
  i když žádný pin nepoužívá.
- **Typy v Abstractions:** nic z toho nepotřebuje backend, `ElementReference` je UI typ.
