using Xunit;

namespace Tempo.Blazor.Tests.Components;

/// <summary>
/// The collection the two phase-9 DEMO PAGE suites share: <c>AnalyticsKitPageTests</c> (a 720-order reporting
/// page with six projections per render) and <c>TraceWaterfallPageTests</c> (a 152-row treegrid per render).
/// </summary>
/// <remarks>
/// <para>
/// Both suites render a whole page from scratch in EVERY test, which is real CPU work; bUnit's
/// <c>WaitFor</c> helpers elsewhere in the assembly use a one-second default timeout. Running these two
/// side by side with the Modeling/Spreadsheet/DocumentEditor suites made those suites' waits flake on a loaded
/// machine (<c>WaitForFailedException</c> with "Component render count: 1") even though every assertion was
/// correct — the interference is CPU, not shared state (both suites pass in any other combination).
/// </para>
/// <para>
/// A collection with <see cref="CollectionDefinitionAttribute.DisableParallelization"/> set is exactly the
/// tool for that: the heavy page renders run one at a time and never steal a wait's budget from another
/// suite.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyDemoPageRenders
{
    /// <summary>The collection name the two suites reference.</summary>
    public const string Name = "Heavy demo page renders";
}
