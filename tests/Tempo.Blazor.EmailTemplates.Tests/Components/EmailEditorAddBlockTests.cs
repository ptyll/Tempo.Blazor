using Tempo.Blazor.EmailTemplates.Abstractions;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class EmailEditorAddBlockTests : BunitContext
{
    public EmailEditorAddBlockTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    [Fact]
    public void ClickingToolboxBlock_OnEmptyDocument_CreatesSectionWithBlock()
    {
        EmailTemplateDocument? changed = null;
        var doc = new EmailTemplateDocument();

        var cut = Render<TmEmailTemplateEditor>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.DocumentChanged, d => changed = d));

        cut.Find("[data-tm-block=\"text\"]").Click();

        changed.Should().NotBeNull();
        changed!.Sections.Should().ContainSingle();
        changed.Sections[0].Columns.Should().ContainSingle();
        changed.Sections[0].Columns[0].Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<EmailTextBlock>();
    }

    [Fact]
    public void ClickingToolboxBlock_AppendsToLastSection()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        section.Columns.Add(new EmailColumn());
        doc.Sections.Add(section);

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-block=\"button\"]").Click();

        doc.Sections.Should().ContainSingle();
        doc.Sections[0].Columns[0].Blocks.Should().ContainSingle()
            .Which.Should().BeOfType<EmailButtonBlock>();
    }

    [Fact]
    public void ClickingLayoutPreset_AddsSectionWithColumns()
    {
        var doc = new EmailTemplateDocument();

        var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, doc));

        cut.Find("[data-tm-preset=\"ThreeEqual\"]").Click();

        doc.Sections.Should().ContainSingle();
        doc.Sections[0].Columns.Should().HaveCount(3);
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
