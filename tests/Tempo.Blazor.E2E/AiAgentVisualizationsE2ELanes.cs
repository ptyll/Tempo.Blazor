using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 10 — <c>/ai-agent-visualizations</c> on ALL THREE lanes. The behaviour (the simulator's play,
/// pause, rate and reset, the lanes' keyboard selection and table switch, the graph's arrow walk, the delegate board's
/// pointer drag plus its menu alternative, the memory panel's forget and undo, the conflict radar settled from the
/// native radio group, the dial's keys, every table alternative and one scoped axe scan per section) lives once in
/// <see cref="AiAgentVisualizationsE2EBase"/>; these subclasses run it against the WASM runtime, a Server circuit and the
/// InteractiveAuto host. The screenshots and the phone geometry stay WASM-only by design, and the two-minute budget has
/// its own perf base so it can run on the Server host too.
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed class AiAgentVisualizationsE2ETests : AiAgentVisualizationsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane: the same simulated run and every reader path over a real Blazor Server circuit, including the
/// sections that used to be WASM-only — the cross-list pointer drag on the delegate board (the shared
/// <c>pointer-drag.js</c> module reporting back over the circuit) and the ten-second undo window.
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class AiAgentVisualizationsServerE2ETests : AiAgentVisualizationsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same run and the same walks on the host that starts over the Server circuit and then
/// hands the page to the WebAssembly runtime, so a regression in either leg fails here.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class AiAgentVisualizationsInteractiveAutoE2ETests : AiAgentVisualizationsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7108";
}
