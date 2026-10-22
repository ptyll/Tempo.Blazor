using Tempo.Blazor.EmailTemplates.Abstractions;
using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Localization;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Resources;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Localization;

/// <summary>
/// The localization contract that made the editor unusable in PostrionCore: its
/// <c>AddJsonLocalization()</c> calls <c>RemoveAll&lt;IStringLocalizerFactory&gt;()</c> and registers its
/// own factory plus an open-generic <c>IStringLocalizer&lt;&gt;</c>. A resx-backed editor localizer
/// resolves through whatever factory is registered, so every Tempo key came back as the key itself.
/// <para>
/// The fix is a CLOSED registration of <c>IStringLocalizer&lt;TmEmailResources&gt;</c> over embedded
/// JSON (exactly what the Tempo core does with <c>TmResources*.json</c>): a closed registration beats an
/// open-generic factory in the DI container, so the host's factory never sees the email resources.
/// These tests simulate the host: the fake factory deliberately answers every key with the key, which
/// is the worst case a real host can produce.
/// </para>
/// </summary>
public class HostLocalizerFactoryReplacementTests
{
    /// <summary>Registers <see cref="ServiceCollectionExtensions.AddTempoEmailTemplates"/> and then rips
    /// the localization pipeline out of it the way PostrionCore's <c>AddJsonLocalization()</c> does.</summary>
    private static ServiceProvider BuildProviderWithHostFactoryAfterTempo()
    {
        var services = new ServiceCollection();
        services.AddTempoEmailTemplates();
        ReplaceLocalizationWithHostFactory(services);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider BuildProviderWithHostFactoryBeforeTempo()
    {
        var services = new ServiceCollection();
        ReplaceLocalizationWithHostFactory(services);
        services.AddTempoEmailTemplates();
        return services.BuildServiceProvider();
    }

    private static void ReplaceLocalizationWithHostFactory(IServiceCollection services)
    {
        services.RemoveAll<IStringLocalizerFactory>();
        services.AddSingleton<IStringLocalizerFactory>(new HostKeyLocalizerFactory());
        services.RemoveAll(typeof(IStringLocalizer<>));
        services.TryAdd(ServiceDescriptor.Singleton(typeof(IStringLocalizer<>), typeof(HostKeyStringLocalizer<>)));
    }

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

    [Theory]
    [InlineData("en", "Save")]
    [InlineData("cs", "Uložit")]
    [InlineData("sk", "Uložiť")]
    [InlineData("fr", "Enregistrer")]
    // A host that sets a region-qualified culture must fall back to the language table, not to English.
    [InlineData("cs-CZ", "Uložit")]
    [InlineData("sk-SK", "Uložiť")]
    // A culture with no shipped table (and the invariant culture) resolves the neutral English table.
    [InlineData("de-DE", "Save")]
    [InlineData("", "Save")]
    public void EmailLocalizer_StillResolvesShippedText_WhenHostReplacesTheFactoryAfterTempo(
        string culture, string expected)
    {
        using var provider = BuildProviderWithHostFactoryAfterTempo();
        var localizer = provider.GetRequiredService<ITmEmailLocalizer>();

        var text = InCulture(culture, () => localizer["EmailEditor_Toolbar_Save"]);

        text.Should().Be(expected,
            "the editor must ship its own {0} table instead of borrowing the host's factory", culture);
        text.Should().NotBe("EmailEditor_Toolbar_Save", "a raw key means the host's factory served the lookup");
    }

    [Theory]
    [InlineData("en", "Save")]
    [InlineData("cs", "Uložit")]
    public void EmailLocalizer_StillResolvesShippedText_WhenHostReplacesTheFactoryBeforeTempo(
        string culture, string expected)
    {
        using var provider = BuildProviderWithHostFactoryBeforeTempo();
        var localizer = provider.GetRequiredService<ITmEmailLocalizer>();

        InCulture(culture, () => localizer["EmailEditor_Toolbar_Save"]).Should().Be(expected);
    }

    [Fact]
    public void EmailLocalizer_ResolvesEveryShippedKey_NotJustTheProbedOne()
    {
        using var provider = BuildProviderWithHostFactoryAfterTempo();
        var localizer = provider.GetRequiredService<ITmEmailLocalizer>();
        var resourceLocalizer = provider.GetRequiredService<IStringLocalizer<TmEmailResources>>();

        var shipped = resourceLocalizer.GetAllStrings(includeParentCultures: true).Select(s => s.Name).ToList();
        shipped.Should().NotBeEmpty("the neutral table is embedded JSON in this very assembly");

        var rawKeys = InCulture("cs", () => shipped
            .Where(key => localizer[key] == key)
            .ToList());

        rawKeys.Should().BeEmpty("every shipped key needs a Czech value, not the key itself");
    }

    [Fact]
    public void ClosedEmailResourceLocalizer_WinsOverTheHostsOpenGenericFactory()
    {
        using var provider = BuildProviderWithHostFactoryAfterTempo();

        var localizer = provider.GetRequiredService<IStringLocalizer<TmEmailResources>>();

        InCulture("en", () => localizer["EmailEditor_Toolbar_Save"].Value).Should().Be("Save");
        InCulture("en", () => localizer["EmailEditor_Toolbar_Save"].ResourceNotFound).Should().BeFalse();
    }

    [Fact]
    public void AddTempoEmailTemplates_DoesNotRegisterAStringLocalizerFactory()
    {
        // B6: the package must not drag AddLocalization() (and its resource-manager pipeline, which
        // returns raw keys under published Blazor WebAssembly) into a host that never asked for it.
        var services = new ServiceCollection();

        services.AddTempoEmailTemplates();

        using var provider = services.BuildServiceProvider();
        provider.GetService<IStringLocalizerFactory>().Should().BeNull(
            "AddTempoEmailTemplates registers closed IStringLocalizer<T> services, not the localization pipeline");
    }

    [Fact]
    public void EmailLocalizer_FormatsArguments_AndNeverThrowsForAMissingKey()
    {
        using var provider = BuildProviderWithHostFactoryAfterTempo();
        var localizer = provider.GetRequiredService<ITmEmailLocalizer>();

        // A shipped key with placeholders still formats through the argument overload.
        InCulture("en", () => localizer["EmailEditor_Import_SummarySections", 3])
            .Should().Be("Sections: 3");
        InCulture("en", () => localizer["EmailEditor_Import_DropLimit", "5 MB"])
            .Should().Be("Up to 5 MB");

        // …and an unknown key must come back as the key, not as a FormatException from string.Format.
        InCulture("en", () => localizer["No_Such_Key", 1])
            .Should().Be("No_Such_Key", "a missing key is reported, never thrown");
    }

    [Fact]
    public void HostOverrides_Win_BeforeAndAfter()
    {
        // (1) A resource localizer registered BEFORE the package call wins: the package uses TryAdd, so
        //     it never replaces a host's own IStringLocalizer<TmEmailResources>.
        var before = new ServiceCollection();
        before.AddSingleton<IStringLocalizer<TmEmailResources>>(new StubStringLocalizer("X"));
        before.AddTempoEmailTemplates();
        using (var provider = before.BuildServiceProvider())
        {
            provider.GetRequiredService<ITmEmailLocalizer>()["EmailEditor_Toolbar_Save"].Should().Be("X",
                "TryAdd keeps a resource localizer the host registered first");
        }

        // (2) An ITmEmailLocalizer decorator registered AFTER the package call wins: the last
        //     registration of one service type wins in MS.DI.
        var after = new ServiceCollection();
        after.AddTempoEmailTemplates();
        after.AddSingleton<ITmEmailLocalizer>(new StubEmailLocalizer("Y"));
        using (var provider = after.BuildServiceProvider())
        {
            provider.GetRequiredService<ITmEmailLocalizer>()["EmailEditor_Toolbar_Save"].Should().Be("Y",
                "a localizer registered after the package call replaces the default one");
        }
    }

    [Fact]
    public void Editor_StillRendersLocalizedToolbar_WhenTheHostFactoryReplacedLocalization()
    {
        using var ctx = new BunitContext();
        ctx.JSInterop.Mode = JSRuntimeMode.Loose;
        ctx.Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        ctx.Services.AddTempoEmailTemplateEditor();
        ctx.Services.AddTempoEmailTemplateEngine();
        ReplaceLocalizationWithHostFactory(ctx.Services);

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("cs");
            var cut = ctx.Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, new EmailTemplateDocument()));

            cut.Find("[data-tm-save]").TextContent.Should().Contain("Uložit");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public void Editor_WithoutTheEngine_StillTranslatesValidationMessages()
    {
        // §3.0 "Sémantika registrací": the validators' closed JSON localizer lives with the validators in
        // Abstractions and is registered by AddTempoEmailTemplateModel — which the editor calls — so an
        // editor-only host (the WASM pattern, no Engine package at all) gets translated validation
        // messages. Moving that registration into the Engine would silently downgrade this to raw keys.
        var services = new ServiceCollection();
        services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        services.AddTempoEmailTemplateEditor();
        ReplaceLocalizationWithHostFactory(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<IMjmlCompiler>().Should().BeNull("the editor registers no engine");
        scope.ServiceProvider.GetService<ITemplateValidator>().Should().BeNull("no engine, no syntax validator");

        var validator = scope.ServiceProvider
            .GetRequiredService<IValidator<CreateEmailTemplateRequest>>();
        var request = new CreateEmailTemplateRequest { Name = string.Empty, Language = "en", ContentJson = "{}" };

        var message = InCulture("cs", () => validator.Validate(request)
            .Errors.First(e => e.PropertyName == nameof(request.Name)).ErrorMessage);

        message.Should().Be("Název šablony je povinný.");
        message.Should().NotBe("Name_Required", "a raw key means the registration moved somewhere the editor cannot see");
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

    /// <summary>Core-component strings are irrelevant here; the email editor injects ITmEmailLocalizer.</summary>
    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }

    /// <summary>A host's own resource localizer — proves a pre-registration is kept, not replaced.</summary>
    private sealed class StubStringLocalizer : IStringLocalizer<TmEmailResources>
    {
        private readonly string _text;

        public StubStringLocalizer(string text) => _text = text;

        public LocalizedString this[string name] => new(name, _text, resourceNotFound: false);

        public LocalizedString this[string name, params object[] arguments] => new(name, _text, resourceNotFound: false);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            Enumerable.Empty<LocalizedString>();
    }

    /// <summary>A host decorator for the editor's localizer — proves the last registration wins.</summary>
    private sealed class StubEmailLocalizer : ITmEmailLocalizer
    {
        private readonly string _text;

        public StubEmailLocalizer(string text) => _text = text;

        public string this[string key] => _text;

        public string this[string key, params object[] arguments] => _text;
    }
}
