using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Reporting.Configuration;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.ReportServer.Web.Pages;
using Tempo.ReportServer.Web.Services;
using Tempo.ReportServer.Web.Tests.Fixtures;

namespace Tempo.ReportServer.Web.Tests;

public sealed class SchedulesPageTests : ReportServerWebTestBase
{
    [Fact]
    public void SchedulesPage_ListsSeededScheduleAndRuns_FromTypedClient()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<SchedulesPage>();

            cut.Find("[data-testid='f16-schedules-page']").TextContent.Should().Contain("Schedules");

            // Both the schedule row and its delivered run come from the typed client (post-cutover),
            // not the in-memory scheduling worker/outbox.
            cut.Find("[data-testid='schedules-table']").TextContent.Should().Contain("Weekly sales digest");
            cut.Find("[data-testid='schedule-runs']").TextContent.Should().Contain("sales-register.pdf");
        }
    }

    [Fact]
    public void SchedulesPage_CreateSchedule_PersistsViaTypedClient_AndAppearsInList()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<SchedulesPage>();

            cut.Find("[data-testid='schedule-name']").Input("Monday ops pack");
            cut.Find("[data-testid='schedule-cron']").Input("30 6 * * 1");
            cut.Find("[data-testid='schedule-email']").Input("ops@example.test");
            cut.Find("[data-testid='schedule-save']").Click();

            cut.Find("[data-testid='schedule-form-status']").TextContent.Should().Contain("Saved Monday ops pack");
            cut.Find("[data-testid='schedules-table']").TextContent.Should().Contain("Monday ops pack");
            cut.Find("[data-testid='schedules-table']").TextContent.Should().Contain("ops@example.test");
        }
    }

    [Fact]
    public void SchedulesPage_ToggleSchedule_FlipsEnabledState_ViaTypedClient()
    {
        // Subject is the page, not the language: pin en so the ambient machine culture cannot
        // turn a page assertion into a translation assertion. See ReportServerWebTestBase.UseUiCulture.
        using (UseUiCulture("en"))
        {
            SignIn();
            var cut = Render<SchedulesPage>();

            cut.Find("[data-testid='toggle-schedule-weekly-sales']").TextContent.Trim().Should().Be("Disable");

            cut.Find("[data-testid='toggle-schedule-weekly-sales']").Click();
            cut.Find("[data-testid='toggle-schedule-weekly-sales']").TextContent.Trim().Should().Be("Enable");

            cut.Find("[data-testid='toggle-schedule-weekly-sales']").Click();
            cut.Find("[data-testid='toggle-schedule-weekly-sales']").TextContent.Trim().Should().Be("Disable");
        }
    }

    [Fact]
    public void SchedulesPage_RunNow_IsOffered_WhereTheServerRunnerIsRegistered()
    {
        // The runner renders the e-mail through the Engine, so it is a server-host registration (D11).
        using (UseUiCulture("en"))
        {
            Services.Replace(ServiceDescriptor.Singleton<ITempoReportServerClient, DemoTempoReportServerClient>());
            SignIn();

            Render<SchedulesPage>().FindAll("[data-testid^='run-schedule-']").Should().NotBeEmpty();
        }
    }

    [Fact]
    public void SchedulesPage_RunNow_IsNotOffered_WithoutARunner_LikeTheWebAssemblyLeg()
    {
        // The WebAssembly leg has no runner (no Engine in the browser): the button is not offered instead
        // of failing on click.
        using (UseUiCulture("en"))
        {
            Services.Replace(ServiceDescriptor.Singleton<ITempoReportServerClient, DemoTempoReportServerClient>());
            Services.Replace(ServiceDescriptor.Singleton<IReportScheduleRunner, UnavailableReportScheduleRunner>());
            SignIn();

            Render<SchedulesPage>().FindAll("[data-testid^='run-schedule-']").Should().BeEmpty();
        }
    }
}