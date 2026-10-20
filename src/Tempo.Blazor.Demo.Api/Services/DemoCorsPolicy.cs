namespace Tempo.Blazor.Demo.Api.Services;

/// <summary>Origins the demo API answers with CORS headers.</summary>
public static class DemoCorsPolicy
{
    /// <summary>
    /// The demo WASM host's own launch-profile origins — always allowed. Hard defaults live
    /// here only; Cors:AllowedOrigins is purely additive.
    /// </summary>
    public static readonly string[] DefaultOrigins = ["http://localhost:5010", "https://localhost:7106"];

    /// <summary>
    /// True for an http(s) origin on a loopback host (localhost, 127.0.0.1, [::1]) on ANY port — so an
    /// E2E or dev host on another port (7116, a sharded lane) can call the API. Development only.
    /// </summary>
    public static bool IsLoopbackOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.IsLoopback;

    /// <summary>The allow decision: the defaults, the configured list (Cors:AllowedOrigins), and in Development any loopback origin.</summary>
    public static bool IsAllowed(string origin, IReadOnlyCollection<string> configured, bool isDevelopment) =>
        DefaultOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || configured.Contains(origin, StringComparer.OrdinalIgnoreCase)
        || (isDevelopment && IsLoopbackOrigin(origin));
}
