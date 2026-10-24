using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tempo.ReportServer.Web.Client;
using Tempo.ReportServer.Web.Services;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// Guards the dual-mode DI gate in <see cref="CommonServiceCollectionExtensions.AddCommonServices"/>.
/// This runs in BOTH InteractiveAuto legs, so the gate must resolve consistently from whichever config
/// the leg carries — the regression that would silently drop the WASM leg into demo mode.
/// </summary>
public sealed class CommonServicesGateTests
{
    [Fact]
    public void NoOidcConfig_ResolvesDemoSession()
    {
        using var provider = BuildProvider(authority: null, clientId: null);
        using var scope = provider.CreateScope();

        var identity = scope.ServiceProvider.GetRequiredService<IPortalIdentity>();

        identity.Should().BeOfType<ReportServerSessionState>();
        identity.CanSwitchTenant.Should().BeTrue();
    }

    [Fact]
    public void OidcConfigured_ResolvesOidcIdentity()
    {
        using var provider = BuildProvider(authority: "https://keycloak/realms/tempo-reports", clientId: "tempo-report-web");
        using var scope = provider.CreateScope();

        var identity = scope.ServiceProvider.GetRequiredService<IPortalIdentity>();

        identity.Should().BeOfType<OidcPortalIdentity>();
        identity.CanSwitchTenant.Should().BeFalse();
    }

    [Fact]
    public void AuthorityWithoutClientId_StaysDemo()
    {
        using var provider = BuildProvider(authority: "https://keycloak/realms/tempo-reports", clientId: "");
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IPortalIdentity>().Should().BeOfType<ReportServerSessionState>();
    }

    [Fact]
    public void CommonServices_RegisterNoEngineBoundService()
    {
        // AddCommonServices runs in the WebAssembly leg too: the email Engine, the delivery service and the
        // worker are server-only (decision D11, AddReportServerEmailDelivery).
        var services = new ServiceCollection();
        services.AddCommonServices(new ConfigurationBuilder().Build());

        services.Should().NotContain(d => d.ServiceType == typeof(IReportScheduledDeliveryService));
        services.Should().NotContain(d => d.ServiceType == typeof(ReportScheduleWorker));
        // The run-now seam is a null object here (CanRun = false): the page injects it directly, and only
        // AddReportServerEmailDelivery swaps in the real worker (REPORTSERVER-ENGINE-SERVER-ONLY).
        services.Should().ContainSingle(d => d.ServiceType == typeof(IReportScheduleRunner))
            .Which.ImplementationType.Should().Be(typeof(UnavailableReportScheduleRunner));
        services.Should().NotContain(d =>
            d.ServiceType == typeof(Tempo.Blazor.EmailTemplates.Abstractions.Rendering.IEmailTemplateRenderer)
            && d.ImplementationType != null
            && d.ImplementationType.Assembly.GetName().Name == "Tempo.Blazor.EmailTemplates.Engine");
        typeof(CommonServiceCollectionExtensions).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .Should().NotContain("Tempo.Blazor.EmailTemplates.Engine");
    }

    [Fact]
    public void ServerHost_RegistersDeliveryWorkerAndRunnerSeam()
    {
        var services = new ServiceCollection();
        services.AddCommonServices(new ConfigurationBuilder().Build());
        services.AddReportServerEmailDelivery();
        services.AddScoped<AuthenticationStateProvider>(_ => new AnonymousAuthenticationStateProvider());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReportScheduledDeliveryService>().Should().BeOfType<ReportEmailDeliveryService>();
        var runner = provider.GetRequiredService<IReportScheduleRunner>();
        runner.Should().BeOfType<ReportScheduleWorker>().And.BeSameAs(provider.GetRequiredService<ReportScheduleWorker>());
        runner.CanRun.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ServerHost_ResolvesTheRealRunner_InEitherCallOrder(bool commonFirst)
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        if (commonFirst)
        {
            services.AddCommonServices(configuration);
            services.AddReportServerEmailDelivery();
        }
        else
        {
            services.AddReportServerEmailDelivery();
            services.AddCommonServices(configuration);
        }

        services.AddScoped<AuthenticationStateProvider>(_ => new AnonymousAuthenticationStateProvider());
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IReportScheduleRunner>().Should().BeOfType<ReportScheduleWorker>()
            .Which.CanRun.Should().BeTrue();
    }
    [Fact]
    public void WebAssemblyLeg_RunnerCannotRun()
    {
        var services = new ServiceCollection();
        services.AddCommonServices(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        var runner = provider.GetRequiredService<IReportScheduleRunner>();
        runner.CanRun.Should().BeFalse();
        runner.RunScheduleNowAsync("t", "s").GetAwaiter().GetResult().Should().BeNull();
    }
    private static ServiceProvider BuildProvider(string? authority, string? clientId)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Authentication:Oidc:Authority"] = authority,
            ["Authentication:Oidc:ClientId"] = clientId,
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        // OidcPortalIdentity needs an ambient provider (present in both legs of the real app).
        services.AddScoped<AuthenticationStateProvider>(_ =>
            new AnonymousAuthenticationStateProvider());
        services.AddCommonServices(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class AnonymousAuthenticationStateProvider : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));
    }
}
