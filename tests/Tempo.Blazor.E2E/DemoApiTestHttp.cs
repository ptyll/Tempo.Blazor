using System.Net.Http;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Handler/client factory for TEST-side HTTP calls against the self-hosted demo API
/// (<c>https://localhost:5100</c>) — seeding, reset and test-data endpoints. Browsers load every
/// demo host with <c>IgnoreHTTPSErrors</c>, but a test-side <c>new HttpClient()</c> has no such
/// switch: on a machine whose ASP.NET Core developer certificate is not trusted, every seeding
/// POST failed with UntrustedRoot — and because most call sites swallow that error, the tests
/// silently ran against whatever stale mock data the previous run had left behind. Mirrors the
/// demo hosts' own <c>DemoApiHttpHandler</c> (Tempo.Blazor.Demo.SharedUI): untrusted certificates
/// are accepted on LOOPBACK addresses only (<c>localhost</c>, <c>127.0.0.1</c>, <c>[::1]</c> —
/// where every self-hosted demo host and its dev certificate live) and never for non-loopback
/// origins, so the suite still fails loudly against an untrusted remote endpoint.
/// </summary>
internal static class DemoApiTestHttp
{
    /// <summary>The self-hosted demo API base address used by the seeding call sites.</summary>
    public const string DemoApiBaseUrl = "https://localhost:5100";

    /// <summary>
    /// Primary handler accepting untrusted certificates on loopback addresses only (the untrusted
    /// developer-certificate case); default validation everywhere else.
    /// </summary>
    public static HttpMessageHandler CreateHandler() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = static (request, _, _, errors) =>
            errors == System.Net.Security.SslPolicyErrors.None
            || (request.RequestUri?.IsLoopback ?? false),
    };

    /// <summary>
    /// Client pre-configured with the loopback-tolerant handler. Defaults to <see cref="DemoApiBaseUrl"/>
    /// as its base address so call sites can post relative seed/reset paths; pass <c>null</c> for the
    /// previous no-base-address semantics (absolute URIs everywhere). Absolute URIs always override.
    /// </summary>
    public static HttpClient CreateClient(string? baseAddress = DemoApiBaseUrl) => new(CreateHandler())
    {
        BaseAddress = baseAddress is null ? null : new Uri(baseAddress),
    };
}
