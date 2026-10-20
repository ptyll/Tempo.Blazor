using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class DocumentHeadEditorTests : BunitContext
{
    public DocumentHeadEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void DocumentPanel_RendersFontAndStyleEditors()
    {
        var cut = Render<TmEmailPropertyPanel>(p => p.Add(c => c.Document, new EmailTemplateDocument()));
        cut.Find("[data-tm-head=\"fonts\"]").Should().NotBeNull();
        cut.Find("[data-tm-head=\"styles\"]").Should().NotBeNull();
    }

    [Fact]
    public void AddingFont_AppendsToStyles()
    {
        var doc = new EmailTemplateDocument();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.OnChanged, () => { }));

        cut.Find("[data-tm-head=\"fonts\"] [data-tm-list-add]").Click();

        doc.Styles.Fonts.Should().ContainSingle();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
