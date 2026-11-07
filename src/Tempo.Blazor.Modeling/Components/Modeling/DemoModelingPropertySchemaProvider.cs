using System.Globalization;
using System.Text.Json;
using Tempo.Blazor.Modeling;

namespace Tempo.Blazor.Components.Modeling;

/// <summary>
/// Demo property-schema provider (phase 5, decision <c>MODELING-PROPERTY-SCHEMA</c>): the BPMN
/// "step attributes" schema the demo page exercises — instructions (markdown), the owning role
/// (entity reference with a host lookup), the used systems (entity reference list), the SLA
/// (duration stored in MINUTES), the priority (enum), the automation mode (enum) and the exception
/// list (structured list with its own required item fields).
/// <para>
/// Registered automatically by <c>AddTempoBlazorModeling</c> for the
/// <see cref="DemoModelingModelProvider"/> key, but it answers ONLY for
/// <see cref="ModelIdValue"/>. Every pre-existing demo scenario therefore keeps the 2.10.0
/// inspector byte-for-byte — the backward-compatibility face of the decision is visible in the demo
/// itself, not just in tests.
/// </para>
/// </summary>
public sealed class DemoModelingPropertySchemaProvider : IModelingPropertySchemaProvider
{
    /// <summary>The model id of the phase-5 demo scenario (<c>?scenario=step-attributes</c>).</summary>
    public const string ModelIdValue = "demo-bpmn-step-attributes";

    /// <summary>The schema identity the demo serves.</summary>
    public const string SchemaIdValue = "tempo.demo.bpmn.step";

    /// <summary>The schema version the demo serves (the editor pins it per session).</summary>
    public const string SchemaVersionValue = "1";

    /// <summary>The element types the step schema applies to (PascalCase per <c>BpmnModelingCatalog</c>;
    /// the demo models spell them lowercase, so the comparison is case-insensitive).</summary>
    private static readonly HashSet<string> StepElementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Task",
        "UserTask",
        "ServiceTask",
        "ManualTask",
        "BusinessRuleTask",
        "ScriptTask",
        "SendTask",
        "ReceiveTask"
    };

    /// <inheritdoc />
    public string ProviderKey => DemoModelingModelProvider.ProviderKeyValue;

    /// <inheritdoc />
    public Task<ModelingPropertySchemaDto?> GetSchemaAsync(ModelingPropertySchemaRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Resolve(request.ModelId, request.Notation, request.SemanticType));
    }

    /// <summary>The synchronous resolver the demo writer hands to
    /// <see cref="ModelingOperationContext.ResolvePropertySchema"/> so a demo save validates the SAME
    /// values the inspector did (the engine path).</summary>
    public static ModelingPropertySchemaDto? Resolve(string? modelId, string? notation, string? semanticType)
    {
        if (!string.Equals(modelId, ModelIdValue, StringComparison.Ordinal)
            || !string.Equals(notation, "bpmn", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(semanticType)
            || !StepElementTypes.Contains(semanticType))
        {
            return null;
        }

        return CreateStepSchema(semanticType);
    }

    /// <summary>The step-attribute schema of one BPMN task type.</summary>
    public static ModelingPropertySchemaDto CreateStepSchema(string semanticType) => new()
    {
        SchemaId = SchemaIdValue,
        Version = SchemaVersionValue,
        Notation = "bpmn",
        SemanticType = semanticType,
        Sections =
        [
            new ModelingPropertySectionDto
            {
                Key = "instructions",
                Label = "Instructions",
                Help = "How the step is executed, in markdown.",
                Order = 0,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "instructions",
                        Label = "Instructions",
                        Type = ModelingPropertyFieldType.Markdown,
                        Required = true,
                        MaxLength = 4000,
                        Placeholder = "What does the step do?",
                        Help = "Required — a step without instructions cannot be handed over.",
                        Order = 0
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "automation",
                        Label = "Automation",
                        Type = ModelingPropertyFieldType.Enum,
                        Order = 1,
                        DefaultValue = JsonSerializer.SerializeToElement("manual"),
                        Options =
                        [
                            new() { Value = "manual", Label = "Manual" },
                            new() { Value = "semi-automatic", Label = "Semi-automatic" },
                            new() { Value = "automatic", Label = "Automatic" }
                        ]
                    }
                ]
            },
            new ModelingPropertySectionDto
            {
                Key = "ownership",
                Label = "Ownership",
                Help = "Who executes the step and which systems it touches.",
                Order = 1,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "role",
                        Label = "Role",
                        Type = ModelingPropertyFieldType.EntityReference,
                        Required = true,
                        Help = "Required — the responsible process role.",
                        Order = 0,
                        Lookup = new ModelingLookupDescriptor
                        {
                            LookupType = "role",
                            MinQueryLength = 2,
                            PageSize = 10,
                            SearchRequired = false
                        }
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "systems",
                        Label = "Systems",
                        Type = ModelingPropertyFieldType.EntityReferenceList,
                        Help = "The systems the step uses, in order.",
                        Order = 1,
                        Lookup = new ModelingLookupDescriptor
                        {
                            LookupType = "system",
                            MinQueryLength = 2,
                            PageSize = 20,
                            SearchRequired = false
                        }
                    }
                ]
            },
            new ModelingPropertySectionDto
            {
                Key = "service-level",
                Label = "Service level",
                Order = 2,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "sla",
                        Label = "SLA",
                        Type = ModelingPropertyFieldType.Duration,
                        Min = 15,
                        Max = 10080,
                        Unit = "min",
                        Help = "Target time for the step; stored in minutes.",
                        Order = 0
                    },
                    new ModelingPropertyFieldDto
                    {
                        Key = "priority",
                        Label = "Priority",
                        Type = ModelingPropertyFieldType.Enum,
                        Order = 1,
                        Options =
                        [
                            new() { Value = "low", Label = "Low" },
                            new() { Value = "normal", Label = "Normal" },
                            new() { Value = "high", Label = "High" },
                            new() { Value = "critical", Label = "Critical" }
                        ]
                    }
                ]
            },
            new ModelingPropertySectionDto
            {
                Key = "exceptions",
                Label = "Exceptions",
                Help = "Known deviations and how to handle them.",
                Order = 3,
                Fields =
                [
                    new ModelingPropertyFieldDto
                    {
                        Key = "exceptions",
                        Label = "Exceptions",
                        Type = ModelingPropertyFieldType.StructuredList,
                        Order = 0,
                        ItemFields =
                        [
                            new ModelingPropertyFieldDto
                            {
                                Key = "code",
                                Label = "Code",
                                Type = ModelingPropertyFieldType.String,
                                Required = true,
                                MaxLength = 20,
                                Order = 0
                            },
                            new ModelingPropertyFieldDto
                            {
                                Key = "description",
                                Label = "Description",
                                Type = ModelingPropertyFieldType.MultilineText,
                                Required = true,
                                Order = 1
                            },
                            new ModelingPropertyFieldDto
                            {
                                Key = "severity",
                                Label = "Severity",
                                Type = ModelingPropertyFieldType.Enum,
                                Order = 2,
                                Options =
                                [
                                    new() { Value = "warning", Label = "Warning" },
                                    new() { Value = "blocker", Label = "Blocker" }
                                ]
                            }
                        ]
                    }
                ]
            }
        ]
    };

    /// <summary>
    /// The phase-5 demo scenario model: four order steps where <c>Validate order</c> carries every
    /// attribute (including a role/system snapshot and one exception) and <c>Approve discount</c>
    /// deliberately misses the two REQUIRED fields, so the inspector's required-field error is
    /// reachable from one deep link.
    /// </summary>
    public static ModelingModelDto CreateStepAttributesModel()
    {
        const string snapshot = "2026-11-06T09:24:33Z";

        ModelingElementDto Task(string id, string semanticType, string name, string description, Dictionary<string, JsonElement> properties)
            => new()
            {
                Id = id,
                SourceId = $"demo/steps/{id}",
                SourceType = "bpmn-task",
                SourcePath = $"/Order fulfillment/{name}",
                Notation = "bpmn",
                SemanticType = semanticType,
                Name = name,
                Description = description,
                Tags = ["bpmn", "step", "phase-5"],
                Properties = properties,
                Governance = new ModelingGovernanceDto
                {
                    TrustLevel = "demo",
                    ReviewState = "authored",
                    SyncState = "fresh",
                    DataSource = "Tempo.Blazor Demo"
                }
            };

        var model = new ModelingModelDto
        {
            Id = ModelIdValue,
            Title = "BPMN steps with schema attributes",
            Notation = "bpmn",
            SupportedNotations = ["bpmn"],
            Metadata = new ModelingMetadataDto
            {
                SourceSystem = "Tempo.Blazor Demo",
                SourceVersion = "2026.11-steps",
                LoadedAt = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero),
                IsFresh = true
            },
            Elements =
            [
                new()
                {
                    Id = "step-receive",
                    SourceId = "demo/steps/receive",
                    SourceType = "bpmn-event",
                    SourcePath = "/Order fulfillment/Order received",
                    Notation = "bpmn",
                    SemanticType = "startEvent",
                    Name = "Order received",
                    Description = "Customer order has been received."
                },
                Task(
                    "step-validate",
                    "userTask",
                    "Validate order",
                    "Sales checks order details and availability.",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["instructions"] = JsonSerializer.SerializeToElement(
                            "# Validate the order\n\nCheck the customer, the **stock** and the price.\n\n> Escalate large discounts."),
                        ["automation"] = JsonSerializer.SerializeToElement("semi-automatic"),
                        ["role"] = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>
                        {
                            ["type"] = JsonSerializer.SerializeToElement("role"),
                            ["id"] = JsonSerializer.SerializeToElement("role-sales"),
                            ["display"] = JsonSerializer.SerializeToElement("Sales"),
                            ["snapshotAt"] = JsonSerializer.SerializeToElement(snapshot)
                        }),
                        ["systems"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, JsonElement>
                            {
                                ["type"] = JsonSerializer.SerializeToElement("system"),
                                ["id"] = JsonSerializer.SerializeToElement("sys-erp"),
                                ["display"] = JsonSerializer.SerializeToElement("ERP"),
                                ["snapshotAt"] = JsonSerializer.SerializeToElement(snapshot)
                            },
                            new Dictionary<string, JsonElement>
                            {
                                ["type"] = JsonSerializer.SerializeToElement("system"),
                                ["id"] = JsonSerializer.SerializeToElement("sys-crm"),
                                ["display"] = JsonSerializer.SerializeToElement("CRM"),
                                ["snapshotAt"] = JsonSerializer.SerializeToElement(snapshot)
                            }
                        }),
                        ["sla"] = JsonSerializer.SerializeToElement(240),
                        ["priority"] = JsonSerializer.SerializeToElement("high"),
                        ["exceptions"] = JsonSerializer.SerializeToElement(new[]
                        {
                            new Dictionary<string, JsonElement>
                            {
                                ["code"] = JsonSerializer.SerializeToElement("E-STOCK"),
                                ["description"] = JsonSerializer.SerializeToElement("Stock is not available — ask the warehouse."),
                                ["severity"] = JsonSerializer.SerializeToElement("warning")
                            }
                        })
                    }),
                Task(
                    "step-approve",
                    "userTask",
                    "Approve discount",
                    "The sales manager approves a discount above the threshold.",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["priority"] = JsonSerializer.SerializeToElement("critical")
                    }),
                Task(
                    "step-ship",
                    "serviceTask",
                    "Ship order",
                    "The warehouse service prepares and ships the package.",
                    new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["instructions"] = JsonSerializer.SerializeToElement("Pack the order and hand it to the carrier."),
                        ["role"] = JsonSerializer.SerializeToElement(new Dictionary<string, JsonElement>
                        {
                            ["type"] = JsonSerializer.SerializeToElement("role"),
                            ["id"] = JsonSerializer.SerializeToElement("role-warehouse"),
                            ["display"] = JsonSerializer.SerializeToElement("Warehouse"),
                            ["snapshotAt"] = JsonSerializer.SerializeToElement(snapshot)
                        })
                    })
            ],
            Relationships =
            [
                new()
                {
                    Id = "step-flow-receive-validate",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "step-receive",
                    TargetElementId = "step-validate",
                    Name = "Start validation"
                },
                new()
                {
                    Id = "step-flow-validate-approve",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "step-validate",
                    TargetElementId = "step-approve",
                    Name = "Discount above the threshold"
                },
                new()
                {
                    Id = "step-flow-approve-ship",
                    RelationshipType = "SequenceFlow",
                    SourceElementId = "step-approve",
                    TargetElementId = "step-ship",
                    Name = "Approved"
                }
            ],
            Views =
            [
                new ModelingViewDto
                {
                    Id = "demo-bpmn-step-attributes-view",
                    Name = "Order steps",
                    Notation = "bpmn",
                    ViewpointKey = "process",
                    Nodes =
                    [
                        new() { ElementId = "step-receive", X = 80, Y = 220, Width = 150, Height = 90 },
                        new() { ElementId = "step-validate", X = 300, Y = 200, Width = 180, Height = 100 },
                        new() { ElementId = "step-approve", X = 550, Y = 200, Width = 180, Height = 100 },
                        new() { ElementId = "step-ship", X = 800, Y = 200, Width = 180, Height = 100 }
                    ],
                    Connections =
                    [
                        new() { RelationshipId = "step-flow-receive-validate" },
                        new() { RelationshipId = "step-flow-validate-approve" },
                        new() { RelationshipId = "step-flow-approve-ship" }
                    ]
                }
            ]
        };

        return model;
    }

    /// <summary>The culture a lookup request answers in (unused by the demo catalogue, kept so the
    /// signature mirrors a real host).</summary>
    internal static string EffectiveCulture(string? culture)
        => string.IsNullOrWhiteSpace(culture) ? CultureInfo.CurrentUICulture.Name : culture;
}
