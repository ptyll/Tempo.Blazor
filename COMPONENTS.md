# Tempo.Blazor - Dokumentace komponent

Kompletní přehled všech komponent knihovny Tempo.Blazor, jejich parametrů, příkladů použití a validace formulářů.

---

## Obsah

1. [Tlačítka (Buttons)](#tlačítka) — TmButton, TmSplitButton, TmCopyButton
2. [Textové vstupy (Inputs)](#textové-vstupy) — TmTextInput, TmTextArea, TmNumberInput, TmSearchInput, TmEntityPicker, TmExpressionEditor, TmPasswordStrengthIndicator
3. [Výběrové komponenty (Select, Dropdown)](#výběrové-komponenty) — TmSelect, TmMultiSelect, TmDropdown, TmFilterableDropdown
4. [Přepínače a checkboxy](#přepínače-a-checkboxy) — TmCheckbox, TmToggle, TmRadioGroup, TmRadio
5. [Datové zobrazení (Data Display)](#datové-zobrazení) — TmBadge, TmCard, TmAccordion, TmChip, TmChipGroup, TmChangeDiff, TmEmptyState, TmStatCard, TmKanbanBoard, TmMultiViewList
6. [Zpětná vazba (Feedback)](#zpětná-vazba) — TmAlert, TmModal, TmDialog, TmTooltip, TmSpinner, TmPopover, TmToastContainer, TmNotificationBell, TmProgressBar, TmSkeleton
7. [Navigace](#navigace) — TmTabs, TmTabPanel, TmContextMenu
8. [Ikony a avatary](#ikony-a-avatary) — TmIcon, TmAvatar, TmAvatarGroup
9. [Pickery (datum, čas)](#pickery) — TmDatePicker, TmDateRangePicker, TmDateTimePicker, TmDateTimeRangePicker, TmTimePicker, TmTimeInput, TmTimeRangePicker, TmCalendarView
10. [Formuláře a validace](#formuláře-a-validace) — TmFormField, TmValidatedField, TmValidationSummary, TmFormValidationMessage, TmInlineEdit, TmFormSection, TmFormRow, TmDynamicFormRenderer
11. [DataTable](#datatable) — TmDataTable, TmDataTableColumn, TmPagination, TmColumnPicker, TmViewManager, TmFilterBuilder, TmFilterChip, TmBulkActionBar
12. [Layout](#layout) — TmDrawer, TmSidebar, TmTopBar, TmBreadcrumbs, TmCommandPalette, TmSection, TmKeyboardShortcutsHelp
13. [Soubory a přílohy](#soubory-a-přílohy) — TmFileDropZone, TmAttachmentManager
14. [Galerie](#galerie) — TmImageGallery, TmLightbox
15. [Import/Export](#importexport) — TmExportOptions, TmImportWizard, TmImportPreview
16. [Grafy](#grafy) — TmChart
17. [Tagy](#tagy) — TmTagPicker
18. [Timeline](#timeline) — TmTimeline
19. [Toolbar](#toolbar) — TmToolbar, TmToolbarButton, TmToolbarDivider
20. [TreeView](#treeview) — TmTreeView
21. [Scheduler](#scheduler) — TmScheduler
22. [Dashboard](#dashboard) — TmDashboard
23. [Workflow](#workflow) — TmStepper, TmWorkflowDesignerCanvas, TmWorkflowPropertiesPanel, TmWorkflowToolbox, TmWorkflowMinimap
24. [Activity](#activity-komentáře-přílohy-rich-editor) — TmActivityLog, TmActivityComments, TmActivityAttachments, TmActivityTimeline, TmRichEditorFull, TmRichEditorSimple
25. [Validace formulářů - kompletní příklady](#validace-formulářů---kompletní-příklady)

---

## Tlačítka

### TmButton

Univerzální tlačítko s variantami, velikostmi a ikonami.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Type` | `ButtonType` | `Button` | HTML typ: `Button`, `Submit`, `Reset` |
| `Variant` | `ButtonVariant` | `Primary` | Vizuální styl: `Primary`, `Secondary`, `Ghost`, `Danger`, `Outline`, `Link`, `Default` |
| `Size` | `ButtonSize` | `Md` | Velikost: `Xs`, `Sm`, `Md`, `Lg` |
| `Icon` | `string?` | `null` | Název ikony (z `IconNames`) |
| `IconRight` | `bool` | `false` | Ikona vpravo od textu |
| `IsLoading` | `bool` | `false` | Zobrazí spinner a zakáže tlačítko |
| `Disabled` | `bool` | `false` | Zakáže tlačítko |
| `Block` | `bool` | `false` | Roztáhne na celou šířku |
| `TabIndex` | `int` | `0` | Tab pořadí |
| `AriaLabel` | `string?` | `null` | ARIA popis |
| `OnClick` | `EventCallback` | — | Handler kliknutí |
| `ChildContent` | `RenderFragment?` | `null` | Obsah tlačítka |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Základní tlačítko *@
<TmButton OnClick="HandleClick">Uložit</TmButton>

@* Všechny varianty *@
<TmButton Variant="ButtonVariant.Primary" OnClick="Save">Primární</TmButton>
<TmButton Variant="ButtonVariant.Secondary" OnClick="Cancel">Sekundární</TmButton>
<TmButton Variant="ButtonVariant.Ghost" OnClick="Cancel">Ghost</TmButton>
<TmButton Variant="ButtonVariant.Danger" OnClick="Delete">Smazat</TmButton>
<TmButton Variant="ButtonVariant.Outline" OnClick="Edit">Outline</TmButton>
<TmButton Variant="ButtonVariant.Link" OnClick="Navigate">Odkaz</TmButton>

@* Velikosti *@
<TmButton Size="ButtonSize.Xs">Extra malé</TmButton>
<TmButton Size="ButtonSize.Sm">Malé</TmButton>
<TmButton Size="ButtonSize.Md">Střední</TmButton>
<TmButton Size="ButtonSize.Lg">Velké</TmButton>

@* S ikonou *@
<TmButton Icon="@IconNames.Plus" OnClick="Add">Přidat</TmButton>
<TmButton Icon="@IconNames.Trash" IconRight="true" Variant="ButtonVariant.Danger">Smazat</TmButton>

@* Loading stav *@
<TmButton IsLoading="_isSaving" OnClick="Save">Uložit</TmButton>

@* Zakázané *@
<TmButton Disabled="true">Nelze kliknout</TmButton>

@* Block (celá šířka) *@
<TmButton Block="true" Variant="ButtonVariant.Primary">Celá šířka</TmButton>

@* Submit ve formuláři *@
<EditForm Model="_model" OnValidSubmit="Submit">
    <TmButton Type="ButtonType.Submit" Variant="ButtonVariant.Primary">Odeslat</TmButton>
    <TmButton Type="ButtonType.Reset" Variant="ButtonVariant.Ghost">Reset</TmButton>
</EditForm>

@* Ikona bez textu (icon-only) *@
<TmButton Icon="@IconNames.Edit" AriaLabel="Upravit" Variant="ButtonVariant.Ghost" />
```

### TmSplitButton

Tlačítko s rozbalovací nabídkou akcí.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Text` | `string` | — | Text primárního tlačítka (povinný) |
| `OnClick` | `EventCallback` | — | Klik na primární tlačítko |
| `Variant` | `ButtonVariant` | `Primary` | Vizuální styl |
| `Size` | `ButtonSize?` | `null` | Velikost |
| `Disabled` | `bool` | `false` | Zakázáno |
| `IsLoading` | `bool` | `false` | Loading stav |
| `ChildContent` | `RenderFragment` | — | Položky dropdown menu |

#### Příklady

```razor
<TmSplitButton Text="Uložit" OnClick="Save">
    <TmDropdownItem OnSelect="SaveAndClose">Uložit a zavřít</TmDropdownItem>
    <TmDropdownItem OnSelect="SaveAsNew">Uložit jako nový</TmDropdownItem>
    <TmDropdownItem OnSelect="SaveAsDraft">Uložit jako koncept</TmDropdownItem>
</TmSplitButton>

<TmSplitButton Text="Export" Variant="ButtonVariant.Secondary" OnClick="ExportDefault">
    <TmDropdownItem OnSelect="ExportCsv">Export CSV</TmDropdownItem>
    <TmDropdownItem OnSelect="ExportExcel">Export Excel</TmDropdownItem>
    <TmDropdownItem OnSelect="ExportPdf">Export PDF</TmDropdownItem>
</TmSplitButton>
```

### TmCopyButton

Tlačítko pro kopírování textu do schránky.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Text` | `string` | **povinný** | Text ke kopírování |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmCopyButton Text="https://example.com/api/token/abc123" />
<TmCopyButton Text="@_generatedCode" Class="ml-2" />
```

---

## Textové vstupy

### TmTextInput

Jednořádkový textový vstup s podporou ikon, validace a pomocného textu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | Aktuální hodnota |
| `ValueChanged` | `EventCallback<string>` | — | Událost změny hodnoty |
| `Label` | `string?` | `null` | Popisek nad vstupem |
| `Placeholder` | `string?` | `null` | Placeholder text |
| `Error` | `string?` | `null` | Chybová zpráva (červený styl) |
| `HelpText` | `string?` | `null` | Pomocný text pod vstupem |
| `LeftIcon` | `string?` | `null` | Ikona vlevo |
| `RightIcon` | `string?` | `null` | Ikona vpravo |
| `Type` | `string` | `"text"` | HTML typ: `text`, `email`, `password`, `url`, `tel` |
| `Id` | `string` | auto | HTML id |
| `Disabled` | `bool` | `false` | Zakázaný vstup |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |
| `Required` | `bool` | `false` | Povinné pole (hvězdička) |
| `TabIndex` | `int` | `0` | Tab pořadí |
| `AutoFocus` | `bool` | `false` | Automatický focus při renderování |
| `ShowValidationIcons` | `bool` | `false` | Zobrazit ikony validace (✓/✗) |
| `IsValid` | `bool?` | `null` | Stav validace: `true` = zelená, `false` = červená, `null` = neutrální |
| `Class` | `string?` | `null` | Další CSS třídy |
| `OnKeyDown` | `EventCallback<KeyboardEventArgs>` | — | Stisk klávesy |
| `OnKeyUp` | `EventCallback<KeyboardEventArgs>` | — | Uvolnění klávesy |
| `OnKeyPress` | `EventCallback<KeyboardEventArgs>` | — | Stisk znaku |
| `OnFocus` | `EventCallback<FocusEventArgs>` | — | Získání focusu |
| `OnBlur` | `EventCallback<FocusEventArgs>` | — | Ztráta focusu |

#### Příklady

```razor
@* Základní *@
<TmTextInput @bind-Value="_name" Label="Jméno" Placeholder="Zadejte jméno" />

@* S ikonami *@
<TmTextInput @bind-Value="_email" Label="Email" Type="email"
    LeftIcon="@IconNames.Mail" Placeholder="user@example.com" />

<TmTextInput @bind-Value="_search" Placeholder="Hledat..."
    LeftIcon="@IconNames.Search" RightIcon="@IconNames.X" />

@* S pomocným textem *@
<TmTextInput @bind-Value="_username" Label="Uživatelské jméno"
    HelpText="Minimálně 3 znaky, bez speciálních znaků" />

@* S chybou *@
<TmTextInput @bind-Value="_email" Label="Email"
    Error="@(_email.Length == 0 ? "Email je povinný" : null)" />

@* S validačními ikonami *@
<TmTextInput @bind-Value="_username" Label="Uživatel"
    ShowValidationIcons="true"
    IsValid="@(_username.Length >= 3 ? true : _username.Length == 0 ? null : false)"
    Error="@(_username.Length > 0 && _username.Length < 3 ? "Min. 3 znaky" : null)" />

@* Povinné, zakázané, readonly *@
<TmTextInput @bind-Value="_name" Label="Jméno" Required="true" />
<TmTextInput @bind-Value="_id" Label="ID" Disabled="true" />
<TmTextInput @bind-Value="_code" Label="Kód" ReadOnly="true" />

@* Password *@
<TmTextInput @bind-Value="_password" Label="Heslo" Type="password" />

@* S klávesovými událostmi *@
<TmTextInput @bind-Value="_search" Placeholder="Hledat (Enter)"
    OnKeyDown="@(e => { if (e.Key == "Enter") Search(); })" />

@* Autofocus *@
<TmTextInput @bind-Value="_first" Label="První pole" AutoFocus="true" />
```

### TmTextArea

Víceřádkové textové pole.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | Aktuální hodnota |
| `ValueChanged` | `EventCallback<string>` | — | Událost změny |
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Error` | `string?` | `null` | Chybová zpráva |
| `Rows` | `int` | `3` | Počet viditelných řádků |
| `MaxLength` | `int?` | `null` | Maximální počet znaků |
| `Disabled` | `bool` | `false` | Zakázáno |
| `TabIndex` | `int` | `0` | Tab pořadí |
| `Id` | `string` | auto | HTML id |

#### Příklady

```razor
@* Základní *@
<TmTextArea @bind-Value="_description" Label="Popis" Placeholder="Zadejte popis..." />

@* Více řádků *@
<TmTextArea @bind-Value="_notes" Label="Poznámky" Rows="6" />

@* S limitem znaků *@
<TmTextArea @bind-Value="_bio" Label="Bio" MaxLength="500"
    HelpText="@($"{_bio.Length}/500 znaků")" />

@* S chybou *@
<TmTextArea @bind-Value="_comment" Label="Komentář"
    Error="@(_comment.Length == 0 ? "Komentář je povinný" : null)" />
```

### TmNumberInput

Číselný vstup s tlačítky +/- a podporou min/max.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `int?` | `null` | Aktuální hodnota |
| `ValueChanged` | `EventCallback<int?>` | — | Událost změny |
| `Min` | `int?` | `null` | Minimální hodnota |
| `Max` | `int?` | `null` | Maximální hodnota |
| `Step` | `int` | `1` | Krok inkrementace |
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Error` | `string?` | `null` | Chybová zpráva |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Disabled` | `bool` | `false` | Zakázáno |
| `ShowButtons` | `bool` | `true` | Zobrazit +/- tlačítka |
| `Prefix` | `string?` | `null` | Prefix (např. "$") |
| `Suffix` | `string?` | `null` | Suffix (např. "Kč") |

#### Příklady

```razor
@* Základní *@
<TmNumberInput @bind-Value="_quantity" Label="Množství" />

@* S rozsahem *@
<TmNumberInput @bind-Value="_age" Label="Věk" Min="0" Max="120" />

@* S krokem *@
<TmNumberInput @bind-Value="_price" Label="Cena" Step="100" Min="0" Max="10000" />

@* S prefixem/suffixem *@
<TmNumberInput @bind-Value="_amount" Label="Částka" Prefix="$" />
<TmNumberInput @bind-Value="_days" Label="Počet dnů" Suffix="dnů" />

@* Bez tlačítek *@
<TmNumberInput @bind-Value="_code" Label="Kód" ShowButtons="false" Placeholder="Zadejte kód" />

@* S validací *@
<TmNumberInput @bind-Value="_count" Label="Počet"
    Error="@(_count is null ? "Povinné pole" : _count < 1 ? "Min. 1" : null)" />
```

### TmSearchInput

Vyhledávací vstup s debounce.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | Aktuální hledaný text |
| `ValueChanged` | `EventCallback<string>` | — | Událost změny (po debounce) |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Disabled` | `bool` | `false` | Zakázáno |
| `DebounceMs` | `int` | `300` | Zpoždění v ms |

#### Příklady

```razor
<TmSearchInput @bind-Value="_searchText" Placeholder="Hledat..." DebounceMs="500" />

<TmSearchInput @bind-Value="_filter" Placeholder="Filtrovat záznamy..." Disabled="_isLoading" />
```

### TmEntityPicker\<TItem, TValue\>

Picker pro výběr entity s asynchronním vyhledáváním.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `TValue` | — | Vybraná hodnota |
| `ValueChanged` | `EventCallback<TValue>` | — | Událost změny |
| `SearchProvider` | `Func<string, Task<IEnumerable<TItem>>>` | **povinný** | Asynchronní vyhledávání |
| `ValueSelector` | `Func<TItem, TValue>` | **povinný** | Extrakce hodnoty |
| `DisplaySelector` | `Func<TItem, string>` | **povinný** | Extrakce textu |
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder |
| `MinSearchLength` | `int` | `2` | Min. znaků pro spuštění hledání |
| `Debounce` | `int` | `300` | Debounce v ms |
| `Error` | `string?` | `null` | Chybová zpráva |
| `Disabled` | `bool` | `false` | Zakázáno |

#### Příklady

```razor
@* Výběr zákazníka *@
<TmEntityPicker TItem="Customer" TValue="int"
    @bind-Value="_customerId"
    SearchProvider="SearchCustomers"
    ValueSelector="@(c => c.Id)"
    DisplaySelector="@(c => $"{c.Name} ({c.Email})")"
    Label="Zákazník"
    Placeholder="Hledejte zákazníka..."
    MinSearchLength="2" />

@code {
    private async Task<IEnumerable<Customer>> SearchCustomers(string query)
        => await CustomerService.SearchAsync(query);
}

@* Výběr produktu *@
<TmEntityPicker TItem="Product" TValue="string"
    @bind-Value="_productCode"
    SearchProvider="SearchProducts"
    ValueSelector="@(p => p.Code)"
    DisplaySelector="@(p => p.Name)"
    Label="Produkt"
    Error="@(_productCode is null ? "Vyberte produkt" : null)" />
```

### TmExpressionEditor

Editor výrazů s proměnnými (pro dynamické šablony, podmínky apod.).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | Aktuální výraz |
| `ValueChanged` | `EventCallback<string>` | — | Událost změny |
| `Variables` | `IReadOnlyList<ExpressionVariable>` | `[]` | Dostupné proměnné |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Label` | `string?` | `null` | Popisek |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Error` | `string?` | `null` | Chybová zpráva |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmExpressionEditor @bind-Value="_template" Label="Šablona emailu"
    Variables="@(new List<ExpressionVariable>
    {
        new() { Name = "customer.name", Description = "Jméno zákazníka", Type = "string" },
        new() { Name = "order.total", Description = "Celková částka", Type = "number" },
        new() { Name = "order.date", Description = "Datum objednávky", Type = "date" },
    })"
    Placeholder="Zadejte šablonu s proměnnými..." />
```

### TmPasswordStrengthIndicator

Vizuální indikátor síly hesla.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Password` | `string` | `""` | Heslo k vyhodnocení |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmTextInput @bind-Value="_password" Label="Heslo" Type="password" />
<TmPasswordStrengthIndicator Password="@_password" />
```

---

## Výběrové komponenty

### TmSelect\<TValue\>

Rozbalovací výběr z možností.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `TValue?` | `null` | Vybraná hodnota |
| `ValueChanged` | `EventCallback<TValue?>` | — | Událost změny výběru |
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder text |
| `Error` | `string?` | `null` | Chybová zpráva |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Id` | `string` | auto | HTML id |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Options` | `IReadOnlyList<SelectOption<TValue>>?` | `null` | Seznam možností |
| `ChildContent` | `RenderFragment?` | `null` | Ruční `<option>` elementy |

#### Příklady

```razor
@* S Options parametrem *@
<TmSelect TValue="string" @bind-Value="_department" Label="Oddělení"
    Placeholder="Vyberte oddělení"
    Options="_departmentOptions" />

@code {
    private string? _department;
    private readonly List<SelectOption<string>> _departmentOptions =
    [
        SelectOption<string>.From("dev", "Vývoj"),
        SelectOption<string>.From("qa", "Testování"),
        SelectOption<string>.From("pm", "Projektový management"),
        SelectOption<string>.From("hr", "HR"),
    ];
}

@* S ručními option elementy *@
<TmSelect TValue="int" @bind-Value="_priority" Label="Priorita">
    <option value="1">Nízká</option>
    <option value="2">Střední</option>
    <option value="3">Vysoká</option>
    <option value="4">Kritická</option>
</TmSelect>

@* S ikonou v options *@
<TmSelect TValue="string" @bind-Value="_status" Label="Stav"
    Options="@(new List<SelectOption<string>>
    {
        new() { Value = "active", Label = "Aktivní", Icon = IconNames.Check },
        new() { Value = "inactive", Label = "Neaktivní", Icon = IconNames.X },
        new() { Value = "pending", Label = "Čekající", Icon = IconNames.Clock },
    })" />

@* S validací *@
<TmSelect TValue="string" @bind-Value="_category" Label="Kategorie" Required="true"
    Error="@(_category is null ? "Vyberte kategorii" : null)" />
```

### TmMultiSelect\<TItem, TValue\>

Pokročilý výběr více položek s vyhledáváním, chipsy, groupingem a server-side daty.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Values` | `IReadOnlyList<TValue>` | `[]` | Vybrané hodnoty |
| `ValuesChanged` | `EventCallback<IReadOnlyList<TValue>>` | — | Událost změny |
| `Items` | `IEnumerable<TItem>` | `[]` | Statické položky |
| `DataProvider` | `IDropdownDataProvider<TItem>?` | `null` | Server-side data provider |
| `DisplayField` | `Func<TItem, string>` | **povinný** | Extrakce textu z položky |
| `ValueField` | `Func<TItem, TValue>` | **povinný** | Extrakce hodnoty z položky |
| `IdField` | `Func<TItem, string>?` | `null` | Extrakce klíče |
| `GroupField` | `Func<TItem, string?>?` | `null` | Extrakce skupiny |
| `Mode` | `MultiSelectMode` | `Chip` | Režim zobrazení: `Chip`, `Delimiter`, `CheckBox` |
| `Delimiter` | `string` | `", "` | Oddělovač v Delimiter režimu |
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Error` | `string?` | `null` | Chybová zpráva |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Id` | `string` | auto | HTML id |
| `Class` | `string?` | `null` | Další CSS třídy |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Required` | `bool` | `false` | Povinné pole |
| `AllowFiltering` | `bool` | `true` | Povolit vyhledávání |
| `FilterPlaceholder` | `string?` | `null` | Placeholder filtru |
| `Debounce` | `int` | `300` | Debounce v ms |
| `MaxSelectionCount` | `int` | `0` | Max. počet vybraných (0 = neomezeno) |
| `ShowSelectAll` | `bool` | `false` | Zobrazit "Vybrat vše" |
| `HideSelectedItems` | `bool` | `false` | Skrýt vybrané z dropdownu |
| `ShowClearButton` | `bool` | `true` | Zobrazit tlačítko vymazání |
| `ShowCheckBox` | `bool` | `false` | Zobrazit zaškrtávací políčka |
| `ItemTemplate` | `RenderFragment<TItem>?` | `null` | Template pro položky v dropdownu |
| `ValueTemplate` | `RenderFragment<TItem>?` | `null` | Template pro vybraný chip |
| `NoRecordsTemplate` | `RenderFragment?` | `null` | Template pro prázdný výsledek |
| `HeaderTemplate` | `RenderFragment?` | `null` | Hlavička dropdownu |
| `FooterTemplate` | `RenderFragment?` | `null` | Patička dropdownu |
| `LoadingMessage` | `string?` | `null` | Zpráva při načítání |
| `EmptyMessage` | `string?` | `null` | Zpráva při žádných výsledcích |
| `OnOpen` | `EventCallback` | — | Otevření dropdownu |
| `OnClose` | `EventCallback` | — | Zavření dropdownu |
| `OnFiltering` | `EventCallback<string>` | — | Změna filtrovacího textu |

#### Příklady

```razor
@* Základní s chipy *@
<TmMultiSelect TItem="TagItem" TValue="int"
    @bind-Values="_selectedTags"
    Items="_tags"
    DisplayField="@(t => t.Name)"
    ValueField="@(t => t.Id)"
    Label="Štítky"
    Placeholder="Vyberte štítky" />

@* Delimiter mód *@
<TmMultiSelect TItem="string" TValue="string"
    @bind-Values="_selectedSkills"
    Items="_skills"
    DisplayField="@(s => s)"
    ValueField="@(s => s)"
    Mode="MultiSelectMode.Delimiter"
    Delimiter="; "
    Label="Dovednosti" />

@* CheckBox mód *@
<TmMultiSelect TItem="Permission" TValue="string"
    @bind-Values="_selectedPermissions"
    Items="_permissions"
    DisplayField="@(p => p.Name)"
    ValueField="@(p => p.Code)"
    Mode="MultiSelectMode.CheckBox"
    ShowCheckBox="true"
    ShowSelectAll="true"
    Label="Oprávnění" />

@* S groupingem *@
<TmMultiSelect TItem="Employee" TValue="int"
    @bind-Values="_selectedEmployees"
    Items="_employees"
    DisplayField="@(e => e.Name)"
    ValueField="@(e => e.Id)"
    GroupField="@(e => e.Department)"
    Label="Zaměstnanci" />

@* S custom šablonami *@
<TmMultiSelect TItem="User" TValue="int"
    @bind-Values="_selectedUsers"
    Items="_users"
    DisplayField="@(u => u.FullName)"
    ValueField="@(u => u.Id)"
    Label="Uživatelé">
    <ItemTemplate Context="user">
        <div class="flex items-center gap-2">
            <TmAvatar Name="@user.FullName" Size="AvatarSize.Xs" />
            <div>
                <div>@user.FullName</div>
                <div class="text-xs text-gray-500">@user.Email</div>
            </div>
        </div>
    </ItemTemplate>
    <ValueTemplate Context="user">
        <TmAvatar Name="@user.FullName" Size="AvatarSize.Xs" />
        <span>@user.FullName</span>
    </ValueTemplate>
</TmMultiSelect>

@* S limitem výběru *@
<TmMultiSelect TItem="string" TValue="string"
    @bind-Values="_selectedColors"
    Items="@(new[] { "Červená", "Modrá", "Zelená", "Žlutá", "Fialová" })"
    DisplayField="@(c => c)"
    ValueField="@(c => c)"
    MaxSelectionCount="3"
    Label="Barvy (max 3)" />

@* Server-side data *@
<TmMultiSelect TItem="Product" TValue="int"
    @bind-Values="_selectedProducts"
    DataProvider="_productDataProvider"
    DisplayField="@(p => p.Name)"
    ValueField="@(p => p.Id)"
    IdField="@(p => p.Id.ToString())"
    FilterPlaceholder="Hledat produkty..."
    Label="Produkty" />

@* S validací *@
<TmMultiSelect TItem="string" TValue="string"
    @bind-Values="_selectedItems"
    Items="_items"
    DisplayField="@(i => i)"
    ValueField="@(i => i)"
    Required="true"
    Error="@(_selectedItems.Count == 0 ? "Vyberte alespoň jednu položku" : null)"
    Label="Položky" />
```

### TmDropdown

Rozbalovací menu s položkami.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Text` | `string?` | `null` | Text trigger tlačítka |
| `Icon` | `string?` | `null` | Ikona trigger tlačítka |
| `OnSelect` | `EventCallback<string>` | — | Událost výběru (hodnota TmDropdownItem) |
| `ChildContent` | `RenderFragment` | — | TmDropdownItem děti |

#### Příklady

```razor
<TmDropdown Text="Akce" Icon="@IconNames.MoreVertical">
    <TmDropdownItem Value="edit" Icon="@IconNames.Edit">Upravit</TmDropdownItem>
    <TmDropdownItem Value="copy" Icon="@IconNames.Copy">Kopírovat</TmDropdownItem>
    <TmDropdownItem Value="delete" Icon="@IconNames.Trash">Smazat</TmDropdownItem>
</TmDropdown>

@code {
    private void HandleSelect(string value) { /* "edit", "copy", "delete" */ }
}
```

### TmDropdownItem

Položka v TmDropdown.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string?` | `null` | Hodnota položky |
| `Icon` | `string?` | `null` | Ikona |
| `ChildContent` | `RenderFragment` | — | Text položky |

### TmFilterableDropdown\<TItem, TValue\>

Dropdown s vyhledáváním a server-side daty.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Items` | `IEnumerable<TItem>?` | `null` | Statické položky |
| `DataProvider` | `IDropdownDataProvider<TItem>?` | `null` | Server-side provider |
| `Value` | `TValue` | — | Vybraná hodnota |
| `ValueChanged` | `EventCallback<TValue>` | — | Událost změny |
| `DisplayField` | `Func<TItem, string>` | **povinný** | Extrakce textu |
| `IdField` | `Func<TItem, TValue>` | **povinný** | Extrakce hodnoty |
| `Placeholder` | `string?` | `null` | Placeholder |
| `FilterPlaceholder` | `string?` | `null` | Placeholder filtru |
| `EmptyMessage` | `string?` | `null` | Zpráva při žádných výsledcích |
| `LoadingMessage` | `string?` | `null` | Zpráva při načítání |
| `ErrorMessage` | `string?` | `null` | Chybová zpráva |
| `ShowClearButton` | `bool` | `true` | Zobrazit tlačítko vymazání |
| `ItemTemplate` | `RenderFragment<TItem>?` | `null` | Custom šablona položky |

#### Příklady

```razor
@* Statické položky *@
<TmFilterableDropdown TItem="City" TValue="int"
    @bind-Value="_cityId"
    Items="_cities"
    DisplayField="@(c => c.Name)"
    IdField="@(c => c.Id)"
    Placeholder="Vyberte město"
    FilterPlaceholder="Hledat město..." />

@* Server-side *@
<TmFilterableDropdown TItem="Customer" TValue="int"
    @bind-Value="_customerId"
    DataProvider="_customerProvider"
    DisplayField="@(c => c.FullName)"
    IdField="@(c => c.Id)"
    Placeholder="Vyberte zákazníka">
    <ItemTemplate Context="c">
        <div class="flex items-center gap-2">
            <TmAvatar Name="@c.FullName" Size="AvatarSize.Xs" />
            <span>@c.FullName</span>
        </div>
    </ItemTemplate>
</TmFilterableDropdown>
```

---

## Přepínače a checkboxy

### TmCheckbox

Zaškrtávací políčko.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `bool` | `false` | Zaškrtnutý stav |
| `ValueChanged` | `EventCallback<bool>` | — | Událost změny |
| `Label` | `string?` | `null` | Text vedle checkboxu |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Indeterminate` | `bool` | `false` | Neurčitý stav (pomlčka) |

#### Příklady

```razor
@* Základní *@
<TmCheckbox @bind-Value="_agreed" Label="Souhlasím s podmínkami" />

@* S pomocným textem *@
<TmCheckbox @bind-Value="_newsletter" Label="Odebírat novinky"
    HelpText="Budeme vám posílat aktualizace jednou týdně" />

@* Indeterminate (select all) *@
<TmCheckbox Value="@_allSelected" Indeterminate="@_someSelected"
    ValueChanged="ToggleAll" Label="Vybrat vše" />

@* Zakázaný *@
<TmCheckbox Value="true" Disabled="true" Label="Aktivní (nelze změnit)" />

@* Více checkboxů *@
<TmCheckbox @bind-Value="_optionA" Label="Možnost A" />
<TmCheckbox @bind-Value="_optionB" Label="Možnost B" />
<TmCheckbox @bind-Value="_optionC" Label="Možnost C" />
```

### TmToggle

Přepínací spínač (switch).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `bool` | `false` | Zapnuto/vypnuto |
| `ValueChanged` | `EventCallback<bool>` | — | Událost změny |
| `Label` | `string?` | `null` | Popisek |
| `Disabled` | `bool` | `false` | Zakázáno |

#### Příklady

```razor
<TmToggle @bind-Value="_darkMode" Label="Tmavý režim" />
<TmToggle @bind-Value="_notifications" Label="Povolit notifikace" />
<TmToggle @bind-Value="_twoFactor" Label="Dvoufaktorové ověření" Disabled="_isLocked" />
```

### TmRadioGroup\<T\>

Skupina přepínacích tlačítek (radio buttons).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Name` | `string` | auto | Sdílené jméno skupiny |
| `Label` | `string?` | `null` | Popisek nad skupinou |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Error` | `string?` | `null` | Chybová zpráva |
| `Value` | `T?` | `null` | Aktuálně vybraná hodnota |
| `ValueChanged` | `EventCallback<T?>` | — | Událost změny |
| `Options` | `List<RadioOption<T>>` | `[]` | Seznam možností |
| `Disabled` | `bool` | `false` | Zakáže celou skupinu |
| `Layout` | `RadioLayout` | `Vertical` | Rozložení: `Vertical`, `Horizontal` |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Vertikální *@
<TmRadioGroup TValue="string" @bind-Value="_priority" Label="Priorita"
    Options="@(new List<RadioOption<string>>
    {
        new("Nízká", "low"),
        new("Střední", "medium"),
        new("Vysoká", "high"),
        new("Kritická", "critical"),
    })" />

@* Horizontální *@
<TmRadioGroup TValue="int" @bind-Value="_rating" Label="Hodnocení"
    Layout="RadioLayout.Horizontal"
    Options="@(new List<RadioOption<int>>
    {
        new("1 hvězda", 1),
        new("2 hvězdy", 2),
        new("3 hvězdy", 3),
        new("4 hvězdy", 4),
        new("5 hvězd", 5),
    })" />

@* S disabled položkou *@
<TmRadioGroup TValue="string" @bind-Value="_plan" Label="Tarif"
    Options="@(new List<RadioOption<string>>
    {
        new("Zdarma", "free"),
        new("Pro", "pro"),
        new("Enterprise", "enterprise") { IsDisabled = true },
    })" />

@* S validací *@
<TmRadioGroup TValue="string" @bind-Value="_gender" Label="Pohlaví"
    Error="@(_gender is null ? "Vyberte pohlaví" : null)"
    Options="@(new List<RadioOption<string>>
    {
        new("Muž", "male"),
        new("Žena", "female"),
        new("Jiné", "other"),
    })" />
```

### TmRadio

Jednotlivý radio button (interní, používá se v TmRadioGroup).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `object?` | `null` | Hodnota |
| `Name` | `string` | — | Sdílené jméno skupiny |
| `Label` | `string?` | `null` | Text |
| `IsChecked` | `bool` | `false` | Zaškrtnutý stav |
| `Disabled` | `bool` | `false` | Zakázáno |
| `OnSelect` | `EventCallback<object?>` | — | Událost výběru |

---

## Datové zobrazení

### TmBadge

Štítek/odznáček pro zobrazení statusu nebo počtu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Variant` | `BadgeVariant` | `Default` | Barva: `Default`, `Primary`, `Success`, `Danger`, `Warning`, `Info` |
| `Size` | `BadgeSize` | `Md` | Velikost: `Sm`, `Md` |
| `BadgeStyle` | `BadgeStyle` | `Filled` | Styl: `Filled`, `Outline`, `Subtle` |
| `Icon` | `string?` | `null` | Ikona |
| `Pill` | `bool` | `false` | Zaoblený tvar |
| `Dot` | `bool` | `false` | Malá tečka před textem |
| `ChildContent` | `RenderFragment?` | `null` | Obsah |

#### Příklady

```razor
@* Varianty *@
<TmBadge Variant="BadgeVariant.Default">Výchozí</TmBadge>
<TmBadge Variant="BadgeVariant.Primary">Primární</TmBadge>
<TmBadge Variant="BadgeVariant.Success">Úspěch</TmBadge>
<TmBadge Variant="BadgeVariant.Danger">Chyba</TmBadge>
<TmBadge Variant="BadgeVariant.Warning">Varování</TmBadge>
<TmBadge Variant="BadgeVariant.Info">Informace</TmBadge>

@* Styly *@
<TmBadge Variant="BadgeVariant.Success" BadgeStyle="BadgeStyle.Filled">Filled</TmBadge>
<TmBadge Variant="BadgeVariant.Success" BadgeStyle="BadgeStyle.Outline">Outline</TmBadge>
<TmBadge Variant="BadgeVariant.Success" BadgeStyle="BadgeStyle.Subtle">Subtle</TmBadge>

@* S ikonou *@
<TmBadge Variant="BadgeVariant.Success" Icon="@IconNames.Check">Aktivní</TmBadge>

@* Pill tvar *@
<TmBadge Variant="BadgeVariant.Primary" Pill="true">42</TmBadge>

@* Dot indikátor *@
<TmBadge Variant="BadgeVariant.Success" Dot="true">Online</TmBadge>
<TmBadge Variant="BadgeVariant.Danger" Dot="true">Offline</TmBadge>
```

### TmCard

Karta pro seskupení obsahu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Variant` | `CardVariant` | `Default` | Styl: `Default`, `Elevated`, `Outlined` |
| `Header` | `string?` | `null` | Text záhlaví |
| `HeaderIcon` | `string?` | `null` | Ikona v záhlaví |
| `ChildContent` | `RenderFragment?` | `null` | Obsah karty |
| `Footer` | `string?` | `null` | Text patičky |
| `FooterContent` | `RenderFragment?` | `null` | RenderFragment patičky (má přednost) |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Základní *@
<TmCard Header="Přehled">
    <p>Obsah karty</p>
</TmCard>

@* S ikonou a patičkou *@
<TmCard Header="Uživatelé" HeaderIcon="@IconNames.Users" Footer="Celkem: 42">
    <p>Seznam uživatelů...</p>
</TmCard>

@* Elevated *@
<TmCard Variant="CardVariant.Elevated" Header="Statistiky">
    <TmStatCard Title="Objednávky" Value="1,234" />
</TmCard>

@* Outlined *@
<TmCard Variant="CardVariant.Outlined" Header="Nastavení">
    <TmToggle @bind-Value="_darkMode" Label="Tmavý režim" />
</TmCard>

@* S custom patičkou *@
<TmCard Header="Formulář">
    <TmTextInput @bind-Value="_name" Label="Jméno" />
    <FooterContent>
        <div class="flex justify-end gap-2">
            <TmButton Variant="ButtonVariant.Ghost">Zrušit</TmButton>
            <TmButton Variant="ButtonVariant.Primary">Uložit</TmButton>
        </div>
    </FooterContent>
</TmCard>
```

### TmAccordion + TmAccordionItem

Akordeon pro sbalitelné sekce.

#### Parametry TmAccordion

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Multiple` | `bool` | `false` | Povolit více otevřených sekcí |
| `ChildContent` | `RenderFragment?` | `null` | TmAccordionItem děti |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Parametry TmAccordionItem

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Id` | `string` | **povinný** | Unikátní ID |
| `Title` | `string` | **povinný** | Nadpis |
| `ChildContent` | `RenderFragment` | — | Obsah |

#### Příklady

```razor
@* Základní (jen jeden otevřený) *@
<TmAccordion>
    <TmAccordionItem Id="faq1" Title="Co je Tempo.Blazor?">
        Knihovna UI komponent pro Blazor.
    </TmAccordionItem>
    <TmAccordionItem Id="faq2" Title="Jak nainstalovat?">
        Přidejte NuGet balíček Tempo.Blazor.
    </TmAccordionItem>
    <TmAccordionItem Id="faq3" Title="Je zdarma?">
        Ano, pro interní použití.
    </TmAccordionItem>
</TmAccordion>

@* Více otevřených *@
<TmAccordion Multiple="true">
    <TmAccordionItem Id="s1" Title="Osobní údaje">
        <TmTextInput @bind-Value="_name" Label="Jméno" />
    </TmAccordionItem>
    <TmAccordionItem Id="s2" Title="Kontakt">
        <TmTextInput @bind-Value="_email" Label="Email" />
    </TmAccordionItem>
</TmAccordion>
```

### TmChip

Chip/tag element s variantami.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Label` | `string` | **povinný** | Text chipu |
| `Color` | `string?` | `null` | Barva (CSS) |
| `Variant` | `ChipVariant` | `Soft` | Styl: `Soft`, `Filled`, `Outlined` |
| `Size` | `ChipSize` | `Md` | Velikost: `Sm`, `Md` |
| `Icon` | `string?` | `null` | Ikona |
| `Removable` | `bool` | `false` | Zobrazit X pro odebrání |
| `OnRemove` | `EventCallback` | — | Událost odebrání |
| `Clickable` | `bool` | `false` | Klikatelný |
| `OnClick` | `EventCallback` | — | Událost kliknutí |
| `Selected` | `bool` | `false` | Vybraný stav |

#### Příklady

```razor
<TmChip Label="Blazor" Color="#512BD4" />
<TmChip Label="C#" Variant="ChipVariant.Filled" Icon="@IconNames.Code" />
<TmChip Label="Nový" Variant="ChipVariant.Outlined" Removable="true" OnRemove="RemoveTag" />
<TmChip Label="Filtr" Clickable="true" Selected="_isSelected" OnClick="ToggleFilter" />
```

### TmChipGroup

Kontejner pro skupinu chipů.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `ChildContent` | `RenderFragment` | — | TmChip děti |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmChipGroup>
    @foreach (var tag in _tags)
    {
        <TmChip Label="@tag" Removable="true" OnRemove="() => RemoveTag(tag)" />
    }
</TmChipGroup>
```

### TmChangeDiff

Zobrazení změn (staré/nové hodnoty).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Changes` | `IEnumerable<TmChangeInfo>` | `[]` | Seznam změn |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmChangeDiff Changes="@(new[]
{
    new TmChangeInfo("Jméno", "Jan", "Jan Novák"),
    new TmChangeInfo("Email", "jan@old.cz", "jan@new.cz"),
    new TmChangeInfo("Oddělení", null, "Vývoj"),
})" />
```

### TmEmptyState

Prázdný stav se zprávou a volitelnou akcí.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis |
| `Description` | `string?` | `null` | Popis |
| `Icon` | `string?` | `null` | Ikona |
| `ActionText` | `string?` | `null` | Text akčního tlačítka |
| `OnAction` | `EventCallback` | — | Událost kliknutí na akci |

#### Příklady

```razor
<TmEmptyState Title="Žádné záznamy"
    Description="Zatím nemáte žádné záznamy. Vytvořte první."
    Icon="@IconNames.Inbox"
    ActionText="Vytvořit záznam"
    OnAction="CreateNew" />

<TmEmptyState Title="Žádné výsledky"
    Description="Pro tento filtr nebyly nalezeny žádné výsledky."
    Icon="@IconNames.Search" />
```

### TmStatCard

Karta se statistikou (KPI).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis/popisek |
| `Label` | `string?` | `null` | Alias pro Title |
| `Value` | `string?` | `null` | Hlavní hodnota |
| `SubValue` | `string?` | `null` | Vedlejší hodnota (trend) |
| `SubValueColor` | `string?` | `null` | Barva trendu |

#### Příklady

```razor
<TmStatCard Title="Celkové tržby" Value="1 234 567 Kč"
    SubValue="+12.5%" SubValueColor="green" />

<TmStatCard Title="Aktivní uživatelé" Value="42"
    SubValue="-3 oproti včera" SubValueColor="red" />

<TmStatCard Label="Průměrný čas" Value="4.2 min" />
```

### TmKanbanBoard\<TItem\>

Kanban board s drag & drop.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Columns` | `List<KanbanColumn>` | **povinný** | Definice sloupců |
| `Items` | `IEnumerable<TItem>` | **povinný** | Položky |
| `ColumnSelector` | `Func<TItem, string>` | **povinný** | Funkce pro přiřazení sloupce |
| `CardTemplate` | `RenderFragment<TItem>` | **povinný** | Šablona karty |
| `OnItemMoved` | `EventCallback<KanbanItemMovedEventArgs<TItem>>` | — | Přesunutí položky |
| `OnCardClick` | `EventCallback<TItem>` | — | Klik na kartu |
| `ColumnHeaderTemplate` | `RenderFragment<KanbanColumn>?` | `null` | Custom hlavička sloupce |
| `EmptyColumnTemplate` | `RenderFragment<KanbanColumn>?` | `null` | Prázdný sloupec |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmKanbanBoard TItem="TaskItem"
    Columns="@(new List<KanbanColumn>
    {
        new("todo", "K vyřízení", Color: "#3b82f6"),
        new("in-progress", "Rozpracované", Color: "#f59e0b", MaxItems: 5),
        new("review", "Ke schválení", Color: "#8b5cf6"),
        new("done", "Hotovo", Color: "#22c55e"),
    })"
    Items="_tasks"
    ColumnSelector="@(t => t.Status)"
    OnItemMoved="HandleMoved"
    OnCardClick="OpenTask">
    <CardTemplate Context="task">
        <div>
            <strong>@task.Title</strong>
            <p class="text-sm text-gray-500">@task.Assignee</p>
            <TmBadge Variant="BadgeVariant.Info" Size="BadgeSize.Sm">@task.Priority</TmBadge>
        </div>
    </CardTemplate>
</TmKanbanBoard>
```

### TmMultiViewList\<TItem\>

Seznam s přepínáním zobrazení (tabulka/karty/seznam) a filtry.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Items` | `IEnumerable<TItem>?` | `null` | In-memory data |
| `DataProvider` | `IDataTableDataProvider<TItem>?` | `null` | Server-side provider |
| `ViewContext` | `string` | **povinný** | Unikátní ID view kontextu |
| `ViewMode` | `ViewMode` | `Table` | Aktuální režim: `Table`, `Card`, `List` |
| `ViewModeChanged` | `EventCallback<ViewMode>` | — | Změna režimu |
| `ShowViewSwitcher` | `bool` | `true` | Přepínač zobrazení |
| `ShowSearch` | `bool` | `true` | Vyhledávání |
| `ShowPagination` | `bool` | `true` | Stránkování |
| `DefaultPageSize` | `int` | `25` | Výchozí stránka |
| `TitleField` | `Func<TItem, string>?` | `null` | Hlavní text |
| `SubTitleField` | `Func<TItem, string>?` | `null` | Podtitulek |
| `AvatarUrlField` | `Func<TItem, string>?` | `null` | URL avataru |
| `StatusLabelField` | `Func<TItem, string>?` | `null` | Text statusu |
| `StatusColorField` | `Func<TItem, string>?` | `null` | Barva statusu |
| `DateField` | `Func<TItem, DateTime?>?` | `null` | Datum |
| `IdField` | `Func<TItem, string>?` | `null` | ID položky |
| `CardTemplate` | `RenderFragment<TItem>?` | `null` | Šablona karty |
| `ListItemTemplate` | `RenderFragment<TItem>?` | `null` | Šablona seznamu |
| `TableRowTemplate` | `RenderFragment<TItem>?` | `null` | Šablona řádku tabulky |
| `OnItemClick` | `EventCallback<TItem>` | — | Klik na položku |
| `EmptyTitle` | `string?` | `null` | Prázdný stav |
| `ScrollMode` | `DataTableScrollMode` | `Pagination` | Scrollovací režim |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmMultiViewList TItem="Contact" Items="_contacts" ViewContext="contacts-list"
    TitleField="@(c => c.FullName)"
    SubTitleField="@(c => c.Email)"
    StatusLabelField="@(c => c.IsActive ? "Aktivní" : "Neaktivní")"
    StatusColorField="@(c => c.IsActive ? "green" : "gray")"
    DateField="@(c => c.CreatedAt)"
    OnItemClick="OpenContact">
    <CardTemplate Context="contact">
        <div class="p-4">
            <TmAvatar Name="@contact.FullName" Size="AvatarSize.Md" />
            <h4>@contact.FullName</h4>
            <p>@contact.Email</p>
        </div>
    </CardTemplate>
</TmMultiViewList>
```

---

## Zpětná vazba

### TmAlert

Notifikační zpráva s různými úrovněmi závažnosti.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Severity` | `AlertSeverity` | `Info` | Úroveň: `Info`, `Success`, `Warning`, `Error` |
| `Variant` | `AlertVariant` | `Soft` | Styl: `Soft`, `Filled`, `Outlined` |
| `Title` | `string?` | `null` | Nadpis |
| `ChildContent` | `RenderFragment?` | `null` | Popis |
| `Dismissable` | `bool` | `false` | Zavírací tlačítko |
| `OnDismiss` | `EventCallback` | — | Událost zavření |
| `Icon` | `string?` | `null` | Vlastní ikona |
| `Actions` | `RenderFragment?` | `null` | Akční tlačítka |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Všechny severity *@
<TmAlert Severity="AlertSeverity.Info" Title="Informace">
    Nová verze je k dispozici.
</TmAlert>

<TmAlert Severity="AlertSeverity.Success" Title="Úspěch">
    Záznam byl úspěšně uložen.
</TmAlert>

<TmAlert Severity="AlertSeverity.Warning" Title="Varování">
    Platnost licence vyprší za 7 dní.
</TmAlert>

<TmAlert Severity="AlertSeverity.Error" Title="Chyba">
    Nepodařilo se uložit záznam.
</TmAlert>

@* Varianty *@
<TmAlert Severity="AlertSeverity.Info" Variant="AlertVariant.Soft">Soft</TmAlert>
<TmAlert Severity="AlertSeverity.Info" Variant="AlertVariant.Filled">Filled</TmAlert>
<TmAlert Severity="AlertSeverity.Info" Variant="AlertVariant.Outlined">Outlined</TmAlert>

@* Zavíratelný *@
<TmAlert Severity="AlertSeverity.Warning" Dismissable="true" OnDismiss="() => _showAlert = false">
    Toto upozornění můžete zavřít.
</TmAlert>

@* S akcemi *@
<TmAlert Severity="AlertSeverity.Error" Title="Chyba připojení">
    <ChildContent>Nelze se připojit k serveru.</ChildContent>
    <Actions>
        <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Outline" OnClick="Retry">
            Zkusit znovu
        </TmButton>
    </Actions>
</TmAlert>
```

### TmModal

Modální okno.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Show` | `bool` | `false` | Zobrazit modal |
| `Title` | `string` | `""` | Nadpis |
| `Subtitle` | `string?` | `null` | Podnadpis |
| `Icon` | `string?` | `null` | Ikona v záhlaví |
| `ChildContent` | `RenderFragment?` | `null` | Obsah těla |
| `Footer` | `RenderFragment?` | `null` | Vlastní patička |
| `Size` | `ModalSize` | `Medium` | Velikost: `Small`, `Medium`, `Large`, `XLarge`, `Fullscreen` |
| `Position` | `ModalPosition` | `Center` | Pozice: `Center`, `Top`, `Bottom` |
| `Animated` | `bool` | `true` | Animace otevření |
| `ShowCloseButton` | `bool` | `true` | Zobrazit X tlačítko |
| `ShowFooter` | `bool` | `true` | Zobrazit patičku |
| `ShowDefaultFooterButtons` | `bool` | `false` | Zobrazit výchozí tlačítka OK/Cancel |
| `ShowOkButton` | `bool` | `true` | Zobrazit OK |
| `ShowCancelButton` | `bool` | `true` | Zobrazit Cancel |
| `OkButtonText` | `string` | `"OK"` | Text OK tlačítka |
| `CancelButtonText` | `string` | `"Cancel"` | Text Cancel tlačítka |
| `OkButtonVariant` | `ButtonVariant` | `Primary` | Varianta OK tlačítka |
| `OkButtonDisabled` | `bool` | `false` | Zakázat OK tlačítko |
| `CloseOnOverlayClick` | `bool` | `true` | Zavřít klikem na overlay |
| `CloseOnEscape` | `bool` | `true` | Zavřít klávesou Escape |
| `OnClose` | `EventCallback` | — | Událost zavření |
| `OnOk` | `EventCallback` | — | Událost OK |
| `OnOpen` | `EventCallback` | — | Událost otevření |

#### Příklady

```razor
@* Základní *@
<TmButton OnClick="() => _showModal = true">Otevřít modal</TmButton>

<TmModal Show="_showModal" Title="Nový záznam" OnClose="() => _showModal = false">
    <TmTextInput @bind-Value="_name" Label="Název" />
    <Footer>
        <TmButton Variant="ButtonVariant.Ghost" OnClick="() => _showModal = false">Zrušit</TmButton>
        <TmButton Variant="ButtonVariant.Primary" OnClick="Save">Uložit</TmButton>
    </Footer>
</TmModal>

@* Různé velikosti *@
<TmModal Show="_show" Title="Malý" Size="ModalSize.Small" OnClose="Close">
    <p>Malý modal</p>
</TmModal>

<TmModal Show="_show" Title="Velký" Size="ModalSize.Large" OnClose="Close">
    <p>Velký modal</p>
</TmModal>

<TmModal Show="_show" Title="Fullscreen" Size="ModalSize.Fullscreen" OnClose="Close">
    <p>Fullscreen modal</p>
</TmModal>

@* S výchozími tlačítky *@
<TmModal Show="_show" Title="Potvrzení"
    ShowDefaultFooterButtons="true"
    OkButtonText="Potvrdit"
    CancelButtonText="Zrušit"
    OkButtonVariant="ButtonVariant.Danger"
    OnOk="Confirm"
    OnClose="() => _show = false">
    <p>Opravdu chcete smazat tento záznam?</p>
</TmModal>

@* Modal nahoře, bez overlay kliknutí *@
<TmModal Show="_show" Title="Upozornění" Position="ModalPosition.Top"
    CloseOnOverlayClick="false" CloseOnEscape="false"
    OnClose="Close">
    <p>Důležité upozornění...</p>
</TmModal>
```

### TmDialog

Zjednodušený dialog (alert, confirm, prompt).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Show` | `bool` | `false` | Zobrazit dialog |
| `Type` | `DialogType` | `Alert` | Typ: `Alert`, `Confirm`, `Prompt`, `Custom` |
| `Variant` | `DialogVariant` | `Info` | Barva: `Info`, `Success`, `Warning`, `Error` |
| `Title` | `string` | `""` | Nadpis |
| `Message` | `string` | `""` | Zpráva |
| `DefaultValue` | `string?` | `null` | Výchozí hodnota pro Prompt |
| `PromptPlaceholder` | `string` | `"Enter value..."` | Placeholder pro Prompt |
| `IsDangerous` | `bool` | `false` | Nebezpečná akce (červené OK) |
| `OkButtonText` | `string` | `"OK"` | Text OK |
| `CancelButtonText` | `string` | `"Cancel"` | Text Cancel |
| `ChildContent` | `RenderFragment?` | `null` | Custom obsah |
| `FooterContent` | `RenderFragment?` | `null` | Custom patička |
| `OnResult` | `EventCallback<bool?>` | — | Výsledek Alert/Confirm |
| `OnPromptResult` | `EventCallback<string?>` | — | Výsledek Prompt |

#### Příklady

```razor
@* Alert *@
<TmDialog Show="_showAlert" Type="DialogType.Alert"
    Variant="DialogVariant.Info"
    Title="Informace"
    Message="Operace proběhla úspěšně."
    OnResult="r => _showAlert = false" />

@* Confirm *@
<TmDialog Show="_showConfirm" Type="DialogType.Confirm"
    Variant="DialogVariant.Warning"
    Title="Smazat záznam?"
    Message="Tato akce je nevratná."
    IsDangerous="true"
    OkButtonText="Smazat"
    OnResult="HandleDeleteConfirm" />

@code {
    private void HandleDeleteConfirm(bool? result)
    {
        _showConfirm = false;
        if (result == true) DeleteRecord();
    }
}

@* Prompt *@
<TmDialog Show="_showPrompt" Type="DialogType.Prompt"
    Title="Přejmenovat"
    Message="Zadejte nový název:"
    DefaultValue="@_currentName"
    PromptPlaceholder="Název..."
    OnPromptResult="HandleRename" />

@code {
    private void HandleRename(string? newName)
    {
        _showPrompt = false;
        if (newName is not null) _currentName = newName;
    }
}

@* Custom *@
<TmDialog Show="_showCustom" Type="DialogType.Custom" Title="Vlastní dialog">
    <ChildContent>
        <TmTextInput @bind-Value="_field1" Label="Pole 1" />
        <TmSelect TValue="string" @bind-Value="_field2" Label="Pole 2"
            Options="_options" />
    </ChildContent>
    <FooterContent>
        <TmButton Variant="ButtonVariant.Primary" OnClick="SaveCustom">Uložit</TmButton>
    </FooterContent>
</TmDialog>
```

### TmTooltip

Tooltip při najetí myší.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Text` | `string` | **povinný** | Text tooltipu |
| `Position` | `TooltipPosition` | `Top` | Pozice: `Top`, `Bottom`, `Left`, `Right` |
| `Delay` | `int` | `200` | Zpoždění zobrazení (ms) |
| `MaxWidth` | `string` | `"200px"` | Max. šířka |
| `ChildContent` | `RenderFragment?` | `null` | Trigger element |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmTooltip Text="Uložit změny">
    <TmButton Icon="@IconNames.Save" />
</TmTooltip>

<TmTooltip Text="Detailní popis akce" Position="TooltipPosition.Bottom" MaxWidth="300px">
    <TmButton>Akce</TmButton>
</TmTooltip>

<TmTooltip Text="Smazat záznam" Position="TooltipPosition.Left">
    <TmButton Icon="@IconNames.Trash" Variant="ButtonVariant.Danger" />
</TmTooltip>
```

### TmSpinner

Indikátor načítání.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Size` | `SpinnerSize` | `Sm` | Velikost: `Xs`, `Sm`, `Md`, `Lg` |
| `Color` | `SpinnerColor` | `Current` | Barva: `Current`, `Primary`, `White` |

#### Příklady

```razor
<TmSpinner />
<TmSpinner Size="SpinnerSize.Lg" Color="SpinnerColor.Primary" />

@if (_isLoading)
{
    <TmSpinner Size="SpinnerSize.Md" />
}
```

### TmPopover

Vyskakovací panel s libovolným obsahem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `TriggerContent` | `RenderFragment` | **povinný** | Trigger element |
| `ChildContent` | `RenderFragment` | — | Obsah popoveru |
| `Position` | `PopoverPosition` | `Bottom` | Pozice: `Top`, `Bottom`, `Left`, `Right` |
| `IsOpen` | `bool?` | `null` | Řízená viditelnost |
| `IsOpenChanged` | `EventCallback<bool>` | — | Změna viditelnosti |
| `ShowArrow` | `bool` | `true` | Zobrazit šipku |
| `CloseOnClickOutside` | `bool` | `true` | Zavřít klikem mimo |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Automatický (hover/click) *@
<TmPopover Position="PopoverPosition.Bottom">
    <TriggerContent>
        <TmButton Variant="ButtonVariant.Outline">Nastavení</TmButton>
    </TriggerContent>
    <ChildContent>
        <div class="p-4 w-64">
            <TmToggle @bind-Value="_darkMode" Label="Tmavý režim" />
            <TmToggle @bind-Value="_notifications" Label="Notifikace" />
        </div>
    </ChildContent>
</TmPopover>

@* Řízený *@
<TmPopover @bind-IsOpen="_showPopover" Position="PopoverPosition.Right">
    <TriggerContent>
        <TmButton OnClick="() => _showPopover = !_showPopover">Info</TmButton>
    </TriggerContent>
    <ChildContent>
        <p>Detailní informace...</p>
    </ChildContent>
</TmPopover>
```

### TmToastContainer

Kontejner pro toast notifikace (umístěte jednou v layoutu).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Position` | `ToastPosition` | `TopRight` | Pozice: `TopRight`, `TopLeft`, `BottomRight`, `BottomLeft` |
| `MaxVisible` | `int` | `5` | Max. počet viditelných toastů |

#### Příklady

```razor
@* V MainLayout.razor *@
<TmToastContainer Position="ToastPosition.TopRight" MaxVisible="5" />

@* Vyvolání toastu přes službu *@
@inject IToastService ToastService

<TmButton OnClick="@(() => ToastService.ShowSuccess("Záznam uložen"))">Uložit</TmButton>
<TmButton OnClick="@(() => ToastService.ShowError("Chyba při ukládání"))">Chyba</TmButton>
<TmButton OnClick="@(() => ToastService.ShowWarning("Upozornění"))">Varování</TmButton>
<TmButton OnClick="@(() => ToastService.ShowInfo("Informace"))">Info</TmButton>
```

### TmNotificationBell

Ikona zvonečku s počtem notifikací a rozbalovacím seznamem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Notifications` | `IReadOnlyList<INotificationItem>` | `[]` | Seznam notifikací |
| `OnMarkAsRead` | `EventCallback<string>` | — | Označit jako přečtené |
| `OnMarkAllRead` | `EventCallback` | — | Označit vše jako přečtené |
| `MaxVisible` | `int` | `10` | Max. viditelných |
| `OnNotificationClick` | `EventCallback<INotificationItem>` | — | Klik na notifikaci |

#### Příklady

```razor
<TmNotificationBell Notifications="_notifications"
    OnMarkAsRead="MarkRead"
    OnMarkAllRead="MarkAllRead"
    OnNotificationClick="OpenNotification"
    MaxVisible="10" />
```

### TmProgressBar

Progress bar s variantami a segmenty.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `double` | `0` | Aktuální hodnota |
| `Max` | `double` | `100` | Maximální hodnota |
| `Size` | `ProgressBarSize` | `Md` | Velikost: `Sm`, `Md`, `Lg` |
| `Variant` | `ProgressBarVariant` | `Default` | Varianta: `Default`, `Success`, `Warning`, `Error`, `Gradient` |
| `ShowLabel` | `bool` | `false` | Zobrazit procenta |
| `LabelFormat` | `string?` | `null` | Formát labelu |
| `Striped` | `bool` | `false` | Pruhovaný |
| `Animated` | `bool` | `false` | Animovaný |
| `Indeterminate` | `bool` | `false` | Neurčitý stav |
| `Segments` | `List<ProgressSegment>?` | `null` | Segmenty pro vícenásobný progress |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmProgressBar Value="75" ShowLabel="true" />
<TmProgressBar Value="30" Variant="ProgressBarVariant.Warning" Striped="true" />
<TmProgressBar Value="90" Variant="ProgressBarVariant.Success" Animated="true" />
<TmProgressBar Indeterminate="true" />

@* Segmentovaný *@
<TmProgressBar Segments="@(new List<ProgressSegment>
{
    new(60, "#22c55e", "Hotovo"),
    new(20, "#f59e0b", "Rozpracované"),
    new(10, "#ef4444", "Chyby"),
})" />
```

### TmSkeleton

Placeholder načítání (skeleton loader).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Variant` | `SkeletonVariant` | `Text` | Tvar: `Text`, `Circle`, `Rect` |
| `Width` | `string?` | `null` | Šířka |
| `Height` | `string?` | `null` | Výška |

#### Příklady

```razor
@* Skeleton pro text *@
<TmSkeleton Variant="SkeletonVariant.Text" Width="200px" />
<TmSkeleton Variant="SkeletonVariant.Text" Width="150px" />

@* Skeleton pro avatar *@
<TmSkeleton Variant="SkeletonVariant.Circle" Width="40px" Height="40px" />

@* Skeleton pro obrázek *@
<TmSkeleton Variant="SkeletonVariant.Rect" Width="100%" Height="200px" />
```

---

## Navigace

### TmTabs + TmTabPanel

Záložky s obsahem.

#### Parametry TmTabs

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `ActiveTabId` | `string` | **povinný** | ID aktivní záložky |
| `ActiveTabIdChanged` | `EventCallback<string>` | — | Událost změny |
| `Variant` | `TabVariant` | `Line` | Styl: `Line`, `Pill`, `Enclosed` |
| `ChildContent` | `RenderFragment?` | `null` | TmTabPanel děti |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Parametry TmTabPanel

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Id` | `string` | **povinný** | Unikátní ID záložky |
| `Title` | `string` | **povinný** | Název záložky |
| `Icon` | `string?` | `null` | Ikona |
| `Badge` | `string?` | `null` | Badge text |
| `Disabled` | `bool` | `false` | Zakázáno |
| `ChildContent` | `RenderFragment` | — | Obsah záložky |

#### Příklady

```razor
@* Line varianta *@
<TmTabs @bind-ActiveTabId="_activeTab">
    <TmTabPanel Id="general" Title="Obecné">
        <p>Obecné nastavení</p>
    </TmTabPanel>
    <TmTabPanel Id="security" Title="Zabezpečení" Icon="@IconNames.Shield">
        <p>Nastavení zabezpečení</p>
    </TmTabPanel>
    <TmTabPanel Id="notifications" Title="Notifikace" Badge="3">
        <p>Notifikace</p>
    </TmTabPanel>
    <TmTabPanel Id="advanced" Title="Pokročilé" Disabled="true">
        <p>Pokročilé nastavení</p>
    </TmTabPanel>
</TmTabs>

@* Pill varianta *@
<TmTabs @bind-ActiveTabId="_tab" Variant="TabVariant.Pill">
    <TmTabPanel Id="all" Title="Vše">...</TmTabPanel>
    <TmTabPanel Id="active" Title="Aktivní">...</TmTabPanel>
    <TmTabPanel Id="archived" Title="Archivované">...</TmTabPanel>
</TmTabs>

@* Enclosed varianta *@
<TmTabs @bind-ActiveTabId="_tab" Variant="TabVariant.Enclosed">
    <TmTabPanel Id="code" Title="Kód">...</TmTabPanel>
    <TmTabPanel Id="preview" Title="Náhled">...</TmTabPanel>
</TmTabs>
```

### TmContextMenu + TmContextMenuItem

Kontextové menu (pravé kliknutí).

#### Parametry TmContextMenu

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Trigger` | `RenderFragment` | — | Element, na kterém se otevírá |
| `ChildContent` | `RenderFragment` | — | TmContextMenuItem děti |

#### Parametry TmContextMenuItem

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Label` | `string?` | `null` | Text položky |
| `Icon` | `string?` | `null` | Ikona |
| `Disabled` | `bool` | `false` | Zakázáno |
| `IsDivider` | `bool` | `false` | Oddělovač |
| `IsDangerous` | `bool` | `false` | Nebezpečná akce (červená) |
| `OnClick` | `EventCallback` | — | Událost kliknutí |

#### Příklady

```razor
<TmContextMenu>
    <Trigger>
        <div class="p-4 border">Klikněte pravým tlačítkem</div>
    </Trigger>
    <ChildContent>
        <TmContextMenuItem Label="Kopírovat" Icon="@IconNames.Copy" OnClick="Copy" />
        <TmContextMenuItem Label="Vložit" Icon="@IconNames.Clipboard" OnClick="Paste" />
        <TmContextMenuItem IsDivider="true" />
        <TmContextMenuItem Label="Smazat" Icon="@IconNames.Trash" IsDangerous="true" OnClick="Delete" />
    </ChildContent>
</TmContextMenu>
```

---

## Ikony a avatary

### TmIcon

SVG ikona z vestavěné knihovny.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Name` | `string` | **povinný** | Název ikony (`IconNames.*`) |
| `Size` | `IconSize` | `Md` | Velikost: `Xs`, `Sm`, `Md`, `Lg`, `Xl` |
| `Color` | `IconColor` | `Current` | Barva: `Current`, `Primary`, `Danger`, `Success`, `Warning`, `Muted` |
| `StrokeWidth` | `double` | `2` | Tloušťka čáry |

#### Příklady

```razor
<TmIcon Name="@IconNames.Check" />
<TmIcon Name="@IconNames.X" Color="IconColor.Danger" />
<TmIcon Name="@IconNames.AlertTriangle" Color="IconColor.Warning" Size="IconSize.Lg" />
<TmIcon Name="@IconNames.Info" Color="IconColor.Primary" Size="IconSize.Xl" StrokeWidth="1.5" />
```

### TmAvatar

Avatar uživatele (obrázek nebo iniciály).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Src` | `string?` | `null` | URL obrázku |
| `Alt` | `string?` | `null` | Alt text |
| `Name` | `string?` | `null` | Jméno (pro generování iniciál) |
| `Size` | `AvatarSize` | `Md` | Velikost: `Xs`, `Sm`, `Md`, `Lg`, `Xl`, `Xxl` |
| `Shape` | `AvatarShape` | `Circle` | Tvar: `Circle`, `Square` |
| `Color` | `AvatarColor` | `Gray` | Barva pozadí |

#### Příklady

```razor
@* S obrázkem *@
<TmAvatar Src="/img/user.jpg" Alt="Jan Novák" />

@* S iniciálami *@
<TmAvatar Name="Jan Novák" Color="AvatarColor.Blue" />
<TmAvatar Name="Marie Svobodová" Color="AvatarColor.Purple" Size="AvatarSize.Lg" />

@* Čtvercový *@
<TmAvatar Name="Firma s.r.o." Shape="AvatarShape.Square" Color="AvatarColor.Green" />

@* Velikosti *@
<TmAvatar Name="JN" Size="AvatarSize.Xs" />
<TmAvatar Name="JN" Size="AvatarSize.Sm" />
<TmAvatar Name="JN" Size="AvatarSize.Md" />
<TmAvatar Name="JN" Size="AvatarSize.Lg" />
<TmAvatar Name="JN" Size="AvatarSize.Xl" />
<TmAvatar Name="JN" Size="AvatarSize.Xxl" />
```

### TmAvatarGroup

Skupina avatarů s limitem a počítadlem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Max` | `int?` | `null` | Max. viditelných avatarů |
| `TotalCount` | `int` | `0` | Celkový počet (pro "+N") |
| `Size` | `AvatarSize` | `Md` | Velikost avatarů |
| `ChildContent` | `RenderFragment` | — | TmAvatar děti |

#### Příklady

```razor
<TmAvatarGroup Max="3" TotalCount="8" Size="AvatarSize.Sm">
    <TmAvatar Name="Jan Novák" Color="AvatarColor.Blue" />
    <TmAvatar Name="Marie Svobodová" Color="AvatarColor.Purple" />
    <TmAvatar Name="Petr Dvořák" Color="AvatarColor.Green" />
    <TmAvatar Name="Eva Černá" Color="AvatarColor.Red" />
</TmAvatarGroup>
```

---

## Pickery

### TmDatePicker

Výběr data s kalendářem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `DateOnly?` | `null` | Vybrané datum |
| `ValueChanged` | `EventCallback<DateOnly?>` | — | Událost změny |
| `MinDate` | `DateOnly?` | `null` | Minimální datum |
| `MaxDate` | `DateOnly?` | `null` | Maximální datum |
| `DateFormat` | `string` | `"dd.MM.yyyy"` | Formát zobrazení |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Required` | `bool` | `false` | Povinné |
| `Label` | `string?` | `null` | Popisek |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Základní *@
<TmDatePicker @bind-Value="_date" Label="Datum narození" />

@* S rozsahem *@
<TmDatePicker @bind-Value="_startDate" Label="Od"
    MinDate="@(DateOnly.FromDateTime(DateTime.Today))"
    MaxDate="@(DateOnly.FromDateTime(DateTime.Today.AddYears(1)))" />

@* Vlastní formát *@
<TmDatePicker @bind-Value="_date" Label="Datum" DateFormat="yyyy-MM-dd" />

@* Povinné *@
<TmDatePicker @bind-Value="_deadline" Label="Termín" Required="true"
    Placeholder="Vyberte termín" />

@* Rozsah dat (dva pickery) *@
<div class="flex gap-4">
    <TmDatePicker @bind-Value="_from" Label="Od" MaxDate="_to" />
    <TmDatePicker @bind-Value="_to" Label="Do" MinDate="_from" />
</div>
```

### TmDateRangePicker

Výběr rozsahu dat s presety.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `(DateTime? Start, DateTime? End)` | — | Vybraný rozsah |
| `ValueChanged` | `EventCallback<(DateTime?, DateTime?)>` | — | Událost změny |
| `MinDate` | `DateTime?` | `null` | Min. datum |
| `MaxDate` | `DateTime?` | `null` | Max. datum |
| `DateFormat` | `string` | `"dd.MM.yyyy"` | Formát |
| `Presets` | `IEnumerable<DateRangePreset>?` | `null` | Předdefinované rozsahy |
| `ShowPresets` | `bool` | `true` | Zobrazit presety |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Label` | `string?` | `null` | Popisek |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmDateRangePicker @bind-Value="_dateRange" Label="Období"
    ShowPresets="true" />

@* S vlastními presety *@
<TmDateRangePicker @bind-Value="_range" Label="Období"
    Presets="@(new[]
    {
        new DateRangePreset("Dnes", DateOnly.FromDateTime(DateTime.Today), DateOnly.FromDateTime(DateTime.Today)),
        new DateRangePreset("Poslední týden", DateOnly.FromDateTime(DateTime.Today.AddDays(-7)), DateOnly.FromDateTime(DateTime.Today)),
        new DateRangePreset("Poslední měsíc", DateOnly.FromDateTime(DateTime.Today.AddMonths(-1)), DateOnly.FromDateTime(DateTime.Today)),
    })" />
```

### TmDateTimePicker

Výběr data a času.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `DateTime?` | `null` | Vybraný datum a čas |
| `ValueChanged` | `EventCallback<DateTime?>` | — | Událost změny |
| `MinValue` | `DateTime?` | `null` | Minimum |
| `MaxValue` | `DateTime?` | `null` | Maximum |
| `DateFormat` | `string` | `"dd.MM.yyyy"` | Formát data |
| `ShowSeconds` | `bool` | `false` | Zobrazit sekundy |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Required` | `bool` | `false` | Povinné |
| `HideClear` | `bool` | `false` | Skrýt vymazání |
| `Label` | `string?` | `null` | Popisek |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmDateTimePicker @bind-Value="_scheduledAt" Label="Naplánováno na"
    Required="true" Placeholder="Vyberte datum a čas" />

<TmDateTimePicker @bind-Value="_eventStart" Label="Začátek události"
    ShowSeconds="true" MinValue="DateTime.Now" />
```

### TmDateTimeRangePicker

Výběr rozsahu datum + čas.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `(DateTime? Start, DateTime? End)` | — | Vybraný rozsah |
| `ValueChanged` | `EventCallback<(DateTime?, DateTime?)>` | — | Událost změny |
| `MinValue` | `DateTime?` | `null` | Minimum |
| `MaxValue` | `DateTime?` | `null` | Maximum |
| `Presets` | `IEnumerable<DateRangePreset>?` | `null` | Presety |
| `ShowSeconds` | `bool` | `false` | Zobrazit sekundy |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Label` | `string?` | `null` | Popisek |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmDateTimeRangePicker @bind-Value="_meetingRange" Label="Schůzka"
    ShowSeconds="false" />
```

### TmTimePicker

Výběr času s dropdownem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `TimeSpan?` | `null` | Vybraný čas |
| `ValueChanged` | `EventCallback<TimeSpan?>` | — | Událost změny |
| `MinTime` | `TimeSpan?` | `null` | Minimální čas |
| `MaxTime` | `TimeSpan?` | `null` | Maximální čas |
| `ShowSeconds` | `bool` | `false` | Zobrazit sekundy |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Required` | `bool` | `false` | Povinné |
| `Label` | `string?` | `null` | Popisek |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmTimePicker @bind-Value="_startTime" Label="Začátek" Required="true" />

<TmTimePicker @bind-Value="_endTime" Label="Konec"
    MinTime="@(new TimeSpan(8, 0, 0))"
    MaxTime="@(new TimeSpan(18, 0, 0))" />
```

### TmTimeInput

Nízkoúrovňový vstup pro čas (hodiny:minuty:sekundy).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `TimeSpan?` | `null` | Hodnota |
| `ValueChanged` | `EventCallback<TimeSpan?>` | — | Událost změny |
| `ShowSeconds` | `bool` | `false` | Zobrazit sekundy |
| `Disabled` | `bool` | `false` | Zakázáno |
| `MinTime` | `TimeSpan?` | `null` | Minimum |
| `MaxTime` | `TimeSpan?` | `null` | Maximum |

#### Příklady

```razor
<TmTimeInput @bind-Value="_time" ShowSeconds="true" />
```

### TmTimeRangePicker

Výběr časového rozsahu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `(TimeSpan? Start, TimeSpan? End)` | — | Rozsah |
| `ValueChanged` | `EventCallback<(TimeSpan?, TimeSpan?)>` | — | Událost změny |
| `ShowDuration` | `bool` | `false` | Zobrazit trvání |
| `ShowSwapButton` | `bool` | `false` | Tlačítko pro prohození |
| `ShowSeconds` | `bool` | `false` | Zobrazit sekundy |
| `Label` | `string?` | `null` | Popisek |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmTimeRangePicker @bind-Value="_workHours" Label="Pracovní doba"
    ShowDuration="true" ShowSwapButton="true" />
```

### TmCalendarView

Měsíční kalendářní pohled s událostmi.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `SelectedDate` | `DateTime?` | `null` | Vybrané datum |
| `OnDateClick` | `EventCallback<DateTime>` | — | Klik na datum |
| `Events` | `IEnumerable<ICalendarEvent>?` | `null` | Události |
| `OnEventClick` | `EventCallback<ICalendarEvent>` | — | Klik na událost |
| `MinDate` | `DateTime?` | `null` | Min. datum |
| `MaxDate` | `DateTime?` | `null` | Max. datum |
| `HighlightedDates` | `IEnumerable<DateTime>?` | `null` | Zvýrazněné dny |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmCalendarView SelectedDate="DateTime.Today"
    OnDateClick="HandleDateClick"
    Events="_calendarEvents"
    OnEventClick="OpenEvent" />
```

---

## Formuláře a validace

### TmFormField

Obalový komponent formulářového pole s label, chybou a helpem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Label` | `string?` | `null` | Popisek |
| `Required` | `bool` | `false` | Zobrazit hvězdičku |
| `HelpText` | `string?` | `null` | Pomocný text |
| `ErrorMessage` | `string?` | `null` | Chybová zpráva |
| `ChildContent` | `RenderFragment?` | `null` | Vstupní prvek |
| `For` | `string?` | `null` | Hodnota for atributu labelu |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmFormField Label="Email" Required="true" ErrorMessage="@_emailError" HelpText="Zadejte firemní email">
    <TmTextInput @bind-Value="_email" Type="email" />
</TmFormField>

<TmFormField Label="Poznámka">
    <TmTextArea @bind-Value="_note" Rows="4" />
</TmFormField>
```

### TmValidatedField

Textový vstup s automatickou integrací do EditContext pro validaci. Podporuje stejné parametry jako TmTextInput — parametry `Error`, `IsValid` a `ShowValidationIcons` se řídí automaticky z EditContext.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Label` | `string?` | `null` | Popisek |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Type` | `string` | `"text"` | HTML typ vstupu |
| `Required` | `bool` | `false` | Povinné pole (hvězdička) |
| `HelpText` | `string?` | `null` | Pomocný text |
| `Id` | `string` | auto | HTML id |
| `LeftIcon` | `string?` | `null` | Ikona vlevo |
| `RightIcon` | `string?` | `null` | Ikona vpravo |
| `Disabled` | `bool` | `false` | Zakázaný vstup |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |
| `TabIndex` | `int` | `0` | Tab pořadí |
| `AutoFocus` | `bool` | `false` | Automatický focus |
| `Class` | `string?` | `null` | Další CSS třídy |
| `OnKeyDown` | `EventCallback<KeyboardEventArgs>` | — | Stisk klávesy |
| `OnKeyUp` | `EventCallback<KeyboardEventArgs>` | — | Uvolnění klávesy |
| `OnKeyPress` | `EventCallback<KeyboardEventArgs>` | — | Stisk znaku |
| `OnFocus` | `EventCallback<FocusEventArgs>` | — | Získání focusu |
| `OnBlur` | `EventCallback<FocusEventArgs>` | — | Ztráta focusu |
| `Value` | `string` | `""` | Aktuální hodnota |
| `ValueChanged` | `EventCallback<string>` | — | Událost změny |
| `ValueExpression` | `Expression<Func<string>>?` | `null` | Výraz pro identifikaci pole (automaticky z @bind) |

> **Automaticky z EditContext** (nelze nastavit ručně): `Error`, `IsValid`, `ShowValidationIcons`

#### Příklady

```razor
@* Základní *@
<EditForm Model="_model">
    <FluentValidationValidator />

    <TmValidatedField Label="Jméno" Required="true"
        @bind-Value="_model.FirstName" Placeholder="Zadejte jméno" />

    <TmValidatedField Label="Email" Required="true" Type="email"
        @bind-Value="_model.Email" Placeholder="user@example.com"
        LeftIcon="@IconNames.Mail" />
</EditForm>

@* S ikonami a klávesovými událostmi *@
<TmValidatedField Label="Telefon" @bind-Value="_model.Phone"
    LeftIcon="@IconNames.Phone" Placeholder="+420..."
    OnBlur="ValidatePhone" />

@* Disabled / ReadOnly *@
<TmValidatedField Label="Kód" @bind-Value="_model.Code" ReadOnly="true" />
<TmValidatedField Label="ID" @bind-Value="_model.Id" Disabled="true" />

@* AutoFocus na první pole *@
<TmValidatedField Label="Jméno" @bind-Value="_model.Name"
    AutoFocus="true" Required="true" />

@* S custom třídou *@
<TmValidatedField Label="Poznámka" @bind-Value="_model.Note"
    Class="my-custom-input" HelpText="Nepovinné pole" />
```

### TmValidationSummary

Souhrn všech validačních chyb formuláře.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis chyb |
| `ShowErrorsList` | `bool` | `true` | Zobrazit seznam chyb |
| `Class` | `string?` | `null` | Další CSS třídy |
| `ManualMode` | `bool` | `false` | Ruční řízení viditelnosti |
| `Show` | `bool` | `false` | Viditelnost (jen v ManualMode) |

#### Příklady

```razor
<EditForm Model="_model" OnValidSubmit="Submit">
    <FluentValidationValidator />
    <TmValidationSummary Title="Opravte následující chyby:" />
    @* ... pole formuláře ... *@
</EditForm>

@* Ruční režim *@
<TmValidationSummary ManualMode="true" Show="_hasErrors" Title="Chyby" />
```

### TmFormValidationMessage

Validační zpráva pro konkrétní pole.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `For` | `Expression<Func<object>>` | **povinný** | Výraz identifikující pole |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmTextInput @bind-Value="_model.Name" Label="Jméno" />
<TmFormValidationMessage For="() => _model.Name" />
```

### TmInlineEdit

Inline editace textu (kliknutím se aktivuje editační režim).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | Aktuální hodnota |
| `OnSave` | `EventCallback<string>` | — | Událost uložení |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Validate` | `Func<string, string?>?` | `null` | Validační funkce (vrací chybu nebo null) |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Základní *@
<TmInlineEdit Value="@_title" OnSave="v => _title = v" />

@* S validací *@
<TmInlineEdit Value="@_name" OnSave="SaveName"
    Validate="@(v => string.IsNullOrWhiteSpace(v) ? "Název nesmí být prázdný" : null)" />

@* S placeholderem *@
<TmInlineEdit Value="@_description" OnSave="SaveDesc"
    Placeholder="Klikněte pro zadání popisu" />
```

### TmFormSection

Sekce formuláře s nadpisem a možností sbalení.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis sekce |
| `Description` | `string?` | `null` | Popis sekce |
| `ChildContent` | `RenderFragment` | — | Obsah sekce |
| `Collapsible` | `bool` | `false` | Lze sbalit |
| `CollapsedByDefault` | `bool` | `false` | Ve výchozím stavu sbaleno |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmFormSection Title="Osobní údaje" Description="Základní informace o uživateli">
    <TmFormRow>
        <TmTextInput @bind-Value="_model.FirstName" Label="Jméno" />
        <TmTextInput @bind-Value="_model.LastName" Label="Příjmení" />
    </TmFormRow>
</TmFormSection>

<TmFormSection Title="Pokročilé nastavení" Collapsible="true" CollapsedByDefault="true">
    <TmTextArea @bind-Value="_model.Notes" Label="Poznámky" />
</TmFormSection>
```

### TmFormRow

Řádek formuláře s automatickým rozvržením sloupců.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `ChildContent` | `RenderFragment` | — | Pole v řádku |
| `Columns` | `int` | `2` | Počet sloupců |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmFormRow>
    <TmTextInput @bind-Value="_model.FirstName" Label="Jméno" />
    <TmTextInput @bind-Value="_model.LastName" Label="Příjmení" />
</TmFormRow>

<TmFormRow Columns="3">
    <TmTextInput @bind-Value="_model.City" Label="Město" />
    <TmTextInput @bind-Value="_model.Street" Label="Ulice" />
    <TmTextInput @bind-Value="_model.Zip" Label="PSČ" />
</TmFormRow>
```

### TmDynamicFormRenderer

Dynamický renderer formuláře z definic polí (metadata-driven).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Values` | `Dictionary<string, object?>` | — | Hodnoty polí |
| `ValuesChanged` | `EventCallback<Dictionary<string, object?>>` | — | Změna hodnot |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |
| `Columns` | `int` | `1` | Počet sloupců |
| `OnFieldChanged` | `EventCallback<(string FieldKey, object? Value)>` | — | Změna konkrétního pole |

#### Příklady

```razor
@* Pole jsou definována dynamicky (např. z API/DB) *@
<TmDynamicFormRenderer
    Values="_formValues"
    ValuesChanged="v => _formValues = v"
    Columns="2"
    OnFieldChanged="HandleFieldChange" />

@code {
    private Dictionary<string, object?> _formValues = new()
    {
        ["name"] = "Jan",
        ["email"] = "jan@example.com",
        ["age"] = 30,
    };

    // Definice polí se předávají přes DynamicFieldDefinition
    // (typicky z backendu nebo konfigurace)
}
```

---

## DataTable

### TmDataTable\<TItem\>

Kompletní datová tabulka s řazením, filtrováním, stránkováním, groupingem a virtualizací.

#### Klíčové parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Items` | `IEnumerable<TItem>?` | `null` | In-memory data |
| `DataProvider` | `IDataTableDataProvider<TItem>?` | `null` | Server-side data provider |
| `ChildContent` | `RenderFragment` | — | Definice sloupců |
| `IsLoading` | `bool` | `false` | Stav načítání |
| `Selectable` | `bool` | `false` | Checkboxy pro výběr řádků |
| `ShowSearch` | `bool` | `true` | Vyhledávání |
| `ShowColumnPicker` | `bool` | `true` | Výběr sloupců |
| `ShowPagination` | `bool` | `true` | Stránkování |
| `DefaultPageSize` | `int` | `25` | Výchozí stránka |
| `PageSizeOptions` | `IReadOnlyList<int>` | `[5,10,25,50,100]` | Možnosti velikosti stránky |
| `ScrollMode` | `DataTableScrollMode` | `Pagination` | `Pagination` / `Virtualized` |
| `ShowGrouping` | `bool` | `false` | Povolit grouping |
| `OnRowClick` | `EventCallback<TItem>` | — | Klik na řádek |
| `OnSelectionChanged` | `EventCallback<IReadOnlyList<TItem>>` | — | Změna výběru |
| `ViewContext` | `string` | **povinný** | Unikátní ID pro ukládání view |

#### TmDataTableColumn\<TItem\>

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Key` | `string` | **povinný** | Unikátní klíč |
| `Title` | `string` | — | Nadpis sloupce |
| `Field` | `Func<TItem, object?>?` | `null` | Accessor pro řazení/filtrování |
| `CellTemplate` | `RenderFragment<TItem>?` | `null` | Vlastní šablona buňky |
| `Sortable` | `bool` | `false` | Povoleno řazení |
| `Groupable` | `bool` | `false` | Povoleno groupování |
| `Hideable` | `bool` | `false` | Povolit skrytí |
| `Width` | `string?` | `null` | Šířka sloupce |
| `Align` | `ColumnAlign` | `Left` | Zarovnání: `Left`, `Center`, `Right` |

#### Příklady

```razor
@* Základní tabulka *@
<TmDataTable TItem="Employee" Items="_employees" ViewContext="employees-list">
    <TmDataTableColumn TItem="Employee" Key="name" Title="Jméno"
        Field="@(e => e.Name)" Sortable="true" />
    <TmDataTableColumn TItem="Employee" Key="email" Title="Email"
        Field="@(e => e.Email)" Sortable="true" />
    <TmDataTableColumn TItem="Employee" Key="department" Title="Oddělení"
        Field="@(e => e.Department)" Sortable="true" Groupable="true" />
    <TmDataTableColumn TItem="Employee" Key="actions" Title="Akce" Width="100px">
        <CellTemplate Context="emp">
            <TmButton Size="ButtonSize.Xs" Variant="ButtonVariant.Ghost"
                Icon="@IconNames.Edit" OnClick="() => Edit(emp)" />
            <TmButton Size="ButtonSize.Xs" Variant="ButtonVariant.Ghost"
                Icon="@IconNames.Trash" OnClick="() => Delete(emp)" />
        </CellTemplate>
    </TmDataTableColumn>
</TmDataTable>

@* Se selekcí a hromadnými akcemi *@
<TmDataTable TItem="Order" Items="_orders" ViewContext="orders"
    Selectable="true" OnSelectionChanged="HandleSelection">
    <SelectionActions>
        <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Danger"
            OnClick="DeleteSelected">Smazat vybrané</TmButton>
        <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Secondary"
            OnClick="ExportSelected">Export</TmButton>
    </SelectionActions>
    <TmDataTableColumn TItem="Order" Key="id" Title="#" Field="@(o => o.Id)" Sortable="true" />
    <TmDataTableColumn TItem="Order" Key="customer" Title="Zákazník" Field="@(o => o.Customer)" />
    <TmDataTableColumn TItem="Order" Key="total" Title="Celkem" Field="@(o => o.Total)" Align="ColumnAlign.Right">
        <CellTemplate Context="order">
            @order.Total.ToString("N2") Kč
        </CellTemplate>
    </TmDataTableColumn>
</TmDataTable>

@* Virtualizovaná tabulka *@
<TmDataTable TItem="LogEntry" Items="_logs" ViewContext="logs"
    ScrollMode="DataTableScrollMode.Virtualized"
    VirtualScrollHeight="600px"
    ShowPagination="false">
    <TmDataTableColumn TItem="LogEntry" Key="time" Title="Čas" Field="@(l => l.Timestamp)" Sortable="true" />
    <TmDataTableColumn TItem="LogEntry" Key="level" Title="Úroveň" Field="@(l => l.Level)" />
    <TmDataTableColumn TItem="LogEntry" Key="message" Title="Zpráva" Field="@(l => l.Message)" />
</TmDataTable>
```

### TmPagination

Stránkování (samostatná komponenta).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `CurrentPage` | `int` | `1` | Aktuální stránka |
| `TotalPages` | `int` | `1` | Celkem stránek |
| `TotalCount` | `int` | `0` | Celkem záznamů |
| `PageSize` | `int` | `25` | Velikost stránky |
| `PageSizeOptions` | `int[]` | `[5,10,25,50,100]` | Možnosti velikosti |
| `OnPageChange` | `EventCallback<int>` | — | Změna stránky |
| `OnPageSizeChange` | `EventCallback<int>` | — | Změna velikosti |

#### Příklady

```razor
<TmPagination CurrentPage="_page" TotalPages="_totalPages"
    TotalCount="_total" PageSize="25"
    OnPageChange="p => _page = p"
    OnPageSizeChange="HandlePageSizeChange" />
```

### TmColumnPicker

Výběr viditelných sloupců tabulky.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Columns` | `IReadOnlyList<ColumnVisibilityItem>` | `[]` | Seznam sloupců |
| `OnToggleColumn` | `EventCallback<string>` | — | Přepnout viditelnost |
| `OnReset` | `EventCallback` | — | Obnovit výchozí |

#### Příklady

```razor
<TmColumnPicker Columns="_columnItems"
    OnToggleColumn="ToggleColumn"
    OnReset="ResetColumns" />
```

### TmViewManager

Správa uložených pohledů (filtry, řazení, sloupce).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Provider` | `IViewProvider` | — | Poskytovatel pohledů |
| `ViewContext` | `string` | — | Unikátní klíč kontextu |
| `OnViewApplied` | `EventCallback<SavedView>` | — | Aplikování pohledu |
| `GetCurrentView` | `Func<ViewDefinition>?` | `null` | Získat aktuální stav |
| `GetCurrentFilters` | `Func<List<ActiveFilter>>?` | `null` | Získat aktuální filtry |
| `OnCreateView` | `EventCallback<ViewDefinition>` | — | Vytvoření pohledu |
| `AvailableColumns` | `IReadOnlyList<ColumnInfo>?` | `null` | Dostupné sloupce |
| `FilterDefinitions` | `IReadOnlyList<FilterDefinition>?` | `null` | Definice filtrů |
| `CanCreateTenantViews` | `bool` | `false` | Sdílené pohledy |
| `CurrentUserId` | `string?` | `null` | Aktuální uživatel |
| `CurrentTenantId` | `string?` | `null` | Aktuální tenant |

#### Příklady

```razor
<TmViewManager Provider="_viewProvider" ViewContext="orders-table"
    OnViewApplied="ApplyView"
    GetCurrentView="GetCurrentViewState"
    FilterDefinitions="_filterDefs"
    CanCreateTenantViews="true"
    CurrentUserId="@_userId" />
```

### TmFilterBuilder

Vizuální builder filtrů s aktivními filter chipy.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `FilterDefinitions` | `IReadOnlyList<FilterDefinition>` | `[]` | Dostupné pole pro filtrování |
| `ActiveFilters` | `IReadOnlyList<ActiveFilter>` | `[]` | Aktuální filtry |
| `OnFiltersChanged` | `EventCallback<IReadOnlyList<ActiveFilter>>` | — | Změna filtrů |
| `ShowClearAll` | `bool` | `true` | Zobrazit "Smazat vše" |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmFilterBuilder
    FilterDefinitions="@(new List<FilterDefinition>
    {
        new() { FieldName = "name", FieldLabel = "Jméno", FieldType = FilterFieldType.Text },
        new() { FieldName = "age", FieldLabel = "Věk", FieldType = FilterFieldType.Number },
        new() { FieldName = "created", FieldLabel = "Vytvořeno", FieldType = FilterFieldType.Date },
        new() { FieldName = "status", FieldLabel = "Stav", FieldType = FilterFieldType.Select,
            Options = new List<SelectOption<string>>
            {
                SelectOption<string>.From("active", "Aktivní"),
                SelectOption<string>.From("inactive", "Neaktivní"),
            }},
        new() { FieldName = "isVip", FieldLabel = "VIP", FieldType = FilterFieldType.Boolean },
    })"
    ActiveFilters="_activeFilters"
    OnFiltersChanged="HandleFiltersChanged" />

@code {
    private IReadOnlyList<ActiveFilter> _activeFilters = [];

    private void HandleFiltersChanged(IReadOnlyList<ActiveFilter> filters)
    {
        _activeFilters = filters;
        // Reload data se filtry
    }
}
```

### TmFilterChip

Chip zobrazující aktivní filtr s možností editace a odebrání.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `OnRemove` | `EventCallback<ActiveFilter>` | — | Odebrání filtru |
| `OnEdit` | `EventCallback<ActiveFilter>` | — | Editace filtru |

### TmBulkActionBar

Panel hromadných akcí pro vybrané řádky.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `SelectedCount` | `int` | `0` | Počet vybraných |
| `OnClearSelection` | `EventCallback` | — | Zrušit výběr |
| `ChildContent` | `RenderFragment` | — | Akční tlačítka |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmBulkActionBar SelectedCount="_selected.Count" OnClearSelection="ClearSelection">
    <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Danger" OnClick="DeleteSelected">
        Smazat (@_selected.Count)
    </TmButton>
    <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Secondary" OnClick="ExportSelected">
        Export
    </TmButton>
</TmBulkActionBar>
```

---

## Layout

### TmDrawer

Vysouvací panel (drawer).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `IsOpen` | `bool` | `false` | Otevřeno |
| `IsOpenChanged` | `EventCallback<bool>` | — | Změna stavu |
| `Position` | `DrawerPosition` | `Right` | Pozice: `Right`, `Left` |
| `Width` | `string` | `"400px"` | Šířka |
| `Title` | `string?` | `null` | Nadpis |
| `ShowOverlay` | `bool` | `true` | Zobrazit overlay |
| `CloseOnOverlayClick` | `bool` | `true` | Zavřít klikem na overlay |
| `CloseOnEscape` | `bool` | `true` | Zavřít Escape |
| `ShowCloseButton` | `bool` | `true` | Zobrazit X |
| `HeaderContent` | `RenderFragment?` | `null` | Custom hlavička |
| `ChildContent` | `RenderFragment` | — | Obsah |
| `FooterContent` | `RenderFragment?` | `null` | Patička |

#### Příklady

```razor
<TmButton OnClick="() => _showDrawer = true">Otevřít detail</TmButton>

<TmDrawer @bind-IsOpen="_showDrawer" Title="Detail záznamu" Width="500px">
    <ChildContent>
        <TmTextInput @bind-Value="_model.Name" Label="Název" />
        <TmTextArea @bind-Value="_model.Description" Label="Popis" />
    </ChildContent>
    <FooterContent>
        <TmButton Variant="ButtonVariant.Primary" OnClick="Save">Uložit</TmButton>
        <TmButton Variant="ButtonVariant.Ghost" OnClick="() => _showDrawer = false">Zavřít</TmButton>
    </FooterContent>
</TmDrawer>

@* Drawer zleva *@
<TmDrawer @bind-IsOpen="_showNav" Position="DrawerPosition.Left" Title="Navigace"
    Width="300px" ShowOverlay="true">
    <TmSidebar Items="_navItems" />
</TmDrawer>
```

### TmSidebar

Boční navigace s vnořenými položkami a sbalením.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Items` | `IReadOnlyList<ISidebarNavItem>` | `[]` | Navigační položky |
| `Collapsed` | `bool` | `false` | Sbalený režim |
| `OnToggle` | `EventCallback<bool>` | — | Přepnutí sbalení |
| `Header` | `RenderFragment?` | `null` | Hlavička |
| `Footer` | `RenderFragment?` | `null` | Patička |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmSidebar Items="@(new List<SidebarNavItem>
{
    new() { Id = "dashboard", Label = "Dashboard", Icon = IconNames.Home, Href = "/" },
    new() { Id = "users", Label = "Uživatelé", Icon = IconNames.Users, Href = "/users", BadgeCount = 5 },
    new() { Id = "settings", Label = "Nastavení", Icon = IconNames.Settings, Children = new List<SidebarNavItem>
    {
        new() { Id = "general", Label = "Obecné", Href = "/settings/general" },
        new() { Id = "security", Label = "Zabezpečení", Href = "/settings/security" },
    }},
})"
Collapsed="_sidebarCollapsed"
OnToggle="v => _sidebarCollapsed = v">
    <Header>
        <h3>Moje Aplikace</h3>
    </Header>
    <Footer>
        <TmAvatar Name="@_userName" Size="AvatarSize.Sm" />
    </Footer>
</TmSidebar>
```

### TmTopBar

Horní lišta aplikace.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `BrandTitle` | `string?` | `null` | Název aplikace |
| `BrandContent` | `RenderFragment?` | `null` | Custom branding |
| `CenterContent` | `RenderFragment?` | `null` | Střední obsah |
| `ActionsContent` | `RenderFragment?` | `null` | Pravá strana (akce) |
| `User` | `IUserInfo?` | `null` | Info o uživateli |
| `OnCommandPalette` | `EventCallback` | — | Otevřít command palette |
| `OnLogout` | `EventCallback` | — | Odhlášení |

#### Příklady

```razor
<TmTopBar BrandTitle="Moje Aplikace" User="_currentUser"
    OnCommandPalette="OpenCommandPalette" OnLogout="Logout">
    <CenterContent>
        <TmSearchInput @bind-Value="_globalSearch" Placeholder="Hledat..." />
    </CenterContent>
    <ActionsContent>
        <TmNotificationBell Notifications="_notifications" />
    </ActionsContent>
</TmTopBar>
```

### TmBreadcrumbs

Drobečková navigace.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Items` | `IReadOnlyList<BreadcrumbItem>` | `[]` | Položky |
| `Separator` | `string` | `"/"` | Oddělovač |
| `OnItemClick` | `EventCallback<BreadcrumbItem>` | — | Klik na položku |

#### Příklady

```razor
<TmBreadcrumbs Items="@(new[]
{
    new BreadcrumbItem { Label = "Domů", Href = "/", Icon = IconNames.Home },
    new BreadcrumbItem { Label = "Uživatelé", Href = "/users" },
    new BreadcrumbItem { Label = "Jan Novák" },
})" />
```

### TmCommandPalette

Command palette (Ctrl+K) pro rychlý přístup k akcím.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `IsOpen` | `bool` | `false` | Otevřeno |
| `IsOpenChanged` | `EventCallback<bool>` | — | Změna stavu |
| `Actions` | `IEnumerable<ICommandPaletteAction>` | `[]` | Dostupné akce |

#### Příklady

```razor
<TmCommandPalette @bind-IsOpen="_showPalette" Actions="_actions" />

@code {
    // Otevřít Ctrl+K
    private void OpenPalette() => _showPalette = true;
}
```

### TmSection

Sekce stránky s nadpisem, ikonou a možností sbalení.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis |
| `Icon` | `string?` | `null` | Ikona |
| `ChildContent` | `RenderFragment` | — | Obsah |
| `HeaderActions` | `RenderFragment?` | `null` | Akce v hlavičce |
| `Collapsible` | `bool` | `false` | Lze sbalit |
| `Collapsed` | `bool` | `false` | Sbaleno |
| `CollapsedChanged` | `EventCallback<bool>` | — | Změna sbalení |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmSection Title="Přehled" Icon="@IconNames.BarChart">
    <HeaderActions>
        <TmButton Size="ButtonSize.Sm" Variant="ButtonVariant.Ghost" Icon="@IconNames.RefreshCw"
            OnClick="Refresh">Obnovit</TmButton>
    </HeaderActions>
    <ChildContent>
        <TmStatCard Title="Tržby" Value="1.2M Kč" />
    </ChildContent>
</TmSection>

<TmSection Title="Detaily" Collapsible="true" @bind-Collapsed="_detailsCollapsed">
    <p>Skrytý obsah...</p>
</TmSection>
```

### TmKeyboardShortcutsHelp

Dialog s přehledem klávesových zkratek.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Categories` | `IEnumerable<TmShortcutCategory>` | `[]` | Kategorie zkratek |
| `IsVisible` | `bool` | `false` | Viditelnost |
| `OnClose` | `EventCallback` | — | Zavření |
| `Title` | `string` | `"Klávesové zkratky"` | Nadpis |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmKeyboardShortcutsHelp IsVisible="_showShortcuts" OnClose="() => _showShortcuts = false"
    Categories="@(new[]
    {
        new TmShortcutCategory("Navigace", new[]
        {
            new TmKeyboardShortcut("Ctrl+K", "Command palette"),
            new TmKeyboardShortcut("Ctrl+/", "Klávesové zkratky"),
        }),
        new TmShortcutCategory("Editace", new[]
        {
            new TmKeyboardShortcut("Ctrl+S", "Uložit"),
            new TmKeyboardShortcut("Ctrl+Z", "Zpět"),
        }),
    })" />
```

---

## Soubory a přílohy

### TmFileDropZone

Drag & drop zóna pro nahrávání souborů.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `OnFilesSelected` | `EventCallback<IReadOnlyList<IBrowserFile>>` | — | Vybrané soubory |
| `Multiple` | `bool` | `true` | Povolit více souborů |
| `Accept` | `string?` | `null` | Povolené typy (MIME) |
| `Disabled` | `bool` | `false` | Zakázáno |
| `ChildContent` | `RenderFragment?` | `null` | Obsah zóny |
| `MaxFileCount` | `int` | `10` | Max. počet souborů |

#### Příklady

```razor
<TmFileDropZone OnFilesSelected="HandleFiles" Accept="image/*" Multiple="true" MaxFileCount="5">
    <p>Přetáhněte soubory sem nebo klikněte pro výběr</p>
</TmFileDropZone>

<TmFileDropZone OnFilesSelected="HandleCsv" Accept=".csv,.xlsx" Multiple="false">
    <TmIcon Name="@IconNames.Upload" Size="IconSize.Lg" />
    <p>Nahrajte CSV nebo Excel soubor</p>
</TmFileDropZone>
```

### TmAttachmentManager

Správa příloh entity (nahrávání, seznam, mazání).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Provider` | `IFileAttachmentProvider` | — | Poskytovatel příloh |
| `EntityId` | `string` | — | ID entity |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Class` | `string?` | `null` | Další CSS třídy |
| `OnDeleted` | `EventCallback<string>` | — | Smazání přílohy |

#### Příklady

```razor
<TmAttachmentManager Provider="_attachmentProvider" EntityId="@_orderId"
    OnDeleted="HandleAttachmentDeleted" />
```

---

## Galerie

### TmImageGallery

Galerie obrázků s lightboxem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Images` | `IReadOnlyList<GalleryImage>?` | `null` | Statické obrázky |
| `DataProvider` | `IGalleryDataProvider?` | `null` | Server-side provider |
| `IsLoading` | `bool` | `false` | Načítání |
| `EmptyTitle` | `string?` | `null` | Text prázdného stavu |
| `OnImageClick` | `EventCallback<GalleryImage>` | — | Klik na obrázek |
| `UrlResolver` | `Func<string, string>?` | `null` | Resolver URL |
| `CanDelete` | `bool` | `false` | Povolit mazání |

#### Příklady

```razor
<TmImageGallery Images="_images" CanDelete="true"
    OnImageClick="OpenImage" />
```

### TmLightbox

Lightbox pro zobrazení obrázku s navigací.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `IsOpen` | `bool` | `false` | Otevřeno |
| `ImageUrl` | `string` | — | URL obrázku |
| `ImageAlt` | `string?` | `null` | Alt text |
| `IsFirst` | `bool` | `false` | První obrázek |
| `IsLast` | `bool` | `false` | Poslední obrázek |
| `CurrentIndex` | `int` | `0` | Aktuální index |
| `TotalCount` | `int` | `0` | Celkový počet |
| `CanDelete` | `bool` | `false` | Povolit smazání |
| `OnClose` | `EventCallback` | — | Zavření |
| `OnPrev` | `EventCallback` | — | Předchozí |
| `OnNext` | `EventCallback` | — | Další |
| `OnDelete` | `EventCallback` | — | Smazat |

#### Příklady

```razor
<TmLightbox IsOpen="_lightboxOpen" ImageUrl="@_currentImageUrl"
    CurrentIndex="_index" TotalCount="_images.Count"
    IsFirst="@(_index == 0)" IsLast="@(_index == _images.Count - 1)"
    CanDelete="true"
    OnClose="() => _lightboxOpen = false"
    OnPrev="PrevImage" OnNext="NextImage" OnDelete="DeleteImage" />
```

---

## Import/Export

### TmExportOptions

Dialog pro výběr formátu a rozsahu exportu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Formats` | `IReadOnlyList<ExportFormat>` | `[]` | Dostupné formáty |
| `EntityTypes` | `IReadOnlyList<string>` | `[]` | Typy entit k exportu |
| `OnExport` | `EventCallback<ExportRequest>` | — | Spuštění exportu |
| `Title` | `string?` | `null` | Nadpis |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmExportOptions
    Formats="@(new[] { new ExportFormat("csv", "CSV"), new ExportFormat("xlsx", "Excel") })"
    EntityTypes="@(new[] { "orders", "customers" })"
    OnExport="HandleExport" Title="Export dat" />
```

### TmImportWizard + TmImportWizardStep

Průvodce importem dat s kroky.

#### Parametry TmImportWizard

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `ChildContent` | `RenderFragment` | — | Kroky (TmImportWizardStep) |
| `ActiveStep` | `int` | `0` | Aktuální krok |
| `ActiveStepChanged` | `EventCallback<int>` | — | Změna kroku |
| `OnComplete` | `EventCallback` | — | Dokončení |
| `OnCancel` | `EventCallback` | — | Zrušení |
| `CompleteText` | `string` | `"Complete"` | Text tlačítka dokončení |
| `NextText` | `string` | `"Next"` | Text Další |
| `BackText` | `string` | `"Back"` | Text Zpět |
| `CancelText` | `string` | `"Cancel"` | Text Zrušit |
| `ShowStepIndicator` | `bool` | `true` | Zobrazit indikátor kroků |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Parametry TmImportWizardStep

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Description` | `string?` | `null` | Popis kroku |
| `Icon` | `string?` | `null` | Ikona |
| `ChildContent` | `RenderFragment` | — | Obsah kroku |

#### Příklady

```razor
<TmImportWizard @bind-ActiveStep="_step" OnComplete="FinishImport" OnCancel="CancelImport">
    <TmImportWizardStep Description="Nahrání souboru" Icon="@IconNames.Upload">
        <TmFileDropZone OnFilesSelected="HandleFile" Accept=".csv" Multiple="false" />
    </TmImportWizardStep>
    <TmImportWizardStep Description="Mapování sloupců" Icon="@IconNames.Columns">
        <p>Mapování polí...</p>
    </TmImportWizardStep>
    <TmImportWizardStep Description="Náhled a potvrzení" Icon="@IconNames.Check">
        <TmImportPreview Title="Náhled importu" OnConfirm="ConfirmImport">
            <p>Bude importováno @_rowCount záznamů.</p>
        </TmImportPreview>
    </TmImportWizardStep>
</TmImportWizard>
```

### TmImportPreview

Náhled importu s potvrzením.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis |
| `Description` | `string?` | `null` | Popis |
| `ChildContent` | `RenderFragment` | — | Obsah náhledu |
| `OnConfirm` | `EventCallback` | — | Potvrzení |
| `OnCancel` | `EventCallback` | — | Zrušení |
| `ConfirmText` | `string` | `"Confirm"` | Text potvrzení |
| `CancelText` | `string` | `"Cancel"` | Text zrušení |
| `IsLoading` | `bool` | `false` | Načítání |
| `Class` | `string?` | `null` | Další CSS třídy |

---

## Grafy

### TmChart

SVG graf s různými typy.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Type` | `ChartType` | **povinný** | Typ: `Bar`, `Line`, `Pie`, `Donut`, `HorizontalBar` |
| `Data` | `ChartData` | **povinný** | Data grafu |
| `Width` | `string?` | `null` | Šířka |
| `Height` | `string?` | `null` | Výška |
| `ShowLegend` | `bool` | `true` | Zobrazit legendu |
| `ShowGrid` | `bool` | `true` | Zobrazit mřížku |
| `ShowValues` | `bool` | `false` | Zobrazit hodnoty |
| `Animated` | `bool` | `true` | Animace |
| `OnSegmentClick` | `EventCallback<ChartSegmentClickEventArgs>` | — | Klik na segment |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
@* Sloupcový *@
<TmChart Type="ChartType.Bar"
    Data="@(new ChartData
    {
        Labels = new[] { "Leden", "Únor", "Březen", "Duben" },
        Datasets = new[]
        {
            new ChartDataset { Label = "Tržby", Values = new[] { 100.0, 200.0, 150.0, 300.0 }, Color = "#3b82f6" },
            new ChartDataset { Label = "Náklady", Values = new[] { 80.0, 120.0, 90.0, 180.0 }, Color = "#ef4444" },
        }
    })" ShowValues="true" />

@* Koláčový *@
<TmChart Type="ChartType.Pie"
    Data="@(new ChartData
    {
        Labels = new[] { "Chrome", "Firefox", "Safari", "Edge" },
        Datasets = new[]
        {
            new ChartDataset { Label = "Podíl", Values = new[] { 65.0, 15.0, 12.0, 8.0 },
                Color = "#3b82f6", BackgroundColor = "#93c5fd" }
        }
    })" />

@* Liniový *@
<TmChart Type="ChartType.Line" Height="300px"
    Data="@_lineData" ShowGrid="true" Animated="true" />

@* Donut *@
<TmChart Type="ChartType.Donut" Data="@_donutData" ShowLegend="true" />

@* Horizontální *@
<TmChart Type="ChartType.HorizontalBar" Data="@_hBarData" />
```

---

## Tagy

### TmTagPicker

Picker pro výběr/vytváření tagů.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `AllTags` | `IReadOnlyList<TagItem>` | `[]` | Všechny dostupné tagy |
| `SelectedTags` | `IEnumerable<string>` | `[]` | Vybrané tagy |
| `OnTagsChanged` | `EventCallback<IEnumerable<string>>` | — | Změna výběru |
| `AllowCreate` | `bool` | `false` | Povolit vytvoření nových tagů |

#### Příklady

```razor
<TmTagPicker AllTags="_allTags" SelectedTags="_selectedTags"
    OnTagsChanged="HandleTagsChanged" AllowCreate="true" />
```

---

## Timeline

### TmTimeline

Vertikální časová osa.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Entries` | `IEnumerable<ITimelineEntry>` | `[]` | Záznamy |
| `ShowInternal` | `bool` | `false` | Zobrazit interní záznamy |

#### Příklady

```razor
<TmTimeline Entries="_timelineEntries" ShowInternal="false" />
```

---

## Toolbar

### TmToolbar

Panel nástrojů s tlačítky.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Title` | `string?` | `null` | Nadpis |
| `ChildContent` | `RenderFragment` | — | Obsah (tlačítka) |
| `Actions` | `RenderFragment?` | `null` | Akce vpravo |
| `Sticky` | `bool` | `false` | Přilepený nahoře |
| `Class` | `string?` | `null` | Další CSS třídy |

### TmToolbarButton

Tlačítko v toolbaru.

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Text` | `string?` | `null` | Text |
| `Icon` | `string?` | `null` | Ikona |
| `Tooltip` | `string?` | `null` | Tooltip |
| `OnClick` | `EventCallback` | — | Klik |
| `Disabled` | `bool` | `false` | Zakázáno |
| `Variant` | `ButtonVariant` | `Ghost` | Varianta |
| `Class` | `string?` | `null` | CSS |

### TmToolbarDivider

Oddělovač v toolbaru (bez parametrů).

#### Příklady

```razor
<TmToolbar Title="Editace dokumentu" Sticky="true">
    <TmToolbarButton Icon="@IconNames.Save" Tooltip="Uložit" OnClick="Save" />
    <TmToolbarButton Icon="@IconNames.Undo" Tooltip="Zpět" OnClick="Undo" />
    <TmToolbarButton Icon="@IconNames.Redo" Tooltip="Vpřed" OnClick="Redo" />
    <TmToolbarDivider />
    <TmToolbarButton Icon="@IconNames.Bold" Tooltip="Tučné" OnClick="ToggleBold" />
    <TmToolbarButton Icon="@IconNames.Italic" Tooltip="Kurzíva" OnClick="ToggleItalic" />
    <Actions>
        <TmButton Variant="ButtonVariant.Primary" OnClick="Publish">Publikovat</TmButton>
    </Actions>
</TmToolbar>
```

---

## TreeView

### TmTreeView\<TKey\>

Stromové zobrazení s vnořenými uzly.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Nodes` | `IEnumerable<TreeNode<TKey>>` | `[]` | Kořenové uzly |
| `OnNodeSelect` | `EventCallback<TreeNode<TKey>>` | — | Výběr uzlu |
| `OnNodeExpand` | `EventCallback<TreeNode<TKey>>` | — | Rozbalení uzlu |

#### Příklady

```razor
<TmTreeView TKey="int" Nodes="_treeNodes"
    OnNodeSelect="HandleNodeSelect"
    OnNodeExpand="HandleNodeExpand" />
```

---

## Scheduler

### TmScheduler

Plánovač/kalendář s denním, týdenním, měsíčním, agenda a timeline pohledem.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Events` | `IEnumerable<IScheduleEvent>?` | `null` | Statické události |
| `DataProvider` | `IScheduleDataProvider?` | `null` | Server-side provider |
| `View` | `SchedulerView` | `Week` | Pohled: `Day`, `Week`, `Month`, `Agenda`, `Timeline` |
| `ViewChanged` | `EventCallback<SchedulerView>` | — | Změna pohledu |
| `CurrentDate` | `DateTime` | `DateTime.Today` | Aktuální datum |
| `CurrentDateChanged` | `EventCallback<DateTime>` | — | Změna data |
| `OnEventClick` | `EventCallback<IScheduleEvent>` | — | Klik na událost |
| `OnSlotClick` | `EventCallback<SchedulerSlotClickEventArgs>` | — | Klik na slot |
| `OnEventChanged` | `EventCallback<ScheduleEventChangeArgs>` | — | Drag & drop změna |
| `Resources` | `IEnumerable<IScheduleResource>?` | `null` | Zdroje (místnosti, osoby) |
| `FirstDayOfWeek` | `DayOfWeek` | `Monday` | První den týdne |
| `WorkDayStart` | `TimeSpan` | `08:00` | Začátek pracovního dne |
| `WorkDayEnd` | `TimeSpan` | `17:00` | Konec pracovního dne |
| `SlotDuration` | `TimeSpan` | `00:30` | Délka slotu |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |
| `EventTemplate` | `RenderFragment<IScheduleEvent>?` | `null` | Custom šablona události |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmScheduler Events="_events"
    @bind-View="_view"
    @bind-CurrentDate="_currentDate"
    OnEventClick="OpenEvent"
    OnSlotClick="CreateEvent"
    OnEventChanged="HandleDragDrop"
    FirstDayOfWeek="DayOfWeek.Monday"
    WorkDayStart="@(new TimeSpan(8, 0, 0))"
    WorkDayEnd="@(new TimeSpan(18, 0, 0))"
    SlotDuration="@(TimeSpan.FromMinutes(30))">
    <EventTemplate Context="evt">
        <div style="background: @evt.Color">
            <strong>@evt.Title</strong>
            <small>@evt.Start.ToString("HH:mm")</small>
        </div>
    </EventTemplate>
</TmScheduler>

@* S resources (timeline pohled) *@
<TmScheduler Events="_events" View="SchedulerView.Timeline"
    Resources="@(new List<TmScheduleResource>
    {
        new() { Id = "room1", Name = "Zasedačka A", Color = "#3b82f6" },
        new() { Id = "room2", Name = "Zasedačka B", Color = "#22c55e" },
    })"
    @bind-CurrentDate="_date"
    OnSlotClick="CreateBooking" />

@* Agenda pohled *@
<TmScheduler Events="_events" View="SchedulerView.Agenda"
    @bind-CurrentDate="_date" OnEventClick="OpenDetail" ReadOnly="true" />
```

---

## Dashboard

### TmDashboard

Konfigurovatelný dashboard s widgety (drag & drop, resize).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `DashboardId` | `string?` | `null` | ID dashboardu |
| `UserId` | `string?` | `null` | ID uživatele |
| `Class` | `string?` | `null` | Další CSS třídy |
| `ShowTitle` | `bool` | `true` | Zobrazit název |
| `AllowEdit` | `bool` | `true` | Povolit úpravy |
| `OnDashboardChanged` | `EventCallback<DashboardLayout>` | — | Změna layoutu |
| `WidgetTemplate` | `RenderFragment<WidgetInstance>?` | `null` | Šablona widgetu |

#### Příklady

```razor
<TmDashboard DashboardId="main" UserId="@_userId" AllowEdit="true"
    OnDashboardChanged="SaveLayout">
    <WidgetTemplate Context="widget">
        @switch (widget.WidgetId)
        {
            case "sales-chart":
                <TmChart Type="ChartType.Line" Data="@_salesData" />
                break;
            case "recent-orders":
                <TmDataTable TItem="Order" Items="_recentOrders" ViewContext="widget-orders">
                    <TmDataTableColumn TItem="Order" Key="id" Title="#" Field="@(o => o.Id)" />
                    <TmDataTableColumn TItem="Order" Key="total" Title="Celkem" Field="@(o => o.Total)" />
                </TmDataTable>
                break;
            case "stats":
                <TmStatCard Title="Tržby" Value="@_totalRevenue" />
                break;
        }
    </WidgetTemplate>
</TmDashboard>
```

---

## Workflow

### TmStepper

Krokovací komponenta (wizard).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Steps` | `IReadOnlyList<StepItem>` | `[]` | Kroky |
| `ActiveStep` | `int` | `0` | Aktuální krok |
| `OnStepClick` | `EventCallback<int>` | — | Klik na krok |
| `Orientation` | `StepperOrientation` | `Horizontal` | `Horizontal`, `Vertical` |
| `Size` | `StepperSize` | `Medium` | `Small`, `Medium`, `Large` |
| `ShowCheckIcon` | `bool` | `true` | Ikona dokončení |
| `ShowConnectors` | `bool` | `true` | Spojovací čáry |
| `ShowDescriptions` | `bool` | `true` | Popisy kroků |
| `AllowStepClick` | `bool` | `false` | Klikání na kroky |
| `ActiveIcon` | `string?` | `null` | Ikona aktivního kroku |

#### Příklady

```razor
<TmStepper Steps="_steps" ActiveStep="_currentStep"
    Orientation="StepperOrientation.Horizontal"
    AllowStepClick="true" OnStepClick="GoToStep">
</TmStepper>

<TmStepper Steps="@(new List<StepItem>
{
    new() { Title = "Osobní údaje", Description = "Jméno, email" },
    new() { Title = "Adresa", Description = "Fakturační adresa" },
    new() { Title = "Platba", Description = "Způsob platby" },
    new() { Title = "Souhrn", Description = "Kontrola objednávky" },
})"
ActiveStep="2" Orientation="StepperOrientation.Vertical" Size="StepperSize.Small" />
```

### TmWorkflowDesignerCanvas

Vizuální editor workflow stavového automatu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `DefinitionChanged` | `EventCallback<WorkflowDefinition>` | — | Změna definice |
| `ShowGrid` | `bool` | `true` | Zobrazit mřížku |
| `GridSize` | `int` | `20` | Velikost mřížky |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |
| `OnStateSelected` | `EventCallback<string>` | — | Výběr stavu |
| `OnTransitionSelected` | `EventCallback<string>` | — | Výběr přechodu |
| `SelectedStateId` | `string?` | `null` | Vybraný stav |
| `SelectedTransitionId` | `string?` | `null` | Vybraný přechod |
| `Class` | `string?` | `null` | Další CSS třídy |
| `OnZoomLevelChanged` | `EventCallback<double>` | — | Změna zoomu |

#### Příklady

```razor
<TmWorkflowDesignerCanvas
    DefinitionChanged="HandleDefinitionChange"
    OnStateSelected="SelectState"
    OnTransitionSelected="SelectTransition"
    ShowGrid="true" GridSize="20" />
```

### TmWorkflowPropertiesPanel

Panel vlastností vybraného stavu/přechodu.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `SelectedState` | `WorkflowState?` | `null` | Vybraný stav |
| `SelectedTransition` | `WorkflowTransition?` | `null` | Vybraný přechod |
| `States` | `IReadOnlyList<WorkflowState>` | `[]` | Všechny stavy |
| `OnStateChanged` | `EventCallback<WorkflowState>` | — | Změna stavu |
| `OnTransitionChanged` | `EventCallback<WorkflowTransition>` | — | Změna přechodu |
| `ReadOnly` | `bool` | `false` | Pouze ke čtení |

### TmWorkflowToolbox

Toolbox pro workflow designer.

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `OnAddState` | `EventCallback` | — | Přidat stav |
| `OnDeleteSelected` | `EventCallback` | — | Smazat vybrané |
| `HasSelection` | `bool` | `false` | Má výběr |
| `ZoomLevel` | `double` | `1.0` | Úroveň zoomu |
| `OnZoomChanged` | `EventCallback<double>` | — | Změna zoomu |
| `OnFitToView` | `EventCallback` | — | Přizpůsobit pohled |

### TmWorkflowMinimap

Minimapa workflow.

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Definition` | `WorkflowDefinition` | — | Definice workflow |
| `ViewBoxX` | `double` | `0` | X pohledu |
| `ViewBoxY` | `double` | `0` | Y pohledu |
| `ViewBoxW` | `double` | `0` | Šířka pohledu |
| `ViewBoxH` | `double` | `0` | Výška pohledu |
| `OnNavigate` | `EventCallback<(double X, double Y)>` | — | Navigace |

---

## Activity (komentáře, přílohy, rich editor)

### TmActivityLog

Kompletní panel aktivity entity (timeline + komentáře + přílohy).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `EntityId` | `string` | — | ID entity |
| `TimelineEntries` | `IEnumerable<ITimelineEntry>?` | `null` | Timeline |
| `Comments` | `IReadOnlyList<ICommentEntry>?` | `null` | Komentáře |
| `Attachments` | `IReadOnlyList<IFileAttachment>?` | `null` | Přílohy |
| `AttachmentProvider` | `IFileAttachmentProvider?` | `null` | Provider příloh |
| `ShowTimeline` | `bool` | `true` | Zobrazit timeline |
| `ShowComments` | `bool` | `true` | Zobrazit komentáře |
| `ShowAttachments` | `bool` | `true` | Zobrazit přílohy |
| `OnCommentAdded` | `EventCallback<string>` | — | Přidání komentáře |
| `OnCommentDeleted` | `EventCallback<string>` | — | Smazání komentáře |

#### Příklady

```razor
<TmActivityLog EntityId="@_orderId"
    TimelineEntries="_timeline"
    Comments="_comments"
    Attachments="_attachments"
    AttachmentProvider="_attachmentProvider"
    OnCommentAdded="AddComment"
    OnCommentDeleted="DeleteComment" />
```

### TmActivityComments

Komentáře s přidáváním, editací a mazáním.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Comments` | `IReadOnlyList<ICommentEntry>` | `[]` | Komentáře |
| `OnCommentAdded` | `EventCallback<string>` | — | Přidání |
| `OnCommentDeleted` | `EventCallback<string>` | — | Smazání |
| `OnCommentEdited` | `EventCallback<(string Id, string NewText)>` | — | Editace |
| `ShowAddButton` | `bool` | `true` | Zobrazit přidávání |

### TmActivityAttachments

Přílohy entity s nahráváním.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `EntityId` | `string` | — | ID entity |
| `Provider` | `IFileAttachmentProvider` | — | Provider |
| `Attachments` | `IReadOnlyList<IFileAttachment>` | `[]` | Přílohy |
| `AllowUpload` | `bool` | `true` | Povolit nahrávání |
| `MaxFileSizeBytes` | `long` | — | Max. velikost |
| `AcceptedFileTypes` | `string?` | `null` | Povolené typy |

### TmActivityTimeline

Timeline záznamů aktivity.

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Entries` | `IEnumerable<ITimelineEntry>` | `[]` | Záznamy |

### TmRichEditorFull

Plnohodnotný rich text editor (WYSIWYG).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | HTML obsah |
| `ValueChanged` | `EventCallback<string>` | — | Změna obsahu |
| `Placeholder` | `string?` | `null` | Placeholder |
| `Height` | `string` | `"300px"` | Výška |
| `MaxLength` | `int?` | `null` | Max. znaků |
| `ShowWordCount` | `bool` | `false` | Počítadlo slov |
| `SupportsMentions` | `bool` | `false` | @zmínky |
| `MentionProvider` | `Func<string, Task<IEnumerable<MentionUser>>>?` | `null` | Provider zmínek |
| `SupportsImages` | `bool` | `false` | Obrázky |
| `SupportsTables` | `bool` | `false` | Tabulky |
| `SupportsCodeBlocks` | `bool` | `false` | Bloky kódu |
| `SupportsBlockquotes` | `bool` | `false` | Citace |
| `IsDisabled` | `bool` | `false` | Zakázáno |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmRichEditorFull @bind-Value="_content" Height="400px"
    SupportsMentions="true" MentionProvider="SearchUsers"
    SupportsImages="true" SupportsTables="true"
    SupportsCodeBlocks="true" ShowWordCount="true"
    Placeholder="Napište článek..." />
```

### TmRichEditorSimple

Jednoduchý rich text editor (pro komentáře).

#### Parametry

| Parametr | Typ | Výchozí | Popis |
|----------|-----|---------|-------|
| `Value` | `string` | `""` | HTML obsah |
| `ValueChanged` | `EventCallback<string>` | — | Změna |
| `Placeholder` | `string?` | `null` | Placeholder |
| `MaxLength` | `int?` | `null` | Max. znaků |
| `SupportsMentions` | `bool` | `false` | @zmínky |
| `MentionProvider` | `Func<string, Task<IEnumerable<MentionUser>>>?` | `null` | Provider |
| `IsDisabled` | `bool` | `false` | Zakázáno |
| `OnSubmit` | `EventCallback` | — | Odeslání (Enter) |
| `Class` | `string?` | `null` | Další CSS třídy |

#### Příklady

```razor
<TmRichEditorSimple @bind-Value="_comment"
    Placeholder="Napište komentář..."
    SupportsMentions="true" MentionProvider="SearchUsers"
    OnSubmit="SubmitComment" />
```

---

## Validace formulářů - kompletní příklady

### 1. FluentValidation - reaktivní validace na každé změně pole

Toto je doporučený přístup. Validace se spouští automaticky při každé změně pole díky napojení na `EditContext.OnFieldChanged`.

#### Registrace v Program.cs

```csharp
// Program.cs
builder.Services.AddTempoFluentValidation(typeof(PersonFormValidator).Assembly);
```

#### Model

```csharp
// Models/ContactFormModel.cs
public class ContactFormModel
{
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Department { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public bool AgreeToTerms { get; set; }
    public string Priority { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
}
```

#### Validátor

```csharp
// Validators/ContactFormValidator.cs
using FluentValidation;

public class ContactFormValidator : AbstractValidator<ContactFormModel>
{
    public ContactFormValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("Jméno je povinné.")
            .MaximumLength(50).WithMessage("Jméno může mít max. 50 znaků.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Příjmení je povinné.")
            .MaximumLength(50).WithMessage("Příjmení může mít max. 50 znaků.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email je povinný.")
            .EmailAddress().WithMessage("Neplatná emailová adresa.");

        RuleFor(x => x.Phone)
            .Matches(@"^\+?\d{9,15}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone))
            .WithMessage("Neplatné telefonní číslo.");

        RuleFor(x => x.Age)
            .InclusiveBetween(18, 65)
            .WithMessage("Věk musí být mezi 18 a 65.");

        RuleFor(x => x.Department)
            .NotEmpty().WithMessage("Oddělení je povinné.");

        RuleFor(x => x.Notes)
            .MaximumLength(500)
            .WithMessage("Poznámky mohou mít max. 500 znaků.");

        RuleFor(x => x.StartDate)
            .NotNull().WithMessage("Datum zahájení je povinné.");

        RuleFor(x => x.EndDate)
            .GreaterThan(x => x.StartDate)
            .When(x => x.EndDate.HasValue && x.StartDate.HasValue)
            .WithMessage("Datum ukončení musí být po datu zahájení.");

        RuleFor(x => x.AgreeToTerms)
            .Equal(true).WithMessage("Musíte souhlasit s podmínkami.");

        RuleFor(x => x.Priority)
            .NotEmpty().WithMessage("Vyberte prioritu.");

        RuleFor(x => x.Tags)
            .Must(t => t.Count >= 1).WithMessage("Vyberte alespoň jeden štítek.");
    }
}
```

#### Kompletní formulář se všemi input komponentami

```razor
@page "/contact-form"
@using FluentValidation

<h2>Kontaktní formulář</h2>

<EditForm Model="_model" OnValidSubmit="HandleValidSubmit" OnInvalidSubmit="HandleInvalidSubmit">
    <FluentValidationValidator />

    @* Souhrn chyb nahoře *@
    <TmValidationSummary Title="Formulář obsahuje chyby:" />

    @* ===== SEKCE 1: Osobní údaje ===== *@
    <TmFormSection Title="Osobní údaje">

        @* TmValidatedField - textový vstup s automatickou validací *@
        <TmFormRow>
            <TmValidatedField Label="Jméno" Required="true"
                @bind-Value="_model.FirstName"
                Placeholder="Zadejte jméno" />

            <TmValidatedField Label="Příjmení" Required="true"
                @bind-Value="_model.LastName"
                Placeholder="Zadejte příjmení" />
        </TmFormRow>

        @* TmTextInput s ruční validační zprávou *@
        <TmFormRow>
            <div>
                <TmTextInput @bind-Value="_model.Email" Label="Email" Required="true"
                    Type="email" LeftIcon="@IconNames.Mail"
                    Placeholder="user@example.com" />
                <TmFormValidationMessage For="() => _model.Email" />
            </div>

            <div>
                <TmTextInput @bind-Value="_model.Phone" Label="Telefon"
                    LeftIcon="@IconNames.Phone"
                    Placeholder="+420 123 456 789" />
                <TmFormValidationMessage For="() => _model.Phone" />
            </div>
        </TmFormRow>

        @* TmNumberInput *@
        <TmFormRow>
            <TmFormField Label="Věk" Required="true" ErrorMessage="@GetError("Age")">
                <TmNumberInput @bind-Value="_model.Age" Min="0" Max="120" Suffix="let" />
            </TmFormField>
        </TmFormRow>

    </TmFormSection>

    @* ===== SEKCE 2: Pracovní údaje ===== *@
    <TmFormSection Title="Pracovní údaje">

        @* TmSelect *@
        <TmFormRow>
            <div>
                <TmSelect TValue="string" @bind-Value="_model.Department"
                    Label="Oddělení" Required="true" Placeholder="Vyberte oddělení"
                    Options="_departmentOptions" />
                <TmFormValidationMessage For="() => _model.Department" />
            </div>

            @* TmRadioGroup *@
            <div>
                <TmRadioGroup TValue="string" @bind-Value="_model.Priority"
                    Label="Priorita" Layout="RadioLayout.Horizontal"
                    Options="@(new List<RadioOption<string>>
                    {
                        new("Nízká", "low"),
                        new("Střední", "medium"),
                        new("Vysoká", "high"),
                    })" />
                <TmFormValidationMessage For="() => _model.Priority" />
            </div>
        </TmFormRow>

        @* TmDatePicker *@
        <TmFormRow>
            <div>
                <TmDatePicker @bind-Value="_model.StartDate"
                    Label="Datum zahájení" Required="true" />
                <TmFormValidationMessage For="() => _model.StartDate" />
            </div>

            <div>
                <TmDatePicker @bind-Value="_model.EndDate"
                    Label="Datum ukončení" MinDate="_model.StartDate" />
                <TmFormValidationMessage For="() => _model.EndDate" />
            </div>
        </TmFormRow>

        @* TmMultiSelect *@
        <div>
            <TmMultiSelect TItem="string" TValue="string"
                @bind-Values="_model.Tags"
                Items="@(new[] { "C#", "Blazor", "SQL", "DevOps", "UI/UX", "PM" })"
                DisplayField="@(t => t)" ValueField="@(t => t)"
                Label="Štítky" Required="true"
                Placeholder="Vyberte dovednosti"
                ShowSelectAll="true" />
            <TmFormValidationMessage For="() => _model.Tags" />
        </div>

    </TmFormSection>

    @* ===== SEKCE 3: Doplňující údaje ===== *@
    <TmFormSection Title="Doplňující údaje">

        @* TmTextArea *@
        <div>
            <TmTextArea @bind-Value="_model.Notes" Label="Poznámky"
                Rows="4" MaxLength="500"
                Placeholder="Volitelné poznámky..." />
            <TmFormValidationMessage For="() => _model.Notes" />
        </div>

        @* TmCheckbox *@
        <div>
            <TmCheckbox @bind-Value="_model.AgreeToTerms"
                Label="Souhlasím s obchodními podmínkami" />
            <TmFormValidationMessage For="() => _model.AgreeToTerms" />
        </div>

    </TmFormSection>

    @* Odeslání *@
    <div class="flex items-center gap-3 mt-6 pt-6 border-t border-slate-200">
        <TmButton Type="ButtonType.Submit" Variant="ButtonVariant.Primary"
            IsLoading="_isSubmitting">
            Odeslat formulář
        </TmButton>
        <TmButton Type="ButtonType.Button" Variant="ButtonVariant.Ghost"
            OnClick="ResetForm">
            Resetovat
        </TmButton>
    </div>

</EditForm>

@if (_submitSuccess)
{
    <TmAlert Severity="AlertSeverity.Success" Title="Odesláno!" Dismissable="true"
        OnDismiss="() => _submitSuccess = false">
        Formulář byl úspěšně odeslán.
    </TmAlert>
}

@code {
    private ContactFormModel _model = new();
    private bool _isSubmitting;
    private bool _submitSuccess;
    private EditContext? _editContext;

    private readonly List<SelectOption<string>> _departmentOptions =
    [
        SelectOption<string>.From("dev", "Vývoj"),
        SelectOption<string>.From("qa", "Testování"),
        SelectOption<string>.From("pm", "Projektový management"),
        SelectOption<string>.From("hr", "HR"),
        SelectOption<string>.From("sales", "Obchod"),
    ];

    private async Task HandleValidSubmit()
    {
        _isSubmitting = true;
        await Task.Delay(1000); // simulace API
        _submitSuccess = true;
        _isSubmitting = false;
    }

    private void HandleInvalidSubmit()
    {
        // Chyby se zobrazí automaticky přes TmValidationSummary a TmFormValidationMessage
    }

    private void ResetForm()
    {
        _model = new();
        _submitSuccess = false;
    }

    // Helper pro zobrazení chyby konkrétního pole (pro TmFormField)
    private string? GetError(string fieldName)
    {
        if (_editContext is null) return null;
        var field = _editContext.Field(fieldName);
        var messages = _editContext.GetValidationMessages(field);
        return messages.FirstOrDefault();
    }
}
```

### 2. Manuální real-time validace (bez EditForm)

Pro scénáře kde nepotřebujete EditForm a chcete plnou kontrolu nad validací.

```razor
@page "/manual-validation"

<h2>Registrace - manuální validace</h2>

@* TmTextInput s IsValid a ShowValidationIcons *@
<TmTextInput @bind-Value="_username" Label="Uživatelské jméno" Required="true"
    ShowValidationIcons="true"
    IsValid="@GetUsernameValidity()"
    Error="@GetUsernameError()"
    HelpText="3-20 znaků, pouze písmena a čísla"
    OnBlur="ValidateUsername" />

<TmTextInput @bind-Value="_email" Label="Email" Required="true" Type="email"
    LeftIcon="@IconNames.Mail"
    ShowValidationIcons="true"
    IsValid="@GetEmailValidity()"
    Error="@GetEmailError()" />

<TmTextInput @bind-Value="_password" Label="Heslo" Required="true" Type="password"
    ShowValidationIcons="true"
    IsValid="@GetPasswordValidity()"
    Error="@GetPasswordError()"
    HelpText="Min. 8 znaků, velké a malé písmeno, číslo" />

<TmTextInput @bind-Value="_passwordConfirm" Label="Potvrzení hesla" Required="true" Type="password"
    ShowValidationIcons="true"
    IsValid="@(_passwordConfirm.Length > 0 ? _passwordConfirm == _password : null)"
    Error="@(_passwordConfirm.Length > 0 && _passwordConfirm != _password ? "Hesla se neshodují" : null)" />

<TmNumberInput @bind-Value="_age" Label="Věk" Required="true" Min="18" Max="120"
    Error="@(_age.HasValue && (_age < 18 || _age > 120) ? "Věk musí být 18-120" : null)" />

<TmSelect TValue="string" @bind-Value="_country" Label="Země" Required="true"
    Placeholder="Vyberte zemi"
    Error="@(_formSubmitted && _country is null ? "Vyberte zemi" : null)"
    Options="_countryOptions" />

<TmCheckbox @bind-Value="_agreeTerms" Label="Souhlasím s podmínkami" />
@if (_formSubmitted && !_agreeTerms)
{
    <span class="text-red-500 text-sm">Musíte souhlasit s podmínkami</span>
}

<TmToggle @bind-Value="_newsletter" Label="Odebírat newsletter" />

<TmButton Variant="ButtonVariant.Primary" OnClick="SubmitManual"
    Disabled="@(!IsFormValid())">
    Registrovat
</TmButton>

@code {
    private string _username = "";
    private string _email = "";
    private string _password = "";
    private string _passwordConfirm = "";
    private int? _age;
    private string? _country;
    private bool _agreeTerms;
    private bool _newsletter;
    private bool _formSubmitted;

    private bool? GetUsernameValidity() =>
        _username.Length == 0 ? null :
        _username.Length >= 3 && _username.Length <= 20 &&
        System.Text.RegularExpressions.Regex.IsMatch(_username, @"^[a-zA-Z0-9]+$");

    private string? GetUsernameError() =>
        _username.Length == 0 ? null :
        _username.Length < 3 ? "Min. 3 znaky" :
        _username.Length > 20 ? "Max. 20 znaků" :
        !System.Text.RegularExpressions.Regex.IsMatch(_username, @"^[a-zA-Z0-9]+$")
            ? "Pouze písmena a čísla" : null;

    private bool? GetEmailValidity() =>
        _email.Length == 0 ? null : _email.Contains("@") && _email.Contains(".");

    private string? GetEmailError() =>
        _email.Length == 0 ? null :
        !_email.Contains("@") || !_email.Contains(".") ? "Neplatný email" : null;

    private bool? GetPasswordValidity()
    {
        if (_password.Length == 0) return null;
        return _password.Length >= 8 &&
            _password.Any(char.IsUpper) &&
            _password.Any(char.IsLower) &&
            _password.Any(char.IsDigit);
    }

    private string? GetPasswordError()
    {
        if (_password.Length == 0) return null;
        if (_password.Length < 8) return "Min. 8 znaků";
        if (!_password.Any(char.IsUpper)) return "Chybí velké písmeno";
        if (!_password.Any(char.IsLower)) return "Chybí malé písmeno";
        if (!_password.Any(char.IsDigit)) return "Chybí číslo";
        return null;
    }

    private bool IsFormValid() =>
        GetUsernameValidity() == true &&
        GetEmailValidity() == true &&
        GetPasswordValidity() == true &&
        _passwordConfirm == _password &&
        _age is >= 18 and <= 120 &&
        _country is not null &&
        _agreeTerms;

    private void ValidateUsername(FocusEventArgs e)
    {
        // Validace při opuštění pole (onblur)
        StateHasChanged();
    }

    private void SubmitManual()
    {
        _formSubmitted = true;
        if (!IsFormValid()) return;
        // ... odeslat data
    }

    private readonly List<SelectOption<string>> _countryOptions =
    [
        SelectOption<string>.From("cz", "Česká republika"),
        SelectOption<string>.From("sk", "Slovensko"),
        SelectOption<string>.From("de", "Německo"),
    ];
}
```

### 3. Programatická FluentValidation (bez komponenty)

```csharp
// Programatické napojení FluentValidation
var editContext = new EditContext(model);
editContext.AddFluentValidation(serviceProvider);
// Nyní validace reaguje na OnFieldChanged a OnValidationRequested automaticky
```

---

## Přehled enumů

### ButtonType
`Button` | `Submit` | `Reset`

### ButtonVariant
`Primary` | `Secondary` | `Ghost` | `Danger` | `Outline` | `Link` | `Default`

### ButtonSize
`Xs` | `Sm` | `Md` | `Lg`

### AlertSeverity
`Info` | `Success` | `Warning` | `Error`

### AlertVariant
`Soft` | `Filled` | `Outlined`

### BadgeVariant
`Default` | `Primary` | `Success` | `Danger` | `Warning` | `Info`

### BadgeSize / BadgeStyle
`Sm` | `Md` — `Filled` | `Outline` | `Subtle`

### ModalSize
`Small` | `Medium` | `Large` | `XLarge` | `Fullscreen`

### ModalPosition
`Center` | `Top` | `Bottom`

### DialogType
`Alert` | `Confirm` | `Prompt` | `Custom`

### DialogVariant
`Info` | `Success` | `Warning` | `Error`

### SpinnerSize
`Xs` | `Sm` | `Md` | `Lg`

### SpinnerColor
`Current` | `Primary` | `White`

### IconSize
`Xs` | `Sm` | `Md` | `Lg` | `Xl`

### IconColor
`Current` | `Primary` | `Danger` | `Success` | `Warning` | `Muted`

### AvatarSize
`Xs` | `Sm` | `Md` | `Lg` | `Xl` | `Xxl`

### AvatarShape
`Circle` | `Square`

### AvatarColor
`Gray` | `Red` | `Orange` | `Green` | `Blue` | `Purple` | `Pink`

### TabVariant
`Line` | `Pill` | `Enclosed`

### CardVariant
`Default` | `Elevated` | `Outlined`

### TooltipPosition / PopoverPosition
`Top` | `Bottom` | `Left` | `Right`

### MultiSelectMode
`Chip` | `Delimiter` | `CheckBox`

### RadioLayout
`Vertical` | `Horizontal`

### DataTableScrollMode
`Pagination` | `Virtualized`

### ColumnAlign
`Left` | `Center` | `Right`

### DrawerPosition
`Right` | `Left`

### ChartType
`Bar` | `Line` | `Pie` | `Donut` | `HorizontalBar`

### ProgressBarVariant
`Default` | `Success` | `Warning` | `Error` | `Gradient`

### SkeletonVariant
`Text` | `Circle` | `Rect`

### StepperOrientation
`Horizontal` | `Vertical`

### FilterFieldType
`Text` | `Number` | `Date` | `DateTime` | `Boolean` | `Select` | `MultiSelect`

### FilterOperator
`Contains` | `NotContains` | `Equals` | `NotEquals` | `GreaterThan` | `LessThan` | `GreaterOrEqual` | `LessOrEqual` | `Between` | `IsEmpty` | `IsNotEmpty` | `In` | `NotIn`
