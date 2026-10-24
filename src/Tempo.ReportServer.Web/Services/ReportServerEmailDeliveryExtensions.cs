using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.EmailTemplates.Abstractions;

namespace Tempo.ReportServer.Web.Services;

/// <summary>
/// Registration of the server-only half of scheduled report delivery. The e-mail is rendered by the
/// email Engine (Scriban sandbox, Mjml.Net, AngleSharp sanitizer), which must run in the server process
/// and never in the browser: <c>Tempo.ReportServer.Web.Client</c> (the WebAssembly leg) only carries the
/// model, stores and the <see cref="IReportScheduleRunner"/> seam (decisions D11, <c>REPORTSERVER-ENGINE-SERVER-ONLY</c>).
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
        // Replaces the null-object UnavailableReportScheduleRunner that AddCommonServices registered (CanRun = false),
        // whatever the call order: the server leg is the only one that can run a schedule now.
        services.Replace(ServiceDescriptor.Singleton<IReportScheduleRunner>(sp => sp.GetRequiredService<ReportScheduleWorker>()));
        return services;
    }
}
