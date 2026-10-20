using Tempo.Blazor.Abstractions.Layout;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Components.Dropdowns;
using Tempo.Blazor.Components.Overlay;
using Tempo.Blazor.Tests.Localization;
using Xunit;

namespace Tempo.Blazor.Tests.DataTable;

public sealed class TmDataTableMobileOverlayTests : LocalizationTestBase
{
    [Theory]
    [InlineData(DataTableMobileLayout.None, PanelPresentation.Popover)]
    [InlineData(DataTableMobileLayout.Scroll, PanelPresentation.Auto)]
    public void ExportDropdownPresentation_FollowsMobileLayout(DataTableMobileLayout layout, PanelPresentation expected)
    {
        var cut = Render<TmDataTable<string>>(p => p
            .Add(c => c.ViewContext, "items")
            .Add(c => c.Items, new[] { "A" })
            .Add(c => c.ShowExport, true)
            .Add(c => c.ShowSearch, false)
            .Add(c => c.ShowColumnPicker, false)
            .Add(c => c.ShowPagination, false)
            .Add(c => c.MobileLayout, layout)
            .AddChildContent<TmDataTableColumn<string>>(c => c
                .Add(x => x.Title, "Value")
                .Add(x => x.PropertyName, "Value")));

        var exportDropdown = cut.FindComponent<TmDropdown>();
        Assert.Equal(expected, exportDropdown.Instance.MobilePresentation);
    }
}
