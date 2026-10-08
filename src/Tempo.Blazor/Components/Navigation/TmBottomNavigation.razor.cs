using Microsoft.AspNetCore.Components;

namespace Tempo.Blazor.Components.Navigation;

/// <summary>
/// A mobile bottom navigation bar for switching the application's top-level destinations.
/// This is application chrome: it navigates between pages/sections and typically anchors to
/// the viewport for the whole session. It is NOT a component action bar — the actions of one
/// component (edit, save, filter, …) belong to <c>TmMobileActionBar</c> (F5), which lives
/// inside the component's own container (sticky, never fixed). See
/// <c>docs/responsive-conventions.md</c> ("Bottom navigation vs action bar").
/// </summary>
public partial class TmBottomNavigation : ComponentBase
{
    /// <summary>The navigation items.</summary>
    [Parameter] public IReadOnlyList<BottomNavItem> Items { get; set; } = [];

    /// <summary>The currently selected item.</summary>
    [Parameter] public BottomNavItem? SelectedItem { get; set; }

    /// <summary>Event fired when an item is clicked.</summary>
    [Parameter] public EventCallback<BottomNavItem> OnItemClick { get; set; }

    /// <summary>Additional CSS classes.</summary>
    [Parameter] public string? AdditionalCssClass { get; set; }

    private async Task HandleClick(BottomNavItem item)
    {
        if (item.Disabled)
            return;

        SelectedItem = item;
        await OnItemClick.InvokeAsync(item);
    }
}
