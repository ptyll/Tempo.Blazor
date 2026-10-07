using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Tests.Theme;

namespace Tempo.Blazor.Tests.Documentation;

/// <summary>
/// Ratchet for undocumented component parameters in the JSON documentation set.
/// <para>
/// <see cref="ComponentDocumentationFreshnessTests"/> proves every documented parameter exists and
/// every live parameter is documented — it says nothing about whether the <c>description</c> it
/// carries is empty. A parameter with a blank description documents nothing: MCP clients and the
/// package docs surface an entry that still leaves the consumer guessing. The backlog is large
/// enough that fixing it in one pass is not this step — what must not happen is the count GROWING.
/// </para>
/// <para>
/// The live fact counts <c>parameters[].description</c> entries that are missing, null or
/// whitespace across <c>JsonDocumentation/**/*.json</c> and asserts the total stays at or below
/// <see cref="MaxUndescribedParameters"/> — a constant measured on the day this ratchet was added
/// (882 of 4453 parameters). Filling in descriptions can only shrink the count: when it does,
/// LOWER the constant — never raise it.
/// </para>
/// </summary>
public sealed class UndescribedParametersRatchetTests
{
    /// <summary>Undescribed <c>parameters[].description</c> entries measured on ratchet day.</summary>
    private const int MaxUndescribedParameters = 882;

    private static string JsonDocumentationDir =>
        Path.Combine(ThemeCss.RepositoryRoot().FullName, "JsonDocumentation");

    [Fact]
    public void UndescribedParameters_DoNotGrow()
    {
        var files = Directory.GetFiles(JsonDocumentationDir, "*.json", SearchOption.AllDirectories);
        files.Should().NotBeEmpty(
            "the ratchet counts parameters across JsonDocumentation/**/*.json — an empty set " +
            "means the guard is comparing nothing, which is exactly the silent hole it exists to close");

        var total = CountParameters(files);
        total.Should().BeGreaterThan(4000,
            "the denominator must prove the scan is still looking at the whole parameter surface — " +
            "a collapsed count would make the ceiling meaningless (4453 parameters at ratchet time)");

        var undescribed = CountUndescribed(files);
        AssertAtMost(undescribed, MaxUndescribedParameters);
    }

    [Fact]
    public void UndescribedParameters_OverCeiling_Fails()
    {
        // Mutation proof over the SAME counting and ceiling logic the live fact runs: a synthetic
        // JSON file with one blank description must count as undescribed, and checking that count
        // against a ceiling of 0 must fail exactly the way the live guard fails over its limit.
        var dir = Path.Combine(Path.GetTempPath(), $"tempo-ratchet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "Synthetic.json");
            File.WriteAllText(file, """
                { "parameters": [ { "name": "P", "type": "string", "description": "" } ] }
                """);

            var undescribed = CountUndescribed([file]);
            undescribed.Should().Be(1,
                "a parameter whose description is whitespace must count — otherwise the live " +
                "ratchet measures nothing and its ceiling is decorative");

            var check = () => AssertAtMost(undescribed, 0);
            check.Should().Throw<Exception>(
                "one undescribed parameter over a zero ceiling must fail — a ceiling that accepts " +
                "an over-limit count would let the documentation backlog grow unnoticed");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Counts <c>parameters[].description</c> entries that are missing, null or whitespace.</summary>
    internal static int CountUndescribed(IEnumerable<string> jsonFiles) =>
        Count(jsonFiles, undescribedOnly: true);

    /// <summary>Counts every <c>parameters[]</c> entry — the denominator the ceiling is judged against.</summary>
    internal static int CountParameters(IEnumerable<string> jsonFiles) =>
        Count(jsonFiles, undescribedOnly: false);

    private static int Count(IEnumerable<string> jsonFiles, bool undescribedOnly)
    {
        var count = 0;
        foreach (var file in jsonFiles)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            if (!doc.RootElement.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var parameter in parameters.EnumerateArray())
            {
                var described = parameter.TryGetProperty("description", out var description)
                    && description.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(description.GetString());
                if (!undescribedOnly || !described)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static void AssertAtMost(int undescribed, int ceiling) =>
        undescribed.Should().BeLessThanOrEqualTo(ceiling,
            "the undocumented-parameter count may only shrink — when descriptions are filled in, " +
            "LOWER MaxUndescribedParameters to lock the progress in; raising it defeats the ratchet");
}
