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

    public static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(CorpusDirectory, fileName));

    public static string ReadHostileFixture(string fileName) => File.ReadAllText(Path.Combine(HostileDirectory, fileName));
}
