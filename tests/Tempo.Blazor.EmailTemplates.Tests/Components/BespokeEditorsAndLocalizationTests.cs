using System.Globalization;
using Bunit;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Tempo.Blazor.EmailTemplates;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Components;
using Tempo.Blazor.Localization;

namespace Tempo.Blazor.EmailTemplates.Tests.Components;

public class BespokeEditorsAndLocalizationTests : BunitContext
{
    public BespokeEditorsAndLocalizationTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplates();
    }

    private static EmailTemplateDocument DocWith(EmailBlockBase block)
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(block);
        section.Columns.Add(col);
        doc.Sections.Add(section);
        return doc;
    }

    private IRenderedComponent<TmEmailPropertyPanel> Panel(EmailTemplateDocument doc, Guid? selected,
        Func<IBrowserFile, Task<string>>? upload = null)
        => Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, selected)
            .Add(c => c.OnImageUpload, upload).Add(c => c.OnChanged, () => { }));

    // ── E7.3a Text: rich editor ─────────────────────────────────────────────────────────────

    [Fact]
    public void TextBlock_UsesRichEditor_NotPlainContentField()
    {
        var text = new EmailTextBlock { Content = "Hi" };
        var cut = Panel(DocWith(text), text.Id);

        cut.FindAll("[data-tm-rich-content]").Should().ContainSingle();
        // Content is rendered by the rich editor, not duplicated as a generic field.
        cut.FindAll("[data-tm-prop=\"Content\"]").Should().BeEmpty();
    }

    // ── E7.3e Raw: warning + textarea ───────────────────────────────────────────────────────

    [Fact]
    public void RawBlock_ShowsUnescapedWarning_AndEditsContent()
    {
        var raw = new EmailRawBlock { Content = "<x>" };
        var cut = Panel(DocWith(raw), raw.Id);

        cut.FindAll("[data-tm-raw-warning]").Should().ContainSingle();
        cut.Find("[data-tm-raw-warning]").Should().NotBeNull();
    }

    // ── E7.3c Image: alt warning + upload ───────────────────────────────────────────────────

    [Fact]
    public void ImageBlock_EmptyAlt_ShowsWarning()
    {
        var image = new EmailImageBlock { Src = "x", Alt = "" };
        var cut = Panel(DocWith(image), image.Id);
        cut.FindAll("[data-tm-alt-warning]").Should().ContainSingle();
    }

    [Fact]
    public void ImageBlock_WithUploadHandler_ShowsUploadZone()
    {
        var image = new EmailImageBlock { Src = "x", Alt = "logo" };
        var cut = Panel(DocWith(image), image.Id, upload: _ => Task.FromResult("https://cdn/img.png"));
        cut.FindAll("[data-tm-image-upload]").Should().ContainSingle();
    }

    // ── E7.3l Wrapper/Group nesting info ────────────────────────────────────────────────────

    [Fact]
    public void WrapperBlock_ShowsNestingInfo()
    {
        var wrapper = new EmailWrapperBlock();
        var cut = Panel(DocWith(wrapper), wrapper.Id);
        cut.FindAll("[data-tm-nesting-info]").Should().ContainSingle();
    }

    // ── E8.6b JSON import ───────────────────────────────────────────────────────────────────

    [Fact]
    public void ImportDialog_JsonMode_ValidJson_Confirms()
    {
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true).Add(c => c.OnImport, d => imported = d));

        cut.Find("[data-tm-import-mode=\"json\"]").Click();
        cut.Find("[data-tm-import-input]").Input("{\"subject\":\"S\",\"sections\":[]}");
        cut.Find("[data-tm-import-confirm]").Click();

        imported.Should().NotBeNull();
        imported!.Subject.Should().Be("S");
    }

    [Fact]
    public void ImportDialog_JsonMode_InvalidJson_ShowsError_NoConfirm()
    {
        EmailTemplateDocument? imported = null;
        var cut = Render<TmEmailImportDialog>(p => p
            .Add(c => c.Show, true).Add(c => c.OnImport, d => imported = d));

        cut.Find("[data-tm-import-mode=\"json\"]").Click();
        cut.Find("[data-tm-import-input]").Input("{ not valid json");

        cut.FindAll("[data-tm-import-errors]").Should().ContainSingle();
        cut.Find("[data-tm-import-confirm]").Click();
        imported.Should().BeNull();
    }

    // ── E8.8 localization ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cs", "Uložit")]
    [InlineData("en", "Save")]
    [InlineData("sk", "Uložiť")]
    [InlineData("fr", "Enregistrer")]
    public void Editor_RendersLocalizedToolbar_PerCulture(string culture, string expectedSave)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            var cut = Render<TmEmailTemplateEditor>(p => p.Add(c => c.Document, new EmailTemplateDocument()));

            // The save button text comes from the real ITmEmailLocalizer over the package's embedded
            // JSON tables, not a key fallback.
            cut.Find("[data-tm-save]").TextContent.Should().Contain(expectedSave);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
