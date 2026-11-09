using System.Text.Json.Nodes;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Phase 5 of the BPMN XML converter plan: <c>modeling_import_bpmn</c> and
/// <c>modeling_export_bpmn</c> expose the headless converter through MCP — the model + loss
/// report on import (optionally persisted through the document provider), the XML + export loss
/// report on export. Both run the built-in tool dialect plugins.
/// </summary>
public class ModelingBpmnToolsTests
{
    private const string SimpleProcessXml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     id="Definitions_1" targetNamespace="http://tempo.example/mcp">
          <process id="Process_1" name="Order flow" isExecutable="false">
            <startEvent id="Start" name="Received"/>
            <task id="Check" name="Check order"/>
            <endEvent id="Done" name="Done"/>
            <sequenceFlow id="F1" sourceRef="Start" targetRef="Check"/>
            <sequenceFlow id="F2" sourceRef="Check" targetRef="Done"/>
          </process>
        </definitions>
        """;

    [Fact]
    public async Task Import_returns_models_and_an_empty_loss_report_for_a_clean_document()
    {
        var backend = new FakeModelingBackend();
        var response = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, SimpleProcessXml));

        response!["success"]!.GetValue<bool>().Should().BeTrue();
        response["modelCount"]!.GetValue<int>().Should().Be(1);
        var model = response["models"]![0]!;
        model["title"]!.GetValue<string>().Should().Be("Order flow");
        model["counts"]!["elements"]!.GetValue<int>().Should().Be(3);
        model["counts"]!["relationships"]!.GetValue<int>().Should().Be(2);
        model["elements"]!.AsArray().Should().HaveCount(3);
        response["losses"]!.AsArray().Should().BeEmpty();
        response["saved"]!.AsArray().Should().BeEmpty("save defaults to false (conversion only)");
        backend.SaveCount.Should().Be(0, "a conversion-only import must not persist anything");
    }

    [Fact]
    public async Task Import_reports_losses_for_unsupported_constructs_instead_of_dropping_them()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Defs" targetNamespace="http://tempo.example/mcp">
              <process id="P" isExecutable="false">
                <task id="T"><multiInstanceLoopCharacteristics isSequential="true"/></task>
              </process>
            </definitions>
            """;
        var backend = new FakeModelingBackend();
        var response = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, xml));

        response!["success"]!.GetValue<bool>().Should().BeTrue();
        var loss = response["losses"]!.AsArray().Should().ContainSingle().Subject!;
        loss["construct"]!.GetValue<string>().Should().Be("multiInstanceLoopCharacteristics");
        loss["severity"]!.GetValue<string>().Should().Be("Loss");
        loss["reason"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Import_runs_the_builtin_tool_plugins_for_dialect_extensions()
    {
        var xml = await File.ReadAllTextAsync(Path.Combine(CorpusDirectory(), "camunda-export.bpmn"));
        var backend = new FakeModelingBackend();
        var response = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, xml));

        response!["success"]!.GetValue<bool>().Should().BeTrue();
        response["losses"]!.AsArray().Should().NotContain(node =>
            node!["reason"]!.GetValue<string>().Contains("Unrecognized extension namespace", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Import_with_save_persists_one_document_per_model()
    {
        var backend = new FakeModelingBackend();
        var response = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(
            backend, SimpleProcessXml, save: true, name: "Imported flow", scopeAppId: "app-1"));

        response!["success"]!.GetValue<bool>().Should().BeTrue();
        var saved = response["saved"]!.AsArray().Should().ContainSingle().Subject!;
        var id = Guid.Parse(saved["id"]!.GetValue<string>());
        saved["title"]!.GetValue<string>().Should().Be("Imported flow");
        backend.LastCreateScopeAppId.Should().Be("app-1");

        var stored = await backend.GetModelingModelDocumentAsync(id);
        stored.Should().NotBeNull();
        stored!.Elements.Should().HaveCount(3);
    }

    [Fact]
    public async Task Import_rejects_oversized_and_malformed_input_without_throwing()
    {
        var backend = new FakeModelingBackend();

        var oversized = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(
            backend, new string('x', ModelingBpmnTools.MaxBpmnXmlChars + 1)));
        oversized!["success"]!.GetValue<bool>().Should().BeFalse();
        oversized["error"]!.GetValue<string>().Should().Be(McpToolResults.ValidationFailed);

        var malformed = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, "<definitions><process"));
        malformed!["success"]!.GetValue<bool>().Should().BeFalse();
        malformed["error"]!.GetValue<string>().Should().Be(McpToolResults.ValidationFailed);

        var longName = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(
            backend, SimpleProcessXml, save: true, name: new string('n', ModelingBpmnTools.MaxNameChars + 1)));
        longName!["success"]!.GetValue<bool>().Should().BeFalse();
        longName["error"]!.GetValue<string>().Should().Be(McpToolResults.ValidationFailed);
        backend.SaveCount.Should().Be(0, "a rejected name must not persist anything");

        var badSplit = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, SimpleProcessXml, modelSplit: "Everything"));
        badSplit!["success"]!.GetValue<bool>().Should().BeFalse();
        badSplit["error"]!.GetValue<string>().Should().Be(McpToolResults.ValidationFailed);
    }

    [Fact]
    public async Task Import_with_save_reports_partial_progress_when_one_document_fails()
    {
        var backend = new FakeModelingBackend
        {
            FailSaveForTitle = "Order flow",
        };

        var response = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, SimpleProcessXml, save: true));

        response!["success"]!.GetValue<bool>().Should().BeFalse();
        response["error"]!.GetValue<string>().Should().Be(McpToolResults.Error);
        // The provider contract has no delete: the caller gets exactly what was saved plus where
        // it stopped (finding 4).
        response["failedAt"]!.GetValue<int>().Should().Be(0);
        response["failed"]!.AsArray().Should().ContainSingle()
            .Which!["error"]!.GetValue<string>().Should().Contain("boom");
        response["saved"]!.AsArray().Should().BeEmpty();
        backend.SaveCount.Should().Be(0, "the failing document never reached the save");
    }

    [Fact]
    public async Task Export_returns_bpmn_xml_and_the_loss_report()
    {
        var backend = new FakeModelingBackend();
        var model = new ModelingModelDto
        {
            Id = "mcp-export",
            Title = "Export me",
            Notation = "bpmn2",
            Metadata = { SourceVersion = "http://tempo.example/mcp-export" }
        };
        model.Elements.Add(new ModelingElementDto { Id = "start", SemanticType = "StartEvent", Name = "Begin", Notation = "bpmn2" });
        model.Elements.Add(new ModelingElementDto { Id = "end", SemanticType = "EndEvent", Name = "Finish", Notation = "bpmn2" });
        var id = backend.Add("Export me", "/", model);

        var response = JsonNode.Parse(await ModelingBpmnTools.ExportBpmn(backend, backend, id));

        response!["success"]!.GetValue<bool>().Should().BeTrue();
        response["xml"]!.GetValue<string>().Should().Contain("<process").And.Contain("id=\"start\"");
        response["losses"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Export_with_host_extensions_round_trips_through_import()
    {
        var backend = new FakeModelingBackend();
        var id = backend.Add("Round trip", "/", RoundTripModel());

        var exported = JsonNode.Parse(await ModelingBpmnTools.ExportBpmn(
            backend, backend, id, profile: "WithHostExtensions"));
        exported!["success"]!.GetValue<bool>().Should().BeTrue();
        var xml = exported["xml"]!.GetValue<string>();
        xml.Should().Contain("bpmn-extensions");

        var reimported = JsonNode.Parse(await ModelingBpmnTools.ImportBpmn(backend, xml));
        reimported!["success"]!.GetValue<bool>().Should().BeTrue();
        reimported["models"]![0]!["elements"]!.AsArray().Should().Contain(node =>
            node!["id"]!.GetValue<string>() == "task-with-props");
    }

    [Fact]
    public async Task Export_is_not_found_for_unknown_models_and_validates_the_profile()
    {
        var backend = new FakeModelingBackend();

        var missing = JsonNode.Parse(await ModelingBpmnTools.ExportBpmn(backend, backend, Guid.NewGuid()));
        missing!["success"]!.GetValue<bool>().Should().BeFalse();
        missing["error"]!.GetValue<string>().Should().Be(McpToolResults.NotFound);

        var id = backend.Add("M", "/", RoundTripModel());
        var badProfile = JsonNode.Parse(await ModelingBpmnTools.ExportBpmn(backend, backend, id, profile: "Fancy"));
        badProfile!["success"]!.GetValue<bool>().Should().BeFalse();
        badProfile["error"]!.GetValue<string>().Should().Be(McpToolResults.ValidationFailed);
    }

    private static ModelingModelDto RoundTripModel()
    {
        var model = new ModelingModelDto
        {
            Id = "mcp-roundtrip",
            Title = "Round trip",
            Notation = "bpmn2",
            Metadata = { SourceVersion = "http://tempo.example/mcp-roundtrip" }
        };
        model.Elements.Add(new ModelingElementDto { Id = "task-with-props", SemanticType = "UserTask", Name = "Approve", Notation = "bpmn2" });
        model.Elements[0].Properties["instructions"] = System.Text.Json.JsonSerializer.SerializeToElement("Check the totals");
        return model;
    }

    private static string CorpusDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return Path.Combine(directory!.FullName, "tests", "Tempo.Blazor.Tests", "Modeling", "BpmnCorpus");
    }
}
