using System.Xml.Linq;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Semantics of the BPMN 2.0 XML exporter: document structure, profiles, extension write-back,
/// determinism and the export loss report (docs/modeling-bpmn-xml.md §5).
/// </summary>
public sealed class BpmnXmlExporterTests
{
    private static BpmnExportResult Export(ModelingModelDto model, BpmnExportOptions? options = null) =>
        new BpmnXmlExporter().Export(model, options);

    private static XElement Parse(BpmnExportResult result)
    {
        result.Success.Should().BeTrue(result.ErrorMessage);
        return XElement.Parse(result.Xml!);
    }

    private static ModelingModelDto SimpleModel()
    {
        var model = new ModelingModelDto { Id = "Process_1", Title = "Handle", Notation = "bpmn2" };
        model.Elements.AddRange(
        [
            new ModelingElementDto { Id = "Start_1", SemanticType = BpmnSemanticTypes.StartEvent, Name = "Begin" },
            new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.UserTask, Name = "Do <it> & \"more\"" },
            new ModelingElementDto { Id = "End_1", SemanticType = BpmnSemanticTypes.EndEvent },
        ]);
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_1",
            SourceElementId = "Start_1",
            TargetElementId = "Task_1",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_2",
            SourceElementId = "Task_1",
            TargetElementId = "End_1",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });
        return model;
    }

    [Fact]
    public void A_single_pool_less_model_exports_one_process()
    {
        var root = Parse(Export(SimpleModel()));

        root.Name.Should().Be(XName.Get("definitions", BpmnConstants.ModelNamespace));
        root.Attribute("targetNamespace")!.Value.Should().Be("http://tempo.blazor/bpmn");
        root.Elements(XName.Get("process", BpmnConstants.ModelNamespace)).Should().ContainSingle();
        root.Elements(XName.Get("collaboration", BpmnConstants.ModelNamespace)).Should().BeEmpty();
    }

    [Fact]
    public void Reuses_the_imported_target_namespace_from_metadata()
    {
        var model = SimpleModel();
        model.Metadata.SourceVersion = "http://tempo.example/bpmn/corpus";

        Parse(Export(model)).Attribute("targetNamespace")!.Value.Should().Be("http://tempo.example/bpmn/corpus");
    }

    [Fact]
    public void TargetNamespace_option_wins_over_metadata()
    {
        var model = SimpleModel();
        model.Metadata.SourceVersion = "http://tempo.example/bpmn/corpus";

        var root = Parse(Export(model, new BpmnExportOptions { TargetNamespace = "http://example.com/custom" }));
        root.Attribute("targetNamespace")!.Value.Should().Be("http://example.com/custom");
    }

    [Fact]
    public void Flow_elements_keep_ids_names_and_order()
    {
        var process = Parse(Export(SimpleModel()))
            .Element(XName.Get("process", BpmnConstants.ModelNamespace))!;

        process.Attribute("id")!.Value.Should().Be("Process_1");
        process.Attribute("name")!.Value.Should().Be("Handle");
        process.Elements()
            .Select(element => element.Name.LocalName)
            .Should().Equal("startEvent", "userTask", "endEvent", "sequenceFlow", "sequenceFlow");
        process.Elements().Select(element => element.Attribute("id")?.Value)
            .Should().Equal("Start_1", "Task_1", "End_1", "Flow_1", "Flow_2");
    }

    [Fact]
    public void Names_with_markup_characters_are_escaped_and_never_injected()
    {
        var task = Parse(Export(SimpleModel()))
            .Descendants(XName.Get("userTask", BpmnConstants.ModelNamespace)).Single();

        task.Attribute("name")!.Value.Should().Be("Do <it> & \"more\"");
        var result = Export(SimpleModel());
        result.Xml.Should().NotContain("<it>", "attribute values must be XML-encoded, never raw");
        result.Xml.Should().NotContain("&amp;amp;#", "no double encoding");
    }

    [Fact]
    public void Documentation_is_written_from_the_description_in_both_profiles()
    {
        var model = SimpleModel();
        model.Elements[1].Description = "Explain <the> task";

        foreach (var profile in new[] { BpmnExportProfile.Standard, BpmnExportProfile.WithHostExtensions })
        {
            var task = Parse(Export(model, new BpmnExportOptions { Profile = profile }))
                .Descendants(XName.Get("userTask", BpmnConstants.ModelNamespace)).Single();
            task.Element(XName.Get("documentation", BpmnConstants.ModelNamespace))!.Value.Should().Be("Explain <the> task");
        }
    }

    [Fact]
    public void Standard_profile_reports_unmappable_properties_as_losses_and_writes_nothing_extra()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["customFlag"] = System.Text.Json.JsonSerializer.SerializeToElement(42);

        var result = Export(model);

        result.Success.Should().BeTrue();
        result.Losses.Should().ContainSingle(loss =>
            loss.ElementId == "Task_1" && loss.Construct == "Property" && loss.Severity == BpmnExportLossSeverity.Loss);
        result.Xml.Should().NotContain("customFlag");
    }

    [Fact]
    public void WithHostExtensions_serializes_properties_as_canonical_JSON_in_the_host_namespace()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["customFlag"] = System.Text.Json.JsonSerializer.SerializeToElement(42);
        model.Elements[1].Properties["zeta"] = System.Text.Json.JsonSerializer.SerializeToElement("last");
        model.Elements[1].Properties["alpha"] = System.Text.Json.JsonSerializer.SerializeToElement("first");

        var root = Parse(Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));
        var task = root.Descendants(XName.Get("userTask", BpmnConstants.ModelNamespace)).Single();
        var extension = task.Element(XName.Get("extensionElements", BpmnConstants.ModelNamespace))!;
        var hostNs = "https://tempo.blazor/schemas/bpmn-extensions";
        var properties = extension.Element(XName.Get("properties", hostNs))!;
        properties.Elements(XName.Get("property", hostNs))
            .Select(element => (element.Attribute("name")!.Value, element.Attribute("value")!.Value))
            .Should().Equal(
                new[] { ("alpha", "\"first\""), ("customFlag", "42"), ("zeta", "\"last\"") },
                "property payloads are canonical: compact JSON with sorted keys");
    }

    [Fact]
    public void Host_extension_namespace_and_prefix_are_configurable()
    {
        var model = SimpleModel();
        model.Elements[0].Properties["flag"] = System.Text.Json.JsonSerializer.SerializeToElement(true);

        var options = new BpmnExportOptions
        {
            Profile = BpmnExportProfile.WithHostExtensions,
            HostExtensionNamespace = "https://example.com/ext",
            HostExtensionPrefix = "acme",
        };
        var root = Parse(Export(model, options));

        root.Attribute(XNamespace.Xmlns + "acme")!.Value.Should().Be("https://example.com/ext");
        root.Descendants(XName.Get("properties", "https://example.com/ext")).Should().ContainSingle();
    }

    [Fact]
    public void Bpmn_catch_all_attributes_are_written_back_as_plain_attributes()
    {
        var model = SimpleModel();
        model.Elements[2].Properties["bpmn:isInterrupting"] = System.Text.Json.JsonSerializer.SerializeToElement("false");

        var end = Parse(Export(model))
            .Descendants(XName.Get("endEvent", BpmnConstants.ModelNamespace)).Single();
        end.Attribute("isInterrupting")!.Value.Should().Be("false");
    }

    [Fact]
    public void Foreign_prefixed_attributes_are_declared_on_the_definitions_root()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["camunda:assignee"] = System.Text.Json.JsonSerializer.SerializeToElement("${who}");

        var root = Parse(Export(model));
        root.Attribute(XNamespace.Xmlns + "camunda")!.Value.Should().Be("http://camunda.org/schema/1.0/bpmn");
        root.Descendants(XName.Get("userTask", BpmnConstants.ModelNamespace)).Single()
            .Attribute(XName.Get("assignee", "http://camunda.org/schema/1.0/bpmn"))!.Value
            .Should().Be("${who}");
    }

    [Fact]
    public void Foreign_extensions_are_embedded_verbatim_under_extensionElements()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["foreignExtensions"] = System.Text.Json.JsonSerializer.SerializeToElement<Dictionary<string, string>>(
            new Dictionary<string, string>
            {
                ["http://example.com/tool"] = "<tool:note xmlns:tool=\"http://example.com/tool\" severity=\"high\"/>",
            });

        var task = Parse(Export(model))
            .Descendants(XName.Get("userTask", BpmnConstants.ModelNamespace)).Single();
        var extension = task.Element(XName.Get("extensionElements", BpmnConstants.ModelNamespace))!;
        var payload = extension.Elements().Single();
        payload.Name.Should().Be(XName.Get("note", "http://example.com/tool"));
        payload.Attribute("severity")!.Value.Should().Be("high");
    }

    [Fact]
    public void Multi_model_export_produces_one_definitions_with_collaboration_and_message_flows()
    {
        var customer = new ModelingModelDto { Id = "Process_A", Title = "A", Notation = "bpmn2" };
        customer.Elements.AddRange(
        [
            new ModelingElementDto { Id = "Pool_A", SemanticType = BpmnSemanticTypes.Pool, Name = "A" },
            new ModelingElementDto { Id = "Task_A", SemanticType = BpmnSemanticTypes.Task, Name = "Work A", ParentId = "Pool_A" },
        ]);
        var shop = new ModelingModelDto { Id = "Process_B", Title = "B", Notation = "bpmn2" };
        shop.Elements.AddRange(
        [
            new ModelingElementDto { Id = "Pool_B", SemanticType = BpmnSemanticTypes.Pool, Name = "B" },
            new ModelingElementDto { Id = "Task_B", SemanticType = BpmnSemanticTypes.Task, Name = "Work B", ParentId = "Pool_B" },
        ]);
        customer.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "MessageFlow_1",
            SourceElementId = "Task_A",
            TargetElementId = "Task_B",
            RelationshipType = BpmnRelationshipTypes.MessageFlow,
        });

        var root = Parse(new BpmnXmlExporter().Export(new List<ModelingModelDto> { customer, shop }));

        var collaboration = root.Element(XName.Get("collaboration", BpmnConstants.ModelNamespace))!;
        collaboration.Elements(XName.Get("participant", BpmnConstants.ModelNamespace))
            .Select(element => (element.Attribute("id")!.Value, element.Attribute("processRef")?.Value))
            .Should().Equal(("Pool_A", "Process_A"), ("Pool_B", "Process_B"));
        collaboration.Elements(XName.Get("messageFlow", BpmnConstants.ModelNamespace))
            .Select(element => (element.Attribute("id")!.Value,
                element.Attribute("sourceRef")!.Value, element.Attribute("targetRef")!.Value))
            .Should().Equal(("MessageFlow_1", "Task_A", "Task_B"));
        root.Elements(XName.Get("process", BpmnConstants.ModelNamespace))
            .Select(element => element.Attribute("id")!.Value).Should().Equal("Process_A", "Process_B");
    }

    [Fact]
    public void Black_box_pool_exports_a_participant_without_processRef()
    {
        var model = new ModelingModelDto { Id = "Process_Internal", Title = "Internal", Notation = "bpmn2" };
        model.Elements.AddRange(
        [
            new ModelingElementDto { Id = "Pool_Internal", SemanticType = BpmnSemanticTypes.Pool, Name = "Us" },
            new ModelingElementDto { Id = "Pool_External", SemanticType = BpmnSemanticTypes.Pool, Name = "Them" },
            new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.Task, ParentId = "Pool_Internal" },
        ]);

        var root = Parse(Export(model));
        var participants = root.Element(XName.Get("collaboration", BpmnConstants.ModelNamespace))!
            .Elements(XName.Get("participant", BpmnConstants.ModelNamespace)).ToList();
        participants.Single(p => p.Attribute("id")!.Value == "Pool_External").Attribute("processRef").Should().BeNull();
        root.Elements(XName.Get("process", BpmnConstants.ModelNamespace)).Should().ContainSingle();
    }

    [Fact]
    public void EmitBpmndi_false_omits_the_diagram()
    {
        var model = SimpleModel();

        var result = Export(model, new BpmnExportOptions { EmitBpmndi = false });

        result.Xml.Should().NotContain("BPMNDiagram");
        XElement.Parse(result.Xml!).Descendants()
            .Should().NotContain(element => element.Name.NamespaceName == BpmnConstants.DiagramNamespace);
    }

    [Fact]
    public void Global_definitions_from_bpmnDefinitions_are_rebuilt_at_the_root()
    {
        var model = SimpleModel();
        model.Properties["bpmnDefinitions"] = System.Text.Json.JsonSerializer.SerializeToElement(
            new List<Dictionary<string, string>>
            {
                new() { ["kind"] = "message", ["id"] = "Message_1", ["name"] = "Order", ["code"] = "" },
                new() { ["kind"] = "error", ["id"] = "Error_1", ["name"] = "Failure", ["code"] = "E-1" },
            });

        var root = Parse(Export(model));

        var message = root.Element(XName.Get("message", BpmnConstants.ModelNamespace))!;
        message.Attribute("id")!.Value.Should().Be("Message_1");
        message.Attribute("name")!.Value.Should().Be("Order");
        var error = root.Element(XName.Get("error", BpmnConstants.ModelNamespace))!;
        error.Attribute("errorCode")!.Value.Should().Be("E-1");
    }

    [Fact]
    public void Unknown_semantic_types_are_reported_and_skipped_without_throwing()
    {
        var model = SimpleModel();
        model.Elements.Add(new ModelingElementDto { Id = "Mystery_1", SemanticType = "CrystalBall" });

        var result = Export(model);

        result.Success.Should().BeTrue();
        result.Losses.Should().ContainSingle(loss =>
            loss.ElementId == "Mystery_1" && loss.Construct == "CrystalBall");
        result.Xml.Should().NotContain("Mystery_1");
    }

    [Fact]
    public void Export_is_deterministic_LF_and_bom_free()
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("collaboration-3-pools.bpmn"));
        var options = new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions };

        var first = new BpmnXmlExporter().Export(imported.Models, options);
        var second = new BpmnXmlExporter().Export(imported.Models, options);

        first.Success.Should().BeTrue(first.ErrorMessage);
        first.Xml.Should().Be(second.Xml);
        first.Xml.Should().NotContain("\r");
        first.Xml.Should().NotStartWith("ï»¿");
        first.Xml.Should().StartWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
    }

    [Fact]
    public void Numbers_use_invariant_culture()
    {
        var model = SimpleModel();
        model.Views.Add(new ModelingViewDto
        {
            Id = "Plane_1",
            Name = "Plane",
            Notation = "bpmn2",
            Nodes = [new ModelingViewNodeDto { ElementId = "Task_1", X = 170.5, Y = 80.25, Width = 100, Height = 80 }],
        });

        var result = Export(model);

        result.Xml.Should().Contain("170.5");
        result.Xml.Should().NotContain("170,5");
    }

    [Fact]
    public void Export_never_throws_for_bad_input_and_reports_failure()
    {
        var exporter = new BpmnXmlExporter();

        exporter.Export((ModelingModelDto)null!).Success.Should().BeFalse();
        exporter.Export(Array.Empty<ModelingModelDto>()).Success.Should().BeFalse();
    }

    [Fact]
    public void Export_honors_cancellation()
    {
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => new BpmnXmlExporter().Export(SimpleModel(), options: null, cts.Token);
        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public void Tool_extensions_receive_an_export_hook_per_element_with_extension_elements()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["camunda:assignee"] = System.Text.Json.JsonSerializer.SerializeToElement("${who}");
        var extension = new RecordingToolExtension();
        var options = new BpmnExportOptions();
        options.ToolExtensions.Add(extension);

        Export(model, options);

        extension.Elements.Should().Contain("Task_1");
        extension.ExtensionElements.Should().NotBeEmpty();
    }

    [Fact]
    public void AddTempoBpmnXml_registers_the_exporter_alongside_the_importer()
    {
        var services = new ServiceCollection();
        services.AddTempoBpmnXml();
        var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBpmnXmlImporter>().Should().BeOfType<BpmnXmlImporter>();
        provider.GetRequiredService<IBpmnXmlExporter>().Should().BeOfType<BpmnXmlExporter>();
        services.AddTempoBpmnXml();
        provider.GetServices<IBpmnXmlExporter>().Should().ContainSingle();
    }

    private sealed class RecordingToolExtension : IBpmnToolExtension
    {
        public List<string> Elements { get; } = [];
        public List<XElement> ExtensionElements { get; } = [];

        public string NamespaceUri => "http://camunda.org/schema/1.0/bpmn";

        public void OnImportElement(BpmnToolImportContext context)
        {
        }

        public void OnExportElement(BpmnToolExportContext context)
        {
            Elements.Add(context.Element.Id);
            ExtensionElements.Add(context.ExtensionElements);
        }
    }
}
