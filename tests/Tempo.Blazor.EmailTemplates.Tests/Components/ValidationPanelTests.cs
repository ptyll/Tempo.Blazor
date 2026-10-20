using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using NSubstitute;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 6 (§3.0 "Co ve WASM nahradí engine funkce"): the validation panel now takes
/// <see cref="ITemplateValidator"/> instead of <see cref="ITemplateEngine"/>. Without the Engine package
/// (or a host validator) it must show a localized explanation that names the missing registration and keep
/// showing the structural findings — never a no-op and never an empty "everything is fine" panel.
/// </summary>
public class ValidationPanelTests : BunitContext
{
    public ValidationPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        // Echo the editor's keys instead of the shipped JSON strings, so the assertions can name keys.
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEditor();
    }

    private static EmailTemplateDocument Document(string subject = "Hi", string content = "Hello")
    {
        var document = new EmailTemplateDocument { Subject = subject };
        var column = new EmailColumn();
        column.Blocks.Add(new EmailTextBlock { Content = content });
        var section = new EmailSection();
        section.Columns.Add(column);
        document.Sections.Add(section);
        return document;
    }

    [Fact]
    public void WithoutAValidator_ShowsTheLocalizedDegradationInsteadOfSilence()
    {
        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, Document()));

        cut.Find("[data-tm-validation-unavailable]").TextContent
            .Should().Contain("EmailEditor_Validation_SyntaxUnavailable");
    }

    [Fact]
    public void WithoutAValidator_TheShippedMessageIsAPassiveNote_WithoutInterfaceNames()
    {
        // UX review: the panel's job is to say "this check did not run, the rest did" — a host or end user
        // cannot act on "register ITemplateValidator". The registration hint belongs in the LOG only.
        using var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        context.Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        context.Services.AddTempoEmailTemplateEditor();

        var cut = context.Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, Document()));

        var note = cut.Find("[data-tm-validation-unavailable]");
        note.GetAttribute("role").Should().Be("note", "it is passive information, not an alert");
        note.ClassList.Should().Contain("tm-email-validation__note");

        var text = note.TextContent;
        text.Should().Contain("Variable syntax", "the shipped English note");
        text.Should().NotContain("ITemplateValidator");
        text.Should().NotContain("Tempo.Blazor.EmailTemplates.Engine");
    }

    [Fact]
    public void WithoutAValidator_LogsTheRegistrationHintOnce()
    {
        var logger = Substitute.For<ILogger<TmEmailValidationPanel>>();
        Services.AddSingleton(logger);

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, Document()));
        cut.Render(p => p.Add(c => c.Document, Document()));

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
        var logged = logger.ReceivedCalls()
            .Select(call => call.GetArguments()[2]?.ToString() ?? string.Empty)
            .ToList();
        logged.Should().Contain(message => message.Contains("ITemplateValidator"),
            "the log is where the registration hint belongs");
    }

    [Fact]
    public void WithoutAValidator_StructuralFindingsAreStillShown()
    {
        // A button without a link is a structural finding of EmailDocumentValidator (no engine needed).
        var document = Document();
        document.Sections[0].Columns[0].Blocks.Add(new EmailButtonBlock { Text = "Go" });

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, document));

        cut.Find("[data-tm-validation-unavailable]").Should().NotBeNull();
        cut.Find("[data-tm-validation-message]").TextContent
            .Should().Contain("validation.button.href_missing",
                "structural validation runs in Abstractions and must keep working without the engine");
    }

    [Fact]
    public void WithAValidator_SyntaxErrorsAreReported()
    {
        Services.AddSingleton<ITemplateValidator>(new StubValidator(TemplateValidationResult.Invalid(
            [new TemplateError("unexpected end of template", 3, 7)])));

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, Document()));

        cut.FindAll("[data-tm-validation-message]")
            .Select(element => element.TextContent)
            .Should().Contain(text => text.Contains(TemplateValidationResult.SyntaxErrorKey, StringComparison.Ordinal));
        cut.FindAll("[data-tm-validation-unavailable]").Should().BeEmpty();
    }

    [Fact]
    public void WithAValidatorAndAValidTemplate_ShowsTheAllClear()
    {
        Services.AddSingleton<ITemplateValidator>(new StubValidator(TemplateValidationResult.Valid));

        var cut = Render<TmEmailValidationPanel>(p => p.Add(c => c.Document, Document()));

        cut.Find("[data-tm-validation-ok]").Should().NotBeNull();
        cut.FindAll("[data-tm-validation-unavailable]").Should().BeEmpty();
    }

    private sealed class StubValidator(TemplateValidationResult result) : ITemplateValidator
    {
        public TemplateValidationResult Validate(string template) => result;
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : Tempo.Blazor.EmailTemplates.Localization.ITmEmailLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }
}
