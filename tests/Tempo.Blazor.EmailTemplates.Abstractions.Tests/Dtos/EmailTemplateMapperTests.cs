using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Dtos;

public class EmailTemplateMapperTests
{
    [Fact]
    public void ContentJson_RoundTrips()
    {
        var doc = SampleDocuments.FullyPopulated();

        var json = EmailTemplateMapper.ToContentJson(doc);
        var back = EmailTemplateMapper.ToDocument(json);

        SampleDocuments.CountAllBlocks(back).Should().Be(SampleDocuments.CountAllBlocks(doc));
        back.Subject.Should().Be(doc.Subject);
    }

    [Fact]
    public void ToDetailDto_CarriesMetadataContentJsonAndRequiredVariables()
    {
        var doc = new EmailTemplateDocument
        {
            Name = "Welcome", Subject = "Hi {{ first_name }}", Preheader = "{{ order_id }}", Language = "en",
        };
        var section = new EmailSection();
        var col = new EmailColumn();
        col.Blocks.Add(new EmailTextBlock { Content = "{{ first_name }}" });
        section.Columns.Add(col);
        doc.Sections.Add(section);

        var dto = EmailTemplateMapper.ToDetailDto(doc);

        dto.Id.Should().Be(doc.Id);
        dto.Name.Should().Be("Welcome");
        dto.Language.Should().Be("en");
        dto.ContentJson.Should().Contain("\"$type\"");
        dto.RequiredVariables.Should().Contain(new[] { "first_name", "order_id" });
        EmailTemplateMapper.ToDocument(dto.ContentJson).Subject.Should().Be("Hi {{ first_name }}");
    }

    [Fact]
    public void ToSummaryDto_ProjectsListFields()
    {
        var doc = new EmailTemplateDocument { Name = "N", Subject = "S", Language = "cs" };

        var summary = EmailTemplateMapper.ToSummaryDto(doc, isActive: true);

        summary.Id.Should().Be(doc.Id);
        summary.Name.Should().Be("N");
        summary.Subject.Should().Be("S");
        summary.IsActive.Should().BeTrue();
    }

    [Fact]
    public void ApplyCreate_BuildsDocumentFromRequest()
    {
        var doc = SampleDocuments.FullyPopulated();
        var request = new CreateEmailTemplateRequest
        {
            Name = "New", Subject = "Subj", Preheader = "Pre", Language = "fr",
            ContentJson = EmailTemplateMapper.ToContentJson(doc),
        };

        var built = EmailTemplateMapper.ApplyCreate(request);

        built.Name.Should().Be("New");
        built.Subject.Should().Be("Subj");
        built.Preheader.Should().Be("Pre");
        built.Language.Should().Be("fr");
        SampleDocuments.CountAllBlocks(built).Should().Be(SampleDocuments.CountAllBlocks(doc));
    }

    // Fáze 10 (carry-forward): the server path must apply the SAME import limits as the editor, so
    // "what passes the import passes the server" holds for JSON coming from a client. The exception's
    // key is what an API turns into a 422 the caller can localize.

    [Fact]
    public void ToDocument_WithImportOptions_RejectsAPayloadOverMaxInputBytes()
    {
        var json = EmailTemplateMapper.ToContentJson(SampleDocuments.FullyPopulated());
        var options = new EmailImportOptions { MaxInputBytes = 32 };

        var act = () => EmailTemplateMapper.ToDocument(json, options);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InputTooLarge);
    }

    [Fact]
    public void ToDocument_WithImportOptions_RejectsMoreSectionsThanTheLimit()
    {
        var doc = SampleDocuments.FullyPopulated();
        var json = EmailTemplateMapper.ToContentJson(doc);
        var options = new EmailImportOptions { MaxSections = 1 };

        var act = () => EmailTemplateMapper.ToDocument(json, options);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(Abstractions.Rendering.DocumentValidationKeys.TooManySections);
    }

    [Fact]
    public void ToDocument_WithImportOptions_RejectsANewerSchemaVersion()
    {
        var options = new EmailImportOptions();

        var act = () => EmailTemplateMapper.ToDocument("{\"schemaVersion\":99,\"sections\":[]}", options);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.UnsupportedSchemaVersion);
    }

    [Fact]
    public void ApplyCreate_WithImportOptions_RejectsAnOversizedPayload()
    {
        var request = new CreateEmailTemplateRequest
        {
            Name = "New", Subject = "S", Language = "en",
            ContentJson = EmailTemplateMapper.ToContentJson(SampleDocuments.FullyPopulated()),
        };

        var act = () => EmailTemplateMapper.ApplyCreate(request, new EmailImportOptions { MaxInputBytes = 16 });

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InputTooLarge);
    }

    [Fact]
    public void ApplyUpdate_WithImportOptions_RejectsANonNumericSchemaVersion()
    {
        var request = new UpdateEmailTemplateRequest
        {
            Name = "N", Subject = "S", Language = "en", IsActive = true,
            ContentJson = "{\"schemaVersion\":\"two\",\"sections\":[]}",
        };

        var act = () => EmailTemplateMapper.ApplyUpdate(request, new EmailImportOptions());

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InvalidSchemaVersion);
    }
}
