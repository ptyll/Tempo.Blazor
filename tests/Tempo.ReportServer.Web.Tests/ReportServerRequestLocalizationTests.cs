using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Localization;

namespace Tempo.ReportServer.Web.Tests;

/// <summary>
/// Review fix: without request localization the server leg rendered in the OS culture of the
/// machine hosting the app regardless of the browser's <c>Accept-Language</c> — a Czech Windows
/// prerendered Czech chrome for every visitor. <c>Program.cs</c> pins the supported set to the
/// library's built-in cultures (en/en-US, cs, sk, fr) with en-US the fallback; these tests drive
/// the REAL pipeline (<see cref="WebApplicationFactory{TEntryPoint}"/>) so the middleware — not a
/// mocked localizer — decides the language of the prerendered shell.
/// </summary>
public sealed class ReportServerRequestLocalizationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ReportServerRequestLocalizationTests(WebApplicationFactory<Program> factory)
        => _factory = factory;

    [Fact]
    public async Task AcceptLanguage_csCZ_PrerendersCzechShellText()
    {
        // cs-CZ is not in the supported list verbatim; the middleware parents it to the supported
        // "cs" and the prerendered login chrome reads Czech.
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptLanguage.ParseAdd("cs-CZ");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain("Přihlásit se", "the prerendered sign-in chrome is Czech");
        html.Should().NotContain(">Sign in<");
    }

    [Fact]
    public async Task AcceptLanguage_deDE_PrerendersEnglishShellText()
    {
        // German is not a shipped culture: the request falls back to the en-US default instead of
        // leaking the host machine's OS culture into the prerender.
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.AcceptLanguage.ParseAdd("de-DE");

        using var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = System.Net.WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        html.Should().Contain(">Sign in<", "an unsupported language falls back to English");
        html.Should().NotContain("Přihlásit se");
    }

    [Fact]
    public void SupportedCultures_CoverTheShippedTables()
    {
        // The pinned set must stay in step with the library's built-in resource tables — the whole
        // point of the middleware is that a shipped language can never fall back to English.
        var options = _factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.RequestLocalizationOptions>>().Value;
        var supported = options.SupportedUICultures!.Select(c => c.Name).ToList();
        supported.Should().Contain("cs");
        supported.Should().Contain("sk");
        supported.Should().Contain("fr");
        options.DefaultRequestCulture.UICulture.Name.Should().Be("en-US");
    }
}
