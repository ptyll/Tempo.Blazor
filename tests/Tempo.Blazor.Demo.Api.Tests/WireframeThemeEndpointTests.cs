using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.Abstractions.Wireframe.Export;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// Decision WIREFRAME-STENCIL-THEME on the server: the headless preview renders a sample document with the theme named in the request
/// (<c>?theme=indigo</c>), an unknown name falls back to the default tokens, and the PNG / PDF export accepts <c>Theme</c> in its request body.
/// </summary>
public class WireframeThemeEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DefaultBlue = "#3b82f6";
    private const string IndigoPrimary = "#6366f1";

    private readonly HttpClient _client;

    public WireframeThemeEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private async Task<string> RenderAsync(string query)
    {
        var response = await _client.GetAsync("/api/wireframe/preview/render.svg?scenario=multipage&page=0" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Preview_WithoutATheme_DrawsTheDefaultTokens()
    {
        var svg = await RenderAsync("");

        Assert.Contains(DefaultBlue, svg);
        Assert.DoesNotContain(IndigoPrimary, svg);
    }

    [Fact]
    public async Task Preview_WithTheme_DrawsThatTheme()
    {
        var svg = await RenderAsync("&theme=indigo");

        Assert.Contains(IndigoPrimary, svg);
        Assert.DoesNotContain(DefaultBlue, svg);
    }

    [Fact]
    public async Task Preview_WithAnUnknownTheme_FallsBackToTheDefaults_NotAnError()
    {
        var svg = await RenderAsync("&theme=definitely-not-a-theme");

        Assert.Contains(DefaultBlue, svg);
    }

    [Fact]
    public async Task PreviewJson_AcceptsTheTheme_ForEveryPage()
    {
        var pages = await _client.GetFromJsonAsync<List<System.Text.Json.JsonElement>>("/api/wireframe/preview/render.json?scenario=multipage&theme=indigo-dark");

        Assert.NotNull(pages);
        Assert.NotEmpty(pages);
        Assert.All(pages, p => Assert.Contains("<svg", p.GetProperty("svg").GetString()));
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("pdf", "application/pdf")]
    public async Task Export_AcceptsAThemeInTheRequest(string format, string contentType)
    {
        var request = new WireframeExportRequest
        {
            Svg = """<svg xmlns="http://www.w3.org/2000/svg" width="200" height="100" viewBox="0 0 200 100"><rect width="200" height="100" fill="#1e293b"/></svg>""",
            FileName = "themed",
            Theme = "indigo-dark",
        };

        var response = await _client.PostAsJsonAsync("/api/wireframe/export/" + format, request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType?.MediaType);
        Assert.True((await response.Content.ReadAsByteArrayAsync()).Length > 100);
    }

    // ── Malformed / hostile theme names (bounded, validated input) ──

    public static IEnumerable<object[]> MalformedThemes() =>
    [
        [new string('a', 65)],
        ["in%20valid"],            // space
        ["a%2Fb"],                 // slash
        ["%3Cscript%3E"],          // <script>
        ["x%0Ay"],                 // newline (log forging)
        ["caf%C3%A9"],             // non-ASCII
    ];

    [Theory]
    [MemberData(nameof(MalformedThemes))]
    public async Task PreviewSvg_WithAMalformedTheme_Is400(string theme)
    {
        var response = await _client.GetAsync("/api/wireframe/preview/render.svg?scenario=multipage&page=0&theme=" + theme);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("theme", body);
        Assert.DoesNotContain("<script>", body);
    }

    [Theory]
    [MemberData(nameof(MalformedThemes))]
    public async Task PreviewJson_WithAMalformedTheme_Is400(string theme)
    {
        var response = await _client.GetAsync("/api/wireframe/preview/render.json?scenario=multipage&theme=" + theme);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Preview_WithAnOverLongButOtherwiseValidTheme_Is400_AndExactly64CharsIsAccepted()
    {
        var tooLong = await _client.GetAsync("/api/wireframe/preview/render.svg?scenario=multipage&page=0&theme=" + new string('a', 65));
        var atLimit = await _client.GetAsync("/api/wireframe/preview/render.svg?scenario=multipage&page=0&theme=" + new string('a', 64));

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, atLimit.StatusCode);   // well-formed but unknown: default tokens
    }

    [Fact]
    public async Task Preview_WithManyDistinctUnknownThemes_StaysOk_AndDrawsTheDefaults()
    {
        for (var i = 0; i < 150; i++)
        {
            var response = await _client.GetAsync($"/api/wireframe/preview/render.svg?scenario=multipage&page=0&theme=unknown-{i}-{Guid.NewGuid():N}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            if (i == 149) Assert.Contains(DefaultBlue, await response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData("png")]
    [InlineData("pdf")]
    public async Task Export_WithAMalformedTheme_Is400(string format)
    {
        foreach (var theme in new[] { new string('a', 65), "bad theme", "x\ny", "<b>" })
        {
            var request = new WireframeExportRequest
            {
                Svg = """<svg xmlns="http://www.w3.org/2000/svg" width="20" height="10"><rect width="20" height="10"/></svg>""",
                Theme = theme,
            };

            var response = await _client.PostAsJsonAsync("/api/wireframe/export/" + format, request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }
}