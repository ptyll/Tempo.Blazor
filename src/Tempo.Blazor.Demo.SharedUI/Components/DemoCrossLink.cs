namespace Tempo.Blazor.Demo.SharedUI.Components;

/// <summary>
/// One cross-link of the demo: a relative href plus the resource key of the anchor text.
/// </summary>
/// <remarks>
/// The demo splits its pages into COMPONENT-LEVEL REFERENCES (<c>/data-display</c>, <c>/forms</c> and
/// <c>/charts</c>) and ASSEMBLED SCENARIOS (<c>/analytics-kit</c>, <c>/trace-waterfall</c>). A reference
/// names the scenario its section is assembled into, and a scenario names the references its components
/// are documented on, so a reader never has to guess where the other half lives. The href is always
/// RELATIVE: a root-absolute href would ignore the demo's <c>&lt;base href&gt;</c> and break when the demo
/// is hosted under a sub-path.
/// </remarks>
/// <param name="Href">The target page's relative href, exactly as it appears in the demo navigation.</param>
/// <param name="LabelKey">The demo resource key of the anchor text (the target page's nav title).</param>
public sealed record DemoCrossLink(string Href, string LabelKey)
{
    /// <summary>The component-level reference for the data-display vocabulary.</summary>
    public static DemoCrossLink DataDisplay { get; } = new("data-display", "TmDemo_ReferenceDataDisplay");

    /// <summary>The component-level reference for the chart vocabulary.</summary>
    public static DemoCrossLink Charts { get; } = new("charts", "TmDemo_ReferenceCharts");

    /// <summary>The component-level reference for the form-input vocabulary.</summary>
    public static DemoCrossLink Forms { get; } = new("forms", "TmDemo_ReferenceForms");

    /// <summary>The assembled reporting scenario.</summary>
    public static DemoCrossLink AnalyticsKit { get; } = new("analytics-kit", "TmDemo_ScenarioAnalyticsKit");

    /// <summary>The assembled generated-trace scenario.</summary>
    public static DemoCrossLink TraceWaterfall { get; } = new("trace-waterfall", "TmDemo_ScenarioTraceWaterfall");
}

/// <summary>
/// The cross-link sets the demo pages render: one set per page, so the component that draws the row and
/// the tests that pin it read the same hrefs and resource keys.
/// </summary>
public static class DemoCrossLinkSets
{
    /// <summary>The references a scenario page names: <c>/analytics-kit</c>.</summary>
    public static IReadOnlyList<DemoCrossLink> ComponentReference { get; } =
        [DemoCrossLink.DataDisplay, DemoCrossLink.Charts, DemoCrossLink.Forms];

    /// <summary>The single reference a focused scenario page names: <c>/trace-waterfall</c>.</summary>
    public static IReadOnlyList<DemoCrossLink> DataDisplayReference { get; } = [DemoCrossLink.DataDisplay];

    /// <summary>The scenario a data-display or form-input section is assembled into.</summary>
    public static IReadOnlyList<DemoCrossLink> AnalyticsKitScenario { get; } = [DemoCrossLink.AnalyticsKit];

    /// <summary>The scenario the trace-waterfall section is assembled into.</summary>
    public static IReadOnlyList<DemoCrossLink> TraceWaterfallScenario { get; } = [DemoCrossLink.TraceWaterfall];

    /// <summary>
    /// Every set the demo pages render, keyed by its role. Exposed so a guard can walk them all (relative
    /// hrefs, demo-owned keys, a nav title behind every anchor) instead of enumerating them by hand.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<DemoCrossLink>> All { get; } =
        new Dictionary<string, IReadOnlyList<DemoCrossLink>>(StringComparer.Ordinal)
        {
            ["component-reference"] = ComponentReference,
            ["data-display-reference"] = DataDisplayReference,
            ["analytics-kit-scenario"] = AnalyticsKitScenario,
            ["trace-waterfall-scenario"] = TraceWaterfallScenario,
        };
}
