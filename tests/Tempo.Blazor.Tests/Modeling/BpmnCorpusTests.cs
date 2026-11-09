using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Components.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Guards the hand-authored BPMN 2.0 test corpus used by the headless BPMN XML converter
/// (plan 6fca9139-c871-41c4-c06e-08df38c30cbd, phase 1). Every fixture must be well-formed,
/// safely parseable (DTD prohibited, no resolver), namespace-correct and internally
/// consistent, so later phases can rely on the corpus for golden importer/exporter tests.
/// </summary>
public sealed class BpmnCorpusTests
{
    private const string BpmnModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private const string BpmnDiNamespace = "http://www.omg.org/spec/BPMN/20100524/DI";

    private static readonly string[] ExpectedCorpusFiles =
    [
        "simple-process.bpmn",
        "collaboration-3-pools.bpmn",
        "nested-lanes.bpmn",
        "all-gateways.bpmn",
        "all-events.bpmn",
        "boundary-events.bpmn",
        "event-subprocess.bpmn",
        "call-activity.bpmn",
        "data-objects.bpmn",
        "no-bpmndi.bpmn",
        "camunda-export.bpmn",
        "signavio-export.bpmn",
        "bizagi-export.bpmn",
        "annotations-group.bpmn",
        "text-annotation-text.bpmn",
        "unsupported-constructs.bpmn",
        "prefixed-qnames.bpmn",
        "nested-subprocess.bpmn",
        "collapsed-subprocess.bpmn",
        "black-box-pool.bpmn",
        "multiple-events.bpmn",
        "io-properties.bpmn",
    ];

    /// <summary>The converter's own semantic-type constants (Tempo.Blazor.Modeling.BpmnXml,
    /// phase 2) must stay in parity with the internal <see cref="BpmnModelingCatalog"/> —
    /// the converter duplicates the names on purpose (no dependency on the internal type).</summary>
    private static readonly string[] ExpectedConverterElementTypes =
    [
        "Task", "UserTask", "ServiceTask", "SendTask", "ReceiveTask", "ScriptTask",
        "BusinessRuleTask", "ManualTask", "SubProcess", "EventSubProcess", "CallActivity",
        "StartEvent", "EndEvent", "IntermediateCatchEvent", "IntermediateThrowEvent",
        "BoundaryEvent", "ExclusiveGateway", "ParallelGateway", "InclusiveGateway",
        "ComplexGateway", "EventBasedGateway", "Pool", "Lane", "DataObject", "DataStore",
        "Group", "TextAnnotation",
    ];

    private static readonly string[] ExpectedConverterRelationshipTypes =
    [
        "SequenceFlow", "ConditionalFlow", "DefaultFlow", "MessageFlow", "Association",
        "DataInputAssociation", "DataOutputAssociation",
    ];

    private static string CorpusDirectory
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                current = current.Parent;
            }

            current.Should().NotBeNull("TempoBlazor.slnx must be findable above the test output directory");
            return Path.Combine(current!.FullName, "tests", "Tempo.Blazor.Tests", "Modeling", "BpmnCorpus");
        }
    }

    [Fact]
    public void Converter_semantic_type_constants_match_BpmnModelingCatalog()
    {
        // Parity guard (InternalsVisibleTo Tempo.Blazor.Tests): the converter package
        // (phase 2) duplicates these names as its own constants; the catalog must not drift.
        ExpectedConverterElementTypes.Should().BeEquivalentTo(BpmnModelingCatalog.ElementTypes);
        ExpectedConverterRelationshipTypes.Should().BeEquivalentTo(BpmnModelingCatalog.RelationshipTypes);
    }

    [Fact]
    public void Corpus_contains_at_least_the_twelve_required_fixtures()
    {
        var files = Directory.GetFiles(CorpusDirectory, "*.bpmn").Select(Path.GetFileName).ToList();

        files.Should().HaveCountGreaterThanOrEqualTo(12);
        files.Should().Contain(ExpectedCorpusFiles);
    }

    [Fact]
    public void Readme_documents_licensing_and_lists_every_fixture()
    {
        var readme = File.ReadAllText(Path.Combine(CorpusDirectory, "README.md"));

        readme.Should().Contain("hand-authored", "the corpus must be license-clean");
        readme.Should().Contain("MIT");
        foreach (var file in ExpectedCorpusFiles)
        {
            readme.Should().Contain(file, $"README must document {file}");
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_fixture_parses_with_safe_xml_settings(string fileName)
    {
        var xml = ReadFixture(fileName);

        xml.Should().NotContain("<!DOCTYPE", "fixtures must never carry a DTD");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 10 * 1024 * 1024,
            IgnoreWhitespace = false,
        };

        XDocument document;
        using (var stringReader = new StringReader(xml))
        using (var reader = XmlReader.Create(stringReader, settings))
        {
            document = XDocument.Load(reader);
        }

        document.Root.Should().NotBeNull();
        document.Root!.Name.LocalName.Should().Be("definitions");
        document.Root.Name.NamespaceName.Should().Be(BpmnModelNamespace);
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_fixture_uses_valid_unique_ncname_identifiers(string fileName)
    {
        var document = LoadFixture(fileName);

        var ids = document.Root!.DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "id")
            .Select(attribute => attribute.Value)
            .ToList();

        ids.Should().OnlyHaveUniqueItems($"fixture {fileName} must not reuse identifiers");
        ids.Should().OnlyContain(id => !string.IsNullOrWhiteSpace(id), "empty id attributes are not allowed");
        foreach (var id in ids)
        {
            System.Xml.XmlConvert.VerifyName(id);
        }
    }

    [Theory]
    [MemberData(nameof(CorpusFiles))]
    public void Every_reference_resolves_to_a_declared_element(string fileName)
    {
        var document = LoadFixture(fileName);
        var declaredIds = document.Root!.DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => attribute.Name.LocalName == "id")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);

        // Structural references every importer follows. Namespace-agnostic by design
        // (the converter matches local names, like RdlReportImporter).
        string[] referenceAttributes =
        [
            "sourceRef", "targetRef", "attachedToRef", "default", "processRef",
            "calledElement", "dataObjectRef", "dataStoreRef", "bpmnElement", "messageRef",
            "signalRef", "errorRef", "escalationRef", "categoryValueRef", "calledCollaborationRef",
            "calledChoreographyRef", "initiatingParticipantRef", "activityRef",
        ];

        var unresolved = document.Root!.DescendantsAndSelf()
            .SelectMany(element => element.Attributes())
            .Where(attribute => referenceAttributes.Contains(attribute.Name.LocalName, StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(attribute.Value))
            .Where(attribute => attribute.Value != "##WebService" && attribute.Value != "##unspecified")
            .Select(attribute => (Value: StripQNamePrefix(attribute.Value), Path: BuildPath(attribute.Parent)))
            .Where(reference => !declaredIds.Contains(reference.Value))
            .ToList();

        // Element-body references (lane membership, flow wiring, data/property targets,
        // choreography participants, group category). Same namespace-agnostic local-name
        // match; QName prefixes are stripped (prefixed-dialect fixture uses tns: refs).
        string[] referenceElements =
        [
            "sourceRef", "targetRef", "flowNodeRef", "incoming", "outgoing",
            "participantRef", "categoryValueRef",
        ];

        var unresolvedElements = document.Root!.Descendants()
            .Where(element => referenceElements.Contains(element.Name.LocalName, StringComparer.Ordinal)
                && !string.IsNullOrWhiteSpace(element.Value))
            .Select(element => (Value: StripQNamePrefix(element.Value.Trim()), Path: BuildPath(element.Parent)))
            .Where(reference => !declaredIds.Contains(reference.Value))
            .ToList();

        unresolved.Should().BeEmpty($"fixture {fileName} has dangling references: " +
            string.Join(", ", unresolved.Select(reference => $"{reference.Value} at {reference.Path}")));
        unresolvedElements.Should().BeEmpty($"fixture {fileName} has dangling element references: " +
            string.Join(", ", unresolvedElements.Select(reference => $"{reference.Value} at {reference.Path}")));
    }

    private static string StripQNamePrefix(string value)
    {
        var colonIndex = value.LastIndexOf(':');
        return colonIndex >= 0 ? value[(colonIndex + 1)..] : value;
    }

    [Fact]
    public void All_fixtures_except_no_bpmndi_carry_a_bpmndi_diagram()
    {
        foreach (var fileName in ExpectedCorpusFiles)
        {
            var document = LoadFixture(fileName);
            var hasDi = document.Descendants()
                .Any(element => element.Name.NamespaceName == BpmnDiNamespace
                    && element.Name.LocalName == "BPMNDiagram");

            if (fileName == "no-bpmndi.bpmn")
            {
                hasDi.Should().BeFalse("the no-bpmndi fixture must stay BPMNDI-free (auto-layout input)");
            }
            else
            {
                hasDi.Should().BeTrue($"{fileName} must carry BPMNDI (its shapes/edges are layout golden data)");
            }
        }
    }

    [Fact]
    public void Bpmndi_shapes_and_edges_reference_existing_semantic_elements()
    {
        foreach (var fileName in ExpectedCorpusFiles.Where(name => name != "no-bpmndi.bpmn"))
        {
            var document = LoadFixture(fileName);
            var declaredIds = document.Root!.DescendantsAndSelf()
                .SelectMany(element => element.Attributes())
                .Where(attribute => attribute.Name.LocalName == "id")
                .Select(attribute => attribute.Value)
                .ToHashSet(StringComparer.Ordinal);

            var missing = document.Descendants()
                .Where(element => element.Name.NamespaceName == BpmnDiNamespace
                    && (element.Name.LocalName == "BPMNShape" || element.Name.LocalName == "BPMNEdge"))
                .Select(element => element.Attribute("bpmnElement")?.Value ?? string.Empty)
                .Where(value => !declaredIds.Contains(value))
                .ToList();

            missing.Should().BeEmpty($"{fileName}: BPMNDI references undeclared elements: {string.Join(", ", missing)}");
        }
    }

    public static TheoryData<string> CorpusFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in ExpectedCorpusFiles)
        {
            data.Add(file);
        }

        return data;
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(CorpusDirectory, fileName));

    private static XDocument LoadFixture(string fileName)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 10 * 1024 * 1024,
        };

        using var stringReader = new StringReader(ReadFixture(fileName));
        using var reader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(reader);
    }

    private static string BuildPath(XElement? element)
    {
        var parts = new List<string>();
        while (element is not null)
        {
            parts.Add(element.Name.LocalName);
            element = element.Parent;
        }

        parts.Reverse();
        return "/" + string.Join("/", parts);
    }
}
