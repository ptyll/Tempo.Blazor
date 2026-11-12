using System.Text.Json.Nodes;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>
/// Phase 7 contract snapshots: the exact JSON an agent sees for the modeling read/write tools. The
/// committed golden file is the contract — a response shape, a field name, an error code or an error
/// text that changes shows up as a diff here, not as a silent break for the PromptHelper host.
/// <para>
/// Regenerate deliberately with <c>TM_REGEN_MODELING_MCP_SNAPSHOT=1 dotnet test
/// tests/Tempo.Blazor.Mcp.Tests --filter FullyQualifiedName~ModelingMcpContractSnapshotTests</c> and
/// review the diff: every line of it is a contract change an agent may depend on.
/// </para>
/// </summary>
public class ModelingMcpContractSnapshotTests
{
    private const string SnapshotRelativePath = "tests/Tempo.Blazor.Mcp.Tests/Fixtures/modeling-mcp-contract.snapshot.json";

    [Fact]
    public async Task The_modeling_tool_contract_matches_the_committed_snapshot()
    {
        var actual = (await BuildSnapshotAsync()).ToJsonString(McpJson.Options);
        var path = Path.Combine(RepoRoot(), SnapshotRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (Environment.GetEnvironmentVariable("TM_REGEN_MODELING_MCP_SNAPSHOT") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual + Environment.NewLine);
            return;
        }

        File.Exists(path).Should().BeTrue(
            $"the committed contract snapshot is missing at {SnapshotRelativePath}; regenerate it with " +
            "TM_REGEN_MODELING_MCP_SNAPSHOT=1 and review the diff");
        var committed = JsonNode.Parse(File.ReadAllText(path));
        JsonNode.DeepEquals(committed, JsonNode.Parse(actual)).Should().BeTrue(
            "a modeling MCP response changed; this is a contract change for agents — review it and " +
            $"regenerate the snapshot if intended.\n--- committed ---\n{committed}\n--- actual ---\n{actual}");
    }

    private static async Task<JsonObject> BuildSnapshotAsync()
    {
        var (backend, id) = Seeded();

        var tree = NormalizeModelId(Normalize(ParseNode(await ModelingModelTools.GetModelTree(backend, backend, id))));
        var schema = NormalizeModelId(Normalize(ParseNode(await ModelingPropertySchemaTools.GetPropertySchema(
            backend,
            backend,
            [new TaskSchemaProvider()],
            modelId: id,
            elementId: "task",
            culture: "en-US"))));
        var rejected = Normalize(ParseNode(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            """
            [
              { "op": "add_relationship", "id": "bad", "relationshipType": "Association", "sourceElementId": "task", "targetElementId": "missing" },
              { "op": "set_view_node", "elementId": "missing", "x": 1, "y": 2 }
            ]
            """,
            FakeModelingNotation.RelationshipRules(),
            backend.ModifiedAtOf(id))));
        // The seeded model is bpmn2 while FakeModelingNotation's registry only knows ArchiMate, so the
        // applied batch runs on the allow-all rules: this snapshot pins the SUCCESS payload (applied
        // counts, created ids and the diff an agent reads), not the notation matrix.
        var applied = NormalizeModelId(Normalize(ParseNode(await ModelingOperationTools.ApplyOperations(
            backend,
            backend,
            id,
            """
            [
              { "op": "add_element", "id": "step", "name": "Approve", "semanticType": "Task", "notation": "bpmn2", "properties": { "instructions": "Approve the order" } },
              { "op": "add_relationship", "id": "flow", "relationshipType": "SequenceFlow", "sourceElementId": "task", "targetElementId": "step" },
              { "op": "set_view_node", "elementId": "task", "x": 40, "y": 60, "width": 180, "height": 90 },
              { "op": "set_view_node", "elementId": "step", "x": 320, "y": 60 },
              { "op": "set_view_connection", "relationshipId": "flow", "waypoints": [ { "x": 240, "y": 100 } ] }
            ]
            """,
            FakeModelingNotation.AllowAllRules(),
            backend.ModifiedAtOf(id),
            [new TaskSchemaProvider()]))));

        // The "applied" case must really succeed — a regression here used to hide behind the
        // committed snapshot (which had recorded the bpmn2 rejection as the contract).
        applied!["success"]!.GetValue<bool>().Should().BeTrue("the applied batch is the success contract");
        applied["applied"]!.GetValue<int>().Should().Be(5);
        applied["createdIds"]!.AsArray().Should().BeEmpty("both ids are supplied by the batch");
        applied["diff"]!["addedElementIds"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("step");
        applied["diff"]!["addedRelationshipIds"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("flow");
        applied["diff"]!["layoutOnlyElementIds"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("task");

        // BPMN converter plan phase 5: the import/export tools join the contract. The seeded model
        // is exported with a fixed targetNamespace so the XML is deterministic (the exporter
        // version is the assembly informational version with any CI prerelease/build-metadata
        // suffix cut off, so CI version stamps cannot churn this snapshot).
        var imported = Normalize(ParseNode(await ModelingBpmnTools.ImportBpmn(backend, """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="Defs" targetNamespace="http://tempo.example/contract-import">
              <process id="ImportedProcess" name="Imported process" isExecutable="false">
                <startEvent id="ImportedStart" name="Begin"/>
              </process>
            </definitions>
            """)));
        var exported = NormalizeModelId(Normalize(ParseNode(await ModelingBpmnTools.ExportBpmn(
            backend,
            backend,
            id,
            profile: "WithHostExtensions",
            targetNamespace: "http://tempo.example/contract-export"))));

        return new JsonObject
        {
            ["responses"] = new JsonObject
            {
                ["modeling_get_model_tree"] = tree,
                ["modeling_get_property_schema"] = schema,
                ["modeling_apply_operations_rejected"] = rejected,
                ["modeling_apply_operations_applied"] = applied,
                ["modeling_import_bpmn"] = imported,
                ["modeling_export_bpmn"] = exported
            },
            // Classification by effect: which tools mutate host state. Everything not listed is
            // read-only; the snapshot pins the classification so a new write tool cannot slip in
            // unnoticed.
            ["toolClassificationByEffect"] = new JsonObject
            {
                ["modeling_apply_operations"] = "write",
                ["modeling_import_bpmn"] = "write (only when save=true; otherwise read-only conversion)"
            }
        };
    }

    private static (FakeModelingBackend Backend, Guid Id) Seeded()
    {
        var model = new ModelingModelDto { Id = "contract", Title = "Contract", Notation = "bpmn2" };
        model.Elements.Add(new ModelingElementDto
        {
            Id = "task",
            Name = "Validate order",
            SemanticType = "Task",
            Notation = "bpmn2"
        });
        model.Views.Add(new ModelingViewDto { Id = "default", Name = "Process", ViewpointKey = "Process" });
        var backend = new FakeModelingBackend();
        var id = backend.Add("Contract", "/", model);
        return (backend, id);
    }

    /// <summary>The backend mints a GUID per model, so the root model id is normalized too.</summary>
    private static JsonNode? NormalizeModelId(JsonNode? node)
    {
        if (node is JsonObject root)
        {
            if (root.ContainsKey("id"))
            {
                root["id"] = "<modelId>";
            }

            if (root.ContainsKey("modelId"))
            {
                root["modelId"] = "<modelId>";
            }
        }

        return node;
    }

    /// <summary>
    /// Replaces the volatile members (wall-clock tokens, generated timestamps) with a stable marker, so
    /// the snapshot pins the SHAPE and the stable values only.
    /// </summary>
    private static JsonNode? Normalize(JsonNode? node)
    {
        if (node is not JsonObject root)
        {
            return node;
        }

        foreach (var key in new[] { "concurrencyToken", "modifiedAt", "generatedAt" })
        {
            if (root.ContainsKey(key))
            {
                root[key] = "<dynamic>";
            }
        }

        return root;
    }

    private static JsonNode? ParseNode(string json) => JsonNode.Parse(json);

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull();
        return directory!.FullName;
    }

    /// <summary>Requires <c>instructions</c> on a Task (the same shape the other MCP schema specs use).</summary>
    private sealed class TaskSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tests.contract-schema";

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(
            ModelingPropertySchemaRequest request,
            CancellationToken cancellationToken)
            => Task.FromResult<ModelingPropertySchemaDto?>(
                string.Equals(request.SemanticType, "Task", StringComparison.Ordinal)
                    ? new ModelingPropertySchemaDto
                    {
                        SchemaId = "tests.contract.task",
                        Version = "1",
                        Notation = "bpmn2",
                        SemanticType = "Task",
                        Sections =
                        [
                            new ModelingPropertySectionDto
                            {
                                Key = "execution",
                                Label = "Execution",
                                Fields =
                                [
                                    new ModelingPropertyFieldDto
                                    {
                                        Key = "instructions",
                                        Label = "Instructions",
                                        Type = ModelingPropertyFieldType.String,
                                        Required = true
                                    }
                                ]
                            }
                        ]
                    }
                    : null);
    }
}
