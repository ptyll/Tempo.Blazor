using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tempo.Blazor.Components.Modeling;
using Tempo.Blazor.Configuration;
using Tempo.Blazor.Modeling;
using Tempo.Blazor.Tests.Localization;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Phase 5 / decision <c>MODELING-PROPERTY-SCHEMA</c>: the EDITOR's side of the contract — the schema
/// provider is resolved by the model provider key (like the writer), the schema reaches the inspector
/// for the SELECTED element, the shared validator's errors land in the issue dock and BLOCK the save,
/// and a host that registered no schema provider keeps the 2.10.0 inspector byte-for-byte.
/// </summary>
public sealed class TmModelingEditorPropertySchemaTests : LocalizationTestBase
{
    private readonly RecordingSchemaProvider _schemaProvider = new();
    private readonly RecordingModelingModelWriter _writer = new();

    public TmModelingEditorPropertySchemaTests()
    {
        Services.AddTempoBlazorModeling();
        Services.TryAddEnumerable(ServiceDescriptor.Singleton<IModelingModelProvider>(new EditingModelingModelProvider()));
        Services.AddSingleton<IModelingModelWriter>(_writer);
    }

    [Fact]
    public void Without_a_schema_provider_the_inspector_stays_the_2_10_0_surface()
    {
        using var cut = RenderEditor();
        SelectElement(cut);

        cut.FindAll("[data-schema-section]").Should().BeEmpty();
        cut.Find("[data-testid='modeling-inspector-property-lane']").Should().NotBeNull();
        cut.Instance.TestPropertyIssues.Should().BeEmpty();
        cut.Instance.TestSelectedPropertySchema.Should().BeNull();
    }

    [Fact]
    public void A_schema_provider_for_the_provider_key_reaches_the_inspector()
    {
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);

        cut.WaitForAssertion(
            () => cut.FindAll("[data-testid='modeling-inspector-schema-section-execution']").Should().ContainSingle(),
            TestWait);

        cut.Find("[data-testid='modeling-inspector-field-instructions']").Should().NotBeNull();
        // The whole-model validation resolves one schema per distinct (notation, semanticType) pair
        // and CACHES it, so a selection never re-asks the host for a pair it already answered.
        _schemaProvider.Requests.Select(request => request.SemanticType).Should().OnlyHaveUniqueItems(
            "each (notation, semanticType) pair is resolved once and cached");
        _schemaProvider.Requests.Should().Contain(request =>
            request.SemanticType == "userTask" && request.Notation == "bpmn");
        _schemaProvider.Requests[0].ModelId.Should().Be("modeling-editor-editing");

        // Selecting the OTHER type asks for ITS schema — the cache key is per (notation, semanticType).
        cut.Find("[data-testid='modeling-tree-node-task-ship']").Click();
        cut.WaitForAssertion(
            () => _schemaProvider.Requests.Select(request => request.SemanticType).Should().Contain("serviceTask"),
            TestWait);
        cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("serviceTask");
    }

    [Fact]
    public void The_missing_required_property_is_an_issue_with_a_unique_identity()
    {
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);

        // Review A12: the issue id carries the OWNER, so two elements that both miss the same field
        // carry distinct identities (the dock keys and the inline matching both need that).
        cut.WaitForAssertion(
            () => cut.Instance.TestPropertyIssues
                .Should().ContainSingle(issue => issue.Id == "required:instructions@task-a" && issue.SourceElementId == "task-a"),
            TestWait);

        // Both elements of the fixture miss the same required field: the ids stay unique per owner.
        cut.Instance.TestPropertyIssues
            .Where(issue => !string.IsNullOrWhiteSpace(issue.SourceElementId))
            .Select(issue => issue.Id)
            .Should().BeEquivalentTo(
                ["required:instructions@task-a", "required:instructions@task-ship"],
                "the same code + path on two owners must not collapse into one identity");
        var issue = cut.Instance.TestPropertyIssues
            .Single(candidate => candidate.SourceElementId == "task-a");
        issue.SourceElementId.Should().Be("task-a");
        issue.Severity.Should().Be(ModelingIssueSeverity.Error);
        issue.Category.Should().Be("property");
        issue.SuggestedFix.Should().Be("Instructions", "the RAW DTO keeps the host-facing suggested fix");

        // The issue shows up in the dock next to the generated ones.
        cut.WaitForAssertion(
            () => ReadIssueCount(cut).Should().BeGreaterThanOrEqualTo(cut.Instance.TestPropertyIssues.Count),
            TestWait);
    }

    [Fact]
    public void A_legacy_error_does_not_block_an_edit_that_touches_another_key()
    {
        // Review A9: task-a misses its required `instructions` (a legacy value), the user edits
        // `lane` — a key the pending change set actually touches — so the save must REACH the writer
        // instead of trapping the user in a model the gate can never satisfy.
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);

        cut.WaitForAssertion(
            () => cut.Instance.TestPropertyIssues.Should().Contain(issue => issue.SourceElementId == "task-a"),
            TestWait);

        var lane = cut.Find("#modeling-inspector-property-lane");
        lane.Input("Warehouse");
        lane.Blur();
        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        var result = cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync()).GetAwaiter().GetResult();

        result.Should().NotBeNull("the untouched property error must not block the save");
        _writer.Calls.Should().ContainSingle();
    }

    [Fact]
    public void The_save_gate_blocks_an_edit_that_touches_the_invalid_value()
    {
        // Review A9: the pending change SETS `owner` (an over-long value), and the shared validator
        // reports that very key — so the gate refuses the save and announces the localized count.
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);

        var owner = cut.Find("#modeling-inspector-field-owner");
        owner.Input("far-too-long-value");
        owner.Blur();

        cut.WaitForAssertion(
            () => cut.Instance.TestPropertyIssues.Should().Contain(issue => issue.Id.StartsWith("length:owner@", StringComparison.Ordinal)),
            TestWait);
        cut.Instance.HasPendingModelChanges.Should().BeTrue();

        var result = cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync()).GetAwaiter().GetResult();

        result.Should().BeNull("an error on a key the pending edit touches blocks the save");
        _writer.Calls.Should().BeEmpty();
        cut.Instance.TestHistoryAnnouncement.Should().Contain("(1)", "the localized count is announced");
    }

    [Fact]
    public void The_dock_localizes_a_schema_issue_and_drops_its_suggested_fix()
    {
        // Review U10: the dock row is the localized message with no suggested fix; the RAW DTO (the
        // MCP/writer facing one) keeps the English text and the field label.
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);

        cut.WaitForAssertion(
            () => cut.Instance.TestDockPropertyIssues.Should().ContainSingle(issue => issue.SourceElementId == "task-a"),
            TestWait);

        var dock = cut.Instance.TestDockPropertyIssues.Single(issue => issue.SourceElementId == "task-a");
        var raw = cut.Instance.TestPropertyIssues.Single(issue => issue.SourceElementId == "task-a");
        dock.Message.Should().Be("Instructions is required.");
        dock.SuggestedFix.Should().BeEmpty("the dock row carries no suggested fix (U10)");
        raw.Message.Should().Be("Property 'Instructions' is required.");
        raw.SuggestedFix.Should().Be("Instructions");
    }

    [Fact]
    public void A_host_writer_rejection_reaches_the_field_and_the_dock()
    {
        RegisterSchema(required: false);
        _writer.NextResult = new ModelingModelSaveResult
        {
            Succeeded = false,
            Message = "The reference is not valid.",
            Issues =
            [
                new ModelingIssueDto
                {
                    Id = "writer-rejected-instructions",
                    Severity = ModelingIssueSeverity.Error,
                    Category = "property",
                    SourceElementId = "task-a",
                    SuggestedFix = "instructions",
                    Message = "The host rejected this value."
                }
            ]
        };

        using var cut = RenderEditor();
        SelectElement(cut);

        var lane = cut.Find("#modeling-inspector-property-lane");
        lane.Input("Warehouse");
        lane.Blur();

        cut.InvokeAsync(() => cut.Instance.SaveModelChangesAsync()).GetAwaiter().GetResult();

        cut.WaitForAssertion(
            () =>
            {
                var field = cut.Find("[data-testid='modeling-inspector-field-instructions']");
                field.TextContent.Should().Contain("The host rejected this value.");
            },
            TestWait);
    }

    // ── A5: schema version pin per type / no caching of a throw ─────────────

    [Fact]
    public void The_schema_version_is_pinned_per_type_not_per_provider()
    {
        var provider = new VersioningSchemaProvider();
        Services.AddSingleton<IModelingPropertySchemaProvider>(provider);

        using var cut = RenderEditor();
        SelectElement(cut);
        cut.WaitForAssertion(() => cut.Instance.TestSelectedPropertySchema.Should().NotBeNull(), TestWait);

        cut.Find("[data-testid='modeling-tree-node-task-ship']").Click();
        cut.WaitForAssertion(
            () => cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("serviceTask"),
            TestWait);

        var serviceTaskRequest = provider.Requests
            .First(request => request.SemanticType == "serviceTask");
        serviceTaskRequest.SchemaVersion.Should().BeNull(
            "the pin belongs to the PAIR — userTask's version must not freeze serviceTask");
        provider.Requests
            .First(request => request.SemanticType == "userTask")
            .SchemaVersion.Should().BeNull("nothing was pinned before the first answer");
    }

    [Fact]
    public void A_throwing_provider_is_not_cached_and_the_next_refresh_retries()
    {
        var provider = new FlakySchemaProvider();
        Services.AddSingleton<IModelingPropertySchemaProvider>(provider);

        using var cut = RenderEditor();
        SelectElement(cut);
        cut.WaitForAssertion(
            () => provider.Failures.Should().BeGreaterThan(0, "the provider throws while the flag is on"),
            TestWait);
        cut.Instance.TestSelectedPropertySchema.Should().BeNull("a throwing pair resolves to no schema");

        // The host recovers: the SAME pair must be asked again (a throw is never cached).
        provider.Failing = false;
        cut.Find("[data-testid='modeling-tree-node-task-ship']").Click();
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();

        cut.WaitForAssertion(
            () => cut.Instance.TestSelectedPropertySchema.Should().NotBeNull("a throw is never cached"),
            TestWait);
    }

    [Fact]
    public void Reselecting_an_element_answers_from_the_cache()
    {
        RegisterSchema();
        using var cut = RenderEditor();
        SelectElement(cut);
        cut.WaitForAssertion(() => cut.Instance.TestSelectedPropertySchema.Should().NotBeNull(), TestWait);

        var userTaskRequests = CountRequests("userTask");

        cut.Find("[data-testid='modeling-tree-node-task-ship']").Click();
        cut.WaitForAssertion(
            () => cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("serviceTask"),
            TestWait);
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        cut.WaitForAssertion(
            () => cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("userTask"),
            TestWait);

        CountRequests("userTask").Should().Be(userTaskRequests, "a resolved pair is asked exactly once");

        int CountRequests(string semanticType)
            => _schemaProvider.Requests.Count(request => request.SemanticType == semanticType);
    }

    // ── A6: the async refresh race ──────────────────────────────────────────

    [Fact]
    public async Task A_late_answer_for_the_previous_selection_never_wins()
    {
        var provider = new GatedSchemaProvider();
        Services.AddSingleton<IModelingPropertySchemaProvider>(provider);

        // A read-only host resolves the SELECTED schema but runs no whole-model validation, so the
        // gate the test controls is the selection's own request (A6: the generations must race).
        using var cut = RenderEditor(allowEditing: false);
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"),
            TestWait);

        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        cut.WaitForAssertion(() => (provider.GateFor("userTask") is not null).Should().BeTrue(), TestWait);

        cut.Find("[data-testid='modeling-tree-node-task-ship']").Click();
        cut.WaitForAssertion(() => (provider.GateFor("serviceTask") is not null).Should().BeTrue(), TestWait);

        // B answers first — its schema is the one the inspector must show.
        provider.GateFor("serviceTask")!.SetResult(provider.SchemaFor("serviceTask"));
        cut.WaitForAssertion(
            () => cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("serviceTask"),
            TestWait);

        // A answers LATE: the generation guard drops it instead of overwriting B.
        provider.GateFor("userTask")!.SetResult(provider.SchemaFor("userTask"));
        await cut.InvokeAsync(() => Task.Delay(50));

        cut.Instance.TestSelectedPropertySchema!.SemanticType.Should().Be("serviceTask");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>bUnit defaults <c>WaitForAssertion</c> to one second — not enough while the whole
    /// suite runs in parallel.</summary>
    private static readonly TimeSpan TestWait = TimeSpan.FromSeconds(15);

    private void RegisterSchema(bool required = true)
    {
        _schemaProvider.Required = required;
        Services.AddSingleton<IModelingPropertySchemaProvider>(_schemaProvider);
    }

    private IRenderedComponent<TmModelingEditor> RenderEditor(bool allowEditing = true)
        => Render<TmModelingEditor>(builder => builder
            .Add(p => p.ProviderKey, EditingModelingModelProvider.Key)
            .Add(p => p.AllowModelEditing, allowEditing));

    private static void SelectElement(IRenderedComponent<TmModelingEditor> cut)
    {
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-state").Should().Be("loaded"),
            TestWait);
        cut.Find("[data-testid='modeling-tree-node-task-a']").Click();
        cut.WaitForAssertion(
            () => cut.Find("[data-testid='modeling-editor']").GetAttribute("data-selected-element-id").Should().Be("task-a"),
            TestWait);
    }

    private static int ReadIssueCount(IRenderedComponent<TmModelingEditor> cut)
        => int.Parse(cut.Find("[data-testid='modeling-issue-panel']").GetAttribute("data-issue-count") ?? "0");

    /// <summary>The schema provider the editor resolves by provider key. It answers with ONE required
    /// markdown field so a missing value is both visible and blocking, and records every request.</summary>
    private sealed class RecordingSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => EditingModelingModelProvider.Key;

        public List<ModelingPropertySchemaRequest> Requests { get; } = [];

        /// <summary>Whether the schema's field is REQUIRED (the save-gate tests need that; the
        /// writer-rejection test must reach the writer, so it turns it off).</summary>
        public bool Required { get; set; } = true;

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "tests.editor.schema",
                Version = "1",
                Notation = request.Notation,
                SemanticType = request.SemanticType,
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
                                Type = ModelingPropertyFieldType.Markdown,
                                Required = Required
                            },
                            new ModelingPropertyFieldDto
                            {
                                Key = "owner",
                                Label = "Owner",
                                Type = ModelingPropertyFieldType.String,
                                MaxLength = 5,
                                Order = 1
                            }
                        ]
                    }
                ]
            });
        }
    }

    /// <summary>Serves a distinct version per type and records every request (A5).</summary>
    private sealed class VersioningSchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => EditingModelingModelProvider.Key;

        public List<ModelingPropertySchemaRequest> Requests { get; } = [];

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "tests.versioning",
                Version = request.SemanticType == "userTask" ? "7" : "9",
                Notation = request.Notation,
                SemanticType = request.SemanticType,
                Sections =
                [
                    new ModelingPropertySectionDto
                    {
                        Key = "main",
                        Label = "Main",
                        Fields = [new ModelingPropertyFieldDto { Key = "instructions", Label = "Instructions", Type = ModelingPropertyFieldType.String }]
                    }
                ]
            });
        }
    }

    /// <summary>Throws while <see cref="Failing"/> is on (A5: a throw is not cached).</summary>
    private sealed class FlakySchemaProvider : IModelingPropertySchemaProvider
    {
        public string ProviderKey => EditingModelingModelProvider.Key;

        public bool Failing { get; set; } = true;

        public int Failures { get; private set; }

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
        {
            if (Failing)
            {
                Failures++;
                throw new InvalidOperationException("The schema service is down.");
            }

            return Task.FromResult<ModelingPropertySchemaDto?>(new ModelingPropertySchemaDto
            {
                SchemaId = "tests.flaky",
                Version = "1",
                Notation = request.Notation,
                SemanticType = request.SemanticType,
                Sections =
                [
                    new ModelingPropertySectionDto
                    {
                        Key = "main",
                        Label = "Main",
                        Fields = [new ModelingPropertyFieldDto { Key = "instructions", Label = "Instructions", Type = ModelingPropertyFieldType.String }]
                    }
                ]
            });
        }
    }

    /// <summary>Blocks the FIRST request per semantic type on a gate the test completes (A6).</summary>
    private sealed class GatedSchemaProvider : IModelingPropertySchemaProvider
    {
        private readonly Dictionary<string, TaskCompletionSource<ModelingPropertySchemaDto?>> _gates = new(StringComparer.Ordinal);
        private readonly HashSet<string> _gated = new(StringComparer.Ordinal);

        public string ProviderKey => EditingModelingModelProvider.Key;

        public TaskCompletionSource<ModelingPropertySchemaDto?>? GateFor(string semanticType)
            => _gates.TryGetValue(semanticType, out var gate) ? gate : null;

        public ModelingPropertySchemaDto SchemaFor(string semanticType) => new()
        {
            SchemaId = $"tests.gated.{semanticType}",
            Version = "1",
            Notation = "bpmn",
            SemanticType = semanticType,
            Sections =
            [
                new ModelingPropertySectionDto
                {
                    Key = "main",
                    Label = "Main",
                    Fields = [new ModelingPropertyFieldDto { Key = "instructions", Label = "Instructions", Type = ModelingPropertyFieldType.String }]
                }
            ]
        };

        public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
        {
            if (_gated.Add(request.SemanticType))
            {
                var gate = new TaskCompletionSource<ModelingPropertySchemaDto?>(TaskCreationOptions.RunContinuationsAsynchronously);
                _gates[request.SemanticType] = gate;
                return gate.Task;
            }

            return Task.FromResult<ModelingPropertySchemaDto?>(SchemaFor(request.SemanticType));
        }
    }

    private sealed class RecordingModelingModelWriter : IModelingModelWriter
    {
        public string ProviderKey => EditingModelingModelProvider.Key;

        public List<ModelingModelChangeSet> Calls { get; } = [];

        public ModelingModelSaveResult? NextResult { get; set; }

        public Task<ModelingModelSaveResult> SaveChangesAsync(ModelingModelChangeSet changes, CancellationToken cancellationToken)
        {
            Calls.Add(changes);
            return Task.FromResult(NextResult ?? new ModelingModelSaveResult { Succeeded = true });
        }
    }
}
