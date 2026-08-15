using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;

namespace Tempo.Blazor.Demo.Api.Tests;

/// <summary>
/// End-to-end delivery tests against the running smtp4dev container (SMTP localhost:2525, REST API
/// http://localhost:5000). Gated behind the <c>RequiresSmtp4Dev</c> trait. Each test uses a unique
/// recipient so it only ever touches its own messages.
/// </summary>
[Trait("Category", "RequiresSmtp4Dev")]
public class EmailTemplateSmtp4DevTests : IClassFixture<WebApplicationFactory<Program>>, IClassFixture<Smtp4DevFixture>
{
    private const string WelcomeId = "11111111-1111-1111-1111-111111111111";
    private const string NewsletterId = "22222222-2222-2222-2222-222222222222";

    private readonly HttpClient _api;
    private readonly HttpClient _smtp4dev = new() { BaseAddress = new Uri(Smtp4DevHost.RestBaseUrl) };

    public EmailTemplateSmtp4DevTests(WebApplicationFactory<Program> factory, Smtp4DevFixture _)
        => _api = factory.CreateClient();

    [Fact]
    public async Task SendWelcome_ArrivesInSmtp4Dev_WithSubstitutedSubjectAndBodies()
    {
        var to = $"emailtemplates-e2e-{Guid.NewGuid():N}@tempo.local";

        var send = await _api.PostAsJsonAsync($"/api/email-templates/{WelcomeId}/send", new SendEmailRequest
        {
            To = new[] { to },
            VariablesJson = "{\"first_name\":\"Ada\"}",
        });
        send.EnsureSuccessStatusCode();

        var message = await PollForMessageAsync(to);
        message.Should().NotBeNull();
        try
        {
            ((string)message!["subject"]!).Should().Contain("Welcome Ada");
            var html = await _smtp4dev.GetStringAsync($"/api/Messages/{message["id"]}/html");
            html.Should().Contain("Welcome");
        }
        finally
        {
            await _smtp4dev.DeleteAsync($"/api/Messages/{message!["id"]}");
        }
    }

    [Fact]
    public async Task SendNewsletter_LoopAndUtf8_RenderInDeliveredHtml()
    {
        var to = $"emailtemplates-e2e-{Guid.NewGuid():N}@tempo.local";

        var send = await _api.PostAsJsonAsync($"/api/email-templates/{NewsletterId}/send", new SendEmailRequest
        {
            To = new[] { to },
            VariablesJson = "{\"newsletter_title\":\"Zprávy 🐎\",\"articles\":[{\"title\":\"Spuštění\",\"summary\":\"Žluťoučký kůň\"}]}",
        });
        send.EnsureSuccessStatusCode();

        var message = await PollForMessageAsync(to);
        message.Should().NotBeNull();
        try
        {
            var html = await _smtp4dev.GetStringAsync($"/api/Messages/{message!["id"]}/html");
            html.Should().Contain("Spuštění");      // loop item rendered
            html.Should().Contain("Žluťoučký kůň"); // UTF-8 decoded correctly
            html.Should().NotContain("{{");          // no unresolved tokens
        }
        finally
        {
            await _smtp4dev.DeleteAsync($"/api/Messages/{message!["id"]}");
        }
    }

    private async Task<Dictionary<string, object>?> PollForMessageAsync(string recipient)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            using var doc = JsonDocument.Parse(
                await _smtp4dev.GetStringAsync($"/api/Messages?pageSize=5&searchTerms={Uri.EscapeDataString(recipient)}"));
            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() > 0)
            {
                var first = results[0];
                return new Dictionary<string, object>
                {
                    ["id"] = first.GetProperty("id").GetString()!,
                    ["subject"] = first.GetProperty("subject").GetString()!,
                };
            }
            await Task.Delay(300);
        }
        return null;
    }
}
