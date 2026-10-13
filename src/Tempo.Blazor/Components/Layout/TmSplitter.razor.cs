using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Layout;

/// <summary>Renders a resizable two-pane splitter layout.</summary>
public partial class TmSplitter : ComponentBase
{
    private ElementReference _splitterRef;
    private readonly List<TmSplitterPane> _panes = new();

    /// <summary>The child content (TmSplitterPane elements).</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    /// <summary>Splitter orientation.</summary>
    [Parameter] public SplitterOrientation Orientation { get; set; } = SplitterOrientation.Horizontal;

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? AdditionalCssClass { get; set; }

    internal IReadOnlyList<TmSplitterPane> Panes => _panes;

    /// <summary>The splitter root element, for measuring the main-axis length (aria-valuemax / End).</summary>
    internal ElementReference SplitterElement => _splitterRef;

    internal bool IsNotLastPane(TmSplitterPane pane) => _panes.IndexOf(pane) < _panes.Count - 1;

    internal void AddPane(TmSplitterPane pane)
    {
        if (!_panes.Contains(pane))
        {
            _panes.Add(pane);
        }
    }

    internal void RemovePane(TmSplitterPane pane)
    {
        _panes.Remove(pane);
    }

    internal void RequestStateUpdate() => StateHasChanged();
}
