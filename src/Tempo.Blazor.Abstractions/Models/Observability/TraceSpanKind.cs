namespace Tempo.Blazor.Models;

/// <summary>
/// What one <see cref="TraceSpan"/> represents in a distributed/agent trace.
/// <para>
/// The kind is presentation metadata: <c>TmTraceWaterfall</c> paints a distinct
/// <c>--tm-trace-*</c> colour and an icon per value, and always ships a visually hidden kind name
/// next to them, so the colour is never the only carrier of the information. The layout maths
/// (<c>TraceWaterfallLayout</c>) does not branch on it.
/// </para>
/// </summary>
public enum TraceSpanKind
{
    /// <summary>A model / LLM call (prompt, completion, embedding).</summary>
    Llm,

    /// <summary>A tool or function call made by the agent.</summary>
    Tool,

    /// <summary>An agent invocation, including a sub-agent of the root agent.</summary>
    Agent,

    /// <summary>An outbound HTTP request.</summary>
    Http,

    /// <summary>A database query or transaction.</summary>
    Db,

    /// <summary>Anything the host classifies itself.</summary>
    Custom
}
