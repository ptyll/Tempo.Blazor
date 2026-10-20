using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Tempo.Blazor.Demo.Services;
using Tempo.Blazor.Demo.SharedUI.Pages;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.Tests.EmailTemplates;

public class EmailTemplatePagesTests : BunitContext
{
    private readonly IEmailTemplateApiClient _api = Substitute.For<IEmailTemplateApiClient>();

    public EmailTemplatePagesTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
        Services.AddSingleton(_api);
    }

    private static string ContentJsonWith(string textContent)
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = textContent });
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return EmailTemplateSerializer.Serialize(doc);
    }

    private NavigationManager Nav => Services.GetRequiredService<NavigationManager>();

    // ── E10.2 list page ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ListPage_RendersTemplateCards()
    {
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = Guid.NewGuid(), Name = "Welcome", Subject = "Hi" },
            new() { Id = Guid.NewGuid(), Name = "Newsletter", Subject = "News" },
        });

        var cut = Render<EmailTemplatesPage>();

        cut.FindAll("[data-tm-template-card]").Should().HaveCount(2);
    }

    [Fact]
    public async Task ListPage_NewTemplate_CreatesAndNavigatesToEditor()
    {
        var newId = Guid.NewGuid();
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>());
        _api.CreateAsync(Arg.Any<CreateEmailTemplateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new EmailTemplateDetailDto { Id = newId, Name = "Untitled template" });

        var cut = Render<EmailTemplatesPage>();
        await cut.Find("[data-tm-new-template]").ClickAsync(new());

        await _api.Received(1).CreateAsync(Arg.Any<CreateEmailTemplateRequest>(), Arg.Any<CancellationToken>());
        Nav.Uri.Should().EndWith($"/email-templates/edit/{newId}");
    }

    [Fact]
    public async Task ListPage_Delete_ConfirmCallsApi()
    {
        var id = Guid.NewGuid();
        _api.ListAsync(Arg.Any<CancellationToken>()).Returns(new List<EmailTemplateSummaryDto>
        {
            new() { Id = id, Name = "Doomed", Subject = "x" },
        });
        _api.DeleteAsync(id, Arg.Any<CancellationToken>()).Returns(true);

        var cut = Render<EmailTemplatesPage>();
        cut.Find("[data-tm-delete]").Click();
        await cut.Find("[data-tm-confirm-delete]").ClickAsync(new());

        await _api.Received(1).DeleteAsync(id, Arg.Any<CancellationToken>());
    }

    // ── E10.3 editor page ───────────────────────────────────────────────────────────────────

    [Fact]
    public void EditorPage_LoadsAndRendersEditor()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi"),
        });

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-email-editor-page]").Should().ContainSingle();
        cut.FindAll("[data-tm-email-editor]").Should().ContainSingle();
    }

    [Fact]
    public void EditorPage_UnknownTemplate_ShowsNotFound()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns((EmailTemplateDetailDto?)null);

        var cut = Render<EmailTemplateEditorPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-email-editor]").Should().BeEmpty();
        cut.Markup.Should().Contain("not found");
    }

    // ── E10.4 / E10.5 send page ─────────────────────────────────────────────────────────────

    [Fact]
    public void SendPage_BuildsFieldsFromVariables()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));

        cut.FindAll("[data-tm-var=\"first_name\"]").Should().ContainSingle();
    }

    [Fact]
    public async Task SendPage_Submit_CallsSendApi()
    {
        var id = Guid.NewGuid();
        _api.GetAsync(id, Arg.Any<CancellationToken>()).Returns(new EmailTemplateDetailDto
        {
            Id = id, Name = "Welcome", ContentJson = ContentJsonWith("Hi {{ first_name }}"),
        });
        _api.SendAsync(id, Arg.Any<SendEmailRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SendEmailResult(true, 202, Array.Empty<string>()));

        var cut = Render<EmailTemplateSendPage>(p => p.Add(c => c.Id, id));
        cut.Find("[data-tm-to]").Change("a@example.com");
        await cut.Find("[data-tm-send-submit]").ClickAsync(new());

        await _api.Received(1).SendAsync(id, Arg.Is<SendEmailRequest>(r => r.To.Contains("a@example.com")), Arg.Any<CancellationToken>());
        cut.FindAll("[data-tm-send-success]").Should().ContainSingle();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
