# TmModelPicker

The model a conversation runs on, chosen once and visible afterwards. `TmModelPicker` is the trigger that
names the current model plus a popup that compares the host's whole catalogue — speed, quality, price per
million input tokens, context window and capabilities — without leaving the page.

The component lives in the **Tempo.Blazor.AI** package, the models live in **Tempo.Blazor.Abstractions**
(`Tempo.Blazor.AI.Models`), and nothing here references an LLM SDK: the catalogue is the host's own snapshot.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmModelPicker Models="@models"
               @bind-Selection="selection"
               EstimatedPromptTokens="100000" />

@code {
    private AiModelSelection? selection = new("gpt-4o");

    private IReadOnlyList<AiModelInfo> models =
    [
        new("gpt-4o", "GPT-4o")
        {
            Provider = "OpenAI",
            Family = "GPT-4",
            ContextWindow = 128_000,
            SpeedScore = 4,
            QualityScore = 5,
            InputPricePerMillion = 3m,
            Capabilities = ["vision", "tools"],
            EffortLevels = [new AiModelEffortLevel("low"), new AiModelEffortLevel("high")],
            SupportsExtendedThinking = true,
        },
        new("gpt-4o-mini", "GPT-4o mini")
        {
            Provider = "OpenAI",
            Family = "GPT-4",
            ContextWindow = 128_000,
            SpeedScore = 5,
            QualityScore = 3,
            InputPricePerMillion = 0.15m,
        },
    ];
}
```

## The answer is one value

`@bind-Selection` is an `AiModelSelection` record:

| Member | Meaning |
| --- | --- |
| `ModelId` | The chosen `AiModelInfo.Id` (the id the host sends to its provider) |
| `ExtendedThinking` | Whether the reader turned extended thinking on (default `false`) |
| `EffortLevel` | The chosen `AiModelEffortLevel.Id`, or `null` for the provider's own default |

A **different** model starts from that model's defaults (thinking off, effort unset). Re-picking the
**same** model keeps the reasoning options the reader already chose, so opening the list again is not a
reset. `Selection = null` renders the placeholder trigger.

The component is usable uncontrolled too: a host that ignores `SelectionChanged` still sees its pick, and a
stale `Selection` parameter never overwrites it (the parameter wins only when its value actually changes).
Reporting through `SelectionChanged` is still the recommended wiring.

## The list

- **Grouping** — `GroupBy` is `Provider` (default), `Family` or `None`. A model that does not report the
  property lands in the localized "Other" group; grouping never hides a model. Group order follows the first
  appearance of each key in the host's list, so the catalogue's own ordering survives.
- **Search** — `ShowSearch` (default `true`) renders the popup's combobox. It matches the name, provider,
  family, description and capabilities, literally and accent-insensitively, and the matched run is marked by
  `TmHighlightedText`. A group whose models all filtered out disappears. The live region reports
  `Matches: {n}` on every search change; an empty result renders the localized sentence instead of an empty
  list.
- **Comparison bars** — each row draws up to three bars with the value in **words** next to them: speed and
  quality are the model's own 0–5 score, drawn on that absolute 0–5 scale, and the price bar is the input
  price **relative to the most expensive model in the catalogue**, so the picture answers "how expensive is
  this next to my alternatives". The bars are `aria-hidden` decoration; the text is the information.
  `ShowSpeed`, `ShowQuality`, `ShowPrice`, `ShowContextWindow` and `ShowCapabilities` turn the individual
  pieces off.
- **Not reported is not zero** — a model without a score, price or context window renders no bar and no chip
  for it. A reported context window renders short ("200k", "1M") and is captioned ("200k context").
- **Deprecated** — a model with `IsDeprecated` is still selectable (a host may have it pinned in an existing
  conversation) but its row and the trigger carry the localized "Deprecated" chip.

## Reasoning options

The selected model's options sit in a block **above** the list:

- `SupportsExtendedThinking` — a switch (`TmToggle`), announced as "Extended thinking on/off".
- `EffortLevels` — a `TmSegmentedControl` in its radio-group pattern, with a localized "Default" option
  (stored as `null`) followed by the model's levels in the order the host supplied them. The picked level is
  announced with its own label.

The block is rendered only for a model that reports at least one of them (`ShowExtendedThinking` /
`ShowEffort` opt out individually), and it never sits inside a `role="option"` — an option is a leaf of the
listbox and may not contain a switch or a radio. The two controls are the only controls in the popup besides
the search field.

## Price estimate

`EstimatedPromptTokens` (a positive token count) plus a selected model with `InputPricePerMillion` renders the
prompt estimate in the popup footer: `Estimated prompt cost ≈ {amount}`. It is a comparison aid, not a bill —
output tokens are not included. No tokens, no price → no estimate (never "≈ 0").

`Culture` decides the number and currency formatting (the culture's own symbol, symbol position and decimal
digits); `PriceFormatter` replaces it entirely for a host with credits or a fixed exchange rate — it receives
a money amount (a price per million tokens or the estimate) and returns the host's own text.

## Keyboard

| Where | Keys |
| --- | --- |
| Trigger (native button) | Enter / Space open the popup natively; ↓ / ↑ open it too. Nothing is emulated on a native button |
| Search field (popup combobox) | ↑ / ↓ move the active option (`aria-activedescendant`), Enter picks it; typing filters |
| Listbox (`ShowSearch="false"`) | ↑ / ↓ move, Home / End jump, Enter (keydown) and Space (keyup) pick, printable characters run APG type-ahead (a repeated letter cycles) |
| Popup | Escape closes through the overlay and returns the focus to the trigger; an outside pointer closes it without stealing the focus the click just earned; a pick closes it and returns the focus itself |

Focus moves into the popup only once it is actually displayed (`OnOpened`), so the move never lands on a
hidden element.

## Accessibility

- One polite live region per picker announces the pick, both reasoning changes and the search match count.
  Nothing else is live (the list, the rows and the bars are not).
- The trigger is named by its own visible text — a visually hidden localized "Model:" prefix, the model name,
  its provider and every state tag — so a deprecated model or an active reasoning option is announced rather
  than merely painted. Only a host-supplied `AriaLabel` replaces that name, and in that case the state tags
  move into the trigger's description. It carries `aria-haspopup="listbox"`, `aria-expanded`,
  `aria-controls` while open and a localized instruction as its description.
- The listbox is `aria-label`ed by the localized "Models" label (the popup title names the card / sheet, so
  the same words are not spoken twice), single-select (`aria-multiselectable` is absent), and the active row
  is marked with both a border and a tone (`option--active`), the picked row with `aria-selected` and its own
  border.
- The reasoning switch and the effort tray keep their own APG semantics; both are outside the listbox.
- A coarse pointer raises the option rows, the trigger and the search field to the 44 px touch target.
- `forced-colors` hands the three focus stops back to the system `Highlight`, marks the picked row with a
  2 px `SelectedItem` border and the active row with a `Highlight` outline, and keeps the comparison bars
  readable (a `CanvasText` track outline plus an explicitly repainted fill).
- The surface declares no motion at all: the reader may keep it open while a run streams behind it.

## Presentation

The popup is the core `TmOverlayPanel`: an anchored popover on a wide layout and, on a phone, a bottom sheet
in the top layer (`MobilePresentation`, default `Auto`; `LayoutMode` pins a mode for a test or a host). The
list is capped in height and scrolls inside the panel; the page never scrolls sideways.

## Wireframe

`TmModelPicker` is a wireframe citizen with the role `model-picker`, a built-in schema
(`title`, `models`, `groupBy`, `showSearch`, `showMetrics`, `showTags`) and a declarative stencil in the
`tempo` pack, so the palette, the properties panel and the MCP catalogue stay in step with the component.

## Demo

`/model-picker` in the demo shows the picker at full strength, a family-grouped variant, a compact toolbar
variant (no search field, no capability chips, no quality bar), the live `SelectionChanged` stream
(`CloseOnSelect="false"`) and the edge cases: an empty catalogue, a disabled picker, a deprecated selection, a
selection whose model is no longer in the catalogue, and a host-owned credit price vocabulary.
