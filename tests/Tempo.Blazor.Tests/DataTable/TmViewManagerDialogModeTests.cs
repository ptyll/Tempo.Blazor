using Bunit;
using FluentAssertions;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.DataTable;

public class TmViewManagerDialogModeTests : LocalizationTestBase
{
    [Fact]
    public void CreateDialog_UsesCreateTitle_AndNoSaveHelper()
    {
        var cut = Render<TmViewManager>(p => p.Add(c => c.ViewContext, "phase4"));
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Markup.Should().Contain("Create New View");
        cut.Markup.Should().NotContain("Keeps the columns, grouping and filters");
    }
}
