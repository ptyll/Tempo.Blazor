using Microsoft.AspNetCore.Components;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Inputs;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Mobile bottom-sheet state of the single steps rail. The panel content renders ONCE (B25).
/// Peek / list / full are the shared <c>TmDrawer</c> snap index (<c>data-snap-index</c> 0 / 1 / 2);
/// the full edit also sets <c>--list-hidden</c>. The runner handle is the only
/// <c>aria-expanded</c> toggle — the drawer's own handle is off. Crossing the mobile breakpoint
/// remounts the drawer; this state survives, focus inside the sheet does not.
/// </content>
public partial class TmSigningFormRunner
{
    // The sheet is "open" in the expanded AND the full state (both show the step panel); full is
    // the active-edit presentation that additionally hides the field list.
    private bool _sheetExpanded;
    private bool _sheetFull;

    /// <summary>Peek / half / full fractions of the shared sheet (phase 9, G13).</summary>
    private static readonly double[] RunnerSheetSnaps = [0.28, 0.55, 1];

    /// <summary>The mobile branch hosts the single panel inside <c>TmDrawer</c>. Desktop and tablet keep the column.</summary>
    private bool IsMobileSheet => ResolvedLayout == TmLayoutMode.Mobile;

    /// <summary>Peek = 0, list = 1, full edit = 2. The drawer snap follows the sheet state machine.</summary>
    private int RunnerSheetSnapIndex => !_sheetExpanded ? 0 : _sheetFull ? 2 : 1;

    /// <summary>
    /// Snap fraction for the layout reserve. Expanded and full set
    /// <c>--tm-signing-sheet-reserve</c> to this fraction of the 85dvh host (the drawer panel
    /// height) so the field can scroll above the sheet. Peek leaves the var unset and keeps the
    /// content-sized peek token.
    /// </summary>
    private string? MobileSheetReserveStyle
    {
        get
        {
            if (!IsMobileSheet || RunnerSheetSnapIndex <= 0)
            {
                return null;
            }

            var fraction = RunnerSheetSnaps[Math.Clamp(RunnerSheetSnapIndex, 0, RunnerSheetSnaps.Length - 1)];
            return FormattableString.Invariant($"--tm-signing-sheet-snap: {fraction:0.##}");
        }
    }
    private bool _pendingHandleFocus;
    private bool _pendingLanguageSelectFocus;
    private string? _pendingFieldListFocus;
    private ElementReference _sheetHandle;
    private ElementReference _languageSelect;

    /// <summary>
    /// The single steps rail. Desktop and tablet keep the column. On mobile the rail is the
    /// shared <c>TmDrawer</c> (phase 9): peek / list / full are the drawer's snap index
    /// (<c>data-snap-index</c> 0 / 1 / 2), not a local sheet stylesheet. The content classes
    /// only fold the field list in the full edit and lift the rail above a host complete bar.
    /// </summary>
    private string StepsClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner__steps" };
            AddClass(classes, IsMobileSheet && _sheetExpanded && !_sheetFull, "tm-signing-form-runner__steps--list");
            AddClass(classes, IsMobileSheet && _sheetExpanded && _sheetFull, "tm-signing-form-runner__steps--list-hidden");
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
    /// <summary>
    /// A swipe on the shared sheet reports a snap index. It drives the same state machine as the
    /// handle — it does not open a second panel and it does not carry <c>aria-expanded</c>.
    /// </summary>
    private Task OnRunnerSheetSnapChanged(int index)
    {
        if (index <= 0)
        {
            CollapseMobileSheet();
        }
        else if (index >= 2)
        {
            LandMobileSheetOnFull();
        }
        else
        {
            _sheetExpanded = true;
            _sheetFull = false;
        }

        return Task.CompletedTask;
    }

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
