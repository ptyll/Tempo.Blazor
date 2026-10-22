using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Tempo.Blazor.Components.Icons;
using Tempo.Blazor.Components.Wireframe.Models;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Collapsible component palette for the wireframe editor.
/// Items are draggable onto <see cref="TmWireframeDesignerCanvas"/> via HTML5 drag-and-drop.
/// </summary>
public partial class TmWireframeToolbox : ComponentBase
{
    // ── DI ────────────────────────────────────────────────────────────────────

    [Inject] private WireframeComponentRegistry _registry { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    // ── Parameters ────────────────────────────────────────────────────────────

    /// <summary>Additional CSS class on the toolbox wrapper.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Raised when the user activates an item by click, tap or keyboard (Enter/Space) — exactly once per
    /// gesture. This is the non-drag alternative to HTML5 drag-and-drop (WCAG 2.5.7): the parent adds the
    /// component of the carried type string, typically centred in the visible canvas area.
    /// </summary>
    [Parameter] public EventCallback<string> OnComponentActivated { get; set; }

    /// <summary>Optional application scope used to resolve custom wireframe components.</summary>
    [Parameter] public WireframeComponentScope? ComponentScope { get; set; }

    /// <summary>
    /// When <c>true</c> the palette is shown but inert: items are <c>aria-disabled</c>, muted, not draggable and
    /// never raise <see cref="OnComponentActivated"/>. <c>TmWireframeEditor</c> passes its effective read-only state.
    /// </summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>
    /// Presents the palette as a single column of rows (icon + name) instead of the 3-column tile grid. The tablet editor
    /// sets it for its overlay drawer, where each row is a finger-sized target.
    /// </summary>
    [Parameter] public bool SingleColumn { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    private ElementReference _containerRef;
    private bool _toolboxJsInit;

    private string _search = "";
    private string _filterMode = FilterAll;

    private const string FilterAll     = "All";
    private const string FilterBuiltIn = "Built-in";
    private const string FilterCustom  = "Custom";

    private static readonly string[] _filters = [FilterAll, FilterBuiltIn, FilterCustom];

    // Cached registry data
    private WireframeComponentDef[] _allDefs     = [];
    private string[]                _allCategories = [];
    private bool                    _hasCustomComponents;

    // Render gate: a parent re-render that changes nothing the toolbox shows must not re-render the
    // (large) palette; the editor re-renders on every pointer move / selection change.
    private bool _parentPass;
    private bool _paramsChanged;
    private bool _firstRender = true;
    private bool _readOnlySeen;
    private bool _singleColumnSeen;
    private string? _classSeen;
    private WireframeComponentScope? _scopeSeen;
    private EventCallback<string> _callbackSeen;

    /// <summary>Number of completed renders (test hook for the parent-render gate).</summary>
    internal int RenderCount { get; private set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public override Task SetParametersAsync(ParameterView parameters)
    {
        _parentPass = true;
        return base.SetParametersAsync(parameters);
    }

    /// <inheritdoc/>
    protected override void OnParametersSet()
    {
        var defs = _registry.GetAll(ComponentScope).ToArray();
        var registryChanged = !defs.SequenceEqual(_allDefs);

        _allDefs          = defs;
        _allCategories    = _registry.GetCategories(ComponentScope);
        _hasCustomComponents = _allDefs.Any(d => !d.IsBuiltIn);

        var changed = registryChanged
                      || ReadOnly != _readOnlySeen
                      || SingleColumn != _singleColumnSeen
                      || !ReferenceEquals(ComponentScope, _scopeSeen)
                      || !string.Equals(Class, _classSeen, StringComparison.Ordinal)
                      || !OnComponentActivated.Equals(_callbackSeen);
        _readOnlySeen = ReadOnly;
        _singleColumnSeen = SingleColumn;
        _scopeSeen    = ComponentScope;
        _classSeen    = Class;
        _callbackSeen = OnComponentActivated;
        if (changed) _paramsChanged = true;
    }

    /// <inheritdoc/>
    protected override bool ShouldRender()
    {
        var render = !_parentPass || _paramsChanged || _firstRender;
        _parentPass = false;
        _paramsChanged = false;
        return render;
    }

    /// <inheritdoc/>
    protected override void OnAfterRender(bool firstRender)
    {
        // ShouldRender is not consulted for the very first render, so the pass flags are also reset here.
        _firstRender = false;
        _parentPass = false;
        _paramsChanged = false;
        RenderCount++;
    }

    /// <inheritdoc/>
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _toolboxJsInit) return;
        _toolboxJsInit = true;
        await JS.InvokeVoidAsync("tmWireframeDesigner.initToolbox", _containerRef);
    }

    // ── Filtering ─────────────────────────────────────────────────────────────

    private IEnumerable<WireframeComponentDef> FilteredDefs
    {
        get
        {
            var defs = _allDefs.AsEnumerable();

            // Filter by built-in / custom
            defs = _filterMode switch
            {
                FilterBuiltIn => defs.Where(d => d.IsBuiltIn),
                FilterCustom  => defs.Where(d => !d.IsBuiltIn),
                _             => defs
            };

            // Filter by search
            if (!string.IsNullOrWhiteSpace(_search))
            {
                var term = _search.Trim();
                defs = defs.Where(d =>
                    d.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    d.Type.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    d.Category.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            return defs;
        }
    }

    private IEnumerable<string> _visibleCategories => FilteredDefs
        .Select(d => d.Category)
        .Distinct()
        .OrderBy(c => c);

    private IEnumerable<WireframeComponentDef> GetVisibleItems(string category)
        => FilteredDefs
            .Where(d => d.Category == category)
            .OrderBy(d => d.DisplayName);

    /// <summary>
    /// True when the definition names an icon that <see cref="TmIcon"/> can actually draw (built-in or registered).
    /// An unknown name would paint an empty tile, so such a definition keeps the mini-SVG preview instead.
    /// </summary>
    private static bool HasResolvableIcon(WireframeComponentDef def)
        => !string.IsNullOrEmpty(def.Icon)
           && (!string.IsNullOrEmpty(TmIcon.GetBuiltInSvg(def.Icon)) || IconRegistry.Contains(def.Icon));

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnSearchInput(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? "";
    }

    private void ClearSearch()
    {
        _search = "";
    }

    private void SetFilter(string mode)
    {
        _filterMode = mode;
    }

    private string GetFilterLabel(string filter) => filter switch
    {
        FilterBuiltIn => Loc["TmWireframeToolbox_FilterBuiltIn"],
        FilterCustom  => Loc["TmWireframeToolbox_FilterCustom"],
        _             => Loc["TmWireframeToolbox_FilterAll"],
    };

    private Task ActivateAsync(string componentType)
        => ReadOnly ? Task.CompletedTask : OnComponentActivated.InvokeAsync(componentType);

    /// <summary>
    /// Keyboard activation for the <c>div[role=button]</c> item (no native activation exists, so it is
    /// emulated — see docs/keyboard-activation-convention.md): Enter fires on keydown (auto-repeat
    /// ignored), Space fires on keyup, exactly where a native button would click.
    /// </summary>
    private async Task OnItemKeyDown(KeyboardEventArgs e, string componentType)
    {
        if (e.Key == "Enter" && !e.Repeat)
            await ActivateAsync(componentType);
    }

    private async Task OnItemKeyUp(KeyboardEventArgs e, string componentType)
    {
        if (e.Key == " ")
            await ActivateAsync(componentType);
    }
}
