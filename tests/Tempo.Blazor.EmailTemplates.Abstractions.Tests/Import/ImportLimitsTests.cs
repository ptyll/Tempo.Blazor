using Tempo.Blazor.EmailTemplates.Abstractions.Import;
using Tempo.Blazor.EmailTemplates.Abstractions.Layout;
using Tempo.Blazor.EmailTemplates.Abstractions.Model;
using Tempo.Blazor.EmailTemplates.Abstractions.Model.Blocks;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;
using Tempo.Blazor.EmailTemplates.Abstractions.Serialization;

namespace Tempo.Blazor.EmailTemplates.Abstractions.Tests.Import;

/// <summary>
/// Import limits (§3.8, D13, B14): <see cref="EmailImportOptions"/> bounds the input size (UTF-8 byte
/// count), the number of sections and blocks and the block nesting depth. Exceeding a limit is a fatal
/// <see cref="ImportMessage"/> — never an exception — and the same limits are available as the
/// <see cref="EmailDocumentLimitsValidator"/> a host can run before saving.
/// </summary>
public class ImportLimitsTests
{
    private const string Small =
        "<mjml><mj-body><mj-section><mj-column><mj-text>hi</mj-text></mj-column></mj-section></mj-body></mjml>";

    private static string DocumentWithSections(int sections)
    {
        var builder = new System.Text.StringBuilder("<mjml><mj-body>");
        for (var i = 0; i < sections; i++)
        {
            builder.Append("<mj-section><mj-column><mj-text>s").Append(i).Append("</mj-text></mj-column></mj-section>");
        }

        return builder.Append("</mj-body></mjml>").ToString();
    }

    /// <summary>A valid document whose text payload is multi-byte in UTF-8 (2 bytes per character).</summary>
    private static string MultiBytePayload(int characters)
        => "<mjml><mj-body><mj-section><mj-column><mj-text>"
           + new string('ě', characters)
           + "</mj-text></mj-column></mj-section></mj-body></mjml>";

    /// <summary>
    /// <paramref name="groups"/> nested groups; the text block therefore sits at nesting depth
    /// <c>groups + 1</c> (a block directly in a section column is depth 1).
    /// </summary>
    private static string NestedGroups(int groups)
    {
        var builder = new System.Text.StringBuilder("<mjml><mj-body><mj-section><mj-column>");
        for (var i = 0; i < groups; i++)
        {
            builder.Append("<mj-group><mj-column>");
        }

        builder.Append("<mj-text>deep</mj-text>");
        for (var i = 0; i < groups; i++)
        {
            builder.Append("</mj-column></mj-group>");
        }

        return builder.Append("</mj-column></mj-section></mj-body></mjml>").ToString();
    }

    private static EmailTemplateDocument TextDocument(params string[] contents)
    {
        var column = new EmailColumn();
        foreach (var content in contents)
        {
            column.Blocks.Add(new EmailTextBlock { Content = content });
        }

        var document = new EmailTemplateDocument();
        document.Sections.Add(new EmailSection { Columns = { column } });
        return document;
    }

    private static EmailTemplateDocument SectionsDocument(int sections)
    {
        var document = new EmailTemplateDocument();
        for (var i = 0; i < sections; i++)
        {
            document.Sections.Add(new EmailSection { Columns = { new EmailColumn() } });
        }

        return document;
    }

    /// <summary>
    /// A table (with rich extra attributes) whose block nesting depth is exactly
    /// <paramref name="leafDepth"/> — <c>leafDepth - 1</c> nested <c>mj-wrapper</c>-shaped containers
    /// around it, the most JSON-expensive legal document shape.
    /// </summary>
    private static EmailTemplateDocument WrapperDocument(int leafDepth)
    {
        var depth = leafDepth - 1;
        var table = new EmailTableBlock();
        table.MjClasses.Add("wide");
        table.ExtraAttributes["data-test"] = "1";
        var row = new EmailTableRow();
        row.Cells.Add(new EmailTableCell { Text = "A", ColSpan = 2 });
        table.Rows.Add(row);

        EmailBlockBase block = table;
        for (var i = 0; i < depth; i++)
        {
            var section = new EmailSection { Columns = { new EmailColumn { Blocks = { block } } } };
            var wrapper = new EmailWrapperBlock { MjClasses = { "outer" } };
            wrapper.Sections.Add(section);
            block = wrapper;
        }

        var document = new EmailTemplateDocument { Subject = "S" };
        document.Sections.Add(new EmailSection { Columns = { new EmailColumn { Blocks = { block } } } });
        return document;
    }

    private static EmailTemplateDocument NestedGroupDocument(int groups, bool withTable = false)
    {
        EmailBlockBase block;
        if (withTable)
        {
            var table = new EmailTableBlock();
            var row = new EmailTableRow();
            row.Cells.Add(new EmailTableCell { Text = "A", ColSpan = 2 });
            table.Rows.Add(row);
            block = table;
        }
        else
        {
            block = new EmailTextBlock { Content = "deep" };
        }

        for (var i = 0; i < groups; i++)
        {
            var group = new EmailGroupBlock();
            group.Columns.Add(new EmailColumn { Blocks = { block } });
            block = group;
        }

        var document = new EmailTemplateDocument();
        document.Sections.Add(new EmailSection { Columns = { new EmailColumn { Blocks = { block } } } });
        return document;
    }

    // ── Defaults ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EmailImportOptions_DefaultsMatchTodaysBehaviour()
    {
        var options = new EmailImportOptions();

        // B14/D13: 5 MB is the historical hard-coded limit — tightening it would be a breaking change.
        options.MaxInputBytes.Should().Be(5 * 1024 * 1024);
        options.MaxSections.Should().Be(200);
        options.MaxBlocks.Should().Be(2000);
        options.MaxNestingDepth.Should().Be(8);
    }

    // ── MjmlImporter ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Import_WithoutOptions_KeepsTheLegacyBehaviour()
    {
        var result = new MjmlImporter().Import(Small);

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
    }

    [Fact]
    public void Import_InputAboveMaxInputBytes_FailsWithALocalizableKey_AndNoException()
    {
        // 100 characters, but 200 UTF-8 bytes: the limit is measured in bytes, not in characters.
        var payload = MultiBytePayload(100);
        var options = new EmailImportOptions { MaxInputBytes = 150 };

        ImportResult? result = null;
        var act = () => result = new MjmlImporter().Import(payload, includeResolver: null, options: options);

        act.Should().NotThrow();
        result!.Document.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Key.Should().Be(ImportKeys.InputTooLarge);
    }

    [Fact]
    public void Import_MultiByteInputWithinMaxInputBytes_Succeeds()
    {
        var result = new MjmlImporter().Import(MultiBytePayload(100), null, new EmailImportOptions { MaxInputBytes = 400 });

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
    }

    [Fact]
    public void Import_TooManySections_FailsWithALocalizableKey()
    {
        var result = new MjmlImporter().Import(
            DocumentWithSections(3), null, new EmailImportOptions { MaxSections = 2 });

        result.Document.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Key.Should().Be(DocumentValidationKeys.TooManySections);
    }

    [Fact]
    public void Import_TooManyBlocks_FailsWithALocalizableKey()
    {
        var mjml = "<mjml><mj-body><mj-section><mj-column><mj-text>a</mj-text><mj-text>b</mj-text>" +
                   "</mj-column></mj-section></mj-body></mjml>";

        var result = new MjmlImporter().Import(mjml, null, new EmailImportOptions { MaxBlocks = 1 });

        result.Document.Should().BeNull();
        result.Errors.Should().ContainSingle().Which.Key.Should().Be(DocumentValidationKeys.TooManyBlocks);
    }

    [Fact]
    public void Import_TooDeeplyNested_FailsWithALocalizableKey()
    {
        // Three nested groups put the text block at nesting depth 4.
        var result = new MjmlImporter().Import(
            NestedGroups(3), null, new EmailImportOptions { MaxNestingDepth = 3 });

        result.Document.Should().BeNull();
        result.Errors.Select(error => error.Key).Should().Contain(DocumentValidationKeys.TooDeeplyNested);
    }

    [Fact]
    public void Import_AtTheNestingLimit_Succeeds()
    {
        var result = new MjmlImporter().Import(
            NestedGroups(3), null, new EmailImportOptions { MaxNestingDepth = 4 });

        result.Errors.Should().BeEmpty();
        result.Document.Should().NotBeNull();
    }

    // ── JSON deserialization ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_JsonDeeperThanTheConfiguredBound_Throws()
    {
        var json = "{\"subject\":\"S\",\"future\":" + new string('[', 40) + new string(']', 40) + "}";

        // MaxNestingDepth = 2 → the derived JSON bound is 14 + 6 * 2 = 26, well below the 42 levels here.
        var act = () => EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxNestingDepth = 2 });

        act.Should().Throw<EmailTemplateSerializationException>();
    }

    [Fact]
    public void Deserialize_WithAGenerousMaxNestingDepth_AcceptsTheSameJson()
    {
        var json = "{\"subject\":\"S\",\"future\":" + new string('[', 60) + new string(']', 60) + "}";

        var document = EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxNestingDepth = 100 });

        document.Subject.Should().Be("S");
    }

    [Fact]
    public void Deserialize_AtTheModelNestingLimit_StillRoundTrips()
    {
        // The JSON depth of a document is several levels per block nesting level (sections → columns →
        // blocks …; a table adds rows → cells), so the JSON parser bound must never reject a document
        // that is within the model limit: 7 container levels put the leaf block at depth 8 = the default
        // limit, and a table leaf is the deepest shape the model allows (measured 40 JSON levels).
        var document = NestedGroupDocument(7, withTable: true);

        var json = EmailTemplateSerializer.Serialize(document);
        var back = EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxNestingDepth = 8 });

        back.Sections.Should().NotBeEmpty();
        back.Sections[0].Columns[0].Blocks.Should().ContainSingle();
    }

    [Fact]
    public void Deserialize_FlatTableDocument_FitsTheSmallestConfiguredBound()
    {
        // A flat document with a table already measures 12 JSON levels, so a tight MaxNestingDepth must
        // not make the parser bound reject ordinary content.
        var document = NestedGroupDocument(0, withTable: true);

        var back = EmailTemplateSerializer.Deserialize(
            EmailTemplateSerializer.Serialize(document), new EmailImportOptions { MaxNestingDepth = 1 });

        back.Sections.Should().NotBeEmpty();
    }

    // ── JSON depth: derived bound, never below the STJ default ──────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    [InlineData(20)]
    public void Deserialize_WrapperNestingAtTheModelLimit_RoundTripsThroughBothOverloads(int leafDepth)
    {
        var document = WrapperDocument(leafDepth);
        var options = new EmailImportOptions { MaxNestingDepth = leafDepth };

        // Invariant: a document that passes the limits validator always deserializes.
        new EmailDocumentLimitsValidator(options).Validate(document).Should().BeEmpty();

        var json = EmailTemplateSerializer.Serialize(document);

        EmailTemplateSerializer.Deserialize(json).Sections.Should().NotBeEmpty();
        EmailTemplateSerializer.Deserialize(json, options).Sections.Should().NotBeEmpty();
        EmailTemplateSerializer.ReadSchemaVersion(json).Should().Be(EmailTemplateDocument.CurrentSchemaVersion);
    }

    [Fact]
    public void Deserialize_WithoutOptions_KeepsTheStjDefaultDepth()
    {
        // A plain Deserialize must not silently become stricter than System.Text.Json's default of 64.
        var json = "{\"subject\":\"S\",\"future\":" + new string('[', 60) + new string(']', 60) + "}";

        var document = EmailTemplateSerializer.Deserialize(json);

        document.Subject.Should().Be("S");
    }

    // ── Deserialize(json, options) enforces the import limits too ──────────────────────────────

    [Fact]
    public void Deserialize_InputAboveMaxInputBytes_ThrowsWithALocalizableKey()
    {
        var json = EmailTemplateSerializer.Serialize(NestedGroupDocument(0));

        var act = () => EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxInputBytes = 32 });

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(SerializationKeys.InputTooLarge);
    }

    [Fact]
    public void Deserialize_TooManySections_ThrowsWithTheValidatorKey()
    {
        var json = EmailTemplateSerializer.Serialize(SectionsDocument(2));

        var act = () => EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxSections = 1 });

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(DocumentValidationKeys.TooManySections);
    }

    [Fact]
    public void Deserialize_TooManyBlocks_ThrowsWithTheValidatorKey()
    {
        var json = EmailTemplateSerializer.Serialize(TextDocument("a", "b"));

        var act = () => EmailTemplateSerializer.Deserialize(json, new EmailImportOptions { MaxBlocks = 1 });

        act.Should().Throw<EmailTemplateSerializationException>()
            .Which.Key.Should().Be(DocumentValidationKeys.TooManyBlocks);
    }

    // ── The reusable server-side validator ──────────────────────────────────────────────────────

    [Fact]
    public void LimitsValidator_ValidDocument_HasNoFindings()
        => new EmailDocumentLimitsValidator().Validate(TextDocument("hi")).Should().BeEmpty();

    [Fact]
    public void LimitsValidator_TooManySections_ReportsTheKey()
    {
        var findings = new EmailDocumentLimitsValidator(new EmailImportOptions { MaxSections = 1 })
            .Validate(SectionsDocument(2));

        var finding = findings.Should().ContainSingle().Subject;
        finding.Key.Should().Be(DocumentValidationKeys.TooManySections);
        finding.Severity.Should().Be(LayoutSeverity.Error);
    }

    [Fact]
    public void LimitsValidator_TooManyBlocks_ReportsTheKey()
    {
        var findings = new EmailDocumentLimitsValidator(new EmailImportOptions { MaxBlocks = 1 })
            .Validate(TextDocument("a", "b"));

        findings.Should().ContainSingle().Which.Key.Should().Be(DocumentValidationKeys.TooManyBlocks);
    }

    [Fact]
    public void LimitsValidator_TooDeeplyNested_ReportsTheKeyAndTheOffendingBlock()
    {
        var findings = new EmailDocumentLimitsValidator(new EmailImportOptions { MaxNestingDepth = 2 })
            .Validate(NestedGroupDocument(2));

        var finding = findings.Should().ContainSingle().Subject;
        finding.Key.Should().Be(DocumentValidationKeys.TooDeeplyNested);
        finding.Path.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void LimitsValidator_DefaultsComposeTheDocumentLimits()
    {
        var defaults = new EmailImportOptions();
        var validator = new EmailDocumentLimitsValidator();

        validator.Options.MaxSections.Should().Be(defaults.MaxSections);
        validator.Options.MaxBlocks.Should().Be(defaults.MaxBlocks);
        validator.Options.MaxNestingDepth.Should().Be(defaults.MaxNestingDepth);
    }
}
