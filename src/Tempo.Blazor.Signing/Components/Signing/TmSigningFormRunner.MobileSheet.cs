using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Mobile bottom-sheet presentation state of the single steps rail (phase 3 review split — a pure
/// move from <c>TmSigningFormRunner.razor.cs</c>): the sheet state machine, the affordance element
/// references and the pending focus moves. The panel content itself is rendered ONCE (B25) — this
/// state only drives the CSS classes.
/// <para>
/// Signing redesign phase 5: the sheet is a THREE-state CSS presentation (review round) —
/// <c>peek</c> (handle + done count + the primary "Sign now" CTA), <c>expanded</c> (field list +
/// step) and <c>full</c> (the active edit: step panel + sticky action bar, field list hidden —
/// every navigation lands here). The HANDLE is the only <c>aria-expanded</c> toggle; the phase-2
/// full-width "Minimize" row is gone ("Show whole document" in the More menu collapses to peek).
/// </para>
/// </content>
public partial class TmSigningFormRunner
{
    // The sheet is "open" in the expanded AND the full state (both show the step panel); full is
    // the active-edit presentation that additionally hides the field list (CSS --sheet-full).
    private bool _sheetExpanded;
    private bool _sheetFull;
    private bool _pendingHandleFocus;
    private bool _pendingLanguageSelectFocus;
    private string? _pendingFieldListFocus;
    private ElementReference _sheetHandle;
    private ElementReference _languageSelect;

    /// <summary>
    /// The single steps rail. On a mobile-sized container it presents as the bottom sheet: the
    /// <c>--sheet-collapsed</c> / <c>--sheet-expanded</c> / <c>--sheet-full</c> state classes drive
    /// the CSS-only sheet presentation (the panel itself stays one DOM instance at every width).
    /// Collapsed is the PEEK state: the handle, the done count and the primary CTA stay visible
    /// above the document; FULL is the active edit: step panel + sticky action bar, list hidden.
    /// </summary>
    private string StepsClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner__steps" };
            AddClass(classes, _sheetExpanded, "tm-signing-form-runner__steps--sheet-expanded");
            AddClass(classes, _sheetExpanded && _sheetFull, "tm-signing-form-runner__steps--sheet-full");
            AddClass(classes, !_sheetExpanded, "tm-signing-form-runner__steps--sheet-collapsed");
            AddClass(classes, !string.IsNullOrWhiteSpace(MobileCompleteTargetSelector), "tm-signing-form-runner__steps--has-complete-target");
            return string.Join(" ", classes);
        }
    }

    /// <summary>The handle's accessible name follows the sheet state (phase 5).</summary>
    private string SheetHandleAriaLabel => _sheetExpanded
        ? Loc["TmSigningFormRunner_SheetCollapse"]
        : Loc["TmSigningFormRunner_SheetExpand"];

    /// <summary>
    /// The peek strip's done count is its own polite status region while the sheet is collapsed
    /// (phase 5 review) — the panel header's live region is display:none in the peek state, so
    /// the collapsed sheet still announces progress. Expanded/full hide the strip (the header
    /// region takes over) and the role goes with it.
    /// </summary>
    private string? PeekProgressRole => _sheetExpanded ? null : "status";

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _sheetExpanded = MobilePanelMode == TmSigningFormRunnerMobilePanelMode.Expanded;
        _sheetFull = false;
    }

    /// <summary>The handle toggles the sheet between the LIST presentation and the peek state
    /// (phase 5 review: the handle is the ONLY aria-expanded toggle). From the full edit state it
    /// collapses straight to peek — the list is one "Field list" tap away in the More menu.</summary>
    private void ToggleMobileSheet()
    {
        if (_sheetExpanded)
        {
            CollapseMobileSheet();
        }
        else
        {
            _sheetExpanded = true;
            _sheetFull = false;
        }
    }

    /// <summary>Every mobile step landing (Next / Previous / Skip / list tap / overlay tap) ends
    /// in the FULL edit state: step panel + sticky action bar visible, field list hidden.</summary>
    private void LandMobileSheetOnFull()
    {
        _sheetExpanded = true;
        _sheetFull = true;
    }

    /// <summary>Collapses the sheet onto the document (the peek state). The caller that hides the
    /// focus owner ("Sign now", the More menu's "Show whole document") restores focus on the
    /// handle — the one affordance that stays visible in every state.</summary>
    private void CollapseMobileSheet()
    {
        _sheetExpanded = false;
        _sheetFull = false;
    }

    /// <summary>The peek state's primary "Sign now" call to action expands the sheet onto the
    /// step; the CTA disappears, so focus moves onto the handle (phase 5 review).</summary>
    private void ExpandMobilePanel()
    {
        _sheetExpanded = true;
        _sheetFull = false;
        _pendingHandleFocus = true;
    }

    /// <summary>The More menu's "Field list" entry switches the full edit state onto the LIST
    /// presentation (full → expanded), scrolls the list into view and hands focus to the current
    /// field's row once the render has made it visible.</summary>
    private void ShowMobileFields()
    {
        _sheetExpanded = true;
        _sheetFull = false;
        _pendingFieldListFocus = CurrentStep?.Field?.Uuid;
    }

    /// <summary>The More menu's "Language" entry expands the sheet and moves focus to the picker.</summary>
    private void FocusMobileLanguage()
    {
        _sheetExpanded = true;
        _sheetFull = false;
        _pendingLanguageSelectFocus = true;
    }

    /// <summary>
    /// Applies the PENDING focus moves after the render has settled (called from the runner's
    /// single <c>OnAfterRenderAsync</c>): the handle (when the state flip hid the focus owner),
    /// the language picker (the More menu's Language entry) and the field list's current row
    /// (the More menu's "Field list" entry).
    /// </summary>
    private async Task ApplyPendingSheetFocusAsync()
    {
        if (_pendingHandleFocus)
        {
            _pendingHandleFocus = false;
            // The expand CTA / minimize affordance vanish with the state flip: focus lands on the
            // handle, which stays visible in every state, so the next Enter / Tab keeps working.
            await SigningFocus.TryFocusAsync(_sheetHandle);
        }

        if (_pendingLanguageSelectFocus)
        {
            _pendingLanguageSelectFocus = false;
            // The More menu's Language entry hands focus to the ONE language picker at the bottom
            // of the panel; focusing a native select opens its option list on touch.
            await SigningFocus.TryFocusAsync(_languageSelect);
        }

        if (_pendingFieldListFocus is not null)
        {
            var fieldUuid = _pendingFieldListFocus;
            _pendingFieldListFocus = null;
            await (_runnerInterop ??= new SigningRunnerInterop(JS)).FocusFieldButtonAsync(fieldUuid);
        }
    }

    /// <summary>
    /// Dispatches the mobile "More" overflow menu (Signing redesign phase 5): Skip lands on the
    /// shared skip path, Field list / Show whole document drive the sheet states, Language hands
    /// focus to the sheet's picker.
    /// </summary>
    private Task HandleMoreSelectAsync(string value)
    {
        switch (value)
        {
            case "skip":
                return SkipCurrentAsync();
            case "fields":
                ShowMobileFields();
                break;
            case "language":
                FocusMobileLanguage();
                break;
            case "document":
                CollapseMobileSheet();
                _pendingHandleFocus = true;
                break;
        }

        return Task.CompletedTask;
    }
}
