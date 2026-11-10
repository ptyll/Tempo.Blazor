using Microsoft.Playwright;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tempo.Blazor.E2E;

/// <summary>
/// Plan 3e96f6d8 phase 7 — the PAGE-WIDE spec of the <c>/ai-agents</c> scenario that belongs to EVERY lane: a scoped axe
/// scan of every section of the page. The visual sweep, the 375 px layout, the selected graph in both themes and the
/// <c>/ai-agent-visualizations</c> captures are the WASM design reference and live in <c>AiAgentsE2ETests.Visual.cs</c>.
/// <para>
/// The class, its base and every test name are shared with the main scenario file (one partial per concern), so each file
/// stays inside the repository's 1200-line source ceiling (N270/F13).
/// </para>
/// </summary>
public abstract partial class AiAgentsE2EBase : PlaywrightTestBase
{
    [TestMethod]
    public async Task ScopedAxeScan_HasNoCriticalOrSeriousViolations()
    {
        var page = await OpenAsync();

        await AssertNoSeriousAxeViolationsAsync(page,
            "ai-agents-plan-section", "ai-agents-tree-section", "ai-agents-lanes-section",
            "ai-agents-graph-section", "ai-agents-fan-out-section", "ai-agents-attempts-section",
            "ai-agents-handoff-section", "ai-agents-fallback-section",
            "ai-agents-autonomy-section", "ai-agents-memory-section",
            "ai-agents-delegate-section", "ai-agents-runs-section",
            "ai-agents-conflict-section", "ai-agents-presence-section",
            "ai-agents-monitor-section", "ai-agents-models-section",
            "ai-agents-context-section", "ai-agents-prompts-section");
    }
}
