using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Tempo.Blazor.Components.DataTable;
using Tempo.Blazor.Interfaces;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Models;
using Tempo.Blazor.Services;

namespace Tempo.Blazor.Tests.Localization;

public class TmViewManagerLocalizationTests : BunitContext
{
    public TmViewManagerLocalizationTests()
    {
        Services.AddSingleton<ITmLocalizer>(LocalizationTestBase.BuildCzechLocalizer());
        Services.AddScoped<DragDropService>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static IDataTableViewProvider ProviderWithCurrent()
    {
        var provider = Substitute.For<IDataTableViewProvider>();
        provider.GetViewsAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IEnumerable<DataTableView>>([]));
        return provider;
    }

    [Fact]
    public void CzechCulture_RendersPanelAndDialogInCzech()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, ProviderWithCurrent())
            .Add(c => c.ViewContext, "people")
            .Add(c => c.CurrentUserId, "user")
            .Add(c => c.CanCreateTenantViews, true)
            .Add(c => c.GetCurrentView, () => new DataTableView { Name = "Aktivní", VisibleColumns = ["Name"] })
            .Add(c => c.AvailableColumns, [new ViewColumnInfo { Key = "Name", Title = "Jméno", Visible = true }])
            .Add(c => c.AvailableGroupableColumns, [new ViewColumnInfo { Key = "Name", Title = "Jméno" }]));

        cut.Markup.Should().Contain("Pohledy");
        cut.Find(".tm-view-manager-toggle").Click();
        cut.Markup.Should().Contain("Osobní pohledy");
        cut.Markup.Should().Contain("Žádné osobní pohledy");
        cut.Markup.Should().Contain("Vytvořit nový pohled");
        cut.Markup.Should().Contain("Uložit aktuální jako nový");

        cut.Find(".tm-btn-primary").Click();
        cut.Markup.Should().Contain("Název pohledu");
        cut.Markup.Should().Contain("Viditelnost");
        cut.Markup.Should().Contain("Osobní (pouze já)");
        cut.Markup.Should().Contain("Tenant (všichni)");
        cut.Markup.Should().Contain("Sloupce");
        cut.Markup.Should().Contain("Seskupování");
        cut.Markup.Should().Contain("Zrušit");
        cut.Markup.Should().Contain("Vytvořit");
    }

    [Fact]
    public void SaveCurrent_UsesLocalizedCopySuffix()
    {
        var cut = Render<TmViewManager>(p => p
            .Add(c => c.Provider, ProviderWithCurrent())
            .Add(c => c.ViewContext, "people")
            .Add(c => c.CurrentUserId, "user")
            .Add(c => c.GetCurrentView, () => new DataTableView { Name = "Aktivní", VisibleColumns = ["Name"] }));

        cut.Find(".tm-view-manager-toggle").Click();
        cut.FindAll("button").First(b => b.TextContent.Contains("Uložit aktuální", StringComparison.Ordinal)).Click();

        cut.Find("input[type=text]").GetAttribute("value").Should().Be("Aktivní (kopie)");
        cut.Markup.Should().NotContain("(Copy)");
    }
}
