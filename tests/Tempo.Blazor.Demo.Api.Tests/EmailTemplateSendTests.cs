using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.EmailTemplates.Abstractions.Contracts;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;

namespace Tempo.Blazor.Demo.Api.Tests;

public sealed class CapturingSenderFactory : WebApplicationFactory<Program>
{
    public CapturingEmailSender Sender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
        => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Sender);
        });
}

public sealed class CapturingEmailSender : IEmailSender
{
    public EmailMessage? Last { get; private set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        Last = message;
        return Task.CompletedTask;
    }
}

public class EmailTemplateSendTests : IClassFixture<CapturingSenderFactory>
{
    private const string WelcomeId = "11111111-1111-1111-1111-111111111111";
    private readonly CapturingSenderFactory _factory;
    private readonly HttpClient _client;

    public EmailTemplateSendTests(CapturingSenderFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Send_Valid_Returns202_AndRendersSubject()
    {
        var response = await _client.PostAsJsonAsync($"/api/email-templates/{WelcomeId}/send", new SendEmailRequest
        {
            To = new[] { "recipient@example.com" },
            VariablesJson = "{\"first_name\":\"Jane\"}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Sender.Last.Should().NotBeNull();
        _factory.Sender.Last!.Subject.Should().Be("Welcome Jane!");
        _factory.Sender.Last.Html.Should().Contain("Jane");
        _factory.Sender.Last.To.Should().ContainSingle().Which.Should().Be("recipient@example.com");
    }

    [Fact]
    public async Task Send_NoRecipients_Returns400()
    {
        var response = await _client.PostAsJsonAsync($"/api/email-templates/{WelcomeId}/send", new SendEmailRequest
        {
            To = Array.Empty<string>(),
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Send_WithBlockedUrl_IsAccepted_WarningDoesNotFailTheSend()
    {
        // A refused URL is a warning (D23): the message is delivered with '#' instead, so /send must
        // answer 202 — a 422 would make the host believe the template is broken.
        var created = await _client.PostAsJsonAsync("/api/email-templates", new CreateEmailTemplateRequest
        {
            Name = $"Blocked link {Guid.NewGuid():N}",
            Subject = "S",
            Language = "en",
            ContentJson = "{\"sections\":[{\"columns\":[{\"blocks\":[" +
                "{\"$type\":\"button\",\"text\":\"Go\",\"href\":\"javascript:alert(1)\"}]}]}]}",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await created.Content.ReadFromJsonAsync<EmailTemplateDetailDto>();

        var response = await _client.PostAsJsonAsync($"/api/email-templates/{detail!.Id}/send", new SendEmailRequest
        {
            To = new[] { "recipient@example.com" },
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        _factory.Sender.Last.Should().NotBeNull();
        _factory.Sender.Last!.Html.Should().NotContain("javascript:");
        _factory.Sender.Last.Html.Should().Contain("href=\"#\"");
    }

    [Fact]
    public async Task Send_UnknownTemplate_Returns404()
    {
        var response = await _client.PostAsJsonAsync($"/api/email-templates/{Guid.NewGuid()}/send", new SendEmailRequest
        {
            To = new[] { "a@b.com" },
        });
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
