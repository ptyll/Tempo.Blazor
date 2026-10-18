using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using System.Globalization;
using Tempo.Blazor.Components.Diagram.Models;

namespace Tempo.Blazor.Components.Diagram;

/// <summary>
/// Toolbox panel showing available diagram stencils that can be dragged onto the canvas.
/// </summary>
public partial class TmDiagramToolbox : TmComponentBase
{
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>Additional CSS class.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>Raised when a stencil drag starts.</summary>
    [Parameter] public EventCallback<string> StencilDragStart { get; set; }

    /// <summary>Raised when a focused stencil item is activated from the keyboard.</summary>
    [Parameter] public EventCallback<string> StencilKeyboardInsert { get; set; }

    /// <summary>When true, stencil tiles are inert: not draggable, aria-disabled, no insert on click or keys.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    private bool _collapsed;
    private readonly HashSet<string> _collapsedPalettes = new(StringComparer.Ordinal);
    private ElementReference _containerRef;
    private bool _initialized;

    /// <summary>Per-instance id of the visually-hidden read-only hint: two toolboxes on one page
    /// must never share an id (a duplicate id breaks every aria-describedby reference).</summary>
    private string ReadOnlyHintId { get; } = $"tm-diagram-toolbox-readonly-hint-{Guid.NewGuid():N}";

    private string _searchQuery = string.Empty;
    private List<ToolboxLibraryGroup> _filteredLibraries = [];

    protected override void OnInitialized()
    {
        RefreshFilter();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_initialized)
        {
            _initialized = true;
            await JS.InvokeVoidAsync("tmDiagramEditor.initToolbox", _containerRef);
        }
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
    }

    private void TogglePalette(string paletteId)
    {
        if (_collapsedPalettes.Contains(paletteId))
            _collapsedPalettes.Remove(paletteId);
        else
            _collapsedPalettes.Add(paletteId);
    }

    private async Task OnDragStart(DragEventArgs e, string stencilId)
    {
        if (ReadOnly) return;
        await StencilDragStart.InvokeAsync(stencilId);
    }

    /// <summary>Enter inserts on keydown (one per press — auto-repeat ignored); Space is handled on keyup.</summary>
    private Task OnStencilKeyDown(KeyboardEventArgs e, string stencilId) =>
        !ReadOnly && e.Key == "Enter" && !e.Repeat
            ? StencilKeyboardInsert.InvokeAsync(stencilId)
            : Task.CompletedTask;

    /// <summary>Space activates on keyup, where a native button fires its click (keyboard convention).</summary>
    private Task OnStencilKeyUp(KeyboardEventArgs e, string stencilId) =>
        !ReadOnly && e.Key is " " or "Spacebar"
            ? StencilKeyboardInsert.InvokeAsync(stencilId)
            : Task.CompletedTask;

    /// <summary>
    /// Phase 6 (tablet and touch): tapping a tile is the non-drag alternative to the HTML5
    /// drag — the same insert-at-viewport-centre path the keyboard uses (WCAG 2.5.7). An edge
    /// stencil tap arms the edge tool instead of inserting a node, exactly like its drag does.
    /// Inert in read-only.
    /// </summary>
    private Task OnStencilActivated(string stencilId) =>
        ReadOnly ? Task.CompletedTask : StencilKeyboardInsert.InvokeAsync(stencilId);

    private void OnSearchChanged(string value)
    {
        _searchQuery = value;
        RefreshFilter();
    }

    /// <summary>Culture-aware comparer for the visible labels: Czech users get Czech alphabetical order.
    /// Cached per culture — the comparer is created once per culture, not once per call.</summary>
    private StringComparer DisplayComparer
    {
        get
        {
            var culture = Loc.Culture ?? CultureInfo.CurrentUICulture;
            if (_displayComparer is null || !Equals(_displayComparerCulture, culture))
            {
                _displayComparer = StringComparer.Create(culture, ignoreCase: true);
                _displayComparerCulture = culture;
            }
            return _displayComparer;
        }
    }

    private StringComparer? _displayComparer;
    private CultureInfo? _displayComparerCulture;

    private void RefreshFilter()
    {
        var query = _searchQuery.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            _filteredLibraries = BuildLibraryGroups(StencilRegistry.GetAll());
            return;
        }

        // Registry search covers tags/keywords/set ids; the localized display name and the raw
        // name are matched diacritic-insensitively on top (D7), so "obdelnik" finds the rectangle.
        var registryHits = new HashSet<string>(
            StencilRegistry.Search(query).Select(s => s.Id),
            StringComparer.Ordinal);
        _filteredLibraries = BuildLibraryGroups(StencilRegistry.GetAll().Where(s =>
            registryHits.Contains(s.Id)
            || DiagramSearchText.MatchesAny(query, GetStencilDisplayName(s), s.Name)));
    }

    private List<ToolboxLibraryGroup> BuildLibraryGroups(IEnumerable<DiagramStencil> stencils)
        => stencils
            .GroupBy(GetSetId, StringComparer.Ordinal)
            .Select(setGroup =>
            {
                var firstSetStencil = setGroup.First();
                var palettes = setGroup
                    .GroupBy(GetPaletteId, StringComparer.Ordinal)
                    .Select(paletteGroup =>
                    {
                        var firstPaletteStencil = paletteGroup.First();
                        return new ToolboxPaletteGroup(
                            paletteGroup.Key,
                            GetPaletteNameResourceKey(firstPaletteStencil),
                            GetPaletteFallbackName(firstPaletteStencil),
                            GetPaletteOrder(firstPaletteStencil),
                            paletteGroup.OrderBy(stencil => stencil.Order)
                                        .ThenBy(GetStencilDisplayName, DisplayComparer)
                                        .ToList());
                    })
                    .OrderBy(palette => palette.Order)
                    .ThenBy(palette => GetLocalizedText(palette.NameResourceKey, palette.FallbackName), DisplayComparer)
                    .ToList();

                return new ToolboxLibraryGroup(
                    setGroup.Key,
                    GetSetNameResourceKey(firstSetStencil),
                    GetSetFallbackName(firstSetStencil),
                    ShouldShowLibraryHeader(firstSetStencil, palettes),
                    palettes);
            })
            .OrderBy(library => GetLocalizedText(library.NameResourceKey, library.FallbackName), DisplayComparer)
            .ToList();

    private string GetLocalizedText(string? resourceKey, string fallback)
    {
        if (string.IsNullOrWhiteSpace(resourceKey))
            return fallback;

        var localized = Loc[resourceKey];
        return string.Equals(localized, $"[{resourceKey}]", StringComparison.Ordinal)
            ? fallback
            : localized;
    }

    private string GetStencilDisplayName(DiagramStencil stencil)
        => GetLocalizedText(stencil.NameResourceKey, stencil.Name);

    private string GetStencilTooltip(string stencilDisplayName)
        => Loc["TmDiagramToolbox_DragStencil", stencilDisplayName];

    private string GetStencilAriaLabel(string stencilDisplayName)
        => Loc["TmDiagramToolbox_InsertStencil", stencilDisplayName];

    /// <summary>Read-only tiles stay visible but inert (A8/D9): the hint replaces the drag
    /// tooltip so the tile never advertises an interaction it refuses.</summary>
    private string GetReadOnlyHint()
        => Loc["TmDiagramToolbox_ReadOnlyHint"];

    private static string GetStencilItemCssClass(DiagramStencil stencil)
        => stencil.Kind == DiagramStencilKind.Edge
            ? "tm-diagram-toolbox__item tm-diagram-toolbox__item--edge"
            : "tm-diagram-toolbox__item";

    private static string GetStencilKindValue(DiagramStencil stencil)
        => stencil.Kind == DiagramStencilKind.Edge ? "edge" : "node";

    private static string GetSetId(DiagramStencil stencil)
        => !string.IsNullOrWhiteSpace(stencil.SetId) ? stencil.SetId : stencil.Category;

    private static string GetSetNameResourceKey(DiagramStencil stencil)
        => stencil.SetNameResourceKey;

    private static string GetSetFallbackName(DiagramStencil stencil)
        => !string.IsNullOrWhiteSpace(stencil.SetId) ? stencil.SetId : stencil.Category;

    private static string GetPaletteId(DiagramStencil stencil)
        => !string.IsNullOrWhiteSpace(stencil.PaletteId) ? stencil.PaletteId : stencil.Category;

    private static string GetPaletteNameResourceKey(DiagramStencil stencil)
        => stencil.PaletteNameResourceKey;

    private static string GetPaletteFallbackName(DiagramStencil stencil)
        => !string.IsNullOrWhiteSpace(stencil.PaletteId) ? stencil.PaletteId : stencil.Category;

    private static int GetPaletteOrder(DiagramStencil stencil)
        => stencil.PaletteOrder;

    private static bool ShouldShowLibraryHeader(DiagramStencil stencil, List<ToolboxPaletteGroup> palettes)
        => !string.IsNullOrWhiteSpace(stencil.SetNameResourceKey)
            || palettes.Count > 1;

    private sealed record ToolboxLibraryGroup(
        string Id,
        string NameResourceKey,
        string FallbackName,
        bool ShowHeader,
        List<ToolboxPaletteGroup> Palettes);

    private sealed record ToolboxPaletteGroup(
        string Id,
        string NameResourceKey,
        string FallbackName,
        int Order,
        List<DiagramStencil> Stencils);
}
