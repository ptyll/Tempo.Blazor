using System;
using System.Globalization;
using Bunit;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Localization;
using Tempo.Blazor.Reporting.Configuration;
using Tempo.Reporting.Abstractions.Dtos;
using Tempo.ReportServer.Api.Security;
using Tempo.ReportServer.Web.Services;

namespace Tempo.ReportServer.Web.Tests.Fixtures;

public abstract class ReportServerWebTestBase : BunitContext
{
    protected ReportServerWebTestBase()
    {
        Services.AddTempoBlazorReporting();
        Services.AddTempoEmailTemplateEngine();
        Services.AddSingleton<IReportApiKeyStore, DemoReportApiKeyStore>();
        Services.AddReportServerSecurity();
        Services.AddSingleton<DemoReportSourceFactory>();
        Services.AddSingleton<ReportServerCatalogStore>();
        // Catalog pages call the typed Report Server client (post-cutover); tests bind a functional
        // in-memory fake so the explorer/revision/data-source pages exercise the real client path.
        Services.AddSingleton<ITempoReportServerClient, FakeTempoReportServerClient>();
        Services.AddSingleton<IReportScheduleClock, SystemReportScheduleClock>();
        Services.AddSingleton<ReportScheduleStore>();
        Services.AddSingleton<ReportRenderJobQueue>();
        Services.AddSingleton<ReportEmailOutbox>();
        Services.AddSingleton<ReportEmailTemplateGalleryStore>();
        Services.AddSingleton<IEmailTemplateStore>(sp => sp.GetRequiredService<ReportEmailTemplateGalleryStore>());
        Services.AddSingleton<IEmailSender, Smtp4DevEmailSender>();
        Services.AddSingleton<IReportScheduledDeliveryService, ReportEmailDeliveryService>();
        Services.AddSingleton<ReportScheduleWorker>();
        Services.AddScoped<ReportServerSessionState>();
        // Portal consumers depend on IPortalIdentity; the default test mode is the demo session
        // (same instance as SignIn()). Auth-mode tests register OidcPortalIdentity + test authorization.
        Services.AddScoped<IPortalIdentity>(sp => sp.GetRequiredService<ReportServerSessionState>());
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    protected ReportServerSessionState SignIn(string userName = "Pavel Author")
    {
        var session = Services.GetRequiredService<ReportServerSessionState>();
        session.SignIn(userName);
        return session;
    }

    /// <summary>
    /// The portal resolves <see cref="ITmLocalizer"/> through <c>AddTempoBlazorReporting()</c> →
    /// <c>AddTempoBlazor()</c>, which registers the real JSON-backed localizer over the embedded
    /// <c>TmResources*.json</c> resources. Rendering inside this scope makes the localizer resolve the
    /// requested culture (e.g. <c>cs</c> / <c>fr</c>) so tests can assert real translations, then the
    /// ambient culture is restored. Use it to prove that portal strings are genuinely localizable.
    /// <para>
    /// It has a second, opposite use: pinning <c>en</c> around a test whose subject is a PAGE and not a
    /// language. Such a test spells English chrome ("Active", "Saved …", "Folder permissions") only
    /// because it had to spell something; without a pin it reads whichever translation the machine's
    /// ambient culture happens to select, so it turns red on a developer box running under
    /// <c>cs_CZ</c> while measuring nothing about the page. Pinning keeps the subject and removes the
    /// machine from the measurement. A test whose subject IS the neutral table must reach it
    /// through <c>UseUiCulture("")</c> rather than through <c>en</c>.
    /// </para>
    /// </summary>
    protected static IDisposable UseUiCulture(string culture)
        => new UiCultureScope(culture);

    private sealed class UiCultureScope : IDisposable
    {
        private readonly CultureInfo _previousUi;
        private readonly CultureInfo _previousCulture;

        public UiCultureScope(string culture)
        {
            _previousUi = CultureInfo.CurrentUICulture;
            _previousCulture = CultureInfo.CurrentCulture;
            var target = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = target;
            CultureInfo.CurrentCulture = target;
        }

        public void Dispose()
        {
            CultureInfo.CurrentUICulture = _previousUi;
            CultureInfo.CurrentCulture = _previousCulture;
        }
    }
}
