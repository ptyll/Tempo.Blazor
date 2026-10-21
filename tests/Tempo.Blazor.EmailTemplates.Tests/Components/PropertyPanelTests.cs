using Tempo.Blazor.Components.Inputs;
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

public class PropertyPanelTests : BunitContext
{
    public PropertyPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
    }

    private static (EmailTemplateDocument doc, EmailButtonBlock button) DocWithButton()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        var button = new EmailButtonBlock { Text = "Go", Href = "https://x" };
        col.Blocks.Add(button);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return (doc, button);
    }

    [Fact]
    public void NoSelection_ShowsDocumentPanel()
    {
        var (doc, _) = DocWithButton();
        var cut = Render<TmEmailPropertyPanel>(p => p.Add(c => c.Document, doc));
        cut.Find("[data-tm-prop-target=\"document\"]").Should().NotBeNull();
    }

    [Fact]
    public void BlockSelected_ShowsBlockPanel()
    {
        var (doc, button) = DocWithButton();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, button.Id));
        cut.Find("[data-tm-prop-target=\"block\"]").Should().NotBeNull();
    }

    [Fact]
    public void SectionSelected_ShowsSectionPanel()
    {
        var (doc, _) = DocWithButton();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, doc.Sections[0].Id));
        cut.Find("[data-tm-prop-target=\"section\"]").Should().NotBeNull();
    }

    [Fact]
    public void ColumnSelected_ShowsColumnPanel()
    {
        var (doc, _) = DocWithButton();
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, doc.Sections[0].Columns[0].Id));
        cut.Find("[data-tm-prop-target=\"column\"]").Should().NotBeNull();
    }

    [Fact]
    public void BlockPanel_CoversEveryScalarAttribute()
    {
        var (doc, button) = DocWithButton();
        var expected = PropertyReflection.GetFields(button).Select(f => f.Name).ToHashSet(StringComparer.Ordinal);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, button.Id));

        // Both inspector tabs together must cover every scalar attribute (Fáze 7A split them).
        var rendered = cut.FindAll("[data-tm-prop]").Select(e => e.GetAttribute("data-tm-prop"))
            .Concat(StyleTabFields(cut)).ToHashSet(StringComparer.Ordinal);
        rendered.Should().BeEquivalentTo(expected, "every scalar attribute must have an editor (parity)");
    }

    [Fact]
    public void EveryBlockType_RendersWithoutError()
    {
        foreach (var type in Enum.GetValues<BlockType>())
        {
            var doc = new EmailTemplateDocument();
            var section = new EmailSection();
            var col = new EmailColumn();
            var block = new Tempo.Blazor.EmailTemplates.Abstractions.Registry.BlockRegistry().CreateInstance(type);
            col.Blocks.Add(block);
            section.Columns.Add(col);
            doc.Sections.Add(section);

            var act = () => Render<TmEmailPropertyPanel>(p => p
                .Add(c => c.Document, doc).Add(c => c.SelectedId, block.Id));
            act.Should().NotThrow($"the property panel must render {type}");
        }
    }

    [Fact]
    public void EditingDocumentSubject_UpdatesModel_AndRaisesOnChanged()
    {
        var doc = new EmailTemplateDocument { Subject = "old" };
        var changed = false;
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc)
            .Add(c => c.OnChanged, () => changed = true));

        cut.Find("[data-tm-prop=\"Subject\"] input").Change("New subject");

        doc.Subject.Should().Be("New subject");
        changed.Should().BeTrue();
    }

    [Fact]
    public void EditingSectionBackground_UpdatesModel()
    {
        var (doc, _) = DocWithButton();
        var section = doc.Sections[0];
        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, section.Id)
            .Add(c => c.OnChanged, () => { }));

        cut.Find("#tab-style").Click();
        // Fáze 7A review: a NULLABLE colour is a picker with a clear button, not a text field (an empty value
        // is a valid "unset" and must not show an error), so the model write is driven through the picker.
        var picker = cut.FindComponent<TmColorPicker>();
        cut.InvokeAsync(() => picker.Instance.ValueChanged.InvokeAsync("#abcdef"));

        section.BackgroundColor.Should().Be("#abcdef");
    }

    private static IEnumerable<string> StyleTabFields(IRenderedComponent<TmEmailPropertyPanel> cut)
    {
        cut.Find("#tab-style").Click();
        return cut.FindAll("[data-tm-prop]").Select(e => e.GetAttribute("data-tm-prop")!).ToList();
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
