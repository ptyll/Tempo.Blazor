using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase-4 review findings 1–12: textAnnotation standard form, xsi typing, plane resolution,
/// dataObject synthesis, process attributes, robustness (NCName/invalid chars/event types),
/// duplicate ids, prefix reservation, tags/governance host payload, export perf and the
/// Camunda-style flow wiring (docs/modeling-bpmn-xml.md §5).
/// </summary>
public sealed class BpmnExporterReviewTests
{
    private static BpmnExportResult Export(ModelingModelDto model, BpmnExportOptions? options = null) =>
        new BpmnXmlExporter().Export(model, options);

    private static XElement Parse(BpmnExportResult result)
    {
        result.Success.Should().BeTrue(result.ErrorMessage);
        return XElement.Parse(result.Xml!);
    }

    private static XName N(string localName) => XName.Get(localName, BpmnConstants.ModelNamespace);

    private static ModelingModelDto SimpleModel()
    {
        var model = new ModelingModelDto { Id = "Process_1", Title = "Handle", Notation = "bpmn2" };
        model.Elements.AddRange(
        [
            new ModelingElementDto { Id = "Start_1", SemanticType = BpmnSemanticTypes.StartEvent },
            new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.UserTask, Name = "Do it" },
            new ModelingElementDto { Id = "End_1", SemanticType = BpmnSemanticTypes.EndEvent },
        ]);
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_1", SourceElementId = "Start_1", TargetElementId = "Task_1",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_2", SourceElementId = "Task_1", TargetElementId = "End_1",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });
        return model;
    }

    // ---------------------------------------------------------------- finding 1: textAnnotation

    [Fact]
    public void Text_annotation_body_is_a_text_child_and_carries_no_name_attribute()
    {
        var model = SimpleModel();
        model.Elements.Add(new ModelingElementDto
        {
            Id = "Note_1",
            SemanticType = BpmnSemanticTypes.TextAnnotation,
            Name = "Remember the SLA",
        });

        var root = Parse(Export(model));
        var annotation = root.Descendants(N("textAnnotation")).Should().ContainSingle().Subject;
        annotation.Attribute("name").Should().BeNull("the annotation text is not a name attribute");
        annotation.Element(N("text"))!.Value.Should().Be("Remember the SLA");
    }

    [Fact]
    public void Import_reads_the_text_child_and_falls_back_to_direct_text()
    {
        var standard = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("text-annotation-text.bpmn"));
        standard.Models.Should().ContainSingle();
        standard.Models[0].Elements
            .Where(element => element.SemanticType == BpmnSemanticTypes.TextAnnotation)
            .Should().ContainSingle()
            .Which.Name.Should().Contain("Standard form");

        var legacy = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("annotations-group.bpmn"));
        legacy.Models.Should().ContainSingle();
        legacy.Models[0].Elements
            .Where(element => element.SemanticType == BpmnSemanticTypes.TextAnnotation)
            .Should().Contain(element => element.Name.Contains("SLA", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- finding 2: xsi typing

    [Fact]
    public void Root_declares_xsi_and_expressions_carry_the_formal_expression_type()
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("simple-process.bpmn"));
        var root = Parse(new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));

        root.Attribute(XNamespace.Xmlns + "xsi").Should().NotBeNull();
        root.Attribute("exporter")!.Value.Should().Be("Tempo.Blazor");
        root.Attribute("exporterVersion")!.Value.Should().NotBeNullOrEmpty();

        var expression = root.Descendants(N("conditionExpression")).Should().ContainSingle().Subject;
        expression.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"))!.Value
            .Should().Be("tFormalExpression");
    }

    // Regression for the red main CI gate (run 43155072274): the publish workflows build with
    // -p:Version=<base>-ci-<timestamp> on every main push, which stamps the exporter assembly's
    // informational version. exporterVersion is that full SemVer (prerelease kept) with the
    // build metadata cut — with a CI-stamped assembly this test fails when the stripping regresses.
    [Fact]
    public void Exporter_version_is_the_informational_version_with_build_metadata_cut()
    {
        var root = Parse(Export(SimpleModel()));

        var informational =
            System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                    typeof(BpmnXmlExporter).Assembly)
                ?.InformationalVersion ?? string.Empty;
        var expected = BpmnExportBuilder.StripBuildMetadata(informational);
        expected.Should().NotBeEmpty("the exporter assembly must carry an informational version");

        root.Attribute("exporterVersion")!.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("2.11.0-ci-20261112+abc", "2.11.0-ci-20261112")]
    [InlineData("2.11.0+abc", "2.11.0")]
    [InlineData("2.11.0", "2.11.0")]
    public void StripBuildMetadata_cuts_only_the_build_metadata(string version, string expected) =>
        BpmnExportBuilder.StripBuildMetadata(version).Should().Be(expected);

    [Fact]
    public void Timer_and_conditional_event_expressions_carry_the_formal_expression_type()
    {
        var imported = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture("all-events.bpmn"));
        var root = Parse(new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));

        foreach (var kind in new[] { "timeDuration", "timeDate", "timeCycle", "condition" })
        {
            foreach (var element in root.Descendants(N(kind)))
            {
                element.Attribute(XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"))!.Value
                    .Should().Be("tFormalExpression", $"{kind} is an expression");
            }
        }
    }

    // ---------------------------------------------------------------- finding 3: plane resolution

    [Fact]
    public void A_plane_with_pool_di_references_the_collaboration()
    {
        var first = PooledModel("Process_A", "Pool_A", "Task_A");
        var second = PooledModel("Process_B", "Pool_B", "Task_B");
        var view = new ModelingViewDto { Id = "View_AB", Name = "Both pools", Notation = "bpmn2" };
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "Pool_A", X = 0, Y = 0, Width = 400, Height = 200 });
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "Pool_B", X = 0, Y = 220, Width = 400, Height = 200 });
        first.Views.Add(view);

        var root = Parse(new BpmnXmlExporter().Export([first, second]));
        root.Elements(N("collaboration")).Should().ContainSingle();
        var plane = root.Descendants(XName.Get("BPMNPlane", BpmnConstants.DiagramNamespace)).Should().ContainSingle().Subject;
        var collaboration = root.Element(N("collaboration"))!;
        plane.Attribute("bpmnElement")!.Value.Should().Be(collaboration.Attribute("id")!.Value);
    }

    [Fact]
    public void Multi_model_export_without_pools_or_flows_writes_no_empty_collaboration()
    {
        var root = Parse(new BpmnXmlExporter().Export([SimpleModel(), SimpleModel2()]));
        root.Elements(N("collaboration")).Should().BeEmpty("no participants and no message flows — no collaboration element");
        root.Elements(N("process")).Should().HaveCount(2);
    }

    private static ModelingModelDto PooledModel(string processId, string poolId, string taskId)
    {
        var model = new ModelingModelDto { Id = processId, Title = processId, Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = poolId, SemanticType = BpmnSemanticTypes.Pool, Name = poolId });
        model.Elements.Add(new ModelingElementDto
        {
            Id = taskId,
            SemanticType = BpmnSemanticTypes.Task,
            Name = taskId,
            ParentId = poolId,
        });
        return model;
    }

    private static ModelingModelDto SimpleModel2()
    {
        var model = new ModelingModelDto { Id = "Process_2", Title = "Other", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = "Task_2", SemanticType = BpmnSemanticTypes.Task });
        return model;
    }

    // ---------------------------------------------------------------- finding 4: dataObject synthesis

    [Fact]
    public void One_synthesized_dataObject_per_distinct_dataObjectRef()
    {
        var model = SimpleModel();
        foreach (var id in new[] { "Ref_1", "Ref_2" })
        {
            model.Elements.Add(new ModelingElementDto
            {
                Id = id,
                SemanticType = BpmnSemanticTypes.DataObject,
                SourceType = "dataObjectReference",
                Name = "Orders",
                Properties =
                {
                    ["dataObjectRef"] = JsonSerializer.SerializeToElement("DataObject_Orders"),
                    ["isCollection"] = JsonSerializer.SerializeToElement(true),
                },
            });
        }

        var root = Parse(Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));
        var declarations = root.Descendants(N("dataObject"))
            .Where(element => element.Attribute("id")?.Value == "DataObject_Orders")
            .ToList();
        declarations.Should().ContainSingle("one dataObject per distinct dataObjectRef");
        declarations[0].Attribute("name")!.Value.Should().Be("Orders");
        declarations[0].Attribute("isCollection")!.Value.Should().Be("true");
    }

    // ---------------------------------------------------------------- finding 5: process attributes

    [Fact]
    public void Process_attributes_round_trip_through_model_properties()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:camunda="http://camunda.org/schema/1.0/bpmn"
                         id="Defs_1" targetNamespace="http://tempo.example/t">
              <process id="Process_X" name="X" isExecutable="true" camunda:versionTag="1.2" camunda:historyTimeToLive="P30D">
                <task id="Task_X"/>
              </process>
            </definitions>
            """;

        var imported = new BpmnXmlImporter().Import(xml);
        imported.HasErrors.Should().BeFalse(imported.ErrorMessage);
        var model = imported.Models.Should().ContainSingle().Subject;
        model.Properties.Should().ContainKey("bpmn:isExecutable");
        model.Properties["bpmn:isExecutable"].GetString().Should().Be("true");
        model.Properties.Should().ContainKey("camunda:versionTag");
        model.Properties.Should().ContainKey("camunda:historyTimeToLive");

        var root = Parse(new BpmnXmlExporter().Export(imported.Models,
            new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));
        var process = root.Element(N("process"))!;
        process.Attribute("isExecutable")!.Value.Should().Be("true");
        process.Attributes().First(attribute => attribute.Name.LocalName == "versionTag").Value.Should().Be("1.2");
        process.Attributes().First(attribute => attribute.Name.LocalName == "historyTimeToLive").Value.Should().Be("P30D");
    }

    [Fact]
    public void IsExecutable_defaults_to_false_and_definitions_carry_the_exporter_identity()
    {
        var root = Parse(Export(SimpleModel()));
        root.Element(N("process"))!.Attribute("isExecutable")!.Value.Should().Be("false");
        root.Attribute("exporter")!.Value.Should().Be("Tempo.Blazor");
        root.Attribute("exporterVersion")!.Value.Should().NotBeNullOrEmpty();
    }

    // ---------------------------------------------------------------- finding 6: robustness

    [Fact]
    public void A_property_key_that_is_not_a_valid_ncname_becomes_a_loss_not_a_failure()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["bpmn:not valid"] = JsonSerializer.SerializeToElement("x");
        model.Elements[1].Properties["0bad:also bad"] = JsonSerializer.SerializeToElement("y");

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().HaveCount(2, "each invalid attribute name is reported as a loss");
        result.Losses.Should().OnlyContain(loss => loss.Construct == "Property");
        result.Xml.Should().NotContain("not valid");
    }

    [Fact]
    public void Characters_invalid_in_xml_are_stripped_with_an_approximated_loss()
    {
        var model = SimpleModel();
        model.Elements[1].Name = "Do \u0001it";

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().ContainSingle(loss =>
            loss.Severity == BpmnExportLossSeverity.Approximated && loss.ElementId == "Task_1");
        result.Xml.Should().NotContain("\u0001");
        Parse(result).Descendants(N("userTask")).Should().ContainSingle()
            .Which.Attribute("name")!.Value.Should().Be("Do it");
    }

    [Fact]
    public void Event_definitions_with_missing_or_unknown_type_are_reported_not_terminating()
    {
        var model = SimpleModel();
        model.Elements[0].Properties["eventDefinitions"] = JsonSerializer.SerializeToElement(new object?[]
        {
            new Dictionary<string, object?> { ["id"] = "Def_1" },
            new Dictionary<string, object?> { ["type"] = "warp", ["id"] = "Def_2" },
            new Dictionary<string, object?> { ["type"] = "message", ["ref"] = "Message_1" },
        });

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().HaveCount(2, "missing and unknown event definition types are losses");
        result.Xml.Should().NotContain("terminateEventDefinition");
        result.Xml.Should().NotContain("warpEventDefinition");
        result.Xml.Should().Contain("messageEventDefinition");
    }

    // ---------------------------------------------------------------- finding 7: duplicate ids

    [Fact]
    public void Duplicate_element_ids_export_the_first_occurrence_and_report_a_loss()
    {
        var model = SimpleModel();
        model.Elements.Add(new ModelingElementDto { Id = "Task_1", SemanticType = BpmnSemanticTypes.ServiceTask });

        var result = Export(model);
        var root = Parse(result);
        root.Descendants().Count(element => element.Attribute("id")?.Value == "Task_1").Should().Be(1);
        result.Losses.Should().ContainSingle(loss => loss.ElementId == "Task_1");
    }

    [Fact]
    public void The_same_element_instance_shared_between_models_is_not_a_duplicate()
    {
        var model = SimpleModel();
        var shared = new ModelingModelDto { Id = "Process_1_Shared", Title = "Shared", Notation = "bpmn2" };
        shared.Elements.Add(model.Elements[2]);

        var result = new BpmnXmlExporter().Export([model, shared]);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().BeEmpty("the same instance is one element, not a duplicate");
    }

    [Fact]
    public void Duplicate_relationship_ids_report_a_loss()
    {
        var model = SimpleModel();
        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_1",
            SourceElementId = "Task_1",
            TargetElementId = "End_1",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().ContainSingle(loss => loss.ElementId == "Flow_1");
        Parse(result).Descendants(N("sequenceFlow")).Should().HaveCount(2);
    }

    // ---------------------------------------------------------------- finding 8: prefix reservation

    [Fact]
    public void Reserved_prefix_collisions_are_renamed_deterministically_with_an_info_loss()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["dc:marker"] = JsonSerializer.SerializeToElement("x");

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        var root = Parse(result);
        root.Attribute(XNamespace.Xmlns + "dc")!.Value.Should().Be(BpmnConstants.DCNamespace);
        root.Attribute(XNamespace.Xmlns + "dc_1").Should().NotBeNull(
            "the colliding extension prefix is renamed deterministically to {prefix}_1");
        result.Losses.Should().Contain(loss =>
            loss.Severity == BpmnExportLossSeverity.Info && loss.Construct == "Namespace",
            "the collision is reported as an info loss");
    }

    [Fact]
    public void Foreign_extension_namespaces_get_stable_generated_prefixes()
    {
        var model = SimpleModel();
        model.Elements[1].Properties["foreignExtensions"] = JsonSerializer.SerializeToElement(
            new Dictionary<string, string>
            {
                ["urn:tempo-blazor:test:foreign"] = "<f:payload xmlns:f=\"urn:tempo-blazor:test:foreign\">v</f:payload>",
            });

        var first = Parse(Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));
        var second = Parse(Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions }));
        string? FirstGeneratedPrefix(XElement root) => root.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration)
            .Select(attribute => attribute.Name.LocalName)
            .SingleOrDefault(prefix => prefix.StartsWith("ns_", StringComparison.Ordinal));

        var prefix1 = FirstGeneratedPrefix(first);
        var prefix2 = FirstGeneratedPrefix(second);
        prefix1.Should().NotBeNull().And.Be(prefix2, "the generated prefix is a stable hash, not GetHashCode");
        first.ToString(SaveOptions.DisableFormatting).Should()
            .NotContain("xmlns:f=", "duplicate payload xmlns declarations are stripped once the URI is declared on the root");
    }

    // ---------------------------------------------------------------- finding 9: tags / governance

    [Fact]
    public void Tags_and_governance_ride_the_host_payload_and_restore_the_typed_fields()
    {
        var model = SimpleModel();
        model.Elements[1].Tags.AddRange(["urgent", "v2"]);
        model.Elements[1].Governance = new ModelingGovernanceDto
        {
            TrustLevel = "approved",
            ReviewState = "reviewed",
            SyncState = "synced",
            DataSource = "jira",
        };
        model.Relationships[0].Tags.Add("watched");

        var exported = Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        exported.Success.Should().BeTrue(exported.ErrorMessage);
        exported.Losses.Should().BeEmpty();

        var reimported = new BpmnXmlImporter().Import(exported.Xml!);
        var task = reimported.Models.Should().ContainSingle().Subject.Elements
            .First(element => element.Id == "Task_1");
        task.Tags.Should().Equal("urgent", "v2");
        task.Governance.TrustLevel.Should().Be("approved");
        task.Governance.DataSource.Should().Be("jira");
        reimported.Models[0].Relationships.First(relationship => relationship.Id == "Flow_1").Tags.Should().Equal("watched");
    }

    [Fact]
    public void Standard_profile_reports_tags_and_governance_as_losses()
    {
        var model = SimpleModel();
        model.Elements[1].Tags.Add("urgent");
        model.Relationships[0].Tags.Add("watched");

        var result = Export(model);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Losses.Should().Contain(loss => loss.Construct == "Tags" && loss.ElementId == "Task_1");
        result.Losses.Should().Contain(loss => loss.Construct == "Tags" && loss.ElementId == "Flow_1");
    }

    // ---------------------------------------------------------------- finding 10: perf

    [Fact]
    public void Exporting_5000_tasks_and_5000_flows_stays_under_two_seconds()
    {
        const int taskCount = 5000;
        var model = new ModelingModelDto { Id = "Process_Big", Title = "Big", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto { Id = "Start_0", SemanticType = BpmnSemanticTypes.StartEvent });
        for (var index = 0; index < taskCount; index++)
        {
            model.Elements.Add(new ModelingElementDto
            {
                Id = $"Task_{index}",
                SemanticType = BpmnSemanticTypes.Task,
                Name = $"Task {index}",
            });
        }

        model.Elements.Add(new ModelingElementDto { Id = "End_0", SemanticType = BpmnSemanticTypes.EndEvent });
        for (var index = 0; index < taskCount; index++)
        {
            var source = index == 0 ? "Start_0" : $"Task_{index - 1}";
            model.Relationships.Add(new ModelingRelationshipDto
            {
                Id = $"Flow_{index}",
                SourceElementId = source,
                TargetElementId = $"Task_{index}",
                RelationshipType = BpmnRelationshipTypes.SequenceFlow,
            });
        }

        model.Relationships.Add(new ModelingRelationshipDto
        {
            Id = "Flow_End",
            SourceElementId = $"Task_{taskCount - 1}",
            TargetElementId = "End_0",
            RelationshipType = BpmnRelationshipTypes.SequenceFlow,
        });

        var exporter = new BpmnXmlExporter();
        var stopwatch = Stopwatch.StartNew();
        var result = exporter.Export(model, new BpmnExportOptions { Profile = BpmnExportProfile.WithHostExtensions });
        stopwatch.Stop();

        result.Success.Should().BeTrue(result.ErrorMessage);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            $"5000 tasks + 5000 flows must export in under 2 s (took {stopwatch.Elapsed})");
    }

    // ---------------------------------------------------------------- finding 12: wiring + DI id collision

    [Fact]
    public void Flow_nodes_carry_camunda_style_incoming_and_outgoing_wiring()
    {
        var root = Parse(Export(SimpleModel()));
        var task = root.Descendants(N("userTask")).Should().ContainSingle().Subject;
        task.Elements(N("incoming")).Select(element => element.Value).Should().Equal("Flow_1");
        task.Elements(N("outgoing")).Select(element => element.Value).Should().Equal("Flow_2");
    }

    [Fact]
    public void Generated_di_ids_step_aside_of_a_colliding_element_id()
    {
        var model = SimpleModel();
        model.Elements.Add(new ModelingElementDto { Id = "Task_1_di", SemanticType = BpmnSemanticTypes.Task });
        var view = new ModelingViewDto { Id = "View_1", Name = "V", Notation = "bpmn2" };
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "Task_1", X = 0, Y = 0, Width = 100, Height = 80 });
        view.Nodes.Add(new ModelingViewNodeDto { ElementId = "Task_1_di", X = 0, Y = 100, Width = 100, Height = 80 });
        model.Views.Add(view);

        var root = Parse(Export(model));
        var shapes = root.Descendants(XName.Get("BPMNShape", BpmnConstants.DiagramNamespace)).ToList();
        shapes.Select(element => element.Attribute("id")!.Value).Should().OnlyHaveUniqueItems();
        shapes.Single(element => element.Attribute("bpmnElement")?.Value == "Task_1")
            .Attribute("id")!.Value.Should().Be("Task_1_di_1", "the generated DI id steps aside of the element that already owns {id}_di");
        shapes.Single(element => element.Attribute("bpmnElement")?.Value == "Task_1_di")
            .Attribute("id")!.Value.Should().Be("Task_1_di_di");
    }
}
