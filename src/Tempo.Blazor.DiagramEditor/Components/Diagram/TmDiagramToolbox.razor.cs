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

    /// <summary>Whether the panel renders its own heading. Hosts that already label the panel (a tab, a sheet tab) pass false. Defaults to true.</summary>
    [Parameter] public bool ShowTitle { get; set; } = true;

    /// <summary>
    /// UX review (the mobile Shapes sheet): when true, every category except the FIRST one (the
    /// General/basic palette) starts collapsed — the sheet opens on the essentials and the user
    /// expands the rest. Applied once; the user's own expand/collapse choices win afterwards.
    /// </summary>
    [Parameter] public bool CollapseCategoriesExceptFirst { get; set; }

    /// <summary>
    /// Fáze 15 (the Modeling "Open in editor" notation palette): stencil ids to pin as the FIRST
    /// library — the palette of the active notation — while every other library starts collapsed
    /// (search still spans the whole stencil library). Order is preserved; unknown ids are
    /// ignored. <c>null</c> (the default) keeps today's toolbox: no pinned library, all palettes
    /// expanded.
    /// </summary>
    [Parameter] public IReadOnlyList<string>? PreferredStencilIds { get; set; }

    /// <summary>
    /// Header label of the pinned library (the Modeling dialog passes the notation profile's
    /// display name). Null falls back to the localized <c>TmDiagramToolbox_PreferredDefault</c>.
    /// Ignored while <see cref="PreferredStencilIds"/> is unset.
    /// </summary>
    [Parameter] public string? PreferredStencilsLabel { get; set; }

    /// <summary>Synthetic set/palette id of the pinned (preferred-stencils) library.</summary>
    internal const string PreferredLibraryId = "notation-palette";

    private bool _collapsed;
    private readonly HashSet<string> _collapsedPalettes = new(StringComparer.Ordinal);
    private ElementReference _containerRef;
    private bool _initialized;

    /// <summary>Whether the <see cref="CollapseCategoriesExceptFirst"/> default was applied.</summary>
    private bool _categoryDefaultsApplied;

    /// <summary>The last-applied <see cref="CollapseCategoriesExceptFirst"/> value — a change
    /// rebuilds the library groups (the flag drives the General-first ordering inside
    /// <see cref="RefreshFilter"/>).</summary>
    private bool _collapseCategoriesExceptFirstApplied;

    /// <summary>Signature of the last-applied preferred set — a change (a notation switch)
    /// rebuilds the groups and re-collapses the non-pinned palettes.</summary>
    private string? _preferredApplied;

    /// <summary>Per-instance id of the visually-hidden read-only hint: two toolboxes on one page
    /// must never share an id (a duplicate id breaks every aria-describedby reference).</summary>
    private string ReadOnlyHintId { get; } = $"tm-diagram-toolbox-readonly-hint-{Guid.NewGuid():N}";

    private string _searchQuery = string.Empty;
    private List<ToolboxLibraryGroup> _filteredLibraries = [];

    protected override void OnInitialized()
    {
        RefreshFilter();
    }

    protected override void OnParametersSet()
    {
        // The flag decides the General-first library ordering inside RefreshFilter — a change
        // must rebuild the groups, otherwise the General library stays pinned first (or the
        // alphabetical order keeps it pinned away) until the next search keystroke.
        if (CollapseCategoriesExceptFirst != _collapseCategoriesExceptFirstApplied)
        {
            _collapseCategoriesExceptFirstApplied = CollapseCategoriesExceptFirst;
            RefreshFilter();
        }

        // Fáze 15: the preferred (pinned) set decides the library groups too — a change (the
        // Modeling dialog switching notation) must rebuild them.
        var preferredSignature = GetPreferredSignature(PreferredStencilIds);
        if (!string.Equals(preferredSignature, _preferredApplied, StringComparison.Ordinal))
        {
            _preferredApplied = preferredSignature;
            RefreshFilter();

            // UX review (Fáze 15): with a pinned palette set, every NON-pinned palette starts
            // collapsed — the palette opens on the notation and the rest of the library stays
            // one expand away. Re-applied when the set changes; the user's own choices win
            // afterwards (a live search always expands matches).
            if (preferredSignature is not null)
            {
                foreach (var library in _filteredLibraries)
                {
                    if (string.Equals(library.Id, PreferredLibraryId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    foreach (var palette in library.Palettes)
                    {
                        _collapsedPalettes.Add(palette.Id);
                    }
                }
            }
        }

        // UX review: the mobile Shapes sheet starts with only the FIRST LIBRARY (the General/basic
        // set) expanded — every palette outside it starts collapsed. Applied exactly once —
        // afterwards the user's own choices own the set.
        if (CollapseCategoriesExceptFirst && !_categoryDefaultsApplied && _filteredLibraries.Count > 0)
        {
            _categoryDefaultsApplied = true;
            for (var i = 1; i < _filteredLibraries.Count; i++)
            {
                foreach (var palette in _filteredLibraries[i].Palettes)
                {
                    _collapsedPalettes.Add(palette.Id);
                }
            }
        }
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
        var allStencils = StencilRegistry.GetAll();
        if (string.IsNullOrWhiteSpace(query))
        {
            _filteredLibraries = BuildLibraryGroups(allStencils);
            return;
        }

        // Registry search covers tags/keywords/set ids; the localized display name and the raw
        // name are matched diacritic-insensitively on top (D7), so "obdelnik" finds the rectangle.
        var registryHits = new HashSet<string>(
            StencilRegistry.Search(query).Select(s => s.Id),
            StringComparer.Ordinal);
        _filteredLibraries = BuildLibraryGroups(allStencils.Where(s =>
            registryHits.Contains(s.Id)
            || DiagramSearchText.MatchesAny(query, GetStencilDisplayName(s), s.Name)));
    }

    /// <summary>The pinned (preferred) library: the preferred stencil ids resolved against the
    /// registry in the given order (unknown ids ignored). Null when no preferred set is active.</summary>
    private ToolboxLibraryGroup? BuildPreferredLibrary(IEnumerable<DiagramStencil> stencils)
    {
        if (PreferredStencilIds is null || PreferredStencilIds.Count == 0)
        {
            return null;
        }

        var byId = stencils.ToDictionary(stencil => stencil.Id, StringComparer.Ordinal);
        var preferred = PreferredStencilIds
            .Where(id => !string.IsNullOrWhiteSpace(id) && byId.ContainsKey(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => byId[id])
            .ToList();

        if (preferred.Count == 0)
        {
            return null;
        }

        return new ToolboxLibraryGroup(
            PreferredLibraryId,
            NameResourceKey: null,
            FallbackName: string.IsNullOrWhiteSpace(PreferredStencilsLabel)
                ? Loc["TmDiagramToolbox_PreferredDefault"]
                : PreferredStencilsLabel,
            ShowHeader: true,
            Palettes:
            [
                new ToolboxPaletteGroup(PreferredLibraryId, null, PreferredStencilsLabel ?? Loc["TmDiagramToolbox_PreferredDefault"], 0, preferred)
            ]);
    }

    private static string? GetPreferredSignature(IReadOnlyList<string>? preferred)
        => preferred is null || preferred.Count == 0
            ? null
            : string.Join("|", preferred);

    private List<ToolboxLibraryGroup> BuildLibraryGroups(IEnumerable<DiagramStencil> stencils)
    {
        var libraries = stencils
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

        // UX review (the mobile Shapes sheet): the General/basic library leads — the sheet
        // opens on the essentials and the other categories stay collapsed by default.
        if (CollapseCategoriesExceptFirst)
        {
            libraries = libraries
                .OrderByDescending(l => IsGeneralLibrary(l))
                .ToList();
        }

        // Fáze 15: the pinned (notation palette) library always renders FIRST — the palette is
        // the sheet/dock's opening set — followed by the rest of the stencil library.
        var preferredLibrary = BuildPreferredLibrary(stencils);
        if (preferredLibrary is not null)
        {
            libraries.Insert(0, preferredLibrary);
        }

        return libraries;
    }

    /// <summary>The built-in essentials set ("general"): identified by its set id only — a
    /// localized-name comparison would also match a custom library whose name translates to
    /// "General" and wrongly pin it first.</summary>
    private bool IsGeneralLibrary(ToolboxLibraryGroup library)
        => string.Equals(library.Id, "general", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a palette body renders: a live search expands every palette that has
    /// matches — the collapsed mobile defaults must never hide a search hit.</summary>
    private bool IsPaletteExpanded(string paletteId)
        => !string.IsNullOrWhiteSpace(_searchQuery) || !_collapsedPalettes.Contains(paletteId);

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
