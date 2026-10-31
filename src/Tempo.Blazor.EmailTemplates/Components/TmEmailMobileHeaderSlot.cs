using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Abstractions.Layout;

namespace Tempo.Blazor.EmailTemplates.Components;

/// <summary>
/// Internal. The email editor's <c>Header</c> slot: renders its content ONLY on a mobile layout and
/// reports the layout the shell resolved back to the editor.
/// <para>
/// Two jobs, one component, because both need the SAME input — the <see cref="TmLayoutContext"/> the
/// shell cascades into its slots. A nested slot must never measure on its own (a private observer would
/// classify with the shared 640/1024 thresholds instead of the editor's 768/1200 breakpoints), and the
/// shell's <c>ResolvedLayoutChanged</c> callback deliberately reports CHANGES only — the initial value
/// is not a change, except that an Auto observer which inherits a non-Desktop resolution from a
/// cascading parent reports it once so late-mounted consumers adopt it — and a callback-only path would
/// still depend on the host wiring it. The cascade is the one source that is correct in both cases
/// (docs/editor-shell.md).
/// </para>
/// </summary>
internal sealed class TmEmailMobileHeaderSlot : ComponentBase
{
    private TmLayoutMode? _reported;

    /// <summary>The layout the shell cascaded into this slot.</summary>
    [CascadingParameter] public TmLayoutContext? Layout { get; set; }

    /// <summary>The mobile header chrome. Never rendered above the mobile breakpoint.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Raised with the resolved layout whenever it differs from the previously reported one.</summary>
    [Parameter] public EventCallback<TmLayoutMode> ResolvedLayoutChanged { get; set; }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (Layout is not { } layout || _reported == layout.Resolved) return;
        _reported = layout.Resolved;
        await ResolvedLayoutChanged.InvokeAsync(layout.Resolved);
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        // The desktop header region stays EMPTY (the editor's stylesheet collapses the empty band), so
        // the mobile-only actions never exist twice in the DOM on a wide layout.
        if (ChildContent is null || Layout is not { IsMobile: true }) return;
        builder.AddContent(0, ChildContent);
    }
}
