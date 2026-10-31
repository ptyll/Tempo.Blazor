using System.Xml.Linq;
using FluentAssertions;
using Tempo.Blazor.Configuration;

namespace Tempo.Blazor.Tests.Configuration;

/// <summary>
/// Guards the split-package contract of <c>Tempo.Blazor.AI</c> — the same shape
/// <see cref="SigningSplitTests"/> pins for Signing: the package owns its registration entry point
/// and its static assets, the core package stays free of it, and the dependency list is narrow
/// enough to prove the AI package carries no LLM SDK (plan rule: the models are generic and live in
/// <c>Tempo.Blazor.Abstractions</c>, so a backend can reference them without Blazor).
/// </summary>
public sealed class AiSplitTests
{
    /// <summary>
    /// The package's only NuGet dependencies are the per-TFM Razor component assembly and the
    /// Markdown processor. Anything else — an LLM SDK, a PromptHelper client — fails this list.
    /// </summary>
    private static readonly string[] AllowedPackageReferences =
    [
        "Microsoft.AspNetCore.Components.Web",
        "Markdig"
    ];

    /// <summary>The package sits on top of the core library and the UI-free abstractions only.</summary>
    private static readonly string[] AllowedProjectReferences =
    [
        "Tempo.Blazor.Abstractions",
        "Tempo.Blazor"
    ];

    /// <summary>
    /// The non-framework assemblies the compiled <c>Tempo.Blazor.AI</c> assembly may reference.
    /// Everything else must be a <c>System.*</c> or <c>Microsoft.*</c> framework assembly, so a new
    /// package that never passed the csproj guard still fails once it is actually used from code.
    /// </summary>
    private static readonly string[] AllowedAssemblyReferences =
    [
        "Markdig",
        "mscorlib",
        "netstandard",
        "Tempo.Blazor",
        "Tempo.Blazor.Abstractions"
    ];

    [Fact]
    public void AddTempoBlazorAll_references_ai_package_and_keeps_ai_registration_callable()
    {
        typeof(AllServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should()
            .Contain("Tempo.Blazor.AI");

        typeof(AIServiceCollectionExtensions)
            .GetMethod("AddTempoBlazorAI")
            .Should()
            .NotBeNull("the compatibility package drives the split registration by name");
    }

    [Fact]
    public void Core_package_does_not_reference_ai_package()
    {
        typeof(ServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Should()
            .NotContain("Tempo.Blazor.AI");

        typeof(ServiceCollectionExtensions)
            .GetMethods()
            .Select(method => method.Name)
            .Should()
            .NotContain("AddTempoBlazorAI");
    }

    [Fact]
    public void Ai_package_ships_its_own_stylesheet_entry_point()
    {
        var root = FindRepositoryRoot();
        var aiCssRoot = Path.Combine(root, "src", "Tempo.Blazor.AI", "wwwroot", "css");
        var aiEntryCss = Path.Combine(aiCssRoot, "tempo-blazor-ai.css");
        var aiPartialCss = Path.Combine(aiCssRoot, "components", "_ai-streaming.css");
        var coreCssRoot = Path.Combine(root, "src", "Tempo.Blazor", "wwwroot", "css");

        File.Exists(aiEntryCss).Should().BeTrue("the package publishes _content/Tempo.Blazor.AI/css/tempo-blazor-ai.css");
        File.Exists(aiPartialCss).Should().BeTrue("tempo-blazor-ai.css is an @import manifest over components/*.css");
        File.Exists(Path.Combine(coreCssRoot, "tempo-blazor-ai.css")).Should().BeFalse(
            "static assets belong to the split package, never to the core package");

        var imports = File.ReadAllText(aiEntryCss);
        imports.Should().Contain("@import \"components/_ai-streaming.css\";");

        var coreImports = File.ReadAllText(Path.Combine(coreCssRoot, "tempo-blazor.css"));
        coreImports.Should().NotContain("components/_ai-streaming.css");

        var project = File.ReadAllText(Path.Combine(root, "src", "Tempo.Blazor.AI", "Tempo.Blazor.AI.csproj"));
        project.Should().Contain("Sdk=\"Microsoft.NET.Sdk.Razor\"");
        project.Should().Contain("<PackageId>Tempo.Blazor.AI</PackageId>");
        project.Should().Contain("<GenerateDocumentationFile>true</GenerateDocumentationFile>");
        project.Should().Contain("<_Parameter1>Tempo.Blazor.Tests</_Parameter1>");
        File.Exists(Path.Combine(root, "src", "Tempo.Blazor.AI", "README.md")).Should().BeTrue(
            "the package ships its own README (PackageReadmeFile)");
    }

    /// <summary>
    /// The AI package is generic: the models are plain contracts in <c>Tempo.Blazor.Abstractions</c>
    /// and the markdown pipeline is the only third-party code. An LLM SDK or a PromptHelper client
    /// reference is the mistake this test exists for, so the list is asserted by SET, not "contains".
    /// </summary>
    [Fact]
    public void Ai_package_has_no_llm_sdk_or_prompthelper_dependency()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(Path.Combine(root, "src", "Tempo.Blazor.AI", "Tempo.Blazor.AI.csproj"));

        var packages = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        packages.Should().BeSubsetOf(AllowedPackageReferences,
            "the AI package must stay free of LLM SDKs and PromptHelper — it is a UI + generic contract "
            + "package, not a client");
        packages.Should().Contain("Markdig", "the safe markdown pipeline is the package's one third-party dependency");

        var projects = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        projects.Should().BeEquivalentTo(AllowedProjectReferences);
    }

    /// <summary>
    /// The AI model and streaming contracts are planned for <c>Tempo.Blazor.Abstractions</c> (plan
    /// 7f21f26b Fáze 2) precisely because that package has zero UI dependencies — a backend references
    /// the records without pulling in Blazor. A Razor component assembly, the markdown processor or a
    /// reference back to the core/AI package would destroy that property before the first contract
    /// lands, so this guard runs on the PROJECT file.
    /// </summary>
    [Fact]
    public void Abstractions_stays_ui_free_for_ai_contracts()
    {
        var root = FindRepositoryRoot();
        var document = XDocument.Load(
            Path.Combine(root, "src", "Tempo.Blazor.Abstractions", "Tempo.Blazor.Abstractions.csproj"));

        var packages = document.Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();

        packages.Should().NotContain(
            name => name.StartsWith("Microsoft.AspNetCore", StringComparison.OrdinalIgnoreCase),
            "Abstractions must stay free of the Razor/ASP.NET Core stack — it is the package a backend "
            + "referencing the AI contracts is allowed to depend on");
        packages.Should().NotContain(
            name => string.Equals(name, "Markdig", StringComparison.OrdinalIgnoreCase),
            "the markdown processor belongs to the UI package (Tempo.Blazor.AI); the contracts it feeds "
            + "are plain records");

        var projects = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => Path.GetFileNameWithoutExtension(value!.Replace('\\', '/')))
            .ToList();

        projects.Should().NotContain(
            AllowedProjectReferences,
            "Abstractions is the BOTTOM of the dependency graph — the core library and the AI package "
            + "sit on top of it, never the other way round");
    }

    /// <summary>
    /// The compiled package, not just its csproj: a transitive package can only reach the assembly
    /// through an assembly reference, so the reference table is the honest dependency list. The
    /// allowlist is <c>System.*</c>/<c>Microsoft.*</c> (the framework), Markdig, and the package's own
    /// Tempo stack — the shape an LLM SDK or a PromptHelper client cannot pass.
    /// </summary>
    [Fact]
    public void Ai_package_references_only_system_microsoft_and_its_own_stack()
    {
        var references = typeof(AIServiceCollectionExtensions).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();

        references.Should().NotBeEmpty(
            "an assembly with no references at all cannot implement the registration entry point");
        references.Should().Contain("Tempo.Blazor",
            "AddTempoBlazorAI() builds on the core registration");

        var unexpected = references
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !AllowedAssemblyReferences.Contains(name, StringComparer.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        unexpected.Should().BeEmpty(
            "Tempo.Blazor.AI may only reference System.*, Microsoft.*, Markdig, netstandard/mscorlib "
            + "and its own Tempo.Blazor stack — anything else is a dependency smuggled past the csproj guards");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TempoBlazor.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
