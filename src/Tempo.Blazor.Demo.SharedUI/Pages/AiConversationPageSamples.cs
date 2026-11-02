using Tempo.Blazor.AI.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// Sample data of the <c>/ai-conversation</c> demo page: the simulated answer blocks, the two tool calls of
/// a streamed answer, the standalone tool-call states, the reasoning parts and the hostile markdown the safe
/// pipeline has to neutralize.
/// </summary>
/// <remarks>
/// These are demo DATA, not user-interface text — the same way a chat demo ships sample messages. Every
/// label, heading and status on the page itself resolves through <c>ITmLocalizer</c>.
/// </remarks>
internal static class AiConversationPageSamples
{
    /// <summary>The IDs of the two sources the simulated answer cites.</summary>
    internal const string DocsSourceId = "docs";

    /// <summary>Second cited source.</summary>
    internal const string NugetSourceId = "nuget";

    /// <summary>Reasoning steps of a simulated answer, streamed one at a time.</summary>
    internal static IReadOnlyList<string> ReasoningSteps { get; } =
    [
        "Read the question and decided the answer needs the streaming contract, not a component tour.",
        "Checked TmAiConversation.StreamAsync and AiMessageStreamApplier, then planned the answer around them.",
    ];

    /// <summary>Reasoning steps of the standalone (not streamed) reasoning panel sample.</summary>
    internal static IReadOnlyList<string> StandaloneReasoningSteps { get; } =
    [
        "Searched the documentation index for the AI conversation contracts.",
        "Cross-checked the tool-call statuses against the stream delta types.",
    ];

    /// <summary>The first answer block: what the demo proves.</summary>
    /// <param name="generation">One-based generation number, so a regenerated answer is visibly different.</param>
    internal static string Intro(int generation) =>
        $"""
        ### Generation {generation}

        This answer is produced by a local `IAsyncEnumerable<AiStreamDelta>` generator — no model is called.
        The deltas arrive one by one and the transcript follows them while the reader is at the bottom.

        **What this demo proves**

        - the text, the reasoning and the tool calls arrive as ordered parts of ONE message
        - a tool call can fail and be retried without restarting the answer
        - the completion is announced once in the polite live region
        """;

    /// <summary>The second answer block: a markdown table and the two citations.</summary>
    internal static string Table { get; } =
        """
        | Step | Tokens | Duration |
        | --- | ---: | ---: |
        | Reason | 96 | 0.4 s |
        | Search | 12 | 0.1 s |
        | Answer | 1 284 | 1.2 s |

        The numbers come from the cited sources [1](cite:docs) and [2](cite:nuget).
        """;

    /// <summary>The third answer block: a fenced code block with the copy affordance.</summary>
    internal static string Code { get; } =
        """
        ```csharp
        var answer = new AiMessage { Role = AiMessageRole.Assistant };
        messages.Add(answer);
        await conversation.StreamAsync(answer, HostDeltas(prompt));
        ```
        """;

    /// <summary>The closing answer block: what the reader can try next.</summary>
    internal static string Closing { get; } =
        """
        Retry the failed call above, press **Regenerate** for another generation and step back through the
        version switcher, or rate the answer with the thumbs.
        """;

    /// <summary>Follow-up prompts the answer offers once it finished.</summary>
    /// <param name="loc">Localizer used for the two suggestion texts.</param>
    internal static IReadOnlyList<string> FollowUps(Tempo.Blazor.Localization.ITmLocalizer loc) =>
    [
        loc["TmDemo_AiConversation_FollowUp1"],
        loc["TmDemo_AiConversation_FollowUp2"],
    ];

    /// <summary>The two cited sources of a simulated answer.</summary>
    internal static IReadOnlyList<AiSource> Sources { get; } =
    [
        new AiSource
        {
            Id = DocsSourceId,
            Title = "AI conversation — TmAiConversation",
            Url = "https://github.com/tempo-blazor/tempo-blazor/blob/main/docs/ai-conversation.md",
            Domain = "github.com",
            Snippet = "The transcript, the delta pump, the render window and the stop affordance.",
        },
        new AiSource
        {
            Id = NugetSourceId,
            Title = "Tempo.Blazor.AI on NuGet",
            Url = "https://www.nuget.org/packages/Tempo.Blazor.AI",
            Domain = "nuget.org",
            Snippet = "The AI conversation package: TmMarkdownView, TmAiMessage, TmAiConversation and TmAiComposer.",
        },
    ];

    /// <summary>The standalone tool-call states: queued, running, succeeded, failed, cancelled and redacted.</summary>
    internal static IReadOnlyList<AiToolCall> ToolCallStates { get; } = BuildToolCallStates();

    /// <summary>A run of three consecutive calls, rendered as one group.</summary>
    internal static IReadOnlyList<AiToolCall> ToolCallRun { get; } = BuildToolCallRun();

    /// <summary>A finished reasoning part with the sources the model inspected.</summary>
    internal static AiReasoningPart Reasoning { get; } = new(
        StandaloneReasoningSteps,
        [
            new AiSource
            {
                Id = "conversation-doc",
                Title = "docs/ai-conversation.md",
                Url = "https://github.com/tempo-blazor/tempo-blazor/blob/main/docs/ai-conversation.md",
            },
            new AiSource
            {
                Id = "streaming-doc",
                Title = "docs/ai-markdown.md",
                Url = "https://github.com/tempo-blazor/tempo-blazor/blob/main/docs/ai-markdown.md",
            },
        ])
    {
        DurationMs = 2400,
    };

    /// <summary>
    /// Hostile markdown: raw HTML, an image with an <c>onerror</c> handler, a <c>javascript:</c> link, a
    /// <c>data:</c> link and a <c>vbscript:</c> link. The page renders it through <c>TmMarkdownView</c> and
    /// nothing here may execute — the raw HTML is escaped, the image is dropped to its alt text and the
    /// refused schemes become <c>#</c>.
    /// </summary>
    internal static string HostileMarkdown { get; } =
        """
        ### A hostile answer

        <script>window.__tmAiXss = 'script';</script>

        <img src="x" onerror="window.__tmAiXss = 'onerror'">

        <div onclick="window.__tmAiXss = 'onclick'">A clickable-looking div</div>

        [javascript link](javascript:window.__tmAiXss='href') and [data link](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==) and [vbscript link](vbscript:msgbox(1))

        [a safe link](https://example.com/safe)

        The literal text below is what a refused construct looks like: it is escaped, not executed.
        """;

    private static IReadOnlyList<AiToolCall> BuildToolCallStates()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new AiToolCall
            {
                Id = "state-queued",
                Name = "queued_tool",
                DisplayName = "Queued tool",
                Status = AiToolCallStatus.Queued,
                ArgumentsJson = """{"batch":2}""",
            },
            new AiToolCall
            {
                Id = "state-running",
                Name = "running_tool",
                DisplayName = "Running tool",
                Status = AiToolCallStatus.Running,
                StartedAt = now.AddSeconds(-2),
                ArgumentsJson = """{"query":"tempo.blazor"}""",
            },
            new AiToolCall
            {
                Id = "state-succeeded",
                Name = "succeeded_tool",
                DisplayName = "Succeeded tool",
                Status = AiToolCallStatus.Succeeded,
                StartedAt = now.AddSeconds(-3),
                CompletedAt = now.AddSeconds(-2),
                ArgumentsJson = """{"query":"tempo.blazor","top":3}""",
                ResultJson = """{"hits":3,"took_ms":128}""",
            },
            new AiToolCall
            {
                Id = "state-failed",
                Name = "failed_tool",
                DisplayName = "Failed tool",
                Status = AiToolCallStatus.Failed,
                StartedAt = now.AddSeconds(-2),
                CompletedAt = now,
                ArgumentsJson = """{"url":"https://usage.example.com"}""",
                Error = "HTTP 503 from usage.example.com",
            },
            new AiToolCall
            {
                Id = "state-cancelled",
                Name = "cancelled_tool",
                DisplayName = "Cancelled tool",
                Status = AiToolCallStatus.Cancelled,
                StartedAt = now.AddSeconds(-2),
                CompletedAt = now,
            },
            new AiToolCall
            {
                Id = "state-redacted",
                Name = "redacted_tool",
                DisplayName = "Redacted tool",
                Status = AiToolCallStatus.Succeeded,
                IsRedacted = true,
                StartedAt = now.AddSeconds(-2),
                CompletedAt = now,
            },
        ];
    }

    private static IReadOnlyList<AiToolCall> BuildToolCallRun()
    {
        var now = DateTimeOffset.UtcNow;
        return
        [
            new AiToolCall
            {
                Id = "run-1",
                Name = "search_docs",
                DisplayName = "Search the docs",
                Status = AiToolCallStatus.Succeeded,
                StartedAt = now.AddSeconds(-4),
                CompletedAt = now.AddSeconds(-3),
                ArgumentsJson = """{"query":"streaming"}""",
                ResultJson = """{"hits":2}""",
            },
            new AiToolCall
            {
                Id = "run-2",
                Name = "fetch_usage",
                DisplayName = "Fetch usage",
                Status = AiToolCallStatus.Failed,
                StartedAt = now.AddSeconds(-3),
                CompletedAt = now.AddSeconds(-2),
                ArgumentsJson = """{"url":"https://usage.example.com"}""",
                Error = "HTTP 503 from usage.example.com",
            },
            new AiToolCall
            {
                Id = "run-3",
                Name = "summarise",
                DisplayName = "Summarise the hits",
                Status = AiToolCallStatus.Succeeded,
                StartedAt = now.AddSeconds(-2),
                CompletedAt = now.AddSeconds(-1),
                ArgumentsJson = """{"hits":2}""",
                ResultJson = """{"summary":"Two documents matched."}""",
            },
        ];
    }
}
