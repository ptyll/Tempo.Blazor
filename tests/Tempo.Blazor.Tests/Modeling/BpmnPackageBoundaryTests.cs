using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// The BPMN XML converter is headless by contract (docs/modeling-bpmn-xml.md §1/§3): PromptHelper
/// runs it on a server, so the package must depend on Tempo.Blazor.Abstractions only and must never
/// drag Blazor/ASP.NET/JS-interop assemblies into the closure. Like the other headless packages
/// (DocumentFormats, EmailTemplates.Engine) it is deliberately NOT part of Tempo.Blazor.All.
/// </summary>
public sealed class BpmnPackageBoundaryTests
{
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.AspNetCore.", "Microsoft.JSInterop",
    ];

    [Fact]
    public void BpmnXml_ReferencesAbstractionsOnly()
    {
        var csproj = Load("src/Tempo.Blazor.Modeling.BpmnXml/Tempo.Blazor.Modeling.BpmnXml.csproj");

        ProjectReferences(csproj).Should().Equal(
            ["../Tempo.Blazor.Abstractions/Tempo.Blazor.Abstractions.csproj"]);
        PackageReferences(csproj).Should().OnlyContain(package =>
            package.StartsWith("Microsoft.Extensions.DependencyInjection.Abstractions", StringComparison.Ordinal));
    }

    [Fact]
    public void BpmnXmlAssembly_ReferencesNoUIOrServerStack()
    {
        var referenced = typeof(IBpmnXmlImporter).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

        foreach (var forbidden in ForbiddenAssemblyPrefixes)
        {
            referenced.Should().NotContain(name => name.StartsWith(forbidden, StringComparison.Ordinal),
                "the headless converter must stay free of {0}", forbidden);
        }

        referenced.Should().NotContain(name =>
                name == "Tempo.Blazor" ||
                (name.StartsWith("Tempo.Blazor.", StringComparison.Ordinal)
                    && name != "Tempo.Blazor.Abstractions"
                    && !name.StartsWith("Tempo.Blazor.Modeling.BpmnXml", StringComparison.Ordinal)),
            "the headless converter may only reach Tempo.Blazor.Abstractions inside the Tempo.Blazor family");

        referenced.Should().Contain("Tempo.Blazor.Abstractions");
    }

    [Fact]
    public void Metapackage_DoesNotReferenceTheHeadlessConverter()
    {
        // Precedent: DocumentFormats and EmailTemplates.Engine are server-side packages and are not
        // part of Tempo.Blazor.All; the BPMN converter follows the same rule.
        var csproj = Load("src/Tempo.Blazor.All/Tempo.Blazor.All.csproj");

        ProjectReferences(csproj).Should().NotContain(reference =>
            reference.Contains("Tempo.Blazor.Modeling.BpmnXml", StringComparison.Ordinal));
    }

    [Fact]
    public void BpmnXml_Version_matches_the_Modeling_package()
    {
        ProjectMetadata("src/Tempo.Blazor.Modeling.BpmnXml/Tempo.Blazor.Modeling.BpmnXml.csproj")
            .Should().Be(ProjectMetadata("src/Tempo.Blazor.Modeling/Tempo.Blazor.Modeling.csproj"),
                "the converter ships in the same 2.11.0 modeling release");
    }

    [Fact]
    public void Hostile_fixtures_are_documented_and_never_in_the_plain_corpus()
    {
        var hostileFiles = Directory.GetFiles(BpmnCorpusPaths.HostileDirectory, "*.bpmn")
            .Select(Path.GetFileName)
            .ToList();
        hostileFiles.Should().HaveCountGreaterThanOrEqualTo(6);

        var readme = File.ReadAllText(Path.Combine(BpmnCorpusPaths.HostileDirectory, "README.md"));
        foreach (var file in hostileFiles)
        {
            readme.Should().Contain(file, $"Hostile/README.md must document {file}");
        }

        var guard = File.ReadAllText(Path.Combine(BpmnCorpusPaths.CorpusDirectory, "..", "BpmnCorpusTests.cs"));
        foreach (var file in hostileFiles)
        {
            guard.Should().NotContain($"\"{file}\"",
                $"hostile fixture {file} must stay outside ExpectedCorpusFiles");
        }
    }

    private static string? ProjectMetadata(string relativePath)
    {
        var csproj = Load(relativePath);
        return csproj.Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Version" && element.Parent?.Name.LocalName == "PropertyGroup")
            ?.Value;
    }

    private static List<string> ProjectReferences(XDocument csproj) => csproj.Descendants()
        .Where(element => element.Name.LocalName == "ProjectReference")
        .Select(element => (element.Attribute("Include")?.Value ?? string.Empty).Replace('\\', '/'))
        .ToList();

    private static List<string> PackageReferences(XDocument csproj) => csproj.Descendants()
        .Where(element => element.Name.LocalName == "PackageReference")
        .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
        .ToList();

    private static XDocument Load(string relativePath) =>
        XDocument.Load(Path.Combine(RepositoryRoot(), relativePath));

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
