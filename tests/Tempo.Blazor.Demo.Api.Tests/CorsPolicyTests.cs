using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Demo.Api.Services;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// The demo API's CORS decision. The WASM demo host on its launch-profile origin
/// (7106/5010) must keep working, and — Development only — so must every other loopback
/// origin (an E2E or dev host on 7116, a sharded lane), while non-loopback origins stay
/// refused everywhere and Production never grows the loopback exemption.
/// <para>
/// The preflight tests exercise the REAL pipeline (<c>app.UseCors()</c> over the default
/// policy), because what a browser reads is the response header, not the registration; the
/// <c>DemoCorsPolicy</c> unit tests below pin the predicate itself, including the spellings a
/// string-matching allow-list gets wrong (<c>localhost.evil.com</c> is not a loopback host, and
/// <c>file:///x</c> is not an http(s) origin at all).
/// </para>
/// </summary>
public class CorsPolicyTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public CorsPolicyTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Development_PreflightFromAnyLoopbackPort_IsAllowed()
    {
        var client = _factory.CreateClient();

        using var response = await PreflightAsync(client, origin: "https://localhost:7116");

        response.Headers.GetValues("Access-Control-Allow-Origin").Should()
            .ContainSingle("the origin must be ECHOED, not '*', because the policy allows credentials")
            .Which.Should().Be("https://localhost:7116");
        response.Headers.GetValues("Access-Control-Allow-Credentials").Should()
            .ContainSingle("the SignalR WebSocket handshake needs credentials on the preflight too")
            .Which.Should().Be("true");
    }

    [Fact]
    public async Task Development_PreflightFromANonLoopbackOrigin_IsRefused()
    {
        var client = _factory.CreateClient();

        using var response = await PreflightAsync(client, origin: "https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "a non-loopback origin must not read as allowed just because it parses as an "
            + "absolute http(s) URL — the Development exemption is loopback-only");
    }

    [Fact]
    public async Task Production_OnlyDefaultsAndConfiguredOrigins_AreAllowed()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            // The trailing slash is deliberate: Program normalizes configured origins, so the
            // entry must still match the bare origin a browser sends.
            builder.UseSetting("Cors:AllowedOrigins:0", "https://ci.example:8443/");
        });
        var client = factory.CreateClient();

        // The launch-profile origin stays allowed (hard default)…
        using var defaultOrigin = await PreflightAsync(client, "https://localhost:7106");
        defaultOrigin.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("https://localhost:7106");

        // …so does the configured one, with its trailing slash normalized away…
        using var configuredOrigin = await PreflightAsync(client, "https://ci.example:8443");
        configuredOrigin.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain("https://ci.example:8443");

        // …but the Development loopback exemption does NOT follow the API into Production.
        using var loopbackOrigin = await PreflightAsync(client, "https://localhost:7116");
        loopbackOrigin.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse(
            "the loopback exemption is a development convenience; a Production deployment "
            + "answers only the origins it names");
    }

    /// <summary>The loopback spellings the exemption MUST cover — host decides, port does not.</summary>
    public static IEnumerable<object[]> LoopbackOrigins =>
    [
        ["http://localhost:7116"],
        ["http://127.0.0.1:9000"],
        ["https://[::1]:7116"],
    ];

    [Theory]
    [MemberData(nameof(LoopbackOrigins))]
    public void IsLoopbackOrigin_TrueForLoopbackHostsOnAnyPort(string origin) =>
        DemoCorsPolicy.IsLoopbackOrigin(origin).Should().BeTrue(
            "the whole point of the exemption: the PORT must not matter, only the host");

    [Theory]
    [InlineData("https://localhost.evil.com")]
    [InlineData("http://localhost.7106.example")]   // 'localhost' inside a hostname is not the loopback host
    [InlineData("file:///x")]
    [InlineData("ftp://localhost:21")]
    [InlineData("")]
    [InlineData("not a url")]
    public void IsLoopbackOrigin_FalseForEverythingElse(string origin) =>
        DemoCorsPolicy.IsLoopbackOrigin(origin).Should().BeFalse(
            "'localhost' inside a hostname or a non-http(s) scheme is not a loopback origin");

    [Fact]
    public void IsAllowed_DevelopmentCoversDefaults_Configured_AndLoopback_NonDevelopmentDoesNot()
    {
        string[] configured = ["https://ci.example:8443"];

        foreach (var origin in new[] { "http://localhost:5010", "https://localhost:7106", "https://ci.example:8443", "https://localhost:7116" })
        {
            DemoCorsPolicy.IsAllowed(origin, configured, isDevelopment: true).Should().BeTrue(
                "Development allows the defaults, the configured list and any loopback origin ('{0}')",
                origin);
        }

        DemoCorsPolicy.IsAllowed("https://localhost:7116", configured, isDevelopment: false).Should().BeFalse(
            "outside Development the loopback exemption disappears");
        DemoCorsPolicy.IsAllowed("https://other.example", configured, isDevelopment: true).Should().BeFalse(
            "even Development does not allow non-loopback origins outside the lists");
    }

    private static async Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/persons");
        request.Headers.TryAddWithoutValidation("Origin", origin);
        request.Headers.TryAddWithoutValidation("Access-Control-Request-Method", "GET");

        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(
            HttpStatusCode.NoContent,
            "a preflight the CORS middleware answers is 204 — anything else means the request "
            + "reached the endpoint pipeline instead of the policy");
        return response;
    }
}
