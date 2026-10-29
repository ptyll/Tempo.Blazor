using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Spreadsheet.Models;

namespace Tempo.Blazor.Components.Spreadsheet;

/// <summary>
/// Displays the active cell reference and provides an input for editing cell values and formulas.
/// Synchronizes bidirectionally with the active cell in the spreadsheet grid.
/// </summary>
public partial class TmSpreadsheetFormulaBar
{
    private ElementReference _rootRef;
    private ElementReference _inputRef;
    private ElementReference _nameBoxRef;
    private ElementReference _nameBoxWrapperRef;
    private string? _editValue;
    private string? _nameBoxValue;
    private bool _shouldFocusAfterRender;
    private readonly string _functionHintId = $"tm-spreadsheet-formula-hint-{Guid.NewGuid():N}";
    private int _renderedSelectionStart;
    private int _renderedSelectionEnd;
    private bool _localIsEditing;
    private bool _suppressExternalEditing;
    private bool _isRefListOpen;
    private (int RowDelta, int ColDelta)? _pendingCommitNavigation;

    private sealed class SelectionSnapshot
    {
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
    }

    private sealed class JsFormulaSessionAnalysis
    {
        public string Text { get; set; } = string.Empty;
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public bool IsFormula { get; set; }
        public bool IsReferencePickingMode { get; set; }
        public SpreadsheetFormulaReferenceToken? ActiveReferenceToken { get; set; }
        public int ActiveReferenceTokenIndex { get; set; } = -1;
        public IReadOnlyList<SpreadsheetFormulaReferenceToken>? ReferenceTokens { get; set; }
        public string? FunctionPrefix { get; set; }
        public int FunctionPrefixStart { get; set; } = -1;
        public int FunctionPrefixEnd { get; set; } = -1;
        public IReadOnlyList<SpreadsheetFormulaFunctionMetadata>? Suggestions { get; set; }
        public SpreadsheetFormulaFunctionHint? ActiveFunctionHint { get; set; }
    }

    private sealed class JsHostFormulaSession
    {
        public string? Owner { get; set; }
        public string? CellRef { get; set; }
        public string? Text { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public bool IsFormula { get; set; }
    }

    /// <summary>The current formula editing session used for shared formula UX.</summary>
    public SpreadsheetFormulaEditSession CurrentSession { get; private set; } = new();

    /// <summary>The current live value displayed inside the formula bar editor.</summary>
    public string? CurrentEditValue => _editValue;
    private bool EditorIsEditing => _localIsEditing || (IsEditing && !_suppressExternalEditing);

    /// <summary>Consumes the pending spreadsheet-like navigation requested by the last commit keystroke.</summary>
    public (int RowDelta, int ColDelta)? ConsumePendingCommitNavigation()
    {
        var navigation = _pendingCommitNavigation;
        _pendingCommitNavigation = null;
        return navigation;
    }

    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The A1 reference of the currently active cell.</summary>
    [Parameter] public string? ActiveCellRef { get; set; }

    /// <summary>The current display value or formula of the active cell.</summary>
    [Parameter] public string? DisplayValue { get; set; }

    /// <summary>Whether the formula bar is in editing mode.</summary>
    [Parameter] public bool IsEditing { get; set; }

    /// <summary>Called when the user starts editing in the formula bar.</summary>
    [Parameter] public EventCallback OnEditStarted { get; set; }

    /// <summary>Called when the user commits a value from the formula bar.</summary>
    [Parameter] public EventCallback<string?> OnValueCommitted { get; set; }

    /// <summary>Called when the user cancels editing in the formula bar.</summary>
    [Parameter] public EventCallback OnEditCancelled { get; set; }

    /// <summary>Called when the formula bar value changes during editing.</summary>
    [Parameter] public EventCallback<string?> OnValueChanged { get; set; }

    /// <summary>Called when the user presses Tab while editing in the formula bar.</summary>
    [Parameter] public EventCallback OnTabPressed { get; set; }

    /// <summary>Called after a commit when spreadsheet-like navigation should move the active cell.</summary>
    [Parameter] public EventCallback<(int RowDelta, int ColDelta)> OnCommitNavigationRequested { get; set; }

    /// <summary>Called when the user enters a cell reference or named range in the name box and presses Enter.</summary>
    [Parameter] public EventCallback<string> OnNavigateToRef { get; set; }

    /// <summary>
    /// The workbook's named ranges, offered as a dropdown list next to the name box. When the list is
    /// empty (or null) the chevron is not rendered at all and the name box stays the plain A1 input.
    /// Selecting an entry raises <see cref="OnNavigateToRef"/> with the range NAME, exactly like typing
    /// it into the box and pressing Enter.
    /// </summary>
    [Parameter] public IReadOnlyList<SpreadsheetNamedRange>? NamedRanges { get; set; }

    /// <summary>Called when the current formula session should move into the inline grid editor.</summary>
    [Parameter] public EventCallback OnTransferToInlineEditorRequested { get; set; }

    protected override void OnParametersSet()
    {
        _nameBoxValue = ActiveCellRef;
        if (IsEditing && !_suppressExternalEditing)
        {
            _localIsEditing = true;
        }

        if (!IsEditing)
        {
            _suppressExternalEditing = false;
            var preserveLocalFormulaSession = _localIsEditing
                && ((_editValue?.StartsWith("=") ?? false) || CurrentSession.IsFormula);
            if (!preserveLocalFormulaSession)
            {
                _localIsEditing = false;
                _editValue = DisplayValue;
            }
        }
    }

    private async Task StartEdit()
    {
        if (EditorIsEditing) return;
        _suppressExternalEditing = false;
        _localIsEditing = true;
        _editValue = DisplayValue;
        _renderedSelectionStart = (_editValue ?? string.Empty).Length;
        _renderedSelectionEnd = _renderedSelectionStart;

        try
        {
            var hostSession = await JS.InvokeAsync<JsHostFormulaSession?>("tmSpreadsheetFormulaBar.getHostFormulaSession", _rootRef);
            if (hostSession is not null
                && string.Equals(hostSession.CellRef, ActiveCellRef, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(hostSession.Text))
            {
                _editValue = hostSession.Text;
                _renderedSelectionStart = Math.Clamp(hostSession.SelectionStart, 0, _editValue.Length);
                _renderedSelectionEnd = Math.Clamp(hostSession.SelectionEnd, 0, _editValue.Length);
            }
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }

        CurrentSession = new SpreadsheetFormulaEditSession
        {
            Text = _editValue ?? string.Empty,
            SelectionStart = _renderedSelectionStart,
            SelectionEnd = _renderedSelectionEnd,
            IsFormula = (_editValue ?? string.Empty).StartsWith("=")
        };
        _shouldFocusAfterRender = true;
        await OnEditStarted.InvokeAsync();
    }

    private async Task OnInput(ChangeEventArgs e)
    {
        _editValue = e.Value?.ToString();
        var value = _editValue ?? string.Empty;
        var selectionStart = (_editValue ?? string.Empty).Length;
        var selectionEnd = selectionStart;

        try
        {
            await Task.Yield();
            var selection = await JS.InvokeAsync<SelectionSnapshot>("tmSpreadsheetFormulaBar.getSelection", _inputRef);
            selectionStart = selection?.SelectionStart ?? selectionStart;
            selectionEnd = selection?.SelectionEnd ?? selectionStart;
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }

        if (value.Length > 0
            && selectionStart == selectionEnd
            && (selectionStart <= 0
                || (selectionStart == _renderedSelectionStart
                    && selectionEnd == _renderedSelectionEnd
                    && !string.Equals(CurrentSession.Text, value, StringComparison.Ordinal))))
        {
            selectionStart = value.Length;
            selectionEnd = value.Length;
        }

        _renderedSelectionStart = selectionStart;
        _renderedSelectionEnd = selectionEnd;
        await RefreshSessionAsync(selectionStart, selectionEnd);
        await OnValueChanged.InvokeAsync(_editValue);
    }

    private async Task HandleKeyDown(KeyboardEventArgs e)
    {
        if (EditorIsEditing)
        {
            if (e.Key == "F2")
            {
                await OnTransferToInlineEditorRequested.InvokeAsync();
                return;
            }

            if (e.Key == "F4" && CurrentSession.IsFormula)
            {
                await CycleReferenceAsync();
                return;
            }

            if (HasSuggestions && e.Key is "ArrowDown" or "ArrowUp")
            {
                MoveSuggestionSelection(e.Key == "ArrowDown" ? 1 : -1);
                return;
            }

            if (HasSuggestions && e.Key == "Enter")
            {
                await RefreshSelectionStateAsync();
                await AcceptSuggestionAsync(CurrentSession.SelectedSuggestionIndex);
                return;
            }
        }

        switch (e.Key)
        {
            case "Enter":
                _pendingCommitNavigation = (e.ShiftKey ? -1 : 1, 0);
                await CommitAsync();
                await OnCommitNavigationRequested.InvokeAsync(_pendingCommitNavigation ?? (e.ShiftKey ? -1 : 1, 0));
                break;
            case "Escape":
                _pendingCommitNavigation = null;
                Cancel();
                break;
            case "Tab":
                _pendingCommitNavigation = (0, e.ShiftKey ? -1 : 1);
                await CommitAsync();
                await OnCommitNavigationRequested.InvokeAsync(_pendingCommitNavigation ?? (0, e.ShiftKey ? -1 : 1));
                await OnTabPressed.InvokeAsync();
                break;
        }
    }

    private void HandleDisplayKeyDown(KeyboardEventArgs e)
    {
        if (e.Key is "Enter" or " ")
        {
            _ = StartEdit();
        }
    }

    private void OnNameBoxInput(ChangeEventArgs e) => _nameBoxValue = e.Value?.ToString();

    private async Task HandleNameBoxKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Enter" && !string.IsNullOrWhiteSpace(_nameBoxValue))
        {
            await OnNavigateToRef.InvokeAsync(_nameBoxValue.Trim());
        }
        else if (e.Key == "Escape")
        {
            _nameBoxValue = ActiveCellRef;
            StateHasChanged();
        }
    }

    private void OnNameBoxBlur()
    {
        _nameBoxValue = ActiveCellRef;
    }

    private bool HasNamedRanges => NamedRanges is { Count: > 0 };

    private void ToggleNamedRangeList()
    {
        _isRefListOpen = !_isRefListOpen;
    }

    private async Task SelectNamedRangeAsync(SpreadsheetNamedRange range)
    {
        _isRefListOpen = false;
        _nameBoxValue = range.Name;
        await OnNavigateToRef.InvokeAsync(range.Name);
    }

    /// <summary>Escape closes the list from the input, the toggle OR a list entry (the whole wrapper).</summary>
    private void HandleNameBoxWrapperKeyDown(KeyboardEventArgs e)
    {
        if (e.Key == "Escape" && _isRefListOpen)
        {
            _isRefListOpen = false;
        }
    }

    /// <summary>Leaving the name box (Tab out, focus elsewhere) closes the list.</summary>
    private async Task HandleNameBoxFocusOutAsync(FocusEventArgs e)
    {
        if (!_isRefListOpen)
        {
            return;
        }

        bool inside;
        try
        {
            inside = await JS.InvokeAsync<bool>("tmSpreadsheetFormulaBar.isFocusWithin", _nameBoxWrapperRef);
        }
        catch
        {
            // No script (prerender/bUnit): treat the focus as gone, closing is the safe direction.
            inside = false;
        }

        if (!inside)
        {
            _isRefListOpen = false;
        }
    }

    private void CloseNamedRangeList() => _isRefListOpen = false;

    /// <summary>The formula bar's ✓: commits the edited text (the same path as Enter).</summary>
    private async Task ConfirmFromBarAsync()
    {
        _pendingCommitNavigation = null;
        await CommitAsync();
    }

    /// <summary>The formula bar's ✗: reverts the edited text (the same path as Escape).</summary>
    private Task CancelFromBarAsync()
    {
        Cancel();
        return Task.CompletedTask;
    }

    private async Task CommitAsync()
    {
        if (!EditorIsEditing) return;
        _suppressExternalEditing = true;
        _localIsEditing = false;
        CurrentSession = new SpreadsheetFormulaEditSession
        {
            Text = _editValue ?? string.Empty,
            SelectionStart = 0,
            SelectionEnd = 0,
            IsFormula = false
        };
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.clearHostFormulaSession", _rootRef, "formulaBar");
            await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.setHostFormulaPointMode", _rootRef, false, string.Empty);
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }
        await InvokeAsync(StateHasChanged);
        await OnValueCommitted.InvokeAsync(_editValue);
    }

    private async Task HandleBlurAsync()
    {
        if (!EditorIsEditing)
            return;

        var shouldRetainFocus = true;
        try
        {
            shouldRetainFocus = await JS.InvokeAsync<bool>("tmSpreadsheetFormulaBar.shouldRetainFocusAfterBlur", _rootRef);
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }

        if (!shouldRetainFocus)
            return;

        _shouldFocusAfterRender = true;
        await InvokeAsync(StateHasChanged);
    }

    private void Cancel()
    {
        if (!EditorIsEditing) return;
        _pendingCommitNavigation = null;
        _suppressExternalEditing = true;
        _localIsEditing = false;
        _editValue = DisplayValue;
        CurrentSession = new SpreadsheetFormulaEditSession
        {
            Text = _editValue ?? string.Empty,
            SelectionStart = 0,
            SelectionEnd = 0,
            IsFormula = false
        };
        _ = ClearFormulaPointModeAsync();
        _ = InvokeAsync(StateHasChanged);
        OnEditCancelled.InvokeAsync();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        try
        {
            await JS.InvokeVoidAsync(
                "tmSpreadsheetFormulaBar.setHostFormulaPointMode",
                _rootRef,
                EditorIsEditing,
                _editValue ?? string.Empty);
            await JS.InvokeVoidAsync(
                "tmSpreadsheetFormulaBar.syncNameBoxText",
                _nameBoxRef,
                _nameBoxValue ?? ActiveCellRef ?? string.Empty);
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }

        if (EditorIsEditing)
        {
            try
            {
                await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.bindHostFormulaPointMode", _rootRef, _inputRef);
            }
            catch
            {
                // ElementReference may not be bound yet.
            }
        }

        if (_shouldFocusAfterRender)
        {
            _shouldFocusAfterRender = false;
            try
            {
                var hostSession = await JS.InvokeAsync<JsHostFormulaSession?>("tmSpreadsheetFormulaBar.getHostFormulaSession", _rootRef);
                if (hostSession is not null
                    && string.Equals(hostSession.CellRef, ActiveCellRef, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(hostSession.Text)
                    && string.IsNullOrEmpty(_editValue))
                {
                    _editValue = hostSession.Text;
                    _renderedSelectionStart = Math.Clamp(hostSession.SelectionStart, 0, _editValue.Length);
                    _renderedSelectionEnd = Math.Clamp(hostSession.SelectionEnd, 0, _editValue.Length);
                }

                await _inputRef.FocusAsync();
                var valueLength = (_editValue ?? string.Empty).Length;
                _renderedSelectionStart = Math.Clamp(_renderedSelectionStart, 0, valueLength);
                _renderedSelectionEnd = Math.Clamp(_renderedSelectionEnd, 0, valueLength);
                await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.setValueAndSelection", _inputRef, _editValue ?? string.Empty, _renderedSelectionStart, _renderedSelectionEnd);
                await RefreshSessionAsync();
            }
            catch
            {
                // ElementReference may not be bound yet.
            }
        }
    }

    /// <summary>Replaces the active reference token or current selection with a new cell reference.</summary>
    public async Task ReplaceReferenceAsync(string cellRef)
    {
        if (!EditorIsEditing || string.IsNullOrWhiteSpace(cellRef))
            return;

        await RefreshSelectionStateAsync();
        var replacement = await JS.InvokeAsync<SelectionReplacement>("tmSpreadsheetFormulaBar.replaceReferenceAtSelection",
            _editValue ?? "=",
            CurrentSession.SelectionStart,
            CurrentSession.SelectionEnd,
            cellRef) ?? new SelectionReplacement
            {
                Value = (_editValue ?? "=") + cellRef,
                SelectionStart = (_editValue ?? "=").Length + cellRef.Length,
                SelectionEnd = (_editValue ?? "=").Length + cellRef.Length
            };
        await ApplySelectionReplacementAsync(replacement);
    }

    private bool HasSuggestions => CurrentSession.Suggestions.Count > 0;

    private string? GetFormulaAriaDescribedBy()
        => CurrentSession.ActiveFunctionHint is not null ? _functionHintId : null;

    private async Task RefreshSelectionStateAsync()
    {
        if (!EditorIsEditing)
            return;

        try
        {
            var selection = await JS.InvokeAsync<SelectionSnapshot>("tmSpreadsheetFormulaBar.getSelection", _inputRef);
            if (!EditorIsEditing)
                return;
            _renderedSelectionStart = selection?.SelectionStart ?? (_editValue ?? string.Empty).Length;
            _renderedSelectionEnd = selection?.SelectionEnd ?? _renderedSelectionStart;
            await RefreshSessionAsync(_renderedSelectionStart, _renderedSelectionEnd, notifyValueChanged: false);
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }
    }

    private async Task RefreshSessionAsync(int? selectionStart = null, int? selectionEnd = null, bool notifyValueChanged = false)
    {
        if (!EditorIsEditing)
            return;

        var value = _editValue ?? string.Empty;
        var start = selectionStart ?? _renderedSelectionStart;
        var end = selectionEnd ?? _renderedSelectionEnd;

        JsFormulaSessionAnalysis analysis;
        try
        {
            analysis = await JS.InvokeAsync<JsFormulaSessionAnalysis>("tmSpreadsheetFormulaBar.analyzeSession", _rootRef, value, start, end)
                ?? new JsFormulaSessionAnalysis();
        }
        catch
        {
            analysis = new JsFormulaSessionAnalysis
            {
                Text = value,
                SelectionStart = Math.Clamp(start, 0, value.Length),
                SelectionEnd = Math.Clamp(end, 0, value.Length),
                IsFormula = value.StartsWith("="),
                IsReferencePickingMode = value.StartsWith("="),
                ReferenceTokens = []
            };
        }

        var suggestions = BuildSuggestions(analysis.FunctionPrefix);
        var previousSelectedSuggestionName = HasSuggestions
            && CurrentSession.SelectedSuggestionIndex >= 0
            && CurrentSession.SelectedSuggestionIndex < CurrentSession.Suggestions.Count
                ? CurrentSession.Suggestions[CurrentSession.SelectedSuggestionIndex].Name
                : null;
        var selectedSuggestionIndex = 0;
        if (suggestions.Count > 0 && !string.IsNullOrWhiteSpace(previousSelectedSuggestionName))
        {
            var preservedIndex = suggestions
                .Select((suggestion, index) => new { suggestion.Name, Index = index })
                .FirstOrDefault(entry => string.Equals(entry.Name, previousSelectedSuggestionName, StringComparison.OrdinalIgnoreCase))
                ?.Index;
            if (preservedIndex is int index && index >= 0)
                selectedSuggestionIndex = index;
        }

        CurrentSession = new SpreadsheetFormulaEditSession
        {
            Text = analysis.Text,
            SelectionStart = analysis.SelectionStart,
            SelectionEnd = analysis.SelectionEnd,
            IsFormula = analysis.IsFormula,
            IsReferencePickingMode = analysis.IsReferencePickingMode,
            ActiveReferenceToken = analysis.ActiveReferenceToken,
            ReferenceTokens = analysis.ReferenceTokens ?? [],
            FunctionPrefix = analysis.FunctionPrefix,
            FunctionPrefixStart = analysis.FunctionPrefixStart,
            FunctionPrefixEnd = analysis.FunctionPrefixEnd,
            Suggestions = analysis.Suggestions ?? suggestions,
            SelectedSuggestionIndex = selectedSuggestionIndex,
            ActiveFunctionHint = analysis.ActiveFunctionHint
        };

        await SyncHostSessionAsync();

        if (notifyValueChanged)
            await OnValueChanged.InvokeAsync(_editValue);

        await InvokeAsync(StateHasChanged);
    }

    private static IReadOnlyList<SpreadsheetFormulaFunctionMetadata> BuildSuggestions(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return [];

        return SpreadsheetFormulaFunctionCatalog.All
            .Where(function => function.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(function => function.Name.Length)
            .ThenBy(function => function.Name, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();
    }

    private void MoveSuggestionSelection(int delta)
    {
        if (!HasSuggestions)
            return;

        var nextIndex = CurrentSession.SelectedSuggestionIndex + delta;
        if (nextIndex < 0)
            nextIndex = CurrentSession.Suggestions.Count - 1;
        else if (nextIndex >= CurrentSession.Suggestions.Count)
            nextIndex = 0;

        CurrentSession = new SpreadsheetFormulaEditSession
        {
            Text = CurrentSession.Text,
            SelectionStart = CurrentSession.SelectionStart,
            SelectionEnd = CurrentSession.SelectionEnd,
            IsFormula = CurrentSession.IsFormula,
            IsReferencePickingMode = CurrentSession.IsReferencePickingMode,
            ActiveReferenceToken = CurrentSession.ActiveReferenceToken,
            ReferenceTokens = CurrentSession.ReferenceTokens,
            FunctionPrefix = CurrentSession.FunctionPrefix,
            FunctionPrefixStart = CurrentSession.FunctionPrefixStart,
            FunctionPrefixEnd = CurrentSession.FunctionPrefixEnd,
            Suggestions = CurrentSession.Suggestions,
            SelectedSuggestionIndex = nextIndex,
            ActiveFunctionHint = CurrentSession.ActiveFunctionHint
        };
        StateHasChanged();
    }

    private async Task AcceptSuggestionAsync(int index)
    {
        if (!HasSuggestions)
            return;

        var suggestion = CurrentSession.Suggestions[Math.Clamp(index, 0, CurrentSession.Suggestions.Count - 1)];
        var replacement = await JS.InvokeAsync<SelectionReplacement>("tmSpreadsheetFormulaBar.acceptFunctionSuggestion",
            _editValue ?? "=",
            CurrentSession.SelectionStart,
            CurrentSession.SelectionEnd,
            suggestion.Name) ?? new SelectionReplacement
            {
                Value = (_editValue ?? "=") + $"{suggestion.Name}(",
                SelectionStart = (_editValue ?? "=").Length + suggestion.Name.Length + 1,
                SelectionEnd = (_editValue ?? "=").Length + suggestion.Name.Length + 1
            };
        await ApplySelectionReplacementAsync(replacement);
    }

    private async Task CycleReferenceAsync()
    {
        var replacement = await JS.InvokeAsync<SelectionReplacement>("tmSpreadsheetFormulaBar.cycleReferenceAtSelection",
            _editValue ?? string.Empty,
            CurrentSession.SelectionStart,
            CurrentSession.SelectionEnd) ?? new SelectionReplacement
            {
                Value = _editValue ?? string.Empty,
                SelectionStart = CurrentSession.SelectionStart,
                SelectionEnd = CurrentSession.SelectionEnd
            };
        await ApplySelectionReplacementAsync(replacement);
    }

    private async Task ApplySelectionReplacementAsync(SelectionReplacement replacement)
    {
        _editValue = replacement.Value ?? string.Empty;
        _renderedSelectionStart = replacement.SelectionStart;
        _renderedSelectionEnd = replacement.SelectionEnd;
        await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.setValueAndSelection", _inputRef, _editValue, _renderedSelectionStart, _renderedSelectionEnd);
        await RefreshSessionAsync(_renderedSelectionStart, _renderedSelectionEnd, notifyValueChanged: true);
    }

    private async Task SyncHostSessionAsync()
    {
        if (!EditorIsEditing)
            return;

        try
        {
            await JS.InvokeVoidAsync(
                "tmSpreadsheetFormulaBar.setHostFormulaSession",
                _rootRef,
                new
                {
                    owner = "formulaBar",
                    cellRef = ActiveCellRef,
                    text = CurrentSession.Text,
                    selectionStart = CurrentSession.SelectionStart,
                    selectionEnd = CurrentSession.SelectionEnd,
                    isFormula = CurrentSession.IsFormula
                });
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }
    }

    private async Task ClearHostSessionAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.clearHostFormulaSession", _rootRef, "formulaBar");
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }
    }

    private async Task ClearFormulaPointModeAsync()
    {
        try
        {
            await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.clearHostFormulaSession", _rootRef, "formulaBar");
            await JS.InvokeVoidAsync("tmSpreadsheetFormulaBar.setHostFormulaPointMode", _rootRef, false, string.Empty);
        }
        catch
        {
            // JS can be unavailable during prerender/tests.
        }
    }

    private sealed class SelectionReplacement
    {
        public string? Value { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionEnd { get; set; }
        public bool Changed { get; set; }
    }
}
