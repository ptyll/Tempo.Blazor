using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.EmailTemplates.Localization;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

/// <summary>
/// Fáze 11 review (leveling pass / ChangesRequested): a component attribute written as a plain string is a
/// LITERAL, so <c>Value="_input"</c> on a string parameter hands the editor the field's NAME instead of its
/// value — the reported symptom was an import box that opened pre-filled with the text “_input”. The Razor
/// compiler cannot catch it (the parameter is a <c>string</c> and the literal type-checks), so these tests pin
/// the two occurrences the source scan found and the behaviour a user sees.
/// </summary>
public class TmEmailFieldBindingReviewFixTests : BunitContext
{
    public TmEmailFieldBindingReviewFixTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ITmEmailLocalizer>(new EchoEmailLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private const string SimpleMjml =
        "<mjml><mj-body><mj-section><mj-column><mj-text>Imported</mj-text></mj-column></mj-section></mj-body></mjml>";

    // ── the import dialog's code surface ────────────────────────────────────────────────────

    [Fact]
    public void ImportDialog_Editor_OpensEmpty_WithItsPlaceholder()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        var textarea = cut.Find("[data-tm-import-input] textarea");
        textarea.GetAttribute("value").Should().BeNullOrEmpty(
            "the box binds the FIELD `_input`; a literal value puts the field's name in the box "
            + "(reported as an import dialog that opens pre-filled with “_input”)");
        textarea.GetAttribute("placeholder").Should().Be("EmailEditor_Import_Placeholder");
    }

    [Fact]
    public void ImportDialog_Editor_ShowsWhatWasTypedOrLoaded()
    {
        var cut = Render<TmEmailImportDialog>(p => p.Add(c => c.Show, true));

        cut.Find("[data-tm-import-input] textarea").Input(SimpleMjml);

        cut.Find("[data-tm-import-input] textarea").GetAttribute("value").Should().Be(SimpleMjml,
            "the code surface mirrors the bound field, not a constant");
    }

    // ── the classes editor's new-name field (same bug, second occurrence) ──────────────────

    [Fact]
    public void ClassesEditor_NewNameField_StartsEmpty_AndAddsTheTypedClass()
    {
        var attributes = new MjAttributes();

        var cut = Render<TmEmailClassesEditor>(p => p.Add(c => c.Attributes, attributes));

        var input = cut.Find("[data-tm-classes-editor] input");
        input.GetAttribute("value").Should().BeNullOrEmpty(
            "a literal Value=\"_newName\" would show the field's name in the input");

        cut.Find("[data-tm-classes-editor] input").Input("brand");
        cut.Find("[data-tm-class-add]").Click();

        attributes.Classes.Should().ContainKey("brand", "the typed name is what the field carries");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }

    private sealed class EchoEmailLocalizer : ITmEmailLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
