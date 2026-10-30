using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

public sealed class ModelingProviderContractTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void ChangeSet_serializes_only_changed_fields()
    {
        var changeSet = new ModelingModelChangeSet
        {
            ModelId = "model-1",
            ProviderKey = "provider-1",
            BaseVersion = "2026.06",
            Elements =
            [
                new ModelingElementChange { ElementId = "element-1", Name = "Renamed element" }
            ],
            Relationships =
            [
                new ModelingRelationshipChange
                {
                    RelationshipId = "rel-1",
                    SetProperties = new Dictionary<string, JsonElement> { ["lane"] = JsonSerializer.SerializeToElement("Sales") }
                }
            ]
        };

        var json = JsonSerializer.Serialize(changeSet, WireOptions);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("modelId").GetString().Should().Be("model-1");
        root.GetProperty("providerKey").GetString().Should().Be("provider-1");
        root.GetProperty("baseVersion").GetString().Should().Be("2026.06");

        var element = root.GetProperty("elements")[0];
        element.GetProperty("elementId").GetString().Should().Be("element-1");
        element.GetProperty("name").GetString().Should().Be("Renamed element");
        // Unchanged fields stay ABSENT from the wire — the writer must be able to tell
        // "not changed" from "changed to null/empty" without a side dictionary.
        element.TryGetProperty("description", out _).Should().BeFalse();
        element.TryGetProperty("setProperties", out _).Should().BeFalse();
        element.TryGetProperty("removedProperties", out _).Should().BeFalse();

        var relationship = root.GetProperty("relationships")[0];
        relationship.GetProperty("relationshipId").GetString().Should().Be("rel-1");
        relationship.GetProperty("setProperties").GetProperty("lane").GetString().Should().Be("Sales");
        relationship.TryGetProperty("name", out _).Should().BeFalse();
    }

    [Fact]
    public void Writer_is_resolved_by_provider_key()
    {
        using var services = new ServiceCollection()
            .AddTempoBlazorModeling()
            .AddSingleton<IModelingModelWriter>(new TestModelingModelWriter("tempo.tests.writer-a"))
            .AddSingleton<IModelingModelWriter>(new TestModelingModelWriter("tempo.tests.writer-b"))
            .BuildServiceProvider();

        var writers = services.GetServices<IModelingModelWriter>().ToList();

        // The demo provider ships an in-memory demo writer; the host's writers are additive and
        // are resolved BY ProviderKey, never by registration order.
        writers.Should().Contain(writer => writer.ProviderKey == DemoModelingModelProvider.ProviderKeyValue);
        writers.Should().Contain(writer => writer.ProviderKey == "tempo.tests.writer-a");
        writers.Should().Contain(writer => writer.ProviderKey == "tempo.tests.writer-b");

        var resolved = writers.FirstOrDefault(writer => writer.ProviderKey == "tempo.tests.writer-b");
        resolved.Should().NotBeNull();

        var changeSet = new ModelingModelChangeSet { ModelId = "m", ProviderKey = "tempo.tests.writer-b", BaseVersion = "v" };
        var result = resolved!.SaveChangesAsync(changeSet, CancellationToken.None);
        result.IsCompletedSuccessfully.Should().BeTrue("the contract is awaitable and must not throw synchronously");
        result.Result.Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_contract_can_be_implemented_without_blazor_assembly_dependency()
    {
        IModelingModelProvider provider = new TestModelingModelProvider();

        var model = await provider.GetModelAsync(
            new ModelingModelRequest
            {
                ProviderKey = provider.ProviderKey,
                SourceKind = "demo",
                SourceId = "source-1"
            },
            CancellationToken.None);

        provider.ProviderKey.Should().Be("test-provider");
        model.Id.Should().Be("test-model");

        var referencedAssemblies = typeof(IModelingModelProvider)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        referencedAssemblies.Should().NotContain("Tempo.Blazor");
        referencedAssemblies.Should().NotContain("Microsoft.AspNetCore.Components");
    }

    [Fact]
    public void Request_with_null_filter_options_keeps_dictionary_safe_to_access()
    {
        const string json = """
            {
              "providerKey": "demo",
              "sourceKind": "workspace",
              "sourceId": "source-1",
              "filterOptions": null
            }
            """;

        var request = JsonSerializer.Deserialize<ModelingModelRequest>(json, Options)!;

        request.FilterOptions.Should().NotBeNull();
        request.FilterOptions.Should().BeEmpty();
        request.FilterOptions.TryGetValue("missing", out _).Should().BeFalse();
    }

    [Fact]
    public void Generation_result_with_empty_issues_and_null_document_serializes_without_failure()
    {
        var result = new ModelingDiagramGenerationResultDto
        {
            Document = null,
            GeneratedAt = new DateTimeOffset(2026, 6, 6, 9, 15, 0, TimeSpan.Zero)
        };

        var json = JsonSerializer.Serialize(result, Options);
        var restored = JsonSerializer.Deserialize<ModelingDiagramGenerationResultDto>(json, Options)!;

        restored.Document.Should().BeNull();
        restored.Issues.Should().BeEmpty();
        restored.GeneratedAt.Should().Be(result.GeneratedAt);

        using var document = JsonDocument.Parse(json);
        document.RootElement.GetProperty(nameof(ModelingDiagramGenerationResultDto.Document)).ValueKind
            .Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty(nameof(ModelingDiagramGenerationResultDto.Issues)).GetArrayLength()
            .Should().Be(0);
    }

    private sealed class TestModelingModelProvider : IModelingModelProvider
    {
        public string ProviderKey => "test-provider";

        public Task<ModelingModelDto> GetModelAsync(ModelingModelRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new ModelingModelDto
            {
                Id = "test-model",
                Title = "Test model",
                Notation = request.Notation
            });
        }
    }

    private sealed class TestModelingModelWriter : IModelingModelWriter
    {
        public TestModelingModelWriter(string providerKey) => ProviderKey = providerKey;

        public string ProviderKey { get; }

        public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
            => Task.FromResult(new ModelingModelSaveResult { Succeeded = true });
    }
}
