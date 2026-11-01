# Signing redesign — Fáze 10: finální UX review

Plan `1fba4858-f46e-431e-8c3d-08df2362aa32`, fáze 10 (S1-7). Porovnání s referencemi v
`output/imagegen/tempo-reimagined/Tempo.Blazor.Signing/` a s fázovými capture.

## Metoda

- Fázové capture (1–9) vznikly dříve s `TM_WRITE_BASELINES=1`. Tato fáze je **negeneruje znovu**.
- Fáze 10 píše jen `tests/Tempo.Blazor.E2E/__screenshots__/signing-editor-bridge/redesign/`
  a případný recheck do `tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-10-recheck/`.
- **Celá E2E sada neběžela** (waiver uživatele). Běžela jen Signing sada.

## Most `/signing-from-editor`

| Reference | Implementace | Verdikt |
|---|---|---|
| Tři kroky v jedné ploše | Záložky Edit \| Place fields \| Preview \| Inline preview. Edit je mimo `TmTabs` (`hidden`, `role=tabpanel`), editor zůstává mounted. [bridge-edit](../tests/Tempo.Blazor.E2E/__screenshots__/signing-editor-bridge/redesign/bridge-edit--1440--light--default.png) | splněno |
| Designer na celou šířku | `signing-bridge__surface--designer`, žádný `showcase-card` | splněno |
| Akce jako tlačítka knihovny | `TmButton` Primary / Outline / OutlinePrimary | splněno |
| Přepínač layoutu jen v Place fields | `TmSelect` v toolbaru Place fields, `data-testid=signing-from-editor-layout` | splněno (nad rámec reference) |
| App shell reference | demo shell hostitele | vědomá odchylka G12 |

`data-testid`: `signing-from-editor-export`, `-insert-body`, `-insert-footer`, `-inline-preview`,
`-designer`, `-runner`, `-inline-runner`, `-status`, `-tabs`, `-layout`, `-layout-select`,
`-edit-stage`, `-tab-edit`, `-tab-place`, `-tab-preview`, `-tab-inline`.

Enablement záložek je z dat: Place po exportu stránek, Preview po polích, Inline po inline stránkách.
Aktivní záložka je zvlášť. Round-trip Edit → Place → Edit → Place pole neshodí.

## Uzavření 22 obrazovek

| Obrazovka | Capture | Stav | Vědomá odchylka |
|---|---|---|---|
| pdf-template-designer | [phase-8](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-8/designer--1440--light--default.png) | splněno | mobil = G2; prahy 768/1200 |
| signing-field-editor-panel | [phase-8 inspector](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-8/inspector-selected--1440--light--default.png) | splněno | geometrie je v inspektoru vždy; standalone panel má `ShowGeometry` default false |
| recipient-role-editor | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/recipient-role-editor--1440--light--default.png) | splněno | — |
| condition-builder | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/condition-builder--1440--light--default.png) | splněno | posun tlačítky, ne tahem |
| formula-builder | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/formula-builder--1440--light--default.png) | splněno | jen závorky |
| share-link-panel | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/share-link-panel--1440--light--default.png) | splněno | — |
| submission-status-timeline | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/submission-status-timeline--1440--light--default.png) | splněno | relativní čas je šum capture |
| audit-trail-viewer | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/audit-trail-viewer--1440--light--default.png) | splněno | — |
| document-comment-thread-panel | [phase-9](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-9/comments--390--light--default.png) | splněno | mobil = `TmDrawer` |
| document-page-viewer | [phase-9](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-9/viewer--390--light--default.png) | splněno | runner pinch vždy zapíná; standalone viewer je opt-in |
| pdf-signature-verification | [phase-7](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-7/pdf-signature-verification--1440--light--default.png) | splněno | — |
| signing-template-from-editor-page | [bridge-edit](../tests/Tempo.Blazor.E2E/__screenshots__/signing-editor-bridge/redesign/bridge-edit--1440--light--default.png) | splněno | G12 |
| signing-form-runner | [phase-5](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-5/signing-runner-docked--1440--light--default.png) | splněno | sheet pod 640 px |
| signing-signature-step | [phase-4](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/signing-signature-step--1440--light--default.png) | splněno | Draw na hrubém ukazateli |
| signing-text-step | [phase-4](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/signing-text-step--1440--light--default.png) | splněno | `TmTextInput` / `TmTextArea` |
| signing-number-step | [phase-4](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/signing-number-step--1440--light--default.png) | splněno | culture parse; čárka se při echo hostu nemaže |
| signing-date-step | [phase-4](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/signing-date-step--1440--light--default.png) | splněno | nativní date |
| signing-phone-step | [phase-6](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-6/signing-active-phone-countdown--1440--light--default.png) | splněno | OTP dokončí host |
| signing-choice-step | [phase-4](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-4/signing-choice-step--1440--light--default.png) | splněno | ≤5 možností = radio karty |
| signing-attachment-step | [phase-6](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-6/signing-active-attachment-uploaded--1440--light--default.png) | splněno | stream čte host |
| signing-external-step | [phase-6](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-6/signing-active-external-verified--1440--light--default.png) | splněno | jen hostem zapsané `true` |
| signing-completion-panel | [phase-3](../tests/Tempo.Blazor.E2E/__screenshots__/signing-redesign/phase-3/signing-completion-panel--1440--light--default.png) | splněno | hero kruh |

## Vědomé odchylky

- App shell referencí se neimplementuje (G12).
- „Režim přístupnosti“ je pryč od fáze 3: jeden panel na každé šířce.
- Designer na telefonu nekreslí tahem (G2).
- Datum není `TmDatePicker` (SI1).
- **Čipy v dark mode:** designer barví čip barvou role (dark token). Runner drží dokument jako light island, takže čip nad stránkou zůstane ve světlé paletě i v dark hostu. Odchylka je záměrná — papír se v dark mode nepřebarví.

## Nálezy a oprava

| Nález | Oprava |
|---|---|
| Comments layout a culture byly nativní `<select>` | `TmSelect`; E2E cílí na vnitřní `<select>` |
| „Simulate provider failure“ | `TmCheckbox`; `data-testid` je na nativním inputu přes `InputAttributes` (ne na wrapperu). Test volá `GetByLabel(...).CheckAsync()` |
| Most přepisoval editor při změně záložky | Edit stage je mimo `TmTabs` a zůstává mounted |
| Čárka v number stepu | `OnParametersSet` přeformátuje jen když se příchozí hodnota liší od parsovaného textu |
| Mobilní footer překrýval obsah; „Previ…“ na 320; composer překrýval komentář | padding/scroll-padding patičky, Previous je pod 22.5rem jen ikona s `aria-label`, komentáře mají `scroll-padding-block-end` = výška composeru |

## Známé otevřené vady

- Celá E2E sada neběžela (waiver). Signing filtr se na `PdfTemplateDesigner_ContextMenuActionsAreClickable` zasekl; změněné Signing testy (phase 10, localization, phase 6, comma, bridge) prošly. Capture matice je ve `signing-editor-bridge/redesign/`.
- Recheck `runner-full--320--light--default.png`: Previous je ikona, patička pole nepřekrývá. Popisek jazyka je na 320 oříznutý.
- Recheck `comments--390--light--default.png`: composer pořád překrývá „Back“ / hlavičku vlákna. `scroll-padding` to nesrovnalo.
