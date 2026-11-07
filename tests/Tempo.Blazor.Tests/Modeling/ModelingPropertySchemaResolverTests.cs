using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 review (A3): <see cref="ModelingPropertySchemaResolver"/> — the one bridge between the
/// async host provider and the synchronous resolver the operation engine consumes. It preloads the
/// pairs a model/batch can judge, degrades a throwing pair to "no schema" and never pins a version
/// (the request the engine builds carries the caller's viewpoint and the current culture).
/// </summary>
public sealed class ModelingPropertySchemaResolverTests
{
    [Fact]
    public void The_pairs_cover_every_element_and_relationship_of_the_model()
    {
        var model = CreateModel();

        var pairs = ModelingPropertySchemaResolver.PropertySchemaPairs(model);

        pairs.Should().BeEquivalentTo(
        [
            ("bpmn", "Task"),
            ("bpmn", "SequenceFlow")
        ]);
    }

    [Fact]
    public void The_pairs_include_the_ones_only_the_batch_introduces()
    {
        var model = CreateModel();
        var batch = new ModelingOperationBatch
        {
            Operations =
            [
                new ModelingOperation { Op = "add_element", Name = "New", SemanticType = "Gateway" },
                new ModelingOperation { Op = "add_element", Name = "Other", SemanticType = "Task", Notation = "umb" },
                new ModelingOperation { Op = "add_relationship", RelationshipType = "MessageFlow" }
            ]
        };

        var pairs = ModelingPropertySchemaResolver.PropertySchemaPairs(model, batch);

        pairs.Should().Contain(("bpmn", "Gateway"));
        pairs.Should().Contain(("umb", "Task"), "the operation's own notation wins");
        pairs.Should().Contain(("bpmn", "MessageFlow"));
        pairs.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void The_json_pairs_of_a_batch_are_read_without_parsing_it_twice()
    {
        var model = CreateModel();

        var pairs = ModelingPropertySchemaResolver.PropertySchemaPairsFromJson(
            model,
            """[ { "op": "add_element", "name": "New", "semanticType": "Gateway" } ]""");

        pairs.Should().Contain(("bpmn", "Gateway"));

        // A malformed payload yields the model's own pairs only — the preload must never turn a parse
        // failure into a schema failure (the engine reports the parse error itself).
        ModelingPropertySchemaResolver.PropertySchemaPairsFromJson(model, "not json")
            .Should().BeEquivalentTo(ModelingPropertySchemaResolver.PropertySchemaPairs(model));
    }

    [Fact]
    public async Task Preloading_builds_a_dictionary_lookup_and_a_failing_pair_degrades_to_null()
    {
        var model = CreateModel();
        var provider = new RecordingSchemaProvider { FailingType = "SequenceFlow" };

        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(
            provider,
            model,
            extraPairs: [("bpmn", "Gateway")],
            viewpointKey: "process",
            culture: "cs-CZ");

        resolver.Should().NotBeNull();
        resolver!(new ModelingPropertySchemaRequest { Notation = "bpmn", SemanticType = "Task" })
            .Should().NotBeNull();
        resolver(new ModelingPropertySchemaRequest { Notation = "bpmn", SemanticType = "Gateway" })
            .Should().NotBeNull("the extra pair was preloaded too");
        resolver(new ModelingPropertySchemaRequest { Notation = "bpmn", SemanticType = "SequenceFlow" })
            .Should().BeNull("a throwing pair validates nothing");
        resolver(new ModelingPropertySchemaRequest { Notation = "bpmn", SemanticType = "Unknown" })
            .Should().BeNull("a pair the preload never saw is not resolved on the fly");
        provider.Requests.Should().Contain(request =>
            request.ViewpointKey == "process" && request.Culture == "cs-CZ" && request.SchemaVersion == null);
    }

    [Fact]
    public async Task No_provider_yields_no_resolver()
    {
        var resolver = await ModelingPropertySchemaResolver.PreloadAsync(null, CreateModel());

        resolver.Should().BeNull("without a provider nothing about property values is validated");
    }

    private static ModelingModelDto CreateModel() => new()
    {
        Id = "resolver-model",
        Title = "Resolver",
        Notation = "bpmn",
        Elements =
        [
            new ModelingElementDto { Id = "task-1", Name = "One", Notation = "bpmn", SemanticType = "Task" }
        ],
        Relationships =
        [
            new ModelingRelationshipDto
            {
                Id = "flow-1",
                RelationshipType = "SequenceFlow",
                SourceElementId = "task-1",
                TargetElementId = "task-1"
            }
        ]
    };

    private sealed class RecordingSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => "tempo.tests.resolver";

        public string? FailingType { get; init; }

        public List<ModelingPropertySchemaRequest> Requests { get; } = [];

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (string.Equals(request.SemanticType, FailingType, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The schema service is down.");
            }

            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = $"tests.{request.SemanticType}",
                Version = "1",
                Notation = request.Notation,
                SemanticType = request.SemanticType,
                Sections =
                [
                    new ModelingPropertySectionDto
                    {
                        Key = "main",
                        Label = "Main",
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
            });
        }
    }
}
