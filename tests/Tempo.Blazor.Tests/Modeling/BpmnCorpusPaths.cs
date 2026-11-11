namespace Tempo.Blazor.Tests.Modeling;

/// <summary>Locates the hand-authored BPMN corpus on disk (same convention as <c>BpmnCorpusTests</c>).</summary>
internal static class BpmnCorpusPaths
{
    public static string CorpusDirectory
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current is not null && !File.Exists(Path.Combine(current.FullName, "TempoBlazor.slnx")))
            {
                current = current.Parent;
            }

            if (current is null)
            {
                throw new InvalidOperationException("TempoBlazor.slnx must be findable above the test output directory");
            }

            return Path.Combine(current.FullName, "tests", "Tempo.Blazor.Tests", "Modeling", "BpmnCorpus");
        }
    }

    public static string HostileDirectory => Path.Combine(CorpusDirectory, "Hostile");

    public static string ExpectedDirectory => Path.Combine(CorpusDirectory, "Expected");

    // Line endings are normalized to LF at read time so the importer's captured text (documentation,
    // loss messages) is identical across checkouts (core.autocrlf) and platforms — the golden files
    // in Expected/ are compared byte-for-byte after the same normalization.
    public static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(CorpusDirectory, fileName)).ReplaceLineEndings("\n");

    public static string ReadHostileFixture(string fileName) =>
        File.ReadAllText(Path.Combine(HostileDirectory, fileName)).ReplaceLineEndings("\n");
}

/// <summary>The corpus fixtures covered by the import/export golden suites (kept in one place).</summary>
internal static class BpmnImportGoldenFixtures
{
    public static readonly string[] All =
    [
        "simple-process.bpmn",
        "collaboration-3-pools.bpmn",
        "nested-lanes.bpmn",
        "all-gateways.bpmn",
        "all-events.bpmn",
        "boundary-events.bpmn",
        "event-subprocess.bpmn",
        "call-activity.bpmn",
        "data-objects.bpmn",
        "no-bpmndi.bpmn",
        "partial-bpmndi.bpmn",
        "camunda-export.bpmn",
        "signavio-export.bpmn",
        "bizagi-export.bpmn",
        "annotations-group.bpmn",
        "text-annotation-text.bpmn",
        "unsupported-constructs.bpmn",
        "prefixed-qnames.bpmn",
        "nested-subprocess.bpmn",
        "collapsed-subprocess.bpmn",
        "black-box-pool.bpmn",
        "multiple-events.bpmn",
        "io-properties.bpmn",
    ];
}
