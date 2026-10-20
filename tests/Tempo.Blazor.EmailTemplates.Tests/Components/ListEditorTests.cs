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

public class ListEditorTests : BunitContext
{
    public ListEditorTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<ITmLocalizer>(new EchoTmLocalizer());
        Services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        Services.AddHttpClient();
        Services.AddTempoEmailTemplateEditor();
        Services.AddTempoEmailTemplateEngine();
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

    [Fact]
    public void SocialBlock_PanelRendersElementEditors()
    {
        var social = new EmailSocialBlock();
        social.Elements.Add(new EmailSocialElement { Name = "facebook", Href = "#" });
        var doc = DocWith(social);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, social.Id));

        cut.FindAll("[data-tm-list-item]").Should().ContainSingle();
    }

    [Fact]
    public void AddItem_AppendsToCollection()
    {
        var navbar = new EmailNavbarBlock();
        var doc = DocWith(navbar);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, navbar.Id)
            .Add(c => c.OnChanged, () => { }));

        cut.Find("[data-tm-list-add]").Click();

        navbar.Links.Should().ContainSingle();
    }

    [Fact]
    public void RemoveItem_RemovesFromCollection()
    {
        var carousel = new EmailCarouselBlock();
        carousel.Images.Add(new EmailCarouselImage { Src = "a" });
        carousel.Images.Add(new EmailCarouselImage { Src = "b" });
        var doc = DocWith(carousel);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, carousel.Id)
            .Add(c => c.OnChanged, () => { }));

        cut.FindAll("[data-tm-list-item] [data-tm-list-action=\"remove\"]")[0].Click();

        carousel.Images.Should().ContainSingle().Which.Src.Should().Be("b");
    }

    [Fact]
    public void EditingItemField_UpdatesModel()
    {
        var accordion = new EmailAccordionBlock();
        accordion.Items.Add(new EmailAccordionItem { Title = "old" });
        var doc = DocWith(accordion);

        var cut = Render<TmEmailPropertyPanel>(p => p
            .Add(c => c.Document, doc).Add(c => c.SelectedId, accordion.Id)
            .Add(c => c.OnChanged, () => { }));

        cut.Find("[data-tm-list-item] [data-tm-prop=\"Title\"] input").Change("New title");

        accordion.Items[0].Title.Should().Be("New title");
    }

    private sealed class EchoTmLocalizer : ITmLocalizer
    {
        public string this[string key] => key;
        public string this[string key, params object[] arguments] => key;
    }
}
