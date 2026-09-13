using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace JsonDocumentationGenerator;

internal static class PackageDocumentationGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static int Run(string[] args)
    {
        var cli = CliOptions.Parse(args);
        var baseDir = ResolveJsonDocumentationDir(cli.BaseDirectory);
        if (baseDir is null)
        {
            Console.Error.WriteLine("Error: Could not find JsonDocumentation directory.");
            Console.Error.WriteLine("Usage: JsonDocumentationGenerator [JsonDocumentation-dir] [generate|validate|list-missing|enrich] [--package PackageId] [--output-dir path]");
            return 1;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var configPath = Path.Combine(baseDir, "packages.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Error: package configuration not found: {configPath}");
            return 1;
        }

        var config = JsonSerializer.Deserialize<PackageDocumentationConfig>(
            File.ReadAllText(configPath), JsonOptions);
        if (config?.Packages is null || config.Packages.Count == 0)
        {
            Console.Error.WriteLine($"Error: package configuration contains no packages: {configPath}");
            return 1;
        }

        var packages = config.Packages
            .Where(p => cli.PackageId is null || string.Equals(p.PackageId, cli.PackageId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (packages.Count == 0)
        {
            Console.Error.WriteLine($"Error: package not found in configuration: {cli.PackageId}");
            return 1;
        }

        var generator = new PackageDocumentationComposer(baseDir, repoRoot, cli.OutputDirectory, config.Packages);
        var results = packages.Select(generator.Compose).ToList();

        return cli.Command switch
        {
            GeneratorCommand.Generate => WriteOutputs(results, config, repoRoot, cli.OutputDirectory, writeAggregate: cli.PackageId is null),
            GeneratorCommand.Validate => Validate(results, repoRoot, config.Packages, failOnManualDrift: cli.FailOnDrift),
            GeneratorCommand.ListMissing => ListMissing(results),
            GeneratorCommand.Enrich => EnrichSourceJson(results),
            GeneratorCommand.Repair => RepairDescriptions(results),
            _ => 1
        };
    }

    private static int WriteOutputs(
        IReadOnlyList<PackageDocumentationResult> results,
        PackageDocumentationConfig config,
        string repoRoot,
        string? outputDirectory,
        bool writeAggregate)
    {
        foreach (var result in results)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(result.OutputPath)!);
            var json = result.Document.ToJsonString(JsonOptions);
            File.WriteAllText(result.OutputPath, json);

            foreach (var aliasPath in result.AliasOutputPaths)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(aliasPath)!);
                File.WriteAllText(aliasPath, json);
            }

            Console.WriteLine($"Generated: {result.OutputPath}");
            foreach (var alias in result.AliasOutputPaths)
            {
                Console.WriteLine($"  Alias: {alias}");
            }

            Console.WriteLine($"  Package: {result.Config.PackageId}");
            Console.WriteLine($"  Items: {result.ItemCount}");
            Console.WriteLine($"  Components discovered: {result.DiscoveredComponentCount}");
            Console.WriteLine($"  Public API types discovered: {result.DiscoveredTypeCount}");
            Console.WriteLine($"  Manual JSON items: {result.ManualItemCount}");
            Console.WriteLine($"  Generated-only items: {result.GeneratedOnlyCount}");
            Console.WriteLine($"  File size: {json.Length:N0} bytes");
            Console.WriteLine();
        }

        if (writeAggregate && !string.IsNullOrWhiteSpace(config.AggregateOutputFile))
        {
            var outputDir = ResolveOutputDirectory(repoRoot, outputDirectory);
            var aggregatePath = Path.Combine(outputDir, config.AggregateOutputFile);
            var aggregate = BuildAggregateDocument(results);
            var json = aggregate.ToJsonString(JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(aggregatePath)!);
            File.WriteAllText(aggregatePath, json);
            Console.WriteLine($"Generated aggregate: {aggregatePath}");
            Console.WriteLine($"  Packages: {results.Count}");
            Console.WriteLine($"  Items: {results.Sum(r => r.ItemCount)}");
            Console.WriteLine($"  File size: {json.Length:N0} bytes");
            Console.WriteLine();
        }

        return 0;
    }

    private static JsonObject BuildAggregateDocument(IReadOnlyList<PackageDocumentationResult> results)
    {
        var packages = new JsonArray();
        foreach (var result in results)
        {
            packages.Add(result.Document.DeepClone());
        }

        return new JsonObject
        {
            ["packages"] = packages,
            ["summary"] = new JsonObject
            {
                ["packageCount"] = results.Count,
                ["itemCount"] = results.Sum(r => r.ItemCount),
                ["manualItemCount"] = results.Sum(r => r.ManualItemCount),
                ["generatedOnlyItemCount"] = results.Sum(r => r.GeneratedOnlyCount),
                ["componentCount"] = results.Sum(r => r.DiscoveredComponentCount),
                ["publicApiTypeCount"] = results.Sum(r => r.DiscoveredTypeCount)
            }
        };
    }

    private static string ResolveOutputDirectory(string repoRoot, string? outputDirectory)
        => string.IsNullOrWhiteSpace(outputDirectory)
            ? repoRoot
            : Path.GetFullPath(Path.IsPathRooted(outputDirectory) ? outputDirectory : Path.Combine(repoRoot, outputDirectory));

    private static int Validate(
        IReadOnlyList<PackageDocumentationResult> results,
        string repoRoot,
        IReadOnlyList<PackageDocumentationPackage> configuredPackages,
        bool failOnManualDrift)
    {
        var errors = new List<string>();

        foreach (var result in results)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = result.Document["items"]?.AsArray() ?? [];
            if (items.Count == 0)
            {
                errors.Add($"{result.Config.PackageId}: output has no items.");
            }

            foreach (var item in items.OfType<JsonObject>())
            {
                var itemName = item.GetString("itemName");
                var kind = item.GetString("kind");
                var description = item.GetString("description");
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    errors.Add($"{result.Config.PackageId}: item without itemName.");
                    continue;
                }

                if (!seen.Add(itemName))
                {
                    errors.Add($"{result.Config.PackageId}: duplicate itemName '{itemName}'.");
                }

                if (string.IsNullOrWhiteSpace(kind))
                {
                    errors.Add($"{result.Config.PackageId}: {itemName} has no kind.");
                }

                if (string.IsNullOrWhiteSpace(description))
                {
                    errors.Add($"{result.Config.PackageId}: {itemName} has no description.");
                }

                if (string.Equals(kind, "Component", StringComparison.OrdinalIgnoreCase)
                    && item["requiredImports"] is not JsonArray { Count: > 0 })
                {
                    errors.Add($"{result.Config.PackageId}: component {itemName} has no requiredImports.");
                }

                if (!string.Equals(kind, "Component", StringComparison.OrdinalIgnoreCase)
                    && string.IsNullOrWhiteSpace(item.GetString("namespace")))
                {
                    errors.Add($"{result.Config.PackageId}: API item {itemName} has no namespace.");
                }
            }

            if (failOnManualDrift && result.GeneratedOnlyCount > 0)
            {
                errors.Add($"{result.Config.PackageId}: {result.GeneratedOnlyCount} generated items do not have source JSON files.");
            }
        }

        var configuredPackageIds = configuredPackages.Select(p => p.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var projectPackageIds = Directory.GetFiles(Path.Combine(repoRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(p => !IsUnderBuildOutput(p))
            .Select(ProjectMetadataReader.Read)
            .Where(m => !string.IsNullOrWhiteSpace(m.PackageId))
            .Select(m => m.PackageId!)
            .ToList();

        foreach (var packageId in projectPackageIds)
        {
            if (!configuredPackageIds.Contains(packageId))
            {
                errors.Add($"PackageId '{packageId}' is not configured in JsonDocumentation/packages.json.");
            }
        }

        if (errors.Count == 0)
        {
            Console.WriteLine("Validation passed.");
            foreach (var result in results)
            {
                Console.WriteLine($"  {result.Config.PackageId}: {result.ItemCount} items ({result.GeneratedOnlyCount} generated-only).");
            }

            return 0;
        }

        Console.Error.WriteLine("Validation failed:");
        foreach (var error in errors)
        {
            Console.Error.WriteLine($"  - {error}");
        }

        return 1;
    }

    private static int ListMissing(IReadOnlyList<PackageDocumentationResult> results)
    {
        foreach (var result in results)
        {
            Console.WriteLine(result.Config.PackageId);
            foreach (var item in result.GeneratedOnlyItems.OrderBy(i => i.Category).ThenBy(i => i.ItemName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"  {item.Kind,-10} {item.Category,-18} {item.ItemName} ({item.SourcePath})");
            }

            if (result.GeneratedOnlyItems.Count == 0)
            {
                Console.WriteLine("  No generated-only items.");
            }
        }

        return 0;
    }

    private static int EnrichSourceJson(IReadOnlyList<PackageDocumentationResult> results)
    {
        var written = 0;
        foreach (var result in results)
        {
            var root = result.Config.DocumentationRoots.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(root))
            {
                Console.WriteLine($"{result.Config.PackageId}: no documentation root configured, skipping.");
                continue;
            }

            var rootPath = Path.Combine(result.BaseDir, root);
            foreach (var item in result.GeneratedOnlyItems)
            {
                var category = SafePathSegment(item.Category);
                var filePath = Path.Combine(rootPath, category, SafePathSegment(item.ItemName) + ".json");
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                File.WriteAllText(filePath, item.Json.ToJsonString(JsonOptions));
                written++;
                Console.WriteLine($"Wrote: {filePath}");
            }
        }

        Console.WriteLine($"Generated {written} source JSON skeletons.");
        return 0;
    }

    /// <summary>
    /// Rewrites overlay descriptions that an older, buggier parse produced, using the current parse of
    /// the same source. Only descriptions that are provably machine-made are touched: either the
    /// generator's own fallback sentence, or text that is a strict subsequence of the freshly parsed
    /// summary (the old parse could only ever delete characters — it dropped whole
    /// <c>&lt;see cref&gt;</c> references, leaving holes such as "Severity of an ."). A hand-written
    /// description fails both tests and is left alone.
    /// </summary>
    private static int RepairDescriptions(IReadOnlyList<PackageDocumentationResult> results)
    {
        var repaired = 0;
        var unfixable = new List<string>();

        foreach (var result in results)
        {
            foreach (var pair in result.OverlayPairs)
            {
                var overlay = JsonNode.Parse(File.ReadAllText(pair.OverlayFile)) as JsonObject;
                if (overlay is null)
                {
                    continue;
                }

                var changes = 0;
                var current = pair.Overlay.GetString("description");
                var fresh = pair.Generated.GetString("description");
                if (!string.IsNullOrWhiteSpace(current))
                {
                    if (pair.GeneratedIsFallback || string.IsNullOrWhiteSpace(fresh))
                    {
                        if (LooksMachineGenerated(current))
                        {
                            unfixable.Add($"{result.Config.PackageId}: {pair.Overlay.GetString("itemName")} (no summary in source)");
                        }
                    }
                    else if (!string.Equals(current, fresh, StringComparison.Ordinal) && IsStaleGeneratedDescription(current, fresh))
                    {
                        overlay["description"] = fresh;
                        changes++;
                    }
                }

                // Parameter and member descriptions come from the same parse and the overlay copy wins
                // in the merge, so a stale reference hole survives there too.
                var sourceFile = ResolveSourceFile(result.BaseDir, pair.Overlay.GetString("sourcePath"));
                changes += RepairNamedDescriptions(overlay, pair.Generated, "parameters", sourceFile);
                changes += RepairNamedDescriptions(overlay, pair.Generated, "members", sourceFile);

                if (changes == 0)
                {
                    continue;
                }

                WriteOverlay(pair.OverlayFile, overlay);
                repaired += changes;
                Console.WriteLine($"Repaired {changes}: {Path.GetFileName(pair.OverlayFile)}");
            }
        }

        Console.WriteLine($"Repaired {repaired} description(s).");

        var orphans = results.SelectMany(r => r.OrphanOverlays.Select(o => $"{r.Config.PackageId}: {Path.GetFileName(o)}")).ToList();
        if (orphans.Count > 0)
        {
            Console.WriteLine($"{orphans.Count} overlay file(s) document a type that no longer exists in source:");
            foreach (var entry in orphans.OrderBy(e => e, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {entry}");
            }
        }

        if (unfixable.Count > 0)
        {
            Console.WriteLine($"{unfixable.Count} placeholder description(s) still need human prose (the source type has no XML summary):");
            foreach (var entry in unfixable.OrderBy(e => e, StringComparer.Ordinal))
            {
                Console.WriteLine($"  {entry}");
            }
        }

        return 0;
    }

    /// <summary>
    /// Repairs stale descriptions inside a named array (<c>parameters</c> / <c>members</c>), matching
    /// entries by name and applying the same provably-machine-made rule as the type description.
    /// </summary>
    private static int RepairNamedDescriptions(JsonObject overlay, JsonObject generated, string arrayName, string? sourceFile)
    {
        if (overlay[arrayName] is not JsonArray overlayEntries)
        {
            return 0;
        }

        var freshByName = generated[arrayName] is JsonArray generatedEntries
            ? generatedEntries.OfType<JsonObject>()
                .Where(e => !string.IsNullOrWhiteSpace(e.GetString("name")))
                .GroupBy(e => e.GetString("name")!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().GetString("description"), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        var changes = 0;
        foreach (var entry in overlayEntries.OfType<JsonObject>())
        {
            var name = entry.GetString("name");
            var current = entry.GetString("description");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(current))
            {
                continue;
            }

            // Members are only generated for single-type files, so fall back to re-reading the
            // declaring source. Ambiguous names (several declarations with different docs) are left
            // alone rather than guessed at.
            if (!freshByName.TryGetValue(name, out var fresh) || string.IsNullOrWhiteSpace(fresh))
            {
                fresh = UnambiguousMemberDescription(sourceFile, name);
            }

            if (string.IsNullOrWhiteSpace(fresh)
                || string.Equals(current, fresh, StringComparison.Ordinal)
                || !IsStaleGeneratedDescription(current, fresh))
            {
                continue;
            }

            entry["description"] = fresh;
            changes++;
        }

        return changes;
    }

    private static string? ResolveSourceFile(string baseDir, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return null;
        }

        var repoRoot = Path.GetFullPath(Path.Combine(baseDir, ".."));
        var full = Path.Combine(repoRoot, sourcePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(full) ? full : null;
    }

    /// <summary>
    /// The XML summary of a member, read straight from source. Returns <see langword="null"/> when the
    /// file declares the name more than once with differing documentation, so a multi-type file never
    /// donates another type's sentence.
    /// </summary>
    private static string? UnambiguousMemberDescription(string? sourceFile, string memberName)
    {
        if (sourceFile is null)
        {
            return null;
        }

        var candidates = SourceDocParser.ExtractMembers(File.ReadAllText(sourceFile), string.Empty)
            .Where(m => string.Equals(m.Name, memberName, StringComparison.Ordinal))
            .Select(m => m.Description)
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>Whether text is one of the generator's own fallback sentences.</summary>
    private static bool LooksMachineGenerated(string text)
        => Regex.IsMatch(text, @"^Public (class|record|interface|struct|enum|recordstruct) defined by ")
        || Regex.IsMatch(text, @"^\S+ Blazor component in the .+ category\.$");

    private static bool IsStaleGeneratedDescription(string current, string fresh)
    {
        if (LooksMachineGenerated(current))
        {
            return true;
        }

        // A dropped reference leaves a hole no author would type: whitespace before sentence
        // punctuation, empty parentheses that are not a method call (including a generic one such as
        // AddThing<T>()), a doubled space, or a trailing space. Its presence alone proves the text
        // came from the old parse.
        if (Regex.IsMatch(current, @"\s[.,;](?:\s|$)|(?<![\w\])>])\(\s*\)|\s{2,}|\s$"))
        {
            return true;
        }

        // No visible hole, so only accept the text if it is literally the fresh summary with
        // characters deleted — which is all the old parse could ever produce.
        return IsSubsequence(Regex.Replace(current, @"\s+", ""), Regex.Replace(fresh, @"\s+", ""));
    }

    /// <summary>Whether <paramref name="candidate"/> can be obtained from <paramref name="full"/> by deletions only.</summary>
    private static bool IsSubsequence(string candidate, string full)
    {
        var i = 0;
        foreach (var c in full)
        {
            if (i < candidate.Length && candidate[i] == c)
            {
                i++;
            }
        }

        return i == candidate.Length;
    }

    /// <summary>Writes an overlay back, keeping the file's existing trailing-newline convention.</summary>
    private static void WriteOverlay(string path, JsonObject overlay)
    {
        var hadTrailingNewline = File.ReadAllText(path).EndsWith('\n');
        var json = overlay.ToJsonString(JsonOptions);
        File.WriteAllText(path, hadTrailingNewline ? json + "\n" : json);
    }

    private static string? ResolveJsonDocumentationDir(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            var full = Path.GetFullPath(explicitDir);
            return Directory.Exists(full) ? full : null;
        }

        var dir = Directory.GetCurrentDirectory();
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "JsonDocumentation");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }

    internal static bool IsUnderBuildOutput(string path)
        => path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(p => string.Equals(p, "bin", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(p, "obj", StringComparison.OrdinalIgnoreCase));

    private static string SafePathSegment(string value)
    {
        var cleaned = Regex.Replace(value, @"[^A-Za-z0-9._ -]+", "-");
        cleaned = cleaned.Replace(" / ", "-").Replace(' ', '-');
        return string.IsNullOrWhiteSpace(cleaned) ? "General" : cleaned;
    }
}

internal sealed class PackageDocumentationComposer
{
    private readonly string _baseDir;
    private readonly string _repoRoot;
    private readonly string? _outputDirectory;
    private readonly IReadOnlyList<PackageDocumentationPackage> _knownPackages;

    public PackageDocumentationComposer(
        string baseDir,
        string repoRoot,
        string? outputDirectory,
        IReadOnlyList<PackageDocumentationPackage> knownPackages)
    {
        _baseDir = baseDir;
        _repoRoot = repoRoot;
        _outputDirectory = outputDirectory;
        _knownPackages = knownPackages;
    }

    public PackageDocumentationResult Compose(PackageDocumentationPackage config)
    {
        var projectPath = Path.Combine(_repoRoot, config.SourceProject);
        var project = ProjectMetadataReader.Read(projectPath);
        var existingItems = LoadExistingItems(config);

        var generated = new List<GeneratedDocumentationItem>();
        foreach (var componentRoot in config.ComponentRoots)
        {
            var rootPath = Path.Combine(_repoRoot, componentRoot);
            if (Directory.Exists(rootPath))
            {
                generated.AddRange(ComponentDocumentationScanner.Scan(rootPath, _repoRoot, config.PackageId));
            }
        }

        var componentNames = generated
            .Where(i => string.Equals(i.Kind, "Component", StringComparison.OrdinalIgnoreCase))
            .Select(i => DocumentationItemNames.BaseName(i.ItemName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (config.IncludePublicTypes)
        {
            var sourceRoot = Path.GetDirectoryName(projectPath)!;
            generated.AddRange(PublicApiDocumentationScanner.Scan(sourceRoot, _repoRoot, config.PackageId, componentNames));
        }

        var generatedByName = new Dictionary<string, GeneratedDocumentationItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in generated)
        {
            generatedByName.TryAdd(GeneratedKey(item), item);
        }

        var finalItems = new List<JsonObject>();
        var generatedOnly = new List<GeneratedDocumentationItem>();
        var overlayPairs = new List<OverlayItemPair>();

        foreach (var generatedItem in generatedByName.Values)
        {
            var key = GeneratedKey(generatedItem);
            if (existingItems.TryGetValue(key, out var existing))
            {
                overlayPairs.Add(new OverlayItemPair(
                    existing.SourceFile, existing.Json, generatedItem.Json, generatedItem.IsFallbackDescription));
                finalItems.Add(DocumentJsonMerger.Merge(generatedItem.Json, existing.Json, generatedItem.IsFallbackDescription));
            }
            else
            {
                generatedOnly.Add(generatedItem);
                finalItems.Add(generatedItem.Json);
            }
        }

        var orphanOverlays = new List<string>();
        foreach (var existing in existingItems.Values)
        {
            if (!generatedByName.ContainsKey(existing.BaseItemName))
            {
                orphanOverlays.Add(existing.SourceFile);
                finalItems.Add(existing.Json);
            }
        }

        var sortedItems = finalItems
            .OrderBy(i => i.GetString("category") ?? "General", StringComparer.OrdinalIgnoreCase)
            .ThenBy(i => i.GetString("itemName") ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packageNode = BuildPackageNode(project, config);
        AddNamespaces(packageNode, sortedItems);
        var gettingStarted = LoadGettingStarted(config, project);
        var examples = LoadExamples(config);
        var assets = config.IncludeAssets ? StaticAssetScanner.Scan(Path.GetDirectoryName(projectPath)!, _repoRoot) : new JsonArray();

        var output = new JsonObject
        {
            ["package"] = packageNode,
            ["gettingStarted"] = gettingStarted,
            ["items"] = ToArray(sortedItems),
            ["libraryExamples"] = examples
        };

        if (assets.Count > 0)
        {
            output["assets"] = assets;
        }

        output["summary"] = new JsonObject
        {
            ["itemCount"] = sortedItems.Count,
            ["manualItemCount"] = existingItems.Count,
            ["generatedItemCount"] = generatedByName.Count,
            ["generatedOnlyItemCount"] = generatedOnly.Count,
            ["componentCount"] = generatedByName.Values.Count(i => i.Kind == "Component"),
            ["publicApiTypeCount"] = generatedByName.Values.Count(i => i.Kind != "Component")
        };

        var outputDir = string.IsNullOrWhiteSpace(_outputDirectory)
            ? _repoRoot
            : Path.GetFullPath(Path.IsPathRooted(_outputDirectory) ? _outputDirectory : Path.Combine(_repoRoot, _outputDirectory));
        var outputPath = Path.Combine(outputDir, config.OutputFile);
        var aliasPaths = config.Aliases.Select(a => Path.Combine(outputDir, a)).ToList();

        return new PackageDocumentationResult(
            _baseDir,
            config,
            output,
            outputPath,
            aliasPaths,
            sortedItems.Count,
            existingItems.Count,
            generatedOnly.Count,
            generatedByName.Values.Count(i => i.Kind == "Component"),
            generatedByName.Values.Count(i => i.Kind != "Component"),
            generatedOnly)
        {
            OverlayPairs = overlayPairs,
            OrphanOverlays = orphanOverlays
        };
    }

    private static string GeneratedKey(GeneratedDocumentationItem item)
        => item.MergeKey ?? DocumentationItemNames.BaseName(item.ItemName);

    private Dictionary<string, ExistingDocumentationItem> LoadExistingItems(PackageDocumentationPackage config)
    {
        var results = new Dictionary<string, ExistingDocumentationItem>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in config.DocumentationRoots)
        {
            var rootPath = Path.Combine(_baseDir, root);
            if (!Directory.Exists(rootPath))
            {
                continue;
            }

            foreach (var file in Directory.GetFiles(rootPath, "*.json", SearchOption.AllDirectories)
                         .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var node = JsonNode.Parse(File.ReadAllText(file)) as JsonObject;
                if (node is null)
                {
                    continue;
                }

                var itemName = node.GetString("itemName");
                if (string.IsNullOrWhiteSpace(itemName))
                {
                    itemName = Path.GetFileNameWithoutExtension(file);
                    node["itemName"] = itemName;
                }

                if (!node.ContainsKey("category"))
                {
                    node["category"] = DeriveCategoryFromPath(rootPath, file);
                }

                node["packageId"] ??= config.PackageId;
                node["documentationStatus"] ??= "manual";

                var key = DocumentationItemNames.BaseName(itemName);
                results.TryAdd(key, new ExistingDocumentationItem(key, node, file));
            }
        }

        return results;
    }

    private JsonObject LoadGettingStarted(PackageDocumentationPackage config, ProjectMetadata project)
    {
        if (!string.IsNullOrWhiteSpace(config.GettingStartedFile))
        {
            var path = Path.Combine(_baseDir, config.GettingStartedFile);
            if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing)
            {
                var clone = existing.DeepClone().AsObject();
                if (clone["packages"] is not JsonArray packages || packages.Count < _knownPackages.Count)
                {
                    clone["packages"] = BuildKnownPackagesArray(_knownPackages, _repoRoot);
                }

                return clone;
            }
        }

        var title = project.PackageId ?? config.PackageId;
        return new JsonObject
        {
            ["title"] = title,
            ["overview"] = project.Description ?? $"Documentation for {title}.",
            ["targetFramework"] = project.TargetFrameworks.Count > 0 ? string.Join(" / ", project.TargetFrameworks) : null,
            ["installation"] = new JsonObject
            {
                ["nuget"] = new JsonArray { $"dotnet add package {config.PackageId}" }
            },
            ["dependencies"] = ToArray(project.PackageReferences.Select(p => new JsonObject
            {
                ["name"] = p.Name,
                ["version"] = p.Version
            }))
        };
    }

    private JsonArray LoadExamples(PackageDocumentationPackage config)
    {
        if (!string.IsNullOrWhiteSpace(config.ExamplesFile))
        {
            var path = Path.Combine(_baseDir, config.ExamplesFile);
            if (File.Exists(path))
            {
                var node = JsonNode.Parse(File.ReadAllText(path));
                if (node?["examples"] is JsonArray examples)
                {
                    return examples.DeepClone().AsArray();
                }
            }
        }

        return DefaultExamples.ForPackage(config.PackageId);
    }

    private static JsonObject BuildPackageNode(ProjectMetadata project, PackageDocumentationPackage config)
    {
        return new JsonObject
        {
            ["packageId"] = config.PackageId,
            ["title"] = project.Title ?? config.PackageId,
            ["description"] = project.Description ?? $"Documentation for {config.PackageId}.",
            ["authors"] = project.Authors,
            ["targetFrameworks"] = ToArray(project.TargetFrameworks.Select(t => JsonValue.Create(t))),
            ["tags"] = project.PackageTags,
            ["repositoryUrl"] = project.RepositoryUrl,
            ["projectUrl"] = project.PackageProjectUrl,
            ["installation"] = new JsonObject
            {
                ["nuget"] = new JsonArray { $"dotnet add package {config.PackageId}" }
            },
            ["dependencies"] = ToArray(project.PackageReferences.Select(p => new JsonObject
            {
                ["name"] = p.Name,
                ["version"] = p.Version
            })),
            ["projectReferences"] = ToArray(project.ProjectReferences.Select(p => JsonValue.Create(p)))
        };
    }

    private static void AddNamespaces(JsonObject packageNode, IReadOnlyList<JsonObject> items)
    {
        var namespaces = items
            .Select(i => i.GetString("namespace"))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => JsonValue.Create(n));

        packageNode["namespaces"] = ToArray(namespaces);
    }

    private static JsonArray BuildKnownPackagesArray(
        IReadOnlyList<PackageDocumentationPackage> packages,
        string repoRoot)
    {
        return ToArray(packages.Select(package =>
        {
            var project = ProjectMetadataReader.Read(Path.Combine(repoRoot, package.SourceProject));
            return new JsonObject
            {
                ["name"] = package.PackageId,
                ["description"] = project.Description ?? $"Documentation for {package.PackageId}."
            };
        }));
    }

    private static string DeriveCategoryFromPath(string rootPath, string file)
    {
        var relative = Path.GetRelativePath(rootPath, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 ? CategoryNames.ToLabel(parts[0]) : "General";
    }

    internal static JsonArray ToArray(IEnumerable<JsonNode?> nodes)
    {
        var array = new JsonArray();
        foreach (var node in nodes)
        {
            array.Add(node?.DeepClone());
        }

        return array;
    }
}

internal static class ComponentDocumentationScanner
{
    public static List<GeneratedDocumentationItem> Scan(string componentRoot, string repoRoot, string packageId)
    {
        var items = new List<GeneratedDocumentationItem>();
        foreach (var razorFile in Directory.GetFiles(componentRoot, "*.razor", SearchOption.AllDirectories)
                     .Where(f => !Path.GetFileName(f).StartsWith("_", StringComparison.Ordinal))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var item = BuildComponentItem(componentRoot, repoRoot, packageId, razorFile);
            items.Add(item);
        }

        return items;
    }

    public static GeneratedDocumentationItem BuildComponentItem(string componentRoot, string repoRoot, string packageId, string razorFile)
    {
        var name = Path.GetFileNameWithoutExtension(razorFile);
        var relative = Path.GetRelativePath(componentRoot, razorFile);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var category = parts.Length > 1 ? CategoryNames.ToLabel(parts[0]) : "Components";
        var subcategory = parts.Length > 2
            ? string.Join(" / ", parts.Skip(1).Take(parts.Length - 2).Select(CategoryNames.ToLabel))
            : null;

        var sourceFiles = GetComponentSourceFiles(razorFile).ToList();
        var source = string.Join("\n", sourceFiles.Select(File.ReadAllText));
        var parameters = SourceDocParser.ExtractParameters(source);
        var namespaceName = SourceDocParser.ExtractNamespace(source)
            ?? ComponentNamespace(packageId, parts.FirstOrDefault());
        var typeParameters = SourceDocParser.ExtractTypeParameters(source, name);
        var summary = SourceDocParser.ExtractTypeSummary(source, name);
        var fallback = string.IsNullOrWhiteSpace(summary);
        var description = summary ?? $"{name} Blazor component in the {category} category.";

        var root = new JsonObject
        {
            ["itemName"] = name,
            ["kind"] = "Component",
            ["category"] = category,
            ["description"] = description,
            ["packageId"] = packageId,
            ["namespace"] = namespaceName,
            ["sourcePath"] = ToRepoRelative(repoRoot, razorFile),
            ["documentationStatus"] = "generated",
            ["requiredImports"] = new JsonArray { $"@using {namespaceName}" },
            ["parameters"] = PackageDocumentationComposer.ToArray(parameters.Select(p => p.ToJson()))
        };

        if (!string.IsNullOrWhiteSpace(subcategory))
        {
            root["subcategory"] = subcategory;
        }

        if (typeParameters.Count > 0)
        {
            root["typeParameters"] = PackageDocumentationComposer.ToArray(typeParameters.Select(t => new JsonObject
            {
                ["name"] = t,
                ["description"] = $"Generic type parameter {t}."
            }));
        }

        return new GeneratedDocumentationItem(name, "Component", category, ToRepoRelative(repoRoot, razorFile), root, fallback);
    }

    private static IEnumerable<string> GetComponentSourceFiles(string razorFile)
    {
        yield return razorFile;

        var directory = Path.GetDirectoryName(razorFile)!;
        var name = Path.GetFileNameWithoutExtension(razorFile);
        // The glob is "{name}.*.cs", not "{name}*.cs": a prefix match would fold the NEXT component's
        // code-behind into this one's source — TmGantt*.cs also matches TmGanttTaskPanel.razor.cs,
        // TmGanttFilterPanel.razor.cs, … and their [Parameter]s were being attributed to TmGantt.
        foreach (var file in Directory.GetFiles(directory, $"{name}.*.cs", SearchOption.TopDirectoryOnly)
                     .Where(f => !PackageDocumentationGenerator.IsUnderBuildOutput(f))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static string ComponentNamespace(string packageId, string? category)
    {
        if (string.Equals(packageId, "Tempo.Blazor.EmailTemplates", StringComparison.OrdinalIgnoreCase))
        {
            return "Tempo.Blazor.EmailTemplates.Components";
        }

        return string.IsNullOrWhiteSpace(category)
            ? "Tempo.Blazor.Components"
            : $"Tempo.Blazor.Components.{category}";
    }

    private static string ToRepoRelative(string repoRoot, string path)
        => Path.GetRelativePath(repoRoot, path).Replace('\\', '/');
}

internal static class PublicApiDocumentationScanner
{
    private static readonly Regex TypeLineRegex = new(
        @"^\s*public\s+(?:(?:abstract|sealed|static|partial|readonly)\s+)*(?<kind>record\s+struct|record\s+class|record|class|interface|struct|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:<[^>{};()\r\n]+>)?)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    public static List<GeneratedDocumentationItem> Scan(string sourceRoot, string repoRoot, string packageId, ISet<string> componentBaseNames)
    {
        var candidates = new List<PublicApiCandidate>();
        foreach (var file in Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(f => !PackageDocumentationGenerator.IsUnderBuildOutput(f))
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var source = File.ReadAllText(file);
            var namespaceName = SourceDocParser.ExtractNamespace(source) ?? NamespaceFromPath(packageId);
            var types = FindPublicTypes(source);
            var singleTopLevelTypeFile = types.Count(t => !t.IsNested) == 1;

            foreach (var type in types)
            {
                var itemName = type.Name;
                var baseName = DocumentationItemNames.BaseName(type.Name);
                if (componentBaseNames.Contains(baseName))
                {
                    continue;
                }

                var kind = KindLabel(type.Kind);
                var category = CategoryFromPath(sourceRoot, file);
                var summary = type.Summary;
                var fallback = string.IsNullOrWhiteSpace(summary);
                var description = summary ?? $"Public {kind.ToLowerInvariant()} defined by {packageId}.";
                var root = new JsonObject
                {
                    ["kind"] = kind,
                    ["category"] = category,
                    ["namespace"] = namespaceName,
                    ["description"] = description,
                    ["packageId"] = packageId,
                    ["sourcePath"] = Path.GetRelativePath(repoRoot, file).Replace('\\', '/'),
                    ["documentationStatus"] = "generated"
                };

                var typeParameters = SourceDocParser.ExtractTypeParametersFromName(type.Name);
                if (typeParameters.Count > 0)
                {
                    root["typeParameters"] = PackageDocumentationComposer.ToArray(typeParameters.Select(t => new JsonObject
                    {
                        ["name"] = t,
                        ["description"] = $"Generic type parameter {t}."
                    }));
                }

                if (singleTopLevelTypeFile && !type.IsNested)
                {
                    var members = SourceDocParser.ExtractMembers(source, baseName);
                    if (members.Count > 0)
                    {
                        root["members"] = PackageDocumentationComposer.ToArray(members.Select(m => m.ToJson()));
                    }
                }

                candidates.Add(new PublicApiCandidate(type.Name, namespaceName, kind, category, root.GetString("sourcePath")!, root, fallback));
            }
        }

        var duplicateNames = candidates
            .GroupBy(c => DocumentationItemNames.BaseName(c.TypeName), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new Dictionary<string, GeneratedDocumentationItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            var itemName = duplicateNames.Contains(DocumentationItemNames.BaseName(candidate.TypeName))
                ? $"{candidate.Namespace}.{candidate.TypeName}"
                : candidate.TypeName;
            var key = DocumentationItemNames.BaseName(itemName);
            var root = candidate.Json.DeepClone().AsObject();
            root["itemName"] = itemName;
            results.TryAdd(key, new GeneratedDocumentationItem(itemName, candidate.Kind, candidate.Category, candidate.SourcePath, root, candidate.IsFallbackDescription, key));
        }

        return results.Values.ToList();
    }

    private static List<PublicTypeDocumentation> FindPublicTypes(string source)
    {
        var results = new List<PublicTypeDocumentation>();
        var lines = SourceDocParser.SplitLines(source);
        var scopes = new List<PublicTypeScope>();
        var braceDepth = 0;
        string? pendingScopeToOpen = null;
        for (var i = 0; i < lines.Length; i++)
        {
            while (scopes.Count > 0 && braceDepth < scopes[^1].BodyDepth)
            {
                scopes.RemoveAt(scopes.Count - 1);
            }

            var line = lines[i];
            var match = TypeLineRegex.Match(line);
            if (match.Success)
            {
                var simpleName = match.Groups["name"].Value.Trim();
                var parentPrefix = scopes.Count > 0 ? scopes[^1].Name + "." : "";
                var typeName = parentPrefix + simpleName;
                results.Add(new PublicTypeDocumentation(
                    typeName,
                    match.Groups["kind"].Value.Trim(),
                    SourceDocParser.CleanXmlDoc(SourceDocParser.ReadXmlDocBlockBefore(lines, i)),
                    scopes.Count > 0));

                if (!line.Contains(';'))
                {
                    pendingScopeToOpen = typeName;
                }
            }

            var depthBeforeLine = braceDepth;
            braceDepth = Math.Max(0, braceDepth + Count(line, '{') - Count(line, '}'));
            if (pendingScopeToOpen is not null && braceDepth > depthBeforeLine)
            {
                scopes.Add(new PublicTypeScope(pendingScopeToOpen, braceDepth));
                pendingScopeToOpen = null;
            }
        }

        return results;
    }

    private static int Count(string value, char character)
        => value.Count(c => c == character);

    private static string CategoryFromPath(string sourceRoot, string file)
    {
        var relative = Path.GetRelativePath(sourceRoot, file);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Length > 1 ? CategoryNames.ToLabel(parts[0]) : "General";
    }

    private static string NamespaceFromPath(string packageId) => packageId;

    private static string KindLabel(string rawKind)
    {
        rawKind = rawKind.Trim();
        return rawKind switch
        {
            "class" => "Class",
            "interface" => "Interface",
            "struct" => "Struct",
            "enum" => "Enum",
            "record" or "record class" => "Record",
            "record struct" => "RecordStruct",
            _ => rawKind
        };
    }
}

internal sealed record PublicApiCandidate(
    string TypeName,
    string Namespace,
    string Kind,
    string Category,
    string SourcePath,
    JsonObject Json,
    bool IsFallbackDescription);

internal sealed record PublicTypeDocumentation(string Name, string Kind, string? Summary, bool IsNested);

internal sealed record PublicTypeScope(string Name, int BodyDepth);

internal static class SourceDocParser
{
    private static readonly Regex NamespaceRegex = new(@"(?m)^\s*namespace\s+([A-Za-z0-9_.]+)", RegexOptions.Compiled);

    private static readonly Regex RazorTypeParamRegex = new(@"(?m)^@typeparam\s+([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private static readonly Regex ParameterPropertyRegex = new(
        @"public\s+(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*(?<default>.*?);)?",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex PropertyLineRegex = new(
        @"public\s+(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get;",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex FieldLineRegex = new(
        @"public\s+(?:const\s+|static\s+readonly\s+|static\s+)?(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=.*)?;",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex MethodLineRegex = new(
        @"public\s+(?:static\s+|async\s+|virtual\s+|override\s+|sealed\s+|abstract\s+)*(?<return>[A-Za-z_][A-Za-z0-9_<>.,?\s\[\]]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\((?<params>[^)]*)\)",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    public static string? ExtractNamespace(string source)
        => NamespaceRegex.Match(source) is { Success: true } match ? match.Groups[1].Value.Trim() : null;

    public static List<ParameterDocumentation> ExtractParameters(string source)
    {
        var results = new List<ParameterDocumentation>();
        var lines = SplitLines(source);

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (!trimmed.StartsWith("[", StringComparison.Ordinal) || !trimmed.Contains("Parameter", StringComparison.Ordinal))
            {
                continue;
            }

            var attrs = trimmed;
            var j = i + 1;
            // Continuation lines are pure attribute lists — a '['-led line that also carries the
            // property declaration (e.g. "[Parameter] public string X { get; set; }") is the NEXT
            // parameter, not an attribute of this one. Absorbing it swallowed every parameter of a
            // consecutive [Parameter] run except the last.
            while (j < lines.Length)
            {
                var next = lines[j].Trim();
                if (!next.StartsWith("[", StringComparison.Ordinal) || !next.EndsWith("]", StringComparison.Ordinal))
                {
                    break;
                }

                attrs += " " + next;
                j++;
            }

            if (!attrs.Contains("Parameter", StringComparison.Ordinal) || attrs.Contains("CascadingParameter", StringComparison.Ordinal))
            {
                continue;
            }

            // If the property declaration is on the same line as the last attribute, scan from there
            // so the declaration is not skipped (e.g. `[Parameter] public string Name { get; set; }`).
            var lastAttrLine = lines[j - 1].Trim();
            if (lastAttrLine.Contains("{ get;", StringComparison.Ordinal)
                && lastAttrLine.Contains("set;", StringComparison.Ordinal))
            {
                j = j - 1;
            }

            var declaration = "";
            for (var k = j; k < Math.Min(lines.Length, j + 8); k++)
            {
                declaration += " " + lines[k].Trim();
                if (declaration.Contains("{ get;", StringComparison.Ordinal)
                    && declaration.Contains("set;", StringComparison.Ordinal))
                {
                    break;
                }
            }

            var match = ParameterPropertyRegex.Match(declaration);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (string.Equals(name, "AdditionalAttributes", StringComparison.Ordinal)
                || attrs.Contains("CaptureUnmatchedValues", StringComparison.Ordinal))
            {
                continue;
            }

            var type = NormalizeType(match.Groups["type"].Value);
            var defaultValue = CleanDefault(match.Groups["default"].Success ? match.Groups["default"].Value : null);
            var description = CleanXmlDoc(ReadXmlDocBlockBefore(lines, i));
            var editorRequired = attrs.Contains("EditorRequired", StringComparison.Ordinal);
            var isRequired = editorRequired || IsRequiredByHeuristic(type, defaultValue);

            results.Add(new ParameterDocumentation(name, type, isRequired, defaultValue, description, editorRequired));
            i = j;
        }

        return results
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string? ExtractTypeSummary(string source, string typeName)
    {
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!line.Contains(typeName, StringComparison.Ordinal))
            {
                continue;
            }

            if (Regex.IsMatch(line, @"\b(class|record|struct|interface|enum)\s+" + Regex.Escape(typeName) + @"(\b|<)"))
            {
                return CleanXmlDoc(ReadXmlDocBlockBefore(lines, i));
            }
        }

        return null;
    }

    public static List<string> ExtractTypeParameters(string source, string typeName)
    {
        var result = RazorTypeParamRegex.Matches(source).Select(m => m.Groups[1].Value.Trim()).ToList();
        result.AddRange(ExtractTypeParametersFromName(ExtractClassNameWithGenerics(source, typeName) ?? typeName));
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static List<string> ExtractTypeParametersFromName(string name)
    {
        var start = name.IndexOf('<');
        var end = name.LastIndexOf('>');
        if (start < 0 || end <= start)
        {
            return [];
        }

        return name[(start + 1)..end]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last())
            .Where(p => p.Length > 0)
            .ToList();
    }

    public static List<MemberDocumentation> ExtractMembers(string source, string typeName)
    {
        var members = new List<MemberDocumentation>();
        var lines = SplitLines(source);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("public ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = PropertyLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name == typeName || name == "AdditionalAttributes")
            {
                continue;
            }

            members.Add(new MemberDocumentation(
                name,
                "Property",
                NormalizeType(match.Groups["type"].Value),
                CleanXmlDoc(ReadXmlDocBlockBefore(lines, i)),
                null));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            var declarationPart = line.Split('=', 2)[0];
            if (!line.StartsWith("public ", StringComparison.Ordinal)
                || !line.Contains(';')
                || declarationPart.Contains('(')
                || declarationPart.Contains('{')
                || line.StartsWith("public event ", StringComparison.Ordinal))
            {
                continue;
            }

            var match = FieldLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name == typeName)
            {
                continue;
            }

            members.Add(new MemberDocumentation(
                name,
                "Field",
                NormalizeType(match.Groups["type"].Value),
                CleanXmlDoc(ReadXmlDocBlockBefore(lines, i)),
                null));
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith("public ", StringComparison.Ordinal) || !line.Contains('('))
            {
                continue;
            }

            var match = MethodLineRegex.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var name = match.Groups["name"].Value.Trim();
            if (name == typeName || name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("set_", StringComparison.Ordinal))
            {
                continue;
            }

            members.Add(new MemberDocumentation(
                name,
                "Method",
                NormalizeType(match.Groups["return"].Value),
                CleanXmlDoc(ReadXmlDocBlockBefore(lines, i)),
                ParseMethodParameters(match.Groups["params"].Value)));
        }

        return members
            .GroupBy(m => $"{m.Kind}:{m.Name}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(m => m.Kind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Take(80)
            .ToList();
    }

    public static string[] SplitLines(string source)
        => source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');

    public static string ReadXmlDocBlockBefore(string[] lines, int lineIndex)
    {
        var docs = new Stack<string>();
        for (var i = lineIndex - 1; i >= 0; i--)
        {
            var trimmed = lines[i].Trim();

            // Attributes sit between the doc block and the declaration ([Flags], [JsonPolymorphic],
            // [Obsolete(...)], …). Walk past them instead of treating them as the end of the block —
            // otherwise every attributed type silently falls back to a generated description.
            if (docs.Count == 0 && trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                continue;
            }

            if (!trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                break;
            }

            docs.Push(lines[i]);
        }

        return string.Join('\n', docs);
    }

    public static string? CleanXmlDoc(string docs)
    {
        if (string.IsNullOrWhiteSpace(docs))
        {
            return null;
        }

        var lines = docs.Split('\n')
            .Select(l => Regex.Replace(l.Trim(), @"^///\s?", ""))
            .ToList();
        var text = string.Join("\n", lines).Trim();
        var summary = Regex.Match(text, @"(?s)<summary>(.*?)</summary>");
        if (summary.Success)
        {
            text = summary.Groups[1].Value;
        }

        // Self-closing doc references carry their meaning in an attribute, so stripping the tag would
        // leave a hole in the sentence ("Severity of an ."). Replace them with the text a human would
        // have written instead.
        text = Regex.Replace(text, @"<see\s+cref=""(?<target>[^""]+)""\s*/>", m => SimpleCrefName(m.Groups["target"].Value));
        text = Regex.Replace(text, @"<(?:see|seealso)\s+langword=""(?<word>[^""]+)""\s*/>", m => m.Groups["word"].Value);
        text = Regex.Replace(text, @"<(?:paramref|typeparamref)\s+name=""(?<name>[^""]+)""\s*/>", m => m.Groups["name"].Value);
        text = Regex.Replace(text, @"<[^>]+>", "");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>
    /// Turns a cref target into the bare name a reader expects in prose: drops the documentation-id
    /// prefix, a parameter list and generic arity, then keeps the last dotted segment
    /// (<c>Tempo.X.ReportDefinition</c> reads as <c>ReportDefinition</c>). Member ids keep their
    /// declaring type, because <c>Read</c> alone would lose the meaning of <c>M:Permission.Read</c>.
    /// </summary>
    private static string SimpleCrefName(string target)
    {
        var trimmed = target.Trim();
        var isMemberId = Regex.IsMatch(trimmed, @"^[MPFE]:");
        var value = Regex.Replace(trimmed, @"^[A-Za-z]:", "");
        value = Regex.Replace(value, @"\([^)]*\)", "");
        value = Regex.Replace(value, @"`+\d+", "");
        var segments = value.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return trimmed;
        }

        return isMemberId && segments.Length > 1
            ? segments[^2] + "." + segments[^1]
            : segments[^1];
    }

    private static string? ExtractClassNameWithGenerics(string source, string typeName)
    {
        var match = Regex.Match(source, @"\b" + Regex.Escape(typeName) + @"(?<generic><[^>{};()\r\n]+>)");
        return match.Success ? typeName + match.Groups["generic"].Value : null;
    }

    private static string NormalizeType(string value)
        => Regex.Replace(value.Trim(), @"\s+", " ");

    private static string? CleanDefault(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = Regex.Replace(value.Trim(), @"\s+", " ");
        value = value.TrimEnd(';').Trim();
        if (value is "default!" or "null!" or "default")
        {
            return null;
        }

        if (value == "string.Empty")
        {
            return "\"\"";
        }

        if (value.StartsWith("new ", StringComparison.Ordinal) || value == "new()" || value.StartsWith("new(", StringComparison.Ordinal))
        {
            return null;
        }

        if (value.Length > 160)
        {
            return value[..157] + "...";
        }

        return value;
    }

    private static bool IsRequiredByHeuristic(string type, string? defaultValue)
    {
        if (defaultValue is not null || type.EndsWith("?", StringComparison.Ordinal))
        {
            return false;
        }

        var nonRequiredPrefixes = new[]
        {
            "EventCallback",
            "RenderFragment",
            "bool",
            "int",
            "double",
            "float",
            "decimal",
            "long",
            "Guid",
            "DateTime",
            "DateOnly",
            "TimeOnly"
        };

        return !nonRequiredPrefixes.Any(p => type.StartsWith(p, StringComparison.Ordinal));
    }

    private static JsonArray? ParseMethodParameters(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var array = new JsonArray();
        foreach (var raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cleaned = raw.Replace("CancellationToken ", "CancellationToken ", StringComparison.Ordinal).Trim();
            var equals = cleaned.IndexOf('=');
            var defaultValue = equals >= 0 ? cleaned[(equals + 1)..].Trim() : null;
            if (equals >= 0)
            {
                cleaned = cleaned[..equals].Trim();
            }

            var pieces = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length < 2)
            {
                continue;
            }

            var name = pieces[^1];
            var type = string.Join(" ", pieces[..^1]);
            var obj = new JsonObject
            {
                ["name"] = name,
                ["type"] = type
            };
            if (defaultValue is not null)
            {
                obj["default"] = defaultValue;
            }

            array.Add(obj);
        }

        return array.Count == 0 ? null : array;
    }
}

internal static class DocumentJsonMerger
{
    public static JsonObject Merge(JsonObject generated, JsonObject manual, bool generatedDescriptionIsFallback)
    {
        var merged = generated.DeepClone().AsObject();
        foreach (var kvp in manual)
        {
            switch (kvp.Key)
            {
                case "parameters" when kvp.Value is JsonArray manualParams && merged["parameters"] is JsonArray generatedParams:
                    merged["parameters"] = MergeParameters(generatedParams, manualParams);
                    break;
                case "requiredImports" when kvp.Value is JsonArray manualImports && merged["requiredImports"] is JsonArray generatedImports:
                    merged["requiredImports"] = MergeStringArrays(generatedImports, manualImports);
                    break;
                case "description":
                    if (generatedDescriptionIsFallback || !string.IsNullOrWhiteSpace(kvp.Value?.GetValue<string>()))
                    {
                        merged[kvp.Key] = kvp.Value?.DeepClone();
                    }
                    break;
                case "documentationStatus":
                    merged["documentationStatus"] = "merged";
                    break;
                case "itemName":
                case "kind":
                case "packageId":
                case "sourcePath":
                    break;
                default:
                    if (!merged.ContainsKey(kvp.Key) || kvp.Key is "examples" or "cssClasses" or "methods" or "usedInComponents")
                    {
                        merged[kvp.Key] = kvp.Value?.DeepClone();
                    }
                    break;
            }
        }

        merged["documentationStatus"] = "merged";
        return Reorder(merged);
    }

    private static JsonArray MergeParameters(JsonArray generated, JsonArray manual)
    {
        var manualByName = manual.OfType<JsonObject>()
            .Where(p => !string.IsNullOrWhiteSpace(p.GetString("name")))
            .GroupBy(p => p.GetString("name")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var generatedParam in generated.OfType<JsonObject>())
        {
            var name = generatedParam.GetString("name") ?? "";
            var merged = generatedParam.DeepClone().AsObject();
            if (manualByName.TryGetValue(name, out var manualParam))
            {
                foreach (var kvp in manualParam)
                {
                    if (!merged.ContainsKey(kvp.Key)
                        || kvp.Key is "description" or "examples" or "remarks")
                    {
                        merged[kvp.Key] = kvp.Value?.DeepClone();
                    }
                }
            }

            seen.Add(name);
            result.Add(merged);
        }

        foreach (var manualParam in manual.OfType<JsonObject>())
        {
            var name = manualParam.GetString("name") ?? "";
            if (!seen.Contains(name))
            {
                result.Add(manualParam.DeepClone());
            }
        }

        return result;
    }

    private static JsonArray MergeStringArrays(JsonArray first, JsonArray second)
    {
        var values = first.Concat(second)
            .Select(v => v?.GetValue<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase);
        var array = new JsonArray();
        foreach (var value in values)
        {
            array.Add(value);
        }

        return array;
    }

    private static JsonObject Reorder(JsonObject original)
    {
        var ordered = new JsonObject();
        string[] firstKeys =
        [
            "itemName",
            "kind",
            "category",
            "subcategory",
            "namespace",
            "description",
            "packageId",
            "sourcePath",
            "documentationStatus",
            "requiredImports",
            "typeParameters",
            "parameters",
            "members"
        ];

        foreach (var key in firstKeys)
        {
            if (original.ContainsKey(key))
            {
                ordered[key] = original[key]?.DeepClone();
            }
        }

        foreach (var kvp in original)
        {
            if (!ordered.ContainsKey(kvp.Key))
            {
                ordered[kvp.Key] = kvp.Value?.DeepClone();
            }
        }

        return ordered;
    }
}

internal static class ProjectMetadataReader
{
    public static ProjectMetadata Read(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            return new ProjectMetadata(projectPath);
        }

        var doc = XDocument.Load(projectPath);
        string? Property(string name) => doc.Descendants(name).FirstOrDefault()?.Value.Trim();

        var targetFrameworks = (Property("TargetFrameworks") ?? Property("TargetFramework") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var packages = doc.Descendants("PackageReference")
            .Select(e => new PackageReferenceMetadata(
                e.Attribute("Include")?.Value ?? e.Attribute("Update")?.Value ?? "",
                e.Attribute("Version")?.Value ?? e.Element("Version")?.Value ?? ""))
            .Where(p => !string.IsNullOrWhiteSpace(p.Name))
            .ToList();

        var projectReferences = doc.Descendants("ProjectReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Replace('\\', '/'))
            .ToList();

        return new ProjectMetadata(projectPath)
        {
            PackageId = Property("PackageId"),
            Title = Property("Title") ?? Property("PackageId"),
            Description = Property("Description"),
            Authors = Property("Authors"),
            PackageTags = Property("PackageTags"),
            RepositoryUrl = Property("RepositoryUrl"),
            PackageProjectUrl = Property("PackageProjectUrl"),
            TargetFrameworks = targetFrameworks,
            PackageReferences = packages,
            ProjectReferences = projectReferences
        };
    }
}

internal static class StaticAssetScanner
{
    public static JsonArray Scan(string projectRoot, string repoRoot)
    {
        var wwwroot = Path.Combine(projectRoot, "wwwroot");
        if (!Directory.Exists(wwwroot))
        {
            return [];
        }

        var files = Directory.GetFiles(wwwroot, "*", SearchOption.AllDirectories)
            .Where(f => !PackageDocumentationGenerator.IsUnderBuildOutput(f))
            .Select(f => Path.GetRelativePath(repoRoot, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(f => new JsonObject
            {
                ["path"] = f,
                ["kind"] = AssetKind(f)
            });

        return PackageDocumentationComposer.ToArray(files);
    }

    private static string AssetKind(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".css" => "Css",
            ".js" or ".mjs" => "JavaScript",
            ".json" => "Json",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".webp" => "Image",
            ".woff" or ".woff2" or ".ttf" => "Font",
            _ => "Asset"
        };
    }
}

internal static class DefaultExamples
{
    public static JsonArray ForPackage(string packageId)
    {
        return packageId switch
        {
            "Tempo.Blazor.FluentValidation" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Register FluentValidation",
                    ["category"] = "Setup",
                    ["code"] = "builder.Services.AddTempoFluentValidation(typeof(MyValidator).Assembly);"
                },
                new JsonObject
                {
                    ["title"] = "Use FluentValidationValidator",
                    ["category"] = "Forms",
                    ["code"] = "<EditForm Model=\"model\" OnValidSubmit=\"SaveAsync\">\\n    <FluentValidationValidator />\\n    <TmValidationSummary />\\n</EditForm>"
                }
            ]),
            "Tempo.Blazor.Collaboration" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Register SignalR document collaboration",
                    ["category"] = "Setup",
                    ["code"] = "services.AddSingleton<IDocumentCollaborationRealtimeProvider>(_ => new SignalRDocumentCollaborationProvider(hubUrl));"
                }
            ]),
            "Tempo.Blazor.DocumentFormats" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Import and export DOCX",
                    ["category"] = "Document Formats",
                    ["code"] = "var importer = new DocumentDocxImporter();\\nvar imported = await importer.ImportAsync(stream);\\n\\nvar exporter = new DocumentDocxExporter();\\nvar exported = await exporter.ExportAsync(imported.Document);"
                }
            ]),
            "Tempo.Blazor.EmailTemplates.Abstractions" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Render an email template",
                    ["category"] = "Rendering",
                    ["code"] = "services.AddTempoEmailTemplateEngine();\\nvar result = await renderer.RenderAsync(document, data, cancellationToken);"
                }
            ]),
            "Tempo.Blazor.EmailTemplates" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Use the email template editor",
                    ["category"] = "Components",
                    ["code"] = "<TmEmailTemplateEditor @bind-Document=\"document\" OnSave=\"SaveAsync\" />"
                }
            ]),
            "Tempo.Blazor.Mcp" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Register wireframe MCP tools",
                    ["category"] = "MCP",
                    ["code"] = "builder.Services.AddTempoWireframeMcpTools();\\nbuilder.Services.AddMcpServer()\\n    .WithHttpTransport()\\n    .WithToolsFromAssembly(typeof(TempoWireframeMcp).Assembly);"
                }
            ]),
            "Tempo.Reporting.Abstractions" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Create a report server client",
                    ["category"] = "Report Server",
                    ["code"] = "services.AddHttpClient<ITempoReportServerClient, TempoReportServerClient>(client =>\\n{\\n    client.BaseAddress = new Uri(reportServerBaseUrl);\\n});"
                }
            ]),
            "Tempo.Reporting.Engine" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Generate a report snapshot",
                    ["category"] = "Rendering",
                    ["code"] = "var instance = ReportBandInstantiator.Instantiate(definition, dataSet, context);\\nvar snapshot = ReportSnapshotGenerator.Generate(instance, textMeasurer);"
                }
            ]),
            "Tempo.Blazor.Reporting" => PackageDocumentationComposer.ToArray([
                new JsonObject
                {
                    ["title"] = "Use the report viewer",
                    ["category"] = "Components",
                    ["code"] = "<TmReportViewer ReportSource=\"@source\" TenantId=\"tenant-a\" UserId=\"user-1\" />"
                }
            ]),
            _ => []
        };
    }
}

internal static class CategoryNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AITools"] = "AI Tools",
        ["Activity"] = "Activity",
        ["Avatars"] = "Avatars",
        ["Buttons"] = "Buttons",
        ["Charts"] = "Charts",
        ["Chat"] = "Chat",
        ["Dashboard"] = "Dashboard",
        ["DataDisplay"] = "Data Display",
        ["DataTable"] = "Data Table",
        ["Data"] = "Data",
        ["Diagram"] = "Diagram",
        ["Definitions"] = "Definitions",
        ["DocumentEditor"] = "Document Editor",
        ["DocumentFormats"] = "Document Formats",
        ["DocumentLibrary"] = "Document Library",
        ["Dropdowns"] = "Dropdowns",
        ["Dtos"] = "DTOs",
        ["Export"] = "Export",
        ["Expressions"] = "Expressions",
        ["Feedback"] = "Feedback",
        ["Files"] = "Files",
        ["Filters"] = "Filters",
        ["Fonts"] = "Fonts",
        ["Forms"] = "Forms",
        ["Gallery"] = "Gallery",
        ["Icons"] = "Icons",
        ["ImportExport"] = "Import / Export",
        ["Inputs"] = "Inputs",
        ["Interop"] = "Interop",
        ["Layout"] = "Layout",
        ["Localization"] = "Localization",
        ["Modeling"] = "Modeling",
        ["Models"] = "Models",
        ["Navigation"] = "Navigation",
        ["Notifications"] = "Notifications",
        ["NotionEditor"] = "Notion Editor",
        ["Pickers"] = "Pickers",
        ["PivotTable"] = "Pivot Table",
        ["Pdf"] = "PDF",
        ["Rendering"] = "Rendering",
        ["Scheduler"] = "Scheduler",
        ["Serialization"] = "Serialization",
        ["Services"] = "Services",
        ["Signing"] = "Signing",
        ["Snapshot"] = "Snapshot",
        ["Spreadsheet"] = "Spreadsheet",
        ["Tags"] = "Tags",
        ["Templating"] = "Templating",
        ["Timeline"] = "Timeline",
        ["Toolbar"] = "Toolbar",
        ["TreeView"] = "Tree View",
        ["Validation"] = "Validation",
        ["Wireframe"] = "Wireframe",
        ["Workflow"] = "Workflow"
    };

    public static string ToLabel(string value)
        => Map.TryGetValue(value, out var label) ? label : SplitPascalCase(value);

    private static string SplitPascalCase(string value)
        => Regex.Replace(value, "([a-z])([A-Z])", "$1 $2");
}

internal static class DocumentationItemNames
{
    public static string BaseName(string itemName)
    {
        var index = itemName.IndexOf('<');
        return index >= 0 ? itemName[..index] : itemName;
    }
}

internal sealed record CliOptions(
    string? BaseDirectory,
    GeneratorCommand Command,
    string? PackageId,
    string? OutputDirectory,
    bool FailOnDrift)
{
    public static CliOptions Parse(string[] args)
    {
        string? baseDirectory = null;
        var remaining = new List<string>(args);
        if (remaining.Count > 0 && Directory.Exists(remaining[0]))
        {
            baseDirectory = remaining[0];
            remaining.RemoveAt(0);
        }

        var command = GeneratorCommand.Generate;
        if (remaining.Count > 0)
        {
            command = remaining[0] switch
            {
                "generate" => GeneratorCommand.Generate,
                "validate" => GeneratorCommand.Validate,
                "list-missing" => GeneratorCommand.ListMissing,
                "enrich" or "--enrich" => GeneratorCommand.Enrich,
                "repair" => GeneratorCommand.Repair,
                _ => command
            };

            if (remaining[0] is "generate" or "validate" or "list-missing" or "enrich" or "--enrich" or "repair")
            {
                remaining.RemoveAt(0);
            }
        }

        string? packageId = null;
        string? outputDirectory = null;
        var failOnDrift = false;

        for (var i = 0; i < remaining.Count; i++)
        {
            var arg = remaining[i];
            if (arg == "--package" && i + 1 < remaining.Count)
            {
                packageId = remaining[++i];
            }
            else if (arg.StartsWith("--package=", StringComparison.Ordinal))
            {
                packageId = arg["--package=".Length..];
            }
            else if (arg == "--output-dir" && i + 1 < remaining.Count)
            {
                outputDirectory = remaining[++i];
            }
            else if (arg.StartsWith("--output-dir=", StringComparison.Ordinal))
            {
                outputDirectory = arg["--output-dir=".Length..];
            }
            else if (arg == "--fail-on-drift")
            {
                failOnDrift = true;
            }
        }

        return new CliOptions(baseDirectory, command, packageId, outputDirectory, failOnDrift);
    }
}

internal enum GeneratorCommand
{
    Generate,
    Validate,
    ListMissing,
    Enrich,
    Repair
}

internal sealed record PackageDocumentationConfig
{
    public string? AggregateOutputFile { get; init; }
    public List<PackageDocumentationPackage> Packages { get; init; } = [];
}

internal sealed record PackageDocumentationPackage
{
    public string PackageId { get; init; } = "";
    public string SourceProject { get; init; } = "";
    public string OutputFile { get; init; } = "";
    public List<string> Aliases { get; init; } = [];
    public List<string> DocumentationRoots { get; init; } = [];
    public List<string> ComponentRoots { get; init; } = [];
    public List<string> IncludePatterns { get; init; } = [];
    public List<string> ExcludePatterns { get; init; } = [];
    public string? GettingStartedFile { get; init; }
    public string? ExamplesFile { get; init; }
    public bool IncludePublicTypes { get; init; }
    public bool IncludeAssets { get; init; }
}

internal sealed record PackageDocumentationResult(
    string BaseDir,
    PackageDocumentationPackage Config,
    JsonObject Document,
    string OutputPath,
    IReadOnlyList<string> AliasOutputPaths,
    int ItemCount,
    int ManualItemCount,
    int GeneratedOnlyCount,
    int DiscoveredComponentCount,
    int DiscoveredTypeCount,
    IReadOnlyList<GeneratedDocumentationItem> GeneratedOnlyItems)
{
    /// <summary>Overlay files matched with their freshly parsed counterpart. Used by <c>repair</c>.</summary>
    public IReadOnlyList<OverlayItemPair> OverlayPairs { get; init; } = [];

    /// <summary>Overlay files whose documented type no longer exists in source.</summary>
    public IReadOnlyList<string> OrphanOverlays { get; init; } = [];
}

internal sealed record ExistingDocumentationItem(string BaseItemName, JsonObject Json, string SourceFile);

/// <summary>An overlay file paired with the item freshly parsed from source, for <c>repair</c>.</summary>
internal sealed record OverlayItemPair(string OverlayFile, JsonObject Overlay, JsonObject Generated, bool GeneratedIsFallback);

internal sealed record GeneratedDocumentationItem(
    string ItemName,
    string Kind,
    string Category,
    string SourcePath,
    JsonObject Json,
    bool IsFallbackDescription,
    string? MergeKey = null);

internal sealed record ProjectMetadata(string ProjectPath)
{
    public string? PackageId { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
    public string? Authors { get; init; }
    public string? PackageTags { get; init; }
    public string? RepositoryUrl { get; init; }
    public string? PackageProjectUrl { get; init; }
    public List<string> TargetFrameworks { get; init; } = [];
    public List<PackageReferenceMetadata> PackageReferences { get; init; } = [];
    public List<string> ProjectReferences { get; init; } = [];
}

internal sealed record PackageReferenceMetadata(string Name, string Version);

internal sealed record ParameterDocumentation(
    string Name,
    string Type,
    bool IsRequired,
    string? Default,
    string? Description,
    bool EditorRequired)
{
    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["name"] = Name,
            ["type"] = Type,
            ["isRequired"] = IsRequired
        };
        if (Default is not null)
        {
            obj["default"] = Default;
        }
        if (!string.IsNullOrWhiteSpace(Description))
        {
            obj["description"] = Description;
        }
        if (EditorRequired)
        {
            obj["editorRequired"] = true;
        }

        return obj;
    }
}

internal sealed record MemberDocumentation(
    string Name,
    string Kind,
    string Type,
    string? Description,
    JsonArray? Parameters)
{
    public JsonObject ToJson()
    {
        var obj = new JsonObject
        {
            ["name"] = Name,
            ["kind"] = Kind,
            ["type"] = Type
        };
        if (!string.IsNullOrWhiteSpace(Description))
        {
            obj["description"] = Description;
        }
        if (Parameters is not null)
        {
            obj["parameters"] = Parameters.DeepClone();
        }

        return obj;
    }
}

internal static class JsonObjectExtensions
{
    public static string? GetString(this JsonObject obj, string key)
        => obj.TryGetPropertyValue(key, out var value) && value is not null
            ? value.GetValue<string?>()
            : null;
}
