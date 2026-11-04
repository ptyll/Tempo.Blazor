# TmPromptLibrary

A library of reusable prompt templates: the templates are text with `{{variable}}` placeholders, a reader picks one,
fills in a form generated from its variables, and the assembled prompt is previewed live — exactly the text that
"Use" hands the host.

`TmPromptLibrary` lives in the **Tempo.Blazor.AI** package; the templates and the parser live in
**Tempo.Blazor.Abstractions** (`Tempo.Blazor.AI.Models`) and reference no provider SDK. The component renders the
templates it is given and owns no persistence and no transport: it reports intent through `OnUse`, `OnCreate`,
`OnEdit` and `OnDelete`.

```razor
@using Tempo.Blazor.Components.AI
@using Tempo.Blazor.AI.Models

<TmPromptLibrary Templates="@templates"
                 @bind-SelectedTemplateId="selectedId"
                 @bind-Values="values"
                 OnUse="OnUseAsync"
                 OnCreate="CreateAsync"
                 OnEdit="EditAsync"
                 OnDelete="DeleteAsync" />

@code {
    private IReadOnlyList<PromptTemplate> templates =
    [
        new PromptTemplate("brief", "Brief a topic", "Write a brief about {{topic}} for {{audience}}.")
        {
            Category = "Writing",
            Variables =
            [
                new PromptVariable("topic", "Topic"),
                new PromptVariable("audience", "Audience") { IsRequired = false, DefaultValue = "general readers" },
            ],
        },
        new PromptTemplate("review", "Review a change", "Review this diff:\n{{diff}}")
        {
            Category = "Engineering",
            Variables = [new PromptVariable("diff", "Diff") { Kind = PromptVariableKind.Multiline }],
        },
    ];

    private string? selectedId;
    private IReadOnlyDictionary<string, string?>? values;

    private Task OnUseAsync(PromptTemplateUse use) => composer.SetDraftAsync(use.Prompt);
}
```

## The template

| Member | Meaning |
| --- | --- |
| `Id` | Stable identity inside the library; the row's `@key`, the selection value and what a host persists. A missing id is generated at construction |
| `Title` | What the reader picks the template by. Rendered as text |
| `Category` | The host's own grouping vocabulary, rendered verbatim. `null`/blank puts the template under the localized "Uncategorised" filter |
| `Description` | What the template is for, shown in the row and in the detail |
| `Body` | The prompt text with `{{variable}}` placeholders and the `{{{{` / `}}}}` escapes |
| `Variables` | The explicit declaration. `null`/empty DERIVES one variable per placeholder |

A variable is `PromptVariable(Name, Label?)`:

| Member | Meaning |
| --- | --- |
| `Label` | The field's visible label; `null` falls back to `Name`. A derived variable is labelled by its name |
| `Description` | Help text under the field |
| `DefaultValue` | The value used when the reader leaves the field empty |
| `IsRequired` | Default `true`: an empty required field makes the prompt incomplete |
| `Kind` | `Text` (default, a text box), `Multiline` (a text area) or `Select` (a select over `Options`) |
| `Options` | The values a `Select` offers. Empty means the prompt library renders a text box instead |

## The parser

`PromptTemplateParser` is pure C# and is the only place that parses or substitutes anything. It substitutes TEXT and
nothing else:

- `{{topic}}` is a placeholder; `{{  topic  }}` is the same variable (the inner whitespace is trimmed).
- A name is a letter, a digit or an underscore followed by letters, digits, underscores, dots and hyphens — Unicode
  letters included, so `{{téma}}` works.
- `{{1 + 2}}` is **literal text**, because it is not a variable name. Nothing is evaluated, ever.
- `{{{{` renders `{{` and `}}}}` renders `}}`; a single unmatched pair stays literal.
- A name the template's explicit `Variables` does not declare is literal text too — the declaration is authoritative.
- A substituted value is never re-expanded: `{{greeting}}` with the value `Hello {{name}}` renders exactly that.

```csharp
var result = PromptTemplateParser.Build(template, values);

result.Prompt;        // the assembled prompt (unfilled variables keep their placeholder)
result.Segments;      // literal runs + filled/unfilled variable runs — what the preview renders
result.Variables;     // the resolved declaration
result.Values;        // the effective values (input + defaults), by name
result.Errors;        // MissingRequired / NotAnOption
result.IsValid;       // Errors.Count == 0
result.ToUse(template); // PromptTemplateUse — throws when the assembly is invalid
```

The assembly is always produced, even when it is invalid: the preview shows the prompt with its unfilled
placeholders in place, so the reader sees exactly what is missing.

## The component

| Parameter | Type | Default | Meaning |
| --- | --- | --- | --- |
| `Templates` | `IReadOnlyList<PromptTemplate>?` | `null` | The library, in the host's order. `null`/empty renders the empty state |
| `SelectedTemplateId` / `SelectedTemplateIdChanged` | `string?` / `EventCallback<string?>` | `null` | Two-way selection. `null` selects the first template and reports it, so the detail pane is never empty |
| `Values` / `ValuesChanged` | `IReadOnlyDictionary<string, string?>?` / `EventCallback<...>` | `null` | The draft values by variable name. Setting it pre-fills the fields; every edit and every reset is reported as a fresh snapshot (the reset is raised after the render, so a `@bind-Values` host never keeps the previous template's draft) |
| `OnUse` | `EventCallback<PromptTemplateUse>` | — | Pressed Use on a COMPLETE prompt. Unbound means the action is not rendered |
| `OnCreate` | `EventCallback` | — | "New template". Unbound means no Create action |
| `OnEdit` / `OnDelete` | `EventCallback<PromptTemplate>` | — | "Edit"/"Delete" for the selected template. Unbound means no such action |
| `Title` | `string?` | `null` | The visible heading and the root group's name. `null` uses the localized "Prompt library" |
| `ShowTitle` | `bool` | `true` | Whether the heading renders |
| `HeadingLevel` | `int` | `3` | The heading's level, clamped 2–6; the detail title renders one level below |
| `SearchPlaceholder` | `string?` | `null` | The search field's placeholder and accessible name |
| `LayoutMode` | `TmLayoutMode` | `Auto` | `Auto` measures the component's own container; a forced value pins desktop / tablet / the phone drill-down |
| `InitialMode` | `TmLayoutMode` | `Desktop` | The mode before the first measurement |
| `Class`, `AdditionalAttributes` | | | Root class / forwarded attributes |

### List, search and categories

The list is a semantic `<ul>` of native `<button>` rows (Enter and Space are native — no key is emulated). Search
matches the title, description, category and body, ignoring case and diacritics ("uvitani" finds "Uvítání"). The
category filter is a `TmSegmentedControl` radio group offering "All", every category in host order (with its count)
and "Uncategorised" when at least one template has no category; the sentinel is the public
`TmPromptLibrary.UncategorisedCategoryValue`, so a host can persist a filter. A search that matches nothing renders
the localized no-results sentence naming the term.

### The detail

One `TmFormField` per resolved variable, `Required` mirroring `IsRequired`, the error text tied to the control through
`aria-describedby` (the wrapped `TmTextInput` / `TmTextArea` / `TmSelect` picks up the field's error id automatically):

- `Text` → `TmTextInput`, updated on input.
- `Multiline` → `TmTextArea` with `Immediate`, so the preview follows the typing.
- `Select` → `TmSelect<string>` over `Options` with a localized placeholder.

A field's error is withheld until the reader has touched the field or pressed Use — a form nobody has touched does
not open covered in red.

### The live preview

The preview renders `PromptTemplateParser.Build(...)` runs: literal text as text, a filled variable as a `mark`, and
an unfilled one as a `mark` with the dashed `--empty` modifier **and** the localized word "Unfilled" inside it (the
modifier class is never the only signal). A prompt's own line breaks survive (`white-space: pre-wrap`) and long
tokens wrap instead of widening the card.

### Use, Copy and the validation summary

Use is rendered only with a bound `OnUse` and is **ARIA-disabled** (`aria-disabled="true"`, never the native
attribute) while the assembly is invalid: a natively disabled button leaves the tab order, so a keyboard user tabbing
past it would never hear why it is unavailable. The action therefore stays reachable — pressing it marks the form as
attempted, which reveals the summary (the button's `aria-describedby`) and announces it in the live region — while
`UseAsync` refuses to send an incomplete prompt, so one never reaches a host. Copy writes the assembled prompt with
`navigator.clipboard.writeText` and announces the result — a refused clipboard write never claims success.

The component owns exactly ONE polite live region. It carries the copy result and the validation summary ("Fill in the
required variables: …") and nothing else: the per-field errors are not `role="alert"` (they are described by their
control instead), so nothing is announced twice. The region is persistent and only its keyed child is replaced, so a
sentence that repeats is still a mutation a screen reader announces.

## Responsive behaviour

`LayoutMode.Auto` measures the component's own container (`TmLayoutObserver`), so a library in a narrow card behaves
like one on a phone.

- **Desktop / tablet:** two panes — the list and the detail side by side (the grid uses `auto-fit`, so a narrow
  container stacks them without a second breakpoint definition).
- **Mobile:** the list is the whole surface; picking a row opens the detail as a drill-down with a Back action. Opening
  moves the focus into the detail; Back returns it to the row that opened it (WCAG 2.4.7 — a reader is never left
  behind on a control that disappeared), falling back to the row's newest position when the list was filtered.

## Accessibility

- The root is a `role="group"` named by the visible heading (or the host's `Title`); the list is a labelled list, the
  detail is a region named by its own title, the preview is a `role="group"` named by its "Preview" label, and the
  variables are a labelled group.
- Every action is a native `<button>`; there is no key emulation and no tab stop on decoration. The phone Back action
  is named by its visible text (no duplicated `aria-label`), and Use stays in the tab order through `aria-disabled`
  so its reason is reachable.
- The selected row carries `aria-current="true"` (not `page` — it is one option in a library).
- `(pointer: coarse)` raises the rows and every action to the 44 px `--tm-touch-target` and stacks the actions.
- `forced-colors` hands every focus ring back to the system `Highlight` and opts the variable marks out of the palette
  collapse (`forced-color-adjust: none`), keeping the dashed border of an unfilled one.
- The surface declares no motion: the preview re-renders on every keystroke, so there is nothing to animate and no
  reduced-motion block is needed.

## Localization

`TmPromptLibrary_*` keys in English, Czech, Slovak and French (`TmResources*.json`) plus `MockTmLocalizer`: the title,
the search placeholder, the category label, "All"/"Uncategorised", the empty and no-results states, "Preview",
"Unfilled", the four actions and Back, the required / not-an-option field errors, the validation summary, the copy
result and failure, the select placeholder, the list label and the variables label. A host's own strings (template
titles, categories, descriptions, variable labels and option values) are rendered verbatim and never translated by the
component.

## Styling

Every rule reads `--tm-*` semantic tokens (so the dark theme follows automatically, with no `[data-theme]` block) and
the component-local knobs are declared on the root:
`--tm-prompt-library-row-bg`, `-row-hover-bg`, `-row-selected-bg`, `-row-selected-border`, `-token-bg`, `-token-fg`,
`-token-border`, `-token-empty-bg`, `-token-empty-fg`, `-token-empty-border`, `-preview-bg`, `-panel-min-width`,
`-list-max-block-size`.

Classes: `.tm-prompt-library`, `__title`, `__toolbar`, `__search`, `__categories`, `__body`, `__panel`,
`__panel--list`, `__panel--detail`, `__list`, `__item`, `__row`, `__row-main`, `__row-title`, `__row-category`,
`__row-description`, `__row-chevron`, `__no-results`, `__empty`, `__detail`, `__back`, `__detail-title`,
`__detail-category`, `__detail-description`, `__detail-empty`, `__variables`, `__field`, `__preview`,
`__preview-label`, `__preview-body`, `__token`, `__token--empty`, `__validation`, `__actions`, `__use`, `__copy`,
`__create`, `__edit`, `__delete`. They are pinned by `TmPromptLibraryCssTests` (a rule per emitted class, no motion,
declared knobs, coarse targets, forced colours, wrapping).

## Wireframe

The component is a wireframe citizen: the `prompt-library` UI role, the `TmPromptLibrary` schema (`title`, `templates`,
`categories`, `showSearch`, `showPreview`, `showActions`) and a declarative stencil that draws the search field, the
category chips, the template rows and the detail pane with its preview and actions.
