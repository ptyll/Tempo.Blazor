using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Sample data of the <c>/ai-agents</c> demo page: the plan the agent proposes, the run tree of its three
/// sub-agents, the fictional model catalogue, the context-window snapshot and the prompt library.
/// </summary>
/// <remarks>
/// These are demo DATA, not user-interface text — the same way the per-component pages ship sample runs. Every
/// label, heading, hint and state sentence on the page itself resolves through <c>ITmLocalizer</c>. The model
/// names, providers and prices are invented on purpose: the page states that nothing there is a real bill and
/// nothing calls a provider.
/// </remarks>
internal static class AiAgentsPageSamples
{
    /// <summary>
    /// The same run as a LANE view: four agents, their work blocks and the handoffs between them. One block is still
    /// OPEN (no end) so the surface's live "now" line and its ticking are visible, and one carries an approval marker
    /// so the verdict chip renders in the sketch.
    /// </summary>
    internal static AgentLanesSnapshot LanesSnapshot(Func<string, string> loc, DateTimeOffset now)
    {
        AgentLaneBlock Block(
            string id,
            string agentId,
            int startMinutes,
            int? endMinutes,
            AgentLaneBlockKind kind,
            AgentActivityStatus status,
            string label,
            AgentLaneBlockMarker? marker = null)
            => new(id, agentId, now.AddMinutes(startMinutes), label)
            {
                End = endMinutes is { } end ? now.AddMinutes(end) : null,
                Kind = kind,
                Status = status,
                Marker = marker,
            };

        return new AgentLanesSnapshot
        {
            Sequence = 7,
            Agents =
            [
                new AgentRef("Planner", "planner") { Role = "Orchestrator", ColorIndex = 0 },
                new AgentRef("Retriever", "retriever") { Role = "Worker", ColorIndex = 1 },
                new AgentRef("Reconciler", "reconciler") { Role = "Worker", ColorIndex = 2 },
                new AgentRef("Reviewer", "reviewer") { Role = "Reviewer", ColorIndex = 3 },
            ],
            Blocks =
            [
                Block("b-plan", "planner", -40, -30, AgentLaneBlockKind.Task, AgentActivityStatus.Succeeded,
                    "Plan the reconciliation", AgentLaneBlockMarker.Approval),
                Block("b-fetch", "retriever", -31, -18, AgentLaneBlockKind.Tool, AgentActivityStatus.Succeeded,
                    "Pull the ledger exports"),
                Block("b-match", "retriever", -17, -6, AgentLaneBlockKind.Message, AgentActivityStatus.Succeeded,
                    "Match invoices to lines"),
                Block("b-diff", "reconciler", -18, -4, AgentLaneBlockKind.Task, AgentActivityStatus.Failed,
                    "Reconcile the differences", AgentLaneBlockMarker.ChangesRequested),
                Block("b-review", "reviewer", -5, 0, AgentLaneBlockKind.Review, AgentActivityStatus.WaitingForApproval,
                    "Approve the write-off", AgentLaneBlockMarker.Review),
                Block("b-write", "reconciler", -3, null, AgentLaneBlockKind.Tool, AgentActivityStatus.Running,
                    "Write the adjustment"),
                Block("b-summary", "planner", 0, 4, AgentLaneBlockKind.Task, AgentActivityStatus.Queued,
                    "Summarise for the room"),
            ],
            Handoffs =
            [
                new AgentHandoffLink("retriever", now.AddMinutes(-30)) { FromBlockId = "b-plan", Label = "Plan ready" },
                new AgentHandoffLink("reconciler", now.AddMinutes(-18)) { FromAgentId = "retriever" },
                new AgentHandoffLink("reviewer", now.AddMinutes(-4)) { FromBlockId = "b-diff" },
                new AgentHandoffLink("reconciler", now.AddMinutes(1)) { FromAgentId = "reviewer", Label = "Approved" },
            ],
        };
    }

    /// <summary>The four steps of the reviewed plan, in display order.</summary>
    internal static IReadOnlyList<AgentPlanStep> ReviewSteps(Func<string, string> loc) =>
    [
        new AgentPlanStep(loc("TmAiAgentsDemo_StepCollect"), "collect"),
        new AgentPlanStep(loc("TmAiAgentsDemo_StepMatch"), "match"),
        new AgentPlanStep(loc("TmAiAgentsDemo_StepReconcile"), "reconcile"),
        new AgentPlanStep(loc("TmAiAgentsDemo_StepPublish"), "publish"),
    ];

    /// <summary>
    /// One run of the reconciliation agent: a root, three sub-agents under it and the tool calls each sub-agent
    /// made. The flat list becomes a treegrid from <see cref="AgentRunNode.ParentId"/>; the root row rolls the
    /// whole subtree up.
    /// </summary>
    internal static IReadOnlyList<AgentRunNode> RunNodes(Func<string, string> loc, DateTimeOffset now)
    {
        var start = now - TimeSpan.FromMinutes(2);

        return
        [
            new AgentRunNode(loc("TmAiAgentsDemo_TreeRoot"), "root")
            {
                Kind = AgentRunNodeKind.Agent,
                Status = AgentStepStatus.Running,
                Model = "Aurora Pro",
                StartedAt = start,
                CostMicroUnits = 1_400_000,
                Tokens = 1_600,
            },
            new AgentRunNode(loc("TmAiAgentsDemo_StepCollect"), "collector")
            {
                ParentId = "root",
                Kind = AgentRunNodeKind.SubAgent,
                Status = AgentStepStatus.Done,
                Model = "Aurora Flash",
                StartedAt = start.AddSeconds(2),
                CompletedAt = start.AddSeconds(24),
                CostMicroUnits = 320_000,
                Tokens = 300,
            },
            new AgentRunNode(loc("TmAiAgentsDemo_TreeReadPlan"), "collector-plan")
            {
                ParentId = "collector",
                Kind = AgentRunNodeKind.Tool,
                Status = AgentStepStatus.Done,
                StartedAt = start.AddSeconds(2),
                CompletedAt = start.AddSeconds(6),
                CostMicroUnits = 20_000,
                Tokens = 40,
            },
            new AgentRunNode(loc("TmAiAgentsDemo_StepMatch"), "matcher")
            {
                ParentId = "root",
                Kind = AgentRunNodeKind.SubAgent,
                Status = AgentStepStatus.Failed,
                Model = "Aurora Pro",
                StartedAt = start.AddSeconds(24),
                CompletedAt = start.AddSeconds(58),
                CostMicroUnits = 640_000,
                Tokens = 780,
                Error = loc("TmAiAgentsDemo_TreeMatchError"),
            },
            new AgentRunNode(loc("TmAiAgentsDemo_TreeLookup"), "matcher-lookup")
            {
                ParentId = "matcher",
                Kind = AgentRunNodeKind.Tool,
                Status = AgentStepStatus.Failed,
                StartedAt = start.AddSeconds(30),
                CompletedAt = start.AddSeconds(58),
                Error = loc("TmAiAgentsDemo_TreeLookupError"),
            },
            new AgentRunNode(loc("TmAiAgentsDemo_StepPublish"), "publisher")
            {
                ParentId = "root",
                Kind = AgentRunNodeKind.SubAgent,
                Status = AgentStepStatus.Pending,
                Model = "Nimbus 2",
            },
            new AgentRunNode(loc("TmAiAgentsDemo_TreeWrite"), "publisher-write")
            {
                ParentId = "publisher",
                Kind = AgentRunNodeKind.Step,
                Status = AgentStepStatus.Pending,
            },
        ];
    }

    /// <summary>
    /// Six FICTIONAL models of the demo catalogue. The prices, scores and windows are illustrative only — the
    /// page marks the catalogue as demo data and never calls a provider. The providers are invented too, so a
    /// reader cannot mistake the numbers for a real price list.
    /// </summary>
    internal static IReadOnlyList<AiModelInfo> Models(Func<string, string> loc) =>
    [
        new AiModelInfo("aurora-pro", "Aurora Pro")
        {
            Provider = "Northwind AI",
            Family = "Aurora",
            Description = loc("TmAiAgentsDemo_ModelAuroraPro"),
            ContextWindow = 200_000,
            SpeedScore = 3,
            QualityScore = 5,
            InputPricePerMillion = 4m,
            OutputPricePerMillion = 16m,
            Capabilities = ["vision", "tools", "json"],
            EffortLevels = Effort(loc, "low", "medium", "high"),
            SupportsExtendedThinking = true,
        },
        new AiModelInfo("aurora-flash", "Aurora Flash")
        {
            Provider = "Northwind AI",
            Family = "Aurora",
            Description = loc("TmAiAgentsDemo_ModelAuroraFlash"),
            ContextWindow = 128_000,
            SpeedScore = 5,
            QualityScore = 3,
            InputPricePerMillion = 0.35m,
            OutputPricePerMillion = 1.4m,
            Capabilities = ["tools", "json"],
            EffortLevels = Effort(loc, "low", "medium"),
        },
        new AiModelInfo("nimbus-2", "Nimbus 2")
        {
            Provider = "Contoso Labs",
            Family = "Nimbus",
            Description = loc("TmAiAgentsDemo_ModelNimbus"),
            ContextWindow = 128_000,
            SpeedScore = 4,
            QualityScore = 4,
            InputPricePerMillion = 1.2m,
            OutputPricePerMillion = 4.8m,
            Capabilities = ["vision", "tools"],
            EffortLevels = Effort(loc, "low", "high"),
            SupportsExtendedThinking = true,
        },
        new AiModelInfo("cirrus-mini", "Cirrus Mini")
        {
            Provider = "Contoso Labs",
            Family = "Nimbus",
            Description = loc("TmAiAgentsDemo_ModelCirrus"),
            ContextWindow = 32_000,
            SpeedScore = 5,
            QualityScore = 2,
            InputPricePerMillion = 0.1m,
            OutputPricePerMillion = 0.4m,
            Capabilities = ["tools"],
        },
        new AiModelInfo("atlas-large", "Atlas Large")
        {
            Provider = "Fabrikam",
            Family = "Atlas",
            Description = loc("TmAiAgentsDemo_ModelAtlas"),
            ContextWindow = 1_000_000,
            SpeedScore = 2,
            QualityScore = 5,
            InputPricePerMillion = 8m,
            OutputPricePerMillion = 24m,
            Capabilities = ["vision", "tools"],
            EffortLevels = Effort(loc, "medium", "high"),
            SupportsExtendedThinking = true,
        },
        new AiModelInfo("local-8b", "Local 8B")
        {
            Family = "Open weights",
            Description = loc("TmAiAgentsDemo_ModelLocal"),
            ContextWindow = 32_000,
            SpeedScore = 5,
            QualityScore = 2,
            InputPricePerMillion = 0m,
            Capabilities = ["offline"],
        },
    ];

    /// <summary>
    /// The context window of the demo conversation: six sections of a 16 000-token budget with a cache hit,
    /// removable entries and an already omitted tool result.
    /// </summary>
    internal static ContextUsageSnapshot Context() => new()
    {
        BudgetTokens = 16_000,
        CacheHit = 0.62,
        Sections =
        [
            new("system", "System prompt") { Kind = ContextUsageKind.System, Tokens = 1_200, IsRequired = true },
            new("tools", "Tools")
            {
                Kind = ContextUsageKind.Tools,
                Tokens = 800,
                CanRemove = true,
                Items =
                [
                    new("search", "Web search") { Tokens = 500, CanRemove = true },
                    new("code", "Code interpreter") { Tokens = 300, CanRemove = true },
                ],
            },
            new("memory", "Memory") { Kind = ContextUsageKind.Memory, Tokens = 400, IsSummarized = true },
            new("files", "Files")
            {
                Kind = ContextUsageKind.Files,
                Tokens = 6_000,
                CanRemove = true,
                Items =
                [
                    new("contract", "Contract.pdf") { Tokens = 4_000, CanRemove = true },
                    new("report", "Report.pdf") { Tokens = 2_000 },
                ],
            },
            new("chat", "Chat history") { Kind = ContextUsageKind.Chat, Tokens = 4_800 },
            new("results", "Tool results") { Kind = ContextUsageKind.Results, Tokens = 600, IsOmitted = true },
        ],
    };

    /// <summary>
    /// The five demo templates. Every title, description, body, variable label and option is localized, because a
    /// template is prose the reader picks by — unlike the technical section labels above, which are data.
    /// </summary>
    internal static IReadOnlyList<PromptTemplate> Templates(Func<string, string> loc) =>
    [
        new PromptTemplate(
            "release-notes",
            loc("TmAiAgentsDemo_TemplateReleaseTitle"),
            loc("TmAiAgentsDemo_TemplateReleaseBody"))
        {
            Category = loc("TmAiAgentsDemo_CategoryEngineering"),
            Description = loc("TmAiAgentsDemo_TemplateReleaseDescription"),
            Variables =
            [
                new PromptVariable("product", loc("TmAiAgentsDemo_VariableProduct")),
                new PromptVariable("version", loc("TmAiAgentsDemo_VariableVersion")),
                new PromptVariable("focus", loc("TmAiAgentsDemo_VariableFocus"))
                {
                    Kind = PromptVariableKind.Select,
                    Options =
                    [
                        loc("TmAiAgentsDemo_FocusFeatures"),
                        loc("TmAiAgentsDemo_FocusFixes"),
                        loc("TmAiAgentsDemo_FocusPerformance"),
                    ],
                    DefaultValue = loc("TmAiAgentsDemo_FocusFeatures"),
                },
            ],
        },
        new PromptTemplate(
            "code-review",
            loc("TmAiAgentsDemo_TemplateReviewTitle"),
            loc("TmAiAgentsDemo_TemplateReviewBody"))
        {
            Category = loc("TmAiAgentsDemo_CategoryEngineering"),
            Description = loc("TmAiAgentsDemo_TemplateReviewDescription"),
            Variables =
            [
                new PromptVariable("diff", loc("TmAiAgentsDemo_VariableDiff"))
                {
                    Kind = PromptVariableKind.Multiline,
                    Description = loc("TmAiAgentsDemo_VariableDiffHint"),
                },
            ],
        },
        new PromptTemplate(
            "customer-reply",
            loc("TmAiAgentsDemo_TemplateReplyTitle"),
            loc("TmAiAgentsDemo_TemplateReplyBody"))
        {
            Category = loc("TmAiAgentsDemo_CategorySupport"),
            Description = loc("TmAiAgentsDemo_TemplateReplyDescription"),
            Variables =
            [
                new PromptVariable("customer", loc("TmAiAgentsDemo_VariableCustomer")),
                new PromptVariable("topic", loc("TmAiAgentsDemo_VariableTopic")),
                new PromptVariable("tone", loc("TmAiAgentsDemo_VariableTone"))
                {
                    Kind = PromptVariableKind.Select,
                    Options = [loc("TmAiAgentsDemo_ToneFriendly"), loc("TmAiAgentsDemo_ToneFormal")],
                    DefaultValue = loc("TmAiAgentsDemo_ToneFriendly"),
                },
            ],
        },
        new PromptTemplate(
            "meeting-summary",
            loc("TmAiAgentsDemo_TemplateSummaryTitle"),
            loc("TmAiAgentsDemo_TemplateSummaryBody"))
        {
            Category = loc("TmAiAgentsDemo_CategoryWriting"),
            Description = loc("TmAiAgentsDemo_TemplateSummaryDescription"),
            Variables =
            [
                new PromptVariable("notes", loc("TmAiAgentsDemo_VariableNotes"))
                {
                    Kind = PromptVariableKind.Multiline,
                },
                new PromptVariable("language", loc("TmAiAgentsDemo_VariableLanguage"))
                {
                    Kind = PromptVariableKind.Select,
                    Options = ["English", "Čeština", "Slovenčina", "Français"],
                    DefaultValue = "English",
                },
            ],
        },
        new PromptTemplate(
            "data-brief",
            loc("TmAiAgentsDemo_TemplateBriefTitle"),
            loc("TmAiAgentsDemo_TemplateBriefBody"))
        {
            Category = loc("TmAiAgentsDemo_CategoryAnalysis"),
            Description = loc("TmAiAgentsDemo_TemplateBriefDescription"),
            Variables =
            [
                new PromptVariable("dataset", loc("TmAiAgentsDemo_VariableDataset")),
                new PromptVariable("audience", loc("TmAiAgentsDemo_VariableAudience")),
            ],
        },
    ];

    /// <summary>The initial draft values of the template the page opens on.</summary>
    internal static IReadOnlyDictionary<string, string?> InitialValues(Func<string, string> loc)
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["product"] = "Tempo.Blazor",
            ["version"] = "2.10.0",
            ["focus"] = loc("TmAiAgentsDemo_FocusFeatures"),
        };

    /// <summary>The effort levels of a model, labelled in the page's language.</summary>
    private static AiModelEffortLevel[] Effort(Func<string, string> loc, params string[] ids)
        => ids.Select(id => new AiModelEffortLevel(id, loc($"TmAiAgentsDemo_Effort{char.ToUpperInvariant(id[0])}{id[1..]}"))).ToArray();
}
