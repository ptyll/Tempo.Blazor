using Tempo.Blazor.Components.Inputs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Components.Spreadsheet.Data;
using Tempo.Blazor.Components.Spreadsheet.Enums;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Layout;

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
    private TmTextInput? _queryInput;
    private bool _shouldFocusQuery = true;
    private readonly string _idSuffix = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Stable id of the Find field (the label's <c>for</c> target).</summary>
    private string QueryDomId => $"tm-spreadsheet-find-query-{_idSuffix}";

    /// <summary>Stable id of the Replace-with field.</summary>
    private string ReplaceDomId => $"tm-spreadsheet-find-replace-{_idSuffix}";

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

    /// <summary>
    /// The layout the panel renders. <see cref="TmLayoutMode.Auto"/> reads the app-level VIEWPORT
    /// scope (never the panel's own container — the panel is position-absolute on the desktop layout,
    /// so measuring it would flip the mode on every render) and keeps the floating panel when the host
    /// provides no scope; <see cref="TmLayoutMode.Mobile"/> renders the non-modal bottom sheet,
    /// <see cref="TmLayoutMode.Desktop"/> and <see cref="TmLayoutMode.Tablet"/> the floating panel.
    /// </summary>
    [Parameter] public TmLayoutMode LayoutMode { get; set; } = TmLayoutMode.Auto;

    /// <summary>The viewport's layout, when the app provides a viewport-scope observer.</summary>
    [CascadingParameter(Name = TmLayoutScopes.Viewport)] private TmLayoutContext? Viewport { get; set; }

    /// <summary>
    /// Whether the panel presents as the non-modal bottom sheet. A forced <see cref="LayoutMode"/> wins;
    /// <see cref="TmLayoutMode.Auto"/> reads the app-level viewport scope and keeps the floating panel
    /// when the host has none (a modal must not be measured from a box its own content sizes).
    /// </summary>
    private bool Mobile => LayoutMode switch
    {
        TmLayoutMode.Mobile => true,
        TmLayoutMode.Auto => Viewport?.IsMobile == true,
        _ => false
    };


    /// <summary>The sheet reports a swipe / Escape close: the host closes the panel.</summary>
    private Task OnSheetOpenChanged(bool open) => open ? Task.CompletedTask : OnClose.InvokeAsync();

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
            try
            {
                if (_queryInput is not null)
                {
                    await _queryInput.FocusAsync();
                }
            }
            catch { }
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

    private Task OnQueryChanged(string value)
    {
        // TmTextInput raises BOTH input and change; the change event of a field that already holds the
        // typed text must not re-run the search (that reset the match index to the first hit when the
        // user pressed Enter to move to the next one).
        var query = value ?? string.Empty;
        if (string.Equals(query, _query, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _query = query;
        return RaiseSearch();
    }

    private Task OnReplacementChanged(string value)
    {
        var replacement = value ?? string.Empty;
        if (string.Equals(replacement, _replacement, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        _replacement = replacement;
        return Task.CompletedTask;
    }

    private Task OnMatchCaseToggle(bool value)
    {
        _options.MatchCase = value;
        return RaiseSearch();
    }

    private Task OnWholeCellToggle(bool value)
    {
        _options.WholeCell = value;
        return RaiseSearch();
    }

    private Task OnInFormulasToggle(bool value)
    {
        _options.SearchIn = value ? SpreadsheetSearchIn.Formulas : SpreadsheetSearchIn.Values;
        return RaiseSearch();
    }

    private Task OnWorkbookScopeToggle(bool value)
    {
        _options.Scope = value ? SpreadsheetSearchScope.Workbook : SpreadsheetSearchScope.Sheet;
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
