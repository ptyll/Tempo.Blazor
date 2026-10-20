using System.Globalization;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Resources;
using Tempo.Blazor.EmailTemplates.Abstractions.Validation;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Validation;

public class ValidatorLocalizationTests
{
    /// <summary>The exact localizer the engine registers: embedded JSON, not the host's factory.</summary>
    private static IStringLocalizer<EmailTemplateValidationResources> Localizer()
        => new JsonStringLocalizer<EmailTemplateValidationResources>();

    private static TResult InCulture<TResult>(string culture, Func<TResult> action)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            return action();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Create_EmptyName_LocalizedMessage_Czech()
    {
        var validator = new CreateEmailTemplateRequestValidator(Localizer());
        var request = new CreateEmailTemplateRequest { Name = "", Language = "cs", ContentJson = "{}" };

        var message = InCulture("cs", () =>
            validator.Validate(request).Errors.First(e => e.PropertyName == nameof(request.Name)).ErrorMessage);

        message.Should().Be("Název šablony je povinný.");
    }

    [Fact]
    public void Create_EmptyName_LocalizedMessage_English()
    {
        var validator = new CreateEmailTemplateRequestValidator(Localizer());
        var request = new CreateEmailTemplateRequest { Name = "", Language = "en", ContentJson = "{}" };

        var message = InCulture("en", () =>
            validator.Validate(request).Errors.First(e => e.PropertyName == nameof(request.Name)).ErrorMessage);

        message.Should().Be("The template name is required.");
    }

    [Fact]
    public void Create_InvalidLanguageAndMissingContent_AreInvalid()
    {
        var validator = new CreateEmailTemplateRequestValidator(Localizer());
        var request = new CreateEmailTemplateRequest { Name = "Ok", Language = "not-a-lang!!", ContentJson = "" };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.Language));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.ContentJson));
    }

    [Fact]
    public void Create_ValidRequest_Passes()
    {
        var validator = new CreateEmailTemplateRequestValidator(Localizer());
        var request = new CreateEmailTemplateRequest { Name = "Welcome", Subject = "Hi", Language = "en-US", ContentJson = "{}" };

        validator.Validate(request).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Send_EmptyTo_LocalizedCzech()
    {
        var validator = new SendEmailRequestValidator(Localizer());
        var request = new SendEmailRequest { To = Array.Empty<string>() };

        var message = InCulture("cs", () =>
            validator.Validate(request).Errors.First(e => e.PropertyName == nameof(request.To)).ErrorMessage);

        message.Should().Be("Je vyžadován alespoň jeden příjemce.");
    }

    [Fact]
    public void Send_InvalidEmailAndBadJson_AreInvalid()
    {
        var validator = new SendEmailRequestValidator(Localizer());
        var request = new SendEmailRequest
        {
            To = new[] { "not-an-email" },
            VariablesJson = "{ broken",
        };

        var result = validator.Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("email") || e.ErrorMessage.Contains("e-mail"));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.VariablesJson));
    }

    [Fact]
    public void Send_ValidRequest_Passes()
    {
        var validator = new SendEmailRequestValidator(Localizer());
        var request = new SendEmailRequest
        {
            To = new[] { "a@example.com" },
            Cc = new[] { "b@example.com" },
            VariablesJson = "{\"name\":\"x\"}",
        };

        validator.Validate(request).IsValid.Should().BeTrue();
    }
}
