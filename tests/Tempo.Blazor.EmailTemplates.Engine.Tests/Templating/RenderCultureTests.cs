using System.Globalization;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Templating;

/// <summary>
/// Formatter culture of a render (§3.7, Fáze 4): <c>TemplateRenderOptions.Culture</c> decides how
/// numbers and dates are formatted, <c>null</c> means <see cref="CultureInfo.InvariantCulture"/> so a
/// render is deterministic, and <c>UseDocumentLanguageCulture</c> lets the renderer take the culture
/// from <see cref="EmailTemplateDocument.Language"/>.
/// </summary>
public class RenderCultureTests
{
    /// <summary>Non-breaking space — the Czech thousands separator.</summary>
    private const string Nbsp = "\u00A0";

    private static readonly object Model = new
    {
        price = 1234.5m,
        created = new DateTime(2026, 10, 20, 13, 5, 0),
    };

    private static ScribanTemplateEngine Engine() => new();

    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(
            new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance), new MjmlNetCompiler(), new TextVersionGenerator());

    private static EmailTemplateDocument DocWithSubject(string subject, string language = "en")
        => new() { Subject = subject, Language = language };

    private static EmailTemplateDocument DocWithBody(string content, string language = "en")
    {
        var doc = new EmailTemplateDocument { Subject = "S", Language = language };
        doc.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = content } } } },
        });
        return doc;
    }

    // ── The public surface of TemplateRenderOptions (the renderer must copy *all* of it) ────────

    [Fact]
    public void TemplateRenderOptions_HasExactlyTheTwoDocumentedProperties()
    {
        // A reminder for EmailTemplateRenderer.ResolveRenderOptions: a third property added later must be
        // carried over when the renderer builds the resolved options, otherwise it is silently dropped.
        var properties = typeof(TemplateRenderOptions).GetProperties();

        properties.Select(property => property.Name)
            .Should().BeEquivalentTo([nameof(TemplateRenderOptions.Culture), nameof(TemplateRenderOptions.UseDocumentLanguageCulture)]);
        properties.Should().OnlyContain(property => property.SetMethod!.ReturnParameter.GetRequiredCustomModifiers()
            .Any(modifier => modifier.Name == "IsExternalInit"));
    }

    // ── Engine: TemplateRenderOptions.Culture ───────────────────────────────────────────────────

    [Fact]
    public void Engine_DefaultCulture_IsInvariantCulture_EvenWhenTheAmbientCultureIsCzech()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("cs-CZ");

            // A render must not depend on the server's thread culture: tests and previews would
            // otherwise change output between machines.
            Engine().Render("{{ price }}", Model).Value.Should().Be("1234.5");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Engine_CzechCulture_FormatsNumberFilterWithDecimalComma()
    {
        var result = Engine().Render(
            "{{ price | math.format \"N2\" }}", Model, new TemplateRenderOptions { Culture = new CultureInfo("cs-CZ") });

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be($"1{Nbsp}234,50");
    }

    [Fact]
    public void Engine_CzechCulture_FormatsDateFilterWithCzechMonthName()
    {
        var result = Engine().Render(
            "{{ created | date.to_string \"%d. %B %Y\" }}",
            Model,
            new TemplateRenderOptions { Culture = new CultureInfo("cs-CZ") });

        result.Value.Should().Be("20. říjen 2026");
    }

    [Fact]
    public void Engine_InvariantCulture_FormatsDateFilterWithEnglishMonthName()
    {
        var result = Engine().Render(
            "{{ created | date.to_string \"%d. %B %Y\" }}",
            Model,
            new TemplateRenderOptions { Culture = CultureInfo.InvariantCulture });

        result.Value.Should().Be("20. October 2026");
    }

    [Fact]
    public void Engine_UseDocumentLanguageCultureWithoutADocument_FallsBackToInvariantCulture()
    {
        // The engine has no document; only the renderer can resolve a document language.
        var result = Engine().Render(
            "{{ price | math.format \"N2\" }}", Model, new TemplateRenderOptions { UseDocumentLanguageCulture = true });

        result.Value.Should().Be("1,234.50");
    }

    // ── Renderer: culture from options or from the document language ────────────────────────────

    [Fact]
    public async Task Renderer_UseDocumentLanguageCulture_FormatsWithTheDocumentLanguage()
    {
        var doc = DocWithSubject("{{ price | math.format \"N2\" }}", language: "cs");

        var result = await Renderer().RenderAsync(
            doc, Model, new TemplateRenderOptions { UseDocumentLanguageCulture = true });

        result.Subject.Should().Be($"1{Nbsp}234,50");
    }

    [Fact]
    public async Task Renderer_DocumentLanguageIsIgnored_WithoutTheOptIn()
    {
        var doc = DocWithSubject("{{ price | math.format \"N2\" }}", language: "cs");

        var result = await Renderer().RenderAsync(doc, Model, new TemplateRenderOptions());

        result.Subject.Should().Be("1,234.50");
    }

    [Fact]
    public async Task Renderer_ExplicitCultureWinsOverTheDocumentLanguage()
    {
        var doc = DocWithSubject("{{ price | math.format \"N2\" }}", language: "cs");

        var result = await Renderer().RenderAsync(
            doc,
            Model,
            new TemplateRenderOptions { Culture = new CultureInfo("de-DE"), UseDocumentLanguageCulture = true });

        result.Subject.Should().Be("1.234,50");
    }

    [Fact]
    public async Task Renderer_UnknownDocumentLanguage_FailsClosedToInvariantCulture()
    {
        var doc = DocWithSubject("{{ price | math.format \"N2\" }}", language: "!!not-a-culture!!");

        var result = await Renderer().RenderAsync(
            doc, Model, new TemplateRenderOptions { UseDocumentLanguageCulture = true });

        result.Subject.Should().Be("1,234.50");
    }

    [Fact]
    public async Task Renderer_BodyPass_UsesTheSameCulture()
    {
        var doc = DocWithBody("{{ price }}", language: "cs");

        var result = await Renderer().RenderAsync(
            doc, Model, new TemplateRenderOptions { UseDocumentLanguageCulture = true });

        result.Html.Should().Contain("1234,5");
        result.Html.Should().NotContain("1234.5");
        result.TextVersion.Should().Be("1234,5");
    }
}
