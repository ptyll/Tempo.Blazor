using Xunit;

namespace Tempo.Blazor.Tests;

/// <summary>
/// The collection that holds the facts which assert a WALL-CLOCK budget (a product performance guard measured with a
/// stopwatch), so they run exclusively instead of competing with the parallel suite for CPU.
/// </summary>
/// <remarks>
/// <para>
/// A budget fact is a real product guard and must NOT be loosened under load — the documented answer for a wall-clock
/// budget is to run it where nothing else is asking for the CPU (docs/e2e-test-lanes.md,
/// DEC-TEMPO-PERF-LANE-BUDGETS, is the same decision for the Playwright lanes). Inside <c>Tempo.Blazor.Tests</c> the
/// equivalent is a collection with <see cref="CollectionDefinitionAttribute.DisableParallelization"/> set: xUnit runs
/// it on its own, so <c>Stopwatch</c> measures the code rather than a queue of twelve starved test threads.
/// </para>
/// <para>
/// Measured evidence (plan 7ba95ed6, Fáze 15, the same commit, only the machine load differing):
/// <c>BpmnXmlImporterTests.Import_of_200k_flat_tasks_completes_within_a_few_seconds</c> read 13 s / 10 s in a loaded
/// full run and ~2 s alone; <c>AgentGraphLayoutTests.ALargeGraph_BuildsUnder50Milliseconds</c> read 56 ms / 50 ms in
/// a loaded run and 20–30 ms alone. Both pass in isolation every time, so a red run was reporting the machine, not a
/// regression. The budgets keep their product values.
/// </para>
/// <para>
/// Add a class here only when its assertions read elapsed time or throughput; functional facts belong in the normal
/// parallel suite.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockBudgets
{
    /// <summary>The collection name the budget suites reference.</summary>
    public const string Name = "Wall-clock performance budgets";
}
