using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Enums;

namespace Tempo.Blazor.Components.Spreadsheet.Dialogs;

/// <summary>
/// Find &amp; replace panel for the spreadsheet. Owns the query/replacement text and search options
/// locally and bubbles changes to the host, which performs the actual search/replace and reports
/// the current match position back via <see cref="MatchIndex"/> / <see cref="MatchCount"/>.
/// </summary>
public partial class TmSpreadsheetFindReplaceDialog
{
    private readonly SpreadsheetSearchOptions _options = new();
    private string _query = string.Empty;
    private string _replacement = string.Empty;
    private ElementReference _queryInput;
    private bool _shouldFocusQuery = true;

    /// <summary>Initial search options used to seed the panel.</summary>
    [Parameter] public SpreadsheetSearchOptions? InitialOptions { get; set; }

    /// <summary>1-based index of the currently highlighted match (0 when none).</summary>
    [Parameter] public int MatchIndex { get; set; }

    /// <summary>Total number of matches for the current query.</summary>
    [Parameter] public int MatchCount { get; set; }

    /// <summary>Raised whenever the query or any option changes; carries the full current options.</summary>
    [Parameter] public EventCallback<SpreadsheetSearchOptions> OnSearchRequested { get; set; }

    /// <summary>Raised to move to the next match.</summary>
    [Parameter] public EventCallback OnFindNext { get; set; }

    /// <summary>Raised to move to the previous match.</summary>
    [Parameter] public EventCallback OnFindPrevious { get; set; }

    /// <summary>Raised to replace the current match; carries the replacement text.</summary>
    [Parameter] public EventCallback<string> OnReplaceRequested { get; set; }

    /// <summary>Raised to replace every match; carries the replacement text.</summary>
    [Parameter] public EventCallback<string> OnReplaceAllRequested { get; set; }

    /// <summary>Raised when the panel should close.</summary>
    [Parameter] public EventCallback OnClose { get; set; }

    private string CounterText => MatchCount > 0
        ? Loc["TmSpreadsheet_Find_MatchCounter", MatchIndex, MatchCount]
        : (_query.Length > 0 ? Loc["TmSpreadsheet_Find_NoMatches"] : string.Empty);

    protected override void OnInitialized()
    {
        if (InitialOptions is not null)
        {
            _options.MatchCase = InitialOptions.MatchCase;
            _options.WholeCell = InitialOptions.WholeCell;
            _options.SearchIn = InitialOptions.SearchIn;
            _options.Scope = InitialOptions.Scope;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_shouldFocusQuery)
        {
            _shouldFocusQuery = false;
            try { await _queryInput.FocusAsync(); } catch { }
        }
    }

    private SpreadsheetSearchOptions BuildOptions() => new()
    {
        Query = _query,
        MatchCase = _options.MatchCase,
        WholeCell = _options.WholeCell,
        SearchIn = _options.SearchIn,
        Scope = _options.Scope
    };

    private Task OnQueryInput(ChangeEventArgs e)
    {
        _query = e.Value?.ToString() ?? string.Empty;
        return RaiseSearch();
    }

    private void OnReplacementInput(ChangeEventArgs e)
        => _replacement = e.Value?.ToString() ?? string.Empty;

    private Task OnMatchCaseToggle(ChangeEventArgs e)
    {
        _options.MatchCase = e.Value is bool b && b;
        return RaiseSearch();
    }

    private Task OnWholeCellToggle(ChangeEventArgs e)
    {
        _options.WholeCell = e.Value is bool b && b;
        return RaiseSearch();
    }

    private Task OnInFormulasToggle(ChangeEventArgs e)
    {
        _options.SearchIn = e.Value is bool b && b ? SpreadsheetSearchIn.Formulas : SpreadsheetSearchIn.Values;
        return RaiseSearch();
    }

    private Task OnWorkbookScopeToggle(ChangeEventArgs e)
    {
        _options.Scope = e.Value is bool b && b ? SpreadsheetSearchScope.Workbook : SpreadsheetSearchScope.Sheet;
        return RaiseSearch();
    }

    private Task RaiseSearch() => OnSearchRequested.InvokeAsync(BuildOptions());

    private Task FindNext() => OnFindNext.InvokeAsync();

    private Task FindPrevious() => OnFindPrevious.InvokeAsync();

    private Task Replace() => OnReplaceRequested.InvokeAsync(_replacement);

    private Task ReplaceAll() => OnReplaceAllRequested.InvokeAsync(_replacement);

    private Task Close() => OnClose.InvokeAsync();

    // Escape stays on the root so it works wherever focus sits in the dialog.
    // Enter/F3 live on the text inputs only (OnFieldKeyDown): the close and
    // action controls are native <button>s — the browser turns Enter on them
    // into a click on keydown, so a root-level Enter case would find-next AND
    // run the button's own click for one key press.
    private Task OnRootKeyDown(KeyboardEventArgs e)
    {
        return e.Key == "Escape" ? Close() : Task.CompletedTask;
    }

    private Task OnFieldKeyDown(KeyboardEventArgs e)
    {
        return e.Key switch
        {
            "Enter" => e.ShiftKey ? FindPrevious() : FindNext(),
            "F3" => e.ShiftKey ? FindPrevious() : FindNext(),
            _ => Task.CompletedTask
        };
    }
}
