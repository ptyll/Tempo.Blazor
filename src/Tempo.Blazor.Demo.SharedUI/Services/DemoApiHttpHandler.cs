using System.Net.Http;

namespace Tempo.Blazor.Demo.Services;

/// <summary>
/// Primary handler factory for the demo hosts' SERVER-side API calls. The interactive demo hosts
/// (Blazor Server, InteractiveAuto) call the demo API over HTTPS from the server — the shared
/// layout's notification bell on /feedback runs during prerender, and so do the data providers on
/// their demo pages. Browsers load the demo with <c>IgnoreHTTPSErrors</c>, but a server-side
/// <c>HttpClient</c> has no such switch: on a fresh machine the ASP.NET Core developer
/// certificate is not in the trust store, and every page that touches the API would fail its
/// prerender with an SSL error (500). In Development the handler accepts untrusted certificates
/// on loopback API addresses only — the demo API always runs on a loopback host, so this covers
/// the developer certificate (and any locally issued certificate) without opening non-loopback
/// origins to unvalidated TLS. Outside Development the default validation applies — configure a
/// trusted certificate there.
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("browser")]
public static class DemoApiHttpHandler
{
    /// <summary>
    /// Returns the primary handler for the demo API calls: accepting untrusted certificates on
    /// loopback addresses in Development (the untrusted developer certificate case), default
    /// validation otherwise.
    /// </summary>
    public static HttpMessageHandler Create(bool isDevelopment) => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = isDevelopment
            ? static (request, _, _, errors) =>
                errors == System.Net.Security.SslPolicyErrors.None
                || (request.RequestUri?.IsLoopback ?? false)
            : null,
    };
}
