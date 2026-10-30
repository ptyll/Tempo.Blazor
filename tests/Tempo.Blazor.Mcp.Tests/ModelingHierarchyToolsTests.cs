using System.Text.Json;
using Tempo.Blazor.Mcp.Modeling;
using Tempo.Blazor.Mcp.Tests.Fixtures;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Mcp.Tests;

/// <summary>Phase 12: ParentId hierarchy support in the modeling MCP tools — add/update/delete
/// operations and the hierarchy validation (missing parent, cycles).</summary>
public class ModelingHierarchyToolsTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static (FakeModelingBackend backend, Guid id) Seeded()
    {
        var model = new ModelingModelDto { Notation = "bpmn" };
        model.Elements.Add(new ModelingElementDto { Id = "subprocess-1", Name = "Order fulfillment", SemanticType = "subProcess", Notation = "bpmn" });
        model.Elements.Add(new ModelingElementDto { Id = "task-1", Name = "Validate order", SemanticType = "userTask", Notation = "bpmn", ParentId = "subprocess-1" });
        var backend = new FakeModelingBackend();
        var id = backend.Add("Hierarchy", "/Architecture", model);
        return (backend, id);
    }

    // ── operations ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateElement_SetsParentId()
    {
        var (backend, id) = Seeded();
        var ops = """
        [
          { "op": "add_element", "id": "task-2", "name": "Ship order", "semanticType": "serviceTask" },
          { "op": "update_element", "id": "task-2", "parentId": "subprocess-1" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        var saved = backend.ModelOf(id);
        saved.Elements.Single(element => element.Id == "task-2").ParentId.Should().Be("subprocess-1");
    }

    [Fact]
    public async Task AddElement_WithParentId_StoresIt()
    {
        var (backend, id) = Seeded();
        var ops = """[ { "op": "add_element", "id": "task-3", "name": "New", "semanticType": "userTask", "parentId": "subprocess-1" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        backend.ModelOf(id).Elements.Single(element => element.Id == "task-3").ParentId.Should().Be("subprocess-1");
    }

    [Fact]
    public async Task UpdateElement_EmptyParentId_ClearsTheParent()
    {
        var (backend, id) = Seeded();
        var ops = """[ { "op": "update_element", "id": "task-1", "parentId": "" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        backend.ModelOf(id).Elements.Single(element => element.Id == "task-1").ParentId.Should().BeEmpty();
    }

    [Fact]
    public async Task AddElement_WithUnknownParent_FailsWithoutSaving()
    {
        var (backend, id) = Seeded();
        var ops = """[ { "op": "add_element", "id": "orphan", "name": "Orphan", "semanticType": "userTask", "parentId": "ghost" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("ghost");
        backend.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateElement_WithSelfParent_Fails()
    {
        var (backend, id) = Seeded();
        var ops = """[ { "op": "update_element", "id": "task-1", "parentId": "task-1" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("cycle");
        backend.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdateElement_WithParentCreatingACycle_Fails()
    {
        var (backend, id) = Seeded();
        var ops = """
        [
          { "op": "add_element", "id": "child-1", "name": "Child", "semanticType": "userTask", "parentId": "task-1" },
          { "op": "update_element", "id": "subprocess-1", "parentId": "child-1" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("cycle");
        backend.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task DeleteElement_WithChildren_IsRejected()
    {
        // Confirmed behavior: no silent cascade — the agent must reparent or delete the
        // children in the same batch first.
        var (backend, id) = Seeded();
        var ops = """[ { "op": "delete_element", "id": "subprocess-1" } ]""";

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeFalse();
        result.GetProperty("error").GetString().Should().Be("validation_failed");
        result.GetProperty("validationErrors")[0].GetString().Should().Contain("task-1");
        backend.SaveCount.Should().Be(0);
        backend.ModelOf(id).Elements.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteElement_ChildrenFirstThenParentInOneBatch_Succeeds()
    {
        // The no-cascade refusal is satisfied WITHIN one batch: the child is deleted first, so
        // by the time the parent's delete is applied it no longer has children — exactly the
        // ordering the error message asks the agent for. One save, both elements gone.
        var (backend, id) = Seeded();
        var ops = """
        [
          { "op": "delete_element", "id": "task-1" },
          { "op": "delete_element", "id": "subprocess-1" }
        ]
        """;

        var result = Parse(await ModelingOperationTools.ApplyOperations(backend, backend, id, ops));

        result.GetProperty("success").GetBoolean().Should().BeTrue();
        result.GetProperty("applied").GetInt32().Should().Be(2);
        backend.ModelOf(id).Elements.Should().BeEmpty();
        backend.SaveCount.Should().Be(1);
    }

    // ── validation ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Validate_ReportsParentCycleAndMissingParent()
    {
        var model = new ModelingModelDto { Notation = "bpmn" };
        model.Elements.Add(new ModelingElementDto { Id = "a", Name = "A", SemanticType = "userTask", ParentId = "b" });
        model.Elements.Add(new ModelingElementDto { Id = "b", Name = "B", SemanticType = "userTask", ParentId = "a" });
        model.Elements.Add(new ModelingElementDto { Id = "orphan", Name = "Orphan", SemanticType = "userTask", ParentId = "ghost" });
        var json = JsonSerializer.Serialize(model, McpJson.Options);

        var result = Parse(await ModelingValidationTools.Validate(new FakeModelingBackend(), new FakeModelingBackend(), modelId: null, modelJson: json));

        result.GetProperty("valid").GetBoolean().Should().BeFalse();
        var issues = result.GetProperty("issues").EnumerateArray()
            .Where(issue => issue.GetProperty("category").GetString() == "hierarchy")
            .ToList();
        issues.Should().Contain(issue =>
            issue.GetProperty("message").GetString()!.Contains("cycle", StringComparison.OrdinalIgnoreCase)
            && issue.GetProperty("sourceElementId").GetString() == "a");
        issues.Should().Contain(issue =>
            issue.GetProperty("message").GetString()!.Contains("ghost")
            && issue.GetProperty("sourceElementId").GetString() == "orphan");
    }

    [Fact]
    public async Task Validate_CleanHierarchy_IsValid()
    {
        var (backend, id) = Seeded();

        var result = Parse(await ModelingValidationTools.Validate(backend, backend, modelId: id));

        result.GetProperty("valid").GetBoolean().Should().BeTrue();
        result.GetProperty("issues").EnumerateArray()
            .Should().NotContain(issue => issue.GetProperty("category").GetString() == "hierarchy");
    }
}
