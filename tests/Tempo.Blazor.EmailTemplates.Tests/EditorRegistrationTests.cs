using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.EmailTemplates.Resources;
using Tempo.Blazor.EmailTemplates.Abstractions.Resources;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests;

/// <summary>
/// Fáze 6/D11: the UI package registers the editor without the engine (<c>AddTempoEmailTemplateEditor</c>),
/// and the legacy <c>AddTempoEmailTemplates</c> is an obsolete shortcut that delegates to it — it no longer
/// registers Scriban/Mjml.Net/AngleSharp, so a WASM host cannot accidentally pull them in.
/// </summary>
public class EditorRegistrationTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        return services;
    }

    [Fact]
    public void Editor_RegistersTheUiAndTheModel_WithoutAnyEngineType()
    {
        using var provider = NewServices().AddTempoEmailTemplateEditor().BuildServiceProvider();

        provider.GetService<ITmEmailLocalizer>().Should().NotBeNull();
        provider.GetRequiredService<IStringLocalizer<TmEmailResources>>().Should().NotBeNull();
        provider.GetRequiredService<IHtmlContentSanitizer>().Should().BeOfType<ConservativeHtmlContentSanitizer>();
        provider.GetRequiredService<ITemplateVariableExtractor>().Should().BeOfType<RegexTemplateVariableExtractor>();
        provider.GetRequiredService<MjmlGenerator>().Should().NotBeNull();

        provider.GetService<IMjmlCompiler>().Should().BeNull("the UI package must not reference the Engine package");
        provider.GetService<IEmailTemplateRenderer>().Should().BeNull();
        provider.GetService<IEmailPreviewRenderer>().Should().BeNull();
        provider.GetService<ITemplateValidator>().Should().BeNull();
        provider.GetService<TemplateSecurityOptions>().Should().NotBeNull();
    }

    [Fact]
    public void Editor_AppliesTheEditorOptionsToTheSharedSecurityOptions()
    {
        using var provider = NewServices()
            .AddTempoEmailTemplateEditor(o => o.Security.LoopLimit = 21)
            .BuildServiceProvider();

        provider.GetRequiredService<TemplateSecurityOptions>().LoopLimit.Should().Be(21);
    }

    [Fact]
    public void Editor_DoesNotRegisterAStringLocalizerFactory()
    {
        using var provider = NewServices().AddTempoEmailTemplateEditor().BuildServiceProvider();

        provider.GetService<IStringLocalizerFactory>().Should().BeNull(
            "the package registers closed IStringLocalizer<T> services, never the host's localization pipeline");
    }

    [Fact]
    public void LegacyAddTempoEmailTemplates_DelegatesToTheEditorRegistration()
    {
        var legacy = NewServices();
#pragma warning disable CS0618 // the obsolete shortcut is exactly what this test pins
        legacy.AddTempoEmailTemplates(o => o.LoopLimit = 33);
#pragma warning restore CS0618
        using var legacyProvider = legacy.BuildServiceProvider();
        using var directProvider = NewServices()
            .AddTempoEmailTemplateEditor(o => o.Security.LoopLimit = 33)
            .BuildServiceProvider();

        legacyProvider.GetRequiredService<TemplateSecurityOptions>().LoopLimit.Should().Be(33);
        legacyProvider.GetService<IMjmlCompiler>().Should().BeNull("the obsolete shortcut registers no engine");
        legacyProvider.GetService<IEmailPreviewRenderer>().Should().BeNull();
        legacyProvider.GetService<ITmEmailLocalizer>().Should().NotBeNull();
        directProvider.GetRequiredService<TemplateSecurityOptions>().LoopLimit.Should().Be(33);
    }

    [Fact]
    public void LegacyShortcut_IsMarkedObsolete()
    {
        var method = typeof(ServiceCollectionExtensions)
            .GetMethods()
            .Single(m => m.Name == nameof(ServiceCollectionExtensions.AddTempoEmailTemplates));

        method.GetCustomAttributes(typeof(ObsoleteAttribute), inherit: false).Should().NotBeEmpty(
            "hosts must be pointed at AddTempoEmailTemplateEditor + AddTempoEmailTemplateEngine (B7)");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;

        public string this[string key, params object[] arguments] => key;
    }
}
