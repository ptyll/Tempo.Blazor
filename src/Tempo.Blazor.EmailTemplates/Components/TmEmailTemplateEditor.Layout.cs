using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.Actions;
using Tempo.Blazor.Components.Buttons;
using Tempo.Blazor.Components.Feedback;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Layout;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Toolbar;
using Tempo.Blazor.EmailTemplates.Abstractions.Editing;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Registry;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Services;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.EmailTemplates.Components;

// The editor's TmEditorShell mapping: layout resolution, the collapse bridge (D10) and the mobile
// panel/sheet state. Everything here forwards to the shell — the editor keeps no breakpoint logic.
public partial class TmEmailTemplateEditor
{
    /// <summary>The layout the shell resolved: the forced <see cref="LayoutMode"/>, else the measured mode, else <see cref="InitialMode"/>.</summary>
    private TmLayoutMode EffectiveResolvedLayout
        => LayoutMode != TmLayoutMode.Auto ? LayoutMode : _measuredLayout ?? InitialMode;

    private bool IsMobileLayout => EffectiveResolvedLayout == TmLayoutMode.Mobile;

    /// <summary>
    /// The mobile panels sheet's snaps: the DEFAULT half snap leaves a phone the tabs plus the palette
    /// SEARCH box only (a host with its own page chrome above the editor pushes the first block tile below
    /// the fold — measured at 390 px), so the closed-to-open snap starts at two thirds of the stage, which
    /// shows the first tile row while the canvas stays visible behind it. The second snap is full height.
    /// </summary>
    private static readonly IReadOnlyList<double> MobileSheetSnaps = new[] { 0.65, 1.0 };

    private TmLayoutBreakpoints EffectiveBreakpoints => Breakpoints ?? new TmLayoutBreakpoints(768, 1200);

    /// <summary>
    /// Every layout-change SIGNAL bumps this counter: the shell's <c>ResolvedLayoutChanged</c>, the mobile
    /// header slot's report (they share this handler) and a change of the <see cref="LayoutMode"/>
    /// parameter. Together with <see cref="_layoutGenerationAtRender"/> it answers "did the layout change
    /// in THIS batch?" synchronously — the collapse bridge (U2) needs that answer before the render, not
    /// after it.
    /// </summary>
    private int _layoutGeneration;

    /// <summary>The generation the last render served: a raise is "in the layout's batch" while the counter is ahead.</summary>
    private int _layoutGenerationAtRender;

    /// <summary>True while a layout change has been signalled and no render has settled yet.</summary>
    private bool LayoutChangedInThisBatch => _layoutGeneration != _layoutGenerationAtRender;

    private async Task OnResolvedLayoutChangedAsync(TmLayoutMode mode)
    {
        if (_measuredLayout == mode) return;
        _measuredLayout = mode;
        // U2: a layout-change signal. The shell raises this BEFORE CollapsedPanelsChanged, so a rail it
        // imposes afterwards lands in the same generation and is recognisable as a layout state.
        _layoutGeneration++;
        // The mobile-only chrome (the compact header) is a markup branch, not a CSS step: switching it
        // needs a render the shell's own callback does not trigger.
        StateHasChanged();
        await ResolvedLayoutChanged.InvokeAsync(mode);
    }

    /// <summary>
    /// The document's name for the compact mobile header (the host's <see cref="ToolbarStart"/> wins when it
    /// supplies one). <see langword="null"/> when the document has no name, so the header renders no title
    /// at all instead of an empty line.
    /// </summary>
    private string? DocumentTitle
        => string.IsNullOrWhiteSpace(_document.Name) ? null : _document.Name;

    /// <summary>
    /// Expands the inspector from its rail (tablet, where at most ONE panel is expanded). Expanding a panel
    /// by hand is a USER action, so the palette it rails is reported through
    /// <see cref="ToolboxCollapsedChanged"/> (U1: only a user collapse does).
    /// </summary>
    private async Task ExpandPropertiesPanelAsync()
    {
        _rightOpen = true;
        // The button that raised this unmounts with the rail: arm the focus recovery for the expanded
        // panel's own toggle (review item 18c — a keyboard user must not land on <body>).
        _focusPanelToggleAfterRender = "right";
        if (!ToolboxCollapsedEffective)
        {
            _toolboxCollapsedInternal = true;
            StateHasChanged();
            await ToolboxCollapsedChanged.InvokeAsync(true);
            return;
        }

        StateHasChanged();
    }

    /// <summary>The D10 mapping: the host's <see cref="ToolboxCollapsed"/> IS the shell's left-panel rail.</summary>
    private EditorShellPanel ShellCollapsedPanels
        => ToolboxCollapsedEffective ? EditorShellPanel.Left : EditorShellPanel.None;

    /// <summary>
    /// The shell's collapse set changed. A rail the LAYOUT imposed — the tablet one-expanded-panel rule,
    /// which the shell applies while resolving a layout transition (D20, U1) — is a LAYOUT STATE: it is
    /// neither adopted into <see cref="ToolboxCollapsed"/> nor reported through
    /// <see cref="ToolboxCollapsedChanged"/>. Only a USER collapse/expand is a preference, and only that
    /// raises the callback.
    /// <para>
    /// The decision is synchronous (U2): the shell raises <c>ResolvedLayoutChanged</c> first, so a
    /// layout-imposed rail arrives while <see cref="LayoutChangedInThisBatch"/> is still true. Adopting it
    /// would pin the palette railed for ever (measured: the Mobile→Tablet flip railed the palette and no
    /// toggle could undo it, because the host had been told it was a preference).
    /// </para>
    /// <para>
    /// The right panel's rail is deliberately not surfaced — the editor has no parameter for it, and a
    /// state the host cannot bind would only be noise.
    /// </para>
    /// </summary>
    private Task OnShellCollapsedPanelsChangedAsync(EditorShellPanel panels)
    {
        if (LayoutChangedInThisBatch) return Task.CompletedTask;

        var collapsed = panels.HasFlag(EditorShellPanel.Left);
        if (collapsed == ToolboxCollapsedEffective) return Task.CompletedTask;

        _toolboxCollapsedInternal = collapsed;
        StateHasChanged();
        return ToolboxCollapsedChanged.InvokeAsync(collapsed);
    }

    /// <summary>
    /// Adopts a HOST-supplied <see cref="ActiveMobilePanel"/> when the parameter CHANGES (the shell's
    /// <c>_activeParamSeen</c> pattern). Without it a one-way host could set the parameter once and never
    /// move the mobile sheet again: the editor's own shadow (<c>_activeMobilePanel</c>) kept winning over
    /// the host's new value.
    /// </summary>
    private void ApplyActiveMobilePanelParameter()
    {
        if (ActiveMobilePanel == _activeParamSeen) return;
        _activeParamSeen = ActiveMobilePanel;
        _activeMobilePanel = ActiveMobilePanel;
    }

    /// <summary>
    /// Adopts a changed <see cref="LayoutMode"/> as a layout generation: the shell resolves the forced mode
    /// on the same parameter pass, and the rail it imposes on a tablet must not be mistaken for a user
    /// collapse (U2). Runs from <c>OnParametersSet</c>, i.e. before the shell's own parameter pass.
    /// </summary>
    private void ApplyLayoutModeParameter()
    {
        if (_layoutModeParamSeen != LayoutMode) _layoutGeneration++;
        _layoutModeParamSeen = LayoutMode;
    }

    /// <summary>
    /// Marks the generation the render settled: from here on a collapse raise is a USER action again.
    /// </summary>
    private void SettleLayoutGeneration() => _layoutGenerationAtRender = _layoutGeneration;

    /// <summary>The mobile panel shown: the host's parameter while its callback is bound, else the editor's own state.</summary>
    private EditorShellPanel EffectiveActiveMobilePanel
        => ActiveMobilePanelChanged.HasDelegate ? ActiveMobilePanel : _activeMobilePanel ?? ActiveMobilePanel;

    private async Task OnShellActiveMobilePanelChangedAsync(EditorShellPanel panel)
    {
        _activeMobilePanel = panel;
        await ActiveMobilePanelChanged.InvokeAsync(panel);
        StateHasChanged();
    }

    /// <summary>
    /// Shows a panel on a mobile layout (the selection and "add block" flows). A no-op above the mobile
    /// breakpoint: a desktop host's click must not silently change what the phone layout will show.
    /// </summary>
    private async Task ShowMobilePanelAsync(EditorShellPanel panel)
    {
        if (!IsMobileLayout) return;

        // The sheet may be CLOSED (the user dismissed it, or an insert collapsed it): a selection has to
        // bring the panel it needs back, otherwise the tap looks like it did nothing.
        if (panel == EditorShellPanel.Right) _rightOpen = true;
        else if (panel == EditorShellPanel.Left) _leftOpen = true;

        if (EffectiveActiveMobilePanel != panel)
        {
            _activeMobilePanel = panel;
            await ActiveMobilePanelChanged.InvokeAsync(panel);
        }

        StateHasChanged();
    }

    /// <summary>
    /// Reveals the canvas after an INSERT on a mobile layout so the new block/section is visible and can
    /// take the focus: the <see cref="MobilePanelPresentation.Tabs"/> presentation has no sheet to close,
    /// so it switches to its CANVAS tab (<see cref="EditorShellPanel.None"/>); the Sheet presentation
    /// closes the sheet. Above the mobile breakpoint the docked panels are untouched.
    /// </summary>
    private Task RevealCanvasAfterInsertAsync()
        => MobilePanelPresentation == MobilePanelPresentation.Tabs
            ? ShowMobilePanelAsync(EditorShellPanel.None)
            : CollapseMobileSheetAsync();

    /// <summary>
    /// Collapses the mobile sheet so the just-added block is visible on the canvas (and the focus lands on
    /// it). Above the mobile breakpoint the docked panels are untouched.
    /// </summary>
    private Task CollapseMobileSheetAsync()
    {
        if (!IsMobileLayout) return Task.CompletedTask;
        _mobileOverflowOpen = false;
        _leftOpen = false;
        _rightOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    private Task OnLeftOpenChangedAsync(bool open)
    {
        _leftOpen = open;
        return Task.CompletedTask;
    }

    private Task OnRightOpenChangedAsync(bool open)
    {
        _rightOpen = open;
        return Task.CompletedTask;
    }

    private Task OnSheetSnapIndexChangedAsync(int index)
    {
        _sheetSnapIndex = index;
        return Task.CompletedTask;
    }

    private Task OnMobileOverflowOpenChanged(bool open)
    {
        _mobileOverflowOpen = open;
        return Task.CompletedTask;
    }
}
