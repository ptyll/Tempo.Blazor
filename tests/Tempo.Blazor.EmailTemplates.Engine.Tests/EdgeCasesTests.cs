using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;
using Tempo.Blazor.EmailTemplates.Abstractions.Templating;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests;

/// <summary>Boundary review across phases E1–E4 (empty/null, extremes, unicode, RTL).</summary>
public class EdgeCasesTests
{
    private static IEmailTemplateRenderer Renderer()
        => new EmailTemplateRenderer(new ScribanTemplateEngine(), new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance), new MjmlNetCompiler(), new TextVersionGenerator());

    [Fact]
    public void EmptyDocument_SerializesClonesAndGenerates()
    {
        var doc = new EmailTemplateDocument();

        var clone = doc.DeepClone();
        clone.Sections.Should().BeEmpty();

        var json = EmailTemplateSerializer.Serialize(doc);
        EmailTemplateSerializer.Deserialize(json).Sections.Should().BeEmpty();

        new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance).Generate(doc).Should().Contain("<mj-body");
    }

    [Fact]
    public async Task EmptyDocument_RendersWithoutCrash()
    {
        var result = await Renderer().RenderAsync(new EmailTemplateDocument());
        result.Should().NotBeNull();
        result.Html.Should().Contain("<html");
    }

    [Fact]
    public async Task ExtremelyLongContent_RendersWithinLimit()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = new string('A', 100_000) });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var result = await Renderer().RenderAsync(doc, new { });

        result.Success.Should().BeTrue();
        result.Html.Should().Contain(new string('A', 1000));
    }

    [Fact]
    public async Task RtlText_SurvivesPipeline()
    {
        const string arabic = "مرحبا بالعالم";
        const string hebrew = "שלום עולם";
        var doc = new EmailTemplateDocument { Subject = arabic };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = hebrew });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var json = EmailTemplateSerializer.Serialize(doc);
        EmailTemplateSerializer.Deserialize(json).Subject.Should().Be(arabic);

        var result = await Renderer().RenderAsync(doc, new { });
        result.Subject.Should().Be(arabic);
        result.Html.Should().Contain(hebrew);
    }

    [Fact]
    public void NullOptionalAttributes_ProduceMinimalMjml()
    {
        var doc = new EmailTemplateDocument();
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "x" }); // all optionals null/default
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var mjml = new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance).Generate(doc);

        mjml.Should().NotContain("padding-top");
        mjml.Should().NotContain("font-style");
    }

    [Fact]
    public void ExtractVariables_EmptyOrWhitespace_IsEmpty()
    {
        var engine = new ScribanTemplateEngine();
        engine.ExtractVariables("").Should().BeEmpty();
        engine.ExtractVariables("   ").Should().BeEmpty();
        engine.ExtractVariables("no variables here").Should().BeEmpty();
    }

    [Fact]
    public void SampleDataGenerator_NoVariables_ReturnsEmptyModel()
    {
        SampleDataGenerator.Generate(Array.Empty<TemplateVariableInfo>()).Should().BeEmpty();
    }

    [Fact]
    public void EmptySection_GeneratesValidColumnlessSection()
    {
        var doc = new EmailTemplateDocument();
        doc.Sections.Add(new EmailSection());

        // Should not throw and should still emit a section element.
        new MjmlGenerator(AngleSharpHtmlContentSanitizer.Instance).Generate(doc).Should().Contain("<mj-section");
    }
}
