using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Components.Inputs;

/// <summary>
/// A multi-select dropdown component supporting both client-side and server-side data.
/// Allows users to select multiple values with search and filtering capabilities.
/// </summary>
public partial class TmMultiSelect<TItem, TValue>
{
    // ── Data Binding ──────────────────────────────────────────────

    /// <summary>Currently selected values.</summary>
    [Parameter] public IReadOnlyList<TValue> Values { get; set; } = [];

    /// <summary>Fires when the selection changes.</summary>
    [Parameter] public EventCallback<IReadOnlyList<TValue>> ValuesChanged { get; set; }

    // ── Data Source ───────────────────────────────────────────────

    /// <summary>Static list of items. Ignored when DataProvider is set.</summary>
    [Parameter] public IEnumerable<TItem> Items { get; set; } = [];

    /// <summary>Server-side data provider. When set, Items is ignored.</summary>
    [Parameter] public IDropdownDataProvider<TItem>? DataProvider { get; set; }

    // ── Field Selectors ──────────────────────────────────────────

    /// <summary>Extracts display text from an item.</summary>
    [Parameter, EditorRequired] public Func<TItem, string> DisplayField { get; set; } = default!;

    /// <summary>Extracts the value from an item.</summary>
    [Parameter, EditorRequired] public Func<TItem, TValue> ValueField { get; set; } = default!;

    /// <summary>Extracts a string key for @key and ExcludedIds. Defaults to ValueField.ToString().</summary>
    [Parameter] public Func<TItem, string>? IdField { get; set; }

    /// <summary>Extracts a group name for grouped display.</summary>
    [Parameter] public Func<TItem, string?>? GroupField { get; set; }

    // ── Display Mode ─────────────────────────────────────────────

    /// <summary>Visual mode for selected items. Default: Chip.</summary>
    [Parameter] public MultiSelectMode Mode { get; set; } = MultiSelectMode.Chip;

    /// <summary>Delimiter string for Delimiter mode. Default: ", ".</summary>
    [Parameter] public string Delimiter { get; set; } = ", ";

    // ── Standard Form Parameters ─────────────────────────────────

    /// <summary>Label shown above the component.</summary>
    [Parameter] public string? Label { get; set; }

    /// <summary>
    /// When true, <see cref="Label"/> is not rendered as a visible <c>&lt;label&gt;</c>. It stays the
    /// accessible name of the trigger and of the popup, unless <see cref="AriaLabel"/> overrides it.
    /// Default false, so an existing multi-select still shows its label.
    /// </summary>
    [Parameter] public bool HideLabel { get; set; }

    /// <summary>
    /// Accessible name of the trigger and of the popup. Null falls back to <see cref="Label"/>, which
    /// is what every existing multi-select announces.
    /// </summary>
    [Parameter] public string? AriaLabel { get; set; }

    /// <summary>Placeholder text when no items are selected.</summary>
    [Parameter] public string? Placeholder { get; set; }

    /// <summary>
    /// How the option popup presents on a narrow viewport. Default
    /// <see cref="PanelPresentation.Popover"/>, the anchored popup at every width, so an existing
    /// multi-select is unchanged. <see cref="PanelPresentation.Auto"/> follows
    /// <see cref="LayoutMode"/>: a content-height bottom sheet on mobile (the F3 sheet), the anchored
    /// popup otherwise.
    /// </summary>
    [Parameter] public PanelPresentation MobilePresentation { get; set; } = PanelPresentation.Popover;

    /// <summary>
    /// The layout <see cref="MobilePresentation"/> resolves from. <see cref="TmLayoutMode.Auto"/>
    /// follows the viewport scope; a forced value never measures, so a test can render a mode
    /// without a DOM. Ignored unless <see cref="MobilePresentation"/> is
    /// <see cref="PanelPresentation.Auto"/>.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>Validation error message.</summary>
    [Parameter] public string? Error { get; set; }

    /// <summary>
    /// When <c>true</c> (default), the rendered <see cref="Error"/> message carries
    /// <c>role="alert"</c> so screen readers announce it assertively the moment it appears.
    /// Set <c>false</c> when the host moves focus to the invalid field itself (typically a modal
    /// submit that focuses the first invalid control): the error is then read through
    /// <c>aria-describedby</c> on the trigger, and the live region would announce the same text a
    /// second time. With <c>false</c> the message still renders, keeps its error styling and stays
    /// referenced by <c>aria-describedby</c>; only the <c>role</c> attribute is dropped.
    /// <para>
    /// Only safe when the same render that shows the error also moves focus to this control. Keep
    /// the default for validation on blur or on input, async/server validation, or when focus
    /// stays on the submit button — otherwise the error is not announced at all. With several
    /// invalid fields, pair it with a summary (e.g. TmValidationSummary) or focus the first
    /// invalid field.
    /// </para>
    /// </summary>
    [Parameter] public bool AnnounceError { get; set; } = true;

    /// <summary>Help text shown below when no error is present.</summary>
    [Parameter] public string? HelpText { get; set; }

    /// <summary>HTML id attribute.</summary>
    [Parameter] public string Id { get; set; } = Guid.NewGuid().ToString("n")[..8];

    /// <summary>Additional CSS class(es).</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Disables the component.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Additional attributes applied to the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)] public Dictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>Marks the field as required.</summary>
    [Parameter] public bool Required { get; set; }

    // ── Accessibility ────────────────────────────────────────────

    /// <summary>The name the trigger and the popup announce: <see cref="AriaLabel"/>, else the label.</summary>
    private string? EffectiveAriaLabel => !string.IsNullOrEmpty(AriaLabel) ? AriaLabel : Label;

    private bool IsInvalid => !string.IsNullOrEmpty(Error);
    private string ErrorId => $"{Id}-error";
    private string HelpId => $"{Id}-help";

    /// <summary>
    /// Id of the error message a surrounding <c>TmFormField</c> currently renders (cascaded as
    /// <c>TmFormField.ErrorIdCascadingName</c>; null when the field shows no error). With no own
    /// <see cref="Error"/> the trigger describes the field-level message (after its help text),
    /// so a single <c>TmFormField</c> error is reachable from the wrapped control too.
    /// </summary>
    [CascadingParameter(Name = Tempo.Blazor.Components.Forms.TmFormField.ErrorIdCascadingName)]
    private string? FormFieldErrorId { get; set; }

    private string? DescribedBy
    {
        get
        {
            if (!string.IsNullOrEmpty(Error)) return ErrorId;
            if (FormFieldErrorId is { Length: > 0 } fieldError)
                return !string.IsNullOrEmpty(HelpText) ? $"{HelpId} {fieldError}" : fieldError;
            return !string.IsNullOrEmpty(HelpText) ? HelpId : null;
        }
    }

    // ── Filtering ────────────────────────────────────────────────

    /// <summary>Enables the search input in the dropdown. Default: true.</summary>
    [Parameter] public bool AllowFiltering { get; set; } = true;

    /// <summary>Placeholder for the filter input.</summary>
    [Parameter] public string? FilterPlaceholder { get; set; }

    /// <summary>Debounce delay in ms for server-side filtering. Default: 300.</summary>
    [Parameter] public int Debounce { get; set; } = 300;

    // ── Selection Behavior ───────────────────────────────────────

    /// <summary>Maximum selectable items. 0 = unlimited.</summary>
    [Parameter] public int MaxSelectionCount { get; set; }

    /// <summary>Shows Select All / Deselect All control.</summary>
    [Parameter] public bool ShowSelectAll { get; set; }

    /// <summary>Hides already-selected items from the dropdown.</summary>
    [Parameter] public bool HideSelectedItems { get; set; }

    /// <summary>Shows a clear-all button. Default: true.</summary>
    [Parameter] public bool ShowClearButton { get; set; } = true;

    /// <summary>Shows a confirmation button in the dropdown footer to close the popup. Default: false.</summary>
    [Parameter] public bool ShowConfirmButton { get; set; }

    /// <summary>Shows checkboxes next to items.</summary>
    [Parameter] public bool ShowCheckBox { get; set; }

    // ── Templates ────────────────────────────────────────────────

    /// <summary>Custom template for each item in the dropdown.</summary>
    [Parameter] public RenderFragment<TItem>? ItemTemplate { get; set; }

    /// <summary>Custom template for each selected value chip.</summary>
    [Parameter] public RenderFragment<TItem>? ValueTemplate { get; set; }

    /// <summary>Template shown when no items match.</summary>
    [Parameter] public RenderFragment? NoRecordsTemplate { get; set; }

    /// <summary>Popup header content.</summary>
    [Parameter] public RenderFragment? HeaderTemplate { get; set; }

    /// <summary>Popup footer content.</summary>
    [Parameter] public RenderFragment? FooterTemplate { get; set; }

    // ── Messages ─────────────────────────────────────────────────

    /// <summary>Loading message.</summary>
    [Parameter] public string? LoadingMessage { get; set; }

    /// <summary>Empty results message.</summary>
    [Parameter] public string? EmptyMessage { get; set; }

    /// <summary>Error message.</summary>
    [Parameter] public string? ErrorMessage { get; set; }

    // ── Callbacks ────────────────────────────────────────────────

    /// <summary>Fires when the dropdown opens.</summary>
    [Parameter] public EventCallback OnOpen { get; set; }

    /// <summary>Fires when the dropdown closes.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    /// <summary>Fires when the filter text changes.</summary>
    [Parameter] public EventCallback<string> OnFiltering { get; set; }

    // ── Internal State ───────────────────────────────────────────

    private bool _isOpen;
    private bool _isLoading;
    private bool _hasError;
    private string _filterText = string.Empty;
    private int _focusedIndex = -1;
    private List<TItem> _providerItems = [];
    private CancellationTokenSource? _debounceToken;
    private ElementReference _filterInputRef;
    private ElementReference _triggerRef;
    private bool _focusFilterPending;
    private bool _focusTriggerAfterClose;

    // ── Lifecycle ────────────────────────────────────────────────

    /// <summary>Arms the filter-input focus once the popup is actually displayed.</summary>
    private void HandlePanelOpened()
    {
        _focusFilterPending = true;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Opening moved DOM focus into the filter input so the user can type
        // immediately; the same move means every close destroys the focused
        // element, which is why the close paths restore to the trigger.
        if (_focusFilterPending && _isOpen)
        {
            _focusFilterPending = false;
            if (AllowFiltering)
            {
                try
                {
                    await _filterInputRef.FocusAsync();
                }
                catch (InvalidOperationException)
                {
                    // Best-effort: the element/JS may not be ready (e.g. prerender). Ignore.
                }
            }
        }

        // Focus was inside the popup when it closed — return it to the
        // trigger combobox instead of dropping it to <body> (WCAG 2.4.3).
        // Mirrors TmColorPicker's close-restore pattern.
        if (_focusTriggerAfterClose)
        {
            _focusTriggerAfterClose = false;
            try
            {
                await _triggerRef.FocusAsync(preventScroll: true);
            }
            catch (InvalidOperationException)
            {
                // Best-effort, same as above.
            }
        }
    }

    // ── Computed ──────────────────────────────────────────────────

    private IReadOnlyList<TItem> SelectedItems
    {
        get
        {
            if (Values.Count == 0) return [];
            var source = DataProvider is not null ? _providerItems : Items;
            var comparer = EqualityComparer<TValue>.Default;
            var selectedSet = new HashSet<TValue>(Values, comparer);
            return source.Where(item => selectedSet.Contains(ValueField(item))).ToList();
        }
    }

    private bool IsSelected(TItem item)
    {
        var val = ValueField(item);
        return Values.Any(v => EqualityComparer<TValue>.Default.Equals(v, val));
    }

    private bool IsMaxReached => MaxSelectionCount > 0 && Values.Count >= MaxSelectionCount;

    // aria-activedescendant target for the currently-highlighted option. The
    // option ids are generated in RenderOption as "{Id}-opt-{visible index}".
    private string? ActiveOptionId =>
        HasListbox && _focusedIndex >= 0 && _focusedIndex < VisibleCount
            ? $"{Id}-opt-{_focusedIndex}"
            : null;

    /// <summary>
    /// The options container exists only while options render. Every idref that names it
    /// (aria-controls, aria-activedescendant) is omitted otherwise, so a closed, empty,
    /// loading or error popup never points at an id that is not in the document.
    /// </summary>
    private bool HasListbox => _isOpen && !_isLoading && !_hasError && VisibleCount > 0;

    private int VisibleCount => GetVisibleItems().Count();

    private string? ListboxIdRef => HasListbox ? $"{Id}-listbox" : null;

    private string? ListboxAriaLabel => string.IsNullOrEmpty(EffectiveAriaLabel) ? null : EffectiveAriaLabel;

    private string? ListboxLabelledBy =>
        ListboxAriaLabel is null && !string.IsNullOrEmpty(Id) ? Id : null;

    /// <summary>
    /// Sheet mode with no filter has no focusable control inside the popup, so the listbox
    /// itself is the tab stop and carries aria-activedescendant. Everywhere else the combobox
    /// or the filter owns that relationship and the listbox stays out of the tab order.
    /// </summary>
    private bool ListboxIsSheetTabStop => !AllowFiltering && IsSheetPresentation;

    /// <summary>
    /// The sheet decision follows the panel, not this component's <see cref="LayoutMode"/>
    /// parameter. With <see cref="PanelPresentation.Auto"/> and <see cref="TmLayoutMode.Auto"/> the
    /// parameter stays Auto while the panel resolves Mobile from the viewport cascade; reading the
    /// parameter left the listbox without a tab stop on a real phone.
    /// </summary>
    private bool IsSheetPresentation => _panel?.IsSheet
        ?? (MobilePresentation == PanelPresentation.Sheet
            || (MobilePresentation == PanelPresentation.Auto && LayoutMode == TmLayoutMode.Mobile));

    private TmOverlayPanel? _panel;

    private Task OnPanelLayoutResolved(TmLayoutMode _)
    {
        // The panel reports a resolved layout only after it changes, so the listbox attributes
        // (tabindex, aria-activedescendant) catch up on the next render.
        StateHasChanged();
        return Task.CompletedTask;
    }

    private string? ListboxTabIndex => ListboxIsSheetTabStop ? "0" : null;

    private bool ListboxOwnsActiveDescendant => ListboxIsSheetTabStop;

    private string? SheetFocusSelector => AllowFiltering
        ? ".tm-multiselect__filter-input"
        : ".tm-multiselect__options[role='listbox']";

    private string GetItemId(TItem item)
    {
        if (IdField is not null) return IdField(item);
        return ValueField(item)?.ToString() ?? Guid.NewGuid().ToString();
    }

    private string GetTriggerCss()
    {
        var parts = new List<string>();
        if (Disabled) parts.Add("tm-multiselect--disabled");
        if (!string.IsNullOrEmpty(Error)) parts.Add("tm-multiselect--error");
        if (_isOpen) parts.Add("tm-multiselect--open");
        return string.Join(' ', parts);
    }

    // ── Visible Items ────────────────────────────────────────────

    private IEnumerable<TItem> GetVisibleItems()
    {
        var source = DataProvider is not null ? _providerItems : Items;
        IEnumerable<TItem> items = source;

        // Client-side text filter
        if (DataProvider is null && !string.IsNullOrWhiteSpace(_filterText))
        {
            var search = _filterText.ToLowerInvariant();
            items = items.Where(i => DisplayField(i).ToLowerInvariant().Contains(search));
        }

        // Hide already-selected
        if (HideSelectedItems && Values.Count > 0)
        {
            var selectedSet = new HashSet<TValue>(Values, EqualityComparer<TValue>.Default);
            items = items.Where(i => !selectedSet.Contains(ValueField(i)));
        }

        return items;
    }

    // ── Actions ──────────────────────────────────────────────────

    private async Task ToggleDropdownAsync()
    {
        if (Disabled) return;
        _isOpen = !_isOpen;
        if (_isOpen)
        {
            _filterText = string.Empty;
            _focusedIndex = ListboxIsSheetTabStop && VisibleCount > 0 ? 0 : -1;
            // The filter focus is armed from OnOpened (HandlePanelOpened) — once overlay.js
            // open() ran showPopover() — not here: this render's OnAfterRenderAsync can run
            // before the panel is displayed, and the move is dropped (UX review round 2, m3).
            if (DataProvider is not null)
                await LoadFromProviderAsync();
            await OnOpen.InvokeAsync();
        }
        else
        {
            // Focus was inside the popup (filter input / a popup control) —
            // restore to the trigger.
            _focusTriggerAfterClose = true;
            await OnClose.InvokeAsync();
        }
    }

    private async Task LoadFromProviderAsync()
    {
        _isLoading = true;
        _hasError = false;
        StateHasChanged();
        try
        {
            var req = new DropdownSearchRequest
            {
                SearchText = _filterText,
                Page = 1,
                PageSize = 50
            };
            var result = await DataProvider!.GetItemsAsync(req, CancellationToken.None);
            _providerItems = result.Items.ToList();
        }
        catch
        {
            _hasError = true;
            _providerItems.Clear();
        }
        finally
        {
            _isLoading = false;
            StateHasChanged();
        }
    }

    private async Task ToggleItemAsync(TItem item)
    {
        if (Disabled) return;
        var val = ValueField(item);
        var list = new List<TValue>(Values);

        if (IsSelected(item))
        {
            list.RemoveAll(v => EqualityComparer<TValue>.Default.Equals(v, val));
        }
        else
        {
            if (IsMaxReached) return;
            list.Add(val);
        }

        Values = list;
        await ValuesChanged.InvokeAsync(list);

        // In plain chip/delimiter modes (no checkboxes), close after selection
        if (!ShowCheckBox && Mode != MultiSelectMode.CheckBox)
        {
            _isOpen = false;
            _focusTriggerAfterClose = true;
        }
    }

    private async Task RemoveItemAsync(TValue val)
    {
        if (Disabled) return;
        var list = Values.Where(v => !EqualityComparer<TValue>.Default.Equals(v, val)).ToList();
        Values = list;
        await ValuesChanged.InvokeAsync(list);
    }

    private async Task ClearAllAsync()
    {
        if (Disabled) return;
        Values = [];
        await ValuesChanged.InvokeAsync(Array.Empty<TValue>());
    }

    private async Task SelectAllAsync()
    {
        if (Disabled) return;
        var allValues = GetVisibleItems().Select(ValueField).ToList();
        if (MaxSelectionCount > 0)
            allValues = allValues.Take(MaxSelectionCount).ToList();
        Values = allValues;
        await ValuesChanged.InvokeAsync(allValues);
    }

    private Task DeselectAllAsync() => ClearAllAsync();

    private async Task ConfirmSelectionAsync()
    {
        _isOpen = false;
        _focusTriggerAfterClose = true;
        await OnClose.InvokeAsync();
    }

    /// <summary>
    /// JS-driven dismissal (Escape or outside pointerdown from overlay.js). Focus restoration is
    /// owned by overlay.js itself: Escape refocuses the anchor, while an outside pointerdown
    /// refocuses it only when the target cannot take focus — a click into another field must keep
    /// the focus it just earned, so this path must not set <see langword="_focusTriggerAfterClose"/>.
    /// </summary>
    private async Task SetOpenFromJsAsync(bool open)
    {
        if (open == _isOpen)
            return;
        _isOpen = open;
        if (!open)
            await OnClose.InvokeAsync();
        else
        {
            _focusedIndex = ListboxIsSheetTabStop && VisibleCount > 0 ? 0 : -1;
            await OnOpen.InvokeAsync();
        }
    }

    // ── Filtering ────────────────────────────────────────────────

    private async Task HandleFilterInputAsync(ChangeEventArgs e)
    {
        _filterText = e.Value?.ToString() ?? string.Empty;
        _focusedIndex = -1;
        await OnFiltering.InvokeAsync(_filterText);

        if (DataProvider is not null)
        {
            _debounceToken?.Cancel();
            _debounceToken = new CancellationTokenSource();
            try
            {
                await Task.Delay(Debounce, _debounceToken.Token);
                await LoadFromProviderAsync();
            }
            catch (TaskCanceledException) { /* debounce cancelled */ }
        }
    }

    // ── Keyboard ─────────────────────────────────────────────────

    private async Task HandleTriggerKeyDownAsync(KeyboardEventArgs e)
    {
        switch (e.Key)
        {
            // With AllowFiltering=false there is no focusable element inside the
            // popup, so DOM focus stays on this combobox and the combobox
            // carries the option navigation (aria-activedescendant pattern):
            // arrows move _focusedIndex, Enter/Space toggles that option. The
            // trigger is a non-native focusable — this emulation is legitimate.
            case "Enter" or " " when _isOpen && !AllowFiltering && _focusedIndex >= 0:
            {
                var items = GetVisibleItems().ToList();
                if (_focusedIndex < items.Count)
                    await ToggleItemAsync(items[_focusedIndex]);
                break;
            }
            case "Enter" or " ":
                await ToggleDropdownAsync();
                break;
            case "ArrowDown" when _isOpen && !AllowFiltering:
                _focusedIndex = Math.Min(_focusedIndex + 1, GetVisibleItems().Count() - 1);
                break;
            case "ArrowUp" when _isOpen && !AllowFiltering:
                _focusedIndex = Math.Max(_focusedIndex - 1, 0);
                break;
            // No Escape branch anywhere in this component: overlay.js consumes Escape at the
            // window capture phase and SetOpenFromJsAsync already invokes OnClose on that path —
            // a bubbling case could never run in a real browser (dead-branch sweep).
            case "Backspace" when Values.Count > 0 && !_isOpen:
                await RemoveItemAsync(Values[^1]);
                break;
            case "ArrowDown" when !_isOpen:
                await ToggleDropdownAsync();
                break;
        }
    }

    private async Task HandleKeyDownAsync(KeyboardEventArgs e)
    {
        var items = GetVisibleItems().ToList();
        switch (e.Key)
        {
            // No Escape branch: overlay.js consumes Escape at the window capture phase, refocuses
            // the anchor itself, and SetOpenFromJsAsync invokes OnClose — a bubbling case could
            // never run (dead-branch sweep, N169 follow-up).
            case "ArrowDown":
                _focusedIndex = Math.Min(_focusedIndex + 1, items.Count - 1);
                break;
            case "ArrowUp":
                _focusedIndex = Math.Max(_focusedIndex - 1, 0);
                break;
            // No Enter handling here: it is handled on the filter input itself
            // (HandleFilterKeyDownAsync). The select-all and retry controls are
            // native <button> elements inside this popup — a container-level
            // Enter case toggled the focused option when the user pressed Enter
            // on those buttons, and the buttons' own click then also ran.
            case "Backspace" when string.IsNullOrEmpty(_filterText) && Values.Count > 0:
                await RemoveItemAsync(Values[^1]);
                break;
        }
    }

    // Enter inside a text input produces no native click, so keyboard option
    // toggling belongs on the filter input — the only intended Enter origin.
    private async Task HandleFilterKeyDownAsync(KeyboardEventArgs e)
    {
        var items = GetVisibleItems().ToList();
        if (e.Key == "Enter" && _focusedIndex >= 0 && _focusedIndex < items.Count)
            await ToggleItemAsync(items[_focusedIndex]);
    }
}
