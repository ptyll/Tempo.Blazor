using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Signing;

/// <content>
/// Mobile bottom-sheet presentation state of the single steps rail (phase 3 review split — a pure
/// move from <c>TmSigningFormRunner.razor.cs</c>): the expanded / collapsed flag, the two
/// affordance element references and the focus move onto the affordance that becomes visible.
/// The panel content itself is rendered ONCE (B25) — this state only drives the CSS classes.
/// </content>
public partial class TmSigningFormRunner
{
    private bool _sheetExpanded;
    private bool _pendingToggleFocus;
    private ElementReference _mobileExpandButton;
    private ElementReference _mobileMinimizeButton;

    /// <summary>
    /// The single steps rail. On a mobile-sized container it presents as the bottom sheet: the
    /// <c>--sheet-collapsed</c> / <c>--sheet-expanded</c> state classes drive the CSS-only sheet
    /// presentation (the panel itself stays one DOM instance at every width).
    /// </summary>
    private string StepsClass
    {
        get
        {
            var classes = new List<string> { "tm-signing-form-runner__steps" };
            AddClass(classes, _sheetExpanded, "tm-signing-form-runner__steps--sheet-expanded");
            AddClass(classes, !_sheetExpanded, "tm-signing-form-runner__steps--sheet-collapsed");
            AddClass(classes, !string.IsNullOrWhiteSpace(MobileCompleteTargetSelector), "tm-signing-form-runner__steps--has-complete-target");
            return string.Join(" ", classes);
        }
    }

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        _sheetExpanded = MobilePanelMode == TmSigningFormRunnerMobilePanelMode.Expanded;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingToggleFocus)
        {
            return;
        }

        _pendingToggleFocus = false;
        // Keyboard users expanded / collapsed the mobile sheet: move focus onto the affordance
        // that is NOW visible, so the next Enter / Tab works without hunting for the button.
        await SigningFocus.TryFocusAsync(_sheetExpanded ? _mobileMinimizeButton : _mobileExpandButton);
    }

    private void ExpandMobilePanel()
    {
        _sheetExpanded = true;
        _pendingToggleFocus = true;
    }

    private void MinimizeMobilePanel()
    {
        _sheetExpanded = false;
        _pendingToggleFocus = true;
    }
}
