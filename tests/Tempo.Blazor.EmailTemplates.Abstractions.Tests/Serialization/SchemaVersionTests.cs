using System.Reflection;
using Tempo.Blazor.EmailTemplates.Abstractions.Dtos;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Serialization;

/// <summary>
/// Schema versioning of the persisted document (§3.8, D18): the serializer always writes
/// <see cref="EmailTemplateDocument.CurrentSchemaVersion"/>, a document without a version is version 1,
/// a document from a newer schema is refused with a localizable key, and the mapper keeps the version
/// consistent across the transport round-trip.
/// </summary>
public class SchemaVersionTests
{
    private static EmailTemplateDocument DocumentWithBlock()
    {
        var doc = new EmailTemplateDocument { Subject = "S" };
        doc.Sections.Add(new EmailSection
        {
            Columns = { new EmailColumn { Blocks = { new EmailTextBlock { Content = "hi" } } } },
        });
        return doc;
    }

    [Fact]
    public void CurrentSchemaVersion_IsOneAndNotAConstant()
    {
        // D18: a property (=> 1), not a const, so a future version can change it without a rebuild of
        // the consumers' IL.
        var property = typeof(EmailTemplateDocument).GetProperty(
            nameof(EmailTemplateDocument.CurrentSchemaVersion), BindingFlags.Public | BindingFlags.Static);

        property.Should().NotBeNull();
        property!.GetMethod!.IsStatic.Should().BeTrue();
        property.GetValue(null).Should().Be(1);
    }

    [Fact]
    public void NewDocument_StartsAtTheCurrentSchemaVersion()
        => new EmailTemplateDocument().SchemaVersion.Should().Be(EmailTemplateDocument.CurrentSchemaVersion);

    [Fact]
    public void Serialize_AlwaysWritesTheCurrentSchemaVersion()
    {
        var doc = DocumentWithBlock();
        doc.SchemaVersion = 7; // a document claiming a future version is still persisted as current

        var json = EmailTemplateSerializer.Serialize(doc);

        json.Should().Contain($"\"schemaVersion\":{EmailTemplateDocument.CurrentSchemaVersion}");
        json.Should().NotContain("\"schemaVersion\":7");
    }

    [Fact]
    public void Serialize_PreservesTheDocumentInstance()
    {
        var doc = DocumentWithBlock();
        doc.SchemaVersion = 7;

        _ = EmailTemplateSerializer.Serialize(doc);

        doc.SchemaVersion.Should().Be(7, "serialization must not mutate the caller's document");
    }

    [Fact]
    public void Deserialize_MissingSchemaVersion_IsCurrent()
    {
        // A document written before schema versioning existed.
        const string legacy = """
        {"subject":"S","sections":[]}
        """;

        var document = EmailTemplateSerializer.Deserialize(legacy);

        document.SchemaVersion.Should().Be(EmailTemplateDocument.CurrentSchemaVersion);
    }

    [Fact]
    public void Deserialize_CurrentVersion_RoundTrips()
    {
        var document = EmailTemplateSerializer.Deserialize(EmailTemplateSerializer.Serialize(DocumentWithBlock()));

        document.SchemaVersion.Should().Be(EmailTemplateDocument.CurrentSchemaVersion);
    }

    [Fact]
    public void Deserialize_NewerSchemaVersion_ThrowsWithALocalizableKey()
    {
        const string json = """
        {"schemaVersion":99,"subject":"S","sections":[]}
        """;

        var act = () => EmailTemplateSerializer.Deserialize(json);

        var exception = act.Should().Throw<EmailTemplateSerializationException>().Which;
        exception.Key.Should().Be(SerializationKeys.UnsupportedSchemaVersion);
        exception.Message.Should().Contain("99");
        exception.Message.Should().Contain(EmailTemplateDocument.CurrentSchemaVersion.ToString());
    }

    [Theory]
    [InlineData("""{"schemaVersion":0,"subject":"S","sections":[]}""")]
    [InlineData("""{"schemaVersion":-3,"subject":"S","sections":[]}""")]
    public void Deserialize_NonPositiveSchemaVersion_IsRefusedAsMalformed(string json)
    {
        // A version a document never carried is a broken header, not "version 1": guessing would hide a
        // corrupted payload.
        var act = () => EmailTemplateSerializer.Deserialize(json);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InvalidSchemaVersion);
    }

    [Theory]
    [InlineData("""{"schemaVersion":1,"schemaVersion":1,"subject":"S"}""")]
    [InlineData("""{"schemaVersion":1,"SCHEMAVERSION":2,"subject":"S"}""")]
    public void ReadSchemaVersion_DuplicateKey_IsRefusedAsMalformed(string json)
    {
        var act = () => EmailTemplateSerializer.ReadSchemaVersion(json);

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InvalidSchemaVersion);
    }

    [Fact]
    public void ReadSchemaVersion_MalformedJson_CarriesTheMalformedKey()
    {
        var act = () => EmailTemplateSerializer.ReadSchemaVersion("{ not json ");

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.Malformed);
    }

    [Theory]
    [InlineData("""{"subject":"S","sections":[]}""", 1)]
    [InlineData("""{"schemaVersion":1,"subject":"S"}""", 1)]
    [InlineData("""{"schemaVersion":3,"subject":"S"}""", 3)]
    [InlineData("""{"SchemaVersion":4,"subject":"S"}""", 4)]
    public void ReadSchemaVersion_ReadsTheHeaderWithoutDeserializingTheDocument(string json, int expected)
        => EmailTemplateSerializer.ReadSchemaVersion(json).Should().Be(expected);

    [Fact]
    public void Deserialize_MalformedJson_CarriesTheMalformedKey()
    {
        var act = () => EmailTemplateSerializer.Deserialize("{ not json ");

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.Malformed);
    }

    [Fact]
    public void ReadSchemaVersion_NonNumericVersion_ThrowsClearError()
    {
        var act = () => EmailTemplateSerializer.ReadSchemaVersion("""{"schemaVersion":"one"}""");

        act.Should().Throw<EmailTemplateSerializationException>();
    }

    // ── Mapper: the version survives the transport round-trip ───────────────────────────────────

    [Fact]
    public void Mapper_ContentJsonCarriesTheCurrentSchemaVersion()
    {
        var json = EmailTemplateMapper.ToContentJson(DocumentWithBlock());

        json.Should().Contain($"\"schemaVersion\":{EmailTemplateDocument.CurrentSchemaVersion}");
    }

    [Fact]
    public void Mapper_ReadsALegacyContentJsonAsTheCurrentSchemaVersion()
    {
        const string legacy = """
        {"subject":"S","sections":[{"columns":[{"blocks":[{"$type":"text","content":"hi"}]}]}]}
        """;

        var document = EmailTemplateMapper.ToDocument(legacy);

        document.SchemaVersion.Should().Be(EmailTemplateDocument.CurrentSchemaVersion);
        document.Subject.Should().Be("S");
    }

    [Fact]
    public void Mapper_CreateRequest_KeepsTheSchemaVersionOfTheCurrentModel()
    {
        var request = new CreateEmailTemplateRequest
        {
            Name = "N",
            Subject = "S",
            Language = "en",
            ContentJson = """{"schemaVersion":1,"subject":"S","sections":[]}""",
        };

        var document = EmailTemplateMapper.ApplyCreate(request);

        document.SchemaVersion.Should().Be(EmailTemplateDocument.CurrentSchemaVersion);
    }
}
