using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Resources;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Validation;

/// <summary>
/// The Abstractions half of the host-localization contract (plan 2.10.0, B6): the request validators
/// take <c>IStringLocalizer&lt;EmailTemplateValidationResources&gt;</c>, and that service is resolved
/// from the container by whoever runs the validator. A host that replaces the whole
/// <c>IStringLocalizerFactory</c> pipeline (PostrionCore's <c>AddJsonLocalization()</c> calls
/// <c>RemoveAll&lt;IStringLocalizerFactory&gt;()</c>) used to get bare keys as validation messages;
/// a host that never calls <c>AddLocalization()</c> got an unresolvable service. Both are fixed by the
/// same thing: a CLOSED registration over embedded JSON, which <c>AddTempoEmailTemplateEngine</c> owns.
/// </summary>
public class HostLocalizerFactoryReplacementTests
{
    private static ServiceProvider BuildProvider(Action<IServiceCollection>? before = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        before?.Invoke(services);
        services.AddTempoEmailTemplateEngine();
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderWithoutAnyHostLocalization() => BuildProvider();

    private static ServiceProvider BuildProviderWithHostFactoryReplacingLocalization()
        => BuildProvider(services =>
        {
            services.RemoveAll<IStringLocalizerFactory>();
            services.AddSingleton<IStringLocalizerFactory>(new HostKeyLocalizerFactory());
            services.RemoveAll(typeof(IStringLocalizer<>));
            services.TryAdd(ServiceDescriptor.Singleton(typeof(IStringLocalizer<>), typeof(HostKeyStringLocalizer<>)));
        });

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

    private static string NameError(ServiceProvider provider, string culture)
    {
        using var scope = provider.CreateScope();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<CreateEmailTemplateRequest>>();
        var request = new CreateEmailTemplateRequest { Name = string.Empty, Language = "en", ContentJson = "{}" };

        return InCulture(culture, () => validator.Validate(request)
            .Errors.First(e => e.PropertyName == nameof(request.Name)).ErrorMessage);
    }

    [Theory]
    [InlineData("en", "The template name is required.")]
    [InlineData("cs", "Název šablony je povinný.")]
    [InlineData("sk", "Názov šablóny je povinný.")]
    [InlineData("fr", "Le nom du modèle est obligatoire.")]
    // A host that sets a region-qualified culture must fall back to the language table, not to English.
    [InlineData("cs-CZ", "Název šablony je povinný.")]
    // A culture with no shipped table (and the invariant culture) resolves the neutral English table.
    [InlineData("de-DE", "The template name is required.")]
    [InlineData("", "The template name is required.")]
    public void ValidatorMessages_ResolveWithoutAnyHostLocalization(string culture, string expected)
    {
        using var provider = BuildProviderWithoutAnyHostLocalization();

        NameError(provider, culture).Should().Be(expected,
            "the engine package ships its own {0} validation table — a host needs no AddLocalization()", culture);
    }

    [Theory]
    [InlineData("en", "The template name is required.")]
    [InlineData("cs", "Název šablony je povinný.")]
    public void ValidatorMessages_SurviveAHostFactoryReplacement(string culture, string expected)
    {
        using var provider = BuildProviderWithHostFactoryReplacingLocalization();

        var message = NameError(provider, culture);

        message.Should().Be(expected);
        message.Should().NotBe("Name_Required", "a raw key means the host's factory served the lookup");
    }

    [Fact]
    public void ClosedValidationResourceLocalizer_WinsOverTheHostsOpenGenericFactory()
    {
        using var provider = BuildProviderWithHostFactoryReplacingLocalization();

        var localizer = provider.GetRequiredService<IStringLocalizer<EmailTemplateValidationResources>>();

        InCulture("cs", () => localizer["Name_Required"].Value).Should().Be("Název šablony je povinný.");
        InCulture("cs", () => localizer["Name_Required"].ResourceNotFound).Should().BeFalse();
    }

    [Fact]
    public void ShippedValidationTable_ResolvesEveryKeyInAllFourLanguages()
    {
        using var provider = BuildProviderWithoutAnyHostLocalization();
        var localizer = provider.GetRequiredService<IStringLocalizer<EmailTemplateValidationResources>>();

        var shipped = localizer.GetAllStrings(includeParentCultures: true).Select(s => s.Name).ToList();
        shipped.Should().NotBeEmpty("the neutral table is embedded JSON in this very assembly");

        foreach (var culture in new[] { "en", "cs", "sk", "fr" })
        {
            var rawKeys = InCulture(culture, () => shipped.Where(key => localizer[key] == key).ToList());
            rawKeys.Should().BeEmpty("every shipped key needs a {0} value, not the key itself", culture);
        }
    }

    [Fact]
    public void AddTempoEmailTemplateEngine_DoesNotRegisterAStringLocalizerFactory()
    {
        using var provider = BuildProviderWithoutAnyHostLocalization();

        provider.GetService<IStringLocalizerFactory>().Should().BeNull(
            "the engine registers closed IStringLocalizer<T> services, not the host's localization pipeline");
    }

    /// <summary>A host factory with no idea what Tempo's keys mean — it answers every lookup with the key.</summary>
    private sealed class HostKeyLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new HostKeyLocalizer();

        public IStringLocalizer Create(string baseName, string location) => new HostKeyLocalizer();
    }

    private sealed class HostKeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Enumerable.Empty<LocalizedString>();
    }

    private sealed class HostKeyStringLocalizer<T> : IStringLocalizer<T>
    {
        public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, name, resourceNotFound: true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Enumerable.Empty<LocalizedString>();
    }
}
