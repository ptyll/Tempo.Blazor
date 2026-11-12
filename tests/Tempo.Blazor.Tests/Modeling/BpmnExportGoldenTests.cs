using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// XML-level golden tests for the exporter: import(corpus) → export(models) must match the
/// committed <c>*.export.bpmn</c> files byte-for-byte (LF) — the converter is deterministic.
/// Regenerate intentionally with <c>TEMPO_REGENERATE_BPMN_EXPORT_GOLDEN=1</c>.
/// </summary>
public sealed class BpmnExportGoldenTests
{
    private static readonly string[] GoldenFixtures = BpmnImportGoldenFixtures.All;

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in GoldenFixtures)
        {
            data.Add(fixture);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Export_matches_the_committed_golden_file(string fixture)
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        imported.HasErrors.Should().BeFalse(imported.ErrorMessage ?? $"import of {fixture} failed");

        var result = new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        result.Success.Should().BeTrue(result.ErrorMessage);

        var goldenPath = Path.Combine(BpmnCorpusPaths.ExpectedDirectory,
            fixture.Replace(".bpmn", ".export.bpmn", StringComparison.Ordinal));
        // exporterVersion carries the assembly's SemVer (incl. a CI prerelease stamp); the goldens
        // pin a placeholder so a -p:Version stamp cannot churn them.
        var actual = NormalizeExporterVersion(result.Xml!.ReplaceLineEndings("\n"));

        if (Environment.GetEnvironmentVariable("TEMPO_REGENERATE_BPMN_EXPORT_GOLDEN") == "1")
        {
            File.WriteAllText(goldenPath, actual);
        }

        File.Exists(goldenPath).Should().BeTrue(
            $"golden file '{Path.GetFileName(goldenPath)}' must be committed — regenerate with TEMPO_REGENERATE_BPMN_EXPORT_GOLDEN=1");
        File.ReadAllText(goldenPath).ReplaceLineEndings("\n").Should().Be(actual,
            $"the export of {fixture} must be deterministic; regenerate the golden intentionally with TEMPO_REGENERATE_BPMN_EXPORT_GOLDEN=1");
    }

    private static string NormalizeExporterVersion(string xml) =>
        System.Text.RegularExpressions.Regex.Replace(
            xml, "exporterVersion=\"[^\"]*\"", "exporterVersion=\"{version}\"");

    [Fact]
    public void Export_goldens_exist_for_every_corpus_fixture()
    {
        foreach (var fixture in GoldenFixtures)
        {
            File.Exists(Path.Combine(BpmnCorpusPaths.ExpectedDirectory,
                    fixture.Replace(".bpmn", ".export.bpmn", StringComparison.Ordinal)))
                .Should().BeTrue($"missing export golden for {fixture}");
        }
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Export_is_structurally_valid_and_every_reference_resolves(string fixture)
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        var result = new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        result.Success.Should().BeTrue(result.ErrorMessage);

        // Well-formed through a safe reader (DTD prohibited, no resolver — the importer's rules).
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        XDocument document;
        using (var reader = XmlReader.Create(new StringReader(result.Xml!), settings))
        {
            document = XDocument.Load(reader);
        }

        var root = document.Root!;
        root.Name.Should().Be(XName.Get("definitions", BpmnConstants.ModelNamespace),
            "the exported root must be a BPMN definitions element in the MODEL namespace");
        foreach (var requiredNamespace in new[]
                 {
                     BpmnConstants.DiagramNamespace, BpmnConstants.DCNamespace, BpmnConstants.DINamespace,
                 })
        {
            root.Descendants().SelectMany(element => element.Attributes().Select(attribute => attribute.Name.NamespaceName))
                .Concat(root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => attribute.Value))
                .Should().Contain(requiredNamespace);
        }

        // XML-level ids are unique across the document.
        var ids = root.DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "id")
            .Select(attribute => attribute.Value)
            .ToList();
        ids.Should().OnlyHaveUniqueItems("exported XML ids must be unique");

        // Every DI bpmnElement reference resolves; every flow endpoint resolves.
        var elementIds = new HashSet<string>(
            root.DescendantsAndSelf().Select(element => element.Attribute("id")?.Value).Where(id => id is not null)!,
            StringComparer.Ordinal);
        foreach (var diElement in root.Descendants()
                     .Where(element => element.Name.NamespaceName == BpmnConstants.DiagramNamespace
                         && element.Name.LocalName is "BPMNShape" or "BPMNEdge"))
        {
            var reference = diElement.Attribute("bpmnElement")?.Value;
            reference.Should().NotBeNullOrEmpty($"{diElement.Name.LocalName} needs a bpmnElement reference");
            elementIds.Should().Contain(reference, $"{diElement.Name.LocalName} references an element present in the document");
        }

        foreach (var flow in root.Descendants()
                     .Where(element => element.Name.LocalName is "sequenceFlow" or "messageFlow"))
        {
            elementIds.Should().Contain(flow.Attribute("sourceRef")?.Value);
            elementIds.Should().Contain(flow.Attribute("targetRef")?.Value);
        }

        // lane flowNodeRef targets resolve.
        foreach (var flowNodeRef in root.Descendants(XName.Get("flowNodeRef", BpmnConstants.ModelNamespace)))
        {
            elementIds.Should().Contain(flowNodeRef.Value);
        }

        // Every reference attribute the exporter can emit resolves to a declared id
        // (finding 4: dataObjectRef, dataStoreRef, messageRef, signalRef, errorRef,
        // escalationRef, attachedToRef, default, categoryValueRef, processRef, activityRef).
        string[] referenceAttributes =
        [
            "dataObjectRef", "dataStoreRef", "messageRef", "signalRef", "errorRef",
            "escalationRef", "attachedToRef", "default", "categoryValueRef", "processRef",
            "activityRef",
        ];

        foreach (var attribute in root.DescendantsAndSelf()
                     .SelectMany(element => element.Attributes())
                     .Where(attribute => referenceAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal)
                         && !string.IsNullOrWhiteSpace(attribute.Value)))
        {
            var target = attribute.Value;
            var colon = target.LastIndexOf(':');
            if (colon >= 0)
            {
                target = target[(colon + 1)..];
            }

            elementIds.Should().Contain(target,
                $"{attribute.Name.LocalName}='{attribute.Value}' on {attribute.Parent!.Name.LocalName} must resolve");
        }

        // Flow nodes carry Camunda-style incoming/outgoing wiring that matches the declared flows.
        foreach (var flowNode in root.Descendants()
                     .Where(element => element.Name.NamespaceName == BpmnConstants.ModelNamespace
                         && element.Name.LocalName is "task" or "userTask" or "serviceTask" or "sendTask"
                             or "receiveTask" or "scriptTask" or "businessRuleTask" or "manualTask"
                             or "callActivity" or "startEvent" or "endEvent" or "intermediateCatchEvent"
                             or "intermediateThrowEvent" or "boundaryEvent" or "exclusiveGateway"
                             or "parallelGateway" or "inclusiveGateway" or "complexGateway" or "eventBasedGateway"))
        {
            var nodeId = flowNode.Attribute("id")!.Value;
            var incoming = flowNode.Elements(XName.Get("incoming", BpmnConstants.ModelNamespace)).Select(element => element.Value).ToList();
            var outgoing = flowNode.Elements(XName.Get("outgoing", BpmnConstants.ModelNamespace)).Select(element => element.Value).ToList();
            incoming.Should().OnlyHaveUniqueItems($"{nodeId} must not repeat an incoming flow");
            outgoing.Should().OnlyHaveUniqueItems($"{nodeId} must not repeat an outgoing flow");
            foreach (var flowId in incoming.Concat(outgoing))
            {
                elementIds.Should().Contain(flowId, $"{nodeId} wiring references a declared flow");
                var flow = root.Descendants(XName.Get("sequenceFlow", BpmnConstants.ModelNamespace))
                    .FirstOrDefault(element => element.Attribute("id")?.Value == flowId);
                flow.Should().NotBeNull($"{flowId} referenced from {nodeId} wiring must be a sequenceFlow");
            }
        }
    }
}
