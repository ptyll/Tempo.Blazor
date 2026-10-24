using Tempo.Blazor.EmailTemplates.Abstractions;

namespace Tempo.ReportServer.Web.Services;

/// <summary>
/// Registration of the server-only half of scheduled report delivery. The e-mail is rendered by the
/// email Engine (Scriban sandbox, Mjml.Net, AngleSharp sanitizer), which must run in the server process
/// and never in the browser: <c>Tempo.ReportServer.Web.Client</c> (the WebAssembly leg) only carries the
/// model, stores and the <see cref="IReportScheduleRunner"/> seam (decision D11).
/// </summary>
public static class ReportServerEmailDeliveryServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>AddTempoEmailTemplateEngine()</c>, <see cref="ReportEmailDeliveryService"/> and
    /// <see cref="ReportScheduleWorker"/> (also exposed as <see cref="IReportScheduleRunner"/>). Call it from
    /// the server host's <c>Program.cs</c> after <c>AddCommonServices</c>, never from a WebAssembly host.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection.</returns>
    public static IServiceCollection AddReportServerEmailDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddTempoEmailTemplateEngine();
        services.AddSingleton<IReportScheduledDeliveryService, ReportEmailDeliveryService>();
        services.AddSingleton<ReportScheduleWorker>();
        services.AddSingleton<IReportScheduleRunner>(sp => sp.GetRequiredService<ReportScheduleWorker>());
        return services;
    }
}
