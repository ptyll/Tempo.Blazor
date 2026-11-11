using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Sample data of the <c>/ai-agent-visualizations</c> demo page: one multi-agent run at a scale big enough that every
/// agent surface has something to say — forty units of work, twenty remembered facts in four categories, three
/// branches over twenty-five files with four real conflicts, three scored drafts, three handoffs, a fallback run, a
/// graded autonomy dial with two locks, a delegate board and a week of scheduled runs.
/// </summary>
/// <remarks>
/// These are demo DATA, not user-interface text — the same way every other demo page ships sample rows. Every
/// heading, label, hint and state sentence the PAGE renders resolves through <c>ITmLocalizer</c>
/// (<c>DemoResources*.json</c>); the agent names, file paths, memory sentences and batch names below are the host's
/// own content, exactly like a real application's.
/// </remarks>
internal static class AiAgentVisualizationsSamples
{
    /// <summary>The four agents of the simulated run, in palette order.</summary>
    internal static IReadOnlyList<AgentRef> Agents() =>
    [
        new("Planner", "planner") { Role = "Orchestrator", ColorIndex = 0 },
        new("Retriever", "retriever") { Role = "Worker", ColorIndex = 1 },
        new("Reconciler", "reconciler") { Role = "Worker", ColorIndex = 2 },
        new("Reviewer", "reviewer") { Role = "Reviewer", ColorIndex = 3 },
    ];

    /// <summary>The three agents the presence overlay and the conflict radar show, in palette order.</summary>
    internal static IReadOnlyList<AgentRef> PresentAgents() => [.. Agents().Take(3)];

    /// <summary>The four workers a fan-out unit is handed to.</summary>
    internal static IReadOnlyList<AgentRef> Workers() =>
    [
        new("Fetcher", "w-fetch") { Role = "Worker", ColorIndex = 1 },
        new("Parser", "w-parse") { Role = "Worker", ColorIndex = 4 },
        new("Matcher", "w-match") { Role = "Worker", ColorIndex = 2 },
        new("Writer", "w-write") { Role = "Worker", ColorIndex = 5 },
    ];

    /// <summary>
    /// The history the lanes open on: six finished blocks across all four agents plus one still RUNNING block, so the
    /// live "now" line and the running state have something to show before the reader presses Play.
    /// </summary>
    internal static IReadOnlyList<AgentLaneBlock> SeededBlocks(DateTimeOffset now) =>
    [
        Block(now, "seed-plan", "planner", -46, -38, AgentLaneBlockKind.Task, AgentActivityStatus.Succeeded,
            "Plan the reconciliation", AgentLaneBlockMarker.Approval),
        Block(now, "seed-fetch", "retriever", -38, -27, AgentLaneBlockKind.Tool, AgentActivityStatus.Succeeded,
            "Pull the ledger exports"),
        Block(now, "seed-match", "retriever", -26, -16, AgentLaneBlockKind.Message, AgentActivityStatus.Succeeded,
            "Match invoices to lines"),
        Block(now, "seed-diff", "reconciler", -16, -9, AgentLaneBlockKind.Task, AgentActivityStatus.Failed,
            "Reconcile the differences", AgentLaneBlockMarker.ChangesRequested),
        Block(now, "seed-review", "reviewer", -9, -4, AgentLaneBlockKind.Review, AgentActivityStatus.WaitingForApproval,
            "Approve the write-off", AgentLaneBlockMarker.Review),
        Block(now, "seed-write", "reconciler", -4, -2, AgentLaneBlockKind.Tool, AgentActivityStatus.Succeeded,
            "Write the adjustment"),
        Block(now, "seed-summary", "planner", -2, null, AgentLaneBlockKind.Task, AgentActivityStatus.Running,
            "Summarise for the room"),
    ];

    /// <summary>The handoffs the seeded history already recorded, oldest first.</summary>
    internal static IReadOnlyList<AgentHandoffLink> SeededHandoffs(DateTimeOffset now) =>
    [
        new("retriever", now.AddMinutes(-38)) { FromBlockId = "seed-plan", Label = "Plan ready" },
        new("reconciler", now.AddMinutes(-26)) { FromAgentId = "retriever" },
        new("reviewer", now.AddMinutes(-16)) { FromBlockId = "seed-diff" },
    ];

    /// <summary>What the simulated work blocks are called, cycling as the run goes on.</summary>
    internal static IReadOnlyList<string> BlockLabels() =>
    [
        "Read the ledger page",
        "Match the payment batch",
        "Ask the grader for a verdict",
        "Reconcile the difference",
        "Write the adjustment note",
        "Summarise the batch",
        "Re-read the failing row",
        "Hand the draft to review",
    ];

    /// <summary>
    /// The network the graph draws: a human, the four agents, the ledger they read and two tools they call. The
    /// call counts are the seeded history's, so the graph and the lanes never disagree about the run.
    /// </summary>
    internal static AgentGraphSnapshot GraphSnapshot(DateTimeOffset now, long sequence = 0) => new()
    {
        Sequence = sequence,
        Nodes =
        [
            new("human", "Dana (requester)") { Kind = AgentGraphNodeKind.Human },
            new("planner", "Planner")
            {
                Kind = AgentGraphNodeKind.Agent,
                Agent = new AgentRef("Planner", "planner") { Role = "Orchestrator", ColorIndex = 0 },
                Status = AgentActivityStatus.Running,
            },
            new("retriever", "Retriever")
            {
                Kind = AgentGraphNodeKind.Agent,
                Agent = new AgentRef("Retriever", "retriever") { Role = "Worker", ColorIndex = 1 },
                Status = AgentActivityStatus.Succeeded,
            },
            new("reconciler", "Reconciler")
            {
                Kind = AgentGraphNodeKind.Agent,
                Agent = new AgentRef("Reconciler", "reconciler") { Role = "Worker", ColorIndex = 2 },
                Status = AgentActivityStatus.Running,
            },
            new("reviewer", "Reviewer")
            {
                Kind = AgentGraphNodeKind.Agent,
                Agent = new AgentRef("Reviewer", "reviewer") { Role = "Reviewer", ColorIndex = 3 },
                Status = AgentActivityStatus.WaitingForApproval,
            },
            new("ledger", "Ledger") { Kind = AgentGraphNodeKind.Resource },
            new("search", "Web search") { Kind = AgentGraphNodeKind.Tool, Status = AgentActivityStatus.Succeeded },
            new("code", "Code interpreter") { Kind = AgentGraphNodeKind.Tool, Status = AgentActivityStatus.Idle },
        ],
        Edges =
        [
            new("g-ask", "human", "planner") { CallCount = 1, LastCallAt = now.AddMinutes(-46) },
            new("g-search", "planner", "search") { CallCount = 4, LastCallAt = now.AddMinutes(-30) },
            new("g-ledger", "retriever", "ledger") { CallCount = 12, LastCallAt = now.AddMinutes(-20) },
            new("g-code", "reconciler", "code") { CallCount = 3, LastCallAt = now.AddMinutes(-8) },
            new("g-hand", "planner", "retriever") { CallCount = 2, LastCallAt = now.AddMinutes(-38) },
            new("g-review", "reviewer", "planner") { CallCount = 1, LastCallAt = now.AddMinutes(-4) },
        ],
    };

    /// <summary>The graph edge ids a simulated tool call travels along, cycling in order.</summary>
    internal static IReadOnlyList<string> CallEdges() => ["g-search", "g-ledger", "g-code"];

    /// <summary>
    /// The fan-out: forty units of work across four workers. Twenty are done, six run with a live estimate, four
    /// failed and still carry their error — the retry the reader can ask for is the page re-queuing the unit — and
    /// ten wait in the queue.
    /// </summary>
    internal static IReadOnlyList<FanOutTask> FanOutTasks(DateTimeOffset now)
    {
        var workers = Workers();
        var tasks = new List<FanOutTask>(40);

        for (var index = 0; index < 40; index++)
        {
            var status = index switch
            {
                < 20 => AgentActivityStatus.Succeeded,
                < 26 => AgentActivityStatus.Running,
                < 30 => AgentActivityStatus.Failed,
                _ => AgentActivityStatus.Queued,
            };

            var started = status is AgentActivityStatus.Succeeded or AgentActivityStatus.Running
                or AgentActivityStatus.Failed
                ? now.AddMinutes(-6)
                : (DateTimeOffset?)null;

            tasks.Add(new FanOutTask($"unit-{index + 1:00}", $"Ledger batch {index + 1:00}")
            {
                Status = status,
                Worker = status == AgentActivityStatus.Queued ? null : workers[index % workers.Count],
                Attempt = status == AgentActivityStatus.Failed ? 2 : 1,
                MaxAttempts = 3,
                StartedAt = started,
                EstimatedEndAt = status is AgentActivityStatus.Running or AgentActivityStatus.Failed
                    ? now.AddSeconds(30 + (index * 7))
                    : null,
                CostMicroUnits = status == AgentActivityStatus.Succeeded ? 45_000 + (index * 1_250) : null,
                Error = status == AgentActivityStatus.Failed
                    ? "Provider rate limit — the unit is queued for another attempt"
                    : null,
            });
        }

        return tasks;
    }

    /// <summary>
    /// A deterministic fan-out FIXTURE of exactly <paramref name="count"/> units (plan 7ba95ed6, Fáze 17). The
    /// <c>/ai-agents</c> page's dataset switch uses it to reach the three shapes a browser guard needs — 0 units
    /// (the empty state), 1 unit (one tile and its detail) and 200 units (the windowed grid) — instead of a
    /// per-visit coincidence of the live sample.
    /// </summary>
    /// <remarks>
    /// A fixture is a FROZEN snapshot: it is built once from the caller's <paramref name="now"/> and never
    /// re-derived, so the live run's clock is effectively paused behind it and a screenshot or a row-count
    /// assertion cannot drift under the reader. No unit carries an estimate, so nothing counts down; the running
    /// units paint the elapsed read-out, which stays truthful for a frozen start point.
    /// </remarks>
    internal static IReadOnlyList<FanOutTask> FanOutFixture(int count, DateTimeOffset now)
    {
        if (count <= 0)
        {
            return [];
        }

        var workers = Workers();
        var tasks = new List<FanOutTask>(count);

        for (var index = 0; index < count; index++)
        {
            // A repeating three-state pattern over the whole list: the LAST unit is always a succeeded one, so the
            // windowed 200-unit fixture proves the focus can reach the end of the list whichever window it mounts.
            var status = (index % 10) switch
            {
                8 => AgentActivityStatus.Running,
                9 => AgentActivityStatus.Failed,
                _ => AgentActivityStatus.Succeeded,
            };
            if (index == count - 1)
            {
                status = AgentActivityStatus.Succeeded;
            }

            var started = (DateTimeOffset?)now.AddMinutes(-2);

            tasks.Add(new FanOutTask($"fixture-{index + 1:000}", $"Fixture batch {index + 1:000}")
            {
                Status = status,
                Worker = workers[index % workers.Count],
                Attempt = status == AgentActivityStatus.Failed ? 2 : 1,
                MaxAttempts = 3,
                StartedAt = started,
                EstimatedEndAt = null,
                CostMicroUnits = status == AgentActivityStatus.Succeeded ? 40_000 + (index * 250) : null,
                Error = status == AgentActivityStatus.Failed
                    ? "Provider rate limit — the fixture keeps the unit failed"
                    : null,
            });
        }

        return tasks;
    }

    /// <summary>
    /// The parallel attempts of one task: three drafts scored by the same grader. Draft B carries the highest score
    /// and two approvals, so it is the best attempt — the page still records only what the reader confirms.
    /// </summary>
    internal static IReadOnlyList<ParallelAttempt> Attempts() =>
    [
        new("draft-a", "Draft A")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.61,
            Summary = "Names the three failing batches but omits the retry window.",
            Votes =
            [
                new AttemptVote("Reviewer") { Outcome = AttemptVoteOutcome.Approve, Score = 0.6, Rationale = "Readable, the retry window is missing." },
                new AttemptVote("Grader") { Outcome = AttemptVoteOutcome.Reject, Score = 0.62, Rationale = "The retry window is missing, so the draft is not shippable yet." },
            ],
        },
        new ParallelAttempt("draft-b", "Draft B")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.93,
            Summary = "Covers the three failing batches, the retry window and the write-off rule.",
            Votes =
            [
                new AttemptVote("Reviewer") { Outcome = AttemptVoteOutcome.Approve, Score = 0.9, Rationale = "Names every batch and states the window." },
                new AttemptVote("Grader") { Outcome = AttemptVoteOutcome.Approve, Score = 0.96, Rationale = "Matches the expected output on all three cases." },
            ],
        },
        new ParallelAttempt("draft-c", "Draft C")
        {
            Status = AgentActivityStatus.Succeeded,
            Score = 0.42,
            Summary = "Summarises one batch and drops the rest.",
            Votes =
            [
                new AttemptVote("Reviewer") { Outcome = AttemptVoteOutcome.Reject, Score = 0.4, Rationale = "Two of the three batches are missing." },
            ],
        },
    ];

    /// <summary>The submitted body of one draft — sample DATA rendered through the surface's content slot.</summary>
    internal static string AttemptBody(string attemptId) => attemptId switch
    {
        "draft-a" => "Batch 4821: retried once, then matched.\nBatch 4822 and 4830: listed as unresolved, no retry window stated.",
        "draft-b" => "Batch 4821: matched after one retry (4 min).\nBatch 4822: matched after two retries (9 min).\nBatch 4830: written off under the 2 % materiality rule.",
        "draft-c" => "Batch 4821: unresolved.\nThe remaining batches are not mentioned.",
        _ => string.Empty,
    };

    /// <summary>
    /// The same run handed over three times: one handoff completed and lost a piece, one accepted and lost nothing,
    /// and one still pending with two losses — so the summary, the acceptance chip and the loss list all differ.
    /// </summary>
    internal static IReadOnlyList<HandoffStep> HandoffSteps(DateTimeOffset now) =>
    [
        new("h-fetch", "Fetcher hands the parsed ledger to the matcher")
        {
            FromAgent = Workers()[0],
            ToAgent = Workers()[2],
            At = now.AddMinutes(-34),
            Status = HandoffStatus.Completed,
            PassedItems =
            [
                new HandoffItem("The task statement", HandoffItemKind.Context),
                new HandoffItem("Invoice schema", HandoffItemKind.Artifact),
            ],
            LostItems =
            [
                new HandoffItem("Conversation history", HandoffItemKind.Context) { Reason = "The context window was full" },
            ],
        },
        new("h-match", "Matcher hands the matched lines to the writer")
        {
            FromAgent = Workers()[2],
            ToAgent = Workers()[3],
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
            FromAgent = Workers()[3],
            ToAgent = Agents()[3],
            At = now.AddMinutes(-2),
            Status = HandoffStatus.Pending,
            PassedItems = [new HandoffItem("The adjustment draft", HandoffItemKind.Artifact)],
            LostItems =
            [
                new HandoffItem("The buyer's deadline", HandoffItemKind.Memory) { Reason = "It was never recorded" },
                new HandoffItem("The materiality threshold", HandoffItemKind.Instruction) { Reason = "The instruction was truncated" },
            ],
        },
    ];

    /// <summary>
    /// The fallback run: the strong model was rate limited, the second was skipped because it cannot read the scanned
    /// invoice, and the third answered — with one attempt that measured no latency, so the surface states that.
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

    /// <summary>The fictional models the fallback editor offers and the chain it starts with.</summary>
    internal static IReadOnlyList<AiModelInfo> Models() =>
    [
        new("atlas-large", "Atlas Large") { Provider = "Atlas", ContextWindow = 1_000_000, InputPricePerMillion = 12m, OutputPricePerMillion = 36m },
        new("aurora-pro", "Aurora Pro") { Provider = "Aurora", ContextWindow = 200_000, InputPricePerMillion = 3m, OutputPricePerMillion = 15m },
        new("nimbus-2", "Nimbus 2") { Provider = "Nimbus", ContextWindow = 128_000, InputPricePerMillion = 0.8m, OutputPricePerMillion = 2.4m },
        new("cirrus-mini", "Cirrus Mini") { Provider = "Cirrus", ContextWindow = 32_000, InputPricePerMillion = 0.1m, OutputPricePerMillion = 0.4m },
    ];

    /// <summary>The chain the fallback editor starts with: three entries over the fictional catalogue.</summary>
    internal static IReadOnlyList<FallbackChainEntry> FallbackChain() =>
    [
        new("atlas-large") { Label = "Atlas Large" },
        new("aurora-pro") { Label = "Aurora Pro" },
        new("nimbus-2") { Label = "Nimbus 2" },
    ];

    /// <summary>The four grades of autonomy, from the most supervised to the least.</summary>
    internal static IReadOnlyList<AutonomyLevel> AutonomyLevels() =>
    [
        new(0) { Label = "Observe", Description = "The agent proposes what it would do and waits for approval." },
        new(1) { Label = "Assist", Description = "The agent does reversible work on its own and asks before anything else." },
        new(2) { Label = "Act", Description = "The agent finishes the task and posts a summary when it is done." },
        new(3) { Label = "Autopilot", Description = "The agent picks its own tasks from the queue and only reports failures." },
    ];

    /// <summary>The limits an administrator enforces, with their reasons: the dial renders them beside the stops.</summary>
    internal static IReadOnlyList<AutonomyLock> AutonomyLocks() =>
    [
        new("Send external email") { Source = "Tenant policy", Reason = "Outbound mail needs a human sign-off." },
        new("Delete a workspace") { Source = "Workspace administrator" },
    ];

    /// <summary>
    /// What the agent remembers: twenty facts over FOUR categories, so every category row has five entries and the
    /// status filter, the inline edit, the pause switch and the forget-with-undo all have something to act on.
    /// </summary>
    internal static IReadOnlyList<MemoryItem> Memories(DateTimeOffset now)
    {
        var categories = new[] { "Preferences", "Security", "People", "Projects" };
        var facts = new[]
        {
            "Prefers metric units and 24-hour time",
            "Reads the ledger in the CET time zone",
            "Wants short, factual status updates",
            "Files the reconciliation report on Mondays",
            "Keeps the raw export for ninety days",
            "Never sends an invoice to a new address without approval",
            "Treats the write-off threshold as material above two percent",
            "Escalates a failed batch to the reviewer",
            "Hides customer identifiers from model prompts",
            "Asks for a second signature above ten thousand",
            "Reports to Dana Král",
            "Reviews the ledger with the night shift",
            "Works with the Prague accounting team",
            "Owns the reconciliation project",
            "Prepares the quarterly close with the auditor",
            "Migrated the ledger reader to the new API",
            "Keeps the retry policy at three attempts",
            "Uses the batch importer for exports over a thousand rows",
            "Tracks the close checklist in the shared board",
            "Archived the legacy reconciliation script",
        };

        var items = new List<MemoryItem>(facts.Length);
        for (var index = 0; index < facts.Length; index++)
        {
            var status = index switch
            {
                4 => MemoryItemStatus.Paused,
                9 => MemoryItemStatus.PendingReview,
                14 => MemoryItemStatus.Paused,
                19 => MemoryItemStatus.Forgotten,
                17 => MemoryItemStatus.PendingReview,
                _ => MemoryItemStatus.Active,
            };

            items.Add(new MemoryItem($"mem-{index + 1:00}", categories[index % categories.Length], facts[index])
            {
                Status = status,
                Confidence = 0.5 + ((index % 5) * 0.1),
                Source = index % 3 == 0 ? $"Conversation {index + 1:x4}" : null,
                CreatedAt = now.AddDays(-(60 - index)),
                UpdatedAt = now.AddDays(-(index % 20)),
            });
        }

        return items;
    }

    /// <summary>
    /// The three agents a delegate board shows: two slots with work in flight, one slot busy for another while, and
    /// one whose slot is already taken — so the full state, the occupied-until preview and the auto pass all decide.
    /// </summary>
    internal static IReadOnlyList<DelegateAgent> DelegateAgents(DateTimeOffset now) =>
    [
        new(Agents()[0]) { MaxConcurrency = 2, ActiveCount = 1, BusyUntil = now.AddMinutes(40) },
        new(Agents()[1]) { MaxConcurrency = 1, BusyUntil = now.AddHours(1) },
        new(Agents()[3]) { MaxConcurrency = 1, ActiveCount = 1 },
    ];

    /// <summary>The work waiting to be handed out: eight tasks over three grades, four of them already on an agent.</summary>
    internal static IReadOnlyList<DelegateTask> DelegateTasks() =>
    [
        new("task-summary", "Draft the release summary") { Priority = DelegateTaskPriority.Urgent, EstimatedDuration = TimeSpan.FromMinutes(45) },
        new("task-invoices", "Collect the open invoices") { Priority = DelegateTaskPriority.High, EstimatedDuration = TimeSpan.FromMinutes(30) },
        new("task-notes", "Summarize yesterday's stand-up") { EstimatedDuration = TimeSpan.FromMinutes(15) },
        new("task-audit", "Audit the permission matrix") { Priority = DelegateTaskPriority.Normal, EstimatedDuration = TimeSpan.FromHours(2), AssignedAgentId = "planner" },
        new("task-changelog", "Write the changelog entry") { Priority = DelegateTaskPriority.Low, EstimatedDuration = TimeSpan.FromMinutes(20), AssignedAgentId = "retriever" },
        new("task-review", "Review the draft against the brief") { Priority = DelegateTaskPriority.High, EstimatedDuration = TimeSpan.FromMinutes(25), AssignedAgentId = "reviewer" },
        new("task-batches", "Requeue the four failed batches") { Priority = DelegateTaskPriority.Urgent, EstimatedDuration = TimeSpan.FromMinutes(10) },
        new("task-retro", "Collect the retro notes") { Priority = DelegateTaskPriority.Low, EstimatedDuration = TimeSpan.FromMinutes(35) },
    ];

    /// <summary>
    /// The scheduler's week: six windows across three days, one close to its capacity and one already over it so the
    /// capacity bar, the free line and the overload all render.
    /// </summary>
    internal static IReadOnlyList<RunWindow> RunWindows(DateTimeOffset now)
    {
        var monday = new DateTimeOffset(now.Year, now.Month, now.Day, 9, 0, 0, now.Offset);
        return
        [
            new("win-mon-am", monday, monday.AddHours(3), capacity: 6, used: 1),
            new("win-mon-pm", monday.AddHours(4), monday.AddHours(7), capacity: 6, used: 2),
            new("win-tue-am", monday.AddDays(1), monday.AddDays(1).AddHours(3), capacity: 6, used: 1),
            new("win-tue-pm", monday.AddDays(1).AddHours(4), monday.AddDays(1).AddHours(6), capacity: 4, used: 5),
            new("win-wed-am", monday.AddDays(2), monday.AddDays(2).AddHours(3), capacity: 5, used: 0),
            new("win-wed-pm", monday.AddDays(2).AddHours(4), monday.AddDays(2).AddHours(6), capacity: 4, used: 1),
        ];
    }

    /// <summary>
    /// The runs competing for that supply: a long one that has to be split, one already running, one finished and a
    /// short one — twelve units against eighteen free, so every run is placed and a re-plan still places them.
    /// </summary>
    internal static IReadOnlyList<ScheduledRun> ScheduledRuns() =>
    [
        new("run-export", "Nightly export") { EstimatedUnits = 6 },
        new("run-invoices", "Invoice batch") { EstimatedUnits = 3, Status = ScheduledRunStatus.Running },
        new("run-report", "Weekly report") { EstimatedUnits = 2, Status = ScheduledRunStatus.Succeeded },
        new("run-cleanup", "Cache cleanup") { EstimatedUnits = 1 },
    ];

    /// <summary>
    /// The conflict radar's input: three agents on three branches over the same twenty-five files. Exactly FOUR paths
    /// are claimed by more than one agent (f08 and f12 by the planner and the reconciler, f10 and f16 by the
    /// reconciler and the reviewer), so the grid shows four conflicts and twenty-one plain changes — a scale a reader
    /// has to scan rather than a toy.
    /// </summary>
    internal static IReadOnlyList<AgentBranchChanges> Branches()
    {
        var paths = ChangedPaths();
        var agents = PresentAgents();

        ChangedPath[] Claim(AgentRef agent, params int[] indexes) => [.. indexes.Select(index =>
            new ChangedPath(paths[index], ((index + agent.ColorIndex) % 4) switch
            {
                0 => ChangeKind.Added,
                1 => ChangeKind.Modified,
                2 => ChangeKind.Deleted,
                _ => ChangeKind.Modified,
            }))];

        return
        [
            new(agents[0], "feat/conflict-radar",
                // f01…f09 plus f12: ten paths, two of them shared with the reconciler (f08, f12).
                Claim(agents[0], 0, 1, 2, 3, 4, 5, 6, 7, 8, 11)),
            new(agents[1], "feat/presence",
                // f08, f10…f17: nine paths, four of them shared (f08 and f12 with the planner, f10 and f16 with the
                // reviewer).
                Claim(agents[1], 7, 9, 11, 10, 12, 13, 14, 15, 16)),
            new(agents[2], "feat/docs",
                // f10, f16, f18…f25: ten paths, two of them shared with the reconciler (f10, f16).
                Claim(agents[2], 9, 15, 17, 18, 19, 20, 21, 22, 23, 24)),
        ];
    }

    /// <summary>The twenty-five paths the three branches touch; the overlap is the conflict the reader must settle.</summary>
    internal static IReadOnlyList<string> ChangedPaths() =>
    [
        "src/Tempo.Blazor.AI/Components/AI/TmAgentLanes.razor",
        "src/Tempo.Blazor.AI/Components/AI/AgentLanesLayout.cs",
        "src/Tempo.Blazor.AI/Components/AI/TmAgentGraph.razor",
        "src/Tempo.Blazor.AI/Components/AI/AgentGraphLayout.cs",
        "src/Tempo.Blazor.AI/Components/AI/TmFanOutGrid.razor",
        "src/Tempo.Blazor.AI/Components/AI/FanOutPresentation.cs",
        "src/Tempo.Blazor.AI/Components/AI/TmParallelAttempts.razor",
        "src/Tempo.Blazor.AI/Components/AI/TmAgentHandoff.razor",
        "src/Tempo.Blazor.AI/Components/AI/TmConflictRadar.razor",
        "src/Tempo.Blazor.AI/Components/AI/ConflictMatrix.cs",
        "src/Tempo.Blazor.AI/Components/AI/TmAgentPresenceOverlay.razor",
        "src/Tempo.Blazor.AI/wwwroot/js/tm-agent-graph.js",
        "src/Tempo.Blazor.AI/wwwroot/js/tm-agent-lanes.js",
        "src/Tempo.Blazor.AI/wwwroot/js/tm-agent-presence.js",
        "docs/agent-lanes.md",
        "docs/agent-graph.md",
        "docs/conflict-radar.md",
        "CHANGELOG.md",
        "README.md",
        "docs/ai-agents.md",
        "docs/agent-presence-overlay.md",
        "docs/delegate-board.md",
        "docs/scheduled-runs.md",
        "docs/memory-panel.md",
        "docs/autonomy-dial.md",
    ];

    /// <summary>The demo document's blocks the presence cursors anchor to, in reading order.</summary>
    internal static IReadOnlyList<string> DocumentAnchors() =>
        ["plan-lead", "plan-goal", "plan-risk", "plan-close"];

    private static AgentLaneBlock Block(
        DateTimeOffset now,
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
}
