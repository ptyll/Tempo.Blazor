using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Tempo.Blazor.Components.Navigation;
using Tempo.Blazor.Components.Wireframe.Models;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Components.Wireframe;

/// <summary>
/// Internal left panel of <see cref="TmWireframeEditor"/>: Components, Pages and Layers behind one <c>TmTabs</c> strip.
/// It owns the active-tab state, so a tab switch re-renders only this panel — never the canvas. <c>TmTabs</c> draws
/// only the strip (its panels carry no content); the tab panels are rendered here: a tab is mounted the first time it
/// is shown (lazy) and is only hidden afterwards, so the toolbox keeps its search text and scroll position and a tab
/// switch never re-renders it (the toolbox's own render gate skips a parent pass with unchanged parameters). With one
/// available tab the strip is dropped. A plain C# component: Razor can only open PUBLIC components from markup.
/// </summary>
internal sealed class TmWireframeSidePanel : ComponentBase
{
    internal const string ComponentsTab = "components";
    internal const string PagesTab = "pages";
    internal const string LayersTab = "layers";

    private string? _active;

    // Tab and panel ids reach the DOM (TmTabs derives tab-{id} / tabpanel-{id} from them): one editor page holds several
    // editors, so the ids carry a per-instance prefix and stay unique.
    private readonly string _uid = Guid.NewGuid().ToString("N")[..8];

    private string Prefixed(string tab) => $"wd-{_uid}-{tab}";

    private string Unprefixed(string id) => id.StartsWith($"wd-{_uid}-", StringComparison.Ordinal) ? id[$"wd-{_uid}-".Length..] : id;

    // Tabs shown at least once stay mounted (hidden when inactive): lazy first render, no remount later.
    private readonly HashSet<string> _mounted = new(StringComparer.Ordinal);

    [Inject] private ITmLocalizer Loc { get; set; } = default!;

    /// <summary>Document whose pages and layers the tabs show.</summary>
    [Parameter] public WireframeDocument? Document { get; set; }

    /// <summary>Raised after a page or layer operation changed the document.</summary>
    [Parameter] public EventCallback<WireframeDocument> DocumentChanged { get; set; }

    /// <summary>Show the Components tab (the toolbox).</summary>
    [Parameter] public bool ShowComponents { get; set; } = true;

    /// <summary>Show the Pages tab (<see cref="TmWireframePagesPanel"/>).</summary>
    [Parameter] public bool ShowPages { get; set; }

    /// <summary>Show the Layers tab (<see cref="TmWireframeLayersPanel"/>).</summary>
    [Parameter] public bool ShowLayers { get; set; } = true;

    /// <summary>Editing is blocked in every tab.</summary>
    [Parameter] public bool ReadOnly { get; set; }

    /// <summary>Optional application scope used to resolve custom wireframe components.</summary>
    [Parameter] public WireframeComponentScope? ComponentScope { get; set; }

    /// <summary>Raised when a toolbox item is activated (click, tap, Enter, Space).</summary>
    [Parameter] public EventCallback<string> OnComponentActivated { get; set; }

    /// <summary>The selected element ids (the layers tab moves them between layers).</summary>
    [Parameter] public string[] SelectedElementIds { get; set; } = [];

    /// <summary>The active layer id.</summary>
    [Parameter] public string? ActiveLayerId { get; set; }

    /// <summary>Raised when the user picks another active layer.</summary>
    [Parameter] public EventCallback<string?> ActiveLayerIdChanged { get; set; }

    /// <summary>Raised with the id of the page that became active in the Pages tab.</summary>
    [Parameter] public EventCallback<string> ActivePageChanged { get; set; }

    /// <summary>Tablet / sheet presentation: the Components tab is a single column of named rows (a finger-sized list) instead of the 3-column tile grid.</summary>
    [Parameter] public bool SingleColumn { get; set; }

    /// <summary>The stencil theme the toolbox previews are drawn with (see <see cref="TmWireframeToolbox.StencilTheme"/>).</summary>
    [Parameter] public string? StencilTheme { get; set; }

    /// <summary>Namespaces every internal <c>data-testid</c> (forwarded from the editor's <c>TestIdPrefix</c>).</summary>
    [Parameter] public string? TestIdPrefix { get; set; }

    private string Prefix(string name) => string.IsNullOrEmpty(TestIdPrefix) ? name : $"{TestIdPrefix}-{name}";

    /// <summary>Number of tabs that are available.</summary>
    internal int TabCount => (ShowComponents ? 1 : 0) + (ShowPages ? 1 : 0) + (ShowLayers ? 1 : 0);

    /// <summary>The selected tab: the user's choice while it is available, otherwise the first available one.</summary>
    internal string ActiveTab => _active is not null && IsAvailable(_active)
        ? _active
        : ShowComponents ? ComponentsTab : ShowPages ? PagesTab : LayersTab;

    private bool IsAvailable(string tab) => tab switch
    {
        ComponentsTab => ShowComponents,
        PagesTab => ShowPages,
        LayersTab => ShowLayers,
        _ => false,
    };

    private bool IsMounted(string tab) => _mounted.Contains(tab) || ActiveTab == tab;

    /// <inheritdoc/>
    protected override void OnParametersSet() => _mounted.Add(ActiveTab);

    private void SetActiveTab(string tab)
    {
        _active = tab;
        _mounted.Add(tab);
    }

    /// <inheritdoc/>
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        var tabbed = TabCount > 1;
        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", "tm-wd-side");
        b.AddAttribute(2, "data-testid", Prefix("wd-side-panel"));

        if (tabbed)
        {
            b.OpenComponent<TmTabs>(3);
            b.AddComponentParameter(4, nameof(TmTabs.ActiveTabId), Prefixed(ActiveTab));
            b.AddComponentParameter(5, nameof(TmTabs.ActiveTabIdChanged),
                EventCallback.Factory.Create<string>(this, id => SetActiveTab(Unprefixed(id))));
            b.AddComponentParameter(6, nameof(TmTabs.Class), "tm-wd-side__tabs");
            b.AddComponentParameter(7, nameof(TmTabs.ChildContent), (RenderFragment)(tabs =>
            {
                if (ShowComponents) Tab(tabs, 0, ComponentsTab, Loc["TmWireframe_TabComponents"]);
                if (ShowPages) Tab(tabs, 1, PagesTab, Loc["TmWireframePages_Title"]);
                if (ShowLayers) Tab(tabs, 2, LayersTab, Loc["TmWireframe_LayersTitle"]);
            }));
            b.CloseComponent();
        }

        if (ShowComponents && IsMounted(ComponentsTab))
        {
            Panel(b, 10, ComponentsTab, tabbed, panel =>
            {
                panel.OpenComponent<TmWireframeToolbox>(0);
                panel.AddComponentParameter(1, nameof(TmWireframeToolbox.OnComponentActivated), OnComponentActivated);
                panel.AddComponentParameter(2, nameof(TmWireframeToolbox.ComponentScope), ComponentScope);
                panel.AddComponentParameter(3, nameof(TmWireframeToolbox.ReadOnly), ReadOnly);
                panel.AddComponentParameter(4, nameof(TmWireframeToolbox.Class), "tm-wd-editor__toolbox");
                panel.AddComponentParameter(5, nameof(TmWireframeToolbox.SingleColumn), SingleColumn);
                panel.AddComponentParameter(6, nameof(TmWireframeToolbox.StencilTheme), StencilTheme);
                panel.CloseComponent();
            });
        }

        if (ShowPages && IsMounted(PagesTab))
        {
            Panel(b, 20, PagesTab, tabbed, panel =>
            {
                panel.OpenComponent<TmWireframePagesPanel>(0);
                panel.AddComponentParameter(1, nameof(TmWireframePagesPanel.Document), Document);
                panel.AddComponentParameter(2, nameof(TmWireframePagesPanel.DocumentChanged), DocumentChanged);
                panel.AddComponentParameter(3, nameof(TmWireframePagesPanel.ActivePageChanged), ActivePageChanged);
                panel.AddComponentParameter(4, nameof(TmWireframePagesPanel.ReadOnly), ReadOnly);
                panel.AddComponentParameter(5, nameof(TmWireframePagesPanel.Class), "tm-wd-editor__pages");
                panel.AddComponentParameter(7, nameof(TmWireframePagesPanel.TestIdPrefix), TestIdPrefix);
                panel.AddComponentParameter(6, nameof(TmWireframePagesPanel.ShowTitle), false);   // the tab already says "Pages"
                panel.CloseComponent();
            });
        }

        if (ShowLayers && IsMounted(LayersTab))
        {
            Panel(b, 30, LayersTab, tabbed, panel =>
            {
                panel.OpenComponent<TmWireframeLayersPanel>(0);
                panel.AddComponentParameter(1, nameof(TmWireframeLayersPanel.Document), Document);
                panel.AddComponentParameter(2, nameof(TmWireframeLayersPanel.DocumentChanged), DocumentChanged);
                panel.AddComponentParameter(3, nameof(TmWireframeLayersPanel.SelectedElementIds), SelectedElementIds);
                panel.AddComponentParameter(4, nameof(TmWireframeLayersPanel.ActiveLayerId), ActiveLayerId);
                panel.AddComponentParameter(5, nameof(TmWireframeLayersPanel.ActiveLayerIdChanged), ActiveLayerIdChanged);
                panel.AddComponentParameter(6, nameof(TmWireframeLayersPanel.ReadOnly), ReadOnly);
                panel.AddComponentParameter(7, nameof(TmWireframeLayersPanel.ShowCollapseToggle), false);
                panel.AddComponentParameter(8, nameof(TmWireframeLayersPanel.Class), "tm-wd-editor__layers");
                panel.CloseComponent();
            });
        }

        b.CloseElement();
    }

    private void Tab(RenderTreeBuilder b, int seq, string id, string title)
    {
        b.OpenComponent<TmTabPanel>(seq);
        b.AddComponentParameter(1, nameof(TmTabPanel.Id), Prefixed(id));
        b.AddComponentParameter(2, nameof(TmTabPanel.Title), title);
        b.CloseComponent();
    }

    private void Panel(RenderTreeBuilder b, int seq, string tab, bool tabbed, RenderFragment content)
    {
        b.OpenElement(seq, "div");
        b.SetKey(tab);
        b.AddAttribute(seq + 1, "class", "tm-wd-side__panel");
        if (tabbed)
        {
            b.AddAttribute(seq + 2, "role", "tabpanel");
            b.AddAttribute(seq + 3, "id", $"tabpanel-{Prefixed(tab)}");
            b.AddAttribute(seq + 4, "aria-labelledby", $"tab-{Prefixed(tab)}");
        }

        b.AddAttribute(seq + 5, "hidden", ActiveTab != tab);
        b.AddContent(seq + 6, content);
        b.CloseElement();
    }
}
