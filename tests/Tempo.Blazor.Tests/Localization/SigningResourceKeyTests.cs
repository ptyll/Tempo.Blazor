using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Tempo.Blazor.Tests.Localization;

/// <summary>
/// Every localized key the Signing package asks for (<c>Loc["…"]</c>) must exist in the REAL
/// <c>TmResources.json</c>. The bUnit suites run against <see cref="MockTmLocalizer"/>, whose maps
/// can drift from the shipped tables — the phase-4 review caught exactly that drift
/// (<c>TmSigningStep_KbaDescription</c> was in the mock but missing from all four resources). This
/// guard parses the Signing sources directly, so a new key used in a component without a resource
/// entry fails here, in English — the <see cref="TmResourceCompletenessTests"/> parity guard keeps
/// cs / sk / fr in step with it.
/// </summary>
public class SigningResourceKeyTests
{
    private static readonly Regex LocKeyPattern = new(
        """Loc\["(?<key>[A-Za-z_][A-Za-z_0-9]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Lazy<HashSet<string>> ResourceKeysLazy = new(LoadResourceKeys);

    private static readonly Lazy<IReadOnlyList<(string File, string Key)>> UsedKeysLazy = new(LoadUsedKeys);

    private static HashSet<string> LoadResourceKeys()
    {
        var resources = Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor", "Resources", "TmResources.json");
        using var document = JsonDocument.Parse(File.ReadAllText(resources));
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            keys.Add(property.Name);
        }

        return keys;
    }

    private static IReadOnlyList<(string File, string Key)> LoadUsedKeys()
    {
        var signingDirectory = Path.Combine(FindRepositoryRoot(), "src", "Tempo.Blazor.Signing");
        var keys = new List<(string File, string Key)>();
        foreach (var path in Directory.EnumerateFiles(signingDirectory, "*.razor", SearchOption.AllDirectories)
                     .Concat(Directory.EnumerateFiles(signingDirectory, "*.cs", SearchOption.AllDirectories)))
        {
            var text = File.ReadAllText(path);
            foreach (Match match in LocKeyPattern.Matches(text))
            {
                keys.Add((Path.GetFileName(path), match.Groups["key"].Value));
            }
        }

        return keys;
    }

    [Fact]
    public void Every_signing_localized_key_exists_in_the_real_resources()
    {
        UsedKeysLazy.Value.Should().NotBeEmpty("the Signing package must keep using the localizer");

        var missing = UsedKeysLazy.Value
            .Where(entry => !ResourceKeysLazy.Value.Contains(entry.Key))
            .Distinct()
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "every Loc[\"…\"] key used by the Signing package must exist in TmResources.json:\n{0}",
            string.Join("\n", missing.Select(entry => $"{entry.Key} ({entry.File})")));
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
