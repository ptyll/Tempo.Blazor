using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Tempo.Blazor.Abstractions.Models;

namespace Tempo.Blazor.Components.Scheduler;

/// <summary>
/// The text tab strip of <see cref="TmGantt"/> (<see cref="GanttSidebarMode.Tabs"/>). Stateless: the active panel
/// lives on <see cref="TmGanttPart.Owner"/>; the drawers it opens are drawn by <see cref="TmGanttSidebar"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class TmGanttPanelTabs : TmGanttPart
{
    private static readonly GanttSidebarPanel[] TabPanels =
        [GanttSidebarPanel.Tasks, GanttSidebarPanel.Workload, GanttSidebarPanel.History, GanttSidebarPanel.Notifications, GanttSidebarPanel.Reports];

    private readonly Dictionary<GanttSidebarPanel, ElementReference> _tabRefs = [];
    private bool _focusSelectedAfterRender;

    /// <summary>The Tasks tab is selected while no drawer is open (the chart itself is the Tasks panel).</summary>
    private bool IsTabSelected(GanttSidebarPanel panel) =>
        panel == GanttSidebarPanel.Tasks ? Owner._activeSidebarPanel is null : Owner._activeSidebarPanel == panel;

    private void SelectTab(GanttSidebarPanel panel)
    {
        _focusSelectedAfterRender = true;
        Owner.SelectSidebarTab(panel);
    }

    private static string TabIcon(GanttSidebarPanel panel) => panel switch
    {
        GanttSidebarPanel.Tasks => "list",
        GanttSidebarPanel.Workload => "users",
        GanttSidebarPanel.History => "clock",
        GanttSidebarPanel.Notifications => "bell",
        _ => "bar-chart-2",
    };

    private static string TabLabelKey(GanttSidebarPanel panel) => $"TmGantt_Sidebar{panel}";

    /// <summary>ARIA tablist keys: Left / Right wrap, Home / End jump; selection and DOM focus move together.</summary>
    private void HandleTabKeyDown(KeyboardEventArgs e)
    {
        var current = Array.FindIndex(TabPanels, IsTabSelected);
        if (current < 0) current = 0;
        var target = e.Key switch
        {
            "ArrowRight" => (current + 1) % TabPanels.Length,
            "ArrowLeft" => (current - 1 + TabPanels.Length) % TabPanels.Length,
            "Home" => 0,
            "End" => TabPanels.Length - 1,
            _ => current,
        };
        if (target == current) return;
        SelectTab(TabPanels[target]);
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_focusSelectedAfterRender) return;
        _focusSelectedAfterRender = false;
        var selected = Array.Find(TabPanels, IsTabSelected);
        if (_tabRefs.TryGetValue(selected, out var element))
        {
            try
            {
                await element.FocusAsync();
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}