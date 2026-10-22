using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.Demo.Api.Tests;

public class EmailTemplateEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public EmailTemplateEndpointTests(WebApplicationFactory<Program> factory) => _client = factory.CreateClient();

    private static CreateEmailTemplateRequest NewRequest(string name) => new()
    {
        Name = name, Subject = "S {{ x }}", Language = "en",
        ContentJson = "{\"sections\":[{\"columns\":[{\"blocks\":[{\"$type\":\"text\",\"content\":\"Hi {{ x }}\"}]}]}]}",
    };

    [Fact]
    public async Task List_ReturnsSeededTemplates()
    {
        var list = await _client.GetFromJsonAsync<List<EmailTemplateSummaryDto>>("/api/email-templates");
        list.Should().NotBeNull();
        list!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        var response = await _client.GetAsync($"/api/email-templates/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_Valid_Returns201WithLocation()
    {
        var response = await _client.PostAsJsonAsync("/api/email-templates", NewRequest("Created template"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var created = await response.Content.ReadFromJsonAsync<EmailTemplateDetailDto>();
        created!.Name.Should().Be("Created template");

        var fetched = await _client.GetFromJsonAsync<EmailTemplateDetailDto>($"/api/email-templates/{created.Id}");
        fetched!.Subject.Should().Be("S {{ x }}");
    }

    [Fact]
    public async Task Create_Invalid_Returns400()
    {
        var bad = NewRequest("") with { Name = "" };
        var response = await _client.PostAsJsonAsync("/api/email-templates", bad);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_Existing_Returns204_Unknown_Returns404()
    {
        var created = await (await _client.PostAsJsonAsync("/api/email-templates", NewRequest("To update")))
            .Content.ReadFromJsonAsync<EmailTemplateDetailDto>();

        var update = new UpdateEmailTemplateRequest
        {
            Name = "Updated", Subject = "S", Language = "en", IsActive = true,
            ContentJson = "{\"sections\":[]}",
        };

        (await _client.PutAsJsonAsync($"/api/email-templates/{created!.Id}", update)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _client.PutAsJsonAsync($"/api/email-templates/{Guid.NewGuid()}", update)).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenNotFound()
    {
        var created = await (await _client.PostAsJsonAsync("/api/email-templates", NewRequest("To delete")))
            .Content.ReadFromJsonAsync<EmailTemplateDetailDto>();

        (await _client.DeleteAsync($"/api/email-templates/{created!.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);
        (await _client.DeleteAsync($"/api/email-templates/{created.Id}")).StatusCode
            .Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Preview_SubstitutesVariables()
    {
        var welcome = await _client.GetFromJsonAsync<EmailTemplateDetailDto>(
            "/api/email-templates/11111111-1111-1111-1111-111111111111");

        var response = await _client.PostAsJsonAsync("/api/email-templates/preview", new RenderPreviewRequest
        {
            ContentJson = welcome!.ContentJson,
            VariablesJson = "{\"first_name\":\"Jane\"}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RenderPreviewResponse>();
        result!.Html.Should().Contain("Jane");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_BlockedUrl_ReturnsWarningWithKeyAndSeverity()
    {
        // A refused URL is a warning: the preview still returns HTML, but the diagnostic carries the
        // localization key and the severity so the client can render it as a warning.
        const string content = "{\"sections\":[{\"columns\":[{\"blocks\":[" +
            "{\"$type\":\"button\",\"text\":\"Go\",\"href\":\"javascript:alert(1)\"}]}]}]}";

        var response = await _client.PostAsJsonAsync("/api/email-templates/preview", new RenderPreviewRequest
        {
            ContentJson = content,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<RenderPreviewResponse>();
        result!.Html.Should().NotBeNullOrWhiteSpace();
        result.Html.Should().NotContain("javascript:");
        result.Errors.Should().ContainSingle();
        result.Errors[0].Key.Should().Be(RenderKeys.BlockedUrlScheme);
        result.Errors[0].Severity.Should().Be(RenderErrorSeverity.Warning);
    }

    [Fact]
    public async Task NameAvailable_ReflectsExistingNames()
    {
        var taken = await _client.GetFromJsonAsync<bool>("/api/email-templates/name-available?name=Welcome%20email");
        var free = await _client.GetFromJsonAsync<bool>($"/api/email-templates/name-available?name=Free{Guid.NewGuid():N}");

        taken.Should().BeFalse();
        free.Should().BeTrue();
    }

    // Fáze 10 (carry-forward): the API path applies the same import limits as the editor and reports a
    // failure as 422 ProblemDetails whose `errors` values are LOCALIZATION KEYS (the client owns the
    // wording). "What passes the import passes the server" is the invariant being pinned.

    [Fact]
    public async Task Create_ContentJsonOverMaxInputBytes_Returns422WithTheInputTooLargeKey()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton(new EmailImportOptions { MaxInputBytes = 64 })));
        var client = factory.CreateClient();
        var request = NewRequest("Oversized") with { ContentJson = "{\"sections\":[],\"pad\":\"" + new string('x', 200) + "\"}" };

        var response = await client.PostAsJsonAsync("/api/email-templates", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var keys = await ErrorKeysAsync(response);
        keys.Should().Contain(SerializationKeys.InputTooLarge);
    }

    [Fact]
    public async Task Create_NonNumericSchemaVersion_Returns422WithTheInvalidSchemaVersionKey()
    {
        var request = NewRequest("Bad schema") with { ContentJson = "{\"schemaVersion\":\"two\",\"sections\":[]}" };

        var response = await _client.PostAsJsonAsync("/api/email-templates", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Contain(SerializationKeys.InvalidSchemaVersion);
    }

    [Fact]
    public async Task Create_NewerSchemaVersion_Returns422WithTheUnsupportedSchemaVersionKey()
    {
        var request = NewRequest("Newer schema") with { ContentJson = "{\"schemaVersion\":99,\"sections\":[]}" };

        var response = await _client.PostAsJsonAsync("/api/email-templates", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Contain(SerializationKeys.UnsupportedSchemaVersion);
    }

    [Fact]
    public async Task Update_TooManySections_Returns422WithTheSectionsLimitKey()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton(new EmailImportOptions { MaxSections = 1 })));
        var client = factory.CreateClient();
        var created = await (await _client.PostAsJsonAsync("/api/email-templates", NewRequest("Limit update")))
            .Content.ReadFromJsonAsync<EmailTemplateDetailDto>();
        var update = new UpdateEmailTemplateRequest
        {
            Name = "Limited", Subject = "S", Language = "en", IsActive = true,
            ContentJson = "{\"sections\":[{\"columns\":[]},{\"columns\":[]}]}",
        };

        var response = await client.PutAsJsonAsync($"/api/email-templates/{created!.Id}", update);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response))
            .Should().Contain(Tempo.Blazor.EmailTemplates.Abstractions.Rendering.DocumentValidationKeys.TooManySections);
    }

    [Fact]
    public async Task Preview_MalformedJson_Returns422WithTheMalformedKey()
    {
        var response = await _client.PostAsJsonAsync("/api/email-templates/preview", new RenderPreviewRequest
        {
            ContentJson = "{ not json",
        });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorKeysAsync(response)).Should().Contain(SerializationKeys.Malformed);
    }

    /// <summary>
    /// The localization keys of a 422 ProblemDetails: the client turns them into text, so the API must
    /// never put a half-localized sentence or an exception message there instead.
    /// </summary>
    private static async Task<IReadOnlyList<string>> ErrorKeysAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("errors", out var errors)
            ? errors.EnumerateObject().SelectMany(property => property.Value.EnumerateArray().Select(v => v.GetString() ?? ""))
                .ToList()
            : [];
    }
}
