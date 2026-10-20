# Cluster C — toolbar core, action bar, editor shell, side panel

Read-only analysis at `main @ 846bc242` (clean except untracked `tmp/`, `output/`, `diagrams.db*`).
Binding decisions read with `get_app_decision_detail`: F4-TOOLBAR-OVERFLOW, F5-ACTION-BAR-API, F6-SHELL-PANEL-STATES,
F6-TABLET-DOCKED-PANELS, F6-SHELL-BREAKPOINTS, F6-SHELL-RESIZER, F6-SIDE-PANEL-PRESENTATION, F6-EDITOR-SHELL-EMAIL-API.
Plans read: DocumentEditor `146c7e1b` (Fáze 4 + 8), DiagramEditor `94d441f2` (Fáze 1, 4, 6), e-mail `da0a2531` (Fáze 8).

Facts I confirmed by running code:
- `node --test src/Tempo.Blazor/wwwroot/js/__tests__/tm-toolbar.test.mjs …/tm-menu-nav.test.mjs` → **82 pass, 0 fail**.
- `ActionOverflowMenu` is `internal`. `Tempo.Blazor.csproj:88-90` has `InternalsVisibleTo` for **`Tempo.Blazor.Tests` only**. The
  DocumentEditor and DiagramEditor packages therefore **cannot** compose the More menu directly. They can reach it only
  through a public `TmToolbar`. This is the main reason the core prerequisites below are real blockers.

Binding constraints that every design below respects:
- **F5 (user decision):** `TmActionItem.Priority` is an overflow rank only. Render order is always `Items` order. Groups never reorder anything.
- **F4:** the DOM order is the order of record. The More menu is the single `ActionOverflowMenu`. Every menu item keeps a
  **stable element-reference capture with loop-invariant sequence numbers** (H1 renderer crash). Sequence numbers must
  not be derived from indexes, and capture frames must not be conditional.
- **F6:** tablet uses docked panels with **at most one expanded**, and there is **no modal**. `Breakpoints` moves only the
  markup branch, and stylesheets keep the shared literals.

Naming check: `TmToolbarItem`, `TmToolbarGroup`, `IToolbarOverflowItem` and `ActionMenuRuns` do not exist anywhere in `src/` (grep: 0 hits).

---

## C1 — [bdab3cb6] Collapsible NON-button toolbar item contract

### 1. Status: **OPEN**
Proof:
- `TmToolbar.razor:44` declares `private readonly List<TmToolbarButton> _buttons`, and `Register(TmToolbarButton)` (`:116`) is the only registration path.
- `docs/toolbar.md:179-181` lists it as prerequisite 1, still open.
- No other component cascades `TmToolbar`.

### 2. Current state
- **`src/Tempo.Blazor/Components/Toolbar/TmToolbar.razor`**
  - `:24-36`: `<CascadingValue Value="this" IsFixed="true">` wraps `ChildContent` and `Actions`. The More menu sits outside the cascade.
  - `:44-46`: `_buttons` / `_menu` / `_collapsed` are all typed `TmToolbarButton`.
  - `:116-137`: `Register` / `Unregister` / `ButtonChanged`, all taking `TmToolbarButton`.
  - `:168-178`: `ApplyDomOrder` sorts by `button.MenuId`.
  - `:180-208`: `Recompute` calls `ToolbarOverflowLayout.Resolve(_buttons, _maxVisible, static b => b.Priority)`; `_pinned` is set when any `OverflowOnly` exists.
  - `:210-242`: `MoreMenu` maps each button to **one** `TmActionItem` (`Id=MenuId, Label=MenuLabel, Icon, Disabled, Danger, OnClick=target.ClickAsync`).
- **`TmToolbarButton.razor`**
  - `:10-16`: `OverflowOnly` renders `<span hidden data-tm-toolbar-item=… data-pin="always">`.
  - `:19-39`: the button carries `inert`, `aria-hidden`, `data-tm-toolbar-item`, `data-rank`, `data-pin` (`never|auto`) and `tm-toolbar-item--collapsed`.
  - `:86` `MenuId`, `:92` `Registered`, `:94` `Collapsed`, `:96` `OmittedFromBar`.
  - `:122-142`: registration happens in `OnParametersSet`, with a snapshot tuple (`:44`, `:144-145`).
- **`src/Tempo.Blazor/wwwroot/js/tm-toolbar.js`**
  - `:99-108` `isFlattened`: a child is flattened when it is `tm-toolbar-start/actions`, has `data-tm-toolbar-group`, **or contains any `[data-tm-toolbar-item]`**.
  - `:162-170`: any element with `data-tm-toolbar-item` is measured as **one item** (`outerWidth`). The measurement is therefore already element-agnostic: a `<div data-tm-toolbar-item>` wrapper works today if it contains no nested item.
  - `:71` `CONTROL_SELECTOR = 'button.tm-toolbar-btn, button.tm-toolbar-more, [data-tm-toolbar-control]'`.
  - `:85-88` `rovingItems`.
  - `:241-244` `isTextEntry` covers INPUT, TEXTAREA, SELECT and contenteditable. Those keys are never intercepted.
- **`src/Tempo.Blazor/wwwroot/css/components/_toolbar.css:92-98`**: `.tm-toolbar-item--collapsed` is out of flow, uses `visibility:hidden` and `pointer-events:none`. It already works for any element.
- **What the DocumentEditor needs** (`src/Tempo.Blazor.DocumentEditor/Components/DocumentEditor/TmDocumentEditorToolbar.razor`, 4575 lines, 79 `data-command`):
  - Native `<select>`s:
    - `:226` style
    - `:366` font family
    - `:382` font size
    - `:570` change case
    - `:711/726/745` line spacing
    - `:1090` page size
    - `:1119` margins
    - `:1159` columns
    - `:1204` line numbering
    - `:1402` review display mode
  - `TmColorPicker` at `:617` (font colour) and `:638` (highlight).
  - Grid picker at `:828-848` (`aria-haspopup="grid"` button plus `TmDocumentTableGridPicker`).
  - Image insert menu at `:852`.
  - ~30 toggle buttons with `aria-pressed` (incl. **`"mixed"`**, `:3608/:3659/:3682`).
  - Groups are `<section class="tm-document-editor__ribbon-group" aria-label=…>` (`:172`, `:220`, `:659`, …).
  - The declarative model `Registry/DocumentToolbarItemKind.cs` has `Button | Toggle | Select | ColorPicker | SplitButton | Menu | GridPicker | Separator`.
  - The DE menu (`TmDocumentToolbarOverflowMenu.razor:51-75`) renders **group headers** (`role="presentation"`) and has a **search box** (`:34-44`, shown at ≥8 items, `TmDocumentEditorToolbar.razor:2258`).
- **DiagramEditor** (`src/Tempo.Blazor.DiagramEditor/Components/Diagram/TmDiagramEditor.razor`):
  - `:89` is `role="group"` (no `TmToolbar`).
  - `TmDropdown` at `:179`, `:195`, `:332`, `:345`, `:368`.
  - A native `<select>` (ruler unit) at `:288`.
  - Number inputs at `:300-312`.
  - Tool modes use `Class="tm-toolbar-btn--active"` with **no `aria-pressed`** (`:146-157`, `:222-243`, `:270-282`, `:386`).

### 3. Proposed change (step by step)

**Design choice: the menu entry.** A `RenderFragment` "MenuTemplate" rendered inside `role="menu"` is **rejected**:
- It would put a `<select>` inside a menu, which is invalid ARIA (a menu may own only menuitem*/group/separator).
- `tm-menu-nav.js` could not rove it.
- It would add conditional capture frames, which is the H1 crash.

An item is therefore offered in More as **plain `TmActionItem`s**. There are two shapes:
- (a) **One entry** (`MenuLabel` + `OnMenuInvoke`). It runs a host action, typically opening the full picker in a dialog/sheet with `MenuKeepOpen=true`.
- (b) **A labelled group of entries** (`MenuItems`). Example: font size 8–72 as checkable entries, or Align L/C/R. This reuses C2 (Checked) and C3 (groups).

**Step 1 — `src/Tempo.Blazor/Components/Toolbar/IToolbarOverflowItem.cs` (new, internal)**
```csharp
namespace Tempo.Blazor.Components.Toolbar;

using Tempo.Blazor.Components.Actions;

/// <summary>
/// Internal. What <see cref="TmToolbar"/> needs from anything that can leave the bar into the "More" menu:
/// <see cref="TmToolbarButton"/> and <see cref="TmToolbarItem"/>. Registration is by the cascade at any depth;
/// the DOM order reported by tm-toolbar.js (<c>data-tm-toolbar-item</c> = <see cref="MenuId"/>) is the order of record.
/// </summary>
internal interface IToolbarOverflowItem
{
    /// <summary>Stable id; rendered as <c>data-tm-toolbar-item</c> and used as the More entry id.</summary>
    string MenuId { get; }

    /// <summary>The EFFECTIVE priority the partition ranks (an item with no menu representation reports Pinned).</summary>
    ToolbarButtonPriority EffectivePriority { get; }

    /// <summary>Re-render after the toolbar's partition or labels changed.</summary>
    void Refresh();

    /// <summary>Appends this item's More-menu entries (one or more, in written order) to <paramref name="target"/>.</summary>
    void AppendMenuEntries(List<TmActionItem> target);
}
```

**Step 2 — `TmToolbar.razor`**
- Retype `_buttons`, `_menu` and `_collapsed` (`:44-46`) to `IToolbarOverflowItem`.
- Change `Register`, `Unregister`, `ButtonChanged` and `IsCollapsed` (`:114-137`) to take `IToolbarOverflowItem`.
- Rename `ButtonChanged` → `ItemChanged` (internal, not breaking). `ApplyDomOrder` (`:173`) keeps using `MenuId`.
- In `Recompute` (`:190`, `:195`), use `static b => b.EffectivePriority`.
- Replace the `MoreMenu` loop (`:212-225`) with:
```csharp
var items = new List<TmActionItem>(_menu.Count);
foreach (var entry in _menu) entry.AppendMenuEntries(items);
```
- Move the per-button mapping into `TmToolbarButton.AppendMenuEntries`:
```csharp
void IToolbarOverflowItem.AppendMenuEntries(List<TmActionItem> target) => target.Add(new TmActionItem
{
    Id = MenuId, Label = MenuLabel, Icon = Icon, Disabled = Disabled,
    Danger = Variant == ButtonVariant.Danger,
    Checked = Pressed, CheckedMixed = PressedMixed,               // C2
    Group = Group?.GroupKey, GroupLabel = Group?.Label,          // C3
    OnClick = EventCallback.Factory.Create(this, ClickAsync),
});
```
  `EventCallback.Factory.Create(this, …)` uses the BUTTON as receiver, so the button re-renders. The toolbar re-renders anyway on `Recompute`.

**Step 3 — `TmToolbarButton.razor`**
- Implement `IToolbarOverflowItem`: `EffectivePriority => Priority`. `MenuId` and `Refresh` already exist.
- Add a `[CascadingParameter] private TmToolbarItem? ParentItem { get; set; }`. Change `Registered` (`:92`) to `Toolbar is { CollapsesButtons: true } && ParentItem is null && _registeredWith is not null`.
- In `OnParametersSet` (`:127`), skip registration when `ParentItem is not null`. A button nested in an item is part of that item and never registers twice.

**Step 4 — `src/Tempo.Blazor/Components/Toolbar/TmToolbarItem.razor` (new, public)**
```razor
@namespace Tempo.Blazor.Components.Toolbar
@using System.Globalization
@using Tempo.Blazor.Components.Actions
@implements IDisposable
@implements IToolbarOverflowItem

@* A NON-button control (select, colour picker, grid picker, toggle, dropdown) that can leave a
   TmToolbar Overflow=Menu bar like a TmToolbarButton. The wrapper is the measured item; it collapses
   exactly like a button (out of flow, invisible, inert); in More it is offered as plain menu entries. *@
@if (OmittedFromBar)
{
    <span hidden data-tm-toolbar-item="@MenuId" data-pin="always"></span>
}
else
{
    <div class="tm-toolbar-item @(Collapsed ? "tm-toolbar-item--collapsed" : null) @Class"
         inert="@Collapsed"
         aria-hidden="@(Collapsed ? "true" : null)"
         data-tm-toolbar-item="@(Registered ? MenuId : null)"
         data-rank="@(Registered ? Rank.ToString(CultureInfo.InvariantCulture) : null)"
         data-pin="@(Registered ? (EffectivePriority == ToolbarButtonPriority.Pinned ? "never" : "auto") : null)"
         @attributes="AdditionalAttributes">
        <CascadingValue Value="this" IsFixed="true">@ChildContent</CascadingValue>
    </div>
}
```
Public parameters, each with exact XML docs:
```csharp
/// <summary>The control rendered on the bar (a TmSelect, TmColorPicker, TmToggle, TmDropdown, …).</summary>
[Parameter] public RenderFragment? ChildContent { get; set; }

/// <summary>
/// How important the item is when its <see cref="TmToolbar"/> runs out of room (<see cref="ToolbarOverflow.Menu"/>).
/// An overflow rank only — items render in written order. An item with neither <see cref="MenuLabel"/> nor
/// <see cref="MenuItems"/> has no menu representation and never leaves the bar (it behaves as
/// <see cref="ToolbarButtonPriority.Pinned"/> whatever this value is).
/// </summary>
[Parameter] public ToolbarButtonPriority Priority { get; set; }

/// <summary>
/// The label of the item's "More" entry (or, with <see cref="MenuItems"/>, the header and accessible name of
/// its group). Required for the item to be able to collapse.
/// </summary>
[Parameter] public string? MenuLabel { get; set; }

/// <summary>Optional icon of the single "More" entry (an <c>IconNames</c> value). Ignored with <see cref="MenuItems"/>.</summary>
[Parameter] public string? MenuIcon { get; set; }

/// <summary>
/// Runs when the user picks the item's single "More" entry. Typically opens the full control in a dialog or
/// sheet (then set <see cref="MenuKeepOpen"/>). Invoked exactly once per activation. Ignored with <see cref="MenuItems"/>.
/// </summary>
[Parameter] public EventCallback OnMenuInvoke { get; set; }

/// <summary>Maps to <see cref="TmActionItem.KeepMenuOpen"/> for the single entry: the menu stays open while the
/// surface the action opens is shown (it paints above the menu). Default false.</summary>
[Parameter] public bool MenuKeepOpen { get; set; }

/// <summary>
/// Instead of one entry, offer these entries in "More", rendered as one labelled group (<see cref="MenuLabel"/>)
/// in the given order — e.g. the sizes of a font-size select as checkable entries (<see cref="TmActionItem.Checked"/>).
/// Each entry's <see cref="TmActionItem.Group"/> and <see cref="TmActionItem.GroupLabel"/> are overwritten by this item.
/// </summary>
[Parameter] public IReadOnlyList<TmActionItem>? MenuItems { get; set; }

/// <summary>Disables the "More" entry (or every <see cref="MenuItems"/> entry). The bar control's own disabled
/// state stays the host's: set both.</summary>
[Parameter] public bool Disabled { get; set; }

/// <summary>Additional CSS class for the wrapper element.</summary>
[Parameter] public string? Class { get; set; }

/// <summary>Additional HTML attributes for the wrapper element.</summary>
[Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }
```
`@code` internals:
- The registration logic is copied from `TmToolbarButton.razor:122-148`, with `Snapshot()` = `(Priority, MenuLabel, MenuIcon, Disabled, MenuKeepOpen, MenuItems /*reference*/, Group?.Label)`.
- `EffectivePriority => string.IsNullOrEmpty(MenuLabel) && MenuItems is not { Count: > 0 } ? ToolbarButtonPriority.Pinned : Priority`.
- `Collapsed` / `OmittedFromBar` / `Rank` follow the button (`:94-98`), but use `EffectivePriority`.
- `[CascadingParameter] private TmToolbar? Toolbar` and `[CascadingParameter] private TmToolbarGroup? Group`.
- `AppendMenuEntries`:
```csharp
void IToolbarOverflowItem.AppendMenuEntries(List<TmActionItem> target)
{
    if (MenuItems is { Count: > 0 } list)
    {
        var key = Group?.GroupKey is { } outer ? $"{outer}/{MenuId}" : MenuId;
        foreach (var source in list)
        {
            target.Add(new TmActionItem
            {
                Id = $"{MenuId}:{ActionOverflowMenu.ActionId(source)}", Label = source.Label, Icon = source.Icon,
                Disabled = Disabled || source.Disabled, Danger = source.Danger, Checked = source.Checked,
                CheckedMixed = source.CheckedMixed, KeepMenuOpen = source.KeepMenuOpen, OnClick = source.OnClick,
                Group = key, GroupLabel = MenuLabel,
            });
        }
        return;
    }
    target.Add(new TmActionItem
    {
        Id = MenuId, Label = MenuLabel ?? string.Empty, Icon = MenuIcon, Disabled = Disabled,
        KeepMenuOpen = MenuKeepOpen, OnClick = OnMenuInvoke,
        Group = Group?.GroupKey, GroupLabel = Group?.Label,
    });
}
```
  This copies the entries and never mutates the host's list. `ActionId` is already `internal static` in `ActionOverflowMenu.cs:215`.

**Step 5 — CSS (`src/Tempo.Blazor/wwwroot/css/components/_toolbar.css`, after `:17`)**
```css
/* A TmToolbarItem wrapper is ONE measured item: it keeps its natural width and leaves the bar into More
   as a whole (tm-toolbar.js measures [data-tm-toolbar-item] wrappers like buttons). */
.tm-toolbar-item { display: inline-flex; align-items: center; gap: var(--tm-space-1); flex-shrink: 0; min-width: 0; }
```
Regenerate `tempo-blazor.bundled.css`. It is a tracked source, rebuilt by the `BundleCssFiles` target in `Tempo.Blazor.csproj:169`.

**Step 6 — `tm-toolbar.js`**
- `isFlattened` (`:99`): add as the FIRST line `if ('tmToolbarItem' in (child.dataset ?? {})) return false;`. An item wrapper is never a layout group, even if a host nests another item inside it.
- Roving (`:71-88`): a `.tm-toolbar-item` contributes its **first non-text-entry focusable**. Text-entry controls stay their own Tab stop (see Q-C2).
```js
const ITEM_WRAPPER = '.tm-toolbar-item';
const FOCUSABLE = 'button:not([disabled]), [tabindex]:not([tabindex="-1"]), a[href]';
function itemControl(wrapper) {
    const marked = wrapper.querySelector?.('[data-tm-toolbar-control]');
    if (marked) return marked;                       // the host (or C5's TmDropdown) chose it
    const found = wrapper.querySelector?.(FOCUSABLE) ?? null;
    return found && !isTextEntry(found) ? found : null;
}
export function rovingItems(root) {
    const seen = new Set();
    const out = [];
    for (const el of Array.from(root.querySelectorAll(`${CONTROL_SELECTOR}, ${ITEM_WRAPPER}`))) {
        const control = el.classList?.contains('tm-toolbar-item') ? itemControl(el) : el;
        if (!control || seen.has(control) || isDisabled(control) || control.closest(EXCLUDED_SELECTOR)) continue;
        seen.add(control); out.push(control);
    }
    return out;
}
```
  `detach` (`:393`) must remove `tabindex` from the controls that `rovingItems` returned: `for (const c of rovingItems(root)) c.removeAttribute('tabindex')`. Remember them in `state.managed` (a Set filled in `refreshTabStops` / `onFocusIn`) and clear exactly those. That way a host's own `tabindex` on a non-managed element is never removed.

**Step 7 — `TmToolbarGroup.razor` (new, public; used by C3 but introduced here so items/buttons can read it)** — see C3.

**JSON documentation**
- New `JsonDocumentation/Components/TmToolbarItem.json`. Category `Toolbar`, `requiredImports` `@using Tempo.Blazor.Components.Toolbar`, every parameter above, `cssClasses: ["tm-toolbar-item","tm-toolbar-item--collapsed"]`. Two examples: a select with a `MenuItems` group, and a colour picker with `OnMenuInvoke` + `MenuKeepOpen`.
- Update `TmToolbar.json` (description: "buttons **and `TmToolbarItem`s** register").
- Run `dotnet run --project JsonDocumentation/JsonDocumentationGenerator/JsonDocumentationGenerator.csproj -- JsonDocumentation generate`.

**Localization:** the component itself adds no keys (labels are the host's). For the demo, add to `TmResources.json` / `.cs.json` / `.fr.json`, `MockTmLocalizer` and `LocalizationTestBase` (en + cs maps):

| Key | en | cs | fr |
|---|---|---|---|
| `TmToolbarDemo_ItemsTitle` | Non-button items | Položky, které nejsou tlačítka | Éléments autres que des boutons |
| `TmToolbarDemo_ItemsIntro` | Selects, colour pickers and toggles leave the bar like buttons. | Výběry, výběr barvy a přepínače odcházejí z lišty jako tlačítka. | Les listes, sélecteurs de couleur et bascules quittent la barre comme les boutons. |
| `TmToolbarDemo_FontSize` | Font size | Velikost písma | Taille de police |
| `TmToolbarDemo_TextColor` | Text colour | Barva textu | Couleur du texte |
| `TmToolbarDemo_ShowGrid` | Show grid | Zobrazit mřížku | Afficher la grille |

**Wireframe stencil:** no new stencil. `TmToolbarItem` has no visuals of its own, and the `TmToolbar` stencil (`tempo.stencil.json:377`) already has the `overflow` "⋯" variant. `TempoPackStructurePhase10Tests` lists types explicitly, so nothing fails.

**Docs**
- `docs/toolbar.md`: add a section "Non-button items (`TmToolbarItem`)". Rewrite `:175-188` so prerequisite 1 reads as done.
- `ToolbarDocumentationTests.cs:34-36` asserts the "Core prerequisites" wording. Update the test together with the doc (assert `TmToolbarItem`, `MenuItems`, "own Tab stop").

### 4. RED tests
**bUnit `tests/Tempo.Blazor.Tests/Toolbar/TmToolbarItemTests.cs`.** Reuse the harness from `TmToolbarOverflowTests.cs:22-76`: `SetupModule` stubs attach/detach, the viewport cascade is Desktop, and `OnFitChanged` drives the count.
1. `Item_InOverflowToolbar_RendersMeasuredWrapper`: `.tm-toolbar-item[data-tm-toolbar-item][data-rank='2'][data-pin='auto']`.
2. `Item_Collapsed_IsInertHidden_AndOfferedInMore` with `[Button "A", Item(Secondary, MenuLabel "Font")]` and `OnFitChanged(1)`:
   - the wrapper has `tm-toolbar-item--collapsed`, `inert` and `aria-hidden="true"`;
   - `MenuLabels == ["Font"]`.
3. `Item_MenuEntry_InvokesOnMenuInvoke_Once_AndClosesTheMenu`: mirrors `MenuItem_InvokesTheButtonsOnClick_OnceAndClosesTheMenu` (`TmToolbarOverflowTests.cs:195`).
4. `Item_MenuKeepOpen_LeavesTheMenuOpen`.
5. `Item_WithMenuItems_RendersOneLabelledGroup_InItemsOrder`:
   - `[role=menu] [role=group][aria-label='Font size'] [role^=menuitem]` texts `8,10,12`;
   - checked entry `aria-checked='true'` (needs C2/C3).
6. `Item_WithoutMenuRepresentation_IsPinned`: `data-pin='never'`. With `OnFitChanged(0)` it is never collapsed or in the menu.
7. `Item_OverflowOnly_RendersOnlyTheHiddenMarker_AndLivesInMore`.
8. `Item_Standalone_AndInOverflowNoneToolbar_RendersNoHooks`.
9. `MixedButtonsAndItems_MenuFollowsReportedDomOrder`: `OnFitChanged(0, ["b2","i1","b1"])` gives menu order b2, i1, b1. Read the ids from the `data-tm-toolbar-item` attributes.
10. `ToolbarButtonNestedInAnItem_DoesNotRegisterTwice`: exactly one `[data-tm-toolbar-item]`.
11. `Item_Dispose_Unregisters`: remove the item and re-render; the menu no longer offers it.

**Node `src/Tempo.Blazor/wwwroot/js/__tests__/tm-toolbar.test.mjs`** (stub style as in `:121-189`, `:447-457`):
- `C1 a .tm-toolbar-item wrapper carrying data-tm-toolbar-item is measured as ONE item even if it contains another [data-tm-toolbar-item]`.
- `C1 rovingItems adds the first non-text-entry focusable of each .tm-toolbar-item, in DOM order, exactly once`.
- `C1 rovingItems skips a collapsed item, a disabled control and an item whose only focusable is a <select>`.
- `C1 detach removes only the tabindex values the module set`.

**E2E `tests/Tempo.Blazor.E2E/ToolbarOverflowE2ETests.cs`**, on a new demo card `toolbar-items-card` in `ToolbarFormsPage.razor`: a `TmToolbar Overflow=Menu` containing `TmToolbarItem(TmSelect font size, Secondary, MenuItems)`, `TmToolbarItem(TmColorPicker, Secondary, OnMenuInvoke opens a TmModal, MenuKeepOpen)` and `TmToolbarItem(TmToggle "Show grid", Primary, MenuLabel)`.
- `C1_Items_1024_NonButtonControlsCollapseIntoMore_AndComeBackWhenWidened`: count `.tm-toolbar-item--collapsed`, open More, assert the group, widen, assert 0 collapsed.
- `C1_Items_Keyboard_ColorPickerTriggerJoinsTheRovingSet_SelectStaysItsOwnTabStop`.
- `C1_Items_390_Touch_SheetShowsTheFontSizeGroup_With44pxEntries`.

### 5. Verification
- `dotnet test tests/Tempo.Blazor.Tests --filter "FullyQualifiedName~Toolbar|FullyQualifiedName~ActionOverflow|FullyQualifiedName~MobileActionBar|FullyQualifiedName~ComponentDocumentationFreshness|FullyQualifiedName~MarkupClassCoverage"`
- `npm run test:overlay`
- E2E filter `ToolbarOverflowE2ETests`
- `JsonDocumentation generate`, then the freshness test

### 6. Breaking / CHANGELOG
- Not breaking. All `TmToolbar` members touched are `internal`; new components are additive.
- CHANGELOG `### Added`: "`TmToolbarItem` — non-button controls collapse into More". `### Changed`: "`tm-toolbar.js` roving includes `TmToolbarItem` controls".

### 7. Effort: **L** (≈ 3–4 d incl. demo + E2E)

### 8. Open questions
- **Q-C1 — menu shape.**
  - (A) one entry `MenuLabel` + `OnMenuInvoke`, or a group via `MenuItems`. **Recommended.**
  - (B) `MenuTemplate` RenderFragment inside the menu. Rejected: invalid ARIA in `role=menu`, no roving, H1 risk.
  - (C) submenus. Large; defer.
- **Q-C2 — text-entry controls** (`<select>`, `<input>`) in the roving set.
  - (A) stay their own Tab stop, as today: `isTextEntry` already exempts them from arrows. **Recommended.**
  - (B) join the roving set; the user cannot arrow out because the select consumes arrows.
  - (C) join, with Ctrl+Arrow escaping (non-standard).

### 9. Scope: **core follow-up plan** (new Phase "Toolbar items + groups + pressed").

---

## C2 — [b6c72292] `TmToolbarButton.Pressed` → `aria-pressed` → `TmActionItem.Checked` → `menuitemcheckbox`

### 1. Status: **PARTLY DONE**
- Keyboard support for checkable items is **already done**: `tm-menu-nav.js:18` `ITEM_SELECTOR = '[role="menuitem"], [role="menuitemcheckbox"], [role="menuitemradio"]'` is used by `enabledItems`, `rove` and `focusFirst`.
- Everything else is **OPEN**:
  - `TmToolbarButton` has no `Pressed`;
  - `TmActionItem.cs` has no `Checked`;
  - `ActionOverflowMenu.cs:170` hard-codes `role="menuitem"`;
  - `TmOverlayPanel.razor:340-342` `SheetInitialFocusSelector` lists only `[role='menuitem'], [role='option']`, so a sheet whose first entry is a `menuitemcheckbox` focuses the wrong element.

### 2. Current state
- `TmToolbarButton.razor:19-30`: no `aria-pressed`. A host can splat `aria-pressed` through `AdditionalAttributes`, but the More menu never knows.
- `TmActionItem.cs:10-68`: properties are `Id, Label, Icon, Priority, Overflow, Danger, Disabled, OnClick, KeepMenuOpen`.
- `ActionOverflowMenu.cs:156-194` `BuildMenu`, constant `seq = 16`, capture at `seq + 13`.
- `TmMobileActionBar.razor:201-222` renders bar tiles with no pressed state.
- DE uses tri-state `aria-pressed` including `"mixed"` (`TmDocumentEditorToolbar.razor:3387`, `:3608`). The Diagram tool modes use only a CSS class (`TmDiagramEditor.razor:146-157`).

### 3. Proposed change
**`TmToolbarButton.razor` (after `LabelPosition`, `:77`)**
```csharp
/// <summary>
/// Makes the button a toggle: <see langword="null"/> (default) is a plain button with no <c>aria-pressed</c>;
/// <see langword="true"/>/<see langword="false"/> render <c>aria-pressed</c> and the pressed look, and when the button
/// sits in a <see cref="TmToolbar"/>'s "More" menu it becomes a <c>menuitemcheckbox</c> with the same state. The host
/// owns the state: flip it in <see cref="OnClick"/>.
/// </summary>
[Parameter] public bool? Pressed { get; set; }

/// <summary>
/// Renders a toggle (<see cref="Pressed"/> = false) as partially on — <c>aria-pressed="mixed"</c> on the bar,
/// <c>aria-checked="mixed"</c> in "More" (e.g. Bold over a partly bold selection). Ignored when
/// <see cref="Pressed"/> is null or true.
/// </summary>
[Parameter] public bool PressedMixed { get; set; }
```
- Markup: add `aria-pressed="@AriaPressed"` before `@attributes` at `:29`, where `private string? AriaPressed => Pressed is null ? null : Pressed == true ? "true" : PressedMixed ? "mixed" : "false";`. A host splat still wins (`@attributes` comes last); document this.
- Snapshot (`:44`, `:144-145`): add `Pressed` and `PressedMixed` (a 9-element value tuple; `==` works).

**`TmActionItem.cs` (after `Danger`, `:46`)**
```csharp
/// <summary>
/// The action's on/off state when it is a toggle. <see langword="null"/> (default) is a plain action — a
/// <c>menuitem</c> in the "More" menu. <see langword="true"/>/<see langword="false"/> make it a
/// <c>menuitemcheckbox</c> with <c>aria-checked</c> and a check mark in the menu, and add <c>aria-pressed</c> to
/// its tile on the mobile action bar. Activating it still runs <see cref="OnClick"/>; the host flips the state.
/// </summary>
public bool? Checked { get; set; }

/// <summary>Renders a toggle whose <see cref="Checked"/> is false as partially on (<c>aria-checked</c> /
/// <c>aria-pressed</c> = <c>mixed</c>). Ignored when <see cref="Checked"/> is null or true.</summary>
public bool CheckedMixed { get; set; }
```

**`ActionOverflowMenu.cs` `BuildMenu`**
- Renumber the item frames in **increasing constant offsets from a const base**. This stays loop-invariant (the H1 rule), so a reorder never adds or removes a capture frame:

| Offset | Frame |
|---|---|
| 0 | `button` |
| 1 | `type` |
| 2 | `role` (`Checked is null ? "menuitem" : "menuitemcheckbox"`) |
| 3 | `aria-checked` (only when `Checked` is not null; values true/mixed/false as above) |
| 4 | `class` |
| 5 | `disabled` |
| 6 | `data-action-id` |
| — | `SetKey` (unchanged) |
| 7 | `onclick` |
| 8 | check span (only when `Checked` is not null): `<span class=CheckClass aria-hidden="true">` |
| 9 | its class |
| 10 | inner `TmIcon` (`IconNames.Check` when true, `IconNames.Minus` when mixed, none when false) |
| 11–12 | icon `Name` / `Size` |
| 13 | close span |
| 14–16 | icon component (`Name`, `Size`) |
| 17–19 | label span |
| 20 | `AddElementReferenceCapture` (**always**, last) |

- Add the parameter `[Parameter] public string? CheckClass { get; set; }` with the XML doc "CSS class of the check-mark slot of a checkable item."
- `TmToolbar.MoreMenu` passes `"tm-toolbar-more__check"`. `TmMobileActionBar` passes `"tm-mobile-action-bar__menu-check"`.

**`TmOverlayPanel.razor:340-342`**: change the selector to `".tm-overlay-panel-sheet__content [role='menuitem'], .tm-overlay-panel-sheet__content [role='menuitemcheckbox'], .tm-overlay-panel-sheet__content [role='menuitemradio'], .tm-overlay-panel-sheet__content [role='option']"`.

**`TmMobileActionBar.razor:201-207`**
- Add `if (item.Checked is not null) builder.AddAttribute(seq + 14, "aria-pressed", item.Checked == true ? "true" : item.CheckedMixed ? "mixed" : "false");`. Offsets `+14` and `+15` are free inside the 16-slot stride (`:196`).
- CSS: `.tm-mobile-action-bar__bar .tm-mobile-action-bar__action[aria-pressed="true"] { background: var(--tm-color-primary-subtle); color: var(--tm-color-primary); }`.

**CSS (`_toolbar.css`)**
```css
/* C2: a pressed toggle (aria-pressed) — attribute selector, no extra class. */
.tm-toolbar-btn[aria-pressed="true"], .tm-toolbar-btn[aria-pressed="mixed"] { background: var(--tm-color-primary-subtle); color: var(--tm-color-primary); }
.tm-toolbar-more__check { display: inline-flex; align-items: center; justify-content: center; inline-size: 1rem; flex-shrink: 0; }
```
Add the same `__menu-check` rule to `_mobile-action-bar.css`. Every class literal passed from C# needs a CSS rule; see `MarkupClassCoverageTests`.

**JSON documentation:** `TmToolbarButton.json` gains `Pressed` and `PressedMixed`; `Actions/TmActionItem.json` gains `Checked` and `CheckedMixed`. Regenerate.
**Localization:** none (assistive technology announces the state).
**Wireframe (optional):** the `TmToolbarButton` stencil (`tempo.stencil.json:378`) could fill its rect with `token("color.primary.subtle")` when a `pressed` prop is set. Cosmetic.

### 4. RED tests
- `TmToolbarButtonTests`:
  - `Pressed_Null_RendersNoAriaPressed`;
  - `Pressed_TrueFalse_RendersAriaPressed`;
  - `PressedMixed_RendersMixed_OnlyWhenNotPressed`;
  - `HostSplatAriaPressed_Wins`.
- `TmToolbarOverflowTests`:
  - `PressedButton_InMore_IsMenuItemCheckbox_WithAriaChecked` (true, false and mixed rows);
  - `TogglingPressed_WhileCollapsed_RefreshesTheMenuEntry`: change `Pressed`, re-render, `aria-checked` flips.
- `ActionOverflowMenuTests`:
  - `CheckedItem_RendersMenuItemCheckbox_WithCheckMarkSlot`;
  - `UncheckableItem_HasNoCheckSlot`;
  - `ReorderWhileOpen_TogglingCheckedOnTheFirstItem_DoesNotCrashTheRenderer`, modelled on `:139-171`;
  - `Sheet_FirstEntryIsCheckbox_IsTheInitialFocusTarget`: assert the drawer's `data-initial-focus-selector` / `InitialFocusSelector` contains `menuitemcheckbox`.
- `TmMobileActionBarTests`: `CheckedItem_TileCarriesAriaPressed`.
- Node `tm-menu-nav.test.mjs`: `C2 ArrowDown roves across menuitem and menuitemcheckbox entries`. This pins the existing selector.
- E2E `ToolbarOverflowE2ETests`: `C2_PressedToolInMore_AnnouncesItsState_AndTogglesOnce`, using the ribbon demo with a "Pan" `Pressed` button.

### 5. Verification
Same filters as C1, plus `npm run test:overlay` and an axe check on the open menu (no `aria-allowed-attr` violation).

### 6. Breaking / CHANGELOG
- Additive.
- `### Changed`: "A `TmToolbarButton` with `aria-pressed="true"` (incl. one splatted by a host) now paints the pressed look."
- Supersedes the F5 note "IsChecked/Pressed not needed". Update decision **F5-ACTION-BAR-API** via `upsert_decision` after shipping.

### 7. Effort: **M** (≈ 1.5 d)

### 8. Open questions
- **Q-C3 — API shape.**
  - (A) `bool? Pressed` + `bool PressedMixed`, and `bool? Checked` + `bool CheckedMixed`. Mirrors `TmCheckbox.Value` + `Indeterminate`. **Recommended.**
  - (B) one enum `ActionCheckState { None, Off, On, Mixed }` on both. Cleaner, but awkward for `@bind`-style hosts.
- **Q-C4 — radio semantics** for exclusive tool modes (Select/Pan/Edge).
  - (A) checkbox only now. **Recommended**: APG accepts toggle buttons, and a group label gives context.
  - (B) add `TmActionItem.CheckRole { Checkbox, Radio }` + `TmToolbarButton.PressedRole`, so a group renders `menuitemradio`.
- **Q-C5 — does activating a checkable entry keep the menu open?**
  - (A) close, as for every entry today (close-before-invoke contract). **Recommended.**
  - (B) keep checkboxes open (APG allows it). Changes the host contract in overlays.md.

### 9. Scope: **core follow-up plan** (same phase as C1/C3).

---

## C3 — [4c483f49] Group headers / separators in `ActionOverflowMenu`

### 1. Status: **OPEN**
- `ActionOverflowMenu.cs:156-194` renders a flat list.
- `TmToolbar` has no group knowledge: `data-tm-toolbar-group` is a plain HTML attribute used only by JS for measurement (`tm-toolbar.js:106`). C# never sees it.

### 2. Current state
- `docs/toolbar.md:63-81` documents a raw `<div role="group" aria-label data-tm-toolbar-group>` wrapper. It affects measurement only.
- The demo uses it in `ToolbarFormsPage.razor:141`.
- The DE menu shows headers (`TmDocumentToolbarOverflowMenu.razor:53-58`, `role="presentation"` divs inside `role="menu"`).

### 3. Proposed change
**`TmActionItem.cs` (additive)**
```csharp
/// <summary>
/// Stable key of the group the action belongs to in the "More" menu. Consecutive items (in <c>Items</c> order —
/// grouping never reorders) with the same non-empty key render as one <c>role="group"</c>, and adjacent runs are
/// divided by a <c>role="separator"</c>. Null or empty = ungrouped. No effect on the bar.
/// </summary>
public string? Group { get; set; }

/// <summary>Accessible name and visible header of the item's group in the "More" menu, read from the first item
/// of a run. Empty renders the run without a group wrapper (separators only).</summary>
public string? GroupLabel { get; set; }
```

**`TmToolbarGroup.razor` (new, public)**
```razor
@namespace Tempo.Blazor.Components.Toolbar
@* A semantic group of toolbar controls. On the bar it is a layout wrapper tm-toolbar.js flattens (each control
   still collapses on its own); in the "More" menu the collapsed members keep the group as a labelled section. *@
<div class="tm-toolbar-group @Class"
     role="@(string.IsNullOrWhiteSpace(Label) ? null : "group")"
     aria-label="@(string.IsNullOrWhiteSpace(Label) ? null : Label)"
     data-tm-toolbar-group="@GroupKey"
     @attributes="AdditionalAttributes">
    <CascadingValue Value="this" IsFixed="true">@ChildContent</CascadingValue>
</div>
@code {
    /// <summary>Accessible name of the group on the bar and the header of its section in the "More" menu.</summary>
    [Parameter] public string? Label { get; set; }
    /// <summary>The group's buttons and items.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }
    /// <summary>Additional CSS class for the group element.</summary>
    [Parameter] public string? Class { get; set; }
    /// <summary>Additional HTML attributes for the group element (e.g. a host's <c>data-tm-group</c>).</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }
    [CascadingParameter] private TmToolbar? Toolbar { get; set; }
    internal string GroupKey { get; } = $"g{Guid.NewGuid():N}";
    private string? _lastLabel; private bool _seen;
    protected override void OnParametersSet()
    {   // A renamed group must re-label its More section (the members' snapshots do not see the cascade change).
        if (_seen && _lastLabel != Label) Toolbar?.ItemChanged(null);
        _seen = true; _lastLabel = Label;
    }
}
```
- `TmToolbar.ItemChanged(IToolbarOverflowItem? item)`: a `null` item recomputes unconditionally.
- CSS: `.tm-toolbar-group { display: flex; align-items: center; gap: var(--tm-space-2); margin: 0; }`. The existing `[data-tm-toolbar-group]` rule (`_toolbar.css:17`) already covers it; add the class rule for coverage.
- `TmToolbarButton` / `TmToolbarItem` gain `[CascadingParameter] private TmToolbarGroup? Group` and set `Group`/`GroupLabel` (C1 Step 2).

**`ActionOverflowMenu.cs`**
- Add three parameters, each with an XML doc:
  - `string? GroupClass` — "CSS class of a group section";
  - `string? GroupLabelClass` — "CSS class of a group's visible header";
  - `string? SeparatorClass` — "CSS class of the divider between runs".
- Add a pure helper `src/Tempo.Blazor/Helpers/ActionMenuRuns.cs`:
```csharp
internal static class ActionMenuRuns
{
    /// <summary>Splits Items (order preserved) into runs of consecutive equal non-empty Group keys; ungrouped items
    /// form runs with a null key.</summary>
    public static IReadOnlyList<(string? Key, string? Label, IReadOnlyList<TmActionItem> Items)> Split(IReadOnlyList<TmActionItem> items);
}
```
- New `BuildMenu`. Groups and ungrouped items are **siblings** of the menu element, so keys compare (the Y9 lesson in `TmMobileActionBar.razor:190-192`). No per-run region.
```
OpenElement(0,"div") class=MenuClass
for each run r:
   if r > 0: OpenElement(2,"div"); SetKey(new SeparatorKey(run.KeyOrFirstId)); role=separator(3); class=SeparatorClass(4); CloseElement
   if run.Key is null or run.Label empty: foreach item → RenderItem(builder, item)   // keyed buttons, siblings
   else: OpenElement(5,"div"); SetKey(new GroupKey(run.Key)); role=group(6); aria-label=run.Label(7); class=GroupClass(8);
         OpenElement(9,"div"); class=GroupLabelClass(10); aria-hidden="true"(11); AddContent(12, run.Label); CloseElement;
         foreach item → RenderItem(builder, item); CloseElement
CloseElement
```
  - `private readonly record struct GroupKey(string Value);` and `SeparatorKey` are distinct key types, so they never equal an item's string id.
  - `RenderItem` keeps the C2 constant offsets from a const base (`ItemSeq = 16`).
  - ARIA: `role=menu` may own `menuitem*`, `group` and `separator`. The visual header is `aria-hidden` (the group's name is its `aria-label`), so no presentational text sits in the menu's accessibility tree. `tm-menu-nav.js` already skips non-item elements (selector at `:18`), and typeahead reads only item labels.
- `TmToolbar.MoreMenu` passes `tm-toolbar-more__group` / `__group-label` / `__separator`. `TmMobileActionBar` passes `tm-mobile-action-bar__menu-group` / `__menu-group-label` / `__menu-separator`.
- CSS in both files:
  - group: `display:flex; flex-direction:column; gap: var(--tm-space-1)`;
  - label: `padding: var(--tm-space-1) var(--tm-space-3); font-size: var(--tm-font-size-xs); font-weight: var(--tm-font-weight-semibold); color: var(--tm-text-secondary); text-transform: none`;
  - separator: `block-size: 1px; background: var(--tm-border-color); margin-block: var(--tm-space-1)`.

**Keyboard:** unchanged. ArrowUp/Down rove across groups (they skip headers and separators); Home and End go to the absolute first and last entries.

**JSON documentation:** new `TmToolbarGroup.json` (`cssClasses: ["tm-toolbar-group"]`); `TmActionItem.json` gains `Group` and `GroupLabel`. **Localization:** none in core; demo keys `TmToolbarDemo_GroupTitle` already exist (`TmResources*.json:105`). **Docs:** `docs/toolbar.md` replaces the raw-wrapper example (`:72-81`) with `<TmToolbarGroup Label="Edit">`; the raw wrapper remains documented as "layout only".

### 4. RED tests
- `ActionMenuRunsTests` (pure):
  - `ConsecutiveSameKey_FormOneRun`;
  - `NonContiguousSameKey_FormTwoRuns_OrderPreserved` (pins F5: no reordering);
  - `EmptyKey_IsUngrouped`.
- `ActionOverflowMenuTests`:
  - `GroupedItems_RenderRoleGroup_NamedByLabel_WithAriaHiddenHeader`;
  - `Runs_AreSeparatedBy_RoleSeparator_NoLeadingOrTrailingSeparator`;
  - `UnlabelledRun_RendersNoGroupWrapper`;
  - `ReorderWhileOpen_ItemMovesAcrossGroups_DoesNotCrashTheRenderer` (H1 regression, pattern `:139-171`);
  - `FirstEnabledItemFocus_SkipsHeaders`: the captured reference is the first menuitem.
- `TmToolbarOverflowTests`:
  - `CollapsedMembersOfATmToolbarGroup_KeepTheGroupInMore`;
  - `RenamingAGroup_RelabelsItsMoreSection`.
- `TmToolbarGroupTests`:
  - `Unlabelled_HasNoRole`;
  - `Labelled_IsRoleGroupWithAriaLabel_AndDataTmToolbarGroup`.
- Node: `C3 rovingItems / enabledItems ignore role=separator and the aria-hidden header`.
- E2E: `C3_RibbonAt390_MoreShowsGroupSections_AndArrowsCrossThem` (sheet), plus an axe run on the open menu (`aria-required-children` clean).

### 5. Verification: as C1/C2, plus axe on the menu in popover and sheet.
### 6. Breaking / CHANGELOG: additive. `### Added`: "`TmToolbarGroup`, menu groups".
### 7. Effort: **M** (≈ 2 d)
### 8. Open questions
- **Q-C6 — group markup.**
  - (A) nested `role=group` + `aria-hidden` visible header + separators. **Recommended**: correct semantics, as in APG.
  - (B) flat list with separators only and no group names. Simpler for the renderer, but screen readers lose "Edit / View".
- **Q-C7 — where groups come from.**
  - (A) a `TmToolbarGroup` component (cascade, bUnit-testable). **Recommended.**
  - (B) JS reports `closest('[data-tm-toolbar-group]')` ids and labels with `OnFitChanged`. No new component, but the interop grows and markup must name groups.
### 9. Scope: **core follow-up plan**.

---

## C4 — [a39625e4] + [eddd747f] DE-1.2 and [d2eb082e] DiagramEditor adoption

### 1. Status: **OPEN**. The component plans contain the adoption, but **with stale text**.
- **DocumentEditor plan 146c7e1b**
  - **Fáze 4** `fze-4-kompaktn-ribbon-s-prioritnm-overflow-de-12-vzor-pro-f4-zkladu` (13 subtasks, none done) says "**DE jde první a je vzorem. Fáze nečeká na F4**". It builds a DE-local priority collapse in `toolbar-overflow.mjs` and keeps `TmDocumentToolbarOverflowMenu` with search.
  - **Fáze 8** `fze-8-tablet-a-mobil-…` subtask `bb72ac0d` "Migrace na F4" contains the real migration: `ToolbarOverflowLayout`, the core JS with a thin DE adapter, `TmDocumentToolbarOverflowMenu` → core More, and `ToolbarItemPriority` `[Obsolete]`.
  - **Stale text in Fáze 8:**
    - RED test `MoreMenu_IsTmDropdown_WithMobilePresentationAuto` and the bullet "jádrový „Více“ (`TmDropdown` s `MobilePresentation=Auto`)" contradict F4-TOOLBAR-OVERFLOW §2 and overlays.md:44-52. The More host is `ActionOverflowMenu`, reachable only through `TmToolbar`, and an editor/formatting toolbar must use `OverflowPresentation=Popover`.
    - "Tablet: inspektor jako jeden boční sheet" is a DE-owned shell, but it is inconsistent with F6-TABLET-DOCKED-PANELS if DE adopts `TmEditorShell`. Flag it to the DE plan owner.
- **DiagramEditor plan 94d441f2**
  - **Fáze 4** `fze-4-desktop-restrukturalizace-…-di2-di3` subtask `51221127` contains the adoption: "příkazový toolbar na `TmToolbar` s `Priority` u každého tlačítka a s menu „Více“ a „Zobrazení“". Its gates check only that F4/F6 are Completed, **not** C1–C3.
  - **Fáze 1** `data-tm-group` + `ToolbarPreset` is compatible: pass `data-tm-group` through `TmToolbarGroup.AdditionalAttributes`.
  - **Fáze 6** text "inspektor jako jediný boční sheet… overlay se zavíracím ×" **contradicts F6-TABLET-DOCKED-PANELS / F6-SHELL-PANEL-STATES** (tablet = docked, rail, no modal). It needs a plan-text fix like 08a22db5 #4.

### 2. Newly found core gaps (not in the inventory), all blocking DE adoption
- **C4-a — focus/selection preservation on toolbar buttons.**
  - Every DE command button uses `@onpointerdown:preventDefault="true" @onmousedown:preventDefault="true"` (e.g. `TmDocumentEditorToolbar.razor:407-408`, `:608-609`, `:836-837`) to keep the editor selection.
  - `TmToolbarButton` cannot express that: `AdditionalAttributes` cannot carry Blazor directive attributes.
  - Proposed (core, additive):
```csharp
/// <summary>
/// When true, pressing the button with a mouse, pen or finger does not move focus to it (the pointerdown and
/// mousedown defaults are prevented), so an editor's text selection survives a formatting command. Keyboard
/// activation and the "More" entry are unchanged. Default false.
/// </summary>
[Parameter] public bool PreserveFocus { get; set; }
```
    Markup at `TmToolbarButton.razor:19`: `@onpointerdown:preventDefault="PreserveFocus" @onmousedown:preventDefault="PreserveFocus"`.
  - RED test: `PreserveFocus_PreventsPointerAndMouseDownDefaults`. bUnit can check the presence of the `preventDefault` attribute in the render tree via `cut.Find("button").HasAttribute("blazor:onmousedown:preventDefault")` — confirm the attribute naming in the bUnit version (1.38.5) when implementing; fallback is an E2E check that focus stays in a contenteditable.
- **C4-b — More-menu search.** The DE menu has a filter when it holds ≥8 commands (`TmDocumentToolbarOverflowMenu.razor:34-50`, threshold at `TmDocumentEditorToolbar.razor:2258`). `ActionOverflowMenu` has none.

### 3. Recommendation
- **The core follow-up plan should contain only the prerequisites:** C1, C2, C3, C5, C6 and C4-a (plus C4-b if Q-C8 = B).
- **The adoption stays in the component plans**, with these plan-text corrections (Czech text for `plan_update_markdown`):
  - **DE 146c7e1b Fáze 4**: replace the first subtask's bullet "**DE jde první a je vzorem.** Fáze nečeká na F4…" with:
    > **Jádro F4 je hotové (faf503eb `fze-7-…`).** Fáze staví ribbon přímo na jádrovém `TmToolbar Overflow=Menu OverflowPresentation=Popover` (`TmToolbarGroup` = skupina ribbonu, `TmToolbarButton Priority/Pressed/PressedMixed/PreserveFocus`, `TmToolbarItem` pro select/colour/grid picker) — žádný DE-lokální kolaps v `toolbar-overflow.mjs`. Brána: fáze follow-up plánu jádra „Toolbar items + groups + pressed“ je Completed.

    Then delete the JS bullet "režim „collapse by priority“…" and the `toolbar-overflow.mjs` subtask (`314bed1a`); the migration happens here, not in Fáze 8.
  - **DE Fáze 8**:
    - change the bullet "`TmDocumentToolbarOverflowMenu` → jádrový „Více“ (`TmDropdown` s `MobilePresentation=Auto`)" to "→ jádrový „Více“ `TmToolbar` (`ActionOverflowMenu`) s `OverflowPresentation=Popover` (editor drží výběr; docs/overlays.md F4)";
    - rename the RED test `MoreMenu_IsTmDropdown_WithMobilePresentationAuto` to `MoreMenu_IsCoreToolbarOverflow_WithPopoverPresentation`.
  - **Diagram 94d441f2 Fáze 4**:
    - add the gate bullet "Brána: fáze follow-up plánu jádra „Toolbar items + groups + pressed“ je Completed (Pressed → menuitemcheckbox, `TmToolbarGroup`, `TmToolbarItem` pro `<select>` jednotky a `TmDropdown` Vložit/Zarovnání/Rozvržení)";
    - add to the `TmDiagramEditor.razor` bullet: "tool modes a přepínače panelů přes `Pressed` místo `Class="tm-toolbar-btn--active"`; `TmDropdown` v baru dostane `data-tm-toolbar-control` automaticky (C5); tablet mockup drží Vložit/Zarovnání/Rozvržení v baru → `Priority=Pinned` nebo `Primary` pro tyto tři".
  - **Diagram Fáze 6**: replace "inspektor jako jediný boční sheet … inspektor jako overlay se zavíracím ×" with "`TmEditorShell` tablet = dokované panely, max. 1 rozbalený (druhý jako 44px rail), žádný modal (F6-TABLET-DOCKED-PANELS); paleta jako rail přes `CollapsedPanels=Left` + `LeftRail`". Change RED `TabletMode_OpeningInspector_ClosesToolboxSheet` → `TabletMode_ExpandingInspector_RailsTheToolbox`.

### 4. RED tests: belong to the component plans (listed there). The core follow-up adds `TmToolbarButtonTests.PreserveFocus_*` and `ToolbarDocumentationTests` updates.
### 5. Verification: `plan_get_phase` re-read after `plan_update_markdown`.
### 6. Breaking: no (core). The DE/Diagram plans already carry their own CHANGELOG bullets.
### 7. Effort: core gaps **S** (C4-a) / **M** (C4-b); plan text **S**.
### 8. Open questions
- **Q-C8 — DE More search.**
  - (A) drop it; groups (C3) plus `tm-menu-nav` typeahead are enough. **Recommended.**
  - (B) core `TmToolbar.OverflowSearchThreshold` (int?, null = off) renders a filter input above the groups. `tm-menu-nav` already ignores keys typed in inputs. Cost M.
- **Q-C9 — DE ordering.**
  - (A) DE Fáze 4 adopts the core directly (no throw-away local collapse). **Recommended.**
  - (B) keep "DE first, migrate in Fáze 8" as written. This double-implements overflow.
### 9. Scope: **component plans** (DE Fáze 4/8, Diagram Fáze 4/6). Core prerequisites → **core follow-up plan**.

---

## C5 — [f4c27b20 #3] Wrapper margin / gap measurement in `tm-toolbar.js` `computeFit`

### 1. Status: **OPEN** (documented limitation)
- `tm-toolbar.js:149-155` adds only a wrapper's padding and border.
- `docs/toolbar.md:67-69`: "Its margin and its own `gap` are **not** measured".
- F4-TOOLBAR-OVERFLOW §3 says the same.

### 2. Current state
- `chooseVisibleCount` (`:38-50`) assumes the bar `gap` between **every** kept item.
- Real wrappers today:
  - DE `.tm-document-editor__ribbon-group` has `gap: var(--tm-space-1)` (4px) vs the bar's 8px (`_document-editor-toolbar.css:107-115`), plus `border-right` at `:738`;
  - Diagram `.tm-diagram-editor__toolbar-group` has gap 0, `border:1px` (`_diagram-editor.css:189-197`).
- A wrapper gap **smaller** than the bar's is safe: it over-reserves. A gap **larger** than the bar's, or any margin, over-commits: the I6 class of bug.

### 3. Proposed change (conservative — never over-commits, the I6 invariant)
In the `wrapper` branch of `computeFit` (`:149-155`), after the padding and border sum:
```js
// C5: a wrapper's own margins are fixed width; its own column gap, when WIDER than the bar gap, is reserved for
// every adjacency between its items as if all of them stayed (conservative: never over-commits; a narrower gap is
// already over-reserved by the bar gap). The start/actions containers share the bar gap and are skipped.
if (!classes?.contains('tm-toolbar-start') && !classes?.contains('tm-toolbar-actions')) {
    fixed += px(wrapperStyle?.marginLeft) + px(wrapperStyle?.marginRight);
    const innerGap = px(wrapperStyle?.columnGap) || px(wrapperStyle?.gap);
    const innerItems = typeof child.querySelectorAll === 'function'
        ? Array.from(child.querySelectorAll(':scope > [data-tm-toolbar-item]:not([data-pin="always"])')).length : 0;
    if (innerGap > gap && innerItems > 1) fixed += (innerGap - gap) * (innerItems - 1);
}
```
- `:scope >` counts only direct item children. Nested wrappers are handled by their own visit, because `walk` recurses.
- Update `docs/toolbar.md:67-69`: "margin and a gap wider than the bar's are measured (conservatively); a narrower gap is over-reserved".
- Not chosen: exact per-adjacency gap modelling, which needs `gapBefore` on every item and container-aware `chooseVisibleCount`. Cost M, little gain.

### 4. RED tests (node)
- `C5 a wrapper's margin-inline counts as fixed width`. Stub: wrapper `computed.marginLeft/Right = '10px'`; the same width that fitted before now collapses one.
- `C5 a wrapper gap wider than the bar gap is reserved per inner adjacency (never over-commits)`.
- `C5 a wrapper gap narrower than the bar gap changes nothing`.
- `C5 tm-toolbar-start/actions margins are not double-counted`.

The stub `wrapper()` helper at `:447-457` needs `marginLeft`, `marginRight` and `querySelectorAll` support.

### 5. Verification: `npm run test:overlay`; E2E `I6_RibbonNarrowedFrom1100_*` and `H2_*` stay green.
### 6. Breaking: no. CHANGELOG `### Fixed`.
### 7. Effort: **S**
### 8. Open question: none. Exact modelling is optional (Q-C10: recommend conservative).
### 9. Scope: **core follow-up plan**.

---

## C6 — [f4c27b20 #5] `TmDropdown` trigger auto-joins the roving set inside `TmToolbar`

### 1. Status: **OPEN**
- Only the doc hook exists (`docs/toolbar.md:154-156`; `ToolbarDocumentationTests.cs:44` asserts the attribute is documented).
- `grep data-tm-toolbar-control src/**/*.razor` returns no hits.

### 2. Current state
- `TmDropdown.razor:12-17` renders the trigger `<button type="button" @ref="_triggerRef" class="tm-dropdown-trigger" aria-expanded aria-haspopup @onclick>`. It has no cascade from `TmToolbar`.
- `AdditionalAttributes` lands on the wrapper `div` (`:11`), not the trigger, so a host cannot even add the hook by hand.
- `TmToolbar` cascades itself (`TmToolbar.razor:24`, `IsFixed`).

### 3. Proposed change
- `TmDropdown.razor`:
  - add `@using Tempo.Blazor.Components.Toolbar`;
  - in `@code`: `/// <summary>The toolbar the dropdown sits in, if any: its trigger then joins the toolbar's roving tab stop.</summary> [CascadingParameter] private TmToolbar? Toolbar { get; set; }`;
  - on the trigger: `data-tm-toolbar-control="@(Toolbar is not null ? "" : null)"`. Blazor renders an empty string as a valueless attribute, and JS tests with `[data-tm-toolbar-control]`.
- Same three lines in `TmSplitButton.razor` (its trigger button(s)) — Q-C11.
- The More menu is outside the toolbar's `CascadingValue` (`TmToolbar.razor:37-40`), so the internal menu never sees it.
- `docs/toolbar.md:154-156`: "a `TmDropdown` (and `TmSplitButton`) inside a `TmToolbar` joins the roving set automatically; mark other custom focusables yourself or wrap them in `TmToolbarItem`".

### 4. RED tests
- `TmDropdownTests`:
  - `Trigger_InsideTmToolbar_CarriesDataTmToolbarControl`;
  - `Trigger_Standalone_HasNoToolbarControlAttribute`;
  - `Items_InTheMenu_NeverCarryTheHook`.
- E2E `ToolbarOverflowE2ETests.C6_DropdownInToolbar_IsOneArrowStop_AndTabLeavesTheToolbarOnce`. Add a `TmDropdown` "Export" to the `toolbar-overflow-demo` toolbar; Tab once into the toolbar, ArrowRight reaches the trigger, Tab leaves.

### 5. Verification: `dotnet test --filter "FullyQualifiedName~TmDropdown|FullyQualifiedName~Toolbar"`; E2E `Keyboard_RovingTabindex_*` stays green.
### 6. Breaking: behaviour change. A dropdown in a toolbar is no longer its own Tab stop. CHANGELOG `### Changed`.
### 7. Effort: **S**
### 8. Open questions
- **Q-C11 — scope.**
  - (A) `TmDropdown` + `TmSplitButton`. **Recommended**: both are menu-button triggers.
  - (B) `TmDropdown` only.
  - (C) also `TmColorPicker`/`TmButton`. Prefer `TmToolbarItem` for those.
### 9. Scope: **core follow-up plan**.

---

## C7 — [96b27129] `TmSidePanel` scope-less fallback

### 1. Status: **OPEN**
`TmSidePanel.razor:167-169` `SheetMode => LayoutMode != Auto ? LayoutMode : Viewport?.Resolved ?? Context.Resolved`. With no viewport scope the **container** decides the sheet geometry. The test `TmSidePanelTests.cs:243-252` `Auto_WithoutAViewportScope_FallsBackToTheContainer` pins today's behaviour.

### 2. Current state
- `TmSidePanel.razor:51-65` opens `TmDrawer Modal="true" LayoutMode="@(LayoutMode)" Position="@SheetPosition"`.
- `TmDrawer.razor:14-19` already renders a `ViewportProbe` when `IsOpen && Modal && Viewport is null && LayoutMode == Auto`.
- The drawer's context comes from `OverlayLayout` (`TmDrawer.razor:247`, `Helpers/OverlayLayout.cs:17-23`). It reports changes through `ResolvedLayoutChanged` and **suppresses the first resolution** (`OverlayLayout.cs:32-38`).
- Today the probed mode only sets the drawer's `data-layout`. The **position** (side vs bottom) comes from TmSidePanel's container. Example: a narrow container on a desktop viewport opens a **bottom** sheet when it should open a right side sheet, and the reverse also happens.

### 3. Proposed change (reuses the drawer's probe; no second probe)
In `TmSidePanel.razor`:
```csharp
// C7: the viewport mode measured by the sheet's own drawer probe when no viewport scope exists.
private TmLayoutMode? _probedViewport;

private TmLayoutMode SheetMode => LayoutMode != TmLayoutMode.Auto
    ? LayoutMode
    : Viewport?.Resolved ?? _probedViewport ?? Context.Resolved;

private Task OnSheetLayoutResolved(TmLayoutMode mode)
{
    // Only the scope-less Auto case reads the drawer's measurement; a scope or a forced mode already decides.
    if (LayoutMode != TmLayoutMode.Auto || Viewport is not null || _probedViewport == mode) return Task.CompletedTask;
    _probedViewport = mode;
    return Task.CompletedTask; // EventCallback on this component re-renders it
}
```
On the `<TmDrawer>` (`:51-65`), add `InitialMode="@SheetMode"` and `ResolvedLayoutChanged="OnSheetLayoutResolved"`.

Why this works:
- The drawer's first (suppressed) resolution equals `SheetMode`, which is the current best guess: a previous probe or the container.
- A probe that **differs** is reported and flips `Position`.
- A probe that **agrees** needs no report.
- Re-opens after a rotation stay correct, because the stale guess becomes the drawer's `InitialMode` and any differing probe is reported.

Docs and decisions:
- `docs/editor-shell.md:206`: "With no viewport scope the container decides" → "With no viewport scope the sheet measures the viewport itself (the drawer's internal probe); the container is only the first-frame guess."
- Update the decision **F6-SIDE-PANEL-PRESENTATION** text the same way (`upsert_decision`).

### 4. RED tests (`tests/Tempo.Blazor.Tests/Layout/TmSidePanelTests.cs`; probe pattern from `Helpers/ViewportProbeTests.cs:250-266`)
- `Auto_NoViewportScope_NarrowContainer_ProbeMeasuresDesktop_OpensASideSheet`:
  - container Mobile, no scope, `Open`;
  - `cut.FindComponent<ViewportProbe>().Instance.OnLayoutModeChanged("desktop")`;
  - `.tm-drawer` has `tm-drawer--right`, not `--bottom`.
- `Sheet_NoViewportScope_DesktopContainer_ProbeMeasuresMobile_OpensABottomSheet` (`Presentation=Sheet`).
- `Auto_WithAViewportScope_IgnoresTheDrawerReport`.
- `ForcedMode_NeverReadsTheProbe`.
- The existing `Auto_WithoutAViewportScope_FallsBackToTheContainer` stays: it checks the first frame, before the probe.

### 5. Verification: `dotnet test --filter "FullyQualifiedName~TmSidePanel|FullyQualifiedName~ViewportProbe"`. Optional E2E: a page without a viewport scope at 1440, panel in a 500px column → side sheet.
### 6. Breaking: behaviour fix for scope-less hosts. CHANGELOG `### Fixed`.
### 7. Effort: **S**
### 8. Open questions: none.
### 9. Scope: **core follow-up plan**.

---

## C8 — [1645e0aa] `TmEditorShell` `MobileSheetSnapPoints` / peek snap — **product decision**

### 1. Status: **OPEN**
- `TmEditorShell.razor:188` `private static readonly double[] DefaultSnaps = [0.5, 1.0];` is hard-wired.
- `:109` `SnapPoints="DefaultSnaps"`.
- `:860` clamps to `DefaultSnaps.Length`.
- `:344` docs "(0 = half height, 1 = full)".

### 2. Current state
- The inline sheet height is `calc(var(--tm-sheet-height) * 100%)` of the **stage** (`_drawer.css:108-114`).
- The stage has a `min-block-size: 15rem` floor (`_editor-shell.css:181-190`).
- At 390×520 the half snap leaves a ~40px body under the sheet header and tabs (E2E `EditorShellE2ETests.cs:376-395` checks only containment).
- The canvas reserve is hard-coded to the half snap: `padding-block-end: calc(50cqh + …)` (`_editor-shell.css:199-201`).

### 3. Options
- **(A) Host parameter.** Add:
```csharp
/// <summary>
/// Fractions (0–1] of the shell's stage the mobile panels sheet snaps to, ascending; <see cref="MobileSheetSnapIndex"/>
/// indexes this list (0 = the lowest). Default <c>[0.5, 1.0]</c>. Invalid values are dropped and the list is sorted
/// (the TmDrawer rules); an empty result falls back to the default.
/// </summary>
[Parameter] public IReadOnlyList<double>? MobileSheetSnapPoints { get; set; }
```
  - Forward it to `TmDrawer.SnapPoints`.
  - Clamp `_snap` against the effective count at `:860`.
  - Set `--tm-editor-shell-sheet-reserve: <lowest snap>` inline on the stage and use `calc(var(--tm-editor-shell-sheet-reserve,0.5) * 100cqh + …)` at `:200`.
  - JSON doc entry required.
- **(B) Automatic floor (CSS only).** `.tm-editor-shell__sheet .tm-drawer__panel.tm-sheet__panel { min-block-size: min(100%, var(--tm-editor-shell-sheet-min-block, 14rem)); }`. The half snap then never shows less than ~14rem on short stages. Caveat: `tm-sheet.js` drag fractions are measured from rects, so it stays consistent.
- **(C) Peek snap.** A collapsed state that shows only the header and tabs (≈ 3.5rem) is a pixel-sized snap, not a fraction. It needs `TmDrawer` to support a px snap: core change in `tm-sheet.js` + `TmDrawer`. Cost M–L.

### 4. RED tests (for A+B)
- `TmEditorShellTests`:
  - `MobileSheetSnapPoints_AreForwardedToTheSheet` (`data-snap-points="0.3,0.6,1"`);
  - `MobileSheetSnapIndex_IndexesTheCustomList_AndClampsToItsLength`;
  - `LayoutFlip_ResetsToTheLowestCustomSnap`.
- E2E: `Demo_390x520_HalfSnap_KeepsAtLeast14remOfSheetBody`.
### 5. Verification: `EditorShellE2ETests` mobile set + `TmEditorShellTests`.
### 6. Breaking: additive (A); visual change on short stages (B). CHANGELOG `Added` / `Changed`.
### 7. Effort: A **S**, B **S**, C **M–L**
### 8. Open question
- **Q-C12.**
  - (A+B) parameter plus a 14rem body floor. **Recommended.**
  - (A) only.
  - (C) peek. Do it only if the e-mail mobile mockup shows a collapsed header-only sheet; the F6 email mapping uses snap 0 = "collapse on add block".
### 9. Scope: **core follow-up plan** (TmEditorShell is core). It is consumed by e-mail plan Fáze 8.

---

## C9 — [ed5296e9] 768 tablet: canvas < `MinCanvasWidth` — **product decision**

### 1. Status: **OPEN**
- `MinCanvasWidth` (`TmEditorShell.razor:328-329`, default 400) only bounds **user resizes**:
  - `ReachableMax` `:440-446`;
  - `EditorShellResize.Clamp`;
  - JS `clampWidth` `tm-editor-shell.js:29-32`.
- It never limits the **initial** layout. At 768 with the inspector expanded (320) plus a rail and toggles, the inventory measured the canvas at 278px.

### 2. Current state
- `.tm-editor-shell__panel { inline-size: var(--tm-editor-shell-panel-width); flex-shrink: 0 }` (`_editor-shell.css:45-51`).
- The rail is 44px (`:129-141`). Toggles are 24px, 44px under coarse pointer (`:60-73`, `:345-348`).
- The canvas width is already measured after render (`TmEditorShell.razor.cs:50-64`), but only while resizers are attached.

### 3. Options (all keep F6: docked, at most one expanded, no modal)
- **(A) Tablet fit width.** On tablet, the expanded panel renders at `min(width, width − (MinCanvasWidth − canvas))`, never below `MinLeftWidth` / `MinRightWidth`.
  - A C# overlay `_tabletFit[side]`: not persisted, not reported via `…WidthChanged` because it is not a user choice.
  - It is computed from the measured canvas in `SyncCanvasWidthAsync` and converges in one extra render: after the shrink the canvas equals `MinCanvasWidth`. When the container grows, it relaxes back towards the host width.
  - `aria-valuenow` reports the rendered width.
  - At 768 touch this gives ≈ 240–270px inspector and 400px canvas.
  - Cost M.
- **(B) CSS-only clamp.** `.tm-editor-shell[data-layout="tablet"] .tm-editor-shell__panel { max-inline-size: max(var(--tm-editor-shell-panel-min), calc(100cqi - var(--tm-editor-shell-min-canvas) - 2 * var(--tm-touch-target))); }` with the vars set inline from C#. The shell root is a `.tm-layout` inline-size container (`_layout-observer.css:13-18`). Cheap (S), but `aria-valuenow` would report the parameter, not the rendered width. That is an a11y mismatch, so not recommended.
- **(C) Rail both on narrow tablets.** Below `MinCanvasWidth + rail + panel` both panels start railed. Expanding one still shrinks the canvas. Conflicts with the F6 default "inspector expanded" in the e-mail mapping.
- **(D) Accept and document** (status quo).

### 4. RED tests (for A)
- `TmEditorShellResizerTests`:
  - `Tablet_ExpandedPanel_ShrinksToKeepMinCanvasWidth_ButNotBelowItsMin` (forced Tablet, `MeasureCanvasAsync` stubbed to 278 → the aside style width becomes 320 − 122 = 198 → clamped to 240);
  - `Tablet_FitWidth_IsNotPersisted_NorReported`;
  - `Desktop_IsUnaffected`.
- E2E `Demo_768_Touch_Tablet_CanvasKeepsMinCanvasWidth` (`EditorShellE2ETests.cs:553` neighbourhood).
### 5. Verification: `EditorShellE2ETests` tablet + resizer suites.
### 6. Breaking: visual (tablet inspector narrower at 768). CHANGELOG `Changed`.
### 7. Effort: A **M**, B **S**, C **S–M**
### 8. Open question
- **Q-C13.**
  - (A) fit width. **Recommended.**
  - (B) CSS clamp.
  - (C) rail both.
  - (D) accept.
### 9. Scope: **core follow-up plan**.

---

## C10 — [08a22db5 #4] Plan-text corrections, e-mail plan `da0a2531` Fáze 8

### 1. Status: **OPEN**
`plan_get_phase(da0a2531…, fze-8-responzivn-rozvren-editoru-desktop-tablet-mobil)` still holds the pre-F6 wording. Nothing has been applied (no `resultSummary`, 0/12 done).

### 2. Current text that is wrong (subtask ids)
- `9f6d5901` (TDD RED Tablet) says "nejvýš JEDEN otevřený boční sheet (`TmSidePanel`) — otevření vlastností zavře toolbox sheet … stav jako parametr `ToolboxCollapsed` + `ToolboxCollapsedChanged`". This contradicts F6-TABLET-DOCKED-PANELS (docked, no modal, no TmSidePanel) and F6-EDITOR-SHELL-EMAIL-API (no editor-specific shell params; the rail comes from `CollapsedPanels=Left` + `LeftRail`).
- `fc17b28c` (container/CSS) says "použij výhradně hodnoty jádrových `TmBreakpoints` (F1) … test shody s `TmBreakpoints`". This is wrong under `Breakpoints(768,1200)`: CSS literals 640/1024 would contradict the markup branch (F6-SHELL-BREAKPOINTS). CSS must key off `data-layout`.
- `93f732f1` (RED shell) has no `Breakpoints`.
- `75e00203` says "jádrový overflow `TmToolbar` … je-li Completed; jinak `TmDropdown` „⋮“". F4 is Completed.
- `e4069386` (gate) does not list F4.
- `64cb4951` (a11y) says "Escape zavře sheet" without scoping to mobile.
- `a579a2f0` (CHANGELOG) mentions `ToolboxCollapsed`.

### 3. Exact replacement text (for `plan_update_markdown`, Czech)
- **`e4069386`**: append to the list of gated phases: „, **„F4 — Overflow toolbar s prioritami a viditelné popisky tlačítek“ (`fze-7-f4-overflow-toolbar-s-prioritami-a-viditeln-popisky-tlatek`)**“.
- **`93f732f1`** — full replacement:
  > TDD RED (bUnit): `TmEmailTemplateEditor` renderuje `TmEditorShell` — sloty `Toolbar` (toolbar editoru z Fáze 7), `Left` (toolbox z Fáze 7A), `LeftRail` (ikonový pruh toolboxu pro sbalený panel), `Canvas` (plátno), `Right` (inspektor z Fáze 7A), `StatusBar` (stav uložení), `MobileActions` (Zpět / Znovu / Náhled / Validovat / Uložit); stávající `TmSplitter` 20/60/20 se odstraní. Editor předává shellu `Breakpoints="new TmLayoutBreakpoints(768, 1200)"` (desktop ≥ 1200, tablet 768–1199, mobil < 768; rozhodnutí F6-SHELL-BREAKPOINTS) a `PersistWidthsKey`. Parametry editoru `LayoutMode` (`TmLayoutMode` Auto/Desktop/Tablet/Mobile, default Auto), `MobilePanelPresentation` (`Sheet` default | `Tabs`) a `CollapsedPanels`/`CollapsedPanelsChanged` (two-way) se jen PŘEDÁVAJÍ shellu (test s vynuceným `LayoutMode` a ověřením parametrů shellu včetně `Breakpoints`); žádná vlastní logika breakpointů v editoru.
- **`9f6d5901`** — full replacement:
  > TDD RED (bUnit, vynucený `LayoutMode=Tablet`): tablet = TYTÉŽ dokované panely jako desktop, **nejvýš jeden ROZBALENÝ** (F6-TABLET-DOCKED-PANELS, F6-SHELL-PANEL-STATES) — žádný modal, žádný `TmSidePanel`, nic není inert. Výchozí stav: toolbox jako 44px rail (`CollapsedPanels="EditorShellPanel.Left"` + slot `LeftRail` s ikonami bloků), inspektor rozbalený; rozbalení Bloků zrailuje Vlastnosti a shell vyvolá `CollapsedPanelsChanged`. Stav sbalení drží hostitel přes `CollapsedPanels` (two-way, předané shellu) — žádný `ToolboxCollapsed`. Dlaždice toolboxu, ikony v `LeftRail` a akce plátna ≥ 44 px.
- **`fc17b28c`** — full replacement:
  > Rozhodování podle šířky KONTEJNERU (ne viewportu) přebírá shell (`TmLayoutObserver` + `Breakpoints(768, 1200)`). Email CSS NEPOUŽÍVÁ vlastní `@container`/`@media` šířkové literály (s vlastními prahy by 640/1024 v CSS odporovaly větvi markupu — F6-SHELL-BREAKPOINTS); pravidla závislá na rozvržení klíčuj na atribut `data-layout` kořene shellu (`.tm-editor-shell[data-layout="tablet"] …`, hodnoty `mobile|tablet|desktop`). Vnořený obsah, který potřebuje režim, čte kaskádovaný `TmLayoutContext` shellu — nikdy vlastní `TmLayoutObserver` (měřil by s výchozími prahy). Test: žádný `@container`/`@media (…width…)` v email CSS (source sweep).
- **`75e00203`**: replace the end of the sentence from „toolbar se zalamuje…“ with:
  > toolbar editoru je jádrový `TmToolbar Overflow="ToolbarOverflow.Menu"` (F4 je Completed) s `Priority` u každé akce (`Pinned` pro Uložit) — akce, které se nevejdou, jsou v menu „Více“ (`OverflowPresentation` Auto = sheet na telefonu; editor bez textového výběru v toolbaru). Na mobilu se slot `Toolbar` nerenderuje, akce jsou v `MobileActions`.
- **`64cb4951`**:
  > Přístupnost: pořadí fokusu, `aria-controls` přepínačů panelů/záložek (ze shellu); na MOBILU Escape zavře spodní sheet a fokus se vrátí jen když byl ztracen (`focusIfLost`); desktop a tablet nemají žádnou překryvnou vrstvu (nic k zavírání); 200% zoom bez ztráty funkcí, kontrast přes tokeny (žádné hardcoded barvy).
- **`a579a2f0`**:
  > CHANGELOG „Breaking / Migration“: `TmSplitter` 20/60/20 → `TmEditorShell` (změna DOM a CSS tříd kořene editoru, nové parametry `LayoutMode`/`MobilePanelPresentation`/`CollapsedPanels`) s ukázkou před/po; CSS hostitele klíčuj na `data-layout`.

### 4. Tests: none (plan text). Verify by re-reading `plan_get_phase`.
### 5. Verification: `plan_get_phase` after `plan_update_markdown` with `expectedSourceHash`.
### 6. Breaking: n/a
### 7. Effort: **S**
### 8. Open question
- **Q-C14.**
  - (A) the editor exposes `CollapsedPanels` pass-through (as in the text above). **Recommended**, per F6-EDITOR-SHELL-EMAIL-API.
  - (B) keep the convenience `ToolboxCollapsed` bool on the e-mail editor, mapped to the `Left` flag. Friendlier name, but a second source of truth.
### 9. Scope: **e-mail plan da0a2531 Fáze 8** (text only, via `plan_update_markdown`; the core plan can carry a one-line "apply text" task).

---

## C11 — [835acf83 #4] One-line tiles floored at two-line height — *optional / cosmetic*

### 1. Status: **OPEN** (by design, F5 Y4)
- `_mobile-action-bar.css:6-18` defines `--tm-mobile-action-bar-tile-block` as icon + gap + **2 lines** + padding.
- `:86-88` sets the tile `min-height: max(touch, tile-block)`.
- The reserve token in `tokens.css:277-283` uses the same terms (pinned by E2E `MobileActionBarE2ETests.cs:324-351` `Demo_320_FrenchFixedViewport_ReserveEqualsBarHeight_WhenLabelsWrap`).
- Result: bar 71px vs 61px with one-line labels.

### 3. Proposal (keep the floor only where it is needed: FixedViewport)
```css
/* C11: in-flow bars (sticky/inline) reserve nothing, so their tiles floor at ONE label line; a wrapping label grows
   the bar (align-items: stretch keeps every tile equal). FixedViewport keeps the two-line floor its reserve relies on. */
.tm-mobile-action-bar:not(.tm-mobile-action-bar--fixed) {
  --tm-mobile-action-bar-tile-block:
      calc(1rem + var(--tm-space-1) + (var(--tm-font-size-xs) * var(--tm-line-height-tight)) + var(--tm-space-1));
}
```
### 4. Tests
- E2E `Demo_390_StickyBar_OneLineLabels_IsOneLineTall` (bar height ≈ 61 ± 2);
- `Demo_320_French_StickyBar_WrapsAndAllTilesStayEqual`;
- the FixedViewport reserve test stays unchanged and green.
### 6. Breaking: visual (sticky bars get 10px shorter). CHANGELOG `Changed`.
### 7. Effort: **S**
### 8. Open question
- **Q-C15.**
  - (A) floor only FixedViewport. **Recommended.**
  - (B) keep as is.
### 9. Scope: core follow-up plan (polish phase) — optional.

## C12 — [835acf83 #6] Demo safe-area `viewport-fit=cover` — *optional / cosmetic*

### 1. Status: **OPEN**
The viewport meta in all three demos lacks `viewport-fit=cover`:
- `src/Tempo.Blazor.Demo/wwwroot/index.html:6`;
- `src/Tempo.Blazor.Demo.Server/Pages/_Host.cshtml:10`;
- `src/Tempo.Blazor.Demo.InteractiveAuto/Tempo.Blazor.Demo.InteractiveAuto/Components/App.razor:6`.

The docs already state the requirement (`docs/responsive-conventions.md:185`, `:226`; CHANGELOG `:300`).

### 3. Proposal
Change all three to `content="width=device-width, initial-scale=1.0, viewport-fit=cover"`.

Risk: any demo chrome fixed to the top edge must pad `env(safe-area-inset-top)`. The shared `.tm-safe-area-top` helper exists (`breakpoints.css:62`); audit `MainLayout` top bar before enabling.
### 4. Tests
- bUnit/source test `DemoHosts_OptIntoViewportFitCover` (reads the three files).
- Chromium cannot emulate a notch, so no runtime E2E.
### 7. Effort: **S**
### 8. Open question
- **Q-C16.**
  - (A) enable in all three demos and add top safe-area padding to the demo top bar. **Recommended.**
  - (B) leave it (docs only).
### 9. Scope: core follow-up plan (demo polish) — optional.

## C13 — [f1ebebef] Optional icon chip on action-bar tiles — *optional / cosmetic*

### 1. Status: **OPEN**
`.tm-mobile-action-bar__icon` (`_mobile-action-bar.css:116-121`) has no box and no background. The icon is 1rem, and that 1rem is part of both height formulas (`:10-13` and `tokens.css:280`).

### 3. Proposal (token-driven, no visual change by default, the reserve stays exact)
- `tokens.css` (and the same term in the reserve formula `:280`): replace the literal `1rem` with `var(--tm-mobile-action-bar-icon-box)`. Define `--tm-mobile-action-bar-icon-box: 1rem; --tm-mobile-action-bar-icon-chip-bg: transparent;` in `:root`. Dark mode needs no override (`--tm-color-primary-subtle` is themed, `tokens-dark.css:68`).
- `_mobile-action-bar.css:10-13`: same substitution.
- `.tm-mobile-action-bar__icon { inline-size: var(--tm-mobile-action-bar-icon-box); block-size: var(--tm-mobile-action-bar-icon-box); border-radius: var(--tm-radius-full); background: var(--tm-mobile-action-bar-icon-chip-bg); color: var(--tm-mobile-action-bar-icon-chip-fg, currentColor); }`
- A host (or a future default) enables the chip with `--tm-mobile-action-bar-icon-box: 1.75rem; --tm-mobile-action-bar-icon-chip-bg: var(--tm-color-primary-subtle); --tm-mobile-action-bar-icon-chip-fg: var(--tm-color-primary);`.
### 4. Tests
- E2E: with the chip tokens set on the demo, the FixedViewport reserve still equals the bar height (reuse `:324-351`).
- `audit-css-tokens` stays clean.
### 7. Effort: **S**
### 8. Open question
- **Q-C17.**
  - (A) tokens with the chip **off** by default. **Recommended.**
  - (B) chip on by default (dashboard mockup parity; the bar grows by ~12px).
### 9. Scope: core follow-up plan (polish phase) — optional.

---

## Summary table

| ID | Item | Status | Effort | Breaking | Scope |
|---|---|---|---|---|---|
| C1 | [bdab3cb6] `TmToolbarItem` (non-button collapsible item) + `IToolbarOverflowItem` + roving for item controls | OPEN | L | No (internal API) | Core follow-up |
| C2 | [b6c72292] `Pressed`/`PressedMixed` → `aria-pressed`; `TmActionItem.Checked`/`CheckedMixed` → `menuitemcheckbox`; sheet initial-focus selector; bar tile `aria-pressed` | PARTLY DONE (keyboard in `tm-menu-nav.js:18`) | M | No (visual Changed) | Core follow-up |
| C3 | [4c483f49] `TmToolbarGroup` + `TmActionItem.Group`/`GroupLabel` + `role=group`/`separator` in `ActionOverflowMenu` | OPEN | M | No | Core follow-up |
| C4 | [a39625e4][eddd747f][d2eb082e] DE/Diagram adoption (+ new gaps: `PreserveFocus`, menu search) | OPEN; adoption already in DE Fáze 4/8 and Diagram Fáze 4, but stale text | core S–M, plan text S | No | Component plans (+ text fixes); gaps → core |
| C5 | [f4c27b20 #3] wrapper margin + wider gap in `computeFit` (conservative) | OPEN | S | No | Core follow-up |
| C6 | [f4c27b20 #5] `TmDropdown`/`TmSplitButton` trigger auto `data-tm-toolbar-control` | OPEN | S | Behaviour (one fewer Tab stop) | Core follow-up |
| C7 | [96b27129] `TmSidePanel` scope-less → drawer's probe decides sheet position | OPEN | S | Fix | Core follow-up |
| C8 | [1645e0aa] `MobileSheetSnapPoints` + body floor (peek optional) | OPEN — product decision | S (+M peek) | Additive / visual | Core follow-up |
| C9 | [ed5296e9] 768 tablet canvas < `MinCanvasWidth` → tablet fit width | OPEN — product decision | M | Visual | Core follow-up |
| C10 | [08a22db5 #4] e-mail plan Fáze 8 text corrections (exact Czech text above) | OPEN | S | n/a | E-mail plan da0a2531 Fáze 8 |
| C11 | [835acf83 #4] one-line tile height (floor only FixedViewport) | OPEN, optional | S | Visual | Core polish (optional) |
| C12 | [835acf83 #6] demo `viewport-fit=cover` | OPEN, optional | S | No | Core polish (optional) |
| C13 | [f1ebebef] icon chip via tokens (off by default) | OPEN, optional | S | No | Core polish (optional) |

**Suggested phase cut for the core follow-up plan**
1. "Toolbar items, groups and pressed state": C1 + C2 + C3 + C4-a + C5 + C6. Optionally add C4-b.
2. "Shell and side-panel follow-ups": C7 + C8 + C9.
3. "Polish": C11–C13, optional.

Plus a coordination task: apply the C10 and C4 plan-text corrections with `plan_update_markdown`, and update decisions F5-ACTION-BAR-API (Checked shipped, Group shipped) and F6-SIDE-PANEL-PRESENTATION (scope-less probe).

**Open questions (each has a recommendation above)**
- Q-C1 menu shape (A)
- Q-C2 select/input stay own Tab stop (A)
- Q-C3 `bool?` + Mixed (A)
- Q-C4 radio later (A)
- Q-C5 checkable closes menu (A)
- Q-C6 nested group (A)
- Q-C7 `TmToolbarGroup` component (A)
- Q-C8 DE search drop (A)
- Q-C9 DE adopts core in Fáze 4 (A)
- Q-C10 conservative gap
- Q-C11 Dropdown + SplitButton (A)
- Q-C12 snap points + floor (A+B)
- Q-C13 tablet fit width (A)
- Q-C14 `CollapsedPanels` pass-through (A)
- Q-C15 floor only FixedViewport (A)
- Q-C16 enable `viewport-fit` (A)
- Q-C17 chip tokens off (A)
