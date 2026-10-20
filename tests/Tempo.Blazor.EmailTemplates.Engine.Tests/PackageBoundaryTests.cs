using System.Xml.Linq;
using Tempo.Blazor.EmailTemplates.Abstractions.Rendering;

namespace Tempo.Blazor.EmailTemplates.Engine.Tests;

/// <summary>
/// The package boundary is part of the host contract (§3.0/D11, §4 point 4): Abstractions must stay free
/// of Scriban/Mjml.Net/AngleSharp (it is what the editor references in Blazor WebAssembly), the UI package
/// must not reference the Engine (otherwise Mjml.Net comes back into the browser), and the Engine must pin
/// Abstractions to an exact version because it reaches internal members through <c>InternalsVisibleTo</c>.
/// </summary>
public class PackageBoundaryTests
{
    private static readonly string[] EngineDependencies = ["Scriban", "Mjml.Net", "AngleSharp"];

    [Theory]
    [InlineData("Scriban")]
    [InlineData("Mjml.Net")]
    [InlineData("AngleSharp")]
    public void Abstractions_HasNoEngineDependency(string packageId)
    {
        var csproj = Load("src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj");

        PackageReferences(csproj).Should().NotContain(packageId,
            "the editor runs in the browser through Abstractions, so {0} must live in the Engine package", packageId);
    }

    [Fact]
    public void AbstractionsAssembly_ReferencesNoEngineDependency()
    {
        var referenced = typeof(IHtmlContentSanitizer).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        foreach (var dependency in EngineDependencies)
        {
            referenced.Should().NotContain(dependency,
                "the compiled Abstractions assembly must not drag {0} into a WebAssembly download", dependency);
        }
    }

    [Fact]
    public void Engine_ReferencesTheThreeEngineDependencies()
    {
        var csproj = Load("src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj");
        var references = PackageReferences(csproj).ToList();

        foreach (var dependency in EngineDependencies)
        {
            references.Should().Contain(dependency);
        }
    }

    [Fact]
    public void Engine_PinsAbstractionsToAnExactVersion()
    {
        // NuGet's pack IGNORES a Version metadata on a ProjectReference (the produced nuspec said "2.9.1",
        // a minimum range), so the exact range is written onto the item the pack task actually reads. The
        // range is the package's own $(Version), i.e. [2.10.0] once the release version is set.
        var csproj = Load("src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj");

        var target = csproj.Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "Target" &&
                                  (string?)e.Attribute("Name") == "PinAbstractionsToTheExactVersion");
        target.Should().NotBeNull("the exact version range is what keeps InternalsVisibleTo from drifting silently");
        target!.Attribute("AfterTargets")?.Value.Should().Be("_GetProjectReferenceVersions");
        target.Descendants().Should().Contain(e => e.Name.LocalName == "_ProjectReferencesWithVersions");
        target.Descendants().Should().Contain(e =>
            e.Name.LocalName == "ProjectVersion" && e.Value.Contains("[$(Version)]", StringComparison.Ordinal));
    }

    [Fact]
    public void EngineAndAbstractions_DeclareTheSameVersion()
    {
        // The pin is [$(Version)] of the ENGINE package, so the two versions must be bumped together —
        // otherwise the packed dependency demands a version of Abstractions that will never be published.
        var engineVersion = ProjectMetadata("src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj");
        var abstractionsVersion = ProjectMetadata("src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj");

        engineVersion.Should().Be(abstractionsVersion);
    }

    private static string? ProjectMetadata(string relativePath)
    {
        var csproj = Load(relativePath);
        return csproj.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "Version" && e.Parent?.Name.LocalName == "PropertyGroup")
            ?.Value;
    }

    [Fact]
    public void EditorPackage_DoesNotReferenceTheEngine()
    {
        var csproj = Load("src/Tempo.Blazor.EmailTemplates/Tempo.Blazor.EmailTemplates.csproj");

        ProjectReferences(csproj).Should().NotContain(r =>
            r.Path.Contains("Tempo.Blazor.EmailTemplates.Engine", StringComparison.Ordinal));
        PackageReferences(csproj).Should().NotContain("Tempo.Blazor.EmailTemplates.Engine");
        foreach (var dependency in EngineDependencies)
        {
            PackageReferences(csproj).Should().NotContain(dependency);
        }
    }

    [Fact]
    public void Metapackage_DoesNotReferenceTheEnginePackage()
    {
        // Deliberate: Tempo.Blazor.All is the UI compatibility bundle and neither of the email packages is
        // part of it. Referencing the Engine from All would put Scriban/Mjml.Net/AngleSharp into every
        // All consumer — exactly what D11 splits them out for.
        var csproj = Load("src/Tempo.Blazor.All/Tempo.Blazor.All.csproj");

        ProjectReferences(csproj).Should().NotContain(r =>
            r.Path.Contains("Tempo.Blazor.EmailTemplates.Engine", StringComparison.Ordinal));
        ProjectReferences(csproj).Should().NotContain(r =>
            r.Path.Contains("Tempo.Blazor.EmailTemplates", StringComparison.Ordinal));
    }

    [Fact]
    public void WasmDemo_ReferencesTheEditorTransitively_ButNeverTheEngine()
    {
        // The proof that D11 reaches the browser: the WASM download is built from this closure, so an
        // Engine reference — direct, or through Demo.SharedUI — would put Scriban, Mjml.Net and
        // AngleSharp back into WebAssembly even though the editor package itself is clean. The Engine
        // tests pin the csproj lists; this pins the project graph a real host actually has.
        var closure = ProjectClosure("src/Tempo.Blazor.Demo/Tempo.Blazor.Demo.csproj");

        closure.Should().NotContain("src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj",
            "the WASM demo renders its preview on the demo API through IEmailPreviewRenderer");
        closure.Should().Contain("src/Tempo.Blazor.EmailTemplates/Tempo.Blazor.EmailTemplates.csproj",
            "…otherwise the assertion above would be vacuous: the editor really is part of the demo");
        closure.Should().Contain(
            "src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj");
    }

    [Fact]
    public void EditorPackage_DoesNotReachTheEngineTransitively()
    {
        // The csproj list above proves the direct references only. A ProjectReference added to an
        // intermediate project (a shared UI project, a helper package) would bring Mjml.Net/AngleSharp back
        // into every consumer of the editor without touching the editor's own csproj — so the closure itself
        // is what has to be clean.
        var closure = ProjectClosure("src/Tempo.Blazor.EmailTemplates/Tempo.Blazor.EmailTemplates.csproj");

        closure.Should().NotContain("src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj",
            "the editor is the WASM half of the split");
        closure.Should().Contain(
            "src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj",
            "…otherwise the assertion above would be vacuous");
    }

    [Fact]
    public void Abstractions_HasNoProjectReferences()
    {
        // Abstractions is the shared contract assembly: one project reference would drag its whole closure
        // (and possibly the engine) into every API/WASM consumer. It must depend on packages only.
        var csproj = Load("src/Tempo.Blazor.EmailTemplates.Abstractions/Tempo.Blazor.EmailTemplates.Abstractions.csproj");

        ProjectReferences(csproj).Should().BeEmpty(
            "Abstractions is the shared half of the split and must stay a leaf project");
    }

    [Fact]
    public void ReportServerWasmClient_IsTheOnlyWasmHostWithTheEngine()
    {
        // Decision of the phase-6 review (contract §3.0/§6): the demo report server client renders the report
        // e-mail locally in the browser, so it deliberately references the Engine even though it is
        // Blazor WebAssembly. That exception is demo-only (IsPackable=false, unpublished) and it must stay
        // the ONLY one: every other WebAssembly host has to render through IEmailPreviewRenderer.
        string[] wasmHosts =
        [
            "src/Tempo.Blazor.Demo/Tempo.Blazor.Demo.csproj",
            "src/Tempo.Blazor.Demo.InteractiveAuto/Tempo.Blazor.Demo.InteractiveAuto.csproj",
            "src/Tempo.ReportServer.Web.Client/Tempo.ReportServer.Web.Client.csproj",
        ];

        var withEngine = wasmHosts
            .Where(host => ProjectClosure(host).Contains(
                "src/Tempo.Blazor.EmailTemplates.Engine/Tempo.Blazor.EmailTemplates.Engine.csproj"))
            .ToList();

        withEngine.Should().BeEquivalentTo(["src/Tempo.ReportServer.Web.Client/Tempo.ReportServer.Web.Client.csproj"],
            "the report-server demo is the documented single exception; a second one would negate D11");

        var csproj = Load("src/Tempo.ReportServer.Web.Client/Tempo.ReportServer.Web.Client.csproj");
        ((string?)csproj.Descendants().FirstOrDefault(e => e.Name.LocalName == "IsPackable")?.Value)
            .Should().Be("false", "the exception is only acceptable while the client is not published");
    }

    [Fact]
    public void WasmDemo_StaysOnTheEditorPlusHostRendererPattern()
    {
        // The regression this pins: swapping the two calls for the legacy AddTempoEmailTemplates() (or
        // adding AddTempoEmailTemplateEngine()) compiles fine and silently pulls the engine into the
        // browser. The demo is the reference host, so its registration is part of the contract.
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "src/Tempo.Blazor.Demo/Program.cs"));

        program.Should().Contain("AddTempoEmailTemplateEditor()");
        program.Should().Contain("IEmailPreviewRenderer",
            "the WASM host renders the preview through the API, which is the pattern the contract documents");
        program.Should().NotContain("AddTempoEmailTemplateEngine(",
            "the engine belongs to the demo API, never to the WebAssembly host");
    }

    /// <summary>
    /// The transitive <c>ProjectReference</c> closure of <paramref name="relativePath"/>, as
    /// repository-relative csproj paths. PackageReferences are covered by the Engine's own project being
    /// absent: it is the only project that pulls Scriban/Mjml.Net/AngleSharp.
    /// </summary>
    private static List<string> ProjectClosure(string relativePath)
    {
        var root = RepositoryRoot();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        var closure = new List<string>();
        queue.Enqueue(relativePath);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!seen.Add(current))
            {
                continue;
            }

            closure.Add(current);
            var full = Path.Combine(root, current);
            if (!File.Exists(full))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(full)!;
            foreach (var reference in ProjectReferences(XDocument.Load(full)))
            {
                var resolved = Path.GetFullPath(Path.Combine(directory, reference.Path));
                queue.Enqueue(Path.GetRelativePath(root, resolved).Replace('\\', '/'));
            }
        }

        return closure;
    }

    private static (string Path, string? Version)[] ProjectReferences(XDocument csproj)
        => csproj.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (
                Path: Normalize(e.Attribute("Include")?.Value ?? string.Empty),
                Version: e.Attribute("Version")?.Value))
            .ToArray();

    private static IEnumerable<string> PackageReferences(XDocument csproj)
        => csproj.Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => e.Attribute("Include")?.Value ?? string.Empty)
            .Where(value => value.Length > 0);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static XDocument Load(string relativePath)
    {
        var root = RepositoryRoot();
        return XDocument.Load(Path.Combine(root, relativePath));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must find the repository root (TempoBlazor.slnx) above its bin folder");
        return directory!.FullName;
    }
}
