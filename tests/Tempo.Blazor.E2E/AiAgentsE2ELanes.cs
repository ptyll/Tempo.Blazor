using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 7ba95ed6, Fáze 10 — the <c>/ai-agents</c> scenario on ALL THREE lanes. The behaviour (keyboard and focus walks,
/// the failing run with its Retry and approval, the treegrid, the model picker, the lanes' selection and table switch,
/// the graph's arrow walk, the fan-out grid and the attempts, the handoff and fallback chains, the dial's keys, the
/// memory panel's queue, the delegate board, the scheduled runs' chord, the conflict radar and the presence overlay) is
/// verified once in <see cref="AiAgentsE2EBase"/> and inherited here, so a WASM-only pass can no longer stand in for the
/// same contract over a Server circuit or an InteractiveAuto host.
/// <para>
/// The WASM subclass keeps the historical name and adds the visual reference specs (<c>AiAgentsE2ETests.Visual.cs</c>);
/// the Server and InteractiveAuto lanes run exactly the same scenario, which is the point: which parts of a walk are
/// lane-specific was the open question this phase closes.
/// </para>
/// </summary>
[TestClass]
[TestCategory("WASM")]
public sealed partial class AiAgentsE2ETests : AiAgentsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl =>
        Environment.GetEnvironmentVariable("TM_E2E_WASM_URL") ?? "https://localhost:7106";
}

/// <summary>
/// The Server lane: the same scenario over a real Blazor Server circuit. The walks that used to be WASM-only (the
/// fan-out's roving keys and Ctrl+End, the attempts' radio plus confirmation, the graph's arrows, the handoff's Details,
/// the fallback editor's grab/move/drop, the dial's keys and Escape, the memory panel's inline edit and undo window, the
/// delegate board's Assign/move menus, the scheduled runs' day stepping, the conflict radar settled from the native radio
/// group and the presence overlay's measurement) now run here too, so a circuit-specific focus or interop regression
/// fails this lane instead of hiding behind a green WASM run.
/// </summary>
[TestClass]
[TestCategory("Server")]
public sealed class AiAgentsServerE2ETests : AiAgentsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7107";
}

/// <summary>
/// The InteractiveAuto lane: the same scenario on the host that renders the page over the Server circuit first and then
/// hands it to the WebAssembly runtime. Every walk is inherited, so the switch between the two runtimes is covered by
/// the same expectations the WASM and Server lanes assert separately.
/// </summary>
[TestClass]
[TestCategory("InteractiveAuto")]
public sealed class AiAgentsInteractiveAutoE2ETests : AiAgentsE2EBase
{
    /// <inheritdoc />
    protected override string BaseUrl => "https://localhost:7108";
}
