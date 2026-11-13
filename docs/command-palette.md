# TmCommandPalette

Searchable command palette overlay (Ctrl/Cmd+K style) over a local action list, extended since
2.11.0 with item icons, a trailing badge/code slot per item, an asynchronous search provider with
debounce and cancellation, loading / empty / transient-error states and an opt-in keyboard-hint
footer. Everything except `ShowIcons` is opt-in: a host that sets no new parameter gets the
pre-2.11 behavior (plus the icon slot — see *Breaking / Migration* in the CHANGELOG).

## Basic usage

```razor
<TmCommandPalette @bind-IsOpen="_open" Actions="_actions" />
```

```csharp
@code {
    private bool _open;
    private readonly ICommandPaletteAction[] _actions =
    [
        new CommandPaletteAction("new", "New file", "Create a new file", Icon: "file", Shortcut: "Ctrl+N",
            Category: "File", Badge: "Cmd", Code: "CMD-NEW",
            Execute: () => { /* … */ return Task.CompletedTask; }),
    ];
}
```

`CommandPaletteAction` (sealed record in `Tempo.Blazor.Abstractions`) is the ready-to-use
implementation of `ICommandPaletteAction` — hosts and demos no longer write a private copy.
`ICommandPaletteAction` also carries opt-in **default members** `Badge`, `Code` and `BadgeTone`:
pre-2.11 implementations compile and behave unchanged (null / neutral defaults). `BadgeTone`
(`Neutral`, `Primary`, `Success`, `Warning`, `Danger`, `Info`) maps to the `TmBadge` variants;
`Code` renders monospace in the trailing slot and is matched by the local filter.

## Asynchronous search source (entity search)

```razor
<TmCommandPalette @bind-IsOpen="_open"
                  Actions="_localCommands"
                  SearchProvider="SearchAsync"
                  GroupByCategory="true"
                  ShowFooter="true"
                  HighlightMatches="true"
                  MinChars="0" />
```

```csharp
private async Task<CommandPaletteSearchResult> SearchAsync(string query, CancellationToken cancellationToken)
{
    var items = await _http.GetFromJsonAsync<List<CommandPaletteAction>>(
        $"/api/search?q={Uri.EscapeDataString(query)}", cancellationToken);

    return items is null or { Count: 0 }
        ? CommandPaletteSearchResult.NoResults
        : CommandPaletteSearchResult.Found(items);
}
```

The async contract (decision DEC-CMDPALETTE-ASYNC):

- **Local first (O3).** Local `Actions` filter synchronously and immediately (title, description,
  code); asynchronous results are appended **after** them. `GroupByCategory` groups the combined
  list by `Category` — the "sections of similar results" (e.g. PromptHelper's entity types) are
  the category groups, no separate section concept exists (O6). With GroupByCategory the order
  is **local first *per group***, not globally: grouping pulls each category's items together and keeps
  the first-seen order of the categories, so inside every group the local matches precede the remote
  ones, but a remote item of an early category renders above a local item of a later category.
  Without GroupByCategory the list is strictly local matches, then remote results.
- **Debounce and cancellation.** Every query change cancels the previous `CancellationTokenSource`
  (debounce timer *and* a running request). A response from a superseded request is dropped even
  when the provider ignored the token (a monotonic request sequence). `OperationCanceledException`
  is swallowed; any other exception is a **transient** failure.
- **`CommandPaletteSearchResult`** (in Abstractions, no UI dependency — a backend can return it
  without referencing the Blazor package) distinguishes `Ok` / `Empty` / `Transient`. Empty is
  "the fetch succeeded, nothing matched"; Transient is "the fetch failed, retry may succeed". The
  component **never retries on its own** — the user (retry button) or the host does.
- **Loading.** Current results stay visible while a fetch runs (no flicker); the list carries
  `aria-busy="true"` and three `TmSkeleton` rows render below (`LoadingTemplate` overrides the
  rows). When there is nothing to show yet, the skeleton replaces the empty state.
  `IsLoading` lets a host that fills `Actions` itself drive the same states.
- **Close/dispose.** Closing the palette or disposing the component cancels the in-flight
  request; reopening starts with an empty query and no stale results.
- **Live region.** One polite `role="status"` region announces "Searching…" and then the result
  count (localized plural family `TmCommandPalette_ResultCount`); errors are announced through
  the error row's `role="alert"`.

Defaults: `DebounceMs = 300`, `MinChars = 1` (0 = the provider runs even for the empty query,
e.g. "recent items"). MinChars counts the **trimmed** query (whitespace alone never calls the
provider). The debounce wait already counts as loading (skeleton + ria-busy); DebounceMs=0 runs
the provider immediately. The live region is rendered (empty) from the moment the palette opens and
only its text changes. **Retry** leaves the error state at once (the skeleton shows while the new
request runs, a second failure brings the alert back) and moves focus to the search input, because
the retry button unmounts with the alert.

## Item anatomy

```
[icon]  Title   Description   [badge] [CODE]   Ctrl+N
```

- `ShowIcons` (default **true**): `Icon` renders through `TmIcon` (`aria-hidden`); an action
  without an icon gets an empty slot of the same width so titles line up. Set `ShowIcons="false"`
  to remove the slot entirely. The slot is also dropped while **no visible item** has an icon (no dead
  gutter); as soon as one does, every item carries it.
- `ItemTrailingTemplate`: replaces the default trailing slot (badge + code).
- `ItemTemplate`: replaces the whole item *content*; the option wrapper — `li`, `role="option"`,
  stable `id`, `aria-selected` and click-to-execute — stays the component's.
- `HighlightMatches` marks the query in the title and the code via `TmHighlightedText`.
- The accessible name of an option is title + description + badge + code; the icon and shortcut
  chips decorate.

## States and templates

| State | Default rendering | Override |
|---|---|---|
| Loading | 3 skeleton rows (`aria-hidden`), list keeps `aria-busy` | `LoadingTemplate`, `IsLoading` |
| Empty | localized "No commands found" | `EmptyTemplate` (receives the query) |
| Transient error | inline `role="alert"` row + retry button | `TransientErrorTemplate` (receives the retry callback) |
| Footer | ↑↓ navigate, ↵ select, Esc close (`<kbd>` hints) | `FooterTemplate` (renders without `ShowFooter`) |

The retry button is a native `<button>`: Tab reaches it and Enter/Space are its own activation —
it sits outside the arrow-key navigation. The footer is not a live region and is hidden on a
`(pointer: coarse)` device via CSS.

## Accessibility

The search input is a `role="combobox"` with `aria-activedescendant` pointing at the highlighted
`role="option"`; arrow keys wrap, Enter executes the highlighted action **on the input only**
(the close/retry buttons are native, so Enter on them never double-executes), and Escape closes
the palette through the focus scope. The highlighted option is kept by action `Id` across result
refreshes. All text paints are held to WCAG AA 4.5:1 in both themes by
`CommandPaletteContrastTests`.

## Demo

`/layout` — the *Entity search* sample simulates a server (fixed 600 ms delay, a random-failure
toggle) over a catalog of use cases / decisions / requirements with badges, codes and category
sections.
