using System.Net;
using System.Net.Http.Json;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.Tests.EmailTemplates;

public class EmailTemplateApiClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_responder(request));
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler) { BaseAddress = new Uri("http://localhost") };
    }

    private static EmailTemplateApiClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => new(new StubFactory(new StubHandler(responder)));

    private static HttpResponseMessage Json(HttpStatusCode status, object body)
        => new(status) { Content = JsonContent.Create(body) };

    [Fact]
    public async Task ListAsync_ReturnsParsedSummaries()
    {
        var client = Client(_ => Json(HttpStatusCode.OK, new[]
        {
            new EmailTemplateSummaryDto { Id = Guid.NewGuid(), Name = "A" },
            new EmailTemplateSummaryDto { Id = Guid.NewGuid(), Name = "B" },
        }));

        var list = await client.ListAsync();
        list.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await client.GetAsync(Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_ReturnsCreatedDetail()
    {
        var id = Guid.NewGuid();
        var client = Client(_ => Json(HttpStatusCode.Created, new EmailTemplateDetailDto { Id = id, Name = "New" }));

        var created = await client.CreateAsync(new CreateEmailTemplateRequest { Name = "New" });
        created.Id.Should().Be(id);
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        (await client.DeleteAsync(Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_Accepted_ReturnsSuccess()
    {
        var client = Client(_ => new HttpResponseMessage(HttpStatusCode.Accepted));
        var result = await client.SendAsync(Guid.NewGuid(), new SendEmailRequest { To = new[] { "a@b.com" } });
        result.Success.Should().BeTrue();
        result.StatusCode.Should().Be(202);
    }

    [Fact]
    public async Task SendAsync_RenderError422_ReturnsFailureWithMessages()
    {
        var client = Client(_ => Json(HttpStatusCode.UnprocessableEntity, new[]
        {
            new RenderErrorDto("Template error", 1, 2),
        }));

        var result = await client.SendAsync(Guid.NewGuid(), new SendEmailRequest { To = new[] { "a@b.com" } });
        result.Success.Should().BeFalse();
        result.StatusCode.Should().Be(422);
        result.Errors.Should().Contain("Template error");
    }

    [Fact]
    public async Task SendAsync_RenderErrorPayload_WithKeyAndSeverity_StillMapsTheMessage()
    {
        var client = Client(_ => Json(HttpStatusCode.UnprocessableEntity, new[]
        {
            new RenderErrorDto("Compile failed", 3, 4, "render.url.blocked_scheme", RenderErrorSeverity.Warning),
        }));

        var result = await client.SendAsync(Guid.NewGuid(), new SendEmailRequest { To = new[] { "a@b.com" } });

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain("Compile failed");
    }

    [Fact]
    public async Task IsNameAvailable_PassesNameAndExclusion()
    {
        StubHandler? captured = null;
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK, true));
        captured = handler;
        var client = new EmailTemplateApiClient(new StubFactory(handler));
        var excluding = Guid.NewGuid();

        (await client.IsNameAvailableAsync("My name", excluding)).Should().BeTrue();
        captured.LastRequest!.RequestUri!.Query.Should().Contain("name=My%20name");
        captured.LastRequest.RequestUri.Query.Should().Contain($"excludingId={excluding}");
    }
}
