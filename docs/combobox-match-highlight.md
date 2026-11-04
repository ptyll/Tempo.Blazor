# Combobox match highlighting and inline create

Two **opt-in** behaviours of the combobox family (`TmMultiSelect`, `TmFilterableDropdown`,
`TmEntityPicker`, `TmUserPicker`), added in 2.10.0 (plan ff043b2e phase 8). Both default to `false`, so a
combobox that does not ask for them renders exactly the markup it rendered before.

## `HighlightMatches`

```razor
<TmMultiSelect TItem="SelectOption<string>" TValue="string"
               Items="_cities"
               DisplayField="@(o => o.Label)"
               ValueField="@(o => o.Value)"
               HighlightMatches="true"
               @bind-Values="_selected" />
```

With `HighlightMatches="true"` the part of each option's display text that matches the filter is wrapped in
`<mark class="tm-highlighted-text__match">` through `TmHighlightedText`. What that buys a reader is the
answer to "why is this row here": scanning a list of long organisation names for the term you typed is work
the component can do for you.

Rules the highlight follows:

- **The query is literal text, never a regular expression.** A search term is user data: handing it to a
  `Regex` would make `(` throw, `.*` match everything and `$1` mean a backreference. Matching uses
  `string.IndexOf(string, StringComparison)` and cannot be surprised by a metacharacter.
- **Casing is ignored** by default (`CaseSensitive` on `TmHighlightedText` turns that off).
- **Diacritics are ignored on both sides** — "usti" finds "Ústí" and "Ústí" finds "Usti" — and the marked
  run is always the **original** text, never the folded query. `TmFilterableDropdown` follows its own
  `AccentInsensitiveFilter` switch instead of assuming.
- **Every occurrence** is marked; the runs never overlap, and the concatenation of all runs is exactly the
  input text, so copy/paste and the accessible name both get the original string.
- **An empty query marks nothing**, which is what makes the component safe to render while the user is
  still typing.
- **The text is never re-interpreted as markup** — `<b>x</b>` is displayed, not executed.

`TmHighlightedText` renders a **fragment** (no wrapper element) because the call sites are option rows,
table cells and picker lines that already own their box and their typography; an extra wrapper would break
their flex layout or force every caller to undo the component's styling.

The highlight is a presentation detail of a search, not a live region: the combobox already announces the
search state, and a per-row announcement would be noise.

## `AllowCreate` + `OnCreate` (`TmMultiSelect` only)

```razor
<TmMultiSelect TItem="SelectOption<string>" TValue="string"
               Items="_cities"
               DisplayField="@(o => o.Label)"
               ValueField="@(o => o.Value)"
               AllowCreate="true"
               OnCreate="CreateCityAsync"
               @bind-Values="_selected" />

@code {
    private Task CreateCityAsync(string text)
    {
        _cities.Add(SelectOption<string>.From(text.ToLowerInvariant(), text));
        return Task.CompletedTask;
    }
}
```

With `AllowCreate="true"` the popup appends a **create row** — the localized
`TmMultiSelect_CreateOption` template ("Create \"{0}\"", with each language's own quotes) — as the **last
option of the same listbox** whenever the filter text matches
no option exactly (compared ignoring case and surrounding whitespace). It is a `role="option"` inside the
list, not a button parked next to it: a screen reader walking the list must meet the same choices a pointer
does.

- It is **not offered** for a filter that matches nothing only because of accents (an exact match is an
  exact match), for blank text, or while the control is disabled.
- `ArrowDown` walks past the results onto it, and it becomes the `aria-activedescendant` — so what `Enter`
  will do is spoken before the key is pressed. **The row paints the same focused style as a regular
  option**, so a sighted keyboard user sees the highlight move onto it instead of watching it vanish; the
  accent tone is `--tm-color-primary-text`, which stays at 4.5:1 on the `--tm-bg-muted` panel the hover and
  focus states paint (the raw primary step is only 4.07:1 there in the dark theme). `Enter` picks it.
- Picking it raises `OnCreate` with the **trimmed** text and closes the popup.
- The component **never fabricates an item.** It does not know how to build a `TItem` from a string, and a
  made-up value would be a lie in the host's data — so the host adds the option (to `Items` or through its
  provider) and decides whether to select the newly created value.

### What the host must do in `OnCreate`

The row closes the popup, clears the filter and returns focus to the trigger, so **the result the user
just asked for is not on screen**. Two host responsibilities follow:

- **Announce it.** `OnCreate` is the only signal — the component adds no live region, because it cannot
  know whether an item was actually created. Put the outcome in the host's own polite live region (the
  `/forms` demo puts one on its "created" status line) and select the new value so the chip reflects it.
  The live region is the guarantee: the component announces no selection itself, so selecting alone changes
  what is on screen without speaking it. A host that neither selects nor announces leaves a screen-reader
  user hearing only the trigger again.
- **De-duplicate when using a `DataProvider`.** The "no exact match" check can only compare the items the
  provider has loaded so far, and a provider may filter on the server: an item that already exists but was
  not returned still gets a create row. Resolve the typed text to the existing item and select that instead
  of creating a duplicate.

```csharp
private Task OnCreateCityAsync(string text)
{
    var existing = _cities.FirstOrDefault(
        city => string.Equals(city.Label, text, StringComparison.OrdinalIgnoreCase));

    var city = existing ?? AddCity(text);

    // Selecting the new value makes the chip reflect the result; the host's polite live region (see
    // above) is what actually speaks it — the component announces no selection itself.
    _selectedCityValues = [.. _selectedCityValues, city.Value];
    return Task.CompletedTask;
}
```

## Accessibility checklist

- The option's accessible name stays the **whole** display text; the `<mark>` only changes how part of it is
  painted.
- The create row is the last `role="option"` of the listbox, is reachable by the keyboard, and carries the
  same visible focus treatment as every other option (WCAG 2.4.7), with an AA-contrast label in both themes.
- Colour is not the only carrier: the create row is distinguished by a separator, by its localized word and
  by its position, and a search match is the term the reader typed, still readable in the text itself.
- The highlight uses `--tm-color-primary-subtle` in light and dark, and re-declares the system
  `Highlight`/`HighlightText` pair under `forced-colors`, where the UA drops the background.
- Creating is host-reported: the host must announce the result in its own polite live region and reflect it
  in the selection (see above) — the component returning focus to the trigger is not confirmation, and it
  renders no selection live region of its own.

## Demo

`/forms` — the "Match highlighting and inline create" section (a multi-select that marks the term and
offers the create row, and a filterable dropdown beside it). `/data-display` — the segmented control that
shares this phase, which deliberately does **not** highlight: a segment label is a value, not a search
result.
