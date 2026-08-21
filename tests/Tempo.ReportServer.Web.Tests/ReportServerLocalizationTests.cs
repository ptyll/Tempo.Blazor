using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Localization;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.ReportServer.Web.Components;
using Tempo.ReportServer.Web.Pages;
using Tempo.ReportServer.Web.Tests.Fixtures;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// Proves the report-server portal is localized through <see cref="ITmLocalizer"/>: the same components
/// render English by default and real Czech/French translations when the UI culture changes — driven by
/// the embedded <c>TmResources.cs.json</c> / <c>TmResources.fr.json</c> resources, not hard-coded text.
/// </summary>
public sealed class ReportServerLocalizationTests : ReportServerWebTestBase
{
    /// <summary>
    /// "By default" names the localizer's NEUTRAL (fallback) table — <c>TmResources.json</c>, the table
    /// <c>JsonStringLocalizer</c> ends every lookup chain with. It does NOT name "whatever an unset
    /// ambient culture happens to give", and the two are not the same claim: under an ambient
    /// <c>cs_CZ</c> this shell renders "Sestavy", which is the localizer working rather than a defect.
    /// <c>UseUiCulture("")</c> selects <c>CultureInfo.InvariantCulture</c>, whose chain is the neutral
    /// table alone, so it is how the fallback is reached with the machine taken out of the measurement.
    /// <para>
    /// Reading the neutral table SPECIFICALLY is the point, not an accident of today's resources. As a
    /// PREMISE measured over the built assembly at the time of writing, no <c>TmResources.en.json</c> is
    /// embedded, so pinning <c>en</c> would resolve to this very table and look equivalent. That premise
    /// can change: the day someone embeds <c>TmResources.en.json</c>, an <c>en</c> pin would read THAT
    /// and let <c>TmResources.json</c> rot unwatched, whereas this test goes on reading the fallback.
    /// Measured off-diagonally — breaking the neutral value reddens both shapes, but with an
    /// <c>en.json</c> present only this one stays red. These two assertions are also the only place in
    /// this assembly where English navigation is asserted against the real localizer.
    /// </para>
    /// </summary>
    [Fact]
    public void Shell_RendersEnglishNavigation_ByDefault()
    {
        SignIn();

        using (UseUiCulture(""))
        {
            var cut = Render<ReportServerShell>(parameters => parameters
                .Add(component => component.Title, "Reports")
                .Add(component => component.ActiveSection, "reports"));

            var nav = cut.Find("[data-testid='nav-reports']").TextContent;
            nav.Should().Contain("Reports");
            cut.Find("[data-testid='nav-favorites']").TextContent.Should().Contain("Favorites");
        }
    }

    [Fact]
    public void Shell_RendersCzechNavigation_WhenCultureIsCzech()
    {
        SignIn();

        using (UseUiCulture("cs"))
        {
            var cut = Render<ReportServerShell>(parameters => parameters
                .Add(component => component.Title, "Sestavy")
                .Add(component => component.ActiveSection, "reports"));

            // Real Czech resource values, not the English literal or the raw key.
            cut.Find("[data-testid='nav-reports']").TextContent.Should().Contain("Sestavy");
            cut.Find("[data-testid='nav-favorites']").TextContent.Should().Contain("Oblíbené");
            cut.Find("[data-testid='nav-reports']").TextContent.Should().NotContain("Reports");
        }
    }

    [Fact]
    public void Shell_RendersFrenchNavigation_WhenCultureIsFrench()
    {
        SignIn();

        using (UseUiCulture("fr"))
        {
            var cut = Render<ReportServerShell>(parameters => parameters
                .Add(component => component.Title, "Rapports")
                .Add(component => component.ActiveSection, "reports"));

            cut.Find("[data-testid='nav-reports']").TextContent.Should().Contain("Rapports");
            cut.Find("[data-testid='nav-favorites']").TextContent.Should().Contain("Favoris");
        }
    }

    [Fact]
    public void FavoritesPage_RendersCzechEmptyState_WhenCultureIsCzech()
    {
        SignIn();

        using (UseUiCulture("cs"))
        {
            var cut = Render<FavoritesPage>();

            cut.Find("[data-testid='favorites-empty']").TextContent
                .Should().Contain("Zatím žádné oblíbené");
        }
    }

    [Fact]
    public void NewReportForm_RendersCzechValidationError_WhenCultureIsCzech()
    {
        var folders = new List<ReportFolderDto>
        {
            new() { TenantId = "northwind", FolderId = "folder-finance", Name = "Finance", Path = "/Finance" },
        };

        using (UseUiCulture("cs"))
        {
            var cut = Render<NewReportForm>(parameters => parameters
                .Add(component => component.TenantId, "northwind")
                .Add(component => component.Folders, folders)
                .Add(component => component.OnSubmit, EventCallback.Factory.Create<CreateReportRequestDto>(this, _ => { })));

            // Submitting with an empty name fails validation; the validator emits a TmResources KEY that
            // the form resolves through Loc, so the inline error shows the real Czech translation.
            cut.Find("[data-testid='new-report-submit']").Click();

            var error = cut.Find("[data-testid='new-report-name-error']").TextContent;
            error.Should().Contain("Název sestavy je povinný.");
            error.Should().NotContain("CreateReport_Name_Required");
            error.Should().NotContain("Report name is required.");
        }
    }

    [Fact]
    public void Loc_CanBeOverriddenWithASeededMock()
    {
        // Mirrors the main app's test pattern: a seeded mock registered AFTER the base wins (last
        // registration wins in .NET DI), so tests can pin specific strings without the real resources.
        Services.AddSingleton<ITmLocalizer>(MockTmLocalizer.Czech());
        SignIn();

        var cut = Render<ReportServerShell>(parameters => parameters
            .Add(component => component.Title, "Sestavy")
            .Add(component => component.ActiveSection, "reports"));

        cut.Find("[data-testid='nav-favorites']").TextContent.Should().Contain("Oblíbené");
    }
}
