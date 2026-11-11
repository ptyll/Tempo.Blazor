using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using FluentAssertions;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Opt-in XSD validation of the exporter output (review finding 13): when the environment
/// variable <c>TM_BPMN_XSD_DIR</c> points at a directory holding the OMG BPMN 2.0.2 XSD files
/// (<c>BPMN20.xsd</c> plus the DC/DI/DI+MODEL schemas it imports), every corpus export golden is
/// validated against the schema. Skipped everywhere else — the XSDs are not redistributable, so
/// CI and default dev runs never set the variable.
/// </summary>
public sealed class BpmnXsdValidationTests
{
    private readonly XmlSchemaSet _schemas = new();

    public BpmnXsdValidationTests()
    {
        var directory = Environment.GetEnvironmentVariable("TM_BPMN_XSD_DIR");
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        _schemas.XmlResolver = new XmlUrlResolver();
        _schemas.Add(null, Path.Combine(directory, "BPMN20.xsd"));
        _schemas.Compile();
    }

    private static bool XsdAvailable => Environment.GetEnvironmentVariable("TM_BPMN_XSD_DIR") is { } directory
        && Directory.Exists(directory);

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in BpmnImportGoldenFixtures.All)
        {
            data.Add(fixture);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Exported_document_validates_against_the_bpmn_xsd(string fixture)
    {
        if (!XsdAvailable)
        {
            return; // opt-in gate: skipped when TM_BPMN_XSD_DIR is unset (see class doc comment)
        }

        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        var result = new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        result.Success.Should().BeTrue(result.ErrorMessage);

        var document = XDocument.Parse(result.Xml!);
        var errors = new List<ValidationEventArgs>();
        document.Validate(_schemas, (_, args) => errors.Add(args));

        errors.Should().BeEmpty($"the export of {fixture} must validate against the BPMN 2.0 XSD: " +
            string.Join("; ", errors.Select(args => $"{args.Severity}: {args.Message}")));
    }
}
