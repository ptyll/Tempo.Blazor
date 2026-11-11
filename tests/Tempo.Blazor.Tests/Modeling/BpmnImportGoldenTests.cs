using System.Text.Json;
using FluentAssertions;
using Tempo.Blazor.Modeling.BpmnXml;

namespace Tempo.Blazor.Tests.Modeling;

/// <summary>
/// Golden tests over the BPMN corpus: the normalized import result (models, losses, warnings)
/// must match the committed JSON exactly — the converter is deterministic (same input = same
/// output). Regenerate intentionally with <c>TEMPO_REGENERATE_BPMN_IMPORT_GOLDEN=1</c>.
/// </summary>
public sealed class BpmnImportGoldenTests
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] GoldenFixtures = BpmnImportGoldenFixtures.All;

    public static TheoryData<string> FixtureNames()
    {
        var data = new TheoryData<string>();
        foreach (var fixture in GoldenFixtures)
        {
            data.Add(fixture);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void Import_matches_the_committed_golden_file(string fixture)
    {
        var result = new BpmnXmlImporter().Import(BpmnCorpusPaths.ReadFixture(fixture));
        result.HasErrors.Should().BeFalse(result.ErrorMessage ?? $"import of {fixture} failed");

        var goldenPath = Path.Combine(BpmnCorpusPaths.ExpectedDirectory, fixture.Replace(".bpmn", ".import.json", StringComparison.Ordinal));
        // Normalize line endings both on the produced JSON and on the committed file so the
        // goldens are stable across checkouts (core.autocrlf) and platforms.
        var actual = JsonSerializer.Serialize(
            new
            {
                result.Models,
                result.Losses,
                result.Warnings,
                result.HasErrors,
                result.ErrorMessage,
            },
            JsonOptions).ReplaceLineEndings("\n");

        if (Environment.GetEnvironmentVariable("TEMPO_REGENERATE_BPMN_IMPORT_GOLDEN") == "1")
        {
            Directory.CreateDirectory(BpmnCorpusPaths.ExpectedDirectory);
            File.WriteAllText(goldenPath, actual);
        }

        File.Exists(goldenPath).Should().BeTrue(
            $"golden file '{Path.GetFileName(goldenPath)}' must be committed — regenerate with TEMPO_REGENERATE_BPMN_IMPORT_GOLDEN=1");
        var expected = File.ReadAllText(goldenPath).ReplaceLineEndings("\n");
        actual.Should().Be(expected,
            $"the import of {fixture} must be deterministic; regenerate the golden intentionally with TEMPO_REGENERATE_BPMN_IMPORT_GOLDEN=1");
    }

    [Fact]
    public void Golden_files_exist_for_every_corpus_fixture()
    {
        Directory.Exists(BpmnCorpusPaths.ExpectedDirectory).Should().BeTrue();
        foreach (var fixture in GoldenFixtures)
        {
            File.Exists(Path.Combine(BpmnCorpusPaths.ExpectedDirectory,
                    fixture.Replace(".bpmn", ".import.json", StringComparison.Ordinal)))
                .Should().BeTrue($"missing golden file for {fixture}");
        }
    }
}
