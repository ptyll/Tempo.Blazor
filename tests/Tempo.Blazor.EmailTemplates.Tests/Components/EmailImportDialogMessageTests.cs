using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Tests.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 7A review fix: every validation sentence the import dialog shows goes through the same formatter as
/// the validation panel. The preheader rule carries a <c>{0}</c> threshold — rendering it with the one-arg
/// localizer indexer printed a literal „{0}“ in the warnings list, so the import reported a placeholder
/// instead of the 100-character limit it measured.
/// </summary>
public class EmailImportDialogMessageTests : BunitContext, IDisposable
{
    private readonly CultureScope _culture = CultureScope.Use("cs");

    public EmailImportDialogMessageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddTempoEmailTemplateEditor();
    }

    void IDisposable.Dispose()
    {
        _culture.Dispose();
        Dispose();
    }

    [Fact]
    public void A_preheader_warning_names_the_limit_instead_of_printing_a_placeholder()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(MjmlWithPreheader(new string('x', 120)));

        var warnings = cut.Find("[data-tm-import-warnings]");
        warnings.TextContent.Should().NotContain("{0}", "the threshold argument must reach the sentence");
        warnings.TextContent.Should().Contain("100", "the rule names the limit it measured");
        warnings.TextContent.Should().Contain("Úvodní text (preheader)",
            "the sentence is the localized preheader rule, and it uses the same term as the field label");
    }

    [Fact]
    public void A_short_preheader_produces_no_warning_row()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(MjmlWithPreheader(new string('x', 20)));

        cut.FindAll("[data-tm-import-warnings]").Should().BeEmpty();
        cut.Find("[data-tm-import-summary]").Should().NotBeNull();
    }

    private static string MjmlWithPreheader(string preheader)
        => $"""
            <mjml>
              <mj-head><mj-preview>{preheader}</mj-preview></mj-head>
              <mj-body><mj-section><mj-column><mj-text>Ahoj</mj-text></mj-column></mj-section></mj-body>
            </mjml>
            """;

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
