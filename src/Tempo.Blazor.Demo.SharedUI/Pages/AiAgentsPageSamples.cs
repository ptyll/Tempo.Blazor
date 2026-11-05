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

    /// <summary>
    /// The SAME run as a NETWORK: a human, the agents that act, the ledger they read and the two tools they call, with
    /// the call counts and the one node waiting for a decision. The call counts are the ones the lane view shows for
    /// the same work, so the two surfaces never disagree about the run.
    /// </summary>
    internal static AgentGraphSnapshot GraphSnapshot(Func<string, string> loc, DateTimeOffset now)
    {
        AgentGraphNode Node(string id, string label, AgentGraphNodeKind kind, AgentActivityStatus status,
            string? group = null, int color = 0)
            => new(id, label)
            {
                Kind = kind,
                Status = status,
                Group = group,
                Agent = kind == AgentGraphNodeKind.Agent
                    ? new AgentRef(label, id) { ColorIndex = color }
                    : null,
            };

        return new AgentGraphSnapshot
        {
            Sequence = 20,
            Nodes =
            [
                Node("human", loc("TmAiAgentsDemo_GraphHuman"), AgentGraphNodeKind.Human, AgentActivityStatus.Idle),
                Node("planner", "Planner", AgentGraphNodeKind.Agent, AgentActivityStatus.Running,
                    group: loc("TmAiAgentsDemo_GraphGroupCore"), color: 0),
                Node("reconciler", "Reconciler", AgentGraphNodeKind.Agent, AgentActivityStatus.WaitingForApproval,
                    group: loc("TmAiAgentsDemo_GraphGroupCore"), color: 1),
                Node("reviewer", "Reviewer", AgentGraphNodeKind.Agent, AgentActivityStatus.Succeeded, color: 2),
                Node("ledger", loc("TmAiAgentsDemo_GraphLedger"), AgentGraphNodeKind.Resource, AgentActivityStatus.Idle),
                Node("search", loc("TmAiAgentsDemo_GraphSearch"), AgentGraphNodeKind.Tool, AgentActivityStatus.Succeeded),
                Node("code", loc("TmAiAgentsDemo_GraphCode"), AgentGraphNodeKind.Tool, AgentActivityStatus.Idle),
            ],
            Edges =
            [
                new AgentGraphEdge("g-ask", "human", "planner") { CallCount = 1, LastCallAt = now.AddMinutes(-42) },
                new AgentGraphEdge("g-search", "planner", "search")
                {
                    CallCount = 3, LastCallAt = now.AddMinutes(-31), IsActive = true,
                },
                new AgentGraphEdge("g-leger", "planner", "ledger") { CallCount = 12, LastCallAt = now.AddMinutes(-18) },
                new AgentGraphEdge("g-delegate", "planner", "reconciler") { CallCount = 1, LastCallAt = now.AddMinutes(-17) },
                new AgentGraphEdge("g-code", "reconciler", "code") { CallCount = 5, LastCallAt = now.AddMinutes(-4) },
                new AgentGraphEdge("g-review", "reviewer", "ledger") { CallCount = 2, LastCallAt = now.AddMinutes(-6) },
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

    /// <summary>
    /// The fan-out of the <c>/ai-agents</c> demo: eighteen units of work across four workers — twelve done, three
    /// running (with a live estimate), two still queued and ONE that failed and can be retried, which is the tile
    /// the page's own handler re-queues.
    /// </summary>
    internal static IReadOnlyList<FanOutTask> FanOutTasks(DateTimeOffset now)
    {
        var workers = new[]
        {
            new AgentRef("Fetcher", "w-fetch") { Role = "Worker", ColorIndex = 1 },
            new AgentRef("Parser", "w-parse") { Role = "Worker", ColorIndex = 4 },
            new AgentRef("Matcher", "w-match") { Role = "Worker", ColorIndex = 2 },
            new AgentRef("Writer", "w-write") { Role = "Worker", ColorIndex = 5 },
        };

        var tasks = new List<FanOutTask>(18);
        for (var index = 0; index < 18; index++)
        {
            var worker = workers[index % workers.Length];
            var estimate = now.AddSeconds(40 + (index * 25));
            var status = index switch
            {
                < 12 => AgentActivityStatus.Succeeded,
                < 15 => AgentActivityStatus.Running,
                15 => AgentActivityStatus.Failed,
                _ => AgentActivityStatus.Queued,
            };

            tasks.Add(new FanOutTask($"unit-{index + 1:00}", $"Invoice batch {index + 1:00}")
            {
                Status = status,
                Worker = index < 17 ? worker : null,
                Attempt = index == 15 ? 1 : 1 + (index % 2 == 0 ? 0 : 1),
                MaxAttempts = 3,
                StartedAt = status == AgentActivityStatus.Queued ? null : now.AddMinutes(-3),
                EstimatedEndAt = status is AgentActivityStatus.Running or AgentActivityStatus.Failed ? estimate : null,
                CostMicroUnits = status == AgentActivityStatus.Succeeded ? 45_000 + (index * 1_500) : null,
                Error = status == AgentActivityStatus.Failed
                    ? "Provider rate limit — retry with backoff"
                    : null,
            });
        }

        return tasks;
    }

    /// <summary>
    /// The parallel attempts of the <c>/ai-agents</c> demo: three drafts at the same task, scored and voted on. Draft B
    /// carries the highest score and two approvals, so it is the best attempt; the page still records only what the
    /// reader confirms.
    /// </summary>
    internal static IReadOnlyList<ParallelAttempt> Attempts() =>
    [
        new ParallelAttempt("draft-a", "Draft A")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.61,
            Summary = "Names the three failing batches but omits the retry window.",
            Votes =
            [
                new AttemptVote("Reviewer")
                {
                    Outcome = AttemptVoteOutcome.Approve,
                    Score = 0.6,
                    Rationale = "Readable, but the retry window is missing.",
                },
            ],
        },
        new ParallelAttempt("draft-b", "Draft B")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.93,
            Summary = "Covers the three failing batches, the retry window and the write-off rule.",
            Votes =
            [
                new AttemptVote("Reviewer")
                {
                    By = new AgentRef("Reviewer", "reviewer") { ColorIndex = 3 },
                    Outcome = AttemptVoteOutcome.Approve,
                    Score = 0.9,
                    Rationale = "Names every batch and states the window.",
                },
                new AttemptVote("Grader")
                {
                    Outcome = AttemptVoteOutcome.Approve,
                    Score = 0.96,
                    Rationale = "Matches the expected output on all three cases.",
                },
            ],
        },
        new ParallelAttempt("draft-c", "Draft C")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.42,
            Summary = "Summarises one batch and drops the rest.",
            Votes =
            [
                new AttemptVote("Reviewer")
                {
                    Outcome = AttemptVoteOutcome.Reject,
                    Score = 0.4,
                    Rationale = "Two of the three batches are missing.",
                },
            ],
        },
    ];

    /// <summary>
    /// The handoff chain of the <c>/ai-agents</c> demo: the same reconciliation run handed over three times. The first
    /// step is completed and lost the conversation history, the second is accepted and lost nothing, and the third is
    /// still PENDING and lost two pieces — so the summary line, the acceptance chip and the loss summary all carry a
    /// different value.
    /// </summary>
    internal static IReadOnlyList<HandoffStep> HandoffSteps(DateTimeOffset now) =>
    [
        new("h-fetch", "Fetcher hands the parsed ledger to the matcher")
        {
            FromAgent = new AgentRef("Fetcher", "w-fetch") { Role = "Worker", ColorIndex = 1 },
            ToAgent = new AgentRef("Matcher", "w-match") { Role = "Worker", ColorIndex = 4 },
            At = now.AddMinutes(-34),
            Status = HandoffStatus.Completed,
            PassedItems =
            [
                new HandoffItem("The task statement", HandoffItemKind.Context),
                new HandoffItem("Invoice schema", HandoffItemKind.Artifact),
            ],
            LostItems =
            [
                new HandoffItem("Conversation history", HandoffItemKind.Context)
                {
                    Reason = "The context window was full",
                },
            ],
        },
        new("h-match", "Matcher hands the matched lines to the writer")
        {
            FromAgent = new AgentRef("Matcher", "w-match") { Role = "Worker", ColorIndex = 4 },
            ToAgent = new AgentRef("Writer", "w-write") { Role = "Worker", ColorIndex = 5 },
            At = now.AddMinutes(-12),
            Status = HandoffStatus.Accepted,
            PassedItems =
            [
                new HandoffItem("Matched lines", HandoffItemKind.Artifact),
                new HandoffItem("The write-off rule", HandoffItemKind.Instruction),
                new HandoffItem("The ledger reader", HandoffItemKind.Tool),
            ],
        },
        new("h-write", "Writer asks the reviewer to approve the write-off")
        {
            FromAgent = new AgentRef("Writer", "w-write") { Role = "Worker", ColorIndex = 5 },
            ToAgent = new AgentRef("Reviewer", "reviewer") { Role = "Reviewer", ColorIndex = 3 },
            At = now.AddMinutes(-2),
            Status = HandoffStatus.Pending,
            PassedItems = [new HandoffItem("The adjustment draft", HandoffItemKind.Artifact)],
            LostItems =
            [
                new HandoffItem("The buyer's deadline", HandoffItemKind.Memory)
                {
                    Reason = "It was never recorded",
                },
                new HandoffItem("The materiality threshold", HandoffItemKind.Instruction)
                {
                    Reason = "The instruction was truncated",
                },
            ],
        },
    ];

    /// <summary>
    /// The fallback run of the <c>/ai-agents</c> demo: the strong model was rate limited, the second was skipped because
    /// it cannot read the scanned invoice, and the third answered — with one attempt that measured no latency, so the
    /// surface has to state that instead of inventing a zero.
    /// </summary>
    internal static IReadOnlyList<FallbackAttempt> FallbackAttempts(DateTimeOffset now) =>
    [
        new("atlas-large")
        {
            Label = "Atlas Large",
            Status = FallbackAttemptStatus.Failed,
            FailureReason = "Provider rate limit (429)",
            LatencyMs = 1200,
            At = now.AddMinutes(-6),
        },
        new("aurora-pro")
        {
            Label = "Aurora Pro",
            Status = FallbackAttemptStatus.Skipped,
            FailureReason = "The model cannot read the scanned invoice",
            At = now.AddMinutes(-6),
        },
        new("nimbus-2")
        {
            Label = "Nimbus 2",
            Status = FallbackAttemptStatus.Succeeded,
            LatencyMs = 640,
            At = now.AddMinutes(-5),
        },
    ];

    /// <summary>
    /// The submitted body of one demo draft — sample DATA (like the votes' rationales), rendered through the
    /// component's content slot so the page shows three DISTINCT attempts rather than the same summary twice.
    /// </summary>
    internal static string AttemptBody(string attemptId) => attemptId switch
    {
        "draft-a" =>
            "Batch 4821: retried once, then matched.\nBatches 4822 and 4830: listed as unresolved, no retry window stated.",
        "draft-b" =>
            "Batch 4821: matched after one retry (4 min).\nBatch 4822: matched after two retries (9 min).\nBatch 4830: written off under the 2 % materiality rule.",
        "draft-c" =>
            "Batch 4821: unresolved.\nThe remaining batches are not mentioned.",
        _ => string.Empty,
    };

    /// <summary>The initial draft values of the template the page opens on.</summary>
    internal static IReadOnlyDictionary<string, string?> InitialValues(Func<string, string> loc)
        => new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["product"] = "Tempo.Blazor",
            ["version"] = "2.10.0",
            ["focus"] = loc("TmAiAgentsDemo_FocusFeatures"),
        };

    /// <summary>
    /// The four grades of autonomy the page's dial offers, from the most supervised to the least. The labels and the
    /// descriptions are host data (the demo's sample content), the way every other sample on this page is.
    /// </summary>
    internal static IReadOnlyList<AutonomyLevel> AutonomyLevels() =>
    [
        new(0)
        {
            Label = "Observe",
            Description = "The agent proposes what it would do and waits for approval.",
        },
        new(1)
        {
            Label = "Assist",
            Description = "The agent does reversible work on its own and asks before anything else.",
        },
        new(2)
        {
            Label = "Act",
            Description = "The agent finishes the task and posts a summary when it is done.",
        },
        new(3)
        {
            Label = "Autopilot",
            Description = "The agent picks its own tasks from the queue and only reports failures.",
        },
    ];

    /// <summary>The limits a tenant and a workspace administrator enforce, with their reasons.</summary>
    internal static IReadOnlyList<AutonomyLock> AutonomyLocks() =>
    [
        new("Send external email")
        {
            Source = "Tenant policy",
            Reason = "Outbound mail needs a human sign-off.",
        },
        new("Delete a workspace")
        {
            Source = "Workspace administrator",
        },
    ];

    /// <summary>
    /// What the agent remembers about the workspace: three categories, one paused fact, one the agent proposed and one
    /// the host already considers forgotten, so every status and every action has something to act on.
    /// </summary>
    internal static IReadOnlyList<MemoryItem> Memories(DateTimeOffset now) =>
    [
        new("mem-units", "Preferences", "Prefers metric units and 24-hour time.")
        {
            Confidence = 0.94,
            Source = "Support ticket #4211",
            CreatedAt = now.AddDays(-42),
            UpdatedAt = now.AddDays(-3),
        },
        new("mem-zone", "Preferences", "Works in the CET time zone.")
        {
            Status = MemoryItemStatus.Paused,
            Confidence = 0.72,
            CreatedAt = now.AddDays(-28),
        },
        new("mem-approval", "Security", "Never sends invoices to a new address without approval.")
        {
            Confidence = 0.99,
            Source = "Conversation 4f2a",
            CreatedAt = now.AddDays(-14),
            UpdatedAt = now.AddDays(-1),
        },
        new("mem-report", "People", "Reports to Dana Král and reviews the ledger on Mondays.")
        {
            Confidence = 0.61,
            CreatedAt = now.AddDays(-9),
        },
        new("mem-tone", "Preferences", "Prefers short, factual status updates.")
        {
            Status = MemoryItemStatus.PendingReview,
            Confidence = 0.55,
            Source = "Conversation 9c17",
            CreatedAt = now.AddHours(-6),
        },
        new("mem-legacy", "People", "Used to be the owner of the reconciliation project.")
        {
            Status = MemoryItemStatus.Forgotten,
            CreatedAt = now.AddDays(-120),
            UpdatedAt = now.AddDays(-60),
        },
    ];

    /// <summary>The effort levels of a model, labelled in the page's language.</summary>
    private static AiModelEffortLevel[] Effort(Func<string, string> loc, params string[] ids)
        => ids.Select(id => new AiModelEffortLevel(id, loc($"TmAiAgentsDemo_Effort{char.ToUpperInvariant(id[0])}{id[1..]}"))).ToArray();
}
