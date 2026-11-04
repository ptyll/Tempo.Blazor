using System.Globalization;
using Tempo.Blazor.Models;

namespace Tempo.Blazor.Demo.SharedUI.Pages;

/// <summary>
/// The generated trace of the <c>/trace-waterfall</c> demo page: one agent run of roughly 150 spans — the
/// planning call, thirty-two decision rounds with their tool, HTTP and DB children, a retried request, a
/// nested sub-agent and the closing answer with a failing notification.
/// </summary>
/// <remarks>
/// <para>
/// This is demo DATA, not user-interface text — a trace viewer's sample trace. Span names, models and HTTP
/// paths are technical identifiers of the simulated run; every heading, status word and label the PAGE puts
/// around them resolves through <c>ITmLocalizer</c>.
/// </para>
/// <para>
/// The generator is seeded and the layout is deterministic (spans advance one cursor), so the same build
/// always produces the same trace. The count deliberately stays under the component's 200-row
/// virtualization threshold: the demo shows the whole tree in the DOM so a reader can scroll it, while a
/// production trace of thousands of spans still virtualizes.
/// </para>
/// </remarks>
internal static class TraceWaterfallPageSamples
{
    /// <summary>How many decision rounds the simulated run makes.</summary>
    internal const int RoundCount = 32;

    /// <summary>How many milliseconds the finished run takes, measured from the root span's start.</summary>
    internal const int RunDurationMs = 14_800;

    /// <summary>The tool the run calls, one per round, in call order.</summary>
    private static readonly string[] ToolNames =
    [
        "search_web",
        "lookup_order",
        "fetch_invoice",
        "get_customer",
        "search_knowledge_base",
        "list_shipments",
        "check_refund_policy",
        "send_receipt",
    ];

    /// <summary>
    /// Builds the demo trace anchored at <paramref name="now"/>.
    /// </summary>
    /// <param name="now">The moment the trace ends (or, while running, the moment it has reached).</param>
    /// <param name="running">
    /// Whether the run is still going: the root span, the closing answer and one streamed tool call then have no
    /// end, so the component's live ticker grows their bars.
    /// </param>
    /// <returns>The flat span list, in layout order (the tree comes from the parent links).</returns>
    internal static IReadOnlyList<TraceSpan> BuildTrace(DateTimeOffset now, bool running)
    {
        var random = new Random(20261031);
        var start = now.AddMilliseconds(-RunDurationMs);
        var spans = new List<TraceSpan>(160);
        var cursor = 40L;

        void Add(
            string id,
            string? parentId,
            string name,
            TraceSpanKind kind,
            long atMilliseconds,
            long durationMilliseconds,
            TraceSpanStatus status = TraceSpanStatus.Ok,
            IReadOnlyDictionary<string, double>? metrics = null,
            IReadOnlyDictionary<string, string>? attributes = null)
        {
            // The two maps default to the model's own shared empty instances, so they are set only when the
            // span actually reports something (the defaults are internal to the abstractions assembly).
            var span = new TraceSpan
            {
                Id = id,
                ParentId = parentId,
                Name = name,
                Kind = kind,
                StartedAt = start.AddMilliseconds(atMilliseconds),
                EndedAt = durationMilliseconds < 0 ? null : start.AddMilliseconds(atMilliseconds + durationMilliseconds),
                Status = status,
            };

            if (metrics is not null)
            {
                span = span with { Metrics = metrics };
            }

            if (attributes is not null)
            {
                span = span with { Attributes = attributes };
            }

            spans.Add(span);
        }

        IReadOnlyDictionary<string, double> Tokens(int input, int output, double cost) =>
            new Dictionary<string, double>
            {
                ["tokens_in"] = input,
                ["tokens_out"] = output,
                ["cost"] = cost,
            };

        IReadOnlyDictionary<string, string> Attributes(params (string Key, string Value)[] pairs) =>
            pairs.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        // The root: the whole run. While running it has no end, so the component extends it to its clock.
        Add("run", null, "agent run — support triage", TraceSpanKind.Agent, 0, running ? -1 : RunDurationMs,
            running ? TraceSpanStatus.Running : TraceSpanStatus.Ok,
            Tokens(48_120, 9_640, 12.42),
            Attributes(("agent", "triage-v3"), ("environment", "production"), ("release", "2.10.0")));

        // The planning call and the retrieval it triggers.
        var planDuration = 180 + random.Next(90);
        Add("plan", "run", "gpt-4o: plan the triage", TraceSpanKind.Llm, cursor, planDuration,
            metrics: Tokens(1_820, 640, 0.42),
            attributes: Attributes(("model", "gpt-4o"), ("temperature", "0.2"), ("round", "0")));
        Add("plan-context", "plan", "GET /v1/threads/{id}/context", TraceSpanKind.Http,
            cursor + 20, planDuration - 60,
            attributes: Attributes(("http.method", "GET"), ("http.status", "200")));
        cursor += planDuration + 30;

        // The nested sub-agent: evidence gathering, its own two calls and the summary it returns.
        var subAgentStart = cursor;
        Add("sub-agent", "run", "sub-agent: evidence gatherer", TraceSpanKind.Agent, subAgentStart, 1_120,
            metrics: Tokens(6_240, 1_180, 1.62),
            attributes: Attributes(("agent", "evidence-gatherer"), ("handoff", "tool-call")));
        Add("sub-agent-plan", "sub-agent", "gpt-4o-mini: choose sources", TraceSpanKind.Llm, subAgentStart + 30, 220,
            metrics: Tokens(920, 210, 0.09),
            attributes: Attributes(("model", "gpt-4o-mini")));
        Add("sub-agent-search", "sub-agent", "search_web", TraceSpanKind.Tool, subAgentStart + 270, 480,
            attributes: Attributes(("tool", "search_web"), ("results", "12")));
        Add("sub-agent-search-http", "sub-agent-search", "GET /v1/search", TraceSpanKind.Http,
            subAgentStart + 290, 320,
            attributes: Attributes(("http.method", "GET"), ("http.status", "200")));
        Add("sub-agent-summary", "sub-agent", "gpt-4o-mini: summarise the evidence", TraceSpanKind.Llm,
            subAgentStart + 760, 300,
            metrics: Tokens(1_640, 480, 0.14),
            attributes: Attributes(("model", "gpt-4o-mini")));
        cursor = subAgentStart + 1_160;

        // Thirty-two decision rounds: one LLM call, one tool call and the two transport spans underneath it.
        for (var round = 0; round < RoundCount; round++)
        {
            var decide = cursor;
            var decideDuration = 120 + random.Next(110);
            Add($"round-{round:D2}-decide", "run", $"gpt-4o: decide next step #{round + 1}", TraceSpanKind.Llm,
                decide, decideDuration,
                metrics: Tokens(880 + random.Next(280), 190 + random.Next(90), 0.11 + (random.NextDouble() * 0.06)),
                attributes: Attributes(("model", "gpt-4o"), ("round", (round + 1).ToString(CultureInfo.InvariantCulture))));
            cursor = decide + decideDuration + 20;

            var tool = cursor;
            var toolName = ToolNames[round % ToolNames.Length];
            var httpDuration = 60 + random.Next(120);
            var dbDuration = 40 + random.Next(80);
            var toolDuration = httpDuration + dbDuration + 70;
            Add($"round-{round:D2}-tool", "run", toolName, TraceSpanKind.Tool, tool, toolDuration,
                attributes: Attributes(("tool", toolName), ("round", (round + 1).ToString(CultureInfo.InvariantCulture))));

            Add($"round-{round:D2}-http", $"round-{round:D2}-tool", $"POST /v1/{toolName}", TraceSpanKind.Http,
                tool + 20, httpDuration,
                attributes: Attributes(("http.method", "POST"), ("http.status", round == 11 ? "429" : "200")));

            // Round 4 fails outright: a DB timeout the run survives by degrading its answer.
            var dbStatus = round == 4 ? TraceSpanStatus.Error : TraceSpanStatus.Ok;
            Add($"round-{round:D2}-db", $"round-{round:D2}-tool", "SELECT orders", TraceSpanKind.Db,
                tool + 30 + httpDuration, dbDuration, dbStatus,
                attributes: dbStatus == TraceSpanStatus.Error
                    ? Attributes(("db.statement", "SELECT orders"), ("error", "timeout after 40 ms"), ("retry", "1"))
                    : Attributes(("db.statement", "SELECT orders"), ("db.rows", "1")));
            cursor = tool + toolDuration + 20;

            // Every fifth round pays for a retry of the previous request.
            if (round % 5 == 4)
            {
                var retry = cursor;
                Add($"round-{round:D2}-retry", "run", $"retry: POST /v1/{toolName}", TraceSpanKind.Tool, retry, 140,
                    attributes: Attributes(("attempt", "2"), ("backoff", "250 ms")));
                Add($"round-{round:D2}-retry-http", $"round-{round:D2}-retry", $"POST /v1/{toolName}", TraceSpanKind.Http,
                    retry + 20, 100,
                    attributes: Attributes(("http.method", "POST"), ("http.status", "200")));
                cursor = retry + 160;
            }
        }

        // The closing answer: a streamed completion, the notification that fails and the wrap-up.
        var answer = cursor;
        var answerDuration = 320;
        Add("answer", "run", "gpt-4o: compose the answer", TraceSpanKind.Llm, answer, running ? -1 : answerDuration,
            running ? TraceSpanStatus.Running : TraceSpanStatus.Ok,
            Tokens(3_480, 1_420, 0.96),
            attributes: Attributes(("model", "gpt-4o"), ("stream", "true")));
        Add("stream", "answer", "stream tokens", TraceSpanKind.Custom, answer + 40, running ? -1 : answerDuration - 80,
            running ? TraceSpanStatus.Running : TraceSpanStatus.Ok,
            attributes: Attributes(("chunks", "38")));

        var notify = answer + answerDuration + 20;
        Add("notify", "run", "POST /v1/notifications", TraceSpanKind.Http, notify, running ? -1 : 160,
            running ? TraceSpanStatus.Running : TraceSpanStatus.Error,
            attributes: Attributes(("http.method", "POST"), ("http.status", "503"), ("error", "upstream unavailable")));

        if (!running)
        {
            Add("wrap-up", "run", "persist the transcript", TraceSpanKind.Db, notify + 170, 60,
                attributes: Attributes(("db.statement", "INSERT INTO transcripts")));
        }

        return spans;
    }

    /// <summary>The metric columns the page renders after the duration.</summary>
    /// <param name="label">Resolves the localized caption of a metric key.</param>
    /// <returns>The three columns, in display order.</returns>
    /// <remarks>
    /// The cost column formats a PLAIN number and carries the currency in its label ("Cost ($)"): the
    /// demo's culture is a NEUTRAL one (the reader picks "en", not "en-US"), whose <c>"C"</c> format
    /// renders the generic currency sign U+00A4 — a "¤12.42" in every screenshot. The unit belongs in
    /// the translated label, where it is visible once and can be worded per language.
    /// </remarks>
    internal static IReadOnlyList<TraceMetricColumn> MetricColumns(Func<string, string> label) =>
    [
        new TraceMetricColumn("tokens_in", label("tokens_in"), "N0"),
        new TraceMetricColumn("tokens_out", label("tokens_out"), "N0"),
        new TraceMetricColumn("cost", label("cost"), "N2"),
    ];
}
